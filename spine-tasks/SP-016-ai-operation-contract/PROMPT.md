# Task: SP-016 — define provider-neutral AI operation contract

## Mission

Execute `client/docs/task-board.md` row **"Define provider-neutral AI operation contract"** (P0, FIRST row of Phase 3 in `spine-tasks/CONTEXT.md`): specify typed outcomes, cancellation/generation, provider switching, interactive versus awareness operations, local memory, endpoint classification/disclosure, moderation, strict command envelope, per-command results, secret storage, offline behavior, and content-free diagnostics **before implementation topology**. This is a DEFINE-ONLY contract packet in the SP-003…SP-006 pattern: a contract document + typed vocabulary + seam mechanics with unit tests. **No providers, no network calls, no UI, no moderation engine** — the contract is the deliverable; provider spikes and the companion row come after.

**Honesty framings (Phase 3 decomposition consult, binding):** (a) every acceptance item maps to a NAMED contract section; mechanics-level items (envelope validation, diagnostic content-freedom, generation invalidation) also get seed tests — vocabulary alone is not evidence; (b) seams (`ISecretStore` already declared-only per SP-005; a local-memory seam here) are DECLARED-ONLY — per the SP-006 honesty rule a declared seam is NOT a capability claim; (c) owner questions stay UNANSWERED and are recorded pending-owner (memory consent scope/retention, moderation policy specifics, endpoint allow-list governance, awareness consent/cooldown values); (d) the contract must NOT import WPF/first-attempt mechanics — WPF is read-only behavioral evidence, the first attempt is lessons-only; (e) environment-free: no network, no Ollama, no cloud proxy — the WSL2 in-packet gate proves the mechanics on Linux without faking provider evidence.

## Dependencies

- **Task:** SP-015 (final Phase-2 chain link; Phase 3 opens serial per decomposition consult — headed-focus and evidence discipline)

## Context to Read First

