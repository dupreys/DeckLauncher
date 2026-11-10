using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeckLauncher.Core;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

// Steam accounts: on a PC with more than one Steam account the launcher asks which one to use (or follows the setting),
// then shows only that account's installed games. The photo in the top bar switches account later.
// Nothing here logs in to Steam: the accounts are only read from the Steam client's local files.
public partial class MainWindow
{
    private IReadOnlyList<SteamLocalAccount> _accounts = Array.Empty<SteamLocalAccount>();
    private string? _activeSteamId;
    private TaskCompletionSource<string?>? _accountChoice;
    private bool _accountCanCancel;

    private bool AccountOpen => AccountPanel.Visibility == Visibility.Visible;
    private bool HasSeveralAccounts => _accounts.Count > 1;

    private SteamLocalAccount? AccountById(string? id) => _accounts.FirstOrDefault(a => a.SteamId64 == id);

    // ---- Start-up ----------------------------------------------------------------------------

    /// <summary>Reads the accounts and decides which one to use. Asks the user when the setting says so (or the saved one is gone).</summary>
    private async Task ChooseAccountAtStartupAsync()
    {
        _accounts = _library.SteamPath is { } path ? await Task.Run(() => SteamLoginUsers.ReadAll(path)) : Array.Empty<SteamLocalAccount>();
        if (_accounts.Count == 0) return;
        if (_accounts.Count == 1)
        {
            _activeSteamId = _accounts[0].SteamId64;
            return;
        }

        var settings = App.Settings;
        var wanted = settings.AccountMode switch
        {
            "Fixed" => settings.FixedAccountId,
            "Last" => settings.LastAccountId,
            _ => null,
        };
        var chosen = AccountById(wanted)?.SteamId64 ?? await AskAccountAsync(canCancel: false) ?? _accounts[0].SteamId64;
        SetActiveAccount(chosen);
    }

    // ---- Picker ------------------------------------------------------------------------------

