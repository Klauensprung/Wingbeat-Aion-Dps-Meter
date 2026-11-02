using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class ReleaseUpdateTests
{
    internal static void Run(Action<bool, string> check)
    {
        string Fixture(string version, bool draft = false, bool prerelease = false, string? url = null) => JsonSerializer.Serialize(new {
            tag_name = version, draft, prerelease, html_url = url ?? $"https://github.com/{ReleaseUpdates.Repository}/releases/tag/{version}"
        });
        var current = new Version(0, 5, 44, 0);
        var next = ReleaseUpdates.Parse(Fixture("v0.5.45"), current);
        check(next.Available && next.Version == "0.5.45" && next.Page.AbsoluteUri.EndsWith("/releases/tag/v0.5.45"),
            "A newer GitHub release supplies a browser page without an installer or asset download");
        check(!ReleaseUpdates.Parse(Fixture("0.5.44"), current).Available && !ReleaseUpdates.Parse(Fixture("0.5.43"), current).Available &&
            ReleaseUpdates.Parse(Fixture("0.6.0"), current).Available && ReleaseUpdates.Parse(Fixture("0.5.100"), current).Available,
            "Release versions compare numerically and normalize missing revision numbers");
        bool Reject(string json) { try { ReleaseUpdates.Parse(json, current); return false; } catch (Exception e) when (e is InvalidDataException or JsonException or KeyNotFoundException) { return true; } }
        check(Reject(Fixture("0.5.45", draft: true)) && Reject(Fixture("0.5.45", prerelease: true)) && Reject(Fixture("0.5.45-preview")),
            "Drafts, prereleases and invalid release tags do not become stable update prompts");
        check(Reject(Fixture("0.5.45", url: "https://example.com/download")) &&
            Reject(Fixture("0.5.45", url: "https://github.com/another/project/releases/tag/0.5.45")) && Reject("{}"),
            "Malformed release metadata cannot send the update button to an unrelated download");
    }
}
