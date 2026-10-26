using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CodexTray;

internal static class LayeredWidgetSurface
{
    internal static unsafe void Present(IntPtr window, Bitmap surface)
    {
        // Supply a top-down DIB containing premultiplied BGRA. Windows composites
        // these pixels over the actual desktop rather than a color-key backdrop.
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) { throw new Win32Exception(); }
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = surface.Width,
                Height = -surface.Height,
                Planes = 1,
                BitCount = 32
            };
            bitmap = CreateDIBSection(dc, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) { throw new Win32Exception(); }
            previous = SelectObject(dc, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) { throw new Win32Exception(); }
            var data = surface.LockBits(new Rectangle(Point.Empty, surface.Size), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
            try
            {
                var rowBytes = checked(surface.Width * 4);
                for (var y = 0; y < surface.Height; y++)
                {
                    Buffer.MemoryCopy((byte*)data.Scan0 + y * data.Stride, (byte*)bits + y * rowBytes, rowBytes, rowBytes);
                }
            }
            finally { surface.UnlockBits(data); }
            var size = surface.Size;
            var source = Point.Empty;
            var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
            // A null destination point preserves the window's current position.
            if (!UpdateLayeredWindow(window, IntPtr.Zero, IntPtr.Zero, ref size, dc, ref source, 0, ref blend, 2))
            {
                throw new Win32Exception();
            }
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) { _ = SelectObject(dc, previous); }
            if (bitmap != IntPtr.Zero) { _ = DeleteObject(bitmap); }
            _ = DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, IntPtr destination,
        ref Size size, IntPtr sourceDc, ref Point source, uint colorKey, ref BlendFunction blend, uint flags);
}
