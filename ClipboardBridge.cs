using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class ClipboardBridge
{
    private static readonly uint PngFormat = Win32Api.RegisterClipboardFormat("PNG");
    private static readonly string ClipboardTempFileName = "quicker-annotate-clipboard-" + Process.GetCurrentProcess().Id + ".png";
    private static readonly object ClipboardTempSync = new object();
    private static string clipboardTempPath;
    private static int clipboardTempLength = -1;
    private static int clipboardTempWidth = -1;
    private static int clipboardTempHeight = -1;
    private static uint clipboardTempHash;

    public static void SetImage(IntPtr owner, string path, int width, int height, byte[] pixels)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        IntPtr dropMemory = IntPtr.Zero;
        IntPtr dibMemory = IntPtr.Zero;
        IntPtr pngMemory = IntPtr.Zero;
        try
        {
            byte[] pngBytes = PngFormat == 0 ? null : File.ReadAllBytes(path);
            dropMemory = CreateFileDropMemory(path);
            dibMemory = CreateDibMemory(width, height, pixels);
            pngMemory = pngBytes == null ? IntPtr.Zero : CreateBytesMemory(pngBytes);
            if (!Win32Api.OpenClipboard(owner))
            {
                return;
            }

            try
            {
                Win32Api.EmptyClipboard();
                if (dropMemory != IntPtr.Zero && Win32Api.SetClipboardData(Win32Api.CfHdrop, dropMemory) != IntPtr.Zero)
                {
                    dropMemory = IntPtr.Zero;
                }
                if (dibMemory != IntPtr.Zero && Win32Api.SetClipboardData(Win32Api.CfDib, dibMemory) != IntPtr.Zero)
                {
                    dibMemory = IntPtr.Zero;
                }
                if (PngFormat != 0 && pngMemory != IntPtr.Zero && Win32Api.SetClipboardData(PngFormat, pngMemory) != IntPtr.Zero)
                {
                    pngMemory = IntPtr.Zero;
                }
            }
            finally
            {
                Win32Api.CloseClipboard();
            }
        }
        finally
        {
            FreeGlobal(dropMemory);
            FreeGlobal(dibMemory);
            FreeGlobal(pngMemory);
        }
    }

    public static bool SetUnicodeText(IntPtr owner, string text)
    {
        text = text ?? string.Empty;
        byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
        IntPtr memory = CreateBytesMemory(bytes);
        if (memory == IntPtr.Zero)
        {
            return false;
        }

        if (!Win32Api.OpenClipboard(owner))
        {
            FreeGlobal(memory);
            return false;
        }

        try
        {
            Win32Api.EmptyClipboard();
            if (Win32Api.SetClipboardData(Win32Api.CfUnicodeText, memory) == IntPtr.Zero)
            {
                return false;
            }
            memory = IntPtr.Zero;
            return true;
        }
        finally
        {
            Win32Api.CloseClipboard();
            FreeGlobal(memory);
        }
    }

    public static string GetUnicodeText(IntPtr owner)
    {
        if (!Win32Api.IsClipboardFormatAvailable(Win32Api.CfUnicodeText) || !Win32Api.OpenClipboard(owner))
        {
            return string.Empty;
        }

        IntPtr memory = IntPtr.Zero;
        IntPtr locked = IntPtr.Zero;
        try
        {
            memory = Win32Api.GetClipboardData(Win32Api.CfUnicodeText);
            if (memory == IntPtr.Zero)
            {
                return string.Empty;
            }
            locked = Win32Api.GlobalLock(memory);
            if (locked == IntPtr.Zero)
            {
                return string.Empty;
            }
            return Marshal.PtrToStringUni(locked) ?? string.Empty;
        }
        finally
        {
            if (locked != IntPtr.Zero)
            {
                Win32Api.GlobalUnlock(memory);
            }
            Win32Api.CloseClipboard();
        }
    }

    public static string TryGetClipboardImagePath()
    {
        string file = TryGetFileDrop();
        if (!string.IsNullOrEmpty(file))
        {
            return file;
        }

        string png = TrySavePngFormat();
        if (!string.IsNullOrEmpty(png))
        {
            return png;
        }

        return TrySaveDibFormat();
    }

    public static void SetPngBytesForSelfTest(byte[] bytes)
    {
        IntPtr memory = CreateBytesMemory(bytes);
        if (memory == IntPtr.Zero)
        {
            throw new InvalidOperationException("Cannot allocate PNG clipboard memory.");
        }
        if (!Win32Api.OpenClipboard(IntPtr.Zero))
        {
            FreeGlobal(memory);
            throw new InvalidOperationException("Cannot open clipboard for PNG self test.");
        }
        try
        {
            Win32Api.EmptyClipboard();
            if (Win32Api.SetClipboardData(PngFormat, memory) == IntPtr.Zero)
            {
                throw new InvalidOperationException("Cannot set PNG clipboard data.");
            }
            memory = IntPtr.Zero;
        }
        finally
        {
            Win32Api.CloseClipboard();
            FreeGlobal(memory);
        }
    }

    public static void SetDibForSelfTest(int width, int height, byte[] pixels)
    {
        IntPtr memory = CreateDibMemory(width, height, pixels);
        if (memory == IntPtr.Zero)
        {
            throw new InvalidOperationException("Cannot allocate DIB clipboard memory.");
        }
        if (!Win32Api.OpenClipboard(IntPtr.Zero))
        {
            FreeGlobal(memory);
            throw new InvalidOperationException("Cannot open clipboard for DIB self test.");
        }
        try
        {
            Win32Api.EmptyClipboard();
            if (Win32Api.SetClipboardData(Win32Api.CfDib, memory) == IntPtr.Zero)
            {
                throw new InvalidOperationException("Cannot set DIB clipboard data.");
            }
            memory = IntPtr.Zero;
        }
        finally
        {
            Win32Api.CloseClipboard();
            FreeGlobal(memory);
        }
    }

    private static string TryGetFileDrop()
    {
        if (!Win32Api.IsClipboardFormatAvailable(Win32Api.CfHdrop))
        {
            return null;
        }
        if (!Win32Api.OpenClipboard(IntPtr.Zero))
        {
            return null;
        }
        try
        {
            IntPtr drop = Win32Api.GetClipboardData(Win32Api.CfHdrop);
            if (drop == IntPtr.Zero)
            {
                return null;
            }
            uint count = Win32Api.DragQueryFile(drop, 0xFFFFFFFF, IntPtr.Zero, 0);
            for (uint i = 0; i < count; i++)
            {
                uint length = Win32Api.DragQueryFile(drop, i, IntPtr.Zero, 0);
                IntPtr buffer = Marshal.AllocHGlobal((int)((length + 1) * 2));
                try
                {
                    Win32Api.DragQueryFile(drop, i, buffer, length + 1);
                    string path = Marshal.PtrToStringUni(buffer);
                    if (IsSupportedImagePath(path))
                    {
                        return path;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        finally
        {
            Win32Api.CloseClipboard();
        }
        return null;
    }

    private static string TrySavePngFormat()
    {
        if (PngFormat == 0 || !Win32Api.IsClipboardFormatAvailable(PngFormat))
        {
            return null;
        }
        if (!Win32Api.OpenClipboard(IntPtr.Zero))
        {
            return null;
        }
        try
        {
            IntPtr handle = Win32Api.GetClipboardData(PngFormat);
            byte[] bytes = CopyGlobalBytes(handle);
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }
            return WriteClipboardBytesIfChanged(bytes);
        }
        finally
        {
            Win32Api.CloseClipboard();
        }
    }

    private static string TrySaveDibFormat()
    {
        uint format = Win32Api.IsClipboardFormatAvailable(Win32Api.CfDibV5) ? Win32Api.CfDibV5 :
            (Win32Api.IsClipboardFormatAvailable(Win32Api.CfDib) ? Win32Api.CfDib : 0);
        if (format == 0 || !Win32Api.OpenClipboard(IntPtr.Zero))
        {
            return null;
        }
        try
        {
            IntPtr handle = Win32Api.GetClipboardData(format);
            byte[] dib = CopyGlobalBytes(handle);
            if (dib == null || dib.Length < 40)
            {
                return null;
            }
            int headerBytes = BitConverter.ToInt32(dib, 0);
            int width = BitConverter.ToInt32(dib, 4);
            int signedHeight = BitConverter.ToInt32(dib, 8);
            short planes = BitConverter.ToInt16(dib, 12);
            short bits = BitConverter.ToInt16(dib, 14);
            int compression = BitConverter.ToInt32(dib, 16);
            if (width <= 0 || signedHeight == 0 || planes != 1 || (bits != 32 && bits != 24) || headerBytes <= 0 || headerBytes >= dib.Length)
            {
                return null;
            }
            int height = Math.Abs(signedHeight);
            int stride = ((width * bits + 31) / 32) * 4;
            int pixelOffset = headerBytes;
            if (compression == 3 && format == Win32Api.CfDib && bits == 32)
            {
                pixelOffset += 12;
            }
            if (pixelOffset + stride * height > dib.Length)
            {
                return null;
            }

            byte[] pixels = new byte[width * height * 4];
            bool bottomUp = signedHeight > 0;
            for (int y = 0; y < height; y++)
            {
                int srcY = bottomUp ? height - 1 - y : y;
                int src = pixelOffset + srcY * stride;
                int dst = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    pixels[dst + 0] = dib[src + 0];
                    pixels[dst + 1] = dib[src + 1];
                    pixels[dst + 2] = dib[src + 2];
                    pixels[dst + 3] = bits == 32 ? (dib[src + 3] == 0 ? (byte)255 : dib[src + 3]) : (byte)255;
                    src += bits / 8;
                    dst += 4;
                }
            }

            string path = GetClipboardTempPath();
            uint hash = HashBytes(pixels);
            lock (ClipboardTempSync)
            {
                if (File.Exists(path) &&
                    clipboardTempLength == pixels.Length &&
                    clipboardTempWidth == width &&
                    clipboardTempHeight == height &&
                    clipboardTempHash == hash)
                {
                    return path;
                }
            }
            string temp = path + ".tmp";
            WicCodec.SavePixels(temp, width, height, pixels);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temp, path);
            lock (ClipboardTempSync)
            {
                clipboardTempPath = path;
                clipboardTempLength = pixels.Length;
                clipboardTempWidth = width;
                clipboardTempHeight = height;
                clipboardTempHash = hash;
            }
            return path;
        }
        finally
        {
            Win32Api.CloseClipboard();
        }
    }

    private static string WriteClipboardBytesIfChanged(byte[] bytes)
    {
        string path = GetClipboardTempPath();
        uint hash = HashBytes(bytes);
        lock (ClipboardTempSync)
        {
            if (File.Exists(path) && clipboardTempLength == bytes.Length && clipboardTempHash == hash)
            {
                return path;
            }

            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temp, path);
            clipboardTempPath = path;
            clipboardTempLength = bytes.Length;
            clipboardTempWidth = -1;
            clipboardTempHeight = -1;
            clipboardTempHash = hash;
            return path;
        }
    }

    private static string GetClipboardTempPath()
    {
        lock (ClipboardTempSync)
        {
            if (string.IsNullOrEmpty(clipboardTempPath))
            {
                clipboardTempPath = Path.Combine(Path.GetTempPath(), ClipboardTempFileName);
            }
            return clipboardTempPath;
        }
    }

    private static uint HashBytes(byte[] bytes)
    {
        unchecked
        {
            uint hash = 2166136261u;
            if (bytes != null)
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= 16777619u;
                }
            }
            return hash;
        }
    }

    private static IntPtr CreateFileDropMemory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        byte[] chars = System.Text.Encoding.Unicode.GetBytes(fullPath + "\0\0");
        int header = Marshal.SizeOf(typeof(Win32Api.DropFiles));
        IntPtr memory = Win32Api.GlobalAlloc(Win32Api.GmemMoveable | Win32Api.GmemZeroinit, new UIntPtr((uint)(header + chars.Length)));
        if (memory == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        IntPtr ptr = Win32Api.GlobalLock(memory);
        if (ptr == IntPtr.Zero)
        {
            FreeGlobal(memory);
            return IntPtr.Zero;
        }
        try
        {
            Win32Api.DropFiles drop = new Win32Api.DropFiles();
            drop.pFiles = (uint)header;
            drop.fWide = 1;
            Marshal.StructureToPtr(drop, ptr, false);
            Marshal.Copy(chars, 0, new IntPtr(ptr.ToInt64() + header), chars.Length);
            return memory;
        }
        finally
        {
            Win32Api.GlobalUnlock(memory);
        }
    }

    private static IntPtr CreateDibMemory(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0 || pixels == null || pixels.Length < width * height * 4)
        {
            return IntPtr.Zero;
        }
        const int header = 40;
        int bytes = header + pixels.Length;
        IntPtr memory = Win32Api.GlobalAlloc(Win32Api.GmemMoveable | Win32Api.GmemZeroinit, new UIntPtr((uint)bytes));
        if (memory == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        IntPtr ptr = Win32Api.GlobalLock(memory);
        if (ptr == IntPtr.Zero)
        {
            FreeGlobal(memory);
            return IntPtr.Zero;
        }
        try
        {
            Marshal.WriteInt32(ptr, 0, header);
            Marshal.WriteInt32(ptr, 4, width);
            Marshal.WriteInt32(ptr, 8, -height);
            Marshal.WriteInt16(ptr, 12, 1);
            Marshal.WriteInt16(ptr, 14, 32);
            Marshal.WriteInt32(ptr, 16, 0);
            Marshal.WriteInt32(ptr, 20, pixels.Length);
            Marshal.Copy(pixels, 0, new IntPtr(ptr.ToInt64() + header), pixels.Length);
            return memory;
        }
        finally
        {
            Win32Api.GlobalUnlock(memory);
        }
    }

    private static IntPtr CreateBytesMemory(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return IntPtr.Zero;
        }
        IntPtr memory = Win32Api.GlobalAlloc(Win32Api.GmemMoveable | Win32Api.GmemZeroinit, new UIntPtr((uint)bytes.Length));
        if (memory == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        IntPtr ptr = Win32Api.GlobalLock(memory);
        if (ptr == IntPtr.Zero)
        {
            FreeGlobal(memory);
            return IntPtr.Zero;
        }
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return memory;
        }
        finally
        {
            Win32Api.GlobalUnlock(memory);
        }
    }

    private static byte[] CopyGlobalBytes(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }
        UIntPtr sizePtr = Win32Api.GlobalSize(handle);
        long size = (long)sizePtr.ToUInt64();
        if (size <= 0 || size > int.MaxValue)
        {
            return null;
        }
        IntPtr ptr = Win32Api.GlobalLock(handle);
        if (ptr == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            byte[] bytes = new byte[(int)size];
            Marshal.Copy(ptr, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Win32Api.GlobalUnlock(handle);
        }
    }

    private static void FreeGlobal(IntPtr memory)
    {
        if (memory != IntPtr.Zero)
        {
            Win32Api.GlobalFree(memory);
        }
    }

    private static bool IsSupportedImagePath(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" ||
            ext == ".gif" || ext == ".tif" || ext == ".tiff";
    }
}
