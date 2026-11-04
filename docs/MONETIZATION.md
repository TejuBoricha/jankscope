# Monetization & Funding Model

Jankscope adheres to strict ethical guardrails regarding funding, advertising, and user trust. This document outlines the rationale behind our funding model (ADR 0004) and records platform comparisons.

---

## 1. Ethical Guardrails (HC-13)

Modern "PC booster" and "game optimizer" software is notorious for predatory monetization: bundling bloatware, displaying deceptive "PC is infected" warnings, selling snake-oil registry tweaks, and collecting personal browsing telemetry.

Jankscope explicitly rejects these practices:
1. **Zero Advertisements:** No ad banners, interstitials, promoted recommendations, or affiliate product links.
2. **Zero Commercial Bias:** Financial contributions never influence diagnostic findings, confidence grades, or recommendation priority.
3. **Identical Open-Source Core:** The complete source code is released under the permissive MIT license on GitHub. Free builds have zero artificial limitations or feature locks.

---

## 2. Funding Pathways

### Pathway A: Voluntary Sponsorship (GitHub Sponsors)
- **Primary Mechanism:** Individual developers and gamers who find value in Jankscope can sponsor the project directly on GitHub.
- **Fees & Structure:** GitHub Sponsors charges 0% platform fee for individual developer sponsorships, maximizing the portion directed to project maintenance.
- **In-App Presence:** Displayed only on the "About and privacy" page as an optional static link. The application never fetches or pre-queries the URL; it opens in the default web browser only on an explicit user click. The link is completely hidden during active game sessions.

### Pathway B: Optional Microsoft Store Convenience Edition (Stage 5)
- **Mechanism:** An optional paid listing on the Microsoft Store (priced around $8–$10).
- **Value Proposition:** Automatic updates via Windows Store infrastructure, easy multi-device deployment, and seamless uninstallation for non-technical users.
- **Parity:** Feature-identical to the free GitHub release.

---

## 3. Donation Platform Evaluation

| Platform | Fee Structure | Regional Availability (India / Global) | Payout Method | Verdict |
|---|---|---|---|---|
| **GitHub Sponsors** | 0% platform fee for individuals | Full support (Stripe Express) | Direct bank transfer | **Recommended primary platform** (ADR 0004). |
| **Ko-fi** | 0% platform fee on donations (5% on memberships) | Broad global support | PayPal / Stripe | Viable secondary option. |
| **Open Collective** | 10% fee (fiscal host) + payment processing | Global (Open Source Collective) | PayPal / Bank wire | Better suited for multi-maintainer legal entities. |
| **Patreon** | 5%–12% platform fee + processing | Global support | PayPal / Payoneer | High fee overhead for pure software sponsorship. |
