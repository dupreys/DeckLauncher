using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeckLauncher.Core;
using DeckLauncher.Core.Manual;
using DeckLauncher.Core.Settings;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

// The system pages: Downloads (Steam), Settings and the Power menu.
public partial class MainWindow
{
    // ======================================================================================
    // Downloads
    // ======================================================================================

    private readonly DispatcherTimer _downloadsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly NetworkMeter _network = new();
    private readonly Dictionary<string, DownloadTrack> _tracks = new();
    private readonly Dictionary<string, DownloadRowUi> _downloadRows = new();
    private IReadOnlyList<DownloadInfo> _manifestDownloads = Array.Empty<DownloadInfo>();
    private IReadOnlyList<DownloadInfo> _downloads = Array.Empty<DownloadInfo>();
    private Dictionary<string, string> _activeDownloadNames = new();
    private string _downloadRowsSignature = "";
    private bool _downloadsWired;
    private bool _polling;
    private int _downloadTick;

    /// <summary>What we know about one download between the manifest readings.</summary>
    private sealed class DownloadTrack
    {
        public long Estimate;
        public long LastManifest;
        public long Total;
        public double Speed;
    }

    /// <summary>The controls of one download card, kept so the numbers can change without rebuilding the page.</summary>
    private sealed class DownloadRowUi
    {
        public readonly TextBlock Status = new();
        public readonly TextBlock Percent = new();
        public readonly ColumnDefinition Done = new();
        public readonly ColumnDefinition Rest = new();
    }

    private void StartDownloads()
    {
        if (!_downloadsWired)
        {
            _downloadsTimer.Tick += async (_, _) => await PollDownloadsAsync();
            _downloadsWired = true;
        }
        _downloadTick = 0;
        _downloadsTimer.Start();
        _ = PollDownloadsAsync();
    }

    private void StopDownloads()
    {
        _downloadsTimer.Stop();
        _network.Reset();
    }

    /// <summary>
    /// Every second: the network speed is read live, and the progress moves with it. Steam rewrites its manifest only now
    /// and then (every few seconds, sometimes minutes), so each fresh manifest reading corrects the running estimate.
    /// </summary>
    private async Task PollDownloadsAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var steamPath = _library.SteamPath;
            var readManifests = _downloadTick++ % 3 == 0; // the files change slowly: every third second is plenty
            var wantNetwork = _manifestDownloads.Any(d => d.Status == DownloadStatus.Downloading);
            var (read, received) = await Task.Run(() =>
            {
                var manifests = readManifests && steamPath is not null ? SteamDownloads.Read(steamPath).ToList() : (readManifests ? new List<DownloadInfo>() : null);
                var bytes = wantNetwork ? _network.Sample() : _network.Reset();
                return (manifests, bytes);
            });

            if (read is not null)
            {
                // Something that was downloading and is no longer listed has finished (or was cancelled).
                var vanished = _activeDownloadNames.Where(p => read.All(d => d.AppId != p.Key)).ToList();
                _activeDownloadNames = read.ToDictionary(d => d.AppId, d => d.Name);
                _manifestDownloads = read;
                foreach (var gone in _tracks.Keys.Where(k => read.All(d => d.AppId != k)).ToList()) _tracks.Remove(gone);
                if (vanished.Count > 0 && steamPath is not null)
                {
                    var installed = await Task.Run(() => SteamLibraryReader.ReadInstalledGames(steamPath).Select(g => g.Id).ToHashSet());
                    foreach (var (id, name) in vanished)
                        if (installed.Contains(id)) ShowToast($"{name} terminou de baixar");
                    _ = RefreshAsync();
                }
            }

            var current = _manifestDownloads;
            var active = Math.Max(1, current.Count(d => d.Status == DownloadStatus.Downloading && d.BytesTotal > 0));
            var list = new List<DownloadInfo>(current.Count);
            foreach (var d in current)
            {
                if (!_tracks.TryGetValue(d.AppId, out var track))
                    _tracks[d.AppId] = track = new DownloadTrack { Estimate = d.BytesDone, LastManifest = d.BytesDone, Total = d.BytesTotal };

                if (d.BytesTotal != track.Total || d.BytesDone + (1 << 20) < track.LastManifest)
                {
                    // A new download of the same game (or a restart): begin again from what the manifest says.
                    track.Estimate = d.BytesDone;
                    track.LastManifest = d.BytesDone;
                    track.Total = d.BytesTotal;
                }
                else if (d.BytesDone != track.LastManifest)
                {
                    track.LastManifest = d.BytesDone;
                    track.Estimate = Math.Max(track.Estimate, d.BytesDone); // a fresh reading never moves the bar backwards
                }

                if (d.Status == DownloadStatus.Downloading && d.BytesTotal > 0)
                {
                    track.Estimate += received / active;
                    track.Speed = _network.BytesPerSecond / active;
                }
                else
                {
                    track.Speed = 0;
                }

                var cap = d.BytesTotal > 0 ? (long)(d.BytesTotal * 0.995) : track.Estimate;
                track.Estimate = Math.Min(track.Estimate, Math.Max(cap, d.BytesDone));
                list.Add(d with { BytesDone = Math.Max(track.Estimate, d.BytesDone) });
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
        var signature = string.Join('|', _downloads.Select(d => $"{d.AppId}:{d.Status}:{d.IsUpdate}:{d.BytesTotal == 0}"));
        if (signature != _downloadRowsSignature)
        {
            _downloadRowsSignature = signature;
            RebuildDownloadCards();
        }
        UpdateDownloadValues();
    }

