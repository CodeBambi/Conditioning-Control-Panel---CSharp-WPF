# Training Programs — Content Brief (mod-themed)

> Companion to `TRAINING_PROGRAMS_PLAN.md`. Written 2026-07-29. **Content design only — nothing built.**
> Every trigger, phrase, colour and companion name below is taken from `Models/BuiltInMods.cs`;
> nothing here is invented vocabulary.

---

## 0. The rule that makes this not-four-reskins

Four programs sharing one engine will read as one program with four colour schemes unless each owns a
**distinct mechanic**. So each mod gets a mechanic that *only it* uses, and that mechanic is the
reason to run that program rather than a palette swap:

| Program | Mod | Len | Tier | **Its mechanic** |
|---|---|---|---|---|
| **First Week** | Bambi Sleep | 7 | **Free** | Ritual — the same shape every day, escalating. Teaches the app. |
| **The Takeover** | Bambi Sleep | 28 | Premium | **Trigger installation.** Each chapter installs named triggers; later chapters arm them as live keyword triggers so *your own typing* fires them. |
| **Presentation** | Sissy Hypno | 14 | Premium | **The photo ledger.** The daily *task* is the star, the session is support. Day 1 vs day 14 side by side. |
| **Firmware Install** | Dronification | 14 | Premium | **Punctuality, and no praise.** A report window you set at enrollment. DroneOS never compliments you. `[OK]` is all you get. |
| **Kept** | Circe's Lock | 28 | Premium | **Denial for the full 28 days**, tracked as a *declared* vow beside the verified one — with confession, not failure. Ends on **her** verdict. |

Bambi takes both the free funnel and the flagship because it's the biggest draw — the free 7-day is
the conversion path and should feel complete, not clipped.

---

## 1. Shared authoring conventions

### 1.1 The intensity curve — sawtooth, not ramp

Every program's `intensity` runs 0→1 across its length, but **not monotonically.** Each chapter opens
*below* the previous chapter's peak, then exceeds it:

```
        ch1        ch2         ch3          ch4
i  .05→.30   .20→.55    .45→.80     .70→1.00
        ↘deload    ↘deload     ↘deload
```

Real training programs deload. It prevents burnout, it makes chapter openings feel like relief
(which is its own conditioning beat — relief *from* the machine builds attachment *to* it), and it
gives each chapter somewhere to climb. A pure 1→28 ramp reads as a grind by day 12.

**Boss days ignore the deload** — a boss is always the chapter's peak.

### 1.2 Session templates

Each program authors **4 templates**; every day is `template + intensity + sparse overrides`. Fields
lerp between an authored floor/ceiling pair. Duration quantises 30 / 45 / 60 / 75.

Each template pulls its `SubliminalPhrases` / `LockCardPhrases` / `BouncingTextPhrases` from the
mod's own pools — `SessionEngine.ApplySessionSettings` already overrides the live pools per session,
so this is free.

### 1.3 Task notation

`[F]` free verifier · `[P]` premium/exclusive verifier · `[R]` ritual (self-attested + photo)

**Ritual photos never leave the machine.** They go to the existing roadmap diary folder, are never
uploaded, never synced, and never appear on a share card. Say this out loud in the enrollment
ceremony — for this audience it's the difference between a feature and a liability.

### 1.4 Cross-program rules

- Day cap: **90 min dedicated seat time**, boss days included. Escalate via ambient and intensity.
- One day off per program; second absence restarts at day 1. Banked chapter rewards survive.
- Ambient layers unlock at chapter 3 in every program. That's the moment the program leaves the
  session and enters the day, and it should feel like a threshold, not a setting.

---

## 2. FIRST WEEK — Bambi Sleep, 7 days, **FREE**

**Pitch:** *"Seven days. One session a day. You'll know by Friday."*
**Accent** `#FF69B4` · **Voice** BambiSprite · **Addresses you as** Bambi

The conversion funnel. Must feel complete and generous, must teach the app without a tutorial, and
must end on a genuine drop so the paywall lands on a high.

