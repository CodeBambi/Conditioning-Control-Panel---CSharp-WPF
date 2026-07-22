# Task: SP-030 — AI companion admission: provider/memory/awareness design-record + implementation slice cut (design-only, zero product code)

## Mission

Deliver the **admission design-record** for the `client/docs/task-board.md` row **"Implement AI companion and awareness integration"** (P0 — blockers discharged: SP-016 contract + SP-019 spike landed, owner network/memory decisions approved 2026-07-21) as `client/docs/ai-companion-admission.md`: the evidence-based design + an implementation slice cut (c1…cN) with acceptance mapping and evidence classes. **Design-record only — ZERO product code** (the SP-022 DTRH-admission pattern). The companion implementation slices execute after this lands.

**Honesty framings (binding):** (a) every design claim traces to the SP-016 contract (`client/docs/ai-operation-contract.md`), the SP-019 spike (`client/docs/ai-provider-spike.md`), WPF archaeology (`File.cs:line`), or is marked greenfield-decision — never transcription, never invention; (b) **owner-decision ledger:** the 2026-07-21 decree ("all gates lifted; AI companion network/memory decisions approved to proceed per SP-016 contract + SP-019 spike") is recorded verbatim with its source; what remains genuinely owner-pending (moderation policy VALUES, endpoint allow-list governance, memory retention specifics, awareness cooldown VALUES) is enumerated as owner questions, NOT silently decided by this document — the design must function with typed placeholders pending those answers; (c) the SP-016 content-free rule (no prompts/completions/user-text in diagnostics) and the SP-019 findings (F1 duplicate-key validator gap — a follow-up fix the implementation row owns; remote-host policy rejection before socket; stale-generation discard) are binding constraints, not suggestions; (d) privacy: memory design must state exactly what is stored, where (SP-005 machinery vs new seam), retention/clear semantics (explicit memory clear = row acceptance), and consent gating (awareness consent/cooldowns = row acceptance); when in doubt, mark it owner-question; (e) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — record in record.md; the orchestrator reconciles at land; (f) Linux evidence classes are stated honestly per slice (no Wayland claims anywhere).

## Dependencies

- **Task:** SP-028 (T-5 patch landed — 2-lane-era gate)

## Context to Read First

- `client/docs/ai-operation-contract.md` (SP-016) — 13 named contract sections (the mechanics this design assembles)
- `client/docs/ai-provider-spike.md` (SP-019) — provider matrix evidence + named limits (Ollama absent, cloud credentials nonexistent, F1 duplicate-key gap, moderation policy values pending-owner, endpoint allow-list governance pending-owner, admissible command set pending-owner, NotExecuted(SupersededGeneration) lands with the execution row)
- `client/docs/dtrh-admission.md` (SP-022) — the admission-record shape this document mirrors (sections, evidence classes, slice cut, explicit non-claims)
- WPF AI subsystem (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/AiService.cs` (cloud endpoint, provider switching, badges/status), the companion/awareness surfaces (`Services/Companion/CompanionService.cs`, `PersonalityService.cs`, memory store, awareness reactions + cooldowns), moderation call sites, secret storage (DPAPI/seams), offline behavior, panic cancellation; `AI_AUDIT.md` (repo root — endpoints, prompt surfaces, compliance gaps)
- `client/docs/task-board.md` row "Implement AI companion and awareness integration" (acceptance text) + the Decisions-needed entries (moderation policy values, endpoint allow-list, memory retention, awareness cooldowns)

## File Scope

- `client/docs/ai-companion-admission.md` (the deliverable)
- `spine-tasks/SP-030-ai-companion-admission/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/ai-companion-admission.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/src/**`, `client/tests/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-030-ai-companion-admission/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: WPF AI archaeology + evidence consolidation + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): AiService (provider model, switching/cancellation, badges/status), companion chat surface, awareness reactions (triggers, consent, cooldowns), memory store (what/where/retention/clear), moderation call sites (every surface + command field per the row acceptance), secret storage, offline behavior, panic cancellation; AI_AUDIT cross-check (endpoint inventory, prompt surfaces, compliance gaps the design must close)
- [ ] Evidence consolidation: SP-016 contract sections → design obligations; SP-019 spike evidence → proven mechanics (validator, generation invalidation, provider failure taxonomy, remote-host rejection, sensitive-logging registry); SP-019 named limits → honest design placeholders
- [ ] **Pre-approach solo consult** (Fable 5, solo) with the archaeology + the planned document outline + the slice-cut sketch; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: The admission document

- [ ] `client/docs/ai-companion-admission.md` — sections mirroring the SP-022 shape: §1 evidence base (contract + spike + archaeology citations per claim); §2 provider design (switching without late results — generation invalidation per SP-016/SP-019; badges/status accuracy; offline zero-network typed semantics); §3 moderation design (every surface + command field, verdict taxonomy, policy VALUES = owner question with typed placeholders); §4 memory design (what/where/retention/explicit-clear; consent gating; SP-005 machinery vs new seam — decided with the rejected alternative); §5 awareness design (consent + cooldowns — values owner-pending, mechanism designed); §6 secrets + offline (secret seam, DPAPI-class discipline, zero-network proof shape); §7 panic cancellation (generation invalidation + bounded teardown); §8 **implementation slice cut c1…cN with acceptance mapping + evidence classes per slice** (mirroring §7 of the DTRH admission); §9 explicit non-claims + owner-question ledger (verbatim decree citation)
- [ ] Consistency pass: every row-acceptance item maps to at least one slice; every slice's evidence classes are honest for Windows AND Linux (no Wayland claims); the F1 duplicate-key fix is assigned to a slice explicitly

### Step 3: Review + board-preparation content + pre-completion consult

- [ ] Write `spine-tasks/SP-030-ai-companion-admission/record.md` (archaeology, decisions + rejected alternatives, owner-question ledger, consult verdicts + ACTUAL answering models, engine-review presence, durable-lesson candidates for the orchestrator's land reconcile)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the document; verdict text in record.md; corrections applied before .DONE
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green — zero product/test change, counts EXACTLY the 391/29 floor, any drift = red flag)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- `client/docs/ai-companion-admission.md` delivered with per-claim evidence citations, the provider/moderation/memory/awareness/secrets/panic designs, the slice cut c1…cN with acceptance mapping + honest evidence classes, explicit non-claims, and the owner-question ledger (decree cited verbatim)
- Every row-acceptance item mapped to a slice; the F1 duplicate-key fix assigned; no owner-pending value silently decided
- Contract green (391/29 exact, no drift); both solo Fable consults persisted with actual answering models

## Do NOT

- Write product code or tests (design-record only); silently decide owner-pending values (moderation policy, endpoint allow-list, memory retention specifics, awareness cooldown values); invent endpoints/credentials; claim Wayland; edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`, `client/src/**`, `client/tests/**`; set any board row state
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-030): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/ai-companion-admission.md`, `spine-tasks/SP-030-ai-companion-admission/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-07-22 (authoring): **row blockers discharged** (SP-016 contract + SP-019 spike landed; owner network/memory decisions approved 2026-07-21 — decree cited verbatim in the owner-question ledger requirement). Enabler 2 encoded (worker excludes task-board.md + port-lessons.md). Waved with SP-029 (quips arbitration q1 — disjoint scope, both non-headed). Design-record pattern per SP-022 (admission → slices). mustNotChange intersected against File Scope at authoring (SP-020 lesson — src/tests excluded entirely). T-11 sizing: headless; 4h budget exported at launch for consistency.
- 2026-07-22 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached wave batch (SP-029 + SP-030, 2 lanes) per owner cycle.
