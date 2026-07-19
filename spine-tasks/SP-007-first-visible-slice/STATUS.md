# Status: SP-007 — validate official migration checklist in first visible slice

**Overall:** 🔄 In progress

**Current Step:** Step 6

## Steps

### Step 1: Pre-approach consult, v12 research, validation skeleton
**Status:** ✅ Complete

- [x] Pre-approach solo consult (Fable 5) run; verdict text persisted in record.md BEFORE checkbox
- [x] STATUS.md updated before starting work
- [x] v12 research per checklist item (migration index + cheat sheet + deeper pages; URL + freshness recorded)
- [x] WPF quick-toggle parity digest in record.md (outcomes only)
- [x] `client/docs/migration-checklist-validation.md` skeleton written (item → where exercised → named observation → citation → status)

### Step 2: Dashboard window and demonstrator card
**Status:** ✅ Complete

- [x] MainWindow evolved to dashboard: one card + retained SP-006 capability surface
- [x] `demo.status-ticker`: toggle = real SP-004 owned operation; ring reflects operation state
- [x] Flag persists via SP-005 (file-content assert; restart restores)
- [x] Right-click quick-toggle + keyboard path; left-click popup carved out (recorded)
- [x] Checklist mechanics: pseudo-class selectors, compiled bindings (x:DataType + named/ancestor), one direct ICommand, load-bearing IsVisible, avares:// asset; no WPF transplants

### Step 3: Tests, wiring, WSL2 gate
**Status:** ✅ Complete

- [x] Unit tests: operation outcomes, ring-from-operation, file-content persistence, restart-restore via composition root, prior integration proofs intact
- [x] Composition-root construction in named phase; restore-then-start ordered; no constructor-started work
- [x] WSL2 native-dir run: testCommand green + session-probe facts recorded (X11 facts, no Wayland claim)

### Step 4: Headed evidence, MCP advisory, visual verification
**Status:** ✅ Complete

- [x] Headed Windows UIA smoke: asset rendered, quick-toggle tick advance, ring flip, :pointerover delta, IsVisible bounds delta, keyboard path, restart-restore, scaling bounds 100%/150%, mid-operation teardown exit 0
- [x] Headed WSLg observation recorded with session-probe facts; unobservable items named
- [x] A-013 MCP advisory: redacted AXAML review done (or unavailability recorded); findings accepted/rejected
- [x] K3 visual verification: lit + unlit screenshots reviewed; bounded defects fixed
- [x] Validation doc: every row filled with ACTUAL named observation (no markup-presence claims)

### Step 5: Evidence, board reconciliation, pre-completion consult
**Status:** ✅ Complete

- [x] record.md written (incl. engine-review presence/absence note)
- [x] Pre-completion solo consult (Fable 5) run; verdict text persisted
- [x] Board row 6 → `WIP` with evidence naming the Linux-Wayland gate (not `DONE`); Decisions-needed §5.1 entry present
- [x] STATUS.md accurate

### Step 6: Testing & Verification
**Status:** 🔄 In progress

- [ ] Contract testCommand passes
- [ ] `git diff --check` clean
- [ ] Only File Scope paths changed
