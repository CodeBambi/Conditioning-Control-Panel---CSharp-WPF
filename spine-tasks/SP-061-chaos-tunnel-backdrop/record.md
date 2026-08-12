# SP-061 record — Chaos tunnel backdrop (opaque below-Topmost web surface)

**Task:** spine-tasks/SP-061-chaos-tunnel-backdrop · **Review Level:** 2 · **Board row:** "Chaos tunnel backdrop surface (`tunnel` + `vendor` payload trees, v6.7.x)" (P1)
**Engine-review presence (T-2):** recorded per `spine_review_step` call below.

---

## Step 1 — dual-source archaeology + layering design + pre-approach consult

### WPF truth (`ConditioningControlPanel/Chaos/ChaosTunnelService.cs`, 573 lines — behavioral truth)

| Fact | Evidence |
|---|---|
| Static service, gated on `ChaosTunnelEnabled` only (no Story gate), default OFF | `ChaosTunnelService.cs:58`, header `:34` |
| `Preload()` at countdown start — warm the ~2s WebView2/three.js boot under the countdown; claims the warm window against the exit watchdog on RunAgain | `:66-80` |
| `Show()` — no-op + closes stray window when disabled; resets streak dedup; starts z-guard; posts `run-start` (queued until page `ready`) | `:82-99` |
| `CloseActive()` — idempotent: `run-end` to page, close on page `exit-done` OR a 1200ms `DispatcherTimer` force-watchdog | `:141-163` (`:153` the 1200ms) |
| Window: `WindowStyle.None`, `AllowsTransparency=false` (OPAQUE — a WebView2 child HWND does not paint in a layered window), `Topmost=false`, `ShowInTaskbar=false`, `ShowActivated=false`, `Focusable=false`, `NoResize`, manual at (0,0) sized `PrimaryScreenWidth×PrimaryScreenHeight` | `:175-197` |
| **Z-order mechanism:** `SinkToBottom()` — `SetWindowPos(HWND_BOTTOM, SWP_NOMOVE\|SWP_NOSIZE\|SWP_NOACTIVATE)` right after show (`:206`, `:433-441`). Plus a **z-guard**: 1500ms `DispatcherTimer` (`:448`) + a preventive `WM_WINDOWPOSCHANGING` hook on MainWindow (`:461-470`, `:484-510`) that rewrites MainWindow raise-to-`HWND_TOP` requests to insert-after-the-tunnel, and `EnforceBelowTunnel()` (`:513-539`) walking `GW_HWNDPREV` up from the tunnel to demote MainWindow back below it. Reason (`:201-205`): freshly shown the tunnel would land above the HUD/sidebar; at the bottom everything sits over it, and MainWindow is the one thing that must stay UNDER it (the opaque tunnel covers the launcher during a run). |
| Ex styles: `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW` applied at `SourceInitialized` (`:199`, `:556-566`). Deliberately **NO `WS_EX_TRANSPARENT`** — clicks reach the page for power-up raycasting (`:31`) |
| WebView2 env: `UserDataFolder = <App.UserDataPath>/browser_data_chaos_tunnel` (`:218`); browser args `--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion` (`:221-229` — keep the WebGL swapchain DWM-composited below topmost overlays; stop Chromium occlusion-tracking from throttling rAF when covered) |
| Serving: `SetVirtualHostNameToFolderMapping("ccp.tunnel", Resources/web, Deny)` (`:249`); start URL `https://ccp.tunnel/tunnel/index.html` (`:43-46`); navigation locked to the virtual host (`:264-271`) |
| WebView2 settings: DevTools/context-menus/statusbar/accelerator-keys/zoom/error-page all OFF; WebMessage ON (`:233-240`) |
| Host→page: `PostWebMessageAsJson`; pending FIFO queue flushed on page `ready` (`:333-344`, `:346-354`) |
| Page→host messages: `ready`, `sfx` (→ `ChaosSfx.Play`, master-volume aware), `powerup-click`, `exit-done` (RunAgain re-arm guard), `log` (`:273-312`) |
| Host→page messages: `run-start`, `run-end`, `zone-hint`, `intensity`, `streak` (deduped on combo, `:120-128`), `video-playing` (page pauses its render loop; host pauses ambient), `spawn-powerup` |
| Ambient bed: NAudio `LoopStream` over `ChaosSfx.ResolvePath("tunnel_ambient")`, volume `master*0.26` clamped; silent when the asset is absent | `:357-430` |
| NO ProcessFailed / heartbeat handling for the tunnel (those are DTRH-host classes) — renderer death leaves a black backdrop; next Show/CloseActive recreates | whole-file read (no such handler exists) |

