using System.Windows;

namespace DeckLauncher.App;

// The screens differ a lot (a 1080p monitor, a 4K TV at 250% scaling that leaves only 1536x864 usable points, a 768p laptop), while the pages
// are drawn for 1920x1080. The whole interface is scaled so that area always fits, instead of the side menu and the pages running off the screen.
public partial class MainWindow
{
    private const double DesignWidth = 1920;
    private const double DesignHeight = 1080;

    /// <summary>The factor that makes the 1920x1080 design fit a window of this size (never distorts: one factor for both directions).</summary>
    internal static double ScaleFor(double width, double height)
    {
        if (width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height)) return 1;
        var scale = Math.Min(width / DesignWidth, height / DesignHeight);
        // A screen that is already (about) 1920x1080 keeps scale 1: a factor like 0.999 would only blur the lines and clip the outlines of the cards.
        if (scale is > 0.97 and < 1.03) return 1;
        // Steps of 0.05 (rounded down, so it never overflows): the same screen always gets the same crisp factor.
        return Math.Clamp(Math.Floor(scale * 20) / 20, 0.4, 4.0);
    }

    private double _uiScale = 1;

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var scale = ScaleFor(ActualWidth, ActualHeight);
        if (Math.Abs(_uiScale - scale) < 0.001) return;
        _uiScale = scale;
        // At scale 1 there is no layout transform at all (the interface is exactly as drawn); otherwise one factor for both directions.
        RootGrid.LayoutTransform = scale == 1 ? System.Windows.Media.Transform.Identity : new System.Windows.Media.ScaleTransform(scale, scale);
    }
}
