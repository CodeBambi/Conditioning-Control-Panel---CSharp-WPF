# Task: SP-040 — AI companion slice c4: memory (IAiMemoryStore on SP-005 machinery)

## Mission

Execute slice **c4** of `client/docs/ai-companion-admission.md` §8 for the `client/docs/task-board.md` row **"Implement AI companion and awareness integration"** (P0): **memory** — the first `IAiMemoryStore` implementation on SP-005 machinery (ONE `PersistenceStore<AiMemoryDocument>` in the user-data root with its OWN named `AsyncOperationOwner` — the SP-024 lesson; schemaVersion + migration journal; corrupt document quarantines once at startup → typed `Degraded` — the b2/b4 precedent); consent-gated writes (code-enforced at write admission); moderation-gated persist with rollback (persist deferred until c3's output moderation passes; a blocked turn is rolled back and never hits disk — admission §4 rule 5); **explicit-clear operation with file-content proof** (empties in-memory state AND deletes the persisted document — WPF `ClearHistory()` + file delete, `LocalAiService.cs:173-178`); retention/disable placeholders per §4 rule 3. Evidence = **U + file-content proof** (document deleted on clear; blocked turn never persisted). No WH/WX claims.

**Honesty framings (binding):** (a) **no retention/disable VALUES decided** — pair-cap (WPF baseline 50, `LocalAiService.cs:92`) and disable semantics (retain-dormant vs delete) are owner questions (§9.2); the document schema is shaped so BOTH answers are implementable additively without schema change (disable flag and retention policy orthogonal; a `dormant` marker representable from v1); WPF baselines are recorded as FACTS, never decisions; (b) **only per-turn user/assistant chat pairs persist** — system prompts, enrichment blocks, and awareness/ambient turns are NEVER persisted (WPF stateless ambient path, `LocalAiService.cs:476-502`; persist skips system/enrichment, `:107-135`); (c) memory content NEVER enters diagnostics (contract §12 rule 3) and is NEVER a secret (contract §10); (d) consent is code-enforced at write admission — never a prompt convention; scope (global vs per-feature) and default are owner-pending (§9.2; WPF baseline fact: `ChatMemoryEnabled` default **true**, `CompanionPromptSettings.cs:120`) — the placeholder default follows the conservative-posture precedent, decided with the consult and recorded; (e) **consume c3's boundary**: persist is deferred until output moderation passes; blocked turn = typed, rolled back, never persisted (§4 rule 5 — **this slice also discharges c3 inventory row 6's Reserved→Wired seam: `EvaluateOutput` consumed by the persist path**); (f) provider switching NEVER implicitly clears memory (contract §5 rule 3); (g) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (h) no Wayland claims; **WSL2 named limit: laptop WSL zero distros (`wsl -l -q` empty, exit 0) — "U both platforms" discharges Windows-only with the Linux run owner-gated, never faked**; (i) rejected alternative on record: a dedicated memory-persistence seam duplicates SP-005's migration/quarantine/schema machinery for zero gain (§4 rule 2's pre-approach consult — re-verify, don't transcribe).

## Dependencies

- **Task:** SP-038 (c3 landed — the output boundary this persist path consumes)

## Context to Read First

