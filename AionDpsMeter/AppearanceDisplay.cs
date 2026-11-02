using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDpsMeter.Local;

internal static class AppearanceDisplay
{
    static readonly Lazy<Dictionary<string, string>> Labels = new(() => {
        using var stream = typeof(AppearanceDisplay).Assembly.GetManifestResourceStream("Wingbeat.AppearanceLabels.json")
            ?? throw new InvalidOperationException("Missing appearance labels.");
        var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        if (labels.Count > 1000 || labels.Any(p => p.Key.Length > 200 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 300))
            throw new InvalidOperationException("Invalid appearance labels.");
        return labels;
    });
    internal static string Name(AppearanceSetting setting, string? gender = null)
    {
        string key = setting.Group.ToLowerInvariant() + ":" + setting.Field;
        if (gender is "male" or "female" && Labels.Value.TryGetValue(gender + ":" + key, out var specific)) return specific;
        if (Labels.Value.TryGetValue(key, out var label)) return label;
        return Regex.Replace(setting.Field.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ");
    }
    static readonly Dictionary<string, string[]> Choices = new(StringComparer.Ordinal) {
        ["Face:EarShapeChange"] = ["Standard Ears", "Pointy Ears"],
        ["Customize:bOddEye_Iris"] = ["Default", "Asymmetrical"],
        ["Customize:bOddEye_Pupil"] = ["Default", "Asymmetrical"],
        ["Customize:bOddEye_Sclera"] = ["Default", "Asymmetrical"],
        ["Customize:bUseHairColorDetailSettings"] = ["Default", "Two-Tone"],
        ["Customize:bUseBodyGlossiness"] = ["All Skin Gloss", "Individual Face/Body"]
    };
    static readonly HashSet<string> Toggles = new(StringComparer.Ordinal) {
        "bUseEmiPupil_L", "bUseEmiPupil_R", "bTattooFlipU", "bTattooFlipV", "bTattooSymmetry"
    };
    // Only fields identified as sliders/pad axes use this scale. Choices and toggles do not.
    static readonly Lazy<HashSet<string>> Sliders = new(() => {
        using var stream = typeof(AppearanceDisplay).Assembly.GetManifestResourceStream("Wingbeat.AppearanceSliders.json")
            ?? throw new InvalidOperationException("Missing appearance slider metadata.");
        return new(JsonSerializer.Deserialize<string[]>(stream)!, StringComparer.Ordinal);
    });

    internal static bool IsSlider(AppearanceSetting setting) =>
        Sliders.Value.Contains(setting.Group.ToLowerInvariant() + ":" + setting.Field);

    internal static string Value(AppearanceSetting setting)
    {
        if (setting.Color != null) return setting.Color;
        if (setting.Value is not int value) return "";
        if (Choices.TryGetValue(setting.Group + ":" + setting.Field, out var choices) && value is >= 100 and <= 101)
            return choices[value - 100];
        if (setting.Group == "Customize" && Toggles.Contains(setting.Field)) return value == 100 ? "Off" : "On";
        // Positive integer rounding matches the creator display, including odd bytes.
        return IsSlider(setting) ? ((value + 1) / 2).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture) + " (raw)";
    }
}
