using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed class BufferedCanvas : Panel
{
    public BufferedCanvas()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        TabStop = true;
        DoubleBuffered = true;
        UpdateStyles();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using (var brush = new SolidBrush(BackColor))
        {
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }
}

internal enum ToolbarIconKind
{
    Rect,
    Ellipse,
    Arrow,
    Pen,
    Text,
    Mosaic,
    Undo,
    Clear,
    Fit,
    Pin,
    Settings,
    Save,
    Cancel
}

internal static class UiShapes
{
    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = Math.Max(1f, radius * 2f);
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ModernToolbarPanel : Panel
{
    public ModernToolbarPanel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;
        UpdateStyles();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF pill = new RectangleF(16, 12, Math.Max(1, ClientSize.Width - 32), 58);
        RectangleF shadow = pill;
        shadow.Offset(0, 3);

        using (GraphicsPath shadowPath = UiShapes.RoundedRectangle(shadow, 10))
        using (Brush shadowBrush = new SolidBrush(AppStyles.ToolbarShadow))
        {
            e.Graphics.FillPath(shadowBrush, shadowPath);
        }

        using (GraphicsPath pillPath = UiShapes.RoundedRectangle(pill, 10))
        using (Brush pillBrush = new SolidBrush(AppStyles.ToolbarSurface))
        using (Pen borderPen = new Pen(AppStyles.ToolbarBorder, 1f))
        {
            e.Graphics.FillPath(pillBrush, pillPath);
            e.Graphics.DrawPath(borderPen, pillPath);
        }
    }
}

internal sealed class ModernIconButton : Button
{
    private bool hover;
    private bool pressed;
    public ToolbarIconKind IconKind;
    public bool Selected;

    public ModernIconButton(ToolbarIconKind iconKind)
    {
        IconKind = iconKind;
        Width = AppStyles.ToolbarButtonSize;
        Height = AppStyles.ToolbarButtonSize;
        Text = string.Empty;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = AppStyles.ToolbarSurface;
        Cursor = Cursors.Hand;
        TabStop = false;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppStyles.ToolbarSurface);

        RectangleF bg = new RectangleF(2, 2, Width - 4, Height - 4);
        Color bgColor = Color.Empty;
        if (Selected)
        {
            bgColor = AppStyles.ToolbarSelectedBackground;
        }
        else if (pressed)
        {
            bgColor = AppStyles.ToolbarPressedBackground;
        }
        else if (hover)
        {
            bgColor = AppStyles.ToolbarHoverBackground;
        }

        if (!bgColor.IsEmpty)
        {
            using (GraphicsPath path = UiShapes.RoundedRectangle(bg, 7))
            using (Brush brush = new SolidBrush(bgColor))
            {
                g.FillPath(brush, path);
            }
        }

