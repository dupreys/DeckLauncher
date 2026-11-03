using System.Collections;
using System.Windows;
using System.Windows.Media;

namespace DeckLauncher.App;

// "Preto OLED": the navy tones of Theme.xaml (all used through DynamicResource) are swapped for neutral blacks.
public partial class MainWindow
{
    private static readonly string[] CoreThemeKeys = { "Bg", "BgDeep", "Panel", "PanelRaised", "Focus" };
    private List<(string Key, Color Color, bool IsBrush)>? _themeOriginals;

    /// <summary>Applies (or removes) the OLED overrides according to the saved setting.</summary>
    private void ApplyOled()
    {
        var resources = Application.Current.Resources;
        _themeOriginals ??= CollectThemeColors();
        foreach (var (key, color, isBrush) in _themeOriginals)
        {
            if (!App.Settings.OledBlack)
            {
                resources.Remove(key); // back to the entry in Theme.xaml
                continue;
            }
            var shade = OledShade(key, color);
            resources[key] = isBrush ? new SolidColorBrush(shade) : shade;
        }
    }

    private static List<(string Key, Color Color, bool IsBrush)> CollectThemeColors()
    {
        var list = new List<(string, Color, bool)>();
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key) continue;
                if (entry.Value is SolidColorBrush brush && (key.StartsWith("T_") || CoreThemeKeys.Contains(key))) list.Add((key, brush.Color, true));
                else if (entry.Value is Color color && (key.StartsWith("C_") || key == "BgColor")) list.Add((key, color, false));
            }
        }
        return list;
    }

    /// <summary>Dark navy becomes black; mid greys lose their blue tint.</summary>
    private static Color OledShade(string key, Color color)
    {
        if (key is "Bg" or "BgDeep" or "BgColor") return Color.FromArgb(color.A, 0, 0, 0);
        var luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        var gray = luminance < 24 ? 0 : luminance < 80 ? luminance - 14 : luminance;
        var value = (byte)Math.Clamp(Math.Round(gray), 0, 255);
        return Color.FromArgb(color.A, value, value, value);
    }

    private void OnOledSwitchClick(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Settings.OledBlack = OledSwitch.IsChecked == true;
        SaveSettings();
        ApplyOled();
    }
}
