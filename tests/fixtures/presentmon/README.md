# PresentMon fixtures

All files here are **fixtures** (`DataProvenance.Fixture`): they exist to test parsers and can never be exported as a measurement (HC-9).

| File | What it is |
|---|---|
| `v2.6.0-real-sanitized.csv` | 857 rows of real PresentMon 2.6.0 console output captured unelevated on the reference bench on 2026-10-16 (`--timed 6 --no_console_stats --output_file`). Application names, process ids and swap-chain addresses were replaced (`TestAppA.exe`, ids from 1000, `0x1xxxx`); timing values are untouched. |
| `synthetic-v1-metrics.csv` | **Hand-written, not a capture.** A PresentMon 1.x style log (`--v1_metrics` header, `TimeInSeconds`, `msBetweenPresents`); built from the documented header, so it exercises format detection only. |
| `synthetic-reordered-extra-columns.csv` | **Hand-written.** A 2.x log with reordered columns and extra unknown columns (columns are matched by name). |
| `synthetic-missing-optional.csv` | **Hand-written.** Required columns present, optional columns absent (features degrade, the log stays supported). |
| `synthetic-missing-frame-time.csv`, `synthetic-missing-timestamp.csv` | **Hand-written.** Each lacks one required column: the parser must report `unsupported log` naming it. |
| `synthetic-quoted-and-unknown.csv` | **Hand-written.** Quoted fields with commas and doubled quotes, and the literal `<unknown>` application text. |
| `synthetic-truncated-last-line.csv` | **Hand-written.** The last line is cut off mid-row (a tool that died while writing). |
| `synthetic-frame-generation.csv` | **Hand-written.** Rows with frame-generation columns, to prove `MsBetweenPresents` and `MsBetweenDisplayChange` are never merged. |

Facts this real log established (also in `docs/FACTS.md`):
- Header (28 columns): `Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,TimeInMs,MsBetweenSimulationStart,MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI,MsRenderPresentLatency,MsUntilDisplayed,CPUStartTimeInMs,MsBetweenAppStart,MsCPUBusy,MsCPUWait,MsGPULatency,MsGPUTime,MsGPUBusy,MsGPUWait,MsAnimationError,AnimationTime,MsFlipDelay,MsAllInputToPhotonLatency,MsClickToPhotonLatency`.
- Unavailable values are the literal text **`NA`** — a parser must map them to "not available", never to zero.
- Numbers use `.` as the decimal separator regardless of Windows region (parse with the invariant culture).
- `PresentMode` is free text such as `Hardware Composed: Independent Flip` (no commas).
- Without elevation PresentMon warns on stderr that short-running or other-account processes appear as `<unknown>`.
