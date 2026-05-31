using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal sealed class GpuRenderer : IDisposable
{
    private readonly IntPtr factory;
    private readonly IntPtr writeFactory;
    private readonly IntPtr target;
    private readonly IntPtr sourceImage;
    private readonly IntPtr roundStroke;
    private readonly IntPtr dashStroke;
    private readonly int sourceWidth;
    private readonly int sourceHeight;
    private readonly Dictionary<int, IntPtr> brushes = new Dictionary<int, IntPtr>();
    private readonly Dictionary<string, IntPtr> textFormats = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
    private bool disposed;

    private GpuRenderer(
        IntPtr factory,
        IntPtr writeFactory,
        IntPtr target,
        IntPtr sourceImage,
        IntPtr roundStroke,
        IntPtr dashStroke,
        int sourceWidth,
        int sourceHeight)
    {
        this.factory = factory;
        this.writeFactory = writeFactory;
        this.target = target;
        this.sourceImage = sourceImage;
        this.roundStroke = roundStroke;
        this.dashStroke = dashStroke;
        this.sourceWidth = sourceWidth;
        this.sourceHeight = sourceHeight;
    }

    public static GpuRenderer CreateForWindow(WicImageDocument document)
    {
        IntPtr factory = IntPtr.Zero;
        IntPtr writeFactory = IntPtr.Zero;
        IntPtr target = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        IntPtr round = IntPtr.Zero;
        IntPtr dash = IntPtr.Zero;
        GCHandle handle = default(GCHandle);
        try
        {
            factory = D2DApi.CreateFactory();
            writeFactory = DWriteApi.CreateFactory();
            target = D2DApi.CreateDCTarget(factory);
            handle = GCHandle.Alloc(document.Pixels, GCHandleType.Pinned);
            image = D2DApi.CreateImageFromMemory(target, document.Width, document.Height, handle.AddrOfPinnedObject(), (uint)(document.Width * 4));
            round = D2DApi.CreateStroke(factory, false);
            dash = D2DApi.CreateStroke(factory, true);
            return new GpuRenderer(factory, writeFactory, target, image, round, dash, document.Width, document.Height);
        }
        catch
        {
            ComUtil.Release(ref dash);
            ComUtil.Release(ref round);
            ComUtil.Release(ref image);
            ComUtil.Release(ref target);
            ComUtil.Release(ref writeFactory);
            ComUtil.Release(ref factory);
            throw;
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    public static GpuRenderer CreateForStore(WicImageDocument document, WicPixelStore store)
    {
        IntPtr factory = IntPtr.Zero;
        IntPtr writeFactory = IntPtr.Zero;
        IntPtr target = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        IntPtr round = IntPtr.Zero;
        IntPtr dash = IntPtr.Zero;
        GCHandle handle = default(GCHandle);
        try
        {
            factory = D2DApi.CreateFactory();
            writeFactory = DWriteApi.CreateFactory();
            target = D2DApi.CreateWicTarget(factory, store.Store);
            handle = GCHandle.Alloc(document.Pixels, GCHandleType.Pinned);
            image = D2DApi.CreateImageFromMemory(target, document.Width, document.Height, handle.AddrOfPinnedObject(), (uint)(document.Width * 4));
            round = D2DApi.CreateStroke(factory, false);
            dash = D2DApi.CreateStroke(factory, true);
            return new GpuRenderer(factory, writeFactory, target, image, round, dash, document.Width, document.Height);
        }
        catch
        {
            ComUtil.Release(ref dash);
            ComUtil.Release(ref round);
            ComUtil.Release(ref image);
            ComUtil.Release(ref target);
            ComUtil.Release(ref writeFactory);
            ComUtil.Release(ref factory);
            throw;
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    public void RenderToHdc(IntPtr hdc, int width, int height, GpuRect view, IList<AnnotationItem> items, AnnotationItem preview, int selectedIndex, bool drawToolbar, ToolMode tool, Rgba stroke, float strokeWidth, SettingsOverlayState settingsOverlay)
    {
        ThrowIfDisposed();
        D2DApi.BindDC(target, hdc, width, height);
        D2DApi.BeginDraw(target);
        try
        {
            DrawScene(width, height, view, items, preview, selectedIndex, drawToolbar, tool, stroke, strokeWidth, settingsOverlay);
        }
        finally
        {
            D2DApi.EndDraw(target);
        }
    }

    public void RenderOffscreen(GpuRect view, IList<AnnotationItem> items)
    {
        ThrowIfDisposed();
        D2DApi.BeginDraw(target);
        try
        {
            DrawScene((int)view.Width, (int)view.Height, view, items, null, -1, false, ToolMode.Rect, AppStyles.DefaultStroke, AppStyles.DefaultStrokeWidth, null);
        }
        finally
        {
            D2DApi.EndDraw(target);
        }
    }

    public static byte[] RenderExport(WicImageDocument document, IList<AnnotationItem> items)
    {
        long probeStart = RenderPerformanceProbe.Start();
        try
        {
            using (WicPixelStore store = WicCodec.CreateStore(document.Width, document.Height))
            using (GpuRenderer renderer = CreateForStore(document, store))
            {
                renderer.RenderOffscreen(new GpuRect(0, 0, document.Width, document.Height), items);
                return store.CopyPixels();
            }
        }
        finally
        {
            RenderPerformanceProbe.Stop(RenderPerformanceProbe.FinalImageRender, probeStart);
        }
    }

    private void DrawScene(int width, int height, GpuRect view, IList<AnnotationItem> items, AnnotationItem preview, int selectedIndex, bool drawToolbar, ToolMode tool, Rgba stroke, float strokeWidth, SettingsOverlayState settingsOverlay)
    {
        RenderPerformanceProbe.MarkFrameStart();
        D2DApi.SetTransform(target, D2DApi.Matrix.Identity);
        D2DApi.Clear(target, AppStyles.CanvasBack);

        GpuRect source = new GpuRect(0, 0, sourceWidth, sourceHeight);
        D2DApi.DrawImageSection(target, sourceImage, view, source, D2DApi.InterpolationLinear);

        float scale = view.Width / Math.Max(1f, sourceWidth);
        D2DApi.SetTransform(target, D2DApi.Matrix.ScaleTranslate(scale, view.X, view.Y));
        int skipIndex = preview != null && preview.TextEditing && selectedIndex >= 0 ? selectedIndex : -1;
        DrawAnnotations(items, skipIndex);
        if (preview != null)
        {
            DrawAnnotation(preview);
        }
        if (selectedIndex >= 0 && selectedIndex < items.Count)
        {
            DrawSelection(items[selectedIndex], scale);
        }
        D2DApi.SetTransform(target, D2DApi.Matrix.Identity);

        if (drawToolbar)
        {
            DrawToolbar(width, tool, settingsOverlay != null && settingsOverlay.TopMost);
            if (settingsOverlay != null && (settingsOverlay.ToolOptionsVisible || settingsOverlay.ToolOptionsOpacity > 0.01f))
            {
                DrawToolOptions(tool, stroke, strokeWidth, settingsOverlay.ToolOptionsOpacity);
            }
        }
        if (settingsOverlay != null && settingsOverlay.Visible)
        {
            DrawSettingsOverlay(width, height, settingsOverlay);
        }
        else if (settingsOverlay != null && !string.IsNullOrEmpty(settingsOverlay.Tooltip))
        {
            DrawTooltip(width, height, settingsOverlay.Tooltip, settingsOverlay.TooltipPoint);
        }
    }

    private void DrawAnnotations(IList<AnnotationItem> items, int skipIndex)
    {
        long probeStart = RenderPerformanceProbe.Start();
        try
        {
            if (items == null)
            {
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (i == skipIndex)
                {
                    continue;
                }
                DrawAnnotation(items[i]);
            }
        }
        finally
        {
            RenderPerformanceProbe.Stop(RenderPerformanceProbe.Direct2DAnnotationDraw, probeStart);
        }
    }

    private void DrawAnnotation(AnnotationItem item)
    {
        if (item == null)
        {
            return;
        }
        Rgba rgba = item.Stroke.A == 0 ? AppStyles.DefaultStroke : item.Stroke;
        float width = Math.Max(0.1f, item.StrokeWidth);
        IntPtr brush = GetBrush(rgba);
        GpuRect rect = GpuRect.Normalize(item.Start, item.End);
        switch (item.Tool)
        {
            case ToolMode.Rect:
                D2DApi.DrawRectangle(target, rect, brush, width, roundStroke);
                break;
            case ToolMode.Ellipse:
                D2DApi.DrawEllipse(target, rect, brush, width, roundStroke);
                break;
            case ToolMode.Pen:
                DrawPolyline(item.GetDrawingPoints(), brush, width);
                break;
            case ToolMode.Arrow:
                DrawPolygon(BuildArrow(item, width), brush);
                break;
            case ToolMode.Text:
                DrawText(item.Text, item.Start, rgba, width, item);
                break;
            case ToolMode.Mosaic:
                DrawMosaic(rect);
                break;
        }
    }

    private void DrawMosaic(GpuRect rect)
    {
        if (rect.Width < 2f || rect.Height < 2f)
        {
            return;
        }

        const float block = 18f;
        IntPtr shade = GetBrush(AppStyles.OverlayShade);
        IntPtr dark = GetBrush(Rgba.FromArgb(190, 0, 0, 0));
        for (float y = rect.Top; y < rect.Bottom; y += block)
        {
            for (float x = rect.Left; x < rect.Right; x += block)
            {
                float w = Math.Min(block, rect.Right - x);
                float h = Math.Min(block, rect.Bottom - y);
                GpuRect destination = new GpuRect(x, y, Math.Max(1f, w), Math.Max(1f, h));
                GpuRect source = new GpuRect(
                    Math.Min(sourceWidth - 1f, Math.Max(0f, x + w / 2f)),
                    Math.Min(sourceHeight - 1f, Math.Max(0f, y + h / 2f)),
                    1f,
                    1f);
                D2DApi.DrawImageSection(target, sourceImage, destination, source, D2DApi.InterpolationNearest);
                D2DApi.FillRectangle(target, destination, dark);
            }
        }
        D2DApi.FillRectangle(target, rect, shade);
    }

    private void DrawPolyline(GpuPoint[] points, IntPtr brush, float width)
    {
        if (points == null || points.Length < 2)
        {
            return;
        }
        for (int i = 1; i < points.Length; i++)
        {
            D2DApi.DrawLine(target, points[i - 1], points[i], brush, width, roundStroke);
        }
    }

    private void DrawPolygon(GpuPoint[] points, IntPtr brush)
    {
        if (points == null || points.Length < 3)
        {
            return;
        }
        IntPtr path = IntPtr.Zero;
        IntPtr sink = IntPtr.Zero;
        try
        {
            path = D2DApi.CreatePath(factory);
            sink = D2DApi.OpenPath(path);
            D2DApi.SetFillMode(sink);
            D2DApi.BeginFigure(sink, points[0]);
            D2DApi.AddLines(sink, points, 1, points.Length - 1);
            D2DApi.EndFigure(sink);
            D2DApi.CloseSink(sink);
            D2DApi.FillPath(target, path, brush);
        }
        finally
        {
            ComUtil.Release(ref sink);
            ComUtil.Release(ref path);
        }
    }

    private void DrawText(string text, GpuPoint origin, Rgba rgba, float strokeWidth, AnnotationItem item)
    {
        text = text ?? string.Empty;
        bool editing = item != null && item.TextEditing;
        if (text.Length == 0 && !editing)
        {
            return;
        }
        float em = Math.Max(1f, strokeWidth * 6f);
        IntPtr brush = GetBrush(rgba);
        IntPtr format = GetTextFormat(AppStyles.UiFontName, em);
        GpuRect layout = new GpuRect(origin.X, origin.Y, Math.Max(80f, sourceWidth - origin.X), Math.Max(em * 1.5f, sourceHeight - origin.Y));
        if (editing)
        {
            DrawTextEditingChrome(text, origin, em, item);
        }
        if (text.Length > 0)
        {
            D2DApi.DrawText(target, text, format, layout, brush);
        }
        if (editing)
        {
            DrawTextCaret(text, origin, em, rgba, item);
        }
    }

    private void DrawTextEditingChrome(string text, GpuPoint origin, float em, AnnotationItem item)
    {
        int selectionStart = Math.Min(ClampTextIndex(text, item.TextCaretIndex), ClampTextIndex(text, item.TextSelectionAnchor));
        int selectionEnd = Math.Max(ClampTextIndex(text, item.TextCaretIndex), ClampTextIndex(text, item.TextSelectionAnchor));
        if (selectionEnd > selectionStart)
        {
            float x = origin.X + MeasureTextPrefixWidth(text, em, selectionStart);
            float width = Math.Max(1f, MeasureTextPrefixWidth(text, em, selectionEnd) - MeasureTextPrefixWidth(text, em, selectionStart));
            D2DApi.FillRoundedRectangle(target, new GpuRect(x, origin.Y + em * 0.05f, width, em * 1.18f), 2f, GetBrush(Rgba.FromArgb(72, AppStyles.Accent.R, AppStyles.Accent.G, AppStyles.Accent.B)));
        }

        if (item.TextCompositionStart >= 0 && item.TextCompositionLength > 0)
        {
            int compStart = ClampTextIndex(text, item.TextCompositionStart);
            int compEnd = ClampTextIndex(text, item.TextCompositionStart + item.TextCompositionLength);
            if (compEnd > compStart)
            {
                float startX = origin.X + MeasureTextPrefixWidth(text, em, compStart);
                float endX = origin.X + MeasureTextPrefixWidth(text, em, compEnd);
                float y = origin.Y + em * 1.25f;
                D2DApi.DrawLine(target, new GpuPoint(startX, y), new GpuPoint(Math.Max(startX + 1f, endX), y), GetBrush(AppStyles.Accent), Math.Max(1f, em / 15f), roundStroke);
            }
        }
    }

    private void DrawTextCaret(string text, GpuPoint origin, float em, Rgba rgba, AnnotationItem item)
    {
        if (!item.TextCaretVisible)
        {
            return;
        }
        int caret = ClampTextIndex(text, item.TextCaretIndex);
        float x = origin.X + MeasureTextPrefixWidth(text, em, caret);
        float top = origin.Y + em * 0.05f;
        float bottom = origin.Y + em * 1.25f;
        D2DApi.DrawLine(target, new GpuPoint(x, top), new GpuPoint(x, bottom), GetBrush(rgba), Math.Max(1.25f, em / 15f), roundStroke);
    }

    private static float MeasureTextPrefixWidth(string text, float em, int length)
    {
        length = Math.Max(0, Math.Min(text == null ? 0 : text.Length, length));
        if (length == 0)
        {
            return 0f;
        }
        return MeasureTextBounds(text.Substring(0, length), em).Width;
    }

    private static int ClampTextIndex(string text, int index)
    {
        int length = text == null ? 0 : text.Length;
        if (index < 0)
        {
            return 0;
        }
        if (index > length)
        {
            return length;
        }
        return index;
    }

    private void DrawSelection(AnnotationItem item, float scale)
    {
        GpuRect rect = GetItemBounds(item);
        if (rect.IsEmpty)
        {
            return;
        }
        float safeScale = Math.Max(0.001f, scale);
        float width = Math.Max(1f, 1.25f / safeScale);
        float pad = Math.Max(3f, 4f / safeScale);
        rect = new GpuRect(rect.X - pad, rect.Y - pad, rect.Width + pad * 2f, rect.Height + pad * 2f);
        IntPtr accent = GetBrush(AppStyles.Accent);
        D2DApi.DrawRectangle(target, rect, accent, width, dashStroke);
        if (item.Tool == ToolMode.Arrow)
        {
            float arrowHandleSide = Math.Max(4f, 8f / safeScale);
            DrawSelectionHandle(item.Start.X, item.Start.Y, arrowHandleSide, width, accent);
            DrawSelectionHandle(item.End.X, item.End.Y, arrowHandleSide, width, accent);
            return;
        }
        if (item.Tool == ToolMode.Text)
        {
            return;
        }
        float side = Math.Max(3f, 7f / safeScale);
        DrawSelectionHandle(rect.Left, rect.Top, side, width, accent);
        DrawSelectionHandle(rect.Left + rect.Width / 2f, rect.Top, side, width, accent);
        DrawSelectionHandle(rect.Right, rect.Top, side, width, accent);
        DrawSelectionHandle(rect.Right, rect.Top + rect.Height / 2f, side, width, accent);
        DrawSelectionHandle(rect.Right, rect.Bottom, side, width, accent);
        DrawSelectionHandle(rect.Left + rect.Width / 2f, rect.Bottom, side, width, accent);
        DrawSelectionHandle(rect.Left, rect.Bottom, side, width, accent);
        DrawSelectionHandle(rect.Left, rect.Top + rect.Height / 2f, side, width, accent);
    }

    private void DrawSelectionHandle(float x, float y, float side, float width, IntPtr accent)
    {
        GpuRect rect = new GpuRect(x - side / 2f, y - side / 2f, side, side);
        D2DApi.FillEllipse(target, rect, GetBrush(Rgba.White));
        D2DApi.DrawEllipse(target, rect, accent, width, roundStroke);
    }

    private void DrawToolbar(int clientWidth, ToolMode currentTool, bool topMost)
    {
        IntPtr surface = GetBrush(AppStyles.ToolbarSurface);
        IntPtr border = GetBrush(AppStyles.ToolbarBorder);
        D2DApi.FillRectangle(target, new GpuRect(0, 0, clientWidth, AppStyles.ToolbarHeight), GetBrush(AppStyles.AppBack));
        GpuRect pill = new GpuRect(16, 12, Math.Max(1, clientWidth - 32), 58);
        D2DApi.FillRoundedRectangle(target, new GpuRect(pill.X, pill.Y + 3, pill.Width, pill.Height), 10f, GetBrush(AppStyles.ToolbarShadow));
        D2DApi.FillRoundedRectangle(target, pill, 10f, surface);
        D2DApi.DrawRoundedRectangle(target, pill, 10f, border, 1f, roundStroke);
        DrawToolbarSeparator(284);
        DrawToolbarSeparator(519);

        ToolbarCommand[] commands = GpuAnnotatorWindow.ToolbarCommands;
        for (int i = 0; i < commands.Length; i++)
        {
            GpuRect rect = GpuAnnotatorWindow.GetToolbarButtonRect(i);
            bool selected = IsSelected(commands[i], currentTool) || (commands[i] == ToolbarCommand.Pin && topMost);
            Rgba fore = selected ? AppStyles.Accent : IconRgba(commands[i]);
            if (selected)
            {
                D2DApi.FillRoundedRectangle(target, new GpuRect(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4), 7f, GetBrush(AppStyles.ToolbarSelectedBack));
            }
            DrawToolbarIcon(commands[i], rect, fore);
        }
    }

    private void DrawToolbarSeparator(float x)
    {
        D2DApi.FillRectangle(target, new GpuRect(x, AppStyles.ToolbarSeparatorTop, 1, AppStyles.ToolbarSeparatorHeight), GetBrush(AppStyles.ToolbarSeparator));
    }

    private void DrawToolbarIcon(ToolbarCommand command, GpuRect rect, Rgba color)
    {
        float cx = rect.X + rect.Width / 2f;
        float cy = rect.Y + rect.Height / 2f;
        IntPtr brush = GetBrush(color);
        float line = 1.8f;
        switch (command)
        {
            case ToolbarCommand.ToolRect:
                D2DApi.DrawRectangle(target, new GpuRect(cx - 9, cy - 9, 18, 18), brush, line, roundStroke);
                break;
            case ToolbarCommand.ToolEllipse:
                D2DApi.DrawEllipse(target, new GpuRect(cx - 10, cy - 10, 20, 20), brush, line, roundStroke);
                break;
            case ToolbarCommand.ToolArrow:
                DrawIconPolygon(new GpuPoint[]
                {
                    RotateIconPoint(cx, cy, -11f, -1.1f, -45f),
                    RotateIconPoint(cx, cy, 3f, -2.4f, -45f),
                    RotateIconPoint(cx, cy, 3f, -7f, -45f),
                    RotateIconPoint(cx, cy, 12f, 0f, -45f),
                    RotateIconPoint(cx, cy, 3f, 7f, -45f),
                    RotateIconPoint(cx, cy, 3f, 2.4f, -45f),
                    RotateIconPoint(cx, cy, -11f, 1.1f, -45f)
                }, brush);
                break;
            case ToolbarCommand.ToolPen:
                DrawIconLine(RotateIconPoint(cx, cy, -8, -3, -45), RotateIconPoint(cx, cy, 6, -3, -45), brush, line);
                DrawIconLine(RotateIconPoint(cx, cy, 6, -3, -45), RotateIconPoint(cx, cy, 11, 0, -45), brush, line);
                DrawIconLine(RotateIconPoint(cx, cy, 11, 0, -45), RotateIconPoint(cx, cy, 6, 3, -45), brush, line);
                DrawIconLine(RotateIconPoint(cx, cy, 6, 3, -45), RotateIconPoint(cx, cy, -8, 3, -45), brush, line);
                DrawIconLine(RotateIconPoint(cx, cy, -8, 3, -45), RotateIconPoint(cx, cy, -8, -3, -45), brush, line);
                DrawIconLine(RotateIconPoint(cx, cy, -10, 4, -45), RotateIconPoint(cx, cy, -6, 8, -45), brush, line);
                break;
            case ToolbarCommand.ToolText:
                D2DApi.DrawRectangle(target, new GpuRect(cx - 10, cy - 10, 20, 20), brush, line, roundStroke);
                DrawIconLine(new GpuPoint(cx - 6, cy - 5), new GpuPoint(cx + 6, cy - 5), brush, 2f);
                DrawIconLine(new GpuPoint(cx, cy - 5), new GpuPoint(cx, cy + 8), brush, 2f);
                break;
            case ToolbarCommand.ToolMosaic:
                D2DApi.DrawRectangle(target, new GpuRect(cx - 10, cy - 10, 20, 20), brush, line, roundStroke);
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        if ((row + col) % 2 == 0)
                        {
                            D2DApi.FillRectangle(target, new GpuRect(cx - 7 + col * 5, cy - 7 + row * 5, 3, 3), brush);
                        }
                    }
                }
                break;
            case ToolbarCommand.Undo:
                DrawIconLine(new GpuPoint(cx + 8, cy + 7), new GpuPoint(cx + 3, cy - 1), brush, line);
                DrawIconLine(new GpuPoint(cx + 3, cy - 1), new GpuPoint(cx - 4, cy - 3), brush, line);
                DrawIconLine(new GpuPoint(cx - 4, cy - 3), new GpuPoint(cx - 8, cy - 1), brush, line);
                DrawIconLine(new GpuPoint(cx - 8, cy - 1), new GpuPoint(cx - 3, cy - 7), brush, line);
                DrawIconLine(new GpuPoint(cx - 8, cy - 1), new GpuPoint(cx - 1, cy + 2), brush, line);
                break;
            case ToolbarCommand.Clear:
                DrawIconLine(new GpuPoint(cx - 7, cy - 8), new GpuPoint(cx + 7, cy - 8), brush, line);
                DrawIconLine(new GpuPoint(cx - 3, cy - 11), new GpuPoint(cx + 3, cy - 11), brush, line);
                D2DApi.DrawRectangle(target, new GpuRect(cx - 6, cy - 5, 12, 15), brush, line, roundStroke);
                DrawIconLine(new GpuPoint(cx - 2, cy - 2), new GpuPoint(cx - 2, cy + 7), brush, line);
                DrawIconLine(new GpuPoint(cx + 2, cy - 2), new GpuPoint(cx + 2, cy + 7), brush, line);
                break;
            case ToolbarCommand.Fit:
                DrawCorner(cx - 9, cy - 9, 1, 1, brush);
                DrawCorner(cx + 9, cy - 9, -1, 1, brush);
                DrawCorner(cx - 9, cy + 9, 1, -1, brush);
                DrawCorner(cx + 9, cy + 9, -1, -1, brush);
                break;
            case ToolbarCommand.Pin:
                DrawIconLine(new GpuPoint(cx - 5, cy - 10), new GpuPoint(cx + 5, cy - 10), brush, line);
                DrawIconLine(new GpuPoint(cx - 3, cy - 10), new GpuPoint(cx - 3, cy - 4), brush, line);
                DrawIconLine(new GpuPoint(cx + 3, cy - 10), new GpuPoint(cx + 3, cy - 4), brush, line);
                DrawIconLine(new GpuPoint(cx - 7, cy - 4), new GpuPoint(cx + 7, cy - 4), brush, line);
                DrawIconLine(new GpuPoint(cx - 5, cy - 4), new GpuPoint(cx, cy + 2), brush, line);
                DrawIconLine(new GpuPoint(cx + 5, cy - 4), new GpuPoint(cx, cy + 2), brush, line);
                DrawIconLine(new GpuPoint(cx, cy + 2), new GpuPoint(cx, cy + 11), brush, line);
                break;
            case ToolbarCommand.Settings:
                for (int i = 0; i < 8; i++)
                {
                    double angle = Math.PI * 2d * i / 8d;
                    GpuPoint a = new GpuPoint(cx + (float)Math.Cos(angle) * 8.2f, cy + (float)Math.Sin(angle) * 8.2f);
                    GpuPoint b = new GpuPoint(cx + (float)Math.Cos(angle) * 11f, cy + (float)Math.Sin(angle) * 11f);
                    DrawIconLine(a, b, brush, 1.9f);
                }
                D2DApi.DrawEllipse(target, new GpuRect(cx - 7, cy - 7, 14, 14), brush, 1.9f, roundStroke);
                D2DApi.DrawEllipse(target, new GpuRect(cx - 2, cy - 2, 4, 4), brush, 1.9f, roundStroke);
                break;
            case ToolbarCommand.Save:
                DrawIconLine(new GpuPoint(cx - 9, cy), new GpuPoint(cx - 3, cy + 6), brush, 2.2f);
                DrawIconLine(new GpuPoint(cx - 3, cy + 6), new GpuPoint(cx + 10, cy - 8), brush, 2.2f);
                break;
            case ToolbarCommand.Cancel:
                DrawIconLine(new GpuPoint(cx - 8, cy - 8), new GpuPoint(cx + 8, cy + 8), brush, 2.1f);
                DrawIconLine(new GpuPoint(cx + 8, cy - 8), new GpuPoint(cx - 8, cy + 8), brush, 2.1f);
                break;
        }
    }

    private void DrawIconLine(GpuPoint a, GpuPoint b, IntPtr brush, float width)
    {
        D2DApi.DrawLine(target, a, b, brush, width, roundStroke);
    }

    private void DrawCorner(float x, float y, int dx, int dy, IntPtr brush)
    {
        DrawIconLine(new GpuPoint(x, y), new GpuPoint(x + dx * 6, y), brush, 1.8f);
        DrawIconLine(new GpuPoint(x, y), new GpuPoint(x, y + dy * 6), brush, 1.8f);
    }

    private void DrawIconPolygon(GpuPoint[] points, IntPtr brush)
    {
        DrawPolygon(points, brush);
    }

    private static GpuPoint RotateIconPoint(float cx, float cy, float x, float y, float degrees)
    {
        double radians = degrees * Math.PI / 180d;
        float cos = (float)Math.Cos(radians);
        float sin = (float)Math.Sin(radians);
        return new GpuPoint(cx + x * cos - y * sin, cy + x * sin + y * cos);
    }

    private void DrawTooltip(int clientWidth, int clientHeight, string text, GpuPoint anchor)
    {
        GpuRect measured = MeasureTextBounds(text, 12f);
        float width = Math.Min(220f, Math.Max(54f, measured.Width + 18f));
        float height = 30f;
        float x = Math.Min(clientWidth - width - 8f, Math.Max(8f, anchor.X + 12f));
        float y = Math.Min(clientHeight - height - 8f, Math.Max(AppStyles.ToolbarHeight + 4f, anchor.Y + 14f));
        GpuRect rect = new GpuRect(x, y, width, height);
        D2DApi.FillRoundedRectangle(target, rect, 7f, GetBrush(Rgba.White));
        D2DApi.DrawRoundedRectangle(target, rect, 7f, GetBrush(AppStyles.ToolbarBorder), 1f, roundStroke);
        D2DApi.DrawText(target, text, GetTextFormat(AppStyles.UiFontName, 12f), new GpuRect(rect.X + 9, rect.Y + 7, rect.Width - 18, rect.Height - 8), GetBrush(AppStyles.ToolbarIcon));
    }

    private void DrawToolOptions(ToolMode tool, Rgba stroke, float strokeWidth, float opacity)
    {
        if (!ToolSupportsOptions(tool))
        {
            return;
        }

        float eased = EaseOutCubic(Clamp01(opacity));
        if (eased <= 0.01f)
        {
            return;
        }

        GpuRect panel = OffsetRect(GetToolOptionsPanelRect(), 0f, -7f + 7f * eased);
        D2DApi.FillRoundedRectangle(target, new GpuRect(panel.X + 1, panel.Y + 5, panel.Width, panel.Height), 6f, GetBrush(WithOpacity(AppStyles.ToolbarShadow, eased)));
        D2DApi.FillRoundedRectangle(target, panel, 6f, GetBrush(WithOpacity(AppStyles.ToolbarSurface, eased)));
        D2DApi.DrawRoundedRectangle(target, panel, 6f, GetBrush(WithOpacity(AppStyles.ToolbarBorder, eased)), 1f, roundStroke);
        for (int i = 0; i < AppStyles.Palette.Length; i++)
        {
            GpuRect rect = OffsetRect(GetPaletteSwatchRect(i), 0f, panel.Y - GetToolOptionsPanelRect().Y);
            bool selected = AppStyles.Palette[i].Packed == stroke.Packed;
            if (selected)
            {
                D2DApi.FillRoundedRectangle(target, Inflate(rect, 5f, 5f), 5f, GetBrush(WithOpacity(AppStyles.ToolbarSelectedBack, eased)));
            }
            D2DApi.FillRoundedRectangle(target, rect, 3f, GetBrush(WithOpacity(AppStyles.Palette[i], eased)));
            Rgba border = selected ? AppStyles.Accent : (AppStyles.Palette[i].Packed == Rgba.White.Packed ? AppStyles.ToolbarBorder : Rgba.Transparent);
            if (border.A > 0)
            {
                D2DApi.DrawRoundedRectangle(target, rect, 3f, GetBrush(WithOpacity(border, eased)), selected ? 2.2f : 1f, roundStroke);
            }
        }

        for (int i = 0; i < AppStyles.StrokeWidths.Length; i++)
        {
            GpuRect rect = OffsetRect(GetWidthOptionRect(i), 0f, panel.Y - GetToolOptionsPanelRect().Y);
            bool selected = Math.Abs(AppStyles.StrokeWidths[i] - strokeWidth) < 0.01f;
            Rgba border = selected ? AppStyles.Accent : AppStyles.ToolbarBorder;
            D2DApi.FillRoundedRectangle(target, rect, 5f, GetBrush(WithOpacity(selected ? AppStyles.ToolbarSelectedBack : AppStyles.FieldBack, eased)));
            D2DApi.DrawRoundedRectangle(target, rect, 5f, GetBrush(WithOpacity(border, eased)), selected ? 2.2f : 1f, roundStroke);
            float y = rect.Y + rect.Height / 2f;
            D2DApi.DrawLine(target, new GpuPoint(rect.X + 9, y), new GpuPoint(rect.Right - 9, y), GetBrush(WithOpacity(stroke, eased)), AppStyles.StrokeWidths[i], roundStroke);
        }
    }

    public static bool ToolSupportsOptions(ToolMode tool)
    {
        return tool == ToolMode.Rect ||
            tool == ToolMode.Ellipse ||
            tool == ToolMode.Arrow ||
            tool == ToolMode.Pen ||
            tool == ToolMode.Text;
    }

    public static GpuRect GetToolOptionsPanelRect()
    {
        return new GpuRect(20, AppStyles.ToolbarHeight + 9, 382, 54);
    }

    public static GpuRect GetPaletteSwatchRect(int index)
    {
        GpuRect panel = GetToolOptionsPanelRect();
        return new GpuRect(panel.X + 14 + index * 34, panel.Y + 14, 24, 24);
    }

    public static GpuRect GetWidthOptionRect(int index)
    {
        GpuRect panel = GetToolOptionsPanelRect();
        return new GpuRect(panel.X + 230 + index * 38, panel.Y + 12, 34, 30);
    }

    private void DrawSettingsOverlay(int clientWidth, int clientHeight, SettingsOverlayState overlay)
    {
        IntPtr shade = GetBrush(Rgba.FromArgb(118, 0, 0, 0));
        D2DApi.FillRectangle(target, new GpuRect(0, 0, clientWidth, clientHeight), shade);
        GpuRect panel = GetSettingsOverlayPanel(clientWidth, clientHeight);
        D2DApi.FillRoundedRectangle(target, new GpuRect(panel.X + 1, panel.Y + 8, panel.Width, panel.Height), 8f, GetBrush(Rgba.FromArgb(42, 0, 0, 0)));
        D2DApi.FillRoundedRectangle(target, panel, 8f, GetBrush(Rgba.White));
        D2DApi.DrawRoundedRectangle(target, panel, 8f, GetBrush(AppStyles.ToolbarBorder), 1f, roundStroke);
        D2DApi.DrawText(target, UiText.SaveDirectory, GetTextFormat(AppStyles.UiFontName, 15f), new GpuRect(panel.X + 24, panel.Y + 20, panel.Width - 48, 26), GetBrush(AppStyles.ToolbarIcon));
        string value = overlay.OutputDirectory ?? string.Empty;
        GpuRect field = GetSettingsOutputRect(panel);
        D2DApi.FillRoundedRectangle(target, field, 5f, GetBrush(AppStyles.FieldBack));
        D2DApi.DrawRoundedRectangle(target, field, 5f, GetBrush(AppStyles.ToolbarBorder), 1f, roundStroke);
        D2DApi.DrawText(target, value, GetTextFormat(AppStyles.UiFontName, 12f), new GpuRect(field.X + 11, field.Y + 8, field.Width - 22, field.Height - 10), GetBrush(AppStyles.ToolbarIcon));
        string hint = UiText.EmptyOutputDirectoryHintPrefix + GetClipboardTempDirectory();
        D2DApi.DrawText(target, hint, GetTextFormat(AppStyles.UiFontName, 12f), new GpuRect(field.X, field.Bottom + 10, panel.Width - 48, 34), GetBrush(AppStyles.MutedText));
        DrawOverlayButton(GetSettingsButtonRect(panel, SettingsOverlayCommand.Browse), UiText.Browse, AppStyles.Accent, false);
        DrawOverlayButton(GetSettingsButtonRect(panel, SettingsOverlayCommand.Clear), UiText.Clear, AppStyles.ToolbarIcon, false);
        DrawOverlayButton(GetSettingsButtonRect(panel, SettingsOverlayCommand.Save), UiText.Save, AppStyles.SaveAccent, true);
        DrawOverlayButton(GetSettingsButtonRect(panel, SettingsOverlayCommand.Cancel), UiText.Cancel, AppStyles.CancelAccent, false);
    }

    private void DrawOverlayButton(GpuRect rect, string text, Rgba color, bool primary)
    {
        Rgba fill = primary ? color : Tint(color, 0.94f);
        Rgba textColor = primary ? Rgba.White : color;
        D2DApi.FillRoundedRectangle(target, rect, 5f, GetBrush(fill));
        D2DApi.DrawRoundedRectangle(target, rect, 5f, GetBrush(color), primary ? 0.8f : 1.2f, roundStroke);
        D2DApi.DrawText(target, text, GetTextFormat(AppStyles.UiFontName, 13f), new GpuRect(rect.X + 10, rect.Y + 8, rect.Width - 20, rect.Height - 10), GetBrush(textColor));
    }

    public static GpuRect GetSettingsOverlayPanel(int clientWidth, int clientHeight)
    {
        float width = Math.Min(620f, Math.Max(380f, clientWidth - 48f));
        float height = 190f;
        return new GpuRect((clientWidth - width) / 2f, Math.Max(16f, (clientHeight - height) / 2f), width, height);
    }

    public static GpuRect GetSettingsOutputRect(GpuRect panel)
    {
        return new GpuRect(panel.X + 24, panel.Y + 50, Math.Max(160f, panel.Width - 170f), 34);
    }

    public static GpuRect GetSettingsButtonRect(GpuRect panel, SettingsOverlayCommand command)
    {
        switch (command)
        {
            case SettingsOverlayCommand.Browse:
                return new GpuRect(panel.Right - 110, panel.Y + 49, 82, 30);
            case SettingsOverlayCommand.Clear:
                return new GpuRect(panel.Right - 110, panel.Y + 86, 82, 30);
            case SettingsOverlayCommand.Cancel:
                return new GpuRect(panel.Right - 110, panel.Bottom - 41, 82, 30);
            case SettingsOverlayCommand.Save:
                return new GpuRect(panel.Right - 200, panel.Bottom - 41, 82, 30);
            default:
                return new GpuRect();
        }
    }

    private static string GetClipboardTempDirectory()
    {
        return System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    private static bool IsSelected(ToolbarCommand command, ToolMode tool)
    {
        return (command == ToolbarCommand.ToolRect && tool == ToolMode.Rect) ||
            (command == ToolbarCommand.ToolEllipse && tool == ToolMode.Ellipse) ||
            (command == ToolbarCommand.ToolArrow && tool == ToolMode.Arrow) ||
            (command == ToolbarCommand.ToolPen && tool == ToolMode.Pen) ||
            (command == ToolbarCommand.ToolMosaic && tool == ToolMode.Mosaic) ||
            (command == ToolbarCommand.ToolText && tool == ToolMode.Text);
    }

    private static Rgba IconRgba(ToolbarCommand command)
    {
        if (command == ToolbarCommand.Save)
        {
            return AppStyles.SaveAccent;
        }
        if (command == ToolbarCommand.Cancel)
        {
            return AppStyles.CancelAccent;
        }
        return AppStyles.ToolbarIcon;
    }

    private static string ToolbarLabel(ToolbarCommand command)
    {
        switch (command)
        {
            case ToolbarCommand.ToolRect:
                return "□";
            case ToolbarCommand.ToolEllipse:
                return "○";
            case ToolbarCommand.ToolArrow:
                return "↗";
            case ToolbarCommand.ToolPen:
                return "\u7b14";
            case ToolbarCommand.ToolMosaic:
                return "\u9a6c";
            case ToolbarCommand.ToolText:
                return "\u5b57";
            case ToolbarCommand.Undo:
                return "↶";
            case ToolbarCommand.Clear:
                return "\u6e05";
            case ToolbarCommand.Fit:
                return "\u9002";
            case ToolbarCommand.Pin:
                return "\u7f6e";
            case ToolbarCommand.Settings:
                return "\u8bbe";
            case ToolbarCommand.Cancel:
                return "×";
            case ToolbarCommand.Save:
                return "✓";
            default:
                return "?";
        }
    }

    private IntPtr GetBrush(Rgba rgba)
    {
        IntPtr brush;
        if (brushes.TryGetValue(rgba.Packed, out brush))
        {
            return brush;
        }
        brush = D2DApi.CreateBrush(target, rgba);
        brushes.Add(rgba.Packed, brush);
        return brush;
    }

    private IntPtr GetTextFormat(string family, float em)
    {
        string key = family + "\0" + em.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        IntPtr format;
        if (textFormats.TryGetValue(key, out format))
        {
            return format;
        }
        format = DWriteApi.CreateTextFormat(writeFactory, family, em);
        textFormats.Add(key, format);
        return format;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        foreach (IntPtr brush in brushes.Values)
        {
            IntPtr local = brush;
            ComUtil.Release(ref local);
        }
        brushes.Clear();
        foreach (IntPtr format in textFormats.Values)
        {
            IntPtr local = format;
            ComUtil.Release(ref local);
        }
        textFormats.Clear();
        IntPtr localSource = sourceImage;
        IntPtr localTarget = target;
        IntPtr localWrite = writeFactory;
        IntPtr localFactory = factory;
        IntPtr localRound = roundStroke;
        IntPtr localDash = dashStroke;
        ComUtil.Release(ref localDash);
        ComUtil.Release(ref localRound);
        ComUtil.Release(ref localSource);
        ComUtil.Release(ref localTarget);
        ComUtil.Release(ref localWrite);
        ComUtil.Release(ref localFactory);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException("GpuRenderer");
        }
    }

    public static GpuRect GetItemBounds(AnnotationItem item)
    {
        if (item == null)
        {
            return new GpuRect();
        }
        float itemWidth = item.StrokeWidth > 0f ? item.StrokeWidth : AppStyles.DefaultStrokeWidth;
        if (item.Tool == ToolMode.Pen)
        {
            GpuPoint[] points = item.GetDrawingPoints();
            if (points.Length == 0)
            {
                return new GpuRect();
            }
            float left = points[0].X;
            float top = points[0].Y;
            float right = points[0].X;
            float bottom = points[0].Y;
            for (int i = 1; i < points.Length; i++)
            {
                left = Math.Min(left, points[i].X);
                top = Math.Min(top, points[i].Y);
                right = Math.Max(right, points[i].X);
                bottom = Math.Max(bottom, points[i].Y);
            }
            return Inflate(GpuRect.FromEdges(left, top, right, bottom), Math.Max(2f, itemWidth), Math.Max(2f, itemWidth));
        }
        if (item.Tool == ToolMode.Arrow)
        {
            return Inflate(GpuRect.Normalize(item.Start, item.End), Math.Max(6f, itemWidth * 3f), Math.Max(6f, itemWidth * 3f));
        }
        if (item.Tool == ToolMode.Text)
        {
            float em = Math.Max(1f, item.StrokeWidth * 6f);
            GpuRect measured = MeasureTextBounds(item.Text, em);
            return new GpuRect(item.Start.X, item.Start.Y, Math.Max(em, measured.Width), Math.Max(em, measured.Height));
        }
        GpuRect bounds = GpuRect.Normalize(item.Start, item.End);
        if (bounds.Width < 1f)
        {
            bounds = Inflate(bounds, 0.5f, 0f);
        }
        if (bounds.Height < 1f)
        {
            bounds = Inflate(bounds, 0f, 0.5f);
        }
        return bounds;
    }

    public static GpuRect MeasureTextBounds(string text, float em)
    {
        text = text ?? string.Empty;
        if (text.Length == 0)
        {
            return new GpuRect(0, 0, em, em);
        }

        IntPtr factory = IntPtr.Zero;
        IntPtr format = IntPtr.Zero;
        IntPtr layout = IntPtr.Zero;
        try
        {
            factory = DWriteApi.CreateFactory();
            format = DWriteApi.CreateTextFormat(factory, AppStyles.UiFontName, Math.Max(1f, em));
            layout = DWriteApi.CreateTextLayout(factory, text, format, 100000f, 10000f);
            DWriteApi.TextMetrics metrics = DWriteApi.GetMetrics(layout);
            return new GpuRect(metrics.left, metrics.top, Math.Max(1f, metrics.widthIncludingTrailingWhitespace), Math.Max(1f, metrics.height));
        }
        finally
        {
            ComUtil.Release(ref layout);
            ComUtil.Release(ref format);
            ComUtil.Release(ref factory);
        }
    }

    public static TextHitResult HitTestText(string text, float em, GpuPoint point)
    {
        IntPtr factory = IntPtr.Zero;
        IntPtr format = IntPtr.Zero;
        IntPtr layout = IntPtr.Zero;
        try
        {
            factory = DWriteApi.CreateFactory();
            format = DWriteApi.CreateTextFormat(factory, AppStyles.UiFontName, Math.Max(1f, em));
            layout = DWriteApi.CreateTextLayout(factory, text ?? string.Empty, format, 100000f, 10000f);
            return DWriteApi.HitTestPoint(layout, point.X, point.Y);
        }
        finally
        {
            ComUtil.Release(ref layout);
            ComUtil.Release(ref format);
            ComUtil.Release(ref factory);
        }
    }

    public static GpuPoint[] BuildArrow(AnnotationItem item, float width)
    {
        float dx = item.End.X - item.Start.X;
        float dy = item.End.Y - item.Start.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length < 2f)
        {
            return new GpuPoint[0];
        }

        float ux = dx / length;
        float uy = dy / length;
        float nx = -uy;
        float ny = ux;

        float headLength = Math.Min(length * 0.55f, Math.Max(14f, width * 5.5f));
        float tailHalf = Math.Max(0.7f, width * 0.22f);
        float neckHalf = Math.Max(1.2f, width * 0.65f);
        float headHalf = Math.Max(neckHalf * 2.2f, width * 2.4f);
        GpuPoint neck = new GpuPoint(item.End.X - ux * headLength, item.End.Y - uy * headLength);
        return new GpuPoint[]
        {
            Offset(item.Start, nx, ny, -tailHalf),
            Offset(neck, nx, ny, -neckHalf),
            Offset(neck, nx, ny, -headHalf),
            item.End,
            Offset(neck, nx, ny, headHalf),
            Offset(neck, nx, ny, neckHalf),
            Offset(item.Start, nx, ny, tailHalf)
        };
    }

    private static GpuRect Inflate(GpuRect rect, float dx, float dy)
    {
        return new GpuRect(rect.X - dx, rect.Y - dy, rect.Width + dx * 2f, rect.Height + dy * 2f);
    }

    private static GpuRect OffsetRect(GpuRect rect, float dx, float dy)
    {
        return new GpuRect(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
    }

    private static Rgba WithOpacity(Rgba color, float opacity)
    {
        float clamped = Clamp01(opacity);
        return Rgba.FromArgb((int)Math.Round(color.A * clamped), color.R, color.G, color.B);
    }

    private static Rgba Tint(Rgba color, float amount)
    {
        float clamped = Clamp01(amount);
        return Rgba.FromRgb(
            (int)Math.Round(color.R + (255 - color.R) * clamped),
            (int)Math.Round(color.G + (255 - color.G) * clamped),
            (int)Math.Round(color.B + (255 - color.B) * clamped));
    }

    private static float EaseOutCubic(float value)
    {
        float t = Clamp01(value);
        float inverse = 1f - t;
        return 1f - inverse * inverse * inverse;
    }

    private static float Clamp01(float value)
    {
        if (value < 0f)
        {
            return 0f;
        }
        if (value > 1f)
        {
            return 1f;
        }
        return value;
    }

    private static GpuPoint Offset(GpuPoint point, float nx, float ny, float distance)
    {
        return new GpuPoint(point.X + nx * distance, point.Y + ny * distance);
    }
}
