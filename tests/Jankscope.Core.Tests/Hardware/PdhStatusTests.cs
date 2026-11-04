using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

public class PdhStatusTests
{
    [Theory]
    [InlineData(0u, true)]
    [InlineData(1u, true)]
    [InlineData(0xC0000BBAu, false)]
    [InlineData(0x800007D6u, false)]
    [InlineData(0xC0000BB8u, false)]
    [InlineData(2u, false)]
    [InlineData(uint.MaxValue, false)]
    public void Only_valid_data_and_new_data_carry_a_value(uint status, bool hasValue)
    {
        Assert.Equal(hasValue, PdhStatus.CarriesValue(status));
    }

    [Theory]
    [InlineData(0xC0000BB8u, "PDH_CSTATUS_NO_OBJECT", "performance object")]
    [InlineData(0xC0000BB9u, "PDH_CSTATUS_NO_COUNTER", "counter")]
    [InlineData(0xC0000BBAu, "PDH_INVALID_DATA", "no valid data")]
    [InlineData(0x800007D6u, "PDH_CALC_NEGATIVE_DENOMINATOR", "second sample")]
    [InlineData(0x800007D5u, "PDH_NO_DATA", "no data")]
    [InlineData(0x800007D1u, "PDH_CSTATUS_NO_INSTANCE", "instance")]
    [InlineData(0xC0000BC0u, "PDH_CSTATUS_BAD_COUNTERNAME", "counter name")]
    [InlineData(0xC0000BC6u, "PDH_INVALID_DATA", "second sample")]
    [InlineData(0xC0000BC4u, "PDH_INVALID_PATH", "counter path")]
    [InlineData(0xC0000BC5u, "PDH_INVALID_INSTANCE", "instance")]
    public void Known_statuses_are_named_with_their_constant_and_a_plain_explanation(uint status, string constant, string phrase)
    {
        var text = PdhStatus.Describe(status);
        Assert.Contains(constant, text, StringComparison.Ordinal);
        Assert.Contains(phrase, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0xDEADBEEFu, "0xDEADBEEF")]
    [InlineData(0x12u, "0x00000012")]
    public void An_unknown_status_is_shown_as_hex_and_never_hidden(uint status, string hex)
    {
        Assert.Contains(hex, PdhStatus.Describe(status), StringComparison.Ordinal);
    }

    [Fact]
    public void A_status_that_means_no_second_sample_yet_is_recognised_as_transient()
    {
        Assert.True(PdhStatus.NeedsSecondSample(0x800007D6));
        Assert.True(PdhStatus.NeedsSecondSample(0xC0000BBA));
        Assert.True(PdhStatus.NeedsSecondSample(0xC0000BC6)); // observed from the array call for the Pages Input/sec counter on its first collection
        Assert.False(PdhStatus.NeedsSecondSample(0xC0000BB8));
        Assert.False(PdhStatus.NeedsSecondSample(0));
    }

    [Fact]
    public void Every_described_status_is_non_empty_ascii()
    {
        foreach (var status in new uint[] { 0, 1, 0x800007D0, 0x800007D1, 0x800007D2, 0x800007D5, 0x800007D6, 0xC0000BB8, 0xC0000BB9, 0xC0000BBA, 0xC0000BBC, 0xC0000BBD, 0xC0000BC0, 77 })
        {
            var text = PdhStatus.Describe(status);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.True(text.All(c => c is >= ' ' and <= '~'), text);
        }
    }
}
