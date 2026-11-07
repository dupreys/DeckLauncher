using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DeckLauncher.App;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>Battery charge in percent and whether it is charging; null on a PC without a battery.</summary>
    public static (int Percent, bool Charging)? Battery()
    {
        if (!GetSystemPowerStatus(out var status)) return null;
        if ((status.BatteryFlag & 128) != 0 || status.BatteryFlag == 255 || status.BatteryLifePercent > 100) return null; // no battery / unknown
        return (status.BatteryLifePercent, status.ACLineStatus == 1 && (status.BatteryFlag & 8) != 0);
    }

    /// <summary>Puts the PC to sleep (not hibernate). Returns false when Windows refuses.</summary>
    public static bool Sleep() => SetSuspendState(false, false, false);

    /// <summary>Full paths of the programs running right now (those Windows lets us query; the rest are skipped).</summary>
    public static List<string> RunningImagePaths() => RunningProcesses().Select(p => p.Path).ToList();

    /// <summary>Id and full path of the programs running right now (those Windows lets us query).</summary>
    public static List<(int Pid, string Path)> RunningProcesses()
    {
        var result = new List<(int, string)>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var builder = new StringBuilder(1024);
                    var size = builder.Capacity;
                    if (QueryFullProcessImageName(handle, 0, builder, ref size)) result.Add((process.Id, builder.ToString(0, size)));
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // the program ended while we looked at it
            }
            finally
            {
                process.Dispose();
            }
        }
        return result;
    }

    // ---- Windows and focus ------------------------------------------------------------------------

    /// <summary>Id of the program that owns the window in front right now (0 when unknown).</summary>
    public static int ForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(window, out var pid);
        return (int)pid;
    }

    /// <summary>Minimizes a window (used as a last resort to uncover the launcher when Windows refuses to move it to the front).</summary>
    public static void Minimize(IntPtr window)
    {
        if (window != IntPtr.Zero) ShowWindow(window, 6 /* SW_MINIMIZE */);
    }

    /// <summary>The main window of the first of these programs that has one (IntPtr.Zero when none).</summary>
    public static IntPtr FindMainWindow(IEnumerable<int> processIds)
    {
        foreach (var id in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // it ended meanwhile
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Brings a window to the front and gives it the keyboard focus. Windows only allows that for the program the user
    /// last interacted with, so this borrows the focus of the current front window first (the usual workaround).
    /// </summary>
    public static void BringToFront(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        if (IsIconic(window)) ShowWindow(window, 9 /* SW_RESTORE */);

        var front = GetForegroundWindow();
        var frontThread = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, out _);
        var thisThread = GetCurrentThreadId();
        var attached = frontThread != 0 && frontThread != thisThread && AttachThreadInput(thisThread, frontThread, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached) AttachThreadInput(thisThread, frontThread, false);
        }

        if (GetForegroundWindow() != window)
        {
            keybd_event(0x12 /* Alt */, 0, 0, UIntPtr.Zero); // a key press makes Windows accept the switch
            keybd_event(0x12, 0, 2 /* key up */, UIntPtr.Zero);
            SetForegroundWindow(window);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint thread, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
                                               [MarshalAs(UnmanagedType.U1)] bool forceCritical,
                                               [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
}
