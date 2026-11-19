using Ganss.Xss;

namespace ArchiveExplorer.Web;

/// <summary>
/// Message HTML from the export (vCard links) is untrusted: only a tiny allow-list survives, and links may only
/// point at files inside the archive (rewritten to /media/...).
/// </summary>
public static class HtmlSafe
{
    private static readonly HtmlSanitizer Sanitizer = Create();

    public static string Sanitize(string html) => Sanitizer.Sanitize(html);

    /// <summary>"WhatsApp/vCards/a b.vcf" → "/media/WhatsApp/vCards/a%20b.vcf"; null when the path leaves the archive.</summary>
    public static string? MediaUrl(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }
        var path = relativePath.Replace('\\', '/');
        if (path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".."))
        {
            return null;
        }
        return "/media/" + string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    private static HtmlSanitizer Create()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var tag in new[] { "a", "br", "b", "i", "em", "strong" })
        {
            s.AllowedTags.Add(tag);
        }
        s.AllowedAttributes.Clear();
        s.AllowedAttributes.Add("href");
        s.AllowedCssProperties.Clear();
        s.AllowedAtRules.Clear();
        s.AllowedSchemes.Clear();
        s.AllowedClasses.Clear();
        s.UriAttributes.Clear();
        s.UriAttributes.Add("href");
        s.KeepChildNodes = true;
        s.FilterUrl += (_, e) =>
        {
            var original = Uri.UnescapeDataString(e.OriginalUrl);
            e.SanitizedUrl = original.StartsWith("/media/", StringComparison.Ordinal) ? e.OriginalUrl : MediaUrl(original);
        };
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement { TagName: "A" } a && a.GetAttribute("href") is null)
            {
                a.RemoveAttribute("href");
            }
            else if (e.Node is AngleSharp.Dom.IElement { TagName: "A" } link)
            {
                link.SetAttribute("rel", "noopener noreferrer");
            }
        };
        return s;
    }
}
