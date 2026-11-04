using Jankscope.Core.Hotkeys;
using Jankscope.Platform.Hotkeys;

namespace Jankscope.Platform.Tests;

internal sealed class FakeHotkeyApi : IHotkeyApi
{
    private readonly Dictionary<string, HotkeyRegistrationResult> _scripted = [];

    public List<(nint Window, int Id, HotkeyChord Chord)> Registered { get; } = [];

    public List<(nint Window, int Id)> Unregistered { get; } = [];

    public FakeHotkeyApi Fail(string chord, HotkeyFailure failure, int error = 0)
    {
        _scripted[HotkeyChord.Parse(chord).ToString()] = new HotkeyRegistrationResult(false, failure, error);
        return this;
    }

    public HotkeyRegistrationResult Register(nint window, int id, HotkeyChord chord)
    {
        if (_scripted.TryGetValue(chord.ToString(), out var scripted))
        {
            return scripted;
        }

        Registered.Add((window, id, chord));
        return HotkeyRegistrationResult.Success;
    }

    public bool Unregister(nint window, int id)
    {
        Unregistered.Add((window, id));
        return true;
    }
}

/// <summary>Skipped, with a visible reason, in a session that cannot register a hotkey at all (no interactive desktop). Never passes vacuously.</summary>
public sealed class HotkeyDesktopFactAttribute : FactAttribute
{
    public HotkeyDesktopFactAttribute()
    {
        var api = new WindowsHotkeyApi();
        var probe = HotkeyChord.Parse("Ctrl+Alt+Shift+F22");
        var result = api.Register(0, 0x4AF0, probe);
        if (result.Succeeded)
        {
            api.Unregister(0, 0x4AF0);
        }
        else if (result.Failure != HotkeyFailure.AlreadyRegistered)
        {
            Skip = $"This session cannot register a hotkey (Win32 error {result.Win32Error}); it needs an interactive desktop.";
        }
    }
}

public class HotkeyRegistrarTests
{
    private const nint Window = 0x1234;

    [Fact]
    public void Both_chords_are_registered_with_the_window_and_stable_ids()
    {
        var api = new FakeHotkeyApi();
        using var registrar = new HotkeyRegistrar(api, Window);
        var statuses = registrar.Apply(HotkeyBindings.Default);

        Assert.All(statuses, s => Assert.True(s.Active));
        Assert.Equal(2, api.Registered.Count);
        Assert.All(api.Registered, r => Assert.Equal(Window, r.Window));
        Assert.Equal([HotkeyRegistrar.FirstId, HotkeyRegistrar.FirstId + 1], api.Registered.Select(r => r.Id).ToArray());
        Assert.Equal("Ctrl+Alt+F9", api.Registered[0].Chord.ToString());
    }

    [Fact]
    public void A_chord_owned_by_another_program_is_reported_with_a_reason_and_the_other_chord_still_works()
    {
        var api = new FakeHotkeyApi().Fail("Ctrl+Alt+F9", HotkeyFailure.AlreadyRegistered, 1409);
        using var registrar = new HotkeyRegistrar(api, Window);
        var statuses = registrar.Apply(HotkeyBindings.Default);

        var stutter = statuses.Single(s => s.Action == HotkeyAction.MarkStutter);
        var stall = statuses.Single(s => s.Action == HotkeyAction.MarkInputStall);
        Assert.False(stutter.Active);
        Assert.Equal(HotkeyFailure.AlreadyRegistered, stutter.Failure);
        Assert.Contains("Another program already uses Ctrl+Alt+F9", stutter.Reason);
        Assert.Contains("tray", stutter.Reason);
        Assert.True(stall.Active);
        Assert.Null(stall.Reason);
    }

