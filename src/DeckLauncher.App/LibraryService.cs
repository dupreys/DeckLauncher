using System.ComponentModel;
using System.IO;
using System.Net.Http;
using DeckLauncher.Core;
using DeckLauncher.Core.Epic;
using DeckLauncher.Core.Manual;
using DeckLauncher.Core.Settings;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

/// <summary>Finds the games of every store, applies the user's names and pictures, and downloads missing art.</summary>
internal sealed class LibraryService
{
    public static readonly HttpClient Http = CreateHttp();

    /// <summary>For the full-size pictures the user picks (a 4K wallpaper can take longer than the usual 15 seconds).</summary>
    public static readonly HttpClient DownloadHttp = CreateHttp(TimeSpan.FromSeconds(90));

    public static string ArtCacheDir => Path.Combine(SettingsStore.DefaultDirectory, "covers");

    private readonly IReadOnlyList<IGameProvider> _providers;

    public string? SteamPath { get; }
    private readonly HashSet<string> _artTried = new();

    public LibraryService()
    {
        var steamPath = SteamPath = SteamLocator.FindSteamPath();
        _providers = new IGameProvider[]
        {
            new SteamProvider(steamPath),
            new EpicProvider(),
            new ManualProvider(() => App.Settings.ManualGames),
        };
    }

    public bool EpicFound => _providers.Any(p => p.Name == EpicProvider.ProviderName && p.IsAvailable);

    /// <summary>Forget which downloads were tried this run, so a cleared cache is filled again.</summary>
    public void ResetArtAttempts() => _artTried.Clear();

    public bool AnyStoreFound => _providers.Any(p => p.Name != ManualProvider.ProviderName && p.IsAvailable);

