# SP-023 record — DTRH host slice b1: shell, origins, transport, boot matrix

**Task:** spine-tasks/SP-023-dtrh-host-b1 · **Review Level:** 2 · **Binding spec:** `client/docs/dtrh-admission.md` (SP-022)
**Engine-review presence (T-2):** recorded per `spine_review_step` call below.

---

## Step 1 — FIRST GATE + archaeology + design + pre-approach consult

### FIRST GATE (literal first checkbox): `invokeCSharpAction` page→host on NativeWebDialog — **PROVEN**

The named risk (admission §3.3/§8): spike-proven only on the EMBEDDED GTK adapter (L5), never on the NativeWebDialog path.

**Method (minimal throwaway probe, not product code):** the existing quarantined spike `client/spikes/CcpSpike.WebView/` copied unmodified to WSL native dir `~/ccp-sp023-firstgate/spike` (ext4, never /mnt/e for the build), built 0W/0E, run under WSLg (`DISPLAY=:0 WAYLAND_DISPLAY=wayland-0`, X11 via XWayland) as:

```
dotnet run --no-build -- --dialog --page probe --payload <READ-ONLY repo dtrh tree> --auto-quit 25
```

Dialog mode = `NativeWebDialog` (WebKitGTK 4.1 dedicated toplevel — the admitted Linux shape, §5). `--page probe` serves the spike's tracked overlay `probe.html`, which calls `window.invokeCSharpAction(JSON.stringify({type:'probe-p2h-ica',...}))` at module eval. The dialog's `WebMessageReceived` is wired to the spike log.

**Transcript:** `evidence/first-gate-dialog-ica.log` (full log). Decisive lines:

```
20:18:39.169 webview: AdapterCreated info='DetailedWebViewAdapterInfo { Type = WebKitGtk, Engine = WebKit, Version = 2.52.3, IsSupported = True, IsInstalled = True, UnavailableReason = , SupportedScenarios = NativeDialog }'
20:18:39.434 webview(dialog): NavigationStarted t=1767ms
20:18:39.459 webview(dialog): NavigationCompleted t=1792ms success=True
20:18:39.552 loopback: GET /dtrh/bridge.js -> 200 1808B (payload:bridge.js)
20:18:39.561 page->host: {"type":"probe-p2h-ica","note":"invokeCSharpAction path"}
```

**Verdict: PROVEN.** `invokeCSharpAction` page→host raises `WebMessageReceived` on the NativeWebDialog path (WebKitGTK 2.52.3, adapter declares `SupportedScenarios = NativeDialog`). Linux page→host = `invokeCSharpAction(JSON.stringify(...))` per admission §3.2 stands; **no poll-both-ways fallback needed**. Also observed: NO `probe-p2h-native` message — `window.chrome.webview` absent on GTK, as L5 recorded; unchanged bridge.js logged no `ready` (isHosted=false), confirming the §3.1 diff is required.

### WPF DTRH host archaeology (READ-ONLY, via wpf-archaeologist subagent; File.cs:line)

Source files: `ConditioningControlPanel/Chaos/ChaosWebViewHost.cs` (367 lines), `ConditioningControlPanel/Services/Chaos/DtrhHostService.cs` (1051 lines), page side `Resources/web/dtrh/bridge.js` + `boot.js`.

