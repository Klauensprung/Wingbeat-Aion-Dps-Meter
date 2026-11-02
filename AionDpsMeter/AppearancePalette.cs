using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class AppearancePalette
{
    static readonly Lazy<Dictionary<string, Dictionary<int, double[]>>> Colors = new(() => {
        using var stream = typeof(AppearancePalette).Assembly.GetManifestResourceStream("Wingbeat.AppearancePalette.json")
            ?? throw new InvalidOperationException("Missing appearance palette.");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, double[]>>>(stream)!;
    });

    internal static string? SkinColor(string? gender, int encodedIndex)
    {
        if (gender is not ("male" or "female") || !Colors.Value[gender].TryGetValue(encodedIndex - 100, out var linear)) return null;
        double[] tint = [.78 / .57, .52 / .57, .42 / .57];
        var rgb = new byte[3];
        for (int channel = 0; channel < 3; channel++) {
            double value = Math.Clamp(linear[channel] * tint[channel], 0, 1);
            double srgb = value <= .0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - .055;
            rgb[channel] = (byte)Math.Floor(srgb * 255 + .5);
        }
        return "#" + Convert.ToHexString(rgb);
    }

    internal static AppearanceSetting Resolve(AppearanceSetting setting, string? gender) =>
        setting is { Group: "Customize", Field: "SkinColor", Value: int index } && SkinColor(gender, index) is string color
            ? setting with { Color = color } : setting;
}
