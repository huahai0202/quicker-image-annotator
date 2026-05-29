using System.Collections.Generic;
using System.Drawing;

internal enum ToolMode
{
    Rect,
    Ellipse,
    Arrow,
    Pen,
    Text,
    Mosaic
}

internal sealed class AnnotationItem
{
    public ToolMode Tool;
    public PointF Start;
    public PointF End;
    public Color StrokeColor;
    public float StrokeWidth;
    public readonly List<PointF> Points = new List<PointF>();
    public string Text;
}
