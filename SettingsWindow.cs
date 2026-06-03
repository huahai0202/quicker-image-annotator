using System;
using System.Runtime.InteropServices;

internal sealed class SettingsWindow
{
    private const string WindowClassName = "QuickerImageAnnotatorSettingsWindow";
    private const int Width = 800;
    private const int Height = 850;
    private const int IdOutputEdit = 101;
    private const int IdScreenshotEdit = 102;
    private const int IdHotkey = 103;
    private const int IdOcrApiKey = 104;
    private const int IdOcrSecretKey = 105;
    private const int IccHotkeyClass = 0x00000040;
    private const string FontFace = "Microsoft YaHei UI";
    private static Win32Api.WindowProc sharedProc;
    private static SettingsWindow current;
    private static IntPtr gdiPlusToken;
    private static bool gdiPlusTried;
    private static bool gdiPlusAvailable;

    private IntPtr hwnd;
    private IntPtr outputEdit;
    private IntPtr screenshotEdit;
    private IntPtr hotkeyControl;
    private IntPtr ocrApiKeyEdit;
    private IntPtr ocrSecretKeyEdit;
    private IntPtr titleFont;
    private IntPtr sectionFont;
    private IntPtr bodyFont;
    private IntPtr smallFont;
    private IntPtr buttonFont;
    private bool autoStartEnabled;
    private bool globalHotkeyEnabled = true;
    private OcrEngineKind ocrEngine = OcrEngineKind.Standard;
    private OcrTextLayout ocrLayout = OcrTextLayout.SmartParagraph;
    private IntPtr owner;
    private bool closed;

    public static int Run()
    {
        return Run(IntPtr.Zero);
    }

    public static int Run(IntPtr owner)
    {
        SettingsWindow window = new SettingsWindow();
        window.owner = owner;
        return window.RunInternal();
    }

    private int RunInternal()
    {
        EnsureCommonControls();
        EnsureWindowClass();
        int x = Math.Max(0, (Win32Api.GetSystemMetrics(Win32Api.SmCxScreen) - Width) / 2);
        int y = Math.Max(0, (Win32Api.GetSystemMetrics(Win32Api.SmCyScreen) - Height) / 2);
        hwnd = Win32Api.CreateWindowEx(
            0,
            WindowClassName,
            UiText.SettingsTitle,
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
            throw new InvalidOperationException("CreateWindowEx failed for settings window.");
        }

        current = this;
        CreateFonts();
        CreateControls();
        LoadSettings();
        Win32Api.ShowWindow(hwnd, Win32Api.SwShow);
        Win32Api.SetForegroundWindow(hwnd);
        Win32Api.UpdateWindow(hwnd);

        Win32Api.Msg msg;
        while (!closed && Win32Api.GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            Win32Api.TranslateMessage(ref msg);
            Win32Api.DispatchMessage(ref msg);
        }

        return 0;
    }

