# Chaos Mode — "Down the Rabbit Hole" — Design Reference

A roguelite minigame layered over the **live desktop**: effect bubbles drift over your
real screen (still clickable), each carrying a conditioning payload. Pop the treats,
**hold-to-defuse** the threats before their trance runs out, draft mantras between loops,
and bank **Drops** (✦) and **Gold** (🪙) toward the Warren's permanent collection.

> Source of truth: `Services/Chaos/`. This doc is generated from that code — if a number
> here disagrees with the code, the code wins. Last synced against the v2 systems
> (hold-to-defuse / focus / behavioral bubbles / two-currency split, 2026-06-11).

---

## 1. Core gameplay loop

```
Lab ▸ DESCEND
   │
   ├─ StartRun: build config (settings + trained habits), apply equipped lifetime
   │            boons + start mantra, pause ambient bubbles, show HUD strip + overlay
   ├─ Countdown ........... 3 · 2 · 1 · GO   (click-through; desktop stays usable)
   │
   ├─ RUN LOOP  (per loop/wave, escalating with run progress)
   │     SpawnTick → weighted variant + behavioral-bubble riders + darter/golden rolls
   │     You interact:
   │        ○ treat        → pop = payload fires + score + streak + heat + FOCUS
   │        ◑ live (threat)→ HOLD 1s to defuse (spends 30 focus; fuse pauses while held)
   │                          · a CLICK or early release TRIGGERS it (payload fires)
   │        💥 fuse-out     → detonation: spends 1 resistance, else streak+heat reset
   │        🐇 darter       → catch = slow-mo (0.12× for 6s)
   │        ❄ freeze       → catch = field hold 3.5s (bubbles stay poppable)
   │        🍀 golden       → pop = instant gold
   │
   ├─ LOOP BOUNDARY
   │     pop the field → MANTRA DRAFT (3–4 cards, one slot may deal a sin)
   │     pick one → apply → next loop   (skip = +1 resistance; 15s auto-skip)
   │
   └─ RUN END (timer elapses; Relapse may bolt on one more loop)
         XP payout (capped) + Drops payout → recap card → rank-up / reveals → Warren
```

**Escalation.** `RunIntensity = elapsed / duration` (0→1) drives everything:

| Knob | Formula |
|------|---------|
| Spawn interval | `(1300 − intensity·850) / difficultyMult / SpawnRateMult` ms (min 280; ÷0.12 while slow-mo) |
| Max concurrent | `round((4 + intensity·7)·√difficultyMult)` |
| Bubble size | random in the variant band, nudged up by intensity; ×0.75 global field shrink (giants ×0.70 further); Breast Enlargement swells back up |
| Live fuse | `baseFuse · (1 − intensity·0.25) · FuseTimeMult` (min 1200 ms) |
| Field speed | ×1.25 global (`CHAOS_SPEED_MULT`); 30% of vertical spawns become side-drifters after the first 5 |

**Loops/Depths.** `WaveIndex` advances on `elapsed / (duration / waveCount)`; HUD shows
`DEPTH {roman} · LOOP {i}/{n}` (a Depth = 5 loops). Defaults: 180s · 5 loops (setup: 60–900s, 1–12 loops).

**Difficulties** (pills, rank-clamped): **Gentle** 1.0 (always) · **Teasing** 1.3 (Tempted) ·
**Relentless** 1.7 (Entranced) · **Inescapable** 2.2 (own `extreme_tier`: Devoted rank + its lesson + ✦350).

---

## 2. The verbs — focus & hold-to-defuse (`ChaosTuning`)

**Focus** is the defuse fuel: max 100, start 50, **no passive regen**.

| Source | Focus |
|--------|-------|
| Treat pop | +10 |
| Golden bubble | +12 |
| Gold Digger droplet | +4 |
| Heart catch | +10 (on top of +1 resistance) |
| White rabbit (darter) catch | +15 |
| Prism popped | +10 |
| Heavy giant | +15 |
| Tease denied | +10 |

