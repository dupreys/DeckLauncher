using System.Runtime.InteropServices;

namespace DeckLauncher.App;

/// <summary>Master volume and mute of the default Windows playback device (Core Audio).</summary>
internal static class SystemVolume
{
    public static bool TryGet(out int percent, out bool muted)
    {
        percent = 0;
        muted = false;
        try
        {
            var endpoint = Open();
            endpoint.GetMasterVolumeLevelScalar(out var level);
            endpoint.GetMute(out muted);
            percent = (int)Math.Round(level * 100);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            return false;
        }
    }

    public static bool SetMuted(bool muted)
    {
        try
        {
            var context = Guid.Empty;
            Open().SetMute(muted, ref context);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            return false;
        }
    }

    /// <summary>Moves the volume by <paramref name="delta"/> percent points (and un-mutes when raising it).</summary>
    public static bool Change(int delta)
    {
        try
        {
            var endpoint = Open();
            endpoint.GetMasterVolumeLevelScalar(out var level);
            var context = Guid.Empty;
            endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level + delta / 100f, 0f, 1f), ref context);
            if (delta > 0) endpoint.SetMute(false, ref context);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            return false;
        }
    }

    private static IAudioEndpointVolume Open()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var device);
        var iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var endpoint);
        return (IAudioEndpointVolume)endpoint;
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

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify);
        void UnregisterControlChangeNotify(IntPtr notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float levelDb, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float levelDb);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
