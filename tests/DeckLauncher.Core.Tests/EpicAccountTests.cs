using DeckLauncher.Core.Epic;
using DeckLauncher.Core.Settings;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class EpicAccountTests
{
    private const string Code = "0123456789abcdef0123456789ABCDEF";

    [Theory]
    [InlineData("{\"redirectUrl\":\"https://x\",\"sid\":null,\"authorizationCode\":\"" + Code + "\"}")]
    [InlineData("{ \"authorizationCode\" : \"" + Code + "\" }")]
    [InlineData(Code)]
    [InlineData("  \"" + Code + "\"  ")]
    public void Finds_the_code_in_what_the_user_copied(string text) =>
        Assert.Equal(Code, EpicAccountClient.ExtractAuthCode(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("{\"authorizationCode\":null}")]
    [InlineData("0123456789abcdef")]
    public void Ignores_other_clipboard_text(string? text) =>
        Assert.Null(EpicAccountClient.ExtractAuthCode(text));

    [Fact]
    public void Reads_library_pages_and_skips_private_and_engine_items()
    {
        const string json = """
        {"records":[
          {"appName":"Sugar","namespace":"ns1","catalogItemId":"id1","sandboxType":"PUBLIC"},
          {"appName":"UE","namespace":"ue","catalogItemId":"id2"},
          {"appName":"Priv","namespace":"ns3","catalogItemId":"id3","sandboxType":"PRIVATE"},
          {"appName":"","namespace":"ns4","catalogItemId":"id4"}
        ],"responseMetadata":{"nextCursor":"abc"}}
        """;
        var assets = EpicAccountClient.ParseAssets(json, out var next);
        Assert.Single(assets);
        Assert.Equal("Sugar", assets[0].AppName);
        Assert.Equal("abc", next);
        Assert.Empty(EpicAccountClient.ParseAssets("not json", out var none));
        Assert.Null(none);
    }

    private static string Item(string title, string categories, string extra = "") => $$"""
        {"id1":{"title":"{{title}}","categories":[{{categories}}],{{extra}}"keyImages":[
          {"type":"Thumbnail","url":"https://cdn1.epicgames.com/t.jpg"},
          {"type":"DieselGameBoxTall","url":"https://cdn1.epicgames.com/tall.jpg"},
          {"type":"DieselGameBox","url":"https://cdn1.epicgames.com/wide.jpg"},
          {"type":"DieselGameBoxLogo","url":"https://cdn1.epicgames.com/logo.png"}]}
        }
        """;

    private static readonly EpicAccountClient.Asset Asset = new("Sugar", "ns1", "id1", "PUBLIC");

    [Fact]
    public void A_game_becomes_a_library_entry_with_the_tall_cover()
    {
        var game = EpicAccountClient.ParseCatalogItem(Item("Celeste™", "{\"path\":\"games\"},{\"path\":\"applications\"}"), Asset);
        Assert.NotNull(game);
        Assert.Equal("Celeste", game!.Title);
        Assert.Equal("Sugar", game.AppName);
        Assert.Equal("https://cdn1.epicgames.com/tall.jpg", game.CoverUrl);
        Assert.Equal("https://cdn1.epicgames.com/wide.jpg", game.WideUrl);
        Assert.Equal("https://cdn1.epicgames.com/logo.png", game.LogoUrl);
        Assert.True(game.ArtChecked);
    }

    [Theory]
    [InlineData("{\"path\":\"games\"}")]
    [InlineData("{\"path\":\"applications\"},{\"path\":\"digitalextras\"}")]
    [InlineData("{\"path\":\"applications\"},{\"path\":\"plugins\"}")]
    public void Non_games_are_left_out(string categories) =>
        Assert.Null(EpicAccountClient.ParseCatalogItem(Item("X", categories), Asset));

    [Fact]
    public void Add_ons_and_other_store_games_are_left_out()
    {
        const string apps = "{\"path\":\"applications\"}";
        Assert.Null(EpicAccountClient.ParseCatalogItem(Item("DLC", apps, "\"mainGameItem\":{\"id\":\"m\"},"), Asset));
        Assert.NotNull(EpicAccountClient.ParseCatalogItem(Item("Launchable", apps + ",{\"path\":\"addons/launchable\"}", "\"mainGameItem\":{\"id\":\"m\"},"), Asset));
        Assert.Null(EpicAccountClient.ParseCatalogItem(Item("EA", apps, "\"customAttributes\":{\"ThirdPartyManagedApp\":{\"value\":\"Origin\"}},"), Asset));
        Assert.Null(EpicAccountClient.ParseCatalogItem(Item("Ubi", apps, "\"customAttributes\":{\"partnerLinkType\":{\"value\":\"ubisoft\"}},"), Asset));
        Assert.Null(EpicAccountClient.ParseCatalogItem("{}", Asset));
        Assert.Null(EpicAccountClient.ParseCatalogItem("oops", Asset));
    }

    [Fact]
    public void Tokens_survive_a_round_trip_and_bad_text_is_not_a_session()
    {
        var tokens = new EpicTokens
        {
            AccessToken = "a", RefreshToken = "r", AccountId = "acc",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), RefreshExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
        };
        var back = EpicTokens.FromJson(tokens.ToJson());
        Assert.NotNull(back);
        Assert.Equal("acc", back!.AccountId);
        Assert.False(back.AccessExpired);
        Assert.True(back.CanRefresh);
        Assert.Null(EpicTokens.FromJson(null));
        Assert.Null(EpicTokens.FromJson("garbage"));
    }

    [Fact]
    public void Settings_keep_a_clean_epic_library()
    {
        var s = new AppSettings
        {
            EpicLibrary = new()
            {
                new EpicLibraryGame { AppName = "A", Title = "Game A", CatalogNamespace = "n", CatalogItemId = "i" },
                new EpicLibraryGame { AppName = "A", Title = "Dup", CatalogNamespace = "n", CatalogItemId = "i" },
                new EpicLibraryGame { AppName = "B", Title = "", CatalogNamespace = "n", CatalogItemId = "j" },
            },
            EpicSkipped = new() { "x", "x" },
        };
        s.Normalize();
        Assert.Single(s.EpicLibrary);
        Assert.Single(s.EpicSkipped);
    }

    [Fact]
    public void Latest_builds_are_read_by_app_name()
    {
        var builds = EpicAccountClient.ParseBuilds("[{\"appName\":\"Sugar\",\"buildVersion\":\"1.2.3\"},{\"appName\":\"\",\"buildVersion\":\"9\"},{\"labelName\":\"x\"}]");
        Assert.Single(builds);
        Assert.Equal("1.2.3", builds["sugar"]);
        Assert.Empty(EpicAccountClient.ParseBuilds("nope"));
        Assert.Empty(EpicAccountClient.ParseBuilds("{}"));
    }

    [Fact]
    public void The_installed_build_comes_from_the_manifest()
    {
        var game = DeckLauncher.Core.Epic.EpicProvider.ParseManifest("{\"AppName\":\"Sugar\",\"DisplayName\":\"Celeste\",\"AppVersionString\":\"1.2.0\"}");
        Assert.Equal("1.2.0", game!.Version);
    }

    [Fact]
    public void Only_epic_servers_may_supply_pictures()
    {
        var game = new Game("Epic", "Sugar", "Celeste") { WideUrl = "https://evil.example.com/x.jpg", CoverUrl = "https://cdn1.epicgames.com/t.jpg" };
        Assert.Empty(CoverDownloader.Urls(game, StoreArtKind.Backdrop));
        Assert.Single(CoverDownloader.Urls(game, StoreArtKind.Cover));
    }
}
