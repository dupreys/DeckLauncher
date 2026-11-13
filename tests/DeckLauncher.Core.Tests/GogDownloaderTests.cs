using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeckLauncher.Core.Gog;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class GogDownloaderTests
{
    private static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return output.ToArray();
    }

    private static string Md5(byte[] data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

    [Fact]
    public void Galaxy_path_splits_the_hash()
    {
        Assert.Equal("ab/cd/abcdef", GogDownloader.GalaxyPath("abcdef"));
        Assert.Equal("ab/cd/abcdef", GogDownloader.GalaxyPath("ab/cd/abcdef"));
    }

    [Fact]
    public void A_chunk_is_checked_and_decompressed()
    {
        var raw = Encoding.UTF8.GetBytes(new string('x', 5000));
        var packed = Zlib(raw);
        var chunk = new GogChunk(Md5(raw), Md5(packed), raw.Length, packed.Length);
        Assert.Equal(raw, GogDownloader.DecodeChunk(packed, chunk));

        Assert.Null(GogDownloader.DecodeChunk(packed, chunk with { CompressedMd5 = new string('0', 32) })); // damaged download
        Assert.Null(GogDownloader.DecodeChunk(packed, chunk with { Md5 = new string('0', 32) }));
        Assert.Null(GogDownloader.DecodeChunk(packed, chunk with { Size = 10 }));
    }

    [Fact]
    public void Manifests_may_be_zlib_or_plain_json()
    {
        var json = Encoding.UTF8.GetBytes("{\"a\":1}");
        Assert.Equal(1, GogDownloader.ParseZlibJson(Zlib(json))!.RootElement.GetProperty("a").GetInt32());
        Assert.Equal(1, GogDownloader.ParseZlibJson(json)!.RootElement.GetProperty("a").GetInt32());
        Assert.Null(GogDownloader.ParseZlibJson(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void The_public_build_is_picked()
    {
        using var doc = JsonDocument.Parse("""{"items":[{"build_id":"2","branch":"beta"},{"build_id":"1","branch":null}]}""");
        Assert.Equal("1", GogDownloader.PickBuild(doc.RootElement)!.Value.GetProperty("build_id").GetString());
        using var none = JsonDocument.Parse("""{"items":[]}""");
        Assert.Null(GogDownloader.PickBuild(none.RootElement));
    }

    [Fact]
    public void Depots_follow_language_and_bitness()
    {
        using var doc = JsonDocument.Parse("""
        {"baseProductId":"10","depots":[
          {"productId":"10","manifest":"m_all","languages":["*"],"osBitness":["64"],"size":1,"compressedSize":1},
          {"productId":"10","manifest":"m_en","languages":["en-US"],"size":1,"compressedSize":1},
          {"productId":"10","manifest":"m_pt","languages":["pt-BR"],"size":1,"compressedSize":1},
          {"productId":"10","manifest":"m_32","languages":["*"],"osBitness":["32"],"size":1,"compressedSize":1},
          {"productId":"99","manifest":"m_dlc","languages":["*"],"size":1,"compressedSize":1}
        ]}
        """);
        var pt = GogDownloader.SelectDepots(doc.RootElement, "10", "pt-BR", "en-US").Select(d => d.Manifest).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "m_all", "m_pt" }, pt);
        var de = GogDownloader.SelectDepots(doc.RootElement, "10", "de-DE", "en-US").Select(d => d.Manifest).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "m_all", "m_en" }, de);
    }

    [Fact]
    public void Depot_manifest_lists_files_without_support_files()
    {
        using var doc = JsonDocument.Parse("""
        {"depot":{"items":[
          {"type":"DepotFile","path":"bin\\game.exe","chunks":[{"md5":"a","compressedMd5":"b","size":10,"compressedSize":5}]},
          {"type":"DepotFile","path":"icon.png","flags":["support"],"chunks":[]},
          {"type":"DepotDirectory","path":"saves"},
          {"type":"DepotFile","path":"empty.txt","chunks":[]}
        ]}}
        """);
        var files = GogDownloader.ParseDepotManifest(doc.RootElement);
        Assert.Equal(2, files.Count);
        Assert.Equal(10, files[0].Size);
        Assert.Empty(files[1].Chunks);
    }

    [Fact]
    public void Paths_cannot_leave_the_install_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "gogtest");
        Assert.NotNull(GogDownloader.SafePath(root, "bin\\game.exe"));
        Assert.NotNull(GogDownloader.SafePath(root, "/data/a.bin"));
        Assert.Null(GogDownloader.SafePath(root, "../evil.exe"));
        Assert.Null(GogDownloader.SafePath(root, "a/../../evil.exe"));
        Assert.Null(GogDownloader.SafePath(root, ""));
    }

    [Fact]
    public void Chunk_url_fills_the_secure_link_format()
    {
        using var doc = JsonDocument.Parse("""{"url_format":"{base_url}/{path}?token={token}","parameters":{"base_url":"https://cdn.gog.com/x","path":"/content","token":"t1","expires_at":123}}""");
        Assert.Equal("https://cdn.gog.com/x//content/ab/cd/abcdef?token=t1".Replace("//content", "/content"), GogDownloader.ChunkUrl(doc.RootElement, "abcdef")!.Replace("//content", "/content"));
        using var bad = JsonDocument.Parse("""{"url_format":"{base_url}/{path}","parameters":{"path":"/c"}}""");
        Assert.Null(GogDownloader.ChunkUrl(bad.RootElement, "abcdef")); // an unfilled placeholder
    }

    [Fact]
    public void The_main_program_comes_from_the_goggame_info()
    {
        var folder = Path.Combine(Path.GetTempPath(), "gogtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        File.WriteAllText(Path.Combine(folder, "bin", "game.exe"), "x");
        File.WriteAllText(Path.Combine(folder, "goggame-10.info"), """{"name":"My Game","playTasks":[{"type":"FileTask","category":"document","path":"readme.exe"},{"type":"FileTask","isPrimary":true,"category":"game","path":"bin\\game.exe","arguments":"-windowed"}]}""");
        try
        {
            var install = GogOwnInstall.FromInfo(folder, "10", "fallback", "1.0");
            Assert.NotNull(install);
            Assert.Equal("My Game", install!.Name);
            Assert.Equal(Path.Combine(folder, "bin", "game.exe"), install.Exe);
            Assert.Equal("-windowed", install.Arguments);
            var game = GogProvider.ParseEntry(install.ToEntry());
            Assert.NotNull(game);
            Assert.Equal("10", game!.Id);
            Assert.Null(GogOwnInstall.FromInfo(folder, "11", "x", null));
        }
        finally { Directory.Delete(folder, true); }
    }
}
