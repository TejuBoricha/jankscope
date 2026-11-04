using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Jankscope.Harness;

/// <summary>
/// Direct3D 11 renderer with a flip-model swap chain (<c>DXGI_SWAP_EFFECT_FLIP_DISCARD</c>). Each frame it clears the back buffer to a colour
/// that differs from its neighbours (a hitch frame flashes red) and presents with sync interval 0, using the tearing flag where the OS offers
/// it so a windowed present is not held back to the display refresh. Hardware first; the WARP software rasterizer when there is no usable
/// hardware adapter. WARP renders the same frames, but its timing says nothing about a GPU: the result records which one ran.
/// </summary>
internal sealed class D3D11Presenter : IFramePresenter, IDisposable
{
    private const int BufferCount = 3;

    /// <summary>DXGI_STATUS_OCCLUDED: the window is not visible, so nothing is shown. Success status; the call returns at once.</summary>
    private const int StatusOccluded = 0x087A0001;

    private static int s_live;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly ID3D11RenderTargetView _target;
    private readonly PresentFlags _presentFlags;
    private int _disposed;

    private D3D11Presenter(
        ID3D11Device device,
        ID3D11DeviceContext context,
        IDXGISwapChain1 swapChain,
        ID3D11RenderTargetView target,
        PresentFlags presentFlags,
        AdapterInfo adapter,
        SwapChainInfo swapChainInfo)
    {
        _device = device;
        _context = context;
        _swapChain = swapChain;
        _target = target;
        _presentFlags = presentFlags;
        Adapter = adapter;
        SwapChain = swapChainInfo;
        Interlocked.Increment(ref s_live);
    }

    /// <summary>Presenters created and not yet disposed (see <see cref="RenderWindow.LiveInstances"/>).</summary>
    public static int LiveInstances => Volatile.Read(ref s_live);

    public AdapterInfo Adapter { get; }

    public SwapChainInfo SwapChain { get; }

