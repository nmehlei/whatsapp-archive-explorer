using System.Net;
using ArchiveExplorer.Contacts;
using ArchiveExplorer.Index;
using ArchiveExplorer.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;

namespace ArchiveExplorer.Tests;

public sealed class ContactsIntegrationTests : IAsyncLifetime
{
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC";

    private readonly string _dir = Directory.CreateTempSubdirectory("wae-contacts-int-").FullName;
    private string Archive => Path.Combine(_dir, "archive");
    private string Vcf => Path.Combine(_dir, "contacts.vcf");
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Archive, "json"));
        File.WriteAllText(Path.Combine(Archive, "json", "a.json"), """
            { "4915550100001@s.whatsapp.net": { "name": null, "messages": {
                "1": { "from_me": false, "timestamp": 1700000001, "data": "hello", "reactions": { "+49 155 50100002": "❤️" } } } },
              "120363-1@g.us": { "name": "Climbing crew", "messages": {
                "1": { "from_me": false, "timestamp": 1700000002, "data": "group hello", "sender": "+49 155 50100002" } } } }
            """);
        File.WriteAllText(Vcf, $"""
            BEGIN:VCARD
            VERSION:3.0
            FN:Jürgen Müller
            TEL:0155 50100001
            PHOTO;ENCODING=b;TYPE=PNG:{TinyPngBase64}
            END:VCARD
            BEGIN:VCARD
            VERSION:3.0
            FN:Anna Becker
            TEL:+4915550100002
            END:VCARD
            """);

        _app = ExplorerHost.Create(new HostSettings(Archive, DbPath: null, ContactsPath: Vcf, CountryCode: "49"),
            builder => builder.WebHost.UseTestServer());
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Host = "localhost";
        await ExplorerHost.WaitUntilReadyAsync(_app, TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private async Task<System.Text.Json.JsonElement> Json(string url) =>
        System.Text.Json.JsonDocument.Parse(await _client.GetStringAsync(url)).RootElement;

    [Fact]
    public async Task ChatList_UsesContactNamesAndPhotos_AndFiltersByThem()
    {
        var chats = await Json("/api/chats");
        var private1 = chats.EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "private");

        Assert.Equal("Jürgen Müller", private1.GetProperty("name").GetString());
        Assert.True(private1.GetProperty("hasAvatar").GetBoolean());
        Assert.Equal(1, (await Json("/api/chats?q=muller")).GetArrayLength());

        using var avatar = await _client.GetAsync($"/api/chats/{private1.GetProperty("id").GetInt64()}/avatar");
        Assert.Equal(HttpStatusCode.OK, avatar.StatusCode);
        Assert.Equal("image/png", avatar.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Messages_ResolveSenderAndReactionNames()
    {
        var chats = (await Json("/api/chats")).EnumerateArray().ToList();
        var group = chats.Single(c => c.GetProperty("kind").GetString() == "group").GetProperty("id").GetInt64();
        var private1 = chats.Single(c => c.GetProperty("kind").GetString() == "private").GetProperty("id").GetInt64();

        var groupMessage = (await Json($"/api/chats/{group}/messages")).GetProperty("items")[0];
        var privateMessage = (await Json($"/api/chats/{private1}/messages")).GetProperty("items")[0];

        Assert.Equal("Anna Becker", groupMessage.GetProperty("sender").GetString());
        Assert.True(privateMessage.GetProperty("reactions").TryGetProperty("Anna Becker", out _));
    }

    [Fact]
    public async Task Search_NamesTheChatAndSender()
    {
        var hits = await Json("/api/search?q=hello");

        var names = hits.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("chatName").GetString()).ToList();
        Assert.Contains("Jürgen Müller", names);
        Assert.Contains(hits.GetProperty("hits").EnumerateArray(), h => h.GetProperty("sender").GetString() == "Anna Becker");
    }

    [Fact]
    public void WithoutContactsNothingChanges()
    {
        var store = IndexStore.Open(Archive, null);
        store.Refresh(null);
        var chats = new Queries(store.Connection, AddressBook.Empty).ListChats(null);

        Assert.Null(chats.Single(c => c.Kind == "private").Name);
        store.Dispose();
    }
}
