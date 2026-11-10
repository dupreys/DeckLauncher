using System.IO;
using System.Text.Json;

namespace DeckLauncher.App;

/// <summary>
/// Which artwork downloads were already tried (and found nothing, or finished), kept on disk for a few days so the launcher does not
/// ask the same servers for the same missing pictures at every start.
/// </summary>
internal sealed class ArtTriedStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(3);
    private readonly string _file;
    private Dictionary<string, long> _map = new();
    private bool _loaded;
    private bool _dirty;

    public ArtTriedStore(string file) => _file = file;

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(_file) && JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(_file)) is { } map)
                _map = map.Where(p => Now - p.Value < Ttl.TotalSeconds).ToDictionary(p => p.Key, p => p.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _map = new(); // unreadable: everything is tried once more
        }
    }

    public bool Contains(string key)
    {
        Load();
        return _map.TryGetValue(key, out var at) && Now - at < Ttl.TotalSeconds;
    }

    /// <summary>True when the key was not tried recently (it is now marked as tried).</summary>
    public bool Add(string key)
    {
        Load();
        if (Contains(key)) return false;
        _map[key] = Now;
        _dirty = true;
        return true;
    }

    public void Clear()
    {
        _map = new();
        _loaded = true;
        _dirty = false;
        try { if (File.Exists(_file)) File.Delete(_file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the cache folder is being removed anyway */ }
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temp = _file + ".part";
            File.WriteAllText(temp, JsonSerializer.Serialize(_map));
            File.Move(temp, _file, overwrite: true);
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* saved with the next batch */ }
    }
}
