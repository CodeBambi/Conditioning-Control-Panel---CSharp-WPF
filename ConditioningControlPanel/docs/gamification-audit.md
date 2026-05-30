# Gamification Systems Audit

**Date:** 2026-05-30
**Scope:** How the three gamification systems (quests, achievements, skill tree / XP) actually work today, what the newer feature modules expose, and where the integration gaps are.
**Method:** Code is the source of truth. Every claim below is grounded in a file/line citation. Where something was ambiguous or unverifiable it is flagged explicitly rather than guessed.

> **The single most important finding for planning:** There is **no gamification event bus**. The "observation layer" is just a set of direct, imperative method calls into three services — `App.Achievements?.Track*(...)`, `App.Quests?.Track*(...)`, and `App.Progression?.AddXP(amount, XPSource.X)`. A feature is "visible" to gamification **only if its code explicitly calls one of those**. Everything else is invisible regardless of how much player behavior it observes internally. New content can only react to behavior that already flows into these three sinks (or to behavior for which we add a new `Track*`/`AddXP` call site).

---

## PART 1 — The three gamification systems

### Shared architecture (read this first)

There are three sinks. Two of them (`Achievements`, `Quests`) are fed by `Track*` calls; one (`Progression`) is fed by `AddXP`. They are wired together internally, which produces a lot of *indirect* feeding:

- **`ProgressionService.AddXP(double amount, XPSource source, XPContext context)`** (`Services/ProgressionService.cs:25`) is the XP hub. On every call it fans out:
  - `App.Companion?.AddCompanionXP(...)` (`ProgressionService.cs:75`) — feeds the **separate** companion-level ledger.
  - `App.Quests?.TrackXPEarned(...)` (`ProgressionService.cs:78`) — feeds "earn N XP" quests.
  - On level-up it calls `App.SkillTree?.OnLevelUp(...)` and `App.Achievements?.CheckLevelAchievements(...)` (`ProgressionService.cs:99-102`).
- **`AchievementService`** is a secondary hub: many of its `Track*` methods turn around and call `App.Quests?.Track*` internally. So most quest progress is a *side effect of achievement tracking*, not a direct feature→quest call (see Quests §Observation).

`XPSource` enum (the canonical list of "what the XP layer can attribute behavior to") lives at `Services/CompanionService.cs:10-25`:
`Flash, Video, Subliminal, Bubble, LockCard, Session, BubbleCount, BouncingText, AvatarInteraction, KeywordTrigger, Mantra, AttentionCheck, Other`.

Anti-cheat: `ProgressionService.AddXP` suppresses passive sources (Flash/Subliminal/BouncingText) when `App.ActivityTracker.IsIdle` (`ProgressionService.cs:39-47`). `ActivityTracker` (`Services/ActivityTracker.cs`) is a pure Win32 `GetLastInputInfo` idle detector (3-min threshold) — **not** an event bus; its only event is `IdleStateChanged`.

There **is** a tiny string-keyed event bus, `TutorialEventBus` (`Services/TutorialEventBus.cs`), with `Emit(string)` / `Event`. It is used **exclusively by the Deeper tutorial system** (emits `"EffectAdded"`, `"RuleAdded"`, `"FileSaved"`, `"WindowLoaded:DeeperEditorWindow"` — see `Views/Deeper/DeeperEditorWindow.Unified.cs:153,187,298`). It is **not** wired to gamification, but it is a precedent for a lightweight pub/sub pattern in this codebase.

---

### 1A. QUESTS

**Where it lives**
| File | Role |
|------|------|
| `Models/Quest.cs` | `QuestType` + `QuestCategory` enums, `QuestDefinition` class, **hardcoded** daily/weekly quest lists |
| `Models/QuestProgress.cs` | `QuestProgress` (persisted) + `ActiveQuest` (per-quest live state) |
| `Services/QuestService.cs` | Runtime engine: generation, progress, completion, rewards, rerolls, persistence (`quests.json`) |
| `Services/QuestDefinitionService.cs` | Fetches/caches **remote** quest defs + seasonal quests + CDN images from the server |
| `QuestCompletePopup.xaml(.cs)` | Completion toast |

**Content definition is dual-source.** Definitions are hardcoded in C# as a fallback and fetched from the server as the live source. Hardcoded lists: `QuestDefinition.DailyQuests` (`Models/Quest.cs:169`) and `QuestDefinition.WeeklyQuests` (`Models/Quest.cs:186`). Remote: `QuestDefinitionService` GETs `/quests/definitions` (`QuestDefinitionService.cs:19-20`), caches 24h to `quest_definitions_cache.json`. The engine prefers remote, falls back to embedded everywhere via `App.QuestDefinitions?.GetDailyQuests() ?? QuestDefinition.DailyQuests.ToList()` (`QuestService.cs:315,335`).

**One quest, as defined today** (`Models/Quest.cs:171`):
```csharp
new("flash_flood_d", "Flash Flood", "View 50 flash images", QuestType.Daily, QuestCategory.Flash, 50, 150, "⚡", "pack://application:,,,/Resources/features/flash.png"),
```
Constructor signature: `QuestDefinition(id, name, description, type, category, target, xpReward, icon, imagePath)` (`Quest.cs:119`). "Registration" = membership in the static list; there is no registry/DI. The server JSON form is parsed in `QuestDefinitionService.ParseQuests` (`QuestDefinitionService.cs:224-260`).

