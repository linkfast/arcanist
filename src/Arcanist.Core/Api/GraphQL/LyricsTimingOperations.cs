using System.ComponentModel;
using Arcanist.Core.Application.Catalog;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("lyricsTiming")]
    [Description("How many songs have lyrics, how many of them are timed (and how many timed on this server), how many plain lyrics could be timed here, and whether the official extension that does it (Lyrics Sync) is installed and running. Administrators only.")]
    public static Task<LyricsTimingStatus> LyricsTiming([FromServices] LyricsTimingService service, CancellationToken cancellationToken) =>
        service.StatusAsync(cancellationToken);
}
