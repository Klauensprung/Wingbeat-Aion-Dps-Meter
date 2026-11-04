using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record BossHealthProfile(string Boss, double[] Thresholds, string Cue, string Response, string Mode, string ResearchId);

internal sealed class BossHealthAlerts
{
    // Only the small curated subset is shipped, not the research catalogue.
    static readonly Lazy<Dictionary<string,BossHealthProfile[]>> profiles = new(() => {
        using var stream = typeof(BossHealthAlerts).Assembly.GetManifestResourceStream("Wingbeat.BossHpAlerts.json")
            ?? throw new InvalidDataException("Missing boss HP profiles.");
        return (JsonSerializer.Deserialize<BossHealthProfile[]>(stream) ?? [])
            .GroupBy(p => Normalize(p.Boss)).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
    });
    static string Normalize(string name) => name.Trim().Replace('\u2019', '\'');
    sealed class Reading
    {
        internal long Time = -1, Maximum;
        internal double? Percent;
        internal readonly HashSet<(string Id,double Hp)> Fired = new();
    }
    readonly Dictionary<int,Reading> readings = new();
    internal void Clear() => readings.Clear();
    internal void ResetEncounter()
    {
        // DPS segmentation is not a new network scene. Current-only HP updates
        // still need the maximum and baseline captured before the reset.
        foreach (var reading in readings.Values) reading.Fired.Clear();
    }
    internal void Remove(int id) => readings.Remove(id);
    internal BossAlert? Apply(int entity, string name, long now, JsonElement fields, out bool reset)
    {
        reset = false;
        if (!profiles.Value.TryGetValue(Normalize(name), out var rules)) return null;
        if (!readings.TryGetValue(entity, out var reading)) {
            // Expired encounters must not permanently prevent new bosses tracking.
            if (readings.Count >= 32) readings.Remove(readings.MinBy(p => p.Value.Time).Key);
            readings[entity] = reading = new();
        }
        if (now < reading.Time) return null;
        long? Number(string key) => fields.TryGetProperty(key,out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : null;
        long? hp = Number("CurrentHp"), max = Number("MaximumHp");
        if (max == 0) max = null;
        if (hp is < 0 || max is < 0 || hp > (max ?? reading.Maximum) && (max ?? reading.Maximum) > 0) return null;
        reading.Time = now;
        if (max is > 0) {
            if (reading.Maximum > 0 && reading.Maximum != max) {
                reading.Percent = null;
                reading.Fired.Clear();
                reset = true;
            }
            reading.Maximum = max.Value;
        }
        if (hp == null || reading.Maximum <= 0) return null;
        double percent = hp.Value * 100d / reading.Maximum;
        double? previous = reading.Percent;
        // A return to full health is an observed encounter reset, not ordinary healing.
        if (percent >= 99 && previous is < 99) { reading.Fired.Clear(); previous = null; reset = true; }
        reading.Percent = percent;
        BossHealthProfile? selected = null;
        double selectedHp = 101;
        foreach (var rule in rules)
            foreach (double threshold in rule.Thresholds) {
                double prepareAt = Math.Min(99, threshold + 3);
                if (percent > prepareAt || reading.Fired.Contains((rule.ResearchId,threshold))) continue;
                reading.Fired.Add((rule.ResearchId,threshold));
                // Initial samples establish a baseline. Large jumps past a phase do not
                // produce a queue of obsolete instructions, nor does HP=0 warn.
                if (previous == null || previous <= prepareAt || percent <= 0 || percent < threshold - 5) continue;
                if (threshold < selectedHp) { selected = rule; selectedHp = threshold; }
            }
        return selected == null ? null : new(entity,name,0,$"Prepare: {selected.Cue}",now,now+6000,0,false,
            $"~{selectedHp:0.#}% HP · {selected.Mode} | {selected.Response}");
    }
}
