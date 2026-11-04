using System.Globalization;

namespace Jankscope.Harness;

internal enum HarnessCommandKind
{
    Invalid,
    Help,
    Plan,
    Run,
}

internal sealed record ParsedArguments(HarnessCommandKind Kind, PlanOptions? Plan, RunOptions? Run, string? Error);

/// <summary>
/// Strict command-line parsing. A value that cannot be read is an error with a reason: the Stage 0 skeleton silently fell back to the default
/// for a typo, which would let a mistyped hitch count produce a "valid" run of the wrong workload.
/// </summary>
internal static class HarnessArguments
{
    private static readonly string[] HelpWords = ["--help", "-h", "help", "/?"];
    private static readonly string[] WorkloadFlags = ["--seed", "--fps", "--frames", "--hitches", "--extra-ms", "--jitter-ms"];

    public static ParsedArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            return Fail("no command given");
        }

        if (HelpWords.Contains(args[0], StringComparer.Ordinal) || args.Any(a => a is "--help" or "-h"))
        {
            return new ParsedArguments(HarnessCommandKind.Help, null, null, null);
        }

        var command = args[0];
        if (command is not ("plan" or "run"))
        {
            return Fail($"unknown command '{command}'");
        }

        var isRun = command == "run";
        var valueFlags = WorkloadFlags.Append("--out").Concat(isRun ? ["--adapter"] : []).ToHashSet(StringComparer.Ordinal);
        var switches = isRun ? new HashSet<string>(["--window-visible"], StringComparer.Ordinal) : [];

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var given = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            var flag = args[i];
            if (!valueFlags.Contains(flag) && !switches.Contains(flag))
            {
                return Fail(flag.StartsWith('-') ? $"unknown option '{flag}' for '{command}'" : $"unexpected argument '{flag}'");
            }

            if (!given.Add(flag))
            {
                return Fail($"option '{flag}' given more than once");
            }

            if (switches.Contains(flag))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                return Fail($"option '{flag}' needs a value");
            }

            values[flag] = args[++i];
        }

        if (!TryWorkload(values, out var workload, out var error))
        {
            return Fail(error!);
        }

        values.TryGetValue("--out", out var outPath);
        if (!isRun)
        {
            return new ParsedArguments(HarnessCommandKind.Plan, new PlanOptions(workload!, outPath), null, null);
        }

        if (string.IsNullOrWhiteSpace(outPath))
        {
            return Fail("'run' needs --out FILE (the CSV; the summary is written next to it)");
        }

        if (outPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("--out names the CSV and must not end in .json: the JSON summary is written next to it as <name>.summary.json");
        }

        var adapter = AdapterRequest.Auto;
        if (values.TryGetValue("--adapter", out var adapterText))
        {
            // Spelled out on purpose: Enum.TryParse would also accept numbers and comma lists.
            switch (adapterText.ToLowerInvariant())
            {
                case "auto":
                    adapter = AdapterRequest.Auto;
                    break;
                case "hardware":
                    adapter = AdapterRequest.Hardware;
                    break;
                case "warp":
                    adapter = AdapterRequest.Warp;
                    break;
                default:
                    return Fail($"--adapter must be auto, hardware or warp (got '{adapterText}')");
            }
        }

        return new ParsedArguments(HarnessCommandKind.Run, null, new RunOptions(workload!, outPath, adapter, given.Contains("--window-visible")), null);
    }

    /// <summary>The summary file next to the CSV: same directory and name, extension replaced by <c>.summary.json</c>.</summary>
    public static string SummaryPathFor(string csvPath)
    {
        var directory = Path.GetDirectoryName(csvPath);
        var name = Path.GetFileNameWithoutExtension(csvPath) + ".summary.json";
        return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
    }

    private static bool TryWorkload(Dictionary<string, string> values, out WorkloadOptions? workload, out string? error)
    {
        workload = null;
        if (!TryInt(values, "--seed", 1, out var seed, out error)
            || !TryDouble(values, "--fps", 144, out var fps, out error)
            || !TryInt(values, "--frames", 3000, out var frames, out error)
            || !TryInt(values, "--hitches", 8, out var hitches, out error)
            || !TryDouble(values, "--extra-ms", 25, out var extra, out error)
            || !TryDouble(values, "--jitter-ms", 0.2, out var jitter, out error))
        {
            return false;
        }

        workload = new WorkloadOptions(seed, fps, frames, hitches, extra, jitter);
        return true;
    }

    private static bool TryInt(Dictionary<string, string> values, string flag, int fallback, out int result, out string? error)
    {
        result = fallback;
        error = null;
        if (!values.TryGetValue(flag, out var text))
        {
            return true;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        error = $"{flag} needs a whole number (got '{text}')";
        return false;
    }

    private static bool TryDouble(Dictionary<string, string> values, string flag, double fallback, out double result, out string? error)
    {
        result = fallback;
        error = null;
        if (!values.TryGetValue(flag, out var text))
        {
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result))
        {
            return true;
        }

        error = $"{flag} needs a finite number with '.' as the decimal separator (got '{text}')";
        return false;
    }

    private static ParsedArguments Fail(string error) => new(HarnessCommandKind.Invalid, null, null, error);
}
