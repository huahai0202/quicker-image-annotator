using System;
using System.Collections.Generic;

internal enum ToolMode
{
    Rect,
    Ellipse,
    Arrow,
    Pen,
    Text,
    Mosaic
}

internal enum ToolbarCommand
{
    ToolRect,
    ToolEllipse,
    ToolArrow,
    ToolPen,
    ToolMosaic,
    ToolText,
    Ocr,
    Undo,
    Clear,
    Fit,
    Pin,
    Settings,
    Cancel,
    Save
}

internal enum SettingsOverlayCommand
{
    None,
    BrowseOutput,
    ClearOutput,
    BrowseScreenshot,
    ClearScreenshot,
    ToggleAutoStart,
    ToggleGlobalHotkey,
    Save,
    Cancel
}

internal enum AnnotationUndoKind
{
    Add,
    Delete,
    Transform
}

internal enum SelectionHandle
{
    None,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    ArrowStart,
    ArrowEnd
}

internal static class UiText
{
    public const string AppName = "\u56fe\u7247\u6807\u6ce8";
    public const string SettingsTitle = "\u8bbe\u7f6e";
    public const string SettingsSubtitle = "\u4fdd\u5b58\u4f4d\u7f6e\u4e0e\u542f\u52a8\u9009\u9879";
    public const string SaveDirectory = "\u6807\u6ce8\u4fdd\u5b58\u76ee\u5f55";
    public const string ScreenshotDirectory = "\u622a\u56fe\u4fdd\u5b58\u76ee\u5f55";
    public const string DirectoryNotSet = "\u672a\u8bbe\u7f6e";
    public const string ScreenshotDirectoryNotSet = "\u8ddf\u968f\u6807\u6ce8\u4fdd\u5b58\u76ee\u5f55";
    public const string EmptyOutputDirectoryHintPrefix = "\u7559\u7a7a\uff1a\u672c\u5730\u56fe\u7247\u5b58\u539f\u76ee\u5f55\uff0c\u526a\u8d34\u677f\u56fe\u7247\u5b58 ";
    public const string EmptyScreenshotDirectoryHintPrefix = "\u7559\u7a7a\uff1a\u8ddf\u968f\u6807\u6ce8\u4fdd\u5b58\u76ee\u5f55\uff1b\u90fd\u4e3a\u7a7a\u65f6\u5b58 ";
    public const string Browse = "\u6d4f\u89c8...";
    public const string Clear = "\u6e05\u7a7a";
    public const string Cancel = "\u53d6\u6d88";
    public const string Close = "\u5173\u95ed";
    public const string Save = "\u4fdd\u5b58";
    public const string SelectOutputDirectory = "\u9009\u62e9\u6807\u6ce8\u4fdd\u5b58\u76ee\u5f55";
    public const string SelectScreenshotDirectory = "\u9009\u62e9\u622a\u56fe\u4fdd\u5b58\u76ee\u5f55";
    public const string AutoStart = "\u5f00\u673a\u81ea\u542f";
    public const string GlobalHotkey = "\u5168\u5c40\u5feb\u6377\u952e ";
    public const string HotkeyLabel = "\u5feb\u6377\u952e";
    public const string HotkeyRequired = "\u8bf7\u8bbe\u7f6e\u81f3\u5c11\u4e00\u4e2a\u4fee\u9970\u952e\u548c\u4e00\u4e2a\u4e3b\u952e\u3002";
    public const string StartupSettingFailed = "\u5f00\u673a\u81ea\u542f\u8bbe\u7f6e\u5931\u8d25\uff1a";
    public const string GlobalHotkeyStartFailed = "\u5168\u5c40\u5feb\u6377\u952e\u672a\u542f\u52a8\uff0cAlt+A \u53ef\u80fd\u5df2\u88ab\u5176\u4ed6\u7a0b\u5e8f\u5360\u7528\u3002";
    public const string GlobalHotkeyStopFailed = "\u5168\u5c40\u5feb\u6377\u952e\u672a\u80fd\u7acb\u5373\u505c\u7528\uff0c\u8bf7\u91cd\u542f\u7a0b\u5e8f\u540e\u518d\u8bd5\u3002";
    public const string AboutTitle = "\u5173\u4e8e";
    public const string Rect = "\u77e9\u5f62";
    public const string Ellipse = "\u692d\u5706";
    public const string Arrow = "\u7bad\u5934";
    public const string Pen = "\u753b\u7b14";
    public const string Mosaic = "\u9a6c\u8d5b\u514b";
    public const string Text = "\u6587\u5b57";
    public const string Ocr = "OCR";
    public const string OcrTitle = "OCR \u8bc6\u522b";
    public const string OcrResultTitle = "OCR \u7ed3\u679c";
    public const string OcrSettings = "\u767e\u5ea6 OCR";
    public const string OcrApiKey = "API Key";
    public const string OcrSecretKey = "Secret Key";
    public const string OcrEngine = "\u8bc6\u522b\u7248\u672c";
    public const string OcrLayout = "\u7ed3\u679c\u6392\u7248";
    public const string OcrStandard = "\u6807\u51c6\u7248";
    public const string OcrAccurate = "\u9ad8\u7cbe\u5ea6\u7248";
    public const string OcrLines = "\u9010\u884c";
    public const string OcrSmartParagraph = "\u667a\u80fd\u6bb5\u843d";
    public const string OcrTranslate = "\u8c37\u6b4c\u7ffb\u8bd1";
    public const string OcrCopy = "\u590d\u5236";
    public const string OcrCopied = "\u5df2\u590d\u5236";
    public const string OcrTranslating = "\u6b63\u5728\u7ffb\u8bd1...";
    public const string OcrTranslated = "\u5df2\u7ffb\u8bd1";
    public const string OcrTranslateFailed = "\u8c37\u6b4c\u7ffb\u8bd1\u5931\u8d25\uff1a";
    public const string OcrNoText = "\u672a\u8bc6\u522b\u5230\u6587\u5b57\u3002";
    public const string OcrRunning = "\u6b63\u5728\u8c03\u7528\u767e\u5ea6 OCR\uff0c\u8bf7\u7a0d\u5019...";
    public const string OcrCredentialsMissing = "\u8bf7\u5148\u5728\u8bbe\u7f6e\u91cc\u586b\u5199\u767e\u5ea6 OCR API Key \u548c Secret Key\u3002";
    public const string OcrEmptyImage = "\u6ca1\u6709\u53ef\u7528\u7684 OCR \u56fe\u7247\u6570\u636e\u3002";
    public const string OcrTokenFailed = "\u83b7\u53d6\u767e\u5ea6 OCR access_token \u5931\u8d25\uff1a";
    public const string OcrRequestFailed = "\u767e\u5ea6 OCR \u8bc6\u522b\u5931\u8d25\uff1a";
    public const string OcrResponseInvalid = "\u767e\u5ea6 OCR \u8fd4\u56de\u7684 JSON \u683c\u5f0f\u65e0\u6cd5\u89e3\u6790\u3002";
    public const string OcrTranslateResponseInvalid = "\u8c37\u6b4c\u7ffb\u8bd1\u8fd4\u56de\u7684 JSON \u683c\u5f0f\u65e0\u6cd5\u89e3\u6790\u3002";
    public const string Undo = "\u64a4\u9500";
    public const string Fit = "\u9002\u5408\u7a97\u53e3";
    public const string Pin = "\u7f6e\u9876";
    public const string Settings = "\u8bbe\u7f6e";
    public const string ClearConfirmTitle = "\u6e05\u7a7a\u6807\u6ce8";
    public const string ClearConfirmMessage = "\u786e\u5b9a\u8981\u6e05\u7a7a\u5f53\u524d\u6807\u6ce8\u5185\u5bb9\u5417\uff1f\u6b64\u64cd\u4f5c\u65e0\u6cd5\u64a4\u9500\u3002";
    public const string NoImageFound = "\u672a\u627e\u5230\u56fe\u7247\u3002\u8bf7\u5148\u590d\u5236\u4e00\u5f20\u56fe\u7247\uff0c\u6216\u5c06\u56fe\u7247\u8def\u5f84\u4f5c\u4e3a\u7b2c\u4e00\u4e2a\u53c2\u6570\u4f20\u5165\u3002";
    public const string ImagePathIsEmpty = "\u56fe\u7247\u8def\u5f84\u4e0d\u80fd\u4e3a\u7a7a\u3002";
    public const string ImageFileDoesNotExist = "\u627e\u4e0d\u5230\u56fe\u7247\u6587\u4ef6\uff1a";
    public const string GpuFatal = "GPU/Direct2D \u6e32\u67d3\u5931\u8d25\uff0c\u65e0\u6cd5\u7ee7\u7eed\u8fd0\u884c\u3002";
}

