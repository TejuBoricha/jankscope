using System.Runtime.InteropServices;

namespace Jankscope.Platform.Pdh;

/// <summary>
/// The Performance Data Helper entry points the sampler uses (pdh.h, documented). Only the English-path variant adds counters, so a
/// counter path resolves on any display language (spec section 3.2). Nothing here opens another process.
/// </summary>
internal static partial class NativePdh
{
    internal const uint FormatDouble = 0x00000200;
    internal const uint FormatNoCap100 = 0x00008000;

    internal const uint Success = 0;
    internal const uint MoreData = 0x800007D2;

    /// <summary>A null data source means the live system.</summary>
    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint PdhAddEnglishCounter(nint query, string fullCounterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll", EntryPoint = "PdhCollectQueryData")]
    internal static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    internal static partial uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, ref uint itemCount, nint itemBuffer);

    [LibraryImport("pdh.dll", EntryPoint = "PdhCloseQuery")]
    internal static partial uint PdhCloseQuery(nint query);
}
