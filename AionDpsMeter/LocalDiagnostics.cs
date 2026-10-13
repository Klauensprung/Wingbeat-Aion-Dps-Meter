using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

public static class LocalLog
{
    static readonly object Gate = new();
    public static string DirectoryPath => AppDataPaths.FilePath("logs");
    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length >= 2 * 1024 * 1024) File.Move(path, path + ".1", true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message}\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
        }
    }
}

public static class SkillAudit
{
    public static object Report(Fight fight, SkillNames names)
    {
        var groups = fight.Damage.Concat(fight.Healing).GroupBy(e => e.SkillCode).OrderBy(g => g.Key).ToArray();
        return new
        {
            fight.Id, GeneratedUtc = DateTimeOffset.UtcNow, CatalogEntries = names.Count, names.LoadError,
            DamageEvents = fight.Damage.Count, HealingEvents = fight.Healing.Count, NetworkEvents = fight.Network.Count,
            DistinctSkills = groups.Length,
            ResolvedSkills = groups.Count(g => names.Resolve(g.Key).MatchedCode != null),
            ResolvedEvents = groups.Where(g => names.Resolve(g.Key).MatchedCode != null).Sum(g => g.Count()),
            Truncated = groups.Length > 4096,
            Skills = groups.Take(4096).Select(g => new { Skill = names.Resolve(g.Key), Events = g.Count() }).ToArray(),
            PvP = PvpAnalysis.Build(fight)
        };
    }

    public static void Write(Fight fight, SkillNames names, string? path = null)
    {
        path ??= Path.Combine(LocalLog.DirectoryPath, "skill-coverage.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Report(fight, names), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }

    public static int Run(string[] args)
    {
        string Argument(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing " + key); }
        try
        {
            var path = Path.GetFullPath(Argument("--fight"));
            var fight = new FightStore(Path.GetDirectoryName(path)!).Load(path);
            Write(fight, new SkillNames(), Argument("--output"));
            return 0;
        }
        catch (Exception ex) { LocalLog.Write("Skill audit failed: " + ex.Message); return 1; }
    }
}
