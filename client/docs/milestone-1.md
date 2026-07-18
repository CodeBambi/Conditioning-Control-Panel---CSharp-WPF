# Milestone 1 — Foundation contracts, tooling admission, and first visible slice

This is the owner-reviewed milestone-scope authority for milestone 1 of the greenfield Windows/Linux client, required by `port-workflow.md` §"/task-auto milestone input". It defines a precise task-board subset; it is not the full doc set. The run is created only via the session prompt (`port-session-prompt.md` §Operator entry — the sole run-creation command):

```text
/task-auto @client/docs/port-session-prompt.md
```

The session prompt's reconciliation task discovers this document as the active milestone scope via the task-board pointer, and the decomposer MUST derive titles only from the rows in scope below — never from the full doc set (the 2026-07-18 run that received the whole documentation produced zero titles).

**Two-stage execution:** row 1 (bootstrap) runs first as a **standalone `/task`** — the owner reviews its architecture proposal before anything else proceeds, and the run gives a second pipeline data point beyond the pilot. Only rows 2–9 form the `/task-auto` run. (No verified pi-task mechanism exists for "pause the AUTO run after task N"; the documented pauses are the verify-FAIL picker and manual cancel, so the pause is structural: two runs.)

Authority order is unchanged (`port-workflow.md` §Sources of authority). `client/docs/task-board.md` remains the only live queue; this document carries **no live status** — reconcile board state at run start per `port-session-prompt.md`. If the board and this document disagree, the board wins and this document must be corrected before the run continues.

## Cannot start until

`/task-auto` for this milestone MUST NOT be issued until every item below is satisfied and evidenced on the task board:

1. The board row **"Pilot pinned pi-task workflow"** is `DONE` with its full acceptance evidence, including the explicit decision that `/task-auto` (and auto-commit, if used) is admitted. The 2026-07-18 MCP-audit pilot exercised agent/loop orchestration only and does **not** satisfy this row (see board gate history). A viable pre-scaffolding pilot that needs no row-1 output: a bounded throwaway spike — create an official Avalonia 12 template project in a temp directory, restore + build it on Windows, and document exact SDK/package versions. This exercises pi-task's real build VERIFY path (which the docs-only MCP pilot could not) with auto-commit off.
2. The board row **"Probe bpx-consult council and task integration"** is `DONE`. Every task in this milestone carries mandatory consult gates; an unprobed council seat is a failed gate, not silent consensus.
3. The **owner has signed off on this document** (section "Owner sign-off" below).
4. If any task in this run will call Avalonia MCP tools: the **owner Sentry-mitigation decision** from `avalonia-mcp-admission.md` §4 is recorded and the "Audit and admit bounded Avalonia MCP use" row is `DONE`. Until then, every task treats the MCP as unavailable and skips it (failure policy per `port-workflow.md`); MCP unavailability never blocks a task.
5. `git status --short` clean on a dedicated port branch/worktree; `/task-config` verified (remote off, verify on, orientation on; auto-commit only per the pilot row's decision).

## Rows in scope (in dependency order)

Nine task-board rows, quoted exactly, one task and one commit per row. Row 1 is a standalone `/task`; rows 2–9 are the `/task-auto` run and start only after the owner approves row 1's architecture proposal:

1. **"Bootstrap discovery and architecture proposal"** — **standalone `/task`, first**; produces `client/` scaffolding and the owner-reviewed Windows/Linux solution proposal. **Owner checkpoint: rows 2–9 do not start until the owner approves this proposal.** Every later row depends on it.
2. **"Define startup, shutdown, and integration contract"** — needs the scaffolding from row 1; its minimal bootstrap slice becomes the composition root every later row wires into.
3. **"Establish async lifecycle and fault policy"** — pairs with row 2 (owners, cancellation, teardown are startup/shutdown concerns); before persistence so write serialization has a lifecycle model.
4. **"Define persistence and migration contract"** — needs rows 2–3 (startup flush/teardown, serialized writes).
5. **"Define truthful runtime capability contract"** — needs row 2 (probes run inside startup phases).
6. **"Validate official migration checklist in first visible slice"** — needs rows 2–5; the first real composition-root-to-user-outcome path (A-014 integration rule). Surface is pre-declared under Owner sign-off below, so the run needs no mid-run owner pause for it.
7. **"Build tiered targeted verification harness"** — the minimal fast gate (affected build + affected tests + `git diff --check`) is part of row 1's declared slice; this row's commit covers only harness completion, runtime budgets, and the seeded-regression proof. The row *closes* only after row 6 exists, because its acceptance requires proving the targeted gate catches a seeded visual regression on a real surface.
8. **"Define asset and packaged-output manifest"** — after row 6 so "every required first-slice asset" is a concrete, testable set. This row may invoke ad-hoc `dotnet publish` for its packaged-output tests; formal Release/publish gates remain row 9's slice.
9. **"Establish Release and publish gates"** — last; its acceptance runs the row-6 slice in Debug, Release, and published Windows/Linux artifacts and needs rows 6–8.

### Why the first visible slice is in milestone 1, not milestone 2

Three in-scope rows textually depend on a first slice existing: row 7 (seeded visual regression on a real surface), row 8 ("first-slice asset" packaged-output tests), and row 9 ("run the first vertical slice in Debug, Release, and published artifacts"). A-014's YAGNI constraint also forbids establishing the contracts without a concrete consumer. Deferring the slice would leave milestone 1 unable to close three of its own rows and would ship six contracts with no integration proof.

### Tooling rows are prerequisites, not run content

The scope recommendation included **"Pilot pinned pi-task workflow"** and **"Probe bpx-consult council and task integration"**. They are refined **out of the `/task-auto` run and into the "Cannot start until" gates above**: the pilot row gates `/task-auto` admission itself, so it cannot logically be executed *by* this run, and the consult probe must pass before the first in-run consult gate fires. Both are executed manually (the pilot via one narrow `/task` with auto-commit off, per `port-workflow.md` §Pilot) and closed on the board before this milestone starts.

## Exclusions (and why)

- **Blocked rows stay blocked** — "Admit DTRH browser and origin design", "Implement web-only DTRH host", "Implement unified fullscreen video presentation", "Implement reliable quips and sound arbitration", "Implement AI companion and awareness integration", "Implement deep-learning webcam and gaze tracking". Their gating spikes/decisions are not in this milestone.
- **All spikes deferred** — "Spike official WebView with the copied DTRH payload", "Spike browser-to-native online-video handoff", "Spike one-decoder multi-monitor video geometry", "Spike cross-platform audio channel backend", "Spike cancellable AI providers and strict commands", "Spike Windows and Linux camera acquisition", "Spike local ONNX face/iris and gaze accuracy". No in-scope foundation row depends on any of them.
- **Feature/UI rows to milestone 2+** — "Prove feature-popup scrolling", "Replace card-title quick-toggle dispatch", "Build per-window behavior manifest", "Prove AvatarTube rendered animation", "Define provider-neutral AI operation contract", "Audit camera model provenance and privacy contract". They need the foundation this milestone builds.
- **"Audit and admit bounded Avalonia MCP use"** — owner-only admission decision; no agent task may flip it.

## Per-row requirements

Common to every row: cite the board row and governing architecture decision; first-attempt code under `ConditioningControlPanel/CCP.*` is **read-only lessons and failure evidence only** — never import its classes, interfaces, timers, DI topology, or status claims (use `first-attempt-lessons.md` / `first-attempt-systemic-lessons.md` ACCEPT/ADAPT/REJECT entries). WPF under `ConditioningControlPanel/` is read-only behavioral evidence. Current Avalonia v12 facts come only through `avalonia-research`; no guessed APIs.

| # | Row (short) | WPF/behavior evidence to inspect (narrow) | Skills | Consult gates |
|---|---|---|---|---|
| 1 | Bootstrap discovery | `capability-inventory.md`; `App.xaml.cs` startup wiring order (orientation only); both first-attempt lessons docs | port-plan, avalonia-research | pre-approach **council** (architecture, scaffolding shape, package baseline); pre-completion **council** |
| 2 | Startup/shutdown contract | WPF `App.xaml.cs` `OnStartup`/`OnExit`, single-instance mutex/handoff, `DispatcherUnhandledException` crash handling; A-014 | port-plan, wpf-parity, avalonia-research (lifetime APIs) | pre-approach solo; pre-completion **council** |
| 3 | Async lifecycle/fault policy | `ConditioningControlPanel/CLAUDE.md` threading issues; `Services/Deeper/IActionDispatcher.cs` comments; DispatcherTimer notes; systemic lessons (leaks, unobserved tasks) | port-plan, avalonia-research (dispatcher/threading) | pre-approach solo; pre-completion **council** |
| 4 | Persistence/migration contract | WPF `Services/Settings/SettingsService.cs` (temp-file+rename atomic write), `%APPDATA%` settings.json shape; systemic lessons (corruption, write-order, exit flush) | port-plan, wpf-parity | pre-approach solo; pre-completion **council** (data-loss risk) |
| 5 | Truthful capability contract | Systemic lessons (misleading capabilities, silent no-ops); WPF availability checks as behavior examples (WebView2 runtime check, webcam device handling in `Services/Webcam/`) | port-plan, avalonia-research (platform probes) | pre-approach solo; pre-completion **council** |
| 6 | First visible slice | A-012 + current official migration index/cheat sheet + deeper topic pages (mandatory citations per the row); WPF evidence for the chosen surface via wpf-parity; owner approves the surface before implementation | port-feature, wpf-parity, avalonia-research, dashboard-design (if a dashboard-family surface), app-visual-verification | pre-approach solo (surface + pattern choices); pre-completion solo with K3 evidence — escalate to **council** if it introduces window/input semantics decisions |
| 7 | Verification harness | Rejected first-attempt whole-app smoke/layer strategy (`first-attempt-systemic-lessons.md`) — reuse only proven narrow primitives | port-plan, app-visual-verification | pre-approach solo; pre-completion **council** (defines the port's task-close and milestone gate strategy — a gate decision per §Mode policy) |
| 8 | Asset/packaged-output manifest | WPF `.csproj` resource/copy items, `Resources/`, `assets/`, `Localization/Languages/*.json`, `installer.iss` file list (what ships and from where) | port-plan, wpf-parity | pre-approach solo; pre-completion **council** (packaging/trust boundary) |
| 9 | Release/publish gates | `build-installer.bat` + `installer.iss` version/staging behavior; WPF/Core `UpdateService` multi-place version bug (AGENTS.md §Version bumps) as the anti-pattern; single version authority per A-014 | port-plan, avalonia-research (publish/trimming facts) | pre-approach solo; pre-completion **council** (release work) |

## Windows and Linux acceptance per row

- **Rows 1–5, 7–9 (contracts/harness/gates):** all automated checks (build, unit, headless, failure-injection, packaged-output tests) must pass on **both Windows and Linux**. Row 5 additionally needs runtime-probe evidence on Windows, Linux X11, and Linux Wayland (an honest typed "unavailable + reason" is a pass; a silent no-op is a fail). Row 9 needs published-artifact startup/shutdown evidence on both OSes.
- **Row 6 (visible slice):** headed evidence on **Windows, Linux X11, and Linux Wayland** as the board row specifies, plus targeted K3 visual/interaction checks.
- **Documented-blocker rule:** if a Linux environment (or a Wayland session) is genuinely unavailable to the run, the task does **not** close — it stays `WIP`/`BLOCKED` on the board with the exact remaining manual gate named. Windows-only evidence never satisfies a cross-platform claim.

## Chokepoints — one task at a time, never parallel

- `client/docs/task-board.md` (every task updates it; serialized).
- `client/docs/architecture.md` and `client/docs/capability-inventory.md` (only when facts change).
- `client/` solution/project scaffolding files (solution, csproj, shared build props) — owned by row 1; later rows touch them only for their own declared slice.
- This file (`client/docs/milestone-1.md`) — corrections only, with owner awareness.
- No task modifies `ConditioningControlPanel/**` (WPF and first attempt are read-only).

## Verification

Tiered per `port-workflow.md` §Verification floor:

- **Fast per-task gate (every task):** build affected `client/` projects, run affected unit/headless tests, `git diff --check`, scoped `git status`. Never launch the whole app by default.
- **Task close gate:** exercise only the affected user path headed on Windows and claimed Linux backends; if pixels changed, capture exact states via `app-visual-verification` and have `kimi-coding/k3` inspect them with defect-specific assertions.
- **Milestone-only matrix (end of milestone, after row 9):** Debug/Release/publish of the row-6 slice on Windows and Linux (X11 + Wayland where applicable): startup/shutdown, native deps, assets/localization open from packaged output, data path, logs, version consistency, no configuration-only crash. Record runtime budgets from row 7.
- **A failed verify gate is never accepted to keep the AUTO loop moving.** No override, retry-until-green without diagnosis, or "accepted failure" may mark a row `DONE`. Repeated failure becomes a `BLOCKED` row or a focused diagnostic task.

## Tracker updates and commit discipline

Before any task is considered complete, and before `/task-auto` advances:

1. Update the matching `client/docs/task-board.md` row with concise concrete evidence or the exact blocker.
2. Update `architecture.md`/lessons docs only when research changed a recorded fact.
3. Record the required consult verdict, dissent, and any fit-ledger caveat in the task evidence (not full transcripts).
4. **One task per commit**, conventional message, containing only that row's declared slice — and a **clean tree between tasks**. `.pi-tasks/` state never substitutes for the board.

## Consultation evidence payload

Every consult in this milestone follows the `port-workflow.md` question contract. Minimum payload: the exact decision/defect; the board row + governing A-decision; relevant files/symbols and current official v12 sources; alternatives considered; latest diff and actual verification output (headed/K3 evidence for row 6); Windows and Linux consequences; security/privacy/performance constraints; and the requested judgment (proceed / stop / choose A-B / missing tests / smaller slice). Check the fit ledger; rerun a focused consult if decisive context was clipped. `consult` cannot mark a row `DONE`.

## Prohibitions (stop conditions)

All `port-workflow.md` §Stop conditions apply. In particular for this milestone: no silent acceptance of failed verification; no edits to WPF or first-attempt code; no package admission without current version/license/platform evidence and council; no guessed v12 APIs; no parallel edits to chokepoints; no Windows-only claims of cross-platform completion; no MCP use before the owner Sentry decision; no closing a product capability from registration, unit tests, or a non-throwing fallback (A-014).

## Review record

- **Workflow challenger** (big-tier agent, 2026-07-18): 4 scope issues + 3 order issues returned; all 6 suggested fixes applied (invocation reconciliation, row-6 surface pre-declared, row-8 ad-hoc publish, row-1/7 harness split, viable pre-scaffolding pilot named, invocation text corrected).
- **Council consult attempted and FAILED** (2026-07-18, two attempts including one live-probe after owner sign-off): synthesizer route `kimi-openai-completions` not registered — deterministic, unchanged across attempts. Additional probe: solo+persona call for the `security` seat answered via `claude-sonnet-4-5`, not the configured `kimi-coding/k3` seat — kimi routes are not engaging at all; solo default (`claude-fable-5`) works. Gut-check route responds without a provider exception, but the answering model could not self-identify, so seat fidelity for `zai/glm-5.2` is unproven (2026-07-18). `.pi/providers/kimi-coding/config.json` exists on disk; the full probe (restart trusted Pi, `/consult status`, every seat, pi-task child call) per the board row is still required. Recorded as partial evidence for the OPEN "Probe bpx-consult council and task integration" row.
- **Solo fallback consult** (2026-07-18, checkpoint-1 substitution because the probe row is still OPEN): verdict "fix-first" — two fixes applied (row 1 made a standalone `/task` because no verified pause-after-task mechanism exists; this review-record block added), plus the Linux-environment sign-off flag below.

## Owner sign-off

- [x] Owner has reviewed scope, order, exclusions, and gates above. **Approved 2026-07-18** (chat sign-off).
- [ ] "Pilot pinned pi-task workflow" row `DONE` on the board.
- [ ] "Probe bpx-consult council and task integration" row `DONE` on the board. (Partial probe evidence 2026-07-18 in Review record above: council route hard-fails, kimi seats not engaging.)
- [x] Sentry mitigation decided **or** MCP declared out of scope for this run. **Decided 2026-07-18 via delegated consult (solo): MCP declared OUT OF SCOPE for milestone 1** — every task treats the MCP as unavailable and skips it (failure policy); no per-task MCP deliberation. The "Audit and admit bounded Avalonia MCP use" row stays `WIP` — admission is deferred, not decided; revisitable at milestone 2 if a real need appears. Rationale: redaction-only is a policy not a control; fork-patching invalidates the audit's hash verification; firewall rules are fragile ops cost to admit a toolset with zero semantic validation; compilation + K3 + headed gates already cover the only defect class ValidateXaml detects.
- [x] Row-6 surface approved 2026-07-18: **main dashboard window with one real toggleable feature card** — exercising selectors/pseudo-classes, compiled bindings including a named/ancestor case, one direct `ICommand`, `IsVisible` layout intent, an `avares://` asset, keyboard/pointer input, scaling, and teardown (the board row's own acceptance list).
- [x] Linux execution environment identified 2026-07-18: **WSL2 Ubuntu 26.04 LTS** present on the workstation (no dotnet SDK installed yet; WSLg Wayland/X11 headed validation pending — setup is part of row 1's scaffolding slice).
