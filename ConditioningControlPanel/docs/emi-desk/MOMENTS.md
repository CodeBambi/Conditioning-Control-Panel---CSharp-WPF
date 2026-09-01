# EMI Desk - MOMENTS (scoping lane, 2026-08-29)

The full moment catalogue for the desktop EMI. Three audiences:

- **writers** (`docs/emi-desk/lines/*.json`): read the `id`, `trigger`, `ctx`, `pool size`, `common pools`
  and `spice` columns. Never exceed a moment's spice ceiling. Section 2 is the common-pool brief.
- **build lane** (`Services/EmiDesk/*`, `Windows/EmiDesk/*`): the `odds` / `cooldownMs` / `priority`
  columns are the `moments` block of `Resources/emi/desk-lines.json`. Section 3 is the silence law.
- **hook agent**: section 4 only. Every fire site below was opened and verified unless marked
  `(UNVERIFIED)`.

**Count: 137 moments catalogued. 114 are shipped (90 v1 + the three onboarding nudges in 1.1b + the 17 promoted and 4 new moments of the wave-3 new-user pass); the other 23 are marked **DEFER** - their fire sites and
payloads are documented and verified, but they carry NO pool in the first wave and the hook agent
should skip them. Nothing needs re-researching to turn one on later. 12 common pools.**

> **Wave 3 (the new-user pass)** promoted 17 of the deferred rows and added 4 moments that did not exist before: `noMediaYet`, `firstFlashEver`, `firstVideoEver`, `firstSessionEver`. The promoted rows below now name the seam that actually ships, which is not always the one the DEFER row guessed at - `lockCardSolved` and the three host closes each moved.

> The DEFER cut was made on line-economy, not on quality: each moment costs 8-25 written lines, and the
> deferred 40 are the lowest-odds, most niche or most redundant of the set (secondary host open/close
> pairs, the rarer progression tickers, the second half of an on/off pair whose partner is in v1). The
> v1 90 keep the whole `ui` group, the whole `session` group, the core media loop, the ceremonies, and
> every HOLD moment (a HOLD is a safety rule, never optional).

## The one architectural finding

`Services/Companion/BarkService.cs` already is an app-wide event funnel. `WireSubscriptions()`
(line 599) subscribes to ~60 service events and 116 call sites end in one private method,
`Raise(string trigger, Action<BarkContext>? fill, bool guaranteed)` (line 987). **Roughly two thirds of
the moments below need no new plumbing at all** - they are a mirror line at the top of `Raise()` plus a
trigger->moment id map. See section 4.A. The remaining third are inline hooks in real methods, listed
individually.

Column notes:

- `ctx` uses the payload token names already locked in `VOICE.md`: `{target}` `{n}` `{level}`
  `{minutes}` `{streak}` `{name}`, plus moment-local extras named in the cell.
- `odds` in the tables below were AUTHORED at roughly half the Arcademy's, paying for a planned
  180s floor. The floor shipped at 45s (`LINES-AUDIT.md`) and never moved, so v6.8.7 doubled the
  spontaneous odds back (cap 1.0; holds and always-say ceremony untouched) and added the `ambient`
  clock beat. The shipped `desk-lines.json` is the authority, not these draft columns.
- `priority`: 1 = low ambient (drop it if anything else wants the bubble), 2 = normal, 3 = must-say
  (ceremony, exempt from the global floor).
- `pool` = how many SPECIFIC lines the writers should produce for that moment's own pool. 0 = wordless
  by rule, the moment draws only faces/chains.
- `spice` = ceiling for that moment (0 innocent only, 1 suggestive, 2 anything). A ceiling is not a
  target: `VOICE.md`'s 55/30/15 mix still applies underneath it.

---

## 1. THE MOMENTS

### 1.1 group `ui` - fired by the build lane itself (no service hooks)

Fire site for every row: `Services/EmiDesk/EmiDeskService.cs` / `Windows/EmiDesk/EmiDeskWindow*.cs`.
The ids marked LOCKED are named in `BRIEF.md` §Moments and must not be renamed.

