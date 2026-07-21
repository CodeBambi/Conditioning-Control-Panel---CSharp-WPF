# STATUS: SP-015 — prove AvatarTube rendered animation

**Current Step:** Step 6 — complete; ready for .DONE
**Last Updated:** 2026-07-21 (Steps 4-6 complete: 66 Windows gates + 16/16 WSLg captures green; pre-completion solo Fable APPROVE with 4 corrections applied; contract green 0W/0E, 176/176 + 22/22)

### Step 1: Decoder claim verification + AvatarTube archaeology + pre-approach consult
**Status:** ✅ Complete

- [x] STATUS.md updated before starting work
- [x] FIRST checkbox: pinned 12.1.0 decode/animate surface verified (never docs site); finding recorded
- [x] WPF + first-attempt archaeology with File.cs:line; leak REJECT lessons cited
- [x] Owner-transition demonstrable-vs-contract split decided from archaeology
- [x] Pre-approach solo Fable 5 consult (verdict in record.md BEFORE checkbox)

### Step 2: Synthetic asset pipeline + animation engine
**Status:** ✅ Complete

- [x] `SyntheticAvatarPacks.cs` — two packs, frame-indexed pixel strip, non-uniform delays, hashed
- [x] Packs routed through SP-009 manifest; `--verify-assets` green Debug + Release
- [x] SP-004-owned engine/timer; typed undecodable-asset capability state
- [x] Unit tests: cadence math, successor-frame resume, pack-switch, registry-count stability

### Step 3: Demonstrator surface + behaviors
**Status:** ✅ Complete

- [x] Demonstrator surface: fade, looping, crossfades, click reaction, float, pause/resume, pack switch, attach/detach, owner transitions, cleanup
- [x] ONE engine, no parallel timers; constants recorded pending-owner

### Step 4: Windows-headed evidence matrix
**Status:** ✅ Complete

- [x] CcpVerify named checks per behavior (deltas, no blanks, no duplicate-runs, cadence vs delays) — 66 gates green (run.log)
- [x] Resume-fast-forward check (successor frame + unchanged cadence, speed 1.023)
- [x] Leak long-run: registry stable (outstanding=2 heartbeat+engine, subs=1 after 25 cycles)
- [x] Click/crossfade named sequences; K3 visual PASS (mid-fade + fallback artifacts); no AXAML changed by this step (A-013 ValidateXaml n/a)

### Step 5: WSLg/X11 gate + board reconciliation + pre-completion consult
**Status:** ✅ Complete

- [x] WSL2 gate: contract green (176/176 + 22/22 native-dir); 16/16 XGetImage captures, all 5 evaluator verdicts PASS; cadence/click/owner NOT claimed on Linux
- [x] avatartube-demonstrator.md + record.md complete (decoder finding, lessons, consult provenance, budgets, octal/UIA findings)
- [x] Pre-completion solo Fable 5 consult: APPROVE with 4 corrections — all applied (count 176, verify-assets re-run, binary-delta note, process closure)
- [x] task-board.md row → WIP with 6 named limits (never DONE)
- [x] STATUS.md accurate before .DONE

### Step 6: Testing & Verification
**Status:** ✅ Complete

- [x] Contract testCommand green (build 0W/0E incl. -t:Rebuild; 176/176 unit + 22/22 headless)
- [x] `git diff --check` clean
- [x] `git status --short` = only untracked `.pi/loops/*.json` (pi-runtime artifacts, recorded in record.md)
