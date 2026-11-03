using LiteLauncher.Core.Settings;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class QuickMessageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ll-quick-" + Guid.NewGuid().ToString("N"));

    public QuickMessageTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Defaults_fill_the_eight_wheel_slots()
    {
        var settings = new AppSettings();
        Assert.Equal(AppSettings.QuickMessageSlots, settings.QuickMessages.Count);
        Assert.Equal("E aí!!", settings.QuickMessages[0]);
    }

    [Fact]
    public void Blank_missing_and_long_messages_are_repaired()
    {
        var result = AppSettings.NormalizeQuickMessages(new[] { "  Oi  ", "", new string('x', 200) });

        Assert.Equal(AppSettings.QuickMessageSlots, result.Count);
        Assert.Equal("Oi", result[0]);
        Assert.Equal(AppSettings.DefaultQuickMessages[1], result[1]);
        Assert.Equal(AppSettings.MaxQuickMessageLength, result[2].Length);
        Assert.Equal(AppSettings.DefaultQuickMessages[7], result[7]);
    }

    [Fact]
    public void Edited_messages_and_the_chat_token_survive_a_save_and_load()
    {
        var store = new SettingsStore(Path.Combine(_dir, "s.json"));
        var settings = new AppSettings { SteamChatToken = "header.payload.signature" };
        settings.QuickMessages[3] = "Bora jogar?";
        store.Save(settings);

        var loaded = store.Load();
        Assert.Equal("Bora jogar?", loaded.QuickMessages[3]);
        Assert.Equal("header.payload.signature", loaded.SteamChatToken);
    }

    [Fact]
    public void On_windows_the_chat_token_is_not_stored_in_plain_text()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = Path.Combine(_dir, "s.json");
        new SettingsStore(path).Save(new AppSettings { SteamChatToken = "header.payload.signature" });
        Assert.DoesNotContain("header.payload.signature", File.ReadAllText(path));
    }
}
