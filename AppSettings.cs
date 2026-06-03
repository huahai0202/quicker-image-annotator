using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal sealed class AppSettings
{
    public string OutputDirectory;
    public string ScreenshotDirectory;
    public bool AutoStartEnabled;
    public bool GlobalHotkeyEnabled = true;
    public uint GlobalHotkeyModifiers = AppFeatures.DefaultHotkeyModifiers;
    public int GlobalHotkeyKey = Win32Api.VkA;
    public string BaiduOcrApiKey;
    public string BaiduOcrSecretKey;
    public OcrEngineKind OcrEngine = OcrEngineKind.Standard;
    public OcrTextLayout OcrLayout = OcrTextLayout.SmartParagraph;
}

internal static class AppSettingsStore
{
    private const string AppFolderName = "QuickerImageAnnotator";
    private const string SettingsFileName = "settings.ini";
    private const string OutputDirectoryKey = "OutputDirectory";
    private const string ScreenshotDirectoryKey = "ScreenshotDirectory";
    private const string AutoStartKey = "AutoStart";
    private const string GlobalHotkeyKey = "GlobalHotkey";
    private const string GlobalHotkeyModifiersKey = "GlobalHotkeyModifiers";
    private const string GlobalHotkeyVirtualKeyKey = "GlobalHotkeyKey";
    private const string BaiduOcrApiKeyKey = "BaiduOcrApiKey";
    private const string BaiduOcrSecretKeyKey = "BaiduOcrSecretKey";
    private const string BaiduOcrSecretKeyProtectedKey = "BaiduOcrSecretKeyProtected";
    private const string OcrEngineKey = "OcrEngine";
    private const string OcrLayoutKey = "OcrLayout";

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        string path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return settings;
        }

        try
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (string.Equals(key, OutputDirectoryKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.OutputDirectory = NormalizeDirectory(value);
                }
                else if (string.Equals(key, ScreenshotDirectoryKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.ScreenshotDirectory = NormalizeDirectory(value);
                }
                else if (string.Equals(key, AutoStartKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.AutoStartEnabled = ParseBool(value);
                }
                else if (string.Equals(key, GlobalHotkeyKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.GlobalHotkeyEnabled = ParseBool(value);
                }
                else if (string.Equals(key, GlobalHotkeyModifiersKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.GlobalHotkeyModifiers = ParseUInt(value, AppFeatures.DefaultHotkeyModifiers);
                }
                else if (string.Equals(key, GlobalHotkeyVirtualKeyKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.GlobalHotkeyKey = ParseInt(value, Win32Api.VkA);
                }
                else if (string.Equals(key, BaiduOcrApiKeyKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.BaiduOcrApiKey = value;
                }
                else if (string.Equals(key, BaiduOcrSecretKeyKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.BaiduOcrSecretKey = value;
                }
                else if (string.Equals(key, BaiduOcrSecretKeyProtectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        settings.BaiduOcrSecretKey = UnprotectSecret(value);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Failed to unprotect OCR secret key.", ex);
                    }
                }
                else if (string.Equals(key, OcrEngineKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.OcrEngine = ParseOcrEngine(value);
                }
                else if (string.Equals(key, OcrLayoutKey, StringComparison.OrdinalIgnoreCase))
                {
                    settings.OcrLayout = ParseOcrLayout(value);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to load settings.", ex);
        }

        return settings;
    }

    public static void Save(AppSettings settings)
    {
        string path = GetSettingsPath();
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string outputDirectory = settings == null ? null : NormalizeDirectory(settings.OutputDirectory);
        string screenshotDirectory = settings == null ? null : NormalizeDirectory(settings.ScreenshotDirectory);
        bool autoStart = settings != null && settings.AutoStartEnabled;
        bool globalHotkey = settings == null || settings.GlobalHotkeyEnabled;
        uint globalHotkeyModifiers = settings == null ? AppFeatures.DefaultHotkeyModifiers : AppFeatures.NormalizeHotkeyModifiers(settings.GlobalHotkeyModifiers);
        int globalHotkeyKey = settings == null ? Win32Api.VkA : AppFeatures.NormalizeHotkeyKey(settings.GlobalHotkeyKey);
        string baiduOcrApiKey = settings == null ? null : (settings.BaiduOcrApiKey ?? string.Empty).Trim();
        string baiduOcrSecretKey = settings == null ? null : (settings.BaiduOcrSecretKey ?? string.Empty).Trim();
        string protectedSecretKey = ProtectSecret(baiduOcrSecretKey);
        OcrEngineKind ocrEngine = settings == null ? OcrEngineKind.Standard : BaiduOcrClient.NormalizeEngine(settings.OcrEngine);
        OcrTextLayout ocrLayout = settings == null ? OcrTextLayout.SmartParagraph : OcrTextFormatter.NormalizeLayout(settings.OcrLayout);
        StringBuilder content = new StringBuilder();
        content.Append(OutputDirectoryKey).Append("=").Append(outputDirectory ?? string.Empty).AppendLine();
        content.Append(ScreenshotDirectoryKey).Append("=").Append(screenshotDirectory ?? string.Empty).AppendLine();
        content.Append(AutoStartKey).Append("=").Append(autoStart ? "1" : "0").AppendLine();
        content.Append(GlobalHotkeyKey).Append("=").Append(globalHotkey ? "1" : "0").AppendLine();
        content.Append(GlobalHotkeyModifiersKey).Append("=").Append(globalHotkeyModifiers.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        content.Append(GlobalHotkeyVirtualKeyKey).Append("=").Append(globalHotkeyKey.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        content.Append(BaiduOcrApiKeyKey).Append("=").Append(baiduOcrApiKey ?? string.Empty).AppendLine();
        content.Append(BaiduOcrSecretKeyKey).Append("=").AppendLine();
        content.Append(BaiduOcrSecretKeyProtectedKey).Append("=").Append(protectedSecretKey ?? string.Empty).AppendLine();
        content.Append(OcrEngineKey).Append("=").Append(ocrEngine.ToString()).AppendLine();
        content.Append(OcrLayoutKey).Append("=").Append(ocrLayout.ToString()).AppendLine();
        File.WriteAllText(path, content.ToString(), new UTF8Encoding(false));
    }

    public static string NormalizeDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return null;
        }

        string cleaned = outputDirectory.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        string expanded = Environment.ExpandEnvironmentVariables(cleaned);
        return Path.GetFullPath(expanded);
    }

    private static string GetSettingsPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            appData = Environment.CurrentDirectory;
        }

        return Path.Combine(Path.Combine(appData, AppFolderName), SettingsFileName);
    }

    internal static string ProtectSecretForSelfTest(string value)
    {
        return ProtectSecret(value);
    }

    internal static string UnprotectSecretForSelfTest(string value)
    {
        return UnprotectSecret(value);
    }

    private static string ProtectSecret(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        byte[] plain = Encoding.UTF8.GetBytes(value);
        byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string UnprotectSecret(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        byte[] protectedBytes = Convert.FromBase64String(value.Trim());
        byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }

    private static bool ParseBool(string value)
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

    private static uint ParseUInt(string value, uint fallback)
    {
        uint parsed;
        if (!uint.TryParse(value, out parsed))
        {
            return fallback;
        }
        return parsed;
    }

    private static int ParseInt(string value, int fallback)
    {
        int parsed;
        if (!int.TryParse(value, out parsed))
        {
            return fallback;
        }
        return parsed;
    }

    private static OcrEngineKind ParseOcrEngine(string value)
    {
        if (string.Equals(value, "Accurate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "HighAccuracy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "accurate_basic", StringComparison.OrdinalIgnoreCase))
        {
            return OcrEngineKind.Accurate;
        }
        return OcrEngineKind.Standard;
    }

    private static OcrTextLayout ParseOcrLayout(string value)
    {
        if (string.Equals(value, "Lines", StringComparison.OrdinalIgnoreCase))
        {
            return OcrTextLayout.Lines;
        }
        return OcrTextLayout.SmartParagraph;
    }
}
