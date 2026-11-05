using Jankscope.Frames.Capture;

namespace Jankscope.Harness.Tests;

public class CaptureProvenancePinTests
{
    [Fact]
    public void The_file_name_the_live_capture_treats_as_the_harness_is_the_name_of_the_harness_assembly()
    {
        // If the harness is ever renamed, a live capture of it would silently be exported as a measurement (HC-9, review of Stage 1b, finding 6).
        var assemblyName = typeof(D3D11Presenter).Assembly.GetName().Name;

        Assert.Equal(CaptureProvenance.HarnessExecutableName, assemblyName + ".exe");
    }
}