### First-attempt lessons (`ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/Services/Chaos/ChaosTunnelService.cs`, 391 lines — lessons ONLY)

- **ACCEPT (mechanisms, not code):** anti-MPO browser args verbatim (WPF parity); own per-surface WebView2 `UserDataFolder` (its `avalonia_browser_data_tunnel` — the dedicated-profile class); pending-queue + ready handshake shape (mirrors WPF); DIP-correct screen sizing at `Opened` via `Screens.Primary` (`Bounds / Scaling`) — the right Avalonia shape vs WPF's raw pixel size; `ShowActivated=false`/`Focusable=false`/`Topmost=false`/`ShowInTaskbar=false` carry over.
- **ADAPT:** borderless via v12 API. The first attempt used `ExtendClientAreaToDecorationsHint=true` with a comment claiming `Window.SystemDecorations` "was renamed/obsoleted in Avalonia v12". **Binary-verified on the pinned 12.1.1: `Window.SystemDecorations` EXISTS, retyped to the new `WindowDecorations` enum (`None,BorderOnly,Full`), plus a `Window.WindowDecorations` alias.** v12-correct borderless = `WindowDecorations = WindowDecorations.None`, not the extend-client-area hint (which keeps resize/chrome behaviors the tunnel must not have).
- **ADAPT:** ex-styles via v12-sanctioned `Win32Properties.AddWindowStylesCallback(TopLevel, CustomWindowStylesCallback)` (present in 12.1.1, binary-verified) at creation, rather than post-show raw `SetWindowLong` (both reach the same Win32 surface; the callback is the package's own seam).
- **REJECT (behavioral deviation):** its z-guard is a 1500ms timer that blindly re-sinks the TUNNEL to `HWND_BOTTOM`. WPF's guard does the opposite: it keeps the tunnel ABOVE MainWindow (demoting MainWindow), while sinking happens once at show. The first attempt's shape lets the launcher cover the tunnel backdrop whenever it re-activates — a WPF-parity break.
- **REJECT:** fixed `Width=1920, Height=1080` initializer before the Opened rescale (arbitrary hard-coded size; WPF sizes from the primary screen at creation).
- **REJECT:** hard-coded ambient asset path + LibVLC `--input-repeat=-1` player (the client has no chaos sound-library port; see named limits).
- **REJECT:** DI-injected `IChaosTunnelService` service shape (`ISettingsService`/`ILibVlcProvider` ctor) — the client uses explicit construction, no DI container (startup-shutdown contract §7).

### Payload derivation (verified, not guessed)

- Import map (`tunnel/index.html:29-34`): `"three" -> ../vendor/three/three.module.min.js`, `"three/addons/" -> ../vendor/three/addons/`.
- Static imports across the 7 tunnel modules: `three` + the 6 sibling modules only (`grep '^import'`).
- Dynamic imports (`main.js:71-73`, bloom tier): `three/addons/postprocessing/{EffectComposer,RenderPass,UnrealBloomPass}.js`.
- Transitive closure of the addons tree (their own `^import` lines): `MaskPass`, `Pass`, `ShaderPass` (postprocessing) + `CopyShader`, `LuminosityHighPassShader` (shaders). **The entire on-disk vendor tree (9 files) is the resolution closure — nothing vendored is dead, nothing resolved is missing.**
- **Manifest addition = 18 copied entries:** `payload/tunnel/*` (9) + `payload/vendor/three/**` (9), output roots `payload/tunnel` + `payload/vendor` preserving the `../vendor` relative shape the import map requires (WPF maps the `Resources/web` root for the same reason, `ChaosTunnelService.cs:43-46`).
- **Distinction from the landed DTRH `engine/tunnel.js`:** that is a DTRH-internal game module inside `payload/dtrh/` (served since b1; the dtrh tree vendors its own three under `payload/dtrh/vendor/`). This task's trees are the STANDALONE `Resources/web/tunnel/` + top-level `Resources/web/vendor/three/`, consumed only by the tunnel page's import map (SP-056 inventory `upstream-payload-inventory.json` notes: dtrh/intake vendor their own three internally).

### Page contract (from the payload itself)

- Bridge: `window.chrome.webview` only (`main.js:20-21,40`) — page→host `postMessage`, host→page `message` listener; **graceful null-degrade** (no host = no-op post, no listener; page boots and renders behind its opaque black curtain).
- Boots and STARTS the render loop immediately (`main.js:206 start()`), curtain opaque until `run-start` fades it (`:165`); posts `ready` after build (`:207`).
- Dev affordances (never used by product): `?auto` self-run-start without a host, `?demo` powerup spawner, `?diag`+`?at=` rail teleport for headless captures (`main.js:26-29`, `:211-219`).
- SFX is host-routed only (`audio.js`: posts `{type:'sfx',name,scale}`, expects the host to resolve `Resources/sounds/chaos/{name}.mp3`; silently no-ops without a host).

### Design (pre-consult)

1. **`Features/Chaos/ChaosTunnelWindow.cs`** (code-only Window): opaque, `WindowDecorations.None`, `Topmost=false`, `ShowInTaskbar=false`, `ShowActivated=false`, `Focusable=false`, `CanResize=false`, black background, positioned/sized on the PRIMARY screen at `Opened` (DIP-correct via `Screens.Primary`). Windows: `Win32Properties.AddWindowStylesCallback` adds `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW` at creation; `SetWindowPos(HWND_BOTTOM, …NOACTIVATE)` sinks it after show. Embedded `NativeWebView` child; `EnvironmentRequested` sets `UserDataFolder = <dataRoot>/chaos/tunnel/wv2-profile` (dataRoot rides `CCP_DATA_ROOT` via `CompositionRoot.DefaultSettingsPath()`) + the two anti-MPO browser args. **Linux = typed Unavailable, NO dialog surface (consult ruling 3, the SP-054 intake precedent):** the page speaks `chrome.webview` only (no host bridge on WebKitGTK) and the `NativeWebDialog` toplevel has no keep-below control, so below-Topmost cannot be honored — a black-curtain surface with no bridge and no z-order proof would be an unfalsifiable claim. WSL zero distros on this machine = the standing gate.
2. **`Features/Chaos/ChaosTunnelLoopback.cs`** — a dedicated minimal §4-discipline server (NOT the DTRH `LoopbackServer`, whose /dtrh//media//bridge/ route table + inbox are DTRH-shaped): ONE GET-only origin on 127.0.0.1 (no media origin — the tunnel serves no media; no CORS split needed, single origin like WPF's single virtual host), ephemeral-port retry with a FRESH listener per attempt (SP-023's disposed-instance lesson), `/health`, `/tunnel/*` → output `payload/tunnel`, `/vendor/*` → output `payload/vendor`, MIME allowlist pinned to the swept tree extensions **{.html, .js}** with 415 deny-by-default + nosniff, traversal refusal 403, route-class logging only (§4.8).
3. **`Features/Chaos/ChaosTunnelService.cs`** (instance, explicit construction): `Preload/Show/CloseActive/SendZoneHint/SetIntensity/SetStreak/SetVideoPlaying/SpawnPowerup` over a testable pure core (`ChaosTunnelCore`: ready latch, pending FIFO, streak dedup, RunAgain re-arm, exit-done fast path vs `OnExitWatchdogElapsed()` force — the 1200ms product timer's elapsed handler invoked DIRECTLY in tests; no wait, no injected budget). Host→page = synthetic `MessageEvent` dispatch via `InvokeScript` (the landed DtrhHostWindow.axaml.cs:1257 shape, WPF `PostWebMessageAsJson` parity). `sfx` frames are HANDLED TYPED (parsed, counted, logged presence+shape — the cues exist upstream but the client has no chaos sound-library port: a content gap owned by that row, NEVER claimed as WPF parity — the SP-051 falsified framing); `SetVideoPlaying` still posts `video-playing` (page render-loop pause is real); the ambient bed is N/A-because-no-bed (named limit).
4. **Z-guard (WPF semantics, not the first attempt's):** sink once at show + while a run is live a 1500ms `DispatcherTimer` re-demotes the DASHBOARD below the tunnel (`SetWindowPos(mainHwnd, tunnelHwnd, …)` after a `GW_HWNDPREV` walk). **The preventive `WM_WINDOWPOSCHANGING` rewrite hook is deliberately NOT ported** (no-visible-flash polish; the timer corrects within 1.5s) — named as a rejected-for-now alternative + intended filing. SFX/ambient per the typed-handling note in item 3.
5. **Capabilities:** ONE capability `chaos-tunnel-webview-embedded` registered in `CompositionRoot` (Lifecycle wiring, in scope). Windows: delegates to `DtrhCapabilityProbes.ProbeEmbedded` (literally the same engine dependency load — cited reuse). Linux: the tunnel's OWN `Unavailable(unsupported-platform)` naming the tunnel's two gaps (chrome.webview-only page transport, `main.js:20-21,40`; no keep-below control on the NativeWebDialog toplevel — b3 precedent). NO `chaos-tunnel-web-dialog` capability (a green row for an unadmitted surface is the divergence the capability contract bans). On Unavailable: no window, one typed log line, honest exit (consult ruling 3b — never a silent fallback).
6. **Stale-profile-lock:** reuse `DtrhProfileLock.IsStaleProfileLock/TryRecover` (generic msedgewebview2 machinery) on tunnel navigation, retry ONCE, typed + logged (SP-027 class inherited by any second WebView2 host). ProcessFailed/heartbeat: **disclaimed with evidence** — the tunnel page has no heartbeat and WPF wires no ProcessFailed for the tunnel; renderer death = black backdrop until next lifecycle event (WPF parity).
7. **Manifest + csproj:** 18 `copied` entries (ids `tunnel.payload/…`, trust-anchor provenance like the dtrh/intake entries) + two linked Content globs in `CcpClient.Desktop.csproj` (`Resources/web/tunnel/**` → `payload/tunnel`, `Resources/web/vendor/**` → `payload/vendor`). **The csproj is NOT named in File Scope** — documented File-Scope amendment, wiring-only, the SP-023/SP-054 precedent class (their packets recorded the same); required because `--verify-assets` otherwise fails on unmanifested/un-copied payload.
8. **Harness (HARNESS-ONLY, Program/App wiring):** `--tunnel-demo` (show + run-start at ready through the real message path), `--tunnel-drive "<steps>"` (timed steps: `topmost-show`/`topmost-hide` over the REAL `DtrhVideoWindow` Topmost surface, `tunnel-hide`/`tunnel-show`, `main-raise` for the z-guard), `--tunnel-auto-close N`. External PS harness: pre/post real-profile sha256 manifests (path-hashed) + diff, `GetWindowRect` lines for every window per capture, `GetForegroundWindow` before/after tunnel show, screen captures per phase.
9. **Tests (timing-guard clean):** tunnel loopback contract (GET-only 405, both routes 200, 415 negative control, traversal 403, 404s, route-class logging), core state machine (ready flush order, streak dedup, exit fast/force paths via direct elapsed-handler calls, RunAgain re-arm), capability selection from injected states, window policy assertions (headless), manifest tunnel-entry presence. All waits via `TestWait` or direct handler invocation; zero new deadline literals; `LoopbackListenerRegistry` registered per SP-059.

### Consults

(verdicts + actual answering models recorded below)

#### Pre-approach consult (Step 1)

**Mode:** solo ×2 (first call truncated mid-item-3 — the SP-022/SP-023 truncation class; second call completed the ruling). **Actual answering model:** NOT surfaced by the tool response (recorded honestly, the standing provenance discipline).

**Verdict — design approved with these rulings/corrections (ALL folded into the design above where marked †):**

1. **Dedicated minimal loopback server APPROVED** over reusing/extending the DTRH `LoopbackServer` (its /dtrh//media//bridge/ route table + inbox are DTRH-shaped; the intake-style overlay-borrow would expose the whole `payload/` parent). Guardrails: each route rooted at its own output dir; traversal/MIME logic cites the `LoopbackServer.cs` lines it mirrors (security-hardening sweeps must find both); shared-invariant tests pin both servers. † **CORRECTION: derive the MIME pin, never assert it** — the allowlist {.html,.js} is pinned by a swept extension walk of both output roots, and a test walks the roots asserting every file's extension is in the allowlist (the §4.4 "pinned by the extension sweep" discipline + drift guard).
2. **Z-order cut APPROVED:** sink-to-bottom at show + the 1500ms timer demoting ONLY the dashboard (never arbitrary windows), stopped at run end, keeping WPF's 512-iteration walk bound; the `WM_WINDOWPOSCHANGING` rewrite hook is the no-flash refinement whose trigger has no greenfield source yet — rejected-for-now + board filing. † **CORRECTION (weak-proof catch):** the headed drive MUST include the direction "Topmost surface shown FIRST, then tunnel (re)shown beneath it" — showing the tunnel only before the Topmost window proves nothing (SinkToBottom happens to be right at that moment).
3. **Linux = typed Unavailable, NOT a degraded black dialog** (the SP-054 intake precedent: a surface that can only render a black curtain, has no host bridge, and cannot be exercised on this machine is an unfalsifiable claim). † **CORRECTION: register ONLY `chaos-tunnel-webview-embedded`, with its OWN Linux reasons** — delegating to `DtrhCapabilityProbes.ProbeEmbedded` would carry DTRH's reason text; the tunnel's Linux `Unavailable(unsupported-platform)` names the tunnel's own two gaps (page transport is `chrome.webview`-only, `main.js:20-21,40`; the `NativeWebDialog` toplevel exposes no keep-below control so below-Topmost cannot be honored — b3 Linux-toplevel-z-order precedent). No `chaos-tunnel-web-dialog` capability at all (a green capability row for an unadmitted surface is the divergence the capability contract exists to prevent). On Unavailable: NO window created, one typed log line, `--tunnel-demo` exits honestly (exit 0 + typed line). Available never implies rendering — rendering is claimed only by the headed capture.
4. **SFX/ambient plan OK but the framing was the ALREADY-FALSIFIED one (correction):** the six upstream `tunnel_*.mp3` cues EXIST in the WPF tree (`Resources/sounds/chaos/` — verified by listing), so "unresolved no-op = WPF parity" is false (SP-051 falsified the near-identical claim via `ChaosSfx.cs:33`'s fallback chain). Recorded as what it is: **a greenfield content gap owned by the chaos-sound-library row**. Implementation requirements: the `sfx` frame is HANDLED TYPED — parsed, counted, logged presence+shape (never silently dropped, never a resolved path logged); `SetVideoPlaying` still does its real half (posts `video-playing` so the page pauses its render loop, `main.js:180`); ambient bed = N/A-because-no-bed, named limit.
5. **csproj amendment APPROVED** (documented wiring-only class, the SP-023/SP-054 precedent): mirror the landed glob shape exactly (`CcpClient.Desktop.csproj:50-63` — `Link` + `CopyToOutputDirectory=PreserveNewest` + `CopyToPublishDirectory=PreserveNewest`; NO `ExcludeFromSingleFile` — the dtrh/intake globs carry none). MSBuild-special-character check on the 18 filenames: **NONE** (`% # ^ &` swept clean). Amendment named in record.md (here + Step 2), STATUS.md, and the Step-2 commit body.
6. **† Filing correction:** `client/docs/upstream-payload-inventory.json` goes disposition-stale the moment these trees are served (`tunnel`/`vendor` still say `not-ported`); SP-056's guard checks well-formedness only, so **the suite stays green on a stale disposition** — stated explicitly in intended filings; the file is outside worker File Scope, so the orchestrator flips both entries to `served` naming this task's serving code path as evidence.

**Provenance anchors (derived, not guessed):** tunnel git tree `7f992f2f6dc40e8de2d22d3241dc6c180a26d497`, vendor git tree `a87ef4a3b2d9b3056208de5cb5ead011aacd0f63`; both added in `3e84a831`, last touched `f1135c4c` (tunnel v2).

---

## Engine-review log (T-2)

| Step | Type | Result | Artifact |
|------|------|--------|----------|
| 1 | plan | **SKIPPED BY DESIGN** (nested reviewer spawn blocked in worker session; `skipped=true, spawnFailed=false` — the engine runs reviews after `.DONE`, SP-195) | `.reviews/1-20260812T174335.md` |
| 2 | plan | (recorded post-call below) | |

---

## Step 2 — implement the window + serving + manifest

**File-Scope amendment (documented per the SP-023/SP-054 precedent class):** `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` is not named in the packet's File Scope, but the copied-payload convention REQUIRES its two linked Content globs (`payload/tunnel`, `payload/vendor`) — wiring-only, the exact shape of the landed dtrh/intake globs (`Link` + PreserveNewest ×2, no `ExcludeFromSingleFile`). Also named in STATUS.md and this step's commit body. The packet's own "`client/assets/**` + the asset manifest source of truth" line covers `Assets/assets.manifest.json` (18 entries appended, formatting-identical mechanical append verified by diff: +270 lines, zero reformat churn).

**Landed:**
- `Features/Chaos/ChaosTunnelCore.cs` — the pure protocol state machine (ready latch + pending FIFO, streak dedup, RunAgain re-arm, exit-done fast/force, typed sfx counting).
- `Features/Chaos/ChaosTunnelLoopback.cs` — the dedicated one-origin §4-discipline server (`/health`, `/tunnel/*`, `/vendor/*`; GET-only 405; swept {.html,.js} MIME pin with 415 deny-by-default; traversal 403; nosniff; ONE-segment route-class logging — tighter than the DTRH two-segment shape). Traversal/MIME logic cites the mirrored `LoopbackServer.cs` lines.
- `Features/Chaos/ChaosTunnelCapabilityProbes.cs` — ONE capability (`chaos-tunnel-webview-embedded`); Windows delegates to the DTRH embedded probe (same engine load), Linux carries the tunnel's own two-gap Unavailable (consult ruling 3b; no dialog capability exists).
- `Features/Chaos/ChaosTunnelWindow.cs` — opaque borderless (`WindowDecorations.None`, 12.1.1-binary-verified), non-topmost/non-activating/non-focusable/no-taskbar, primary-screen sizing at Opened (DIP-correct), `Win32Properties.AddWindowStylesCallback` ex-styles at creation (NOACTIVATE|TOOLWINDOW, never TRANSPARENT), anti-MPO + occlusion browser args, per-surface `wv2-profile` under the CCP_DATA_ROOT-riding data root, stale-profile-lock retry-once via the shared `DtrhProfileLock` machinery.
- `Features/Chaos/ChaosTunnelService.cs` — own-lifecycle orchestration: Preload/Show/CloseActive + the message set; sink-to-bottom at show; the z-guard (1500ms cadence, dashboard-only demotion, 512-bound walk, stops at run end — WPF :444-539 semantics; the WndProc rewrite hook NOT ported, filed); exit watchdog 1200ms (product timer; tests invoke the elapsed path directly — zero test waits, zero injected budgets).
- `Features/Chaos/ChaosTunnelWin32.cs` — the shared constants/P-Invoke surface.
- `Features/Chaos/ChaosTunnelDemoDrive.cs` — HARNESS-ONLY `--tunnel-demo`/`--tunnel-drive`/`--tunnel-auto-close` (the --loom-demo demonstrator class; timed steps honestly labeled).
- Wiring (in-scope): CompositionRoot capability registration; Program.cs/App.axaml.cs flag threading.
- Manifest: 18 `copied` entries (ids `tunnel.payload/*` ×9, `tunnel.vendor/*` ×9), trust-anchor provenance (git trees 7f992f2f… / a87ef4a3…, added 3e84a831, last touched f1135c4c).

**Tests (+29 unit, +2 headless — 892/892 + 35/35 green):** `ChaosTunnelCoreTests` (10), `ChaosTunnelLoopbackTests` (12 — incl. the 415 negative control, the shared-invariant pin against the mirrored DTRH server, the swept-extension derivation guard, the manifest two-direction-vs-upstream-trees guard), `ChaosTunnelCapabilityTests` (4), `ChaosTunnelWindowHeadlessTests` (2 — declared-policy + safe-no-surface); `CapabilityTests` exact-name list + `AssetManifestTests` copied-count tripwire bumped WITH reasons (3682→3700). One honest test-authoring correction recorded: a bare `%2e%2e/` path never reaches the server (System.Uri unescapes + dot-segment-removes client-side → 404 route refusal instead of 403); the 403 cases use encoded slashes.

**Gates:** build 0W/0E Debug + Release; `--verify-assets` PASS Debug AND Release (`asset OK copied: 3700 entries present, case-exact, sweep clean`).

**Timing guard:** no new deadline literals, no `Task.Delay`/`Thread.Sleep` in tests (the one `longPollTimeout: 200ms` in the shared-invariant fixture mirrors the landed DTRH fixture's own injectable — wait, see record note: this constructor parameter is the LANDED LoopbackServer's existing injectable seam (DtrhLoopbackContractTests:46 precedent), not a NEW budget added by this task; named here per framing (e) so the budgets row's sweep sees it.

## Step 3 — headed layering evidence

(pending)

## Intended board filings (orchestrator reconciles at land — ENABLER 2)

(pending — Step 4)
