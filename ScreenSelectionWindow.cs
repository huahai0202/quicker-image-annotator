using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal sealed class ScreenSelectionWindow
{
    private const string WindowClassName = "QuickerScreenSelectionWindow";
    private const int MinSelectionSide = 4;
    private const int DirtyPadding = 180;
    private const int DragThreshold = 4;
    private const byte DimOverlayAlpha = 122;
    private const int HudHeight = 28;
    private const int HudPadding = 10;
    private const int MagnifierFrameSize = 136;
    private const int MagnifierImageSize = 128;
    private const int MagnifierInset = 4;
    private const int MagnifierScale = 4;
    private static readonly Dictionary<IntPtr, ScreenSelectionWindow> Windows = new Dictionary<IntPtr, ScreenSelectionWindow>();
    private static Win32Api.WindowProc sharedProc;

    private readonly ScreenCaptureSnapshot snapshot;
    private readonly ScreenSelectionTarget[] windowTargets;
    private byte[] paintBuffer;
    private byte[] magnifierBuffer;
    private IntPtr hwnd;
    private IntPtr backBufferDc;
    private IntPtr backBufferSurface;
    private IntPtr oldBackBufferSurface;
    private int backBufferWidth;
    private int backBufferHeight;
    private IntPtr dimDc;
    private IntPtr dimSurface;
    private IntPtr oldDimSurface;
    private bool closed;
    private bool mouseDown;
    private bool dragging;
    private bool hasSelection;
    private bool accepted;
    private int startX;
    private int startY;
    private int currentX;
    private int currentY;
    private bool pointerSeen;
    private int hoverTargetIndex = -1;
    private int pressedTargetIndex = -1;
    private Win32Api.NativeRect selectedRegion;

    private ScreenSelectionWindow(ScreenCaptureSnapshot snapshot)
    {
        this.snapshot = snapshot;
        windowTargets = CaptureWindowTargets(snapshot);
    }

    public static bool TrySelectRegion(ScreenCaptureSnapshot snapshot, out Win32Api.NativeRect region)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException("snapshot");
        }

        ScreenSelectionWindow window = new ScreenSelectionWindow(snapshot);
        return window.Run(out region);
    }

    public static Win32Api.NativeRect NormalizeRegionForSelfTest(int x1, int y1, int x2, int y2, int width, int height)
    {
        return NormalizeRegion(x1, y1, x2, y2, width, height);
    }

    public static bool TryConvertWindowRectForSelfTest(Win32Api.NativeRect windowRect, int snapshotLeft, int snapshotTop, int snapshotWidth, int snapshotHeight, out Win32Api.NativeRect region)
    {
        return TryConvertWindowRect(windowRect, snapshotLeft, snapshotTop, snapshotWidth, snapshotHeight, out region);
    }

    public static string GetRegionSizeTextForSelfTest(Win32Api.NativeRect region)
    {
        return GetRegionSizeText(region);
    }

    public static Win32Api.NativeRect GetMagnifierRectForSelfTest(int x, int y, int width, int height)
    {
        return GetMagnifierRect(x, y, width, height);
    }

    private bool Run(out Win32Api.NativeRect region)
    {
        EnsureWindowClass();
        hwnd = Win32Api.CreateWindowEx(
            Win32Api.WsExTopMost | Win32Api.WsExToolWindow,
            WindowClassName,
            UiText.AppName,
            Win32Api.WsPopup,
            snapshot.Left,
            snapshot.Top,
            snapshot.Width,
            snapshot.Height,
            IntPtr.Zero,
            IntPtr.Zero,
            Win32Api.GetModuleHandle(null),
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowEx failed.");
        }

        Windows[hwnd] = this;
        Win32Api.ShowWindow(hwnd, Win32Api.SwShow);
        Win32Api.SetForegroundWindow(hwnd);
        Win32Api.SetFocus(hwnd);
        Win32Api.UpdateWindow(hwnd);

        Win32Api.Msg msg;
        while (!closed && Win32Api.GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            Win32Api.TranslateMessage(ref msg);
            Win32Api.DispatchMessage(ref msg);
        }

        region = selectedRegion;
        return accepted;
    }

    private static void EnsureWindowClass()
    {
        if (sharedProc != null)
        {
            return;
        }

        sharedProc = WindowProc;
        Win32Api.WndClassEx wc = new Win32Api.WndClassEx();
        wc.cbSize = (uint)Marshal.SizeOf(typeof(Win32Api.WndClassEx));
        wc.style = Win32Api.CsHRedraw | Win32Api.CsVRedraw;
        wc.lpfnWndProc = sharedProc;
        wc.hInstance = Win32Api.GetModuleHandle(null);
        wc.hCursor = Win32Api.LoadCursor(IntPtr.Zero, new IntPtr(Win32Api.IdcCross));
        wc.hbrBackground = IntPtr.Zero;
        wc.lpszClassName = WindowClassName;
        ushort atom = Win32Api.RegisterClassEx(ref wc);
        if (atom == 0)
        {
            throw new InvalidOperationException("RegisterClassEx failed.");
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        ScreenSelectionWindow window;
        if (Windows.TryGetValue(hwnd, out window))
        {
            return window.HandleMessage(msg, wParam, lParam);
        }
        return Win32Api.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr HandleMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32Api.WmEraseBkgnd:
                return new IntPtr(1);
            case Win32Api.WmPaint:
                Paint();
                return IntPtr.Zero;
            case Win32Api.WmSetCursor:
                Win32Api.SetCursor(Win32Api.LoadCursor(IntPtr.Zero, new IntPtr(Win32Api.IdcCross)));
                return new IntPtr(1);
            case Win32Api.WmLButtonDown:
                BeginSelection(Win32Api.GetX(lParam), Win32Api.GetY(lParam));
                return IntPtr.Zero;
            case Win32Api.WmMouseMove:
                MoveSelection(Win32Api.GetX(lParam), Win32Api.GetY(lParam));
                return IntPtr.Zero;
            case Win32Api.WmLButtonUp:
                EndSelection(Win32Api.GetX(lParam), Win32Api.GetY(lParam));
                return IntPtr.Zero;
            case Win32Api.WmRButtonDown:
                Cancel();
                return IntPtr.Zero;
            case Win32Api.WmKeyDown:
                KeyDown(wParam.ToInt32());
                return IntPtr.Zero;
            case Win32Api.WmClose:
                Cancel();
                return IntPtr.Zero;
            case Win32Api.WmDestroy:
                Windows.Remove(hwnd);
                ReleaseBackBufferResources();
                ReleaseDimResources();
                this.hwnd = IntPtr.Zero;
                closed = true;
                return IntPtr.Zero;
        }

        return Win32Api.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void BeginSelection(int x, int y)
    {
        mouseDown = true;
        dragging = false;
        hasSelection = false;
        startX = Clamp(x, 0, snapshot.Width);
        startY = Clamp(y, 0, snapshot.Height);
        currentX = startX;
        currentY = startY;
        pointerSeen = true;
        pressedTargetIndex = HitTestWindowTarget(startX, startY);
        SetHoverTarget(pressedTargetIndex);
        Win32Api.SetCapture(hwnd);
    }

    private void MoveSelection(int x, int y)
    {
        Win32Api.NativeRect previousMagnifier = GetCurrentMagnifierRect();
        int nextX = Clamp(x, 0, snapshot.Width);
        int nextY = Clamp(y, 0, snapshot.Height);
        pointerSeen = true;

        if (!mouseDown)
        {
            currentX = nextX;
            currentY = nextY;
            SetHoverTarget(HitTestWindowTarget(nextX, nextY));
            RequestMagnifierPaint(previousMagnifier);
            return;
        }

        if (nextX == currentX && nextY == currentY)
        {
            return;
        }

        if (!dragging && IsDragDistanceExceeded(startX, startY, nextX, nextY))
        {
            Win32Api.NativeRect previousHover = GetHoverRegion();
            hoverTargetIndex = -1;
            pressedTargetIndex = -1;
            dragging = true;
            hasSelection = true;
            currentX = nextX;
            currentY = nextY;
            RequestSelectionPaint(previousHover, GetSelectionRegion());
            RequestMagnifierPaint(previousMagnifier);
            return;
        }

        if (!dragging)
        {
            currentX = nextX;
            currentY = nextY;
            RequestMagnifierPaint(previousMagnifier);
            return;
        }

        Win32Api.NativeRect previous = GetSelectionRegion();
        currentX = nextX;
        currentY = nextY;
        RequestSelectionPaint(previous, GetSelectionRegion());
        RequestMagnifierPaint(previousMagnifier);
    }

    private void EndSelection(int x, int y)
    {
        if (!mouseDown)
        {
            return;
        }

        Win32Api.NativeRect previous = GetSelectionRegion();
        mouseDown = false;
        dragging = false;
        currentX = Clamp(x, 0, snapshot.Width);
        currentY = Clamp(y, 0, snapshot.Height);
        Win32Api.ReleaseCapture();
        if (hasSelection)
        {
            if (HasUsableSelection())
            {
                Accept();
            }
            else
            {
                Win32Api.NativeRect current = GetSelectionRegion();
                hasSelection = false;
                RequestSelectionPaint(previous, current);
            }
            return;
        }

        int releaseTargetIndex = HitTestWindowTarget(currentX, currentY);
        if (releaseTargetIndex >= 0 && pressedTargetIndex == releaseTargetIndex)
        {
            AcceptWindowTarget(releaseTargetIndex);
            return;
        }

        pressedTargetIndex = -1;
        SetHoverTarget(releaseTargetIndex);
    }

    private void KeyDown(int key)
    {
        if (key == Win32Api.VkEscape)
        {
            Cancel();
        }
        else if ((key == Win32Api.VkReturn || key == Win32Api.VkSpace) && hoverTargetIndex >= 0)
        {
            AcceptWindowTarget(hoverTargetIndex);
        }
        else if ((key == Win32Api.VkReturn || key == Win32Api.VkSpace) && HasUsableSelection())
        {
            Accept();
        }
    }

    private void Accept()
    {
        selectedRegion = GetSelectionRegion();
        accepted = true;
        CloseWindow();
    }

    private void AcceptWindowTarget(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= windowTargets.Length)
        {
            return;
        }

        selectedRegion = windowTargets[targetIndex].Region;
        accepted = true;
        CloseWindow();
    }

    private void Cancel()
    {
        accepted = false;
        selectedRegion = new Win32Api.NativeRect();
        CloseWindow();
    }

    private void CloseWindow()
    {
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.DestroyWindow(hwnd);
        }
        else
        {
            closed = true;
        }
    }

    private bool HasUsableSelection()
    {
        Win32Api.NativeRect region = GetSelectionRegion();
        return region.right - region.left >= MinSelectionSide && region.bottom - region.top >= MinSelectionSide;
    }

    private Win32Api.NativeRect GetSelectionRegion()
    {
        return NormalizeRegion(startX, startY, currentX, currentY, snapshot.Width, snapshot.Height);
    }

    private void Paint()
    {
        Win32Api.PaintStruct ps;
        IntPtr hdc = Win32Api.BeginPaint(hwnd, out ps);
        try
        {
            Win32Api.NativeRect dirty = ClampToSnapshot(ps.rcPaint);
            if (IsEmpty(dirty))
            {
                return;
            }

            IntPtr target = hdc;
            bool buffered = EnsureBackBufferResources(hdc, dirty.right - dirty.left, dirty.bottom - dirty.top);
            if (buffered)
            {
                target = backBufferDc;
            }
            int targetOriginX = buffered ? 0 : dirty.left;
            int targetOriginY = buffered ? 0 : dirty.top;

            DrawSnapshotRegion(target, dirty, targetOriginX, targetOriginY);
            DrawDimOverlay(target, targetOriginX, targetOriginY, dirty.right - dirty.left, dirty.bottom - dirty.top);
            Win32Api.NativeRect highlight = GetActiveHighlightRegion();
            if (!IsEmpty(highlight))
            {
                Win32Api.NativeRect selectedDirty;
                if (TryIntersect(dirty, highlight, out selectedDirty))
                {
                    int selectedX = buffered ? selectedDirty.left - dirty.left : selectedDirty.left;
                    int selectedY = buffered ? selectedDirty.top - dirty.top : selectedDirty.top;
                    DrawSnapshotRegion(target, selectedDirty, selectedX, selectedY);
                }
                DrawSelectionBorder(target, buffered ? Offset(highlight, -dirty.left, -dirty.top) : highlight);
            }
            DrawHud(target, dirty, buffered, highlight);
            DrawMagnifier(target, dirty, buffered);

            if (buffered)
            {
                Win32Api.BitBlt(hdc, dirty.left, dirty.top, dirty.right - dirty.left, dirty.bottom - dirty.top, backBufferDc, 0, 0, Win32Api.RasterCopy);
            }
        }
        finally
        {
            Win32Api.EndPaint(hwnd, ref ps);
        }
    }

    private void RequestSelectionPaint(Win32Api.NativeRect previous, Win32Api.NativeRect current)
    {
        Win32Api.NativeRect dirty = Union(Inflate(previous, DirtyPadding), Inflate(current, DirtyPadding));
        dirty = Union(dirty, Inflate(GetHudRectForRegion(previous), 4));
        dirty = Union(dirty, Inflate(GetHudRectForRegion(current), 4));
        dirty = Union(dirty, Inflate(GetInstructionHudRect(), 4));
        dirty = Union(dirty, Inflate(GetCurrentMagnifierRect(), 4));
        RequestPaint(dirty);
    }

    private void RequestMagnifierPaint(Win32Api.NativeRect previous)
    {
        Win32Api.NativeRect dirty = Union(Inflate(previous, 4), Inflate(GetCurrentMagnifierRect(), 4));
        RequestPaint(dirty);
    }

    private void SetHoverTarget(int targetIndex)
    {
        if (targetIndex == hoverTargetIndex)
        {
            return;
        }

        Win32Api.NativeRect previous = GetHoverRegion();
        hoverTargetIndex = targetIndex;
        RequestSelectionPaint(previous, GetHoverRegion());
    }

    private Win32Api.NativeRect GetActiveHighlightRegion()
    {
        if (hasSelection)
        {
            return GetSelectionRegion();
        }
        return GetHoverRegion();
    }

    private Win32Api.NativeRect GetHoverRegion()
    {
        if (hoverTargetIndex < 0 || hoverTargetIndex >= windowTargets.Length)
        {
            return new Win32Api.NativeRect();
        }
        return windowTargets[hoverTargetIndex].Region;
    }

    private void RequestPaint(Win32Api.NativeRect region)
    {
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.NativeRect clipped = ClampToSnapshot(region);
            if (!IsEmpty(clipped))
            {
                Win32Api.InvalidateRectRef(hwnd, ref clipped, false);
            }
        }
    }

    private void DrawSnapshotRegion(IntPtr hdc, Win32Api.NativeRect region, int targetX, int targetY)
    {
        int width = region.right - region.left;
        int height = region.bottom - region.top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (region.left == 0 && region.top == 0 && width == snapshot.Width && height == snapshot.Height)
        {
            ScreenCapture.DrawPixels(hdc, targetX, targetY, snapshot.Width, snapshot.Height, snapshot.Pixels);
            return;
        }

        int length = checked(width * height * 4);
        EnsurePaintBuffer(length);
        CopySnapshotRegion(region, paintBuffer);
        ScreenCapture.DrawPixels(hdc, targetX, targetY, width, height, paintBuffer);
    }

    private void DrawDimOverlay(IntPtr hdc, int targetX, int targetY, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        EnsureDimResources(hdc);
        Win32Api.BlendFunction blend = new Win32Api.BlendFunction();
        blend.blendOp = Win32Api.AcSrcOver;
        blend.sourceConstantAlpha = DimOverlayAlpha;
        Win32Api.AlphaBlend(hdc, targetX, targetY, width, height, dimDc, 0, 0, 1, 1, blend);
    }

    private void EnsurePaintBuffer(int length)
    {
        if (paintBuffer == null || paintBuffer.Length < length)
        {
            paintBuffer = new byte[length];
        }
    }

    private void CopySnapshotRegion(Win32Api.NativeRect region, byte[] target)
    {
        int width = region.right - region.left;
        int height = region.bottom - region.top;
        int targetStride = width * 4;
        for (int y = 0; y < height; y++)
        {
            int sourceOffset = ((region.top + y) * snapshot.Width + region.left) * 4;
            int targetOffset = y * targetStride;
            Buffer.BlockCopy(snapshot.Pixels, sourceOffset, target, targetOffset, targetStride);
        }
    }

    private bool EnsureBackBufferResources(IntPtr hdc, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (backBufferDc != IntPtr.Zero && backBufferWidth >= width && backBufferHeight >= height)
        {
            return true;
        }

        ReleaseBackBufferResources();
        backBufferDc = Win32Api.CreateCompatibleDC(hdc);
        if (backBufferDc == IntPtr.Zero)
        {
            return false;
        }

        backBufferSurface = Win32Api.CreateCompatibleSurface(hdc, width, height);
        if (backBufferSurface == IntPtr.Zero)
        {
            ReleaseBackBufferResources();
            return false;
        }

        oldBackBufferSurface = Win32Api.SelectObject(backBufferDc, backBufferSurface);
        backBufferWidth = width;
        backBufferHeight = height;
        return true;
    }

    private void ReleaseBackBufferResources()
    {
        if (backBufferDc != IntPtr.Zero && oldBackBufferSurface != IntPtr.Zero)
        {
            Win32Api.SelectObject(backBufferDc, oldBackBufferSurface);
        }
        oldBackBufferSurface = IntPtr.Zero;
        if (backBufferSurface != IntPtr.Zero)
        {
            Win32Api.DeleteObject(backBufferSurface);
            backBufferSurface = IntPtr.Zero;
        }
        if (backBufferDc != IntPtr.Zero)
        {
            Win32Api.DeleteDC(backBufferDc);
            backBufferDc = IntPtr.Zero;
        }
        backBufferWidth = 0;
        backBufferHeight = 0;
    }

    private void EnsureDimResources(IntPtr hdc)
    {
        if (dimDc != IntPtr.Zero)
        {
            return;
        }

        dimDc = Win32Api.CreateCompatibleDC(hdc);
        if (dimDc == IntPtr.Zero)
        {
            return;
        }

        dimSurface = Win32Api.CreateCompatibleSurface(hdc, 1, 1);
        if (dimSurface == IntPtr.Zero)
        {
            ReleaseDimResources();
            return;
        }

        oldDimSurface = Win32Api.SelectObject(dimDc, dimSurface);
        IntPtr brush = Win32Api.CreateSolidBrush(0);
        try
        {
            Win32Api.NativeRect pixel = new Win32Api.NativeRect();
            pixel.right = 1;
            pixel.bottom = 1;
            Win32Api.FillRect(dimDc, ref pixel, brush);
        }
        finally
        {
            if (brush != IntPtr.Zero)
            {
                Win32Api.DeleteObject(brush);
            }
        }
    }

    private void ReleaseDimResources()
    {
        if (dimDc != IntPtr.Zero && oldDimSurface != IntPtr.Zero)
        {
            Win32Api.SelectObject(dimDc, oldDimSurface);
        }
        oldDimSurface = IntPtr.Zero;
        if (dimSurface != IntPtr.Zero)
        {
            Win32Api.DeleteObject(dimSurface);
            dimSurface = IntPtr.Zero;
        }
        if (dimDc != IntPtr.Zero)
        {
            Win32Api.DeleteDC(dimDc);
            dimDc = IntPtr.Zero;
        }
    }

    private int HitTestWindowTarget(int x, int y)
    {
        for (int i = 0; i < windowTargets.Length; i++)
        {
            if (Contains(windowTargets[i].Region, x, y))
            {
                return i;
            }
        }
        return -1;
    }

    private static ScreenSelectionTarget[] CaptureWindowTargets(ScreenCaptureSnapshot snapshot)
    {
        List<ScreenSelectionTarget> targets = new List<ScreenSelectionTarget>();
        IntPtr shell = Win32Api.GetShellWindow();
        IntPtr desktop = Win32Api.GetDesktopWindow();
        Win32Api.EnumWindowsProc callback = delegate(IntPtr window, IntPtr parameter)
        {
            if (window == IntPtr.Zero || window == shell || window == desktop)
            {
                return true;
            }
            if (!Win32Api.IsWindowVisible(window) || Win32Api.IsIconic(window))
            {
                return true;
            }
            if (IsWindowCloaked(window))
            {
                return true;
            }

            string className = Win32Api.GetClassNameUnicode(window);
            if (IsIgnoredWindowClass(className))
            {
                return true;
            }

            Win32Api.NativeRect rect;
            if (!TryGetWindowTargetRect(window, out rect))
            {
                return true;
            }

            Win32Api.NativeRect region;
            if (TryConvertWindowRect(rect, snapshot.Left, snapshot.Top, snapshot.Width, snapshot.Height, out region))
            {
                targets.Add(new ScreenSelectionTarget(region));
            }
            return true;
        };
        Win32Api.EnumWindows(callback, IntPtr.Zero);
        return targets.ToArray();
    }

    private static bool IsIgnoredWindowClass(string className)
    {
        return string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetWindowTargetRect(IntPtr window, out Win32Api.NativeRect rect)
    {
        rect = new Win32Api.NativeRect();
        int size = Marshal.SizeOf(typeof(Win32Api.NativeRect));
        if (Win32Api.DwmGetWindowRectAttribute(window, Win32Api.DwmwaExtendedFrameBounds, out rect, size) == 0 && !IsEmpty(rect))
        {
            return true;
        }
        return Win32Api.GetWindowRect(window, out rect) && !IsEmpty(rect);
    }

    private static bool IsWindowCloaked(IntPtr window)
    {
        int cloaked;
        int size = Marshal.SizeOf(typeof(int));
        return Win32Api.DwmGetWindowIntAttribute(window, Win32Api.DwmwaCloaked, out cloaked, size) == 0 && cloaked != 0;
    }

    private static void DrawSelectionBorder(IntPtr hdc, Win32Api.NativeRect region)
    {
        if (region.right <= region.left || region.bottom <= region.top)
        {
            return;
        }

        IntPtr oldPen = IntPtr.Zero;
        IntPtr oldBrush = IntPtr.Zero;
        IntPtr outerPen = IntPtr.Zero;
        IntPtr whitePen = IntPtr.Zero;
        IntPtr accentPen = IntPtr.Zero;
        try
        {
            outerPen = Win32Api.CreatePen(Win32Api.PenStyleSolid, 6, ToColorRef(0, 0, 0));
            whitePen = Win32Api.CreatePen(Win32Api.PenStyleSolid, 4, ToColorRef(255, 255, 255));
            accentPen = Win32Api.CreatePen(Win32Api.PenStyleSolid, 2, ToColorRef(AppStyles.Accent.R, AppStyles.Accent.G, AppStyles.Accent.B));
            oldPen = Win32Api.SelectObject(hdc, outerPen);
            oldBrush = Win32Api.SelectObject(hdc, Win32Api.GetStockObject(Win32Api.StockNullBrush));
            Win32Api.Rectangle(hdc, region.left, region.top, region.right, region.bottom);
            Win32Api.SelectObject(hdc, whitePen);
            Win32Api.Rectangle(hdc, region.left, region.top, region.right, region.bottom);
            Win32Api.SelectObject(hdc, accentPen);
            Win32Api.Rectangle(hdc, region.left, region.top, region.right, region.bottom);
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
            if (outerPen != IntPtr.Zero)
            {
                Win32Api.DeleteObject(outerPen);
            }
            if (whitePen != IntPtr.Zero)
            {
                Win32Api.DeleteObject(whitePen);
            }
            if (accentPen != IntPtr.Zero)
            {
                Win32Api.DeleteObject(accentPen);
            }
        }
    }

    private void DrawHud(IntPtr hdc, Win32Api.NativeRect dirty, bool buffered, Win32Api.NativeRect highlight)
    {
        string text = GetHudText(highlight);
        Win32Api.NativeRect hud = GetHudRectForRegion(highlight);
        Win32Api.NativeRect intersection;
        if (!TryIntersect(dirty, hud, out intersection))
        {
            return;
        }

        Win32Api.NativeRect target = buffered ? Offset(hud, -dirty.left, -dirty.top) : hud;
        IntPtr brush = Win32Api.CreateSolidBrush(ToColorRef(18, 22, 30));
        try
        {
            Win32Api.FillRect(hdc, ref target, brush);
            Win32Api.SetBkMode(hdc, Win32Api.TransparentBkMode);
            Win32Api.SetTextColor(hdc, ToColorRef(255, 255, 255));
            Win32Api.NativeRect textRect = target;
            textRect.left += HudPadding;
            textRect.right -= HudPadding;
            Win32Api.DrawText(hdc, text, -1, ref textRect, Win32Api.DtVCenter | Win32Api.DtSingleLine | Win32Api.DtLeft);
        }
        finally
        {
            if (brush != IntPtr.Zero)
            {
                Win32Api.DeleteObject(brush);
            }
        }
    }

    private void DrawMagnifier(IntPtr hdc, Win32Api.NativeRect dirty, bool buffered)
    {
        if (!pointerSeen)
        {
            return;
        }

        Win32Api.NativeRect frame = GetCurrentMagnifierRect();
        Win32Api.NativeRect intersection;
        if (!TryIntersect(dirty, frame, out intersection))
        {
            return;
        }

        Win32Api.NativeRect target = buffered ? Offset(frame, -dirty.left, -dirty.top) : frame;
        ModernUiPainter.FillRoundRect(hdc, target, 12, ToColorRef(15, 23, 42), ToColorRef(255, 255, 255), 1);
        EnsureMagnifierBuffer();
        FillMagnifierBuffer(currentX, currentY);
        int imageLeft = target.left + MagnifierInset;
        int imageTop = target.top + MagnifierInset;
        ScreenCapture.DrawPixels(hdc, imageLeft, imageTop, MagnifierImageSize, MagnifierImageSize, magnifierBuffer);
        DrawMagnifierCrosshair(hdc, imageLeft, imageTop);
    }

    private void EnsureMagnifierBuffer()
    {
        int length = MagnifierImageSize * MagnifierImageSize * 4;
        if (magnifierBuffer == null || magnifierBuffer.Length != length)
        {
            magnifierBuffer = new byte[length];
        }
    }

    private void FillMagnifierBuffer(int centerX, int centerY)
    {
        int half = MagnifierImageSize / 2;
        for (int y = 0; y < MagnifierImageSize; y++)
        {
            int sourceY = Clamp(centerY + (y - half) / MagnifierScale, 0, snapshot.Height - 1);
            for (int x = 0; x < MagnifierImageSize; x++)
            {
                int sourceX = Clamp(centerX + (x - half) / MagnifierScale, 0, snapshot.Width - 1);
                int sourceOffset = (sourceY * snapshot.Width + sourceX) * 4;
                int targetOffset = (y * MagnifierImageSize + x) * 4;
                magnifierBuffer[targetOffset] = snapshot.Pixels[sourceOffset];
                magnifierBuffer[targetOffset + 1] = snapshot.Pixels[sourceOffset + 1];
                magnifierBuffer[targetOffset + 2] = snapshot.Pixels[sourceOffset + 2];
                magnifierBuffer[targetOffset + 3] = 255;
            }
        }
    }

    private static void DrawMagnifierCrosshair(IntPtr hdc, int imageLeft, int imageTop)
    {
        int centerX = imageLeft + MagnifierImageSize / 2;
        int centerY = imageTop + MagnifierImageSize / 2;
        ModernUiPainter.FillRect(hdc, ModernUiPainter.Rect(centerX - 14, centerY, centerX + 15, centerY + 1), ToColorRef(255, 255, 255));
        ModernUiPainter.FillRect(hdc, ModernUiPainter.Rect(centerX, centerY - 14, centerX + 1, centerY + 15), ToColorRef(255, 255, 255));
        ModernUiPainter.FillRect(hdc, ModernUiPainter.Rect(centerX - 8, centerY, centerX + 9, centerY + 1), ToColorRef(37, 99, 235));
        ModernUiPainter.FillRect(hdc, ModernUiPainter.Rect(centerX, centerY - 8, centerX + 1, centerY + 9), ToColorRef(37, 99, 235));
    }

    private Win32Api.NativeRect GetCurrentMagnifierRect()
    {
        if (!pointerSeen)
        {
            return new Win32Api.NativeRect();
        }
        return GetMagnifierRect(currentX, currentY, snapshot.Width, snapshot.Height);
    }

    private string GetHudText(Win32Api.NativeRect highlight)
    {
        if (hasSelection)
        {
            return GetRegionSizeText(highlight);
        }
        if (hoverTargetIndex >= 0)
        {
            return "\u5355\u51fb\u9009\u62e9\u7a97\u53e3  " + GetRegionSizeText(highlight);
        }
        return "\u62d6\u52a8\u6846\u9009 \u00b7 \u5355\u51fb\u7a97\u53e3 \u00b7 Enter \u786e\u8ba4 \u00b7 Esc \u53d6\u6d88";
    }

    private Win32Api.NativeRect GetHudRectForRegion(Win32Api.NativeRect region)
    {
        if (IsEmpty(region))
        {
            return GetInstructionHudRect();
        }

        int textWidth = 320;
        int x = Clamp(region.left, 8, Math.Max(8, snapshot.Width - textWidth - 8));
        int y = region.top - HudHeight - 8;
        if (y < 8)
        {
            y = Math.Min(snapshot.Height - HudHeight - 8, region.bottom + 8);
        }

        Win32Api.NativeRect rect = new Win32Api.NativeRect();
        rect.left = x;
        rect.top = Math.Max(8, y);
        rect.right = Math.Min(snapshot.Width - 8, rect.left + textWidth);
        rect.bottom = rect.top + HudHeight;
        return rect;
    }

    private Win32Api.NativeRect GetInstructionHudRect()
    {
        int width = Math.Min(560, Math.Max(320, snapshot.Width - 32));
        Win32Api.NativeRect rect = new Win32Api.NativeRect();
        rect.left = Math.Max(8, (snapshot.Width - width) / 2);
        rect.top = 18;
        rect.right = rect.left + width;
        rect.bottom = rect.top + HudHeight;
        return rect;
    }

    private static Win32Api.NativeRect GetMagnifierRect(int x, int y, int width, int height)
    {
        int margin = 16;
        int left = x + margin;
        int top = y + margin;
        if (left + MagnifierFrameSize > width - margin)
        {
            left = x - MagnifierFrameSize - margin;
        }
        if (top + MagnifierFrameSize > height - margin)
        {
            top = y - MagnifierFrameSize - margin;
        }
        left = Clamp(left, margin, Math.Max(margin, width - MagnifierFrameSize - margin));
        top = Clamp(top, margin, Math.Max(margin, height - MagnifierFrameSize - margin));

        Win32Api.NativeRect rect = new Win32Api.NativeRect();
        rect.left = left;
        rect.top = top;
        rect.right = left + MagnifierFrameSize;
        rect.bottom = top + MagnifierFrameSize;
        return rect;
    }

    private static string GetRegionSizeText(Win32Api.NativeRect region)
    {
        int width = Math.Max(0, region.right - region.left);
        int height = Math.Max(0, region.bottom - region.top);
        return width.ToString(System.Globalization.CultureInfo.InvariantCulture) + " x " + height.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private Win32Api.NativeRect ClampToSnapshot(Win32Api.NativeRect rect)
    {
        Win32Api.NativeRect result = new Win32Api.NativeRect();
        result.left = Clamp(Math.Min(rect.left, rect.right), 0, snapshot.Width);
        result.top = Clamp(Math.Min(rect.top, rect.bottom), 0, snapshot.Height);
        result.right = Clamp(Math.Max(rect.left, rect.right), 0, snapshot.Width);
        result.bottom = Clamp(Math.Max(rect.top, rect.bottom), 0, snapshot.Height);
        return result;
    }

    private static Win32Api.NativeRect Inflate(Win32Api.NativeRect rect, int amount)
    {
        Win32Api.NativeRect result = new Win32Api.NativeRect();
        result.left = rect.left - amount;
        result.top = rect.top - amount;
        result.right = rect.right + amount;
        result.bottom = rect.bottom + amount;
        return result;
    }

    private static Win32Api.NativeRect Union(Win32Api.NativeRect a, Win32Api.NativeRect b)
    {
        if (IsEmpty(a))
        {
            return b;
        }
        if (IsEmpty(b))
        {
            return a;
        }

        Win32Api.NativeRect result = new Win32Api.NativeRect();
        result.left = Math.Min(a.left, b.left);
        result.top = Math.Min(a.top, b.top);
        result.right = Math.Max(a.right, b.right);
        result.bottom = Math.Max(a.bottom, b.bottom);
        return result;
    }

    private static bool TryIntersect(Win32Api.NativeRect a, Win32Api.NativeRect b, out Win32Api.NativeRect result)
    {
        result = new Win32Api.NativeRect();
        result.left = Math.Max(a.left, b.left);
        result.top = Math.Max(a.top, b.top);
        result.right = Math.Min(a.right, b.right);
        result.bottom = Math.Min(a.bottom, b.bottom);
        return !IsEmpty(result);
    }

    private static Win32Api.NativeRect Offset(Win32Api.NativeRect rect, int dx, int dy)
    {
        Win32Api.NativeRect result = new Win32Api.NativeRect();
        result.left = rect.left + dx;
        result.top = rect.top + dy;
        result.right = rect.right + dx;
        result.bottom = rect.bottom + dy;
        return result;
    }

    private static bool IsEmpty(Win32Api.NativeRect rect)
    {
        return rect.right <= rect.left || rect.bottom <= rect.top;
    }

    private static bool Contains(Win32Api.NativeRect rect, int x, int y)
    {
        return x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;
    }

    private static bool IsDragDistanceExceeded(int x1, int y1, int x2, int y2)
    {
        return Math.Abs(x2 - x1) >= DragThreshold || Math.Abs(y2 - y1) >= DragThreshold;
    }

    private static bool TryConvertWindowRect(Win32Api.NativeRect windowRect, int snapshotLeft, int snapshotTop, int snapshotWidth, int snapshotHeight, out Win32Api.NativeRect region)
    {
        Win32Api.NativeRect relative = new Win32Api.NativeRect();
        relative.left = windowRect.left - snapshotLeft;
        relative.top = windowRect.top - snapshotTop;
        relative.right = windowRect.right - snapshotLeft;
        relative.bottom = windowRect.bottom - snapshotTop;

        region = new Win32Api.NativeRect();
        region.left = Clamp(Math.Min(relative.left, relative.right), 0, snapshotWidth);
        region.top = Clamp(Math.Min(relative.top, relative.bottom), 0, snapshotHeight);
        region.right = Clamp(Math.Max(relative.left, relative.right), 0, snapshotWidth);
        region.bottom = Clamp(Math.Max(relative.top, relative.bottom), 0, snapshotHeight);
        return region.right - region.left >= MinSelectionSide && region.bottom - region.top >= MinSelectionSide;
    }

    private static Win32Api.NativeRect NormalizeRegion(int x1, int y1, int x2, int y2, int width, int height)
    {
        Win32Api.NativeRect rect = new Win32Api.NativeRect();
        rect.left = Clamp(Math.Min(x1, x2), 0, width);
        rect.top = Clamp(Math.Min(y1, y2), 0, height);
        rect.right = Clamp(Math.Max(x1, x2), 0, width);
        rect.bottom = Clamp(Math.Max(y1, y2), 0, height);
        return rect;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }

    private static uint ToColorRef(int r, int g, int b)
    {
        return (uint)((Clamp(r, 0, 255)) | (Clamp(g, 0, 255) << 8) | (Clamp(b, 0, 255) << 16));
    }

    private struct ScreenSelectionTarget
    {
        public readonly Win32Api.NativeRect Region;

        public ScreenSelectionTarget(Win32Api.NativeRect region)
        {
            Region = region;
        }
    }
}
