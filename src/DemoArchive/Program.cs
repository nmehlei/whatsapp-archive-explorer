namespace ArchiveExplorer.Demo;

public static class Program
{
    /// <summary>dotnet run --project src/DemoArchive -- &lt;target dir&gt; [--single-file] [--chats N] [--messages N]</summary>
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            Console.Error.WriteLine("usage: DemoArchive <target dir> [--single-file] [--chats N] [--messages N]");
            return 2;
        }

        var options = new DemoOptions(
            Chats: IntOption(args, "--chats") ?? 12,
            MessagesPerChat: IntOption(args, "--messages") ?? 200,
            PerChat: !args.Contains("--single-file"));
        var summary = DemoArchiveWriter.Write(args[0], options);
        Console.WriteLine($"demo archive: {summary.Chats} chats, {summary.Messages} messages, " +
                          $"{summary.MediaFiles} media files, {summary.VCards} vCards in {Path.GetFullPath(args[0])}");
        return 0;
    }

    private static int? IntOption(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var value) ? value : null;
    }
}
