# Gamification Content Plan — Achievements v2

**Date:** 2026-05-30
**Companion to:** `docs/gamification-audit.md`
**Scope:** 30 new achievements (19 free + 11 patron), the model/category changes they need, and the per-achievement wiring map for the `GamificationBridge`.

---

## Design rules (locked)

1. **Free + shipped features only** for the free set. No achievement rides on a paid feature or a not-ready feature (this is why Mantra and the standalone quiz/webcam-minigame ideas are not in the free set).
2. **Paid achievements live in a separate section with their own counter.** The free section shows its own completion (e.g. 19/19); the patron section shows its own (e.g. 3/11). The two counts are never summed into one global number. This is the rule that keeps the split honest instead of putting paywalled holes in everyone's gallery.
3. **All achievements stay cosmetic.** No XP, no skill points, same as the existing 30. Paid achievements granting XP would sell leaderboard position, so this line holds.
4. **Patron achievements are entitlement-gated on unlock.** `TryUnlock` for an exclusive achievement checks tier first. A user who earned one and later downgrades keeps it (you earned it).
5. Patron set is shown to free users as a labeled, locked collection (perk + soft upgrade nudge), framed as a thank-you, not a taunt.

---

## Model + category changes

**`Models/Achievement.cs`**
- Add `public bool IsExclusive { get; init; } = false;` to `Achievement`.
- Add to `AchievementCategory` enum: `Deeper`, `Creator`. (Companion and keyword achievements fold into existing categories for now; a dedicated `Companion` category is the natural future home if the gallery gets crowded.)

**UI (achievements gallery)**
- Render two sections: standard, and "Patron" (filters `IsExclusive == true`), each with its own progress counter. Within each section, keep grouping by `AchievementCategory`.

**`Models/AchievementProgress.cs` — new counters**
`EnhancementsPlayed`, `DeeperMinutes`, `EnhancementsBuilt`, `ModsInstalled`, `ActivatedModIds` (a set, for distinct-count), `KeywordTriggersFired`, `CompanionMessages`, `QuizzesPassed`, `QuizFailStreak` (resets on pass), `BlinkTrainerBlinks`, `GazePops`, plus transient per-session trackers for `RemoteCommandsThisSession`, `DistinctTriggerTypesThisPlay`, and lockdown start time.

---

## FREE SET (19)

### Category: Deeper (new)
| id | name | requirement | flavor | tag |
|---|---|---|---|---|
| `going_deeper` | Going Deeper | Play your first enhancement | First descent. The water's warm down here. | |
| `down_the_rabbit_hole` | Down the Rabbit Hole | Play 25 enhancements | Twenty five trips down. You know the way by now. | |
| `permanent_resident` | Permanent Resident | 10 hours total in the Deeper player | Ten hours under. You live here now, don't you? | |
| `directors_cut` | Director's Cut | Finish a featured enhancement start to end | You sat through the whole thing. Good girl. | |
| `wired_in` | Wired In | Play an enhancement with webcam triggers active | Camera on, eyes tracked. Nowhere to hide now. | |
| `dont_look_away` | Don't Look Away | Hold gaze through a full webcam enhancement | You held it the entire time. Not one glance away. | secret |
| `on_rails` | On Rails | Fire 5+ different trigger types in one enhancement | Every trigger firing at once. No driver needed. | secret |

### Category: Creator (new)
| id | name | requirement | flavor | tag |
|---|---|---|---|---|
| `not_a_video_editor` | Not a Video Editor | Build your first enhancement | btw, not a video editor. and yet. | |
| `on_the_shelf` | On the Shelf | Publish an enhancement to the catalogue | You made something and put it out there. Look at you. | |
| `featured` | Featured | Get an enhancement featured | Front of the catalogue. Everyone's going to see your work. | |
| `mad_scientist` | Mad Scientist | Build an enhancement using 5+ triggers | Five triggers in one build. What are you cooking? | |
| `modder` | Modder | Install your first mod | First mod installed. The rabbit hole goes deeper than you thought. | |
| `curator` | Curator | Activate 10 different mods | Ten mods deep. Quite the collection. | |
| `community_supported` | Community Supported | Run a mod made by someone else | Running someone else's work. We're all in this together. | secret |

