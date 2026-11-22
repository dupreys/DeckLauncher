using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeckLauncher.App;

/// <summary>
/// Shared shell of the Epic and GOG sign-in windows: the store's own page in a WebView2 plus a controller-operable keyboard
/// below it. Controller: D-pad up/down or LB/RB walk the page's fields, A presses the focused item, Y opens the keyboard, B closes
/// the window. With the keyboard open: D-pad moves over the keys, A types, X erases, Y adds a space, B closes the keyboard.
/// The text goes straight into the page; this window never keeps or logs what was typed (passwords included).
/// </summary>
internal abstract class LoginWindowBase : Window
{
    private static readonly string[][] Layout =
    {
        new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "⌫" },
        new[] { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" },
        new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l", "@" },
        new[] { "⇧", "z", "x", "c", "v", "b", "n", "m", "-", "_", "." },
        new[] { "!", "#", "$", "%", "&", "*", "+", "=", "?", "/" },
        new[] { "abc", "espaço", "entrar", "fechar" },
    };

    protected readonly WebView2 Web = new();
    private readonly Grid _keyboard = new() { Visibility = Visibility.Collapsed, Background = System.Windows.Media.Brushes.Black };
    private readonly List<Button> _letters = new();
    private Button? _capsButton;
    private bool _shift, _caps;
    private readonly Gamepad _pad = new();

    protected LoginWindowBase(string title, string profileFolder)
    {
        ProfileFolder = profileFolder;
        Title = UiText.T(title);
        Width = 620;
        Height = Math.Min(980, SystemParameters.WorkArea.Height * 0.92); // a small screen (4K TV at high scaling) must not cut the window off
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.Black;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(Web, 0);
        Grid.SetRow(_keyboard, 1);
        root.Children.Add(Web);
        BuildKeyboard();
        root.Children.Add(_keyboard);
        Content = root;

        Loaded += async (_, _) => { _pad.ButtonPressed += OnPad; _pad.Start(); await StartAsync(); };
        Closed += (_, _) => { _pad.ButtonPressed -= OnPad; _pad.Dispose(); };
    }

    private string ProfileFolder { get; }

    /// <summary>True when the login page could not be opened here (the caller then falls back to the browser).</summary>
    public bool Failed { get; protected set; }

    /// <summary>Called once WebView2 is ready: hook the navigation events and open the login page.</summary>
    protected abstract void OnWebReady(CoreWebView2 web);

