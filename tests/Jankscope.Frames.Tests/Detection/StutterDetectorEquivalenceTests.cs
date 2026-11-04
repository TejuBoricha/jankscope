using Jankscope.Frames.Detection;

namespace Jankscope.Frames.Tests.Detection;

/// <summary>Batch == streaming == the naive oracle, on seeded random series that include every kind of bad input.</summary>
public class StutterDetectorEquivalenceTests
{
    private static readonly double[] BaseFrameTimes = [2.0, 4.17, 6.94, 8.33, 16.67, 33.3, 50.0];
    private static readonly double[] Windows = [50.0, 250.0, 1000.0, 1500.0];
    private static readonly int[] Histories = [1, 3, 10, 30];
    private static readonly double[] Multipliers = [1.0, 1.5, 2.0, 3.0];
    private static readonly double[] Excesses = [0.0, 1.0, 4.0, 10.0];

    public static TheoryData<int> Seeds
    {
        get
        {
            var data = new TheoryData<int>();
            for (var seed = 1; seed <= 60; seed++)
            {
                data.Add(seed);
            }

            return data;
        }
    }

    private static (FrameSample[] Frames, StutterOptions Options) RandomCase(int seed)
    {
        var random = new Random(seed);
        var options = new StutterOptions(
            RollingWindowMs: Windows[random.Next(Windows.Length)],
            Multiplier: Multipliers[random.Next(Multipliers.Length)],
            MinExcessMs: Excesses[random.Next(Excesses.Length)],
            MinHistoryFrames: Histories[random.Next(Histories.Length)],
            ClusterGapMs: random.Next(3) * 250.0,
            Calibrated: random.Next(2) == 0);

        var baseMs = BaseFrameTimes[random.Next(BaseFrameTimes.Length)];
        var count = 400 + random.Next(2600);
        var frames = new FrameSample[count];
        var timestamp = 0.0;
        for (var i = 0; i < count; i++)
        {
            var frameTime = baseMs * (0.8 + (random.NextDouble() * 0.4));
            if (random.Next(100) < 3)
            {
                frameTime *= 1.5 + (random.NextDouble() * 9.0); // a spike, sometimes below and sometimes above the threshold
            }

            // Every ~2 s a run of identical values so the window holds many duplicates (the structure must cope with ties).
            if ((i / 100) % 20 == 7)
            {
                frameTime = baseMs;
            }

            timestamp += frameTime;
            var reportedFrameTime = frameTime;
            var reportedTimestamp = timestamp;
            var roll = random.Next(1000);
            if (roll < 4)
            {
                reportedFrameTime = double.NaN;
            }
            else if (roll < 7)
            {
                reportedFrameTime = -frameTime;
            }
            else if (roll < 9)
            {
                reportedFrameTime = 0.0;
            }
            else if (roll < 10)
            {
                reportedTimestamp = double.PositiveInfinity;
            }
            else if (roll < 11)
            {
                reportedTimestamp = timestamp - 500.0; // the clock jumps back once; later frames carry on from the real clock
            }

            frames[i] = new FrameSample(i, reportedTimestamp, reportedFrameTime);
        }

        return (frames, options);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_sliding_median_structure_agrees_with_the_naive_oracle(int seed)
    {
        var (frames, options) = RandomCase(seed);

        var actual = StutterDetector.Analyze(frames, options);
        var expected = NaiveStutterOracle.Run(frames, options);

        Assert.Equal(expected.Events, actual.Events);
        Assert.Equal(expected.Evaluated, actual.FramesEvaluated);
        Assert.Equal(expected.InsufficientHistory, actual.InsufficientHistoryFrames);
        Assert.Equal(expected.Rejected, actual.RejectedFrames);
        Assert.Equal(expected.ClockDiscontinuities, actual.ClockDiscontinuities);
        Assert.Equal(frames.Length, actual.FramesSeen);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Batch_and_streaming_produce_identical_results(int seed)
    {
        var (frames, options) = RandomCase(seed);

        var batch = StutterDetector.Analyze(frames, options);

        var detector = new StutterDetector(options);
        var streamedEvents = new List<StutterEvent>();
        foreach (var frame in frames)
        {
            var verdict = detector.Push(frame);
            if (verdict.Status == FrameStatus.Stutter)
            {
                Assert.NotNull(verdict.Event);
                streamedEvents.Add(verdict.Event.Value);
            }
            else
            {
                Assert.Null(verdict.Event);
            }
        }

        var streamed = detector.GetResult();
        Assert.Equal(batch.Events, streamed.Events);
        Assert.Equal(streamedEvents, streamed.Events);
        Assert.Equal(batch.Clusters, streamed.Clusters);
        Assert.Equal(batch.FramesSeen, streamed.FramesSeen);
        Assert.Equal(batch.FramesEvaluated, streamed.FramesEvaluated);
        Assert.Equal(batch.InsufficientHistoryFrames, streamed.InsufficientHistoryFrames);
        Assert.Equal(batch.RejectedFrames, streamed.RejectedFrames);
        Assert.Equal(batch.RejectedByReason.OrderBy(p => p.Key), streamed.RejectedByReason.OrderBy(p => p.Key));
        Assert.Equal(batch.ClockDiscontinuities, streamed.ClockDiscontinuities);
        Assert.Equal(batch.Calibrated, streamed.Calibrated);
        Assert.Equal(batch.Options, streamed.Options);
    }

    [Fact]
    public void The_random_cases_really_exercise_events_rejections_warm_ups_and_clock_jumps()
    {
        long events = 0, rejected = 0, insufficient = 0, discontinuities = 0, evaluated = 0;
        for (var seed = 1; seed <= 60; seed++)
        {
            var (frames, options) = RandomCase(seed);
            var result = StutterDetector.Analyze(frames, options);
            events += result.Events.Count;
            rejected += result.RejectedFrames;
            insufficient += result.InsufficientHistoryFrames;
            discontinuities += result.ClockDiscontinuities;
            evaluated += result.FramesEvaluated;
        }

        Assert.True(events > 200, $"only {events} events across the random cases");
        Assert.True(rejected > 100, $"only {rejected} rejected frames");
        Assert.True(insufficient > 100);
        Assert.True(discontinuities > 5, $"only {discontinuities} clock jumps");
        Assert.True(evaluated > 20_000);
    }

    [Fact]
    public void Resetting_a_detector_gives_the_same_result_as_a_fresh_one()
    {
        var (frames, options) = RandomCase(3);
        var detector = new StutterDetector(options);
        foreach (var f in frames)
        {
            detector.Push(f);
        }

        detector.Reset();
        foreach (var f in frames)
        {
            detector.Push(f);
        }

        Assert.Equal(StutterDetector.Analyze(frames, options).Events, detector.GetResult().Events);
        Assert.Equal(frames.Length, detector.GetResult().FramesSeen);
    }

    [Fact]
    public void A_result_snapshot_is_not_changed_by_frames_pushed_afterwards()
    {
        var detector = new StutterDetector();
        foreach (var s in Series.FromFrameTimes(Series.Join(Series.Constant(10.0, 20), [100.0])))
        {
            detector.Push(s);
        }

        var snapshot = detector.GetResult();
        detector.Push(new FrameSample(21, 10_000.0, 500.0));

        Assert.Single(snapshot.Events);
        Assert.Equal(21L, snapshot.FramesSeen);
        Assert.Equal(22L, detector.GetResult().FramesSeen);
    }
}
