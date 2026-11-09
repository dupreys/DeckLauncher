using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeckLauncher.Core;
using DeckLauncher.Core.Epic;
using DeckLauncher.Core.Manual;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

public partial class MainWindow : Window
{
    private const int HomeTileCount = 20;
    private static readonly TimeSpan RefreshWhenBack = TimeSpan.FromSeconds(45);

    private readonly LibraryService _library = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<string, GameItem> _itemCache = new();
    private readonly Dictionary<string, RadioButton> _tabs = new();

    private List<GameItem> _all = new();       // library order: by name
    private string _filter = "all";
    private string _page = "Início";
    private GameItem? _heroItem;
    private bool _refreshing;
    private List<GameItem> _notInstalled = new();
    private DateTime _lastRefresh = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };
        BuildTabs();
        InitInput();
    }

    private void UpdateClock()
    {
        Clock.Text = DateTime.Now.ToString("HH:mm");
        UpdateRoomStatus();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (XboxMode.Active) XboxMode.Log("DeckLauncher aberto pelo Modo Xbox.");
        ApplyHomeStyle();
        ApplyAppearance();
        ApplyFullscreen(App.Settings.StartFullscreen || XboxMode.Active);
        ApplyMouseMode();
        _ = CheckForUpdateAsync(manual: false);
        await ChooseAccountAtStartupAsync();
        await RefreshAsync();
    }

    /// <summary>With a game (or the store) in front, the launcher stops animating the wallpaper so it does not take frames from it.</summary>
    private void OnDeactivated(object? sender, EventArgs e) => StopHeroDrift();

    private async void OnActivated(object? sender, EventArgs e)
    {
        if (_page == "Início") StartHeroDrift();
        // Back from a game or from the store: pick up what was installed or played meanwhile.
        if (!_refreshing && DateTime.UtcNow - _lastRefresh > RefreshWhenBack) await RefreshAsync();
    }

    // ---- Library data -----------------------------------------------------------------------

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        if (_accountChoice is not null && !_accountCanCancel) return; // waiting for the start-up account choice
        _refreshing = true;
        try
        {
            var games = await FilterByAccountAsync(await _library.LoadAsync());
            ApplyGames(games);
            _ = DownloadArtAsync(games);
            _ = RefreshNotInstalledAsync(games);
        }
        finally
        {
            _refreshing = false;
            _lastRefresh = DateTime.UtcNow;
        }
    }

    /// <summary>Owned Steam games that are not installed (listed after the installed ones in the Steam tab, dimmed). Runs after the installed list is shown.</summary>
    private async Task RefreshNotInstalledAsync(List<Game> installed)
    {
        try
        {
            var games = await _library.LoadNotInstalledAsync(installed, SteamIdValue());
            ApplyNotInstalled(games);
            if (games.Count > 0 && await _library.DownloadNotInstalledCoversAsync(games))
                ApplyNotInstalled(await _library.LoadNotInstalledAsync(installed, SteamIdValue()));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // offline: the tab keeps what it had
        }
    }

    private void ApplyNotInstalled(List<Game> games)
    {
        var items = games.Select(ItemFor).ToList();
        var changed = !items.SequenceEqual(_notInstalled);
        _notInstalled = items;
        if (!changed) return;

        // Re-setting the grid takes the focus away, so only redo it when the Steam tab is the one in view, keeping the place.
        if (_filter == "steam") ApplyFilterKeepingPlace();
    }

    /// <summary>Redraws the list and puts the selector back on the same game, at the same scroll position (the user may be browsing).</summary>
    private void ApplyFilterKeepingPlace()
    {
        GameItem? focusedGame = Keyboard.FocusedElement is FrameworkElement { DataContext: GameItem g } element && IsAncestor(LibGrid, element) ? g : null;
        var offset = LibScroll.VerticalOffset;
        ApplyFilter();
        if (focusedGame is null) return;
        LibScroll.ScrollToVerticalOffset(offset);
        if (FindAll<Button>(LibGrid).FirstOrDefault(b => ReferenceEquals(b.DataContext, focusedGame)) is { } tile) tile.Focus();
        else FocusLibrary();
    }

    private string NotInstalledEmptyText() => _library.NotInstalledProblem switch
    {
        "noid" => "Não achei a sua conta da Steam. Abra a Steam neste PC com a sua conta ou entre em Configurações > Integrações.",
        "fetch" => "Não consegui ler os seus jogos da Steam. Veja a internet, a Web API key (Configurações > Integrações) e se os detalhes dos jogos estão públicos no seu perfil.",
        _ => "Nenhum jogo da Steam nesta lista.",
    };

    private async Task DownloadArtAsync(List<Game> games)
    {
        if (await _library.DownloadMissingArtAsync(games)) await RefreshAsync();
    }

    private GameItem ItemFor(Game game)
    {
        var key = GameSorter.Key(game);
        if (_itemCache.TryGetValue(key, out var item) && item.Matches(game)) return item;
        return _itemCache[key] = new GameItem(game);
    }

    private void ApplyGames(List<Game> games)
    {
        var recent = GameSorter.MostRecentFirst(games).Take(HomeTileCount).Select(ItemFor).ToList();
        _all = games.Select(ItemFor).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var any = _all.Count > 0;
        foreach (var item in _itemCache.Values) item.IsFeatured = false;
        if (recent.Count > 0)
        {
            recent[0].IsFeatured = true; // the last played game is the wide one
            recent[0].EnsureWideLoaded();
        }
        RecentRow.ItemsSource = recent;
        MoreTile.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentTitle.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Text = _library.AnyStoreFound
            ? "Nenhum jogo instalado foi encontrado. Instale um jogo na Steam ou na Epic, ou adicione um manualmente."
            : "Steam e Epic não foram encontradas neste PC. Instale uma delas ou adicione jogos manualmente.";
        FillHomeStyles(recent, any, EmptyText.Text);

        ApplyFilter();
        if (_page == "Início" && !MenuOpen && !IsFocusInside(HomePage)) FocusHome();
    }

    // ---- Library tabs, search and grid ------------------------------------------------------

    private static readonly (string Key, string Title)[] TabDefs =
    {
        ("all", "TODOS OS JOGOS"), ("steam", "STEAM"), ("epic", "EPIC"), ("manual", "ADICIONADOS"), ("fav", "FAVORITOS"), ("apps", "APLICATIVOS"),
    };

    private void BuildTabs()
    {
        foreach (var (key, title) in TabDefs)
        {
            var tab = new RadioButton { Style = (Style)FindResource("LibTab"), GroupName = "LibTabs", Tag = key, Content = title, IsChecked = key == _filter };
            tab.Checked += (_, _) => { _filter = key; ApplyFilter(); };
            _tabs[key] = tab;
            TabsPanel.Children.Add(tab);
        }
        InitApps();
    }

    private bool InFilter(GameItem item, string filter) => filter switch
    {
        "steam" => item.Source == SteamProvider.ProviderName,
        "epic" => item.Source == EpicProvider.ProviderName,
        "manual" => item.Source == ManualProvider.ProviderName,
        "fav" => item.IsFavorite,
        _ => true,
    };

    private void ApplyFilter()
    {
        foreach (var (key, title) in TabDefs)
        {
            var count = key == "apps" ? AppsCount : _all.Count(i => InFilter(i, key));
            _tabs[key].Content = BuildTabLabel(title, count);
        }

        var text = SearchBox.Text.Trim();
        var onApps = _filter == "apps";
        LibScroll.Visibility = onApps ? Visibility.Collapsed : Visibility.Visible;
        AppsScroll.Visibility = onApps ? Visibility.Visible : Visibility.Collapsed;
        if (onApps)
        {
            LibEmpty.Visibility = Visibility.Collapsed;
            RebuildApps();
            return;
        }

        // Steam tab: the installed games first, then the owned ones that are not installed (shown dimmed).
        IEnumerable<GameItem> source = _all.Where(i => InFilter(i, _filter));
        if (_filter == "steam") source = source.Concat(_notInstalled);
        var shown = source.Where(i => text.Length == 0 || i.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        LibGrid.ItemsSource = shown;
        ResetLibrarySelector();
        LibEmpty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibEmpty.Text = _filter == "steam" && _library.NotInstalledProblem is not null && text.Length == 0 ? NotInstalledEmptyText()
            : _all.Count == 0 ? "Sua biblioteca está vazia." : "Nenhum jogo nesta lista.";
    }

    private object BuildTabLabel(string title, int count)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = title };
        name.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(RadioButton), 1) });
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = count.ToString(), Margin = new Thickness(10, 0, 0, 0), Opacity = 0.6, Foreground = (Brush)FindResource("TextMuted") });
        return panel;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    // ---- Tiles ------------------------------------------------------------------------------

    private void OnTileLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GameItem item }) return;
        if (item.IsFeatured) item.EnsureWideLoaded(); else item.EnsureCoverLoaded();
    }

    private async void OnHomeTileFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not Button { DataContext: GameItem item }) return;
        if (item.IsFeatured) item.EnsureWideLoaded(); else item.EnsureCoverLoaded();
        _heroItem = item;
        CaptionTitle.Text = item.Name;
        CaptionSub.Text = item.RecentText;
        var image = await item.LoadBackdropAsync();
        if (!ReferenceEquals(_heroItem, item)) return; // ignore pictures that arrive after the focus moved on
        SetHero(image);
        SetGlow(item.Cover ?? image);
    }

    private void OnMoreTileClick(object sender, RoutedEventArgs e)
    {
        ShowPage("Biblioteca");
        FocusLibrary();
    }

    /// <summary>
    /// The white selector glides to the focused cover and the list scrolls smoothly so the cover (and the caption under it)
    /// stay in view.
    /// </summary>
    private void OnLibTileFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not FrameworkElement tile || tile.ActualWidth <= 0) return;
        var pos = tile.TranslatePoint(new Point(0, 0), LibContent);
        const double zoom = 0.02; // the tile grows 4 %: the selector is a little bigger than the tile
        var tileWidth = tile.ActualWidth * CardScale;
        var tileHeight = tile.ActualHeight * CardScale;
        var x = pos.X - tileWidth * zoom;
        var y = pos.Y - tileHeight * zoom;
        var w = tileWidth * (1 + 2 * zoom);
        var h = tileHeight * (1 + 2 * zoom);

        var span = TimeSpan.FromMilliseconds(170);
        if (LibCursor.Visibility != Visibility.Visible || !IsLoaded)
        {
            LibCursor.Visibility = Visibility.Visible;
            foreach (var property in new[] { Canvas.LeftProperty, Canvas.TopProperty, WidthProperty, HeightProperty }) LibCursor.BeginAnimation(property, null);
            Canvas.SetLeft(LibCursor, x);
            Canvas.SetTop(LibCursor, y);
            LibCursor.Width = w;
            LibCursor.Height = h;
        }
        else
        {
            void Glide(DependencyProperty property, double to) =>
                LibCursor.BeginAnimation(property, new DoubleAnimation(to, span) { EasingFunction = EaseOut });
            Glide(Canvas.LeftProperty, x);
            Glide(Canvas.TopProperty, y);
            Glide(WidthProperty, w);
            Glide(HeightProperty, h);
        }

        ScrollLibraryTo(pos.Y, tileHeight);
    }

    private void ScrollLibraryTo(double tileTop, double tileHeight)
    {
        var current = _libAnimating ? _libTarget : LibScroll.VerticalOffset;
        var viewport = LibScroll.ViewportHeight;
        if (viewport <= 0) return;
        var needTop = tileTop - 28;
        var needBottom = tileTop + tileHeight + 28 + 56; // + room for the caption under the cover
        var target = current;
        if (needTop < current) target = needTop;
        else if (needBottom > current + viewport) target = needBottom - viewport;
        else return;
        if (target < 70) target = 0; // first row: show the whole top margin
        target = Math.Clamp(target, 0, Math.Max(0, LibScroll.ExtentHeight - viewport));
        if (Math.Abs(target - current) < 0.5) return;

        _libTarget = target;
        _libAnimating = true;
        var animation = new DoubleAnimation(LibScroll.VerticalOffset, target, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut };
        animation.Completed += (_, _) => { if (Math.Abs(_libTarget - target) < 0.5) _libAnimating = false; };
        BeginAnimation(LibOffsetProperty, animation);
    }

    /// <summary>A new list (tab, search, refresh): the old selector position and scroll belong to tiles that no longer exist.</summary>
    private void ResetLibrarySelector()
    {
        foreach (var property in new[] { Canvas.LeftProperty, Canvas.TopProperty, WidthProperty, HeightProperty }) LibCursor.BeginAnimation(property, null);
        LibCursor.Visibility = Visibility.Collapsed;
        BeginAnimation(LibOffsetProperty, null);
        _libAnimating = false;
        LibScroll.ScrollToVerticalOffset(0);
        LibGrid.UpdateLayout(); // the first tile of the new list must have its real position before it takes the focus
    }

    private void OnLibGridLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is DependencyObject next && IsAncestor(LibGrid, next)) return;
        LibCursor.Visibility = Visibility.Collapsed;
    }

    /// <summary>Whole columns only, so the grid can be centred instead of leaving a wide empty strip on the right.</summary>
    private void OnLibScrollSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLibGridWidth();

    private void UpdateLibGridWidth()
    {
        const double cell = 244 + 66; // tile + both side margins
        var columns = Math.Max(1, (int)Math.Floor((LibScroll.ActualWidth - 40) / (cell * CardScale)));
        LibGrid.Width = columns * cell; // the card size scales the whole grid (LayoutTransform), so this stays in unscaled units
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GameItem item }) return;
        OpenGame(item);
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ---- Focus helpers ----------------------------------------------------------------------

    private static bool IsFocusInside(DependencyObject root) =>
        Keyboard.FocusedElement is DependencyObject focused && IsAncestor(root, focused);

    private static bool IsAncestor(DependencyObject root, DependencyObject node)
    {
        for (var current = node; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (ReferenceEquals(current, root)) return true;
        return false;
    }

    private static T? FindFirst<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindFirst<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private void FocusLibrary(int attempt = 0)
    {
        if (_filter == "apps")
        {
            if (FindFirst<Button>(AppsPanel) is { } firstApp) firstApp.Focus();
            else if (_tabs.TryGetValue("apps", out var appsTab)) appsTab.Focus();
            return;
        }
        if (FindFirst<Button>(LibGrid) is { } first) { first.Focus(); return; }
        if (_tabs.TryGetValue(_filter, out var tab) && (attempt >= 3 || LibGrid.Items.Count == 0)) { tab.Focus(); return; }
        if (attempt < 3) Dispatcher.BeginInvoke(() => FocusLibrary(attempt + 1), DispatcherPriority.Loaded);
    }

    // ---- Menu and pages ---------------------------------------------------------------------

    private bool MenuOpen => MenuLayer.Visibility == Visibility.Visible;

    private void OpenMenu()
    {
        MenuLayer.Visibility = Visibility.Visible;
        UiSound.Play("menu_open");
        SlideInMenu();
        var shown = _page == "Jogo" ? _returnPage : _page;
        var current = MenuList.Children.OfType<Button>().FirstOrDefault(b => (string)b.Tag == shown) ?? MenuList.Children.OfType<Button>().First(b => b.Visibility == Visibility.Visible);
        current.Focus();
    }

    private void CloseMenu(bool silent = false)
    {
        if (!silent && MenuOpen) UiSound.Play("menu_close");
        MenuLayer.Visibility = Visibility.Collapsed;
        FocusPage();
    }

    private void FocusPage()
    {
        if (_page == "Início") FocusHome();
        else if (_page == "Biblioteca") FocusLibrary();
        else if (_page == "Jogo") PlayButton.Focus();
        else if (_page == "Feed") FocusFeed();
        else if (_page == "Mídias") FocusMedia(_mediaIndex);
        else if (_page == "Lojas") StoreSteamButton.Focus();
        else if (_page == "Downloads") OpenSteamDownloadsButton.Focus();
        else if (_page == "Configurações")
        {
            var current = FindAll<RadioButton>(SettingsPage).FirstOrDefault(r => r.IsChecked == true);
            current?.Focus();
        }
    }

    private void OnScrimClick(object sender, MouseButtonEventArgs e) => CloseMenu();

    private void OnMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page }) return;
        if (page == "Ligar/Desligar")
        {
            CloseMenu(silent: true);
            OpenPower();
            return;
        }
        ShowPage(page);
        CloseMenu(silent: true);
    }

    private void ShowPage(string page)
    {
        var previous = _page;
        _page = page;
        var home = page == "Início";
        var library = page == "Biblioteca";
        var game = page == "Jogo";
        if (page == "Biblioteca" && previous is not ("Biblioteca" or "Jogo") && _tabs.TryGetValue("all", out var allTab) && allTab.IsChecked != true)
            allTab.IsChecked = true; // the Library always opens on "all games"; Aplicativos picks its own tab afterwards
        HomePage.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        LibraryPage.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        GamePage.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
        var downloads = page == "Downloads";
        var settings = page == "Configurações";
        DownloadsPage.Visibility = downloads ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        if (downloads) StartDownloads(); else StopDownloads();
        if (settings) LoadSettingsUi();
        var feed = page == "Feed";
        FeedPage.Visibility = feed ? Visibility.Visible : Visibility.Collapsed;
        var media = page == "Mídias";
        MediaPage.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        if (media) LoadMedia(); else _mediaLoadGen++;
        var stores = page == "Lojas";
        StoresPage.Visibility = stores ? Visibility.Visible : Visibility.Collapsed;
        OtherPage.Visibility = home || library || game || downloads || settings || feed || media || stores ? Visibility.Collapsed : Visibility.Visible;
        if (home) StartHeroDrift(); else StopHeroDrift();
        if (home && previous != page && !IsClassicHome) { UpdateRoomStatus(refreshSlowParts: true); _ = RefreshHomeInfoAsync(force: false); }
        SearchHost.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        HeroImage.Visibility = (home && HomeStyleKey != StyleRoomKey) || game ? Visibility.Visible : Visibility.Collapsed;
        HeroImagePrev.Visibility = HeroImage.Visibility;
        UpdateHeroShade(home);
        if (previous != page) PlayPageTransition(page);
        OtherTitle.Text = page;
        UpdateHints();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F1:
                ToggleMenu();
                e.Handled = true;
                break;
            case Key.Escape:
                GoBack();
                e.Handled = true;
                break;
            case Key.F5:
                _ = RefreshAsync();
                e.Handled = true;
                break;
            case Key.F11: // handy while developing: window <-> full screen
                ApplyFullscreen(WindowStyle != WindowStyle.None);
                e.Handled = true;
                break;
        }
    }
}
