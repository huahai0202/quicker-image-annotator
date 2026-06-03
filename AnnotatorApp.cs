using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool selfTest = args.Length > 0 && string.Equals(args[0], "-SelfTest", StringComparison.OrdinalIgnoreCase);
        bool background = IsOptionPresent(args, "--background");
        bool screenshot = IsOptionPresent(args, "--screenshot");
        bool settingsWindow = IsOptionPresent(args, "--settings");
        try
        {
            Win32Api.CoInitializeEx(IntPtr.Zero, 2);
            Win32Api.ConfigureProcessDpiAwareness();
            if (selfTest)
            {
                RunSelfTest();
                return 0;
            }
            if (background)
            {
                return BackgroundHotkeyAgent.Run();
            }
            AppSettings settings = AppSettingsStore.Load();
            EnsureBackgroundHotkeyAgentForInteractiveLaunch(settings);
            if (settingsWindow)
            {
                return SettingsWindow.Run();
            }

            string outputDirectory = settings.OutputDirectory;
            string screenshotDirectory = settings.ScreenshotDirectory;
            string[] inputArgs = GetInputArgs(args);
            string inputPath = screenshot ? ScreenCapture.CaptureSelectedScreenToTempFile() : GetInputImagePath(inputArgs);
            if (string.IsNullOrEmpty(inputPath))
            {
                return 0;
            }
            using (WicImageDocument image = WicImageDocument.Load(inputPath))
            using (GpuAnnotatorWindow window = new GpuAnnotatorWindow(inputPath, image, outputDirectory, screenshotDirectory, screenshot))
            {
                return window.Run();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Application failed.", ex);
            if (selfTest || background)
            {
                WriteFailure(ex);
                return 1;
            }
            Win32Api.MessageBoxUnicode(IntPtr.Zero, ex.Message, UiText.AppName, Win32Api.MbOk | Win32Api.MbIconError);
            return 1;
        }
        finally
        {
            DWriteApi.ReleaseSharedFactory();
            Win32Api.CoUninitialize();
        }
    }

    private static void EnsureBackgroundHotkeyAgentForInteractiveLaunch(AppSettings settings)
    {
        string error;
        if (!AppFeatures.TryEnsureBackgroundHotkeyAgent(settings, out error) && !string.IsNullOrEmpty(error))
        {
            AppLog.Error("Failed to ensure background hotkey agent.", new InvalidOperationException(error));
        }
    }

    private static string GetInputImagePath(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(args[0].Trim('"')));
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(UiText.ImageFileDoesNotExist + Environment.NewLine + path);
            }
            return path;
        }

        string clipboardFile = ClipboardBridge.TryGetClipboardImagePath();
        if (!string.IsNullOrEmpty(clipboardFile))
        {
            return clipboardFile;
        }

        throw new InvalidOperationException(UiText.NoImageFound);
    }

    private static string[] GetInputArgs(string[] args)
    {
        List<string> input = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (IsOption(args[i], "--screenshot") ||
                IsOption(args[i], "--background") ||
                IsOption(args[i], "--settings"))
            {
                continue;
            }
            input.Add(args[i]);
        }
        return input.ToArray();
    }

    private static bool IsOptionPresent(string[] args, string option)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (IsOption(args[i], option))
            {
                return true;
            }
        }
        return false;
    }

    private static BenchmarkResult RunBenchmarkCase(BenchmarkCase benchmarkCase, int frames)
    {
        using (WicImageDocument image = WicImageDocument.CreateSynthetic(benchmarkCase.Width, benchmarkCase.Height))
        {
            List<AnnotationItem> annotations = CreateBenchmarkAnnotations(benchmarkCase.Width, benchmarkCase.Height);
            double[] samples = new double[frames];
            using (SelfTestRenderSurface surface = SelfTestRenderSurface.Create(benchmarkCase.CanvasWidth, benchmarkCase.CanvasHeight))
            {
                using (GpuRenderer renderer = GpuRenderer.CreateForWindow(image))
                {
                    GpuRect view = GetBenchmarkView(benchmarkCase);
                    for (int i = 0; i < frames; i++)
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        renderer.RenderToHdc(surface.Hdc, benchmarkCase.CanvasWidth, benchmarkCase.CanvasHeight, view, annotations, null, -1, false, ToolMode.Rect, AppStyles.DefaultStroke, AppStyles.DefaultStrokeWidth, null);
                        sw.Stop();
                        samples[i] = sw.Elapsed.TotalMilliseconds;
                    }
                }
            }
            Array.Sort(samples);
            double sum = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i];
            }
            BenchmarkResult result = new BenchmarkResult();
            result.Avg = sum / samples.Length;
            result.P50 = samples[Math.Min(samples.Length - 1, (int)Math.Floor(samples.Length * 0.50))];
            result.P95 = samples[Math.Min(samples.Length - 1, (int)Math.Floor(samples.Length * 0.95))];
            result.Max = samples[samples.Length - 1];
            return result;
        }
    }

    private static List<AnnotationItem> CreateBenchmarkAnnotations(int width, int height)
    {
        List<AnnotationItem> items = new List<AnnotationItem>();
        items.Add(new AnnotationItem { Tool = ToolMode.Rect, Start = new GpuPoint(width * 0.08f, height * 0.08f), End = new GpuPoint(width * 0.42f, height * 0.30f), Stroke = AppStyles.Palette[0], StrokeWidth = 8f });
        items.Add(new AnnotationItem { Tool = ToolMode.Ellipse, Start = new GpuPoint(width * 0.52f, height * 0.10f), End = new GpuPoint(width * 0.86f, height * 0.34f), Stroke = AppStyles.Palette[1], StrokeWidth = 8f });
        items.Add(new AnnotationItem { Tool = ToolMode.Arrow, Start = new GpuPoint(width * 0.18f, height * 0.72f), End = new GpuPoint(width * 0.78f, height * 0.44f), Stroke = AppStyles.Palette[2], StrokeWidth = 10f });
        AnnotationItem pen = new AnnotationItem { Tool = ToolMode.Pen, Stroke = AppStyles.Palette[4], StrokeWidth = 7f };
        for (int i = 0; i < 140; i++)
        {
            float t = i / 139f;
            pen.AddPoint(new GpuPoint(width * (0.08f + 0.84f * t), height * (0.56f + 0.08f * (float)Math.Sin(t * Math.PI * 8))));
        }
        items.Add(pen);
        items.Add(new AnnotationItem { Tool = ToolMode.Text, Start = new GpuPoint(width * 0.12f, height * 0.38f), Stroke = AppStyles.Palette[3], StrokeWidth = 7f, Text = "GPU" });
        items.Add(new AnnotationItem { Tool = ToolMode.Mosaic, Start = new GpuPoint(width * 0.58f, height * 0.58f), End = new GpuPoint(width * 0.88f, height * 0.86f), Stroke = AppStyles.Palette[0], StrokeWidth = 5f });
        return items;
    }

    private static GpuRect GetBenchmarkView(BenchmarkCase benchmarkCase)
    {
        float scale = Math.Min(
            benchmarkCase.CanvasWidth / (float)benchmarkCase.Width,
            benchmarkCase.CanvasHeight / (float)benchmarkCase.Height);
        float width = benchmarkCase.Width * scale;
        float height = benchmarkCase.Height * scale;
        return new GpuRect(
            (benchmarkCase.CanvasWidth - width) / 2f,
            (benchmarkCase.CanvasHeight - height) / 2f,
            width,
            height);
    }

    private static void RunSelfTest()
    {
        AssertNoForbiddenDependencies();
        AssertWicRoundTrip();
        AssertScreenCapture();
        AssertScreenSelection();
        AssertGpuExport();
        AssertDirectWriteTextLayout();
        AssertGpuRendererRebuild();
        AssertGpuWindowChromeRender();
        AssertGpuInteractionSemantics();
        AssertAppFeatureSemantics();
        AssertOcrSemantics();
        AssertClipboardWorkflow();
        AssertClipboardStartupSmoke();
        AssertBenchmarkSmoke();
    }

    private static void AssertNoForbiddenDependencies()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string[] forbidden = new string[]
        {
            "System." + "Drawing",
            "System.Windows." + "Forms",
            "Gra" + "phics",
            "Bit" + "map",
            "Image" + "Format"
        };
        foreach (string file in Directory.GetFiles(root, "*.cs"))
        {
            string text = File.ReadAllText(file);
            for (int i = 0; i < forbidden.Length; i++)
            {
                if (text.IndexOf(forbidden[i], StringComparison.Ordinal) >= 0)
                {
                    throw new InvalidOperationException("Forbidden dependency token '" + forbidden[i] + "' found in " + Path.GetFileName(file));
                }
            }
        }
        string build = File.ReadAllText(Path.Combine(root, "BuildAndRun.ps1"));
        if (build.IndexOf("System." + "Drawing", StringComparison.Ordinal) >= 0 ||
            build.IndexOf("System.Windows." + "Forms", StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("Forbidden build reference found.");
        }
    }

    private static void AssertWicRoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), "quicker-gpu-wic-roundtrip-" + Guid.NewGuid().ToString("N") + ".png");
        using (WicImageDocument image = WicImageDocument.CreateSynthetic(32, 24))
        {
            image.Save(path);
        }
        using (WicImageDocument loaded = WicImageDocument.Load(path))
        {
            if (loaded.Width != 32 || loaded.Height != 24 || loaded.Pixels.Length != 32 * 24 * 4)
            {
                throw new InvalidOperationException("WIC roundtrip self test failed.");
            }
        }
    }

    private static void AssertScreenCapture()
    {
        string path = ScreenCapture.CapturePrimaryScreenToTempFileForSelfTest(32, 24);
        try
        {
            using (WicImageDocument loaded = WicImageDocument.Load(path))
            {
                if (loaded.Width <= 0 || loaded.Height <= 0 || loaded.Width > 32 || loaded.Height > 24)
                {
                    throw new InvalidOperationException("Screen capture self test produced an unexpected image size.");
                }
            }
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    private static void AssertScreenSelection()
    {
        Win32Api.NativeRect normalized = ScreenSelectionWindow.NormalizeRegionForSelfTest(-5, 8, 12, 2, 10, 10);
        if (normalized.left != 0 || normalized.top != 2 || normalized.right != 10 || normalized.bottom != 8)
        {
            throw new InvalidOperationException("Screen selection normalization self test failed.");
        }

        Win32Api.NativeRect windowRect = new Win32Api.NativeRect();
        windowRect.left = -90;
        windowRect.top = -40;
        windowRect.right = 260;
        windowRect.bottom = 180;
        Win32Api.NativeRect converted;
        if (!ScreenSelectionWindow.TryConvertWindowRectForSelfTest(windowRect, -100, -50, 320, 240, out converted) ||
            converted.left != 10 || converted.top != 10 || converted.right != 320 || converted.bottom != 230)
        {
            throw new InvalidOperationException("Screen window selection conversion self test failed.");
        }

        byte[] source = new byte[4 * 3 * 4];
        for (int i = 0; i < source.Length; i += 4)
        {
            int pixel = i / 4;
            source[i] = (byte)pixel;
            source[i + 1] = (byte)(pixel + 30);
            source[i + 2] = (byte)(pixel + 60);
            source[i + 3] = 255;
        }

        Win32Api.NativeRect region = new Win32Api.NativeRect();
        region.left = 1;
        region.top = 1;
        region.right = 3;
        region.bottom = 3;
        int width;
        int height;
        byte[] copied = ScreenCapture.CopyRegionPixels(source, 4, 3, region, out width, out height);
        if (width != 2 || height != 2 || copied.Length != 16 || copied[0] != 5 || copied[4] != 6 || copied[8] != 9 || copied[12] != 10)
        {
            throw new InvalidOperationException("Screen selection crop self test failed.");
        }
    }

    private static void AssertGpuExport()
    {
        using (WicImageDocument image = WicImageDocument.CreateSynthetic(128, 96))
        {
            List<AnnotationItem> items = CreateBenchmarkAnnotations(128, 96);
            byte[] pixels = GpuRenderer.RenderExport(image, items);
            if (pixels == null || pixels.Length != 128 * 96 * 4)
            {
                throw new InvalidOperationException("GPU export self test produced invalid pixels.");
            }
            bool anyAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    anyAlpha = true;
                    break;
                }
            }
            if (!anyAlpha)
            {
                throw new InvalidOperationException("GPU export self test produced transparent output.");
            }
        }
    }

    private static void AssertDirectWriteTextLayout()
    {
        GpuRect bounds = GpuRenderer.MeasureTextBounds("GPU\u6587\u5b57", 24f);
        if (bounds.Width <= 20f || bounds.Height <= 10f)
        {
            throw new InvalidOperationException("DirectWrite measurement self test failed.");
        }

        TextHitResult hit = GpuRenderer.HitTestText("GPU\u6587\u5b57", 24f, new GpuPoint(4f, 8f));
        if (!hit.IsInside || hit.TextPosition < 0)
        {
            throw new InvalidOperationException("DirectWrite hit-test self test failed.");
        }
    }

    private static void AssertGpuRendererRebuild()
    {
        using (WicImageDocument image = WicImageDocument.CreateSynthetic(96, 64))
        {
            List<AnnotationItem> annotations = CreateBenchmarkAnnotations(96, 64);
            using (GpuRenderer renderer = GpuRenderer.CreateForWindow(image))
            using (SelfTestRenderSurface surface = SelfTestRenderSurface.Create(96, 64))
            {
                renderer.RenderToHdc(surface.Hdc, 96, 64, new GpuRect(0, 0, 96, 64), annotations, null, -1, false, ToolMode.Rect, AppStyles.DefaultStroke, AppStyles.DefaultStrokeWidth, null);
            }
            using (GpuRenderer rebuilt = GpuRenderer.CreateForWindow(image))
            using (SelfTestRenderSurface surface = SelfTestRenderSurface.Create(96, 64))
            {
                rebuilt.RenderToHdc(surface.Hdc, 96, 64, new GpuRect(0, 0, 96, 64), annotations, null, -1, false, ToolMode.Rect, AppStyles.DefaultStroke, AppStyles.DefaultStrokeWidth, null);
            }
        }
    }

    private static void AssertGpuWindowChromeRender()
    {
        using (WicImageDocument image = WicImageDocument.CreateSynthetic(320, 180))
        {
            using (SelfTestRenderSurface surface = SelfTestRenderSurface.Create(640, 420))
            {
                List<AnnotationItem> annotations = CreateBenchmarkAnnotations(320, 180);
                AnnotationItem preview = new AnnotationItem
                {
                    Tool = ToolMode.Rect,
                    Start = new GpuPoint(18, 18),
                    End = new GpuPoint(128, 86),
                    Stroke = AppStyles.Palette[0],
                    StrokeWidth = 6f
                };
                SettingsOverlayState overlay = new SettingsOverlayState();
                overlay.Tooltip = "\u5f53\u524d\u540e\u7aef: GPU/Direct2D";
                overlay.TooltipPoint = new GpuPoint(460, 90);
                using (GpuRenderer renderer = GpuRenderer.CreateForWindow(image))
                {
                    GpuRect view = new GpuRect(42, 126, 512, 288);
                    renderer.RenderToHdc(surface.Hdc, 640, 420, view, annotations, preview, 0, true, ToolMode.Rect, AppStyles.Palette[0], 6f, overlay);
                    overlay.Visible = true;
                    overlay.OutputDirectory = Path.GetTempPath();
                    renderer.RenderToHdc(surface.Hdc, 640, 420, view, annotations, preview, 0, true, ToolMode.Rect, AppStyles.Palette[0], 6f, overlay);
                }
            }
        }
    }

    private static void AssertGpuInteractionSemantics()
    {
        AnnotationItem arrow = new AnnotationItem
        {
            Tool = ToolMode.Arrow,
            Start = new GpuPoint(0, 0),
            End = new GpuPoint(100, 0),
            Stroke = AppStyles.Palette[0],
            StrokeWidth = 10f
        };
        GpuPoint[] arrowPoints = GpuRenderer.BuildArrow(arrow, 10f);
        if (arrowPoints.Length != 7 ||
            Math.Abs(arrowPoints[0].Y + 2.2f) > 0.05f ||
            Math.Abs(arrowPoints[2].Y + 24f) > 0.05f ||
            Math.Abs(arrowPoints[3].X - 100f) > 0.05f)
        {
            throw new InvalidOperationException("GPU arrow geometry self test failed.");
        }

        using (WicImageDocument image = WicImageDocument.CreateSynthetic(80, 60))
        {
            GpuAnnotatorWindow window = new GpuAnnotatorWindow("selftest.png", image, null, null, false);
            Type type = typeof(GpuAnnotatorWindow);
            System.Reflection.BindingFlags instanceFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            System.Reflection.BindingFlags staticFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            System.Reflection.MethodInfo add = type.GetMethod("AddAnnotation", instanceFlags);
            System.Reflection.MethodInfo delete = type.GetMethod("DeleteSelectedAnnotation", instanceFlags);
            System.Reflection.MethodInfo undo = type.GetMethod("UndoAnnotationAction", instanceFlags);
            System.Reflection.MethodInfo capture = type.GetMethod("CaptureAnnotation", staticFlags);
            System.Reflection.MethodInfo record = type.GetMethod("RecordTransformUndo", instanceFlags);
            System.Reflection.MethodInfo cursorForHandle = type.GetMethod("GetCursorForSelectionHandle", staticFlags);
            System.Reflection.MethodInfo applyResize = type.GetMethod("ApplyResizeToAnnotation", staticFlags);
            System.Reflection.MethodInfo beginText = type.GetMethod("BeginInlineTextEdit", instanceFlags);
            System.Reflection.MethodInfo beginExistingText = type.GetMethod("BeginExistingTextEdit", instanceFlags);
            System.Reflection.MethodInfo replaceText = type.GetMethod("ReplaceInlineSelection", instanceFlags);
            System.Reflection.MethodInfo moveCaret = type.GetMethod("MoveInlineCaret", instanceFlags);
            System.Reflection.MethodInfo selectAllText = type.GetMethod("SelectAllInlineText", instanceFlags);
            System.Reflection.MethodInfo deleteText = type.GetMethod("DeleteInlineText", instanceFlags);
            System.Reflection.MethodInfo commitText = type.GetMethod("CommitTextIfNeeded", instanceFlags);
            System.Reflection.MethodInfo applyStyle = type.GetMethod("ApplyActiveStyle", instanceFlags);
            System.Reflection.FieldInfo itemsField = type.GetField("items", instanceFlags);
            System.Reflection.FieldInfo inlineTextField = type.GetField("inlineText", instanceFlags);
            System.Reflection.FieldInfo inlineCaretField = type.GetField("inlineCaretIndex", instanceFlags);
            if (add == null || delete == null || undo == null || capture == null || record == null ||
                cursorForHandle == null || applyResize == null || beginText == null || beginExistingText == null || replaceText == null || moveCaret == null ||
                selectAllText == null || deleteText == null || commitText == null || applyStyle == null ||
                itemsField == null || inlineTextField == null || inlineCaretField == null)
            {
                throw new InvalidOperationException("GPU interaction self test cannot find helpers.");
            }

            if ((int)cursorForHandle.Invoke(null, new object[] { SelectionHandle.TopLeft }) != Win32Api.IdcSizeNwSe ||
                (int)cursorForHandle.Invoke(null, new object[] { SelectionHandle.TopRight }) != Win32Api.IdcSizeNeSw ||
                (int)cursorForHandle.Invoke(null, new object[] { SelectionHandle.Top }) != Win32Api.IdcSizeNs ||
                (int)cursorForHandle.Invoke(null, new object[] { SelectionHandle.Left }) != Win32Api.IdcSizeWe)
            {
                throw new InvalidOperationException("GPU resize cursor self test failed.");
            }

            AnnotationItem resizeArrow = new AnnotationItem
            {
                Tool = ToolMode.Arrow,
                Start = new GpuPoint(2, 3),
                End = new GpuPoint(30, 40),
                Stroke = AppStyles.Palette[0],
                StrokeWidth = 5f
            };
            object resizeArrowBefore = capture.Invoke(null, new object[] { resizeArrow });
            applyResize.Invoke(null, new object[] { resizeArrow, resizeArrowBefore, GpuRect.Normalize(resizeArrow.Start, resizeArrow.End), SelectionHandle.ArrowEnd, new GpuPoint(7, -6) });
            if (Math.Abs(resizeArrow.Start.X - 2f) > 0.01f || Math.Abs(resizeArrow.Start.Y - 3f) > 0.01f ||
                Math.Abs(resizeArrow.End.X - 37f) > 0.01f || Math.Abs(resizeArrow.End.Y - 34f) > 0.01f)
            {
                throw new InvalidOperationException("GPU arrow end handle resize self test failed.");
            }
            applyResize.Invoke(null, new object[] { resizeArrow, resizeArrowBefore, GpuRect.Normalize(resizeArrow.Start, resizeArrow.End), SelectionHandle.ArrowStart, new GpuPoint(-4, 8) });
            if (Math.Abs(resizeArrow.Start.X + 2f) > 0.01f || Math.Abs(resizeArrow.Start.Y - 11f) > 0.01f ||
                Math.Abs(resizeArrow.End.X - 30f) > 0.01f || Math.Abs(resizeArrow.End.Y - 40f) > 0.01f)
            {
                throw new InvalidOperationException("GPU arrow start handle resize self test failed.");
            }

            AnnotationItem rect = new AnnotationItem
            {
                Tool = ToolMode.Rect,
                Start = new GpuPoint(10, 10),
                End = new GpuPoint(30, 30),
                Stroke = AppStyles.Palette[0],
                StrokeWidth = 5f
            };
            add.Invoke(window, new object[] { rect });
            List<AnnotationItem> items = (List<AnnotationItem>)itemsField.GetValue(window);
            if (items.Count != 1)
            {
                throw new InvalidOperationException("GPU add selection self test failed.");
            }

            object before = capture.Invoke(null, new object[] { rect });
            rect.Translate(5f, 7f);
            object after = capture.Invoke(null, new object[] { rect });
            record.Invoke(window, new object[] { rect, 0, before, after });
            undo.Invoke(window, null);
            if (Math.Abs(rect.Start.X - 10f) > 0.01f || Math.Abs(rect.Start.Y - 10f) > 0.01f)
            {
                throw new InvalidOperationException("GPU transform undo self test failed.");
            }

            bool deleted = (bool)delete.Invoke(window, null);
            if (!deleted || items.Count != 0)
            {
                throw new InvalidOperationException("GPU delete self test failed.");
            }
            undo.Invoke(window, null);
            if (items.Count != 1)
            {
                throw new InvalidOperationException("GPU delete undo self test failed.");
            }
            undo.Invoke(window, null);
            if (items.Count != 0)
            {
                throw new InvalidOperationException("GPU add undo self test failed.");
            }

            beginText.Invoke(window, new object[] { new GpuPoint(4, 4) });
            replaceText.Invoke(window, new object[] { "abc" });
            moveCaret.Invoke(window, new object[] { 1, false });
            replaceText.Invoke(window, new object[] { "X" });
            if ((string)inlineTextField.GetValue(window) != "aXbc" || (int)inlineCaretField.GetValue(window) != 2)
            {
                throw new InvalidOperationException("GPU text caret insert self test failed.");
            }
            selectAllText.Invoke(window, null);
            replaceText.Invoke(window, new object[] { "\u6587" });
            if ((string)inlineTextField.GetValue(window) != "\u6587")
            {
                throw new InvalidOperationException("GPU text selection replace self test failed.");
            }
            selectAllText.Invoke(window, null);
            deleteText.Invoke(window, null);
            if (!string.IsNullOrEmpty((string)inlineTextField.GetValue(window)))
            {
                throw new InvalidOperationException("GPU text selection delete self test failed.");
            }
            commitText.Invoke(window, null);

            AnnotationItem styled = new AnnotationItem
            {
                Tool = ToolMode.Rect,
                Start = new GpuPoint(8, 8),
                End = new GpuPoint(28, 28),
                Stroke = AppStyles.Palette[0],
                StrokeWidth = 4f
            };
            add.Invoke(window, new object[] { styled });
            applyStyle.Invoke(window, new object[] { AppStyles.Palette[1], 9f, true, true });
            if (styled.Stroke.Packed != AppStyles.Palette[1].Packed || Math.Abs(styled.StrokeWidth - 9f) > 0.01f)
            {
                throw new InvalidOperationException("GPU selected annotation style self test failed.");
            }
            undo.Invoke(window, null);
            if (styled.Stroke.Packed != AppStyles.Palette[0].Packed || Math.Abs(styled.StrokeWidth - 4f) > 0.01f)
            {
                throw new InvalidOperationException("GPU selected annotation style undo self test failed.");
            }

            AnnotationItem textItem = new AnnotationItem
            {
                Tool = ToolMode.Text,
                Start = new GpuPoint(6, 6),
                Stroke = AppStyles.Palette[0],
                StrokeWidth = 4f,
                Text = "hello"
            };
            add.Invoke(window, new object[] { textItem });
            int textIndex = items.IndexOf(textItem);
            beginExistingText.Invoke(window, new object[] { textIndex, new GpuPoint(8, 8) });
            selectAllText.Invoke(window, null);
            replaceText.Invoke(window, new object[] { "hi" });
            commitText.Invoke(window, null);
            if (textItem.Text != "hi")
            {
                throw new InvalidOperationException("GPU existing text edit self test failed.");
            }
            undo.Invoke(window, null);
            if (textItem.Text != "hello")
            {
                throw new InvalidOperationException("GPU existing text edit undo self test failed.");
            }
        }
    }

    private static void AssertAppFeatureSemantics()
    {
        ToolbarCommand command;
        if (!AppShortcuts.TryGetToolbarCommand(Win32Api.VkD1, false, false, out command) || command != ToolbarCommand.ToolRect ||
            !AppShortcuts.TryGetToolbarCommand(Win32Api.VkD6, false, false, out command) || command != ToolbarCommand.ToolText ||
            !AppShortcuts.TryGetToolbarCommand(Win32Api.VkF, true, false, out command) || command != ToolbarCommand.Fit ||
            !AppShortcuts.TryGetToolbarCommand(Win32Api.VkO, true, false, out command) || command != ToolbarCommand.Ocr ||
            !AppShortcuts.TryGetToolbarCommand(Win32Api.VkS, true, false, out command) || command != ToolbarCommand.Save ||
            AppShortcuts.TryGetToolbarCommand(Win32Api.VkD1, false, true, out command))
        {
            throw new InvalidOperationException("Shortcut mapping self test failed.");
        }

        string tooltip = AppShortcuts.GetTooltip(ToolbarCommand.Save);
        if (tooltip.IndexOf("Ctrl+S", StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException("Shortcut tooltip self test failed.");
        }
        if (!string.Equals(AppFeatures.GetGlobalHotkeyText(new AppSettings()), "Alt+A", StringComparison.Ordinal) ||
            !string.Equals(AppFeatures.GetGlobalHotkeyText(new AppSettings { GlobalHotkeyModifiers = Win32Api.HotkeyModControl | Win32Api.HotkeyModShift, GlobalHotkeyKey = Win32Api.VkD2 }), "Ctrl+Shift+2", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Global hotkey text self test failed.");
        }
        if (BackgroundTrayIcon.GetTooltipForSelfTest().IndexOf(AppFeatures.GlobalHotkeyText, StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException("Tray tooltip self test failed.");
        }
        AppSettings traySettings = new AppSettings();
        if (!string.Equals(BackgroundTrayIcon.ResolveOutputDirectoryForSelfTest(traySettings), Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Tray output directory fallback self test failed.");
        }
        traySettings.OutputDirectory = Path.Combine(Path.GetTempPath(), "quicker-annotated");
        if (!string.Equals(BackgroundTrayIcon.ResolveScreenshotDirectoryForSelfTest(traySettings), traySettings.OutputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Tray screenshot directory fallback self test failed.");
        }
        traySettings.ScreenshotDirectory = Path.Combine(Path.GetTempPath(), "quicker-screenshots");
        if (!string.Equals(BackgroundTrayIcon.ResolveScreenshotDirectoryForSelfTest(traySettings), traySettings.ScreenshotDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Tray screenshot directory self test failed.");
        }

        string exe = Path.Combine(Path.GetTempPath(), "AnnotatorApp.exe");
        string commandLine = AppFeatures.BuildAutoStartCommand(exe);
        if (!AppFeatures.IsAutoStartCommand(commandLine, exe) ||
            AppFeatures.IsAutoStartCommand(AppFeatures.QuoteArgument(exe) + " --screenshot", exe))
        {
            throw new InvalidOperationException("Startup command self test failed.");
        }

        string[] filtered = GetInputArgs(new string[]
        {
            "--screenshot",
            "--background",
            "--settings",
            "sample.png"
        });
        if (filtered.Length != 1 || !string.Equals(filtered[0], "sample.png", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Command-line option filtering self test failed.");
        }

        if (!IsOptionPresent(new string[] { "--screenshot" }, "--screenshot") ||
            !IsOptionPresent(new string[] { "--background" }, "--background") ||
            !IsOptionPresent(new string[] { "--settings" }, "--settings") ||
            IsOptionPresent(new string[] { "/screenshot" }, "--screenshot") ||
            IsOptionPresent(new string[] { "-background" }, "--background"))
        {
            throw new InvalidOperationException("Command-line option matching self test failed.");
        }

        Win32Api.NativeRect sizeRegion = new Win32Api.NativeRect();
        sizeRegion.left = 3;
        sizeRegion.top = 4;
        sizeRegion.right = 23;
        sizeRegion.bottom = 14;
        if (!string.Equals(ScreenSelectionWindow.GetRegionSizeTextForSelfTest(sizeRegion), "20 x 10", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Screen selection HUD self test failed.");
        }
        Win32Api.NativeRect magnifier = ScreenSelectionWindow.GetMagnifierRectForSelfTest(315, 235, 320, 240);
        if (magnifier.left < 0 || magnifier.top < 0 || magnifier.right > 320 || magnifier.bottom > 240 || magnifier.right - magnifier.left != 136 || magnifier.bottom - magnifier.top != 136)
        {
            throw new InvalidOperationException("Screen selection magnifier self test failed.");
        }
    }

    private static void AssertOcrSemantics()
    {
        List<string> lines = new List<string>();
        lines.Add("Hello");
        lines.Add("world.");
        lines.Add("\u4e0b\u4e00\u884c");
        lines.Add("\u7ee7\u7eed");

        string lineText = OcrTextFormatter.Format(lines, OcrTextLayout.Lines);
        if (lineText.IndexOf(Environment.NewLine, StringComparison.Ordinal) <= 0)
        {
            throw new InvalidOperationException("OCR line layout self test failed.");
        }

        string paragraph = OcrTextFormatter.Format(lines, OcrTextLayout.SmartParagraph);
        string expectedParagraph = "Hello world." + Environment.NewLine + Environment.NewLine + "\u4e0b\u4e00\u884c\u7ee7\u7eed";
        if (!string.Equals(paragraph, expectedParagraph, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OCR smart paragraph layout self test failed.");
        }

        List<string> hyphenLines = new List<string>();
        hyphenLines.Add("inter-");
        hyphenLines.Add("national");
        if (!string.Equals(OcrTextFormatter.Format(hyphenLines, OcrTextLayout.SmartParagraph), "international", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OCR smart paragraph hyphen self test failed.");
        }

        string translated = GoogleTranslateClient.ParseTranslatedTextForSelfTest("[[[\"\u4f60\u597d\",\"Hello\",null,null,10],[\"\uff0c\u4e16\u754c\",\" world\",null,null,10]],null,\"en\"]");
        if (!string.Equals(translated, "\u4f60\u597d\uff0c\u4e16\u754c", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Google translate parser self test failed.");
        }

        string protectedSecret = AppSettingsStore.ProtectSecretForSelfTest("secret-for-self-test");
        if (string.IsNullOrEmpty(protectedSecret) ||
            string.Equals(protectedSecret, "secret-for-self-test", StringComparison.Ordinal) ||
            !string.Equals(AppSettingsStore.UnprotectSecretForSelfTest(protectedSecret), "secret-for-self-test", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OCR secret protection self test failed.");
        }

        if (BaiduOcrClient.NormalizeEngine(OcrEngineKind.Accurate) != OcrEngineKind.Accurate ||
            OcrTextFormatter.NormalizeLayout((OcrTextLayout)999) != OcrTextLayout.SmartParagraph ||
            AppShortcuts.GetTooltip(ToolbarCommand.Ocr).IndexOf("Ctrl+O", StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException("OCR settings semantics self test failed.");
        }
    }

    private static void AssertClipboardWorkflow()
    {
        using (WicImageDocument image = WicImageDocument.CreateSynthetic(24, 16))
        {
            string pngPath = Path.Combine(Path.GetTempPath(), "quicker-gpu-clipboard-" + Guid.NewGuid().ToString("N") + ".png");
            image.Save(pngPath);
            byte[] pngBytes = File.ReadAllBytes(pngPath);

            ClipboardBridge.SetPngBytesForSelfTest(pngBytes);
            string fromPng = ClipboardBridge.TryGetClipboardImagePath();
            using (WicImageDocument loaded = WicImageDocument.Load(fromPng))
            {
                if (loaded.Width != 24 || loaded.Height != 16)
                {
                    throw new InvalidOperationException("PNG clipboard self test failed.");
                }
            }

            ClipboardBridge.SetDibForSelfTest(image.Width, image.Height, image.Pixels);
            string fromDib = ClipboardBridge.TryGetClipboardImagePath();
            using (WicImageDocument loaded = WicImageDocument.Load(fromDib))
            {
                if (loaded.Width != 24 || loaded.Height != 16)
                {
                    throw new InvalidOperationException("DIB clipboard self test failed.");
                }
            }

            ClipboardBridge.SetImage(IntPtr.Zero, pngPath, image.Width, image.Height, image.Pixels);
            string fromFileDrop = ClipboardBridge.TryGetClipboardImagePath();
            using (WicImageDocument loaded = WicImageDocument.Load(fromFileDrop))
            {
                if (loaded.Width != 24 || loaded.Height != 16)
                {
                    throw new InvalidOperationException("clipboard output self test failed.");
                }
            }
        }
    }

    private static void AssertClipboardStartupSmoke()
    {
        using (WicImageDocument image = WicImageDocument.CreateSynthetic(96, 64))
        {
            string pngPath = Path.Combine(Path.GetTempPath(), "quicker-gpu-startup-" + Guid.NewGuid().ToString("N") + ".png");
            image.Save(pngPath);
            byte[] pngBytes = File.ReadAllBytes(pngPath);
            ClipboardBridge.SetPngBytesForSelfTest(pngBytes);

            Stopwatch sw = Stopwatch.StartNew();
            string startupPath = GetInputImagePath(new string[0]);
            using (WicImageDocument loaded = WicImageDocument.Load(startupPath))
            {
                sw.Stop();
                if (loaded.Width != 96 || loaded.Height != 64)
                {
                    throw new InvalidOperationException("Clipboard startup smoke loaded an unexpected image.");
                }
            }

            double elapsedMs = sw.Elapsed.TotalMilliseconds;
            AppLog.Info("Clipboard startup smoke: " + elapsedMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " ms");
            if (elapsedMs > 1500d)
            {
                throw new InvalidOperationException("Clipboard startup smoke exceeded 1500 ms.");
            }
        }
    }

    private static void AssertBenchmarkSmoke()
    {
        BenchmarkResult result = RunBenchmarkCase(new BenchmarkCase("smoke", 96, 64, 96, 64), 1);
        if (result.Max <= 0)
        {
            throw new InvalidOperationException("Benchmark smoke self test failed.");
        }
    }

    private sealed class SelfTestRenderSurface : IDisposable
    {
        private readonly IntPtr screenDc;
        private readonly IntPtr memoryDc;
        private readonly IntPtr surfaceBits;
        private readonly IntPtr oldObject;
        private bool disposed;

        private SelfTestRenderSurface(IntPtr screenDc, IntPtr memoryDc, IntPtr surfaceBits, IntPtr oldObject)
        {
            this.screenDc = screenDc;
            this.memoryDc = memoryDc;
            this.surfaceBits = surfaceBits;
            this.oldObject = oldObject;
        }

        public IntPtr Hdc
        {
            get { return memoryDc; }
        }

        public static SelfTestRenderSurface Create(int width, int height)
        {
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr surfaceBits = IntPtr.Zero;
            IntPtr oldObject = IntPtr.Zero;
            try
            {
                screenDc = Win32Api.GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Self-test surface could not acquire screen DC.");
                }
                memoryDc = Win32Api.CreateCompatibleDC(screenDc);
                if (memoryDc == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Self-test surface could not create memory DC.");
                }
                surfaceBits = Win32Api.CreateCompatibleSurface(screenDc, width, height);
                if (surfaceBits == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Self-test surface could not create backing surface.");
                }
                oldObject = Win32Api.SelectObject(memoryDc, surfaceBits);
                if (oldObject == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Self-test surface could not bind backing surface.");
                }
                return new SelfTestRenderSurface(screenDc, memoryDc, surfaceBits, oldObject);
            }
            catch
            {
                if (oldObject != IntPtr.Zero)
                {
                    Win32Api.SelectObject(memoryDc, oldObject);
                }
                if (surfaceBits != IntPtr.Zero)
                {
                    Win32Api.DeleteObject(surfaceBits);
                }
                if (memoryDc != IntPtr.Zero)
                {
                    Win32Api.DeleteDC(memoryDc);
                }
                if (screenDc != IntPtr.Zero)
                {
                    Win32Api.ReleaseDC(IntPtr.Zero, screenDc);
                }
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (memoryDc != IntPtr.Zero && oldObject != IntPtr.Zero)
            {
                Win32Api.SelectObject(memoryDc, oldObject);
            }
            if (surfaceBits != IntPtr.Zero)
            {
                Win32Api.DeleteObject(surfaceBits);
            }
            if (memoryDc != IntPtr.Zero)
            {
                Win32Api.DeleteDC(memoryDc);
            }
            if (screenDc != IntPtr.Zero)
            {
                Win32Api.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    private static bool IsOption(string arg, string option)
    {
        return string.Equals(arg, option, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteFailure(Exception exception)
    {
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "QuickerImageAnnotator-selftest-error.log"), exception.ToString());
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private struct BenchmarkCase
    {
        public readonly string Name;
        public readonly int Width;
        public readonly int Height;
        public readonly int CanvasWidth;
        public readonly int CanvasHeight;

        public BenchmarkCase(string name, int width, int height, int canvasWidth, int canvasHeight)
        {
            Name = name;
            Width = width;
            Height = height;
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
        }
    }

    private struct BenchmarkResult
    {
        public double Avg;
        public double P50;
        public double P95;
        public double Max;
    }
}
