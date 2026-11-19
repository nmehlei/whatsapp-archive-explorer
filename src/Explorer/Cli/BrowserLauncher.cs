using System.Diagnostics;

namespace ArchiveExplorer.Cli;

public static class BrowserLauncher
{
    public static bool TryOpen(string url)
    {
        try
        {
            var start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd", ["/c", "start", "", url]) { CreateNoWindow = true }
                : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", [url]);
            start.UseShellExecute = false;
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
