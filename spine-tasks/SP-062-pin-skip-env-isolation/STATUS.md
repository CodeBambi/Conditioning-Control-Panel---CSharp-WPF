## STATUS: SP-062 — Loud skip + real process-env isolation for the SP-057 pin
**Current Step:** 1
**Last Updated:** 2026-08-12 (worker, Step 1 started)
**Blockers:** none
**Discoveries / File-Scope amendments:**
- 2026-08-12 (Step 1, worker): wave-19 base was RED (891 passed / 1 failed) — land commit f3a1192b flipped `tunnel`+`vendor` to `served` in `client/docs/upstream-payload-inventory.json` WITHOUT the `evidence` field SP-056's guard requires for served trees (`UpstreamPayloadInventoryTests.RealRepo_InventoryCoversEveryUpstreamPayloadTree`). Repaired as a documented File-Scope amendment (land-defect repair, commit 59fbcf1e); field content dictated verbatim by SP-061's landed record intended filing #3. Named here per FR-WORK-06; the 10-green measurement runs on the repaired base.

### Step 1: reproduce, verify the API, design the isolation + pre-approach consult — IN PROGRESS
- [x] Update STATUS.md before starting work
- [x] Deterministic repro of the cross-collection env leak, both directions, with observed counts
  - baseline run 1: 891/1 (the inherited inventory land defect above — repaired; NOT the pin, NOT the row-49 site)
  - probe 1 (cross-collection handshake): GREEN 2/2 in 65ms — observer in the default collection saw ProcessEnvCollection's held CCP_DATA_ROOT mid-flight ⇒ DisableParallelization does NOT serialize cross-collection on this runner (trx: sp062-probe1-cross.trx)
  - probe 2 (intra-collection handshake): RED 2/2, both 20s TestWait timeouts TIMING-VERDICT:CONDITION-NEVER-TRUE ⇒ tests in ONE collection never overlap ⇒ co-location is a REAL serialization fix on this runner (trx: sp062-probe2-intra.trx)
  - direction A (vacuous pass): pin filtered run with CCP_DATA_ROOT set process-wide, CURRENT code: Passed 1 / Skipped 0 — silent vacuous green proven (trx: sp062-dirA-prefix.trx)
- [x] Skip API verified against the PINNED xunit.v3 + runner versions (compile + observed run output)
  - pinned: xunit.v3 3.2.2 (xunit.v3.assert 3.2.2), runner xunit.runner.visualstudio 3.1.5 + MTP; binary reflection on the pinned assert DLL: Assert.Skip(String), SkipUnless(Boolean,String), SkipWhen(Boolean,String) + Xunit.Sdk.SkipException all exist; compile check via the probe build 0W/0E; observed skipped-output lands with the positive control (Step 2)
- [x] Full enumeration of process-wide-state / DisableParallelization dependencies with dispositions
  - swept both test projects: DisableParallelization ×1 (ProcessEnvCollection — REAL dependency, this fix); SetEnvironmentVariable ×1 class (DataRootOverrideEnvTests, CCP_DATA_ROOT — REAL, same fix); SetCurrentDirectory ×0; AppContext.SetSwitch ×0 (BaseDirectory reads are read-only); culture mutation ×0; mutable statics ×0 besides LoopbackListenerRegistry (ConcurrentDictionary, assembly-teardown assertion only — cleared, safe by construction)
- [ ] Isolation design + rejected alternatives + serialization cost, proven by the repro harness
- [ ] Pre-approach solo consult (verdict + ACTUAL model in record.md)

**Design (pre-consult):** co-locate `DataRootOverrideTests` into `ProcessEnvCollection` (intra-collection sequentiality = probe-2-proven) + `Assert.SkipWhen` loud skips at both checkpoints (reasons name CCP_DATA_ROOT + leak class); keep DisableParallelization as a non-relied-upon hint. Rejected: assembly-wide DisableTestParallelization (serializes 892 tests for one contested resource; baseline parallel suite 36s), trusting DisableParallelization (probe-1-falsified), product-side seam (out of scope; inverts dependency — test-scheduling defect, not product), cross-collection lock fixture (can't bind tests outside the collection), assert-unconditionally (banned false-RED, framing a). Serial cost ≈ 0: only the 11 pin/env tests serialize among themselves.

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
