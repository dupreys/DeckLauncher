using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DeckLauncher.Core.Gog;

/// <summary>A game of the GOG library, as the account reports it.</summary>
public sealed class GogLibraryGame
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>The name in the store address (gog.com/game/&lt;slug&gt;).</summary>
    public string Slug { get; set; } = "";
    /// <summary>The cover's address on GOG's image server, without the size suffix (https).</summary>
    public string? ImageBase { get; set; }
}

/// <summary>The tokens GOG hands out after the web sign-in. The password never reaches the launcher.</summary>
public sealed class GogTokens
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }

    public bool IsValid => AccessToken.Length > 0 && RefreshToken.Length > 0;
    public bool AccessExpired => ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(2);

    public static GogTokens? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var tokens = JsonSerializer.Deserialize<GogTokens>(json);
            return tokens is { IsValid: true } ? tokens : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Reads GOG's token answer (access_token, refresh_token, expires_in in seconds, user_id).</summary>
    public static GogTokens? FromResponse(string json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? S(string n) => root.TryGetProperty(n, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null : null;
            var seconds = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var s) ? s : 3600;
            var tokens = new GogTokens
            {
                AccessToken = S("access_token") ?? "",
                RefreshToken = S("refresh_token") ?? "",
                UserId = S("user_id") ?? "",
                ExpiresAt = now.AddSeconds(Math.Clamp(seconds, 60, 86400)),
            };
            return tokens.IsValid ? tokens : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Raised when GOG refuses the sign-in (expired or revoked): the user has to sign in again.</summary>
public sealed class GogSignInException : Exception
{
    public GogSignInException(string message) : base(message) { }
}

/// <summary>
/// The GOG account sign-in and library, the way GOG Galaxy and Heroic do it: the user signs in on GOG's own web page, which ends on
/// a page whose address carries a one-time code; that code is exchanged for tokens. Only the user's own library is read.
/// These are GOG's unofficial but widely used endpoints (the client id is the one of GOG Galaxy) and may change.
/// </summary>
public sealed class GogAccountClient
{
    private const string ClientId = "46899977096215655";
    private const string ClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
    private const string RedirectUri = "https://embed.gog.com/on_login_success?origin=client";

    /// <summary>The page GOG lands on after a successful sign-in; its address carries the code.</summary>
    public const string RedirectPrefix = "https://embed.gog.com/on_login_success";

    public static string LoginUrl { get; } =
        $"https://auth.gog.com/auth?client_id={ClientId}&redirect_uri={Uri.EscapeDataString(RedirectUri)}&response_type=code&layout=client2";

    private readonly HttpClient _http;

    public GogAccountClient(HttpClient http) => _http = http;

    /// <summary>The code from the address GOG ended on (or from a pasted address / bare code); null when there is none.</summary>
    public static string? ExtractAuthCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000) return null;
        var trimmed = text.Trim().Trim('"');
        var match = Regex.Match(trimmed, @"[?&]code=(?<code>[A-Za-z0-9_\-]{20,})");
        if (match.Success && trimmed.Contains("gog.com", StringComparison.OrdinalIgnoreCase)) return match.Groups["code"].Value;
        return Regex.IsMatch(trimmed, @"^[A-Za-z0-9_\-]{40,}$") ? trimmed : null;
    }

    public Task<GogTokens> SignInAsync(string code, CancellationToken ct = default) =>
        RequestTokensAsync($"grant_type=authorization_code&code={Uri.EscapeDataString(code)}&redirect_uri={Uri.EscapeDataString(RedirectUri)}", ct);

    private async Task<GogTokens> RequestTokensAsync(string query, CancellationToken ct)
    {
        var url = $"https://auth.gog.com/token?client_id={ClientId}&client_secret={ClientSecret}&{query}";
        using var response = await _http.GetAsync(url, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        var tokens = response.IsSuccessStatusCode ? GogTokens.FromResponse(text, DateTimeOffset.UtcNow) : null;
        return tokens ?? throw new GogSignInException("A GOG não aceitou o código (ele vale poucos minutos e só uma vez). Tente entrar de novo.");
    }

    /// <summary>The tokens to use now: refreshed when the access token is about to expire (GOG replaces the refresh token too).</summary>
    public async Task<GogTokens> EnsureFreshAsync(GogTokens current, CancellationToken ct = default)
    {
        if (!current.AccessExpired) return current;
        if (current.RefreshToken.Length == 0) throw new GogSignInException("A sessão da GOG expirou. Entre de novo.");
        var fresh = await RequestTokensAsync($"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(current.RefreshToken)}", ct);
        if (fresh.UserId.Length == 0) fresh.UserId = current.UserId;
        return fresh;
    }

    private async Task<string> GetAsync(string url, GogTokens tokens, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new GogSignInException("A sessão da GOG expirou. Entre de novo.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<string?> GetUserNameAsync(GogTokens tokens, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(await GetAsync("https://embed.gog.com/userData.json", tokens, ct));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("username", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads every page of the account's games (movies are left out).</summary>
    public async Task<List<GogLibraryGame>> ReadLibraryAsync(GogTokens tokens, CancellationToken ct = default)
    {
        var all = new List<GogLibraryGame>();
        var page = 1;
        var total = 1;
        do
        {
            var json = await GetAsync($"https://embed.gog.com/account/getFilteredProducts?mediaType=1&page={page}", tokens, ct);
            all.AddRange(ParseProducts(json, out var pages));
            total = Math.Min(pages, 100);
        }
        while (++page <= total);

        return all.GroupBy(g => g.Id, StringComparer.Ordinal).Select(g => g.First())
                  .OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static IReadOnlyList<GogLibraryGame> ParseProducts(string json, out int totalPages)
    {
        totalPages = 1;
        var list = new List<GogLibraryGame>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return list;
            if (root.TryGetProperty("totalPages", out var tp) && tp.ValueKind == JsonValueKind.Number && tp.TryGetInt32(out var n) && n > 0) totalPages = n;
            if (!root.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array) return list;

            foreach (var p in products.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                string? S(string name) => p.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null : null;
                var id = S("id");
                var title = S("title");
                if (string.IsNullOrWhiteSpace(id) || !id.All(char.IsAsciiDigit) || string.IsNullOrWhiteSpace(title)) continue;
                if (p.TryGetProperty("isGame", out var isGame) && isGame.ValueKind == JsonValueKind.False) continue;
                if (p.TryGetProperty("isMovie", out var isMovie) && isMovie.ValueKind == JsonValueKind.True) continue;
                list.Add(new GogLibraryGame { Id = id, Title = title.Replace("™", "").Replace("®", "").Trim(), Slug = S("slug") ?? "", ImageBase = ImageBase(S("image")) });
            }
        }
        catch (JsonException)
        {
            totalPages = 1;
        }
        return list;
    }

    /// <summary>"//images-4.gog.com/abc" → "https://images-4.gog.com/abc"; null when it is not a picture on GOG's servers.</summary>
    public static string? ImageBase(string? image)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        var url = image.StartsWith("//", StringComparison.Ordinal) ? "https:" + image : image;
        return IsGogImageUrl(url) ? url : null;
    }

    public static bool IsGogImageUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.EndsWith("gog.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("gog-statics.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>The game's page in GOG's store.</summary>
    public static string StorePageUrl(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? "https://www.gog.com/games" : "https://www.gog.com/en/game/" + Uri.EscapeDataString(slug);
}