internal sealed class AnnotationSnapshot
{
    public ToolMode Tool;
    public GpuPoint Start;
    public GpuPoint End;
    public Rgba Stroke;
    public float StrokeWidth;
    public GpuPoint[] Points = new GpuPoint[0];
    public string Text;
}

internal sealed class AnnotationUndoAction
{
    public readonly AnnotationUndoKind Kind;
    public readonly AnnotationItem Item;
    public readonly int Index;
    public readonly AnnotationSnapshot Before;

    public AnnotationUndoAction(AnnotationUndoKind kind, AnnotationItem item, int index)
        : this(kind, item, index, null)
    {
    }

    public AnnotationUndoAction(AnnotationUndoKind kind, AnnotationItem item, int index, AnnotationSnapshot before)
    {
        Kind = kind;
        Item = item;
        Index = index;
        Before = before;
    }
}

internal sealed class SettingsOverlayState
{
    public bool Visible;
    public bool TopMost;
    public bool ToolOptionsVisible;
    public bool ToolOptionsClosing;
    public float ToolOptionsOpacity;
    public string OutputDirectory;
    public string ScreenshotDirectory;
    public bool AutoStartEnabled;
    public bool GlobalHotkeyEnabled = true;
    public string Tooltip;
    public GpuPoint TooltipPoint;
}

