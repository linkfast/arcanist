using System.Collections.Concurrent;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Libraries;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum LibraryMovePhase
{
    /// <summary>Copying the files to the new folder; the library still reads the old one.</summary>
    Copying,

    /// <summary>Everything is copied: the library now reads the new folder.</summary>
    Switching,

    /// <summary>Deleting the files from the old folder (unless they are kept).</summary>
    Cleaning,

    Completed,

    /// <summary>Stopped; the library still reads the old folder, or the new one if it got that far (see the error).</summary>
    Failed,
}

/// <summary>Moving a library's folder to another place on the server.</summary>
/// <param name="From">The old folder.</param>
/// <param name="To">The new folder.</param>
/// <param name="KeepOriginals">Only copy: the old folder keeps its files.</param>
/// <param name="ErrorCode">Why it failed: <c>storage_conflict</c> (a different file with the same name in the new folder),
/// <c>write_failed</c>, <c>access_denied</c>, <c>interrupted</c>…; or, completed, <c>cleanup_incomplete</c> when some old
/// files could not be deleted.</param>
public sealed record LibrarySourceMove(
    Guid SourceId,
    Guid LibraryId,
    string From,
    string To,
    bool KeepOriginals,
    LibraryMovePhase Phase,
    int FilesDone,
    int FilesTotal,
    long BytesDone,
    long BytesTotal,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? ErrorCode);

/// <param name="Destination">An existing folder on the server, outside the current one; it may hold files from a move
/// that was interrupted (the same files are not copied again), never different files with the same names.</param>
/// <param name="KeepOriginals">Copy only, leaving the old folder as it is; by default the old files are deleted
/// once everything is copied.</param>
public sealed record MoveLibrarySourceInput(string Destination, bool KeepOriginals = false);

/// <summary>
/// Moves library folders on this server: copies every file (the trash and hidden files too) to the new folder with its
/// modification time, then points the library at it, and last deletes the old files unless they are kept. The library
/// is locked meanwhile (no scans, organizing or imports), its catalog keeps its ids (paths are relative), and nothing is
/// overwritten. Moves live in memory: a restart in the middle leaves the library on the old folder, and moving again
/// skips what was already copied.
/// </summary>
public sealed class LibraryMoves(
    IServiceScopeFactory scopes,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    LibraryWatchSignal watches,
    DatabaseMaintenance maintenance,
    TimeProvider time,
    IHostApplicationLifetime lifetime,
    ILogger<LibraryMoves> logger)
{
    private readonly ConcurrentDictionary<Guid, LibrarySourceMove> _moves = new();

    public IReadOnlyList<LibrarySourceMove> List() => _moves.Values.OrderByDescending(m => m.StartedAt).ToList();

    public bool IsMoving(Guid libraryId) =>
        _moves.Values.Any(m => m.LibraryId == libraryId && m.Phase is LibraryMovePhase.Copying or LibraryMovePhase.Switching or LibraryMovePhase.Cleaning);

    internal LibrarySourceMove Start(Data.Entities.LibrarySource source, string destination, bool keepOriginals, Guid userId)
    {
        var move = new LibrarySourceMove(source.Id, source.LibraryId, source.Location, destination, keepOriginals, LibraryMovePhase.Copying,
            0, 0, 0, 0, Now(), null, null);
        _moves[source.Id] = move;
        _ = Task.Run(() => RunAsync(move, userId));
        return move;
    }

    private async Task RunAsync(LibrarySourceMove move, Guid userId)
    {
        var cancellationToken = lifetime.ApplicationStopping;
        var storage = (LocalStorage)storages.For(LibrarySourceKind.Local);
        var switched = false;
        try
        {
            using (await locks.AcquireForWritingAsync(move.LibraryId, cancellationToken))
            {
                var files = storage.AllFiles(move.From).ToList();
                Update(move.SourceId, m => m with { FilesTotal = files.Count, BytesTotal = files.Sum(f => f.Size) });
                logger.LogInformation("Moving library folder {From} to {To}: {Count} files", move.From, move.To, files.Count);

                foreach (var (path, size) in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await storage.CopyFileAsync(move.From, move.To, path, cancellationToken);
                    Update(move.SourceId, m => m with { FilesDone = m.FilesDone + 1, BytesDone = m.BytesDone + size });
                }

                if (files.Any(f => !storage.IsCopied(move.From, move.To, f.RelativePath)))
                    throw new IOException("A copied file does not match its original.");

                Update(move.SourceId, m => m with { Phase = LibraryMovePhase.Switching });
                using (await maintenance.WorkAsync(cancellationToken))
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
                    await using (db)
                    {
                        await db.LibrarySources.Where(s => s.Id == move.SourceId)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Location, move.To), cancellationToken);
                    }
                }

                switched = true;
                watches.Refresh();

                var failures = 0;
                if (!move.KeepOriginals)
                {
                    Update(move.SourceId, m => m with { Phase = LibraryMovePhase.Cleaning });
                    foreach (var (path, _) in files)
                    {
                        try
                        {
                            storage.DeleteMoved(move.From, path);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            failures++;
                            logger.LogWarning("Could not delete {Path} from the old folder {From}: {Message}", path, move.From, e.Message);
                        }
                    }
                }

                Update(move.SourceId, m => m with
                {
                    Phase = LibraryMovePhase.Completed,
                    FinishedAt = Now(),
                    ErrorCode = failures > 0 ? ErrorCodes.CleanupIncomplete : null,
                });
                logger.LogInformation("Library folder moved to {To}", move.To);
            }

            // Nothing should have changed for the catalog, but a scan confirms it.
            using (await maintenance.WorkAsync(CancellationToken.None))
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ScanCoordinator>()
                    .EnqueueAsync(move.LibraryId, ScanKind.Incremental, userId, CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            var code = e switch
            {
                OperationCanceledException => ErrorCodes.Interrupted,
                StorageConflictException => ErrorCodes.StorageConflict,
                UnauthorizedAccessException => ErrorCodes.AccessDenied,
                _ => ErrorCodes.WriteFailed,
            };
            if (e is not OperationCanceledException)
                logger.LogError(e, "Moving library folder {From} to {To} failed", move.From, move.To);
            Update(move.SourceId, m => m with
            {
                Phase = switched ? LibraryMovePhase.Completed : LibraryMovePhase.Failed,
                FinishedAt = Now(),
                ErrorCode = switched ? ErrorCodes.CleanupIncomplete : code,
            });
        }
    }

    private void Update(Guid sourceId, Func<LibrarySourceMove, LibrarySourceMove> change) =>
        _moves.AddOrUpdate(sourceId, _ => throw new InvalidOperationException(), (_, m) => change(m));

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
