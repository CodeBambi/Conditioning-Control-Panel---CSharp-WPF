# Task: SP-035 — AI companion slice c2: loopback Ollama provider (first REAL provider, LAB)

## Mission

Execute slice **c2** of `client/docs/ai-companion-admission.md` §8 for the `client/docs/task-board.md` row **"Implement AI companion and awareness integration"** (P0): the **loopback Ollama provider** — the FIRST real provider implementation on SP-033's landed foundation: cancellable request/stream client, timeout failure-classifier (never the cancellation mechanism), bounded retry (WPF-observed shape placeholder per §9.2 #6), refusal no-retry, malformed → Unavailable never partial, remote-host pre-socket rejection in-product, and **panic cancellation re-verified LIVE against a real in-flight network operation**. Evidence = **LAB both platforms** (deterministic loopback lab on 127.0.0.1, zero external network — real Linux evidence per the SP-019 instrument) + U.

**Honesty framings (binding):** (a) **Ollama is ABSENT on the evidence box (SP-019 limit 1 stands)** — the deterministic loopback lab is the instrument and is named as such; no real-Ollama round-trip is claimed; (b) the provider CONSUMES c1's foundation (`Ai/AiOperationPipeline.cs` + the strategy seam + endpoint classification + offline semantics) — changes to c1's public surface need per-change justification in record.md; (c) timeout is a failure CLASSIFIER, never the cancellation mechanism (SP-019's proven distinction — cancellation is token-driven with generation invalidation); (d) retry policy values are the WPF-observed placeholder shape (`OpenAiCompatibleService.cs:425-427`, one bounded retry ≤2s clamp) — §9.2 #6 owner question, typed placeholder, never silently decided; (e) refusal = typed carrier, exactly one attempt, NO string-sniffing; malformed/truncated → typed `Unavailable`, never a partial apply, truncated reply prefix never surfaced; (f) remote-host policy rejection happens BEFORE any socket (send-attempt counter = 0 — the SP-019 discipline, now in-product); (g) content-free diagnostics maintained; every new log site joins the SP-018 redaction registry; prompts/completions/user-text never in logs (content-free rule — the lab's fake payloads are the only "content" and are registered secrets in the audit); (h) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — record in record.md; the orchestrator reconciles at land; (i) no Wayland claims; LAB = loopback only, honestly scoped.

## Dependencies

- **Task:** SP-033 (c1 landed — foundation + seams)

## Context to Read First

