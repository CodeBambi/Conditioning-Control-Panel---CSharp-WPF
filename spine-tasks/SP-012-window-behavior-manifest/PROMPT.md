# Task: SP-012 — build per-window behavior manifest

## Mission

Execute `client/docs/task-board.md` row **"Build per-window behavior manifest"** (P0, Phase 2 of `spine-tasks/CONTEXT.md`): inventory every retained WPF window's category, owner, modality, activation/focus, taskbar/Alt-Tab, topmost, resize, placement, decorations, and close/reuse lifecycle. Deliver `client/docs/window-behavior-manifest.md` — one named row per retained WPF window, every field carrying WPF source evidence (`File.cs:line`, code-derived), plus a **defined observation procedure per field** (how the later "exercise every row" gate will exercise it).

**Honesty framings (pre-authoring consult, binding):** (a) the acceptance's "exercise every row on Windows and supported Linux backends before approving shared chrome" is NOT dischargeable by this packet — those windows do not exist in the greenfield client yet; it stays a NAMED GATE on the board row (annotate-don't-rewrite, SP-007 Wayland-gate pattern), never rewrite the acceptance; (b) evidence class per WPF row is **code-derived, not runtime-verified** — WPF is read-only behavioral evidence, state the class explicitly; (c) the open owner question "which Linux distributions, display backends, and window managers define the window-behavior acceptance matrix" is NOT answered by this packet — platform-matrix columns stay "pending owner question" with WSLg/X11 recorded as the only observed environment (session facts, never backend claims — capability-contract honesty rule; Wayland stays §5.1 untouched); (d) the dashboard demonstrator is **observation-only** — zero product-code change; a field not observable on the one existing window records "observation procedure defined, not demonstrable on this window", never an invented value.

## Dependencies

- **Task:** SP-011 (Phase 2 serial chain; the spike's window-surface findings — NativeWebDialog, embedded WebView — are manifest-relevant cross-refs)

## Context to Read First

- `client/docs/task-board.md` — the manifest row + Decisions-needed (window-matrix owner question; popup-height/AvatarTube constants question) + gate history (SP-007 Wayland-gate pattern)
- `client/docs/capability-inventory.md` — window/windowing sections (which WPF windows exist and what they do)
- `client/docs/first-attempt-lessons.md` + `first-attempt-systemic-lessons.md` — window-management lesson items (ACCEPT/ADAPT/REJECT dispositions where recorded)
- `client/docs/runtime-capability-contract.md` — honesty rule (probe-derived claims only; session facts vs backend claims)
- WPF window sources — READ-ONLY archaeology: `ConditioningControlPanel/*.xaml(.cs)` (MainWindow, AvatarTubeWindow, `*Dialog`, `*Popup`, overlay windows), `ConditioningControlPanel/Services/` window-management code (OverlayService and friends)
- `spine-tasks/SP-007-first-visible-slice/record.md` — WSLg/X11 headed-evidence pattern (XGetImage captures, honest scoping); SP-011 record for the NativeWebDialog/WebView window surfaces
- Required skills: load `wpf-parity` before Step 1 (WPF behavior extraction discipline); `overlay-clickthrough` for classifying overlay windows against their dedicated row

## File Scope

- `client/docs/window-behavior-manifest.md` (deliverable — named row per retained WPF window)
- `client/docs/task-board.md` (manifest-row evidence edit only)
- `spine-tasks/SP-012-window-behavior-manifest/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/window-behavior-manifest.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/src/**`, `client/tests/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `client/docs/window-behavior-manifest.md`, `spine-tasks/SP-012-window-behavior-manifest/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — the engine parses `^##\s+Review Level:\s*(\d+)`; without this structured heading reviews silently skip. Record engine-review presence/absence per call in record.md (SP-011 proved reviews fire with the heading; absence now = regression, say so explicitly).

## Steps

### Step 1: Retained-window inventory (completeness-checkable) + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Enumerate ALL WPF windows mechanically (auditable method, e.g. every `*.xaml` with a `Window` root + every `System.Windows.Window` subclass under `ConditioningControlPanel/`, excluding `CCP.*/` first-attempt and `CCP.WindowsOnly/` adapters UNLESS a retained window's behavior lives there); record the enumeration method + raw list in record.md so completeness is checkable by a reviewer
- [ ] Classify each window: RETAINED (manifest row) vs cross-referenced to its dedicated board row (overlay windows → overlay-clickthrough scope; AvatarTube → the AvatarTube row; DTRH/WebView surfaces → SP-011 spike + admit row) vs excluded-with-reason (dead/duplicated); the classification table goes in the manifest
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable — probe failed, seats unproven) with the inventory + classification before writing manifest rows; verdict text in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Manifest authoring — named row per retained window

