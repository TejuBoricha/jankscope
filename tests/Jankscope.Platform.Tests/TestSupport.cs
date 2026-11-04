using Jankscope.Core;

namespace Jankscope.Platform.Tests;

/// <summary>What this test account may do with ETW. Evaluated once, at discovery time.</summary>
internal static class EtwAccess
{
    private static readonly Lazy<bool> StartAllowed =
        new(() => EtwNative.TryOpenAndCloseRealTimeSession().StartStatus == EtwNative.ErrorSuccess);

    private static readonly Lazy<bool> EnumerateAllowed =
        new(() => EtwNative.QueryAllSessionNames().Status == EtwNative.ErrorSuccess);

    public static bool CanStart => StartAllowed.Value;

    public static bool CanEnumerate => EnumerateAllowed.Value;
}

/// <summary>Skipped, with a visible reason, when this account cannot start real-time ETW sessions. Never passes vacuously.</summary>
public sealed class EtwStartFactAttribute : FactAttribute
{
    public EtwStartFactAttribute()
    {
        if (!EtwAccess.CanStart || !EtwAccess.CanEnumerate)
        {
            Skip = "This account cannot start and enumerate real-time ETW sessions (needs Performance Log Users or elevation).";
        }
    }
}

/// <summary>ETW tests share one collection so they never run in parallel with each other.</summary>
[CollectionDefinition("ETW", DisableParallelization = true)]
public sealed class EtwCollection;

internal sealed class FakeServiceReader(IReadOnlyDictionary<string, ServiceSnapshot> snapshots) : IServiceStateReader
{
    public ServiceSnapshot Read(string serviceName) =>
        snapshots.TryGetValue(serviceName, out var snapshot) ? snapshot : ServiceSnapshot.NotInstalled;
}

/// <summary>A process-name snapshot; <c>null</c> models a snapshot that could not be taken.</summary>
internal sealed class FakeProcessSource(IEnumerable<string>? names) : IProcessNameSource
{
    public IReadOnlySet<string>? GetNames() =>
        names is null ? null : new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
}

internal sealed class FakeGameSource(GameSessionStatus status) : IGameSessionSource
{
    public GameSessionStatus Get() => status;
}

/// <summary>A scripted Service Control Manager that records every access mask requested and every handle opened and closed.</summary>
internal sealed class FakeScmNative(bool managerOk = true, uint openServiceError = 0, bool queryOk = true, uint currentState = 4) : IScmNative
{
    public List<uint> ManagerAccess { get; } = [];

    public List<uint> ServiceAccess { get; } = [];

    public int Opened { get; private set; }

    public int Closed { get; private set; }

    public nint OpenManager(uint access)
    {
        ManagerAccess.Add(access);
        if (!managerOk)
        {
            return 0;
        }

        Opened++;
        return 1;
    }

    public nint OpenService(nint manager, string name, uint access, out uint error)
    {
        ServiceAccess.Add(access);
        error = openServiceError;
        if (openServiceError != 0)
        {
            return 0;
        }

        Opened++;
        return 2;
    }

    public bool QueryState(nint service, out uint state)
    {
        state = queryOk ? currentState : 0;
        return queryOk;
    }

    public void Close(nint handle) => Closed++;
}