    private static HttpClient CreateHttp(TimeSpan? timeout = null)
    {
        var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 64 * 1024 * 1024 }; // pictures and JSON only
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeckLauncher/0.2");
        return client;
    }

    public async Task<List<Game>> LoadAsync()
    {
        var games = new List<Game>();
        foreach (var provider in _providers.Where(p => p.IsAvailable))
        {
            try { games.AddRange(await provider.GetGamesAsync()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The store may be rewriting its files right now; the next refresh picks them up.
            }
        }

        games.RemoveAll(g => App.Settings.HiddenGames.ContainsKey(GameSorter.Key(g)));
        for (var i = 0; i < games.Count; i++)
            games[i] = WithCachedArt(PlayTracker.Apply(GameOverrides.Apply(games[i], App.Settings.GameOverrides), App.Settings.PlayRecords));
        return games;
    }

    // ---- Steam games that are owned but not installed -----------------------------------------

    private IReadOnlyList<OwnedGame>? _owned;
    private string? _ownedFor;
    private DateTime _ownedAt = DateTime.MinValue;

    /// <summary>Why the "not installed" list is empty: null (nothing wrong), "noid" (no Steam account found) or "fetch" (could not read the games).</summary>
    public string? NotInstalledProblem { get; private set; }

    /// <summary>Forget the owned-games list (the Web API key changed).</summary>
    public void ResetOwned() => _owned = null;

    /// <summary>The Steam games the account owns that are not installed on this PC. The owned list is kept for 15 minutes.</summary>
    public async Task<List<Game>> LoadNotInstalledAsync(IReadOnlyList<Game> installed, string? steamId)
    {
        NotInstalledProblem = null;
        if (string.IsNullOrWhiteSpace(steamId))
        {
            NotInstalledProblem = "noid";
            return new List<Game>();
        }

        if (_owned is null || _ownedFor != steamId || DateTime.UtcNow - _ownedAt > TimeSpan.FromMinutes(15))
        {
            var fetched = await SteamOwnedGames.FetchAsync(Http, steamId, App.Settings.SteamWebApiKey);
            if (fetched is null)
            {
                NotInstalledProblem = "fetch";
                return new List<Game>();
            }
            _owned = fetched;
            _ownedFor = steamId;
            _ownedAt = DateTime.UtcNow;
        }

        var ids = installed.Where(g => g.Source == SteamProvider.ProviderName).Select(g => g.Id).ToHashSet();
        var games = SteamOwnedGames.NotInstalled(_owned, ids, SteamPath).ToList();
        games.RemoveAll(g => App.Settings.HiddenGames.ContainsKey(GameSorter.Key(g)));
        for (var i = 0; i < games.Count; i++) games[i] = WithCachedArt(GameOverrides.Apply(games[i], App.Settings.GameOverrides));
        return games;
    }

    /// <summary>Downloads Steam's own cover for not-installed games that have none (at most 40 per run). True when something new was saved.</summary>
    public async Task<bool> DownloadNotInstalledCoversAsync(IReadOnlyList<Game> games)
    {
        var fetched = false;
        var tried = 0;
        foreach (var game in games.Where(g => g.CoverPath is null))
        {
            if (tried >= 40) break;
            if (!_artTried.Add(GameSorter.Key(game) + "#cover")) continue;
            tried++;
            if (await CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Cover) is not null) fetched = true;
        }
        return fetched;
    }

    /// <summary>Starts the game through its store. Returns false when that failed.</summary>
    public bool Launch(Game game)
    {
        var provider = _providers.FirstOrDefault(p => p.Name == game.Source);
        if (provider is null) return false;
        try
        {
            provider.Launch(game);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or ArgumentException or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>Uses artwork downloaded earlier when the store's own cache has none.</summary>
    private Game WithCachedArt(Game game)
    {
        var cover = game.CoverPath ?? CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Cover)
                    ?? SteamGridDbArt.Cached(ArtCacheDir, game, StoreArtKind.Cover);
        var hero = game.BackdropPath ?? SteamGridDbArt.CachedWideHero(ArtCacheDir, game) ?? CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Backdrop);
        var logo = game.LogoPath ?? LogoDownloader.Cached(ArtCacheDir, game) ?? LogoDownloader.FindSteamLocal(SteamPath, game);
        return cover == game.CoverPath && hero == game.BackdropPath && logo == game.LogoPath
            ? game
            : game with { CoverPath = cover, BackdropPath = hero, LogoPath = logo };
    }

    /// <summary>
    /// Fetches covers and backgrounds that are still missing, one game at a time. Returns true when something new was saved.
    /// Steam games: Steam's own CDN. Epic and manual games: Steam store screenshot, then Wallhaven (backgrounds, no key)
    /// and SteamGridDB (covers, the user's own key).
    /// </summary>
    public async Task<bool> DownloadMissingArtAsync(IReadOnlyList<Game> games)
    {
        var fetched = false;
        var gridKey = App.Settings.SteamGridDbApiKey;
        var gridRejected = false;

        foreach (var game in games)
        {
            // Logo for the game page: Steam's own copy or CDN first, then SteamGridDB with the user's key.
            if (game.LogoPath is null && _artTried.Add(GameSorter.Key(game) + "#logo"))
            {
                var logoPath = await LogoDownloader.DownloadFromSteamAsync(Http, ArtCacheDir, game);
                if (logoPath is null && gridKey is not null && !gridRejected)
                {
                    try { logoPath = await LogoDownloader.DownloadFromGridAsync(Http, gridKey, ArtCacheDir, game); }
                    catch (UnauthorizedAccessException) { gridRejected = true; }
                }
                if (logoPath is not null) fetched = true;
            }

            if (SteamGridDbArt.Applies(game))
            {
                if (SteamGridDbArt.CachedSteamShot(ArtCacheDir, game) is null && _artTried.Add(GameSorter.Key(game) + "#shot")
                    && await SteamGridDbArt.DownloadSteamShotAsync(Http, ArtCacheDir, game) is not null)
                    fetched = true;

                if (SteamGridDbArt.CachedWideHero(ArtCacheDir, game) is null && _artTried.Add(GameSorter.Key(game) + "#wall")
                    && await SteamGridDbArt.DownloadWallhavenAsync(Http, ArtCacheDir, game) is not null)
                    fetched = true;

                if (gridKey is not null && !gridRejected && game.CoverPath is null && _artTried.Add(GameSorter.Key(game) + "#sgdb"))
                {
                    try
                    {
                        int? gridId = await SteamGridDb.FindGameIdAsync(Http, gridKey, game);
                        if (gridId is not null
                            && await SteamGridDbArt.DownloadAsync(Http, gridKey, ArtCacheDir, game, StoreArtKind.Cover, gridId) is not null)
                            fetched = true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        gridRejected = true; // wrong key: no point asking again this time
                    }
                }
                continue;
            }

            if (game.Source != SteamProvider.ProviderName || (game.CoverPath is not null && game.BackdropPath is not null)) continue;
            if (!_artTried.Add(GameSorter.Key(game))) continue; // once per run; failures are retried next start

            if (game.CoverPath is null && await CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Cover) is not null) fetched = true;
            if (game.BackdropPath is null && await CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Backdrop) is not null) fetched = true;
        }
        return fetched;
    }
}
