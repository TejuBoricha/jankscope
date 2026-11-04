using System.Globalization;
using System.Text;
using Jankscope.Core;
using Jankscope.Core.Reports;
using Jankscope.Frames;
using Jankscope.Frames.Detection;
using Jankscope.Frames.PresentMon;
using Jankscope.Frames.Reports;
using Jankscope.Platform;

Console.OutputEncoding = Encoding.UTF8;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var asJson = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
var redact = args.Contains("--redact", StringComparer.OrdinalIgnoreCase);

switch (command)
{
    case "probe":
        return Probe(asJson, redact);
    case "incident":
        return Incident(args);
    case "version":
        Console.WriteLine($"{Branding.ProductName} {typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.1.0"}");
        return 0;
    default:
        Console.WriteLine($"""
            {Branding.ProductName} (Stage 1a/1b diagnostic CLI)

            Usage:
              jankscope probe [--json] [--redact]      Show the runtime capability matrix for this PC
              jankscope incident --from-csv <path>      Generate an incident diagnosis report from a frame capture
                                 [--out <path>] [--no-redact] [--process <name>]
              jankscope version                         Show the version

            The probe makes no changes to your system and sends nothing anywhere. It briefly opens and
            closes one tracing session to test whether real-time frame capture is allowed.
            """);
        return command == "help" ? 0 : 2;
}

static CatalogSet LoadCatalogs()
{
    var dir = CatalogSet.FindDefaultDirectory(AppContext.BaseDirectory);
    try
    {
        return dir is null ? CatalogSet.Empty : CatalogSet.Load(dir);
    }
    catch (Exception ex) when (ex is InvalidDataException or IOException)
    {
        Console.Error.WriteLine($"Catalog problem (failing closed, anti-cheat knowledge unavailable): {ex.Message}");
        return CatalogSet.Empty;
    }
}

static int Probe(bool asJson, bool redact)
{
    var catalogs = LoadCatalogs();
    var matrix = new CapabilityMatrixBuilder(DefaultProbes.Create(catalogs)).Build();
    if (redact)
    {
        matrix = matrix.Redacted();
    }

    if (asJson)
    {
        Console.WriteLine(matrix.ToJson());
        return 0;
    }

    Console.WriteLine($"{Branding.ProductName} capability matrix  ({matrix.GeneratedUtc:u})");
    Console.WriteLine();
    foreach (var e in matrix.Entries)
    {
        var detail = e.Availability == Availability.Available ? e.Value : $"{e.Availability}: {e.Reason}";
        Console.WriteLine($"[{e.Tier}] {e.Id,-34} {detail}");
    }

    Console.WriteLine();
    var failed = matrix.ProbeHealth.Where(h => !h.Succeeded).ToList();
    Console.WriteLine($"Probes: {matrix.ProbeHealth.Count - failed.Count} ok, {failed.Count} failed. Source of each row is in --json output.");
    return 0;
}

static int Incident(string[] args)
{
    string? fromCsv = null;
    string? outPath = null;
    string? process = null;
    var doRedact = !args.Contains("--no-redact", StringComparer.OrdinalIgnoreCase);

    for (var i = 1; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--from-csv", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            fromCsv = args[++i];
        }
        else if (string.Equals(args[i], "--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            outPath = args[++i];
        }
        else if (string.Equals(args[i], "--process", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            process = args[++i];
        }
    }

    if (string.IsNullOrWhiteSpace(fromCsv) || !File.Exists(fromCsv))
    {
        Console.Error.WriteLine("Error: --from-csv <path> is required and must point to an existing CSV file.");
        return 1;
    }

    using var reader = new StreamReader(fromCsv);
    var samples = PresentMonCsv.ReadAll(reader, out var header, out var problems, DataProvenance.Measured);
    if (!header.Supported)
    {
        Console.Error.WriteLine($"Error: unsupported CSV format ({header.UnsupportedReason}).");
        return 1;
    }

    var valid = samples.Where(s => s.PresentMode != "Other").ToArray();
    if (valid.Length == 0)
    {
        Console.Error.WriteLine("Error: CSV contains no valid swapchain presentation frames.");
        return 1;
    }

    var detectorResult = StutterDetector.Analyze(valid);
    var catalogs = LoadCatalogs();
    var matrix = new CapabilityMatrixBuilder(DefaultProbes.Create(catalogs)).Build();

    var report = IncidentReportBuilder.Build(
        IncidentTrigger.UserMarked,
        process ?? valid[0].Application,
        valid,
        detectorResult,
        matrix,
        redact: doRedact);

    var json = report.ToJson();
    if (doRedact)
    {
        ExportGuard.Verify(json, RedactionContext.ForThisMachine());
    }

    if (!string.IsNullOrWhiteSpace(outPath))
    {
        var full = Path.GetFullPath(outPath);
        var dir = Path.GetDirectoryName(full) ?? throw new IOException("The output path has no directory.");
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, Path.GetFileName(full) + ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        Console.WriteLine($"Incident report written to {outPath}");
    }
    else
    {
        Console.WriteLine(json);
    }

    return 0;
}
