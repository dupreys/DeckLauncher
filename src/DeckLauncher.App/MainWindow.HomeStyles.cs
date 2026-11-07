using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

// The home styles chosen in Settings > Interface: "Xbox" (Clássico, the original row), "Panel" (Painel) and "Room" (Sala OLED).
// All three are always filled with the same recent games; only one is visible.
// Layout of this file: style switch and data, focus, clicks, Sala status (clock, battery, network, running game), keyboard/controller movement.
public partial class MainWindow
{
    private const string StyleClassicKey = "Xbox", StylePanelKey = "Panel", StyleRoomKey = "Room";
    private DateTime _homeInfoAt = DateTime.MinValue;
    private bool _homeInfoBusy;
    private int? _friendsOnline;
    private int _statusTick;

    private string HomeStyleKey => App.Settings.HomeStyle;
    private bool IsClassicHome => HomeStyleKey != StylePanelKey && HomeStyleKey != StyleRoomKey;

    // ---- Style switch and data ---------------------------------------------------------------

    /// <summary>Shows the layout that matches the saved style and hides the other two.</summary>
    private void ApplyHomeStyle()
    {
        var style = HomeStyleKey;
        ClassicHome.Visibility = IsClassicHome ? Visibility.Visible : Visibility.Collapsed;
        PanelHome.Visibility = style == StylePanelKey ? Visibility.Visible : Visibility.Collapsed;
        RoomHome.Visibility = style == StyleRoomKey ? Visibility.Visible : Visibility.Collapsed;
        // The game-art background belongs to the classic and Painel screens; the Sala is pure black.
        HeroImage.Visibility = (_page == "Início" && style != StyleRoomKey) || _page == "Jogo" ? Visibility.Visible : Visibility.Collapsed;
        HeroImagePrev.Visibility = HeroImage.Visibility;
        UpdateRoomStatus(refreshSlowParts: true);
        if (!IsClassicHome) _ = RefreshHomeInfoAsync(force: true);
    }

    /// <summary>Fills the Painel and the Sala from the recent games (called by ApplyGames).</summary>
    private void FillHomeStyles(List<GameItem> recent, bool any, string emptyText)
    {
        PanelFeatured.DataContext = recent.Count > 0 ? recent[0] : null;
        PanelFeatured.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelGrid.ItemsSource = recent.Skip(1).Take(6).ToList();
        RoomRow.ItemsSource = recent.Take(6).ToList();
        PanelEmpty.Visibility = RoomEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        PanelEmpty.Text = RoomEmpty.Text = emptyText;
        if (recent.Count == 0) RoomName.Text = RoomSub.Text = "";
        PanelInfoLibraryText.Text = _all.Count == 1 ? "1 jogo" : $"{_all.Count} jogos";
        if (!IsClassicHome) _ = RefreshHomeInfoAsync(force: false);
    }

    /// <summary>The game tiles of the visible home, in reading order.</summary>
    private IEnumerable<Button> HomeTiles() => HomeStyleKey switch
    {
        StylePanelKey => FindAll<Button>(PanelHome).Where(b => b.DataContext is GameItem),
        StyleRoomKey => FindAll<Button>(RoomRow),
        _ => FindAll<Button>(RecentRow),
    };

    // ---- Focus -------------------------------------------------------------------------------

    private void FocusHome(int attempt = 0)
    {
        var done = HomeStyleKey switch
        {
            StylePanelKey => PanelFeatured.IsVisible && PanelFeatured.Focus() || PanelInfoLibrary.IsVisible && PanelInfoLibrary.Focus(),
            StyleRoomKey => FindFirst<Button>(RoomRow) is { } icon ? icon.Focus() : RoomChips().FirstOrDefault()?.Focus() == true,
            _ => RecentRow.Items.Count > 0 && FindFirst<Button>(RecentRow) is { } first && first.Focus(),
        };
        // The tiles may not exist yet right after the list was filled: try again a few times.
        var expectTiles = IsClassicHome ? RecentRow.Items.Count > 0 : true;
        if (!done && expectTiles && attempt < 20) Dispatcher.BeginInvoke(() => FocusHome(attempt + 1), DispatcherPriority.Loaded);
    }

    // ---- Clicks ------------------------------------------------------------------------------

