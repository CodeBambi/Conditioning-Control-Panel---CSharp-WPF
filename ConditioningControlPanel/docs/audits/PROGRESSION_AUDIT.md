# PROGRESSION AUDIT — Chaos Mode ("the rabbit hole") meta-progression
*2026-06-11 · exploration/report only, no code changed · audited at branch `feat/emotive-avatar-portraits` (head `c0b2d39`)*

Scope: everything progression-relevant in `Services/Chaos/*`, `ChaosHubWindow.*`, `ChaosHudWindow.*`, `ChaosOverlayWindow.*`, `Services/BarkService.cs`, `Resources/sounds/companion_audio/**/bark_rules.json`.

---

# PART A — AUDIT (ground truth)

## A1. Purchasables

Currency is `ChaosMetaState.Sparks` (code) — displayed as **"drops"** with the **✦** glyph in the Warren top bar (`ChaosHubWindow.xaml:139-141`), but as **"GOLD"** on the recap chip and **"gold"** in every in-run event line. One pool, three display names (see C7).

### A1.a Habits — single-rank, always-on, toggleable (`ChaosUpgrades.All`, bought via `ChaosMeta.TryPurchase`)
Sold on the **Habits** tab with verb **"Train ✦{cost}"**; trained rows toggle "switch on / switch off".

| id | Display name | Branch (display tag) | Cost ✦ | Effect at run start |
|---|---|---|---|---|
| `slow_fuses` | Slower Trance | Control ("restraint") | 120 | `FuseTimeMult ×1.15` |
| `silk_touch` | Silk Touch | Control ("restraint") | 180 | `HitboxScale 1.25` + near-miss magnet |
| `popup_notification` | Pop-up Notification | Control ("restraint") | 160 | once-per-loop 60% heart drift, +1 resistance on catch |
| `draft4` | 4-Mantra Draft | Depth ("depth") | 200 | draft offers 4 cards |
| `extreme_tier` | Inescapable Tier | Depth ("depth") | 350 | sets `ExtremeUnlocked`, opens the Inescapable pill |

No Greed-branch ("craving") habits currently exist — the branch enum and color survive with zero rows.

### A1.b Skills — active-use toys, pocket-slotted (1 slot) (`ChaosLifetimeBoons`, Category `Skill`)
Sold on **Enhancements** ("Unlock ✦x" → "Upgrade ✦x" → "MAX ✓"). All fire on a keybind (default Q) or the bottom-left hero button.

| id | Display | Unlock | Levels 2..max | Level value | Capstone |
|---|---|---|---|---|---|
| `vibe_popping` | VibePopping | 200 | 250/400/600 (L4) | 3/4/5/5 s buzz | hover alone pops |
| `freeze_trigger` | Freeze Trigger | 250 | 400/650/900 (L4) | 1/2/3/3 uses | each freeze snaps every live |
| `porn_dvd` | Porn DVD | 300 | 450/700/1000 (L4) | 10/15/20/20 s flight | two screens |
| `snap_field` | Snap Field | 300 | 400/600 (L3) | 60/45/30 s cooldown | clears EVERYTHING |
| `rabbit_caller` | Rabbit Caller | 250 | 350/550 (L3) | 1/2/3 rabbits | +8-rabbit storm over 10s |
| `e_stim` | E-Stim | 300 | 450/650 (L3) | 3/4/5 charged pops | charged pops chain-react |

### A1.c Accessories — passives, pocket-slotted (1 slot) (Category `Accessory`)

| id | Display | Unlock | Levels 2..max | Level value | Capstone |
|---|---|---|---|---|---|
| `surrender` | Surrender | 150 | 250/450 (L3) | +0.05/0.10/0.15x per sin | every draft offers a sin; yes = +1 resistance; first sin loses its sting |
| `chain_reaction` | Poppers | 150 | 120/160/220/300 (L5) | 1.2→2.0x burst reach | — |
| `blindfold` | Blindfold | 300 | 450/700 (L3) | x1.5/1.75/2.0 payout (opacity 40/32/25%) | heartbeat warns of the nearest fuse |
| `last_breath` | Last Breath | 250 | 350/550 (L3) | x5/10/20 at the brink (0.4/0.6/0.8 s window) | — |
| `taking_chances` | Taking Chances | 250 | 300/500 (L3) | 1/2/3 draft rerolls; coin 50/55/60% double | — |
| `the_pull` | The Pull | 200 | 200/300/450/650 (L5) | 0.12→0.58 cursor pull; rabbits home | — |
| `the_spanker` | The Spanker | 300 | 450/700 (L3) | x1.20/1.45/1.70 swell | bouncing texts smackable too |
| `intrusive_thoughts` | Intrusive Thoughts | 250 | 350/550 (L3) | 3/4/5 s thoughts (one per 5s) | thoughts split on rabbits (max 8, +2s) |

### A1.d Charms — Utility lifetime boons; **render on the Habits tab**, uncapped pockets

| id | Display | Unlock | Levels 2..max | Level value | Capstone |
|---|---|---|---|---|---|
| `rabbits_foot` | Rabbit's Foot | 200 | 350/600/900 (L4) | 1.0/1.5/2.0/2.0% golden chance; gold 12-24→16-32 | gold doubles (20-40) |
| `drip_feed` | Drip Feed | 250 | 400/650/1000 (L4) | +5/10/15/20 drops a pop | +10% on the whole surface haul |
| `blank_eyes` | Blank Eyes | 120 | — (L1) | pop payouts float up | — |
| `breast_enlargement` | Breast Enlargement | 120 | 180/260/380 (L4) | +5/10/15/25% bubble size | — |
| `slow_recovery` | Slow Recovery | 200 | 300/450/650 (L4) | 60/50/40/30 pops a point | — |
| `start_resistance` | It would never work on me... | 100 | 200/350 (L3) | +1/2/3 resistance at start | — |
| `collar` | Collar | 200 | 300/450 (L3) | 1/2/3 streak saves | — |
| `golden_touch` | Golden Touch | 150 | 250/400/600 (L4) | x1.1→1.45 baseline; calm pops 0.45→0.60 | — |
| `slowburner` | Slowburner | 150 | 250/400/600 (L4) | 10/20/30/40% slower fuse | brink snap (≤1.5s) pays triple |
| `pocket_watch` | Pocket Watch | 150 | — (L1) | wave countdown + run clock visible | — |

