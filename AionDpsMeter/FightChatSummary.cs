using System.Globalization;

namespace AionDpsMeter.Local;

internal static class FightChatSummary
{
    // Keep each independently copyable line short. This is not a claim about
    // an undocumented game chat character limit, and never sends to the game.
    static string Clean(string value, int length)
    {
        string clean = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        clean = new string(clean.Where(c => !char.IsControl(c)).ToArray());
        var elements = new StringInfo(clean);
        return elements.LengthInTextElements > length ? elements.SubstringByTextElements(0, length) + "…" : clean;
    }
    internal static string[] Build(Fight fight, bool healing, string scope)
    {
        var data = MeterPresentation.Build(fight, Analysis.Totals(fight), healing, scope == "All", limit: 50, actorScope: scope);
        string unit = healing ? "HPS" : "DPS";
        var lines = new List<string> { $"[Wingbeat] {Clean(EncounterOverview.Title(fight), 45)} | {MeterPresentation.Duration(fight)} | {data.Scope}",
            $"Total: {MeterPresentation.Number(data.Amount)} {(healing ? "healing" : "damage")} | {MeterPresentation.Number(data.Rate)} {unit}" };
        var rows = data.Rows.Where(r => r.Amount > 0).ToArray();
        for (int i = 0; i < rows.Length; i++) {
            var row = rows[i];
            lines.Add($"{i + 1}. {Clean(row.Name, 28)} | {MeterPresentation.Number(row.Amount)} | {MeterPresentation.Number(row.Rate)} {unit} | {row.Share:0.0}%");
        }
        if (rows.Length == 0) lines.Add(healing ? "No healing recorded in this scope." : "No damage recorded in this scope.");
        return lines.ToArray();
    }
}
