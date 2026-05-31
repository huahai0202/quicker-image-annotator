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
    Browse,
    Clear,
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
    public const string SaveDirectory = "\u4fdd\u5b58\u76ee\u5f55";
    public const string EmptyOutputDirectoryHintPrefix = "\u7559\u7a7a\uff1a\u672c\u5730\u56fe\u7247\u5b58\u539f\u76ee\u5f55\uff0c\u526a\u8d34\u677f\u56fe\u7247\u5b58 ";
    public const string Browse = "\u6d4f\u89c8...";
    public const string Clear = "\u6e05\u7a7a";
    public const string Cancel = "\u53d6\u6d88";
    public const string Save = "\u4fdd\u5b58";
    public const string SelectOutputDirectory = "\u9009\u62e9\u6807\u6ce8\u4fdd\u5b58\u76ee\u5f55";
    public const string Rect = "\u77e9\u5f62";
    public const string Ellipse = "\u692d\u5706";
    public const string Arrow = "\u7bad\u5934";
    public const string Pen = "\u753b\u7b14";
    public const string Mosaic = "\u9a6c\u8d5b\u514b";
    public const string Text = "\u6587\u5b57";
    public const string Undo = "\u64a4\u9500";
    public const string Fit = "\u9002\u5408\u7a97\u53e3";
    public const string Pin = "\u7f6e\u9876";
    public const string Settings = "\u8bbe\u7f6e";
    public const string ClearConfirmTitle = "\u6e05\u7a7a\u6807\u6ce8";
    public const string ClearConfirmMessage = "\u786e\u5b9a\u8981\u6e05\u7a7a\u5f53\u524d\u6807\u6ce8\u5185\u5bb9\u5417\uff1f\u6b64\u64cd\u4f5c\u65e0\u6cd5\u64a4\u9500\u3002";
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
    public static readonly Rgba Black = new Rgba(255, 0, 0, 0);

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

    public ToolMode Tool;
    public GpuPoint Start;
    public GpuPoint End;
    public Rgba Stroke = AppStyles.DefaultStroke;
    public float StrokeWidth = AppStyles.DefaultStrokeWidth;
    public string Text = string.Empty;
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
}

internal static class AppStyles
{
    public const string UiFontName = "Microsoft YaHei UI";
    public const int ToolbarHeight = 82;
    public const int ToolbarButtonTop = 23;
    public const int ToolbarButtonSide = 36;
    public const int ToolbarButtonGap = 6;
    public const int ToolbarStartX = 24;
    public const int ToolbarSeparatorTop = 27;
    public const int ToolbarSeparatorHeight = 28;
    public const float DefaultStrokeWidth = 4f;
    public const int AnimationFrameMilliseconds = 16;

    public static readonly Rgba AppBack = Rgba.FromRgb(18, 18, 18);
    public static readonly Rgba CanvasBack = Rgba.FromRgb(24, 24, 24);
    public static readonly Rgba ToolbarSurface = Rgba.White;
    public static readonly Rgba ToolbarBorder = Rgba.FromRgb(232, 235, 239);
    public static readonly Rgba ToolbarSeparator = Rgba.FromRgb(226, 230, 235);
    public static readonly Rgba ToolbarShadow = Rgba.FromArgb(32, 0, 0, 0);
    public static readonly Rgba ToolbarIcon = Rgba.FromRgb(31, 35, 40);
    public static readonly Rgba ToolbarDisabled = Rgba.FromRgb(170, 176, 184);
    public static readonly Rgba ToolbarSelectedBack = Rgba.FromRgb(229, 242, 255);
    public static readonly Rgba ToolbarHoverBack = Rgba.FromRgb(245, 247, 250);
    public static readonly Rgba ToolbarPressedBack = Rgba.FromRgb(232, 236, 242);
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
