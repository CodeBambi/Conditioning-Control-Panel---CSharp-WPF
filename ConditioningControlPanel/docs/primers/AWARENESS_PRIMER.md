# AWARENESS — Feature Primer

> **Load this instead of re-exploring the feature.** One-load orientation for **Awareness** — the
> companion's ability to notice what you're doing and react to it. @-mention this file for coding or
> design sessions. §0 = what it is in one paragraph. **§1 = the disambiguation that will save you
> hours — "Awareness" is TWO different systems that share a brand. Read it first.** §2 = the
> `WindowAwarenessService` architecture. §3 = what it observes (window titles only — no screenshots).
> §4 = the awareness-preset system (which actually belongs to the *other* Awareness). §5 = **how it's
> invoked & how it touches the rest of the app** (the load-bearing section). §6 = the OCR /
> `GetActiveTextScreenRects` / #287 relationship. §7 = file map. §8 = where-to-change-X. §9 = gotchas.
> §10 = dated status.
>
> **Freshness.** Verified against source **2026-07-23** (branch `fix/web-video-interruptions`, HEAD
> `95586020`, v6.5.0). Every `file:line` below was read-verified when written and is git-verifiable,
> but line numbers drift — confirm with a quick read before quoting. **§10 is a dated snapshot —
> verify with `git log`/`git branch` before acting on it.**

---

## 0. What Awareness is, in one paragraph

Awareness is the companion's "she notices what you're up to" behaviour: the avatar spontaneously
comments when you switch to a game / shopping site / porn tab, and nags when you've been on the same
thing too long. The engine at its core is **`WindowAwarenessService`** (`App.WindowAwareness`), a
tiny (~740-line) polling service that every 1.5 s reads the **foreground window's title text**,
maps it to a coarse `ActivityCategory` (Gaming / Shopping / Media / Social / …) plus a display name
("Throne", "YouTube", "VS Code") via hard-coded keyword dictionaries, and raises `ActivityChanged`
/ `StillOnActivity` events. The **AvatarTube companion** is the sole consumer: it debounces on a
cooldown, then either asks the AI for an in-character line (`GetAwarenessReactionAsync`) or falls
back to a canned phrase, and giggles it. It captures **window titles only — never screenshots,
never OCR, and it deliberately never stores or logs the raw title** (only the resolved
category/name). It is **free for all users** (no Patreon gate) but requires an explicit consent
flag. **Do not confuse it with the "Awareness Engine" tab (§1), which is the entirely separate
premium keyword-trigger + Screen-OCR + preset system** that also wears the "Awareness" name.

### 0a. Awareness v2 (Train 2, 2026-08-06) — what the rest of this doc no longer describes

Everything below §2 describes System A **as it behaves with `UseAwarenessV2` off**. With that setting
on (the shipped default) `Services/Awareness/AwarenessObserver.cs` (`App.Awareness`) owns the
pipeline instead, and the differences that matter when reading the rest of this file are:

- `WindowAwarenessService` **still polls** — its `CurrentServiceName` / `CurrentActivityDuration`
  readouts are consumed elsewhere — but it **stops raising `ActivityChanged` / `StillOnActivity`**.
  Every consumer described in §5b/§5c/§5d is therefore dormant under v2, by design: one pipeline at a
  time, one mouth (the `ReactionArbiter`).
- The `{1, 5, 10}`-minute still-on nag (§2b) is gone; cumulative-dwell milestones at 30m/1h/2h/3h
  replace it, and a title flicker no longer resets the clock.
- A candidate must hold the foreground for **20 seconds** before a frame is cut, so alt-tab
  pass-throughs produce nothing and sustained churn produces exactly one `RapidCycling` event.
- Idle is **real input idle** (`GetLastInputInfo`), not "the title stopped changing", and there is a
  do-not-disturb layer (fullscreen with recent input, meeting app + live mic, typing burst, CCP's own
  mandatory-video / lock-card / DtRH surfaces).
- **The privacy posture inverts.** Private-browsing titles are a hard drop; a user deny list drops a
  window before anything is written; page titles are withheld from the frame unless the user
  allow-listed that app (the shipped allow list is empty); adult-cluster frames send only the cluster
  id. All of it runs *before* the `ActivityLedger` write, not after.
- **One privacy implementation, not two.** `AwarenessPrivacyRules.Evaluate` is the only place these
  rules live; `AwarenessObserverPolicy.EvaluatePrivacy` resolves the app's *identity* and then asks
  it. That matters because the shipped deny protection is three **group tokens** (`@passwords`,
  `@banking`, `@email-titles`) which mean nothing until that class expands them — a second matcher
  comparing them literally would show the user active chips that blocked nothing.
