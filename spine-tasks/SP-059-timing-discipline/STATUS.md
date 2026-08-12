## STATUS: SP-059 — Timing discipline in tests (convert, sweep, guard)
**Current Step:** Step 2
**Last Updated:** 2026-08-12 (worker, Step 1 complete pending review)
**Blockers:** none

### Step 1: sweep, classify, design + pre-approach consult — COMPLETE (plan review pending)
- [x] Update STATUS.md before starting work
- [x] Suite-wide sweep table (file:line → construct → what it waits for → class)
- [x] Classify every site (deterministic-convertible / tolerant-window-required / legitimately real-time)
- [x] Deliberate reproduction attempt of the named flake (or the honest cannot-reproduce record)
- [x] Design the approved wait helper (loud classifier), the guard rule, and any product seam (default: none)
- [x] Pre-approach solo consult (verdict + ACTUAL model in record.md)

### Step 2: convert the AI lab + close SP-041's registry gap — NOT STARTED
- [ ] `WaitForAsync`/`WaitForRecordAsync` converted; the 8000 ms literal gone (not enlarged)
- [ ] `IntakeServingTests.LoopbackServer` registered in the leaked-listener self-check
- [ ] Repeated local runs of the flaking test under the conversion recorded

### Step 3: suite-wide conversion + the guard — NOT STARTED
- [ ] Class-1 sites converted; class-2 sites on the helper; class-3 sites justified
- [ ] `ManualClock` duplication consolidated only if zero assertions/behavior change (else recorded why not)
- [ ] Guard test proven red-then-green against a re-introduced literal
- [ ] Exact `docs/constitution.md` sentence DRAFTED in record.md (file not edited)

### Step 4: ten consecutive full-suite green runs — NOT STARTED
- [ ] 10 runs, each captured to `evidence/run-NN.log`
- [ ] Run index table in record.md (pass/fail, unit + headless counts, duration)
- [ ] At least one cold run, with "cold" defined
- [ ] Floor discipline honored (862/33; any red named before discussion)

### Step 5: record + pre-completion consult — NOT STARTED
- [ ] record.md complete (incl. assertion-change proof and intended board filings)
- [ ] Pre-completion solo consult (verdict + ACTUAL model)
- [ ] STATUS.md accurate before .DONE

### Step 6: Testing & Verification — NOT STARTED
- [ ] Contract testCommand passes (verify.mjs 0, 0W/0E, ≥862 unit + ≥33 headless, TRX)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