    private async Task StartAsync()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckLauncher", ProfileFolder);
            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            await Web.EnsureCoreWebView2Async(env);
            Web.CoreWebView2.CookieManager.DeleteAllCookies(); // every sign-in starts clean, so another account can be chosen
            OnWebReady(Web.CoreWebView2);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        {
            Failed = true;
            Close();
        }
    }

    // ---- keyboard ------------------------------------------------------------------------

    private void BuildKeyboard()
    {
        var rows = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
        foreach (var keys in Layout)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var key in keys)
            {
                var button = new Button { Style = (Style)FindResource("OskKey"), Content = key, Tag = key, Width = 48, Height = 52, FontSize = 20, Margin = new Thickness(2.5), Focusable = true };
                if (key is "espaço") { button.Width = 200; button.Content = UiText.T("espaço"); }
                else if (key is "entrar") { button.Width = 120; button.Content = UiText.T("entrar"); }
                else if (key is "fechar") { button.Width = 120; button.Content = UiText.T("fechar"); }
                else if (key is "abc") { button.Width = 76; _capsButton = button; }
                if (key.Length == 1 && char.IsLetter(key[0])) _letters.Add(button);
                button.Click += OnKey;
                row.Children.Add(button);
            }
            rows.Children.Add(row);
        }
        var hint = new TextBlock
        {
            Text = UiText.T("A digita · X apaga · Y espaço · B fecha o teclado"),
            Foreground = System.Windows.Media.Brushes.Gray, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2),
        };
        rows.Children.Add(hint);
        _keyboard.Children.Add(rows);
    }

    private bool KeyboardOpen => _keyboard.Visibility == Visibility.Visible;

    private void SetKeyboard(bool open)
    {
        _keyboard.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (!open) return;
        _shift = false;
        _caps = false;
        RefreshLetters();
        // the first letter row gets the focus; the page keeps its own focused field
        Dispatcher.BeginInvoke(() =>
        {
            if (_keyboard.Children[0] is StackPanel { Children: [_, StackPanel second, ..] } && second.Children[0] is Button first) first.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RefreshLetters()
    {
        foreach (var button in _letters)
        {
            var letter = (string)button.Tag;
            button.Content = _shift || _caps ? letter.ToUpperInvariant() : letter;
        }
        if (_capsButton is not null) _capsButton.Content = _caps ? "ABC" : "abc";
    }

    private async void OnKey(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        switch (key)
        {
            case "⌫": await RunAsync("(function(){var e=document.activeElement;if(!e)return;var p=e.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;var s=Object.getOwnPropertyDescriptor(p,'value');if(!s||!s.set)return;var v=e.value||'';var a=e.selectionStart,b=e.selectionEnd;if(a===null||a===undefined){a=b=v.length;}if(a!==b){v=v.slice(0,a)+v.slice(b);}else if(a>0){v=v.slice(0,a-1)+v.slice(a);a--;}s.set.call(e,v);try{e.setSelectionRange(a,a);}catch(x){}e.dispatchEvent(new Event('input',{bubbles:true}));})()"); break;
            case "⇧": _shift = !_shift; RefreshLetters(); break;
            case "abc": _caps = !_caps; RefreshLetters(); break;
            case "espaço": await TypeAsync(" "); break;
            case "entrar": await RunAsync("(function(){var e=document.activeElement;var f=e&&e.form;if(f&&f.requestSubmit){f.requestSubmit();return;}var b=document.querySelector('button[type=submit],input[type=submit]');if(b)b.click();})()"); break;
            case "fechar": SetKeyboard(false); break;
            default:
                await TypeAsync(_shift || _caps ? key.ToUpperInvariant() : key);
                if (_shift) { _shift = false; RefreshLetters(); }
                break;
        }
    }

    private Task TypeAsync(string text)
    {
        var literal = JsonSerializer.Serialize(text); // only the key pressed is sent, never anything read back from the page
        return RunAsync("(function(t){var e=document.activeElement;if(!e)return;var tag=e.tagName;if(tag!=='INPUT'&&tag!=='TEXTAREA')return;var p=tag==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;var s=Object.getOwnPropertyDescriptor(p,'value');if(!s||!s.set)return;var v=e.value||'';var a=e.selectionStart,b=e.selectionEnd;if(a===null||a===undefined){a=b=v.length;}v=v.slice(0,a)+t+v.slice(b);a+=t.length;s.set.call(e,v);try{e.setSelectionRange(a,a);}catch(x){}e.dispatchEvent(new Event('input',{bubbles:true}));})(" + literal + ")");
    }

    private async Task RunAsync(string script)
    {
        try { if (Web.CoreWebView2 is { } web) await web.ExecuteScriptAsync(script); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException) { /* page is changing */ }
    }

    // ---- controller ----------------------------------------------------------------------

    private async void OnPad(GamepadButton button)
    {
        if (!IsActive) return;
        if (KeyboardOpen)
        {
            switch (button)
            {
                case GamepadButton.Up: Move(FocusNavigationDirection.Up); break;
                case GamepadButton.Down: Move(FocusNavigationDirection.Down); break;
                case GamepadButton.Left: Move(FocusNavigationDirection.Left); break;
                case GamepadButton.Right: Move(FocusNavigationDirection.Right); break;
                case GamepadButton.A: if (Keyboard.FocusedElement is Button b) b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                case GamepadButton.X: foreach (var k in AllKeys()) if ((string)k.Tag == "⌫") k.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                case GamepadButton.Y: await TypeAsync(" "); break;
                case GamepadButton.RightTrigger: _caps = !_caps; RefreshLetters(); break;
                case GamepadButton.B: SetKeyboard(false); break;
            }
            return;
        }

        switch (button)
        {
            case GamepadButton.Up:
            case GamepadButton.Left:
            case GamepadButton.LeftShoulder: await RunAsync(StepScript(-1)); break;
            case GamepadButton.Down:
            case GamepadButton.Right:
            case GamepadButton.RightShoulder: await RunAsync(StepScript(+1)); break;
            case GamepadButton.A: await RunAsync("(function(){var e=document.activeElement;if(e&&e!==document.body)e.click();})()"); break;
            case GamepadButton.Y: SetKeyboard(true); break;
            case GamepadButton.B: Close(); break;
        }
    }

    private IEnumerable<Button> AllKeys()
    {
        if (_keyboard.Children[0] is not StackPanel rows) yield break;
        foreach (var row in rows.Children.OfType<StackPanel>())
            foreach (var key in row.Children.OfType<Button>()) yield return key;
    }

    private void Move(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is UIElement current) current.MoveFocus(new TraversalRequest(direction));
    }

    /// <summary>Moves the page's focus to the next (or previous) visible field or button and scrolls it into view.</summary>
    private static string StepScript(int step) =>
        "(function(d){var l=Array.prototype.filter.call(document.querySelectorAll('input:not([type=hidden]),button,a[href],select,textarea,[tabindex]:not([tabindex=\"-1\"])'),function(e){var r=e.getBoundingClientRect();return !e.disabled&&r.width>0&&r.height>0;});if(!l.length)return;var i=l.indexOf(document.activeElement);i=i<0?(d>0?0:l.length-1):(i+d+l.length)%l.length;l[i].focus();l[i].scrollIntoView({block:'center'});})(" + step + ")";
}
