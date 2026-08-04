# Task: SP-046 — AI companion slice c7: companion UI surface (chat view + badge/status + memory-clear control)

## Mission

Execute slice **c7** of `client/docs/ai-companion-admission.md` §8 for the `client/docs/task-board.md` row **"Implement AI companion and awareness integration"** (P0): the **companion UI surface** — a chat view wired to the typed pipeline (c1–c6 consume the REAL machinery: pipeline, provider seam, moderation boundary, memory store, awareness service, command executor through the product composition — no product CompositionRoot constructs the pipeline yet; this slice owns the wiring); **badge/status accuracy headed proof** (provenance drives the badge — `AiReply.Generated(text, provenance)` and nothing else; SP-006 capability state drives status — never a registration/selection fact); **refusal bubbles** (interactive class surfaces typed refusals; awareness stays drop-by-type); **user-reachable memory-clear control** (c4's operation with file-content proof; confirm flow per the WPF shape); **awareness consent + cooldown settings surfaces** (the typed states get owner-visible controls); **panic-quiet headed proof** (panic mid-operation → UI quiets, typed Cancelled, nothing partial surfaces). Evidence = **WH-class via avalonia-live on this machine** (the verified 27-tool seat: screenshots + semantic trees + synthetic input + binding errors — the laptop's DISPLAY3-class substitute) + U + K3 visual review (orchestrator at land). **WSL2 named limit** (zero distros — WX render/session facts owner-gated, never faked).

