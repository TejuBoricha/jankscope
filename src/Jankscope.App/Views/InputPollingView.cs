using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Jankscope.App.Views;

/// <summary>
/// Interactive Stage 2 Mouse Motion-Continuity &amp; Effective Polling Rate Analyzer.
/// Measures pointer event arrival intervals (dt in milliseconds) while the user moves the mouse inside this test surface.
/// Strictly compliant with HC-4: uses only local WPF MouseMove timing and pointer deltas on this element (zero global hooks, zero key codes).
/// </summary>
public sealed class InputPollingView : FrameworkElement
{
    private const int MaxSamples = 240;
    private readonly List<(double IntervalMs, double SpeedPxPerMs, Point Position, bool IsStall)> _samples = new(MaxSamples);
    private long _lastTicks;
    private Point _lastPoint;
    private int _stallEvents;

    public InputPollingView()
    {
        MinHeight = 200;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
    }

    /// <summary>Raised whenever new samples arrive or the buffer is cleared so KPI cards can refresh.</summary>
    public event EventHandler? MetricsUpdated;

    public int SampleCount => _samples.Count;

    public int StallEvents => _stallEvents;

    /// <summary>Returns (EffectiveHz, MedianIntervalMs, MaxIntervalMs, JitterMs, Verdict).</summary>
    public (double EffectiveHz, double MedianIntervalMs, double MaxIntervalMs, double JitterMs, string Verdict) Summary()
    {
        if (_samples.Count < 4)
        {
            return (0, 0, 0, 0, "Move cursor in smooth circles inside the test pad below");
        }

        var intervals = _samples.Select(s => s.IntervalMs).Order().ToArray();
        var median = intervals[intervals.Length / 2];
        var p95 = intervals[Math.Clamp((int)(intervals.Length * 0.95), 0, intervals.Length - 1)];
        var max = intervals[^1];
        var hz = median > 0 ? Math.Round(1000.0 / median, 0) : 0;
        var jitter = Math.Round(Math.Max(0, p95 - median), 2);

        var verdict = _stallEvents > 0
            ? $"Detected {_stallEvents.ToString(CultureInfo.InvariantCulture)} mid-motion discontinuity stall(s) (> 35 ms gap while moving fast)"
            : jitter <= 3.5
                ? "Consistent motion delivery (low inter-event jitter)"
                : "Elevated delivery variance (check wireless receiver distance or USB hub load)";

        return (hz, Math.Round(median, 2), Math.Round(max, 2), jitter, verdict);
    }

