using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Jankscope.App.Views;

/// <summary>
/// Frame-time distribution and pacing consistency breakdown chart.
/// Left bay renders a 270-degree radial arc gauge for the 0-100 Pacing Consistency Score.
/// Right bay renders 5-tier frame-latency distribution bars (< 4.2 ms / 240+ Hz through > 33.3 ms / Hitch Stall).
/// </summary>
public sealed class FrameDistributionView : FrameworkElement
{
    private static readonly (double MaxMs, string Label, string CompactLabel, string BrushKey)[] Bands =
    [
        (4.17, "< 4.2 ms", "< 4.2 ms", "CyanAccentBrush"),
        (8.34, "4.2 - 8.3 ms", "4.2-8.3", "OkBrush"),
        (16.67, "8.3 - 16.7 ms", "8.3-16.7", "ChartLineBrush"),
        (33.34, "16.7 - 33.3 ms", "16.7-33", "WarningBrush"),
        (double.PositiveInfinity, "> 33.3 ms", "> 33.3 ms", "DangerBrush"),
    ];

    private int[] _counts = new int[5];
    private int _totalFrames;
    private double _consistencyScore = 100.0;
    private string _verdict = "Waiting for frames";

    public FrameDistributionView()
    {
        MinHeight = 148;
        ClipToBounds = true;
    }

    public int TotalFrames => _totalFrames;

    public double ConsistencyScore => _consistencyScore;

    public string Verdict => _verdict;

    public IReadOnlyList<int> Counts => _counts;

    /// <summary>Computes the 0-100 Pacing Consistency Score and tier counts from a span of frame times in milliseconds.</summary>
    public static (int[] Counts, int Total, double Score, string Verdict) Compute(ReadOnlySpan<double> frameTimesMs)
    {
        var counts = new int[5];
        var valid = 0;
        var list = new List<double>(frameTimesMs.Length);
        for (var i = 0; i < frameTimesMs.Length; i++)
        {
            var ms = frameTimesMs[i];
            if (!double.IsFinite(ms) || ms <= 0)
            {
                continue;
            }

            valid++;
            list.Add(ms);
            if (ms <= 4.17)
            {
                counts[0]++;
            }
            else if (ms <= 8.34)
            {
                counts[1]++;
            }
            else if (ms <= 16.67)
            {
                counts[2]++;
            }
            else if (ms <= 33.34)
            {
                counts[3]++;
            }
            else
            {
                counts[4]++;
            }
        }

        if (valid == 0)
        {
            return (counts, 0, 0.0, "No frames in window");
        }

        list.Sort();
        var p50 = list[list.Count / 2];
        var p99Index = Math.Clamp((int)Math.Ceiling(list.Count * 0.99) - 1, 0, list.Count - 1);
        var p99 = list[p99Index];

        var tailRatio = p99 > 0 ? Math.Clamp(p50 / p99, 0.0, 1.0) : 1.0;
        var hitchPenalty = Math.Min(45.0, (counts[4] / (double)valid) * 900.0);
        var score = Math.Round(Math.Clamp((tailRatio * 100.0) - hitchPenalty, 5.0, 100.0), 0);

        var verdict = score switch
        {
            >= 88 => "LOCKED // FLUID",
            >= 72 => "MINOR VARIANCE",
            >= 52 => "MICRO-STUTTER",
            _ => "SEVERE STALLS",
        };

        return (counts, valid, score, verdict);
    }

