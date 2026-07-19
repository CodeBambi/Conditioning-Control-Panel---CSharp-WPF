# Task: SP-008 — build tiered targeted verification harness

## Mission

Execute `client/docs/task-board.md` row 7 (**"Build tiered targeted verification harness"**, P0, Phase 1 of `spine-tasks/CONTEXT.md`). Deliver `client/docs/verification-harness.md` plus a working harness that REPLACES the rejected first-attempt whole-app smoke/layer strategy with **four honest tiers**: (1) fast affected build/unit/headless checks that do NOT launch the app; (2) targetable one-surface/state capture + headed actions for task close; (3) K3 image inspection driven by a **named-check manifest** (defect-specific assertions, e.g. card border geometry); (4) theme/language/platform matrices ONLY at named milestones/releases (define the hook, do not run matrices). The row owns the **`Avalonia.Headless.XUnit` admission decision** (proposal §6) — admission requires evidence (restore/build/real test green on Windows AND WSL2), not documentation. Runtime budgets are **measured** (Windows + WSL2, cold vs incremental stated separately) and the targeted gate must **provably catch a seeded visual regression** via a re-runnable self-test.

## Dependencies

- **Task:** SP-007 (the dashboard card surface is the harness's first real consumer; its ad-hoc headed-smoke.ps1 + layout-probe + capture patterns are the raw material to formalize)

## Context to Read First

- `client/docs/task-board.md` row 7 + gate history
- `client/docs/architecture.md` — A-012 (targeted checks over blanket sweeps), A-014 (YAGNI: checks with a real current consumer only)
- `client/docs/architecture-proposal.md` — §2 (xunit.v3 chosen BECAUSE v12 headless requires it — compatibility expected, not proven), §6 (headless admission + visual harness = this row's decision)
- `client/docs/first-attempt-lessons.md` + `first-attempt-systemic-lessons.md` — the rejected whole-app smoke/layer strategy (outcomes only)
- `spine-tasks/SP-007-first-visible-slice/record.md` — the harness patterns to formalize: layout probe (no UIA peers for Border/Grid/StackPanel), SetWindowPos-TOPMOST capture pattern, XGetImage WSLg capture, headed-smoke.ps1 structure, AVLN2000 throwaway-edit negative proof, named manual gates
- `client/docs/runtime-capability-contract.md` — the evidence-class discipline this row extends to test evidence
- Required skills: load `port-feature`, `avalonia-research`, `app-visual-verification` before Step 1

## File Scope

- `client/tools/verify/**` (harness: thin PS/Python glue + one cross-platform .NET console assertion tool + check manifest)
- `client/tests/CcpClient.HeadlessTests/**` (NEW separate headless test project — see Step 2)
- `client/tests/CcpClient.Tests/**` (unit tests for the assertion tool's logic; existing 85 tests must stay green untouched in behavior)
- `client/docs/verification-harness.md` (deliverable)
- `client/CcpClient.sln` (add the two new projects only)
- `client/docs/task-board.md` (row-7 evidence edit only)
- `spine-tasks/SP-008-verification-harness/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/verification-harness.md`, `client/tools/verify/`, `client/tests/CcpClient.HeadlessTests/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `.spine/**` |
| artifactsMustExist | `client/docs/verification-harness.md`, `spine-tasks/SP-008-verification-harness/record.md` |

**Review Level 2 (plan + code)** — every later row's evidence depends on this harness. Call `spine_review_step` after each step. Engine reviews are empirically dead (zero reviews in SP-001…SP-007 — T-2 open); if `spine_review_step` returns skipped, record it in record.md and rely on the mandatory Fable consults. Do not stall.

## Steps

### Step 1: Pre-approach consult, admission spike design, harness doc skeleton

- [ ] **Pre-approach solo consult** (Fable 5, solo) with the planned tier design, evidence-class rule, manifest shape, and self-test design; verdict text in record.md BEFORE checkbox (write-then-check). Keep questions few/pointed — Fable truncates
- [ ] Update STATUS.md before starting work
- [ ] v12 research (avalonia-research): current `Avalonia.Headless.XUnit` v12 facts (setup attributes, `[AvaloniaTest]`, `GetLastRenderedFrame` semantics, xUnit v3 requirements) from official sources — record URL + freshness
- [ ] Write `client/docs/verification-harness.md` skeleton: tier definitions; **evidence-class rule** (every check declares `draw-verified` or `presentation-verified`; headless frames are Skia draw output — no compositor/window/DPI/activation/occlusion — and can ONLY satisfy draw-level assertions; a headed Windows/WSLg gate is NEVER dischargeable by a headless frame); **"affected" defined concretely** (tier-1 selection by csproj path, matching how testCommands narrow); budget table (empty, filled in Step 4); check-manifest schema (surface, state, region, property, tolerance); self-test usage

### Step 2: Headless admission spike (evidence-gated)

- [ ] Restore `Avalonia.Headless.XUnit` pinned to **12.1.0** (exact baseline match) in a NEW `client/tests/CcpClient.HeadlessTests/` project — `[assembly: AvaloniaTestApplication]` is assembly-wide, so it MUST NOT live in `CcpClient.Tests` (the 85 landed tests stay unpolluted; tiers become selectable by csproj path)
- [ ] Admission evidence: build + at least one REAL `[AvaloniaTest]` interaction test against the dashboard card (e.g., pseudo-class/classes applied on toggle-viewmodel state, arranged DIP bounds of the card, binding resolution) green on **Windows AND WSL2** (native-dir copy, SP-005/007 pattern) — recorded in record.md. If the package fails admission, STOP and record the blocker (do not substitute another package)
- [ ] Headless tests assert draw-level facts ONLY (style/pseudo-class applied, arranged bounds, brush change) — no presentation claims
- [ ] Unit tests for the slice stay in `CcpClient.Tests`; the contract testCommand builds the solution and runs BOTH test projects

### Step 3: Harness implementation (tiers 2–3)

- [ ] `client/tools/verify/`: thin PowerShell (+ Python for XGetImage) glue formalizing SP-007's patterns — launch app, `SetWindowPos`-TOPMOST raise, UIA/layout-probe reads, one-surface capture BY NAME (`--surface dashboard --state lit|unlit`), WSLg XGetImage capture path. Scripts stay thin: all pixel/geometry logic lives in the console tool
- [ ] **One cross-platform .NET console assertion tool** (zero new packages; decode captures via `Avalonia.Media.Imaging.Bitmap` — `System.Drawing` is Windows-only, do NOT use it): reads the check manifest, evaluates named checks (region pixel sampling, border geometry, tolerance) against a capture, exits non-zero on first failed named check with the check name
- [ ] **Check manifest** (`checks.json` or equivalent): named checks scoped to REAL current consumers (the SP-007 dashboard card lit/unlit states, capability surface) — no speculative checks for surfaces that don't exist (A-014)
- [ ] Assertion-logic unit tests in `CcpClient.Tests` (synthetic bitmaps: pass case, per-check fail case, tolerance boundary)

### Step 4: Seeded-regression self-test, budgets, K3 integration

- [ ] **Re-runnable self-test mode** (documented command in the harness doc): throwaway-edit pattern exactly like SP-007's AVLN2000 proof — edit the REAL AXAML (break the card border brush/thickness), build, capture, assert the SPECIFIC NAMED check fails, restore, assert green. NO defect-injection flags/switches in product code. Record one full self-test run in record.md
- [ ] **Measured budgets:** time each tier on Windows AND WSL2, cold vs incremental stated separately; record actual numbers in the doc; budget = observed + stated headroom. Never invent numbers
- [ ] **K3 integration:** the manifest is what the K3 review step consumes (check name → region/property/tolerance + capture path) — run one real K3 review of card lit + unlit captures against the manifest via the `app-visual-verification` skill; record verdict. Milestone-matrix hook: document the tier-4 trigger (named milestone/release only), do NOT run matrices
- [ ] WSL2 gate (in-packet): full contract testCommand green on native-dir copy + the tier-2 WSLg capture path exercised against the real surface (X11 session facts recorded; no Wayland claim)

### Step 5: Evidence, board reconciliation, pre-completion consult

- [ ] Write `spine-tasks/SP-008-verification-harness/record.md`: admission evidence, design decisions, consult verdicts, measured budgets (all four cells), self-test transcript summary, K3 verdict, surprises; **record engine-review presence/absence** (T-2)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the diff + harness doc; verdict text in record.md
- [ ] Update `client/docs/task-board.md` row **"Build tiered targeted verification harness"** to `WIP` with evidence citing record.md — never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 6: Testing & Verification

- [ ] Contract testCommand passes (solution build + BOTH test projects)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- `verification-harness.md` defines the four tiers, the draw/presentation evidence-class rule, concrete tier-1 "affected" selection, the manifest schema, self-test usage, and MEASURED budgets (Windows + WSL2, cold/incremental)
- `Avalonia.Headless.XUnit@12.1.0` admission evidenced (restore/build/real test green on both platforms) in its own project; 85 landed tests untouched and green
- Harness captures one surface/state by name on Windows AND WSLg; named-check manifest evaluated by the console tool; assertion logic unit-tested
- Seeded-regression self-test provably trips a SPECIFIC named check on the real AXAML and restores green (re-runnable command documented)
- One real K3 manifest-driven review recorded; tier-4 matrices documented as milestone-gated only
- Both solo Fable consults persisted; STATUS.md accurate; board row `WIP` (not `DONE`); no tracked changes outside File Scope

## Do NOT

- Modify `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`, `.pi/`
- Touch `ConditioningControlPanel/**` (read-only evidence)
- Admit any package beyond `Avalonia.Headless.XUnit@12.1.0` (this row's explicit decision); if headless fails admission, STOP with a named blocker — no substitutes
- Put headless attributes/tests in `CcpClient.Tests` (assembly-wide pollution)
- Use `System.Drawing` anywhere in the harness (Windows-only); no presentation claims from headless frames
- Add defect-injection flags/switches to product code (throwaway-edit pattern only)
- Build speculative checks, a generic assertion framework, or tier-4 matrix automation beyond the documented hook (A-014; matrices run at named milestones only)
- Weaken the 85 landed tests or SP-003…SP-007 invariants
- Invent budget numbers (measure cold AND incremental, both platforms)
- Use `consult` council mode (solo Fable 5 only); set any board row to `DONE`; fake STATUS.md/review notes

## Git Commit Convention

- `feat(SP-008): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/verification-harness.md` (deliverable), `client/docs/task-board.md` (row-7 evidence), `spine-tasks/SP-008-verification-harness/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (append only on durable surprise); `.spine/spine-config.json` `testing.*` — the orchestrator updates it post-land if the contract testCommand (now including the headless project) should become the default (note in record.md)

## Amendments

- 2026-07-19 (authoring): **pre-authoring Fable consult RAN (two solo consults; first truncated mid-Q1.3 — recovered with a pointed follow-up per precedent; truncation labeled).** Verdicts applied: (a) headless ADMITTED in this row but evidence-gated: pinned 12.1.0 restore/build/real-test green on Windows AND WSL2 before harness work builds on it, separate `CcpClient.HeadlessTests` project (assembly-wide attribute; tier selection by csproj path); (b) evidence-class rule: headless frames = draw-level only, never presentation; headed gates never discharged by headless frames; (c) harness at `client/tools/verify/` with thin scripts + ONE cross-platform .NET console assertion tool (Avalonia `Bitmap` decode, zero packages, no `System.Drawing`), assertion logic unit-tested in tier 1; (d) seeded-regression proof = re-runnable self-test mode using the throwaway-edit pattern on REAL AXAML, tripping a SPECIFIC NAMED check — no injection flags; (e) budgets MEASURED (Windows + WSL2, cold vs incremental separate; budget = observed + headroom), "affected" = csproj-path selection; (f) K3 assertions manifest-driven (surface/state/region/property/tolerance), scoped to real current consumers only.
- 2026-07-19 (authoring): engine reviews assumed absent (T-2 open); Review Level 2 retained for auto-activation. Stub-first launch follows the T-6 playbook: stub validation batch → abort+clean → SEPARATE fresh real batch.
