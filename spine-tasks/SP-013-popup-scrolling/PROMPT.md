# Task: SP-013 — prove feature-popup scrolling

## Mission

Execute `client/docs/task-board.md` row **"Prove feature-popup scrolling"** (P0, Phase 2 of `spine-tasks/CONTEXT.md`) — build the popup SP-007 carved out: the demonstrator card's left-click settings popup as a REAL Avalonia window implementing the WPF FeaturePopupWindow behavior contract (manifest row W-04): modeless, owned by the dashboard, centered **on the owner's monitor's working area** (never primary-by-default), absent from taskbar, non-resizable, draggable by its title bar, closed by close button or Escape through ONE command path, one-at-a-time with close-existing-before-new, focus restoration to the dashboard on close. Prove the scrolling acceptance with synthetic content: every feature popup remains inside the owner monitor's working area and reaches its final control by mouse wheel, trackpad/touch, keyboard focus, scrollbar controls, and thumb drag; short content remains compact; mixed scaling and nested scrolling pass.

**Honesty framings (pre-authoring consult, binding):** (a) the popup is an explicitly-labeled DEMONSTRATOR (SP-007 card pattern: really-functioning, superseded-by-first-real, owner may async-veto) — it does NOT discharge manifest row W-04's exercise gate and no board/manifest row may say so (annotate-don't-rewrite); (b) NO shared chrome — `SystemDecorations.None` + custom title bar stays popup-LOCAL (A-005 trap; the manifest's exercise gate precedes any shared chrome framework); (c) trackpad/touch: PROBE, don't promise — if the workstation has no precision touchpad/touch digitizer, that input path is a named MANUAL gate, never a faked pass; (d) WSLg/X11 evidence = render + owner-monitor capping + geometry as session facts (no input automation — SP-008 named limit); Wayland stays §5.1 untouched; Linux input-gesture acceptance stays a NAMED GATE on the board row; (e) the board's open owner question "what maximum popup height fraction should become fixed acceptance constants" is NOT answered — the demonstrator uses a WPF-parity constant recorded as pending-owner; (f) observable scrolling evidence means recording changing `Extent`/`Viewport`/`Offset` (or equivalent), not screenshots alone — the row's own verification language.

## Dependencies

- **Task:** SP-012 (window-behavior manifest — W-04 row + owner-monitor constraint are this packet's behavior contract)

## Context to Read First

- `client/docs/task-board.md` — the popup-scrolling row + Decisions-needed (popup-height-fraction owner question) + SP-007 gate history (demonstrator framing, owner veto flag)
- `client/docs/window-behavior-manifest.md` — W-04 FeaturePopupWindow row (File.cs:line evidence) + §6 constraint 4 (CenterOwner = owner's monitor)
- `client/docs/capability-inventory.md` — "Feature-popup behavior" section (~line 105-118): one-at-a-time, modeless/owned/centered/taskbar/resize/drag/close contract; nested-scroll chaining; keyboard focus brings clipped controls into view; Extent/Viewport/Offset verification language
- WPF sources (READ-ONLY): `ConditioningControlPanel/Features/FeaturePopupWindow.xaml(.cs)`, `MainWindow.Presets.cs:846-873` (one-at-a-time, Show, focus restoration)
- `client/docs/verification-harness.md` — tier model; `spine-tasks/SP-008-verification-harness/record.md` — named limits (no WSLg input automation); SP-007 record — demonstrator + K3 + headed-evidence patterns
- Required skills: load `wpf-parity`, `avalonia-research`, `dashboard-design` before Step 1

## File Scope

- `client/src/CcpClient.Desktop/**` (popup window, demonstrator-card left-click wiring, synthetic content)
- `client/tests/CcpClient.Tests/**` (unit tests: working-area capping math, one-at-a-time manager, command path)
- `client/tests/CcpClient.HeadlessTests/**` (draw-level interaction tests where honestly possible)
- `client/docs/task-board.md` (popup-row evidence edit only)
- `spine-tasks/SP-013-popup-scrolling/**` (STATUS.md, record.md, evidence artifacts, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/**` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `spine-tasks/SP-013-popup-scrolling/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: WPF evidence + current v12 research + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY): FeaturePopupWindow XAML/code + `MainWindow.Presets.cs:846-873` — extract the exact behavior contract (owner, modality, placement, chrome, drag, close paths, focus restoration, one-at-a-time, min/default size) with File.cs:line citations; the SP-012 manifest W-04 row is the index, the WPF source is the authority
- [ ] `avalonia-research` (CURRENT v12 sources only): `Window` ownership/`Show(Window)` modeless API, `SystemDecorations.None` + `BeginMoveDrag` title-bar drag, `Screens` API for owner-monitor working area, `ScrollViewer` Extent/Viewport/Offset + nested-scroll chaining behavior, Escape-key handling, `AVALONIA_GLOBAL_SCALE_FACTOR` for mixed-scale evidence — record citations in record.md
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable) with the design: popup window shape, owner-monitor working-area capping math, synthetic content plan (tall/short/nested-list), evidence matrix plan; verdict text in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Popup implementation (popup-local chrome, one command path)

- [ ] Popup window: owned modeless `Show(dashboard)`, `ShowInTaskbar=false`, `CanResize=false`, `SystemDecorations.None` with popup-LOCAL title bar (drag via `BeginMoveDrag`, close button), Escape + close button through ONE command path; close-existing-before-new one-at-a-time manager; focus restoration to dashboard on close (W-04 contract)
- [ ] Owner-monitor working-area capping: max height = WPF-parity fraction (recorded pending-owner) of the WORKING AREA of the monitor containing the dashboard, computed at open + on DPI/working-area change; centered on owner within that monitor
- [ ] Unit tests: capping math (primary vs secondary monitor geometry, mixed scale), one-at-a-time manager transitions, command-path close (Escape ≡ close button ≡ one operation), focus-restoration call — SP-004 owned-operation discipline for any async

### Step 3: Synthetic content + Windows-headed evidence matrix

- [ ] Synthetic content variants: TALL (forces capping; final control below the fold), SHORT (compact, no scrollbar), NESTED scrollable list inside the popup (scrolls itself, then chains remaining movement to the popup)
- [ ] Windows-headed evidence (headed harness patterns): all five input paths to the FINAL control — mouse wheel, trackpad/touch (PROBE the digitizer first; absent = named manual gate, never faked), keyboard focus (Tab brings clipped controls into view), scrollbar controls, thumb drag — each recording **changing Extent/Viewport/Offset** + capture evidence; mixed-scaling run via `AVALONIA_GLOBAL_SCALE_FACTOR`; K3 visual review of popup lit/unlit/scroll states
- [ ] **A-013 Avalonia MCP advisory** (owner-admitted, advisory-only): send small REDACTED AXAML snippets (popup layout/chrome) after the v12 research; record accepted/rejected findings concisely in record.md; unavailability never blocks

### Step 4: WSLg/X11 gate + board reconciliation + pre-completion consult

- [ ] WSL2 in-packet gate (native-dir copy, never /mnt/e): contract testCommand green; popup renders on WSLg/X11 (XGetImage captures); owner-monitor working-area capping + geometry observed as SESSION FACTS (no input automation — SP-008 limit); `_NET_CLIENT_LIST` absence handled per port-lessons 2026-07-20
- [ ] Write `spine-tasks/SP-013-popup-scrolling/record.md`: design decisions, research citations, consult verdicts (provenance — record the ACTUAL answering model), engine-review presence per call, evidence matrix + budgets, surprises
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` popup row → `WIP` with evidence: named remaining gates — Linux input-gesture acceptance (X11 + Wayland), trackpad/touch if probed-absent, owner height-fraction constant, W-04 exercise gate NOT discharged by this demonstrator — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (build 0W/0E + both test projects green incl. new tests)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Real demonstrator popup implementing the W-04 contract (modeless/owned/owner-monitor-centered/taskbar-absent/non-resizable/title-bar-drag/one-command-path close/one-at-a-time/focus-restore), popup-LOCAL chrome only
- Windows-headed evidence for all five input paths reaching the final control with changing Extent/Viewport/Offset recorded; tall/short/nested-chaining variants pass; mixed-scaling evidence; K3 visual PASS
- WSLg/X11 render + capping + geometry session facts; Wayland §5.1 untouched; no input automation claimed on Linux
- Unit tests for capping math / one-at-a-time / command path green on Windows AND WSL2; contract testCommand green
- A-013 advisory recorded; board row `WIP` (not `DONE`) with named remaining gates (Linux input gestures, touch-if-probed-absent, owner constant, W-04 non-discharge); both solo Fable consults persisted

## Do NOT

- Build shared/reusable popup chrome or any window framework (A-005 trap — manifest exercise gate precedes shared chrome); claim W-04's exercise gate discharged; set any board row `DONE`
- Answer the owner's height-fraction question (demonstrator constant, pending-owner); fake trackpad/touch evidence (probe first; absent = named manual gate); claim Wayland; automate input on WSLg
- Modify `ConditioningControlPanel/**` (READ-ONLY evidence); widen scope beyond the demonstrator popup + its card wiring; weaken SP-003…SP-012 invariants
- Use `consult` council mode (route broken — solo Fable 5 only); substitute screenshots for Extent/Viewport/Offset evidence

## Git Commit Convention

- `feat(SP-013): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/task-board.md` (popup-row evidence), `spine-tasks/SP-013-popup-scrolling/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only)

## Amendments

- 2026-07-20 (authoring): **pre-authoring consult RAN — solo Fable 5 (requested `anthropic/claude-fable-5`; council unavailable per failed probe).** Verdicts applied: (a) demonstrator frame endorsed — really-functioning/labeled/superseded-by-first-real, NO W-04 exercise-gate discharge claim; (b) no shared chrome — popup-LOCAL title bar only (A-005 trap); (c) synthetic content must cover tall/short/nested-chaining by construction; (d) Escape + close button through ONE command path; (e) trackpad/touch PROBE-don't-promise (absent digitizer = named manual gate); (f) WSLg = render/capping/geometry session facts only, Linux input = named gate, annotate-don't-rewrite; (g) demonstrator height constant = WPF-parity, pending-owner; (h) Extent/Viewport/Offset recording REQUIRED (row's own verification language — omission = land REVISE).
- 2026-07-20 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