### Category: Sessions & Time (existing)
| id | name | requirement | flavor | tag |
|---|---|---|---|---|
| `magic_word` | Magic Word | Fire your first keyword trigger | Said the word, felt the pull. Just like that. | |
| `pavlov` | Pavlov | Fire 500 keyword triggers lifetime | Five hundred times. The bell rings, you respond. No thinking required. | |

### Category: Minigames (existing — joins the avatar-interaction theme)
| id | name | requirement | flavor | tag |
|---|---|---|---|---|
| `pleased_to_meet_you` | Pleased to Meet You | Send your first message to the companion | First words with her. This is the start of something. | |
| `pillow_talk` | Pillow Talk | Exchange 100 messages with the companion | A hundred messages in. She's getting to know you. | |
| `best_friends` | Best Friends | Reach a companion level milestone | She's leveled up right alongside you. Inseparable now. | |

---

## PATRON SET (11) — `IsExclusive = true`

### Category: Minigames (quiz + webcam minigames + companion memory)
| id | name | requirement | flavor | tier | tag |
|---|---|---|---|---|---|
| `top_of_the_class` | Top of the Class | Perfect score on a quiz | Perfect score. Empty head, perfect score. Funny how that works. | Quiz | |
| `teachers_pet` | Teacher's Pet | Pass 25 quizzes | Twenty five quizzes passed. Such a good student. | Quiz | |
| `honor_roll` | Honor Roll | Clear a full quiz category | Cleared the whole category. Nothing left to learn here. | Quiz | |
| `held_back` | Held Back | Fail three quizzes in a row | Three failures in a row. Maybe the material's too hard. Maybe that's the point. | Quiz | secret |
| `blink_and_youll_miss_it` | Blink and You'll Miss It | 100 blinks logged in the blink trainer | A hundred blinks tracked. Every one of them counted. | Webcam minigame | |
| `hands_free` | Hands-Free | Pop 50 bubbles by gaze alone | Fifty pops, no hands. Just your eyes doing the work. | Webcam minigame | |
| `she_remembers` | She Remembers | Companion recalls something across sessions | She brought up something from before. She doesn't forget. | Companion T2 | |

### Category: Hardcore (lockdown + remote)
| id | name | requirement | flavor | tier | tag |
|---|---|---|---|---|---|
| `locked_in` | Locked In | Trigger your first lockdown | Door's shut. You chose this. | Lockdown (Exclusives) | |
| `throw_away_the_key` | Throw Away the Key | Sit through a 60+ minute lockdown | A full hour locked down. You weren't going anywhere anyway. | Lockdown | secret |
| `hand_over_control` | Hand Over Control | Complete your first remote-controlled session | You gave someone else the wheel. Brave. | Remote (paid) | |
| `puppet_strings` | Puppet Strings | Take 100 remote commands in one session | A hundred commands, one session. Whose hands are these? | Remote | secret |

---

## Wiring map (for CC)

The bridge subscribes to a signal and, on each, bumps a counter on `AchievementProgress` and calls `TryUnlock`. Three states below:
- **EXISTS** — the feature already emits a C# event the bridge can subscribe to today.
- **EMIT** — the feature has the data internally but emits no event yet; CC adds the event first.
- **SERVER** — depends on server state, not a pure client signal; softer, treat as a second pass.

