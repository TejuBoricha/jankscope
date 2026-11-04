using Jankscope.Core.Hardware;
using Jankscope.Platform.Pdh;

namespace Jankscope.Platform.Tests.Pdh;

/// <summary>The exact flags passed to PdhGetFormattedCounterArray (review 1a, finding 19, mutation L6): losing NOCAP100 would silently cap a processor utility of 120 % at 100.</summary>
public class PdhFormatFlagsTests
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;

    [Fact]
    public void Double_formatting_is_always_requested_and_the_cap_is_removed_only_when_the_counter_asks_for_it()
    {
        Assert.Equal(PdhFmtDouble, PdhCounterSource.FormatFlagsFor(noCap100: false));
        Assert.Equal(PdhFmtDouble | PdhFmtNoCap100, PdhCounterSource.FormatFlagsFor(noCap100: true));
    }

    [Fact]
    public void The_documented_values_of_the_two_flags_are_the_ones_the_code_uses()
    {
        // winapi pdh.h: PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000.
        Assert.Equal(PdhFmtDouble, NativePdh.FormatDouble);
        Assert.Equal(PdhFmtNoCap100, NativePdh.FormatNoCap100);
    }

    [Fact]
    public void The_processor_utility_counter_is_the_one_that_must_not_be_capped()
    {
        var utility = CounterSpecs.All.Single(c => c.Id == CounterIds.CpuUtility);
        Assert.True(utility.NoCap100);
        Assert.All(CounterSpecs.All.Where(c => c.Id != CounterIds.CpuUtility), c => Assert.False(c.NoCap100, c.Id));
    }
}
