using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DeckLauncher.Core.Epic;

namespace DeckLauncher.App;

// Epic account: the user signs in on Epic's own web page (in the browser), copies the one-time code Epic shows and the launcher
// exchanges it for tokens (never the password). The library is then read from Epic and its not installed games show in the Epic tab.
public partial class MainWindow
{
    private DispatcherTimer? _epicWatch;
    private DateTime _epicWatchUntil;
    private bool _epicBusy;

    private void UpdateEpicAccountUi()
    {
        var connected = LibraryService.EpicConnected;
        var waiting = _epicWatch is not null;
        var count = App.Settings.EpicLibrary.Count;
        EpicStatusText.Text = waiting ? "Esperando você entrar na Epic no navegador…"
            : connected ? $"Conectada{(string.IsNullOrEmpty(App.Settings.EpicAccountName) ? "" : " como " + App.Settings.EpicAccountName)}. {count} jogos na biblioteca. Os que não estão instalados aparecem esmaecidos na aba Epic; Instalar abre o Epic Games Launcher."
            : _library.EpicProblem == "signin" ? "A sessão da Epic terminou. Entre de novo."
            : "Não conectada. Entre para ver na aba Epic os jogos que você tem e ainda não instalou. O launcher não vê nem guarda sua senha.";
        EpicHelpText.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        EpicPasteButton.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        EpicSignInButton.Visibility = connected && !waiting ? Visibility.Collapsed : Visibility.Visible;
        EpicSignInButton.Content = waiting ? "ABRIR A PÁGINA DE NOVO" : "ENTRAR NA EPIC";
        EpicSyncButton.Visibility = connected && !waiting ? Visibility.Visible : Visibility.Collapsed;
        EpicSignOutButton.Visibility = connected || waiting ? Visibility.Visible : Visibility.Collapsed;
        EpicSignOutButton.Content = waiting ? "CANCELAR" : "SAIR DA CONTA";
    }

    private async void OnEpicSignInClick(object sender, RoutedEventArgs e)
    {
        if (_epicBusy) return;
        // First choice: Epic's login page in a window of the launcher; the code is read from it automatically (like Playnite).
        var window = new EpicLoginWindow { Owner = this };
        window.ShowDialog();
        if (window.Code is { } found)
        {
            await CompleteEpicSignInAsync(found);
            return;
        }
        if (!window.Failed) return; // the user closed the window
        ShowToast("Não deu para abrir o login aqui dentro; abri no navegador.");
        OpenEpicLoginInBrowser();
    }

    private void OpenEpicLoginInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(EpicAccountClient.LoginUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowToast("Não foi possível abrir o navegador.");
            return;
        }

        // The page is Epic's own; the launcher only watches the clipboard for the one-time code the page shows.
        _epicWatchUntil = DateTime.UtcNow.AddMinutes(8);
        if (_epicWatch is null)
        {
            _epicWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _epicWatch.Tick += async (_, _) => await EpicWatchTickAsync();
            _epicWatch.Start();
        }
        UpdateEpicAccountUi();
    }

    private async Task EpicWatchTickAsync()
    {
        if (_epicBusy) return;
        if (DateTime.UtcNow > _epicWatchUntil)
        {
            StopEpicWatch();
            ShowToast("O tempo para entrar na Epic acabou. Tente de novo.");
            return;
        }
        if (EpicAccountClient.ExtractAuthCode(ReadClipboardText()) is { } code) await CompleteEpicSignInAsync(code);
    }

    private async void OnEpicPasteClick(object sender, RoutedEventArgs e)
    {
        if (EpicAccountClient.ExtractAuthCode(ReadClipboardText()) is { } code) await CompleteEpicSignInAsync(code);
        else ShowToast("Não achei o código da Epic no que você copiou. Copie o texto da página (Ctrl+A, Ctrl+C).");
    }

    private static string? ReadClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (Exception ex) when (ex is COMException or ExternalException) { return null; } // another program has the clipboard right now
    }

    private void StopEpicWatch()
    {
        _epicWatch?.Stop();
        _epicWatch = null;
        UpdateEpicAccountUi();
    }

    private async Task CompleteEpicSignInAsync(string code)
    {
        if (_epicBusy) return;
        _epicBusy = true;
        try
        {
            EpicStatusText.Text = "Entrando…";
            var client = new EpicAccountClient(LibraryService.Http);
            var tokens = await client.SignInAsync(code);
            App.Settings.EpicTokens = tokens.ToJson();
            try { App.Settings.EpicAccountName = await client.GetDisplayNameAsync(tokens); }
            catch (Exception ex) when (ex is HttpRequestException or EpicSignInException) { App.Settings.EpicAccountName = null; }
            App.Settings.EpicLibrary = new();
            App.Settings.EpicSkipped = new();
            App.Settings.EpicSyncedAt = null;
            LibraryService.SaveEpicSettings();
            StopEpicWatch();
            await SyncEpicNowAsync("Conta da Epic conectada");
        }
        catch (EpicSignInException ex)
        {
            StopEpicWatch();
            ShowToast(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            StopEpicWatch();
            ShowToast("Não consegui falar com a Epic. Veja a internet e tente de novo.");
        }
        finally
        {
            _epicBusy = false;
            UpdateEpicAccountUi();
        }
    }

    private async void OnEpicSyncClick(object sender, RoutedEventArgs e)
    {
        if (_epicBusy) return;
        _epicBusy = true;
        try { await SyncEpicNowAsync("Biblioteca da Epic atualizada"); }
        finally
        {
            _epicBusy = false;
            UpdateEpicAccountUi();
        }
    }

    private async Task SyncEpicNowAsync(string doneText)
    {
        EpicStatusText.Text = "Lendo a biblioteca da Epic… (na primeira vez pode levar um minuto)";
        await _library.SyncEpicAsync(force: true);
        UpdateEpicAccountUi();
        if (_library.EpicProblem == "signin") ShowToast("A sessão da Epic terminou. Entre de novo.");
        else if (_library.EpicProblem == "fetch") ShowToast("Não consegui ler a biblioteca da Epic agora. Tente de novo mais tarde.");
        else ShowToast($"{doneText}: {App.Settings.EpicLibrary.Count} jogos.");

        _lastRefresh = DateTime.MinValue;
        await RefreshNotInstalledAsync(_all.Select(i => i.Game).ToList());
    }

    private void OnEpicSignOutClick(object sender, RoutedEventArgs e)
    {
        if (_epicWatch is not null)
        {
            StopEpicWatch(); // cancel the sign-in that was waiting
            return;
        }
        App.Settings.EpicTokens = null;
        App.Settings.EpicAccountName = null;
        App.Settings.EpicLibrary = new();
        App.Settings.EpicSkipped = new();
        App.Settings.EpicSyncedAt = null;
        LibraryService.SaveEpicSettings();
        UpdateEpicAccountUi();
        ShowToast("Saiu da conta da Epic neste launcher.");
        _ = RefreshNotInstalledAsync(_all.Select(i => i.Game).ToList());
    }
}
