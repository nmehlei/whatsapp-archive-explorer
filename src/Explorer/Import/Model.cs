namespace ArchiveExplorer.Import;

/// <summary>One chat as read from a WhatsApp-Chat-Exporter JSON export.</summary>
public sealed record ChatRecord(
    string Jid,
    string? Name,
    string Kind,
    string? AvatarPath,
    IReadOnlyList<MessageRecord> Messages);

/// <summary>One message. <see cref="Id"/> is the exporter's message key (may be "&lt;id&gt;~&lt;key_id&gt;" in archives).</summary>
public sealed record MessageRecord(
    string Id,
    string? KeyId,
    long Timestamp,
    bool FromMe,
    string? Sender,
    int? MessageType,
    string? Text,
    string? Caption,
    bool IsMedia,
    string? MediaPath,
    string? Mime,
    string? ReplyKeyId,
    string? QuotedPreview,
    IReadOnlyDictionary<string, string>? Reactions,
    bool IsSystem,
    bool Edited,
    IReadOnlyList<MessageRecord>? Revisions,
    string? RawHtml);

/// <summary>A JSON file of the export with the fingerprint used for incremental refresh.</summary>
public sealed record ExportFile(string RelativePath, long Size, DateTime MtimeUtc);

/// <summary>Counts only — never names or content.</summary>
public sealed class ImportStats
{
    public int Chats { get; set; }
    public int Messages { get; set; }
    public int SkippedMessages { get; set; }
    public int BrokenFiles { get; set; }
}
