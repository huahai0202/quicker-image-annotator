using System;
using System.Runtime.InteropServices;
using System.Threading;

internal sealed class OcrResultWindow
{
    private const string WindowClassName = "QuickerImageAnnotatorOcrResultWindow";
    private const int Width = 760;
    private const int Height = 640;
    private const int IdResultEdit = 201;
    private const int WmTranslateComplete = Win32Api.WmApp + 51;
    private const string FontFace = "Microsoft YaHei UI";
    private static Win32Api.WindowProc sharedProc;
    private static OcrResultWindow current;

    private readonly IntPtr owner;
    private readonly OcrResult result;
    private OcrTextLayout layout;
    private IntPtr hwnd;
    private IntPtr resultEdit;
    private IntPtr titleFont;
    private IntPtr bodyFont;
    private IntPtr smallFont;
    private IntPtr buttonFont;
    private bool closed;
    private string copyStatus;
    private string translatedText;
    private bool showingTranslation;
    private bool translationRunning;
    private int translationVersion;

    private OcrResultWindow(IntPtr owner, OcrResult result, OcrTextLayout layout)
    {
        this.owner = owner;
        this.result = result;
        this.layout = OcrTextFormatter.NormalizeLayout(layout);
    }

    public static void Show(IntPtr owner, OcrResult result, OcrTextLayout layout)
    {
        new OcrResultWindow(owner, result, layout).RunInternal();
    }

