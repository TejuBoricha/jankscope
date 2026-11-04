# Test plan — Stage 1a (headless core)

Written **before** implementation (spec §1 #3). Produced with the testing-strategy skill's pyramid: many fast unit tests, some integration tests, few end-to-end tests. Exit criterion (spec §17): **injected hitches are detected within ±1 frame**, verified by the agent; the run works as a non-elevated user; no metric is ever fabricated.

## Conventions
- Pure logic lives in `Jankscope.Core` (portable); Windows adapters in `Jankscope.Platform` behind small interfaces so every failure path can be injected.
- Fixtures are tagged `DataProvenance.Fixture`; harness output `Simulated`. Neither may be exported as a measurement.
- Hardware- or tool-dependent tests carry a trait (`requires-d3d`, `requires-presentmon`, `requires-gpu`) and skip **with a reason** when the dependency is absent, so the suite runs on any Windows PC or VM.
- Coverage targets: ≥ 90 % lines on metrics, stutter detector, ring buffer, CSV parser and runner logic; ≥ 80 % on Platform adapters; none for generated code. Measured with the Microsoft code-coverage collector already restored.
- Safety-critical rules get a **mutation check** (break the rule, see a test fail, restore).

## Test matrix

| # | Area | Level | What must be proven | Example cases |
|---|---|---|---|---|
| 1 | Stutter detector (Core) | Unit + seeded randomized | Definition from METRICS.md: frame ≥ 2× rolling 1 s median **and** ≥ 4 ms above it; isolated spikes vs low average FPS are distinguished; outliers never silently dropped | Exactly 2× and exactly +4 ms boundaries (both must hold); 1.99×; +3.9 ms; fewer than N frames of history ⇒ "insufficient history", not "no stutter"; non-finite/negative frame times rejected with a reason; clustering/severity numbers; mutation: change 2× to 1.5× ⇒ tests fail |
| 2 | Ring buffer (Core) | Unit + concurrency + allocation | Fixed capacity, oldest evicted, pre-roll window selection, consistent snapshots, no hot-path allocation | Wrap-around index math; snapshot during concurrent writes never torn (1 writer, 3 readers, 2 s); `GC.GetAllocatedBytesForCurrentThread()` delta = 0 after warm-up; capacity scales with hardware class |
| 3 | PresentMon CSV parser (Core) | Unit + fixture files | Columns by **name**; unknown columns ignored; missing optional columns degrade features; missing required columns ⇒ explicit "unsupported log: <why>"; culture-invariant; frame-generation columns kept separate | v2.x sample; v1-style log (`--v1_metrics`); extra and reordered columns; `<unknown>` process name; quoted fields; truncated last line; decimal parsing under `de-DE`/`tr-TR` thread cultures; `MsBetweenPresents` vs `MsBetweenDisplayChange` never merged; 1 M-row stream parses without loading the file into memory |
| 4 | PresentMon runner | Integration with a **fake executable** + unit | Fixed argument construction (no shell, no string concatenation), unique session name with our prefix, hash verified **before every launch**, watchdog and one-click stop, crash restart with back-off, orphan cleanup only for our prefix | Arguments for process names containing spaces, quotes, `&`, `;` stay one argv element; hash mismatch ⇒ refuse to launch; fake exe hangs ⇒ watchdog kills it; fake exe exits non-zero ⇒ health row + back-off; `--stop_existing_session` never passed for a foreign session name; pinned absolute path (no PATH lookup); real PresentMon test runs only when the approved binary is present |
| 5 | PDH sampler (Platform) | Unit (parsers) + integration | English counter paths via `PdhAddEnglishCounter`; GPU Engine instances parsed into pid/LUID/engine type and **keyed by LUID**; missing instance ⇒ explicit "not available (reason)", never 0 | Real hybrid-laptop instance strings as fixtures (two LUIDs, several engine types); per-core counter count equals logical processors; hard-fault counter present; sampling at 1–10 Hz drift test; sampler self-cost measured |
| 6 | Hardware discovery (Platform) | Unit (pure mappers) + integration | CPU topology (hybrid P/E via efficiency classes, SMT), GPU adapters keyed by LUID (which adapter renders, which drives the display), per-monitor refresh rates, VM detection from a catalog | Topology mapper on fixtures: hybrid 6P+8E, non-hybrid, SMT off, multi-CCD; real machine: P+E counts sum to logical processors; mixed-refresh dual-monitor fixture; VM markers from catalog (no hardcoded vendors — the lint stays green) |
| 7 | Harness renderer (tool) | Integration (`requires-d3d`) | Renders at the target rate, injects the scheduled hitches, logs its **own** QPC at `Present` return independent of the app's capture; falls back to the software renderer; output tagged Simulated | Achieved frame times match the schedule within tolerance; hitch frames carry the injected extra time; runs unelevated; software-renderer run proves the pipeline only (labeled so) |
| 8 | **End-to-end exit test** | E2E | Harness → frame log → parser → detector → incident report: every injected hitch found within **±1 frame**, zero false positives on clean runs | ≥ 10 seeds × {60, 144, 240 fps} × hitch sizes 15/25/50 ms and jitter 0/0.3 ms; precision = recall = 100 % where the hitch exceeds the threshold; sub-threshold hitches are reported as *not detectable by definition*, not as misses; variant (b) through the real PresentMon once the binary is approved |
| 9 | CLI incident report | Golden-file + unit | Provenance labels, units, windows, data-quality flags; "insufficient samples" for 0.1 % low in incident windows; unavailable data reads "not available — reason" | Golden text and JSON; fixture/simulated data refused for export as a measurement; redaction canaries (username, hostname, SSID, MAC, IP, serial) must not survive export (fails closed) |
| 10 | Overhead measurement v1 | Benchmark (agent-run) | Paired runs, collectors on vs off, measured from the harness side; per-process CPU/working set/disk summed over all app-owned processes; budgets from spec §14 | Interleaved A-B-A-B, ≥ 10 runs per condition, median + bootstrap 95 % interval; budget misses reported, not hidden; ≥ 10 minute passive run |
| 11 | Standard-user behaviour | Integration | Core features need no elevation; elevated ones say so | Restricted-token run (`runas /trustlevel`) of `probe` and the harness pipeline, labeled *restricted-token simulation*; forced-denied ETW seam (`IEtwAccess`) ⇒ frame capture shows the exact user action |
| 12 | Portability | Unit/fixture | Behaviour under foreign conditions | Non-English counter-name fixture; no GPU counters; no battery; ≤ 4-core scaling; Windows 10 build number paths (simulated); absent vendor APIs (none adopted yet) |
| 13 | Safety and privacy | Static + unit | HC-1/HC-5/HC-12 stay enforced as code grows | Existing lints stay green; no `OpenProcess` even in the runner (PresentMon is launched, never opened as a foreign PID); no network APIs; canary string planted in diagnostic data never appears outside the machine (no egress exists) |
| 14 | Failure paths | Unit/integration | Each degrades with a stated reason | PresentMon crash, truncated CSV, clock jump, access denied mid-run, GPU counter vanishes, ETW session orphan, temp disk full |

## Order of work (TDD)
1. Ring buffer and stutter detector with tests 1–2 (pure, fast).
2. CSV parser with fixtures (test 3), then runner against a fake exe (test 4).
3. PDH instance parsers (pure) then the sampler; topology and LUID mappers (tests 5–6).
4. Harness renderer (test 7) → end-to-end test 8 using the harness's own present log first.
5. CLI report goldens (9), overhead benchmark (10), restricted-token and portability checks (11–12), failure paths (14).
6. Independent code review and security review of the runner; record in `docs/reviews/stage-1a.md`.

## Gaps in current (Stage 0) coverage this stage closes
- No test seam for ETW access (`IEtwAccess`) — add it so the "no group membership" path is tested without changing the machine.
- No code-coverage reporting configured — enable the existing collector in `build.ps1 -Coverage`.
- Probe tests assert structure, not host-independent truth tables for hardware mappings (SMBIOS table has a known bug; fixed under the Stage 0 review).
- No Windows 10 or non-English simulation yet — added as fixtures.

## Out of scope for Stage 1a
UI, hotkeys, incident bundles, redacted export UI (Stage 1b); input and network collectors (Stage 2); elevated helper (Stage 3).

## Resolutions from the independent Stage 1a review (docs/reviews/stage-1a.md)
- **Row 4 (runner) additions:** the launcher cannot be given a bare path (`ProcessLaunchRequest` carries a `VerifiedExecutable`); the verified file stays write- and rename-protected until the launcher has returned; a junction anywhere above the file is refused (real junctions in the tests), and the reparse refusal is pinned through an attribute seam because this account cannot create symbolic links; a failure while setting a run up still ends the child and removes its ETW session; a cut-off last line is dropped, never parsed; the exact set of `Process` members the launcher file may use is pinned by `LauncherLintTests`.
- **Row 9/13 (provenance, HC-9):** a frame carries `Provenance`; the harness log's `JankscopeProvenance` column makes its frames Simulated; the detector's result carries the weakest provenance of any frame it saw.
- **Row 7 (harness) skips:** a skip is allowed only when no device or window can be had (`HarnessEnvironmentException.DeviceUnavailable`); a failure after a device exists fails the tests and a canary names it. `build.ps1 -RequireHardwareTests` (reference bench) turns any skipped Direct3D or real-PresentMon test into a failure.
- **Row 8, variant through the real PresentMon:** run the harness with `--window-visible` (a hidden window is invisible to PresentMon), through the real runner and parser, as the workload instead of whatever the desktop presents.
- **Resolved:** the orphan-ETW-session cleaner, managed/game-session probes, anti-cheat start gate, locale redaction, incident report and overhead benchmark are complete; see `docs/STATUS.md` and `docs/reviews/stage-1a-reverify.md`.
- **Remaining limitation:** unfiltered captures still need swap-chain identity and stream splitting before the detector sees mixed processes (LIMITATIONS L26); this is a later engine follow-up.
