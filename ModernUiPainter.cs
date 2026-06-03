using System;

internal static class ModernUiPainter
{
    private static IntPtr gdiPlusToken;
    private static bool gdiPlusTried;
    private static bool gdiPlusAvailable;

    public static void FillRect(IntPtr hdc, Win32Api.NativeRect rect, uint color)
    {
        IntPtr brush = Win32Api.CreateSolidBrush(color);
        try
        {
            Win32Api.FillRect(hdc, ref rect, brush);
        }
        finally
        {
            if (brush != IntPtr.Zero)
            {
                Win32Api.DeleteObject(brush);
            }
        }
    }

    public static void FillRoundRect(IntPtr hdc, Win32Api.NativeRect rect, int radius, uint fill, uint border, int borderWidth)
    {
        if (TryFillRoundRectWithGdiPlus(hdc, rect, radius, fill, border, borderWidth))
        {
            return;
        }

        IntPtr oldPen = IntPtr.Zero;
        IntPtr oldBrush = IntPtr.Zero;
        IntPtr pen = Win32Api.CreatePen(Win32Api.PenStyleSolid, Math.Max(1, borderWidth), border);
        IntPtr brush = Win32Api.CreateSolidBrush(fill);
        try
        {
            oldPen = Win32Api.SelectObject(hdc, pen);
            oldBrush = Win32Api.SelectObject(hdc, brush);
            Win32Api.RoundRect(hdc, rect.left, rect.top, rect.right, rect.bottom, radius * 2, radius * 2);
        }
        finally
        {
            if (oldPen != IntPtr.Zero)
            {
                Win32Api.SelectObject(hdc, oldPen);
            }
            if (oldBrush != IntPtr.Zero)
            {
                Win32Api.SelectObject(hdc, oldBrush);
            }
            if (pen != IntPtr.Zero)
            {
                Win32Api.DeleteObject(pen);
            }
            if (brush != IntPtr.Zero)
            {
                Win32Api.DeleteObject(brush);
            }
        }
    }

    public static void DrawText(IntPtr hdc, string text, IntPtr font, int x, int y, int width, int height, uint color, uint format)
    {
        IntPtr oldFont = IntPtr.Zero;
        if (font != IntPtr.Zero)
        {
            oldFont = Win32Api.SelectObject(hdc, font);
        }
        Win32Api.SetBkMode(hdc, Win32Api.TransparentBkMode);
        Win32Api.SetTextColor(hdc, color);
        Win32Api.NativeRect rect = Rect(x, y, x + width, y + height);
        try
        {
            Win32Api.DrawText(hdc, text ?? string.Empty, -1, ref rect, format);
        }
        finally
        {
            if (oldFont != IntPtr.Zero)
            {
                Win32Api.SelectObject(hdc, oldFont);
            }
        }
    }

    public static Win32Api.NativeRect Rect(int left, int top, int right, int bottom)
    {
        Win32Api.NativeRect rect = new Win32Api.NativeRect();
        rect.left = left;
        rect.top = top;
        rect.right = right;
        rect.bottom = bottom;
        return rect;
    }

    public static uint Color(int r, int g, int b)
    {
        return (uint)((Clamp(r) & 255) | ((Clamp(g) & 255) << 8) | ((Clamp(b) & 255) << 16));
    }

