using System.Globalization;
using System.Text;
using Jankscope.Core;

namespace Jankscope.Frames.PresentMon;

/// <summary>
/// Builds, and independently re-checks, the argument vector for the pinned PresentMon console executable.
/// <para>
/// The result is always an ordered list of separate elements, never a joined command-line string: the launcher hands it to
/// <c>ProcessStartInfo.ArgumentList</c>, which quotes every element, so a process name containing spaces, <c>&amp;</c>, <c>;</c> or
/// text that looks like an option is still exactly one argument. Names that could change the meaning of the command line are
/// rejected instead of escaped (HC-7: no strings passed to shells, no generated command lines).
/// </para>
/// <para>
/// <see cref="EnsureSafe"/> is an allowlist grammar, not a denylist: any option that is not listed here (including
/// <c>--restart_as_admin</c>, which would make PresentMon re-launch itself elevated, and file-writing options) is refused, so a
/// bug in a caller cannot widen what the launcher will start.
/// </para>
/// </summary>
public static class PresentMonArguments
{
    public const string RestartAsAdminOption = "--restart_as_admin";

    public const int MaxProcessNames = 32;
    public const int MaxProcessNameLength = 255;
    public const int MaxSessionNameLength = 128;
    public const int MaxTimedSeconds = 86_400;

    private const string OutputStdout = "--output_stdout";
    private const string NoConsoleStats = "--no_console_stats";
    private const string SessionNameOption = "--session_name";
    private const string ProcessNameOption = "--process_name";
    private const string TerminateOnProcExit = "--terminate_on_proc_exit";
    private const string Timed = "--timed";
    private const string TerminateAfterTimed = "--terminate_after_timed";
    private const string StopExistingSession = "--stop_existing_session";
    private const string TerminateExistingSession = "--terminate_existing_session";

