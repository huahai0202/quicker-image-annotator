using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

internal sealed partial class AnnotatorForm : Form
{
    private readonly string imagePath;
    private readonly Bitmap baseImage;
    private readonly List<AnnotationItem> items = new List<AnnotationItem>();
    private readonly ModernToolbarPanel toolbar = new ModernToolbarPanel();
    private readonly BufferedCanvas canvas = new BufferedCanvas();
    private readonly Dictionary<ToolMode, Button> toolButtons = new Dictionary<ToolMode, Button>();
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
    private Bitmap displayCache;
    private Size displayCacheSize = Size.Empty;
    private Icon windowIcon;
    private float zoomFactor = 1f;
    private PointF viewOffset = PointF.Empty;
    private readonly Timer cacheRefreshTimer = new Timer();
    private readonly AnimationFrameScheduler canvasRenderScheduler;
    private readonly ThrottledAction imePositionThrottle;
    private bool suspendDisplayCache;
    private Color strokeColor = AppStyles.DefaultStroke;
    private float strokeWidth = 4f;
    private const int MaxCachePixels = 6000000;
    private const float MinAnnotationExtent = 2f;
    private bool inlineTextEditing;
    private TextBox inlineTextBox;
    private string inlineText = string.Empty;
    private bool inlineCaretVisible;
    private readonly Timer inlineCaretTimer = new Timer();
    private PointF textInputPosition;
    private const int MosaicBlockSize = 18;

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

        if (displayCache != null && displayCacheSize == targetSize)
        {
            return;
        }

        ResetDisplayCache();
        displayCache = new Bitmap(targetSize.Width, targetSize.Height);
        displayCacheSize = targetSize;

        using (Graphics g = Graphics.FromImage(displayCache))
        {
            g.Clear(AppStyles.CanvasBackground);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(baseImage, new Rectangle(0, 0, targetSize.Width, targetSize.Height));
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
        if (!suspendDisplayCache && CanUseDisplayCache(view))
        {
            EnsureDisplayCache(view);
            scale = (float)displayCache.Width / baseImage.Width;
            e.Graphics.DrawImageUnscaled(displayCache, viewX, viewY);
        }
        else
        {
            DrawVisibleImageRegion(e.Graphics, view, scale);
        }

        GraphicsState state = e.Graphics.Save();
        e.Graphics.TranslateTransform(viewX, viewY);
        e.Graphics.ScaleTransform(scale, scale);

        foreach (AnnotationItem item in items)
        {
            DrawItem(e.Graphics, item, scale);
        }

        if (drawing && currentTool != ToolMode.Pen)
        {
            var preview = new AnnotationItem();
            preview.Tool = currentTool;
            preview.Start = startPoint;
            preview.End = currentPoint;
            preview.StrokeColor = strokeColor;
            preview.StrokeWidth = strokeWidth;
            DrawItem(e.Graphics, preview, scale);
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
            else if (item.Tool == ToolMode.Arrow)
            {
                using (var cap = new AdjustableArrowCap(7f, 9f, true))
                {
                    pen.CustomEndCap = cap;
                    g.DrawLine(pen, item.Start, item.End);
                }
            }
            else if (item.Tool == ToolMode.Pen && item.Points.Count > 1)
            {
                for (int i = 1; i < item.Points.Count; i++)
                {
                    g.DrawLine(pen, item.Points[i - 1], item.Points[i]);
                }
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

    private void DrawInlineTextInput(Graphics g, float scale)
    {
        float fontSize = strokeWidth * 6f;
        string text = inlineText ?? string.Empty;
        using (Font font = new Font(AppStyles.UiFontName, fontSize, FontStyle.Bold))
        using (Brush textBrush = new SolidBrush(strokeColor))
        using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
        {
            if (text.Length > 0)
            {
                g.DrawString(text, font, textBrush, textInputPosition);
            }

            if (inlineCaretVisible)
            {
                float caretX = textInputPosition.X;
                if (text.Length > 0)
                {
                    caretX += g.MeasureString(text, font, int.MaxValue, format).Width;
                }

                float caretHeight = g.MeasureString("M", font, int.MaxValue, format).Height;
                float caretWidth = Math.Max(1f, 1f / Math.Max(0.001f, scale));
                using (Pen caretPen = new Pen(strokeColor, caretWidth))
                {
                    g.DrawLine(caretPen, caretX, textInputPosition.Y, caretX, textInputPosition.Y + caretHeight);
                }
            }
        }
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

    private void Canvas_MouseDown(object sender, MouseEventArgs e)
    {
        toolOptionsPanel.Visible = false;

        if (e.Button == MouseButtons.Right)
        {
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
            currentPenItem.Points.Add(startPoint);
            items.Add(currentPenItem);
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
        inlineTextEditing = true;
        inlineTextBox = CreateImeHostTextBox(screenLocation);
        inlineTextBox.KeyDown += InlineTextBox_KeyDown;
        inlineTextBox.TextChanged += InlineTextBox_TextChanged;
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
        inlineText = string.Empty;
        inlineCaretVisible = false;
        inlineCaretTimer.Stop();
        if (inlineTextBox != null)
        {
            try
            {
                inlineTextBox.KeyDown -= InlineTextBox_KeyDown;
                inlineTextBox.TextChanged -= InlineTextBox_TextChanged;
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
            items.Add(item);
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

    private TextBox CreateImeHostTextBox(Point screenLocation)
    {
        float fontSize = Math.Max(1f, strokeWidth * 6f);
        var textBox = new TextBox();
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

    private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        HandleInlineTextKeyDown(e);
    }

    private void InlineTextBox_TextChanged(object sender, EventArgs e)
    {
        if (inlineTextBox == null)
        {
            return;
        }

        inlineText = inlineTextBox.Text;
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
        if (text.Length > 0)
        {
            using (Graphics g = canvas.CreateGraphics())
            using (Font font = new Font(AppStyles.UiFontName, Math.Max(1f, strokeWidth * 6f * scale), FontStyle.Bold))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                x += g.MeasureString(text, font, int.MaxValue, format).Width;
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

        if (!drawing)
        {
            return;
        }

        currentPoint = ToImagePoint(e.Location);
        if (currentTool == ToolMode.Pen && currentPenItem != null)
        {
            currentPenItem.Points.Add(currentPoint);
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

        if (!drawing)
        {
            return;
        }

        drawing = false;
        canvas.Capture = false;
        currentPoint = ToImagePoint(e.Location);
        if (currentTool == ToolMode.Pen)
        {
            if (!IsMeaningfulAnnotation(currentPenItem))
            {
                items.Remove(currentPenItem);
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
                items.Add(item);
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

        if (e.Control && e.KeyCode == Keys.Z)
        {
            if (items.Count > 0)
            {
                items.RemoveAt(items.Count - 1);
                RequestCanvasRender();
            }
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
        using (var bmp = new Bitmap(32, 32))
        using (Graphics g = Graphics.FromImage(bmp))
        using (var pen = new Pen(Color.Red, 2f))
        using (var cap = new AdjustableArrowCap(7f, 9f, true))
        {
            pen.CustomEndCap = cap;
            g.DrawLine(pen, new PointF(2, 2), new PointF(30, 30));
        }

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
        penItem.Points.Add(new PointF(10, 10));
        AssertMeaningful(penItem, false);
        penItem.Points.Add(new PointF(20, 20));
        AssertMeaningful(penItem, true);
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
