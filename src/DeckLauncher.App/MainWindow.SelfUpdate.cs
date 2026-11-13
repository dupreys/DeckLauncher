using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using DeckLauncher.Core;
using DeckLauncher.Core.Settings;
using Microsoft.Win32;

namespace DeckLauncher.App;

// Updating from inside the launcher: the installer of the newer release is downloaded from GitHub (size and SHA-256 checked), then run
// silently; it closes the launcher, replaces the files and opens the launcher again. Only for a copy that was put in place by the installer.
public partial class MainWindow
{
    private static readonly HttpClient UpdateHttp = CreateUpdateHttp();
    private static string UpdateDir => Path.Combine(SettingsStore.DefaultDirectory, "update");

    private string? _updateFile; // downloaded and verified, waiting to be installed
    private bool _updateDownloading;
    private double _updateProgress;

    private static HttpClient CreateUpdateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) }; // the installer is large: the usual 15 s would cut it
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeckLauncher");
        return client;
    }

    /// <summary>True when this launcher runs from the folder the installer used (not "dotnet run", not a copied folder).</summary>
    private static bool IsInstalledCopy()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\DeckLauncher");
            if (key?.GetValue("InstallDir") is not string dir || dir.Length == 0) return false;
            static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
            return string.Equals(Norm(dir), Norm(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool CanSelfUpdate(UpdateInfo info) => UpdateInstaller.CanInstall(info) && IsInstalledCopy();

    /// <summary>Downloads the installer in the background. With <paramref name="installWhenReady"/> it installs right after; otherwise it offers to.</summary>
    private async Task DownloadUpdateAsync(UpdateInfo info, bool installWhenReady)
    {
        if (_updateDownloading || _updateFile is not null) return;
        _updateDownloading = true;
        _updateProgress = 0;
        ShowUpdateState();
        ShowToast($"Baixando a versão {info.Tag}… acompanhe em Configurações > Sistema.");
        try
        {
            var progress = new Progress<double>(value =>
            {
                _updateProgress = value;
                ShowUpdateState();
            });
            _updateFile = await UpdateInstaller.DownloadAsync(UpdateHttp, info, UpdateDir, progress);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException)
        {
            _updateFile = null;
            ShowToast("Não foi possível baixar a atualização. Tente de novo em Configurações > Sistema.");
        }
        finally
        {
            _updateDownloading = false;
            ShowUpdateState();
        }

        if (_updateFile is null) return;
        AddNotification($"A versão {info.Tag} foi baixada e está pronta para instalar.");
        UpdateBell();
        if (installWhenReady) { InstallUpdate(); return; }

        // A game is open: do not interrupt it, the button in Configurações > Sistema stays ready.
        if (_session is not null || LaunchOverlayOpen) { ShowToast($"A versão {info.Tag} está pronta. Instale em Configurações > Sistema."); return; }
        var now = await AskConfirmAsync($"Atualizar para a {info.Tag}?",
            "A nova versão já foi baixada. O launcher fecha, instala e abre sozinho em alguns segundos.",
            "Atualizar agora", "Depois");
        if (now) InstallUpdate();
    }

    private void InstallUpdate()
    {
        if (_updateFile is not { } file || !File.Exists(file)) return;
        if (_session is not null) { ShowToast("Encerre o jogo aberto antes de atualizar."); return; }
        try
        {
            SaveSettings();
            Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NOCANCEL /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            ShowToast("Não foi possível iniciar o instalador da atualização.");
            return;
        }
        Close(); // the installer waits for the launcher to close, replaces it and opens the new one
    }

    private void OnAutoUpdateSwitchClick(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Settings.AutoUpdate = AutoUpdateSwitch.IsChecked == true;
        SaveSettings();
        ShowToast(App.Settings.AutoUpdate ? "O launcher vai baixar as atualizações sozinho." : "O launcher só avisa quando houver versão nova.");
        if (App.Settings.AutoUpdate && _update is { } info && CanSelfUpdate(info)) _ = DownloadUpdateAsync(info, false);
    }
}