**Defuse channel:** hold mouse over a live bubble **1000 ms** (fuse pauses; bubble shrinks
to 0.55×). Costs **30 focus on completion** (Bound halves: 15 each). Press+release under
180 ms reads as a click — and a click **triggers** the bubble. Below 30 focus the HUD bar
dims and pulses ("don't touch lives"); 8s of low-focus-with-lives-on-screen fires a
once-per-run warning bark.

**Fuse ring phases:** yellow = burning · yellow↔red flash from 2400 ms left · **solid red
through the final 800 ms** — the brink window Last Breath / Slowburner capstone judge against.

**The Ripple (right-click, 2026-06-12):** base kit — one charge on a **15 s gather**. A
right-click near the bubbles (within wave reach + 80 px of any chaos bubble; clicks farther
out pass through to the desktop, so context menus keep working mid-run) sends an expanding
wave from the cursor: **treats pop paid, trances snap clean, rabbits get FLUNG** away from
the cast point at full sting pace — and a flung rabbit is marked spanked, so its body mows
bubbles for the rest of its life (no Spanker needed, no rabbit_caller lesson tick). The
Tease, the Brittle and freeze pickups stay cursor-only; frozen fields refuse the cast (they
are already a free-pop window). Unworn wave: 260 px over 520 ms. The **Skipping Stone**
charm (Utility, ✦220 + 380/650/950) gathers in 13/11/9/8 s by level and each level adds
+45 px / +110 ms (wider, slower, longer on screen); the capstone skips the stone — **three
waves a second apart per cast**. Input is a run-scoped low-level mouse hook
(`GlobalMouseHook`) whose swallow decision reads only `BubbleService.ChaosBubbleCentersSnapshot`.

---

## 3. Scoring & the multiplier stack

**Base points:** `BasePoints(strength) = 40 + strength·1.6` → 40–200 (strength keys off
the *classic* unscaled size, so visual shrink never weakens payloads or pay).

| Action | Award |
|--------|-------|
| Treat pop | `Base · BenignBaseline(0.40; Golden Touch charm 0.45–0.60) · PayMult · Pendulum · TotalMult` |
| Defuse | `Base · 1.0 · LastBreath · Slowburner-capstone · Pendulum · TotalMult` |
| Darter catch | `(120 + 90 if quick) · TotalMult` |
| Freeze catch | `140 · TotalMult` |
| Prism pop | `Base · 10 · TotalMult` |
| Tease denied | `120 · TotalMult` |

**TotalMult = BaseMult × ComboMult × DifficultyMult × HeatMult × BoonMult × UrgeMult × (Double-loop ? ×2 : 1)**

| Factor | Value |
|--------|-------|
| BaseMult | 1.0 (Golden Touch charm: 1.10/1.20/1.30/1.45) |
| ComboMult | `min(1 + streak·0.08, 6.0)` |
| DifficultyMult | Gentle 1.0 · Teasing 1.3 · Relentless 1.7 · Inescapable 2.2 |
| HeatMult | `1 + heat` (up to ×2) |
| BoonMult | `1 + Σ(mantra RunMultBonus) + Surrender's per-sin bonus` |
| UrgeMult | ×3 while "The urge" sin holds |

**Streak** +1 per pop/defuse/catch; **halves** when a treat fades unpopped or you touch
The Tease; **resets to 0** on an unshielded detonation (the Collar charm can hold it).
Milestone fanfare every 10; big announcements at edge-detected high thresholds.

**Heat** (0–1): +0.04 treat, +0.07 defuse, +0.05 darter/freeze/prism; −0.2 on an absorbed
detonation, **→0** on an unshielded one; decay −0.0015/tick.

---

## 4. Resistance & detonation

- **Base resistance is ZERO.** The *"It would never work on me..."* charm (+1/+2/+3) is the
  only way to descend wearing any. Shown as ♥/♡ against the start-of-run cap.
- A **detonation** (fuse-out, click, early release, no-focus touch):
  - **Absorbed** (resistance available): −1 resistance, heat −0.2, streak kept.
  - **Collar save** (charm, 1/2/3 per descent): payload still plays, streak held.
  - **Bare**: streak & heat → 0, payload fires full strength.
