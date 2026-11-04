namespace Jankscope.Harness.Tests;

/// <summary>
/// Finds out, once per adapter request, whether this machine can run the harness at all: it opens the window, creates the device and
/// swap chain for real, and presents one frame. There are exactly three answers:
/// <list type="bullet">
/// <item>it works (no skip, no defect);</item>
/// <item>this machine cannot give the harness a device or a window (<see cref="UnavailableReason"/>: a test may skip, with that reason on screen);</item>
/// <item>a device exists but the harness failed after that, creating the swap chain or presenting (<see cref="Defect"/>): that is a bug, so the tests are
/// NOT skipped and fail with the real exception, and a canary test names it. A skip must never hide a regression (review 1a, finding 7).</item>
/// </list>
/// With <c>JANKSCOPE_REQUIRE_D3D=1</c> (set by <c>build.ps1 -RequireHardwareTests</c> on the reference bench) a hardware device that is missing fails a canary test too.
/// </summary>
internal static class D3dEnvironment
{
    private static readonly Lazy<Verdict> Hardware = new(() => Probe(AdapterRequest.Hardware, "hardware"));
    private static readonly Lazy<Verdict> Warp = new(() => Probe(AdapterRequest.Warp, "WARP software"));

    internal sealed record Verdict(string? SkipReason, string? Defect);

    /// <summary>Null when the request can run, or when it failed for a reason that is a defect (then the tests run and fail); otherwise why this machine cannot run it.</summary>
    public static string? UnavailableReason(AdapterRequest request) => For(request).SkipReason;

    /// <summary>Null unless a device exists and the harness still failed.</summary>
    public static string? Defect(AdapterRequest request) => For(request).Defect;

    public static bool StrictModeRequested { get; } = Environment.GetEnvironmentVariable("JANKSCOPE_REQUIRE_D3D") == "1";

    private static Verdict For(AdapterRequest request) => request == AdapterRequest.Hardware ? Hardware.Value : Warp.Value;

    private static Verdict Probe(AdapterRequest request, string label)
    {
        try
        {
            return StaThread.Run(() =>
            {
                using var window = new RenderWindow(visible: false, 64, 64);
                using var presenter = D3D11Presenter.Create(window.Open(), 64, 64, request);
                presenter.Prepare(0, false);
                presenter.Present();
                return new Verdict(null, null);
            });
        }
        catch (HarnessEnvironmentException e) when (e.DeviceUnavailable)
        {
            return new Verdict($"No {label} Direct3D 11 device can be created on this machine ({e.Message})", null);
        }
        catch (Exception e)
        {
            // A device could be had, or the failure is not one of "no device": do not skip, let the tests fail with the real exception.
            return new Verdict(null, $"The {label} Direct3D 11 harness failed after a device was available ({e.GetType().Name}: {e.Message})");
        }
    }
}

/// <summary>A fact that is skipped, with the reason, when no Direct3D 11 device of the requested kind can be created here (and only then).</summary>
internal sealed class D3dFactAttribute : FactAttribute
{
    public D3dFactAttribute(bool requireHardware = false)
    {
        Skip = D3dEnvironment.UnavailableReason(requireHardware ? AdapterRequest.Hardware : AdapterRequest.Warp);
    }
}

/// <summary>The tests that make skipping safe: they fail where a skip would hide a regression.</summary>
public class D3dEnvironmentCanaryTests
{
    [Fact]
    public void A_machine_that_can_give_the_harness_a_device_can_also_create_a_swap_chain_and_present()
    {
        Assert.Null(D3dEnvironment.Defect(AdapterRequest.Warp));
        Assert.Null(D3dEnvironment.Defect(AdapterRequest.Hardware));
    }

    [Fact]
    public void When_hardware_tests_are_required_a_hardware_device_must_exist_here()
    {
        if (!D3dEnvironment.StrictModeRequested)
        {
            return; // an ordinary run: a machine without a device skips its Direct3D tests, with the reason shown
        }

        Assert.Null(D3dEnvironment.UnavailableReason(AdapterRequest.Hardware));
        Assert.Null(D3dEnvironment.UnavailableReason(AdapterRequest.Warp));
    }
}
