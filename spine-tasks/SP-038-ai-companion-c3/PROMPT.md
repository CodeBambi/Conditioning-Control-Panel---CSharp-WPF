# Task: SP-038 — AI companion slice c3: moderation boundary

## Mission

Execute slice **c3** of `client/docs/ai-companion-admission.md` §8 for the `client/docs/task-board.md` row **"Implement AI companion and awareness integration"** (P0): the **moderation boundary** — every-surface + every-command-field wiring per admission §3 (input side: every text field entering an AI request; output side: every model-produced text field shown, spoken, persisted, or executed; every free-text command field pre-execution), typed refusal surfacing per operation class (interactive surfaces; awareness drops by type — contract §4 rule 3), escalation counter mechanism with placeholder thresholds (WPF baseline `ModerationCounter.cs:84-85,108-125`: 3-hit one-shot warning / 5-hit cooldown on user chat input — mechanism admitted, VALUES owner-pending), and policy-document injection with the SP-019 verdict-rejected default posture (no category, wordlist, or soft-hit value invented — §9.2 owner question). Evidence = **U (boundary-coverage tests enumerate the surface/command-field inventory)**; no WH/WX claims.

**Honesty framings (binding):** (a) **COVERAGE HONESTY (the admission's own binding):** the boundary tests distinguish "surface exists and is wired" from "seam reserved for a future surface" — some §3 surfaces (community prompts, quiz templates) do NOT exist in the greenfield client; c3 claims the wired ones and reserves the rest, NEVER claiming coverage of a nonexistent surface; (b) **no policy VALUES decided** — category list, wordlist contents, soft-hit handling, escalation thresholds stay owner-pending (§3 rule 5, §9.2); the guard evaluates an INJECTED policy document whose default is the SP-019 "verdict-rejected shape only" posture; the WPF 3-hit/5-hit values are recorded as baseline, never as decision; (c) the sentinel-string refusal channel (`ModerationRefusal.cs:53-54`) is REJECTED — refusals are typed values carrying `ModerationSource` + category (§3 rule 2); (d) the guard lives OUTSIDE the model — user-authored prompt sections can never widen or bypass it (§3 rule 3); (e) consume c1's pipeline + c2's provider (per-change justification for any seam extension); content-free diagnostics maintained — moderation verdicts ride typed values and stable codes, never logged content; every new log site joins the SP-018 redaction registry; (f) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (g) no Wayland claims; **WSL2 named limit: this laptop has WSL with ZERO distros (`wsl -l -q` empty, exit 0) — the "U both platforms" evidence class discharges Windows-only with the Linux run recorded as owner-gated, never faked.**

## Dependencies

- **Task:** SP-035 (c2 landed — provider + lab; the boundary wires into the same pipeline)

## Context to Read First

- `client/docs/ai-companion-admission.md` §3 (moderation design — boundary placement, verdict taxonomy, guard-outside-model, escalation mechanism, values-pending) + §8 c3 row (exact scope + evidence class + coverage honesty) + §9.2 (owner-question ledger)
- `client/docs/ai-operation-contract.md` §7 (moderation — rules 1–5) + §4 rule 3 (awareness drops by type) + §12 (content-free diagnostics)
- `spine-tasks/SP-033-ai-companion-c1/record.md` + `spine-tasks/SP-035-ai-companion-c2/record.md` (pipeline surface, provider seam, consult dispositions, evidence classes)
- Landed mechanics (consume): `client/src/CcpClient.Desktop/Ai/` (pipeline, vocabulary, validator, diagnostics, provider)
- WPF (READ-ONLY, `File.cs:line`): the 4 moderation call sites (`Services/AiService.cs:274,409`; `Services/AIService/LocalAiService.cs:415,555`; `Services/Quiz/QuizService.cs:561`); escalation baseline `Services/Moderation/ModerationCounter.cs:84-85,108-125`; sentinel `Services/Moderation/ModerationRefusal.cs:53-54`; removed keyword pre-check `Services/KeywordTriggerService.cs:1294-1301`; first-attempt toggle-only command path `CCP.Avalonia/Services/Commands/AiCommandService.cs:87-103` (lessons-only)

## File Scope

- `client/src/CcpClient.Desktop/Ai/**` (boundary mechanism: policy seam, verdict taxonomy surfacing, escalation counter, pipeline wiring)
- `client/tests/CcpClient.Tests/**` (boundary-coverage inventory tests, verdict surfacing per class, escalation mechanism, placeholder-policy behavior)
- `client/tests/CcpClient.HeadlessTests/**` (surface tests where honest — likely none; recorded if absent)
- `spine-tasks/SP-038-ai-companion-c3/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Ai/AiModerationBoundary.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-038-ai-companion-c3/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Archaeology + surface/command-field inventory + boundary design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): the 4 call sites' exact input/output positions; `ModerationCounter` mechanism (hit counting, warning/cooldown transitions, state location); sentinel-channel shape (REJECTED); the removed keyword pre-check (why the boundary is uniform instead)
- [ ] Greenfield surface/command-field INVENTORY (typed, in record.md): every surface per §3 that EXISTS in the client today (c1 pipeline interactive + awareness operation paths; c2 provider output path; free-text command fields that exist in the envelope vocabulary) marked **wired**, every §3 surface that does not exist (community prompts, quiz templates, …) marked **reserved** with the seam that will carry it — coverage-honesty table, never a blanket claim
- [ ] Design: `AiModerationBoundary.cs` (contract-named) — injected policy document (default = verdict-rejected posture; shape validated, no values invented); verdict taxonomy per §3 rule 2 (Pass / Block(category, surface) / SoftHit(category, surface) — SoftHit handled by placeholder policy, typed); boundary position in the pipeline (input at operation admission, output before application/execution — consume c1's seam, per-change justification if extended); typed refusal surfacing per operation class (interactive: surfaced typed; awareness: dropped by type); escalation counter MECHANISM (typed hit counter + cooldown state consulted at operation admission; placeholder thresholds with WPF 3-hit/5-hit recorded as baseline; state on SP-005 machinery or session-scoped — decide with justification recorded)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + inventory + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Boundary mechanism + pipeline wiring

- [ ] `AiModerationBoundary.cs` + policy-document seam + verdict types on c1's pipeline (input at admission, output before application; interactive surfaced typed, awareness dropped by type)
- [ ] Escalation counter mechanism (typed, placeholder thresholds, consulted at admission; never silently deciding values)
- [ ] Unit tests: taxonomy round-trips; placeholder-policy default = verdict-rejected posture (a policy document with a test-only category blocks BY INJECTION, proving the guard evaluates the injected document, never a hardcoded list); interactive-surfaced vs awareness-dropped classes; guard-outside-model proof (a user-authored prompt section attempting to widen the guard is ineffective by construction)

### Step 3: Coverage-honesty tests + escalation behavior + offline/diagnostics re-verify

- [ ] **Boundary-coverage tests enumerate the inventory:** for every inventory row, an executable assertion — wired surfaces produce typed verdicts through the real pipeline (input side AND output side where the surface exists); reserved surfaces assert the SEAM exists and is typed `Reserved(future-surface)` (never a coverage claim); the inventory test FAILS if a new surface appears in the codebase unregistered (completeness tripwire, SP-009-sweep-class discipline)
- [ ] Escalation mechanism tests: hit counting transitions through the placeholder thresholds (warning/cooldown states typed; consulted at admission — a cooling-down operation is typed, not silently allowed); state location behavior per the Step-1 decision
- [ ] Offline zero-network re-verified (the boundary is pure local evaluation — prove no network paths); content-free diagnostics maintained (schema proof green); any new log site registered in the redaction registry

### Step 4: Evidence consolidation + pre-completion consult

- [ ] Write `spine-tasks/SP-038-ai-companion-c3/record.md` (archaeology, inventory table with wired/reserved dispositions, design, consult verdicts + ACTUAL answering models, engine-review presence, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 492/29 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Moderation boundary live in the real pipeline: every EXISTING §3 surface/command field wired input+output with typed verdicts (coverage-honesty inventory green; reserved surfaces typed, never claimed)
- Typed refusal surfacing per operation class (interactive surfaced; awareness dropped by type); escalation counter mechanism with placeholder thresholds (no values decided; WPF baseline recorded)
- Policy-document injection with the verdict-rejected default posture (no category/wordlist invented; test-only injected policy proves the guard evaluates the document)
- Offline zero-network preserved; content-free diagnostics maintained; contract green (≥492/29 floor); both solo consults persisted with actual answering models

## Do NOT

- Decide or invent policy VALUES (categories, wordlists, soft-hit handling, escalation thresholds — §9.2 owner question; WPF values = recorded baseline only); claim coverage of nonexistent surfaces (coverage honesty — reserved is typed, never implied); string-match refusal channels (sentinel channel REJECTED); put the guard inside the model/prompt (user-authored sections can never widen it); rewrite c1/c2 foundations (consume; per-change justification); add network paths (boundary is pure local evaluation); log moderated content or policy-document contents (content-free rule); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); claim Wayland; fake WSL2/Linux evidence (named limit recorded); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`; set any board row state
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-038): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-038-ai-companion-c3/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-04 (authoring, orchestrator): **admission §8 slice c3 (moderation boundary) per the approved serial cut; SP-035 landed `8efd60b4`.** Coverage honesty + values-pending + guard-outside-model encoded from §3/§8 verbatim. Enabler 2 (no hot docs). Headless (boundary = mechanism + tests); 4h budget exported at launch. WSL zero-distros named limit encoded in honesty framing (g). Consult route per the 2026-08-04 rewire. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-04 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-038 + SP-039, 2 lanes) per owner cycle.
