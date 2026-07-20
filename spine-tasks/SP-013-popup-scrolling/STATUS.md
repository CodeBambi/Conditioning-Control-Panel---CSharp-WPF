# STATUS: SP-013 — prove feature-popup scrolling

**Current Step:** DONE (all 5 steps complete)
**Last Updated:** 2026-07-20 (Step 5 complete: contract green Windows + WSL2, diff/scope clean; engine reviews absent ×5 per T-2)

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
**Status:** ✅ Complete

- [x] TALL / SHORT / NESTED-list content variants
- [x] Five input paths to final control, each with changing Extent/Viewport/Offset recorded + captures; touch probed first (digitizer present but injection unreliable → NAMED MANUAL GATE, not faked)
- [x] Mixed-scaling run (AVALONIA_GLOBAL_SCALE_FACTOR is X11-only → Step 4 WSLg run); K3 visual review PASS (4 states)
- [x] A-013 MCP advisory on redacted AXAML snippets (ValidateXaml PASS; AnalyzePerformance REJECTED self-contradictory)

### Step 4: WSLg/X11 gate + board reconciliation + pre-completion consult
**Status:** ✅ Complete

- [x] WSL2 in-packet gate: contract green (139+11 = Windows counts), render + capping + geometry session facts at scale 1 + 1.5 (no input automation) — wslg-popup.sh, all PASS, exit 0
- [x] record.md complete (design, citations, consult provenance, engine-review presence, evidence matrix, budgets)
- [x] Pre-completion solo Fable 5 consult — APPROVE WITH THREE REQUIRED ANNOTATIONS (taskbar property-only, capping branch coverage, focus-restore scope); all applied to record.md + board row
- [x] task-board.md popup row → WIP with named remaining gates (never DONE)
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ✅ Complete

- [x] Contract testCommand green (Windows: build 0W/0E 1.5s, 139/139 CcpClient.Tests, 11/11 CcpClient.HeadlessTests — identical to WSL2 counts)
- [x] `git diff --check` clean
- [x] `git status --short` = File Scope only (task-folder STATUS/record)