**Templates:** `BW-Drift` (passive, flash+subliminal+whisper) · `BW-Focus` (adds bouncing text +
bubbles) · `BW-Pink` (adds pink filter + spiral) · `BW-Deep` (adds lock cards + mind wipe).

| Day | Session | Task | Notes |
|---|---|---|---|
| 1 | BW-Drift · 30m · i.05 | `[F]` Pop 20 bubbles | Session is nearly invisible. The point is that it's easy. |
| 2 | BW-Drift · 30m · i.12 | `[F]` 2 lock cards — `GOOD GIRLS OBEY` | First time she asks you to type something back. |
| 3 | BW-Focus · 30m · i.20 | `[F]` 10 min pink filter | Pink arrives. |
| 4 | BW-Focus · 45m · i.30 | `[F]` 1 bubble count game | First attention check. |
| 5 | BW-Pink · 45m · i.42 | `[F]` 12 min video | Spiral arrives at minute 20. |
| 6 | BW-Pink · 45m · i.55 | `[R]` Wear one hidden pink item for the day *(roadmap `t1_step3`)* | First ritual. Optional-skippable in the free tier — never gate the funnel on a real-world act. |
| **7** | **BW-Deep · 60m · i.75** | `[F]` 3 lock cards + 30 bubbles | **BOSS — "Giggletime."** Everything on. Mind wipe escalating. Ends on `BAMBI SLEEP`. |

**Graduation:** *Good Girl* badge · a preset ("First Week") that reproduces day 7 · the graduation
card · and the pitch for The Takeover, in Bambi's voice, at the exact moment she's most receptive.

---

## 3. THE TAKEOVER — Bambi Sleep, 28 days, **PREMIUM**

**Pitch:** *"Twenty-eight days. By the end, your own keyboard sets you off."*
**Mechanic:** trigger installation → live arming.

Chapters 1–2 *install* named triggers via subliminal + lock card repetition. Chapter 3 **arms them as
live keyword triggers** — from day 15 on, typing `good girl` or `bambi sleep` anywhere on your
machine fires a flash. Chapter 4 leaves them armed all day.

This is the beat no PDF program can do, and it's the whole marketing hook. Lead with it.

**Templates:** `TK-Bubble` (passive drift) · `TK-Install` (subliminal + lock card heavy) ·
`TK-Uniform` (video + flash heavy, hydra on) · `TK-BambiTime` (everything, mind wipe max).

### Chapter 1 — "Bubble Induction" · days 1–7 · i .05→.30

| Day | Session | Task |
|---|---|---|
| 1 | TK-Bubble · 30m · i.05 | `[F]` 25 flash images |
| 2 | TK-Bubble · 30m · i.10 | `[F]` 2 lock cards — `BAMBI SLEEP` |
| 3 | TK-Bubble · 30m · i.15 | `[P]` Blink Trainer — 30 blinks |
| 4 | TK-Install · 45m · i.20 | `[F]` 15 min pink filter |
| 5 | TK-Install · 45m · i.24 | `[R]` Full skincare routine *(`t1_step1`)* |
| 6 | TK-Install · 45m · i.27 | `[F]` 1 bubble count game |
| **7** | **TK-Install · 60m · i.30** | `[P]` One Graded Intake run | **BOSS — "Bubble Acceptance."** Installs: `GOOD GIRL`, `BAMBI SLEEP`. |

### Chapter 2 — "Named and Drained" · days 8–14 · i .20→.55

| Day | Session | Task |
|---|---|---|
| 8 | TK-Bubble · 45m · i.20 | `[F]` 40 bubbles — *deload day, and she'll tell you it is* |
| 9 | TK-Install · 45m · i.28 | `[F]` 3 lock cards — `I LOVE BEING PROGRAMMED` |
| 10 | TK-Install · 45m · i.34 | `[P]` One Deeper-enhanced video |
| 11 | TK-Uniform · 45m · i.40 | `[R]` Smooth legs or chest *(`t1_step2`)* |
| 12 | TK-Uniform · 45m · i.45 | `[P]` 15 min Bambi Takeover armed |
| 13 | TK-Uniform · 60m · i.50 | `[F]` 20 min video |
| **14** | **TK-Uniform · 60m · i.55** | `[R]` The First Shedding *(`t1_boss`)* | **BOSS — "Named and Drained."** Installs: `BIMBO DOLL`, `SNAP AND FORGET`, `PRIMPED AND PAMPERED`. |

