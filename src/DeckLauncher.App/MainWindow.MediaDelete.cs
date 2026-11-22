using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeckLauncher.App;

// Mídias: delete a capture from inside the launcher (X or Delete on a tile, or inside the viewer). It goes to the Windows Recycle Bin, never straight to oblivion.
public partial class MainWindow
{
    private bool _deletingMedia;

    /// <summary>X / Delete on the focused tile of the Mídias page. Returns true when the key was used.</summary>
    private bool TryDeleteFocusedMedia()
    {
        if (_page != "Mídias" || MediaViewerOpen || ConfirmOpen) return false;
        if (Keyboard.FocusedElement is not Button button) return false;
        var index = MediaGrid.Children.IndexOf(button);
        if (index < 0) return false;
        _mediaIndex = index;
        _ = DeleteMediaAsync(index, fromViewer: false);
        return true;
    }

    private async Task DeleteMediaAsync(int index, bool fromViewer)
    {
        if (_deletingMedia || index < 0 || index >= _media.Count) return;
        _deletingMedia = true;
        try
        {
            var file = _media[index];
            var sure = await AskConfirmAsync("Excluir mídia?",
                file.IsVideo ? "Este vídeo será enviado para a Lixeira do Windows." : "Esta captura de tela será enviada para a Lixeira do Windows.",
                "Excluir", "Cancelar", $"{file.Title}  ·  {file.Taken:dd/MM/yyyy HH:mm}");
            if (!sure) return;

            if (fromViewer) StopVideo(); // lets go of the file
            var path = file.Path;
            var done = await Task.Run(() => SendToRecycleBin(path));
            if (!done)
            {
                ShowToast(UiText.T("Não foi possível excluir esta mídia. Ela pode estar em uso."));
                return;
            }

            UiSound.Play("back");
            ShowToast(UiText.T("Mídia enviada para a Lixeira"));
            _media.RemoveAt(index);
            _mediaIndex = Math.Max(0, Math.Min(index, _media.Count - 1));
            if (fromViewer)
            {
                if (_media.Count == 0) CloseMediaViewer();
                else ShowViewerItem(_mediaIndex);
            }
            _mediaSignature = ""; // the page is read again and its tiles rebuilt
            LoadMedia();
        }
        finally { _deletingMedia = false; }
    }

    private static bool SendToRecycleBin(string path)
    {
        try
        {
            if (!File.Exists(path)) return true; // already gone
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException or NotSupportedException)
        {
            return false;
        }
    }
}
