# Contributing

Thanks for helping. The most valuable contributions are **catalog entries** and **test fixtures from hardware we cannot test ourselves**.

## Ground rules
- Run `./build.ps1` before you open a change: it must finish with 0 warnings and all tests green.
- Product code must not name vendors or models (HC-12); knowledge goes in `catalogs/*.json`. A test enforces this.
- Product code must not use hooks, process-memory or driver APIs, or touch the network. Tests enforce this too; read [`MASTER_PROMPT_v4.md`](MASTER_PROMPT_v4.md) §5 for the hard constraints.
- Safety-relevant changes need a test that **fails without the change**.
- Sign off your commits with `git commit -s` (Developer Certificate of Origin). By contributing you agree your work is released under the MIT license.

## Catalog changes
Each entry needs a real `source` (URL or `observed:<where>`), today's `lastReviewed`, and an honest `verification` level (`observed`, `documented`, `community`, `unverified`). Do not guess names. See [`docs/CATALOGS.md`](docs/CATALOGS.md).

## Reporting a wrong diagnosis or a bug
Use the issue templates (arriving with the first release). Attach a redacted support bundle (Compatibility Report); never attach traces that contain usernames, hostnames, SSIDs or serial numbers.

## Security
Please report vulnerabilities privately. The contact address will be published in `SECURITY.md` before the first public release.
