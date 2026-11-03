using SteamKit2;
using SteamKit2.Authentication;

namespace DeckLauncher.App;

/// <summary>
/// The launcher's own connection to Steam (SteamKit2), the way the Steam client talks: it signs in with the token saved after
/// the QR approval, sends chat messages and receives the ones friends send. It stays connected only while the Amigos page is open.
/// </summary>
internal sealed class SteamLink : IDisposable
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(25);

    private readonly SteamClient _client = new();
    private readonly CallbackManager _manager;
    private readonly SteamUser _user;
    private readonly SteamFriends _friends;

    private TaskCompletionSource? _connected;
    private TaskCompletionSource? _loggedOn;
    private volatile bool _stop;
    private volatile bool _isConnected;
    private volatile bool _isLoggedOn;
    private bool _pumping;

    public SteamLink()
    {
        _manager = new CallbackManager(_client);
        _user = _client.GetHandler<SteamUser>()!;
        _friends = _client.GetHandler<SteamFriends>()!;

        _manager.Subscribe<SteamClient.ConnectedCallback>(_ =>
        {
            _isConnected = true;
            _connected?.TrySetResult();
        });
        _manager.Subscribe<SteamClient.DisconnectedCallback>(_ =>
        {
            _isConnected = false;
            _isLoggedOn = false;
            var lost = new SteamChatException("A conexão com a Steam caiu.");
            _connected?.TrySetException(lost);
            _loggedOn?.TrySetException(lost);
            Disconnected?.Invoke();
        });
        _manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _manager.Subscribe<SteamFriends.FriendMsgCallback>(callback =>
        {
            if (callback.EntryType != EChatEntryType.ChatMsg || string.IsNullOrEmpty(callback.Message)) return;
            MessageReceived?.Invoke(callback.Sender.ConvertToUInt64(), callback.Message);
        });
    }

    /// <summary>A friend's message arrived: friend SteamID64 and the text. Raised on a background thread.</summary>
    public event Action<ulong, string>? MessageReceived;

    /// <summary>The connection dropped (not raised by <see cref="Dispose"/>). Raised on a background thread.</summary>
    public event Action? Disconnected;

    public bool IsReady => _isLoggedOn;

    // ---- Connection ----------------------------------------------------------------------------------

    private void StartPump()
    {
        if (_pumping) return;
        _pumping = true;
        _ = Task.Run(() =>
        {
            while (!_stop) _manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(250));
        });
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        if (_isConnected) return;
        _connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StartPump();
        _client.Connect();
        await WaitAsync(_connected.Task, "Não foi possível conectar à Steam. Verifique a internet.", ct);
    }

    /// <summary>Signs in with the token saved after the QR approval.</summary>
    public async Task LogOnAsync(string account, string token, CancellationToken ct)
    {
        if (_isLoggedOn) return;
        await ConnectAsync(ct);
        _loggedOn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _user.LogOn(new SteamUser.LogOnDetails
        {
            Username = account,
            AccessToken = token,
            ShouldRememberPassword = true,
            LoginID = 0x44454B31, // lets this session live next to the Steam app on the same account
        });
        await WaitAsync(_loggedOn.Task, "A Steam não respondeu ao pedido de entrada.", ct);
        _friends.SetPersonaState(EPersonaState.Online); // friends' messages are only delivered to a session that is online
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result == EResult.OK)
        {
            _isLoggedOn = true;
            _loggedOn?.TrySetResult();
            return;
        }

        Exception error = callback.Result is EResult.AccessDenied or EResult.InvalidPassword or EResult.Expired or EResult.InvalidLoginAuthCode
            ? new UnauthorizedAccessException()
            : new SteamChatException($"A Steam recusou a entrada ({callback.Result}).");
        _loggedOn?.TrySetException(error);
    }

    private static async Task WaitAsync(Task task, string timeoutMessage, CancellationToken ct)
    {
        var finished = await Task.WhenAny(task, Task.Delay(StepTimeout, ct));
        ct.ThrowIfCancellationRequested();
        if (finished != task) throw new SteamChatException(timeoutMessage);
        await task; // brings the error along when the step failed
    }

    // ---- QR sign-in ----------------------------------------------------------------------------------

    /// <summary>
    /// Shows a QR code (through <paramref name="showUrl"/>, called again whenever Steam renews it) and waits until the user approves
    /// it in the Steam app. Returns the account name and the token to save.
    /// </summary>
    public async Task<(string Account, string Token)> SignInWithQrAsync(Action<string> showUrl, CancellationToken ct)
    {
        await ConnectAsync(ct);
        try
        {
            var session = await _client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails { DeviceFriendlyName = "DeckLauncher" });
            showUrl(session.ChallengeURL);
            session.ChallengeURLChanged = () => showUrl(session.ChallengeURL);
            var result = await session.PollingWaitForResultAsync(ct);
            return (result.AccountName, result.RefreshToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not SteamChatException)
        {
            throw new SteamChatException("A Steam não concluiu a entrada: " + ex.Message);
        }
    }

    // ---- Chat ----------------------------------------------------------------------------------------

    public void SendMessage(ulong friendSteamId, string text)
    {
        if (!_isLoggedOn) throw new SteamChatException("O chat ainda não está conectado.");
        _friends.SendChatMessage(new SteamID(friendSteamId), EChatEntryType.ChatMsg, text);
    }

    public void Dispose()
    {
        _stop = true;
        try { if (_isLoggedOn) _user.LogOff(); }
        catch (Exception) { /* closing anyway */ }
        _client.Disconnect();
    }
}
