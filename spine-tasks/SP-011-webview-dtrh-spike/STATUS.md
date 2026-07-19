# STATUS: SP-011 — spike official WebView with the copied DTRH payload

**Current Step:** Step 3 — Windows evidence matrix
**Last Updated:** 2026-07-19 (worker session 1)

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
**Status:** 🔄 In Progress

- [ ] spike.html granular items (if archaeology supports): WebGL, workers, WebAudio/autoplay, video seek, CORS-clean upload, fullscreen, focus
- [ ] Bridge ordering observation (unchanged bridge.js)
- [ ] Loopback routes enumerated + path-traversal refusal
- [ ] Exit/teardown observation
- [ ] Failure injection ×3 (kill process, blocked route, missing media)
- [ ] Startup time + frame behavior measured

### Step 4: WSL2 gate — Linux build + WSLg/X11 evidence
**Status:** ⬜ Not Started

- [ ] Native-dir copy (~/ccp-sp011); WebKitGTK/WPE via wsl -u root (packages + versions recorded)
- [ ] Linux restore/build + full contract testCommand green on WSL2
- [ ] WSLg/X11 boot attempt with XGetImage evidence (diagnosed non-boot acceptable, never faked)
- [ ] Budgets both platforms (cold precondition verified)

### Step 5: Evidence, board reconciliation, pre-completion consult
**Status:** ⬜ Not Started

- [ ] client/docs/webview-dtrh-spike.md — named observation per acceptance item
- [ ] record.md complete (consult verdicts; engine-review presence/absence — T-2; worker-child council attempt outcome)
- [ ] Pre-completion solo Fable 5 consult (verdict in record.md)
- [ ] task-board.md spike row → WIP with evidence (admit row stays BLOCKED)
- [ ] STATUS.md accurate before .DONE

### Step 6: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand green (product suite + spike build)
- [ ] `git diff --check` clean
- [ ] `git status --short` = File Scope only (no payload/scratch content)
