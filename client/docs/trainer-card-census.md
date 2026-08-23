# Trainer Card — census against the shipping WPF source

Evidence scope: the shipping WPF source, re-derived from the committed tree on
2026-08-21. Method: the repeatable inventory rules and source citations stated in this document.

**Verdict: BUILDABLE-IN-PART, and the residue is the headline.** One unit is genuinely buildable and
is named in §6 with its file inventory. Everything the board row actually describes — the wardrobe,
the banners, the achievement gating, the leaderboard privacy dialog — sits behind either an absent
subsystem or an owner decision about what leaves the machine.

**STATUS 2026-08-23 — §6.1'S BUILDABLE UNIT IS BUILT, AND THIS DOCUMENT DID NOT SAY SO.** Commit
`9057cfa5b` landed `client/src/CcpClient.Desktop/Features/Progression/GradedRunAwards.cs` (293 lines,
wired end to end at `client/src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs:553`, 21
facts in `client/tests/CcpClient.Tests/GradedRunAwardsTests.cs`) AFTER this census was written, and
amended nothing here. The string `GradedRunAwards` appeared zero times in this document until this
repair. Corrected below: §3 rows B1, B4, B7, B8, B9; the §3.1 tally; §4's headline and rows C9-C16;
§6.1; §6.2; §8's first bullet. **And the same wave built the surface that renders it** —
`client/src/CcpClient.Desktop/Features/Progression/TrainerCard.cs` on
`client/src/CcpClient.Desktop/Views/Pages/IntakePage.axaml`, which is why B1 moved too. What that
card deliberately does NOT carry is recorded in §6.2 and in its own remarks: no sharing, export,
upload or publish path exists in this build, not even as a disabled control. Unchanged and still
re-derived from the shipping bytes by
`TrainerCardCensusTests` on every run: §1-§2 (the WPF inventory), §5 (the `honor_roll` defect) and §9
(the pins). **The evidence method is unchanged too — every "port anchor" cell below is an opened
`client/src/**` line, and the repair opened them rather than trusting this document's own prose.**

**And the board row's evidence is wrong in a way that a wrong number would not have been.** Four of
its five counts are arithmetically CORRECT (§1.2). The row is still misleading, because its
headline number counts a directory that is **96.5% other people's features** — 109 of its 113 (§1.3; 95.6% of the directory as it stands today, §9.5). A wrong number
gets corrected; a right number that means something else gets trusted.

---

## 1. The universe, and what the board row's evidence actually says

The universe is the tracked repository tree, walked recursively. Enumeration takes a root directory,
excludes only generated-byte patterns, records declined symlinks, and cross-checks each count against
`git ls-files`. Every count below is re-derivable from the committed source tree.

### 1.1 The row makes six claims. All six were tested; none was inherited

The Trainer Card row of `client/docs/task-board.md` (cited without a line: the board is rewritten
every wave, so a line number into it is a citation with an expiry date).

### 1.2 Four of five directory counts are EXACTLY RIGHT — as *delta* counts

The row's numbers are not directory totals. They are **files ADDED by the merge that brought this
work onto the port branch** — `42286638c` (*"Merge branch 'main' into feat/crossplatform"*,
2026-08-12), whose first parent predates the Trainer Card work and whose second parent contains it.

```
git diff --diff-filter=A --name-only 42286638c^1 42286638c -- ConditioningControlPanel/<dir> | wc -l
git ls-files ConditioningControlPanel/<dir>
```

| # | Row claim | Added at the merge | In the directory today | Verdict |
|---|---|---|---|---|
| R1 | `Views/Controls/` (113 new) | **113** | 114 | **number RIGHT, meaning WRONG** — §1.3 |
| R2 | `Resources/cosmetics/` (94) | **95** | 95 | **off by one** — §1.4 |
| R3 | `Resources/banners/` (12) | **12** | 12 | **RIGHT** |
| R4 | `Resources/achievements/` (6) | **6** | **70** | delta right; the directory is 11.7x the delta — §1.5 |
| R5 | `Services/Profile/` (3) | **3** | 3 | **RIGHT** |
| R6 | "wardrobe of **60 adornments**" | — | **79** | **WRONG** — §1.6 |

I did not assume the delta interpretation. The first range I tested was the v6.7.4 sync
(`42286638c..607cda0e6`), which reports **0** added files in all five directories, so the Trainer
Card had already arrived; `42286638c` is the merge that brought it, and there the five numbers land.

### 1.3 THE HEADLINE: `Views/Controls/` "(113 new files)" is 3.5% Trainer Card

Grouping the same 113 added files by subtree:

```
git diff --diff-filter=A --name-only 42286638c^1 42286638c -- ConditioningControlPanel/Views/Controls
```

| Subtree | Files | What it is |
|---|---|---|
| `Companion/` | **87** | The AI companion surface — hero card, room preview, memory diary, workshop accordion, awareness privacy, engine-room drawer. **A different board row.** |
| `AppSettings/` | **16** | Eight settings sections (Account, Audio, Data, Devices, General, Notifications, Performance, Updates). A different surface. |
| `Studio/` | **6** | `BrainDrainFeatureControl`, `RampRackPanel`, `SchedulerRackPanel` — **rack panels for three modules this port has ALREADY SHIPPED** (Brain Drain, Intensity Ramp, Scheduler). |
| root | **4** | `AdornedAvatar.xaml(.cs)`, `ProfilePrivacyPanel.xaml(.cs)` — **the only Trainer Card files in the number.** |

**4 of 113 = 3.5%.** This is the FYP census's `Services/Fyp/Online/` finding at more than double the
severity — that one was 42% foreign, this one is **96.5% foreign** (109 of the 113 added; 109 of 114 = 95.6% of the directory today, §9.5). A packet authored against "port
`Views/Controls/`" would port the AI companion surface, the settings pages, and three rack panels
the port already has.