- `client/docs/ai-companion-admission.md` §8 c2 row (exact scope + evidence classes + the named limit) + §9.2 #6 (retry placeholder) + §2 (provider design)
- `client/docs/ai-provider-spike.md` (SP-019) — the provider matrix evidence (mid-stream cancel 18B-partial-body shape, timeout 797ms vs 800ms bound, 429 exactly-2-hits Retry-After, 500, refusal exactly-1-hit, malformed/truncated never partial, remote-host pre-socket rejection with sendAttempts==0, live stale discard) — the lab's failure shapes this provider must handle identically
- `spine-tasks/SP-033-ai-companion-c1/record.md` — c1's delivered surface (pipeline, seam, classification, offline semantics) + c2 hand-off notes
- Landed mechanics (consume): `client/src/CcpClient.Desktop/Ai/` (c1's pipeline + seams)
- WPF (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/CCP.Core/Services/` OpenAiCompatibleService (`:425-427` retry shape — placeholder), LocalAiService (Ollama client shape, endpoint, model default), the timeout/cancellation paths
- `client/spikes/CcpSpike.AiProvider/` (READ-ONLY — the SP-019 lab instrument: fake OpenAI-compatible loopback endpoint with failure injection; the c2 lab reuses its SHAPES, not its code — the spike stays quarantined)

## File Scope

- `client/src/CcpClient.Desktop/Ai/**` (provider implementation + lab-facing seams)
- `client/tests/CcpClient.Tests/**` (provider/lab/retry/cancel/malformed tests)
- `client/tests/CcpClient.HeadlessTests/**` (surface tests where honest — likely none; recorded if absent)
- `spine-tasks/SP-035-ai-companion-c2/**` (STATUS.md, record.md, evidence incl. the lab, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Ai/LoopbackOllamaProvider.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-035-ai-companion-c2/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Provider archaeology + lab design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): Ollama client shape (endpoint, model default, request/stream protocol), the retry shape (`:425-427` — placeholder per §9.2 #6), timeout/cancellation paths, refusal handling
- [ ] Design: `LoopbackOllamaProvider.cs` (contract-named) on c1's seam — request/stream client with token-driven cancellation + generation invalidation; timeout as failure classifier with the recorded bound discipline; bounded retry (placeholder shape, typed, configurable-off by default per the conservative posture); refusal typed no-retry; malformed/truncated → typed Unavailable never partial; remote-host classification → pre-socket rejection through c1's endpoint classification; the LAB (127.0.0.1 fake OpenAI-compatible endpoint with the SP-019 failure-injection shapes — mid-stream hang, timeout, 429+Retry-After, 500, refusal, malformed, truncated, slow-ok for stale-discard)
- [ ] **Pre-approach solo consult** (Fable 5, solo) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Provider implementation

- [ ] `LoopbackOllamaProvider.cs` + types on c1's seam (consume, don't rewrite — per-change justification if the seam needs extension)
- [ ] Unit tests: request/stream round-trips against the in-process lab; timeout classification (bounded, no hang, token-NOT-cancelled disambiguation); retry bounded (exactly the placeholder count; 429 honors Retry-After, no retry-storm; 500 bounded; refusal exactly 1 attempt); malformed/truncated never partial; remote-host classes rejected with sendAttempts==0 before socket

### Step 3: LAB matrix + live panic + WSL gate

- [ ] **LAB both platforms** (the deterministic loopback lab, real sockets on 127.0.0.1, zero external network): the full failure matrix per the SP-019 shapes — mid-stream cancel (partial body, typed Cancelled fast, generation advanced, lab observes client-gone — NO late result can arrive), timeout, 429, 500, refusal, malformed, truncated (prefix never surfaced), slow-ok late completion → exactly 1 STALE-DISCARD at the application seam, zero applied
- [ ] **Panic re-verified LIVE:** panic during a real in-flight lab operation → typed Cancelled + bounded drain + generation invalidation (the c1 mechanism against a real network operation, not a mock)
- [ ] **WSL2 in-packet gate (`~/ccp-sp035`, never /mnt/e):** the lab + contract testCommand green on Linux (loopback = real Linux evidence); offline zero-network re-verified (no external traffic from the lab runs — the lab binds 127.0.0.1 only, proven)
- [ ] Sensitive-logging audit: the lab's fake payloads/keys registered as secrets; grep over all committed artifacts → zero hits (SP-018/SP-019 discipline)

### Step 4: Evidence consolidation + pre-completion consult

- [ ] Write `spine-tasks/SP-035-ai-companion-c2/record.md` (archaeology, design, lab shapes, consult verdicts + ACTUAL answering models, engine-review presence, matrix transcripts, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 466/29 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- First REAL provider live on c1's foundation: cancellable request/stream client with token-driven cancellation + generation invalidation; timeout as classifier (never the mechanism); bounded-retry placeholder (typed, §9.2 #6 honored); refusal no-retry typed; malformed/truncated → typed Unavailable never partial
- Remote-host pre-socket rejection in-product (sendAttempts==0)
- LAB matrix green on BOTH platforms (all SP-019 failure shapes handled identically); panic re-verified live against a real in-flight operation; offline zero-network preserved and proven
- Sensitive-logging audit zero hits; contract green both platforms (≥466/29 floor); both solo Fable consults persisted with actual answering models

## Do NOT

- Contact a real Ollama or any external host (lab = 127.0.0.1 only; Ollama absent = named limit, never claimed); implement cloud (typed absence stands); decide retry VALUES (placeholder shape only, §9.2 #6); string-sniff refusals; surface truncated prefixes; partial-apply malformed output; log prompts/completions/lab secrets; rewrite c1's foundation (consume; per-change justification); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); claim Wayland; modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`; set any board row state
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-035): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-035-ai-companion-c2/record.md` (evidence + durable-lesson candidates)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-07-22 (authoring): **admission §8 slice c2 binding (first REAL provider; LAB both platforms; panic re-verified live); SP-033 landed `2f77c934`.** Ollama absence = named limit (lab is the instrument). Retry = placeholder shape only (§9.2 #6). Enabler 2 encoded (no hot docs in worker scope). Headless (LAB = loopback); 4h budget exported at launch. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-07-22 (authoring): Launch: validate → analyze → plan → preflight → detached wave batch (SP-035 + SP-036, 2 lanes) per owner cycle.
