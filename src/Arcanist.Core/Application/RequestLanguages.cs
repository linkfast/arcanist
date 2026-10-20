using Arcanist.Core.Data;

namespace Arcanist.Core.Application;

/// <summary>The languages to name things in for a request: the one asked for, the request's (Accept-Language), then the server's default.</summary>
public static class RequestLanguages
{
    public static async Task<List<string>> ResolveAsync(string? language, HttpContext? context, SettingsStore settings, CancellationToken cancellationToken)
    {
        var languages = new List<string>();
        if (!string.IsNullOrWhiteSpace(language))
            languages.Add(language.Trim());
        if (context?.Request.Headers.AcceptLanguage.ToString() is { Length: > 0 } accept)
            languages.AddRange(accept.Split(',').Select(part => part.Split(';')[0].Trim()).Where(tag => tag.Length > 0));
        if (await settings.GetAsync<string?>(SettingKeys.DefaultLanguage, null, cancellationToken) is { Length: > 0 } fallback)
            languages.Add(fallback);
        return languages;
    }
}
