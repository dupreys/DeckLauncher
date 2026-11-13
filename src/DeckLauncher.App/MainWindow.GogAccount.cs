using System.ComponentModel;
using DeckLauncher.Core;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using DeckLauncher.Core.Gog;

namespace DeckLauncher.App;

// GOG account: the user signs in on GOG's own page (in a window of the launcher, or in the browser), GOG's success address carries a
// one-time code and the launcher exchanges it for tokens (never the password). The library is then read and the games that are not
// installed show dimmed in the GOG tab. GOG Galaxy has no install link, so Install opens Galaxy (or the store page).
public partial class MainWindow
{
    private DispatcherTimer? _gogWatch;
    private DateTime _gogWatchUntil;
    private bool _gogBusy;

    private void UpdateGogAccountUi()
    {
        var connected = LibraryService.GogConnected;
        var waiting = _gogWatch is not null;
        var count = App.Settings.GogLibrary.Count;
        GogStatusText.Text = waiting ? "Esperando você entrar na GOG no navegador…"
            : connected ? $"Conectada{(string.IsNullOrEmpty(App.Settings.GogAccountName) ? "" : " como " + App.Settings.GogAccountName)}. {count} jogos na biblioteca. Os que não estão instalados aparecem esmaecidos na aba GOG; Instalar abre o GOG Galaxy (ou a página do jogo)."
            : _library.GogProblem == "signin" ? "A sessão da GOG terminou. Entre de novo."
            : "Não conectada. Entre para ver na aba GOG os jogos que você tem e ainda não instalou. O launcher não vê nem guarda sua senha. Recurso experimental: a GOG não documenta esse acesso.";
        GogHelpText.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        GogPasteButton.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        GogSignInButton.Visibility = connected && !waiting ? Visibility.Collapsed : Visibility.Visible;
        GogSignInButton.Content = waiting ? "ABRIR A PÁGINA DE NOVO" : "ENTRAR NA GOG";
        GogSyncButton.Visibility = connected && !waiting ? Visibility.Visible : Visibility.Collapsed;
        GogSignOutButton.Visibility = connected || waiting ? Visibility.Visible : Visibility.Collapsed;
        GogSignOutButton.Content = waiting ? "CANCELAR" : "SAIR DA CONTA";
    }

    private async void OnGogSignInClick(object sender, RoutedEventArgs e)
    {
        if (_gogBusy) return;
        var window = new GogLoginWindow { Owner = this };
        window.ShowDialog();
        if (window.Code is { } found)
        {
            await CompleteGogSignInAsync(found);
            return;
        }
        if (!window.Failed) return; // the user closed the window
        ShowToast("Não deu para abrir o login aqui dentro; abri no navegador.");
        OpenGogLoginInBrowser();
    }