    private void RunInternal()
    {
        EnsureWindowClass();
        int x = Math.Max(0, (Win32Api.GetSystemMetrics(Win32Api.SmCxScreen) - Width) / 2);
        int y = Math.Max(0, (Win32Api.GetSystemMetrics(Win32Api.SmCyScreen) - Height) / 2);
        hwnd = Win32Api.CreateWindowEx(
            0,
            WindowClassName,
            UiText.OcrResultTitle,
            Win32Api.WsOverlappedWindow,
            x,
            y,
            Width,
            Height,
            owner,
            IntPtr.Zero,
            Win32Api.GetModuleHandle(null),
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowEx failed for OCR result window.");
        }

        current = this;
        CreateFonts();
        CreateControls();
        UpdateResultText();
        Win32Api.ShowWindow(hwnd, Win32Api.SwShow);
        Win32Api.SetForegroundWindow(hwnd);
        Win32Api.UpdateWindow(hwnd);

        Win32Api.Msg msg;
        while (!closed && Win32Api.GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            Win32Api.TranslateMessage(ref msg);
            Win32Api.DispatchMessage(ref msg);
        }
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
        wc.lpfnWndProc = sharedProc;
        wc.hInstance = Win32Api.GetModuleHandle(null);
        wc.hIcon = Win32Api.LoadIcon(wc.hInstance, new IntPtr(Win32Api.IdiApplication));
        wc.hIconSm = wc.hIcon;
        wc.hCursor = Win32Api.LoadCursor(IntPtr.Zero, new IntPtr(Win32Api.IdcArrow));
        wc.hbrBackground = Win32Api.GetStockObject(Win32Api.StockWhiteBrush);
        wc.lpszClassName = WindowClassName;
        ushort atom = Win32Api.RegisterClassEx(ref wc);
        if (atom == 0)
        {
            throw new InvalidOperationException("RegisterClassEx failed for OCR result window.");
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        OcrResultWindow window = current;
        if (window != null && window.hwnd == hwnd)
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
            case Win32Api.WmLButtonDown:
                HandleClick(Win32Api.GetX(lParam), Win32Api.GetY(lParam));
                return IntPtr.Zero;
            case Win32Api.WmKeyDown:
                if (wParam.ToInt32() == Win32Api.VkEscape)
                {
                    Close();
                    return IntPtr.Zero;
                }
                break;
            case WmTranslateComplete:
                CompleteTranslation(wParam);
                return IntPtr.Zero;
            case Win32Api.WmClose:
                Close();
                return IntPtr.Zero;
            case Win32Api.WmDestroy:
                DestroyFonts();
                closed = true;
                hwnd = IntPtr.Zero;
                if (current == this)
                {
                    current = null;
                }
                return IntPtr.Zero;
        }
        return Win32Api.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void CreateControls()
    {
        Win32Api.NativeRect rect = ResultField();
        resultEdit = Win32Api.CreateWindowEx(
            0,
            "EDIT",
            string.Empty,
            Win32Api.WsChild | Win32Api.WsVisible | Win32Api.WsTabStop | Win32Api.WsVScroll | Win32Api.EsMultiline | Win32Api.EsReadonly | Win32Api.EsWantReturn,
            rect.left + 14,
            rect.top + 12,
            rect.right - rect.left - 28,
            rect.bottom - rect.top - 24,
            hwnd,
            new IntPtr(IdResultEdit),
            Win32Api.GetModuleHandle(null),
            IntPtr.Zero);
        ApplyControlFont(resultEdit);
    }

    private void CreateFonts()
    {
        int dpi = 96;
        IntPtr hdc = Win32Api.GetDC(hwnd);
        if (hdc != IntPtr.Zero)
        {
            try
            {
                dpi = Math.Max(72, Win32Api.GetDeviceCaps(hdc, Win32Api.LogPixelsY));
            }
            finally
            {
                Win32Api.ReleaseDC(hwnd, hdc);
            }
        }

        titleFont = CreateUiFont(18, dpi, Win32Api.FontWeightSemiBold);
        bodyFont = CreateUiFont(10, dpi, Win32Api.FontWeightRegular);
        smallFont = CreateUiFont(9, dpi, Win32Api.FontWeightRegular);
        buttonFont = CreateUiFont(10, dpi, Win32Api.FontWeightSemiBold);
    }

    private static IntPtr CreateUiFont(int points, int dpi, int weight)
    {
        return Win32Api.CreateFont(-Win32Api.MulDiv(points, dpi, 72), 0, 0, 0, weight, 0, 0, 0, Win32Api.DefaultCharSet, Win32Api.OutDefaultPrecision, Win32Api.ClipDefaultPrecision, Win32Api.ClearTypeQuality, Win32Api.DefaultPitch, FontFace);
    }

    private void ApplyControlFont(IntPtr control)
    {
        if (control != IntPtr.Zero && bodyFont != IntPtr.Zero)
        {
            Win32Api.SendMessage(control, Win32Api.WmSetFont, bodyFont, new IntPtr(1));
        }
    }

    private void DestroyFonts()
    {
        DeleteFont(ref titleFont);
        DeleteFont(ref bodyFont);
        DeleteFont(ref smallFont);
        DeleteFont(ref buttonFont);
    }

    private static void DeleteFont(ref IntPtr font)
    {
        if (font != IntPtr.Zero)
        {
            Win32Api.DeleteObject(font);
            font = IntPtr.Zero;
        }
    }

    private void HandleClick(int x, int y)
    {
        if (Contains(LayoutLinesButton(), x, y))
        {
            ChangeLayout(OcrTextLayout.Lines);
        }
        else if (Contains(LayoutParagraphButton(), x, y))
        {
            ChangeLayout(OcrTextLayout.SmartParagraph);
        }
        else if (Contains(TranslateButton(), x, y))
        {
            TranslateCurrentText();
        }
        else if (Contains(CopyButton(), x, y))
        {
            ClipboardBridge.SetUnicodeText(hwnd, GetDisplayedText());
            copyStatus = UiText.OcrCopied;
            RequestPaint();
        }
        else if (Contains(CloseButton(), x, y))
        {
            Close();
        }
    }

    private void ChangeLayout(OcrTextLayout value)
    {
        layout = OcrTextFormatter.NormalizeLayout(value);
        translatedText = null;
        showingTranslation = false;
        translationRunning = false;
        translationVersion++;
        copyStatus = null;
        UpdateResultText();
        SaveLayoutPreference();
        RequestPaint();
    }

    private void SaveLayoutPreference()
    {
        AppSettings settings = AppSettingsStore.Load();
        settings.OcrLayout = layout;
        AppSettingsStore.Save(settings);
    }

    private void UpdateResultText()
    {
        Win32Api.SetWindowTextUnicode(resultEdit, GetDisplayedText());
    }

    private string GetDisplayedText()
    {
        if (showingTranslation && !string.IsNullOrWhiteSpace(translatedText))
        {
            return translatedText;
        }
        return GetFormattedText();
    }

    private string GetFormattedText()
    {
        string text = OcrTextFormatter.Format(result == null ? null : result.Lines, layout);
        return string.IsNullOrWhiteSpace(text) ? UiText.OcrNoText : text;
    }

    private void TranslateCurrentText()
    {
        if (translationRunning)
        {
            return;
        }

        string source = OcrTextFormatter.Format(result == null ? null : result.Lines, layout);
        if (string.IsNullOrWhiteSpace(source))
        {
            Win32Api.MessageBoxUnicode(hwnd, UiText.OcrNoText, UiText.OcrResultTitle, Win32Api.MbOk);
            return;
        }

        int version = ++translationVersion;
        translationRunning = true;
        copyStatus = UiText.OcrTranslating;
        RequestPaint();
        Win32Api.UpdateWindow(hwnd);
        IntPtr target = hwnd;
        ThreadPool.QueueUserWorkItem(delegate
        {
            AsyncTextResult async = new AsyncTextResult();
            async.Version = version;
            try
            {
                async.Text = GoogleTranslateClient.TranslateToChinese(source);
            }
            catch (Exception ex)
            {
                async.Error = ex;
            }
            PostAsyncResult(target, async);
        });
    }

    private void CompleteTranslation(IntPtr handle)
    {
        AsyncTextResult async = TakeAsyncResult(handle);
        if (async == null || async.Version != translationVersion)
        {
            return;
        }

        translationRunning = false;
        if (async.Error != null)
        {
            AppLog.Error("Google translate failed.", async.Error);
            copyStatus = null;
            Win32Api.MessageBoxUnicode(hwnd, UiText.OcrTranslateFailed + Environment.NewLine + async.Error.Message, UiText.OcrResultTitle, Win32Api.MbOk | Win32Api.MbIconError);
            RequestPaint();
            return;
        }

        translatedText = async.Text;
        showingTranslation = true;
        copyStatus = UiText.OcrTranslated;
        UpdateResultText();
        RequestPaint();
    }

    private static void PostAsyncResult(IntPtr target, AsyncTextResult async)
    {
        GCHandle handle = GCHandle.Alloc(async);
        IntPtr pointer = GCHandle.ToIntPtr(handle);
        if (target == IntPtr.Zero || !Win32Api.PostMessage(target, WmTranslateComplete, pointer, IntPtr.Zero))
        {
            handle.Free();
        }
    }

    private static AsyncTextResult TakeAsyncResult(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return null;
        }
        GCHandle handle = GCHandle.FromIntPtr(pointer);
        try
        {
            return handle.Target as AsyncTextResult;
        }
        finally
        {
            handle.Free();
        }
    }

