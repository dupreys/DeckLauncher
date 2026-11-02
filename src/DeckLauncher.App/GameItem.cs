using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiteLauncher.Core;

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

    public Game Game { get; }

    public string Name => Game.Name;

    public string Source => Game.Source;

    public bool IsFavorite => App.Settings.Favorites.Contains(GameSorter.Key(Game));

    /// <summary>Portrait cover (library grid and narrow home tiles).</summary>
    public ImageSource? Cover => _cover;

    /// <summary>Wide picture shown when a home tile is focused: the background, or the cover when there is none.</summary>
    public ImageSource? Wide => _wide;

    public ImageSource? Logo => _logo;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>True when nothing the tile shows has changed, so the same item can be reused after a refresh.</summary>
    public bool Matches(Game other) =>
        Game.Name == other.Name && Game.CoverPath == other.CoverPath && Game.BackdropPath == other.BackdropPath && Game.LogoPath == other.LogoPath;

    public void EnsureCoverLoaded()
    {
        if (_coverRequested || Game.CoverPath is null) return;
        _coverRequested = true;
        _ = LoadAsync(Game.CoverPath, 300, image => { _cover = image; Raise(nameof(Cover)); });
    }

    public void EnsureWideLoaded()
    {
        EnsureCoverLoaded();
        if (_wideRequested) return;
        _wideRequested = true;
        var path = Game.BackdropPath ?? Game.CoverPath;
        if (path is not null) _ = LoadAsync(path, 1000, image => { _wide = image; Raise(nameof(Wide)); });
        if (Game.LogoPath is { } logo) _ = LoadAsync(logo, 600, image => { _logo = image; Raise(nameof(Logo)); });
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
    /// <summary>Decodes an image on a pool thread at a reduced size and freezes it for cross-thread use.</summary>
    public static Task<ImageSource?> LoadAsync(string path, int decodeWidth) => Task.Run<ImageSource?>(() =>
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
    });
}
