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

    [Theory]
    [InlineData("Square", "Square")]
    [InlineData("Poster", "Poster")]
    [InlineData("round", "Poster")]
    public void Card_shape_falls_back_to_poster(string saved, string expected)
    {
        var settings = new AppSettings { CardShape = saved };
        settings.Normalize();
        Assert.Equal(expected, settings.CardShape);
    }

    [Fact]
    public void Defaults_keep_the_current_look()
    {
        var settings = new AppSettings();
        Assert.Equal("Blue", settings.AccentName);
        Assert.Equal("Medium", settings.CardSize);
        Assert.Equal("Poster", settings.CardShape);
        Assert.False(settings.BigClock);
    }
}

public class AvatarPresetTests
{
    [Fact]
    public void Preset_needs_a_picture_and_a_valid_name()
    {
        var ok = new AppSettings { CustomAvatarPath = "x.png", AvatarPreset = "avatar-03" };
        ok.Normalize();
        Assert.Equal("avatar-03", ok.AvatarPreset);

        var noPicture = new AppSettings { AvatarPreset = "avatar-03" };
        noPicture.Normalize();
        Assert.Null(noPicture.AvatarPreset);

        var bad = new AppSettings { CustomAvatarPath = "x.png", AvatarPreset = "..\\evil" };
        bad.Normalize();
        Assert.Null(bad.AvatarPreset);
    }
}
