using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiteLauncher.Core.Settings;
using QRCoder;

namespace DeckLauncher.App;

// Amigos: the Steam friends list, the conversation, the quick message wheel (hold Select) and the Steam chat sign-in.
public partial class MainWindow
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] WheelPositionNames =
    {
        "Topo", "Topo à direita", "Direita", "Baixo à direita", "Baixo", "Baixo à esquerda", "Esquerda", "Topo à esquerda",
    };

    private readonly DispatcherTimer _friendsTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _wheelTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly List<Border> _wheelItems = new();
    private readonly List<TextBlock> _wheelTexts = new();

    private List<SteamFeeds.FriendData> _friends = new();
    private string _friendsSignature = "";
    private bool _friendsLoading;

    private SteamFeeds.FriendData? _chatFriend;
    private SteamChat.Client? _history;
    private SteamLink? _link;
    private Task? _linkTask;
    private List<SteamChat.Message> _chatMessages = new();
    private string _chatSignature = "";
    private CancellationTokenSource? _chatCts;
    private CancellationTokenSource? _qrCts;
    private int _wheelIndex = -1;
    private bool _selectIgnored;

    private bool ChatOpen => _page == "Amigos" && _chatFriend is not null && ChatView.Visibility == Visibility.Visible;
    private bool WheelOpen => WheelLayer.Visibility == Visibility.Visible;

    private void InitFriends()
    {
        _gamepad.SelectHoldStarted += OnSelectHoldStarted;
        _gamepad.SelectHoldEnded += CloseWheel;
        _gamepad.SelectTapped += OnSelectTapped;
        _friendsTimer.Tick += async (_, _) => await LoadFriendsAsync(initial: false);
        _wheelTimer.Tick += (_, _) => UpdateWheelFromStick();
        BuildWheel();
    }

    // ======================================================================================
    // The page
    // ======================================================================================

    private void StartFriends()
    {
        _friendsTimer.Start();
        _ = LoadFriendsAsync(initial: true);
    }

    private void StopFriends()
    {
        _friendsTimer.Stop();
        if (_chatFriend is not null || _qrCts is not null) CloseConversation();
        CloseWheel();
        DropLink();
    }

    private void DropLink()
    {
        var link = _link;
        _link = null;
        _linkTask = null;
        if (link is not null) _ = Task.Run(link.Dispose);
    }

    private void FocusFriends()
    {
        var first = FindAll<Button>(FriendsList).FirstOrDefault();
        first?.Focus();
    }

    private async Task LoadFriendsAsync(bool initial)
    {
        if (_friendsLoading) return;
        _friendsLoading = true;
        try
        {
            var key = App.Settings.SteamWebApiKey;
            var id = SteamIdValue();
            if (string.IsNullOrWhiteSpace(key)) { ShowFriendsNotice(ProblemText("nokey")); return; }
            if (id is null) { ShowFriendsNotice(ProblemText("noid")); return; }

            try
            {
                var friends = await SteamFeeds.FetchFriendsAsync(LibraryService.Http, key, id);
                _friends = friends;
                FriendsNotice.Visibility = Visibility.Collapsed;
                FriendsScroll.Visibility = Visibility.Visible;
                RebuildFriendList(initial);
                if (_chatFriend is not null && friends.FirstOrDefault(f => f.SteamId == _chatFriend.SteamId) is { } fresh)
                {
                    _chatFriend = fresh;
                    ChatStatus.Text = StatusText(fresh);
                }
            }
            catch (UnauthorizedAccessException)
            {
                if (initial || _friends.Count == 0) ShowFriendsNotice(ProblemText("private"));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (initial && _friends.Count == 0) ShowFriendsNotice(ProblemText("offline"));
            }
        }
        finally
        {
            _friendsLoading = false;
        }
    }

    private void ShowFriendsNotice(string text)
    {
        FriendsNotice.Text = text;
        FriendsNotice.Visibility = Visibility.Visible;
        FriendsScroll.Visibility = Visibility.Collapsed;
    }

    private static bool IsOnline(SteamFeeds.FriendData f) => f.PersonaState > 0 || f.GameId is not null;

    private static string LastSeenText(long lastLogoff)
    {
        if (lastLogoff <= 0) return "Off-line";
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(lastLogoff);
        if (span.TotalMinutes < 2) return "Última vez on-line agora há pouco";
        if (span.TotalHours < 1) return $"Última vez on-line há {(int)span.TotalMinutes} min";
        if (span.TotalDays < 1) return $"Última vez on-line há {(int)span.TotalHours} h";
        if (span.TotalDays < 2) return "Última vez on-line há 1 dia";
        if (span.TotalDays < 14) return $"Última vez on-line há {(int)span.TotalDays} dias";
        if (span.TotalDays < 60) return $"Última vez on-line há {(int)(span.TotalDays / 7)} semanas";
        return $"Última vez on-line há {(int)(span.TotalDays / 30)} meses";
    }

    private static string StatusText(SteamFeeds.FriendData f)
    {
        if (!string.IsNullOrWhiteSpace(f.GameName)) return f.GameName!;
        if (!IsOnline(f)) return LastSeenText(f.LastLogoff);
        return f.PersonaState switch
        {
            2 => "Ocupado",
            3 => "Ausente",
            4 => "Soneca",
            5 => "Procurando troca",
            6 => "Procurando jogar",
            _ => "Disponível",
        };
    }

    private static Brush StatusBrush(SteamFeeds.FriendData f) =>
        !string.IsNullOrWhiteSpace(f.GameName) ? new SolidColorBrush(Color.FromRgb(0x90, 0xBA, 0x3C))
        : IsOnline(f) ? new SolidColorBrush(Color.FromRgb(0x6D, 0xCF, 0xF6))
        : new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0x9D));

    private void RebuildFriendList(bool force)
    {
        var comparer = StringComparer.Create(PtBr, ignoreCase: true);
        var online = _friends.Where(IsOnline)
            .OrderByDescending(f => f.GameId is not null).ThenBy(f => f.Name, comparer).ToList();
        var offline = _friends.Where(f => !IsOnline(f)).OrderBy(f => f.Name, comparer).ToList();

        var signature = string.Join(';', online.Concat(offline).Select(f => $"{f.SteamId}|{f.PersonaState}|{f.GameName}|{StatusText(f)}"));
        if (!force && signature == _friendsSignature) return;
        _friendsSignature = signature;

        var focusedId = Keyboard.FocusedElement is Button { Tag: SteamFeeds.FriendData focused } && IsAncestor(FriendsList, (Button)Keyboard.FocusedElement)
            ? focused.SteamId : null;
        var offset = FriendsScroll.VerticalOffset;

        FriendsList.Children.Clear();
        FriendsList.Children.Add(SectionHeader($"Amigos on-line  ({online.Count})"));
        if (online.Count == 0)
            FriendsList.Children.Add(new TextBlock { Text = "Ninguém on-line agora.", FontSize = 14, Foreground = (Brush)FindResource("TextMuted"), Margin = new Thickness(26, 6, 0, 10) });
        foreach (var friend in online) FriendsList.Children.Add(FriendRow(friend, loadAvatar: true));

        FriendsList.Children.Add(SectionHeader($"Off-line  ({offline.Count})"));
        var withAvatar = 0;
        foreach (var friend in offline) FriendsList.Children.Add(FriendRow(friend, loadAvatar: withAvatar++ < 40));

        Dispatcher.BeginInvoke(() =>
        {
            if (focusedId is not null)
            {
                var row = FindAll<Button>(FriendsList).FirstOrDefault(b => b.Tag is SteamFeeds.FriendData f && f.SteamId == focusedId);
                if (row is not null) { row.Focus(); return; }
            }
            FriendsScroll.ScrollToVerticalOffset(offset);
            // First load while the page is in front: put the focus on the first friend so the controller works right away.
            if (_page == "Amigos" && !MenuOpen && !(Keyboard.FocusedElement is DependencyObject now && IsAncestor(FriendsPage, now)))
                FindAll<Button>(FriendsList).FirstOrDefault()?.Focus();
        }, DispatcherPriority.Loaded);
    }

    private static UIElement SectionHeader(string text) => new TextBlock
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD2)),
        Margin = new Thickness(22, 16, 0, 8),
    };

    private Button FriendRow(SteamFeeds.FriendData friend, bool loadAvatar)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var box = new Border { Width = 46, Height = 46, Child = new Grid { Children = { new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 20, Foreground = (Brush)FindResource("TextMuted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, image } } };
        box.SetResourceReference(Border.BackgroundProperty, "PanelRaised");
        if (loadAvatar) _ = LoadAvatarAsync(image, friend.Avatar);

        var online = IsOnline(friend);
        var texts = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = friend.Name,
            FontSize = 18,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = online ? StatusBrush(friend) : new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD2)),
        });
        texts.Children.Add(new TextBlock { Text = StatusText(friend), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = StatusBrush(friend) });

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(box);
        content.Children.Add(texts);

        var row = new Button { Style = (Style)FindResource("FriendRow"), Content = content, Tag = friend, Height = 62 };
        AutomationProperties.SetName(row, friend.Name);
        row.Click += OnFriendClick;
        return row;
    }

    private static async Task LoadAvatarAsync(Image target, string? url)
    {
        var source = await RemoteImage.LoadAsync(LibraryService.Http, url, 92);
        if (source is not null) target.Source = source;
    }

    private void OnFriendClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SteamFeeds.FriendData friend }) OpenConversation(friend);
    }

    // ======================================================================================
    // Conversation
    // ======================================================================================

    /// <summary>True when a usable sign-in is saved (the connection itself is made when needed).</summary>
    private bool EnsureChatClient()
    {
        if (!string.IsNullOrWhiteSpace(App.Settings.SteamChatToken) && SteamChat.ParseSaved(App.Settings.SteamChatToken) is null)
        {
            App.Settings.SteamChatToken = null; // an older or damaged sign-in: ask again
            SaveSettings();
        }
        if (SteamChat.ParseSaved(App.Settings.SteamChatToken) is not { } saved) return false;
        if (_history is null && SteamChat.SteamIdOf(saved.Token) is { } id) _history = new SteamChat.Client(saved.Token, id);
        return true;
    }

    /// <summary>Connects (once) to Steam with the saved sign-in. Everything that sends or receives waits on this.</summary>
    private Task EnsureLinkAsync()
    {
        if (_link is { IsReady: true }) return Task.CompletedTask;
        if (_linkTask is { IsCompleted: false }) return _linkTask;
        if (SteamChat.ParseSaved(App.Settings.SteamChatToken) is not { } saved) return Task.FromException(new UnauthorizedAccessException());

        DropLink();
        var link = _link = new SteamLink();
        link.MessageReceived += (friendId, text) => Dispatcher.BeginInvoke(() => OnIncomingMessage(friendId, text));
        link.Disconnected += () => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(_link, link)) { _link = null; _linkTask = null; }
        });
        var cts = _chatCts ?? new CancellationTokenSource();
        return _linkTask = link.LogOnAsync(saved.Account, saved.Token, cts.Token);
    }

    /// <summary>Connects in the background when a conversation opens; says so in the hint line.</summary>
    private async Task ConnectChatAsync()
    {
        if (_link is { IsReady: true }) return;
        ChatHint.Text = "Conectando ao chat da Steam...";
        try
        {
            await EnsureLinkAsync();
            ChatHint.Text = ChatHintText;
        }
        catch (OperationCanceledException) { }
        catch (UnauthorizedAccessException)
        {
            ChatSignOut();
            ShowToast("A sessão do chat expirou. Entre de novo.");
            UpdateChatMode();
        }
        catch (SteamChatException ex)
        {
            ChatHint.Text = ex.Message + " Tentarei de novo ao enviar.";
        }
    }

    private const string ChatHintText = "Segure SELECT para mensagens rápidas   ·   B volta à lista de amigos";

    private void OnIncomingMessage(ulong friendId, string text)
    {
        var id = friendId.ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (ChatOpen && _chatFriend is not null && _chatFriend.SteamId == id)
        {
            _chatMessages = _chatMessages.Append(new SteamChat.Message(false, text, now, uint.MaxValue - 1)).ToList();
            RenderMessages(_chatMessages);
            return;
        }
        var name = _friends.FirstOrDefault(f => f.SteamId == id)?.Name ?? "Um amigo";
        ShowToast($"{name}: {text}");
    }

    private void OpenConversation(SteamFeeds.FriendData friend)
    {
        _chatCts?.Cancel();
        _chatCts = new CancellationTokenSource();
        _chatFriend = friend;
        _chatMessages = new List<SteamChat.Message>();
        _chatSignature = "";

        ChatEmpty.Visibility = Visibility.Collapsed;
        ChatView.Visibility = Visibility.Visible;
        ChatName.Text = friend.Name;
        ChatStatus.Text = StatusText(friend);
        ChatStatus.Foreground = StatusBrush(friend);
        ChatAvatar.Source = null;
        _ = LoadAvatarAsync(ChatAvatar, friend.Avatar);
        ChatMessages.Children.Clear();
        ChatInput.Clear();

        UpdateChatMode();
        if (EnsureChatClient())
        {
            _ = ConnectChatAsync();
            _ = LoadHistoryAsync();
        }
        Dispatcher.BeginInvoke(FocusChat, DispatcherPriority.Input);
    }

    private void CloseConversation()
    {
        _chatCts?.Cancel();
        _qrCts?.Cancel();
        _qrCts = null;
        _chatFriend = null;
        ChatView.Visibility = Visibility.Collapsed;
        ChatEmpty.Visibility = Visibility.Visible;
        ChatMessages.Children.Clear();
        VirtualKeyboard.Hide();
    }

    /// <summary>Messages and the input when signed in; the QR sign-in otherwise.</summary>
    private void UpdateChatMode()
    {
        var ready = EnsureChatClient();
        ChatLogin.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        ChatScroll.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        ChatInputBar.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        ChatHint.Text = ChatHintText;
    }

    private void FocusChat()
    {
        if (ChatView.Visibility != Visibility.Visible) return;
        if (ChatInputBar.IsVisible) ChatInput.Focus();
        else ChatLoginButton.Focus();
    }

    private void FocusFriendList()
    {
        VirtualKeyboard.Hide();
        var row = _chatFriend is null ? null : FindAll<Button>(FriendsList).FirstOrDefault(b => b.Tag is SteamFeeds.FriendData f && f.SteamId == _chatFriend.SteamId);
        (row ?? FindAll<Button>(FriendsList).FirstOrDefault())?.Focus();
    }

    private bool FocusInChat => Keyboard.FocusedElement is DependencyObject focus && IsAncestor(ChatView, focus);

    /// <summary>Left / Right between the friends list and the conversation. True when handled.</summary>
    private bool TryFriendsMove(FocusNavigationDirection direction)
    {
        if (_page != "Amigos" || Keyboard.FocusedElement is not DependencyObject focus) return false;

        if (IsAncestor(FriendsList, focus) && direction == FocusNavigationDirection.Right)
        {
            if (ChatOpen) FocusChat();
            return true;
        }

        if (IsAncestor(ChatView, focus) && direction == FocusNavigationDirection.Left)
        {
            if (focus is UIElement element && element.PredictFocus(FocusNavigationDirection.Left) is DependencyObject neighbour && IsAncestor(ChatView, neighbour))
                return false; // another control to the left inside the conversation
            FocusFriendList();
            return true;
        }
        return false;
    }

    /// <summary>Earlier messages of the conversation, when the Steam web service gives them (best effort: new ones arrive live).</summary>
    private async Task LoadHistoryAsync()
    {
        if (_history is null || _chatFriend is null || _chatCts is null) return;
        var friend = _chatFriend;
        var token = _chatCts.Token;
        try
        {
            var list = await _history.GetMessagesAsync(friend.SteamId, token);
            if (!ReferenceEquals(friend, _chatFriend) || token.IsCancellationRequested) return;
            var lastOld = list.Count > 0 ? list[^1].Time : 0;
            // Keep what arrived or was sent since the history ends.
            var newer = _chatMessages.Where(m => m.Time > lastOld || (m.Ordinal >= uint.MaxValue - 1 && m.Time >= lastOld)).ToList();
            _chatMessages = list.Concat(newer).ToList();
            RenderMessages(_chatMessages);
        }
        catch (Exception ex) when (ex is OperationCanceledException or UnauthorizedAccessException or SteamChatException)
        {
            // No history is not an error: the conversation simply starts empty.
        }
    }

    private void RenderMessages(List<SteamChat.Message> messages)
    {
        var signature = messages.Count + ":" + (messages.Count > 0 ? $"{messages[^1].Time}:{messages[^1].Ordinal}:{messages[^1].Mine}" : "");
        if (signature == _chatSignature) return;
        var first = _chatSignature.Length == 0;
        _chatSignature = signature;
        var atEnd = first || ChatScroll.VerticalOffset >= ChatScroll.ScrollableHeight - 40;

        ChatMessages.Children.Clear();
        DateTime? lastDay = null;
        foreach (var m in messages)
        {
            var time = DateTimeOffset.FromUnixTimeSeconds(m.Time).ToLocalTime().DateTime;
            if (lastDay is null || lastDay.Value.Date != time.Date)
            {
                lastDay = time;
                ChatMessages.Children.Add(new TextBlock
                {
                    Text = time.ToString("dddd, d 'de' MMMM 'de' yyyy", PtBr).ToUpper(PtBr),
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextMuted"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 12, 0, 10),
                });
            }

            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = m.Text, FontSize = 17, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White });
            body.Children.Add(new TextBlock { Text = time.ToString("HH:mm", PtBr), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0xB1, 0xBC)), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) });
            var bubble = new Border
            {
                Child = body,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 7),
                MaxWidth = 560,
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = m.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            if (m.Mine) bubble.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x5C, 0x9E));
            else bubble.SetResourceReference(Border.BackgroundProperty, "T_232A34");
            ChatMessages.Children.Add(bubble);
        }
        if (atEnd) Dispatcher.BeginInvoke(() => ChatScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    private void OnChatInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        SendFromInput();
    }

    private void OnChatSendClick(object sender, RoutedEventArgs e) => SendFromInput();

    private void SendFromInput()
    {
        var text = ChatInput.Text.Trim();
        if (text.Length == 0) return;
        ChatInput.Clear();
        VirtualKeyboard.Hide();
        _ = SendChatTextAsync(text);
    }

    private async Task SendChatTextAsync(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || _chatFriend is null) return;
        if (!EnsureChatClient())
        {
            ShowToast("Entre no chat da Steam primeiro.");
            return;
        }

        var friend = _chatFriend;
        var pending = new SteamChat.Message(true, text, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), uint.MaxValue);
        _chatMessages = _chatMessages.Append(pending).ToList();
        RenderMessages(_chatMessages);
        try
        {
            await EnsureLinkAsync();
            _link!.SendMessage(ulong.Parse(friend.SteamId), text);
        }
        catch (OperationCanceledException) { }
        catch (UnauthorizedAccessException)
        {
            ChatSignOut();
            ShowToast("A sessão do chat expirou. Entre de novo.");
            UpdateChatMode();
        }
        catch (SteamChatException ex)
        {
            _chatMessages = _chatMessages.Where(m => !ReferenceEquals(m, pending)).ToList();
            RenderMessages(_chatMessages);
            ShowToast("Mensagem não enviada: " + ex.Message);
        }
    }

    /// <summary>The Steam app's own chat window for this friend (works without signing in here).</summary>
    private void OnChatOpenSteamClick(object sender, RoutedEventArgs e)
    {
        if (_chatFriend is null) return;
        try
        {
            Process.Start(new ProcessStartInfo($"steam://friends/message/{_chatFriend.SteamId}") { UseShellExecute = true })?.Dispose();
            _ = BringStoreToFrontAsync(new[] { "steam", "steamwebhelper" }, null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowToast("Não foi possível abrir a Steam. Ela está instalada?");
        }
    }

    // ======================================================================================
    // Steam chat sign-in (QR code approved in the Steam app)
    // ======================================================================================

    private async void OnChatLoginClick(object sender, RoutedEventArgs e) => await StartChatLoginAsync();

    private async Task StartChatLoginAsync()
    {
        _qrCts?.Cancel();
        var cts = _qrCts = new CancellationTokenSource();
        ChatLoginButton.Content = "NOVO CÓDIGO QR";
        ChatLoginStatus.Text = "Gerando o código...";
        ChatQrHost.Visibility = Visibility.Collapsed;
        DropLink();
        var link = _link = new SteamLink();
        try
        {
            var signIn = await link.SignInWithQrAsync(url => Dispatcher.BeginInvoke(() =>
            {
                ShowQr(url);
                ChatLoginStatus.Text = "Abra o app da Steam no celular, vá em Steam Guard > Entrar com código QR e aponte para esta tela. Aguardando a aprovação...";
            }), cts.Token);
            if (cts.IsCancellationRequested) return;
            FinishChatLogin(signIn.Account, signIn.Token);
        }
        catch (OperationCanceledException) { }
        catch (SteamChatException ex)
        {
            if (cts.IsCancellationRequested) return;
            ChatQrHost.Visibility = Visibility.Collapsed;
            ChatLoginStatus.Text = ex.Message;
        }
        finally
        {
            // The sign-in connection is only for the QR step; the chat connects again with the saved token.
            if (ReferenceEquals(_link, link)) DropLink();
        }
    }

    private void FinishChatLogin(string account, string token)
    {
        if (string.IsNullOrWhiteSpace(account) || SteamChat.SteamIdOf(token) is null)
        {
            ChatLoginStatus.Text = "A Steam devolveu uma resposta que o launcher não entende.";
            ChatQrHost.Visibility = Visibility.Collapsed;
            return;
        }
        App.Settings.SteamChatToken = SteamChat.Compose(account, token);
        SaveSettings();
        _history = null;
        _qrCts = null;
        ChatQrHost.Visibility = Visibility.Collapsed;
        UpdateChatMode();
        ShowToast("Chat da Steam conectado.");
        _ = ConnectChatAsync();
        _ = LoadHistoryAsync();
        Dispatcher.BeginInvoke(FocusChat, DispatcherPriority.Input);
    }

    private void ShowQr(string url)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(8, new byte[] { 0, 0, 0, 255 }, new byte[] { 255, 255, 255, 255 });
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(png);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            ChatQr.Source = bitmap;
            ChatQrHost.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            ChatLoginStatus.Text = "Não foi possível desenhar o código QR.";
        }
    }

    private void ChatSignOut()
    {
        DropLink();
        _history = null;
        App.Settings.SteamChatToken = null;
        SaveSettings();
    }

    private void OnChatSignOutClick(object sender, RoutedEventArgs e)
    {
        ChatSignOut();
        UpdateChatAccountText();
        ShowToast("Você saiu do chat da Steam neste launcher.");
    }

    private void UpdateChatAccountText()
    {
        var connected = SteamChat.ParseSaved(App.Settings.SteamChatToken) is not null;
        ChatAccountText.Text = connected
            ? "Conectado. O launcher guarda só um token criptografado (nunca a senha). SAIR DO CHAT apaga esse token."
            : "Não conectado. Abra uma conversa em Amigos e aprove o código QR no app da Steam para conversar pelo launcher.";
        ChatSignOutButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
    }

    // ======================================================================================
    // Quick message wheel
    // ======================================================================================

    private void BuildWheel()
    {
        const double radiusX = 300, radiusY = 200;
        for (var i = 0; i < AppSettings.QuickMessageSlots; i++)
        {
            var angle = (-90 + 45 * i) * Math.PI / 180;
            var text = new TextBlock { FontSize = 20, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 };
            var item = new Border
            {
                Child = text,
                Padding = new Thickness(20, 10, 20, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform(Math.Cos(angle) * radiusX, Math.Sin(angle) * radiusY) } },
                Cursor = Cursors.Hand,
            };
            item.SetResourceReference(Border.BackgroundProperty, "T_2B323C");
            var index = i;
            item.MouseLeftButtonUp += (_, _) => { SetWheelIndex(index); ChooseWheel(); };
            _wheelItems.Add(item);
            _wheelTexts.Add(text);
            WheelCanvas.Children.Add(item);
        }
    }

    private void OnSelectHoldStarted()
    {
        _selectIgnored = false; // a hold is not a tap
        if (!IsActive || !ChatOpen || MenuOpen || PowerOpen || QuickOpen || OptionsOpen || ProfileOpen || MediaViewerOpen) return;
        OpenWheel();
    }

    private void OnSelectTapped()
    {
        if (!_selectIgnored) return;
        _selectIgnored = false;
        if (IsActive && ChatOpen && !MenuOpen && !WheelOpen) ToggleMenu();
    }

    private void OpenWheel()
    {
        if (WheelOpen || _chatFriend is null) return;
        if (!EnsureChatClient())
        {
            ShowToast("Entre no chat da Steam para usar as mensagens rápidas.");
            return;
        }

        VirtualKeyboard.Hide();
        var messages = AppSettings.NormalizeQuickMessages(App.Settings.QuickMessages);
        for (var i = 0; i < _wheelTexts.Count; i++) _wheelTexts[i].Text = messages[i];
        SetWheelIndex(-1);
        WheelLayer.Visibility = Visibility.Visible;
        _wheelTimer.Start();
        UiSound.Play("menu_open");
    }

    private void CloseWheel()
    {
        if (!WheelOpen) return;
        _wheelTimer.Stop();
        WheelLayer.Visibility = Visibility.Collapsed;
        FocusChat();
    }

    private void SetWheelIndex(int index)
    {
        _wheelIndex = index;
        for (var i = 0; i < _wheelItems.Count; i++)
        {
            var selected = i == index;
            var item = _wheelItems[i];
            if (selected) item.Background = (Brush)FindResource("Accent");
            else item.SetResourceReference(Border.BackgroundProperty, "T_2B323C");
            var scale = ((TransformGroup)item.RenderTransform).Children[0] as ScaleTransform;
            if (scale is not null) { scale.ScaleX = selected ? 1.1 : 1; scale.ScaleY = selected ? 1.1 : 1; }
        }
    }

    /// <summary>The right stick picks the slot in its direction (the choice stays when the stick returns to the middle).</summary>
    private void UpdateWheelFromStick()
    {
        var (x, y) = _gamepad.RightStick;
        if (Math.Sqrt(x * x + y * y) < 0.5) return;
        var degrees = Math.Atan2(x, y) * 180 / Math.PI; // 0 = up, clockwise
        var index = ((int)Math.Round(degrees / 45) % 8 + 8) % 8;
        if (index != _wheelIndex)
        {
            SetWheelIndex(index);
            UiSound.Play("nav");
        }
    }

    private void ChooseWheel()
    {
        if (_wheelIndex < 0) return;
        var messages = AppSettings.NormalizeQuickMessages(App.Settings.QuickMessages);
        var text = messages[_wheelIndex];
        CloseWheel();
        UiSound.Play("select");
        _ = SendChatTextAsync(text);
    }

    private void HandleWheelButton(GamepadButton button)
    {
        switch (button)
        {
            case GamepadButton.A: ChooseWheel(); break;
            case GamepadButton.B: CloseWheel(); break;
            case GamepadButton.Up: SetWheelIndex(0); break;
            case GamepadButton.Right: SetWheelIndex(2); break;
            case GamepadButton.Down: SetWheelIndex(4); break;
            case GamepadButton.Left: SetWheelIndex(6); break;
        }
    }

    // ======================================================================================
    // Settings > Interface: edit the quick messages
    // ======================================================================================

    private void BuildQuickMessageEditor()
    {
        QuickMessageList.Children.Clear();
        var messages = AppSettings.NormalizeQuickMessages(App.Settings.QuickMessages);
        for (var i = 0; i < AppSettings.QuickMessageSlots; i++)
        {
            var index = i;
            var box = new TextBox
            {
                Text = messages[i],
                FontSize = 16,
                Padding = new Thickness(8, 7, 8, 7),
                MaxLength = AppSettings.MaxQuickMessageLength,
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                Width = 420,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            box.SetResourceReference(Control.BackgroundProperty, "Panel");
            box.SetResourceReference(Control.BorderBrushProperty, "Focus");
            AutomationProperties.SetName(box, $"Mensagem rápida: {WheelPositionNames[i]}");
            box.TextChanged += (_, _) =>
            {
                if (_loadingSettings) return;
                var list = AppSettings.NormalizeQuickMessages(App.Settings.QuickMessages);
                list[index] = box.Text;
                App.Settings.QuickMessages = list;
                _keySaveTimer.Stop();
                _keySaveTimer.Start(); // saved shortly after typing stops
            };

            var row = new Border { Style = (Style)FindResource("SettingsRow"), Padding = new Thickness(24, 10, 24, 10), Margin = new Thickness(0, 0, 0, 6) };
            var grid = new Grid();
            grid.Children.Add(new TextBlock { Text = WheelPositionNames[i], FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
            grid.Children.Add(box);
            row.Child = grid;
            QuickMessageList.Children.Add(row);
        }
    }

    private void OnQuickMessagesResetClick(object sender, RoutedEventArgs e)
    {
        App.Settings.QuickMessages = new List<string>(AppSettings.DefaultQuickMessages);
        SaveSettings();
        BuildQuickMessageEditor();
        ShowToast("Mensagens rápidas restauradas.");
    }
}
