# ADR 0002 — Process model and IPC

**Status:** Accepted (design; implemented from Stage 1b/3) · **Spec:** §6.1, HC-3

## Context
Collection must keep running when the UI closes, rendering must never sit on the capture path, and anything needing admin rights must be small, separate and explicit. A game (same user, possibly high priority) must not be able to drive the privileged component.

## Decision
Three components:
1. **Engine** — headless, non-elevated; collectors, ring buffers, storage. Started on demand by the UI; optional opt-in per-user autostart (default off) enables automatic game-launch monitoring. No Windows service in v1.
2. **UI** — separate process (WPF). Talks to the Engine over a named pipe: created by the Engine with `FILE_FLAG_FIRST_PIPE_INSTANCE`, ACL limited to the current user's SID; each client verified by PID plus process creation time, image path equal to the installed UI executable, and a per-launch secret; every message schema-validated.
3. **Elevated helper** — optional, signed, started per use through a UAC prompt, random-named pipe, **one client: the Engine** (PID and creation time equal the launcher's). Fixed typed commands only; each mutating command needs a single-use approval token bound to action id, parameter hash, nonce and a short expiry; no strings reach a shell; runs only from an admin-writable install path; exits when idle.

Trust boundary (stated in `SECURITY.md`): a same-user process is **inside** the boundary for user-level actions (it could write the same registry keys itself). For **elevated** actions the boundary is the UAC prompt plus the single-use token, so a spoofed UI client can at worst cause a visible, unexpected UAC prompt.

## Alternatives rejected
| Option | Why not |
|---|---|
| Always-on Windows service | Persistent privileged surface; violates least privilege and the no-always-on decision (§2.2 #4). |
| Single process with in-process elevation | Cannot elevate a running process; a compromised UI would hold admin rights. |
| COM / RPC endpoint | Larger attack surface and harder to restrict to one verified client than a first-instance named pipe. |
| ACL-only pipe security | A same-user game process could connect; ACL cannot distinguish processes of one user. |

## Consequences
Two integration tests are required before Stage 3 exits (acceptance #28): wrong PID/creation time/image/secret is refused, and a spoofed client cannot cause an elevated action without UAC and a valid token. Portable builds refuse elevation.

## Verification
Not implemented in Stage 0 (no IPC code exists). Tracked in `docs/STATUS.md` as not started.
