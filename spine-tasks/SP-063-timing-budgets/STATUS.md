## STATUS: SP-063 — Timing discipline part 2: injected timeout BUDGETS, not waits
**Current Step:** not started
**Last Updated:** 2026-08-12 (orchestrator, authored — wave 20, single lane)
**Blockers:** none

### Step 1: reproduce cold, define the fourth class, sweep, design + pre-approach consult — NOT STARTED
- [ ] Update STATUS.md before starting work
- [ ] Cold reproduction attempt (fresh worktree, first-ever build) with honest attempt counts; mechanism read from the provider's classification order
- [ ] Fourth-class definition (budget that decides an outcome vs budget whose elapsing IS the subject)
- [ ] Suite-wide enumeration of injected budgets with per-site dispositions incl. cleared sites
- [ ] Design: guard extension + deterministic fix + rejected alternatives (product-code justification first if needed)
- [ ] Pre-approach solo consult (verdict + ACTUAL model in record.md)

### Step 2: implement — guard extension, sweep dispositions, deterministic fix — NOT STARTED
- [ ] `TestTimingGuardTests` extended to option-assignment budgets under the pinned-allowlist discipline
- [ ] Guard BITE demonstrated with a captured RED (injected unpinned budget), then removed
- [ ] Sweep dispositions applied (markers + pins for legitimate budgets; time dependence removed elsewhere)
- [ ] Named site fixed deterministically — 800 ms NOT raised, no assertion weakened
- [ ] Exact unit/headless counts stated and stable

### Step 3: ten consecutive greens, one genuinely cold — NOT STARTED
- [ ] 10 consecutive full-suite runs, zero reds, zero unexpected skips, TRX attached, output redirected
- [ ] ≥1 fresh-checkout first-ever build; per-run table with counts and skipped column
- [ ] Named site green in EVERY run including the cold one
- [ ] Run table + TRX committed under evidence/

### Step 4: record + pre-completion consult — NOT STARTED
- [ ] record.md complete (repro honesty, class definition, sweep table, guard RED demo, fix + alternatives, run table, intended filings)
- [ ] Honesty cell: indirection the token surface can miss
- [ ] Pre-completion solo consult (verdict + ACTUAL model)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification — NOT STARTED
- [ ] Contract testCommand passes (verify.mjs 0, 0W/0E, stated counts, 0 skipped, TRX)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
