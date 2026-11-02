using System.IO;

namespace AionDpsMeter.Local;

internal static class EffectCatalogueTests
{
    internal static void Run(Action<bool, string> check)
    {
        var catalogue = EffectCatalogue.Default;
        check(catalogue.LoadError == null && catalogue.Count == 4621 && catalogue.Find(100) is { Name: "Stun", Kind: "Debuff" },
            "Bundled effect catalogue loads exact names and explicit variant types through ID dictionary lookups");
        check(catalogue.Find(999999999) == null && EffectCatalogue.ControlName(catalogue.Find(100)) == "Stun",
            "Unknown effect IDs remain unknown while a named debuff can identify its control label");
        check(EffectCatalogue.ControlName(new(1, "Stun Immunity", "Buff")) == null &&
            EffectCatalogue.ControlName(new(1, "Stun", "Unknown")) == null && EffectCatalogue.ControlName(new(1, "Sleep Resistance", "Debuff")) == null,
            "Control classification does not use name substrings or uncertain effect types");
        var hash = new string('0', 64);
        bool rejected = false;
        try { EffectCatalogue.Validate(new("synthetic", hash, hash, [new(100, "Stun", "Debuff"), new(100, "Other", "Buff")])); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Duplicate effect identities are rejected before building the catalogue");
        var row = new EffectRow("Player 1", "Player 2", 100, "Source", 1, 10, 1, false);
        check(row.EffectName == "Stun" && row.EffectKind == "Debuff" && (row with { EffectId = 999999999 }).EffectKind == "Unknown",
            "Saved-effect analysis uses exact effect identity rather than its source skill for name and type");
    }
}
