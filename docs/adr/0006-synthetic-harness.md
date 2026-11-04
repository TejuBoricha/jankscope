# ADR 0006 — Synthetic workload harness

**Status:** Accepted for Stage 1a (2026-10-16): Vortice.Windows (Direct3D11 + DXGI) · **Spec:** §18

## Context
Stutter detection must be validated without a real game or anti-cheat: a small program renders at a configurable rate and injects **known** hitches, so detection, lows and incident anchoring can be checked against ground truth. No MSVC or Windows SDK is installed, so the harness is C#.

## Decision (so far)
- Stage 0 ships the **ground-truth generator** only (`SyntheticWorkload`, tool `jankscope-harness plan`): deterministic frame-time series with injected hitches, always tagged `Simulated` (HC-9), unit-tested.
- Stage 1a adds a **managed Direct3D renderer** that presents frames following that schedule. Candidates: Vortice.Windows or Silk.NET. Selection criteria: license (MIT/BSD/Apache), .NET 10 support, maintenance activity, trimming/AOT friendliness, ability to create a flip-model swap chain and set present intervals.
- Observer-effect test (§14): the harness logs its own QPC at `Present` return, independent of the app's capture.

## Alternatives rejected
C++ harness (no toolchain); a real game (anti-cheat, non-repeatable); Windows Sandbox/VM for rendering (Home edition lacks them; a VM cannot validate GPU timing — WARP in a cloud VM proves the pipeline only).

## Consequences
The Stage 1a exit criterion (injected hitches detected within ±1 frame) cannot be claimed until the renderer exists; STATUS marks the harness `done-unverified`.

## Renderer decision (2026-10-16)
**Vortice.Windows** `Vortice.Direct3D11` / `Vortice.DXGI` 3.8.3. Read from the packages' NuGet metadata: MIT license, author Amer Koleci, repository github.com/amerkoleci/Vortice.Windows, `net10.0` and `net10.0-windows10.0.22621` targets, depends on `Vortice.DirectX`, `SharpGen.Runtime`, `SharpGen.Runtime.COM`, `Vortice.Mathematics` (2.1.1, MIT). Silk.NET 2.23.0 exists as the alternative and is not needed. Direct3D11 with a flip-model swap chain is enough for a frame-pacing ground truth; no Direct3D12 required.

## Still open
License of `SharpGen.Runtime` / `SharpGen.Runtime.COM` (read from nuspec at restore time and added to `THIRD_PARTY.md`), trimming/self-contained behaviour, and the actual flip-model present-interval control — validated by the Stage 1a harness tests.
