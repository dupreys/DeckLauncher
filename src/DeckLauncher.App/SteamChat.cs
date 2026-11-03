using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DeckLauncher.App;

/// <summary>A chat step that failed; the message is already in Portuguese and safe to show.</summary>
internal sealed class SteamChatException : Exception
{
    public SteamChatException(string message) : base(message) { }
}

/// <summary>
/// Helpers for the Steam chat: how the saved sign-in is stored, and the conversation history read from the Steam web service.
/// Sending and live messages go through <see cref="SteamLink"/> (SteamKit2). The saved sign-in is the account name plus the
/// token Steam gave after the user approved a QR code in the Steam app; the password is never typed here or stored.
/// </summary>
internal static class SteamChat
{
    private const string Api = "https://api.steampowered.com";
    private const ulong SteamIdBase = 76561197960265728UL;

    public sealed record Message(bool Mine, string Text, long Time, uint Ordinal);

    public static ulong AccountId(string steamId64) => ulong.TryParse(steamId64, out var id) && id > SteamIdBase ? id - SteamIdBase : 0;

    /// <summary>What is kept in settings: "account:token". Account names never contain a colon.</summary>
    public static string Compose(string account, string token) => account + ":" + token;

    public static (string Account, string Token)? ParseSaved(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved)) return null;
        var colon = saved.IndexOf(':');
        if (colon <= 0 || colon == saved.Length - 1) return null; // older format without the account name: sign in again
        var token = saved[(colon + 1)..];
        return SteamIdOf(token) is null ? null : (saved[..colon], token);
    }

    // ---- Tokens -----------------------------------------------------------------------------------

    /// <summary>The SteamID64 inside a Steam token (its "sub" field), or null when the text is not a token.</summary>
    public static string? SteamIdOf(string token) => Claim(token, "sub");

    private static string? Claim(string token, string name)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!doc.RootElement.TryGetProperty(name, out var value)) return null;
            return value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.GetString();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads history: trades the saved token for short-lived access tokens as needed.</summary>
    public sealed class Client
    {
        private readonly string _refreshToken;
        private string? _access;
        private DateTime _accessUntil;

        public Client(string refreshToken, string steamId)
        {
            _refreshToken = refreshToken;
            SteamId = steamId;
            MyAccountId = AccountId(steamId);
        }

        public string SteamId { get; }
        public ulong MyAccountId { get; }

        private async Task<string> AccessAsync(CancellationToken ct, bool renew = false)
        {
            if (!renew && _access is not null && DateTime.UtcNow < _accessUntil) return _access;

            var response = await CallAsync(HttpMethod.Post, "IAuthenticationService/GenerateAccessTokenForApp/v1/",
                new { refresh_token = _refreshToken, steamid = SteamId }, null, ct);
            var access = Str(response, "access_token");
            if (string.IsNullOrEmpty(access)) throw new UnauthorizedAccessException();
            _access = access;
            var expires = Claim(access, "exp");
            _accessUntil = long.TryParse(expires, out var exp)
                ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime.AddMinutes(-2)
                : DateTime.UtcNow.AddMinutes(10);
            return access;
        }

        /// <summary>Runs a call with the access token, getting a fresh one once if Steam says it expired.</summary>
        private async Task<JsonElement> WithAccessAsync(Func<string, Task<JsonElement>> call, CancellationToken ct)
        {
            try { return await call(await AccessAsync(ct)); }
            catch (UnauthorizedAccessException)
            {
                return await call(await AccessAsync(ct, renew: true));
            }
        }

        /// <summary>The latest messages of the conversation, oldest first.</summary>
        public async Task<List<Message>> GetMessagesAsync(string friendSteamId, CancellationToken ct)
        {
            var response = await WithAccessAsync(access => CallAsync(HttpMethod.Get,
                $"IFriendMessagesService/GetRecentMessages/v1/?steamid1={SteamId}&steamid2={friendSteamId}&count=60&most_recent_conversation=false&bbcode_format=false",
                null, access, ct), ct);

            var list = new List<Message>();
            if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) return list;
            foreach (var m in messages.EnumerateArray())
            {
                var text = m.TryGetProperty("message", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (string.IsNullOrEmpty(text)) continue;
                var account = m.TryGetProperty("accountid", out var a) && a.TryGetUInt64(out var accountId) ? accountId : 0;
                var time = m.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var seconds) ? seconds : 0;
                var ordinal = m.TryGetProperty("ordinal", out var o) && o.TryGetUInt32(out var ord) ? ord : 0u;
                list.Add(new Message(account == MyAccountId, text, time, ordinal));
            }
            return list.OrderBy(m => m.Time).ThenBy(m => m.Ordinal).ToList();
        }
    }

    // ---- HTTP -------------------------------------------------------------------------------------

    private static string? Str(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    /// <summary>
    /// A Steam web service call. The access token travels in the address, as the Steam Web API requires; it is never logged
    /// (errors below only carry the status code).
    /// </summary>
    private static async Task<JsonElement> CallAsync(HttpMethod method, string path, object? input, string? accessToken, CancellationToken ct)
    {
        var url = $"{Api}/{path}";
        url += (url.Contains('?') ? "&" : "?") + "format=json";
        if (accessToken is not null) url += "&access_token=" + Uri.EscapeDataString(accessToken);

        using var request = new HttpRequestMessage(method, url);
        if (input is not null)
            request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("input_json", JsonSerializer.Serialize(input)) });

        HttpResponseMessage response;
        try { response = await LibraryService.Http.SendAsync(request, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new SteamChatException("Sem conexão com a Steam. Verifique a internet.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new UnauthorizedAccessException();

            var eresult = response.Headers.TryGetValues("X-eresult", out var values) ? values.FirstOrDefault() : null;
            if (!response.IsSuccessStatusCode)
                throw new SteamChatException($"A Steam recusou o pedido (erro {(int)response.StatusCode}{(eresult is null ? "" : $", código {eresult}")}).");
            if (eresult is not null && eresult != "1")
            {
                // 5/8/15/19/20 mean the token is invalid, the account is blocked or messages are not allowed.
                if (eresult is "5" or "8" or "9" or "29") throw new UnauthorizedAccessException();
                throw new SteamChatException(eresult == "15" ? "A Steam não permitiu a mensagem para este amigo." : $"A Steam recusou o pedido (código {eresult}).");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return default;
            try
            {
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("response", out var r) ? r.Clone() : default;
            }
            catch (JsonException)
            {
                throw new SteamChatException("Resposta inesperada da Steam.");
            }
        }
    }
}
