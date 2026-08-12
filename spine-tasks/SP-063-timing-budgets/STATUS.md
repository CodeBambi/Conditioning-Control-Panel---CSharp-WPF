## STATUS: SP-063 — Raise the injected test timeout budgets (owner decree)
**Current Step:** Step 1 — verify the preserved sweep, classify every site, pick the constant (IN PROGRESS)
**Last Updated:** 2026-08-12 (orchestrator, re-authored — wave 20, single lane)
**Blockers:** none

### Step 1: verify the preserved sweep, classify every site, pick the constant — IN PROGRESS
- [x] Update STATUS.md before starting work
- [x] Sweep greps re-run on the current tree; site table with population 1/2/3 + confirmed/corrected/new per row
- [x] Shared constant named + value justified (60 s default; finite, never `Timeout.InfiniteTimeSpan`)
- [x] Expected suite wall-clock impact stated (to be checked in Step 3)
- [x] Pre-approach solo consult scoped to EXECUTION (the decree is settled) — verdict + ACTUAL model

### Step 2: apply the raise + the one guard token — NOT STARTED
- [ ] Population 1 → shared constant; population 2 → short literals + `// wallclock-allow:` marker + pin; population 3 → assignment deleted
- [ ] `TestTimingGuardTests` gains the single option-assignment token + pins (no new fact; floor stays 892)
- [ ] Guard RED captured (injected unpinned budget), then removed
- [ ] No stale pins (SP-062 touched `AiProviderLab.cs`)

### Step 3: ten consecutive greens, one genuinely cold — NOT STARTED
- [ ] 10 consecutive full-suite runs, zero reds, zero unexpected skips, TRX attached, output redirected
- [ ] ≥1 fresh-checkout first-ever build; per-run table incl. skipped column
- [ ] `Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` green in EVERY run incl. cold
- [ ] Suite wall-clock not materially regressed

### Step 4: record + pre-completion consult — NOT STARTED
- [ ] record.md complete (decree verbatim, verified sweep, constant justification, guard RED, run table, intended filings)
- [ ] Honesty cell: a bigger number lengthens the fuse, it does not remove the time dependence
- [ ] Pre-completion solo consult (verdict + ACTUAL model)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification — NOT STARTED
- [ ] Contract testCommand passes (verify.mjs 0, 0W/0E, 892/35, 0 skipped, TRX)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

### Discoveries
- Batch `20260812T221746` was aborted mid-Step-1 by owner decree, not by failure. Its completed sweep + two non-reproducing cold runs are preserved under `prior-step1/` as INPUT to verify, never as evidence to cite.
