using System.ComponentModel;
using Arcanist.Core.Application.Catalog;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("lyricsTranslation")]
    [Description("A song's lyrics in another language (ISO 639-1, such as es; with the Lyrics Languages extension): READY with one line per line of trackLyrics (so their times apply), NOT_REQUESTED until someone asks for it (requestLyricsTranslation), QUEUED or WORKING while it is made (follow lyricsTranslationReady), FAILED (tried again an hour later), UNAVAILABLE (nothing translates into that language here) or SAME_LANGUAGE. Kept for everyone once made. Errors: not_found (no such song, or no lyrics).")]
    public static Task<LyricsTranslationResult> LyricsTranslation([Id] Guid trackId, string language, [FromServices] LyricsLanguageService service,
        CancellationToken cancellationToken) =>
        service.TranslationAsync(trackId, language, request: false, cancellationToken);

    [Name("lyricsTranslationLanguages")]
    [Description("The languages lyrics can be translated into on this server (ISO 639-1); empty without a translator (the Lyrics Languages extension, with a model or an LLM).")]
    public static IReadOnlyList<string> LyricsTranslationLanguages([FromServices] LyricsLanguageService service) => service.Languages();
}

public sealed partial class Mutation
{
    [Name("requestLyricsTranslation")]
    [Description("Asks for a song's lyrics in another language (ISO 639-1): READY at once when someone asked for them before, else QUEUED; lyricsTranslationReady says when it is done. Anyone who sees the song may ask. Errors: not_found (no such song, or no lyrics).")]
    public static Task<LyricsTranslationResult> RequestLyricsTranslation([Id] Guid trackId, string language, [FromServices] LyricsLanguageService service,
        CancellationToken cancellationToken) =>
        service.TranslationAsync(trackId, language, request: true, cancellationToken);
}

public sealed partial class Subscription
{
    [Name("lyricsTranslationReady")]
    [Description("A translation of the song's lyrics asked for is done (READY) or failed (FAILED): read lyricsTranslation again.")]
    public static IObservable<LyricsTranslationReady> LyricsTranslationReady([Id] Guid trackId, [FromServices] LyricsLanguageService service) =>
        service.WatchReady(trackId);
}
