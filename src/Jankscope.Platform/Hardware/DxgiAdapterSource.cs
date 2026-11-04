using System.Globalization;
using System.Runtime.InteropServices;
using Jankscope.Core.Hardware;
using SharpGen.Runtime;
using Vortice.DXGI;

namespace Jankscope.Platform.Hardware;

/// <summary>
/// Lists graphics adapters through DXGI (documented): <c>CreateDXGIFactory1</c>, then <c>EnumAdapters1</c> and the adapter description.
/// That is all. No device is created and no output is enumerated, because creating a device for a discrete adapter can bring a sleeping
/// GPU out of its low-power state and the product must not (spec section 7.2). Which adapter drives which display comes from the display
/// configuration API instead. A test pins this: this file may not name the device-creating or output-enumerating calls.
/// </summary>
internal sealed class DxgiAdapterSource : IGpuAdapterSource
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const int MaxAdapters = 64;

    public AdapterEnumeration Enumerate()
    {
        try
        {
            // The factory comes from our own System32-only import; the binding package only wraps the pointer (see NativeHardware.CreateDxgiFactory1).
            var iid = typeof(IDXGIFactory1).GUID;
            var created = NativeHardware.CreateDxgiFactory1(in iid, out var raw);
            if (created < 0 || raw == 0)
            {
                return new AdapterEnumeration([], string.Create(CultureInfo.InvariantCulture, $"the DXGI factory could not be created (HRESULT 0x{created:X8})"));
            }

            using (var factory = new IDXGIFactory1(raw))
            {
                var adapters = new List<GpuAdapterInfo>();
                for (uint index = 0; index < MaxAdapters; index++)
                {
                    var result = factory.EnumAdapters1(index, out var adapter);
                    if (result.Code == DxgiErrorNotFound)
                    {
                        break;
                    }

                    if (result.Failure || adapter is null)
                    {
                        return new AdapterEnumeration(adapters, string.Create(CultureInfo.InvariantCulture, $"adapter enumeration failed (HRESULT 0x{result.Code:X8})"));
                    }

                    using (adapter)
                    {
                        var d = adapter.Description1;
                        adapters.Add(new GpuAdapterInfo(
                            Luid.FromInt64(((long)d.Luid.HighPart << 32) | d.Luid.LowPart),
                            d.Description ?? string.Empty,
                            d.VendorId,
                            d.DeviceId,
                            d.SubsystemId,
                            d.Revision,
                            (ulong)d.DedicatedVideoMemory,
                            (ulong)d.DedicatedSystemMemory,
                            (ulong)d.SharedSystemMemory,
                            (d.Flags & AdapterFlags.Software) != 0,
                            (d.Flags & AdapterFlags.Remote) != 0));
                    }
                }

                return new AdapterEnumeration(adapters, null);
            }
        }
        catch (Exception ex) when (ex is SharpGenException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or COMException or InvalidOperationException)
        {
            return new AdapterEnumeration([], $"DXGI is not usable here ({ex.GetType().Name})");
        }
    }
}