    /// <summary>Creates the cards (only when a download starts, ends or changes kind).</summary>
    private void RebuildDownloadCards()
    {
        var hadFocus = IsFocusInside(DownloadsList);
        DownloadsList.Children.Clear();
        _downloadRows.Clear();
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
            var ui = new DownloadRowUi();
            ui.Status.FontSize = 14;
            ui.Status.Foreground = (Brush)FindResource("TextMuted");
            ui.Status.Margin = new Thickness(0, 4, 0, 0);
            ui.Percent.FontSize = 30;
            ui.Percent.FontWeight = FontWeights.Light;
            ui.Percent.VerticalAlignment = VerticalAlignment.Center;
            ui.Percent.HorizontalAlignment = HorizontalAlignment.Right;

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = d.IsUpdate ? "ATUALIZANDO" : "INSTALANDO", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Accent") });
            text.Children.Add(new TextBlock { Text = d.Name, FontSize = 22, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
            text.Children.Add(ui.Status);

            var bar = new Grid { Height = 6, Margin = new Thickness(0, 14, 0, 0) };
            bar.ColumnDefinitions.Add(ui.Done);
            bar.ColumnDefinitions.Add(ui.Rest);
            var track = new Border { Background = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)) };
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(new Border { Background = (Brush)FindResource("Accent") });

            var top = new Grid();
            top.Children.Add(text);
            top.Children.Add(ui.Percent);

            var card = new StackPanel();
            card.Children.Add(top);
            card.Children.Add(bar);
            DownloadsList.Children.Add(new Border { Style = (Style)FindResource("SettingsRow"), Padding = new Thickness(28, 22, 28, 24), Child = card });
            _downloadRows[d.AppId] = ui;
        }