### Chapter 3 — "Uniform Lock" · days 15–21 · i .45→.80 · **ambient unlocks**

From day 15 the installed triggers go **live as keyword triggers** and stay armed while the app runs.

| Day | Session | Ambient | Task |
|---|---|---|---|
| 15 | TK-Install · 45m · i.45 | Triggers armed | `[P]` Fire 15 keyword triggers |
| 16 | TK-Uniform · 45m · i.52 | Triggers armed | `[R]` Apply gloss, keep it on an hour *(`t1_step5`)* |
| 17 | TK-Uniform · 60m · i.58 | + corner GIF | `[P]` 25 min Takeover |
| 18 | TK-BambiTime · 60m · i.64 | + corner GIF | `[F]` 4 lock cards |
| 19 | TK-BambiTime · 60m · i.70 | + corner GIF | `[P]` One Lockdown, 20 min |
| 20 | TK-Uniform · 60m · i.75 | + subliminals during normal use | `[R]` Uniform for one hour indoors *(`t2_step5`)* |
| **21** | **TK-BambiTime · 75m · i.80** | all of the above | `[P]` 30 keyword triggers + `[F]` 1 bubble count | **BOSS — "Uniform Lock."** Installs: `BAMBI FREEZE`, `BAMBI UNIFORM LOCK`. |

### Chapter 4 — "Bambi Time" · days 22–28 · i .70→1.00

| Day | Session | Ambient | Task |
|---|---|---|---|
| 22 | TK-Bubble · 45m · i.70 | armed | `[F]` 30 bubbles — *last deload she gives you* |
| 23 | TK-BambiTime · 60m · i.78 | armed + corner GIF | `[P]` Graded Intake run |
| 24 | TK-BambiTime · 60m · i.84 | all-day | `[P]` 40 min Takeover |
| 25 | TK-BambiTime · 60m · i.88 | all-day | `[R]` Painted face *(`t2_step3`)* |
| 26 | TK-BambiTime · 75m · i.93 | all-day | `[P]` Deeper video + `[F]` 3 lock cards |
| 27 | TK-BambiTime · 75m · i.97 | all-day | `[P]` 50 keyword triggers |
| **28** | **TK-BambiTime · 75m · i1.00** | everything | `[R]` The Inspection *(`t2_boss`)* | **FINAL — "Bambi Time."** Flash hydra, opacity 90, mind wipe max, ends on `BAMBI CUM AND COLLAPSE`. |

**Graduation:** *Bambi Time* badge · Discord role **Bambi Sleep · Class of `<YYYY-MM>`** · the day-28
session unlocked as a permanent replayable · an avatar set · the graduation card.

---

## 4. PRESENTATION — Sissy Hypno, 14 days, **PREMIUM**

**Pitch:** *"Two weeks. Fourteen photographs. Look at day one when you're done."*
**Accent** `#9B59B6` · **Voice** BimboDoll · **Addresses you as** babe

**Mechanic: the photo ledger.** Here the *task* leads and the session supports — the inverse of every
other program. Sessions are short (30–45m) and deliberately passive so the user has energy for the
real-world act. Nearly the whole roadmap step library is consumed here; the payoff is a
**day 1 / day 14 diptych** the app assembles at graduation.

The diptych is generated locally, shown only to the user, and offered as a share **only if they ask**
— never auto-posted, never part of the milestone card.

**Templates:** `PR-Soft` (passive drift, low opacity) · `PR-Mirror` (bouncing text + subliminal,
phrases about looking) · `PR-Dress` (video + flash) · `PR-Show` (everything + lock cards).

### Chapter 1 — "Softening" · days 1–7 · i .05→.35