**Data model / objective kinds.** A quest's objective kind is `QuestCategory` (`Quest.cs:14-27`), 11 values: `Flash, Video, Spiral, PinkFilter, Bubbles, LockCard, Session, Streak, BubbleCount, Mantra, Combined`. `QuestType` (`Quest.cs:8`) is only `Daily`/`Weekly` (seasonal is a `bool IsSeasonal` flag). At runtime only **one daily + one weekly** quest is active at a time (`QuestProgress.DailyQuest`/`WeeklyQuest`, `QuestProgress.cs`).

**Progression / completion logic.** All count-based progress flows through one private method `UpdateQuestProgress(QuestCategory category, int amount)` (`QuestService.cs:706-747`): if the active quest's category matches and it isn't complete, add `amount`; on reaching `TargetValue`, call `CompleteQuest`. Two objectives bypass this with bespoke trackers: `TrackXPEarned(int)` (`:654`, feeds `conditioning_champion_w`) and `TrackStreak(int)` (`:681`, feeds `streak_keeper_w`). Completion (`CompleteQuest`, `:764-858`) scales XP by level/streak/skill multipliers (`:813-819`) and awards via `App.Progression?.AddXP(scaledXP, XPSource.Other)` (`:827`) — deliberately `Other` to avoid recursion with `TrackXPEarned`. Generation/expiry is checked on startup and by a 1-minute `_refreshTimer` (`:114-126`).

**Observation mechanism (most important).** The quest service **subscribes to nothing and polls no feature**. It is fed purely by direct `App.Quests?.Track*` calls. Crucially, **most of those calls come from inside `AchievementService`, not from the feature itself**:

| Quest method | Immediate caller | Originating feature signal |
|---|---|---|
| `TrackFlashImage()` | `AchievementService.cs:292` | `FlashService.cs:1100` → `App.Achievements?.TrackFlashImage()` |
| `TrackBubblePopped()` | `AchievementService.cs:323` | `BubbleService.cs:304` |
| `TrackLockCardCompleted()` | `AchievementService.cs:398` | `LockCardWindow.xaml.cs:367` → `TrackLockCardCompletion` |
| `TrackVideoMinutes(min)` | `AchievementService.cs:433` | `VideoService.cs:1984` → `TrackVideoWatched` |
| `TrackSessionCompleted()` | `AchievementService.cs:615` | `SessionEngine.cs:297` → `TrackSessionComplete` |
| `TrackSpiralMinutes` / `TrackPinkFilterMinutes` / `TrackBrainDrainMinutes` | `AchievementService.cs:198/169/219` | AchievementService's **1-second `DispatcherTimer`** (`TrackTimeBasedProgress`, `:138`), gated on `App.Settings.Current.*Enabled && App.Overlay?.IsRunning` |
| `TrackBubbleCountCompleted()` | `BubbleCountService.cs:242` (**direct**) | bubble-count minigame |
| `TrackMantraCompleted()` | `MantraService.cs:64` (**direct**) | mantra minigame |
| `TrackXPEarned(int)` | `ProgressionService.cs:78` (**hub**) | every XP gain |
| `TrackStreak(int)` | `App.xaml.cs:1186` | startup, reads `Achievements.Progress.ConsecutiveDays` |

So: count/event categories are incremented at the moment of action; the three overlay-minute categories are **polled** once per second; XP and streak are pushed from the hub and startup. The reverse direction (UI listening to quests) does use real events: `QuestCompleted`, `QuestProgressChanged`, `QuestsRefreshed` (`QuestService.cs:67-69`).

**Purpose today.** Active daily/weekly engagement loop that awards level/streak-scaled XP into the main progression system, plus a completion popup, sound, haptics, a daily completion streak (with skill-tree "streak shield"), rerolls, and server sync of progress. Functional and live.

**Flags:**
- **`QuestCategory.Mantra` is effectively dead.** `TrackMantraCompleted()` is called (`MantraService.cs:64`) but **no embedded quest uses `QuestCategory.Mantra`**, and `QuestDefinition.ParseCategory` (`Quest.cs:136-152`) has **no `"mantra"` case**, so a server-sent mantra quest parses to `Combined`. Mantra completions essentially never credit a quest.
- **Fragile coupling:** because Flash/Bubble/Video/Session/LockCard quest credit routes *through* `AchievementService`, if a feature stops calling its `App.Achievements?.Track*` hook (or AchievementService early-returns), the corresponding quest silently stops progressing.

---

### 1B. ACHIEVEMENTS

**Where it lives**
| File | Role |
|------|------|
| `Models/Achievement.cs` | `Achievement` class + the `Achievement.All` static dictionary (all 30 defs) + `AchievementCategory` enum |
| `Models/AchievementProgress.cs` | Player progress: unlocked set + ~35 stat counters + streak logic |
| `Services/AchievementService.cs` | Engine: tracking, unlock, persistence, popup event |
| `AchievementPopup.xaml(.cs)` | Unlock toast |

**Content is entirely hardcoded in C#** — no JSON/config for definitions. Definitions live in a `static readonly Dictionary<string, Achievement>` named `All` (`Achievement.cs:29`). Adding one = editing that dictionary. Progress (not definitions) persists to `%APPDATA%\ConditioningControlPanel\achievements.json` (`AchievementService.cs:39-42`).

