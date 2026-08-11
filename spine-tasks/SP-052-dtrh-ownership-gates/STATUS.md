## STATUS: SP-052 — DTRH run-setup ownership gates (b4 parity defects)
**Current Step:** Step 2 — the two gates + tests
**Last Updated:** 2026-08-05 (worker)
**Blockers:** none

### Step 1: drift verification + design + pre-approach consult (COMPLETE — engine plan review absent per SP-195, spawnFailed=false)
- [x] Update STATUS.md before starting work
- [x] Re-verify every drift line against git main (clamp sites, endless points, habit exclusion)
- [x] Design (ownership-gated ceiling both points; Endless additive member + five-point carry)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: the two gates + tests (IN PROGRESS)
- [ ] Hourglass ownership-gated ceiling (persist + deal)
- [ ] Endless end-to-end (member, gated persist, carries, re-check, habit exclusion)
- [ ] Unit tests (clamp matrix; five endless points; b4 test updates cited, none weakened)

### Step 3: headed round-trips + evidence + pre-completion consult
- [ ] Owner >20min setup survives persist, deals ≥1201s (non-owner still 1200)
- [ ] Owner endless:true reaches rc.endless
- [ ] record.md (verification, design, consults, review presence, transcripts)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥669/33 floor; TRX logger)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
