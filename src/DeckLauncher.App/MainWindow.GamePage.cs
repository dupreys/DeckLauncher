using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeckLauncher.Core;
using DeckLauncher.Core.Manual;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

// The game page: banner with the logo, Play button, last session / play time, favourite and the details tabs.
public partial class MainWindow
{
    private static readonly CultureInfo Portuguese = new("pt-BR");

    private GameItem? _gameItem;
    private string _returnPage = "Início";
    private CancellationTokenSource? _detailsCts;

    private void OpenGame(GameItem item)
    {
        if (_page != "Jogo") _returnPage = _page;
        _gameItem = item;
        item.EnsureWideLoaded();
        GamePage.DataContext = item;

        StatLast.Text = LastPlayedText(item.Game.LastPlayed);
        StatTime.Text = PlaytimeText(item.Game);
        StatStore.Text = StoreName(item.Game);
        UpdateFavoriteIcon(item);
        GameAbout.Text = "";
        GamePlayers.Text = "";
        BuildInfoRows(item, null);
        GameTabAbout.IsChecked = true;

        ShowPage("Jogo");
        UpdatePlayButton();
        _ = LoadGameHeroAsync(item);
        _ = LoadDetailsAsync(item);
        Dispatcher.BeginInvoke(() => PlayButton.Focus(), DispatcherPriority.Loaded);
    }

    private void CloseGame()
    {
        var item = _gameItem;
        _detailsCts?.Cancel();
        ShowPage(_returnPage);
        Dispatcher.BeginInvoke(() =>
        {
            var tiles = _returnPage == "Biblioteca" ? FindAll<Button>(LibGrid) : HomeTiles();
            var tile = item is null ? null : tiles.FirstOrDefault(b => ReferenceEquals(b.DataContext, item));
            if (tile is not null) { tile.Focus(); tile.BringIntoView(); }
            else FocusPage();
        }, DispatcherPriority.Loaded);
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }

    private async Task LoadGameHeroAsync(GameItem item)
    {
        var image = await item.LoadBackdropAsync();
        if (ReferenceEquals(_gameItem, item) && _page == "Jogo") SetHero(image);
    }

    private async Task LoadDetailsAsync(GameItem item)
    {
        _detailsCts?.Cancel();
        var cts = _detailsCts = new CancellationTokenSource();
        try
        {
            var details = await GameInfo.FetchAsync(LibraryService.Http, item.Game, _library.SteamPath, App.Settings.SteamId, cts.Token);
            if (cts.IsCancellationRequested) return;

            GameAbout.Text = details.Description
                ?? (item.Game.Source == SteamProvider.ProviderName ? "" : "Sem descrição disponível para este jogo.");
            GamePlayers.Text = details.PlayersNow is { } now ? $"{now.ToString("N0", Portuguese)} jogadores agora na Steam" : "";
            BuildInfoRows(item, details);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Offline: the page simply shows what is known locally.
        }
    }

    // ---- Texts ------------------------------------------------------------------------------

    private static string LastPlayedText(DateTimeOffset? played)
    {
        if (played is not { } date) return "Nunca";
        var days = (DateTime.Today - date.LocalDateTime.Date).Days;
        return days switch
        {
            <= 0 => "hoje",
            1 => "ontem",
            < 30 => $"há {days} dias",
            _ => date.LocalDateTime.ToString("d", Portuguese),
        };
    }

    private string PlaytimeText(Game game)
    {
        int? minutes = null;
        if (game.Source == SteamProvider.ProviderName && _library.SteamPath is { } steam)
            minutes = SteamPlaytime.Read(steam, App.Settings.SteamId, game.Id);
        else if (App.Settings.PlayRecords.TryGetValue(GameSorter.Key(game), out var record) && record.Minutes > 0)
            minutes = record.Minutes;

        return minutes switch
        {
            null or <= 0 => "—",
            < 60 => $"{minutes} min",
            _ => $"{(minutes.Value / 60.0).ToString("N1", Portuguese)} horas",
        };
    }

    private static string StoreName(Game game) => game.Source == ManualProvider.ProviderName ? "Adicionado" : game.Source;

    private static string SizeText(long bytes) =>
        bytes >= 1L << 30 ? $"{(bytes / (double)(1L << 30)).ToString("N1", Portuguese)} GB" : $"{(bytes / (double)(1L << 20)).ToString("N0", Portuguese)} MB";