    public void Show(ReadOnlySpan<double> frameTimesMs)
    {
        var (counts, total, score, verdict) = Compute(frameTimesMs);
        _counts = counts;
        _totalFrames = total;
        _consistencyScore = score;
        _verdict = verdict;
        InvalidateVisual();
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
        var fg = Themed("ForegroundBrush", Brushes.White);
        var muted = Themed("MutedBrush", Brushes.Gray);

        dc.DrawRoundedRectangle(bg, new Pen(grid, 1), new Rect(0, 0, width, height), 6, 6);

        if (_totalFrames == 0)
        {
            DrawText(dc, "NO FRAMES IN WINDOW // START CAPTURE OR SIMULATOR", muted, new Point(16, (height / 2) - 7), dpi, 10.5, true, mono: true, maxWidth: width - 32, maxLines: 3);
            return;
        }

        // Left bay: 270-degree Radial Smoothness Arc Gauge
        var compact = width < 380.0;
        var gaugeBayWidth = compact ? Math.Min(108.0, width * 0.35) : Math.Min(160.0, Math.Max(130.0, width * 0.31));
        dc.DrawLine(new Pen(grid, 1), new Point(gaugeBayWidth, 8), new Point(gaugeBayWidth, height - 8));

        var scoreBrush = _consistencyScore switch
        {
            >= 85 => Themed("OkBrush", Brushes.MediumSeaGreen),
            >= 60 => Themed("WarningBrush", Brushes.Goldenrod),
            _ => Themed("DangerBrush", Brushes.IndianRed),
        };

        DrawText(dc, compact ? "SMOOTH" : "SMOOTHNESS INDEX", muted, new Point(10, 7), dpi, 9.0, true, mono: true);

        var cx = gaugeBayWidth * 0.5;
        var cy = Math.Min(height * 0.50, 74.0);
        var radius = Math.Clamp(Math.Min(gaugeBayWidth * 0.31, (height - 54) * 0.44), 21.0, 38.0);

        DrawArc(dc, new Point(cx, cy), radius, 135.0, 405.0, new Pen(Themed("PanelRaisedBrush", Brushes.DarkSlateGray), 5.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var sweep = 270.0 * Math.Clamp(_consistencyScore / 100.0, 0.02, 1.0);
        DrawArc(dc, new Point(cx, cy), radius, 135.0, 135.0 + sweep, new Pen(scoreBrush, 5.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        var scoreStr = _consistencyScore.ToString("0", CultureInfo.InvariantCulture);
        var scoreFmt = new FormattedText(scoreStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), compact ? 16 : 20, scoreBrush, dpi);
        dc.DrawText(scoreFmt, new Point(cx - (scoreFmt.Width * 0.5), cy - (scoreFmt.Height * 0.5) - 1));

        var verdictFmt = new FormattedText(_verdict, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), compact ? 8.5 : 9.5, fg, dpi);
        dc.DrawText(verdictFmt, new Point(Math.Max(4, cx - (verdictFmt.Width * 0.5)), cy + radius + 4));

        var countStr = $"{_totalFrames.ToString(CultureInfo.InvariantCulture)} frames";
        var countFmt = new FormattedText(countStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 8.8, muted, dpi);
        dc.DrawText(countFmt, new Point(Math.Max(4, cx - (countFmt.Width * 0.5)), Math.Min(height - 14, cy + radius + 17)));

        // Right bay: 5-tier latency distribution histogram
        var barsLeft = gaugeBayWidth + 10;
        DrawText(dc, compact ? "TIERS" : "FRAME-TIME TIER DISTRIBUTION", muted, new Point(barsLeft, 7), dpi, 9.0, true, mono: true);

        var topOffset = 24.0;
        var rowHeight = Math.Max(18.0, (height - topOffset - 8) / Bands.Length);

        for (var i = 0; i < Bands.Length; i++)
        {
            var y = topOffset + (i * rowHeight);
            var count = _counts[i];
            var fraction = _totalFrames > 0 ? count / (double)_totalFrames : 0.0;
            var pctText = compact
                ? $"{(fraction * 100.0).ToString("0.0", CultureInfo.InvariantCulture)}%"
                : $"{(fraction * 100.0).ToString("0.0", CultureInfo.InvariantCulture)}% ({count.ToString(CultureInfo.InvariantCulture)})";

            DrawText(dc, compact ? Bands[i].CompactLabel : Bands[i].Label, fg, new Point(barsLeft, y + 1), dpi, compact ? 9.2 : 10, true, mono: true);

            var labelColW = compact ? 58.0 : 90.0;
            var rightColW = compact ? 44.0 : 84.0;
            var trackLeft = barsLeft + labelColW;
            var trackRight = Math.Max(trackLeft + 20, width - rightColW);
            var trackWidth = trackRight - trackLeft;
            var barH = Math.Clamp(rowHeight - 9, 6.0, 12.0);
            var barY = y + ((rowHeight - barH) * 0.45);
            var barRect = new Rect(trackLeft, barY, trackWidth, barH);
            dc.DrawRoundedRectangle(Themed("PanelRaisedBrush", Brushes.DarkSlateGray), null, barRect, 3, 3);

            if (fraction > 0)
            {
                var fillWidth = Math.Max(4, trackWidth * fraction);
                var fillRect = new Rect(trackLeft, barY, Math.Min(trackWidth, fillWidth), barH);
                dc.DrawRoundedRectangle(Themed(Bands[i].BrushKey, Brushes.DodgerBlue), null, fillRect, 3, 3);
            }

            DrawText(dc, pctText, muted, new Point(trackRight + 5, y + 1), dpi, compact ? 9.0 : 9.5, false, mono: true);
        }
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double startDeg, double endDeg, Pen pen)
    {
        var sweep = endDeg - startDeg;
        if (sweep <= 0.1)
        {
            return;
        }

        var startRad = startDeg * Math.PI / 180.0;
        var endRad = endDeg * Math.PI / 180.0;
        var p0 = new Point(center.X + (radius * Math.Cos(startRad)), center.Y + (radius * Math.Sin(startRad)));
        var p1 = new Point(center.X + (radius * Math.Cos(endRad)), center.Y + (radius * Math.Sin(endRad)));

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(p0, false, false);
            ctx.ArcTo(p1, new Size(radius, radius), 0, sweep > 180.0, SweepDirection.Clockwise, true, false);
        }

        geom.Freeze();
        dc.DrawGeometry(null, pen, geom);
    }

    /// <summary>Draws text that stays inside <paramref name="maxWidth"/>: it is cut with an ellipsis (<paramref name="maxLines"/> of 1) or wraps onto at most that many lines, never past the edge.</summary>
    private static void DrawText(DrawingContext dc, string text, Brush brush, Point at, double dpi, double size, bool bold, bool mono = false, double maxWidth = double.PositiveInfinity, int maxLines = 1)
    {
        var typeface = new Typeface(new FontFamily(mono ? "Consolas" : "Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush, dpi);
        if (double.IsFinite(maxWidth))
        {
            formatted.MaxTextWidth = Math.Max(1.0, maxWidth);
            formatted.MaxLineCount = Math.Max(1, maxLines);
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        dc.DrawText(formatted, at);
    }

    private Brush Themed(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
