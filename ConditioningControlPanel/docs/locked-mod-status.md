# Locked Mode — Build Status & Gap Report

**Generated:** 2026-06-02 · read-only recon, nothing built/registered/moved.
**Reference mode:** Dronification (`DroneMod/`, `mod.json`, `Models/BuiltInMods.cs::CreateDronification`, `Models/ModManifest.cs`).
**Concept:** "Locked" — goonification (goon / chastity / beta / locked). Hot-magenta + black + red-glitch hypno-mommy.

> **Headline:** The **asset art layer is ~90% done** (achievements, feature tiles, mod props, character + 4 stage avatars). The **avatar pose sets are incomplete** (only pose 1 of 5 stages — 15 poses missing) and the **entire non-asset content layer** (manifest: identity, theme hex, ~29 phrase categories, subliminals, triggers, lock-cards, text-replacement renames, enhancement overrides, browser, tube layout) plus the **audio layer** (companion voicelines, flash/subliminal audio, bubble/giggle SFX) have **not been started**.

---

## STEP 1 — Inventory of generated assets (`tools/asset_gen/output/`)

| Subfolder | Count | Contents | Notes / flags |
|---|---|---|---|
| `locked_character/` | 2 | `locked_character_master.png` (356×960 RGBA, transparent), `locked_character_waistup_sq.png` (1024² RGB, ref crop) | Master = avatar **stage 1 / pose 1** candidate. Old full-latex design — predates the 4 redesigned distinct stages. |
| `locked_avatars/` | 4 | `avatar2_pose1`(303×960), `avatar3_pose1`(313×960), `avatar4_pose1`(249×960), `avatar5_pose1`(307×960) — all RGBA transparent | ⚠️ **Pose 1 only.** Each stage needs poses 2–4. No `avatar_pose1` (set 1) placed yet. |
| `locked_achievements/` | 58 | Full achievement set, matches default filenames exactly | ✅ **Complete** (58 = default 58 = drone 58). 1024² RGB (+ `docile_cow` 1871×1851, `BrainwashedSlavedoll`/`PlatinumPuppet` 512²). |
| `locked_features/` | 19 | `flash, subliminal, audio_whispers, corner_gif` (512²); `Pink_filter, Mind_Wipers, Bubble_pop, Bubble_count` (1024²); `brain_drain, bambi takeover, takeover, vibe` (RGBA exact dims); `awareness, blink_trainer, remote_control, lab_aimemory_hero, lab_focusgaze_hero, lab_gaze_hero, lab_quiz_hero` (1376×768 banners) | ✅ Matches default feature dims/aspects. |
| `locked_tiles_square/` | 4 | `Phrase_Lock, bouncing_text, mandatory_videos, spiral_overlay` (1024² RGB, magenta character) | ✅ The **good** versions of the 4 tiles missing from `locked_features`. |
| `locked_tiles/` | 4 | same 4 names | ⚠️ **Superseded** — old crimson/brass v1. Don't ship. |
| `locked_tiles - Copy/` | 4 | same 4 names | ⚠️ **Stray duplicate folder** — delete. |
| `locked_v2/` | 3 | `Phrase_Lock, mandatory_videos, spiral_overlay` | ⚠️ Superseded draft. |
| `locked_modassets/` | 9 | `bubble, bubble2, bubble2_alt1/2/3, bubble3` (1024² RGBA transparent); `tube`(2048² RGBA), `tube2`(2048² RGBA); `logo`(2076×2048 RGB) | ✅ Bubble + 2 tubes + logo done. Need to pick ONE bubble. |
| `locked_style_samples/` (+lock/video/spiral) | 12 | Early A/B/C style exploration + 3 comparison sheets | 🗑️ Exploration only — not shippable assets. |
| `_discarded_drafts/`, `kept_v6/` | — | Old "Keeper/kept" avatar drafts (avatar8_*) | 🗑️ Unrelated to Locked / discarded. |

**Garbled / failed renders:** none currently in the kept sets (all earlier defects — `corner_hit` subtitle, `curator` spelling, `she_remembers` spelling, broken `avatar5` cutout — were fixed in prior sessions).

---

## STEP 2 — Target: what a complete built-in mode requires (from Drone)

### A) ASSETS

