# Testing and review strategy

Spec §18, plus the owner's standing request (2026-10-16): **code review and testing at every stage**.

## Run everything
```
./build.ps1            # restore, build (0 warnings required), all tests, CLI smoke run
./build.ps1 -Clean     # same from scratch
```
Individual projects: `dotnet test tests/Jankscope.Core.Tests -c Release` (portable, runs anywhere) and `dotnet test tests/Jankscope.Platform.Tests -c Release` (Windows probes, behaviour checked against the real machine).

## Layers
| Layer | Where | What it proves |
|---|---|---|
| Unit | `Jankscope.Core.Tests` | Metric math, provenance rules, capability invariants, HC-1 decision table, ETW classification, catalog validation (positive and negative) |
| Static lints over the repo | `RepoLintTests` | No hooks/injection/process-memory/driver-loading APIs (acceptance #13), no hardcoded vendor names (#17), no network or process-launching APIs in product code, .NET 10 only, every package recorded in `THIRD_PARTY.md` |
| Platform behaviour | `Jankscope.Platform.Tests` | Probe isolation (a throwing probe becomes a row), derived capabilities (frame capture, optimize-mode gate), Windows 11 detection by build, elevation matches the token, real-time ETW session cleanup, anti-cheat logic with synthetic catalogs, regression tests for bugs found by running on the bench |
| End-to-end smoke | `build.ps1` | The built CLI runs and emits a parseable capability matrix |

Hardware-dependent assertions are structural (no fixed expectations about a particular GPU, battery or vendor), so the suite runs on any Windows PC or VM. Tests that need hardware will be tagged and skipped with a reason when it is absent.

## Tests must be able to fail
Safety-critical rules get a **mutation check**: plant a violation, confirm the test fails, remove it. Stage 0 result (2026-10-16): a file containing a vendor name, a hook API and `HttpClient` failed all three lints; the file was removed. Every bug found by running on the bench gets a regression test (e.g. the MDM false positive).

## Code review gate (every stage)
1. Self-review and simplification pass.
2. **Independent review** by a separate reviewer with fresh context (security-sensitive code additionally by a dedicated security reviewer), recorded in `docs/reviews/stage-N.md`: findings, severity, and for each one *fixed* (with commit/test reference), *deferred* (with reason and owner) or *rejected* (with reasoning).
3. No stage is marked `done-verified` while a Critical or High finding is open.

## Not covered yet
IPC, helper, rollback/journal, UI, input and network collectors, real-frame capture, soak/overhead measurement — all later stages (see `STATUS.md`).
