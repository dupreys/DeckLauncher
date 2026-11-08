using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeckLauncher.Core.Apps;

namespace DeckLauncher.App;

// The "Aplicativos" tab of the library: a few Windows programs (found through the Start menu), the ones the user added by hand,
// and a tile to add more. Each tile looks like the ones on the Lojas page: icon in the middle and the name below.
public partial class MainWindow
{
    /// <summary>One tile of the Apps tab.</summary>
    private sealed class AppEntry
    {
        public string Name { get; init; } = "";
        public string? AppId { get; init; }      // Start menu app: opened through shell:AppsFolder
        public string? Path { get; init; }       // a file: opened directly (also used to read the icon)
        public string? Uri { get; init; }        // a protocol such as ms-settings:
        public string? Url { get; init; }        // a website, opened in the default browser
        public ManualApp? Manual { get; init; }  // set when the user added it
        public string Key { get; init; } = "";   // identity for the saved order: "b:Name" or "m:path-or-url"
    }

    /// <summary>Programs shown by default. Only the ones installed on this PC appear (so AMD or NVIDIA show up only when their app exists).</summary>
    private sealed record BuiltInApp(string Name, string[] Wanted, string? FallbackPath = null, string? FallbackUri = null);

    private static readonly BuiltInApp[] BuiltInApps =
    {
        new("Discord", new[] { "Discord" }),
        new("Explorador de Arquivos", new[] { "Explorador de Arquivos", "File Explorer", "Windows Explorer" },
            FallbackPath: Environment.ExpandEnvironmentVariables(@"%WINDIR%\explorer.exe")),
        new("Microsoft Edge", new[] { "Microsoft Edge" }),
        new("Microsoft Store", new[] { "Microsoft Store", "Loja da Microsoft" }, FallbackUri: "ms-windows-store:"),
        new("Configurações", new[] { "Configurações", "Settings" }, FallbackUri: "ms-settings:"),
        new("AMD Adrenalin", new[] { "AMD Software", "AMD Adrenalin", "Radeon Software" }),
        new("NVIDIA App", new[] { "NVIDIA App" }),
    };

    private IReadOnlyList<StartApp>? _startApps;
    private DateTime _startAppsAt = DateTime.MinValue;
    private bool _startAppsLoading;
    private readonly Dictionary<string, BitmapSource?> _appIcons = new(StringComparer.OrdinalIgnoreCase);
    private List<AppEntry> _appEntries = new();

    private int AppsCount => _appEntries.Count;

