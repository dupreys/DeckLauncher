using System.IO;
using System.Windows;
using System.Windows.Threading;
using DeckLauncher.Core;
using DeckLauncher.Core.Epic;
using DeckLauncher.Core.Steam;
using System.Windows.Controls;

namespace DeckLauncher.App;

// Notification bell in the top bar: one counter for all the artwork (covers, logos, wallpapers) being downloaded, plus a short history.
public partial class MainWindow
{
    private readonly List<(DateTime When, string Text)> _notifications = new();
    private int _unreadNotifications;
    private List<DownloadInfo> _gameDownloads = new();
    private HashSet<string>? _seenGameDownloads;
    private readonly DispatcherTimer _gameDownloadTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    private void InitNotifications()
    {
        var art = _library.Art;
        // The downloads report from worker threads, many times a second: hop to the interface thread and merge the updates.
        art.Started += () => Dispatcher.BeginInvoke(() =>
        {
            if (art.Total >= 3) ShowToast("Baixando capas, logos e wallpapers… acompanhe no sino.");
            QueueBell();
        });
        art.Changed += QueueBell;
        art.Finished += () => Dispatcher.BeginInvoke(() =>
        {
            if (art.Saved > 0)
            {
                var parts = new List<string>();
                if (art.Covers > 0) parts.Add(art.Covers == 1 ? "1 capa" : $"{art.Covers} capas");
                if (art.Logos > 0) parts.Add(art.Logos == 1 ? "1 logo" : $"{art.Logos} logos");
                if (art.Wallpapers > 0) parts.Add(art.Wallpapers == 1 ? "1 wallpaper" : $"{art.Wallpapers} wallpapers");
                AddNotification("Arte baixada: " + string.Join(", ", parts) + ".");
                if (art.Total >= 3) ShowToast("Arte baixada: " + string.Join(", ", parts) + ".");
            }
            UpdateBell();
        });
        _gameDownloadTimer.Tick += async (_, _) => await PollGameDownloadsAsync();
        _gameDownloadTimer.Start();
        UpdateBell();
    }

    private readonly Dictionary<string, (string Name, DateTime Since)> _epicInstalling = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An Epic install was requested: the bell shows it until the game appears among the installed ones.</summary>
    private void TrackEpicInstall(Game game)
    {
        _epicInstalling[game.Id] = (game.Name, DateTime.UtcNow);
        UpdateBell();
        _ = PollGameDownloadsAsync();
    }

    private void NotifyEpicUpdates(List<Game> games)
    {
        if (games.Count == 0) return;
        foreach (var game in games) AddNotification($"Atualização disponível: {game.Name} (Epic). Abra o Epic Games Launcher para atualizar.");
        ShowToast(games.Count == 1 ? $"Atualização disponível: {games[0].Name} (Epic)." : $"{games.Count} jogos da Epic têm atualização. Veja no sino.");
        UpdateBell();
    }

