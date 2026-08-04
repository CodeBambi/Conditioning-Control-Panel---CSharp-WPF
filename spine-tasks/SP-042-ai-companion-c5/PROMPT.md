# Task: SP-042 — AI companion slice c5: awareness (consent, cooldowns, context packaging, keyword routing)

## Mission

Execute slice **c5** of `client/docs/ai-companion-admission.md` §8 for the `client/docs/task-board.md` row **"Implement AI companion and awareness integration"** (P0): **awareness** — code-enforced consent at operation admission (typed consent state, never a prompt convention); typed cooldown machinery (per-trigger / global / per-keyword / loop-protection classes, **extend-not-shrink** semantics) with observable `Suppressed(cooldown)` outcomes, never silent no-ops; context packaging under consent (the WPF-observed `[Category | App | Title | Duration]` shape — every transmitted field passes c3's input moderation boundary; fields NEVER enter diagnostics or memory); keyword-trigger routing as SP-004 **owned** awareness operations (panic-cancellable — the WPF fire-and-forget `Task.Run` shape is rejected); awareness-class outcomes drop by type (no out-of-context refusal bubble — c3's typed outcomes with the awareness-drop behavior landing here at the operation-class level); typed `AiReply.Fallback`/`Unavailable` visibility (the badge always reflects the true source). Evidence = **U (Windows) + Windows title-observation session facts + named Linux limit** (see (g)).

**Honesty framings (binding):** (a) **consent scope/granularity/defaults are owner-pending** (§9.2): the seam is shaped so granularity answers tighten without contract change; the placeholder default = NOT GIVEN (conservative posture — awareness performs no operations until an owner-decided consent exists; WPF baseline FACTS: `AwarenessModeEnabled` AND `AwarenessConsentGiven` both default false, `WindowAwarenessService.cs:337-338`); (b) **cooldown VALUES are owner-pending** (§9.2): mechanism only; WPF baseline values recorded as FACTS, never decisions — reaction cooldown effective default 10s (`AppSettings.cs:3000-3008`, clamp 10–600) **with the recorded discrepancy that the service's `?? 90` fallback (`WindowAwarenessService.cs:374-388`) is dead code against the non-nullable 10s default (owner question: 10 or 90?)**; keyword global 10s (`:4294`); per-keyword 15s (`:4314`); loop protection 5s (`:4438,4450`); still-on milestones 1/5/10 min (`WindowAwarenessService.cs:405-407`); (c) **extend-not-shrink**: a cooldown operation may extend a live cooldown, never shorten it (WPF mechanism, `:374-402`); (d) **drop-by-type**: awareness-class refusal/unavailable outcomes drop silently BY TYPE at the operation-class result handling (contract §4 rule 3) — this slice owns the routing-layer drop behavior c3 deferred (the typed outcome exists from c3; the surfacing policy lands here); (e) **context fields are privacy-sensitive**: transmitted only under consent, every field through c3's `EvaluateInput`, NEVER into diagnostics/memory; window-title observation is platform evidence — capability-probed typed state, honestly scoped, no Wayland claim anywhere; (f) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (g) **WSL2 named limit: laptop WSL zero distros (`wsl -l -q` empty, exit 0) — the admission's "WX session facts for window-title observation (X11 only)" is UNDISCHARGEABLE on this machine; recorded as owner-gated (provision a distro), NEVER faked; Windows title-observation facts are the session evidence.**

## Dependencies

- **Task:** SP-038 (c3 landed — the input boundary every context field passes)

## Context to Read First

- `client/docs/ai-companion-admission.md` §5 (awareness design — rules 1–5 verbatim: consent, packaging, cooldowns, keyword routing, interactive-vs-awareness) + §8 c5 row (exact scope + evidence class) + §9.2 (owner-question ledger)
- `client/docs/ai-operation-contract.md` §4 (awareness — consent rule 1, cooldown rule 2, drop-by-type rule 3) + §12 (content-free diagnostics)
- `spine-tasks/SP-038-ai-companion-c3/record.md` (boundary semantics; awareness typed outcomes; the drop-behavior deferral this slice owns)
- Landed mechanics (consume): `client/src/CcpClient.Desktop/Ai/` (pipeline, boundary, escalation precedent for injected-clock mechanisms), `client/src/CcpClient.Desktop/Capabilities/` (SP-006 probe discipline for title-observation capability)
- WPF (READ-ONLY, `File.cs:line`): consent `Services/UI/WindowAwarenessService.cs:337-349`; reaction cooldown `:374-407` (+ `?? 90` dead-code discrepancy `:374-388`); context packaging `Services/AiService.cs:160-163,182-188`; keyword machinery `Services/KeywordTriggerService.cs:158-187`; gates `:590-591,604-605`; AvatarComment routing `:1280-1328`; removed keyword pre-check `:1294-1301`; defaults `CCP.Core/Models/AppSettings.cs:2982,2993,3000-3008,4294,4314,4438,4450`

## File Scope

