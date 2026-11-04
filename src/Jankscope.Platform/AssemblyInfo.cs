using System.Runtime.InteropServices;

// Review #6: resolve every native library from System32 only, never from the application directory, so a planted DLL
// next to the executable cannot be loaded (this matters more once an elevated helper exists).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
