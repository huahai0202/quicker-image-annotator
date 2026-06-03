using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

internal static class AppFeatures
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "QuickerImageAnnotator";
    private const string BackgroundArgument = "--background";
    private const string SettingsArgument = "--settings";
    private const string ReleaseUrl = "https://github.com/huahai0202/quicker-image-annotator/releases/latest";
    internal const string BackgroundHotkeyMutexName = @"Local\QuickerImageAnnotatorBackgroundHotkey";
    internal const string BackgroundHotkeyStopEventName = @"Local\QuickerImageAnnotatorBackgroundHotkeyStop";
    public const uint DefaultHotkeyModifiers = Win32Api.HotkeyModAlt;

    public static string GlobalHotkeyText
    {
        get { return GetGlobalHotkeyText(AppSettingsStore.Load()); }
    }

    public static string GetGlobalHotkeyText(AppSettings settings)
    {
        return FormatHotkey(GetGlobalHotkeyModifiers(settings), GetGlobalHotkeyKey(settings));
    }

    public static uint GetGlobalHotkeyModifiers(AppSettings settings)
    {
        return NormalizeHotkeyModifiers(settings == null ? DefaultHotkeyModifiers : settings.GlobalHotkeyModifiers);
    }

    public static int GetGlobalHotkeyKey(AppSettings settings)
    {
        return NormalizeHotkeyKey(settings == null ? Win32Api.VkA : settings.GlobalHotkeyKey);
    }

    public static uint NormalizeHotkeyModifiers(uint modifiers)
    {
        uint supported = Win32Api.HotkeyModAlt | Win32Api.HotkeyModControl | Win32Api.HotkeyModShift;
        modifiers &= supported;
        return modifiers == 0 ? DefaultHotkeyModifiers : modifiers;
    }

    public static int NormalizeHotkeyKey(int key)
    {
        return key <= 0 ? Win32Api.VkA : key;
    }

    public static bool IsUsableHotkey(uint modifiers, int key)
    {
        return NormalizeHotkeyModifiers(modifiers) != 0 && NormalizeHotkeyKey(key) != 0;
    }

    public static void ApplyAutoStart(bool enabled)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            if (key == null)
            {
                throw new InvalidOperationException("Cannot open current user startup registry key.");
            }

            if (enabled)
            {
                key.SetValue(RunValueName, BuildAutoStartCommand(GetExecutablePath()), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(RunValueName, false);
            }
        }
    }

    public static bool IsAutoStartEnabled()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
        {
            string value = key == null ? null : key.GetValue(RunValueName) as string;
            return IsAutoStartCommand(value, GetExecutablePath());
        }
    }

    public static string BuildAutoStartCommand(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new ArgumentException("Executable path cannot be empty.");
        }
        return QuoteArgument(Path.GetFullPath(exePath)) + " " + BackgroundArgument;
    }

    public static bool IsAutoStartCommand(string command, string exePath)
    {
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        string expected = BuildAutoStartCommand(exePath);
        return string.Equals(command.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    public static string QuoteArgument(string value)
    {
        value = value ?? string.Empty;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    public static string GetExecutablePath()
    {
        return Process.GetCurrentProcess().MainModule.FileName;
    }

    public static void StartSettingsWindow()
    {
        try
        {
            StartDetached(SettingsArgument);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to start settings window.", ex);
        }
    }

    public static void OpenReleasePage()
    {
        try
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = ReleaseUrl;
            info.UseShellExecute = true;
            Process.Start(info);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to open release page.", ex);
        }
    }

    public static void ShowAbout(IntPtr owner)
    {
        string buildTime = string.Empty;
        try
        {
            buildTime = File.GetLastWriteTime(GetExecutablePath()).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture);
        }
        catch
        {
        }

        string message = UiText.AppName + Environment.NewLine +
            "Version: " + GetAppVersionText() + Environment.NewLine +
            "Hotkey: " + GlobalHotkeyText + Environment.NewLine +
            (string.IsNullOrEmpty(buildTime) ? string.Empty : "Build: " + buildTime + Environment.NewLine) +
            "Updates: " + ReleaseUrl;
        Win32Api.MessageBoxUnicode(owner, message, UiText.AboutTitle, Win32Api.MbOk);
    }

    public static string GetAppVersionText()
    {
        return "2026.06.03";
    }

    public static bool TryStartBackgroundHotkeyAgent(out string error)
    {
        error = null;
        try
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = GetExecutablePath();
            info.Arguments = BackgroundArgument;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            Process process = Process.Start(info);
            if (process == null)
            {
                error = "Cannot start background hotkey agent.";
                return false;
            }

            using (process)
            {
                if (!process.WaitForExit(700))
                {
                    if (WaitForBackgroundHotkeyAgentStart(1500))
                    {
                        return true;
                    }

                    error = "Background hotkey agent did not report ready.";
                    return false;
                }

                if (process.ExitCode == 0)
                {
                    if (IsBackgroundHotkeyAgentRunning())
                    {
                        return true;
                    }

                    error = "Background hotkey agent stopped before registering.";
                    return false;
                }

                error = "Background hotkey agent exited with code " + process.ExitCode + ".";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Error("Failed to start background hotkey agent.", ex);
            return false;
        }
    }

    public static bool TrySyncBackgroundHotkeyAgent(bool enabled, out string error)
    {
        if (!enabled)
        {
            return TryStopBackgroundHotkeyAgent(out error);
        }

        string stopError;
        if (!TryStopBackgroundHotkeyAgent(out stopError))
        {
            error = stopError;
            return false;
        }

        return TryStartBackgroundHotkeyAgent(out error);
    }

    public static bool TryEnsureBackgroundHotkeyAgent(AppSettings settings, out string error)
    {
        error = null;
        settings = settings ?? AppSettingsStore.Load();
        if (!settings.GlobalHotkeyEnabled)
        {
            return true;
        }
        if (IsBackgroundHotkeyAgentRunning())
        {
            return true;
        }
        return TryStartBackgroundHotkeyAgent(out error);
    }

    public static bool TryStopBackgroundHotkeyAgent(out string error)
    {
        error = null;
        try
        {
            EventWaitHandle stopEvent;
            try
            {
                stopEvent = EventWaitHandle.OpenExisting(BackgroundHotkeyStopEventName);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                if (!IsBackgroundHotkeyAgentRunning())
                {
                    return true;
                }

                error = "Background hotkey stop signal is unavailable.";
                return false;
            }

            using (stopEvent)
            {
                stopEvent.Set();
            }

            if (WaitForBackgroundHotkeyAgentExit(1500))
            {
                return true;
            }

            error = "Background hotkey agent did not exit in time.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Error("Failed to stop background hotkey agent.", ex);
            return false;
        }
    }

    private static bool WaitForBackgroundHotkeyAgentStart(int timeoutMilliseconds)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (IsBackgroundHotkeyAgentRunning())
            {
                return true;
            }
            Thread.Sleep(50);
        }
        return IsBackgroundHotkeyAgentRunning();
    }

    private static bool WaitForBackgroundHotkeyAgentExit(int timeoutMilliseconds)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (!IsBackgroundHotkeyAgentRunning())
            {
                return true;
            }
            Thread.Sleep(50);
        }
        return !IsBackgroundHotkeyAgentRunning();
    }

    private static bool IsBackgroundHotkeyAgentRunning()
    {
        bool created;
        using (Mutex mutex = new Mutex(true, BackgroundHotkeyMutexName, out created))
        {
            if (created)
            {
                mutex.ReleaseMutex();
            }
            return !created;
        }
    }

    private static void StartDetached(string arguments)
    {
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = GetExecutablePath();
        info.Arguments = arguments;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        Process.Start(info);
    }

    private static string FormatHotkey(uint modifiers, int key)
    {
        modifiers = NormalizeHotkeyModifiers(modifiers);
        key = NormalizeHotkeyKey(key);
        var parts = new System.Collections.Generic.List<string>();
        if ((modifiers & Win32Api.HotkeyModControl) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((modifiers & Win32Api.HotkeyModAlt) != 0)
        {
            parts.Add("Alt");
        }
        if ((modifiers & Win32Api.HotkeyModShift) != 0)
        {
            parts.Add("Shift");
        }
        parts.Add(FormatVirtualKey(key));
        return string.Join("+", parts.ToArray());
    }

    private static string FormatVirtualKey(int key)
    {
        if (key >= Win32Api.VkA && key <= Win32Api.VkZ)
        {
            return ((char)key).ToString();
        }
        if (key >= Win32Api.VkD0 && key <= Win32Api.VkD9)
        {
            return ((char)key).ToString();
        }
        if (key >= Win32Api.VkF1 && key <= Win32Api.VkF12)
        {
            return "F" + (key - Win32Api.VkF1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (key == Win32Api.VkSpace)
        {
            return "Space";
        }
        return "VK" + key.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

internal static class BackgroundHotkeyAgent
{
    private const int HotkeyId = 7001;
    private static readonly IntPtr SettingsPollTimerId = new IntPtr(7002);
    private const uint SettingsPollMilliseconds = 100;

    public static int Run()
    {
        bool created;
        using (Mutex mutex = new Mutex(true, AppFeatures.BackgroundHotkeyMutexName, out created))
        {
            if (!created)
            {
                return 0;
            }

            AppSettings settings = AppSettingsStore.Load();
            if (!settings.GlobalHotkeyEnabled)
            {
                return 0;
            }

            uint hotkeyModifiers = AppFeatures.GetGlobalHotkeyModifiers(settings);
            int hotkeyKey = AppFeatures.GetGlobalHotkeyKey(settings);
            if (!Win32Api.RegisterHotKey(IntPtr.Zero, HotkeyId, hotkeyModifiers, (uint)hotkeyKey))
            {
                AppLog.Info("Global hotkey is already in use: " + AppFeatures.GetGlobalHotkeyText(settings));
                return 1;
            }

            using (BackgroundTrayIcon trayIcon = BackgroundTrayIcon.TryCreate())
            using (EventWaitHandle stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppFeatures.BackgroundHotkeyStopEventName))
            {
                Win32Api.SetTimer(IntPtr.Zero, SettingsPollTimerId, SettingsPollMilliseconds, IntPtr.Zero);
                try
                {
                    Win32Api.Msg msg;
                    while (Win32Api.GetMessage(out msg, IntPtr.Zero, 0, 0))
                    {
                        if (ShouldStop(stopEvent))
                        {
                            break;
                        }

                        if ((int)msg.message == Win32Api.WmHotkey && msg.wParam.ToInt32() == HotkeyId)
                        {
                            StartScreenshotAnnotator();
                        }
                        else if ((int)msg.message != Win32Api.WmTimer)
                        {
                            Win32Api.TranslateMessage(ref msg);
                            Win32Api.DispatchMessage(ref msg);
                        }
                    }
                    return 0;
                }
                finally
                {
                    Win32Api.KillTimer(IntPtr.Zero, SettingsPollTimerId);
                    Win32Api.UnregisterHotKey(IntPtr.Zero, HotkeyId);
                }
            }
        }
    }

    public static bool IsTriggerMessage(Win32Api.Msg msg)
    {
        return (int)msg.message == Win32Api.WmHotkey && msg.wParam.ToInt32() == HotkeyId;
    }

    private static bool ShouldStop(EventWaitHandle stopEvent)
    {
        if (stopEvent != null && stopEvent.WaitOne(0))
        {
            return true;
        }
        return !AppSettingsStore.Load().GlobalHotkeyEnabled;
    }

    internal static void StartScreenshotAnnotator()
    {
        try
        {
            if (!AppSettingsStore.Load().GlobalHotkeyEnabled)
            {
                return;
            }

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = AppFeatures.GetExecutablePath();
            info.Arguments = "--screenshot";
            info.UseShellExecute = false;
            Process.Start(info);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to start screenshot annotator.", ex);
        }
    }
}

internal sealed class BackgroundTrayIcon : IDisposable
{
    private const string WindowClassName = "QuickerImageAnnotatorBackgroundTrayWindow";
    private const uint TrayIconId = 1;
    private const int TrayCallbackMessage = Win32Api.WmApp + 701;
    private const int ClickThrottleMilliseconds = 700;
    private const int MenuSettings = 1001;
    private const int MenuOpenOutputDirectory = 1002;
    private const int MenuOpenScreenshotDirectory = 1003;
    private const int MenuCheckUpdates = 1004;
    private const int MenuAbout = 1005;
    private const int MenuExit = 1006;
    private static Win32Api.WindowProc sharedProc;
    private static BackgroundTrayIcon current;
    private readonly IntPtr hwnd;
    private readonly IntPtr icon;
    private readonly bool ownsIcon;
    private bool iconAdded;
    private bool disposed;
    private int lastClickTick = Environment.TickCount - ClickThrottleMilliseconds;

    private BackgroundTrayIcon(IntPtr hwnd, IntPtr icon, bool ownsIcon)
    {
        this.hwnd = hwnd;
        this.icon = icon;
        this.ownsIcon = ownsIcon;
    }

    public static BackgroundTrayIcon TryCreate()
    {
        BackgroundTrayIcon trayIcon = null;
        try
        {
            EnsureWindowClass();
            IntPtr hwnd = Win32Api.CreateWindowEx(
                0,
                WindowClassName,
                UiText.AppName,
                0,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                Win32Api.GetModuleHandle(null),
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateWindowEx failed for tray icon window.");
            }

            bool ownsIcon;
            IntPtr icon = LoadTrayIcon(out ownsIcon);
            trayIcon = new BackgroundTrayIcon(hwnd, icon, ownsIcon);
            current = trayIcon;
            trayIcon.AddIcon();
            return trayIcon;
        }
        catch (Exception ex)
        {
            if (trayIcon != null)
            {
                trayIcon.Dispose();
            }
            AppLog.Error("Failed to create tray icon.", ex);
            return null;
        }
    }

    public static string GetTooltipForSelfTest()
    {
        return BuildTooltip();
    }

    public static string ResolveOutputDirectoryForSelfTest(AppSettings settings)
    {
        return ResolveOutputDirectory(settings);
    }

    public static string ResolveScreenshotDirectoryForSelfTest(AppSettings settings)
    {
        return ResolveScreenshotDirectory(settings);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        if (iconAdded)
        {
            Win32Api.NotifyIconData data = CreateData();
            Win32Api.ShellNotifyIcon(Win32Api.NimDelete, ref data);
            iconAdded = false;
        }

        if (hwnd != IntPtr.Zero)
        {
            Win32Api.DestroyWindow(hwnd);
        }

        if (ownsIcon && icon != IntPtr.Zero)
        {
            Win32Api.DestroyIcon(icon);
        }

        if (current == this)
        {
            current = null;
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
        wc.lpszClassName = WindowClassName;
        ushort atom = Win32Api.RegisterClassEx(ref wc);
        if (atom == 0)
        {
            throw new InvalidOperationException("RegisterClassEx failed for tray icon window.");
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        BackgroundTrayIcon trayIcon = current;
        if (trayIcon != null && trayIcon.hwnd == hwnd && msg == TrayCallbackMessage)
        {
            trayIcon.HandleTrayMessage(lParam.ToInt32());
            return IntPtr.Zero;
        }
        return Win32Api.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void AddIcon()
    {
        Win32Api.NotifyIconData data = CreateData();
        if (!Win32Api.ShellNotifyIcon(Win32Api.NimAdd, ref data))
        {
            throw new InvalidOperationException("Shell_NotifyIcon add failed.");
        }
        iconAdded = true;
    }

    private Win32Api.NotifyIconData CreateData()
    {
        Win32Api.NotifyIconData data = new Win32Api.NotifyIconData();
        data.cbSize = (uint)Marshal.SizeOf(typeof(Win32Api.NotifyIconData));
        data.hWnd = hwnd;
        data.uID = TrayIconId;
        data.uFlags = Win32Api.NifMessage | Win32Api.NifIcon | Win32Api.NifTip;
        data.uCallbackMessage = TrayCallbackMessage;
        data.hIcon = icon;
        data.szTip = BuildTooltip();
        return data;
    }

    private static IntPtr LoadTrayIcon(out bool ownsIcon)
    {
        ownsIcon = false;
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            IntPtr loaded = Win32Api.LoadImage(IntPtr.Zero, iconPath, Win32Api.ImageIcon, 16, 16, Win32Api.LrLoadFromFile);
            if (loaded != IntPtr.Zero)
            {
                ownsIcon = true;
                return loaded;
            }
        }

        return Win32Api.LoadIcon(IntPtr.Zero, new IntPtr(Win32Api.IdiApplication));
    }

    private static string BuildTooltip()
    {
        return UiText.AppName + " - " + AppFeatures.GlobalHotkeyText;
    }

    private void HandleTrayMessage(int message)
    {
        if (message == Win32Api.WmLButtonUp)
        {
            StartCaptureWithThrottle();
            return;
        }

        if (message == Win32Api.WmRButtonUp)
        {
            ShowContextMenu();
            return;
        }
    }

    private void StartCaptureWithThrottle()
    {
        int now = Environment.TickCount;
        if (unchecked(now - lastClickTick) >= ClickThrottleMilliseconds)
        {
            lastClickTick = now;
            BackgroundHotkeyAgent.StartScreenshotAnnotator();
        }
    }

    private void ShowContextMenu()
    {
        IntPtr menu = Win32Api.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Win32Api.AppendMenu(menu, Win32Api.MfString, new UIntPtr((uint)MenuSettings), "\u8bbe\u7f6e...");
            Win32Api.AppendMenu(menu, Win32Api.MfSeparator, UIntPtr.Zero, null);
            Win32Api.AppendMenu(menu, Win32Api.MfString, new UIntPtr((uint)MenuOpenOutputDirectory), "\u6253\u5f00\u6807\u6ce8\u4fdd\u5b58\u76ee\u5f55");
            Win32Api.AppendMenu(menu, Win32Api.MfString, new UIntPtr((uint)MenuOpenScreenshotDirectory), "\u6253\u5f00\u622a\u56fe\u4fdd\u5b58\u76ee\u5f55");
            Win32Api.AppendMenu(menu, Win32Api.MfSeparator, UIntPtr.Zero, null);
            Win32Api.AppendMenu(menu, Win32Api.MfString, new UIntPtr((uint)MenuCheckUpdates), "\u68c0\u67e5\u66f4\u65b0");
            Win32Api.AppendMenu(menu, Win32Api.MfString, new UIntPtr((uint)MenuAbout), "\u5173\u4e8e");
            Win32Api.AppendMenu(menu, Win32Api.MfSeparator, UIntPtr.Zero, null);
            Win32Api.AppendMenu(menu, Win32Api.MfString, new UIntPtr((uint)MenuExit), "\u9000\u51fa\u540e\u53f0");

            Win32Api.NativePoint point;
            if (!Win32Api.GetCursorPos(out point))
            {
                return;
            }

            Win32Api.SetForegroundWindow(hwnd);
            int command = Win32Api.TrackPopupMenu(
                menu,
                Win32Api.TpmRightButton | Win32Api.TpmReturnCmd | Win32Api.TpmNonotify,
                point.x,
                point.y,
                0,
                hwnd,
                IntPtr.Zero);
            HandleMenuCommand(command);
            Win32Api.PostMessage(hwnd, Win32Api.WmNull, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            Win32Api.DestroyMenu(menu);
        }
    }

    private void HandleMenuCommand(int command)
    {
        switch (command)
        {
            case MenuSettings:
                AppFeatures.StartSettingsWindow();
                break;
            case MenuOpenOutputDirectory:
                OpenDirectory(ResolveOutputDirectory(AppSettingsStore.Load()));
                break;
            case MenuOpenScreenshotDirectory:
                OpenDirectory(ResolveScreenshotDirectory(AppSettingsStore.Load()));
                break;
            case MenuCheckUpdates:
                AppFeatures.OpenReleasePage();
                break;
            case MenuAbout:
                AppFeatures.ShowAbout(hwnd);
                break;
            case MenuExit:
                Win32Api.PostQuitMessage(0);
                break;
        }
    }

    private static string ResolveOutputDirectory(AppSettings settings)
    {
        if (settings != null && !string.IsNullOrEmpty(settings.OutputDirectory))
        {
            return settings.OutputDirectory;
        }
        return Path.GetTempPath();
    }

    private static string ResolveScreenshotDirectory(AppSettings settings)
    {
        if (settings != null && !string.IsNullOrEmpty(settings.ScreenshotDirectory))
        {
            return settings.ScreenshotDirectory;
        }
        return ResolveOutputDirectory(settings);
    }

    private static void OpenDirectory(string directory)
    {
        try
        {
            directory = AppSettingsStore.NormalizeDirectory(directory) ?? Path.GetTempPath();
            Directory.CreateDirectory(directory);
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "explorer.exe";
            info.Arguments = AppFeatures.QuoteArgument(directory);
            info.UseShellExecute = true;
            Process.Start(info);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to open tray menu directory.", ex);
        }
    }
}