        Color iconColor = GetIconColor();
        DrawIcon(g, iconColor);
    }

    private Color GetIconColor()
    {
        if (!Enabled)
        {
            return AppStyles.ToolbarIconDisabled;
        }
        if (Selected)
        {
            return AppStyles.ToolbarSelectedForeground;
        }
        if (IconKind == ToolbarIconKind.Save)
        {
            return AppStyles.SaveAccent;
        }
        if (IconKind == ToolbarIconKind.Cancel)
        {
            return AppStyles.CancelAccent;
        }
        return AppStyles.ToolbarIcon;
    }

    private void DrawIcon(Graphics g, Color color)
    {
        int cx = Width / 2;
        int cy = Height / 2;
        using (Pen pen = new Pen(color, 1.8f))
        using (Brush brush = new SolidBrush(color))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;

            switch (IconKind)
            {
                case ToolbarIconKind.Rect:
                    g.DrawRectangle(pen, cx - 9, cy - 9, 18, 18);
                    break;

                case ToolbarIconKind.Ellipse:
                    g.DrawEllipse(pen, cx - 10, cy - 10, 20, 20);
                    break;

                case ToolbarIconKind.Arrow:
                    GraphicsState arrowState = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(-45f);
                    g.FillPolygon(brush, new PointF[]
                    {
                        new PointF(-11f, -1.1f),
                        new PointF(3f, -2.4f),
                        new PointF(3f, -7f),
                        new PointF(12f, 0f),
                        new PointF(3f, 7f),
                        new PointF(3f, 2.4f),
                        new PointF(-11f, 1.1f)
                    });
                    g.Restore(arrowState);
                    break;

                case ToolbarIconKind.Pen:
                    GraphicsState penState = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(-45f);
                    g.DrawRectangle(pen, -8, -3, 14, 6);
                    g.DrawLine(pen, -5, -3, -5, 3);
                    g.DrawLine(pen, 6, -3, 11, 0);
                    g.DrawLine(pen, 6, 3, 11, 0);
                    g.DrawLine(pen, -10, 4, -6, 8);
                    g.Restore(penState);
                    break;

                case ToolbarIconKind.Text:
                    g.DrawRectangle(pen, cx - 10, cy - 10, 20, 20);
                    pen.Width = 2f;
                    g.DrawLine(pen, cx - 6, cy - 5, cx + 6, cy - 5);
                    g.DrawLine(pen, cx, cy - 5, cx, cy + 8);
                    break;

                case ToolbarIconKind.Mosaic:
                    g.DrawRectangle(pen, cx - 10, cy - 10, 20, 20);
                    for (int row = 0; row < 3; row++)
                    {
                        for (int col = 0; col < 3; col++)
                        {
                            if ((row + col) % 2 == 0)
                            {
                                g.FillRectangle(brush, cx - 7 + col * 5, cy - 7 + row * 5, 3, 3);
                            }
                        }
                    }
                    break;

                case ToolbarIconKind.Undo:
                    using (GraphicsPath undoPath = new GraphicsPath())
                    {
                        undoPath.AddBezier(
                            cx + 8, cy + 7,
                            cx + 4, cy - 2,
                            cx - 3, cy - 4,
                            cx - 8, cy - 1);
                        g.DrawPath(pen, undoPath);
                    }
                    g.DrawLine(pen, cx - 8, cy - 1, cx - 3, cy - 7);
                    g.DrawLine(pen, cx - 8, cy - 1, cx - 1, cy + 2);
                    break;

                case ToolbarIconKind.Clear:
                    g.DrawLine(pen, cx - 7, cy - 8, cx + 7, cy - 8);
                    g.DrawLine(pen, cx - 3, cy - 11, cx + 3, cy - 11);
                    g.DrawRectangle(pen, cx - 6, cy - 5, 12, 15);
                    g.DrawLine(pen, cx - 2, cy - 2, cx - 2, cy + 7);
                    g.DrawLine(pen, cx + 2, cy - 2, cx + 2, cy + 7);
                    break;

                case ToolbarIconKind.Fit:
                    DrawCorner(g, pen, cx - 9, cy - 9, 1, 1);
                    DrawCorner(g, pen, cx + 9, cy - 9, -1, 1);
                    DrawCorner(g, pen, cx - 9, cy + 9, 1, -1);
                    DrawCorner(g, pen, cx + 9, cy + 9, -1, -1);
                    break;

                case ToolbarIconKind.Pin:
                    g.DrawLine(pen, cx - 5, cy - 10, cx + 5, cy - 10);
                    g.DrawLine(pen, cx - 3, cy - 10, cx - 3, cy - 4);
                    g.DrawLine(pen, cx + 3, cy - 10, cx + 3, cy - 4);
                    g.DrawLine(pen, cx - 7, cy - 4, cx + 7, cy - 4);
                    g.DrawLine(pen, cx - 5, cy - 4, cx, cy + 2);
                    g.DrawLine(pen, cx + 5, cy - 4, cx, cy + 2);
                    g.DrawLine(pen, cx, cy + 2, cx, cy + 11);
                    break;

                case ToolbarIconKind.Settings:
                    pen.Width = 1.9f;
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = Math.PI * 2d * i / 8d;
                        float x1 = cx + (float)Math.Cos(angle) * 8.2f;
                        float y1 = cy + (float)Math.Sin(angle) * 8.2f;
                        float x2 = cx + (float)Math.Cos(angle) * 11f;
                        float y2 = cy + (float)Math.Sin(angle) * 11f;
                        g.DrawLine(pen, x1, y1, x2, y2);
                    }
                    g.DrawEllipse(pen, cx - 7, cy - 7, 14, 14);
                    g.DrawEllipse(pen, cx - 2, cy - 2, 4, 4);
                    break;

                case ToolbarIconKind.Save:
                    pen.Width = 2.2f;
                    g.DrawLine(pen, cx - 9, cy, cx - 3, cy + 6);
                    g.DrawLine(pen, cx - 3, cy + 6, cx + 10, cy - 8);
                    break;

                case ToolbarIconKind.Cancel:
                    pen.Width = 2.1f;
                    g.DrawLine(pen, cx - 8, cy - 8, cx + 8, cy + 8);
                    g.DrawLine(pen, cx + 8, cy - 8, cx - 8, cy + 8);
                    break;
            }
        }
    }

    private static void DrawCorner(Graphics g, Pen pen, int x, int y, int dx, int dy)
    {
        g.DrawLine(pen, x, y, x + dx * 6, y);
        g.DrawLine(pen, x, y, x, y + dy * 6);
    }
}
