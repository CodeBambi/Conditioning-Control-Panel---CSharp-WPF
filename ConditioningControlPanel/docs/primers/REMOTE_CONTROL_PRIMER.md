# REMOTE CONTROL — Feature Primer

> **Load this instead of re-exploring the feature.** One-load orientation for **Remote Control** — the
> premium feature that lets another person ("the Controller") drive a subject's app from a phone/web
> page while the subject watches. @-mention this file for coding or design sessions. §0 = what it is in
> one paragraph. §1 = architecture (the client↔proxy↔controller triangle). §2 = the service internals
> (session lifecycle + the poll loop). §3 = the command model / wire protocol. §4 = **how it's invoked
> & how it touches the rest of the app** (the dispatch table + every subsystem it drives — read this
> before adding a command). §5 = render/side-effects (overlay, tray, avatar). §6 = settings & gating
> (premium is UI-gated; tiers are server-enforced only — read §6/§8). §7 = the Available Subjects
> directory sibling. §8 = file map. §9 = where-to-change-X. §10 = gotchas. §11 = dated status.
>
> **Freshness. Verified against source 2026-07-23** (branch `fix/web-video-interruptions`, HEAD
> `95586020`, v6.5.0). Every `file:line` below was **read-verified** when written and is
> git-verifiable — but line numbers drift, so confirm with a quick read before quoting. **§11 is a
> dated snapshot — verify with `git log`/`git branch` before acting on it.**

---

## 0. What Remote Control is, in one paragraph

Remote Control turns the desktop app into a **remotely-drivable subject**. The subject enables it,
picks a tier (light / standard / full), accepts a double-consent waiver, and the app opens a **session
on a cloud proxy** (`https://codebambi-proxy.vercel.app`, the CCP-Server proxy) identified by a short
**session code** plus a client-generated **4-digit PIN**. A Controller opens the pairing page
(`https://cclabs.app/remote/#code=…&pin=…`, encoded in a QR code) on any device, connects, and issues
**commands** — trigger a flash, start a video, show the spiral, start a session, disable the panic key,
etc. There is **no push channel**: the desktop **polls** `POST /v2/remote/poll` every 5 s, executes any
queued commands on the UI thread through one big `switch`, and pushes its own state back via
`POST /v2/remote/status`. The whole client lives in **one service**, `RemoteControlService`
(`App.RemoteControl`), plus a large MainWindow partial (`MainWindow.RemoteControl.cs`) for the tab UI,
pairing/QR, overlays, and session bridging. Commands fan out into essentially every other
service (Flash, Video, Subliminal, Bubbles, Overlay, MindWipe, LockCard, Autonomy, Haptics,
SessionEngine, …). It is **Patreon-premium-gated** (`HasPremiumAccess`) — but see §6: that gate is a
UI overlay, and the light/standard/full tiers are **not** enforced on the client at all.

---

## 1. Architecture — the client ↔ proxy ↔ controller triangle

There is no direct peer connection. Three parties talk through the proxy:

```
 SUBJECT (this app)                PROXY (codebambi-proxy.vercel.app)         CONTROLLER (cclabs.app/remote/)
 RemoteControlService  ── start ──▶  /v2/remote/start   (mint code)  ◀── pair (code+PIN) ── QR / link
        │  poll 5s  ── poll ──────▶  /v2/remote/poll    (drain queue) ◀── enqueue command ─ web UI
        │  push 15s ── status ─────▶ /v2/remote/status  (subject state)─── read ──────────▶ pinned strip
        └  emote    ── emote ──────▶ /v2/remote/emote   (subject→controller chat)
```

- **Client**: `Services/RemoteControlService.cs` (`RemoteControlService`, class at `:30`; ~1,310 lines)
  owns the HTTP client, the poll `DispatcherTimer`, session state, and the command dispatcher.
- **Static accessor**: `App.RemoteControl` — declared `App.xaml.cs:366`, constructed `App.xaml.cs:1520`,
  `MainWindowRef` injected `App.xaml.cs:1700`, disposed first on shutdown `App.xaml.cs:3233`.
