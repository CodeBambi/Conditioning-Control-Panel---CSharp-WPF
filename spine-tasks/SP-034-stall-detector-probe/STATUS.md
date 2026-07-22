## STATUS: SP-034 — Stall-detector probe tooling
**Current Step:** Step 4 — verification (IN PROGRESS)
**Last Updated:** 2026-07-22 (step 3 complete: manifest applied/verified/idempotent; record.md complete with post-consult fixes; pre-completion consult recorded)
**Blockers:** none

### Step 1: evidence + design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Threshold consolidation from both incidents (cited)
- [x] Design (classification state machine, windows, scoping, output contract, self-test shape)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

**Step 1 Status: COMPLETE** (review: skipped nested_spawn_blocked — engine reviews after .DONE)

### Step 2: probe script + self-test
- [x] Tools/spine-worker-probe.ps1 (classification, evidence numbers, T-10 template)
- [x] Self-test (live batch no false wedge; wedged simulation; crawling simulation)
- [x] Skill-template amendment authored (manifest patch entry)

**Step 2 Status: COMPLETE** (live: alive-progressing exit 0; wedged sim: wedged exit 2 + T-10; crawl sim: alive-crawling exit 1; review: skipped nested_spawn_blocked)

### Step 3: manifest patch + apply/verify + docs
- [x] Manifest entry applied + verified (idempotent, loud-on-drift)
- [x] record.md complete (script reproduced)
- [x] Pre-completion solo consult
- [x] STATUS.md accurate

**Step 3 Status: COMPLETE** (pre-completion consult: 2 gaps adopted + fixed — per-pid clamp, multi-lane note; re-runs PASS; §6 regenerated)

### Step 4: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; ≥446/29 floor, no drift)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
