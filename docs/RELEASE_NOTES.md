# Release Notes

All notable changes to Jankscope are documented in this file. Jankscope follows [Semantic Versioning](https://semver.org/).

> [!NOTE]
> In accordance with our honesty and transparency standards (spec §4.2), any change to diagnostic heuristics, baseline thresholds, or confidence scoring rules is documented here with an explanation of why the verdict or grade calculation changed.

---

## [0.1.0-preview] - 2026-11-04 (Stage 1b Developer Preview)

### Added
- **Diagnostic Core & Capability Matrix (Stage 1a):**
  - 14 automated system probes evaluating 41 hardware, operating system, security, and virtualization capability rows.
  - Clean integration with Intel PresentMon 2.6.0 (hash-verified, arguments allowlist, user-mode ETW).
  - High-performance ring buffer storing up to 32,768 frame samples.
  - Moving-window stutter detection isolating spikes exceeding $3.0\times$ baseline median.
  - Direct3D 11 synthetic frame harness for zero-risk diagnostic validation.
- **Desktop Graphical Interface (Stage 1b):**
  - Unelevated modern WPF desktop interface with dark, light, and Windows theme support.
  - Live 30-second frame pacing timeline with interactive zoom and visual stutter indicators.
  - Global hotkeys (`Ctrl+F11` for stutters, `Ctrl+F12` for input stalls) with non-intrusive audio feedback.
  - Incident Investigator with automated candidate anchor detection and manual timeline placement.
- **Privacy & Export Security:**
  - Automated redaction of usernames, machine names, IP addresses, MAC addresses, and command lines.
  - Byte-exact visual export preview prior to writing any report to disk.
  - Independent `ExportGuard` validation engine ensuring no sensitive data escapes to exported JSON.
- **Governance & Engineering Infrastructure:**
  - Strict .NET 10 LTS toolchain pinned via `global.json`.
  - Comprehensive banned-API Roslyn analyzer (106 rules) and regex repo lints enforcing zero code injection and zero memory reads.
  - GitHub Flow branching strategy and robust CI pipeline.
