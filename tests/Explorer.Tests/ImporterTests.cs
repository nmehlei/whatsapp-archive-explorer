using ArchiveExplorer.Demo;
using ArchiveExplorer.Import;

namespace ArchiveExplorer.Tests;

public sealed class ImporterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wae-import-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private List<ChatRecord> ReadAll(ImportStats stats) =>
        ExportReader.DiscoverFiles(_root).SelectMany(f => ExportReader.ReadFile(_root, f, stats)).ToList();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Importer_ReadsDemoArchive(bool perChat)
    {
        var summary = DemoArchiveWriter.Write(_root, new DemoOptions(PerChat: perChat));
        var stats = new ImportStats();

        var chats = ReadAll(stats);

        Assert.Equal(summary.Chats, chats.Count);
        Assert.Equal(summary.Messages, chats.Sum(c => c.Messages.Count));
        Assert.Equal(0, stats.SkippedMessages);
        Assert.Contains(chats, c => c.Kind == "calls" && c.Jid == DemoArchiveWriter.CallsJid);
        var groups = chats.Where(c => c.Kind == "group").ToList();
        Assert.NotEmpty(groups);
        Assert.Contains(groups.SelectMany(g => g.Messages), m => m.Sender is not null && !m.FromMe);
        var all = chats.SelectMany(c => c.Messages).ToList();
        Assert.Contains(all, m => m.ReplyKeyId is not null && m.QuotedPreview is not null);
        Assert.Contains(all, m => m.IsMedia && m.MediaPath!.StartsWith("WhatsApp/Media/", StringComparison.Ordinal));
        Assert.Contains(all, m => m.RawHtml is not null && m.Text is not null && !m.Text.Contains('<'));
        Assert.Contains(all, m => m.IsSystem);
        var revised = Assert.Single(all.Where(m => m.Revisions is { Count: > 0 }).Take(1));
        Assert.Equal("This message was deleted", revised.Revisions![0].Text);
        Assert.Contains(all, m => m.Id.Contains('~'));
        Assert.Contains(chats, c => c.AvatarPath is not null);
    }

    [Fact]
    public void Importer_TolerantOfMissingAndUnknownFields()
    {
        Directory.CreateDirectory(Path.Combine(_root, "json"));
        File.WriteAllText(Path.Combine(_root, "json", "a.json"), """
            {
              "4915550100001@s.whatsapp.net": { "name": "Empty", "messages": null, "future_field": 1 },
              "120363-1@g.us": {
                "name": null,
                "messages": {
                  "1": { "from_me": false, "timestamp": 1500000000, "data": "ok", "brand_new": [1, 2] },
                  "2": { "from_me": true, "data": "no timestamp" },
                  "3": "not an object",
                  "4": { "timestamp": 1500000100, "data": null, "media": true }
                }
              }
            }
            """);
        var stats = new ImportStats();

        var chats = ReadAll(stats);

        Assert.Equal(2, chats.Count);
        Assert.Empty(chats.Single(c => c.Jid.StartsWith("49155501", StringComparison.Ordinal)).Messages);
        var group = chats.Single(c => c.Kind == "group");
        Assert.Equal(["1", "4"], group.Messages.Select(m => m.Id));
        Assert.Null(group.Messages[1].MediaPath);
        Assert.Equal(2, stats.SkippedMessages);
    }

    [Fact]
    public void Importer_NullAndWrongTypedValuesAreTolerated()
    {
        Directory.CreateDirectory(Path.Combine(_root, "json"));
        File.WriteAllText(Path.Combine(_root, "json", "a.json"), """
            { "4915550100001@s.whatsapp.net": { "name": 5, "their_avatar": false, "messages": {
                "1": { "from_me": "yes", "timestamp": 1500000000, "message_type": null, "data": 7, "reactions": [],
                       "revisions": {}, "media": 1, "key_id": null, "sender": [], "edited": null },
                "2": { "from_me": true, "timestamp": 1500000001, "message_type": "7", "data": "x", "reactions": { "A": 1 } } } } }
            """);
        var stats = new ImportStats();

        var chat = ReadAll(stats).Single();

        Assert.Equal(2, chat.Messages.Count);
        Assert.Null(chat.Name);
        Assert.Null(chat.Messages[0].MessageType);
        Assert.Null(chat.Messages[1].Reactions);
        Assert.Equal(0, stats.SkippedMessages);
    }

    [Fact]
    public void Importer_KeepsZeroAndClampsImplausibleTimestamps()
    {
        Directory.CreateDirectory(Path.Combine(_root, "json"));
        File.WriteAllText(Path.Combine(_root, "json", "a.json"), """
            { "4915550100001@s.whatsapp.net": { "name": "A", "messages": {
                "1": { "from_me": false, "timestamp": 0, "data": "epoch" },
                "2": { "from_me": false, "timestamp": 1500000000123456, "data": "microseconds" },
                "3": { "from_me": false, "timestamp": "Infinity", "data": "nonsense" } } } }
            """);
        var stats = new ImportStats();

        var messages = ReadAll(stats).Single().Messages;

        Assert.Equal(3, messages.Count); // never lose a message over its timestamp
        Assert.Equal(0, stats.SkippedMessages);
        Assert.All(messages, m => Assert.InRange(m.Timestamp, 0, 32503680000)); // <= year 3000
        Assert.Contains(messages, m => m.Timestamp == 1500000000);
    }

    [Theory]
    [InlineData("line one<br>line two", "line one\nline two")]
    [InlineData("if a <b and c", "if a <b and c")]
    [InlineData("<i>not markup</i> &amp; friends", "<i>not markup</i> &amp; friends")]
    public void Importer_PlainTextStaysText(string data, string expected)
    {
        Directory.CreateDirectory(Path.Combine(_root, "json"));
        File.WriteAllText(Path.Combine(_root, "json", "a.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["4915550100001@s.whatsapp.net"] = new { name = "A", messages = new Dictionary<string, object> { ["1"] = new { from_me = false, timestamp = 1500000000, data } } },
        }));

        var message = ReadAll(new ImportStats()).Single().Messages.Single();

        Assert.Null(message.RawHtml);
        Assert.Equal(expected.Replace("\\n", "\n", StringComparison.Ordinal), message.Text);
    }

    [Fact]
    public void Importer_NormalisesMillisecondTimestamps()
    {
        Directory.CreateDirectory(Path.Combine(_root, "json"));
        File.WriteAllText(Path.Combine(_root, "json", "a.json"), """
            { "4915550100001@s.whatsapp.net": { "name": "A", "messages": {
                "1": { "from_me": false, "timestamp": 1500000000123, "data": "ms" } } } }
            """);

        var message = ReadAll(new ImportStats()).Single().Messages.Single();

        Assert.Equal(1500000000, message.Timestamp);
    }

    [Fact]
    public void Importer_BrokenJsonFileIsSkippedNotFatal()
    {
        Directory.CreateDirectory(Path.Combine(_root, "json"));
        File.WriteAllText(Path.Combine(_root, "json", "broken.json"), "{ \"x\": ");
        File.WriteAllText(Path.Combine(_root, "json", "good.json"),
            """{ "4915550100001@s.whatsapp.net": { "name": "A", "messages": {} } }""");
        var stats = new ImportStats();

        var chats = ReadAll(stats);

        Assert.Single(chats);
        Assert.Equal(1, stats.BrokenFiles);
    }
}
