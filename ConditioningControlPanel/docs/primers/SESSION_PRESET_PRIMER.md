# Sessions & Presets — Feature Primer

> **Purpose.** One-load orientation for the **Sessions/Presets tab** and the whole session+preset
> system, so you can maintain it WITHOUT re-reading the ~1,700-line `MainWindow.Presets.cs` or the
> 1,600-line `SessionEngine.cs`. §0 is the **concept map** — the vocabulary here is badly overloaded
> and that is the #1 source of confusion; read it first. §1 is the tab UI. §2–7 are the layers
> (preset model + storage, session model, the runtime engine, save/load/import/export, logging +
> post-session windows, the editor). **§8 is the load-bearing section — how a preset/session is
> invoked and every system it touches.** §9 file map, §10 where-to-change-X, §11 gotchas, §12 status.
>
> **Freshness.** Verified against source **2026-07-23** (branch `fix/web-video-interruptions`, HEAD
> `95586020`, v6.5.0). Every `file:line` below was read-verified when written and is git-verifiable,
> but line numbers drift — confirm with a quick read before quoting. §1–§11 track the code and rarely
> rot; **§12 is a dated snapshot — verify with `git log` before acting.**

---

## 0. What this is — and the concept map (READ THIS)

The "Sessions/Presets" tab (`Views/Tabs/PresetsTabView.xaml`) is one dashboard tab with two halves: a
**Presets** carousel (top-left) and a **Sessions** list (bottom-left), plus a shared details/action
panel on the right. Behind that single tab sit **four genuinely different concepts that all use the
words "session" or "preset".** Getting them straight is the whole game:

| Concept | Type(s) | What it actually is |
|---|---|---|
| **Preset** | `Models/Preset.cs` (533 lines) | A **saved snapshot of dashboard settings** (flash/video/subliminal/overlay/… toggles + numbers). Applying one calls `preset.ApplyTo(App.Settings.Current)` — it just overwrites live settings. No timeline, no timer, no runtime object. Stored inside `settings.json` (`AppSettings.UserPresets`). |
| **Session** | `Models/Session.cs` (950), `SessionSettings` (same file) | A **timed, scripted experience** — a duration, difficulty, XP reward, a `SessionSettings` block (per-feature start/end minutes + ramps) and a `Phases` timeline. It's *run* by an engine that ramps effects over minutes and restores your settings at the end. Stored as `.session.json` files. |
| **SessionEngine** | `Services/Session/SessionEngine.cs` (1639) | The **runtime that plays a Session**: a 1-second `DispatcherTimer` that ramps opacities, fires deferred feature starts, awards XP, and coordinates every service. `IDisposable`. **NOT AI-powered** (see the box below). |
| **The tab UI** | `PresetsTabView.xaml(.cs)` + `MainWindow.Presets.cs`/`.SessionIO.cs`/`.PresetIO.cs` | The visual surface + all its handlers. The `.xaml.cs` is a **thin forwarder** — every handler just calls `MainWindow.<same-name>`. |

