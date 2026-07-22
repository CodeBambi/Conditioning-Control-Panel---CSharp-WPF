--dtrh-block-route /umedia/` → the probe media fetch answered **403 (HARNESS blocked-route injection, logged typed)** → page reports the typed load error (`probe-img LOAD ERROR`) and SURVIVES → graceful close EXIT=0. (First combined runC+D attempt blocked `/media/` — a prefix nothing fetches; the injection never fired. Split into honest separate cells.)
- **Run D — missing-media (W19 class) (`runD-drive.log` + `runD.log`, EXIT=0):** fx-drive probe-missing-media → the fetch **404s (typed, logged)** → page reports the typed load error and SURVIVES → EXIT=0.
- All captures pixel-verified (dark% + distinct-colors recorded per capture; never a black surface outside the intentionally-unreachable post-kill window).

### WSL2 in-packet gate (`~/ccp-sp027` native ext4 via tar sync, never /mnt/e for the tree)

- **Contract testCommand green on the synced tree:** sln build **0W/0E**; **391/391 unit + 29/29 headless** (≥ the 366/29 b4 floor).
- **WX run 1 (`evidence/wx-run1.log`, EXIT=0):** engine live on the WebKitGTK dialog; **typed capability state logged, never faked:** "native ProcessFailed signal UNAVAILABLE (unsupported-platform) on the WebKitGTK dialog path — heartbeat watchdog is the only net (named limit…)"; **graceful exit fast path on Linux:** auto-close → end-run → "exit-done received — closing (graceful fast path)" → EXIT=0. GStreamer/WebVTT warnings = pre-existing WSLg image facts (SP-026 class).
- **WX run 2 (`evidence/wx-run2.log`, recovery proven on Linux):** engine live → `pkill WebKitWebProcess` (renderer-kill equivalent) → heartbeat stops → **watchdog silence detection: "heartbeat-silent (24s > 20s, hub)" → demands RELAUNCH generation 1 → relaunching ONCE** → relaunch-path stale-profile recovery typed **Unavailable on Linux** ("WebView2 (Windows) class; no msedgewebview2 children and no 0x800700AA surface (named limit)" — logged, never faked) → new window → **second ENGINE LIVE**. APP-EXIT=143 = the harness's own SIGTERM after recovery was proven (recorded honestly; graceful EXIT=0 on Linux is wx run 1's fact).
- **Linux named limits (honest, consolidated below):** no native process-failure signal on the WebKitGTK dialog (typed Unavailable — the heartbeat watchdog is the only net; W17-class detection lands after the black-but-beating window + threshold, observed 24s hub — NEVER claimed fast); stale-profile-lock recovery is a WebView2/Windows class (typed Unavailable on Linux); no timing claims; no input automation; Wayland untouched (§5.1).

### Consolidated b1–b5 named limits (mirrored into the board row)

Wayland untouched (§5.1) · Linux timing/latency never claimed · no Linux input automation (SP-008 class; Linux exit evidence = timed/auto close, not ESC) · WPE unpackaged on Ubuntu 26.04 (owner question stands, SP-011) · **published-artifact payload location still UNDECIDED (b1 land condition — the WPF-tree read-only source does not exist in a published artifact)** · Linux native ProcessFailed = typed Unavailable, watchdog-only net with black-but-beating-window + threshold latency (never fast) · Linux stale-profile recovery = typed Unavailable (WebView2-only class) · Linux user-video page-side playback = WSLg GStreamer session fact · vmem crop-rectangle class (luma-0 buffers) = unified-video row · VN portrait tint + in-run freeze pulse page-internal, b4-gated · greenfield SFX content gap (8 payload files; unresolved cues = logged silent no-op; WPF sound library = future row) · Loom rack pane render not driven (pane + 3D navigation gate; display proof = served URL rendered in-engine) · skillMult 1.0 (no skill tree) · difficulty reveal-clamp skipped · thoughtTexts empty · app-level hooks (AddXP/achievements/bark/reveal-sync/session-telemetry) = no greenfield subsystems · bark = Deferred("voice-arbitration (quips row)") · mod content = modContent:null (no mod system) · ESC-hold exit during a fullscreen cheshire VN scene is swallowed BY PAYLOAD DESIGN (capture-phase handler, WPF-shared — WPF parity, not a defect) · Chrome_WidgetWin_0 unregister error line at process teardown = WebView2 cosmetic noise (present in every clean EXIT=0 run).

### Surprise ledger

1. **The fresh-slot VN scene swallows ESC-hold exit** (capture-phase `stopImmediatePropagation`, `cheshireVn.js:484-491`) — three failed B1 runs + one orphaned app before isolation (diagB1v4). WPF-shared payload = WPF parity. Harness fix = `vn-clear` click-through (the real user path); durable port-lessons entry.
2. **keybd_event needs the scancode** (0x01 for ESC); scancode-0 keys never reached the page. The SP-011 precedent masked it (SFW on an already-focused spike window).
3. **SetForegroundWindow lies under foreground-lock** — returns success while the owner's window keeps foreground; the real canvas click is the only reliable claim; verify before key drive.
4. **The after-kill black surface is intentionally unreachable when the native signal lands** — ProcessFailed + teardown outrun an ~800ms capture; the W17 black-but-beating window is measurable only on the heartbeat-only path (Linux WX run 2: 24s to silence-detection).
5. **`--dtrh-block-route /media/` matched nothing** (media routes live under `/umedia/`) — an injection flag that never fires is NOT evidence; the cell was split and re-run honestly.
6. WSL sync: robocopy's `\\wsl.localhost` target is mangled by Git bash path conversion (files landed in a Windows-side `E:\wsl.localhost\` literal); tar through `/mnt/e` read + native-ext4 write is the reliable shape.
7. Salvage-commit provenance honored: nothing from `038fe603` was trusted unverified; every cell re-run.

### Budgets

ESC forensics (4 diags + 3 failed B1 attempts) ≈ 50 min; headed matrix re-runs ≈ 15 min app time; WSL sync ≈ 2 min (tar); WSL contract ≈ 30s; WX runs ≈ 3 min; record/board ≈ 30 min.

### Plan review

Step 4: engine-skipped (T-2 heading; presence/absence recorded).

### Pre-completion consult (Step 4 gate)

- (verdict recorded below after the call)
