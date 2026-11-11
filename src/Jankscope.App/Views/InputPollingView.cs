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
    private readonly List<(double IntervalMs, double SpeedPxPerMs, Point Position, bool IsStall, bool Normalized)> _samples = new(MaxSamples);
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
            return (0, 0, 0, 0, "Move cursor in smooth circles inside the radar reticle below");
        }

        var intervals = _samples.Select(s => s.IntervalMs).Order().ToArray();
        var median = intervals[intervals.Length / 2];
        var p95 = intervals[Math.Clamp((int)(intervals.Length * 0.95), 0, intervals.Length - 1)];
        var max = intervals[^1];
        var hz = median > 0 ? Math.Round(1000.0 / median, 0) : 0;
        var jitter = Math.Round(Math.Max(0, p95 - median), 2);

        var verdict = _stallEvents > 0
            ? $"ALERT // {_stallEvents.ToString(CultureInfo.InvariantCulture)} mid-motion discontinuity stall(s) (> 35 ms gap while moving)"
            : jitter <= 3.5
                ? "LOCKED // Continuous pointer delivery (low inter-event variance)"
                : "VARIANCE // Elevated delivery jitter (check wireless receiver or USB hub load)";

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

    /// <summary>Feeds a synthetic 250 Hz motion trace with an injected 58 ms stall so users or tests can inspect the analyzer with one click.</summary>
    public void LoadSimulatedTrace()
    {
        _samples.Clear();
        _stallEvents = 0;
        for (var i = 0; i < 120; i++)
        {
            var angle = i * 0.14;
            var pt = new Point(Math.Cos(angle), Math.Sin(angle * 2.0) * 0.72);
            var dt = i == 72 ? 58.0 : 4.0 + ((i % 5) * 0.35);
            var isStall = dt >= 35.0;
            if (isStall)
            {
                _stallEvents++;
            }

            _samples.Add((dt, 1.4, pt, isStall, true));
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
                var isStall = dtMs >= 35.0 && prevSpeed >= 0.45 && dist >= 12.0;
                if (isStall)
                {
                    _stallEvents++;
                }

                if (_samples.Count >= MaxSamples)
                {
                    _samples.RemoveAt(0);
                }

                _samples.Add((dtMs, speed, pos, isStall, false));
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

        dc.DrawRoundedRectangle(bg, new Pen(grid, 1), new Rect(0, 0, width, height), 6, 6);

        var padWidth = Math.Max(160, width * 0.42);
        var padRect = new Rect(10, 10, padWidth - 16, height - 20);
        var chartRect = new Rect(padWidth + 6, 26, Math.Max(80, width - padWidth - 18), height - 42);

        dc.DrawRoundedRectangle(Themed("PanelInsetBrush", Brushes.Black), new Pen(grid, 1), padRect, 6, 6);
        DrawText(dc, "01 // VECTOR RETICLE TEST SURFACE (ORBIT CURSOR HERE)", muted, new Point(padRect.Left + 10, padRect.Top + 7), dpi, 9.5, true, mono: true);

        // Radar bullseye concentric rings & crosshairs
        var center = new Point(padRect.Left + (padRect.Width * 0.5), padRect.Top + 14 + ((padRect.Height - 14) * 0.5));
        var maxR = Math.Max(20, Math.Min(padRect.Width, padRect.Height - 28) * 0.42);
        var ringPen = new Pen(grid, 1) { DashStyle = DashStyles.Dot };
        dc.DrawEllipse(null, ringPen, center, maxR, maxR);
        dc.DrawEllipse(null, ringPen, center, maxR * 0.62, maxR * 0.62);
        dc.DrawEllipse(null, ringPen, center, maxR * 0.28, maxR * 0.28);
        dc.DrawLine(ringPen, new Point(padRect.Left + 12, center.Y), new Point(padRect.Right - 12, center.Y));
        dc.DrawLine(ringPen, new Point(center.X, padRect.Top + 26), new Point(center.X, padRect.Bottom - 10));

        if (_samples.Count < 2)
        {
            DrawText(dc, "ORBIT MOUSE CONTINUOUSLY INSIDE RETICLE", fg, new Point(Math.Max(padRect.Left + 12, center.X - 115), center.Y - 6), dpi, 10, true, mono: true);
        }
        else
        {
            Point ResolvePoint((double IntervalMs, double SpeedPxPerMs, Point Position, bool IsStall, bool Normalized) sample)
            {
                if (sample.Normalized)
                {
                    return new Point(center.X + (sample.Position.X * maxR * 0.86), center.Y + (sample.Position.Y * maxR * 0.86));
                }

                return new Point(
                    Math.Clamp(sample.Position.X, padRect.Left + 6, padRect.Right - 6),
                    Math.Clamp(sample.Position.Y, padRect.Top + 26, padRect.Bottom - 6));
            }

            var trail = new StreamGeometry();
            using (var ctx = trail.Open())
            {
                var startIdx = Math.Max(0, _samples.Count - 70);
                for (var i = startIdx; i < _samples.Count; i++)
                {
                    var clamped = ResolvePoint(_samples[i]);
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
            var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 0, 229, 255)), 5.0) { LineJoin = PenLineJoin.Round };
            glowPen.Freeze();
            dc.DrawGeometry(null, glowPen, trail);
            dc.DrawGeometry(null, new Pen(line, 1.8) { LineJoin = PenLineJoin.Round }, trail);

            var startDots = Math.Max(0, _samples.Count - 70);
            for (var i = startDots; i < _samples.Count; i++)
            {
                if (_samples[i].IsStall)
                {
                    var stallPt = ResolvePoint(_samples[i]);
                    dc.DrawEllipse(warn, new Pen(fg, 1), stallPt, 5.5, 5.5);
                }
            }

            var latestClamped = ResolvePoint(_samples[^1]);
            dc.DrawEllipse(ok, new Pen(fg, 1), latestClamped, 4.5, 4.5);
        }

        // Right side: Inter-Event Interval (dt ms) Spectrum
        DrawText(dc, "02 // INTER-EVENT ARRIVAL SPECTRUM (DT MS // LOWER IS FASTER)", muted, new Point(chartRect.Left, 7), dpi, 9.5, true, mono: true);
        dc.DrawRectangle(null, new Pen(grid, 1), chartRect);

        const double yMax = 45.0;
        double Y(double ms) => chartRect.Bottom - (Math.Clamp(ms, 0, yMax) / yMax * chartRect.Height);

        foreach (var (refMs, hzLabel) in new[] { (4.0, "4 ms (250 Hz)"), (8.0, "8 ms (125 Hz)"), (16.0, "16 ms (60 Hz)"), (35.0, "35 ms STALL GATE") })
        {
            var y = Y(refMs);
            dc.DrawLine(new Pen(refMs >= 35.0 ? warn : grid, 1) { DashStyle = refMs >= 35.0 ? DashStyles.Dash : DashStyles.Dot }, new Point(chartRect.Left, y), new Point(chartRect.Right, y));
            DrawText(dc, hzLabel, refMs >= 35.0 ? warn : muted, new Point(chartRect.Right - 96, y - 13), dpi, 9.0, refMs >= 35.0, mono: true);
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
