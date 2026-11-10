using System.Globalization;

namespace DeckLauncher.Core.Settings;

/// <summary>A headset saved in the launcher: its name and Bluetooth address (12 hex digits, no separators).</summary>
public sealed class SavedBluetoothDevice
{
    public string Name { get; set; } = "";

    public string Address { get; set; } = "";

    public static string FormatAddress(ulong address) => (address & 0xFFFFFFFFFFFFUL).ToString("X12", CultureInfo.InvariantCulture);

    public static bool TryParseAddress(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var hex = text.Replace(":", "").Replace("-", "").Trim();
        return hex.Length == 12 && ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    /// <summary>Drops entries with an invalid address and duplicates, and gives nameless ones a name.</summary>
    public static List<SavedBluetoothDevice> Clean(IEnumerable<SavedBluetoothDevice>? devices)
    {
        var result = new List<SavedBluetoothDevice>();
        var seen = new HashSet<ulong>();
        foreach (var device in devices ?? Enumerable.Empty<SavedBluetoothDevice>())
        {
            if (device is null || !TryParseAddress(device.Address, out var address) || !seen.Add(address)) continue;
            var name = string.IsNullOrWhiteSpace(device.Name) ? "Fone de ouvido" : device.Name.Trim();
            result.Add(new SavedBluetoothDevice { Name = name, Address = FormatAddress(address) });
        }
        return result;
    }
}
