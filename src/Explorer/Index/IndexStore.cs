using System.Globalization;
using System.Text;
using System.Text.Json;
using ArchiveExplorer.Import;
using Microsoft.Data.Sqlite;

namespace ArchiveExplorer.Index;

public sealed record RefreshResult(int FilesTotal, int FilesIndexed, int FilesRemoved, int Chats, int Messages);

public sealed record IndexProgress(int FilesDone, int FilesTotal);

/// <summary>
/// The SQLite index of one archive. Reads the archive only; everything it writes goes to the index file
/// (or memory). A schema version mismatch rebuilds the index from scratch.
/// </summary>
public sealed class IndexStore : IDisposable
{
    private readonly string _archiveRoot;

    private IndexStore(string archiveRoot, SqliteConnection connection, string connectionString)
    {
        _archiveRoot = archiveRoot;
        Connection = connection;
        ConnectionString = connectionString;
    }

    /// <summary>The writer connection (also keeps an in-memory index alive).</summary>
    public SqliteConnection Connection { get; }

    public string ConnectionString { get; }

    /// <summary>A separate read-only connection, e.g. one per web request.</summary>
    public SqliteConnection OpenReader()
    {
        var reader = new SqliteConnection(ConnectionString);
        reader.Open();
        using var cmd = reader.CreateCommand();
        cmd.CommandText = "PRAGMA query_only = 1";
        cmd.ExecuteNonQuery();
        return reader;
    }