**Cost totals:** habits 1,010 · skills 9,950 (unlocks 1,600) · accessories 9,850 (unlocks 1,850) · charms 11,560 (unlocks 1,640). **Catalogue grand total: 32,370 ✦** (unlocks only: 6,100 ✦).

## A2. The mantra/sin draft pool (`ChaosBoonPool.All`, `ChaosModels.cs:219`) — 23 cards

Draft (`ChaosModels.cs:300`): 3 choices (4 with `draft4`); a sin occupies one slot 50% of the time (hardcoded `0.5`; `guaranteeCurse` forced only by the Surrender capstone); skip = +1 resistance; untouched drafts auto-skip after 15 s; `taking_chances` grants rerolls.

| id | Display | Kind | Pool-entry condition |
|---|---|---|---|
| `defuse_chain` | Snap Chain | mantra (+0.10x) | always; **re-draftable** (not Unique) |
| `golden_touch` | Golden Touch | mantra (+0.15x) | always; re-draftable (same id as the charm — different store) |
| `extra_shield` | Left Brain | mantra | always; re-draftable |
| `gold_digger` | Gold Digger | mantra | always; Unique |
| `welcome_shower` | Welcome Shower | mantra | always; Unique |
| `heavy_drop` | Heavy Drop | mantra | always; Unique |
| `gg_rabbits` | GG make more GG | mantra | always; Unique |
| `size_queen` | Size Queen | mantra | always; Unique |
| `aftermath` | Aftermath | mantra | always; Unique |
| `focus_here` | Focus here... | mantra | always; Unique |
| `overload` | Overload | duo | `e_stim` equipped |
| `afterglow` | Afterglow | duo | `vibe_popping` equipped |
| `casting_couch` | Casting Couch | duo | `porn_dvd` equipped |
| `tail_plug` | Tail-Plug | duo | any of `rabbit_caller`/`the_pull`/`the_spanker` |
| `unleashed` | Unleashed | duo | `collar` equipped |
| `electrified_rabbits` | Electrified Rabbits | duo (RequiresAll) | `the_spanker` AND `e_stim` |
| `body_buzz` | Body Buzz | duo (RequiresAll) | `chain_reaction` AND `e_stim` |
| `hair_trigger` | Hair Trigger | sin (+0.40x) | always; **re-draftable** (only non-unique sin) |
| `playing_fire` | Playing with fire | sin (+0.15x) | always; Unique; shielded keeps last-second gold |
| `bright_colors` | Look at the bright colors... | sin | always; Unique |
| `cam_girl` | Cam Girl | sin (+0.40x) | always; Unique; shielded keeps tips |
| `the_urge` | The urge | sin | always; Unique; shielded keeps x3 |
| `double_or_nothing` | Relapse | sin | always; Unique; 60% extra loop (shielded = certain) |

There is **no rank/run-count filter anywhere in the pool** — a descent-1 player can draw Body Buzz logic-wise (only equipment gates it).

## A3. The rank ladder (`ChaosUpgrades.cs:167`, `ChaosMeta.Rank`)

Derived from lifetime `RunsCompleted`, monotonic:

| Rank | Threshold (descents) |
|---|---|
| Curious | 0–2 |
| Tempted | 3–9 |
| Slipping | 10–24 |
| Entranced | 25–49 |
| Devoted | 50–99 |
| Claimed | 100+ |

**What's keyed to it today: almost nothing.** It renders in the Warren top bar (`TxtRank`) and nowhere else. The Tease spawn gate checks the raw count (`RunsCompleted >= 10`, `ChaosModeService.cs:688`) — the comment calls it "the Slipping rank" but it doesn't read `ChaosMeta.Rank`. No rank-up moment, card, bark, or sound exists.

## A4. Warren tabs and unlock conditions (`ChaosHubWindow.xaml:152-156`, `ApplyUnlocks` at `.xaml.cs:66`)

| Tab (display) | tag | At first launch | Gate |
|---|---|---|---|
| Loadout | `loadout` | **open** | none |
| Enhancements | `enhance` | **open** | none |
| Habits | `habits` | **open** | none |
| The Descent | `run` | **open** | none (run setup must be reachable pre-run-1) |
| Improvements | `improve` | locked | `RunsCompleted >= 1` (`UNLOCK_STATS_RUNS`); tooltip: "finish a descent to unlock" |

**Dead constants:** `UNLOCK_UPGRADES_RUNS = 1`, `UNLOCK_CODEX_RUNS = 1`, `UNLOCK_LOADOUT_RUNS = 3` are defined (`ChaosUpgrades.cs:161-164`) but never read — only `UNLOCK_STATS_RUNS` is consumed. Evidence an earlier progressive-disclosure intent was started and rolled back to "everything open".

Entry points: Lab card **"▶ FALL IN"** (`MainWindow.xaml:7520` → `BtnStartChaos_Click` opens the Warren) and a **Quick Start** button (`BtnQuickStartChaos_Click` → `StartRun()` directly, bypassing the hub). No Patreon/premium gate on either.

## A5. chaos_meta.json schema (`ChaosMetaState.cs`; stored at `%APPDATA%/ConditioningControlPanel/chaos_meta.json`, atomic tmp+move, never-throws load)

