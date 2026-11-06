namespace AionDpsMeter.Local;

public sealed record CooldownCharacterPreset(string Name, string Job, int[] Skills, Dictionary<int, double> Estimates)
{
    public int[] Buffs { get; init; } = [];
    public int[] VoiceSkills { get; init; } = [];
}

internal static class CooldownCharacterPresets
{
    internal static string? Key(Actor? actor) => actor is { Player: true, Server: > 0, PersistentId: > 0 }
        ? $"{actor.Server}:{actor.PersistentId}" : null;

    internal static void Save(Preferences settings)
    {
        settings.CooldownCharacters ??= new();
        if (settings.CooldownCharacterKey is not { } key ||
            !settings.CooldownCharacters.TryGetValue(key, out var previous)) return;
        settings.CooldownCharacters[key] = previous with {
            Skills = [.. settings.CooldownSkills ?? []],
            Buffs = [.. settings.CooldownBuffs ?? []],
            VoiceSkills = [.. settings.CooldownVoiceSkills ?? []],
            Estimates = new(settings.CooldownFallbackSeconds ?? new())
        };
    }

    internal static bool Activate(Preferences settings, Actor? actor)
    {
        string? key = Key(actor);
        if (key == null) return false;
        settings.CooldownCharacters ??= new();
        if (settings.CooldownCharacterKey == key && settings.CooldownCharacters.ContainsKey(key)) return false;
        Save(settings);
        // Preserve the existing setup once when upgrading; subsequent characters
        // start empty rather than inheriting another class's tracked skills.
        bool migrate = settings.CooldownCharacterKey == null && settings.CooldownCharacters.Count == 0;
        if (!settings.CooldownCharacters.TryGetValue(key, out var preset)) {
            preset = new(actor!.Name, actor.Job, migrate ? [.. settings.CooldownSkills ?? []] : [],
                migrate ? new(settings.CooldownFallbackSeconds ?? new()) : new()) { Buffs = migrate ? [.. settings.CooldownBuffs ?? []] : [], VoiceSkills = migrate ? [.. settings.CooldownVoiceSkills ?? []] : [] };
            settings.CooldownCharacters[key] = preset;
        }
        settings.CooldownCharacterKey = key;
        settings.CooldownSkills = [.. preset.Skills ?? []];
        settings.CooldownBuffs = [.. preset.Buffs ?? []];
        settings.CooldownVoiceSkills = [.. preset.VoiceSkills ?? []];
        settings.CooldownFallbackSeconds = new(preset.Estimates ?? new());
        return true;
    }

    internal static string Description(Preferences settings) => settings.CooldownCharacterKey is { } key &&
        settings.CooldownCharacters?.TryGetValue(key, out var preset) == true
        ? Localization.F($"{preset.Name} ({preset.Job}) — saved automatically for this character.")
        : Localization.T("Your setup will be assigned when your in-game character is detected.");
}
