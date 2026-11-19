using Microsoft.AspNetCore.Http;

namespace ArchiveExplorer.Web;

/// <summary>Strict headers on every response. Inline scripts of the bundled UI are allowed by hash only.</summary>
public sealed class SecurityHeaders(RequestDelegate next, UiFiles ui)
{
    public const string MediaPolicy = "default-src 'none'; img-src 'self'; media-src 'self'; style-src 'unsafe-inline'; sandbox";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        var policy = context.Request.Path.StartsWithSegments("/media")
            ? MediaPolicy
            : "default-src 'self'; " +
              $"script-src 'self'{ui.ScriptHashSources}; " +
              "style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self'; " +
              "connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        headers.ContentSecurityPolicy = policy;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers.XFrameOptions = "DENY";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }
        return next(context);
    }
}
