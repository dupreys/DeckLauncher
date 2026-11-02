using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LiteLauncher.Core;
using LiteLauncher.Core.Manual;
using LiteLauncher.Core.Settings;
using LiteLauncher.Core.Steam;

namespace DeckLauncher.App;

// The system pages: Downloads (Steam), Settings and the Power menu.
public partial class MainWindow
{
    // ======================================================================================
    // Downloads
    // ======================================================================================

    private readonly DispatcherTimer _downloadsTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<string, (long Bytes, DateTime At, double Speed)> _downloadSpeed = new();
    private IReadOnlyList<DownloadInfo> _downloads = Array.Empty<DownloadInfo>();
    private Dictionary<string, string> _activeDownloadNames = new();
    private bool _downloadsWired;
    private bool _polling;

    private void StartDownloads()
    {
        if (!_downloadsWired)
        {
            _downloadsTimer.Tick += async (_, _) => await PollDownloadsAsync();
            _downloadsWired = true;
        }
        _downloadsTimer.Start();
        _ = PollDownloadsAsync();
    }

    private void StopDownloads() => _downloadsTimer.Stop();

    private async Task PollDownloadsAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var steamPath = _library.SteamPath;
            var read = steamPath is null ? new List<DownloadInfo>() : (await Task.Run(() => SteamDownloads.Read(steamPath))).ToList();
            var now = DateTime.UtcNow;
            var list = new List<DownloadInfo>(read);

            // Steam rewrites the manifest only every so often, so between rewrites the progress is estimated from the speed
            // measured at the last two real readings (never past the total).
            for (var k = 0; k < list.Count; k++)
            {
                var d = list[k];
                if (d.Status != DownloadStatus.Downloading || d.BytesTotal <= 0) continue;
                var rate = 0.0;
                var last = d.BytesDone;
                var changedAt = now;
                if (_downloadSpeed.TryGetValue(d.AppId, out var t))
                {
                    rate = t.Speed; last = t.Bytes; changedAt = t.At;
                    if (d.BytesDone > t.Bytes)
                    {
                        var seconds = Math.Max(1, (now - t.At).TotalSeconds);
                        var measured = (d.BytesDone - t.Bytes) / seconds;
                        rate = rate > 0 ? (rate * 0.4) + (measured * 0.6) : measured;
                        last = d.BytesDone;
                        changedAt = now;
                    }
                }
                _downloadSpeed[d.AppId] = (last, changedAt, rate);
                var estimate = last + (long)(rate * Math.Min((now - changedAt).TotalSeconds, 45));
                list[k] = d with { BytesDone = Math.Min(Math.Max(estimate, d.BytesDone), (long)(d.BytesTotal * 0.995)) };
            }
            foreach (var gone in _downloadSpeed.Keys.Where(k => read.All(d => d.AppId != k)).ToList()) _downloadSpeed.Remove(gone);

            // Something that was downloading and is no longer listed has finished (or was cancelled).
            var vanished = _activeDownloadNames.Where(p => read.All(d => d.AppId != p.Key)).ToList();
            _activeDownloadNames = read.ToDictionary(d => d.AppId, d => d.Name);
            if (vanished.Count > 0 && steamPath is not null)
            {
                var installed = await Task.Run(() => SteamLibraryReader.ReadInstalledGames(steamPath).Select(g => g.Id).ToHashSet());
                foreach (var (id, name) in vanished)
                    if (installed.Contains(id)) ShowToast($"{name} terminou de baixar");
                _ = RefreshAsync();
            }

