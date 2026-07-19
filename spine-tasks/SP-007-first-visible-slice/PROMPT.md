# Task: SP-007 — validate official migration checklist in first visible slice

## Mission

Execute `client/docs/task-board.md` row 6 (**"Validate official migration checklist in first visible slice"**, P0, Phase 1 of `spine-tasks/CONTEXT.md`) against the landed SP-003–SP-006 foundation. Deliver the **main dashboard window with ONE real toggleable demonstrator feature card** (owner-ratified surface, board gate history 2026-07-18) that exercises every A-012 migration-checklist item with a **named observation per item**, plus `client/docs/migration-checklist-validation.md` mapping each item → where it is exercised → the observation that proves it → the official v12 citation. The slice reaches the screen for real on Windows and WSL2 Linux (X11 session facts recorded); **Linux Wayland is a named, documented gate** (see Amendments — WSLg is XWayland-only and Wayland backend opt-in is open owner question §5.1; never fake it).

The card is a **demonstrator feature** with stable ID `demo.status-ticker`, explicitly labeled as a demonstrator (A-004 stable identities; do NOT name it after a real WPF feature with no backend — that is the first-attempt capability lie SP-006 exists to kill). Record that the first real feature card supersedes it in a later dashboard row, and that "one real toggleable feature card" was interpreted as *really-toggling demonstrator card* (owner may async-veto).

## Dependencies

