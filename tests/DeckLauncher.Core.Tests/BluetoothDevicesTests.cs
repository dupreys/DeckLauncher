using DeckLauncher.Core.Settings;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class BluetoothDevicesTests
{
    [Fact]
    public void AddressRoundTrips()
    {
        Assert.True(SavedBluetoothDevice.TryParseAddress("AA:BB:CC:00:11:22", out var address));
        Assert.Equal(0xAABBCC001122UL, address);
        Assert.Equal("AABBCC001122", SavedBluetoothDevice.FormatAddress(address));
    }

    [Theory]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("AABBCC00112")]
    public void BadAddressIsRejected(string text) => Assert.False(SavedBluetoothDevice.TryParseAddress(text, out _));

    [Fact]
    public void CleanDropsInvalidAndDuplicates()
    {
        var cleaned = SavedBluetoothDevice.Clean(new[]
        {
            new SavedBluetoothDevice { Name = " Fone A ", Address = "aa-bb-cc-00-11-22" },
            new SavedBluetoothDevice { Name = "Repetido", Address = "AABBCC001122" },
            new SavedBluetoothDevice { Name = "Ruim", Address = "nope" },
            new SavedBluetoothDevice { Name = "", Address = "010203040506" },
        });
        Assert.Equal(2, cleaned.Count);
        Assert.Equal("Fone A", cleaned[0].Name);
        Assert.Equal("Fone de ouvido", cleaned[1].Name);
    }

    [Fact]
    public void SettingsNormalizeCleansTheList()
    {
        var settings = new AppSettings { BluetoothDevices = new() { new SavedBluetoothDevice { Name = "x", Address = "bad" } } };
        settings.Normalize();
        Assert.Empty(settings.BluetoothDevices);
        Assert.True(settings.BluetoothAutoConnect);
    }
}
