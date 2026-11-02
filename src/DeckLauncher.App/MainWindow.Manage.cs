using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiteLauncher.Core;
using LiteLauncher.Core.Settings;
using LiteLauncher.Core.Steam;

namespace DeckLauncher.App;

// Manage art: SteamGridDB covers / backgrounds / logos, 16:9 wallpapers from Wallhaven and Steam screenshots.
// The picture the user picks is saved as an override for that game and wins over the store's own art.
public partial class MainWindow
{
    private readonly Dictionary<string, int?> _gridGameIds = new();
    private CancellationTokenSource? _manageCts;
    private ArtKind _manageKind = ArtKind.Cover;

    private bool ManageOpen => ManagePanel.Visibility == Visibility.Visible;

    private void OpenManage()
    {
        if (_gameItem is not { } item) return;
        ManageGameName.Text = item.Name;
        KeyBox.Text = "";
        KeyRow.Visibility = App.Settings.SteamGridDbApiKey is null ? Visibility.Visible : Visibility.Collapsed;
        ManagePanel.Visibility = Visibility.Visible;

        foreach (var tab in ManageTabs.Children.OfType<RadioButton>()) tab.IsChecked = false;
        ManageTabCover.IsChecked = true; // raises Checked, which loads the covers
        ManageTabCover.Focus();
    }

    private void CloseManage()
    {
        _manageCts?.Cancel();
        ManagePanel.Visibility = Visibility.Collapsed;
        ManageResults.Children.Clear();
        ManageButton.Focus();
    }

    private void OnManageBackClick(object sender, RoutedEventArgs e) => CloseManage();

