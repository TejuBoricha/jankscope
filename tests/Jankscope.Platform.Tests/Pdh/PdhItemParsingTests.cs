using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Jankscope.Core.Hardware;
using Jankscope.Platform.Pdh;

namespace Jankscope.Platform.Tests.Pdh;

/// <summary>
/// The trust boundary of the PDH reader: it decodes a buffer the operating system filled, and nothing in it is believed. These tests build
/// that buffer byte for byte (PDH_FMT_COUNTERVALUE_ITEM_W, 64-bit: name pointer 8, status 4, padding 4, double 8 = 24 bytes) in pinned memory.
/// </summary>
public class PdhItemParsingTests
{
    private const uint ValidData = 0;
    private const uint NewData = 1;
    private const uint InvalidData = 0xC0000BBA;

    private sealed class Block : IDisposable
    {
        private readonly byte[] _bytes;
        private readonly GCHandle _pin;

        public Block(int size)
        {
            _bytes = new byte[size];
            _pin = GCHandle.Alloc(_bytes, GCHandleType.Pinned);
        }

        public nint Address => _pin.AddrOfPinnedObject();

        public uint Capacity => (uint)_bytes.Length;

        public Span<byte> Bytes => _bytes;

        public void Item(int index, nint namePointer, uint status, double value)
        {
            var at = Bytes[(index * PdhCounterSource.ItemSize)..];
            BinaryPrimitives.WriteInt64LittleEndian(at, namePointer);
            BinaryPrimitives.WriteUInt32LittleEndian(at[8..], status);
            BinaryPrimitives.WriteDoubleLittleEndian(at[16..], value);
        }

        public nint WriteName(int offset, string name, bool terminate = true)
        {
            var bytes = Encoding.Unicode.GetBytes(name);
            bytes.CopyTo(Bytes[offset..]);
            if (terminate)
            {
                Bytes[(offset + bytes.Length)..][..2].Clear();
            }

            return Address + offset;
        }

        public void Dispose() => _pin.Free();
    }

    [Fact]
    public void Valid_items_are_decoded_with_their_names()
    {
        using var block = new Block(2 * PdhCounterSource.ItemSize + 64);
        var firstName = block.WriteName(2 * PdhCounterSource.ItemSize, "0,0");
        var secondName = block.WriteName(2 * PdhCounterSource.ItemSize + 16, "_Total");
        block.Item(0, firstName, ValidData, 12.5);
        block.Item(1, secondName, NewData, 99);

        var result = PdhCounterSource.ParseItems(block.Address, block.Capacity, 2);

        Assert.Null(result.UnavailableReason);
        Assert.Equal([new InstanceReading("0,0", Reading.Of(12.5)), new InstanceReading("_Total", Reading.Of(99))], result.Instances);
    }

    [Fact]
    public void An_item_with_invalid_data_status_is_unavailable_even_though_its_value_field_holds_a_zero()
    {
        using var block = new Block(PdhCounterSource.ItemSize + 32);
        var name = block.WriteName(PdhCounterSource.ItemSize, "0 C:");
        block.Item(0, name, InvalidData, 0.0);

        var item = Assert.Single(PdhCounterSource.ParseItems(block.Address, block.Capacity, 1).Instances);

        Assert.Equal("0 C:", item.Instance);
        Assert.False(item.Value.IsAvailable);
        Assert.Contains("PDH_INVALID_DATA", item.Value.UnavailableReason, StringComparison.Ordinal);
        Assert.Contains("second sample", item.Value.UnavailableReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_value_that_is_not_a_finite_number_is_never_a_reading(double value)
    {
        using var block = new Block(PdhCounterSource.ItemSize + 32);
        block.Item(0, block.WriteName(PdhCounterSource.ItemSize, "x"), ValidData, value);

        var item = Assert.Single(PdhCounterSource.ParseItems(block.Address, block.Capacity, 1).Instances);

        Assert.False(item.Value.IsAvailable);
        Assert.Contains("finite", item.Value.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_pointer_outside_the_buffer_gives_an_unnamed_unavailable_instance_and_does_not_read_there()
    {
        using var block = new Block(PdhCounterSource.ItemSize * 3 + 64);
        block.Item(0, block.Address - 64, ValidData, 1);                       // before the allocation
        block.Item(1, block.Address + (nint)block.Capacity + 64, ValidData, 2);      // after it
        block.Item(2, 0, ValidData, 3);                                        // null

        var result = PdhCounterSource.ParseItems(block.Address, block.Capacity, 3);

        Assert.Equal(3, result.Instances.Count);
        Assert.All(result.Instances, i =>
        {
            Assert.Equal(string.Empty, i.Instance);
            Assert.False(i.Value.IsAvailable);
            Assert.Contains("name could not be read", i.Value.UnavailableReason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_misaligned_name_pointer_is_rejected()
    {
        using var block = new Block(PdhCounterSource.ItemSize + 32);
        var name = block.WriteName(PdhCounterSource.ItemSize, "ab");
        block.Item(0, name + 1, ValidData, 1);

        var item = Assert.Single(PdhCounterSource.ParseItems(block.Address, block.Capacity, 1).Instances);

        Assert.False(item.Value.IsAvailable);
    }

    [Fact]
    public void A_name_without_a_terminator_before_the_end_of_the_buffer_is_not_read_past_it()
    {
        const int Tail = 8; // four characters, none of them NUL
        using var block = new Block(PdhCounterSource.ItemSize + Tail);
        var name = block.WriteName(PdhCounterSource.ItemSize, "abcd", terminate: false);
        block.Item(0, name, ValidData, 1);

        var item = Assert.Single(PdhCounterSource.ParseItems(block.Address, block.Capacity, 1).Instances);

        Assert.Equal(string.Empty, item.Instance);
        Assert.False(item.Value.IsAvailable);
    }

    [Fact]
    public void A_count_larger_than_the_buffer_can_hold_makes_the_whole_counter_unavailable()
    {
        using var block = new Block(PdhCounterSource.ItemSize * 2);

        var result = PdhCounterSource.ParseItems(block.Address, block.Capacity, 3);

        Assert.Empty(result.Instances);
        Assert.Contains("more items", result.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_huge_count_cannot_overflow_the_size_check()
    {
        using var block = new Block(PdhCounterSource.ItemSize);

        var result = PdhCounterSource.ParseItems(block.Address, block.Capacity, uint.MaxValue);

        Assert.NotNull(result.UnavailableReason);
    }

    [Fact]
    public void Zero_items_is_a_counter_that_exists_with_no_instance_not_an_unavailable_counter()
    {
        using var block = new Block(PdhCounterSource.ItemSize);

        var result = PdhCounterSource.ParseItems(block.Address, block.Capacity, 0);

        Assert.Null(result.UnavailableReason);
        Assert.Empty(result.Instances);
    }

    [Fact]
    public void The_item_size_constant_matches_the_marshalled_layout_the_reader_assumes()
    {
        Assert.Equal(PdhCounterSource.ItemSize, PdhCounterSource.MarshalledItemSize);
        Assert.Equal(24, PdhCounterSource.ItemSize);
        Assert.Equal(8, nint.Size);
    }
}
