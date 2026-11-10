using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using DeckLauncher.Core.Epic;

namespace DeckLauncher.App;

/// <summary>Epic's own login page inside a window (like Playnite). When Epic lands on its redirect page the code is read from it; the password is never seen.</summary>
internal sealed class EpicLoginWindow : Window
{
    private readonly WebView2 _web = new();
    private bool _done;

    /// <summary>The sign-in code, or null when the window was closed without signing in.</summary>
    public string? Code { get; private set; }

    public EpicLoginWindow()
    {
        Title = "Entrar na Epic Games";
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
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckLauncher", "epic-login");
            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.CookieManager.DeleteAllCookies(); // every sign-in starts clean, so another account can be chosen
            _web.CoreWebView2.NavigationCompleted += async (_, _) => await CheckPageAsync();
            _web.CoreWebView2.Navigate(EpicAccountClient.LoginUrl);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        {
            Failed = true;
            Close();
        }
    }

    /// <summary>True when the login page could not be opened here (the caller then falls back to the browser).</summary>
    public bool Failed { get; private set; }

    private async Task CheckPageAsync()
    {
        if (_done) return;
        var uri = _web.Source?.AbsoluteUri ?? "";
        if (!uri.StartsWith("https://www.epicgames.com/id/api/redirect", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var raw = await _web.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
            var text = JsonSerializer.Deserialize<string>(raw);
            if (EpicAccountClient.ExtractAuthCode(text) is not { } code) return;
            _done = true;
            Code = code;
            _web.CoreWebView2.CookieManager.DeleteAllCookies();
            Close();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException) { /* not the page yet */ }
    }
}
