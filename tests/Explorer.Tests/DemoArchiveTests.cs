using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArchiveExplorer.Demo;

namespace ArchiveExplorer.Tests;

public sealed class DemoArchiveTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wae-demo-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Write_ProducesDeterministicExport()
    {
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");

        var summary = DemoArchiveWriter.Write(a, new DemoOptions());
        DemoArchiveWriter.Write(b, new DemoOptions());

        Assert.Equal(Hashes(a), Hashes(b));
        Assert.Equal(summary.Chats, Directory.GetFiles(Path.Combine(a, "json"), "*.json").Length);
        var messages = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(a, "json"), "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var chat in doc.RootElement.EnumerateObject())
            {
                messages += chat.Value.GetProperty("messages").EnumerateObject().Count();
            }
        }
        Assert.Equal(summary.Messages, messages);
        Assert.Equal(summary.VCards, Directory.GetFiles(Path.Combine(a, "WhatsApp", "vCards")).Length);
        var media = Directory.GetFiles(Path.Combine(a, "WhatsApp", "Media"), "*", SearchOption.AllDirectories).Length;
        Assert.Equal(summary.MediaFiles, media);
    }

    [Fact]
    public void Write_UsesOnlyFictionalIdentities()
    {
        DemoArchiveWriter.Write(_root, new DemoOptions());
        var allowed = new Regex(@"^(49155501\d{5}@s\.whatsapp\.net|\d{6}-\d+@g\.us|000000000000000)$");

        foreach (var file in Directory.GetFiles(Path.Combine(_root, "json"), "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var chat in doc.RootElement.EnumerateObject())
            {
                Assert.Matches(allowed, chat.Name);
            }
        }
    }

    [Fact]
    public void Write_SingleFileLayout()
    {
        var summary = DemoArchiveWriter.Write(_root, new DemoOptions(PerChat: false));

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "result.json")));
        Assert.Equal(summary.Chats, doc.RootElement.EnumerateObject().Count());
    }

    private static Dictionary<string, string> Hashes(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(root, f),
            f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
}
