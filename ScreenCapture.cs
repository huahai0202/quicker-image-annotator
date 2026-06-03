using System;
using System.IO;
using System.Runtime.InteropServices;

internal static class ScreenCapture
{
    public static string CaptureVirtualScreenToTempFile()
    {
        ScreenCaptureSnapshot snapshot = CaptureVirtualScreen();
        return SavePixelsToTempFile(snapshot.Width, snapshot.Height, snapshot.Pixels);
    }

    public static string CaptureSelectedScreenToTempFile()
    {
        ScreenCaptureSnapshot snapshot = CaptureVirtualScreen();
        Win32Api.NativeRect region;
        if (!ScreenSelectionWindow.TrySelectRegion(snapshot, out region))
        {
            return null;
        }

        int width;
        int height;
        byte[] pixels = CopyRegionPixels(snapshot.Pixels, snapshot.Width, snapshot.Height, region, out width, out height);
        string path = SavePixelsToTempFile(width, height, pixels);
        ClipboardBridge.SetImage(IntPtr.Zero, path, width, height, pixels);
        return path;
    }

    public static ScreenCaptureSnapshot CaptureVirtualScreen()
    {
        int left = Win32Api.GetSystemMetrics(Win32Api.SmXVirtualScreen);
        int top = Win32Api.GetSystemMetrics(Win32Api.SmYVirtualScreen);
        int width = Math.Max(1, Win32Api.GetSystemMetrics(Win32Api.SmCxVirtualScreen));
        int height = Math.Max(1, Win32Api.GetSystemMetrics(Win32Api.SmCyVirtualScreen));
        byte[] pixels = CapturePixels(left, top, width, height);
        return new ScreenCaptureSnapshot(left, top, width, height, pixels);
    }

    public static string SaveRegionToTempFile(ScreenCaptureSnapshot snapshot, Win32Api.NativeRect region)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException("snapshot");
        }

        int width;
        int height;
        byte[] pixels = CopyRegionPixels(snapshot.Pixels, snapshot.Width, snapshot.Height, region, out width, out height);
        return SavePixelsToTempFile(width, height, pixels);
    }

    public static string CapturePrimaryScreenToTempFileForSelfTest(int maxWidth, int maxHeight)
    {
        int width = Math.Max(1, Math.Min(Math.Max(1, maxWidth), Win32Api.GetSystemMetrics(0)));
        int height = Math.Max(1, Math.Min(Math.Max(1, maxHeight), Win32Api.GetSystemMetrics(1)));
        string path = Path.Combine(Path.GetTempPath(), "quicker-capture-selftest-" + Guid.NewGuid().ToString("N") + ".png");
        CaptureToFile(0, 0, width, height, path);
        return path;
    }

    public static byte[] CopyRegionPixels(byte[] sourcePixels, int sourceWidth, int sourceHeight, Win32Api.NativeRect region, out int width, out int height)
    {
        if (sourcePixels == null)
        {
            throw new ArgumentNullException("sourcePixels");
        }
        if (sourceWidth <= 0 || sourceHeight <= 0 || sourcePixels.Length < sourceWidth * sourceHeight * 4)
        {
            throw new ArgumentException("Source pixels are invalid.");
        }

        int left = Math.Max(0, Math.Min(sourceWidth, Math.Min(region.left, region.right)));
        int top = Math.Max(0, Math.Min(sourceHeight, Math.Min(region.top, region.bottom)));
        int right = Math.Max(0, Math.Min(sourceWidth, Math.Max(region.left, region.right)));
        int bottom = Math.Max(0, Math.Min(sourceHeight, Math.Max(region.top, region.bottom)));
        width = Math.Max(1, right - left);
        height = Math.Max(1, bottom - top);

        byte[] target = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            Buffer.BlockCopy(sourcePixels, ((top + y) * sourceWidth + left) * 4, target, y * width * 4, width * 4);
        }
        return target;
    }

    public static void DrawPixels(IntPtr hdc, int x, int y, int width, int height, byte[] pixels)
    {
        if (hdc == IntPtr.Zero || pixels == null || width <= 0 || height <= 0)
        {
            return;
        }

        Win32Api.DibInfo info = CreateDibInfo(width, height, pixels.Length);
        Win32Api.SetDIBitsToDevice(hdc, x, y, (uint)width, (uint)height, 0, 0, 0, (uint)height, pixels, ref info, Win32Api.DibRgbColors);
    }

    private static void CaptureToFile(int left, int top, int width, int height, string path)
    {
        byte[] pixels = CapturePixels(left, top, width, height);
        WicCodec.SavePixels(path, width, height, pixels);
    }

    private static string SavePixelsToTempFile(int width, int height, byte[] pixels)
    {
        string path = Path.Combine(Path.GetTempPath(), "quicker-capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture) + ".png");
        WicCodec.SavePixels(path, width, height, pixels);
        return path;
    }

    private static byte[] CapturePixels(int left, int top, int width, int height)
    {
        IntPtr screenDc = IntPtr.Zero;
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr surface = IntPtr.Zero;
        IntPtr oldObject = IntPtr.Zero;
        try
        {
            screenDc = Win32Api.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("Cannot acquire screen device context.");
            }

            memoryDc = Win32Api.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("Cannot create capture device context.");
            }

            surface = Win32Api.CreateCompatibleSurface(screenDc, width, height);
            if (surface == IntPtr.Zero)
            {
                throw new InvalidOperationException("Cannot create capture surface.");
            }

            oldObject = Win32Api.SelectObject(memoryDc, surface);
            if (oldObject == IntPtr.Zero)
            {
                throw new InvalidOperationException("Cannot bind capture surface.");
            }

            if (!Win32Api.BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, Win32Api.RasterCopy | Win32Api.RasterCaptureLayered))
            {
                throw new InvalidOperationException("Screen capture copy failed.");
            }

            byte[] pixels = new byte[checked(width * height * 4)];
            Win32Api.DibInfo info = CreateDibInfo(width, height, pixels.Length);

            int lines = Win32Api.GetDIBits(memoryDc, surface, 0, (uint)height, pixels, ref info, Win32Api.DibRgbColors);
            if (lines != height)
            {
                throw new InvalidOperationException("Screen capture readback failed.");
            }

            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
            return pixels;
        }
        finally
        {
            if (oldObject != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                Win32Api.SelectObject(memoryDc, oldObject);
            }
            if (surface != IntPtr.Zero)
            {
                Win32Api.DeleteObject(surface);
            }
            if (memoryDc != IntPtr.Zero)
            {
                Win32Api.DeleteDC(memoryDc);
            }
            if (screenDc != IntPtr.Zero)
            {
                Win32Api.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    private static Win32Api.DibInfo CreateDibInfo(int width, int height, int length)
    {
        Win32Api.DibInfo info = new Win32Api.DibInfo();
        info.header.size = (uint)Marshal.SizeOf(typeof(Win32Api.DibHeader));
        info.header.width = width;
        info.header.height = -height;
        info.header.planes = 1;
        info.header.bitCount = 32;
        info.header.compression = Win32Api.DibRgb;
        info.header.imageSize = (uint)length;
        return info;
    }
}

internal sealed class ScreenCaptureSnapshot
{
    public readonly int Left;
    public readonly int Top;
    public readonly int Width;
    public readonly int Height;
    public readonly byte[] Pixels;

    public ScreenCaptureSnapshot(int left, int top, int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Capture dimensions must be positive.");
        }
        if (pixels == null || pixels.Length < width * height * 4)
        {
            throw new ArgumentException("Capture pixels are invalid.");
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Pixels = pixels;
    }
}
