using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeckLauncher.Core.Epic;

namespace DeckLauncher.Core.GameAchievements;

/// <summary>One achievement of a game, with the player's state.</summary>
public sealed record Achievement(string Id, string Name, string? Description, string? IconUrl, bool Unlocked, DateTimeOffset? UnlockedAt, bool Hidden = false, double? RarityPercent = null);

/// <summary>The achievements of one game. <see cref="Problem"/> says why the list is empty or incomplete.</summary>
public sealed class AchievementSet
{
    public List<Achievement> Items { get; init; } = new();

    /// <summary>null (all fine), "noid" (no Steam account), "private" (Steam would not tell: private profile/game details, or offline), "none" (the game has no achievements),
    /// "signin" (Epic account not connected), "noprogress" (Epic gave the list but not the player's progress) or "fetch" (no connection / unexpected answer).</summary>
    public string? Problem { get; init; }

    public int UnlockedCount => Items.Count(a => a.Unlocked);
    public static AchievementSet Failed(string problem) => new() { Problem = problem };
}

/// <summary>Steam: the existing <see cref="Achievements"/> reader (public community page, and the Web API when a key was saved), shaped for the tab.</summary>
public static class SteamAchievements
{
    public static async Task<AchievementSet> FetchAsync(HttpClient http, Game game, string? steamId64, string? apiKey, CancellationToken ct = default)
    {
        AchievementResult result;
        try { result = await Achievements.FetchAsync(http, game, steamId64, apiKey, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return AchievementSet.Failed("fetch"); }
        return From(result);
    }

    public static AchievementSet From(AchievementResult result)
    {
        switch (result.Status)
        {
            case AchievementStatus.None: return AchievementSet.Failed("none");
            case AchievementStatus.NotSignedIn: return AchievementSet.Failed("noid");
            case AchievementStatus.NotSupported: return AchievementSet.Failed("source");
            case AchievementStatus.Unavailable: return AchievementSet.Failed("private");
        }
        var items = result.Items
            .Select(i => new Achievement(i.Name, i.Name, string.IsNullOrWhiteSpace(i.Description) ? null : i.Description, i.IconUrl, i.Unlocked, i.Unlocked ? i.UnlockedAt : null,
                Hidden: !i.Unlocked && string.IsNullOrWhiteSpace(i.Description)))
            .ToList();
        return items.Count == 0 ? AchievementSet.Failed("none") : new AchievementSet { Items = Sort(items) };
    }

    /// <summary>Unlocked first (the latest on top), then the locked ones in the game's order.</summary>
    internal static List<Achievement> Sort(List<Achievement> items) =>
        items.Select((a, i) => (a, i)).OrderBy(p => p.a.Unlocked ? 0 : 1).ThenByDescending(p => p.a.UnlockedAt ?? DateTimeOffset.MinValue).ThenBy(p => p.i).Select(p => p.a).ToList();
}

/// <summary>
/// Epic: achievement definitions and the player's progress from the Epic Store's GraphQL service, with the account that signed in
/// (the same session as the library). Epic does not document this service, so any failure is reported as a problem, never as an empty list.
/// </summary>
public sealed class EpicAchievementsClient
{
    private const string Endpoint = "https://store.epicgames.com/graphql";

    private const string DefinitionsQuery = "query Achievement($sandboxId: String!, $locale: String!) { Achievement { productAchievementsRecordBySandbox(sandboxId: $sandboxId, locale: $locale) { " +
        "sandboxId totalAchievements achievements { achievement { name hidden unlockedDisplayName lockedDisplayName unlockedDescription lockedDescription unlockedIconLink lockedIconLink XP rarity { percent } } } } } }";

    private const string ProgressQuery = "query PlayerAchievement($epicAccountId: String!, $sandboxId: String!) { PlayerAchievement { playerAchievementGameRecordsBySandbox(epicAccountId: $epicAccountId, sandboxId: $sandboxId) { " +
        "records { sandboxId totalUnlocked playerAchievements { playerAchievement { achievementName unlocked progress XP unlockDate } } } } } }";

    private readonly HttpClient _http;
    public EpicAchievementsClient(HttpClient http) => _http = http;

