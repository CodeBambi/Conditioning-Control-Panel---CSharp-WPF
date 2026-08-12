# Task: SP-059 — Timing discipline in tests: convert, sweep, guard (THIRD occurrence — encode, do not fix once)

## Mission

The suite that grades every land is not trustworthy. At the SP-058 land the **orchestrator's merged-state verification caught a red that the worker's own suite AND the engine's `contract.verified` gate both certified green**: `AiProviderLabIntegrationTests.Panic_Live_DuringRealInFlightOperation_TypedCancelled_BoundedDrain_ClientGone` failed **2 times in 6 runs** on the merged tree, both correlated with cold/first-run conditions, with `timed out waiting for a real in-flight network operation`.

The mechanism is a hard-coded **8000 ms** wall-clock deadline: `client/tests/CcpClient.Tests/AiProviderLabIntegrationTests.cs:88-97` (`WaitForAsync`, `Environment.TickCount64 + 8000`), consumed at `:283` waiting for `BytesReadSoFar > 0` over a loopback `HttpListener`. The sibling poll `WaitForRecordAsync` (`:70-86`) has the same shape.

This is the **third occurrence of one class**: T-15 (SP-041 — the same lab harness's listener lifecycle) and T-16 (SP-043 — DTRH cap timers converted to an injected `ManualClock`). Two occurrences are lessons; three means the fix must be **encoded**, not applied once. Deliver all three parts: **convert**, **sweep and classify suite-wide**, **guard + standing order**.

**Binding framings:**
(a) **The obvious wrong fix is banned.** Raising 8000 to 30000 (or any larger literal) is not a fix — it makes the flake rarer and the failure slower. Any wait that survives must be either driven by an injected clock/deterministic signal, or a tolerant window with a **loud classifier**.
(b) **A timeout must say which thing happened.** "The condition never became true" (a real product/test failure) and "this machine was slow" are different verdicts and must be reported differently. A wait that fails with one undifferentiated message is why this class keeps returning.
(c) **Zero assertions weakened.** SP-043's bar, verbatim: no assertion is relaxed, deleted, tolerance-widened, or `[Fact]`→`[Fact(Skip=...)]`'d to buy green. Prove it — a grep-level diff summary of every assertion touched belongs in record.md, and the honest answer is normally "none".
(d) **The bar is 10 consecutive full-suite green runs, output captured to FILES.** SP-058's land process change: the first failure's identity was lost by tailing. Every run's output goes to its own file under the packet's `evidence/`, and the run index (pass/fail + counts per run) is a table in record.md.
(e) **Policy text is drafted, not applied.** The `docs/constitution.md` line banning new wall-clock waits is **policy-touching**: write the EXACT proposed sentence in record.md; the orchestrator applies it at land. Do not edit `docs/constitution.md`.
(f) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — this is a 2-lane parallel wave (constitution: parallel waves reconcile at land). Name intended filings in record.md.

## Dependencies

- none (wave-17 lane-1; file-scope-disjoint from lane-2, which touches zero tests and zero product code)

## Context to Read First

- `client/docs/task-board.md` — the row "Test suite still contains hard-coded wall-clock waits — THIRD occurrence of the timing-discipline class" (READ-ONLY; its acceptance is this task's acceptance, including the floor note)
- `client/tests/CcpClient.Tests/AiProviderLabIntegrationTests.cs:70-97` (`WaitForRecordAsync`, `WaitForAsync`) and `:275-295` (the flaking test) — the concrete defect
- `client/tests/CcpClient.Tests/AiProviderLab.cs` — the loopback lab harness SP-041 hardened (listener lifecycle, fresh-instance-per-bind, leaked-listener self-check registry)
- `client/tests/CcpClient.Tests/IntakeServingTests.cs` — its `LoopbackServer` fixture is **pre-existing on base** and is **NOT registered** in SP-041's leaked-listener self-check registry (named in the row as in scope)
- `spine-tasks/SP-043-dtrh-captimer-tests/record.md` — the pattern to reuse: class-wide injected clock, real durations on a `ManualClock`, latent-timer surface closed class-wide, zero assertions changed
- `spine-tasks/SP-041-ai-lab-harness/record.md` — what was already hardened in this exact harness (do not re-litigate it; build on it)
- `client/src/CcpClient.Desktop/Audio/AudioSeams.cs:113` (`ISoundClock`) — the existing product clock seam and its real-clock default; the four private `ManualClock` copies live at `BarkPipelineTests.cs:703`, `DtrhFxRouterTests.cs:147`, `DtrhNativeEffectsTests.cs:492`, `SoundArbitrationTests.cs:551`
- `client/tests/CcpClient.Tests/DataRootChokePointGuardTests.cs` — the guard-test style (SP-056/SP-057 lineage) this task's guard follows
- `docs/constitution.md` — where the drafted standing order will land (orchestrator applies; READ-ONLY here)

## File Scope

- `client/tests/CcpClient.Tests/**` (conversions, the shared wait helper, the guard test)
- `client/tests/CcpClient.HeadlessTests/**` (only if the sweep finds wall-clock dependencies there)
- `client/src/CcpClient.Desktop/**` — **only** if a conversion provably requires an injected-clock seam that does not exist yet: **additive only, real clock stays the default**, no behavior change (the SP-043 precedent). If no seam is needed, this path must not change and record.md says so.
- `spine-tasks/SP-059-timing-discipline/**`
- **NOT in scope:** `ConditioningControlPanel/**`, `docs/constitution.md` (draft only — orchestrator applies), `client/docs/**`, `.spine/**`, `.pi/**`, `client/CcpClient.sln`, `client/spikes/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/AiProviderLabIntegrationTests.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `docs/constitution.md`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `.spine/**`, `.pi/**`, `client/CcpClient.sln`, `client/spikes/**` |
| artifactsMustExist | `spine-tasks/SP-059-timing-discipline/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Sweep, classify, design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] **Sweep suite-wide, do not sample:** enumerate every wall-clock dependency in `client/tests/**` (`Task.Delay`, `Thread.Sleep`, `Stopwatch`, `Environment.TickCount64`, `TimeSpan.From*` deadlines, polling loops, `CancellationTokenSource` timeouts). Record a table: `file:line` → construct → what it waits for → **class**
- [ ] **Classify each** into exactly one: (1) **deterministic-convertible** (an injected clock or an awaited signal/completion source can replace it outright); (2) **tolerant-window-required** (waits on a real external actor — loopback socket, real process, real backend — where a bounded window is legitimate) → needs the loud classifier; (3) **legitimately real-time** (the test's subject IS elapsed time and it is not a deadline the machine can lose) → left alone WITH the reason recorded
- [ ] Reproduce the named flake deliberately if possible (cold-start correlation) and record what was observed — if it cannot be reproduced on demand, say so rather than inventing a mechanism
- [ ] Design (a) the single approved wait helper — bounded window + **loud classifier** distinguishing "condition never became true" from "machine was slow" (the message must name which); (b) the guard's detection rule and its allowlist shape; (c) whether any product seam is required (default answer: none)
- [ ] **Pre-approach solo consult** (`mode: "solo"` — bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback per the pause protocol); verdict + **ACTUAL answering model** in record.md before checking this box

### Step 2: Convert the AI lab + close SP-041's registry gap

- [ ] Convert `AiProviderLabIntegrationTests`' waits (`WaitForAsync`, `WaitForRecordAsync`) to the approved mechanism — deterministic signal where the lab can expose one, otherwise the tolerant window with the loud classifier. The 8000 ms literal disappears; it is not replaced by a bigger literal
- [ ] Register `IntakeServingTests`' `LoopbackServer` in SP-041's leaked-listener self-check registry (the row names this gap explicitly); the self-check must still fail loud, not warn
- [ ] Run the flaking test repeatedly under the conversion (cold conditions included) and record the observed result — this is the local proof before the suite-wide bar in Step 4

### Step 3: Suite-wide conversion + the guard

- [ ] Convert every class-1 (deterministic-convertible) site found in Step 1; class-2 sites move onto the approved helper; class-3 sites stay with their recorded reason
- [ ] If the four duplicated private `ManualClock` copies obstruct a class-wide conversion, consolidate them — **only** if it changes zero assertions and zero product behavior; otherwise leave them and record why (do not turn this into a refactor task)
- [ ] **Guard test:** fails when a new hard-coded deadline literal appears in test code outside the approved helper (`DataRootChokePointGuardTests` style — source-scanning, deterministic, with a named allowlist for class-3 sites). Prove it **red then green**: a transcript showing the guard failing against a deliberately re-introduced literal, then passing after removal
- [ ] Draft the EXACT `docs/constitution.md` sentence banning new wall-clock waits in tests (one line, in the Hard rules voice) into record.md — do NOT edit the file

### Step 4: Ten consecutive full-suite green runs (captured to files)

- [ ] Run the full contract testCommand **10 consecutive times**, each run's complete output written to `spine-tasks/SP-059-timing-discipline/evidence/run-NN.log` (never tailed — SP-058's land lesson)
- [ ] Record the run index table in record.md: run → pass/fail → unit count → headless count → duration. **10/10 green is the bar**; a single red means diagnose and restart the count, and the aborted sequence stays in the record
- [ ] Include at least one **cold** run (fresh boot conditions as close as the environment allows — build artifacts cleared / first-run after a rebuild), since both observed reds correlated with cold start; state exactly what "cold" meant here
- [ ] Floor discipline: **862 unit / 33 headless**. Any red is identified BY NAME before it is discussed; no red is attributed to the known flake by assumption

### Step 5: Record + pre-completion consult

- [ ] Write `record.md`: the sweep table + classification, the reproduction attempt, the helper design + rejected alternatives (bigger literal — rejected at authoring), the conversion inventory, the registry-gap closure, the guard's red/green transcript, the drafted constitution line, the 10-run index, **the assertion-change proof (expected: none)**, consults + ACTUAL answering models, engine-review presence, surprises, durable-lesson candidates, and intended board filings
- [ ] **Pre-completion solo consult** (same route discipline as Step 1); verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 6: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0, build 0W/0E, ≥862 unit / ≥33 headless, TRX logger attached)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- The 8000 ms deadline and its sibling polls are gone from `AiProviderLabIntegrationTests`, replaced by a deterministic signal or the approved tolerant-window helper with a loud classifier — not by a larger literal
- Every wall-clock dependency in `client/tests/**` is classified with its verdict recorded; class-1 sites converted, class-2 sites on the helper, class-3 sites justified
- A guard test fails on a new hard-coded deadline literal outside the approved helper, proven red-then-green
- `LoopbackServer` is registered in the leaked-listener self-check
- 10 consecutive full-suite green runs, each captured to its own file, including at least one cold run
- Zero assertions weakened (proven, not asserted); zero product behavior change (or an additive real-clock-default seam with the reason recorded)
- The constitution line is drafted in record.md, not applied

## Do NOT

- Raise any deadline literal, widen a tolerance, skip/quarantine the flaking test, or delete an assertion to buy green
- Attribute an unexplained red to the known flake without naming the test
- Tail run output instead of capturing files; claim 10 greens without 10 files
- Edit `docs/constitution.md`, `client/docs/**`, `ConditioningControlPanel/**`, `.spine/**`, `.pi/**`, the sln, or `client/spikes/**`; set any board row state
- Turn this into a general test refactor — conversions only, with the classification as the boundary
- Use `consult` council mode (T-7: solo only — a bare `consult` call errors with the stale synthesizer seat)

## Git Commit Convention

- `feat(SP-059): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-059-timing-discipline/record.md`
**Explicitly NOT updated by the worker:** `docs/constitution.md` (drafted only), `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md` (orchestrator reconciles at land)

## Amendments

- 2026-08-12 (authoring, orchestrator): wave-17 lane-1. **Ordering correction from the wave-17 decomposition consult (solo, Opus 5): this row front-runs the Chaos tunnel backdrop that the wave-16 consult had queued for lane-1.** Reasons recorded: (1) the tunnel lane writes the most wall-clock-hungry test class in the repo (b1–b5 heartbeat 5s/10s/20s, 1200 ms exit waits) — landing a deadline-literal guard in parallel with new deadline literals is a merge-time landmine that reproduces the SP-058 failure deliberately; (2) the suite-wide sweep is a snapshot that decays with every test-bearing lane authored before it; (3) wave-17's own lands are graded by the suite this row repairs. Chaos tunnel moves to wave 18 (alone, under the new guard). Lane-2 (SP-060) is zero product code and zero tests, so the two lanes are provably disjoint. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-12 (authoring, orchestrator): the wave-17 decomposition consult response was **truncated mid-sentence** by the tool at the point it began discussing the MCP posture for the tunnel packet; the recovered guidance is recorded above verbatim in substance and **nothing was stitched or guessed** (SP-058 precedent for truncated-consult honesty). MCP posture at authoring: 0/3 servers connected (`avalonia-live` cached, `avalonia-docs`/`avalonia-ui` not connected) — irrelevant to this packet (no UI surface), and re-checked before the tunnel packet is authored.