- [ ] Write `client/docs/window-behavior-manifest.md`: one row per retained window with every field from the board acceptance — category, owner, modality, activation/focus, taskbar/Alt-Tab, topmost, resize, placement, decorations, close/reuse lifecycle — each value carrying `File.cs:line` WPF evidence and its evidence class (code-derived; runtime-verified only where SP-007/SP-010 landed evidence already exists)
- [ ] **Observation procedure per field** (the auditability requirement): how the later exercise-gate will observe/exercise each field on Windows (UIA, headed harness) and Linux (wmctrl/xprop/XGetImage, WSLg session facts) — a manifest of values without procedures is rejected
- [ ] Platform-matrix columns: "pending owner question" for distro/backend/WM acceptance matrix; WSLg/X11 recorded as the only observed environment; Wayland §5.1 untouched
- [ ] Shared-chrome implications section: what the manifest CONSTRAINS for any future shared chrome decision (the row's purpose) — constraints only, no chrome design (not this packet's scope)

### Step 3: Dashboard observability demonstrator (observation-only)

- [ ] Execute the Step-2 observation procedures against the ONE existing greenfield window (dashboard) on Windows — headed, SP-007/SP-008 harness patterns (UIA enumeration, window properties, taskbar/Alt-Tab presence, placement coordinates, decorations, close behavior); zero product-code change
- [ ] Same on WSLg/X11 (wmctrl/xprop/XGetImage; record as session facts, never backend claims)
- [ ] Fields not observable on this window (e.g., modality — no owner/modal relationship exists) record "observation procedure defined, not demonstrable on this window" — never an invented value
- [ ] Record the demonstrator outcome per field in the manifest (procedure-proven vs procedure-defined-only), with evidence pointers in record.md

### Step 4: Board reconciliation + pre-completion consult

- [ ] Write `spine-tasks/SP-012-window-behavior-manifest/record.md`: enumeration method + raw inventory, classification decisions, consult verdicts (provenance — record the ACTUAL answering model per T-7), engine-review presence/absence per call, demonstrator evidence pointers, surprises
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the manifest + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` manifest row → `WIP` with evidence citing window-behavior-manifest.md; the named gates (exercise-every-row before shared chrome; owner matrix question) recorded as the row's remaining gates — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (product build + both test projects green — the manifest changes no code; this proves the lane is clean)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths (no WPF-tree edits, no product code, no scratch)

## Completion Criteria

- Completeness-checkable retained-window inventory (mechanical enumeration method + raw list recorded; reviewer can re-run it)
- `client/docs/window-behavior-manifest.md`: named row per retained window, all acceptance fields with `File.cs:line` evidence + explicit evidence class; observation procedure per field; platform-matrix columns pending owner question with WSLg/X11 as only observed environment; shared-chrome constraints section
- Dashboard demonstrator executed on Windows AND WSLg/X11 with zero product-code change; per-field procedure-proven vs defined-only recorded honestly
- Board row `WIP` (not `DONE`) with evidence + named remaining gates; both solo Fable consults persisted; engine-review presence recorded; no tracked changes outside File Scope

## Do NOT

- Modify `ConditioningControlPanel/**` (READ-ONLY behavioral evidence), `client/src/**`, `client/tests/**`, or any product/test code — the demonstrator is observation-only
- Answer the owner's window-matrix question (distros/backends/WMs), claim Wayland, or upgrade session facts to backend claims
- Design shared chrome or any windowing architecture (constraints section only); exercise windows that do not exist; rewrite the row's acceptance text (annotate-don't-rewrite)
- Set any board row to `DONE`; flip owner-held rows; use `consult` council mode (route broken — all gates solo Fable 5)
- Weaken SP-003…SP-011 invariants; invent manifest values not traceable to WPF source or observed evidence

## Git Commit Convention

- `feat(SP-012): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/window-behavior-manifest.md` (deliverable), `client/docs/task-board.md` (manifest-row evidence), `spine-tasks/SP-012-window-behavior-manifest/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only)

## Amendments

- 2026-07-20 (authoring): **pre-authoring consult RAN — solo Fable 5 (requested `anthropic/claude-fable-5`; council unavailable per failed probe, seats unproven, owner direction).** Verdicts applied: (a) manifest doc + named-gate deferral of "exercise every row" endorsed (windows don't exist; annotate-don't-rewrite); (b) dashboard demonstrator kept but observation-ONLY — `fileScopeMustNotChange` on `client/src/**`/`client/tests/**`; unobservable fields record "procedure defined, not demonstrable on this window"; (c) observation PROCEDURE per field required (the later gate is only dischargeable if the manifest says how each field is exercised); (d) platform-matrix columns stay "pending owner question", WSLg/X11 = only observed environment, Wayland §5.1 untouched; (e) evidence class per WPF row explicit (code-derived, not runtime-verified); (f) docs-only contract REJECTED — real scoped dotnet testCommand per session prompt; (g) Review Level 2 endorsed; (h) overlay/AvatarTube/DTRH windows included with cross-refs to their dedicated rows + completeness-checkable inventory required.
- 2026-07-20 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
