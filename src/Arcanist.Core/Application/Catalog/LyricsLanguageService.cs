namespace Arcanist.Core.Application.Catalog;

/// <summary>Translations of a song's lyrics, for players: those who can see the song read and ask for them.</summary>
public sealed class LyricsLanguageService(CatalogService catalog, LyricsLanguages languages, AccessGuard guard)
{
    /// <summary>The song's lyrics in <paramref name="language"/>: ready, on their way, or why not; asked for when <paramref name="request"/>.</summary>
    public async Task<LyricsTranslationResult> TranslationAsync(Guid trackId, string language, bool request, CancellationToken cancellationToken)
    {
        guard.Authenticated();
        var code = LyricsLanguages.Language(language) ?? throw ArcanistException.Field("language", ErrorCodes.InvalidFormat);
        // Visibility, and the lines as players show them (not_found without lyrics).
        var lyrics = await catalog.LyricsAsync(trackId, cancellationToken);
        if (lyrics.Instrumental)
            return new LyricsTranslationResult(trackId, code, LyricsTranslationStatus.Unavailable, lyrics.Language, null, null, null);
        return await languages.TranslationAsync(trackId, LyricsLanguages.LinesOf(lyrics.Plain, lyrics.Synced), lyrics.Language, code, request,
            cancellationToken);
    }

    public IReadOnlyList<string> Languages()
    {
        guard.Authenticated();
        return languages.TranslationLanguages;
    }

    public IObservable<LyricsTranslationReady> WatchReady(Guid trackId)
    {
        guard.Authenticated();
        return languages.WatchReady(trackId);
    }
}
