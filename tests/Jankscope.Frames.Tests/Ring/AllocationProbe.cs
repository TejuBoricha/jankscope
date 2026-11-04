namespace Jankscope.Frames.Tests.Ring;

/// <summary>
/// Measures managed allocations made by the current thread while a piece of work runs.
/// </summary>
internal static class AllocationProbe
{
    /// <summary>
    /// Runs <paramref name="work"/> up to <paramref name="attempts"/> times and returns the smallest number of bytes any single
    /// attempt allocated on this thread (and every attempt's figure in <paramref name="deltas"/>). Code that allocates does so on every
    /// attempt, so its minimum is never zero; the occasional stray byte count from the runtime or the garbage collector while other
    /// tests run on the machine disappears in the minimum instead of making the test flaky.
    /// </summary>
    public static long MinimumBytes(Action work, out long[] deltas, int attempts = 5)
    {
        var seen = new List<long>(attempts);
        for (var i = 0; i < attempts; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            work();
            var delta = GC.GetAllocatedBytesForCurrentThread() - before;
            seen.Add(delta);
            if (delta == 0)
            {
                break; // one clean attempt is proof enough
            }
        }

        deltas = seen.ToArray();
        return seen.Min();
    }
}
