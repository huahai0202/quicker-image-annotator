using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

internal sealed class GpuAnnotatorWindow : IDisposable
{
    public static readonly ToolbarCommand[] ToolbarCommands = new ToolbarCommand[]
    {
        ToolbarCommand.ToolRect,
        ToolbarCommand.ToolEllipse,
        ToolbarCommand.ToolArrow,
        ToolbarCommand.ToolPen,
        ToolbarCommand.ToolMosaic,
        ToolbarCommand.ToolText,
        ToolbarCommand.Ocr,
        ToolbarCommand.Undo,
        ToolbarCommand.Clear,
        ToolbarCommand.Fit,
        ToolbarCommand.Pin,
        ToolbarCommand.Settings,
        ToolbarCommand.Cancel,
        ToolbarCommand.Save
    };

    private const string WindowClassName = "QuickerGpuOnlyAnnotatorWindow";
    private const int MinWindowWidth = 720;
    private const int MinWindowHeight = 520;
    private const int InitialWindowScreenMargin = 80;
    private const float MinAnnotationExtent = 2f;
    private const float SelectionHitTolerancePixels = 8f;
    private const float MoveSampleThresholdPixels = 0.5f;
    private const float PenSampleThresholdPixels = 0.9f;
    private const uint ToolOptionsAutoHideMilliseconds = 1200;
    private const uint ToolOptionsAnimationFrameMilliseconds = 16;
    private const float ToolOptionsAnimationMilliseconds = 150f;
    private const uint TextCaretBlinkMilliseconds = 530;
    private const int WmOcrComplete = Win32Api.WmApp + 61;
    private static readonly IntPtr ToolOptionsTimerId = new IntPtr(101);
    private static readonly IntPtr ToolOptionsAnimationTimerId = new IntPtr(102);
    private static readonly IntPtr TextCaretTimerId = new IntPtr(103);
    private static readonly IntPtr FirstFrameFadeTimerId = new IntPtr(104);
    private static readonly Dictionary<IntPtr, GpuAnnotatorWindow> Windows = new Dictionary<IntPtr, GpuAnnotatorWindow>();
    private static Win32Api.WindowProc sharedProc;
    private static IntPtr largeIcon;
    private static IntPtr smallIcon;
    private const int MaxConsecutiveRenderFailures = 3;
    private readonly WicImageDocument image;
    private readonly string imagePath;
    private readonly bool preferActualSize;
    private string outputDirectory;
    private string screenshotDirectory;
    private readonly List<AnnotationItem> items = new List<AnnotationItem>();
    private readonly Stack<AnnotationUndoAction> undoStack = new Stack<AnnotationUndoAction>();
    private GpuRenderer renderer;
    private IntPtr hwnd;
    private ToolMode currentTool = ToolMode.Rect;
    private Rgba stroke = AppStyles.DefaultStroke;
    private float strokeWidth = AppStyles.DefaultStrokeWidth;
    private float zoom = 1f;
    private GpuPoint viewOffset = GpuPoint.Empty;
    private bool drawing;
    private bool panning;
    private bool topMost;
    private bool textEditing;
    private string inlineText = string.Empty;
    private string compositionText = string.Empty;
    private int inlineCaretIndex;
    private int inlineSelectionAnchor;
    private bool selectingInlineText;
    private bool inlineComposing;
    private bool textCaretVisible = true;
    private int editingTextIndex = -1;
    private AnnotationSnapshot editingTextStartState;
    private GpuPoint textOrigin;
    private TextLayoutCache inlineTextLayout;
    private GpuPoint startPoint;
    private GpuPoint currentPoint;
    private GpuPoint lastPan;
    private AnnotationItem currentPen;
    private int selectedIndex = -1;
    private bool movingSelection;
    private bool resizingSelection;
    private SelectionHandle activeSelectionHandle = SelectionHandle.None;
    private GpuPoint moveStartPoint;
    private GpuPoint moveCurrentPoint;
    private GpuPoint lastMovePoint;
    private bool selectionMoved;
    private AnnotationSnapshot selectionEditStartState;
    private GpuRect selectionEditStartBounds;
    private int cursorId = Win32Api.IdcArrow;
    private readonly SettingsOverlayState settingsOverlay = new SettingsOverlayState();
    private bool pointerInToolOptions;
    private int toolOptionsAnimationStartTick;
    private float toolOptionsAnimationStartOpacity;
    private float toolOptionsAnimationTargetOpacity;
    private int hoverStateVersion;
    private int cachedHoverVersion = -1;
    private int cachedHoverX = int.MinValue;
    private int cachedHoverY = int.MinValue;
    private int cachedHoverCursorId = Win32Api.IdcArrow;
    private int consecutiveRenderFailures;
    private bool fatalErrorShown;
    private bool disposed;
    private bool initialViewApplied;
    private bool firstFrameFadeStarted;
    private bool ocrRunning;
    private int ocrRequestVersion;
    private byte firstFrameOpacity = 255;

    public GpuAnnotatorWindow(string imagePath, WicImageDocument image, string outputDirectory, string screenshotDirectory, bool preferActualSize)
    {
        this.imagePath = imagePath;
        this.image = image;
        this.outputDirectory = outputDirectory;
        this.screenshotDirectory = screenshotDirectory;
        this.preferActualSize = preferActualSize;
    }

    public int Run()
    {
        EnsureWindowClass();
        GpuExtent extent = GetInitialWindowExtent();
        hwnd = Win32Api.CreateWindowEx(
            Win32Api.WsExLayered,
            WindowClassName,
            UiText.AppName,
            Win32Api.WsOverlappedWindow,
            Win32Api.CwUseDefault,
            Win32Api.CwUseDefault,
            extent.Width,
            extent.Height,
            IntPtr.Zero,
            IntPtr.Zero,
            Win32Api.GetModuleHandle(null),
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowEx failed.");
        }

        Windows[hwnd] = this;
        SetWindowIcons(hwnd);
        Win32Api.SetWindowTextUnicode(hwnd, UiText.AppName);
        Win32Api.SetUserData(hwnd, GCHandle.ToIntPtr(GCHandle.Alloc(this)));
        Win32Api.SetLayeredWindowAttributes(hwnd, 0, 0, Win32Api.LwaAlpha);
        WarmUpRenderer();
        Win32Api.ShowWindow(hwnd, Win32Api.SwShow);
        Win32Api.UpdateWindow(hwnd);

        Win32Api.Msg msg;
        while (Win32Api.GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            Win32Api.TranslateMessage(ref msg);
            Win32Api.DispatchMessage(ref msg);
        }
        return 0;
    }

    public static GpuRect GetToolbarButtonRect(int index)
    {
        int x = AppStyles.ToolbarStartX + index * (AppStyles.ToolbarButtonSide + AppStyles.ToolbarButtonGap);
        if (index >= 7)
        {
            x += 25;
        }
        if (index >= 12)
        {
            x += 25;
        }
        return new GpuRect(x, AppStyles.ToolbarButtonTop, AppStyles.ToolbarButtonSide, AppStyles.ToolbarButtonSide);
    }

    public static string RegisteredClassName
    {
        get { return WindowClassName; }
    }

    public static void EnsureWindowClass()
    {
        if (sharedProc != null)
        {
            return;
        }

        sharedProc = WindowProc;
        Win32Api.WndClassEx wc = new Win32Api.WndClassEx();
        wc.cbSize = (uint)Marshal.SizeOf(typeof(Win32Api.WndClassEx));
        wc.style = Win32Api.CsHRedraw | Win32Api.CsVRedraw | Win32Api.CsDblClks;
        wc.lpfnWndProc = sharedProc;
        wc.hInstance = Win32Api.GetModuleHandle(null);
        wc.hCursor = Win32Api.LoadCursor(IntPtr.Zero, new IntPtr(Win32Api.IdcArrow));
        largeIcon = LoadAppIcon(32);
        smallIcon = LoadAppIcon(16);
        wc.hIcon = largeIcon;
        wc.hIconSm = smallIcon;
        wc.hbrBackground = IntPtr.Zero;
        wc.lpszClassName = WindowClassName;
        ushort atom = Win32Api.RegisterClassEx(ref wc);
        if (atom == 0)
        {
            throw new InvalidOperationException("RegisterClassEx failed.");
        }
    }

    private static IntPtr LoadAppIcon(int size)
    {
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppIcon.ico");
        if (!File.Exists(iconPath))
        {
            return IntPtr.Zero;
        }
        return Win32Api.LoadImage(IntPtr.Zero, iconPath, Win32Api.ImageIcon, size, size, Win32Api.LrLoadFromFile);
    }

    private static void SetWindowIcons(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return;
        }
        if (largeIcon != IntPtr.Zero)
        {
            Win32Api.SendMessage(window, Win32Api.WmSetIcon, new IntPtr(Win32Api.IconBig), largeIcon);
        }
        if (smallIcon != IntPtr.Zero)
        {
            Win32Api.SendMessage(window, Win32Api.WmSetIcon, new IntPtr(Win32Api.IconSmall), smallIcon);
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        GpuAnnotatorWindow window;
        if (Windows.TryGetValue(hwnd, out window))
        {
            return window.HandleMessage(msg, wParam, lParam);
        }
        return Win32Api.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr HandleMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case Win32Api.WmEraseBkgnd:
                    return new IntPtr(1);
                case Win32Api.WmPaint:
                    Paint();
                    return IntPtr.Zero;
                case Win32Api.WmSetCursor:
                    Win32Api.SetCursor(Win32Api.LoadCursor(IntPtr.Zero, new IntPtr(cursorId)));
                    return new IntPtr(1);
                case Win32Api.WmLButtonDown:
                    MouseDown(Win32Api.GetX(lParam), Win32Api.GetY(lParam));
                    return IntPtr.Zero;
                case Win32Api.WmLButtonUp:
                    MouseUp(Win32Api.GetX(lParam), Win32Api.GetY(lParam));
                    return IntPtr.Zero;
                case Win32Api.WmLButtonDblClk:
                    MouseDoubleClick(Win32Api.GetX(lParam), Win32Api.GetY(lParam));
                    return IntPtr.Zero;
                case Win32Api.WmMouseMove:
                    MouseMove(Win32Api.GetX(lParam), Win32Api.GetY(lParam));
                    return IntPtr.Zero;
                case Win32Api.WmTimer:
                    TimerTick(wParam);
                    return IntPtr.Zero;
                case Win32Api.WmRButtonDown:
                    panning = true;
                    lastPan = new GpuPoint(Win32Api.GetX(lParam), Win32Api.GetY(lParam));
                    Win32Api.SetCapture(hwnd);
                    return IntPtr.Zero;
                case Win32Api.WmRButtonUp:
                    panning = false;
                    cursorId = Win32Api.IdcArrow;
                    Win32Api.ReleaseCapture();
                    return IntPtr.Zero;
                case Win32Api.WmMouseWheel:
                    MouseWheel(Win32Api.HighWord(wParam), lParam);
                    return IntPtr.Zero;
                case Win32Api.WmKeyDown:
                    KeyDown(wParam.ToInt32());
                    return IntPtr.Zero;
                case Win32Api.WmChar:
                    CharInput((char)wParam.ToInt32());
                    return IntPtr.Zero;
                case Win32Api.WmImeComposition:
                    ImeComposition(wParam, lParam);
                    return IntPtr.Zero;
                case Win32Api.WmImeEndComposition:
                    compositionText = string.Empty;
                    inlineComposing = false;
                    RequestPaint();
                    return IntPtr.Zero;
                case WmOcrComplete:
                    CompleteOcr(wParam);
                    return IntPtr.Zero;
                case Win32Api.WmClose:
                    Win32Api.DestroyWindow(hwnd);
                    return IntPtr.Zero;
                case Win32Api.WmDestroy:
                    Windows.Remove(hwnd);
                    Dispose();
                    Win32Api.PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            ShowFatalErrorAndClose("Window message failed.", ex);
        }

