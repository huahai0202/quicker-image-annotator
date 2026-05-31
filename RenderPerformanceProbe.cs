using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

internal static class RenderPerformanceProbe
{
    public const string Direct2DFrameRender = "Direct2DFrameRender";
    public const string Direct2DAnnotationDraw = "Direct2DAnnotationDraw";
    public const string FinalImageRender = "FinalImageRender";
    public const string FrameInterval = "FrameInterval";
    public const string LaunchToFirstFrame = "LaunchToFirstFrame";
    public const string InputToFrame = "InputToFrame";

    private const int MaxSamplesPerMetric = 2048;
    private const int DefaultSummaryIntervalFrames = 120;
    private static readonly object SyncRoot = new object();
    private static readonly Dictionary<string, MetricStats> Metrics = new Dictionary<string, MetricStats>(StringComparer.Ordinal);
    private static bool enabled;
    private static string logPath;
    private static long lastFrameStartTimestamp;
    private static int framesSinceSummary;
    private static int summaryIntervalFrames = DefaultSummaryIntervalFrames;

    public static bool Enabled
    {
        get { return enabled; }
    }

    public static string LogPath
    {
        get { return logPath; }
    }

    public static void Configure(bool isEnabled, string requestedLogPath)
    {
        lock (SyncRoot)
        {
            Metrics.Clear();
            enabled = isEnabled;
            logPath = isEnabled ? NormalizeLogPath(requestedLogPath) : null;
            lastFrameStartTimestamp = 0;
            framesSinceSummary = 0;
            summaryIntervalFrames = DefaultSummaryIntervalFrames;
        }

        if (isEnabled)
        {
            WriteLine("Render performance probe enabled.");
        }
    }

    public static long Start()
    {
        if (!enabled)
        {
            return 0;
        }

        return Stopwatch.GetTimestamp();
    }

