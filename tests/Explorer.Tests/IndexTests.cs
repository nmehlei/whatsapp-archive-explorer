using ArchiveExplorer.Demo;
using ArchiveExplorer.Index;
using Microsoft.Data.Sqlite;

namespace ArchiveExplorer.Tests;

public sealed class IndexTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wae-index-").FullName;
    private string Archive => Path.Combine(_dir, "archive");
    private string Db => Path.Combine(_dir, "cache", "index.sqlite");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static long Scalar(IndexStore store, string sql)
    {
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Index_BuildsFromDemo()
    {
        var summary = DemoArchiveWriter.Write(Archive, new DemoOptions());
        using var store = IndexStore.Open(Archive, Db);

        var result = store.Refresh(null);

        Assert.Equal(summary.Chats, result.Chats);
        Assert.Equal(summary.Messages, result.Messages);
        Assert.Equal(summary.Messages, Scalar(store, "SELECT count(*) FROM messages"));
        Assert.Equal(0, Scalar(store, "SELECT count(*) FROM chats c WHERE c.last_ts <> (SELECT max(ts) FROM messages m WHERE m.chat_rowid = c.rowid)"));
        Assert.Equal(1, Scalar(store, $"SELECT count(*) FROM messages_fts WHERE messages_fts MATCH '\"{DemoArchiveWriter.UniquePhrase}\"'"));
    }

    [Fact]
    public void Index_IncrementalRefresh()
    {
        DemoArchiveWriter.Write(Archive, new DemoOptions());
        using (var first = IndexStore.Open(Archive, Db))
        {
            first.Refresh(null);
        }

        using var store = IndexStore.Open(Archive, Db);
        Assert.Equal(0, store.Refresh(null).FilesIndexed);

        var files = Directory.GetFiles(Path.Combine(Archive, "json")).Order(StringComparer.Ordinal).ToArray();
        File.SetLastWriteTimeUtc(files[1], DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(1, store.Refresh(null).FilesIndexed);

        File.Delete(files[2]);
        var removed = store.Refresh(null);
        Assert.Equal(1, removed.FilesRemoved);
        Assert.Equal(files.Length - 1, removed.Chats);

        File.WriteAllText(Path.Combine(Archive, "json", "new.json"),
            """{ "4915550199999@s.whatsapp.net": { "name": "New", "messages": { "1": { "from_me": true, "timestamp": 1700000000, "data": "hello new" } } } }""");
        var added = store.Refresh(null);
        Assert.Equal(1, added.FilesIndexed);
        Assert.Equal(files.Length, added.Chats);
        Assert.Equal(1, Scalar(store, "SELECT count(*) FROM messages_fts WHERE messages_fts MATCH 'new'"));
    }

    [Fact]
    public void Index_RenamedFileKeepsItsChats()
    {
        DemoArchiveWriter.Write(Archive, new DemoOptions(Chats: 4, MessagesPerChat: 10));
        using var store = IndexStore.Open(Archive, Db);
        var before = store.Refresh(null);
        var file = Directory.GetFiles(Path.Combine(Archive, "json")).Order(StringComparer.Ordinal).Last();

        File.Move(file, Path.Combine(Archive, "json", "zz-renamed-contact.json"));
        var result = store.Refresh(null);

        Assert.Equal(before.Chats, result.Chats);
        Assert.Equal(before.Messages, result.Messages);
    }

    [Fact]
    public void Index_DuplicateChatTakenOverWhenWinnerDisappears()
    {
        Directory.CreateDirectory(Path.Combine(Archive, "json"));
        const string chat = """{ "4915550100001@s.whatsapp.net": { "name": "A", "messages": { "1": { "from_me": true, "timestamp": 1700000000, "data": "{0}" } } } }""";
        File.WriteAllText(Path.Combine(Archive, "json", "a.json"), chat.Replace("{0}", "from a", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(Archive, "json", "b.json"), chat.Replace("{0}", "from b", StringComparison.Ordinal));
        using var store = IndexStore.Open(Archive, Db);
        Assert.Equal(1, store.Refresh(null).Chats);

        File.Delete(Path.Combine(Archive, "json", "a.json"));
        var result = store.Refresh(null);

        Assert.Equal(1, result.Chats);
        Assert.Equal(1, Scalar(store, "SELECT count(*) FROM messages_fts WHERE messages_fts MATCH '\"from b\"'"));
    }

    [Fact]
    public void Index_SchemaVersionMismatchRebuilds()
    {
        DemoArchiveWriter.Write(Archive, new DemoOptions(Chats: 3, MessagesPerChat: 10));
        using (var first = IndexStore.Open(Archive, Db))
        {
            first.Refresh(null);
            using var cmd = first.Connection.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = '-1' WHERE key = 'schema_version'";
            cmd.ExecuteNonQuery();
        }

        using var store = IndexStore.Open(Archive, Db);
        Assert.Equal(3, store.Refresh(null).FilesIndexed);
    }

    [Fact]
    public void Index_DoesNotWriteIntoArchive()
    {
        DemoArchiveWriter.Write(Archive, new DemoOptions(Chats: 4, MessagesPerChat: 20));
        var before = Snapshot(Archive);

        using (var store = IndexStore.Open(Archive, Db))
        {
            store.Refresh(null);
        }

        Assert.Equal(before, Snapshot(Archive));
    }

    [Fact]
    public void Index_InMemoryWhenNoPath()
    {
        DemoArchiveWriter.Write(Archive, new DemoOptions(Chats: 3, MessagesPerChat: 10));
        using var store = IndexStore.Open(Archive, null);

        Assert.Equal(3, store.Refresh(null).Chats);
        Assert.False(Directory.Exists(Path.GetDirectoryName(Db)));
    }

    [Fact]
    public void CachePaths_AreStablePerArchiveAndOutsideIt()
    {
        var a = CachePaths.ForArchive(Archive);
        var b = CachePaths.ForArchive(Archive + Path.DirectorySeparatorChar);

        Assert.Equal(a, b);
        Assert.False(a.StartsWith(Path.GetFullPath(Archive), StringComparison.Ordinal));
        Assert.EndsWith(".sqlite", a, StringComparison.Ordinal);
    }

    private static string Snapshot(string root) => string.Join('\n',
        Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(p => $"{Path.GetRelativePath(root, p)}|{File.GetLastWriteTimeUtc(p):O}|{(File.Exists(p) ? new FileInfo(p).Length : -1)}"));
}