    public async Task<AchievementSet> FetchAsync(EpicTokens? tokens, string? sandboxId, string locale = "pt-BR", CancellationToken ct = default)
    {
        if (tokens is null) return AchievementSet.Failed("signin");
        if (string.IsNullOrEmpty(sandboxId)) return AchievementSet.Failed("none");
        try
        {
            var definitions = await PostAsync(tokens, "Achievement", DefinitionsQuery, new { sandboxId, locale }, ct);
            if (definitions is null) return AchievementSet.Failed("fetch");

            string? progress;
            try { progress = await PostAsync(tokens, "PlayerAchievement", ProgressQuery, new { epicAccountId = tokens.AccountId, sandboxId }, ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { progress = null; }
            return Merge(definitions, progress);
        }
        catch (EpicSignInException)
        {
            return AchievementSet.Failed("signin");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AchievementSet.Failed("fetch");
        }
    }

    private async Task<string?> PostAsync(EpicTokens tokens, string operation, string query, object variables, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { operationName = operation, query, variables });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue(string.IsNullOrEmpty(tokens.TokenType) ? "bearer" : tokens.TokenType, tokens.AccessToken);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized) throw new EpicSignInException("A sessão da Epic expirou. Entre de novo.");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Joins the definitions with the player's progress (<paramref name="progressJson"/> null: the progress is not known).</summary>
    public static AchievementSet Merge(string definitionsJson, string? progressJson)
    {
        var defs = Collect(definitionsJson, "achievement");
        if (defs is null || defs.Count == 0) return AchievementSet.Failed(defs is null ? "fetch" : "none");

        Dictionary<string, (bool Unlocked, DateTimeOffset? When)>? player = null;
        if (progressJson is not null && Collect(progressJson, "playerAchievement") is { } mine)
        {
            player = new(StringComparer.OrdinalIgnoreCase);
            foreach (var p in mine)
            {
                if (Str(p, "achievementName") is not { Length: > 0 } name) continue;
                var unlocked = p.TryGetProperty("unlocked", out var u) && u.ValueKind == JsonValueKind.True;
                DateTimeOffset? when = Str(p, "unlockDate") is { } text && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) && d.Year > 1971 ? d : null;
                player[name] = (unlocked, when);
            }
        }

        var items = new List<Achievement>();
        foreach (var d in defs)
        {
            if (Str(d, "name") is not { Length: > 0 } id) continue;
            var state = player is not null && player.TryGetValue(id, out var s) ? s : (Unlocked: false, When: (DateTimeOffset?)null);
            var hidden = d.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True;
            var title = state.Unlocked ? Str(d, "unlockedDisplayName") : Str(d, "lockedDisplayName");
            var text = state.Unlocked ? Str(d, "unlockedDescription") : Str(d, "lockedDescription");
            var icon = state.Unlocked ? Str(d, "unlockedIconLink") : Str(d, "lockedIconLink");
            double? rarity = d.TryGetProperty("rarity", out var r) && r.ValueKind == JsonValueKind.Object && r.TryGetProperty("percent", out var pc) && pc.TryGetDouble(out var pv) ? pv : null;
            items.Add(new Achievement(id, string.IsNullOrWhiteSpace(title) ? (Str(d, "unlockedDisplayName") ?? id) : title, string.IsNullOrWhiteSpace(text) ? null : text,
                CoverDownloader.IsEpicImageUrl(icon) ? icon : null, state.Unlocked, state.When, hidden && !state.Unlocked, rarity));
        }
        return new AchievementSet { Items = SteamAchievements.Sort(items), Problem = player is null ? "noprogress" : null };
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Every object found in arrays named <paramref name="arrayName"/>, wherever they sit in the answer; null for an answer with GraphQL errors only.</summary>
    private static List<JsonElement>? Collect(string json, string arrayName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var found = new List<JsonElement>();
            Walk(doc.RootElement, arrayName, found);
            if (found.Count == 0 && doc.RootElement.TryGetProperty("errors", out _)) return null;
            return found.Select(e => e.Clone()).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Walk(JsonElement node, string arrayName, List<JsonElement> found)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in node.EnumerateObject())
            {
                if (p.Name == arrayName && p.Value.ValueKind == JsonValueKind.Array)
                    found.AddRange(p.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object));
                else Walk(p.Value, arrayName, found);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in node.EnumerateArray()) Walk(e, arrayName, found);
        }
    }
}
