using System.Windows.Threading;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

// Quick notices (the toast at the bottom of the launcher): low battery and a Steam download that finished.
// They only show while the launcher is in front; nothing is shown over a game.
public partial class MainWindow
{
    private static readonly int[] BatteryLevels = { 20, 10, 5 };
    private readonly DispatcherTimer _alertTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private Dictionary<string, string> _knownDownloads = new();
    private int _batteryWarned = 101;
    private bool _alertBusy;

    private void InitAlerts()
    {
        _alertTimer.Tick += async (_, _) => await CheckAlertsAsync();
        _alertTimer.Start();
    }

    private async Task CheckAlertsAsync()
    {
        if (_alertBusy) return;
        _alertBusy = true;
        try
        {
            CheckBattery();
            await CheckFinishedDownloadsAsync();
        }
        finally
        {
            _alertBusy = false;
        }
    }

    /// <summary>One warning at 20 %, another at 10 % and 5 %; it starts over once the PC is plugged in or the charge is back above 25 %.</summary>
    private void CheckBattery()
    {
        if (Native.Battery() is not { } battery) return;
        if (battery.Charging || battery.Percent > 25)
        {
            _batteryWarned = 101;
            return;
        }
        var level = BatteryLevels.Where(l => battery.Percent <= l).DefaultIfEmpty(0).Min();
        if (level == 0 || level >= _batteryWarned) return;
        _batteryWarned = level;
        if (IsActive) ShowToast($"Bateria baixa: {battery.Percent}%. Ligue o carregador.");
    }

    /// <summary>The Downloads page already announces finished downloads while it is open; this covers the rest of the time.</summary>
    private async Task CheckFinishedDownloadsAsync()
    {
        if (_library.SteamPath is not { } steamPath) return;
        if (_page == "Downloads")
        {
            _knownDownloads = new Dictionary<string, string>(_activeDownloadNames); // the page is watching: stay in step with it
            return;
        }

        var read = await Task.Run(() => SteamDownloads.Read(steamPath).ToList());
        var vanished = _knownDownloads.Where(p => read.All(d => d.AppId != p.Key)).ToList();
        _knownDownloads = read.ToDictionary(d => d.AppId, d => d.Name);
        if (vanished.Count == 0) return;

        var installed = await Task.Run(() => SteamLibraryReader.ReadInstalledGames(steamPath).Select(g => g.Id).ToHashSet());
        foreach (var (id, name) in vanished)
            if (installed.Contains(id))
            {
                ShowToast($"{name} terminou de baixar");
                AddNotification($"{name} terminou de baixar.");
                UpdateBell();
            }
        _ = RefreshAsync();
    }
}
