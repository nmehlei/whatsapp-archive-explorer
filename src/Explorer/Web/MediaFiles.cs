using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace ArchiveExplorer.Web;

/// <summary>
/// Serves files from inside the archive only. Paths are decoded once, must stay relative, and are resolved
/// through every symlink before the containment check. Only passive media types are shown inline; everything
/// else (HTML, SVG, PDF, …) is a sandboxed download so it can never run script on our origin.
/// </summary>
public sealed class MediaFiles(string archiveRoot)
{
    private static readonly FileExtensionContentTypeProvider Types = CreateTypes();
    private readonly string _realRoot = RealPath(Path.GetFullPath(archiveRoot));

    public IResult Serve(string? relativePath)
    {
        var file = Resolve(relativePath);
        if (file is null)
        {
            return Results.NotFound();
        }
        var type = Types.TryGetContentType(file, out var t) ? t : "application/octet-stream";
        if (IsInline(type))
        {
            return Results.File(file, type, enableRangeProcessing: true);
        }
        return Results.File(file, "application/octet-stream", fileDownloadName: Path.GetFileName(file), enableRangeProcessing: true);
    }

    /// <summary>The real path of a file inside the archive, or null.</summary>
    internal string? Resolve(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return null;
        }
        // relativePath is already decoded (ASP.NET leaves only %2F encoded); never decode twice, or a file named
        // "a%20b.png" would be looked up as "a b.png"
        var decoded = relativePath.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase).Replace('\\', '/');
        if (decoded.StartsWith('/') || decoded.Contains(':', StringComparison.Ordinal) || decoded.Contains('\0', StringComparison.Ordinal))
        {
            return null;
        }
        var segments = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
        {
            return null;
        }
        var candidate = Path.Combine([_realRoot, .. segments]);
        if (!File.Exists(candidate))
        {
            return null;
        }
        var real = RealPath(candidate);
        var prefix = _realRoot.EndsWith(Path.DirectorySeparatorChar) ? _realRoot : _realRoot + Path.DirectorySeparatorChar;
        return real.StartsWith(prefix, StringComparison.Ordinal) && File.Exists(real) ? real : null;
    }

    private static bool IsInline(string type) =>
        type is "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "image/bmp"
        || type.StartsWith("video/", StringComparison.Ordinal)
        || type.StartsWith("audio/", StringComparison.Ordinal);

    /// <summary>Resolves symlinks component by component (also directory links in the middle of the path).</summary>
    internal static string RealPath(string path, int depth = 0)
    {
        if (depth > 32)
        {
            throw new IOException("too many symbolic links");
        }
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, part);
            var info = new FileInfo(next);
            if (info.LinkTarget is not null)
            {
                var target = info.LinkTarget;
                next = RealPath(Path.IsPathRooted(target) ? target : Path.Combine(current, target), depth + 1);
            }
            current = next;
        }
        return current;
    }

    private static FileExtensionContentTypeProvider CreateTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".opus"] = "audio/ogg";
        provider.Mappings[".oga"] = "audio/ogg";
        provider.Mappings[".m4a"] = "audio/mp4";
        provider.Mappings[".aac"] = "audio/aac";
        provider.Mappings[".amr"] = "audio/amr";
        provider.Mappings[".3gp"] = "video/3gpp";
        provider.Mappings[".webp"] = "image/webp";
        provider.Mappings[".vcf"] = "text/vcard";
        return provider;
    }
}
