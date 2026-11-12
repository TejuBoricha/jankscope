using Jankscope.App.Views;
using Jankscope.Core;
using Jankscope.Frames.Incidents;

namespace Jankscope.App.Tests;

/// <summary>The capability check's wording (real-app run of 2026-11-12, defects 13 and 14).</summary>
public class CapabilityTextTests
{
    private static CapabilityEntry Row(Availability availability) => availability == Availability.Available
        ? CapabilityEntry.Available("x.row", "A row", CapabilityTier.B, "test", "the value", DataProvenance.Measured)
        : CapabilityEntry.Unavailable("x.row", "A row", CapabilityTier.B, availability, "test", "the stated reason");

    public static TheoryData<Availability> States => new(Enum.GetValues<Availability>());

    [Theory]
    [MemberData(nameof(States))]
    public void Every_state_has_a_plain_badge_that_fits_and_is_not_the_raw_enum_name(Availability availability)
    {
        var badge = CapabilityText.Badge(Row(availability));

        Assert.True(badge.Length <= 16, $"'{badge}' will not fit the badge column");

        // A state named with several words (NeedsOptionalTool) must not appear as one run-together token (NEEDSOPTIONALTOOL).
        var raw = availability.ToString();
        if (raw.Skip(1).Any(char.IsUpper))
        {
            Assert.NotEqual(raw.ToUpperInvariant(), badge);
            Assert.Contains(' ', badge);
        }
    }

    [Fact]
    public void A_ready_row_shows_its_value_and_every_other_row_shows_its_reason_without_the_enum_name_in_front()
    {
        Assert.Equal("the value", CapabilityText.Detail(Row(Availability.Available)));
        Assert.Equal("the stated reason", CapabilityText.Detail(Row(Availability.NotImplemented)));
        Assert.Equal("the stated reason", CapabilityText.Detail(Row(Availability.NeedsOptionalTool)));
    }

    [Theory]
    [InlineData(Availability.NeedsElevation, true)]
    [InlineData(Availability.NeedsOptionalTool, true)]
    [InlineData(Availability.NeedsUserAction, true)]
    [InlineData(Availability.Available, false)]
    [InlineData(Availability.NotAvailable, false)]
    [InlineData(Availability.NotImplemented, false)]
    [InlineData(Availability.NotMeasurableBySoftware, false)]
    [InlineData(Availability.Unknown, false)]
    public void Only_what_the_person_could_set_up_needs_attention(Availability availability, bool expected) =>
        Assert.Equal(expected, CapabilityText.NeedsAttention(availability));

    [Theory]
    [InlineData("Idle; elevated and kernel collectors not suppressed", "No game running")]
    [InlineData("ResidentOnly; elevated and kernel collectors not suppressed", "Resident, idle")]
    [InlineData("GameSession; elevated and kernel collectors suppressed", "Game session")]
    [InlineData("ProtectedTitle; elevated and kernel collectors suppressed", "Protected title")]
    public void The_anti_cheat_card_shows_the_state_that_was_reached(string value, string expected)
    {
        var entry = CapabilityEntry.Available("anticheat.state", "Anti-cheat safety state", CapabilityTier.A, "test", value, DataProvenance.Inferred);

        Assert.Equal(expected, CapabilityText.AntiCheatPosture(entry));
    }

    [Fact]
    public void An_anti_cheat_state_that_could_not_be_read_is_unknown_never_clear()
    {
        var unknown = CapabilityEntry.Unavailable("anticheat.state", "Anti-cheat safety state", CapabilityTier.A, Availability.Unknown, "test", "cannot rule out a game session");

        Assert.Equal("Unknown", CapabilityText.AntiCheatPosture(unknown));
        Assert.Equal("Unknown", CapabilityText.AntiCheatPosture(null));
        Assert.Equal("Unknown", CapabilityText.AntiCheatPosture(CapabilityEntry.Available("anticheat.state", "t", CapabilityTier.A, "test", "something new", DataProvenance.Inferred)));
    }

    [Fact]
    public void List_items_announce_readable_text_to_a_screen_reader_not_the_record_or_type_name()
    {
        var card = new HypothesisCardItem("GRADE B", "Frame pacing hitch", "A summary.", "Supporting Evidence: x", "What would falsify: y");
        var radar = new CapabilityRadarItem("os.version", "PLATFORM", "OS version", "READY [TIER A]", "Windows 11", "Source: test", string.Empty, true, false);

        Assert.Equal("GRADE B: Frame pacing hitch. A summary.", card.ToString());
        Assert.Equal("OS version: READY [TIER A]. Windows 11", radar.ToString());
        Assert.DoesNotContain("{", card.ToString());
        Assert.DoesNotContain("{", radar.ToString());

        var mark = new MarkItem(
            new IncidentMark(
                "m1",
                MarkKind.Stutter,
                DateTimeOffset.UtcNow,
                PressedTicks: 0,
                CaptureTime: null,
                Search: new AnchorSearchResult([], null, null, 0, []),
                Selection: null,
                Note: null,
                Window: new MarkWindow(null, null, 10.0, 0.0, 5.0),
                PreRoll: FrameSeries.Empty,
                TargetProcess: null),
            1);
        Assert.Equal($"{mark.Title}. {mark.Subtitle}", mark.ToString());
        Assert.DoesNotContain("Jankscope.App.Views", mark.ToString());
    }
}