    /// <summary>Steam downloads in progress and Epic installs waiting to finish: they count on the bell and show in its panel, like the artwork.</summary>
    private async Task PollGameDownloadsAsync()
    {
        if (!IsActive && _epicInstalling.Count == 0 && _seenGameDownloads is not null) return; // in the background (a game is open): nothing to show
        try
        {
            if (_library.SteamPath is { } steamPath)
            {
                var read = await Task.Run(() => SteamDownloads.Read(steamPath).ToList());
                var first = _seenGameDownloads is null;
                _seenGameDownloads ??= new HashSet<string>();
                foreach (var d in read)
                {
                    if (!_seenGameDownloads.Add(d.AppId) || first) continue;
                    ShowToast(d.IsUpdate ? $"Atualizando {d.Name}… acompanhe no sino." : $"Baixando {d.Name}… acompanhe no sino.");
                    if (d.IsUpdate) AddNotification($"Atualização de {d.Name} (Steam) em andamento.");
                }
                _gameDownloads = read;
            }

            if (_epicInstalling.Count > 0 && EpicProvider.DefaultManifestDirectory() is { } dir && Directory.Exists(dir))
            {
                var installed = await Task.Run(() => EpicProvider.ReadInstalledGames(dir).Select(g => g.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
                foreach (var (id, info) in _epicInstalling.ToList())
                {
                    if (installed.Contains(id))
                    {
                        _epicInstalling.Remove(id);
                        AddNotification($"{info.Name} terminou de instalar (Epic).");
                        ShowToast($"{info.Name} terminou de instalar");
                        _lastRefresh = DateTime.MinValue;
                        _ = RefreshAsync();
                    }
                    else if (DateTime.UtcNow - info.Since > TimeSpan.FromHours(12)) _epicInstalling.Remove(id); // never finished: stop waiting
                }
            }
            UpdateBell();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a store is writing its files: next time */ }
    }

    private void AddNotification(string text)
    {
        _notifications.Insert(0, (DateTime.Now, UiText.T(text)));
        if (_notifications.Count > 20) _notifications.RemoveAt(_notifications.Count - 1);
        if (NotifPanel.Visibility != Visibility.Visible) _unreadNotifications++;
    }

    /// <summary>Asks for a bell update at low priority; many calls in a row become one.</summary>
    private void QueueBell()
    {
        if (Interlocked.Exchange(ref _bellQueued0, 1) == 1) return;
        Dispatcher.BeginInvoke(() => { Volatile.Write(ref _bellQueued0, 0); UpdateBell(); }, DispatcherPriority.Background);
    }

    private int _bellQueued0;

    private void UpdateBell()
    {
        var art = _library.Art;
        var running = (art.Active ? art.Total - art.Done : 0) + _gameDownloads.Count(d => d.Status == DownloadStatus.Downloading) + _epicInstalling.Count + (_gogJob is not null ? 1 : 0);
        var number = running > 0 ? running : _unreadNotifications;
        BellBadge.Visibility = number > 0 ? Visibility.Visible : Visibility.Collapsed;
        BellBadgeText.Text = number > 99 ? "99+" : number.ToString();
        if (NotifPanel.Visibility == Visibility.Visible) FillNotifications();

        UpdateGogJobUi();

        // The same progress on the Downloads page.
        ArtCard.Visibility = art.Active ? Visibility.Visible : Visibility.Collapsed;
        if (art.Active)
        {
            var fraction = art.Total == 0 ? 0 : (double)art.Done / art.Total;
            ArtCardBar.Value = fraction;
            ArtCardPercent.Text = $"{(int)Math.Round(fraction * 100)}%";
            ArtCardDetail.Text = $"{art.Done} de {art.Total} · {art.Breakdown()}";
        }
    }

    private void OnTopBellClick(object sender, RoutedEventArgs e)
    {
        OpenQuick(BellButton, NotifPanel, NotifDetailsButton);
        if (NotifPanel.Visibility != Visibility.Visible) return;
        _unreadNotifications = 0;
        _ = PollGameDownloadsAsync();
        FillNotifications();
        UpdateBell();
    }

    private void FillNotifications()
    {
        var art = _library.Art;
        NotifActive.Visibility = art.Active ? Visibility.Visible : Visibility.Collapsed;
        if (art.Active)
        {
            NotifTitle.Text = "Baixando capas, logos e wallpapers";
            NotifBar.Value = art.Total == 0 ? 0 : (double)art.Done / art.Total;
            NotifDetail.Text = $"{art.Done} de {art.Total} · {art.Breakdown()}";
        }

        NotifGames.Children.Clear();
        NotifGames.Visibility = _gameDownloads.Count + _epicInstalling.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var d in _gameDownloads)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var state = d.Status switch { DownloadStatus.Downloading => "Baixando", DownloadStatus.Paused => "Pausado", _ => "Na fila" };
            row.Children.Add(new TextBlock { Text = $"{UiText.T(state)}: {d.Name}", FontSize = 17, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new ProgressBar { Height = 6, Margin = new Thickness(0, 8, 0, 6), Minimum = 0, Maximum = 1, Value = d.Fraction, BorderThickness = new Thickness(0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 255, 255, 255)), Foreground = (System.Windows.Media.Brush)FindResource("Accent") });
            row.Children.Add(new TextBlock { Text = $"{(int)Math.Round(d.Fraction * 100)}%", FontSize = 13, Foreground = (System.Windows.Media.Brush)FindResource("TextMuted") });
            NotifGames.Children.Add(row);
        }

        foreach (var (_, info) in _epicInstalling)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(new TextBlock { Text = $"Instalando pela Epic: {info.Name}", FontSize = 17, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new ProgressBar { Height = 6, Margin = new Thickness(0, 8, 0, 6), IsIndeterminate = true, BorderThickness = new Thickness(0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 255, 255, 255)), Foreground = (System.Windows.Media.Brush)FindResource("Accent") });
            row.Children.Add(new TextBlock { Text = "Acompanhe o andamento no Epic Games Launcher", FontSize = 13, Foreground = (System.Windows.Media.Brush)FindResource("TextMuted") });
            NotifGames.Children.Add(row);
        }

        NotifList.Children.Clear();
        foreach (var (when, text) in _notifications)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(new TextBlock { Text = text, FontSize = 15, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = when.ToString("HH:mm"), FontSize = 12, Foreground = (System.Windows.Media.Brush)FindResource("TextMuted") });
            NotifList.Children.Add(row);
        }
        NotifEmpty.Visibility = _notifications.Count == 0 && _gogJob is null && !art.Active && _gameDownloads.Count == 0 && _epicInstalling.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNotifClearClick(object sender, RoutedEventArgs e)
    {
        _notifications.Clear();
        _unreadNotifications = 0;
        CloseQuick();
        UpdateBell();
    }

    private void OnNotifDetailsClick(object sender, RoutedEventArgs e)
    {
        CloseQuick();
        ShowPage("Downloads");
        FocusPage();
    }
}
