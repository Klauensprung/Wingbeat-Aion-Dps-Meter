using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AionDpsMeter.Local;

internal static class AppDataPaths
{
    internal static string Root { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wingbeat");
    internal static string FilePath(string name) => Path.Combine(Root, name);

    internal static void Initialize(bool isolated)
    {
        if (isolated) Root = Path.Combine(AppContext.BaseDirectory, "test-artifacts", "user-data");
        Directory.CreateDirectory(Root);
        if (!isolated) Migrate(Path.Combine(AppContext.BaseDirectory, "local-data"), Root);
    }

    // Copy once per legacy installation. Keep originals and prefer existing user data.
    internal static void Migrate(string legacy, string destination)
    {
        legacy = Path.GetFullPath(legacy); destination = Path.GetFullPath(destination);
        if (!Directory.Exists(legacy) || string.Equals(legacy, destination, StringComparison.OrdinalIgnoreCase)) return;
        if ((File.GetAttributes(legacy) & FileAttributes.ReparsePoint) != 0) return;
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacy.ToUpperInvariant())));
        string marker = Path.Combine(destination, "migrations", id + ".done");
        if (File.Exists(marker)) return;
        CopyDirectory(legacy, destination);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "Completed");
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string path in Directory.EnumerateFileSystemEntries(source)) {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
            string target = Path.Combine(destination, Path.GetFileName(path));
            if ((attributes & FileAttributes.Directory) != 0) { CopyDirectory(path, target); continue; }
            if (File.Exists(target) || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            string temporary = target + ".migration-" + Guid.NewGuid().ToString("N");
            try {
                File.Copy(path, temporary);
                if (!File.Exists(target)) File.Move(temporary, target);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
