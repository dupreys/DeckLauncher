namespace DeckLauncher.App;

public enum ArtItem { Cover, Logo, Wallpaper }

/// <summary>What the artwork downloads (covers, logos, wallpapers) are doing right now, as one running total for the notification bell.</summary>
public sealed class ArtProgress
{
    private readonly int[] _total = new int[3];
    private readonly int[] _done = new int[3];
    private readonly int[] _saved = new int[3];

    public int Total => _total.Sum();
    public int Done => _done.Sum();
    public bool Active => Done < Total;

    /// <summary>Images saved so far, by kind.</summary>
    public int Covers => _saved[0];
    public int Logos => _saved[1];
    public int Wallpapers => _saved[2];
    public int Saved => _saved.Sum();

    public int TotalOf(ArtItem kind) => _total[(int)kind];
    public int DoneOf(ArtItem kind) => _done[(int)kind];

    /// <summary>"Capas 3/10 · Logos 5/17 · Wallpapers 7/17" (only the kinds that are being downloaded).</summary>
    public string Breakdown()
    {
        var parts = new List<string>();
        if (_total[0] > 0) parts.Add($"Capas {_done[0]}/{_total[0]}");
        if (_total[1] > 0) parts.Add($"Logos {_done[1]}/{_total[1]}");
        if (_total[2] > 0) parts.Add($"Wallpapers {_done[2]}/{_total[2]}");
        return string.Join(" · ", parts);
    }

    /// <summary>A batch of downloads starts (the counters restart when nothing was running).</summary>
    public event Action? Started;
    public event Action? Changed;
    /// <summary>Everything that was queued is done.</summary>
    public event Action? Finished;

    private readonly object _gate = new(); // the downloads report from several threads

    public ArtJob Begin(int covers, int logos, int wallpapers)
    {
        var counts = new[] { covers, logos, wallpapers };
        if (counts.Sum() > 0)
        {
            bool fresh;
            lock (_gate)
            {
                fresh = !Active;
                if (fresh) { Array.Clear(_total); Array.Clear(_done); Array.Clear(_saved); }
                for (var i = 0; i < 3; i++) _total[i] += counts[i];
            }
            if (fresh) Started?.Invoke();
            Changed?.Invoke();
        }
        return new ArtJob(this, counts);
    }

    internal void Step(ArtItem kind, bool saved)
    {
        bool finished;
        lock (_gate)
        {
            var i = (int)kind;
            if (_done[i] >= _total[i]) _total[i]++; // more work than counted
            _done[i]++;
            if (saved) _saved[i]++;
            finished = !Active;
        }
        Changed?.Invoke();
        if (finished) Finished?.Invoke();
    }

    internal void Skip(int[] counts)
    {
        var any = false;
        bool finished;
        lock (_gate)
        {
            for (var i = 0; i < 3; i++)
            {
                if (counts[i] <= 0) continue;
                _done[i] = Math.Min(_total[i], _done[i] + counts[i]);
                any = true;
            }
            finished = !Active;
        }
        if (!any) return;
        Changed?.Invoke();
        if (finished) Finished?.Invoke();
    }
}

/// <summary>One caller's share of the downloads: it reports each finished image and releases whatever it did not need.</summary>
public sealed class ArtJob
{
    private readonly ArtProgress _progress;
    private readonly int[] _left;

    internal ArtJob(ArtProgress progress, int[] counts)
    {
        _progress = progress;
        _left = (int[])counts.Clone();
    }

    public async Task<bool> RunAsync(ArtItem kind, Func<Task<string?>> download)
    {
        string? path = null;
        try { path = await download(); }
        finally
        {
            lock (_left) { if (_left[(int)kind] > 0) _left[(int)kind]--; }
            _progress.Step(kind, path is not null);
        }
        return path is not null;
    }

    public void End()
    {
        int[] rest;
        lock (_left) { rest = (int[])_left.Clone(); Array.Clear(_left); }
        _progress.Skip(rest);
    }
}
