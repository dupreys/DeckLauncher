using System.Globalization;
using System.Text.Json;
using DeckLauncher.Core.Epic;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.Core;

/// <summary>Extra information shown in the game menu. Every field is optional: offline means "just less to show".</summary>
public sealed record GameDetails
{
    public string? Developer { get; init; }
    public string? ReleaseDate { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    public string? Description { get; init; }
    public int? PlayersNow { get; init; }
    public int? PlaytimeMinutes { get; init; }
}

public static class GameInfo
{
    /// <summary>The store page for the game, in the store client when possible.</summary>
    public static string StoreUrl(Game game) => game.Source switch
    {
        SteamProvider.ProviderName => $"steam://store/{game.Id}",
        EpicProvider.ProviderName => "https://store.epicgames.com/browse?q=" +
                                     Uri.EscapeDataString(game.OriginalName ?? game.Name) + "&sortBy=relevancy",
        "GOG" => "https://www.gog.com/en/games?query=" + Uri.EscapeDataString(game.OriginalName ?? game.Name),
        _ => "https://www.google.com/search?q=" + Uri.EscapeDataString(game.OriginalName ?? game.Name),
    };

    /// <summary>Browser fallback when the store client is not installed.</summary>
    public static string StoreWebUrl(Game game) => game.Source == SteamProvider.ProviderName
        ? $"https://store.steampowered.com/app/{game.Id}/"
        : StoreUrl(game);

    // Details already received this session (one game page opened twice does not ask the stores again).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, GameDetails> Cache = new();

    /// <summary>Steam's store language name for the launcher's language.</summary>
    public static string SteamLanguage(string code) => code switch { "pt" => "brazilian", "es" => "spanish", _ => "english" };

    public static async Task<GameDetails> FetchAsync(HttpClient http, Game game, string? steamPath, string? steamId64,
                                                     CancellationToken cancellationToken = default)
    {
        var key = game.Source + "|" + game.Id + "|" + Loc.Language;
        var language = SteamLanguage(Loc.Language);

        if (game.Source == SteamProvider.ProviderName)
        {
            var playtime = steamPath is null ? null : SteamPlaytime.Read(steamPath, steamId64, game.Id);
            var storeTask = GetStringOrNull(http, $"https://store.steampowered.com/api/appdetails?appids={game.Id}&l={language}", cancellationToken);
            var playersTask = GetStringOrNull(http, $"https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={game.Id}", cancellationToken);
            await Task.WhenAll(storeTask, playersTask);

            var details = ParseSteamStore(await storeTask, game.Id) ?? new GameDetails();
            return details with { PlayersNow = ParsePlayerCount(await playersTask), PlaytimeMinutes = playtime };
        }

        if (Cache.TryGetValue(key, out var cached)) return cached;

        // GOG: the store's own text first; every other store (and GOG without a text) borrows the Steam page of the game with the same name.
        GameDetails? found = null;
        if (game.Source == "GOG" && game.Id.All(char.IsAsciiDigit) && game.Id.Length > 0)
        {
            var locale = Loc.Language switch { "pt" => "pt-BR", "es" => "es-ES", _ => "en-US" };
            found = ParseGogProduct(await GetStringOrNull(http, $"https://api.gog.com/products/{game.Id}?expand=description&locale={locale}", cancellationToken));
        }
        if (found?.Description is null)
        {
            var steamId = await SteamStoreSearch.FindAppIdAsync(http, game.OriginalName ?? game.Name, cancellationToken);
            if (steamId is null && game.OriginalName is not null && game.OriginalName != game.Name)
                steamId = await SteamStoreSearch.FindAppIdAsync(http, game.Name, cancellationToken);
            if (steamId is not null &&
                ParseSteamStore(await GetStringOrNull(http, $"https://store.steampowered.com/api/appdetails?appids={steamId}&l={language}", cancellationToken), steamId) is { } fromSteam)
                found = found is null ? fromSteam : fromSteam with { Developer = found.Developer ?? fromSteam.Developer };
        }

        var result = found ?? new GameDetails();
        if (found is not null) Cache[key] = result; // an empty answer (offline, not found) is asked again next time
        return result;
    }

