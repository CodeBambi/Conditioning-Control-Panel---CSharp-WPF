# Task: SP-044 — AI companion slice c6: command execution (envelope → plan → gated dispatch)

## Mission

Execute slice **c6** of `client/docs/ai-companion-admission.md` §8 for the `client/docs/task-board.md` row **"Implement AI companion and awareness integration"** (P0): **command execution** — validated envelope → execution plan → per-effect dispatch behind master + per-effect consent gates (post-validation, contract §8 rule 6); moderation pre-execution via c3's boundary (every free-text command field, through the shipped `AiEnvelopePolicy.ForBoundary` factory); **`NotExecuted(SupersededGeneration)` verdict LANDS** (discharges SP-019 limit 7 — supersession proven at operation level, now at execution level); **canary zero-execution proofs** (no effect backends exist in the greenfield client — dispatch targets are typed placeholders/canaries, never fake effects). Evidence = **U** (canary, verdict round-trips, superseded-generation). **Provable scope (admission §8 c6 acceptance-mapping, binding):** with the none-admitted default, c6's provable scope is canary + verdict round-trips + `NotExecuted`/`ConsentGated` paths — **the WH line shrinks to what exists, never an undischargeable claim** (flash/subliminal/spiral backends do not exist yet; no WH/WX claims).