- **Boot handshake:** page sends `{type:'ready', protocol:1}` (`bridge.js:47`, `PROTOCOL=1` at `bridge.js:11`) at end of `boot.js:207`, synchronously AFTER all `bridge.on(...)` handlers register (`boot.js:132-176`) — ready means "handlers registered, send init+manifest" (`boot.js:9-10`). Host intercepts `ready` → `IsReady=true` → `FlushPending()` → `OnReady` (`ChaosWebViewHost.cs:301-305`). **Host never sends before ready; early posts queue FIFO in `_pending` (`ChaosWebViewHost.cs:73,177-188`) and flush in order at ready (`:319-326`).** No NavigationCompleted gate anywhere in the WPF host.
- **Ready payload order** (`DtrhHostService.OnPageReady`, `:166-211`): (1) `init` `{type,protocol:1,settings:{masterVolume},modId,modContent,runSetup,m2Test}` (`:175-194`); (2) meta snapshot (b2/b4 scope); (3) `manifest` `{type,images:[{name,url}],videos:[{name,url}],skipped,truncated}` (`:196-204`); (4) loom-list + favorites (b4 scope). **b1 sends init + manifest only.**
- **"Engine live":** page log line `engine live (game mode)` (`boot.js:86`) after `scene.start()` resolves; host maps `{type:'log'}` to its logger (`ChaosWebViewHost.cs:306-310`). Real liveness = heartbeat.
- **Focus-claim at ready** (`DtrhHostService.cs:169-172`, comment: "Keyboard focus does not land in the WebView2 child on a fresh launch until a click - claim it now"): `FocusWeb()` = `Window.Activate()` + `WebView2.Focus()` (`ChaosWebViewHost.cs:191-198`).
- **Autoplay flag** (`DtrhHostService.cs:119-120`): `ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required"` (comment: "The game's audio bed / drift voice must start without a click"), passed via `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments` (`ChaosWebViewHost.cs:216-221`). Spike W10 proved the same flag via `WindowsWebView2EnvironmentRequestedEventArgs.AdditionalBrowserArguments`.
- **Origin serving:** WPF uses WebView2 `SetVirtualHostNameToFolderMapping` (`ccp.game` → Resources/web Deny-CORS; `ccp.assets` → user assets Allow; `ccp.art`, `ccp.spirals`, `ccp.mod`) — `DtrhHostService.cs:85-106`; start URL `https://ccp.game/dtrh/index.html` (`:108-109`); navigation locked to the primary host (`ChaosWebViewHost.cs:263-269`). bridge.js is a plain static file — no injection route. **The product substitutes the §4 two-loopback-origin contract (approved by decree; preserves the cross-origin split).**
- **init field list** (`BuildRunSetup`, `:483-510` — raw saved settings, not clamped): `difficulty` ("Easy"), `durationSec` (180), `waveCount` (5), `motion` ("Mixed"), `enabledVariants` (string[]|null), `effectIntensity` (0.85), `colorFlashes` (true), `boonDraftEnabled` (true), `allowCurses` (true), `dartersEnabled` (true), `key1` ("Q"), `key2` ("E"); volume under `settings.masterVolume` (`SafeMasterVolume`, `:1041-1045`); `modId` default `"builtin-sissyhypno"` (`:1047-1051`). run-config/request-run/meta/loom = b2…b4.
- **Exit:** page posts `{type:'exit'}` (ESC held 1.2s, `boot.js:180-190`), disposes engine, posts `{type:'exit-done'}` (`boot.js:126-130`); host disposes on `exit-done`, 1200ms watchdog backstop (`DtrhHostService.cs:312-318,879-884`). **b1: close on `exit`; bounded `exit-done` wait + watchdog recovery = b5.**
- **Heartbeat:** page posts `{type:'heartbeat', t}` every ~2s from rAF (`boot.js:178-183`); host watchdog (10s mid-run/20s hub silence) is **b5 scope**; b1 records beats only.
- **Payload packaging archaeology** (`ConditioningControlPanel.csproj:349-357`): `Resources\web\**\*` shipped as `<Content CopyToOutputDirectory=PreserveNewest ExcludeFromSingleFile=true>` — files **copied to output next to the binary**, served from disk. Justifies the SP-009 `copied` asset class (below).

### Host design (from the admission spec)

1. **Per-platform shell** (`client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml(.cs)`):
   - **Windows:** Avalonia `Window` hosting the embedded `NativeWebView` control (WebView2). app.manifest with `supportedOS` (admission §1 integration consequence — NativeControlHost crashes without it; spike-proven). `EnvironmentRequested`: `UserDataFolder` under the app data dir, `AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"` (archaeology + W10 parity).
   - **Linux:** `NativeWebDialog` (WebKitGTK 4.1 dedicated toplevel) — the only observed-rendering shape (L7; embedded never presents on WSLg/X11, L4, adapter declares NativeDialog-only). The host window shows an honest state surface; the dialog carries the game.
   - **No classic fallback** (§5): where neither engine is available → typed unsupported surface, never an external browser.