    private static bool TryFillRoundRectWithGdiPlus(IntPtr hdc, Win32Api.NativeRect rect, int radius, uint fill, uint border, int borderWidth)
    {
        if (hdc == IntPtr.Zero || !EnsureGdiPlus())
        {
            return false;
        }

        IntPtr surface = IntPtr.Zero;
        IntPtr path = IntPtr.Zero;
        IntPtr brush = IntPtr.Zero;
        IntPtr pen = IntPtr.Zero;
        try
        {
            if (Win32Api.GdipCreateFromHDC(hdc, out surface) != Win32Api.GdiPlusOk || surface == IntPtr.Zero)
            {
                return false;
            }
            Win32Api.GdipSetSmoothingMode(surface, Win32Api.GdiPlusSmoothingModeAntiAlias);
            Win32Api.GdipSetPixelOffsetMode(surface, Win32Api.GdiPlusPixelOffsetModeHalf);

            if (Win32Api.GdipCreatePath(Win32Api.GdiPlusFillModeAlternate, out path) != Win32Api.GdiPlusOk || path == IntPtr.Zero)
            {
                return false;
            }
            if (!AddRoundedRectPath(path, rect, radius))
            {
                return false;
            }

            if (Win32Api.GdipCreateSolidFill(Argb(fill), out brush) != Win32Api.GdiPlusOk || brush == IntPtr.Zero)
            {
                return false;
            }
            if (Win32Api.GdipFillPath(surface, brush, path) != Win32Api.GdiPlusOk)
            {
                return false;
            }

            if (borderWidth > 0)
            {
                if (Win32Api.GdipCreatePen1(Argb(border), borderWidth, Win32Api.GdiPlusUnitPixel, out pen) != Win32Api.GdiPlusOk || pen == IntPtr.Zero)
                {
                    return false;
                }
                if (Win32Api.GdipDrawPath(surface, pen, path) != Win32Api.GdiPlusOk)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            if (pen != IntPtr.Zero)
            {
                Win32Api.GdipDeletePen(pen);
            }
            if (brush != IntPtr.Zero)
            {
                Win32Api.GdipDeleteBrush(brush);
            }
            if (path != IntPtr.Zero)
            {
                Win32Api.GdipDeletePath(path);
            }
            if (surface != IntPtr.Zero)
            {
                Win32Api.GdipDeleteSurface(surface);
            }
        }
    }

    private static bool AddRoundedRectPath(IntPtr path, Win32Api.NativeRect rect, int radius)
    {
        float left = rect.left + 0.5f;
        float top = rect.top + 0.5f;
        float right = rect.right - 0.5f;
        float bottom = rect.bottom - 0.5f;
        float width = Math.Max(1f, right - left);
        float height = Math.Max(1f, bottom - top);
        float diameter = Math.Min(Math.Max(1f, radius * 2f), Math.Min(width, height));
        float arcRight = right - diameter;
        float arcBottom = bottom - diameter;
        float half = diameter / 2f;

        if (Win32Api.GdipAddPathArc(path, left, top, diameter, diameter, 180f, 90f) != Win32Api.GdiPlusOk)
        {
            return false;
        }
        if (Win32Api.GdipAddPathLine(path, left + half, top, right - half, top) != Win32Api.GdiPlusOk)
        {
            return false;
        }
        if (Win32Api.GdipAddPathArc(path, arcRight, top, diameter, diameter, 270f, 90f) != Win32Api.GdiPlusOk)
        {
            return false;
        }
        if (Win32Api.GdipAddPathLine(path, right, top + half, right, bottom - half) != Win32Api.GdiPlusOk)
        {
            return false;
        }
        if (Win32Api.GdipAddPathArc(path, arcRight, arcBottom, diameter, diameter, 0f, 90f) != Win32Api.GdiPlusOk)
        {
            return false;
        }
        if (Win32Api.GdipAddPathLine(path, right - half, bottom, left + half, bottom) != Win32Api.GdiPlusOk)
        {
            return false;
        }
        if (Win32Api.GdipAddPathArc(path, left, arcBottom, diameter, diameter, 90f, 90f) != Win32Api.GdiPlusOk)
        {
            return false;
        }
        if (Win32Api.GdipAddPathLine(path, left, bottom - half, left, top + half) != Win32Api.GdiPlusOk)
        {
            return false;
        }

        return Win32Api.GdipClosePathFigure(path) == Win32Api.GdiPlusOk;
    }

    private static bool EnsureGdiPlus()
    {
        if (gdiPlusAvailable)
        {
            return true;
        }
        if (gdiPlusTried)
        {
            return false;
        }

        gdiPlusTried = true;
        Win32Api.GdiplusStartupInput input = new Win32Api.GdiplusStartupInput();
        input.GdiplusVersion = 1;
        int status = Win32Api.GdiplusStartup(out gdiPlusToken, ref input, IntPtr.Zero);
        gdiPlusAvailable = status == Win32Api.GdiPlusOk && gdiPlusToken != IntPtr.Zero;
        return gdiPlusAvailable;
    }

    private static int Argb(uint color)
    {
        uint r = color & 0xff;
        uint g = (color >> 8) & 0xff;
        uint b = (color >> 16) & 0xff;
        return unchecked((int)(0xff000000u | (r << 16) | (g << 8) | b));
    }

    private static int Clamp(int value)
    {
        if (value < 0)
        {
            return 0;
        }
        if (value > 255)
        {
            return 255;
        }
        return value;
    }
}
