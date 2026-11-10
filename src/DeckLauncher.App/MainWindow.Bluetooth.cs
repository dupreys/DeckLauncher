using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeckLauncher.Core.Settings;

namespace DeckLauncher.App;

// Bluetooth in the sound panel: radio switch, saved wireless headsets, connect by clicking or by itself.
public partial class MainWindow
{
    private string? _btState;                       // "On", "Off", "None" or null (unknown)
    private bool _btBusy;
    private readonly HashSet<ulong> _btConnected = new();
    private readonly HashSet<ulong> _btManuallyOff = new(); // disconnected by the user: automatic connection leaves them alone until the launcher restarts
    private readonly DispatcherTimer _btTimer = new() { Interval = TimeSpan.FromSeconds(45) };

    private static List<SavedBluetoothDevice> BtSaved => App.Settings.BluetoothDevices;

    private void InitBluetooth()
    {
        _btTimer.Tick += async (_, _) => await AutoConnectBluetoothAsync();
        _btTimer.Start();
        AutoConnectBluetoothLater(5);
        UpdateBluetoothUi();
    }

    private async void AutoConnectBluetoothLater(int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        await AutoConnectBluetoothAsync();
    }

    private void SaveBluetooth()
    {
        try { App.Store.Save(App.Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task AutoConnectBluetoothAsync()
    {
        if (_btBusy || !App.Settings.BluetoothAutoConnect || BtSaved.Count == 0 || !IsActive) return;
        _btBusy = true;
        try
        {
            var targets = BtSaved.Select(d => SavedBluetoothDevice.TryParseAddress(d.Address, out var a) ? a : 0UL)
                .Where(a => a != 0 && !_btManuallyOff.Contains(a)).ToList();
            if (targets.Count == 0) return;
            var connectedNow = await Task.Run(() =>
            {
                var done = new List<ulong>();
                foreach (var address in targets)
                {
                    if (BluetoothNative.IsConnected(address)) { done.Add(address); continue; }
                    // Only try the ones Windows can see are in range: a connection attempt to a switched-off headset just waits.
                    if (BluetoothNative.PairedDevices().Any(d => d.Address == address) && BluetoothNative.Connect(address)) done.Add(address);
                }
                return done;
            });
            foreach (var address in connectedNow)
            {
                if (_btConnected.Add(address))
                {
                    var name = BtSaved.FirstOrDefault(d => SavedBluetoothDevice.TryParseAddress(d.Address, out var a) && a == address)?.Name ?? "Fone";
                    AddNotification($"{name} conectado por Bluetooth.");
                    ShowToast($"{name} conectado.");
                }
            }
        }
        finally { _btBusy = false; }
        if (VolPanel.Visibility == Visibility.Visible) FillBluetoothSaved();
    }

    private async Task RefreshBluetoothAsync()
    {
        BtToggleButton.Content = "Bluetooth: verificando…";
        _btState = await Task.Run(() => BluetoothNative.RadioState("Get"));
        UpdateBluetoothUi();
        await RefreshBluetoothConnectionsAsync();
    }

    private async Task RefreshBluetoothConnectionsAsync()
    {
        var addresses = BtSaved.Select(d => SavedBluetoothDevice.TryParseAddress(d.Address, out var a) ? a : 0UL).Where(a => a != 0).ToList();
        var connected = _btState == "On" ? await Task.Run(() => addresses.Where(BluetoothNative.IsConnected).ToList()) : new List<ulong>();
        _btConnected.Clear();
        foreach (var a in connected) _btConnected.Add(a);
        FillBluetoothSaved();
    }

    private void UpdateBluetoothUi()
    {
        BtToggleButton.Content = _btState switch
        {
            "On" => "Bluetooth: ligado · desligar",
            "Off" => "Bluetooth: desligado · ligar",
            "None" => "Este PC não tem Bluetooth",
            _ => "Bluetooth: indisponível",
        };
        BtToggleButton.IsEnabled = _btState is "On" or "Off";
        BtAddButton.IsEnabled = _btState == "On";
        BtAutoButton.Content = App.Settings.BluetoothAutoConnect ? "Conectar automaticamente: sim" : "Conectar automaticamente: não";
        BtSavedTitle.Visibility = BtSaved.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_btState != "On") BtPickPanel.Visibility = Visibility.Collapsed;
    }

    private async void OnBtToggleClick(object sender, RoutedEventArgs e)
    {
        if (_btBusy || _btState is not ("On" or "Off")) return;
        var turnOn = _btState == "Off";
        _btBusy = true;
        BtToggleButton.Content = turnOn ? "Ligando o Bluetooth…" : "Desligando o Bluetooth…";
        string? result;
        try { result = await Task.Run(() => BluetoothNative.RadioState(turnOn ? "On" : "Off")); }
        finally { _btBusy = false; }
        if (result is null) ShowToast("Não foi possível alterar o Bluetooth. Tente pelas configurações do Windows.");
        else _btState = result;
        UpdateBluetoothUi();
        await RefreshBluetoothConnectionsAsync();
        if (_btState == "On") AutoConnectBluetoothLater(4);
    }

    private async void OnBtAddClick(object sender, RoutedEventArgs e)
    {
        if (BtPickPanel.Visibility == Visibility.Visible) { BtPickPanel.Visibility = Visibility.Collapsed; return; }
        var all = await Task.Run(BluetoothNative.PairedDevices);
        var saved = BtSaved.Select(d => d.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = all.Where(d => !saved.Contains(SavedBluetoothDevice.FormatAddress(d.Address)) && !string.IsNullOrWhiteSpace(d.Name)).ToList();
        var audio = candidates.Where(d => d.IsAudio).ToList();
        if (audio.Count > 0) candidates = audio;

        BtPickList.Children.Clear();
        BtPickHint.Text = candidates.Count > 0
            ? "Escolha um aparelho já pareado com o Windows. Para um fone novo, coloque-o em modo de pareamento e use o botão abaixo."
            : "Nenhum aparelho pareado novo. Coloque o fone em modo de pareamento e use o botão abaixo; depois volte aqui para adicioná-lo.";
        foreach (var device in candidates)
        {
            var item = device;
            var button = new Button { Style = (Style)FindResource("PowerItem"), Height = 52, FontSize = 16, Content = "🎧  " + item.Name };
            button.Click += (_, _) => AddBluetoothDevice(item);
            BtPickList.Children.Add(button);
        }
        BtPickPanel.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => { (BtPickList.Children.OfType<Button>().FirstOrDefault() ?? BtPairButton).Focus(); }, DispatcherPriority.Input);
    }

    private async void AddBluetoothDevice(BluetoothNative.Device device)
    {
        BtSaved.Add(new SavedBluetoothDevice { Name = device.Name, Address = SavedBluetoothDevice.FormatAddress(device.Address) });
        App.Settings.Normalize();
        SaveBluetooth();
        BtPickPanel.Visibility = Visibility.Collapsed;
        UpdateBluetoothUi();
        FillBluetoothSaved();
        BtAddButton.Focus();
        ShowToast($"{device.Name} salvo. Conectando…");
        await ConnectBluetoothAsync(device.Address, device.Name);
    }

    private void OnBtPairClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); }
        catch (Win32Exception) { ShowToast("Não foi possível abrir as configurações de Bluetooth."); }
    }

    private void OnBtAutoClick(object sender, RoutedEventArgs e)
    {
        App.Settings.BluetoothAutoConnect = !App.Settings.BluetoothAutoConnect;
        SaveBluetooth();
        UpdateBluetoothUi();
        if (App.Settings.BluetoothAutoConnect) _ = AutoConnectBluetoothAsync();
    }

    private void FillBluetoothSaved()
    {
        BtSavedList.Children.Clear();
        foreach (var saved in BtSaved.ToList())
        {
            if (!SavedBluetoothDevice.TryParseAddress(saved.Address, out var address)) continue;
            var name = saved.Name;
            var connected = _btConnected.Contains(address);

            var connect = new Button
            {
                Style = (Style)FindResource("PowerItem"), Height = 56, FontSize = 16, Margin = new Thickness(4, 4, 4, 4),
                Content = "🎧  " + name + (connected ? "  ·  conectado" : "  ·  conectar"),
            };
            connect.Click += async (_, _) =>
            {
                if (_btConnected.Contains(address))
                {
                    _btManuallyOff.Add(address);
                    await Task.Run(() => BluetoothNative.Disconnect(address));
                    _btConnected.Remove(address);
                    ShowToast($"{name} desconectado.");
                    FillBluetoothSaved();
                }
                else
                {
                    _btManuallyOff.Remove(address);
                    await ConnectBluetoothAsync(address, name);
                }
            };

            var remove = new Button
            {
                Style = (Style)FindResource("SquareButton"), Width = 56, Height = 56, Margin = new Thickness(0, 4, 0, 4),
                Content = new TextBlock { Text = "", Style = (Style)FindResource("Icon") },
            };
            remove.Click += (_, _) =>
            {
                BtSaved.RemoveAll(d => string.Equals(d.Address, saved.Address, StringComparison.OrdinalIgnoreCase));
                SaveBluetooth();
                UpdateBluetoothUi();
                FillBluetoothSaved();
                BtAddButton.Focus();
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(remove, 1);
            row.Children.Add(connect);
            row.Children.Add(remove);
            BtSavedList.Children.Add(row);
        }
    }

    private async Task ConnectBluetoothAsync(ulong address, string name)
    {
        if (_btBusy) { ShowToast("Aguarde a operação de Bluetooth em andamento."); return; }
        if (_btState != "On")
        {
            ShowToast("Ligue o Bluetooth primeiro.");
            return;
        }
        _btBusy = true;
        ShowToast($"Conectando {name}…");
        bool ok;
        try { ok = await Task.Run(() => BluetoothNative.Connect(address)); }
        finally { _btBusy = false; }
        if (ok) _btConnected.Add(address); else _btConnected.Remove(address);
        ShowToast(ok ? $"{name} conectado." : $"Não consegui conectar {name}. Confira se está ligado e perto.");
        if (VolPanel.Visibility == Visibility.Visible) FillBluetoothSaved();
    }
}
