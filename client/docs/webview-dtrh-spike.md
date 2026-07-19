# WebView + DTRH spike evidence (SP-011)

**Date:** 2026-07-19/20 · **Task:** SP-011 (task-board row "Spike official WebView with the copied DTRH payload") · **Feeds:** BLOCKED row "Admit DTRH browser and origin design" (owner reviews this spike)

**Verdict shape:** every acceptance item has a NAMED observation below (PASS / FAIL / DIAGNOSED-BLOCKED + evidence pointer). Headline: the package restores/builds clean against the 12.1.0 baseline; the **exact unchanged payload boots and renders on Windows** (engine live t=1502ms cold, 360 fps steady-state, exit 0); on WSLg/X11 the embedded control **loads but never presents** (precisely diagnosed; adapter auto-fell-back to WebKitGTK which declares dialog-only scenarios), while **NativeWebDialog renders the page for real**; the unchanged WebView2-shaped `bridge.js` transports on Windows but **cannot transport on Linux** (`invokeCSharpAction` page→host works there — the admit row's "minimal transport-only edit" question is empirically live).

---

## 1. Package facts (live-feed verified 2026-07-19)

| Fact | Value | Source |
|------|-------|--------|
| Package ID | `Avalonia.Controls.WebView` | nuget.org flat-container index |
| Version claimed / actual | 12.0.1 / **12.0.1 exists and is current latest** (12.0.0 → 12.0.1; 11.4.0 prior train) | `https://api.nuget.org/v3-flatcontainer/avalonia.controls.webview/index.json` |
| License | **MIT** | nuspec (`licenses.nuget.org/MIT`) |
| TFMs | net10.0, net8.0, net10.0-android36.0, net10.0-browser1.0 | nuspec |
| Dependency | `Avalonia@12.0.0` (minimum) | nuspec |
| Native engines | None bundled (managed DLLs + one .mjs); platform-native engines | nupkg listing + package README |
| Windows engine | WebView2 (Blink); runtime preinstalled Win11, installer for Win10 | docs.avaloniaui.net/accelerate/components/webview/quickstart |
| Linux embedded engine | **WPE WebKit**, offscreen SHM rendering, X11+Wayland; pkg `libwpewebkit-2.0-1` (Debian/Ubuntu 24.04+) | quickstart §Platform prerequisites |
| Linux fallback | `NativeWebDialog` = WebKitGTK 4.1 dedicated window (`libgtk-3-0 libwebkit2gtk-4.1-0`); `LinuxWpeWebViewEnvironmentRequestedEventArgs.PreferWebKitGtkInstead` for embedded-GTK fallback | quickstart + webview-environment docs |
| Messaging (docs) | JS→host `invokeCSharpAction(body)` → `WebMessageReceived`; host→JS `InvokeScript`; `TryGetPlatformHandle` exposes native handles | nativewebview docs |

**Restore/build vs the 12.1.0/net10.0 baseline (empirical):** clean restore, no conflicts — Avalonia core family resolves to 12.1.0 (nearest-wins over the WebView's 12.0.0 minimum); `Avalonia.BuildServices/11.3.2` comes from `Avalonia/12.1.0` itself (present with or without the WebView package — not a spike anomaly). Spike skeleton + full host build: **0 warnings / 0 errors** on Windows AND WSL2 (contract testCommand green on both, incl. product suite 118/118 + 3/3 untouched).

**Integration consequence discovered:** Avalonia's Win32 NativeControlHost crashes without `supportedOS` entries in an app.manifest (`InvalidOperationException: Unable to create child window`). The spike carries `app.manifest`; **the product head will need one when a WebView lands.**

## 2. Payload identity (the "exact payload" claim — checkable)

- Source (served READ-ONLY, never copied, never written): `ConditioningControlPanel/Resources/web/dtrh/` — **1536 files, ~383MB**; git root tree SHA `40be29df822bbfece639b435b0820419aed54c19`; per-entry blob/tree SHAs + last-touching commit (9e9fc875) in `spine-tasks/SP-011-webview-dtrh-spike/record.md`.
- `bridge.js` blob `13af3f4d` served **BYTE-UNCHANGED** in every run (loopback request log shows `payload:bridge.js`; no overlay shadow exists).
- Tracked overlay dir (served overlay-first, every deviation a reviewable file): `overlay/probe.html` (transport checks), `overlay/matrix.html` (workers/WebAudio/autoplay). Both are NEW paths — they never shadow a payload file.
- `spike.html`/`spike.js` provenance: WPF-era M0 probe (commit ecfe184c) exercising exactly this pipeline — reused, not reinvented. `m2test.js` needs a full economy host → host-row scope, not reused.

## 3. Origin / route shape (for the admit row's loopback decision)

Two GET-only loopback origins on 127.0.0.1, ephemeral ports (random 49152–65535 retry loop — HttpListener cannot bind port 0; no URL ACL needed on Windows):

- **Page origin** (e.g. `http://127.0.0.1:59401`): `GET /dtrh/*` → overlay-first over the READ-ONLY payload tree; `GET /dtrh/` → index.html; `GET /health` → 200; everything else → 404; non-GET → 405 (framework surfaces 411 for length-less POST before the handler — recorded precisely).
- **Media origin** (e.g. `http://127.0.0.1:51545`): `GET /media/*` → payload `assets/` (READ-ONLY) with `Access-Control-Allow-Origin: <page-origin>` and `Access-Control-Expose-Headers: Content-Range`; OPTIONS preflight → 204 `Access-Control-Allow-Headers: range`.
- Traversal refused (403): encoded `..%2F`, `%2e%2e`, backslash, drive-colon, leading-slash, escape-under-root. (Literal `..` segments are normalized away by curl client-side and never reach the server — recorded so nobody "proves" traversal wrong with a normalized URL.)
- HTTP Range: 206 + `Content-Range`; invalid → 416. Required by video seek; verified by the range-fetch probe AND curl.
- **CORS-on-errors lesson:** a CORS-less error response surfaces to `fetch()` as an opaque TypeError, not a status — refusals on the media origin now carry CORS headers. Error diagnosability is part of the loopback contract.
- The two-origin split preserves the WPF `ccp.game`/`ccp.assets` cross-origin shape, so CORS/taint checks stay meaningful. Single-origin would make them trivially same-origin (prove nothing). This is direct evidence for the board's one-origin-vs-other-layout question: **two origins work with no payload change; the payload itself is origin-agnostic** (root-relative `/dtrh/...` importmap + host-supplied absolute media URLs).

## 4. Windows named observations (WebView2 150.0.4078.83, user-level install)

| # | Observation | Result | Evidence |
|---|-------------|--------|----------|
| W1 | `index.html` boots (the row's literal claim) | **PASS** | log `engine live (game mode)` t=1502ms cold; capture `index-engine2.png` (Warren hub rendered); `index-boot-early.png` (loader) |
| W2 | Bridge transport check 1 — JS host objects | **PASS** | `window.chrome.webview` PRESENT; `invokeCSharpAction` function (probe log) |
| W3 | Bridge transport check 2 — page→host | **PASS** | BOTH native `window.chrome.webview.postMessage` AND `invokeCSharpAction` raise `WebMessageReceived` (probe log) |
| W4 | Bridge transport check 3 — host→page | **PASS** | synthetic `MessageEvent` dispatch on `window.chrome.webview` delivered into unchanged `bridge.on` (probe log `check3 ... DELIVERED`) |
| W5 | Bridge ordering — ready/init handshake | **PASS** | `ready` (protocol 1) arrived t=750ms BEFORE NavigationCompleted (WebView2 pumps messages during load — hosts must not gate on nav-complete); host queued init/manifest until ready |
| W6 | Bridge ordering — preBuffer replay | **PASS** | host sent `probe-buffered` pre-registration; unchanged bridge.js replayed it synchronously on late `on()` (probe log `preBuffer REPLAY delivered`) |
| W7 | Loopback routes | **PASS** | §3 table; full request log of the engine's module graph (boot.js → vendor/three 687KB) served from the READ-ONLY tree; traversal/404/405/416 behaviors enumerated |
| W8 | WebGL (image + video texture, CORS taint) | **PASS** | `webgl-image glError=0 canvas untainted`; `webgl-video glError=0` (spike-result log lines) |
| W9 | Module workers | **PASS** | payload's real `/dtrh/engine/gifWorker.js` constructed, no error event in 2s (matrix log) |
| W10 | WebAudio + autoplay | **PASS with named precondition** | default: AudioContext `suspended`, unmuted `play()` → NotAllowedError. With WPF-parity `--autoplay-policy=no-user-gesture-required` (DtrhHostService.cs:120) via `WindowsWebView2EnvironmentRequestedEventArgs.AdditionalBrowserArguments`: `state=running`, unmuted `play()` resolved WITHOUT gesture; 20.88s bark decoded via CORS fetch (matrix logs both runs) |
| W11 | Video seek | **PASS** | `video-seek duration=1.0s target=1.0s landed=1.0s` (spiral.webm is a 1s clip — seek mechanics exercised; Range independently proven by W7/range-fetch) |
| W12 | CORS-clean media upload | **PASS** | cross-origin (media origin) image+video uploaded to WebGL textures untainted + 2D `getImageData` clean (spike-result lines) — preflight + Expose-Headers verified |
| W13 | Fullscreen | **PASS (enter) / OFF-toggle not exercised** | real F11 → page `fullscreen-set on=True` → host `WindowState=FullScreen` → capture `index-fullscreen.png` (1920x1080 borderless). Second synthesized F11 did not produce `on=False` (page-side toggle semantics; round-trip = host-row matrix) |
| W14 | Focus | **PASS with named behavior** | synthesized key BEFORE any click never reached the page; after ONE real click keys arrive (`input-keydown key=y`). Matches WPF host comment ("Keyboard focus does not land in the WebView2 child on a fresh launch until a click — claim it now"); the product host must claim focus explicitly at ready |
| W15 | Input (pointer) | **PASS** | real SendInput click → `spike-pointer x=632 y=369`; click on the hub produced `sfx ui_click` (native-cue message path observed) |
| W16 | Exit (graceful) | **PASS** | ESC held 1500ms (real keybd_event) → page `exit` at its 1.2s threshold → host Close → idempotent teardown → exit 0. `exit-done` not awaited (host row's watchdog owns the bounded wait) |
| W17 | Failure injection 1 — kill renderer process | **DIAGNOSED (transient zombie state)** | all 7 msedgewebview2 processes for the spike's UserDataFolder killed at t≈12–13s: web surface rendered BLACK (`after-renderer-kill.png`, t≈24s) and **`AdapterDestroyed` NEVER fired**. Heartbeat beat-math (cadence ~2s from t≈2.7s): beats continued ~28s post-kill (#16 at t=32.7s) then STOPPED — 20 beats at teardown (t=60.5s) ⇒ silence from t≈40.7s (pre-completion consult correction: an earlier draft said "continued uninterrupted"; beat counts disprove it). The failure evolves: black surface + live bridge (transient zombie) → bridge silence ~28s later — and STILL no Avalonia-level fault event at any point. Detection implication routed to host row: a heartbeat watchdog catches this only after the black-but-beating window; native `ProcessFailed` via `TryGetPlatformHandle` is the documented immediate signal (not exercised in spike) |
| W18 | Failure injection 2 — blocked loopback route | **PASS (diagnosable)** | media origin 403: all 4 media probes FAIL with `status=403` detail, `spike-done pass=false`, page survives, host/page-origin unaffected. (First attempt silently vacuous-passed: CORS-less 403 → fetch TypeError → spike.js abort — §3 lesson fixed + re-verified) |
| W19 | Failure injection 3 — missing media file | **PASS (diagnosable)** | 404 → probes FAIL `status=404`, page survives |
| W20 | Startup time (cold) | **MEASURED** | process start → nav-completed ~750ms; → engine live **1502ms** first-ever run, **1080ms** warm-profile run |
| W21 | Frame behavior | **MEASURED** | steady-state rAF 360.4/361.4/363.0/363.2 fps across 4 runs (WebGL-accelerated); no blanks/artifacts at boot/engine-live/fullscreen captures; black ONLY in the W17 zombie state |
| W22 | Teardown/exit code | **PASS** | 10+ runs: idempotent teardown, exit 0 every time; benign WebView2 stderr `Failed to unregister class Chrome_WidgetWin_0` on exit (shutdown noise) |

## 5. Linux named observations (WSL2 Ubuntu 26.04, WSLg — X11 via XWayland; `WAYLAND_DISPLAY=wayland-0 DISPLAY=:0`; Wayland native NOT claimed — owner question §5.1 untouched)

| # | Observation | Result | Evidence |
|---|-------------|--------|----------|
| L1 | WPE availability | **ABSENT (diagnosed)** | `apt-get install libwpewebkit-2.0-1` → "Unable to locate package"; `apt-cache search wpewebkit` empty on Ubuntu 26.04. The docs' WPE package does not exist here |
| L2 | Installed natives | **RECORDED** | `libgtk-3-0t64 3.24.52-0ubuntu1`, `libwebkit2gtk-4.1-0 2.52.3-0ubuntu0.26.04.2` via `wsl -u root` |
| L3 | Contract testCommand on WSL2 | **PASS** | sln 0W/0E; 118/118; 3/3 headless; spike 0W/0E; rc=0 |
| L4 | Embedded boot (WPE-default attempt) | **DIAGNOSED NON-BOOT (acceptance-satisfying evidence, never faked)** | Avalonia AUTO-fell-back to WebKitGTK (env-args arrived as `GtkWebViewEnvironmentRequestedEventArgs`; no flag needed). Adapter `WebKitGtk 2.52.3, IsSupported=True, SupportedScenarios=NativeDialog`. Page navigated + fetched the FULL module graph over loopback — **but XGetImage shows the web area DARK (never presents)** while Avalonia chrome renders (`index-linux.bmp`). Embedded presentation fails on this backend/session; libEGL DRI3 warnings on stderr |
| L5 | Bridge transport on WebKitGTK | **ABSENT (unchanged payload)** | probe: `window.chrome.webview` ABSENT, `invokeCSharpAction` function; page→host via invokeCSharpAction WORKS; bridge.js `isHosted=false` → no `ready`, no heartbeats, no boot — **the unchanged WebView2-shaped bridge cannot transport on Linux** |
| L6 | Host→page on WebKitGTK embedded | **AVAILABLE but unused-by-payload** | `InvokeScript` exists on the embedded control; a plan-B shim would need to exist BEFORE bridge.js module eval (import-time `isHosted` read) — i.e. the admit row's documented "one minimal transport-only edit" (shim inside bridge.js) vs early-injection (not offered by the current API at the needed moment). No fake shim performed |
| L7 | NativeWebDialog render | **PASS (renders for real)** | dialog window shows the DTRH loader ("Opening the hole…" + spinner) — XGetImage `dialog-linux.bmp` 800x600. **Constraint:** NativeWebDialog has NO `InvokeScript` → host→page messaging unavailable on the dialog path; game boot (init/manifest) cannot complete there as-is |
| L8 | Loopback on Linux | **PASS** | HttpListener two-origin server served the entire module graph from the READ-ONLY 9p-mounted payload tree; GET-only/Range/CORS/traversal code identical (shared assembly) |
| L9 | Linux budgets | **MEASURED** | spike build 4.24s incremental (cold figure lost to /tmp churn — recorded honestly); embedded page module graph complete ~1.6s after nav-start; dialog loader rendered at the 11s capture mark (bounded observation, not a precise TTI) |

## 6. Answers to the board's Decisions-needed (evidence, not decisions)

1. **"Does the official WebView run the unchanged WebView2-shaped bridge.js on Linux?"** — **NO** (L5). On Windows it does (W3/W4). The "documented minimal transport-only edit" is empirically required for Linux: either (a) a transport-only `bridge.js` shim mapping `window.chrome.webview` ↔ `invokeCSharpAction`+`InvokeScript` dispatch (must land before bridge.js import-time `isHosted` read → realistically inside bridge.js itself, exactly the capability-inventory's allowed host-only compatibility edit), or (b) native per-platform messaging via `TryGetPlatformHandle` (WebView2 handle documented; WPE/GTK handle only on NativeWebDialog per docs).
2. **"Does one protected loopback origin with route prefixes satisfy DTRH media isolation and performance?"** — the payload is origin-agnostic (§3). Two origins (page+media, CORS-scoped) are proven with zero payload change and keep taint checks honest; single origin would also work mechanically but weakens CORS evidence. Range + preflight + CORS-on-errors are the non-obvious contract points (§3).
3. **"Is WPE SHM presentation fast enough … WebKitGTK dedicated-window fallback?"** — WPE is **not packaged on Ubuntu 26.04** (L1), so WPE-SHM performance is UNMEASURABLE here; the WebKitGTK dedicated-window fallback **renders** (L7) but has **no host→page channel** (no `InvokeScript`) — it cannot host the game protocol as-is. The embedded GTK auto-fallback **does not present** on WSLg/X11 (L4). **This is now a hard owner question: real-hardware Ubuntu 24.04 (where WPE is packaged) or another WPE source is required to answer the embedded-performance question; WSLg cannot answer it.**
4. **New question for the board (from W17):** renderer-process-kill detection — `AdapterDestroyed` did not fire on Windows when the render/GPU processes were killed (black surface; heartbeats continued ~28s then stopped). The host row's "browser process failure" recovery policy needs a detection mechanism beyond Avalonia events + heartbeat (native `ProcessFailed` subscription via the platform handle is the documented route).

## 7. Explicit non-claims

- No Wayland-native session evidence (WSLg = X11 via XWayland; owner question §5.1 untouched).
- No packaged/bundled serving (that is the DTRH HOST row's acceptance, not this spike's).
- No product integration: nothing added to `CcpClient.sln`, `client/src/**`, or `client/tests/**`; the spike is quarantined under `client/spikes/` (build-only).
- No payload modification: `bridge.js` byte-unchanged everywhere; overlay files are NEW paths only.
- `exit-done`, fullscreen OFF-toggle, payout/meta/economy paths: host-row scope, not exercised.
- The 383MB tree was never copied; scratch/profile output never committed (gitignored `scratch/`).

## 8. Artifact index

- Spike host + overlay + tooling: `client/spikes/CcpSpike.WebView/` (quarantined; inherits `client/Directory.Build.props` — stated per packet).
- Run logs + captures: `client/spikes/CcpSpike.WebView/scratch/` (gitignored, regenerable; key captures referenced above).
- Worker session record (consult verdicts, engine-review log, transcripts): `spine-tasks/SP-011-webview-dtrh-spike/record.md`.
