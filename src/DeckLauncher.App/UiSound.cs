using System.IO;
using System.Media;
using System.Windows;

namespace DeckLauncher.App;

/// <summary>Short interface sounds (generated WAV files in Assets/Sounds), scaled by the volume in the settings.</summary>
internal static class UiSound
{
    private static readonly Dictionary<string, byte[]> Cache = new();
    private static SoundPlayer? _current; // keep a reference while it plays

    public static void Play(string name)
    {
        if (!App.Settings.UiSounds) return;
        try
        {
            var wav = Load(name);
            if (wav is null) return;
            var scaled = Scale(wav, Math.Clamp(App.Settings.UiVolume * 1.6, 0.0, 1.5));
            _current = new SoundPlayer(new MemoryStream(scaled));
            _current.Play();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UriFormatException or UnauthorizedAccessException)
        {
            // sound is optional: never break the launcher because of it
        }
    }

    private static byte[]? Load(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;
        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Sounds/{name}.wav", UriKind.Absolute));
        if (info is null) return null;
        using var stream = info.Stream;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Cache[name] = copy.ToArray();
    }

    /// <summary>16-bit PCM with a 44-byte header: multiply every sample by <paramref name="factor"/>.</summary>
    private static byte[] Scale(byte[] wav, double factor)
    {
        var result = (byte[])wav.Clone();
        for (var i = 44; i + 1 < result.Length; i += 2)
        {
            var sample = (short)(result[i] | (result[i + 1] << 8));
            var value = (short)Math.Clamp((int)Math.Round(sample * factor), short.MinValue, short.MaxValue);
            result[i] = (byte)(value & 0xFF);
            result[i + 1] = (byte)((value >> 8) & 0xFF);
        }
        return result;
    }
}