            _downloads = list;
            BuildDownloadRows();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Steam is rewriting its files; the next poll tries again.
        }
        finally
        {
            _polling = false;
        }
    }

    private void BuildDownloadRows()
    {
        var hadFocus = IsFocusInside(DownloadsList);
        DownloadsList.Children.Clear();
        if (_downloads.Count == 0)
        {
            DownloadsList.Children.Add(new TextBlock
            {
                Text = _library.SteamPath is null ? "A Steam não foi encontrada neste PC." : "Não há downloads na fila.",
                FontSize = 18,
                Foreground = (Brush)FindResource("TextMuted"),
            });
            return;
        }

        foreach (var d in _downloads)
        {
            var speed = _downloadSpeed.TryGetValue(d.AppId, out var s) ? s.Speed : 0;
            var status = d.Status switch
            {
                DownloadStatus.Paused => $"Pausado · {SteamDownloads.FormatBytes(d.BytesDone)} de {SteamDownloads.FormatBytes(d.BytesTotal)}",
                _ when d.BytesTotal == 0 => "Preparando…",
                _ => $"{SteamDownloads.FormatBytes(d.BytesDone)} de {SteamDownloads.FormatBytes(d.BytesTotal)}"
                     + (speed > 1 ? $"  ·  {SteamDownloads.FormatBytes((long)speed)}/s" : ""),
            };

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = d.IsUpdate ? "ATUALIZANDO" : "INSTALANDO", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Accent") });
            text.Children.Add(new TextBlock { Text = d.Name, FontSize = 22, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
            text.Children.Add(new TextBlock { Text = status, FontSize = 14, Foreground = (Brush)FindResource("TextMuted"), Margin = new Thickness(0, 4, 0, 0) });

            var percent = new TextBlock
            {
                Text = d.BytesTotal == 0 ? "" : $"{(int)(d.Fraction * 100)}%",
                FontSize = 30,
                FontWeight = FontWeights.Light,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var bar = new Grid { Height = 6, Margin = new Thickness(0, 14, 0, 0) };
            var done = Math.Max(d.Fraction, 0.0001);
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(done, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - done, 0.0001), GridUnitType.Star) });
            var track = new Border { Background = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)) };
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(new Border { Background = (Brush)FindResource("Accent") });

            var top = new Grid();
            top.Children.Add(text);
            top.Children.Add(percent);
            percent.HorizontalAlignment = HorizontalAlignment.Right;

            var card = new StackPanel();
            card.Children.Add(top);
            card.Children.Add(bar);
            DownloadsList.Children.Add(new Border { Style = (Style)FindResource("SettingsRow"), Padding = new Thickness(28, 22, 28, 24), Child = card });
        }

        if (hadFocus) OpenSteamDownloadsButton.Focus(); // the rows are not focusable; keep the arrows alive
    }

    private void OnOpenSteamDownloadsClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("steam://nav/downloads") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { ShowToast("Não foi possível abrir a Steam."); }
    }

    // ======================================================================================
    // Settings
    // ======================================================================================

    private readonly DispatcherTimer _keySaveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _loadingSettings;
    private bool _keyTimerWired;

    private void LoadSettingsUi()
    {
        _loadingSettings = true;
        FullscreenSwitch.IsChecked = App.Settings.StartFullscreen;
        SoundSwitch.IsChecked = App.Settings.UiSounds;
        MouseSwitch.IsChecked = App.Settings.MouseEnabled;
        AutostartSwitch.IsChecked = Autostart.IsEnabled();
        SettingsKeyBox.Text = App.Settings.SteamGridDbApiKey ?? "";
        SteamKeyBox.Text = App.Settings.SteamWebApiKey ?? "";
        _loadingSettings = false;

        UpdateCacheSize();
        BuildAbout();
        BuildManualList();
        BuildHiddenList();
        if (!_keyTimerWired)
        {
            _keySaveTimer.Tick += (_, _) => { _keySaveTimer.Stop(); SaveSettings(); };
            _keyTimerWired = true;
        }
    }

    private void OnSettingsNavChecked(object sender, RoutedEventArgs e)
    {
        if (SetSystem is null || sender is not RadioButton { Tag: string tag }) return; // fires while the window is being built
        SetSystem.Visibility = tag == "System" ? Visibility.Visible : Visibility.Collapsed;
        SetIntegrations.Visibility = tag == "Integrations" ? Visibility.Visible : Visibility.Collapsed;
        SetGames.Visibility = tag == "Games" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyFullscreen(bool fullscreen)
    {
        WindowState = WindowState.Normal; // changing the border while maximized leaves the window mis-sized
        WindowStyle = fullscreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        WindowState = WindowState.Maximized;
    }

    private void OnAutostartSwitchClick(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var wanted = AutostartSwitch.IsChecked == true;
        if (!Autostart.Set(wanted))
        {
            AutostartSwitch.IsChecked = Autostart.IsEnabled();
            ShowToast("Não foi possível mudar isso (rodando por 'dotnet run'? Use o DeckLauncher.exe publicado).");
            return;
        }
        ShowToast(wanted ? "O launcher vai abrir junto com o Windows." : "O launcher não abre mais com o Windows.");
    }

    private void BuildHiddenList()
    {
        HiddenList.Children.Clear();
        if (App.Settings.HiddenGames.Count == 0)
        {
            HiddenList.Children.Add(new TextBlock { Text = "Nenhum jogo escondido.", FontSize = 16, Foreground = (Brush)FindResource("TextMuted") });
            return;
        }

        foreach (var (key, name) in App.Settings.HiddenGames.OrderBy(p => p.Value, StringComparer.CurrentCultureIgnoreCase).ToList())
        {
            var show = new Button { Style = (Style)FindResource("TabText"), Content = "MOSTRAR", HorizontalAlignment = HorizontalAlignment.Right, Tag = key };
            show.Click += OnShowHiddenClick;
            var row = new Grid();
            row.Children.Add(new TextBlock { Text = name, FontSize = 19, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 150, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(show);
            HiddenList.Children.Add(new Border { Style = (Style)FindResource("SettingsRow"), Child = row });
        }
    }

    private async void OnShowHiddenClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        App.Settings.HiddenGames.Remove(key);
        SaveSettings();
        BuildHiddenList();
        ShowToast("Jogo de volta nas listas.");
        while (_refreshing) await Task.Delay(100);
        await RefreshAsync();
    }

    private void OnSoundSwitchClick(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Settings.UiSounds = SoundSwitch.IsChecked == true;
        SaveSettings();
        UiSound.Play("select");
    }

    private void OnMouseSwitchClick(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Settings.MouseEnabled = MouseSwitch.IsChecked == true;
        SaveSettings();
        ApplyMouseMode();
    }

    /// <summary>Controller-only by default: no cursor over the launcher and no reaction to clicks or hovering.</summary>
    private void ApplyMouseMode()
    {
        var enabled = App.Settings.MouseEnabled;
        Cursor = enabled ? null : System.Windows.Input.Cursors.None;
        ForceCursor = !enabled; // also overrides the "hand" cursor buttons ask for
        RootGrid.IsHitTestVisible = enabled;
    }

    private void OnFullscreenSwitchClick(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Settings.StartFullscreen = FullscreenSwitch.IsChecked == true;
        SaveSettings();
        ApplyFullscreen(App.Settings.StartFullscreen);
    }

    private void OnRefreshNowClick(object sender, RoutedEventArgs e)
    {
        _ = RefreshAsync();
        ShowToast("Atualizando a biblioteca…");
    }

    private void OnSettingsKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingSettings) return;
        var key = SettingsKeyBox.Text.Trim();
        App.Settings.SteamGridDbApiKey = key.Length == 0 ? null : key;
        _gridGameIds.Clear();
        _keySaveTimer.Stop();
        _keySaveTimer.Start(); // saved shortly after typing stops
    }

    private static long FolderBytes(string folder)
    {
        try { return Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private void UpdateCacheSize() => CacheSizeText.Text = SteamDownloads.FormatBytes(FolderBytes(LibraryService.ArtCacheDir));

    private void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(LibraryService.ArtCacheDir)) Directory.Delete(LibraryService.ArtCacheDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Não foi possível apagar tudo (algum arquivo está em uso).");
        }

        _library.ResetArtAttempts();
        UpdateCacheSize();
        ShowToast("Arte baixada apagada. Ela será baixada de novo conforme necessário.");
        _ = RefreshAsync();
    }

    private void BuildAbout()
    {
        AboutRows.Children.Clear();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AddAboutRow("Versão", version is null ? "—" : $"{version.Major}.{version.Minor}.{version.Build}");
        AddAboutRow("Pasta de dados", SettingsStore.DefaultDirectory);
        AddAboutRow("Steam", _library.SteamPath ?? "não encontrada");
        AddAboutRow("Epic Games", _library.EpicFound ? "encontrada" : "não encontrada");
    }

    private void AddAboutRow(string label, string value)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 17 });
        var text = new TextBlock { Text = value, FontSize = 16, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextMuted") };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        AboutRows.Children.Add(new Border { Style = (Style)FindResource("SettingsRow"), Padding = new Thickness(24, 14, 24, 14), Child = row });
    }

    // ---- Manually added games ---------------------------------------------------------------

    private void BuildManualList()
    {
        ManualList.Children.Clear();
        if (App.Settings.ManualGames.Count == 0)
        {
            ManualList.Children.Add(new TextBlock { Text = "Nenhum jogo adicionado ainda.", FontSize = 16, Foreground = (Brush)FindResource("TextMuted") });
            return;
        }

        foreach (var game in App.Settings.ManualGames.ToList())
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = game.Name, FontSize = 19 });
            text.Children.Add(new TextBlock { Text = game.ExePath, FontSize = 13, Foreground = (Brush)FindResource("TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });

            var remove = new Button { Style = (Style)FindResource("TabText"), Content = "REMOVER", HorizontalAlignment = HorizontalAlignment.Right, Tag = game.Id };
            remove.Click += OnRemoveManualClick;

            var row = new Grid();
            row.Children.Add(text);
            row.Children.Add(remove);
            text.Margin = new Thickness(0, 0, 150, 0);
            ManualList.Children.Add(new Border { Style = (Style)FindResource("SettingsRow"), Child = row });
        }
    }

    private void OnAddGameClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Escolha o .exe do jogo", Filter = "Programas (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) != true) return;

        var added = ManualProvider.Add(App.Settings.ManualGames, dialog.FileName, ManualProvider.SuggestName(dialog.FileName));
        if (added is null)
        {
            ShowToast("Esse arquivo não pôde ser adicionado (já está na lista ou não é um .exe válido).");
            return;
        }

        SaveSettings();
        BuildManualList();
        _ = RefreshAsync();
        ShowToast($"{added.Name} adicionado. A arte será buscada automaticamente.");
    }

    private void OnRemoveManualClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        App.Settings.ManualGames.RemoveAll(g => g.Id == id);
        SaveSettings();
        BuildManualList();
        _ = RefreshAsync();
        ShowToast("Jogo removido da lista (o arquivo do jogo não foi apagado).");
    }

    // ======================================================================================
    // Power menu
    // ======================================================================================

    private enum PowerAction { Sleep, Restart, ShutDown, LogOff }

    private PowerAction _pendingPower;

    private bool PowerOpen => PowerPanel.Visibility == Visibility.Visible;

    private void OpenPower()
    {
        ShowPowerMain();
        PowerPanel.Visibility = Visibility.Visible;
        PowerSleepButton.Focus();
    }

    private void ClosePower()
    {
        PowerPanel.Visibility = Visibility.Collapsed;
        FocusPage();
    }

    private void ShowPowerMain()
    {
        PowerTitle.Text = "Ligar/Desligar";
        PowerMain.Visibility = Visibility.Visible;
        PowerConfirm.Visibility = Visibility.Collapsed;
    }

    /// <summary>Esc: one step back inside the confirmation, otherwise closes the menu.</summary>
    private void PowerBack()
    {
        if (PowerConfirm.Visibility == Visibility.Visible)
        {
            ShowPowerMain();
            PowerMain.Children.OfType<Button>().FirstOrDefault(b => (string?)b.Tag == _pendingPower.ToString())?.Focus();
        }
        else ClosePower();
    }

    private void OnPowerDimClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => ClosePower();

    private void OnPowerCancelClick(object sender, RoutedEventArgs e) => ClosePower();

    private void OnPowerChoiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        switch (tag)
        {
            case "Minimize":
                ClosePower();
                WindowState = WindowState.Minimized;
                return;
            case "Exit":
                Close();
                return;
            case "Sleep": // reversible and quick: no confirmation
                ClosePower();
                if (!Native.Sleep()) ShowToast("O Windows não conseguiu suspender o sistema.");
                return;
        }

        if (!Enum.TryParse<PowerAction>(tag, out var action)) return;

        // Shutting down, restarting and signing out close everything that is open, so they ask first (Cancel is pre-selected).
        _pendingPower = action;
        PowerTitle.Text = action switch { PowerAction.Restart => "Reiniciar?", PowerAction.LogOff => "Finalizar sessão?", _ => "Desligar?" };
        PowerConfirmButton.Content = action switch { PowerAction.Restart => "Reiniciar agora", PowerAction.LogOff => "Finalizar sessão agora", _ => "Desligar agora" };
        PowerMain.Visibility = Visibility.Collapsed;
        PowerConfirm.Visibility = Visibility.Visible;
        PowerCancelButton.Focus();
    }

    private void OnPowerConfirmClick(object sender, RoutedEventArgs e)
    {
        ClosePower();
        SaveSettings();
        var arguments = _pendingPower switch { PowerAction.Restart => "/r /t 0", PowerAction.LogOff => "/l", _ => "/s /t 0" };
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", arguments) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowToast("O Windows não aceitou o comando.");
        }
    }
}
