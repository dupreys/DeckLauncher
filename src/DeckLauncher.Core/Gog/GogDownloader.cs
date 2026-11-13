using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DeckLauncher.Core.Gog;

public sealed record GogChunk(string Md5, string CompressedMd5, long Size, long CompressedSize);

public sealed record GogDepotFile(string Path, IReadOnlyList<GogChunk> Chunks)
{
    public long Size => Chunks.Sum(c => c.Size);
}

public sealed record GogDepot(string ProductId, string Manifest, long Size, long CompressedSize);

/// <summary>What will be downloaded for one game.</summary>
public sealed class GogInstallPlan
{
    public string InstallDirectory { get; init; } = "";
    public string BuildVersion { get; init; } = "";
    public string[] SecureLinkProducts { get; init; } = Array.Empty<string>();
    public List<GogDepotFile> Files { get; init; } = new();
    public long DiskSize => Files.Sum(f => f.Size);
    public long DownloadSize => Files.Sum(f => f.Chunks.Sum(c => c.CompressedSize));
}

/// <summary>Lets the interface pause and resume a download (the chunks already running finish first).</summary>
public sealed class GogDownloadControl
{
    private TaskCompletionSource? _gate;

    public bool Paused => Volatile.Read(ref _gate) is not null;

    public void Pause() => Interlocked.CompareExchange(ref _gate, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), null);

    public void Resume() => Interlocked.Exchange(ref _gate, null)?.TrySetResult();

    public Task WaitAsync(CancellationToken ct) => Volatile.Read(ref _gate) is { } gate ? gate.Task.WaitAsync(ct) : Task.CompletedTask;
}

/// <summary>Raised when the game cannot be downloaded this way (older depot format, no Windows build): the caller falls back.</summary>
public sealed class GogUnsupportedException : Exception
{
    public GogUnsupportedException(string message) : base(message) { }
}

/// <summary>
/// Downloads a GOG game the way Heroic's gogdl does (GOG's "generation 2" content system, the one GOG Galaxy uses): the build lists
/// depots, each depot lists files made of zlib-compressed chunks, and the chunks come from GOG's CDN through signed links.
/// Every chunk is checked against the hashes in the manifest. Redistributables (Visual C++, DirectX…) are not installed.
/// </summary>
public sealed class GogDownloader
{
    private const string ContentSystem = "https://content-system.gog.com";
    private const string Cdn = "https://gog-cdn-fastly.gog.com";

    private readonly HttpClient _http;

    public GogDownloader(HttpClient http) => _http = http;

    // ---- pure parsing (tested) -----------------------------------------------------------------

    /// <summary>"abcdef…" → "ab/cd/abcdef…" (how GOG lays out manifests and chunks).</summary>
    public static string GalaxyPath(string hash) =>
        hash.Contains('/') || hash.Length < 4 ? hash : $"{hash[..2]}/{hash.Substring(2, 2)}/{hash}";

