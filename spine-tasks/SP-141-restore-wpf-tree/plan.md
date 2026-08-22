# SP-141 — plan checkpoint (Review Level 3). STOPPED before any product edit.

Worktree: `C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-a0c03ae909f7fcc5d`
Base: `feat/crossplatform` @ `a0476b467`. `main` @ `87035e9a7`.
Nothing outside this file has been written. Measurement was done read-only against a scratch export
of `main`'s tree (`git archive main ConditioningControlPanel | tar -x -C <scratch>`), never against
the worktree.

---

## 1. Premises checked, one by one

| Packet claim | Measured | Verdict |
|---|---|---|
| `git diff --stat main HEAD -- ConditioningControlPanel/` = 1180 files | 1180 files, 254585 insertions, 3413 deletions | CONFIRMED |
| 1032 of them are the `CCP.*` first-attempt tree | 964 under `ConditioningControlPanel/CCP.*` + 68 under `ConditioningControlPanel/tests/` (59 `CCP.Core.Tests`, 9 `CCP.Avalonia.Desktop.Windows.Smoke`) = 1032 | CONFIRMED |
| `main` has no `CCP.Core` `ProjectReference`; this branch has one at csproj:52 | `ProjectReference Include="CCP.Core\CCP.Core.csproj"` is at line 52 on this branch; absent on `main` | CONFIRMED |
| payloads 1542 dtrh / 2138 intake / 9 tunnel / 9 vendor | identical on both trees: 1542 / 2138 / 9 / 9 | CONFIRMED, and the restore does not touch them |
| floor pin 2622 / 152 | `CcpClient.Tests` Total = 2622 today | CONFIRMED |
| floor delta 0 | no tests added by this packet | plan holds |
| exactly TWO guards go red | **FALSE — at least FIVE facts across FIVE test classes, and three of them cannot be fixed from a document** | **REFUTED** |

Baseline floor, before any edit: `CcpClient.Tests` Failed 4, Passed 2616, Skipped 2, **Total 2622**.
The 4 are the standing environmental family (`PointerCoexistenceTests` x3,
`BubbleCountCapabilityTests.THEOVERLAYIsUnharmedThroughTheWholeGame_MeasuredAtFourMoments`).
`check-warnings.mjs`: 0 warnings / 0 errors across 4 projects.
`client/tools/citations/detect.mjs` baseline: **TOTAL ROWS 167** (NEEDS-VERDICT 9, NEW-CITATION 152,
CITATION-GONE 0, UNRESOLVED 4, AMBIGUOUS 1, DELTA-MISMATCH 1); 659 dropped tokens, 49 of which
resolve only under `CCP.*`/`tests`.

## 2. What the divergence actually is (it is worse than "CCP.* plus residue")

Beyond the 1032 port files and the ~90 residue files, this branch **mutilated the shipping product**:

- **72 `Models/*.cs` files were MOVED out of the shipping project into `CCP.Core/Models/`** (rename
  detection confirms: `Models/AppSettings.cs -> CCP.Core/Models/AppSettings.cs`, `Achievement.cs`,
  `AchievementProgress.cs`, `Preset.cs`, `Session.cs`, ... plus `Services/Speech/MicFrontEnd.cs`).
- **Four `PackageReference`s were deleted from the shipping csproj**: `OpenAI-DotNet 8.6.2`,
  `SharpDX 4.2.0`, `SharpDX.DXGI 4.2.0`, `SharpDX.Direct3D11 4.2.0`.
- 30 shipping source files were edited, 13 `docs/*.md` deleted.

So the packet is right that this is a RESTORE, not a delete, and right that deleting `CCP.*`
piecemeal would not satisfy the rule.

## 3. How I would restore, and how the empty diff is proved

Two commands, run with `pwd` verified in the same invocation immediately before the destructive one
(the caution in the briefing):

```
git -C <worktree> rm -r --quiet ConditioningControlPanel/
git -C <worktree> checkout main -- ConditioningControlPanel/
```

