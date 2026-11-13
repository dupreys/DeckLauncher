using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DeckLauncher.App;

/// <summary>A capture recorded by the Windows Game Bar (a screenshot or a video).</summary>
internal sealed record MediaFile(string Path, bool IsVideo, DateTime Taken)
{
    /// <summary>"Dead by Daylight 2026-11-02 03-15-46.mp4" becomes "Dead by Daylight"; Game Bar screenshots become "Captura de tela".</summary>
    public string Title
    {
        get
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(Path);
            if (name.StartsWith("Screenshot", StringComparison.OrdinalIgnoreCase)) return "Captura de tela";
            var cut = Regex.Match(name, @"^(.*?)[\s_]*\d{4}[-_]\d{2}[-_]\d{2}");
            if (cut.Success && cut.Groups[1].Value.Trim().Length > 0) return cut.Groups[1].Value.Trim();
            return IsVideo && cut.Success ? "Vídeo" : name;
        }
    }
}

/// <summary>
/// Mídias: the Game Bar captures (Videos\Captures) shown inside the launcher, photos and videos alike.
/// View only: no sharing, no editing.
/// </summary>
public partial class MainWindow
{
    private static readonly string[] PhotoExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".wmv", ".avi" };

    private List<MediaFile> _media = new();
    private string _mediaSignature = "";
    private int _mediaIndex;
    private int _mediaLoadGen;
    private int _viewerGen;
    private readonly SemaphoreSlim _thumbGate = new(3);

    private readonly DispatcherTimer _viewerTimeline = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _viewerBarTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _viewerMessageTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private TimeSpan _videoDuration;
    private bool _videoPlaying;
    private bool _videoEnded;
    private bool _videoFailed;
    private double _viewerVolume = 0.8;

    private bool MediaViewerOpen => MediaViewer.Visibility == Visibility.Visible;

    private void InitMedia()
    {
        PreviewKeyDown += OnViewerPreviewKey;
        LoadStoreLogos();
        _viewerTimeline.Tick += (_, _) => UpdateViewerTimeline();
        _viewerBarTimer.Tick += (_, _) =>
        {
            _viewerBarTimer.Stop();
            if (_videoPlaying) ViewerBar.Visibility = Visibility.Collapsed;
        };
        _viewerMessageTimer.Tick += (_, _) =>
        {
            _viewerMessageTimer.Stop();
            if (!_videoEnded && !_videoFailed) ViewerMessage.Visibility = Visibility.Collapsed;
        };
        ViewerTrack.SizeChanged += (_, _) => UpdateViewerTimeline();
    }

    /// <summary>
    /// Store icons: the program's own icon (Steam / Epic Games Launcher, found through the registered link) or,
    /// when you put store-steam.png / store-epic.png in the Assets folder, that picture. Without either, a drawing.
    /// </summary>
    private async void LoadStoreLogos()
    {
        var steam = SteamLogo; var epic = EpicLogo;
        var galaxy = GalaxyLogo;
        var (steamPicture, epicPicture, galaxyPicture) = await Task.Run(() => (StoreIcon("steam", "store-steam.png"), StoreIcon("com.epicgames.launcher", "store-epic.png"), StoreIcon(null, "store-gog.png", LibraryService.GalaxyExe)));
        ShowStoreLogo(steam, SteamIcon, SteamLabel, steamPicture);
        ShowStoreLogo(epic, EpicIcon, EpicLabel, epicPicture);
        ShowStoreLogo(galaxy, GalaxyIcon, GalaxyLabel, galaxyPicture);
        UpdateStoreTiles();
    }

    /// <summary>The Lojas page shows only the stores whose program is installed on this PC.</summary>
    private void UpdateStoreTiles()
    {
        var steamInstalled = ProtocolCommand("steam") is not null;
        var epicInstalled = ProtocolCommand("com.epicgames.launcher") is not null;
        StoreSteamButton.Visibility = steamInstalled ? Visibility.Visible : Visibility.Collapsed;
        StoreEpicButton.Visibility = epicInstalled ? Visibility.Visible : Visibility.Collapsed;
        var galaxyInstalled = LibraryService.GalaxyExe is not null;
        StoreGalaxyButton.Visibility = galaxyInstalled ? Visibility.Visible : Visibility.Collapsed;
        StoresEmpty.Visibility = steamInstalled || epicInstalled || galaxyInstalled ? Visibility.Collapsed : Visibility.Visible;
        if (_page == "Lojas" && !StoreSteamButton.IsKeyboardFocusWithin && !StoreEpicButton.IsKeyboardFocusWithin && !StoreGalaxyButton.IsKeyboardFocusWithin) FocusStores();
    }

    private void FocusStores()
    {
        if (StoreSteamButton.Visibility == Visibility.Visible) StoreSteamButton.Focus();
        else if (StoreEpicButton.Visibility == Visibility.Visible) StoreEpicButton.Focus();
        else if (StoreGalaxyButton.Visibility == Visibility.Visible) StoreGalaxyButton.Focus();
    }

    private static BitmapSource? StoreIcon(string? scheme, string file, string? exe = null)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", file);
            if (File.Exists(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            if (exe is not null) return ShellThumbnail.Get(exe, 256, 256, iconOnly: true);
            return scheme is not null && ProtocolCommand(scheme) is { } command ? ShellThumbnail.Get(command.Exe, 256, 256, iconOnly: true) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ShowStoreLogo(Image image, UIElement drawing, UIElement label, BitmapSource? picture)
    {
        if (picture is null) return;
        image.Source = picture;
        image.Visibility = Visibility.Visible;
        drawing.Visibility = Visibility.Collapsed;
        label.Visibility = Visibility.Collapsed;
    }

    // ---- The page: a grid of thumbnails -----------------------------------------------------

    /// <summary>Where the Game Bar saves captures: the user's Videos\Captures folder.</summary>
    private static string CapturesFolder()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrEmpty(videos)) videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
        return Path.Combine(videos, "Captures");
    }

    private static List<MediaFile> ScanCaptures(string folder)
    {
        var list = new List<MediaFile>();
        if (!Directory.Exists(folder)) return list;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 3 };
        foreach (var path in Directory.EnumerateFiles(folder, "*", options))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var video = VideoExtensions.Contains(extension);
            if (!video && !PhotoExtensions.Contains(extension)) continue;
            try { list.Add(new MediaFile(path, video, File.GetLastWriteTime(path))); }
            catch (IOException) { }
        }
        return list.OrderByDescending(f => f.Taken).Take(600).ToList();
    }

    private async void LoadMedia()
    {
        var generation = ++_mediaLoadGen;
        var folder = CapturesFolder();
        List<MediaFile> found;
        try { found = await Task.Run(() => ScanCaptures(folder)); }
        catch (Exception) { found = new List<MediaFile>(); }
        if (generation != _mediaLoadGen || _page != "Mídias") return;

        var signature = string.Join('|', found.Select(f => f.Path + f.Taken.Ticks));
        if (signature == _mediaSignature && MediaGrid.Children.Count == found.Count) return; // nothing new: keep the tiles and the focus

        _mediaSignature = signature;
        _media = found;
        MediaGrid.Children.Clear();
        for (var i = 0; i < found.Count; i++)
        {
            var tile = BuildMediaTile(found[i], i, out var image);
            MediaGrid.Children.Add(tile);
            _ = LoadTileAsync(image, found[i]);
        }

        var photos = found.Count(f => !f.IsVideo);
        var videos = found.Count - photos;
        MediaSubtitle.Text = found.Count == 0 ? "" : $"{photos} capturas de tela  ·  {videos} vídeos";
        MediaEmpty.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MediaEmpty.Text = Directory.Exists(folder)
            ? "Nenhuma captura por aqui ainda.\nTire prints ou grave vídeos com o Game Bar do Windows (Win + G) e eles aparecem aqui."
            : $"A pasta de capturas ainda não existe:\n{folder}\nO Game Bar a cria na primeira captura (Win + G).";

        if (!MenuOpen && _page == "Mídias") FocusMedia(Math.Min(_mediaIndex, found.Count - 1));
    }

    private Button BuildMediaTile(MediaFile file, int index, out Image image)
    {
        image = new Image { Stretch = Stretch.UniformToFill };
        var grid = new Grid { ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x22, 0x2C)) };
        grid.Children.Add(image);

        if (file.IsVideo)
        {
            grid.Children.Add(new Border
            {
                Width = 56, Height = 56, CornerRadius = new CornerRadius(28), Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "▶", FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) },
            });
        }

        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 14, 10) };
        label.Children.Add(new TextBlock { Text = file.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        label.Children.Add(new TextBlock { Text = file.Taken.ToString("dd/MM/yyyy  HH:mm"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD2)) });
        grid.Children.Add(new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(0, 26, 0, 0),
            Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xD0, 0, 0, 0), 90),
            Child = label,
        });

        var button = new Button { Style = (Style)FindResource("MediaTile"), Content = grid };
        button.Click += (_, _) => OpenMediaViewer(index);
        return button;
    }

    private async Task LoadTileAsync(Image target, MediaFile file)
    {
        await _thumbGate.WaitAsync();
        BitmapSource? bitmap;
        try { bitmap = await Task.Run(() => LoadThumbnail(file)); }
        finally { _thumbGate.Release(); }
        if (bitmap is not null) target.Source = bitmap;
    }

    private static BitmapSource? LoadThumbnail(MediaFile file)
    {
        try
        {
            if (file.IsVideo) return ShellThumbnail.Get(file.Path, 560, 315);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 560;
            bitmap.UriSource = new Uri(file.Path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void FocusMedia(int index = 0)
    {
        var tiles = MediaGrid.Children.OfType<Button>().ToList();
        if (tiles.Count == 0) return;
        tiles[Math.Clamp(index, 0, tiles.Count - 1)].Focus();
    }

    // ---- The viewer: photo or video over the whole screen -----------------------------------

    private void OpenMediaViewer(int index)
    {
        if (index < 0 || index >= _media.Count) return;
        MediaViewer.Visibility = Visibility.Visible;
        MediaViewer.Focus();
        UiSound.Play("menu_open");
        ShowViewerItem(index);
    }

    private void CloseMediaViewer()
    {
        StopVideo();
        _viewerGen++;
        _viewerBarTimer.Stop();
        _viewerMessageTimer.Stop();
        MediaViewer.Visibility = Visibility.Collapsed;
        ViewerImage.Source = null;
        FocusMedia(_mediaIndex);
    }

    private void ShowViewerItem(int index)
    {
        _mediaIndex = index;
        var file = _media[index];
        var generation = ++_viewerGen;

        StopVideo();
        ViewerMessage.Visibility = Visibility.Collapsed;
        ViewerTitle.Text = $"{file.Title}   ·   {file.Taken:dd/MM/yyyy HH:mm}";
        ViewerCounter.Text = $"{index + 1} / {_media.Count}";
        ViewerHelp.Text = ViewerHelpText(file.IsVideo);
        _videoEnded = false;
        _videoFailed = false;

        if (file.IsVideo)
        {
            ViewerImage.Source = null;
            ViewerImage.Visibility = Visibility.Collapsed;
            ViewerVideo.Visibility = Visibility.Visible;
            ViewerTimeline.Visibility = Visibility.Visible;
            ViewerVideo.Volume = _viewerVolume;
            ViewerVideo.Source = new Uri(file.Path);
            ViewerVideo.Play();
            _videoPlaying = true;
            _viewerTimeline.Start();
            UpdateViewerTimeline();
        }
        else
        {
            ViewerVideo.Visibility = Visibility.Collapsed;
            ViewerTimeline.Visibility = Visibility.Collapsed;
            ViewerImage.Visibility = Visibility.Visible;
            ViewerImage.Source = null;
            _ = LoadViewerPhotoAsync(file, generation);
        }
        PokeViewerBar();
    }

    private async Task LoadViewerPhotoAsync(MediaFile file, int generation)
    {
        var bitmap = await Task.Run(() =>
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(file.Path);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        });

        if (generation != _viewerGen) return; // the user already moved on
        if (bitmap is null) ShowViewerMessage("Não foi possível abrir esta imagem.", persistent: true);
        else ViewerImage.Source = bitmap;
    }

    private string ViewerHelpText(bool video)
    {
        var pad = _padHints;
        if (video)
            return pad
                ? "A  Pausar / continuar      ◀ ▶  Voltar / avançar 10 s      ▲ ▼  Volume      LB / RB  Anterior / próximo      B  Fechar"
                : "Espaço  Pausar / continuar      ← →  Voltar / avançar 10 s      ↑ ↓  Volume      PgUp / PgDn  Anterior / próximo      Esc  Fechar";
        return pad
            ? "◀ ▶  ou  LB / RB  Anterior / próxima      B  Fechar"
            : "← →  Anterior / próxima      Esc  Fechar";
    }

    private void StopVideo()
    {
        _viewerTimeline.Stop();
        _videoPlaying = false;
        _videoDuration = TimeSpan.Zero;
        try
        {
            ViewerVideo.Stop();
            ViewerVideo.Source = null; // lets go of the file
        }
        catch (Exception) { }
        ViewerFill.Width = 0;
    }

    private void StepViewer(int delta)
    {
        var next = _mediaIndex + delta;
        if (next < 0 || next >= _media.Count) return;
        ShowViewerItem(next);
    }

    private void ToggleVideo()
    {
        if (!_media[_mediaIndex].IsVideo || _videoFailed) return;
        if (_videoEnded)
        {
            _videoEnded = false;
            ViewerMessage.Visibility = Visibility.Collapsed;
            ViewerVideo.Position = TimeSpan.Zero;
            ViewerVideo.Play();
            _videoPlaying = true;
        }
        else if (_videoPlaying)
        {
            ViewerVideo.Pause();
            _videoPlaying = false;
            ShowViewerMessage("⏸", persistent: false);
        }
        else
        {
            ViewerVideo.Play();
            _videoPlaying = true;
            ShowViewerMessage("▶", persistent: false);
        }
        PokeViewerBar();
    }

    private void SeekVideo(int seconds)
    {
        if (!_media[_mediaIndex].IsVideo || _videoFailed || _videoDuration <= TimeSpan.Zero) return;
        var target = ViewerVideo.Position + TimeSpan.FromSeconds(seconds);
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (target >= _videoDuration) target = _videoDuration - TimeSpan.FromMilliseconds(300);
        ViewerVideo.Position = target;
        _videoEnded = false;
        if (ViewerMessage.Visibility == Visibility.Visible && !_videoFailed) ViewerMessage.Visibility = Visibility.Collapsed;
        UpdateViewerTimeline();
        PokeViewerBar(showAlways: true);
    }

    private void ChangeViewerVolume(double delta)
    {
        if (!_media[_mediaIndex].IsVideo) return;
        _viewerVolume = Math.Clamp(Math.Round(_viewerVolume + delta, 1), 0, 1);
        ViewerVideo.Volume = _viewerVolume;
        ShowViewerMessage($"Volume {(int)Math.Round(_viewerVolume * 100)}%", persistent: false);
    }

    private void UpdateViewerTimeline()
    {
        if (!MediaViewerOpen || ViewerTimeline.Visibility != Visibility.Visible) return;
        var position = ViewerVideo.Source is null ? TimeSpan.Zero : ViewerVideo.Position;
        ViewerTime.Text = FormatSpan(position);
        ViewerDuration.Text = FormatSpan(_videoDuration);
        var ratio = _videoDuration > TimeSpan.Zero ? Math.Clamp(position.TotalSeconds / _videoDuration.TotalSeconds, 0, 1) : 0;
        ViewerFill.Width = ViewerTrack.ActualWidth * ratio;
    }

    private static string FormatSpan(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";

    private void ShowViewerMessage(string text, bool persistent)
    {
        ViewerMessage.Text = text;
        ViewerMessage.Visibility = Visibility.Visible;
        _viewerMessageTimer.Stop();
        if (!persistent) _viewerMessageTimer.Start();
    }

    /// <summary>Shows the info bar; while a video plays it fades away again after a few seconds.</summary>
    private void PokeViewerBar(bool showAlways = false)
    {
        ViewerBar.Visibility = Visibility.Visible;
        _viewerBarTimer.Stop();
        if (_videoPlaying || showAlways) _viewerBarTimer.Start();
    }

    private void OnViewerVideoOpened(object sender, RoutedEventArgs e)
    {
        _videoDuration = ViewerVideo.NaturalDuration.HasTimeSpan ? ViewerVideo.NaturalDuration.TimeSpan : TimeSpan.Zero;
        UpdateViewerTimeline();
    }

    private void OnViewerVideoEnded(object sender, RoutedEventArgs e)
    {
        _videoPlaying = false;
        _videoEnded = true;
        ViewerVideo.Pause();
        ShowViewerMessage("Fim do vídeo", persistent: true);
        PokeViewerBar();
        _viewerBarTimer.Stop(); // keep the bar while the video is over
    }

    private void OnViewerVideoFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _videoPlaying = false;
        _videoFailed = true;
        _viewerTimeline.Stop();
        ShowViewerMessage("Não foi possível reproduzir este vídeo.\nO Windows pode estar sem o codec dele.", persistent: true);
        PokeViewerBar();
        _viewerBarTimer.Stop();
    }

    // ---- Input while the viewer is open -----------------------------------------------------

    private void HandleViewerButton(GamepadButton button)
    {
        var video = _media[_mediaIndex].IsVideo;
        switch (button)
        {
            case GamepadButton.B: UiSound.Play("back"); CloseMediaViewer(); break;
            case GamepadButton.A: ToggleVideo(); break;
            case GamepadButton.Left: if (video) SeekVideo(-10); else StepViewer(-1); break;
            case GamepadButton.Right: if (video) SeekVideo(+10); else StepViewer(+1); break;
            case GamepadButton.Up: ChangeViewerVolume(+0.1); break;
            case GamepadButton.Down: ChangeViewerVolume(-0.1); break;
            case GamepadButton.LeftShoulder: StepViewer(-1); break;
            case GamepadButton.RightShoulder: StepViewer(+1); break;
        }
        if (button is not GamepadButton.B) PokeViewerBar(showAlways: true);
    }

    private void OnViewerPreviewKey(object sender, KeyEventArgs e)
    {
        if (!MediaViewerOpen) return;
        var video = _media[_mediaIndex].IsVideo;
        switch (e.Key)
        {
            case Key.Escape: UiSound.Play("back"); CloseMediaViewer(); break;
            case Key.Space or Key.Enter: ToggleVideo(); break;
            case Key.Left: if (video) SeekVideo(-10); else StepViewer(-1); break;
            case Key.Right: if (video) SeekVideo(+10); else StepViewer(+1); break;
            case Key.Up: ChangeViewerVolume(+0.1); break;
            case Key.Down: ChangeViewerVolume(-0.1); break;
            case Key.PageUp: StepViewer(-1); break;
            case Key.PageDown: StepViewer(+1); break;
            case Key.F11: return; // full screen toggle keeps working
            default: e.Handled = true; return;
        }
        e.Handled = true;
        if (MediaViewerOpen) PokeViewerBar(showAlways: true);
    }
}