The 114th file in the directory today is `HapticUiModels.cs`, which predates the merge and is not
part of the 113.

### 1.4 The off-by-one is the only machine-readable file in the tree

`Resources/cosmetics/` gained **95** files, not 94: 91 PNG + 3 `.gitkeep` + **`registry.json`**.
91 + 3 = 94, so the row's number is the byte files with the registry dropped. That single omitted
file is the wardrobe catalog itself — the one file in the tree that is code-adjacent rather than
art, and the one a port must read.

The registry is internally sound, which I checked rather than assumed: **all 79 entries resolve to a
file that exists, and the only 12 unreferenced PNGs are the preset avatars** (a separate catalog).

### 1.5 The achievement directory is 70 files, and the 6 is a trap for a sizer

The delta added 6 badge PNGs (`01887f1dc feat(profile): badge art for the 6 new achievements`).
The directory holds **70**. "Earn-it-to-wear-it" gates wardrobe items on the **whole** achievement
set, not on the six new badges, so a size derived from the 6 is wrong by an order of magnitude.

### 1.6 "60 adornments" is 79

`Resources/cosmetics/registry.json` declares **79 items — 55 `deco` + 24 `charm`**, across four mods
(bambi 19, sissy 20, drone 20, circe 20). It declared 79 at the merge too, so this is not drift.
**63 of the 79 are achievement-gated** (`unlock: "achievement:<id>"`); 16 are `free`.

### 1.7 The row names five directories and MISSES the one the feature lives in

None of the five contains the Trainer Card's own implementation. The anchored sweep
(`walk.mjs . --match "trainer[ _-]?card" --flags gi` → 59 files, 127 lines) found it in
`MainWindow/`, which the row does not mention at all:

| Path | Lines |
|---|---|
| `MainWindow/MainWindow.ProfileBubble.cs` | 702 |
| `MainWindow/MainWindow.ProfileCosmetics.cs` | 531 |
| `MainWindow/MainWindow.ProfileFaucet.cs` | 448 |
| `MainWindow/MainWindow.ProfileVat.cs` | 414 |
| `MainWindow/MainWindow.ProfileCard.cs` | 337 |
| `MainWindow/MainWindow.ProfileFx.cs` | 238 |
| `MainWindow/MainWindow.ProfileSpiral.cs` | 223 |
| `MainWindow/MainWindow.ProfileWardrobe.cs` | 167 |
| | **3060** |

plus `MainWindow.Leaderboard.cs` (1451), `MainWindow.LeaderboardFx.cs` (404),
`MainWindow.AchievementsTab.cs` (832) and `MainWindow.TabFxPresetsQuestsAchievements.cs` (831).

**The Trainer Card is not a directory. It is twelve partial-class members of `MainWindow`**, which
is why a directory-shaped board row could not see it and why no `Views/Controls/` count could ever
have been the right number.

---

## 2. The real inventory

Roots walked recursively; the two counts agree in every tree (no untracked bytes anywhere).

### 2.1 Code

| Group | Files | Lines | Note |
|---|---|---|---|
| `MainWindow/MainWindow.Profile*.cs` | 8 | 3060 | The card itself, the wardrobe host, the vat, the bubble |
| `MainWindow/MainWindow.{Leaderboard,LeaderboardFx,AchievementsTab}.cs` | 3 | 2687 | Leaderboard + achievements tab |
| `MainWindow/MainWindow.TabFxPresetsQuestsAchievements.cs` | 1 | 831 | The twelfth partial. **SHARED** — FX presets, quests AND achievements, so only partly this surface; counted here, never folded into §6.1 |
| `Services/Profile/` | 3 | 968 | `CosmeticsCatalog` 406, `WardrobeCatalog` 431, `WardrobeStageGeometry` 131 |
| `Services/Progression/` | 10 | 6574 | `AchievementService` 1130, `QuestService` 1338, `SkillTreeService` 983, `LeaderboardService` 771, … |
| `Services/GamificationBridge.cs` | 1 | 648 | The consumer this packet's cross-reference is about |
| `Dialogs/Profile{Customize,Privacy}Dialog.*` | 4 | 1135 | Wardrobe editor + privacy dialog |
| `Views/Controls/{AdornedAvatar,ProfilePrivacyPanel}.*` | 4 | 604 | The 4 real files inside R1's 113 |
| `Models/ProfileCosmetics.cs` | 1 | 319 | Pin cap `MaxPinnedAchievements = 4` (`:83`) |
| `Windows/ItemUnlockedPopup.xaml.cs` | 1 | 244 | The unlock popup |
| `Views/Tabs/{AchievementsTabView.xaml,DiscordTabView.*}` | 3 | 1305 | |

### 2.2 Bytes — counted separately and NEVER summed with code (plan §4.3)

| Tree | Files | Kind |
|---|---|---|
| `Resources/cosmetics/` | 95 | 91 PNG + 3 `.gitkeep` + 1 `registry.json` |
| `Resources/banners/` | 12 | 12 PNG — the 12 scene banners, 3 per mod |
| `Resources/achievements/` | 70 | 70 PNG badges (**39.3 MB**) |

**No proposal to fork these bytes into `client/` appears anywhere in this document.** The precedent
is the linked read-only csproj glob that already serves `dtrh`, `intake`, `tunnel` and `vendor`.

---

## 3. Behaviour map — every row cites both sides and carries a platform cell

Vocabulary closed: `COVERED`, `PARTIAL`, `GAP`, `OWNER-GATED`. Essentiality is decided against the
noun phrases the owner wrote in the Trainer Card row of `client/docs/task-board.md`, not re-derived
per row. An anchor
must **expose the required primitive** at an opened `client/src/**` line; a shipped page of the same
kind is not an anchor.

