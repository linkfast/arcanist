namespace Arcanist.Core.Hosting;

/// <summary>Baseline headers for a server that may be exposed to the internet.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "SAMEORIGIN";
        headers["Referrer-Policy"] = "same-origin";
        return next(context);
    }
}