    private async void OnManageTabChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) await LoadManageTabAsync(tag);
    }

    private async Task LoadManageTabAsync(string tag)
    {
        if (_gameItem is not { } item) return;
        _manageCts?.Cancel();
        var cts = _manageCts = new CancellationTokenSource();
        ManageResults.Children.Clear();

        try
        {
            switch (tag)
            {
                case "Wallhaven": await LoadWallhavenAsync(item, cts); break;
                case "Shots": await LoadShotsAsync(item, cts); break;
                default: await LoadGridAsync(item, tag == "Logo" ? ArtKind.Logo : tag == "Background" ? ArtKind.Background : ArtKind.Cover, cts); break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (!cts.IsCancellationRequested) ManageStatus.Text = "Não foi possível acessar o serviço. Verifique sua conexão com a internet.";
        }
    }

    private async Task LoadGridAsync(GameItem item, ArtKind kind, CancellationTokenSource cts)
    {
        if (App.Settings.SteamGridDbApiKey is not { } apiKey)
        {
            KeyRow.Visibility = Visibility.Visible;
            ManageStatus.Text = "Informe sua chave gratuita do SteamGridDB acima para ver capas, fundos e logotipos.";
            return;
        }

        _manageKind = kind;
        var name = kind switch { ArtKind.Cover => "capas", ArtKind.Background => "fundos", _ => "logotipos" };
        ManageStatus.Text = "Buscando no SteamGridDB…";
        try
        {
            var key = GameSorter.Key(item.Game);
            if (!_gridGameIds.TryGetValue(key, out var gameId))
                _gridGameIds[key] = gameId = await SteamGridDb.FindGameIdAsync(LibraryService.Http, apiKey, item.Game, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (gameId is null)
            {
                ManageStatus.Text = "Este jogo não foi encontrado no SteamGridDB.";
                return;
            }

            var images = await SteamGridDb.GetImagesAsync(LibraryService.Http, apiKey, gameId.Value, kind, cts.Token);
            if (cts.IsCancellationRequested) return;
            ManageStatus.Text = images.Count == 0
                ? $"Nenhum item de {name} no SteamGridDB para este jogo."
                : $"{images.Count} {name} · escolha uma para usar";
            foreach (var image in images.Take(60)) ManageResults.Children.Add(MakeArtButton(image, kind, wide: false));
        }
        catch (UnauthorizedAccessException)
        {
            KeyRow.Visibility = Visibility.Visible;
            ManageStatus.Text = "O SteamGridDB recusou a chave. Confira e salve de novo acima.";
        }
    }

    private async Task LoadWallhavenAsync(GameItem item, CancellationTokenSource cts)
    {
        _manageKind = ArtKind.Background;
        ManageStatus.Text = "Buscando no Wallhaven…";
        var images = await Wallhaven.SearchAsync(LibraryService.Http, item.Game.OriginalName ?? item.Game.Name, cts.Token);
        if (cts.IsCancellationRequested) return;
        ManageStatus.Text = images.Count == 0
            ? "Nenhum papel de parede 16:9 encontrado no Wallhaven para este jogo."
            : $"{images.Count} papéis de parede do Wallhaven (16:9) · escolha um para usar como fundo";
        foreach (var image in images) ManageResults.Children.Add(MakeArtButton(image, ArtKind.Background, wide: true));
    }

    private async Task LoadShotsAsync(GameItem item, CancellationTokenSource cts)
    {
        _manageKind = ArtKind.Background;
        if (item.Game.Source != SteamProvider.ProviderName)
        {
            ManageStatus.Text = "As capturas só existem para jogos da Steam.";
            return;
        }

        ManageStatus.Text = "Carregando…";
        var shots = await SteamScreenshots.FetchAsync(LibraryService.Http, item.Game.Id, cts.Token);
        if (cts.IsCancellationRequested) return;
        ManageStatus.Text = shots.Count == 0
            ? "Nenhuma captura encontrada para este jogo na Steam."
            : $"{shots.Count} capturas (16:9, ocupam a tela inteira) · escolha uma para usar como fundo";
        foreach (var shot in shots.Take(40)) ManageResults.Children.Add(MakeArtButton(shot, ArtKind.Background, wide: true));
    }

    private Button MakeArtButton(ArtImage art, ArtKind kind, bool wide)
    {
        var (w, h) = kind switch
        {
            ArtKind.Cover => (150.0, 225.0),
            ArtKind.Background => wide ? (288.0, 162.0) : (360.0, 118.0),
            _ => (260.0, 118.0),
        };
        var image = new Image
        {
            Stretch = kind == ArtKind.Logo ? Stretch.Uniform : Stretch.UniformToFill,
            Margin = new Thickness(kind == ArtKind.Logo ? 12 : 0),
        };
        var button = new Button
        {
            Style = (Style)FindResource("PickTile"),
            Width = w,
            Height = h,
            Content = image,
            ToolTip = art.Width > 0 ? $"{art.Width} × {art.Height}" : null,
        };
        button.Click += async (_, _) => await ApplyArtAsync(art, kind);
        _ = SetThumbAsync(image, art.ThumbUrl);
        return button;
    }

    private static async Task SetThumbAsync(Image target, string url) =>
        target.Source = await RemoteImage.LoadAsync(LibraryService.Http, url, 380);

    // ---- Apply / reset ----------------------------------------------------------------------

    private async Task ApplyArtAsync(ArtImage art, ArtKind kind)
    {
        if (_gameItem is not { } item) return;
        var key = GameSorter.Key(item.Game);
        ManageStatus.Text = "Baixando…";

        var extension = Path.GetExtension(new Uri(art.Url).AbsolutePath);
        if (extension.Length is 0 or > 5) extension = ".png";
        var safeKey = string.Concat(key.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var folder = Path.Combine(SettingsStore.DefaultDirectory, "games");
        var destination = Path.Combine(folder, $"{safeKey}_{kind.ToString().ToLowerInvariant()}_{DateTime.UtcNow.Ticks}{extension}");

        try
        {
            var bytes = await LibraryService.DownloadHttp.GetByteArrayAsync(art.Url);
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(destination, bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            ManageStatus.Text = "O download falhou. Tente outra imagem.";
            return;
        }

        if (await ImageLoader.LoadAsync(destination, 64) is null)
        {
            DeleteQuietly(destination);
            ManageStatus.Text = "Esse formato de imagem não é suportado. Tente outra.";
            return;
        }

        string? old = null;
        if (App.Settings.GameOverrides.TryGetValue(key, out var current))
            old = kind switch { ArtKind.Cover => current.CoverPath, ArtKind.Background => current.BackdropPath, _ => current.LogoPath };

        await EditOverrideAsync(key, o =>
        {
            switch (kind)
            {
                case ArtKind.Cover: o.CoverPath = destination; break;
                case ArtKind.Background: o.BackdropPath = destination; break;
                default: o.LogoPath = destination; break;
            }
        });
        DeleteQuietly(old);

        ManageStatus.Text = kind switch
        {
            ArtKind.Cover => "Capa aplicada. Escolha outra, ou volte.",
            ArtKind.Background => "Fundo aplicado. Escolha outro, ou volte.",
            _ => "Logotipo aplicado. Escolha outro, ou volte.",
        };
        ShowToast(kind switch { ArtKind.Cover => "Capa atualizada", ArtKind.Background => "Fundo atualizado", _ => "Logotipo atualizado" });
    }

    private async void OnManageResetClick(object sender, RoutedEventArgs e)
    {
        if (_gameItem is not { } item) return;
        var key = GameSorter.Key(item.Game);
        if (!App.Settings.GameOverrides.Remove(key, out var removed))
        {
            ManageStatus.Text = "Este jogo já usa a arte original.";
            return;
        }

        DeleteQuietly(removed.CoverPath);
        DeleteQuietly(removed.BackdropPath);
        DeleteQuietly(removed.LogoPath);
        SaveSettings();
        await ReloadGameAfterChangeAsync(key);
        ManageStatus.Text = "Capa, fundo e logotipo originais restaurados.";
        ShowToast("Arte original restaurada");
    }

    /// <summary>Edits this game's override, saves it and shows the change everywhere (lists and the open game page).</summary>
    private async Task EditOverrideAsync(string key, Action<GameOverride> edit)
    {
        var overrides = App.Settings.GameOverrides;
        if (!overrides.TryGetValue(key, out var o)) overrides[key] = o = new GameOverride();
        edit(o);
        if (o.IsEmpty) overrides.Remove(key);
        SaveSettings();
        await ReloadGameAfterChangeAsync(key);
    }

    private async Task ReloadGameAfterChangeAsync(string key)
    {
        while (_refreshing) await Task.Delay(100);
        await RefreshAsync();

        if (_itemCache.TryGetValue(key, out var fresh))
        {
            _gameItem = fresh;
            fresh.EnsureWideLoaded();
            GamePage.DataContext = fresh;
            _ = LoadGameHeroAsync(fresh);
        }
    }

    private static void DeleteQuietly(string? path)
    {
        try { if (path is not null && File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a leftover file is harmless */ }
    }

    // ---- SteamGridDB key --------------------------------------------------------------------

    private void OnSaveKeyClick(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Text.Trim();
        if (key.Length == 0) { ManageStatus.Text = "Cole a chave da API no campo ao lado."; return; }

        App.Settings.SteamGridDbApiKey = key;
        SaveSettings();
        _gridGameIds.Clear();
        KeyRow.Visibility = Visibility.Collapsed;
        ShowToast("Chave salva");
        ManageTabCover.IsChecked = true;
        _ = LoadManageTabAsync("Cover");
        ManageTabCover.Focus();
    }

    private void OnGetKeyClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://www.steamgriddb.com/profile/preferences/api") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ManageStatus.Text = "Abra https://www.steamgriddb.com/profile/preferences/api no navegador para criar a chave.";
        }
    }
}