**One achievement, as defined today** (`Achievement.cs:32-40`):
```csharp
["plastic_initiation"] = new Achievement
{
    Id = "plastic_initiation",
    Name = "Plastic Initiation",
    Requirement = "Reach Level 10",
    FlavorText = "Welcome to the dollhouse. You're just getting started.",
    ImageName = "lv_10.png",
    Category = AchievementCategory.Progression
},
```
**Note: the definition object carries no unlock predicate.** The condition lives in imperative code in `AchievementService` (see below). This means the data and the unlock logic can drift apart.

**Categories** (`AchievementCategory`, `Achievement.cs:293-299`): `Progression`, `TimeSessions`, `Minigames`, `Hardcore`. There are **no tiers and no points**.

**The 30 achievements and their real unlock conditions** (condition source cited):
- **Progression** (level checks, `AchievementService.cs:256-265`): `plastic_initiation` (Lv10), `dumb_bimbo` (20), `fully_synthetic` (50), `docile_cow` (75), `perfect_plastic_puppet` (100), `brainwashed_slavedoll` (125), `platinum_puppet` (150).
- **TimeSessions:** `rose_tinted_reality` (PinkFilter ≥600 min, `:163`), `deep_sleep` (session ≥180 min, `:562`), `daily_maintenance` (ConsecutiveDays ≥7, `:272`), `retinal_burn` (TotalFlashImages ≥5000, `:286`), `morning_glory` ("morning drift" session in 6–9am, `:597`), `player_2_disconnected` ("gamer girl" with no alt-tab, `:608`), `sofa_decor` ("distant doll", `:584`), `look_but_dont_touch` ("good girls" + strictLock, `:590`), `spiral_eyes` (ContinuousSpiralMinutes ≥20, `:192`).
- **Minigames:** `mathematicians_nightmare` (BubbleCount streak ≥5, `:371`), `pop_the_thought` (TotalBubblesPopped ≥1000, `:303`), `typing_tutor` (lock card 0 errors, `:401`), `obedience_reflex` (≥3 phrases <15s, `:408`), `mercy_beggar` (AttentionCheckFailures ≥3, `:444`), `clean_slate` (mind wipe ≥60s, `:458`), `corner_hit` (bouncing-text corner hit, `:467`), `neon_obsession` (20 avatar clicks in 10s, `:485`).
- **Hardcore:** `what_panic_button` (session with no-panic enabled, `:573`), `relapse` (restart within 10s of ESC/panic, `:533`), `total_lockdown` (StrictLock + !PanicKey + PinkFilter simultaneously, `:242`), `system_overload` (Bubbles + BouncingText + Spiral all on, `:230`).

**Unlock logic.** Everything funnels through `TryUnlock(string id)` (`AchievementService.cs:623-665`): idempotent, validates against `Achievement.All`, saves immediately, and (unless `SuppressPopups`) fires `AchievementUnlocked` + two haptic patterns. Consumed by `App.OnAchievementUnlocked` (`App.xaml.cs:1302-1328`, shows popup + sound + optional Discord webhook), plus MainWindow and AvatarTubeWindow handlers.

**Observation mechanism.** Push-based via direct `App.Achievements?.Track*` calls, **plus one 1-second `DispatcherTimer`** (`TrackTimeBasedProgress`, `:138-251`) that reads `App.Settings.Current` toggles + `App.Overlay.IsRunning` to accumulate pink/spiral minutes and detect the live-combination achievements (`system_overload`, `total_lockdown`). Every external feed site:

| Caller | Method | Drives |
|---|---|---|
| `FlashService.cs:1100` | `TrackFlashImage()` | retinal_burn |
| `BubbleService.cs:304` | `TrackBubblePopped()` | pop_the_thought + bubble milestone points |
| `BubbleCountResultWindow.xaml.cs:189/210` | `TrackBubbleCountResult()` | mathematicians_nightmare |
| `LockCardWindow.xaml.cs:367` | `TrackLockCardCompletion(...)` | typing_tutor, obedience_reflex (+ quest) |
| `MindWipeService.cs:300` | `TrackMindWipeDuration()` | clean_slate |
| `BouncingTextService.cs:272/281` | `TrackCornerHit()` | corner_hit |
| `VideoService.cs:1935/1946/1948/1984` | `TrackAttentionCheckPassed/Failed`, `TrackVideoAttentionCheckFailed`, `TrackVideoWatched` | mercy_beggar (+ video minutes quest) |
| `SessionEngine.cs:198/267/297/326` | `TrackSessionStart/Abandoned/Complete/PanicPressed` | deep_sleep, what_panic_button, sofa_decor, look_but_dont_touch, morning_glory, player_2_disconnected, relapse |
| `MainWindow.xaml.cs:824` | `TrackAltTab()` | blocks player_2_disconnected |
| `MainWindow.xaml.cs` / `AvatarTubeWindow.xaml.cs:2048` | `TrackAvatarClick()` | neon_obsession |
| `ProgressionService.cs:62/99` | `TrackXPEarned`, `CheckLevelAchievements` | progression tier |
| `SkillTreeService.cs:774` | `TrackSkillPointsEarned` | counter only |

Counters live on `AchievementProgress` and are mutated inside the matching `Track*` method, which then inline-checks its threshold via `TryUnlock`.

