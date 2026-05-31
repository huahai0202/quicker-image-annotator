using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

internal static class Program
{
    private const string OutputDirectoryEnvironmentVariable = "QUICKER_ANNOTATOR_OUTPUT_DIR";
    private const string RenderProfileEnvironmentVariable = "QUICKER_ANNOTATOR_RENDER_PROFILE";
    private const string RenderProfileLogEnvironmentVariable = "QUICKER_ANNOTATOR_RENDER_PROFILE_LOG";

    [STAThread]
    private static int Main(string[] args)
    {
        bool selfTest = args.Length > 0 && string.Equals(args[0], "-SelfTest", StringComparison.OrdinalIgnoreCase);
        bool benchmark = IsBenchmarkRequested(args);
        try
        {
            Win32Api.CoInitializeEx(IntPtr.Zero, 2);
            Win32Api.SetProcessDPIAware();
            if (selfTest)
            {
                RunSelfTest();
                return 0;
            }
            ConfigureRenderPerformanceProbe(args);
            if (benchmark)
            {
                RunBenchmark(args);
                return 0;
            }

            string outputDirectory = GetConfiguredOutputDirectory(args);
            string[] inputArgs = GetInputArgs(args);
            string inputPath = GetInputImagePath(inputArgs);
            using (WicImageDocument image = WicImageDocument.Load(inputPath))
            using (GpuAnnotatorWindow window = new GpuAnnotatorWindow(inputPath, image, outputDirectory))
            {
                return window.Run();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Application failed.", ex);
            if (selfTest || benchmark)
            {
                WriteFailure(ex);
                return 1;
            }
            Win32Api.MessageBoxUnicode(IntPtr.Zero, ex.Message, UiText.AppName, Win32Api.MbOk | Win32Api.MbIconError);
            return 1;
        }
        finally
        {
            RenderPerformanceProbe.FlushSummary("shutdown");
            Win32Api.CoUninitialize();
        }
    }

    private static string GetInputImagePath(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(args[0].Trim('"')));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Image file does not exist.", path);
            }
            return path;
        }

        string clipboardFile = ClipboardBridge.TryGetClipboardImagePath();
        if (!string.IsNullOrEmpty(clipboardFile))
        {
            return clipboardFile;
        }

        throw new InvalidOperationException("No image found. Copy an image file first, or pass an image path as the first argument.");
    }

    private static string GetConfiguredOutputDirectory(string[] args)
    {
        string outputDirectory = AppSettingsStore.Load().OutputDirectory;
        string environmentDirectory = Environment.GetEnvironmentVariable(OutputDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentDirectory))
        {
            outputDirectory = environmentDirectory;
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string value;
            if (TryGetInlineOption(arg, "--output-dir", out value))
            {
                outputDirectory = value;
            }
            else if (IsOption(arg, "--output-dir"))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("--output-dir requires a directory path.");
                }
                outputDirectory = args[++i];
            }
            else if (IsOption(arg, "--render-profile-log"))
            {
                i++;
            }
            else if (IsOption(arg, "--render-benchmark-log"))
            {
                i++;
            }
            else if (IsOption(arg, "--render-benchmark-frames"))
            {
                i++;
            }
        }

        return AppSettingsStore.NormalizeDirectory(outputDirectory);
    }

    private static string[] GetInputArgs(string[] args)
    {
        List<string> input = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string value;
            if (TryGetInlineOption(args[i], "--output-dir", out value) ||
                TryGetInlineOption(args[i], "--render-profile", out value) ||
                TryGetInlineOption(args[i], "--render-profile-log", out value) ||
                TryGetInlineOption(args[i], "--render-benchmark-log", out value) ||
                TryGetInlineOption(args[i], "--render-benchmark-frames", out value))
            {
                continue;
            }
            if (IsOption(args[i], "--output-dir") ||
                IsOption(args[i], "--render-profile-log") ||
                IsOption(args[i], "--render-benchmark-log") ||
                IsOption(args[i], "--render-benchmark-frames"))
            {
                i++;
                continue;
            }
            if (IsOption(args[i], "--render-profile") || IsOption(args[i], "--render-benchmark"))
            {
                continue;
            }
            input.Add(args[i]);
        }
        return input.ToArray();
    }

    private static void ConfigureRenderPerformanceProbe(string[] args)
    {
        bool enabled = IsTruthy(Environment.GetEnvironmentVariable(RenderProfileEnvironmentVariable));
        string logPath = Environment.GetEnvironmentVariable(RenderProfileLogEnvironmentVariable);
        for (int i = 0; i < args.Length; i++)
        {
            string value;
            if (IsOption(args[i], "--render-profile"))
            {
                enabled = true;
            }
            else if (TryGetInlineOption(args[i], "--render-profile", out value))
            {
                enabled = IsTruthy(value);
            }
            else if (TryGetInlineOption(args[i], "--render-profile-log", out value))
            {
                logPath = value;
            }
            else if (IsOption(args[i], "--render-profile-log") && i + 1 < args.Length)
            {
                logPath = args[++i];
            }
        }
        RenderPerformanceProbe.Configure(enabled, logPath);
    }

    private static bool IsBenchmarkRequested(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (IsOption(args[i], "--render-benchmark"))
            {
                return true;
            }
        }
        return false;
    }

    private static void RunBenchmark(string[] args)
    {
        int frames = 60;
        string logPath = Path.Combine(Path.GetTempPath(), "QuickerImageAnnotator-render-benchmark.md");
        for (int i = 0; i < args.Length; i++)
        {
            string value;
            if (TryGetInlineOption(args[i], "--render-benchmark-frames", out value))
            {
                int.TryParse(value, out frames);
            }
            else if (IsOption(args[i], "--render-benchmark-frames") && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out frames);
            }
            else if (TryGetInlineOption(args[i], "--render-benchmark-log", out value))
            {
                logPath = value;
            }
            else if (IsOption(args[i], "--render-benchmark-log") && i + 1 < args.Length)
            {
                logPath = args[++i];
            }
        }
        frames = Math.Max(1, frames);

        BenchmarkCase[] cases = new BenchmarkCase[]
        {
            new BenchmarkCase("1080p", 1920, 1080, 1280, 720),
            new BenchmarkCase("4k", 3840, 2160, 1600, 900),
            new BenchmarkCase("8k", 7680, 4320, 1600, 900),
            new BenchmarkCase("long-shot", 1440, 6400, 900, 1400)
        };

        StringBuilder report = new StringBuilder();
        report.AppendLine("# Render Benchmark");
        report.AppendLine();
        report.AppendLine("| case | image | canvas | actual_backend | hardware_accelerated | avg_ms | p50_ms | p95_ms | max_ms |");
        report.AppendLine("| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: |");
        for (int i = 0; i < cases.Length; i++)
        {
            BenchmarkResult result = RunBenchmarkCase(cases[i], frames);
            report.Append("| ").Append(cases[i].Name)
                .Append(" | ").Append(cases[i].Width).Append("x").Append(cases[i].Height)
                .Append(" | ").Append(cases[i].CanvasWidth).Append("x").Append(cases[i].CanvasHeight)
                .Append(" | GpuRenderer | yes | ")
                .Append(result.Avg.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(" | ")
                .Append(result.P50.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(" | ")
                .Append(result.P95.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(" | ")
                .Append(result.Max.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" |");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath)));
        File.WriteAllText(logPath, report.ToString(), new UTF8Encoding(false));
    }

    private static BenchmarkResult RunBenchmarkCase(BenchmarkCase benchmarkCase, int frames)
    {
        using (WicImageDocument image = WicImageDocument.CreateSynthetic(benchmarkCase.Width, benchmarkCase.Height))
        {
            List<AnnotationItem> annotations = CreateBenchmarkAnnotations(benchmarkCase.Width, benchmarkCase.Height);
            double[] samples = new double[frames];
            GpuAnnotatorWindow.EnsureWindowClass();
            IntPtr hwnd = Win32Api.CreateWindowEx(
                0,
                GpuAnnotatorWindow.RegisteredClassName,
                "benchmark",
                Win32Api.WsOverlappedWindow,
                0,
                0,
                benchmarkCase.CanvasWidth,
                benchmarkCase.CanvasHeight,
                IntPtr.Zero,
                IntPtr.Zero,
                Win32Api.GetModuleHandle(null),
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Benchmark window creation failed.");
            }
            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = Win32Api.GetDC(hwnd);
                using (GpuRenderer renderer = GpuRenderer.CreateForWindow(image))
                {
                    GpuRect view = GetBenchmarkView(benchmarkCase);
                    for (int i = 0; i < frames; i++)
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        renderer.RenderToHdc(hdc, benchmarkCase.CanvasWidth, benchmarkCase.CanvasHeight, view, annotations, null, -1, false, ToolMode.Rect, AppStyles.DefaultStroke, AppStyles.DefaultStrokeWidth, null);
                        sw.Stop();
                        samples[i] = sw.Elapsed.TotalMilliseconds;
                    }
                }
            }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    Win32Api.ReleaseDC(hwnd, hdc);
                }
                Win32Api.DestroyWindow(hwnd);
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
        AssertGpuExport();
        AssertDirectWriteTextLayout();
        AssertGpuRendererRebuild();
        AssertGpuWindowChromeRender();
        AssertGpuInteractionSemantics();
        AssertClipboardWorkflow();
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
            GpuAnnotatorWindow.EnsureWindowClass();
            IntPtr hwnd = Win32Api.CreateWindowEx(
                0,
                GpuAnnotatorWindow.RegisteredClassName,
                "selftest-rebuild",
                Win32Api.WsOverlappedWindow,
                0,
                0,
                96,
                64,
                IntPtr.Zero,
                IntPtr.Zero,
                Win32Api.GetModuleHandle(null),
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Renderer rebuild self test cannot create window.");
            }
            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = Win32Api.GetDC(hwnd);
                List<AnnotationItem> annotations = CreateBenchmarkAnnotations(96, 64);
                using (GpuRenderer renderer = GpuRenderer.CreateForWindow(image))
                {
                    renderer.RenderToHdc(hdc, 96, 64, new GpuRect(0, 0, 96, 64), annotations, null, -1, false, ToolMode.Rect, AppStyles.DefaultStroke, AppStyles.DefaultStrokeWidth, null);
                }
                using (GpuRenderer rebuilt = GpuRenderer.CreateForWindow(image))
                {
                    rebuilt.RenderToHdc(hdc, 96, 64, new GpuRect(0, 0, 96, 64), annotations, null, -1, false, ToolMode.Rect, AppStyles.DefaultStroke, AppStyles.DefaultStrokeWidth, null);
                }
            }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    Win32Api.ReleaseDC(hwnd, hdc);
                }
                Win32Api.DestroyWindow(hwnd);
            }
        }
    }

    private static void AssertGpuWindowChromeRender()
    {
        using (WicImageDocument image = WicImageDocument.CreateSynthetic(320, 180))
        {
            GpuAnnotatorWindow.EnsureWindowClass();
            IntPtr hwnd = Win32Api.CreateWindowEx(
                0,
                GpuAnnotatorWindow.RegisteredClassName,
                "selftest-chrome",
                Win32Api.WsOverlappedWindow,
                0,
                0,
                640,
                420,
                IntPtr.Zero,
                IntPtr.Zero,
                Win32Api.GetModuleHandle(null),
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Window chrome render self test cannot create window.");
            }

            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = Win32Api.GetDC(hwnd);
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
                    renderer.RenderToHdc(hdc, 640, 420, view, annotations, preview, 0, true, ToolMode.Rect, AppStyles.Palette[0], 6f, overlay);
                    overlay.Visible = true;
                    overlay.OutputDirectory = Path.GetTempPath();
                    renderer.RenderToHdc(hdc, 640, 420, view, annotations, preview, 0, true, ToolMode.Rect, AppStyles.Palette[0], 6f, overlay);
                }
            }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    Win32Api.ReleaseDC(hwnd, hdc);
                }
                Win32Api.DestroyWindow(hwnd);
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
            GpuAnnotatorWindow window = new GpuAnnotatorWindow("selftest.png", image, null);
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

    private static void AssertBenchmarkSmoke()
    {
        BenchmarkResult result = RunBenchmarkCase(new BenchmarkCase("smoke", 96, 64, 96, 64), 1);
        if (result.Max <= 0)
        {
            throw new InvalidOperationException("Benchmark smoke self test failed.");
        }
    }

    private static bool TryGetInlineOption(string arg, string option, out string value)
    {
        value = null;
        string[] prefixes = new string[]
        {
            option + "=",
            option + ":",
            "-" + option.TrimStart('-') + "=",
            "-" + option.TrimStart('-') + ":",
            "/" + option.TrimStart('-') + "=",
            "/" + option.TrimStart('-') + ":"
        };
        for (int i = 0; i < prefixes.Length; i++)
        {
            if (arg.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
            {
                value = arg.Substring(prefixes[i].Length);
                return true;
            }
        }
        return false;
    }

    private static bool IsOption(string arg, string option)
    {
        string trimmed = option.TrimStart('-');
        return string.Equals(arg, option, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-" + trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/" + trimmed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTruthy(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        value = value.Trim();
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
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
