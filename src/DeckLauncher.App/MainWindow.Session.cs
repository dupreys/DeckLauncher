using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiteLauncher.Core;

namespace DeckLauncher.App;

// A game that was started from the launcher: the "entering the game" screen with the animated controller, following the
// game's programs, the menu that Select + B (held for two seconds) opens over the game, resuming, ending it, and muting
// the game while the launcher is in front.
public partial class MainWindow
{
    private sealed class GameSession
    {
        public GameSession(GameItem item) => Item = item;

        public GameItem Item { get; }
        public DateTime Started { get; } = DateTime.UtcNow;
        public string? Prefix { get; set; }
        public List<int> Pids { get; set; } = new();
        public bool Seen { get; set; }
        public int Misses { get; set; }
        public bool Muted { get; set; }
    }

    private GameSession? _session;
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _sessionBusy;
    private int _launchToken;
    private bool _launchHiding;
    private readonly List<(IAnimatable Target, DependencyProperty Property)> _padAnimated = new();

    private bool LaunchOverlayOpen => LaunchOverlay.Visibility == Visibility.Visible && !_launchHiding;

    private void InitSession()
    {
        _sessionTimer.Tick += async (_, _) => await SessionTickAsync();
        _gamepad.MenuComboHeld += () => Dispatcher.BeginInvoke(new Action(OpenInGameMenu));
        Closed += (_, _) =>
        {
            if (_session is { Muted: true } s) GameAudio.SetMuted(s.Pids, false); // never leave a game silent
        };
        MenuLayer.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnMenuFocusChanged));
    }

    // ---- Start and end -----------------------------------------------------------------------

    private void BeginSession(GameItem item)
    {
        var session = new GameSession(item);
        try
        {
            if (ProcessScope.Resolve(item.Game) is { } folder)
                session.Prefix = Path.GetFullPath(folder).TrimEnd('\\', '/') + "\\";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }

        _session = session;
        MenuGameName.Text = item.Name;
        item.EnsureCoverLoaded();
        MenuGameIcon.Source = item.Cover ?? item.Wide;
        item.PropertyChanged += OnSessionItemChanged;
        MenuGameItem.Visibility = Visibility.Visible;
        UpdatePlayButton();
        _sessionTimer.Start();
        ShowLaunchOverlay(item);
    }

    private void OnSessionItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is GameItem item && _session is { } s && ReferenceEquals(s.Item, item)) MenuGameIcon.Source = item.Cover ?? item.Wide;
    }

    private void EndSession()
    {
        if (_session is not { } s) return;
        if (s.Muted) GameAudio.SetMuted(s.Pids, false);
        s.Item.PropertyChanged -= OnSessionItemChanged;
        _session = null;
        _sessionTimer.Stop();
        MenuGameItem.Visibility = Visibility.Collapsed;
        GameMenuPanel.Visibility = Visibility.Collapsed;
        ShowGameConfirm(false);
        if (LaunchOverlayOpen) HideLaunchOverlay();
        UpdatePlayButton();
        _lastRefresh = DateTime.MinValue; // play time and last session changed
        if (MenuOpen) MenuList.Children.OfType<Button>().First(b => b.Visibility == Visibility.Visible).Focus();
    }

    private void UpdatePlayButton()
    {
        var same = _session is { } s && _gameItem is { } g && GameSorter.Key(s.Item.Game) == GameSorter.Key(g.Game);
        PlayButton.Content = same ? "Retomar" : "Jogar";
    }

    private static bool BelongsTo(GameSession session, string path)
    {
        var exe = session.Item.Game.ExecutablePath;
        if (!string.IsNullOrEmpty(exe) && string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)) return true;
        return session.Prefix is not null && path.StartsWith(session.Prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Once a second: which programs of the game are running, when it ended, and the sound rule.</summary>
    private async Task SessionTickAsync()
    {
        if (_sessionBusy) return;
        _sessionBusy = true;
        try
        {
            if (_session is not { } session)
            {
                _sessionTimer.Stop();
                return;
            }

            var processes = await Task.Run(Native.RunningProcesses);
            if (!ReferenceEquals(session, _session)) return;

            var self = Environment.ProcessId;
            var pids = processes.Where(p => p.Pid != self && BelongsTo(session, p.Path)).Select(p => p.Pid).ToList();
            session.Pids = pids;
            var elapsed = DateTime.UtcNow - session.Started;

            if (pids.Count > 0)
            {
                session.Seen = true;
                session.Misses = 0;
            }
            else if (session.Seen)
            {
                if (++session.Misses >= 3)
                {
                    EndSession(); // the game closed by itself
                    return;
                }
            }
            else if (elapsed > TimeSpan.FromSeconds(90))
            {
                var name = session.Item.Name;
                EndSession();
                ShowToast($"Não consegui acompanhar {name}: o menu durante o jogo não vai estar disponível.");
                return;
            }

            if (LaunchOverlayOpen && ((session.Seen && elapsed > TimeSpan.FromSeconds(5)) || elapsed > TimeSpan.FromSeconds(25)))
                HideLaunchOverlay();

            if (!session.Seen) return;

            // The game is silent while the launcher is in front, and loud again as soon as the game is.
            var front = Native.ForegroundProcessId();
            if (front == self && !LaunchOverlayOpen)
            {
                session.Muted = true;
                GameAudio.SetMuted(pids, true); // every second, so a sound that starts later is muted too
            }
            else if (session.Muted && pids.Contains(front))
            {
                session.Muted = false;
                GameAudio.SetMuted(pids, false);
            }
        }
        finally
        {
            _sessionBusy = false;
        }
    }

    // ---- "Entering the game" screen ---------------------------------------------------------

    private async void ShowLaunchOverlay(GameItem item)
    {
        var token = ++_launchToken;
        _launchHiding = false;
        LaunchTitle.Text = item.Name;
        LaunchTitle.Visibility = Visibility.Visible;
        LaunchLogo.Source = null;
        LaunchBackdrop.Source = item.Wide ?? item.Cover;
        LaunchStatus.Text = "Iniciando…";

        LaunchOverlay.BeginAnimation(OpacityProperty, null);
        LaunchOverlay.Visibility = Visibility.Visible;
        LaunchOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var content = new ScaleTransform(1.08, 1.08);
        LaunchContent.RenderTransform = content;
        var settle = new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease };
        content.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
        content.BeginAnimation(ScaleTransform.ScaleYProperty, settle);

        var drift = new ScaleTransform(1, 1);
        LaunchBackdrop.RenderTransform = drift;
        var slow = new DoubleAnimation(1, 1.08, TimeSpan.FromSeconds(25));
        drift.BeginAnimation(ScaleTransform.ScaleXProperty, slow);
        drift.BeginAnimation(ScaleTransform.ScaleYProperty, slow);

        StartPadAnimation();
        LaunchCancelButton.Focus();

        // The pictures come a moment later (decoded off the UI thread).
        if (item.Game.LogoPath is { } logoPath)
        {
            var logo = await ImageLoader.LoadAsync(logoPath, 900);
            if (logo is not null && token == _launchToken)
            {
                LaunchLogo.Source = logo;
                LaunchTitle.Visibility = Visibility.Collapsed;
            }
        }
        var backdrop = await item.LoadBackdropAsync();
        if (backdrop is not null && token == _launchToken) LaunchBackdrop.Source = backdrop;
    }

    private void HideLaunchOverlay()
    {
        if (!LaunchOverlayOpen) return;
        _launchHiding = true;
        var token = ++_launchToken;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
        fade.Completed += (_, _) =>
        {
            if (token != _launchToken) return;
            LaunchOverlay.Visibility = Visibility.Collapsed;
            LaunchOverlay.BeginAnimation(OpacityProperty, null);
            _launchHiding = false;
            StopPadAnimation();
            if (!MenuOpen && !PowerOpen && !ProfileOpen && !OptionsOpen) FocusPage();
        };
        LaunchOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private void OnLaunchCancelClick(object sender, RoutedEventArgs e) => _ = CancelLaunchAsync();

    private async Task CancelLaunchAsync()
    {
        var session = _session;
        HideLaunchOverlay();
        if (session is null) return;
        if (session.Pids.Count > 0)
        {
            if (!await CloseGameAsync(session)) return;
        }
        else
        {
            EndSession();
        }
        ShowToast("Lançamento cancelado");
    }

    // ---- The animated controller ---------------------------------------------------------------

    private void StartPadAnimation()
    {
        StopPadAnimation();
        const double loop = 4.2;

        // Buttons light up one after another, then the D-pad, the bumpers and finally Select + B: the combo for the menu.
        GlowAndPress(PadA, PadAGlow, loop, (0.2, 0.3));
        GlowAndPress(PadB, PadBGlow, loop, (0.7, 0.3), (3.65, 0.35));
        GlowAndPress(PadX, PadXGlow, loop, (1.2, 0.3));
        GlowAndPress(PadY, PadYGlow, loop, (1.7, 0.3));
        Animate(PadDpadGlow, UIElement.OpacityProperty, PulseAnimation(0, 1, loop, (2.2, 0.4)));
        Animate(PadLBGlow, UIElement.OpacityProperty, PulseAnimation(0, 1, loop, (2.8, 0.25)));
        Animate(PadRBGlow, UIElement.OpacityProperty, PulseAnimation(0, 1, loop, (3.2, 0.25)));
        Animate(PadSelectGlow, UIElement.OpacityProperty, PulseAnimation(0, 1, loop, (3.65, 0.35)));

        Wobble(PadStickL, 1.4, 1.0, 5);
        Wobble(PadStickR, 1.1, 1.7, 4);

        var spin = new RotateTransform();
        LaunchSpinner.RenderTransform = spin;
        Animate(spin, RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void StopPadAnimation()
    {
        foreach (var (target, property) in _padAnimated) target.BeginAnimation(property, null);
        _padAnimated.Clear();
    }

    private void Animate(IAnimatable target, DependencyProperty property, AnimationTimeline animation)
    {
        target.BeginAnimation(property, animation);
        _padAnimated.Add((target, property));
    }

    private void GlowAndPress(FrameworkElement button, UIElement glow, double loop, params (double Start, double Length)[] pulses)
    {
        Animate(glow, UIElement.OpacityProperty, PulseAnimation(0, 1, loop, pulses));
        var scale = new ScaleTransform(1, 1);
        button.RenderTransform = scale;
        Animate(scale, ScaleTransform.ScaleXProperty, PulseAnimation(1, 0.82, loop, pulses));
        Animate(scale, ScaleTransform.ScaleYProperty, PulseAnimation(1, 0.82, loop, pulses));
    }

    private void Wobble(UIElement knob, double secondsX, double secondsY, double range)
    {
        var move = new TranslateTransform();
        knob.RenderTransform = move;
        Animate(move, TranslateTransform.XProperty, new DoubleAnimation(-range, range, TimeSpan.FromSeconds(secondsX))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        Animate(move, TranslateTransform.YProperty, new DoubleAnimation(-range * 0.7, range * 0.7, TimeSpan.FromSeconds(secondsY))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
    }

    /// <summary>Rests at <paramref name="low"/>, goes to <paramref name="high"/> for each (start, length) and loops. Pulses must be at least 0.2 s apart.</summary>
    private static DoubleAnimationUsingKeyFrames PulseAnimation(double low, double high, double loop, params (double Start, double Length)[] pulses)
    {
        static KeyTime At(double seconds) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));

        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(loop), RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(low, At(0)));
        foreach (var (start, length) in pulses)
        {
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(low, At(start)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(high, At(start + 0.08)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(high, At(start + length)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(low, At(start + length + 0.12)));
        }
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(low, At(loop)));
        return animation;
    }

    // ---- Menu over a running game ---------------------------------------------------------------

    /// <summary>Select + B held for two seconds, from anywhere: the launcher comes to the front with its menu open on the game.</summary>
    private void OpenInGameMenu()
    {
        if (_session is not { } session) return;
        if (LaunchOverlayOpen) HideLaunchOverlay();
        if (QuickOpen) CloseQuick();

        BringLauncherToFront();
        session.Muted = true; // the launcher is in front: the game goes quiet
        GameAudio.SetMuted(session.Pids, true);

        if (!MenuOpen) OpenMenu();
        MenuGameItem.Focus();
    }

    private void BringLauncherToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Maximized;
        Show();
        Topmost = true; // above the game even if Windows is slow to hand over the focus
        Topmost = false;
        Activate();
        Native.BringToFront(new WindowInteropHelper(this).Handle);
    }

    private void OnMenuFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is not DependencyObject focus) return;
        var onGame = ReferenceEquals(focus, MenuGameItem);
        var inPanel = IsAncestor(GameMenuPanel, focus);
        if (onGame) ShowGameConfirm(false);
        GameMenuPanel.Visibility = _session is not null && (onGame || inPanel) ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool FocusInGamePanel() => Keyboard.FocusedElement is DependencyObject f && IsAncestor(GameMenuPanel, f);

    private void LeaveGamePanel()
    {
        ShowGameConfirm(false);
        MenuGameItem.Focus();
    }

    /// <summary>Right from the game entry reaches its options; Left from them goes back. True when the key was used here.</summary>
    private bool TryGameMenuMove(FocusNavigationDirection direction)
    {
        if (!MenuOpen || Keyboard.FocusedElement is not DependencyObject focus) return false;
        if (ReferenceEquals(focus, MenuGameItem) && direction == FocusNavigationDirection.Right && _session is not null)
        {
            ResumeGameButton.Focus();
            return true;
        }
        if (IsAncestor(GameMenuPanel, focus) && direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
        {
            if (direction == FocusNavigationDirection.Left) LeaveGamePanel();
            return true;
        }
        return false;
    }

    private void OnMenuGameClick(object sender, RoutedEventArgs e)
    {
        if (_session is not null) ResumeGameButton.Focus();
    }

    private void OnResumeGameClick(object sender, RoutedEventArgs e) => ResumeGame();

    private void ResumeGame()
    {
        if (_session is not { } session) return;
        var window = Native.FindMainWindow(session.Pids);
        if (window == IntPtr.Zero)
        {
            ShowToast("Não encontrei a janela do jogo. Ele ainda está abrindo?");
            return;
        }

        if (MenuOpen) CloseMenu(silent: true);
        GameMenuPanel.Visibility = Visibility.Collapsed;
        session.Muted = false;
        GameAudio.SetMuted(session.Pids, false);
        Native.BringToFront(window);
    }

    private void ShowGameConfirm(bool confirm)
    {
        GameMenuMain.Visibility = confirm ? Visibility.Collapsed : Visibility.Visible;
        GameMenuConfirm.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnEndGameClick(object sender, RoutedEventArgs e)
    {
        ShowGameConfirm(true);
        EndCancelButton.Focus(); // Cancel is pre-selected: ending a game loses unsaved progress
    }

    private void OnEndGameCancelClick(object sender, RoutedEventArgs e)
    {
        ShowGameConfirm(false);
        EndGameButton.Focus();
    }

    private async void OnEndGameConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_session is not { } session) return;
        var name = session.Item.Name;
        if (await CloseGameAsync(session)) ShowToast($"{name} encerrado");
    }

    /// <summary>Asks the game's programs to close and, after a few seconds, forces the ones still open. True when all are gone.</summary>
    private async Task<bool> CloseGameAsync(GameSession session)
    {
        var pids = session.Pids.ToList();
        var remaining = await Task.Run(() => CloseProcesses(pids));
        if (remaining > 0)
        {
            ShowToast($"Não foi possível encerrar {session.Item.Name}.");
            return false;
        }
        if (ReferenceEquals(session, _session)) EndSession();
        return true;
    }

    private static int CloseProcesses(List<int> pids)
    {
        foreach (var pid in pids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow();
            }
            catch (Exception ex) when (IsProcessError(ex)) { }
        }

        for (var i = 0; i < 8; i++)
        {
            Thread.Sleep(500);
            if (pids.All(IsGone)) return 0;
        }

        foreach (var pid in pids.Where(p => !IsGone(p)))
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (IsProcessError(ex)) { }
        }
        Thread.Sleep(500);
        return pids.Count(p => !IsGone(p));
    }

    private static bool IsGone(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (Exception ex) when (IsProcessError(ex))
        {
            return true;
        }
    }

    private static bool IsProcessError(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception;
}
