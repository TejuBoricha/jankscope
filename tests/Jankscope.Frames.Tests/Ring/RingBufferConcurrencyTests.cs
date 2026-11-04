using System.Diagnostics;
using Jankscope.Frames.Ring;
using Xunit.Abstractions;

namespace Jankscope.Frames.Tests.Ring;

public class RingBufferConcurrencyTests(ITestOutputHelper output)
{
    private static readonly TimeSpan StressDuration = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// 64 bytes whose fields all derive from <see cref="Seq"/>. A reader that sees fields from two different writes
    /// (a torn read) cannot satisfy <see cref="IsIntact"/>, and the sequence number doubles as a contiguity check.
    /// </summary>
    private readonly struct Record
    {
        public readonly long Seq, A, B, C, D, E, F, G;

        public Record(long seq)
        {
            Seq = seq;
            A = (seq * 3) + 1;
            B = ~seq;
            C = seq ^ 0x5DEECE66DL;
            D = seq + 1_000_003;
            E = seq * 5;
            F = -seq;
            G = seq ^ -1234567L;
        }

        public bool IsIntact =>
            A == (Seq * 3) + 1 && B == ~Seq && C == (Seq ^ 0x5DEECE66DL) && D == Seq + 1_000_003 &&
            E == Seq * 5 && F == -Seq && G == (Seq ^ -1234567L);
    }

    private sealed class ReaderStats
    {
        public long Snapshots;
        public long ItemsSeen;
        public long Torn;
        public long Gaps;
        public long WrongFirstSequence;
    }

    [Fact]
    public void Three_readers_never_see_a_torn_or_non_contiguous_snapshot_while_one_writer_overwrites_continuously()
    {
        // A small buffer and a writer in a tight loop make the writer lap readers constantly: the readers that copy the
        // whole buffer are lapped on almost every copy, so a missing validation step cannot go unnoticed.
        const int capacity = 256;
        var ring = new RingBuffer<Record>(capacity);
        var stop = 0;
        long written = 0;

        var writer = new Thread(() =>
        {
            long i = 0;
            while (Volatile.Read(ref stop) == 0)
            {
                ring.Write(new Record(i));
                i++;
            }

            Volatile.Write(ref written, i);
        }) { IsBackground = true, Name = "ring-writer" };

        var readerSizes = new[] { 8, 64, capacity };
        var stats = readerSizes.Select(_ => new ReaderStats()).ToArray();
        var readers = readerSizes.Select((size, n) => new Thread(() =>
        {
            var buffer = new Record[size];
            var s = stats[n];
            while (Volatile.Read(ref stop) == 0)
            {
                var copied = ring.CopyLatest(buffer, out var first);
                s.Snapshots++;
                s.ItemsSeen += copied;
                for (var k = 0; k < copied; k++)
                {
                    if (!buffer[k].IsIntact)
                    {
                        s.Torn++;
                    }

                    if (buffer[k].Seq != buffer[0].Seq + k)
                    {
                        s.Gaps++;
                    }
                }

                if (copied > 0 && buffer[0].Seq != first)
                {
                    s.WrongFirstSequence++;
                }
            }
        }) { IsBackground = true, Name = $"ring-reader-{size}" }).ToArray();

        writer.Start();
        foreach (var r in readers)
        {
            r.Start();
        }

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < StressDuration)
        {
            Thread.Sleep(50);
        }

        Volatile.Write(ref stop, 1);
        Assert.True(writer.Join(TimeSpan.FromSeconds(10)), "writer did not stop");
        foreach (var r in readers)
        {
            Assert.True(r.Join(TimeSpan.FromSeconds(10)), "a reader did not stop");
        }

        foreach (var (s, size) in stats.Zip(readerSizes))
        {
            Assert.True(s.Torn == 0, $"reader({size}): {s.Torn} torn records");
            Assert.True(s.Gaps == 0, $"reader({size}): {s.Gaps} non-contiguous items");
            Assert.True(s.WrongFirstSequence == 0, $"reader({size}): firstSequence disagreed with the data {s.WrongFirstSequence} times");
        }

        output.WriteLine($"writer wrote {Volatile.Read(ref written):N0} items in {clock.Elapsed.TotalSeconds:F1} s");
        foreach (var (s, size) in stats.Zip(readerSizes))
        {
            output.WriteLine($"reader({size}): {s.Snapshots:N0} snapshots, {s.ItemsSeen:N0} items verified, torn={s.Torn}, gaps={s.Gaps}");
        }

        // Guard against a vacuous pass: the small readers must have really read a lot of data, and the writer must have lapped the buffer many times.
        Assert.True(stats[0].ItemsSeen > 10_000, $"reader(8) saw only {stats[0].ItemsSeen} items");
        Assert.True(stats[1].ItemsSeen > 1_000, $"reader(64) saw only {stats[1].ItemsSeen} items");
        Assert.True(Volatile.Read(ref written) > capacity * 100L, $"writer wrote only {written} items");

        // After the writer stopped, the buffer holds exactly the newest `capacity` records.
        var total = Volatile.Read(ref written);
        Assert.Equal(total, ring.TotalWritten);
        var final = ring.Snapshot();
        Assert.Equal(capacity, final.Length);
        for (var k = 0; k < final.Length; k++)
        {
            Assert.True(final[k].IsIntact);
            Assert.Equal(total - capacity + k, final[k].Seq);
        }
    }

    [Fact]
    public void A_second_concurrent_writer_is_refused_instead_of_corrupting_the_buffer()
    {
        var ring = new RingBuffer<Record>(64);
        var deadline = Stopwatch.StartNew();
        long refusedA = 0, refusedB = 0;
        var stop = 0;

        Thread Start(string name, Action<long> onRefused) => new(() =>
        {
            long i = 0;
            while (Volatile.Read(ref stop) == 0)
            {
                if (!ring.TryWrite(new Record(i)))
                {
                    onRefused(1);
                }

                i++;
            }
        }) { IsBackground = true, Name = name };

        var a = Start("writer-a", n => refusedA += n);
        var b = Start("writer-b", n => refusedB += n);
        a.Start();
        b.Start();

        while (Interlocked.Read(ref refusedA) + Interlocked.Read(ref refusedB) == 0 && deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(10);
        }

        Volatile.Write(ref stop, 1);
        Assert.True(a.Join(TimeSpan.FromSeconds(10)) && b.Join(TimeSpan.FromSeconds(10)), "a writer did not stop");

        Assert.True(Interlocked.Read(ref refusedA) + Interlocked.Read(ref refusedB) > 0, "two simultaneous writers were never detected");
        foreach (var record in ring.Snapshot())
        {
            Assert.True(record.IsIntact);
        }
    }

    [Fact]
    public void Write_throws_when_another_writer_is_active_but_TryWrite_does_not()
    {
        var ring = new RingBuffer<Record>(64);
        var stop = 0;
        var holder = new Thread(() =>
        {
            long i = 0;
            while (Volatile.Read(ref stop) == 0)
            {
                ring.TryWrite(new Record(i++));
            }
        }) { IsBackground = true };
        holder.Start();

        var sawThrow = false;
        var clock = Stopwatch.StartNew();
        while (!sawThrow && clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            try
            {
                ring.Write(new Record(-1));
            }
            catch (InvalidOperationException)
            {
                sawThrow = true;
            }
        }

        Volatile.Write(ref stop, 1);
        Assert.True(holder.Join(TimeSpan.FromSeconds(10)));
        Assert.True(sawThrow, "Write never reported the overlapping writer");
    }
}
