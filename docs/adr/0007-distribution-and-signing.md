# ADR 0007 — Distribution and code signing

**Status:** Proposed (final decision in Stage 5) · **Spec:** §4.2, §16, §2.3

## Context
Smart App Control blocks unsigned apps outright when on, and SmartScreen reputation applies to downloads. The owner is in India and no signing identity exists. The product is open source (MIT) and free.

## Options for signing
| Option | Facts | Verdict |
|---|---|---|
| Azure Artifact Signing | Individuals only in US/Canada; Indian organizations not listed (FACTS F18) | Excluded unless eligibility changes — re-check at Stage 5. |
| **SignPath Foundation (free OSS)** | OSI license, verifiable automated CI build, public code-signing policy page, MFA, manual approval per release; project needs a "verifiable reputation" at their discretion (FACTS F19) | **Preferred** — apply after the first beta. |
| **Microsoft Store MSIX** | Free individual registration; Microsoft signs store packages (FACTS F20); packaged-app elevation for the helper unverified (F22) | **Parallel route**; if packaging blocks a capability the store edition ships without it and says so. |
| Purchased OV/open-source certificate | e.g. Certum kit (~€69 + token, India availability unverified, FACTS F24) | Fallback; any spend is the owner's decision. |
| Unsigned | SmartScreen warning; blocked on Smart App Control systems | Allowed for early "unsigned preview" GitHub releases only, with SHA-256 hashes and a documented warning. |

## Channels
GitHub Releases (installer, portable zip, checksums, SBOM) is primary; a winget manifest and the Store listing are evaluated (rules to verify); never third-party download portals that bundle installers. Release builds come from an automated, verifiable CI workflow (also a SignPath requirement) with MFA on the repository and signing accounts. Portable builds refuse elevation (ADR 0002).

## Decision timing
Decided at Stage 5 with the then-current facts; Stage 0–4 builds stay unsigned (Smart App Control is off on the bench).

## Owner-only
Accounts, identity checks and any purchase (`OWNER_CHECKLIST.md`).
