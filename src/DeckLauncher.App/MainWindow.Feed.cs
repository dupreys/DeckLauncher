using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeckLauncher.Core;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

// The Novidades / Amigos / Recomendados screens that open below the recent games row.
public partial class MainWindow
{
    private CancellationTokenSource? _feedCts;
    private bool _suppressFeedTab;
    private bool _feedWired;
    private string _feedTab = "news";
    private readonly Dictionary<string, (DateTime At, List<UIElement> Items)> _feedCache = new();
    private (DateTime At, FriendsSnapshot Data)? _friendsCache;

    private sealed record FriendsSnapshot(List<SteamFeeds.FriendData> Friends, List<SteamFeeds.ActivityData> Activity, string? Problem);

    // ---- Opening and closing ------------------------------------------------------------------

    private void OnHomeTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab }) OpenFeed(tab);
    }

    /// <summary>Down from the recent games row: the feed screens (Novidades first), like the Steam home.</summary>
    private bool TryOpenFeedFromRow()
    {
        if (_page != "Início" || PowerOpen || MenuOpen || QuickOpen) return false;
        if (Keyboard.FocusedElement is not DependencyObject focused || !IsAncestor(RecentHost, focused)) return false;
        OpenFeed("news");
        return true;
    }

    private RadioButton FeedRadio(string tab) => tab == "friends" ? FeedTabFriends : tab == "recs" ? FeedTabRecs : FeedTabNews;

    private void OpenFeed(string tab)
    {
        if (PowerOpen || ManageOpen || QuickOpen || OptionsOpen || ProfileOpen || AccountOpen || LaunchOverlayOpen) return;
        if (!_feedWired)
        {
            _feedWired = true;
            FeedContent.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnFeedCardClick));
        }

        ShowPage("Feed");
        SetFeedTab(tab);
        Dispatcher.BeginInvoke(() => { FeedRadio(tab).Focus(); }, DispatcherPriority.Loaded);
    }

    private void FocusFeed() => FeedRadio(_feedTab).Focus();

    private void SetFeedTab(string tab)
    {
        _feedTab = tab;
        _suppressFeedTab = true;
        FeedRadio(tab).IsChecked = true;
        _suppressFeedTab = false;
        _ = LoadFeedAsync(tab);
    }

    private void OnFeedTabChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressFeedTab || sender is not RadioButton { Tag: string tab }) return;
        _feedTab = tab;
        _ = LoadFeedAsync(tab);
    }

    // ---- Loading ------------------------------------------------------------------------------

    private async Task LoadFeedAsync(string tab)
    {
        _feedCts?.Cancel();
        var cts = _feedCts = new CancellationTokenSource();
        var ct = cts.Token;
        FeedScroll.ScrollToTop();

        if (_feedCache.TryGetValue(tab, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(10))
        {
            ShowFeed(cached.Items);
            return;
        }

        ShowFeed(new List<UIElement> { Note("Carregando…") });
        List<UIElement> items;
        var cacheable = true;
        try
        {
            (items, cacheable) = tab switch
            {
                "friends" => await BuildFriendsAsync(ct),
                "recs" => await BuildRecsAsync(ct),
                _ => await BuildNewsAsync(ct),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) return;
            items = new List<UIElement> { Note("Não foi possível carregar agora. Confira a conexão e tente de novo (troque de aba para tentar de novo).") };
            cacheable = false;
        }

        if (ct.IsCancellationRequested || _page != "Feed" || _feedTab != tab) return;
        if (cacheable) _feedCache[tab] = (DateTime.UtcNow, items);
        ShowFeed(items);
    }

    private void ShowFeed(List<UIElement> items)
    {
        FeedContent.Children.Clear();
        foreach (var item in items) FeedContent.Children.Add(item);
        FeedContent.Children.Add(new Border { Height = 30 });
    }

    private string? SteamIdValue()
    {
        if (_activeSteamId is not null && HasSeveralAccounts) return _activeSteamId; // the account chosen at start-up
        if (SteamLoginUsers.IsSteamId64(App.Settings.SteamId)) return App.Settings.SteamId;
        return _library.SteamPath is { } path ? SteamLoginUsers.ReadMostRecent(path)?.SteamId64 : null;
    }

    private async Task<FriendsSnapshot> GetFriendsAsync(CancellationToken ct)
    {
        if (_friendsCache is { } cached && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(5)) return cached.Data;

        var key = App.Settings.SteamWebApiKey;
        var id = SteamIdValue();
        var empty = new List<SteamFeeds.FriendData>();
        var none = new List<SteamFeeds.ActivityData>();
        if (string.IsNullOrWhiteSpace(key)) return new FriendsSnapshot(empty, none, "nokey");
        if (id is null) return new FriendsSnapshot(empty, none, "noid");

        try
        {
            var friends = await SteamFeeds.FetchFriendsAsync(LibraryService.Http, key, id, ct);
            var recent = friends.OrderByDescending(f => f.GameId is not null ? long.MaxValue : f.LastLogoff).Take(20).ToList();
            var activity = await SteamFeeds.FetchActivityAsync(LibraryService.Http, key, recent, ct);
            var data = new FriendsSnapshot(friends, activity, null);
            _friendsCache = (DateTime.UtcNow, data);
            return data;
        }
        catch (UnauthorizedAccessException)
        {
            return new FriendsSnapshot(empty, none, "private");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            return new FriendsSnapshot(empty, none, "offline");
        }
    }

    private static string ProblemText(string problem) => problem switch
    {
        "nokey" => "Para ver seus amigos, informe a sua chave da API Web da Steam em Configurações > Integrações (é gratuita).",
        "noid" => "Não encontrei a sua conta da Steam. Entre na Steam neste PC e abra o launcher de novo.",
        "private" => "A Steam recusou o pedido. Confira a chave e deixe a sua lista de amigos pública (Steam > Perfil > Privacidade).",
        _ => "Sem conexão com a Steam agora.",
    };

    // ---- Novidades ----------------------------------------------------------------------------

    private async Task<(List<UIElement>, bool)> BuildNewsAsync(CancellationToken ct)
    {
        var steamGames = _all.Select(i => i.Game).Where(g => g.Source == "Steam")
            .OrderByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue).ToList();
        var libraryIds = new HashSet<string>(steamGames.Select(g => g.Id));
        var steamPath = _library.SteamPath;

        var newsTask = steamGames.Count > 0
            ? SteamFeeds.FetchNewsAsync(LibraryService.Http, steamGames, ct)
            : Task.FromResult(new List<SteamFeeds.NewsData>());
        var updatedTask = Task.Run(() => steamPath is null ? new List<SteamFeeds.UpdatedData>() : SteamFeeds.ReadRecentlyUpdated(steamPath));
        var friendsTask = GetFriendsAsync(ct);
        var news = await newsTask;
        var updated = await updatedTask;
        var friends = await friendsTask;

        var items = new List<UIElement>();
        if (news.Count > 0) items.Add(CardRow(news.Select(n => (object)new NewsCard(n)), "NewsTemplate"));
        if (updated.Count > 0)
        {
            items.Add(Section("Recém-atualizados neste dispositivo"));
            items.Add(CardRow(updated.Select(u => (object)new UpdatedCard(u)), "UpdatedTemplate"));
        }

        List<CapsuleCard> popular;
        string popularTitle;
        if (friends.Problem is null)
        {
            popularTitle = "Populares entre amigos";
            popular = friends.Activity
                .GroupBy(a => a.AppId)
                .OrderByDescending(g => g.Select(a => a.FriendName).Distinct().Count()).ThenByDescending(g => g.Sum(a => a.Minutes))
                .Take(14)
                .Select(g => new CapsuleCard(g.Key, g.First().GameName, libraryIds.Contains(g.Key)))
                .ToList();
        }
        else
        {
            popularTitle = "Mais jogados agora na Steam";
            popular = (await SteamFeeds.FetchMostPlayedAsync(LibraryService.Http, ct))
                .Select(id => new CapsuleCard(id, null, libraryIds.Contains(id))).ToList();
        }

        if (popular.Count > 0)
        {
            items.Add(Section(popularTitle));
            items.Add(CardRow(popular.Cast<object>(), "CapsuleTemplate"));
        }

        if (items.Count == 0)
            return (new List<UIElement> { Note("Nada de novo por aqui ainda. Quando os seus jogos da Steam tiverem anúncios, eles aparecem nesta aba.") }, false);
        return (items, true);
    }

    // ---- Amigos -------------------------------------------------------------------------------

    private async Task<(List<UIElement>, bool)> BuildFriendsAsync(CancellationToken ct)
    {
        var snapshot = await GetFriendsAsync(ct);
        var items = new List<UIElement>();
        if (snapshot.Problem is not null)
        {
            items.Add(Note(ProblemText(snapshot.Problem)));
            if (snapshot.Problem is "nokey" or "private")
            {
                var open = new Button { Style = (Style)FindResource("TabText"), Content = "ABRIR CONFIGURAÇÕES", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
                open.Click += (_, _) => { ShowPage("Configurações"); FocusPage(); };
                items.Add(open);
            }
            return (items, false);
        }

        var libraryIds = new HashSet<string>(_all.Select(i => i.Game).Where(g => g.Source == "Steam").Select(g => g.Id));
        var playing = snapshot.Friends.Where(f => f.GameId is not null && f.GameName is not null).ToList();
        items.Add(Section(playing.Count == 0 ? "Nenhum amigo(a) está jogando agora"
            : playing.Count == 1 ? "1 amigo(a) está jogando agora" : $"{playing.Count} amigos estão jogando agora"));
        if (playing.Count > 0)
        {
            var cards = playing.GroupBy(f => f.GameId!)
                .Select(g => (object)new FriendGameCard(g.Key, g.First().GameName!, g.Select(f => f.Name).ToList(), libraryIds.Contains(g.Key)));
            items.Add(CardRow(cards, "FriendGameTemplate"));
        }

        var online = snapshot.Friends.Where(f => f.PersonaState != 0 && SteamFriends.ChatUrl(f.SteamId) is not null)
            .OrderByDescending(f => f.GameId is not null).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).Take(30).ToList();
        if (online.Count > 0)
        {
            items.Add(Section($"Amigos online ({online.Count})", "Ao escolher um amigo, a conversa abre no cliente oficial da Steam."));
            items.Add(CardRow(online.Select(f => (object)new FriendChatCard(f)), "FriendChatTemplate"));
        }

        items.Add(Section("Atividade"));
        items.Add(DayHeader("ÚLTIMAS 2 SEMANAS"));
        if (snapshot.Activity.Count == 0)
        {
            items.Add(Note("Seus amigos não jogaram nada nas últimas duas semanas (ou os perfis deles são privados)."));
        }
        else
        {
            items.Add(new ItemsControl
            {
                ItemsSource = snapshot.Activity.Take(10).Select(a => new ActivityCard(a)).ToList(),
                ItemTemplate = (DataTemplate)FindResource("ActivityTemplate"),
                Focusable = false,
            });
        }
        return (items, true);
    }

    // ---- Recomendados -------------------------------------------------------------------------

    private async Task<(List<UIElement>, bool)> BuildRecsAsync(CancellationToken ct)
    {
        var releases = await SteamFeeds.FetchReleasesAsync(LibraryService.Http, ct);
        var libraryIds = new HashSet<string>(_all.Select(i => i.Game).Where(g => g.Source == "Steam").Select(g => g.Id));
        var today = DateTime.Today;

        var days = releases
            .Where(r => r.Date.Date >= today.AddDays(-3) && r.Date.Date <= today.AddDays(45))
            .GroupBy(r => r.Date.Date)
            .OrderBy(g => g.Key)
            .Take(7)
            .Select(g => new CalendarDay(g.Key, g.Take(2).Select(r => new CapsuleCard(r.AppId, r.Name, libraryIds.Contains(r.AppId))).ToList()))
            .ToList();

        var items = new List<UIElement> { Section("Calendário de lançamentos", "Lançamentos populares de hoje e dos próximos dias na Steam.") };
        if (days.Count > 0) items.Add(CardRow(days.Cast<object>(), "CalendarTemplate"));
        else items.Add(Note("Não consegui carregar os lançamentos agora."));
        items.Add(DiscoveryBanner());
        return (items, days.Count > 0);
    }

    private FrameworkElement DiscoveryBanner()
    {
        var content = new Grid { Background = new LinearGradientBrush(Color.FromRgb(0x6A, 0x2F, 0x7F), Color.FromRgb(0x1D, 0x5A, 0x86), 0) };
        var texts = new StackPanel { Margin = new Thickness(56, 0, 56, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = "Explore a sua lista de descobrimento", FontSize = 32, FontWeight = FontWeights.SemiBold });
        texts.Children.Add(new TextBlock
        {
            Text = "Clique para abrir, na Steam, a lista de títulos sucessos de venda, novos e recomendados para você.",
            FontSize = 22, TextWrapping = TextWrapping.Wrap, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0),
        });
        content.Children.Add(texts);
        return new Button
        {
            Style = (Style)FindResource("FeedCard"),
            Height = 213,
            Margin = new Thickness(0, 26, 54, 0),
            Content = content,
            DataContext = new LinkCard("steam://openurl/https://store.steampowered.com/explore/"),
        };
    }

    // ---- Building blocks ----------------------------------------------------------------------

    private FrameworkElement Section(string title, string? subtitle = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 22, 0, 12) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.SemiBold });
        if (subtitle is not null)
            panel.Children.Add(new TextBlock { Text = subtitle, FontSize = 20, Foreground = (Brush)FindResource("TextSoft"), Margin = new Thickness(0, 4, 0, 0) });
        return panel;
    }

    private FrameworkElement DayHeader(string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 54, 10) };
        grid.Children.Add(new TextBlock { Text = text, FontSize = 17, Foreground = (Brush)FindResource("TextMuted"), HorizontalAlignment = HorizontalAlignment.Left });
        return grid;
    }

    private FrameworkElement Note(string text) => new TextBlock
    {
        Text = text, FontSize = 18, TextWrapping = TextWrapping.Wrap, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left,
        Foreground = (Brush)FindResource("TextMuted"), Margin = new Thickness(0, 16, 0, 0),
    };

    /// <summary>One horizontal, scrolling row of cards drawn with a template from Theme.xaml.</summary>
    private FrameworkElement CardRow(IEnumerable<object> items, string template)
    {
        var list = new ItemsControl
        {
            Style = (Style)FindResource("HorizontalList"),
            ItemsSource = items.ToList(),
            ItemTemplate = (DataTemplate)FindResource(template),
        };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = list,
            Padding = new Thickness(0, 0, 0, 6),
        };
        // The wheel scrolls the whole screen, not just the row.
        scroll.PreviewMouseWheel += (_, e) =>
        {
            FeedScroll.ScrollToVerticalOffset(FeedScroll.VerticalOffset - e.Delta);
            e.Handled = true;
        };
        return scroll;
    }

    // ---- Clicks -------------------------------------------------------------------------------

    private void OnFeedCardClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { DataContext: FeedItem { Url: { Length: > 0 } url } }) return;

        // A game that is in the library opens its own page here instead of going to Steam.
        var match = Regex.Match(url, @"^steam://(?:store|nav/games/details)/(\d+)$");
        if (match.Success && _all.FirstOrDefault(i => i.Game.Source == "Steam" && i.Game.Id == match.Groups[1].Value) is { } owned)
        {
            OpenGame(owned);
            return;
        }
        OpenExternal(url);
    }

    private void OpenExternal(string url)
    {
        // Links come from outside (news, store pages): only web and Steam links may be opened, never files or programs.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "steam"))
        {
            ShowToast("Este link não pode ser aberto.");
            return;
        }
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowToast("Não foi possível abrir (a Steam está instalada?).");
        }
    }

    private void OnSteamKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingSettings) return;
        var key = SteamKeyBox.Text.Trim();
        App.Settings.SteamWebApiKey = key.Length == 0 ? null : key;
        _friendsCache = null;
        _library.ResetOwned();
        _feedCache.Clear();
        _keySaveTimer.Stop();
        _keySaveTimer.Start(); // saved shortly after typing stops
    }

    private void OnGetSteamKeyClick(object sender, RoutedEventArgs e) => OpenExternal("https://steamcommunity.com/dev/apikey");
}