- **UI / bridge**: `MainWindow.RemoteControl.cs` (~1,493 lines) — the Remote Control tab, tier cards,
  consent waiver, QR/pairing, the "someone is controlling you" overlay, tray notifications, emote
  picker, directory opt-in, and the `*FromRemote` session bridge methods the service calls back into.
- **Controller surface**: a **web page** at `cclabs.app/remote/` (not in this repo; served from the
  `cclabs-site` project). A native mobile controller (`CodeBambi/CCP-Mobile`, Expo) also exists per
  auto-memory. The pairing URL is built at `MainWindow.RemoteControl.cs:1153`.
- **Server**: endpoints live in the private `CC-Labs-llc/CCP-Server` repo (proxied via
  `codebambi-proxy.vercel.app`). This primer covers only the **client** contract.

---

## 2. The service — internals

### 2a. Session lifecycle (`StartSessionAsync` → poll → `StopSessionAsync`/`CleanupSession`)
- **`StartSessionAsync(tier)`** (`:112`): requires `App.UnifiedUserId` (the cloud identity from Patreon
  or Discord login) — bails if empty (`:115`). Generates a random **4-digit PIN** (`:124`), POSTs
  `{unified_id, tier, connect_pin}` to `/v2/remote/start` (`:127`), stores the server-returned `code`
  as `SessionCode` (`:137`), sets `IsActive`, resets all counters, and starts a 5 s poll
  `DispatcherTimer` (`:151`). Raises `SessionStarted` (`:159`). Returns the session code (or `null` on
  failure).
- **`StopSessionAsync`** (`:253`): POSTs `/v2/remote/stop` (best-effort) then `CleanupSession`.
- **`CleanupSession`** (`:275`): stops the timer, resets all flags, **runs `StopAllRemoteEffects` on
  the UI thread** (`:296`), clears `App.Overlay.BypassLevelCheck`, fires `ControllerConnectedChanged`
  (if it was connected) + `SessionEnded`.
- **`Dispose`** (`:303`): stops the timer, disposes the `HttpClient`. Called first in `App` shutdown so
  no new effects queue during teardown.

### 2b. The poll loop (`PollForCommandsAsync`, `:341`)
The heartbeat. Guards against re-entrance (`_pollInProgress`, `:344`), POSTs `{unified_id}` to
`/v2/remote/poll` (`:354`), and:
- **Failure handling** (`:356`): `404` → session expired → `CleanupSession`. `429` → **exponential
  backoff** (`_currentPollInterval ×2` up to 60 s, `:369`) and logs the server's `cap`/`count` from
  `Read429CapAsync` (`:97`). `401` → after **3 consecutive** auth failures, terminate the session
  (`:379`). Otherwise increment `_consecutivePollFailures` and log.
- **Recovery** (`:400`): on the first success after a backoff, restores the 5 s interval.
- **Controller connection** (`:435`): reads `controller_connected` / `controller_idle` from the
  response. On the **first** controller connect of the session, if the local engine is running it
  **stops the engine** (`_engineStoppedForController` guard prevents a takeover re-stop, `:472`) and
  ensures the overlay service is running (`EnsureOverlayRunning`, `:479`). On disconnect it runs
  `HandleControllerDisconnectCleanup` (`:486`) and re-publishes the directory entry.
- **Idle auto-disconnect** (`:506`): after `IdleAutoDisconnectSeconds` = **120 s** idle (`:322`), the
  client force-treats the controller as disconnected.
- **Command execution** (`:534`): each element of the `commands` JArray → `ExecuteCommand(action,
  params)` (`:543`) then `CommandReceived?.Invoke(this, action)` (`:544`).
- **Status push** (`:555`): throttled — pushed immediately on a command or a connection change,
  otherwise every `StatusPushIntervalSeconds` = **15 s** (`:38`).

### 2c. Status push (`SendStatusAsync`, `:579`)
POSTs `{unified_id, active_services, level, last_executed, available_sessions, session_info,
share_avatar}` to `/v2/remote/status` (`:621`). `active_services` comes from `GetActiveServices`
(`:747`) — a snapshot of which subsystems are live. On `429` it sets a **60 s status backoff**
(`StatusBackoffSeconds`, `:40`/`:627`). `PushStatusNowAsync` (`:658`) is the public "push now" wrapper
(used when the user toggles `RemoteShareAvatar`).

