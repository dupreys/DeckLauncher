using DeckLauncher.Core.Apps;
using DeckLauncher.Core.Settings;
using DeckLauncher.Core.Steam;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class FriendChatAndBackupTests
{
    [Fact]
    public void Chat_link_opens_the_official_client_for_a_plain_steam_id()
        => Assert.Equal("steam://friends/message/76561198000000000", SteamFriends.ChatUrl("76561198000000000"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("7656119800000000x")]
    [InlineData("76561198000000000/../../x")]
    [InlineData("76561198000000000 ")]
    [InlineData("765611980000000001234567")]
    public void Chat_link_is_refused_for_anything_that_is_not_a_steam_id(string? id) => Assert.Null(SteamFriends.ChatUrl(id));

    [Theory]
    [InlineData(0, null, "Offline")]
    [InlineData(1, null, "Online")]
    [InlineData(2, null, "Ocupado")]
    [InlineData(3, null, "Ausente")]
    [InlineData(4, null, "Ausente")]
    [InlineData(1, "Hades", "Jogando Hades")]
    [InlineData(0, "  ", "Offline")]
    public void Status_text(int state, string? game, string expected) => Assert.Equal(expected, SteamFriends.StatusOf(state, game));

    [Fact]
    public void Backup_round_trip_keeps_apps_and_drops_secrets()
    {
        var settings = new AppSettings
        {
            SteamWebApiKey = "secret-key",
            SteamGridDbApiKey = "other-secret",
            NotifiedUpdate = "v9.9.9",
            HiddenApps = new() { "Discord" },
            AppsOrder = new() { "b:Microsoft Edge", "m:https://youtube.com/" },
            ManualApps = new() { new ManualApp { Name = "YouTube", Url = "https://youtube.com/" } },
        };

        var json = SettingsBackup.Export(settings);
        Assert.DoesNotContain("secret-key", json);
        Assert.DoesNotContain("other-secret", json);
        Assert.DoesNotContain("v9.9.9", json);

        var back = SettingsBackup.Import(json);
        Assert.NotNull(back);
        Assert.Null(back!.SteamWebApiKey);
        Assert.Null(back.NotifiedUpdate);
        Assert.Equal(new[] { "Discord" }, back.HiddenApps);
        Assert.Equal(new[] { "b:Microsoft Edge", "m:https://youtube.com/" }, back.AppsOrder);
        Assert.Single(back.ManualApps);
        Assert.True(back.ManualApps[0].IsWeb);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"format\":\"other\",\"version\":1,\"settings\":{}}")]
    public void Backup_import_rejects_garbage(string json) => Assert.Null(SettingsBackup.Import(json));

    [Fact]
    public void Normalize_clamps_values_edited_by_hand()
    {
        var settings = new AppSettings { BackdropDim = 5, UiVolume = -3, Accent = " ", HomeStyle = "Nonsense", LibrarySort = "x" };
        settings.Normalize();
        Assert.Equal(0.9, settings.BackdropDim);
        Assert.Equal(0, settings.UiVolume);
        Assert.Equal("#4C9AFF", settings.Accent);
        Assert.Equal("Xbox", settings.HomeStyle);
        Assert.Equal("Recent", settings.LibrarySort);
    }

    [Fact]
    public void Start_apps_filter_ignores_accents_case_and_punctuation()
    {
        var apps = new[]
        {
            new StartApp("AMD Software: Adrenalin Edition", "amd"),
            new StartApp("Configurações", "cfg"),
            new StartApp("NVIDIA app", "nv"),
            new StartApp("Discord Canary", "dc"),
        };

        var found = StartApps.Filter(apps, new[] { "amd software", "configuracoes", "NVIDIA App", "Discord" });
        Assert.Equal(new[] { "amd", "cfg", "nv", "dc" }, found.Select(a => a.AppId));
        Assert.Empty(StartApps.Filter(apps, new[] { "Disc" })); // whole words only
    }
}