    private static void EnsureCommonControls()
    {
        Win32Api.InitCommonControlsExData data = new Win32Api.InitCommonControlsExData();
        data.dwSize = Marshal.SizeOf(typeof(Win32Api.InitCommonControlsExData));
        data.dwICC = IccHotkeyClass;
        Win32Api.InitCommonControlsEx(ref data);
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
            throw new InvalidOperationException("RegisterClassEx failed for settings window.");
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        SettingsWindow window = current;
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
            case Win32Api.WmClose:
                Close();
                return IntPtr.Zero;
            case Win32Api.WmDestroy:
                DestroyFonts();
                ShutdownGdiPlus();
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
        outputEdit = CreateEdit(IdOutputEdit, FieldOutput().left + 14, FieldOutput().top + 8, FieldOutput().right - FieldOutput().left - 28, 22, Win32Api.EsAutoHScroll);
        screenshotEdit = CreateEdit(IdScreenshotEdit, FieldScreenshot().left + 14, FieldScreenshot().top + 8, FieldScreenshot().right - FieldScreenshot().left - 28, 22, Win32Api.EsAutoHScroll);
        hotkeyControl = CreateHotkey(IdHotkey, HotkeyField().left + 14, HotkeyField().top + 7, HotkeyField().right - HotkeyField().left - 28, 24);
        ocrApiKeyEdit = CreateEdit(IdOcrApiKey, OcrApiKeyField().left + 14, OcrApiKeyField().top + 8, OcrApiKeyField().right - OcrApiKeyField().left - 28, 22, Win32Api.EsAutoHScroll);
        ocrSecretKeyEdit = CreateEdit(IdOcrSecretKey, OcrSecretKeyField().left + 14, OcrSecretKeyField().top + 8, OcrSecretKeyField().right - OcrSecretKeyField().left - 28, 22, Win32Api.EsAutoHScroll | Win32Api.EsPassword);
        ApplyControlFont(outputEdit);
        ApplyControlFont(screenshotEdit);
        ApplyControlFont(hotkeyControl);
        ApplyControlFont(ocrApiKeyEdit);
        ApplyControlFont(ocrSecretKeyEdit);
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
        sectionFont = CreateUiFont(12, dpi, Win32Api.FontWeightSemiBold);
        bodyFont = CreateUiFont(10, dpi, Win32Api.FontWeightRegular);
        smallFont = CreateUiFont(9, dpi, Win32Api.FontWeightRegular);
        buttonFont = CreateUiFont(10, dpi, Win32Api.FontWeightSemiBold);
    }