| id | trigger | fire site | ctx | odds | cooldownMs | prio | pool | common pools | spice |
|---|---|---|---|---|---|---|---|---|---|
| `desktopFirstBoot` LOCKED | the very first time she is ever summoned outside the Arcademy | build lane: `EmiDeskWindow.Summon()` first-run branch | none | 1.0 once ever | - | 3 | 8 | - | 0 |
| `summoned` LOCKED | she is summoned (rail circle or hotkey) | build lane: `EmiDeskService.Summon()` | `{via}` rail\|hotkey, `{minutes}` since last dismiss | 0.25 | 45000 | 2 | 25 | common.attention, common.smallTalk | 2 |
| `dismissed` LOCKED | she is sent away (x, second click, hotkey) | build lane: `EmiDeskService.Dismiss()` | `{minutes}` she was out | 0.12 | 60000 | 1 | 15 | common.smallTalk | 1 |
| `ringOpen` LOCKED | body clicked, the six cards fan out | build lane: `EmiDeskWindow.Ring.cs` open | `{n}` cards, `{target}` top card | 0.08 | 180000 | 2 | 25 | common.attention | 2 |
| `ringDismissed` LOCKED | ring closed with no pick (Esc, click-away, second body click) | build lane: `EmiDeskWindow.Ring.cs` close | none | 0.05 | rides `ringOpen` | 1 | 15 | common.smallTalk | 1 |
| `ringPick` LOCKED | a card is chosen and the app navigates | build lane: `EmiDeskWindow.Ring.cs` pick -> `EmiTargets.Open()` | `{target}`, `{n}` lifetime opens, `pickIsTop` bool | 0.06 | 120000 | 1 | 25 | common.encourage | 2 |
| `arcademyFromRing` | the Arcademy card specifically (outranks `ringPick`) | same, when `target == "arcademy"` | none | 0.4 | 1/sitting | 2 | 15 | common.win | 1 |
| `pinAdded` LOCKED | user pins a card | build lane: `EmiDeskWindow.Ring.cs` pin | `{target}` | 0.5 | 2/sitting | 2 | 15 | common.win | 1 |
| `pinRemoved` **DEFER** | user unpins a card | same, unpin | `{target}` | 0.10 | 120000 | 1 | 8 | common.smallTalk | 0 |
| `suggestionIgnored3x` LOCKED | an auto-suggested card sat in the ring three opens running, untouched | build lane: `EmiSuggester` ring-fill bookkeeping | `{target}` | 0.33 | 1/sitting, once per card ever | 2 | 15 | common.tease | 1 |
| `resized` LOCKED | widget resized by more than a third either way | build lane: `EmiDeskWindow` resize commit | `{n}` new width, `bigger` bool | 0.15 | 300000 | 1 | 15 (8 bigger / 7 smaller) | common.smallTalk | 1 |
| `dragged3x` **DEFER** | third drag in one sitting (she may be in the way) | build lane: `EmiDeskWindow` drag end counter | `{n}` drags | 0.20 | 1/sitting | 1 | 8 | common.smallTalk | 0 |
| `petted` LOCKED | her body is clicked and held / stroked (the pet gesture) | build lane: `EmiDeskWindow` pet gesture | `{n}` pets this sitting, lifetime | 0.45 | 30000 | 2 | 25 | common.attention | 2 |
| `glassOffer` LOCKED | the glass glitch-flips to a channel preview | build lane: `EmiChannels` reveal | `{channel}` spiral\|video\|burst\|rain, `{target}` video name | 0.10 | 1 per 20-40 min | 2 | 25 (tag per channel) | common.tease | 2 |
| `effectFired` LOCKED | the glass was tapped and the effect went to the real screen | build lane: `EmiChannels` tap handler | `{channel}` | 0.20 | 2/sitting all kinds | 2 | 15 | common.win | 2 |
| `effectDeclined` LOCKED | the preview timed out untouched (~25s) | build lane: `EmiChannels` timeout | `{channel}` | 0 | - | 1 | 0 | common.hold | 0 |
| `askAnswered` LOCKED | a chip was pressed on an offer | build lane: `EmiOffers` | `{ask}`, `yes` bool | 0 (the ask's own yes/no reaction speaks) | - | 3 | 0 | - | - |
| `askIgnored` LOCKED | an offer hit its 40s give-up | build lane: `EmiOffers` | `{ask}` | 0 | - | 1 | 0 | common.hold | 0 |
| `askIgnored3x` **DEFER** | three offers ignored in a row; she stops offering for the sitting | build lane: `EmiOffers` ignore-streak latch | none | 0.25 | 1/sitting | 1 | 8 | common.smallTalk | 0 |
| `bedtimeSet` | the bedtime offer was accepted; glass offers mute till 06:00 | build lane: `EmiOffers` effect `bedtime` | none | 1.0 ceremony | - | 3 | 8 | common.lateNight | 0 |
| `bedtimeBroken` | she is summoned again after a bedtime was set, before 06:00 | build lane: `EmiDeskService.Summon()` bedtime branch | `{n}` skips running | 0.40 | 1/night | 2 | 8 | common.lateNight | 2 |

### 1.1b onboarding / teaching nudges (group `ui`, added 2026-08-29)

The gestures changed (owner call 2026-08-29): **left click on her body = a pat/pet. Right click on her body,
or the small pixel "doors" glyph that appears on hover = the ring of six cards. Right click on a card = pin
it so it stays in the ring.** Nobody reads a manual for a mascot, so she teaches the three gestures herself:
a handful of barks per track, then never again, always steering towards the petting. The pools are her first
impressions, so they are written as fishing, dares and stumbles, never as instructions: she never says
"click", "left click", "right click", "menu" or "button" the way a tooltip would ("the other button" is
allowed because that is how she would say it). Each track is a normal `moments` entry drawing only its own
pool (no common pool, `mix` 1.0), so a nudge is always on-message.

| id | trigger (as the build lane implements it) | ctx | odds | prio | pool | common pools | spice | stop condition |
|---|---|---|---|---|---|---|---|---|
| `petNudge` | ~25s after a summon, then every ~4 min while she is out | `{name}` display name | 1.0 | 2 | 12 | - | 1 | stops for good once the user has petted her 3 times (lifetime) |
| `ringNudge` | on the 2nd summon ever, then every ~6 min while she is out | none | 1.0 | 2 | 10 | - | 1 | stops for good once the ring has been opened twice (lifetime) |
| `pinNudge` | once per summon, on ring open, while the ring is showing | none | 1.0 | 2 | 8 | - | 1 | stops for good after the first pin ever |

Rules shared by all three tracks (the build lane owns the counters; they persist in the `emi` stats blob):

- **Hard cap: 6 fires per track, ever** (`limit: {per: "ever", max: 6}` in `desk-lines.json`), even if the
  stop condition is never met. After that the track is dead on this install.
- **Never within 90s of another nudge**, whichever track fired it. The nudges share one spacing clock.
- **Never during an ask**, and never while a full-screen feature is running (video, intake, lockdown,
  emergency exit, the Arcademy, DtRH, Loom, a session's mandatory feature). The silence law in section 3
  still applies on top: panic hold, attention checks and the avatar's voice all outrank a nudge.
- A nudge is priority 2, so a priority-3 ceremony (`levelUp`, `sessionCompleted`, ...) wins the slot and
  the nudge simply waits for its next tick; it is not rescheduled early.
- `petNudge` and `ringNudge` do not fire on the same tick; if both are due, the pet nudge goes first (the
  owner wants the petting learned before the cards) and the ring nudge takes the next slot.
- `pinNudge` only fires when the ring is actually open. It never fires on a ring that opened from the
  hover glyph less than 2s ago (give the cards time to fan) and never while a card is mid-press.
- Wording lock for these three pools: "cards", "the six", "my other side", "the other button", "squeeze"
  are her words. "doors" is NOT used in the lines: the checker's fence bans `door`/`doors` (the singular
  "door" is the Arcademy story fence) and the desk corpus has zero door lines, so the glyph's name stays
  in the code and the docs only.
- All three pools are spice ceiling 1, majority spice 0. Nothing here guilts; she is fishing, not wounded.

### 1.2 group `companion` - the avatar arbiter

| id | trigger | fire site | ctx | odds | cooldownMs | prio | pool | common pools | spice |
|---|---|---|---|---|---|---|---|---|---|
| `avatarSpeaking` | the tube bubble is live (or audio is playing) | `Services/Companion/BarkService.cs:143` `BarkSpoken` (raised in `Speak` `:1800`, i.e. BEFORE the audio starts) + `AvatarTube/AvatarTubeWindow.Speech.cs:238` `Giggle` / `:320` `GigglePriority`. **There is no speaking-started/stopped event anywhere** - only the poll `AvatarTubeWindow.Speech.cs:1638` `IsSpeakingAudio`. See 4.E | none | 0 HOLD | +20s tail | 3 | 0 | common.hold | 0 |
| `avatarMuted` LOCKED | the summon prompt was answered "mute" (or `EmiDeskMuteAvatar` silently muted her) | build lane: `EmiDeskService` mute arbitration | none | 1.0 | 1/session | 2 | 15 | common.smallTalk | 1 |
| `avatarKept` LOCKED | the summon prompt was answered "keep the avatar" | build lane: same | none | 0.6 | 1/session | 2 | 8 | common.smallTalk | 1 |
| `takeoverStarted` | Bambi Takeover switched on | `Services/EmiDesk/EmiDeskService.cs` `EnabledChanged` handler, the `enabled` branch (partner of `takeoverEnded`) | none | 0.15 | 300000 | 1 | 8 | common.deeper | 1 |
| `takeoverEnded` | Takeover switched off | same, false | `{minutes}` | 0.25 | 300000 | 1 | 8 | common.afterEffect | 2 |
| `sheListeningOn` | the mic starts listening | `Services/Speech/SpeechService.cs:105` `ListeningChanged` (true); wake variant `Services/Speech/SherpaWakeService.cs:49` | none | 0.20 | 600000 | 1 | 8 | common.attention | 2 |
| `awarenessReaction` | the awareness arbiter decided the avatar speaks about the active window | `Services/Awareness/AwarenessReactionService.cs` -> `BarkService` awareness entry point (:~250) | none | 0 HOLD | +20s tail | 3 | 0 | common.hold | 0 |
| `companionLevelUp` | the AI companion levelled | bridge row `CompanionLevelUp` (`Services/Companion/BarkService.cs:755`) | `{level}` | 0.30 | 1/launch | 2 | 8 | common.win | 1 |

### 1.3 group `feature` - a feature was opened, started or finished

| id | trigger | fire site | ctx | odds | cooldownMs | prio | pool | common pools | spice |
|---|---|---|---|---|---|---|---|---|---|
| `featureOpened` | any tab / door opened (the generic catch-all) | `MainWindow/MainWindow.TabNavigation.cs:106` `ShowTab(string tab)`, at the existing bark hook (line 158-162) | `{target}` tab key (settings=Home, studio, presets, haptics, companion, bambitakeover, shelistening, awareness, play, deeper, exclusives, gradedintake, lockdown, blinktrainer, remotecontrol, availablesubjects, discord, spiral, quests, achievements, enhancements, programs, leaderboard, assets, appsettings) | 0.06 | 90000 | 1 | 25 (token lines) | common.attention | 2 |
| `featureOpenedRepeat` | the same target opened a 3rd time in one sitting | same, counter in `EmiState` | `{target}`, `{n}` | 0.20 | 1/target/sitting | 1 | 8 | common.tease | 2 |
| `flashesStarted` | flashing turned on | `Services/Flash/FlashService.cs:385` `Start()` | none | 0.20 | 180000 | 2 | 15 | common.deeper | 2 |
| `flashesStopped` | flashing turned off | `Services/Flash/FlashService.cs:407` `Stop()` | `{minutes}` | 0.12 | 180000 | 1 | 8 | common.afterEffect | 1 |
| `flashClicked` **DEFER** | the user clicked a flash image | `Services/Flash/FlashService.cs:196` `FlashClicked` event | none | 0.05 | 120000 | 1 | 8 | common.tease | 2 |
| `videoRunning` LOCKED | a mandatory video is playing and she is sitting on top of it | `Services/Video/VideoService.cs:387` `VideoStarted` (arms); bark fires >=8s in, never in the last 5s | `{target}` file name, `{minutes}` length | 0.35 | 1 per video | 2 | 25 | common.attention | 2 |
| `videoEnded` LOCKED | the video finished (pass or fail, same pool) | `Services/Video/VideoService.cs:388` `VideoEnded`; richer ctx at `:5240` `EndCurrentVideo` | `{minutes}` watched, `passed` bool | 0.25 | 1 per video | 2 | 15 | common.afterEffect | 2 |
| `attentionCheckShown` | an attention target is on screen | `Services/AttentionCheckService.cs` `Fire()` (via `:115 FireNow()`) and `Services/Video/VideoService.cs:5024` `SpawnTarget()` | none | 0 HOLD | until resolved | 3 | 0 | common.hold | 0 |
| `attentionCheckPassed` | the check was clicked in time | `Services/AttentionCheckService.cs:83` `OnPass` | none | 0.20 | 120000 | 1 | 15 | common.win | 2 |
| `attentionCheckFailed` | the check was missed | `Services/AttentionCheckService.cs:84` `OnFail` | `{n}` fail count | 0.15 | 120000 | 1 | 15 | common.loss | 1 |
| `subliminalsStarted` | subliminals turned on | `Services/Subliminal/SubliminalService.cs:112` `Start()` | none | 0.15 | 180000 | 1 | 8 | common.deeper | 2 |
| `subliminalShown` **DEFER** | a subliminal card was displayed | `Services/Subliminal/SubliminalService.cs:91` `SubliminalDisplayed` | `{n}` count | 0.02 | 300000 | 1 | 8 | common.deeper | 2 |
| `overlaySpiralUp` | a spiral overlay went up (any source, hers or the app's) | `Services/Notifications/OverlayService.cs:842` `ShowOverlayTimed(kind,...)` / `:1038` `ShowOverlaySustained` when `kind=="spiral"` | `{channel}` kind, `{n}` seconds | 0.20 | 120000 | 2 | 15 | common.deeper | 2 |
| `brainDrainOn` | brain drain blur came up | `Services/Notifications/OverlayService.cs:2129` `StartBrainDrainBlur(intensity, melt)`; trigger event `Services/LockCard/BrainDrainService.cs:120` `BrainDrainTriggered` | `{n}` intensity, `melt` bool | 0.25 | 180000 | 2 | 15 | common.deeper | 2 |
| `brainDrainOff` | the blur came down | `Services/Notifications/OverlayService.cs` `StopBrainDrainBlur()`, gated on the run stamp set beside the `brainDrainOn` fire so the no-op stops stay silent | `{minutes}` | 0.15 | 180000 | 1 | 8 | common.afterEffect | 2 |
| `mindWipeTriggered` **DEFER** | a mind wipe fired | `Services/LockCard/MindWipeService.cs:95` `MindWipeTriggered` | none | 0.25 | 300000 | 2 | 8 | common.afterEffect | 2 |
| `lockCardSolved` | a lock card was completed | `Services/LockCard/LockCardService.cs` `NotifyCompleted()` - NOT the bark trigger, which only fires on the pool-bark fallback branch | `{n}` tries (mistakes + 1; omitted on a clean card) | 0.25 | 180000 | 1 | 8 | common.win | 2 |
| `bubblesStarted` **DEFER** | the bubble minigame turned on | `Services/BubbleService.cs:195` `Start(bypassLevelCheck, frequency)` | `{n}` frequency | 0.15 | 300000 | 1 | 8 | common.smallTalk | 1 |
| `bubbleCountWon` | the counting minigame was passed | `Services/BubbleCountService.cs:50` `GameCompleted` | `{n}` xp | 0.30 | 120000 | 2 | 15 | common.win | 1 |
| `bubbleCountLost` | the counting minigame was failed | `Services/BubbleCountService.cs:51` `GameFailed` (six raise sites, all bare `EventArgs.Empty`) | none | 0.25 | 120000 | 2 | 15 | common.loss | 0 |
| `blinkTrainerStarted` | the blink trainer started or stopped | `Services/BlinkTrainerService.cs:66` `StateChanged` (read `IsRunning`) | `running` bool, `{minutes}` duration | 0.20 | 300000 | 1 | 8 | common.deeper | 2 |
| `keywordTriggerFired` **DEFER** | a keyword trigger fired | `Services/KeywordTriggerService.cs:55` `TriggerFired` | `{target}` keyword | 0.10 | 240000 | 1 | 8 | common.tease | 2 |
| `memoryRecalled` **DEFER** | the companion surfaced something she remembers about you | `Services/Companion/BarkService.cs` trigger `PersistentMemoryRecalled` | none | 0.15 | 300000 | 1 | 8 | common.attention | 2 |
| `mantraCompleted` | a mantra was completed | `Services/MantraService.cs:23` `MantraCompleted` | none | 0.20 | 180000 | 1 | 8 | common.win | 2 |
| `mantraStreakBroken` | the mantra streak broke | bridge row `MantraStreakBroken` (`Services/Companion/BarkService.cs:834`) | none - `MantraService.BreakStreak()` zeroes `Streak` before it invokes, so `{streak}` cannot be read and its one line is skipped | 0.20 | 180000 | 1 | 8 | common.loss | 0 |
| `hapticsConnected` | a toy connected | `Services/Haptics/HapticService.cs` `ConnectionChanged` relay, the true edge only | `{target}` device name (`ConnectedDevices[0]`, nickname first) | 0.30 | 1/launch | 2 | 8 | common.smallTalk | 2 |
| `arcademyOpened` | the Arcademy launched | `Services/Arcademy/ArcademyHostService.cs:146` `Launch()` (gate `:127 DoorAvailable`) | none | 0.40 | 1/sitting | 2 | 15 | common.win | 1 |
| `arcademyClosed` | the Arcademy closed | `Services/Arcademy/ArcademyHostService.cs` `DisposeAll()` - the one funnel all four exits reach, not `CloseActive()` | `{minutes}` (stamp set beside the `arcademyOpened` fire) | 0.20 | 1/launch | 1 | 8 | common.afterEffect | 1 |
| `dtrhOpened` | Down the Rabbit Hole launched | `Services/Chaos/DtrhHostService.cs:70` `Launch(testMode)` | none | 0.30 | 1/sitting | 1 | 8 | common.deeper | 1 |
| `dtrhClosed` | Down the Rabbit Hole closed (a run ended or the host was shut) | `Services/Chaos/DtrhHostService.cs` `DisposeAll()` - the one funnel all four exits reach, not `CloseActive()` | `{minutes}` is passed but no line reads it; the pool's one tokened line wants `{n}` xp, and the run total goes to BarkService and never reaches the host, so that line is skipped | 0.30 | 1/launch | 1 | 8 | common.win | 1 |
| `loomOpened` **DEFER** | the Loom launched | `Services/Chaos/LoomHostService.cs:30` `Launch()` | none | 0.25 | 1/sitting | 1 | 8 | common.smallTalk | 1 |
| `fypOpened` | the For You feed opened | `Services/Fyp/FypHostService.cs:67` `Launch()` (routed via `ShowTab("fyp")`) | none | 0.25 | 1/sitting | 1 | 8 | common.tease | 2 |
| `fypClosed` | the feed closed | `Services/Fyp/FypHostService.cs` `Close()` (also the ProcessFailed, window-Closed and panic path) | `{minutes}` (stamp set beside the `fypOpened` fire) | 0.20 | 1/launch | 1 | 8 | common.afterEffect | 2 |
| `intakeOpened` | Graded Intake launched | `Services/Quiz/IntakeHostService.cs:93` `Launch(testMode, duckMainWindow)` | none | 0.20 | 1/sitting | 1 | 8 | common.attention | 1 |
| `intakeRunning` | intake is up and a question may be on screen | `Services/Quiz/IntakeHostService.cs:83` `IsActive` | none | 0 HOLD | until `CloseActive` | 3 | 0 | common.hold | 0 |
| `intakeClosed` | intake closed | `Services/Quiz/IntakeHostService.cs:164` `CloseActive()`; result detail at `BarkService` `"QuizCompleted"` (:817, has `passed`/`perfect`) | `passed` bool | 0.25 | 1/sitting | 1 | 8 | common.win + common.loss | 1 |
| `justDropOpened` **DEFER** | the JustDrop shop opened | `Services/JustDrop/JustDropHostService.cs:75` `LaunchShop()` | none | 0.25 | 1/sitting | 1 | 8 | common.smallTalk | 1 |
| `goonMatchEnded` **DEFER** | a Goon Game match finished | `Services/GoonGame/GoonMatchService.cs:264` `MatchEnded` (phases at `:235 PhaseChanged`) | `won` bool | 0.25 | 1/sitting | 1 | 8 | common.win + common.loss | 2 |
| `enhancementApplied` | a Deeper enhancement was applied | bridge row `EnhancementApplied` (`Services/Companion/BarkService.cs:379`) | `{target}` enhancement id, lowercased | 0.20 | 180000 | 1 | 8 | common.deeper | 2 |
| `contentPackInstalled` | a content pack finished installing | `Services/Content/ContentPackService.cs`, BOTH `PackDownloadCompleted` invoke sites (download and local zip) | `{target}` pack display name, lowercased | 0.40 | 1/launch | 2 | 8 | common.win | 1 |
| `noMediaYet` | the library is empty and the app is still on local media | `Services/Flash/FlashService.cs` + `Services/Video/WallpaperService.cs`, beside each `App.OfferRemoteMediaSource` call; and `EmiDeskService` 7s behind the summon greeting. Probe: `EmiOffers.LibraryIsEmpty()` | none | 0.90 | 1800000 + 1/launch | 2 | 10 | common.encourage | **0** |
| `firstFlashEver` | the first flash image this account has ever been shown | `Services/Flash/FlashService.cs`, after `App.Achievements?.TrackFlashImage()`, gated `TotalFlashImages == 1` | none | 0.80 | 1/ever | 2 | 6 | - | 1 |
| `firstVideoEver` | the first video this account has ever started | `Services/Video/VideoService.cs` `StartVideoPlayback()`, at the top so both engines carry it, gated `TotalVideoMinutes <= 0` | none | 0.80 | 1/ever | 2 | 6 | - | 1 |
| `emergencyExitOpened` | the emergency-exit game opened | `Services/EmergencyExit/EmergencyExitHostService.cs:87` `Open()`; existing entry `BarkService.NotifyEmergencyExitOpened` (:418) | `{target}` game, `{n}` attempt | 0 HOLD | - | 3 | 0 | common.hold | 0 |
| `lockdownArmed` | lockdown activated | `Services/Haptics/LockdownService.cs:45` `LockdownActivated` | `{minutes}` | 0.30 one line at arm only | 1/lockdown | 2 | 8 | - | **0** |
| `lockdownCountdown` | the lockdown countdown is running | `Services/Haptics/LockdownService.cs:47` `CountdownTick` | - | 0 HOLD | whole countdown | 3 | 0 | common.hold | **0** |
| `lockdownEnded` | lockdown released | `Services/Haptics/LockdownService.cs:46` `LockdownDeactivated` | `{minutes}` | 0.30 | 1/lockdown | 2 | 8 | common.afterEffect | **0** |
| `possessionRungChanged` **DEFER** | Possession stepped up or down a rung | `Services/Possession/PossessionDirector.cs:98` `RungChanged` | `{n}` rung | 0.15 | 300000 | 1 | 8 | common.deeper | 2 |
| `descentFusePhase` **DEFER** | the Descent fuse changed phase | `Services/Descent/DescentCountdownService.cs:158` `PhaseChanged` (zero at `:173 ZeroReached`) | `{target}` phase | 0.20 | 1/phase | 1 | 8 | - | **0** (ceremony content; never spoil it) |
| `remoteControllerConnected` **DEFER** | a remote controller connected / a remote session started | `Services/RemoteControlService.cs:73` `ControllerConnectedChanged`, `:83` `SessionStarted` | none | 0.30 | 1/session | 2 | 8 | common.attention | 2 |
| `panicPressed` | the panic key was pressed | `MainWindow/MainWindow.xaml.cs:1408` `HandlePanicKeyPress()` (and `:1552` `App.Bark?.NotifyPanic()`); Arcademy branch `Services/Arcademy/ArcademyHostService.cs:397` `HandlePanicPress()` | none | 0 HOLD | 300s silence after | 3 | 0 | common.hold | **0** |

### 1.4 group `session` - the engine and the session runtime

| id | trigger | fire site | ctx | odds | cooldownMs | prio | pool | common pools | spice |
|---|---|---|---|---|---|---|---|---|---|
| `engineStarted` | the user hit Start (features go live, no Session preset) | `MainWindow/MainWindow.StartStop.cs:177` `StartEngine(bool systemInitiated)` | `systemInitiated` bool | 0.30 | 180000 | 2 | 25 | common.deeper | 2 |
| `engineStopped` | the user hit Stop | `MainWindow/MainWindow.StartStop.cs:313` `StopEngine()` (core at `:330 StopEngineCore`) | `{minutes}` | 0.20 | 180000 | 1 | 15 | common.afterEffect | 1 |
| `rampStepUp` | the ramp stepped the intensity up | `Services/Session/SessionEngine.cs:591` `UpdateRampingValues(elapsedMinutes, totalMinutes)` - the only genuinely STEPPED value is the bubble frequency ramp at `:663-677` (`rampSteps = timeSinceBubbleStart / 5`); everything else is a per-second lerp, so hook the bubble step only. Engine-mode equivalent: `MainWindow/MainWindow.StartStop.cs:501` `RampTimer_Tick` | `{n}` step | 0.15 | 300000 | 1 | 8 | common.deeper | 2 |
| `sessionFeatureArrived` | the session turned a feature on by itself at its scheduled minute | `Services/Session/SessionEngine.cs:690` `CheckDelayedFeatures(elapsedMinutes)` - generic queue fires `pending.Start()` at `:704`; hardcoded arrivals at `:715` pink filter, `:727` spiral, `:758` bubbles, `:779` corner gif | `{target}` `pending.Name` (mind wipe, flash, lock cards, bouncing text, mandatory videos), `{n}` start minute | 0.20 | 120000 | 1 | 15 | common.deeper | 2 |
| `sessionStarted` | a Session preset started | `Services/Session/SessionEngine.cs:147` `StartSessionAsync(Session)` / event `:26 SessionStarted` | `{target}` session name, `{minutes}` total | 0.40 | 1/session | 2 | 25 | common.deeper | 2 |
| `firstSessionEver` | the first session this account has ever started | `Services/Session/SessionEngine.cs`, after `App.Achievements?.TrackSessionStart()`, gated `TotalSessionsStarted == 1` | none | 0.90 | 1/ever | 2 | 6 | - | 1 |
| `sessionPaused` | the session was paused | `Services/Session/SessionEngine.cs:435` `PauseSession()` - **no event, inline hook** | `{n}` pause count, `{n}` xp penalty | 0.20 | 120000 | 1 | 8 | common.encourage | **0** |
| `sessionResumed` | the session was resumed | `Services/Session/SessionEngine.cs:485` `ResumeSession()` - **no event, inline hook** | `{minutes}` remaining | 0.25 | 120000 | 1 | 8 | common.encourage | 2 |
| `sessionPhaseChanged` | the session moved to the next phase | `Services/Session/SessionEngine.cs:23` `PhaseChanged` (`SessionPhaseChangedEventArgs.Phase/PhaseIndex`) | `{target}` phase name, `{n}` index | 0.10 | 120000 | 1 | 15 | common.deeper | 2 |
| `sessionHalfway` | the session crossed 50% | `Services/Session/SessionEngine.cs:24` `ProgressUpdated` (`ProgressPercent`) | `{minutes}` left | 0.30 | 1/session | 2 | 8 | common.encourage | 2 |
| `sessionLastMinute` | under 60s remaining | same, `ProgressUpdated` | none | 0.30 | 1/session | 2 | 8 | common.encourage | 2 |
| `sessionCompleted` | the session ran to the end | `Services/Session/SessionEngine.cs:25` `SessionCompleted` (`Session`, `Duration`, `XPEarned`) | `{target}`, `{minutes}`, `{n}` xp | 0.50 ceremony | - | 3 | 25 | common.win | 2 |
| `sessionAbandoned` | the session was stopped early | `Services/Session/SessionEngine.cs:288` `StopSession(completed:false)`, the `else` branch at `:415`. **`SessionStopped` (:352) fires for BOTH outcomes and carries no `completed` flag** - the mirror cannot tell an abort from a finish, so this one needs the inline hook | `{minutes}` done of `{minutes}` | 0.20 | - | 1 | 8 | common.loss | **0** |

### 1.5 group `progression`

| id | trigger | fire site | ctx | odds | cooldownMs | prio | pool | common pools | spice |
|---|---|---|---|---|---|---|---|---|---|
| `levelUp` | the user levelled | `Services/Progression/ProgressionService.cs:13` `LevelUp` (int) | `{level}` | 1.0 ceremony | - | 3 | 25 | common.win | 2 |
| `xpBigAward` | a single XP award over the "that was a lot" threshold | `Services/Progression/ProgressionService.cs:35` `XPAwarded` (`XpAward(Amount, Source, LeveledUp)`); suppress when `LeveledUp` (levelUp wins) | `{n}` amount, `{target}` source | 0.15 | 300000 | 1 | 15 | common.win | 1 |
| `achievementUnlocked` | an achievement popped | `Services/Progression/AchievementService.cs:59` `AchievementUnlocked`, raised at `:891` inside `TryUnlock` on the UI thread. Correctly silent when `SuppressPopups` (early return `:877`). **The bark ctx carries the id, not the name (`:768`) - subscribe to the event directly for `{target}`** | `{target}` name | 0.50 | 60000 | 2 | 25 | common.win | 1 |
| `questCompleted` | a daily/weekly quest completed | `Services/Progression/QuestService.cs:103` `QuestCompleted` | `{target}` quest title | 0.30 | 120000 | 2 | 15 | common.win | 1 |
| `questsRefreshed` **DEFER** | the quest board rolled over | `Services/Progression/QuestService.cs:105` `QuestsRefreshed` | `{n}` quests | 0.15 | 1/day | 1 | 8 | common.smallTalk | 0 |
| `streakMilestone` | a login-streak milestone (7/14/30/60/100/365) | `AvatarTube/AvatarTubeWindow.Speech.cs:2448` (`App.Bark.NotifyStreakMilestone`); ledger `Models/AchievementProgress.cs:608` `AwardDeferredStreakBonus()` | `{streak}` | 0.80 ceremony | - | 3 | 15 | common.win | 2 |
| `streakKept` | the daily streak ticked up (not a milestone) | `Models/AchievementProgress.cs:249` `UpdateDailyStreak()` | `{streak}` | 0.25 | 1/day | 2 | 15 | common.win | 1 |
| `streakBroken` | the daily streak was lost | `Models/AchievementProgress.cs:407` `ResolveDeferredStreakBreak(reason)` | `{streak}` lost, `{target}` reason | 0.30 | 1/day | 2 | 15 | common.loss, common.encourage | **0** |
| `skillUnlocked` | a skill-tree node was unlocked | `Services/Progression/SkillTreeService.cs:32` `SkillUnlocked` (string id) | `{target}` skill id | 0.30 | 120000 | 2 | 8 | common.win | 1 |
| `pinkRushStarted` | Pink Rush began | `Services/Progression/SkillTreeService.cs:37` `PinkRushStarted` | none | 0.35 | 1/rush | 2 | 8 | common.deeper | 2 |
| `pinkRushEnded` | Pink Rush ended | bridge row `PinkRushEnded` (`Services/Companion/BarkService.cs:768`) | none - a rush is a flat 60s, so `{minutes}` would always read "1 minutes" and its one line is skipped | 0.20 | 1/rush | 1 | 8 | common.afterEffect | 2 |
| `luckyProc` | a lucky proc landed | bridge row `LuckyProc` (`Services/Companion/BarkService.cs:764`) | none | 0.10 | 300000 | 1 | 8 | common.win | 1 |
| `punchCardCompleted` **DEFER** | an intake punch card filled | `Services/Progression/IntakePunchCardService.cs:81` `PunchCardCompleted` | none | 0.40 | 1/card | 2 | 8 | common.win | 1 |
| `leaderboardViewed` **DEFER** | the leaderboard was opened | `Services/Companion/BarkService.cs:427` `NotifyLeaderboardViewed(rank, total)`; source `Services/Progression/LeaderboardService.cs:59` `LeaderboardUpdated` | `{n}` rank, `{n}` total | 0.20 | 1/sitting | 1 | 8 | common.smallTalk | 1 |
| `programDayCompleted` **DEFER** | a Program day was completed | `Services/Program/ProgramService.cs:139` `DayCompleted` (`ProgramDayEventArgs`) | `{n}` day | 0.30 | 1/day | 2 | 8 | common.win | 1 |
| `programLapsed` **DEFER** | the Program lapsed (a day was missed and the chapter reset) | `Services/Program/ProgramService.cs:142` `ProgramLapsed` (`ProgramLapsedEventArgs`); day-miss at `:140 DayMissed` | `{n}` days | 0.20 | 1/day | 1 | 8 | common.encourage | **0** |
| `seasonRolled` **DEFER** | the season rolled over and a recap exists | `Services/Progression/SeasonRecapService.cs:168` `CaptureAndRollover(newSeasonKey)` | `{target}` season | 0.50 | 1/season | 2 | 8 | common.win | 1 |
| `roadmapStepDone` **DEFER** | a roadmap step / track / badge landed | `Services/RoadmapService.cs:43` `StepCompleted`, `:44 TrackUnlocked`, `:45 BadgeEarned` | `{target}` | 0.20 | 120000 | 1 | 8 | common.win | 1 |

> **Prestige**: searched (`grep -rn "Prestige" Services/ Models/`) and there is **no prestige system in the
> desktop app** - the closest is `Services/Descent/DescentMigrationService.cs` (the curve recurve ceremony,
> `:40 StageCeremonyDue`). No `prestige` moment is defined. The Arcademy ticket/token wallet
> (`../../CCP.Core/Services/Arcademy/ArcademyEconomy.cs`) is **pure static functions with no events** and lives inside the
> web host, so no desktop economy moment exists either. Both would need new seams. (UNVERIFIED that the
> owner wants them at all.)

### 1.6 group `time`

All rows in this group are owned by an EmiDesk clock timer (60s tick in `EmiDeskService`); there is no
time-of-day event anywhere in the app. `BarkService` exposes the raw value as the `local_hour` condition
(`Services/Companion/BarkService.cs:1345`), which is the precedent to copy.

| id | trigger | fire site | ctx | odds | cooldownMs | prio | pool | common pools | spice |
|---|---|---|---|---|---|---|---|---|---|
| `lateNight` LOCKED-ish | first summon or ring open after local midnight | build lane: `EmiDeskService` clock tick | `{n}` hour | 0.50 | 1/night, outranks `ringOpen` | 2 | 25 | common.lateNight | 2 |
| `smallHours` | still up past 03:00 | build lane: same | `{n}` hour | 0.35 | 1/night | 2 | 15 | common.lateNight | 2 |
| `morningFirst` | first summon of the day before 10:00 | build lane: same | none | 0.30 | 1/day | 1 | 15 | common.smallTalk | 1 |
| `weekend` | a summon on a Saturday or Sunday | build lane: `EmiDeskService.Summon()`, fired on every weekend summon and left to the moment's own day/1 limit to make it the first | none | 0.20 | 1/day | 1 | 8 | common.smallTalk | 1 |
| `appIdleLong` LOCKED | no input to the app for 20+ min while she is out | `Services/Tracking/ActivityTracker.cs:38` `IdleStateChanged` (true) + EmiDesk timer | `{minutes}` | 0.15 | 1/sitting, 15 min from any bark | 1 | 25 | common.idle | 2 |
| `idleShort` | 5 min of no input on her (the ambient heartbeat) | build lane: `EmiDeskService` idle timer | `{minutes}` | 0.06 | 300000 | 1 | 25 | common.idle | 2 |
| `longSitting` | the app has been open 2h+ this launch | build lane: same, from process start | `{minutes}` | 0.25 | 1/sitting | 1 | 15 | common.encourage | 2 |
| `backSoon` | she was dismissed and re-summoned within 5 min | build lane: `EmiDeskService.Summon()` | `{minutes}` | 0.35 | 120000 | 1 | 8 | common.attention | 2 |
| `dayTurned` | the calendar date turned over while she was out | build lane: `EmiDeskService.OnClockTick`, `_clockDay` latch (the first tick of a sitting only records the date it found) | none | 0.30 | 1/night | 1 | 8 | common.lateNight | 1 |

### 1.7 group `tier`

| id | trigger | fire site | ctx | odds | cooldownMs | prio | pool | common pools | spice |
|---|---|---|---|---|---|---|---|---|---|
| `premiumTeaseSeen` LOCKED | a locked feature was clicked and refused | `Services/TierGate.cs:128` `ShowDenied(in TierVerdict)` - the single refusal surface every gate funnels through (`Demand` :113) | `{target}` `verdict.Feature` | 0.33 | 1/feature/day | 2 | 25 | common.tease | 1 (never a pitch, never the word premium) |
| `lockedCardTapped` | a padlocked card in the ring was tapped | build lane: `EmiTargets` gate branch -> `TierGate.ShowDenied` | `{target}` | 0.33 | 1/feature/day | 2 | 8 | common.tease | 1 |
| `tierUp` | the subscription tier went up | `Services/Account/PatreonService.cs:41` `TierChanged` (up); mirror `Services/SubscribeStarService.cs:48` | `{target}` tier | 0.60 | 1/session | 2 | 8 | common.win | 1 |
| `tierLapse` | the subscription tier went down or expired | `MainWindow/MainWindow.Patreon.cs:965` `OnPatreonTierChanged` (subscribed `:1015`); SubscribeStar mirror `MainWindow.SubscribeStar.cs:23`. **Guard on `PatreonService.EntitlementResolved` (:190): before it flips, a false `HasLabAccess` means UNKNOWN, not lapsed (#1048). A free-user logout raises no `TierChanged` at all (`MainWindow.Patreon.cs:350`), so this moment cannot catch that case** | `{target}` tier | 0.30 | 1/session | 2 | 15 | common.encourage | **0** (kind, never a sales line, never a nag) |
| `dailyFreeToday` | today's free rotating feature is one she can offer | `Services/DailyFreeService.cs:71` `TodayChanged`, read `:143 IsFreeToday(key)`, pool `:40` (takeover, awareness, fyp, remote) | `{target}` feature key | 0.30 | 1/day | 2 | 8 | common.tease | 1 |

### 1.8 group `lifecycle`

| id | trigger | fire site | ctx | odds | cooldownMs | prio | pool | common pools | spice |
|---|---|---|---|---|---|---|---|---|---|
| `appOpened` | the app launched and she is enabled (the welcome-back beat) | `AvatarTube/AvatarTubeWindow.Speech.cs:2383` (`App.Bark?.NotifyAppOpened(GreetingAwayBucket(lastSeen))`) | `{target}` away bucket (first/soon/back/while/long) | 0.35 | 1/launch | 2 | 25 | common.attention | 2 |
| `firstLaunchEver` | the first-run wizard claimed this launch | `MainWindow/MainWindow.xaml.cs:542` `FirstRunWizard.ShouldRunAndClaim()` | none | 0 HOLD (the wizard owns the screen) | - | 3 | 0 | common.hold | 0 |
| `updateAvailable` | a newer release was found | `Services/Update/UpdateService.cs:193` `UpdateAvailable` (`AppUpdateInfo`); dialog at `App.xaml.cs:3702` / `:3976` | `{target}` version | 0.40 | 1/launch | 2 | 8 | common.smallTalk | 1 (never nag, never promise) |
| `afterUpdate` | this launch is the first on a new version | `MainWindow/MainWindow.Marquee.cs:366` (the `LastSeenVersion` gate; stamp at `:383` / `:440`) | `{target}` version | 0.60 | 1 ever per version | 2 | 15 | common.smallTalk | 1 |
| `crashRecovered` | the previous run ended badly | `App.xaml.cs:1555` `ChaosCrashSentinel.ConsumeAndReport` / `:1556` `EngineCrashSentinel.ConsumeAndReport` / `:1562` `UiHangWatchdog.ConsumeAndReportPreviousHang` | none | 0.40 | 1/launch | 2 | 8 | - | **0** (kind, never blame the user, never claim she fixed it) |
| `minimizedToTray` | the window was minimized to tray while she is out | `MainWindow/MainWindow.WindowChrome.cs` `OnClosing`, the X -> tray branch - NOT `TrayIconService.MinimizeToTray`, which is also start-minimized and the Chaos takeover | none | 0.20 | 1/launch | 1 | 8 | common.smallTalk | 1 |
| `appClosing` | the app is really exiting | `App.xaml.cs:4551` `OnExit(ExitEventArgs)` | none | 0 - **there is NO pool on app close, by the fence** | - | 3 | 0 | common.hold (the wordless exit flinch only) | **0** |
| `modChanged` | the active mod / persona changed | bridge row `ModChanged` (`Services/Companion/BarkService.cs:863`) | `{target}` mod id, lowercased | 0.30 | 1/launch | 1 | 8 | common.smallTalk | 1 |
| `discordLinked` **DEFER** | the Discord account linked or unlinked | `Services/Account/DiscordService.cs:38` `AuthenticationChanged` | `linked` bool | 0.25 | 1/session | 1 | 8 | common.smallTalk | 1 |

---

## 2. COMMON POOLS

Multi-purpose pools, attached to many moments. A common line must read correctly after **every** moment
in its attachment list, so it may never name a feature, a number or a channel. Specific pools do that.

`mix` in the moments file is the chance of drawing from the moment's own specific pool first; the
default is 0.65 and only `common.hold` moments override it to 0.

The attachment lists below name every moment in the catalogue, DEFER rows included, so a common line still
has to read right after a moment that is not in v1. That costs nothing: a common line may not name a
feature anyway, so writing to the full list is the same job as writing to the v1 list. Pool sizes are
unaffected by the DEFER cut - the common pools carry the variety, and they are what v1 leans on hardest.

| pool | brief | target size | attached to |
|---|---|---|---|
| `common.attention` | she noticed you, or wants you to notice her. arrival, being clicked, being petted, you came back. warm, a bit needy, never demanding. | 40+ | summoned, ringOpen, petted, videoRunning, featureOpened, backSoon, appOpened, sheListeningOn, remoteControllerConnected, intakeOpened |
| `common.idle` | nothing is happening and she is fine with that, mostly. waiting, being decor, small observations about the cursor, the window, the hour. **no doubles** (it is one of the most frequent pools). | 50+ | appIdleLong, idleShort, and the glass's own quiet beats |
| `common.encourage` | you are mid-something and she is on your side. never a chaperone, never a nudge to stay, never guilt. covers a pause, a lapse, a broken streak, a long haul. | 40+ | ringPick, sessionPaused, sessionResumed, sessionHalfway, sessionLastMinute, longSitting, streakBroken, programLapsed, tierLapse |
| `common.tease` | she is being a brat about something small. pokes her own suggestion, your habits, the thing you keep opening. punches at herself or at nothing, never at you. | 40+ | suggestionIgnored3x, glassOffer, featureOpenedRepeat, flashClicked, keywordTriggerFired, fypOpened, premiumTeaseSeen, lockedCardTapped, dailyFreeToday |
| `common.deeper` | temptation. the spice-1 and spice-2 heartland: come closer, one more, stay a bit, hold still. she wants you further in and says so like a dork. | 40+ | flashesStarted, subliminalsStarted, overlaySpiralUp, brainDrainOn, blinkTrainerStarted, engineStarted, sessionStarted, sessionPhaseChanged, rampStepUp, pinkRushStarted, takeoverStarted, possessionRungChanged, enhancementApplied, dtrhOpened |
| `common.lateNight` | the small hours. the room is dark, the screen is the only thing on, she is still up too. tired-fond, never a scold, never a "go to bed" she means. | 40+ | lateNight, smallHours, dayTurned, bedtimeSet, bedtimeBroken |
| `common.afterEffect` | something just finished and the air is still ringing. the come-down: quiet, pleased, a little dazed. | 40+ | videoEnded, flashesStopped, brainDrainOff, mindWipeTriggered, engineStopped, takeoverEnded, lockdownEnded, pinkRushEnded, arcademyClosed, fypClosed |
| `common.smallTalk` | filler with a pulse. the pool that catches everything ordinary: a tab opened, a toy connected, a version bump, a resize. real speech, fillers allowed, "ok." and "hm." are lines. | 50+ | dismissed, ringDismissed, pinRemoved, resized, dragged3x, askIgnored3x, avatarMuted, avatarKept, bubblesStarted, loomOpened, justDropOpened, hapticsConnected, questsRefreshed, leaderboardViewed, morningFirst, weekend, updateAvailable, afterUpdate, minimizedToTray, modChanged, discordLinked |
| `common.win` | it went well. celebrates too big and slightly wrong-scale. gold stars she does not have, applause with no hands. | 40+ | levelUp, achievementUnlocked, questCompleted, streakMilestone, streakKept, skillUnlocked, luckyProc, punchCardCompleted, programDayCompleted, seasonRolled, roadmapStepDone, sessionCompleted, bubbleCountWon, attentionCheckPassed, lockCardSolved, mantraCompleted, pinAdded, effectFired, arcademyFromRing, arcademyOpened, contentPackInstalled, dtrhClosed, companionLevelUp, xpBigAward, tierUp |
| `common.loss` | it did not go well. **the fail is hers or nobody's.** never a grade, never a scold, never "you should have". | 40+ | attentionCheckFailed, bubbleCountLost, mantraStreakBroken, streakBroken, sessionAbandoned, goonMatchEnded (lost), intakeClosed (failed) |
| `common.hold` | **no text at all.** face + chain rows only (`{"face": "-_-", "hold": 1400}` shape), for the moments where speaking is forbidden. this is what she does instead of talking. | 30 rows | avatarSpeaking, awarenessReaction, attentionCheckShown, intakeRunning, lockdownCountdown, panicPressed, effectDeclined, askIgnored, appClosing, firstLaunchEver, emergencyExitOpened |
| `common.dork` | the RARE_DORK channel from round 1: a costume bit (bad evil-computer impression, a miscast movie line) with the wrong face on it. its own odds (0.12) and max 1 a sitting, drawn INSTEAD of the moment's pool, never as well. ~1 line in 12 of the whole corpus. | 20 | ringOpen, appIdleLong, summoned, glassOffer, premiumTeaseSeen |

---

## 3. HARD RULES (the silence law)

Checked in this order, before any odds roll. A rule that says HOLD means: no bubble, no offer, no glass
channel; the face and chain still play.

1. **Panic key.** From `HandlePanicKeyPress()` (`MainWindow/MainWindow.xaml.cs:1408`) **and from
   `TriggerPanicFromRemote()` (`MainWindow/MainWindow.RemoteControl.cs:1458`), which does NOT call
   `NotifyPanic()` today** - both sites must arm the hold. Total silence, and a 300s silence tail after. No line, ever, on any moment, including a moment that fires because the panic
   press stopped it. The panic press is the one input she must never comment on.
2. **Lockdown.** Silent for the whole countdown (`LockdownService.CountdownTick`). **Release the hold on
   `LockdownDeactivated`, never on a countdown reaching zero: `CountdownTick` deliberately skips the final
   0:00 tick (`Services/Haptics/LockdownService.cs:342-353`), so a zero-watcher would hold forever.** `lockdownArmed` gets at
   most one line at the moment of arming and `lockdownEnded` one at release, both spice 0. Never a line
   about escape attempts (`LockdownService.EscapeAttempted`) - that is the app's business, not hers.
3. **Attention checks.** Silent from the moment a target spawns (`VideoService.SpawnTarget`,
   `AttentionCheckService.Fire`) until it resolves. Never over one, never about one that is live.
4. **Intake.** Silent for the whole of `IntakeHostService.IsActive` - a question may be on screen at any
   moment and there is no per-question event to gate on.
5. **The avatar owns voice.** While a tube bubble is live (`AvatarTubeWindow.IsSpeaking` /
   `IsSpeakingAudio`, or within 20s of `BarkService.BarkSpoken`), EMI holds to blink-only faces. If she is
   NOT muted, an EMI line or fired effect counts against BarkService's min-gap (`BRIEF.md` §4).
6. **Emergency exit.** Silent while `EmergencyExitHostService.IsOpen`.
7. **Bedtime.** After `bedtimeSet`, no offers and no glass channel until 06:00. Barks drop to
   `common.lateNight` at half odds. Bedtime NEVER closes the app and never says don't go.
8. **No pool on app close.** `appClosing` has no lines and never will. The wordless exit flinch (1 in 3)
   is the only thing that may ride it. Same rule as the campus.
9. **Video.** One bark per video maximum, never before 8s in, never in the last 5s, never over the
   attention check, never a comment on what is in the video, never a nudge to keep watching.
10. **Global floor.** 45s between any two spontaneous lines (BRIEF §8; a 180s floor was drafted
    here but never shipped, and the odds have been re-tuned to the 45s reality). Priority-3 ceremony
    moments (`desktopFirstBoot`, `levelUp`, `sessionCompleted`, `streakMilestone`, `bedtimeSet`) are
    exempt; nothing else is.
11. **Doubles ration.** One `"double": true` line per session across ALL pools, `common.idle` and
    `common.attention` carry none.
12. **The fence** (unchanged, `VOICE.md`): no acronym, no "i love you" in words, never argues her own
    nature, no self-aware scripting gags, no cruelty at the user, no guilt at a real exit, no door / lab /
    records-room hints, no trigger vocabulary, no real names, no promises.
13. **Spice ceiling per moment** is a maximum, not a target. The rows marked **0** in section 1
    (`panicPressed`, `lockdownArmed/Countdown/Ended`, `crashRecovered`, `tierLapse`, `sessionPaused`,
    `sessionAbandoned`, `streakBroken`, `programLapsed`, `descentFusePhase`, `appClosing`,
    `firstLaunchEver`, `desktopFirstBoot`) are innocent-and-kind only. These are the moments where
    something went wrong for the user or their control is what matters; flirting there is the one thing
    that would read as cruel.
14. **She never claims a number that is not real** (telemetry rule). Every `{n}` / `{minutes}` /
    `{streak}` in a line must come from the ctx payload, never be invented in prose.

---

## 4. HOOK PLAN

`App.EmiDesk?.Fire("id", new { ... })` is a no-op when she is not out, so no call site needs a guard
beyond the existing null-conditional. Every call goes inside the try/catch the surrounding method already
has, or its own bare `try { } catch { }` where it does not.

### 4.A The bulk hook - one line, ~35 moments

`Services/Companion/BarkService.cs`, method `Raise` (line 987). Insert at the **top of the `try`, before
the `_rules.ForTrigger(trigger)` lookup** - that lookup early-returns when no bark rule exists for the
trigger, and most of these triggers have no rule in the shipped `bark_rules.json`:

```csharp
private bool Raise(string trigger, Action<BarkContext>? fill = null, bool guaranteed = false)
{
    try
    {
        Services.EmiDesk.EmiDeskService.MirrorBarkTrigger(trigger, fill);   // <- the only new line
        var rules = _rules.ForTrigger(trigger);
        ...
```

The full trigger vocabulary is 108 strings (`grep -oP 'Raise\("\K[A-Za-z_]+' Services/Companion/BarkService.cs`);
every name in the table below was taken from that list.

**Two bark contexts are lossy and must NOT be read through the mirror:** `SessionCompleted` discards
`finalXP` / `finalElapsedTime` / `_pauseCount` (`:914`), and `SessionProgress` sets only
`session_elapsed_sec`, never the remaining time (`:925`). The whole `session` group therefore subscribes
to `SessionEngine`'s own events directly (4.C), and only the non-session rows below use the mirror.

`MirrorBarkTrigger` lives in `EmiDeskService`, builds its own `BarkContext` from `fill` (so the mirror
never mutates the bark one), maps the trigger through a static table, and returns immediately for an
unmapped trigger. The table:

| BarkService trigger | EMI moment |
|---|---|
| `AppOpened` | `appOpened` (ctx `away_bucket` -> `{target}`) |
| `TabNavigated` | `featureOpened` / `featureOpenedRepeat` (ctx `tab` -> `{target}`) |
| `FeatureOpened` | `featureOpened` (ctx `feature`) |
| `VideoStarted` / `VideoEnded` | `videoRunning` / `videoEnded` |
| `AttentionCheckPass` / `AttentionCheckFail` | `attentionCheckPassed` / `attentionCheckFailed` |
| `FlashDisplayed` | (no moment - too frequent; used only to arm `flashesStarted`) |
| `SubliminalDisplayed` | `subliminalShown` |
| `BrainDrainTriggered` / `MindWipeTriggered` | `brainDrainOn` / `mindWipeTriggered` |
| `BubbleCountCompleted` / `BubbleCountFailed` | `bubbleCountWon` / `bubbleCountLost` |
| `BlinkTrainerStateChanged` | `blinkTrainerStarted` (ctx `running`) |
| `KeywordTriggerFired` | `keywordTriggerFired` |
| `MantraCompleted` / `MantraStreakBroken` | `mantraCompleted` / `mantraStreakBroken` |
| `LockdownActivated` / `LockdownDeactivated` / `LockdownCountdownTick` | `lockdownArmed` / `lockdownEnded` / `lockdownCountdown` (HOLD) |
| `LevelUp` | `levelUp` (ctx `level`) |
| `AchievementUnlocked` | `achievementUnlocked` (ctx `achievement`) |
| `QuestCompleted` / `QuestsRefreshed` | `questCompleted` / `questsRefreshed` |
| `SkillUnlocked` / `PinkRushStarted` / `PinkRushEnded` / `LuckyProc` | same-named moments |
| `StreakMilestone` | `streakMilestone` (ctx `streak_days` -> `{streak}`) |
| `CompanionLevelUp` | `companionLevelUp` |
| `LeaderboardViewed` | `leaderboardViewed` (ctx `rank`, `total`) |
| `RoadmapStepCompleted` / `RoadmapTrackUnlocked` / `RoadmapBadgeEarned` | `roadmapStepDone` |
| `SessionStarted` / `SessionStopped` / `SessionCompleted` / `SessionPhaseChanged` / `SessionProgress` | `sessionStarted` / `sessionAbandoned` / `sessionCompleted` / `sessionPhaseChanged` / (progress feeds `sessionHalfway` + `sessionLastMinute`) |
| `QuizCompleted` | `intakeClosed` (ctx `passed`, `perfect`) |
| `UpdateAvailable` | `updateAvailable` |
| `PatreonTierChanged` | `tierUp` / `tierLapse` (ctx `tier`, `tier_up`) |
| `IdleStateChanged` | `appIdleLong` (ctx `idle`) |
| `ModChanged` | `modChanged` |
| `DiscordAuthChanged` | `discordLinked` |
| `RemoteCommandReceived` / `ControllerConnectedChanged` | `remoteControllerConnected` |
| `EnhancementApplied` | `enhancementApplied` |
| `Panic` | `panicPressed` (HOLD + 300s silence tail) |
| `LockCardCompleted` | `lockCardSolved` |
| `PersistentMemoryRecalled` | `memoryRecalled` |
| `VideoAboutToStart` | arms `videoRunning`, never speaks |
| `BubblePopped` / `BubbleMissed` / `Blink` / `LongStare` / `GazePopped` / `FaceFound` / `FaceLost` / `MouthOpen` / `TongueOut` / `TrackingStateChanged` | **deliberately unmapped** - too frequent, would burn the floor |
| `UserMessageSent` / `WakeBambiRequested` / `AvatarClicked` / `SettingChanged` / `TutorialCompleted` / the ~35 `Chaos*` triggers | **deliberately unmapped** - the avatar owns the chat and the run and would be talked over. Available if the owner wants them later |

**Why the mirror and not a second `WireSubscriptions()`:** BarkService already owns the subscribe/unsubscribe
lifetime for all of these (`Wire<T>` at `:~960`), including the mod-reload path. A parallel subscriber block
would double every event handler and would have to re-solve the same teardown. The mirror is one line and
inherits all of it. The mute in `BRIEF.md` §4 must therefore be applied **below** the mirror line (in
`Speak`, or in the gate) so a muted avatar still feeds EMI her moments.

### 4.B Inline hooks - services with no event

One line each, at the top of the method body after the existing early-return guards.

| file | method (line) | call |
|---|---|---|
| `Services/Flash/FlashService.cs` | `Start()` 385 | `App.EmiDesk?.Fire("flashesStarted", null);` |
| `Services/Flash/FlashService.cs` | `Stop()` 407 | `App.EmiDesk?.Fire("flashesStopped", new { minutes = RunMinutes });` |
| `Services/Subliminal/SubliminalService.cs` | `Start()` 112 | `App.EmiDesk?.Fire("subliminalsStarted", null);` |
| `Services/BubbleService.cs` | `Start(bool, int?)` 195 | `App.EmiDesk?.Fire("bubblesStarted", new { n = frequency });` |
| `Services/Notifications/OverlayService.cs` | `ShowOverlayTimed(kind, durationMs, opacity)` 842 | `App.EmiDesk?.Fire("overlaySpiralUp", new { channel = kind, n = safeDurationMs / 1000 });` (guard `kind == "spiral"`) |
| `Services/Notifications/OverlayService.cs` | `StartBrainDrainBlur(intensity, melt)` 2129 | `App.EmiDesk?.Fire("brainDrainOn", new { n = intensity, melt });` - after the `BrainDrainWithheld` early return at 2131 |
| `Services/Notifications/OverlayService.cs` | `StopBrainDrainBlur()` 2444 | `App.EmiDesk?.Fire("brainDrainOff", null);` |
| `Services/AttentionCheckService.cs` | `Fire()` (private, called from `ScheduleNext`; `FireNow()` 115 is the test path) | `App.EmiDesk?.Fire("attentionCheckShown", null);` - **HOLD arm, not a line** |
| `Services/Video/VideoService.cs` | `SpawnTarget()` 5024 | same HOLD arm as above (the in-video target family) |
| `Services/Session/SessionEngine.cs` | `PauseSession()` 435 | `App.EmiDesk?.Fire("sessionPaused", new { n = _pauseCount });` - **no event exists** |
| `Services/Session/SessionEngine.cs` | `ResumeSession()` 485 | `App.EmiDesk?.Fire("sessionResumed", new { minutes = RemainingTime.TotalMinutes });` - **no event exists** |
| `MainWindow/MainWindow.StartStop.cs` | `StartEngine(bool systemInitiated)` 177 | `App.EmiDesk?.Fire("engineStarted", new { systemInitiated });` |
| `MainWindow/MainWindow.StartStop.cs` | `StopEngine()` 313 | `App.EmiDesk?.Fire("engineStopped", new { minutes = EngineRunMinutes });` |
| `MainWindow/MainWindow.StartStop.cs` | `RampTimer_Tick` 501 | `App.EmiDesk?.Fire("rampStepUp", new { n = _rampStep });` - only on an actual step change |
| `Services/TierGate.cs` | `ShowDenied(in TierVerdict)` 128 | `App.EmiDesk?.Fire("premiumTeaseSeen", new { target = verdict.Feature });` - **the single refusal surface for every gate in the app** |
| `Services/Arcademy/ArcademyHostService.cs` | `Launch()` 146 / `CloseActive()` 322 | `arcademyOpened` / `arcademyClosed` |
| `Services/Chaos/DtrhHostService.cs` | `Launch(bool)` 70 / `CloseActive()` 172 | `dtrhOpened` / `dtrhClosed` |
| `Services/Chaos/LoomHostService.cs` | `Launch()` 30 | `loomOpened` |
| `Services/Fyp/FypHostService.cs` | `Launch()` 67 / `Close()` 121 | `fypOpened` / `fypClosed` |
| `Services/Quiz/IntakeHostService.cs` | `Launch(bool, bool)` 93 / `CloseActive()` 164 | `intakeOpened` (+ arm `intakeRunning` HOLD) / `intakeClosed` (drop the HOLD) |
| `Services/JustDrop/JustDropHostService.cs` | `LaunchShop()` 75 | `justDropOpened` |
| `Services/EmergencyExit/EmergencyExitHostService.cs` | `Open()` 87 / `Close()` 523 | `emergencyExitOpened` HOLD arm / drop |
| `MainWindow/MainWindow.RemoteControl.cs` | `TriggerPanicFromRemote()` 1458 | `App.EmiDesk?.Fire("panicPressed", null);` - **the second panic site, and the one nothing else hooks** |
| `Services/Session/SessionEngine.cs` | `CheckDelayedFeatures(double)` 690, at `pending.Start()` 704 | `App.EmiDesk?.Fire("sessionFeatureArrived", new { target = pending.Name, n = pending.StartMinute });` |
| `Services/Session/SessionEngine.cs` | `UpdateRampingValues(...)` 591, bubble step branch 663-677 | `App.EmiDesk?.Fire("rampStepUp", new { n = rampSteps });` - only when `rampSteps` actually changed |
| `MainWindow/MainWindow.xaml.cs` | `HandlePanicKeyPress()` 1408 | `App.EmiDesk?.Fire("panicPressed", null);` **first line of the method** - it must land before anything else stops, so the silence tail is armed even if the rest of the ladder throws |
| `MainWindow/MainWindow.xaml.cs` | first-run branch 542 (`FirstRunWizard.ShouldRunAndClaim()`) | `firstLaunchEver` HOLD arm |
| `MainWindow/MainWindow.Marquee.cs` | the `LastSeenVersion` gate 366 (before the stamp at 383/440) | `App.EmiDesk?.Fire("afterUpdate", new { target = currentVersion });` |
| `App.xaml.cs` | after `EngineCrashSentinel.ConsumeAndReport` 1556 | `App.EmiDesk?.Fire("crashRecovered", null);` - only when a sentinel actually consumed something |
| `App.xaml.cs` | `OnExit(ExitEventArgs)` 4551 | `App.EmiDesk?.Fire("appClosing", null);` - **HOLD only, the wordless flinch. Never a line.** |
| `MainWindow/MainWindow.xaml.cs` | tray-minimize path ~291 | `minimizedToTray` |
| `Models/AchievementProgress.cs` | `UpdateDailyStreak()` 249 / `ResolveDeferredStreakBreak(reason)` 407 | `streakKept` / `streakBroken` (the streak-milestone case is already covered by 4.A) |
| `Services/EmiDesk/EmiDeskService.cs` | wherever EMI actually speaks | `App.Bark?.NotifyExternalLineSpoken();` (`Services/Companion/BarkService.cs:326`) - **required by `BRIEF.md` §4**: when the avatar is NOT muted, an EMI line must push the avatar's 60s `GlobalMinGapMs` floor or the two of them talk over each other |

### 4.C Events that exist but nothing subscribes to - add a `Wire<>` (or subscribe from `EmiDeskService`)

These are already `public event` and need no new seam, only a subscriber:

| event | moment |
|---|---|
| `Services/Flash/FlashService.cs:196` `FlashClicked` | `flashClicked` |
| `Services/Progression/ProgressionService.cs:35` `XPAwarded` (`XpAward`) | `xpBigAward` (suppress when `LeveledUp`) |
| `Services/Progression/IntakePunchCardService.cs:81` `PunchCardCompleted` | `punchCardCompleted` |
| `Services/Program/ProgramService.cs:139` `DayCompleted` / `:142` `ProgramLapsed` | `programDayCompleted` / `programLapsed` |
| `Services/Haptics/HapticService.cs:59` `ConnectionChanged` / `:60` `DeviceDiscovered` | `hapticsConnected` |
| `Services/LockCard/LockCardService.cs:42` `LockCardCompleted` | `lockCardSolved` |
| `Services/Possession/PossessionDirector.cs:98` `RungChanged` | `possessionRungChanged` |
| `Services/Descent/DescentCountdownService.cs:158` `PhaseChanged` / `:173` `ZeroReached` | `descentFusePhase` |
| `Services/GoonGame/GoonMatchService.cs:264` `MatchEnded` (`GoonMatchResult`) | `goonMatchEnded` |
| `Services/Content/ContentPackService.cs:78` `PackDownloadCompleted` | `contentPackInstalled` |
| `Services/AutonomyService.cs:134` `EnabledChanged` | `takeoverStarted` / `takeoverEnded` |
| `Services/Speech/SpeechService.cs:105` `ListeningChanged` | `sheListeningOn` |
| `Services/DailyFreeService.cs:71` `TodayChanged` | `dailyFreeToday` |
| `Services/Companion/BarkService.cs:143` `BarkSpoken` | `avatarSpeaking` HOLD (+20s tail) |
| `Services/Tracking/ActivityTracker.cs:38` `IdleStateChanged` | `appIdleLong` (also mirrored in 4.A) |

### 4.D Owned entirely by the build lane

Everything in §1.1, §1.6 and `avatarMuted` / `avatarKept` / `lockedCardTapped`: no service touched.
The `time` group needs one 60s `DispatcherTimer` inside `EmiDeskService` (there is no time-of-day event
anywhere in the app; the only precedent is BarkService's `local_hour` condition at
`Services/Companion/BarkService.cs:1345`).

### 4.E Seams that do NOT exist and would have to be carved

Listed so nobody hunts for them:

- **Prestige** - no prestige system exists in the desktop app. No moment defined.
- **Wallet / economy** - `../../CCP.Core/Services/Arcademy/ArcademyEconomy.cs` is pure static functions on a `JObject`
  wallet with no events, and the wallet itself lives in the web host. Sparkle points are a skill-tree
  multiplier (`SkillTreeService.GetSparkleBoostTier()` :192), not a currency with a balance event. No
  economy moment defined.
- **Audio** - `Services/AudioService.cs` exposes **zero** events. `Duck` (:906) / `Unduck` (:1092) /
  `PlayOneShot` (Playback.cs:172) would each need an inline hook. Judged not worth a moment (a duck is a
  side effect of things that already have moments).
- **Session pause / resume** - `SessionEngine` has five events and none of them covers pause or resume;
  both need the inline hooks in 4.B.
- **Attention check shown** - neither `AttentionCheckService` nor `VideoService` raises anything at
  display time. Two separate inline sites (4.B), and this is the one gap that a HOLD rule depends on.
- **`BubbleCountService.GameFailed`** raises `EventArgs.Empty` from six different sites (mercy, no videos,
  poison cooldown, window skip, exception), so `bubbleCountLost` cannot tell a mercy from a crash without
  widening the event. Written as one pool on purpose.
- **No avatar speaking start/stop event.** `AvatarTube/AvatarTubeWindow.Speech.cs:1645` `PlaySpokenAudio`
  already holds both transitions (`_isSpeakingAudio = true` at `:1662`, false at `:1672` clip-end and
  `:1683` refused), so two one-line events there are the clean fix; the alternative is polling
  `IsSpeakingAudio` on the EmiDesk timer. `BarkSpoken` alone is not enough - it fires before the audio.
- **`BankAccumulator`** (`Services/BankAccumulator.cs:39`, `OnAward` `:129` returning a `Flight?`) is XP
  presentation, not economy, and raises nothing. No moment defined; it is the seam if the owner ever
  wants EMI to react to a bank flight.
- **The Bureau host** (`Services/Bureau/BureauHostService.cs:63` `Launch()`) is deliberately given no
  moment: it is a local-only tool that never ships.
- **There is no `GoonGallery`** anywhere in the tree; `goonMatchEnded` is the only Goon moment.
- **`ShowTab` "settings" is Home.** The dashboard's tab key is `"settings"` and the real settings door is
  `"appsettings"` (`MainWindow/MainWindow.TabNavigation.cs:590`). `{target}` lines must use the display
  name from `EmiTargets`, never the raw key.
