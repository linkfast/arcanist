using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Libraries;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Where uploads go when the client does not choose a library.</summary>
/// <param name="DefaultLibraryId">The library uploads without a library go to (when the user can upload to it); null
/// leaves the choice to each upload.</param>
/// <param name="DefaultLibraryName">Its name, for clients to show what «automatic» means.</param>
public sealed record UploadSettings(Guid? DefaultLibraryId, string? DefaultLibraryName);

public sealed record SetUploadSettingsInput(Guid? DefaultLibraryId);

/// <summary>Where the user's uploads go when they name no library.</summary>
/// <param name="LibraryId">The library the user chose (null: the server's default).</param>
/// <param name="EffectiveLibraryId">Where an upload without a library goes now: the user's choice while they can still
/// upload to it, else the server's default when they can; null means such an upload answers <c>upload_library_required</c>.</param>
public sealed record MyUploadSettings(Guid? LibraryId, string? LibraryName, Guid? EffectiveLibraryId, string? EffectiveLibraryName);

/// <param name="LibraryId">A library the user can upload to; null goes back to the server's default.</param>
public sealed record SetMyUploadSettingsInput(Guid? LibraryId);

/// <summary>The stored choice (<see cref="SettingKeys.Uploads"/>).</summary>
internal sealed record StoredUploadSettings(Guid? DefaultLibraryId);

/// <summary>
/// The server's rule for uploads that do not name a library, and the library an upload goes to: the one of the wish
/// it fulfils when that wish says, else the server's default.
/// </summary>
public sealed class UploadDestinations(ArcanistDbContext db, AccessGuard guard, SettingsStore settings)
{
    /// <summary>Any signed-in user: clients show it as «automatic».</summary>
    public async Task<UploadSettings> GetAsync(CancellationToken cancellationToken)
    {
        guard.Authenticated();
        return await ToModelAsync(await StoredAsync(cancellationToken), cancellationToken);
    }

    /// <summary>Requires MANAGE_LIBRARIES; the library must have a folder Arcanist writes to.</summary>
    public async Task<UploadSettings> SetAsync(SetUploadSettingsInput input, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.ManageLibraries);
        if (input.DefaultLibraryId is { } id)
        {
            if (VirtualLibraries.IsVirtual(id) || !access.CanAccessLibrary(id) || !await WritableAsync(id, cancellationToken))
                throw ArcanistException.Field("defaultLibraryId", ErrorCodes.NotFound);
        }

