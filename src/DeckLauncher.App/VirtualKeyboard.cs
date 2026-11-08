using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace DeckLauncher.App;

/// <summary>
/// Shows and hides the Windows text input panel (touch keyboard, TabTip), which works with a controller on Windows 11.
/// Neither takes keyboard focus away from the launcher's text box, so typed characters land where expected.
/// </summary>
internal static class VirtualKeyboard
{
    private const string TouchKeyboardClass = "IPTip_Main_Window";
    private const string ClassicKeyboardClass = "OSKMainClass";
    private const uint WmSysCommand = 0x0112;
    private static readonly IntPtr ScClose = new(0xF060);

    private static int _generation; // lets a late "show" notice that a "hide" happened in between
    private static bool _requested;

    /// <summary>True between a Show and the next Hide requested by the launcher.</summary>
    public static bool IsVisible => _requested;

    private static string TabTipPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "microsoft shared", "ink", "TabTip.exe");

    public static async Task ShowAsync()
    {
        if (_requested) return;
        _requested = true;
        var generation = ++_generation;

        try
        {
            // Only the Windows text input panel (touch keyboard) is used. The classic on-screen keyboard (osk.exe) is
            // never started: the panel's window is not always recognised, and a fallback would open both keyboards.
            if (XboxMode.Active) AllowAutoInvoke();
            if (File.Exists(TabTipPath))
            {
                Process.Start(new ProcessStartInfo(TabTipPath) { UseShellExecute = true });
                await Task.Delay(450);
                if (generation != _generation) { Close(); return; } // closed while it was still starting
                // In the Xbox full-screen experience the panel can start without showing itself: ask it to appear.
                if (!IsPanelVisible()) Toggle();
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // No keyboard available on this PC: the physical keyboard still works.
        }
    }

    /// <summary>Lets the touch keyboard open on a PC that has a keyboard attached (the Xbox full-screen mode is used with a controller).</summary>
    private static void AllowAutoInvoke()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\TabletTip\1.7");
            key.SetValue("TipbandDesiredVisibility", 1, RegistryValueKind.DWord);
            key.SetValue("EnableDesktopModeAutoInvoke", 1, RegistryValueKind.DWord);
            key.SetValue("TouchKeyboardTapInvoke", 2, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Not allowed to write: the panel still opens by itself in most cases.
        }
    }

    private static bool IsPanelVisible()
    {
        var window = FindWindow(TouchKeyboardClass, null);
        return window != IntPtr.Zero && IsWindowVisible(window);
    }

    private static void Toggle()
    {
        try
        {
            var invocation = (ITipInvocation)new UIHostNoLaunch();
            invocation.Toggle(GetDesktopWindow());
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // The panel is not available here.
        }
    }

    public static void Hide()
    {
        _generation++;
        _requested = false;
        Close();
    }

    private static void Close()
    {
        PostClose(TouchKeyboardClass);
        PostClose(ClassicKeyboardClass);
    }

    private static void PostClose(string windowClass)
    {
        var window = FindWindow(windowClass, null);
        if (window != IntPtr.Zero) PostMessage(window, WmSysCommand, ScClose, IntPtr.Zero);
    }

    [ComImport, Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")]
    private class UIHostNoLaunch { }

    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr window);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
