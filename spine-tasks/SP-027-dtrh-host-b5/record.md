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
