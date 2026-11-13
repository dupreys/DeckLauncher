using System.Diagnostics;
using System.Reflection;
using System.Windows;
using DeckLauncher.Core;

namespace DeckLauncher.App;

// "Is there a newer version?" Asks GitHub's public releases API (no key, nothing downloaded or installed by itself):
// when there is one, a notice appears once and Configurações > Sistema > Atualizações opens the download page.
public partial class MainWindow
{
    /// <summary>"owner/name" of the GitHub repository whose releases are checked. Releases are published at github.com/dupreys/DeckLauncher.</summary>
    private const string UpdateRepository = "dupreys/DeckLauncher";

    private UpdateInfo? _update;
    private bool _checkingUpdate;

    private static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_checkingUpdate) return;

        if (UpdateRepository.StartsWith("SEU-USUARIO", StringComparison.Ordinal))
        {
            if (manual) UpdateText.Text = "O repositório do GitHub ainda não foi configurado no programa (UpdateRepository em MainWindow.Update.cs).";
            return;
        }

        _checkingUpdate = true;
        if (manual)
        {
            UpdateButton.IsEnabled = false;
            UpdateText.Text = "Procurando...";
        }

        try
        {
            var info = await UpdateChecker.CheckAsync(LibraryService.Http, UpdateRepository, CurrentVersion);
            App.Settings.LastUpdateCheck = DateTimeOffset.Now;
            _update = info;
            if (info is null) { _updateFile = null; UpdateInstaller.Clean(UpdateDir); } // up to date: an installer left from the last update is no longer needed
            ShowUpdateState();

            if (info is not null && App.Settings.NotifiedUpdate != info.Tag)
            {
                App.Settings.NotifiedUpdate = info.Tag;
                ShowToast($"Nova versão {info.Tag} disponível. Veja em Configurações > Sistema.");
            }
            SaveSettings();
            if (info is not null && App.Settings.AutoUpdate && !manual && CanSelfUpdate(info)) _ = DownloadUpdateAsync(info, false);
        }
        finally
        {
            _checkingUpdate = false;
            UpdateButton.IsEnabled = !_updateDownloading;
        }
    }

    private void ShowUpdateState()
    {
        var current = CurrentVersion;
        var shown = $"{current.Major}.{current.Minor}.{Math.Max(current.Build, 0)}";
        UpdateButton.IsEnabled = !_updateDownloading;
        if (_updateDownloading && _update is { } loading)
        {
            UpdateText.Text = $"Baixando a versão {loading.Tag}… {(int)Math.Round(_updateProgress * 100)}%";
            UpdateButton.Content = "BAIXANDO";
        }
        else if (_updateFile is not null && _update is { } ready)
        {
            UpdateText.Text = $"A versão {ready.Tag} está pronta (você tem a {shown}). O launcher fecha, instala e abre sozinho.";
            UpdateButton.Content = "INSTALAR";
        }
        else if (_update is { } info && CanSelfUpdate(info))
        {
            UpdateText.Text = $"Nova versão {info.Tag} disponível (você tem a {shown}).";
            UpdateButton.Content = "ATUALIZAR";
        }
        else if (_update is { } page)
        {
            UpdateText.Text = $"Nova versão {page.Tag} disponível (você tem a {shown}). Baixe o instalador e execute para atualizar.";
            UpdateButton.Content = "BAIXAR";
        }
        else
        {
            var when = App.Settings.LastUpdateCheck is { } t ? $" Última verificação: {t:dd/MM HH:mm}." : "";
            UpdateText.Text = $"Você está na versão {shown}, sem versão nova encontrada.{when}";
            UpdateButton.Content = "VERIFICAR";
        }
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_updateFile is not null) { InstallUpdate(); return; }
        if (_update is { } fresh && CanSelfUpdate(fresh)) { await DownloadUpdateAsync(fresh, installWhenReady: true); return; }
        if (_update is { } info)
        {
            try { Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true })?.Dispose(); }
            catch (System.ComponentModel.Win32Exception) { ShowToast("Não foi possível abrir o navegador."); }
            return;
        }
        await CheckForUpdateAsync(manual: true);
    }
}
