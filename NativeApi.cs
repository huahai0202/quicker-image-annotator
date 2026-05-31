using System;
using System.Runtime.InteropServices;

internal static class ComUtil
{
    public static T GetDelegate<T>(IntPtr comPointer, int index) where T : class
    {
        IntPtr table = Marshal.ReadIntPtr(comPointer);
        IntPtr fn = Marshal.ReadIntPtr(table, index * IntPtr.Size);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
    }

    public static void Release(ref IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.Release(pointer);
            pointer = IntPtr.Zero;
        }
    }

    public static void Check(int hr, string operation)
    {
        if (hr < 0)
        {
            throw new NativeCallException(operation, hr);
        }
    }
}

internal sealed class NativeCallException : InvalidOperationException
{
    public NativeCallException(string operation, int result)
        : base(operation + " failed: 0x" + result.ToString("X8"), Marshal.GetExceptionForHR(result))
    {
        Operation = operation;
        Result = result;
    }

    public string Operation { get; private set; }
    public int Result { get; private set; }
}

internal static class Win32Api
{
    public const int CwUseDefault = unchecked((int)0x80000000);
    public const int SwShow = 5;
    public const int WmDestroy = 0x0002;
    public const int WmPaint = 0x000F;
    public const int WmClose = 0x0010;
    public const int WmTimer = 0x0113;
    public const int WmEraseBkgnd = 0x0014;
    public const int WmSetCursor = 0x0020;
    public const int WmKeyDown = 0x0100;
    public const int WmChar = 0x0102;
    public const int WmMouseMove = 0x0200;
    public const int WmLButtonDown = 0x0201;
    public const int WmLButtonUp = 0x0202;
    public const int WmLButtonDblClk = 0x0203;
    public const int WmRButtonDown = 0x0204;
    public const int WmRButtonUp = 0x0205;
    public const int WmMouseWheel = 0x020A;
    public const int WmImeComposition = 0x010F;
    public const int WmImeEndComposition = 0x010E;
    public const int GcsCompStr = 0x0008;
    public const int GcsResultStr = 0x0800;
    public const int CfsPoint = 0x0002;
    public const int WsOverlappedWindow = 0x00CF0000;
    public const int CsHRedraw = 0x0002;
    public const int CsVRedraw = 0x0001;
    public const int CsDblClks = 0x0008;
    public const int VkEscape = 0x1B;
    public const int VkDelete = 0x2E;
    public const int VkBack = 0x08;
    public const int VkReturn = 0x0D;
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkLeft = 0x25;
    public const int VkRight = 0x27;
    public const int VkHome = 0x24;
    public const int VkEnd = 0x23;
    public const int VkA = 0x41;
    public const int VkC = 0x43;
    public const int VkV = 0x56;
    public const int VkX = 0x58;
    public const int VkZ = 0x5A;
    public const int VkS = 0x53;
    public const int GwlpUserData = -21;
    public const int IdcArrow = 32512;
    public const int IdcIBeam = 32513;
    public const int IdcCross = 32515;
    public const int IdcSizeNwSe = 32642;
    public const int IdcSizeNeSw = 32643;
    public const int IdcSizeWe = 32644;
    public const int IdcSizeNs = 32645;
    public const int IdcSizeAll = 32646;
    public const int WmSetIcon = 0x0080;
    public const int IconSmall = 0;
    public const int IconBig = 1;
    public const uint ImageIcon = 1;
    public const uint LrLoadFromFile = 0x00000010;
    public const uint MbIconError = 0x00000010;
    public const uint MbIconWarning = 0x00000030;
    public const uint MbOk = 0x00000000;
    public const uint MbYesNo = 0x00000004;
    public const int IdYes = 6;
    public const uint CfHdrop = 15;
    public const uint CfUnicodeText = 13;
    public const uint CfDib = 8;
    public const uint CfDibV5 = 17;
    public const uint GmemMoveable = 0x0002;
    public const uint GmemZeroinit = 0x0040;
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public static readonly IntPtr HwndTopMost = new IntPtr(-1);
    public static readonly IntPtr HwndNoTopMost = new IntPtr(-2);
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoSize = 0x0001;