        return Win32Api.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Paint()
    {
        Win32Api.PaintStruct ps;
        IntPtr hdc = Win32Api.BeginPaint(hwnd, out ps);
        bool frameRendered = false;
        try
        {
            Win32Api.NativeRect rect;
            Win32Api.GetClientRect(hwnd, out rect);
            int width = Math.Max(1, rect.right - rect.left);
            int height = Math.Max(1, rect.bottom - rect.top);
            try
            {
                settingsOverlay.TopMost = topMost;
                ApplyInitialView(width, height);
                if (renderer == null)
                {
                    renderer = GpuRenderer.CreateForWindow(image);
                }
                renderer.RenderToHdc(hdc, width, height, GetView(width, height), items, GetPreviewItem(), selectedIndex, true, currentTool, stroke, strokeWidth, settingsOverlay);
                consecutiveRenderFailures = 0;
                frameRendered = true;
            }
            catch (Exception ex)
            {
                HandleRenderFailure(ex);
            }
        }
        finally
        {
            Win32Api.EndPaint(hwnd, ref ps);
            if (frameRendered)
            {
                StartFirstFrameFadeIn();
            }
        }
    }

    private void MouseDown(int x, int y)
    {
        if (settingsOverlay.Visible)
        {
            HandleSettingsOverlayClick(x, y);
            return;
        }
        if (HitToolOptions(x, y))
        {
            RequestPaint();
            return;
        }
        if (y < AppStyles.ToolbarHeight)
        {
            CommitTextIfNeeded();
            ToolbarCommand? command = HitToolbar(x, y);
            if (command.HasValue)
            {
                ExecuteCommand(command.Value);
            }
            return;
        }

        if (textEditing)
        {
            if (HandleInlineTextMouseDown(x, y))
            {
                return;
            }
            CommitTextIfNeeded();
        }
        HideToolOptions();
        GpuPoint imagePoint = ToImagePoint(x, y);
        SelectionHandle handle = HitTestSelectionHandle(imagePoint);
        if (handle != SelectionHandle.None && HasSelectedItem())
        {
            BeginSelectionEdit(imagePoint, handle);
            Win32Api.SetCapture(hwnd);
            RequestPaint();
            return;
        }

        int hitIndex = HitTestAnnotation(imagePoint);
        if (hitIndex >= 0)
        {
            SelectAnnotation(hitIndex);
            BeginSelectionMove(imagePoint);
            Win32Api.SetCapture(hwnd);
            RequestPaint();
            return;
        }

        ClearSelection();
        drawing = true;
        startPoint = imagePoint;
        currentPoint = imagePoint;
        if (currentTool == ToolMode.Pen)
        {
            cursorId = Win32Api.IdcCross;
            currentPen = new AnnotationItem();
            currentPen.Tool = ToolMode.Pen;
            currentPen.Stroke = stroke;
            currentPen.StrokeWidth = strokeWidth;
            currentPen.AddPoint(imagePoint);
        }
        else if (currentTool == ToolMode.Text)
        {
            BeginInlineTextEdit(imagePoint);
        }
        else
        {
            cursorId = Win32Api.IdcCross;
        }
        Win32Api.SetCapture(hwnd);
        RequestPaint();
    }

    private void MouseDoubleClick(int x, int y)
    {
        if (settingsOverlay.Visible || y < AppStyles.ToolbarHeight)
        {
            return;
        }
        if (textEditing)
        {
            if (HandleInlineTextMouseDown(x, y))
            {
                return;
            }
            CommitTextIfNeeded();
        }

        GpuPoint imagePoint = ToImagePoint(x, y);
        int textIndex = HitTestTextAnnotation(imagePoint);
        if (textIndex >= 0)
        {
            BeginExistingTextEdit(textIndex, imagePoint);
            RequestPaint();
        }
    }

    private void MouseMove(int x, int y)
    {
        UpdateToolOptionsHover(x, y);
        UpdateTooltip(x, y);
        if (selectingInlineText)
        {
            SetInlineTextCaretFromPoint(ToImagePoint(x, y), true);
            RequestPaint();
            return;
        }
        if (panning)
        {
            cursorId = Win32Api.IdcSizeAll;
            viewOffset.X += x - lastPan.X;
            viewOffset.Y += y - lastPan.Y;
            lastPan = new GpuPoint(x, y);
            InvalidateHoverCache();
            RequestPaint();
            return;
        }
        GpuPoint clientPoint = new GpuPoint(x, y);
        GpuRect view = GetView();
        GpuPoint imagePoint = ToImagePoint(clientPoint, view);
        if (movingSelection || resizingSelection)
        {
            cursorId = movingSelection ? Win32Api.IdcSizeAll : GetCursorForSelectionHandle(activeSelectionHandle);
            if (!HasSelectedItem())
            {
                ResetSelectionEditState();
                return;
            }

            if (Distance(lastMovePoint, imagePoint) >= CanvasPixelsToImageDistance(MoveSampleThresholdPixels))
            {
                moveCurrentPoint = imagePoint;
                lastMovePoint = imagePoint;
                selectionMoved = Distance(moveStartPoint, moveCurrentPoint) >= CanvasPixelsToImageDistance(MoveSampleThresholdPixels);
                ApplyActiveSelectionEdit();
                InvalidateHoverCache();
                RequestPaint();
            }
            return;
        }
        if (!drawing)
        {
            cursorId = GetHoverCursorId(clientPoint, view);
            return;
        }
        currentPoint = imagePoint;
        if (currentTool == ToolMode.Pen && currentPen != null)
        {
            GpuPoint[] points = currentPen.GetDrawingPoints();
            GpuPoint last = points.Length == 0 ? currentPoint : points[points.Length - 1];
            if (Distance(last, currentPoint) >= CanvasPixelsToImageDistance(PenSampleThresholdPixels))
            {
                currentPen.AddPoint(currentPoint);
            }
        }
        RequestPaint();
    }

    private void MouseUp(int x, int y)
    {
        GpuPoint clientPoint = new GpuPoint(x, y);
        GpuRect view = GetView();
        GpuPoint imagePoint = ToImagePoint(clientPoint, view);
        if (selectingInlineText)
        {
            SetInlineTextCaretFromPoint(imagePoint, true);
            selectingInlineText = false;
            cursorId = Win32Api.IdcIBeam;
            InvalidateHoverCache();
            Win32Api.ReleaseCapture();
            RequestPaint();
            return;
        }
        if (movingSelection || resizingSelection)
        {
            FinishSelectionEdit(imagePoint);
            cursorId = GetHoverCursorId(clientPoint, view);
            InvalidateHoverCache();
            Win32Api.ReleaseCapture();
            RequestPaint();
            return;
        }
        if (!drawing)
        {
            Win32Api.ReleaseCapture();
            return;
        }
        currentPoint = imagePoint;
        AnnotationItem item = GetPreviewItem();
        if (IsMeaningfulAnnotation(item))
        {
            AddAnnotation(item);
        }
        currentPen = null;
        drawing = false;
        cursorId = GetHoverCursorId(clientPoint, view);
        InvalidateHoverCache();
        Win32Api.ReleaseCapture();
        RequestPaint();
    }

    private int GetHoverCursorId(GpuPoint clientPoint, GpuRect view)
    {
        int x = (int)clientPoint.X;
        int y = (int)clientPoint.Y;
        if (cachedHoverVersion == hoverStateVersion &&
            cachedHoverX == x &&
            cachedHoverY == y)
        {
            return cachedHoverCursorId;
        }

        if (settingsOverlay.Visible || y < AppStyles.ToolbarHeight)
        {
            return CacheHoverCursor(x, y, Win32Api.IdcArrow);
        }
        if (settingsOverlay.ToolOptionsVisible && GpuRenderer.GetToolOptionsPanelRect().Contains(new GpuPoint(x, y)))
        {
            return CacheHoverCursor(x, y, Win32Api.IdcArrow);
        }
        if (!view.Contains(clientPoint))
        {
            return CacheHoverCursor(x, y, Win32Api.IdcArrow);
        }

        GpuPoint imagePoint = ToImagePoint(clientPoint, view);
        if (textEditing && IsPointInInlineTextEditBounds(imagePoint))
        {
            return CacheHoverCursor(x, y, Win32Api.IdcIBeam);
        }
        SelectionHandle handle = HitTestSelectionHandle(imagePoint);
        if (handle != SelectionHandle.None)
        {
            return CacheHoverCursor(x, y, GetCursorForSelectionHandle(handle));
        }
        if (HitTestAnnotation(imagePoint) >= 0)
        {
            return CacheHoverCursor(x, y, Win32Api.IdcSizeAll);
        }
        return CacheHoverCursor(x, y, currentTool == ToolMode.Text ? Win32Api.IdcIBeam : Win32Api.IdcCross);
    }

    private static int GetCursorForSelectionHandle(SelectionHandle handle)
    {
        switch (handle)
        {
            case SelectionHandle.TopLeft:
            case SelectionHandle.BottomRight:
                return Win32Api.IdcSizeNwSe;
            case SelectionHandle.TopRight:
            case SelectionHandle.BottomLeft:
                return Win32Api.IdcSizeNeSw;
            case SelectionHandle.Top:
            case SelectionHandle.Bottom:
                return Win32Api.IdcSizeNs;
            case SelectionHandle.Left:
            case SelectionHandle.Right:
                return Win32Api.IdcSizeWe;
            case SelectionHandle.ArrowStart:
            case SelectionHandle.ArrowEnd:
                return Win32Api.IdcSizeAll;
            default:
                return Win32Api.IdcSizeAll;
        }
    }

    private void MouseWheel(int delta, IntPtr lParam)
    {
        Win32Api.NativePoint p = new Win32Api.NativePoint();
        p.x = Win32Api.GetX(lParam);
        p.y = Win32Api.GetY(lParam);
        Win32Api.ScreenToClient(hwnd, ref p);
        GpuPoint before = ToImagePoint(p.x, p.y);
        float factor = delta > 0 ? 1.12f : 1f / 1.12f;
        zoom = Math.Max(0.05f, Math.Min(16f, zoom * factor));
        GpuRect view = GetView();
        viewOffset.X = p.x - (before.X * view.Width / image.Width) - (view.X - viewOffset.X);
        viewOffset.Y = p.y - (before.Y * view.Height / image.Height) - (view.Y - viewOffset.Y);
        RequestPaint();
    }

    private void KeyDown(int key)
    {
        bool ctrl = (Win32Api.GetKeyState(Win32Api.VkControl) & 0x8000) != 0;
        bool shift = (Win32Api.GetKeyState(Win32Api.VkShift) & 0x8000) != 0;
        ToolbarCommand shortcutCommand;
        if (AppShortcuts.TryGetToolbarCommand(key, ctrl, shift, out shortcutCommand) &&
            (!textEditing || shortcutCommand == ToolbarCommand.Save))
        {
            ExecuteCommand(shortcutCommand);
            return;
        }
        if (textEditing && HandleInlineTextKeyDown(key, ctrl, shift))
        {
            return;
        }
        if (ctrl && key == Win32Api.VkZ)
        {
            UndoAnnotationAction();
            return;
        }
        if (key == Win32Api.VkEscape)
        {
            if (textEditing)
            {
                CancelInlineTextEdit();
            }
            else
            {
                Win32Api.DestroyWindow(hwnd);
            }
            return;
        }
        if (textEditing)
        {
            return;
        }
        if ((key == Win32Api.VkDelete || key == Win32Api.VkBack) && DeleteSelectedAnnotation())
        {
            return;
        }
    }

    private void CharInput(char ch)
    {
        if (!textEditing)
        {
            return;
        }
        if (ch >= ' ' && ch != 127)
        {
            ReplaceInlineSelection(ch.ToString());
        }
    }

