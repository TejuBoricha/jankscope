# Security Policy

## Security & Trust Model

Jankscope is engineered with a **zero-trust, fail-closed security architecture** designed specifically for gaming PCs where kernel-level anti-cheat drivers and personal data coexist:

1. **Zero Network Attack Surface (Stage 1):**
   - Jankscope contains no HTTP clients, sockets, telemetry SDKs, auto-updaters, or remote listeners.
   - `System.Net.Http`, `System.Net.Sockets`, `System.Net.WebSockets`, `System.Net.WebClient`, and `System.Net.Dns` are banned at compile time by [`BannedSymbols.txt`](BannedSymbols.txt) (`RS0030`).
2. **Zero Foreign Process Intrusion:**
   - Jankscope never calls `OpenProcess`, `ReadProcessMemory`, `WriteProcessMemory`, `CreateRemoteThread`, `SetWindowsHookEx`, or `DebugActiveProcess`.
   - Even when terminating its own PresentMon child process, Jankscope never calls `Process.Kill(entireProcessTree: true)` because the .NET runtime implementation of tree-kill enumerates and opens handles to unrelated processes on the machine (documented in [`docs/reviews/stage-1a.md`](docs/reviews/stage-1a.md)).
3. **Pinned & Handle-Verified Child Executable (`HC-7`):**
   - The only external binary Jankscope starts is the pinned Intel PresentMon 2.6.0 console executable ([`third_party/PresentMon/presentmon.lock.json`](third_party/PresentMon/presentmon.lock.json)).
   - Before launch, [`FileHashVerifier`](src/Jankscope.Frames/PresentMon/FileHashVerifier.cs) opens the executable with `FileShare.Read` (blocking concurrent writers), refuses symbolic links and reparse points across the file and its ancestor directories, and verifies both file byte length and SHA-256 hash on the open stream.
   - [`PresentMonArguments`](src/Jankscope.Frames/PresentMon/PresentMonArguments.cs) passes arguments strictly via `ProcessStartInfo.ArgumentList` (never a concatenated shell command string) and enforces an allowlist that unconditionally rejects `--restart_as_admin`.
4. **System32-Only Native DLL Loading:**
   - Every assembly with native P/Invoke (`Jankscope.Platform`) declares `[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` and uses an exact, test-pinned list of 26 `[LibraryImport]` entry points.
5. **Two-Layer Fail-Closed Export Redaction:**
   - Diagnostic reports are scrubbed by [`Redactor`](src/Jankscope.Core/Reports/Redactor.cs) and independently verified by [`ExportGuard`](src/Jankscope.Core/Reports/ExportGuard.cs) across raw text, URL-decoded text, and unescaped JSON strings. If any personal identifier or canary survives, `ExportBlockedException` aborts the export before any file is created.

---

## Supported Versions

| Version / Stage | Supported | Notes |
| :--- | :--- | :--- |
| `main` (Stage 1a + Stage 1b verified) | Yes | Active development branch targeting `.NET 10.0` (`10.0.402`) on Windows 10/11 x64 |

---

## Reporting a Vulnerability

If you discover a security, privacy, redaction-bypass, or anti-cheat safety vulnerability in Jankscope:

1. **Do not open a public issue** if the report includes an exploit or a bypass of `Redactor` / `ExportGuard` / `BannedSymbols.txt` / `FileHashVerifier`.
2. **Use GitHub Private Vulnerability Reporting** on this repository (`Security` tab -> `Report a vulnerability`), or contact the maintainer directly via the profile contact on [@TejuBoricha](https://github.com/TejuBoricha).
3. Include:
   - Affected component and file path (e.g., `src/Jankscope.Core/Reports/ExportGuard.cs` or `src/Jankscope.Frames/PresentMon/FileHashVerifier.cs`)
   - Minimal reproduction test case (xUnit `[Fact]` or PowerShell command)
   - Impact assessment against `HC-1` .. `HC-13`

Every confirmed vulnerability is fixed with a regression test and a mutation proof verifying that the test fails when the fix is removed.