- **Task:** SP-006 (capability states surface on the window; session-probe facts are the Linux evidence discipline), SP-005 (toggle flag persists through the atomic store), SP-004 (the card's "service" is a real owned operation; UI updates cross the phase-4 `IUiDispatch` boundary), SP-003 (dashboard window constructs via the composition root; teardown path is the single guarded entry point)

## Context to Read First

- `client/docs/task-board.md` row 6 + gate history (owner-ratified surface; Wayland/WSLg facts)
- `client/docs/architecture.md` — A-012 (official migration baseline: selectors/pseudo-classes, compiled bindings, direct `ICommand`, pointer events/routing, dispatcher, asset URIs; rejected methodology), A-013 (Avalonia MCP advisory role + redaction rules), A-004 (stable feature identities, one command path), A-005 (window semantics precede chrome — popup carve-out), A-014 (YAGNI)
- `client/docs/architecture-proposal.md` — §2 package baseline (no new packages), §5.1 (Wayland opt-in = open owner question), §6 (headless test harness admission = row 7's decision, NOT this task's)
- `client/docs/capability-inventory.md` §Dashboard and feature popups (lines ~93–123) — the WPF quick-toggle contract: plain right-click on an unlocked toggleable card body immediately reverses enabled state; changing only a persisted flag is an inert-UI failure; ring reflects state
- `client/docs/first-attempt-lessons.md` and `first-attempt-systemic-lessons.md` — dashboard/XAML-relevant lessons only (outcomes, not mechanics)
- `client/docs/startup-shutdown-contract.md`, `async-lifecycle-fault-contract.md`, `persistence-migration-contract.md`, `runtime-capability-contract.md` — the four landed contracts this slice consumes
- Required skills: load `port-feature`, `avalonia-research`, `dashboard-design`, `wpf-parity` before Step 1; `app-visual-verification` before Step 4

## File Scope

- `client/src/CcpClient.Desktop/**` (dashboard view/viewmodel, demonstrator feature, wiring, one `avares://` asset)
- `client/tests/CcpClient.Tests/**` (slice tests)
- `client/docs/migration-checklist-validation.md` (deliverable)
- `client/docs/task-board.md` (row-6 evidence edit; Decisions-needed entry check only — see Step 5)
- `spine-tasks/SP-007-first-visible-slice/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/migration-checklist-validation.md`, `client/src/CcpClient.Desktop/Views/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `.spine/**` |
| artifactsMustExist | `client/docs/migration-checklist-validation.md`, `spine-tasks/SP-007-first-visible-slice/record.md` |

**Review Level 2 (plan + code)** — first UI surface; every later dashboard/feature row inherits its patterns. Call `spine_review_step` after each step. Engine reviews are empirically dead (zero reviews in SP-001…SP-006 — diagnostic row T-2 open); if `spine_review_step` returns skipped, record that fact in record.md and rely on the mandatory Fable consults instead. Do not stall waiting for a reviewer.

## Steps

### Step 1: Pre-approach consult, v12 research, validation skeleton

- [ ] Run a **pre-approach solo consult** (Fable 5 via `consult` tool, mode solo) with the planned slice design (demonstrator card + real operation toggle, checklist item → observation mapping, popup carve-out, Wayland gate); record the verdict text in record.md **before** marking the checkbox (write-then-check). Keep questions few/pointed — Fable truncates long multi-question prompts
- [ ] Update STATUS.md Step 1 checkboxes as you work (before, not after)
- [ ] **v12 research (avalonia-research):** for EACH checklist item, pull the current official source — migration index + WPF cheat sheet first, then the linked deeper v12 page (selectors/pseudo-classes, compiled bindings, commands, layout/`IsVisible`, assets, input, scaling); record URL + freshness per item in the validation doc. No stale v11 guidance
- [ ] **WPF parity digest (wpf-parity):** extract the dashboard quick-toggle behavior contract from `ConditioningControlPanel/Features/FeatureCard.xaml.cs` + `MainWindow/MainWindow.Presets.cs` — outcomes only (what the user observes), one short note in record.md; do NOT transplant mechanics
- [ ] Write `client/docs/migration-checklist-validation.md` skeleton: one row per checklist item (selector/pseudo-class state; compiled binding incl. named/ancestor case; one direct `ICommand`; `IsVisible` layout intent; `avares://` asset; keyboard/pointer input; scaling; teardown) with columns: item → where exercised → named observation → official citation → status (pending)

### Step 2: Dashboard window and demonstrator card

- [ ] Evolve `Views/MainWindow` into the minimal dashboard surface: one feature card (dashboard-design grammar: lit/unlit visual meaning; five-theme/dark-neon grammar only insofar as resources already exist — do NOT build the theme system, that is a later row), plus the retained SP-006 capability-state surface (may move/shrink, never deleted — it is the integration proof of prior slices)
- [ ] **Demonstrator feature `demo.status-ticker`:** toggle = start/cancel of a REAL SP-004 owned operation (owner, generation, typed terminal outcome) that renders a live tick/count on the card through the phase-4 `IUiDispatch` boundary. Toggle ON → operation started; OFF → cancelled with typed outcome. The card's lit/unlit ring reflects OPERATION state, never the persisted flag alone
- [ ] **Persistence:** the enabled flag round-trips through the SP-005 store (atomic write; restart restores it; tests assert FILE content, not view-model state)
- [ ] **Input contract:** plain right-click on the card body quick-toggles (WPF parity outcome); a keyboard path also toggles (focus + key). **Left-click settings popup is CARVED OUT** (A-005 per-window contract, deferred to the dashboard/feature rows) — do not wire a no-op left-click that pretends; record the exclusion
- [ ] **Checklist mechanics:** pseudo-class state (e.g. `:pointerover` + a toggled class driving the ring) via selectors, no WPF-style triggers; compiled bindings ON with `x:DataType`, including one named/ancestor case (`ElementName` or `$parent[...]`) that provably resolves; exactly one direct `ICommand` (no `RoutedCommand`); one element whose `IsVisible` collapse is load-bearing; one `avares://` asset rendered on the card; no pack URIs, no literal WPF XAML transplant

### Step 3: Tests, wiring, WSL2 gate

- [ ] Unit tests: toggle starts/cancels the operation with typed outcomes (SP-004 vocabulary); ring state derives from operation state; flag persists (file-content assert) and restores on restart through the real composition root; capability surface still renders (prior integration proofs intact)
- [ ] Dashboard constructs via the composition root in a named phase (SP-003 model; no constructor-started work; operation not started until the restored flag says so — restore-then-start is explicit and ordered)
- [ ] **WSL2 Linux gate (SP-005 pattern):** copy `client/` to a native WSL dir (never `/mnt/e`), run the contract testCommand green, AND record the ACTUAL session-probe facts for the run (X11/XWayland session facts — no Wayland backend claim)

### Step 4: Headed evidence, MCP advisory, visual verification

- [ ] **Headed Windows smoke (SP-003/004/006 UIA pattern):** launch Debug exe; observe: card renders with `avares://` asset visible; right-click quick-toggle starts the operation (tick ADVANCES, UIA-observed); ring flips lit; `:pointerover` visual delta observed; `IsVisible` element provably leaves layout (measured bounds delta); keyboard toggle path works; restart restores the flag AND restarts the operation; **scaling: measured card bounds recorded at 100% and 150%**; **teardown: close the window mid-operation → operation cancelled with typed terminal outcome, settings flushed, exit 0**
- [ ] **Headed WSLg observation:** launch under WSL2/WSLg; record what renders/works with the session-probe facts alongside; anything unobservable there is named, not claimed. This is X11-session evidence; Wayland stays a named gate
- [ ] **A-013 Avalonia MCP advisory (bounded):** after Step 1's official v12 research, send SMALL REDACTED AXAML snippets (card + selectors + bindings only — no repo paths, secrets, or proprietary code) to the `avalonia` MCP for a second opinion; treat 11.3.1-pinned heuristics skeptically against the 12.1.0 baseline; record accepted/rejected findings concisely. MCP unavailability never blocks — record that fact instead
- [ ] **K3 visual verification (app-visual-verification):** targeted screenshots of the card lit AND unlit at task close; compare against the dashboard-design contract; fix bounded visual defects and recapture
- [ ] Fill every validation-doc row with its ACTUAL named observation (or an explicit "not observable — named manual gate" entry); an item claimed from markup presence alone is a contract violation

### Step 5: Evidence, board reconciliation, pre-completion consult

- [ ] Write `spine-tasks/SP-007-first-visible-slice/record.md`: design decisions (demonstrator interpretation, popup carve-out, Wayland gate), consult verdicts, v12 citations, WPF digest, test output (Windows AND WSL2), headed observations (incl. scaling bounds + teardown outcome), WSLg session facts, MCP advisory findings accepted/rejected, K3 visual verdict, surprises. **Record engine-review presence/absence** (row T-2)
- [ ] Run a **pre-completion solo consult** (Fable 5, solo) on the diff and validation doc; record the verdict text in record.md
- [ ] Update `client/docs/task-board.md` row **"Validate official migration checklist in first visible slice"** to `WIP` with evidence text citing record.md — never `DONE`. The evidence text MUST name the **Linux-Wayland gate** explicitly (open owner question §5.1; WSLg = X11 session facts only) so the row visibly stays WIP on it. Check the board's "Decisions needed" list: if the §5.1 Wayland opt-in question is not already surfaced there, add it — landing this row makes it load-bearing for the first time
- [ ] Update STATUS.md — all checkboxes reflect reality before .DONE

### Step 6: Testing & Verification

- [ ] Contract testCommand passes: `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo`
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- `migration-checklist-validation.md` maps EVERY checklist item to a named observation + official v12 citation (A-012 baseline); no item is claimed from markup presence
- Dashboard window with one `demo.status-ticker` card renders for real; right-click quick-toggle starts/stops a real SP-004 operation; ring reflects operation state; flag persists through SP-005 (file-content proof) and restores on restart
- Headed Windows smoke observed all Step-4 items incl. scaling bounds (100%/150%) and mid-operation teardown (exit 0); WSL2 testCommand green + WSLg observation recorded with session facts
- Wayland named as a documented gate (board evidence text + record.md); no Wayland backend opt-in, no silent acceptance narrowing
- A-013 MCP advisory findings recorded (or unavailability recorded); K3 lit/unlit screenshots reviewed
- Both solo Fable consults run with verdict text persisted; STATUS.md accurate; board row `WIP` with evidence (not `DONE`)
- No tracked changes outside File Scope; `.spine/` untouched; no new packages admitted

## Do NOT

- Modify `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`, `.pi/`
- Touch `ConditioningControlPanel/**` (WPF + first attempt are read-only evidence)
- Admit ANY new package — in particular **NOT `Avalonia.Headless.XUnit`** (headless-harness admission is row 7's explicit decision, proposal §6); interaction evidence comes from headed UIA on Windows + recorded WSLg observation, with anything unautomatable named as a manual gate
- Opt into a Wayland backend to manufacture Wayland evidence (owner question §5.1), or narrow the board row's acceptance text — annotate, never rewrite
- Name the demonstrator card after a real WPF feature (Flash, Subliminals, …) or imply a backend exists
- Wire a no-op left-click settings popup (carved out to the dashboard/feature rows — a no-op is a claim)
- Build the theme system, locked-card semantics, multiple cards, or the settings popup (later rows; YAGNI)
- Use WPF triggers, `RoutedCommand`, pack URIs, or literal XAML transplants
- Add native interop (PInvoke/DllImport) or split a per-OS head
- Disturb SP-003/SP-004/SP-005/SP-006 invariants (single guarded teardown, registry ownership, phases, atomic persistence, probe honesty); the SP-006 capability surface stays wired
- Use `consult` council mode (seats unproven — solo Fable 5 only)
- Set any board row to `DONE`
- Skip or fake STATUS.md updates, consult checkboxes, or review-evidence notes

## Git Commit Convention

- `feat(SP-007): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/migration-checklist-validation.md` (deliverable), `client/docs/task-board.md` (row-6 evidence + Decisions-needed check), `spine-tasks/SP-007-first-visible-slice/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (append only if a durable surprise emerges)

## Amendments

- 2026-07-19 (authoring): **pre-authoring Fable consult RAN (two solo consults; first reply truncated mid-Q2 — known Fable behavior, missing portion recovered with a pointed follow-up; truncation labeled).** Verdicts applied: (a) toggle drives a REAL SP-004 owned operation rendering a live tick via `IUiDispatch` — ring reflects operation state, flag persists via SP-005 with file-content assert; the "periodic capability re-probe" alternative REJECTED (invents consumer-less behavior, mutates SP-006 semantics); (b) demonstrator must be explicitly labeled (`demo.status-ticker`) with supersedure note + owner async-veto flag — never named after a real WPF feature; (c) left-click settings popup carved out (A-005 work, absent from row-6 acceptance) — no pretending no-op; (d) **Wayland conflict RESOLVED: row acceptance literally demands Linux-Wayland, but WSLg is XWayland-only (SP-006 session-probe facts) and §5.1 Wayland opt-in is an open owner question — packet delivers Windows + WSL2/X11 acceptance with Linux-Wayland as a named documented gate; worker must neither opt into Wayland nor silently narrow the acceptance text; §5.1 surfaces in "Decisions needed" if absent;** (e) every checklist item requires a NAMED OBSERVATION (pseudo-class triggered + delta observed; compiled binding provably resolving; `avares://` stream-tested AND headed-observed; `ICommand` observed executing; `IsVisible` proven via measured bounds; scaling = measured bounds at 100%/150%); (f) `Avalonia.Headless.XUnit` forbidden — row 7's admission decision, proposal §6.
- 2026-07-19 (authoring): A-013 MCP advisory step made mandatory-bounded per handoff requirement (small redacted AXAML snippets after official v12 research; accepted/rejected findings recorded; unavailability never blocks).
- 2026-07-19 (authoring): coverage-gate checkbox omitted (row 7 scope); engine reviews assumed absent (T-2 open); Review Level stays 2 for auto-activation if T-2 lands. Consult-cap note: orchestrator cap is now 8/session post-restart; if a worker hits its own cap, document the skip per the SP-006 pattern — never fake a verdict.