    private void ImeComposition(IntPtr wParam, IntPtr lParam)
    {
        IntPtr context = Win32Api.ImmGetContext(hwnd);
        if (context == IntPtr.Zero)
        {
            return;
        }
        try
        {
            if (((long)lParam & Win32Api.GcsResultStr) != 0)
            {
                string result = GetCompositionString(context, Win32Api.GcsResultStr);
                compositionText = string.Empty;
                inlineComposing = false;
                if (!string.IsNullOrEmpty(result))
                {
                    ReplaceInlineSelection(result);
                }
                else
                {
                    ResetTextCaretBlink();
                }
                compositionText = string.Empty;
                UpdateInlineTextLayout();
            }
            else if (((long)lParam & Win32Api.GcsCompStr) != 0)
            {
                if (!inlineComposing && HasInlineTextSelection())
                {
                    DeleteInlineSelection();
                }
                compositionText = GetCompositionString(context, Win32Api.GcsCompStr);
                inlineComposing = !string.IsNullOrEmpty(compositionText);
                ResetTextCaretBlink();
                UpdateInlineTextLayout();
            }
            PositionImeWindow(context);
            RequestPaint();
        }
        finally
        {
            Win32Api.ImmReleaseContext(hwnd, context);
        }
    }

    private string GetCompositionString(IntPtr context, int kind)
    {
        int byteCount = Win32Api.ImmGetCompositionString(context, kind, null, 0);
        if (byteCount <= 0)
        {
            return string.Empty;
        }
        byte[] buffer = new byte[byteCount];
        int copied = Win32Api.ImmGetCompositionString(context, kind, buffer, buffer.Length);
        if (copied <= 0)
        {
            return string.Empty;
        }
        return System.Text.Encoding.Unicode.GetString(buffer, 0, copied).TrimEnd('\0');
    }

    private bool HandleInlineTextKeyDown(int key, bool ctrl, bool shift)
    {
        if (ctrl)
        {
            if (key == Win32Api.VkA)
            {
                SelectAllInlineText();
                RequestPaint();
                return true;
            }
            if (key == Win32Api.VkC)
            {
                CopyInlineTextSelection();
                return true;
            }
            if (key == Win32Api.VkX)
            {
                CutInlineTextSelection();
                return true;
            }
            if (key == Win32Api.VkV)
            {
                PasteInlineText();
                return true;
            }
            if (key == Win32Api.VkZ)
            {
                return true;
            }
        }

        switch (key)
        {
            case Win32Api.VkReturn:
                CommitTextIfNeeded();
                RequestPaint();
                return true;
            case Win32Api.VkBack:
                BackspaceInlineText();
                return true;
            case Win32Api.VkDelete:
                DeleteInlineText();
                return true;
            case Win32Api.VkLeft:
                MoveInlineCaret(!shift && HasInlineTextSelection() ? GetInlineSelectionStart() : (ctrl ? GetPreviousWordBoundary(inlineCaretIndex) : inlineCaretIndex - 1), shift);
                return true;
            case Win32Api.VkRight:
                MoveInlineCaret(!shift && HasInlineTextSelection() ? GetInlineSelectionEnd() : (ctrl ? GetNextWordBoundary(inlineCaretIndex) : inlineCaretIndex + 1), shift);
                return true;
            case Win32Api.VkHome:
                MoveInlineCaret(0, shift);
                return true;
            case Win32Api.VkEnd:
                MoveInlineCaret(inlineText.Length, shift);
                return true;
            default:
                return false;
        }
    }

    private bool HandleInlineTextMouseDown(int x, int y)
    {
        if (!textEditing)
        {
            return false;
        }

        GpuPoint imagePoint = ToImagePoint(x, y);
        if (!IsPointInInlineTextEditBounds(imagePoint))
        {
            return false;
        }

        bool shift = (Win32Api.GetKeyState(Win32Api.VkShift) & 0x8000) != 0;
        SetInlineTextCaretFromPoint(imagePoint, shift);
        selectingInlineText = true;
        cursorId = Win32Api.IdcIBeam;
        Win32Api.SetCapture(hwnd);
        RequestPaint();
        return true;
    }

    private void BeginInlineTextEdit(GpuPoint imagePoint)
    {
        drawing = false;
        textEditing = true;
        textOrigin = imagePoint;
        inlineText = string.Empty;
        compositionText = string.Empty;
        inlineCaretIndex = 0;
        inlineSelectionAnchor = 0;
        selectingInlineText = false;
        inlineComposing = false;
        editingTextIndex = -1;
        editingTextStartState = null;
        textCaretVisible = true;
        currentTool = ToolMode.Text;
        StartTextCaretTimer();
        UpdateInlineTextLayout();
        InvalidateHoverCache();
        PositionImeWindow();
    }

    private void BeginExistingTextEdit(int index, GpuPoint imagePoint)
    {
        if (index < 0 || index >= items.Count || items[index].Tool != ToolMode.Text)
        {
            return;
        }

        AnnotationItem item = items[index];
        SelectAnnotation(index);
        HideToolOptions();
        drawing = false;
        movingSelection = false;
        resizingSelection = false;
        textEditing = true;
        textOrigin = item.Start;
        inlineText = item.Text ?? string.Empty;
        compositionText = string.Empty;
        inlineCaretIndex = inlineText.Length;
        inlineSelectionAnchor = inlineCaretIndex;
        selectingInlineText = false;
        inlineComposing = false;
        editingTextIndex = index;
        editingTextStartState = CaptureAnnotation(item);
        stroke = item.Stroke.A == 0 ? AppStyles.DefaultStroke : item.Stroke;
        strokeWidth = item.StrokeWidth > 0f ? item.StrokeWidth : AppStyles.DefaultStrokeWidth;
        currentTool = ToolMode.Text;
        textCaretVisible = true;
        cursorId = Win32Api.IdcIBeam;
        StartTextCaretTimer();
        UpdateInlineTextLayout();
        SetInlineTextCaretFromPoint(imagePoint, false);
        InvalidateHoverCache();
        PositionImeWindow();
    }

    private void CancelInlineTextEdit()
    {
        textEditing = false;
        inlineText = string.Empty;
        compositionText = string.Empty;
        inlineCaretIndex = 0;
        inlineSelectionAnchor = 0;
        selectingInlineText = false;
        inlineComposing = false;
        editingTextIndex = -1;
        editingTextStartState = null;
        StopTextCaretTimer();
        DisposeInlineTextLayout();
        InvalidateHoverCache();
        RequestPaint();
    }

    private void ReplaceInlineSelection(string text)
    {
        text = NormalizeInlineInput(text);
        if (text.Length == 0 && !HasInlineTextSelection())
        {
            return;
        }

        compositionText = string.Empty;
        inlineComposing = false;
        int start = GetInlineSelectionStart();
        int end = GetInlineSelectionEnd();
        inlineText = inlineText.Remove(start, end - start).Insert(start, text);
        inlineCaretIndex = start + text.Length;
        inlineSelectionAnchor = inlineCaretIndex;
        ResetTextCaretBlink();
        UpdateInlineTextLayout();
        PositionImeWindow();
        RequestPaint();
    }

    private void DeleteInlineSelection()
    {
        if (!HasInlineTextSelection())
        {
            return;
        }

        int start = GetInlineSelectionStart();
        int end = GetInlineSelectionEnd();
        inlineText = inlineText.Remove(start, end - start);
        inlineCaretIndex = start;
        inlineSelectionAnchor = start;
        compositionText = string.Empty;
        inlineComposing = false;
        ResetTextCaretBlink();
        UpdateInlineTextLayout();
    }

    private void BackspaceInlineText()
    {
        if (HasInlineTextSelection())
        {
            DeleteInlineSelection();
            PositionImeWindow();
            RequestPaint();
            return;
        }
        if (inlineCaretIndex <= 0)
        {
            ResetTextCaretBlink();
            RequestPaint();
            return;
        }
        int removeStart = GetPreviousTextElementBoundary(inlineCaretIndex);
        inlineText = inlineText.Remove(removeStart, inlineCaretIndex - removeStart);
        inlineCaretIndex = removeStart;
        inlineSelectionAnchor = inlineCaretIndex;
        compositionText = string.Empty;
        inlineComposing = false;
        ResetTextCaretBlink();
        UpdateInlineTextLayout();
        PositionImeWindow();
        RequestPaint();
    }

    private void DeleteInlineText()
    {
        if (HasInlineTextSelection())
        {
            DeleteInlineSelection();
            PositionImeWindow();
            RequestPaint();
            return;
        }
        if (inlineCaretIndex >= inlineText.Length)
        {
            ResetTextCaretBlink();
            RequestPaint();
            return;
        }
        int removeEnd = GetNextTextElementBoundary(inlineCaretIndex);
        inlineText = inlineText.Remove(inlineCaretIndex, removeEnd - inlineCaretIndex);
        inlineSelectionAnchor = inlineCaretIndex;
        compositionText = string.Empty;
        inlineComposing = false;
        ResetTextCaretBlink();
        UpdateInlineTextLayout();
        PositionImeWindow();
        RequestPaint();
    }

    private void MoveInlineCaret(int index, bool extendSelection)
    {
        inlineCaretIndex = ClampInlineIndex(index);
        if (!extendSelection)
        {
            inlineSelectionAnchor = inlineCaretIndex;
        }
        compositionText = string.Empty;
        inlineComposing = false;
        ResetTextCaretBlink();
        UpdateInlineTextLayout();
        PositionImeWindow();
        RequestPaint();
    }

    private void SelectAllInlineText()
    {
        inlineCaretIndex = inlineText.Length;
        inlineSelectionAnchor = 0;
        compositionText = string.Empty;
        inlineComposing = false;
        ResetTextCaretBlink();
        PositionImeWindow();
    }

    private void CopyInlineTextSelection()
    {
        string selection = GetInlineSelectedText();
        if (selection.Length > 0)
        {
            ClipboardBridge.SetUnicodeText(hwnd, selection);
        }
    }

    private void CutInlineTextSelection()
    {
        string selection = GetInlineSelectedText();
        if (selection.Length == 0)
        {
            return;
        }
        ClipboardBridge.SetUnicodeText(hwnd, selection);
        DeleteInlineSelection();
        PositionImeWindow();
        RequestPaint();
    }

    private void PasteInlineText()
    {
        string text = ClipboardBridge.GetUnicodeText(hwnd);
        if (!string.IsNullOrEmpty(text))
        {
            ReplaceInlineSelection(text);
        }
    }

    private string GetInlineSelectedText()
    {
        if (!HasInlineTextSelection())
        {
            return string.Empty;
        }
        int start = GetInlineSelectionStart();
        return inlineText.Substring(start, GetInlineSelectionEnd() - start);
    }

    private bool HasInlineTextSelection()
    {
        return ClampInlineIndex(inlineCaretIndex) != ClampInlineIndex(inlineSelectionAnchor);
    }

    private int GetInlineSelectionStart()
    {
        return Math.Min(ClampInlineIndex(inlineCaretIndex), ClampInlineIndex(inlineSelectionAnchor));
    }

    private int GetInlineSelectionEnd()
    {
        return Math.Max(ClampInlineIndex(inlineCaretIndex), ClampInlineIndex(inlineSelectionAnchor));
    }

    private void SetInlineTextCaretFromPoint(GpuPoint imagePoint, bool extendSelection)
    {
        compositionText = string.Empty;
        inlineComposing = false;
        float em = GetInlineTextEm();
        UpdateInlineTextLayout();
        GpuPoint relative = new GpuPoint(imagePoint.X - textOrigin.X, imagePoint.Y - textOrigin.Y);
        TextHitResult hit = inlineTextLayout == null ? GpuRenderer.HitTestText(inlineText, em, relative) : inlineTextLayout.HitTestPoint(relative);
        int index = hit.TextPosition + (hit.IsTrailing ? 1 : 0);
        inlineCaretIndex = ClampInlineIndex(index);
        if (!extendSelection)
        {
            inlineSelectionAnchor = inlineCaretIndex;
        }
        ResetTextCaretBlink();
        PositionImeWindow();
    }

