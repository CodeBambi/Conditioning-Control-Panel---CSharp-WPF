## STATUS: SP-045 — DtrhFxRouterTests ManualClock hygiene
**Current Step:** Step 1 — verify + inject + consult
**Last Updated:** 2026-08-04 (worker, Step 1 in progress)
**Blockers:** none

### Step 1: verify + inject + consult
- [x] Update STATUS.md before starting work
- [ ] Verify every construction in the file; inject ManualClock class-wide (SP-043 shape)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: green + zero-wall-clock grep + evidence + pre-completion consult
- [ ] Full DTRH test classes green; zero assertion changes (grep-proven)
- [ ] Zero-wall-clock grep over the file
- [ ] record.md (constructions found + injected, consults, review presence)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 3: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + counts EXACTLY 564/29)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