| # | Owner's phrase | WPF evidence (opened) | Required primitive | Port anchor | Label | Platform |
|---|---|---|---|---|---|---|
| B1 | Profile rebuilt as a Trainer Card | `MainWindow/MainWindow.ProfileCard.cs` (337 lines) + `Views/Tabs/DiscordTabView.xaml` (1077) | A page that renders a portrait, a name, a level and a stat block from local state | `client/src/CcpClient.Desktop/Features/Progression/TrainerCard.cs` + `Views/Pages/IntakePage.axaml` — the card, mounted on the Graded Intake page | **PARTIAL, and the half that landed is the half the port can stand behind.** *Re-derived 2026-08-23:* **no XP, level, streak or rank exists anywhere in `client/src`** — still true, and every `XP` hit in the tree is a comment saying the port does not have one (`Effects/BouncingTextEffect.cs:35`, `Effects/BubbleCountEffect.cs:84`). So the card renders the award ledger and SAYS, in words a user reads, that it has no level, no portrait and no wardrobe, and that an unreadable record is not the same as an empty one. Still missing: the portrait, the name, the level, the pinned badges and the banner — every one of them needs state or bytes this build does not have | Windows: unproven — gate: a headed capture of the page; the headless facts are draw-level only. **Linux: unproven** — no WSL distro on this machine (`client/memories/port-status.md:89-93 @ a8d32c219`) |
| B2 | 12 hand-made scene banners (3 per mod, card-sized) | `Resources/banners/` = 12 PNG (walk); `MainWindow.ProfileCard.cs` selects one | Show one of twelve fixed images behind the card, chosen by active mod | `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj:50-51` (`dtrh` payload glob) — the linked-read-only mechanism, already shipped four times | **COVERED by the payload-link precedent** — the cheapest item in the inventory | Windows: unproven — gate: headed capture. Linux: unproven — same gate on X11/Wayland |
| B3 | wardrobe of 60 adornments with a full editor + click-to-pin | `Dialogs/ProfileCustomizeDialog.xaml.cs:88` `BuildPins()`, `:356-370` pin tiles capped at `ProfileCosmetics.MaxPinnedAchievements` (`Models/ProfileCosmetics.cs:83` = 4); `Services/Profile/WardrobeCatalog.cs` (431) | Place, move and remove image adornments on a portrait, and pin up to four badges | `none` — nothing in `client/src` composes user-placed layers over an image, and nothing takes a drag-and-drop | **GAP: a layered image compositor with hit-testable, user-placed items** — (a) primitive: per-item placement + z-order + drag; (b) WPF uses a `Canvas` with `WardrobeStageGeometry` normalised coordinates (`Services/Profile/WardrobeStageGeometry.cs`, 131 lines); (c) the port would have to build the stage, the geometry mapping and the editor | Windows: unproven. Linux: unproven — identical gap on both, no OS interop involved |
| B4 | earn-it-to-wear-it (wardrobe pieces, guild avatars and badges unlock through achievements) | `Services/Profile/WardrobeCatalog.cs:181-194` `IsUnlockedForCurrentUser` — `RequiredAchievementId` (`:41-43`) then `App.Achievements?.Progress.IsUnlocked(gate)`, **fail-OPEN**: *"gating must never brick the picker"* (`:178-179`) | A predicate that hides an item until a named achievement is earned | `client/src/CcpClient.Desktop/Features/Progression/GradedRunAwards.cs:190` — `IsAwarded(id)`, a persisted membership test over `graded_run_awards.json` | **GAP: the wardrobe catalog and its gate.** *Corrected 2026-08-23: this row read "the port has **no achievement subsystem at all**", and that has been false since `9057cfa5b`.* (a) The ledger primitive this row asked for — a persisted earned-id set with an `IsUnlocked(id)` predicate — EXISTS; (b) what is still absent is the catalog side: `RequiredAchievementId` (`Services/Profile/WardrobeCatalog.cs:41-43`) and the fail-OPEN predicate (`:181-194`) over 79 items; (c) the ledger holds two ids, so 63 gated items would resolve against a two-entry set | Windows: unproven. Linux: unproven — pure logic + a file, so the gap is identical on both |
| B5 | 12 preset blank-subject avatars (3 per mod) | `Resources/cosmetics/avatars/` = 12 PNG, the 12 files the registry does NOT reference (§1.4); `Services/Profile/CosmeticsCatalog.cs` (406) | Offer twelve fixed portraits and remember which one is chosen | `client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs:83` — a generic typed store with typed load outcomes, already shipped | **PARTIAL on persistence** — missing member: no profile document type exists to hold the choice; the store itself is generic and ready | Windows: unproven — gate: headed capture of the picker. Linux: unproven |
| B6 | a leaderboard **privacy dialog** deciding exactly what is shared | `Dialogs/ProfilePrivacyDialog.xaml(.cs)` (119 lines) hosting `Views/Controls/ProfilePrivacyPanel.xaml.cs` — **11 sharing toggles** at `:43-74` (`ChkDiscordRichPresence`, `ChkShowLevelInPresence`, `ChkShowOnlineStatus`, `ChkShareAchievements`, `ChkShareLevelUps`, `ChkAllowDiscordDm`, `ChkShareProfilePicture`, `ChkPublicShareRealAvatar`, `ChkGoonShareAvatar`, `ChkGoonShareDiscordDm`, `ChkGoonRichPresence`) | Decide, per channel, what of the user's identity and progress other people can see | `none`, **and it may not get one from a lane** | **OWNER-GATED** (§5) — it expands both the network boundary and what is shown to others; `client/docs/capability-inventory.md:70` requires *"a consent-contract revision and owner review"* | Windows: unproven. Linux: unproven. **Neither cell is dischargeable by engineering** |
| B7 | the "Quiz" section renamed **"Graded runs"**, source-agnostic `OnQuizCompleted` | `Services/GamificationBridge.cs:88` (*"Graded runs (patron: top_of_the_class, teachers_pet, honor_roll, held_back)"*), `:91` subscribe, `:193` unsubscribe, `:578-611` the handler; *"source-agnostic on purpose"* (`:566-567`) | One handler that reads a grade and a category and does not care which surface produced them | `client/src/CcpClient.Desktop/Features/Progression/GradedRunAwards.cs:234` — `RecordGradedRun(bool topMarks, string? category)`, reached from `client/src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs:553` | **COVERED** since `9057cfa5b`. *Corrected 2026-08-23: this row read PARTIAL, "nothing subscribes and nothing is raised".* The consumer takes a verdict and a category and knows nothing about the producer, which is upstream's own stated property (`Services/GamificationBridge.cs:566-567`). The port carries no static event: the seam is a direct call at `IntakeQuizRun.cs:188`, which is mechanism, not outcome | Windows: unproven. Linux: unproven — pure logic; a unit test covers both equally, which is why no gate is claimed |
| B8 | `top_of_the_class` at the 90% bar | `Services/GamificationBridge.cs:571`, `:599-600`; bar `Services/Quiz/IntakeHostService.cs:55` `TopMarksPercent = 90.0`, guard `:434` `run.MaxScore > 0 && pct >= TopMarksPercent` | Award once when a run grades at or above 90% of max | `client/src/CcpClient.Desktop/Features/Intake/IntakeQuizRun.cs:139` `TopMarksPercent = 90.0`, `:147-148` `IsTopMarks`, and the award at `Features/Progression/GradedRunAwards.cs:274` `TryAward` | **COVERED** since `9057cfa5b`. *Corrected 2026-08-23: this row read PARTIAL, "missing member: the award".* The predicate stays on the producer exactly as upstream keeps it, and `top_of_the_class` is granted once, membership tested before the mutation (`AchievementService.cs:1115` ordering) | Windows: unproven. Linux: unproven — pure arithmetic, already unit-pinned by the shipped intake port |
| B9 | `honor_roll` over **DISTINCT** categories | `Services/GamificationBridge.cs:40` `HonorRollCategories = 3`, `:601-606` the add-and-count | Count distinct perfected categories and award at three | `client/src/CcpClient.Desktop/Features/Progression/GradedRunAwards.cs:47` (the persisted set), `:162` (`CategoryComparer`), `:153` (`HonorRollCategories = 3`) | **COVERED** since `9057cfa5b`, and **it does not import §5.2's defect**. *Corrected 2026-08-23: this row read PARTIAL, "missing members: the distinct SET, its comparer, and the threshold".* All three exist, and the comparer is `OrdinalIgnoreCase` where upstream's is the default ordinal one, with consumer-side normalisation at `:208` as the second layer — §5.3's recommendation, taken | Windows: unproven. Linux: unproven |
| B10 | `held_back` deliberately fail-streak-only | `Services/GamificationBridge.cs:42` `HeldBackFailStreak = 3`, `:592-595`; the deliberateness at `:574-576` (*"An intake has no fail state, so this can only ever come from the classic quiz. Left as-is deliberately (product decision)"*) and again at `Services/Quiz/IntakeHostService.cs:418-420` | Count consecutive failures and award at three — from the classic quiz only | `none` — no fail streak exists in the port | **GAP: a persisted fail-streak counter** — (a) primitive: an integer that survives restart and resets on a pass; (b) WPF keeps it on `ProgressionData.QuizFailStreak`; (c) trivial to build, **but it is dead in this port** because the only ported source can never fail | Windows: unproven. Linux: unproven |

