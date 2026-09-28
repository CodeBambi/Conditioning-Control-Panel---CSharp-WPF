# CCP Default trigger + audio audit (2026-09-25, lane pivot/audit)

Owner ask: "check if we got only classic triggers like drop, sink, relax, etc around - check also the
attached audios and what audios are there for the flashes."

Legend: **CLASSIC** = classic neutral trance word. **NICHE** = Bambi / bimbo / sissy / girl / cock /
drone / puppy / chastity coded. **OTHER** = neutral UI or instruction text, not a trigger.

## 1. Triggers CCP Default ships or defaults to

| Source | Words | Verdict |
|---|---|---|
| `BuiltInMods.CCPDefault.SubliminalPool` (also the fresh `AppSettings.SubliminalPool`) | FOCUS, BREATHE, RELAX, LISTEN, DEEPER, OBEY, SUBMIT, QUIET MIND, EMPTY, DROP, TRANCE | CLASSIC |
| `CCPDefault.LockCardPhrases` (fresh `AppSettings.LockCardPhrases`) | FOCUS, OBEY, DROP DEEPER, EMPTY AND READY | CLASSIC |
| `CCPDefault.CustomTriggers` | FOCUS, BREATHE, RELAX, DEEPER, OBEY, DROP, TRANCE | CLASSIC |
| `AppSettings.CustomTriggers` fresh extras | was SNAP AND FORGET + SAFE AND SECURE | **SNAP AND FORGET = NICHE (Bambi trigger with a Bambi-voiced clip). FIXED -> LET GO** |
| `CCPDefault.BouncingTextPool` (fallback for Bambi/Sissy/Drone/Infection too) | was GOOD GIRL, OBEY, SUBMIT, BIMBO, EMPTY, MINDLESS, OBEDIENT, PRETTY, PINK, DROP | **NICHE (GOOD GIRL, BIMBO, PRETTY, PINK). FIXED** |
| `AppSettings.BouncingTextPool` fresh default (the one a fresh install actually uses) | was DEEPER, OBEY, SUBMIT, BLANK, EMPTY, MINDLESS, OBEDIENT, PRETTY, PINK, DROP | **PRETTY, PINK = NICHE (bimbo coded). FIXED -> RELAX, SINK** |
| `AppSettings.AttentionPool` | CLICK ME, DROP, OBEY, ACCEPT, SUBMIT, BLANK AND EMPTY | CLASSIC |
| `AppSettings.MantraPool` | "I am deeply relaxed", "My mind is open and receptive", ... "Every breath takes me deeper" | CLASSIC |
| `AppSettings.KeywordTriggers` | empty list | n/a |
| `CCPDefault.Triggers` | Freeze, Reset, RELEASE, "Autonomous mode engaged." | CLASSIC |
| `CCPDefault.Messages` / `Phrases` (attention fail, mercy, bubble, flash-pre, idle, mind wipe...) | "ATTENTION REQUIRED", "Deeper.", "Quiet now.", "Empty and calm." etc. | CLASSIC / OTHER |
| Deeper demo `Resources/DeeperDemos/welcome.ccpenh.json` | labels Intro / Deepen / Rest | OTHER (neutral) |
| Goon Game options preview chip (`web/goon/ui/opponent.js`) | bouncing text sample read "good girl" | **NICHE. FIXED -> "good pet"** |
| Awareness presets `Resources/AwarenessPresets/*.json` (library, all OFF by default, shown to every mod) | trance: relax, deeper, sleep, drop, breathe, trance, empty, spiral | CLASSIC |
| | bimbo: smart, focus, think, intelligent, work, study, concentrate, remember + bimbo lines ("Pink thoughts only, babe") | NICHE |
| | puppy: good boy, **good girl**, sit, stay, fetch, treat, obedient, collar | NICHE (pet play, gendered) |
| | chastity: edge, cum, porn, orgasm, release, tease, denied, horny (+ ChasterAddTime) | NICHE (chastity; tied to the Chaster feature) |
| `CCPDefault.Browser.DefaultVideoLinks` (HypnoTube pool the companion may suggest) | Femboy World, Suck Cock For Her, The ABC Of Transgirls, Sissy Steps 1, Bambi TikTok 4, Cock Suck Encouragement, To Be A Sissy Hypno Slut... | **NICHE, almost all 13.** Not changed: owner said the HT link set comes later. |
| Companion giggle SFX (CCP Default speech bubbles) | giggle1-8 | OTHER, feminine-coded sound; see recommendations |

