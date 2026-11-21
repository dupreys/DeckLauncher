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
        ApplyCardShape();
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
        // The same scale for the library and for the rows of the home styles (the Painel fills the screen by itself).
        Transform Scale() => scale == 1.0 ? Transform.Identity : new ScaleTransform(scale, scale);
        LibGrid.LayoutTransform = Scale();
        RecentHost.LayoutTransform = Scale();
        CinemaRow.LayoutTransform = Scale();
        CarouselRow.LayoutTransform = Scale();
        RoomRow.LayoutTransform = Scale();
        UpdateLibGridWidth();
        LibCursor.Visibility = Visibility.Collapsed;
    }

    /// <summary>Poster cards (tall covers, cropped to fill) or square cards (the whole cover fitted inside, nothing cut).</summary>
    private void ApplyCardShape()
    {
        var square = App.Settings.CardShape == "Square";
        var resources = Application.Current.Resources;
        resources["LibTileH"] = square ? 244.0 : 366.0;
        resources["GameTileW"] = square ? 250.0 : 207.0;
        resources["GameTileH"] = square ? 250.0 : 311.0;
        resources["CinemaW"] = square ? 200.0 : 170.0;
        resources["CinemaH"] = square ? 200.0 : 232.0;
        resources["CarouselTileStyle"] = FindResource(square ? "CarouselTileSquare" : "CarouselTile");
        LibCursor.Visibility = Visibility.Collapsed;
    }

    private void ApplyBigClock()
    {
        var big = App.Settings.BigClock;
        Clock.FontSize = big ? 32 : 22;
        RoomClock.FontSize = big ? 170 : 124;
        RoomGreeting.FontSize = big ? 32 : 24;
        CarouselClock.FontSize = big ? 124 : 88;
        CarouselDate.FontSize = big ? 30 : 22;
    }

    private void ShowAppearanceChecks()
    {
        SetChoice(App.Settings.AccentName, (AccentBlue, "Blue"), (AccentGreen, "Green"), (AccentPurple, "Purple"),
                  (AccentOrange, "Orange"), (AccentPink, "Pink"), (AccentRed, "Red"));
        SetChoice(App.Settings.CardShape, (ShapePoster, "Poster"), (ShapeSquare, "Square"));
        SetChoice(App.Settings.CardSize, (SizeSmall, "Small"), (SizeMedium, "Medium"), (SizeLarge, "Large"));
        BigClockSwitch.IsChecked = App.Settings.BigClock;
    }

    private void OnAccentClick(object sender, RoutedEventArgs e) =>
        OnChoiceClick(sender, key => App.Settings.AccentName = key, ShowAppearanceChecks, ApplyAccent);

    private void OnCardShapeClick(object sender, RoutedEventArgs e) =>
        OnChoiceClick(sender, key => App.Settings.CardShape = key, ShowAppearanceChecks, ApplyCardShape);

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
