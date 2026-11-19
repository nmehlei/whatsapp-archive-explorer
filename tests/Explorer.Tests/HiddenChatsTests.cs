using ArchiveExplorer.Index;
using Microsoft.Data.Sqlite;

namespace ArchiveExplorer.Tests;

/// <summary>WhatsApp keeps "hidden" chats (group-member side entries, channels, …) that its own chat list never shows.</summary>
public sealed class HiddenChatsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wae-hidden-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private void WriteExport()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "json"));
        string Chat(string jid, string text) =>
            $$"""{ "{{jid}}": { "name": null, "messages": { "1": { "from_me": false, "timestamp": 1700000000, "data": "{{text}}" } } } }""";
        File.WriteAllText(Path.Combine(_dir, "json", "a.json"), Chat("4915550100001@s.whatsapp.net", "visible chat"));
        File.WriteAllText(Path.Combine(_dir, "json", "b.json"), Chat("60123456789@s.whatsapp.net", "hidden member entry"));
        File.WriteAllText(Path.Combine(_dir, "json", "c.json"), Chat("4915550100003@s.whatsapp.net", "visible through its lid chat"));

        using var db = new SqliteConnection($"Data Source={Path.Combine(_dir, "msgstore.db")};Pooling=false");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE jid (_id INTEGER PRIMARY KEY, raw_string TEXT);
            CREATE TABLE chat (_id INTEGER PRIMARY KEY, jid_row_id INTEGER, hidden INTEGER);
            CREATE TABLE jid_map (lid_row_id INTEGER, jid_row_id INTEGER);
            INSERT INTO jid VALUES (1, '4915550100001@s.whatsapp.net'), (2, '60123456789@s.whatsapp.net'),
                                   (3, '4915550100003@s.whatsapp.net'), (4, '999@lid');
            INSERT INTO chat VALUES (10, 1, 0), (20, 2, 1), (30, 3, 1), (40, 4, 0);
            INSERT INTO jid_map VALUES (4, 3);
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void HiddenChatsAreLeftOutOfTheListUnlessAsked()
    {
        WriteExport();
        using var store = IndexStore.Open(_dir, null);
        store.Refresh(null);
        var q = new Queries(store.Connection);

        var visible = q.ListChats(null);
        var all = q.ListChats(null, includeHidden: true);

        Assert.Equal(["4915550100001@s.whatsapp.net", "4915550100003@s.whatsapp.net"], visible.Select(c => c.Jid).Order(StringComparer.Ordinal));
        Assert.Equal(3, all.Count);
        Assert.True(all.Single(c => c.Jid.StartsWith("60", StringComparison.Ordinal)).Hidden);
        Assert.Equal(1, q.CountHidden());
        Assert.Equal(1, q.Search("member", null, null, null, null, null, 0, 10).Total); // still searchable
    }

    [Fact]
    public void WithoutMsgstoreNothingIsHidden()
    {
        WriteExport();
        File.Delete(Path.Combine(_dir, "msgstore.db"));
        using var store = IndexStore.Open(_dir, null);
        store.Refresh(null);

        Assert.Equal(3, new Queries(store.Connection).ListChats(null).Count);
    }

    [Fact]
    public void MsgstoreIsOnlyRead()
    {
        WriteExport();
        var path = Path.Combine(_dir, "msgstore.db");
        var before = (File.GetLastWriteTimeUtc(path), new FileInfo(path).Length);

        using (var store = IndexStore.Open(_dir, null))
        {
            store.Refresh(null);
        }

        Assert.Equal(before, (File.GetLastWriteTimeUtc(path), new FileInfo(path).Length));
        Assert.False(File.Exists(path + "-wal"));
        Assert.False(File.Exists(path + "-journal"));
    }
}
