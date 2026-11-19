namespace ArchiveExplorer.Index;

public static class IndexSchema
{
    /// <summary>Bump whenever tables, tokenizer or import rules change: older indexes are rebuilt.</summary>
    public const int Version = 2;

    public const string Create = """
        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE files (path TEXT PRIMARY KEY, size INTEGER NOT NULL, mtime INTEGER NOT NULL, has_conflicts INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE chats (
            rowid INTEGER PRIMARY KEY,
            jid TEXT NOT NULL UNIQUE,
            name TEXT,
            name_folded TEXT NOT NULL,
            kind TEXT NOT NULL,
            avatar TEXT,
            last_ts INTEGER,
            last_preview TEXT,
            message_count INTEGER NOT NULL,
            file TEXT NOT NULL);
        CREATE INDEX chats_file ON chats(file);
        CREATE TABLE messages (
            rowid INTEGER PRIMARY KEY,
            chat_rowid INTEGER NOT NULL,
            id TEXT NOT NULL,
            key_id TEXT,
            ts INTEGER NOT NULL,
            from_me INTEGER NOT NULL,
            sender TEXT,
            type INTEGER,
            text TEXT,
            caption TEXT,
            is_media INTEGER NOT NULL,
            media_path TEXT,
            mime TEXT,
            reply_key_id TEXT,
            quoted_preview TEXT,
            reactions_json TEXT,
            is_system INTEGER NOT NULL,
            edited INTEGER NOT NULL,
            revisions_json TEXT,
            raw_html TEXT);
        CREATE INDEX messages_chat_ts ON messages(chat_rowid, ts, rowid);
        CREATE INDEX messages_chat_key ON messages(chat_rowid, key_id);
        CREATE VIRTUAL TABLE messages_fts USING fts5(
            text, caption, sender,
            tokenize = 'unicode61 remove_diacritics 2',
            prefix = '2 3');
        """;
}
