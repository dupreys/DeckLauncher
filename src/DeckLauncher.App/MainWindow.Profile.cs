using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeckLauncher.Core.Settings;

namespace DeckLauncher.App;

// Profile: the round-corner photo in the top bar opens a panel to change the photo and the name shown in the launcher.
// Nothing here touches the Steam account: the photo and name are kept only in this launcher.
public partial class MainWindow
{
    private bool ProfileOpen => ProfilePanel.Visibility == Visibility.Visible;

    private ImageSource? _steamAvatar;

    private void InitProfile()
    {
        _ = RefreshAvatarAsync();
        _ = FetchSteamProfileAsync();
    }

    // ---- Name and picture ----------------------------------------------------------------------

    private string DisplayName =>
        !string.IsNullOrWhiteSpace(App.Settings.CustomName) ? App.Settings.CustomName!
        : !string.IsNullOrWhiteSpace(App.Settings.PersonaName) ? App.Settings.PersonaName!
        : Environment.UserName;

    private static ImageSource? LoadLocalImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 256;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null; // not a picture Windows can open
        }
    }

    /// <summary>Shows the chosen photo, else the Steam one, else the plain icon.</summary>
    private async Task RefreshAvatarAsync()
    {
        var image = LoadLocalImage(App.Settings.CustomAvatarPath);
        if (image is null && !string.IsNullOrWhiteSpace(App.Settings.AvatarUrl))
        {
            _steamAvatar ??= await RemoteImage.LoadAsync(LibraryService.Http, App.Settings.AvatarUrl, 256);
            image = _steamAvatar;
        }
        if (image is null && _activeSteamId is { } id) image = LoadLocalImage(AvatarCachePath(id)); // the picture Steam keeps on this PC
        AvatarImage.Source = image;
        AvatarGlyph.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        ProfileImage.Source = image;
    }

    /// <summary>With a Steam Web API key, reads the Steam name and photo (public info) once per run.</summary>
    private async Task FetchSteamProfileAsync()
    {
        var key = App.Settings.SteamWebApiKey;
        var id = SteamIdValue();
        if (string.IsNullOrWhiteSpace(key) || id is null) return;
        try
        {
            var json = await LibraryService.Http.GetStringAsync($"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={key}&steamids={id}");
            using var doc = JsonDocument.Parse(json);
            var players = doc.RootElement.GetProperty("response").GetProperty("players");
            if (players.GetArrayLength() == 0) return;
            var p = players[0];
            string? Str(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            App.Settings.PersonaName = Str("personaname") ?? App.Settings.PersonaName;
            App.Settings.AvatarUrl = Str("avatarfull") ?? Str("avatarmedium") ?? App.Settings.AvatarUrl;
            _steamAvatar = null;
            SaveSettings();
            await RefreshAvatarAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no network or a wrong key: the plain icon stays
        }
    }

    // ---- Panel ---------------------------------------------------------------------------------

    private void OnProfileClick(object sender, RoutedEventArgs e) => OpenProfile();

    private static readonly (string Id, string Name)[] AvatarPresets =
    {
        ("avatar-01", "Controle"), ("avatar-02", "Fantasma"), ("avatar-03", "Estrela"), ("avatar-04", "Coração"),
        ("avatar-05", "Raio"), ("avatar-06", "Coroa"), ("avatar-07", "Planeta"), ("avatar-08", "Diamante"),
        ("avatar-09", "Escudo"), ("avatar-10", "Gota"), ("avatar-11", "Lua"), ("avatar-12", "Alvo"),
    };

    private readonly Dictionary<string, Button> _profileChoices = new();
    private readonly Dictionary<string, string> _profileChoiceNames = new();

    private void OpenProfile()
    {
        if (PowerOpen || ManageOpen || OptionsOpen || ProfileOpen || LaunchOverlayOpen) return;
        if (QuickOpen) CloseQuick();
        if (MenuOpen) CloseMenu(true);
        if (App.Settings.AvatarUrl is null) _ = FetchSteamProfileAsync();
        if (_profileChoices.Count == 0) BuildProfileChoices();
        UpdateProfilePanel();
        ProfilePanel.Visibility = Visibility.Visible;
        var current = App.Settings.AvatarPreset ?? (string.IsNullOrWhiteSpace(App.Settings.CustomAvatarPath) && _profileChoices["steam"].Visibility == Visibility.Visible ? "steam" : "file");
        (_profileChoices.TryGetValue(current, out var tile) ? tile : _profileChoices["file"]).Focus();
    }

    private void CloseProfile()
    {
        ProfilePanel.Visibility = Visibility.Collapsed;
        VirtualKeyboard.Hide();
        ProfileButton.Focus();
    }

    /// <summary>The row of pictures: the Steam photo, "from this PC" and the launcher's own pack. Built once; the rings are refreshed in place.</summary>
    private void BuildProfileChoices()
    {
        AddChoice("steam", "Foto da Steam", null, null);
        AddChoice("file", "Escolher uma foto do computador...", "\uE838", null);
        foreach (var (id, name) in AvatarPresets)
        {
            var brush = new ImageBrush(new BitmapImage(new Uri($"pack://application:,,,/Assets/Avatars/{id}.png"))) { Stretch = Stretch.UniformToFill };
            AddChoice(id, name, null, brush);
        }
    }

    private void AddChoice(string id, string name, string? glyph, Brush? picture)
    {
        var button = new Button { Style = (Style)FindResource("AvatarChoice"), Tag = id, Background = picture ?? (Brush)FindResource("PanelRaised") };
        if (glyph is not null) button.Content = new System.Windows.Controls.TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 28, Foreground = (Brush)FindResource("TextSoft") };
        else if (picture is null) button.Content = new System.Windows.Controls.TextBlock { Text = "\uE77B", Style = (Style)FindResource("Icon"), FontSize = 30, Foreground = (Brush)FindResource("TextSoft") };
        _profileChoiceNames[id] = name;
        button.Click += OnProfileChoiceClick;
        button.GotKeyboardFocus += (_, _) => ProfileChoiceLabel.Text = UiText.T(name);
        _profileChoices[id] = button;
        ProfileChoices.Children.Add(button);
    }

    private void UpdateProfilePanel()
    {
        ProfileSwitchButton.Visibility = HasSeveralAccounts ? Visibility.Visible : Visibility.Collapsed;
        ProfileName.Text = DisplayName;
        ProfileNameBox.Text = App.Settings.CustomName ?? "";
        var hasCustom = !string.IsNullOrWhiteSpace(App.Settings.CustomAvatarPath);
        var hasSteam = !string.IsNullOrWhiteSpace(App.Settings.AvatarUrl) || (_activeSteamId is { } id && File.Exists(AvatarCachePath(id)));
        var preset = App.Settings.AvatarPreset;
        ProfileSub.Text = UiText.T(preset is not null ? "Foto do launcher" : hasCustom ? "Foto escolhida por você" : hasSteam ? "Foto da Steam" : "Sem foto");
        ProfileRemovePhotoButton.IsEnabled = hasCustom;

        // the Steam tile shows the Steam photo, or is hidden when there is none
        var steamTile = _profileChoices["steam"];
        steamTile.Visibility = hasSteam ? Visibility.Visible : Visibility.Collapsed;
        var steamPicture = _steamAvatar ?? (_activeSteamId is { } sid ? LoadLocalImage(AvatarCachePath(sid)) : null);
        if (steamPicture is not null)
        {
            steamTile.Background = new ImageBrush(steamPicture) { Stretch = Stretch.UniformToFill };
            steamTile.Content = null;
        }

        var accent = (Brush)FindResource("Accent");
        var selected = preset ?? (hasCustom ? null : hasSteam ? "steam" : null);
        foreach (var (key, tile) in _profileChoices) tile.BorderBrush = key == selected ? accent : Brushes.Transparent;
        ProfileChoiceLabel.Text = selected is not null && _profileChoiceNames.TryGetValue(selected, out var name) ? UiText.T(name) : "";
    }

    private void OnProfileBackClick(object sender, RoutedEventArgs e) => CloseProfile();

    private void OnProfileDimClick(object sender, MouseButtonEventArgs e) => CloseProfile();

    private async void OnProfileChoiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (id == "file") { await ChooseOwnPhotoAsync(); return; }
        if (id == "steam") { await UseSteamPhotoAsync(); return; }
        await UsePresetAsync(id);
    }

    private async Task UsePresetAsync(string id)
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Avatars/{id}.png"));
            if (info is null) return;
            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            var copy = Path.Combine(SettingsStore.DefaultDirectory, $"{id}-{DateTime.Now:yyyyMMddHHmmss}.png");
            await using (var output = File.Create(copy))
            {
                await using var input = info.Stream;
                await input.CopyToAsync(output);
            }
            DeleteOldAvatar();
            App.Settings.CustomAvatarPath = copy;
            App.Settings.AvatarPreset = id;
            SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Não foi possível salvar a foto.");
            return;
        }
        await RefreshAvatarAsync();
        UpdateProfilePanel();
        ShowToast("Foto atualizada");
    }

    private async Task ChooseOwnPhotoAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = UiText.T("Escolha a sua foto"),
            Filter = UiText.T("Imagens (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"),
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            if (new FileInfo(dialog.FileName).Length > 12 * 1024 * 1024)
            {
                ShowToast("A imagem é grande demais (máximo 12 MB).");
                return;
            }
            if (LoadLocalImage(dialog.FileName) is null)
            {
                ShowToast("Não foi possível abrir essa imagem.");
                return;
            }

            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            var copy = Path.Combine(SettingsStore.DefaultDirectory, $"avatar-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(dialog.FileName).ToLowerInvariant()}");
            File.Copy(dialog.FileName, copy, true);
            DeleteOldAvatar();
            App.Settings.CustomAvatarPath = copy;
            App.Settings.AvatarPreset = null;
            SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Não foi possível salvar a foto.");
            return;
        }

        await RefreshAvatarAsync();
        UpdateProfilePanel();
        ShowToast("Foto atualizada");
    }

    private async Task UseSteamPhotoAsync()
    {
        DeleteOldAvatar();
        App.Settings.CustomAvatarPath = null;
        App.Settings.AvatarPreset = null;
        SaveSettings();
        await RefreshAvatarAsync();
        UpdateProfilePanel();
        ShowToast("Usando a foto da Steam");
    }

    private async void OnProfileRemovePhotoClick(object sender, RoutedEventArgs e)
    {
        DeleteOldAvatar();
        App.Settings.CustomAvatarPath = null;
        App.Settings.AvatarPreset = null;
        SaveSettings();
        await RefreshAvatarAsync();
        UpdateProfilePanel();
        ShowToast("Foto removida");
        _profileChoices["file"].Focus();
    }

    private void OnProfileSaveNameClick(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (name.Length > AppSettingsMaxName) name = name[..AppSettingsMaxName];
        App.Settings.CustomName = name.Length == 0 ? null : name;
        SaveSettings();
        UpdateProfilePanel();
        ShowToast(name.Length == 0 ? "Nome original restaurado" : "Nome salvo");
    }

    private const int AppSettingsMaxName = AppSettings.MaxNameLength;

    private static void DeleteOldAvatar()
    {
        var old = App.Settings.CustomAvatarPath;
        if (string.IsNullOrWhiteSpace(old)) return;
        try { if (File.Exists(old) && Path.GetFileName(old).StartsWith("avatar-", StringComparison.Ordinal)) File.Delete(old); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a leftover file is harmless */ }
    }
}