- `client/src/CcpClient.Desktop/Ai/**` (awareness consent seam, cooldown machinery, context packaging, keyword routing, title-observation capability)
- `client/tests/CcpClient.Tests/AiAwareness*` (NEW test files)
- `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs` + `client/tests/CcpClient.Tests/AiOfflineIntegrationTests.cs` (call-site updates ONLY if the pipeline signature changes — additive-optional wiring shape, the c4 lane-disjointness precedent)
- `client/tests/CcpClient.HeadlessTests/**` (surface tests where honest — likely none; recorded if absent)
- `spine-tasks/SP-042-ai-companion-c5/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/tests/CcpClient.Tests/AiProviderLab.cs`, `client/tests/CcpClient.Tests/DtrhNativeEffectsTests.cs` |
| artifactsMustExist | `spine-tasks/SP-042-ai-companion-c5/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Archaeology + consent/cooldown design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): consent pair (both default false), reaction cooldown mechanism incl. the `?? 90` dead-code discrepancy, context packaging shape + assembly, keyword machinery (trigger/global/per-keyword/loop-protection + gates), AvatarComment routing (+ the two admission corrections: owned operation + typed visibility), removed keyword pre-check
- [ ] Design: `AiAwarenessService.cs` (contract-named) — typed `AiAwarenessConsent` state checked at admission (placeholder default NOT GIVEN, recorded); typed `AiCooldownRegistry` (4 classes, extend-not-shrink, injectable clock per the c3 precedent; placeholder values with WPF baselines recorded incl. the 10-vs-90 owner question verbatim); `Suppressed(cooldown)` typed outcome (observable, content-free); context packaging (the `[Category|App|Title|Duration]` shape — every field through `EvaluateInput` before assembly; fields never into diagnostics/memory); keyword routing as SP-004 owned awareness operations (panic-cancellable; typed Fallback/Unavailable per §2 rule 4); title-observation capability (SP-006 probe discipline: Windows session facts via a probed typed state; Linux typed Unavailable — never faked)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Consent + cooldown machinery + suppression outcomes

- [ ] Consent seam at admission (typed; denied = typed no-op, never silent, never throws; no operation performs awareness work without the consent state)
- [ ] `AiCooldownRegistry` + 4 classes + extend-not-shrink + `Suppressed(cooldown)` typed outcomes (injectable clock; mechanism tests: extend accepted, shrink rejected, suppression observable, expiry behavior)
- [ ] Unit tests: consent denied/admitted; each cooldown class; extend-not-shrink; Suppressed typed shape

### Step 3: Context packaging + keyword routing + title observation + boundary integration

- [ ] Context packaging: the WPF shape assembled ONLY under consent; every field passes `EvaluateInput` before assembly (a blocking policy prevents transmission — zero network follows); fields never into diagnostics/memory (proof)
- [ ] Keyword routing: trigger → owned awareness operation (SP-004 registry: panic-cancellable, generation-invalidated); typed Fallback/Unavailable visibility (badge reflects true source); drop-by-type at the operation-class result handling (no refusal bubble on the awareness class)
- [ ] Title observation: Windows session facts via the probed capability (real title capture evidence on Windows — mechanism, honestly scoped); Linux typed `Unavailable` (never faked); no Wayland claim
- [ ] Offline zero-network re-verified (no consent/cooldown/packaging path performs network without an operation); content-free diagnostics maintained

### Step 4: Evidence consolidation + pre-completion consult

- [ ] Write `spine-tasks/SP-042-ai-companion-c5/record.md` (archaeology, design, consult verdicts + ACTUAL answering models, engine-review presence, title-observation session facts, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 537/29 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Code-enforced consent at admission (placeholder default NOT GIVEN recorded; baseline both-false FACTS cited)
- Typed cooldown machinery: 4 classes, extend-not-shrink, observable `Suppressed(cooldown)` (placeholder values; WPF baselines recorded incl. the 10-vs-90 owner question)
- Context packaging under consent with every field through the c3 boundary (blocking policy = zero transmission); fields never in diagnostics/memory
- Keyword routing as owned operations (panic-cancellable); typed Fallback/Unavailable visibility; drop-by-type at the awareness class
- Title-observation capability: Windows session facts + Linux typed Unavailable (WSL named limit; no Wayland claim)
- Contract green (≥537/29 floor); both solo consults persisted with actual answering models

## Do NOT

- Decide consent granularity/defaults or cooldown VALUES (§9.2 owner questions; WPF baselines = recorded facts only; the 10-vs-90 discrepancy goes to the owner verbatim); perform awareness work without the consent state; silent no-op on suppression (Suppressed is typed + observable); transmit context fields unmoderated or unconsented; log context fields/titles (content-free rule); fire-and-forget keyword operations (owned ops only); fake the badge source; fake Linux/Wayland title-observation evidence (named limit recorded); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/tests/CcpClient.Tests/AiProviderLab.cs`, `client/tests/CcpClient.Tests/DtrhNativeEffectsTests.cs`; set any board row state
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-042): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-042-ai-companion-c5/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-04 (authoring, orchestrator): **admission §8 slice c5 (awareness) per the approved serial cut; SP-040 landed `6255a643`.** Consent-defaults/cooldown-values owner-pending + extend-not-shrink + owned-operation routing encoded from §5 verbatim; the 10-vs-90 discrepancy carried to the owner verbatim. Enabler 2 (no hot docs). Headless (mechanism + Windows session facts); 4h budget exported at launch. WSL zero-distros named limit encoded in honesty framing (g). Consult route per the 2026-08-04 rewire. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-04 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-042 + SP-043, 2 lanes — disjoint scopes: awareness tests are NEW `AiAwareness*` files; T-16 touches only `DtrhNativeEffectsTests.cs` (+ a possible minimal product seam with justification)) per owner cycle.
