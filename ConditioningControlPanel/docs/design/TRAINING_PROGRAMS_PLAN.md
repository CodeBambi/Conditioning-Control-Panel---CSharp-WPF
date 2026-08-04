# Training Programs — Design Plan

> **Status:** proposal, nothing built. Written 2026-07-29 against v6.5.2.
> Scope: multi-day (7/14/28) guided programs as a retention layer above Sessions & Presets,
> free + Patreon-exclusive tiers, and a way to make progress *visible to other people*.

---

## 0. TL;DR — what I'd change about the brief

| Your proposal | Verdict | My tweak |
|---|---|---|
| 7 / 14 / 28 day lengths | Keep | Make **every** program a stack of 7-day **Chapters**. 7=1, 14=2, 28=4. Uniform boss-day cadence, tractable authoring, and a user who quits at day 16 still banked 2 chapters instead of "failing". |
| Progressively harder sessions | Keep, but not by hand | 28 hand-authored `.session.json` per program is the project killer. Days reference **one session template + an intensity scalar + sparse overrides**. Author 4 templates, get 28 days. |
| Use as many CCP features as possible | Keep, with a cap | Breadth per *program*, not per *day*. A day that demands 5 subsystems gets skipped. 1 session + 1 task + (late program) 1 ambient layer. |
| Premium = exclusives, not Lab | Agree | Exclusives set is exactly: Blink Trainer, Remote Control, Bambi Takeover, She's Listening, Graded Intake, Haptics, Awareness, Lockdown. Lab (Chaos, Bureau, DTRH) stays out — it's too stateful/long to be a daily obligation. |
| 2 daily sessions in the last stretch | **Replace this** | 2×45min on day 25 breaks completion. Escalate to **one longer session + an all-day passive layer** (Takeover armed, keyword triggers live, corner GIF). Same escalation feeling, no extra sitting time, and far more on-theme — the takeover *leaves* the session and enters your day. |
| Daily task (blink set / video / Deeper / Intake / remote / lockdown) | Keep — and you already own half of it | Split into **auto-verified** (app watches) and **ritual** (self-attested + photo). The ritual half **already exists**: `RoadmapStepDefinition` has `Objective` + `PhotoRequirement` + a diary folder + boss steps. Consume it, don't rebuild it. |
| Discord webhook posting progress | Right instinct, wrong cadence | Post **cards, not lines**, on **milestones, not days**. Daily posts kill the channel inside a week. `CardExporter` already renders share PNGs. |
| Leaderboard of "most dutiful" | Keep, but fix the metric | Rank on **consistency**, not volume, or it collapses into the existing XP leaderboard. |
| — (missing) | **Add** | The **enrollment contract**. This is the #1 appeal driver in the reference program and it's absent from the brief. |
| — (missing) | **Add → decided** | The **miss policy**: one day off per program, second absence restarts from day 1. Strict track = zero days off. |
| — (missing) | **Add** | A **Discord graduate role**. Beats a leaderboard row: it's visible on every message they ever send. |

---

## 1. Why the 20-Day Bambi Takeover works (and what we can do that it can't)

The reference plan is a PDF of playlists. Three phases, ~1–2h of audio a day (induction →
conditioning → reinforcement), new trigger phrases introduced per stage, and a named end state.
That's it. No app, no verification, no reward. It still gets run over and over. Why:

1. **It removes the choice.** You don't decide what to do today; the plan decides. In this niche the
   abdication of decision-making *is* the product. The plan is the domme.
2. **Pre-commitment.** On day 1 you agree to what day 18 asks. Day 18 is then not a new decision —
   it's a promise you already made. Escalation feels consensual instead of pushy.
3. **Named phases, not numbered days.** You're not "on day 9", you're "in Uniform Lock". Identity
   labels beat progress bars.
4. **Ritual through repetition.** Same induction file every day. The sameness is the mechanism; the
   ritual becomes the trigger. (Note this directly contradicts "keep it varied so it stays fresh" —
   resist that instinct.)
5. **A finite, named finish line.** "Takeover" is a promised end *state*. Twenty days and you are
   something you weren't.
6. **Missing a day costs something.** Some variants restart the phase. That's what makes a streak
   feel precious rather than decorative.
7. **It's a shareable status.** "Day 12" is a thing you can say to people. This niche runs on being
   seen doing it — which is exactly the read behind your point 3.

