using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

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

        if (e.Key is Key.Left or Key.Right && TryGameMenuMove(e.Key == Key.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right))
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

    private void OnRecentBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is not FrameworkElement target) return;
        e.Handled = true; // we scroll ourselves, with an animation

        var left = target.TranslatePoint(new Point(0, 0), RecentHost).X;
        var finalWidth = target.DataContext is GameItem ? 640 : target.ActualWidth;
        var viewport = RecentScroll.ViewportWidth;
        if (viewport <= 0) return;

        var offset = _rowTarget;
        if (left < offset + 20) offset = left - 20 - 221; // keep one game visible before the focused one
        else if (left + finalWidth + 40 > offset + viewport) offset = left + finalWidth + 40 - viewport;

        var contentWidth = RecentHost.ActualWidth + (target.DataContext is GameItem && target.ActualWidth < finalWidth ? finalWidth - target.ActualWidth : 0);
        offset = Math.Clamp(offset, 0, Math.Max(0, contentWidth - viewport));
        if (Math.Abs(offset - _rowTarget) < 0.5 && Math.Abs(RecentScroll.HorizontalOffset - offset) < 0.5) return;

        _rowTarget = offset;
        BeginAnimation(RowOffsetProperty, new DoubleAnimation(RecentScroll.HorizontalOffset, offset, TimeSpan.FromMilliseconds(280)) { EasingFunction = EaseOut });
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
