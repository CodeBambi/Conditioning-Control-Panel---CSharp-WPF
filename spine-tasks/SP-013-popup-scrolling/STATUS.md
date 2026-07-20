# STATUS: SP-013 — prove feature-popup scrolling

**Current Step:** Step 3 — synthetic content + Windows-headed evidence matrix
**Last Updated:** 2026-07-20 (Step 2 complete: 139 unit + 11 headless green)

### Step 1: WPF evidence + current v12 research + pre-approach consult
**Status:** ✅ Complete

- [x] STATUS.md updated before starting work
- [x] WPF archaeology (FeaturePopupWindow + MainWindow.Presets.cs:846-873) with File.cs:line citations
- [x] avalonia-research: v12 window/ownership/chrome/drag/Screens/ScrollViewer chaining/Escape/scale-factor citations in record.md
- [x] Pre-approach solo Fable 5 consult (verdict in record.md BEFORE checkbox)

### Step 2: Popup implementation (popup-local chrome, one command path)
**Status:** ✅ Complete

- [x] Popup window: owned modeless, taskbar-absent, non-resizable, WindowDecorations.None + popup-local title bar (BeginMoveDrag + close), Escape ≡ close one command path, one-at-a-time manager, focus restoration
- [x] Owner-monitor working-area capping (WPF-parity fraction, pending-owner; open-time + owner-monitor/scaling change)
- [x] Unit tests: capping math, one-at-a-time transitions, command-path close (headless — real routing), focus restoration

### Step 3: Synthetic content + Windows-headed evidence matrix
**Status:** ⬜ Not Started

- [ ] TALL / SHORT / NESTED-list content variants
- [ ] Five input paths to final control, each with changing Extent/Viewport/Offset recorded + captures; touch probed first (absent = named manual gate)
- [ ] Mixed-scaling run (AVALONIA_GLOBAL_SCALE_FACTOR); K3 visual review
- [ ] A-013 MCP advisory on redacted AXAML snippets (accepted/rejected recorded)

### Step 4: WSLg/X11 gate + board reconciliation + pre-completion consult
**Status:** ⬜ Not Started

- [ ] WSL2 in-packet gate: contract green, render + capping + geometry session facts (no input automation)
- [ ] record.md complete (design, citations, consult provenance, engine-review presence, evidence matrix, budgets)
- [ ] Pre-completion solo Fable 5 consult (verdict in record.md)
- [ ] task-board.md popup row → WIP with named remaining gates (never DONE)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand green (build 0W/0E + both test projects incl. new tests)
- [ ] `git diff --check` clean
- [ ] `git status --short` = File Scope only
