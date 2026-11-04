using System.Globalization;
using System.Runtime.InteropServices;
using Jankscope.Core.Hardware;

namespace Jankscope.Platform.Pdh;

/// <summary>
/// Where counter values come from. The real implementation is PDH; tests and other platforms substitute their own. A counter that cannot be
/// read is a <see cref="CounterResult"/> with a reason inside the snapshot, never an exception and never a zero.
/// </summary>
public interface ICounterSource : IDisposable
{
    CounterSnapshot Collect();
}

/// <summary>
/// Reads the counters of <see cref="CounterSpecs"/> through PDH: <c>PdhOpenQuery</c>, <c>PdhAddEnglishCounter</c> (English paths, so the
/// same code runs on any display language), <c>PdhCollectQueryData</c> and <c>PdhGetFormattedCounterArray</c>. Wildcard counters are
/// expanded by PDH at every collection, so processes and adapters that appear later show up. Not thread-safe by design: one collector
/// thread owns it, and calls are serialised by a lock anyway. A rate counter has no value until the second collection; the first
/// collection after creation therefore reports those counters as unavailable with that reason.
/// </summary>
public sealed unsafe class PdhCounterSource : ICounterSource
{
    private const uint MaxBufferBytes = 64 * 1024 * 1024;
    private const int MaxReadAttempts = 4;

    private readonly object _gate = new();
    private readonly List<(CounterSpec Spec, nint Handle, string? AddFailure)> _counters = [];
    private readonly string? _openFailure;
    private nint _query;
    private bool _disposed;

    public PdhCounterSource()
        : this(CounterSpecs.All)
    {
    }

    internal PdhCounterSource(IReadOnlyList<CounterSpec> specs)
    {
        if (nint.Size != 8)
        {
            _openFailure = "the PDH item layout used here is only defined for 64-bit processes";
        }
        else
        {
            var status = NativePdh.PdhOpenQuery(null, 0, out _query);
            if (status != NativePdh.Success)
            {
                _query = 0;
                _openFailure = $"PdhOpenQuery failed: {PdhStatus.Describe(status)}";
            }
        }

        foreach (var spec in specs)
        {
            if (_openFailure is not null)
            {
                _counters.Add((spec, 0, null));
                continue;
            }

            var status = NativePdh.PdhAddEnglishCounter(_query, spec.EnglishPath, 0, out var handle);
            _counters.Add(status == NativePdh.Success ? (spec, handle, null) : (spec, 0, PdhStatus.Describe(status)));
        }
    }

    public CounterSnapshot Collect()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            string? collectFailure = _openFailure;
            if (collectFailure is null)
            {
                var status = NativePdh.PdhCollectQueryData(_query);
                if (status != NativePdh.Success)
                {
                    collectFailure = PdhStatus.Describe(status);
                }
            }

            var results = new List<KeyValuePair<string, CounterResult>>(_counters.Count);
            foreach (var (spec, handle, addFailure) in _counters)
            {
                var result = addFailure is not null
                    ? CounterResult.Unavailable(addFailure)
                    : collectFailure is not null
                        ? CounterResult.Unavailable(collectFailure)
                        : ReadArray(handle, spec);
                results.Add(new KeyValuePair<string, CounterResult>(spec.Id, result));
            }

