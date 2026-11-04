# Install and developer setup

## Developers
1. Install the .NET SDK pinned in `global.json` (10.0.402; later 10.0.x patches are accepted). On Windows:
   ```
   winget install --id Microsoft.DotNet.SDK.10 --source winget --accept-package-agreements --accept-source-agreements
   ```
   A machine-wide install shows a UAC prompt. Check that the installer hash is verified in the output.
2. `./build.ps1` builds, tests and smoke-runs the CLI.
3. `src/Jankscope.Cli/bin/Release/net10.0-windows/jankscope.exe probe` prints the capability matrix for the PC (read-only, nothing is sent anywhere). Add `--json` for the full detail including sources.

## Users (end-user packages arrive in Stage 5)
No installer exists yet. When it does it will be per-user (no elevation to install); a portable zip will also be offered, with elevated features unavailable in portable mode.

## Unelevated frame capture (optional, your decision)
Frame-level capture needs permission to create real-time tracing sessions. Without it Jankscope still works, but frame pacing shows as unavailable. **Jankscope never changes this for you.** If you want it:
1. Open **Computer Management** as an administrator (`compmgmt.msc`).
2. Go to *Local Users and Groups → Groups → Performance Log Users* and add your account.
3. Sign out and back in.

What this implies: members of Performance Log Users can start and read ETW tracing sessions without admin rights. Only add accounts you trust. This is the same requirement PresentMon documents.

To undo it, remove the account from the group the same way.

## Uninstall
Nothing is installed by Stage 0. Future uninstall will offer to roll back any active changes and ask whether to delete the data folder (`%LOCALAPPDATA%\Jankscope\`).