    public delegate IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public WindowProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaintStruct
    {
        public IntPtr hdc;
        public bool fErase;
        public NativeRect rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CompositionForm
    {
        public int dwStyle;
        public NativePoint ptCurrentPos;
        public NativeRect rcArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DropFiles
    {
        public uint pFiles;
        public int ptX;
        public int ptY;
        public int fNC;
        public int fWide;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BrowseInfo
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public IntPtr lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    public static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    public static extern IntPtr CreateWindowEx(
        int dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    public static extern bool GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", ExactSpelling = true)]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct lpPaint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hWnd, ref PaintStruct lpPaint);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", ExactSpelling = true)]
    private static extern bool SetWindowTextPtr(IntPtr hWnd, IntPtr lpString);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(IntPtr hWnd, IntPtr uIDEvent);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", ExactSpelling = true)]
    private static extern int MessageBoxPtr(IntPtr hWnd, IntPtr lpText, IntPtr lpCaption, uint uType);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    public static extern IntPtr LoadImage(IntPtr hInst, [MarshalAs(UnmanagedType.LPWStr)] string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string lpModuleName);

    [DllImport("imm32.dll")]
    public static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    public static extern int ImmGetCompositionString(IntPtr hIMC, int dwIndex, byte[] lpBuf, int dwBufLen);

    [DllImport("imm32.dll")]
    public static extern bool ImmSetCompositionWindow(IntPtr hIMC, ref CompositionForm lpCompForm);

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        out IntPtr ppv);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern UIntPtr GlobalSize(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern uint RegisterClipboardFormat([MarshalAs(UnmanagedType.LPWStr)] string lpszFormat);

    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern uint DragQueryFile(IntPtr hDrop, uint iFile, IntPtr lpszFile, uint cch);

    [DllImport("shell32.dll", EntryPoint = "SHBrowseForFolderW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr SHBrowseForFolder(ref BrowseInfo lpbi);

    [DllImport("shell32.dll", EntryPoint = "SHGetPathFromIDListW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern bool SHGetPathFromIDList(IntPtr pidl, IntPtr pszPath);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);

    public static IntPtr SetUserData(IntPtr hwnd, IntPtr value)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, GwlpUserData, value) : SetWindowLong32(hwnd, GwlpUserData, value);
    }

    public static bool SetWindowTextUnicode(IntPtr hwnd, string text)
    {
        IntPtr pointer = Marshal.StringToHGlobalUni(text ?? string.Empty);
        try
        {
            return SetWindowTextPtr(hwnd, pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public static int MessageBoxUnicode(IntPtr hwnd, string text, string caption, uint type)
    {
        IntPtr textPointer = Marshal.StringToHGlobalUni(text ?? string.Empty);
        IntPtr captionPointer = Marshal.StringToHGlobalUni(caption ?? string.Empty);
        try
        {
            return MessageBoxPtr(hwnd, textPointer, captionPointer, type);
        }
        finally
        {
            Marshal.FreeHGlobal(captionPointer);
            Marshal.FreeHGlobal(textPointer);
        }
    }

    public static int GetX(IntPtr lParam)
    {
        return (short)((long)lParam & 0xffff);
    }

    public static int GetY(IntPtr lParam)
    {
        return (short)(((long)lParam >> 16) & 0xffff);
    }

    public static int HighWord(IntPtr value)
    {
        return (short)(((long)value >> 16) & 0xffff);
    }
}

internal static class DWriteApi
{
    private const int FactoryTypeShared = 0;
    private const int WeightBold = 700;
    private const int StyleNormal = 0;
    private const int StretchNormal = 5;
    private static readonly Guid FactoryId = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    [StructLayout(LayoutKind.Sequential)]
    public struct TextMetrics
    {
        public float left;
        public float top;
        public float width;
        public float widthIncludingTrailingWhitespace;
        public float height;
        public float layoutWidth;
        public float layoutHeight;
        public uint maxBidiReorderingDepth;
        public uint lineCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HitTestMetrics
    {
        public uint textPosition;
        public uint length;
        public float left;
        public float top;
        public float width;
        public float height;
        public uint bidiLevel;
        public int isText;
        public int isTrimmed;
    }

    [DllImport("dwrite.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int DWriteCreateFactory(int factoryType, ref Guid iid, out IntPtr factory);

    public static IntPtr CreateFactory()
    {
        Guid id = FactoryId;
        IntPtr factory;
        ComUtil.Check(DWriteCreateFactory(FactoryTypeShared, ref id, out factory), "DWriteCreateFactory");
        return factory;
    }

    public static IntPtr CreateTextFormat(IntPtr factory, string family, float em)
    {
        IntPtr format;
        CreateTextFormatDelegate create = ComUtil.GetDelegate<CreateTextFormatDelegate>(factory, 15);
        ComUtil.Check(create(factory, family, IntPtr.Zero, WeightBold, StyleNormal, StretchNormal, em, "zh-cn", out format), "CreateTextFormat");
        return format;
    }

    public static IntPtr CreateTextLayout(IntPtr factory, string text, IntPtr format, float maxWidth, float maxHeight)
    {
        text = text ?? string.Empty;
        IntPtr layout;
        CreateTextLayoutDelegate create = ComUtil.GetDelegate<CreateTextLayoutDelegate>(factory, 18);
        ComUtil.Check(create(factory, text, (uint)text.Length, format, maxWidth, maxHeight, out layout), "CreateTextLayout");
        return layout;
    }

    public static TextMetrics GetMetrics(IntPtr layout)
    {
        TextMetrics metrics;
        GetMetricsDelegate get = ComUtil.GetDelegate<GetMetricsDelegate>(layout, 60);
        ComUtil.Check(get(layout, out metrics), "TextLayout.GetMetrics");
        return metrics;
    }

    public static TextHitResult HitTestPoint(IntPtr layout, float x, float y)
    {
        int trailing;
        int inside;
        HitTestMetrics metrics;
        HitTestPointDelegate hit = ComUtil.GetDelegate<HitTestPointDelegate>(layout, 64);
        ComUtil.Check(hit(layout, x, y, out trailing, out inside, out metrics), "TextLayout.HitTestPoint");
        TextHitResult result = new TextHitResult();
        result.TextPosition = (int)metrics.textPosition;
        result.IsInside = inside != 0;
        result.IsTrailing = trailing != 0;
        return result;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int CreateTextFormatDelegate(
        IntPtr self,
        [MarshalAs(UnmanagedType.LPWStr)] string family,
        IntPtr collection,
        int weight,
        int style,
        int stretch,
        float em,
        [MarshalAs(UnmanagedType.LPWStr)] string locale,
        out IntPtr format);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int CreateTextLayoutDelegate(
        IntPtr self,
        [MarshalAs(UnmanagedType.LPWStr)] string text,
        uint textLength,
        IntPtr format,
        float maxWidth,
        float maxHeight,
        out IntPtr layout);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetMetricsDelegate(IntPtr self, out TextMetrics metrics);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int HitTestPointDelegate(
        IntPtr self,
        float x,
        float y,
        out int isTrailingHit,
        out int isInside,
        out HitTestMetrics metrics);
}

internal static class D2DApi
{
    public const int InterpolationLinear = 1;
    public const int InterpolationNearest = 0;
    public const int AlphaPremultiplied = 1;
    public const int AlphaIgnore = 3;
    public const int FormatBgra = 87;

    private const int FactorySingleThreaded = 0;
    private const int TargetDefault = 0;
    private const int TargetHardware = 2;
    private const int UsageNone = 0;
    private const int FeatureDefault = 0;
    private const int CapFlat = 0;
    private const int CapRound = 2;
    private const int JoinRound = 2;
    private const int DashSolid = 0;
    private const int DashDash = 1;
    private const int FillWinding = 1;
    private const int FigureFilled = 0;
    private const int FigureClosed = 1;
    private static readonly Guid FactoryId = new Guid("06152247-6f50-465a-9245-118bfd3b6007");

    [StructLayout(LayoutKind.Sequential)]
    public struct IntRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;

        public IntRect(int left, int top, int right, int bottom)
        {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bottom = bottom;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PixelFormat
    {
        public int format;
        public int alphaMode;

        public PixelFormat(int format, int alphaMode)
        {
            this.format = format;
            this.alphaMode = alphaMode;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TargetProperties
    {
        public int type;
        public PixelFormat pixelFormat;
        public float dpiX;
        public float dpiY;
        public int usage;
        public int minLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ImageProperties
    {
        public PixelFormat pixelFormat;
        public float dpiX;
        public float dpiY;

        public static ImageProperties Premultiplied
        {
            get
            {
                ImageProperties p = new ImageProperties();
                p.pixelFormat = new PixelFormat(FormatBgra, AlphaPremultiplied);
                p.dpiX = 96f;
                p.dpiY = 96f;
                return p;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExtentU
    {
        public uint width;
        public uint height;

        public ExtentU(uint width, uint height)
        {
            this.width = width;
            this.height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RectF
    {
        public float left;
        public float top;
        public float right;
        public float bottom;

        public RectF(float left, float top, float right, float bottom)
        {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bottom = bottom;
        }

        public static RectF FromRect(GpuRect rect)
        {
            return new RectF(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point2
    {
        public float x;
        public float y;

        public Point2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Point2 FromPoint(GpuPoint point)
        {
            return new Point2(point.X, point.Y);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Ellipse
    {
        public Point2 point;
        public float radiusX;
        public float radiusY;

        public static Ellipse FromRect(GpuRect rect)
        {
            Ellipse e = new Ellipse();
            e.point = new Point2(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);
            e.radiusX = Math.Abs(rect.Width) / 2f;
            e.radiusY = Math.Abs(rect.Height) / 2f;
            return e;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RoundedRect
    {
        public RectF rect;
        public float radiusX;
        public float radiusY;

        public RoundedRect(GpuRect rect, float radius)
        {
            this.rect = RectF.FromRect(rect);
            radiusX = radius;
            radiusY = radius;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Matrix
    {
        public float m11;
        public float m12;
        public float m21;
        public float m22;
        public float dx;
        public float dy;

        public static Matrix Identity
        {
            get
            {
                Matrix m = new Matrix();
                m.m11 = 1f;
                m.m22 = 1f;
                return m;
            }
        }

        public static Matrix ScaleTranslate(float scale, float x, float y)
        {
            Matrix m = new Matrix();
            m.m11 = scale;
            m.m22 = scale;
            m.dx = x;
            m.dy = y;
            return m;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2Rgba
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public static D2Rgba FromRgba(Rgba rgba)
        {
            D2Rgba result = new D2Rgba();
            result.r = rgba.R / 255f;
            result.g = rgba.G / 255f;
            result.b = rgba.B / 255f;
            result.a = rgba.A / 255f;
            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StrokeProperties
    {
        public int startCap;
        public int endCap;
        public int dashCap;
        public int lineJoin;
        public float miterLimit;
        public int dashStyle;
        public float dashOffset;

        public static StrokeProperties Round
        {
            get
            {
                StrokeProperties p = new StrokeProperties();
                p.startCap = CapRound;
                p.endCap = CapRound;
                p.dashCap = CapRound;
                p.lineJoin = JoinRound;
                p.miterLimit = 10f;
                p.dashStyle = DashSolid;
                return p;
            }
        }

        public static StrokeProperties Dash
        {
            get
            {
                StrokeProperties p = Round;
                p.startCap = CapFlat;
                p.endCap = CapFlat;
                p.dashCap = CapFlat;
                p.dashStyle = DashDash;
                return p;
            }
        }
    }

    [DllImport("d2d1.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int D2D1CreateFactory(int factoryType, ref Guid riid, IntPtr options, out IntPtr factory);

    public static IntPtr CreateFactory()
    {
        Guid id = FactoryId;
        IntPtr factory;
        ComUtil.Check(D2D1CreateFactory(FactorySingleThreaded, ref id, IntPtr.Zero, out factory), "D2D factory");
        return factory;
    }

    public static IntPtr CreateDCTarget(IntPtr factory)
    {
        TargetProperties p = CreateTargetProperties(TargetHardware, AlphaIgnore);
        IntPtr target;
        CreateDCTargetDelegate create = ComUtil.GetDelegate<CreateDCTargetDelegate>(factory, 16);
        ComUtil.Check(create(factory, ref p, out target), "D2D DC target");
        return target;
    }

    public static IntPtr CreateWicTarget(IntPtr factory, IntPtr pixelStore)
    {
        TargetProperties p = CreateTargetProperties(TargetDefault, AlphaPremultiplied);
        IntPtr target;
        CreateWicTargetDelegate create = ComUtil.GetDelegate<CreateWicTargetDelegate>(factory, 13);
        ComUtil.Check(create(factory, pixelStore, ref p, out target), "D2D WIC target");
        return target;
    }

    public static IntPtr CreateImageFromMemory(IntPtr target, int width, int height, IntPtr data, uint stride)
    {
        ExtentU extent = new ExtentU((uint)width, (uint)height);
        ImageProperties props = ImageProperties.Premultiplied;
        IntPtr image;
        CreateImageDelegate create = ComUtil.GetDelegate<CreateImageDelegate>(target, 4);
        ComUtil.Check(create(target, extent, data, stride, ref props, out image), "D2D image upload");
        return image;
    }

    public static IntPtr CreateBrush(IntPtr target, Rgba rgba)
    {
        D2Rgba c = D2Rgba.FromRgba(rgba);
        IntPtr brush;
        CreateBrushDelegate create = ComUtil.GetDelegate<CreateBrushDelegate>(target, 8);
        ComUtil.Check(create(target, ref c, IntPtr.Zero, out brush), "D2D brush");
        return brush;
    }

    public static IntPtr CreateStroke(IntPtr factory, bool dashed)
    {
        StrokeProperties p = dashed ? StrokeProperties.Dash : StrokeProperties.Round;
        IntPtr stroke;
        CreateStrokeDelegate create = ComUtil.GetDelegate<CreateStrokeDelegate>(factory, 11);
        ComUtil.Check(create(factory, ref p, IntPtr.Zero, 0, out stroke), "D2D stroke");
        return stroke;
    }

    public static IntPtr CreatePath(IntPtr factory)
    {
        IntPtr path;
        CreatePathDelegate create = ComUtil.GetDelegate<CreatePathDelegate>(factory, 10);
        ComUtil.Check(create(factory, out path), "D2D path");
        return path;
    }

    public static IntPtr OpenPath(IntPtr path)
    {
        IntPtr sink;
        OpenPathDelegate open = ComUtil.GetDelegate<OpenPathDelegate>(path, 17);
        ComUtil.Check(open(path, out sink), "D2D path sink");
        return sink;
    }

    public static void CloseSink(IntPtr sink)
    {
        CloseSinkDelegate close = ComUtil.GetDelegate<CloseSinkDelegate>(sink, 9);
        ComUtil.Check(close(sink), "D2D close sink");
    }

    public static void SetFillMode(IntPtr sink)
    {
        SetFillModeDelegate set = ComUtil.GetDelegate<SetFillModeDelegate>(sink, 3);
        set(sink, FillWinding);
    }

    public static void BeginFigure(IntPtr sink, GpuPoint point)
    {
        BeginFigureDelegate begin = ComUtil.GetDelegate<BeginFigureDelegate>(sink, 5);
        begin(sink, Point2.FromPoint(point), FigureFilled);
    }

    public static void AddLines(IntPtr sink, GpuPoint[] points, int start, int count)
    {
        if (count <= 0)
        {
            return;
        }
        Point2[] native = new Point2[count];
        for (int i = 0; i < count; i++)
        {
            native[i] = Point2.FromPoint(points[start + i]);
        }
        AddLinesDelegate add = ComUtil.GetDelegate<AddLinesDelegate>(sink, 6);
        add(sink, native, (uint)native.Length);
    }

    public static void EndFigure(IntPtr sink)
    {
        EndFigureDelegate end = ComUtil.GetDelegate<EndFigureDelegate>(sink, 8);
        end(sink, FigureClosed);
    }

    public static void BindDC(IntPtr target, IntPtr hdc, int width, int height)
    {
        IntRect rect = new IntRect(0, 0, width, height);
        BindDCDelegate bind = ComUtil.GetDelegate<BindDCDelegate>(target, 57);
        ComUtil.Check(bind(target, hdc, ref rect), "D2D BindDC");
    }

    public static void BeginDraw(IntPtr target)
    {
        BeginDrawDelegate begin = ComUtil.GetDelegate<BeginDrawDelegate>(target, 48);
        begin(target);
    }

    public static void EndDraw(IntPtr target)
    {
        ulong a;
        ulong b;
        EndDrawDelegate end = ComUtil.GetDelegate<EndDrawDelegate>(target, 49);
        ComUtil.Check(end(target, out a, out b), "D2D EndDraw");
    }

    public static void SetTransform(IntPtr target, Matrix matrix)
    {
        SetTransformDelegate set = ComUtil.GetDelegate<SetTransformDelegate>(target, 30);
        set(target, ref matrix);
    }

    public static void Clear(IntPtr target, Rgba rgba)
    {
        D2Rgba c = D2Rgba.FromRgba(rgba);
        ClearDelegate clear = ComUtil.GetDelegate<ClearDelegate>(target, 47);
        clear(target, ref c);
    }

    public static void DrawImageSection(IntPtr target, IntPtr image, GpuRect destination, GpuRect sourceRect, int interpolation)
    {
        RectF dest = RectF.FromRect(destination);
        RectF source = RectF.FromRect(sourceRect);
        DrawImageDelegate draw = ComUtil.GetDelegate<DrawImageDelegate>(target, 26);
        draw(target, image, ref dest, 1f, interpolation, ref source);
    }

    public static void DrawLine(IntPtr target, GpuPoint a, GpuPoint b, IntPtr brush, float width, IntPtr stroke)
    {
        DrawLineDelegate draw = ComUtil.GetDelegate<DrawLineDelegate>(target, 15);
        draw(target, Point2.FromPoint(a), Point2.FromPoint(b), brush, width, stroke);
    }

    public static void DrawRectangle(IntPtr target, GpuRect rect, IntPtr brush, float width, IntPtr stroke)
    {
        RectF r = RectF.FromRect(rect);
        DrawRectDelegate draw = ComUtil.GetDelegate<DrawRectDelegate>(target, 16);
        draw(target, ref r, brush, width, stroke);
    }

    public static void FillRectangle(IntPtr target, GpuRect rect, IntPtr brush)
    {
        RectF r = RectF.FromRect(rect);
        FillRectDelegate fill = ComUtil.GetDelegate<FillRectDelegate>(target, 17);
        fill(target, ref r, brush);
    }

    public static void DrawRoundedRectangle(IntPtr target, GpuRect rect, float radius, IntPtr brush, float width, IntPtr stroke)
    {
        RoundedRect r = new RoundedRect(rect, radius);
        DrawRoundedRectDelegate draw = ComUtil.GetDelegate<DrawRoundedRectDelegate>(target, 18);
        draw(target, ref r, brush, width, stroke);
    }

    public static void FillRoundedRectangle(IntPtr target, GpuRect rect, float radius, IntPtr brush)
    {
        RoundedRect r = new RoundedRect(rect, radius);
        FillRoundedRectDelegate fill = ComUtil.GetDelegate<FillRoundedRectDelegate>(target, 19);
        fill(target, ref r, brush);
    }

    public static void DrawEllipse(IntPtr target, GpuRect rect, IntPtr brush, float width, IntPtr stroke)
    {
        Ellipse e = Ellipse.FromRect(rect);
        DrawEllipseDelegate draw = ComUtil.GetDelegate<DrawEllipseDelegate>(target, 20);
        draw(target, ref e, brush, width, stroke);
    }

    public static void FillEllipse(IntPtr target, GpuRect rect, IntPtr brush)
    {
        Ellipse e = Ellipse.FromRect(rect);
        FillEllipseDelegate fill = ComUtil.GetDelegate<FillEllipseDelegate>(target, 21);
        fill(target, ref e, brush);
    }

    public static void FillPath(IntPtr target, IntPtr path, IntPtr brush)
    {
        FillPathDelegate fill = ComUtil.GetDelegate<FillPathDelegate>(target, 23);
        fill(target, path, brush, IntPtr.Zero);
    }

    public static void DrawText(IntPtr target, string text, IntPtr format, GpuRect layout, IntPtr brush)
    {
        RectF rect = RectF.FromRect(layout);
        DrawTextDelegate draw = ComUtil.GetDelegate<DrawTextDelegate>(target, 27);
        draw(target, text, (uint)text.Length, format, ref rect, brush, 0, 0);
    }

    private static TargetProperties CreateTargetProperties(int type, int alpha)
    {
        TargetProperties p = new TargetProperties();
        p.type = type;
        p.pixelFormat = new PixelFormat(FormatBgra, alpha);
        p.dpiX = 96f;
        p.dpiY = 96f;
        p.usage = UsageNone;
        p.minLevel = FeatureDefault;
        return p;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDCTargetDelegate(IntPtr self, ref TargetProperties p, out IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateWicTargetDelegate(IntPtr self, IntPtr store, ref TargetProperties p, out IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateImageDelegate(IntPtr self, ExtentU extent, IntPtr data, uint pitch, ref ImageProperties p, out IntPtr image);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateBrushDelegate(IntPtr self, ref D2Rgba c, IntPtr p, out IntPtr brush);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateStrokeDelegate(IntPtr self, ref StrokeProperties p, IntPtr dashes, uint dashCount, out IntPtr stroke);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreatePathDelegate(IntPtr self, out IntPtr path);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OpenPathDelegate(IntPtr self, out IntPtr sink);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetFillModeDelegate(IntPtr self, int mode);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BeginFigureDelegate(IntPtr self, Point2 point, int begin);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void AddLinesDelegate(IntPtr self, [In] Point2[] points, uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void EndFigureDelegate(IntPtr self, int end);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CloseSinkDelegate(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BindDCDelegate(IntPtr self, IntPtr hdc, ref IntRect rect);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BeginDrawDelegate(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EndDrawDelegate(IntPtr self, out ulong tag1, out ulong tag2);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetTransformDelegate(IntPtr self, ref Matrix matrix);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ClearDelegate(IntPtr self, ref D2Rgba rgba);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DrawImageDelegate(IntPtr self, IntPtr image, ref RectF dest, float opacity, int interpolation, ref RectF source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DrawLineDelegate(IntPtr self, Point2 a, Point2 b, IntPtr brush, float width, IntPtr stroke);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DrawRectDelegate(IntPtr self, ref RectF rect, IntPtr brush, float width, IntPtr stroke);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FillRectDelegate(IntPtr self, ref RectF rect, IntPtr brush);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DrawRoundedRectDelegate(IntPtr self, ref RoundedRect rect, IntPtr brush, float width, IntPtr stroke);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FillRoundedRectDelegate(IntPtr self, ref RoundedRect rect, IntPtr brush);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DrawEllipseDelegate(IntPtr self, ref Ellipse ellipse, IntPtr brush, float width, IntPtr stroke);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FillEllipseDelegate(IntPtr self, ref Ellipse ellipse, IntPtr brush);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FillPathDelegate(IntPtr self, IntPtr path, IntPtr brush, IntPtr opacityBrush);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate void DrawTextDelegate(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string text, uint len, IntPtr format, ref RectF layout, IntPtr brush, int options, int measuringMode);
}
