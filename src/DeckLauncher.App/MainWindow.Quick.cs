using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeckLauncher.App;

// Top-bar quick panels: connected networks (Wi-Fi / Ethernet) and the system volume.
public partial class MainWindow
{
    private const string WifiGlyph = "", EthernetGlyph = "", OfflineGlyph = "";

    private Button? _quickOpener;

    private bool QuickOpen => QuickLayer.Visibility == Visibility.Visible;

    private sealed record NetConnection(bool Wifi, string Adapter, string Address);

    private void InitQuick()
    {
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Dispatcher.BeginInvoke(UpdateNetworkIcon);
        NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.BeginInvoke(UpdateNetworkIcon);
        UpdateNetworkIcon();
        UpdateVolumeUi();
    }

    // ---- Open / close -----------------------------------------------------------------------

    private void OpenQuick(Button opener, Border panel, Button firstFocus)
    {
        if (PowerOpen || ManageOpen || OptionsOpen || ProfileOpen || AccountOpen || LaunchOverlayOpen) return;
        if (QuickOpen && panel.Visibility == Visibility.Visible) { CloseQuick(); return; }
        if (MenuOpen) MenuLayer.Visibility = Visibility.Collapsed;

        NetPanel.Visibility = Visibility.Collapsed;
        VolPanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
        QuickLayer.Visibility = Visibility.Visible;
        _quickOpener = opener;
        Dispatcher.BeginInvoke(() => { firstFocus.Focus(); }, DispatcherPriority.Input);
    }

    private void CloseQuick()
    {
        QuickLayer.Visibility = Visibility.Collapsed;
        NetPanel.Visibility = Visibility.Collapsed;
        VolPanel.Visibility = Visibility.Collapsed;
        _quickOpener?.Focus();
    }

    private void OnQuickDimClick(object sender, MouseButtonEventArgs e) => CloseQuick();

    // ---- Network ----------------------------------------------------------------------------

    private static List<NetConnection> GetConnections()
    {
        var result = new List<NetConnection>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                var wired = ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                    or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx;
                if (!wifi && !wired) continue;

                var text = (ni.Name + " " + ni.Description).ToLowerInvariant();
                if (text.Contains("virtual") || text.Contains("vmware") || text.Contains("vethernet") || text.Contains("hyper-v")
                    || text.Contains("bluetooth") || text.Contains("pseudo") || text.Contains("tap-")) continue;

                var ip = ni.GetIPProperties().UnicastAddresses
                    .Select(a => a.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254"));
                if (ip is null) continue;
                result.Add(new NetConnection(wifi, ni.Name, ip.ToString()));
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException)
        {
            // keep whatever was found
        }
        return result;
    }

    private static string? ReadWifiName()
    {
        try
        {
            var info = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var process = Process.Start(info);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            var match = Regex.Match(output, @"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private void UpdateNetworkIcon()
    {
        var connections = GetConnections();
        NetIcon.Text = connections.Any(c => !c.Wifi) ? EthernetGlyph : connections.Count > 0 ? WifiGlyph : OfflineGlyph;
        NetIcon.Opacity = connections.Count > 0 ? 1.0 : 0.35;
    }

    private void OnTopNetworkClick(object sender, RoutedEventArgs e)
    {
        OpenQuick(NetButton, NetPanel, NetSettingsButton);
        if (NetPanel.Visibility == Visibility.Visible) FillNetworkList();
    }

    private async void FillNetworkList()
    {
        var connections = GetConnections();
        NetList.Children.Clear();
        if (connections.Count == 0)
        {
            NetList.Children.Add(new TextBlock
            {
                Text = "Sem conexão com a rede.", FontSize = 17, Foreground = (Brush)FindResource("TextMuted"), Margin = new Thickness(0, 0, 0, 4),
            });
            return;
        }

        TextBlock? wifiLine = null;
        foreach (var connection in connections)
        {
            var title = new TextBlock { Text = connection.Wifi ? "Wi-Fi" : "Ethernet", FontSize = 19, FontWeight = FontWeights.SemiBold };
            var detail = new TextBlock
            {
                Text = connection.Wifi ? "Conectado · " + connection.Address : "Cabo conectado · " + connection.Address,
                FontSize = 15, Foreground = (Brush)FindResource("TextMuted"),
            };
            if (connection.Wifi) wifiLine = detail;

            var texts = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(title);
            texts.Children.Add(detail);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(new TextBlock { Text = connection.Wifi ? WifiGlyph : EthernetGlyph, Style = (Style)FindResource("Icon"), FontSize = 26, Width = 34 });
            row.Children.Add(texts);
            NetList.Children.Add(row);
        }

        if (wifiLine is not null)
        {
            var name = await Task.Run(ReadWifiName);
            if (name is not null && NetList.IsVisible) wifiLine.Text = name + " · " + connections.First(c => c.Wifi).Address;
        }
    }

    private void OnNetSettingsClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:network") { UseShellExecute = true }); }
        catch (Win32Exception) { ShowToast("Não foi possível abrir as configurações de rede."); }
    }

    // ---- Volume -----------------------------------------------------------------------------

    private void OnTopVolumeClick(object sender, RoutedEventArgs e)
    {
        OpenQuick(VolButton, VolPanel, MuteButton);
        UpdateVolumeUi();
    }

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        if (!SystemVolume.TryGet(out _, out var muted) || !SystemVolume.SetMuted(!muted)) ShowToast("Não foi possível controlar o som.");
        UpdateVolumeUi();
    }

    private void OnVolDownClick(object sender, RoutedEventArgs e) => ChangeVolume(-5);

    private void OnVolUpClick(object sender, RoutedEventArgs e) => ChangeVolume(+5);

    private void ChangeVolume(int delta)
    {
        if (!SystemVolume.Change(delta)) ShowToast("Não foi possível controlar o som.");
        UpdateVolumeUi();
    }

    private void UpdateVolumeUi()
    {
        if (!SystemVolume.TryGet(out var percent, out var muted))
        {
            VolText.Text = "Indisponível";
            VolBar.Value = 0;
            MuteButton.Content = "Silenciar";
            VolIcon.Opacity = 0.35;
            return;
        }

        VolIcon.Opacity = 1.0;
        VolIcon.Text = muted || percent == 0 ? "" : "";
        VolText.Text = muted ? "Mudo" : percent + "%";
        VolBar.Value = muted ? 0 : percent;
        MuteButton.Content = muted ? "Ativar o som" : "Silenciar";
    }
}