    [Theory]
    [InlineData(HotkeyFailure.InvalidKey, 87, "does not accept")]
    [InlineData(HotkeyFailure.InvalidWindow, 1400, "not available")]
    [InlineData(HotkeyFailure.Other, 5, "error 5")]
    public void Every_failure_has_a_plain_reason_and_points_to_the_tray_fallback(HotkeyFailure failure, int error, string expected)
    {
        var api = new FakeHotkeyApi().Fail("Ctrl+Alt+F9", failure, error).Fail("Ctrl+Alt+F10", failure, error);
        using var registrar = new HotkeyRegistrar(api, Window);
        foreach (var status in registrar.Apply(HotkeyBindings.Default))
        {
            Assert.False(status.Active);
            Assert.Contains(expected, status.Reason);
            Assert.Contains("tray", status.Reason);
        }
    }

    [Fact]
    public void A_key_press_message_maps_to_the_action_only_for_live_registrations()
    {
        var api = new FakeHotkeyApi().Fail("Ctrl+Alt+F10", HotkeyFailure.AlreadyRegistered, 1409);
        using var registrar = new HotkeyRegistrar(api, Window);
        registrar.Apply(HotkeyBindings.Default);

        Assert.True(registrar.TryHandle(HotkeyRegistrar.FirstId, out var action));
        Assert.Equal(HotkeyAction.MarkStutter, action);
        Assert.False(registrar.TryHandle(HotkeyRegistrar.FirstId + 1, out _)); // that registration failed
        Assert.False(registrar.TryHandle(12345, out _));
    }

    [Fact]
    public void Applying_again_releases_the_old_chords_first()
    {
        var api = new FakeHotkeyApi();
        using var registrar = new HotkeyRegistrar(api, Window);
        registrar.Apply(HotkeyBindings.Default);
        Assert.True(HotkeyBindings.TryCreate("Ctrl+Alt+F11", "Ctrl+Alt+F12", out var changed, out _));
        registrar.Apply(changed);

        Assert.Equal(2, api.Unregistered.Count);
        Assert.Equal("Ctrl+Alt+F11", api.Registered[2].Chord.ToString());
        Assert.Equal("Ctrl+Alt+F11", registrar.Statuses.Single(s => s.Action == HotkeyAction.MarkStutter).Chord.ToString());
    }

    [Fact]
    public void Disposing_releases_only_what_is_live_and_is_safe_to_repeat()
    {
        var api = new FakeHotkeyApi().Fail("Ctrl+Alt+F10", HotkeyFailure.AlreadyRegistered, 1409);
        var registrar = new HotkeyRegistrar(api, Window);
        registrar.Apply(HotkeyBindings.Default);

        registrar.Dispose();
        registrar.Dispose();

        var released = Assert.Single(api.Unregistered);
        Assert.Equal(HotkeyRegistrar.FirstId, released.Id);
        Assert.Throws<ObjectDisposedException>(() => registrar.Apply(HotkeyBindings.Default));
        Assert.False(registrar.TryHandle(HotkeyRegistrar.FirstId, out _));
    }

    [Fact]
    public void The_status_list_is_empty_until_chords_are_applied()
    {
        using var registrar = new HotkeyRegistrar(new FakeHotkeyApi(), Window);
        Assert.Empty(registrar.Statuses);
    }

    [HotkeyDesktopFact]
    public void The_real_api_registers_a_free_chord_refuses_a_second_claim_and_lets_go()
    {
        var api = new WindowsHotkeyApi();
        var chord = HotkeyChord.Parse("Ctrl+Alt+Shift+F23");

        var first = api.Register(0, 0x4AF1, chord);
        Assert.True(first.Succeeded, $"error {first.Win32Error}");
        try
        {
            var second = api.Register(0, 0x4AF2, chord);
            Assert.False(second.Succeeded);
            Assert.Equal(HotkeyFailure.AlreadyRegistered, second.Failure);
            Assert.Equal(1409, second.Win32Error);
        }
        finally
        {
            Assert.True(api.Unregister(0, 0x4AF1));
        }

        Assert.False(api.Unregister(0, 0x4AF1)); // already released
        var again = api.Register(0, 0x4AF3, chord);
        Assert.True(again.Succeeded);
        Assert.True(api.Unregister(0, 0x4AF3));
    }
}
