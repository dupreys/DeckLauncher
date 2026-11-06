using System.Windows;
using System.Windows.Input;
using DeckLauncher.Core;

namespace DeckLauncher.App;

// Game options on the game page: a name of your own for the game, and hiding it from the lists.
public partial class MainWindow
{
    private bool OptionsOpen => OptionsPanel.Visibility == Visibility.Visible || AppMenuPanel.Visibility == Visibility.Visible || AppAddPanel.Visibility == Visibility.Visible;

    private void OnOptionsClick(object sender, RoutedEventArgs e) => OpenOptions();

    private void OpenOptions()
    {
        if (_gameItem is not { } item || PowerOpen || ManageOpen || ProfileOpen || LaunchOverlayOpen) return;
        OptionsGameName.Text = item.Name;
        OptionsNameBox.Text = item.Name;
        OptionsResetNameButton.IsEnabled = App.Settings.GameOverrides.TryGetValue(GameSorter.Key(item.Game), out var o) && !string.IsNullOrWhiteSpace(o.Name);
        OptionsPanel.Visibility = Visibility.Visible;
        OptionsSaveNameButton.Focus();
    }

    private void CloseOptions()
    {
        if (AppAddPanel.Visibility == Visibility.Visible) { CloseAppAdd(); return; }
        if (AppMenuPanel.Visibility == Visibility.Visible) { CloseAppMenu(); return; }
        OptionsPanel.Visibility = Visibility.Collapsed;
        VirtualKeyboard.Hide();
        OptionsButton.Focus();
    }

    private void OnOptionsBackClick(object sender, RoutedEventArgs e) => CloseOptions();

    private void OnOptionsDimClick(object sender, MouseButtonEventArgs e) => CloseOptions();

    private async void OnOptionsSaveNameClick(object sender, RoutedEventArgs e)
    {
        if (_gameItem is not { } item) return;
        var name = GameOverrides.NormalizeName(OptionsNameBox.Text, item.Game.OriginalName ?? item.Game.Name);
        CloseOptions();
        await EditOverrideAsync(GameSorter.Key(item.Game), o => o.Name = name);
        ShowToast(name is null ? "Nome original restaurado" : "Nome salvo");
    }

    private async void OnOptionsResetNameClick(object sender, RoutedEventArgs e)
    {
        if (_gameItem is not { } item) return;
        CloseOptions();
        await EditOverrideAsync(GameSorter.Key(item.Game), o => o.Name = null);
        ShowToast("Nome original restaurado");
    }

    private async void OnOptionsHideClick(object sender, RoutedEventArgs e)
    {
        if (_gameItem is not { } item) return;
        App.Settings.HiddenGames[GameSorter.Key(item.Game)] = item.Game.OriginalName ?? item.Game.Name;
        SaveSettings();
        CloseOptions();
        CloseGame();
        ShowToast("Jogo escondido. Para mostrar de novo: Configurações > Jogos.");
        while (_refreshing) await Task.Delay(100);
        await RefreshAsync();
    }
}