    private static IntPtr CreateUiFont(int points, int dpi, int weight)
    {
        return Win32Api.CreateFont(
            -Win32Api.MulDiv(points, dpi, 72),
            0,
            0,
            0,
            weight,
            0,
            0,
            0,
            Win32Api.DefaultCharSet,
            Win32Api.OutDefaultPrecision,
            Win32Api.ClipDefaultPrecision,
            Win32Api.ClearTypeQuality,
            Win32Api.DefaultPitch,
            FontFace);
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
        DeleteFont(ref sectionFont);
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

    private void LoadSettings()
    {
        AppSettings settings = AppSettingsStore.Load();
        Win32Api.SetWindowTextUnicode(outputEdit, settings.OutputDirectory ?? string.Empty);
        Win32Api.SetWindowTextUnicode(screenshotEdit, settings.ScreenshotDirectory ?? string.Empty);
        Win32Api.SetWindowTextUnicode(ocrApiKeyEdit, settings.BaiduOcrApiKey ?? string.Empty);
        Win32Api.SetWindowTextUnicode(ocrSecretKeyEdit, settings.BaiduOcrSecretKey ?? string.Empty);
        autoStartEnabled = settings.AutoStartEnabled;
        globalHotkeyEnabled = settings.GlobalHotkeyEnabled;
        ocrEngine = BaiduOcrClient.NormalizeEngine(settings.OcrEngine);
        ocrLayout = OcrTextFormatter.NormalizeLayout(settings.OcrLayout);
        SetHotkey(AppFeatures.GetGlobalHotkeyModifiers(settings), AppFeatures.GetGlobalHotkeyKey(settings));
        RequestPaint();
    }

    private void HandleClick(int x, int y)
    {
        if (Contains(ButtonOutputBrowse(), x, y))
        {
            BrowseDirectory(outputEdit, UiText.SelectOutputDirectory);
        }
        else if (Contains(ButtonOutputClear(), x, y))
        {
            Win32Api.SetWindowTextUnicode(outputEdit, string.Empty);
        }
        else if (Contains(ButtonScreenshotBrowse(), x, y))
        {
            BrowseDirectory(screenshotEdit, UiText.SelectScreenshotDirectory);
        }
        else if (Contains(ButtonScreenshotClear(), x, y))
        {
            Win32Api.SetWindowTextUnicode(screenshotEdit, string.Empty);
        }
        else if (Contains(ToggleAutoStart(), x, y))
        {
            autoStartEnabled = !autoStartEnabled;
            RequestPaint();
        }
        else if (Contains(ToggleGlobalHotkey(), x, y))
        {
            globalHotkeyEnabled = !globalHotkeyEnabled;
            RequestPaint();
        }
        else if (Contains(OcrEngineStandardButton(), x, y))
        {
            ocrEngine = OcrEngineKind.Standard;
            RequestPaint();
        }
        else if (Contains(OcrEngineAccurateButton(), x, y))
        {
            ocrEngine = OcrEngineKind.Accurate;
            RequestPaint();
        }
        else if (Contains(OcrLayoutLinesButton(), x, y))
        {
            ocrLayout = OcrTextLayout.Lines;
            RequestPaint();
        }
        else if (Contains(OcrLayoutParagraphButton(), x, y))
        {
            ocrLayout = OcrTextLayout.SmartParagraph;
            RequestPaint();
        }
        else if (Contains(ButtonSave(), x, y))
        {
            SaveAndClose();
        }
        else if (Contains(ButtonCancel(), x, y))
        {
            Close();
        }
    }

    private void BrowseDirectory(IntPtr edit, string title)
    {
        string selected = ShellDialogs.BrowseForFolder(hwnd, title);
        if (!string.IsNullOrEmpty(selected))
        {
            Win32Api.SetWindowTextUnicode(edit, selected);
        }
    }

    private void SaveAndClose()
    {
        uint modifiers = AppFeatures.DefaultHotkeyModifiers;
        int key = Win32Api.VkA;
        if (globalHotkeyEnabled && !TryGetHotkey(out modifiers, out key))
        {
            Win32Api.MessageBoxUnicode(hwnd, UiText.HotkeyRequired, UiText.AppName, Win32Api.MbOk | Win32Api.MbIconWarning);
            return;
        }
        if (!globalHotkeyEnabled && !TryGetHotkey(out modifiers, out key))
        {
            modifiers = AppFeatures.DefaultHotkeyModifiers;
            key = Win32Api.VkA;
        }

        string outputDirectory = AppSettingsStore.NormalizeDirectory(Win32Api.GetWindowTextUnicode(outputEdit));
        string screenshotDirectory = AppSettingsStore.NormalizeDirectory(Win32Api.GetWindowTextUnicode(screenshotEdit));
        string ocrApiKey = (Win32Api.GetWindowTextUnicode(ocrApiKeyEdit) ?? string.Empty).Trim();
        string ocrSecretKey = (Win32Api.GetWindowTextUnicode(ocrSecretKeyEdit) ?? string.Empty).Trim();
        try
        {
            AppFeatures.ApplyAutoStart(autoStartEnabled);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to update startup setting.", ex);
            Win32Api.MessageBoxUnicode(hwnd, UiText.StartupSettingFailed + Environment.NewLine + ex.Message, UiText.AppName, Win32Api.MbOk | Win32Api.MbIconError);
            return;
        }

        AppSettings updated = AppSettingsStore.Load();
        updated.OutputDirectory = outputDirectory;
        updated.ScreenshotDirectory = screenshotDirectory;
        updated.AutoStartEnabled = autoStartEnabled;
        updated.GlobalHotkeyEnabled = globalHotkeyEnabled;
        updated.GlobalHotkeyModifiers = modifiers;
        updated.GlobalHotkeyKey = key;
        updated.BaiduOcrApiKey = ocrApiKey;
        updated.BaiduOcrSecretKey = ocrSecretKey;
        updated.OcrEngine = ocrEngine;
        updated.OcrLayout = ocrLayout;
        AppSettingsStore.Save(updated);

        string error;
        if (!AppFeatures.TrySyncBackgroundHotkeyAgent(globalHotkeyEnabled, out error))
        {
            string message = globalHotkeyEnabled ? UiText.GlobalHotkeyStartFailed : UiText.GlobalHotkeyStopFailed;
            if (!string.IsNullOrEmpty(error))
            {
                message += Environment.NewLine + error;
            }
            Win32Api.MessageBoxUnicode(hwnd, message, UiText.AppName, Win32Api.MbOk | Win32Api.MbIconWarning);
        }

        Close();
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
            FillRoundRect(hdc, Card(), 22, Color(255, 255, 255), Color(224, 231, 241), 1);

            DrawText(hdc, UiText.SettingsTitle, 24, 42, 40, 260, 36, Color(15, 23, 42));
            DrawText(hdc, UiText.SettingsSubtitle, 12, 42, 72, 440, 24, Color(71, 85, 105));
            DrawText(hdc, "v" + AppFeatures.GetAppVersionText(), 11, 620, 48, 110, 22, Color(100, 116, 139), Win32Api.DtLeft | Win32Api.DtVCenter | Win32Api.DtSingleLine);

            DrawSectionTitle(hdc, "\u4fdd\u5b58\u76ee\u5f55", 42, 112);
            DrawDirectoryBlock(hdc, UiText.SaveDirectory, FieldOutput(), ButtonOutputBrowse(), ButtonOutputClear(), OutputDirectoryHint());
            DrawDirectoryBlock(hdc, UiText.ScreenshotDirectory, FieldScreenshot(), ButtonScreenshotBrowse(), ButtonScreenshotClear(), ScreenshotDirectoryHint());

            DrawSeparator(hdc, 42, 360, 728);
            DrawSectionTitle(hdc, "\u540e\u53f0\u4e0e\u5feb\u6377\u952e", 42, 382);
            DrawToggleRow(hdc, ToggleAutoStart(), UiText.AutoStart, "\u767b\u5f55 Windows \u540e\u81ea\u52a8\u542f\u52a8", autoStartEnabled);
            DrawToggleRow(hdc, ToggleGlobalHotkey(), UiText.GlobalHotkey.Trim(), "\u4fdd\u5b58\u540e\u7acb\u5373\u751f\u6548", globalHotkeyEnabled);
            DrawText(hdc, UiText.HotkeyLabel, 13, 438, 472, 92, 24, Color(15, 23, 42));
            DrawField(hdc, HotkeyField());
            DrawText(hdc, "\u70ed\u952e\u5728\u540e\u53f0\u8fd0\u884c\u65f6\u751f\u6548", 10, HotkeyField().left, HotkeyField().bottom + 7, 220, 18, Color(100, 116, 139));

            DrawSeparator(hdc, 42, 548, 728);
            DrawSectionTitle(hdc, UiText.OcrSettings, 42, 570);
            DrawText(hdc, UiText.OcrApiKey, 13, OcrApiKeyField().left, OcrApiKeyField().top - 29, 180, 22, Color(15, 23, 42));
            DrawField(hdc, OcrApiKeyField());
            DrawText(hdc, UiText.OcrSecretKey, 13, OcrSecretKeyField().left, OcrSecretKeyField().top - 29, 180, 22, Color(15, 23, 42));
            DrawField(hdc, OcrSecretKeyField());
            DrawText(hdc, UiText.OcrEngine, 13, 42, OcrEngineStandardButton().top - 29, 160, 22, Color(15, 23, 42));
            DrawSegmentButton(hdc, OcrEngineStandardButton(), UiText.OcrStandard, ocrEngine == OcrEngineKind.Standard);
            DrawSegmentButton(hdc, OcrEngineAccurateButton(), UiText.OcrAccurate, ocrEngine == OcrEngineKind.Accurate);
            DrawText(hdc, UiText.OcrLayout, 13, 430, OcrLayoutLinesButton().top - 29, 160, 22, Color(15, 23, 42));
            DrawSegmentButton(hdc, OcrLayoutLinesButton(), UiText.OcrLines, ocrLayout == OcrTextLayout.Lines);
            DrawSegmentButton(hdc, OcrLayoutParagraphButton(), UiText.OcrSmartParagraph, ocrLayout == OcrTextLayout.SmartParagraph);

            DrawButton(hdc, ButtonSave(), UiText.Save, Color(13, 148, 136), Color(255, 255, 255), true);
            DrawButton(hdc, ButtonCancel(), UiText.Cancel, Color(220, 38, 38), Color(220, 38, 38), false);
        }
        finally
        {
            Win32Api.EndPaint(hwnd, ref ps);
        }
    }