**Purpose today.** Recognition/cosmetic with light reward hooks: unlock toast, sound + haptics, optional Discord broadcast, cloud sync, and a gallery counter in MainWindow. **Achievements grant no XP or skill points.** The one adjacent reward is the **bubble milestone** (+1 skill/sparkle point per 100 bubbles, `AchievementService.cs:309-320`) — a counter milestone wired through the same popup but **not** a registered achievement (synthesized id `bubble_milestone`, not in `Achievement.All`).

**Flags:**
- **No condition on the definition object** → `All` and unlock logic drift (the `bubble_milestone` "achievement" exists only in code).
- **Session-name matching is substring-based** (`sessionName.ToLowerInvariant().Contains("...")`, `:581-612`) → renaming/localizing a session silently breaks those unlocks.
- `TrackAttentionCheckPassed`/`TrackVideoAttentionCheckFailed` increment counters but call no `TryUnlock` — **dead counters** with no achievement behind them today (likely placeholders).
- Several continuous counters reset to 0 on startup (`:47-51`) by design.

---

### 1C. SKILL TREE + XP/LEVELS

**Where it lives**
| File | Role |
|------|------|
| `Models/SkillTree.cs` | `SkillDefinition` + the hardcoded `SkillDefinition.All` catalog (`:42-351`) + `SkillEffectType` enum (`:357-403`) |
| `Services/SkillTreeService.cs` | Purchase validation, effect calc, lucky procs, Pink Rush timer, streak shields, reroll bonuses |
| `Services/ProgressionService.cs` | XP/level engine |

**Skill nodes are hardcoded C#** (no JSON). `SkillDefinition.All` is a static `List<SkillDefinition>` (`SkillTree.cs:42`). Purchase state lives in `App.Settings.Current.UnlockedSkills` (`List<string>` of IDs) and is **server-authoritative** — `SkillTreeService.PurchaseSkillAsync` (`SkillTreeService.cs:115`) requires login and calls `App.ProfileSync.PurchaseSkillAsync(skillId)`.

**One skill node, as defined today** (`SkillTree.cs:76-88`):
```csharp
new()
{
    Id = "sparkle_boost_1",
    Name = "Sparkle Boost",
    Icon = "✨⚡",
    Tier = 2,
    Cost = 8,
    PrerequisiteId = "pink_hours",
    FlavorText = "Good girls deserve extra sparkles! ...",
    Description = "+10% XP from all sources. Adds pink glow ...",
    EffectType = SkillEffectType.XpMultiplier,
    EffectValue = 0.10
},
```
`SkillDefinition` fields (`SkillTree.cs:9-37`): `Id, Name, Icon, FlavorText, Description, Tier, Cost, PrerequisiteId (single), IsSecret, SecretRequirementDesc, EffectType, EffectValue`. There are **23 nodes** across 5 tiers. Every node either grants an XP/economy perk (XpMultiplier, LuckyFlash/LuckyBubble, PinkRush, streak shields/recovery, quest rerolls, streak/perfect-week XP) or toggles a **stat-display panel** (StatDisplay/LifetimeStats). **No node gates a content feature.**

**XP / levels.** `ProgressionService.AddXP` (`:25`) applies the skill multiplier (`adjustedAmount = amount * App.SkillTree.GetTotalXpMultiplier()`, `:55-56`), adds to `settings.PlayerXP`, and runs a level-up loop (`:81-125`) that fires `LevelUp`, updates `HighestLevelEver`, awards skill points via `App.SkillTree.OnLevelUp`, and checks level achievements. Level curve `GetXPForLevel` (`:130-176`) is piecewise-linear to L150 then 3% compound. 20 `AddXP` call sites exist (Flash, Bubble, BubbleCount, Subliminal, BouncingText, Video, Mantra, KeywordTrigger ×2, Quest ×2, Session via MainWindow `:17874`, LockCard, PopQuiz, AvatarInteraction, daily streak bonus).

**THE GATING QUESTION — answered plainly: the skill tree gates NO content feature, and levels gate NO content feature. All feature gating has been deliberately removed.**
- The level-gate helper `ProgressionService.IsUnlocked(string feature, int level)` (`:244-253`) still encodes `spiral`/`pink_filter` ≥10, `bubbles` ≥20 — but a full-codebase search finds **zero callers** of this 2-arg overload. It is **orphaned dead code**.
- `AvatarTubeWindow.IsAvatarSetUnlocked(...)` hardcodes `return true;` with comment *"Feature level gating has been removed — every avatar set is always unlocked."* (`AvatarTubeWindow.xaml.cs:615-618`).
- `CompanionService.IsCompanionUnlocked(...)` hardcodes `return true;` *"Companion level gating has been removed — every companion is available from level 1."* (`CompanionService.cs:323-334`). `CompanionDefinition.RequiredLevel` is still populated as data but **never read for gating**.

**What the skill tree DOES still gate is only its own perks**, all checked via `App.SkillTree.HasSkill("...")`: XP multipliers (`ProgressionService.cs:55`), lucky procs (`FlashService.cs:964`, `BubbleService.cs:288`), sparkle VFX tiers, quest rerolls (`QuestProgress.cs:51,71`), streak shields / oopsie insurance, perfect-week bonus (`QuestService.cs:832`), and stat-panel visibility in the Enhancements tab.

