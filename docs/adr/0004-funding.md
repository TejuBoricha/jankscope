# ADR 0004 — Funding: donations and an optional paid store edition, no ads

**Status:** Accepted (2026-10-16, owner decision plus agent research) · **Spec:** §4.1, HC-13

## Context
The owner wants to earn from the product without harming user trust. The product is a local, no-telemetry diagnostics tool, so income must not depend on data, advertising or influencing findings. The owner is in India.

## Decision
1. **GitHub Sponsors** is the primary stream: India is a supported region (FACTS F16), personal-account sponsorships carry no GitHub fee, one-time and monthly tiers, social perks only.
2. **Optional paid "convenience edition" in the Microsoft Store** (P2): identical source and features, free from GitHub; Microsoft signs store packages (FACTS F20). Precedent: the Files app (FACTS F23).
3. Ko-fi (tips) and UPI are optional after verification (FACTS F25).
4. The app shows at most a "Support the project" control on Support & About; the URLs are static and never fetched (HC-13, acceptance #22–#23).

## Alternatives rejected
Third-party ads (privacy and trust cost, sandboxing complexity, restricted desktop placement), pay-per-install offers, selling data or telemetry, vendor-paid sponsorship of findings (breaks vendor neutrality), feature-gating behind payment, relicensing as source-available, Open Collective (10% host fee plus processing) and Patreon-style memberships (overhead).

## Consequences
Income will likely be small and irregular; running cost stays near zero because there is no backend. Owner-only steps: GitHub account, Sponsors enrollment (bank, tax residence, identity check), optional Partner Center registration, accountant for income handling. Open facts: payout and withdrawal fees, Store revenue share and payout to India, whether a packaged app can launch the elevated helper (FACTS F16, F21, F22).

## Verification
`RepoLintTests` forbid network APIs in product code; acceptance #21–#23 will add a static check that no ad SDK or web view exists.
