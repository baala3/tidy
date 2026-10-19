using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Tidy.App.Services;

/// <summary>
/// File icons the way File Explorer draws them, from <c>IShellItemImageFactory</c>.
/// Works for programs, .ico files and Store logos alike, with transparency intact.
/// </summary>
public static class ShellIcons
{
    /// <summary>The icon as an image to show, read off the UI thread. Call it on the UI thread.</summary>
    public static async Task<ImageSource?> ImageAsync(string path, int size)
    {
        var pixels = await Task.Run(() => Pixels(path, size));
        if (pixels is null) return null;
        var bitmap = new WriteableBitmap(size, size);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            await stream.WriteAsync(pixels);
        }
        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>Premultiplied BGRA pixels, top row first, or null when the shell has no icon.</summary>
    public static byte[]? Pixels(string path, int size)
    {
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory) != 0) return null;
        try
        {
            // Pictures (Store logos) as their content; programs and .ico files as their icon,
            // never a thumbnail of the file.
            var isPicture = Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
            var flags = SIIGBF_BIGGERSIZEOK | (isPicture ? SIIGBF_THUMBNAILONLY : SIIGBF_ICONONLY);
            if (factory.GetImage(new NativeSize { Width = size, Height = size }, flags, out var bitmap) != 0) return null;
            try
            {
                return ReadBitmap(bitmap, size);
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    private static byte[]? ReadBitmap(IntPtr bitmap, int size)
    {
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = size,
            Height = -size, // negative: top-down rows
            Planes = 1,
            BitCount = 32,
        };
        var pixels = new byte[size * size * 4];
        var screen = GetDC(IntPtr.Zero);
        try
        {
            var lines = GetDIBits(screen, bitmap, 0, (uint)size, pixels, ref header, 0);
            return lines == size ? pixels : null;
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private const int SIIGBF_BIGGERSIZEOK = 0x1;
    private const int SIIGBF_ICONONLY = 0x4;
    private const int SIIGBF_THUMBNAILONLY = 0x8;

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