internal struct TextHitResult
{
    public int TextPosition;
    public bool IsInside;
    public bool IsTrailing;
}

internal struct GpuPoint
{
    public float X;
    public float Y;

    public GpuPoint(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static readonly GpuPoint Empty = new GpuPoint(0f, 0f);
}

internal struct GpuExtent
{
    public int Width;
    public int Height;

    public GpuExtent(int width, int height)
    {
        Width = width;
        Height = height;
    }
}

internal struct GpuRect
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public GpuRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float Left { get { return X; } }
    public float Top { get { return Y; } }
    public float Right { get { return X + Width; } }
    public float Bottom { get { return Y + Height; } }

    public bool IsEmpty
    {
        get { return Width <= 0f || Height <= 0f; }
    }

    public bool Contains(GpuPoint point)
    {
        return point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
    }

    public static GpuRect FromEdges(float left, float top, float right, float bottom)
    {
        return new GpuRect(left, top, right - left, bottom - top);
    }

    public static GpuRect Normalize(GpuPoint a, GpuPoint b)
    {
        float left = Math.Min(a.X, b.X);
        float top = Math.Min(a.Y, b.Y);
        float right = Math.Max(a.X, b.X);
        float bottom = Math.Max(a.Y, b.Y);
        return FromEdges(left, top, right, bottom);
    }
}

internal struct Rgba
{
    public byte A;
    public byte R;
    public byte G;
    public byte B;

    public Rgba(byte a, byte r, byte g, byte b)
    {
        A = a;
        R = r;
        G = g;
        B = b;
    }

    public int Packed
    {
        get { return (A << 24) | (R << 16) | (G << 8) | B; }
    }

    public static Rgba FromRgb(int r, int g, int b)
    {
        return new Rgba(255, ClampByte(r), ClampByte(g), ClampByte(b));
    }

