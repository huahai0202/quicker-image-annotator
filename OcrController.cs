using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class OcrController
{
    public static OcrRequest CreateRequest(WicImageDocument image, IList<AnnotationItem> items, int selectedIndex, AppSettings settings, int version)
    {
        if (image == null)
        {
            throw new ArgumentNullException("image");
        }
        if (settings == null)
        {
            throw new ArgumentNullException("settings");
        }

        string sourceScope;
        byte[] pngBytes = CreatePngBytes(image, items, selectedIndex, out sourceScope);
        OcrRequest request = new OcrRequest();
        request.PngBytes = pngBytes;
        request.Settings = settings;
        request.SourceScope = sourceScope;
        request.Version = version;
        return request;
    }

    public static void BeginRecognize(IntPtr targetWindow, int message, OcrRequest request)
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            OcrAsyncResult async = new OcrAsyncResult();
            async.Version = request == null ? 0 : request.Version;
            async.Settings = request == null ? null : request.Settings;
            try
            {
                if (request == null)
                {
                    throw new ArgumentNullException("request");
                }
                async.Result = BaiduOcrClient.Recognize(request.PngBytes, request.Settings, request.SourceScope);
            }
            catch (Exception ex)
            {
                async.Error = ex;
            }
            PostResult(targetWindow, message, async);
        });
    }

    public static OcrAsyncResult TakeResult(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return null;
        }
        GCHandle handle = GCHandle.FromIntPtr(pointer);
        try
        {
            return handle.Target as OcrAsyncResult;
        }
        finally
        {
            handle.Free();
        }
    }

    internal static bool TryGetSelectedRegionForSelfTest(WicImageDocument image, IList<AnnotationItem> items, int selectedIndex, out Win32Api.NativeRect region)
    {
        return TryGetSelectedOcrRegion(image, items, selectedIndex, out region);
    }

    private static void PostResult(IntPtr targetWindow, int message, OcrAsyncResult async)
    {
        GCHandle handle = GCHandle.Alloc(async);
        IntPtr pointer = GCHandle.ToIntPtr(handle);
        if (targetWindow == IntPtr.Zero || !Win32Api.PostMessage(targetWindow, message, pointer, IntPtr.Zero))
        {
            handle.Free();
        }
    }

    private static byte[] CreatePngBytes(WicImageDocument image, IList<AnnotationItem> items, int selectedIndex, out string sourceScope)
    {
        int width = image.Width;
        int height = image.Height;
        byte[] pixels = image.Pixels;
        sourceScope = "\u6574\u5f20\u56fe";

        Win32Api.NativeRect region;
        if (TryGetSelectedOcrRegion(image, items, selectedIndex, out region))
        {
            pixels = ScreenCapture.CopyRegionPixels(image.Pixels, image.Width, image.Height, region, out width, out height);
            sourceScope = "\u9009\u533a";
        }

        string path = Path.Combine(Path.GetTempPath(), "quicker-ocr-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            WicCodec.SavePixels(path, width, height, pixels);
            return File.ReadAllBytes(path);
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    private static bool TryGetSelectedOcrRegion(WicImageDocument image, IList<AnnotationItem> items, int selectedIndex, out Win32Api.NativeRect region)
    {
        region = new Win32Api.NativeRect();
        if (image == null || items == null || selectedIndex < 0 || selectedIndex >= items.Count)
        {
            return false;
        }

        GpuRect bounds = GpuRenderer.GetItemBounds(items[selectedIndex]);
        if (bounds.IsEmpty)
        {
            return false;
        }

        int left = Math.Max(0, (int)Math.Floor(bounds.Left));
        int top = Math.Max(0, (int)Math.Floor(bounds.Top));
        int right = Math.Min(image.Width, (int)Math.Ceiling(bounds.Right));
        int bottom = Math.Min(image.Height, (int)Math.Ceiling(bounds.Bottom));
        if (right - left < 4 || bottom - top < 4)
        {
            return false;
        }

        region.left = left;
        region.top = top;
        region.right = right;
        region.bottom = bottom;
        return true;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    internal sealed class OcrRequest
    {
        public byte[] PngBytes;
        public AppSettings Settings;
        public string SourceScope;
        public int Version;
    }

    internal sealed class OcrAsyncResult
    {
        public int Version;
        public OcrResult Result;
        public AppSettings Settings;
        public Exception Error;
    }
}
