namespace AionDpsMeter.Local;

// Assign once per encounter, independent of ranks, names, classes and meter mode.
internal sealed class ParticipantColors
{
    static readonly string[] Palette = ["#65CBE9", "#AE91EF", "#65D6AF", "#EABF70", "#EA8EA7",
        "#82B1F0", "#B5CE75", "#DA9CE9", "#F19665", "#E4E8F0"];
    readonly Dictionary<int, string> assigned = new();
    readonly HashSet<string> used = new();
    string? encounter;
    int next, fallback;

    public MeterData Apply(Fight fight, MeterData data)
    {
        if (encounter != fight.Id) { encounter = fight.Id; assigned.Clear(); used.Clear(); next = fallback = 0; }
        foreach (int id in data.Rows.Select(r => r.Id).Distinct().Order()) {
            if (assigned.ContainsKey(id)) continue;
            assigned[id] = Allocate();
        }
        return data with { Rows = data.Rows.Select(r => r with { Color = assigned[r.Id] }).ToArray() };
    }

    string Allocate()
    {
        // Rounded HSV colours have a finite gamut. Never retry it indefinitely.
        for (int attempt = 0; attempt < 32; attempt++) {
            string color = Generate(next++);
            if (used.Add(color)) return color;
        }
        // An odd multiplier permutes the 24-bit RGB space without repetition.
        while (fallback < 0x1000000) {
            string color = $"#{(fallback++ * 0x9E3779L & 0xFFFFFF):X6}";
            if (used.Add(color)) return color;
        }
        return Palette[0]; // Safe even if every representable RGB colour was used.
    }

    static string Generate(int index)
    {
        if (index < Palette.Length) return Palette[index];
        // Extend the palette without cycling when many PvP opponents are present.
        double hue = (index * 137.507764) % 360 / 60;
        double chroma = .50 + .08 * (index % 3), minimum = .92 - chroma;
        double x = chroma * (1 - Math.Abs(hue % 2 - 1));
        (double r, double g, double b) = (int)hue switch {
            0 => (chroma, x, 0d), 1 => (x, chroma, 0d), 2 => (0d, chroma, x),
            3 => (0d, x, chroma), 4 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        return $"#{(int)Math.Round((r + minimum) * 255):X2}{(int)Math.Round((g + minimum) * 255):X2}{(int)Math.Round((b + minimum) * 255):X2}";
    }
}
