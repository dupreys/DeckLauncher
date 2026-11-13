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

    private async void OnUninstallClick(object sender, RoutedEventArgs e)
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
            if (!await AskConfirmAsync($"Desinstalar {item.Name}?",
                    "A Steam vai abrir a janela dela para confirmar e remover o jogo.",
                    "Desinstalar", "Cancelar",
                    "A janela da Steam não aceita o controle: confirme com o mouse ou com Enter no teclado.")) return;
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
        else if (game.Source == DeckLauncher.Core.Gog.GogProvider.ProviderName)
        {
            if (App.Settings.GogInstalls.FirstOrDefault(g => g.Id == game.Id) is { } own)
            {
                if (!await UninstallOwnGogGameAsync(item, own)) return;
                WatchUninstall(game);
                return;
            }
            if (DeckLauncher.Core.Gog.GogProvider.FindUninstaller(game) is not { } uninstaller)
            {
                ShowToast($"Não encontrei o desinstalador de {item.Name}. Desinstale pelo Windows (Configurações > Aplicativos).");
                return;
            }
            if (!await AskConfirmAsync($"Desinstalar {item.Name}?",
                    "O desinstalador do jogo vai abrir em uma janela própria.",
                    "Desinstalar", "Cancelar",
                    "Essa janela não aceita o controle: use o mouse ou o teclado.")) return;
            try { Process.Start(new ProcessStartInfo(uninstaller) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(uninstaller)! })?.Dispose(); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                ShowToast($"Não foi possível abrir o desinstalador de {item.Name}.");
                return;
            }
            ShowToast($"Confirme a desinstalação de {item.Name} na janela que abriu.");
        }
        else return;

        WatchUninstall(game);
    }

    /// <summary>A GOG game the launcher installed itself has no uninstaller: its folder (inside Games\\GOG) is removed after a confirmation.</summary>
    private async Task<bool> UninstallOwnGogGameAsync(GameItem item, DeckLauncher.Core.Gog.GogOwnInstall own)
    {
        var root = System.IO.Path.GetFullPath(GogGamesRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var folder = System.IO.Path.GetFullPath(own.Folder);
        if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            ShowToast($"Não encontrei o desinstalador de {item.Name}. Apague a pasta do jogo à mão.");
            return false;
        }
        if (!await AskConfirmAsync($"Desinstalar {item.Name}?",
                "A pasta do jogo e os saves dentro dela serão apagados do disco.",
                "Desinstalar", "Cancelar")) return false;
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast($"Não foi possível apagar a pasta de {item.Name}. Feche o jogo e tente de novo.");
            return false;
        }
        App.Settings.GogInstalls.RemoveAll(g => g.Id == own.Id);
        SaveSettings();
        return true;
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
        var gogKeeps = LibraryService.GogConnected; // with a GOG account the game stays in the GOG tab, to install again
        var tab = game.Source == SteamProvider.ProviderName ? "steam" : game.Source == EpicProvider.ProviderName ? "epic" : gogKeeps ? "gog" : "gogless";
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

                var title = tab == "steam" ? "Steam" : tab == "gog" ? "GOG" : "Epic";
                var message = tab == "gogless" ? $"{game.Name} desinstalado." : $"{game.Name} desinstalado. Ele continua na aba {title} para instalar de novo.";
                ShowToast(message);
                AddNotification(message);
                if (_page == "Jogo" && _gameItem is { } open && GameSorter.Key(open.Game) == key)
                {
                    _detailsCts?.Cancel();
                    ShowPage("Biblioteca");
                    if (_tabs.TryGetValue(tab == "gogless" ? "all" : tab, out var radio)) radio.IsChecked = true;
                    FocusPage();
                }
                return;
            }
        }
        catch (OperationCanceledException) { /* a newer uninstall took over */ }
    }
}
