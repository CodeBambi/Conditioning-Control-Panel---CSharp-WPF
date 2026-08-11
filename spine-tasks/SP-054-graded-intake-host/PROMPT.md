# Task: SP-054 — Graded Intake web-core host: window + bridge + stores + profiler + session drafting (v6.6.3)

## Mission

Execute the `client/docs/task-board.md` P1 row **"Graded Intake web-core host: window + bridge + stores + profiler + session drafting (v6.6.3)"** (OPEN, filed 2026-08-05): the Weekly Intake Pass's web-core host — the window class (ChaosWebViewHost parity, riding the landed b-series pattern), the full two-way bridge vocabulary (6 message types out / 12 in, C#-pinned per SP-050's table), 3 stores, the profiler (deterministic pure function), the session-drafting sink, and `loom-save` against the SHARED b4 `DtrhLoom` store. **Degraded-delivery contract (VERBATIM, binding):** the intake RUNS and drafts a session; the drafted session is never runnable (no session engine — punches pend forever, silently); the VO corpus may be absent (chimes survive via the dtrh-tree borrow); niche falls back to bambi (WPF's own last-resort); the AI runs the page's deterministic local stub (empty token = WPF's own no-network fallback). Linux = named limit (WSL zero distros — owner-gated, never faked). No Wayland claims.

**Honesty framings (binding):** (a) **privacy boundaries never widened** (per the row + SP-050 verbatim): the `ai` auth token is a Patreon bearer handed to the page for ITS proxy call — empty token → the page's own deterministic local stub, no network; **mic host-gated by consent** (`micEnabled = MicConsentGiven`); user flash-image paths, enabled subliminal phrases, and whisper audio handed to the page = **presence+shape logging only**; the 4-digit subject id is local fiction, never transmitted; (b) **the pass cadence is intentionally NOT in this packet** (the BLOCKED inventory's item: entitlement/auth + dashboard rows — this packet ships the host machinery with the pass service's state machine as typed placeholders; completion-spend semantics land tested against fixtures, the entitlement hooks stay seams); (c) **serving discipline:** the intake tree serves under the page origin through the SAME §4 contract class as DTRH (GET-only, overlay-first, Range, MIME, CORS, traversal refusal, token) — the payload tree stays READ-ONLY; (d) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (e) every new protocol message lands typed (unknown/forward-version/malformed tolerance per b2's vocabulary discipline); (f) **authoring obligation (the row's own text): the packet PINS OR TYPES `ping` and `payload-state`** (payload-attested in `intake/web-shim.js:218-227`; C# emit sites never pinned in the audit).

## Dependencies

- **Task:** SP-050 (the audit whose table + degraded-delivery contract this row carries)

## Context to Read First

- `client/docs/task-board.md` row "Graded Intake web-core host" (the acceptance text incl. the degraded-delivery contract + privacy boundaries + dependencies)
- `spine-tasks/SP-050-v663-obligation-audit/record.md` §Delta 6 (the obligation table cell — full bridge vocabulary, stores, provisions, the ping/payload-state gap, privacy boundaries)
- The INTAKE payload (READ-ONLY): `ConditioningControlPanel/Resources/web/intake/` — `web-shim.js` (the bridge vocabulary page-side: `init`, `ping`, `payload-state`, `ready`, `log`, `heartbeat`, `pong`, `quiz-result`, `boot-error`, `fullscreen-set`, `exit`, `intake-close`), `ui/fullscreen.js:102`, the audio serving (`core/audioSrc.js:4-12` + `render/audio.js:222-229,449-451,1104`)
- WPF host (READ-ONLY, against git main): `Services/Quiz/IntakeHostService.cs` (the window + bridge + provisions + watchdogs + completion loop), `Services/Progression/IntakePassService.cs` (the pass state machine: ISO-week, completion-spend, rollback guard, fail-closed, dual-provider refund), `Services/Progression/IntakePunchCardService.cs` + `IntakePunchCardState.cs` (the punch card: first hole free, holes 2-8 need drafted-session runs, silent stamping while `UiEnabled=false`), `IntakeProfiler.cs` (deterministic QuizRunResult → five axes), `QuizSessionGenerator.cs` (session drafting sink), `IntakeNiche.cs` (niche mapping + bambi fallback)
- Landed mechanics (consume): `Features/Dtrh/` (the b-series window/watchdog/transport patterns incl. `DtrhLoomWindow`'s stripped sibling shape + `DtrhLoom` shared store), SP-005 persistence machinery, SP-006 capability probes
- The avalonia-live usage map (27 verified tools; `windowId` silent-drop quirk: use `target`/`handle`, validate capture dimensions BEFORE the evidence pass)

## File Scope

- `client/src/CcpClient.Desktop/Features/Intake/**` (the host: window, bridge/protocol, pass service, punch card, profiler, drafting sink, serving routes)
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhLoom*.cs` (loom-share glue only, if needed — per-change justification)
- `client/tests/CcpClient.Tests/Intake*` + `client/tests/CcpClient.Tests/DtrhLoom*` (unit tests)
- `client/tests/CcpClient.HeadlessTests/Intake*` (where honest)
- `spine-tasks/SP-054-graded-intake-host/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Intake/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**` |
| artifactsMustExist | `spine-tasks/SP-054-graded-intake-host/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Archaeology + host design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, against git main, `File.cs:line`): the window class (profile dir, autoplay, lockdown, fullscreen persist, minimize/restore, 20s-heartbeat relaunch-once, 1200ms exit watchdog); the FULL bridge vocabulary (6 out / 12 in with exact message shapes); the pass state machine (ISO-week key, Monday 00:00 local, completion-spend ONLY, >5min future-stamp = rollback, fail-closed to Spent on exception, dual-provider refund); the punch card (first hole free, holes 2-8 need drafted-session runs ≥50%/natural end, silent stamping while `UiEnabled=false`, 30s dirty-flush + atomic .tmp-swap, load repairs); the profiler (the exact axis math + exclusion rules); the provisions inside `init` (niche/caps/endless/steerValve/priorRun/micEnabled/media manifest/subliminal pool/subject id/ai token); the serving origins (page origin, audio borrow from the dtrh tree)
- [ ] Design: `Features/Intake/` host (window + `IntakeProtocol` typed vocabulary per b2's tolerance discipline + `IntakePassService` placeholder-shaped state machine + `IntakePunchCard` on SP-005 machinery + `IntakeProfiler` pure function + the drafting sink + serving routes through the §4 contract class + `loom-save` against the shared b4 store); the degraded-delivery contract encoded (drafted sessions marked never-runnable typed, VO absence typed, niche fallback, AI stub); ping/payload-state PINS OR TYPES per the authoring obligation
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Host machinery + bridge + stores + profiler

- [ ] The window class (capability-driven surface per b-series; profile dir; autoplay flag; fullscreen persist; heartbeat watchdog + relaunch-once; 1200ms exit watchdog; minimize/restore ducking)
- [ ] `IntakeProtocol` — the full typed vocabulary (6 out / 12 in; unknown/forward-version/malformed tolerance; ping + payload-state pinned or typed per the obligation)
- [ ] `IntakePassService` (placeholder-shaped: ISO-week arithmetic incl. the New-Year guard, completion-spend, rollback guard, fail-closed — entitlement hooks as typed seams); `IntakePunchCard` on SP-005 machinery (own named owner; first-hole-free; silent stamping while hidden — WPF `UiEnabled=false` parity); `IntakeProfiler` (the exact axis math as a pure function with the WPF case matrix); the session-drafting sink (writes the drafted session marked never-runnable per the degraded contract)
- [ ] Unit tests: bridge parse/classify/tolerance, pass-week arithmetic, punch-card hygiene, profiler matrix vs WPF cases, store round-trips, degraded-contract typings

### Step 3: Serving + loom-share + headed evidence + pre-completion consult

- [ ] The intake tree serves under the page origin through the §4 contract class (GET-only, overlay-first, Range, MIME, CORS, traversal refusal, token; the audio-borrow from the dtrh tree proven; payload READ-ONLY)
- [ ] `loom-save` handled against the SHARED b4 `DtrhLoom` store (file-content proof; the store path shared, never a second loom root)
- [ ] **Headed evidence (Windows, avalonia-live + harness class):** the intake page boots on the REAL host (engine live), `init` provisions land (niche fallback bambi recorded; mic gated off recorded; ai stub recorded), a scripted `quiz-result` through the REAL dispatch → drafting + completion-spend (file-content proofs: drafted session marked never-runnable; pass Spent for THIS ISO week; punch stamped); `intake-close` abort → NO spend (typed); `fullscreen-set` round trip; watchdog relaunch-once (b5 pattern re-verified on this host); captures = screenshot + semantic tree, dimension-validated per the windowId quirk rule
- [ ] Write `spine-tasks/SP-054-graded-intake-host/record.md` (archaeology, design, consult verdicts + ACTUAL answering models, engine-review presence, evidence index, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 683/33 floor; TRX logger attached per the template amendment)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- The intake host window boots the page (engine live) with the full typed bridge vocabulary (6 out / 12 in; ping/payload-state pinned or typed)
- `init` provisions land (niche fallback bambi; mic consent-gated; ai local stub; media manifest; subject id local-only)
- `quiz-result` through the real dispatch → profiling + session drafting (marked never-runnable) + completion-spend (ISO-week) + punch stamp (first-hole-free); `intake-close` abort = NO spend; fail-closed on exception
- `loom-save` against the shared b4 store (file-content proof); serving through the §4 contract class (audio borrow proven)
- Privacy boundaries honored (empty token → no network; presence+shape logging; subject id never transmitted)
- Linux named limit; contract green (≥683/33 floor); both solo consults persisted with actual answering models

## Do NOT

- Build the session engine (the drafted session is marked never-runnable — the degraded contract); build the entitlement/auth hooks beyond typed seams (BLOCKED inventory); widen the privacy boundaries (no bearer transmission by the host, no subject-id transmission, presence+shape only); build the dashboard cadence UI (flip tile/nudge = BLOCKED); copy WPF sound/VO content (the borrow is the serving path; the pack pipeline is a Decisions-needed item); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**`; set any board row state; claim Wayland; fake Linux evidence
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-054): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-054-graded-intake-host/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-11 (authoring, orchestrator): **row filed at the wave-12 land (SP-050 audit, size L; gets a wave to itself per the wave-12 land consult).** Degraded-delivery contract + privacy boundaries + ping/payload-state authoring obligation encoded verbatim from the row. Enabler 2 (no hot docs). avalonia-live = headed instrument (windowId quirk rule). **T-11 sizing: each headed step <2h; 4h budget exported at launch.** WSL zero-distros named limit. Consult route per the 2026-08-04 rewire. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-11 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached single-lane batch (L-size host runs alone) per owner cycle.
