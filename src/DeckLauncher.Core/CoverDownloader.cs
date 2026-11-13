using System.Net.Http;

namespace DeckLauncher.Core;

public enum StoreArtKind { Cover, Backdrop }

/// <summary>
/// Fetches Steam store artwork for games whose cover Steam has not cached locally yet (new installs, small apps).
/// Files are kept in a cache folder; nothing is downloaded twice. Epic offers no public artwork, so those keep the letter.
/// </summary>
public static class CoverDownloader
{
    private const string Cdn = "https://cdn.cloudflare.steamstatic.com/steam/apps";

    public static string CachePath(string cacheDir, Game game, StoreArtKind kind) =>
        Path.Combine(cacheDir, $"{(game.Source == "Epic" ? "epic_" + SafeName(game.Id) : game.Source == "GOG" ? "gog_" + SafeName(game.Id) : "steam_" + game.Id)}_{(kind == StoreArtKind.Cover ? "cover" : "hero")}.jpg");

    private static string SafeName(string id) => new(id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

    public static IReadOnlyList<string> Urls(Game game, StoreArtKind kind) =>
        game.Source == "Epic"
            ? (EpicUrl(game, kind) is { } epic ? new[] { epic } : Array.Empty<string>())
            : game.Source == "GOG" ? GogUrls(game, kind)
            : kind == StoreArtKind.Cover
        ? new[] { $"{Cdn}/{game.Id}/library_600x900.jpg", $"{Cdn}/{game.Id}/header.jpg" }
        : new[] { $"{Cdn}/{game.Id}/library_hero.jpg", $"{Cdn}/{game.Id}/page_bg_generated_v6b.jpg" };

    /// <summary>The cached file when it exists already; null otherwise. Never touches the network.</summary>
    public static string? Cached(string cacheDir, Game game, StoreArtKind kind)
    {
        if (!HasStoreArt(game, kind)) return null;
        var path = CachePath(cacheDir, game, kind);
        return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
    }

    /// <summary>Downloads the artwork (first URL that works) into the cache and returns its path; null when none was available.</summary>
    public static async Task<string?> DownloadAsync(HttpClient http, string cacheDir, Game game, StoreArtKind kind, CancellationToken ct = default)
    {
        if (!HasStoreArt(game, kind)) return null;
        if (Cached(cacheDir, game, kind) is { } existing) return existing;

        foreach (var url in Urls(game, kind))
        {
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) continue;
                if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true) continue;

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 512) continue; // an error page, not a picture

                Directory.CreateDirectory(cacheDir);
                var path = CachePath(cacheDir, game, kind);
                var temp = path + ".part";
                await File.WriteAllBytesAsync(temp, bytes, ct);
                File.Move(temp, path, overwrite: true);
                return path;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
            {
                // offline or blocked: the next refresh tries again
            }
        }
        return null;
    }

    /// <summary>Only pictures served by Epic itself over https are downloaded.</summary>
    public static bool IsEpicImageUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.EndsWith("epicgames.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("unrealengine.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("epicgamescdn.com", StringComparison.OrdinalIgnoreCase));

    private static string? EpicUrl(Game game, StoreArtKind kind)
    {
        var url = kind == StoreArtKind.Cover ? game.CoverUrl : game.WideUrl;
        return IsEpicImageUrl(url) ? url : null;
    }

    /// <summary>GOG's cover sits at the address the library gives, plus a size suffix; the first suffix that answers is kept. (GOG documents none of this: if none works the game keeps the letter.)</summary>
    private static IReadOnlyList<string> GogUrls(Game game, StoreArtKind kind) =>
        kind == StoreArtKind.Cover && Gog.GogAccountClient.IsGogImageUrl(game.CoverUrl)
            ? new[] { game.CoverUrl + "_product_card_v2_mobile_slider_639.jpg", game.CoverUrl + ".jpg" }
            : Array.Empty<string>();

    private static bool HasStoreArt(Game game, StoreArtKind kind) =>
        IsSteamAppId(game) || (game.Source == "Epic" && EpicUrl(game, kind) is not null)
        || (game.Source == "GOG" && kind == StoreArtKind.Cover && Gog.GogAccountClient.IsGogImageUrl(game.CoverUrl));

    private static bool IsSteamAppId(Game game) =>
        game.Source == "Steam" && game.Id.Length > 0 && game.Id.All(char.IsAsciiDigit);
}
