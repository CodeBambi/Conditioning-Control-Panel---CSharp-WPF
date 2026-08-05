## STATUS: SP-051 — ChaosSfx cue→chain audit + typed resolution
**Current Step:** Step 3 — evidence consolidation + pre-completion consult
**Last Updated:** 2026-08-05 (worker, Step 2 complete)
**Blockers:** none
**Discoveries:** (1) Pre-audit drift removed in Step 2: greenfield substituted `chime1.mp3` for the `wave_clear` chain and `Pop2.mp3` for the `ripple_cast` chain — neither is a chain member. Per the audit binary (chain OR named gap) + pre-approach consult ruling, both become typed named gaps; `wave_clear`/`ripple_cast` go SILENT in the greenfield until the WPF chaos sound-library content row lands. User-observable behavior change, surfaced for the owner. (2) Page-sent `detonate_thud`/`dive` are absent even from the WPF library — silent in WPF too. (3) Solo consult route on this laptop answers with anthropic/claude-fable-5 (bpx-consult.json).

### Step 1: map + classification + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Complete cue→chain enumeration (every WPF chain, File.cs:line)
- [x] Classify per cue (resolvable vs named content gap)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: typed chain resolution + tests
- [x] Resolution layer handles audited chains typed (chain target + scale kept; gaps typed + recorded)
- [x] Tests pin every resolved chain + every named gap
- [x] Complete table in record.md

### Step 3: evidence consolidation + pre-completion consult
- [ ] record.md (map, classification, design, consults, review presence)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥629/33 floor; TRX logger)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
