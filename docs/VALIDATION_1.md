# Stage 1 Owner Validation Guide (`VALIDATION_1.md`)

**Milestone:** End of Stage 1 (Stage 1a Headless Core + Stage 1b Interactive Slice)
**Status:** Ready for owner live-game validation (`2,504` tests passing, `0` warnings, `0` errors)

This guide walks through validating `jankscope-app.exe` against a real game on your PC (such as **Aim Lab** or **PC Building Simulator**) before proceeding to Stage 2 (Raw Input timing, display/GPU correlations, and kernel ETW opt-in).

---

## 1. Prerequisites

1. Ensure the pinned PresentMon 2.6.0 executable is present at `third_party\PresentMon\2.6.0\PresentMon-2.6.0-x64.exe` (already verified on this machine).
2. Ensure your Windows account is in the **Performance Log Users** group (already verified; unelevated ETW capture works without administrator prompts).
3. Build the Release binaries if you have not already:
   ```powershell
   .\build.ps1
   ```

---

## 2. Step-by-Step Live Game Validation

### Step 1 — Launch Jankscope Desktop (`jankscope-app.exe`)
Run the desktop app from PowerShell:
```powershell
.\src\Jankscope.App\bin\Release\net10.0-windows\jankscope-app.exe
```
- On first launch, the **Welcome** dialog explains what Jankscope collects (frame presentation times only in Stage 1), confirms zero network access, and shows the active global hotkeys (`Ctrl+Alt+F9` for **Mark stutter**, `Ctrl+Alt+F10` for **Mark input stall**).
- Click **Continue** to enter the main window.
- Verify the system tray icon appears in the Windows notification area.

### Step 2 — Launch a Game and Start Capture
1. Start **Aim Lab**, **PC Building Simulator**, or any Direct3D 11/12 game in borderless windowed or fullscreen mode and enter an active scene where frames are rendering.
2. In Jankscope's **Capture** tab:
   - Click **Scan running programs (3 s)** to automatically list processes currently presenting frames on your GPU, **or** type the game's executable name directly (for example `AimLab_tb.exe` or `PCBS.exe`).
   - Click **Start capture**.
3. Verify the status banner changes to **Capturing `<game>.exe`**, the live FPS / frame-time / 99th-percentile counters update every 500 ms, and the 60-second scrolling frame-time timeline draws smoothly.

### Step 3 — Trigger an In-Game Incident Mark (`Ctrl+Alt+F9`)
1. While playing (or right after inducing a stutter, such as changing graphics quality, alt-tabbing, or loading an asset), press **`Ctrl+Alt+F9`** (or **`Ctrl+Alt+F10`** for an input stall).
2. Verify:
   - You hear the two-tone ascending confirmation chime (without the game losing focus or the Jankscope window popping up over the game).
   - Pressing `Ctrl+Alt+F9` twice within 1 second is debounced so accidental double-taps do not create duplicate marks.

### Step 4 — Inspect the Mark & Select an Anchor in the Investigator
1. Switch back to Jankscope and open the **Investigator** tab.
2. Select your mark in the left-hand list.
3. Verify:
   - The timeline shows the pre-roll window leading up to your mark, the mark's capture-clock uncertainty band, and numbered markers for the top **Candidate Anchors** found in the 10 seconds before your key press.
   - **No candidate is auto-selected** (the key press is never silently treated as the event time).
4. Click **Candidate 1** (or click directly on a spike on the timeline to set a custom adjusted time, or click **Use key-press time**), and optionally add a short note describing what you saw.

### Step 5 — Preview & Export a Redacted Incident Bundle
1. Click **Preview & export...** in the Investigator tab.
2. In the **Export Preview** window, verify:
   - The summary header lists how many personal identifiers (`UserName`, `HostName`, `IpAddress`, etc.) were replaced and shows the exact SHA-256 hash of the output bytes.
   - Scroll through the JSON lines to verify your Windows username and PC hostname are replaced with `<USER>` and `<HOST>`.
3. Click **Save redacted JSON...** and save the report to disk.
4. (Optional) Verify the saved file matches the preview SHA-256:
   ```powershell
   Get-FileHash .\path\to\saved-incident.json -Algorithm SHA256
   ```

### Step 6 — Clean Shutdown Verification
1. Click **Quit Jankscope** from the system tray menu (or close the window after stopping capture).
2. Verify no orphaned PresentMon process or ETW session remains:
   ```powershell
   Get-Process PresentMon-*
   logman query -ets | Select-String "Jankscope"
   ```
   Both commands should return nothing.

---

## 3. What to Report Back

Once you have tried the flow above:
- Reply **"Stage 1 validated — proceed to Stage 2"** if the capture, hotkey mark, candidate selection, and redacted export work as expected.
- Or note any UI/UX adjustments you want before Stage 2 begins.
