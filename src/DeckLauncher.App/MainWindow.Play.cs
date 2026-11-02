using System.IO;
using System.Windows.Threading;
using LiteLauncher.Core;
using LiteLauncher.Core.Steam;

namespace DeckLauncher.App;

// Play time for games outside Steam (Epic and added games): the launcher watches the running programs and
// counts the time a game's program is open, whether it was started from here or not.
public partial class MainWindow
{
    private readonly PlayTracker _playTracker = new();
    private readonly DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, double> _playCarry = new();
    private DateTime _lastPlayTick = DateTime.UtcNow;
    private bool _playChecking;

    private void InitPlayMonitor()
    {
        _playTimer.Tick += async (_, _) => await CheckPlayingAsync();
        _playTimer.Start();
    }

    private async Task CheckPlayingAsync()
    {
        if (_playChecking) return;
        _playChecking = true;
        try
        {
            var games = _all.Select(i => i.Game).Where(g => g.Source != SteamProvider.ProviderName).ToList();
            if (games.Count == 0 && _playTracker.Running.Count == 0) return;

            var now = DateTime.UtcNow;
            var elapsed = Math.Min((now - _lastPlayTick).TotalSeconds, 60);
            _lastPlayTick = now;

            var paths = await Task.Run(Native.RunningImagePaths);
            var running = RunningGameKeys(games, paths);
            var (started, stopped) = _playTracker.Update(running, DateTimeOffset.Now);
            var names = games.ToDictionary(GameSorter.Key, g => g.Name);
            var changed = false;

            foreach (var key in started)
            {
                var record = RecordFor(key);
                record.LastPlayed = DateTimeOffset.Now;
                changed = true;
            }

            foreach (var key in running)
            {
                _playCarry.TryGetValue(key, out var carry);
                carry += elapsed;
                var whole = (int)(carry / 60);
                if (whole > 0)
                {
                    RecordFor(key).Minutes += whole;
                    carry -= whole * 60;
                    changed = true;
                }
                _playCarry[key] = carry;
            }

            foreach (var (key, minutes) in stopped)
            {
                names.TryGetValue(key, out var name);
                PlayHistory.Add(App.Settings.PlaySessions, key, name ?? key, DateTimeOffset.Now - TimeSpan.FromMinutes(minutes), minutes);
                _playCarry.Remove(key);
                changed = true;
            }

            if (!changed) return;
            SaveSettings();
            if (_page == "Jogo" && _gameItem is { } open) StatTime.Text = PlaytimeText(open.Game);
        }
        finally
        {
            _playChecking = false;
        }
    }

    private static PlayRecord RecordFor(string key)
    {
        if (!App.Settings.PlayRecords.TryGetValue(key, out var record))
            App.Settings.PlayRecords[key] = record = new PlayRecord();
        return record;
    }

    /// <summary>The games whose program (or any program inside the game's own folder) is running.</summary>
    private static HashSet<string> RunningGameKeys(IReadOnlyList<Game> games, IReadOnlyList<string> paths)
    {
        var result = new HashSet<string>();
        foreach (var game in games)
        {
            string? prefix = null;
            try
            {
                if (ProcessScope.Resolve(game) is { } folder)
                    prefix = Path.GetFullPath(folder).TrimEnd('\\', '/') + "\\";
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }

            foreach (var path in paths)
            {
                var sameExe = game.ExecutablePath is { Length: > 0 } exe && string.Equals(path, exe, StringComparison.OrdinalIgnoreCase);
                if (sameExe || (prefix is not null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(GameSorter.Key(game));
                    break;
                }
            }
        }
        return result;
    }
}
