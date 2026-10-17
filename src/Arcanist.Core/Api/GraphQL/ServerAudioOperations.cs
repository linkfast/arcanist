using System.ComponentModel;
using Arcanist.Core.Application.ServerAudio;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("serverAudio")]
    [Description("Server audio: whether it plays, on which device, and the server's audio devices. Administrators only. The server plays through \"play to\" as the renderer \"server\".")]
    public static Task<ServerAudioStatus> ServerAudio([FromServices] ServerAudioService serverAudio, CancellationToken cancellationToken) =>
        serverAudio.StatusAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("setServerAudioSettings")]
    [Description("Chooses the device server audio plays on (the system's default when null) and the name it shows. Administrators only, with a recent authentication.")]
    public static Task<ServerAudioStatus> SetServerAudioSettings(ServerAudioSettingsInput input, [FromServices] ServerAudioService serverAudio, CancellationToken cancellationToken) =>
        serverAudio.SetSettingsAsync(input, cancellationToken);

    [Name("testServerAudio")]
    [Description("Plays a short tone on the server's device, to check it; refused while a queue plays. Administrators only.")]
    public static Task<bool> TestServerAudio([FromServices] ServerAudioService serverAudio, CancellationToken cancellationToken) =>
        serverAudio.TestAsync(cancellationToken);
}