| Field | Type | Default | Used for |
|---|---|---|---|
| `SchemaVersion` | int | 1 | future migrations (unused so far) |
| `Sparks` | int | 0 | the drops balance |
| `PurchasedUpgrades` | set\<string\> | {} | trained habits |
| `DisabledUpgrades` | set\<string\> | {} | habits switched OFF (absent = on; old saves stay active) |
| `ExtremeUnlocked` | bool | false | Inescapable pill |
| `EquippedStartBoon` | string? | null | start-mantra picker |
| `DiscoveredCodexIds` | set\<string\> | {} | Diary/mantra silhouettes (`bubble:{id}` / `boon:{id}`) |
| `LifetimeBoonLevels` | dict\<string,int\> | {} | skill/accessory/charm levels (0/absent = locked) |
| `ActiveLifetimeBoons` | set\<string\> | {} | equipped/worn boons |
| `SeenDefuseTutorial` | bool | false | one-time hold-to-snap announce |
| `SeenBarkDefuseFirst/NoFocus/Release`, `SeenBarkClickDetonate` | bool ×4 | false | first-time defuse barks |
| `SeenEcho/Chaperone/Tease/Bound/Brittle` | bool ×5 | false | behavioral-bubble debut beats |
| `RunsCompleted` | int | 0 | rank + Improvements gate + Tease gate + First Spark guard |
| `BestScore` / `BestCombo` / `TotalDefused` / `TotalRunSeconds` | long/int/long/double | 0 | stats strip + PB delta |

All additive-defaults — the established pattern the design's "first-time flags checked at spawn/draft time" maps onto directly.

## A6. Bark integration surface

**Raise path:** `ChaosModeService` (and `ChaosOverlayWindow.ShowResults`) call `App.Bark?.NotifyChaosX(...)` → `BarkService.Raise("TriggerName", ctx)` (`BarkService.cs:208-287`). First-time gating for the `rh_*` family lives in chaos meta flags, **not** rule conditions.

**Rule format** (`Resources/sounds/companion_audio/bark_rules.json`, loaded by `BarkRuleLoader`): array of
`{ id, trigger, conditions{key_op: value}, priority, cooldown_ms, repeatable, scope, mood, class, variant_pool[{text, audio}] }`.
Base manifest defines **39 rules**; per-mod manifests (`mods/builtin-{bambisleep,sissyhypno,locked}/bark_rules.json`, 451/383/384 rules) override by `id` (field-level merge — mod rows omit `trigger` and supply localized variant pools).

**Chaos triggers in use (21)** with their rule ids:

| Trigger | ctx keys | Rules today |
|---|---|---|
| ChaosRunStarted | difficulty | chaos_run_started, _extreme |
| ChaosWaveEscalated | wave | chaos_wave_escalated, chaos_wave_final_stretch |
| ChaosActChanged | act, wave | chaos_act_changed |
| ChaosBenignPopped | variant_id, payload, combo | chaos_benign_popped |
| ChaosBubbleDefused | combo, payload, difficulty | chaos_bubble_defused |
| ChaosBubbleDetonated | payload, strength, run_detonations, combo, difficulty | chaos_bubble_detonated, _first, _extreme, _braindrain, _streak_dies |
| ChaosBubbleDetonatedAbsorbed | + shields_left | chaos_detonate_absorbed |
| ChaosDarterCaught | points, combo, quick | chaos_darter_caught, _quick |
| ChaosFreezeCaught | points, combo | chaos_freeze_caught |
| ChaosWaveCleared | wave | chaos_wave_cleared |
| ChaosBoonPicked / ChaosCursePicked / ChaosBoonSkipped | boon, rarity, run_mult_bonus / shields_now | chaos_boon_picked, chaos_curse_picked, _juicy, chaos_boon_skipped |
| ChaosComboMilestone / ChaosComboBig | combo, difficulty / threshold | chaos_combo_milestone, _25, _50, _100 |
| ChaosRunCompleted | xp, difficulty | chaos_run_completed, _extreme |
| ChaosResultsShown | score, best_score, pb_delta, is_pb, defused, detonated, best_combo, difficulty | chaos_results_shown, chaos_results_pb |
| ChaosDefuseFirst/NoFocus/Release, ChaosClickDetonate, ChaosFocusLow | — | rh_defuse_first/nofocus/release, rh_click_detonate, rh_focus_low |
| ChaosTeaseDebut/Denied/Clicked/DeniedStreak | denied_count | rh_tease_debut/denied/clicked/denied_streak |

**Gap for progression barks:** no trigger carries `runs_completed` or `rank` context, and there is no "hub opened / shelf revealed / rank up" trigger. New progression barks need either new `NotifyChaosX` methods or a `rank`/`runs` ctx key added to ChaosRunCompleted/ResultsShown.

## A7. Currency — earn, sinks, first-time bonuses

**End-of-run award** (`ChaosMeta.AwardRunRewards`, `ChaosUpgrades.cs:366`):
```
sparks = round((score/100 × diffMult + 35 × diffMult) × SparkGainMult)
       + TrickleDrops                      (Drip Feed, ×2 inside a Relapse loop)
       × 1.10 if drip_feed maxed           (on the whole haul)
       + 25 if RunsCompleted == 0          (FIRST_SPARK_BONUS — "First Spark", the only first-time bonus in code)
```
`SparkGainMult` is always 1.0 (the `spark_gain` habit was retired pre-release, no live writer). diffMult = 1.0/1.3/1.7/2.2.

**Instant in-run gold** (banks straight into `ChaosMeta.State.Sparks`, persists immediately — survives an abandoned run):

| Source | Amount | Condition |
|---|---|---|
| Lucky golden bubble | 10–20 base; 12–24→20–40 by Rabbit's Foot level | 0.5% per spawn base; 1.0–2.0% worn |
| Gold Digger droplet | 3–8 each (×3 droplets) | `gold_digger` mantra |
| Tease denied | 5–10 | Tease expires untouched |
| Cam Girl tip | 2–5 | 25% per pop while the sin runs |
| Playing with fire | 5–10 | snap inside the final second |
| (all of the above) | ×2 | during the Relapse bonus loop |