        var stored = new StoredUploadSettings(input.DefaultLibraryId);
        await settings.SetAsync(SettingKeys.Uploads, stored, cancellationToken);
        return await ToModelAsync(stored, cancellationToken);
    }

    /// <summary>The user's own choice for uploads that name no library, and where they go now.</summary>
    public async Task<MyUploadSettings> MyAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        return await MyModelAsync(access, await MineAsync(access.UserId, cancellationToken), cancellationToken);
    }

    /// <summary>Chooses the library the user's uploads go to when they name none (one they can upload to), or none.</summary>
    public async Task<MyUploadSettings> SetMyAsync(SetMyUploadSettingsInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (input.LibraryId is { } id && (!access.Has(Permissions.Upload) || !await CanTargetAsync(access, id, cancellationToken)))
            throw ArcanistException.Field("libraryId", ErrorCodes.NotFound);

        var stored = new StoredUploadSettings(input.LibraryId);
        if (input.LibraryId is null)
            await settings.DeleteAsync(SettingKeys.UserUploads(access.UserId), cancellationToken);
        else
            await settings.SetAsync(SettingKeys.UserUploads(access.UserId), stored, cancellationToken);
        return await MyModelAsync(access, stored, cancellationToken);
    }

    private Task<StoredUploadSettings> MineAsync(Guid userId, CancellationToken cancellationToken) =>
        settings.GetAsync(SettingKeys.UserUploads(userId), new StoredUploadSettings(null), cancellationToken);

    private async Task<MyUploadSettings> MyModelAsync(UserAccess access, StoredUploadSettings mine, CancellationToken cancellationToken)
    {
        var effective = await EffectiveAsync(access, cancellationToken);
        var chosenId = mine.DefaultLibraryId ?? Guid.Empty;
        var names = await db.Libraries.AsNoTracking().Where(l => l.Id == chosenId || l.Id == effective)
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        var chosen = mine.DefaultLibraryId is { } id && names.ContainsKey(id) ? id : (Guid?)null;
        return new MyUploadSettings(chosen, chosen is { } c ? names[c] : null, effective, effective is { } e ? names.GetValueOrDefault(e) : null);
    }

    /// <summary>Where an upload of <paramref name="access"/> naming no library goes now, or null.</summary>
    internal async Task<Guid?> EffectiveAsync(UserAccess access, CancellationToken cancellationToken)
    {
        if (!access.Has(Permissions.Upload))
            return null;
        if ((await MineAsync(access.UserId, cancellationToken)).DefaultLibraryId is { } mine && await CanTargetAsync(access, mine, cancellationToken))
            return mine;
        return (await StoredAsync(cancellationToken)).DefaultLibraryId is { } fallback && access.CanUploadTo(fallback)
                                                                        && await CanTargetAsync(access, fallback, cancellationToken)
            ? fallback
            : null;
    }

    /// <summary>
    /// The library an upload without one goes to for <paramref name="access"/>: the target of their wish
    /// <paramref name="wishId"/> when it has one, else the user's own choice, else the server's default.
    /// <c>upload_library_required</c> when none applies.
    /// </summary>
    internal async Task<Guid> ResolveAsync(UserAccess access, Guid? wishId, CancellationToken cancellationToken)
    {
        if (wishId is { } wish)
        {
            var target = await db.WishListItems.AsNoTracking().Where(w => w.Id == wish && w.UserId == access.UserId)
                             .Select(w => new { w.TargetLibraryId }).SingleOrDefaultAsync(cancellationToken)
                         ?? throw ArcanistException.Field("wishId", ErrorCodes.NotFound);
            if (target.TargetLibraryId is { } chosen)
                return chosen;
        }

        if ((await MineAsync(access.UserId, cancellationToken)).DefaultLibraryId is { } mine && await CanTargetAsync(access, mine, cancellationToken))
            return mine;

        return (await StoredAsync(cancellationToken)).DefaultLibraryId is { } fallback && access.CanUploadTo(fallback)
            ? fallback
            : throw ArcanistException.Field("libraryId", ErrorCodes.UploadLibraryRequired);
    }

    /// <summary>Whether <paramref name="access"/> may send uploads to <paramref name="libraryId"/> (a wish's target).</summary>
    internal async Task<bool> CanTargetAsync(UserAccess access, Guid libraryId, CancellationToken cancellationToken) =>
        !VirtualLibraries.IsVirtual(libraryId) && access.CanAccessLibrary(libraryId) && access.CanUploadTo(libraryId)
        && await WritableAsync(libraryId, cancellationToken);

    private Task<bool> WritableAsync(Guid libraryId, CancellationToken cancellationToken) =>
        db.LibrarySources.AnyAsync(s => s.LibraryId == libraryId && (s.Kind == LibrarySourceKind.Local || s.Kind == LibrarySourceKind.S3)
                                        && s.Library.OwnerUserId == null, cancellationToken);

    private Task<StoredUploadSettings> StoredAsync(CancellationToken cancellationToken) =>
        settings.GetAsync(SettingKeys.Uploads, new StoredUploadSettings(null), cancellationToken);

    private async Task<UploadSettings> ToModelAsync(StoredUploadSettings stored, CancellationToken cancellationToken)
    {
        var name = stored.DefaultLibraryId is { } id
            ? await db.Libraries.AsNoTracking().Where(l => l.Id == id).Select(l => l.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        // A library deleted since leaves no default.
        return name is null ? new UploadSettings(null, null) : new UploadSettings(stored.DefaultLibraryId, name);
    }
}
