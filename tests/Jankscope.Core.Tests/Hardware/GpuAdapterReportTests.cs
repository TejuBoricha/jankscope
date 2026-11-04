using System.Globalization;
using Jankscope.Core.Hardware;

namespace Jankscope.Core.Tests.Hardware;

public class GpuAdapterReportTests
{
    private const ulong MiB = 1024UL * 1024UL;

    private static GpuAdapterInfo Adapter(uint luidLow, string description, uint vendor = 0x1234, uint device = 0x5678,
        ulong dedicated = 0, ulong shared = 8192 * MiB, bool software = false, bool remote = false) =>
        new(new Luid(0, luidLow), description, vendor, device, 0x3C9017AA, 1, dedicated, 0, shared, software, remote);

    [Fact]
    public void Each_adapter_is_described_by_its_luid_with_numeric_ids_and_memory_in_mib()
    {
        var text = GpuAdapterReport.Describe([Adapter(0x11FD6, "Test Adapter A", 0x10DE, 0x28E0, dedicated: 8342470656)]);
        Assert.StartsWith("1 adapter(s): ", text, StringComparison.Ordinal);
        Assert.Contains("LUID 0x00000000_0x00011FD6", text, StringComparison.Ordinal);
        Assert.Contains("Test Adapter A", text, StringComparison.Ordinal);
        Assert.Contains("vendor 0x10DE", text, StringComparison.Ordinal);
        Assert.Contains("device 0x28E0", text, StringComparison.Ordinal);
        Assert.Contains("dedicated 7956 MiB", text, StringComparison.Ordinal);
        Assert.Contains("shared 8192 MiB", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_software_adapter_is_labeled_and_a_remote_one_too()
    {
        var text = GpuAdapterReport.Describe([Adapter(1, "Soft", software: true), Adapter(2, "Remote", remote: true), Adapter(3, "Hardware")]);
        Assert.Contains("[LUID 0x00000000_0x00000001, Soft, vendor 0x1234, device 0x5678, dedicated 0 MiB, shared 8192 MiB, software renderer]", text, StringComparison.Ordinal);
        Assert.Contains("remote adapter", text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split("software renderer").Length - 1);
    }

    [Fact]
    public void The_listing_does_not_depend_on_enumeration_order_and_never_mentions_an_index()
    {
        var a = Adapter(0x11FD6, "Alpha");
        var b = Adapter(0x11B0C, "Bravo");
        var c = Adapter(0x11FA1, "Charlie");
        var one = GpuAdapterReport.Describe([a, b, c]);
        Assert.Equal(one, GpuAdapterReport.Describe([c, a, b]));
        Assert.Equal(one, GpuAdapterReport.Describe([b, c, a]));
        Assert.True(one.IndexOf("Bravo", StringComparison.Ordinal) < one.IndexOf("Charlie", StringComparison.Ordinal));
        Assert.True(one.IndexOf("Charlie", StringComparison.Ordinal) < one.IndexOf("Alpha", StringComparison.Ordinal));
        Assert.DoesNotContain("#0", one, StringComparison.Ordinal);
        Assert.DoesNotContain("adapter 0", one, StringComparison.Ordinal);
    }

    [Fact]
    public void An_adapter_listed_twice_under_one_luid_appears_once()
    {
        var text = GpuAdapterReport.Describe([Adapter(5, "First"), Adapter(5, "Second")]);
        Assert.StartsWith("1 adapter(s): ", text, StringComparison.Ordinal);
        Assert.Contains("First", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Second", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Display_ownership_is_stated_only_when_the_display_data_is_known()
    {
        var a = Adapter(1, "A");
        var b = Adapter(2, "B");
        Assert.DoesNotContain("display", GpuAdapterReport.Describe([a, b]), StringComparison.Ordinal);

        var text = GpuAdapterReport.Describe([a, b], new Dictionary<Luid, int> { [new Luid(0, 1)] = 2 });
        Assert.Contains("drives 2 active display(s)", text, StringComparison.Ordinal);
        Assert.Contains("drives no active display", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("  Spaced   Name  ", "Spaced Name")]
    [InlineData("Tab\tAnd\r\nNewline", "Tab And Newline")]
    [InlineData("", "(no description)")]
    [InlineData("   ", "(no description)")]
    [InlineData(null, "(no description)")]
    public void Descriptions_are_cleaned_of_control_characters_and_never_empty(string? raw, string expected)
    {
        Assert.Equal(expected, GpuAdapterReport.CleanDescription(raw));
    }

    [Fact]
    public void An_escape_character_in_a_driver_description_is_dropped_so_it_cannot_reach_a_console()
    {
        Assert.Equal("Esc[31mape", GpuAdapterReport.CleanDescription("Esc" + (char)0x1B + "[31mape"));
    }

    [Fact]
    public void A_very_long_description_is_cut()
    {
        Assert.Equal(128, GpuAdapterReport.CleanDescription(new string('x', 500)).Length);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void The_text_does_not_depend_on_the_thread_culture(string culture)
    {
        var adapters = new[] { Adapter(0x11B0C, "Integrated", dedicated: 128 * MiB), Adapter(0x11FD6, "Discrete", dedicated: 8342470656) };
        var expected = GpuAdapterReport.Describe(adapters);
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(expected, GpuAdapterReport.Describe(adapters));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Fact]
    public void Facts_are_reported_without_conclusions()
    {
        // The report never says which adapter a game should use or which one is "the" GPU (spec section 7.2 conclusions belong to the diagnosis engine).
        var text = GpuAdapterReport.Describe([Adapter(1, "Integrated"), Adapter(2, "Discrete")], new Dictionary<Luid, int> { [new Luid(0, 1)] = 1 });
        foreach (var word in new[] { "primary", "recommended", "should", "best", "slow", "fast", "game on" })
        {
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
