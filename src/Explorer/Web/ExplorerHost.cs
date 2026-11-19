using System.Net;
using ArchiveExplorer.Index;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArchiveExplorer.Web;

public static class ExplorerHost
{
    /// <param name="configure">Test hook (e.g. UseTestServer) applied to the builder before Build().</param>
    public static WebApplication Create(HostSettings settings, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders(); // the CLI prints counts itself; framework logs could contain paths/queries
        builder.WebHost.UseKestrel(k =>
        {
            k.AddServerHeader = false;
            var address = settings.ListenAddress is null ? IPAddress.Loopback : IPAddress.Parse(settings.ListenAddress);
            k.Listen(address, settings.Port);
        });
        configure?.Invoke(builder);

        var store = IndexStore.Open(settings.ArchiveRoot, settings.DbPath);
        var state = new IndexState(store);
        var media = new MediaFiles(settings.ArchiveRoot);
        var ui = new UiFiles();
        builder.Services.AddSingleton(state);

        var app = builder.Build();
        var hosts = new HashSet<string>(LocalOnly.LoopbackNames, StringComparer.OrdinalIgnoreCase);
        foreach (var name in settings.ExtraHostNames ?? [])
        {
            hosts.Add(name.ToLowerInvariant());
        }
        app.UseMiddleware<LocalOnly>((IReadOnlySet<string>)hosts);
        app.UseMiddleware<SecurityHeaders>(ui);
        Api.Map(app, state, media, ui);

        app.Lifetime.ApplicationStarted.Register(() => Task.Run(state.Run));
        app.Lifetime.ApplicationStopped.Register(store.Dispose);
        return app;
    }

    public static async Task WaitUntilReadyAsync(WebApplication app, TimeSpan timeout) =>
        await app.Services.GetRequiredService<IndexState>().Ready.WaitAsync(timeout);
}