`git checkout main -- <path>` alone is NOT sufficient and this is load-bearing: it copies `main`'s
entries in, but leaves index entries that `main` lacks (every `CCP.*` file) untouched. The `git rm -r`
first makes the set exact. `git rm` only touches TRACKED files, so ignored `bin/obj` under `CCP.*`
survive; I would then report `git clean -nd ConditioningControlPanel/` and delete only ignored build
output under the `CCP.*` directories, with `pwd` verified first.

Proof of completion, three independent checks:

1. `git diff main HEAD -- ConditioningControlPanel/` prints nothing (after commit).
2. `git diff main -- ConditioningControlPanel/` prints nothing (working tree, before commit).
3. `git ls-tree -r HEAD -- ConditioningControlPanel/ | sha256sum` equals the same for `main`.

## 4. Citations that move, per document, and how they are re-derived

Re-derivation method: the same parser the guard uses, run against `main`'s bytes; where the needle is
not on the claimed line, the tool prints EVERY line that carries it, and the row is re-anchored only
when there is exactly one and the surrounding code is the same statement. No nearby-line guessing.

### `client/docs/goon-game-census.md` — 3 of 52 §10.4 citations move, all by -1
| Key | now | on `main` |
|---|---|---|
| `lab-entry` `MainWindow/MainWindow.Lab.cs` | 202 | 201 |
| `stale-single-perk-comment` `MainWindow/MainWindow.Lab.cs` | 194 | 193 |
| `wrong-citation-target` `MainWindow/MainWindow.Lab.cs` | 183 | 182 |

Plus four prose restatements of the same three sites: §4.2 `MainWindow.Lab.cs:193-194` -> `:192-193`
(twice, at doc lines 331 and 713), §4.3 "the rationale it meant is at `:192-194`" -> `:191-193`, and
the §4.1 behaviour row `MainWindow/MainWindow.Lab.cs:198`, call at `:202` -> `:197` / `:201`.
Everything else in §10.4/§10.4.1/§10.4.2/§10.4.3/§10.4.4/§10.5/§10.6 re-derives unchanged: I compared
`Services/GoonGame` (25 files), `Resources/web/goon` (184), `Services/Media/Transfer` (6),
`Services/GoonGame/Rounds` (5) and every §10.3 path across both trees and they are byte-identical.

**No referent is gone from `main`** for this document. §4.3's finding survives the restore: on `main`
lines 182-186 of `MainWindow.Lab.cs` are still the Inspection Bureau catch block, so the xaml's
citation is still wrong about which feature it names.

### `client/docs/fyp-census.md` — 0 citations move, but ONE CONSUMER APPEARS
No line-pinned citation table is enforced here. The consumer sweep re-derives to **17 files on
`main`, not 16**: `Models/AppSettings.cs` returns to the shipping project and carries
`FypGhostOverlay` (its line 3193) and `FypOnlineCoordinator` (line 3247). The document needs a `C17`
row. **See §5 — the document edit alone cannot make the guard green.**

### `client/docs/haptic-limb-census.md` — 4+ citations move, and this document is OUT OF SCOPE
`Services/Subliminal/BouncingTextService.cs` loses a line on restore. Its candidate-line set stays at
exactly 80 (matching `ExpectedCandidateLines`), but the KEYS move:
`:516` (site 18, `App.Haptics?.BouncingTextBounceAsync()`) -> `:515`; `:515` (noise row) -> `:514`;
`:519` -> `:518`; `:534` -> `:533`. That reds
`HapticSiteCensusTests.EveryCitedUpstreamLineStillCarriesItsRecordedNeedle` and
`.EveryHapticLineInTheFamilyIsAccountedFor_RederivedFromTheShippingBytesAndNotFromTheDocument`.
Fixable in the document — but `client/docs/haptic-limb-census.md` is not in this packet's File Scope.

### `client/docs/trainer-card-census.md` — its `CCP.Core/Models/AchievementProgress.cs` citation loses its referent
Also out of this packet's File Scope.

## 5. The blocker: three facts that no document edit can reach

