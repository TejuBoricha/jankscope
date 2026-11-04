using Jankscope.Core;

namespace Jankscope.Platform;

public enum GameSessionStatus
{
    /// <summary>The status cannot be established. Never the same as <see cref="Inactive"/>.</summary>
    Unknown,
    Inactive,
    Active,
}

/// <summary>
/// Seam for game-session detection (spec HC-1 state 2; implemented in Stage 1a).
/// </summary>
public interface IGameSessionSource
{
    GameSessionStatus Get();
}

/// <summary>The honest default until game-session detection exists.</summary>
public sealed class UnimplementedGameSessionSource : IGameSessionSource
{
    public GameSessionStatus Get() => GameSessionStatus.Unknown;
}

public readonly record struct GameSessionResult(GameSessionStatus Status, string? ActiveTitle, string? Error);

/// <summary>
/// Detects game sessions by comparing a process-name snapshot against the consented protected titles catalog (HC-1 state 2/3).
/// Never opens foreign-process handles (HC-1).
/// </summary>
public sealed class CatalogGameSessionSource(CatalogSet catalogs, IProcessNameSource? processes = null) : IGameSessionSource
{
    private readonly IProcessNameSource _processes = processes ?? new SystemProcessNameSource();

    public GameSessionResult Evaluate()
    {
        if (catalogs.ProtectedTitles.Count == 0)
        {
            return new GameSessionResult(GameSessionStatus.Unknown, null, "protected-titles catalog not loaded");
        }

        var processNames = _processes.GetNames();
        if (processNames is null)
        {
            return new GameSessionResult(GameSessionStatus.Unknown, null, "process-name snapshot unavailable");
        }

        var match = catalogs.ProtectedTitles.FirstOrDefault(t =>
            t.ProcessNames.Any(p => processNames.Contains(ProcessNames.Normalize(p))));

        return match is not null
            ? new GameSessionResult(GameSessionStatus.Active, match.DisplayName, null)
            : new GameSessionResult(GameSessionStatus.Inactive, null, null);
    }

    public GameSessionStatus Get() => Evaluate().Status;
}

/// <summary>
/// Capability probe for game session detection. Emits the <c>game.session</c> row.
/// </summary>
public sealed class GameSessionProbe(IGameSessionSource? sessionSource = null, CatalogSet? catalogs = null) : IProbe
{
    private readonly IGameSessionSource _source = sessionSource ?? new CatalogGameSessionSource(catalogs ?? CatalogSet.Empty);
    public string Id => "game";

    public IReadOnlyList<CapabilityEntry> Run()
    {
        const string Title = "Game session detection (consented list)";
        const string Source = "process-name snapshot, catalog-driven";

        if (_source is CatalogGameSessionSource catalogSource)
        {
            var res = catalogSource.Evaluate();
            return
            [
                res.Status switch
                {
                    GameSessionStatus.Active => CapabilityEntry.Available("game.session", Title, CapabilityTier.A,
                        Source, $"{ManagedValues.GameActivePrefix}{res.ActiveTitle}"),
                    GameSessionStatus.Inactive => CapabilityEntry.Available("game.session", Title, CapabilityTier.A,
                        Source, ManagedValues.GameNone),
                    _ => CapabilityEntry.Unavailable("game.session", Title, CapabilityTier.A, Availability.Unknown,
                        Source, res.Error ?? "game session status could not be established"),
                }
            ];
        }

        var status = _source.Get();
        return
        [
            status switch
            {
                GameSessionStatus.Active => CapabilityEntry.Available("game.session", Title, CapabilityTier.A,
                    Source, $"{ManagedValues.GameActivePrefix}active session"),
                GameSessionStatus.Inactive => CapabilityEntry.Available("game.session", Title, CapabilityTier.A,
                    Source, ManagedValues.GameNone),
                _ => CapabilityEntry.Unavailable("game.session", Title, CapabilityTier.A, Availability.Unknown,
                    Source, "game session status could not be established"),
            }
        ];
    }
}
