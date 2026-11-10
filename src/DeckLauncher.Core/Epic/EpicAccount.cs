using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DeckLauncher.Core.Epic;

/// <summary>A game of the Epic library, as the account reports it.</summary>
public sealed class EpicLibraryGame
{
    public string AppName { get; set; } = "";
    public string Title { get; set; } = "";
    public string CatalogNamespace { get; set; } = "";
    public string CatalogItemId { get; set; } = "";
    public string? CoverUrl { get; set; }
    /// <summary>Wide picture (wallpaper) and logo from Epic's catalog.</summary>
    public string? WideUrl { get; set; }
    public string? LogoUrl { get; set; }
    /// <summary>True once the catalog was read with the wide/logo pictures in mind (older saved lists lack them and are read again once).</summary>
    public bool ArtChecked { get; set; }
}

/// <summary>The tokens Epic hands out after the web sign-in. The password never reaches the launcher.</summary>
public sealed class EpicTokens
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("token_type")] public string TokenType { get; set; } = "bearer";
    [JsonPropertyName("account_id")] public string AccountId { get; set; } = "";
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    [JsonPropertyName("refresh_expires_at")] public DateTimeOffset RefreshExpiresAt { get; set; }

    public bool IsValid => AccessToken.Length > 0 && AccountId.Length > 0;
    public bool AccessExpired => ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(2);
    public bool CanRefresh => RefreshToken.Length > 0 && RefreshExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2);

    public static EpicTokens? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var tokens = JsonSerializer.Deserialize<EpicTokens>(json);
            return tokens is { IsValid: true } ? tokens : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}

/// <summary>Raised when Epic refuses the sign-in (expired or revoked): the user has to sign in again.</summary>
public sealed class EpicSignInException : Exception
{
    public EpicSignInException(string message) : base(message) { }
}

/// <summary>
/// The Epic account sign-in and library, the same way the Epic launcher, Legendary and Playnite do it: the user signs in on
/// Epic's own web page, Epic shows a one-time code, and that code is exchanged for tokens. The library is then read from
/// Epic's library and catalog services. Only the user's own account is read; nothing is written.
/// </summary>
public sealed class EpicAccountClient
{
    // The Epic launcher's own public client (the same one Playnite uses).
    public const string ClientId = "34a02cf8f4414e29b15921876da36f9a";
    private const string BasicAuth = "MzRhMDJjZjhmNDQxNGUyOWIxNTkyMTg3NmRhMzZmOWE6ZGFhZmJjY2M3Mzc3NDUwMzlkZmZlNTNkOTRmYzc2Y2Y=";

    private const string OauthUrl = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token";
    private const string AccountUrl = "https://account-public-service-prod03.ol.epicgames.com/account/api/public/account/";
    private const string LibraryUrl = "https://library-service.live.use1a.on.epicgames.com/library/api/public/items?includeMetadata=true&platform=Windows";
    private const string CatalogUrl = "https://catalog-public-service-prod06.ol.epicgames.com/catalog/api/shared/namespace/";

    /// <summary>Opens Epic's official sign-in page; after signing in, Epic shows the code (the "authorizationCode").</summary>
    public static string LoginUrl { get; } =
        "https://www.epicgames.com/id/login?redirectUrl=" +
        Uri.EscapeDataString($"https://www.epicgames.com/id/api/redirect?clientId={ClientId}&responseType=code");

    /// <summary>Opens the Epic launcher on the library page, where the game's Install button is.</summary>
    public const string LibraryPageUri = "com.epicgames.launcher://store/library";

    /// <summary>Link that makes the Epic launcher open the game and ask to install it.</summary>
    public static string InstallUri(string catalogNamespace, string catalogItemId, string appName) =>
        $"com.epicgames.launcher://apps/{Uri.EscapeDataString(catalogNamespace)}%3A{Uri.EscapeDataString(catalogItemId)}%3A{Uri.EscapeDataString(appName)}?action=install";

    private readonly HttpClient _http;

    public EpicAccountClient(HttpClient http) => _http = http;

