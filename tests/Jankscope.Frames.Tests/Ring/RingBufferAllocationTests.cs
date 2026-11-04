using Jankscope.Frames.Ring;
using Xunit.Abstractions;

namespace Jankscope.Frames.Tests.Ring;

public class RingBufferAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void The_write_path_allocates_nothing_after_warm_up()
    {
        var ring = new RingBuffer<FrameSample>(1024);
        var sample = new FrameSample(0, 16.7, 16.7, 16.6, 5.0, 9.0, "Hardware: Independent Flip", "game.exe", 4242);

        for (var i = 0; i < 5_000; i++) // warm-up: JIT, tiering, and a few wrap-arounds
        {
            ring.Write(sample);
        }

        var allocated = AllocationProbe.MinimumBytes(() =>
        {
            for (var i = 0; i < 1_000_000; i++)
            {
                ring.Write(sample);
            }
        }, out var deltas);

        output.WriteLine("allocated per attempt: " + string.Join(", ", deltas));
        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void TryWrite_and_a_value_type_without_references_also_allocate_nothing()
    {
        var ring = new RingBuffer<long>(512);
        for (var i = 0; i < 5_000; i++)
        {
            ring.TryWrite(i);
        }

        var allocated = AllocationProbe.MinimumBytes(() =>
        {
            for (var i = 0; i < 1_000_000; i++)
            {
                ring.TryWrite(i);
            }
        }, out var deltas);

        output.WriteLine("allocated per attempt: " + string.Join(", ", deltas));
        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void Copying_into_a_caller_supplied_span_allocates_nothing()
    {
        var ring = new RingBuffer<FrameSample>(256);
        for (var i = 0; i < 1_000; i++)
        {
            ring.Write(new FrameSample(i, i * 4.0, 4.0));
        }

        var destination = new FrameSample[128];
        for (var i = 0; i < 1_000; i++) // warm-up
        {
            ring.CopyLatest(destination, out _);
        }

        long checksum = 0;
        var allocated = AllocationProbe.MinimumBytes(() =>
        {
            for (var i = 0; i < 100_000; i++)
            {
                checksum += ring.CopyLatest(destination, out var first) + first;
            }
        }, out var deltas);

        output.WriteLine("allocated per attempt: " + string.Join(", ", deltas));
        Assert.Equal(0L, allocated);
        Assert.True(checksum > 0);
    }
}
