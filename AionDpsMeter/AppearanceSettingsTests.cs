using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class AppearanceSettingsTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        check(AppearanceDisplay.Name(new("Customize", "HairType", 101, null)) == "Hairstyle" &&
            AppearanceDisplay.Name(new("Body", "Length_Spine", 100, null)) == "Rib Cage Length",
            "Appearance labels use verified creator names rather than protocol field spellings");
        check(AppearanceDisplay.Name(new("Body", "Thick_Forearm", 100, null), "male") == "Forearm Thickness" &&
            AppearanceDisplay.Name(new("Body", "Thick_Forearm", 100, null), "female") == "Lower Arm Thickness" &&
            AppearanceDisplay.Name(new("Body", "Thick_Forearm", 100, null)) == "Thick Forearm",
            "Gender-specific appearance labels do not assume gender when it is unknown");
        check(AppearanceDisplay.Name(new("Customize", "Unknown_FieldName", 100, null)) == "Unknown Field Name",
            "Unknown appearance fields retain a readable fallback name");
        using var output = new StreamWriter(Path.Combine(temp, "reference-appearance-settings.jsonl"));
        AppearanceSetting[]? Decode(byte[] bytes) {
            try { return AppearanceSettings.Decode(Convert.ToBase64String(bytes)); }
            catch (InvalidDataException) { return null; }
        }
        void Export(string name, byte[] bytes) => output.WriteLine(JsonSerializer.Serialize(new { Name = name, Data = Convert.ToBase64String(bytes), Expected = Decode(bytes) }));
        var ramp = Enumerable.Range(0, AppearanceSettings.ByteLength).Select(i => (byte)(i % 256)).ToArray(); ramp[0] = 12;
        var rows = Decode(ramp)!;
        using (var paletteOutput = new StreamWriter(Path.Combine(temp, "reference-appearance-palette.jsonl")))
            foreach (string gender in new[] { "male", "female", "unknown" })
                for (int index = 0; index < 256; index++)
                    paletteOutput.WriteLine(JsonSerializer.Serialize(new { Gender = gender, EncodedIndex = index, Expected = AppearancePalette.SkinColor(gender, index) }));
        check(new[] { "male", "female" }.All(g => Enumerable.Range(0, 256).Count(i => AppearancePalette.SkinColor(g, i) != null) == 100),
            "Both local skin palettes resolve exactly the 100 captured indices");
        var skin = new AppearanceSetting("Customize", "SkinColor", 101, null);
        check(AppearancePalette.Resolve(skin, "male") is { Color: not null, Value: 101 } &&
            AppearancePalette.Resolve(skin, null) == skin && AppearancePalette.Resolve(skin with { Value = 100 }, "male").Color == null &&
            AppearancePalette.Resolve(skin with { Group = "Face" }, "male").Color == null,
            "Skin resolution preserves the encoded index and does not guess missing gender, missing index or unrelated fields");
        using (var displayOutput = new StreamWriter(Path.Combine(temp, "reference-appearance-display.jsonl")))
            for (int value = 0; value < 256; value++) {
                var bytes = Enumerable.Repeat((byte)value, AppearanceSettings.ByteLength).ToArray(); bytes[0] = 12;
                displayOutput.WriteLine(JsonSerializer.Serialize(new { Data = Convert.ToBase64String(bytes), Expected = Decode(bytes)!.Select(AppearanceDisplay.Value).ToArray() }));
            }
        check(rows.Count(AppearanceDisplay.IsSlider) == 184, "Appearance slider metadata covers only the 184 known creator slider axes");
        check(Enumerable.Range(0, 256).All(v => AppearanceDisplay.Value(new("Body", "Height", v, null)) == Math.Floor(v / 2d + .5).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "Appearance slider display rounds all byte values like the creator without silently clamping unusual values");
        check(AppearanceDisplay.Value(new("Face", "EarShapeChange", 101, null)) == "Pointy Ears" &&
            AppearanceDisplay.Value(new("Customize", "bOddEye_Pupil", 100, null)) == "Default" &&
            AppearanceDisplay.Value(new("Customize", "HairColor", null, "#AABBCC")) == "#AABBCC",
            "Appearance choices show verified labels while RGB colors remain exact");
        check(AppearanceDisplay.Value(new("Face", "EarShapeChange", 102, null)) == "102 (raw)" &&
            AppearanceDisplay.Value(new("Customize", "UnknownChoice", 100, null)) == "100 (raw)" &&
            AppearanceDisplay.Value(new("Customize", "EarShapeChange", 101, null)) == "101 (raw)",
            "Unknown appearance choices preserve raw evidence and cannot borrow labels from another group");
        check(Enumerable.Range(0, 256).All(v => AppearanceDisplay.Value(new("Customize", "bTattooSymmetry", v, null)) == (v == 100 ? "Off" : "On")),
            "Appearance toggles match the reference nonzero decoded-state behavior across all bytes");
        check(rows.Length == 260 && rows.Count(r => r.Group == "Body") == 42 && rows.Count(r => r.Group == "Face") == 60 &&
            rows.Count(r => r.Group == "Customize") == 158 && rows.Count(r => r.Color != null) == 34 && rows[0] is { Field: "Length_Waist", Value: 1 },
            "Version-12 appearance settings retain body, face, material and RGB field widths");
        Export("ramp", ramp);
        bool truncationsRejected = true;
        for (int n = 0; n < ramp.Length; n++) { var bytes = ramp[..n]; truncationsRejected &= Decode(bytes) == null; Export("truncated-" + n, bytes); }
        check(truncationsRejected, "Every truncated version-12 appearance payload is rejected atomically");
        var wrong = ramp.ToArray(); wrong[0] = 11; Export("wrong-version", wrong);
        check(Decode(wrong) == null && Decode([..ramp, 255, 1])!.SequenceEqual(rows), "Appearance rejects unknown versions and accepts bounded extension bytes like the reference");
        Export("extension", [..ramp, 255, 1]);
        foreach (byte value in new byte[] { 0, 100, 200, 255 }) { var bytes = Enumerable.Repeat(value, ramp.Length).ToArray(); bytes[0] = 12; Export("uniform-" + value, bytes); }
        var random = new Random(4917);
        for (int n = 0; n < 8; n++) { var bytes = new byte[ramp.Length]; random.NextBytes(bytes); bytes[0] = 12; Export("random-" + n, bytes); }
        bool invalidEncoding = false;
        try { AppearanceSettings.Decode("not base64"); } catch (InvalidDataException) { invalidEncoding = true; }
        check(invalidEncoding && Decode(new byte[65537]) == null, "Appearance decoding bounds allocation and rejects malformed encoding");
        var color = rows.First(r => r.Color != null);
        check(color.Value == null && color.Color!.Length == 7, "Appearance RGB bytes remain colors rather than unverified numeric slider values");
    }
}
