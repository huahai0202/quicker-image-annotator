using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal sealed partial class AnnotatorForm
{
    private static readonly MosaicBlock[] EmptyMosaicBlocks = new MosaicBlock[0];
    private static readonly Color MosaicOverlayColor = Color.FromArgb(110, 0, 0, 0);

    private struct MosaicBlock
    {
        public readonly Rectangle Rect;
        public readonly Color Color;

        public MosaicBlock(Rectangle rect, Color color)
        {
            Rect = rect;
            Color = color;
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

    private static void ApplyMosaic(Bitmap bitmap, AnnotationItem item)
    {
        Rectangle rect = GetMosaicRect(item, bitmap.Width, bitmap.Height);
        if (rect.Width < 2 || rect.Height < 2)
        {
            return;
        }

        MosaicBlock[] blocks = BuildMosaicBlocks(bitmap, rect);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            PaintMosaicBlocks(g, blocks, rect);
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
        MosaicBlock[] blocks = BuildMosaicBlocks(sampleBitmap, rect);
        PaintMosaicBlocks(g, blocks, rect);
    }

    private static void PaintMosaicBlocks(Graphics g, MosaicBlock[] blocks, Rectangle rect)
    {
        if (blocks.Length == 0)
        {
            return;
        }

        using (var brush = new SolidBrush(Color.Black))
        using (var overlay = new SolidBrush(MosaicOverlayColor))
        {
            for (int i = 0; i < blocks.Length; i++)
            {
                Color color = blocks[i].Color;
                brush.Color = Color.FromArgb(color.R / 2, color.G / 2, color.B / 2);
                g.FillRectangle(brush, blocks[i].Rect);
            }

            g.FillRectangle(overlay, rect);
        }
    }

    private static MosaicBlock[] BuildMosaicBlocks(Bitmap sampleBitmap, Rectangle rect)
    {
        if (sampleBitmap == null || rect.Width <= 0 || rect.Height <= 0)
        {
            return EmptyMosaicBlocks;
        }

        PixelFormat lockFormat = GetMosaicLockFormat(sampleBitmap.PixelFormat);
        if (lockFormat != PixelFormat.Undefined)
        {
            try
            {
                return BuildMosaicBlocksFromBitmap(sampleBitmap, rect, rect, lockFormat);
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (ExternalException)
            {
            }
        }

        using (Bitmap clone = CloneMosaicSample(sampleBitmap, rect))
        {
            Rectangle cloneRect = new Rectangle(0, 0, rect.Width, rect.Height);
            return BuildMosaicBlocksFromBitmap(clone, cloneRect, rect, PixelFormat.Format32bppArgb);
        }
    }

    private static Bitmap CloneMosaicSample(Bitmap source, Rectangle rect)
    {
        var clone = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(clone))
        {
            g.DrawImage(source, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
        }
        return clone;
    }

    private static PixelFormat GetMosaicLockFormat(PixelFormat format)
    {
        if (format == PixelFormat.Format24bppRgb ||
            format == PixelFormat.Format32bppRgb ||
            format == PixelFormat.Format32bppArgb ||
            format == PixelFormat.Format32bppPArgb)
        {
            return format;
        }

        return PixelFormat.Undefined;
    }

    private static MosaicBlock[] BuildMosaicBlocksFromBitmap(
        Bitmap bitmap,
        Rectangle lockRect,
        Rectangle outputRect,
        PixelFormat lockFormat)
    {
        BitmapData data = null;
        try
        {
            data = bitmap.LockBits(lockRect, ImageLockMode.ReadOnly, lockFormat);
            int bytesPerPixel = Image.GetPixelFormatSize(lockFormat) / 8;
            return BuildMosaicBlocksFromLockedData(data, outputRect, bytesPerPixel);
        }
        finally
        {
            if (data != null)
            {
                bitmap.UnlockBits(data);
            }
        }
    }

    private static MosaicBlock[] BuildMosaicBlocksFromLockedData(
        BitmapData data,
        Rectangle outputRect,
        int bytesPerPixel)
    {
        if (bytesPerPixel < 3)
        {
            return EmptyMosaicBlocks;
        }

        int columns = (outputRect.Width + MosaicBlockSize - 1) / MosaicBlockSize;
        int rows = (outputRect.Height + MosaicBlockSize - 1) / MosaicBlockSize;
        MosaicBlock[] blocks = new MosaicBlock[columns * rows];
        int index = 0;

        for (int y = outputRect.Top; y < outputRect.Bottom; y += MosaicBlockSize)
        {
            for (int x = outputRect.Left; x < outputRect.Right; x += MosaicBlockSize)
            {
                int w = Math.Min(MosaicBlockSize, outputRect.Right - x);
                int h = Math.Min(MosaicBlockSize, outputRect.Bottom - y);
                Color color = AverageLockedColor(
                    data,
                    bytesPerPixel,
                    x - outputRect.Left,
                    y - outputRect.Top,
                    w,
                    h);

                blocks[index++] = new MosaicBlock(new Rectangle(x, y, w, h), color);
            }
        }

        return blocks;
    }

    private unsafe static Color AverageLockedColor(BitmapData data, int bytesPerPixel, int x, int y, int w, int h)
    {
        long r = 0;
        long g = 0;
        long b = 0;
        long count = 0;
        int step = Math.Max(1, Math.Min(w, h) / 4);
        byte* scan0 = (byte*)data.Scan0.ToPointer();

        for (int yy = y; yy < y + h; yy += step)
        {
            byte* row = scan0 + yy * data.Stride;
            for (int xx = x; xx < x + w; xx += step)
            {
                byte* pixel = row + xx * bytesPerPixel;
                b += pixel[0];
                g += pixel[1];
                r += pixel[2];
                count++;
            }
        }

        if (count == 0)
        {
            return Color.Gray;
        }

        return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
    }
}
