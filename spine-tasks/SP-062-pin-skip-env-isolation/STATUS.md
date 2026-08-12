## STATUS: SP-062 — Loud skip + real process-env isolation for the SP-057 pin
**Current Step:** 4
**Last Updated:** 2026-08-12 (worker, Step 3 complete — 10/10 consecutive greens incl. 1 cold first-ever build)
**Blockers:** none
**Discoveries / File-Scope amendments:**
- 2026-08-12 (Step 1, worker): wave-19 base was RED (891 passed / 1 failed) — land commit f3a1192b flipped `tunnel`+`vendor` to `served` in `client/docs/upstream-payload-inventory.json` WITHOUT the `evidence` field SP-056's guard requires for served trees (`UpstreamPayloadInventoryTests.RealRepo_InventoryCoversEveryUpstreamPayloadTree`). Repaired as a documented File-Scope amendment (land-defect repair, commit 59fbcf1e); field content dictated verbatim by SP-061's landed record intended filing #3. Named here per FR-WORK-06; the 10-green measurement runs on the repaired base.

### Step 1: reproduce, verify the API, design the isolation + pre-approach consult — COMPLETE (plan review skipped-by-design SP-195, `.reviews/1-20260812T205835.md`)
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

  - consult: solo ×1, verdict design-sound + hole (probe2 was intra-CLASS; fix needs cross-CLASS same-collection) closed by probe1b RED×2; actual answering model NOT surfaced by the tool (recorded honestly)
### Step 2: implement — loud skip + isolation — COMPLETE (plan review skipped-by-design SP-195, `.reviews/2-20260812T210637.md`)
- [x] Silent `return` → loud skip naming CCP_DATA_ROOT at both checkpoints; assertion strictness unchanged
  - Assert.SkipWhen at both checkpoints (pinned-binary-verified API); the Assert.Equal when bound is byte-identical
  - co-location: DataRootOverrideTests joined [Collection(nameof(ProcessEnvCollection))]; DisableParallelization kept as non-relied-upon hint
- [x] Isolation fix implemented; normal-run skip path unreachable
  - proven by probe 1b (cross-class same-collection sequentiality) + the enumeration (single in-suite mutator, finally-restore); the skip now fires ONLY for an externally-set override
- [x] Sweep findings applied or dispositioned per site
  - enumeration table in record.md Step 1: 2 REAL (this fix), all other surfaces cleared with reasons
  - DISCOVERED RED fixed (run 1): AiProviderLabIntegrationTests.Refusal_ThroughPipeline_TypedCarrier_ExactlyOneHit 891/1, Expected 1 Actual 0 at HitsFor(Refusal) — root cause proven by code read: AiProviderLab.Handle wrote+CLOSED the response before _records.Enqueue, so a fast client could read the count before its own record existed; fixed by recording BEFORE the response becomes observable for every outcome-independent mode (9 sites, one root-cause class; Timeout/HangStream/SlowOk records stay post-outcome — their state is outcome-dependent and awaited via WaitForRecordAsync). Assertion-neutral harness ordering, in-scope (client/tests/**), allowlist untouched
- [x] Positive control captured: `891 passed / 1 skipped` with the reason visible
  - command: `set CCP_DATA_ROOT=C:\Temp\ccp-sp062-positive-control-sandbox && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` → `Passed: 891, Skipped: 1, Total: 892`, console line `Skipped CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault`, TRX outcome=NotExecuted with Message `CCP_DATA_ROOT override is active at the guard checkpoint (leak class: runner-set override in the external process environment) — ...` (trx: sp062-positive-control.trx)
- [x] No new deadline literals / injected budgets; timing guard green
  - pure reordering + SkipWhen calls; TestTimingGuardTests pins untouched (verified in the full-suite greens)
- [ ] Silent `return` → loud skip naming CCP_DATA_ROOT at both checkpoints; assertion strictness unchanged
- [ ] Isolation fix implemented; normal-run skip path unreachable
- [ ] Sweep findings applied or dispositioned per site
- [ ] Positive control captured: `891 passed / 1 skipped` with the reason visible
- [ ] No new deadline literals / injected budgets; timing guard green

### Step 3: ten consecutive greens, one genuinely cold — COMPLETE (run table below)
- [x] 10 consecutive full-suite runs, zero reds, TRX attached, output redirected to files
- [x] ≥1 fresh-checkout first-ever build (rebuild-in-place does not count)
- [x] Every clean run 892 passed / 0 skipped + 35 headless
- [x] Row-49 site occurrences recorded (run #, cold/warm, TRX name) — untouched
- [x] Run table + TRX artifacts committed under evidence/

**Run table (post-fix tree, commit b89b25f0; zero reds, zero skips, zero row-49 firings):**

| run | worktree | cold/warm | wall | unit | headless | TRX |
|---|---|---|---|---|---|---|
| green01 | in-place | warm | 79s | 892/0 | 35/0 | sp062-green01-{unit,headless}.trx |
| green02 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green02-* |
| green03 | in-place | warm | 71s | 892/0 | 35/0 | sp062-green03-* |
| green04 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green04-* |
| green05 | FRESH (`C:/Code/ccp-sp062-cold`, detached @b89b25f0, removed after) | **COLD — first-ever build** (0W/0E, build log committed) | 106s | 892/0 | 35/0 | sp062-green05-cold-* |
| green06 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green06-* |
| green07 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green07-* |
| green08 | in-place | warm | 73s | 892/0 | 35/0 | sp062-green08-* |
| green09 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green09-* |
| green10 | in-place | warm | 73s | 892/0 | 35/0 | sp062-green10-* |

Row-49 site (`LoopbackOllamaProviderTests.Truncated_PrefixCut_NeverSurfaced_TypedUnavailable`): **zero firings across all 10 runs incl. the cold first-ever build** — nothing to attribute, site untouched. Hit-rate data point for that row: 0/1 cold, 0/10 this measurement.
Interruption named honestly: a pre-fix run (logs `run01-*`, TRX `sp062-run01-*`) went 891/1 on the AiProviderLab record-ordering race — root-caused + fixed in Step 2 before this series; the 10 above are consecutive post-fix.

### Step 4: record + pre-completion consult — NOT STARTED
- [ ] record.md complete (repro, API verification, enumeration, design, positive control, run table, limits, intended filings)
- [ ] Honesty cell: what this task does NOT prove
- [ ] Pre-completion solo consult (verdict + ACTUAL model)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification — NOT STARTED
- [ ] Contract testCommand passes (verify.mjs 0, 0W/0E, 892/35, 0 skipped, TRX)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
