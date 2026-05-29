using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal sealed partial class AnnotatorForm : Form
{
    private readonly string imagePath;
    private readonly Bitmap baseImage;
    private readonly List<AnnotationItem> items = new List<AnnotationItem>();
    private readonly ModernToolbarPanel toolbar = new ModernToolbarPanel();
    private readonly BufferedCanvas canvas = new BufferedCanvas();
    private readonly Dictionary<ToolMode, Button> toolButtons = new Dictionary<ToolMode, Button>();
    private readonly Stack<AnnotationUndoAction> undoStack = new Stack<AnnotationUndoAction>();
    private readonly Panel toolOptionsPanel = new Panel();
    private readonly List<Button> colorButtons = new List<Button>();
    private readonly List<Button> widthButtons = new List<Button>();
    private readonly ToolTip toolbarToolTip = new ToolTip();
    private ModernIconButton topMostButton;
    private ToolMode currentTool = ToolMode.Rect;
    private bool drawing;
    private bool panning;
    private PointF startPoint;
    private PointF currentPoint;
    private Point lastPanPoint;
    private AnnotationItem currentPenItem;
    private int selectedItemIndex = -1;
    private bool movingSelection;
    private bool resizingSelection;
    private SelectionHandle activeSelectionHandle = SelectionHandle.None;
    private PointF lastMovePoint;
    private PointF moveStartPoint;
    private PointF moveCurrentPoint;
    private bool selectionMoved;
    private AnnotationSnapshot selectionEditStartState;
    private RectangleF selectionEditStartBounds;
    private Bitmap displayCache;
    private Size displayCacheSize = Size.Empty;
    private Bitmap moveBackgroundCache;
    private Size moveBackgroundCacheSize = Size.Empty;
    private Icon windowIcon;
    private float zoomFactor = 1f;
    private PointF viewOffset = PointF.Empty;
    private readonly Timer cacheRefreshTimer = new Timer();
    private readonly AnimationFrameScheduler canvasRenderScheduler;
    private readonly ThrottledAction imePositionThrottle;
    private bool suspendDisplayCache;
    private bool annotationsChanged = true;
    private Color strokeColor = AppStyles.DefaultStroke;
    private float strokeWidth = 4f;
    private const int MaxCachePixels = 6000000;
    private const float MinAnnotationExtent = 2f;
    private const float SelectionHitTolerancePixels = 8f;
    private const float MoveSampleThresholdPixels = 0.5f;
    private const float PenSampleThresholdPixels = 0.9f;
    private bool inlineTextEditing;
    private InlineImeTextBox inlineTextBox;
    private string inlineText = string.Empty;
    private string inlineCompositionText = string.Empty;
    private bool inlineTextSelecting;
    private int inlineTextSelectionAnchor;
    private bool inlineCaretVisible;
    private readonly Timer inlineCaretTimer = new Timer();
    private PointF textInputPosition;
    private const int MosaicBlockSize = 18;

    private enum AnnotationUndoKind
    {
        Add,
        Delete,
        Transform
    }

    private enum SelectionHandle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left
    }

    private sealed class AnnotationSnapshot
    {
        public ToolMode Tool;
        public PointF Start;
        public PointF End;
        public Color StrokeColor;
        public float StrokeWidth;
        public PointF[] Points = new PointF[0];
        public string Text;
    }

    private sealed class AnnotationUndoAction
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

    private sealed class InlineImeTextBox : TextBox
    {
        private const int WmImeComposition = 0x010F;
        private const int WmImeEndComposition = 0x010E;
        private const int GcsCompReadStr = 0x0001;
        private const int GcsCompStr = 0x0008;

        public event EventHandler CompositionChanged;
        public string CompositionText = string.Empty;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmImeComposition)
            {
                SetCompositionText(GetCurrentCompositionText(Handle));
            }
            else if (m.Msg == WmImeEndComposition)
            {
                SetCompositionText(string.Empty);
            }
        }

        private void SetCompositionText(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(CompositionText, value, StringComparison.Ordinal))
            {
                return;
            }

            CompositionText = value;
            EventHandler handler = CompositionChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static string GetCurrentCompositionText(IntPtr handle)
        {
            string readingText = GetCompositionString(handle, GcsCompReadStr);
            if (!string.IsNullOrEmpty(readingText))
            {
                return readingText;
            }
            return GetCompositionString(handle, GcsCompStr);
        }

        private static string GetCompositionString(IntPtr handle, int kind)
        {
            IntPtr context = ImmGetContext(handle);
            if (context == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                int byteCount = ImmGetCompositionString(context, kind, null, 0);
                if (byteCount <= 0)
                {
                    return string.Empty;
                }

                byte[] buffer = new byte[byteCount];
                int copied = ImmGetCompositionString(context, kind, buffer, buffer.Length);
                if (copied <= 0)
                {
                    return string.Empty;
                }

                return Encoding.Unicode.GetString(buffer, 0, copied).TrimEnd('\0');
            }
            finally
            {
                ImmReleaseContext(handle, context);
            }
        }

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
        private static extern int ImmGetCompositionString(IntPtr hIMC, int dwIndex, byte[] lpBuf, int dwBufLen);
    }

    public AnnotatorForm(string path, Bitmap image)
    {
        imagePath = path;
        baseImage = image;
        canvasRenderScheduler = new AnimationFrameScheduler(InvalidateCanvasNow);
        imePositionThrottle = AppUtilities.Throttle(PositionImeHostAtCaret, AppStyles.AnimationFrameMilliseconds);

        Text = "图片标注";
        windowIcon = LoadAppIcon();
        if (windowIcon != null)
        {
            Icon = windowIcon;
        }
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Normal;
        Size = GetInitialWindowSize(image);
        MinimumSize = new Size(640, 420);
        BackColor = AppStyles.AppBackground;
        Font = new Font(AppStyles.UiFontName, 9f);
        KeyPreview = true;
        DoubleBuffered = true;

        toolbar.Dock = DockStyle.Top;
        toolbar.Height = AppStyles.ToolbarHeight;
        toolbar.Padding = Padding.Empty;
        toolbar.BackColor = AppStyles.AppBackground;
        Controls.Add(toolbar);

        canvas.Dock = DockStyle.Fill;
        canvas.BackColor = AppStyles.CanvasBackground;
        canvas.Cursor = Cursors.Cross;
        canvas.Paint += Canvas_Paint;
        canvas.MouseDown += Canvas_MouseDown;
        canvas.MouseMove += Canvas_MouseMove;
        canvas.MouseUp += Canvas_MouseUp;
        canvas.MouseWheel += Canvas_MouseWheel;
        canvas.Resize += delegate
        {
            ResetDisplayCache();
            imePositionThrottle.Invoke();
            RequestCanvasRender();
        };
        Controls.Add(canvas);

        toolbarToolTip.InitialDelay = 350;
        toolbarToolTip.ReshowDelay = 120;
        toolbarToolTip.AutoPopDelay = 4000;

        BuildToolbar();

        cacheRefreshTimer.Interval = 140;
        cacheRefreshTimer.Tick += delegate
        {
            cacheRefreshTimer.Stop();
            suspendDisplayCache = false;
            ResetDisplayCache();
            RequestCanvasRender();
        };
        inlineCaretTimer.Interval = 500;
        inlineCaretTimer.Tick += delegate
        {
            if (!inlineTextEditing)
            {
                inlineCaretTimer.Stop();
                return;
            }
            inlineCaretVisible = !inlineCaretVisible;
            RequestCanvasRender();
        };
        KeyDown += AnnotatorForm_KeyDown;
    }

    private static Size GetInitialWindowSize(Bitmap image)
    {
        Rectangle work = Screen.PrimaryScreen.WorkingArea;
        int maxW = Math.Max(640, (int)(work.Width * 0.72));
        int maxH = Math.Max(420, (int)(work.Height * 0.78));
        int minW = 720;
        int minH = 520;

        float scale = Math.Min((float)(maxW - 40) / image.Width, (float)(maxH - 84) / image.Height);
        scale = Math.Min(1f, Math.Max(0.25f, scale));

        int width = Math.Max(minW, Math.Min(maxW, (int)Math.Round(image.Width * scale) + 40));
        int height = Math.Max(minH, Math.Min(maxH, (int)Math.Round(image.Height * scale) + 84));
        return new Size(width, height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (baseImage != null)
            {
                baseImage.Dispose();
            }
            cacheRefreshTimer.Stop();
            cacheRefreshTimer.Dispose();
            inlineCaretTimer.Stop();
            inlineCaretTimer.Dispose();
            canvasRenderScheduler.Dispose();
            imePositionThrottle.Dispose();
            toolbarToolTip.Dispose();
            if (windowIcon != null)
            {
                windowIcon.Dispose();
                windowIcon = null;
            }
            ResetMoveBackgroundCache();
            ResetDisplayCache();
        }
        base.Dispose(disposing);
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }

            string exePath = Application.ExecutablePath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                return Icon.ExtractAssociatedIcon(exePath);
            }
        }
        catch
        {
        }

        return null;
    }

    private static float ClampZoom(float value)
    {
        return Math.Max(0.25f, Math.Min(8f, value));
    }

    private void ResetZoom()
    {
        zoomFactor = 1f;
        viewOffset = PointF.Empty;
        suspendDisplayCache = false;
        cacheRefreshTimer.Stop();
        ResetDisplayCache();
        RequestCanvasRender();
    }

    private void RequestCanvasRender()
    {
        canvasRenderScheduler.RequestFrame();
    }

    private void InvalidateCanvasNow()
    {
        if (!IsDisposed && canvas.IsHandleCreated)
        {
            canvas.Invalidate();
        }
    }

    private RectangleF GetView()
    {
        float cw = Math.Max(1, canvas.ClientSize.Width);
        float ch = Math.Max(1, canvas.ClientSize.Height);
        float fitScale = Math.Min(cw / baseImage.Width, ch / baseImage.Height);
        float scale = fitScale * zoomFactor;
        float w = baseImage.Width * scale;
        float h = baseImage.Height * scale;
        return new RectangleF((cw - w) / 2f + viewOffset.X, (ch - h) / 2f + viewOffset.Y, w, h);
    }

    private float GetScale()
    {
        RectangleF view = GetView();
        return view.Width / baseImage.Width;
    }

    private void ResetDisplayCache()
    {
        if (displayCache != null)
        {
            displayCache.Dispose();
            displayCache = null;
            displayCacheSize = Size.Empty;
        }
    }

    private void ResetMoveBackgroundCache()
    {
        if (moveBackgroundCache != null)
        {
            moveBackgroundCache.Dispose();
            moveBackgroundCache = null;
            moveBackgroundCacheSize = Size.Empty;
        }
    }

    private void MarkAnnotationsChanged()
    {
        annotationsChanged = true;
    }

    private void SuspendDisplayCacheForInteraction()
    {
        suspendDisplayCache = true;
        cacheRefreshTimer.Stop();
    }

    private void ResumeDisplayCacheAfterInteraction(bool changed)
    {
        suspendDisplayCache = false;
        cacheRefreshTimer.Stop();
        ResetMoveBackgroundCache();
        if (changed)
        {
            MarkAnnotationsChanged();
        }
    }

    private float CanvasPixelsToImageDistance(float pixels)
    {
        float scale = GetScale();
        if (scale <= 0)
        {
            return pixels;
        }
        return Math.Max(0.01f, pixels / scale);
    }

    private bool CanUseDisplayCache(RectangleF view)
    {
        if (view.Width <= 0 || view.Height <= 0)
        {
            return false;
        }
        return (double)view.Width * view.Height <= MaxCachePixels;
    }

    private void EnsureDisplayCache(RectangleF view)
    {
        var targetSize = new Size(
            Math.Max(1, (int)Math.Round(view.Width)),
            Math.Max(1, (int)Math.Round(view.Height)));

        if (displayCache != null && displayCacheSize == targetSize && !annotationsChanged)
        {
            return;
        }

        ResetDisplayCache();
        displayCache = new Bitmap(targetSize.Width, targetSize.Height);
        displayCacheSize = targetSize;
        annotationsChanged = false;

        float displayScale = targetSize.Width / (float)baseImage.Width;

        using (Graphics g = Graphics.FromImage(displayCache))
        {
            g.Clear(AppStyles.CanvasBackground);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(baseImage, new Rectangle(0, 0, targetSize.Width, targetSize.Height));

            if (items.Count > 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(displayScale, displayScale, MatrixOrder.Prepend);
                DrawAnnotations(g, displayScale, -1);
            }
        }
    }

    private void EnsureMoveBackgroundCache(RectangleF view)
    {
        if (!HasSelectedItem() || !CanUseDisplayCache(view))
        {
            ResetMoveBackgroundCache();
            return;
        }

        var targetSize = new Size(
            Math.Max(1, (int)Math.Round(view.Width)),
            Math.Max(1, (int)Math.Round(view.Height)));

        if (moveBackgroundCache != null && moveBackgroundCacheSize == targetSize)
        {
            return;
        }

        ResetMoveBackgroundCache();
        moveBackgroundCache = new Bitmap(targetSize.Width, targetSize.Height);
        moveBackgroundCacheSize = targetSize;

        float displayScale = targetSize.Width / (float)baseImage.Width;
        using (Graphics g = Graphics.FromImage(moveBackgroundCache))
        {
            g.Clear(AppStyles.CanvasBackground);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(baseImage, new Rectangle(0, 0, targetSize.Width, targetSize.Height));

            if (items.Count > 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(displayScale, displayScale, MatrixOrder.Prepend);
                DrawAnnotations(g, displayScale, selectedItemIndex);
            }
        }
    }

    private void DrawAnnotations(Graphics g, float scale, int skipIndex)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (i == skipIndex)
            {
                continue;
            }

            DrawItem(g, items[i], scale);
        }
    }

    private PointF ToImagePoint(Point point)
    {
        RectangleF view = GetView();
        float scale = GetScale();
        float x = (point.X - view.X) / scale;
        float y = (point.Y - view.Y) / scale;
        x = Math.Max(0, Math.Min(baseImage.Width, x));
        y = Math.Max(0, Math.Min(baseImage.Height, y));
        return new PointF(x, y);
    }

    private void Canvas_MouseWheel(object sender, MouseEventArgs e)
    {
        PointF anchorImage = ToImagePoint(e.Location);
        float wheelSteps = e.Delta / 120f;
        float step = (float)Math.Pow(1.12, wheelSteps);
        SetZoomKeepingAnchor(ClampZoom(zoomFactor * step), e.Location, anchorImage);
        if (!suspendDisplayCache)
        {
            ResetDisplayCache();
        }
        suspendDisplayCache = true;
        cacheRefreshTimer.Stop();
        cacheRefreshTimer.Start();
        RequestCanvasRender();
    }

    private void SetZoomKeepingAnchor(float newZoom, Point screenPoint, PointF imagePoint)
    {
        zoomFactor = ClampZoom(newZoom);
        RectangleF view = GetView();
        float scale = GetScale();
        viewOffset.X += screenPoint.X - (view.X + imagePoint.X * scale);
        viewOffset.Y += screenPoint.Y - (view.Y + imagePoint.Y * scale);
        PositionImeHostAtCaret();
    }

    private void Canvas_Paint(object sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        RectangleF view = GetView();
        int viewX = (int)Math.Round(view.X);
        int viewY = (int)Math.Round(view.Y);
        float scale = view.Width / baseImage.Width;
        bool editingSelection = IsEditingSelection();
        bool drewMovingBackground = false;
        if (editingSelection)
        {
            EnsureMoveBackgroundCache(view);
            if (moveBackgroundCache != null)
            {
                scale = (float)moveBackgroundCache.Width / baseImage.Width;
                e.Graphics.DrawImageUnscaled(moveBackgroundCache, viewX, viewY);
                drewMovingBackground = true;
            }
        }

        if (!drewMovingBackground && !suspendDisplayCache && CanUseDisplayCache(view))
        {
            EnsureDisplayCache(view);
            scale = (float)displayCache.Width / baseImage.Width;
            e.Graphics.DrawImageUnscaled(displayCache, viewX, viewY);
        }
        else if (!drewMovingBackground)
        {
            DrawVisibleImageRegion(e.Graphics, view, scale);

            GraphicsState slowState = e.Graphics.Save();
            e.Graphics.TranslateTransform(viewX, viewY, MatrixOrder.Prepend);
            e.Graphics.ScaleTransform(scale, scale, MatrixOrder.Prepend);
            DrawAnnotations(e.Graphics, scale, editingSelection ? selectedItemIndex : -1);
            e.Graphics.Restore(slowState);
        }

        GraphicsState state = e.Graphics.Save();
        e.Graphics.TranslateTransform(viewX, viewY, MatrixOrder.Prepend);
        e.Graphics.ScaleTransform(scale, scale, MatrixOrder.Prepend);

        if (drawing)
        {
            if (currentTool == ToolMode.Pen && currentPenItem != null && currentPenItem.Points.Count > 1)
            {
                DrawItem(e.Graphics, currentPenItem, scale);
            }
            else if (currentTool != ToolMode.Pen)
            {
                var preview = new AnnotationItem();
                preview.Tool = currentTool;
                preview.Start = startPoint;
                preview.End = currentPoint;
                preview.StrokeColor = strokeColor;
                preview.StrokeWidth = strokeWidth;
                DrawItem(e.Graphics, preview, scale);
            }
        }

        if (editingSelection)
        {
            AnnotationItem preview = GetSelectionPreviewItem();
            if (preview != null)
            {
                DrawItem(e.Graphics, preview, scale);
                DrawSelection(e.Graphics, preview, scale);
            }
        }
        else if (HasSelectedItem())
        {
            DrawSelection(e.Graphics, items[selectedItemIndex], scale);
        }

        if (inlineTextEditing)
        {
            DrawInlineTextInput(e.Graphics, scale);
        }

        e.Graphics.Restore(state);
    }

    private void DrawVisibleImageRegion(Graphics g, RectangleF view, float scale)
    {
        RectangleF visibleCanvas = new RectangleF(0, 0, canvas.ClientSize.Width, canvas.ClientSize.Height);
        RectangleF clippedView = RectangleF.Intersect(view, visibleCanvas);
        if (clippedView.Width <= 0 || clippedView.Height <= 0)
        {
            return;
        }

        RectangleF src = new RectangleF(
            (clippedView.X - view.X) / scale,
            (clippedView.Y - view.Y) / scale,
            clippedView.Width / scale,
            clippedView.Height / scale);

        g.DrawImage(baseImage, clippedView, src, GraphicsUnit.Pixel);
    }

    private void DrawItem(Graphics g, AnnotationItem item, float scale)
    {
        if (item == null || scale <= 0)
        {
            return;
        }

        Color itemColor = item.StrokeColor.IsEmpty ? strokeColor : item.StrokeColor;
        float itemWidth = item.StrokeWidth > 0 ? item.StrokeWidth : strokeWidth;

        if (item.Tool == ToolMode.Arrow)
        {
            DrawFilledArrow(g, item, itemColor, itemWidth);
            return;
        }

        using (var pen = new Pen(itemColor, Math.Max(0.1f, itemWidth)))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;

            if (item.Tool == ToolMode.Rect)
            {
                float x = Math.Min(item.Start.X, item.End.X);
                float y = Math.Min(item.Start.Y, item.End.Y);
                float w = Math.Abs(item.End.X - item.Start.X);
                float h = Math.Abs(item.End.Y - item.Start.Y);
                g.DrawRectangle(pen, x, y, w, h);
            }
            else if (item.Tool == ToolMode.Ellipse)
            {
                float x = Math.Min(item.Start.X, item.End.X);
                float y = Math.Min(item.Start.Y, item.End.Y);
                float w = Math.Abs(item.End.X - item.Start.X);
                float h = Math.Abs(item.End.Y - item.Start.Y);
                g.DrawEllipse(pen, x, y, w, h);
            }
            else if (item.Tool == ToolMode.Pen && item.Points.Count > 1)
            {
                g.DrawLines(pen, item.GetDrawingPoints());
            }
            else if (item.Tool == ToolMode.Text && !string.IsNullOrEmpty(item.Text))
            {
                float fontSize = item.StrokeWidth * 6f;
                using (Font font = new Font(AppStyles.UiFontName, fontSize, FontStyle.Bold))
                using (Brush textBrush = new SolidBrush(item.StrokeColor))
                {
                    g.DrawString(item.Text, font, textBrush, item.Start);
                }
            }
            else if (item.Tool == ToolMode.Mosaic)
            {
                DrawMosaic(g, item);
            }
        }
    }

    private static void DrawFilledArrow(Graphics g, AnnotationItem item, Color color, float width)
    {
        PointF[] points = BuildFilledArrowPoints(item, width);
        if (points.Length == 0)
        {
            return;
        }

        using (Brush brush = new SolidBrush(color))
        {
            g.FillPolygon(brush, points);
        }
    }

    private static PointF[] BuildFilledArrowPoints(AnnotationItem item, float width)
    {
        float dx = item.End.X - item.Start.X;
        float dy = item.End.Y - item.Start.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length < MinAnnotationExtent)
        {
            return new PointF[0];
        }

        float ux = dx / length;
        float uy = dy / length;
        float nx = -uy;
        float ny = ux;

        float headLength = Math.Min(length * 0.55f, Math.Max(14f, width * 5.5f));
        float tailHalf = Math.Max(0.7f, width * 0.22f);
        float neckHalf = Math.Max(1.2f, width * 0.65f);
        float headHalf = Math.Max(neckHalf * 2.2f, width * 2.4f);

        PointF neck = new PointF(
            item.End.X - ux * headLength,
            item.End.Y - uy * headLength);

        return new PointF[]
        {
            OffsetPoint(item.Start, nx, ny, -tailHalf),
            OffsetPoint(neck, nx, ny, -neckHalf),
            OffsetPoint(neck, nx, ny, -headHalf),
            item.End,
            OffsetPoint(neck, nx, ny, headHalf),
            OffsetPoint(neck, nx, ny, neckHalf),
            OffsetPoint(item.Start, nx, ny, tailHalf)
        };
    }

    private static PointF OffsetPoint(PointF point, float normalX, float normalY, float distance)
    {
        return new PointF(point.X + normalX * distance, point.Y + normalY * distance);
    }

    private void DrawSelection(Graphics g, AnnotationItem item, float scale)
    {
        RectangleF bounds = GetSelectionFrameBounds(item, scale);
        if (bounds.IsEmpty)
        {
            return;
        }

        float safeScale = Math.Max(0.001f, scale);
        float lineWidth = Math.Max(0.1f, 1.25f / safeScale);
        using (Pen pen = new Pen(AppStyles.OptionSelectedForeground, lineWidth))
        using (Brush handleBrush = new SolidBrush(Color.White))
        using (Pen handlePen = new Pen(AppStyles.OptionSelectedForeground, lineWidth))
        {
            pen.DashStyle = DashStyle.Dash;
            g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);

            if (!CanResizeAnnotation(item))
            {
                return;
            }

            float handleSize = Math.Max(3f, 7f / safeScale);
            DrawSelectionHandle(g, handleBrush, handlePen, bounds.Left, bounds.Top, handleSize);
            DrawSelectionHandle(g, handleBrush, handlePen, bounds.Left + bounds.Width / 2f, bounds.Top, handleSize);
            DrawSelectionHandle(g, handleBrush, handlePen, bounds.Right, bounds.Top, handleSize);
            DrawSelectionHandle(g, handleBrush, handlePen, bounds.Right, bounds.Top + bounds.Height / 2f, handleSize);
            DrawSelectionHandle(g, handleBrush, handlePen, bounds.Right, bounds.Bottom, handleSize);
            DrawSelectionHandle(g, handleBrush, handlePen, bounds.Left + bounds.Width / 2f, bounds.Bottom, handleSize);
            DrawSelectionHandle(g, handleBrush, handlePen, bounds.Left, bounds.Bottom, handleSize);
            DrawSelectionHandle(g, handleBrush, handlePen, bounds.Left, bounds.Top + bounds.Height / 2f, handleSize);
        }
    }

    private static void DrawSelectionHandle(Graphics g, Brush brush, Pen pen, float x, float y, float size)
    {
        RectangleF rect = new RectangleF(x - size / 2f, y - size / 2f, size, size);
        g.FillEllipse(brush, rect);
        g.DrawEllipse(pen, rect);
    }

    private RectangleF GetSelectionFrameBounds(AnnotationItem item)
    {
        return GetSelectionFrameBounds(item, GetScale());
    }

    private RectangleF GetSelectionFrameBounds(AnnotationItem item, float scale)
    {
        RectangleF bounds = GetItemBounds(item);
        if (bounds.IsEmpty)
        {
            return bounds;
        }

        float safeScale = Math.Max(0.001f, scale);
        float padding = Math.Max(2f, 4f / safeScale);
        bounds.Inflate(padding, padding);
        return bounds;
    }

    private static bool CanResizeAnnotation(AnnotationItem item)
    {
        return item != null && item.Tool != ToolMode.Text;
    }

    private SelectionHandle HitTestSelectionHandle(PointF point)
    {
        if (!HasSelectedItem())
        {
            return SelectionHandle.None;
        }

        AnnotationItem item = items[selectedItemIndex];
        if (!CanResizeAnnotation(item))
        {
            return SelectionHandle.None;
        }

        RectangleF bounds = GetSelectionFrameBounds(item);
        if (bounds.IsEmpty)
        {
            return SelectionHandle.None;
        }

        float radius = GetSelectionHandleHitRadius();
        SelectionHandle[] handles = new SelectionHandle[]
        {
            SelectionHandle.TopLeft,
            SelectionHandle.TopRight,
            SelectionHandle.BottomRight,
            SelectionHandle.BottomLeft,
            SelectionHandle.Top,
            SelectionHandle.Right,
            SelectionHandle.Bottom,
            SelectionHandle.Left
        };

        for (int i = 0; i < handles.Length; i++)
        {
            if (IsPointInSelectionHandle(point, GetSelectionHandlePoint(bounds, handles[i]), radius))
            {
                return handles[i];
            }
        }

        return SelectionHandle.None;
    }

    private float GetSelectionHandleHitRadius()
    {
        float scale = GetScale();
        if (scale <= 0)
        {
            return 8f;
        }

        return Math.Max(4f, 8f / scale);
    }

    private static bool IsPointInSelectionHandle(PointF point, PointF handlePoint, float radius)
    {
        float dx = point.X - handlePoint.X;
        float dy = point.Y - handlePoint.Y;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static PointF GetSelectionHandlePoint(RectangleF bounds, SelectionHandle handle)
    {
        switch (handle)
        {
            case SelectionHandle.TopLeft:
                return new PointF(bounds.Left, bounds.Top);
            case SelectionHandle.Top:
                return new PointF(bounds.Left + bounds.Width / 2f, bounds.Top);
            case SelectionHandle.TopRight:
                return new PointF(bounds.Right, bounds.Top);
            case SelectionHandle.Right:
                return new PointF(bounds.Right, bounds.Top + bounds.Height / 2f);
            case SelectionHandle.BottomRight:
                return new PointF(bounds.Right, bounds.Bottom);
            case SelectionHandle.Bottom:
                return new PointF(bounds.Left + bounds.Width / 2f, bounds.Bottom);
            case SelectionHandle.BottomLeft:
                return new PointF(bounds.Left, bounds.Bottom);
            case SelectionHandle.Left:
                return new PointF(bounds.Left, bounds.Top + bounds.Height / 2f);
            default:
                return PointF.Empty;
        }
    }

    private static Cursor GetSelectionHandleCursor(SelectionHandle handle)
    {
        switch (handle)
        {
            case SelectionHandle.TopLeft:
            case SelectionHandle.BottomRight:
                return Cursors.SizeNWSE;
            case SelectionHandle.TopRight:
            case SelectionHandle.BottomLeft:
                return Cursors.SizeNESW;
            case SelectionHandle.Top:
            case SelectionHandle.Bottom:
                return Cursors.SizeNS;
            case SelectionHandle.Left:
            case SelectionHandle.Right:
                return Cursors.SizeWE;
            default:
                return Cursors.Cross;
        }
    }

    private RectangleF GetItemBounds(AnnotationItem item)
    {
        if (item == null)
        {
            return RectangleF.Empty;
        }

        float itemWidth = item.StrokeWidth > 0 ? item.StrokeWidth : strokeWidth;
        RectangleF bounds;
        if (item.Tool == ToolMode.Rect || item.Tool == ToolMode.Ellipse || item.Tool == ToolMode.Mosaic)
        {
            bounds = NormalizeRect(item.Start, item.End);
        }
        else if (item.Tool == ToolMode.Arrow)
        {
            bounds = BoundsFromPoints(item.Start, item.End);
            bounds.Inflate(Math.Max(6f, itemWidth * 3f), Math.Max(6f, itemWidth * 3f));
        }
        else if (item.Tool == ToolMode.Pen)
        {
            bounds = GetPointsBounds(item.Points);
            bounds.Inflate(Math.Max(2f, itemWidth), Math.Max(2f, itemWidth));
        }
        else if (item.Tool == ToolMode.Text)
        {
            bounds = GetTextBounds(item);
        }
        else
        {
            bounds = BoundsFromPoints(item.Start, item.End);
        }

        if (bounds.Width < 1f)
        {
            bounds.Inflate(0.5f, 0f);
        }
        if (bounds.Height < 1f)
        {
            bounds.Inflate(0f, 0.5f);
        }
        return bounds;
    }

    private RectangleF GetTextBounds(AnnotationItem item)
    {
        string text = item.Text ?? string.Empty;
        float fontSize = Math.Max(1f, item.StrokeWidth * 6f);
        if (text.Length == 0)
        {
            return new RectangleF(item.Start.X, item.Start.Y, fontSize, fontSize);
        }

        using (Graphics g = canvas.CreateGraphics())
        using (Font font = new Font(AppStyles.UiFontName, fontSize, FontStyle.Bold))
        using (StringFormat format = CreateInlineTextFormat())
        {
            SizeF size = g.MeasureString(text, font, int.MaxValue, format);
            return new RectangleF(item.Start, size);
        }
    }

    private static RectangleF GetPointsBounds(List<PointF> points)
    {
        if (points == null || points.Count == 0)
        {
            return RectangleF.Empty;
        }

        float left = points[0].X;
        float right = points[0].X;
        float top = points[0].Y;
        float bottom = points[0].Y;
        for (int i = 1; i < points.Count; i++)
        {
            left = Math.Min(left, points[i].X);
            right = Math.Max(right, points[i].X);
            top = Math.Min(top, points[i].Y);
            bottom = Math.Max(bottom, points[i].Y);
        }
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static RectangleF BoundsFromPoints(PointF a, PointF b)
    {
        return RectangleF.FromLTRB(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Max(a.X, b.X),
            Math.Max(a.Y, b.Y));
    }

    private void DrawInlineTextInput(Graphics g, float scale)
    {
        float fontSize = strokeWidth * 6f;
        string text = inlineText ?? string.Empty;
        string composition = inlineCompositionText ?? string.Empty;
        using (Font font = new Font(AppStyles.UiFontName, fontSize, FontStyle.Bold))
        using (Brush textBrush = new SolidBrush(strokeColor))
        using (StringFormat format = CreateInlineTextFormat())
        {
            int selectionStart = GetInlineSelectionStart();
            int selectionLength = GetInlineSelectionLength();
            selectionStart = Math.Max(0, Math.Min(text.Length, selectionStart));
            selectionLength = Math.Max(0, Math.Min(text.Length - selectionStart, selectionLength));
            int caretIndex = GetInlineCaretIndex();
            float textHeight = MeasureInlineTextHeight(g, font, format);

            if (composition.Length > 0)
            {
                int insertionIndex = selectionLength > 0 ? selectionStart : caretIndex;
                insertionIndex = Math.Max(0, Math.Min(text.Length, insertionIndex));
                int replaceEnd = selectionLength > 0 ? selectionStart + selectionLength : insertionIndex;
                string beforeText = text.Substring(0, insertionIndex);
                string afterText = text.Substring(replaceEnd);
                float beforeWidth = MeasureInlineTextWidth(g, font, format, beforeText, beforeText.Length);
                float compositionWidth = MeasureInlineTextWidth(g, font, format, composition, composition.Length);
                float compositionX = textInputPosition.X + beforeWidth;

                if (beforeText.Length > 0)
                {
                    g.DrawString(beforeText, font, textBrush, textInputPosition);
                }
                g.DrawString(composition, font, textBrush, new PointF(compositionX, textInputPosition.Y));
                if (afterText.Length > 0)
                {
                    g.DrawString(afterText, font, textBrush, new PointF(compositionX + compositionWidth, textInputPosition.Y));
                }
                using (Pen compositionPen = new Pen(strokeColor, Math.Max(0.8f, 1f / Math.Max(0.001f, scale))))
                {
                    float underlineY = textInputPosition.Y + textHeight - Math.Max(1.2f, 2f / Math.Max(0.001f, scale));
                    g.DrawLine(compositionPen, compositionX, underlineY, compositionX + Math.Max(1f, compositionWidth), underlineY);
                }

                if (inlineCaretVisible)
                {
                    DrawInlineCaret(g, compositionX + compositionWidth, textHeight, scale);
                }
                return;
            }

            DrawCommittedInlineText(g, font, format, textBrush, text, selectionStart, selectionLength, textHeight);

            if (inlineCaretVisible && selectionLength == 0)
            {
                float caretX = textInputPosition.X + MeasureInlineTextWidth(g, font, format, text, caretIndex);
                DrawInlineCaret(g, caretX, textHeight, scale);
            }
        }
    }

    private void DrawCommittedInlineText(Graphics g, Font font, StringFormat format, Brush textBrush, string text, int selectionStart, int selectionLength, float textHeight)
    {
        if (selectionLength > 0)
        {
            float selectionX = textInputPosition.X + MeasureInlineTextWidth(g, font, format, text, selectionStart);
            string selectedText = text.Substring(selectionStart, selectionLength);
            float selectionWidth = MeasureInlineTextWidth(g, font, format, selectedText, selectedText.Length);
            using (Brush selectionBrush = new SolidBrush(Color.FromArgb(190, AppStyles.OptionSelectedForeground)))
            {
                g.FillRectangle(selectionBrush, selectionX, textInputPosition.Y, Math.Max(1f, selectionWidth), textHeight);
            }
        }

        if (text.Length > 0)
        {
            g.DrawString(text, font, textBrush, textInputPosition);
            if (selectionLength > 0)
            {
                string selectedText = text.Substring(selectionStart, selectionLength);
                float selectionX = textInputPosition.X + MeasureInlineTextWidth(g, font, format, text, selectionStart);
                using (Brush selectedTextBrush = new SolidBrush(Color.White))
                {
                    g.DrawString(selectedText, font, selectedTextBrush, new PointF(selectionX, textInputPosition.Y));
                }
            }
        }
    }

    private void DrawInlineCaret(Graphics g, float caretX, float textHeight, float scale)
    {
        float caretWidth = Math.Max(1f, 1f / Math.Max(0.001f, scale));
        using (Pen caretPen = new Pen(strokeColor, caretWidth))
        {
            g.DrawLine(caretPen, caretX, textInputPosition.Y, caretX, textInputPosition.Y + textHeight);
        }
    }

    private static float MeasureInlineTextWidth(Graphics g, Font font, StringFormat format, string text, int count)
    {
        if (string.IsNullOrEmpty(text) || count <= 0)
        {
            return 0f;
        }

        count = Math.Min(count, text.Length);
        string measuredText = text.Substring(0, count);
        using (StringFormat measureFormat = (StringFormat)format.Clone())
        {
            measureFormat.SetMeasurableCharacterRanges(new CharacterRange[] { new CharacterRange(0, measuredText.Length) });
            RectangleF layout = new RectangleF(0, 0, 100000, 10000);
            Region[] regions = g.MeasureCharacterRanges(measuredText, font, layout, measureFormat);
            try
            {
                if (regions.Length > 0)
                {
                    RectangleF bounds = regions[0].GetBounds(g);
                    if (bounds.Right > 0)
                    {
                        return bounds.Right;
                    }
                }
            }
            finally
            {
                for (int i = 0; i < regions.Length; i++)
                {
                    regions[i].Dispose();
                }
            }
        }

        return g.MeasureString(measuredText, font, int.MaxValue, format).Width;
    }

    private static float MeasureInlineTextHeight(Graphics g, Font font, StringFormat format)
    {
        return g.MeasureString("M", font, int.MaxValue, format).Height;
    }

    private float MeasureInlineEditingWidth(Graphics g, Font font, StringFormat format, string text, string composition)
    {
        text = text ?? string.Empty;
        composition = composition ?? string.Empty;
        if (composition.Length == 0)
        {
            return Math.Max(font.Size, MeasureInlineTextWidth(g, font, format, text, text.Length));
        }

        int selectionStart = Math.Max(0, Math.Min(text.Length, GetInlineSelectionStart()));
        int selectionLength = Math.Max(0, Math.Min(text.Length - selectionStart, GetInlineSelectionLength()));
        int caretIndex = Math.Max(0, Math.Min(text.Length, GetInlineCaretIndex()));
        int insertionIndex = selectionLength > 0 ? selectionStart : caretIndex;
        int replaceEnd = selectionLength > 0 ? selectionStart + selectionLength : insertionIndex;
        float beforeWidth = MeasureInlineTextWidth(g, font, format, text, insertionIndex);
        float compositionWidth = MeasureInlineTextWidth(g, font, format, composition, composition.Length);
        string afterText = text.Substring(replaceEnd);
        float afterWidth = MeasureInlineTextWidth(g, font, format, afterText, afterText.Length);
        return Math.Max(font.Size, beforeWidth + compositionWidth + afterWidth);
    }

    private static StringFormat CreateInlineTextFormat()
    {
        StringFormat format = new StringFormat(StringFormat.GenericTypographic);
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return format;
    }

    private void DrawMosaic(Graphics g, AnnotationItem item)
    {
        Rectangle rect = GetMosaicRect(item, baseImage.Width, baseImage.Height);
        if (rect.Width < 2 || rect.Height < 2)
        {
            return;
        }

        DrawMosaicBlocks(g, baseImage, rect);
    }

    private static RectangleF NormalizeRect(PointF a, PointF b)
    {
        float x = Math.Min(a.X, b.X);
        float y = Math.Min(a.Y, b.Y);
        float w = Math.Abs(a.X - b.X);
        float h = Math.Abs(a.Y - b.Y);
        return new RectangleF(x, y, w, h);
    }

    private static void ApplyMosaic(Bitmap bitmap, AnnotationItem item)
    {
        Rectangle rect = GetMosaicRect(item, bitmap.Width, bitmap.Height);
        if (rect.Width < 2 || rect.Height < 2)
        {
            return;
        }

        using (Graphics g = Graphics.FromImage(bitmap))
        {
            DrawMosaicBlocks(g, bitmap, rect);
        }
    }

    private static Rectangle GetMosaicRect(AnnotationItem item, int width, int height)
    {
        RectangleF rf = NormalizeRect(item.Start, item.End);
        return Rectangle.Intersect(
            Rectangle.Round(rf),
            new Rectangle(0, 0, width, height));
    }

    private static void DrawMosaicBlocks(Graphics g, Bitmap sampleBitmap, Rectangle rect)
    {
        using (var brush = new SolidBrush(Color.Black))
        using (var overlay = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
        {
            for (int y = rect.Top; y < rect.Bottom; y += MosaicBlockSize)
            {
                for (int x = rect.Left; x < rect.Right; x += MosaicBlockSize)
                {
                    int w = Math.Min(MosaicBlockSize, rect.Right - x);
                    int h = Math.Min(MosaicBlockSize, rect.Bottom - y);
                    Color color = AverageColor(sampleBitmap, x, y, w, h);
                    brush.Color = Color.FromArgb(color.R / 2, color.G / 2, color.B / 2);
                    g.FillRectangle(brush, x, y, w, h);
                }
            }
            g.FillRectangle(overlay, rect);
        }
    }

    private static Color AverageColor(Bitmap bitmap, int x, int y, int w, int h)
    {
        long r = 0;
        long g = 0;
        long b = 0;
        long count = 0;
        int step = Math.Max(1, Math.Min(w, h) / 4);

        for (int yy = y; yy < y + h; yy += step)
        {
            for (int xx = x; xx < x + w; xx += step)
            {
                Color c = bitmap.GetPixel(xx, yy);
                r += c.R;
                g += c.G;
                b += c.B;
                count++;
            }
        }

        if (count == 0)
        {
            return Color.Gray;
        }
        return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
    }

    private static bool IsMeaningfulAnnotation(AnnotationItem item)
    {
        if (item == null)
        {
            return false;
        }

        if (item.Tool == ToolMode.Arrow)
        {
            return Distance(item.Start, item.End) >= MinAnnotationExtent;
        }

        if (item.Tool == ToolMode.Rect || item.Tool == ToolMode.Ellipse || item.Tool == ToolMode.Mosaic)
        {
            RectangleF rect = NormalizeRect(item.Start, item.End);
            return rect.Width >= MinAnnotationExtent && rect.Height >= MinAnnotationExtent;
        }

        if (item.Tool == ToolMode.Pen)
        {
            if (item.Points.Count < 2)
            {
                return false;
            }

            PointF first = item.Points[0];
            for (int i = 1; i < item.Points.Count; i++)
            {
                if (Distance(first, item.Points[i]) >= MinAnnotationExtent)
                {
                    return true;
                }
            }
            return false;
        }

        return true;
    }

    private static float Distance(PointF a, PointF b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private bool IsEditingSelection()
    {
        return HasSelectedItem() && (movingSelection || resizingSelection);
    }

    private AnnotationItem GetSelectionPreviewItem()
    {
        if (!HasSelectedItem())
        {
            return null;
        }

        AnnotationSnapshot snapshot = selectionEditStartState ?? CaptureAnnotation(items[selectedItemIndex]);
        AnnotationItem preview = CreateAnnotationItem(snapshot);
        PointF offset = GetMoveOffset();
        if (movingSelection)
        {
            preview.MoveBy(offset.X, offset.Y);
        }
        else if (resizingSelection)
        {
            ApplyResizeToAnnotation(preview, snapshot, selectionEditStartBounds, activeSelectionHandle, offset);
        }
        return preview;
    }

    private static AnnotationSnapshot CaptureAnnotation(AnnotationItem item)
    {
        if (item == null)
        {
            return null;
        }

        return new AnnotationSnapshot
        {
            Tool = item.Tool,
            Start = item.Start,
            End = item.End,
            StrokeColor = item.StrokeColor,
            StrokeWidth = item.StrokeWidth,
            Points = item.Points.ToArray(),
            Text = item.Text
        };
    }

    private static AnnotationItem CreateAnnotationItem(AnnotationSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return null;
        }

        var item = new AnnotationItem();
        ApplyAnnotationSnapshot(item, snapshot);
        return item;
    }

    private static void ApplyAnnotationSnapshot(AnnotationItem item, AnnotationSnapshot snapshot)
    {
        if (item == null || snapshot == null)
        {
            return;
        }

        item.Tool = snapshot.Tool;
        item.Start = snapshot.Start;
        item.End = snapshot.End;
        item.StrokeColor = snapshot.StrokeColor;
        item.StrokeWidth = snapshot.StrokeWidth;
        item.Text = snapshot.Text;
        item.SetPoints(snapshot.Points);
    }

    private bool RecordTransformUndo(AnnotationItem item, int index, AnnotationSnapshot before, AnnotationSnapshot after)
    {
        if (item == null || before == null || after == null || AnnotationSnapshotsEqual(before, after))
        {
            return false;
        }

        undoStack.Push(new AnnotationUndoAction(AnnotationUndoKind.Transform, item, index, before));
        MarkAnnotationsChanged();
        return true;
    }

    private static bool AnnotationSnapshotsEqual(AnnotationSnapshot a, AnnotationSnapshot b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        if (a.Tool != b.Tool ||
            !SamePoint(a.Start, b.Start) ||
            !SamePoint(a.End, b.End) ||
            a.StrokeColor.ToArgb() != b.StrokeColor.ToArgb() ||
            Math.Abs(a.StrokeWidth - b.StrokeWidth) > 0.001f ||
            !string.Equals(a.Text ?? string.Empty, b.Text ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        PointF[] aPoints = a.Points ?? new PointF[0];
        PointF[] bPoints = b.Points ?? new PointF[0];
        if (aPoints.Length != bPoints.Length)
        {
            return false;
        }

        for (int i = 0; i < aPoints.Length; i++)
        {
            if (!SamePoint(aPoints[i], bPoints[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SamePoint(PointF a, PointF b)
    {
        return Math.Abs(a.X - b.X) <= 0.001f && Math.Abs(a.Y - b.Y) <= 0.001f;
    }

    private static void ApplyResizeToAnnotation(AnnotationItem item, AnnotationSnapshot originalState, RectangleF originalBounds, SelectionHandle handle, PointF offset)
    {
        if (item == null || originalState == null || handle == SelectionHandle.None || originalBounds.IsEmpty)
        {
            return;
        }

        ApplyAnnotationSnapshot(item, originalState);
        RectangleF targetBounds = GetResizedBounds(originalBounds, handle, offset);
        TransformAnnotationToBounds(item, originalBounds, targetBounds);
    }

    private static RectangleF GetResizedBounds(RectangleF originalBounds, SelectionHandle handle, PointF offset)
    {
        float left = originalBounds.Left;
        float top = originalBounds.Top;
        float right = originalBounds.Right;
        float bottom = originalBounds.Bottom;

        if (HandleAffectsLeft(handle))
        {
            left += offset.X;
        }
        if (HandleAffectsRight(handle))
        {
            right += offset.X;
        }
        if (HandleAffectsTop(handle))
        {
            top += offset.Y;
        }
        if (HandleAffectsBottom(handle))
        {
            bottom += offset.Y;
        }

        if (right - left < MinAnnotationExtent)
        {
            if (HandleAffectsLeft(handle))
            {
                left = right - MinAnnotationExtent;
            }
            else
            {
                right = left + MinAnnotationExtent;
            }
        }

        if (bottom - top < MinAnnotationExtent)
        {
            if (HandleAffectsTop(handle))
            {
                top = bottom - MinAnnotationExtent;
            }
            else
            {
                bottom = top + MinAnnotationExtent;
            }
        }

        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static bool HandleAffectsLeft(SelectionHandle handle)
    {
        return handle == SelectionHandle.TopLeft || handle == SelectionHandle.BottomLeft || handle == SelectionHandle.Left;
    }

    private static bool HandleAffectsRight(SelectionHandle handle)
    {
        return handle == SelectionHandle.TopRight || handle == SelectionHandle.BottomRight || handle == SelectionHandle.Right;
    }

    private static bool HandleAffectsTop(SelectionHandle handle)
    {
        return handle == SelectionHandle.TopLeft || handle == SelectionHandle.TopRight || handle == SelectionHandle.Top;
    }

    private static bool HandleAffectsBottom(SelectionHandle handle)
    {
        return handle == SelectionHandle.BottomLeft || handle == SelectionHandle.BottomRight || handle == SelectionHandle.Bottom;
    }

    private static void TransformAnnotationToBounds(AnnotationItem item, RectangleF sourceBounds, RectangleF targetBounds)
    {
        if (item.Tool == ToolMode.Rect || item.Tool == ToolMode.Ellipse || item.Tool == ToolMode.Mosaic)
        {
            item.Start = new PointF(targetBounds.Left, targetBounds.Top);
            item.End = new PointF(targetBounds.Right, targetBounds.Bottom);
            return;
        }

        item.Start = TransformPointToBounds(item.Start, sourceBounds, targetBounds);
        item.End = TransformPointToBounds(item.End, sourceBounds, targetBounds);

        if (item.Points.Count > 0)
        {
            PointF[] points = new PointF[item.Points.Count];
            for (int i = 0; i < item.Points.Count; i++)
            {
                points[i] = TransformPointToBounds(item.Points[i], sourceBounds, targetBounds);
            }
            item.SetPoints(points);
        }
    }

    private static PointF TransformPointToBounds(PointF point, RectangleF sourceBounds, RectangleF targetBounds)
    {
        float xRatio = sourceBounds.Width <= 0.0001f ? 0.5f : (point.X - sourceBounds.Left) / sourceBounds.Width;
        float yRatio = sourceBounds.Height <= 0.0001f ? 0.5f : (point.Y - sourceBounds.Top) / sourceBounds.Height;
        return new PointF(
            targetBounds.Left + xRatio * targetBounds.Width,
            targetBounds.Top + yRatio * targetBounds.Height);
    }

    private void ResetSelectionEditState()
    {
        movingSelection = false;
        resizingSelection = false;
        activeSelectionHandle = SelectionHandle.None;
        selectionMoved = false;
        selectionEditStartState = null;
        selectionEditStartBounds = RectangleF.Empty;
    }

    private bool HasSelectedItem()
    {
        return selectedItemIndex >= 0 && selectedItemIndex < items.Count;
    }

    private void ClearSelection()
    {
        bool wasEditing = movingSelection || resizingSelection;
        bool moved = selectionMoved;
        selectedItemIndex = -1;
        ResetSelectionEditState();
        if (wasEditing)
        {
            ResumeDisplayCacheAfterInteraction(moved);
        }
    }

    private void SelectAnnotation(int index)
    {
        if (index < 0 || index >= items.Count)
        {
            ClearSelection();
            return;
        }

        selectedItemIndex = index;
        ResetSelectionEditState();
    }

    private void AddAnnotation(AnnotationItem item)
    {
        if (item == null)
        {
            return;
        }

        int index = items.Count;
        items.Add(item);
        undoStack.Push(new AnnotationUndoAction(AnnotationUndoKind.Add, item, index));
        SelectAnnotation(index);
        MarkAnnotationsChanged();
    }

    private bool DeleteSelectedAnnotation()
    {
        if (!HasSelectedItem())
        {
            return false;
        }

        int index = selectedItemIndex;
        AnnotationItem item = items[index];
        items.RemoveAt(index);
        undoStack.Push(new AnnotationUndoAction(AnnotationUndoKind.Delete, item, index));
        ClearSelection();
        MarkAnnotationsChanged();
        RequestCanvasRender();
        return true;
    }

    private void UndoAnnotationAction()
    {
        if (undoStack.Count > 0)
        {
            AnnotationUndoAction action = undoStack.Pop();
            if (action.Kind == AnnotationUndoKind.Add)
            {
                int index = items.IndexOf(action.Item);
                if (index >= 0)
                {
                    items.RemoveAt(index);
                    if (selectedItemIndex == index)
                    {
                        ClearSelection();
                    }
                    else if (selectedItemIndex > index)
                    {
                        selectedItemIndex--;
                    }
                }
            }
            else if (action.Kind == AnnotationUndoKind.Delete && action.Item != null)
            {
                int index = Math.Max(0, Math.Min(action.Index, items.Count));
                items.Insert(index, action.Item);
                SelectAnnotation(index);
            }
            else if (action.Kind == AnnotationUndoKind.Transform && action.Item != null && action.Before != null)
            {
                int index = items.IndexOf(action.Item);
                if (index >= 0)
                {
                    ApplyAnnotationSnapshot(action.Item, action.Before);
                    SelectAnnotation(index);
                }
            }

            MarkAnnotationsChanged();
            RequestCanvasRender();
            return;
        }

        if (items.Count > 0)
        {
            items.RemoveAt(items.Count - 1);
            if (selectedItemIndex >= items.Count)
            {
                ClearSelection();
            }
            MarkAnnotationsChanged();
            RequestCanvasRender();
        }
    }

    private void ClearAnnotations()
    {
        items.Clear();
        undoStack.Clear();
        ClearSelection();
        MarkAnnotationsChanged();
        RequestCanvasRender();
    }

    private bool ConfirmClearAnnotations()
    {
        if (items.Count == 0 && !inlineTextEditing)
        {
            return false;
        }

        DialogResult result = MessageBox.Show(
            this,
            "确定要清空当前标注内容吗？此操作无法撤销。",
            "清空标注",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        return result == DialogResult.Yes;
    }

    private int HitTestAnnotation(PointF point)
    {
        float tolerance = GetSelectionHitTolerance();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (IsPointInAnnotation(point, items[i], tolerance))
            {
                return i;
            }
        }
        return -1;
    }

    private float GetSelectionHitTolerance()
    {
        float scale = GetScale();
        if (scale <= 0)
        {
            return SelectionHitTolerancePixels;
        }
        return Math.Max(2f, SelectionHitTolerancePixels / scale);
    }

    private bool IsPointInAnnotation(PointF point, AnnotationItem item, float tolerance)
    {
        if (item == null)
        {
            return false;
        }

        float itemWidth = item.StrokeWidth > 0 ? item.StrokeWidth : strokeWidth;
        if (item.Tool == ToolMode.Arrow)
        {
            return IsPointInFilledArrow(point, item, itemWidth, tolerance);
        }

        if (item.Tool == ToolMode.Pen)
        {
            if (item.Points.Count == 1)
            {
                return Distance(point, item.Points[0]) <= tolerance;
            }

            float threshold = Math.Max(tolerance, itemWidth * 0.75f);
            for (int i = 1; i < item.Points.Count; i++)
            {
                if (DistanceToSegment(point, item.Points[i - 1], item.Points[i]) <= threshold)
                {
                    return true;
                }
            }
            return false;
        }

        if (item.Tool == ToolMode.Rect)
        {
            return IsPointOnRectangle(point, NormalizeRect(item.Start, item.End), Math.Max(tolerance, itemWidth * 0.5f));
        }

        if (item.Tool == ToolMode.Ellipse)
        {
            return IsPointOnEllipse(point, NormalizeRect(item.Start, item.End), Math.Max(tolerance, itemWidth * 0.5f));
        }

        RectangleF bounds = GetItemBounds(item);
        float hitPadding = item.Tool == ToolMode.Text ? tolerance : Math.Max(tolerance, itemWidth);
        bounds.Inflate(hitPadding, hitPadding);
        return bounds.Contains(point);
    }

    private static bool IsPointInFilledArrow(PointF point, AnnotationItem item, float itemWidth, float tolerance)
    {
        PointF[] points = BuildFilledArrowPoints(item, itemWidth);
        if (points.Length == 0)
        {
            return false;
        }

        using (GraphicsPath path = new GraphicsPath())
        using (Pen outlinePen = new Pen(Color.Black, Math.Max(1f, tolerance * 2f)))
        {
            path.AddPolygon(points);
            return path.IsVisible(point) || path.IsOutlineVisible(point, outlinePen);
        }
    }

    private static bool IsPointOnRectangle(PointF point, RectangleF rect, float tolerance)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        RectangleF outer = rect;
        outer.Inflate(tolerance, tolerance);
        if (!outer.Contains(point))
        {
            return false;
        }

        RectangleF inner = rect;
        inner.Inflate(-tolerance, -tolerance);
        if (inner.Width <= 0 || inner.Height <= 0)
        {
            return true;
        }
        return !inner.Contains(point);
    }

    private static bool IsPointOnEllipse(PointF point, RectangleF rect, float tolerance)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        using (GraphicsPath path = new GraphicsPath())
        using (Pen pen = new Pen(Color.Black, Math.Max(1f, tolerance * 2f)))
        {
            path.AddEllipse(rect);
            return path.IsOutlineVisible(point, pen);
        }
    }

    private static float DistanceToSegment(PointF point, PointF a, PointF b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        float lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0.0001f)
        {
            return Distance(point, a);
        }

        float t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
        t = Math.Max(0f, Math.Min(1f, t));
        PointF projection = new PointF(a.X + t * dx, a.Y + t * dy);
        return Distance(point, projection);
    }

    private static void MoveAnnotation(AnnotationItem item, float dx, float dy)
    {
        item.MoveBy(dx, dy);
    }

    private PointF GetMoveOffset()
    {
        return new PointF(moveCurrentPoint.X - moveStartPoint.X, moveCurrentPoint.Y - moveStartPoint.Y);
    }

    private bool AppendPenPointIfNeeded(AnnotationItem item, PointF point, float threshold)
    {
        if (item == null)
        {
            return false;
        }

        if (item.Points.Count == 0)
        {
            item.AddPoint(point);
            return true;
        }

        PointF last = item.Points[item.Points.Count - 1];
        if (Distance(last, point) < threshold)
        {
            return false;
        }

        item.AddPoint(point);
        return true;
    }

    private void Canvas_MouseDown(object sender, MouseEventArgs e)
    {
        toolOptionsPanel.Visible = false;

        if (e.Button == MouseButtons.Right)
        {
            bool wasEditingSelection = movingSelection || resizingSelection;
            ResetSelectionEditState();
            if (wasEditingSelection)
            {
                ResumeDisplayCacheAfterInteraction(false);
            }
            panning = true;
            drawing = false;
            currentPenItem = null;
            lastPanPoint = e.Location;
            canvas.Cursor = Cursors.Hand;
            canvas.Capture = true;
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        startPoint = ToImagePoint(e.Location);
        currentPoint = startPoint;

        if (inlineTextEditing)
        {
            if (IsPointInInlineTextInput(startPoint))
            {
                BeginInlineTextSelection(startPoint);
                return;
            }

            HideInlineTextInput(true);
        }

        SelectionHandle hitHandle = HitTestSelectionHandle(startPoint);
        if (hitHandle != SelectionHandle.None && HasSelectedItem())
        {
            CommitInlineTextInput();
            resizingSelection = true;
            activeSelectionHandle = hitHandle;
            selectionMoved = false;
            moveStartPoint = startPoint;
            moveCurrentPoint = startPoint;
            lastMovePoint = startPoint;
            selectionEditStartState = CaptureAnnotation(items[selectedItemIndex]);
            selectionEditStartBounds = GetItemBounds(items[selectedItemIndex]);
            drawing = false;
            currentPenItem = null;
            SuspendDisplayCacheForInteraction();
            EnsureMoveBackgroundCache(GetView());
            canvas.Cursor = GetSelectionHandleCursor(hitHandle);
            canvas.Capture = true;
            RequestCanvasRender();
            return;
        }

        int hitIndex = HitTestAnnotation(startPoint);
        if (hitIndex >= 0)
        {
            CommitInlineTextInput();
            SelectAnnotation(hitIndex);
            movingSelection = true;
            selectionMoved = false;
            moveStartPoint = startPoint;
            moveCurrentPoint = startPoint;
            lastMovePoint = startPoint;
            selectionEditStartState = CaptureAnnotation(items[selectedItemIndex]);
            selectionEditStartBounds = GetItemBounds(items[selectedItemIndex]);
            drawing = false;
            currentPenItem = null;
            SuspendDisplayCacheForInteraction();
            EnsureMoveBackgroundCache(GetView());
            canvas.Cursor = Cursors.SizeAll;
            canvas.Capture = true;
            RequestCanvasRender();
            return;
        }

        ClearSelection();

        if (currentTool == ToolMode.Text)
        {
            ShowInlineTextInput(e.Location);
            return;
        }

        drawing = true;
        currentPenItem = null;
        canvas.Capture = true;

        if (currentTool == ToolMode.Pen)
        {
            currentPenItem = new AnnotationItem();
            currentPenItem.Tool = ToolMode.Pen;
            currentPenItem.StrokeColor = strokeColor;
            currentPenItem.StrokeWidth = strokeWidth;
            AppendPenPointIfNeeded(currentPenItem, startPoint, 0f);
        }
    }

    private void ShowInlineTextInput(Point screenLocation)
    {
        if (inlineTextEditing)
        {
            HideInlineTextInput(true);
        }

        textInputPosition = ToImagePoint(screenLocation);
        inlineText = string.Empty;
        inlineCompositionText = string.Empty;
        inlineTextEditing = true;
        inlineTextSelecting = false;
        inlineTextSelectionAnchor = 0;
        inlineTextBox = CreateImeHostTextBox(screenLocation);
        inlineTextBox.KeyDown += InlineTextBox_KeyDown;
        inlineTextBox.KeyUp += InlineTextBox_KeyUp;
        inlineTextBox.TextChanged += InlineTextBox_TextChanged;
        inlineTextBox.CompositionChanged += InlineTextBox_CompositionChanged;
        canvas.Controls.Add(inlineTextBox);
        inlineTextBox.BringToFront();
        RestartInlineCaret();
        inlineTextBox.Focus();
        RequestCanvasRender();
    }

    private void HideInlineTextInput(bool saveText)
    {
        if (!inlineTextEditing)
        {
            return;
        }

        string text = inlineTextBox == null ? (inlineText ?? string.Empty).Trim() : inlineTextBox.Text.Trim();
        inlineTextEditing = false;
        inlineTextSelecting = false;
        inlineText = string.Empty;
        inlineCompositionText = string.Empty;
        inlineCaretVisible = false;
        inlineCaretTimer.Stop();
        if (inlineTextBox != null)
        {
            try
            {
                inlineTextBox.KeyDown -= InlineTextBox_KeyDown;
                inlineTextBox.KeyUp -= InlineTextBox_KeyUp;
                inlineTextBox.TextChanged -= InlineTextBox_TextChanged;
                inlineTextBox.CompositionChanged -= InlineTextBox_CompositionChanged;
                canvas.Controls.Remove(inlineTextBox);
                inlineTextBox.Dispose();
            }
            catch
            {
            }
            finally
            {
                inlineTextBox = null;
            }
        }

        if (saveText && !string.IsNullOrWhiteSpace(text))
        {
            var item = new AnnotationItem();
            item.Tool = ToolMode.Text;
            item.Start = textInputPosition;
            item.End = textInputPosition;
            item.StrokeColor = strokeColor;
            item.StrokeWidth = strokeWidth;
            item.Text = text;
            AddAnnotation(item);
        }
        if (!IsDisposed && canvas.IsHandleCreated)
        {
            canvas.Focus();
        }
        RequestCanvasRender();
    }

    private void CommitInlineTextInput()
    {
        if (inlineTextEditing)
        {
            HideInlineTextInput(true);
        }
    }

    private bool CancelInlineTextInput()
    {
        if (!inlineTextEditing)
        {
            return false;
        }

        HideInlineTextInput(false);
        return true;
    }

    private bool IsPointInInlineTextInput(PointF point)
    {
        RectangleF bounds = GetInlineTextInputBounds();
        if (bounds.IsEmpty)
        {
            return false;
        }

        float tolerance = GetSelectionHitTolerance();
        bounds.Inflate(tolerance, tolerance);
        return bounds.Contains(point);
    }

    private RectangleF GetInlineTextInputBounds()
    {
        string text = inlineTextBox == null ? (inlineText ?? string.Empty) : inlineTextBox.Text;
        string composition = inlineCompositionText ?? string.Empty;
        float fontSize = Math.Max(1f, strokeWidth * 6f);
        using (Graphics g = canvas.CreateGraphics())
        using (Font font = new Font(AppStyles.UiFontName, fontSize, FontStyle.Bold))
        using (StringFormat format = CreateInlineTextFormat())
        {
            float width = MeasureInlineEditingWidth(g, font, format, text, composition);
            float height = MeasureInlineTextHeight(g, font, format);
            return new RectangleF(textInputPosition.X, textInputPosition.Y, width, height);
        }
    }

    private int GetInlineTextIndexAtPoint(PointF point)
    {
        string text = inlineTextBox == null ? (inlineText ?? string.Empty) : inlineTextBox.Text;
        string composition = inlineCompositionText ?? string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        float localX = point.X - textInputPosition.X;
        if (localX <= 0)
        {
            return 0;
        }

        float fontSize = Math.Max(1f, strokeWidth * 6f);
        using (Graphics g = canvas.CreateGraphics())
        using (Font font = new Font(AppStyles.UiFontName, fontSize, FontStyle.Bold))
        using (StringFormat format = CreateInlineTextFormat())
        {
            if (composition.Length > 0)
            {
                int insertionIndex = Math.Max(0, Math.Min(text.Length, GetInlineSelectionStart()));
                float beforeWidth = MeasureInlineTextWidth(g, font, format, text, insertionIndex);
                float compositionWidth = MeasureInlineTextWidth(g, font, format, composition, composition.Length);
                if (localX >= beforeWidth && localX <= beforeWidth + compositionWidth)
                {
                    return insertionIndex;
                }

                if (localX > beforeWidth + compositionWidth)
                {
                    localX -= compositionWidth;
                }
            }

            float previousWidth = 0f;
            for (int i = 1; i <= text.Length; i++)
            {
                float width = MeasureInlineTextWidth(g, font, format, text, i);
                if (localX <= (previousWidth + width) / 2f)
                {
                    return i - 1;
                }
                previousWidth = width;
            }
        }
        return text.Length;
    }

    private void BeginInlineTextSelection(PointF point)
    {
        if (inlineTextBox == null)
        {
            return;
        }

        int index = GetInlineTextIndexAtPoint(point);
        inlineTextSelectionAnchor = index;
        inlineTextSelecting = true;
        inlineTextBox.Select(index, 0);
        inlineTextBox.Focus();
        canvas.Cursor = Cursors.IBeam;
        canvas.Capture = true;
        SyncInlineTextInputState();
    }

    private void UpdateInlineTextSelection(PointF point)
    {
        if (inlineTextBox == null)
        {
            return;
        }

        int index = GetInlineTextIndexAtPoint(point);
        int start = Math.Min(inlineTextSelectionAnchor, index);
        int length = Math.Abs(index - inlineTextSelectionAnchor);
        inlineTextBox.Select(start, length);
        SyncInlineTextInputState();
    }

    private InlineImeTextBox CreateImeHostTextBox(Point screenLocation)
    {
        float fontSize = Math.Max(1f, strokeWidth * 6f);
        var textBox = new InlineImeTextBox();
        textBox.Multiline = false;
        textBox.BorderStyle = BorderStyle.None;
        textBox.AutoSize = false;
        textBox.ShortcutsEnabled = true;
        textBox.TabStop = false;
        textBox.Font = new Font(AppStyles.UiFontName, fontSize, FontStyle.Bold);
        textBox.BackColor = strokeColor;
        textBox.ForeColor = strokeColor;
        textBox.Left = Math.Max(0, Math.Min(canvas.ClientSize.Width - 1, screenLocation.X));
        textBox.Top = Math.Max(0, Math.Min(canvas.ClientSize.Height - 1, screenLocation.Y));
        textBox.Width = 1;
        textBox.Height = 1;
        return textBox;
    }

    private int GetInlineSelectionStart()
    {
        return inlineTextBox == null ? 0 : inlineTextBox.SelectionStart;
    }

    private int GetInlineSelectionLength()
    {
        return inlineTextBox == null ? 0 : inlineTextBox.SelectionLength;
    }

    private int GetInlineCaretIndex()
    {
        if (inlineTextBox == null)
        {
            return (inlineText ?? string.Empty).Length;
        }

        return Math.Max(0, Math.Min(inlineTextBox.TextLength, inlineTextBox.SelectionStart + inlineTextBox.SelectionLength));
    }

    private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (!HandleInlineTextKeyDown(e) && !IsDisposed && IsHandleCreated)
        {
            BeginInvoke((MethodInvoker)SyncInlineTextInputState);
        }
    }

    private void InlineTextBox_KeyUp(object sender, KeyEventArgs e)
    {
        SyncInlineTextInputState();
    }

    private void InlineTextBox_TextChanged(object sender, EventArgs e)
    {
        SyncInlineTextInputState();
    }

    private void InlineTextBox_CompositionChanged(object sender, EventArgs e)
    {
        SyncInlineTextInputState();
    }

    private void SyncInlineTextInputState()
    {
        if (inlineTextBox == null)
        {
            return;
        }

        inlineText = inlineTextBox.Text;
        inlineCompositionText = inlineTextBox.CompositionText;
        PositionImeHostAtCaret();
        RestartInlineCaret();
        RequestCanvasRender();
    }

    private void PositionImeHostAtCaret()
    {
        if (inlineTextBox == null)
        {
            return;
        }

        Point caret = GetInlineCaretCanvasPoint();
        inlineTextBox.Left = Math.Max(0, Math.Min(canvas.ClientSize.Width - 1, caret.X));
        inlineTextBox.Top = Math.Max(0, Math.Min(canvas.ClientSize.Height - 1, caret.Y));
    }

    private Point GetInlineCaretCanvasPoint()
    {
        RectangleF view = GetView();
        float scale = GetScale();
        float x = view.X + textInputPosition.X * scale;
        float y = view.Y + textInputPosition.Y * scale;
        string text = inlineText ?? string.Empty;
        string composition = inlineCompositionText ?? string.Empty;
        int caretIndex = GetInlineCaretIndex();
        if ((text.Length > 0 && caretIndex > 0) || composition.Length > 0)
        {
            using (Graphics g = canvas.CreateGraphics())
            using (Font font = new Font(AppStyles.UiFontName, Math.Max(1f, strokeWidth * 6f * scale), FontStyle.Bold))
            using (StringFormat format = CreateInlineTextFormat())
            {
                if (composition.Length > 0)
                {
                    int selectionStart = Math.Max(0, Math.Min(text.Length, GetInlineSelectionStart()));
                    int selectionLength = Math.Max(0, Math.Min(text.Length - selectionStart, GetInlineSelectionLength()));
                    int insertionIndex = selectionLength > 0 ? selectionStart : caretIndex;
                    x += MeasureInlineTextWidth(g, font, format, text, insertionIndex);
                    x += MeasureInlineTextWidth(g, font, format, composition, composition.Length);
                }
                else
                {
                    x += MeasureInlineTextWidth(g, font, format, text, caretIndex);
                }
            }
        }
        return new Point((int)Math.Round(x), (int)Math.Round(y));
    }

    private void RestartInlineCaret()
    {
        inlineCaretVisible = true;
        inlineCaretTimer.Stop();
        inlineCaretTimer.Start();
    }

    private bool HandleInlineTextKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            HideInlineTextInput(true);
            SaveAndClose();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        if (e.Control && e.KeyCode == Keys.A && inlineTextBox != null)
        {
            inlineTextBox.SelectAll();
            SyncInlineTextInputState();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        if (e.KeyCode == Keys.Enter)
        {
            HideInlineTextInput(true);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        if (e.KeyCode == Keys.Escape)
        {
            HideInlineTextInput(false);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        return false;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (panning)
        {
            viewOffset.X += e.Location.X - lastPanPoint.X;
            viewOffset.Y += e.Location.Y - lastPanPoint.Y;
            lastPanPoint = e.Location;
            imePositionThrottle.Invoke();
            RequestCanvasRender();
            return;
        }

        if (inlineTextSelecting)
        {
            UpdateInlineTextSelection(ToImagePoint(e.Location));
            return;
        }

        if (movingSelection || resizingSelection)
        {
            if (!HasSelectedItem())
            {
                ClearSelection();
                canvas.Cursor = Cursors.Cross;
                return;
            }

            PointF point = ToImagePoint(e.Location);
            if (Distance(lastMovePoint, point) >= CanvasPixelsToImageDistance(MoveSampleThresholdPixels))
            {
                moveCurrentPoint = point;
                lastMovePoint = point;
                selectionMoved = Distance(moveStartPoint, moveCurrentPoint) >= CanvasPixelsToImageDistance(MoveSampleThresholdPixels);
                RequestCanvasRender();
            }
            canvas.Cursor = resizingSelection ? GetSelectionHandleCursor(activeSelectionHandle) : Cursors.SizeAll;
            return;
        }

        if (!drawing)
        {
            PointF point = ToImagePoint(e.Location);
            if (inlineTextEditing && IsPointInInlineTextInput(point))
            {
                canvas.Cursor = Cursors.IBeam;
                return;
            }

            SelectionHandle hoverHandle = HitTestSelectionHandle(point);
            if (hoverHandle != SelectionHandle.None)
            {
                canvas.Cursor = GetSelectionHandleCursor(hoverHandle);
                return;
            }

            canvas.Cursor = HitTestAnnotation(point) >= 0 ? Cursors.SizeAll : Cursors.Cross;
            return;
        }

        currentPoint = ToImagePoint(e.Location);
        if (currentTool == ToolMode.Pen && currentPenItem != null)
        {
            if (AppendPenPointIfNeeded(currentPenItem, currentPoint, CanvasPixelsToImageDistance(PenSampleThresholdPixels)))
            {
                RequestCanvasRender();
            }
            return;
        }
        RequestCanvasRender();
    }

    private void Canvas_MouseUp(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            panning = false;
            canvas.Cursor = Cursors.Cross;
            canvas.Capture = false;
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (inlineTextSelecting)
        {
            inlineTextSelecting = false;
            canvas.Capture = false;
            canvas.Cursor = Cursors.IBeam;
            if (inlineTextBox != null)
            {
                inlineTextBox.Focus();
            }
            RequestCanvasRender();
            return;
        }

        if (movingSelection || resizingSelection)
        {
            bool wasMovingSelection = movingSelection;
            SelectionHandle resizeHandle = activeSelectionHandle;
            AnnotationSnapshot before = selectionEditStartState;
            RectangleF originalBounds = selectionEditStartBounds;
            moveCurrentPoint = ToImagePoint(e.Location);
            PointF offset = GetMoveOffset();
            bool changed = selectionMoved || Distance(moveStartPoint, moveCurrentPoint) >= CanvasPixelsToImageDistance(MoveSampleThresholdPixels);
            if (changed && HasSelectedItem())
            {
                AnnotationItem item = items[selectedItemIndex];
                if (before == null)
                {
                    before = CaptureAnnotation(item);
                }

                ApplyAnnotationSnapshot(item, before);
                if (wasMovingSelection)
                {
                    MoveAnnotation(item, offset.X, offset.Y);
                }
                else
                {
                    ApplyResizeToAnnotation(item, before, originalBounds, resizeHandle, offset);
                }

                AnnotationSnapshot after = CaptureAnnotation(item);
                changed = RecordTransformUndo(item, selectedItemIndex, before, after);
            }
            ResetSelectionEditState();
            canvas.Cursor = Cursors.Cross;
            canvas.Capture = false;
            ResumeDisplayCacheAfterInteraction(changed);
            RequestCanvasRender();
            return;
        }

        if (!drawing)
        {
            return;
        }

        drawing = false;
        canvas.Capture = false;
        currentPoint = ToImagePoint(e.Location);
        if (currentTool == ToolMode.Pen)
        {
            AppendPenPointIfNeeded(currentPenItem, currentPoint, 0.01f);
            if (currentPenItem != null && IsMeaningfulAnnotation(currentPenItem))
            {
                AddAnnotation(currentPenItem);
            }
        }
        else
        {
            var item = new AnnotationItem();
            item.Tool = currentTool;
            item.Start = startPoint;
            item.End = currentPoint;
            item.StrokeColor = strokeColor;
            item.StrokeWidth = strokeWidth;
            if (IsMeaningfulAnnotation(item))
            {
                AddAnnotation(item);
            }
        }
        currentPenItem = null;
        RequestCanvasRender();
    }

    private void AnnotatorForm_KeyDown(object sender, KeyEventArgs e)
    {
        if (inlineTextEditing)
        {
            return;
        }

        if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
        {
            if (DeleteSelectedAnnotation())
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }
        else if (e.Control && e.KeyCode == Keys.Z)
        {
            UndoAnnotationAction();
        }
        else if (e.Control && e.KeyCode == Keys.S)
        {
            SaveAndClose();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
        else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.F)
        {
            ResetZoom();
        }
        else if (e.KeyCode == Keys.D1)
        {
            currentTool = ToolMode.Rect;
            UpdateToolButtons();
        }
        else if (e.KeyCode == Keys.D2)
        {
            currentTool = ToolMode.Arrow;
            UpdateToolButtons();
        }
        else if (e.KeyCode == Keys.D3)
        {
            currentTool = ToolMode.Pen;
            UpdateToolButtons();
        }
        else if (e.KeyCode == Keys.D4)
        {
            currentTool = ToolMode.Mosaic;
            UpdateToolButtons();
        }
    }

    private Bitmap RenderFinalImage()
    {
        var result = new Bitmap(baseImage.Width, baseImage.Height);
        using (Graphics g = Graphics.FromImage(result))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(baseImage, 0, 0, baseImage.Width, baseImage.Height);
        }

        foreach (AnnotationItem item in items)
        {
            if (item.Tool == ToolMode.Mosaic)
            {
                ApplyMosaic(result, item);
            }
            else
            {
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    DrawItem(g, item, 1f);
                }
            }
        }
        return result;
    }

    private void SaveAndClose()
    {
        try
        {
            if (inlineTextEditing)
            {
                HideInlineTextInput(true);
            }

            using (Bitmap result = RenderFinalImage())
            {
                string outputPath = GetAnnotatedOutputPath(imagePath);
                SaveBitmap(result, outputPath);
                Clipboard.SetImage((Bitmap)result.Clone());
            }
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "图片标注", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void SaveBitmap(Bitmap bitmap, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        ImageFormat format = ImageFormat.Png;
        if (ext == ".jpg" || ext == ".jpeg")
        {
            format = ImageFormat.Jpeg;
        }
        else if (ext == ".bmp")
        {
            format = ImageFormat.Bmp;
        }

        string tempPath = path + ".tmp";
        bitmap.Save(tempPath, format);
        File.Move(tempPath, path);
    }

    private static string GetAnnotatedOutputPath(string path)
    {
        string dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir))
        {
            dir = Environment.CurrentDirectory;
        }

        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".png";
        }

        string candidate = Path.Combine(dir, name + "_标注" + ext);
        int index = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, name + "_标注_" + index + ext);
            index++;
        }
        return candidate;
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && string.Equals(args[0], "-SelfTest", StringComparison.OrdinalIgnoreCase))
            {
                RunSelfTest();
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string path = CaptureService.GetInputImagePath(args);
            using (Bitmap image = CaptureService.LoadBitmap(path))
            using (var form = new AnnotatorForm(path, (Bitmap)image.Clone()))
            {
                AppLog.Info("Starting annotator window.");
                Application.Run(form);
            }
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("Application failed.", ex);
            MessageBox.Show(ex.Message, "图片标注", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void RunSelfTest()
    {
        AssertGraphicsTransformOrder();
        AssertAnnotationPointReplacementInvalidatesCache();
        AssertResizeTransform();

        AssertMeaningful(new AnnotationItem
        {
            Tool = ToolMode.Arrow,
            Start = new PointF(10, 10),
            End = new PointF(10, 10)
        }, false);

        AssertMeaningful(new AnnotationItem
        {
            Tool = ToolMode.Arrow,
            Start = new PointF(10, 10),
            End = new PointF(20, 20)
        }, true);

        AssertMeaningful(new AnnotationItem
        {
            Tool = ToolMode.Rect,
            Start = new PointF(10, 10),
            End = new PointF(10.5f, 12)
        }, false);

        var penItem = new AnnotationItem();
        penItem.Tool = ToolMode.Pen;
        penItem.AddPoint(new PointF(10, 10));
        AssertMeaningful(penItem, false);
        penItem.AddPoint(new PointF(20, 20));
        AssertMeaningful(penItem, true);
    }

    private static void AssertGraphicsTransformOrder()
    {
        using (var bitmap = new Bitmap(1, 1))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.TranslateTransform(100f, 50f, MatrixOrder.Prepend);
            g.ScaleTransform(2f, 2f, MatrixOrder.Prepend);
            g.TranslateTransform(3f, 4f, MatrixOrder.Prepend);

            PointF[] point = new PointF[] { new PointF(10f, 20f) };
            using (Matrix transform = g.Transform)
            {
                transform.TransformPoints(point);
            }

            AssertNear(point[0].X, 126f, "GDI+ transform x");
            AssertNear(point[0].Y, 98f, "GDI+ transform y");
        }
    }

    private static void AssertAnnotationPointReplacementInvalidatesCache()
    {
        var item = new AnnotationItem();
        item.Tool = ToolMode.Pen;
        item.AddPoint(new PointF(1, 1));
        item.GetDrawingPoints();
        item.SetPoints(new PointF[] { new PointF(4, 5), new PointF(8, 9) });

        PointF[] points = item.GetDrawingPoints();
        if (points.Length != 2)
        {
            throw new InvalidOperationException("Point replacement self test failed.");
        }
        AssertNear(points[0].X, 4f, "point replacement x");
        AssertNear(points[1].Y, 9f, "point replacement y");
    }

    private static void AssertResizeTransform()
    {
        var item = new AnnotationItem
        {
            Tool = ToolMode.Rect,
            Start = new PointF(10, 10),
            End = new PointF(30, 30),
            StrokeColor = AppStyles.DefaultStroke,
            StrokeWidth = 4f
        };

        Type formType = typeof(AnnotatorForm);
        System.Reflection.MethodInfo capture = formType.GetMethod(
            "CaptureAnnotation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        System.Reflection.MethodInfo apply = formType.GetMethod(
            "ApplyResizeToAnnotation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Type handleType = formType.GetNestedType(
            "SelectionHandle",
            System.Reflection.BindingFlags.NonPublic);
        if (capture == null || apply == null || handleType == null)
        {
            throw new InvalidOperationException("Resize transform self test cannot find helpers.");
        }

        object snapshot = capture.Invoke(null, new object[] { item });
        object rightHandle = Enum.Parse(handleType, "Right");
        apply.Invoke(null, new object[]
        {
            item,
            snapshot,
            new RectangleF(10, 10, 20, 20),
            rightHandle,
            new PointF(5, 0)
        });

        AssertNear(item.Start.X, 10f, "resize transform left");
        AssertNear(item.End.X, 35f, "resize transform right");
        AssertNear(item.End.Y, 30f, "resize transform bottom");
    }

    private static void AssertNear(float actual, float expected, string label)
    {
        if (Math.Abs(actual - expected) > 0.001f)
        {
            throw new InvalidOperationException(label + " self test failed.");
        }
    }

    private static void AssertMeaningful(AnnotationItem item, bool expected)
    {
        var method = typeof(AnnotatorForm).GetMethod(
            "IsMeaningfulAnnotation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method == null)
        {
            throw new InvalidOperationException("Self test cannot find annotation filter.");
        }

        bool actual = (bool)method.Invoke(null, new object[] { item });
        if (actual != expected)
        {
            throw new InvalidOperationException("Annotation filter self test failed.");
        }
    }
}
