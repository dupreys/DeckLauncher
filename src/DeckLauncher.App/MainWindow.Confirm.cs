using System.Windows;
using System.Windows.Threading;

namespace DeckLauncher.App;

// A yes/no question shown inside the launcher, so it can be answered with the controller (the Windows message box needs the mouse).
public partial class MainWindow
{
    private TaskCompletionSource<bool>? _confirm;
    private IInputElement? _confirmReturnFocus;

    private bool ConfirmOpen => ConfirmLayer.Visibility == Visibility.Visible;

    /// <summary>Asks the question; "No" is pre-selected, so a stray A press never confirms. Back (B/Esc) answers no.</summary>
    private Task<bool> AskConfirmAsync(string title, string message, string yes, string no, string? hint = null)
    {
        _confirm?.TrySetResult(false);
        var source = _confirm = new TaskCompletionSource<bool>();
        _confirmReturnFocus = System.Windows.Input.Keyboard.FocusedElement;
        if (QuickOpen) CloseQuick();
        ConfirmTitle.Text = UiText.T(title);
        ConfirmMessage.Text = UiText.T(message);
        ConfirmHint.Text = hint is null ? "" : UiText.T(hint);
        ConfirmHint.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
        ConfirmYesButton.Content = UiText.T(yes);
        ConfirmNoButton.Content = UiText.T(no);
        ConfirmLayer.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => ConfirmNoButton.Focus(), DispatcherPriority.Input);
        return source.Task;
    }

    private void CloseConfirm(bool answer)
    {
        ConfirmLayer.Visibility = Visibility.Collapsed;
        var source = _confirm;
        _confirm = null;
        (_confirmReturnFocus as UIElement)?.Focus();
        source?.TrySetResult(answer);
    }

    private void OnConfirmYesClick(object sender, RoutedEventArgs e) => CloseConfirm(true);

    private void OnConfirmNoClick(object sender, RoutedEventArgs e) => CloseConfirm(false);
}
