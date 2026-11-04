using Jankscope.Frames.Detection;

namespace Jankscope.Frames.Tests.Detection;

public class StutterOptionsTests
{
    [Fact]
    public void Defaults_are_the_documented_definition_and_uncalibrated()
    {
        var o = new StutterOptions();

        Assert.Equal(1000.0, o.RollingWindowMs);
        Assert.Equal(2.0, o.Multiplier);
        Assert.Equal(4.0, o.MinExcessMs);
        Assert.Equal(10, o.MinHistoryFrames);
        Assert.Equal(500.0, o.ClusterGapMs);
        Assert.False(o.Calibrated);
        Assert.Equal(o, StutterOptions.Default);
    }

    [Fact]
    public void Default_options_validate()
    {
        StutterOptions.Default.Validate();
        new StutterOptions(Multiplier: 1.0, MinExcessMs: 0.0, MinHistoryFrames: 1, ClusterGapMs: 0.0).Validate();
    }

    [Theory]
    [InlineData(nameof(StutterOptions.RollingWindowMs), 0.0)]
    [InlineData(nameof(StutterOptions.RollingWindowMs), -1.0)]
    [InlineData(nameof(StutterOptions.RollingWindowMs), double.NaN)]
    [InlineData(nameof(StutterOptions.RollingWindowMs), double.PositiveInfinity)]
    [InlineData(nameof(StutterOptions.Multiplier), 0.99)]
    [InlineData(nameof(StutterOptions.Multiplier), double.NaN)]
    [InlineData(nameof(StutterOptions.Multiplier), double.PositiveInfinity)]
    [InlineData(nameof(StutterOptions.MinExcessMs), -0.1)]
    [InlineData(nameof(StutterOptions.MinExcessMs), double.NaN)]
    [InlineData(nameof(StutterOptions.MinHistoryFrames), 0.0)]
    [InlineData(nameof(StutterOptions.MinHistoryFrames), -5.0)]
    [InlineData(nameof(StutterOptions.ClusterGapMs), -1.0)]
    [InlineData(nameof(StutterOptions.ClusterGapMs), double.NaN)]
    public void Nonsensical_values_are_rejected_with_the_name_of_the_offending_property(string property, double value)
    {
        var options = property switch
        {
            nameof(StutterOptions.RollingWindowMs) => new StutterOptions(RollingWindowMs: value),
            nameof(StutterOptions.Multiplier) => new StutterOptions(Multiplier: value),
            nameof(StutterOptions.MinExcessMs) => new StutterOptions(MinExcessMs: value),
            nameof(StutterOptions.MinHistoryFrames) => new StutterOptions(MinHistoryFrames: (int)value),
            nameof(StutterOptions.ClusterGapMs) => new StutterOptions(ClusterGapMs: value),
            _ => throw new InvalidOperationException(property),
        };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        Assert.Equal(property, ex.ParamName);
    }

    [Fact]
    public void The_detector_and_the_matcher_refuse_invalid_options()
    {
        var bad = new StutterOptions(Multiplier: 0.5);

        Assert.Throws<ArgumentOutOfRangeException>(() => new StutterDetector(bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => StutterDetector.Analyze([], bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundTruthMatcher.Match([], [], 1, bad, 16.7));
    }
}