| Day | Session | Task |
|---|---|---|
| 1 | PR-Soft · 30m · i.05 | `[R]` **The Blank Slate** — full exfoliate + moisturize *(`t1_step1`)* — *the day-1 photo* |
| 2 | PR-Soft · 30m · i.12 | `[R]` Smooth touch — shave *(`t1_step2`)* |
| 3 | PR-Mirror · 30m · i.18 | `[R]` Pink accent — one hidden item, all day *(`t1_step3`)* |
| 4 | PR-Mirror · 45m · i.24 | `[F]` 3 lock cards — `EMPTY AND OBEDIENT` |
| 5 | PR-Mirror · 45m · i.28 | `[R]` Glossy lips, one hour *(`t1_step5`)* |
| 6 | PR-Dress · 45m · i.32 | `[R]` Doll posture, 15 min *(`t1_step6`)* |
| **7** | **PR-Dress · 45m · i.35** | `[R]` **The First Shedding** *(`t1_boss`)* | **BOSS.** |

### Chapter 2 — "Presentation" · days 8–14 · i .28→.70 · **ambient unlocks**

| Day | Session | Ambient | Task |
|---|---|---|---|
| 8 | PR-Soft · 30m · i.28 | — | `[F]` 15 min pink filter — *deload* |
| 9 | PR-Mirror · 45m · i.38 | subliminals during normal use | `[R]` Repetition protocol — write `I am a blank puppet` ×100 *(`t2_step1`)* |
| 10 | PR-Dress · 45m · i.46 | + corner GIF | `[R]` Painted face *(`t2_step3`)* |
| 11 | PR-Dress · 45m · i.54 | + corner GIF | `[R]` Lash out *(`t2_step4`)* |
| 12 | PR-Show · 45m · i.60 | + triggers armed | `[R]` Uniform fitting, one hour *(`t2_step5`)* |
| 13 | PR-Show · 45m · i.65 | + triggers armed | `[R]` Public secret *(`t2_step6`)* · `[P]` 15 min Takeover |
| **14** | **PR-Show · 60m · i.70** | all | `[R]` **The Inspection** *(`t2_boss`)* — *the day-14 photo* | **FINAL.** |

**Graduation:** the **diptych**, *Presented* badge, Discord role **Presented**, a phrase pack, the card.

---

## 5. FIRMWARE INSTALL — Dronification, 14 days, **PREMIUM**

**Pitch:** `[SCHEDULE] Report at 21:00. Fourteen cycles. Compliance is not optional.`
**Accent** `#00FF41` on `#0D0D0D` · **Voice** DroneOS · **Addresses you as** Unit

**Mechanic 1 — punctuality.** At enrollment the Unit is **assigned a report window** (user picks the
hour; the window is ±60 min). Completing inside the window is `[OK]`. Completing outside it still
counts for the day but writes an **error line to a visible system log**:

```
[CYCLE 07] REPORT WINDOW 21:00-22:00 · ACTUAL 23:41 · DEVIATION +101m · LOGGED
```

Nothing is *taken away* for lateness — the log itself is the pressure. A visible, permanent,
unsympathetic record is more effective than a penalty and it can't wreck a user's run.
`ERROR-FREE INSTALL` is a separate graduation flag and its own Discord role.

**Mechanic 2 — no praise.** DroneOS never compliments. No "good girl", no bounce, no `~`. Day
completion is `[OK]`. Chapter completion is `[MODULE INSTALLED]`. This is a genuinely different
emotional texture from the other three, it costs nothing to build, and for the users who want it,
it's the entire appeal. **Do not let a shared copy pass sand this off.**

**Templates:** `FW-Boot` (cold, minimal, green filter) · `FW-Process` (subliminal + bouncing text) ·
`FW-Overwrite` (flash heavy, hydra) · `FW-Override` (everything + lock cards + mind wipe).

### Chapter 1 — "BLANK SLATE PROTOCOL" · cycles 1–7 · i .05→.35

