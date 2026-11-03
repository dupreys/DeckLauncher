using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DeckLauncher.App;

/// <summary>Asks Windows for the thumbnail Explorer shows for a file (works for videos recorded by the Game Bar).</summary>
internal static class ShellThumbnail
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Size size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Width, Height;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr context, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    private const int BiggerSizeOk = 0x1;
    private const int IconOnly = 0x4;
    private const int ThumbnailOnly = 0x8;

    /// <summary>A frozen thumbnail, or null when Windows has none (the caller then shows a placeholder).</summary>
    public static BitmapSource? Get(string path, int width, int height, bool iconOnly = false)
    {
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            var hr = factory.GetImage(new Size { Width = width, Height = height }, iconOnly ? BiggerSizeOk | IconOnly : BiggerSizeOk | ThumbnailOnly, out var handle);
            if (hr != 0 || handle == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(handle);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
