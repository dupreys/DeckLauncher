using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeckLauncher.App;

// Controller and keyboard input, the button hints in the bottom bar and the top-bar icons.
public partial class MainWindow
{
    private readonly Gamepad _gamepad = new();
    private bool _padHints;

    private void InitInput()
    {
        _gamepad.ButtonPressed += OnGamepad;
        _gamepad.Start();
        Closed += (_, _) => _gamepad.Dispose();

        // Any key goes back to the keyboard hints; the controller switches them to A / B.
        PreviewKeyDown += (_, _) => SetHints(false);

        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Down) { ConfirmSearch(); e.Handled = true; }
        };

        InitQuick();
        InitSounds();
        InitPlayMonitor();
        InitProfile();
        InitSession();
    }

    // ---- Back, menu, search -----------------------------------------------------------------

    private void GoBack()
    {
        if (!MenuOpen && (PowerOpen || QuickOpen || OptionsOpen || ProfileOpen || LaunchOverlayOpen || ManageOpen || _page != "Início")) UiSound.Play("back");
        if (LaunchOverlayOpen) _ = CancelLaunchAsync();
        else if (PowerOpen) PowerBack();
        else if (ProfileOpen) CloseProfile();
        else if (OptionsOpen) CloseOptions();
        else if (QuickOpen) CloseQuick();
        else if (MenuOpen && FocusInGamePanel()) LeaveGamePanel();
        else if (MenuOpen) CloseMenu();
        else if (ManageOpen) CloseManage();
        else if (_page == "Jogo") CloseGame();
        else if (_page != "Início") { ShowPage("Início"); FocusHome(); }
        if (!IsKeyboardFocusWithin || Keyboard.FocusedElement is not TextBox) VirtualKeyboard.Hide();
    }

    private void ToggleMenu()
    {
        if (PowerOpen || ManageOpen || OptionsOpen || ProfileOpen || LaunchOverlayOpen) return;
        if (QuickOpen) CloseQuick();
        if (MenuOpen) CloseMenu(); else OpenMenu();
    }

    private void OpenSearch(bool keyboard)
    {
        if (PowerOpen || ManageOpen || OptionsOpen || ProfileOpen || LaunchOverlayOpen) return;
        if (QuickOpen) CloseQuick();
        if (MenuOpen) MenuLayer.Visibility = Visibility.Collapsed;
        ShowPage("Biblioteca");
        SearchBox.Focus();
        SearchBox.SelectAll();
        if (keyboard) _ = VirtualKeyboard.ShowAsync();
    }

    private void ConfirmSearch()
    {
        VirtualKeyboard.Hide();
        FocusLibrary();
    }

    private void OnTopSearchClick(object sender, RoutedEventArgs e) => OpenSearch(keyboard: _padHints);

    private void OnTopDownloadsClick(object sender, RoutedEventArgs e)
    {
        if (PowerOpen || ManageOpen) return;
        ShowPage("Downloads");
        FocusPage();
    }

    // ---- Button hints -----------------------------------------------------------------------

    private void SetHints(bool pad)
    {
        if (_padHints == pad) return;
        _padHints = pad;
        SetChip(MenuChip, MenuChipText, pad, "F1", "≡", "#3C4350");
        SetChip(SelectChip, SelectChipText, pad, "Enter", "A", "#4CA82E");
        SetChip(BackChip, BackChipText, pad, "Esc", "B", "#C9302C");
    }

    private static void SetChip(Border chip, TextBlock text, bool pad, string key, string padText, string color)
    {
        if (pad)
        {
            chip.CornerRadius = new CornerRadius(15);
            chip.Padding = new Thickness(0);
            chip.Width = 30;
            chip.Height = 30;
            chip.Background = (Brush)new BrushConverter().ConvertFromString(color)!;
            text.Text = padText;
            text.Foreground = Brushes.White;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            chip.CornerRadius = new CornerRadius(3);
            chip.Padding = new Thickness(8, 3, 8, 3);
            chip.Width = double.NaN;
            chip.Height = double.NaN;
            chip.Background = (Brush)new BrushConverter().ConvertFromString("#F2F4F6")!;
            text.Text = key;
            text.Foreground = Brushes.Black;
        }
    }

    // ---- Controller -------------------------------------------------------------------------

    private void OnGamepad(GamepadButton button)
    {
        // The controller keeps reporting while a game is in front: never steer the launcher from behind it.
        if (!IsActive) return;
        SetHints(true);

        switch (button)
        {
            case GamepadButton.Up: Move(FocusNavigationDirection.Up); break;
            case GamepadButton.Down: Move(FocusNavigationDirection.Down); break;
            case GamepadButton.Left: Move(FocusNavigationDirection.Left); break;
            case GamepadButton.Right: Move(FocusNavigationDirection.Right); break;
            case GamepadButton.A: ActivateFocused(); break;
            case GamepadButton.B: GoBack(); break;
            case GamepadButton.Select: ToggleMenu(); break; // the side menu (power is its last item)
            case GamepadButton.LeftShoulder: CycleTabs(-1); break;
            case GamepadButton.RightShoulder: CycleTabs(+1); break;
            case GamepadButton.Y: OpenSearch(keyboard: true); break;
            case GamepadButton.X: ToggleFavoriteOfFocused(); break;
        }
    }

    private void Move(FocusNavigationDirection direction)
    {
        // Inside a text box left/right move the caret; up/down leave it (and close the on-screen keyboard).
        if (Keyboard.FocusedElement is TextBox && direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right) return;

        if (Keyboard.FocusedElement is not UIElement { IsVisible: true } focused || !IsAncestor(this, focused))
        {
            FocusPage(); // focus was left on something that no longer exists (a list was swapped)
            return;
        }

        if (focused is TextBox) VirtualKeyboard.Hide();
        if (TryGameMenuMove(direction) || TrySettingsMove(direction)) return;
        if (direction == FocusNavigationDirection.Down && TryOpenFeedFromRow()) return;
        if (TryListMove(direction)) return;
        _navUntil = DateTime.UtcNow.AddMilliseconds(200);
        focused.MoveFocus(new TraversalRequest(direction));
    }

    /// <summary>A on the controller: click, toggle or select whatever has focus; on a text box it opens the Windows keyboard.</summary>
    private void ActivateFocused()
    {
        if (Keyboard.FocusedElement is TextBox box)
        {
            if (ReferenceEquals(box, SearchBox)) ConfirmSearch();
            else if (VirtualKeyboard.IsVisible) VirtualKeyboard.Hide();
            else _ = VirtualKeyboard.ShowAsync();
            return;
        }

        if (Keyboard.FocusedElement is not UIElement element) return;
        if (IsSettingsNav(element))
        {
            // The section is already showing: A goes into its options.
            if (FirstSettingsControl() is { } first) first.Focus();
            return;
        }
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
        else if (peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection) selection.Select();
        else if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
    }

    /// <summary>LB / RB: previous / next tab of the screen in front.</summary>
    private void CycleTabs(int delta)
    {
        if (PowerOpen || MenuOpen || QuickOpen || OptionsOpen || ProfileOpen || LaunchOverlayOpen) return;

        if (ManageOpen) { CycleRadios(ManageTabs.Children.OfType<RadioButton>().ToList(), delta); return; }
        switch (_page)
        {
            case "Biblioteca": CycleRadios(_tabs.Values.ToList(), delta); break;
            case "Jogo": CycleRadios(new List<RadioButton> { GameTabAbout, GameTabInfo }, delta); break;
            case "Feed": CycleRadios(new List<RadioButton> { FeedTabNews, FeedTabFriends, FeedTabRecs }, delta); break;
            case "Configurações": CycleRadios(FindAll<RadioButton>(SettingsPage).ToList(), delta); break;
        }
    }

    private static void CycleRadios(List<RadioButton> tabs, int delta)
    {
        if (tabs.Count == 0) return;
        var current = tabs.FindIndex(t => t.IsChecked == true);
        var next = tabs[((current < 0 ? 0 : current) + delta + tabs.Count) % tabs.Count];
        next.IsChecked = true;
        next.Focus();
    }

    /// <summary>X on a game tile or on the game page: add to or remove from the favourites.</summary>
    private void ToggleFavoriteOfFocused()
    {
        GameItem? item = Keyboard.FocusedElement is FrameworkElement { DataContext: GameItem focusedItem } ? focusedItem : null;
        if (item is null && _page == "Jogo") item = _gameItem;
        if (item is null) return;

        var key = LiteLauncher.Core.GameSorter.Key(item.Game);
        if (!App.Settings.Favorites.Remove(key)) App.Settings.Favorites.Add(key);
        SaveSettings();
        if (_page == "Jogo" && ReferenceEquals(item, _gameItem)) UpdateFavoriteIcon(item);
        ApplyFilter();
        ShowToast(item.IsFavorite ? $"{item.Name} adicionado aos favoritos" : $"{item.Name} removido dos favoritos");
    }
}
