# Task: SP-032 — Quips/sound arbitration slice q2: bark content pipeline + host wiring

## Mission

Execute slice **q2** of the `client/docs/task-board.md` row **"Implement reliable quips and sound arbitration"** (P0) on top of SP-029's landed q1 (arbitration core): the **bark content pipeline** — ordinary/priority bark freshness surfacing, text/audio/emotion payload integrity, mute text-only behavior, disabled phrase persistence — plus **required rapid click cues under voice/video** and **stale-device fallback UX**, and **wiring the DTRH host's `bark` message from `Deferred("voice-arbitration (quips row)")` to `Handled` through q1's arbitration**. Real product code in `client/src/CcpClient.Desktop/Audio/` + `client/src/CcpClient.Desktop/Companion/` (new home for phrase/personality content services) + the DTRH wiring point.

**Honesty framings (binding):** (a) q1's core is the foundation, not a rewrite target — the pipeline CONSUMES `Audio/SoundArbitration.cs`; changes to q1's public surface need per-change justification in record.md (the q1/q2 boundary note: per-item `pacing` parameter is where q2 supplies TEXT-derived timing — `VoicePacingDelay` 2s floor WPF `Speech.cs:112-119`, AI-bonus +5s, per-char 0.02s/char>100); (b) WPF semantics from archaeology (`File.cs:line`): `Services/Bark/` rules engine (BarkRule/Loader/Set/State/Variant/Context), `Services/Companion/BarkService.cs` (freshness/priority/content pipeline), `CompanionPhraseService.cs` (phrase/audio/emotion pairing), disabled-phrase persistence surface, mute behavior; (c) **disabled phrase persistence rides SP-005 machinery** (schema-versioned store, atomic writes, quarantine — never a parallel settings file); (d) audio evidence = backend-event-verified; rapid click cues under voice/video = coexistence evidence (q1's arbitration under concurrent channels — the SP-017 A11 class, now through the real pipeline); Linux = WSLg session facts, no timing claims; (e) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — record in record.md; the orchestrator reconciles at land; (f) the DTRH `bark` wiring keeps presence+shape logging (never bark TEXT/content in logs — SP-016 content-free class); DTRH host runs follow the DISPLAY3 convention + rect-persistence binding + modal-drive rule + orphan-guard rule if headed evidence uses the host.

## Dependencies

- **Task:** SP-029 (q1 landed — arbitration core + the recorded q1/q2 boundary)

## Context to Read First

- `spine-tasks/SP-029-quips-arbitration-q1/record.md` — q1's delivered surface (`Audio/SoundArbitration.cs`, options, the per-item `pacing` seam, device layer) + the q1/q2 boundary note (pacing TEXT inputs)
- `client/docs/audio-backend-spike.md` (SP-017 selection + A11 coexistence class) + `client/docs/port-lessons.md` (SoundFlow off-sync-context; Play-panic-race guard — q2's new start paths must follow the TryStart pattern)
- WPF (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/Bark/` (rules engine: triggers, variants, state, context evaluation), `ConditioningControlPanel/Services/Companion/BarkService.cs` (freshness windows, priority/preemption, queue surfacing, mute text-only, panic cleanup), `CompanionPhraseService.cs` + `PersonalityService.cs` (phrase/audio/emotion payload assembly), the disabled-phrase persistence surface (locate via repo search — where muted/disabled phrases live), `Speech.cs:112-119` (pacing constants), the rapid-click-cue path (DTRH sfx + click-cue consumers)
- The DTRH wiring point (greenfield, landed): `client/src/CcpClient.Desktop/Features/Dtrh/DtrhProtocol.cs` (`bark` currently Deferred) + `DtrhHostWindow.axaml.cs` dispatch — the upgrade touches ONLY the bark outcome + dispatch seam, per the b1–b5 regression discipline
- `client/docs/task-board.md` row "Implement reliable quips and sound arbitration" (acceptance text + q1's named limits)

## File Scope

- `client/src/CcpClient.Desktop/Audio/**` (pipeline additions on q1's core)
- `client/src/CcpClient.Desktop/Companion/**` (new phrase/content services home)
- `client/src/CcpClient.Desktop/Features/Dtrh/**` (bark outcome upgrade + dispatch seam ONLY)
- `client/tests/CcpClient.Tests/**` (pipeline/persistence/wiring tests)
- `client/tests/CcpClient.HeadlessTests/**` (surface tests where honest)
- `spine-tasks/SP-032-quips-content-q2/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Companion/BarkPipeline.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-032-quips-content-q2/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Content-pipeline archaeology + design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): bark rules engine semantics (trigger evaluation, variant selection, state), freshness windows + priority/preemption surfacing, text/audio/emotion payload assembly, mute text-only behavior, disabled-phrase persistence (where/how persisted, re-enable semantics), pacing TEXT inputs (the constants + how WPF computes them), rapid-click-cue requirements, stale-device UX surface
- [ ] Design: `Companion/BarkPipeline.cs` (contract-named) over q1's arbitration — trigger→variant→payload→arbitration path; disabled-phrase store on SP-005 machinery (document shape, quarantine honesty); mute text-only mode (payload degradation, never silent); pacing parameter computation (TEXT-derived, q1 seam); DTRH `bark` outcome upgrade (Deferred→Handled through the pipeline; presence+shape logging); rapid-cue path under concurrent voice/video (coexistence through arbitration, A11 class)
- [ ] **Pre-approach solo consult** (Fable 5, solo) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Pipeline implementation + disabled-phrase persistence

- [ ] `Companion/BarkPipeline.cs` + supporting types: rules evaluation, variant selection, payload integrity (text/audio/emotion assembled as ONE unit — never a torn payload), freshness + priority surfacing through arbitration, mute text-only degradation, pacing computation into q1's seam
- [ ] Disabled-phrase store on SP-005 machinery (schema-versioned document; disable/re-enable round-trips; quarantine → typed Degraded + flagged defaults, never silent)
- [ ] Unit tests: payload integrity (assembly as one unit), freshness/priority ordering, mute degradation, disabled-phrase persistence (round-trip + corrupt→quarantine), pacing math vs WPF cases, TryStart-pattern compliance on every new start path (the Play-panic-race guard)

### Step 3: Rapid cues + DTRH wiring + backend-event evidence

- [ ] Required rapid click cues under voice/video: through-arbitration coexistence evidence (voice + whisper + video + rapid SFX bursts — no starvation, typed overflow only, backend-event-verified on Windows; mechanism facts on Linux)
- [ ] DTRH `bark` upgrade: `DtrhProtocol` outcome Deferred→Handled through the pipeline (presence+shape logging — never bark content); host dispatch seam wired; b1–b5 regression discipline (the DTRH contract suite stays green)
- [ ] **WSL2 in-packet gate (`~/ccp-sp032`, never /mnt/e):** contract testCommand green; Linux mechanism session facts; no timing claims
- [ ] If headed DTRH host evidence is used for the wiring: DISPLAY3 convention + rect-persistence binding + modal-drive rule + orphan-guard rule (port-lessons 2026-07-22)

### Step 4: Evidence consolidation + pre-completion consult

- [ ] Write `spine-tasks/SP-032-quips-content-q2/record.md` (archaeology, design, q1-surface-change justifications if any, consult verdicts + ACTUAL answering models, engine-review presence, evidence transcripts, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 412/29 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Bark content pipeline live on q1's arbitration: rules/variants/payload integrity + freshness/priority surfacing + mute text-only degradation (never silent) + TEXT-derived pacing into q1's seam
- Disabled phrase persistence on SP-005 machinery (round-trip + quarantine-honest)
- Rapid click cues under voice/video proven through arbitration (coexistence, no starvation, typed overflow; backend-event-verified Windows, mechanism facts Linux)
- DTRH `bark` Deferred→Handled through the pipeline with presence+shape logging; DTRH contract suite green (no b1–b5 regression)
- Contract green both platforms (≥412/29 floor); both solo Fable consults persisted with actual answering models

## Do NOT

- Rewrite q1's core (consume it; per-change justification required); build a parallel phrase/settings store (SP-005 machinery only); log bark text/content or media filenames (presence+shape only); claim Linux timing; claim audibility; silently degrade (mute text-only is a TYPED, surfaced mode); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); add packages beyond the admitted set; modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`; set any board row state
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-032): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-032-quips-content-q2/record.md` (evidence + durable-lesson candidates)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-07-22 (authoring): **slice cut q2 per SP-029's recorded q1/q2 boundary** (per-item pacing seam; the row's remaining acceptance items = this packet). DTRH `bark` wiring included (the Deferred label names this row; upgrade touches only the outcome + dispatch seam). Enabler 2 encoded (no hot docs in worker scope). Waved with SP-031 (T-5 anchor re-base — headless tooling; this packet's possible headed DTRH evidence = the wave's only headed lane). mustNotChange intersected against File Scope at authoring (SP-020 lesson). T-11 sizing: headed step possible (DISPLAY3 bindings carried); 4h budget exported at launch.
- 2026-07-22 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached wave batch (SP-031 + SP-032, 2 lanes) per owner cycle.
