using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Jankscope.App.Views;

/// <summary>
/// Frame-time distribution and pacing consistency breakdown chart. Shows how frames distribute across latency tiers
/// (Under 4.2 ms / 240+ FPS, 4.2-8.3 ms / 120-240 FPS, 8.3-16.7 ms / 60-120 FPS, 16.7-33.3 ms / 30-60 FPS, Over 33.3 ms / Hitch)
/// alongside a 0-100 Pacing Consistency Score and key percentile callouts.
/// </summary>
public sealed class FrameDistributionView : FrameworkElement
{
    private static readonly (double MaxMs, string Label, string SubLabel, string BrushKey)[] Bands =
    [
        (4.17, "< 4.2 ms", "240+ FPS Ultra-Low Latency", "CyanAccentBrush"),
        (8.34, "4.2 - 8.3 ms", "120 - 240 FPS High Refresh", "OkBrush"),
        (16.67, "8.3 - 16.7 ms", "60 - 120 FPS Standard Smooth", "ChartLineBrush"),
        (33.34, "16.7 - 33.3 ms", "30 - 60 FPS Heavy Frame", "WarningBrush"),
        (double.PositiveInfinity, "> 33.3 ms", "Sub-30 FPS / Hitch Stall", "DangerBrush"),
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
        var sum = 0.0;
        var list = new List<double>(frameTimesMs.Length);
        for (var i = 0; i < frameTimesMs.Length; i++)
        {
            var ms = frameTimesMs[i];
            if (!double.IsFinite(ms) || ms <= 0)
            {
                continue;
            }

            valid++;
            sum += ms;
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

        // Ratio of median to P99 measures tail stability (1.0 = flat line, < 0.35 = severe spikes)
        var tailRatio = p99 > 0 ? Math.Clamp(p50 / p99, 0.0, 1.0) : 1.0;
        var hitchPenalty = Math.Min(45.0, (counts[4] / (double)valid) * 900.0);
        var score = Math.Round(Math.Clamp((tailRatio * 100.0) - hitchPenalty, 5.0, 100.0), 0);

        var verdict = score switch
        {
            >= 88 => "Locked & Fluid Pacing",
            >= 72 => "Minor Frame-Time Variance",
            >= 52 => "Noticeable Micro-Stutter",
            _ => "Severe Hitching / Frame Stalls",
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

        dc.DrawRoundedRectangle(bg, new Pen(grid, 1), new Rect(0, 0, width, height), 8, 8);

        if (_totalFrames == 0)
        {
            DrawText(dc, "No frames collected in this window yet.", muted, new Point(16, (height / 2) - 8), dpi, 12, false);
            return;
        }

        // Left column: Consistency Score Badge
        const double scoreWidth = 175;
        dc.DrawLine(new Pen(grid, 1), new Point(scoreWidth, 12), new Point(scoreWidth, height - 12));

        var scoreBrush = _consistencyScore switch
        {
            >= 85 => Themed("OkBrush", Brushes.MediumSeaGreen),
            >= 60 => Themed("WarningBrush", Brushes.Goldenrod),
            _ => Themed("DangerBrush", Brushes.IndianRed),
        };

        DrawText(dc, "PACING CONSISTENCY", muted, new Point(16, 14), dpi, 10, true);
        DrawText(dc, $"{_consistencyScore.ToString("0", CultureInfo.InvariantCulture)} / 100", scoreBrush, new Point(16, 32), dpi, 24, true, mono: true);
        DrawText(dc, _verdict, fg, new Point(16, 66), dpi, 11.5, true);
        DrawText(dc, $"{_totalFrames.ToString(CultureInfo.InvariantCulture)} frames analyzed", muted, new Point(16, 86), dpi, 10.5, false);
        DrawText(dc, "Higher = flatter frame times", muted, new Point(16, 106), dpi, 10, false);

        // Right column: 5 horizontal latency distribution bars
        var barsLeft = scoreWidth + 16;
        var barsWidth = Math.Max(60, width - barsLeft - 16);
        var rowHeight = (height - 20) / Bands.Length;

        for (var i = 0; i < Bands.Length; i++)
        {
            var y = 10 + (i * rowHeight);
            var count = _counts[i];
            var fraction = _totalFrames > 0 ? count / (double)_totalFrames : 0.0;
            var pctText = $"{(fraction * 100.0).ToString("0.0", CultureInfo.InvariantCulture)}% ({count.ToString(CultureInfo.InvariantCulture)})";

            DrawText(dc, Bands[i].Label, fg, new Point(barsLeft, y + 2), dpi, 11, true, mono: true);

            var trackLeft = barsLeft + 105;
            var trackRight = width - 98;
            var trackWidth = Math.Max(20, trackRight - trackLeft);
            var barRect = new Rect(trackLeft, y + 4, trackWidth, Math.Max(8, rowHeight - 10));
            dc.DrawRoundedRectangle(Themed("PanelRaisedBrush", Brushes.DarkSlateGray), null, barRect, 4, 4);

            if (fraction > 0)
            {
                var fillWidth = Math.Max(4, trackWidth * fraction);
                var fillRect = new Rect(trackLeft, y + 4, Math.Min(trackWidth, fillWidth), Math.Max(8, rowHeight - 10));
                dc.DrawRoundedRectangle(Themed(Bands[i].BrushKey, Brushes.DodgerBlue), null, fillRect, 4, 4);
            }

            DrawText(dc, pctText, muted, new Point(trackRight + 8, y + 2), dpi, 10.5, false, mono: true);
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
