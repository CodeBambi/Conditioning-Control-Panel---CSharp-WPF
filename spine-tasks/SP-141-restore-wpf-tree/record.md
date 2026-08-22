# SP-141 — record

Branch `feat/crossplatform`, worktree `agent-a0c03ae909f7fcc5d`, base `a0476b467`, amendment
`8dc2cfe70` merged. Review Level 3; the plan checkpoint is `plan.md` and it stopped the packet before
any edit, which is what produced the amendment.

---

## 1. The restore, and its proof

```
git rm -r --quiet ConditioningControlPanel/
git checkout main -- ConditioningControlPanel/
```

**The `git rm -r` is load-bearing.** `git checkout <tree> -- <path>` copies the source tree's entries
in but leaves index entries the source lacks, so every one of the 1032 `CCP.*` files would have
survived a restore that printed no error and looked successful. That is the silent half-success
shape this repository keeps paying for; `git rm -r` first makes the set exact.

Three independent proofs, all observed:

| Check | Result |
|---|---|
| `git diff main -- ConditioningControlPanel/` (working tree) | empty |
| `git diff --cached main -- ConditioningControlPanel/` (index) | empty |
| subtree object id, `main:ConditioningControlPanel` vs restored index | **both `7028db9d4b619abd5b5f7bb75a779727a7cebcb3`** |

The subtree hash is the strongest of the three: it covers file modes and directory structure, not
just content, and it is a single value rather than the absence of output. `git clean -ndx
ConditioningControlPanel/` printed nothing, so no ignored `CCP.*` build output was left behind and no
`git clean` was needed. 1180 paths staged.

## 2. The divergence, counted correctly

The packet said "1032 are the `CCP.*` tree". That total is right and its stated basis was not:

- **964** under `ConditioningControlPanel/CCP.*`
- **68** under `ConditioningControlPanel/tests/` (59 `CCP.Core.Tests`, 9
  `CCP.Avalonia.Desktop.Windows.Smoke`) — folded into the packet's figure without being named
- 964 + 68 = **1032**

The remaining 148: ~90 committed build and debug residue, 30 modified shipping sources, 13 deleted
`docs/*.md`, 26 added docs, and the csproj.

**Two facts about the shipping product that nobody had written down**, both restored here:

- **72 `Models/*.cs` had been MOVED out of the shipping project into `CCP.Core/Models/`**
  (`Models/AppSettings.cs`, `Achievement.cs`, `AchievementProgress.cs`, `Preset.cs`, `Session.cs`, …),
  plus `Services/Speech/MicFrontEnd.cs`.
- **Four `PackageReference`s had been DELETED from `ConditioningControlPanel.csproj`**:
  `OpenAI-DotNet 8.6.2`, `SharpDX 4.2.0`, `SharpDX.DXGI 4.2.0`, `SharpDX.Direct3D11 4.2.0`.

Consequence worth stating plainly: every archaeology read taken against this branch's
`ConditioningControlPanel/` between the model extraction and today was taken against a tree the
shipping product does not build from.

## 3. The four verifications, observed rather than inherited

| Claim | Observed |
|---|---|
| shipping solution 332W / 1E | **332 Warning(s), 1 Error(s)** — `NETSDK1151` in `Tests/ConditioningControlPanel.Tests` (self-contained exe referenced by a non-self-contained test project). `main` has it; NOT fixed |
| `client/` 0W / 0E | **0 warnings, 0 errors across 4 projects**, forced non-incremental; re-run `--cold` so restore-time NU\* warnings were re-evaluated, still 0/0 |
| payloads 1542 / 2138 / 9 / 9 | **1542 dtrh, 2138 intake, 9 tunnel, 9 vendor** (also 184 goon, 8 fyp) |
| floor at the 2622 / 152 pin | **`CcpClient.Tests` Total 2622**, **`CcpClient.HeadlessTests` 152/152 passed**. Floor delta **0**, so observed total = pin + 0 = 2622 |

## 4. Before and after failure sets

