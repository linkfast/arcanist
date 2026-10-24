using System.ComponentModel;
using Arcanist.Core.Application.Dlna;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("dlna")]
    [Description("The DLNA media server: whether it is on, what it publishes and the addresses devices find. Administrators only.")]
    public static Task<DlnaStatus> Dlna([FromServices] DlnaService dlna, CancellationToken cancellationToken) => dlna.StatusAsync(cancellationToken);

    [Name("myDlnaStarTracks")]
    [Description("Whether the caller's StarTracks are shown to the devices of the local network (DLNA), and whether DLNA is on.")]
    public static Task<MyDlnaStarTracks> MyDlnaStarTracks([FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.MyStarTracksAsync(cancellationToken);

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

    [Name("setMyDlnaStarTracks")]
    [Description("Shows (true) or stops showing (false) the caller's StarTracks to the devices of the local network, as a folder of DLNA's StarTracks: only songs of the published libraries, to anyone there without signing in.")]
    public static Task<MyDlnaStarTracks> SetMyDlnaStarTracks(bool shown, [FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.SetMyStarTracksAsync(shown, cancellationToken);

    [Name("setDlnaRecordPlays")]
    [Description("Pauses (false) or resumes (true) counting the plays of \"play to\" in users' statistics; renderers keep playing. Administrators only.")]
    public static Task<DlnaStatus> SetDlnaRecordPlays(bool record, [FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.SetRecordPlaysAsync(record, cancellationToken);

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

    [Name("editDlnaQueue")]
    [Description("Changes the queue playing on a renderer: append or insert tracks after the one playing, remove, move or jump to an entry. The track playing goes on. Only who started the queue (or an administrator) while it plays.")]
    public static Task<DlnaRendererState> EditDlnaQueue(EditDlnaQueueInput input, [FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.EditQueueAsync(input, cancellationToken);

    [Name("searchDlnaRenderers")]
    [Description("Asks the local network for renderers now and returns the list a few seconds later.")]
    public static Task<IReadOnlyList<DlnaRenderer>> SearchDlnaRenderers([FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.SearchRenderersAsync(cancellationToken);

    [Name("setDlnaRendererVolume")]
    [Description("Sets a renderer's volume, 0–100.")]
    public static Task<DlnaRendererState> SetDlnaRendererVolume(string rendererId, int volume, [FromServices] DlnaService dlna, CancellationToken cancellationToken) =>
        dlna.SetVolumeAsync(rendererId, volume, cancellationToken);
}