    private bool IsPointInInlineTextEditBounds(GpuPoint imagePoint)
    {
        if (!textEditing)
        {
            return false;
        }
        return GetInlineTextEditBounds().Contains(imagePoint);
    }

    private GpuRect GetInlineTextEditBounds()
    {
        float em = GetInlineTextEm();
        string visible = GetInlineVisibleText();
        UpdateInlineTextLayout();
        GpuRect measured = inlineTextLayout == null ? GpuRenderer.MeasureTextBounds(visible.Length == 0 ? " " : visible, em) : inlineTextLayout.Bounds;
        float width = Math.Max(80f, measured.Width + em);
        float height = Math.Max(em * 1.5f, measured.Height + em * 0.45f);
        return Inflate(new GpuRect(textOrigin.X, textOrigin.Y, width, height), em * 0.35f, em * 0.25f);
    }

    private string GetInlineVisibleText()
    {
        int caret = ClampInlineIndex(inlineCaretIndex);
        if (string.IsNullOrEmpty(compositionText))
        {
            return inlineText;
        }
        return inlineText.Substring(0, caret) + compositionText + inlineText.Substring(caret);
    }

    private float GetInlineTextEm()
    {
        return Math.Max(1f, strokeWidth * 6f);
    }

    private int GetPreviousWordBoundary(int index)
    {
        int i = ClampInlineIndex(index);
        while (i > 0 && char.IsWhiteSpace(inlineText[i - 1]))
        {
            i--;
        }
        while (i > 0 && !char.IsWhiteSpace(inlineText[i - 1]))
        {
            i--;
        }
        return i;
    }

    private int GetNextWordBoundary(int index)
    {
        int i = ClampInlineIndex(index);
        while (i < inlineText.Length && char.IsWhiteSpace(inlineText[i]))
        {
            i++;
        }
        while (i < inlineText.Length && !char.IsWhiteSpace(inlineText[i]))
        {
            i++;
        }
        return i;
    }

    private int GetPreviousTextElementBoundary(int index)
    {
        int i = ClampInlineIndex(index);
        if (i > 1 && char.IsLowSurrogate(inlineText[i - 1]) && char.IsHighSurrogate(inlineText[i - 2]))
        {
            return i - 2;
        }
        return Math.Max(0, i - 1);
    }

    private int GetNextTextElementBoundary(int index)
    {
        int i = ClampInlineIndex(index);
        if (i + 1 < inlineText.Length && char.IsHighSurrogate(inlineText[i]) && char.IsLowSurrogate(inlineText[i + 1]))
        {
            return i + 2;
        }
        return Math.Min(inlineText.Length, i + 1);
    }

    private int ClampInlineIndex(int index)
    {
        if (index < 0)
        {
            return 0;
        }
        if (index > inlineText.Length)
        {
            return inlineText.Length;
        }
        return index;
    }

