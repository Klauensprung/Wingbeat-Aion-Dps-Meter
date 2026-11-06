using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class ReleaseUpdateTests
{
    internal static void Run(Action<bool, string> check)
    {
        var asset = new Velopack.VelopackAsset { PackageId = ReleaseUpdates.PackageId, Version = new(0, 5, 50), SHA256 = new string('a', 64), Size = 100 };
        check(ReleaseUpdates.ValidPackage(asset, "0.5.50"), "Updater accepts the matching Wingbeat package with a SHA256 digest");
        check(!ReleaseUpdates.ValidPackage(asset, "0.5.51"), "Updater rejects a package from a different release");
        asset.PackageId = "Other.App";
        check(!ReleaseUpdates.ValidPackage(asset, "0.5.50"), "Updater rejects a different application identity");
        asset.PackageId = ReleaseUpdates.PackageId; asset.SHA256 = "";
        check(!ReleaseUpdates.ValidPackage(asset, "0.5.50"), "Updater refuses packages without SHA256 metadata");
        string package = Path.GetTempFileName();
        try {
            byte[] bytes = [1, 2, 3, 4]; File.WriteAllBytes(package, bytes);
            asset.Size = bytes.Length;
            asset.SHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            var verifier = new PackageVerifier();
            verifier.Verify(asset, package);
            check(true, "Velopack verifies a complete download against SHA256");
            File.WriteAllBytes(package, [4, 3, 2, 1]);
            bool rejected = false;
            try { verifier.Verify(asset, package); } catch (Velopack.Exceptions.ChecksumFailedException) { rejected = true; }
            check(rejected, "Velopack refuses altered downloads even when the file size matches");
            File.WriteAllBytes(package, [1]); rejected = false;
            try { verifier.Verify(asset, package); } catch (Velopack.Exceptions.ChecksumFailedException) { rejected = true; }
            check(rejected, "Velopack refuses incomplete downloads");
        } finally { File.Delete(package); }
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
    sealed class PackageVerifier : Velopack.UpdateManager
    {
        internal PackageVerifier() : base("https://github.com/" + ReleaseUpdates.Repository) { }
        internal void Verify(Velopack.VelopackAsset asset, string path) => VerifyPackageChecksumAsync(asset, path).GetAwaiter().GetResult();
    }
}
