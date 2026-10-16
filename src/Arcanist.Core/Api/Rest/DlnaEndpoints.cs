using Arcanist.Core.Application.Dlna;

namespace Arcanist.Core.Api.Rest;

public sealed record AddDlnaRendererInput(string Url);

public sealed record SetDlnaRendererVolumeInput(int Volume);

public sealed record SetDlnaRecordPlaysInput(bool Record);

public static class DlnaEndpoints
{
    public static IEndpointRouteBuilder MapDlnaEndpoints(this IEndpointRouteBuilder rest)
    {
        var dlna = rest.MapGroup("/dlna").WithTags("DLNA");

        dlna.MapGet("", (DlnaService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithName("Dlna")
            .WithSummary("The DLNA media server: whether it is on, what it publishes and the addresses devices find. Administrators only.");

        dlna.MapPut("/settings", (DlnaSettingsInput input, DlnaService service, CancellationToken ct) => service.SetSettingsAsync(input, ct))
            .WithName("SetDlnaSettings")
            .WithSummary("Sets the name devices show, the libraries published on the local network (to anyone there, without signing in) " +
                         "and the folders of the tree. Administrators only, with a recent authentication.");

        dlna.MapPut("/record-plays", (SetDlnaRecordPlaysInput input, DlnaService service, CancellationToken ct) => service.SetRecordPlaysAsync(input.Record, ct))
            .WithName("SetDlnaRecordPlays")
            .WithSummary("Pauses (false) or resumes (true) counting the plays of \"play to\" in users' statistics; renderers keep playing. Administrators only.");

        dlna.MapGet("/renderers", (DlnaService service) => service.Renderers())
            .WithName("DlnaRenderers")
            .WithSummary("Devices on the local network Arcanist can play to (DLNA renderers); empty while DLNA is off.");

        dlna.MapPost("/renderers", (AddDlnaRendererInput input, DlnaService service, CancellationToken ct) => service.AddRendererAsync(input.Url, ct))
            .WithName("AddDlnaRenderer")
            .WithSummary("Adds a renderer by the URL of its UPnP description, for networks where it is not found by itself. Administrators only.");

        dlna.MapGet("/renderers/{id}", (string id, DlnaService service, CancellationToken ct) => service.RendererAsync(id, ct))
            .WithName("DlnaRenderer")
            .WithSummary("What a renderer is doing: its state, position, volume and Arcanist's queue on it.");

        dlna.MapPost("/play", (PlayOnDlnaRendererInput input, DlnaService service, CancellationToken ct) => service.PlayAsync(input, ct))
            .WithName("PlayOnDlnaRenderer")
            .WithSummary("Plays tracks on a renderer, in the best format it plays (lossless first). The plays count for the caller.");

        dlna.MapPost("/control", (ControlDlnaRendererInput input, DlnaService service, CancellationToken ct) => service.ControlAsync(input, ct))
            .WithName("ControlDlnaRenderer")
            .WithSummary("Plays, pauses, stops, skips or seeks on a renderer.");

        dlna.MapPut("/renderers/{id}/volume", (string id, SetDlnaRendererVolumeInput input, DlnaService service, CancellationToken ct) =>
                service.SetVolumeAsync(id, input.Volume, ct))
            .WithName("SetDlnaRendererVolume")
            .WithSummary("Sets a renderer's volume, 0–100.");

        return rest;
    }
}
