using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArchiveExplorer.Import;

/// <summary>
/// Reads WhatsApp-Chat-Exporter JSON (per-chat files or one result.json). Tolerant by design: unknown fields
/// are ignored, malformed messages are skipped and counted, a broken file is skipped and counted.
/// </summary>
public static partial class ExportReader
{
    public const string CallsJid = "000000000000000";
    private const int SystemMessageType = 7;

    /// <summary>The export's JSON files, sorted. Accepts the export root or its json/ folder.</summary>
    public static IReadOnlyList<ExportFile> DiscoverFiles(string root)
    {
        var candidates = new[] { Path.Combine(root, "json"), root };
        foreach (var dir in candidates)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }
            var files = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => new FileInfo(f))
                .Select(f => new ExportFile(
                    Path.GetRelativePath(root, f.FullName).Replace(Path.DirectorySeparatorChar, '/'),
                    f.Length,
                    f.LastWriteTimeUtc))
                .ToList();
            if (files.Count > 0)
            {
                return files;
            }
        }
        return [];
    }

    public static IEnumerable<ChatRecord> ReadFile(string root, ExportFile file, ImportStats stats)
    {
        JsonDocument doc;
        try
        {
            using var stream = File.OpenRead(Path.Combine(root, file.RelativePath));
            doc = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            stats.BrokenFiles++;
            yield break;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                stats.BrokenFiles++;
                yield break;
            }
            foreach (var chat in doc.RootElement.EnumerateObject())
            {
                if (chat.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var record = ReadChat(chat.Name, chat.Value, stats);
                stats.Chats++;
                stats.Messages += record.Messages.Count;
                yield return record;
            }
        }
    }

    private static ChatRecord ReadChat(string jid, JsonElement chat, ImportStats stats)
    {
        var kind = KindOf(jid);
        var messages = new List<MessageRecord>();
        if (chat.TryGetProperty("messages", out var list) && list.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in list.EnumerateObject())
            {
                var message = ReadMessage(entry.Name, entry.Value, kind);
                if (message is null)
                {
                    stats.SkippedMessages++;
                }
                else
                {
                    messages.Add(message);
                }
            }
        }
        messages.Sort((a, b) => a.Timestamp != b.Timestamp
            ? a.Timestamp.CompareTo(b.Timestamp)
            : string.CompareOrdinal(a.Id, b.Id));
        return new ChatRecord(jid, String(chat, "name"), kind, String(chat, "their_avatar"), messages);
    }

    private static MessageRecord? ReadMessage(string id, JsonElement m, string kind)
    {
        if (m.ValueKind != JsonValueKind.Object || !TryTimestamp(m, out var ts))
        {
            return null;
        }

        var isMedia = Bool(m, "media");
        var data = String(m, "data");
        var mime = String(m, "mime");
        string? text = null, html = null, mediaPath = null;
        if (isMedia)
        {
            mediaPath = string.IsNullOrWhiteSpace(data) ? null : data;
        }
        else if (data is not null && LooksLikeHtml().IsMatch(data))
        {
            html = data;
            text = WebUtility.HtmlDecode(Tags().Replace(data.Replace("<br>", " ", StringComparison.OrdinalIgnoreCase), "")).Trim();
        }
        else
        {
            text = data;
        }

        int? type = m.TryGetProperty("message_type", out var t) && t.TryGetInt32(out var tv) ? tv : null;
        var isSystem = type == SystemMessageType
            || (Bool(m, "meta") && !isMedia && mime is null && html is null && kind != "calls");

        Dictionary<string, string>? reactions = null;
        if (m.TryGetProperty("reactions", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in r.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    (reactions ??= new Dictionary<string, string>())[p.Name] = p.Value.GetString()!;
                }
            }
        }

        List<MessageRecord>? revisions = null;
        if (m.TryGetProperty("revisions", out var revs) && revs.ValueKind == JsonValueKind.Array)
        {
            foreach (var rev in revs.EnumerateArray())
            {
                var parsed = ReadMessage(id, rev, kind);
                if (parsed is not null)
                {
                    (revisions ??= []).Add(parsed);
                }
            }
        }

        return new MessageRecord(
            Id: id,
            KeyId: String(m, "key_id"),
            Timestamp: ts,
            FromMe: Bool(m, "from_me"),
            Sender: String(m, "sender"),
            MessageType: type,
            Text: text,
            Caption: String(m, "caption"),
            IsMedia: isMedia,
            MediaPath: mediaPath,
            Mime: mime,
            ReplyKeyId: String(m, "reply"),
            QuotedPreview: String(m, "quoted_data"),
            Reactions: reactions,
            IsSystem: isSystem,
            Edited: Bool(m, "edited"),
            Revisions: revisions,
            RawHtml: html);
    }

    internal static string KindOf(string jid) => jid switch
    {
        CallsJid => "calls",
        _ when jid.EndsWith("@g.us", StringComparison.Ordinal) => "group",
        _ when jid.EndsWith("@s.whatsapp.net", StringComparison.Ordinal) || jid.EndsWith("@lid", StringComparison.Ordinal) => "private",
        _ => "other",
    };

    /// <summary>Unix seconds; exporter versions that stored milliseconds are normalised.</summary>
    private static bool TryTimestamp(JsonElement m, out long ts)
    {
        ts = 0;
        if (!m.TryGetProperty("timestamp", out var v))
        {
            return false;
        }
        var parsed = v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var text) => text,
            _ => (double?)null,
        };
        if (parsed is not { } raw || raw <= 0)
        {
            return false;
        }
        if (raw > 9_999_999_999)
        {
            raw /= 1000;
        }
        ts = (long)raw;
        return true;
    }

    private static string? String(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"<(a|br|b|i|em|strong)[\s>/]", RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeHtml();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
}