| Asset group | Drone reference | Spec |
|---|---|---|
| **Avatars** | `build/resources/avatar{,2..7}_pose{1..4}.png`; `SupportedAvatarSets=[1,2,3,4,5]` | **5 sets × 4 poses = 20 sprites**, RGBA transparent, ~960px-tall source, tight crop. (Sets 6–7 exist but unsupported.) |
| **Feature tiles** | `build/resources/features/*.png` (21) → default `Resources/features/` (24) | Square tiles: `flash, mandatory_videos, subliminal, bouncing_text, Pink_filter, spiral_overlay, brain_drain, Bubble_pop, Phrase_Lock, Bubble_count, corner_gif, audio_whispers, Mind_Wipers, takeover, bambi takeover, vibe`. Wide banners (1376×768): `awareness, blink_trainer, remote_control, lab_aimemory_hero, lab_focusgaze_hero, lab_gaze_hero, lab_quiz_hero`. (`4new.png` = spare, verify.) |
| **Achievements** | `Achievements/` + `build/resources/achievements/` (58) | 58 badges, filenames matching the default set. |
| **Mod root assets** | `build/resources/` root | `bubble.png`, `tube.png`, `tube2.png`, `logo.png`, `logo2.png`, `preview.png`, `speechbubble1.png`, `speechbubble2.png`, `spiral.gif`. |
| **Cards** | `build/resources/Cards/` (3) | Celebration cards: `fireworks.png, hearth.png, spotlight.png`. |
| **Skills** | `Skills/` + `build/resources/skills/` (22) | 22 enhancement-tree icons (overclock I/II/III, streak, hive, etc.). |
| **Theme colors** | `mod.json::theme` | 7 hex values: accent / accentLight / accentDark / background / panel / surface / filter. |

### B) NON-ASSET CONTENT (manifest data — `ModManifest` schema)

- **Identity** — `companionName, userTerm, modeDisplayName, talkToLabel, takeoverLabel, affirmation, rankSubject`.
- **Theme palette** — 7 hex colors (above).
- **Triggers** — `freeze, reset, cumAndCollapse, autonomyOn`.
- **Messages** — `attentionCheckFail, attentionCheckMercy, bubbleCountRetry`.
- **Browser** — `defaultUrl, siteName, showBambiCloudOption, defaultVideoLinks` (~20 entries).
- **SubliminalPool** — Drone has 21 phrases.
- **LockCardPhrases** — Drone has 7.
- **CustomTriggers** — Drone has 18.
- **Phrases** — **29 categories**: Greeting, StartupGreeting, Idle, RandomFloating, Generic, Gaming, Browsing, Shopping, Social, Discord, TrainingSite, HypnoContent, Working, Media, Learning, WindowAwarenessIdle, EngineStop, FlashPre, SubliminalAck, RandomBubble, BubbleCountMercy, BubblePop, GameFailed, BubbleMissed, FlashClicked, LevelUp, MindWipe, BrainDrain, Thinking. (6–26 lines each; `{0}` app-name interpolation in activity categories.)
- **TextReplacements** — ~120 renames (achievements, features, personality presets, enhancement skills, base terms Bambi→/Bimbo→/pink→).
- **EnhancementOverrides** — tree title/subtitle/warning, points & stats labels, pink-rush name/desc, lucky labels, `boostTooltips` (7), `statPillTooltips` (3).
- **TubeLayout** — `avatarOffsetX/Y, avatarDetachedOffsetX/Y, avatarScale` (5 numbers, tuned to the tube art).
- **SupportedAvatarSets** — list of set numbers.
- **Personalities** *(optional)* — Drone ships **none**; it renames the 5 default presets via TextReplacements instead. Locked can do the same.
- **PreviewImage** — manifest pointer to `preview.png`.

### C) AUDIO (per-mode, Drone ships all of these)

- **Companion voicelines (TTS)** — `DroneMod/VoiceLines/` ~33 category folders (Greeting, Idle, FlashPre, BubblePop, LockCard, Triggers, Subliminals, …), hundreds of clips. Generated via `generate_voicelines.py` + `postprocess_voicelines.py`.
- **Flash / subliminal audio** — `build/resources/sounds/flashes_audio/` (240 clips).
- **Bubble SFX** — `sounds/bubbles/Pop{,,2,3}.wav` (3).
- **Giggles / reaction SFX** — `sounds/giggle1..8.wav` (8).

---

## STEP 3 — Gap report: ITEM · DONE · MISSING

### ASSETS

