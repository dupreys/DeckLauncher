using DeckLauncher.Core.Apps;
using DeckLauncher.Core.Settings;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class AppListTests
{
    [Theory]
    [InlineData("youtube.com", "https://youtube.com/")]
    [InlineData("  https://www.twitch.tv/directory  ", "https://www.twitch.tv/directory")]
    [InlineData("http://netflix.com", "http://netflix.com/")]
    public void Normalizes_site_addresses(string input, string expected) => Assert.Equal(expected, AppList.NormalizeUrl(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("two words.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("ftp://server.com")]
    public void Rejects_what_is_not_a_web_address(string input) => Assert.Null(AppList.NormalizeUrl(input));

    [Fact]
    public void Suggests_a_name_from_the_host()
    {
        Assert.Equal("Youtube", AppList.SuggestWebName("https://www.youtube.com/"));
        Assert.Equal("Twitch", AppList.SuggestWebName("https://twitch.tv/x"));
        Assert.Equal("youtube.com", AppList.HostOf("https://www.youtube.com/"));
    }

    [Fact]
    public void Order_follows_the_saved_list_and_appends_new_items()
    {
        var result = AppList.Order(new[] { "a", "b", "c", "d" }, new[] { "c", "gone", "a", "c" });
        Assert.Equal(new[] { "c", "a", "b", "d" }, result);
    }

    [Fact]
    public void Move_swaps_with_the_neighbour_and_stops_at_the_ends()
    {
        var keys = new List<string> { "a", "b", "c" };
        Assert.True(AppList.Move(keys, "b", -1));
        Assert.Equal(new[] { "b", "a", "c" }, keys);
        Assert.False(AppList.Move(keys, "b", -1));
        Assert.False(AppList.Move(keys, "c", +1));
        Assert.False(AppList.Move(keys, "zzz", +1));
        Assert.Equal(new[] { "b", "a", "c" }, keys);
    }

    [Fact]
    public void Settings_keep_programs_and_sites_and_drop_empty_or_duplicate_ones()
    {
        var settings = new AppSettings
        {
            ManualApps = new()
            {
                new ManualApp { Name = "", Path = @"C:\Tools\tool.exe" },
                new ManualApp { Name = "Dup", Path = @"c:\tools\TOOL.exe" },
                new ManualApp { Name = "", Url = "https://www.youtube.com/" },
                new ManualApp { Name = "Nothing" },
            },
            HiddenApps = new() { "Discord", "discord", " " },
            AppsOrder = new() { "b:Edge", "b:Edge", "" },
        };
        settings.Normalize();

        Assert.Equal(2, settings.ManualApps.Count);
        Assert.Equal("tool", settings.ManualApps[0].Name);
        Assert.True(settings.ManualApps[1].IsWeb);
        Assert.Equal("Youtube", settings.ManualApps[1].Name);
        Assert.Single(settings.HiddenApps);
        Assert.Single(settings.AppsOrder);
    }
}
