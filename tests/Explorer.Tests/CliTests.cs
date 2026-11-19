using ArchiveExplorer.Cli;

namespace ArchiveExplorer.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wae-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Parse_RequiresArchive()
    {
        var result = Options.Parse([]);

        Assert.Null(result.Options);
        Assert.Contains("--archive", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PositionalArchive_AndFlags()
    {
        var result = Options.Parse([_dir, "--port", "8123", "--no-browser", "--no-cache", "--verbose"]);

        var o = Assert.IsType<Options>(result.Options);
        Assert.Equal(_dir, o.Archive);
        Assert.Equal(8123, o.Port);
        Assert.True(o.NoBrowser);
        Assert.True(o.NoCache);
        Assert.True(o.Verbose);
        Assert.Null(o.Listen);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_ListenPrintsWarning()
    {
        var result = Options.Parse(["--archive", _dir, "--listen", "0.0.0.0"]);

        Assert.Equal("0.0.0.0", result.Options!.Listen);
        Assert.Contains(result.Warnings, w => w.Contains("network", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("--port", "abc")]
    [InlineData("--port", "70000")]
    [InlineData("--listen", "not-an-ip")]
    [InlineData("--bogus", "x")]
    public void Parse_RejectsBadValues(string name, string value)
    {
        var result = Options.Parse(["--archive", _dir, name, value]);

        Assert.Null(result.Options);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_HelpAndVersion()
    {
        Assert.True(Options.Parse(["--help"]).Options!.ShowHelp);
        Assert.True(Options.Parse(["--version"]).Options!.ShowVersion);
    }

    [Fact]
    public async Task Startup_RejectsFolderWithoutExport()
    {
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "private words");
        var err = new StringWriter();

        var code = await Program.RunAsync(["--archive", _dir, "--no-browser", "--no-cache"], TextWriter.Null, err);

        Assert.Equal(2, code);
        Assert.Contains("WhatsApp-Chat-Exporter", err.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private words", err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_RejectsMissingFolder()
    {
        var err = new StringWriter();

        var code = await Program.RunAsync(["--archive", Path.Combine(_dir, "nope"), "--no-browser"], TextWriter.Null, err);

        Assert.Equal(2, code);
    }
}