| achievement | signal | state | notes |
|---|---|---|---|
| `going_deeper` / `down_the_rabbit_hole` | `EnhancementEngine` enhancement-completed | **EMIT** | Add `EnhancementCompleted` (carry id, featured flag, distinct-trigger-types count, webcam-trigger-used bool). Drives the whole Deeper block. |
| `permanent_resident` | Deeper player active minutes | **EMIT** | Either accumulate on stop, or expose `IsPlaying` and let the existing 1s `AchievementService` poller add minutes (cheapest, matches the spiral/pink pattern). |
| `directors_cut` | `EnhancementCompleted` + featured flag | **EMIT** | Featured flag comes from enhancement metadata. |
| `wired_in` | `EnhancementCompleted` + webcam-trigger-used | **EMIT** | Set the bool when any gaze/blink/face trigger fired during the play. |
| `dont_look_away` | Deeper play + sustained-gaze duration vs length | **EMIT** | Needs Deeper to compare gaze-held time against enhancement length, or consume `WebcamTrackingService.OnLongStare` during a Deeper session. Most complex of the Deeper set. |
| `on_rails` | `EnhancementCompleted` + distinct-trigger-types count | **EMIT** | Count distinct trigger types fired per play, report on complete. |
| `not_a_video_editor` / `mad_scientist` | `DeeperEditorWindow` save | **EXISTS** | `TutorialEventBus` already emits `FileSaved` (and `RuleAdded`/`EffectAdded`) from `DeeperEditorWindow.Unified.cs`. Subscribe to `FileSaved`; `mad_scientist` reads trigger count at save. |
| `on_the_shelf` | catalogue submit success | **EMIT** | Fire on a successful submission POST in the publish flow (client knows its own request succeeded). |
| `featured` | catalogue approval state | **SERVER** | Manual approval is server-side. Surface via catalogue lookup / entitlement check on launch. Second pass. |
| `modder` | `ModService.ModChanged` / `InstallModAsync` | **EXISTS** | Counter `ModsInstalled`. |
| `curator` | `ModService.ActivateMod` / `ModChanged` | **EXISTS** | Track distinct ids in `ActivatedModIds`, unlock at 10. |
| `community_supported` | mod activation + manifest author != self | **EXISTS** | Read `ModManifest` author on activate. |
| `magic_word` / `pavlov` | `KeywordTriggerService` action execution | **EXISTS** | Hook the action `switch` (~`KeywordTriggerService.cs:1130`) or the `AddXP(...KeywordTrigger)` sites (`:1157`/`:1378`). Counter `KeywordTriggersFired`. |
| `pleased_to_meet_you` / `pillow_talk` | companion chat send | **EMIT** | AI chat currently fires nothing (per audit). Add a hook in the chat-send handler in `AvatarTubeWindow`. Counter `CompanionMessages`. |
| `best_friends` | `CompanionService.CompanionLevelUp` | **EXISTS** | Subscribe, check level threshold. |
| `top_of_the_class` / `teachers_pet` / `honor_roll` / `held_back` | quiz result | **EMIT** | Quiz fires nothing today. Add `QuizCompleted(score, passed, perfect, category)` from `QuizReportWindow`. `held_back` uses `QuizFailStreak` (reset on pass). All four entitlement-gated. |
| `blink_and_youll_miss_it` | `BlinkTrainerService.HandleBlink` | **EXISTS** | Subscribe, counter `BlinkTrainerBlinks`. Entitlement-gated. |
| `hands_free` | `GazeFocusService` dwell-pop | **EXISTS** | Count gaze-pops (`AdvanceBubbleDwell` path). Counter `GazePops`. Entitlement-gated. |
| `she_remembers` | persistent-memory recall | **EMIT** | Find where T2 memory injects recalled content (`AiService` / companion memory) and emit on recall. Entitlement-gated (T2). Softest of the patron set; defer if the hook point is unclear. |
| `locked_in` | `LockdownService.LockdownActivated` | **EXISTS** | Subscribe. Entitlement-gated. |
| `throw_away_the_key` | `LockdownActivated` -> `LockdownDeactivated` duration | **EXISTS** | Compute elapsed >= 60 min. |
| `hand_over_control` | `RemoteControlService.SessionStarted` | **EXISTS** | Subscribe. Entitlement-gated. |
| `puppet_strings` | `RemoteControlService.CommandReceived` | **EXISTS** | Per-session counter `RemoteCommandsThisSession`, unlock at 100, reset on session end. |

### Work summary
- **New events to emit:** Deeper `EnhancementCompleted` (+ metadata) and play-minutes; catalogue submit-success; companion chat-send; `QuizCompleted`; persistent-memory recall.
- **Already-emitting, bridge just subscribes:** Deeper editor `FileSaved`, `ModChanged`/activate, keyword trigger execution, `CompanionLevelUp`, `BlinkTrainerService.HandleBlink`, `GazeFocusService` dwell-pop, `LockdownActivated`/`Deactivated`, remote `SessionStarted`/`CommandReceived`.
- **Server / deferred:** `featured` (approval state), `she_remembers` (recall hook).
- Everything routes through the single `GamificationBridge` so no new gamification calls land inside feature modules except the handful of new events above.