> **⚠ The `SessionEngine` is NOT AI/OpenRouter, despite the root `CLAUDE.md`.** The project
> `CLAUDE.md` says *"SessionEngine.cs — AI-powered session management with OpenRouter integration."*
> That is **stale/incorrect** for the current code. `SessionEngine.cs` contains no AI, no OpenRouter,
> no network calls — it is a deterministic timer + settings coordinator. A grep for
> `OpenRouter|GenerateSession|AiSession` under `Services/Session/` returns nothing. (Sessions *can* be
> AI-*authored* elsewhere — the Graded Intake's `QuizSessionGenerator` drafts a `.session.json` — but
> that's a separate feature that writes a file the engine later plays.) Treat this primer's §5 as the
> authority over the CLAUDE.md one-liner.

### The two conversions everything funnels through

- **`SessionSettings`** (the flat, one-segment-per-feature view the *engine* reads) ↔
  **`TimelineEvent` list** (the rich, multiple-segments-per-feature view the *editor* reads).
  `TimelineSession.ToSessionSettings()` / `TimelineSession.FromSession()` bridge them.
- **`Session`** (runtime object) ↔ **`SessionDefinition`** (the serializable `.session.json` shape).
  `SessionDefinition.ToSession()` / `FromSession()` bridge them.

### Same-named-but-UNRELATED things — NOT covered here

- **`AssetPreset`** (`Models/AssetPreset.cs`) — a *different* preset kind: a blacklist of disabled
  image/video paths ("which assets are on"), used by the **Assets tab**, not the Presets carousel.
  Included in the file map for disambiguation only.
- **`KeywordTriggerPreset`**, **`PersonalityPreset`/companion presets** — unrelated feature presets.
- **`ModerationSession`**, **Bureau/quiz "session"**, **Graded-Intake "session"** — web/AI features
  that reuse the word.
- **`SessionLogService`/`SessionLog`** — the post-session *media log* (what played), NOT the engine.
  Covered in §6 because it's wired to the engine, but it is a separate service.

---

## 1. The tab UI (`Views/Tabs/PresetsTabView.xaml`, 639 lines)

The tab is a `UserControl` embedded in `MainWindow`. Its `.xaml.cs` (121 lines) does **nothing but
forward** every event to the matching `MainWindow` method (`Window.GetWindow(this) is MainWindow mw`
→ `mw.<Handler>`). So all real logic lives in the `MainWindow.*.cs` partials. Controls are exposed to
those partials via `x:FieldModifier="internal"` and referenced as `PresetsTab.<Name>`.

### Layout (three columns, GridSplitter between the left pair and the right)

- **Top-left — Presets carousel** (`PresetCardsPanel`, horizontally scrolling): one 100×70 card per
  preset, generated in code by `CreatePresetCard` (`MainWindow.Presets.cs:259`). A fixed **"➕ New"**
  card (`BtnNewPreset_Click`, XAML `:43`) creates a preset from current settings.
- **Bottom-left — Sessions list** (`SessionsPanel`): **four hard-coded built-in session cards** in
  XAML (`SessionMorningDrift`/`SessionGamerGirl`/`SessionDistantDoll`/`SessionGoodGirls`, each with a
  `Tag` id + ✏ edit / 📤 export buttons), then a code-generated **"Your Sessions"** area
  (`CustomSessionsPanel` + `TxtCustomSessionsHeader`, hidden until a custom session exists). Below
  that: a **Catalogue card** (`SessionDropZone`, repurposed from the old drag-drop box → opens the
  web catalogue, `CatalogueCard_Click`) and a **Session Editor** panel with **"Create New"**
  (`BtnCreateSession`) + **"Export"** (`BtnExportSession`) buttons. A **"Recent Sessions"** button
  (`BtnSessionHistory`) opens the log history window. `BtnSessionHistory` and the help `?` buttons are
  the only chrome up top.
- **Right — details + actions** (two mutually-exclusive scrollers + two mutually-exclusive button
  panels, toggled by `SelectPreset` vs `SelectSession`):
  - **Preset view**: `PresetDetailScroller` (flash/video/subliminal/audio/overlay/advanced summary)
    + `PresetButtonsPanel` (**Load Preset**, **Save** [over], **Delete**, **Export Preset**,
    **Share to Catalogue**). All disabled until a preset is selected; Save/Delete/Share disabled for
    built-in "default" presets.
  - **Session view (spoiler-free)**: `SessionDetailScroller` — duration/XP/difficulty badges, a
    spoiler-free description, an optional **Corner GIF** config panel (only for `HasCornerGifOption`
    sessions, i.e. Gamer Girl), a **"Reveal Details"** button that pops a **three-step spoiler
    warning** (`BtnRevealSpoilers_Click`, `MainWindow.Presets.cs:520`) before showing the
    `SessionSpoilerPanel`. `SessionButtonsPanel` holds the single **Start Session** button
    (`BtnStartSession`) which doubles as Stop while running.

There is **also a separate presets dropdown** (`CmbPresets`) that lives elsewhere in MainWindow (not
in this tab view) — `RefreshPresetsDropdown` / `CmbPresets_SelectionChanged`
(`MainWindow.Presets.cs:162/209`). It offers the same presets + a "➕ Save as New Preset…" row.

---

## 2. The Preset data model + storage

### `Models/Preset.cs` (533)
A flat POCO (`INotifyPropertyChanged`) with ~70 settings fields mirroring `AppSettings` (flash,
video, attention, subliminal, audio, overlays, bubbles, trigger-bubbles, lock card, bubble-count,
bouncing text, mind wipe, brain drain, system). Key methods:
- **`ApplyTo(AppSettings settings)`** (`:319`) — copies every field into live settings, then sets
  `settings.CurrentPresetName = Name`. **Nullable fields skip when null** (`BubblesVolume`,
  `BubbleSharedHost`, `BubblesLinkRamp`, `BubblesClickable`, `:378–381`) so an *old* preset can't
  clobber newer opt-outs (the mixed-DPI `BubbleSharedHost=false` workaround).
- **`FromSettings(settings, name, description)`** (`:428`) — the inverse; deep-copies the three
  phrase dictionaries (`SubliminalPool`/`AttentionPool`/`LockCardPhrases`).
- **`GetDefaultPresets()`** (`:154`) — the **5 built-in presets** ("Gentle Introduction", "Bimbo
  Basics", "Pink Cloud", "Deep Conditioning", "Total Surrender"), each `IsDefault = true` with fixed
  string ids (`default-gentle`, …). Built-ins can't be saved-over/deleted/shared.

### Storage — presets live in `settings.json`, NOT their own files
User presets are `AppSettings.UserPresets` (`Models/AppSettings.cs:100`), serialized with the rest of
settings (Newtonsoft, PascalCase). `CurrentPresetName` (`:93`) remembers the last-applied preset.
`_allPresets` in the tab is `GetDefaultPresets()` + `UserPresets` rebuilt each refresh
(`MainWindow.Presets.cs:145/249`).

### `Services/PresetFileService.cs` (196) — only for **sharing / drag-drop**
Serializes a **standalone `.preset.json`** with **System.Text.Json camelCase** (deliberately
different convention from the in-settings storage, matching `.session.json` so the web catalogue
round-trips). `SerializePreset`/`ExportPreset`/`ImportPreset` (`:53/61/69`), `ValidatePresetFile`
(`:105`), `CopyToCustomPresets` (`:162`, provenance copy into
`%APPDATA%/ConditioningControlPanel/CustomPresets`). The carousel **still reads from
`UserPresets`** — the CustomPresets folder is only a re-share copy.

---

## 3. The Session data model — timeline vs definition vs runtime

Three shapes of the same thing (see §0):

### `Models/Session.cs` (950) — the runtime session
`Session` (id, name, icon, duration, difficulty, `BonusXP`, `Source`, `SourceFilePath`, spoiler-free
`Description`) + **`SessionSettings`** (`:819`) — the flat per-feature block the engine reads. Each
feature has `…Enabled`, `…StartMinute`, `…EndMinute` (-1 = whole session), and where relevant
start/end ramp values (`FlashOpacity`/`FlashOpacityEnd`, `PinkFilterStartOpacity`/`…EndOpacity`, …).
Also `List<SessionPhase>` (the named timeline milestones) and `List<TimelineEvent> TimelineEvents`
(the raw editor events — see the #429 note below). Ships **4 hard-coded built-ins** as static
properties (`MorningDrift :137`, `GamerGirl :231`, `DistantDoll :343`, `GoodGirlsDontCum :439`) plus
3 "coming soon" placeholders in `GetAllSessions()` (`:558`). `MakeModeAware`/`GetModeAwareName`
(`:90–132`) rewrite Bambi-specific triggers for SH mode. A pile of `GetSpoiler*` / `GenerateFeature
Description` methods build the right-panel text (`:605–811`).

### `Models/SessionDefinition.cs` (121) — the `.session.json` file shape
Serializable twin of `Session`. `ToSession()`/`FromSession()` (`:74/99`) convert. Carries
`TimelineEvents` (`:62`) so **every authored segment survives** save/reload — `SessionSettings` is
only a flattened one-per-feature view (#429). `Source`/`SourceFilePath` are `[JsonIgnore]`.

### `Models/TimelineSession.cs` (804) — the editor's working shape
What `SessionEditorWindow` edits. Holds a `List<TimelineEvent>` (paired Start/Stop, per-event
`Settings` dictionary + optional `StartValue`/`EndValue` ramp). Key methods:
- **`ToSessionSettings()`** (`:296`) — flattens events → `SessionSettings` via 13 `Process*` helpers
  (one per feature id). **Only takes the FIRST start-event per feature** (`startEvents.First()`), so
  multi-segment authoring collapses to one segment in the flat view — hence `TimelineEvents` is also
  persisted raw.
- **`FromSession(session)`** (`:575`) — prefers the raw `TimelineEvents` when present; else rebuilds
  events from `SessionSettings` (one segment per feature). This is the #429 dual-representation seam.
- **`CalculateXP()`** (`:123`, 10 XP/min + per-feature bonus, rounded to 50) and
  **`CalculateDifficulty()`** (`:150`, duration + feature count + weights + intensity → Easy/Medium/
  Hard/Extreme). `ToSession()` (`:757`) stamps both onto the saved session.

### `Models/TimelineEvent.cs` (118) + `Models/FeatureDefinition.cs`
`TimelineEvent.GetSetting<T>` (`:64`) materializes `JsonElement` values after reload (else every
per-event setting silently resets, #429). `FeatureDefinition.GetAllFeatures()`
(`FeatureDefinition.cs:63`) is the **catalog of the 13 session step-types** the editor offers, keyed
by id: `audio_whispers`, `mind_wipe`, `flash`, `mandatory_videos`, `subliminal`, `bouncing_text`,
`pink_filter`, `spiral`, `brain_drain`, `bubbles`, `lock_cards`, `bubble_count`, `corner_gif` — each
with its settings, `XPBonus`, `DifficultyWeight`, category, ramp support. `GetById` (`:303`).

---

## 4. Session file storage (`.session.json`)

### `Services/SessionFileService.cs` (325)
System.Text.Json camelCase. Two folders:
- **Custom**: `%APPDATA%/ConditioningControlPanel/CustomSessions` (`CustomSessionsFolder :27`).
- **Built-in**: `<BaseDirectory>/assets/sessions` (`BuiltInSessionsFolder :39`).

`ExportSession` (`:62/71`), `ImportSession` (`:81`, tags `Source=Imported`), `ValidateSessionFile`
(`:118`, requires `.session.json` + id + name + duration>0), `LoadBuiltInSessions`/
`LoadCustomSessions` (`:202/180`), `SaveCustomSession` (`:226`), `CopyToCustomSessions` (`:252`,
de-dups filename), `DeleteCustomSession` (`:279`, refuses to delete outside the custom folder).

### `Services/Session/SessionManager.cs` (254)
In-memory registry (`App` does **not** hold this — `MainWindow` owns `_sessionManager`, created
lazily in `InitializeSessionManager`, `MainWindow.SessionIO.cs:39`). `LoadAllSessions()` (`:55`):
loads built-in `.session.json` files → **falls back to `Session.GetAllSessions()` hard-coded set if
none on disk** → loads custom files. Fires `SessionAdded`/`SessionRemoved`/`SessionsReloaded`.
`ImportSession` (`:101`, de-dups id), `AddNewSession` (`:174`), `UpdateCustomSession` (`:152`),
`DeleteSession` (`:201`, built-ins can't be deleted). **`LoadAllSessions` runs exactly once** (at
startup) — anything writing a session file behind its back (Graded Intake) must call
`RegisterExternallySavedSession` or the card won't appear until relaunch (#614, §11).

---

## 5. The runtime engine (`Services/Session/SessionEngine.cs`, 1639)

`SessionEngine(MainWindow)` — one per session run, created lazily in `StartSession`
(`MainWindow.Presets.cs:1170`), `IDisposable`. **No AI** (see §0 box).

### Lifecycle
1. **`StartSessionAsync(session)`** (`:139`): guards single-run; snapshots current settings
   (`SaveCurrentSettings :797`); captures achievement-relevant flags (`StrictLock`/`PanicKey`) at
   start; randomizes pink/spiral start ±3 min (`RandomizeStartTimes :710`); **`ApplySessionSettings`**
   (`:880`) writes the session's settings into `App.Settings.Current` and starts/defers each service;
   schedules bubble bursts; starts Mind Wipe (escalating session mode); starts a **1-second
   `DispatcherTimer`** (`_mainTimer`); fires `SessionStarted`; sets Discord presence; tracks
   achievements + Season Recap; **`App.SessionLog?.BeginSession(session)`** (`:246`).
2. **`MainTimer_Tick`** (`:460`, every 1s): completes the session at duration; raises
   `ProgressUpdated`; `CheckPhaseTransition`; **`UpdateRampingValues`** (`:520`) lerps flash opacity/
   freq, pink & spiral opacity (driven **directly** via `App.Overlay.SetSustainedOverlayOpacity`, NOT
   into `App.Settings` — see #471 note), bubble freq; **`CheckDelayedFeatures`** (`:600`) fires queued
   deferred starts + pink/spiral/bubble/corner-gif delayed activation; `HandleIntermittentBubbles`.
3. **`StopSession(completed)`** (`:260`): stops timers, clears pending starts, stops every service,
   `App.Audio.ForceUnduck()`, **`RestoreSettings`** (`:1168`, restores the snapshot + phrase pools +
   `App.Overlay.ReleaseOpacityRampHolds()`), fires `SessionStopped`, computes final XP (base capped
   2500 − 100/pause, × level multiplier + duration bonus), tracks achievements from the **start-time
   snapshot**, fires `SessionCompleted`, then `App.SessionLog?.EndSession(...)`.
4. **`PauseSession`/`ResumeSession`** (`:390/426`): each pause costs 100 XP (`XPPenalty :89`); pause
   stops all services, resume re-starts only non-pending ones. Elapsed time is preserved.

### Deferred feature starts (#483)
The editor serializes a `StartMinute` for all 13 features, but historically only 4 (pink, spiral,
bubbles, corner GIF) were honored. `_pendingFeatureStarts` + `DeferFeatureStart` (`:869/871`) now
queue *any* delayed feature; `CheckDelayedFeatures` fires each when its minute arrives.

### Anti-cheat clock
`ElapsedTime` (`:91`) cross-checks `DateTime` against a monotonic `Stopwatch`; >30s divergence
(either direction) trusts the Stopwatch (guards speed-hacks AND backward NTP/DST/sleep jumps, #369).

### Corner GIF
A separate topmost click-through `Window` (`ShowCornerGif :1262`) for the Gamer Girl session; live
size/path changes **recreate** the window (in-place resize of an animating transparent window
deadlocks the render thread, #474); position/opacity change in place.

---

## 6. Session logging + the post-session windows

### `Services/Session/SessionLogService.cs` (294) — `App.SessionLog`
Declared `App.xaml.cs:288`, constructed `:1383`, disposed `:3243`. Captures **what media played**
during a session (not settings). `BeginSession` (`:45`) subscribes to
`App.Flash.FlashDisplayed` + `App.Video.VideoStarted` (`SubscribeUnlocked :150`); `OnFlashDisplayed`/
`OnVideoStarted` (`:173/205`) read `App.Flash.LastDisplayedImagePaths` / `App.Video.LastVideoPath`
into a `SessionLog` (`Models/SessionLog.cs`). `EndSession` (`:76`) finalizes, **persists to
`%APPDATA%/…/session_logs`** (skips <30s sessions with no media), prunes to `MaxRetainedLogs = 20`,
and raises **`LogReady`**. **Subscribed only while a session is active.**

### The two windows
- **`Windows/SessionCompleteWindow`** — the post-session summary. MainWindow subscribes
  `App.SessionLog.LogReady += OnSessionLogReady` (`MainWindow.xaml.cs:341`); `OnSessionLogReady`
  (`MainWindow.Presets.cs:1221`) → **`ShowSessionSummaryWhenClear`** (`:1238`) which *waits out* any
  in-flight video teardown (a dying fullscreen video buries the modal, #462) before showing the
  dialog at `ApplicationIdle`. Two ctors: `(SessionLog, playSound)` (`:26`, the live path) and
  `(Session, TimeSpan, int)` (`:41`) which is **dead — never constructed** (both call sites use the
  `SessionLog` ctor).
- **`Windows/SessionLogHistoryWindow`** — the "Recent Sessions" browser (`BtnSessionHistory_Click`,
  `MainWindow.Presets.cs:1118`); reads `SessionLogService.LoadRecentLogs()` and can re-open a
  `SessionCompleteWindow(log, playSound:false)`.

---

## 7. The session editor (`Windows/SessionEditorWindow.xaml(.cs)`, 1157)

A modal timeline editor. Ctor `(Session?)` (`:51`): loads via `TimelineSession.FromSession`, else
starts blank. Drag features from a palette onto a canvas timeline (start/stop bars, per-event
settings popup, ramp start/end). On save it sets `ResultSession` (`TimelineSession.ToSession()`).
Opened from **Create New** (`BtnCreateSession_Click`, `MainWindow.SessionIO.cs:1189` → SaveFileDialog
→ `SessionManager.AddNewSession`) and **Edit ✏** (`SessionBtn_Edit :979` → editing a **built-in**
forks a new custom session with a fresh id; editing a custom one saves over it via
`UpdateCustomSession`). No level/Patreon gate.

---

## 8. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

Read this before wiring anything new.

### 8a. Applying a **preset** (the simple path)
- **Select** a carousel card → `SelectPreset` (`MainWindow.Presets.cs:348`) fills the detail panel,
  enables buttons. **Load Preset** (`BtnLoadPreset_Click :1628` → `LoadPreset :1612`) confirms, then
  `preset.ApplyTo(App.Settings.Current)` → `App.Settings.Save()` → `LoadSettings()` re-syncs the UI.
  The dropdown `CmbPresets_SelectionChanged` (`:209`) does the same after a confirm.
- **Save/New**: `PromptSaveNewPreset` (`:1649`) / `BtnSaveOverPreset_Click` (`:1681`) call
  `Preset.FromSettings(App.Settings.Current, …)`, mutate `UserPresets`, `Save()`.
- **A preset does NOT start anything.** It only writes settings. Whether effects run depends on the
  engine (Start button) / feature toggles. Applying a preset mid-run just changes live values.

### 8b. Starting/stopping a **session** (the rich path)
- **Start**: `BtnStartSession_Click` (`:1131`) — if a session is running it dispatches to Stop; else
  confirms and calls `StartSession(session)` (`:1157`). That applies corner-GIF UI choices, lazily
  builds the `SessionEngine` + wires 5 events, **starts the dashboard engine if idle**
  (`BtnStart_Click`), then `await _sessionEngine.StartSessionAsync(session)`.
- **Stop/Pause**: `BtnStopSession_Click` (`:1391`) / `BtnPauseSession_Click` (`:1429`) — both refuse
  while `App.Lockdown.IsActive`. `OnSessionStopped` (`:1367`) also calls `StopEngine()`.
- **Progress/label**: `OnSessionProgressUpdated` (`:1302`) rewrites the Start button + hero label
  with a live countdown; `OnSessionStarted`/`Stopped` (`:1333/1367`) flip button chrome + the
  `App.IsSessionRunning` flag.

### 8c. `App.IsSessionRunning` — the global session flag
`App.xaml.cs:408`, set true/false in `OnSessionStarted`/`OnSessionCompleted`/`OnSessionStopped`
(`MainWindow.Presets.cs:1335/1204/1369`). Consumers gate behavior on it:
`AutonomyService.cs:1532/1639` (autonomy backs off), `AttentionCheckService.cs:228`,
`BubbleService.cs:873/923` (bubble clickability honors session setting), `ChaosModeService.cs:301`
(chaos won't start during a session), `RemoteControlService.cs:757` + `ProfileSyncService.cs:154`
(reported state). **`MainWindow.ApplySessionSettings()`** (`MainWindow.Presets.cs:1465`) is a
misleadingly-named "reload settings into the UI" helper (`_isLoading=true; LoadSettings();`) — the
engine calls it after apply/restore, and cloud-restore calls it too (`App.xaml.cs:2339`). It does
**not** itself apply session settings — `SessionEngine.ApplySessionSettings` (private, `:880`) does.

### 8d. What a running session drives
The engine writes `App.Settings.Current.*` and Starts/Stops the services directly
(`ApplySessionSettings :880`): **Flash** (`App.Flash.Start/Stop`), **Subliminal** (+ overrides the
subliminal phrase pool with session phrases, mod-aware), **Sub-audio whispers** (flag), **audio
ducking** (session duck level), **Bouncing Text** (`bypassLevelCheck:true` + phrase override),
**Pink Filter / Spiral** (via `App.Overlay`, ramped directly), **Bubbles**
(`App.Bubbles.Start(bypassLevelCheck:true)` + burst scheduler), **Mandatory Videos**
(`App.Video.Start`), **Lock Cards** (+ phrase override), **Pop Quiz** (user-level flag, not
per-session), **Bubble Count**, **Mind Wipe** (`StartSession` escalating mode), **Corner GIF**,
**Brain Drain** (present but **disabled/commented out** — "up for rework", §11). All start-minute>0
features route through `DeferFeatureStart`.

### 8e. Progression / achievements / Discord / Season Recap
`StartSessionAsync` calls `App.Achievements.TrackSessionStart`, `SeasonRecapService` feature tracking
(`:229–237`), `App.DiscordRpc.SetSessionActivity`. `StopSession` awards XP via `SessionCompleted` →
`OnSessionCompleted` → `App.Progression.AddXP(xp, XPSource.Session)` + cloud sync; tracks
complete/abandon/panic achievements from the **start-time snapshot**. Pauses cost 100 XP each.

### 8f. Share / import round-trip (catalogue + drag-drop)
- **Share preset** (`BtnSharePreset_Click`, `MainWindow.PresetIO.cs:184` →
  `SharePresetToCatalogueAsync :190`) serializes via `PresetFileService`, needs `AuthToken`, posts to
  `App.Catalogue` with schema `ccp-preset/v1`; **share session** (`SessionBtn_Share`,
  `MainWindow.SessionIO.cs:1043` → `ShareSessionToCatalogueAsync`, `MainWindow.PresetIO.cs:231`,
  schema `ccp-session/v1`). Status pills via `CreateCatalogueStatusBadge` (`:295`).
- **Import** is drag-drop onto MainWindow: `Window_Drop`/`DetectDropType`
  (`MainWindow.SessionIO.cs:697/782`) routes `.session.json` → `HandleSessionDrop` (→
  `SessionManager.ImportSession`) and `.preset.json` → `HandlePresetDrop` (`MainWindow.PresetIO.cs:139`
  → adds to `UserPresets`). The old dedicated session drop-zone handlers still exist
  (`SessionDropZone_Drop :1111`) but the box was repurposed into the Catalogue link.
- **Export** writes a file via SaveFileDialog (`ExportSessionToFile :1235`, `BtnExportPreset_Click`,
  `MainWindow.PresetIO.cs:28`).

### 8g. Patreon / level gating — **there is essentially none on this system**
Grep confirms `SessionEngine` has **no** `HasPremiumAccess`/`HasAiAccess` checks, and neither do
`Preset`/`Session`/`SessionManager`. Sessions deliberately start services with
`bypassLevelCheck:true`, so a session can run features **below their normal unlock level**. Presets
and plain sessions are **free/ungated**. (The only nearby gate is the commented-out Brain Drain
`IsLevelUnlocked(70)`, `SessionEngine.cs:452`.) The premium line lives in *other* features (AI chat,
Deeper, catalogue publishing needs auth) — not in the session/preset core. If the CLAUDE.md's
"AI sessions" claim ever gets a real implementation, that's where a gate would go; today it doesn't
exist.

---

## 9. File map (all `file:line` read-verified 2026-07-23)

| File | Owns | Key lines |
|---|---|---|
| `Views/Tabs/PresetsTabView.xaml` (639) | The tab layout | Presets carousel `:38/43`; built-in session cards `:107/151/195/239`; catalogue card `:296`; editor buttons `:341/361`; preset buttons `:602`; start button `:633`; spoiler panel `:574` |
| `Views/Tabs/PresetsTabView.xaml.cs` (121) | **Thin forwarders** → `MainWindow` | every handler `:15–120` |
| `MainWindow/MainWindow.Presets.cs` (1741) | Preset carousel + session start/stop/pause + engine callbacks | `SelectPreset :348`, `CreatePresetCard :259`, `BtnStartSession_Click :1131`, `StartSession :1157`, `OnSessionCompleted :1202`, `OnSessionLogReady :1221`, `ShowSessionSummaryWhenClear :1238`, `ApplySessionSettings :1465`, `LoadPreset :1612`, `PromptSaveNewPreset :1649`, `BtnSaveOverPreset :1681`, `BtnDeletePreset :1715` |
| `MainWindow/MainWindow.SessionIO.cs` (1268) | Session cards, import/export, editor launch, drag-drop | `InitializeSessionManager :39`, `RegisterExternallySavedSession :78`, `AddCustomSessionCard :131`, `SelectSession :358`, `Window_Drop :697`, `DetectDropType :782`, `SessionBtn_Edit :979`, `SessionBtn_Share :1043`, `BtnCreateSession_Click :1189`, `ExportSessionToFile :1235` |
| `MainWindow/MainWindow.PresetIO.cs` (366) | Preset export + drag-drop import + catalogue share glue | `BtnExportPreset_Click :28`, `HandlePresetDrop :139`, `SharePresetToCatalogueAsync :190`, `ShareSessionToCatalogueAsync :231`, `CreateCatalogueStatusBadge :295` |
| `Models/Preset.cs` (533) | Preset POCO | `ApplyTo :319`, `FromSettings :428`, `GetDefaultPresets :154` |
| `Models/Session.cs` (950) | Runtime session + `SessionSettings` | `Session :19`, `SessionSettings :819`, built-ins `:137/231/343/439`, `GetAllSessions :558`, `GetSpoiler* :696–811` |
| `Models/SessionDefinition.cs` (121) | `.session.json` shape | `ToSession :74`, `FromSession :99`, `TimelineEvents :62` |
| `Models/TimelineSession.cs` (804) | Editor working shape | `ToSessionSettings :296`, `FromSession :575`, `ToSession :757`, `CalculateXP :123`, `CalculateDifficulty :150` |
| `Models/TimelineEvent.cs` (118) | Timeline event | `GetSetting<T> :64` (JsonElement fix, #429) |
| `Models/FeatureDefinition.cs` | 13 editor step-types catalog | `GetAllFeatures :63`, `GetById :303` |
| `Models/SessionLog.cs` (78) | Media-log model | `MediaLogEntry :13`, `SessionLog :38` |
| `Models/AssetPreset.cs` (165) | **UNRELATED** asset blacklist preset | `ApplyToSettings :125` |
| `Services/Session/SessionEngine.cs` (1639) | Runtime engine | `StartSessionAsync :139`, `StopSession :260`, `Pause/Resume :390/426`, `MainTimer_Tick :460`, `UpdateRampingValues :520`, `CheckDelayedFeatures :600`, `ApplySessionSettings :880`, `RestoreSettings :1168`, `ShowCornerGif :1262`, `ElapsedTime :91` |
| `Services/Session/SessionManager.cs` (254) | Session registry | `LoadAllSessions :55`, `ImportSession :101`, `AddNewSession :174`, `DeleteSession :201` |
| `Services/Session/SessionFileService.cs` (325) | `.session.json` I/O | folders `:27/39`, `ImportSession :81`, `Validate :118`, `LoadBuiltInSessions :202` |
| `Services/Session/SessionLogService.cs` (294) | Post-session media log | `BeginSession :45`, `EndSession :76`, `OnFlashDisplayed :173`, `OnVideoStarted :205`, `LoadRecentLogs :109` |
| `Services/PresetFileService.cs` (196) | `.preset.json` share I/O | `SerializePreset :53`, `ImportPreset :69`, `CustomPresetsFolder :33` |
| `Windows/SessionEditorWindow.xaml.cs` (1157) | Timeline editor | ctor `:51`, `ResultSession :47` |
| `Windows/SessionCompleteWindow.xaml.cs` | Post-session summary | live ctor `:26`, **dead** ctor `:41` |
| `Windows/SessionLogHistoryWindow.xaml.cs` | Recent-sessions browser | ctor `:13` |
| `App.xaml.cs` | Wiring | `SessionLog` decl `:288` + init `:1383` + dispose `:3243`, `IsSessionRunning :408`, `ApplySessionSettings` (cloud restore) `:2339` |
| `MainWindow/MainWindow.xaml.cs` | Wiring | `_sessionEngine :165`, `LogReady` sub `:341`, `InitializePresets :358` |

---

## 10. Where to change X

| Want to… | Edit |
|---|---|
| Add a **preset field** | Add the property to `Models/Preset.cs`, then copy it in **both** `ApplyTo` (`:319`) and `FromSettings` (`:428`); add to `AppSettings` if it's a real setting; update the detail-panel text in `SelectPreset` (`MainWindow.Presets.cs:348`) if you want it shown. Make it nullable if old presets must not clobber it. |
| Add a **session step-type** | Add a `FeatureDefinition` in `FeatureDefinition.cs:GetAllFeatures`; add fields to `SessionSettings` (`Session.cs:819`); add a `Process<Feature>Settings` + `FromSession` block in `TimelineSession.cs`; teach `SessionEngine.ApplySessionSettings`/`CheckDelayedFeatures` to start/defer it. |
| Change the **tab layout** | `Views/Tabs/PresetsTabView.xaml` (+ add a forwarder in the `.xaml.cs` and the real handler in a `MainWindow.*.cs` partial). |
| Change the **preset save format / share** | `Services/PresetFileService.cs` (standalone `.preset.json`); in-settings storage is just `AppSettings.UserPresets`. |
| Change the **session file format** | `Services/SessionFileService.cs` + `Models/SessionDefinition.cs`. |
| Change **ramps / XP / pause penalty** | `SessionEngine.UpdateRampingValues :520` / final-XP block in `StopSession :320` / `XPPenalty :89`. |
| Change **what a session drives** | `SessionEngine.ApplySessionSettings :880` (start/defer) + `RestoreSettings :1168` (must mirror it). |
| Add a **built-in session** | A `.session.json` in `assets/sessions/` (preferred) or a static `Session` in `Session.cs` + `GetAllSessions`; add a card in `PresetsTabView.xaml` with the matching `Tag` id. |

---

## 11. Gotchas

1. **`SessionEngine` is NOT AI/OpenRouter** despite the root `CLAUDE.md` line. It's a deterministic
   timer. Don't go looking for a model call. (§0.)
2. **`MainWindow.ApplySessionSettings()` ≠ apply a session.** It's a UI-resync (`LoadSettings()`
   under `_isLoading`). The real apply is the private `SessionEngine.ApplySessionSettings`. Same name,
   very different jobs (`MainWindow.Presets.cs:1465` vs `SessionEngine.cs:880`).
3. **Presets don't live in files.** They're inside `settings.json` (`UserPresets`). The
   `.preset.json` / CustomPresets folder exist only for sharing + drag-drop; the carousel ignores
   them. `AssetPreset` is a *different* preset system entirely (asset blacklist).
4. **Dual session representation (#429).** `SessionSettings` is a flattened one-segment-per-feature
   view; `TimelineEvents` preserves every authored segment. `ToSessionSettings` only reads
   `startEvents.First()`, so multi-segment sessions lose extra segments in the flat view — always keep
   `TimelineEvents` in sync when editing session persistence, and remember `GetSetting<T>` must
   materialize `JsonElement`s after reload or every per-event setting silently resets.
5. **Ramps drive overlays directly, not settings (#471/#476).** Pink/spiral ramps call
   `App.Overlay.SetSustainedOverlayOpacity(...)` instead of writing `App.Settings.Current.*Opacity`,
   because that value auto-saves — a crash mid-session used to freeze the ramp maximum into
   `settings.json` permanently ("screen stays pink forever"). Don't "simplify" it back.
6. **Brain Drain is dead code in the engine.** All Brain Drain start/ramp branches are commented out
   ("up for rework due to performance issues", `SessionEngine.cs:452/589/695`). The session model
   still has the fields; nothing runs them from a session.
7. **`SessionManager.LoadAllSessions` runs once (startup).** A session written to disk by another
   feature (Graded Intake auto-drafts) is invisible until relaunch unless you call
   `RegisterExternallySavedSession` (`MainWindow.SessionIO.cs:78`, born from #614).
8. **The 4 built-in session cards are hard-coded XAML**, but their data comes from `GetSessionById`
   (`MainWindow.SessionIO.cs:1098`) which checks `SessionManager` first (JSON-backed built-ins) then
   falls back to `Session.GetAllSessions()`. If you rename an id in one place, fix both.
9. **`SessionCompleteWindow(Session,...)` ctor is dead** (`:41`) — both live call sites use the
   `SessionLog` ctor. Don't wire new code to the dead one.
10. **Post-session dialog waits out video teardown (#462).** `ShowSessionSummaryWhenClear` polls up
    to ~10s while a video `CloseAll` is in flight, then shows at `ApplicationIdle`. Don't force a
    cleanup there (risks a UI-thread LibVLC wedge) — just let it wait.
11. **Sessions bypass level gates** (`bypassLevelCheck:true`) and there is **no Patreon gate** on
    presets or sessions. Don't assume a feature is unlocked just because a session can run it.
12. **Pausing costs 100 XP each and is blocked in Lockdown.** Stop is also blocked in Lockdown
    (`MainWindow.Presets.cs:1395/1433`).
13. **Two preset selection surfaces** (`CmbPresets` dropdown + carousel) both mutate the same state;
    keep `RefreshPresetsDropdown` + `RefreshPresetsList` called together after any preset change.

---

## 12. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

- **State: mature and shipping.** No dedicated in-flight branch for the session/preset core; HEAD
  `95586020` on `fix/web-video-interruptions` (v6.5.0). The system is stable; recent churn is
  peripheral — catalogue share/import glue (`MainWindow.PresetIO.cs`), the #614
  `RegisterExternallySavedSession` hook for Graded-Intake drafts, and the #483 deferred-feature-start
  fix in the engine.
- **Known dead/vestigial** (see §11): the AI/OpenRouter claim in `CLAUDE.md` (engine has none), the
  Brain Drain branches (commented out), the second `SessionCompleteWindow` ctor (unreferenced), and
  the old dedicated session drop-zone (box repurposed as the Catalogue link, handlers retained).
- **Design tensions, not bugs**: the flat-vs-timeline dual representation (#429) and the
  ramp-drives-overlay-directly workaround (#471) are deliberate — documented so they aren't "fixed"
  blindly.
- **Cross-refs**: memory `catalogue-presets-sessions-share` (share/import — merged; round-trip
  play-test pending) and `intake-primer-doc` / `quiz-rework-intake-web-core` (the AI *authoring* of
  sessions that this engine merely plays).
- **Tests**: no dedicated xUnit coverage found for the session/preset core in this pass; the standing
  gate is play-test. The pure-ish seams worth a test if regressions appear are
  `TimelineSession.CalculateXP/CalculateDifficulty` and `SessionEngine.ElapsedTime` (anti-cheat).
- This primer is **new** and not previously committed.
