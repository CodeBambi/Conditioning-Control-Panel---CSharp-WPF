# Browser Video Engine (hybrid) — Implementation Plan

Branch: `feat/browser-video-engine` (worktree `C:\Projects\ccp-wt-browservideo`).
Owner decision (2026-08-04): **hybrid** — browser-first playback, existing LibVLC path
stays as fallback for files the browser cannot decode. No caller-facing API changes.

## 1. Why

Roughly 2,000 lines of `Services/Video/VideoService.cs` exist solely to survive LibVLC:
native quarantine (#559), retire circuit breaker (#557-#560/#574), 3-rung wedge ladder
(#529/#532/#765/#766/#767), vout self-heal (#600), poison cooldown (#766), `VideoDiag`.
Procdump (v6.6.2, #750-#753) proved a native use-after-free inside LibVLC's event
manager. LibVLC renders in-process and can wedge the WPF dispatcher; WebView2 media is
out-of-process — a dead renderer is a `ProcessFailed` event, not a frozen PC.

## 2. Architecture

```
VideoService (unchanged public contract, stays the director:
  scheduling, selection, XP, troll replay, mercy, events, ducking, safety timers)
    └─ after file selection, route:
         BrowserVideoGate.ShouldUseBrowser(path)  ── yes ──> BrowserVideoEngine
                                                └── no ───> existing LibVLC path (untouched)
```

New files (all under `Services/Video/Browser/`):
- **`BrowserVideoEngine.cs`** — process-wide singleton owned by VideoService (or App).
  Creates ONE shared `CoreWebView2Environment` (user-data folder
  `%LOCALAPPDATA%\ConditioningControlPanel\browser_data_video`), owns the per-monitor
  window set for the active session, exposes `StartSession(BrowserVideoRequest)` /
  `StopSession()` and events back to VideoService. If environment creation throws
  (WebView2 runtime missing), mark `IsAvailable=false` for the whole session and log
  one clear Warning — every video then routes to LibVLC. Never throw to callers.
- **`BrowserVideoWindow`** (WPF window class) — one per monitor, borderless, sized to
  full **physical** bounds of its `Screen` (mirror `ForceFullScreenBounds` logic in
  VideoService for mixed-DPI; use `BubbleCountWindow.GetDpiForScreen`). Topmost.
  `AllowsTransparency = false` — HARD RULE, WebView2 never paints in layered windows.
  NOTHING may ever call `SetLayeredWindowAttributes` on these windows.
- **`BrowserVideoGate.cs`** — `ShouldUseBrowser(path)`:
  `App.Settings.Current.BrowserVideoEngineEnabled && engine.IsAvailable
   && ext ∈ {.mp4, .m4v, .webm} && !UnsafeCache.Contains(path)`.
- **`BrowserUnsafeVideoCache.cs`** — persisted JSON (`App.UserDataPath\browser_unsafe_videos.json`),
  key = full path + file size. A file lands here when the page reports `error` for it
  or never reports `playing` within 10 s; from then on it routes to LibVLC forever.
  Cap entries (say 2,000), save debounced, load lazily. Corrupt file ⇒ empty cache.

Web page: **`Resources/web/player/index.html` + `player.js` + `player.css`**.
Served as `https://ccp.game/player/index.html`. Copy the decoder discipline from
`Resources/web/fyp/surfaces.js` (do NOT import fyp files; the player is standalone).

### Host windows / WebView2 setup
Reuse the `ChaosWebViewHost` *patterns*, not necessarily the class (it is hardcoded to
the primary screen and carries FYP-specific z-order glue). If extending it, the change
must be purely additive (optional explicit-bounds option); forking a lean host window
is acceptable and probably simpler. Requirements per window:
- Browser args: `--autoplay-policy=no-user-gesture-required
  --disable-direct-composition-video-overlays
  --disable-features=CalculateNativeWinOcclusion
  --disable-backgrounding-occluded-windows`
  (MPO black-screen #449/#439; occlusion throttling freezes covered/parked pages).
- Virtual host mappings (create directories BEFORE mapping — a missing folder makes
  WebView2 silently skip the mapping, see `FypHostService.cs:65-68`):
  - `ccp.game`  → `AppContext.BaseDirectory\Resources\web` (`Deny`)
  - `ccp.assets` → `App.EffectiveAssetsPath` (`Allow`)
  - `ccp.packs` → the content-pack temp-decrypt directory (`Allow`) — pack videos are
    AES-decrypted to a temp path per play (`GetPackFileTempPath`); the page can only
    reach them if that directory is mapped. Resolve the actual temp root from the
    existing pack code; do not hardcode.
- URL building: percent-escape per segment exactly like `FypAssetManifest.cs:112`.
- `NavigationStarting` lockdown to `https://ccp.game/`.
- `CoreWebView2.ProcessFailed` ⇒ treat as playback error: end session, fall back
  (§4). Also guards the "zombie control" state (`BrowserService.cs:232-238`).
- `EnsureCoreWebView2Async` only after the control is in the visual tree.
- Multi-monitor: one window per `Screen`, honouring `ShouldFillSecondaryMonitors`
  (high monitor-count cap, #389). Primary window carries audio; secondaries load with
  `muted: true`. Windows share the single environment.
- Input: window-level `PreviewMouseDown` is unreliable over a WebView2 child HWND —
  the page posts `{type:'click'}` instead (§3) and C# calls `BringTargetsToFront()`.

## 3. C# ⇄ page protocol (JSON via PostWebMessageAsJson / WebMessageReceived)

Queue outbound messages until the page posts `ready` (ChaosWebViewHost pattern).

C# → page:
| msg | fields | notes |
|---|---|---|
| `load` | `url, volume (0-1), muted, blurBackground (bool), hideCursor (bool), startAtMs?` | begins playback immediately. `hideCursor` defaults to **false** — the LibVLC video windows do not hide the pointer either, and BubbleCount is mouse-driven |
| `pause` / `resume` | — | grace pause / DtRH / voice commands |
| `stop` | — | decoder hygiene: `pause()` → `removeAttribute('src')` → `load()` |
| `setVolume` | `volume`, `muted?` | master × video volume; the optional `muted` folds external mute in. Secondaries are always sent `volume: 0, muted: true` |
| `seek` | `ms` | future Deeper use; implement anyway (trivial) |
| `attentionShow` | `id, text, size, font, color1, color2, textColor, borderColor, floating, showBorder, xPct, yPct, vx, vy, reportMotion` | one attention-check target. C# owns the text (mod trigger pool + localization stay C#-side, already line-split) and the whole style; `xPct/yPct` are 0-1 *of the free spawn range* — only the page knows how big the element measured — and `vx/vy` are CSS px per second. `reportMotion` asks for `attentionMove` and is only set when gaze-click is on |
| `attentionHide` | `id, fade` | `fade:true` = the user got it (300ms opacity fade, then the node is reaped); `false` = timeout / teardown, gone now |

page → C#:
| msg | fields | notes |
|---|---|---|
| `ready` | — | host built-in |
| `meta` | `durationMs, width, height` | on `loadedmetadata`; feeds safety timer + `VideoMetadataCache` backfill |
| `playing` | — | first real `playing` event; arms the C#-side "started" state |
| `time` | `ms` | ~10 Hz from `timeupdate`; drives `PrimaryPlaybackTimeMsChanged` |
| `ended` | — | natural end |
| `error` | `code, message` | media error OR unrequested stall the page can't recover |
| `click` | — | any pointer-down on the surface that was NOT on an attention target |
| `attentionClick` | `id` | the user pressed a target. Reported INSTEAD of `click` (a hit must not also trigger the host's z-order lift). The target then waits for `attentionHide` — C# stays the only authority on which checks are outstanding |
| `attentionMove` | `id, xPct, yPct, wPct, hPct` | ~10 Hz viewport-fraction rectangle of a bouncing target, only when `reportMotion` was set. Gaze hit-testing is the sole consumer; C# maps it back to DIPs against the window |
| `key` | `key, alt, ctrl, shift` | every non-repeat keydown the page saw (DOM `event.key`). Keyboard over a focused WebView2 goes to Chromium, so this is the ONLY route ESC / panic keys have back to C#; the page `preventDefault`s the dangerous ones and C# owns the policy |
| `log` | `msg` | host built-in page logging |

Page behaviour (from `surfaces.js` discipline):
- `<video>` born muted then volume applied via message (autoplay flag makes `play()`
  legal anyway, but belt-and-suspenders).
- `playsInline`, `disablePictureInPicture`, `preload='auto'`, no controls,
  context-menu suppressed, text selection suppressed. Cursor hiding is opt-in per load
  (`hideCursor`), NOT unconditional — see the `load` row above.
- Anti-pause: an unrequested `pause` while a session is live ⇒ resume once, then
  report `error` if it re-pauses (don't fight forever).
- Blur background: when `blurBackground`, render TWO elements from the same src —
  a background `<video>` with `object-fit: cover; filter: blur(40px) brightness(.7);
  transform: scale(1.1)` (blur edge bleed) and the foreground `object-fit: contain`.
  Background is always `muted`. When off: black background, contain only.
  This replaces the entire `BlurVmemSurface` vmem path for browser sessions.
- Media `error` event or `stalled`>10s ⇒ post `error`, never throw.

## 4. Hybrid routing + runtime fallback

1. Selection (existing off-thread logic, #732) produces a path.
2. `ShouldUseBrowser(path)`? → browser session; else LibVLC path exactly as today.
3. Runtime failure fallback (once per video): if the page posts `error`, or posts no
   `playing` within **10 s** of `load`, or `ProcessFailed` fires:
   - mark path in `BrowserUnsafeVideoCache` (only for media errors / playing-timeout,
     NOT for `ProcessFailed` — that's an environment failure, not the file's fault),
   - tear down the browser session **without** raising VideoEnded,
   - replay the SAME file through the LibVLC path (guard flag so this happens at most
     once per trigger; if LibVLC then also fails, normal existing error flow applies).
4. `ProcessFailed` twice in one app session ⇒ `IsAvailable=false` for the rest of the
   session (stop flapping).

## 5. VideoService integration — parity checklist

The browser session must be indistinguishable from a LibVLC session to every consumer.
All of the following stay in VideoService (C#-side) and must fire for browser sessions:

- [ ] `VideoAboutToStart` / `VideoStarted` / `VideoEnded` events; `LastVideoPath`,
      `LastVideoTitle`, `IsPlaying`, `_videoPlaying` flag lifecycle.
- [ ] Ducking: `App.Audio.Duck(level)` at start with `_didDuck`; exactly one `Unduck`
      in the teardown path (`CloseAll` finally parity, #526). NOTE: WebView2 PIDs are
      excluded from the duck sweep by default (`ExcludeBambiCloudFromDucking`), which
      is what we want — the video's own audio survives the duck.
- [ ] Safety timers (all C#-side, unchanged mechanisms): duration guillotine armed
      from the page `meta` message (was `LengthChanged`); 10-min fallback timer;
      `VideoMaxDurationSeconds` hard cap; `_enhancementDriving` stall-watch semantics.
      The stall watch must read the engine-agnostic `GetCurrentPlaybackTimeMs()` and
      must treat an **unknown** clock as no-progress, not as progress — otherwise the
      force-close is unreachable for a browser session and a hung renderer (no
      `ProcessFailed`, page watchdogs dead with it) holds the screen forever, because
      arming the duration guillotine nulls the 10-min fallback timer (#874).
- [ ] Attention checks: `SetupAttention`, the spawn schedule, the pass/fail tally, gaze
      (`GetGazeTargets`/`GazeClick`), the toy-button alternative and `EndCurrentVideo`'s
      pass/fail/XP/troll-replay/mercy logic are all unchanged. What changed is only the RENDERING:
      `_targets` holds `IAttentionTarget`, and each spawn picks a representation PER SCREEN - a DOM
      element in the player page (browser session), a WPF element inside the video window (LibVLC
      vmem/blur path), or the original `FloatingText` topmost window (VideoView airspace, the
      MediaElement fallback, or a monitor with no video window at all). The page `click` message
      still drives `BringTargetsToFront()` for the windows that are still separate.
- [ ] Strict mode: `Closing` veto + panic/Alt-F4/system-key swallowing on
      `BrowserVideoWindow` (mirror `SetupStrictHandlers`); non-strict ESC ⇒ Cleanup.
      NOTE: keyboard events over a focused WebView2 go to Chromium — also suppress
      in-page (`keydown` preventDefault for Escape/F4/system keys) and make the
      windows non-activating like the LibVLC ones (`MakeNonActivating`) so the WPF
      handlers keep working app-side.
- [ ] Grace pause (#735): `TryGracePauseFromPanic` sends `pause`/`resume`;
      `GracePauseOverlayWindow` reuse unchanged; TimerArm capture/re-arm unchanged;
      60 s auto-resume.
- [ ] `SetExternalMute` (DtRH dive) and volume updates ⇒ `setVolume` messages via
      `GetEffectiveVolume()`.
- [ ] `PausePrimary`/`PlayPrimary`/`SeekPrimary` map to messages (voice commands,
      DtRH); `GetCurrentPlaybackTimeMs` + `PrimaryPlaybackTimeMsChanged` fed from
      `time` messages (FunScript haptics + Deeper time source keep working).
- [x] `PrimaryMediaPlayer` stays **null** during browser sessions, but Deeper's
      enhancement bridge attaches anyway (#874): `VideoServiceTimeSource` reads the
      engine-agnostic surface (`GetPrimaryDurationSeconds`, `IsPrimaryMediaPlaying`,
      `BrowserVideoAspect` from the page `meta` size), and the bridge's guard only
      refuses when NEITHER engine has a clock (the MediaElement fallback window).
      The original Stage-1 gap — bridge silently refusing every mp4/m4v/webm while
      the browser engine was default — shipped 6.7.0→6.7.4 and is closed.
- [ ] `SessionSwitch` (lock) + `PowerModeChanged` (suspend) force-clean unchanged.
- [ ] Teardown generation guard applies to browser session continuations too.
- [ ] `VideoMetadataCache` backfill: write duration from the page `meta` message so
      the duration filter improves over time without LibVLC parses.
- [ ] Wedge/vout/quarantine/poison machinery: **not armed** for browser sessions
      (nothing native to wedge). `NativePoisonCooldownRemainingMs` must NOT gate
      browser playback.
- [ ] FYP exclusivity gates (`FypHostService` active ⇒ video features off) unchanged.

## 6. Stage 2 — BubbleCount

`BubbleCountService` (scheduling/XP/mercy) untouched. `BubbleCountWindow` gets a
browser-mode surface: when `BrowserVideoGate.ShouldUseBrowser(path)`, host a WebView2
playing the same player page instead of leasing a LibVLC player. Bubble overlay
windows, count logic, result window, duration resolution (`meta` message replaces
`AdoptRealDuration`'s `LengthChanged`) unchanged. Poison-cooldown skip
(`BubbleCountService.cs:132/:334`, `BubbleCountWindow:233`) only applies when the
session would use LibVLC. Managed wedge watchdog not armed in browser mode.
Simplest implementation: reuse `BrowserVideoEngine`'s environment + a
`BrowserVideoWindow` variant (or the same class with an options struct).

## 7. Settings + localization

- `AppSettings.BrowserVideoEngineEnabled` — bool, `[JsonProperty]`, default **false**
  (beta opt-in). Toggle UI next to the existing video settings (near the blurred-
  background toggle), labelled as beta.
- Loc keys in ALL 9 `Localization/Languages/*.json`. STRICT JSON: escaped `\n` only,
  never literal newlines; don't touch line endings (files are LF in git, CRLF in
  worktree via autocrlf — normal).

## 8. Traps (read before coding)

1. WebView2 + `AllowsTransparency=true` ⇒ nothing paints. `LWA_ALPHA` ⇒ solid black.
2. MPO: without `--disable-direct-composition-video-overlays` some GPUs scan out
   black video / video above topmost overlays.
3. Occlusion: without the two occlusion flags Chromium stops rendering covered
   windows.
4. Missing mapped folder ⇒ mapping silently skipped. `Directory.CreateDirectory` first.
5. Never block inside `WebMessageReceived` (`await Task.Yield()` before anything
   modal) — re-enters the browser message loop.
6. `document.exitFullscreen` unreliable — we never use HTML fullscreen; windows are
   OS-level fullscreen borderless. Don't call `requestFullscreen` in the page.
7. Duplicate WPF resource keys crash at launch and tests don't catch it.
8. `--no-build` runs a stale dll; always full `dotnet build`.
9. `DispatcherPriority.Loaded` is starved in this app — use `Normal`.
10. Don't put converters in local Grid.Resources (DataTemplate lookup fails).
11. LibVLC path stays byte-for-byte untouched except the routing branch — the
    fallback must keep working exactly as shipped.
12. The FYP ghost/DWM-mirror machinery is NOT needed here — mandatory video windows
    are opaque fullscreen. Do not touch `FypGhostOverlay`.

## 9. Later stages (not this branch)

- `setSinkId` per-element audio-device routing (parity with `ApplyPreferredDevice`).
- Duck-exclusion granularity per user-data-folder/PID.
- Optional ffmpeg transcode tool for non-browser-safe libraries.
- ~~DOM-based attention targets~~ — done on this branch (§3 `attentionShow`/`attentionHide`),
  alongside a WPF in-window target for the LibVLC vmem path. `FloatingText` cannot be deleted
  yet: it is still the only representation that works over a `VideoView`'s airspace, over the
  MediaElement fallback, and on a monitor that has no video window. It goes when the last
  `VideoView` does.
- Migrate small LibVLC consumers (mini-player, help video, gaze minigame, editor
  preview, inline loop); then delete the watchdog museum + `BlurVmemSurface`.
