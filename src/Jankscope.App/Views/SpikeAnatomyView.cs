using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Jankscope.Frames;
using Jankscope.Frames.Incidents;

namespace Jankscope.App.Views;

/// <summary>
/// Dual-bay precision forensic visualizer:
/// Left bay renders a 31-frame micro-anatomy waterfall (f-15 .. f0 .. f+15) centered on the target or worst spike frame,
/// exposing pre-spike cadence, exact single-frame vs. cluster stall geometry, and post-stall queue drain.
/// Right bay renders a Poincare phase-space return map (frame[n-1] vs. frame[n]) that distinguishes tight refresh lock
/// from isolated excursions and frame-doubling oscillation.
/// </summary>
public sealed class SpikeAnatomyView : FrameworkElement
{
    private const int HalfWindow = 15;
    private const int WindowSize = (HalfWindow * 2) + 1;

    private float[] _windowMs = [];
    private float[] _cpuMs = [];
    private float[] _gpuMs = [];
    private float[] _phasePairs = [];
    private int _anchorOffset;
    private long _anchorFrameIndex;
    private double _anchorTimeMs;
    private double _anchorPeakMs;
    private double _medianMs;
    private int _totalFrames;

    public SpikeAnatomyView()
    {
        MinHeight = 165;
        ClipToBounds = true;
    }

    public int TotalFrames => _totalFrames;

    public double AnchorPeakMs => _anchorPeakMs;

    public long AnchorFrameIndex => _anchorFrameIndex;

    public void Show(ReadOnlySpan<FrameSample> samples, double? focusTimeMs = null)
    {
        if (samples.Length == 0)
        {
            Clear();
            return;
        }

        var count = samples.Length;
        var times = new float[count];
        var stamps = new double[count];
        var indexes = new long[count];
        var cpu = new float[count];
        var gpu = new float[count];

        for (var i = 0; i < count; i++)
        {
            times[i] = (float)samples[i].FrameTimeMs;
            stamps[i] = samples[i].TimestampMs;
            indexes[i] = samples[i].Index;
            cpu[i] = (float)(samples[i].CpuBusyMs ?? 0.0);
            gpu[i] = (float)(samples[i].GpuBusyMs ?? 0.0);
        }

        Populate(indexes, stamps, times, cpu, gpu, focusTimeMs);
    }

    public void Show(FrameSeries? series, double? focusTimeMs = null)
    {
        if (series is null || series.Count == 0)
        {
            Clear();
            return;
        }

        var count = series.Count;
        var indexes = series.Indexes.ToArray();
        var stamps = series.TimestampsMs.ToArray();
        var times = series.FrameTimesMs.ToArray();
        var cpu = new float[count];
        var gpu = new float[count];
        Populate(indexes, stamps, times, cpu, gpu, focusTimeMs);
    }

    private void Clear()
    {
        _windowMs = [];
        _cpuMs = [];
        _gpuMs = [];
        _phasePairs = [];
        _totalFrames = 0;
        _anchorPeakMs = 0;
        _medianMs = 0;
        InvalidateVisual();
    }

    private void Populate(long[] indexes, double[] stamps, float[] times, float[] cpu, float[] gpu, double? focusTimeMs)
    {
        _totalFrames = times.Length;
        var sorted = (float[])times.Clone();
        Array.Sort(sorted);
        _medianMs = sorted[sorted.Length / 2];

        var centerIdx = 0;
        if (focusTimeMs is { } target)
        {
            // Find nearest frame to target, then snap to the highest spike within +/- 350 ms so reaction-time marks lock onto the actual hitch
            var nearest = 0;
            var bestDist = double.MaxValue;
            for (var i = 0; i < stamps.Length; i++)
            {
                var dist = Math.Abs(stamps[i] - target);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = i;
                }
            }

            centerIdx = nearest;
            var localMax = times[nearest];
            for (var i = Math.Max(0, nearest - 45); i <= Math.Min(times.Length - 1, nearest + 10); i++)
            {
                if (Math.Abs(stamps[i] - stamps[nearest]) <= 350.0 && times[i] > localMax * 1.15f)
                {
                    localMax = times[i];
                    centerIdx = i;
                }
            }
        }
        else
        {
            var maxVal = -1f;
            for (var i = 0; i < times.Length; i++)
            {
                if (times[i] > maxVal)
                {
                    maxVal = times[i];
                    centerIdx = i;
                }
            }
        }

        var start = Math.Max(0, centerIdx - HalfWindow);
        var end = Math.Min(times.Length - 1, centerIdx + HalfWindow);
        var len = end - start + 1;

        _windowMs = new float[len];
        _cpuMs = new float[len];
        _gpuMs = new float[len];
        Array.Copy(times, start, _windowMs, 0, len);
        Array.Copy(cpu, start, _cpuMs, 0, len);
        Array.Copy(gpu, start, _gpuMs, 0, len);