    private static string NormalizeInlineInput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        return text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').TrimEnd('\0');
    }

    private void ExecuteCommand(ToolbarCommand command)
    {
        settingsOverlay.Tooltip = null;
        bool toolChanged = false;
        switch (command)
        {
            case ToolbarCommand.ToolRect:
                currentTool = ToolMode.Rect;
                toolChanged = true;
                break;
            case ToolbarCommand.ToolEllipse:
                currentTool = ToolMode.Ellipse;
                toolChanged = true;
                break;
            case ToolbarCommand.ToolArrow:
                currentTool = ToolMode.Arrow;
                toolChanged = true;
                break;
            case ToolbarCommand.ToolPen:
                currentTool = ToolMode.Pen;
                toolChanged = true;
                break;
            case ToolbarCommand.ToolMosaic:
                currentTool = ToolMode.Mosaic;
                toolChanged = true;
                break;
            case ToolbarCommand.ToolText:
                currentTool = ToolMode.Text;
                toolChanged = true;
                break;
            case ToolbarCommand.Ocr:
                RunOcr();
                break;
            case ToolbarCommand.Undo:
                UndoAnnotationAction();
                break;
            case ToolbarCommand.Clear:
                if (ConfirmClearAnnotations())
                {
                    ClearAnnotations();
                }
                break;
            case ToolbarCommand.Fit:
                ResetView();
                break;
            case ToolbarCommand.Pin:
                topMost = !topMost;
                Win32Api.SetWindowPos(hwnd, topMost ? Win32Api.HwndTopMost : Win32Api.HwndNoTopMost, 0, 0, 0, 0, Win32Api.SwpNoMove | Win32Api.SwpNoSize);
                break;
            case ToolbarCommand.Settings:
                HideToolOptions();
                settingsOverlay.Visible = false;
                SettingsWindow.Run(hwnd);
                ReloadSettingsForCurrentWindow();
                break;
            case ToolbarCommand.Cancel:
                Win32Api.DestroyWindow(hwnd);
                break;
            case ToolbarCommand.Save:
                SaveAndClose();
                break;
        }
        if (toolChanged && GpuRenderer.ToolSupportsOptions(currentTool))
        {
            ShowToolOptionsTemporarily();
        }
        else if (toolChanged)
        {
            HideToolOptions();
        }
        if (toolChanged)
        {
            InvalidateHoverCache();
        }
        RequestPaint();
    }

    private ToolbarCommand? HitToolbar(int x, int y)
    {
        for (int i = 0; i < ToolbarCommands.Length; i++)
        {
            if (GetToolbarButtonRect(i).Contains(new GpuPoint(x, y)))
            {
                return ToolbarCommands[i];
            }
        }
        return null;
    }

    private void UpdateTooltip(int x, int y)
    {
        if (settingsOverlay.Visible)
        {
            ClearTooltip();
            return;
        }
        ToolbarCommand? command = y < AppStyles.ToolbarHeight ? HitToolbar(x, y) : null;
        if (command.HasValue)
        {
            if (IsSelectedToolCommand(command.Value, currentTool) && GpuRenderer.ToolSupportsOptions(currentTool))
            {
                ClearTooltip();
                ShowToolOptionsTemporarily();
                return;
            }
            string tooltip = AppShortcuts.GetTooltip(command.Value);
            GpuPoint point = new GpuPoint(x, y);
            if (!string.Equals(settingsOverlay.Tooltip, tooltip, StringComparison.Ordinal) ||
                settingsOverlay.TooltipPoint.X != point.X ||
                settingsOverlay.TooltipPoint.Y != point.Y)
            {
                settingsOverlay.Tooltip = tooltip;
                settingsOverlay.TooltipPoint = point;
                RequestPaint();
            }
            return;
        }
        ClearTooltip();
    }

    private void ClearTooltip()
    {
        if (!string.IsNullOrEmpty(settingsOverlay.Tooltip))
        {
            settingsOverlay.Tooltip = null;
            RequestPaint();
        }
    }

    private static bool IsSelectedToolCommand(ToolbarCommand command, ToolMode tool)
    {
        return (command == ToolbarCommand.ToolRect && tool == ToolMode.Rect) ||
            (command == ToolbarCommand.ToolEllipse && tool == ToolMode.Ellipse) ||
            (command == ToolbarCommand.ToolArrow && tool == ToolMode.Arrow) ||
            (command == ToolbarCommand.ToolPen && tool == ToolMode.Pen) ||
            (command == ToolbarCommand.ToolMosaic && tool == ToolMode.Mosaic) ||
            (command == ToolbarCommand.ToolText && tool == ToolMode.Text);
    }

    private bool HitToolOptions(int x, int y)
    {
        if (!settingsOverlay.ToolOptionsVisible || settingsOverlay.ToolOptionsOpacity < 0.25f || !GpuRenderer.ToolSupportsOptions(currentTool))
        {
            return false;
        }
        GpuPoint point = new GpuPoint(x, y);
        GpuRect panel = GpuRenderer.GetToolOptionsPanelRect();
        if (!panel.Contains(point))
        {
            return false;
        }
        pointerInToolOptions = true;
        for (int i = 0; i < AppStyles.Palette.Length; i++)
        {
            if (GpuRenderer.GetPaletteSwatchRect(i).Contains(point))
            {
                ApplyActiveStyle(AppStyles.Palette[i], strokeWidth, true, false);
                ShowToolOptionsTemporarily();
                return true;
            }
        }
        for (int i = 0; i < AppStyles.StrokeWidths.Length; i++)
        {
            if (GpuRenderer.GetWidthOptionRect(i).Contains(point))
            {
                ApplyActiveStyle(stroke, AppStyles.StrokeWidths[i], false, true);
                ShowToolOptionsTemporarily();
                return true;
            }
        }
        ShowToolOptionsTemporarily();
        return true;
    }

    private void ApplyActiveStyle(Rgba newStroke, float newStrokeWidth, bool updateStroke, bool updateStrokeWidth)
    {
        if (updateStroke)
        {
            stroke = newStroke;
        }
        if (updateStrokeWidth)
        {
            strokeWidth = newStrokeWidth;
        }

        if (textEditing)
        {
            return;
        }
        if (!HasSelectedItem())
        {
            return;
        }

        AnnotationItem item = items[selectedIndex];
        if (!GpuRenderer.ToolSupportsOptions(item.Tool))
        {
            return;
        }

        AnnotationSnapshot before = CaptureAnnotation(item);
        if (updateStroke)
        {
            item.Stroke = newStroke;
        }
        if (updateStrokeWidth)
        {
            item.StrokeWidth = newStrokeWidth;
        }
        if (item.Tool == ToolMode.Text)
        {
            item.DisposeTextLayout();
        }
        AnnotationSnapshot after = CaptureAnnotation(item);
        RecordTransformUndo(item, selectedIndex, before, after);
    }

    private void UpdateToolOptionsHover(int x, int y)
    {
        if (!settingsOverlay.ToolOptionsVisible)
        {
            pointerInToolOptions = false;
            return;
        }

        bool inside = IsPointInToolOptions(x, y);
        if (inside)
        {
            pointerInToolOptions = true;
            if (settingsOverlay.ToolOptionsClosing)
            {
                ShowToolOptionsTemporarily();
            }
            StopToolOptionsTimer();
            return;
        }

        if (pointerInToolOptions)
        {
            pointerInToolOptions = false;
            RestartToolOptionsTimer();
        }
    }

    private bool IsPointInToolOptions(int x, int y)
    {
        return settingsOverlay.ToolOptionsVisible &&
            settingsOverlay.ToolOptionsOpacity > 0.12f &&
            GpuRenderer.GetToolOptionsPanelRect().Contains(new GpuPoint(x, y));
    }

    private bool IsCursorInToolOptions()
    {
        if (!settingsOverlay.ToolOptionsVisible)
        {
            return false;
        }

        Win32Api.NativePoint point;
        if (!Win32Api.GetCursorPos(out point))
        {
            return false;
        }
        Win32Api.ScreenToClient(hwnd, ref point);
        return IsPointInToolOptions(point.x, point.y);
    }

    private void ShowToolOptionsTemporarily()
    {
        if (!GpuRenderer.ToolSupportsOptions(currentTool))
        {
            HideToolOptions();
            return;
        }

        settingsOverlay.ToolOptionsVisible = true;
        settingsOverlay.ToolOptionsClosing = false;
        StartToolOptionsAnimation(1f);
        if (IsCursorInToolOptions())
        {
            pointerInToolOptions = true;
            StopToolOptionsTimer();
        }
        else
        {
            pointerInToolOptions = false;
            RestartToolOptionsTimer();
        }
        RequestPaint();
    }

    private void HideToolOptions()
    {
        StopToolOptionsTimer();
        pointerInToolOptions = false;
        if (settingsOverlay.ToolOptionsVisible || settingsOverlay.ToolOptionsOpacity > 0.01f)
        {
            settingsOverlay.ToolOptionsClosing = true;
            StartToolOptionsAnimation(0f);
        }
    }

    private void RestartToolOptionsTimer()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        Win32Api.KillTimer(hwnd, ToolOptionsTimerId);
        Win32Api.SetTimer(hwnd, ToolOptionsTimerId, ToolOptionsAutoHideMilliseconds, IntPtr.Zero);
    }

    private void StopToolOptionsTimer()
    {
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.KillTimer(hwnd, ToolOptionsTimerId);
        }
    }

    private void StartToolOptionsAnimation(float targetOpacity)
    {
        targetOpacity = Clamp01(targetOpacity);
        settingsOverlay.ToolOptionsOpacity = Clamp01(settingsOverlay.ToolOptionsOpacity);
        toolOptionsAnimationStartOpacity = settingsOverlay.ToolOptionsOpacity;
        toolOptionsAnimationTargetOpacity = targetOpacity;
        toolOptionsAnimationStartTick = Environment.TickCount;
        if (Math.Abs(toolOptionsAnimationStartOpacity - toolOptionsAnimationTargetOpacity) < 0.01f)
        {
            FinishToolOptionsAnimation();
            return;
        }
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.KillTimer(hwnd, ToolOptionsAnimationTimerId);
            Win32Api.SetTimer(hwnd, ToolOptionsAnimationTimerId, ToolOptionsAnimationFrameMilliseconds, IntPtr.Zero);
        }
        RequestPaint();
    }

    private void StopToolOptionsAnimation()
    {
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.KillTimer(hwnd, ToolOptionsAnimationTimerId);
        }
    }

    private void TimerTick(IntPtr timerId)
    {
        if (timerId == TextCaretTimerId)
        {
            ToggleTextCaret();
            return;
        }

        if (timerId == ToolOptionsAnimationTimerId)
        {
            AnimateToolOptions();
            return;
        }

        if (timerId == FirstFrameFadeTimerId)
        {
            AnimateFirstFrameFade();
            return;
        }

        if (timerId != ToolOptionsTimerId)
        {
            return;
        }

        if (IsCursorInToolOptions())
        {
            pointerInToolOptions = true;
            StopToolOptionsTimer();
            return;
        }
        HideToolOptions();
    }

    private void ToggleTextCaret()
    {
        if (!textEditing)
        {
            StopTextCaretTimer();
            return;
        }
        textCaretVisible = !textCaretVisible;
        RequestPaint();
    }

    private void ResetTextCaretBlink()
    {
        textCaretVisible = true;
        if (textEditing)
        {
            StartTextCaretTimer();
        }
    }

    private void StartTextCaretTimer()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        Win32Api.KillTimer(hwnd, TextCaretTimerId);
        Win32Api.SetTimer(hwnd, TextCaretTimerId, TextCaretBlinkMilliseconds, IntPtr.Zero);
    }

    private void StopTextCaretTimer()
    {
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.KillTimer(hwnd, TextCaretTimerId);
        }
        textCaretVisible = true;
    }

    private void AnimateFirstFrameFade()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (firstFrameOpacity >= 255)
        {
            Win32Api.KillTimer(hwnd, FirstFrameFadeTimerId);
            DetachLayeredStyle();
            return;
        }

        firstFrameOpacity = (byte)Math.Min(255, firstFrameOpacity + 72);
        ApplyFirstFrameOpacity();
        if (firstFrameOpacity >= 255)
        {
            Win32Api.KillTimer(hwnd, FirstFrameFadeTimerId);
            DetachLayeredStyle();
        }
    }

    private void ApplyFirstFrameOpacity()
    {
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.SetLayeredWindowAttributes(hwnd, 0, firstFrameOpacity, Win32Api.LwaAlpha);
        }
    }

    private void DetachLayeredStyle()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = Win32Api.GetWindowLongPtr(hwnd, Win32Api.GwlExStyle).ToInt64();
        if ((style & Win32Api.WsExLayered) == 0)
        {
            return;
        }

        Win32Api.SetWindowLongPtr(hwnd, Win32Api.GwlExStyle, new IntPtr(style & ~((long)Win32Api.WsExLayered)));
        Win32Api.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, Win32Api.SwpNoMove | Win32Api.SwpNoSize | Win32Api.SwpNoZOrder | Win32Api.SwpNoActivate | Win32Api.SwpFrameChanged);
    }

    private void AnimateToolOptions()
    {
        int elapsed = unchecked(Environment.TickCount - toolOptionsAnimationStartTick);
        float t = Clamp01(elapsed / ToolOptionsAnimationMilliseconds);
        float eased = 1f - (1f - t) * (1f - t) * (1f - t);
        settingsOverlay.ToolOptionsOpacity = toolOptionsAnimationStartOpacity + (toolOptionsAnimationTargetOpacity - toolOptionsAnimationStartOpacity) * eased;
        if (t >= 1f)
        {
            FinishToolOptionsAnimation();
        }
        RequestPaint();
    }

    private void FinishToolOptionsAnimation()
    {
        StopToolOptionsAnimation();
        settingsOverlay.ToolOptionsOpacity = toolOptionsAnimationTargetOpacity;
        if (settingsOverlay.ToolOptionsOpacity <= 0.01f)
        {
            settingsOverlay.ToolOptionsOpacity = 0f;
            settingsOverlay.ToolOptionsVisible = false;
            settingsOverlay.ToolOptionsClosing = false;
        }
        else
        {
            settingsOverlay.ToolOptionsOpacity = 1f;
            settingsOverlay.ToolOptionsVisible = true;
            settingsOverlay.ToolOptionsClosing = false;
        }
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

    private bool ConfirmClearAnnotations()
    {
        if (items.Count == 0 && !textEditing)
        {
            return false;
        }
        int result = Win32Api.MessageBoxUnicode(
            hwnd,
            UiText.ClearConfirmMessage,
            UiText.ClearConfirmTitle,
            Win32Api.MbYesNo | Win32Api.MbIconWarning);
        return result == Win32Api.IdYes;
    }

    private void HandleSettingsOverlayClick(int x, int y)
    {
        Win32Api.NativeRect client;
        Win32Api.GetClientRect(hwnd, out client);
        GpuRect panel = GpuRenderer.GetSettingsOverlayPanel(
            Math.Max(1, client.right - client.left),
            Math.Max(1, client.bottom - client.top));
        GpuPoint point = new GpuPoint(x, y);
        SettingsOverlayCommand command = HitSettingsOverlay(panel, point);
        switch (command)
        {
            case SettingsOverlayCommand.BrowseOutput:
                string selected = ShellDialogs.BrowseForFolder(hwnd, UiText.SelectOutputDirectory);
                if (!string.IsNullOrEmpty(selected))
                {
                    settingsOverlay.OutputDirectory = selected;
                }
                break;
            case SettingsOverlayCommand.ClearOutput:
                settingsOverlay.OutputDirectory = null;
                break;
            case SettingsOverlayCommand.BrowseScreenshot:
                string screenshotSelected = ShellDialogs.BrowseForFolder(hwnd, UiText.SelectScreenshotDirectory);
                if (!string.IsNullOrEmpty(screenshotSelected))
                {
                    settingsOverlay.ScreenshotDirectory = screenshotSelected;
                }
                break;
            case SettingsOverlayCommand.ClearScreenshot:
                settingsOverlay.ScreenshotDirectory = null;
                break;
            case SettingsOverlayCommand.ToggleAutoStart:
                settingsOverlay.AutoStartEnabled = !settingsOverlay.AutoStartEnabled;
                break;
            case SettingsOverlayCommand.ToggleGlobalHotkey:
                settingsOverlay.GlobalHotkeyEnabled = !settingsOverlay.GlobalHotkeyEnabled;
                break;
            case SettingsOverlayCommand.Save:
                outputDirectory = AppSettingsStore.NormalizeDirectory(settingsOverlay.OutputDirectory);
                screenshotDirectory = AppSettingsStore.NormalizeDirectory(settingsOverlay.ScreenshotDirectory);
                try
                {
                    AppFeatures.ApplyAutoStart(settingsOverlay.AutoStartEnabled);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Failed to update startup setting.", ex);
                    Win32Api.MessageBoxUnicode(hwnd, UiText.StartupSettingFailed + Environment.NewLine + ex.Message, UiText.AppName, Win32Api.MbOk | Win32Api.MbIconError);
                    break;
                }
                AppSettings existingSettings = AppSettingsStore.Load();
                existingSettings.OutputDirectory = outputDirectory;
                existingSettings.ScreenshotDirectory = screenshotDirectory;
                existingSettings.AutoStartEnabled = settingsOverlay.AutoStartEnabled;
                existingSettings.GlobalHotkeyEnabled = settingsOverlay.GlobalHotkeyEnabled;
                existingSettings.GlobalHotkeyModifiers = AppFeatures.GetGlobalHotkeyModifiers(existingSettings);
                existingSettings.GlobalHotkeyKey = AppFeatures.GetGlobalHotkeyKey(existingSettings);
                AppSettingsStore.Save(existingSettings);
                string error;
                if (!AppFeatures.TrySyncBackgroundHotkeyAgent(settingsOverlay.GlobalHotkeyEnabled, out error))
                {
                    string message = settingsOverlay.GlobalHotkeyEnabled ? UiText.GlobalHotkeyStartFailed : UiText.GlobalHotkeyStopFailed;
                    if (!string.IsNullOrEmpty(error))
                    {
                        message += Environment.NewLine + error;
                    }
                    Win32Api.MessageBoxUnicode(hwnd, message, UiText.AppName, Win32Api.MbOk | Win32Api.MbIconWarning);
                }
                settingsOverlay.Visible = false;
                break;
            case SettingsOverlayCommand.Cancel:
                settingsOverlay.Visible = false;
                break;
            default:
                if (!panel.Contains(point))
                {
                    settingsOverlay.Visible = false;
                }
                break;
        }
        RequestPaint();
    }

    private static SettingsOverlayCommand HitSettingsOverlay(GpuRect panel, GpuPoint point)
    {
        if (GpuRenderer.GetSettingsButtonRect(panel, SettingsOverlayCommand.BrowseOutput).Contains(point))
        {
            return SettingsOverlayCommand.BrowseOutput;
        }
        if (GpuRenderer.GetSettingsButtonRect(panel, SettingsOverlayCommand.ClearOutput).Contains(point))
        {
            return SettingsOverlayCommand.ClearOutput;
        }
        if (GpuRenderer.GetSettingsButtonRect(panel, SettingsOverlayCommand.BrowseScreenshot).Contains(point))
        {
            return SettingsOverlayCommand.BrowseScreenshot;
        }
        if (GpuRenderer.GetSettingsButtonRect(panel, SettingsOverlayCommand.ClearScreenshot).Contains(point))
        {
            return SettingsOverlayCommand.ClearScreenshot;
        }
        if (GpuRenderer.GetSettingsButtonRect(panel, SettingsOverlayCommand.ToggleAutoStart).Contains(point))
        {
            return SettingsOverlayCommand.ToggleAutoStart;
        }
        if (GpuRenderer.GetSettingsButtonRect(panel, SettingsOverlayCommand.ToggleGlobalHotkey).Contains(point))
        {
            return SettingsOverlayCommand.ToggleGlobalHotkey;
        }
        if (GpuRenderer.GetSettingsButtonRect(panel, SettingsOverlayCommand.Save).Contains(point))
        {
            return SettingsOverlayCommand.Save;
        }
        if (GpuRenderer.GetSettingsButtonRect(panel, SettingsOverlayCommand.Cancel).Contains(point))
        {
            return SettingsOverlayCommand.Cancel;
        }
        return SettingsOverlayCommand.None;
    }

    private AnnotationItem GetPreviewItem()
    {
        if (textEditing)
        {
            int caret = ClampInlineIndex(inlineCaretIndex);
            string text = GetInlineVisibleText();
            return new AnnotationItem
            {
                Tool = ToolMode.Text,
                Start = textOrigin,
                End = textOrigin,
                Stroke = stroke,
                StrokeWidth = strokeWidth,
                Text = text,
                TextEditing = true,
                TextCaretIndex = caret + (compositionText == null ? 0 : compositionText.Length),
                TextSelectionAnchor = string.IsNullOrEmpty(compositionText) ? ClampInlineIndex(inlineSelectionAnchor) : caret,
                TextCompositionStart = string.IsNullOrEmpty(compositionText) ? -1 : caret,
                TextCompositionLength = string.IsNullOrEmpty(compositionText) ? 0 : compositionText.Length,
                TextCaretVisible = textCaretVisible
            };
        }
        if (!drawing)
        {
            return null;
        }
        if (currentTool == ToolMode.Pen)
        {
            return currentPen;
        }
        return new AnnotationItem
        {
            Tool = currentTool,
            Start = startPoint,
            End = currentPoint,
            Stroke = stroke,
            StrokeWidth = strokeWidth
        };
    }

    private void CommitTextIfNeeded()
    {
        if (!textEditing)
        {
            return;
        }
        int committedIndex = editingTextIndex;
        AnnotationSnapshot before = editingTextStartState;
        if (committedIndex >= 0 && committedIndex < items.Count && items[committedIndex].Tool == ToolMode.Text)
        {
            AnnotationItem item = items[committedIndex];
            if (string.IsNullOrEmpty(inlineText))
            {
                item.DisposeTextLayout();
                items.RemoveAt(committedIndex);
                undoStack.Push(new AnnotationUndoAction(AnnotationUndoKind.Delete, item, committedIndex));
                ClearSelection();
            }
            else
            {
                item.Start = textOrigin;
                item.End = textOrigin;
                item.Stroke = stroke;
                item.StrokeWidth = strokeWidth;
                item.Text = inlineText;
                AnnotationSnapshot after = CaptureAnnotation(item);
                RecordTransformUndo(item, committedIndex, before, after);
                SelectAnnotation(committedIndex);
            }
        }
        else if (!string.IsNullOrEmpty(inlineText))
        {
            AddAnnotation(new AnnotationItem
            {
                Tool = ToolMode.Text,
                Start = textOrigin,
                End = textOrigin,
                Stroke = stroke,
                StrokeWidth = strokeWidth,
                Text = inlineText
            });
        }
        textEditing = false;
        inlineText = string.Empty;
        compositionText = string.Empty;
        inlineCaretIndex = 0;
        inlineSelectionAnchor = 0;
        selectingInlineText = false;
        inlineComposing = false;
        editingTextIndex = -1;
        editingTextStartState = null;
        StopTextCaretTimer();
        DisposeInlineTextLayout();
        InvalidateHoverCache();
    }

    private void PositionImeWindow()
    {
        IntPtr context = Win32Api.ImmGetContext(hwnd);
        if (context == IntPtr.Zero)
        {
            return;
        }
        try
        {
            PositionImeWindow(context);
        }
        finally
        {
            Win32Api.ImmReleaseContext(hwnd, context);
        }
    }

    private void PositionImeWindow(IntPtr context)
    {
        if (context == IntPtr.Zero)
        {
            return;
        }
        GpuRect view = GetView();
        float scale = view.Width / Math.Max(1f, image.Width);
        float caretX = GetInlineCaretOffset();
        Win32Api.NativePoint client = new Win32Api.NativePoint();
        client.x = (int)Math.Round(view.X + (textOrigin.X + caretX) * scale);
        client.y = (int)Math.Round(view.Y + (textOrigin.Y + GetInlineTextEm() * 1.25f) * scale);
        Win32Api.CompositionForm form = new Win32Api.CompositionForm();
        form.dwStyle = Win32Api.CfsPoint;
        form.ptCurrentPos = client;
        Win32Api.ImmSetCompositionWindow(context, ref form);
    }

    private float GetInlineCaretOffset()
    {
        int caret = ClampInlineIndex(inlineCaretIndex);
        if (caret <= 0)
        {
            return 0f;
        }
        UpdateInlineTextLayout();
        if (inlineTextLayout == null)
        {
            return GpuRenderer.MeasureTextBounds(inlineText.Substring(0, caret), GetInlineTextEm()).Width;
        }
        return inlineTextLayout.HitTestTextPosition(caret, false).X;
    }

    private void UpdateInlineTextLayout()
    {
        if (!textEditing)
        {
            DisposeInlineTextLayout();
            return;
        }
        string visible = GetInlineVisibleText();
        float em = GetInlineTextEm();
        if (inlineTextLayout != null &&
            string.Equals(inlineTextLayout.Text, visible, StringComparison.Ordinal) &&
            Math.Abs(inlineTextLayout.Em - em) < 0.001f)
        {
            return;
        }
        DisposeInlineTextLayout();
        inlineTextLayout = new TextLayoutCache(visible, em, Math.Max(80f, image.Width), Math.Max(em * 1.5f, image.Height));
    }

    private void DisposeInlineTextLayout()
    {
        if (inlineTextLayout != null)
        {
            inlineTextLayout.Dispose();
            inlineTextLayout = null;
        }
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
        InvalidateHoverCache();
    }

    private static void DisposeAnnotationResources(AnnotationItem item)
    {
        if (item != null)
        {
            item.DisposeTextLayout();
        }
    }

    private bool DeleteSelectedAnnotation()
    {
        if (!HasSelectedItem())
        {
            return false;
        }

        int index = selectedIndex;
        AnnotationItem item = items[index];
        items.RemoveAt(index);
        DisposeAnnotationResources(item);
        undoStack.Push(new AnnotationUndoAction(AnnotationUndoKind.Delete, item, index));
        ClearSelection();
        InvalidateHoverCache();
        RequestPaint();
        return true;
    }

    private void ClearAnnotations()
    {
        for (int i = 0; i < items.Count; i++)
        {
            DisposeAnnotationResources(items[i]);
        }
        items.Clear();
        undoStack.Clear();
        ClearSelection();
        textEditing = false;
        inlineText = string.Empty;
        compositionText = string.Empty;
        inlineCaretIndex = 0;
        inlineSelectionAnchor = 0;
        selectingInlineText = false;
        inlineComposing = false;
        editingTextIndex = -1;
        editingTextStartState = null;
        StopTextCaretTimer();
        DisposeInlineTextLayout();
        InvalidateHoverCache();
        HideToolOptions();
    }

    private void UndoAnnotationAction()
    {
        if (undoStack.Count == 0)
        {
            if (items.Count > 0)
            {
                AnnotationItem removed = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                DisposeAnnotationResources(removed);
                if (selectedIndex >= items.Count)
                {
                    ClearSelection();
                }
                InvalidateHoverCache();
                RequestPaint();
            }
            return;
        }

        AnnotationUndoAction action = undoStack.Pop();
        if (action.Kind == AnnotationUndoKind.Add)
        {
            int index = items.IndexOf(action.Item);
            if (index >= 0)
            {
                DisposeAnnotationResources(action.Item);
                items.RemoveAt(index);
                if (selectedIndex == index)
                {
                    ClearSelection();
                }
                else if (selectedIndex > index)
                {
                    selectedIndex--;
                }
            }
        }
        else if (action.Kind == AnnotationUndoKind.Delete && action.Item != null)
        {
            int index = Math.Max(0, Math.Min(action.Index, items.Count));
            if (action.Item.Tool == ToolMode.Text)
            {
                action.Item.DisposeTextLayout();
            }
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

        InvalidateHoverCache();
        RequestPaint();
    }

    private void ReloadSettingsForCurrentWindow()
    {
        AppSettings settings = AppSettingsStore.Load();
        outputDirectory = settings.OutputDirectory;
        screenshotDirectory = settings.ScreenshotDirectory;
    }

    private void SelectAnnotation(int index)
    {
        if (index < 0 || index >= items.Count)
        {
            ClearSelection();
            return;
        }

        selectedIndex = index;
        SyncStyleFromSelectedAnnotation();
        ResetSelectionEditState();
        InvalidateHoverCache();
    }

    private void ClearSelection()
    {
        selectedIndex = -1;
        ResetSelectionEditState();
        InvalidateHoverCache();
    }

    private bool HasSelectedItem()
    {
        return selectedIndex >= 0 && selectedIndex < items.Count;
    }

    private void SyncStyleFromSelectedAnnotation()
    {
        if (!HasSelectedItem())
        {
            return;
        }
        AnnotationItem item = items[selectedIndex];
        currentTool = item.Tool;
        stroke = item.Stroke.A == 0 ? AppStyles.DefaultStroke : item.Stroke;
        strokeWidth = item.StrokeWidth > 0f ? item.StrokeWidth : AppStyles.DefaultStrokeWidth;
        if (GpuRenderer.ToolSupportsOptions(item.Tool))
        {
            ShowToolOptionsTemporarily();
        }
        else
        {
            HideToolOptions();
        }
    }

    private int HitTestTextAnnotation(GpuPoint point)
    {
        float tolerance = GetSelectionHitTolerance();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].Tool == ToolMode.Text && IsPointInAnnotation(point, items[i], tolerance))
            {
                return i;
            }
        }
        return -1;
    }

    private int HitTestAnnotation(GpuPoint point)
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

    private bool IsPointInAnnotation(GpuPoint point, AnnotationItem item, float tolerance)
    {
        if (item == null)
        {
            return false;
        }

        float itemWidth = item.StrokeWidth > 0f ? item.StrokeWidth : strokeWidth;
        if (item.Tool == ToolMode.Arrow)
        {
            return IsPointInPolygon(point, GpuRenderer.BuildArrow(item, itemWidth), tolerance);
        }
        if (item.Tool == ToolMode.Pen)
        {
            GpuPoint[] points = item.GetDrawingPoints();
            if (points.Length == 1)
            {
                return Distance(point, points[0]) <= tolerance;
            }
            float threshold = Math.Max(tolerance, itemWidth * 0.75f);
            for (int i = 1; i < points.Length; i++)
            {
                if (DistanceToSegment(point, points[i - 1], points[i]) <= threshold)
                {
                    return true;
                }
            }
            return false;
        }
        if (item.Tool == ToolMode.Rect)
        {
            return IsPointOnRectangle(point, GpuRect.Normalize(item.Start, item.End), Math.Max(tolerance, itemWidth * 0.5f));
        }
        if (item.Tool == ToolMode.Ellipse)
        {
            return IsPointOnEllipse(point, GpuRect.Normalize(item.Start, item.End), Math.Max(tolerance, itemWidth * 0.5f));
        }

        GpuRect bounds = GpuRenderer.GetItemBounds(item);
        float pad = item.Tool == ToolMode.Text ? tolerance : Math.Max(tolerance, itemWidth);
        bounds = Inflate(bounds, pad, pad);
        return bounds.Contains(point);
    }

    private static bool IsPointInPolygon(GpuPoint point, GpuPoint[] polygon, float tolerance)
    {
        if (polygon == null || polygon.Length < 3)
        {
            return false;
        }

        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            GpuPoint a = polygon[i];
            GpuPoint b = polygon[j];
            if (DistanceToSegment(point, a, b) <= tolerance)
            {
                return true;
            }
            bool crosses = ((a.Y > point.Y) != (b.Y > point.Y)) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / ((b.Y - a.Y) == 0f ? 0.0001f : (b.Y - a.Y)) + a.X;
            if (crosses)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static bool IsPointOnRectangle(GpuPoint point, GpuRect rect, float tolerance)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return false;
        }
        GpuRect outer = Inflate(rect, tolerance, tolerance);
        if (!outer.Contains(point))
        {
            return false;
        }
        GpuRect inner = Inflate(rect, -tolerance, -tolerance);
        if (inner.Width <= 0f || inner.Height <= 0f)
        {
            return true;
        }
        return !inner.Contains(point);
    }

    private static bool IsPointOnEllipse(GpuPoint point, GpuRect rect, float tolerance)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return false;
        }

        float rx = rect.Width / 2f;
        float ry = rect.Height / 2f;
        float cx = rect.X + rx;
        float cy = rect.Y + ry;
        if (rx <= 0.001f || ry <= 0.001f)
        {
            return DistanceToSegment(point, new GpuPoint(rect.Left, rect.Top), new GpuPoint(rect.Right, rect.Bottom)) <= tolerance;
        }

        float nx = (point.X - cx) / rx;
        float ny = (point.Y - cy) / ry;
        float normalizedDistance = (float)Math.Sqrt(nx * nx + ny * ny);
        float normalizedTolerance = tolerance / Math.Max(1f, Math.Min(rx, ry));
        return Math.Abs(normalizedDistance - 1f) <= normalizedTolerance;
    }

    private SelectionHandle HitTestSelectionHandle(GpuPoint point)
    {
        if (!HasSelectedItem() || !CanResizeAnnotation(items[selectedIndex]))
        {
            return SelectionHandle.None;
        }

        AnnotationItem item = items[selectedIndex];
        float radius = Math.Max(4f, 8f / Math.Max(0.001f, GetCurrentScale()));
        if (item.Tool == ToolMode.Arrow)
        {
            if (Distance(point, item.End) <= radius)
            {
                return SelectionHandle.ArrowEnd;
            }
            if (Distance(point, item.Start) <= radius)
            {
                return SelectionHandle.ArrowStart;
            }
            return SelectionHandle.None;
        }

        GpuRect bounds = GetSelectionFrameBounds(item);
        if (bounds.IsEmpty)
        {
            return SelectionHandle.None;
        }

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
            if (Distance(point, GetSelectionHandlePoint(bounds, handles[i])) <= radius)
            {
                return handles[i];
            }
        }
        return SelectionHandle.None;
    }

    private static GpuPoint GetSelectionHandlePoint(GpuRect bounds, SelectionHandle handle)
    {
        switch (handle)
        {
            case SelectionHandle.TopLeft:
                return new GpuPoint(bounds.Left, bounds.Top);
            case SelectionHandle.Top:
                return new GpuPoint(bounds.Left + bounds.Width / 2f, bounds.Top);
            case SelectionHandle.TopRight:
                return new GpuPoint(bounds.Right, bounds.Top);
            case SelectionHandle.Right:
                return new GpuPoint(bounds.Right, bounds.Top + bounds.Height / 2f);
            case SelectionHandle.BottomRight:
                return new GpuPoint(bounds.Right, bounds.Bottom);
            case SelectionHandle.Bottom:
                return new GpuPoint(bounds.Left + bounds.Width / 2f, bounds.Bottom);
            case SelectionHandle.BottomLeft:
                return new GpuPoint(bounds.Left, bounds.Bottom);
            case SelectionHandle.Left:
                return new GpuPoint(bounds.Left, bounds.Top + bounds.Height / 2f);
            default:
                return GpuPoint.Empty;
        }
    }

    private GpuRect GetSelectionFrameBounds(AnnotationItem item)
    {
        GpuRect bounds = GpuRenderer.GetItemBounds(item);
        if (bounds.IsEmpty)
        {
            return bounds;
        }
        float pad = Math.Max(2f, 4f / Math.Max(0.001f, GetCurrentScale()));
        return Inflate(bounds, pad, pad);
    }

    private void RunOcr()
    {
        if (ocrRunning)
        {
            return;
        }

        CommitTextIfNeeded();
        AppSettings settings = AppSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.BaiduOcrApiKey) || string.IsNullOrWhiteSpace(settings.BaiduOcrSecretKey))
        {
            Win32Api.MessageBoxUnicode(hwnd, UiText.OcrCredentialsMissing, UiText.OcrTitle, Win32Api.MbOk | Win32Api.MbIconWarning);
            AppFeatures.StartSettingsWindow();
            return;
        }

        try
        {
            int version = ++ocrRequestVersion;
            OcrController.OcrRequest request = OcrController.CreateRequest(image, items, selectedIndex, settings, version);
            ocrRunning = true;
            Win32Api.SetWindowTextUnicode(hwnd, UiText.OcrRunning);
            OcrController.BeginRecognize(hwnd, WmOcrComplete, request);
        }
        catch (Exception ex)
        {
            ocrRunning = false;
            Win32Api.SetWindowTextUnicode(hwnd, UiText.AppName);
            AppLog.Error("OCR failed.", ex);
            Win32Api.MessageBoxUnicode(hwnd, ex.Message, UiText.OcrTitle, Win32Api.MbOk | Win32Api.MbIconError);
        }
    }

    private void CompleteOcr(IntPtr resultHandle)
    {
        OcrController.OcrAsyncResult async = OcrController.TakeResult(resultHandle);
        if (async == null || async.Version != ocrRequestVersion)
        {
            return;
        }

        ocrRunning = false;
        Win32Api.SetWindowTextUnicode(hwnd, UiText.AppName);
        if (async.Error != null)
        {
            AppLog.Error("OCR failed.", async.Error);
            Win32Api.MessageBoxUnicode(hwnd, async.Error.Message, UiText.OcrTitle, Win32Api.MbOk | Win32Api.MbIconError);
            return;
        }
        if (async.Result == null)
        {
            return;
        }

        AppSettings settings = async.Settings ?? AppSettingsStore.Load();
        OcrResultWindow.Show(hwnd, async.Result, settings.OcrLayout);
    }

    private void SaveAndClose()
    {
        CommitTextIfNeeded();
        string targetDirectory = outputDirectory;
        if (preferActualSize && !string.IsNullOrEmpty(screenshotDirectory))
        {
            targetDirectory = screenshotDirectory;
        }
        byte[] pixels = GpuRenderer.RenderExport(image, items);
        string outputPath = GetAnnotatedOutputPath(imagePath, targetDirectory);
        WicCodec.SavePixels(outputPath, image.Width, image.Height, pixels);
        ClipboardBridge.SetImage(hwnd, outputPath, image.Width, image.Height, pixels);
        Win32Api.DestroyWindow(hwnd);
    }

    private GpuRect GetView()
    {
        Win32Api.NativeRect rect;
        Win32Api.GetClientRect(hwnd, out rect);
        return GetView(Math.Max(1, rect.right - rect.left), Math.Max(1, rect.bottom - rect.top));
    }

    private GpuRect GetView(int width, int height)
    {
        float availableH = GetAvailableImageHeight(height);
        float scale = GetFitScale(width, height) * zoom;
        float w = RoundViewPixel(image.Width * scale);
        float h = RoundViewPixel(image.Height * scale);
        float x = RoundViewCoordinate((width - w) / 2f + viewOffset.X);
        float y = RoundViewCoordinate(AppStyles.ToolbarHeight + (availableH - h) / 2f + viewOffset.Y);
        return new GpuRect(x, y, w, h);
    }

    private GpuPoint ToImagePoint(int x, int y)
    {
        GpuRect view = GetView();
        return ToImagePoint(new GpuPoint(x, y), view);
    }

    private GpuPoint ToImagePoint(GpuPoint clientPoint, GpuRect view)
    {
        float scale = view.Width / Math.Max(1f, image.Width);
        float imageX = (clientPoint.X - view.X) / Math.Max(0.001f, scale);
        float imageY = (clientPoint.Y - view.Y) / Math.Max(0.001f, scale);
        imageX = Math.Max(0f, Math.Min(image.Width, imageX));
        imageY = Math.Max(0f, Math.Min(image.Height, imageY));
        return new GpuPoint(imageX, imageY);
    }

    private void ResetView()
    {
        zoom = 1f;
        viewOffset = GpuPoint.Empty;
        InvalidateHoverCache();
    }

    private void ApplyInitialView(int width, int height)
    {
        if (initialViewApplied)
        {
            return;
        }
        initialViewApplied = true;
        if (!preferActualSize)
        {
            return;
        }

        float fitScale = GetFitScale(width, height);
        if (fitScale > 0.001f)
        {
            zoom = Math.Max(0.05f, Math.Min(16f, 1f / fitScale));
            viewOffset = GpuPoint.Empty;
        }
    }

    private float GetFitScale(int width, int height)
    {
        float availableH = GetAvailableImageHeight(height);
        return Math.Min((float)width / image.Width, availableH / image.Height);
    }

    private static float GetAvailableImageHeight(int height)
    {
        return Math.Max(1f, height - AppStyles.ToolbarHeight);
    }

    private GpuExtent GetInitialWindowExtent()
    {
        int screenWidth = Math.Max(MinWindowWidth, Win32Api.GetSystemMetrics(Win32Api.SmCxScreen));
        int screenHeight = Math.Max(MinWindowHeight, Win32Api.GetSystemMetrics(Win32Api.SmCyScreen));
        int maxWidth = Math.Max(MinWindowWidth, screenWidth - InitialWindowScreenMargin);
        int maxHeight = Math.Max(MinWindowHeight, screenHeight - InitialWindowScreenMargin);
        int width = Math.Max(MinWindowWidth, Math.Min(maxWidth, image.Width + 60));
        int height = Math.Max(MinWindowHeight, Math.Min(maxHeight, image.Height + AppStyles.ToolbarHeight + 60));
        return new GpuExtent(width, height);
    }

    private static float RoundViewPixel(float value)
    {
        return Math.Max(1f, (float)Math.Round(value));
    }

    private static float RoundViewCoordinate(float value)
    {
        return (float)Math.Round(value);
    }

    private void RequestPaint()
    {
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }

    private void WarmUpRenderer()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (renderer == null)
        {
            renderer = GpuRenderer.CreateForWindow(image);
        }
    }

    private void StartFirstFrameFadeIn()
    {
        if (firstFrameFadeStarted || hwnd == IntPtr.Zero)
        {
            return;
        }
        firstFrameFadeStarted = true;
        firstFrameOpacity = 48;
        ApplyFirstFrameOpacity();
        Win32Api.KillTimer(hwnd, FirstFrameFadeTimerId);
        Win32Api.SetTimer(hwnd, FirstFrameFadeTimerId, 16, IntPtr.Zero);
    }

    private void StopFirstFrameFade()
    {
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.KillTimer(hwnd, FirstFrameFadeTimerId);
        }
    }

    private void RecreateRenderer()
    {
        if (renderer != null)
        {
            renderer.Dispose();
            renderer = null;
        }
        renderer = GpuRenderer.CreateForWindow(image);
    }

    private void HandleRenderFailure(Exception ex)
    {
        bool transient = IsTransientD2DFailure(ex);
        consecutiveRenderFailures = transient ? 0 : consecutiveRenderFailures + 1;
        AppLog.Error("GPU render failed; rebuilding Direct2D resources.", ex);

        Exception rebuildError;
        if (!TryRecreateRenderer(out rebuildError))
        {
            ShowFatalErrorAndClose("GPU renderer rebuild failed.", rebuildError);
            return;
        }

        if (!transient && consecutiveRenderFailures >= MaxConsecutiveRenderFailures)
        {
            ShowFatalErrorAndClose("GPU renderer failed repeatedly.", ex);
            return;
        }

        RequestPaint();
    }

    private void InvalidateHoverCache()
    {
        hoverStateVersion++;
        cachedHoverVersion = -1;
    }

    private int CacheHoverCursor(int x, int y, int cursor)
    {
        cachedHoverVersion = hoverStateVersion;
        cachedHoverX = x;
        cachedHoverY = y;
        cachedHoverCursorId = cursor;
        return cursor;
    }

    private bool TryRecreateRenderer(out Exception error)
    {
        error = null;
        try
        {
            RecreateRenderer();
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private void ShowFatalErrorAndClose(string logMessage, Exception ex)
    {
        if (fatalErrorShown)
        {
            return;
        }
        fatalErrorShown = true;
        AppLog.Error(logMessage, ex);
        string message = UiText.GpuFatal + Environment.NewLine + ex.Message;
        Win32Api.MessageBoxUnicode(hwnd, message, UiText.AppName, Win32Api.MbOk | Win32Api.MbIconError);
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.DestroyWindow(hwnd);
        }
    }

    private static bool IsTransientD2DFailure(Exception ex)
    {
        NativeCallException native = FindNativeCallException(ex);
        if (native == null || native.Operation == null || !native.Operation.StartsWith("D2D", StringComparison.Ordinal))
        {
            return false;
        }

        uint result = unchecked((uint)native.Result);
        return result == 0x8899000C;
    }

    private static NativeCallException FindNativeCallException(Exception ex)
    {
        while (ex != null)
        {
            NativeCallException native = ex as NativeCallException;
            if (native != null)
            {
                return native;
            }
            ex = ex.InnerException;
        }
        return null;
    }

    private void BeginSelectionMove(GpuPoint point)
    {
        movingSelection = true;
        resizingSelection = false;
        cursorId = Win32Api.IdcSizeAll;
        activeSelectionHandle = SelectionHandle.None;
        selectionMoved = false;
        moveStartPoint = point;
        moveCurrentPoint = point;
        lastMovePoint = point;
        selectionEditStartState = CaptureAnnotation(items[selectedIndex]);
        selectionEditStartBounds = GpuRenderer.GetItemBounds(items[selectedIndex]);
        drawing = false;
        currentPen = null;
    }

    private void BeginSelectionEdit(GpuPoint point, SelectionHandle handle)
    {
        movingSelection = false;
        resizingSelection = true;
        cursorId = GetCursorForSelectionHandle(handle);
        activeSelectionHandle = handle;
        selectionMoved = false;
        moveStartPoint = point;
        moveCurrentPoint = point;
        lastMovePoint = point;
        selectionEditStartState = CaptureAnnotation(items[selectedIndex]);
        selectionEditStartBounds = GpuRenderer.GetItemBounds(items[selectedIndex]);
        drawing = false;
        currentPen = null;
    }

    private void FinishSelectionEdit(GpuPoint point)
    {
        if (!HasSelectedItem())
        {
            ResetSelectionEditState();
            return;
        }

        moveCurrentPoint = point;
        bool changed = selectionMoved || Distance(moveStartPoint, moveCurrentPoint) >= CanvasPixelsToImageDistance(MoveSampleThresholdPixels);
        AnnotationItem item = items[selectedIndex];
        AnnotationSnapshot before = selectionEditStartState ?? CaptureAnnotation(item);
        if (changed)
        {
            ApplyActiveSelectionEdit();
            AnnotationSnapshot after = CaptureAnnotation(item);
            RecordTransformUndo(item, selectedIndex, before, after);
        }
        else
        {
            ApplyAnnotationSnapshot(item, before);
        }
        ResetSelectionEditState();
    }

    private void ApplyActiveSelectionEdit()
    {
        if (!HasSelectedItem())
        {
            return;
        }

        AnnotationItem item = items[selectedIndex];
        AnnotationSnapshot before = selectionEditStartState ?? CaptureAnnotation(item);
        ApplyAnnotationSnapshot(item, before);
        GpuPoint offset = GetMoveOffset();
        if (movingSelection)
        {
            item.Translate(offset.X, offset.Y);
        }
        else if (resizingSelection)
        {
            ApplyResizeToAnnotation(item, before, selectionEditStartBounds, activeSelectionHandle, offset);
        }
    }

    private GpuPoint GetMoveOffset()
    {
        return new GpuPoint(moveCurrentPoint.X - moveStartPoint.X, moveCurrentPoint.Y - moveStartPoint.Y);
    }

    private void ResetSelectionEditState()
    {
        movingSelection = false;
        resizingSelection = false;
        activeSelectionHandle = SelectionHandle.None;
        selectionMoved = false;
        selectionEditStartState = null;
        selectionEditStartBounds = new GpuRect();
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
            Stroke = item.Stroke,
            StrokeWidth = item.StrokeWidth,
            Points = item.Points.ToArray(),
            Text = item.Text
        };
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
        item.Stroke = snapshot.Stroke;
        item.StrokeWidth = snapshot.StrokeWidth;
        item.Text = snapshot.Text;
        item.SetPoints(snapshot.Points);
        if (item.Tool == ToolMode.Text)
        {
            item.DisposeTextLayout();
        }
    }

    private bool RecordTransformUndo(AnnotationItem item, int index, AnnotationSnapshot before, AnnotationSnapshot after)
    {
        if (item == null || before == null || after == null || AnnotationSnapshotsEqual(before, after))
        {
            return false;
        }

        undoStack.Push(new AnnotationUndoAction(AnnotationUndoKind.Transform, item, index, before));
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
            a.Stroke.Packed != b.Stroke.Packed ||
            Math.Abs(a.StrokeWidth - b.StrokeWidth) > 0.001f ||
            !string.Equals(a.Text ?? string.Empty, b.Text ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        GpuPoint[] aPoints = a.Points ?? new GpuPoint[0];
        GpuPoint[] bPoints = b.Points ?? new GpuPoint[0];
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

    private static void ApplyResizeToAnnotation(AnnotationItem item, AnnotationSnapshot originalState, GpuRect originalBounds, SelectionHandle handle, GpuPoint offset)
    {
        if (item == null || originalState == null || handle == SelectionHandle.None)
        {
            return;
        }

        ApplyAnnotationSnapshot(item, originalState);
        if (item.Tool == ToolMode.Arrow)
        {
            if (handle == SelectionHandle.ArrowStart)
            {
                item.Start = new GpuPoint(originalState.Start.X + offset.X, originalState.Start.Y + offset.Y);
            }
            else if (handle == SelectionHandle.ArrowEnd)
            {
                item.End = new GpuPoint(originalState.End.X + offset.X, originalState.End.Y + offset.Y);
            }
            return;
        }
        if (originalBounds.IsEmpty)
        {
            return;
        }
        GpuRect targetBounds = GetResizedBounds(originalBounds, handle, offset);
        TransformAnnotationToBounds(item, originalBounds, targetBounds);
    }

    private static GpuRect GetResizedBounds(GpuRect originalBounds, SelectionHandle handle, GpuPoint offset)
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

        return GpuRect.FromEdges(left, top, right, bottom);
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

    private static void TransformAnnotationToBounds(AnnotationItem item, GpuRect sourceBounds, GpuRect targetBounds)
    {
        if (item.Tool == ToolMode.Rect || item.Tool == ToolMode.Ellipse || item.Tool == ToolMode.Mosaic)
        {
            item.Start = new GpuPoint(targetBounds.Left, targetBounds.Top);
            item.End = new GpuPoint(targetBounds.Right, targetBounds.Bottom);
            return;
        }

        item.Start = TransformPointToBounds(item.Start, sourceBounds, targetBounds);
        item.End = TransformPointToBounds(item.End, sourceBounds, targetBounds);
        if (item.Points.Count > 0)
        {
            GpuPoint[] points = new GpuPoint[item.Points.Count];
            for (int i = 0; i < item.Points.Count; i++)
            {
                points[i] = TransformPointToBounds(item.Points[i], sourceBounds, targetBounds);
            }
            item.SetPoints(points);
        }
    }

    private static GpuPoint TransformPointToBounds(GpuPoint point, GpuRect sourceBounds, GpuRect targetBounds)
    {
        float xRatio = sourceBounds.Width <= 0.0001f ? 0.5f : (point.X - sourceBounds.Left) / sourceBounds.Width;
        float yRatio = sourceBounds.Height <= 0.0001f ? 0.5f : (point.Y - sourceBounds.Top) / sourceBounds.Height;
        return new GpuPoint(
            targetBounds.Left + xRatio * targetBounds.Width,
            targetBounds.Top + yRatio * targetBounds.Height);
    }

    private static bool CanResizeAnnotation(AnnotationItem item)
    {
        return item != null && item.Tool != ToolMode.Text;
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
            GpuRect rect = GpuRect.Normalize(item.Start, item.End);
            return rect.Width >= MinAnnotationExtent && rect.Height >= MinAnnotationExtent;
        }
        if (item.Tool == ToolMode.Pen)
        {
            GpuPoint[] points = item.GetDrawingPoints();
            if (points.Length < 2)
            {
                return false;
            }
            for (int i = 1; i < points.Length; i++)
            {
                if (Distance(points[0], points[i]) >= MinAnnotationExtent)
                {
                    return true;
                }
            }
            return false;
        }
        if (item.Tool == ToolMode.Text)
        {
            return !string.IsNullOrEmpty(item.Text);
        }
        return true;
    }

    private float GetCurrentScale()
    {
        GpuRect view = GetView();
        return view.Width / Math.Max(1f, image.Width);
    }

    private float CanvasPixelsToImageDistance(float pixels)
    {
        return Math.Max(0.01f, pixels / Math.Max(0.001f, GetCurrentScale()));
    }

    private float GetSelectionHitTolerance()
    {
        return Math.Max(2f, SelectionHitTolerancePixels / Math.Max(0.001f, GetCurrentScale()));
    }

    private static GpuRect Inflate(GpuRect rect, float dx, float dy)
    {
        return new GpuRect(rect.X - dx, rect.Y - dy, rect.Width + dx * 2f, rect.Height + dy * 2f);
    }

    private static float Distance(GpuPoint a, GpuPoint b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private static float DistanceToSegment(GpuPoint point, GpuPoint a, GpuPoint b)
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
        GpuPoint projection = new GpuPoint(a.X + t * dx, a.Y + t * dy);
        return Distance(point, projection);
    }

    private static bool SamePoint(GpuPoint a, GpuPoint b)
    {
        return Math.Abs(a.X - b.X) <= 0.001f && Math.Abs(a.Y - b.Y) <= 0.001f;
    }

    private static string GetAnnotatedOutputPath(string path, string outputDirectory)
    {
        string dir = outputDirectory;
        if (string.IsNullOrEmpty(dir))
        {
            dir = Path.GetDirectoryName(path);
        }
        if (string.IsNullOrEmpty(dir))
        {
            dir = Environment.CurrentDirectory;
        }
        Directory.CreateDirectory(dir);
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".png";
        }
        if (string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".tif", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".tiff", StringComparison.OrdinalIgnoreCase))
        {
            ext = ".png";
        }
        string candidate = Path.Combine(dir, name + "_\u6807\u6ce8" + ext);
        int index = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, name + "_\u6807\u6ce8_" + index + ext);
            index++;
        }
        return candidate;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        StopToolOptionsTimer();
        StopToolOptionsAnimation();
        StopTextCaretTimer();
        StopFirstFrameFade();
        for (int i = 0; i < items.Count; i++)
        {
            DisposeAnnotationResources(items[i]);
        }
        if (renderer != null)
        {
            renderer.Dispose();
            renderer = null;
        }
    }
}

