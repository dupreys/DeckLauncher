using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DeckLauncher.App;

// Settings > Interface: highlight colour, card size and big clock.
public partial class MainWindow
{
    private static readonly Dictionary<string, Color> AccentColors = new()
    {
        ["Blue"] = Color.FromRgb(0x1A, 0x9F, 0xFF),
        ["Green"] = Color.FromRgb(0x3D, 0xBE, 0x6B),
        ["Purple"] = Color.FromRgb(0x9B, 0x6B, 0xFF),
        ["Orange"] = Color.FromRgb(0xFF, 0x8A, 0x2B),
        ["Pink"] = Color.FromRgb(0xFF, 0x5F, 0xA2),
        ["Red"] = Color.FromRgb(0xFF, 0x4B, 0x4B),
    };

    /// <summary>Scale of the library cards: 0.85, 1 or 1.15.</summary>
    private double CardScale => App.Settings.CardSize switch { "Small" => 0.85, "Large" => 1.15, _ => 1.0 };

    private void ApplyAppearance()
    {
        ApplyAccent();
        ApplyCardSize();
        ApplyBigClock();
    }

    private void ApplyAccent()
    {
        var color = AccentColors.TryGetValue(App.Settings.AccentName, out var c) ? c : AccentColors["Blue"];
        // Everything that shows the accent uses DynamicResource, so a new brush at the top level updates it all at once.
        Application.Current.Resources["Accent"] = new SolidColorBrush(color);
        GlowCore.Color = Color.FromArgb(0x4C, color.R, color.G, color.B);
        GlowEdge.Color = Color.FromArgb(0x00, color.R, color.G, color.B);
    }

    private void ApplyCardSize()
    {
        var scale = CardScale;
        LibGrid.LayoutTransform = scale == 1.0 ? Transform.Identity : new ScaleTransform(scale, scale);
        UpdateLibGridWidth();
        LibCursor.Visibility = Visibility.Collapsed;
    }

    private void ApplyBigClock()
    {
        var big = App.Settings.BigClock;
        Clock.FontSize = big ? 32 : 22;
        RoomClock.FontSize = big ? 170 : 124;
        RoomGreeting.FontSize = big ? 32 : 24;
    }

    private void ShowAppearanceChecks()
    {
        SetChoice(App.Settings.AccentName, (AccentBlue, "Blue"), (AccentGreen, "Green"), (AccentPurple, "Purple"),
                  (AccentOrange, "Orange"), (AccentPink, "Pink"), (AccentRed, "Red"));
        SetChoice(App.Settings.CardSize, (SizeSmall, "Small"), (SizeMedium, "Medium"), (SizeLarge, "Large"));
        BigClockSwitch.IsChecked = App.Settings.BigClock;
    }

    private void OnAccentClick(object sender, RoutedEventArgs e) =>
        OnChoiceClick(sender, key => App.Settings.AccentName = key, ShowAppearanceChecks, ApplyAccent);

    private void OnCardSizeClick(object sender, RoutedEventArgs e) =>
        OnChoiceClick(sender, key => App.Settings.CardSize = key, ShowAppearanceChecks, ApplyCardSize);

    private void OnBigClockClick(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Settings.BigClock = BigClockSwitch.IsChecked == true;
        SaveSettings();
        ApplyBigClock();
    }
}