**Our unfair advantage: verification.** The PDF is pure honor system. CCP *knows* whether you sat
through the session, whether you finished the Intake run, whether you actually blinked. Nobody else
in this space can say "the program watched you do it." That single line is the marketing hook and it
should drive the whole design — every day must have a machine-checkable completion condition.

Second advantage: **mechanical escalation.** The audio program escalates narratively. Ours escalates
in real numbers — flash rate, opacity, spiral, duration, ambient exposure — on a curve.

Third: **it can react.** Missed a day, hit panic, alt-tabbed out of Gamer Girl — the program can
respond in-fiction. A PDF can't.

---

## 2. What already exists (do not rebuild these)

| Existing thing | File | Reuse as |
|---|---|---|
| **Transformation Roadmap** — 3 tracks × 7 steps, objective + photo requirement, boss steps, diary folder, track unlocks, badge | `Models/RoadmapDefinition.cs`, `Services/RoadmapService.cs` | The **ritual-task library** and the entire photo/self-attestation plumbing. This is a program system that just isn't calendar-bound. |
| **Session engine + timeline editor** | `Services/Session/SessionEngine.cs`, `Windows/SessionEditorWindow` | The daily session runtime, unchanged. Programs schedule, they don't re-implement. |
| **Quest tracking fan-out** — `Track*` calls already wired into Flash/Bubble/LockCard/Video/Session/Autonomy/Lockdown/Remote/Keyword/Blink | `Services/Progression/QuestService.cs`, `Models/Quest.cs` | The **auto-verifier**. Every verifier a program task needs already has a call site. Fan out from there; do NOT add a second tracking pass. |
| **`QuestCategory` premium categories** — Autonomy, Lockdown, Remote, KeywordTrigger, BlinkTrainer, each with `RequiresPremium` | `Models/Quest.cs:29-35` | The exclusive-feature task pool, pre-classified. |
| **Streak + shields + oopsie insurance** | `AchievementProgress.UpdateDailyStreak`, `SkillTreeService.UseStreakShield/UseOopsieInsurance` | The grace-token mechanic. Already built, already synced. |
| **Share-card renderer** — 2× PNG, solid backdrop, clipboard/disk hand-off | `Services/CardExporter.cs` | The Discord/X progress card. |
| **Community webhook** — `POST /discord/community-webhook`, auth-token gated, achievement + level-up payloads | `Services/Account/DiscordService.cs:607/663` | Program milestone posts. Add a payload type, don't add an endpoint. |
| **Season leaderboard** — `leaderboard:<YYYY-MM>` ZSET, monthly reset, all-time archive | server `/v2/user/sync`, `LeaderboardService` | Pattern to clone for Devotion. |
| **Catalogue share/import** — `ccp-preset/v1`, `ccp-session/v1` schemas, drag-drop import | `MainWindow.PresetIO.cs`, `SessionFileService` | `ccp-program/v1` — community-authored programs for free. |
| **Bark system** — 3 mods, rule-driven voicelines | `Services/Companion/BarkService.cs`, `bark_rules.json` | The daily nudge. See §6. |
| **Locked content spec** | `docs/locked-content-spec.md` | Program-day rewards that aren't XP. |

**The headline:** Programs ≈ **Roadmap + a clock + a session schedule + a ledger.** The genuinely new
code is the day clock, the verification aggregator, and the ledger. Everything else is composition.

---

## 3. Anatomy of a Program

```
Program  (7/14/28 days, Free | Premium)
└── Chapter × N          "Phase 1 — The Empty Shell"   (7 days each, named, themed)
    ├── Day × 6          session + task
    └── Boss Day         longer session + harder task + a reward that persists
```

### 3.1 A Day

Every day has **exactly three slots**, two of which are usually filled:

| Slot | What | Verification |
|---|---|---|
| **Session** (always) | One CCP session, run to completion. Not stopped early. | `SessionEngine.SessionCompleted` with `completed:true`. Anti-cheat clock (`ElapsedTime`, monotonic Stopwatch cross-check) already guards this. |
| **Task** (always) | Auto-verified *or* ritual | Auto: fan-out off the existing `Track*` call sites. Ritual: photo + note into the roadmap diary. |
| **Ambient** (late program only) | An all-day passive layer — Takeover armed, keyword triggers live, corner GIF, subliminals during normal use | Cumulative minutes of the feature being active, same accounting as the Autonomy quests. |