### 3.1 What the map says overall

*Retallied 2026-08-23 after `9057cfa5b`. The tally when this census was written is kept beside it,
because the movement is the point: three rows moved, and all three moved for the same commit.*

| | At authoring (2026-08-21) | Today (2026-08-23) |
|---|---|---|
| **COVERED** | 1 of 10 (B2) | **4 of 10** (B2, **B7**, **B8**, **B9**) |
| **PARTIAL** | 5 of 10 (B1, B5, B7, B8, B9) | **2 of 10** (B1, B5) |
| **GAP** | 3 of 10 (B3, B4, B10) | 3 of 10 (B3, B4, B10) — B4's missing member SHRANK (the ledger landed; the catalog did not) |
| **OWNER-GATED** | 1 of 10 (B6) | 1 of 10 (B6) — unmoved, and not movable by engineering |

---

## 4. What the port already has of the consumer side — member by member

Answered by walking `client/src/` whole, not by reading the board row.

**AT AUTHORING (2026-08-21): five of sixteen enumerated members were present, and every one of the
five was pure arithmetic** — nothing stateful, nothing persisted, nothing that awarded.

**RE-WALKED 2026-08-23: eleven of sixteen are present, and the five that moved are exactly the
stateful ones.** `9057cfa5b` built the ledger, so "nothing persisted, nothing that awards" is no
longer true of this port. Three members remain deliberately absent (C6-C8, the static event and its
args — the port calls the consumer directly, which is mechanism rather than outcome) and two remain
absent because their behaviour was not built (C10, C12 — the passed branch; §6.2).

