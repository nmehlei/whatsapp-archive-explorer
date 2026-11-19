namespace ArchiveExplorer.Tests;

public class ScaffoldTests
{
    [Fact]
    public void Program_Exits_Zero() => Assert.Equal(0, ArchiveExplorer.Program.Main([]));
}
