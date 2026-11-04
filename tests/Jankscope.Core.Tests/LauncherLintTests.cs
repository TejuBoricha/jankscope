namespace Jankscope.Core.Tests;

/// <summary>
/// The PresentMon launcher is the one product file that may start a process, and the whole file is exempt from the Process rules. These tests pin
/// what the exemption allows (review 1a, findings 1 and 18): the launcher ends the child it started with Process.Kill() and never with the
/// tree form, which makes the .NET runtime enumerate every process on the machine and open a handle to each.
/// </summary>
public class LauncherLintTests
{
    private const string LauncherFile = "src/Jankscope.Frames/PresentMon/PresentMonProcessLauncher.cs";

    private static IReadOnlyList<string> Violations(string source) => ProductLints.LauncherProcessMemberViolations(CSharpScanner.Scan(source));

    [Fact]
    public void The_launcher_uses_Process_Start_once_and_no_other_member_that_looks_at_a_process_from_outside()
    {
        var text = File.ReadAllText(Path.Combine(Repo.Root, LauncherFile.Replace('/', Path.DirectorySeparatorChar)));
        var scanned = CSharpScanner.Scan(text);

        Assert.Empty(ProductLints.LauncherProcessMemberViolations(scanned));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(scanned.Code, @"\bProcess\s*\.\s*Start\b"));
        Assert.Contains(".Kill()", scanned.Code, StringComparison.Ordinal); // and the plain kill is what it actually uses
    }

    [Theory]
    [InlineData("process.Kill(entireProcessTree: true);", "Kill(entireProcessTree: true)")]
    [InlineData("process.Kill(entireProcessTree: true);", "entireProcessTree")]
    [InlineData("process.Kill(true);", "Kill(true)")]
    [InlineData("var all = Process.GetProcesses();", "Process.GetProcesses")]
    [InlineData("var me = Process.GetCurrentProcess();", "Process.GetCurrentProcess")]
    [InlineData("var other = Process.GetProcessById(7);", "Process.GetProcessById")]
    [InlineData("var m = process.MainModule;", ".MainModule")]
    [InlineData("var t = process.StartTime;", ".StartTime")]
    [InlineData("process.EnableRaisingEvents = true;", ".EnableRaisingEvents")]
    [InlineData("Process.EnterDebugMode();", "Process.EnterDebugMode")]
    public void Anything_beyond_the_approved_members_is_a_violation(string code, string expected)
    {
        Assert.Contains(expected, Violations("class C { void M(System.Diagnostics.Process process) { " + code + " } }"));
    }

    [Fact]
    public void The_members_the_launcher_really_needs_are_not_violations()
    {
        const string code = "class C { void M(System.Diagnostics.ProcessStartInfo info) { var p = System.Diagnostics.Process.Start(info); " +
                            "p.StandardInput.Close(); var e = p.ExitCode; var gone = p.HasExited; p.Kill(); p.WaitForExit(2000); p.Dispose(); } }";
        Assert.Empty(Violations(code));
    }

    [Fact]
    public void The_text_of_a_comment_or_a_string_is_not_code_so_the_launchers_own_notes_about_the_tree_kill_are_fine()
    {
        const string code = "class C { // never Process.GetProcesses() or Kill(entireProcessTree: true)\n void M() { var s = \"Kill(true)\"; } }";
        Assert.Empty(Violations(code));
    }
}