**Honesty framings (binding):** (a) **admissible command set defaults to NONE ADMITTED — a DELIBERATE divergence, not silent:** the WPF baseline (contract §8 rule 6) has master OFF but bubbles/subliminal/bounce ON; the greenfield pipeline executes only the owner-admitted subset, default none (conservative pending-owner posture, §9.2 owner question — recorded verbatim, never silently decided); (b) **consent gates are post-validation and typed**: master gate + per-effect consent gates evaluated AFTER envelope validation (contract §8 rule 6); a gated effect = typed `NotExecuted(ConsentGated)`, never silent, never partially applied; (c) **atomic envelope semantics stand** (SP-016/SP-019): whole-envelope rejection on any invalid member; valid siblings of a rejected envelope carry `NotExecuted(EnvelopeRejected)` and nothing executes; the canary proves ZERO execution on every rejected class; (d) **moderation pre-execution is wired through the product factory** (`AiEnvelopePolicy.ForBoundary` — c3's shipped composition, consumed never re-implemented); a moderated command field = typed refusal at the envelope, zero dispatch; (e) **supersession is typed at execution**: a plan executed under a stale generation (provider switch / panic between plan and dispatch) yields `NotExecuted(SupersededGeneration)` per command — SP-019 limit 7's mechanism landing in-product; (f) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (g) no Wayland claims; **WSL2 named limit: laptop WSL zero distros — "U both platforms" discharges Windows-only with the Linux run owner-gated, never faked**; (h) **packet-assigned obligations from the wave-7 land (board row named limits 3+4):** flip the `awareness-context-fields` inventory row Reserved→Wired (counts 6/5 + one assertion arm in `AiModerationCoverageTests.cs` — explicitly IN File Scope) and retire the bool-overload door if the 4 test call sites migrate cleanly (grep first; migration = typed overload everywhere + bool overload deleted; if any call site resists, record and leave the overload with the retirement condition intact).

## Dependencies

- **Task:** SP-042 (c5 landed — the awareness/service surface + typed overload this slice consumes)

## Context to Read First

- `client/docs/ai-companion-admission.md` §8 c6 row (exact scope + the none-admitted divergence verbatim + the acceptance-mapping's provable-scope shrink) + §9.2 (admissible command set owner question)
- `client/docs/ai-operation-contract.md` §8 (strict command envelope — rule 6: master + per-effect consent gates post-validation; the WPF baseline divergence) + §9 (per-command results — typed verdicts incl. `NotExecuted` reasons) + §2 (generations/supersession)
- `client/docs/ai-provider-spike.md` (SP-019): the 62-case fuzz (atomic rejection, canary, valid-sibling `NotExecuted(EnvelopeRejected)`), limit 7 (`NotExecuted(SupersededGeneration)` not validator-reachable — this slice lands it)
- `spine-tasks/SP-038-ai-companion-c3/record.md` (`ForBoundary` factory + free-text-field enumeration + the Reserved→Wired known limitation) + `spine-tasks/SP-042-ai-companion-c5/record.md` (typed overload + bool-door retirement condition)
- Landed mechanics (consume): `client/src/CcpClient.Desktop/Ai/` (`AiExecutionPlan` validator-constructed, vocabulary, boundary, `AiEnvelopePolicy.ForBoundary`)
- WPF (READ-ONLY, `File.cs:line`): effect dispatch + consent gate shapes `Services/AiService.cs` (effect command handling), master/effect toggles `CCP.Core/Models/CompanionPromptSettings.cs`; first-attempt toggle-only command path `CCP.Avalonia/Services/Commands/AiCommandService.cs:87-103` (lessons-only — gates on toggles only, never moderated)

## File Scope

- `client/src/CcpClient.Desktop/Ai/**` (execution plan dispatch, consent gates, canary/typed effect placeholders, superseded-generation verdicts)
- `client/tests/CcpClient.Tests/AiCommand*` (NEW test files — canary zero-execution, verdict round-trips, consent gates, superseded-generation)
- `client/tests/CcpClient.Tests/AiModerationCoverageTests.cs` (the Reserved→Wired flip: disposition + counts 6/5 + one assertion arm — wave-7 land obligation)
- `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs` + `client/tests/CcpClient.Tests/AiOfflineIntegrationTests.cs` + the 2 other bool-overload call sites found by grep (bool-door retirement migration — if clean)
- `client/tests/CcpClient.HeadlessTests/**` (surface tests where honest — likely none; recorded if absent)
- `spine-tasks/SP-044-ai-companion-c6/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Ai/AiCommandExecutor.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/tests/CcpClient.Tests/AiProviderLab.cs`, `client/tests/CcpClient.Tests/DtrhNativeEffectsTests.cs` |
| artifactsMustExist | `spine-tasks/SP-044-ai-companion-c6/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Archaeology + dispatch/consent design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): effect command handling + master/effect toggle shapes; the first-attempt toggle-only gate (REJECTED — unmoderated); the SP-019 fuzz outcomes this dispatch must reproduce (atomic rejection, valid-sibling NotExecuted, canary silence)
- [ ] Design: `AiCommandExecutor.cs` (contract-named) — plan → per-effect dispatch behind master + per-effect consent gates (post-validation; gates typed; none-admitted default with the WPF-divergence verbatim); canary/typed effect placeholders per command class (typed `NotExecuted(EffectUnavailable)` vs `ConsentGated` vs `EnvelopeRejected` vs `SupersededGeneration` — closed vocabulary, content-free); moderation pre-execution through `ForBoundary`; superseded-generation checked at dispatch (stale generation = per-command `NotExecuted(SupersededGeneration)`, never a late apply)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Executor + consent gates + canary

- [ ] `AiCommandExecutor.cs` + typed per-command result vocabulary on the SP-016 plan shape (consume — `AiExecutionPlan` stays validator-constructed)
- [ ] Master + per-effect consent gates (post-validation, typed, none-admitted default with divergence recorded)
- [ ] Canary effect placeholders (every command class resolves to a typed placeholder — no effect backends exist; a canary records every would-execute so zero-execution proofs are falsifiable)

### Step 3: Zero-execution proofs + superseded-generation + moderation wiring + assigned obligations

- [ ] Zero-execution proofs across every rejected class (invalid/mixed/moderated/out-of-range/stale-generation/consent-gated: canary silent, typed verdicts exact, valid siblings `NotExecuted(EnvelopeRejected)`)
- [ ] `NotExecuted(SupersededGeneration)` at execution level (plan under generation N, switch to N+1 before dispatch → per-command typed, nothing applied — SP-019 limit 7 discharged)
- [ ] Moderation pre-execution through `ForBoundary` (a moderated free-text command field = typed refusal at the envelope, zero dispatch)
- [ ] **Assigned obligations:** Reserved→Wired flip in `AiModerationCoverageTests.cs` (disposition + counts 6/5 + one assertion arm — suite stays green); bool-overload retirement IF the 4 call sites migrate cleanly (grep-proven; else recorded and left)

### Step 4: Evidence consolidation + pre-completion consult

- [ ] Write `spine-tasks/SP-044-ai-companion-c6/record.md` (archaeology, design, canary shapes, consult verdicts + ACTUAL answering models, engine-review presence, obligations dispositions, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 564/29 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Envelope → plan → gated dispatch live: master + per-effect consent gates post-validation, typed, none-admitted default with the WPF divergence recorded verbatim
- Moderation pre-execution through the shipped `ForBoundary` factory (moderated field = typed refusal, zero dispatch)
- Canary zero-execution proofs on every rejected class; valid siblings typed `NotExecuted(EnvelopeRejected)`
- `NotExecuted(SupersededGeneration)` lands at execution level (SP-019 limit 7 discharged)
- Reserved→Wired flip landed (counts 6/5 + assertion arm); bool-door retired or retirement condition re-recorded
- Contract green (≥564/29 floor); both solo consults persisted with actual answering models

## Do NOT

- Admit any effect command by default (none-admitted posture; the WPF baseline divergence is recorded verbatim, never silently widened); execute or fake effect backends (canary/typed placeholders only); gate pre-validation (gates are post-validation per contract §8 rule 6); partial-apply an envelope (atomic semantics); late-apply a stale-generation plan; log command-field contents (content-free rule); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/tests/CcpClient.Tests/AiProviderLab.cs`, `client/tests/CcpClient.Tests/DtrhNativeEffectsTests.cs`; set any board row state; claim headed/WH/WX evidence (provable scope is U-only per the admission's shrink)
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-044): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-044-ai-companion-c6/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-04 (authoring, orchestrator): **admission §8 slice c6 (command execution) per the approved serial cut; SP-042 landed `49c4af7b`.** None-admitted default + provable-scope shrink + assigned obligations (Reserved flip with the coverage test EXPLICITLY in scope; bool-door retirement) encoded per the wave-7 land consult's authoring notes. Enabler 2 (no hot docs). Headless (U-only per the admission's conditioned WH shrink); 4h budget exported at launch. WSL zero-distros named limit encoded in honesty framing (g). Consult route per the 2026-08-04 rewire. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-04 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-044 + SP-045, 2 lanes — disjoint scopes: command tests are NEW `AiCommand*` files; SP-045 touches only `DtrhFxRouterTests.cs`) per owner cycle.
