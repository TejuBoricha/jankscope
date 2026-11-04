## Summary

<!-- Describe what this PR changes and why. -->

## Hard-Constraint & Verification Checklist

- [ ] `.\build.ps1` passes locally with **0 warnings** and **0 errors**
- [ ] Banned-symbol coverage proof (`tools/dev/verify-banned-symbols.ps1`) and analyzer wiring proof (`tools/dev/verify-analyzer-wiring.ps1`) pass
- [ ] `RepoLintTests` passes (no unapproved `[LibraryImport]`, no non-ASCII bytes in `.cs`/`.ps1`, no hardcoded vendor strings outside `catalogs/`, `THIRD_PARTY.md` matches `Directory.Packages.props`)
- [ ] Every new safety-critical rule or redactor/guard pattern has been verified to fail when mutated
- [ ] `docs/STATUS.md` and `docs/LIMITATIONS.md` updated if applicable
