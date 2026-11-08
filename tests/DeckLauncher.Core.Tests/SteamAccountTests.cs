using DeckLauncher.Core.Settings;
using DeckLauncher.Core.Steam;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class SteamAccountTests
{
    private const string LoginUsers = """
        "users"
        {
            "76561198000000001" { "AccountName" "ana" "PersonaName" "Ana" "MostRecent" "0" "Timestamp" "100" "RememberPassword" "1" }
            "76561198000000002" { "AccountName" "bruno" "PersonaName" "Bruno" "MostRecent" "1" "Timestamp" "50" }
            "notanid" { "AccountName" "x" }
        }
        """;

    [Fact]
    public void Reads_every_account_most_recent_first()
    {
        var accounts = SteamLoginUsers.ParseAll(LoginUsers);
        Assert.Equal(2, accounts.Count);
        Assert.Equal("76561198000000002", accounts[0].SteamId64);
        Assert.Equal("Ana", accounts[1].PersonaName);
        Assert.True(accounts[1].RememberPassword);
        Assert.False(accounts[0].RememberPassword);
    }

    [Fact]
    public void Account_id_32_is_the_userdata_folder_name()
    {
        Assert.Equal("39734273", SteamAccountFilter.AccountId32("76561198000000001"));
        Assert.Equal("", SteamAccountFilter.AccountId32("abc"));
    }

    [Fact]
    public void Reads_app_ids_from_local_config()
    {
        var ids = SteamAccountFilter.ParseAppIds("""
            "UserLocalConfigStore" { "Software" { "Valve" { "Steam" { "apps" { "730" { "LastPlayed" "1" } "570" { } } } } } }
            """);
        Assert.Equal(new[] { "570", "730" }, ids.OrderBy(i => i));
    }

    [Fact]
    public void Keeps_own_games_hides_the_others_and_never_hides_non_steam()
    {
        var mine = new HashSet<string> { "730" };
        Game Steam(string id, string? owner) => new("Steam", id, "g" + id) { Owner = owner };

        Assert.True(SteamAccountFilter.Keep(Steam("1", "A"), "A", mine));      // installed by this account
        Assert.True(SteamAccountFilter.Keep(Steam("730", "B"), "A", mine));    // in this account's config
        Assert.False(SteamAccountFilter.Keep(Steam("2", "B"), "A", mine));     // someone else's
        Assert.True(SteamAccountFilter.Keep(new Game("Epic", "x", "e") { Owner = "B" }, "A", mine));
        Assert.True(SteamAccountFilter.Keep(Steam("3", null), "A", new HashSet<string>())); // nothing to compare with
    }

    [Theory]
    [InlineData("Last", "Last")]
    [InlineData("Fixed", "Fixed")]
    [InlineData("nonsense", "Ask")]
    public void Account_mode_falls_back_to_ask(string saved, string expected)
    {
        var settings = new AppSettings { AccountMode = saved, FixedAccountId = "not-an-id" };
        settings.Normalize();
        Assert.Equal(expected, settings.AccountMode);
        Assert.Null(settings.FixedAccountId);
    }

    [Fact]
    public void Marks_only_the_chosen_account_as_most_recent()
    {
        var result = SteamAccountSwitch.MarkMostRecent(LoginUsers, "76561198000000001");
        var accounts = SteamLoginUsers.ParseAll(result);
        Assert.Equal("76561198000000001", accounts[0].SteamId64);
        Assert.True(accounts[0].MostRecent);
        Assert.False(accounts[1].MostRecent);
    }
}
