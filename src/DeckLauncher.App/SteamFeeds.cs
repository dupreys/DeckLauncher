using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiteLauncher.Core;
using LiteLauncher.Core.Steam;

namespace DeckLauncher.App;

/// <summary>
/// What the Novidades / Amigos / Recomendados screens show, straight from Steam's public web endpoints.
/// Everything here is best-effort: any network or format problem gives an empty list, never a crash.
/// </summary>
internal static class SteamFeeds
{
    private const string Api = "https://api.steampowered.com";
    private const string Cdn = "https://cdn.cloudflare.steamstatic.com/steam/apps";

    public static string HeaderUrl(string appId) => $"{Cdn}/{appId}/header.jpg";

    public static string CapsuleUrl(string appId) => $"{Cdn}/{appId}/capsule_616x353.jpg";

    public static string AkamaiHeaderUrl(string appId) => $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";

    /// <summary>The header picture address the store itself reports (works for games with hashed file names).</summary>
    public static async Task<string?> StoreHeaderUrlAsync(HttpClient http, string appId)
    {
        try
        {
            var json = await http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty(appId).GetProperty("data").GetProperty("header_image").GetString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public static string CoverUrl(string appId) => $"{Cdn}/{appId}/library_600x900.jpg";

    public sealed record NewsData(string AppId, string GameName, string Title, string Url, string ImageUrl, DateTimeOffset Date);

    public sealed record UpdatedData(string AppId, string Name, long Size, DateTimeOffset Updated);

    public sealed record FriendData(string SteamId, string Name, string? Avatar, string? GameId, string? GameName, long LastLogoff);

    public sealed record ActivityData(string FriendName, string? Avatar, string AppId, string GameName, int Minutes);

    public sealed record ReleaseData(string AppId, string Name, DateTime Date);

    // ---- Novidades: announcements ---------------------------------------------------------------

    /// <summary>The newest announcements of the user's Steam games (installed ones, most played first).</summary>
    public static async Task<List<NewsData>> FetchNewsAsync(HttpClient http, IReadOnlyList<Game> games, CancellationToken ct = default)
    {
        using var gate = new SemaphoreSlim(6);
        var tasks = games.Take(24).Select(async game =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var url = $"{Api}/ISteamNews/GetNewsForApp/v2/?appid={game.Id}&count=2&maxlength=1500&feeds=steam_community_announcements";
                return ParseNews(await http.GetStringAsync(url, ct), game);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return new List<NewsData>();
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        var all = new List<NewsData>();
        foreach (var list in await Task.WhenAll(tasks)) all.AddRange(list);
        return all.OrderByDescending(n => n.Date).Take(12).ToList();
    }

    private static List<NewsData> ParseNews(string json, Game game)
    {
        var list = new List<NewsData>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("appnews", out var appNews) ||
            !appNews.TryGetProperty("newsitems", out var items) || items.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in items.EnumerateArray())
        {
            string? Str(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var title = Str("title");
            var url = Str("url");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url)) continue;
            var date = item.TryGetProperty("date", out var d) && d.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : DateTimeOffset.MinValue;
            var image = ExtractImage(Str("contents")) ?? HeaderUrl(game.Id);
            list.Add(new NewsData(game.Id, game.Name, WebUtility.HtmlDecode(title), url, image, date));
        }
        return list;
    }

    /// <summary>The first picture of an announcement ([img] tag), only when it is hosted by Steam.</summary>
    private static string? ExtractImage(string? contents)
    {
        if (string.IsNullOrEmpty(contents)) return null;
        var match = Regex.Match(contents, @"\[img\]\s*(.+?)\s*\[/img\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success) return null;

        var source = match.Groups[1].Value
            .Replace("{STEAM_CLAN_IMAGE}", "https://clan.fastly.steamstatic.com/images")
            .Replace("{STEAM_CLAN_LOC_IMAGE}", "https://clan.fastly.steamstatic.com/images");
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        var host = uri.Host.ToLowerInvariant();
        return host.EndsWith("steamstatic.com") || host.EndsWith("steamusercontent.com") ? uri.ToString() : null;
    }

    // ---- Novidades: recently updated on this PC ---------------------------------------------------

    public static List<UpdatedData> ReadRecentlyUpdated(string steamPath)
    {
        var result = new List<UpdatedData>();
        foreach (var library in SteamLibraryReader.GetLibraryFolders(steamPath))
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            try
            {
                foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    try
                    {
                        var state = VdfParser.Parse(File.ReadAllText(manifest))["AppState"];
                        var appId = state?.GetString("appid");
                        var name = state?.GetString("name");
                        if (state is null || string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name)) continue;
                        if ((state.GetLong("StateFlags") & 4) == 0 || SteamLibraryReader.IsIgnored(appId, name)) continue;
                        var updated = state.GetLong("LastUpdated");
                        if (updated <= 0) continue;
                        result.Add(new UpdatedData(appId, name, state.GetLong("SizeOnDisk"), DateTimeOffset.FromUnixTimeSeconds(updated)));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(u => u.Updated).Take(8).ToList();
    }

    // ---- Novidades: most played ---------------------------------------------------------------------

    public static async Task<List<string>> FetchMostPlayedAsync(HttpClient http, CancellationToken ct = default)
    {
        var ids = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"{Api}/ISteamChartsService/GetMostPlayedGames/v1/", ct));
            if (!doc.RootElement.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("ranks", out var ranks) || ranks.ValueKind != JsonValueKind.Array) return ids;
            foreach (var rank in ranks.EnumerateArray())
                if (rank.TryGetProperty("appid", out var id) && id.TryGetInt64(out var appId) && appId > 0) ids.Add(appId.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
        return ids.Take(14).ToList();
    }

    // ---- Amigos --------------------------------------------------------------------------------------

    /// <summary>All friends with their current game. Throws <see cref="UnauthorizedAccessException"/> for a bad key or a private friends list.</summary>
    public static async Task<List<FriendData>> FetchFriendsAsync(HttpClient http, string apiKey, string steamId, CancellationToken ct = default)
    {
        var key = Uri.EscapeDataString(apiKey.Trim());
        using var response = await http.GetAsync($"{Api}/ISteamUser/GetFriendList/v1/?key={key}&steamid={steamId}&relationship=friend", ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new UnauthorizedAccessException();
        response.EnsureSuccessStatusCode();
        var ids = SteamFriends.ParseFriendIds(await response.Content.ReadAsStringAsync(ct));

        var friends = new List<FriendData>();
        foreach (var batch in ids.Chunk(100))
        {
            var json = await http.GetStringAsync($"{Api}/ISteamUser/GetPlayerSummaries/v2/?key={key}&steamids={string.Join(',', batch)}", ct);
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("response", out var r) ||
                    !r.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array) continue;
                foreach (var p in players.EnumerateArray())
                {
                    string? Str(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    var id = Str("steamid");
                    if (id is null) continue;
                    var last = p.TryGetProperty("lastlogoff", out var l) && l.TryGetInt64(out var seconds) ? seconds : 0;
                    friends.Add(new FriendData(id, Str("personaname") ?? "Amigo", Str("avatarmedium") ?? Str("avatar"), Str("gameid"), Str("gameextrainfo"), last));
                }
            }
            catch (JsonException) { }
        }
        return friends;
    }

    /// <summary>What the most recently active friends played in the last two weeks.</summary>
    public static async Task<List<ActivityData>> FetchActivityAsync(HttpClient http, string apiKey, IReadOnlyList<FriendData> friends, CancellationToken ct = default)
    {
        var key = Uri.EscapeDataString(apiKey.Trim());
        using var gate = new SemaphoreSlim(5);
        var tasks = friends.Select(async friend =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var json = await http.GetStringAsync($"{Api}/IPlayerService/GetRecentlyPlayedGames/v1/?key={key}&steamid={friend.SteamId}&count=2", ct);
                var list = new List<ActivityData>();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("response", out var r) ||
                    !r.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array) return list;
                foreach (var g in games.EnumerateArray())
                {
                    if (!g.TryGetProperty("appid", out var id) || !id.TryGetInt64(out var appId)) continue;
                    var name = g.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    var minutes = g.TryGetProperty("playtime_2weeks", out var m) && m.TryGetInt32(out var mins) ? mins : 0;
                    if (!string.IsNullOrWhiteSpace(name) && minutes > 0) list.Add(new ActivityData(friend.Name, friend.Avatar, appId.ToString(), name, minutes));
                }
                return list;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return new List<ActivityData>();
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        var all = new List<ActivityData>();
        foreach (var list in await Task.WhenAll(tasks)) all.AddRange(list);
        return all.Take(20).ToList();
    }

    // ---- Recomendados: release calendar ---------------------------------------------------------------

    /// <summary>Popular new and upcoming releases with their release day (from the Steam store search, in English so the dates parse).</summary>
    public static async Task<List<ReleaseData>> FetchReleasesAsync(HttpClient http, CancellationToken ct = default)
    {
        var all = new List<ReleaseData>();
        foreach (var filter in new[] { "popularnew", "popularcomingsoon" })
        {
            try
            {
                var url = $"https://store.steampowered.com/search/results/?query&start=0&count=50&filter={filter}&infinite=1&cc=us&l=english";
                all.AddRange(ParseSearch(await http.GetStringAsync(url, ct)));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
        }
        return all.GroupBy(r => r.AppId).Select(g => g.First()).ToList();
    }

    private static readonly string[] DateFormats = { "d MMM, yyyy", "dd MMM, yyyy", "MMM d, yyyy", "MMM dd, yyyy" };

    private static List<ReleaseData> ParseSearch(string json)
    {
        var list = new List<ReleaseData>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results_html", out var htmlElement) || htmlElement.ValueKind != JsonValueKind.String) return list;
        var html = htmlElement.GetString() ?? "";

        foreach (Match row in Regex.Matches(html, @"data-ds-appid=""(\d+)""(.*?)</a>", RegexOptions.Singleline))
        {
            var body = row.Groups[2].Value;
            var title = Regex.Match(body, @"class=""title"">(.*?)</span>", RegexOptions.Singleline);
            var released = Regex.Match(body, @"search_released[^>]*>\s*(.*?)\s*</div>", RegexOptions.Singleline);
            if (!title.Success || !released.Success) continue;
            if (!DateTime.TryParseExact(WebUtility.HtmlDecode(released.Groups[1].Value).Trim(), DateFormats,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            list.Add(new ReleaseData(row.Groups[1].Value, WebUtility.HtmlDecode(title.Groups[1].Value).Trim(), date));
        }
        return list;
    }
}