    /// <summary>GOG's product answer (expand=description): the lead text, or the full text, without HTML.</summary>
    public static GameDetails? ParseGogProduct(string? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("description", out var d) || d.ValueKind != JsonValueKind.Object) return null;
            string? Part(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var text = CleanText(Part("lead")) ?? CleanText(Part("full"));
            return text is null ? null : new GameDetails { Description = text };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>HTML to plain text, in at most about 600 characters (cut at a word).</summary>
    public static string? CleanText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = System.Text.RegularExpressions.Regex.Replace(html, @"<\s*(br|/p|/li)\s*/?>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        text = System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
        if (text.Length == 0) return null;
        if (text.Length <= 600) return text;
        var cut = text.LastIndexOf(' ', 600);
        return text[..(cut > 300 ? cut : 600)].TrimEnd(' ', ',', ';', '.') + "…";
    }

    private static async Task<string?> GetStringOrNull(HttpClient http, string url, CancellationToken ct)
    {
        try { return await http.GetStringAsync(url, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    public static GameDetails? ParseSteamStore(string? json, string appId)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(appId, out var app) ||
                !app.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True ||
                !app.TryGetProperty("data", out var data)) return null;

            string? Str(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var developer = data.TryGetProperty("developers", out var devs) && devs.ValueKind == JsonValueKind.Array
                ? string.Join(", ", devs.EnumerateArray().Select(d => d.GetString()).Where(d => !string.IsNullOrEmpty(d)))
                : null;
            var genres = data.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array
                ? g.EnumerateArray().Select(x => Str(x, "description")).OfType<string>().ToList()
                : new List<string>();
            var date = data.TryGetProperty("release_date", out var rd) ? Str(rd, "date") : null;

            return new GameDetails
            {
                Developer = string.IsNullOrEmpty(developer) ? null : developer,
                ReleaseDate = date,
                Genres = genres,
                Description = System.Net.WebUtility.HtmlDecode(Str(data, "short_description") ?? "").Trim() is { Length: > 0 } d ? d : null,
            };
        }
        catch (JsonException) { return null; }
    }

    public static int? ParsePlayerCount(string? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("response", out var r) &&
                   r.TryGetProperty("player_count", out var c) && c.TryGetInt32(out var n) ? n : null;
        }
        catch (JsonException) { return null; }
    }

    public static string FormatPlaytime(int minutes) =>
        minutes < 60 ? Loc.F("{0} min", minutes) : Loc.F("{0} hrs {1} min", minutes / 60, minutes % 60);

    public static string FormatSize(long bytes) => bytes <= 0
        ? "—"
        : bytes >= 1L << 30
            ? (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB";
}

/// <summary>Your playtime, as the Steam client stores it in userdata\&lt;account&gt;\config\localconfig.vdf.</summary>
public static class SteamPlaytime
{
    private const long SteamId64Base = 76561197960265728;

    public static int? Read(string steamPath, string? steamId64, string appId)
    {
        try
        {
            var userdata = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userdata)) return null;

            // The signed-in account first; otherwise any account on this PC that has a value.
            var folders = new List<string>();
            if (long.TryParse(steamId64, out var id64) && id64 > SteamId64Base)
                folders.Add(Path.Combine(userdata, (id64 - SteamId64Base).ToString(CultureInfo.InvariantCulture)));
            folders.AddRange(Directory.EnumerateDirectories(userdata));

            foreach (var folder in folders.Distinct())
            {
                var file = Path.Combine(folder, "config", "localconfig.vdf");
                if (!File.Exists(file)) continue;
                if (Load(file).TryGetValue(appId, out var minutes)) return minutes;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Playtime in minutes of every game in the account's localconfig.vdf, read once (appId -> minutes).</summary>
    public static IReadOnlyDictionary<string, int> ReadAll(string steamPath, string? steamId64)
    {
        var result = new Dictionary<string, int>();
        try
        {
            var userdata = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userdata)) return result;

            var folders = new List<string>();
            if (long.TryParse(steamId64, out var id64) && id64 > SteamId64Base)
                folders.Add(Path.Combine(userdata, (id64 - SteamId64Base).ToString(CultureInfo.InvariantCulture)));
            folders.AddRange(Directory.EnumerateDirectories(userdata));

            foreach (var folder in folders.Distinct())
            {
                var file = Path.Combine(folder, "config", "localconfig.vdf");
                if (!File.Exists(file)) continue;
                var all = Load(file);
                if (all.Count == 0) continue;
                return all;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return result;
    }

    // localconfig.vdf can be several MB: it is parsed again only when Steam changed the file.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Stamp, IReadOnlyDictionary<string, int> Times)> Parsed = new();

    private static IReadOnlyDictionary<string, int> Load(string file)
    {
        var stamp = File.GetLastWriteTimeUtc(file);
        if (Parsed.TryGetValue(file, out var known) && known.Stamp == stamp) return known.Times;
        var times = ParseAllLocalConfig(File.ReadAllText(file));
        Parsed[file] = (stamp, times);
        return times;
    }

    public static IReadOnlyDictionary<string, int> ParseAllLocalConfig(string vdf)
    {
        var result = new Dictionary<string, int>();
        var steam = VdfParser.Parse(vdf)["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"];
        var apps = steam?["apps"] ?? steam?["Apps"];
        if (apps is null) return result;
        foreach (var (id, node) in apps.Children)
        {
            var minutes = node.GetLong("Playtime", -1);
            if (minutes > 0 && id.All(char.IsAsciiDigit)) result[id] = (int)Math.Min(minutes, int.MaxValue);
        }
        return result;
    }

    public static int? ParseLocalConfig(string vdf, string appId)
    {
        var root = VdfParser.Parse(vdf);
        var app = root["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"]?["apps"]?[appId]
                  ?? root["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"]?["Apps"]?[appId];
        var value = app?.GetLong("Playtime", -1) ?? -1;
        return value >= 0 ? (int)value : null;
    }
}