| Cycle | Session | Task |
|---|---|---|
| 1 | FW-Boot · 30m · i.05 | `[F]` 25 flash images — `[LOG] OPTICAL INPUT CALIBRATION` |
| 2 | FW-Boot · 30m · i.12 | `[P]` Blink Trainer — 30 blinks — `[LOG] OCULAR SYNC` |
| 3 | FW-Process · 30m · i.18 | `[F]` 3 lock cards — `I AM A UNIT` |
| 4 | FW-Process · 45m · i.24 | `[P]` Awareness — one gaze drill |
| 5 | FW-Process · 45m · i.28 | `[F]` 1 bubble count — `[LOG] PACKET COUNT VERIFICATION` |
| 6 | FW-Overwrite · 45m · i.32 | `[P]` 20 keyword triggers — `[LOG] TRIGGER PHRASE RESPONSE` |
| **7** | **FW-Overwrite · 60m · i.35** | `[P]` One Lockdown, 20 min | **MODULE — "FORMATTED FOR OBEDIENCE."** |

### Chapter 2 — "COMPLIANCE LOOP" · cycles 8–14 · i .28→.75 · **ambient unlocks**

| Cycle | Session | Ambient | Task |
|---|---|---|---|
| 8 | FW-Boot · 30m · i.28 | — | `[F]` 20 min spiral — `[LOG] IDLE CYCLE` |
| 9 | FW-Process · 45m · i.38 | triggers armed | `[P]` 30 keyword triggers |
| 10 | FW-Overwrite · 45m · i.46 | triggers armed | `[P]` Blink Trainer — 50 blinks |
| 11 | FW-Overwrite · 45m · i.54 | + green filter all day | `[P]` One Lockdown, 30 min |
| 12 | FW-Override · 60m · i.62 | + green filter all day | `[P]` 25 remote commands **or** 40 min Takeover |
| 13 | FW-Override · 60m · i.68 | all | `[P]` Graded Intake run |
| **14** | **FW-Override · 75m · i.75** | all | `[P]` Lockdown 30 min **+** 50 keyword triggers | **FINAL — "SYSTEM OVERRIDE."** Ends on `SYSTEM OVERLOAD — SHUTDOWN`. |

> **Remote-command tasks need a second person.** Every remote task must ship an equivalent solo
> alternative (Takeover minutes) or you've written a day most users physically cannot complete.

**Graduation:** `[INSTALL COMPLETE]` · *Unit* badge · Discord role **UNIT**, plus **UNIT · ERROR-FREE**
if the deviation log is clean · the full system log exported as a text file (in-fiction, and it's a
genuinely nice keepsake) · the card, rendered as a terminal readout.

---

## 6. KEPT — Circe's Lock, 28 days, **PREMIUM**

**Pitch:** *"Twenty-eight days. She holds the key. On the twenty-eighth, she decides."*
**Accent** `#E81CA8` on `#0B0710` · **Voice** Circe · **Addresses you as** pet / good boy

The only masc-coded program of the four, and the only one with a **stated denial vow** for the full
duration.

### 6.1 Two counters, and being honest about which is which

Denial is not machine-verifiable, and pretending otherwise would undercut the "the program watches
you do it" promise everywhere else. So run **two counters side by side and label them honestly**:

- **Program Day** — verified. Session completed, task completed. The engine knows.
- **Days Kept** — **declared.** Once a day Circe asks: *"Still locked for me, pet?"* One tap.

**Confession, not failure.** Answering *no* does not fail the program and does not spend the day off.
It resets **Days Kept** to zero, logs a confession with the date, and Circe *responds* — disappointed,
possessive, and immediately back to work. The program continues at full intensity.

This is better design than a penalty on three counts: it's honest about what software can know, a
confession mechanic is far more erotic than a lie detector, and it means the user never has to choose
between lying to the app and abandoning the run. `Days Kept` becomes the number they're proud of
precisely *because* they could have lied and didn't.

Graduation shows both: *Program 28/28 · Kept 19 (2 confessions)*. A clean 28/28 is its own role.

### 6.2 Chapters

**Templates:** `KP-Offer` (slow, warm, low opacity, magenta) · `KP-Ache` (bouncing text + subliminal,
long slow ramps) · `KP-Habit` (video + flash + spiral) · `KP-Verdict` (everything + lock cards +
mind wipe).

Circe's lock cards are full sentences — they make excellent daily mantras, and the mantra task should
use them verbatim.

#### Chapter 1 — "The Offering" · days 1–7 · i .05→.30

