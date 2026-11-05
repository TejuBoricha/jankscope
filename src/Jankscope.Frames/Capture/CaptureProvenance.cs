using Jankscope.Core;

namespace Jankscope.Frames.Capture;

/// <summary>
/// Decides how a live capture is labelled (HC-9). The synthetic harness writes <c>Simulated</c> into its own log, but PresentMon never prints that
/// column, so a live capture of the harness cannot say what it is. The one thing the capture does know is which program it was asked to watch:
/// when that is the harness, every frame is <see cref="DataProvenance.Simulated"/> and the export says so. Every other program is
/// <see cref="DataProvenance.Measured"/>. (Review of Stage 1b, finding 6.)
/// </summary>
public static class CaptureProvenance
{
    /// <summary>The file name of the synthetic harness (<c>tools/Jankscope.Harness</c>, assembly name <c>jankscope-harness</c>). A test pins it to the real assembly name.</summary>
    public const string HarnessExecutableName = "jankscope-harness.exe";

    private const string ExecutableSuffix = ".exe";

    /// <summary>True when <paramref name="programName"/> names the synthetic harness, with or without ".exe" and in any letter case.</summary>
    public static bool IsSyntheticHarness(string? programName)
    {
        if (string.IsNullOrWhiteSpace(programName))
        {
            return false;
        }

        return string.Equals(WithoutSuffix(programName.Trim()), WithoutSuffix(HarnessExecutableName), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What a live capture of <paramref name="programName"/> must be labelled.</summary>
    public static DataProvenance ForProgram(string? programName) =>
        IsSyntheticHarness(programName) ? DataProvenance.Simulated : DataProvenance.Measured;

    private static string WithoutSuffix(string name) =>
        name.EndsWith(ExecutableSuffix, StringComparison.OrdinalIgnoreCase) ? name[..^ExecutableSuffix.Length] : name;
}
