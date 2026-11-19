using Microsoft.AspNetCore.Http;

namespace ArchiveExplorer.Web;

/// <summary>
/// Every website open in the browser can send requests to 127.0.0.1; DNS rebinding could even let one read the
/// answers. So: only requests addressed to a loopback name (plus --listen names) with our own port, and API/media
/// calls only from our own pages.
/// </summary>
public sealed class LocalOnly(RequestDelegate next, IReadOnlySet<string> allowedHosts)
{
    public static readonly string[] LoopbackNames = ["localhost", "127.0.0.1", "[::1]"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsAllowedHost(context.Request.Host, context.Connection.LocalPort))
        {
            context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
            return;
        }

        var path = context.Request.Path;
        if ((path.StartsWithSegments("/api") || path.StartsWithSegments("/media")) && IsCrossSite(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }

    private bool IsAllowedHost(HostString host, int localPort)
    {
        if (!host.HasValue || !allowedHosts.Contains(host.Host.ToLowerInvariant()))
        {
            return false;
        }
        return host.Port is null || localPort == 0 || host.Port == localPort;
    }

    private bool IsCrossSite(HttpRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0 && site is not ("same-origin" or "none"))
        {
            return true;
        }
        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
        {
            return false;
        }
        return !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || !IsAllowedHost(new HostString(uri.Authority), request.HttpContext.Connection.LocalPort);
    }
}