    /// <param name="dbPath">Index file; <c>null</c> keeps the index in memory.</param>
    public static IndexStore Open(string archiveRoot, string? dbPath)
    {
        string connectionString;
        if (dbPath is null)
        {
            connectionString = $"Data Source=wae-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        }
        else
        {
            CachePaths.EnsurePrivateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
            connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
        }

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        Execute(connection, dbPath is null ? "PRAGMA foreign_keys = OFF" : "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL");
        if (dbPath is not null && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(dbPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        if (ReadSchemaVersion(connection) != IndexSchema.Version)
        {
            Reset(connection);
        }
        return new IndexStore(Path.GetFullPath(archiveRoot), connection, connectionString);
    }

    public RefreshResult Refresh(IProgress<IndexProgress>? progress)
    {
        var files = ExportReader.DiscoverFiles(_archiveRoot);
        var known = new Dictionary<string, (long Size, long Mtime)>(StringComparer.Ordinal);
        using (var cmd = Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT path, size, mtime FROM files";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                known[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
            }
        }

        var indexed = 0;
        var done = 0;
        var stats = new ImportStats();
        foreach (var file in files)
        {
            var mtime = file.MtimeUtc.Ticks;
            if (!known.Remove(file.RelativePath, out var fingerprint) || fingerprint != (file.Size, mtime))
            {
                IndexFile(file, mtime, stats);
                indexed++;
            }
            progress?.Report(new IndexProgress(++done, files.Count));
        }

        foreach (var gone in known.Keys)
        {
            using var tx = Connection.BeginTransaction();
            DeleteFile(gone, tx);
            tx.Commit();
        }

        return new RefreshResult(
            files.Count,
            indexed,
            known.Count,
            (int)Count("SELECT count(*) FROM chats"),
            (int)Count("SELECT count(*) FROM messages"));
    }

    private void IndexFile(ExportFile file, long mtime, ImportStats stats)
    {
        using var tx = Connection.BeginTransaction();
        DeleteFile(file.RelativePath, tx);

        using var insertChat = Command(tx, """
            INSERT OR IGNORE INTO chats (jid, name, name_folded, kind, avatar, last_ts, last_preview, message_count, file)
            VALUES ($jid, $name, $folded, $kind, $avatar, $last_ts, $preview, $count, $file)
            RETURNING rowid
            """);
        using var insertMessage = Command(tx, """
            INSERT INTO messages (chat_rowid, id, key_id, ts, from_me, sender, type, text, caption, is_media, media_path,
                mime, reply_key_id, quoted_preview, reactions_json, is_system, edited, revisions_json, raw_html)
            VALUES ($chat, $id, $key, $ts, $from_me, $sender, $type, $text, $caption, $is_media, $media, $mime, $reply,
                $quoted, $reactions, $system, $edited, $revisions, $html)
            RETURNING rowid
            """);
        using var insertFts = Command(tx, "INSERT INTO messages_fts (rowid, text, caption, sender) VALUES ($rowid, $text, $caption, $sender)");

        foreach (var chat in ExportReader.ReadFile(_archiveRoot, file, stats))
        {
            var last = chat.Messages.Count > 0 ? chat.Messages[^1] : null;
            Bind(insertChat,
                ("$jid", chat.Jid), ("$name", chat.Name), ("$folded", Fold($"{chat.Name} {chat.Jid.Split('@')[0]}")),
                ("$kind", chat.Kind), ("$avatar", chat.AvatarPath), ("$last_ts", last?.Timestamp),
                ("$preview", last is null ? null : Preview(last)), ("$count", chat.Messages.Count), ("$file", file.RelativePath));
            if (insertChat.ExecuteScalar() is not long chatRowId)
            {
                continue; // the same chat id in two export files: the first one wins
            }

            foreach (var m in chat.Messages)
            {
                Bind(insertMessage,
                    ("$chat", chatRowId), ("$id", m.Id), ("$key", m.KeyId), ("$ts", m.Timestamp), ("$from_me", m.FromMe ? 1 : 0),
                    ("$sender", m.Sender), ("$type", m.MessageType), ("$text", m.Text), ("$caption", m.Caption),
                    ("$is_media", m.IsMedia ? 1 : 0), ("$media", m.MediaPath), ("$mime", m.Mime), ("$reply", m.ReplyKeyId),
                    ("$quoted", m.QuotedPreview),
                    ("$reactions", m.Reactions is null ? null : JsonSerializer.Serialize(m.Reactions)),
                    ("$system", m.IsSystem ? 1 : 0), ("$edited", m.Edited ? 1 : 0),
                    ("$revisions", m.Revisions is null ? null : JsonSerializer.Serialize(m.Revisions)),
                    ("$html", m.RawHtml));
                var rowId = (long)insertMessage.ExecuteScalar()!;
                if (m.Text is not null || m.Caption is not null)
                {
                    Bind(insertFts, ("$rowid", rowId), ("$text", m.Text), ("$caption", m.Caption), ("$sender", m.Sender));
                    insertFts.ExecuteNonQuery();
                }
            }
        }

        using var upsert = Command(tx, "INSERT OR REPLACE INTO files (path, size, mtime) VALUES ($path, $size, $mtime)");
        Bind(upsert, ("$path", file.RelativePath), ("$size", file.Size), ("$mtime", mtime));
        upsert.ExecuteNonQuery();
        tx.Commit();
    }

    private void DeleteFile(string path, SqliteTransaction tx)
    {
        using var cmd = Command(tx, """
            DELETE FROM messages_fts WHERE rowid IN
                (SELECT m.rowid FROM messages m JOIN chats c ON c.rowid = m.chat_rowid WHERE c.file = $path);
            DELETE FROM messages WHERE chat_rowid IN (SELECT rowid FROM chats WHERE file = $path);
            DELETE FROM chats WHERE file = $path;
            DELETE FROM files WHERE path = $path;
            """);
        Bind(cmd, ("$path", path));
        cmd.ExecuteNonQuery();
    }

    internal static string Preview(MessageRecord m)
    {
        var text = m.Text ?? m.Caption ?? (m.IsMedia ? MediaLabel(m.Mime) : "");
        return text.Length > 120 ? text[..120] : text;
    }

    private static string MediaLabel(string? mime) => (mime ?? "").Split('/')[0] switch
    {
        "image" => "📷",
        "video" => "🎥",
        "audio" => "🎤",
        _ => "📄",
    };

    /// <summary>Lower case without diacritics, for "muller" → "Müller" matching in the chat list.</summary>
    internal static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC)
            .Replace('ø', 'o').Replace('ß', 's').Replace('ı', 'i').Replace('æ', 'a').Replace('œ', 'o');
    }

    private static int? ReadSchemaVersion(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        try
        {
            return cmd.ExecuteScalar() is string v && int.TryParse(v, CultureInfo.InvariantCulture, out var n) ? n : null;
        }
        catch (SqliteException)
        {
            return null; // no meta table yet
        }
    }

    private static void Reset(SqliteConnection c)
    {
        var tables = new List<string>();
        using (var list = c.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'messages_fts_%'";
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }
        foreach (var table in tables)
        {
            Execute(c, $"DROP TABLE IF EXISTS \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");
        }
        Execute(c, IndexSchema.Create);
        Execute(c, $"INSERT INTO meta (key, value) VALUES ('schema_version', '{IndexSchema.Version}')");
    }

    private long Count(string sql)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    private SqliteCommand Command(SqliteTransaction tx, string sql)
    {
        var cmd = Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void Bind(SqliteCommand cmd, params (string Name, object? Value)[] values)
    {
        cmd.Parameters.Clear();
        foreach (var (name, value) in values)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static void Execute(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => Connection.Dispose();
}