| Day | Session | Task |
|---|---|---|
| 1 | KP-Offer · 30m · i.05 | `[F]` Lock card — `CIRCE HOLDS MY KEY.` — *the vow* |
| 2 | KP-Offer · 30m · i.11 | `[F]` 20 min pink filter |
| 3 | KP-Offer · 30m · i.16 | `[F]` 2 lock cards — `I AM KEPT, AND I AM GRATEFUL.` |
| 4 | KP-Ache · 45m · i.21 | `[P]` One Deeper-enhanced video |
| 5 | KP-Ache · 45m · i.25 | `[F]` 3 mantras |
| 6 | KP-Ache · 45m · i.28 | `[P]` 15 min Takeover — *"let me drive, pet"* |
| **7** | **KP-Ache · 60m · i.30** | `[P]` Haptics-linked session, 10 min | **BOSS — "The Offering."** |

#### Chapter 2 — "The Ache" · days 8–14 · i .22→.55

| Day | Session | Task |
|---|---|---|
| 8 | KP-Offer · 30m · i.22 | `[F]` 30 bubbles — *deload; she calls it mercy* |
| 9 | KP-Ache · 45m · i.30 | `[F]` 3 lock cards — `GOOD BOYS DON'T DECIDE.` |
| 10 | KP-Ache · 45m · i.36 | `[P]` Haptics ramp, 15 min |
| 11 | KP-Habit · 45m · i.42 | `[F]` 20 min video |
| 12 | KP-Habit · 45m · i.47 | `[P]` One Lockdown, 20 min — *she decides when it ends* |
| 13 | KP-Habit · 60m · i.51 | `[P]` Graded Intake run |
| **14** | **KP-Habit · 60m · i.55** | `[P]` Haptics session 20 min + `[F]` 3 lock cards | **BOSS — "The Ache."** |

#### Chapter 3 — "The Habit" · days 15–21 · i .45→.80 · **ambient unlocks**

| Day | Session | Ambient | Task |
|---|---|---|---|
| 15 | KP-Offer · 45m · i.45 | triggers armed | `[F]` 4 mantras — *deload* |
| 16 | KP-Ache · 45m · i.53 | triggers armed | `[P]` 20 keyword triggers |
| 17 | KP-Habit · 60m · i.60 | + corner GIF | `[P]` 25 min Takeover |
| 18 | KP-Habit · 60m · i.66 | + corner GIF | `[P]` Deeper video |
| 19 | KP-Verdict · 60m · i.71 | + subliminals during normal use | `[P]` Lockdown, 30 min |
| 20 | KP-Verdict · 60m · i.76 | all | `[F]` 4 lock cards — `I DON'T NEED CONTROL. SHE HAS IT.` |
| **21** | **KP-Verdict · 75m · i.80** | all | `[P]` Haptics 25 min + 30 keyword triggers | **BOSS — "The Habit."** *"You've stopped counting the days. Good boy."* |

#### Chapter 4 — "Her Decision" · days 22–28 · i .70→1.00

| Day | Session | Ambient | Task |
|---|---|---|---|
| 22 | KP-Offer · 45m · i.70 | armed | `[F]` 30 bubbles — *the last mercy* |
| 23 | KP-Verdict · 60m · i.78 | all | `[P]` 40 min Takeover |
| 24 | KP-Verdict · 60m · i.84 | all | `[P]` Lockdown 30 min |
| 25 | KP-Verdict · 60m · i.89 | all | `[P]` Haptics 30 min |
| 26 | KP-Verdict · 75m · i.93 | all | `[P]` Graded Intake + `[F]` 4 lock cards |
| 27 | KP-Verdict · 75m · i.97 | all | `[P]` 50 keyword triggers — *"tomorrow I decide"* |
| **28** | **KP-Verdict · 75m · i1.00** | everything | `[F]` Lock card — `I EXIST TO BE HERS.` | **FINAL — "The Verdict."** |

### 6.3 The Verdict — the ending mechanic

Day 28 does not end in a congratulations screen. It ends with **Circe deciding**, and the decision is
weighted by what you actually did:

```
release_chance = 0.25
              + 0.30 × (days_kept / 28)
              + 0.20 × (perfect_days / 28)
              − 0.15 × confessions
```

