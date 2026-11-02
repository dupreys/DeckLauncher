using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeckLauncher.App;

internal static class RemoteImage
{
    private static readonly Dictionary<string, ImageSource> Cache = new();

    /// <summary>Downloads and decodes a small picture off the UI thread. Null on any failure.</summary>
    public static async Task<ImageSource?> LoadAsync(HttpClient http, string? url, int decodeWidth)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var cacheKey = $"{decodeWidth}|{url}";
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;
        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            var image = await Task.Run<ImageSource?>(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.DecodePixelWidth = decodeWidth;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            });
            if (image is not null && Cache.Count < 300) Cache[cacheKey] = image;
            return image;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or IOException or InvalidOperationException)
        {
            return null; // the tile just stays empty
        }
    }
}
