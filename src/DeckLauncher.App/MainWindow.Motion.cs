using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace DeckLauncher.App;

// Motion and sound: hero crossfade, smooth scrolling of the recent games row, page transitions,
// "stay at the end of the list" navigation and the interface sounds.
public partial class MainWindow
{
    private DateTime _navUntil = DateTime.MinValue;
    private double _rowTarget;

    private static readonly DependencyProperty RowOffsetProperty = DependencyProperty.Register(
        "RowOffset", typeof(double), typeof(MainWindow),
        new PropertyMetadata(0.0, (d, e) => ((MainWindow)d).RecentScroll.ScrollToHorizontalOffset((double)e.NewValue)));

    private static IEasingFunction EaseOut => new CubicEase { EasingMode = EasingMode.EaseOut };

    private void InitSounds()
    {
        // A tick when the focus moves because of an arrow key / the controller; a click when something is pressed.
        PreviewKeyDown += OnNavigationKey;
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnAnyFocus), true);
        AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnAnyClick), true);
    }

    private void OnNavigationKey(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Tab)) return;
        if (Keyboard.FocusedElement is TextBox) return;

        if (e.Key is Key.Left or Key.Right &&
            (TryGameMenuMove(e.Key == Key.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right) ||
             TrySettingsMove(e.Key == Key.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right)))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Down && TryOpenFeedFromRow())
        {
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Left or Key.Right or Key.Down && TryListMove(e.Key == Key.Left ? FocusNavigationDirection.Left : e.Key == Key.Right ? FocusNavigationDirection.Right : FocusNavigationDirection.Down))
        {
            e.Handled = true;
            return;
        }
        _navUntil = DateTime.UtcNow.AddMilliseconds(200);
    }

    private void OnAnyFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DateTime.UtcNow < _navUntil && e.OldFocus is not null && e.NewFocus is ButtonBase) UiSound.Play("nav");
    }

    private void OnAnyClick(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, PlayButton)) return; // plays the launch sound instead
        UiSound.Play("select");
    }

    // ---- Left / right inside the game lists ---------------------------------------------------

    /// <summary>
    /// Moves one game to the left or right inside the home row or the library grid, and stays put at the ends
    /// (no wrapping around). Returns true when the key was handled here.
    /// </summary>
    private bool TryListMove(FocusNavigationDirection direction)
    {
        if (direction is not (FocusNavigationDirection.Left or FocusNavigationDirection.Right or FocusNavigationDirection.Down)) return false;
        if (Keyboard.FocusedElement is not UIElement focused) return false;

        List<UIElement> items;
        var columns = int.MaxValue;
        if (IsAncestor(RecentHost, focused))
        {
            items = FindAll<Button>(RecentRow).Cast<UIElement>().ToList();
            if (MoreTile.IsVisible) items.Add(MoreTile);
        }
        else if (IsAncestor(LibGrid, focused))
        {
            items = FindAll<Button>(LibGrid).Cast<UIElement>().ToList();
            columns = Math.Max(1, (int)Math.Round(LibGrid.ActualWidth / 310));
        }
        else return false;

        // Down only matters inside the library grid (the home row hands it to the feed elsewhere).
        if (direction == FocusNavigationDirection.Down && columns == int.MaxValue) return false;

        var index = items.IndexOf(focused);
        if (index < 0)
        {
            // focus is on something inside a tile: find the tile that contains it
            index = items.FindIndex(i => IsAncestor(i, focused));
            if (index < 0) return false;
        }

        if (direction == FocusNavigationDirection.Down)
        {
            // Last row: stay there. A shorter last row is reached through its final game.
            var below = index + columns;
            if (below >= items.Count) below = index / columns < (items.Count - 1) / columns ? items.Count - 1 : index;
            if (below == index)
            {
                UiSound.Play("bump");
                return true;
            }
            _navUntil = DateTime.UtcNow.AddMilliseconds(200);
            items[below].Focus();
            return true;
        }

        var right = direction == FocusNavigationDirection.Right;
        var next = index + (right ? 1 : -1);
        var rowEdge = columns != int.MaxValue && (right ? index % columns == columns - 1 : index % columns == 0);
        if (next < 0 || next >= items.Count || rowEdge)
        {
            UiSound.Play("bump");
            return true;
        }

        _navUntil = DateTime.UtcNow.AddMilliseconds(200);
        items[next].Focus();
        return true;
    }

    // ---- Hero picture crossfade --------------------------------------------------------------

    private void SetHero(ImageSource? image)
    {
        if (ReferenceEquals(HeroImage.Source, image)) return;
        var span = TimeSpan.FromMilliseconds(380);
        HeroImagePrev.Source = HeroImage.Source;
        HeroImagePrev.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, span));
        HeroImage.Source = image;
        HeroImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, span));
    }

    // ---- Smooth scrolling of the recent games row --------------------------------------------

    /// <summary>Left margin of the row: room so the zoom and the outline of the first card are not clipped.</summary>
    private const double RecentHostLeft = 28;

    private void OnRecentBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is not FrameworkElement target) return;
        e.Handled = true; // we scroll ourselves, with an animation

        var left = target.TranslatePoint(new Point(0, 0), RecentHost).X + RecentHostLeft;
        var isGame = target is Button { DataContext: GameItem };
        var finalWidth = target.ActualWidth; // tiles no longer change size when focused
        var viewport = RecentScroll.ViewportWidth;
        if (viewport <= 0) return;

        // Like the Steam home: the focused game stays in the middle of the screen while the row slides under it.
        // Near either end the row stops, and the selection keeps moving towards the edge instead.
        var offset = left + finalWidth / 2 - viewport / 2;

        var contentWidth = RecentHost.ActualWidth + RecentHostLeft;
        offset = Math.Clamp(offset, 0, Math.Max(0, contentWidth - viewport));
        MoveCaption(isGame, left - offset, finalWidth);
        if (Math.Abs(offset - _rowTarget) < 0.5 && Math.Abs(RecentScroll.HorizontalOffset - offset) < 0.5) return;

        _rowTarget = offset;
        BeginAnimation(RowOffsetProperty, new DoubleAnimation(RecentScroll.HorizontalOffset, offset, TimeSpan.FromMilliseconds(280)) { EasingFunction = EaseOut });
    }

    /// <summary>The name and play time under the focused game follow it as the row scrolls.</summary>
    private void MoveCaption(bool visible, double tileX, double tileWidth)
    {
        if (!visible)
        {
            RecentCaption.Visibility = Visibility.Collapsed;
            RecentGlow.Visibility = Visibility.Collapsed;
            return;
        }
        var x = 18 + tileX + 3; // the row starts 18 px in; 3 px is the tile outline
        var glowX = 18 + tileX + tileWidth / 2 - RecentGlow.Width / 2;
        var wasHidden = RecentCaption.Visibility != Visibility.Visible;
        RecentCaption.Visibility = Visibility.Visible;
        RecentGlow.Visibility = Visibility.Visible;
        if (wasHidden)
        {
            CaptionMove.BeginAnimation(TranslateTransform.XProperty, null);
            GlowMove.BeginAnimation(TranslateTransform.XProperty, null);
            CaptionMove.X = x;
            GlowMove.X = glowX;
            return;
        }
        var span = TimeSpan.FromMilliseconds(280);
        CaptionMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(CaptionMove.X, x, span) { EasingFunction = EaseOut });
        GlowMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(GlowMove.X, glowX, span) { EasingFunction = EaseOut });
    }

    /// <summary>Tints the light under the focused game with the main colours of its picture.</summary>
    private void SetGlow(ImageSource? source)
    {
        var color = DominantColor(source);
        var span = TimeSpan.FromMilliseconds(380);
        GlowCore.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Color.FromArgb(0x4C, color.R, color.G, color.B), span));
        GlowEdge.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Color.FromArgb(0x00, color.R, color.G, color.B), span));
    }

    /// <summary>The average colour of a picture, favouring vivid areas, brightened so it reads as light.</summary>
    private static Color DominantColor(ImageSource? source)
    {
        var fallback = Color.FromRgb(0x1A, 0x9F, 0xFF);
        if (source is not BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } bitmap) return fallback;
        try
        {
            var scaled = new TransformedBitmap(bitmap, new ScaleTransform(24.0 / bitmap.PixelWidth, 24.0 / bitmap.PixelHeight));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);

            double r = 0, g = 0, b = 0, total = 0;
            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                double blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
                var max = Math.Max(red, Math.Max(green, blue));
                var min = Math.Min(red, Math.Min(green, blue));
                var saturation = max <= 0 ? 0 : (max - min) / max;
                var weight = 0.1 + saturation * (max / 255.0);
                r += red * weight; g += green * weight; b += blue * weight; total += weight;
            }
            if (total <= 0) return fallback;
            r /= total; g /= total; b /= total;
            var peak = Math.Max(r, Math.Max(g, b));
            if (peak < 1) return fallback;
            var scale = Math.Min(235 / peak, 3.0);
            byte Clamp(double v) => (byte)Math.Clamp(v * scale, 0, 255);
            return Color.FromRgb(Clamp(r), Clamp(g), Clamp(b));
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    // ---- Slow drift of the wallpaper on the home screen ---------------------------------------

    private readonly ScaleTransform _heroScale = new(1.05, 1.05);
    private readonly TranslateTransform _heroShift = new();
    private bool _heroDrifting;

    private void StartHeroDrift()
    {
        if (_heroDrifting) return;
        _heroDrifting = true;
        if (HeroLayer.RenderTransform is not TransformGroup)
        {
            var group = new TransformGroup();
            group.Children.Add(_heroScale);
            group.Children.Add(_heroShift);
            HeroLayer.RenderTransform = group;
        }

        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        // 30 frames a second look the same for a drift this slow and cost half the work.
        DoubleAnimation Loop(double from, double to, double seconds)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            };
            Timeline.SetDesiredFrameRate(animation, 30);
            return animation;
        }
        _heroScale.BeginAnimation(ScaleTransform.ScaleXProperty, Loop(1.05, 1.11, 16));
        _heroScale.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(1.05, 1.11, 16));
        _heroShift.BeginAnimation(TranslateTransform.XProperty, Loop(-22, 22, 22));
        _heroShift.BeginAnimation(TranslateTransform.YProperty, Loop(-8, 8, 19));
    }

    private void StopHeroDrift()
    {
        if (!_heroDrifting) return;
        _heroDrifting = false;
        _heroScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _heroScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _heroShift.BeginAnimation(TranslateTransform.XProperty, null);
        _heroShift.BeginAnimation(TranslateTransform.YProperty, null);
        _heroScale.ScaleX = _heroScale.ScaleY = 1;
        _heroShift.X = _heroShift.Y = 0;
    }

    // ---- Page and menu transitions -----------------------------------------------------------

    private void PlayPageTransition(string page)
    {
        FrameworkElement element = page switch
        {
            "Início" => HomePage,
            "Biblioteca" => LibraryPage,
            "Jogo" => GamePage,
            "Feed" => FeedPage,
            "Mídias" => MediaPage,
            "Lojas" => StoresPage,
            "Downloads" => DownloadsPage,
            "Configurações" => SettingsPage,
            _ => OtherPage,
        };
        FadeIn(element, page == "Jogo" ? 34 : 16, page == "Jogo" ? 320 : 220);

        if (page == "Jogo")
        {
            // The banner settles in with a slight zoom-out, like opening the game.
            var zoom = new ScaleTransform(1.07, 1.07);
            HeroImage.RenderTransformOrigin = new Point(0.5, 0.3);
            HeroImage.RenderTransform = zoom;
            var animation = new DoubleAnimation(1.07, 1.0, TimeSpan.FromMilliseconds(520)) { EasingFunction = EaseOut };
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }

    private static void FadeIn(FrameworkElement element, double rise, int milliseconds)
    {
        var move = new TranslateTransform(0, rise);
        element.RenderTransform = move;
        var span = TimeSpan.FromMilliseconds(milliseconds);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, span) { EasingFunction = EaseOut });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, span) { EasingFunction = EaseOut });
    }

    private void SlideInMenu()
    {
        var slide = new TranslateTransform(-MenuBox.Width, 0);
        MenuBox.RenderTransform = slide;
        var span = TimeSpan.FromMilliseconds(220);
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-MenuBox.Width, 0, span) { EasingFunction = EaseOut });
        MenuScrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, span));
    }
}