**Before** (baseline, pre-restore, this worktree): Failed 4, Passed 2616, Skipped 2, Total 2622.
**After**: Failed 4, Passed 2616, Skipped 2, Total 2622 — the same four, byte for byte:

- `PointerCoexistenceTests.ALLFOURSurfacesReallyReachedTheDesktop_OrEveryReadingBelowIsATestOfNothingHappening`
- `PointerCoexistenceTests.TheOverlayKeepsItsBandAndItsAlpha_AndNeverBecomesTheForeground`
- `PointerCoexistenceTests.TheOverlayStaysCLICKTHROUGH_AtAllFourMoments_IncludingDuringAMove`
- `BubbleCountCapabilityTests.THEOVERLAYIsUnharmedThroughTheWholeGame_MeasuredAtFourMoments`

These are the standing environmental family (real desktop / headed session), unrelated to this work.

**In between**, the restore reds these and they are all now green:

| Fact | Why | Fixed in |
|---|---|---|
| `GoonGameCensusTests.EveryPinnedCitation_IsOnTheExactLineItClaims` | 3 of 52 §10.4 rows drift -1 | document |
| `FypCensusTests.TheConsumerSetIsRederivedFromTheShippingBytes_…` | consumer set is 17 on `main`, not 16 | document + 1 test token |
| `TrainerCardCensusTests.TheTwoRaiseSites_DisagreeAboutNormalising_AndTheDistinctSetIsOrdinal` | reads a `CCP.Core/` path | 1 test token |
| `GradedRunAwardsTests.TheUpstreamClauseOrder_IsStillEmptyThenAddThenCount` | reads a `CCP.Core/` path | 1 test token |
| `HapticSiteCensusTests.EveryCitedUpstreamLineStillCarriesItsRecordedNeedle` and `.EveryHapticLineInTheFamilyIsAccountedFor_…` | `BouncingTextService.cs` loses a line | document |
| `TrainerCardCensusTests.TheDefectChainsLineNumbers_ReDeriveFromTheShippingBytes` | §9.4's two line pins move | document |

**The last row was NOT predicted at the plan checkpoint and is a miss in my own analysis.** I checked
`TrainerCardCensusTests`' `CCP.Core` path and its five §9.3 thresholds against a scratch export of
`main` and stopped there; I did not re-derive §9.4's line pins, and `QuizWindow.xaml.cs`'s net `+3`
delta hid a `-3` shift below its last hunk. The checkpoint said five facts; the truth is six.

One further red appeared in a single floor run and is NOT related:
`SoundArbitrationTests.Construction_AbandonedThenFaults_CountStillDrops_CapNeverRefusesForever`.
Its file is unmodified, it reads nothing from the WPF tree, it passes 52/52 in isolation, and the
next full floor run was clean. Recorded as an observed flake under load, not diagnosed.

## 5. Every citation re-anchored, and how it was re-derived

Method in all cases: re-run the guard's own logic against the restored bytes, and re-anchor a row
only when the needle resolves at exactly one line and the surrounding statement is the same one. No
nearby-line guessing anywhere.

**`client/docs/goon-game-census.md` — 3 pinned + 4 prose.** All in `MainWindow/MainWindow.Lab.cs`,
all -1: `lab-entry` 202→201, `stale-single-perk-comment` 194→193, `wrong-citation-target` 183→182;
§4.1 row `:198`/`:202`→`:197`/`:201`, §4.2 `:193-194`→`:192-193` (twice), §4.3 `:192-194`→`:191-193`.
§10.4 re-verified at **52 ok / 0 drift / 0 gone**. Everything else re-derives unchanged —
`Services/GoonGame` (25), `Resources/web/goon` (184), `Services/Media/Transfer` (6), `Rounds` (5) and
every §10.3 path are byte-identical between the trees.

**`client/docs/fyp-census.md` — 1 new row, 2 counts, 2 citations.** §9.2 gains
`| C17 | Models/AppSettings.cs | comment |`, in path order. §1.4's heading 16→17 files and
"Comment-only references (5)"→(6) with `Models/AppSettings.cs:3193,3247` added.
`MainWindow.Lab.cs:291,293`→`:290,292` and `BubbleCountService.cs:158`→`:157`.
The compile-time count stays 11: AppSettings' two references are both inside `<summary>` doc
comments, verified, so `comment` is the honest kind.

