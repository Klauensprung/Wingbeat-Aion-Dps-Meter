using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record ReleaseUpdate(string Version, bool Available, Uri Page);
internal static class ReleaseUpdates
{
    internal const string Repository = "Klauensprung/Wingbeat-Aion-Dps-Meter";
    static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 512 * 1024 };
    internal static Version Current => typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);
    internal static string CurrentLabel => $"{Current.Major}.{Current.Minor}.{Math.Max(0, Current.Build)}";
    static ReleaseUpdates() {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Wingbeat/" + CurrentLabel);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }
    internal static async Task<ReleaseUpdate> CheckAsync()
        => Parse(await client.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest").ConfigureAwait(false), Current);

    internal static ReleaseUpdate Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("The latest release is not a stable published release.");
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        string version = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        if (!Version.TryParse(version, out var latest)) throw new InvalidDataException("Unsupported release version.");
        if (!Uri.TryCreate(release.GetProperty("html_url").GetString(), UriKind.Absolute, out var page) || page.Scheme != "https" ||
            page.Host != "github.com" || !page.IsDefaultPort || page.UserInfo.Length != 0 ||
            !page.AbsolutePath.StartsWith("/" + Repository + "/releases/tag/", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected release page.");
        static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
        return new(version, Normalize(latest) > Normalize(current), page);
    }
}