    public static void Stop(string metricName, long startTimestamp)
    {
        if (!enabled || startTimestamp == 0)
        {
            return;
        }

        long elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - startTimestamp);
        RecordTicks(metricName, elapsedTicks);
    }

    public static void RecordSince(string metricName, long startTimestamp)
    {
        if (startTimestamp == 0)
        {
            return;
        }

        long elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - startTimestamp);
        double elapsedMs = TicksToMilliseconds(elapsedTicks);
        AppLog.Info(metricName + ": " + elapsedMs.ToString("0.###", CultureInfo.InvariantCulture) + " ms");
        if (enabled)
        {
            RecordTicks(metricName, elapsedTicks);
        }
    }

    public static void MarkFrameStart()
    {
        if (!enabled)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        long previous;
        lock (SyncRoot)
        {
            previous = lastFrameStartTimestamp;
            lastFrameStartTimestamp = now;
            framesSinceSummary++;
        }

        if (previous > 0)
        {
            RecordTicks(FrameInterval, Math.Max(0, now - previous));
        }
    }

    public static void FlushSummary(string reason)
    {
        if (!enabled)
        {
            return;
        }

        string summary = GetSummary(reason);
        if (summary.Length > 0)
        {
            WriteLine(summary);
        }
    }

    public static string GetSummary(string reason)
    {
        MetricSnapshot[] snapshots;
        lock (SyncRoot)
        {
            snapshots = new MetricSnapshot[Metrics.Count];
            int index = 0;
            foreach (KeyValuePair<string, MetricStats> pair in Metrics)
            {
                snapshots[index++] = pair.Value.ToSnapshot(pair.Key);
            }
        }

        Array.Sort(snapshots, delegate(MetricSnapshot left, MetricSnapshot right)
        {
            return string.CompareOrdinal(left.Name, right.Name);
        });

        if (snapshots.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("Render performance summary");
        if (!string.IsNullOrEmpty(reason))
        {
            builder.Append(" (");
            builder.Append(reason);
            builder.Append(")");
        }
        builder.AppendLine(":");

        for (int i = 0; i < snapshots.Length; i++)
        {
            MetricSnapshot snapshot = snapshots[i];
            builder.Append("  ");
            builder.Append(snapshot.Name);
            builder.Append(": count=");
            builder.Append(snapshot.Count.ToString(CultureInfo.InvariantCulture));
            builder.Append(", avg=");
            AppendMilliseconds(builder, snapshot.AverageMilliseconds);
            builder.Append("ms, p50=");
            AppendMilliseconds(builder, snapshot.P50Milliseconds);
            builder.Append("ms, p95=");
            AppendMilliseconds(builder, snapshot.P95Milliseconds);
            builder.Append("ms, min=");
            AppendMilliseconds(builder, snapshot.MinMilliseconds);
            builder.Append("ms, max=");
            AppendMilliseconds(builder, snapshot.MaxMilliseconds);
            builder.AppendLine("ms");
        }

        return builder.ToString().TrimEnd();
    }

    private static void RecordTicks(string metricName, long elapsedTicks)
    {
        if (string.IsNullOrEmpty(metricName))
        {
            return;
        }

        bool shouldWriteSummary = false;
        lock (SyncRoot)
        {
            MetricStats stats;
            if (!Metrics.TryGetValue(metricName, out stats))
            {
                stats = new MetricStats();
                Metrics.Add(metricName, stats);
            }

            stats.Add(elapsedTicks);
            if (string.Equals(metricName, Direct2DFrameRender, StringComparison.Ordinal) &&
                framesSinceSummary >= summaryIntervalFrames)
            {
                framesSinceSummary = 0;
                shouldWriteSummary = true;
            }
        }

        if (shouldWriteSummary)
        {
            FlushSummary("periodic");
        }
    }

    private static string NormalizeLogPath(string requestedLogPath)
    {
        if (string.IsNullOrWhiteSpace(requestedLogPath))
        {
            return Path.Combine(Path.GetTempPath(), "QuickerImageAnnotator-render-profile.log");
        }

        return Path.GetFullPath(requestedLogPath);
    }

    private static void WriteLine(string message)
    {
        AppLog.Info(message);

        string path = logPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                path,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                    " " + message + Environment.NewLine,
                Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to write render performance log.", ex);
        }
    }

    private static void AppendMilliseconds(StringBuilder builder, double value)
    {
        builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private sealed class MetricStats
    {
        private readonly List<long> samples = new List<long>();
        private int nextSampleIndex;
        private long count;
        private long totalTicks;
        private long minTicks = long.MaxValue;
        private long maxTicks;

        public void Add(long ticks)
        {
            count++;
            totalTicks += ticks;
            if (ticks < minTicks)
            {
                minTicks = ticks;
            }
            if (ticks > maxTicks)
            {
                maxTicks = ticks;
            }

            if (samples.Count < MaxSamplesPerMetric)
            {
                samples.Add(ticks);
            }
            else
            {
                samples[nextSampleIndex] = ticks;
                nextSampleIndex = (nextSampleIndex + 1) % MaxSamplesPerMetric;
            }
        }

        public MetricSnapshot ToSnapshot(string name)
        {
            long[] sortedSamples = samples.ToArray();
            Array.Sort(sortedSamples);

            return new MetricSnapshot(
                name,
                count,
                count == 0 ? 0 : TicksToMilliseconds(totalTicks) / count,
                sortedSamples.Length == 0 ? 0 : TicksToMilliseconds(sortedSamples[PercentileIndex(sortedSamples.Length, 0.50)]),
                sortedSamples.Length == 0 ? 0 : TicksToMilliseconds(sortedSamples[PercentileIndex(sortedSamples.Length, 0.95)]),
                minTicks == long.MaxValue ? 0 : TicksToMilliseconds(minTicks),
                TicksToMilliseconds(maxTicks));
        }

        private static int PercentileIndex(int length, double percentile)
        {
            if (length <= 1)
            {
                return 0;
            }

            int index = (int)Math.Ceiling(length * percentile) - 1;
            return Math.Max(0, Math.Min(length - 1, index));
        }
    }

    private struct MetricSnapshot
    {
        public readonly string Name;
        public readonly long Count;
        public readonly double AverageMilliseconds;
        public readonly double P50Milliseconds;
        public readonly double P95Milliseconds;
        public readonly double MinMilliseconds;
        public readonly double MaxMilliseconds;

        public MetricSnapshot(
            string name,
            long count,
            double averageMilliseconds,
            double p50Milliseconds,
            double p95Milliseconds,
            double minMilliseconds,
            double maxMilliseconds)
        {
            Name = name;
            Count = count;
            AverageMilliseconds = averageMilliseconds;
            P50Milliseconds = p50Milliseconds;
            P95Milliseconds = p95Milliseconds;
            MinMilliseconds = minMilliseconds;
            MaxMilliseconds = maxMilliseconds;
        }
    }
}