2. **Capability honesty (SP-006):** two capabilities registered at composition, probed as owned operations in the existing `CapabilityProbes` phase:
   - `dtrh-webview-embedded` — Windows: exercised-backend probe = real P/Invoke `GetAvailableCoreWebView2BrowserVersionString` on the loaded WebView2 loader (the exact dependency the embedded path needs; reports the runtime version in detail). Linux: `Unavailable` with the adapter evidence (`SupportedScenarios = NativeDialog` — embedded never presents on X11/WSLg, L4). Rendering itself is claimed ONLY by the headed boot matrix, never by the probe.
   - `dtrh-web-dialog` — Linux: exercised-backend probe = dlopen of `libwebkit2gtk-4.1.so.0` + `libgtk-3.so.0` (the §2-pinned system packages the dialog path dlopens at runtime). Windows: not the admitted shape — `Unavailable` (embedded is the admitted Windows path, §5).
   - Fallback policy per SP-006 §7: two capabilities, each with its own honest state; the shell selects surface from states; no silent substitution.
3. **Payload serving class (1536-file packaging decision): `copied`.** Rationale: (a) WPF archaeology ships the identical tree as Content-copied-to-output, `ExcludeFromSingleFile` (csproj:349-357) — same class; (b) §4 requires Range-streamable real files (video seek, 206/416) — embedded avares:// streams would work mechanically but a 383MB assembly is absurd and the row-9 publish hook is single-file-shaped; (c) SP-009's schema reserves `copied` for exactly this ("convention defined by the first copied consumer") — b1 IS the first copied consumer. Convention: output-relative `payload/dtrh/<tree-relative path>`, populated by a **linked MSBuild Content glob** from the READ-ONLY repo tree (`ConditioningControlPanel/Resources/web/dtrh/**`, `CopyToOutputDirectory=PreserveNewest`) — the trust-anchored tree stays the single source of truth (no 383MB repo duplication; bytes come only from tree `40be29df`). The manifest gains **1536 generated `copied` entries** (id `dtrh.payload/<tree-relative path>`, case-exact path, `required: true`, trust `full`, overridePolicy `none`) — per-file entries, not one directory entry, because per-file case-exactness + completeness sweep is the SP-009 drift protection that actually catches ext4 case drift. Generation method recorded in evidence; the assert-empty copied direction of SP-009's tests is extended (the documented extension point) to real file-existence + a copied completeness sweep over the output `payload/` dir.
4. **bridge.js product derivative + provenance:** product file `Features/Dtrh/overlay/bridge.js` = original blob `13af3f4d` bytes + the §3.1 transport diff ONLY: (a) import-time `isHosted = !!(window.chrome && window.chrome.webview) || typeof invokeCSharpAction === 'function'`; (b) `send()`: chrome.webview → native `postMessage(obj)`; else `invokeCSharpAction(JSON.stringify(msg))` (page side owns stringify; host owns parse); (c) receive: chrome.webview listener unchanged (Windows synthetic dispatch, W4/W6); else a long-poll loop on `GET {location.origin}/bridge/<token>/inbox?after=N` feeding the SAME dispatch+preBuffer path (token read from `location` query). Served overlay-first: exactly ONE payload shadow (`/dtrh/bridge.js`) — the admitted single change; every other overlay path is NEW. Provenance: original blob hash + the diff both recorded (this record + a unit shape-test that diffs derivative vs the copied original and asserts changed hunks match the transport-only allowlist).
5. **Loopback server + inbox (product port of the §4/§3.3 contract):** `Features/Dtrh/LoopbackServer.cs` + `Inbox.cs` — two GET-only HttpListener origins on 127.0.0.1, ephemeral-port retry loop (spike shape); page origin: `/health`, `/dtrh/*` overlay-first over output payload, `/bridge/<token>/inbox?after=N` long-poll (seq-numbered retained delivery, ack by `after`, ~25s bounded hang, JSON-only, 404 outside token route); media origin: `/media/*` with CORS scoped to page origin, `Access-Control-Expose-Headers: Content-Range`, OPTIONS preflight 204 `allow-headers: range`. MIME allowlist pinned to the §4.4 extension sweep (9 extensions: css/gif/html/js/json/mp3/png/webm/webp), unknown → **415 deny-by-default**; `X-Content-Type-Options: nosniff` everywhere; traversal refusal 403; CORS-on-errors on the media origin; sensitive-logging ban (route classes only; the token NEVER logged).
6. **Composition + lifecycle (SP-003/SP-004):** `DtrhParticipant : IBackgroundParticipant` owns server+inbox+token (construction starts nothing; StartAsync binds origins; StopAsync idempotent teardown, generation-cancelled). Window opens via a bounded `--dtrh-demo` flag (same demonstrator pattern as `--avatartube-demo`; WSLg has no input automation, SP-008) — b1 has no product UI surface for DTRH; the flag IS the boot-matrix harness entry. Boot: navigate `{pageOrigin}/dtrh/index.html?bridge=<token>`; on `ready` → claim focus (window activate + web focus) → send init+manifest (archaeology shapes, spike-proven); host→page Windows = synthetic MessageEvent dispatch byte-identical to W4; Linux = inbox enqueue (retained = pre-ready queue + replay-equivalent per §3.2). Page→host both platforms via `WebMessageReceived` (FIRST GATE proven). On `exit` → close window, exit 0.

