# STATUS: SP-011 — spike official WebView with the copied DTRH payload

**Current Step:** Complete — all 6 steps done
**Last Updated:** 2026-07-19 (worker session 1, task complete)

## Steps

### Step 1: Package verification, payload archaeology, pre-approach consult
**Status:** ✅ Complete (plan review: engine-skipped in worker per SP-195, recorded)

- [x] FIRST: verify `Avalonia.Controls.WebView` ID/version/license/native deps on the live feed (wrong ID/conflict = finding, not failure)
- [x] STATUS.md updated before starting work
- [x] Payload archaeology: git log spike.html/spike.js/m2test.js; source path + file count/hash recorded
- [x] Restore/build spike skeleton vs Avalonia 12.1.0 baseline (dependency-range outcome recorded)
- [x] Pre-approach solo Fable 5 consult (verdict in record.md BEFORE checkbox)

### Step 2: Quarantined spike host + loopback
**Status:** ✅ Complete (plan review: engine-skipped in worker per SP-195, recorded)

- [x] client/spikes/CcpSpike.WebView/ minimal host (NOT in CcpClient.sln, build-only)
- [x] Loopback: GET-only into read-only tree, overlay-first, scratch-dir writes, HTTP Range, origin/port recorded
- [x] Windows boot of index.html with capture evidence

### Step 3: Windows evidence matrix (named observation per item)
**Status:** ✅ Complete (plan review: engine-skipped in worker per SP-195, recorded)

- [x] spike.html granular items (if archaeology supports): WebGL, workers, WebAudio/autoplay, video seek, CORS-clean upload, fullscreen, focus
- [x] Bridge ordering observation (unchanged bridge.js)
- [x] Loopback routes enumerated + path-traversal refusal
- [x] Exit/teardown observation
- [x] Failure injection ×3 (kill process, blocked route, missing media)
- [x] Startup time + frame behavior measured

### Step 4: WSL2 gate — Linux build + WSLg/X11 evidence
**Status:** ✅ Complete (plan review: engine-skipped in worker per SP-195, recorded)

- [x] Native-dir copy (~/ccp-sp011); WebKitGTK/WPE via wsl -u root (packages + versions recorded)
- [x] Linux restore/build + full contract testCommand green on WSL2
- [x] WSLg/X11 boot attempt with XGetImage evidence (diagnosed non-boot acceptable, never faked)
- [x] Budgets both platforms (cold precondition verified)

### Step 5: Evidence, board reconciliation, pre-completion consult
**Status:** ✅ Complete (plan review: engine-skipped in worker per SP-195, recorded)

- [x] client/docs/webview-dtrh-spike.md — named observation per acceptance item
- [x] record.md complete (consult verdicts; engine-review presence/absence — T-2; worker-child council attempt outcome)
- [x] Pre-completion solo Fable 5 consult (verdict in record.md)
- [x] task-board.md spike row → WIP with evidence (admit row stays BLOCKED)
- [x] STATUS.md accurate before .DONE

### Step 6: Testing & Verification
**Status:** ✅ Complete

- [x] Contract testCommand green (product suite + spike build) — Windows: sln 0W/0E, 118/118, 3/3 headless, spike 0W/0E; WSL2: same (Step 4)
- [x] `git diff --check` clean
- [x] `git status --short` = File Scope only (no payload/scratch content; forbidden paths verified clean vs batch base 1d50384a)
