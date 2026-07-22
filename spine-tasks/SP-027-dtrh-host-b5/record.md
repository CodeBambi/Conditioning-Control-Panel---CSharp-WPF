# SP-027 — DTRH host slice b5: watchdog recovery, graceful exit, failure injection (FINAL slice)

## Step 1 — archaeology + design + pre-approach consult

### WPF archaeology (READ-ONLY, `File.cs:line` — `ConditioningControlPanel/Services/Chaos/DtrhHostService.cs`)

- **Heartbeat watch:** `:819-841` — `DispatcherTimer` 5s cadence (`:823`); guarded on `_host == null || !IsReady || _exiting` (`:830`); silence limit **10s mid-run / 20s hub** (`:833-836`); trip → `Recover("heartbeat-silent")`. Comment at `:826-829` cites the trap case: a locked page main thread also kills the JS Esc-hold exit.
- **ProcessFailed:** `:850-852` — `OnProcessFailed(kind)` → `Recover($"process-failed:{kind}")`; wired at `:123`.
- **Relaunch-once:** `:858-876` — `Recover(reason)` marshals to the dispatcher; `retry = !_relaunchedOnce`; logs "relaunching once" / "giving up"; `DisposeAll()`; retry sets `_relaunchedOnce = true` + full `Launch(wasTest)`. Never a restart loop.
- **Graceful close:** `:149-160` — `CloseActive()`: if ready && !exiting → `_exiting = true`, post `end-run` {reason:"host"}, `ArmExitWatchdog()`; else `DisposeAll()`. Idempotent.
- **Exit watchdog constant:** `:877-882` — 1200ms, tick → `DisposeAll()` (force close). Comment at `:147-148`: "ask the page to wind down, watchdog-force after 1200ms".
- **Dispatch:** heartbeat stamp `:276-278`; `exit` → `_exiting = true; ArmExitWatchdog()` `:312-314`; `exit-done` → `DisposeAll()` `:316`; `pong` → heartbeat stamp `:318-320`.
- **Payload semantics (`Resources/web/dtrh/boot.js`):** ESC-hold exit sends `exit` then `shutdown()` → `exit-done` back-to-back (`:197-198`, `:119-123`); host's `end-run` → `shutdown()` (`:179`); `ping` → `pong` (`:180`); rAF heartbeat ~2s (`:186`); page's own 45s boot deadline → `boot-error` (`:104-116`).
- **Stale-profile lock (`0x800700AA` class):** WPF has NO explicit handler — SP-023 record surprise #7: back-to-back runs after a killed process leave the WebView2 profile locked briefly → init panic (loud, exit 2); recovery = kill stale `msedgewebview2` children; proper recovery routed to b5. The greenfield profile dir is `<settings-dir>/dtrh/wv2-profile` (`DtrhHostWindow.axaml.cs:396`).

### Spike/manifest verification

