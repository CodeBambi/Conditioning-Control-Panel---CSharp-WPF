# SP-011 record — spike official WebView with the copied DTRH payload

Worker session log. Design decisions, consult verdicts (provenance), research citations, transcripts, surprises, engine-review presence (T-2 closure evidence).

## Engine-review presence log (T-2 fix validation)

Packet emits structured `## Review Level: 2` heading (T-2 root-cause fix). Per-call record:

| Step | spine_review_step call | Result | Engine review fired? |
|------|------------------------|--------|----------------------|
| 1 | type=plan after step-1 commit | `skipped: true`, `spawnFailed: false` — "Nested reviewer spawn blocked inside pi worker session (SP-195); batch engine runs reviews after worker success" | NO in-worker (by design); post-.DONE engine review = the actual T-2 check, observable only at land time |
| 2 | type=plan after step-2 commit | `skipped: true`, `spawnFailed: false` (same SP-195 skip) | NO in-worker |

## Step 1 — package verification, payload archaeology, pre-approach consult

### Package claim verification (FIRST checkbox — live feed)

- **ID:** `Avalonia.Controls.WebView` — EXISTS on nuget.org.
  Feed: `https://api.nuget.org/v3-flatcontainer/avalonia.controls.webview/index.json` (queried 2026-07-19).
- **Versions:** 11.3.11 … 11.4.0, 12.0.0-preview1/preview2/rc1, 12.0.0, **12.0.1 (latest)**. The claimed 12.0.1 exists and IS current.
- **License:** MIT (`https://licenses.nuget.org/MIT`, per nuspec). ProjectUrl https://avaloniaui.net/. Source repo: `AvaloniaUI/Avalonia.Controls.WebView` (GitHub search; description "NativeWebView, NativeWebDialog and WebAuthenticationBroker").
- **TFMs:** net10.0, net8.0, net10.0-android36.0, net10.0-browser1.0. Dependency: `Avalonia@12.0.0` (minimum).
- **Package layout (nupkg inspection):** managed DLLs only + `staticwebassets/av-webview.mjs`; no bundled native engines (README: "leverages the platform's native web rendering capabilities"). AOT/trim compatible per README.
- **Native dependencies (official docs, 2026-07-19):**
  - Windows: Microsoft Edge **WebView2** (pre-installed Win11; may need runtime install on Win10). Two adapters exist (WebView2 modern; WebView1 legacy fallback). Sources: `https://docs.avaloniaui.net/accelerate/components/webview/quickstart` §Platform prerequisites/Windows.
  - Linux embedded `NativeWebView`: **WPE WebKit, offscreen (SHM) rendering**, works on X11 and Wayland. Package per docs: Debian/Ubuntu 24.04+ `libwpewebkit-2.0-1`; Fedora copr `philn/wpewebkit`; Arch `wpewebkit`.
  - Linux fallback `NativeWebDialog`: **WebKitGTK 4.1** in a dedicated GTK window (`libgtk-3-0 libwebkit2gtk-4.1-0` on Debian/Ubuntu; 4.0/soup-2.4 tolerated for older Ubuntu). Docs also name `LinuxWpeWebViewEnvironmentRequestedEventArgs.PreferWebKitGtkInstead` to fall back to the WebKitGTK adapter where WPE is unpackaged.
  - Summary matrix in docs: NativeWebView ✓ Windows/macOS/Linux(WPE)/iOS/Android, ✗ Browser.
- **Messaging shape (docs):** JS→host = `invokeCSharpAction(body)` → `WebMessageReceived` (Body string). Host→JS = `InvokeScript(script)`. `TryGetPlatformHandle()` exposes native handles (Windows: `IWindowsWebView2PlatformHandle` → CoreWebView2/Controller COM pointers; Linux: `IGtkWebViewPlatformHandle` for NativeWebDialog only — "NativeWebView does not support WebKitGTK" in the interop sense).

### Restore/build vs the 12.1.0/net10.0 baseline (empirical)

