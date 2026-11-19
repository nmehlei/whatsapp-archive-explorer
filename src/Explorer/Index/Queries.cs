using System.Text;
using System.Text.Json;
using ArchiveExplorer.Contacts;
using ArchiveExplorer.Import;
using ArchiveExplorer.Web;
using Microsoft.Data.Sqlite;

namespace ArchiveExplorer.Index;

public sealed record ChatSummary(long Id, string Jid, string? Name, string Kind, bool HasAvatar, long? LastTs, string? LastPreview, int MessageCount);

public sealed record MessageDto(
    long Id,
    string MessageId,
    string? KeyId,
    long Ts,
    bool FromMe,
    string? Sender,
    bool IsSystem,
    string? Text,
    string? Caption,
    string? Html,
    bool IsMedia,
    string? MediaUrl,
    string? Mime,
    string? ReplyKeyId,
    string? QuotedPreview,
    long? ReplyToId,
    IReadOnlyDictionary<string, string>? Reactions,
    bool Edited,
    IReadOnlyList<MessageDto>? Revisions);

/// <summary>A search hit. <see cref="Snippet"/> marks matches with \u0002 … \u0003 (plain text otherwise).</summary>
public sealed record SearchHit(long ChatId, string? ChatName, long MessageId, long Ts, string? Sender, string Snippet);

public sealed record Page<T>(IReadOnlyList<T> Items, bool HasMoreBefore, bool HasMoreAfter);

public sealed record MediaItem(long Id, long Ts, string Url, string? Mime, string? Caption);

public sealed record MediaPage(IReadOnlyList<MediaItem> Items, int Total);

public sealed class Queries(SqliteConnection connection, AddressBook? contacts = null)
{
    private const string MessageColumns = """
        m.rowid, m.id, m.key_id, m.ts, m.from_me, m.sender, m.is_system, m.text, m.caption, m.raw_html, m.is_media,
        m.media_path, m.mime, m.reply_key_id, m.quoted_preview,
        (SELECT r.rowid FROM messages r WHERE r.chat_rowid = m.chat_rowid AND r.key_id = m.reply_key_id LIMIT 1),
        m.reactions_json, m.edited, m.revisions_json
        """;

    private readonly AddressBook _contacts = contacts ?? AddressBook.Empty;

