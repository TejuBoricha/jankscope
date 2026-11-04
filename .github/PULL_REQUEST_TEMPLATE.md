## Summary

<!-- Concise description of what this PR changes and why. -->

## Related Issue(s)

<!-- Link the issue(s) this PR resolves or relates to, e.g. `Closes #12` or `Relates to #15` (or `N/A` if none). -->

## Change Type

- [ ] `feat` — New diagnostic capability, collector, or UI feature
- [ ] `fix` / `hotfix` — Bug fix or review-finding remediation
- [ ] `docs` — Documentation, ADR, benchmark ledger, or review report
- [ ] `test` — New unit, integration, fuzz, or mutation-proof tests
- [ ] `ci` / `chore` — Build scripts, GitHub Actions, catalog data, or dependency maintenance
- [ ] `refactor` — Internal restructuring with no behavior change

## Hard-Constraint & Verification Checklist

- [ ] Branch follows `<type>/<short-kebab-slug>` naming and is rebased/up-to-date with `main`
- [ ] `.\build.ps1` passes locally with **0 warnings** and **0 errors**
- [ ] Banned-symbol coverage proof (`tools/dev/verify-banned-symbols.ps1`) and MSBuild analyzer wiring proof (`tools/dev/verify-analyzer-wiring.ps1`) pass
- [ ] `RepoLintTests` passes (no unapproved `[LibraryImport]`, strictly ASCII `.cs`/`.ps1` files, no hardcoded vendor strings outside `catalogs/`, `docs/THIRD_PARTY.md` matches `Directory.Packages.props`)
- [ ] Every new safety-critical rule, redactor pattern, or export guard has been proven able to fail via a mutation check
- [ ] `docs/STATUS.md`, `docs/LIMITATIONS.md`, and `CONTEXT.md` updated if applicable