### 2d. Emote channel (`SendEmoteAsync`, `:681`)
The **subject→controller** back-channel: POSTs `{unified_id, text, icon, kind}` to `/v2/remote/emote`
(`:714`). Client-side validation mirrors the server (`text` ≤ 60 chars, `icon` ≤ 8, `kind` ∈
{`preset`,`custom`}) plus a **300 ms debounce** (`EmoteDebounceMs`, `:329`) to swallow double-clicks.
Returns `(ok, error, retryAfterSeconds)`.

---

## 3. The command model / wire protocol

- **Transport**: plain HTTPS **long-poll**, not WebSocket. 5 s cadence; the server holds a per-user
  command queue that the poll drains.
- **Auth**: every request goes through `AuthPostAsync` (`:82`), which attaches the
  **`X-Auth-Token`** header from `App.Settings.Current.AuthToken` (`:88`) and identifies the user by
  `unified_id` in the JSON body. Also sends `X-Client-Version` + a `User-Agent` (`:74`).
- **Command shape**: each command in the poll response is `{ "action": string, "id": string,
  "params": { … } }`. `id` is echoed back in the next status push as `last_executed` (`:592`).
- **Rate limits** (client-aware constants): poll 5 s ≈ 12/min, status 15 s — both sit under the
  server's documented **40/min per-user** cap (`:33`). 429s trigger backoff (poll) or suppression
  (status).
- **Pairing**: session `code` (server-minted) + client `PIN`. The pairing URL puts the PIN in the
  **hash fragment** (`#code=…&pin=…`, `:1157`) so it never lands in server access logs or Referer
  headers; the web page strips it after parsing.
- **The action set** is the `switch` in `ExecuteCommand` (`:967`) — see §4b for the full table.

---

## 4. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

**This is the load-bearing section.** Remote Control is almost pure fan-out: it receives an `action`
string over the network and calls into another service. There is no per-command state of its own.

### 4a. How commands arrive and dispatch
1. The 5 s poll (`PollForCommandsAsync`, `:341`) drains the server queue.
2. For each command, `ExecuteCommand(action, params)` (`:967`) runs the body on the UI thread via
   `DispatcherHelper.RunOnUISync` (`:969`) inside one try-catch, then `CommandReceived` fires (`:544`).
3. Two subscribers on `CommandReceived`:
   - **Quest credit**: `App.xaml.cs:1522` → `Quests?.TrackRemoteCommand()` (`QuestService.cs:728` →
     `QuestCategory.Remote`). This is a **Patreon-exclusive quest category** — Remote quests carry
     `RequiresPremium = true` (`Quest.cs:216-247`, e.g. "Hand Over Control", "Puppet Strings").
   - **UI log/notification**: `MainWindow.RemoteControl.cs:1103` (`OnRemoteCommandReceived`) → a
     toast (`ShowCommandNotification`) + the tab command log, unless the action is in
     `SuppressedCommands` (`MainWindow.xaml.cs:145`: the high-frequency flash/subliminal/opacity/duck
     verbs are logged silently to avoid toast spam).

### 4b. The dispatch table (`ExecuteCommand`, `:967`) — every command and where it lands