    /// <summary>Newest first. Names missing in the export come from the address book; the filter matches names and numbers, ignoring accents.</summary>
    public IReadOnlyList<ChatSummary> ListChats(string? filter)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT rowid, jid, name, kind, avatar, last_ts, last_preview, message_count FROM chats ORDER BY last_ts IS NULL, last_ts DESC, rowid";
        var folded = string.IsNullOrWhiteSpace(filter) ? null : IndexStore.Fold(filter.Trim());
        var result = new List<ChatSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var jid = r.GetString(1);
            var name = NullableString(r, 2) ?? _contacts.NameFor(jid);
            if (folded is not null && !IndexStore.Fold($"{name} {jid.Split('@')[0]}").Contains(folded, StringComparison.Ordinal))
            {
                continue;
            }
            result.Add(new ChatSummary(r.GetInt64(0), jid, name, r.GetString(3), !r.IsDBNull(4) || _contacts.PhotoFor(jid) is not null,
                r.IsDBNull(5) ? null : r.GetInt64(5), NullableString(r, 6), r.GetInt32(7)));
        }
        return result;
    }

    public string? ChatJid(long chatId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT jid FROM chats WHERE rowid = $id";
        cmd.Parameters.AddWithValue("$id", chatId);
        return cmd.ExecuteScalar() as string;
    }

    public string? AvatarPath(long chatId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT avatar FROM chats WHERE rowid = $id";
        cmd.Parameters.AddWithValue("$id", chatId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// One page of a chat, oldest first. Without a cursor: the newest messages. <paramref name="before"/>/<paramref name="after"/>
    /// page from a message id; <paramref name="around"/>/<paramref name="aroundTs"/> centre on a message or a moment.
    /// </summary>
    public Page<MessageDto> Messages(long chatId, long? before, long? after, long? around, long? aroundTs, int limit = 80)
    {
        limit = Math.Clamp(limit, 1, 500);
        if (aroundTs is not null && around is null)
        {
            around = ScalarLong("SELECT rowid FROM messages WHERE chat_rowid = $c AND ts >= $t ORDER BY ts, rowid LIMIT 1",
                         ("$c", chatId), ("$t", aroundTs))
                     ?? ScalarLong("SELECT rowid FROM messages WHERE chat_rowid = $c ORDER BY ts DESC, rowid DESC LIMIT 1", ("$c", chatId));
            if (around is null)
            {
                return new Page<MessageDto>([], false, false);
            }
        }

        if (around is not null)
        {
            var key = Key(chatId, around.Value);
            if (key is null)
            {
                return new Page<MessageDto>([], false, false);
            }
            var half = limit / 2;
            var (older, moreBefore) = Fetch(chatId, "(m.ts, m.rowid) < ($ts, $id)", key.Value, descending: true, half);
            var (newer, moreAfter) = Fetch(chatId, "(m.ts, m.rowid) >= ($ts, $id)", key.Value, descending: false, limit - half);
            older.Reverse();
            return new Page<MessageDto>([.. older, .. newer], moreBefore, moreAfter);
        }

        if (after is not null)
        {
            var key = Key(chatId, after.Value);
            if (key is null)
            {
                return new Page<MessageDto>([], false, false);
            }
            var (items, more) = Fetch(chatId, "(m.ts, m.rowid) > ($ts, $id)", key.Value, descending: false, limit);
            return new Page<MessageDto>(items, items.Count > 0 || Exists(chatId, "<", key.Value), more);
        }

        if (before is not null)
        {
            var key = Key(chatId, before.Value);
            if (key is null)
            {
                return new Page<MessageDto>([], false, false);
            }
            var (items, more) = Fetch(chatId, "(m.ts, m.rowid) < ($ts, $id)", key.Value, descending: true, limit);
            items.Reverse();
            return new Page<MessageDto>(items, more, true);
        }

        var (newest, hasMore) = Fetch(chatId, "1 = 1", (0, 0), descending: true, limit);
        newest.Reverse();
        return new Page<MessageDto>(newest, hasMore, false);
    }

    /// <summary>Media messages of a chat (images, videos, audio, documents), newest first.</summary>
    public MediaPage Media(long chatId, int offset, int limit = 60)
    {
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT count(*) FROM messages WHERE chat_rowid = $c AND is_media = 1 AND media_path IS NOT NULL";
        count.Parameters.AddWithValue("$c", chatId);
        var total = Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT rowid, ts, media_path, mime, caption FROM messages
            WHERE chat_rowid = $c AND is_media = 1 AND media_path IS NOT NULL
            ORDER BY ts DESC, rowid DESC LIMIT $limit OFFSET $offset
            """;
        cmd.Parameters.AddWithValue("$c", chatId);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        cmd.Parameters.AddWithValue("$offset", Math.Max(0, offset));
        var items = new List<MediaItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (HtmlSafe.MediaUrl(r.GetString(2)) is { } url)
            {
                items.Add(new MediaItem(r.GetInt64(0), r.GetInt64(1), url, NullableString(r, 3), NullableString(r, 4)));
            }
        }
        return new MediaPage(items, total);
    }

    /// <summary>Ids of the messages in a chat that match, oldest first.</summary>
    public IReadOnlyList<long> SearchInChat(long chatId, string q)
    {
        var match = FtsQuery(q);
        if (match is null)
        {
            return [];
        }
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT m.rowid FROM messages_fts f JOIN messages m ON m.rowid = f.rowid
            WHERE messages_fts MATCH $q AND m.chat_rowid = $c ORDER BY m.ts, m.rowid
            """;
        cmd.Parameters.AddWithValue("$q", match);
        cmd.Parameters.AddWithValue("$c", chatId);
        var ids = new List<long>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ids.Add(r.GetInt64(0));
        }
        return ids;
    }

    public (IReadOnlyList<SearchHit> Hits, int Total) Search(string q, long? chatId, bool? fromMe, long? fromTs, long? toTs, bool? hasMedia, int offset, int limit = 50)
    {
        var match = FtsQuery(q);
        if (match is null)
        {
            return ([], 0);
        }
        var where = new StringBuilder("messages_fts MATCH $q");
        if (chatId is not null)
        {
            where.Append(" AND m.chat_rowid = $c");
        }
        if (fromMe is not null)
        {
            where.Append(" AND m.from_me = $me");
        }
        if (fromTs is not null)
        {
            where.Append(" AND m.ts >= $from");
        }
        if (toTs is not null)
        {
            where.Append(" AND m.ts <= $to");
        }
        if (hasMedia is not null)
        {
            where.Append(" AND m.is_media = $media");
        }

        void Bind(SqliteCommand cmd)
        {
            cmd.Parameters.AddWithValue("$q", match);
            cmd.Parameters.AddWithValue("$c", (object?)chatId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$me", fromMe == true ? 1 : 0);
            cmd.Parameters.AddWithValue("$from", (object?)fromTs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$to", (object?)toTs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$media", hasMedia == true ? 1 : 0);
        }

        using var count = connection.CreateCommand();
        count.CommandText = $"SELECT count(*) FROM messages_fts JOIN messages m ON m.rowid = messages_fts.rowid WHERE {where}";
        Bind(count);
        var total = Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT m.chat_rowid, c.name, m.rowid, m.ts, m.sender,
                   snippet(messages_fts, -1, char(2), char(3), '…', 12), c.jid
            FROM messages_fts JOIN messages m ON m.rowid = messages_fts.rowid JOIN chats c ON c.rowid = m.chat_rowid
            WHERE {where}
            ORDER BY m.ts DESC, m.rowid DESC
            LIMIT $limit OFFSET $offset
            """;
        Bind(cmd);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        cmd.Parameters.AddWithValue("$offset", Math.Max(0, offset));
        var hits = new List<SearchHit>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            hits.Add(new SearchHit(r.GetInt64(0), NullableString(r, 1) ?? _contacts.NameFor(r.GetString(6)), r.GetInt64(2), r.GetInt64(3),
                Sender(NullableString(r, 4)), r.IsDBNull(5) ? "" : r.GetString(5)));
        }
        return (hits, total);
    }

    /// <summary>
    /// User text → a safe FTS5 query. Every whitespace-separated term becomes a quoted phrase of its letter/digit runs
    /// ("3.5" → "3 5", an email or URL → its words in order), exactly as the unicode61 tokenizer split the indexed text.
    /// No operators or column filters can get through; the last term is a prefix. Emoji/symbols are not tokens and are
    /// dropped (not searchable).
    /// </summary>
    internal static string? FtsQuery(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return null;
        }
        var phrases = new List<string>();
        foreach (var term in q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var words = new List<string>();
            var current = new StringBuilder();
            foreach (var rune in term.EnumerateRunes())
            {
                if (Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    current.Append(rune.ToString());
                }
                else if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
            }
            if (current.Length > 0)
            {
                words.Add(current.ToString());
            }
            if (words.Count > 0)
            {
                phrases.Add("\"" + string.Join(' ', words) + "\"");
            }
        }
        if (phrases.Count == 0)
        {
            return null;
        }
        phrases[^1] += "*";
        return string.Join(' ', phrases);
    }

    private (List<MessageDto> Items, bool More) Fetch(long chatId, string condition, (long Ts, long Id) key, bool descending, int limit)
    {
        using var cmd = connection.CreateCommand();
        var order = descending ? "DESC" : "ASC";
        cmd.CommandText = $"SELECT {MessageColumns} FROM messages m WHERE m.chat_rowid = $c AND {condition} ORDER BY m.ts {order}, m.rowid {order} LIMIT $limit";
        cmd.Parameters.AddWithValue("$c", chatId);
        cmd.Parameters.AddWithValue("$ts", key.Ts);
        cmd.Parameters.AddWithValue("$id", key.Id);
        cmd.Parameters.AddWithValue("$limit", limit + 1);
        var items = new List<MessageDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            items.Add(ReadMessage(r));
        }
        var more = items.Count > limit;
        if (more)
        {
            items.RemoveAt(items.Count - 1);
        }
        return (items, more);
    }

    private bool Exists(long chatId, string op, (long Ts, long Id) key) =>
        ScalarLong($"SELECT 1 FROM messages WHERE chat_rowid = $c AND (ts, rowid) {op} ($t, $i) LIMIT 1",
            ("$c", chatId), ("$t", key.Ts), ("$i", key.Id)) is not null;

    private (long Ts, long Id)? Key(long chatId, long messageId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ts, rowid FROM messages WHERE rowid = $id AND chat_rowid = $c";
        cmd.Parameters.AddWithValue("$id", messageId);
        cmd.Parameters.AddWithValue("$c", chatId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt64(0), r.GetInt64(1)) : null;
    }

    private long? ScalarLong(string sql, params (string Name, object? Value)[] values)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in values)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return cmd.ExecuteScalar() is long v ? v : null;
    }

    private MessageDto ReadMessage(SqliteDataReader r)
    {
        var html = NullableString(r, 9);
        return new MessageDto(
            Id: r.GetInt64(0),
            MessageId: r.GetString(1),
            KeyId: NullableString(r, 2),
            Ts: r.GetInt64(3),
            FromMe: r.GetInt64(4) == 1,
            Sender: Sender(NullableString(r, 5)),
            IsSystem: r.GetInt64(6) == 1,
            Text: NullableString(r, 7),
            Caption: NullableString(r, 8),
            Html: html is null ? null : HtmlSafe.Sanitize(html),
            IsMedia: r.GetInt64(10) == 1,
            MediaUrl: HtmlSafe.MediaUrl(NullableString(r, 11)),
            Mime: NullableString(r, 12),
            ReplyKeyId: NullableString(r, 13),
            QuotedPreview: NullableString(r, 14),
            ReplyToId: r.IsDBNull(15) ? null : r.GetInt64(15),
            Reactions: r.IsDBNull(16) ? null : Reactions(JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(16))),
            Edited: r.GetInt64(17) == 1,
            Revisions: r.IsDBNull(18) ? null : JsonSerializer.Deserialize<List<MessageRecord>>(r.GetString(18))?.Select(FromRecord).ToList());
    }

    private string? Sender(string? sender) => _contacts.NameForSender(sender) ?? sender;

    private Dictionary<string, string>? Reactions(IReadOnlyDictionary<string, string>? reactions)
    {
        if (reactions is null)
        {
            return null;
        }
        var mapped = new Dictionary<string, string>();
        foreach (var (who, emoji) in reactions)
        {
            mapped[Sender(who) ?? who] = emoji;
        }
        return mapped;
    }

    private MessageDto FromRecord(MessageRecord m) => new(
        0, m.Id, m.KeyId, m.Timestamp, m.FromMe, Sender(m.Sender), m.IsSystem, m.Text, m.Caption,
        m.RawHtml is null ? null : HtmlSafe.Sanitize(m.RawHtml), m.IsMedia, HtmlSafe.MediaUrl(m.MediaPath), m.Mime,
        m.ReplyKeyId, m.QuotedPreview, null, Reactions(m.Reactions), m.Edited, null);

    private static string? NullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