    public void Reset()
    {
        _samples.Clear();
        _lastTicks = 0;
        _stallEvents = 0;
        MetricsUpdated?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Feeds a synthetic 125 Hz / 500 Hz motion trace with an injected 55 ms stall so users or tests can inspect the analyzer with one click.</summary>
    public void LoadSimulatedTrace()
    {
        _samples.Clear();
        _stallEvents = 0;
        var width = Math.Max(360.0, ActualWidth);
        var height = Math.Max(180.0, ActualHeight);
        var cx = width * 0.5;
        var cy = height * 0.5;
        for (var i = 0; i < 120; i++)
        {
            var angle = i * 0.12;
            var pt = new Point(cx + (Math.Cos(angle) * 90), cy + (Math.Sin(angle) * 45));
            var dt = i == 72 ? 58.0 : 4.0 + ((i % 5) * 0.35);
            var isStall = dt >= 35.0;
            if (isStall)
            {
                _stallEvents++;
            }

            _samples.Add((dt, 1.4, pt, isStall));
        }

        MetricsUpdated?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var now = Stopwatch.GetTimestamp();
        var pos = e.GetPosition(this);
        if (_lastTicks != 0)
        {
            var dtMs = (now - _lastTicks) * 1000.0 / Stopwatch.Frequency;
            var dx = pos.X - _lastPoint.X;
            var dy = pos.Y - _lastPoint.Y;
            var dist = Math.Sqrt((dx * dx) + (dy * dy));
            if (dtMs is >= 0.25 and <= 250.0 && dist >= 0.5)
            {
                var prevSpeed = _samples.Count > 0 ? _samples[^1].SpeedPxPerMs : 1.0;
                var speed = dist / dtMs;
                // ADR-0009 heuristic: distinguish natural hand deceleration from an abrupt mid-motion delivery gap
                var isStall = dtMs >= 35.0 && prevSpeed >= 0.45 && dist >= 12.0;
                if (isStall)
                {
                    _stallEvents++;
                }

                if (_samples.Count >= MaxSamples)
                {
                    _samples.RemoveAt(0);
                }

                _samples.Add((dtMs, speed, pos, isStall));
                MetricsUpdated?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }
        }

        _lastTicks = now;
        _lastPoint = pos;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _lastTicks = 0;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 80 || height < 80)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bg = Themed("ChartBackgroundBrush", Brushes.Black);
        var grid = Themed("ChartGridBrush", Brushes.DimGray);
        var line = Themed("ChartLineBrush", Brushes.DeepSkyBlue);
        var ok = Themed("OkBrush", Brushes.MediumSeaGreen);
        var warn = Themed("DangerBrush", Brushes.OrangeRed);
        var muted = Themed("MutedBrush", Brushes.Gray);
        var fg = Themed("ForegroundBrush", Brushes.White);

        dc.DrawRoundedRectangle(bg, new Pen(grid, 1), new Rect(0, 0, width, height), 8, 8);

        // Split: Left 42% is interactive motion pad + trail, Right 58% is live inter-event dt oscilloscope
        var padWidth = Math.Max(160, width * 0.42);
        var padRect = new Rect(10, 10, padWidth - 16, height - 20);
        var chartRect = new Rect(padWidth + 6, 22, Math.Max(80, width - padWidth - 18), height - 42);

        dc.DrawRoundedRectangle(Themed("PanelInsetBrush", Brushes.Black), new Pen(grid, 1) { DashStyle = DashStyles.Dash }, padRect, 6, 6);
        DrawText(dc, "INTERACTIVE MOTION TEST PAD (MOVE MOUSE HERE)", muted, new Point(padRect.Left + 10, padRect.Top + 8), dpi, 10, true);

        if (_samples.Count < 2)
        {
            DrawText(dc, "Move your mouse continuously in smooth figure-8s here", fg, new Point(padRect.Left + 12, padRect.Top + (padRect.Height / 2) - 8), dpi, 11, false);
        }
        else
        {
            var trail = new StreamGeometry();
            using (var ctx = trail.Open())
            {
                var startIdx = Math.Max(0, _samples.Count - 60);
                for (var i = startIdx; i < _samples.Count; i++)
                {
                    var p = _samples[i].Position;
                    var clamped = new Point(Math.Clamp(p.X, padRect.Left + 4, padRect.Right - 4), Math.Clamp(p.Y, padRect.Top + 26, padRect.Bottom - 4));
                    if (i == startIdx)
                    {
                        ctx.BeginFigure(clamped, false, false);
                    }
                    else
                    {
                        ctx.LineTo(clamped, true, false);
                    }
                }
            }

            trail.Freeze();
            dc.DrawGeometry(null, new Pen(line, 2.0), trail);

            var latest = _samples[^1].Position;
            var latestClamped = new Point(Math.Clamp(latest.X, padRect.Left + 6, padRect.Right - 6), Math.Clamp(latest.Y, padRect.Top + 26, padRect.Bottom - 6));
            dc.DrawEllipse(ok, null, latestClamped, 4.5, 4.5);
        }

        // Right side: Inter-Event Interval (dt ms) Oscilloscope
        DrawText(dc, "INTER-EVENT ARRIVAL INTERVAL (MS) - LOWER & FLATTER IS BETTER", muted, new Point(chartRect.Left, 6), dpi, 10, true);
        dc.DrawRectangle(null, new Pen(grid, 1), chartRect);

        const double yMax = 45.0;
        double Y(double ms) => chartRect.Bottom - (Math.Clamp(ms, 0, yMax) / yMax * chartRect.Height);

        foreach (var refMs in new[] { 4.0, 8.0, 16.0, 35.0 })
        {
            var y = Y(refMs);
            dc.DrawLine(new Pen(grid, 1), new Point(chartRect.Left, y), new Point(chartRect.Right, y));
            DrawText(dc, $"{refMs.ToString("0", CultureInfo.InvariantCulture)} ms", muted, new Point(chartRect.Right - 36, y - 13), dpi, 9.5, false, mono: true);
        }

        if (_samples.Count > 0)
        {
            var barStep = chartRect.Width / MaxSamples;
            for (var i = 0; i < _samples.Count; i++)
            {
                var s = _samples[i];
                var x = chartRect.Left + (i * barStep);
                var y = Y(s.IntervalMs);
                var brush = s.IsStall ? warn : s.IntervalMs <= 9.0 ? ok : line;
                dc.DrawLine(new Pen(brush, Math.Max(1.5, barStep - 0.5)), new Point(x, chartRect.Bottom), new Point(x, Math.Min(chartRect.Bottom - 2, y)));
            }
        }
    }

    private static void DrawText(DrawingContext dc, string text, Brush brush, Point at, double dpi, double size, bool bold, bool mono = false)
    {
        var typeface = new Typeface(new FontFamily(mono ? "Consolas" : "Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush, dpi);
        dc.DrawText(formatted, at);
    }

    private Brush Themed(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
