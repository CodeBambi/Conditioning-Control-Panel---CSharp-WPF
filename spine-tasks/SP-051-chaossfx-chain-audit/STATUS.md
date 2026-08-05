## STATUS: SP-051 — ChaosSfx cue→chain audit + typed resolution
**Current Step:** complete — .DONE
**Last Updated:** 2026-08-05 (worker, contract green)
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
- [x] record.md (map, classification, design, consults, review presence)
- [x] Pre-completion solo consult (verdict + actual model in record.md)
- [x] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [x] Contract testCommand passes (verify.mjs exit 0 + rebuild 0W/0E + 669/33 green, floor 629/33; TRX loggers attached: ccp-tests-sp051.trx, ccp-headless-sp051.trx)
- [x] git diff --check clean
- [x] git status --short shows only File Scope paths
