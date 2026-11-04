# Owner checklist — Jankscope

Things only you can do (accounts, identity checks, payments, publishing, legal). **None of them blocks Stages 0–4.** Details and rationale: `MASTER_PROMPT_v4.md` §2.3 and §4.1. Facts below were researched on 2026-10-16 and are logged as "verify" items — re-check each page when you reach the step.

## When you are ready to go public (Stage 5)
| # | Step | Cost | Notes |
|---|---|---|---|
| 1 | Create or pick a GitHub account and publish the repository. | Free | **Done 2026-11-04:** Published at `https://github.com/TejuBoricha/jankscope` with branch protection, GitHub Flow governance, Private Vulnerability Reporting, and Dependabot alerts enabled. |
| 2 | Run a **formal trademark search** for "Jankscope" — USPTO, EUIPO TMview, India IP portal (ipindiaonline.gov.in). Optionally ask a lawyer or legal-aid clinic for a one-hour review of the name, the disclaimer and the anti-cheat wording. | Free search; consult optional | My web check found no hits, but that is **not** trademark clearance. If it fails, the backups are HitchLens and StutterLens; renaming is a branding-config change. |
| 3 | Enable **GitHub Sponsors** on the account. | Free (personal sponsorships have no GitHub fee) | Needs a bank account and tax residence in India, plus an identity check. Set one-time and monthly tiers with social perks only. |
| 4 | Ask me for the release package; **approve each public action** (create repo, push, tag, release). | — | I prepare everything in `dist/` and the docs; I will ask for a one-line yes per action. |
| 5 | Apply to **SignPath Foundation** for free OSS code signing, after the first beta. | Free | Needs a public repo, an automated CI build, a code-signing policy page, MFA, and a reputation they judge themselves. Until then ship "unsigned preview" builds with hashes. |
| 6 | Optional: **Microsoft Store** individual developer registration for the paid convenience edition. | Free (reported) | Identity verification with ID and selfie. Microsoft signs store MSIX packages. Check revenue share, India payout and tax forms in Partner Center before relying on income. |
| 7 | Optional: a domain. | ~yearly fee | Not needed; GitHub Pages works. |
| 8 | Talk to a **chartered accountant** before accepting money. | — | Earnings are taxable income; I give no tax advice. |

## Optional, any time
- **Real standard Windows account for testing** (otherwise I use restricted-token runs, labeled as simulation). In an *elevated* terminal:
  ```
  net user JankTest * /add
  ```
  It prompts for a password (not stored anywhere). Remove it later with `net user JankTest /delete`.
- **Beta testers:** I will draft recruitment posts in `docs/BETA_PLAN.md`; you post them. Target: an NVIDIA desktop, an AMD desktop, an Intel-iGPU laptop or handheld, a Windows 10 PC and a non-English-locale PC.

## What I will not do for you
Create accounts, enter payment or identity details, accept platform terms, post publicly, push to a remote, or give legal or tax advice.