- **Slow Recovery** charm: every 60/50/40/30 pops knits +1 resistance back (capped at start value).
- **Skipping a mantra draft** grants **+1 resistance** (revealed from run 3).

---

## 5. In-run power-ups

| Power-up | Trigger | Effect |
|----------|---------|--------|
| **Slow-mo** | catch a **white rabbit** (darter: 72–96px, 9 DIP/frame, 3 bounces then flees; roll `0.05+0.12·intensity` per tick) | time scale **0.12× for 6s**; payload durations ×(1/0.12); refreshes on re-catch |
| **Freeze** | catch a **Freeze** bubble ❄ | field holds **3.5s**: motion+fuses paused, spawns halt, bubbles stay poppable; channels finished while frozen cost no focus (Freeze Trigger toy) |
| **Pendulum** | trained habit; once per loop at a random beat | 2.5s slow-mo; the *Focus here...* mantra turns the swing into a ×3 pay window |

---

## 6. Bubble variants & field entities

Core table (`ChaosBubbleVariants.All` — weight · min-intensity · fuse):

| Variant | Kind | Size band | Motion | Payload | W · MinInt · Fuse(ms) |
|---------|------|-----------|--------|---------|------------------------|
| **Flash** | treat | 150–210 | FloatUp | flash burst | 3.0 · 0.00 · — |
| **Subliminal** ♥ | treat | 170–220 | FloatUp | subliminal flash | 3.0 · 0.00 · — |
| **Pink Filter** ◑ | live | 180–240 | RainDown | pink_filter overlay | 2.0 · 0.10 · 3500–5000 |
| **Spiral** ◎ | live | 180–240 | RoamBounce | spiral overlay | 2.0 · 0.15 · 3500–5000 |
| **BrainDrain** ☁ | live | 240–320 | RoamBounce | mind-mist wash | 1.4 · 0.25 · 4500–6500 |
| **Freeze** ❄ | pickup | 190–250 | FloatUp | freeze hold | 1.0 · 0.15 · — |
| **Video** ▶ | live, rare | 240–300 | RainDown | mandatory video | 0.5 · 0.50 · 5000–7000 (Entranced reveal) |
| **Gif Rain** ▼ (`htlink`) | live, rare | 200–280 | FloatUp | gif cascade | 0.45 · 0.60 · 4500–6500 (Entranced reveal) |

Special entities (built outside the weighted table):

