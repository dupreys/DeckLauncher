using System.Net.Http;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.Core;

/// <summary>
/// Automatic game logos (transparent PNG shown over the banner on the game page).
/// Order: the logo Steam already cached on this PC, Steam's own CDN, then SteamGridDB with the user's key.
/// Files go to the same cache folder as the other art and are never downloaded twice.
/// </summary>
public static class LogoDownloader
{
    private const int MaxBytes = 4 * 1024 * 1024;
    private const string Cdn = "https://cdn.cloudflare.steamstatic.com/steam/apps";

    private static string Safe(string value) => string.Concat(value.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_'));

    public static string CachePath(string cacheDir, Game game) =>
        Path.Combine(cacheDir, $"logo_{Safe(game.Source)}_{Safe(game.Id)}.png");

    /// <summary>The logo already on disk (downloaded earlier); never touches the network.</summary>
    public static string? Cached(string cacheDir, Game game)
    {
        var path = CachePath(cacheDir, game);
        return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
    }

    /// <summary>The logo the Steam client cached for an installed game (librarycache/&lt;appid&gt;/.../logo.png).</summary>
    public static string? FindSteamLocal(string? steamPath, Game game)
    {
        if (steamPath is null || game.Source != "Steam" || !IsAppId(game)) return null;
        var cache = Path.Combine(steamPath, "appcache", "librarycache");
        try
        {
            var flat = Path.Combine(cache, $"{game.Id}_logo.png");
            if (File.Exists(flat)) return flat;

            var folder = Path.Combine(cache, game.Id);
            if (!Directory.Exists(folder)) return null;
            return Directory.EnumerateFiles(folder, "logo.png", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Steam's CDN logo (no key). Many newer games have none there; the caller then tries SteamGridDB.</summary>
    public static async Task<string?> DownloadFromSteamAsync(HttpClient http, string cacheDir, Game game, CancellationToken ct = default)
    {
        if (!IsAppId(game) || game.Source != "Steam") return null;
        if (Cached(cacheDir, game) is { } existing) return existing;
        return await SaveAsync(http, $"{Cdn}/{game.Id}/logo.png", CachePath(cacheDir, game), ct);
    }

    /// <summary>Picks a PNG logo (WPF cannot read WebP), preferring the widest one. Throws <see cref="UnauthorizedAccessException"/> for a wrong key.</summary>
    public static async Task<string?> DownloadFromGridAsync(HttpClient http, string apiKey, string cacheDir, Game game,
                                                            int? knownGameId = null, CancellationToken ct = default)
    {
        if (Cached(cacheDir, game) is { } existing) return existing;
        try
        {
            var id = knownGameId ?? await SteamGridDb.FindGameIdAsync(http, apiKey, game, ct);
            if (id is null) return null;

            var images = await SteamGridDb.GetImagesAsync(http, apiKey, id.Value, ArtKind.Logo, ct);
            var best = images
                .Where(i => SteamGridDbArt.IsAllowedUrl(i.Url) && i.Url.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.Height > 0 && i.Width >= i.Height ? 1 : 0)
                .ThenByDescending(i => i.Width)
                .FirstOrDefault();
            return best is null ? null : await SaveAsync(http, best.Url, CachePath(cacheDir, game), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null; // offline: tried again next start
        }
    }

    private static async Task<string?> SaveAsync(HttpClient http, string url, string path, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true) return null;
            if (response.Content.Headers.ContentLength is > MaxBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length is < 512 or > MaxBytes) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".part";
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            return null;
        }
    }

    private static bool IsAppId(Game game) => game.Id.Length > 0 && game.Id.All(char.IsAsciiDigit);
}
