using System.Windows.Media;

namespace AionDpsMeter.Local;

// Original, font-independent vector symbols; no downloaded game artwork.
internal static class ClassSymbols
{
    static Geometry Shape(string path) { var shape = Geometry.Parse(path); shape.Freeze(); return shape; }
    static readonly Geometry Unknown = Shape("M8,2 L14,8 8,14 2,8 Z");
    static readonly Dictionary<string, Geometry> Shapes = new(StringComparer.OrdinalIgnoreCase) {
        ["Gladiator"] = Shape("M3,13 L12,4 14,2 12,2 3,11 M2,9 L7,14 M2,14 L4,12"),
        ["Templar"] = Shape("M8,1 L14,4 13,10 8,15 3,10 2,4 Z M8,4 L8,11 M5,7 L11,7"),
        ["Assassin"] = Shape("M2,2 L7,7 6,9 1,4 Z M14,2 L9,7 10,9 15,4 Z M5,8 L10,13 M11,8 L6,13 M4,10 L7,7 M9,7 L12,10"),
        ["Ranger"] = Shape("M4,1 Q15,8 4,15 L4,1 M1,8 L15,8 M12,5 L15,8 12,11"),
        ["Sorcerer"] = Shape("M8,1 Q2,6 3,11 Q4,15 8,15 Q14,14 13,9 L10,11 Q12,5 8,1 Z"),
        ["Spiritmaster"] = Shape("M3,14 L3,7 Q3,1 8,1 Q13,1 13,7 L13,14 10,12 8,14 6,12 Z M6,6 L6,8 M10,6 L10,8"),
        ["Cleric"] = Shape("M6,1 L10,1 10,6 15,6 15,10 10,10 10,15 6,15 6,10 1,10 1,6 6,6 Z"),
        ["Chanter"] = Shape("M8,6 L8,15 M8,1 L11,4 8,7 5,4 Z M2,7 Q8,12 14,7 M4,10 Q8,14 12,10"),
        ["Brawler"] = Shape("M3,8 L3,4 6,4 6,2 9,2 9,3 12,3 12,5 14,5 14,10 11,15 5,15 2,10 2,8 Z M6,4 L6,8 M9,3 L9,8 M12,5 L12,9")
    };
    internal static Geometry For(string job) => Shapes.GetValueOrDefault(job, Unknown);
}
