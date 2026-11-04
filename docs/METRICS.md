# Metrics — operational definitions

Spec §7.1. Implemented in `Jankscope.Core/FrameStats.cs` and covered by `FrameStatsTests`; the stutter event is in `Jankscope.Frames/Detection/StutterDetector.cs`. All inputs are frame times in milliseconds.

| Metric | Definition | Notes |
|---|---|---|
| Average FPS | frames ÷ total duration | Not the mean of per-frame FPS (the mean over-weights fast frames). Test: frames of 10 ms and 30 ms give 50 fps, not 66.7. |
| Percentile frame time | **Nearest rank**: the value at rank ⌈p/100 × N⌉ of the ascending sort | Documented choice; interpolation is not used. Empty input or p outside (0, 100] gives NaN. |
| 1 % low (primary) | FPS at the **99th-percentile frame time** = 1000 ÷ P99 ms | Needs ≥ 1 000 frames; below that the value is "insufficient samples" (null), never an estimate. |
| 0.1 % low (primary) | FPS at the 99.9th-percentile frame time | Needs ≥ 10 000 frames; **session-level windows only**. |
| Alternative lows (secondary column) | Mean of the slowest 1 % / 0.1 % of frames | Not implemented yet (Stage 1a); labeled everywhere when shown. |

## Stutter event (implemented, Stage 1a)

A frame is a **stutter** when its frame time (`MsBetweenPresents`) is **at least 2×** the median frame time of the window before it **and at least 4 ms above** that median. The constants are `StutterOptions` and are **uncalibrated** until a baseline exists for the machine and game; every result carries `Calibrated`/`CalibrationLabel` and downstream grades are capped at *Possible* while they are uncalibrated (spec §7.1).

Decisions that change the result and are therefore part of the definition (checked by `StutterDetector*Tests`, the 216-run synthetic grid and a reference implementation without heaps):

| Decision | Rule |
|---|---|
| Window | The accepted frames whose timestamps lie in `[T − 1000 ms, T]`, where **T is the timestamp of the previous accepted frame**, i.e. the instant the judged frame began. Anchoring there means a long freeze cannot empty the window and hide itself. |
| The judged frame | Never part of its own median. Every accepted frame, hitches included, is added to the window afterwards: outliers are not dropped, and the median is robust to them. |
| Warm-up | With fewer than **10** frames in the window a frame is `InsufficientHistory`: counted, never "no stutter". |
| Rejected samples | A non-finite or negative or **zero** frame time, or a non-finite timestamp, is rejected with a reason and counted (`FramesSeen = FramesAccepted + RejectedFrames`). Zero is rejected because no frame takes no time: it is a missing measurement. |
| Backwards time | A timestamp earlier than the previous accepted one (by any amount) is a **clock discontinuity**: counted, and the history restarts. The same timestamp twice is not a discontinuity. |
| Tolerance | Both comparisons carry an absolute tolerance of 1e-9 ms, so values exactly on an edge in decimal (6.1 ms against a 2.1 ms median is exactly +4 ms) are not lost to binary rounding. |
| Severity | By excess over the median: Minor < 10 ms ≤ Moderate < 25 ms ≤ Major < 50 ms ≤ Severe. |
| Clusters | Events closer than **500 ms** (strictly less; exactly 500 ms starts a new cluster) belong to one cluster; a cluster of exactly one event is an isolated spike. |
| Provenance | The result carries the weakest provenance of any frame that went in (`ProvenanceRules.Combine`): one simulated frame makes the whole result simulated. |

**Known blind spot.** After a frame of at least 1000 ms (a freeze as long as the window) the window holds only that frame, so the next frames are `InsufficientHistory` until 10 have gone by, and a second hitch in that span is not judged. It is counted, never reported as "no stutter". Below about 10 fps the detector judges nothing, for the same reason. Pinned by `A_freeze_as_long_as_the_window_leaves_the_frames_after_it_unjudged_and_counts_them_as_insufficient_history`.

**What the tests can and cannot claim.** The exact grid proves the implementation matches this definition on regular workloads with isolated spikes. It cannot claim detection quality on irregular real traces; that needs the calibration baselines (spec §7.1).

## Not implemented yet
- **Frame generation:** `MsBetweenPresents` (application frames) vs `MsBetweenDisplayChange` (displayed frames) are reported separately and never mixed.
- Outliers are never silently dropped; alt-tab and loading windows get with/without views.
- Lost events, collector gaps and clock gaps appear on the timeline and lower the grade of overlapping findings.
- A capture without a process filter interleaves every presenting process; the pipeline must pass a process name and split streams by (`ProcessId`, swap-chain address) before the detector sees them (the swap-chain address is not carried by `FrameSample` yet; review 1a, finding 15).

## Provenance
Every value carries a provenance (`Measured`, `Inferred`, `Assumed`, `Simulated`, `Fixture`). Simulated and fixture data cannot be exported as a measurement (HC-9); the guard is `ProvenanceRules.RequireExportableAsMeasurement`. A frame carries its provenance (`FrameSample.Provenance`): the parser sets `Measured` for the pinned tool's own output (or what the caller states, such as `Fixture`), a row of the synthetic harness says `Simulated` in its `JankscopeProvenance` column, a sample built by hand is `Assumed`, and combining data keeps the weakest claim.
