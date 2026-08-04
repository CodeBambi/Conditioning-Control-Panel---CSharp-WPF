# Task: SP-043 — T-16: DTRH cap-timer tests — deterministic timing discipline for the remaining flake class

## Mission

Execute the `client/docs/task-board.md` P3 tooling row **"T-16: DTRH cap-timer tests — the remaining timing-bound flake class"** (OPEN, filed 2026-08-04): make the `DtrhNativeEffectsTests.FirePayload_Video_*` tests deterministic under parallel load. The class: a 0.05s cap-timer + wall-clock poll (`FirePayload_Video_PlaysFromPool_RaisesStarted_CapsAtSegment`, `DtrhNativeEffectsTests.cs:277` — `Assert.Equal(1, video.StopCalls)` actual 0 observed once in 10 full-suite runs during SP-041's stability chain; same profile as the wave-4 T-3 attempt-1 flake). **Fix the timing discipline, NEVER the assertions' meaning** — the cap behavior under test is product semantics; the test's OBSERVATION of it must stop depending on wall-clock luck.

**Honesty framings (binding):** (a) **never weaken/loosen/delete an assertion to buy green** — widen no timeout as the primary fix; the deterministic shapes are: fake/injected clock or timer seam (c3's injectable-clock precedent), or a tolerant window with a loud flake classifier naming the class (if full determinism is genuinely unavailable — justified per case); (b) **product-code changes are conditional and minimal** — if determinism needs a test-visible seam in `DtrhNativeEffects` (e.g., an injectable clock/timer factory), it is additive-only with a per-change justification in record.md; behavior of the product path NEVER changes (the seam defaults to the real clock); (c) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (d) acceptance is EMPIRICAL: 10 consecutive full-suite runs with ZERO cap-timer reds (transcripts); (e) **WSL2 named limit: laptop WSL zero distros — Windows-only evidence, never faked.**

## Dependencies

- **None** (the flake class is documented in the board row + wave-4/SP-041 records)

## Context to Read First

- `client/docs/task-board.md` row T-16 (acceptance text) + the wave-4 gate-history T-3 note (attempt-1 flake precedent) + `spine-tasks/SP-041-ai-lab-harness/record.md` §6 (the observed run-4 red with forensics)
- `client/tests/CcpClient.Tests/DtrhNativeEffectsTests.cs` (the cap-timer tests — every timing dependency, not just the one observed red)
- `client/src/CcpClient.Desktop/Features/Dtrh/` (the fire-payload video path: cap timer implementation — where the 0.05s cap and the StopCalls sequencing live)
- The c3 injectable-clock precedent (`client/src/CcpClient.Desktop/Ai/` escalation/cooldown mechanisms — injected clock as a ctor dependency with production default)
- `spine-tasks/SP-025-dtrh-host-b3/record.md` (the fire-payload video semantics: 15s segment cap, pool, payload-state on/off)

## File Scope

- `client/tests/CcpClient.Tests/DtrhNativeEffectsTests.cs` (the timing discipline)
- `client/src/CcpClient.Desktop/Features/Dtrh/**` (CONDITIONAL, additive-only: a test-visible clock/timer seam if determinism genuinely requires it — per-change justification; product behavior unchanged)
- `spine-tasks/SP-043-dtrh-captimer-tests/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/DtrhNativeEffectsTests.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/src/CcpClient.Desktop/Ai/**` |
| artifactsMustExist | `spine-tasks/SP-043-dtrh-captimer-tests/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Timing archaeology + fix design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Read EVERY timing dependency in `DtrhNativeEffectsTests.cs` (not just the observed red) + the product cap-timer implementation (where the 0.05s cap and StopCalls sequencing live); classify each: wall-clock poll / real timer / sleeping assertion
- [ ] Design: per-dependency deterministic shape (injected clock/timer seam per the c3 precedent — additive, production defaults to real clock; or tolerant-window + loud flake classifier with per-case justification); the loud classifier names the class when it fires (never silent)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Implement the timing discipline

- [ ] Test changes in `DtrhNativeEffectsTests.cs` (every classified dependency converted; assertion MEANINGS unchanged — same cap semantics verified)
- [ ] Conditional product seam (only if required; additive; justified; product behavior unchanged with the real-clock default)
- [ ] The full DTRH test class green; the suite's other tests untouched

### Step 3: Stability proof + evidence + pre-completion consult

- [ ] **10 consecutive full-suite runs with ZERO cap-timer reds** (transcripts in evidence/; runs under the suite's normal parallel load — no serialization switches)
- [ ] If any non-cap-timer flake appears during the chain: name it via TRX, record the class, re-run — never silently absorb (the wave-4 rule)
- [ ] Write `spine-tasks/SP-043-dtrh-captimer-tests/record.md` (archaeology, per-dependency classifications, design + rejected alternatives, stability transcripts, consult verdicts + ACTUAL answering models, engine-review presence, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green; counts ≥ the 537/29 floor — new tests only if a deterministic seam adds them, recorded)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Every timing dependency in the cap-timer tests classified and converted to a deterministic shape (or tolerant window + loud classifier with per-case justification)
- Assertion meanings unchanged; product behavior unchanged (any seam additive-only, real-clock default)
- 10 consecutive full-suite runs zero cap-timer reds (transcripts)
- Contract green; record.md carries classifications, justifications, both solo consult verdicts with actual answering models, engine-review presence per call

## Do NOT

- Weaken/loosen/delete assertions to buy green; widen timeouts as the primary fix; change product behavior (seams additive-only with real-clock defaults); serialize the suite; touch `client/src/CcpClient.Desktop/Ai/**` (wave-mate scope); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`; set any board row state; fake the 10-run transcript
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-043): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-043-dtrh-captimer-tests/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-04 (authoring, orchestrator): **T-16 row filed at the wave-6 reconcile (SP-041's stability-chain forensics + wave-4 precedent).** Deterministic-shapes-first discipline (fake/injected clock per c3 precedent) with tolerant-window+classifier as the justified fallback; product seam conditional + additive. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-04 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-042 + SP-043, 2 lanes — disjoint scopes) per owner cycle.