    /// <summary>Finds the sign-in code in what the user copied: the whole page text ({"authorizationCode":"…"}) or only the code.</summary>
    public static string? ExtractAuthCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000) return null;
        var match = Regex.Match(text, "\"authorizationCode\"\\s*:\\s*\"(?<code>[0-9a-fA-F]{32})\"");
        if (match.Success) return match.Groups["code"].Value;
        var bare = text.Trim().Trim('"');
        return Regex.IsMatch(bare, "^[0-9a-fA-F]{32}$") ? bare : null;
    }

    public Task<EpicTokens> SignInAsync(string authorizationCode, CancellationToken ct = default) =>
        RequestTokensAsync($"grant_type=authorization_code&code={Uri.EscapeDataString(authorizationCode)}&token_type=eg1", ct);

    private Task<EpicTokens> RefreshAsync(EpicTokens tokens, CancellationToken ct) =>
        RequestTokensAsync($"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(tokens.RefreshToken)}&token_type=eg1", ct);

    private async Task<EpicTokens> RequestTokensAsync(string form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, OauthUrl) { Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded") };
        request.Headers.Authorization = new AuthenticationHeaderValue("basic", BasicAuth);
        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        var tokens = response.IsSuccessStatusCode ? EpicTokens.FromJson(text) : null;
        return tokens ?? throw new EpicSignInException("A Epic não aceitou o código (ele vale poucos minutos e só uma vez). Tente entrar de novo.");
    }

    /// <summary>The tokens to use now: refreshed when the access token is about to expire. <paramref name="current"/> is replaced when it changed.</summary>
    public async Task<EpicTokens> EnsureFreshAsync(EpicTokens current, CancellationToken ct = default)
    {
        if (!current.AccessExpired) return current;
        if (!current.CanRefresh) throw new EpicSignInException("A sessão da Epic expirou. Entre de novo.");
        return await RefreshAsync(current, ct);
    }

    public async Task<string?> GetDisplayNameAsync(EpicTokens tokens, CancellationToken ct = default)
    {
        var json = await GetAsync(AccountUrl + Uri.EscapeDataString(tokens.AccountId), tokens, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("displayName", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> GetAsync(string url, EpicTokens tokens, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(string.IsNullOrEmpty(tokens.TokenType) ? "bearer" : tokens.TokenType, tokens.AccessToken);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new EpicSignInException("A sessão da Epic expirou. Entre de novo.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private const string AssetsUrl = "https://launcher-public-service-prod06.ol.epicgames.com/launcher/api/public/assets/Windows?label=Live";

    /// <summary>The newest build of every game Epic lists for this account: appName → buildVersion.</summary>
    public async Task<Dictionary<string, string>> GetLatestBuildsAsync(EpicTokens tokens, CancellationToken ct = default) =>
        ParseBuilds(await GetAsync(AssetsUrl, tokens, ct));

    public static Dictionary<string, string> ParseBuilds(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string? S(string n) => item.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (S("appName") is { Length: > 0 } app && S("buildVersion") is { Length: > 0 } build) result[app] = build;
            }
        }
        catch (JsonException) { /* an unexpected answer: no updates are reported */ }
        return result;
    }

    /// <summary>One entry of the account's library, before the catalog says what it is.</summary>
    public sealed record Asset(string AppName, string CatalogNamespace, string CatalogItemId, string? SandboxType);

    public static IReadOnlyList<Asset> ParseAssets(string json, out string? nextCursor)
    {
        nextCursor = null;
        var list = new List<Asset>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("responseMetadata", out var meta) && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind == JsonValueKind.String)
                nextCursor = cursor.GetString();
            if (!root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array) return list;

            foreach (var record in records.EnumerateArray())
            {
                string? S(string n) => record.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var app = S("appName");
                var ns = S("namespace");
                var id = S("catalogItemId");
                if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(id)) continue;
                if (ns == "ue" || S("sandboxType") == "PRIVATE") continue; // Unreal Engine content and private sandboxes are not games
                list.Add(new Asset(app, ns, id, S("sandboxType")));
            }
        }
        catch (JsonException)
        {
            nextCursor = null;
        }
        return list;
    }

    /// <summary>The game described by a catalog item, or null for add-ons, extras, tools and games that Epic does not install.</summary>
    public static EpicLibraryGame? ParseCatalogItem(string json, Asset asset)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            // The answer is { "<catalogItemId>": { ... } }
            if (!root.TryGetProperty(asset.CatalogItemId, out var item) || item.ValueKind != JsonValueKind.Object) return null;

            string? S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var title = S(item, "title");
            if (string.IsNullOrWhiteSpace(title)) return null;

            var paths = new List<string>();
            if (item.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
                foreach (var c in categories.EnumerateArray()) if (S(c, "path") is { } p) paths.Add(p);

            if (!paths.Contains("applications")) return null;
            var isAddon = item.TryGetProperty("mainGameItem", out var main) && main.ValueKind == JsonValueKind.Object;
            if (isAddon && !paths.Contains("addons/launchable")) return null;
            if (paths.Any(p => p is "digitalextras" or "plugins" or "plugins/engine")) return null;

            // Games run by another store's launcher (EA app, Ubisoft Connect) cannot be installed from the Epic launcher.
            if (item.TryGetProperty("customAttributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
            {
                if (attributes.TryGetProperty("ThirdPartyManagedApp", out var third) && S(third, "value") is { Length: > 0 }) return null;
                if (attributes.TryGetProperty("partnerLinkType", out var link) && string.Equals(S(link, "value"), "ubisoft", StringComparison.OrdinalIgnoreCase)) return null;
            }

            string? cover = null, wide = null, logo = null;
            if (item.TryGetProperty("keyImages", out var images) && images.ValueKind == JsonValueKind.Array)
            {
                string? Image(string type) => images.EnumerateArray()
                    .Where(i => string.Equals(S(i, "type"), type, StringComparison.OrdinalIgnoreCase)).Select(i => S(i, "url")).FirstOrDefault(u => !string.IsNullOrEmpty(u));
                cover = Image("DieselGameBoxTall") ?? Image("OfferImageTall") ?? Image("Thumbnail") ?? Image("DieselGameBox");
                wide = Image("DieselGameBox") ?? Image("OfferImageWide") ?? Image("DieselStoreFrontWide");
                logo = Image("DieselGameBoxLogo");
            }

            return new EpicLibraryGame
            {
                AppName = asset.AppName,
                Title = title.Replace("™", "").Replace("®", "").Trim(),
                CatalogNamespace = asset.CatalogNamespace,
                CatalogItemId = asset.CatalogItemId,
                CoverUrl = cover,
                WideUrl = wide,
                LogoUrl = logo,
                ArtChecked = true,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the account's library. Games already in <paramref name="known"/> are not looked up again; only new ones cost a
    /// catalog request (done a few at a time).
    /// </summary>
    public async Task<List<EpicLibraryGame>> ReadLibraryAsync(EpicTokens tokens, IReadOnlyCollection<EpicLibraryGame> known, ISet<string> skipped, CancellationToken ct = default)
    {
        var assets = new List<Asset>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = cursor is null ? LibraryUrl : $"{LibraryUrl}&cursor={Uri.EscapeDataString(cursor)}";
            assets.AddRange(ParseAssets(await GetAsync(url, tokens, ct), out cursor));
        }
        while (cursor is not null && ++pages < 100);

        var byItem = known.ToDictionary(g => g.CatalogItemId, StringComparer.OrdinalIgnoreCase);
        var result = new List<EpicLibraryGame>();
        var todo = new List<Asset>();
        var stale = new Dictionary<string, EpicLibraryGame>(StringComparer.OrdinalIgnoreCase); // known games read again for their pictures
        foreach (var asset in assets.DistinctBy(a => a.CatalogItemId, StringComparer.OrdinalIgnoreCase))
        {
            if (skipped.Contains(asset.CatalogItemId)) continue; // an add-on or tool the catalog already said is not a game
            if (byItem.TryGetValue(asset.CatalogItemId, out var existing))
            {
                if (existing.ArtChecked) result.Add(existing);
                else { stale[asset.CatalogItemId] = existing; todo.Add(asset); }
            }
            else todo.Add(asset);
        }

        using var gate = new SemaphoreSlim(5);
        var lookups = todo.Select(async asset =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var url = $"{CatalogUrl}{Uri.EscapeDataString(asset.CatalogNamespace)}/bulk/items?id={Uri.EscapeDataString(asset.CatalogItemId)}&country=US&locale=en-US&includeMainGameDetails=true";
                var json = await GetAsync(url, tokens, ct);
                var game = ParseCatalogItem(json, asset);
                // Only an answer that does describe the item (and was refused) marks it as "not a game".
                if (game is null && json.Contains(asset.CatalogItemId, StringComparison.OrdinalIgnoreCase)) lock (skipped) skipped.Add(asset.CatalogItemId);
                return game;
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                return null; // that one is looked up again on the next sync
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        var looked = await Task.WhenAll(lookups);
        for (var i = 0; i < todo.Count; i++)
        {
            if (looked[i] is { } game) result.Add(game);
            else if (stale.TryGetValue(todo[i].CatalogItemId, out var old) && !skipped.Contains(todo[i].CatalogItemId)) result.Add(old); // keep it until the lookup works
        }

        return result.GroupBy(g => g.AppName, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                     .OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