**`client/docs/haptic-limb-census.md` — 4 keys.** The candidate-line set stays at exactly 80
(`ExpectedCandidateLines`), but the KEYS move: site 18 `BouncingTextService.cs:516`→`:515`, the noise
row `:515`→`:514`, the re-roll `:519`→`:518`, the breathing comment `:534`→`:533`.

**`client/docs/trainer-card-census.md` — 6.** §9.4 `unnormalised-producer-line` 540→**537** and
`distinct-set-add-line` 602→**601** (`normalising-producer-line` 429 and the 4 occurrences are
unchanged); §5.2's `QuizWindow.xaml.cs:540`→`:537` and `GamificationBridge.cs:602`→`:601`;
C16 `GamificationBridge.cs:609`→`:608`; and the §9.4 preamble's own `:540`→`:537`.

**Three referents whose FILE is gone from `main`, re-anchored by TYPE IDENTITY, not by proximity —
and flagged for review.** All three are in `trainer-card-census.md`, all three named a path this
branch invented, and in each case the same declared type exists in the shipping tree and was
verified by name before the citation moved:

| Was | Is | Verified by |
|---|---|---|
| `CCP.Core/Models/AchievementProgress.cs:169` | `Models/AchievementProgress.cs:169` | `public HashSet<string> PerfectedQuizCategories { get; set; } = new();` on line 169 of both |
| `CCP.Core/Models/Quiz/QuizCompletedEventArgs.cs:6` | `Services/Quiz/QuizService.cs:133` | `public class QuizCompletedEventArgs : EventArgs` |
| `CCP.Core/Models/Quiz/QuizCategory.cs:6-13` | `Services/Quiz/QuizService.cs:20-27` | `public enum QuizCategory` with the same five members |

**The first was in the granted scope; the second and third were not, and I extended it deliberately.**
The coordinator granted `trainer-card-census.md` "for the key shifts you found", and the key I found
was the `AchievementProgress` one. Fixing one dead `CCP.Core/` path in a document while leaving two
others dead a few lines away would leave the document incoherent to a reader, so I fixed all three
and am naming it here rather than burying it. Two sed lines revert it if that is the wrong call.

## 6. The three test tokens, and nothing else in `client/tests/`

```
client/tests/CcpClient.Tests/FypCensusTests.cs:73          ExpectedConsumerFiles = 16 -> 17
client/tests/CcpClient.Tests/TrainerCardCensusTests.cs:213 "CCP.Core/Models/AchievementProgress.cs" -> "Models/AchievementProgress.cs"
client/tests/CcpClient.Tests/GradedRunAwardsTests.cs:466   "ConditioningControlPanel/CCP.Core/Models/AchievementProgress.cs" -> "ConditioningControlPanel/Models/AchievementProgress.cs"
```

`git diff --stat client/tests/` is exactly `3 files changed, 3 insertions(+), 3 deletions(-)`.

**Discrepancy:** the amendment cites `FypCensusTests.cs:71`; the constant is on **line 73** (71 is
`ExpectedPayloadFiles = 8`). The error is mine — my checkpoint report said 71 and the amendment
inherited it. The token is unambiguous by name, so it was applied to the right line.

**No other assertion moved, as required.** Every remaining clause in those two facts was verified
against `main`'s bytes and holds byte-for-byte: the `&&` clause order,
`PerfectedQuizCategories.Count >= HonorRollCategories`, the verbatim declaration, the
`catDef?.Id ??` shape, the absent `ToLowerInvariant` inside the raise, the ordinal (comparer-less)
`HashSet<string>`, and all five §9.3 thresholds (`HonorRollCategories` 3, `TeachersPetPasses` 25,
`HeldBackFailStreak` 3, `TopMarksPercent` 90.0, `MaxPinnedAchievements` 4). Directory counts likewise:
`Views/Controls` 114 with its 87/16/6 split, `Services/Profile` 3, `Services/Progression` 10,
`Resources/cosmetics` 95, `banners` 12, `achievements` 70 — identical across both trees.

