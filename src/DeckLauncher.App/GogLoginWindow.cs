using Microsoft.Web.WebView2.Core;
using DeckLauncher.Core.Gog;

namespace DeckLauncher.App;

/// <summary>GOG's own login page inside a window. When GOG lands on its success page the code is read from the address; the password is never seen.</summary>
internal sealed class GogLoginWindow : LoginWindowBase
{
    private bool _done;

    /// <summary>The sign-in code, or null when the window was closed without signing in.</summary>
    public string? Code { get; private set; }

    public GogLoginWindow() : base("Entrar na GOG", "gog-login") { }

    protected override void OnWebReady(CoreWebView2 web)
    {
        web.NavigationStarting += (_, e) => Check(e.Uri);
        web.SourceChanged += (_, _) => Check(Web.Source?.AbsoluteUri);
        web.NavigationCompleted += (_, _) => Check(Web.Source?.AbsoluteUri);
        web.Navigate(GogAccountClient.LoginUrl);
    }

    private void Check(string? uri)
    {
        if (_done || uri is null || !uri.StartsWith(GogAccountClient.RedirectPrefix, StringComparison.OrdinalIgnoreCase)) return;
        if (GogAccountClient.ExtractAuthCode(uri) is not { } code) return;
        _done = true;
        Code = code;
        Web.CoreWebView2.CookieManager.DeleteAllCookies();
        Dispatcher.BeginInvoke(new Action(Close)); // not from inside the navigation event
    }
}
