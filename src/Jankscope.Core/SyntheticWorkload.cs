using System.Text.Json;

namespace Jankscope.Core;

/// <summary>A hitch injected at a known frame: the ground truth the detector must find (spec §18).</summary>
public sealed record InjectedHitch(int FrameIndex, double ExtraMs);

/// <summary>Deterministic synthetic frame-time series with known hitches. Always tagged <see cref="DataProvenance.Simulated"/>.</summary>
public sealed record SyntheticRun(
    int Seed,
    double TargetFps,
    IReadOnlyList<double> FrameTimesMs,
    IReadOnlyList<InjectedHitch> Hitches)
{
    public DataProvenance Provenance => DataProvenance.Simulated;

    public string ToJson() => JsonSerializer.Serialize(
        new { provenance = Provenance, seed = Seed, targetFps = TargetFps, frames = FrameTimesMs.Count, hitches = Hitches, frameTimesMs = FrameTimesMs },
        JankscopeJson.Options);
}

public static class SyntheticWorkload
{
    public const int WarmUpFrames = 60;
    public const int MinHitchSpacingFrames = 30;

    public static SyntheticRun Generate(int seed, double targetFps, int frames, int hitchCount, double hitchExtraMs, double jitterMs = 0.0)
    {
        if (!double.IsFinite(targetFps) || targetFps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFps), "must be finite and positive");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(hitchCount);
        if (!double.IsFinite(hitchExtraMs) || hitchExtraMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hitchExtraMs), "must be finite and not negative");
        }

        var baseMs = 1000.0 / targetFps;
        if (!double.IsFinite(jitterMs) || jitterMs < 0 || jitterMs >= baseMs)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterMs), "must be finite, not negative, and smaller than the base frame time (otherwise frames could have zero or negative duration)");
        }

        var usable = frames - WarmUpFrames;
        if (hitchCount > 0 && usable / MinHitchSpacingFrames < hitchCount)
        {
            throw new ArgumentException("Too many hitches for the run length and minimum spacing.", nameof(hitchCount));
        }

        var rng = new SplitMix64((ulong)(uint)seed);
        var times = new double[frames];
        for (var i = 0; i < frames; i++)
        {
            var jitter = jitterMs == 0 ? 0 : (rng.NextDouble() * 2 - 1) * jitterMs;
            times[i] = baseMs + jitter;
        }

        // One hitch per equal-width slot guarantees spacing >= MinHitchSpacingFrames without rejection loops.
        var hitches = new List<InjectedHitch>(hitchCount);
        if (hitchCount > 0)
        {
            var slot = usable / hitchCount;
            for (var h = 0; h < hitchCount; h++)
            {
                var offsetRange = Math.Max(1, slot - MinHitchSpacingFrames);
                var index = WarmUpFrames + h * slot + rng.NextInt(offsetRange);
                times[index] += hitchExtraMs;
                hitches.Add(new InjectedHitch(index, hitchExtraMs));
            }
        }

        return new SyntheticRun(seed, targetFps, times, hitches);
    }

    /// <summary>
    /// SplitMix64: a tiny, fully specified generator. <see cref="Random"/> is not guaranteed to produce the same sequence
    /// across .NET versions, which would silently change every "seed N" ground truth; this one never changes.
    /// </summary>
    private struct SplitMix64(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));

        public int NextInt(int exclusiveMax) => (int)(Next() % (ulong)exclusiveMax);
    }
}