    private void OnRoomTileFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not Button { DataContext: GameItem item }) return;
        item.EnsureCoverLoaded();
        _heroItem = item;
        RoomName.Text = item.Name;
        RoomSub.Text = item.RecentText;
    }

    private void OnHomeShortcutClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target }) return;
        switch (target)
        {
            case "Aplicativos":
                ShowPage("Biblioteca");
                if (_tabs.TryGetValue("apps", out var appsTab)) appsTab.IsChecked = true;
                FocusLibrary();
                break;
            case "Amigos":
                OpenFeed("friends");
                break;
            case "Biblioteca":
                ShowPage("Biblioteca");
                FocusLibrary();
                break;
            default: // Lojas, Downloads, Mídias
                ShowPage(target);
                FocusPage();
                break;
        }
    }

    private void OnRoomNowPlayingClick(object sender, RoutedEventArgs e)
    {
        if (_all.FirstOrDefault(i => i.IsRunning) is { } running) OpenGame(running);
    }

    /// <summary>The style cards in Settings work like radio buttons: the clicked one is on, the others off, and the home changes at once.</summary>
    private void OnHomeStyleClick(object sender, RoutedEventArgs e) =>
        OnChoiceClick(sender, key => App.Settings.HomeStyle = key, ShowHomeStyleChecks, ApplyHomeStyle);

    private void ShowHomeStyleChecks() =>
        SetChoice(HomeStyleKey, (StyleClassic, StyleClassicKey), (StylePanel, StylePanelKey), (StyleRoom, StyleRoomKey));

    /// <summary>Marks the option whose key is chosen and clears the others (the toggles of the Interface page behave like radio buttons).</summary>
    private static void SetChoice(string chosen, params (ToggleButton Button, string Key)[] options)
    {
        foreach (var (button, key) in options) button.IsChecked = key == chosen;
    }

    /// <summary>Shared click of the Interface choices: store the key, save, refresh the marks and apply.</summary>
    private void OnChoiceClick(object sender, Action<string> store, Action showChecks, Action apply)
    {
        if (_loadingSettings || sender is not ToggleButton { Tag: string key }) return;
        store(key);
        SaveSettings();
        showChecks(); // a toggle flips itself when pressed: put the marks back to what is saved
        apply();
    }

    // ---- Sala status: clock, greeting, battery, network, running game ------------------------------------

    /// <summary>Called every second by the clock; the cheap parts every time, battery and network every 30 s.</summary>
    private void UpdateRoomStatus(bool refreshSlowParts = false)
    {
        if (RoomHome.Visibility != Visibility.Visible) return;
        RoomClock.Text = DateTime.Now.ToString("HH:mm");
        var hour = DateTime.Now.Hour;
        var greeting = hour < 5 ? "Boa madrugada" : hour < 12 ? "Bom dia" : hour < 18 ? "Boa tarde" : "Boa noite";
        RoomGreeting.Text = _friendsOnline is { } n
            ? $"{greeting}  ·  {(n == 1 ? "1 amigo online" : n + " amigos online")}"
            : greeting;

        var running = _all.FirstOrDefault(i => i.IsRunning);
        RoomNowPlaying.Visibility = running is null ? Visibility.Collapsed : Visibility.Visible;
        if (running is not null) RoomNowPlaying.Content = "▶  Jogando: " + running.Name;

        if (!refreshSlowParts && _statusTick++ % 30 != 0) return;
        var parts = new List<string>();
        if (Native.Battery() is { } battery) parts.Add($"Bateria {battery.Percent}%{(battery.Charging ? " (carregando)" : "")}");
        var connections = GetConnections();
        parts.Add(connections.Count == 0 ? "Sem rede" : connections.Any(c => !c.Wifi) ? "Cabo" : "Wi-Fi");
        RoomStatus.Text = string.Join("  ·  ", parts);
    }

    /// <summary>Downloads in progress and friends online for the Painel tiles and the Sala greeting (at most every 20 s).</summary>
    private async Task RefreshHomeInfoAsync(bool force)
    {
        if (_homeInfoBusy || (!force && DateTime.UtcNow - _homeInfoAt < TimeSpan.FromSeconds(20))) return;
        _homeInfoBusy = true;
        try
        {
            var steamPath = _library.SteamPath;
            var downloads = await Task.Run(() => steamPath is null ? new List<DownloadInfo>() : SteamDownloads.Read(steamPath).ToList());
            PanelInfoDownloadsText.Text = DownloadsSummary(downloads);

            if (string.IsNullOrWhiteSpace(App.Settings.SteamWebApiKey) || await GetFriendsAsync(CancellationToken.None) is not { Problem: null } snapshot)
            {
                _friendsOnline = null;
                PanelInfoFriendsText.Text = "Ver amigos";
            }
            else
            {
                var online = snapshot.Friends.Count(f => f.PersonaState != 0);
                var playing = snapshot.Friends.Count(f => f.GameId is not null);
                _friendsOnline = online;
                PanelInfoFriendsText.Text = playing > 0 ? $"{online} online · {playing} jogando" : $"{online} online";
            }
            UpdateRoomStatus();
            _homeInfoAt = DateTime.UtcNow;
        }
        catch (Exception)
        {
            // The tiles are only a convenience: keep whatever they showed.
        }
        finally
        {
            _homeInfoBusy = false;
        }
    }

    private static string DownloadsSummary(List<DownloadInfo> downloads)
    {
        if (downloads.Count == 0) return "Nenhum em andamento";
        var active = downloads.Where(d => d.Status == DownloadStatus.Downloading).ToList();
        var shown = active.Count > 0 ? active : downloads;
        var percent = (int)Math.Round(shown.Average(d => d.Fraction) * 100);
        return active.Count > 0 ? $"{active.Count} em andamento · {percent}%" : $"{downloads.Count} na fila · {percent}%";
    }

    // ---- Controller / keyboard movement inside the Painel and the Sala ------------------------------
    // Explicit rules (one press = one step) instead of the geometric focus search.

    private List<UIElement> RoomChips() =>
        FindAll<Button>(RoomHome).Where(b => b.DataContext is not GameItem && b.IsVisible).Cast<UIElement>().ToList();

    /// <summary>True when the key was handled here.</summary>
    private bool TryHomeStyleMove(FocusNavigationDirection direction)
    {
        if (IsClassicHome || PowerOpen || MenuOpen || QuickOpen || OptionsOpen || ProfileOpen || LaunchOverlayOpen) return false;
        if (Keyboard.FocusedElement is not UIElement focused) return false;
        return HomeStyleKey == StylePanelKey ? TryPanelMove(direction, focused) : TryRoomMove(direction, focused);
    }

    private void GoTo(UIElement target)
    {
        _navUntil = DateTime.UtcNow.AddMilliseconds(200);
        target.Focus();
    }

    private static bool Bump()
    {
        UiSound.Play("bump");
        return true;
    }

    /// <summary>Left/right inside one row of tiles: one step, and a bump at the ends. False for the other directions.</summary>
    private bool StepInRow(List<UIElement> row, int index, FocusNavigationDirection direction)
    {
        if (direction is not (FocusNavigationDirection.Left or FocusNavigationDirection.Right)) return false;
        var next = index + (direction == FocusNavigationDirection.Right ? 1 : -1);
        if (next < 0 || next >= row.Count) return Bump();
        GoTo(row[next]);
        return true;
    }

    private bool TryPanelMove(FocusNavigationDirection direction, UIElement focused)
    {
        const int columns = 3;
        var grid = FindAll<Button>(PanelGrid).Cast<UIElement>().ToList();
        var info = FindAll<Button>(PanelHome).Where(b => b.Style == PanelInfoLibrary.Style).Cast<UIElement>().ToList();
        var hasFeatured = PanelFeatured.IsVisible;
        var (left, right, up, down) = Directions(direction);

        if (hasFeatured && ReferenceEquals(focused, PanelFeatured))
        {
            if (right) { if (grid.Count > 0) GoTo(grid[0]); else Bump(); return true; }
            if (down) { if (info.Count > 0) GoTo(info[0]); return true; }
            return left ? Bump() : false; // up: the top bar
        }

        var g = grid.FindIndex(t => IsAncestor(t, focused));
        if (g >= 0)
        {
            if (right) return g % columns == columns - 1 || g + 1 >= grid.Count ? Bump() : Go(grid[g + 1]);
            if (left)
            {
                if (g % columns != 0) return Go(grid[g - 1]);
                return hasFeatured ? Go(PanelFeatured) : Bump();
            }
            if (up) return g >= columns && Go(grid[g - columns]);
            if (down)
            {
                if (g + columns < grid.Count) return Go(grid[g + columns]);
                return info.Count == 0 || Go(info[Math.Min(info.Count - 1, 2 + g % columns)]);
            }
            return false;
        }

        var j = info.FindIndex(t => ReferenceEquals(t, focused) || IsAncestor(t, focused));
        if (j < 0) return false;
        if (StepInRow(info, j, direction)) return true;
        if (up)
        {
            if (j >= 2 && grid.Count > 0) GoTo(grid[Math.Min(grid.Count - 1, columns + (j - 2))]);
            else if (hasFeatured) GoTo(PanelFeatured);
            else if (grid.Count > 0) GoTo(grid[0]);
            return true;
        }
        return down && Bump();
    }

    private bool TryRoomMove(FocusNavigationDirection direction, UIElement focused)
    {
        var icons = FindAll<Button>(RoomRow).Cast<UIElement>().ToList();
        var chips = RoomChips();
        var (_, _, up, down) = Directions(direction);

        var i = icons.FindIndex(t => ReferenceEquals(t, focused) || IsAncestor(t, focused));
        if (i >= 0)
        {
            if (StepInRow(icons, i, direction)) return true;
            if (down && chips.Count > 0) return Go(chips[Math.Min(chips.Count - 1, i * chips.Count / Math.Max(1, icons.Count))]);
            return down; // up: the top bar
        }

        var c = chips.IndexOf(focused);
        if (c < 0) return false;
        if (StepInRow(chips, c, direction)) return true;
        if (up && icons.Count > 0) return Go(icons[Math.Min(icons.Count - 1, c * icons.Count / Math.Max(1, chips.Count))]);
        return down && Bump();
    }

    private static (bool Left, bool Right, bool Up, bool Down) Directions(FocusNavigationDirection d) =>
        (d == FocusNavigationDirection.Left, d == FocusNavigationDirection.Right, d == FocusNavigationDirection.Up, d == FocusNavigationDirection.Down);

    /// <summary>Moves focus to the target and reports the key as handled.</summary>
    private bool Go(UIElement target)
    {
        GoTo(target);
        return true;
    }
}
