using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DeckLauncher.Core;
using DeckLauncher.Core.Epic;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

// Uninstall button on the game page: the store's own launcher asks and removes the game; here we notice it is gone.
public partial class MainWindow
{
    private CancellationTokenSource? _uninstallCts;

    private void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (_gameItem is not { } item || item.Game.NotInstalled) return;
        var game = item.Game;
        if (_session is { } running && GameSorter.Key(running.Item.Game) == GameSorter.Key(game))
        {
            ShowToast($"Encerre {item.Name} antes de desinstalar.");
            return;
        }

        if (game.Source == SteamProvider.ProviderName)
        {
            try { Process.Start(new ProcessStartInfo(SteamDownloads.UninstallUri(game.Id)) { UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
            {
                ShowToast("Não foi possível abrir a Steam. Ela está instalada?");
                return;
            }
            ShowToast($"Confirme a desinstalação de {item.Name} na Steam. Depois dela, o jogo volta para a aba Steam.");
        }
        else if (game.Source == EpicProvider.ProviderName)
        {
            OpenEpicLibrary(item);
        }
        else return;

        WatchUninstall(game);
    }

    /// <summary>The Epic launcher has no direct uninstall link, so it opens on the library, where the game's menu has Uninstall.</summary>
    private async void OpenEpicLibrary(GameItem item)
    {
        try
        {
            if (!EpicProvider.IsLauncherRunning() && EpicProvider.FindLauncherExe() is { } exe)
            {
                ShowToast("Abrindo o Epic Games Launcher…");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(12));
            }
            Process.Start(new ProcessStartInfo(EpicAccountClient.LibraryPageUri) { UseShellExecute = true })?.Dispose();
            ShowToast($"No Epic Games Launcher, abra o menu (⋯) de {item.Name} na Biblioteca e escolha Desinstalar. Depois, o jogo volta para a aba Epic.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowToast("Não foi possível abrir o Epic Games Launcher. Ele está instalado?");
        }
    }

    /// <summary>Waits (up to 15 minutes) for the game to disappear from the installed games, then updates the lists and goes back to its store tab.</summary>
    private async void WatchUninstall(Game game)
    {
        _uninstallCts?.Cancel();
        var cts = _uninstallCts = new CancellationTokenSource();
        var key = GameSorter.Key(game);
        var tab = game.Source == SteamProvider.ProviderName ? "steam" : "epic";
        try
        {
            for (var tick = 1; tick <= 180; tick++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

                // The app also rescans when it comes back to the front; between those, look again when the folder is gone and every 30 s.
                var folderGone = !string.IsNullOrEmpty(game.InstallPath) && !Directory.Exists(game.InstallPath);
                if (folderGone || tick % 6 == 0)
                {
                    while (_refreshing) await Task.Delay(100, cts.Token);
                    await RefreshAsync();
                }
                if (_all.Any(i => GameSorter.Key(i.Game) == key && !i.Game.NotInstalled)) continue;

                var title = tab == "steam" ? "Steam" : "Epic";
                var message = $"{game.Name} desinstalado. Ele continua na aba {title} para instalar de novo.";
                ShowToast(message);
                AddNotification(message);
                if (_page == "Jogo" && _gameItem is { } open && GameSorter.Key(open.Game) == key)
                {
                    _detailsCts?.Cancel();
                    ShowPage("Biblioteca");
                    if (_tabs.TryGetValue(tab, out var radio)) radio.IsChecked = true;
                    FocusPage();
                }
                return;
            }
        }
        catch (OperationCanceledException) { /* a newer uninstall took over */ }
    }
}
