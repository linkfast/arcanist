using System.ComponentModel;
using Arcanist.Core.Application.Dlna;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("dlna")]
    [Description("The DLNA media server: whether it is on, what it publishes and the addresses devices find. Administrators only.")]
    public static Task<DlnaStatus> Dlna([FromServices] DlnaService dlna, CancellationToken cancellationToken) => dlna.StatusAsync(cancellationToken);

    [Name("dlnaRenderers")]
    [Description("Devices on the local network Arcanist can play to (DLNA renderers); empty while DLNA is off.")]
    public static IReadOnlyList<DlnaRenderer> DlnaRenderers([FromServices] DlnaService dlna) => dlna.Renderers();

    [Name("dlnaRenderer")]
    [Description("What a renderer is doing: its state, position, volume and Arcanist's queue on it.")]
    public static Task<DlnaRendererState> DlnaRenderer(string id, [FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.RendererAsync(id, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("setDlnaSettings")]
    [Description("Sets the name devices show, the libraries published on the local network (to anyone there, without signing in) and the folders of the tree. Administrators only, with a recent authentication.")]
    public static Task<DlnaStatus> SetDlnaSettings(DlnaSettingsInput input, [FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.SetSettingsAsync(input, cancellationToken);

    [Name("addDlnaRenderer")]
    [Description("Adds a renderer by the URL of its UPnP description, for networks where it is not found by itself. Administrators only.")]
    public static Task<DlnaRenderer> AddDlnaRenderer(string url, [FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.AddRendererAsync(url, cancellationToken);

    [Name("playOnDlnaRenderer")]
    [Description("Plays tracks on a renderer, in the best format it plays (lossless first). The plays count for the caller.")]
    public static Task<DlnaRendererState> PlayOnDlnaRenderer(PlayOnDlnaRendererInput input, [FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.PlayAsync(input, cancellationToken);

    [Name("controlDlnaRenderer")]
    [Description("Plays, pauses, stops, skips or seeks on a renderer.")]
    public static Task<DlnaRendererState> ControlDlnaRenderer(ControlDlnaRendererInput input, [FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.ControlAsync(input, cancellationToken);

    [Name("setDlnaRendererVolume")]
    [Description("Sets a renderer's volume, 0–100.")]
    public static Task<DlnaRendererState> SetDlnaRendererVolume(string rendererId, int volume, [FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.SetVolumeAsync(rendererId, volume, cancellationToken);
}