**Sinks:** the four shelves (A1) — nothing else consumes drops. No respec costs (unequips are free; habits toggle free).

## A8. Difficulty gating as implemented

Pills on The Descent tab (`ChaosHubWindow.xaml:240-243`): **Gentle** (`Easy`), **Teasing** (`Medium`), **Relentless** (`Hard`) — all open from install. **Inescapable** (`Extreme`) renders "Inescapable 🔒", enabled only by the `extreme_tier` habit (350 ✦ → `ExtremeUnlocked`). Randomize respects the lock.

Content gated by difficulty (not rank):

| Content | Gate |
|---|---|
| Echo (5%), Chaperone (4%) | not on Gentle |
| Brittle (3.5% rider) | not on Gentle |
| Bound (3%) | Relentless+ (`>= Hard`) |
| Tease (3%) | **run count** (≥10 descents), any non-Gentle... actually any difficulty incl. Gentle is excluded by the early return — behavioral roll exits on Gentle first |
| video / htlink variants | `MinIntensity` 0.50/0.60 — late-run intensity, plus user pool toggles |
| braindrain | `MinIntensity` 0.25 |

## A9. Recently landed (hold-to-defuse rework + behavioral bubbles) — final identifiers

Focus economy: `FOCUS_MAX 100`, start 50, +10/pop (+12 golden, +15 rabbit/heavy, +10 heart/prism/denied, +4 droplet), defuse costs 30 (Bound halves 15 each), hold 1000 ms, free while frozen. Onboarding flags + barks per A5/A6.

| Code id | Display | Debut flag | Spawn gate | Debut announce |
|---|---|---|---|---|
| `echo` | The Echo | `SeenEcho` | non-Gentle, 5% | "◌ THE ECHO — hold it down, or it multiplies" |
| `chaperone` | The Chaperone | `SeenChaperone` | non-Gentle, 4% | "💞 THE CHAPERONE — its little escort first" |
| `tease` | The Tease | `SeenTease` (+`rh_tease_debut` bark) | ≥10 descents, 3% | "✖ THE TEASE — whatever you do, don't" |
| `bound` | The Bound | `SeenBound` | Relentless+, 3% | "⛓ THE BOUND — both, and quickly" |
| `brittle` | The Brittle | `SeenBrittle` | non-Gentle, 3.5% rider | "◇ THE BRITTLE — don't even hover" |

Debuts spawn alone (consume the spawn tick) with `DEBUT_FUSE_MULT 1.5`; all five write `MarkDiscovered("bubble:{id}")` → Diary rows exist for all (incl. brittle). These are the **only** run-count/difficulty-gated content beats in the mode today — the Tease is the prototype of the design's rank-gating.

## A10. Everything else progression-relevant

- **Stats strip** (Improvements tab): drops, descents, time under, best score, best streak, total snapped. Unlocks with the tab (1 descent).
- **Diary** (Improvements box + pop-out window): every variant + darter/golden/echo/chaperone/tease/bound/brittle. Un-met entries render a `?` tile, name "???", body **"hazy. go back down and look closer."** — the canonical lock voice. Keyed to `DiscoveredCodexIds` at spawn time.
- **Mantra box / start-mantra picker** (Improvements): all 23 pool cards listed; undiscovered = "???" + hazy line; discovered mantras clickable → `EquippedStartBoon` (gold ring + "start ★"); sins labeled **"taken, never chosen"**.
- **Improvements bench**: two placeholder rows — "a second skill pocket / not yet sewn. the seamstress takes her time." and "a second accessory pocket / not yet sewn. she only has two hands." Not purchasable.
- **Loadout tab**: pocket slots (1 skill + 1 accessory, `SlotsFor`), collection grids with tile states Equipped(gold)/Owned(pink)/Locked(dim+"???")/Empty("+", e.g. "another accessory is being stitched / it'll hang here when it's ready."). Locked tiles deep-link to the selling tab.
- **Recap card** (`ChaosOverlayWindow.ShowResults`): hero banner art, chips DEPTH·BEST STREAK·SURVIVED, snapped/triggered/effects line, score + PB delta ("★ NEW BEST"), chips XP ("✦", "base × skill") + GOLD ("banked in the warren"), buttons "▸ FALL DEEPER" / "wake up" ("wake up (you'll be back)" on a PB).
- **XP payout**: `min(score, 250 × minutes × diffMult)` → `Progression.AddXP(XPSource.Chaos)` — separate from drops.
- **Pendulum** free once-per-loop slow-mo event; **freeze** pickup variant; **goldens** base 0.5%; **hearts** behind the `popup_notification` habit — all present from descent 1 (no gating).

---

# PART B — DESIGN MAPPING (intent vs reality)

Legend: **EXISTS** (infrastructure ready, content/config work only) · **PARTIAL** (some machinery, needs extension) · **MISSING** (new machinery).

### The gating spine
- **Rank ladder as spine — PARTIAL.** The ladder *exists with exactly the design's names* (Curious→Tempted→Slipping→Entranced→Devoted→Claimed, thresholds 3/10/25/50/100 — your "rank 2/3/4/5 ≈ 10/25/50/100" maps cleanly, and Tempted@3 is a free extra beat for the descent-3 rank-up). But nothing reads it. Needed: a `ChaosMeta.RankIndex` int (string compares are fragile), gate checks in `ApplyUnlocks`, `BuildHabits/BuildLifetimeBoons` (silhouette rows), `ChaosBoonPool.Draftable`, and the difficulty pills. Cheap.
- **Conflict:** the Tease gate hardcodes `>= 10` instead of the rank — converge on one source.

