using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeckLauncher.Core;

namespace DeckLauncher.App;

/// <summary>View model for one game tile. Pictures load lazily, off the UI thread.</summary>
public sealed class GameItem : INotifyPropertyChanged
{
    private ImageSource? _cover;
    private ImageSource? _wide;
    private ImageSource? _logo;
    private bool _coverRequested;
    private bool _wideRequested;

    public GameItem(Game game) => Game = game;

    public Game Game { get; private set; }

    public string Name => Game.Name;

    public string Source => Game.Source;

    /// <summary>An owned game that is not installed: its tile is shown dimmed.</summary>
    public bool IsNotInstalled => Game.NotInstalled;

    public bool IsFavorite => App.Settings.Favorites.Contains(GameSorter.Key(Game));

    /// <summary>Portrait cover (library grid and narrow home tiles).</summary>
    public ImageSource? Cover => _cover;

    /// <summary>Wide picture shown when a home tile is focused: the background, or the cover when there is none.</summary>
    public ImageSource? Wide => _wide;

    public ImageSource? Logo => _logo;

    /// <summary>"ÚLTIMAS DUAS SEMANAS: 13,1 H": time played through the launcher in the last 14 days.</summary>
    public string RecentText
    {
        get
        {
            var key = GameSorter.Key(Game);
            var cutoff = DateTimeOffset.Now.AddDays(-14);
            var minutes = App.Settings.PlaySessions.Where(s => s.Key == key && s.Start >= cutoff).Sum(s => s.Minutes);
            var prefix = IsRunning ? "JOGANDO AGORA" : "";
            if (minutes <= 0) return IsRunning ? prefix : "NÃO JOGADO NAS ÚLTIMAS DUAS SEMANAS";
            var text = minutes < 60
                ? $"ÚLTIMAS DUAS SEMANAS: {minutes} MIN"
                : $"ÚLTIMAS DUAS SEMANAS: {(minutes / 60.0).ToString("0.0", UiText.Culture)} H";
            return IsRunning ? $"{prefix}  ·  {text}" : text;
        }
    }

    /// <summary>True while the game's program is open (set by the play monitor).</summary>
    public bool IsRunning { get; set; }

    /// <summary>Tells the tile (and the caption under it) that the recent play time changed.</summary>
    public void RefreshRecent() => Raise(nameof(RecentText));

    private bool _featured;

    /// <summary>The last played game on the home row: the only tile shown wide, with its background and logo.</summary>
    public bool IsFeatured
    {
        get => _featured;
        set
        {
            if (_featured == value) return;
            _featured = value;
            Raise(nameof(IsFeatured));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>True when nothing the tile shows has changed, so the same item can be reused after a refresh.</summary>
    public bool Matches(Game other) =>
        Game.NotInstalled == other.NotInstalled && Game.ExecutablePath == other.ExecutablePath && Game.InstallPath == other.InstallPath
        && Game.Name == other.Name && Game.CoverPath == other.CoverPath && Game.BackdropPath == other.BackdropPath && Game.LogoPath == other.LogoPath;

    /// <summary>Same tile (name, state, folder), only the pictures differ: the item can take the new pictures instead of being replaced, so the lists are not rebuilt.</summary>
    public bool SameTile(Game other) =>
        Game.NotInstalled == other.NotInstalled && Game.ExecutablePath == other.ExecutablePath && Game.InstallPath == other.InstallPath && Game.Name == other.Name;

    /// <summary>Takes new picture paths (UI thread). A picture the tile already showed is loaded again from the new file.</summary>
    public void UpdateArt(Game other)
    {
        var coverChanged = other.CoverPath != Game.CoverPath;
        var wideChanged = (other.BackdropPath ?? other.CoverPath) != (Game.BackdropPath ?? Game.CoverPath);
        var logoChanged = other.LogoPath != Game.LogoPath;
        var hadCover = _coverRequested;
        var hadWide = _wideRequested;
        Game = other;
        if (coverChanged) { _coverRequested = false; if (hadCover) EnsureCoverLoaded(); }
        if (wideChanged || logoChanged) { _wideRequested = false; if (hadWide) EnsureWideLoaded(); }
    }

    public void EnsureCoverLoaded()
    {
        if (_coverRequested || Game.CoverPath is null) return;
        _coverRequested = true;
        var cover = Game.CoverPath;
        _ = LoadAsync(cover, 300, image => { if (Game.CoverPath != cover) return; _cover = image; Raise(nameof(Cover)); }); // an older picture arriving late is dropped
    }

    public void EnsureWideLoaded()
    {
        EnsureCoverLoaded();
        if (_wideRequested) return;
        _wideRequested = true;
        var path = Game.BackdropPath ?? Game.CoverPath;
        if (path is not null) _ = LoadAsync(path, 1000, image => { if ((Game.BackdropPath ?? Game.CoverPath) != path) return; _wide = image; Raise(nameof(Wide)); });
        if (Game.LogoPath is { } logo) _ = LoadAsync(logo, 600, image => { if (Game.LogoPath != logo) return; _logo = image; Raise(nameof(Logo)); });
    }

    /// <summary>The full-size background for the screen behind the tiles; null when the game has none.</summary>
    public Task<ImageSource?> LoadBackdropAsync() =>
        Game.BackdropPath is { } path ? ImageLoader.LoadAsync(path, 1920) : Task.FromResult<ImageSource?>(null);

    private static async Task LoadAsync(string path, int width, Action<ImageSource?> apply)
    {
        var image = await ImageLoader.LoadAsync(path, width);
        if (image is not null) apply(image);
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class ImageLoader
{
    // A big library asks for hundreds of pictures at once: decode a few at a time so the interface and the pool threads stay free.
    private static readonly SemaphoreSlim Gate = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));

    /// <summary>Decodes an image on a pool thread at a reduced size and freezes it for cross-thread use.</summary>
    public static async Task<ImageSource?> LoadAsync(string path, int decodeWidth)
    {
        await Gate.WaitAsync();
        try { return await Task.Run(() => Decode(path, decodeWidth)); }
        finally { Gate.Release(); }
    }

    private static ImageSource? Decode(string path, int decodeWidth)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = decodeWidth; // decode small: keeps RAM low for big libraries
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            return null; // corrupt or half-written image: the tile shows the game's name instead
        }
    }
}
