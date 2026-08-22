# Agent F — Chaos Mode: Retention Systems + Intensity-Reactive Music

Exploration & planning only. Code is source of truth; verified against the files below.
Scope: ADDITIVE retention systems that plug into existing gamification infra, plus a
dynamic music layer. No new generic event bus, no fork of the gamification stack.

## Files inspected (ground truth)

- `Services/Chaos/ChaosModeService.cs` — run lifecycle. `RunTick`/`SpawnTick` @ 250ms/~800ms;
  `_state.RunIntensity = elapsed/duration`; `BeginRun()`, `EndRun()` (payout: `App.Progression.AddXP`
  + `ChaosMeta.AwardRunRewards`), `ToggleManualPause()`, `ActivateSlowMo`/`Freeze`. SFX hooked at
  `BeginWaveTransition` (`ChaosSfx.PlayWaveClear`) and inside `ChaosOverlayWindow`'s draft. Already
  calls `App.Achievements?.TrackBubblePopped()` directly and `App.Bark?.NotifyChaos*`.
- `Services/Chaos/ChaosSfx.cs` — one-shot NAudio, `PlayFirstAvailable(candidates[], scale)` resolving
  override→fallback via `ModResourceResolver.ResolveAudioPath`, `App.Audio.ApplyPreferredDevice`,
  `MasterVolume` scaling. Cues: `PlayWaveClear`, `PlayBoonReveal(isRare)`, `PlayBoonPicked`.
- `Services/Chaos/ChaosMetaState.cs` / `ChaosUpgrades.cs` (`ChaosMeta` facade) — Sparks,
  PurchasedUpgrades, EquippedStartBoon, DiscoveredCodexIds, RunsCompleted/BestScore/BestCombo/
  TotalDefused; hub unlocks `UNLOCK_*_RUNS`; `AwardRunRewards(run)`, `EquipStartBoon`, `TryPurchase`.
- `Services/Chaos/ChaosBubbleVariants.cs` — **single `private static readonly Random _rng = new()`**
  used by `Pick`, `Build`, `RollDarter`, `BuildDarter`. This is the only RNG to seed for a daily run.
- `Services/LeaderboardService.cs` — server-backed board via `codebambi-proxy` `/v3/leaderboard`,
  sorts on server-side fields (`xp`, `level`, `total_bubbles_popped`, `total_flashes`,
  `total_video_minutes`, `total_lock_cards_completed`). `YourRank`/`YourTotal`, `GetPlayerPercentile()`,
  `SeasonRecapService.SampleRank` fed on monthly refresh. New metrics require **server schema work**.
- `Services/SeasonRecapService.cs` (+ `Models/SeasonRecap.cs`, `ViewModels/SeasonRecapViewModel.cs`)
  — static, local-only counters on `AppSettings`; `TrackFeature(key)` is the engagement seam
  (called from `SessionEngine` per enabled feature); `CaptureAndRollover` snapshots before clearing;
  `SeasonFeatureKeys` enumerates feature tiles (`FeaturesTotal`).
- `Services/QuestService.cs` + `Models/Quest.cs` — daily/weekly from `QuestDefinitionService`
  (server) with embedded fallback; `QuestCategory { Flash, Video, Spiral, PinkFilter, Bubbles,
  LockCard, Session, Streak, BubbleCount, Mantra, Combined }`; `Track*` methods bump a category;
  `ParseCategory` maps server strings → category. **No Chaos category exists.**
- `Services/AchievementService.cs` + `GamificationBridge.cs` — bridge SUBSCRIBES to module events and
  calls `Progress.<counter>++ → MarkDirty() → TryUnlock/TryUnlockExclusive`. Guardrail: this bridge is
  the ONLY place new wiring lives; modules raise EMIT events, the bridge consumes them.
- `Services/AudioService.cs` — `CreateWaveOut()` / `ApplyPreferredDevice(WaveOutEvent|MediaPlayer)`
  (preferred device), `Duck(strength)`/`Unduck(gen)`/`ForceUnduck()` (ref-counted ducking + watchdog),
  `DuckGeneration`, `MasterVolume` on settings. No existing looping/music player anywhere.
- `Services/BarkService.cs` — `NotifyChaos*` raise bark events (`ChaosRunStarted`, `ChaosWaveEscalated`,
  `ChaosBoonPicked`, `ChaosComboMilestone`, `ChaosRunCompleted`, …). This is a separate consumer from
  the gamification bridge but the same emit pattern.

