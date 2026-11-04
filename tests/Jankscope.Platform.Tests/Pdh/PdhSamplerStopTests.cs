using System.Diagnostics;
using Jankscope.Platform.Pdh;

namespace Jankscope.Platform.Tests.Pdh;

/// <summary>Stopping the sampler (review 1a, finding 13 and mutation L5): it waits for the running tick, and it never waits for ever.</summary>
[Collection("PdhTiming")]
public class PdhSamplerStopTests
{
    [Fact]
    public void Stop_waits_for_the_tick_that_is_running_and_reports_that_the_thread_ended()
    {
        using var entered = new ManualResetEventSlim(false);
        var finishedTick = 0;
        var fake = new FakeCounterSource(call =>
        {
            if (call == 2) // call 1 primes the counters; call 2 is the first real tick
            {
                entered.Set();
                Thread.Sleep(400);
                Volatile.Write(ref finishedTick, 1);
            }

            return FakeCounterSource.Snapshot();
        });
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), _ => { });

        sampler.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "the sampler never reached its first tick");
        var stopped = sampler.Stop();

        Assert.True(stopped);
        Assert.Equal(1, Volatile.Read(ref finishedTick)); // Stop did not return before the running tick finished
        Assert.False(sampler.IsRunning);
    }

    [Fact]
    public void Stop_with_a_timeout_returns_false_when_a_counter_provider_never_comes_back_and_the_sampler_ends_once_it_does()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var fake = new FakeCounterSource(call =>
        {
            if (call == 2)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }

            return FakeCounterSource.Snapshot();
        });
        using var sampler = new PdhSampler(fake, new SamplerOptions(10), _ => { });

        sampler.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var watch = Stopwatch.StartNew();
        var stopped = sampler.Stop(TimeSpan.FromMilliseconds(150));
        watch.Stop();

        Assert.False(stopped);
        Assert.True(sampler.IsRunning);
        Assert.InRange(watch.Elapsed.TotalSeconds, 0.1, 5);

        release.Set();
        Assert.True(sampler.Stop(TimeSpan.FromSeconds(10)));
        Assert.False(sampler.IsRunning);
    }

    [Fact]
    public void Stop_before_start_and_repeated_stops_are_harmless()
    {
        using var sampler = new PdhSampler(new FakeCounterSource(_ => FakeCounterSource.Snapshot()), new SamplerOptions(10), _ => { });
        Assert.True(sampler.Stop());
        sampler.Start();
        Assert.True(sampler.Stop());
        Assert.True(sampler.Stop());
    }
}
