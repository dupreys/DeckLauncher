using DeckLauncher.Core;
using DeckLauncher.Core.Gog;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class GogProviderTests
{
    private static Dictionary<string, string?> Entry(string id, string name, string folder, string exe, params (string, string)[] extra)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["gameID"] = id, ["gameName"] = name, ["path"] = folder, ["exe"] = exe, ["exeFile"] = Path.GetFileName(exe),
        };
        foreach (var (k, v) in extra) d[k] = v;
        return d;
    }

    [Fact]
    public void An_entry_becomes_a_game()
    {
        var exe = Path.Combine(Path.GetTempPath(), "Witcher", "witcher3.exe");
        var game = GogProvider.ParseEntry(Entry("1207664643", "The Witcher 3", Path.GetDirectoryName(exe)!, exe, ("ver", "4.04")));
        Assert.NotNull(game);
        Assert.Equal("GOG", game!.Source);
        Assert.Equal("1207664643", game.Id);
        Assert.Equal("The Witcher 3", game.Name);
        Assert.Equal(exe, game.ExecutablePath);
        Assert.Equal(Path.GetDirectoryName(exe), game.InstallPath);
    }

    [Fact]
    public void Dlc_and_broken_entries_are_skipped()
    {
        var exe = Path.Combine(Path.GetTempPath(), "G", "g.exe");
        Assert.Null(GogProvider.ParseEntry(Entry("2", "DLC", Path.GetDirectoryName(exe)!, exe, ("dependsOn", "1"))));
        Assert.Null(GogProvider.ParseEntry(Entry("abc", "Bad id", Path.GetDirectoryName(exe)!, exe)));
        Assert.Null(GogProvider.ParseEntry(Entry("3", "Not an exe", Path.GetDirectoryName(exe)!, Path.Combine(Path.GetTempPath(), "G", "readme.txt"))));
        Assert.Null(GogProvider.ParseEntry(Entry("4", "Relative", "G", "g.exe")));
        Assert.Null(GogProvider.ParseEntry(new Dictionary<string, string?> { ["gameID"] = "5" }));
    }

    [Fact]
    public void The_exe_is_built_from_the_folder_when_the_entry_has_only_a_file_name()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Hades");
        var values = new Dictionary<string, string?> { ["gameID"] = "9", ["gameName"] = "Hades", ["path"] = folder, ["exeFile"] = "Hades.exe" };
        Assert.Equal(Path.Combine(folder, "Hades.exe"), GogProvider.ParseEntry(values)!.ExecutablePath);
    }

    [Fact]
    public async Task Only_games_whose_program_exists_are_listed()
    {
        var dir = Directory.CreateTempSubdirectory("gogtest").FullName;
        try
        {
            var there = Path.Combine(dir, "a.exe");
            File.WriteAllText(there, "x");
            var gone = Path.Combine(dir, "b.exe");
            var provider = new GogProvider(() => new IReadOnlyDictionary<string, string?>[]
            {
                Entry("1", "Zeta", dir, there), Entry("2", "Gone", dir, gone), Entry("3", "Alpha", dir, there),
            });
            var games = await provider.GetGamesAsync();
            Assert.Equal(new[] { "Alpha", "Zeta" }, games.Select(g => g.Name));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_uninstaller_is_found_next_to_the_game()
    {
        var dir = Directory.CreateTempSubdirectory("gogunins").FullName;
        try
        {
            var game = new Game("GOG", "1", "G") { InstallPath = dir };
            Assert.Null(GogProvider.FindUninstaller(game));
            File.WriteAllText(Path.Combine(dir, "unins000.exe"), "x");
            Assert.Equal(Path.Combine(dir, "unins000.exe"), GogProvider.FindUninstaller(game));
        }
        finally { Directory.Delete(dir, true); }
    }
}