- `client/docs/task-board.md` — the AI contract row + gate history (Phase 3 decomposition verdict) + Decisions-needed
- `client/docs/async-lifecycle-fault-contract.md` (SP-004) — operation ownership/generation/typed outcomes the AI contract REUSES (cancellation/generation semantics are not reinvented)
- `client/docs/runtime-capability-contract.md` (SP-006) — typed-state honesty rule (declared seams ≠ capability)
- `client/docs/persistence-migration-contract.md` (SP-005) — `ISecretStore` declared-only precedent
- `AI_AUDIT.md` (repo root) — 2026-05-27 provider inventory: cloud proxy companion chat (`Services/AiService.cs:20-407`), local Ollama (`CCP.Core/Services/AIService/LocalAiService.cs`), AI-driven effects JSON commands (`CCP.Avalonia/Services/Commands/AiCommandService.cs`), awareness `AvatarComment` routing (`Services/KeywordTriggerService.cs`), quiz, community-prompt manifest, catalogue/bug-report endpoints
- WPF sources (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/AiService.cs`, `ConditioningControlPanel/Services/AIService/`, `ConditioningControlPanel/Services/Companion/`, `ConditioningControlPanel/Services/Moderation/`
- First attempt (READ-ONLY, lessons-only): `ConditioningControlPanel/CCP.Core/Services/AIService/` (`IAiService.cs`, `AiServiceStrategy.cs`, `CoreAiService.cs`, `LocalAiService.cs`, `OpenAiService.cs`, `AiResponseParser.cs`) + `client/docs/first-attempt-lessons.md` — strategy-pattern and response-parser ACCEPT/ADAPT/REJECT dispositions must be cited explicitly
- Required skills: load `wpf-parity` before Step 1

## File Scope

- `client/docs/ai-operation-contract.md` (deliverable contract)
- `client/src/CcpClient.Desktop/Ai/**` (typed vocabulary + seam declarations + mechanics — no providers)
- `client/tests/CcpClient.Tests/**` (vocabulary/envelope/diagnostics/generation tests)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-016-ai-operation-contract/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/ai-operation-contract.md`, `client/src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `client/docs/ai-operation-contract.md`, `spine-tasks/SP-016-ai-operation-contract/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: AI archaeology + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF + first-attempt archaeology (READ-ONLY, `File.cs:line`): provider inventory and switching, interactive (chat) vs awareness (`AvatarComment`) operation shapes, token caps/temperatures as recorded facts, local-Ollama JSON effects command envelope + parsing failures observed, moderation surfaces (every surface/command field), endpoint inventory (cloud proxy, loopback Ollama, remote-host policy, manifest/catalogue/bug-report), secret storage, offline behavior
- [ ] First-attempt strategy/parser lessons with explicit ACCEPT/ADAPT/REJECT dispositions (strategy seam, response parser, strategy-switch races, error taxonomy)
- [ ] Owner-question inventory: which acceptance items need owner values (memory consent/retention, moderation policy specifics, endpoint allow-list governance, awareness consent/cooldowns) — recorded pending-owner, never answered
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable T-7) with the archaeology + contract section map; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Contract document

- [ ] `client/docs/ai-operation-contract.md` — NAMED section per acceptance item: typed outcomes (reusing SP-004 `OperationOutcome` failure-kind vocabulary); cancellation/generation (SP-004 ownership, no new machinery); provider switching (strategy seam vocabulary, switch = generation invalidation, no late results); interactive vs awareness operation classes (consent/cooldown semantics as contract, values pending-owner); local memory (declared-only seam, explicit-clear operation, consent gating as contract); endpoint classification/disclosure (typed endpoint classes incl. loopback vs remote-host policy, disclosure rule); moderation (every-surface/every-command-field rule, verdict taxonomy — policy values pending-owner); strict command envelope (schema authority, reject-by-default, per-command results vocabulary); secret storage (`ISecretStore` reuse, declared-only); offline behavior (typed per-class offline semantics, honesty rule applies); content-free diagnostics (diagnostic vocabulary carries NO prompts/completions/user text — schema-level rule); implementation-topology neutrality stated explicitly
- [ ] Every section traces to archaeology evidence (`File.cs:line` or AI_AUDIT line) or is marked greenfield-decision

### Step 3: Typed vocabulary + seam mechanics + tests

- [ ] `client/src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs` (contract-named) + siblings: typed outcomes, operation classes, endpoint classes, moderation verdict taxonomy, command-envelope schema + validator, per-command results, diagnostic record
- [ ] Seams declared-only (memory, secret) — interfaces + documentation, NO implementations
- [ ] Unit tests: envelope validation (valid/invalid/mixed/malformed envelopes — reject-by-default vocabulary, zero execution semantics asserted as TYPES not runtime), per-command results round-trip, diagnostic content-freedom (the diagnostic record cannot serialize prompt/completion/user-text fields — schema proof), generation-invalidation reuse demonstrated against SP-004 registry, serialization round-trips of every vocabulary type

### Step 4: WSL2 gate + board reconciliation + pre-completion consult

- [ ] WSL2 in-packet gate (native-dir copy `~/ccp-sp016`, never /mnt/e): contract testCommand green; output in record.md (environment-free — no provider claims made or needed)
- [ ] Write `spine-tasks/SP-016-ai-operation-contract/record.md` (archaeology, dispositions, owner questions, consult verdicts + ACTUAL answering models, engine-review presence, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the contract + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` row → `WIP` with evidence + named limits (owner questions pending, seams declared-only, no provider evidence) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild` per the xUnit1051 lesson)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Contract document covers EVERY acceptance item as a named section with archaeology trace or greenfield-decision mark; implementation-topology neutrality explicit
- Typed vocabulary + envelope validator + diagnostic schema in `client/src/CcpClient.Desktop/Ai/`; memory/secret seams declared-only (SP-006 honesty: no capability claims from declarations)
- Mechanics tests green: envelope reject-by-default, per-command results, diagnostic content-freedom schema proof, generation-invalidation reuse, serialization round-trips
- WSL2 gate green; board row `WIP` (not `DONE`) with named limits; both solo Fable consults persisted with actual answering models

## Do NOT

- Implement any provider, network call, endpoint client, UI, or moderation engine; configure real endpoints (classification only); answer owner questions (memory consent/retention, moderation policy values, endpoint allow-list governance, awareness cooldowns — record pending-owner)
- Import WPF/first-attempt mechanics (read-only evidence); claim provider capability from declared seams; store or log prompts/completions/user text in diagnostics (content-free is a schema rule, not a convention)
- Modify `ConditioningControlPanel/**`, `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-016): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/ai-operation-contract.md` (deliverable), `client/docs/task-board.md` (row evidence), `spine-tasks/SP-016-ai-operation-contract/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only)

## Amendments

- 2026-07-21 (authoring): **Phase 3 decomposition consult RAN — solo Fable 5 (council unavailable T-7).** Verdicts applied: (a) AI contract FIRST (define-only, environment-free, zero headed evidence — trivially T-11-compliant, gates the later AI spike + companion rows); (b) Phase 3 order: SP-016 AI contract → SP-017 audio backend spike → SP-018 online-video handoff spike; serial execution (headed-focus discipline); (c) geometry spike stays EXCLUDED (owner-present display-topology work, not autonomous); camera rows + MCP audit + BLOCKED rows excluded; (d) T-11 sizing rule applied to the packet template before this authoring.
- 2026-07-21 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
