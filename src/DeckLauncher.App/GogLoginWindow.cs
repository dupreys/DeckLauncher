using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using DeckLauncher.Core.Gog;

namespace DeckLauncher.App;

/// <summary>GOG's own login page inside a window. When GOG lands on its success page the code is read from the address; the password is never seen.</summary>
internal sealed class GogLoginWindow : Window
{
    private readonly WebView2 _web = new();
    private bool _done;

    /// <summary>The sign-in code, or null when the window was closed without signing in.</summary>
    public string? Code { get; private set; }

    /// <summary>True when the login page could not be opened here (the caller then falls back to the browser).</summary>
    public bool Failed { get; private set; }

    public GogLoginWindow()
    {
        Title = UiText.T("Entrar na GOG");
        Width = 520;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.Black;
        Content = new Grid { Children = { _web } };
        Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckLauncher", "gog-login");
            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.CookieManager.DeleteAllCookies(); // every sign-in starts clean, so another account can be chosen
            _web.CoreWebView2.NavigationStarting += (_, e) => Check(e.Uri);
            _web.CoreWebView2.SourceChanged += (_, _) => Check(_web.Source?.AbsoluteUri);
            _web.CoreWebView2.NavigationCompleted += (_, _) => Check(_web.Source?.AbsoluteUri);
            _web.CoreWebView2.Navigate(GogAccountClient.LoginUrl);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        {
            Failed = true;
            Close();
        }
    }

    private void Check(string? uri)
    {
        if (_done || uri is null || !uri.StartsWith(GogAccountClient.RedirectPrefix, StringComparison.OrdinalIgnoreCase)) return;
        if (GogAccountClient.ExtractAuthCode(uri) is not { } code) return;
        _done = true;
        Code = code;
        _web.CoreWebView2.CookieManager.DeleteAllCookies();
        Dispatcher.BeginInvoke(new Action(Close)); // not from inside the navigation event
    }
}