| # | Upstream member | Cited | Port | Status |
|---|---|---|---|---|
| C1 | `TopMarksPercent = 90.0` | `Services/Quiz/IntakeHostService.cs:55` | `IntakeQuizRun.cs:139` | **PRESENT** |
| C2 | grade % = `MaxScore > 0 ? Total/Max*100 : 0` | `IntakeHostService.cs:426` | `IntakeQuizRun.cs:142-143` | **PRESENT** |
| C3 | perfect guard `MaxScore > 0 && pct >= bar` | `IntakeHostService.cs:434` | `IntakeQuizRun.cs:147-148` | **PRESENT** |
| C4 | category normalisation | `IntakeHostService.cs:427-429` | `IntakeQuizRun.cs:153-154` | **PRESENT — verbatim** (§5.1) |
| C5 | mantra credit `min(affirmed, 5)` | `IntakeHostService.cs:451` | `IntakeQuizRun.cs:158-159` | **PRESENT** |
| C6 | `QuizService.QuizCompleted` static event | `Services/Quiz/QuizService.cs:29` | — | **ABSENT, and deliberately.** The port's seam is a direct call (`IntakeQuizRun.cs:188`); a static event is mechanism, and the outcome it carries is ported |
| C7 | `QuizService.RaiseQuizCompleted(...)` | `Services/Quiz/QuizService.cs:32-35` | — | **ABSENT, and deliberately** — same reason as C6. *Corrected 2026-08-23: this cell read "the port logs instead"; since `9057cfa5b` the same site RECORDS and then logs what it awarded (`IntakeHostWindow.axaml.cs:553-555`).* |
| C8 | `QuizCompletedEventArgs` | `Services/Quiz/QuizService.cs:133` | — | **ABSENT, and deliberately** — the two fields the consumer reads travel as parameters (`GradedRunAwards.cs:234`) |
| C9 | `OnQuizCompleted` handler | `GamificationBridge.cs:578-611` | `Features/Progression/GradedRunAwards.cs:234` `RecordGradedRun` | **PRESENT** since `9057cfa5b` — the perfect branch only; the passed branch is C10/C12/C13 |
| C10 | `TeachersPetPasses = 25` | `GamificationBridge.cs:41` | — | **ABSENT** — named residue (§6.2); the port awards no id it cannot count toward |
| C11 | `HonorRollCategories = 3` | `GamificationBridge.cs:40` | `Features/Progression/GradedRunAwards.cs:153` | **PRESENT** since `9057cfa5b` |
| C12 | `HeldBackFailStreak = 3` | `GamificationBridge.cs:42` | — | **ABSENT** — and unreachable by construction here: the port's only producer emits `passed: true` (§6.2, B10) |
| C13 | `ProgressionData.QuizzesPassed` / `QuizFailStreak` | `GamificationBridge.cs:586-593` | — | **ABSENT** — both counters; they belong to the passed branch, which was not built |
| C14 | `AchievementProgress.PerfectedQuizCategories` | `Models/AchievementProgress.cs:169` | `Features/Progression/GradedRunAwards.cs:47` `PerfectedCategories` | **PRESENT, and NOT a copy** — the set carries a named `OrdinalIgnoreCase` comparer (`:162`) where upstream's is the default ordinal one, which is §5.2's defect refused |
| C15 | `Ach.TryUnlockExclusive(id)` | `GamificationBridge.cs:589,595,600,605` | `Features/Progression/GradedRunAwards.cs:274` `TryAward` | **PRESENT, and UNGATED on purpose.** Upstream refuses unless `App.Patreon?.HasPremiumAccess == true` (`Services/Progression/AchievementService.cs:1107`, gate at `:1116-1120`); all four ids are `IsExclusive = true` (`Models/Achievement.cs:670,680,690,700`). This port has no entitlement AUTHORITY, and reading that absence as a refusal is what `Entitlement/EntitlementOutcome.cs:7-17` forbids — so it grants (owner decision; divergence D228) |
| C16 | `Ach.MarkDirty()` | `GamificationBridge.cs:608` | `Features/Progression/GradedRunAwards.cs:262` | **PRESENT as the OUTCOME** — upstream's 30 s dirty timer becomes an immediate save, and a run that changed nothing writes nothing |

**The board row's claim that the shipped intake port "computes and logs … but raises nothing" was
VERIFIED when this census was written, and it is NO LONGER TRUE.** The seam it named is
`client/src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs:553`, and `9057cfa5b`
attached the consumer there: the verdict reaches `IntakeGraded.Record` before the diagnostic line,
and the line reports what was awarded (`:555`) instead of discarding four computed values.

### 4.1 A citation defect inside the port's own product code

The shipped intake port's source comments cite upstream lines that do not say what they claim, checked
against its own stated baseline `0c9947a6` (v6.7.4) as well as today's tree:

| Port comment | Claims | At `0c9947a6` that line is | Verdict |
|---|---|---|---|
| `IntakeQuizRun.cs:150` | normalisation at `:418-420` | `:418` blank, `:419-420` the raise call; normalisation is **`:415-417`** | **WRONG by 3** |
| `IntakeQuizRun.cs:145` | perfect guard at `:417` | `:417` is `: run.Niche.Trim().ToLowerInvariant();`; the guard is **`:422`** | **WRONG by 5** |
| `IntakeQuizRun.cs:141` | grade at `:414` | `:414` **is** `var pct = …` | correct |

`client/src/**` was CLOSED to this packet, so these are reported, not fixed. Today the same members
sit at `:427-429` and `:434`.

---

## 5. `honor_roll`'s normalisation — pinned, and it is not where the row says it is

### 5.1 The exact upstream call, and the port matches it verbatim

**Upstream**, `ConditioningControlPanel/Services/Quiz/IntakeHostService.cs:427-429`, opened:

```csharp
var niche = string.IsNullOrWhiteSpace(run.Niche)
    ? IntakeNiche.Fallback
    : run.Niche.Trim().ToLowerInvariant();
```

**Port**, `client/src/CcpClient.Desktop/Features/Intake/IntakeQuizRun.cs:153-154`, opened:

```csharp
public static string Category(IntakeQuizRun run) =>
    string.IsNullOrWhiteSpace(run.Niche) ? IntakeNiche.Fallback : run.Niche.Trim().ToLowerInvariant();
```

**MATCHES — verbatim, including the whitespace fallback.** The board row is right that
`Trim().ToLowerInvariant()` is load-bearing and right that this port already carries it.

### 5.2 But the row locates it wrongly, and the real arrangement contains a latent defect

The row implies the normalisation protects `honor_roll` at the consumer. **It does not, because the
consumer normalises nothing and the set that holds the categories is case-sensitive.**

