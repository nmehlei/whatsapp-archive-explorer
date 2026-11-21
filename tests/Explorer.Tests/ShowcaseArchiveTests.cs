using System.Text.Json;
using System.Text.RegularExpressions;
using ArchiveExplorer.Contacts;
using ArchiveExplorer.Demo;

namespace ArchiveExplorer.Tests;

/// <summary>The scripted archive behind the README screenshots (one write, ~20 s: it paints a few hundred photos).</summary>
public sealed class ShowcaseArchiveTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wae-showcase-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Write_ProducesTheScriptedFictionalArchive()
    {
        var summary = ShowcaseArchiveWriter.Write(_root);

        var allowed = new Regex(@"^(447700900\d{3}@s\.whatsapp\.net|\d{6}-\d+@g\.us|000000000000000)$");
        var messages = 0;
        var chatsWithSearchWord = 0;
        var heroCount = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(_root, "json"), "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var chat in doc.RootElement.EnumerateObject())
            {
                Assert.Matches(allowed, chat.Name);
                var keys = new HashSet<string>();
                var found = false;
                var list = chat.Value.GetProperty("messages").EnumerateObject().Select(m => m.Value).ToList();
                foreach (var m in list)
                {
                    keys.Add(m.GetProperty("key_id").GetString()!);
                    var data = m.GetProperty("data").GetString() ?? "";
                    var caption = m.GetProperty("caption").ValueKind == JsonValueKind.String ? m.GetProperty("caption").GetString()! : "";
                    found |= (data + " " + caption).Contains(ShowcaseArchiveWriter.SearchWord, StringComparison.OrdinalIgnoreCase);
                    if (m.GetProperty("media").GetBoolean())
                    {
                        Assert.True(File.Exists(Path.Combine(_root, data)), data);
                    }
                }
                foreach (var m in list)
                {
                    if (m.GetProperty("reply").GetString() is { } reply)
                    {
                        Assert.Contains(reply, keys); // replies resolve within the chat
                    }
                }
                Assert.Equal(list.Select(m => m.GetProperty("timestamp").GetInt64()).Order(), list.Select(m => m.GetProperty("timestamp").GetInt64()));
                chatsWithSearchWord += found ? 1 : 0;
                messages += list.Count;
                if (chat.Value.GetProperty("name").GetString() == ShowcaseArchiveWriter.HeroChat)
                {
                    heroCount = list.Count;
                }
            }
        }

        Assert.Equal(summary.Messages, messages);
        Assert.True(heroCount > 20_000, $"the hero chat should feel like years of history, has {heroCount}");
        Assert.Equal(5, chatsWithSearchWord);
        var book = AddressBook.Load(Path.Combine(_root, "contacts.vcf"), null);
        Assert.Equal(11, book.Contacts);
        Assert.Equal(8, book.Photos);
    }
}
