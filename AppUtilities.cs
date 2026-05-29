using System;
using System.Diagnostics;
using System.Windows.Forms;

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
        string line = string.Format("[{0}] [{1}] {2}", timestamp, level, message);
        if (exception != null)
        {
            line = line + Environment.NewLine + exception;
        }

        Trace.WriteLine(line);
        Debug.WriteLine(line);
    }
}

internal static class AppUtilities
{
    public static ThrottledAction Throttle(Action action, int intervalMilliseconds)
    {
        return new ThrottledAction(action, intervalMilliseconds);
    }
}

internal sealed class ThrottledAction : IDisposable
{
    private readonly Action action;
    private readonly int intervalMilliseconds;
    private readonly Timer timer = new Timer();
    private DateTime lastRunUtc = DateTime.MinValue;
    private bool pending;
    private bool disposed;

    public ThrottledAction(Action action, int intervalMilliseconds)
    {
        if (action == null)
        {
            throw new ArgumentNullException("action");
        }

        this.action = action;
        this.intervalMilliseconds = Math.Max(1, intervalMilliseconds);
        timer.Interval = this.intervalMilliseconds;
        timer.Tick += Timer_Tick;
    }

    public void Invoke()
    {
        if (disposed)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        double elapsed = (now - lastRunUtc).TotalMilliseconds;
        if (lastRunUtc == DateTime.MinValue || elapsed >= intervalMilliseconds)
        {
            RunNow();
            return;
        }

        pending = true;
        int remaining = Math.Max(1, intervalMilliseconds - (int)elapsed);
        timer.Interval = remaining;
        timer.Stop();
        timer.Start();
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        timer.Stop();
        if (pending)
        {
            RunNow();
        }
    }

    private void RunNow()
    {
        pending = false;
        lastRunUtc = DateTime.UtcNow;
        timer.Interval = intervalMilliseconds;
        action();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= Timer_Tick;
        timer.Dispose();
    }
}

internal sealed class AnimationFrameScheduler : IDisposable
{
    private readonly Action callback;
    private readonly Timer timer = new Timer();
    private bool pending;
    private bool disposed;

    public AnimationFrameScheduler(Action callback)
    {
        if (callback == null)
        {
            throw new ArgumentNullException("callback");
        }

        this.callback = callback;
        timer.Interval = AppStyles.AnimationFrameMilliseconds;
        timer.Tick += Timer_Tick;
    }

    public void RequestFrame()
    {
        if (disposed || pending)
        {
            return;
        }

        pending = true;
        timer.Start();
    }

    public void Flush()
    {
        if (disposed || !pending)
        {
            return;
        }

        timer.Stop();
        pending = false;
        callback();
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        timer.Stop();
        if (disposed)
        {
            return;
        }

        pending = false;
        callback();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= Timer_Tick;
        timer.Dispose();
    }
}