    /// <summary>GOG sends manifests as zlib streams; some answers are plain JSON.</summary>
    public static JsonDocument? ParseZlibJson(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return JsonDocument.Parse(output.ToArray());
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            try { return JsonDocument.Parse(data); }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>The newest build to install: the one without a branch (the public one), else the first.</summary>
    public static JsonElement? PickBuild(JsonElement builds)
    {
        if (builds.ValueKind != JsonValueKind.Object || !builds.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return null;
        foreach (var item in items.EnumerateArray())
            if (!item.TryGetProperty("branch", out var branch) || branch.ValueKind == JsonValueKind.Null) return item;
        return items[0];
    }

    /// <summary>The depots of the game itself in the best language: always the language-free ones, plus the first wanted language that has some.</summary>
    public static List<GogDepot> SelectDepots(JsonElement meta, string gameId, params string[] languages)
    {
        var all = new List<(GogDepot Depot, string[] Languages, bool Is64)>();
        if (meta.ValueKind != JsonValueKind.Object || !meta.TryGetProperty("depots", out var depots) || depots.ValueKind != JsonValueKind.Array) return new();
        var baseId = meta.TryGetProperty("baseProductId", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? gameId : gameId;

        foreach (var d in depots.EnumerateArray())
        {
            string? S(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            long N(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
            if (S("productId") is not { } product || product != baseId || S("manifest") is not { Length: > 0 } manifest) continue;
            var langs = d.TryGetProperty("languages", out var l) && l.ValueKind == JsonValueKind.Array ? l.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : new[] { "*" };
            var bits = d.TryGetProperty("osBitness", out var ob) && ob.ValueKind == JsonValueKind.Array ? ob.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
            if (bits.Length > 0 && !bits.Contains("64")) { all.Add((new GogDepot(product, manifest, N("size"), N("compressedSize")), langs, false)); continue; }
            all.Add((new GogDepot(product, manifest, N("size"), N("compressedSize")), langs, true));
        }

        var usable = all.Where(x => x.Is64).ToList();
        if (usable.Count == 0) usable = all; // only 32-bit depots exist

        var result = usable.Where(x => x.Languages.Length == 0 || x.Languages.Contains("*")).Select(x => x.Depot).ToList();
        foreach (var language in languages)
        {
            var specific = usable.Where(x => x.Languages.Any(l => l != "*" && string.Equals(l, language, StringComparison.OrdinalIgnoreCase))).Select(x => x.Depot).ToList();
            if (specific.Count == 0) continue;
            result.AddRange(specific);
            break;
        }
        return result.DistinctBy(d => d.Manifest).ToList();
    }

    /// <summary>The files of one depot manifest. Directories, links and "support" files (Galaxy extras) are not downloaded.</summary>
    public static List<GogDepotFile> ParseDepotManifest(JsonElement root)
    {
        var files = new List<GogDepotFile>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("depot", out var depot) || !depot.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return files;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) || type.GetString() != "DepotFile") continue;
            if (item.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Array && flags.EnumerateArray().Any(f => f.GetString() == "support")) continue;
            if (!item.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String) continue;
            var chunks = new List<GogChunk>();
            if (item.TryGetProperty("chunks", out var cs) && cs.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cs.EnumerateArray())
                {
                    string? S(string n) => c.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    long N(string n) => c.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
                    chunks.Add(new GogChunk(S("md5") ?? "", S("compressedMd5") ?? "", N("size"), N("compressedSize")));
                }
            }
            files.Add(new GogDepotFile(path.GetString()!, chunks));
        }
        return files.Where(f => f.Path.Length > 0).ToList();
    }

    /// <summary>The path of a manifest file inside the install folder, or null when it would escape it.</summary>
    public static string? SafePath(string root, string relative)
    {
        var clean = relative.Replace('\\', '/').TrimStart('/');
        if (clean.Length == 0) return null;
        var full = Path.GetFullPath(Path.Combine(root, clean));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>The download address of a chunk from one of the secure-link endpoints GOG handed out.</summary>
    public static string? ChunkUrl(JsonElement endpoint, string compressedMd5)
    {
        if (endpoint.ValueKind != JsonValueKind.Object || !endpoint.TryGetProperty("url_format", out var format) || format.GetString() is not { } url) return null;
        if (!endpoint.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in parameters.EnumerateObject())
        {
            var value = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
            if (p.Name == "path") value = value.TrimEnd('/') + "/" + GalaxyPath(compressedMd5);
            url = url.Replace("{" + p.Name + "}", value);
        }
        return url.Contains('{') || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ? null : url;
    }

    /// <summary>One chunk: zlib-decompressed bytes, or null when the compressed data or the result does not match the manifest.</summary>
    public static byte[]? DecodeChunk(byte[] compressed, GogChunk chunk)
    {
        if (!string.Equals(Convert.ToHexString(MD5.HashData(compressed)), chunk.CompressedMd5, StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var input = new MemoryStream(compressed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            var data = output.ToArray();
            if (chunk.Size > 0 && data.Length != chunk.Size) return null;
            return string.Equals(Convert.ToHexString(MD5.HashData(data)), chunk.Md5, StringComparison.OrdinalIgnoreCase) ? data : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { return null; }
    }

    // ---- network ---------------------------------------------------------------------------------

    private async Task<byte[]?> GetBytesAsync(string url, GogTokens? tokens, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (tokens is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden && tokens is not null)
            throw new GogSignInException("A sessão da GOG expirou. Entre de novo.");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
    }

    /// <summary>Reads the build and the manifests: what has to be downloaded. Throws <see cref="GogUnsupportedException"/> when this method does not apply.</summary>
    public async Task<GogInstallPlan> PlanAsync(GogTokens tokens, string gameId, string[] languages, CancellationToken ct = default)
    {
        var buildsBytes = await GetBytesAsync($"{ContentSystem}/products/{Uri.EscapeDataString(gameId)}/os/windows/builds?generation=2", tokens, ct)
                          ?? throw new GogUnsupportedException("no builds");
        using var builds = JsonDocument.Parse(buildsBytes);
        if (PickBuild(builds.RootElement) is not { } build) throw new GogUnsupportedException("no builds");
        if (build.TryGetProperty("generation", out var generation) && generation.ValueKind == JsonValueKind.Number && generation.GetInt32() != 2)
            throw new GogUnsupportedException("old depot format");
        var link = build.TryGetProperty("link", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
        if (link is null || !Uri.TryCreate(link, UriKind.Absolute, out var linkUri) || linkUri.Scheme != Uri.UriSchemeHttps) throw new GogUnsupportedException("no build link");

        using var meta = ParseZlibJson(await GetBytesAsync(link, null, ct) ?? throw new GogUnsupportedException("no build meta"))
                         ?? throw new GogUnsupportedException("bad build meta");
        var depots = SelectDepots(meta.RootElement, gameId, languages);
        if (depots.Count == 0) throw new GogUnsupportedException("no depots");

        var files = new Dictionary<string, GogDepotFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var depot in depots)
        {
            using var manifest = ParseZlibJson(await GetBytesAsync($"{Cdn}/content-system/v2/meta/{GalaxyPath(depot.Manifest)}", null, ct) ?? throw new GogUnsupportedException("no manifest"))
                                 ?? throw new GogUnsupportedException("bad manifest");
            foreach (var file in ParseDepotManifest(manifest.RootElement)) files[file.Path.Replace('\\', '/')] = file;
        }
        if (files.Count == 0) throw new GogUnsupportedException("no files");

        var installDirectory = meta.RootElement.TryGetProperty("installDirectory", out var dir) && dir.ValueKind == JsonValueKind.String ? dir.GetString() ?? gameId : gameId;
        return new GogInstallPlan
        {
            InstallDirectory = GogInstallers.SafeFileName(installDirectory),
            BuildVersion = build.TryGetProperty("version_name", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "",
            Files = files.Values.ToList(),
        };
    }

    private async Task<List<JsonElement>> SecureLinksAsync(Func<Task<GogTokens?>> getTokens, string gameId, CancellationToken ct, List<JsonDocument> keep)
    {
        var tokens = await getTokens() ?? throw new GogSignInException("A sessão da GOG expirou. Entre de novo.");
        var bytes = await GetBytesAsync($"{ContentSystem}/products/{Uri.EscapeDataString(gameId)}/secure_link?_version=2&generation=2&path=/", tokens, ct)
                    ?? throw new HttpRequestException("no secure link");
        var doc = JsonDocument.Parse(bytes);
        keep.Add(doc);
        if (!doc.RootElement.TryGetProperty("urls", out var urls) || urls.ValueKind != JsonValueKind.Array) throw new HttpRequestException("no secure link");
        return urls.EnumerateArray().ToList();
    }

    /// <summary>Downloads every file of the plan into <paramref name="folder"/>. <paramref name="progress"/> gets (compressed bytes done, total).</summary>
    public async Task DownloadAsync(GogInstallPlan plan, string gameId, string folder, Func<Task<GogTokens?>> getTokens, IProgress<(long Done, long Total)> progress, GogDownloadControl? control = null, CancellationToken ct = default)
    {
        var documents = new List<JsonDocument>();
        try
        {
            var endpoints = await SecureLinksAsync(getTokens, gameId, ct, documents);
            var endpointLock = new SemaphoreSlim(1, 1);
            var generation = 0;

            async Task RefreshEndpointsAsync(int seen)
            {
                await endpointLock.WaitAsync(ct);
                try
                {
                    if (generation != seen) return; // another chunk already renewed them
                    endpoints = await SecureLinksAsync(getTokens, gameId, ct, documents);
                    generation++;
                }
                finally { endpointLock.Release(); }
            }

            var total = plan.DownloadSize;
            long done = 0;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var gate = new SemaphoreSlim(8);
            var pending = new List<Task>();
            Exception? failure = null;
            Directory.CreateDirectory(folder);

            foreach (var file in plan.Files)
            {
                if (control is not null) await control.WaitAsync(cts.Token);
                if (cts.IsCancellationRequested) break;
                var path = SafePath(folder, file.Path);
                if (path is null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                if (file.Chunks.Count == 0)
                {
                    await File.WriteAllBytesAsync(path, Array.Empty<byte>(), ct);
                    continue;
                }

                var handle = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.Asynchronous, file.Size);
                var chunkTasks = new List<Task>();
                long offset = 0;
                foreach (var chunk in file.Chunks)
                {
                    try { await gate.WaitAsync(cts.Token); }
                    catch (OperationCanceledException) { break; }
                    var at = offset;
                    offset += chunk.Size;
                    chunkTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (control is not null) await control.WaitAsync(cts.Token);
                            var data = await FetchChunkAsync(chunk, () => endpoints, generation, RefreshEndpointsAsync, cts.Token);
                            await RandomAccess.WriteAsync(handle, data, at, cts.Token);
                            progress.Report((Interlocked.Add(ref done, chunk.CompressedSize), total));
                        }
                        catch (Exception ex)
                        {
                            Interlocked.CompareExchange(ref failure, ex, null);
                            cts.Cancel();
                        }
                        finally { gate.Release(); }
                    }, CancellationToken.None));
                }
                pending.Add(Task.WhenAll(chunkTasks).ContinueWith(_ => handle.Dispose(), CancellationToken.None));
            }

            await Task.WhenAll(pending);
            if (failure is GogSignInException) throw failure;
            if (failure is not null) throw failure is OperationCanceledException && ct.IsCancellationRequested ? failure : new HttpRequestException("download failed", failure);
            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            foreach (var d in documents) d.Dispose();
        }
    }

    private async Task<byte[]> FetchChunkAsync(GogChunk chunk, Func<List<JsonElement>> endpoints, int generation, Func<int, Task> refresh, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var list = endpoints();
            var url = list.Count == 0 ? null : ChunkUrl(list[attempt % list.Count], chunk.CompressedMd5);
            try
            {
                if (url is null) throw new HttpRequestException("no chunk address");
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    await refresh(generation); // the signed link expired
                    generation++;
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var compressed = await response.Content.ReadAsByteArrayAsync(ct);
                if (DecodeChunk(compressed, chunk) is { } data) return data;
                last = new InvalidDataException("chunk does not match its hash");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
            {
                last = ex;
            }
            await Task.Delay(TimeSpan.FromSeconds(1 + attempt), ct);
        }
        throw new HttpRequestException("chunk failed", last);
    }
}