### Key gap found
Chaos currently feeds gamification by calling `App.Achievements?.TrackBubblePopped()` **directly**
inside `OnBenignPopped/OnDefused/OnDarterCaught/OnFreezeCaught` — which is the kind of direct Track*
call the bridge guardrail forbids elsewhere. For the new systems we should add **Chaos EMIT events**
(mirroring the Bark `NotifyChaos*` shape) and have `GamificationBridge` subscribe, rather than adding
more direct `Track*`/`TryUnlock` calls in `ChaosModeService`. (Pre-existing direct calls can stay;
don't expand them.)

---

## 1. Proposed additive systems

### A. Daily SEEDED run / Daily Challenge
- **What:** one fixed daily seed → everyone gets the same spawn sequence, sizes, darter rolls, and
  draft offers. One scored attempt/day (or best-of, replayable but only first scores the board).
  A fixed config preset (difficulty/duration/variants) shipped per day or derived from the seed.
- **Plug-in / seam:**
  - **RNG seeding (the real work):** replace the single `ChaosBubbleVariants._rng` with an injectable
    `Random`. Thread a seed from `ChaosRunConfig` (e.g. `Config.Seed`) into `Pick/Build/RollDarter/
    BuildDarter`. Because all four share `_rng`, this is one chokepoint — but it is *static state*, so
    a daily run must set the seed at `StartRun`/`BeginRun` and restore the default (re-`new()`) at
    `EndRun`/`ForceShutdown` so ambient/normal runs stay non-deterministic. Seed = hash of
    `yyyy-MM-dd` (UTC) so all clients agree without a server. Also confirm no *other* nondeterminism
    leaks in (bubble spawn positions / motion live in `BubbleService` — audit those for `Random` and
    seed or accept as visual-only jitter that doesn't affect score).
  - Daily config lives in `ChaosMeta`/a small `ChaosDailyState` (last-played date, today's score) so
    "already played today" survives restart.
  - Reward: bonus Sparks via `ChaosMeta.AwardRunRewards` path; XP via existing capped `AddXP`.
- **Build cost:** Medium (seed threading + static-state save/restore + a "play daily" entry + dedupe).
- **Retention impact:** High — daily reason to open the app; pairs with the leaderboard (below).

### B. Chaos Leaderboard
- **What:** a board ranking Chaos performance, ideally scoped to the **daily seed** (fair, comparable)
  with an all-time/season Chaos-best fallback.
- **Plug-in / seam:** `LeaderboardService` is server-backed and sorts on server columns. Two options:
  - **Cheap (client-only, now):** reuse the existing board — Chaos XP already flows into total XP via
    `AddXP`, so Chaos lifts your existing `xp`/`level` rank for free; surface `ChaosMetaState.BestScore`
    locally and via `SeasonRecap`. No server change.
  - **Proper (server work):** add a `chaos_daily` / `chaos_best` sorted set + a new `sort_by` value and
    `your_rank` plumbing in `/v3/leaderboard`. Score metric = **daily seeded run score** (deterministic
    seed makes scores comparable; that's the whole point of A).
- **Anti-cheat note:** scores are client-reported (the proxy trusts the client today). The daily seed
  is the cheapest integrity lever — a server that knows the seed can sanity-cap an implausible score
  (e.g. > theoretical max for that seed's spawn budget) and rate-limit one submission/seed/user.
  Without server validation, treat the board as "for fun," matching the current trust model.
- **Build cost:** Low (reuse existing XP board) → High (dedicated server set + validation).
- **Retention impact:** High (with daily seed) — competition is the retention multiplier; Low-Med if
  just "Chaos lifts your XP rank."

### C. Season integration (does Chaos feed SeasonRecap?)
- **What:** Chaos shows up on the end-of-month recap card: runs completed, best score, Sparks earned,
  best combo this season; and Chaos counts as an engaged feature.
- **Plug-in / seam:** `SeasonRecapService.TrackFeature(SeasonFeatureKeys.<Chaos>)` from `BeginRun`
  (add a `Chaos` key to `SeasonFeatureKeys` in `Models/SeasonRecap.cs`, bumps `FeaturesTotal`). For
  numeric stats, add season counters on `AppSettings` (`SeasonChaosRuns`, `SeasonChaosBestScore`)
  mutated in `EndRun`, snapshotted in `BuildSnapshot`, cleared in `RollBucket`, surfaced in
  `SeasonRecapViewModel`/the recap control. `SampleRank` already covers the season peak rank if Chaos
  ever rides the XP board (B-cheap).
- **Build cost:** Low-Medium (1 feature key wired free; numeric stats touch ~4 files in the recap chain).
- **Retention impact:** Medium — reinforces the monthly loop, but only visible once/month.

### D. Chaos quests / achievements (via GamificationBridge)
- **What:** dailies/weeklies and one-off achievements that reward playing Chaos.
- **Plug-in / seam:** add **Chaos EMIT events** on a Chaos surface (e.g. `ChaosModeService.RunCompleted`
  / `BubbleDefusedInRun` events, or reuse the Bark `NotifyChaos*` shape) and subscribe in
  `GamificationBridge.Start()` exactly like `QuizService.QuizCompleted`. The bridge then bumps
  `AchievementProgress` counters → `MarkDirty()` → `TryUnlock(...)` and calls the Quest tracker.
  - **Quests:** add `QuestCategory.Chaos` to `Models/Quest.cs` (enum + `ParseCategory "chaos"` case) and
    a `QuestService.TrackChaosRun()` / `TrackChaosDefuse()` calling `UpdateQuestProgress(QuestCategory.Chaos, n)`.
    Quest *definitions* are server-driven (`QuestDefinitionService`) with embedded fallback, so new
    Chaos dailies can ship server-side once the category exists. Example defs: "Defuse 25 bubbles in
    Chaos" (daily), "Complete 5 Chaos runs" (weekly), "Reach a x20 combo in Chaos" (weekly).
  - **Achievements (concrete, wired in the bridge):**
    - `chaos_initiate` — complete your first Chaos run (on `RunCompleted`).
    - `chaos_untouchable` — finish a run with zero unshielded detonations (run payload carries the count).
    - `chaos_combo_lord` — hit a x50 combo in a single run (`ChaosComboMilestone`/run best).
    - `chaos_paragon` — 100 runs completed (mirror `ChaosMeta.Rank` "Paragon"; bridge reads `RunsCompleted`).
    - `chaos_perfect_daily` — top-1% on the daily seed (needs B-proper; otherwise park as `IsHidden`).
  - Note the **guardrail correction:** prefer routing these through bridge subscriptions to new Chaos
    events rather than adding more direct `App.Achievements.Track*` calls in `ChaosModeService`.
- **Build cost:** Medium (events + bridge handlers + `QuestCategory.Chaos` + achievement defs/loc/art).
- **Retention impact:** High — quests are the proven daily-engagement driver and reuse all existing UI.

### E. Loadout expansion (as the boon pool grows)
- **What:** today Loadout equips ONE start boon (`EquippedStartBoon`). As `ChaosBoonPool` grows, expand
  to a small curated loadout: a 2nd start-boon slot, or a "ban one curse" / "guarantee a rare in draft 1"
  slot — gated behind more lifetime runs or a Sparks purchase.
- **Plug-in / seam:** `ChaosMetaState` (add `EquippedStartBoon2` / `BannedCurseId`), `ChaosMeta`
  facade methods (mirror `EquipStartBoon`), applied in `BeginRun` next to the existing equipped-boon
  block; gate via `UNLOCK_*_RUNS`-style threshold or a `ChaosUpgrades` row.
- **Build cost:** Low (extends an existing, well-factored slot).
- **Retention impact:** Medium — deepens the meta build-craft for engaged players; low reach for new ones.

### F. Weekly run MUTATOR (modifier)
- **What:** a rotating weekly global modifier ("this week: all fuses −25%", "double darters",
  "every wave-1 bubble is live", "×1.5 Sparks but ×2 detonation cost") that reshapes every run that week.
- **Plug-in / seam:** weekly modifier id derived from the ISO week (deterministic, no server needed) or
  delivered by `QuestDefinitionService`-style remote config; applied at `StartRun` by mutating the
  freshly-built `ChaosRunConfig` (exactly the `ChaosMeta.ApplyTo(config)` pattern — a `ChaosMutators.ApplyWeekly(config)`).
  Pairs naturally with the weekly quest and the leaderboard's weekly scope.
- **Build cost:** Low-Medium (config mutators + a tiny weekly-id resolver + HUD label).
- **Retention impact:** Medium-High — cheap, reuses the config-mutation seam, and gives a weekly "what's
  the rule this week" hook; strongest when combined with B/D.

---

## 2. AUDIO — intensity-reactive music layer

### Current state
No music/looping player exists. `ChaosSfx` is one-shot fire-and-forget NAudio; `AudioService` owns
ducking + preferred-device resolution + `MasterVolume`. `RunIntensity` (0→1) ticks every 250ms in
`RunTick` and is the natural escalation driver. Power-up state (`_slowMoRemainingSec`,
`_freezeRemainingSec`, `_manualPaused`) is all in `ChaosModeService`.

### Design: `ChaosMusic` (new static/instance service in `Services/Chaos/`)
A small layered-loop player, sibling to `ChaosSfx`, owning its own `WaveOutEvent`(s).

- **Where it hooks:**
  - Lifecycle driven from `ChaosModeService`: `ChaosMusic.Begin(seed?)` in `BeginRun`,
    `ChaosMusic.SetIntensity(_state.RunIntensity)` once per `RunTick`, `ChaosMusic.Stop()` in `EndRun`
    and `ForceShutdown`. Pause/freeze/slow-mo map to filter/tempo shifts (below) from the existing
    `ToggleManualPause`, `ActivateFreeze/EndFreeze`, `ActivateSlowMo/EndSlowMo`.
  - **Device + volume:** create the loop device via `App.Audio.CreateWaveOut()` (or
    `ApplyPreferredDevice(waveOut)` before `Init`) so it honors the user's chosen output device, exactly
    like `ChaosSfx`. Volume = `MasterVolume/100 * musicScale`, re-read so the master slider works live.
  - **Ducking relationship:** music is CCP's own output, so it must NOT be ducked by `AudioService.Duck`
    (Duck only touches *other* processes' sessions — our own PID is skipped, so this is automatic).
    Conversely, when a Chaos bubble payload fires a mandatory video/whisper, consider having `ChaosMusic`
    self-duck (drop its own gain ~40%) so the music sits under the payload — a local gain change, not a
    call into `AudioService.Duck`. `ChaosSfx` one-shots already ride over the music fine at their scales.

- **Stem / loop structure + resolver convention (mod-skinnable, mirror ChaosSfx):**
  - Layered stems under `Resources/sounds/chaos/music/`, each an independently-looping bar-aligned
    stem so they can be summed:
    - `bed.mp3` (always on — pad/drone)
    - `pulse.mp3` (drums/arp, fades in ~intensity ≥ 0.33)
    - `lead.mp3` (melodic/aggressive layer, fades in ~intensity ≥ 0.66)
    - optional `climax.mp3` (final-act sting, ~intensity ≥ 0.9)
  - Resolve each with the **same override→fallback candidate-list pattern** as `ChaosSfx`:
    `ResolveAudioPath("chaos/music/bed.mp3")` → fallback to a bundled ambient if absent. Drop dedicated
    files and they win automatically; active mods can override via `ModResourceResolver`.
  - Stems are **Suno-generated** at the same BPM/key so summing them is phase-clean. Keep them short
    (8–16 bars) and seamless-looped.

- **Escalation behavior (driven by `SetIntensity`):**
  - Crossfade layer gains in at the thresholds above (smooth lerp toward target gain over ~1–2s so it
    doesn't pop on the 250ms tick).
  - Optional global low-pass "filter open" as intensity rises (muffled → bright). NAudio can do this with
    a `BiQuadFilter`/`Lowpass` sample provider on the mixed output; if that's too much for v1, gain-only
    layering is the MVP and still reads as "escalating."
  - Tempo shifts: true tempo change is hard with prebaked loops — prefer **swapping to a faster-BPM
    stem set per Act** (or just adding the `pulse`/`lead` layers) rather than time-stretching.

- **Run-lifecycle transitions (start/stop/crossfade + power-ups):**
  - **BeginRun:** fade in `bed` from 0 over ~1.5s (after the 3·2·1 countdown, at GO).
  - **EndRun / ForceShutdown:** fade all layers to 0 over ~1s then dispose (don't hard-cut).
  - **Manual pause (`ToggleManualPause`):** duck music to a low "held" gain + close the filter (muffled),
    restore on resume — reinforces the frozen-field feel.
  - **Freeze power-up:** brief filter sweep / pitch-dip to "icy" for the ~3.5s hold, restore on `EndFreeze`.
  - **Slow-mo power-up:** drop the filter and/or lower a high layer for the 5s so it feels heavy; restore
    on `EndSlowMo`. (Don't try to slow the audio tempo — just tonal shift.)
  - **Wave boundary / boon draft (`_paused`):** dip to the `bed` layer only while the draft overlay is up,
    swell back when the next wave starts.

- **Fallback when no music files are present (silent, like ChaosArt):**
  - On `Begin`, resolve each stem; if a stem's candidate list yields nothing on disk, that layer is simply
    absent. If `bed` (the only required layer) is missing, `ChaosMusic` runs as a no-op — **completely
    silent, no error, no log spam** — exactly mirroring `ChaosArt`'s "sprite present → use it, else fall
    back/skip" behavior. Ship the feature with zero bundled music and it's inert until stems are added.

---

## 3. Ranking — retention impact vs build cost

| # | Proposal | Retention | Build cost | Plugs into | Verdict |
|---|----------|-----------|-----------|------------|---------|
| A | Daily seeded run | **High** | Medium | `ChaosBubbleVariants._rng` seed + `ChaosMeta` daily state | **DO FIRST** — unlocks B & F |
| D | Chaos quests/achievements | **High** | Medium | `GamificationBridge` + `QuestCategory.Chaos` | **DO FIRST** — reuses all gamification UI |
| B-cheap | Chaos lifts XP/level board | Med | Low | existing `AddXP` → `LeaderboardService` (no server) | **DO FIRST** — free, already half-true |
| Audio | Intensity-reactive music | Med-High | Medium | new `ChaosMusic` + `AudioService` device/MasterVolume | **DO** — big feel/retention per cost, silent fallback de-risks it |
| C | Season recap integration | Medium | Low-Med | `SeasonRecapService.TrackFeature` + recap chain | **DO** — 1 feature key is nearly free; stats are cheap |
| F | Weekly mutator | Med-High | Low-Med | `ChaosRunConfig` mutation (`ApplyTo` pattern) | **DO** if A ships (deterministic weekly id) |
| B-proper | Dedicated Chaos server board | High | **High** | new `/v3/leaderboard` sorted set + anti-cheat | **DEFER** — needs server + validation |
| E | Loadout expansion | Medium | Low | `ChaosMetaState`/`ChaosMeta` slots | **DEFER** — wait until boon pool is large enough to matter |

### Recommended MINIMUM first set
1. **A — Daily seeded run** (seed the one shared `_rng`; save/restore the static seed around the run).
2. **D — Chaos quests/achievements** via `GamificationBridge` + a new `QuestCategory.Chaos` (add Chaos
   EMIT events; do not expand the direct `Track*` calls).
3. **B-cheap — Chaos already lifts XP/level rank**; surface `BestScore` locally. No server work.
4. **Audio — `ChaosMusic`** (gain-only layered Suno stems is an acceptable MVP; silent fallback).
5. **C — one `SeasonFeatureKeys.Chaos`** track at `BeginRun` (trivial), numeric season stats optional.

This set is fully additive, uses only existing seams (bridge subscriptions, config mutation, the
ChaosSfx resolver convention, AudioService device/volume), and needs **zero server changes**. F is the
natural next add once A exists; B-proper and E are the cut candidates.

---

## Open questions
1. **Static-RNG safety:** `ChaosBubbleVariants._rng` is static. Confirm Chaos runs are never concurrent
   (they aren't — `StartRun` early-returns if `_active`), so swapping the static seed per-run and
   restoring it is safe. Audit `BubbleService` for any *score-affecting* `Random` that also needs seeding
   for a truly fair daily.
2. **Anti-cheat appetite:** is the leaderboard "for fun" (client-trusted, like today) or do we want the
   server to validate daily scores against the seed? Determines B-cheap vs B-proper.
3. **Music authoring:** how many Suno stem sets (one universal vs per-Act vs per-difficulty)? Same
   BPM/key is required for clean layering — lock that before generating.
4. **Bridge vs direct calls:** OK to leave the existing direct `App.Achievements.TrackBubblePopped()`
   calls in `ChaosModeService`, but route all NEW Chaos gamification through bridge subscriptions to new
   Chaos events? (Recommended, matches the guardrail.)
5. **Daily reward economy:** how much bonus Sparks/XP for the daily, and does a daily failure still pay
   the normal run payout? Needs a balance pass against `AwardRunRewards`.
6. **Filter DSP scope for v1:** ship gain-only layering first and add the low-pass "filter open" later,
   or invest in the `BiQuadFilter` mixed-output path up front?
