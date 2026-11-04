using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

public class ReadingAndLuidTests
{
    [Fact]
    public void An_available_reading_carries_its_value_and_no_reason()
    {
        var r = Reading.Of(12.5);
        Assert.True(r.IsAvailable);
        Assert.Equal(12.5, r.Value);
        Assert.Null(r.UnavailableReason);
    }

    [Fact]
    public void A_zero_is_a_real_measurement_and_stays_available()
    {
        var r = Reading.Of(0);
        Assert.True(r.IsAvailable);
        Assert.Equal(0.0, r.Value);
    }

    [Fact]
    public void An_unavailable_reading_has_no_value_at_all_never_zero()
    {
        var r = Reading.NotAvailable("counter object not found");
        Assert.False(r.IsAvailable);
        Assert.Null(r.Value);
        Assert.Equal("counter object not found", r.UnavailableReason);
    }

    [Fact]
    public void A_default_reading_is_unavailable_with_a_stated_reason_not_a_zero()
    {
        var r = default(Reading);
        Assert.False(r.IsAvailable);
        Assert.Null(r.Value);
        Assert.False(string.IsNullOrWhiteSpace(r.UnavailableReason));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_numbers_cannot_become_available_readings(double bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Reading.Of(bad));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unavailable_reading_must_say_why(string reason)
    {
        Assert.Throws<ArgumentException>(() => Reading.NotAvailable(reason));
    }

    [Fact]
    public void Readings_print_culture_independently()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("12.5", Reading.Of(12.5).ToString());
            Assert.StartsWith("not available", Reading.NotAvailable("x").ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Fact]
    public void Luid_prints_in_the_exact_spelling_of_performance_counter_instance_names()
    {
        Assert.Equal("0x00000000_0x00011B0C", new Luid(0, 0x11B0C).ToString());
        Assert.Equal("0x00000001_0xFFFFFFFF", new Luid(1, uint.MaxValue).ToString());
        Assert.Equal("0xFFFFFFFF_0x00000002", new Luid(-1, 2).ToString());
    }

    [Theory]
    [InlineData("0x00000000_0x00011B0C", 0, 0x11B0Cu)]
    [InlineData("0x00000000_0x00011fd6", 0, 0x11FD6u)]
    [InlineData("0X00000000_0X00011FA1", 0, 0x11FA1u)]
    [InlineData("0x0_0x1", 0, 1u)]
    [InlineData("0xFFFFFFFF_0x00000002", -1, 2u)]
    public void Luid_parses_the_counter_spelling(string text, int high, uint low)
    {
        Assert.True(Luid.TryParse(text, out var luid));
        Assert.Equal(new Luid(high, low), luid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x00000000")]
    [InlineData("0x00000000_")]
    [InlineData("00000000_00011B0C")]
    [InlineData("0x00000000_0x")]
    [InlineData("0x00000000_0x00011B0C_phys_0")]
    [InlineData("0x000000000_0x00011B0C")]
    [InlineData("0x00000000_0x00011B0G")]
    [InlineData("0x00000000-0x00011B0C")]
    [InlineData(" 0x00000000_0x00011B0C")]
    [InlineData("0x-1_0x2")]
    public void Luid_rejects_anything_that_is_not_exactly_two_hex_words(string text)
    {
        Assert.False(Luid.TryParse(text, out _));
    }

    [Fact]
    public void Luid_round_trips_through_int64_as_windows_defines_it()
    {
        var luid = Luid.FromInt64(0x0000000100011B0CL);
        Assert.Equal(new Luid(1, 0x11B0C), luid);
        Assert.Equal(0x0000000100011B0CL, luid.ToInt64());
        Assert.Equal(new Luid(-1, 5), Luid.FromInt64(unchecked((long)0xFFFFFFFF00000005UL)));
    }

    [Fact]
    public void Luid_orders_by_its_numeric_value_so_listings_do_not_depend_on_enumeration_order()
    {
        var list = new[] { new Luid(0, 0x11FD6), new Luid(0, 0x11B0C), new Luid(0, 0x11FA1) }.Order().ToArray();
        Assert.Equal([new Luid(0, 0x11B0C), new Luid(0, 0x11FA1), new Luid(0, 0x11FD6)], list);
    }
}