    private void OpenGogLoginInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GogAccountClient.LoginUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowToast("Não foi possível abrir o navegador.");
            return;
        }

        _gogWatchUntil = DateTime.UtcNow.AddMinutes(8);
        if (_gogWatch is null)
        {
            _gogWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _gogWatch.Tick += async (_, _) => await GogWatchTickAsync();
            _gogWatch.Start();
        }
        UpdateGogAccountUi();
    }

    private async Task GogWatchTickAsync()
    {
        if (_gogBusy) return;
        if (DateTime.UtcNow > _gogWatchUntil)
        {
            StopGogWatch();
            ShowToast("O tempo para entrar na GOG acabou. Tente de novo.");
            return;
        }
        if (GogAccountClient.ExtractAuthCode(ReadClipboardText()) is { } code) await CompleteGogSignInAsync(code);
    }

    private async void OnGogPasteClick(object sender, RoutedEventArgs e)
    {
        if (GogAccountClient.ExtractAuthCode(ReadClipboardText()) is { } code) await CompleteGogSignInAsync(code);
        else ShowToast("Não achei o código da GOG no que você copiou. Copie o endereço da página (Ctrl+L, Ctrl+C).");
    }

    private void StopGogWatch()
    {
        _gogWatch?.Stop();
        _gogWatch = null;
        UpdateGogAccountUi();
    }

    private async Task CompleteGogSignInAsync(string code)
    {
        if (_gogBusy) return;
        _gogBusy = true;
        try
        {
            GogStatusText.Text = "Entrando…";
            var client = new GogAccountClient(LibraryService.Http);
            var tokens = await client.SignInAsync(code);
            App.Settings.GogTokens = tokens.ToJson();
            try { App.Settings.GogAccountName = await client.GetUserNameAsync(tokens); }
            catch (Exception ex) when (ex is HttpRequestException or GogSignInException) { App.Settings.GogAccountName = null; }
            App.Settings.GogLibrary = new();
            App.Settings.GogSyncedAt = null;
            LibraryService.SaveEpicSettings();
            StopGogWatch();
            await SyncGogNowAsync("Conta da GOG conectada");
        }
        catch (GogSignInException ex)
        {
            StopGogWatch();
            ShowToast(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            StopGogWatch();
            ShowToast("Não consegui falar com a GOG. Veja a internet e tente de novo.");
        }
        finally
        {
            _gogBusy = false;
            UpdateGogAccountUi();
        }
    }

    private async void OnGogSyncClick(object sender, RoutedEventArgs e)
    {
        if (_gogBusy) return;
        _gogBusy = true;
        try { await SyncGogNowAsync("Biblioteca da GOG atualizada"); }
        finally
        {
            _gogBusy = false;
            UpdateGogAccountUi();
        }
    }

    private async Task SyncGogNowAsync(string doneText)
    {
        GogStatusText.Text = "Lendo a biblioteca da GOG…";
        await _library.SyncGogAsync(force: true);
        UpdateGogAccountUi();
        if (_library.GogProblem == "signin") ShowToast("A sessão da GOG terminou. Entre de novo.");
        else if (_library.GogProblem == "fetch") ShowToast("Não consegui ler a biblioteca da GOG agora. Tente de novo mais tarde.");
        else ShowToast($"{doneText}: {App.Settings.GogLibrary.Count} jogos.");

        _lastRefresh = DateTime.MinValue;
        ApplyFilter(); // the GOG tab appears (or goes away) with the account
        await RefreshNotInstalledAsync(_all.Select(i => i.Game).ToList());
    }

    private void OnGogSignOutClick(object sender, RoutedEventArgs e)
    {
        if (_gogWatch is not null)
        {
            StopGogWatch(); // cancel the sign-in that was waiting
            return;
        }
        App.Settings.GogTokens = null;
        App.Settings.GogAccountName = null;
        App.Settings.GogLibrary = new();
        App.Settings.GogSyncedAt = null;
        LibraryService.SaveEpicSettings();
        UpdateGogAccountUi();
        ShowToast("Saiu da conta da GOG neste launcher.");
        ApplyFilter();
        _ = RefreshNotInstalledAsync(_all.Select(i => i.Game).ToList());
    }

    // ---- GOG Galaxy -----------------------------------------------------------------------------

    /// <summary>Opens the GOG Galaxy program and brings it to the front.</summary>
    private void OpenGalaxy()
    {
        if (LibraryService.GalaxyExe is not { } exe)
        {
            ShowToast("O GOG Galaxy não foi encontrado neste PC.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(exe)! })?.Dispose();
            _ = BringStoreToFrontAsync(new[] { "GalaxyClient" }, null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowToast("Não foi possível abrir o GOG Galaxy.");
        }
    }

    /// <summary>The GOG install in progress (one at a time): shown in the bell and on the Downloads page, with pause and cancel.</summary>
    private sealed class GogInstallJob
    {
        public required string Name { get; init; }
        public string Phase { get; set; } = "Preparando";
        public long Done { get; set; }
        public long Total { get; set; }
        public GogDownloadControl Control { get; } = new();
        public CancellationTokenSource Cancel { get; } = new();
        public double Fraction => Total > 0 ? Math.Min(1.0, (double)Done / Total) : 0;
    }

    private GogInstallJob? _gogJob;

    private void UpdateGogJobUi()
    {
        var job = _gogJob;
        var visible = job is not null ? Visibility.Visible : Visibility.Collapsed;
        NotifGog.Visibility = visible;
        GogCard.Visibility = visible;
        if (job is null) return;

        var paused = job.Control.Paused;
        var state = job.Total == 0 ? job.Phase : paused ? "Pausado" : "Baixando";
        var detail = job.Total > 0 ? $"{FormatBytes(job.Done)} / {FormatBytes(job.Total)}" : "";
        NotifGogTitle.Text = $"{UiText.T(state)}: {job.Name} (GOG)";
        NotifGogBar.IsIndeterminate = job.Total == 0;
        NotifGogBar.Value = job.Fraction;
        NotifGogDetail.Text = detail;
        GogCardState.Text = UiText.T(paused ? "PAUSADO (GOG)" : "BAIXANDO (GOG)");
        GogCardTitle.Text = job.Name;
        GogCardDetail.Text = detail;
        GogCardBar.IsIndeterminate = job.Total == 0;
        GogCardBar.Value = job.Fraction;
        GogCardPercent.Text = job.Total > 0 ? $"{(int)Math.Round(job.Fraction * 100)}%" : "";
        var pauseText = UiText.T(paused ? "RETOMAR" : "PAUSAR");
        NotifGogPause.Content = pauseText;
        GogCardPause.Content = pauseText;
        NotifGogPause.IsEnabled = GogCardPause.IsEnabled = job.Total > 0;
    }

    private void OnGogPauseClick(object sender, RoutedEventArgs e)
    {
        if (_gogJob is not { } job) return;
        if (job.Control.Paused) job.Control.Resume();
        else job.Control.Pause();
        UpdateGogJobUi();
    }

    private void OnGogCancelClick(object sender, RoutedEventArgs e)
    {
        if (_gogJob is not { } job) return;
        job.Control.Resume(); // a paused download has to wake up to notice the cancel
        job.Cancel.Cancel();
        ShowToast($"Cancelando o download de {job.Name}…");
    }

    /// <summary>The folder where the launcher installs GOG games it downloads itself.</summary>
    private static string GogGamesRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "GOG");

    /// <summary>
    /// Installs a GOG game the way Heroic does (GOG Galaxy has no install command): the files come straight from GOG's CDN, checked
    /// chunk by chunk, into Games\GOG\&lt;game&gt;. If that does not work for the game (older depot format…) it downloads the offline
    /// installer instead, and as a last resort opens GOG Galaxy or the store page.
    /// </summary>
    private async void InstallGogGame(GameItem item)
    {
        if (_gogJob is not null)
        {
            ShowToast("Já há um download da GOG em andamento. Espere terminar.");
            return;
        }
        var tokens = await _library.FreshGogTokensAsync();
        if (tokens is null)
        {
            OpenGogFallback(item);
            return;
        }

        var job = _gogJob = new GogInstallJob { Name = item.Name };
        UpdateBell();
        try
        {
            if (await TryInstallGogDirectAsync(item, tokens, job)) return;
            await InstallGogOfflineAsync(item, tokens);
        }
        catch (GogSignInException ex)
        {
            ShowToast(ex.Message);
        }
        finally
        {
            _gogJob = null;
            UpdateBell();
        }
    }

    /// <summary>True when the game was handled here (installed, or reported to the user); false to try the offline installer.</summary>
    private async Task<bool> TryInstallGogDirectAsync(GameItem item, GogTokens tokens, GogInstallJob job)
    {
        var game = item.Game;
        var languages = DeckLauncher.Core.Loc.Language switch { "pt-BR" => new[] { "pt-BR", "en-US" }, "es" => new[] { "es-ES", "es-MX", "en-US" }, _ => new[] { "en-US" } };
        var downloader = new GogDownloader(LibraryService.DownloadHttp);

        GogInstallPlan plan;
        try
        {
            ShowToast($"Preparando a instalação de {item.Name}…");
            UpdateBell();
            plan = await downloader.PlanAsync(tokens, game.Id, languages);
        }
        catch (Exception ex) when (ex is GogUnsupportedException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return false;
        }

        var folder = System.IO.Path.Combine(GogGamesRoot, plan.InstallDirectory);
        try
        {
            System.IO.Directory.CreateDirectory(GogGamesRoot);
            var free = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(folder)!).AvailableFreeSpace;
            if (free < plan.DiskSize + (500L << 20))
            {
                ShowToast($"Pouco espaço em disco para instalar {item.Name} (precisa de {FormatBytes(plan.DiskSize)}).");
                return true;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException) { /* cannot tell: try anyway */ }

        job.Total = plan.DownloadSize;
        ShowToast($"Baixando {item.Name} ({FormatBytes(plan.DownloadSize)}) para a pasta Games\\GOG. Acompanhe no sino.");
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            job.Done = p.Done;
            job.Total = p.Total;
            UpdateBell();
        });

        try
        {
            await downloader.DownloadAsync(plan, game.Id, folder, () => _library.FreshGogTokensAsync(), progress, job.Control, job.Cancel.Token);
        }
        catch (OperationCanceledException)
        {
            try { System.IO.Directory.Delete(folder, true); } catch (Exception cleanup) when (cleanup is System.IO.IOException or UnauthorizedAccessException) { /* left behind */ }
            AddNotification($"Download de {item.Name} cancelado.");
            ShowToast($"Download de {item.Name} cancelado.");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.IO.IOException or UnauthorizedAccessException)
        {
            try { System.IO.Directory.Delete(folder, true); } catch (Exception cleanup) when (cleanup is System.IO.IOException or UnauthorizedAccessException) { /* left for the next try */ }
            ShowToast($"Não consegui instalar {item.Name} direto. Tentando o instalador offline.");
            return false;
        }

        var record = GogOwnInstall.FromInfo(folder, game.Id, item.Name, plan.BuildVersion);
        if (record is null)
        {
            ShowToast($"{item.Name} foi baixado, mas não achei o programa do jogo em Games\\GOG.");
            return true;
        }
        App.Settings.GogInstalls.RemoveAll(g => g.Id == game.Id);
        App.Settings.GogInstalls.Add(record);
        SaveSettings();

        _lastRefresh = DateTime.MinValue;
        await RefreshAsync(); // queued by RefreshAsync itself when another refresh is running
        while (_refreshing) await Task.Delay(100); // a refresh that was already running ends before the one that sees the new game
        await RefreshAsync();
        var key = GameSorter.Key(game);
        if (_page == "Jogo" && _gameItem is { } open && GameSorter.Key(open.Game) == key && _itemCache.TryGetValue(key, out var fresh) && !fresh.Game.NotInstalled)
            OpenGame(fresh); // the game page now shows Jogar and Desinstalar
        AddNotification($"{item.Name} instalado (GOG).");
        ShowToast($"{item.Name} instalado. Ele já está na aba GOG.");
        return true;
    }

    /// <summary>Downloads the game's offline installer (the .exe and its .bin parts) and starts it.</summary>
    private async Task InstallGogOfflineAsync(GameItem item, GogTokens tokens)
    {
        try
        {
            var wanted = DeckLauncher.Core.Loc.Language switch { "pt-BR" => new[] { "Portugu", "Brazil" }, "es" => new[] { "Espa", "Spanish" }, _ => Array.Empty<string>() };
            var files = await GogInstallers.GetInstallersAsync(LibraryService.Http, tokens, item.Game.Id, wanted);
            if (files.Count == 0)
            {
                OpenGogFallback(item);
                return;
            }

            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "GOG", GogInstallers.SafeFileName(item.Name));
            System.IO.Directory.CreateDirectory(folder);
            var total = files.Sum(f => f.Size);
            ShowToast($"Baixando o instalador de {item.Name} ({FormatBytes(total)}). Avisarei quando terminar.");

            long done = 0;
            var lastPercent = 0;
            string? installer = null;
            foreach (var file in files)
            {
                var path = System.IO.Path.Combine(folder, GogInstallers.SafeFileName(file.Name));
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) installer ??= path;
                if (System.IO.File.Exists(path) && file.Size > 0 && new System.IO.FileInfo(path).Length == file.Size)
                {
                    done += file.Size;
                    continue;
                }

                tokens = await _library.FreshGogTokensAsync() ?? throw new GogSignInException("A sessão da GOG expirou. Entre de novo.");
                var url = await GogInstallers.ResolveAsync(LibraryService.Http, tokens, file.ManualUrl);
                if (url is null) throw new HttpRequestException("no download address");

                var part = path + ".part";
                using (var response = await LibraryService.DownloadHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync();
                    await using var target = System.IO.File.Create(part);
                    var buffer = new byte[256 * 1024];
                    int read;
                    var fileDone = 0L;
                    while ((read = await source.ReadAsync(buffer)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read));
                        fileDone += read;
                        if (total > 0 && (int)((done + fileDone) * 100 / total) is var percent && percent >= lastPercent + 10)
                        {
                            lastPercent = percent / 10 * 10;
                            ShowToast($"{item.Name}: {lastPercent}% baixado.");
                        }
                    }
                }
                System.IO.File.Move(part, path, overwrite: true);
                done += file.Size;
            }

            AddNotification($"{item.Name}: instalador baixado.");
            ShowToast($"{item.Name} baixado. Abrindo o instalador…");
            if (installer is not null)
                Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, WorkingDirectory = folder })?.Dispose();
            else Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
            _lastRefresh = DateTime.MinValue; // the lists refresh when the launcher comes back to the front
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.IO.IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            ShowToast($"Não consegui baixar o instalador de {item.Name}. Abri a alternativa.");
            OpenGogFallback(item);
        }
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB" : bytes > 0 ? $"{bytes / 1024} KB" : "0 MB";

    /// <summary>No installer to download: open GOG Galaxy (install from its library) or the game's page in the store.</summary>
    private void OpenGogFallback(GameItem item)
    {
        if (LibraryService.GalaxyExe is not null)
        {
            OpenGalaxy();
            ShowToast($"Abri o GOG Galaxy. Procure {item.Name} na sua biblioteca e clique em Instalar.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(GogAccountClient.StorePageUrl(item.Game.Slug)) { UseShellExecute = true })?.Dispose();
            ShowToast($"Abri a página de {item.Name} na GOG. Instale o GOG Galaxy ou baixe o instalador de lá.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowToast("Não foi possível abrir o navegador.");
        }
    }
}