**Two adjacent systems, for clarity:**
- `Models/FeatureDefinition.cs` is **unrelated** to skill tree / level gating. It's the feature palette for the **Session Editor** (each feature carries `XPBonus`/`DifficultyWeight` used to compute a session's reward/difficulty). No `RequiredLevel` field. The `spiral`/`pink_filter`/`bubbles` IDs here are the same strings the orphaned `IsUnlocked` references — a vestige of once-gated features.
- `Models/RoadmapDefinition.cs` + `Services/RoadmapService.cs` are a **separate** "Transformation Roadmap" — a 3-track, photo-submission journey gated **sequentially** (not by level/XP/skills), persists to `roadmap.json`, awards a cosmetic badge, **no XP**. Completely decoupled from the rest.

**Observation mechanism.** Direct calls, not events. Award path = `App.Progression.AddXP(...)`. The skill tree reads XP state (multiplier pull) and receives `OnLevelUp` pushes; it emits `SkillUnlocked`, `PinkRushStarted/Ended`, `LuckyProc` (`SkillTreeService.cs:31-46`) for VFX, and tracks time-of-day usage via `TrackTimeOfDayUsage()` for secret-skill unlocks. ProgressionService emits `LevelUp`/`XPChanged` (`:13-14`).

**Purpose today.** XP/levels are **pure progression + leaderboard + economy + cosmetics** with no feature gating. Levels drive a numeric rank, a `GetTitle()` label (`:231`), Discord presence/milestones, achievements, and the cloud leaderboard (server stores total XP). Skill points (1/level) buy self-contained perks. **All content is available regardless of level.**

**Flag:** `ProgressionService.cs:101` comment says "5 points per level" but `SkillTreeService.PointsPerLevel = 1` (`:26`) — stale comment.

---

## PART 2 — Feature-module inventory (added roughly since v5)

Legend: **Feeds** = calls a gamification sink directly · **Invisible** = observes player behavior but never calls a sink.

### Webcam / gaze / attention / blink cluster — **ENTIRELY INVISIBLE**

| Module | What it does | Key runtime signals (events) | Gamification today |
|---|---|---|---|
| `WebcamTrackingService.cs` | Offline webcam eye/gaze/face engine; the sensor everything else subscribes to | `OnBlink, OnLongStare, OnMouthOpen, OnTongueOut, OnGazeMove, OnGazeSide, OnFaceLost, OnFaceFound, OnHeadPose` (`:315-332,433`) | **Invisible** — no sink calls. Pure event source. |
| `GazeFocusService.cs` | Pop bubbles / dismiss flashes by gaze dwell or blink | `OnActiveChanged`, dwell-pop in `AdvanceBubbleDwell/FlashDwell/FloatingTextDwell` (`:433/460/512`) | **Invisible itself.** It calls downstream `Bubble.Pop()` / `FlashService.GazePop` / `VideoService.GazeClick`, which *may* award XP on their own — so gaze substitutes for a click into existing pipelines, but the gaze action records nothing. |
| `GazeContentScreenPolicy.cs` | Static screen-placement helper | none | **Invisible** |
| `GazeDebugCursorService.cs` | Debug gaze dot | none meaningful | **Invisible** |
| `BlinkTrainerService.cs` (+ `BlinkTrainerAssetPool.cs`) | Overlay swaps to a random asset on every blink | `StateChanged`, `HandleBlink`→`ShowRandom` (`:222/229`) | **Invisible** — blinks earn nothing |
| `AttentionCheckService.cs` | Webcam fixation check (ring); defines `PassXp=20`/`FailXpPenalty=10` and `OnPass`/`OnFail` | `OnPass`/`OnFail` (`:73-74`) | **Invisible + DEAD.** Per `App.xaml.cs:1114-1124` the service is constructed but **never `Start()`'d** and its OnPass/OnFail is **unwired** ("scrapped pre-ship per design call"). |
| Webcam calibration (`WebcamCalibrationWindow`, `WebcamCalibrationData.cs`, `CalibrationSoundService.cs`) | Calibration UI + persisted data | "calibration completed" lives in window code | **Invisible** — completing calibration grants nothing |
| `FocusGameService.cs` (+ `FocusGameBucket.cs`) | Lab "Focus Training" gaze game | **Skeleton** — `RefreshBuckets`, `ValidateBucketSelection` only; no round loop yet (`:16`) | **Invisible + not functional yet** |

> **Trap:** `mercy_beggar` looks gaze/attention-related but is fed by the **legacy video mandatory-playback attention check** in `VideoService.cs:1946`, **not** by `AttentionCheckService`. No quest/achievement/skill node references any module in this cluster.

### Companion / AI / session / keyword / OCR cluster

