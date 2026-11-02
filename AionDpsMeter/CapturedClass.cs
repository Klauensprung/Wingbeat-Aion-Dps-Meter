namespace AionDpsMeter.Local;

internal static class CapturedClass
{
    // Character template variants occupy groups of four. Unknown/base groups
    // remain unknown; these IDs are not combat skill prefixes.
    internal static string Name(int template) => template > 0 ? ((template - 1) / 4) switch {
        1 => "Gladiator", 2 => "Templar", 3 => "Ranger", 4 => "Assassin",
        5 => "Spiritmaster", 6 => "Sorcerer", 7 => "Cleric", 8 => "Chanter",
        11 => "Brawler", _ => "Unknown"
    } : "Unknown";
}