        _anchorOffset = centerIdx - start;
        _anchorFrameIndex = indexes[centerIdx];
        _anchorTimeMs = stamps[centerIdx];
        _anchorPeakMs = times[centerIdx];

        // Sample up to 180 consecutive pairs around the center for the Poincare phase-space return map
        var phaseStart = Math.Max(1, centerIdx - 90);
        var phaseEnd = Math.Min(times.Length - 1, centerIdx + 90);
        var pairCount = Math.Max(0, phaseEnd - phaseStart + 1);
        _phasePairs = new float[pairCount * 2];
        var k = 0;
        for (var i = phaseStart; i <= phaseEnd; i++)
        {
            _phasePairs[k++] = times[i - 1];
            _phasePairs[k++] = times[i];
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 120 || height < 90)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bg = Themed("ChartBackgroundBrush", Brushes.Black);
        var grid = Themed("ChartGridBrush", Brushes.DimGray);
        var fg = Themed("ForegroundBrush", Brushes.White);
        var muted = Themed("MutedBrush", Brushes.Gray);
        var cyan = Themed("CyanAccentBrush", Brushes.Cyan);
        var ok = Themed("OkBrush", Brushes.MediumSeaGreen);
        var line = Themed("ChartLineBrush", Brushes.DeepSkyBlue);
        var warn = Themed("WarningBrush", Brushes.Gold);
        var danger = Themed("DangerBrush", Brushes.Crimson);

        dc.DrawRoundedRectangle(bg, new Pen(grid, 1), new Rect(0, 0, width, height), 6, 6);

        if (_totalFrames == 0 || _windowMs.Length == 0)
        {
            DrawText(dc, "AWAITING TELEMETRY STREAM // START CAPTURE OR CLICK 'SIMULATE 120 FPS'", muted, new Point(16, (height / 2) - 7), dpi, 10.5, true, mono: true);
            return;
        }

        var compact = width < 480.0;
        var splitX = Math.Round(width * 0.63);
        dc.DrawLine(new Pen(grid, 1), new Point(splitX, 8), new Point(splitX, height - 8));

        // ---- LEFT BAY: 31-Frame Spike Micro-Anatomy Waterfall ----
        var wfPlot = new Rect(36, 26, Math.Max(60, splitX - 48), Math.Max(40, height - 48));
        var ratio = _medianMs > 0.1 ? _anchorPeakMs / _medianMs : 1.0;
        var summaryBadge = string.Format(
            CultureInfo.InvariantCulture,
            compact ? "#{0} // PEAK {1:0.0}ms // P50 {2:0.0}ms ({3:0.1}x)" : "FRAME #{0} // PEAK {1:0.0} ms // P50 {2:0.0} ms ({3:0.1}x)",
            _anchorFrameIndex,
            _anchorPeakMs,
            _medianMs,
            ratio);
        DrawText(dc, summaryBadge, cyan, new Point(12, 7), dpi, compact ? 9.0 : 9.5, true, mono: true);

        var maxBarMs = Math.Clamp(Math.Max(36.0, _anchorPeakMs * 1.15), 36.0, 220.0);
        double WfY(double ms) => wfPlot.Bottom - (Math.Clamp(ms, 0, maxBarMs) / maxBarMs * wfPlot.Height);

        dc.DrawRectangle(null, new Pen(grid, 1), wfPlot);

        foreach (var refMs in new[] { 8.33, 16.67, 33.33 })
        {
            if (refMs < maxBarMs * 0.94)
            {
                var y = WfY(refMs);
                dc.DrawLine(new Pen(grid, 1) { DashStyle = DashStyles.Dot }, new Point(wfPlot.Left, y), new Point(wfPlot.Right, y));
                DrawText(dc, refMs.ToString("0.#", CultureInfo.InvariantCulture), muted, new Point(6, y - 6), dpi, 9, false, mono: true);
            }
        }

        var slotW = wfPlot.Width / Math.Max(1, _windowMs.Length);
        var barW = Math.Max(2.0, slotW - 2.5);

        // Highlight column behind the anchor frame f0
        if (_anchorOffset >= 0 && _anchorOffset < _windowMs.Length)
        {
            var anchorX = wfPlot.Left + (_anchorOffset * slotW);
            var highlightBrush = new SolidColorBrush(Color.FromArgb(28, 255, 179, 0));
            highlightBrush.Freeze();
            dc.DrawRectangle(highlightBrush, null, new Rect(anchorX - 1, wfPlot.Top, slotW + 2, wfPlot.Height));
        }