- **W17 (`client/docs/webview-dtrh-spike.md` :66):** all 7 msedgewebview2 processes killed at t≈12–13s → surface BLACK at t≈24s, **`AdapterDestroyed` NEVER fired**; heartbeats continued ~28s post-kill (last beat #16 at t=32.7s), silence from t≈40.7s; no Avalonia-level fault event at any point. Detection implication: heartbeat watchdog catches it only after the black-but-beating window; native `ProcessFailed` via `TryGetPlatformHandle` = the documented immediate route.
- **W21 (:70):** black ONLY in the W17 zombie state (no false-positive on black).
- **W18/W19:** blocked-route (403) + missing-media (404) injections are diagnosable typed failures, page survives.
- **W16:** ESC-hold exit → exit 0; `exit-done` not awaited (b5 owns the bounded wait).

### API verification (not guessed — binary + official IDL)

- `NativeWebView.TryGetPlatformHandle()` (public) returns the adapter; adapter implements the PUBLIC `Avalonia.Platform.IWindowsWebView2PlatformHandle` exposing `CoreWebView2`/`CoreWebView2Controller` raw IntPtrs (reflection dump, `Avalonia.Controls.WebView` 12.0.1 net10.0).
- `ICoreWebView2` GUID `76eceacb-0462-4d94-ac83-423a6793775e`, `add_ProcessFailed` **vtable slot 25**, `remove_ProcessFailed` 26 (package's own interop metadata order, cross-checked against `Microsoft.Web.WebView2` 1.0.2535.41 `WebView2.idl`).
- `ICoreWebView2ProcessFailedEventHandler` GUID `79e0aea4-990b-42d9-aa1d-0fcc2e5bc7f1` (Invoke at slot 3); `ICoreWebView2ProcessFailedEventArgs` GUID `8155a9a4-1474-4a86-8cae-151b0fa6b8ca` (`get_ProcessFailedKind` slot 3) — both from the official IDL. `COREWEBVIEW2_PROCESS_FAILED_KIND` enum 0..9 (BrowserProcessExited … UnknownProcessExited) from the IDL.

### Design (post-consult)

- **Detection stack:** (1) native `ProcessFailed` subscription via minimal COM interop on the platform-handle pointer — immediate signal, Windows-embedded only; capability state probed at `AdapterCreated` (typed Available / Unavailable(unsupported-platform) on the Linux dialog / Unavailable(attach-failed) — never faked); (2) heartbeat watchdog (5s cadence, 10s run / 20s hub) — the net on both platforms, catches JS-main-thread wedges ProcessFailed never sees.
- **`DtrhWatchdog` (contract-named):** pure testable state machine — heartbeat stamps, silence computation, recovery-episode **generation latch** (consult CORRECTION 1), relaunch-once → typed outcomes (Relaunching / Relaunched / Exhausted→honest close).
- **Relaunch:** full window recreation through `DtrhLaunchCoordinator` (WPF DisposeAll+Launch parity — consult (c)); close-for-recovery must NOT raise `FlowEnded` (consult CORRECTION 2).
- **Graceful exit:** host-initiated close → post `end-run` + arm 1200ms force-close; page `exit` → arm bounded wait; `exit-done` → close now; one `_exiting` latch + one-shot close, idempotent against reentrant `exit-done`/Closing (consult CORRECTION 3); `pong` stamps heartbeat. Exit path rides the existing Closing teardown (b3 mid-freeze force-resume, b4 flush — no regression).
- **Stale-profile-lock (`0x800700AA`):** classify init/navigation exceptions by HRESULT (ERROR_BUSY 0x800700AA = resource in use), kill `msedgewebview2` processes whose command line carries our `--user-data-dir`, retry once; typed outcome + logged, never silent. Runs on the RELAUNCH path too (consult (b)2 — relaunch-into-locked-profile is the deterministic case).
- **Harness (HARNESS-ONLY flags, b3/b4 norm):** `--dtrh-kill-renderers` (kill profile-matched msedgewebview2 children at engine-live +N s; second kill after relaunch live → exhaustion evidence), `--dtrh-block-route <prefix>` (loopback 403 injection), `--dtrh-fx-drive` carries exit-matrix messages (`exit`, `exit-done` suppression for the timeout path).

### Pre-approach consult (Step 1 gate)

- **Mode:** solo (Fable 5 requested; council route broken — T-7). Two calls: first verdict TRUNCATED mid-correction; second call completed it (truncation recorded per SP-022 provenance discipline). **Actual answering model not surfaced by the tool (recorded honestly).**
- **Verdict: design sound — proceed, with three corrections.**
  - **(a) COM subscription workable, traps:** call `add_ProcessFailed` on the UI thread (apartment-bound, same class as SP-023 surprise #4); subscribe at `AdapterCreated`, re-subscribe after relaunch, null-check the pointer; `remove_ProcessFailed` on a dead browser returns failure HRESULT — tolerate, never throw from teardown; store the `EventRegistrationToken`; keep a strong ref to the managed handler; `Marshal.GetObjectForIUnknown` AddRefs internally; release our RCW at teardown so we don't pin the browser alive.
  - **CORRECTION 1 (double-Recover, latent WPF bug):** one kill burst fires MULTIPLE ProcessFailed events; WPF's queued second `Recover` would tear down the freshly relaunched window. Fix: recovery-episode **generation latch** — after Recover triggers, drop every further failure signal (ProcessFailed AND heartbeat-silent) until the new instance reaches `ready`. Tests: burst of N → exactly one relaunch, new instance survives; failure after relaunched instance live → typed Exhausted → honest close.
  - **(b) detection-stack gaps (record, none fatal):** watchdog-only latency = ~28s black-but-beating window + 10/20s threshold (Linux named limit — never imply the watchdog catches W17 fast); stale-profile recovery MUST run on the relaunch path (relaunch-into-locked-profile is deterministic); relaunch-that-never-boots is covered by the page's own 45s boot deadline → `boot-error` → honest close (boot.js:104-116) — no second host boot timer.
  - **(c) full window recreation — correct** (zombie CoreWebView2 makes re-navigate undefined). **CORRECTION 2:** `FlowEnded` on close must distinguish close-for-recovery from close-for-real (harness treats FlowEnded as flow-over). **CORRECTION 3:** exit path idempotency — `exit`+`exit-done` arrive back-to-back on the fast path; late `exit-done` after force-close and Close-from-Closing must be no-ops (SP-023 surprise #2 class).

---

## Step 2 — watchdog core (detection stack + relaunch-once)

- **`Features/Dtrh/DtrhWatchdog.cs` (contract-named):** pure testable state machine — heartbeat stamps, silence computation (5s cadence / 10s run / 20s hub, WPF `:819-841`), recovery-episode **generation latch** (consult CORRECTION 1: after Recover triggers, every further failure signal — ProcessFailed AND heartbeat-silent — is dropped until the new instance reaches ready; one kill burst fires N ProcessFailed events and WPF's queued second Recover would tear down the fresh window), relaunch-once → typed outcomes (Relaunching / Relaunched / Exhausted→honest close; never a restart loop).
- **`Features/Dtrh/DtrhProcessFailed.cs`:** native ProcessFailed subscription via minimal COM interop on the platform-handle pointer (ICoreWebView2 `add_ProcessFailed` vtable slot 25 — package interop metadata order cross-checked against the official `WebView2.idl`; event-handler/args GUIDs from the IDL). Typed capability outcome probed at AdapterCreated: `Attached` / `Unavailable(unsupported-platform|invalid-handle|attach-failed)` — never faked. UI-thread subscription, re-subscribe after relaunch, teardown tolerates dead-browser failure HRESULTs (consult traps (a)).
- **`DtrhLaunchCoordinator`:** owns the watchdog across window recreation (survives relaunch); close-for-recovery does NOT raise `FlowEnded` (consult CORRECTION 2).
- **Unit tests (`DtrhWatchdogTests`, +12):** silence timing vs the W17 case, heartbeat resume resets, burst-of-N → exactly one relaunch, once-then-typed-exhaustion, capability-Unavailable never fakes a signal, no fire during a live session (b4 run-lifecycle regression guard). **378/378 + 29/29** at commit `0069a3b7`.
- **Plan review:** engine-skipped (T-2 heading; presence/absence recorded).

## Step 3 — graceful exit + stale-profile recovery

- **Graceful exit (`DtrhHostWindow` + `DtrhWatchdog` exit flow):** host-initiated close → post `end-run` + arm the 1200ms force-close (WPF `:880` constant cited); page `exit` → arm the bounded exit-done wait; `exit-done` → close now (fast path); wait elapsed → watchdog-FORCED close (page wedged mid-shutdown). One `_exiting` latch + one-shot close, idempotent against back-to-back `exit`+`exit-done`, late `exit-done` after force-close, and Close-from-Closing (consult CORRECTION 3; SP-023 surprise #2 class). `pong` stamps the heartbeat. Exit rides the landed Closing teardown (b3 mid-freeze force-resume + b4 flush — no regression).
- **`Features/Dtrh/DtrhProfileLock.cs`:** `0x800700AA`-class stale-profile-lock detection (HRESULT classification, ERROR_BUSY = resource in use), recovery = kill `msedgewebview2` processes whose command line carries our `--user-data-dir`, retry once; typed outcome + logged, never silent, never a crash loop. Runs on the RELAUNCH path too (consult (b)2 — relaunch-into-locked-profile is the deterministic case).
- **Unit tests (`DtrhExitFlowTests`, +12):** exit-done fast path, timeout force path, mid-freeze exit (b3 invariant), lock classification + recovery outcome. **390/390 + 29/29** at commit `d2568417`.
- **Plan review:** engine-skipped (T-2 heading; presence/absence recorded).

---

## Step 4 — failure-injection evidence + consolidated limits + board + pre-completion consult

### Salvage provenance (honest record)

The first Step-4 worker session **wedged at 0-CPU during run B1** (2026-07-22 ~08:24); the orchestrator salvaged WIP in commit `038fe603` (marked UNVERIFIED provenance). This session re-ran and verified every cell from scratch; all transcripts below are this session's. Root-cause of the failed B1 cell is fully diagnosed (ESC-drive forensics below) — it was a HARNESS defect class (no product wedge; the SP-024 E-series lesson holds: harness wedge ≠ product wedge).

### ESC-drive forensics (the durable finding — three stacked causes)

1. **Fresh-slot VN capture-phase swallow (primary):** a fresh slot opens the cheshire `hub_welcome` fullscreen VN scene (15 beats; `cheshireGuide.js:355`, stage NEW→ARRIVED). The scene's **capture-phase** keydown handler `preventDefault + stopImmediatePropagation`s EVERY non-modifier key — including ESC (`cheshireVn.js:484-491`, installed at `:492`). boot.js's bubble-phase exit handler never sees the hold. **WPF parity, not a port bug** — the payload is the shared read-only WPF tree, so WPF's DTRH behaves identically (ESC-hold is dead during any fullscreen VN scene). Fix in the harness: new `vn-clear` drive action clicks the scene through with real canvas clicks (first click completes the typewriter, second advances — `cheshireVn.js:547`; 40 clicks covers 15 beats + arm delays), exactly the real user path. diagB1v4 isolated this: identical drive, prep (fresh slot) = FAIL, warm slot = PASS.
2. **keybd_event scancode:** scancode-0 synthetic ESC never reached the page; `keybd_event(0x1B, 0x01, …)` works (diagB1 A/B isolation). The SP-011 sendkeys.ps1 precedent used scancode 0 but paired it with `SetForegroundWindow` on an already-focused spike window — the combination masked it.
3. **Foreground:** the app opens UNACTIVATED behind windows (SP-007 lesson); `SetForegroundWindow` is foreground-locked while the owner uses the machine (diagB1v2: returned success, foreground unchanged). The **real canvas click** is the only reliable foreground claim; the harness now verifies `GetForegroundWindow == target` immediately before keydown and fails LOUD (exit 4) otherwise.
- Also encoded: orphan-guard in every run script (bounded `WaitForExit` + loud FAIL + `Kill($true)`) — the 08:24 orphan ran heartbeats to #2116 before the orchestrator killed it; never again silently.
- Stale `runA-after-kill.png` deleted: it predated the salvaged run (the after-kill capture raced the recovery — ProcessFailed detection + teardown completes in well under the ~800ms capture delay; the W17 black surface is intentionally unreachable by design when the native signal lands. The black-but-beating window exists only on the heartbeat-only path — measured on Linux, see WX run 2).

### Windows headed injection matrix (DISPLAY3, rect-persistence binding discharged — literal `GetWindowRect: (-2576,1091)-(-1280,1930) [1296x839]` lines in every committed transcript)

- **Run A — renderer-kill (W17) end-to-end (`runA-drive.log` + `runA.log`, EXIT=0):** engine live → HARNESS kill of profile-matched msedgewebview2 children → **native ProcessFailed → BrowserProcessExited, immediate detection** (AdapterDestroyed never fires — W17) → watchdog demands RELAUNCH generation 1 → **relaunching ONCE** (stale-profile recovery on the relaunch path: no stale children, profile free) → full window recreation → engine live again → **SECOND kill → typed EXHAUSTED** → honest close ("relaunch already spent", WPF 'giving up' `:864` parity) → flow ended → teardown → **EXIT=0**. Never a restart loop.
- **Run B1 — graceful-exit fast path + ESC regression (`runB1-drive.log` + `runB1.log`, EXIT=0):** vn-clear (hub_welcome clicked through on the fresh slot) → real ESC-hold (1500ms, vk+scan, click-focus + foreground verified) → page `exit` received → "page winding down; bounded exit-done wait armed (1200ms)" → **`exit-done` received → closing (graceful fast path)** — the page's real back-to-back exit+exit-done beat the 1200ms force. Attempt 1 was still mid-scene (recorded honestly in the transcript); attempt 2 exited.
- **Run B2 — host-initiated wind-down (`runB2-drive.log` + `runB2.log`, EXIT=0):** auto-close → "graceful exit — end-run posted to the page; bounded exit-done wait armed (1200ms, WPF :880)" → the REAL page shut down and answered **`exit-done` → fast-path close** → EXIT=0.
- **Run B3 — timeout force path (`runB3-drive.log` + `runB3.log`, EXIT=0):** fx-drive injected page `exit` through the real dispatch path with NO real wind-down behind it → "exit received; bounded wait armed" → **"exit-done wait elapsed (1200ms) — watchdog-FORCED close (page wedged mid-shutdown; WPF :881)"** → teardown → EXIT=0.
- **Run C — blocked-route (W18 class) (`runC-drive.log` + `runC.log`, EXIT=0):** `-
--dtrh-block-route /umedia/` → the probe media fetch answered **403 (HARNESS blocked-route injection, logged typed)** → page reports the typed load error (`probe-img LOAD ERROR`) and SURVIVES → graceful close EXIT=0. (First combined runC+D attempt blocked `/media/` — a prefix nothing fetches; the injection never fired. Split into honest separate cells.)
- **Run D — missing-media (W19 class) (`runD-drive.log` + `runD.log`, EXIT=0):** fx-drive probe-missing-media → the fetch **404s (typed, logged)** → page reports the typed load error and SURVIVES → EXIT=0.
- All captures pixel-verified (dark% + distinct-colors recorded per capture; never a black surface outside the intentionally-unreachable post-kill window).

- **Stale-profile-lock proof level (pre-completion consult CORRECTION 1 — recorded precisely):** HRESULT classification + kill-and-retry recovery are **unit-proven** (Step 3, `DtrhExitFlowTests`); the relaunch-path sweep **executed live** in run A and found the profile free ("no stale children"); **a live `0x800700AA` event was never reproduced headed** (the deterministic back-to-back-after-kill reproduction, SP-023 surprise #7, was not exercised — the packet's Step 4 matrix names renderer-kill/blocked-route/missing-media/exit, not a live lock). Typed and unit+path proven; NOT injection-level proven.

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

- (recorded after the actual spine_review_step call — never pre-recorded)

### Pre-completion consult (Step 4 gate)

- **Mode:** solo (Fable 5 requested; council route broken — T-7). **Actual answering model not surfaced by the tool (recorded honestly, per the Step 1 precedent).**
- **Verdict: PROCEED to done — evidence covers the completion criteria and honesty framings — with three corrections (all applied in this record + the board row before .DONE):**
  - **CORRECTION 1 (stale-profile proof level):** never let "typed and proven" read as end-to-end — classification + recovery unit-proven, relaunch-path sweep executed live (no lock present), live `0x800700AA` reproduction NOT performed. Applied verbatim above + as a clause in the board row.
  - **CORRECTION 2 (never pre-record review outcomes):** the Step-4 plan-review line was a prediction; replaced with the post-call record (see Plan review).
  - **CORRECTION 3 (this placeholder + final status checks):** verdict recorded here in full; `git status --short` + `git diff --check` re-run AFTER the final commit.
- **Minor (record-only):** the 378→390→391 walk is attributable (+12 `DtrhWatchdogTests`, +12 `DtrhExitFlowTests`, +1 `DtrhLoopbackContractTests`); wx2's APP-EXIT=143 is the harness's own SIGTERM — the Linux graceful-exit claim stays pinned to wx1 only.
- **Endorsed as sound:** the W17 detection stack (black window unreachable when ProcessFailed lands; measurable only on the heartbeat path — the Linux 24s number is the right honest framing), relaunch-once with exhaustion, the exit triptych, the split of the dead `/media/` prefix into honest 403/404 cells, the VN capture-phase ESC finding (WPF parity — shared payload tree), and re-running every salvaged cell instead of trusting `038fe603`. Board row stays WIP — correct.
