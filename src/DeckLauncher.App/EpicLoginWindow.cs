using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using DeckLauncher.Core.Epic;

namespace DeckLauncher.App;

/// <summary>Epic's own login page inside a window (like Playnite). When Epic lands on its redirect page the code is read from it; the password is never seen.</summary>
internal sealed class EpicLoginWindow : LoginWindowBase
{
    private bool _done;

    /// <summary>The sign-in code, or null when the window was closed without signing in.</summary>
    public string? Code { get; private set; }

    public EpicLoginWindow() : base("Entrar na Epic Games", "epic-login") { }

    protected override void OnWebReady(CoreWebView2 web)
    {
        web.NavigationCompleted += async (_, _) => await CheckPageAsync();
        web.Navigate(EpicAccountClient.LoginUrl);
    }

    private async Task CheckPageAsync()
    {
        if (_done) return;
        var uri = Web.Source?.AbsoluteUri ?? "";
        if (!uri.StartsWith("https://www.epicgames.com/id/api/redirect", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var raw = await Web.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
            var text = JsonSerializer.Deserialize<string>(raw);
            if (EpicAccountClient.ExtractAuthCode(text) is not { } code) return;
            _done = true;
            Code = code;
            Web.CoreWebView2.CookieManager.DeleteAllCookies();
            Close();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException) { /* not the page yet */ }
    }
}