        if (hadFocus) OpenSteamDownloadsButton.Focus(); // the cards are not focusable; keep the arrows alive
    }

    /// <summary>Refreshes the numbers and the bar of each card.</summary>
    private void UpdateDownloadValues()
    {
        foreach (var d in _downloads)
        {
            if (!_downloadRows.TryGetValue(d.AppId, out var ui)) continue;
            var speed = _tracks.TryGetValue(d.AppId, out var track) ? track.Speed : 0;

            ui.Status.Text = d.Status switch
            {
                DownloadStatus.Paused => $"Pausado · {SteamDownloads.FormatBytes(d.BytesDone)} de {SteamDownloads.FormatBytes(d.BytesTotal)}",
                _ when d.BytesTotal == 0 => "Preparando…",
                _ => $"{SteamDownloads.FormatBytes(d.BytesDone)} de {SteamDownloads.FormatBytes(d.BytesTotal)}"
                     + (speed > 1024 ? $"  ·  {(speed * 8 / 1_000_000).ToString("0.0", Portuguese)} Mb/s" : "")
                     + (speed > 1024 && d.BytesTotal > d.BytesDone ? $"  ·  {FormatRemaining((d.BytesTotal - d.BytesDone) / speed)} restantes" : ""),
            };
            ui.Percent.Text = d.BytesTotal == 0 ? "" : $"{(int)(d.Fraction * 100)}%";
            ui.Done.Width = new GridLength(Math.Max(d.Fraction, 0.0001), GridUnitType.Star);
            ui.Rest.Width = new GridLength(Math.Max(1 - d.Fraction, 0.0001), GridUnitType.Star);
        }
    }

    private static string FormatRemaining(double seconds)
    {
        if (seconds >= 3600 * 99) return "muito tempo";
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} h {span.Minutes:00} min";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes} min";
        return "menos de 1 min";
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
        OledSwitch.IsChecked = App.Settings.OledBlack;
        ShowHomeStyleChecks();
        ShowAppearanceChecks();
        ShowAccountChecks();
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

    // ---- Lojas: open the Steam or Epic store ------------------------------------------------------

    private const string SteamStoreUrl = "steam://openurl/https://store.steampowered.com/";
    private const string EpicStoreUrl = "com.epicgames.launcher://store";

    private void OnStoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string store }) return;
        var epic = store == "epic";
        try
        {
            if (epic) OpenStore("com.epicgames.launcher", EpicStoreUrl);
            else OpenStore("steam", SteamStoreUrl);
            _ = BringStoreToFrontAsync(epic ? new[] { "EpicGamesLauncher" } : new[] { "steam", "steamwebhelper" }, epic ? "com.epicgames.launcher" : null);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            ShowToast(epic ? "Não foi possível abrir o Epic Games Launcher. Ele está instalado?" : "Não foi possível abrir a Steam. Ela está instalada?");
        }
    }

    /// <summary>
    /// The store opens behind the full-screen launcher unless it is brought forward: wait for its window and raise it.
    /// If the program never started (the link did nothing), start it directly.
    /// </summary>
    private static async Task BringStoreToFrontAsync(string[] processNames, string? startIfMissing)
    {
        var started = false;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            await Task.Delay(500);
            var pids = new List<int>();
            foreach (var name in processNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    pids.Add(process.Id);
                    process.Dispose();
                }
            }

            if (pids.Count == 0)
            {
                if (startIfMissing is not null && !started && attempt >= 8 && ProtocolCommand(startIfMissing) is { } command)
                {
                    started = true;
                    try { Process.Start(new ProcessStartInfo(command.Exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(command.Exe) ?? "" }); }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return; }
                }
                continue;
            }

            var window = Native.FindMainWindow(pids);
            if (window != IntPtr.Zero)
            {
                Native.BringToFront(window);
                return;
            }
        }
    }

    /// <summary>Runs the store program the way Windows does for its link: the command registered for the protocol, with the link filled in.</summary>
    private static void OpenStore(string scheme, string url)
    {
        if (ProtocolCommand(scheme) is { } command)
        {
            var args = command.Args.Contains("%1") ? command.Args.Replace("%1", url) : $"{command.Args} \"{url}\"".Trim();
            Process.Start(new ProcessStartInfo(command.Exe, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(command.Exe) ?? "" });
            return;
        }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>The program (and its arguments) Windows has registered for a link such as steam://, or null when it is not installed.</summary>
    private static (string Exe, string Args)? ProtocolCommand(string scheme)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{scheme}\shell\open\command");
            if (key?.GetValue("") is not string command || command.Trim().Length == 0) return null;
            command = command.Trim();
            string exe, rest;
            if (command.StartsWith('"'))
            {
                var end = command.IndexOf('"', 1);
                if (end < 0) return null;
                exe = command[1..end];
                rest = command[(end + 1)..].Trim();
            }
            else
            {
                var space = command.IndexOf(' ');
                exe = space < 0 ? command : command[..space];
                rest = space < 0 ? "" : command[(space + 1)..].Trim();
            }
            return File.Exists(exe) ? (exe, rest) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- Settings: the left list switches section as you move, Right / A enters the section ----------

    private static bool IsSettingsNav(object? element) => element is RadioButton { GroupName: "SettingsNav" };

    /// <summary>Moving onto a section in the left list shows it at once.</summary>
    private void OnSettingsNavFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: not true } radio) radio.IsChecked = true;
    }

    private bool InSettingsContent(DependencyObject node) =>
        IsAncestor(SetSystem, node) || IsAncestor(SetInterface, node) || IsAncestor(SetIntegrations, node) || IsAncestor(SetGames, node);

    /// <summary>First thing to focus in the section being shown (null when it has nothing to select).</summary>
    private UIElement? FirstSettingsControl()
    {
        var section = SetSystem.IsVisible ? SetSystem : SetInterface.IsVisible ? SetInterface : SetIntegrations.IsVisible ? SetIntegrations : SetGames.IsVisible ? SetGames : null;
        if (section is null) return null;
        return FindAll<Control>(section).FirstOrDefault(c => c.Focusable && c.IsTabStop && c.IsEnabled && c.IsVisible);
    }

    private void FocusSettingsNav() =>
        (FindAll<RadioButton>(SettingsPage).FirstOrDefault(r => IsSettingsNav(r) && r.IsChecked == true) ?? NavSystem).Focus();

    /// <summary>Right from the left list enters the section; Left from the section's first column goes back to the list. True when handled.</summary>
    private bool TrySettingsMove(FocusNavigationDirection direction)
    {
        if (_page != "Configurações" || Keyboard.FocusedElement is not DependencyObject focus) return false;

        if (IsSettingsNav(focus) && direction == FocusNavigationDirection.Right)
        {
            if (FirstSettingsControl() is { } first) first.Focus();
            return true; // nothing to enter: stay on the list
        }

        if (InSettingsContent(focus) && direction == FocusNavigationDirection.Left)
        {
            // Another control to the left in the same row? Then let the normal movement take it.
            if (focus is UIElement element && element.PredictFocus(FocusNavigationDirection.Left) is DependencyObject neighbour && InSettingsContent(neighbour))
                return false;
            FocusSettingsNav();
            return true;
        }
        return false;
    }

    private void OnSettingsNavChecked(object sender, RoutedEventArgs e)
    {
        if (SetSystem is null || sender is not RadioButton { Tag: string tag }) return; // fires while the window is being built
        SetSystem.Visibility = tag == "System" ? Visibility.Visible : Visibility.Collapsed;
        SetInterface.Visibility = tag == "Interface" ? Visibility.Visible : Visibility.Collapsed;
        SetIntegrations.Visibility = tag == "Integrations" ? Visibility.Visible : Visibility.Collapsed;
        SetGames.Visibility = tag == "Games" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "Integrations") UpdateEpicAccountUi();
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
