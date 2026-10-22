using Arcanist.Core.Capabilities;
using Arcanist.Core.Cloud;
using Arcanist.Core.Extensions;
using Arcanist.Core.Hosting;

namespace Arcanist.Core.Application.Server;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>What the server is, as its mark says under ARCANIST.</summary>
public enum ServerEdition
{
    /// <summary>Only open source components are in use.</summary>
    OpenSource,

    /// <summary>Some closed source component is in use (a web player such as Ultimate Web Player, an extension, a tool).</summary>
    Standard,

    /// <summary>Ultimate Cloud's paid plan, with its sync component (ultimate-audio) installed and running. A free account does not make it Ultimate.</summary>
    Ultimate,
}

public enum ClosedComponentKind
{
    Extension,
    Player,
    /// <summary>A program the server runs, such as ultimate-audio.</summary>
    Tool,
}

/// <summary>A closed source component installed here.</summary>
/// <param name="Active">In use now: an extension switched on and running, a player served, a tool installed.</param>
public sealed record ClosedComponent(ClosedComponentKind Kind, string Id, string Name, string? License, bool Active);

/// <param name="Enabled">Only open source components may be installed and run.</param>
/// <param name="SetByEnvironment">Set outside the panel (environment variables): the panel cannot change it.</param>
/// <param name="Components">The closed source components installed (none run while <see cref="Enabled"/>).</param>
public sealed record OpenSourceMode(bool Enabled, bool SetByEnvironment, ServerEdition Edition, IReadOnlyList<ClosedComponent> Components);

public sealed record SetOpenSourceModeInput(bool Enabled);

/// <summary>The server's edition, worked out from what is unlocked and installed. Cheap: read on every serverInfo.</summary>
public sealed class EditionResolver(ExtensionHost extensions, FrontendCatalog players, IUltimateAudio ultimateAudio, ICloudLinkState cloud,
    IConfiguration configuration)
{
    public ServerEdition Current =>
        // Ultimate: the paid plan (a free account unlocks nothing of it) with the sync component running.
        cloud.Tier == CloudTier.Paid && ultimateAudio.IsInstalled && !OpenSource.IsOn(configuration) ? ServerEdition.Ultimate
        : Components().Any(c => c.Active) ? ServerEdition.Standard
        : ServerEdition.OpenSource;

    public IReadOnlyList<ClosedComponent> Components()
    {
        var list = new List<ClosedComponent>();
        foreach (var extension in extensions.Extensions.Where(e => extensions.IsClosed(e.Manifest.Id)))
            list.Add(new ClosedComponent(ClosedComponentKind.Extension, extension.Manifest.Id, extension.Manifest.Name, extension.Manifest.License,
                extensions.IsEnabled(extension.Manifest.Id) && extension.Status == ExtensionStatus.Running));
        foreach (var player in players.List().Where(p => !OpenSource.IsOpenLicense(p.Manifest.License)))
            list.Add(new ClosedComponent(ClosedComponentKind.Player, player.Manifest.Id, player.Manifest.Name, player.Manifest.License,
                players.IsUsable(player)));
        if (ultimateAudio.IsInstalled)
            list.Add(new ClosedComponent(ClosedComponentKind.Tool, "ultimate-audio", "ultimate-audio", null, !OpenSource.IsOn(configuration)));
        return list;
    }
}

/// <summary>The 100% open source mode: administrators turn it on or off; while on, closed source components do not run.</summary>
public sealed class OpenSourceService(
    AccessGuard guard,
    ISettingsFileWriter settings,
    IConfiguration configuration,
    ExtensionHost extensions,
    EditionResolver edition)
{
    public Task<OpenSourceMode> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        return Task.FromResult(Status());
    }

    public async Task<OpenSourceMode> SetAsync(bool enabled, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        if (settings.IsSetOutsideSettingsFile(OpenSource.ModeKey))
            throw new ArcanistException.Conflict(ErrorCodes.SetByEnvironment);

        await settings.SetAsync(new Dictionary<string, object?> { [OpenSource.ModeKey] = enabled }, cancellationToken);
        // Closed source extensions stop (or may start again after a restart): tell whoever listens.
        foreach (var extension in extensions.Extensions.Where(e => extensions.IsClosed(e.Manifest.Id)))
            extensions.OnEnabledChanged(extension.Manifest.Id);
        return Status();
    }

    private OpenSourceMode Status() =>
        new(OpenSource.IsOn(configuration), settings.IsSetOutsideSettingsFile(OpenSource.ModeKey), edition.Current, edition.Components());
}
