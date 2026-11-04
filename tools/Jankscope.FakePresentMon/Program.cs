using System.Globalization;
using System.Text;
using System.Text.Json;

// Test double for the PresentMon 2.6.0 console executable (spec 6.6, test plan row 4). It prints the real 2.6.0 header and rows to
// standard output, writes the same kind of warnings to standard error, and behaves like the real tool in the ways the runner depends on:
//   - it "owns" an ETW session, modelled as a marker file below sessions/ next to the executable;
//   - ending that session by name (--terminate_existing_session) makes a running capture leave with exit code 0, exactly as the real
//     tool does within about a second; ending a session that is not there exits with code 7 and "no existing sessions found";
//   - a crash or a kill leaves the marker behind, like the real tool leaves its session behind.
// Its behaviour is chosen by "fake-presentmon.control" next to the executable (key=value lines; a key may carry a launch-number suffix such
// as mode.2=normal), and every start is recorded as one JSON line in "fake-presentmon.argv.jsonl" so tests can assert exactly what was passed.
// Unknown arguments are accepted and recorded, as the runner must not depend on the tool rejecting them.
// Nothing here is shipped: it is a developer tool for the tests.

var baseDirectory = AppContext.BaseDirectory;
var control = Control.Load(Path.Combine(baseDirectory, "fake-presentmon.control"));
var argvLog = Path.Combine(baseDirectory, "fake-presentmon.argv.jsonl");
var sessionsDirectory = Path.Combine(baseDirectory, "sessions");

var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

string? sessionName = null;
var terminateExisting = false;
var stopExisting = false;
int? timedSeconds = null;
var terminateAfterTimed = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--session_name" when i + 1 < args.Length:
            sessionName = args[++i];
            break;
        case "--terminate_existing_session":
            terminateExisting = true;
            break;
        case "--stop_existing_session":
            stopExisting = true;
            break;
        case "--timed" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds):
            timedSeconds = seconds;
            i++;
            break;
        case "--terminate_after_timed":
            terminateAfterTimed = true;
            break;
    }
}

var ordinal = terminateExisting ? 0 : CountCaptureLaunches(argvLog) + 1;
AppendLine(argvLog, JsonSerializer.Serialize(new
{
    pid = Environment.ProcessId,
    ordinal,
    args,
    cwd = Directory.GetCurrentDirectory(),
    exe = Environment.ProcessPath,
    envNames = Environment.GetEnvironmentVariables().Keys.Cast<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray(),
    stdinAtEnd = Console.IsInputRedirected && Console.In.Peek() < 0,
}));

var sessionFile = sessionName is null ? null : Path.Combine(sessionsDirectory, sessionName.ToLowerInvariant() + ".session");

if (terminateExisting)
{
    stderr.WriteLine("warning: --terminate_existing_session exits without capturing anything; ignoring all other options.");
    stderr.WriteLine("warning: --no_console_stats added because stdout does not support statistics reporting.");
    if (sessionFile is not null && File.Exists(sessionFile))
    {
        File.Delete(sessionFile);
        return 0;
    }

    stderr.WriteLine("error: no existing sessions found: " + sessionName);
    return 7;
}

var mode = control.Get("mode", ordinal, "normal");
var rows = int.Parse(control.Get("rows", ordinal, "5"), CultureInfo.InvariantCulture);
var rowDelay = int.Parse(control.Get("rowDelayMs", ordinal, "0"), CultureInfo.InvariantCulture);
var startDelay = int.Parse(control.Get("startDelayMs", ordinal, "0"), CultureInfo.InvariantCulture);
var crashCode = int.Parse(control.Get("exitCode", ordinal, "3"), CultureInfo.InvariantCulture);
var headerFirst = control.Get("headerFirst", ordinal, "true") == "true";
var holdOpen = control.Get("holdOpen", ordinal, "true") == "true";
var ignoreTerminate = control.Get("ignoreTerminate", ordinal, "false") == "true";
var warn = control.Get("warn", ordinal, "true") == "true";
var header = control.Get("header", ordinal, Sample.Header);

if (warn)
{
    stderr.WriteLine("warning: PresentMon requires elevated privilege in order to query processes that are");
    stderr.WriteLine("         short-running or started on another account.  Without it, those processes will");
    stderr.WriteLine("         be listed as '<unknown>' and they can't be targeted by --process_name nor trigger");
    stderr.WriteLine("         --terminate_on_proc_exit.");
}

Thread.Sleep(startDelay);

