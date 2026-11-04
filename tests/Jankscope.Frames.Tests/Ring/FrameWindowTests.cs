using Jankscope.Frames.Ring;

namespace Jankscope.Frames.Tests.Ring;

public class FrameWindowTests
{
    /// <summary>Eleven samples at 0, 10, ... 100 ms; the sample's index equals its timestamp / 10.</summary>
    private static FrameSample[] Ladder() =>
        Enumerable.Range(0, 11).Select(i => new FrameSample(i, i * 10.0, 10.0)).ToArray();

    private static long[] Indices(ReadOnlySpan<FrameSample> samples)
    {
        var result = new long[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            result[i] = samples[i].Index;
        }

        return result;
    }

    [Fact]
    public void SelectWindow_includes_both_ends()
    {
        // [70, 100] -> 70, 80, 90, 100
        Assert.Equal([7L, 8, 9, 10], Indices(FrameRingExtensions.SelectWindow(Ladder(), endTimestampMs: 100, windowMs: 30)));
    }

    [Fact]
    public void SelectWindow_ends_at_the_mark_when_the_mark_is_in_the_past()
    {
        // mark at 95 ms, 30 ms pre-roll -> [65, 95] -> 70, 80, 90
        Assert.Equal([7L, 8, 9], Indices(FrameRingExtensions.SelectWindow(Ladder(), 95, 30)));
    }

    [Fact]
    public void SelectWindow_of_zero_length_keeps_only_samples_at_the_mark()
    {
        Assert.Equal([5L], Indices(FrameRingExtensions.SelectWindow(Ladder(), 50, 0)));
        Assert.Empty(FrameRingExtensions.SelectWindow(Ladder(), 55, 0).ToArray());
    }

    [Fact]
    public void SelectWindow_larger_than_the_data_returns_everything_up_to_the_mark()
    {
        Assert.Equal(11, FrameRingExtensions.SelectWindow(Ladder(), 100, 10_000).Length);
        Assert.Equal(6, FrameRingExtensions.SelectWindow(Ladder(), 50, 10_000).Length);
    }

    [Fact]
    public void SelectWindow_before_the_first_sample_or_on_nothing_is_empty()
    {
        Assert.True(FrameRingExtensions.SelectWindow(Ladder(), -5, 20).IsEmpty);
        Assert.True(FrameRingExtensions.SelectWindow(ReadOnlySpan<FrameSample>.Empty, 100, 20).IsEmpty);
    }

    [Theory]
    [InlineData(double.NaN, 10.0)]
    [InlineData(double.PositiveInfinity, 10.0)]
    [InlineData(100.0, double.NaN)]
    [InlineData(100.0, double.PositiveInfinity)]
    [InlineData(100.0, -1.0)]
    public void Invalid_arguments_are_rejected_not_silently_treated_as_an_empty_window(double end, double window)
    {
        var samples = Ladder();
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameRingExtensions.SelectWindow(samples, end, window).ToArray());
    }

    [Fact]
    public void SnapshotWindow_ends_at_the_newest_sample_by_default()
    {
        var ring = new RingBuffer<FrameSample>(8);
        foreach (var s in Ladder())
        {
            ring.Write(s); // capacity 8 -> holds indices 3..10 (30..100 ms)
        }

        Assert.Equal([8L, 9, 10], Indices(ring.SnapshotWindow(windowMs: 20)));
        Assert.Equal([3L, 4, 5, 6, 7, 8, 9, 10], Indices(ring.SnapshotWindow(windowMs: 1_000))); // the ring only holds eight
    }

    [Fact]
    public void SnapshotWindow_at_an_explicit_mark_is_a_pre_roll_selection()
    {
        var ring = new RingBuffer<FrameSample>(16);
        foreach (var s in Ladder())
        {
            ring.Write(s);
        }

        Assert.Equal([3L, 4, 5], Indices(ring.SnapshotWindow(endTimestampMs: 50, windowMs: 20)));
    }

    [Fact]
    public void SnapshotWindow_of_an_empty_ring_is_empty()
    {
        var ring = new RingBuffer<FrameSample>(4);
        Assert.Empty(ring.SnapshotWindow(100));
        Assert.Empty(ring.SnapshotWindow(10, 100));
    }

    [Fact]
    public void SelectWindow_cuts_at_a_clock_restart_or_application_change_instead_of_mixing_epochs()
    {
        var samples = new[]
        {
            new FrameSample(0, 50_000, 16.7, Application: "game.exe"),
            new FrameSample(1, 50_016.7, 16.7, Application: "game.exe"),
            new FrameSample(0, 10, 16.7, Application: "game.exe"),
            new FrameSample(1, 20, 16.7, Application: "game.exe"),
            new FrameSample(2, 30, 16.7, Application: "game.exe"),
        };

        var window = FrameRingExtensions.SelectWindow(samples, endTimestampMs: 30, windowMs: 120_000);
        Assert.Equal([10.0, 20.0, 30.0], window.ToArray().Select(s => s.TimestampMs).ToArray());

        var switchedApp = new[]
        {
            new FrameSample(0, 10, 16.7, Application: "first.exe"),
            new FrameSample(1, 20, 16.7, Application: "first.exe"),
            new FrameSample(2, 30, 16.7, Application: "second.exe"),
            new FrameSample(3, 40, 16.7, Application: "second.exe"),
        };

        var appWindow = FrameRingExtensions.SelectWindow(switchedApp, endTimestampMs: 40, windowMs: 120_000);
        Assert.Equal([30.0, 40.0], appWindow.ToArray().Select(s => s.TimestampMs).ToArray());
    }
}
