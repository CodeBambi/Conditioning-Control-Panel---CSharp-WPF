# Task: SP-033 — AI companion slice c1: AI foundation (pipeline, provider seam, F1, offline, secrets, panic)

## Mission

Execute slice **c1** of `client/docs/ai-companion-admission.md` §8 for the `client/docs/task-board.md` row **"Implement AI companion and awareness integration"** (P0): the **AI foundation** — SP-004 owned-operation pipeline (interactive + awareness classes), provider strategy seam (switch = generation invalidation + cancel + stale discard), **F1 duplicate-key rejection** in the real validator, endpoint classification + loopback-only admission-policy placeholder, offline zero-network typed semantics + send-attempt-counter proof, **cloud-absence typed proof**, `ISecretStore` platform implementations, content-free `AiDiagnosticRecord` wiring, and **panic cancellation at pipeline level**. Real product code in `client/src/CcpClient.Desktop/Ai/` (on SP-016's landed mechanics) + the secret-store platform implementations.

**Honesty framings (binding):** (a) SP-016's contract mechanics are the foundation, not a rewrite target — `AiCommandEnvelope.cs`/`AiDiagnosticRecord.cs`/`AiOperationVocabulary.cs`/`IAiMemoryStore.cs` are consumed; the F1 fix (duplicate-key rejection) is the ONLY validator change and is the SP-019-assigned hardening, with the 62-case fuzz as regression proof; (b) **cloud = typed absence, never invention:** no credentials exist and none are invented (§2 rule 6); selected-but-unproven provider → `AiReply.Unavailable(reason)` + SP-006 typed capability state (the rejected shapes: WPF's identity-check `IsAvailable` `AiService.cs:52` and local always-true `LocalAiService.cs:46`); (c) **offline = zero network:** no speculative retries, no endpoint guessing — proof = the SP-019 send-attempt-counter discipline (an integration test asserts ZERO outbound attempts when no provider is proven); (d) **Linux secret-service is EXPECTED `Unavailable` on WSL2** (no session daemon) — the typed-Unavailable probe path is the honest Linux evidence; a working-daemon proof needs a desktop-session box (named limit, never faked); (e) panic at pipeline level (typed `Cancelled` + stale discard + bounded drain) — c2 re-verifies live, c7 carries the UI-quiet headed proof (the row's acceptance discharges across slices, recorded); (f) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — record in record.md; the orchestrator reconciles at land; (g) content-free diagnostics maintained (schema cannot carry prompts/completions/user-text — SP-016 rule, schema-level proof); every new log site joins the SP-018 redaction registry; no Wayland claims.

## Dependencies

- **Task:** SP-030 (admission landed — §8 c1 scope + evidence classes)

## Context to Read First

- `client/docs/ai-companion-admission.md` §8 c1 row (exact scope + evidence classes + the acceptance-mapping notes: badge plumbing in c1 does NOT discharge "accurate badges/status" — c7; panic = c1+c2+c7 together) + §2 rules 2/3/6 + §9.2 owner-question ledger
- `client/docs/ai-operation-contract.md` (SP-016) — §2 (switch mechanics), §3 rules 2/3, §6 rule 4 (cloud inventory-not-admission), §8 (strict schema), §10 rule 2 (secret seam), §11 (offline semantics)
- `client/docs/ai-provider-spike.md` (SP-019) — F1 duplicate-key finding (the parser-differential hazard + the reject-duplicates answer); send-attempt-counter discipline; the 62 fuzz cases (regression instrument)
- Landed greenfield mechanics (consume): `client/src/CcpClient.Desktop/Ai/AiCommandEnvelope.cs` (the validator — F1's fix site), `AiDiagnosticRecord.cs`, `AiOperationVocabulary.cs`, `IAiMemoryStore.cs`; `client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs:501` (`ISecretStore` declaration); `client/src/CcpClient.Desktop/Capabilities/` (SP-006 typed states); SP-004's `Lifecycle/` owned-operation registry (pipeline classes to reuse)
- WPF (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/AiService.cs` (`:27` endpoint inventory, `:52` IsAvailable identity-check, `:332-352`/`:479-499` auth shapes — inventory only), `LocalAiService.cs:46` (always-true availability), the interactive/awareness operation classes, `CCP.Core/Models/AppSettings.cs:4005-4008` (AuthToken through the Core secret seam)
- Required skills: load `wpf-parity` before Step 1

## File Scope

- `client/src/CcpClient.Desktop/Ai/**` (pipeline, provider seam, F1 fix, endpoint classification, offline semantics, panic)
- `client/src/CcpClient.Desktop/Persistence/**` (ISecretStore platform implementations ONLY)
- `client/src/CcpClient.Desktop/Capabilities/**` (provider capability states ONLY)
- `client/tests/CcpClient.Tests/**` (pipeline/seam/F1/offline/secrets/panic tests)
- `client/tests/CcpClient.HeadlessTests/**` (surface tests where honest — likely none for c1; recorded if absent)
- `spine-tasks/SP-033-ai-companion-c1/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-033-ai-companion-c1/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Contract/archaeology consolidation + design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Consolidate the SP-016 mechanics inventory (what exists vs declared-only) + WPF archaeology (`File.cs:line`): provider model, operation classes, availability semantics, secret seam usage, panic/cancellation paths
- [ ] Design: `AiOperationPipeline.cs` (contract-named) — SP-004 owned operations for interactive + awareness classes; provider strategy seam (switch = generation invalidation → token cancellation → stale-application discard; selection ≠ availability → typed Unavailable + capability state); endpoint classification (loopback/remote/cloud classes, loopback-only admission placeholder per §9.2 #2); offline zero-network semantics; the F1 fix shape (reject duplicates — the only contract-consistent answer per SP-019); ISecretStore implementations (Windows DPAPI / Linux secret-service candidate with typed Unavailable — probe-decided, never faked); content-free diagnostic wiring; panic at pipeline level
- [ ] **Pre-approach solo consult** (Fable 5, solo) with the inventory + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Pipeline + provider seam + F1 + endpoint classification

- [ ] `AiOperationPipeline.cs` + types: owned-operation classes, provider seam with the full switch semantics (a reply started under provider A can never be displayed/executed/remembered after a switch to B), typed Unavailable + SP-006 capability states, endpoint classification + loopback-only placeholder
- [ ] **F1 duplicate-key rejection in the real validator** (`AiCommandEnvelope.cs` — minimal change; SP-019's 62 fuzz cases as regression + new duplicate-key cases)
- [ ] Unit tests: switch/stale-discard matrix, selection≠availability, endpoint classification (remote rejected before socket — send-attempt counter zero), F1 duplicates rejected + 62-case fuzz green

### Step 3: Offline zero-network + secrets + diagnostics + panic + WSL gate

- [ ] Offline semantics: no-provider-proven ⇒ ZERO outbound attempts (integration test with the send-attempt counter); loopback operations degrade independently of cloud
- [ ] `ISecretStore` implementations: Windows DPAPI (round-trip proof); Linux candidate probe → typed Unavailable on WSL2 (expected — the probe path is the honest evidence; recorded); settings documents carry opaque secret NAMES, never values
- [ ] Content-free `AiDiagnosticRecord` wiring (schema-level content-freedom proof maintained); every new log site registered in the redaction registry (SP-018 pattern)
- [ ] Panic at pipeline level: typed `Cancelled` + stale discard + bounded drain (c2/c7 hand-off recorded)
- [ ] **WSL2 in-packet gate (`~/ccp-sp033`, never /mnt/e):** contract testCommand green on Linux; secret-store probe facts recorded honestly

### Step 4: Evidence consolidation + pre-completion consult

- [ ] Write `spine-tasks/SP-033-ai-companion-c1/record.md` (inventory, design, consult verdicts + ACTUAL answering models, engine-review presence, evidence transcripts, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 446/29 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- AI foundation live: SP-004 owned-operation pipeline (interactive + awareness); provider seam with switch = generation invalidation + cancel + stale discard (a reply under A never surfaces under B); selection≠availability → typed Unavailable + capability state
- **F1 duplicate-key rejection landed in the real validator** (62-case fuzz regression green + new duplicate cases)
- Endpoint classification + loopback-only placeholder; offline zero-network with send-attempt-counter proof; cloud-absence typed proof
- ISecretStore: Windows DPAPI round-trip + Linux typed-Unavailable probe (never faked); secret names never values
- Content-free diagnostics maintained; redaction registry covers new sites; panic at pipeline level (typed Cancelled + bounded drain)
- Contract green both platforms (≥446/29 floor); both solo Fable consults persisted with actual answering models

## Do NOT

- Implement a cloud provider or invent credentials/endpoints (typed absence only); implement the loopback Ollama provider (c2); build moderation/memory/awareness/command-execution/UI surfaces (c3–c7); silently decide owner-pending values (§9.2 ledger); fake a Linux secret-service (typed Unavailable is the honest evidence); carry prompts/completions/user-text in diagnostics; edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); claim Wayland; modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`; set any board row state
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-033): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-033-ai-companion-c1/record.md` (evidence + durable-lesson candidates)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-07-22 (authoring): **admission §8 slice c1 binding (exact scope + evidence classes + acceptance-mapping notes); SP-030 landed `bff8f037`.** F1 fix assigned to c1 per the admission. Linux secret-service expectation recorded (typed-Unavailable = honest evidence, never faked). Enabler 2 encoded (no hot docs in worker scope). Headless packet (no DISPLAY3 step); 4h budget exported at launch. T-5 named gate armed: this wave's finalization is the v2 patch's proving ground.
- 2026-07-22 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached wave batch (SP-033 + SP-034, 2 lanes) per owner cycle.
