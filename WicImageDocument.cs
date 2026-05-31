using System;
using System.IO;
using System.Runtime.InteropServices;

internal sealed class WicImageDocument : IDisposable
{
    public readonly int Width;
    public readonly int Height;
    public readonly byte[] Pixels;
    private bool disposed;

    public WicImageDocument(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Image dimensions must be positive.");
        }
        if (pixels == null || pixels.Length < width * height * 4)
        {
            throw new ArgumentException("Pixel buffer is too small.");
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public static WicImageDocument Load(string path)
    {
        return WicCodec.Load(path);
    }

    public static WicImageDocument CreateSynthetic(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i + 0] = (byte)(80 + (x * 90 / Math.Max(1, width)));
                pixels[i + 1] = (byte)(110 + (y * 80 / Math.Max(1, height)));
                pixels[i + 2] = (byte)(180 - (x * 50 / Math.Max(1, width)));
                pixels[i + 3] = 255;
            }
        }
        return new WicImageDocument(width, height, pixels);
    }

    public void Save(string path)
    {
        WicCodec.SavePixels(path, Width, Height, Pixels);
    }

    public void Dispose()
    {
        disposed = true;
    }

    public void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException("WicImageDocument");
        }
    }
}

internal sealed class WicPixelStore : IDisposable
{
    public readonly IntPtr Store;
    public readonly int Width;
    public readonly int Height;
    private bool disposed;

    public WicPixelStore(IntPtr store, int width, int height)
    {
        Store = store;
        Width = width;
        Height = height;
    }

    public byte[] CopyPixels()
    {
        byte[] pixels = new byte[Width * Height * 4];
        WicCodec.CopyPixels(Store, Width, Height, pixels);
        return pixels;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        IntPtr store = Store;
        ComUtil.Release(ref store);
    }
}

internal static class WicCodec
{
    private const uint ClsctxInprocServer = 1;
    private const int DecodeCacheOnLoad = 1;
    private const int EncoderNoCache = 2;
    private const int CacheOnLoad = 1;
    private const int DitherNone = 0;
    private const int PaletteCustom = 0;
    private static readonly Guid FactoryClass = new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid FactoryInterface = new Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid PixelPbgra = new Guid("6fddc324-4e03-4bfe-b185-3d77768dc910");
    private static readonly Guid ContainerPng = new Guid("1b7cfaf4-713f-473c-bbcd-6137425faeaf");
    private static readonly Guid ContainerJpeg = new Guid("19e4a5aa-5662-4fc5-a0c0-1758028e1057");
    private static readonly Guid ContainerBmp = new Guid("0af1d87e-fcfe-4188-bdeb-a7906471cbe3");

