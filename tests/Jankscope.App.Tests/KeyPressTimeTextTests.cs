using Jankscope.App.Views;
using Jankscope.Frames.Incidents;

namespace Jankscope.App.Tests;

/// <summary>What the investigator says about the key-press time (review of Stage 1b, finding 3).</summary>
public class KeyPressTimeTextTests
{
    [Fact]
    public void With_enough_frames_it_gives_the_jitter_and_says_that_is_the_jitter_only()
    {
        var text = KeyPressTimeText.Describe(new CaptureTime(1000.0, 12.4, 200, 0));

        Assert.Contains("about 12 ms", text);
        Assert.Contains("jitter only", text);
        Assert.Contains("minimum delay", text);
    }

    [Fact]
    public void With_too_few_frames_it_gives_no_number_instead_of_zero()
    {
        var text = KeyPressTimeText.Describe(new CaptureTime(1000.0, 0.0, 3, 0));

        Assert.Contains("only 3 frame(s)", text);
        Assert.DoesNotContain(" 0 ms", text);
        Assert.Contains("minimum delay", text);
    }
}