    private void DrawDirectoryBlock(IntPtr hdc, string label, Win32Api.NativeRect field, Win32Api.NativeRect browse, Win32Api.NativeRect clear, string hint)
    {
        DrawText(hdc, label, 13, field.left, field.top - 29, 260, 22, Color(15, 23, 42));
        DrawField(hdc, field);
        DrawButton(hdc, browse, UiText.Browse, Color(37, 99, 235), Color(37, 99, 235), false);
        DrawButton(hdc, clear, UiText.Clear, Color(71, 85, 105), Color(71, 85, 105), false);
        DrawText(hdc, hint, 10, field.left, field.bottom + 8, 680, 18, Color(100, 116, 139));
    }

    private void DrawSectionTitle(IntPtr hdc, string text, int x, int y)
    {
        DrawText(hdc, text, 14, x, y, 260, 24, Color(15, 23, 42));
    }

    private static void DrawSeparator(IntPtr hdc, int x, int y, int right)
    {
        FillRect(hdc, Rect(x, y, right, y + 1), Color(226, 232, 240));
    }

    private void DrawToggleRow(IntPtr hdc, Win32Api.NativeRect rect, string title, string detail, bool enabled)
    {
        DrawText(hdc, title, 13, rect.left, rect.top, 200, 22, Color(15, 23, 42));
        DrawText(hdc, detail, 10, rect.left, rect.top + 24, 270, 18, Color(100, 116, 139));
        Win32Api.NativeRect track = Rect(rect.right - 54, rect.top + 8, rect.right, rect.top + 34);
        uint trackColor = enabled ? Color(15, 118, 110) : Color(203, 213, 225);
        FillRoundRect(hdc, track, 13, trackColor, trackColor, 1);
        int knobLeft = enabled ? track.right - 23 : track.left + 3;
        FillRoundRect(hdc, Rect(knobLeft, track.top + 3, knobLeft + 20, track.top + 23), 10, Color(255, 255, 255), Color(255, 255, 255), 1);
    }

