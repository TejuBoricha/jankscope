namespace Jankscope.Frames.Detection;

/// <summary>
/// A first-in-first-out window of (time, value) pairs that gives the median in O(1) and supports insertion and eviction of the oldest
/// pair in O(log n). Two indexed heaps hold the lower half (a max-heap) and the upper half (a min-heap); every window entry remembers
/// which heap it is in and where, so the entry that falls out of the window (the oldest by time, not by value) can be removed from the
/// middle of its heap. After the arrays have grown to the largest window seen, no operation allocates.
/// </summary>
internal sealed class SlidingMedian
{
    private const int InitialCapacity = 64;

    // Per-slot data. A slot id is stable for as long as its entry is in the window.
    private double[] _value = new double[InitialCapacity];
    private double[] _time = new double[InitialCapacity];
    private int[] _heapIndex = new int[InitialCapacity]; // position of the slot inside its heap
    private bool[] _inLow = new bool[InitialCapacity];

    private int[] _free = new int[InitialCapacity];      // stack of unused slot ids
    private int _freeCount;
    private int[] _queue = new int[InitialCapacity];     // slot ids, oldest first (circular)
    private int _queueHead;
    private int _count;

    private readonly Heap _low = new(sign: -1.0, isLow: true);   // max-heap on value: key = -value in a min-heap
    private readonly Heap _high = new(sign: 1.0, isLow: false);  // min-heap on value

    public SlidingMedian()
    {
        FillFreeList(0, InitialCapacity);
    }

    /// <summary>Number of entries in the window.</summary>
    public int Count => _count;

    public void Clear()
    {
        _low.Count = 0;
        _high.Count = 0;
        _queueHead = 0;
        _count = 0;
        _freeCount = 0;
        FillFreeList(0, _value.Length);
    }

    /// <summary>Appends an entry as the newest.</summary>
    public void Add(double time, double value)
    {
        if (_freeCount == 0)
        {
            Grow();
        }

        var slot = _free[--_freeCount];
        _value[slot] = value;
        _time[slot] = time;

        var tail = _queueHead + _count;
        if (tail >= _queue.Length)
        {
            tail -= _queue.Length;
        }

        _queue[tail] = slot;
        _count++;

        var target = _low.Count == 0 || value <= _value[_low.Items[0]] ? _low : _high;
        Push(target, slot);
        Rebalance();
    }

    /// <summary>Removes, oldest first, every entry whose time is below <paramref name="cutoff"/>.</summary>
    public void EvictOlderThan(double cutoff)
    {
        while (_count > 0)
        {
            var slot = _queue[_queueHead];
            if (_time[slot] >= cutoff)
            {
                return;
            }

            _queueHead = _queueHead + 1 == _queue.Length ? 0 : _queueHead + 1;
            _count--;
            RemoveAt(_inLow[slot] ? _low : _high, _heapIndex[slot]);
            _free[_freeCount++] = slot;
            Rebalance();
        }
    }

    /// <summary>Median of the values in the window (the mean of the two middle values for an even count); NaN when empty.</summary>
    public double Median()
    {
        if (_count == 0)
        {
            return double.NaN;
        }

        var lowTop = _value[_low.Items[0]];
        if (_low.Count > _high.Count)
        {
            return lowTop;
        }

        var highTop = _value[_high.Items[0]];
        return lowTop + ((highTop - lowTop) * 0.5); // lowTop <= highTop; written this way it cannot overflow
    }

    private void FillFreeList(int from, int to)
    {
        for (var id = to - 1; id >= from; id--)
        {
            _free[_freeCount++] = id;
        }
    }

    private void Grow()
    {
        var oldCapacity = _value.Length;
        var newCapacity = oldCapacity * 2;

        Array.Resize(ref _value, newCapacity);
        Array.Resize(ref _time, newCapacity);
        Array.Resize(ref _heapIndex, newCapacity);
        Array.Resize(ref _inLow, newCapacity);
        Array.Resize(ref _free, newCapacity);
        Array.Resize(ref _low.Items, newCapacity);
        Array.Resize(ref _high.Items, newCapacity);

        var queue = new int[newCapacity];
        for (var i = 0; i < _count; i++)
        {
            var from = _queueHead + i;
            queue[i] = _queue[from >= oldCapacity ? from - oldCapacity : from];
        }

        _queue = queue;
        _queueHead = 0;

        FillFreeList(oldCapacity, newCapacity); // the free stack was empty: it now holds exactly the new ids
    }

    private void Rebalance()
    {
        while (_low.Count > _high.Count + 1)
        {
            MoveTop(_low, _high);
        }

        while (_high.Count > _low.Count)
        {
            MoveTop(_high, _low);
        }
    }

    private void MoveTop(Heap from, Heap to)
    {
        var slot = from.Items[0];
        RemoveAt(from, 0);
        Push(to, slot);
    }

    private double Key(Heap heap, int slot) => heap.Sign * _value[slot];

    private void Place(Heap heap, int index, int slot)
    {
        heap.Items[index] = slot;
        _heapIndex[slot] = index;
        _inLow[slot] = heap.IsLow;
    }

    private void Push(Heap heap, int slot)
    {
        Place(heap, heap.Count, slot);
        heap.Count++;
        SiftUp(heap, heap.Count - 1);
    }

    private void RemoveAt(Heap heap, int index)
    {
        heap.Count--;
        var last = heap.Items[heap.Count];
        if (index == heap.Count)
        {
            return; // the removed entry was the last one
        }

        Place(heap, index, last);
        SiftUp(heap, index);
        SiftDown(heap, _heapIndex[last]);
    }

    private void SiftUp(Heap heap, int index)
    {
        var slot = heap.Items[index];
        var key = Key(heap, slot);
        while (index > 0)
        {
            var parent = (index - 1) >> 1;
            var parentSlot = heap.Items[parent];
            if (Key(heap, parentSlot) <= key)
            {
                break;
            }

            Place(heap, index, parentSlot);
            index = parent;
        }

        Place(heap, index, slot);
    }

    private void SiftDown(Heap heap, int index)
    {
        var slot = heap.Items[index];
        var key = Key(heap, slot);
        var count = heap.Count;
        while (true)
        {
            var child = (index * 2) + 1;
            if (child >= count)
            {
                break;
            }

            if (child + 1 < count && Key(heap, heap.Items[child + 1]) < Key(heap, heap.Items[child]))
            {
                child++;
            }

            if (Key(heap, heap.Items[child]) >= key)
            {
                break;
            }

            Place(heap, index, heap.Items[child]);
            index = child;
        }

        Place(heap, index, slot);
    }

    private sealed class Heap(double sign, bool isLow)
    {
        public int[] Items = new int[InitialCapacity];
        public int Count;
        public readonly double Sign = sign;
        public readonly bool IsLow = isLow;
    }
}
