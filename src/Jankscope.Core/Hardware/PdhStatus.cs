using System.Globalization;

namespace Jankscope.Core.Hardware;

/// <summary>PDH status and error codes (pdhmsg.h) as plain words, so an unavailable counter always says why.</summary>
public static class PdhStatus
{
    public const uint ValidData = 0x00000000;
    public const uint NewData = 0x00000001;
    public const uint CalcNegativeDenominator = 0x800007D6;
    public const uint InvalidData = 0xC0000BBA;

    /// <summary>The function-level twin of <see cref="InvalidData"/>: what the array call returns for a single-instance rate counter before its second sample.</summary>
    public const uint InvalidDataReturned = 0xC0000BC6;

    /// <summary>True when a formatted counter value with this status is a measurement (valid or new data).</summary>
    public static bool CarriesValue(uint status) => status is ValidData or NewData;

    /// <summary>True for the statuses a rate counter reports until it has two samples.</summary>
    public static bool NeedsSecondSample(uint status) => status is CalcNegativeDenominator or InvalidData or InvalidDataReturned;

    public static string Describe(uint status) => status switch
    {
        0x00000000 => "valid data (PDH_CSTATUS_VALID_DATA)",
        0x00000001 => "new data (PDH_CSTATUS_NEW_DATA)",
        0x800007D0 => "the machine does not exist (PDH_CSTATUS_NO_MACHINE)",
        0x800007D1 => "the counter instance does not exist (PDH_CSTATUS_NO_INSTANCE)",
        0x800007D2 => "the buffer was too small (PDH_MORE_DATA)",
        0x800007D5 => "no data was collected (PDH_NO_DATA)",
        0x800007D6 => "needs a second sample (PDH_CALC_NEGATIVE_DENOMINATOR)",
        0x800007D7 => "the sample time base went backwards (PDH_CALC_NEGATIVE_TIMEBASE)",
        0x800007D8 => "the computed value was negative (PDH_CALC_NEGATIVE_VALUE)",
        0xC0000BB8 => "the performance object does not exist on this system (PDH_CSTATUS_NO_OBJECT)",
        0xC0000BB9 => "the counter does not exist on this system (PDH_CSTATUS_NO_COUNTER)",
        0xC0000BBA => "no valid data yet: a rate needs a second sample, or the instance vanished (PDH_INVALID_DATA)",
        0xC0000BBB => "PDH could not allocate memory (PDH_MEMORY_ALLOCATION_FAILURE)",
        0xC0000BBC => "invalid PDH handle (PDH_INVALID_HANDLE)",
        0xC0000BBD => "invalid argument (PDH_INVALID_ARGUMENT)",
        0xC0000BBE => "PDH function not found (PDH_FUNCTION_NOT_FOUND)",
        0xC0000BBF => "the counter has no name (PDH_CSTATUS_NO_COUNTERNAME)",
        0xC0000BC0 => "the counter name is malformed (PDH_CSTATUS_BAD_COUNTERNAME)",
        0xC0000BC1 => "PDH rejected the buffer (PDH_INVALID_BUFFER)",
        0xC0000BC2 => "the buffer was too small (PDH_INSUFFICIENT_BUFFER)",
        0xC0000BC4 => "the counter path is invalid (PDH_INVALID_PATH)",
        0xC0000BC5 => "the counter instance is invalid (PDH_INVALID_INSTANCE)",
        0xC0000BC6 => "no valid data yet: a rate needs a second sample, or the counter returned invalid data (PDH_INVALID_DATA)",
        _ => string.Create(CultureInfo.InvariantCulture, $"PDH status 0x{status:X8}"),
    };
}