| Module | What it does | Key signals | Gamification today |
|---|---|---|---|
| `CompanionService.cs` | Per-companion XP/leveling + companion-specific XP modifiers | `AddCompanionXP` (`:237`); `CompanionLevelUp, CompanionSwitched, XPDrained, XPAwarded` (`:71-74`) | **Is a SINK, not a feeder.** `ProgressionService.AddXP:75` pushes into it. **Companion XP is a fully separate ledger** from player XP (`Models/CompanionProgress.cs`: per-companion `Level` 1–100, formula `500 + level*50` at `:134`). |
| `SessionEngine.cs` | Runs timed conditioning sessions | `SessionStarted/Stopped/Completed/PhaseChanged/ProgressUpdated` (`:192-306`) | **Feeds achievements directly** (`TrackSessionStart/Abandoned/Complete/PanicPressed` `:198/267/297/326`); quests **indirectly** (via `AchievementService.cs:615`). Session **XP** fires in `MainWindow.xaml.cs:17874` (`AddXP(e.XPEarned, XPSource.Session)`), **not** in SessionEngine. |
| `SessionManager.cs` | CRUD/persistence of session definitions | `SessionAdded/Removed/Reloaded` | **Invisible** |
| `AutonomyService.cs` | Idle/context-driven autonomous effect triggering | `ActionTriggered, AnnouncementMade` (`:118-119`), `OnContextTrigger` (`:1688`) | **Invisible itself.** Modulates XP only via `XPContext.TriggeredByAutonomy` (companion bonus); effects it fires award their own XP. |
| `KeywordTriggerService.cs` | Matches OCR/subliminal keywords → runs actions | `CheckOcrWords`, action `switch` (`:1130`) | **Feeds Progression (XP).** `AddXP(..., XPSource.KeywordTrigger)` at `:1157` and `:1378` (×1.5 in-session). Note `ExtendSessionAction`/`ChasterAddTimeAction` are **stubs** (`:1166/1170`). |
| `ScreenOcrService.cs` | Periodically OCRs screens, pushes words to KeywordTrigger | `DispatchOcrResultsAsync`→`CheckOcrWords` (`:127/180`) | **Invisible** — XP happens inside KeywordTrigger, not here |
| `WindowAwarenessService.cs` | Watches foreground window, categorizes activity for AI | `ActivityChanged, StillOnActivity` (`:65-66`) | **Invisible** — drives AI commentary only |
| `AiService.cs` (+ `Services/AIService/`) | LLM client (cloud + local Ollama) | `GetBambiReplyAsync`, awareness/keyword/lockscreen/video reactions | **Invisible** — zero sink calls across the whole folder. AI chat earns nothing. |
| `MantraService.cs` | Mantra-typing minigame (anti-cheat + streak) | `MantraCompleted, StreakChanged, SessionComplete` (`:69-71`) | **Feeds Progression + Quests directly:** `AddXP(30+bonus, XPSource.Mantra)` (`:63`), `App.Quests?.TrackMantraCompleted()` (`:64`). |
| `AvatarTubeWindow` (companion UI) | Avatar clicks/bubbles/AI chat | — | **Feeds** exactly two: `TrackAvatarClick()` (`:2048`, neon_obsession) + `AddXP(5, XPSource.AvatarInteraction)` on bubble pop (`:3577`). **AI chat path earns nothing.** |

### Haptics / remote / mods / quiz / Deeper / overlays cluster

| Module | What it does | Key signals | Gamification today |
|---|---|---|---|
| `HapticService.cs` (+ `Services/Haptics`, `HapticSettings.cs`, `HapticTrack.cs`) | Lovense/Buttplug toy integration | `TriggerAsync, LevelUpPatternAsync, AchievementPatternAsync, HapticTriggered, ConnectionChanged` | **Is a SINK/consumer.** `AchievementService.cs:655-656` and `QuestService.cs:871-872` call into it. Never feeds gamification. |
| `RemoteControlService.cs` | Remote/partner control of the app via server relay | `CommandReceived` (`:546`), `SessionStarted/Ended`, `ExecuteCommand` table (`:903-979`) | **Invisible directly.** Triggered effects award their own XP, but "a remote command arrived" records nothing. |
| `ModService.cs` / `ContentPackService.cs` / `ModCreatorWindow` (+ `BuiltInMods.cs`, `ModManifest.cs`) | Load/install/activate `.ccpmod` content packs | `InstallModAsync` (`:106`), `ActivateMod` (`:482`), `ModChanged` (`:41`) | **Invisible.** Installing/activating content earns nothing. (Skill tree only *reads* mod labels for cosmetic lucky-proc text.) |
| `QuizService.cs` / `QuizSessionGenerator.cs` / `QuizWindow` / `QuizReportWindow` | Full quiz minigame (banks, grading, report) | quiz answered/passed handled in windows | **Invisible — confirmed zero sink calls across all four files.** No `QuestCategory.Quiz`, no quiz achievement. |
| `PopQuizService.cs` / `PopQuizWindow` | Lightweight pop-up quiz | correct-answer at `PopQuizWindow.xaml.cs:145-162` | **Partial — XP only.** `AddXP(25, XPSource.Other)` (`:156`). No achievement, no quest, anonymized as `Other`. |
| **Deeper** (`Services/Deeper/`, `Views/Deeper/` incl. `EnhancementPlayerWindow(.Mission3).cs`, `Models/Deeper/`, `MainWindow.DeeperHub.cs`) | Major newer "enhancement player" (HypnoTube-style timeline/rule/trigger engine + editor + player) | enhancement played / rule fired / trigger hit (internal to `EnhancementEngine`) | **Invisible — confirmed zero sink calls across the entire feature.** The single largest gamification blind spot. (Note: it has its own `TutorialEventBus` for tutorials, unrelated to gamification.) |
| `LockCardService.cs` / `LockCardWindow` | Phrase-lock typing card | completion at `LockCardWindow.xaml.cs:355-367` | **Feeds (via the window):** `AddXP(..., XPSource.LockCard)` (`:362`) + `TrackLockCardCompletion(...)` (`:367`) → typing_tutor, obedience_reflex + quests `obedience_drill_d`/`phrase_mastery_w`. `LockCardService.cs` itself is clean. |
| `MindWipeService.cs` | Looping audio/visual blanking | `MindWipeTriggered` (`:61`), duration at `:300` | **Feeds achievement only:** `TrackMindWipeDuration(elapsed)` (`:300`) → clean_slate. No XP, no quest. |
| `LockdownService.cs` | Time-boxed exit/feature restriction | `LockdownActivated/Deactivated/CountdownTick` (`:36-38`) | **Invisible.** The `total_lockdown` achievement is detected by AchievementService's settings poller, **not** by this service's runtime event. |
| `WallpaperService.cs` | Overrides desktop wallpaper | activate/deactivate | **Invisible** |
| `BouncingTextService.cs` | DVD-style bouncing affirmation text | corner-hit `:272/281`, XP `:300` | **Feeds:** `TrackCornerHit()` (corner_hit) + `AddXP(15, XPSource.BouncingText)`. Also feeds `system_overload` via the poller. No dedicated quest (folds into `Combined`). |
| `BrainDrainService.cs` | Ambient "brain drain" audio overlay | `BrainDrainTriggered` (`:30`) | **Invisible directly; tracked via poller.** AchievementService's 1s timer calls `TrackBrainDrainMinutes` (`:219`) → quest `Combined` only. No XP, no achievement. |