    private void Paint()
    {
        Win32Api.PaintStruct ps;
        IntPtr hdc = Win32Api.BeginPaint(hwnd, out ps);
        try
        {
            Win32Api.NativeRect client;
            Win32Api.GetClientRect(hwnd, out client);
            FillRect(hdc, client, Color(242, 246, 250));
            FillRoundRect(hdc, Card(), 18, Color(255, 255, 255), Color(224, 231, 241), 1);
            DrawText(hdc, UiText.OcrResultTitle, titleFont, 38, 34, 220, 36, Color(15, 23, 42));
            DrawText(hdc, BuildSubtitle(), smallFont, 38, 70, 520, 22, Color(100, 116, 139));
            DrawSegment(hdc, LayoutLinesButton(), UiText.OcrLines, layout == OcrTextLayout.Lines);
            DrawSegment(hdc, LayoutParagraphButton(), UiText.OcrSmartParagraph, layout == OcrTextLayout.SmartParagraph);
            DrawField(hdc, ResultField());
            if (!string.IsNullOrEmpty(copyStatus))
            {
                DrawText(hdc, copyStatus, smallFont, 38, 560, 300, 24, Color(13, 148, 136));
            }
            DrawButton(hdc, TranslateButton(), UiText.OcrTranslate, translationRunning ? Color(148, 163, 184) : Color(37, 99, 235), translationRunning ? Color(100, 116, 139) : Color(37, 99, 235), false);
            DrawButton(hdc, CopyButton(), UiText.OcrCopy, Color(37, 99, 235), Color(37, 99, 235), false);
            DrawButton(hdc, CloseButton(), UiText.Close, Color(13, 148, 136), Color(255, 255, 255), true);
        }
        finally
        {
            Win32Api.EndPaint(hwnd, ref ps);
        }
    }

