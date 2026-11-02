using System.Runtime.InteropServices;

namespace DeckLauncher.App;

/// <summary>Mutes or un-mutes the sound of particular programs (Windows' per-app volume), leaving everything else alone.</summary>
internal static class GameAudio
{
    /// <summary>Mutes every audio session that belongs to one of <paramref name="processIds"/>. Best effort: any COM problem is ignored.</summary>
    public static void SetMuted(IReadOnlyCollection<int> processIds, bool muted)
    {
        if (processIds.Count == 0) return;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var device);
            var iid = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var managerObject);
            var manager = (IAudioSessionManager2)managerObject;
            manager.GetSessionEnumerator(out var sessions);
            sessions.GetCount(out var count);

            var context = Guid.Empty;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    sessions.GetSession(i, out var control);
                    control.GetProcessId(out var pid);
                    if (!processIds.Contains((int)pid)) continue;
                    ((ISimpleAudioVolume)control).SetMute(muted, ref context);
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException)
                {
                    // one session may disappear while we look at it
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            // no audio device: nothing to mute
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        // IAudioSessionManager
        void GetAudioSessionControl(IntPtr groupingParam, uint streamFlags, out IntPtr sessionControl);
        void GetSimpleAudioVolume(IntPtr groupingParam, uint streamFlags, out IntPtr audioVolume);
        // IAudioSessionManager2
        void GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        void GetGroupingParam(out Guid groupingParam);
        void SetGroupingParam(ref Guid groupingParam, ref Guid context);
        void RegisterAudioSessionNotification(IntPtr notifications);
        void UnregisterAudioSessionNotification(IntPtr notifications);
        // IAudioSessionControl2
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        void SetMasterVolume(float level, ref Guid context);
        void GetMasterVolume(out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