Hard cap: **no day may demand more than 90 minutes of dedicated seat time.** Escalate via ambient
exposure and intensity, not duration.

### 3.2 Escalation without 28 hand-authored sessions

A `ProgramDay` does **not** carry a full `SessionSettings`. It carries:

```
sessionTemplateId : "prog_orientation_core"     // one of ~4 per program
intensity         : 0.0 → 1.0                   // position on the program's curve
overrides         : { spiralEnabled: true, ... } // sparse, only where the curve isn't enough
```

At run time the engine takes the template's `SessionSettings` and lerps every ramp-capable field
between an authored `floor` and `ceiling` pair by `intensity`. Duration is quantised (30/45/60/75).
Authoring cost drops from 28 files to ~4 templates + a curve + a handful of sparse overrides — and a
program author can *feel* the escalation by dragging one curve instead of editing 28 documents.

This is the single most important architectural decision in the feature. Get it wrong and the
content pipeline becomes the schedule.

### 3.3 Task pool

**Free / non-gated:** complete a Bubble Count at difficulty N · pop N bubbles · N lock cards ·
N minutes of pink filter or spiral · watch N minutes of video · N mantras · a specific flash set ·
a phrase-manager exercise.

**Premium (exclusives only, per your constraint):** Blink Trainer set of N · one Graded Intake run ·
one Deeper-enhanced video · N minutes of Lockdown · take N remote commands (or one remote-controlled
session) · fire N keyword triggers · N minutes Takeover armed · a Haptics-linked session ·
Awareness/gaze drill · She's-Listening voice repetition.

**Ritual (roadmap-style, self-attested + photo):** the existing 21 roadmap steps are day-1 content.
Grooming, posture, uniform, mantra-writing, inspection pose.

Rule of thumb per chapter: 4 auto-verified, 2 ritual, 1 boss (both).

### 3.4 Rewards — XP is not enough

Per day: XP + streak. Per **chapter**: something permanent — a preset, a session, an avatar set, a
companion outfit, a mod, a DTRH boon, a phrase pack, a title. Per **program**: a badge, a Discord
role, and a card. Content unlocks retain far harder than numbers; `docs/locked-content-spec.md`
already describes the machinery.

---

## 4. The two things missing from the brief

### 4.1 The enrollment contract (highest-value addition)

Pre-commitment is the mechanic that makes the reference program work, and it's free to build.
Enrollment is a **ceremony**, not a button:

1. Show the **full arc up front** — chapter names, what escalates, an honest preview of what day 22
   demands. Spoiler-free on specifics (reuse the existing three-step spoiler-reveal pattern from the
   session panel), explicit on *commitment*.
2. Choose **strict or standard** (see 4.2), and **share level** (see §5).
3. Choose a **daily window** — day boundary hour (default 04:00 local, so a 2am session counts for
   the day before) and a target time-of-day for the nudge.
4. **Sign it.** A typed confirmation phrase, in-fiction, in the user's mod voice. This is the moment
   the identity hook lands and it's ~20 lines of XAML.

Once enrolled, the program is the authority: the Today card tells you what today is. No browsing, no
picking. That's the product.

### 4.2 The miss policy — DECIDED

**One absence per program. Program-length independent — 7, 14 or 28, you get exactly one.
The second absence restarts the program from day 1.**

- The allowance is a single **program-wide** token, not per-chapter. Simple to explain, simple to
  show ("1 day off remaining"), and it means the stakes rise as you go rather than resetting each
  week — which is exactly the escalation feeling we want.
- A miss is evaluated at day rollover (`dayBoundaryHour`, default 04:00 local): the day's session
  and task were not both completed.
- `SkillTreeService.UseStreakShield` / `UseOopsieInsurance` already implement the spend-a-token
  mechanic; reuse it rather than adding a parallel one.

**Strict Enrollment** becomes the zero-absence track: no day off at all, first miss restarts.
Distinct badge/flair, and the only track that appears on the public Devotion leaderboard.

**Two things a restart must NOT do**, or a day-26 restart on a 28-day program is a guaranteed
rage-quit and an uninstall:

1. **Keep banked chapter rewards.** Anything permanently unlocked at a chapter boss stays unlocked
   through a restart. You lose your *position*, never your *possessions*.