- `client/docs/ai-companion-admission.md` §4 (memory design — rules 1–5 verbatim: what/where/retention/clear/consent) + §8 c4 row (exact scope + evidence class) + §9.2 (owner-question ledger)
- `client/docs/ai-operation-contract.md` §5 (memory — consent, provider-neutral pairs, no-implicit-clear) + §12 (content-free diagnostics) + §10 (never-a-secret)
- `spine-tasks/SP-038-ai-companion-c3/record.md` (boundary semantics, inventory row 6's reserved seam, escalation-state shape precedent for serializable documents)
- Landed mechanics (consume): `client/src/CcpClient.Desktop/Ai/` (pipeline, boundary, `IAiMemoryStore` declared-only from SP-016), `client/src/CcpClient.Desktop/Persistence/` (SP-005 machinery: `PersistenceStore<T>`, owners, quarantine, journal)
- `spine-tasks/SP-024-dtrh-host-b2/record.md` (the N-stores-N-owners lesson — one named owner per store)
- WPF (READ-ONLY, `File.cs:line`): memory lifecycle `Services/AIService/LocalAiService.cs:92-178` (50-pair cap, consent check, persist shape), clear `:173-178` (+ UI callers `MainWindow/MainWindow.Patreon.cs:962,1539,1570`), stateless ambient path `:476-502`, moderation-gated persist precedent `:546-586`, consent default `CCP.Core/Models/CompanionPromptSettings.cs:120`

## File Scope

- `client/src/CcpClient.Desktop/Ai/**` (memory store, document schema, consent seam, pipeline persist wiring)
- `client/tests/CcpClient.Tests/AiMemory*` (NEW test files — memory store + pipeline persist proofs)
- `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs` + `client/tests/CcpClient.Tests/AiOfflineIntegrationTests.cs` (ctor/wiring call-site updates ONLY if the pipeline signature changes)
- `client/tests/CcpClient.HeadlessTests/**` (surface tests where honest — likely none; recorded if absent)
- `spine-tasks/SP-040-ai-companion-c4/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**
- **LANE-DISJOINTNESS CONSTRAINT (orchestrator, recorded):** the wave-mate SP-041 owns `AiProviderLab.cs`, `AiProviderLabIntegrationTests.cs`, `LoopbackOllamaProviderTests.cs` — if the pipeline signature changes, existing call sites in THOSE files must keep compiling WITHOUT edits (additive-optional wiring shape; the c3 boundary-ctor precedent adapted for lane disjointness). If that is genuinely impossible, HARD STOP the lane and record why (do not touch SP-041's files).

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Ai/AiMemoryStore.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/tests/CcpClient.Tests/AiProviderLab.cs`, `client/tests/CcpClient.Tests/AiProviderLabIntegrationTests.cs`, `client/tests/CcpClient.Tests/LoopbackOllamaProviderTests.cs` |
| artifactsMustExist | `spine-tasks/SP-040-ai-companion-c4/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Archaeology + document schema + consent design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): the memory lifecycle (50-pair cap, consent check site, persist shape, ClearHistory + file delete, UI clear callers, stateless ambient path, moderation-gated persist precedent `:546-586`)
- [ ] Design: `AiMemoryDocument` schema (schemaVersion 1; per-turn user/assistant pairs, provider-neutral; disable flag + retention policy ORTHOGONAL fields; `dormant` marker representable — §4 rule 3's both-answers-additively condition); ONE `PersistenceStore<AiMemoryDocument>` with its OWN named owner; consent seam (typed consent state checked at write admission; placeholder default per honesty framing (d), recorded); the persist position in the pipeline (after c3's output boundary passes, before reply application — rollback on block; per-change justification if the seam extends)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + schema + consent design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Store implementation + schema machinery

- [ ] `AiMemoryStore.cs` (contract-named) implementing `IAiMemoryStore` on SP-005 machinery: round-trips, pair-cap placeholder enforcement (mechanism only, value owner-pending), corrupt-document quarantine → typed Degraded once at startup (b2 precedent), migration journal, unknown-member preserve
- [ ] Consent gating: writes occur only under the explicit typed consent state (code-enforced at write admission — denied write = typed no-op, never silent, never throws); placeholder default recorded
- [ ] Unit tests: round-trips, quarantine→Degraded, journal, consent denied/admitted writes, schema both-answers shape (dormant marker representable; disable + retention orthogonal)

### Step 3: Pipeline persist wiring + explicit clear + file-content proofs

- [ ] Persist wiring: per-turn pairs persist after the output boundary passes (interactive class); blocked turn → typed, rolled back, NEVER persisted (test proves zero file content after a blocked turn); awareness turns never persisted (negative proof); provider switch never implicitly clears (test)
- [ ] **Explicit-clear operation**: empties in-memory state AND deletes the persisted document — file-content proof (document bytes gone from disk; a subsequent read yields the empty state, never a resurrected document)
- [ ] Offline zero-network re-verified (memory is pure local persistence); content-free diagnostics maintained (no memory content in any record); any new log site registered in the redaction registry

### Step 4: Evidence consolidation + pre-completion consult

- [ ] Write `spine-tasks/SP-040-ai-companion-c4/record.md` (archaeology, schema + consent design, persist position, file-content proofs, consult verdicts + ACTUAL answering models, engine-review presence, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 516/29 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- First `IAiMemoryStore` on SP-005 machinery: own named owner, schemaVersion + journal, quarantine→Degraded, unknown-member preserve
- Consent-gated writes code-enforced at admission (placeholder default recorded; WPF baseline fact cited)
- Moderation-gated persist with rollback: blocked turn never persisted (file-content proof); awareness never persisted; provider switch never implicitly clears
- Explicit-clear operation: in-memory emptied + document deleted (file-content proof)
- Retention/disable schema shaped for both owner answers additively (no values decided)
- Contract green (≥516/29 floor); both solo consults persisted with actual answering models

## Do NOT

- Decide or invent retention/disable VALUES (pair-cap number, dormant-vs-delete — §9.2 owner questions; WPF baselines = recorded facts only); persist system prompts, enrichment, or awareness turns; put memory content in diagnostics/logs (content-free rule) or treat it as a secret (§10); persist on a blocked turn (rollback discipline); implicitly clear on provider switch; create a second persistence path/seam duplicating SP-005 machinery; edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); claim Wayland; fake WSL2/Linux evidence (named limit recorded); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`; set any board row state
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-040): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-040-ai-companion-c4/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-04 (authoring, orchestrator): **admission §8 slice c4 (memory) per the approved serial cut; SP-038 landed `f4eea79e` (§4 rule 5 binds this slice's moderation-gated persist; c3's inventory row-6 seam is this slice's discharge obligation).** Values-pending + ambient-never-persisted + consent-code-enforced encoded from §4 verbatim. Enabler 2 (no hot docs). Headless (mechanism + file-content proofs); 4h budget exported at launch. WSL zero-distros named limit encoded in honesty framing (h). Consult route per the 2026-08-04 rewire. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-04 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-040 + SP-041, 2 lanes — disjoint file scopes: memory tests are NEW files; T-15 touches only the lab harness files) per owner cycle.
- 2026-08-04 (authoring, orchestrator): **scope narrowed for lane disjointness** (planner flagged overlap with SP-041): test scope = new `AiMemory*` files + the two non-lab wiring call-site files; SP-041's three lab files moved into `fileScopeMustNotChange` with the additive-optional wiring constraint recorded in File Scope.
