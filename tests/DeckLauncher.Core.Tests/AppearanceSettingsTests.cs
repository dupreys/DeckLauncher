using DeckLauncher.Core.Settings;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class AppearanceSettingsTests
{
    [Theory]
    [InlineData("Green", "Green")]
    [InlineData("Red", "Red")]
    [InlineData("Nonsense", "Blue")]
    [InlineData("", "Blue")]
    public void Accent_name_falls_back_to_blue(string saved, string expected)
    {
        var settings = new AppSettings { AccentName = saved };
        settings.Normalize();
        Assert.Equal(expected, settings.AccentName);
    }

    [Theory]
    [InlineData("Small", "Small")]
    [InlineData("Large", "Large")]
    [InlineData("huge", "Medium")]
    public void Card_size_falls_back_to_medium(string saved, string expected)
    {
        var settings = new AppSettings { CardSize = saved };
        settings.Normalize();
        Assert.Equal(expected, settings.CardSize);
    }

    [Fact]
    public void Defaults_keep_the_current_look()
    {
        var settings = new AppSettings();
        Assert.Equal("Blue", settings.AccentName);
        Assert.Equal("Medium", settings.CardSize);
        Assert.False(settings.BigClock);
    }
}
