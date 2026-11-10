using DeckLauncher.Core.GameAchievements;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class GameAchievementsTests
{
    [Fact]
    public void Steam_result_becomes_a_sorted_list()
    {
        var result = new AchievementResult(AchievementStatus.Ok, 1, 3)
        {
            Items = new[]
            {
                new AchievementItem("Locked one", "Do it", false),
                new AchievementItem("Won", "Done", true, DateTimeOffset.FromUnixTimeSeconds(1700000000), "https://cdn.steamstatic.com/b.jpg"),
                new AchievementItem("Secret", "", false),
            },
        };
        var set = SteamAchievements.From(result);
        Assert.Null(set.Problem);
        Assert.Equal(new[] { "Won", "Locked one", "Secret" }, set.Items.Select(a => a.Name));
        Assert.Equal(1, set.UnlockedCount);
        Assert.True(set.Items[2].Hidden);
        Assert.False(set.Items[1].Hidden);
    }

    [Theory]
    [InlineData(AchievementStatus.None, "none")]
    [InlineData(AchievementStatus.NotSignedIn, "noid")]
    [InlineData(AchievementStatus.Unavailable, "private")]
    [InlineData(AchievementStatus.NotSupported, "source")]
    public void Steam_problems_are_named(AchievementStatus status, string problem) =>
        Assert.Equal(problem, SteamAchievements.From(new AchievementResult(status)).Problem);

    private const string Definitions = """
        {"data":{"Achievement":{"productAchievementsRecordBySandbox":{"achievements":{"achievement":[
          {"name":"A1","hidden":false,"unlockedDisplayName":"First","lockedDisplayName":"First?","unlockedDescription":"You did","lockedDescription":"Do it","unlockedIconLink":"https://cdn1.epicgames.com/u.png","lockedIconLink":"https://cdn1.epicgames.com/l.png","rarity":{"percent":12.5}},
          {"name":"A2","hidden":true,"unlockedDisplayName":"Second","lockedDisplayName":"Secret","unlockedDescription":"x","lockedDescription":"","unlockedIconLink":"https://evil.example.com/u.png","lockedIconLink":"https://cdn1.epicgames.com/l.png"}
        ]}}}}}
        """;

    private const string Progress = """
        {"data":{"PlayerAchievement":{"playerAchievementGameRecordsBySandbox":{"records":[{"playerAchievements":{"playerAchievement":[
          {"achievementName":"A1","unlocked":true,"unlockDate":"2024-05-01T10:00:00.000Z"},{"achievementName":"A2","unlocked":false,"unlockDate":"1970-01-01T00:00:00.000Z"}
        ]}}]}}}}
        """;

    [Fact]
    public void Epic_definitions_and_progress_are_joined()
    {
        var set = EpicAchievementsClient.Merge(Definitions, Progress);
        Assert.Null(set.Problem);
        Assert.Equal(2, set.Items.Count);
        var first = set.Items[0];
        Assert.Equal("First", first.Name);
        Assert.True(first.Unlocked);
        Assert.Equal("https://cdn1.epicgames.com/u.png", first.IconUrl);
        Assert.Equal(12.5, first.RarityPercent);
        Assert.Equal(2024, first.UnlockedAt!.Value.Year);
        var second = set.Items[1];
        Assert.False(second.Unlocked);
        Assert.True(second.Hidden);
        Assert.Null(second.UnlockedAt);
    }

    [Fact]
    public void Epic_without_progress_is_marked_and_errors_are_not_an_empty_list()
    {
        Assert.Equal("noprogress", EpicAchievementsClient.Merge(Definitions, null).Problem);
        Assert.Equal("noprogress", EpicAchievementsClient.Merge(Definitions, "{\"errors\":[{\"message\":\"x\"}]}").Problem);
        Assert.Equal("fetch", EpicAchievementsClient.Merge("{\"errors\":[{\"message\":\"x\"}]}", Progress).Problem);
        Assert.Equal("fetch", EpicAchievementsClient.Merge("garbage", null).Problem);
    }
}
