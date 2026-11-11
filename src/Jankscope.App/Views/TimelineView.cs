using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Jankscope.Frames;
using Jankscope.Frames.Incidents;

namespace Jankscope.App.Views;

/// <summary>A numbered moment drawn on the timeline: a candidate anchor (<see cref="Weak"/> ones are drawn hollow).</summary>
public readonly record struct TimelineMarker(double Ms, string Label, bool Weak);

/// <summary>Everything the timeline draws. Times are milliseconds on the capture clock.</summary>
/// <param name="Samples">Live frames (used when <paramref name="Series"/> is null).</param>
/// <param name="Series">A frozen window of frames.</param>
/// <param name="RelativeToMs">Where the time axis counts from (the mark, or the newest frame); null counts from the start.</param>
public sealed record TimelineData(
    ReadOnlyMemory<FrameSample> Samples,
    FrameSeries? Series,
    double StartMs,
    double EndMs,
    double? RelativeToMs,
    double? MarkMs,
    double? MarkUncertaintyMs,
    double? WindowEndMs,
    double? AnchorMs,
    IReadOnlyList<TimelineMarker> Candidates,
    IReadOnlyList<double> EventsMs);

/// <summary>
/// The frame-time timeline (spec section 15: every number shows unit, window and data quality). It keeps each pixel column's minimum and maximum frame time
/// so a one-frame spike is visible at any zoom, shows stretches with no data as grey bands (never as zero), draws the mark with the uncertainty of its
/// time, the candidate anchors, and the anchor the person chose. Drawn with no charting package. Operable by mouse (click chooses a time) and keyboard
/// (arrow keys move a cursor, Enter chooses).
/// </summary>
public sealed class TimelineView : FrameworkElement
{
    internal const double LeftMargin = 112;
    internal const double RightMargin = 14;
    private const double TopMargin = 22;
    private const double BottomMargin = 28;
    private static readonly double[] GridMilliseconds = [8.33, 16.67, 33.33, 50, 100, 200];
    private static readonly double[] TickSeconds = [0.5, 1, 2, 5, 10, 15, 30, 60, 120];

    private TimelineData _data = new(default, null, 0, 1, null, null, null, null, null, [], []);
    private TimelineBucket[] _buckets = [];
    private int _bucketCount = -1;
    private int _gapColumns = 1;
    private bool _dirty = true;
    private double? _cursorMs;