| Link | What it actually does | Cited |
|---|---|---|
| The distinct set | `public HashSet<string> PerfectedQuizCategories { get; set; } = new();` — a **default** `HashSet<string>`, i.e. `EqualityComparer<string>.Default`: **ordinal, case-sensitive, whitespace-sensitive** | `Models/AchievementProgress.cs:169` |
| The consumer | `p.PerfectedQuizCategories.Add(e.Category)` — **the raw category, no trim, no case fold** | `Services/GamificationBridge.cs:601` |
| Source A (intake) | `run.Niche.Trim().ToLowerInvariant()` — **normalised** | `Services/Quiz/IntakeHostService.cs:427-429` |
| Source B (classic quiz) | `var categoryId = catDef?.Id ?? result.Category.ToString();` — **not normalised** | `Windows/QuizWindow.xaml.cs:537` |

**The two sources of one deliberately source-agnostic event disagree about normalisation.** Source
B's fallback is `QuizCategory.ToString()`, and that enum is PascalCase — `Sissy`, `Bambi`,
`Obedience`, `Mindlessness`, `Submission` (`Services/Quiz/QuizService.cs:20-27`). The
built-in definitions carry lowercase ids (`QuizService.cs:1122,1135,1148,1161,1174`), so the two
agree **only by coincidence**; the `??` arm is reachable by construction, since `catDef` comes from
`_quizService?.CurrentCategoryDefinition` (`QuizWindow.xaml.cs:467`) and both links are nullable.

If it fires, `"sissy"` and `"Sissy"` are two entries. `HonorRollCategories = 3`
(`GamificationBridge.cs:40`), so **one category counted twice fills a third of the award and
`honor_roll` unlocks a category early**. It throws nothing and logs nothing. The set is persisted to
`%APPDATA%/ConditioningControlPanel/achievements.json` (`Services/Progression/AchievementService.cs:70-74`),
so a duplicate, once written, is permanent.

Mitigation today, stated so the finding is not overclaimed: the classic quiz launcher is
`Visibility="Collapsed"` (`Views/Tabs/GradedIntakeTabView.xaml:140`), so source B is unreachable for
a new user. **This is a latent defect with a reachable construction, not a reproduced bug** — I did
not run the app.

### 5.3 The recommendation, stated as shape rather than as a fix

A port that lands the award should normalise **at the consumer or in the set's comparer**, not at
one of two producers. The upstream arrangement is safe only while exactly one producer is reachable,
and the handler's own comment says it is source-agnostic *on purpose* — which is precisely the
property that makes producer-side normalisation the wrong place for it.

### 5.4 Every threshold, re-derived from the shipping bytes

| Constant | Value | Cited |
|---|---|---|
| `HonorRollCategories` | **3** | `Services/GamificationBridge.cs:40` |
| `TeachersPetPasses` | **25** | `Services/GamificationBridge.cs:41` |
| `HeldBackFailStreak` | **3** | `Services/GamificationBridge.cs:42` |
| `TopMarksPercent` | **90.0** | `Services/Quiz/IntakeHostService.cs:55` |
| `MaxPinnedAchievements` | **4** | `Models/ProfileCosmetics.cs:83` |

---

## 6. Verdict: BUILDABLE-IN-PART — with the inventory and the residue

Applying plan §12.2 in order: clause 1 fails (there are GAP and OWNER-GATED rows). **Clause 2 fires**
— a subset of rows is entirely COVERED/PARTIAL and is independently user-observable — so the verdict
is BUILDABLE-IN-PART and clause 3 is not reached.

### 6.1 The buildable unit, named with its inventory — **BUILT at `9057cfa5b`**

*Status 2026-08-23: this section is no longer a proposal. Every item in the inventory below landed,
and the "Port code to add" row now reads as what was added. The unit's own honest bound — that it
writes to a store nothing renders — is what the Trainer Card surface row addresses; see §6.2.*

> **The graded-run award path.** B7 + B8 + B9 as a unit: subscribe the computed verdict that
> `IntakeGraded` already produces, hold a persisted distinct-category set with an explicit comparer,
> and award `top_of_the_class` and `honor_roll`.

| Item | Size |
|---|---|
| Upstream behaviour to port | `Services/GamificationBridge.cs:578-611` — **34 lines**, plus 3 constants at `:40-42` |
| Port code already present | `IntakeQuizRun.cs:133-159` — 5 of 16 members (§4) |
| Port code ADDED (`9057cfa5b`) | `Features/Progression/GradedRunAwards.cs`, 293 lines: the ledger (`IsAwarded` `:190`, `TryAward` `:274`) over the existing `PersistenceStore<TModel>` (`Persistence/PersistenceStore.cs:83`), the distinct-category set with its **named** comparer (`:47`, `:162`), and the call at `IntakeHostWindow.axaml.cs:553`. 21 facts, `client/tests/CcpClient.Tests/GradedRunAwardsTests.cs` |
| Assets required | **none** — no banner, no badge art, no wardrobe PNG is needed to award an achievement the port does not yet render |
| OS interop required | **none.** Cross-platform by construction, headlessly testable |
| Owner decisions required | **none** — it writes to a store the port already owns and expands no contract (plan §12.1) |

It is user-observable: a user completes a graded intake and the app records an award it did not
record before.

**THE THINNEST JOINT IN THIS DERIVATION, named rather than asserted.** Everything else in the
verdict is mechanical — the labels come from the five-cell rule, and clauses 1-3 are applied in a
fixed order. Clause 2's *"independently user-observable"* is the one predicate that is a judgement,
and this unit sits close to its edge: **the award is written to a store that, by row B1, nothing in
the port can render.** A user would see the effect only through whatever surface the next packet
gives it, or by opening the file.

Two things keep it on the right side of the line, and a reader is entitled to weigh them
differently. (1) The alternative reading — that nothing is user-observable until the Trainer Card
page exists — would make clause 2 unreachable for every unit of this surface and collapse the
verdict to REFUSED by a definition rather than by the code, which is the failure mode plan §12.1 was
amended to avoid. (2) An award ledger is the thing B4's "earn-it-to-wear-it" and the achievements
tab both consume, so building it first is what makes the *next* row renderable rather than being a
write into nowhere.

