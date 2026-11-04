using System.Globalization;
using System.Runtime.InteropServices;

namespace Jankscope.Platform.Hardware;

/// <summary>
/// Reads the processor records with GetLogicalProcessorInformationEx(RelationAll): one call to learn the size, then fills a buffer of
/// exactly that size, retrying if the size changed in between (documented ERROR_INSUFFICIENT_BUFFER protocol). The record layout is
/// the 64-bit one, so other process architectures report that they are unsupported instead of guessing.
/// </summary>
internal sealed class WindowsProcessorInfoSource : IProcessorInfoSource
{
    private const int MaxAttempts = 4;
    private const uint MaxPlausibleBytes = 16 * 1024 * 1024;

    public ProcessorInfoReading Read()
    {
        var active = (int)NativeHardware.GetActiveProcessorCount(NativeHardware.AllProcessorGroups);
        int? osCount = active > 0 ? active : null;

        if (nint.Size != 8)
        {
            return new ProcessorInfoReading(null, osCount, "the processor record layout is only defined here for 64-bit processes");
        }

        uint length = 0;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var buffer = length == 0 ? [] : new byte[length];
            if (NativeHardware.GetLogicalProcessorInformationEx(NativeHardware.RelationAll, buffer, ref length))
            {
                return new ProcessorInfoReading(buffer.AsSpan(0, (int)Math.Min(length, (uint)buffer.Length)).ToArray(), osCount, null);
            }

            var error = Marshal.GetLastPInvokeError();
            if (error != NativeHardware.ErrorInsufficientBuffer)
            {
                return new ProcessorInfoReading(null, osCount, string.Create(CultureInfo.InvariantCulture, $"GetLogicalProcessorInformationEx failed with Win32 error {error}"));
            }

            if (length == 0 || length > MaxPlausibleBytes)
            {
                return new ProcessorInfoReading(null, osCount, string.Create(CultureInfo.InvariantCulture, $"GetLogicalProcessorInformationEx asked for an implausible {length} bytes"));
            }
        }

        return new ProcessorInfoReading(null, osCount, "the processor information kept changing size between calls");
    }
}
