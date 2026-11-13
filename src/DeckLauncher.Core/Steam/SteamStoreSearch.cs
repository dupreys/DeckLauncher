using System.Text.Json;
using DeckLauncher.Core.Apps;

namespace DeckLauncher.Core.Steam;

/// <summary>Finds a game's Steam app id by name through the public store search (no key needed).</summary>
public static class SteamStoreSearch
{
    // Answers that were really received (found or "not on Steam"), so one game is not searched again for each kind of picture.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> Known = new();

    /// <param name="attempt">Set to "network failed" when Steam could not be reached or throttled the request (so the caller tries again later instead of remembering "not found").</param>
    public static async Task<string?> FindAppIdAsync(HttpClient http, string name, CancellationToken ct = default, ArtAttempt? attempt = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = StartApps.Normalize(name);
        if (Known.TryGetValue(key, out var known)) return known;
        try
        {
            var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(name)}&l=english&cc=us";
            var id = ParseExactMatch(await http.GetStringAsync(url, ct), name);
            Known[key] = id;
            return id;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (attempt is not null) attempt.NetworkFailed = true;
            return null;
        }
    }

    /// <summary>The id of the app whose name is the same as <paramref name="name"/> (ignoring case, accents and punctuation); anything looser could pick the wrong game.</summary>
    public static string? ParseExactMatch(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var wanted = StartApps.Normalize(name);
        if (wanted.Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() != "app") continue;
                var title = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                if (title is null || StartApps.Normalize(title) != wanted) continue;
                if (item.TryGetProperty("id", out var id) && id.TryGetInt64(out var value) && value > 0) return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (JsonException) { }
        return null;
    }
}
