using System.IO;
using System.Windows.Threading;
using LiteLauncher.Core;
using LiteLauncher.Core.Steam;

namespace DeckLauncher.App;

// Play time: the launcher watches the running programs and counts the time a game's program is open, whether it was
// started from here or not. Every game gets a session in the history (the "last two weeks" text under the tile and the
// play time screen read it, and it grows while the game runs); only games outside Steam also get their total minutes here,
// because Steam keeps its own total.
public partial class MainWindow
{
    private readonly PlayTracker _playTracker = new();
    private readonly DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, double> _playCarry = new();
    private readonly Dictionary<string, PlaySession> _liveSessions = new();
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
            var games = _all.Select(i => i.Game).ToList();
            if (games.Count == 0 && _playTracker.Running.Count == 0) return;

            var now = DateTime.UtcNow;
            var elapsed = Math.Min((now - _lastPlayTick).TotalSeconds, 60);
            _lastPlayTick = now;

            var paths = await Task.Run(Native.RunningImagePaths);
            var running = RunningGameKeys(games, paths);
            var (started, stopped) = _playTracker.Update(running, DateTimeOffset.Now);
            var names = games.ToDictionary(GameSorter.Key, g => g.Name);
            var changed = false;

            var touched = new HashSet<string>();
            foreach (var item in _all)
            {
                var key = GameSorter.Key(item.Game);
                var isRunning = running.Contains(key);
                if (item.IsRunning == isRunning) continue;
                item.IsRunning = isRunning; // "JOGANDO AGORA" appears and disappears with the game
                touched.Add(key);
            }

            foreach (var key in started)
            {
                RecordFor(key).LastPlayed = DateTimeOffset.Now;
                changed = true;
            }

            foreach (var key in running)
            {
                _playCarry.TryGetValue(key, out var carry);
                carry += elapsed;
                var whole = (int)(carry / 60);
                if (whole > 0)
                {
                    if (!key.StartsWith(SteamProvider.ProviderName + ":", StringComparison.Ordinal)) RecordFor(key).Minutes += whole;
                    AddToLiveSession(key, names.TryGetValue(key, out var gameName) ? gameName : key, whole);
                    carry -= whole * 60;
                    touched.Add(key);
                    changed = true;
                }
                _playCarry[key] = carry;
            }

            foreach (var (key, _) in stopped)
            {
                _liveSessions.Remove(key); // the session was written minute by minute while it ran
                _playCarry.Remove(key);
            }

            if (touched.Count > 0) RefreshRecentText(touched);

            if (!changed) return;
            SaveSettings();
            if (_page == "Jogo" && _gameItem is { } open) StatTime.Text = PlaytimeText(open.Game);
        }
        finally
        {
            _playChecking = false;
        }
    }

    /// <summary>The running game's session in the history grows by the minutes that passed (a new session starts after a stop).</summary>
    private void AddToLiveSession(string key, string name, int minutes)
    {
        if (_liveSessions.TryGetValue(key, out var session) && App.Settings.PlaySessions.Contains(session))
        {
            session.Minutes = Math.Min(session.Minutes + minutes, 24 * 60);
            return;
        }
        if (!PlayHistory.Add(App.Settings.PlaySessions, key, name, DateTimeOffset.Now - TimeSpan.FromMinutes(minutes), minutes)) return;
        _liveSessions[key] = App.Settings.PlaySessions[^1];
    }

    /// <summary>Refreshes the "last two weeks" text of the games that were just played.</summary>
    private void RefreshRecentText(HashSet<string> keys)
    {
        foreach (var item in _all.Where(i => keys.Contains(GameSorter.Key(i.Game))))
        {
            item.RefreshRecent();
            if (ReferenceEquals(item, _heroItem)) CaptionSub.Text = item.RecentText;
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