**Honesty framings (binding):** (a) **improve-don't-clone (owner decree 2026-08-04):** behavior parity is the constraint (badge truth, refusal surfacing, clear semantics, panic quiet, toggle/popup behaviors); the visual presentation is DESIGNED to the dashboard grammar's evolution (dark, neon accents, restrained glow, clear hierarchy — the five-theme grammar evolves, not copied); WPF evidence = behavior contracts, NEVER visual templates; (b) **A-013 advisory discipline:** official v12 research FIRST (avalonia-research skill), hand-authored smallest AXAML, then advisory MCP review (`ValidateXaml` only — PASS is never API-validity proof; record accepted/rejected findings with reasons); (c) badge/status truth is load-bearing: the badge derives from reply provenance by TYPE and nothing else; status derives from the SP-006 capability state and nothing else — a decorative badge is a contract violation; (d) **panic-quiet**: panic during a real in-flight operation → the UI shows nothing partial, the operation is typed Cancelled, and the surface returns to a calm state (proof via a real operation against the c2 lab, not a mock); (e) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (f) no Wayland claims; the WSL named limit covers Linux evidence; (g) **assigned obligation (bool-door retirement, board named limit 4):** migrate ALL 6 bool-overload call-site files to the typed `AiAwarenessConsent` overload and DELETE the bool overload (the 6 files per SP-044's grep: `AiMemoryPipelineTests.cs`, `AiModerationCoverageTests.cs`, `AiModerationPipelineBoundaryTests.cs`, `AiOfflineIntegrationTests.cs`, `AiOperationPipelineTests.cs`, `AiProviderLabIntegrationTests.cs` — re-grep at execution time; ALL are in File Scope for this slice); (h) headed evidence windows on this machine are driven through avalonia-live (app launched with `CCP_MCP=1`); captures land in evidence/ with the semantic tree, not pixels alone.

## Dependencies

- **Task:** SP-044 (c6 landed — the executor + gated dispatch this surface composes)

## Context to Read First

- `client/docs/ai-companion-admission.md` §8 c7 row (exact scope + evidence classes) + §2 rule 4 (badge/status honesty) + §5 rule 5 (interactive vs awareness)
- `client/docs/ai-operation-contract.md` §1 (typed replies/badge provenance) + §4 (interactive vs awareness surfacing)
- The **dashboard-design skill** (visual grammar — evolve per the decree; implementation rules; the A-013 advisory chain: contract/screenshots → official v12 docs → hand-authored smallest AXAML → advisory MCP review → real build/headed interaction → K3 review; record accepted/rejected)
- The **app-visual-verification skill** (bounded screenshot verification with image review at the close)
- The **avalonia-research skill** (current v12 API research is MANDATORY before AXAML — the baseline is 12.1.1)
- `spine-tasks/SP-040-ai-companion-c4/record.md` (memory-clear operation + the UI clear-flow cites corrected: `MainWindow.Patreon.cs:912-953`) + `spine-tasks/SP-044-ai-companion-c6/record.md` (executor + the bool-door retirement obligation)
- Landed mechanics (consume): `client/src/CcpClient.Desktop/Ai/**` (the full chain), `client/src/CcpClient.Desktop/Features/` (existing surfaces + the SP-013 popup + SP-014 dispatch precedents), the SP-003 composition root + SP-004 `IUiDispatch` late-bound boundary
- WPF (READ-ONLY, `File.cs:line`): badge mechanism `AvatarTube/AvatarTubeWindow.Speech.cs:319-353` (`IsAiGenerated` → badge); chat input `AvatarTubeWindow.ChatInput.cs:727`; memory-clear UI flow `MainWindow/MainWindow.Patreon.cs:912-953` (confirm, default No); awareness consent/cooldown settings shape (`CompanionPromptSettings.cs` + awareness toggles)

## File Scope

- `client/src/CcpClient.Desktop/Features/Companion/**` (the new surface: view, view model, badge/status/refusal-bubble/clear-control/consent+cooldown surfaces)
- `client/src/CcpClient.Desktop/Ai/**` (composition wiring glue only, if needed — per-change justification)
- `client/src/CcpClient.Desktop/**` (Program/App/CompositionRoot wiring — the SP-023 wiring-file norm: per-file justification in record.md)
- `client/tests/CcpClient.Tests/AiProviderLabIntegrationTests.cs`, `client/tests/CcpClient.Tests/AiMemoryPipelineTests.cs`, `client/tests/CcpClient.Tests/AiModerationCoverageTests.cs`, `client/tests/CcpClient.Tests/AiModerationPipelineBoundaryTests.cs`, `client/tests/CcpClient.Tests/AiOfflineIntegrationTests.cs`, `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs` (the 6 bool-overload migration files)
- `client/tests/CcpClient.Tests/Companion*` (NEW test files — surface logic where honestly testable)
- `client/tests/CcpClient.HeadlessTests/**` (headless draw-level tests where honest)
- `spine-tasks/SP-046-ai-companion-c7/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Companion/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-046-ai-companion-c7/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Archaeology + surface design + avalonia-research + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): badge mechanism (what drives it; when it shows), chat input behavior (send path, disabled states, in-flight affordances), the clear flow (confirm default-No, failure path), awareness consent + cooldown settings surfaces, refusal presentation on the interactive class
- [ ] Design per the dashboard-design skill (evolved grammar; placement decision — window vs dashboard surface — made with evidence and recorded; the composition root wiring for the full AI chain: pipeline + memory store + awareness service + executor, SP-004 owned, SP-003 phases respected, `IUiDispatch` late-bound discipline)
- [ ] **avalonia-research pass (MANDATORY before any AXAML):** current v12 facts for every API the surface needs (compiled bindings, ItemsControl/chat-list shape, pseudo-classes, commands, windows) — cite the sources
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + design + research; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Companion surface + composition wiring

- [ ] `Features/Companion/` surface (chat view wired to the typed pipeline; badge from provenance; status from capability state; refusal bubbles interactive-only; memory-clear control with the confirm flow + failure path; awareness consent + cooldown settings surfaces reading/writing the typed states)
- [ ] Composition wiring (Program/App/CompositionRoot per the wiring-file norm; `CCP_MCP=1` seam untouched; the AI chain composed as SP-004 owned operations)
- [ ] **Bool-door retirement:** migrate the 6 call-site files to the typed overload and DELETE the bool overload (re-grep first; if any call site resists, HARD STOP that part and record — never a partial migration)
- [ ] Unit tests (surface logic: badge truth from provenance, status from capability, refusal bubble class discipline, clear-control flow, consent/cooldown surfaces) + headless draw-level tests where honest

### Step 3: Headed evidence via avalonia-live + A-013 advisory + panic-quiet

- [ ] Launch the app with `CCP_MCP=1`; drive the surface through the avalonia-live seat: window open, chat send against the c2 LAB provider (loopback only — zero external network), **badge accuracy pixels** (Generated badged; Fallback/Unavailable/Refused never badged), **status from capability state**, **refusal bubble** on a blocked turn, **memory-clear control** (confirm default-No flow + file-content proof the document is deleted), **consent/cooldown surfaces** toggling the typed states, **panic-quiet** during a REAL in-flight lab operation (typed Cancelled + calm surface + nothing partial)
- [ ] Per capture: screenshot + semantic tree into `evidence/` (never pixels alone); binding-error capture reviewed (zero binding errors on the surface)
- [ ] **A-013 advisory:** `ValidateXaml` on the hand-authored AXAML (after the v12 research) — record accepted/rejected findings with reasons
- [ ] Sensitive-logging discipline: chat/memory content never in logs/diagnostics; evidence screenshots carry synthetic content only

### Step 4: Evidence consolidation + pre-completion consult

- [ ] Write `spine-tasks/SP-046-ai-companion-c7/record.md` (archaeology, design decisions incl. placement, research citations, consult verdicts + ACTUAL answering models, engine-review presence, headed evidence index, advisory dispositions, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 581/29 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Chat view wired to the REAL typed pipeline through product composition (no mocks in the surface path; loopback lab as the provider in evidence)
- Badge accuracy: provenance-driven, pixel-verified (Generated badged; Fallback/Unavailable/Refused never); status from capability state
- Refusal bubbles on the interactive class (awareness stays drop-by-type); memory-clear control user-reachable with confirm flow + file-content deletion proof
- Awareness consent + cooldown settings surfaces drive the typed states
- Panic-quiet proven against a real in-flight operation
- Bool-overload retired (6 files migrated; overload deleted) or hard-stop recorded
- A-013 advisory dispositions recorded; avalonia-live evidence in evidence/; K3 visual review by the orchestrator at land
- Contract green (≥581/29 floor); both solo consults persisted with actual answering models

## Do NOT

- Clone WPF visuals (improve-don't-clone; behavior parity is the contract) — and never copy first-attempt resource topology; fake the badge/status from anything but provenance/capability state; use MCP-generated themes/controls/output as production code (advisory only; ValidateXaml PASS ≠ API-validity proof); contact any external host (the lab is loopback); log chat/memory content; weaken the c1–c6 machinery to make the surface easy (consume; per-change justification); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`; set any board row state; claim Wayland; fake Linux evidence
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-046): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-046-ai-companion-c7/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-05 (authoring, orchestrator): **admission §8 slice c7 (companion UI surface) per the approved serial cut; SP-044 landed `b1a5b5f8`.** FIRST UI slice: the owner's improve-don't-clone decree encoded (behavior parity = constraint; visuals designed to the evolved grammar); avalonia-live is the headed-evidence instrument on this machine (27 verified tools); A-013 advisory chain + avalonia-research mandatory; bool-door retirement assigned (6 files, all in scope). Enabler 2 (no hot docs). **T-11 sizing: each headed step sized <2h of worker budget; 4h budget exported at launch.** WSL zero-distros named limit. Consult route per the 2026-08-04 rewire. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-05 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached single-lane batch (UI slice runs alone — headed-focus discipline) per owner cycle.