    /// <summary>Session name for a runner owned by the process with id <paramref name="ownerProcessId"/>: <c>Jankscope-&lt;pid&gt;[-label]</c> (ADR 0003).</summary>
    public static string NewSessionName(int ownerProcessId, string? label = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ownerProcessId);
        var name = Branding.EtwSessionPrefix + ownerProcessId.ToString(CultureInfo.InvariantCulture) + (label is null ? string.Empty : "-" + label);
        ValidateSessionName(name);
        return name;
    }

    /// <summary>
    /// A session name must start with <see cref="Branding.EtwSessionPrefix"/> (matched exactly, so only sessions we could have created are
    /// ever started or stopped) and continue with 1 to a few letters, digits, <c>-</c>, <c>_</c> or <c>.</c>.
    /// </summary>
    public static void ValidateSessionName(string? sessionName)
    {
        if (string.IsNullOrEmpty(sessionName))
        {
            throw new ArgumentException("The ETW session name is empty.", nameof(sessionName));
        }

        if (!sessionName.StartsWith(Branding.EtwSessionPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"The ETW session name must start with '{Branding.EtwSessionPrefix}' so that only sessions we own are started or stopped.", nameof(sessionName));
        }

        if (sessionName.Length == Branding.EtwSessionPrefix.Length)
        {
            throw new ArgumentException("The ETW session name has nothing after the prefix.", nameof(sessionName));
        }

        if (sessionName.Length > MaxSessionNameLength)
        {
            throw new ArgumentException($"The ETW session name is longer than {MaxSessionNameLength} characters.", nameof(sessionName));
        }

        foreach (var c in sessionName)
        {
            var plain = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.';
            if (!plain)
            {
                throw new ArgumentException("The ETW session name may contain only ASCII letters, digits, '-', '_' and '.'.", nameof(sessionName));
            }
        }
    }

    /// <summary>
    /// A process name is a plain executable file name: no path, no drive, no stream, no wildcard, no quote, no control or format
    /// character (so no right-to-left override), no leading <c>-</c>, no leading or trailing blank, and it ends in <c>.exe</c>.
    /// Spaces, <c>&amp;</c>, <c>;</c> and non-ASCII letters are fine because the name travels as a single argument element.
    /// </summary>
    public static void ValidateProcessName(string? name)
    {
        if (name is null)
        {
            throw new ArgumentException("A process name is null.", nameof(name));
        }

        if (name.Length == 0 || string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A process name is empty.", nameof(name));
        }

        if (name.Length > MaxProcessNameLength)
        {
            throw new ArgumentException($"A process name is longer than {MaxProcessNameLength} characters.", nameof(name));
        }

        if (name[0] == '-')
        {
            throw new ArgumentException("A process name must not start with '-' (it would be read as an option).", nameof(name));
        }

        if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[^1]))
        {
            throw new ArgumentException("A process name must not start or end with a blank.", nameof(name));
        }

        var invalid = Path.GetInvalidFileNameChars();
        for (var i = 0; i < name.Length;)
        {
            if (!Rune.TryGetRuneAt(name, i, out var rune))
            {
                throw new ArgumentException("A process name contains an unpaired surrogate.", nameof(name));
            }

            var category = Rune.GetUnicodeCategory(rune);
            if (rune.IsBmp && Array.IndexOf(invalid, (char)rune.Value) >= 0)
            {
                throw new ArgumentException("A process name must be a plain file name: no path separators, drive, stream, wildcard, quote or control character.", nameof(name));
            }

            if (category is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
            {
                throw new ArgumentException("A process name contains a control, format or unassigned character.", nameof(name));
            }

            i += rune.Utf16SequenceLength;
        }

        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.Length == ".exe".Length)
        {
            throw new ArgumentException("A process name must be an executable file name ending in '.exe'.", nameof(name));
        }
    }

    /// <summary>
    /// The capture command line: <c>--output_stdout --no_console_stats --session_name &lt;name&gt; [--stop_existing_session]
    /// [--process_name &lt;exe&gt;]... [--terminate_on_proc_exit] [--timed N --terminate_after_timed]</c>.
    /// There is deliberately no parameter that could add <c>--restart_as_admin</c> or any other option.
    /// </summary>
    /// <param name="sessionName">Must start with <see cref="Branding.EtwSessionPrefix"/>.</param>
    /// <param name="processNames">Plain executable names to capture; null or empty captures every process.</param>
    /// <param name="terminateOnProcessExit">End the capture when the named processes have exited; needs at least one name.</param>
    /// <param name="timedSeconds">Stop after this many seconds and exit (1 to <see cref="MaxTimedSeconds"/>).</param>
    /// <param name="stopExistingSession">Ask PresentMon to stop a session of the same name first. Only for a name that carries our prefix, which is enforced for every name.</param>
    public static IReadOnlyList<string> Build(
        string sessionName,
        IReadOnlyList<string>? processNames = null,
        bool terminateOnProcessExit = false,
        int? timedSeconds = null,
        bool stopExistingSession = false)
    {
        ValidateSessionName(sessionName);

        var names = new List<string>();
        foreach (var name in processNames ?? [])
        {
            ValidateProcessName(name);
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        if (names.Count > MaxProcessNames)
        {
            throw new ArgumentException($"More than {MaxProcessNames} process names.", nameof(processNames));
        }

        if (terminateOnProcessExit && names.Count == 0)
        {
            throw new ArgumentException("Terminate-on-process-exit needs at least one process name.", nameof(terminateOnProcessExit));
        }

        if (timedSeconds is { } seconds && (seconds < 1 || seconds > MaxTimedSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timedSeconds), $"A timed capture lasts 1 to {MaxTimedSeconds} seconds.");
        }

        var argv = new List<string> { OutputStdout, NoConsoleStats, SessionNameOption, sessionName };
        if (stopExistingSession)
        {
            argv.Add(StopExistingSession);
        }

        foreach (var name in names)
        {
            argv.Add(ProcessNameOption);
            argv.Add(name);
        }

        if (terminateOnProcessExit)
        {
            argv.Add(TerminateOnProcExit);
        }

        if (timedSeconds is { } timed)
        {
            argv.Add(Timed);
            argv.Add(timed.ToString(CultureInfo.InvariantCulture));
            argv.Add(TerminateAfterTimed);
        }

        EnsureSafe(argv);
        return argv;
    }

    /// <summary>
    /// The command line that stops an ETW session named <paramref name="sessionName"/> and exits: used to end our own capture gracefully
    /// and to remove a session that a killed or crashed capture left behind. Only names with our prefix are accepted.
    /// </summary>
    public static IReadOnlyList<string> BuildTerminateExistingSession(string sessionName)
    {
        ValidateSessionName(sessionName);
        IReadOnlyList<string> argv = [TerminateExistingSession, SessionNameOption, sessionName];
        EnsureSafe(argv);
        return argv;
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> unless <paramref name="argv"/> is exactly a vector this class could have produced:
    /// only listed options, every value in its own element and valid, a session name with our prefix, no repeated session name.
    /// </summary>
    public static void EnsureSafe(IReadOnlyList<string> argv)
    {
        ArgumentNullException.ThrowIfNull(argv);
        if (argv.Count == 0)
        {
            throw new ArgumentException("The argument vector is empty.", nameof(argv));
        }

        string? session = null;
        var terminating = false;
        var capturing = false;
        for (var i = 0; i < argv.Count; i++)
        {
            var token = argv[i] ?? throw new ArgumentException("The argument vector contains a null element.", nameof(argv));
            switch (token)
            {
                case OutputStdout or NoConsoleStats or StopExistingSession or TerminateOnProcExit or TerminateAfterTimed:
                    capturing = true;
                    break;
                case TerminateExistingSession:
                    terminating = true;
                    break;
                case SessionNameOption:
                    if (session is not null)
                    {
                        throw new ArgumentException("The session name is given twice.", nameof(argv));
                    }

                    session = NextValue(argv, ref i, token);
                    ValidateSessionName(session);
                    break;
                case ProcessNameOption:
                    ValidateProcessName(NextValue(argv, ref i, token));
                    capturing = true;
                    break;
                case Timed:
                    var text = NextValue(argv, ref i, token);
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds < 1 || seconds > MaxTimedSeconds)
                    {
                        throw new ArgumentException($"'{Timed}' needs a whole number of seconds from 1 to {MaxTimedSeconds}.", nameof(argv));
                    }

                    capturing = true;
                    break;
                default:
                    throw new ArgumentException(
                        token.Contains(' ', StringComparison.Ordinal)
                            ? "An element looks like a joined command line; the arguments must be separate elements."
                            : "An argument that is not on the allowlist was refused (this includes --restart_as_admin, which is never passed).",
                        nameof(argv));
            }
        }

        if (session is null)
        {
            throw new ArgumentException("The argument vector has no session name.", nameof(argv));
        }

        if (terminating && capturing)
        {
            throw new ArgumentException("A terminate-existing-session command line must contain nothing but the session name.", nameof(argv));
        }
    }

    private static string NextValue(IReadOnlyList<string> argv, ref int i, string option)
    {
        if (i + 1 >= argv.Count)
        {
            throw new ArgumentException($"'{option}' has no value.", nameof(argv));
        }

        i++;
        return argv[i] ?? throw new ArgumentException($"The value of '{option}' is null.", nameof(argv));
    }
}
