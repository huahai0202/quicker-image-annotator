using System;
using System.Diagnostics;

internal static class AppLog
{
    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string message, Exception exception)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception exception)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = "[" + timestamp + "] [" + level + "] " + message;
        if (exception != null)
        {
            line += Environment.NewLine + exception;
        }

        Trace.WriteLine(line);
        Debug.WriteLine(line);
    }
}