### Descent 1 — the naked run
- Hub hidden, straight into the fall — **PARTIAL/conflict.** Today the Warren IS the launcher (FALL IN opens it; run setup lives on The Descent tab). The Quick Start path (`BtnQuickStartChaos_Click`) already starts a run hub-free — a `RunsCompleted == 0` branch on the Lab card can reuse it. Needs a curated first-run `ChaosRunConfig` built in code (ignore settings).
- Minimal bestiary (flash, subliminal, pink, spiral) — **EXISTS** (`ChaosRunConfig.EnabledVariants` whitelist; freeze/braindrain/video/htlink excluded by omission; behavioral bubbles excluded by forcing Gentle or a new flag).
- No drafts — **EXISTS** (`BoonDraftEnabled = false`).
- No toys — **EXISTS** (don't apply lifetime boons / empty pockets on run 1; trivial guard in `BeginRun`).
- Reduced spawn density — **MISSING** (spawn cadence formula `ChaosModeService.cs:618` has no multiplier knob; add `SpawnRateMult` to config).
- Scripted first-live teaching — **PARTIAL** (`SeenDefuseTutorial` announce exists; a *scripted* lone first live with a long fuse needs a small forced-spawn beat like the debut pattern).
- Hub revealed AFTER first surface, with run tab + 2–3 item shelf — **MISSING/conflict.** `ApplyUnlocks` opens four tabs unconditionally. Real names: the run tab is **"The Descent"** (tag `run`); the 2–3 item starter shelf would be Habits rows filtered to e.g. `start_resistance` (100), `blank_eyes` (120), `slow_fuses` (120).

### Descent 2 — drafts
- Drafts unlock — **PARTIAL** (`BoonDraftEnabled` exists but is a *user setting*; the gate must override the setting below the threshold, not fight it).
- Plain-mantras-only curated pool (~5) — **MISSING** (no rank filter in `Draftable`; one extra predicate). Real starter five: `extra_shield`, `defuse_chain`, `golden_touch`, `welcome_shower`, `heavy_drop`.
- No skip yet — **conflict.** Skip (+1 resistance) and the 15 s auto-skip exist from day 1 and the auto-resume *depends* on skip. If skip hides at D2–3, auto-resume needs a fallback (auto-take leftmost card, or pause until pick).

### Descent 3
- Scripted first sin, rigged good Relapse-type — **PARTIAL.** `Draft(guaranteeCurse:)` exists (Surrender capstone uses it); forcing the *specific* card `double_or_nothing` with its shielded apply (`ApplyShielded` = certain bonus loop = guaranteed-good) is a small new overload. The pieces are all on the shelf.
- Rank-up beat — **MISSING** (no rank-up detection/card anywhere). Tempted fires at exactly 3 ✓.
- Habits shelf opens partially — **conflict** (always fully visible today; silhouette rendering exists for *tiles* but Enhancements/Habits *rows* always render full).
- New difficulty — **conflict.** Teasing is open from install. Gating it to D3 takes a pill-lock like `ApplyExtremeGate` (pattern exists).

### Descent 4
- First rabbit scripted — **PARTIAL/conflict.** Darters roll from run 1 (`DartersEnabled` default true). Scripted debut = gate `RollDarter` behind a flag + one forced `SpawnDarter()` with an announce (debut pattern exists).
- Skip-for-resistance debuts — see D2 conflict.
- Sins enter at reduced rate — **MISSING** (the 50% curse-slot odds are a literal in `Draft`; needs a `sinChance` param).

### Descent 5
- BrainDrain spectacle debut — **PARTIAL** (`MinIntensity 0.25` gates it within a run, not across runs; add it to the variant whitelist at D5 + a debut announce).
- Pool deepens silently — **EXISTS** (whitelist growth).
- Session-end hook bark teasing a locked room — **PARTIAL** (ChaosRunCompleted/ResultsShown rules exist; needs `runs_completed` ctx on the trigger to condition `runs_eq: 5`).

### ~Descent 10 (Slipping)
- Charms shelf "a new room" — **conflict.** Charms are not a room: they render inline on the **Habits** tab (`BuildHabits` appends Utility boons) and inside the habits tile grid. "A new room" means either a new tab or a revealed section header within Habits.
- Accessory pocket unlocks (start with skill pocket ONLY) — **MISSING/conflict.** `SlotsFor` returns 1 flat for both; rank-aware `SlotsFor` + grandfather clause (any accessory owned ⇒ pocket open) required.
- Stats page + Diary — **conflict.** Both unlock at 1 descent today (Improvements tab). Moving to 10 *takes content away* from existing sub-10 players unless grandfathered.
- Freeze / pendulum / goldens / hearts enter the field — **conflict/PARTIAL.** All live from descent 1 today (hearts behind the habit purchase). Gating = whitelist (`bambifreeze`), flag (pendulum event), `GoldenChance = 0` below rank, habit row hidden below rank.
- Tease — **EXISTS** at exactly 10 already ✓.

### ~Descent 25 (Entranced)
- Third difficulty (Relentless) — **conflict** (open from install; same pill-lock work).
- Duo mantras — **PARTIAL** (equipment-gated today; add rank floor to `Draftable`).
- Giant bubbles video/htlink — **PARTIAL** (intensity-gated within runs; add to whitelist at rank).
- Start-mantra picker — **conflict** (lives on Improvements since descent 1; move behind rank or behind a bench purchase).
- Boss loop hooks (8–12 window, separate mission) — **MISSING.** Note: the approved NEW CORE STRUCTURE (4 waves 1/1/1/2 min, boss W4, recap) is locked in the plan but unimplemented; the boss debut should ride that work, with `ChaosMeta` first-time flag + announce reserved now (`SeenBossLoop`).

### ~Descent 50 (Devoted)
- Capstone levels purchasable — **MISSING** (any level buys anytime; needs a rank check in `TryUpgradeBoon` when `level+1 == MaxLevel` + lock-row UI).
- Top difficulty habit visible — **PARTIAL.** `extreme_tier` is visible AND buyable from install (only drops-gated). Design = visible always, *buyable* at Devoted.
- Second pocket on the bench — **PARTIAL** (placeholder rows exist; purchase flow MISSING; `SlotsFor` already centralizes the change; HUD keybind slot 2 (`ChaosAccessoryKey2`/"E") already plumbed in `BuildActiveToys`).
- Surrender capstone — exists; falls under the capstone gate above.

### ~Descent 100 (Claimed)
- Daily seeded run / leaderboard / prestige — **MISSING entirely.** Note `Random.Shared` is used throughout spawning — a seeded run means threading a seeded RNG through `ChaosBubbleVariants`/`ChaosModeService` (not huge, but touches everything). Reserve `SeenEndgame` flag + a Claimed-locked bench row now, build later.

### Economy intent
- First purchase right after descent 1 — **NEARLY TRUE.** D1 yields ~100–160 ✦ (score part + 35 completion + 25 First Spark); `start_resistance` costs exactly 100. ✓ by accident or design.
- First-time micro-bonuses (first pop / first defuse / first draft / first sin / first toy) — **MISSING** (only First Spark exists). The Seen-flag pattern makes these ~10 lines each.
- Locked items as silhouettes with lock-tooltip flavor, never hidden once the shelf exists — **PARTIAL.** Tiles do this (Loadout grids, Diary, mantra box); Enhancements/Habits *rows* show full name+desc when locked (only dimmed) — they'd need a silhouette mode.
- Draft pool grows with rank — **MISSING** (one predicate, cheap).
- Scripted debuts as chaos_meta flags, not run-count checks — **EXISTS** as the established pattern ✓ (note: design says flags, but *availability* gates should stay run-count/rank-based; flags are for the one-time beat itself. The Tease does both correctly.)

## B.2 Proposed unlock table (real identifiers)

| Descent | Rank | Unlocks / scripted beats |
|---|---|---|
| 1 | Curious | Forced naked run (no Warren): `EnabledVariants=[flash,subliminal,pink,spiral]`, `BoonDraftEnabled=false`, `DartersEnabled=false`, no boons applied, `SpawnRateMult≈0.6` (new), scripted lone first live (`DEBUT_FUSE_MULT`-style). Surface → First Spark +25 → Warren reveals with **The Descent** tab + a 3-row Habits shelf: `start_resistance`, `blank_eyes`, `slow_fuses`. |
| 2 | Curious | Drafts on; pool = `extra_shield, defuse_chain, golden_touch, welcome_shower, heavy_drop`; no skip button; first-draft micro-bonus. |
| 3 | **Tempted** (rank card #1) | Scripted guaranteed sin = `double_or_nothing` with `ApplyShielded` (certain bonus loop); first-sin micro-bonus; Teasing pill unlocks; Habits shelf fills (`silk_touch`, `popup_notification`, `draft4` rows appear as silhouettes→buyable); Enhancements tab reveals with 2 skills (`vibe_popping`, `freeze_trigger`) + 2 accessories (`chain_reaction`, `the_pull`). |
| 4 | Tempted | Scripted first darter + announce; `DartersEnabled` unlocks; skip-for-resistance appears at the table; sins enter the pool at `sinChance 0.25`. |
| 5 | Tempted | `braindrain` joins the whitelist (spectacle debut announce); remaining always-pool mantras (`gold_digger`, `gg_rabbits`, `size_queen`, `aftermath`, `focus_here`) drip in over D5–9; ChaosRunCompleted teaser bark ("there's a room you haven't seen", `runs_eq: 5`). |
| 6–9 | Tempted | Remaining skills/accessories appear on the shelves one per descent (silhouette → named); `sinChance` ramps 0.25→0.5; Loadout tab reveals once ≥2 ownables exist. |
| 10 | **Slipping** (rank card #2) | Charms section reveals on Habits ("a new room"); **accessory pocket opens** (was skill-only); Improvements tab (stats + Diary); `bambifreeze` whitelist + pendulum event + `GoldenChance` base 0.5% + `popup_notification` row; Tease begins (already coded). |
| 25 | **Entranced** (#3) | Relentless pill; duo cards draftable (`overload`…`body_buzz`); `video` + `htlink` whitelist; start-mantra picker on Improvements; boss-loop debut flag reserved (8–12 window, separate mission). |
| 50 | **Devoted** (#4) | Capstone (final) levels purchasable; `extreme_tier` becomes buyable (visible since D3); second skill + accessory pockets purchasable on the bench; Surrender L3. |
| 100 | **Claimed** (#5) | Reserved gates: daily seeded descent, leaderboard, prestige. One locked bench row each, endgame-flavor tooltip, nothing functional yet. |

## B.3 Economy curve (real formula, real prices)

Earn model per completed descent (3 min, `SparkGainMult` 1, no Drip Feed): `(score/100 + 35) × diffMult` + incidental gold (~10–60 from goldens/denials).

| Phase | Typical score | Difficulty | ✦/descent (base) | Cumulative by end of phase |
|---|---|---|---|---|
| D1 | 3–6k | Gentle | ~90–120 (+25 First Spark) | ~100–160 |
| D2–5 | 5–12k | Gentle/Teasing | ~100–200 | ~500–900 |
| D6–10 | 10–20k | Teasing | ~170–300 | ~1.5–2.5k |
| D11–25 | 15–35k | Teasing/Relentless | ~250–550 | ~6–10k |
| D26–50 | 25–60k | Relentless | ~450–900 | ~18–30k |
| D51–100 | 40–100k | Relentless/Inescapable | ~700–1,500 | full catalogue (32.4k) well before D100 |

Checkpoints vs shelf costs:
- **D1 → first purchase:** 100–160 earned vs `start_resistance` 100 ✓ (intent already true).
- **D10 → charms wave:** ~1.5–2.5k earned vs starter spend ~1.4k (3 cheap habits + 1 skill unlock + 1 accessory unlock + first charm) — *slightly poorer than the next thing* ✓.
- **D25:** ~6–10k earned vs ~12k of "everything visible so far" — healthy hunger ✓.
- **D50–100:** capstones (~15k of the catalogue) land on schedule **only if** mid-game income includes Drip Feed or equivalent.

**Pacing breaks found:**
1. **`drip_feed` detonates the curve.** +5–20/pop × ~80–150 pops ≈ **+400–3,000 ✦/descent** — at L2+ it multiplies income 3–10×. Unlocked at 250 ✦ it's buyable by D3–4 today. Recommendation: make Drip Feed *the* Slipping-rank economy unlock (it IS the "new room" reward), and/or cut LevelValues to 1/2/3/5.
2. **Without Drip Feed the tail starves:** 32.4k total at ~250–550/descent = 60–130 descents of pure grinding for completion. The curve *needs* the drip — which is fine once it's rank-placed deliberately.
3. **Instant-gold persistence:** golden/denial gold saves to disk immediately mid-run; an abandoned ("wake up") descent keeps the gold but `RunsCompleted` doesn't tick — farming-by-abandon is possible but weak. Acceptable; note it.
4. **`hair_trigger` re-draftable** stacking −25% fuse per take is a difficulty cliff unrelated to economy but worth a Unique flag review.

---

# PART C — TERMINOLOGY & FLAVOR PROPOSALS

Voice studied from shipped strings: snap / trance / trigger / streak / resistance / lust (HUD label for Heat) / descent / loop / depth / drops (✦) / FALL IN / SINK / YOU SURFACE / FALL DEEPER / wake up / the Warren / pockets / train (habits) / charms / mantras & sins / the seamstress ("not yet sewn", "being stitched") / "hazy. go back down and look closer." / "taken, never chosen" / "you were always going to." Lowercase, understated, no em dashes, the hole as a patient predator, tailoring metaphors for the meta layer.

## C1. The progression system's own name
1. **the claim** *(recommended)* — ranks are stages of being claimed; the final rank already says it; "the hole's claim on you deepens" explains every gate in four words.
2. **the deepening** — neutral, reads as both the player going down and the warren getting deeper; pairs with "it deepens at {rank}".
3. **the undertow** — inevitability made physical; what hands you things as it drags you down.
4. **how far you've fallen** — plain-spoken stat-page framing; works as the rank panel header even if another name wins.
5. **the fitting** — the seamstress thread: she fits the warren to you as you sink; warm and sinister at once.

## C2. Rank-up moment presentation
Name options for the beat/card itself:
1. **the naming** *(recommended)* — the hole names what you've become; the card just shows the new word.
2. **depth marks** — scratch-marks-on-the-wall framing, fits the diary voice.
3. **a deeper fitting** — seamstress thread; "she let the hem out again."
4. **it noticed** — the card title literally; understated dread.

One line per transition (lowercase, no em dashes):
- **Curious → Tempted (3):** "tempted. three times down. you can stop calling it curiosity."
- **Tempted → Slipping (10):** "slipping. the climb out takes longer every time. you noticed. you came anyway."
- **Slipping → Entranced (25):** "entranced. you don't fall anymore. you arrive."
- **Entranced → Devoted (50):** "devoted. the warren keeps a room warm for you now. it always knew it would."
- **Devoted → Claimed (100):** "claimed. it stopped counting your visits a long time ago. so did you."

## C3. Lock-state tooltip families (siblings of "hazy. go back down and look closer.")

**(a) item visible but rank-locked:**
1. *(rec)* "not yours yet. the hole knows exactly how many times you've been down."
2. "she'll sell this to someone deeper."
3. "locked. not by a key. by how far you've come."
4. "it would fit you. it doesn't want to yet."

**(b) shelf/room not yet revealed:**
1. *(rec)* "there's a wall here that isn't quite a wall."
2. "you can hear something being sewn behind it."
3. "this part of the warren hasn't decided to exist for you yet."
4. "a door you've walked past before. it hasn't noticed you. keep falling."

**(c) capstone locked behind rank:**
1. *(rec)* "the last stitch is hers to give. she gives it to the devoted."
2. "it goes one level deeper than you do."
3. "the final rank is sealed. it's waiting for steadier hands."

**(d) endgame gates:**
1. *(rec)* "the bottom is not where you think it is."
2. "what lives below the warren doesn't have a name yet."
3. "deeper than devotion. you'll see."

## C4. Shelf/room reveal lines (announce/bark when a tab or shelf appears)
1. *(rec)* "a new room. it was always there. you just weren't."
2. "the seamstress cleared a shelf for you. she doesn't do that for everyone."
3. "the warren is bigger today. or you're smaller. either way, go look."
4. "a wall came down overnight. nobody heard it."

## C5. First-time bonus family ("First Spark" exists for the first surface)
Family name options:
1. **first times** *(recommended)* — plain, quietly loaded, scales to any count.
2. **firsts** — terser sibling.
3. **little firsts** — warmer, seamstress-adjacent.
4. **sweet nothings** — riskier; reads as the hole paying you compliments in coin.

Per-bonus labels (each a one-time ✦ drop with a toast):
- first pop → **"first taste"**
- first snap (defuse) → **"first snap"** (the lexicon word is already perfect)
- first draft pick → **"first whisper"**
- first sin accepted → **"first yes"**
- first toy fired → **"first play"**
- (existing) first surface → **"first spark"** (keep; it predates the family and still fits)

## C6. The silhouette/locked render state
1. **hazy** *(recommended)* — already canon in the diary line; extend it: "hazy" as the state name in any UI copy ("three shelves still hazy").
2. **unmet** — pairs with the diary's "what you've met down there".
3. **still sealed** — for capstones specifically.
4. **behind the wall** — for unrevealed rooms specifically (matches C3b).

## C7. Placeholder-name collisions & corrections

| Design placeholder | Reality |
|---|---|
| "Curious -> Claimed or whatever" | **Exactly right**: Curious / Tempted / Slipping / Entranced / Devoted / Claimed at 0/3/10/25/50/100. Six ranks, five transitions — the design's "rank 2/3/4/5" = Slipping/Entranced/Devoted/Claimed, and Tempted@3 is a bonus early beat. |
| "drops (✦)" | Code: `Sparks`. UI: "drops" (Warren), "GOLD" (recap chip), "gold" (every event line), "Sparks" (one diary blurb: gold_droplet). **Four names, one pool — pick a canon split** (suggest: *drops* = the banked currency word, *gold* = allowed in-run flavor for instant payouts, retire "Sparks" from user-facing text — `gold_droplet` codex line says "a few Sparks" today). |
| "charms shelf / a new room" | Charms live **inline on the Habits tab**, not a room. The reveal must create the room (new tab or revealed section). |
| "run tab" | Real name **"The Descent"** (tag `run`). |
| "Inescapable Tier habit" | Real: `extreme_tier`, display "Inescapable Tier", 350 ✦. |
| "skip-for-resistance" | Real: draft skip → +1 resistance (`ChaosBoonSkipped`), exists from day 1 + 15 s auto-skip depends on it. |
| "First Spark" | Real: `FIRST_SPARK_BONUS = 25` in `AwardRunRewards`. The only first-time bonus in code. |
| "gif cascade" | Variant id `htlink` (persisted, never change), display **"Gif Rain"**. |
| "boss loop" | Nothing in code. The locked NEW CORE STRUCTURE (4 waves 1/1/1/2 min, boss W4) is approved-unbuilt; reserve `SeenBossLoop` only. |
| "leaderboard" | A main-app leaderboard exists (`NotifyLeaderboardViewed`) — unrelated to chaos. A chaos leaderboard is net-new (server work). |
| "plain mantras" | The three re-draftable stat cards: `defuse_chain` (Snap Chain), `golden_touch`, `extra_shield` (Left Brain). |

**Retired ids — never reuse** (refund machinery in `ChaosMeta.RefundRetiredBoons` keys on them): `muscle_memory`, `magic_wand`, `spark_gain`, `tunnel_vision`, `max_bubbles`, `bigger_hitboxes`, `magnet`, `shield_recharge`, `start_shield`, `base_mult`, `take_more`, `pendulum` (as a purchasable; the free event keeps the display word).
**Traps:** `collar` and `golden_touch` appear in retired-refund lists **and** as live ids (charm / charm+run-boon) — they're fine where they are, but any *new* proposal must avoid both the retired list and every live id in A1/A2. `golden_touch` existing twice (charm + mantra) is prior art for cross-store id reuse, but don't add a third.

---

# CONFLICTS & RISKS

**Architecture friction**
1. **The Warren is the front door.** The design's "no hub on descent 1" inverts the current flow (Lab → Warren → FALL IN). Lowest-risk shape: Lab button branches on `RunsCompleted == 0` into a code-built naked `ChaosRunConfig` (Quick Start path), and the Warren first opens from the recap ("a door appears"). Don't try to make the Warren itself progressively render on first open — reveal the whole window later instead.
2. **Run setup is user-owned settings.** Drafts, darters, difficulty, pool toggles are `AppSettings` the player can already touch. Progressive gates must *clamp* (`enabled && rankOk`), never overwrite the saved setting, or unlocking feels like the app forgot their config.
3. **Skip vs auto-resume.** Hiding skip before D4 breaks `DraftAutoResumeSec` (its timeout action IS skip). Pre-skip descents need the timeout to auto-take a card or pause indefinitely.
4. **Rank as string.** Gate on a new `RankIndex`/enum, not `ChaosMeta.Rank` string compares; keep the Tease's `>= 10` and the new gates reading one source.
5. **Bark context gap.** Progression barks need `runs_completed`/`rank` ctx keys or new triggers (`ChaosRankUp`, `ChaosShelfRevealed`); base manifest + 3 mod manifests + audio per the established `/add-barks` pipeline.
6. **Seeded daily** (Claimed tier) fights `Random.Shared` everywhere — flag now, design later.

**Save migration (CRITICAL — nobody loses anything)**
- Owned items must never re-lock: every rank gate is `rankOk || alreadyOwned` (`LifetimeBoonLevels`/`PurchasedUpgrades` checked first). Banked `Sparks` untouched.
- Rank derives from already-persisted `RunsCompleted` — veterans land mid-ladder automatically, no data migration needed for the spine.
- **Grandfather rules:** accessory pocket opens if `RunsCompleted >= 10` **or** any accessory level ≥ 1 (covers the 5-run player who owns Poppers). Improvements tab moving 1→10 must keep `>= 1` for saves with `RunsCompleted >= 1` at migration time, or simply stay at 1 for anyone who already has the tab (one-time `SchemaVersion 2` flag: `LegacyOpenHub = true` when migrating a file with `RunsCompleted > 0`).
- Scripted D1–D5 beats key on `RunsCompleted < N` so veterans never see tutorials; debut `Seen*` flags already protect re-announces.
- `EquippedStartBoon`/`ActiveLifetimeBoons` referencing now-gated content: keep functional (owned ⇒ usable), only *acquisition* is gated.

**Build-cost honesty**
- **Cheap:** rank index + gate checks; draft-pool rank predicate + `sinChance` param; variant whitelist per rank; first-time micro-bonuses; lock tooltips; forced first sin (`guaranteeCurse` + forced card); dead-constant cleanup; bark rules (content pipeline exists).
- **Medium:** naked descent 1 (entry branch + curated config + `SpawnRateMult` + scripted first live); rank-up card (new `ChaosOverlayWindow`/announcer state); accessory-pocket gating + grandfather; shelf silhouette mode for Enhancements/Habits rows; difficulty pill locks; Improvements gate move with migration; second-pocket purchase (bench row + `SlotsFor` + key 2 already plumbed).
- **Expensive:** boss loop (separate mission, rides NEW CORE STRUCTURE); daily seeded run + chaos leaderboard + prestige (RNG threading + server); full economy retune (Drip Feed repricing/placement needs playtesting — it currently is the economy).
