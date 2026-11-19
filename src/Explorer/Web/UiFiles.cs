using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace ArchiveExplorer.Web;

/// <summary>
/// The web UI (Next.js static export) embedded in the executable as resources named "ui/&lt;path&gt;".
/// Inline scripts in its HTML are hashed once so the CSP can allow exactly those and nothing else.
/// </summary>
public sealed partial class UiFiles
{
    private const string Prefix = "ui/";
    private static readonly FileExtensionContentTypeProvider Types = new();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public UiFiles(Assembly? assembly = null)
    {
        assembly ??= typeof(UiFiles).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            _files[name[Prefix.Length..]] = ms.ToArray();
        }

        var hashes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (path, bytes) in _files.Where(f => f.Key.EndsWith(".html", StringComparison.Ordinal)))
        {
            foreach (Match m in InlineScript().Matches(Encoding.UTF8.GetString(bytes)))
            {
                hashes.Add($" 'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups[1].Value)))}'");
            }
        }
        ScriptHashSources = string.Concat(hashes);
    }

    public bool HasUi => _files.ContainsKey("index.html");

    /// <summary>CSP source list fragment, e.g. " 'sha256-…' 'sha256-…'".</summary>
    public string ScriptHashSources { get; }

    public IResult Serve(string path)
    {
        if (!HasUi)
        {
            return Results.Content(
                "<!doctype html><meta charset=utf-8><title>whatsapp-archive-explorer</title>" +
                "<p>This build has no web UI. Build it with <code>npm run build</code> in <code>src/web</code>.</p>",
                "text/html; charset=utf-8");
        }
        var key = path.Trim('/');
        if (key.Length == 0)
        {
            key = "index.html";
        }
        if (!_files.TryGetValue(key, out var bytes) && !_files.TryGetValue(key + "/index.html", out bytes) && !_files.TryGetValue(key + ".html", out bytes))
        {
            if (Path.HasExtension(key))
            {
                return Results.NotFound();
            }
            key = "index.html"; // client-side route
            bytes = _files[key];
        }
        else if (!_files.ContainsKey(key))
        {
            key = _files.ContainsKey(key + "/index.html") ? key + "/index.html" : key + ".html";
        }
        var type = Types.TryGetContentType(key, out var t) ? t : "application/octet-stream";
        return Results.Bytes(bytes, type);
    }

    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();
}