    public static Rgba FromArgb(int a, int r, int g, int b)
    {
        return new Rgba(ClampByte(a), ClampByte(r), ClampByte(g), ClampByte(b));
    }

    public static readonly Rgba Transparent = new Rgba(0, 0, 0, 0);
    public static readonly Rgba White = new Rgba(255, 255, 255, 255);

    private static byte ClampByte(int value)
    {
        if (value < 0)
        {
            return 0;
        }
        if (value > 255)
        {
            return 255;
        }
        return (byte)value;
    }
}

internal sealed class AnnotationItem
{
    private GpuPoint[] drawingPointsCache;
    private TextLayoutCache textLayoutCache;
    private string text = string.Empty;

    public ToolMode Tool;
    public GpuPoint Start;
    public GpuPoint End;
    public Rgba Stroke = AppStyles.DefaultStroke;
    public float StrokeWidth = AppStyles.DefaultStrokeWidth;
    public string Text
    {
        get { return text; }
        set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(text, normalized, StringComparison.Ordinal))
            {
                return;
            }
            text = normalized;
            DisposeTextLayout();
        }
    }
    public bool TextEditing;
    public int TextCaretIndex;
    public int TextSelectionAnchor;
    public int TextCompositionStart = -1;
    public int TextCompositionLength;
    public bool TextCaretVisible = true;
    public readonly List<GpuPoint> Points = new List<GpuPoint>();

    public void AddPoint(GpuPoint point)
    {
        Points.Add(point);
        drawingPointsCache = null;
    }

    public void SetPoints(IEnumerable<GpuPoint> points)
    {
        Points.Clear();
        if (points != null)
        {
            Points.AddRange(points);
        }
        drawingPointsCache = null;
    }

    public GpuPoint[] GetDrawingPoints()
    {
        if (drawingPointsCache == null || drawingPointsCache.Length != Points.Count)
        {
            drawingPointsCache = Points.ToArray();
        }
        return drawingPointsCache;
    }

    public void Translate(float dx, float dy)
    {
        Start = new GpuPoint(Start.X + dx, Start.Y + dy);
        End = new GpuPoint(End.X + dx, End.Y + dy);
        for (int i = 0; i < Points.Count; i++)
        {
            GpuPoint p = Points[i];
            Points[i] = new GpuPoint(p.X + dx, p.Y + dy);
        }
        drawingPointsCache = null;
    }

    public TextLayoutCache GetTextLayout(float em, float maxWidth, float maxHeight)
    {
        if (Tool != ToolMode.Text)
        {
            return null;
        }
        if (textLayoutCache != null &&
            string.Equals(textLayoutCache.Text, text, StringComparison.Ordinal) &&
            Math.Abs(textLayoutCache.Em - Math.Max(1f, em)) < 0.001f &&
            Math.Abs(textLayoutCache.MaxWidth - Math.Max(1f, maxWidth)) < 0.001f &&
            Math.Abs(textLayoutCache.MaxHeight - Math.Max(1f, maxHeight)) < 0.001f)
        {
            return textLayoutCache;
        }
        DisposeTextLayout();
        textLayoutCache = new TextLayoutCache(Text ?? string.Empty, em, maxWidth, maxHeight);
        return textLayoutCache;
    }

    public void DisposeTextLayout()
    {
        if (textLayoutCache != null)
        {
            textLayoutCache.Dispose();
            textLayoutCache = null;
        }
    }
}

internal sealed class TextLayoutCache : IDisposable
{
    private readonly IntPtr format;
    private readonly IntPtr layout;
    private readonly string text;
    private readonly float em;
    private readonly float maxWidth;
    private readonly float maxHeight;
    private readonly GpuRect bounds;
    private bool disposed;

    public TextLayoutCache(string text, float em)
        : this(text, em, 100000f, 10000f)
    {
    }

