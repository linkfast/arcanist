using Arcanist.Core.Application.ServerAudio;

namespace Arcanist.Core.Api.Rest;

public static class ServerAudioEndpoints
{
    public static IEndpointRouteBuilder MapServerAudioEndpoints(this IEndpointRouteBuilder rest)
    {
        var audio = rest.MapGroup("/server-audio").WithTags("Server audio");

        audio.MapGet("", (ServerAudioService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithName("ServerAudio")
            .WithSummary("Server audio: whether it plays, on which device, and the server's audio devices. Administrators only. The server plays " +
                         "through \"play to\" as the renderer \"server\".");

        audio.MapPut("/settings", (ServerAudioSettingsInput input, ServerAudioService service, CancellationToken ct) => service.SetSettingsAsync(input, ct))
            .WithName("SetServerAudioSettings")
            .WithSummary("Chooses the device server audio plays on (the system's default when null) and the name it shows. Administrators only, " +
                         "with a recent authentication.");

        audio.MapPost("/test", (ServerAudioService service, CancellationToken ct) => service.TestAsync(ct))
            .WithName("TestServerAudio")
            .WithSummary("Plays a short tone on the server's device, to check it; refused while a queue plays. Administrators only.");

        return rest;
    }
}
