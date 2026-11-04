using System.Runtime.InteropServices;
using Jankscope.Core.Hotkeys;

namespace Jankscope.Platform.Hotkeys;

public enum HotkeyFailure
{
    None = 0,

    /// <summary>Another program (or another part of this one) already owns the chord.</summary>
    AlreadyRegistered = 1,

    /// <summary>Windows does not accept the key or modifiers.</summary>
    InvalidKey = 2,

    /// <summary>The window that should receive the key press does not exist.</summary>
    InvalidWindow = 3,

    Other = 4,
}

public readonly record struct HotkeyRegistrationResult(bool Succeeded, HotkeyFailure Failure, int Win32Error)
{
    public static HotkeyRegistrationResult Success { get; } = new(true, HotkeyFailure.None, 0);
}

/// <summary>The seam over <c>RegisterHotKey</c> and <c>UnregisterHotKey</c>, so every failure can be tested without owning a real chord.</summary>
public interface IHotkeyApi
{
    HotkeyRegistrationResult Register(nint window, int id, HotkeyChord chord);

    bool Unregister(nint window, int id);
}

/// <summary>
/// The only way this program watches the keyboard (HC-4): <c>RegisterHotKey</c>. Windows tells the registered window when exactly this chord is pressed; no
/// other key is seen, no key code is read, and nothing is hooked. The chord is registered with <c>MOD_NOREPEAT</c> so holding it down is one press.
/// </summary>
public sealed class WindowsHotkeyApi : IHotkeyApi
{
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorInvalidWindowHandle = 1400;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    public HotkeyRegistrationResult Register(nint window, int id, HotkeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (NativeHotkeys.RegisterHotKey(window, id, (uint)chord.Modifiers | ModNoRepeat, (uint)chord.VirtualKey))
        {
            return HotkeyRegistrationResult.Success;
        }

        var error = Marshal.GetLastPInvokeError();
        var failure = error switch
        {
            ErrorHotkeyAlreadyRegistered => HotkeyFailure.AlreadyRegistered,
            ErrorInvalidWindowHandle => HotkeyFailure.InvalidWindow,
            ErrorInvalidParameter => HotkeyFailure.InvalidKey,
            _ => HotkeyFailure.Other,
        };
        return new HotkeyRegistrationResult(false, failure, error);
    }

    public bool Unregister(nint window, int id) => NativeHotkeys.UnregisterHotKey(window, id);
}

/// <summary>The state of one hotkey: the chord asked for, whether Windows accepted it, and if not, why in plain words.</summary>
public sealed record HotkeyStatus(HotkeyAction Action, HotkeyChord Chord, bool Active, HotkeyFailure Failure, string? Reason);

/// <summary>
/// Registers the chords of a <see cref="HotkeyBindings"/> with one window and reports, per action, whether it worked. A chord that another program owns is
/// a normal condition (another product may use it, and the function row depends on Fn-lock): it is reported with a reason, never hidden, and the
/// other chord is still tried. Thread-safe.
/// </summary>
public sealed class HotkeyRegistrar : IDisposable
{
    /// <summary>First id used; ids are per window, so only this program's own registrations can collide with them.</summary>
    public const int FirstId = 0x4A50;

    private readonly IHotkeyApi _api;
    private readonly nint _window;
    private readonly object _gate = new();
    private readonly Dictionary<HotkeyAction, HotkeyStatus> _status = [];
    private readonly Dictionary<int, HotkeyAction> _live = [];
    private bool _disposed;

    public HotkeyRegistrar(IHotkeyApi api, nint window)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        _window = window;
    }

    public IReadOnlyList<HotkeyStatus> Statuses
    {
        get
        {
            lock (_gate)
            {
                return [.. _status.Values.OrderBy(s => s.Action)];
            }
        }
    }

    /// <summary>Releases what was registered before, then registers every chord of <paramref name="bindings"/>. Returns the status of each action.</summary>
    public IReadOnlyList<HotkeyStatus> Apply(HotkeyBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ReleaseAll();
            foreach (var (action, chord) in bindings.All())
            {
                var id = FirstId + (int)action;
                var result = _api.Register(_window, id, chord);
                if (result.Succeeded)
                {
                    _live[id] = action;
                    _status[action] = new HotkeyStatus(action, chord, true, HotkeyFailure.None, null);
                }
                else
                {
                    _status[action] = new HotkeyStatus(action, chord, false, result.Failure, Explain(chord, result));
                }
            }

            return Statuses;
        }
    }

    /// <summary>Maps the id of a WM_HOTKEY message to the action it triggers. False for an id this registrar does not own or whose registration failed.</summary>
    public bool TryHandle(int id, out HotkeyAction action)
    {
        lock (_gate)
        {
            return _live.TryGetValue(id, out action);
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
            ReleaseAll();
        }
    }

    private void ReleaseAll()
    {
        foreach (var id in _live.Keys)
        {
            _api.Unregister(_window, id);
        }

        _live.Clear();
        _status.Clear();
    }

    private static string Explain(HotkeyChord chord, HotkeyRegistrationResult result) => result.Failure switch
    {
        HotkeyFailure.AlreadyRegistered =>
            $"Another program already uses {chord}. Choose a different chord in Settings, or use the tray menu to mark.",
        HotkeyFailure.InvalidKey => $"Windows does not accept {chord}. Choose a different chord in Settings, or use the tray menu to mark.",
        HotkeyFailure.InvalidWindow => "The window that receives the hotkeys is not available. Use the tray menu to mark.",
        _ => $"Windows refused {chord} (error {result.Win32Error.ToString(System.Globalization.CultureInfo.InvariantCulture)}). Use the tray menu to mark.",
    };
}