| Action | Lands in | Notes |
|---|---|---|
| `trigger_flash` | `App.Flash.TriggerFlashOnce()` (`:977`) | |
| `start_flash` / `stop_flash` | sets `FlashEnabled`, `App.Flash.Start()`/`.Stop()` (`:984`/`:990`) | |
| `trigger_subliminal` | `App.Subliminal.FlashSubliminal()` (`:981`) | |
| `start_subliminal` / `stop_subliminal` | `App.Subliminal.Start()`/`.Stop()` (`:994`/`:1000`) | |
| `trigger_custom_subliminal` | `App.Subliminal.FlashSubliminalCustom(text)` (`:1004`) | **Controller-supplied text** (service caps at 200 chars, strips tags). |
| `show_pink_filter` / `stop_pink_filter` | `Settings.PinkFilterEnabled` + `EnablePinkFilter` + overlay refresh (`:1010`/`:1021`) | |
| `show_spiral` / `stop_spiral` | `Settings.SpiralEnabled` + `EnableSpiral` (`:1032`/`:1043`) | |
| `set_pink_opacity` / `set_spiral_opacity` | clamp 0–50 → settings (`:1054`/`:1065`) | |
| `start_bubbles` / `stop_bubbles` | `App.Bubbles.Start(bypassLevelCheck:true)` / `.Stop()` (`:1076`/`:1080`) | |
| `trigger_video` / `start_video` / `stop_video` | `App.Video.TriggerVideo()` / `.Start()` / `.Stop()` (`:1085`/`:1089`/`:1093`) | |
| `play_hypnotube` | `MainWindowRef.PlayHypnotubeFromRemote(url)` (`:1097`) | **Hard-gated**: URL must pass `HtUrlHelper.IsEligibleHtUrl` (`:1106`); routed through WebView2, NOT LibVLC. Only command that validates untrusted input. |
| `trigger_haptic` | `App.Haptics.TriggerAsync("remote_control", 0.7, 2000)` (`:1116`) | |
| `duck_audio` / `unduck_audio` | `App.Audio.Duck(80)` / `.ForceUnduck()` (`:1120`/`:1124`) | |
| `start_autonomy` / `stop_autonomy` | `App.Autonomy.Start()`/`.Stop()` (`:1129`/`:1133`) | |
| `trigger_bubble_count` | `App.BubbleCount.TriggerGame(forceTest:true)` (`:1137`) | |
| `trigger_lock_card` / `start_lock_card` / `stop_lock_card` | `App.LockCard.ShowLockCard()` / `.Start()` / `.Stop()` (`:1141`/`:1145`/`:1150`) | |
| `trigger_mind_wipe` | `App.MindWipe.TriggerOnce()` **only if `AudioFileCount > 0`** (`:1154`) | Pre-checks the pool to avoid the empty-pool MessageBox. |
| `start_mind_wipe` / `stop_mind_wipe` | `App.MindWipe.Start(freq, vol)` / `.Stop()` (`:1159`/`:1165`) | Reads `MindWipeFrequency`/`Volume` from settings. |
| `start_bounce_text` / `stop_bounce_text` | `App.BouncingText.Start(bypassLevelCheck:true)` / `.Stop()` (`:1169`/`:1173`) | |
| `start_session` | `MainWindowRef.StartSessionFromRemote(session)` (`:1177`) | Looks up `session_id` via `FindSessionByIdCallback`; else builds a generic "Remote Session" that mirrors current settings (`:1187`). Optional `strict_lock` param. |
| `pause_session` / `resume_session` / `stop_session` | `MainWindowRef.Pause/Resume/StopSessionFromRemote()` (`:1229`/`:1234`/`:1239`) | |
| `enable_strict_lock` / `disable_strict_lock` | `Settings.StrictLockEnabled` (`:1244`/`:1252`) | |
| `disable_panic` / `enable_panic` | `Settings.PanicKeyEnabled` (`:1260`/`:1268`) | Controller can disable the subject's ESC panic key. |
| `trigger_wallpaper` / `stop_wallpaper` | `App.Wallpaper.Activate()/Shuffle()` / `.Deactivate()` (`:1276`/`:1283`) | |
| `trigger_panic` | `StopAllRemoteEffects()` (`:1287`) | The "stop everything" verb (see §4c). |
| _unknown_ | logged as warning (`:1291`) | Silently ignored otherwise. |