    /// <summary>Shows the account cards and waits for a choice. Null when cancelled (only possible after start-up).</summary>
    private Task<string?> AskAccountAsync(bool canCancel)
    {
        _accountCanCancel = canCancel;
        AccountList.Children.Clear();
        foreach (var account in _accounts) AccountList.Children.Add(BuildAccountCard(account));
        AccountHint.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        AccountPanel.Visibility = Visibility.Visible;
        _accountChoice = new TaskCompletionSource<string?>();

        var current = AccountList.Children.OfType<Button>().FirstOrDefault(b => (string?)b.Tag == _activeSteamId)
                      ?? AccountList.Children.OfType<Button>().FirstOrDefault();
        Dispatcher.BeginInvoke(() => current?.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
        return _accountChoice.Task;
    }

    private Button BuildAccountCard(SteamLocalAccount account)
    {
        var avatar = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(75), Background = (Brush)FindResource("PanelRaised"), ClipToBounds = true };
        var image = LoadLocalImage(AvatarCachePath(account.SteamId64));
        if (image is not null) avatar.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        else avatar.Child = new TextBlock
        {
            Text = account.PersonaName.Length > 0 ? account.PersonaName[..1].ToUpperInvariant() : "?",
            FontSize = 64, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        var content = new StackPanel { Width = 200, Margin = new Thickness(8) };
        content.Children.Add(avatar);
        content.Children.Add(new TextBlock { Text = account.PersonaName, FontSize = 22, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 14, 0, 0), Foreground = Brushes.White });
        content.Children.Add(new TextBlock { Text = account.AccountName, FontSize = 13, Foreground = (Brush)FindResource("TextMuted"), TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var card = new Button { Style = (Style)FindResource("PanelTile"), Content = content, Tag = account.SteamId64, Margin = new Thickness(14, 0, 14, 0), Padding = new Thickness(10) };
        card.Click += (_, _) => CompleteAccountChoice(account.SteamId64);
        return card;
    }

    private string? AvatarCachePath(string steamId64) =>
        _library.SteamPath is { } path ? Path.Combine(path, "config", "avatarcache", steamId64 + ".png") : null;

    private void CompleteAccountChoice(string? id)
    {
        AccountPanel.Visibility = Visibility.Collapsed;
        var choice = _accountChoice;
        _accountChoice = null;
        choice?.TrySetResult(id);
    }

    /// <summary>B on the picker: only when it was opened from the profile (at start-up a choice is required).</summary>
    private void CloseAccountPicker()
    {
        if (_accountCanCancel) CompleteAccountChoice(null);
    }

    // ---- Switching ---------------------------------------------------------------------------

    private async void OnProfileSwitchClick(object sender, RoutedEventArgs e)
    {
        CloseProfile();
        if (!HasSeveralAccounts) return;
        var id = await AskAccountAsync(canCancel: true);
        FocusPage();
        if (id is null || id == _activeSteamId) return;
        SetActiveAccount(id);
        await RefreshAsync();
    }

    /// <summary>Makes the account the one in use: remembers it, resets what belongs to the previous one and reloads the photo and name.</summary>
    private void SetActiveAccount(string id)
    {
        var changed = App.Settings.LastAccountId != id; // the saved name and photo belong to another account
        _activeSteamId = id;
        App.Settings.LastAccountId = id;
        if (changed || string.IsNullOrWhiteSpace(App.Settings.PersonaName))
        {
            App.Settings.PersonaName = AccountById(id)?.PersonaName;
            App.Settings.AvatarUrl = null;
        }
        _steamAvatar = null;
        _friendsCache = null;
        _friendsOnline = null;
        _homeInfoAt = DateTime.MinValue;
        SaveSettings();
        _ = RefreshAvatarAsync();
        _ = FetchSteamProfileAsync();
        ShowToast("Conta: " + (AccountById(id)?.PersonaName ?? id));
        if (HasSeveralAccounts && App.Settings.SwitchSteamClient) _ = SyncSteamClientAsync(id);
    }

    /// <summary>Asks the Steam client to start on the chosen account too (see SteamClientSwitch).</summary>
    private async Task SyncSteamClientAsync(string id)
    {
        if (AccountById(id) is not { } account || _library.SteamPath is not { } path) return;
        var result = await SteamClientSwitch.SwitchAsync(path, account, _all.Any(i => i.IsRunning));
        switch (result)
        {
            case SteamClientSwitch.Result.Switched:
                ShowToast($"Steam trocada para a conta {account.PersonaName}");
                break;
            case SteamClientSwitch.Result.NeedsPassword:
                ShowToast($"A Steam não lembra a senha de {account.PersonaName}: entre nessa conta uma vez na Steam");
                break;
            case SteamClientSwitch.Result.GameRunning:
                ShowToast("Há um jogo aberto: a Steam não foi trocada de conta");
                break;
            case SteamClientSwitch.Result.Failed:
                ShowToast("Não consegui trocar a conta da Steam. Troque por ela mesma");
                break;
        }
    }

    /// <summary>Every game installed on this PC shows for every account: Steam installs for the whole PC, and the Steam profile in use keeps its own saves.</summary>
    private Task<List<Game>> FilterByAccountAsync(List<Game> games) => Task.FromResult(games);

    // ---- Settings > Integrações ----------------------------------------------------------------

    private void ShowAccountChecks()
    {
        SwitchClientSwitch.IsChecked = App.Settings.SwitchSteamClient;
        SetChoice(App.Settings.AccountMode, (AccountModeAsk, "Ask"), (AccountModeLast, "Last"), (AccountModeFixed, "Fixed"));
        var fixedName = AccountById(App.Settings.FixedAccountId)?.PersonaName;
        FixedAccountButton.Content = HasSeveralAccounts ? "CONTA: " + (fixedName ?? "escolher") : "SÓ UMA CONTA ENCONTRADA";
        FixedAccountButton.IsEnabled = HasSeveralAccounts;
    }

    private void OnAccountModeClick(object sender, RoutedEventArgs e) =>
        OnChoiceClick(sender, key => App.Settings.AccountMode = key, ShowAccountChecks, () => { });

    private void OnSwitchClientClick(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Settings.SwitchSteamClient = SwitchClientSwitch.IsChecked == true;
        SaveSettings();
    }

    /// <summary>Goes through the accounts one by one and uses that one every time the launcher opens.</summary>
    private void OnFixedAccountClick(object sender, RoutedEventArgs e)
    {
        if (!HasSeveralAccounts) return;
        var index = _accounts.ToList().FindIndex(a => a.SteamId64 == App.Settings.FixedAccountId);
        App.Settings.FixedAccountId = _accounts[(index + 1) % _accounts.Count].SteamId64;
        App.Settings.AccountMode = "Fixed";
        SaveSettings();
        ShowAccountChecks();
    }
}
