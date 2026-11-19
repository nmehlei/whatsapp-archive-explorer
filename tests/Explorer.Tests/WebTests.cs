using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArchiveExplorer.Demo;
using ArchiveExplorer.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;

namespace ArchiveExplorer.Tests;

public sealed class WebTests : IAsyncLifetime
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wae-web-").FullName;
    private string Archive => Path.Combine(_dir, "archive");
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        DemoArchiveWriter.Write(Archive, new DemoOptions());
        File.WriteAllText(Path.Combine(_dir, "secret.txt"), "outside the archive");
        Directory.CreateDirectory(Path.Combine(Archive, "WhatsApp", "Media", "Extra"));
        File.WriteAllText(Path.Combine(Archive, "WhatsApp", "Media", "Extra", "page.html"), "<script>alert(1)</script>");
        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(Path.Combine(Archive, "WhatsApp", "Media", "link.txt"), Path.Combine(_dir, "secret.txt"));
            Directory.CreateSymbolicLink(Path.Combine(Archive, "WhatsApp", "Media", "outside"), _dir);
        }

        _app = ExplorerHost.Create(new HostSettings(Archive, DbPath: null), builder => builder.WebHost.UseTestServer());
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Host = "localhost";
        await ExplorerHost.WaitUntilReadyAsync(_app, TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private async Task<JsonElement> Json(string url) =>
        JsonDocument.Parse(await _client.GetStringAsync(url)).RootElement;

    [Fact]
    public async Task HostGuard_RejectsForeignHost()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/chats");
        request.Headers.Host = "evil.example";

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("[::1]")]
    public async Task HostGuard_AllowsLoopbackNames(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        request.Headers.Host = host;

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RemoteGuard_RejectsNonLoopbackClientsWithoutListen()
    {
        var context = await _app.GetTestServer().SendAsync(c =>
        {
            c.Request.Method = "GET";
            c.Request.Path = "/api/status";
            c.Request.Host = new Microsoft.AspNetCore.Http.HostString("127.0.0.1");
            c.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
        });

        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task Host_IgnoresConfigurationThatCouldOpenPorts()
    {
        Environment.SetEnvironmentVariable("Kestrel__Endpoints__Lan__Url", "http://0.0.0.0:59123");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await using var app = ExplorerHost.Create(new HostSettings(Archive, DbPath: null), b => b.WebHost.UseTestServer());

            Assert.Null(app.Configuration["Kestrel:Endpoints:Lan:Url"]);
            Assert.Equal("Production", app.Environment.EnvironmentName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Kestrel__Endpoints__Lan__Url", null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
    }

    [Fact]
    public void Listen_AllowsLoopbackAndOwnAddresses()
    {
        var any = ExplorerHost.AllowedHostNames("0.0.0.0");
        var specific = ExplorerHost.AllowedHostNames("10.1.2.3");
        var none = ExplorerHost.AllowedHostNames(null);

        Assert.Contains("127.0.0.1", any);
        Assert.Contains(Environment.MachineName.ToLowerInvariant(), any);
        Assert.Contains("10.1.2.3", specific);
        Assert.Contains("localhost", specific);
        Assert.Equal(["127.0.0.1", "[::1]", "localhost"], none.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Api_RejectsCrossSiteFetch()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/chats");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var withOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/chats");
        withOrigin.Headers.Add("Origin", "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(withOrigin)).StatusCode);
    }

    [Fact]
    public async Task Headers_HaveStrictCspAndNoCors()
    {
        using var response = await _client.GetAsync("/api/chats");

        var csp = string.Join(';', response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Api_ChatsMessagesAndSearchRoundTrip()
    {
        var chats = await Json("/api/chats");
        Assert.Equal(12, chats.GetArrayLength());
        Assert.Equal(1, (await Json("/api/chats?q=muller")).GetArrayLength());

        var hits = await Json($"/api/search?q={Uri.EscapeDataString(DemoArchiveWriter.UniquePhrase)}");
        Assert.Equal(1, hits.GetProperty("total").GetInt32());
        var hit = hits.GetProperty("hits")[0];
        var chatId = hit.GetProperty("chatId").GetInt64();
        var messageId = hit.GetProperty("messageId").GetInt64();

        var page = await Json($"/api/chats/{chatId}/messages?around={messageId}&limit=10");
        Assert.Contains(page.GetProperty("items").EnumerateArray(), m => m.GetProperty("id").GetInt64() == messageId);
        var inChat = await Json($"/api/chats/{chatId}/search?q=lighthouse");
        Assert.Equal(messageId, inChat[0].GetInt64());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/chats/999999/messages")).StatusCode);
        var media = await Json($"/api/chats/{chatId}/media?limit=3");
        Assert.True(media.GetProperty("total").GetInt32() >= media.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Media_ServesFileWithRange()
    {
        var image = Directory.GetFiles(Path.Combine(Archive, "WhatsApp", "Media", "WhatsApp Images"))[0];
        var url = HtmlSafe.MediaUrl(Path.GetRelativePath(Archive, image).Replace('\\', '/'))!;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Media_FileNamesWithPercentAreFound()
    {
        var path = Path.Combine(Archive, "WhatsApp", "Media", "Extra", "a%20b.png");
        File.Copy(Directory.GetFiles(Path.Combine(Archive, "WhatsApp", "Media", "WhatsApp Images"))[0], path);

        using var response = await _client.GetAsync(HtmlSafe.MediaUrl("WhatsApp/Media/Extra/a%20b.png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/media/..%2Fsecret.txt")]
    [InlineData("/media/WhatsApp/..%2F..%2Fsecret.txt")]
    [InlineData("/media/%2Fetc%2Fpasswd")]
    [InlineData("/media/C:%5CWindows%5Cwin.ini")]
    [InlineData("/media/WhatsApp/Media/link.txt")]
    [InlineData("/media/WhatsApp/Media/outside/secret.txt")]
    [InlineData("/media/WhatsApp/Media/missing.png")]
    public async Task Media_RejectsTraversalAndSymlinkEscape(string url)
    {
        using var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("outside the archive", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Media_ActiveContentIsDownloadOnlyAndSandboxed()
    {
        using var response = await _client.GetAsync("/media/WhatsApp/Media/Extra/page.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("sandbox", string.Join(';', response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ui_FilesWithExtensionsAreRouted()
    {
        using var response = await _client.GetAsync("/_next/static/does-not-exist.js");

        // routed to the UI handler (no UI embedded in tests → 404 from it, but with our security headers)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        using var index = await _client.GetAsync("/");
        Assert.Equal("text/html", index.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void Sanitizer_StripsScriptAndForeignLinks()
    {
        var html = HtmlSafe.Sanitize(
            "<script>alert(1)</script><img src=x onerror=alert(1)><a href=\"javascript:alert(1)\">x</a>" +
            "<a href=\"https://evil.example/\">y</a><a href=\"../../secret\">z</a>" +
            "<b onclick=\"alert(1)\">bold</b><br><a href=\"WhatsApp/vCards/1-Anna Becker.vcf\">Anna</a>");

        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.example", html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("<b>bold</b>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/media/WhatsApp/vCards/1-Anna%20Becker.vcf\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_ReportsReadyWithCounts()
    {
        var status = await _client.GetFromJsonAsync<JsonElement>("/api/status");

        Assert.Equal("ready", status.GetProperty("state").GetString());
        Assert.Equal(12, status.GetProperty("chats").GetInt32());
    }
}
