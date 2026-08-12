## STATUS: SP-059 — Timing discipline in tests (convert, sweep, guard)
**Current Step:** complete
**Last Updated:** 2026-08-12 (worker, all steps complete — .DONE)
**Blockers:** none

### Step 1: sweep, classify, design + pre-approach consult — COMPLETE
- [x] Update STATUS.md before starting work
- [x] Suite-wide sweep table (file:line → construct → what it waits for → class)
- [x] Classify every site (deterministic-convertible / tolerant-window-required / legitimately real-time)
- [x] Deliberate reproduction attempt of the named flake (or the honest cannot-reproduce record)
- [x] Design the approved wait helper (loud classifier), the guard rule, and any product seam (default: none)
- [x] Pre-approach solo consult (verdict + ACTUAL model in record.md)

### Step 2: convert the AI lab + close SP-041's registry gap — COMPLETE
- [x] `WaitForAsync`/`WaitForRecordAsync` converted; the 8000 ms literal gone (not enlarged)
- [x] `IntakeServingTests.LoopbackServer` registered in the leaked-listener self-check
- [x] Repeated local runs of the flaking test under the conversion recorded

### Step 3: suite-wide conversion + the guard — COMPLETE
- [x] Class-1 sites converted; class-2 sites on the helper; class-3 sites justified
- [x] `ManualClock` duplication consolidated only if zero assertions/behavior change (else recorded why not)
- [x] Guard test proven red-then-green against a re-introduced literal
- [x] Exact `docs/constitution.md` sentence DRAFTED in record.md (file not edited)

### Step 4: ten consecutive full-suite green runs — COMPLETE
- [x] 10 runs, each captured to `evidence/run-NN.log`
- [x] Run index table in record.md (pass/fail, unit + headless counts, duration)
- [x] At least one cold run, with "cold" defined
- [x] Floor discipline honored (862/33; any red named before discussion)

### Step 5: record + pre-completion consult — COMPLETE
- [x] record.md complete (incl. assertion-change proof and intended board filings)
- [x] Pre-completion solo consult (verdict + ACTUAL model)
- [x] STATUS.md accurate before .DONE

### Step 6: Testing & Verification — COMPLETE
- [x] Contract testCommand passes (verify.mjs 0, 0W/0E, ≥862 unit + ≥33 headless, TRX)
- [x] `git diff --check` clean
- [x] `git status --short` shows only File Scope paths
