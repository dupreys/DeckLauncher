using System.Xml;
using System.Xml.Linq;

namespace DeckLauncher.Core.Steam;

/// <summary>Public Steam profile info. Never contains credentials.</summary>
public sealed record SteamAccount(string SteamId64, string PersonaName, string? AvatarUrl = null);

/// <summary>An account that has signed in to the Steam client on this PC.</summary>
public sealed record SteamLocalAccount(string SteamId64, string AccountName, string PersonaName, bool MostRecent, long Timestamp, bool RememberPassword = false);

/// <summary>Reads the accounts that have signed in to the local Steam client.</summary>
public static class SteamLoginUsers
{
    /// <summary>Every account in loginusers.vdf, the most recently used first.</summary>
    public static IReadOnlyList<SteamLocalAccount> ReadAll(string steamPath)
    {
        var file = Path.Combine(steamPath, "config", "loginusers.vdf");
        try
        {
            return File.Exists(file) ? ParseAll(File.ReadAllText(file)) : Array.Empty<SteamLocalAccount>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<SteamLocalAccount>();
        }
    }

    public static IReadOnlyList<SteamLocalAccount> ParseAll(string vdf)
    {
        var users = VdfParser.Parse(vdf)["users"];
        if (users is null) return Array.Empty<SteamLocalAccount>();

        return users.Children
            .Where(c => IsSteamId64(c.Key))
            .Select(c => new SteamLocalAccount(
                c.Key,
                c.Value.GetString("AccountName") ?? c.Key,
                c.Value.GetString("PersonaName") ?? c.Value.GetString("AccountName") ?? c.Key,
                c.Value.GetString("MostRecent") == "1",
                c.Value.GetLong("Timestamp"),
                c.Value.GetString("RememberPassword") == "1"))
            .OrderByDescending(a => a.MostRecent)
            .ThenByDescending(a => a.Timestamp)
            .ToList();
    }

    public static bool IsSteamId64(string? value) =>
        value is { Length: 17 } && value.All(char.IsAsciiDigit);

    /// <summary>The account most recently used in the Steam client on this PC, if any.</summary>
    public static SteamAccount? ReadMostRecent(string steamPath)
    {
        var file = Path.Combine(steamPath, "config", "loginusers.vdf");
        try
        {
            return File.Exists(file) ? Parse(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SteamAccount? Parse(string vdf)
    {
        var users = VdfParser.Parse(vdf)["users"];
        if (users is null) return null;

        SteamAccount? best = null;
        var bestMostRecent = false;
        long bestTimestamp = -1;

        foreach (var (id, node) in users.Children)
        {
            if (!IsSteamId64(id)) continue;

            var name = node.GetString("PersonaName") ?? node.GetString("AccountName") ?? id;
            var mostRecent = node.GetString("MostRecent") == "1";
            var timestamp = node.GetLong("Timestamp");

            var better = best is null
                || (mostRecent && !bestMostRecent)
                || (mostRecent == bestMostRecent && timestamp > bestTimestamp);

            if (!better) continue;
            best = new SteamAccount(id, name);
            bestMostRecent = mostRecent;
            bestTimestamp = timestamp;
        }

        return best;
    }
}

/// <summary>Fetches name and avatar from the public Steam Community profile (no API key).</summary>
public static class SteamProfile
{
    public static async Task<SteamAccount?> FetchAsync(HttpClient http, string steamId64, CancellationToken cancellationToken = default)
    {
        if (!SteamLoginUsers.IsSteamId64(steamId64)) return null;

        try
        {
            var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId64}/?xml=1", cancellationToken);
            return ParseProfileXml(xml, steamId64);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException)
        {
            return null; // offline, private profile, or Steam hiccup: caller falls back to local info
        }
    }

    public static SteamAccount? ParseProfileXml(string xml, string steamId64)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);

        var root = XDocument.Load(reader).Root;
        if (root is null || root.Name != "profile") return null; // private/unknown profiles return <response><error>

        var name = root.Element("steamID")?.Value;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var avatar = root.Element("avatarFull")?.Value;
        if (avatar is not null && !avatar.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) avatar = null;

        return new SteamAccount(steamId64, name.Trim(), avatar);
    }
}

/// <summary>
/// Which installed Steam games belong to an account. Steam installs games for the whole PC, so the account is told apart by the
/// manifest's LastOwner and by the games listed in the account's own userdata\&lt;id&gt;\config\localconfig.vdf.
/// </summary>
public static class SteamAccountFilter
{
    private const long SteamId64Base = 76561197960265728;

    /// <summary>The 32-bit account id Steam uses for the userdata folder names.</summary>
    public static string AccountId32(string steamId64) =>
        long.TryParse(steamId64, out var id) && id > SteamId64Base ? (id - SteamId64Base).ToString() : "";

    /// <summary>App ids the account has in its local config (games it has played or installed). Empty when the file is missing.</summary>
    public static HashSet<string> OwnedAppIds(string steamPath, string steamId64)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var account = AccountId32(steamId64);
        if (account.Length == 0) return result;
        var file = Path.Combine(steamPath, "userdata", account, "config", "localconfig.vdf");
        try
        {
            if (!File.Exists(file)) return result;
            return ParseAppIds(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return result;
        }
    }

    public static HashSet<string> ParseAppIds(string vdf)
    {
        var apps = VdfParser.Parse(vdf)["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"]?["apps"];
        return apps is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(apps.Children.Keys, StringComparer.Ordinal);
    }

    /// <summary>Non-Steam games are always kept; a Steam game is kept when the account owns the install or has it in its config.</summary>
    public static bool Keep(Game game, string steamId64, HashSet<string> accountApps)
    {
        if (game.Source != SteamProvider.ProviderName) return true;
        if (game.Owner == steamId64) return true;
        if (accountApps.Contains(game.Id)) return true;
        // Nothing tells whose it is (no owner and no config to compare): do not hide it.
        return (string.IsNullOrEmpty(game.Owner) || game.Owner == "0") && accountApps.Count == 0;
    }
}

/// <summary>
/// Local switching of the account the Steam client signs in with: the same thing the client's own "switch account" does with
/// the saved login. No password and no Steam sign-in request is ever made by the launcher.
/// </summary>
public static class SteamAccountSwitch
{
    /// <summary>loginusers.vdf text with only <paramref name="steamId64"/> marked as the most recent account.</summary>
    public static string MarkMostRecent(string vdf, string steamId64)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            vdf,
            "(\"(?<id>\\d{17})\"\\s*\\{)(?<body>[^{}]*)(\\})",
            m =>
            {
                var wanted = m.Groups["id"].Value == steamId64 ? "1" : "0";
                var body = m.Groups["body"].Value;
                body = System.Text.RegularExpressions.Regex.IsMatch(body, "\"MostRecent\"\\s+\"[01]\"")
                    ? System.Text.RegularExpressions.Regex.Replace(body, "(\"MostRecent\"\\s+)\"[01]\"", "$1\"" + wanted + "\"")
                    : body + "\n\t\t\"MostRecent\"\t\t\"" + wanted + "\"";
                return m.Groups[1].Value + body + m.Groups[4].Value;
            },
            System.Text.RegularExpressions.RegexOptions.Singleline);
    }
}