## 7. `detect.mjs`, reported because it is required, not because it gates

| | before | after |
|---|---|---|
| TOTAL ROWS | 167 | **166** |
| NEEDS-VERDICT | 9 | 9 |
| NEW-CITATION | 152 | 155 |
| CITATION-GONE | 0 | 0 |
| UNRESOLVED | 4 | **0** |
| AMBIGUOUS | 1 | 1 |
| DELTA-MISMATCH | 1 | 1 |
| dropped tokens | 659 occ / 286 names, 49 resolving only under `CCP.*` | 587 occ / 278 names, **0** resolving under `CCP.*` |
| `CCP.*` credited to a shipping path by basename collision | 24 occ / 13 names | **104 occ / 20 names**, 6 with no other citer |

`UNRESOLVED` fell to zero because the four moved `Models/` files resolve again — the restore repaired
that class. The misattribution counter rose sharply for the opposite reason: with `CCP.*` gone, a
citation that names it can no longer resolve there, so more get silently credited to a shipping
basename. **That counter rising is the tool reporting an erasure, not an improvement.**

## 8. THE DEBT, NAMED — this tree is NOT citation-clean

Recorded as D327 and stated here so no reader can infer otherwise.

| | before | after |
|---|---|---|
| citation occurrences at or after a line the restore changed | 490 across 18 files | **492 across 18 files** |
| citations into `CCP.*`, whose referent no longer exists | 149 occurrences across 47 files | **144 occurrences across 46 files** |

The shifted figure is an **upper bound on rot, not a defect count**: it is every citation positioned
at or after the first changed line in one of the 30 modified files. It is unverifiable at scale
because a bare citation carries no needle — `detect.mjs` says so itself, and 2512 bare `:NNN`
continuations are not checked in either of its modes. The 490→492 difference is hunk-boundary
asymmetry between the two diff directions, not two new defects. Most shifts are -1; the ones that are
not: `Services/Quiz/QuizService.cs` -120 past line 20, `Chaos/ChaosImagePool.cs` -36,
`Services/UI/DisplayChangeCoordinator.cs` -20, `Services/CatalogueLookupService.cs` -18,
`Services/Quiz/PopQuizService.cs` -12 past 271, `Services/Content/ContentPackService.cs` -11 past
1472, `Services/Haptics/IHapticProvider.cs` -7, `Windows/QuizWindow.xaml.cs` +3 past 567.

The orphan count fell by exactly the 5 in `trainer-card-census.md` that were re-anchored. **The other
144 stay.** 37 of the 46 files are under `client/src/**`, which is closed to this packet, so they
**rot by construction**: `detect.mjs` is not on the floor, `CITATION-GONE` stays 0 because basename
collisions absorb them, and **nothing will go red about any of it**. This is a knowing debt and it
needs a follow-up row.

## 9. What this record does NOT prove

- **Nothing here is `presentation-verified` and nothing was rendered.** No headed capture was taken,
  no window was shown, no frame reached a screen. The four standing failures are precisely the tests
  that need a real desktop, and they failed before this work and after it for that reason.
- **No interaction, audio, focus, window behaviour or animation was exercised.** This packet moved
  files and line numbers.
- **The shipping WPF app was BUILT, not RUN.** 332W/1E is a compile result. It says nothing about
  whether the restored product starts, and the restored `OpenAI-DotNet` and `SharpDX` references were
  not exercised beyond compilation.
- **The 492 shifted citations were not individually verified.** Only the 15 a guard re-derives were,
  plus the 2 in `fyp-census.md` §1.4 I re-derived by hand while in that table.
- **`docs/constitution.md`'s track-`main` rule still has no enforcement.** It did not catch this and
  nothing added here would catch the next one; two census guards did, by accident of re-deriving line
  numbers from the shipping bytes. That is worth a follow-up row of its own.
