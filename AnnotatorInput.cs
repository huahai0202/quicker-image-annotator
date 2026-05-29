using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed partial class AnnotatorForm
{
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
}