        for (var i = 0; i < _windowMs.Length; i++)
        {
            var ms = _windowMs[i];
            var x = wfPlot.Left + (i * slotW) + ((slotW - barW) * 0.5);
            var top = WfY(ms);
            var h = Math.Max(2.0, wfPlot.Bottom - top);
            var isAnchor = i == _anchorOffset;

            var barBrush = ms > 33.33 ? danger : ms > 16.67 ? warn : ms <= 9.5 ? ok : line;
            dc.DrawRoundedRectangle(barBrush, isAnchor ? new Pen(fg, 1.0) : null, new Rect(x, top, barW, h), 1.5, 1.5);

            // Overlay GPU busy inner core if available
            if (_gpuMs[i] > 0.5f && _gpuMs[i] < ms)
            {
                var gpuTop = WfY(_gpuMs[i]);
                var gpuH = Math.Max(1.5, wfPlot.Bottom - gpuTop);
                var gpuBrush = new SolidColorBrush(Color.FromArgb(150, 0, 229, 255));
                gpuBrush.Freeze();
                dc.DrawRectangle(gpuBrush, null, new Rect(x + (barW * 0.2), gpuTop, barW * 0.6, gpuH));
            }

            var rel = i - _anchorOffset;
            if (rel is -15 or -10 or -5 or 0 or 5 or 10 or 15)
            {
                var lbl = rel == 0 ? "f0" : (rel > 0 ? "+" : "") + rel.ToString(CultureInfo.InvariantCulture);
                DrawText(dc, lbl, rel == 0 ? warn : muted, new Point(x - 4, wfPlot.Bottom + 4), dpi, 9, rel == 0, mono: true);
            }
        }

        // ---- RIGHT BAY: Poincare Phase-Space Return Map (f[n-1] vs f[n]) ----
        var mapLeft = splitX + 10;
        DrawText(dc, compact ? "POINCARE JITTER MAP" : "POINCARE MAP (f[n-1] -> f[n])", muted, new Point(mapLeft, 7), dpi, compact ? 9.0 : 9.5, true, mono: true);

        var mapPlot = new Rect(mapLeft + 4, 26, Math.Max(40, width - mapLeft - 14), Math.Max(40, height - 48));
        dc.DrawRectangle(null, new Pen(grid, 1), mapPlot);

        // Diagonal lock line (f[n] == f[n-1])
        dc.DrawLine(new Pen(grid, 1) { DashStyle = DashStyles.Dash }, new Point(mapPlot.Left, mapPlot.Bottom), new Point(mapPlot.Right, mapPlot.Top));

        double MapX(double ms) => mapPlot.Left + (Math.Clamp(ms, 0, maxBarMs) / maxBarMs * mapPlot.Width);
        double MapY(double ms) => mapPlot.Bottom - (Math.Clamp(ms, 0, maxBarMs) / maxBarMs * mapPlot.Height);

        // Target lock bullseye at (P50, P50)
        var lockCenter = new Point(MapX(_medianMs), MapY(_medianMs));
        dc.DrawEllipse(null, new Pen(ok, 1) { DashStyle = DashStyles.Dot }, lockCenter, 9, 9);

        // Plot consecutive pairs
        var dotBrush = new SolidColorBrush(Color.FromArgb(155, 0, 229, 255));
        dotBrush.Freeze();
        var excursionBrush = new SolidColorBrush(Color.FromArgb(220, 255, 61, 87));
        excursionBrush.Freeze();

        Point? prevPt = null;
        var vecPen = new Pen(new SolidColorBrush(Color.FromArgb(75, 255, 179, 0)), 1.0);
        vecPen.Freeze();

        for (var i = 0; i + 1 < _phasePairs.Length; i += 2)
        {
            var prevMs = _phasePairs[i];
            var currMs = _phasePairs[i + 1];
            var pt = new Point(MapX(prevMs), MapY(currMs));
            var isSpike = prevMs > 25.0f || currMs > 25.0f;
            if (isSpike && prevPt is { } p0)
            {
                dc.DrawLine(vecPen, p0, pt);
            }

            dc.DrawEllipse(isSpike ? excursionBrush : dotBrush, null, pt, isSpike ? 3.2 : 2.0, isSpike ? 3.2 : 2.0);
            prevPt = pt;
        }

        DrawText(dc, "f[n-1] ms ->", muted, new Point(mapPlot.Right - 64, mapPlot.Bottom + 4), dpi, 8.8, false, mono: true);
        DrawText(dc, "LOCK", ok, new Point(mapPlot.Left + 4, mapPlot.Bottom - 14), dpi, 8.5, true, mono: true);
    }

    private static void DrawText(DrawingContext dc, string text, Brush brush, Point at, double dpi, double size, bool bold, bool mono = false)
    {
        var typeface = new Typeface(new FontFamily(mono ? "Consolas" : "Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush, dpi);
        dc.DrawText(formatted, at);
    }

    private Brush Themed(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