**If the owner reads clause 2 more strictly, the verdict is REFUSED with exactly the same
inventory** — §6.1 stays the first thing to build either way, and nothing else in this census moves.
That is the honest bound on the one soft predicate here.

### 6.2 Named residue, so nothing is lost

| Item | Disposition |
|---|---|
| Wardrobe + editor + click-to-pin (B3) | **GAP.** Needs a layered image stage with placement geometry; 79 items, 63 of them gated. |
| Earn-it-to-wear-it (B4) | **GAP, and one half of it is now built.** The ledger landed with §6.1 (`GradedRunAwards.cs:190`); what remains is the 79-item catalog and its fail-OPEN predicate (`Services/Profile/WardrobeCatalog.cs:41-43,181-194`). |
| `held_back` (B10) | **GAP, and dead on arrival.** The only ported source cannot fail. Port the counter only if the classic quiz is ever ported. |
| Trainer Card surface (B1) | **PARTIAL, and the honest minimum is BUILT** (`Features/Progression/TrainerCard.cs`, rendered on `Views/Pages/IntakePage.axaml`). XP, level, streak and rank still exist nowhere in `client/src` (re-derived 2026-08-23), so the card shows none of them and says so rather than showing a zero. It is a section on the Graded Intake page rather than a sixth rail door, because `Navigation/ShellRoutes.cs` refuses a door whose destination is nearly empty and one module of real state does not fill a room. |
| Banners (B2) | **COVERED.** A four-line csproj glob; the cheapest item here. |
| Preset avatars (B5) | **PARTIAL.** Needs a profile document; the store is generic and shipped. |
| Leaderboard privacy dialog (B6) | **OWNER-GATED.** §5 of this document's owner section; **never priced.** |
| `teachers_pet` | Not in the row's phrases; found by the walk. 25 passes, same ledger as §6.1 — **still unbuilt**, because the ledger holds no pass counter (C10/C13) and an award nothing can count toward is worse than an absent one. |

### 6.3 What the next packet should NOT be

**Not "port `Views/Controls/`".** §1.3: 96.5% of it is the AI companion, the settings pages and
three rack panels this port already ships.

---

## 7. OWNER-FLAGGED — one decision, deliberately not priced

Per plan §12.1 the test is on the **contract**, not the row: expanding consent, persistence,
networking or entitlement is owner-gated; writing to a store the port already owns is sizable. §6.1
is on the sizable side. This section is not.

### 7.1 The privacy dialog decides what other people see, over the network

`Dialogs/ProfilePrivacyDialog.xaml.cs` hosts `Views/Controls/ProfilePrivacyPanel.xaml.cs`, whose
**11 toggles** (`:43-74`) govern Discord rich presence, online status, level display, achievement
and level-up sharing, DM permission, profile picture sharing, real-avatar publication, and three
Goon-Game sharing switches. The leaderboard itself makes **two GET requests and no POST** against
`https://codebambi-proxy.vercel.app` (`Services/Progression/LeaderboardService.cs:16`): the board
fetch at `:106`, whose URL carries the user's own `unified_id` (`:103-105`), and a display-name
lookup at `:174` (`:173`). Both carry `X-Client-Version` and a product user-agent (`:64-65`).

**Corrected at code review, and the correction sharpens the finding rather than softening it.** An
earlier draft of this paragraph said "POSTs and GETs"; there is **no `PostAsync` anywhere in
`LeaderboardService.cs`**, and no upload path appears in that service at all. What leaves the
machine here is therefore an identifier, a display name and a version — not a score upload — and
where the score submission happens is a question this census did not answer and does not guess at.
The owner-flagged conclusion is unchanged: an identifier plus headers going to a remote endpoint is
outbound identity, whatever the verb.

Unlike the FYP census's finding this is a **first-party** endpoint, not an unofficial third-party API, so
the question is narrower — but it is still the port's first outbound identity-sharing surface, and
`client/docs/capability-inventory.md:70` puts *"networking"* and *"telemetry"* under
*"a consent-contract revision and owner review"*.

**What the owner must decide, and no amount of engineering answers:** whether the port takes on an
outbound progress-sharing boundary at all; whether it reproduces the 11-toggle model or a smaller
one; and what the default is for a user who never opens the dialog.

### 7.2 The four privacy questions, answered for the whole surface

| # | Question | Answer | Citation |
|---|---|---|---|
| Q1 | Changes what is **persisted**? | **YES** — achievement progress including the honor-roll category set, to `%APPDATA%/ConditioningControlPanel/achievements.json` | `Services/Progression/AchievementService.cs:70-74`; `Models/AchievementProgress.cs:169` |
| Q2 | Changes what is **shown to others**? | **YES, and it is the point of the surface** — 11 toggles over Discord presence, achievements, level-ups, avatars and DMs | `Views/Controls/ProfilePrivacyPanel.xaml.cs:43-74` |
| Q3 | Changes what **leaves the machine**? | **YES** — leaderboard traffic to a first-party proxy, with client version and rank identity | `Services/Progression/LeaderboardService.cs:16,64-65,106,174` |
| Q4 | What **sensor**, under whose consent? | **NONE.** No camera, microphone, or screen capture anywhere in this surface — I searched the whole walk and found none. **This surface differs from For You Feed exactly here** | walk over the surface trees; no webcam/gaze/capture token in any file of §2.1 |

**Q1 is on the sizable side of §12.1, not the gated side:** the port already owns a typed persistence
store (`Persistence/PersistenceStore.cs:83`) and adding an achievement document to it expands no
contract. **Q2 and Q3 are the gated pair**, and they are what §7.1 is about.

---

## 8. What this census does NOT prove