    public static IntPtr CreateFactory()
    {
        Guid clsid = FactoryClass;
        Guid iid = FactoryInterface;
        IntPtr factory;
        ComUtil.Check(Win32Api.CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer, ref iid, out factory), "WIC factory");
        return factory;
    }

    public static WicImageDocument Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Image path is empty.");
        }
        path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim('"')));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Image file does not exist.", path);
        }

        IntPtr factory = IntPtr.Zero;
        IntPtr decoder = IntPtr.Zero;
        IntPtr frame = IntPtr.Zero;
        IntPtr converter = IntPtr.Zero;
        try
        {
            factory = CreateFactory();
            decoder = CreateDecoder(factory, path);
            frame = GetFrame(decoder, 0);
            converter = CreateConverter(factory);
            InitializeConverter(converter, frame);
            int width;
            int height;
            GetPixelExtent(converter, out width, out height);
            byte[] pixels = new byte[width * height * 4];
            CopyPixels(converter, width, height, pixels);
            return new WicImageDocument(width, height, pixels);
        }
        finally
        {
            ComUtil.Release(ref converter);
            ComUtil.Release(ref frame);
            ComUtil.Release(ref decoder);
            ComUtil.Release(ref factory);
        }
    }

    public static WicPixelStore CreateStore(int width, int height)
    {
        IntPtr factory = IntPtr.Zero;
        try
        {
            factory = CreateFactory();
            IntPtr store;
            Guid format = PixelPbgra;
            CreatePixelStoreDelegate create = ComUtil.GetDelegate<CreatePixelStoreDelegate>(factory, 17);
            ComUtil.Check(create(factory, (uint)width, (uint)height, ref format, CacheOnLoad, out store), "WIC pixel store");
            return new WicPixelStore(store, width, height);
        }
        finally
        {
            ComUtil.Release(ref factory);
        }
    }

    public static void SavePixels(string path, int width, int height, byte[] pixels)
    {
        if (pixels == null)
        {
            throw new ArgumentNullException("pixels");
        }

        string temp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        IntPtr factory = IntPtr.Zero;
        IntPtr stream = IntPtr.Zero;
        IntPtr encoder = IntPtr.Zero;
        IntPtr frame = IntPtr.Zero;
        IntPtr bag = IntPtr.Zero;
        GCHandle handle = default(GCHandle);
        try
        {
            factory = CreateFactory();
            stream = CreateStream(factory);
            InitializeStream(stream, temp);
            encoder = CreateEncoder(factory, EncoderForPath(path));
            InitializeEncoder(encoder, stream);
            CreateNewFrame(encoder, out frame, out bag);
            InitializeFrame(frame, bag);
            SetFrameExtent(frame, width, height);
            Guid format = PixelPbgra;
            SetFrameFormat(frame, ref format);
            handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            WriteFramePixels(frame, height, width * 4, pixels.Length, handle.AddrOfPinnedObject());
            CommitFrame(frame);
            CommitEncoder(encoder);
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
            ComUtil.Release(ref bag);
            ComUtil.Release(ref frame);
            ComUtil.Release(ref encoder);
            ComUtil.Release(ref stream);
            ComUtil.Release(ref factory);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
        File.Move(temp, path);
    }

    public static void CopyPixels(IntPtr source, int width, int height, byte[] target)
    {
        GCHandle handle = GCHandle.Alloc(target, GCHandleType.Pinned);
        try
        {
            CopyPixelsDelegate copy = ComUtil.GetDelegate<CopyPixelsDelegate>(source, 7);
            ComUtil.Check(copy(source, IntPtr.Zero, (uint)(width * 4), (uint)target.Length, handle.AddrOfPinnedObject()), "WIC copy pixels");
        }
        finally
        {
            handle.Free();
        }
    }

    private static IntPtr CreateDecoder(IntPtr factory, string path)
    {
        IntPtr decoder;
        CreateDecoderDelegate create = ComUtil.GetDelegate<CreateDecoderDelegate>(factory, 3);
        ComUtil.Check(create(factory, path, IntPtr.Zero, Win32Api.GenericRead, DecodeCacheOnLoad, out decoder), "WIC decoder");
        return decoder;
    }

    private static IntPtr GetFrame(IntPtr decoder, uint index)
    {
        IntPtr frame;
        GetFrameDelegate get = ComUtil.GetDelegate<GetFrameDelegate>(decoder, 13);
        ComUtil.Check(get(decoder, index, out frame), "WIC frame");
        return frame;
    }

    private static IntPtr CreateConverter(IntPtr factory)
    {
        IntPtr converter;
        CreateConverterDelegate create = ComUtil.GetDelegate<CreateConverterDelegate>(factory, 10);
        ComUtil.Check(create(factory, out converter), "WIC converter");
        return converter;
    }

    private static void InitializeConverter(IntPtr converter, IntPtr source)
    {
        Guid format = PixelPbgra;
        InitializeConverterDelegate init = ComUtil.GetDelegate<InitializeConverterDelegate>(converter, 8);
        ComUtil.Check(init(converter, source, ref format, DitherNone, IntPtr.Zero, 0.0, PaletteCustom), "WIC converter init");
    }

    private static void GetPixelExtent(IntPtr source, out int width, out int height)
    {
        uint w;
        uint h;
        GetExtentDelegate get = ComUtil.GetDelegate<GetExtentDelegate>(source, 3);
        ComUtil.Check(get(source, out w, out h), "WIC extent");
        width = checked((int)w);
        height = checked((int)h);
    }

    private static IntPtr CreateStream(IntPtr factory)
    {
        IntPtr stream;
        CreateStreamDelegate create = ComUtil.GetDelegate<CreateStreamDelegate>(factory, 14);
        ComUtil.Check(create(factory, out stream), "WIC stream");
        return stream;
    }

    private static void InitializeStream(IntPtr stream, string path)
    {
        InitializeStreamDelegate init = ComUtil.GetDelegate<InitializeStreamDelegate>(stream, 15);
        ComUtil.Check(init(stream, path, Win32Api.GenericWrite), "WIC stream init");
    }

    private static IntPtr CreateEncoder(IntPtr factory, Guid container)
    {
        IntPtr encoder;
        CreateEncoderDelegate create = ComUtil.GetDelegate<CreateEncoderDelegate>(factory, 8);
        ComUtil.Check(create(factory, ref container, IntPtr.Zero, out encoder), "WIC encoder");
        return encoder;
    }

    private static void InitializeEncoder(IntPtr encoder, IntPtr stream)
    {
        EncoderInitializeDelegate init = ComUtil.GetDelegate<EncoderInitializeDelegate>(encoder, 3);
        ComUtil.Check(init(encoder, stream, EncoderNoCache), "WIC encoder init");
    }

    private static void CreateNewFrame(IntPtr encoder, out IntPtr frame, out IntPtr bag)
    {
        NewFrameDelegate create = ComUtil.GetDelegate<NewFrameDelegate>(encoder, 10);
        ComUtil.Check(create(encoder, out frame, out bag), "WIC frame encoder");
    }

    private static void InitializeFrame(IntPtr frame, IntPtr bag)
    {
        FrameInitializeDelegate init = ComUtil.GetDelegate<FrameInitializeDelegate>(frame, 3);
        ComUtil.Check(init(frame, bag), "WIC frame init");
    }

    private static void SetFrameExtent(IntPtr frame, int width, int height)
    {
        FrameExtentDelegate set = ComUtil.GetDelegate<FrameExtentDelegate>(frame, 4);
        ComUtil.Check(set(frame, (uint)width, (uint)height), "WIC frame extent");
    }

    private static void SetFrameFormat(IntPtr frame, ref Guid format)
    {
        FrameFormatDelegate set = ComUtil.GetDelegate<FrameFormatDelegate>(frame, 6);
        ComUtil.Check(set(frame, ref format), "WIC frame format");
    }

    private static void WriteFramePixels(IntPtr frame, int height, int stride, int length, IntPtr data)
    {
        FrameWriteDelegate write = ComUtil.GetDelegate<FrameWriteDelegate>(frame, 10);
        ComUtil.Check(write(frame, (uint)height, (uint)stride, (uint)length, data), "WIC write pixels");
    }

    private static void CommitFrame(IntPtr frame)
    {
        CommitDelegate commit = ComUtil.GetDelegate<CommitDelegate>(frame, 12);
        ComUtil.Check(commit(frame), "WIC frame commit");
    }

    private static void CommitEncoder(IntPtr encoder)
    {
        CommitDelegate commit = ComUtil.GetDelegate<CommitDelegate>(encoder, 11);
        ComUtil.Check(commit(encoder), "WIC encoder commit");
    }

    private static Guid EncoderForPath(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".jpg" || ext == ".jpeg")
        {
            return ContainerJpeg;
        }
        if (ext == ".bmp")
        {
            return ContainerBmp;
        }
        return ContainerPng;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int CreateDecoderDelegate(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr vendor, uint access, int options, out IntPtr decoder);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFrameDelegate(IntPtr self, uint index, out IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateConverterDelegate(IntPtr self, out IntPtr converter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitializeConverterDelegate(IntPtr self, IntPtr source, ref Guid format, int dither, IntPtr palette, double alpha, int paletteTranslate);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetExtentDelegate(IntPtr self, out uint width, out uint height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CopyPixelsDelegate(IntPtr self, IntPtr rect, uint stride, uint length, IntPtr pixels);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreatePixelStoreDelegate(IntPtr self, uint width, uint height, ref Guid format, int option, out IntPtr store);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateStreamDelegate(IntPtr self, out IntPtr stream);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int InitializeStreamDelegate(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string path, uint access);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateEncoderDelegate(IntPtr self, ref Guid container, IntPtr vendor, out IntPtr encoder);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EncoderInitializeDelegate(IntPtr self, IntPtr stream, int option);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int NewFrameDelegate(IntPtr self, out IntPtr frame, out IntPtr bag);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FrameInitializeDelegate(IntPtr self, IntPtr bag);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FrameExtentDelegate(IntPtr self, uint width, uint height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FrameFormatDelegate(IntPtr self, ref Guid format);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FrameWriteDelegate(IntPtr self, uint lineCount, uint stride, uint length, IntPtr pixels);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CommitDelegate(IntPtr self);
}