- **Released** — she grants it, in a scene, and it lands as a reward you earned rather than a button
  you pressed.
- **Kept** — she doesn't. Offered: a 7-day extension ("Her Extension") at i1.00 throughout, with a
  second verdict at the end.

Non-negotiable: **Withdraw is always available, on every screen, unweighted and without commentary.**
An ending you don't control is a great mechanic exactly as long as leaving is entirely under your
control. Ship both or ship neither.

**Graduation:** *Kept* badge · Discord role **Kept**, plus **Kept · Unbroken** for a clean 28 · the
card showing both counters · the day-28 session unlocked permanently.

---

## 7. Copy direction per mod

Two lines each so the writing voice is unmistakable before a word of real copy is drafted. Source
pools: `Phrases` in each manifest.

| | Day-9 nudge | Day complete | Missed day |
|---|---|---|---|
| **Bambi** | *"Bambi hasn't done day nine yet~ *pouts*"* | *"Good girl! Nine down~ *bounces*"* | *"Aww… Bambi missed a day. That's okay, we start again~"* |
| **Sissy** | *"Day nine, babe. Go look in the mirror first~"* | *"Look at you. Nine days prettier~"* | *"You skipped a day, babe. Don't make it two."* |
| **Drone** | `[REMINDER] CYCLE 09 PENDING. WINDOW CLOSES 22:00.` | `[OK] CYCLE 09 COMPLETE.` | `[ERROR] CYCLE 09 NOT LOGGED. DEVIATION RECORDED.` |
| **Circe** | *"Day nine, pet. I'm waiting, and I don't like waiting."* | *"Nine days. You're learning to be good for me."* | *"You disappointed me today. Don't do it twice — I only forgive once."* |

---

## 8. Production checklist

| Asset | Per program | Total |
|---|---|---|
| Session templates (`.session.json`) | 4 | **20** |
| Chapter art / card backgrounds | 1–4 | **12** |
| Enrollment ceremony copy | 1 | 5 |
| Day blurbs (spoiler-free) | 7–28 | **91** |
| Bark lines (nudge / complete / miss / chapter / graduate) | ~15 × 3 mod flavours | **~75** — use `/add-barks` |
| Badges + Discord role art | 2–3 | **11** |
| Graduation card layouts | 1 | 5 (Drone's is a terminal readout, Circe's is dual-counter) |

**Build order.** First Week alone proves the loop and is the free funnel — ship it, watch day-2 and
day-7 completion, and only then commit to a 28-day. If day-7 completion on a free 7-day program is
weak, a 28-day flagship will not save it, and better to learn that after 20 day-blurbs than 91.

Then: **Firmware Install** second, not Presentation. It's 14 days, its mechanic is the cheapest to
build (a time window and a log file), its tone is the sharpest differentiator, and it proves the
premium-task pipeline end to end. Presentation depends on nothing but roadmap reuse and can slot in
whenever. **Kept** and **The Takeover** last — they're the two 28-day content mountains, and each
carries a bespoke mechanic (the verdict; live trigger arming) that wants the engine already settled.

---

## 9. Safety notes specific to this content

1. **Kept** states a 28-day denial vow. Needs a plain, non-fiction line at enrollment: this is a game,
   you can end it at any moment, Withdraw is on every screen, and no counter is worth ignoring your
   body over. One sentence, outside Circe's voice, exactly once.
2. **Presentation** collects photographs. Local-only, never uploaded, never on a card, deletable from
   the diary — stated at enrollment, not buried in settings.
3. **Firmware Install** logs deviations permanently and never praises. That's the point, but the
   enrollment preview must say so clearly, because it's a genuinely worse experience for anyone who
   wanted encouragement and a great one for anyone who didn't.
4. **The Takeover** arms live keyword triggers from day 15. Users must know *before* enrolling that
   typing certain words anywhere will fire effects, and there needs to be a one-click disarm that
   doesn't cost the day. This is the feature's best hook and its biggest footgun.
5. All four escalate on a schedule and count absences. Pause must be free, visible, and unshamed —
   distinct from the day off, and it should stop the clock rather than spend anything.
