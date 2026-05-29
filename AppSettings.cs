using System;
using System.IO;
using System.Text;

internal sealed class AppSettings
{
    public string OutputDirectory;
}

internal static class AppSettingsStore
{
    private const string AppFolderName = "QuickerImageAnnotator";
    private const string SettingsFileName = "settings.ini";
    private const string OutputDirectoryKey = "OutputDirectory";

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
        string content = OutputDirectoryKey + "=" + (outputDirectory ?? string.Empty) + Environment.NewLine;
        File.WriteAllText(path, content, new UTF8Encoding(false));
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
}
