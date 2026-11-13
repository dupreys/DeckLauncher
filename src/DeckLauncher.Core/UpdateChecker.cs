using System.Net.Http;
using System.Text.Json;

namespace DeckLauncher.Core;

/// <summary>What the latest GitHub release says.</summary>
/// <param name="InstallerUrl">The release's DeckLauncher-Setup-*.exe on github.com (null when the release has none).</param>
/// <param name="InstallerSize">Its size in bytes, as GitHub lists it (0 = unknown).</param>
/// <param name="Sha256">Its SHA-256 as GitHub lists it ("digest" of the asset), lowercase hex; null when GitHub gives none.</param>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? InstallerUrl = null, long InstallerSize = 0, string? Sha256 = null);

/// <summary>
/// Looks for a newer release on GitHub (public API, no key). It only reports: the user opens the download page
/// and runs the installer themselves, so nothing is downloaded or replaced behind their back.
/// </summary>
public static class UpdateChecker
{
    private static readonly System.Text.RegularExpressions.Regex InstallerName = new(@"^DeckLauncher-Setup-[0-9A-Za-z.\-]+\.exe$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex Sha256Hex = new("^[0-9a-fA-F]{64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>"v1.2.3" or "1.2.3-beta" -> 1.2.3; null when it is not a version number.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) s = s[..cut];
        return Version.TryParse(s, out var version) ? version : null;
    }

    /// <summary>Reads GitHub's "latest release" JSON. Null for drafts, pre-releases and anything unreadable.</summary>
    public static UpdateInfo? ParseLatestRelease(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Flag(root, "draft") || Flag(root, "prerelease")) return null;

            var tag = Text(root, "tag_name");
            var url = Text(root, "html_url");
            var version = ParseVersion(tag);
            if (version is null || tag is null || url is null) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com") return null; // only ever open a github.com page
            var (installer, size, sha) = FindInstaller(root);
            return new UpdateInfo(version, tag, url, installer, size, sha);
        }
        catch (JsonException)
        {
            return null;
        }

        static (string? Url, long Size, string? Sha) FindInstaller(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return (null, 0, null);
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;
                var name = Text(asset, "name");
                var url = Text(asset, "browser_download_url");
                if (name is null || url is null || !InstallerName.IsMatch(name)) continue;
                // Only a file of that name, served from a github.com release: nothing else is ever downloaded and run.
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com"
                    || !uri.AbsolutePath.Contains("/releases/download/", StringComparison.Ordinal) || !uri.AbsolutePath.EndsWith("/" + name, StringComparison.Ordinal)) continue;
                var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : 0;
                var digest = Text(asset, "digest");
                var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && Sha256Hex.IsMatch(digest[7..]) ? digest[7..].ToLowerInvariant() : null;
                return (url, size, sha);
            }
            return (null, 0, null);
        }

        static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        static string? Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>True when <paramref name="latest"/> is newer than what is running. Version 0.0.0 (dev builds) never asks for updates.</summary>
    public static bool IsNewer(Version latest, Version current) => current > new Version(0, 0, 0) && Normalize(latest) > Normalize(current);

    private static Version Normalize(Version v) => new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    /// <summary>Returns the newer release, or null when up to date, offline, or the repository is unknown ("owner/name").</summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, string? repository, Version current, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repository) || !repository.Contains('/')) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("User-Agent", "LiteLauncher");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var info = ParseLatestRelease(await response.Content.ReadAsStringAsync(ct));
            return info is not null && IsNewer(info.Version, current) ? info : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null; // offline or rate-limited: try again next time
        }
    }
}