    private string BuildSubtitle()
    {
        string engine = result != null && result.Engine == OcrEngineKind.Accurate ? UiText.OcrAccurate : UiText.OcrStandard;
        int count = result == null || result.Lines == null ? 0 : result.Lines.Count;
        string scope = result == null ? string.Empty : result.SourceScope;
        return engine + " · " + scope + " · " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " \u884c";
    }

    private static void DrawField(IntPtr hdc, Win32Api.NativeRect rect)
    {
        FillRoundRect(hdc, rect, 10, Color(255, 255, 255), Color(216, 226, 236), 1);
    }

    private void DrawSegment(IntPtr hdc, Win32Api.NativeRect rect, string text, bool selected)
    {
        uint border = selected ? Color(37, 99, 235) : Color(203, 213, 225);
        uint fill = selected ? Color(239, 246, 255) : Color(255, 255, 255);
        uint textColor = selected ? Color(29, 78, 216) : Color(71, 85, 105);
        FillRoundRect(hdc, rect, 10, fill, border, 1);
        DrawText(hdc, text, buttonFont, rect.left, rect.top + 1, rect.right - rect.left, rect.bottom - rect.top, textColor, Win32Api.DtCenter | Win32Api.DtVCenter | Win32Api.DtSingleLine | Win32Api.DtEndEllipsis);
    }

    private void DrawButton(IntPtr hdc, Win32Api.NativeRect rect, string text, uint color, uint textColor, bool primary)
    {
        uint fill = primary ? color : Color(255, 255, 255);
        FillRoundRect(hdc, rect, 11, fill, color, primary ? 0 : 1);
        DrawText(hdc, text, buttonFont, rect.left, rect.top + 1, rect.right - rect.left, rect.bottom - rect.top, textColor, Win32Api.DtCenter | Win32Api.DtVCenter | Win32Api.DtSingleLine);
    }

    private static void FillRect(IntPtr hdc, Win32Api.NativeRect rect, uint color)
    {
        ModernUiPainter.FillRect(hdc, rect, color);
    }

    private static void FillRoundRect(IntPtr hdc, Win32Api.NativeRect rect, int radius, uint fill, uint border, int borderWidth)
    {
        ModernUiPainter.FillRoundRect(hdc, rect, radius, fill, border, borderWidth);
    }

    private static void DrawText(IntPtr hdc, string text, IntPtr font, int x, int y, int width, int height, uint color)
    {
        DrawText(hdc, text, font, x, y, width, height, color, Win32Api.DtLeft | Win32Api.DtVCenter | Win32Api.DtSingleLine | Win32Api.DtEndEllipsis);
    }

    private static void DrawText(IntPtr hdc, string text, IntPtr font, int x, int y, int width, int height, uint color, uint format)
    {
        ModernUiPainter.DrawText(hdc, text, font, x, y, width, height, color, format);
    }

    private void RequestPaint()
    {
        if (hwnd != IntPtr.Zero)
        {
            Win32Api.InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }

    private void Close()
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

    private static Win32Api.NativeRect Card()
    {
        return Rect(18, 18, 724, 598);
    }

    private static Win32Api.NativeRect ResultField()
    {
        return Rect(38, 116, 704, 548);
    }

    private static Win32Api.NativeRect LayoutLinesButton()
    {
        return Rect(480, 42, 556, 78);
    }

    private static Win32Api.NativeRect LayoutParagraphButton()
    {
        return Rect(568, 42, 704, 78);
    }

    private static Win32Api.NativeRect TranslateButton()
    {
        return Rect(408, 558, 512, 594);
    }

    private static Win32Api.NativeRect CopyButton()
    {
        return Rect(528, 558, 616, 594);
    }

    private static Win32Api.NativeRect CloseButton()
    {
        return Rect(632, 558, 704, 594);
    }

    private static Win32Api.NativeRect Rect(int left, int top, int right, int bottom)
    {
        Win32Api.NativeRect rect = new Win32Api.NativeRect();
        rect.left = left;
        rect.top = top;
        rect.right = right;
        rect.bottom = bottom;
        return rect;
    }

    private static bool Contains(Win32Api.NativeRect rect, int x, int y)
    {
        return x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;
    }

    private static uint Color(int r, int g, int b)
    {
        return ModernUiPainter.Color(r, g, b);
    }

    private sealed class AsyncTextResult
    {
        public int Version;
        public string Text;
        public Exception Error;
    }
}
