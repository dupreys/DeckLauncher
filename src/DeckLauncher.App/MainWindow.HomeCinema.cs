using System.Globalization;
using DeckLauncher.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DeckLauncher.App;

// The home styles "Cinema" (the focused game in big letters, art behind, recent games in a strip) and
// "Carrossel" (one big poster in the middle that the others slide around).
public partial class MainWindow
{
    private GameItem? _cinemaItem;

    // ---- Cinema --------------------------------------------------------------------------------

    private void OnCinemaTileFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        OnHomeTileFocus(sender, e); // the art behind
        if (sender is not Button { DataContext: GameItem item }) return;
        _cinemaItem = item;
        item.EnsureWideLoaded();
        CinemaCaption.Text = item.IsRunning ? "JOGANDO AGORA" : "JOGOS RECENTES";
        CinemaTitle.Text = item.Name;
        CinemaMeta.Text = item.RecentText;
        // The logo replaces the title when the game has one (it may arrive a moment later).
        void ShowLogo()
        {
            var hasLogo = item.Logo is not null && ReferenceEquals(_cinemaItem, item);
            if (!ReferenceEquals(_cinemaItem, item)) return;
            CinemaLogo.Source = item.Logo;
            CinemaLogo.Visibility = hasLogo ? Visibility.Visible : Visibility.Collapsed;
            CinemaTitle.Visibility = hasLogo ? Visibility.Collapsed : Visibility.Visible;
        }
        ShowLogo();
        System.ComponentModel.PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName != nameof(GameItem.Logo)) return;
            ShowLogo();
            if (!ReferenceEquals(_cinemaItem, item)) item.PropertyChanged -= handler;
        };
        item.PropertyChanged += handler;
    }

    private double _cinemaTarget;
    private long _cinemaUntil; // when the running scroll animation ends: it heals itself, no flag that can get stuck

    /// <summary>The strip scrolls by itself (smoothly) so the focused poster, its frame and some air around it stay inside the screen.</summary>
    private void OnCinemaBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true; // must be handled inside the viewer (on the row): the viewer's own content presenter would otherwise scroll flush to the edge
        if (e.TargetObject is not FrameworkElement tile || tile.ActualWidth <= 0 || CinemaScroll.ViewportWidth <= 0) return;

        const double air = 56;
        var left = tile.TranslatePoint(new Point(0, 0), CinemaScroll).X - CinemaShift.X;
        var width = tile.ActualWidth * CardScale;
        var current = Environment.TickCount64 < _cinemaUntil ? _cinemaTarget : -CinemaShift.X;
        var viewport = CinemaScroll.ViewportWidth;
        var target = current;
        if (left - air < current) target = left - air;
        else if (left + width + air > current + viewport) target = left + width + air - viewport;
        else return;
        target = Math.Clamp(target, 0, Math.Max(0, CinemaScroll.ExtentWidth - viewport));
        if (Math.Abs(target - current) < 0.5) return;

        _cinemaTarget = target;
        _cinemaUntil = Environment.TickCount64 + 240;
        // The strip slides with a render transform (no layout pass per frame, runs on the render thread): much smoother than scrolling the viewer.
        var animation = new DoubleAnimation(CinemaShift.X, -target, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut };
        CinemaShift.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void OnCinemaPlayClick(object sender, RoutedEventArgs e)
    {
        if (_cinemaItem is { } item) OpenGame(item);
    }

    // ---- Carrossel -----------------------------------------------------------------------------

    private void OnCarouselTileFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        OnHomeTileFocus(sender, e);
        if (sender is not Button { DataContext: GameItem item }) return;
        item.EnsureCoverLoaded();
        CarouselName.Text = item.Name;
        CarouselSub.Text = item.IsRunning ? "JOGANDO AGORA  ·  " + item.RecentText : item.RecentText;

        var index = Math.Max(0, CarouselRow.Items.IndexOf(item));
        BuildCarouselDots(CarouselRow.Items.Count, index);
        CenterCarouselOn(index);
    }

    // Poster sizes of the CarouselTile style: the others are 250 wide with 30 on each side, the focused one grows to 325.
    private const double CarouselCell = 250 + 60, CarouselBig = 325;

    /// <summary>Slides the row so the focused poster sits in the middle of the screen (worked out from the sizes, so it does not wait for the growing animation).</summary>
    private void CenterCarouselOn(int index)
    {
        if (CarouselView.ActualWidth <= 0)
        {
            // Not measured yet (the first poster right after the list was filled): try again once the layout is done.
            Dispatcher.BeginInvoke(() => CenterCarouselOn(index), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }
        var center = (index * CarouselCell + 30 + CarouselBig / 2) * CardScale;
        var target = CarouselView.ActualWidth / 2 - center;
        if (Math.Abs(CarouselShift.X - target) < 0.5) return;
        CarouselShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOut });
    }

    private void BuildCarouselDots(int count, int selected)
    {
        if (CarouselDots.Children.Count != count)
        {
            CarouselDots.Children.Clear();
            for (var i = 0; i < count; i++)
                CarouselDots.Children.Add(new Border { Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(5, 0, 5, 0) });
        }
        for (var i = 0; i < CarouselDots.Children.Count; i++)
        {
            var dot = (Border)CarouselDots.Children[i];
            var on = i == selected;
            dot.Width = on ? 28 : 10;
            dot.Background = on ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromRgb(0x46, 0x54, 0x6B));
        }
    }

    private void UpdateCarouselClock()
    {
        if (CarouselHome.Visibility != Visibility.Visible) return;
        var now = DateTime.Now;
        CarouselClock.Text = now.ToString("HH:mm");
        var date = now.ToString(Loc.Language == "en" ? "dddd, MMMM d" : "dddd, d 'de' MMMM", UiText.Culture);
        CarouselDate.Text = char.ToUpper(date[0], CultureInfo.InvariantCulture) + date[1..];
    }
}
