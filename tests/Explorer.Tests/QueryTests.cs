using ArchiveExplorer.Demo;
using ArchiveExplorer.Index;
using Microsoft.Data.Sqlite;

namespace ArchiveExplorer.Tests;

public sealed class QueryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wae-query-").FullName;
    private readonly IndexStore _store;
    private readonly Queries _q;

    public QueryTests()
    {
        DemoArchiveWriter.Write(_dir, new DemoOptions());
        _store = IndexStore.Open(_dir, null);
        _store.Refresh(null);
        _q = new Queries(_store.Connection);
    }

    public void Dispose()
    {
        _store.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private ChatSummary BusiestPrivateChat() =>
        _q.ListChats(null).Where(c => c.Kind == "private").MaxBy(c => c.MessageCount)!;

    [Fact]
    public void ListChats_NewestFirst_AndFilterIgnoresDiacritics()
    {
        var chats = _q.ListChats(null);

        Assert.Equal(12, chats.Count);
        Assert.Equal(chats.OrderByDescending(c => c.LastTs).Select(c => c.Id), chats.Select(c => c.Id));
        Assert.Equal("Jürgen Müller", Assert.Single(_q.ListChats("muller")).Name);
        Assert.Equal("Jürgen Müller", Assert.Single(_q.ListChats("JÜRG")).Name);
        Assert.Single(_q.ListChats("4915550100005"));
        Assert.Empty(_q.ListChats("nobody-here"));
    }

    [Fact]
    public void Messages_DefaultPageIsNewest()
    {
        var chat = BusiestPrivateChat();

        var page = _q.Messages(chat.Id, null, null, null, null, 20);

        Assert.Equal(20, page.Items.Count);
        Assert.True(page.HasMoreBefore);
        Assert.False(page.HasMoreAfter);
        Assert.Equal(chat.LastTs, page.Items[^1].Ts);
        Assert.Equal(page.Items.OrderBy(m => m.Ts).ThenBy(m => m.Id).Select(m => m.Id), page.Items.Select(m => m.Id));
    }

    [Fact]
    public void Messages_BeforeAndAfterPage()
    {
        var chat = BusiestPrivateChat();
        var newest = _q.Messages(chat.Id, null, null, null, null, 20);

        var older = _q.Messages(chat.Id, newest.Items[0].Id, null, null, null, 20);
        var back = _q.Messages(chat.Id, null, older.Items[^1].Id, null, null, 20);

        Assert.True(older.Items[^1].Id < newest.Items[0].Id || older.Items[^1].Ts <= newest.Items[0].Ts);
        Assert.DoesNotContain(older.Items, m => newest.Items.Any(n => n.Id == m.Id));
        Assert.Equal(newest.Items.Select(m => m.Id), back.Items.Select(m => m.Id));
    }

    [Fact]
    public void Messages_AroundIdReturnsBothSides()
    {
        var chat = BusiestPrivateChat();
        var middle = _q.Messages(chat.Id, null, null, null, null, 200).Items[100];

        var page = _q.Messages(chat.Id, null, null, middle.Id, null, 21);

        Assert.Contains(page.Items, m => m.Id == middle.Id);
        Assert.True(page.HasMoreBefore);
        Assert.True(page.HasMoreAfter);
        Assert.InRange(page.Items.ToList().FindIndex(m => m.Id == middle.Id), 5, 15);
    }

    [Fact]
    public void Messages_AroundDate()
    {
        var chat = BusiestPrivateChat();
        var target = _q.Messages(chat.Id, null, null, null, null, 200).Items[50];

        var page = _q.Messages(chat.Id, null, null, null, target.Ts, 21);

        Assert.Contains(page.Items, m => m.Id == target.Id);
    }

    [Fact]
    public void Messages_ResolvesReplyToIdByKeyId_AndMapsExtras()
    {
        var all = _q.ListChats(null).SelectMany(c => _q.Messages(c.Id, null, null, null, null, 1000).Items).ToList();

        var reply = all.First(m => m.ReplyKeyId is not null);
        Assert.Equal(all.Single(m => m.KeyId == reply.ReplyKeyId).Id, reply.ReplyToId);
        var image = all.First(m => m.Mime == "image/png");
        Assert.StartsWith("/media/WhatsApp/Media/WhatsApp%20Images/", image.MediaUrl, StringComparison.Ordinal);
        var card = all.First(m => m.Html is not null);
        Assert.Contains("href=\"/media/WhatsApp/vCards/", card.Html, StringComparison.Ordinal);
        Assert.Equal("This message was deleted", all.First(m => m.Revisions is { Count: > 0 }).Revisions![0].Text);
        Assert.Contains(all, m => m.Reactions is { Count: > 0 });
    }

    [Fact]
    public void Search_FindsAcrossChats_WithSnippetAndFilters()
    {
        var (hits, total) = _q.Search("Lighthouse", null, null, null, null, null, 0, 50);

        var hit = Assert.Single(hits);
        Assert.Equal(1, total);
        Assert.Contains("\u0002lighthouse\u0003", hit.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_q.Search("lightho", null, null, null, null, null, 0, 50).Hits); // prefix
        Assert.Single(_q.Search("lighthouse", hit.ChatId, null, null, null, null, 0, 50).Hits);
        var other = _q.ListChats(null).First(c => c.Id != hit.ChatId).Id;
        Assert.Empty(_q.Search("lighthouse", other, null, null, null, null, 0, 50).Hits);
        Assert.Empty(_q.Search("lighthouse", null, null, hit.Ts + 1, null, null, 0, 50).Hits);
        Assert.Empty(_q.Search("lighthouse", null, null, null, null, true, 0, 50).Hits);

        var greet = _q.Search("munchen", null, null, null, null, null, 0, 50); // "München" without diacritics
        Assert.True(greet.Total > 0);
        Assert.True(_q.Search("good", null, null, null, null, null, 0, 10).Total > 10);
    }

    [Fact]
    public void SearchInChat_ReturnsIdsOldestFirst()
    {
        var chat = BusiestPrivateChat();

        var ids = _q.SearchInChat(chat.Id, "good");

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Order(), ids);
    }

    [Fact]
    public void Media_ListsChatMediaNewestFirst_WithPaging()
    {
        var chat = BusiestPrivateChat();

        var first = _q.Media(chat.Id, 0, 5);
        var all = _q.Media(chat.Id, 0, 500);

        Assert.NotEmpty(all.Items);
        Assert.All(all.Items, m => Assert.StartsWith("/media/", m.Url, StringComparison.Ordinal));
        Assert.Equal(all.Items.OrderByDescending(m => m.Ts).ThenByDescending(m => m.Id).Select(m => m.Id), all.Items.Select(m => m.Id));
        Assert.Equal(all.Items.Take(5).Select(m => m.Id), first.Items.Select(m => m.Id));
        Assert.Equal(all.Items.Count, first.Total);
    }

    [Theory]
    [InlineData("3.5", 1)]
    [InlineData("anna.becker@example.org", 1)]
    [InlineData("https://example.org/path?a=1", 1)]
    [InlineData("example.org", 2)]
    [InlineData("e.g.", 1)]
    [InlineData("❤️", 0)] // emoji are not searchable (documented)
    public void Search_TermsWithPunctuationMatchAsPhrases(string q, int expected)
    {
        var dir = Directory.CreateTempSubdirectory("wae-punct-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "json"));
            File.WriteAllText(Path.Combine(dir, "json", "a.json"), """
                { "4915550100001@s.whatsapp.net": { "name": "A", "messages": {
                    "1": { "from_me": false, "timestamp": 1500000001, "data": "Version 3.5 is out, e.g. today ❤️" },
                    "2": { "from_me": false, "timestamp": 1500000002, "data": "Mail anna.becker@example.org please" },
                    "3": { "from_me": false, "timestamp": 1500000003, "data": "See https://example.org/path?a=1 for details" } } } }
                """);
            using var store = IndexStore.Open(dir, null);
            store.Refresh(null);

            Assert.Equal(expected, new Queries(store.Connection).Search(q, null, null, null, null, null, 0, 10).Total);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("*")]
    [InlineData("NEAR(")]
    [InlineData("-")]
    [InlineData("a OR")]
    [InlineData("(")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("col:value")]
    public void Search_SpecialCharactersDoNotThrow(string q)
    {
        var (hits, _) = _q.Search(q, null, null, null, null, null, 0, 10);
        Assert.NotNull(hits);
        Assert.NotNull(_q.SearchInChat(BusiestPrivateChat().Id, q));
    }
}