| Item | DONE (in asset_gen/output) | MISSING |
|---|---|---|
| Character master | ✅ `locked_character_master.png` | — (may want a redo to match the 4 redesigned stages' look) |
| Avatar set 1 (`avatar_pose1..4`) | ⚠️ master only (pose 1 candidate, not named/placed) | **poses 1–4 as `avatar_pose*`** (decide: master = pose1?) → **3–4 sprites** |
| Avatar set 2 "The Claim" | ✅ `avatar2_pose1` | `avatar2_pose2/3/4` → **3** |
| Avatar set 3 "The Pull" | ✅ `avatar3_pose1` | `avatar3_pose2/3/4` → **3** |
| Avatar set 4 "The Bind" | ✅ `avatar4_pose1` | `avatar4_pose2/3/4` → **3** |
| Avatar set 5 "The Apex" | ✅ `avatar5_pose1` | `avatar5_pose2/3/4` → **3** |
| **Avatars total** | **5 / 20 poses** | **~15 poses missing** |
| Feature tiles (16 square) | ✅ 12 in `locked_features` + 4 in `locked_tiles_square` | none (consolidate; drop `locked_tiles`, `locked_v2`, `- Copy`) |
| Feature banners (7 wide) | ✅ all 7 in `locked_features` | none |
| Achievements (58) | ✅ all 58 | none |
| Bubble | ✅ `bubble*` (pick 1) | choose final; rename to `bubble.png` |
| Tubes attached/detached | ✅ `tube.png`, `tube2.png` | verify which is attached vs detached |
| Logo / hub | ✅ `logo.png` | `logo2.png` (second logo variant) |
| Preview image | ❌ | **`preview.png`** (mode card art) |
| Speech bubbles | ❌ | **`speechbubble1.png`, `speechbubble2.png`** |
| Spiral gif | ❌ | **`spiral.gif`** (overlay anim) |
| Celebration cards | ❌ | **`Cards/fireworks.png, hearth.png, spotlight.png`** (3) |
| Enhancement skill icons | ❌ | **22 skill icons** (`skills/`) |
| Theme hex palette | ❌ (de-facto magenta/black/red from art) | **7 hex values** in manifest |

### NON-ASSET CONTENT — **0% started**

| Item | DONE | MISSING |
|---|---|---|
| Identity strings | ❌ | companionName, userTerm, modeDisplayName, talkToLabel, takeoverLabel, affirmation, rankSubject |
| Triggers | ❌ | freeze, reset, cumAndCollapse, autonomyOn |
| Messages | ❌ | attentionCheckFail, attentionCheckMercy, bubbleCountRetry |
| Browser | ❌ | defaultUrl, siteName, showBambiCloudOption, ~20 video links |
| SubliminalPool | ❌ | ~21 goon/lock/beta subliminal phrases |
| LockCardPhrases | ❌ | ~7 lock-card phrases |
| CustomTriggers | ❌ | ~18 named triggers |
| Phrases (29 categories) | ❌ | all 29 companion phrase pools |
| TextReplacements | ❌ | ~120 renames (achievements/features/presets/skills/base terms) |
| EnhancementOverrides | ❌ | tree strings + 7 boost tooltips + 3 stat-pill tooltips |
| TubeLayout | ❌ | 5 offset/scale values tuned to Locked tube |
| SupportedAvatarSets | ❌ | set list |
| Personalities | ❌ | (optional) rename 5 presets via TextReplacements |
| Manifest + registration | ❌ | `mod.json`, `CreateLocked()` in `BuiltInMods.cs`, id constant, build packaging |

### AUDIO — **0% started**

| Item | DONE | MISSING |
|---|---|---|
| Companion voicelines (TTS) | ❌ | ~33 category folders of spoken lines (or ship text-only / no-audio) |
| Flash / subliminal audio | ❌ | `sounds/flashes_audio/` clip set |
| Bubble SFX | ❌ | `sounds/bubbles/Pop*.wav` (or reuse default) |
| Giggle / reaction SFX | ❌ | `sounds/giggle*.wav` (or reuse default) |

---

## Cleanup recommended before build (asset_gen/output)
- Delete `locked_tiles - Copy/` (stray duplicate).
- Archive `locked_tiles/`, `locked_v2/`, `locked_style_samples/` (superseded/exploration).
- Pick ONE bubble from `locked_modassets/` → `bubble.png`; confirm `tube` vs `tube2` = attached vs detached.
- Decide whether `locked_character_master.png` becomes `avatar_pose1` or gets a redesign to match the 4 distinct stages.