internal static class ShellDialogs
{
    public static string BrowseForFolder(IntPtr owner, string title)
    {
        IntPtr display = Marshal.AllocHGlobal(520);
        IntPtr titlePointer = Marshal.StringToHGlobalUni(title ?? string.Empty);
        IntPtr pidl = IntPtr.Zero;
        try
        {
            Win32Api.BrowseInfo info = new Win32Api.BrowseInfo();
            info.hwndOwner = owner;
            info.pszDisplayName = display;
            info.lpszTitle = titlePointer;
            info.ulFlags = 0x00000001 | 0x00000040 | 0x00000010;
            pidl = Win32Api.SHBrowseForFolder(ref info);
            if (pidl == IntPtr.Zero)
            {
                return null;
            }

            IntPtr pathBuffer = Marshal.AllocHGlobal(520);
            try
            {
                if (!Win32Api.SHGetPathFromIDList(pidl, pathBuffer))
                {
                    return null;
                }
                string path = Marshal.PtrToStringUni(pathBuffer);
                return AppSettingsStore.NormalizeDirectory(path);
            }
            finally
            {
                Marshal.FreeHGlobal(pathBuffer);
            }
        }
        finally
        {
            if (pidl != IntPtr.Zero)
            {
                Win32Api.CoTaskMemFree(pidl);
            }
            Marshal.FreeHGlobal(titlePointer);
            Marshal.FreeHGlobal(display);
        }
    }
}
