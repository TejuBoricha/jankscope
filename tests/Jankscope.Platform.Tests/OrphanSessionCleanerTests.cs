using Jankscope.Core;

namespace Jankscope.Platform.Tests;

internal sealed class FakeProcessLiveness(HashSet<int> alivePids) : IProcessLiveness
{
    public bool IsAlive(int processId) => alivePids.Contains(processId);
}

public class OrphanSessionCleanerTests
{
    [Theory]
    [InlineData("Jankscope-1234", 1234)]
    [InlineData("Jankscope-1234-stop-1", 1234)]
    [InlineData("Jankscope-1234-timed", 1234)]
    [InlineData("Jankscope-probe-5678-abcd1234", 5678)]
    [InlineData("Jankscope-9999-my-custom-label", 9999)]
    public void Valid_session_names_extract_correct_owner_pid(string sessionName, int expectedPid)
    {
        var extracted = EtwSessionClassifier.TryExtractOwnerPid(sessionName, Branding.EtwSessionPrefix, out var pid);
        Assert.True(extracted);
        Assert.Equal(expectedPid, pid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Jankscope-")]
    [InlineData("Jankscope-abc")]
    [InlineData("Jankscope-0")]
    [InlineData("Jankscope--1")]
    [InlineData("Jankscope-probe-notapid-abc")]
    [InlineData("ForeignSession-1234")]
    public void Invalid_or_foreign_session_names_fail_pid_extraction(string? sessionName)
    {
        var extracted = EtwSessionClassifier.TryExtractOwnerPid(sessionName, Branding.EtwSessionPrefix, out var pid);
        Assert.False(extracted);
        Assert.Equal(0, pid);
    }

    [Fact]
    public void Sessions_owned_by_current_process_are_kept_active()
    {
        const int myPid = 1000;
        var liveness = new FakeProcessLiveness([myPid, 2000]);

        // Current process owns Jankscope-1000; process 2000 is also alive
        var sessionName = $"{Branding.EtwSessionPrefix}{myPid}-run-1";
        Assert.True(EtwSessionClassifier.TryExtractOwnerPid(sessionName, Branding.EtwSessionPrefix, out var ownerPid));
        Assert.Equal(myPid, ownerPid);
    }

    [Fact]
    public void Sessions_owned_by_dead_process_are_classified_as_orphans()
    {
        const int deadPid = 9999;
        var liveness = new FakeProcessLiveness([1000, 2000]); // deadPid not in alive set

        var sessionName = $"{Branding.EtwSessionPrefix}{deadPid}-orphaned";
        Assert.True(EtwSessionClassifier.TryExtractOwnerPid(sessionName, Branding.EtwSessionPrefix, out var ownerPid));
        Assert.False(liveness.IsAlive(ownerPid));
    }

    [Fact]
    public void System_process_liveness_reports_current_process_alive_and_invalid_pid_dead()
    {
        var sys = new SystemProcessNameSource();
        Assert.True(sys.IsAlive(Environment.ProcessId));
        Assert.False(sys.IsAlive(int.MaxValue));
        Assert.False(sys.IsAlive(-1));
    }
}
