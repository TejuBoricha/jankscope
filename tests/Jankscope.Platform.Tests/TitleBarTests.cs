using Jankscope.Platform.Windowing;

namespace Jankscope.Platform.Tests;

/// <summary>The title-bar call (GUI-run report, defect 9): what is asked of Windows, and that a refusal is a plain false.</summary>
public class TitleBarTests
{
    private sealed class Recorder(int result)
    {
        public List<(nint Window, int Attribute, int Value, int Size)> Calls { get; } = [];

        public int Set(nint window, int attribute, ref int value, int size)
        {
            Calls.Add((window, attribute, value, size));
            return result;
        }
    }

    [Fact]
    public void Dark_asks_Windows_for_the_immersive_dark_mode_attribute_with_a_four_byte_one()
    {
        var recorder = new Recorder(0);

        var accepted = TitleBar.TrySetDark(0x1234, dark: true, recorder.Set);

        Assert.True(accepted);
        var call = Assert.Single(recorder.Calls);
        Assert.Equal((nint)0x1234, call.Window);
        Assert.Equal(20, call.Attribute); // DWMWA_USE_IMMERSIVE_DARK_MODE, documented in dwmapi.h
        Assert.Equal(1, call.Value);
        Assert.Equal(4, call.Size);       // a BOOL
    }

    [Fact]
    public void Light_sends_zero_so_a_title_bar_can_be_taken_back_to_light()
    {
        var recorder = new Recorder(0);

        Assert.True(TitleBar.TrySetDark(0x1234, dark: false, recorder.Set));

        Assert.Equal(0, Assert.Single(recorder.Calls).Value);
    }

    [Fact]
    public void A_window_with_no_handle_is_never_sent_to_Windows()
    {
        var recorder = new Recorder(0);

        Assert.False(TitleBar.TrySetDark(0, dark: true, recorder.Set));

        Assert.Empty(recorder.Calls);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070057))] // E_INVALIDARG: what an older Windows build answers to an attribute it does not know
    [InlineData(1)]
    [InlineData(-1)]
    public void Any_answer_other_than_success_is_false_and_never_an_exception(int hresult)
    {
        var recorder = new Recorder(hresult);

        Assert.False(TitleBar.TrySetDark(0x1234, dark: true, recorder.Set));
        Assert.Single(recorder.Calls);
    }
}
