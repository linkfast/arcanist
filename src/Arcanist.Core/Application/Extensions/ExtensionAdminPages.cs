using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Extensions;
using Arcanist.Extensibility;

namespace Arcanist.Core.Application.Extensions;

/// <summary>Values of an extension's settings page, by field key.</summary>
public sealed record ExtensionAdminValues(Dictionary<string, JsonElement>? Values);

/// <summary>
/// The settings pages of installed extensions (<see cref="IExtensionAdminPage"/>, keyed by the extension's id): the
/// admin panel draws them from what they describe. Administrators only; saving and actions need a recent
/// authentication. A failing page answers <c>extension_failed</c> instead of taking the request down.
/// </summary>
public sealed class ExtensionAdminPages(
    AccessGuard guard,
    ExtensionHost host,
    SettingsStore settings,
    IHttpContextAccessor http,
    IServiceProvider services,
    ILogger<ExtensionAdminPages> logger)
{
    public async Task<AdminPage> GetAsync(string id, string? language, CancellationToken cancellationToken)
    {
        var access = guard.Admin();
        var page = Resolve(id);
        var context = await ContextAsync(access.UserId, language, cancellationToken);
        return await RunGuardedAsync(id, () => page.GetAsync(context, cancellationToken));
    }

    public async Task<AdminPageResult> SaveAsync(string id, ExtensionAdminValues input, string? language, CancellationToken cancellationToken)
    {
        var access = guard.Admin();
        guard.RecentAuthentication();
        var page = Resolve(id);
        var context = await ContextAsync(access.UserId, language, cancellationToken);
        return await RunGuardedAsync(id, () => page.SaveAsync(context, input.Values ?? [], cancellationToken));
    }

    public async Task<AdminPageResult> RunAsync(string id, string action, ExtensionAdminValues input, string? language, CancellationToken cancellationToken)
    {
        var access = guard.Admin();
        guard.RecentAuthentication();
        var page = Resolve(id);
        var context = await ContextAsync(access.UserId, language, cancellationToken);
        return await RunGuardedAsync(id, () => page.RunAsync(context, action, input.Values ?? [], cancellationToken));
    }

    /// <summary>The page of an extension that runs now and has one.</summary>
    private IExtensionAdminPage Resolve(string id)
    {
        if (host.Find(id) is not { Status: ExtensionStatus.Running, Manifest.AdminPage: not null } || !host.IsEnabled(id))
            throw new ArcanistException.NotFound();
        return services.GetKeyedService<IExtensionAdminPage>(id) ?? throw new ArcanistException.NotFound();
    }

    private async Task<AdminPageContext> ContextAsync(Guid user, string? language, CancellationToken cancellationToken)
    {
        var languages = await RequestLanguages.ResolveAsync(language, http.HttpContext, settings, cancellationToken);
        var first = languages.Select(l => l.Split('-')[0].ToLowerInvariant()).FirstOrDefault(l => l.Length > 0) ?? "en";
        return new AdminPageContext(first, user, services);
    }

    private async Task<T> RunGuardedAsync<T>(string id, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception e) when (e is not ArcanistException and not OperationCanceledException)
        {
            logger.LogError(e, "The settings page of the extension {Id} failed", id);
            throw new ArcanistException.Conflict(ErrorCodes.ExtensionFailed);
        }
    }
}
