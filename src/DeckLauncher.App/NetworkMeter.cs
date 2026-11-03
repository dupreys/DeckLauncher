using System.Net.NetworkInformation;

namespace DeckLauncher.App;

/// <summary>
/// Measures what the PC receives from the network, second by second. While Steam downloads a game it is by far the
/// main traffic, so this gives the live download speed that Steam only writes to disk every now and then.
/// </summary>
internal sealed class NetworkMeter
{
    private static readonly string[] VirtualMarkers = { "vethernet", "virtual", "vmware", "hyper-v", "loopback", "bluetooth", "tap-", "wsl" };

    private long _lastTotal;
    private DateTime _lastAt;
    private bool _started;

    /// <summary>Smoothed speed in bytes per second.</summary>
    public double BytesPerSecond { get; private set; }

    /// <summary>Forgets the previous reading (call when nobody is downloading, so the next sample starts clean).</summary>
    public long Reset()
    {
        _started = false;
        BytesPerSecond = 0;
        return 0;
    }

    /// <summary>Bytes received since the previous call (0 on the first one); also updates <see cref="BytesPerSecond"/>.</summary>
    public long Sample()
    {
        var total = TotalReceived();
        var now = DateTime.UtcNow;
        if (!_started)
        {
            _started = true;
            _lastTotal = total;
            _lastAt = now;
            return 0;
        }

        var delta = Math.Max(0, total - _lastTotal);
        var seconds = Math.Max(0.2, (now - _lastAt).TotalSeconds);
        var instant = delta / seconds;
        BytesPerSecond = BytesPerSecond <= 0 ? instant : BytesPerSecond * 0.5 + instant * 0.5;
        _lastTotal = total;
        _lastAt = now;
        return delta;
    }

    private static long TotalReceived()
    {
        long total = 0;
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var name = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
                if (VirtualMarkers.Any(name.Contains)) continue; // a virtual switch would count the same bytes twice
                try { total += adapter.GetIPv4Statistics().BytesReceived; }
                catch (NetworkInformationException) { }
            }
        }
        catch (NetworkInformationException) { }
        return total;
    }
}
