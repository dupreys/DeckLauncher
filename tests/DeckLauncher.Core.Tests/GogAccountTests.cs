using DeckLauncher.Core;
using DeckLauncher.Core.Gog;
using DeckLauncher.Core.Settings;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class GogAccountTests
{
    private const string Code = "abcDEF123_-abcDEF123_-abcDEF123_-abcDEF123_-";

    [Fact]
    public void The_code_is_read_from_the_success_address()
    {
        Assert.Equal(Code, GogAccountClient.ExtractAuthCode($"https://embed.gog.com/on_login_success?origin=client&code={Code}"));
        Assert.Equal(Code, GogAccountClient.ExtractAuthCode($"  https://embed.gog.com/on_login_success?code={Code}&origin=client "));
    }

    [Fact]
    public void A_bare_code_is_accepted_and_other_text_is_not()
    {
        Assert.Equal(Code, GogAccountClient.ExtractAuthCode(Code));
        Assert.Null(GogAccountClient.ExtractAuthCode("hello world"));
        Assert.Null(GogAccountClient.ExtractAuthCode(null));
        Assert.Null(GogAccountClient.ExtractAuthCode($"https://example.com/?code={Code}")); // only GOG's own address counts
        Assert.Null(GogAccountClient.ExtractAuthCode("https://www.gog.com/en/games"));
    }

    [Fact]
    public void The_login_address_is_gogs_own()
    {
        Assert.StartsWith("https://auth.gog.com/auth?", GogAccountClient.LoginUrl);
        Assert.Contains("response_type=code", GogAccountClient.LoginUrl);
    }

    [Fact]
    public void Tokens_come_from_the_answer_and_survive_json()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tokens = GogTokens.FromResponse("""{"access_token":"a","refresh_token":"r","expires_in":3600,"user_id":"123","token_type":"bearer"}""", now);
        Assert.NotNull(tokens);
        Assert.Equal("a", tokens!.AccessToken);
        Assert.Equal("123", tokens.UserId);
        Assert.Equal(now.AddHours(1), tokens.ExpiresAt);

        var back = GogTokens.FromJson(tokens.ToJson());
        Assert.NotNull(back);
        Assert.Equal("r", back!.RefreshToken);
        Assert.Null(GogTokens.FromResponse("""{"error":"invalid_grant"}""", now));
        Assert.Null(GogTokens.FromResponse("not json", now));
        Assert.Null(GogTokens.FromJson("{}"));
    }

    [Fact]
    public void Products_become_games_without_movies()
    {
        const string json = """
        {"totalPages":3,"products":[
          {"id":1207664643,"title":"The Witcher® 3: Wild Hunt","slug":"the_witcher_3_wild_hunt_game","image":"//images-4.gog.com/abc123","isGame":true,"isMovie":false},
          {"id":99,"title":"A Movie","slug":"movie","image":"//images-1.gog.com/m","isGame":false,"isMovie":true},
          {"id":"1495134320","title":"Cyberpunk 2077","slug":"cyberpunk_2077","image":"https://evil.example.com/x","isGame":true},
          {"title":"No id"}
        ]}
        """;
        var games = GogAccountClient.ParseProducts(json, out var pages);
        Assert.Equal(3, pages);
        Assert.Equal(2, games.Count);
        Assert.Equal("1207664643", games[0].Id);
        Assert.Equal("The Witcher 3: Wild Hunt", games[0].Title);
        Assert.Equal("https://images-4.gog.com/abc123", games[0].ImageBase);
        Assert.Null(games[1].ImageBase); // not a picture served by GOG
        Assert.Empty(GogAccountClient.ParseProducts("garbage", out _));
    }

    [Fact]
    public void Only_gog_servers_are_trusted_for_pictures()
    {
        Assert.True(GogAccountClient.IsGogImageUrl("https://images-1.gog.com/x"));
        Assert.True(GogAccountClient.IsGogImageUrl("https://images.gog-statics.com/x"));
        Assert.False(GogAccountClient.IsGogImageUrl("http://images-1.gog.com/x"));
        Assert.False(GogAccountClient.IsGogImageUrl("https://gog.com.evil.example/x"));
        Assert.False(GogAccountClient.IsGogImageUrl(null));
    }

    [Fact]
    public void Store_page_uses_the_slug()
    {
        Assert.Equal("https://www.gog.com/en/game/cyberpunk_2077", GogAccountClient.StorePageUrl("cyberpunk_2077"));
        Assert.Equal("https://www.gog.com/games", GogAccountClient.StorePageUrl(null));
    }

    [Fact]
    public void Cover_cache_and_urls_work_for_gog_games()
    {
        var game = new Game("GOG", "1495134320", "Cyberpunk 2077") { CoverUrl = "https://images-4.gog.com/abc" };
        var urls = CoverDownloader.Urls(game, StoreArtKind.Cover);
        Assert.Equal(2, urls.Count);
        Assert.All(urls, u => Assert.StartsWith("https://images-4.gog.com/abc", u));
        Assert.Empty(CoverDownloader.Urls(game, StoreArtKind.Backdrop));
        Assert.Contains("gog_1495134320_cover", CoverDownloader.CachePath("c", game, StoreArtKind.Cover));
        Assert.Empty(CoverDownloader.Urls(game with { CoverUrl = "https://evil.example.com/x" }, StoreArtKind.Cover));
    }

    [Fact]
    public void Tokens_stay_out_of_backups()
    {
        var settings = new AppSettings { GogTokens = "{\"secret\":1}", GogAccountName = "me" };
        var exported = SettingsBackup.Export(settings);
        Assert.DoesNotContain("secret", exported);
    }
}
