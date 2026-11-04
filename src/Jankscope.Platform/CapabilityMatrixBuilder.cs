using System.Diagnostics;
using System.Reflection;
using Jankscope.Core;
using Jankscope.Platform.Hardware;
using Jankscope.Platform.Pdh;

namespace Jankscope.Platform;

/// <summary>Collector contract (spec §6.4), reduced to what the capability probe needs: report what is and is not available.</summary>
public interface IProbe
{
    string Id { get; }

    IReadOnlyList<CapabilityEntry> Run();
}

/// <summary>
/// Runs probes in isolation: a failing or hung probe becomes an explicit "not available (reason)" row and never stops the
/// others. Only the exception <em>type</em> is exported; messages can contain paths and user names (review #16). A probe that
/// exceeds <see cref="DefaultProbeTimeout"/> is abandoned on its worker thread (a blocked WMI call cannot be cancelled).
/// </summary>
public sealed class CapabilityMatrixBuilder(IEnumerable<IProbe> probes, TimeProvider? timeProvider = null, TimeSpan? probeTimeout = null)
{
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(15);

    private readonly IReadOnlyList<IProbe> _probes = probes.ToList();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _timeout = probeTimeout ?? DefaultProbeTimeout;

    public CapabilityMatrix Build()
    {
        var entries = new List<CapabilityEntry>();
        var health = new List<ProbeHealth>();

        foreach (var probe in _probes)
        {
            var watch = Stopwatch.StartNew();
            string? error = null;
            try
            {
                var task = Task.Factory.StartNew(probe.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                if (((IAsyncResult)task).AsyncWaitHandle.WaitOne(_timeout))
                {
                    entries.AddRange(task.GetAwaiter().GetResult());
                }
                else
                {
                    error = $"timed out after {_timeout.TotalSeconds:0.##} s";
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name;
            }

            if (error is null)
            {
                health.Add(new ProbeHealth(probe.Id, true, watch.Elapsed.TotalMilliseconds, null));
                continue;
            }

            entries.Add(CapabilityEntry.Unavailable(
                $"{probe.Id}.probe", $"Probe '{probe.Id}'", CapabilityTier.A, Availability.NotAvailable,
                "probe isolation", $"probe failed ({error})"));
            health.Add(new ProbeHealth(probe.Id, false, watch.Elapsed.TotalMilliseconds, error));
        }

        entries.AddRange(DerivedCapabilities.Compute(entries));
        return new CapabilityMatrix(_time.GetUtcNow(), ToolVersion(), entries, health);
    }

    private static string ToolVersion() =>
        typeof(CapabilityMatrixBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
}

public static class DefaultProbes
{
    public static IReadOnlyList<IProbe> Create(CatalogSet catalogs)
    {
        var gameSession = new CatalogGameSessionSource(catalogs);
        return
        [
            new OsProbe(),
            new PrivilegeProbe(),
            new EtwProbe(),
            new SecurityPostureProbe(),
            new HardwareProbe(),
            new ManagedDeviceProbe(),
            new AntiCheatProbe(catalogs, gameSession: gameSession),
            new GameSessionProbe(gameSession),
            ..HardwareDiscoveryProbes.Create(catalogs),
            new PdhCountersProbe(),
            new PlannedProbe(),
        ];
    }
}

/// <summary>Rows that exist in the plan but have no collector yet. They say so instead of guessing (spec §1 #6).</summary>
public sealed class PlannedProbe : IProbe
{
    public string Id => "planned";

    public IReadOnlyList<CapabilityEntry> Run() =>
    [
        CapabilityEntry.Planned("input.devices", "Pointing devices and Raw Input timing", CapabilityTier.A, "Stage 2"),
        CapabilityEntry.Planned("network.probes", "Gateway, DNS and internet latency probes", CapabilityTier.A, "Stage 2"),
    ];
}

/// <summary>Capabilities computed from other rows: frame capture (coverage matrix, spec §6.6) and the optimize-mode gate (HC-11, §3.5).</summary>
public static class DerivedCapabilities
{
    public static IReadOnlyList<CapabilityEntry> Compute(IReadOnlyList<CapabilityEntry> entries)
    {
        CapabilityEntry? Find(string id) => entries.FirstOrDefault(e => e.Id == id);
        var result = new List<CapabilityEntry>();

        var etw = Find("etw.realtime_session");
        var elevated = PrivilegeProbe.IsElevatedValue(Find("privilege.elevation")?.Value);
        if (etw is null)
        {
            result.Add(CapabilityEntry.Unavailable("frame.capture", "Frame-level capture", CapabilityTier.C, Availability.Unknown,
                "derived", "unknown: the real-time ETW access probe did not report"));
        }
        else if (etw.Availability == Availability.Available && elevated != false)
        {
            // An elevated token (or an unknown one) succeeding proves nothing about an unelevated process (review #2).
            result.Add(CapabilityEntry.Unavailable("frame.capture", "Frame-level capture", CapabilityTier.C, Availability.Unknown,
                "derived", "Real-time ETW access worked, but the probing process " +
                           (elevated == true ? "is elevated" : "has an unknown elevation state") +
                           ", so unelevated frame capture in game sessions is unproven. Re-run the probe from a non-elevated process."));
        }
        else if (etw.Availability == Availability.Available)
        {
            result.Add(CapabilityEntry.Unavailable("frame.capture", "Frame-level capture", CapabilityTier.C, Availability.NeedsOptionalTool,
                "derived", "Unelevated real-time ETW access works, so frame capture is possible in game sessions. " +
                           "The pinned frame-capture tool is not bundled yet (Stage 1a)."));
        }
        else if (etw.Availability != Availability.NeedsUserAction)
        {
            result.Add(CapabilityEntry.Unavailable("frame.capture", "Frame-level capture", CapabilityTier.C, Availability.Unknown,
                "derived", $"Real-time ETW access could not be established ({etw.Availability}: {etw.Reason}), so frame capture cannot be assessed."));
        }
        else
        {
            result.Add(CapabilityEntry.Unavailable("frame.capture", "Frame-level capture", CapabilityTier.C, Availability.NeedsUserAction,
                "derived", "Frame pacing is unavailable without real-time ETW access: a one-time user action (membership in " +
                           "Performance Log Users, see INSTALL.md) enables it in game sessions; outside game sessions the elevated helper can capture."));
        }

        // Optimize mode needs every input to be known: a row that is missing or anything other than Available is a blocker (HC-11).
        var blockers = new List<string>();
        void Check(string id, string label, Func<CapabilityEntry, bool> isBlocker, string? whenNotAvailable = null)
        {
            var e = Find(id);
            if (e is null || e.Availability != Availability.Available)
            {
                blockers.Add(e?.Availability == Availability.NotAvailable && whenNotAvailable is not null ? whenNotAvailable : $"{label} unknown");
            }
            else if (isBlocker(e))
            {
                blockers.Add(label);
            }
        }

        Check("os.support", "Windows support state", _ => false, whenNotAvailable: "unsupported Windows");
        // The managed rows compare against the exact values the probes emit (ManagedValues), never a text prefix: a value that is neither a
        // known blocking value nor a known clean value is itself a blocker, so rewording a probe can only make the gate stricter.
        void CheckValue(string id, string label, Func<string, bool> blocking, Func<string, bool> known, string? blockingLabel = null)
        {
            var e = Find(id);
            if (e is null || e.Availability != Availability.Available || e.Value is null)
            {
                blockers.Add($"{label} unknown");
            }
            else if (blocking(e.Value))
            {
                blockers.Add(blockingLabel ?? label);
            }
            else if (!known(e.Value))
            {
                blockers.Add(label.EndsWith(" state", StringComparison.Ordinal) ? $"{label} unrecognized" : $"{label} state unrecognized");
            }
        }

        void CheckManaged(string id, string label, string blockingValue, params string[] cleanValues) =>
            CheckValue(id, label, v => v == blockingValue, cleanValues.Contains);

        CheckManaged("managed.domain_join", "domain-joined device", ManagedValues.DomainJoined, ManagedValues.Workgroup, ManagedValues.NotJoined);
        CheckManaged("managed.mdm_enrollment", "MDM-enrolled device", ManagedValues.MdmEnrolled, ManagedValues.MdmNone);
        CheckManaged("managed.s_mode", "S mode", ManagedValues.SModeOn, ManagedValues.SModeOff);
        CheckManaged("managed.applied_policies", "applied-policy state", ManagedValues.PoliciesApplied, ManagedValues.PoliciesNone);
        CheckManaged("managed.entra_join", "Entra-join state", ManagedValues.EntraJoined, ManagedValues.EntraNotJoined);
        // A detected virtual machine is Recommend-only like a managed device (spec section 6.6 and ADR 0003: "VM, managed or unknown configuration");
        // timing and GPU data in a guest are not those of the hardware. Review 1a, finding 5: this used to be constant false.
        CheckValue("environment.virtual_machine", "virtual-machine state", ManagedValues.IsVirtualMachine, ManagedValues.IsKnownVirtualMachineValue, "virtual machine detected");
        CheckValue("game.session", "game-session state", _ => false, ManagedValues.IsKnownGameSessionValue);
        CheckValue("anticheat.state", "anti-cheat state", _ => false, ManagedValues.IsKnownAntiCheatStateValue);

        result.Add(blockers.Count == 0
            ? CapabilityEntry.Available("mode.optimize", "Optimize mode eligibility", CapabilityTier.A, "derived",
                "eligible (each action still needs its own eligibility, evidence and approval; Stage 4)", DataProvenance.Inferred)
            : CapabilityEntry.Unavailable("mode.optimize", "Optimize mode eligibility", CapabilityTier.A, Availability.NotAvailable, "derived",
                $"Recommend-only: {string.Join("; ", blockers)}. Managed or unknown configurations default to Recommend-only (HC-11, spec §3.5)."));

        return result;
    }
}