### 4c. Panic / stop-all fan-out (`StopAllRemoteEffects`, `:770`)
Called by `trigger_panic` and by `CleanupSession`. Kills audio, cancels + re-arms Autonomy (careful
dance to keep the toggle honest, `:787`), stops Haptics, Video (LibVLC **and** the WebView2 HypnoTube
path via `StopBrowserVideoFromRemote`, `:807`), Flash, Subliminal, Bubbles, BouncingText, BubbleCount,
MindWipe, BrainDrain, LockCard, Wallpaper; **resets `App.InteractionQueue` before** force-closing
LockCard/BubbleCount windows (`:822`, ordering matters — #462); turns off overlays; stops the session
engine (`StopSessionFromRemote`); restores the window from tray + shows the avatar. `StopRemoteTriggeredEffects`
(`:900`) is the **lighter** variant used on controller disconnect when
`StopEffectsOnRemoteDisconnect` is set.

### 4d. Subsystem touchpoints beyond the dispatch table
- **SessionEngine**: three callbacks wired by `WireRemoteSessionCallbacks` (`MainWindow.RemoteControl.cs:989`)
  — `GetAvailableSessionsCallback`, `GetSessionProgressCallback`, `FindSessionByIdCallback` — feed the
  controller the subject's session list + live progress via the status push.
- **Engine takeover**: first controller connect **stops the running engine/session** both in the
  service (`:472`) and in `OnRemoteControllerChanged` (`MainWindow.RemoteControl.cs:871`); on
  controller-left the engine is optionally **restored** (`RestoreEngineAfterControllerLeftIfNeeded`,
  `:882`, #294) unless `StopEffectsOnRemoteDisconnect`.
- **Overlay**: `EnsureOverlayRunning` (`:953`) sets `App.Overlay.BypassLevelCheck = true` so remote
  pink/spiral work regardless of the subject's level; cleared on session end.
- **Progression / quests**: only the quest hook (§4a). **No XP is awarded for remote commands
  themselves.**
- **GamificationBridge**: subscribes to the service's `SessionStarted` event
  (`GamificationBridge.cs:105`).
- **Avatar / tray**: controller-joined pops a tray balloon + taskbar flash (`NotifyRemoteControllerJoined`,
  `:1455`); emotes flash the avatar speech bubble (`SendEmoteAndReportAsync`, `:358`).
- **Chaos / DtRH**: no direct remote command drives Chaos; the shared `StopEngineAndSession` helper
  (`:1358`) is used by both remote and Chaos dives.

### 4e. Patreon gating (where premium is actually checked)
- The Remote Control **tab** shows a translucent **gating overlay** (`RemoteControlGate`,
  `RemoteControlTabView.xaml:514`) for non-premium users, toggled by `RefreshPremiumGate`
  (`MainWindow.Patreon.cs:166`) purely on `App.Patreon?.HasPremiumAccess`.
- `HasPremiumAccess` (`PatreonService.cs:131`) = Patreon Tier 1+ **OR** whitelisted **OR** cached
  premium **OR** SubscribeStar Tier 1+ (within a 2-week grace window). It is **not** AI-access
  (`HasAiAccess`) — Remote Control is a plain premium feature, not an AI one.
- **Important**: the enable toggle handler (`ChkRemoteControlEnabled_Changed`, `:35`) only checks
  `UnifiedUserId` (login), **not** `HasPremiumAccess`. Premium enforcement on the client is the tab
  overlay blocking clicks — see §10.1.

---

## 5. Render / side-effects (what the subject sees)

Remote Control has **no compositor layer** and no persistent visual of its own. Its on-screen surface:
- **"Someone is controlling your app" overlay** (`ShowRemoteControlOverlay`, `:952`): a full-tab
  fade-in overlay shown while a controller is connected. It **hides the WebView2 browser** first to
  dodge the WebView2 airspace issue (`:963`).
- **Command notification toast** (`ShowCommandNotification`, `:1245`): 2 s fade for each non-suppressed
  command.
- **Command log** (`AppendRemoteCommandLog`, `:1118`): capped at 50 entries.
- **QR code** (`RefreshRemoteQrCode`, `:1164`): rendered with the bundled **QRCoder** library, tinted
  with the active mod's accent color.
- **Tray + taskbar**: balloon + flash on controller join; `RestoreFromTrayForRemote` on stop.
- Everything else the controller triggers renders through the **target service's own** render path
  (Flash images, Subliminal cards, Video windows, overlays, etc.) — Remote Control just calls the entry
  point.

---

## 6. Settings & gating

| Setting | `file:line` | Default | Effect |
|---|---|---|---|
| `StopEffectsOnRemoteDisconnect` | `AppSettings.cs:4643` | `false` | If true, remote-started effects stop when the controller leaves; else they continue (engine restored). |
| `RemoteShareAvatar` | `AppSettings.cs:4658` | `false` | Subject-side opt-in to expose the linked **Discord** avatar to the controller (fails closed). Toggling pushes status immediately. |
| `RememberDirectoryDetails` | `AppSettings.cs:4671` | `false` | Persist directory tags+status across sessions (opt-in checkbox itself never persists). |
| `SavedDirectoryTags` | `AppSettings.cs:4684` | `[]` | Remembered directory tags (capped 5). |
| `SavedDirectoryStatusText` | `AppSettings.cs:4696` | `""` | Remembered directory status text (clamped 80 chars on set). |
| `RemoteEmotePresets` | `AppSettings.cs:110` | 6 defaults | Editable emote quick-send buttons. |
| `AuthToken` | (Settings) | — | Cloud auth token sent as `X-Auth-Token` on every request. |

**Gating summary** (read carefully — this surprised the prior batch):
- **Premium**: `HasPremiumAccess` (§4e). **UI-only** — the tab gate overlay. Not enforced in
  `StartSessionAsync` or `ExecuteCommand`.
- **Tiers (light/standard/full)**: selected in `CmbRemoteTier` (`GetSelectedRemoteTier`, `:126`),
  described in the waiver (`ShowRemoteControlWaiver`, `:137`), and sent to the server at start — but
  **the client `switch` executes any action regardless of tier** (there is no tier check in
  `ExecuteCommand`). Tier enforcement is **server-side only**. See §10.2.
- **Login**: `UnifiedUserId` required to enable at all (`:43`).

---

## 7. The Available Subjects directory (sibling surface)

A public opt-in matchmaking directory bolted onto Remote Control ("SP5 layer 3"). Two services:
- **`RemoteControlService.OptInToDirectoryAsync`** (`:185`) POSTs the active session's `{code, pin,
  tags, status_text}` to `/v2/directory/opt-in` so the subject appears as **available**. The PIN is
  sent plaintext in the body (same PIN already shown on-screen — no new exposure, per the code
  comment). On controller disconnect the entry is **re-published** to flip back to available
  (`RepublishDirectoryIfOptedInAsync`, `:236`).
- **`AvailableSubjectsService`** (`Services/AvailableSubjectsService.cs:30`) is the **controller-side**
  browser: polls `/v2/directory/list` every 15 s while the tab is visible (`:142`), and
  `TryClaimAsync` POSTs `/v2/directory/claim` (`:196`/`:210`) to grab a subject and get a one-click
  pairing URL (opened via `Process.Start`, `MainWindow.RemoteControl.cs:568`).
- UI: the opt-in form (10 fixed tags, cap 5) + "Available Subjects" tab live in
  `MainWindow.RemoteControl.cs:588-811`. Free users get a "become a subject → Patreon" CTA
  (`BtnBecomeASubject_Click`, `:459`).

---

## 8. File map (read-verified)

| File | `:line` | Role |
|---|---|---|
| `Services/RemoteControlService.cs` | class `:30` | **The client.** HTTP, poll loop, session lifecycle, status/emote, `ExecuteCommand` dispatcher (`:967`). |
| `MainWindow/MainWindow.RemoteControl.cs` | partial `:31` | Tab UI, tier cards, consent waiver (`:137`), pairing URL (`:1153`) + QR (`:1164`), control overlay (`:952`), directory opt-in, session bridge (`StartSessionFromRemote` `:1278`, `TriggerPanicFromRemote` `:1381`). |
| `App.xaml.cs` | `:366`, `:1520`, `:1522`, `:1700`, `:3233` | Declares/constructs `App.RemoteControl`; wires `CommandReceived → Quests.TrackRemoteCommand`; injects `MainWindowRef`; disposes first on shutdown. |
| `MainWindow/MainWindow.xaml.cs` | `:121`, `:145` | `CommandLabels` (toast/log localization) + `SuppressedCommands` (log-silently set). |
| `MainWindow/MainWindow.Patreon.cs` | `:166` | `RefreshPremiumGate` — the premium overlay toggle. |
| `MainWindow/MainWindow.Browser.cs` | `:2430`, `:2449` | `PlayHypnotubeFromRemote` / `StopBrowserVideoFromRemote` (WebView2 path for remote HT videos). |
| `Views/Tabs/RemoteControlTabView.xaml` | `:514` | The tab layout + `RemoteControlGate` premium overlay. |
| `Views/Tabs/AvailableSubjectsTabView.xaml` | — | Controller-side directory tab. |
| `Services/AvailableSubjectsService.cs` | `:30` | Controller-side directory list/claim (`/v2/directory/list`, `/v2/directory/claim`). |
| `Models/AppSettings.cs` | `:4643`+, `:110` | Remote settings region + emote presets. |
| `Services/Progression/QuestService.cs` | `:728` | `TrackRemoteCommand` → `QuestCategory.Remote`. |
| `Models/Quest.cs` | `:32`, `:216-247` | `QuestCategory.Remote` + the premium remote quests. |
| `Services/GamificationBridge.cs` | `:105` | Subscribes to `SessionStarted`. |
| `Helpers/HtUrlHelper.cs` | — | `IsEligibleHtUrl` / `TryExtractHtVideoId` — the `play_hypnotube` domain gate. |

---

## 9. WHERE TO CHANGE X

| Want to… | Edit |
|---|---|
| Add a new remote command | Add a `case` in `ExecuteCommand` (`RemoteControlService.cs:967`); add its label to `CommandLabels` (`MainWindow.xaml.cs:121`); add to `SuppressedCommands` if high-frequency; add a matching button on the **web controller** (separate `cclabs-site` repo). |
| Enforce a command per tier on the client | There is **no** tier gate today (§10.2) — you'd add a `Tier`-aware check in `ExecuteCommand` reading `this.Tier`. |
| Change the poll cadence / rate-limit behavior | `PollIntervalSeconds` (`:35`), backoff in `PollForCommandsAsync` (`:366-375`). |
| Change status-push throttle | `StatusPushIntervalSeconds` (`:38`), `StatusBackoffSeconds` (`:40`), `SendStatusAsync` (`:579`). |
| Change what state the controller sees | `GetActiveServices` (`:747`) + the `SendStatusAsync` body (`:608`). |
| Change the pairing URL / QR | `BuildRemotePairingUrl` (`MainWindow.RemoteControl.cs:1153`) + `RefreshRemoteQrCode` (`:1164`). |
| Change the consent waiver copy | `ShowRemoteControlWaiver` (`:137`). |
| Change the idle-disconnect timeout | `IdleAutoDisconnectSeconds` (`:322`). |
| Change premium gating | `RefreshPremiumGate` (`MainWindow.Patreon.cs:166`) + `HasPremiumAccess` (`PatreonService.cs:131`); the enable-toggle login check is `:43`. |
| Change directory opt-in tags/behavior | `OptInToDirectoryAsync` (`:185`) + the opt-in UI (`MainWindow.RemoteControl.cs:588-811`). |

---

## 10. GOTCHAS

1. **Premium gating is UI-only.** The Remote Control tab is covered by a hit-testable overlay
   (`RemoteControlGate`) for non-premium users, but `StartSessionAsync` and `ExecuteCommand` never
   check `HasPremiumAccess`. The enable toggle only checks `UnifiedUserId` (`:43`). If a code path
   reached the service directly it would run for a free user — the gate is the overlay, not the
   service.
2. **Tiers are NOT enforced on the client.** `ExecuteCommand` (`:967`) has no `Tier` check anywhere —
   it will happily run a "full"-tier verb (`disable_panic`, `start_session`) even if the session was
   started as "light". The tier is passed to the server at `/v2/remote/start` and shown in the waiver;
   **only the server decides which commands to enqueue for a given tier.** Trusting the tier for
   safety requires trusting the proxy.
3. **`trigger_panic` (remote) does NOT call `TriggerPanicFromRemote`.** The remote panic verb runs
   `StopAllRemoteEffects()` (`:1287`). `MainWindow.TriggerPanicFromRemote` (`:1381`) — despite the name
   — is actually invoked by the **local voice** panic command (`AutonomyService.VoiceCommands.cs:133`),
   not by any remote command. Misleading name; don't assume the remote path goes through it.
4. **`MinimizeToTrayForRemote` (`:1439`) appears unused.** No caller found in the C# sources — likely
   vestigial. (`MinimizeToTrayForChaos` right below it *is* used.) Verify with a grep before relying on
   it.
5. **Two different hosts.** API calls go to `codebambi-proxy.vercel.app` (`ProxyBaseUrl`, `:32`); the
   pairing page the controller opens is `cclabs.app/remote/` (`:1157`). Don't conflate them.
6. **PIN is intentionally in the URL hash fragment**, not a query param, so it stays out of server logs
   and Referer headers (`:1151` comment). It is also sent plaintext in the directory opt-in body — by
   design (same PIN already on-screen, `:172` comment). Keep both properties if you touch pairing.
7. **The engine-takeover has a false→true→true trap.** A controller takeover (A leaves, B joins)
   re-fires the connect transition; `_engineStoppedForController` (`:339`) and
   `_remoteSessionHasTakenLocal` (`MainWindow.RemoteControl.cs:890`) guard against re-stopping a
   session the previous controller had running (#166). Preserve these guards.
8. **Controller-supplied text and URLs cross a trust boundary.** `trigger_custom_subliminal` puts
   arbitrary controller text on the subject's screens (capped/stripped inside `FlashSubliminalCustom`).
   `play_hypnotube` is the **only** command that hard-validates its input (`HtUrlHelper.IsEligibleHtUrl`,
   `:1106`) and deliberately avoids `App.Video.PlayUrl` (no domain gate). Any new command that takes a
   URL/HTML param should follow the `play_hypnotube` pattern.
9. **No push channel — everything is 5 s-quantized.** Commands, connection state, and idle detection
   all ride the poll. Perceived latency is up to ~5 s (worse under 429 backoff). Don't design a command
   that assumes real-time delivery.
10. **`CleanupSession` runs `StopAllRemoteEffects` on the UI thread synchronously** (`:296`,
    `RunOnUISync`). If you add expensive teardown, keep it UI-thread-safe and fast, or the session-stop
    click will hitch.

---

## 11. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

> This section rots. Confirm with `git log --oneline -- Services/RemoteControlService.cs
> MainWindow/MainWindow.RemoteControl.cs` and `git branch` before acting.

- **State: mature and shipping.** No dedicated in-flight branch for Remote Control. HEAD `95586020` on
  `fix/web-video-interruptions` (v6.5.0). Recent churn is peripheral (SP5 "Available Subjects"
  directory layer, emote channel, avatar-share privacy toggle, `#166`/`#294` takeover fixes).
- **Server contract lives elsewhere.** The `/v2/remote/*` and `/v2/directory/*` endpoints are in the
  private `CC-Labs-llc/CCP-Server` repo (proxied via `codebambi-proxy.vercel.app`); the web controller
  is in `cclabs-site`. This primer is client-only — server-side tier enforcement and rate-limit
  specifics must be confirmed there.
- **Known quirks (see §10), none are user-reported bugs**: UI-only premium gate; no client-side tier
  enforcement; misleadingly-named `TriggerPanicFromRemote`; apparently-dead `MinimizeToTrayForRemote`.
  Documented so they aren't "fixed" blindly.
- **No dedicated unit tests** cover `RemoteControlService` (network + `DispatcherTimer` + static `App.*`
  make it hard to isolate). The standing gate is play-test with a real pairing.
- **This primer is new** and not previously committed.

---

## 12. Build / run / dev

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```
Then: log in (Patreon or Discord for a `UnifiedUserId`) with premium (or whitelist), open the **Remote
Control** tab, pick a tier, accept the waiver, and enable. Scan the QR / open the printed
`cclabs.app/remote/#code=…&pin=…` on a phone to act as the Controller. Watch `logs/` for
`[RemoteControl] …` Serilog lines (session code, poll health every 30 s, each executed command, 429
backoff).
