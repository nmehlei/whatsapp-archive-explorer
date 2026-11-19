using System.Globalization;
using System.Net;

namespace ArchiveExplorer.Cli;

public sealed record Options(
    string? Archive,
    int Port,
    string? Listen,
    bool NoBrowser,
    bool NoCache,
    bool Verbose,
    bool ShowHelp,
    bool ShowVersion)
{
    public const string Usage = """
        Usage: whatsapp-archive-explorer [--archive] <export folder> [options]

        Browse and search a WhatsApp-Chat-Exporter export (per-chat JSON + media) in your browser.
        Read-only: nothing in the export folder is changed. Runs on this computer only.

        Options:
          --archive <dir>   The export folder (contains json/ or result.json). May also be given first.
          --port <n>        Port on 127.0.0.1 (default: a free port).
          --listen <ip>     Also listen on this address (0.0.0.0 = all) — anyone on the network can then read your chats!
          --no-browser      Do not open the browser.
          --no-cache        Keep the search index in memory only (rebuilt on every start).
          --verbose         More progress output (counts only).
          --version         Print the version.
          --help            Show this help.
        """;

    public sealed record ParseResult(Options? Options, string? Error, IReadOnlyList<string> Warnings);

    public static ParseResult Parse(string[] args)
    {
        string? archive = null, listen = null;
        int port = 0;
        bool noBrowser = false, noCache = false, verbose = false, help = false, version = false;
        var warnings = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? Value()
            {
                return i + 1 < args.Length ? args[++i] : null;
            }

            switch (arg)
            {
                case "--archive":
                    archive = Value();
                    if (archive is null)
                    {
                        return Fail("--archive needs a folder");
                    }
                    break;
                case "--port":
                    if (!int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 0 or > 65535)
                    {
                        return Fail("--port needs a number between 0 and 65535");
                    }
                    break;
                case "--listen":
                    listen = Value();
                    if (listen is null || !IPAddress.TryParse(listen, out _))
                    {
                        return Fail("--listen needs an IP address, e.g. 0.0.0.0");
                    }
                    warnings.Add($"Listening on {listen}: anyone who can reach this computer over the network can read your chats.");
                    break;
                case "--no-browser":
                    noBrowser = true;
                    break;
                case "--no-cache":
                    noCache = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--help" or "-h" or "/?":
                    help = true;
                    break;
                case "--version":
                    version = true;
                    break;
                default:
                    if (arg.StartsWith('-') || archive is not null)
                    {
                        return Fail($"unknown option {arg}");
                    }
                    archive = arg;
                    break;
            }
        }

        if (archive is null && !help && !version)
        {
            return Fail("give the export folder: --archive <dir>");
        }
        return new ParseResult(new Options(archive, port, listen, noBrowser, noCache, verbose, help, version), null, warnings);

        static ParseResult Fail(string error) => new(null, error, []);
    }
}