- **Pause is a real drop**, not a mute: while `AwarenessPause.IsPaused`, nothing is recorded and
  nothing is said. Process-lifetime only; it does not survive a restart.
- **The LLM leg has its own prompt.** Awareness does *not* go through `CompanionBrain.ReactAsync` or
  the multi-thousand-token chat prompt; `AwarenessReactionService` builds a dedicated ~800-token
  reaction prompt (persona digest + angle cards + frame projection + ban list) and sends it with
  `AiCallOptions.Reaction`, so `[AI-METER]` shows `purpose=reaction`. Moderation is unchanged — the
  full spine runs inside `IAiService.SendAsync` exactly as it does for chat. One consequence worth
  knowing: an awareness line does **not** enter the chat turn log.

`UseAwarenessV2 = false` restores every line of the legacy behaviour documented below — and so does
"v2 is configured but its observer failed to construct", because the legacy suppression asks
`AwarenessV2Routing.IsActive` (an arbiter is attached *and* v2 is enabled), never the setting alone.

---

## 1. DISAMBIGUATION — "Awareness" is two unrelated systems (read this first)

The word "Awareness" is overloaded across the codebase onto **two systems with almost no shared
code**. Getting these confused is the single biggest trap in this feature.

| | **A. Awareness Mode** (this doc's core) | **B. The "Awareness Engine" tab** |
|---|---|---|
| Code | `Services/UI/WindowAwarenessService.cs` (`App.WindowAwareness`) | `Services/KeywordTriggerService.cs` (`App.KeywordTriggers`) + `Services/ScreenOcrService.cs` (`App.ScreenOcr`) |
| What it senses | **Foreground window TITLE** (GetWindowText), polled 1.5 s | **On-screen text via OCR** + a **global keyboard hook** |
| Reaction | Avatar comment / "still-on" nag | Per-keyword actions (audio, flash, subliminal, mind-wipe, haptic, XP, highlight box, avatar comment) |
| Settings gate | `AwarenessModeEnabled` + `AwarenessConsentGiven` (`AppSettings.cs:3060`/`3071`) | `KeywordTriggersEnabled` + `ScreenOcrEnabled` |
| UI home | **Companion tab** → "Awareness Mode" toggle (`MainWindow.Patreon.cs:1140`) | **Awareness tab** → `Views/Tabs/AwarenessTabView` (`MainWindow.Awareness.cs`) |
| Patreon gate | **NONE — free for all** (`MainWindow.Patreon.cs:870-871`, `awarenessAvailable = true`) | **Premium** (`KeywordTriggerService.HasAccess()` → `HasPremiumAccess`, `:314-318`) |
| "Awareness presets" | does **not** use them | **owns** them — presets ARE `KeywordTriggerPreset`s (§4) |
| Self-exclusion / #287 | implicit only (its own titles never match a dict) | the real #287 machinery (OCR-rect exclusion, §6) |

**They meet in exactly two places:** (1) the shared "Awareness" branding + the `AwarenessIgnoreOwnUi`
/ `AwarenessLoopProtection*` settings that live in the same `AppSettings` region but are **B-only**;
and (2) both can ultimately make the avatar talk. Everything else is separate. This primer's core
(§2, §3, §5) is **System A**; §4 and §6 document System B where the task requires it, clearly
flagged.

> The comment at `App.xaml.cs:596` ("CCP window rect cache (used by Awareness Engine self-exclusion)")
> is itself a victim of this confusion — that cache is consumed by **ScreenOcrService (System B)**,
> not by `WindowAwarenessService`. See §6.

---

## 2. `WindowAwarenessService` — architecture & internals

One class, `WindowAwarenessService : IDisposable` (`WindowAwarenessService.cs:59`), namespace
`ConditioningControlPanel.Services`. No render path, no window, no audio. Two P/Invokes only:
`GetForegroundWindow` + `GetWindowText` (`:62-66`).

### 2a. The poll loop
- `Start()` (`:332`): bails if `_isRunning`/`_isDisposed`, then **hard-gates on
  `AwarenessModeEnabled == true && AwarenessConsentGiven == true`** (`:337-338`) — if either is
  false it logs and returns without starting. Otherwise spins a `DispatcherTimer` at **1.5 s**
  (`:344-347`, "fast polling for quick tab/app detection") and sets `_isRunning`.
- `OnPollTick` (`:477`): reads `GetActiveWindowTitle()` (`:549`, a 512-char `StringBuilder`).
  - If the title is unchanged from `_lastWindowTitle` and ≥ **5 min** (`IdleThresholdMinutes`,
    `:87`) have passed → sets `ActivityCategory.Idle` ("being idle").
  - If the title changed → `CategorizeWindow` (§3), and if the resolved category **or** detected
    name differs from current, also runs `AppClusterMap.Classify(windowTitle)` (`:512`, §4-note)
    and calls `SetActivity` (`:522`).
- `SetActivity` (`:522`): updates all `_current*` fields, computes `isNewService` (service changed
  vs just a new page), logs **the detected name only, never the title** (`:538-539`), raises
  `ActivityChanged`, and restarts the still-on milestone timer.
- `Stop()` (`:358`): stops both timers, resets category to `Unknown`. `Dispose()` (`:731`) calls
  `Stop()`.

### 2b. The "still-on" milestone timer
A **second** `DispatcherTimer` (`_stillOnTimer`) fires periodic "you're *still* on X?" nags at
milestones **{1, 5, 10} minutes** (`StillOnMilestonesMinutes`, `:408`). `RestartStillOnTimer`
(`:414`) resets the milestone index on every activity change and only arms when the current category
is neither `Unknown` nor `Idle`. Each tick raises `StillOnActivity` and advances to the next
milestone (`OnStillOnMilestoneTick`, `:454`). After the 10-min milestone it stops (no further nags
until the activity changes).

### 2c. Cooldown bookkeeping (owned here, enforced by the consumer)
`CanReact()` / `CanStillOnReact()` (`:376`/`:385`) compare `now − _lastReactionTime` against
`AwarenessReactionCooldownSeconds`; `MarkReaction()` / `MarkStillOnReaction()` (`:394`/`:402`) reset
the clocks. **The service exposes these but does not self-enforce** — AvatarTube calls `CanReact()`
then `MarkReaction()` around each reaction (§5). Note the fallback constant **90** in `CanReact`
(`:378`) is effectively dead: `AwarenessReactionCooldownSeconds` always has a value (default **10**,
clamp 10-600, `AppSettings.cs:3081-3085`), so the `?? 90` never triggers.

### 2d. `IsCategoryEnabled` is a no-op stub
`IsCategoryEnabled(category)` (`:725`) **always returns `true`** — the comment says "All categories
enabled - AI handles context appropriately." Per-category muting was removed; the checks that call
it in AvatarTube (`Reactions.cs:55`/`:142`) can therefore never suppress a reaction. Treat it as
vestigial.

---

## 3. What it observes — window titles, categorized (no screenshots, no OCR)

`CategorizeWindow(title)` (`:563`) lowercases the title and scans **six hard-coded keyword→display
dictionaries in priority order**, returning `(Category, DetectedName, ServiceName, PageTitle)`:

| Order | Dictionary | `:line` | → Category | Example match |
|---|---|---|---|---|
| 1 | `GamingApps` (~90 entries) | 93 | Gaming | "league of legends" → "League of Legends" |
| 2 | `LearningSites` | 238 | Learning | "wikipedia" → "Wikipedia" |
| 3 | `ShoppingSites` | 192 | Shopping | "throne" → "Throne", "amazon" → "Amazon" |
| 4 | `SocialApps` | 169 | Social | "discord" → "Discord" |
| 5 | `MediaSites` | 215 | Media | "youtube" → "YouTube"; "pornhub"→"adult content" |
| 6 | `WorkingApps` | 259 | Working | "cursor" → "Cursor" |
| 7 | generic browser sniff (chrome/firefox/edge/…) | 630 | Browsing | strips browser suffix → tab title |
| — | fallthrough | 638 | Unknown → "something" | |

- **Substring `Contains` matching** on the whole lowercased title — so a Discord tab open in a
  browser reads as Social, a browser window whose tab is "Amazon …" reads as Shopping, etc. First
  dictionary to hit wins (gaming beats everything).
- **Page/service extraction:** `ExtractPageNameWithService` (`:695`) splits on `" - "`, `" — "`,
  `" | "` and yields e.g. DisplayName `"CodeBambi on Throne"` + PageTitle `"CodeBambi"`.
  `ExtractBrowserTabName` (`:645`) strips known browser suffixes for the generic-browser path.
- **Privacy posture:** the raw title never leaves the service as a stored/logged value — only
  `DetectedName`/`ServiceName`/`PageTitle`/`Category` are surfaced and logged (see the class summary
  `:55-58` and the log-site comment `:538`). **Caveat worth knowing:** the extracted `ServiceName`
  and `PageTitle` (derived *from* the title) ARE passed to the LLM in `GetAwarenessReactionAsync`
  (§5c) — so page names like "CodeBambi's wishlist" do reach the AI provider even though the full
  title is never persisted. "Never logs titles" ≠ "title-derived text never leaves the machine".

### 3a. AppClusterMap — the fine-grained second layer
`AppClusterMap.Classify(title)` (`Services/AppClusterMap.cs:79`) is a **separate** static classifier
run alongside `CategorizeWindow` (called at `WindowAwarenessService.cs:512`). It maps the title to an
`appCluster` id (`game_competitive`, `site_doomscroll`, `site_eh`, …) and a bespoke `appId`
(`hades`, `obs`, `discord`) — longest-substring-wins, bespoke apps beat clusters. These ride along in
`ActivityChangedEventArgs.AppCluster`/`.AppId` (`:38-39`) and exist to feed **awareness-gated bark
rules**, not the avatar reaction. Data-driven: an external `app_clusters.json` in the companion-audio
folder overrides the embedded defaults (`EnsureLoaded`, `:38`). Same privacy rule — only the resolved
id is surfaced.

---

## 4. The "awareness-preset" system (belongs to System B, not to `WindowAwareness`)

This is the part most likely to mislead: **"awareness presets" have nothing to do with
`WindowAwarenessService`.** They are `KeywordTriggerPreset` objects (`Models/KeywordTriggerPreset.cs`)
that drive **System B** (keyword/OCR triggers).

- **Source of truth:** `Resources/AwarenessPresets/*.json` — shipped built-ins `trance.json`,
  `bimbo.json`, `puppy.json`, `chastity.json`. Each is a bundle: metadata + `avatarPromptTemplate` +
  `cannedPhrases` pools + a `triggers[]` list where each trigger is a keyword + a `matchType` +
  `cooldownSeconds` + an `actions[]` list (`PlayAudio`, `VisualEffect` [OverlayPulse / SubliminalFlash
  / MindWipe / Bubbles / …], `AvatarComment`, `Highlight`, `Haptic`). See `trance.json` for the
  canonical shape (keywords "relax"/"deeper"/"drop"/"empty"→MindWipe).
- **Merge on load:** `SettingsService.MergeBuiltInAwarenessPresets(settings)` (`SettingsService.cs:344`,
  called from load paths `:168`/`:226`/`:253`) reads the folder, appends new presets
  (`MasterEnabled = false`), skips ids in `RemovedBuiltInPresetIds`, and **refreshes triggers/phrases
  in place when a built-in's `Version` bumps** (`:418-451`). Because `App.KeywordPresets` doesn't
  exist yet at merge time, a version bump on an *installed* preset is queued into
  `PendingPresetReinstalls` (`:448`) and **drained in `App.OnStartup`** (`App.xaml.cs:1499-1509`),
  which calls `KeywordPresets.InstallPreset(id)` to push the new clones into the live trigger list.
- **UI:** the Awareness tab renders preset cards imperatively (`MainWindow.Awareness.cs:642`),
  activate/deactivate via `KeywordPresets.Install/UninstallPreset` (`:807-813`), edit via
  `AwarenessPresetDetailDialog`, author new ones via the "+ New Preset" tile (`:920`). Storage:
  `AppSettings.KeywordTriggerPresets` (`:4564`) + `RemovedBuiltInPresetIds` (`:4575`).

If your task is "make the companion notice a new *app*", that's **§3 dictionaries** (System A). If
it's "fire an effect when a *word* appears on screen", that's **§4 presets** (System B).

---

## 5. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

This is the load-bearing section — read it before wiring anything. System A's entire consumer surface
is **AvatarTube**; there is no SessionEngine / autonomy / remote / voice path into
`WindowAwarenessService` (unlike most CCP services).

### 5a. Start / stop / lifecycle
- **Constructed** once in `App.OnStartup`: `WindowAwareness = new WindowAwarenessService();`
  (`App.xaml.cs:1489`), static accessor declared at `App.xaml.cs:333`.
- **Started** from **AvatarTube wire-up**, not App startup: when the avatar window wires its events
  it calls `App.WindowAwareness.Start()` (`AvatarTubeWindow.xaml.cs:304`). `Start()` then self-gates
  on the consent+enabled flags (§2a), so the call is a no-op unless Awareness Mode is on.
- **Started on toggle:** enabling "Awareness Mode" in the Companion tab
  (`ChkAwarenessMode_Changed`, `MainWindow.Patreon.cs:1140`) sets **both**
  `AwarenessModeEnabled` and `AwarenessConsentGiven = true` (auto-consent, `:1150-1151`) then calls
  `App.WindowAwareness?.Start()` (`:1157`).
- **Disposed** on app shutdown: `WindowAwareness?.Dispose();` (`App.xaml.cs:3259`).
- **No explicit engine Start/Stop coupling** — Awareness runs whenever the flags are on and the
  avatar has wired it, independent of the dashboard Start/Stop button.

### 5b. Event consumers (all in AvatarTube)
- **Subscribe:** `AvatarTubeWindow.xaml.cs:301-302` (`ActivityChanged += OnActivityChanged`,
  `StillOnActivity += OnStillOnActivity`), immediately before the `Start()` call.
- **Unsubscribe:** `AvatarTubeWindow.Windowing.cs:855-856` (on teardown).
- **`OnActivityChanged`** (`AvatarTubeWindow.Reactions.cs:34`, `async void`, fully guarded for #386):
  marshals to UI thread; bails during startup cooldown or while a bubble is showing; checks
  `IsCategoryEnabled` (no-op, §2d) then `CanReact()` and calls `MarkReaction()`; then §5c.
- **`OnStillOnActivity`** (`Reactions.cs:123`): same guards but uses `CanStillOnReact()` /
  `MarkStillOnReaction()` and `CurrentActivityDuration`; 50/50 picks service-name vs page-title for
  variety; canned fallback includes the elapsed minutes.

### 5c. How a detection becomes a companion line
Inside both handlers: **if `AiChatEnabled` and `App.Ai.IsAvailable`**, it awaits
`App.Ai.GetAwarenessReactionAsync(displayName, category, serviceName, pageTitle)` (`AiService.cs:150`;
strategy dispatch `AiServiceStrategy.cs:74`; also `GetStillOnReactionAsync`). AI lines get
`GigglePriority` + a double-bounce; **on null/failure it falls back** to a canned category phrase
(`GetPhraseForCategory`) and a normal `Giggle` (`Reactions.cs:98-109`). The AI prompt path runs
through the usual `ModerationGuard` (all LLM I/O is moderated, `App.xaml.cs:1453-1462`). A **second,
manual** entry exists: `AvatarTubeWindow.ChatInput.cs:245` calls `GetAwarenessReactionAsync` directly
(a "what am I doing right now" chat path), reusing the same current detection.

### 5d. Barks (awareness-gated, System-A adjacent)
Per-mod `bark_rules.json` files carry rules with `"setting_eq": "AwarenessModeEnabled"`
(e.g. `builtin-*/bark_rules.json:3707`/`:3751`) — so some companion barks only arm while Awareness
Mode is on. The `AppCluster`/`AppId` ids from §3a are the fine-grained hook these rules can key on.
This is the only place System A's output flows outside AvatarTube's direct reaction.

### 5d-v2. Who is allowed to speak (Awareness v2's arbiter)
Everything in §5b–§5d describes the **legacy** path, which is still exactly what runs today. Awareness
v2 adds a single owner of ambient speech, `ReactionArbiter`
(`Services/Companion/Brain/ReactionArbiter.cs`, namespace `Services.Awareness`), because the two
paths above can *both* fire on one window change — a canned bark and an LLM quip about the same tab,
on independent cooldowns. That is the "two mouths" bug.

- **The switch is `AwarenessV2Routing.IsActive`**, and it is true only when an arbiter has been
  `Attach`ed *and* `AwarenessObserver.IsEnabled` (`UseAwarenessV2` + `AwarenessModeEnabled` +
  `AwarenessConsentGiven`). Configured-but-unwired deliberately means "legacy, unchanged" — the
  alternative would be a companion that goes silent instead of one that double-speaks.
- While it is active, `BarkService`'s `ActivityChanged`/`StillOnActivity` subscriptions and
  `AvatarTubeWindow`'s `OnActivityChanged`/`OnStillOnActivity` handlers **return immediately**. The
  arbiter re-raises the same bark triggers via `BarkService.RaiseAwarenessBark(frame)` (same trigger
  names, same context keys, so authored `bark_rules.json` rules are untouched) and delivers model
  lines via `AvatarTubeWindow.SpeakAwarenessLine`.
- **One shared cooldown ledger** across barks, LLM lines and System B's keyword `AvatarComment`
  (which reports itself with `RecordExternalLine`): 60s between any two lines, 90s between two LLM
  lines, 10 min between two lines about the same app, plus an hourly line budget from the intensity
  dial. Keyword lines are exempt from the LLM floor and the budget — the user configured them — but
  not from the 60s gap.
- **Cooldowns burn on delivery only.** A timeout (>8s), a provider failure, an empty or moderated
  reply, a `[PASS]`, or a line dropped for arriving after the user moved on all leave the budget
  untouched. An LLM leg that produces nothing falls back to a bark exactly once, so a frame yields at
  most one line, ever.
- Every submitted frame writes one `[AWARE] app=… score=… tier=… verdict=… gate=… lines_hr=…` line,
  the same way bark decisions write `[BARK]`.

### 5e. Patreon gating (the asymmetry)
- **System A (Awareness Mode): FREE.** `MainWindow.Patreon.cs:870-871` hard-codes
  `awarenessAvailable = true` with the comment "Awareness Mode settings (free for all users)". The
  `awarenessAvailable` variable is now vestigial — nothing can set it false. The only real gates are
  the consent+enabled flags and, for AI-quality reactions, `AiChatEnabled` + AI availability.
- **System B (keyword/OCR/presets): PREMIUM.** Toggling the master or OCR in the Awareness tab checks
  `KeywordTriggerService.HasAccess()` → `App.Patreon.HasPremiumAccess` (`KeywordTriggerService.cs:314`,
  enforced in the UI at `MainWindow.Awareness.cs:384`/`:435` with a Patreon-only MessageBox).

So: the avatar noticing your windows is free; the on-screen-keyword engine is premium. AI reactions
in either case need AI access (`HasAiAccess` via `AiChatEnabled`/provider availability), but that's an
AI gate, not an Awareness gate.

---

## 6. OCR / `GetActiveTextScreenRects` / #287 — the self-exclusion contract (System B)

`WindowAwarenessService` does **no OCR**. The OCR arm is **`ScreenOcrService`** (System B), and the
`GetActiveTextScreenRects` / #287 story is entirely about keeping *that* engine from reading CCP's own
on-screen text. It is documented here because the task asks for it and because the
`App.xaml.cs:596` comment mislabels the cache as "Awareness Engine self-exclusion".

- **The cache:** `App.GetCcpWindowRectsCached()` (`App.xaml.cs:629`) returns the physical-pixel screen
  rects of all visible CCP-owned windows, cached ~250 ms (`:596-599`). A **per-monitor span filter**
  (`:687`+) drops any window that fully covers a screen (full-screen overlay containers) so it doesn't
  swallow every external OCR word (#273).
- **#287 pattern:** full-screen overlays that DO carry readable CCP text — **Bouncing Text** and
  **Subliminals** — are intentionally left in screen capture, so instead of excluding the whole
  monitor, only their small live text rects are added back: `BouncingText.GetActiveTextScreenRects()`
  (`App.xaml.cs:676`) and `Subliminal.GetActiveTextScreenRects()` (`App.xaml.cs:683`). See
  `SUBLIMINALS_PRIMER.md` §6 for the subliminal side.
- **Consumer:** `ScreenOcrService.DispatchOcrResultsAsync` (`ScreenOcrService.cs:149-151`) — when
  `AwarenessIgnoreOwnUi` is on, it drops OCR word hits that intersect any cached CCP rect, preventing
  the app from reacting to its own output (the OCR feedback loop). This is what `AwarenessIgnoreOwnUi`
  (`AppSettings.cs:4526`) and `AwarenessLoopProtectionEnabled`/`Ms` (`:4538`/`:4549`) gate.
- **System A's self-exclusion is implicit, not coded:** `WindowAwarenessService` reads only the
  foreground title, and CCP's own window titles ("Conditioning Control Panel", dialogs) don't match
  any category dictionary, so they land in `Unknown`/"something" and produce no reaction. There is no
  explicit "skip my own window" branch in the service. Don't assume the rect cache protects System A —
  it doesn't touch it.

---

## 7. Where it lives — file map

| File | `:line` | Role |
|---|---|---|
| `Services/UI/WindowAwarenessService.cs` (~740) | `59` | **The whole System-A engine.** Poll loop, title read, category dictionaries, page extraction, still-on milestones, cooldown helpers, events. |
| `Services/AppClusterMap.cs` | `25`/`79` | Fine-grained cluster/app classifier layered on top; feeds awareness-gated barks (§3a). |
| `App.xaml.cs` | `333` / `1489` / `3259` | Declares `App.WindowAwareness`; constructs it in `OnStartup`; disposes on shutdown. |
| `App.xaml.cs` | `596` / `629` / `676`-`684` | CCP-window rect cache + `GetCcpWindowRectsCached` (**System B / OCR**, mislabeled comment). |
| `AvatarTube/AvatarTubeWindow.xaml.cs` | `299`-`305` | **Sole consumer** — subscribes + `Start()`s the service. |
| `AvatarTube/AvatarTubeWindow.Reactions.cs` | `34` / `123` | `OnActivityChanged` / `OnStillOnActivity` → AI or canned giggle. |
| `AvatarTube/AvatarTubeWindow.Windowing.cs` | `855`-`856` | Unsubscribe on teardown. |
| `AvatarTube/AvatarTubeWindow.ChatInput.cs` | `245` | Manual "what am I doing" reaction reuse. |
| `Services/AiService.cs` (+ `AIService/*`) | `150` | `GetAwarenessReactionAsync` / `GetStillOnReactionAsync` — the AI line generators. |
| `Models/AppSettings.cs` | `3060`/`3071`/`3081` | System-A settings: `AwarenessModeEnabled`, `AwarenessConsentGiven`, `AwarenessReactionCooldownSeconds`. |
| `MainWindow/MainWindow.Patreon.cs` | `870`-`878` / `1140` | Companion-tab "Awareness Mode" toggle + sync (System A). |
| **System B (shares the brand):** | | |
| `Services/KeywordTriggerService.cs` | `314` | Keyword engine + `HasAccess()` → `HasPremiumAccess`. |
| `Services/ScreenOcrService.cs` | `149` | OCR engine + `AwarenessIgnoreOwnUi` self-exclusion. |
| `Services/KeywordTriggerPresetService.cs` | — | Install/uninstall of "awareness presets". |
| `Services/Settings/SettingsService.cs` | `344` | `MergeBuiltInAwarenessPresets` — loads `Resources/AwarenessPresets/*.json`. |
| `Resources/AwarenessPresets/{trance,bimbo,puppy,chastity}.json` | — | Built-in keyword-trigger preset bundles. |
| `Views/Tabs/AwarenessTabView.xaml(.cs)` + `MainWindow/MainWindow.Awareness.cs` | `642` | The "Awareness Engine" tab UI + preset cards. |
| `Dialogs/AwarenessPresetDetailDialog.xaml(.cs)` | — | Per-preset editor. |
| `Models/AppSettings.cs` | `4526`/`4538`/`4549`/`4564`/`4575` | System-B settings: `AwarenessIgnoreOwnUi`, `AwarenessLoopProtection*`, `KeywordTriggerPresets`, `RemovedBuiltInPresetIds`. |

---

## 8. WHERE TO CHANGE X

| Want to… | Edit |
|---|---|
| Make the avatar recognize a new app/site | Add a keyword→display entry to the right dictionary in `WindowAwarenessService.cs` (`GamingApps` `:93`, `MediaSites` `:215`, etc.). Substring, case-insensitive; mind the priority order (gaming first). |
| Add a new activity category | Extend the `ActivityCategory` enum (`:13`), add a dictionary + a scan block in `CategorizeWindow` (`:563`), and a canned-phrase branch in AvatarTube's `GetPhraseForCategory`. |
| Change poll cadence / idle threshold | `_pollTimer` interval (`:346`, 1.5 s); `IdleThresholdMinutes` (`:87`, 5). |
| Change still-on nag timing | `StillOnMilestonesMinutes` (`:408`, {1,5,10}). |
| Change reaction cooldown | `AwarenessReactionCooldownSeconds` (`AppSettings.cs:3081`, default 10); consumers already call `CanReact`/`MarkReaction`. |
| Change what the AI is told | `GetAwarenessReactionAsync` prompt build (`AiService.cs:150` and the `AIService/*` providers). |
| Add a fine-grained cluster/app id (for barks) | `AppClusterMap` embedded defaults (`AppClusterMap.cs:106`/`129`) or ship an `app_clusters.json` override. |
| Add/edit a keyword-trigger **preset** (System B) | `Resources/AwarenessPresets/<id>.json` (bump `version` to force refresh); merge logic `SettingsService.cs:344`. |
| Change OCR self-exclusion | `GetCcpWindowRectsCached` (`App.xaml.cs:629`) + `ScreenOcrService.cs:149`; keep `GetActiveTextScreenRects` accessors in sync (#287, §6). |
| Change the Patreon gate | System A: it's free (`MainWindow.Patreon.cs:871`). System B: `KeywordTriggerService.HasAccess()` (`:314`). |

---

## 9. GOTCHAS

1. **"Awareness" is two systems (§1).** `WindowAwarenessService` (title polling, free) vs the
   Awareness-tab keyword/OCR engine (premium). Grepping "Awareness" pulls both. The
   `App.xaml.cs:596` "Awareness Engine self-exclusion" comment is about System B's OCR, not System A.
2. **Titles only — no screenshots, no OCR.** System A can only see what's in the window's title bar.
   A fullscreen game with an empty title, or a site whose brand isn't in a dictionary, reads as
   `Unknown`. If you expect it to "see" page content, that's OCR (System B).
3. **"Never logs titles" has a caveat.** The raw title is never stored/logged, but the *extracted*
   service/page name IS sent to the LLM (`GetAwarenessReactionAsync`, §3/§5c). Keep this in mind for
   any privacy claim.
4. **Consent auto-fires from the UI toggle.** `ChkAwarenessMode_Changed` sets `AwarenessConsentGiven`
   = the same value as enabled (`MainWindow.Patreon.cs:1151`). There is no separate consent dialog on
   that path — the toggle *is* the consent. `Start()` still double-gates on both flags (`:337`).
5. **`IsCategoryEnabled` always returns true (`:725`).** The category-filter checks in AvatarTube are
   dead — don't rely on them to suppress a category; add real filtering in `CategorizeWindow` or the
   consumer instead.
6. **The `?? 90` cooldown fallback (`:378`) is dead code.** The setting always has a value (default
   10). Change the default in `AppSettings.cs:3077`, not the fallback.
7. **AvatarTube is the only starter.** `Start()` lives behind avatar wire-up
   (`AvatarTubeWindow.xaml.cs:304`). If the avatar window never initializes, Awareness never runs even
   with the flags on. There is no App-startup or engine-Start path.
8. **`ExtractPageName` (`:685`) is unused.** A private wrapper around `ExtractPageNameWithService`
   with no callers — dead. Don't wire new code to it; call `ExtractPageNameWithService` (`:695`).
9. **Substring matching is greedy across the whole title.** "target" (Shopping) will match a window
   titled "on target for the deadline"; "origin" (Gaming launcher) matches many unrelated titles.
   Category priority (gaming→learning→shopping→social→media→working) can also produce surprises. Add
   distinctive keys, not short generic ones.
10. **Two `DispatcherTimer`s, both UI-thread.** Poll + still-on. `Stop()`/`Dispose()` null them both;
    the consumers already guard `async void` handlers for shutdown (#386, `Reactions.cs:34`).

---

## 10. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

> This section rots. Confirm with `git log --oneline -- Services/UI/WindowAwarenessService.cs` and
> `git branch` before acting.

- **State: mature and shipping.** No dedicated in-flight branch for Awareness. HEAD `95586020` on
  `fix/web-video-interruptions` (v6.5.0). `WindowAwarenessService` is stable and rarely touched;
  recent churn around the "Awareness" name is almost all on **System B** (keyword presets, OCR
  highlight, the Awareness tab) per the git log, not the title engine.
- **System A is free + consent-gated**; System B is premium (`HasPremiumAccess`). AI-quality
  reactions in both need AI access. The `awarenessAvailable` gate variable
  (`MainWindow.Patreon.cs:871`) is vestigial (always `true`).
- **Known dead/vestigial code** (see §9): `IsCategoryEnabled` no-op, `ExtractPageName` unused, the
  `?? 90` cooldown fallback, and the mislabeled `App.xaml.cs:596` comment. None are bugs users hit —
  documented so they aren't "fixed" blindly or trusted.
- **No dedicated unit tests** cover `WindowAwarenessService`. `CategorizeWindow`/`AppClusterMap` are
  the pure-function seams worth a test if regressions appear (both are string-in, tuple-out).
- **This primer is new** and not previously committed.

---

## 11. Build / run / dev

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```
Then: open the **Companion** tab, enable **Awareness Mode** (auto-consents + `Start()`s the engine),
make sure the avatar is enabled, and switch to a game / YouTube / a shopping site — after the startup
cooldown and the reaction cooldown (default 10 s) she'll comment. Watch `logs/` for
`WindowAwareness: Detected … (Category)` Serilog lines (the name, never the title). For the premium
keyword/OCR side, use the **Awareness** tab (§4/§6) — a separate, gated feature.