- **Nothing was built, run, or rendered BY THIS CENSUS.** `client/src/**` was closed to the packet
  that wrote it and no product code was written here. *Corrected 2026-08-23: this bullet also said
  "No Trainer Card code exists in `client/`", and that stopped being true at `9057cfa5b` — which
  landed after this document and amended nothing in it. A census that is trusted for what the port
  does NOT have is exactly the document a later build must come back and amend; this one was not,
  for two days. Anything below about the port's own tree is a claim about the tree AS OF the date in
  the line that makes it.*
- **No headed evidence of any kind.** No window was shown, no frame composited, no pixel compared.
  Every claim above is a claim about *source*.
- **Linux is unproven for every row without exception.** `wsl.exe --list --verbose` reports no
  installed distributions on this machine (`client/memories/port-status.md:89-93 @ a8d32c219`), so every Linux
  cell is a named gate, never a discharge. Windows is unproven for every row too — nothing here was
  executed on either OS.
- **The `honor_roll` duplicate-category defect (§5.2) is reasoning over source, not a reproduction.**
  I did not run the app, did not construct a `catDef`-null state, and did not observe a duplicate
  entry in a real `achievements.json`.
- **The consumer enumeration (§4) is 16 members found by grep and by reading `GamificationBridge`'s
  quiz path.** A member reached by reflection, by a source generator, or through a data-bound XAML
  path would not appear.
- **The 96.5% is NOT re-derived by any test.** It is `109/113` over the merge delta, and the delta
  comes from `git diff --diff-filter=A` against one historical commit. The guard re-derives the
  directory share instead — `109/114 = 95.6%`, pinned exactly in §9.5 — so a reader trusting a
  machine-checked number should use that one. Both are stated with their denominator named because
  they answer different questions: the row's claim was about the delta, the port's work is about the
  directory.
- **The delta interpretation in §1.2 rests on one merge commit.** I tested two candidate ranges and
  reported both; if the row's author used a third, the row's numbers might have meant something
  else again, and my directory totals would still stand because those are measured from the bytes.
- **The 39.3 MB badge figure is bytes on disk**, not the size of anything the port would ship; no
  packaging decision is implied.

---

## 9. Pinned enumeration (parsed by `TrainerCardCensusTests`, re-derived from the shipping bytes)

This section is the DATA; `client/tests/CcpClient.Tests/TrainerCardCensusTests.cs` is the LOGIC. The
guard walks `ConditioningControlPanel/` **by directory, recursively** on every run, re-derives every
count and every pinned expression from the shipping bytes, and compares them against the tables
below. The directory roots, the needles and the expression patterns live in the test, not here, so
editing this document can never shrink the search.

### 9.1 Directory counts, walked recursively

| Key | Value |
|---|---|
| Views/Controls | 114 |
| Views/Controls/Companion | 87 |
| Views/Controls/AppSettings | 16 |
| Views/Controls/Studio | 6 |
| Services/Profile | 3 |
| Services/Progression | 10 |
| Resources/cosmetics | 95 |
| Resources/banners | 12 |
| Resources/achievements | 70 |

### 9.2 The wardrobe registry

| Key | Value |
|---|---|
| registry-items | 79 |
| registry-deco | 55 |
| registry-charm | 24 |
| registry-achievement-gated | 63 |
| registry-free | 16 |
| registry-unresolved-files | 0 |

### 9.3 Thresholds re-derived from source

| Key | Value | File |
|---|---|---|
| HonorRollCategories | 3 | Services/GamificationBridge.cs |
| TeachersPetPasses | 25 | Services/GamificationBridge.cs |
| HeldBackFailStreak | 3 | Services/GamificationBridge.cs |
| TopMarksPercent | 90.0 | Services/Quiz/IntakeHostService.cs |
| MaxPinnedAchievements | 4 | Models/ProfileCosmetics.cs |

### 9.4 The normalisation asymmetry (§5.2), re-derived on both sides

| Key | Value |
|---|---|
| upstream-normalises | Services/Quiz/IntakeHostService.cs |
| upstream-does-not-normalise | Windows/QuizWindow.xaml.cs |
| distinct-set-comparer | default |
| port-normalisation-matches | true |

**The LINE NUMBERS the defect chain rests on, re-derived from the bytes on every run.** Added at
code review. §9.4 originally pinned paths only, and the guard regex-matched the producer expression
*anywhere in the file* — so the suite was green while three citations in this document were wrong,
one of them the `:537` in §5.2 and D222. **A pin that cannot see the number it is protecting is not
protecting it**, and this is the coverage gap that let a citation defect ride inside the packet whose
own §4.1 grades someone else's comments for exactly that.

| Key | Value |
|---|---|
| unnormalised-producer-line | 537 |
| distinct-set-add-line | 601 |
| normalising-producer-line | 429 |
| unnormalised-producer-expression-occurrences | 4 |

### 9.5 The split that carries the finding — pinned EXACTLY, not behind a threshold

Added at code review. The published headline share was `109/113` over the **merge delta**, while the
guard re-derived `109/114` over **today's directory** and then asserted only `> 0.90` — so the claim
would have survived a drift all the way to 91%. Both numbers are now stated with their denominator
named, and the re-derivable one is pinned to the tenth of a percent.

| Key | Value |
|---|---|
| views-controls-total | 114 |
| views-controls-foreign | 109 |
| views-controls-trainer-card | 4 |
| views-controls-foreign-share-percent | 95.6 |

**`views-controls-foreign-share-percent` is `109/114` of the directory as it stands today, and every
term in it is re-derived from the shipping bytes on every run.** The **96.5%** quoted in §1.3 and in
D220 is the same ratio over the row's own denominator — `109/113` of the merge delta — which comes
from `git diff --diff-filter=A` and is therefore historical: **no test re-derives it**, and §8 says
so. The two numbers differ only because `HapticUiModels.cs` predates the merge.