    private void InitApps()
    {
        AppsScroll.SizeChanged += (_, e) =>
        {
            const double cell = 444; // tile 420 + right margin 24
            AppsPanel.Width = Math.Max(1, Math.Floor((e.NewSize.Width - 40) / cell)) * cell;
        };
        Dispatcher.BeginInvoke(() => _ = LoadStartAppsAsync(), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Reads the Start menu list (a second or two, off the UI thread) and redraws the tab when it arrives.</summary>
    private async Task LoadStartAppsAsync()
    {
        if (_startAppsLoading) return;
        _startAppsLoading = true;
        try
        {
            var apps = await Task.Run(() => StartApps.ReadAsync());
            if (apps.Count > 0 || _startApps is null)
            {
                _startApps = apps;
                _startAppsAt = DateTime.UtcNow;
            }
        }
        catch (Exception)
        {
            _startApps ??= Array.Empty<StartApp>();
        }
        finally
        {
            _startAppsLoading = false;
        }

        if (_filter == "apps") ApplyFilter();
        else RefreshAppsCount();
    }

    private List<AppEntry> BuildEntries()
    {
        var list = new List<AppEntry>();
        var hidden = App.Settings.HiddenApps;
        foreach (var def in BuiltInApps)
        {
            if (hidden.Contains(def.Name, StringComparer.OrdinalIgnoreCase)) continue;
            var key = "b:" + def.Name;
            var found = _startApps is null ? null : StartApps.Filter(_startApps, def.Wanted).FirstOrDefault();
            if (found is not null) list.Add(new AppEntry { Key = key, Name = def.Name, AppId = found.AppId });
            else if (def.FallbackPath is not null && File.Exists(def.FallbackPath)) list.Add(new AppEntry { Key = key, Name = def.Name, Path = def.FallbackPath });
            else if (def.FallbackUri is not null) list.Add(new AppEntry { Key = key, Name = def.Name, Uri = def.FallbackUri });
        }

        foreach (var manual in App.Settings.ManualApps)
        {
            list.Add(new AppEntry
            {
                Key = "m:" + manual.Key, Name = manual.Name, Manual = manual,
                Path = manual.IsWeb ? null : manual.Path, Url = manual.IsWeb ? manual.Url : null,
            });
        }

        var order = AppList.Order(list.Select(e => e.Key), App.Settings.AppsOrder);
        return order.Select(k => list.First(e => e.Key == k)).ToList();
    }

    /// <summary>Built-in apps the user hid that are still hidden (for the "show hidden" tile).</summary>
    private int HiddenAppsCount => App.Settings.HiddenApps.Count;

    private void RefreshAppsCount()
    {
        _appEntries = BuildEntries();
        if (_tabs.TryGetValue("apps", out var tab)) tab.Content = BuildTabLabel("APLICATIVOS", AppsCount);
    }

    /// <summary>Redraws the tiles of the tab (also used when searching and after adding, moving or removing one).</summary>
    private void RebuildApps(string? focusKey = null)
    {
        if (_startApps is null || DateTime.UtcNow - _startAppsAt > TimeSpan.FromMinutes(3)) _ = LoadStartAppsAsync();

        var focusedIndex = focusKey is null && Keyboard.FocusedElement is DependencyObject f && IsAncestor(AppsPanel, f)
            ? FindAll<Button>(AppsPanel).ToList().FindIndex(b => IsAncestor(b, f))
            : -1;

        _appEntries = BuildEntries();
        if (_tabs.TryGetValue("apps", out var tab)) tab.Content = BuildTabLabel("APLICATIVOS", AppsCount);

        var text = SearchBox.Text.Trim();
        AppsPanel.Children.Clear();
        foreach (var entry in _appEntries.Where(a => text.Length == 0 || a.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)))
            AppsPanel.Children.Add(CreateAppTile(entry));
        if (text.Length == 0)
        {
            AppsPanel.Children.Add(CreateTextTile("+", "Adicionar à lista", "add", AddToAppsList));
            if (HiddenAppsCount > 0) AppsPanel.Children.Add(CreateTextTile("↺", $"Mostrar ocultos ({HiddenAppsCount})", "restore", RestoreHiddenApps));
        }

        var buttons = FindAll<Button>(AppsPanel).ToList();
        if (focusKey is not null)
        {
            var target = buttons.FirstOrDefault(b => b.Tag is AppEntry e && e.Key == focusKey || b.Tag is string t && t == focusKey) ?? buttons.FirstOrDefault();
            target?.Focus();
        }
        else if (focusedIndex >= 0 && buttons.Count > 0)
        {
            buttons[Math.Min(focusedIndex, buttons.Count - 1)].Focus();
        }
    }

    private Button CreateAppTile(AppEntry entry)
    {
        var image = new Image { Margin = new Thickness(60, 34, 60, 50), Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var initial = new TextBlock
        {
            Text = entry.Name.Length > 0 ? entry.Name[..1].ToUpperInvariant() : "?",
            FontSize = 64, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextSoft"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 24),
        };
        var label = new TextBlock
        {
            Text = entry.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextSoft"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 12),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var grid = new Grid();
        grid.Children.Add(initial);
        grid.Children.Add(image);
        grid.Children.Add(label);

        var button = new Button
        {
            Style = (Style)FindResource("MediaTile"), Content = grid, Tag = entry,
            Margin = new Thickness(0, 0, 24, 24),
        };
        System.Windows.Automation.AutomationProperties.SetName(button, entry.Name);
        button.Click += (_, _) => LaunchApp(entry);
        button.MouseRightButtonUp += (_, e) => { OpenAppMenu(entry); e.Handled = true; };
        button.GotKeyboardFocus += (_, _) => button.BringIntoView(new Rect(-24, -24, button.ActualWidth + 48, button.ActualHeight + 48 + 56));

        var key = IconKey(entry);
        if (_appIcons.TryGetValue(key, out var cached)) ApplyIcon(cached);
        else _ = LoadIconAsync();
        return button;

        void ApplyIcon(BitmapSource? icon)
        {
            if (icon is null) return;
            image.Source = icon;
            initial.Visibility = Visibility.Collapsed;
        }

        async Task LoadIconAsync()
        {
            var icon = entry.Url is not null ? await LoadSiteIconAsync(entry.Url) : await Task.Run(() => ReadIcon(entry));
            _appIcons[key] = icon;
            ApplyIcon(icon);
        }
    }

    /// <summary>A tile with a big symbol instead of an icon ("+" to add, an arrow to bring hidden apps back).</summary>
    private Button CreateTextTile(string symbol, string caption, string tag, Action onClick)
    {
        var plus = new TextBlock
        {
            Text = symbol, FontSize = 84, FontWeight = FontWeights.Light, Foreground = (Brush)FindResource("TextSoft"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 34),
        };
        var label = new TextBlock
        {
            Text = caption, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextSoft"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12),
        };
        var grid = new Grid();
        grid.Children.Add(plus);
        grid.Children.Add(label);

        var button = new Button { Style = (Style)FindResource("MediaTile"), Content = grid, Tag = tag, Margin = new Thickness(0, 0, 24, 24) };
        System.Windows.Automation.AutomationProperties.SetName(button, caption);
        button.Click += (_, _) => onClick();
        button.GotKeyboardFocus += (_, _) => button.BringIntoView(new Rect(-24, -24, button.ActualWidth + 48, button.ActualHeight + 48 + 56));
        return button;
    }

    /// <summary>Favicon of a site, through Google's public icon service (only the domain name is sent).</summary>
    private static async Task<BitmapSource?> LoadSiteIconAsync(string url)
    {
        try
        {
            var host = AppList.HostOf(url);
            var bytes = await LibraryService.Http.GetByteArrayAsync($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=128");
            if (bytes.Length == 0) return null;
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string IconKey(AppEntry e) => e.Url is not null ? "url:" + AppList.HostOf(e.Url) : e.AppId is not null ? "app:" + e.AppId : e.Path is not null ? "file:" + e.Path : "uri:" + e.Uri;

    /// <summary>The icon Windows shows for the app (runs off the UI thread).</summary>
    private static BitmapSource? ReadIcon(AppEntry entry)
    {
        try
        {
            if (entry.AppId is not null)
            {
                var icon = ShellThumbnail.Get(@"shell:AppsFolder\" + entry.AppId, 256, 256, iconOnly: true);
                if (icon is not null) return icon;
            }
            if (entry.Path is not null) return ShellThumbnail.Get(entry.Path, 256, 256, iconOnly: true);
            if (entry.Uri is not null && ProtocolCommand(entry.Uri.TrimEnd(':')) is { } command)
                return ShellThumbnail.Get(command.Exe, 256, 256, iconOnly: true);
        }
        catch (Exception)
        {
        }
        return null;
    }

    private void LaunchApp(AppEntry entry)
    {
        try
        {
            if (entry.AppId is not null) StartApps.Open(entry.AppId);
            else if (entry.Url is not null) Process.Start(new ProcessStartInfo(entry.Url) { UseShellExecute = true })?.Dispose();
            else if (entry.Uri is not null) Process.Start(new ProcessStartInfo(entry.Uri) { UseShellExecute = true })?.Dispose();
            else if (entry.Path is not null)
            {
                var info = new ProcessStartInfo(entry.Path) { UseShellExecute = true };
                if (Path.GetDirectoryName(entry.Path) is { Length: > 0 } dir && Directory.Exists(dir)) info.WorkingDirectory = dir;
                Process.Start(info)?.Dispose();
            }
            ShowToast($"Abrindo {entry.Name}…");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            ShowToast($"Não foi possível abrir {entry.Name}.");
        }
    }

    // ---- Add, options, order -----------------------------------------------------------------

    private string? _appMenuKey;

    private void AddToAppsList()
    {
        AppAddChoice.Visibility = Visibility.Visible;
        AppAddSite.Visibility = Visibility.Collapsed;
        AppAddUrlBox.Text = "";
        AppAddNameBox.Text = "";
        AppAddPanel.Visibility = Visibility.Visible;
        AppAddProgramButton.Focus();
    }

    private void CloseAppAdd(string? focusKey = "add")
    {
        AppAddPanel.Visibility = Visibility.Collapsed;
        VirtualKeyboard.Hide();
        RebuildApps(focusKey);
    }

    private void OnAppAddBackClick(object sender, RoutedEventArgs e) => CloseAppAdd();

    private void OnAppAddDimClick(object sender, MouseButtonEventArgs e) => CloseAppAdd();

    private void OnAppAddSiteClick(object sender, RoutedEventArgs e)
    {
        AppAddChoice.Visibility = Visibility.Collapsed;
        AppAddSite.Visibility = Visibility.Visible;
        AppAddUrlBox.Focus();
    }

    private void OnAppAddProgramClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha o programa",
            Filter = "Programas e atalhos (*.exe;*.lnk)|*.exe;*.lnk",
        };
        if (dialog.ShowDialog(this) != true) return;

        if (App.Settings.ManualApps.Any(a => string.Equals(a.Key, dialog.FileName, StringComparison.OrdinalIgnoreCase)))
        {
            ShowToast("Esse programa já está na lista.");
            return;
        }

        var name = Path.GetFileNameWithoutExtension(dialog.FileName);
        var app = new ManualApp { Name = name, Path = dialog.FileName };
        App.Settings.ManualApps.Add(app);
        SaveSettings();
        CloseAppAdd("m:" + app.Key);
        ShowToast($"{name} adicionado. Aperte X sobre ele para mais opções.");
    }

    private void OnAppAddSaveClick(object sender, RoutedEventArgs e)
    {
        var url = AppList.NormalizeUrl(AppAddUrlBox.Text);
        if (url is null)
        {
            ShowToast("Endereço inválido. Exemplo: youtube.com");
            AppAddUrlBox.Focus();
            return;
        }

        if (App.Settings.ManualApps.Any(a => string.Equals(a.Key, url, StringComparison.OrdinalIgnoreCase)))
        {
            ShowToast("Esse site já está na lista.");
            return;
        }

        var name = AppAddNameBox.Text.Trim();
        if (name.Length == 0) name = AppList.SuggestWebName(url);
        var app = new ManualApp { Name = name, Url = url };
        App.Settings.ManualApps.Add(app);
        SaveSettings();
        CloseAppAdd("m:" + app.Key);
        ShowToast($"{name} adicionado.");
    }

    private void RestoreHiddenApps()
    {
        App.Settings.HiddenApps.Clear();
        SaveSettings();
        RebuildApps(_appEntries.FirstOrDefault()?.Key);
        ShowToast("Aplicativos ocultos de volta à lista.");
    }

    /// <summary>X (or right click) on an app tile opens its options. False when the focus is not on an app.</summary>
    private bool OpenAppMenuForFocused()
    {
        if (_page != "Biblioteca" || _filter != "apps" || OptionsOpen) return false;
        if (Keyboard.FocusedElement is not FrameworkElement { Tag: AppEntry entry }) return false;
        OpenAppMenu(entry);
        return true;
    }

    private void OpenAppMenu(AppEntry entry)
    {
        if (OptionsOpen || PowerOpen || ManageOpen || ProfileOpen || AccountOpen || LaunchOverlayOpen || MenuOpen) return;
        _appMenuKey = entry.Key;
        AppMenuName.Text = entry.Name;
        AppMenuRemove.Content = entry.Manual is null ? "Ocultar da lista" : "Remover da lista";
        UpdateAppMenuButtons();
        AppMenuPanel.Visibility = Visibility.Visible;
        (AppMenuLeft.IsEnabled ? AppMenuLeft : AppMenuRight.IsEnabled ? AppMenuRight : AppMenuRemove).Focus();
    }

    private void UpdateAppMenuButtons()
    {
        var index = _appEntries.FindIndex(e => e.Key == _appMenuKey);
        AppMenuLeft.IsEnabled = index > 0;
        AppMenuRight.IsEnabled = index >= 0 && index < _appEntries.Count - 1;
    }

    private void CloseAppMenu()
    {
        AppMenuPanel.Visibility = Visibility.Collapsed;
        RebuildApps(_appMenuKey);
    }

    private void OnAppMenuBack(object sender, RoutedEventArgs e) => CloseAppMenu();

    private void OnAppMenuDimClick(object sender, MouseButtonEventArgs e) => CloseAppMenu();

    private void OnAppMenuMoveLeft(object sender, RoutedEventArgs e) => MoveApp(-1);

    private void OnAppMenuMoveRight(object sender, RoutedEventArgs e) => MoveApp(+1);

    private void MoveApp(int delta)
    {
        if (_appMenuKey is null) return;
        var keys = _appEntries.Select(e => e.Key).ToList();
        if (!AppList.Move(keys, _appMenuKey, delta)) return;

        App.Settings.AppsOrder = keys;
        SaveSettings();
        _appEntries = BuildEntries();
        UpdateAppMenuButtons();
        if (!(delta < 0 ? AppMenuLeft : AppMenuRight).IsEnabled) (delta < 0 ? AppMenuRight : AppMenuLeft).Focus();
        UiSound.Play("move");
    }

    private void OnAppMenuRemove(object sender, RoutedEventArgs e)
    {
        var entry = _appEntries.FirstOrDefault(a => a.Key == _appMenuKey);
        if (entry is null) { CloseAppMenu(); return; }

        var index = _appEntries.IndexOf(entry);
        if (entry.Manual is { } manual)
        {
            App.Settings.ManualApps.Remove(manual);
            ShowToast($"{entry.Name} removido da lista (o programa não foi apagado).");
        }
        else
        {
            App.Settings.HiddenApps.Add(entry.Name);
            ShowToast($"{entry.Name} ocultado. Use \"Mostrar ocultos\" no fim da lista para trazer de volta.");
        }

        SaveSettings();
        var after = BuildEntries();
        _appMenuKey = after.Count == 0 ? "add" : after[Math.Min(index, after.Count - 1)].Key;
        AppMenuPanel.Visibility = Visibility.Collapsed;
        RebuildApps(_appMenuKey);
    }
}