    /// <exception cref="HarnessEnvironmentException">No device could be created for the request, or the swap chain could not be created.</exception>
    public static D3D11Presenter Create(IntPtr windowHandle, int width, int height, AdapterRequest request, bool simulateNoHardware = false)
    {
        IDXGIFactory2? factory = null;
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        try
        {
            try
            {
                factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // No DXGI factory at all (a Windows Server Core image, a broken driver stack): this machine cannot run the harness.
                throw new HarnessEnvironmentException("DXGI is not available (" + e.GetType().Name + ")", deviceUnavailable: true);
            }

            var kind = AdapterKind.Hardware;
            string description = string.Empty;
            string? fallbackReason = null;
            FeatureLevel level = default;
            var created = false;

            if (request != AdapterRequest.Warp)
            {
                if (simulateNoHardware)
                {
                    fallbackReason = "hardware adapter creation was skipped by a test hook";
                }
                else
                {
                    created = TryCreateOnHardware(factory, out device, out context, out level, out description, out fallbackReason);
                }

                if (!created && request == AdapterRequest.Hardware)
                {
                    throw new HarnessEnvironmentException("no hardware Direct3D 11 adapter could be created (" + fallbackReason + ")", deviceUnavailable: true);
                }
            }

            if (!created)
            {
                kind = AdapterKind.Warp;
                var result = D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, FeatureLevels, out device, out level, out context);
                if (result.Failure || device is null || context is null)
                {
                    throw new HarnessEnvironmentException("the WARP software adapter could not be created (" + result + ")", deviceUnavailable: true);
                }

                description = DescribeWarp(device);
            }

            var info = new AdapterInfo(kind, description, level.ToString(), request, kind == AdapterKind.Warp && request == AdapterRequest.Auto ? fallbackReason : null);
            var presenter = CreateSwapChainAndTarget(factory, device!, context!, windowHandle, width, height, info);
            device = null; // ownership moved to the presenter
            context = null;
            return presenter;
        }
        catch (HarnessEnvironmentException)
        {
            throw;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new HarnessEnvironmentException("Direct3D 11 setup failed: " + e.Message);
        }
        finally
        {
            context?.Dispose();
            device?.Dispose();
            factory?.Dispose();
        }
    }

    public void Prepare(int frameIndex, bool isHitch)
    {
        // Only the clear is recorded here, ahead of the wait: Present() is then the single call between the target time and the timestamp.
        var phase = frameIndex % 120 / 120f;
        var color = isHitch ? new Color4(0.9f, 0.1f, 0.1f, 1f) : new Color4(0.05f, 0.1f + 0.3f * phase, 0.35f, 1f);
        _context.ClearRenderTargetView(_target, color);
    }

    public PresentOutcome Present()
    {
        var result = _swapChain.Present(0, _presentFlags);
        if (result.Code == StatusOccluded)
        {
            return PresentOutcome.Occluded;
        }

        if (result.Failure)
        {
            throw new HarnessEnvironmentException("Present failed: " + result);
        }

        return PresentOutcome.Presented;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _context.ClearState();
            _context.Flush();
        }
        finally
        {
            _target.Dispose();
            _swapChain.Dispose();
            _context.Dispose();
            _device.Dispose();
            Interlocked.Decrement(ref s_live);
        }
    }

    private static readonly FeatureLevel[] FeatureLevels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];

    private static bool TryCreateOnHardware(
        IDXGIFactory1 factory,
        out ID3D11Device? device,
        out ID3D11DeviceContext? context,
        out FeatureLevel level,
        out string description,
        out string? failure)
    {
        device = null;
        context = null;
        level = default;
        description = string.Empty;
        failure = "no hardware adapter is installed";

        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            using (adapter!)
            {
                var desc = adapter!.Description1;
                if ((desc.Flags & AdapterFlags.Software) != 0)
                {
                    continue;
                }

                var result = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, FeatureLevels, out device, out level, out context);
                if (result.Success && device is not null && context is not null)
                {
                    description = desc.Description;
                    failure = null;
                    return true;
                }

                failure = $"adapter '{desc.Description}' refused a Direct3D 11 device ({result})";
                device?.Dispose();
                context?.Dispose();
                device = null;
                context = null;
            }
        }

        return false;
    }

    private static string DescribeWarp(ID3D11Device device)
    {
        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            return "WARP software rasterizer (" + adapter.Description.Description + ")";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return "WARP software rasterizer";
        }
    }

    private static D3D11Presenter CreateSwapChainAndTarget(
        IDXGIFactory2 factory,
        ID3D11Device device,
        ID3D11DeviceContext context,
        IntPtr windowHandle,
        int width,
        int height,
        AdapterInfo adapter)
    {
        using var factory5 = factory.QueryInterfaceOrNull<IDXGIFactory5>();
        var tearing = factory5?.PresentAllowTearing ?? false;

        var description = new SwapChainDescription1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = Format.B8G8R8A8_UNorm,
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = BufferCount,
            SampleDescription = new SampleDescription(1, 0),
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None,
        };

        IDXGISwapChain1? swapChain = null;
        ID3D11Texture2D? backBuffer = null;
        try
        {
            swapChain = factory.CreateSwapChainForHwnd(device, windowHandle, description);
            factory.MakeWindowAssociation(windowHandle, WindowAssociationFlags.IgnoreAll); // no Alt+Enter, no PrintScreen handling
            backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            var target = device.CreateRenderTargetView(backBuffer);

            var flags = tearing ? PresentFlags.AllowTearing : PresentFlags.None;
            // The swap effect is read back from the created swap chain, not written down again: a summary must say what was really used.
            var effect = swapChain.Description1.SwapEffect.ToString();
            var info = new SwapChainInfo(effect, BufferCount, width, height, 0, (int)flags, tearing, "0x" + swapChain.NativePointer.ToString("X"));
            var presenter = new D3D11Presenter(device, context, swapChain, target, flags, adapter, info);
            swapChain = null; // ownership moved to the presenter
            return presenter;
        }
        finally
        {
            backBuffer?.Dispose();
            swapChain?.Dispose();
        }
    }
}