> **Pattern note for whoever does the wiring:** for features that *do* feed gamification, the calls almost always live in the **UI window** (LockCardWindow, PopQuizWindow, BouncingText overlay, AvatarTubeWindow) or in **AchievementService's poller** — **not** in the corresponding `*Service.cs`. Grepping only `Services/*.cs` will produce false negatives.

---

## PART 3 — Gap & dead-content analysis

### 3A. New features with ZERO gamification coverage (high-value targets)

In rough priority order by size/engagement:

1. **Deeper enhancement player** — the biggest, newest subsystem, completely invisible. Playing/finishing an enhancement, firing rules/triggers, completing a "mission" — none of it is observed. No XP source, no quest category, no achievement, no skill node.
2. **Full Quiz system** (`QuizService`/`QuizSessionGenerator`/`QuizWindow`/`QuizReportWindow`) — answering, passing, perfect scores, category completion all invisible. (Only the unrelated PopQuiz earns a flat 25 `XPSource.Other`.)
3. **Entire webcam/gaze/attention/blink stack** — `WebcamTrackingService` emits a rich event stream (`OnBlink`, `OnGazeSide`, `OnLongStare`, `OnFaceLost`, calibration-complete) and **nothing in gamification listens**. Obvious targets: blink counts, sustained-gaze minutes, calibration-complete, gaze-pop counts (GazeFocusService), and the **defined-but-unused** `AttentionCheckService.PassXp`/`FailXpPenalty`/`OnPass`/`OnFail`.
4. **Mods / content packs** — installing or activating community content earns nothing.
5. **Remote control** — receiving remote commands / partner sessions records nothing directly.
6. **Lockdown** — `Activate(TimeSpan)` is a meaningful commitment event that records nothing (only the passive 3-toggle `total_lockdown` combo is detected).
7. **Companion AI chat** — sending/receiving chat messages earns nothing (companion *XP* exists but is fed only as a mirror of player `AddXP`, not by chatting).
8. **AI-driven effects, autonomy, window-awareness, OCR** — all invisible except where they happen to trigger an effect that already awards XP.

### 3B. Stale / dead / broken existing content

- **`QuestCategory.Mantra`**: in the enum and has a tracker, but **no quest uses it** and `ParseCategory` can't parse `"mantra"` (`Quest.cs:136-152`) → mantra quests can never be credited. **Dead category.**
- **`ProgressionService.IsUnlocked(feature, level)`** (`:244-253`): orphaned level-gate code (spiral≥10, pink_filter≥10, bubbles≥20) with **zero callers**. Misleading — implies gating that no longer exists.
- **`CompanionDefinition.RequiredLevel`** (50/75/100/125/150): still populated as data but **never read** — gating removed (`CompanionService.cs:323-334`). Stale data.
- **`AttentionCheckService`**: constructed but never started; `OnPass`/`OnFail` unwired; `PassXp`/`FailXpPenalty` defined but never awarded ("scrapped pre-ship", `App.xaml.cs:1114-1124`). **Dead service** that looks like working gamification.
- **`TrackAttentionCheckPassed` / `TrackVideoAttentionCheckFailed`** counters (`AchievementService.cs:672/685`): incremented but no `TryUnlock` reads them — **dead counters / placeholder achievements**.
- **`bubble_milestone`**: a synthesized "achievement" not in `Achievement.All` (`AchievementService.cs:309-320`) — works, but bypasses the registry; flag for consistency.
- **Session-name substring matching** for `morning_glory`/`player_2_disconnected`/`sofa_decor`/`look_but_dont_touch` (`AchievementService.cs:581-612`): will silently break if those built-in sessions are renamed or localized. Not broken *today*, but fragile.
- **`ProgressionService.cs:101`** comment ("5 points per level") contradicts `PointsPerLevel = 1`. Cosmetic/stale.

### 3C. The integration seam — how a new quest/achievement would listen to a newer feature

**Current reality:** the "observation layer" is not rich enough to cover the newer features. A new quest or achievement cannot "listen" to anything; it can only react to a `Track*`/`AddXP` call that some code makes. The concrete wiring paths today are:

- **A new achievement** needs (a) a definition added to `Achievement.All` (`Achievement.cs`), and (b) imperative unlock logic added inside an `AchievementService.Track*` method — and (c) **a feature has to call that `Track*`**. If the feature emits a C# event instead (e.g. `WebcamTrackingService.OnBlink`), someone must add a subscriber that calls the tracker; the achievement system does not subscribe to anything itself.
- **A new quest** needs (a) a `QuestCategory` enum value (`Quest.cs:14`), (b) a `ParseCategory` case for the server string (`Quest.cs:136`), (c) a `Track*` method on `QuestService` that calls `UpdateQuestProgress(thatCategory, amount)`, and (d) a call site that invokes it — conventionally placed inside the relevant `AchievementService.Track*` method (the established pattern), or a direct `App.Quests?.Track*` call from the feature.
- **A new XP source** needs an `XPSource` enum value (`CompanionService.cs:10`) and an `AddXP(..., XPSource.New)` call in the feature.

**Specific gaps that block coverage of the newer features (i.e., what would have to be added):**

1. **Deeper** emits no gamification signal at all. To cover it, add explicit calls at enhancement start/finish and rule/trigger fire inside `Services/Deeper/EnhancementEngine.cs` (and/or `EnhancementPlayerWindow`). No counters, no events the layer can see today. The existing `TutorialEventBus` shows the team is comfortable with a string-keyed bus — a similar `GamificationEventBus` could be the cleaner long-term seam, but nothing like it exists yet.
2. **Webcam/gaze/blink** emits rich C# events (`OnBlink`, `OnGazeSide`, `OnLongStare`, `OnFaceLost`) but **no subscriber feeds gamification**. Coverage requires a new subscriber (likely in `AchievementService` or a small new bridge) that increments counters and calls `Track*`/`AddXP`. Also revive or repurpose the dormant `AttentionCheckService.OnPass/OnFail` wiring.
3. **Quiz** has no sink calls and no `QuestCategory`/`XPSource`/achievement. Full new vertical needed (enum value + tracker + call site in `QuizWindow`/`QuizReportWindow`, mirroring how `LockCardWindow` does it).
4. **Mods, Remote, Lockdown** each raise a clean C# event (`ModChanged`, `CommandReceived`, `LockdownActivated`) — straightforward to subscribe and feed, but **no subscriber exists today**.
5. **The minute-based polling pattern is centralized in `AchievementService.TrackTimeBasedProgress` (the 1s timer)** and currently only knows about spiral/pink/braindrain via settings toggles. Any new "spend N minutes in feature X" objective for a newer feature must be added there (or the feature must self-report minutes), because nothing else polls.

**Bottom line:** the event/stat layer is **not** rich enough to absorb new content for the post-v5 features. Every new hook requires touching the specific feature module (or adding a subscriber/poller) **plus** the enum + tracker + (for quests) the parser. There is no generic "emit a gameplay event" path that quests/achievements can opt into.

---

## Open questions for planning

1. **Build a real event bus, or keep imperative `Track*` calls?** A `GamificationEventBus` (precedent: `TutorialEventBus`) would let new content subscribe without editing each feature, but it's a bigger refactor. The current pattern requires touching 2-4 places per hook. Which way do we invest?
2. **Deeper is the biggest blind spot — what should it reward?** Enhancement completion XP? A "Deeper" quest category? Achievements for #enhancements played / total Deeper minutes / completing a bundled enhancement? This needs design before wiring `EnhancementEngine`.
3. **Webcam/gaze content — opt-in or universal?** Gaze/blink quests/achievements would only progress for users who enabled and calibrated the webcam. Do we want gamification that a large fraction of users can never complete? (Same question for Lovense/haptics, Remote control, Roadmap photos.)
4. **Revive `AttentionCheckService`?** It already defines `PassXp`/`FailXpPenalty`/`OnPass`/`OnFail` but was scrapped pre-ship. Is it coming back (in which case wire it) or should the dead code + dead counters be removed?
5. **Quiz vs PopQuiz:** the full Quiz system is invisible while PopQuiz grants a flat 25 `XPSource.Other`. Do we add a proper `XPSource.Quiz` + quiz quests/achievements, and unify the two quiz paths?
6. **Companion XP integration:** companion levels are a separate ledger fed only as a mirror of player `AddXP`. Should companion interactions (chat, voice, switching) earn anything, and should achievements/quests ever read companion level?
7. **Dead-content cleanup scope:** confirm we can delete/repair `QuestCategory.Mantra`, the orphaned `ProgressionService.IsUnlocked`, the unused `CompanionDefinition.RequiredLevel`, the dead attention-check counters, and the stale comments — do any of these have server-side or roadmap dependencies before removal?
8. **Definition storage:** quests are server-driven JSON (good for live content), but achievements and skill nodes are hardcoded C# requiring an app release to change. Do we want achievements/skills to move to a server-fetched definition model too, so new gamification content can ship without a client update?
9. **Skill tree's purpose going forward:** it gates nothing and is a pure XP-efficiency/perk economy. Is that the intended end state, or do we want it to gate or unlock any of the newer features (Deeper presets, companions, gaze modes)?
10. **Session-name fragility:** should the session-specific achievements key off a stable session ID instead of localized name substrings before we add more session-based content?
