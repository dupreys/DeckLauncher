using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DeckLauncher.App;

/// <summary>Bluetooth headsets through the classic Win32 API (paired devices, connect, disconnect) and the radio switch through the Windows runtime.</summary>
internal static class BluetoothNative
{
    public sealed record Device(string Name, ulong Address, bool Connected, bool IsAudio);

    private static readonly Guid[] AudioServices =
    {
        new("0000110B-0000-1000-8000-00805F9B34FB"), // A2DP sink: stereo sound
        new("0000111E-0000-1000-8000-00805F9B34FB"), // Hands-free: microphone
        new("00001108-0000-1000-8000-00805F9B34FB"), // Headset
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct FindRadioParams { public int Size; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SearchParams
    {
        public int Size;
        public int ReturnAuthenticated;
        public int ReturnRemembered;
        public int ReturnUnknown;
        public int ReturnConnected;
        public int IssueInquiry;
        public byte TimeoutMultiplier;
        public IntPtr Radio;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime { public ushort A, B, C, D, E, F, G, H; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceInfo
    {
        public int Size;
        public ulong Address;
        public uint ClassOfDevice;
        public int Connected;
        public int Remembered;
        public int Authenticated;
        public SystemTime LastSeen;
        public SystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref FindRadioParams p, out IntPtr radio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindRadioClose(IntPtr find);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref SearchParams p, ref DeviceInfo info);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindNextDevice(IntPtr find, ref DeviceInfo info);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindDeviceClose(IntPtr find);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern int BluetoothGetDeviceInfo(IntPtr radio, ref DeviceInfo info);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern int BluetoothSetServiceState(IntPtr radio, ref DeviceInfo info, ref Guid service, int flags);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static IntPtr OpenRadio()
    {
        try
        {
            var p = new FindRadioParams { Size = Marshal.SizeOf<FindRadioParams>() };
            var find = BluetoothFindFirstRadio(ref p, out var radio);
            if (find == IntPtr.Zero) return IntPtr.Zero;
            BluetoothFindRadioClose(find);
            return radio;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or Win32Exception) { return IntPtr.Zero; }
    }

    private static DeviceInfo NewInfo() => new() { Size = Marshal.SizeOf<DeviceInfo>(), Name = "" };

    private static Device ToDevice(DeviceInfo i) => new(i.Name ?? "", i.Address, i.Connected != 0, ((i.ClassOfDevice >> 8) & 0x1F) == 4);

    /// <summary>Devices Windows already knows (paired), without scanning the air.</summary>
    public static List<Device> PairedDevices()
    {
        var list = new List<Device>();
        var radio = OpenRadio();
        try
        {
            var search = new SearchParams
            {
                Size = Marshal.SizeOf<SearchParams>(), ReturnAuthenticated = 1, ReturnRemembered = 1, ReturnConnected = 1,
                ReturnUnknown = 0, IssueInquiry = 0, TimeoutMultiplier = 0, Radio = radio,
            };
            var info = NewInfo();
            var find = BluetoothFindFirstDevice(ref search, ref info);
            if (find == IntPtr.Zero) return list;
            do
            {
                list.Add(ToDevice(info));
                info = NewInfo();
            }
            while (BluetoothFindNextDevice(find, ref info));
            BluetoothFindDeviceClose(find);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or Win32Exception) { }
        finally { if (radio != IntPtr.Zero) CloseHandle(radio); }
        return list;
    }

    public static bool IsConnected(ulong address)
    {
        try
        {
            var info = NewInfo();
            info.Address = address;
            return BluetoothGetDeviceInfo(IntPtr.Zero, ref info) == 0 && info.Connected != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or Win32Exception) { return false; }
    }

    /// <summary>Asks Windows to connect the headset's audio services and waits a few seconds for it to answer.</summary>
    public static bool Connect(ulong address)
    {
        if (IsConnected(address)) return true;
        var radio = OpenRadio();
        try
        {
            var info = NewInfo();
            info.Address = address;
            if (BluetoothGetDeviceInfo(radio, ref info) != 0) return false;
            foreach (var service in AudioServices)
            {
                var guid = service;
                BluetoothSetServiceState(radio, ref info, ref guid, 0);
                BluetoothSetServiceState(radio, ref info, ref guid, 1);
            }
            for (var i = 0; i < 12; i++)
            {
                if (IsConnected(address)) return true;
                Thread.Sleep(500);
            }
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or Win32Exception) { return false; }
        finally { if (radio != IntPtr.Zero) CloseHandle(radio); }
    }

    public static bool Disconnect(ulong address)
    {
        var radio = OpenRadio();
        try
        {
            var info = NewInfo();
            info.Address = address;
            if (BluetoothGetDeviceInfo(radio, ref info) != 0) return false;
            foreach (var service in AudioServices)
            {
                var guid = service;
                BluetoothSetServiceState(radio, ref info, ref guid, 0);
            }
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or Win32Exception) { return false; }
        finally { if (radio != IntPtr.Zero) CloseHandle(radio); }
    }

    // ---- Radio on/off (Windows runtime through PowerShell: no change to the project's target framework) --------

    private const string RadioScript = @"
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($op, $type) { $t = $asTask.MakeGenericMethod($type).Invoke($null, @($op)); $t.Wait(-1) | Out-Null; $t.Result }
[Windows.Devices.Radios.Radio,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null
[Windows.Devices.Radios.RadioAccessStatus,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null
[Windows.Devices.Radios.RadioState,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null
Await ([Windows.Devices.Radios.Radio]::RequestAccessAsync()) ([Windows.Devices.Radios.RadioAccessStatus]) | Out-Null
$radios = Await ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]])
$bt = $radios | Where-Object { $_.Kind -eq 'Bluetooth' } | Select-Object -First 1
if ($null -eq $bt) { 'None'; exit 0 }
if ('@ACTION@' -eq 'On' -or '@ACTION@' -eq 'Off') { Await ($bt.SetStateAsync('@ACTION@')) ([Windows.Devices.Radios.RadioAccessStatus]) | Out-Null }
$bt.State.ToString()
";

    /// <summary>Returns "On", "Off", "None" (no Bluetooth on this PC) or null when it could not be read.</summary>
    public static string? RadioState(string action = "Get")
    {
        try
        {
            var script = RadioScript.Replace("@ACTION@", action);
            var start = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(20000)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            return line is "On" or "Off" or "None" or "Disabled" ? (line == "Disabled" ? "Off" : line) : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException) { return null; }
    }
}
