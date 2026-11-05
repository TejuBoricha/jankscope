# Release Readiness & Go/No-Go Gate

This document serves as the formal checklist and decision ledger for releasing Jankscope (spec §19, §20).

---

## 1. Automated Quality Gates

| Gate | Requirement | Current Status | Evidence / Notes |
|---|---|---|---|
| **Build Warnings** | 0 warnings, warnings treated as errors | **PASS** | Enforced by `Directory.Build.props` (`TreatWarningsAsErrors=true`). |
| **Test Suite** | 100% pass across all unit and integration tests | **PASS** | CI run `41867073761` on `main`: 2,544 passed, 0 failed, 4 skipped (the real-PresentMon tests skip on the headless runner). Reference PC per `CONTEXT.md`: 2,547 passed, 1 skipped (symlink privileges). |
| **Banned APIs** | 106 rules proven to fire in test suite | **PASS** | Verified by `tools/dev/verify-banned-symbols.ps1`. |
| **Analyzer Wiring** | Analyzer verified active on all product projects | **PASS** | Verified by `tools/dev/verify-analyzer-wiring.ps1`. |
| **Code Hygiene** | ASCII-only C# code, valid UTF-8, no mojibake | **PASS** | Enforced by `RepoLintTests`. |
| **Overhead Bound** | Observer overhead within the spec section 14 budgets (median `Present()` error < 0.200 ms, p99 < 0.500 ms, median frame time < 0.100 ms) | **PASS** | Measured deltas +0.009 ms, -0.012 ms and +0.002 ms across 10 interleaved runs (`docs/benchmarks/overhead-v1.md`). |
| **Supply Chain** | SBOM generated, zero unvetted dependencies | **PASS** | CI workflow generates CycloneDX SBOM; licenses verified in `docs/THIRD_PARTY.md`. |

---

## 2. Acceptance Criteria Status (Spec §19)

- **Tier: MVP (Stages 0–2):**
  - Stage 0 Bootstrap: **100% Verified**
  - Stage 1a Headless Core: **100% Verified**
  - Stage 1b GUI & Capture: **100% Implemented & Reviewed**
  - Stage 2 Input & Network Labs: **In Planning (Test Plan & ADR Created)**
- **Tier: v1.0 (Stages 3–5):**
  - Planned for subsequent release cycle.

---

## 3. Owner Go / No-Go Decision Ledger

Before tagging the first public version (v1.0 General Availability), the repository owner must record formal sign-off on the items below:

| # | Decision Item | Owner Sign-Off | Date |
|---|---|---|---|
| 1 | Trademark search for "Jankscope" cleared with zero conflicts | [ ] Approved | Pending |
| 2 | Official GitHub repository handle reserved | [X] Approved | 2026-11-04 (https://github.com/TejuBoricha/jankscope) |
| 3 | Code signing certificate obtained (SignPath Foundation / Azure Artifact Signing) | [ ] Approved | Stage 5 |
| 4 | GitHub Sponsors account active and linked to bank account | [ ] Approved | Pending |
| 5 | Legal review completed on Privacy Notice (`docs/PRIVACY.md`) | [ ] Approved | Pending |
| 6 | Final Go / No-Go public release approval | [ ] Approved | Stage 5 |