    public TimelineView()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = false;
        MinHeight = 150;
    }

    /// <summary>True when a click or Enter chooses a time (the investigator); false for the live view.</summary>
    public bool IsInteractive { get; set; }

    /// <summary>Raised with the capture-clock time the person chose.</summary>
    public event EventHandler<double>? TimeChosen;

    public TimelineData Data => _data;

    /// <summary>The frame-time buckets drawn at the current width (for tests and for the screen-reader summary).</summary>
    public IReadOnlyList<TimelineBucket> Buckets => _buckets;

    public void Show(TimelineData data)
    {
        _data = data;
        _dirty = true;
        InvalidateVisual();
    }

    public double? ToTimeMs(double x)
    {
        var width = ActualWidth - LeftMargin - RightMargin;
        if (width <= 0 || _data.EndMs <= _data.StartMs)
        {
            return null;
        }

        var fraction = Math.Clamp((x - LeftMargin) / width, 0, 1);
        return _data.StartMs + fraction * (_data.EndMs - _data.StartMs);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var plot = new Rect(LeftMargin, TopMargin, Math.Max(0, width - LeftMargin - RightMargin), Math.Max(0, height - TopMargin - BottomMargin));
        dc.DrawRoundedRectangle(Themed("ChartBackgroundBrush", Brushes.Black), null, new Rect(0, 0, width, height), 8, 8);
        if (plot.Width < 40 || plot.Height < 40 || _data.EndMs <= _data.StartMs)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        EnsureBuckets(plot);

        var maxFrame = 0.0;
        var nonEmptyBuckets = 0;
        foreach (var bucket in _buckets)
        {
            if (!bucket.IsEmpty)
            {
                nonEmptyBuckets++;
                if (bucket.MaxMs > maxFrame)
                {
                    maxFrame = bucket.MaxMs;
                }
            }
        }

        var yMax = Math.Clamp(Math.Max(33.4, maxFrame * 1.08), 33.4, 250.0);
        double Y(double ms) => plot.Bottom - Math.Min(Math.Max(ms, 0), yMax) / yMax * plot.Height;
        double X(double ms) => plot.Left + (ms - _data.StartMs) / (_data.EndMs - _data.StartMs) * plot.Width;
        bool Inside(double ms) => ms >= _data.StartMs && ms <= _data.EndMs;

        var text = Themed("ChartTextBrush", Brushes.Gray);
        var grid = Themed("ChartGridBrush", Brushes.DimGray);
        var line = Themed("ChartLineBrush", Brushes.DeepSkyBlue);
        var markBrush = Themed("ChartMarkBrush", Brushes.Orange);
        var anchorBrush = Themed("ChartAnchorBrush", Brushes.LimeGreen);
        var gapBrush = Themed("ChartGapBrush", Brushes.Gray);
        var warnBrush = Themed("ChartWarnBrush", Brushes.OrangeRed);

        // frame-time grid and labels
        foreach (var ms in GridMilliseconds)
        {
            if (ms > yMax * 0.98)
            {
                continue;
            }

            var y = Y(ms);
            dc.DrawLine(new Pen(grid, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            Label(dc, $"{ms.ToString("0.#", CultureInfo.InvariantCulture)} ms ({(1000 / ms).ToString("0", CultureInfo.InvariantCulture)} fps)", text, new Point(6, y - 7), dpi, 10.5);
        }

        // time axis
        var span = (_data.EndMs - _data.StartMs) / 1000.0;
        var tickSeconds = TickSeconds.FirstOrDefault(t => span / t <= Math.Max(2, plot.Width / 80.0), TickSeconds[^1]);
        var reference = _data.RelativeToMs ?? _data.StartMs;
        var firstTick = Math.Ceiling((_data.StartMs - reference) / 1000.0 / tickSeconds) * tickSeconds;
        for (var t = firstTick; reference + t * 1000.0 <= _data.EndMs; t += tickSeconds)
        {
            var x = X(reference + t * 1000.0);
            dc.DrawLine(new Pen(grid, 1), new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            var zero = _data.RelativeToMs is null ? "0" : _data.MarkMs is null ? "now" : "key press";
            var label = t == 0 ? zero : (t > 0 ? "+" : "-") + Math.Abs(t).ToString("0.#", CultureInfo.InvariantCulture) + " s";
            Label(dc, label, text, new Point(x - 14, plot.Bottom + 6), dpi, 10.5);
        }

        dc.DrawRectangle(null, new Pen(grid, 1), plot);

        // stretches with no data
        var step = plot.Width / _buckets.Length;
        for (var i = 0; i < _buckets.Length;)
        {
            if (!_buckets[i].IsEmpty)
            {
                i++;
                continue;
            }

            var run = i;
            while (i < _buckets.Length && _buckets[i].IsEmpty)
            {
                i++;
            }

            // Only a stretch longer than a few frame intervals is "no data"; at a zoomed view the columns are narrower than the frame spacing and empty ones are normal.
            var runWidth = (i - run) * step;
            if (i - run >= _gapColumns && runWidth >= 3)
            {
                dc.DrawRectangle(gapBrush, null, new Rect(plot.Left + run * step, plot.Top, runWidth, plot.Height));
                if (runWidth >= 70)
                {
                    Label(dc, "no frames logged", text, new Point(plot.Left + run * step + 6, plot.Top + 4), dpi, 10);
                }
            }
        }

        // the uncertainty of the mark, then the frame times
        if (_data.MarkMs is { } mark && _data.MarkUncertaintyMs is { } uncertainty && uncertainty > 0 && Inside(mark))
        {
            var left = Math.Max(plot.Left, X(mark - uncertainty));
            var right = Math.Min(plot.Right, X(mark + uncertainty));
            if (right - left >= 2)
            {
                dc.DrawRectangle(Translucent(markBrush, 0.18), null, new Rect(left, plot.Top, right - left, plot.Height));
            }
        }

        // Subtle area fill under the mean curve for visual depth
        if (nonEmptyBuckets > 1 && line is SolidColorBrush lineSolid)
        {
            var area = new StreamGeometry();
            using (var context = area.Open())
            {
                var previous = -1;
                var segStartX = plot.Left;
                for (var i = 0; i < _buckets.Length; i++)
                {
                    if (_buckets[i].IsEmpty)
                    {
                        continue;
                    }

                    var x = plot.Left + (i + 0.5) * step;
                    var y = Y(_buckets[i].MeanMs);
                    if (previous >= 0 && i - previous - 1 < _gapColumns)
                    {
                        context.LineTo(new Point(x, y), false, false);
                    }
                    else
                    {
                        if (previous >= 0)
                        {
                            var prevX = plot.Left + (previous + 0.5) * step;
                            context.LineTo(new Point(prevX, plot.Bottom), false, false);
                            context.LineTo(new Point(segStartX, plot.Bottom), false, false);
                        }

                        segStartX = x;
                        context.BeginFigure(new Point(x, plot.Bottom), true, true);
                        context.LineTo(new Point(x, y), false, false);
                    }

                    previous = i;
                }

                if (previous >= 0)
                {
                    var prevX = plot.Left + (previous + 0.5) * step;
                    context.LineTo(new Point(prevX, plot.Bottom), false, false);
                }
            }

            area.Freeze();
            var fillBrush = new LinearGradientBrush(
                Color.FromArgb(46, lineSolid.Color.R, lineSolid.Color.G, lineSolid.Color.B),
                Color.FromArgb(4, lineSolid.Color.R, lineSolid.Color.G, lineSolid.Color.B),
                new Point(0, 0),
                new Point(0, 1));
            fillBrush.Freeze();
            dc.DrawGeometry(fillBrush, null, area);
        }

        var barPen = new Pen(line, Math.Max(1.0, step)) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        var spikePen = new Pen(warnBrush, Math.Max(1.0, step)) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        for (var i = 0; i < _buckets.Length; i++)
        {
            var bucket = _buckets[i];
            if (bucket.IsEmpty)
            {
                continue;
            }

            var x = plot.Left + (i + 0.5) * step;
            var top = Y(bucket.MaxMs);
            var bottom = Math.Max(Y(bucket.MinMs), top + 1.0);
            dc.DrawLine(bucket.MaxMs > yMax ? spikePen : barPen, new Point(x, top), new Point(x, bottom));
        }

        // A line through the column means joins neighbouring columns, so a spike that fills a column on its own (min equals max) still reads as a peak
        // rising from and falling back to the baseline instead of a one-pixel dot. Stretches with no frames break the line.
        var trend = new StreamGeometry();
        using (var context = trend.Open())
        {
            var previous = -1;
            for (var i = 0; i < _buckets.Length; i++)
            {
                if (_buckets[i].IsEmpty)
                {
                    continue;
                }

                var point = new Point(plot.Left + (i + 0.5) * step, Y(_buckets[i].MeanMs));
                if (previous >= 0 && i - previous - 1 < _gapColumns)
                {
                    context.LineTo(point, true, false); // a short run of empty columns is bridged
                }
                else
                {
                    context.BeginFigure(point, false, false); // a real gap breaks the line
                }

                previous = i;
            }
        }

        trend.Freeze();
        dc.DrawGeometry(null, new Pen(line, 1.6) { LineJoin = PenLineJoin.Round }, trend);

        // detector events (live view): small ticks along the top edge
        foreach (var eventMs in _data.EventsMs)
        {
            if (Inside(eventMs))
            {
                var x = X(eventMs);
                dc.DrawLine(new Pen(warnBrush, 2.2), new Point(x, plot.Top), new Point(x, plot.Top + 9));
            }
        }

        // the end of the window, the mark and the chosen event
        if (_data.WindowEndMs is { } end && Inside(end))
        {
            dc.DrawLine(new Pen(grid, 1) { DashStyle = DashStyles.Dot }, new Point(X(end), plot.Top), new Point(X(end), plot.Bottom));
        }

        if (_data.MarkMs is { } markMs && Inside(markMs))
        {
            var x = X(markMs);
            dc.DrawLine(new Pen(markBrush, 1.5) { DashStyle = DashStyles.Dash }, new Point(x, plot.Top), new Point(x, plot.Bottom));
            Label(dc, "key pressed", markBrush, new Point(Math.Min(x + 4, plot.Right - 66), plot.Bottom - 17), dpi, 10.5);
        }

        if (_data.AnchorMs is { } anchorMs && Inside(anchorMs))
        {
            var x = X(anchorMs);
            dc.DrawLine(new Pen(anchorBrush, 2.5), new Point(x, plot.Top), new Point(x, plot.Bottom));
            Label(dc, "event", anchorBrush, new Point(Math.Min(x + 5, plot.Right - 40), plot.Top + 12), dpi, 11);
        }

        foreach (var candidate in _data.Candidates)
        {
            if (!Inside(candidate.Ms))
            {
                continue;
            }

            var center = new Point(X(candidate.Ms), plot.Top + 9);
            dc.DrawEllipse(candidate.Weak ? Themed("ChartBackgroundBrush", Brushes.Black) : warnBrush, new Pen(warnBrush, 1.5), center, 8.5, 8.5);
            var number = new FormattedText(candidate.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, candidate.Weak ? warnBrush : Brushes.White, dpi);
            dc.DrawText(number, new Point(center.X - number.Width / 2, center.Y - number.Height / 2));
        }

        // Interactive crosshair & floating HUD pill
        if (_cursorMs is { } cursor && Inside(cursor))
        {
            var x = X(cursor);
            dc.DrawLine(new Pen(text, 1) { DashStyle = DashStyles.Dash }, new Point(x, plot.Top), new Point(x, plot.Bottom));
            if (DescribeCursor() is { } hudText)
            {
                var formatted = new FormattedText(hudText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 11, Themed("ForegroundBrush", Brushes.White), dpi);
                var boxWidth = formatted.Width + 16;
                var boxHeight = formatted.Height + 8;
                var boxX = Math.Clamp(x + 10, plot.Left + 4, Math.Max(plot.Left + 4, plot.Right - boxWidth - 4));
                var boxY = plot.Top + 22;
                var hudRect = new Rect(boxX, boxY, boxWidth, boxHeight);
                dc.DrawRoundedRectangle(Themed("PanelRaisedBrush", Brushes.DarkSlateGray), new Pen(Themed("AccentBrush", Brushes.DeepSkyBlue), 1), hudRect, 4, 4);
                dc.DrawText(formatted, new Point(boxX + 8, boxY + 4));
            }
        }

        if (IsKeyboardFocused)
        {
            dc.DrawRoundedRectangle(null, new Pen(Themed("ChartMarkBrush", Brushes.Orange), 1.5) { DashStyle = DashStyles.Dot }, new Rect(1, 1, width - 2, height - 2), 8, 8);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _cursorMs = ToTimeMs(e.GetPosition(this).X);
        ToolTip = DescribeCursor();
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsKeyboardFocused)
        {
            _cursorMs = null;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Focus();
        if (IsInteractive && ToTimeMs(e.GetPosition(this).X) is { } ms)
        {
            TimeChosen?.Invoke(this, ms);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var range = _data.EndMs - _data.StartMs;
        if (range <= 0)
        {
            return;
        }

        var stepMs = range * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.05 : 0.01);
        var current = _cursorMs ?? _data.MarkMs ?? _data.EndMs;
        switch (e.Key)
        {
            case Key.Left:
                _cursorMs = Math.Max(_data.StartMs, current - stepMs);
                break;
            case Key.Right:
                _cursorMs = Math.Min(_data.EndMs, current + stepMs);
                break;
            case Key.Home:
                _cursorMs = _data.StartMs;
                break;
            case Key.End:
                _cursorMs = _data.EndMs;
                break;
            case Key.Enter or Key.Space when IsInteractive && _cursorMs is { } chosen:
                TimeChosen?.Invoke(this, chosen);
                e.Handled = true;
                return;
            default:
                return;
        }

        e.Handled = true;
        ToolTip = DescribeCursor();
        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _cursorMs ??= _data.MarkMs ?? _data.EndMs;
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _dirty = true;
    }

    /// <summary>What is under the cursor, in words: the time (from the mark when there is one) and the worst frame near it.</summary>
    public string? DescribeCursor()
    {
        if (_cursorMs is not { } cursor)
        {
            return null;
        }

        var reference = _data.RelativeToMs ?? _data.StartMs;
        var seconds = (cursor - reference) / 1000.0;
        var when = (seconds >= 0 ? "+" : "-") + Math.Abs(seconds).ToString("0.00", CultureInfo.InvariantCulture) + " s";
        var index = _buckets.Length == 0 ? -1 : (int)Math.Clamp((cursor - _data.StartMs) / (_data.EndMs - _data.StartMs) * _buckets.Length, 0, _buckets.Length - 1);
        if (index >= 0 && _buckets[index].IsEmpty)
        {
            // At a zoomed view an empty column is normal; look for the nearest frame within the gap length before saying there is none.
            for (var distance = 1; distance <= _gapColumns && _buckets[index].IsEmpty; distance++)
            {
                foreach (var candidate in new[] { index - distance, index + distance })
                {
                    if (candidate >= 0 && candidate < _buckets.Length && !_buckets[candidate].IsEmpty)
                    {
                        index = candidate;
                        break;
                    }
                }
            }
        }

        if (index < 0 || _buckets[index].IsEmpty)
        {
            return $"{when}: no frames logged here";
        }

        return $"{when}: worst frame {_buckets[index].MaxMs.ToString("0.0", CultureInfo.InvariantCulture)} ms ({_buckets[index].Count} frame(s))";
    }

    private void EnsureBuckets(Rect plot)
    {
        var count = Math.Max(1, (int)Math.Round(plot.Width));
        if (!_dirty && count == _bucketCount)
        {
            return;
        }

        _bucketCount = count;
        _dirty = false;
        _buckets = _data.Series is { } series
            ? TimelineBuckets.Build(series, _data.StartMs, _data.EndMs, count)
            : TimelineBuckets.Build(_data.Samples.Span, _data.StartMs, _data.EndMs, count);

        // How many empty columns in a row make a real gap: three typical frame intervals (at least 50 ms), expressed in columns of this width.
        var means = _buckets.Where(b => !b.IsEmpty).Select(b => b.MeanMs).Order().ToArray();
        var typicalFrameMs = means.Length == 0 ? 16.7 : means[means.Length / 2];
        var columnMs = (_data.EndMs - _data.StartMs) / count;
        _gapColumns = Math.Max(1, (int)Math.Ceiling(Math.Max(50.0, 3.0 * typicalFrameMs) / columnMs));
    }

    private static void Label(DrawingContext dc, string text, Brush brush, Point at, double dpi, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi);
        dc.DrawText(formatted, at);
    }

    private static Brush Translucent(Brush brush, double opacity)
    {
        if (brush is SolidColorBrush solid)
        {
            var copy = new SolidColorBrush(solid.Color) { Opacity = opacity };
            copy.Freeze();
            return copy;
        }

        return brush;
    }

    private Brush Themed(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
