# Task: SP-047 — Wire companion memory into prompt context (memory→prompt assembly)

## Mission

Execute the `client/docs/task-board.md` P0 row **"Wire companion memory into prompt context (memory→prompt assembly)"** (OPEN, filed 2026-08-05): interactive companion requests assemble the prompt from the memory store's per-turn pairs (provider-neutral) — the c4 store finally gets CONSUMED, completing the AI row's largest named limit. WPF behavior shape: the full dialogue history is sent per request (`LocalAiService.cs:374-390`); the stateless ambient path stays stateless (`:476-502`); enrichment/system blocks are never assembled as dialogue (`:107-135,166-170`). Evidence = **U** (a new pair round-trips INTO the next request's prompt — falsifiable). No WH/WX claims.

**Honesty framings (binding):** (a) **READ-gating is a BEHAVIOR FACT, not a values decision (phase-consult binding):** WPF's `LoadPersistedHistory` checks consent FIRST (`LocalAiService.cs:113`) — consent off ⇒ history is NEITHER read NOR written. Port that behavior exactly. What stays owner-pending is the consent flag's DEFAULT VALUE (WPF baseline `ChatMemoryEnabled` default **true** `CompanionPromptSettings.cs:120` vs the greenfield c4 placeholder **Denied**) — record the tension VERBATIM: WPF companions remember by default; the greenfield placeholder does not (owner question, §9.2 #3, never silently decided); (b) memory content NEVER enters diagnostics/logs (content-free rule) and is NEVER a secret (§10) — the assembled prompt is a transport payload, not a log artifact; (c) **ambient/awareness paths stay stateless** (WPF `:476-502`) — the awareness class never assembles from memory (negative proof); (d) trimming rides the c4 retention MECHANISM (placeholder values stand — no new values decided); (e) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (f) **WSL2 named limit: laptop WSL zero distros — "U both platforms" discharges Windows-only, Linux owner-gated, never faked;** (g) consume the landed chain (c1–c7); per-change justification for any seam extension.

## Dependencies

- **Task:** SP-046 (c7 landed — the surface + composition this assembly plugs into)

## Context to Read First

- `client/docs/task-board.md` row "Wire companion memory into prompt context" (the filed acceptance) + the AI row's named limit 4
- `client/docs/ai-companion-admission.md` §4 (memory design — rules 1, 3, 5) + `client/docs/ai-operation-contract.md` §5 (memory contract) + §12 (content-free)
- `spine-tasks/SP-040-ai-companion-c4/record.md` (store semantics; consent write-gating; the forward-reference this packet discharges; the consult note "revocation BETWEEN operations, not mid-operation") + `spine-tasks/SP-046-ai-companion-c7/record.md` §7 item 1 (the scope divergence + the honest non-claim line this packet updates)
- Landed mechanics (consume): `client/src/CcpClient.Desktop/Ai/AiMemoryStore.cs`, `AiOperationPipeline.cs`, `Features/Companion/` (the surface's request path)
- WPF (READ-ONLY, `File.cs:line`): prompt assembly `Services/AIService/LocalAiService.cs:374-390` (history consumed per request); load-gating `:104-129` (`:113` consent check); stateless ambient `:476-502`; enrichment exclusion `:107-135,166-170`; persist call `:644-646`

## File Scope

- `client/src/CcpClient.Desktop/Ai/**` (prompt-assembly seam: memory→request construction on the interactive path)
- `client/src/CcpClient.Desktop/Features/Companion/**` (surface wiring if needed — per-change justification; the honest non-claim line gets updated when memory is genuinely consumed)
- `client/tests/CcpClient.Tests/AiMemory*` + `client/tests/CcpClient.Tests/Companion*` (round-trip-into-prompt proofs, read-gating, ambient-stateless, trimming)
- `client/tests/CcpClient.HeadlessTests/Companion*` (surface tests where honest — likely the non-claim line flip only)
- `spine-tasks/SP-047-memory-prompt-context/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Ai/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-047-memory-prompt-context/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Archaeology + assembly design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): the exact prompt assembly (which turns, order, system/enrichment handling, trimming at assembly vs at persist, stateless ambient shape, load-gating)
- [ ] Design: the assembly seam on the interactive path (memory store → request prompt; where it composes in the pipeline/participant; consent-read-gating per honesty framing (a) — port `:113` exactly: consent off ⇒ neither read nor written; the c4 Denied placeholder default stands with the WPF-true tension recorded verbatim; ambient stays stateless; the surface's honest non-claim line updates when consumption is real)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Assembly implementation + tests

- [ ] Assembly seam: interactive requests carry the store's pairs (provider-neutral; order per WPF; trimming rides the c4 mechanism; consent-read-gated per (a))
- [ ] Unit tests: **a new pair round-trips INTO the next request's prompt (falsifiable — the prompt's content reflects the persisted pairs)**; consent off ⇒ neither read nor written (read-gating proof); ambient/awareness requests carry NO memory (negative proof); enrichment/system blocks never assembled; trimming per mechanism; the Denied placeholder default recorded

### Step 3: Surface honesty + evidence + pre-completion consult

- [ ] The companion surface's non-claim line updates to the now-true state (memory IS consumed as context; the line reflects the typed placeholder consent default honestly — never implying recall under Denied)
- [ ] Offline zero-network re-verified (assembly is pure local read); content-free diagnostics maintained (prompt content never in any record/log)
- [ ] Write `spine-tasks/SP-047-memory-prompt-context/record.md` (archaeology, design, consult verdicts + ACTUAL answering models, engine-review presence, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 601/33 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Interactive companion requests assemble the prompt from the memory store's pairs (round-trip INTO the next request proven)
- Read-gating ported exactly (consent off ⇒ neither read nor written); the WPF-true vs placeholder-Denied tension recorded verbatim (owner question)
- Ambient/awareness paths stateless (negative proof); enrichment never assembled; trimming rides the c4 mechanism
- Surface honesty updated (the non-claim line reflects real consumption, typed-default-honest)
- Contract green (≥601/33 floor); both solo consults persisted with actual answering models

## Do NOT

- Decide the consent default VALUE (owner question — record the WPF-true/placeholder-Denied tension verbatim); assemble memory into ambient/awareness paths; put prompt/memory content in logs/diagnostics; persist anything new (c4's machinery stands); imply recall under a Denied consent state; edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`; set any board row state; claim Wayland; fake Linux evidence
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-047): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-047-memory-prompt-context/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-05 (authoring, orchestrator): **board row filed at the wave-9 land (land-consult follow-up; phase re-derivation consult ordered this first).** Read-gating encoded as a BEHAVIOR FACT (`LocalAiService.cs:113` ported exactly) with the consent-DEFAULT tension recorded verbatim (WPF true vs c4 placeholder Denied — owner question, never decided). Enabler 2 (no hot docs). Headless (U-only); 4h budget exported at launch. WSL zero-distros named limit. Consult route per the 2026-08-04 rewire. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-05 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-047 + SP-048, 2 lanes — disjoint scopes: memory/prompt vs publish/payload) per owner cycle.
