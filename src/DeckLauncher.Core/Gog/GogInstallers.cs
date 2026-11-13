using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DeckLauncher.Core.Gog;

/// <summary>One file of a game's Windows offline installer (the .exe and, for big games, the .bin parts).</summary>
public sealed record GogInstallerFile(string Name, string ManualUrl, long Size);

/// <summary>
/// GOG games are DRM-free and every game of the account has offline installers. This lists them and resolves their download
/// addresses with the account's token (the same endpoints MiniGalaxy and lgogdownloader use; GOG does not document them).
/// </summary>
public static class GogInstallers
{
    /// <summary>The Windows installer files in the best language: <paramref name="wanted"/> words first, then English, then the first one.</summary>
    public static IReadOnlyList<GogInstallerFile> ParseDetails(string json, params string[] wanted)
    {
        var result = new List<GogInstallerFile>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("downloads", out var downloads) || downloads.ValueKind != JsonValueKind.Array)
                return result;

            var byLanguage = new List<(string Language, List<GogInstallerFile> Files)>();
            foreach (var entry in downloads.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 2) continue;
                var language = entry[0].ValueKind == JsonValueKind.String ? entry[0].GetString() ?? "" : "";
                if (entry[1].ValueKind != JsonValueKind.Object || !entry[1].TryGetProperty("windows", out var windows) || windows.ValueKind != JsonValueKind.Array) continue;

                var files = new List<GogInstallerFile>();
                foreach (var f in windows.EnumerateArray())
                {
                    string? S(string n) => f.ValueKind == JsonValueKind.Object && f.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    var manual = S("manualUrl");
                    if (string.IsNullOrWhiteSpace(manual) || !manual.StartsWith("/downlink/", StringComparison.Ordinal)) continue;
                    long size = f.TryGetProperty("size", out var sz) ? sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out var n) ? n : long.TryParse(sz.GetString()?.Trim(), out var parsed) ? parsed : 0 : 0;
                    files.Add(new GogInstallerFile(S("name") ?? "installer", manual, size));
                }
                if (files.Count > 0) byLanguage.Add((language, files));
            }

            foreach (var word in wanted.Concat(new[] { "English" }))
                if (byLanguage.FirstOrDefault(l => l.Language.Contains(word, StringComparison.OrdinalIgnoreCase)) is { Files: not null } hit && hit.Files is { Count: > 0 })
                    return hit.Files;
            return byLanguage.Count > 0 ? byLanguage[0].Files : result;
        }
        catch (JsonException)
        {
            return result;
        }
    }

    public static async Task<IReadOnlyList<GogInstallerFile>> GetInstallersAsync(HttpClient http, GogTokens tokens, string gameId, string[] wanted, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://embed.gog.com/account/gameDetails/{Uri.EscapeDataString(gameId)}.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new GogSignInException("A sessão da GOG expirou. Entre de novo.");
        if (!response.IsSuccessStatusCode) return Array.Empty<GogInstallerFile>();
        return ParseDetails(await response.Content.ReadAsStringAsync(ct), wanted);
    }

    /// <summary>The address to download one file from (GOG answers with JSON {"downlink": …} or redirects straight to the file); null when it cannot be resolved.</summary>
    public static async Task<string?> ResolveAsync(HttpClient http, GogTokens tokens, string manualUrl, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://embed.gog.com" + manualUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            return ParseDownlink(await response.Content.ReadAsStringAsync(ct));
        var final = response.RequestMessage?.RequestUri;
        return final is not null && final.AbsoluteUri != "https://embed.gog.com" + manualUrl ? final.AbsoluteUri : null; // redirected to the file itself
    }

    public static string? ParseDownlink(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("downlink", out var d) && d.ValueKind == JsonValueKind.String
                && Uri.TryCreate(d.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                return uri.AbsoluteUri;
        }
        catch (JsonException) { /* not the expected answer */ }
        return null;
    }

    /// <summary>A file name that is safe on disk (the name GOG gives, cleaned).</summary>
    public static string SafeFileName(string name)
    {
        var clean = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return clean.Length == 0 ? "installer" : clean;
    }
}
