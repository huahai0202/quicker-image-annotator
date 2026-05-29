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
    private PointF[] drawingPointsCache;

    public void AddPoint(PointF point)
    {
        Points.Add(point);
        drawingPointsCache = null;
    }

    public PointF[] GetDrawingPoints()
    {
        if (drawingPointsCache == null || drawingPointsCache.Length != Points.Count)
        {
            drawingPointsCache = Points.ToArray();
        }

        return drawingPointsCache;
    }

    public void MoveBy(float dx, float dy)
    {
        Start = new PointF(Start.X + dx, Start.Y + dy);
        End = new PointF(End.X + dx, End.Y + dy);

        for (int i = 0; i < Points.Count; i++)
        {
            PointF point = Points[i];
            Points[i] = new PointF(point.X + dx, point.Y + dy);
        }

        if (drawingPointsCache == null || drawingPointsCache.Length != Points.Count)
        {
            drawingPointsCache = null;
            return;
        }

        for (int i = 0; i < drawingPointsCache.Length; i++)
        {
            PointF point = drawingPointsCache[i];
            drawingPointsCache[i] = new PointF(point.X + dx, point.Y + dy);
        }
    }
}
