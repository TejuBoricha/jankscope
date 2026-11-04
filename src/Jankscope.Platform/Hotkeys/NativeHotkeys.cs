using System.Runtime.InteropServices;

namespace Jankscope.Platform.Hotkeys;

/// <summary>
/// The two documented Win32 entry points for global hotkeys (winuser.h). <c>RegisterHotKey</c> makes Windows post a message to the window when exactly one chord is
/// pressed; it does not report any other key. Nothing here reads keyboard state, installs a hook or sends input. The DLL search path is restricted to System32
/// by the assembly attribute in AssemblyInfo.cs.
/// </summary>
internal static partial class NativeHotkeys
{
    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint window, int id);
}
