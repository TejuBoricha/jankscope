using System.Windows.Interop;
using Jankscope.Core.Hotkeys;
using Jankscope.Platform.Hotkeys;

namespace Jankscope.App.Services;

/// <summary>
/// Receives the registered hotkeys through a hidden, message-only window. This is the only way the program watches the keyboard (HC-4): Windows posts
/// WM_HOTKEY to this window when exactly a registered chord is pressed; no other key is seen. The window filter below belongs to this one window and
/// reads only that one message; it is not a system-wide hook.
/// </summary>
public sealed class HotkeyHost : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HwndMessage = -3;

    private readonly HwndSource _window;
    private readonly HotkeyRegistrar _registrar;

    public HotkeyHost(IHotkeyApi? api = null)
    {
        _window = new HwndSource(new HwndSourceParameters("JankscopeHotkeys")
        {
            ParentWindow = new IntPtr(HwndMessage),
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        _window.AddHook(OnMessage);
        _registrar = new HotkeyRegistrar(api ?? new WindowsHotkeyApi(), _window.Handle);
    }

    /// <summary>Raised on the interface thread when a registered chord is pressed.</summary>
    public event Action<HotkeyAction>? Pressed;

    public IReadOnlyList<HotkeyStatus> Statuses => _registrar.Statuses;

    /// <summary>Registers the chords, releasing the previous ones first. A chord another program owns is reported in the result, not thrown.</summary>
    public IReadOnlyList<HotkeyStatus> Apply(HotkeyBindings bindings) => _registrar.Apply(bindings);

    public void Dispose()
    {
        _registrar.Dispose();
        _window.RemoveHook(OnMessage);
        _window.Dispose();
    }

    private IntPtr OnMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _registrar.TryHandle(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }

        return IntPtr.Zero;
    }
}
