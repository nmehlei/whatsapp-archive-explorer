using System.Security.Cryptography;
using System.Text;

namespace ArchiveExplorer.Index;

/// <summary>Where the index of an archive lives: the user's cache folder, never inside the archive.</summary>
public static class CachePaths
{
    public const string AppFolder = "whatsapp-archive-explorer";

    public static string BaseDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder);
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Caches", AppFolder);
        }
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".cache") : xdg, AppFolder);
    }

    /// <summary>Index file for an archive: named by a hash of its canonical path (the path itself is not stored in the name).</summary>
    public static string ForArchive(string archiveRoot)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(archiveRoot));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return Path.Combine(BaseDirectory(), $"{hash[..32]}.sqlite");
    }

    /// <summary>Creates the folder readable only by the current user.</summary>
    public static void EnsurePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