if (sessionFile is not null)
{
    Directory.CreateDirectory(sessionsDirectory);
    if (File.Exists(sessionFile) && !stopExisting)
    {
        stderr.WriteLine("error: a trace session named '" + sessionName + "' is already running");
        return 1;
    }

    File.WriteAllText(sessionFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
}

bool SessionEnded() => !ignoreTerminate && sessionFile is not null && !File.Exists(sessionFile);

if (mode == "hang")
{
    if (headerFirst)
    {
        stdout.WriteLine(header);
    }

    while (!SessionEnded())
    {
        Thread.Sleep(20);
    }

    return 0;
}

var endAt = timedSeconds is { } t && terminateAfterTimed ? DateTime.UtcNow.AddSeconds(t) : (DateTime?)null;
stdout.WriteLine(header);
for (var i = 0; i < rows; i++)
{
    if (SessionEnded())
    {
        break;
    }

    stdout.WriteLine(Sample.Row(i));
    if (mode == "crash" && i + 1 == int.Parse(control.Get("crashAfterRows", ordinal, "2"), CultureInfo.InvariantCulture))
    {
        return crashCode; // the marker stays behind, like the session of a crashed capture
    }

    if (rowDelay > 0)
    {
        Thread.Sleep(rowDelay);
    }
}

if (mode == "crash")
{
    return crashCode;
}

if (timedSeconds is not null && terminateAfterTimed)
{
    while (DateTime.UtcNow < endAt && !SessionEnded())
    {
        Thread.Sleep(20);
    }

    RemoveMarker();
    return 0;
}

if (!holdOpen)
{
    RemoveMarker();
    return 0;
}

while (!SessionEnded())
{
    Thread.Sleep(20);
}

return 0;

void RemoveMarker()
{
    if (sessionFile is not null && File.Exists(sessionFile))
    {
        File.Delete(sessionFile);
    }
}

static int CountCaptureLaunches(string path)
{
    if (!File.Exists(path))
    {
        return 0;
    }

    var count = 0;
    foreach (var line in ReadShared(path))
    {
        using var doc = JsonDocument.Parse(line);
        if (doc.RootElement.GetProperty("ordinal").GetInt32() > 0)
        {
            count++;
        }
    }

    return count;
}

static IEnumerable<string> ReadShared(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(stream, Encoding.UTF8);
    while (reader.ReadLine() is { } line)
    {
        if (line.Length > 0)
        {
            yield return line;
        }
    }
}

static void AppendLine(string path, string line)
{
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(line + "\n");
            return;
        }
        catch (IOException) when (attempt < 50)
        {
            Thread.Sleep(10);
        }
    }
}

internal sealed class Control(Dictionary<string, string> values)
{
    public static Control Load(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var split = line.IndexOf('=');
                if (split > 0)
                {
                    values[line[..split].Trim()] = line[(split + 1)..].Trim();
                }
            }
        }

        return new Control(values);
    }

    /// <summary>The value for launch number <paramref name="ordinal"/> (key.N) if given, else the plain key, else the fallback.</summary>
    public string Get(string key, int ordinal, string fallback) =>
        values.TryGetValue(key + "." + ordinal.ToString(CultureInfo.InvariantCulture), out var specific) ? specific
        : values.TryGetValue(key, out var general) ? general
        : fallback;
}

/// <summary>The 2.6.0 header and a few rows copied from the real, sanitized capture in tests/fixtures/presentmon/v2.6.0-real-sanitized.csv.</summary>
internal static class Sample
{
    public const string Header = "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,TimeInMs,MsBetweenSimulationStart,MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI,MsRenderPresentLatency,MsUntilDisplayed,CPUStartTimeInMs,MsBetweenAppStart,MsCPUBusy,MsCPUWait,MsGPULatency,MsGPUTime,MsGPUBusy,MsGPUWait,MsAnimationError,AnimationTime,MsFlipDelay,MsAllInputToPhotonLatency,MsClickToPhotonLatency";

    private static readonly string[] Rows =
    [
        "TestAppA.exe,1000,0x103E8,DXGI,0,0,0,Hardware Composed: Independent Flip,15.3549,NA,6.43950000000000,6.95100000000000,5.18820000000000,19.75560000000000,25.8716,13.6823,6.8608,1.6726,5.1882,14.4858,6.9424,6.9424,0.0000,NA,13.6823,NA,NA,NA",
        "TestAppA.exe,1000,0x103E8,DXGI,0,0,0,Hardware Composed: Independent Flip,22.1620,NA,6.80710000000000,7.01300000000000,5.32310000000000,19.85410000000000,26.0775,20.5431,6.9420,1.6189,5.3231,14.5674,6.9056,6.9056,0.0000,-0.1522,20.5431,NA,NA,NA",
        "TestAppA.exe,1000,0x103E8,DXGI,0,0,0,Hardware Composed: Independent Flip,29.0863,NA,6.92430000000000,6.91950000000000,5.32580000000000,20.06350000000000,26.0727,27.4851,6.9270,1.6012,5.3258,14.5310,7.1337,7.1337,0.0000,0.0225,27.4851,NA,NA,NA",
        "TestAppA.exe,1000,0x103E8,DXGI,0,0,0,Hardware Composed: Independent Flip,36.0896,NA,7.00330000000000,6.92010000000000,5.28470000000000,19.88210000000000,25.9895,34.4121,6.9622,1.6775,5.2847,14.7377,6.8219,6.8219,0.0000,0.0069,34.4121,NA,NA,NA",
        "TestAppA.exe,1000,0x103E8,DXGI,0,0,0,Hardware Composed: Independent Flip,43.1841,NA,7.09450000000000,7.01170000000000,5.28050000000000,19.64940000000000,25.9067,41.3743,7.0903,1.8098,5.2805,14.5974,6.8618,6.8618,0.0000,-0.0495,41.3743,NA,NA,NA",
    ];

    /// <summary>Row <paramref name="index"/>: the sample rows in turn, with TimeInMs (the ninth column) replaced so it increases steadily.</summary>
    public static string Row(int index)
    {
        var columns = Rows[index % Rows.Length].Split(',');
        columns[8] = ((index + 1) * 7.0).ToString("F4", CultureInfo.InvariantCulture);
        return string.Join(',', columns);
    }
}
