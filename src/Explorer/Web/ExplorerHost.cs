using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ArchiveExplorer.Index;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArchiveExplorer.Web;

public static class ExplorerHost
{
    /// <param name="configure">Test hook (e.g. UseTestServer) applied to the builder before Build().</param>
    public static WebApplication Create(HostSettings settings, Action<WebApplicationBuilder>? configure = null)
    {
        // No configuration from the working folder (appsettings.json) or the environment (Kestrel__Endpoints__…,
        // ASPNETCORE_ENVIRONMENT): either could open extra network ports or enable developer error pages.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
        });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders(); // the CLI prints counts itself; framework logs could contain paths/queries
        var extra = settings.ListenAddress is null ? null : IPAddress.Parse(settings.ListenAddress);
        if (extra is not null && IPAddress.IsLoopback(extra))
        {
            extra = null;
        }
        // with a second listener both must share one port, so a random port is picked up front
        var port = settings.Port == 0 && extra is not null ? FreePort() : settings.Port;
        builder.WebHost.UseKestrel(k =>
        {
            k.AddServerHeader = false;
            if (extra is null || !(extra.Equals(IPAddress.Any) || extra.Equals(IPAddress.IPv6Any)))
            {
                k.Listen(IPAddress.Loopback, port); // 0.0.0.0 / :: already include loopback
            }
            if (extra is not null)
            {
                k.Listen(extra, port);
            }
        });
        configure?.Invoke(builder);

        var store = IndexStore.Open(settings.ArchiveRoot, settings.DbPath);
        var state = new IndexState(store);
        var media = new MediaFiles(settings.ArchiveRoot);
        var ui = new UiFiles();
        builder.Services.AddSingleton(state);

        var app = builder.Build();
        var hosts = AllowedHostNames(settings.ListenAddress);
        foreach (var name in settings.ExtraHostNames ?? [])
        {
            hosts.Add(name.ToLowerInvariant());
        }
        app.UseMiddleware<LocalOnly>((IReadOnlySet<string>)hosts, settings.ListenAddress is not null);
        app.UseMiddleware<SecurityHeaders>(ui);
        Api.Map(app, state, media, ui);

        app.Lifetime.ApplicationStarted.Register(() => Task.Run(state.Run));
        app.Lifetime.ApplicationStopped.Register(store.Dispose);
        return app;
    }

    /// <summary>
    /// Host names the server answers to: always the loopback names; with --listen also the machine's names and
    /// the listen address (for 0.0.0.0 / :: every address of this machine).
    /// </summary>
    public static HashSet<string> AllowedHostNames(string? listenAddress)
    {
        var hosts = new HashSet<string>(LocalOnly.LoopbackNames, StringComparer.OrdinalIgnoreCase);
        if (listenAddress is null)
        {
            return hosts;
        }
        hosts.Add(Environment.MachineName.ToLowerInvariant());
        hosts.Add(Environment.MachineName.ToLowerInvariant() + ".local");
        var address = IPAddress.Parse(listenAddress);
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    hosts.Add(Format(unicast.Address));
                }
            }
        }
        else
        {
            hosts.Add(Format(address));
        }
        return hosts;
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static string Format(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{new IPAddress(address.GetAddressBytes())}]" : address.ToString();

    public static async Task WaitUntilReadyAsync(WebApplication app, TimeSpan timeout) =>
        await app.Services.GetRequiredService<IndexState>().Ready.WaitAsync(timeout);
}