- Baseline read from `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`: net10.0, Avalonia/Avalonia.Desktop/Avalonia.Themes.Fluent all pinned **12.1.0**.
- Spike project (`client/spikes/CcpSpike.WebView/`) pins the same 12.1.0 trio + `Avalonia.Controls.WebView` **12.0.1**.
- `dotnet restore`: clean, no conflicts/downgrades. Resolved: Avalonia core family **12.1.0** (nearest-wins over WebView's 12.0.0 minimum), WebView 12.0.1. `Avalonia.BuildServices/11.3.2` resolved — traced: dependency of `Avalonia/12.1.0` itself, present with or without the WebView package; NOT a spike anomaly.
- `dotnet build -c Debug`: **succeeded, 0 warnings, 0 errors** (2.46s). Dependency-range outcome: 12.0.1 WebView is restore/build-clean against the 12.1.0 baseline.

### Payload archaeology

- Source tree (READ-ONLY): `ConditioningControlPanel/Resources/web/dtrh/` — absolute path on this machine `E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260719T210942\lane-1\ConditioningControlPanel\Resources\web\dtrh`.
- **File count: 1536** (git ls-tree -r). Size ~383MB on disk (380M in `assets/`).
- **Checkable identity (git):** root tree SHA `40be29df822bbfece639b435b0820419aed54c19` (git rev-parse HEAD:…/dtrh). Top-level: `assets/` tree bb1adbc8, `engine/` b5028e93, `game/` f1145db8, `shared/` c1539fae, `vendor/` ddc66e12; root blobs: boot.js ab637b55, **bridge.js 13af3f4d**, hostMedia.js db5daaa0, index.html 7c21e167, m2test.js c2599c71, modContent.js 7e2e167b→(see git ls-tree), spike.html f2fef6a5, spike.js 61091576, styles.css 8d9df0cb.
- Last commit touching the tree: 9e9fc875 (merge creator-mod-pipeline).
- **spike.html/spike.js provenance:** ONE commit `ecfe184c feat(dtrh-web): M0 spike - virtual-host pipeline verified (all pass)` — the WPF era's own capability-probe surface, driven by `Services/Chaos/DtrhSpike.cs` (comment in spike.html). It exercises exactly the pipeline the spike needs: Range fetch (expects 206), video metadata+arbitrary seek, cross-origin image→WebGL texture + 2D getImageData taint check, cross-origin video frame→WebGL texture, keyboard/pointer input. Driven by host `spike-run` message `{assets:{video,image}}`; results on-page (PASS/FAIL DOM) AND over the bridge (`spike-result`/`spike-done`). **Reusable for granular items — and it reports to the DOM even without a working bridge (results readable via InvokeScript if transport fails).**
- **m2test.js provenance:** M2+ (dee7c103 → 285055ee). A full bridge/economy exercise (meta commands, crafting, payout round-trip) requiring a complete host meta-store. **NOT spike-reusable** — that's the DTRH-host row's harness.
- **bridge.js (13af3f4d):** protocol v1, transport `window.chrome.webview.postMessage` (JS→host) and host `PostWebMessageAsJson` (host→JS); both sides buffer pre-handler/pre-ready messages (ordering contract). **WebView2-shaped.**
- **index.html:** served at `https://ccp.game/dtrh/` on WPF (virtual host); importmap uses **root-relative** `/dtrh/vendor/three/...` → any origin works if the payload is served under the `/dtrh/` path prefix. Offline, vendored three.js ESM.
- **boot.js boot contract:** register bridge handlers → `announceReady()` → host flushes `init` + `manifest` → engine starts (Warren hub); 45s progress-aware boot deadline → `boot-error` on stall; rAF heartbeat ~2s; hold-Escape 1.2s → `exit` → `exit-done`. Loader `#sf-loader` hides on engine live; `#sf-nope` shows on genuine WebGL/boot failure.

### WSL environment recon (pre-consult)

- WSL2 distro: **Ubuntu 26.04** (Resolute Raccoon), dotnet 10.0.110 present.
- `apt-cache search wpewebkit`: **EMPTY — no WPE package in Ubuntu 26.04 repos.** Docs' `libwpewebkit-2.0-1` (24.04+) is NOT available here. `libwebkit2gtk-4.1-0` IS available (candidate 2.52.3-0ubuntu0.26.04.2). → Linux path on this box must be the WebKitGTK adapter (`PreferWebKitGtkInstead`) or NativeWebDialog, or a precisely-diagnosed non-boot. This is exactly the admit row's Linux-dependency decision input.

### Pre-approach consult (solo Fable 5; council unavailable per failed probe — packet honesty framing)

Design presented: two HttpListener loopback origins (page + media, cross-origin split preserving the WPF ccp.game/ccp.assets taint-check meaning; GET-only + Range; overlay-first; traversal refusal) / host→page via InvokeScript synthetic MessageEvent on window.chrome.webview (bridge.js byte-unchanged) / Linux order = PreferWebKitGtkInstead embedded → NativeWebDialog → diagnosed non-boot / failure case 1 = kill msedgewebview2 renderer child process.

**VERDICT (2026-07-19, solo): design approved on all four points, with four concrete corrections/additions before you build.** Verbatim:

> Q1 — Two-origin split: justified, not YAGNI. The board's Decisions-needed explicitly asks whether one loopback origin with route prefixes suffices or another origin layout is required — the two-origin shape produces evidence *for that exact question*, and single-origin taint checks prove nothing (same-origin is trivially CORS-clean). Cost is one extra HttpListener with a different root + header. Two traps you must handle or the CORS evidence rots:
> 1. **`Range` is not a CORS-safelisted request header.** spike.js's `testRangeFetch` does a cross-origin `fetch()` with a `Range` header → the browser sends a **preflight OPTIONS** to the media origin. Your "GET-only" media origin must answer OPTIONS preflights (`Access-Control-Allow-Headers: range`, `Access-Control-Allow-Origin: <page-origin>`) or range-fetch fails for CORS reasons and you'll misdiagnose it as a Range-support failure. Keep "GET-only" meaning "no state-changing methods"; preflight OPTIONS is part of serving GET cross-origin. Record this nuance in the origin/route-shape evidence — it's a real input to the admit row's loopback contract.
> 2. **`Access-Control-Expose-Headers: Content-Range`** on 206 responses, or spike.js's `r.headers.get('Content-Range')` logs null cross-origin. The ok-check doesn't depend on it, but the evidence line does.
> Also: **HttpListener cannot bind port 0.** There is no OS-assigned-port support. Pick ephemeral ports with a retry loop (random port in 49152–65535, catch `HttpListenerException`, retry), and record the chosen shape. 127.0.0.1 prefixes need no URL ACL on Windows.
> Q2 — Synthetic MessageEvent: honest, but it has a hard precondition you haven't verified, and you need a declared plan B. Dispatching `new MessageEvent('message', {data: …})` on `window.chrome.webview` works *only if that object exists* under the Avalonia WebView2 adapter (i.e., the adapter left `IsWebMessageEnabled` on). If the adapter disables it, `window.chrome.webview` is `undefined`, bridge.js's `isHosted` is false, `announceReady()` silently no-ops, and your synthetic dispatch has no target. So the Windows transport observation is really three ordered empirical checks — record each as its own named observation: 1. Does `window.chrome.webview` exist in the page [TRUNCATED at this point in transit]

**Worker completion of the truncated direction (declared, faithful to the verdict's shape):** the three ordered transport checks are (1) does `window.chrome.webview` exist in the page; (2) if yes, does native `window.chrome.webview.postMessage` raise Avalonia `WebMessageReceived` (page→host); (3) does synthetic `MessageEvent` dispatch deliver host→page into unchanged bridge.js handlers. **Plan B (declared pre-build):** if `window.chrome.webview` is absent, a NEW tracked overlay file (never shadowing bridge.js) provides a minimal `window.chrome.webview`-compatible EventTarget over `invokeCSharpAction` — served overlay-first, labeled in evidence as spike demonstration material for the admit row's "minimal transport-only edit" question, NOT a payload edit. Consult response truncation itself recorded for provenance.

## Step 2 — quarantined spike host + loopback + Windows boot

### Host shape (per consult verdict)

- `client/spikes/CcpSpike.WebView/` — minimal Avalonia 12.1.0/net10.0 app, NOT in `client/CcpClient.sln` (verified: never added), build-only, inherits `client/Directory.Build.props` (single Version authority — stated here per packet).
- Two-origin loopback (`LoopbackServer.cs`): page origin serves `GET /dtrh/*` overlay-first (tracked `overlay/` over the READ-ONLY payload tree) + `GET /health`; media origin serves `GET /media/*` from the payload's `assets/` (READ-ONLY) with `Access-Control-Allow-Origin: <page-origin>`, `Access-Control-Expose-Headers: Content-Range`, and OPTIONS preflight (`Allow-Headers: range` — consult correction: Range is NOT a CORS-safelisted request header). Non-GET → 405; unknown route → 404; traversal (`..`, `\`, `:`, leading `/`, escape-under-root) → 403; Range → 206/`Content-Range`, invalid → 416.
- Ports: HttpListener cannot bind port 0 (consult) → retry loop, random in 49152–65535. Origin shape per run recorded in the spike log (e.g. page `http://127.0.0.1:59401`, media `http://127.0.0.1:51545`). 127.0.0.1 prefixes needed no URL ACL on this Windows box (consult assertion confirmed empirically).
- Scratch: `client/spikes/CcpSpike.WebView/scratch/` (gitignored via spike-local `.gitignore`) holds the WebView2 UserDataFolder, WPE dirs, spike logs, captures. Payload writes: none observed/attempted; any would land under scratch.
- Windows app.manifest with supportedOS REQUIRED — first run crashed `InvalidOperationException: Unable to create child window for native control host` (NativeControlHost). Fixed by tracked `app.manifest`. **Integration consequence for the admit row: the product head will need the same manifest.** (Surprise, durable.)
- WebView2 runtime: user-level install at `%LOCALAPPDATA%\Microsoft\EdgeWebView\Application\150.0.4078.83` (registry `pv` absent; loader found it).

### Transport checks (probe.html — tracked overlay file importing the payload's UNCHANGED bridge.js)

Adapter: `DetailedWebViewAdapterInfo { Type = WebView2, Engine = Blink, Version = 150.0.4078.83, IsSupported = True, IsInstalled = True, SupportedScenarios = NativeControlHost }`.

- **check1:** `window.chrome.webview` PRESENT; `invokeCSharpAction` = function. (`transport check1: window.chrome.webview=present invokeCSharpAction=function`)
- **check2 (page→host):** BOTH `window.chrome.webview.postMessage` AND `invokeCSharpAction(...)` raised Avalonia `WebMessageReceived`. → Unchanged `bridge.js` page→host transport WORKS on Windows under the Avalonia WebView2 adapter.
- **check3 (host→page):** synthetic `MessageEvent` dispatch on `window.chrome.webview` DELIVERED into the unchanged `bridge.on('probe-h2p')` handler. → Host→page works byte-unchanged via this spike transport (admit row picks the real one; native `CoreWebView2` handle is also reachable via `TryGetPlatformHandle` per docs — not needed for the spike).
- Captures: `scratch/probe-boot.png` (probe transcript visible in the rendered page; text dim — cosmetic only).

### Windows boot of the exact payload (index.html)

- `ready` (protocol 1) arrived at t=750ms — **BEFORE NavigationCompleted** (WebView2 pumps messages during load; worker race fixed — detect transport on demand in SendBootMessages). Recorded under bridge-ordering evidence.
- init+manifest sent ready-triggered (WPF-shaped init: settings.masterVolume, modId, modContent:null, runSetup full shape, m2Test:false; manifest empty on this run).
- **ENGINE LIVE (game mode) at t=1502ms cold** (page log `engine live (game mode)`; loader hid; Warren hub rendered).
- Full module graph loaded over loopback from the READ-ONLY tree: boot.js, bridge.js, hostMedia.js, modContent.js, shared/, game/ (chaosRun 210KB, warren 76KB, spawner 103KB…), engine/ (scene.js 72KB…), `vendor/three/three.module.min.js` 687KB via the importmap's root-relative `/dtrh/...` specifier — the importmap works unchanged on the loopback origin.
- Frame behavior: steady-state rAF average **360.4 fps** over 90 frames at t=2268ms (WebGL-accelerated; no blanks observed at boot/engine-live captures).
- Heartbeats ~2s cadence (29 over 60s run). Clean auto-quit teardown, exit code 0. Benign stderr on exit: WebView2 `Failed to unregister class Chrome_WidgetWin_0` (shutdown noise, no functional impact).
- Captures: `scratch/index-boot-early.png` (loader "Opening the hole…"), `scratch/index-engine2.png` (Warren hub rendered: tunnel WebGL, FALL IN, difficulty/duration pills, HUD).
- Surprises: (a) app.manifest requirement (above); (b) `ready` precedes NavigationCompleted — hosts must not gate message handling on nav-complete.