`client/tests/**` is must-not-change in its entirety, and three of the five red facts have the wrong
value or the wrong PATH baked into the TEST:

1. `FypCensusTests.TheConsumerSetIsRederivedFromTheShippingBytes_SoTheSurfacesReachCannotDriftUnnoticed`
   asserts `Assert.Equal(ExpectedConsumerFiles, report.ConsumersOnDisk.Count)` with
   `private const int ExpectedConsumerFiles = 16` (`FypCensusTests.cs:71`). `main` holds 17. Adding
   the `C17` row fixes `ConsumersMissingFromCensus` and then breaks `ConsumerRows.Count == 16`;
   omitting it breaks `ConsumersMissingFromCensus`. Boxed either way.
2. `TrainerCardCensusTests.TheTwoRaiseSites_DisagreeAboutNormalising_AndTheDistinctSetIsOrdinal`
   reads `ReadShippingFile(reference.Wpf, "CCP.Core/Models/AchievementProgress.cs")`
   (`TrainerCardCensusTests.cs:213`). That path does not exist on `main`.
3. `GradedRunAwardsTests.TheUpstreamClauseOrder_IsStillEmptyThenAddThenCount` reads
   `ReadRepoFile("ConditioningControlPanel/CCP.Core/Models/AchievementProgress.cs")`
   (`GradedRunAwardsTests.cs:466`) behind an `Assert.True(File.Exists(...))` that by its own comment
   "never skips".

The fix for all three is a re-derivation, not a weakening, and it is three tokens:
`16` -> `17`, and `CCP.Core/Models/AchievementProgress.cs` -> `Models/AchievementProgress.cs` twice.
I verified against `main`'s bytes that every OTHER assertion in those two facts still holds exactly:
the clause order, `PerfectedQuizCategories.Count >= HonorRollCategories`, the verbatim declaration
`public HashSet<string> PerfectedQuizCategories { get; set; } = new();`, the `catDef?.Id ??` shape,
the absent `ToLowerInvariant` in the raise, and all five §9.3 thresholds
(`HonorRollCategories 3`, `TeachersPetPasses 25`, `HeldBackFailStreak 3`, `TopMarksPercent 90.0`,
`MaxPinnedAchievements 4`).

**I am not making those edits without an explicit scope decision.** Both the constitution's file-scope
rule and this packet name `client/tests/**` as untouchable.

## 6. The unenforced cost the packet does not price

The restore moves lines in 30 shipping files. Citations in `client/docs/**` and `client/src/**` that
sit at or after the first changed line: **490 occurrences across 18 files**. Most shift by -1 (a
one-line header comment this branch added), but the large ones do not:
`Services/Quiz/QuizService.cs` -120 past line 20, `Chaos/ChaosImagePool.cs` -36,
`Services/UI/DisplayChangeCoordinator.cs` -20, `Services/CatalogueLookupService.cs` -18,
`Services/Quiz/PopQuizService.cs` -12 past 271, `Services/Content/ContentPackService.cs` -11 past
1472, `Services/Haptics/IHapticProvider.cs` -7, `Windows/QuizWindow.xaml.cs` +3 past 567.
Separately, **149 citations into `CCP.*` across 46 files** (37 under `client/src/**`, 9 under
`client/docs/**`) lose their referent entirely.

`client/src/**` is must-not-change here, so those rot by construction. `detect.mjs` is not on the
floor, so none of it reds anything — which is exactly why it is written down here.

## 7. Recommendation

Restore is correct and I can execute it. It needs ONE of:

- **(A)** widen File Scope by three tokens in `client/tests/` (the two `AchievementProgress.cs` paths
  and `ExpectedConsumerFiles` 16 -> 17) plus `client/docs/haptic-limb-census.md` and
  `client/docs/trainer-card-census.md`; or
- **(B)** a re-authored packet that prices the real cost.

Option (A) is a re-derivation against the shipping bytes in every case, which is what those guards
say they are for; it weakens nothing and removes no assertion.
