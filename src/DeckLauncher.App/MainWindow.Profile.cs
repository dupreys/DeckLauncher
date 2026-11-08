using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
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

    private void OpenProfile()
    {
        if (PowerOpen || ManageOpen || OptionsOpen || ProfileOpen || LaunchOverlayOpen) return;
        if (QuickOpen) CloseQuick();
        if (MenuOpen) CloseMenu(true);
        if (App.Settings.AvatarUrl is null) _ = FetchSteamProfileAsync();
        UpdateProfilePanel();
        ProfilePanel.Visibility = Visibility.Visible;
        ProfilePhotoButton.Focus();
    }

    private void CloseProfile()
    {
        ProfilePanel.Visibility = Visibility.Collapsed;
        VirtualKeyboard.Hide();
        ProfileButton.Focus();
    }

    private void UpdateProfilePanel()
    {
        ProfileSwitchButton.Visibility = HasSeveralAccounts ? Visibility.Visible : Visibility.Collapsed;
        ProfileName.Text = DisplayName;
        ProfileNameBox.Text = App.Settings.CustomName ?? "";
        var hasCustom = !string.IsNullOrWhiteSpace(App.Settings.CustomAvatarPath);
        var hasSteam = !string.IsNullOrWhiteSpace(App.Settings.AvatarUrl);
        ProfileSub.Text = hasCustom ? "Foto escolhida por você" : hasSteam ? "Foto da Steam" : "Sem foto";
        ProfileRemovePhotoButton.IsEnabled = hasCustom;
        ProfileSteamPhotoButton.IsEnabled = hasCustom && hasSteam; // only useful when a chosen photo is hiding it
        ProfileSteamPhotoButton.Visibility = hasSteam ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnProfileBackClick(object sender, RoutedEventArgs e) => CloseProfile();

    private void OnProfileDimClick(object sender, MouseButtonEventArgs e) => CloseProfile();

    private async void OnProfilePhotoClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha a sua foto",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
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

    private async void OnProfileSteamPhotoClick(object sender, RoutedEventArgs e)
    {
        DeleteOldAvatar();
        App.Settings.CustomAvatarPath = null;
        SaveSettings();
        await RefreshAvatarAsync();
        UpdateProfilePanel();
        ShowToast("Usando a foto da Steam");
    }

    private async void OnProfileRemovePhotoClick(object sender, RoutedEventArgs e)
    {
        DeleteOldAvatar();
        App.Settings.CustomAvatarPath = null;
        SaveSettings();
        await RefreshAvatarAsync();
        UpdateProfilePanel();
        ShowToast("Foto removida");
        ProfilePhotoButton.Focus();
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
