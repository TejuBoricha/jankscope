using Jankscope.App.Services;
using Jankscope.App.Views;
using Jankscope.Core.Hotkeys;
using Jankscope.Platform.Hotkeys;

namespace Jankscope.App.Tests;

/// <summary>
/// HC-4: the hidden hotkey window acts on WM_HOTKEY only (mutation survivor L5 of the independent Stage 1b review: nothing failed when the message check
/// was removed, which would have let any message with a matching number in it trigger a mark).
/// </summary>
public class HotkeyRoutingTests
{
    private const int WmHotkey = 0x0312;   // winuser.h
    private const int WmKeyDown = 0x0100;
    private const int WmChar = 0x0102;
    private const int WmSysKeyDown = 0x0104;
    private const int WmInput = 0x00FF;

    private sealed class AcceptingApi : IHotkeyApi
    {
        public HotkeyRegistrationResult Register(nint window, int id, HotkeyChord chord) => HotkeyRegistrationResult.Success;

        public bool Unregister(nint window, int id) => true;
    }

    private static HotkeyRegistrar Registered()
    {
        var registrar = new HotkeyRegistrar(new AcceptingApi(), 0x1234);
        Assert.All(registrar.Apply(HotkeyBindings.Default), s => Assert.True(s.Active));
        return registrar;
    }

    [Fact]
    public void The_hotkey_message_with_a_registered_id_becomes_its_action()
    {
        using var registrar = Registered();

        Assert.True(HotkeyHost.TryRoute(registrar, WmHotkey, HotkeyRegistrar.FirstId, out var first));
        Assert.True(HotkeyHost.TryRoute(registrar, WmHotkey, HotkeyRegistrar.FirstId + 1, out var second));

        Assert.Equal(HotkeyAction.MarkStutter, first);
        Assert.Equal(HotkeyAction.MarkInputStall, second);
    }

    [Theory]
    [InlineData(WmKeyDown)]
    [InlineData(WmChar)]
    [InlineData(WmSysKeyDown)]
    [InlineData(WmInput)]
    [InlineData(0)]
    [InlineData(WmHotkey + 1)]
    public void Any_other_message_is_not_acted_on_even_when_its_number_matches_a_registered_id(int message)
    {
        using var registrar = Registered();

        Assert.False(HotkeyHost.TryRoute(registrar, message, HotkeyRegistrar.FirstId, out _));
    }

    [Fact]
    public void The_hotkey_message_with_an_id_that_is_not_registered_is_not_acted_on()
    {
        using var registrar = Registered();

        Assert.False(HotkeyHost.TryRoute(registrar, WmHotkey, HotkeyRegistrar.FirstId + 99, out _));
    }

    [Fact]
    public void A_candidate_in_the_list_is_announced_by_its_text_not_by_its_type()
    {
        // Review of Stage 1b, finding 12: a screen reader read "CandidateItem { Rank = 1, Text = ... }".
        var item = new CandidateItem(1, "1.  Frame 966: 67.1 ms  (2.4 s before the key press)");

        Assert.Equal(item.Text, item.ToString());
    }
}
