using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Organization;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Library = Arcanist.Core.Application.Libraries.Library;
using LibraryEntity = Arcanist.Core.Data.Entities.Library;
using OrganizeRunEntity = Arcanist.Core.Data.Entities.OrganizeRun;

namespace Arcanist.Core.Application.Organization;

/// <summary>
/// Moving a library's files to the paths its template gives them: settings, a preview, runs in the background and
/// undoing them. Requires MANAGE_LIBRARIES on a library the caller can access; moving files also requires a recent
/// authentication.
/// </summary>
public sealed class OrganizationService(
    ArcanistDbContext db,
    AccessGuard guard,
    LibraryOrganizer organizer,
    OrganizeQueue queue,
    ILibraryStorageResolver storages,
    LibraryWatchSignal watches,
    Libraries.CloudLibraries cloud,
    TimeProvider time)
{
    public const int PreviewMoves = 200;
    public const int MaxRuns = 20;

    public async Task<Library> UpdateAsync(Guid libraryId, UpdateLibraryOrganizationInput input, CancellationToken cancellationToken)
    {
        var library = await FindAsync(libraryId, cancellationToken);
        var errors = new ValidationErrors();
        string? template = null;
        if (!string.IsNullOrWhiteSpace(input.Template))
        {
            if (PathTemplate.TryParse(input.Template, out var parsed, out var error))
                template = parsed.Text == PathTemplate.Default ? null : parsed.Text;
            else
                errors.Add("template", error!);
        }

        if (input.AutoOrganize && !library.AutoOrganize && !await AllWritableAsync(library, cancellationToken))
            errors.Add("autoOrganize", ErrorCodes.ReadOnly);
        errors.ThrowIfAny();

        library.OrganizeTemplate = template;
        if (input.AutoOrganize && !library.AutoOrganize)
            library.AutoOrganizeSince = Now();
        var watchChanged = library.AutoOrganize != input.AutoOrganize;
        library.AutoOrganize = input.AutoOrganize;
        await db.SaveChangesAsync(cancellationToken);
        if (watchChanged)
            watches.Refresh();

        return library.ToModel(includeSources: true);
    }

    /// <summary>What organizing would move, with <paramref name="template"/> or the library's own.</summary>
    public async Task<OrganizePreview> PreviewAsync(Guid libraryId, string? template, CancellationToken cancellationToken)
    {
        var library = await FindAsync(libraryId, cancellationToken);
        var parsed = Template(template ?? library.OrganizeTemplate, "template");

        var plan = await organizer.PlanAsync(library.Id, parsed, addedSince: null, cancellationToken);
        var audio = plan.Moves.Where(m => !m.IsCompanion).ToList();
        return new OrganizePreview(
            parsed.Text,
            audio.Count,
            plan.InPlace,
            plan.Moves.Count - audio.Count,
            audio.Take(PreviewMoves).Select(m => new PlannedMove(m.SourceId, m.From, m.To)).ToList(),
            plan.UnavailableSources);
    }

    /// <summary>Starts organizing the library with its template, in the background.</summary>
    public async Task<OrganizeRun> StartAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var library = await FindAsync(libraryId, cancellationToken);
        guard.RecentAuthentication();
        await EnsureIdleAsync(library.Id, cancellationToken);
        if (!await AllWritableAsync(library, cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.ReadOnly);

        var run = new OrganizeRunEntity
        {
            LibraryId = library.Id,
            Kind = OrganizeRunKind.Manual,
            Status = OrganizeRunStatus.Running,
            Template = Template(library.OrganizeTemplate, "template").Text,
            RequestedByUserId = guard.Authenticated().UserId,
            StartedAt = Now(),
        };
        db.OrganizeRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        queue.Enqueue(run.Id, undo: false);
        return ToModel(run);
    }

    /// <summary>The library's latest runs, newest first.</summary>
    public async Task<IReadOnlyList<OrganizeRun>> RunsAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var library = await FindAsync(libraryId, cancellationToken);
        var runs = await db.OrganizeRuns.AsNoTracking()
            .Where(r => r.LibraryId == library.Id)
            .OrderByDescending(r => r.StartedAt)
            .Take(MaxRuns)
            .ToListAsync(cancellationToken);
        return runs.Select(ToModel).ToList();
    }

    /// <summary>Moves the files of a finished run back where they were, in the background.</summary>
    public async Task<OrganizeRun> UndoAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await db.OrganizeRuns.SingleOrDefaultAsync(r => r.Id == runId, cancellationToken) ?? throw new ArcanistException.NotFound();
        await FindAsync(run.LibraryId, cancellationToken);
        guard.RecentAuthentication();

        if (run.Status is not (OrganizeRunStatus.Completed or OrganizeRunStatus.Failed) || run.Moved == 0)
            throw new ArcanistException.Conflict(ErrorCodes.NotUndoable);
        await EnsureIdleAsync(run.LibraryId, cancellationToken);

        run.Status = OrganizeRunStatus.Undoing;
        await db.SaveChangesAsync(cancellationToken);
        queue.Enqueue(run.Id, undo: true);
        return ToModel(run);
    }

    private async Task EnsureIdleAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        if (await db.OrganizeRuns.AnyAsync(r => r.LibraryId == libraryId && (r.Status == OrganizeRunStatus.Running || r.Status == OrganizeRunStatus.Undoing), cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.OrganizeRunning);
    }

    private async Task<bool> AllWritableAsync(LibraryEntity library, CancellationToken cancellationToken)
    {
        foreach (var source in library.Sources)
        {
            if (!await storages.For(source.Kind).CanWriteAsync(StorageLocation.From(source), cancellationToken))
                return false;
        }

        return true;
    }

    private static PathTemplate Template(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
            return PathTemplate.DefaultTemplate;
        return PathTemplate.TryParse(text, out var parsed, out var error) ? parsed : throw ArcanistException.Field(field, error!);
    }

    private async Task<LibraryEntity> FindAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.ManageLibraries);
        var library = await LibraryService.Visible(db, access).Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == libraryId, cancellationToken)
                      ?? throw new ArcanistException.NotFound();
        if (cloud.IsDisabled(library.Sources.Select(s => s.Kind)))
            throw new ArcanistException.Conflict(ErrorCodes.LibraryDisabled);
        return library;
    }

    private static OrganizeRun ToModel(OrganizeRunEntity run) => new(
        run.Id, run.LibraryId, run.Kind, run.Status, run.Template, run.StartedAt, run.FinishedAt, run.UndoneAt,
        run.Total, run.Moved, run.Failed, run.ErrorCode);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