2. **Count the attempt, and make it a status.** Persist `attemptNumber` on the enrollment and surface
   it in-fiction ("Attempt 3 — she keeps coming back"). A restart re-read as devotion rather than
   failure is the difference between a churn event and a retention event. It should also be
   card-able and leaderboard-visible — repeated attempts on a 28-day are their own flex.

**Re-entry, not shame.** The retention cliff is not day 2 — it's the first day *after* a miss.
Burning the day off should present a reduced **Return Day** ("welcome back — one short session, we
pick up at day 12"), and a restart should open on the enrollment ceremony again rather than a wall of
failure. Always exactly one next action, never a dead end.

---

## 5. Making progress visible (your point 3)

Your webhook instinct is right; the cadence and the unit are wrong.

### 5.1 Three share levels — **default Private** (decided)

- **Private** *(default)* — nothing leaves the machine. Local ledger only.
- **Ledger** — day completions count toward the Devotion leaderboard; no posts.
- **Public** — Ledger + milestone posts to Discord under your display name.

Per-program, changeable mid-run, and never retroactive (flipping to Public doesn't backfill posts).

**Consequence to design around:** a Private default means the Devotion leaderboard and the Discord
feed start empty, and a setting nobody flips is a feature nobody built. Two mitigations, both cheap:

1. **Make it an explicit screen in the enrollment ceremony**, not a buried toggle — three cards,
   in-fiction copy, Private pre-selected but requiring a deliberate tap to move past. An informed
   opt-out beats a silent default in both directions.
2. **Re-ask at graduation.** "Show them what you finished?" with the finished card already rendered
   on screen is a far better ask than a checkbox on day 0, and it's a one-off share that doesn't
   require flipping the whole program public. A user who says no to Ledger on day 1 will often say
   yes to a single card on day 28.

### 5.2 Post cards, on milestones only

Unit: a **PNG card** via `CardExporter` — program name, chapter, `Day 12 / 28`, streak, strict flag,
mod-themed art. Not a line of text.

Cadence: **enrollment · each chapter/boss completion · graduation · (opt-in) a broken streak.**
That's 6 posts across a 28-day program instead of 28. A daily-post channel is muted within a week; a
6-post channel is an event feed.

Extend the existing `POST /discord/community-webhook` with a `program_milestone` payload type —
same auth-token gating as the achievement and level-up posts. No new endpoint.

### 5.3 The Devotion leaderboard — rank consistency, not volume

If the metric is "program days completed" it just re-ranks the same whales as the XP board. Proposal:

```
Devotion(month) = Σ completed program-days
                × 1.25 if the chapter was perfect (no grace spent)
                × 1.5  if strict enrollment
                + graduation bonus per program finished this month
                (no active program for 7d → the month's score stops accruing, it does not decay)
```

Clone the `leaderboard:<YYYY-MM>` ZSET pattern as `devotion:<YYYY-MM>`. Monthly reset matches the
existing season cadence, so a newcomer is never staring at a year-one leaderboard they can't touch.

**Guard:** day advancement must be validated server-side (reject >1 advancement per 20h per user,
require the client to submit the session-completion timestamp) or the JSON gets edited within a
week. Local play stays client-authoritative; only the *public* ledger needs the check.

### 5.4 The strongest identity mechanic isn't the leaderboard

**It's a Discord role.** Graduating grants `Bimbodoll Graduate` / `Class of 2026-08`. A role is
visible on every message that user ever posts; a leaderboard row is visible when someone opens the
leaderboard. The bot pipeline already exists (`POST /admin/announce` → Discord). This is the highest
identity-return-per-line-of-code item in the entire plan.

### 5.5 Cohorts (design for it now, ship it later)

Programs that start on a **fixed date, together**: "August intake — enrollment closes Aug 3, starts
Aug 4," with a Discord thread per cohort. Shared-experience beats individual-ranking in this niche by
a wide margin, and it manufactures a recurring launch moment you can market monthly. Ship in a later
phase, but put `cohortId` on the enrollment record now so it isn't a migration.

---

## 6. Retention mechanics (the actual point of the feature)

- **Today card on the dashboard**, above everything else: *"Day 9 · The Obedient Puppet · 1 session,
  1 task remaining."* Most users should never open the Programs tab after enrolling. The tab is for
  browsing; the card is the product.
- **The nudge should be the companion, not a toast.** The bark system already runs three mod voices.
  "You haven't done day nine yet, princess" in Bambi's voice at the user's chosen hour is worth ten
  Windows notifications. New bark trigger + `/add-barks` — the pipeline exists.
- **Tray + optional Discord DM** as fallback when the app isn't open. (DM requires the bot; scope
  carefully — unsolicited DMs are a fast route to a Discord report. Opt-in only.)
- **Streak visible everywhere** — Today card, tray tooltip, avatar, Discord role tier.
- **Never show a wall of failure.** Lapsed → Return Day. Always exactly one next action.

---

## 7. Free vs Premium

**Free:** one complete, genuinely good **7-day Orientation** program. This is the conversion funnel —
do not cripple it. Local progress, XP, chapter reward, graduation card. Plus the ability to *import*
community programs from the Catalogue.

**Premium adds:** 14 and 28-day programs · exclusive-feature tasks (Blink/Intake/Deeper/Lockdown/
Remote/Takeover/Haptics/Awareness/She's Listening) · strict track + Devotion leaderboard · Discord
role + cohorts · program authoring/export.

**Decide now: what happens to a mid-program user whose pledge lapses.** Recommendation: they keep
their progress and can *finish the program solo* — no leaderboard, no role, no cohort, and premium
tasks swap to free equivalents. Hard-locking someone out on day 19 of 28 generates a refund request
and a Discord post, and the goodwill of letting them finish costs nothing.

---

## 8. Architecture sketch

### New models — `Models/Program/`
- **`ProgramDefinition`** — id, title, icon, lengthDays, tier, `List<ProgramChapter>`,
  `List<SessionTemplate>`, intensity curve, rules (graceTokensPerChapter, strictAvailable),
  rewards, `schemaVersion`. Serialized as `.program.json`, System.Text.Json camelCase — matches the
  existing `.session.json` / `.preset.json` convention so the Catalogue round-trips for free.
- **`ProgramChapter`** — id, name, theme/accent, `List<ProgramDay>` (6 + 1 boss).
- **`ProgramDay`** — dayIndex, title, spoiler-free blurb, `sessionTemplateId` + `intensity` +
  sparse `overrides`, `List<ProgramTask>`, optional `AmbientRequirement`, reward.
- **`ProgramTask`** — kind (AutoVerified | Ritual), `verifier` (enum + target — mirrors
  `QuestCategory` deliberately), or ritual (`objective`, `photoRequirement` — same shape as
  `RoadmapStepDefinition`), `requiresPremium`.
- **`ProgramEnrollment`** — programId, cohortId?, startedAt, dayBoundaryHour, currentDay,
  `Dictionary<int, DayRecord>`, graceTokensRemaining, strictMode, shareLevel, lapse state.
  Persisted to `%LOCALAPPDATA%/…/programs/enrollment.json` (matches `roadmap.json` / `quests.json`).

### New services — `Services/Program/`
- **`ProgramService`** — enrollment, the day clock (rollover at `dayBoundaryHour`, same
  minute-timer + startup-check pattern as `QuestService`), advancement, grace/lapse/re-entry,
  rewards. Events: `DayCompleted`, `ChapterCompleted`, `ProgramGraduated`, `DayMissed`,
  `ProgramLapsed`, `TodayChanged`.
- **`ProgramVerifier`** — **fans out from the existing `QuestService.Track*` call sites.** Do not add
  a second tracking pass over the feature services; that's where the drift and the double-count bugs
  come from.
- **`ProgramFileService`** — `.program.json` I/O + Catalogue share/import (`ccp-program/v1`).

### UI
- `Views/Tabs/ProgramsTabView.xaml` — browse, enroll, arc view, Today panel, ledger/calendar.
- **Dashboard Today card** — the actual surface (see §6).
- Enrollment ceremony window.
- Program Editor — later; fork the `SessionEditorWindow` patterns.

### Server
- `POST /v2/program/enroll` · `POST /v2/program/day-complete` (validated) · `GET /v2/program/state` ·
  `GET /leaderboard/devotion/:season`
- `program_milestone` payload on the existing `/discord/community-webhook`
- Cohort keys + graduate-role grant via the existing bot pipeline

### Known seams to respect
- `SessionManager.LoadAllSessions` runs **once at startup** — a program writing a session file behind
  its back needs `RegisterExternallySavedSession` (#614).
- Session ramps drive overlays **directly**, not through settings (#471) — the intensity lerp must
  write into `SessionSettings` *before* `StartSessionAsync`, never mid-run.
- Sessions run with `bypassLevelCheck:true` and no Patreon gate today — the gate for premium program
  tasks has to live in `ProgramService`, not in the session layer.
- `AchievementService.TrackSessionComplete` matches built-in sessions **by name substring** — program
  sessions must not collide with `"morning drift"`, `"gamer girl"`, `"distant doll"`, `"good girls"`.

---

## 9. Phasing

| Phase | Contents | Proves |
|---|---|---|
| **P0** | Program model + engine + day clock + verifier fan-out + **one free 7-day Orientation** + Today card + local ledger + XP/unlock rewards. No server, no sharing. | The daily loop retains. If it doesn't, nothing downstream matters. |
| **P1** | Premium 14-day + exclusive-feature tasks + grace/strict + enrollment ceremony + bark nudge + `.program.json` share/import. | Premium conversion. |
| **P2** | Server ledger + Devotion leaderboard + milestone cards + **graduate Discord role**. | Identity / visibility. |
| **P3** | Cohorts + 28-day flagship + Program Editor + community programs via Catalogue. | Content scales without you authoring it. |

**Rough cost:** engine + Today card + one program ≈ 2–3 weeks of code. The 28-day flagship is a
**content project, not a code project** — sessions, task copy, chapter art, barks, reward assets.
Budget it as such; that's where this slips.

---

## 10. Risks

1. **Authoring cost is the schedule risk.** Mitigated by the intensity-curve design (§3.2), reusing
   the 21 roadmap steps as ritual content, and shipping the editor so the community authors the long
   tail.
2. **Cheating the ledger.** Local play is client-authoritative and that's fine. The public leaderboard
   is not — validate day advancement server-side (§5.3).
3. **Channel spam.** Milestones only. Six posts, not twenty-eight.
4. **Duty of care.** A system that escalates on a schedule and penalises missed days is by design a
   compulsion loop. It needs: a visible non-shaming **Pause** and **Withdraw**, the 90-minute/day
   cap, an honest arc preview before signing, and no dark-pattern re-engagement (no "you've been
   forgotten" guilt-bait). Worth being deliberate about — it's also what makes it defensible.
5. **Feature sprawl vs Quests.** *(Resolved.)* Programs and daily Quests both say "do a thing today",
   so a **program day auto-satisfies the matching daily quest** wherever `ProgramTask.verifier` and
   `QuestCategory` overlap. The program is strictly the stronger surface; the Quests tab becomes what
   you use *between* programs. Implementation note: this falls out of the shared verifier fan-out
   (§8) almost for free — both listen to the same `Track*` call sites, so the quest ticks anyway.
   The only real work is making the Quests tab *say* so ("satisfied by Day 9") instead of silently
   completing, or users will think one of the two is broken.

---

## 11. Decisions

**Settled 2026-07-29:**

1. **Miss policy** — one day off per program regardless of length; second absence restarts from
   day 1. Strict Enrollment = zero days off. Banked chapter rewards survive a restart; attempts are
   counted and surfaced as status. (§4.2)
2. **Share default** — **Private**, with an explicit choice screen at enrollment and a re-ask at
   graduation. (§5.1)
3. **Placement** — **own top-level nav tab**, with the Today card on the dashboard as the real daily
   surface. Completing a program day **auto-satisfies the matching daily quest** where categories
   overlap, so the user never faces two competing checklists. (§6, §10.5)

**Still open:**

4. **Lapsed-pledge mid-program** — finish solo with premium tasks swapped for free equivalents (rec)
   vs hard lock. (§7)
5. **Cohorts** — P3 as planned, or pull forward as the launch hook for the 28-day flagship. (§5.5)
6. **First program's identity** — what the free 7-day Orientation actually *is* thematically, and
   which three chapter themes the 28-day flagship runs. This is the content brief and it gates P0
   more than any code decision does.

---

## Sources

- [20 Days To Bambi Takeover (plan document)](https://pdfcoffee.com/20-days-to-bambi-takeover-pdf-free.html)
- [20-Day Bambi Takeover Plan, Phase 1](https://www.scribd.com/document/548985957/20DayBambiTakeoverPhase1)
- [Bambi Sleep — Beginner Guide / Safe First Steps](https://www.bambisleep.com/how-to-start)
- [Bambi Sleep Basics — Trance, Repetition, and Persona](https://www.bambisleep.com/bambi-basics)