    private void BuildInfoRows(GameItem item, GameDetails? details)
    {
        var game = item.Game;
        var rows = new List<(string Label, string? Value)>
        {
            ("Loja", StoreName(game)),
            ("Desenvolvedora", details?.Developer),
            ("Lançamento", details?.ReleaseDate),
            ("Gêneros", details is { Genres.Count: > 0 } ? string.Join(", ", details.Genres) : null),
            ("Tamanho em disco", game.SizeOnDisk > 0 ? SizeText(game.SizeOnDisk) : null),
            ("Local de instalação", game.InstallPath),
        };

        GameInfoPanel.Children.Clear();
        foreach (var (label, value) in rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = label, FontSize = 16, Foreground = (Brush)FindResource("TextMuted") });
            var text = new TextBlock { Text = value, FontSize = 16, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextSoft") };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            GameInfoPanel.Children.Add(row);
        }
    }

    // ---- Buttons ----------------------------------------------------------------------------

    private void OnGameTabChecked(object sender, RoutedEventArgs e)
    {
        if (GameAboutPanel is null) return; // fires while the window is still being built
        var about = GameTabAbout.IsChecked == true;
        GameAboutPanel.Visibility = about ? Visibility.Visible : Visibility.Collapsed;
        GameInfoPanel.Visibility = about ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (_gameItem is not { } item) return;
        if (item.Game.NotInstalled)
        {
            InstallSteamGame(item);
            return;
        }
        if (_session is { } running)
        {
            if (GameSorter.Key(running.Item.Game) == GameSorter.Key(item.Game)) ResumeGame();
            else ShowToast($"Encerre {running.Item.Name} antes de abrir outro jogo.");
            return;
        }
        if (!_library.Launch(item.Game))
        {
            ShowToast($"Não foi possível iniciar {item.Name}.");
            return;
        }

        UiSound.Play("launch");
        RecordPlayed(item.Game);
        StatLast.Text = "hoje";
        _lastRefresh = DateTime.MinValue; // refresh the lists when the launcher comes back to the front
        BeginSession(item);
    }

    /// <summary>Asks the Steam client to install the game (it does the download; progress shows on the Downloads page).</summary>
    private void InstallSteamGame(GameItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(SteamDownloads.InstallUri(item.Game.Id)) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            ShowToast("Não foi possível abrir a Steam. Ela está instalada?");
            return;
        }

        _lastRefresh = DateTime.MinValue; // the lists refresh when the launcher comes back to the front
        ShowToast($"Pedi à Steam para instalar {item.Name}. Acompanhe em Downloads (a Steam pode pedir para escolher a pasta).");
    }

    private void OnStorePageClick(object sender, RoutedEventArgs e)
    {
        if (_gameItem is not { } item || item.Game.Source != SteamProvider.ProviderName) return;
        try
        {
            Process.Start(new ProcessStartInfo(SteamDownloads.StoreUri(item.Game.Id)) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            ShowToast("Não foi possível abrir a Steam. Ela está instalada?");
        }
    }

    private static void RecordPlayed(Game game)
    {
        var key = GameSorter.Key(game);
        App.Settings.PlayRecords.TryGetValue(key, out var existing);
        App.Settings.PlayRecords[key] = new PlayRecord { LastPlayed = DateTimeOffset.Now, Minutes = existing?.Minutes ?? 0 };
        SaveSettings();
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (_gameItem is not { } item) return;
        var key = GameSorter.Key(item.Game);
        if (!App.Settings.Favorites.Remove(key)) App.Settings.Favorites.Add(key);
        SaveSettings();
        UpdateFavoriteIcon(item);
        ApplyFilter(); // the Favorites tab count changes
        ShowToast(item.IsFavorite ? "Adicionado aos favoritos" : "Removido dos favoritos");
    }

    private void UpdateFavoriteIcon(GameItem item) => FavIcon.Text = item.IsFavorite ? "" : "";

    private void OnManageClick(object sender, RoutedEventArgs e) => OpenManage();

    private static void SaveSettings()
    {
        try { App.Store.Save(App.Settings); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // The choice still applies for this session.
        }
    }
}