    private void DrawField(IntPtr hdc, Win32Api.NativeRect rect)
    {
        FillRoundRect(hdc, rect, 11, Color(255, 255, 255), Color(216, 226, 236), 1);
    }

    private void DrawButton(IntPtr hdc, Win32Api.NativeRect rect, string text, uint color, uint textColor, bool primary)
    {
        uint fill = primary ? color : Color(255, 255, 255);
        FillRoundRect(hdc, rect, 11, fill, color, primary ? 0 : 1);
        DrawTextWithFont(hdc, text, buttonFont, rect.left, rect.top + 1, rect.right - rect.left, rect.bottom - rect.top, textColor, Win32Api.DtCenter | Win32Api.DtVCenter | Win32Api.DtSingleLine);
    }

    private void DrawSegmentButton(IntPtr hdc, Win32Api.NativeRect rect, string text, bool selected)
    {
        uint border = selected ? Color(37, 99, 235) : Color(203, 213, 225);
        uint fill = selected ? Color(239, 246, 255) : Color(255, 255, 255);
        uint textColor = selected ? Color(29, 78, 216) : Color(71, 85, 105);
        FillRoundRect(hdc, rect, 10, fill, border, 1);
        DrawTextWithFont(hdc, text, buttonFont, rect.left, rect.top + 1, rect.right - rect.left, rect.bottom - rect.top, textColor, Win32Api.DtCenter | Win32Api.DtVCenter | Win32Api.DtSingleLine | Win32Api.DtEndEllipsis);
    }

    private static void FillRect(IntPtr hdc, Win32Api.NativeRect rect, uint color)
    {
        ModernUiPainter.FillRect(hdc, rect, color);
    }

    private static void FillRoundRect(IntPtr hdc, Win32Api.NativeRect rect, int radius, uint fill, uint border, int borderWidth)
    {
        ModernUiPainter.FillRoundRect(hdc, rect, radius, fill, border, borderWidth);
    }

