using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using ArchiveExplorer.Cli;
using ArchiveExplorer.Import;
using ArchiveExplorer.Index;
using ArchiveExplorer.Web;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace ArchiveExplorer;

public static class Program
{
    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out, Console.Error);

    /// <summary>Prints counts and paths only — never chat names or message text.</summary>
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = Options.Parse(args);
        if (parsed.Options is not { } options)
        {
            await error.WriteLineAsync($"error: {parsed.Error}\n\n{Options.Usage}");
            return 2;
        }
        if (options.ShowHelp)
        {
            await output.WriteLineAsync(Options.Usage);
            return 0;
        }
        if (options.ShowVersion)
        {
            await output.WriteLineAsync(Version());
            return 0;
        }

        var archive = Path.GetFullPath(options.Archive!);
        if (!Directory.Exists(archive))
        {
            await error.WriteLineAsync($"error: folder not found: {archive}");
            return 2;
        }
        if (ExportReader.DiscoverFiles(archive).Count == 0)
        {
            await error.WriteLineAsync(
                $"error: no WhatsApp-Chat-Exporter JSON in {archive} — expected json/*.json (export with --per-chat) or result.json");
            return 2;
        }
        foreach (var warning in parsed.Warnings)
        {
            await error.WriteLineAsync($"warning: {warning}");
        }

        var dbPath = options.NoCache ? null : CachePaths.ForArchive(archive);
        var app = ExplorerHost.Create(new HostSettings(
            archive,
            dbPath,
            options.Port,
            options.Listen,
            options.Listen is null ? null : [options.Listen, Environment.MachineName, Environment.MachineName + ".local"]));
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var port = new Uri(address).Port;
        var url = $"http://127.0.0.1:{port}/";
        await output.WriteLineAsync($"whatsapp-archive-explorer {Version()}");
        await output.WriteLineAsync($"archive: {archive}");
        await output.WriteLineAsync($"index:   {dbPath ?? "in memory"}");
        await output.WriteLineAsync($"open:    {url}   (Ctrl+C to stop)");
        if (!options.NoBrowser && !BrowserLauncher.TryOpen(url))
        {
            await output.WriteLineAsync("Could not open a browser — open the address above yourself.");
        }

        var state = app.Services.GetRequiredService<IndexState>();
        var watch = Stopwatch.StartNew();
        while (!state.Ready.IsCompleted)
        {
            await Task.WhenAny(state.Ready, Task.Delay(TimeSpan.FromSeconds(options.Verbose ? 2 : 10)));
            if (!state.Ready.IsCompleted && state.FilesTotal > 0)
            {
                await output.WriteLineAsync($"indexing: {state.FilesDone}/{state.FilesTotal} files ({watch.Elapsed.TotalSeconds:F0} s)");
            }
        }
        if (state.State == "ready" && state.Result is { } r)
        {
            await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"ready: {r.Chats} chats, {r.Messages} messages ({r.FilesIndexed} of {r.FilesTotal} files indexed in {state.Elapsed.TotalSeconds:F1} s)"));
        }
        else
        {
            await error.WriteLineAsync("error: indexing failed — the browser shows the status.");
        }

        await app.WaitForShutdownAsync();
        return 0;
    }

    private static string Version() =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
