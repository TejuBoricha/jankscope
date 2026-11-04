namespace Jankscope.Frames.Ring;

/// <summary>
/// Fixed-capacity ring of structs: ONE writer, MANY concurrent readers, no locks. When full, each write overwrites the oldest item.
/// Spec section 6.3 (fixed-size in-memory ring buffers per signal) and 7.3 (rolling pre-roll buffer).
/// </summary>
/// <remarks>
/// <para>
/// Every item has a <b>sequence number</b>: the 0-based count of writes before it. Item <c>n</c> lives in slot <c>n % Capacity</c>.
/// </para>
/// <para>
/// Consistency (a per-slot seqlock). The writer keeps two counters. <c>claimed</c> is raised, with a full fence, BEFORE a slot is
/// touched; <c>committed</c> (<see cref="TotalWritten"/>) is raised, with release semantics, AFTER the slot is complete. A reader takes
/// <c>end = committed</c>, copies the items it wants out of the slots, fences, and re-reads <c>claimed</c>. A write that started
/// before that second read may have changed the slot of item <c>j</c> only if <c>j + Capacity &lt; claimed</c>; such items are cut from
/// the front of the copy. What is returned is therefore always a contiguous run of intact items ending at <c>end</c>, never a mixture of
/// two writes. If the writer laps the whole ring during a copy the result is empty (the caller can simply copy again); readers never block
/// the writer and the writer never blocks readers.
/// </para>
/// <para>The write path performs no allocation and takes no lock. Reads into a caller-supplied span do not allocate either.</para>
/// </remarks>
/// <typeparam name="T">The element type. A struct, so a slot is a value and a copy never aliases a live object.</typeparam>
public class RingBuffer<T> where T : struct
{
    private readonly T[] _slots;
    private int _nextSlot;          // writer only
    private long _claimed;          // writes started (writer raises it before touching a slot)
    private long _committed;        // writes completed (writer raises it after the slot is complete)
    private int _writerActive;      // 0/1: detects a second concurrent writer, which would corrupt the counters

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _slots = new T[capacity];
        Capacity = capacity;
    }

    /// <summary>Maximum number of items held.</summary>
    public int Capacity { get; }

    /// <summary>Number of completed writes since creation. Item <c>n</c> has sequence number <c>n</c>.</summary>
    public long TotalWritten => Volatile.Read(ref _committed);

    /// <summary>Number of items currently held: <c>min(TotalWritten, Capacity)</c>.</summary>
    public int Count => (int)Math.Min(TotalWritten, Capacity);

    /// <summary>Number of items overwritten so far (they were written and then lost to newer data).</summary>
    public long TotalEvicted => Math.Max(0L, TotalWritten - Capacity);

    /// <summary>
    /// Appends an item, overwriting the oldest when full. Throws <see cref="InvalidOperationException"/> if another thread is
    /// writing at the same moment: the buffer has exactly one writer.
    /// </summary>
    public void Write(in T item)
    {
        if (!TryWrite(in item))
        {
            throw new InvalidOperationException("RingBuffer has a single writer; another thread was writing at the same moment.");
        }
    }

    /// <summary>
    /// Appends an item, overwriting the oldest when full. Returns false, storing nothing, only if another thread is writing at the
    /// same moment (a contract violation that would otherwise corrupt the counters). Never throws.
    /// </summary>
    public bool TryWrite(in T item)
    {
        if (Interlocked.CompareExchange(ref _writerActive, 1, 0) != 0)
        {
            return false;
        }

        var index = _claimed;
        Volatile.Write(ref _claimed, index + 1);
        Interlocked.MemoryBarrier(); // the claim must be visible before any byte of the slot changes

        _slots[_nextSlot] = item;
        _nextSlot = _nextSlot + 1 == _slots.Length ? 0 : _nextSlot + 1;

        Volatile.Write(ref _committed, index + 1); // release: the slot's bytes are visible before the count is
        Volatile.Write(ref _writerActive, 0);
        return true;
    }

    /// <summary>
    /// Copies the newest items, oldest first, into <paramref name="destination"/> (at most its length and at most <see cref="Capacity"/>)
    /// and returns how many were copied. Only items verified intact are returned; see the remarks on the type.
    /// </summary>
    public int CopyLatest(Span<T> destination) => CopyLatest(destination, out _);

    /// <summary>
    /// As <see cref="CopyLatest(Span{T})"/>, and reports in <paramref name="firstSequence"/> the sequence number of the first
    /// returned item (so the caller knows exactly which part of the stream it holds). When nothing is returned, it is the sequence number
    /// just after the newest completed item.
    /// </summary>
    public int CopyLatest(Span<T> destination, out long firstSequence)
    {
        var end = Volatile.Read(ref _committed); // acquire: every item below `end` is fully visible
        var wanted = (int)Math.Min(Math.Min(destination.Length, Capacity), end);
        if (wanted == 0)
        {
            firstSequence = end;
            return 0;
        }

        var start = end - wanted;
        CopyRange(start, wanted, destination);

        OnCopiedBeforeValidation();

        Interlocked.MemoryBarrier(); // the loads above must not move past the load below
        var claimed = Volatile.Read(ref _claimed);

        // Item j may have been overwritten (or be mid-overwrite) iff item j + Capacity was claimed, i.e. j < claimed - Capacity.
        var firstIntact = Math.Max(start, claimed - Capacity);
        if (firstIntact >= end)
        {
            firstSequence = end;
            return 0;
        }

        var drop = (int)(firstIntact - start);
        if (drop > 0)
        {
            destination.Slice(drop, wanted - drop).CopyTo(destination); // overlapping move toward the front is safe for Span
        }

        firstSequence = firstIntact;
        return wanted - drop;
    }

    /// <summary>All items currently held, oldest first. Allocates the result array (a read-side convenience, not the hot path).</summary>
    public T[] Snapshot() => SnapshotLatest(Capacity, out _);

    /// <summary>The newest <paramref name="count"/> items (fewer if fewer exist), oldest first. Allocates the result array.</summary>
    public T[] SnapshotLatest(int count) => SnapshotLatest(count, out _);

    /// <summary>As <see cref="SnapshotLatest(int)"/>, and reports the sequence number of the first returned item.</summary>
    public T[] SnapshotLatest(int count, out long firstSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var buffer = new T[Math.Min(count, Capacity)];
        var copied = CopyLatest(buffer, out firstSequence);
        if (copied == buffer.Length)
        {
            return buffer;
        }

        return buffer.AsSpan(0, copied).ToArray();
    }

    /// <summary>
    /// Called by readers after the items were copied and before they are validated. It exists so a test can write from this thread and
    /// deterministically reproduce "the writer overwrote slots while I was copying". Does nothing by default.
    /// </summary>
    protected virtual void OnCopiedBeforeValidation()
    {
    }

    private void CopyRange(long start, int count, Span<T> destination)
    {
        var slot = (int)(start % Capacity);
        var firstPart = Math.Min(count, Capacity - slot);
        _slots.AsSpan(slot, firstPart).CopyTo(destination);
        if (firstPart < count)
        {
            _slots.AsSpan(0, count - firstPart).CopyTo(destination[firstPart..]);
        }
    }
}