| Entity | Source | Behaviour |
|--------|--------|-----------|
| **White rabbit** (darter) | intensity roll / Rabbit Caller | catch = slow-mo + 120(+90) pts; The Spanker turns catches into smacks |
| **Golden** 🍀 | 0.5% of spawns (Rabbit's Foot 1.0–2.0%) | benign, ×2.8 speed, pays 10–20 gold base (charm-scaled to 20–40) on the spot; fading halves streak |
| **Heart** 💖 | Pop-up Notification habit, ≤1/loop @60% | catch = +1 resistance, +10 focus; missing costs nothing |
| **Gold droplet** ✧ | Gold Digger mantra | 3 per golden burst, fall fast, 3–7 gold each |
| **Heavy giant** | Heavy Drop mantra, every 10th spawn | ×1.55 size, half speed, 9s life, pays ×3 |
| **Prism** ❂ | *bright colors* sin, 5%/spawn | mimics another bubble; pops ×10 + fires the copied effect (video excluded) |
| **Sweeper rabbits** | *GG make more GG* mantra, 15% of pops | 3 uncatchable rabbits, born spanked, mow what they cross |

**Behavioral bubbles** (riders on ordinary spawn slots; first encounter debuts alone with
×1.5 trance). Gating is by RANK, not difficulty (2026-06-12 — the old hard Gentle return
left default-settings players with permanent ??? diary rows): **Echo + Chaperone + Brittle**
from Tempted; **Tease** from Slipping; **Bound** from Entranced (or any Relentless+
descent). Gentle halves every roll instead of forbidding the menagerie.

| Bubble | Roll | Behaviour |
|--------|------|-----------|
| **The Echo** ◌ | 5% | triggering it splits into 2 children (0.6× size, 1.5× speed, 2.5–3s fuse, never re-split); a held defuse is clean |
| **The Chaperone** | 4% | a live shielded by an orbiting escort treat — pop the escort first |
| **The Tease** ✖ | 3% | any mouse-down triggers it AND halves streak; ignored 6s = DENIED: 120 pts + 5–10 gold + 10 focus; immune to toys/chains |
| **The Brittle** ◇ | 3.5% | glass mine carrying a random LIVE effect — hovering shatters it; immune to toys/sweeps; safe while frozen; 900ms arm grace |
| **The Bound** | 3% | two tethered lives, 15 focus each; second defuse within 2.5s of the first or the survivor enrages (half fuse, ×1.4 speed) |

---

## 7. Mantra draft (between loops)

Draft of **3** cards (→**4** with the *4-Mantra Draft* habit; clamp 2–4). One dedicated
**sin slot** rolls at `SinChance`: 0 before run 3, debuts at 25%, ramps linearly to 50% by
run 10 (Slipping); the user's Allow-Curses toggle clamps on top; Surrender's capstone
**guarantees** the sin slot. Cards reveal one at a time; **skip = +1 resistance**;
unattended drafts auto-skip after 15s. Taking Chances grants 1/2/3 **rerolls** per descent.
`Unique` cards never re-deal once taken. **Partner-gated cards** (gold frame) deal as soon
as the named lifetime boon/habit is equipped — no rank gate on top (dropped 2026-06-12: the
old Entranced wall meant the duo demo teased a card at purchase, then revoked it for ~10 runs).

### Mantras
| Mantra | Rarity | Gate | Effect |
|--------|--------|------|--------|
| **Left Brain** | Common | — | +2 resistance now |
| **Snap Chain** | Uncommon | — | 0.9s grace after every defuse · +0.10× |
| **Golden Touch** | Uncommon | — | +0.15× run multiplier |
| **Welcome Shower** | Common | — | every loop opens with 6 treats raining |
| **Heavy Drop** | Common | — | every 10th bubble is a ×3-pay giant |
| **Gold Digger** | Uncommon | — | golden bubbles burst into 3 droplets (3–7 gold each) |
| **Size Queen** | Uncommon | — | every defuse emits a 430px treat-popping ring |
| **Aftermath** | Uncommon | — | last-1.5s defuses leave a 2s/170px pop-zone |
| **GG make more GG** | Rare | — | 15% of pops birth 3 sweeper rabbits |
| **Focus here...** | Uncommon | `pendulum_swing` habit | pendulum swing pays ×3 |

### Partner duos (gold frame)
| Card | Partner(s) | Effect |
|------|-----------|--------|
| **Overload** | E-Stim | double charges per press (6/8/10) |
| **Afterglow** | VibePopping | buzz lingers 2.5s after it ends |
| **Casting Couch** | Porn DVD | logo splits on first two bounces (1→2→4) |
| **Tail-Plug** | Rabbit Caller / The Pull / The Spanker | rabbits drag a 2s popping trail (46px) |
| **Unleashed** | Collar | each collar save snaps every live on screen for full pay |
| **Electrified Rabbits** | The Spanker **+** E-Stim | spanked-rabbit kills arc lightning to ≤3 bubbles in 620px |
| **Body Buzz** | Poppers **+** E-Stim | 1 pop in 8 fires a 440px shockwave arcing into ≤8 bubbles |

### Sins (risk/reward; Surrender adds +0.05/0.10/0.15× per accepted sin)
| Sin | Effect |
|-----|--------|
| **Hair Trigger** | fuses −25% · +0.40× |
| **Playing with fire** | payload durations ×1.5 · last-second defuses tip 5–9 gold · +0.15× |
| **Look at the bright colors...** | 5% prism spawns (×10 pay, copied effect fires) |
| **Cam Girl** | bubbles flee the cursor (1.6) · 25% of pops tip 2–4 gold · +0.40× |
| **The urge** | everything pays ×3 for the rest of the descent · toys disabled |
| **Relapse** (`double_or_nothing`) | 60% chance the descent runs +1 loop; that loop pays double gold & drops |

Sins carry an `ApplyShielded` half: when Surrender's capstone waives the first sin's
drawback, only the sweet half lands — at reduced sweetness where the full reward would
dominate (The urge shields to ×2, not ×3; bright colors' shielded prisms only mimic
treat looks; Relapse's extra loop becomes certain).

---

## 8. Meta-progression (`chaos_meta.json`)

### Two currencies
- **Drops** ✦ (`Sparks` in code): banked at run end —
  `round((1.5·√score + 35·diff·min(1, durMin/3))·SparkGainMult) + DripFeed trickle`
  (+10% with Drip Feed capstone; +25 one-time *first fall*). Spent in the **Toybox**
  (unlocks, levels, habits). The √ compresses the late-game multiplier explosion and
  self-normalizes duration; difficulty is NOT re-applied to the score part (it already
  rides every pop via TotalMult) — the sub-3-minute scaling on the flat bonus stops
  short-run farming. Tuned so the full collection (~32.6k ✦) lands around 100 descents
  (≈ Claimed): ~200 ✦/run fresh on Gentle → ~650 ✦/run deep on Inescapable.
- **Gold** 🪙: instant in-run pickups (golden bubbles, droplets, tease denials, cam-girl
  tips, last-second tips) **plus a loop-clear tip** — every loop boundary reached banks
  `3–6 · diff` gold, **doubled when the loop had zero detonations** (the final loop's tip
  pays at run end, full-course descents only). Banked immediately; doubled during the
  Relapse loop. Spent only at **her bench**.

### XP (main progression)
`baseXp = min(score, 250 · durationMinutes · difficultyMult)`; skill-tree multiplier
applied at payout (shown on the recap).

### Rank spine (lifetime completed descents) — `ChaosRanks`
| Rank | Descents | Unlocks at this rank |
|------|----------|----------------------|
| Curious | 0 | — |
| Tempted | 3 | Teasing pill |
| Slipping | 10 | Looking Glass tab; The Tease spawns |
| Entranced | 25 | Relentless pill; video + gif-rain variants; The Bound on any difficulty |
| Devoted | 50 | capstone levels; extreme_tier buyable; bench pocket #2 rows |
| Claimed | 100 | (title) |

### Reveal framework (`RevealService`)
Surfaces start hidden; predicates flip → pending → flash once on the next Warren open →
seen. Gates **clamp** user settings, never overwrite them. Highlights: dollhouse at 1 run;
her-corner stub at run 2; draft-skip at run 3; pills/variants per the rank table above.

### First times (one-time drops bonuses)
first taste +5 · first snap +10 · first whisper +10 · first yes +15 · first play +15 ·
first fall +25.

### Lessons (`ChaosLessons`)
Unlock (level 1) of a toy/accessory/habit needs drops **and** its lesson; deeper levels
drops only; **capstones need Devoted**. `e_stim` + `the_spanker` are lessonless (scripted
firsts). Examples: VibePopping "pop 10 treats inside 5s" · Snap Field "defuse 5 in one
loop" · Surrender "accept 5 sins" · draft4 "take 15 mantras" · extreme_tier "finish 10
relentless descents".

---

## 9. The Warren (hub) — shelves

### Toys (active skills; pocket-slotted, fire on keybind/HUD button)
| Toy | ✦ L1 (→max) | Levels | Capstone |
|-----|------------|--------|----------|
| **VibePopping** | 200 (→600) | 3/4/5/5s buzz, sweep-pop · 20s cd | hover alone pops |
| **Freeze Trigger** | 250 (→900) | 1/2/3/3 uses, freeze 3.5s; frozen channels cost no focus | each freeze snaps every live |
| **Porn DVD** | 300 (→1000) | 10/15/20/20s bouncing logo, pops/snaps everything | two screens |
| **Snap Field** | 300 (→600) | snap every live on screen · 60/45/30s cd | clears EVERYTHING, fully paid (treats fire payloads, lives snap) |
| **Rabbit Caller** | 250 (→550) | 1/2/3 rabbits at your click · 45s cd | +8 rabbits over 10s |
| **E-Stim** | 300 (→650) | 3/4/5 charged clicks, arcs to ≤3 in 600px · 30s cd | chains onward |

### Accessories (passives; pocket-slotted)
| Accessory | ✦ L1 | Levels |
|-----------|------|--------|
| **Surrender** | 150 | +0.05/0.10/0.15× per accepted sin · capstone: sin every draft, +1 resistance per yes, first sin stingless |
| **Poppers** (`chain_reaction`) | 150 | pop bursts ripple ×1.20–2.00 reach (5 lvls) |
| **Blindfold** | 300 | bubbles at 40/32/25% opacity · pay ×1.5/1.75/2.0 · capstone: brink heartbeat |
| **Last Breath** | 250 | brink defuse (0.4/0.6/0.8s) pays ×5/×10/×20 |
| **Taking Chances** | 250 | every pop ×2-or-×0.5 coin (50/55/60% double) · 1/2/3 draft rerolls |
| **The Pull** | 200 | bubbles drift to cursor 0.12–0.58 (5 lvls); rabbits home |
| **The Spanker** | 300 | rabbits smacked not caught; swell ×1.20/1.45/1.70, +18% speed/smack · capstone: smack bouncing texts |
| **Intrusive Thoughts** | 250 | every 5s a thought races 3/4/5s, popping what it touches · capstone: splits on rabbits (max 8, +2s) |

### Charms (Utility; always-on, uncapped pockets)
| Charm | ✦ L1 | Levels |
|-------|------|--------|
| **Rabbit's Foot** | 200 | goldens on 1.0/1.5/2.0/2.0% of spawns, pay 12–24 → 20–40 gold |
| **Drip Feed** | 250 | +1/2/3/4 drops per pop, capped 60/90/120/150 ✦/descent, banked at surface · capstone +10% on the haul |
| **Blank Eyes** | 120 | floats every pop's true payout (1 lvl, QoL) |
| **Breast Enlargement** | 120 | bubbles +5/10/15/25% size (pay unchanged) |
| **Slow Recovery** | 200 | every 60/50/40/30 pops regrows +1 resistance |
| **It would never work on me...** | 100 | start with +1/+2/+3 resistance (base is zero) |
| **Collar** | 200 | 1/2/3 streak saves per descent |
| **Golden Touch** | 150 | BaseMult ×1.10–1.45 + calm-pop baseline 45–60% |
| **Slowburner** | 150 | fuses +10/20/30/40% longer · capstone: final-1.5s defuse ×3 |
| **Pocket Watch** | 150 | loop countdown + descent clock (QoL) |

### Habits (always-on trained upgrades; toggleable)
| Habit | ✦ | Effect |
|-------|---|--------|
| **Slower Trance** (`slow_fuses`) | 120 | fuses ×1.15 |
| **Silk Touch** | 180 | hitboxes ×1.25 + near-miss defuse |
| **Pop-up Notification** | 160 | ≤1 heart/loop @60% (+1 resistance, +10 focus) |
| **Pendulum** (`pendulum_swing`) | 220 | once/loop: 2.5s slow-mo (×3 pay with *Focus here...*) |
| **4-Mantra Draft** (`draft4`) | 200 | drafts offer 4 |
| **Inescapable Tier** (`extreme_tier`) | 350 | unlocks Inescapable (Devoted + lesson) |

### Her bench (gold shop)
| Item | 🪙 | Note |
|------|----|------|
| Toy pocket #1 | 50 | reveals the Toys shelf; one-time gift covers a short balance |
| Accessory pocket #1 | 150 | reveals the Accessories shelf |
| Starting mantra | 200 | equip one mantra to auto-apply before loop 1 |
| Diary | 150 | codex of everything met |
| Stats panel | 100 | the numbers |
| Toy pocket #2 | 2000 | Devoted |
| Accessory pocket #2 | 2500 | Devoted |

Pockets start at **zero** — the bench sews them. Fresh unlocks auto-equip only if a pocket
is free.

---

## 10. Happy path (scripted onboarding — `ChaosHappyPath`)

- **Run 1** (RunsCompleted == 0): forced naked config — Gentle, 180s/5 loops, treats only,
  no drafts/darters/sins, 0.6× spawn air. Beats at run-progress: lone threat @0.30 (×3
  trance, hold-to-defuse classroom), scripted 3-card draft @0.55 from a starter pool,
  darter @0.88; streak teach at ×3.
- **Run 2**: braindrain debut @0.25, guaranteed golden @0.50; her-corner stub appears.
- **Run 3**: sins debut (25% slot); draft-skip revealed.
- **Run 4-ish**: first-sin rig (drawback waived once) + duo demo; the_spanker is the only
  buyable accessory until owned (the duo-demo anchor).
- Behavioral bubbles each debut alone with a ×1.5 gentler trance (per-bubble Seen flags).
- One-time focus tip fires before the harsher no-focus lesson can land.

---

## 11. Per-run setup knobs (setup window / `AppSettings`)

Difficulty (rank-clamped) · Duration 60–900s · Loop count 1–12 · Motion override
(Mixed/Float/Rain/Roam) · Enabled variants (video/htlink rank-clamped) · Screen shake +
intensity · Colour flashes · Effect intensity 0.2–1.5 · Mantra draft on/off · Allow sins ·
Darters on/off · Ambient mode (remaps video/gif-rain detonations to lighter payloads).
Trained habits layer on top via `ChaosMeta.ApplyTo`; equipped lifetime boons via
`ChaosMeta.ApplyLifetimeBoons`.

---

## 12. File map

> Some of these files no longer sit next to this document: `ChaosTuning.cs`, `ChaosMetaState.cs`,
> `ChaosCraftingIds.cs` and `ChaosNarrativeModels.cs` moved to `CCP.Core/Services/Chaos/` (the
> platform-agnostic `net8.0` library shared with the future Linux/VR heads). Namespace and
> behaviour are unchanged - `ConditioningControlPanel.Services.Chaos` either way - so nothing
> below changes for a caller; only the folder you edit them in does.

| File | Role |
|------|------|
| `ChaosModeService.cs` | Run lifecycle: timers, scoring, waves, drafts, payout, power-ups, pendulum, gold |
| `ChaosModels.cs` | `ChaosRunConfig` / `ChaosRunState` (HUD-bound) / `ChaosBoon` + the mantra/sin pool |
| `ChaosTuning.cs` | Focus / defuse-channel / behavioral-bubble constants (the one tuning block) |
| `ChaosBubbleVariants.cs` | Variant table + weighted picker + every special-entity builder |
| `ChaosLifetimeBoons.cs` | Toys / Accessories / Charms catalogue (leveled, capstoned) |
| `ChaosUpgrades.cs` | Habits catalogue + `ChaosMeta` facade (purchases, pockets, payouts, refund scrub) |
| `ChaosMetaState.cs` / `ChaosMetaStore.cs` | Persistent save model + JSON store |
| `ChaosRanks.cs` | Rank spine + locked-copy strings + currency glyphs |
| `ChaosRevealService.cs` | Reveal framework (hidden → pending → flash → seen) + bench ids |
| `ChaosLessons.cs` | Lesson gates + first-times bonuses |
| `ChaosHappyPath.cs` | Scripted first descents |
| `ChaosLessonHooks.cs` | In-run lesson progress hooks (brink windows etc.) |
| `EffectPayload.cs` / `EffectPayloadFactory.cs` | Payload behaviours (flash/overlay/video/gif-cascade/…) |
| `ChaosSfx.cs` / `ChaosArt.cs` / `ChaosTips.cs` / `ChaosBubbleHints.cs` | Juice, sprites, her voice, verb hints |
| `ChaosHubWindow` (+ `.Bench/.Lessons/.Reveals/.Debug`) | The Warren: shelves, bench, diary, loadout |
| `ChaosHudWindow` · `ChaosOverlayWindow` · `ChaosToyButtonWindow` | HUD strip · countdown/draft/recap · toy hero button |
| `BubbleService.cs` | Renders + animates every bubble (chaos extends the ambient pop game) |