            return new CounterSnapshot(results);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_query != 0)
            {
                _ = NativePdh.PdhCloseQuery(_query);
                _query = 0;
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A source that is dropped without Dispose still closes its PDH query (review 1a, finding 19); the query handle is not a managed object.</summary>
    ~PdhCounterSource() => Dispose();

    /// <summary>
    /// PDH_FMT_DOUBLE, plus PDH_FMT_NOCAP100 for counters whose value may legitimately exceed 100 (a processor utility of 120 % at turbo speed is real;
    /// without the flag PDH would silently cap it at 100). A separate method so a test can pin the exact flags (review 1a, finding 19, mutation L6).
    /// </summary>
    internal static uint FormatFlagsFor(bool noCap100) => NativePdh.FormatDouble | (noCap100 ? NativePdh.FormatNoCap100 : 0);

    private static CounterResult ReadArray(nint counter, CounterSpec spec)
    {
        var format = FormatFlagsFor(spec.NoCap100);
        uint size = 0;
        uint count = 0;
        var status = NativePdh.PdhGetFormattedCounterArray(counter, format, ref size, ref count, 0);

        for (var attempt = 0; attempt < MaxReadAttempts; attempt++)
        {
            if (status == NativePdh.Success)
            {
                return CounterResult.Of([]); // the counter exists and has no instance right now
            }

            if (status != NativePdh.MoreData)
            {
                return CounterResult.Unavailable(PdhStatus.Describe(status));
            }

            if (size == 0 || size > MaxBufferBytes)
            {
                return CounterResult.Unavailable(string.Create(CultureInfo.InvariantCulture, $"PDH asked for an implausible buffer of {size} bytes"));
            }

            var capacity = size;
            var buffer = NativeMemory.Alloc(capacity);
            try
            {
                status = NativePdh.PdhGetFormattedCounterArray(counter, format, ref size, ref count, (nint)buffer);
                if (status == NativePdh.Success)
                {
                    return ParseItems((byte*)buffer, capacity, count);
                }
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }

        return CounterResult.Unavailable("the set of counter instances kept changing while it was being read");
    }

    // PDH_FMT_COUNTERVALUE_ITEM_W on 64-bit: LPWSTR szName (8), then PDH_FMT_COUNTERVALUE { DWORD CStatus; padding; double }.
    [StructLayout(LayoutKind.Sequential)]
    private struct Item
    {
        public nint Name;
        public uint Status;
        public double Value;
    }

    /// <summary>
    /// Decodes the item array PDH wrote into <paramref name="buffer"/>. Nothing PDH returned is trusted: the item array and every name
    /// must lie inside the allocation, a name must end inside it, and a value counts only with a valid-data status and a finite number.
    /// </summary>
    /// <summary>The layout the reader assumes (see <see cref="Item"/>); a test pins it to the real marshalled size.</summary>
    internal const int ItemSize = 24;

    internal static int MarshalledItemSize => sizeof(Item);

    /// <summary>Pointer-free entry for tests, which may not use unsafe code: <paramref name="buffer"/> is the address of a pinned block of <paramref name="capacity"/> bytes.</summary>
    internal static CounterResult ParseItems(nint buffer, uint capacity, uint count) => ParseItems((byte*)buffer, capacity, count);

    private static CounterResult ParseItems(byte* buffer, uint capacity, uint count)
    {
        if ((ulong)count * (ulong)sizeof(Item) > capacity)
        {
            return CounterResult.Unavailable("PDH reported more items than its buffer can hold");
        }

        var items = (Item*)buffer;
        var result = new List<InstanceReading>((int)count);
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var name = ReadName(buffer, capacity, item.Name);
            var reading = !PdhStatus.CarriesValue(item.Status)
                ? Reading.NotAvailable(PdhStatus.Describe(item.Status))
                : double.IsFinite(item.Value)
                    ? Reading.Of(item.Value)
                    : Reading.NotAvailable("PDH returned a value that is not a finite number");
            result.Add(new InstanceReading(name ?? string.Empty, name is null ? Reading.NotAvailable("the instance name could not be read") : reading));
        }

        return CounterResult.Of(result);
    }

    private static string? ReadName(byte* buffer, uint capacity, nint pointer)
    {
        var start = (byte*)pointer;
        if (start < buffer || start >= buffer + capacity || ((nint)start & 1) != 0)
        {
            return null;
        }

        var chars = (int)((buffer + capacity - start) / sizeof(char));
        var span = new ReadOnlySpan<char>(start, chars);
        var end = span.IndexOf('\0');
        return end < 0 ? null : new string(span[..end]);
    }
}