    private static bool TryFillRoundRectWithGdiPlus(IntPtr hdc, Win32Api.NativeRect rect, int radius, uint fill, uint border, int borderWidth)
    {
        if (hdc == IntPtr.Zero || !EnsureGdiPlus())
        {
            return false;
        }

        IntPtr graphics = IntPtr.Zero;
        IntPtr path = IntPtr.Zero;
        IntPtr brush = IntPtr.Zero;
        IntPtr pen = IntPtr.Zero;
        try
        {
            if (Win32Api.GdipCreateFromHDC(hdc, out graphics) != Win32Api.GdiPlusOk || graphics == IntPtr.Zero)
            {
                return false;
            }
            Win32Api.GdipSetSmoothingMode(graphics, Win32Api.GdiPlusSmoothingModeAntiAlias);
            Win32Api.GdipSetPixelOffsetMode(graphics, Win32Api.GdiPlusPixelOffsetModeHalf);

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
            if (Win32Api.GdipFillPath(graphics, brush, path) != Win32Api.GdiPlusOk)
            {
                return false;
            }

            if (borderWidth > 0)
            {
                if (Win32Api.GdipCreatePen1(Argb(border), borderWidth, Win32Api.GdiPlusUnitPixel, out pen) != Win32Api.GdiPlusOk || pen == IntPtr.Zero)
                {
                    return false;
                }
                if (Win32Api.GdipDrawPath(graphics, pen, path) != Win32Api.GdiPlusOk)
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
            if (graphics != IntPtr.Zero)
            {
                Win32Api.GdipDeleteSurface(graphics);
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

    private static void ShutdownGdiPlus()
    {
        if (gdiPlusToken != IntPtr.Zero)
        {
            Win32Api.GdiplusShutdown(gdiPlusToken);
            gdiPlusToken = IntPtr.Zero;
        }
        gdiPlusAvailable = false;
        gdiPlusTried = false;
    }

    private static int Argb(uint color)
    {
        uint r = color & 0xff;
        uint g = (color >> 8) & 0xff;
        uint b = (color >> 16) & 0xff;
        return unchecked((int)(0xff000000u | (r << 16) | (g << 8) | b));
    }

    private void DrawText(IntPtr hdc, string text, int em, int x, int y, int width, int height, uint color)
    {
        DrawText(hdc, text, em, x, y, width, height, color, Win32Api.DtLeft | Win32Api.DtVCenter | Win32Api.DtSingleLine | Win32Api.DtEndEllipsis);
    }

    private void DrawText(IntPtr hdc, string text, int em, int x, int y, int width, int height, uint color, uint format)
    {
        DrawTextWithFont(hdc, text, FontForSize(em), x, y, width, height, color, format);
    }

    private static void DrawTextWithFont(IntPtr hdc, string text, IntPtr font, int x, int y, int width, int height, uint color, uint format)
    {
        ModernUiPainter.DrawText(hdc, text, font, x, y, width, height, color, format);
    }

    private IntPtr FontForSize(int em)
    {
        if (em >= 20)
        {
            return titleFont;
        }
        if (em >= 14)
        {
            return sectionFont;
        }
        if (em <= 10)
        {
            return smallFont;
        }
        if (em == 13)
        {
            return bodyFont;
        }
        return bodyFont;
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

    private void SetHotkey(uint modifiers, int key)
    {
        int packed = (HotkeyFlagsFromModifiers(modifiers) << 8) | AppFeatures.NormalizeHotkeyKey(key);
        Win32Api.SendMessage(hotkeyControl, Win32Api.HkmSetHotkey, new IntPtr(packed), IntPtr.Zero);
    }

    private bool TryGetHotkey(out uint modifiers, out int key)
    {
        int packed = Win32Api.SendMessage(hotkeyControl, Win32Api.HkmGetHotkey, IntPtr.Zero, IntPtr.Zero).ToInt32();
        key = packed & 0xff;
        byte flags = (byte)((packed >> 8) & 0xff);
        modifiers = ModifiersFromHotkeyFlags(flags);
        return key != 0 && modifiers != 0;
    }

    private static byte HotkeyFlagsFromModifiers(uint modifiers)
    {
        byte flags = 0;
        if ((modifiers & Win32Api.HotkeyModShift) != 0)
        {
            flags |= Win32Api.HotkeyfShift;
        }
        if ((modifiers & Win32Api.HotkeyModControl) != 0)
        {
            flags |= Win32Api.HotkeyfControl;
        }
        if ((modifiers & Win32Api.HotkeyModAlt) != 0)
        {
            flags |= Win32Api.HotkeyfAlt;
        }
        return flags;
    }

    private static uint ModifiersFromHotkeyFlags(byte flags)
    {
        uint modifiers = 0;
        if ((flags & Win32Api.HotkeyfShift) != 0)
        {
            modifiers |= Win32Api.HotkeyModShift;
        }
        if ((flags & Win32Api.HotkeyfControl) != 0)
        {
            modifiers |= Win32Api.HotkeyModControl;
        }
        if ((flags & Win32Api.HotkeyfAlt) != 0)
        {
            modifiers |= Win32Api.HotkeyModAlt;
        }
        return modifiers;
    }

    private static IntPtr CreateEdit(int id, int x, int y, int width, int height, int editStyle)
    {
        return Win32Api.CreateWindowEx(0, "EDIT", string.Empty, Win32Api.WsChild | Win32Api.WsVisible | Win32Api.WsTabStop | editStyle, x, y, width, height, current.hwnd, new IntPtr(id), Win32Api.GetModuleHandle(null), IntPtr.Zero);
    }

    private static IntPtr CreateHotkey(int id, int x, int y, int width, int height)
    {
        return Win32Api.CreateWindowEx(0, "msctls_hotkey32", string.Empty, Win32Api.WsChild | Win32Api.WsVisible | Win32Api.WsTabStop, x, y, width, height, current.hwnd, new IntPtr(id), Win32Api.GetModuleHandle(null), IntPtr.Zero);
    }

    private static Win32Api.NativeRect Card()
    {
        return Rect(20, 20, 764, 808);
    }

    private static Win32Api.NativeRect FieldOutput()
    {
        return Rect(42, 166, 562, 208);
    }

    private static Win32Api.NativeRect ButtonOutputBrowse()
    {
        return Rect(578, 166, 660, 208);
    }

    private static Win32Api.NativeRect ButtonOutputClear()
    {
        return Rect(672, 166, 728, 208);
    }

    private static Win32Api.NativeRect FieldScreenshot()
    {
        return Rect(42, 270, 562, 312);
    }

    private static Win32Api.NativeRect ButtonScreenshotBrowse()
    {
        return Rect(578, 270, 660, 312);
    }

    private static Win32Api.NativeRect ButtonScreenshotClear()
    {
        return Rect(672, 270, 728, 312);
    }

    private static Win32Api.NativeRect ToggleAutoStart()
    {
        return Rect(42, 420, 390, 468);
    }

    private static Win32Api.NativeRect ToggleGlobalHotkey()
    {
        return Rect(42, 472, 390, 520);
    }

    private static Win32Api.NativeRect HotkeyField()
    {
        return Rect(520, 470, 728, 512);
    }

    private static Win32Api.NativeRect ButtonSave()
    {
        return Rect(544, 762, 636, 800);
    }

    private static Win32Api.NativeRect ButtonCancel()
    {
        return Rect(648, 762, 728, 800);
    }

    private static Win32Api.NativeRect OcrApiKeyField()
    {
        return Rect(42, 622, 360, 662);
    }

    private static Win32Api.NativeRect OcrSecretKeyField()
    {
        return Rect(386, 622, 728, 662);
    }

    private static Win32Api.NativeRect OcrEngineStandardButton()
    {
        return Rect(42, 696, 160, 736);
    }

    private static Win32Api.NativeRect OcrEngineAccurateButton()
    {
        return Rect(172, 696, 360, 736);
    }

    private static Win32Api.NativeRect OcrLayoutLinesButton()
    {
        return Rect(430, 696, 540, 736);
    }

    private static Win32Api.NativeRect OcrLayoutParagraphButton()
    {
        return Rect(552, 696, 728, 736);
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

    private static string OutputDirectoryHint()
    {
        return "\u672a\u8bbe\u7f6e\uff1a\u672c\u5730\u56fe\u7247\u5b58\u539f\u76ee\u5f55\uff0c\u526a\u8d34\u677f\u56fe\u7247\u5b58\u4e34\u65f6\u76ee\u5f55";
    }

    private static string ScreenshotDirectoryHint()
    {
        return "\u672a\u8bbe\u7f6e\uff1a\u8ddf\u968f\u6807\u6ce8\u76ee\u5f55\uff0c\u90fd\u4e3a\u7a7a\u65f6\u5b58\u4e34\u65f6\u76ee\u5f55";
    }
}
