using Jankscope.Frames.Ring;

namespace Jankscope.Frames.Tests.Ring;

public class RingBufferTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Capacity_must_be_positive(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(capacity));

    [Fact]
    public void A_new_buffer_is_empty_and_every_read_returns_nothing()
    {
        var ring = new RingBuffer<int>(4);

        Assert.Equal(4, ring.Capacity);
        Assert.Equal(0, ring.Count);
        Assert.Equal(0L, ring.TotalWritten);
        Assert.Equal(0L, ring.TotalEvicted);
        Assert.Empty(ring.Snapshot());
        Assert.Empty(ring.SnapshotLatest(3));
        Assert.Equal(0, ring.CopyLatest(new int[3], out var first));
        Assert.Equal(0L, first);
        Assert.Equal(0, ring.CopyLatest(Span<int>.Empty));
    }

    [Fact]
    public void Items_come_back_oldest_first_before_the_buffer_is_full()
    {
        var ring = new RingBuffer<int>(5);
        ring.Write(10);
        ring.Write(20);
        ring.Write(30);

        Assert.Equal(3, ring.Count);
        Assert.Equal(3L, ring.TotalWritten);
        Assert.Equal(0L, ring.TotalEvicted);
        Assert.Equal([10, 20, 30], ring.Snapshot());

        var destination = new int[8];
        Assert.Equal(3, ring.CopyLatest(destination, out var first));
        Assert.Equal(0L, first);
        Assert.Equal([10, 20, 30], destination[..3]);
    }

    [Fact]
    public void The_oldest_item_is_evicted_by_the_write_after_the_buffer_is_full()
    {
        var ring = new RingBuffer<int>(3);
        ring.Write(1);
        ring.Write(2);
        ring.Write(3);
        Assert.Equal(0L, ring.TotalEvicted); // exactly full: nothing lost yet
        Assert.Equal([1, 2, 3], ring.Snapshot());

        ring.Write(4);

        Assert.Equal(1L, ring.TotalEvicted);
        Assert.Equal(3, ring.Count);
        Assert.Equal(4L, ring.TotalWritten);
        Assert.Equal([2, 3, 4], ring.Snapshot());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(64)]
    public void Snapshot_always_equals_the_newest_capacity_items_in_write_order(int capacity)
    {
        var ring = new RingBuffer<int>(capacity);
        var written = new List<int>();

        for (var i = 0; i < (capacity * 3) + 2; i++)
        {
            ring.Write(i * 7);
            written.Add(i * 7);

            var expected = written.Skip(Math.Max(0, written.Count - capacity)).ToArray();
            Assert.Equal(expected, ring.Snapshot());
            Assert.Equal(expected.Length, ring.Count);
            Assert.Equal(written.Count, ring.TotalWritten);
            Assert.Equal(Math.Max(0L, written.Count - capacity), ring.TotalEvicted);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    public void SnapshotLatest_returns_the_newest_N_oldest_first_and_reports_where_they_start(int wanted)
    {
        var ring = new RingBuffer<int>(8);
        for (var i = 0; i < 21; i++)
        {
            ring.Write(i); // newest 8 are 13..20; the sequence number of item i is i
        }

        var items = ring.SnapshotLatest(wanted, out var firstSequence);

        var expectedCount = Math.Min(wanted, 8);
        Assert.Equal(expectedCount, items.Length);
        Assert.Equal(21L - expectedCount, firstSequence);
        for (var k = 0; k < items.Length; k++)
        {
            Assert.Equal((int)firstSequence + k, items[k]);
        }
    }

    [Fact]
    public void CopyLatest_into_a_short_destination_fills_it_with_the_newest_items_in_order()
    {
        var ring = new RingBuffer<int>(6);
        for (var i = 1; i <= 10; i++)
        {
            ring.Write(i);
        }

        var destination = new int[4];
        var copied = ring.CopyLatest(destination, out var first);

        Assert.Equal(4, copied);
        Assert.Equal(6L, first);
        Assert.Equal([7, 8, 9, 10], destination);
    }

    [Fact]
    public void SnapshotLatest_of_zero_is_empty_and_a_negative_count_is_rejected()
    {
        var ring = new RingBuffer<int>(4);
        ring.Write(1);

        Assert.Empty(ring.SnapshotLatest(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.SnapshotLatest(-1));
    }

    [Fact]
    public void TryWrite_stores_the_item_and_reports_success_when_no_other_writer_is_active()
    {
        var ring = new RingBuffer<int>(2);

        Assert.True(ring.TryWrite(5));
        Assert.True(ring.TryWrite(6));
        Assert.True(ring.TryWrite(7));

        Assert.Equal([6, 7], ring.Snapshot());
    }

    [Fact]
    public void Structs_that_carry_references_round_trip_unchanged()
    {
        var ring = new RingBuffer<FrameSample>(3);
        var a = new FrameSample(0, 16.7, 16.7, 16.6, 5.0, 9.0, "Hardware: Independent Flip", "game.exe", 4242);
        var b = new FrameSample(1, 33.4, 16.7);
        ring.Write(a);
        ring.Write(b);

        var snapshot = ring.Snapshot();

        Assert.Equal([a, b], snapshot);
        Assert.Null(snapshot[1].DisplayedFrameTimeMs); // a missing value stays missing, never 0
    }

    [Fact]
    public void A_lapped_snapshot_returns_only_the_items_it_can_vouch_for()
    {
        // The hook runs after the items were copied and before they are validated; writing there is a deterministic
        // stand-in for a concurrent writer that overwrites slots during the copy.
        var cases = new (int Capacity, int WrittenFirst, int WrittenDuringCopy, int ExpectedCount, long ExpectedFirst)[]
        {
            (4, 4, 0, 4, 0),   // nobody wrote during the copy: everything intact
            (4, 4, 1, 3, 1),   // item 4 reuses the slot of item 0: only item 0 is suspect
            (4, 4, 3, 1, 3),   // items 4..6 reuse the slots of items 0..2
            (4, 4, 4, 0, 4),   // the whole buffer was overwritten: nothing can be vouched for
            (4, 4, 9, 0, 4),   // lapped more than once
            (4, 10, 2, 2, 8),  // wrapped buffer: items 6..9 were copied, 10 and 11 reuse the slots of 6 and 7
            (1, 1, 1, 0, 1),   // capacity 1: the only slot is reused
        };

        foreach (var c in cases)
        {
            var ring = new HookedRing(c.Capacity);
            for (var i = 0; i < c.WrittenFirst; i++)
            {
                ring.Write(i);
            }

            ring.ToWriteDuringCopy = c.WrittenDuringCopy;
            var destination = new int[c.Capacity];

            var copied = ring.CopyLatest(destination, out var first);

            Assert.Equal(c.ExpectedCount, copied);
            Assert.Equal(c.ExpectedFirst, first);
            for (var k = 0; k < copied; k++)
            {
                Assert.Equal((int)first + k, destination[k]); // what is returned is intact and contiguous
            }
        }
    }

    private sealed class HookedRing(int capacity) : RingBuffer<int>(capacity)
    {
        public int ToWriteDuringCopy;
        private int _next = 1_000_000;

        protected override void OnCopiedBeforeValidation()
        {
            for (var i = 0; i < ToWriteDuringCopy; i++)
            {
                Write(_next++);
            }
        }
    }
}