    public TextLayoutCache(string text, float em, float maxWidth, float maxHeight)
    {
        this.text = text ?? string.Empty;
        this.em = Math.Max(1f, em);
        this.maxWidth = Math.Max(1f, maxWidth);
        this.maxHeight = Math.Max(1f, maxHeight);
        IntPtr factory = DWriteApi.GetSharedFactory();
        format = DWriteApi.CreateTextFormat(factory, AppStyles.UiFontName, this.em);
        layout = DWriteApi.CreateTextLayout(factory, this.text, format, this.maxWidth, this.maxHeight);
        DWriteApi.TextMetrics metrics = DWriteApi.GetMetrics(layout);
        bounds = new GpuRect(metrics.left, metrics.top, Math.Max(1f, metrics.widthIncludingTrailingWhitespace), Math.Max(1f, metrics.height));
    }

    public string Text
    {
        get { return text; }
    }

    public float Em
    {
        get { return em; }
    }

    public float MaxWidth
    {
        get { return maxWidth; }
    }

    public float MaxHeight
    {
        get { return maxHeight; }
    }

    public GpuRect Bounds
    {
        get { return bounds; }
    }

    public IntPtr Layout
    {
        get
        {
            ThrowIfDisposed();
            return layout;
        }
    }

    public TextHitResult HitTestPoint(GpuPoint point)
    {
        ThrowIfDisposed();
        return DWriteApi.HitTestPoint(layout, point.X, point.Y);
    }

    public GpuPoint HitTestTextPosition(int textPosition, bool trailing)
    {
        ThrowIfDisposed();
        return DWriteApi.HitTestTextPosition(layout, textPosition, trailing);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        IntPtr localLayout = layout;
        IntPtr localFormat = format;
        ComUtil.Release(ref localLayout);
        ComUtil.Release(ref localFormat);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException("TextLayoutCache");
        }
    }
}

internal static class AppStyles
{
    public static readonly string UiFontName = Win32Api.GetSystemMessageFontName();
    public const int ToolbarHeight = 82;
    public const int ToolbarButtonTop = 23;
    public const int ToolbarButtonSide = 36;
    public const int ToolbarButtonGap = 6;
    public const int ToolbarStartX = 24;
    public const int ToolbarSeparatorTop = 27;
    public const int ToolbarSeparatorHeight = 28;
    public const float DefaultStrokeWidth = 4f;

    public static readonly Rgba AppBack = Rgba.FromRgb(18, 18, 18);
    public static readonly Rgba CanvasBack = Rgba.FromRgb(24, 24, 24);
    public static readonly Rgba ToolbarSurface = Rgba.White;
    public static readonly Rgba ToolbarBorder = Rgba.FromRgb(232, 235, 239);
    public static readonly Rgba ToolbarSeparator = Rgba.FromRgb(226, 230, 235);
    public static readonly Rgba ToolbarShadow = Rgba.FromArgb(32, 0, 0, 0);
    public static readonly Rgba ToolbarIcon = Rgba.FromRgb(31, 35, 40);
    public static readonly Rgba ToolbarSelectedBack = Rgba.FromRgb(229, 242, 255);
    public static readonly Rgba FieldBack = Rgba.FromRgb(248, 250, 252);
    public static readonly Rgba MutedText = Rgba.FromRgb(94, 101, 112);
    public static readonly Rgba Accent = Rgba.FromRgb(0, 112, 224);
    public static readonly Rgba SaveAccent = Rgba.FromRgb(0, 172, 111);
    public static readonly Rgba CancelAccent = Rgba.FromRgb(255, 78, 78);
    public static readonly Rgba DefaultStroke = Rgba.FromRgb(255, 78, 78);
    public static readonly Rgba OverlayShade = Rgba.FromArgb(110, 0, 0, 0);

    public static readonly Rgba[] Palette = new Rgba[]
    {
        Rgba.FromRgb(255, 78, 78),
        Rgba.FromRgb(0, 122, 255),
        Rgba.FromRgb(39, 174, 96),
        Rgba.FromRgb(255, 193, 7),
        Rgba.FromRgb(45, 48, 53),
        Rgba.White
    };

    public static readonly float[] StrokeWidths = new float[]
    {
        2f,
        4f,
        8f,
        12f
    };
}
