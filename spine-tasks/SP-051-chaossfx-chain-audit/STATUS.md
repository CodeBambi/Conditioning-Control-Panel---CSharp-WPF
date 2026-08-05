## STATUS: SP-051 — ChaosSfx cue→chain audit + typed resolution
**Current Step:** Step 1 — complete cue→chain map + gap classification + pre-approach consult
**Last Updated:** 2026-08-05 (authored)
**Blockers:** none

### Step 1: map + classification + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] Complete cue→chain enumeration (every WPF chain, File.cs:line)
- [ ] Classify per cue (resolvable vs named content gap)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: typed chain resolution + tests
- [ ] Resolution layer handles audited chains typed (chain target + scale kept; gaps typed + recorded)
- [ ] Tests pin every resolved chain + every named gap
- [ ] Complete table in record.md

### Step 3: evidence consolidation + pre-completion consult
- [ ] record.md (map, classification, design, consults, review presence)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥629/33 floor; TRX logger)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
