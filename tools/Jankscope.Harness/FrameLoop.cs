namespace Jankscope.Harness;

/// <summary>
/// One presented frame as the harness itself observed it. All times are clock ticks from the pacing origin.
/// <see cref="PresentTicks"/> is the harness's own timestamp taken when <c>Present()</c> returned; it does not depend on any capture tool.
/// </summary>
internal readonly record struct FrameRecord(int Index, long ScheduledTicks, long PresentTicks, long PresentCallTicks, double InjectedExtraMs);

internal enum PresentOutcome
{
    Presented,

    /// <summary>The window was not visible (DXGI_STATUS_OCCLUDED): the call returned normally and is timed like any other.</summary>
    Occluded,
}

/// <summary>What the frame loop needs from a renderer: prepare a frame's contents ahead of time, then present it on cue.</summary>
internal interface IFramePresenter
{
    void Prepare(int frameIndex, bool isHitch);

    PresentOutcome Present();
}

internal sealed record FrameLoopResult(IReadOnlyList<FrameRecord> Records, long OriginTimestamp, long EndTimestamp, int OccludedPresents);

internal static class FrameLoop
{
    /// <summary>
    /// Presents every scheduled frame at its absolute time. The frame's contents are prepared before the wait so that <c>Present()</c> itself is
    /// the only call between the target time and the timestamp. <paramref name="afterPresent"/> runs after the timestamp was taken
    /// (message pumping for a visible window), so it cannot add to the measured Present time. Cancellation is honoured between frames.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled; the caller still owns, and disposes, the presenter.</exception>
    public static FrameLoopResult Run(
        FrameSchedule schedule,
        IFramePresenter presenter,
        FramePacer pacer,
        IClock clock,
        Action? afterPresent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(pacer);
        ArgumentNullException.ThrowIfNull(clock);

        var records = new FrameRecord[schedule.Count];
        var occluded = 0;
        var origin = clock.GetTimestamp();
        for (var i = 0; i < records.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            presenter.Prepare(i, schedule.IsHitch(i));
            pacer.WaitUntil(origin + schedule.TargetTicks[i]);

            var before = clock.GetTimestamp();
            var outcome = presenter.Present();
            var after = clock.GetTimestamp();

            if (outcome == PresentOutcome.Occluded)
            {
                occluded++;
            }

            records[i] = new FrameRecord(i, schedule.TargetTicks[i], after - origin, after - before, schedule.InjectedExtraMs[i]);
            afterPresent?.Invoke();
        }

        return new FrameLoopResult(records, origin, clock.GetTimestamp(), occluded);
    }
}
