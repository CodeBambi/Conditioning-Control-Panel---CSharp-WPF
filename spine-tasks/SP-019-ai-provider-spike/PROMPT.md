# Task: SP-019 — spike cancellable AI providers and strict commands

## Mission

Execute `client/docs/task-board.md` row **"Spike cancellable AI providers and strict commands"** (P0, FIRST row of Phase 4 in `spine-tasks/CONTEXT.md`): exercise cloud, loopback Ollama, remote-host rejection/policy, and approved OpenAI-compatible endpoint with cancellation, timeout/rate/error/refusal/malformed output; fuzz strict command schema and verify zero command execution on mixed/invalid/moderated/out-of-range payloads; capture no sensitive logs. Deliverable: a THIRD quarantined spike host (`client/spikes/CcpSpike.AiProvider/`, OUT of the solution — SP-011/017/018 pattern; a **project reference** to `client/src/CcpClient.Desktop` to exercise SP-016's REAL validator is permitted — references are not product-code changes) + `client/docs/ai-provider-spike.md` with a named observation per acceptance item. **Zero product-code change.**

**Honesty framings (Phase 4 decomposition consult, binding):** (a) **a deterministic fake OpenAI-compatible loopback endpoint is the PRIMARY provider instrument** — it injects timeouts, 429 rate responses, refusals, malformed/truncated JSON, and mid-stream cancellation ON DEMAND (falsifiable shapes a live model cannot produce on cue); **real Ollama = bonus session fact if present on this box (probe honestly), named limit if absent**; **cloud/approved-endpoint = named limit (no credentials exist on this box — never invent them)**; (b) **remote-host rejection is a POLICY test against a non-loopback address — no real remote needed** (SP-016 endpoint classification enforced: non-loopback rejected before any socket opens); (c) **the fuzz half exercises SP-016's real `AiCommandEnvelope` validator + `AiExecutionPlan`** (constructible only by the validator) with a canary executor that records every invocation — zero-execution is PROVEN by rejected payloads producing no plan AND the canary never firing; valid envelopes must produce plans whose commands the canary DOES record (the falsifiable pair); moderation policy values stay pending-owner (moderated = verdict-rejected shape only); (d) **"capture no sensitive logs" is enforced the SP-018 way:** central redaction registry + `--audit-logs` self-check + the worker's audit; (e) cancellation demonstrates SP-004 generation discipline (stale-generation late results discarded); (f) no Wayland relevance; WSL2 gate = contract pollution guard + fuzz on Linux (the lab is loopback — real Linux evidence); (g) presentation/UI is out of scope.

## Dependencies

- **Task:** SP-018 (Phase-4 serial chain)

## Context to Read First

- `client/docs/task-board.md` — the AI provider spike row + Decisions-needed + gate history (Phase 4 decomposition verdict)
- `client/docs/ai-operation-contract.md` (SP-016) — the contract being exercised: typed outcomes, operation classes, endpoint classification, moderation verdict taxonomy, strict command envelope, per-command results, content-free diagnostics
- `client/src/CcpClient.Desktop/Ai/` (SP-016 mechanics — `AiCommandEnvelope.cs`, `AiOperationVocabulary.cs`, `AiDiagnosticRecord.cs`, `IAiMemoryStore.cs`) + `client/tests/CcpClient.Tests/AiOperationContractTests.cs`
- `client/docs/video-handoff-spike.md` + `client/spikes/CcpSpike.VideoHandoff/` (SP-018) — loopback-lab + central redaction registry + `--audit-logs` pattern to reuse
- WPF sources (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/AiService.cs` (cloud proxy client), `ConditioningControlPanel/Services/AIService/` + `CCP.Core/Services/AIService/LocalAiService.cs` (Ollama client), `ConditioningControlPanel/Services/Commands/` + `CCP.Avalonia/Services/Commands/AiCommandService.cs` (effects command execution — the zero-execution archaeology)
- `AI_AUDIT.md` — endpoint inventory (cloud proxy, loopback Ollama, remote-host policy)
- Required skills: load `wpf-parity` before Step 1

## File Scope

- `client/spikes/CcpSpike.AiProvider/**` (quarantined spike host — NOT added to `client/CcpClient.sln`; project reference to `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` permitted)
- `client/docs/ai-provider-spike.md` (evidence deliverable)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-019-ai-provider-spike/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/ai-provider-spike.md`, `client/spikes/CcpSpike.AiProvider/CcpSpike.AiProvider.csproj` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/src/**`, `client/tests/**`, `.spine/**` |
| artifactsMustExist | `client/docs/ai-provider-spike.md`, `spine-tasks/SP-019-ai-provider-spike/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Provider/command archaeology + spike design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF + first-attempt archaeology (READ-ONLY, `File.cs:line`): cloud proxy client (endpoint, auth shape, caps), Ollama client (endpoint, request/response shape), effects-command execution path (what a command JSON becomes — the zero-execution ground truth), error/timeout/refusal shapes observed historically, remote-host policy; cite command-safety REJECT lessons
- [ ] Spike design: fake OpenAI-compatible loopback endpoint with a failure-injection control surface (timeout, 429, refusal, malformed/truncated JSON, mid-stream hang); cancellation wiring via SP-004 generation discipline; canary executor design (records every command invocation); redaction registry + `--audit-logs`
- [ ] Ollama presence probe on this box (session fact, honest either way)
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable T-7) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Loopback AI lab + redaction/audit core

- [ ] `client/spikes/CcpSpike.AiProvider/` (console/host, NOT in the solution; project reference to `CcpClient.Desktop` permitted): fake OpenAI-compatible endpoint (`/v1/chat/completions` shape) with deterministic failure injection; cancellable provider client (HttpClient + SP-004 generation tokens); redaction registry + log scrubber + `--audit-logs` (SP-018 pattern — provider payloads, auth headers, endpoint URLs with any tokens all in scope)

### Step 3: Strict-envelope fuzz evidence (zero-execution)

- [ ] Fuzz matrix against SP-016's real validator: mixed valid+invalid commands, invalid schema (wrong types/missing fields/extra fields), moderated payloads (verdict-rejected shape), out-of-range values (numeric bounds, lengths, enum near-misses), malformed JSON (truncated/duplicate keys/depth bombs within declared limits) — per case assert: rejected → NO `AiExecutionPlan` exists AND the canary never fires; valid → plan exists AND canary records exactly the plan's commands (falsifiable pair)
- [ ] Per-command results vocabulary exercised (partial rejection shapes — whole-envelope atomic rejection per SP-016 verified)
- [ ] `--audit-logs` GREEN over the fuzz run

### Step 4: Provider-behavior evidence + WSL2 gate + record + pre-completion consult + board reconciliation

- [ ] Provider matrix against the lab: cancellation (mid-stream cancel → client stops, generation invalidated, no late result applied), timeout (bounded wait → typed timeout outcome, no hang), 429/rate (typed rate outcome + backoff semantics per contract, no retry-storm), error 5xx (typed error outcome), refusal (typed refusal outcome), malformed/truncated JSON (typed parse outcome, never a partial apply); remote-host rejection policy test (non-loopback endpoint rejected before socket open — asserted, no real remote); Ollama session fact (present → one real round-trip recorded; absent → named limit); cloud/approved-endpoint → named limit (no credentials exist)
- [ ] WSL2 in-packet gate (`~/ccp-sp019`, never /mnt/e): fuzz + lab matrix green on Linux (loopback = real Linux evidence); contract testCommand ALSO green (pollution guard)
- [ ] `client/docs/ai-provider-spike.md` — named observation per acceptance item + session facts + named limits; `record.md` complete
- [ ] **Pre-completion solo consult** (Fable 5, solo); verdict text in record.md
- [ ] Update `client/docs/task-board.md` row → `WIP` with evidence + named limits (Ollama presence, cloud credentials, moderation policy values pending-owner) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (client build 0W/0E + both test projects green — pollution guard; spike host builds clean separately)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Fuzz evidence: every payload class (mixed/invalid/moderated/out-of-range/malformed) → zero execution PROVEN (no plan + canary silent) with the valid falsifiable pair green; per-command results exercised; atomic rejection re-verified
- Provider evidence: cancellation/timeout/rate/error/refusal/malformed each → typed outcome per the SP-016 contract (no hangs, no retry-storms, no partial applies, no late results); remote-host rejection before socket open; Ollama session fact or named limit; cloud named limit
- Sensitive-logging audit GREEN and recorded
- WSL2 gate green (lab = real Linux evidence); quarantine holds (zero `client/src`/`tests`/sln changes — project reference only); both solo Fable consults persisted with actual answering models; board row `WIP` (not `DONE`)

## Do NOT

- Add the spike to `client/CcpClient.sln` or touch product code/tests (project reference only); invent cloud credentials or call real cloud/remote endpoints (remote-host rejection is a policy test, no real remote); scrape external services; log sensitive VALUES (payloads/auth/tokens — presence+shape only); implement moderation policy values (pending-owner); add an executor to product code (the canary lives in the spike)
- Answer owner questions (provider allow-list, moderation policy, memory decisions — record pending-owner); modify `ConditioningControlPanel/**`, `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-019): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/ai-provider-spike.md` (deliverable), `client/docs/task-board.md` (row evidence), `spine-tasks/SP-019-ai-provider-spike/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only — **UTF-8 only; the CP1252 lesson applies**)

## Amendments

- 2026-07-21 (authoring): **Phase 4 decomposition consult verdicts applied (solo Fable 5):** AI provider spike CLAIMABLE as ONE packet (fuzz + provider halves share the acceptance + envelope code path — no two-deliverable split); deterministic fake endpoint = the BETTER fuzz instrument (live models can't produce timeout/429/refusal/malformed/mid-stream-cancel on demand); real Ollama = bonus session fact or named limit; remote-host rejection = policy test, no real remote; cloud = named limit (no credentials). **Quips/sound row stays BLOCKED** (Linux audio-stack decision is owner-only). T-11 sizing holds trivially (no headed evidence).
- 2026-07-21 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
