# Compatibility matrix

Honest **tested / expected / untested** table (spec §3.6). Physical-hardware, cloud-VM and untested rows are never merged.

## Tested (physical hardware)
| Configuration | What was run | Result | Date |
|---|---|---|---|
| Reference bench: Windows 11 Home Single Language, build 26300.9711, x64, Intel + NVIDIA hybrid laptop, admin account in a non-elevated shell, member of Performance Log Users, Smart App Control off, Secure Boot on, boot-start anti-cheat driver present | Full build + 141 automated tests (115 Core, 24 Platform, 2 Frames; 108 at the first clean-copy build), `jankscope probe` | Passed. Probe rows match independently read facts (build, ETW session count, Smart App Control, anti-cheat services) | 2026-10-16 |

## Tested (cloud VM)
None yet (P1).

## Expected to work (not tested)
Windows 11 on other x64 PCs (the probes use documented APIs and report "unknown" rather than guess) · Windows 10 22H2 (best-effort via feature detection; the Windows 10 path is untested).

## Untested
AMD-only and NVIDIA desktop systems · AMD+NVIDIA laptops · Intel iGPU-only · handhelds · Windows 10 · non-English display languages · true standard-user accounts · managed/domain/MDM-enrolled PCs · virtual machines · ARM64 · HDD systems and ≤ 4-core CPUs · multi-monitor mixed-refresh · Windows Server and S mode (should be detected and explained, never crash).

## Recommended external beta matrix
NVIDIA desktop · AMD desktop · AMD+NVIDIA laptop · Intel iGPU-only or handheld · Windows 10 22H2 · non-English locale · standard-user account · managed PC.
