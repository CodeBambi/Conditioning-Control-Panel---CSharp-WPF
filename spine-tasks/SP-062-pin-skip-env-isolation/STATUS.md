## STATUS: SP-062 — Loud skip + real process-env isolation for the SP-057 pin
**Current Step:** not started
**Last Updated:** 2026-08-12 (orchestrator, authored — wave 19, single lane)
**Blockers:** none

### Step 1: reproduce, verify the API, design the isolation + pre-approach consult — NOT STARTED
- [ ] Update STATUS.md before starting work
- [ ] Deterministic repro of the cross-collection env leak, both directions, with observed counts
- [ ] Skip API verified against the PINNED xunit.v3 + runner versions (compile + observed run output)
- [ ] Full enumeration of process-wide-state / DisableParallelization dependencies with dispositions
- [ ] Isolation design + rejected alternatives + serialization cost, proven by the repro harness
- [ ] Pre-approach solo consult (verdict + ACTUAL model in record.md)

### Step 2: implement — loud skip + isolation — NOT STARTED
- [ ] Silent `return` → loud skip naming CCP_DATA_ROOT at both checkpoints; assertion strictness unchanged
- [ ] Isolation fix implemented; normal-run skip path unreachable
- [ ] Sweep findings applied or dispositioned per site
- [ ] Positive control captured: `891 passed / 1 skipped` with the reason visible
- [ ] No new deadline literals / injected budgets; timing guard green

### Step 3: ten consecutive greens, one genuinely cold — NOT STARTED
- [ ] 10 consecutive full-suite runs, zero reds, TRX attached, output redirected to files
- [ ] ≥1 fresh-checkout first-ever build (rebuild-in-place does not count)
- [ ] Every clean run 892 passed / 0 skipped + 35 headless
- [ ] Row-49 site occurrences recorded (run #, cold/warm, TRX name) — untouched
- [ ] Run table + TRX artifacts committed under evidence/

### Step 4: record + pre-completion consult — NOT STARTED
- [ ] record.md complete (repro, API verification, enumeration, design, positive control, run table, limits, intended filings)
- [ ] Honesty cell: what this task does NOT prove
- [ ] Pre-completion solo consult (verdict + ACTUAL model)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification — NOT STARTED
- [ ] Contract testCommand passes (verify.mjs 0, 0W/0E, 892/35, 0 skipped, TRX)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
