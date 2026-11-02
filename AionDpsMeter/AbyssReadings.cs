using System.IO;
using System.Text;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record AbyssServer(int Server, string Race);
internal sealed record AbyssMatching(AbyssServer[] Servers);
internal sealed record AbyssSeason(string Name, int Grade, long Points, double? Kills, double? Deaths, double? Assists, int RankChange);
internal static class AbyssWire
{
    static int Count(ref WireReader r) { int n = r.Var(); if (n > 4096) throw new InvalidDataException("Abyss list exceeds bounds."); return n; }
    static string Text(ref WireReader r) => new UTF8Encoding(false, true).GetString(r.Take(Count(ref r))).TrimEnd('\0');
    internal static AbyssMatching? Matching(ref WireReader r)
    {
        int count = Count(ref r); AbyssMatching? result = null; int total = 0;
        for (int i = 0; i < count; i++) {
            byte type = r.Byte(); int size = Count(ref r); total += size;
            if (total > 4096) throw new InvalidDataException("Abyss matching entries exceed bounds.");
            var servers = new List<AbyssServer>();
            for (int j = 0; j < size; j++) { int server = r.U16(); byte race = r.Byte(); if (type == 1 && race is 1 or 2) servers.Add(new(server, race == 1 ? "light" : "dark")); }
            if (type == 1 && result == null) result = new(servers.ToArray());
        }
        if (r.Remaining != 0) throw new InvalidDataException("Unexpected matching trailer."); return result;
    }
    internal static AbyssSeason? Season(ref WireReader r, string? localName)
    {
        ushort result = r.U16(); byte type = r.Byte(); r.Byte();
        r.U32(); r.U64(); Text(ref r); Text(ref r); r.U16(); r.Take(8); r.Byte(); long points = unchecked((long)r.U64());
        string extra = Text(ref r); int change = unchecked((int)r.U32()); r.U32(); int grade = unchecked((int)r.U32()); r.U32(); r.U32(); r.U64();
        if (r.Remaining != 0) throw new InvalidDataException("Unexpected season ranking trailer.");
        if (result != 0 || type != 1 || localName == null) return null;
        double? kills = null, deaths = null, assists = null;
        try {
            using var json = JsonDocument.Parse(extra);
            double? Number(string name) => json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(name, out var v) &&
                v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double n) && double.IsFinite(n) ? n : null;
            kills = Number("kill_count"); deaths = Number("death_count"); assists = Number("assist_count");
        } catch (JsonException) { }
        return new(localName, grade, points, kills, deaths, assists, change);
    }
}

internal sealed class AbyssTracker
{
    internal AbyssSeason? Season { get; private set; }
    internal long SeasonAt { get; private set; } = -1;
    AbyssMatching? matching;
    long matchingAt = -1;
    (int Server, string Name)? owner;
    internal void Clear(long at) { Season = null; matching = null; owner = null; SeasonAt = matchingAt = at; }
    internal void Follow(Actor actor)
    {
        var next = (actor.Server, actor.Name);
        if (owner is { } previous && previous != next) { Season = null; SeasonAt = -1; if (previous.Server != next.Server) { matching = null; matchingAt = -1; } }
        owner = next;
    }
    internal void Apply(NetworkEvent e)
    {
        if (e.Kind == "abyss-matching" && e.Timestamp >= matchingAt) {
            matching = e.Fields.Deserialize<AbyssMatching>(); matchingAt = e.Timestamp;
        }
        if (e.Kind == "abyss-season" && e.Timestamp >= SeasonAt) {
            var season = e.Fields.Deserialize<AbyssSeason>();
            if (season != null && owner?.Name == season.Name) { Season = season; SeasonAt = e.Timestamp; }
        }
    }
    internal AbyssServer[] Servers() => matching?.Servers.ToArray() ?? [];
}
