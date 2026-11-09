using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DeckLauncher.App;

// The launcher's own on-screen keyboard: Y opens it for the search, B closes it, A types, X erases, Y adds a space.
// It does not depend on the Windows touch keyboard, which does not always show up in the Xbox full-screen mode.
public partial class MainWindow
{
    private static readonly string[][] OskLayout =
    {
        new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "⌫" },
        new[] { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" },
        new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l", "ç" },
        new[] { "⇧", "z", "x", "c", "v", "b", "n", "m", "-", "." },
        new[] { "caps", "espaço", "limpar", "pronto" },
    };

    private TextBox? _oskTarget;
    private bool _oskShift;   // ⇧: the next letter only
    private bool _oskCaps;    // Aa / RT: every letter, until switched off
    private Button? _oskCapsButton;
    private bool _oskBuilt;
    private readonly List<Button> _oskLetters = new();

    private bool OskOpen => OskPanel.Visibility == Visibility.Visible;

    private void BuildOsk()
    {
        _oskBuilt = true;
        foreach (var keys in OskLayout)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var key in keys)
            {
                var button = new Button { Style = (Style)FindResource("OskKey"), Content = key, Tag = key };
                if (key is "caps") { button.Width = 120; _oskCapsButton = button; }
                else if (key is "espaço") button.Width = 340;
                else if (key is "limpar" or "pronto") button.Width = 160;
                else if (key is "⌫" or "⇧") button.Width = 76;
                if (key.Length == 1 && char.IsLetter(key[0])) _oskLetters.Add(button);
                button.Click += OnOskKeyClick;
                row.Children.Add(button);
            }
            OskRows.Children.Add(row);
        }
    }

    private void ShowOsk(TextBox target)
    {
        if (!_oskBuilt) BuildOsk();
        _oskTarget = target;
        target.SelectionLength = 0;
        target.CaretIndex = target.Text.Length; // typing continues the text
        _oskShift = false;
        _oskCaps = false;
        RefreshOskKeys();
        OskText.Text = target.Text;
        OskPanel.Visibility = Visibility.Visible;
        UiSound.Play("menu_open");
        Dispatcher.BeginInvoke(() =>
        {
            if (OskRows.Children.Count > 1 && ((StackPanel)OskRows.Children[1]).Children[0] is Button first) first.Focus();
            UpdateHints(force: true);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Closes the keyboard and gives the focus back to the text box it was typing into.</summary>
    private void CloseOsk(bool confirm = false)
    {
        if (!OskOpen) return;
        OskPanel.Visibility = Visibility.Collapsed;
        var target = _oskTarget;
        _oskTarget = null;
        if (target is null) return;
        if (confirm && ReferenceEquals(target, SearchBox)) ConfirmSearch();
        else target.Focus();
        UpdateHints(force: true);
    }

    private void RefreshOskKeys()
    {
        foreach (var button in _oskLetters)
        {
            var letter = (string)button.Tag;
            button.Content = _oskShift || _oskCaps ? letter.ToUpperInvariant() : letter;
        }
        if (_oskCapsButton is not null) _oskCapsButton.Content = _oskCaps ? "ABC" : "abc";
    }

    private void OnOskKeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        switch (key)
        {
            case "⌫": OskBackspace(); break;
            case "⇧": _oskShift = !_oskShift; RefreshOskKeys(); break;
            case "caps": ToggleOskCaps(); break;
            case "espaço": OskType(" "); break;
            case "limpar": if (_oskTarget is not null) { _oskTarget.Text = ""; OskText.Text = ""; } break;
            case "pronto": CloseOsk(confirm: true); break;
            default:
                OskType(_oskShift || _oskCaps ? key.ToUpperInvariant() : key);
                if (_oskShift) { _oskShift = false; RefreshOskKeys(); }
                break;
        }
    }

    private void ToggleOskCaps()
    {
        _oskCaps = !_oskCaps;
        RefreshOskKeys();
        UiSound.Play("select");
    }

    private void OskType(string text)
    {
        if (_oskTarget is not { } box) return;
        box.SelectedText = text; // replaces the selection (the search starts fully selected) or inserts at the caret
        box.CaretIndex = box.SelectionStart + box.SelectionLength;
        box.SelectionLength = 0;
        OskText.Text = box.Text;
        UiSound.Play("move");
    }

    private void OskBackspace()
    {
        if (_oskTarget is not { } box) return;
        if (box.SelectionLength > 0) box.SelectedText = "";
        else if (box.CaretIndex > 0)
        {
            var at = box.CaretIndex;
            box.Text = box.Text.Remove(at - 1, 1);
            box.CaretIndex = at - 1;
        }
        OskText.Text = box.Text;
    }

    /// <summary>Controller buttons while the keyboard is open. Returns true when the button was used.</summary>
    private bool HandleOskButton(GamepadButton button)
    {
        switch (button)
        {
            case GamepadButton.B: UiSound.Play("back"); CloseOsk(); return true;
            case GamepadButton.X: OskBackspace(); return true;
            case GamepadButton.Y: OskType(" "); return true;
            case GamepadButton.RightTrigger: ToggleOskCaps(); return true;
            case GamepadButton.Select:
            case GamepadButton.LeftShoulder:
            case GamepadButton.RightShoulder: return true; // nothing else steers the launcher behind the keyboard
            default: return false; // directions and A work like in any other list
        }
    }
}