Existing users: nothing is migrated. Saved settings load with `ObjectCreationHandling.Replace`, and mod
switches restore the per-mod backups (`*ByMod`), so the new defaults only reach fresh installs or an
empty pool with no backup.

## 2. Audio attached to triggers

How linking works: `SubliminalService.FindLinkedAudio` / `KeywordTriggerService.FindLinkedAudio` look for
a file whose name EXACTLY equals the phrase (case-insensitive), first in the active mod's
`resources/sounds/flashes_audio`, then in `Resources/sub_audio`. Trigger Mode (`PlayTriggerAudio`),
subliminals, freeze/reset, Arcademy (`ccp.subaudio`) and Intake whispers all use this.

`Resources/sub_audio` (21 clips, IN THE BOX via csproj `<Content Include="Resources\sub_audio\**\*">`, all
Bambi-voiced): BAMBI CUM AND COLLAPSE, BAMBI DOES AS SHE'S TOLD, BAMBI FREEZE, BAMBI RESET, BAMBI SLEEP,
BAMBI UNIFORM LOCK, BIMBO DOLL, COCK TURNS MY BRAIN OFF, COCK ZOMBIE NOW, DONT THINK SILLY, DROP FOR COCK,
GIGGLETIME, GOOD GIRL, GOOD GIRLS DONT THINK, I CANT RESIST MY TRIGGERS, JUST OBEY, PRIMPED AND PAMPERED,
SNAP AND FORGET, THERES NO NEED TO THINK, TURN YOUR BRAIN OFF, ZAP COCK DRAIN OBEY.

- CCP Default's own defaults match NONE of these names (FOCUS, DROP, OBEY, Freeze, Reset... have no file),
  so CCP Default subliminals and triggers are silent, with ONE exception that was live: the fresh
  `CustomTriggers` default "SNAP AND FORGET" matched `SNAP AND FORGET.MP3`, so Trigger Mode on a fresh
  CCP Default install whispered in Bambi's voice. Fixed (now LET GO).
- A CCP Default user who types a Bambi phrase (e.g. GOOD GIRL, JUST OBEY) still gets the Bambi clip,
  because `sub_audio` is a shared in-box fallback for every mod.
- `Resources/sounds/` root also holds Bambi trigger mp3s (BAMBI FREEZE, BAMBI RESET, BAMBI SLEEP, GOOD GIRL,
  DROP FOR COCK, COCK ZOMBIE NOW, ZAP COCK DRAIN OBEY, BIMBO DOLL, 00 Bimbo Drone, Giggle Time, PRIMPED AND
  PAMPERED, SNAP AND FORGET...). IN THE BOX (the sounds root is not in `ContentPackSoundsExclude`). No code
  reference to them by name was found (only giggle1-8, chime1-3, lvup, result via `ModResourceResolver`);
  they look like orphaned duplicates of `sub_audio`.
- Awareness preset audio `Resources/AwarenessPresets/audio` (IN THE BOX): bell.wav (trance), chime.wav
  (bimbo), clicker.mp3 (puppy), lock-click.mp3 (chastity). Plain SFX, no voice; neutral.
- Companion audio: `companion_audio/mods/builtin-{bambisleep,sissyhypno,locked}` ship as packs
  (mod-bambi / mod-sissy / mod-locked). There is no `builtin-ccp-default` folder, so CCP Default has no
  companion voice lines; the root `companion_audio` holds only the three `egg_first_enhancement_*` clips.

