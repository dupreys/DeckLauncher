using System.ComponentModel;
using System.IO;
using System.Net.Http;
using DeckLauncher.Core;
using DeckLauncher.Core.Epic;
using DeckLauncher.Core.Gog;
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
    private readonly ArtTriedStore _artTried = new(Path.Combine(ArtCacheDir, "art_tried.json"));

    /// <summary>Running total of the artwork downloads (the notification bell reads it).</summary>
    public ArtProgress Art { get; } = new();

    public LibraryService()
    {
        var steamPath = SteamPath = SteamLocator.FindSteamPath();
        _providers = new IGameProvider[]
        {
            new SteamProvider(steamPath),
            new EpicProvider(),
            new DeckLauncher.Core.Gog.GogProvider(() => GogProvider.ReadRegistry().Concat(App.Settings.GogInstalls.Select(g => g.ToEntry())).ToList()),
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
        // Looking up each game's pictures on disk is many small file checks: keep them off the interface thread.
        return await Task.Run(() =>
        {
            for (var i = 0; i < games.Count; i++)
                games[i] = WithCachedArt(PlayTracker.Apply(GameOverrides.Apply(games[i], App.Settings.GameOverrides), App.Settings.PlayRecords));
            return games;
        });
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
        var games = await LoadSteamNotInstalledAsync(installed, steamId);
        games.AddRange(await Task.Run(() => EpicNotInstalled(installed)));
        games.AddRange(await Task.Run(() => GogNotInstalled(installed)));
        return games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---- Epic: the account's library (web sign-in, like Playnite) -------------------------------

    /// <summary>Why the Epic list could not be updated: null, "signin" (the session ended: sign in again) or "fetch" (no connection or Epic did not answer).</summary>
    public string? EpicProblem { get; private set; }

    public static bool EpicConnected => EpicTokens.FromJson(App.Settings.EpicTokens) is not null;

    /// <summary>Reads the Epic library when the account is connected and the last read is older than six hours (or <paramref name="force"/>). True when the list changed.</summary>
    public async Task<bool> SyncEpicAsync(bool force)
    {
        var tokens = EpicTokens.FromJson(App.Settings.EpicTokens);
        if (tokens is null) return false;
        var settings = App.Settings;
        if (!force && settings.EpicSyncedAt is { } last && DateTimeOffset.Now - last < TimeSpan.FromHours(6) && settings.EpicLibrary.Count > 0) return false;

        var client = new EpicAccountClient(Http);
        try
        {
            var fresh = await client.EnsureFreshAsync(tokens);
            if (!ReferenceEquals(fresh, tokens))
            {
                settings.EpicTokens = fresh.ToJson();
                SaveEpicSettings(); // Epic's refresh tokens are replaced at each use: keep the new one at once
            }
            var skipped = new HashSet<string>(settings.EpicSkipped, StringComparer.OrdinalIgnoreCase);
            var games = await client.ReadLibraryAsync(fresh, settings.EpicLibrary, skipped);
            var changed = games.Count != settings.EpicLibrary.Count || games.Zip(settings.EpicLibrary).Any(p => p.First.AppName != p.Second.AppName);
            settings.EpicLibrary = games;
            settings.EpicSkipped = skipped.ToList();
            settings.EpicSyncedAt = DateTimeOffset.Now;
            EpicProblem = null;
            SaveEpicSettings();
            return changed;
        }
        catch (EpicSignInException)
        {
            settings.EpicTokens = null; // the session ended: the list stays, the user signs in again
            EpicProblem = "signin";
            SaveEpicSettings();
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            EpicProblem = "fetch";
            return false;
        }
    }

    /// <summary>The Epic session ready to use (refreshed when needed); null when the account is not connected or the session ended. Network errors pass through.</summary>
    public async Task<EpicTokens?> FreshEpicTokensAsync()
    {
        var tokens = EpicTokens.FromJson(App.Settings.EpicTokens);
        if (tokens is null) return null;
        try
        {
            var fresh = await new EpicAccountClient(Http).EnsureFreshAsync(tokens);
            if (!ReferenceEquals(fresh, tokens))
            {
                App.Settings.EpicTokens = fresh.ToJson();
                SaveEpicSettings();
            }
            return fresh;
        }
        catch (EpicSignInException)
        {
            App.Settings.EpicTokens = null;
            EpicProblem = "signin";
            SaveEpicSettings();
            return null;
        }
    }

    private readonly HashSet<string> _epicUpdateSeen = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _epicUpdatesAt = DateTime.MinValue;

    /// <summary>Installed Epic games with a newer build on Epic (each game and build is reported once; Epic is asked at most every 3 hours).</summary>
    public async Task<List<Game>> CheckEpicUpdatesAsync(IReadOnlyList<Game> installed)
    {
        var none = new List<Game>();
        var tokens = EpicTokens.FromJson(App.Settings.EpicTokens);
        var epic = installed.Where(g => g.Source == EpicProvider.ProviderName && !string.IsNullOrEmpty(g.Version)).ToList();
        if (tokens is null || epic.Count == 0 || DateTime.UtcNow - _epicUpdatesAt < TimeSpan.FromHours(3)) return none;

        var client = new EpicAccountClient(Http);
        try
        {
            var fresh = await client.EnsureFreshAsync(tokens);
            if (!ReferenceEquals(fresh, tokens))
            {
                App.Settings.EpicTokens = fresh.ToJson();
                SaveEpicSettings();
            }
            var builds = await client.GetLatestBuildsAsync(fresh);
            _epicUpdatesAt = DateTime.UtcNow;
            return epic.Where(g => builds.TryGetValue(g.Id, out var build) && build != g.Version && _epicUpdateSeen.Add(g.Id + "@" + build)).ToList();
        }
        catch (Exception ex) when (ex is EpicSignInException or HttpRequestException or TaskCanceledException)
        {
            return none; // no connection or the session ended: the library sync reports that
        }
    }

    // ---- GOG: the account's library (web sign-in) ---------------------------------------------

    /// <summary>Why the GOG list could not be updated: null, "signin" (the session ended) or "fetch" (no connection or GOG did not answer).</summary>
    public string? GogProblem { get; private set; }

    public static bool GogConnected => GogTokens.FromJson(App.Settings.GogTokens) is not null;

    /// <summary>Reads the GOG library when the account is connected and the last read is older than six hours (or <paramref name="force"/>). True when the list changed.</summary>
    public async Task<bool> SyncGogAsync(bool force)
    {
        var tokens = GogTokens.FromJson(App.Settings.GogTokens);
        if (tokens is null) return false;
        var settings = App.Settings;
        if (!force && settings.GogSyncedAt is { } last && DateTimeOffset.Now - last < TimeSpan.FromHours(6) && settings.GogLibrary.Count > 0) return false;

        var client = new GogAccountClient(Http);
        try
        {
            var fresh = await client.EnsureFreshAsync(tokens);
            if (!ReferenceEquals(fresh, tokens))
            {
                settings.GogTokens = fresh.ToJson();
                SaveEpicSettings(); // GOG replaces the refresh token at each use: keep the new one at once
            }
            var games = await client.ReadLibraryAsync(fresh);
            var changed = games.Count != settings.GogLibrary.Count || games.Zip(settings.GogLibrary).Any(p => p.First.Id != p.Second.Id);
            settings.GogLibrary = games;
            settings.GogSyncedAt = DateTimeOffset.Now;
            GogProblem = null;
            SaveEpicSettings();
            return changed;
        }
        catch (GogSignInException)
        {
            settings.GogTokens = null; // the session ended: the list stays, the user signs in again
            GogProblem = "signin";
            SaveEpicSettings();
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            GogProblem = "fetch";
            return false;
        }
    }

    /// <summary>The GOG games of the account that are not installed on this PC.</summary>
    private List<Game> GogNotInstalled(IReadOnlyList<Game> installed)
    {
        var ids = installed.Where(g => g.Source == GogProvider.ProviderName).Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var games = new List<Game>();
        foreach (var owned in App.Settings.GogLibrary)
        {
            if (ids.Contains(owned.Id)) continue;
            var game = new Game(GogProvider.ProviderName, owned.Id, owned.Title)
            {
                NotInstalled = true,
                Slug = owned.Slug,
                CoverUrl = owned.ImageBase,
            };
            if (App.Settings.HiddenGames.ContainsKey(GameSorter.Key(game))) continue;
            games.Add(WithCachedArt(GameOverrides.Apply(game, App.Settings.GameOverrides)));
        }
        return games;
    }

    /// <summary>The GOG session ready to use (refreshed when needed); null when the account is not connected or the session ended.</summary>
    public async Task<GogTokens?> FreshGogTokensAsync()
    {
        var tokens = GogTokens.FromJson(App.Settings.GogTokens);
        if (tokens is null) return null;
        try
        {
            var fresh = await new GogAccountClient(Http).EnsureFreshAsync(tokens);
            if (!ReferenceEquals(fresh, tokens))
            {
                App.Settings.GogTokens = fresh.ToJson();
                SaveEpicSettings();
            }
            return fresh;
        }
        catch (GogSignInException)
        {
            App.Settings.GogTokens = null;
            GogProblem = "signin";
            SaveEpicSettings();
            return null;
        }
    }

    /// <summary>The program of GOG Galaxy when it is installed.</summary>
    public static string? GalaxyExe => GalaxyClient.FindExe();

    public static void SaveEpicSettings()
    {
        try { App.Store.Save(App.Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the next change saves it again */ }
    }

    /// <summary>The Epic games of the account that are not installed on this PC.</summary>
    private List<Game> EpicNotInstalled(IReadOnlyList<Game> installed)
    {
        var apps = installed.Where(g => g.Source == EpicProvider.ProviderName).Select(g => g.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = installed.Where(g => g.Source == EpicProvider.ProviderName && g.CatalogItemId is not null).Select(g => g.CatalogItemId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var games = new List<Game>();
        foreach (var owned in App.Settings.EpicLibrary)
        {
            if (apps.Contains(owned.AppName) || items.Contains(owned.CatalogItemId)) continue;
            var game = new Game(EpicProvider.ProviderName, owned.AppName, owned.Title)
            {
                NotInstalled = true,
                CatalogNamespace = owned.CatalogNamespace,
                CatalogItemId = owned.CatalogItemId,
                CoverUrl = owned.CoverUrl,
                WideUrl = owned.WideUrl,
                LogoUrl = owned.LogoUrl,
            };
            if (App.Settings.HiddenGames.ContainsKey(GameSorter.Key(game))) continue;
            games.Add(WithCachedArt(GameOverrides.Apply(game, App.Settings.GameOverrides)));
        }
        return games;
    }

    private async Task<List<Game>> LoadSteamNotInstalledAsync(IReadOnlyList<Game> installed, string? steamId)
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
        await Task.Run(() => { for (var i = 0; i < games.Count; i++) games[i] = WithCachedArt(GameOverrides.Apply(games[i], App.Settings.GameOverrides)); });
        return games;
    }

    /// <summary>Downloads Steam's own cover for not-installed games that have none (at most 150 per run, four at a time). True when something new was saved.</summary>
    public async Task<bool> DownloadNotInstalledCoversAsync(IReadOnlyList<Game> games)
    {
        var todo = games.Where(g => g.CoverPath is null && !_artTried.Contains(GameSorter.Key(g) + "#cover") && g.Source is not (EpicProvider.ProviderName or GogProvider.ProviderName))
            .OrderBy(g => g.Source == SteamProvider.ProviderName ? 1 : 0).Take(150).ToList();
        var job = Art.Begin(todo.Count, 0, 0);
        var fetched = false;
        using var gate = new SemaphoreSlim(4);
        try
        {
            await Task.WhenAll(todo.Select(async game =>
            {
                if (!_artTried.Add(GameSorter.Key(game) + "#cover")) return;
                await gate.WaitAsync();
                try
                {
                    if (await job.RunAsync(ArtItem.Cover, () => CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Cover))) fetched = true;
                }
                finally { gate.Release(); }
            }));
        }
        finally
        {
            job.End();
            _artTried.Save();
        }
        return fetched;
    }

    /// <summary>A game whose cover is missing or is only the store's own picture (Epic/GOG), so Steam's or SteamGridDB's can replace it.</summary>
    /// <summary>The user's own choice (Manage art) or a cover set by hand: it is never replaced by an automatic one.</summary>
    private bool HasOwnCover(Game game)
    {
        if (game.CoverPath is null) return false;
        return game.CoverPath != SteamGridDbArt.CachedSteamCover(ArtCacheDir, game)
               && game.CoverPath != SteamGridDbArt.Cached(ArtCacheDir, game, StoreArtKind.Cover)
               && game.CoverPath != CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Cover);
    }

    /// <summary>Steam's 600x900 cover is the preferred one: wanted whenever the game has none yet, even if a SteamGridDB or the store's picture is already there.</summary>
    private bool WantsSteamCover(Game game) =>
        SteamGridDbArt.Applies(game) && !HasOwnCover(game) && SteamGridDbArt.CachedSteamCover(ArtCacheDir, game) is null;

    private bool WantsBetterCover(Game game)
    {
        if (!SteamGridDbArt.Applies(game) || game.Source == "Manual") return game.CoverPath is null && SteamGridDbArt.Applies(game);
        if (SteamGridDbArt.CachedBestCover(ArtCacheDir, game) is not null) return false;
        return game.CoverPath is null || game.CoverPath == CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Cover);
    }

    /// <summary>Logos and wallpapers for the Epic games that are not installed (a few per run; games already tried are not tried again).</summary>
    public async Task<bool> DownloadEpicNotInstalledArtAsync(IReadOnlyList<Game> games)
    {
        var pending = await Task.Run(() => games.Where(g => g.Source is "Epic" or "GOG"
            && ((g.LogoPath is null && !_artTried.Contains(GameSorter.Key(g) + "#logo"))
                || (WantsSteamCover(g) && !_artTried.Contains(GameSorter.Key(g) + "#steamcover2"))
                || (!string.IsNullOrWhiteSpace(App.Settings.SteamGridDbApiKey) && WantsBetterCover(g) && !_artTried.Contains(GameSorter.Key(g) + "#sgdbcover2"))
                || (g.BackdropPath is null && !_artTried.Contains(GameSorter.Key(g) + "#epicwall")
                    && !(g.Source == "GOG" && _artTried.Contains(GameSorter.Key(g) + "#sgdbhero")))))
            .OrderBy(g => g.Source == GogProvider.ProviderName ? 0 : 1).Take(200).ToList());
        return pending.Count > 0 && await DownloadMissingArtAsync(pending);
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
        if (game.Source == EpicProvider.ProviderName && game.CoverUrl is null && game.WideUrl is null && game.LogoUrl is null
            && App.Settings.EpicLibrary.FirstOrDefault(e => string.Equals(e.AppName, game.Id, StringComparison.OrdinalIgnoreCase)) is { } owned)
            game = game with { CoverUrl = owned.CoverUrl, WideUrl = owned.WideUrl, LogoUrl = owned.LogoUrl }; // installed Epic game: Epic's own pictures
        if (game.Source == GogProvider.ProviderName && game.CoverUrl is null
            && App.Settings.GogLibrary.FirstOrDefault(e => e.Id == game.Id) is { ImageBase: { } image })
            game = game with { CoverUrl = image }; // installed GOG game: GOG's own cover
        // Covers of other stores: Steam's 600x900 first, then SteamGridDB, and the store's own picture last (other proportions: it gets cropped).
        var cover = game.CoverPath ?? SteamGridDbArt.CachedBestCover(ArtCacheDir, game) ?? CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Cover);
        var hero = game.Source == GogProvider.ProviderName
            ? game.BackdropPath ?? SteamGridDbArt.Cached(ArtCacheDir, game, StoreArtKind.Backdrop) ?? SteamGridDbArt.CachedWideHero(ArtCacheDir, game) // GOG: SteamGridDB first
            : game.Source == EpicProvider.ProviderName
            ? game.BackdropPath ?? CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Backdrop) ?? SteamGridDbArt.CachedWideHero(ArtCacheDir, game) // Epic's own picture first
            : game.BackdropPath ?? SteamGridDbArt.CachedWideHero(ArtCacheDir, game) ?? CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Backdrop);
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
        // One run at a time: a refresh that starts while the last one is still downloading waits (and then finds most of it tried already)
        // instead of running the same lookups twice in parallel.
        await _artRun.WaitAsync();
        try { return await DownloadMissingArtCoreAsync(games); }
        finally { _artRun.Release(); }
    }

    private readonly SemaphoreSlim _artRun = new(1, 1);

    private async Task<bool> DownloadMissingArtCoreAsync(IReadOnlyList<Game> games)
    {
        var fetched = false;
        var gridKey = App.Settings.SteamGridDbApiKey;
        var gridRejected = false;
        var (coverCount, logoCount, wallCount) = await Task.Run(() => CountArtWork(games, gridKey is not null)); // many small file checks: not on the interface thread
        var job = Art.Begin(coverCount, logoCount, wallCount);

        using var gate = new SemaphoreSlim(4);
        async Task ProcessAsync(Game game)
        {
                // Logo for the game page: Steam's own copy or CDN first, then SteamGridDB with the user's key.
                if (game.LogoPath is null && _artTried.Add(GameSorter.Key(game) + "#logo"))
                {
                    var rejected = false;
                    if (await job.RunAsync(ArtItem.Logo, async () =>
                        {
                            var logoPath = await LogoDownloader.DownloadFromSteamAsync(Http, ArtCacheDir, game)
                                           ?? await LogoDownloader.DownloadFromEpicAsync(Http, ArtCacheDir, game);
                            if (logoPath is null && gridKey is not null && !gridRejected)
                            {
                                try { logoPath = await LogoDownloader.DownloadFromGridAsync(Http, gridKey, ArtCacheDir, game); }
                                catch (UnauthorizedAccessException) { rejected = true; }
                            }
                            return logoPath;
                        })) fetched = true;
                    if (rejected) gridRejected = true;
                }

                if (SteamGridDbArt.Applies(game))
                {
                    if (game.Source == GogProvider.ProviderName && gridKey is not null && !gridRejected && game.BackdropPath is null
                        && SteamGridDbArt.Cached(ArtCacheDir, game, StoreArtKind.Backdrop) is null && _artTried.Add(GameSorter.Key(game) + "#sgdbhero"))
                    {
                        var rejectedHero = false;
                        if (await job.RunAsync(ArtItem.Wallpaper, async () =>
                            {
                                try { return await SteamGridDbArt.DownloadAsync(Http, gridKey, ArtCacheDir, game, StoreArtKind.Backdrop); }
                                catch (UnauthorizedAccessException) { rejectedHero = true; return null; }
                            })) fetched = true;
                        if (rejectedHero) gridRejected = true;
                    }
                    // Cover: the game's Steam cover when it is on Steam (same as the Steam tab), then SteamGridDB, then the store's own picture.
                    if (WantsSteamCover(game) && _artTried.Add(GameSorter.Key(game) + "#steamcover2"))
                    {
                        var attempt = new ArtAttempt();
                        if (await job.RunAsync(ArtItem.Cover, () => SteamGridDbArt.DownloadSteamCoverAsync(Http, ArtCacheDir, game, default, attempt))) fetched = true;
                        if (attempt.NetworkFailed) _artTried.Remove(GameSorter.Key(game) + "#steamcover2"); // Steam was busy or unreachable: not "not found"
                    }

                    if (game.Source is EpicProvider.ProviderName || (game.Source == GogProvider.ProviderName && gridKey is null))
                    {
                        // Epic's own wide picture first (from the catalog, no key needed).
                        if (game.BackdropPath is null && _artTried.Add(GameSorter.Key(game) + "#epicwall")
                            && CoverDownloader.Urls(game, StoreArtKind.Backdrop).Count > 0
                            && await job.RunAsync(ArtItem.Wallpaper, () => CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Backdrop)))
                            fetched = true;
                    }
                    var hasOfficialWide = CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Backdrop) is not null
                                          || (game.Source == GogProvider.ProviderName && SteamGridDbArt.Cached(ArtCacheDir, game, StoreArtKind.Backdrop) is not null);

                    if (!hasOfficialWide && SteamGridDbArt.CachedSteamShot(ArtCacheDir, game) is null && _artTried.Add(GameSorter.Key(game) + "#shot")
                        && await job.RunAsync(ArtItem.Wallpaper, () => SteamGridDbArt.DownloadSteamShotAsync(Http, ArtCacheDir, game)))
                        fetched = true;

                    if (!hasOfficialWide && SteamGridDbArt.CachedWideHero(ArtCacheDir, game) is null && _artTried.Add(GameSorter.Key(game) + "#wall")
                        && await job.RunAsync(ArtItem.Wallpaper, () => SteamGridDbArt.DownloadWallhavenAsync(Http, ArtCacheDir, game)))
                        fetched = true;

                    if (gridKey is not null && !gridRejected && WantsBetterCover(game) && SteamGridDbArt.CachedBestCover(ArtCacheDir, game) is null
                        && _artTried.Add(GameSorter.Key(game) + "#sgdbcover2"))
                    {
                        var rejected = false;
                        if (await job.RunAsync(ArtItem.Cover, async () =>
                            {
                                try
                                {
                                    int? gridId = await SteamGridDb.FindGameIdAsync(Http, gridKey, game);
                                    return gridId is not null
                                        ? await SteamGridDbArt.DownloadAsync(Http, gridKey, ArtCacheDir, game, StoreArtKind.Cover, gridId)
                                        : null;
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    rejected = true; // wrong key: no point asking again this time
                                    return null;
                                }
                            })) fetched = true;
                        if (rejected) gridRejected = true;
                    }
                    // The store's own cover only when nothing better was found.
                    if (game.Source is EpicProvider.ProviderName or GogProvider.ProviderName && SteamGridDbArt.CachedBestCover(ArtCacheDir, game) is null
                        && CoverDownloader.Cached(ArtCacheDir, game, StoreArtKind.Cover) is null && CoverDownloader.Urls(game, StoreArtKind.Cover).Count > 0
                        && _artTried.Add(GameSorter.Key(game) + "#officialcover")
                        && await job.RunAsync(ArtItem.Cover, () => CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Cover)))
                        fetched = true;
                    return;
                }

                if (game.Source != SteamProvider.ProviderName || (game.CoverPath is not null && game.BackdropPath is not null)) return;
                if (!_artTried.Add(GameSorter.Key(game))) return; // once per run; failures are retried next start

                if (game.CoverPath is null && await job.RunAsync(ArtItem.Cover, () => CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Cover))) fetched = true;
                if (game.BackdropPath is null && await job.RunAsync(ArtItem.Wallpaper, () => CoverDownloader.DownloadAsync(Http, ArtCacheDir, game, StoreArtKind.Backdrop))) fetched = true;
        }

        try
        {
            await Task.WhenAll(games.Select(async game =>
            {
                await gate.WaitAsync();
                try { await Task.Run(() => ProcessAsync(game)); } // parsing the answers happens off the interface thread
                finally { gate.Release(); }
            }));
        }
        finally
        {
            job.End();
            _artTried.Save();
        }
        return fetched;
    }

    /// <summary>How many downloads <see cref="DownloadMissingArtAsync"/> is about to try (for the progress total; a close estimate).</summary>
    private (int Covers, int Logos, int Wallpapers) CountArtWork(IReadOnlyList<Game> games, bool hasGridKey)
    {
        int covers = 0, logos = 0, walls = 0;
        foreach (var game in games)
        {
            var key = GameSorter.Key(game);
            if (game.LogoPath is null && !_artTried.Contains(key + "#logo")) logos++;
            if (SteamGridDbArt.Applies(game))
            {
                if (game.Source is EpicProvider.ProviderName or GogProvider.ProviderName)
                {
                    if (game.CoverPath is null && CoverDownloader.Urls(game, StoreArtKind.Cover).Count > 0 && !_artTried.Contains(key + "#epiccover")) covers++;
                    if (game.BackdropPath is null && CoverDownloader.Urls(game, StoreArtKind.Backdrop).Count > 0 && !_artTried.Contains(key + "#epicwall")) walls++;
                }
                if (SteamGridDbArt.CachedSteamShot(ArtCacheDir, game) is null && !_artTried.Contains(key + "#shot")) walls++;
                if (SteamGridDbArt.CachedWideHero(ArtCacheDir, game) is null && !_artTried.Contains(key + "#wall")) walls++;
                if (hasGridKey && game.CoverPath is null && !_artTried.Contains(key + "#sgdb")) covers++;
            }
            else if (game.Source == SteamProvider.ProviderName && !_artTried.Contains(key))
            {
                if (game.CoverPath is null) covers++;
                if (game.BackdropPath is null) walls++;
            }
        }
        return (covers, logos, walls);
    }
}