### Consults

(recorded below with actual answering models)

#### Pre-approach consult (Step 1)

**Mode:** solo (council route broken — T-7, never used per owner instruction). **Actual answering model:** NOT surfaced by the consult tool response (requested Fable 5 per packet; tool returned text without a model identity header — recorded honestly, same truncation/provenance discipline as SP-022).

**Questions:** (1) payload class copied + linked-glob vs repo duplication; (2) capability probe honesty (P/Invoke WebView2 loader / dlopen WebKitGTK); (3) `--dtrh-demo` flag as b1 shell entry; (4) inbox single-waiter long-poll + token-in-URL-query.

**Verdict (folded into the design):**
1. **Copied + linked-glob is the right call** — repo duplication rejected; the WSL gate must copy the payload tree preserving the repo-relative layout (the glob reaches outside `client/`); watch MSBuild-special characters in filenames. **Checked: zero `% # ^ &` in all 1536 filenames** (`SP-022 evidence/dtrh-tree-files.txt`).
2. **Probe mechanism = Step-2 research outcome, not a guess.** P/Invoke on `WebView2Loader.dll` may FALSE-NEGATIVE if Avalonia's package loads WebView2 differently (bundled loader / registry / embedded client dll). Prefer the package's OWN adapter-info surface (`DetailedWebViewAdapterInfo { IsSupported, IsInstalled, SupportedScenarios }` — observed in the spike log) if reachable without a window; else probe the actual native dependency the package loads. Detail strings must state exactly what was confirmed ("runtime present; rendering unclaimed" — rendering is claimed only by the headed matrix). `Unavailable` from platform evidence is honest; only `Available` requires the exercised backend.
3. **`--dtrh-demo` flag acceptable** (established demonstrator pattern; routed through Program.cs's bounded pre-phase shape).
4. **Inbox corrections applied:** (a) **request logs must strip query strings** — the token rides in the navigated URL's `?bridge=<token>` query, so raw-path logging would leak it (route-class logging only, token never logged — now explicitly includes query stripping); (b) page reload semantics: retained unacked messages re-deliver to the fresh page (replay-equivalent; duplicate init tolerance is the boot handler's, re-init robustness named for later slices); (c) bounded hang must be **injectable** (tests use short timeouts); (d) teardown must complete hanging waiters and tolerate aborted contexts.

---

## Engine-review log (T-2)

PENDING — no `spine_review_step` calls yet.