## 3. Flash audio

- Flash voicelines: `FlashService.SoundsPath` = `CompanionPhraseService.VoiceLineFolder`. For CCP Default
  it resolves to `companion_audio/mods/builtin-ccp-default/flashes_audio`, which does not exist, so
  **CCP Default flashes play no voice**. The shared `Resources/sounds/flashes_audio` (118 clips, bimbo /
  "good girl" / "fuck doll" / cock lines) is reachable only by BambiSleep or no mod id
  (`CompanionContentResolver.OwnsBaselineVoiceLines`).
- Origin of `flashes_audio`: stripped from the installer (`ContentPackSoundsExclude`) and shipped as the
  **audio-base** content pack ("Baseline voice"). BUT `ReleaseContentService.EnsureBaselineAsync`
  downloads audio-base at startup for EVERY user, CCP Default included, so the Bambi flash voice lands on
  disk for everyone even though CCP Default never plays it.
- Lucky flash: `chime1-3.mp3` (Resources/sounds root, in the box). Neutral chimes.
- Flash audio on/off: `AppSettings.FlashAudioEnabled`, muted by master 0 / AvatarMuted /
  CompanionVoiceLinesMuted.

## 4. Changed in this lane (defaults only, CCP Default only)

1. `AppSettings._bouncingTextPool`: PRETTY, PINK -> RELAX, SINK.
2. `AppSettings._customTriggers` fresh extra: SNAP AND FORGET -> LET GO (kills the Bambi-voiced clip).
3. `BuiltInMods.CCPDefault.BouncingTextPool`: GOOD GIRL, BIMBO, PRETTY, PINK out; now the same classic
   list as the AppSettings default (DEEPER OBEY SUBMIT BLANK EMPTY MINDLESS OBEDIENT RELAX SINK DROP).
   Side effect: Bambi/Sissy/Drone/Infection, which have no bouncing pool of their own, fall back to this
   list only when their pool is empty and has no backup (rare; programs derive Bambi/Drone lines elsewhere).
4. `web/goon/ui/opponent.js`: the Bouncing Text preview chip reads "good pet" instead of "good girl".
5. New test `CcpDefaultClassicTriggersTests` (banned-substring list over the manifest pools, the fresh
   AppSettings pools and a no-Bambi-clip check for fresh Trigger Mode phrases).

No loc keys changed. No audio, installer or pack file touched.

## 5. Recommendations for the owner (release / pack decisions, not done here)

1. Move `Resources/sub_audio` (21 Bambi clips) out of the box into mod-bambi (and teach `FindLinkedAudio`
   to look in the active mod's pack only), so CCP Default and other mods cannot reach them by typing a
   phrase. Same list as the Stripe "two houses" plan.
2. Delete or pack the Bambi trigger mp3s in the `Resources/sounds` root (BAMBI *, GOOD GIRL, DROP FOR COCK,
   COCK ZOMBIE NOW, ZAP COCK DRAIN OBEY, BIMBO DOLL, 00 Bimbo Drone, PRIMPED AND PAMPERED, SNAP AND
   FORGET, Giggle Time / GIGGLETIME). No code reads them by name; verify with a grep before removing.
3. Stop auto-fetching **audio-base** for everyone: make it lazy, fetched only when BambiSleep is active
   (or fold it into mod-bambi). Rename it: it is not a "Baseline voice".
4. CCP Default's HypnoTube `DefaultVideoLinks` are almost all niche (sissy / femboy / trans / cock /
   Bambi); replace with the neutral HT set the owner is preparing.
5. Awareness library: bimbo, puppy (has "good girl") and chastity presets are niche but off by default
   and mod-agnostic. Consider showing them only under their themed mods, or keep and label them.
6. CCP Default companion uses giggle1-8 SFX on speech bubbles (feminine-coded); consider a neutral
   blip or silence for CCP Default, matching the gender-neutral direction.
