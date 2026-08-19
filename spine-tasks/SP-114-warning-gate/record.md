# SP-114 — record

Branch `lane/SP-114-warning-gate`, base `4332b8b9`, worktree
`.claude/worktrees/agent-a47b44268bce8d064`.

Floor: pin **1930 unit / 117 headless**; observed **1938 unit / 117 headless**; declared delta
**+8 unit / +0 headless** (`floor-delta.json`). 1938 = 1930 + 8 and 117 = 117 + 0, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-114-warning-gate`. **The floor run
therefore exits 1 on the declared total drift and on nothing else** — that is the expected state
under the multi-lane delta mechanism, not a failure. Two skips, both pre-existing and both already
pinned (`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); **none added, and
`allowedSkips` was never opened**. `client/tests/floor/floor.json` was never opened.

Build: **0 warnings / 0 errors**, and for the first time in this port that sentence is the output of
a gate rather than of an eye — `WARNING GATE OK (SP-114): 0 warnings, 0 errors across 4 project(s)
[CcpClient.Desktop, CcpClient.HeadlessTests, CcpClient.Tests, CcpVerify] in Debug, forced
non-incremental.` (`evidence/gate-03-final-clean.txt`, full unfiltered build log at
`evidence/gate-final/build.log`).

---

## 0. THE HEADLINE — a second false-green mechanism, independent of SP-113's filter

> **The project's own mandated build command reports `0 Warning(s)` on a tree that contains live
> warnings, whenever the previous build already compiled it.** MSBuild skips `CoreCompile` for an
> up-to-date project, so the compiler never re-emits the diagnostic. A warning is a property of the
> COMPILATION, not of the assembly.

Measured on the base tree BEFORE any gate existed, every build read unfiltered, logs committed:

| # | command | tree | result | evidence |
|---|---|---|---|---|
| 1 | `dotnet build client/CcpClient.sln -c Debug --nologo` (cold, no `bin/`) | base `4332b8b9`, unmodified | **0 Warning(s) / 0 Error(s)** | `measure-01`… see `build-01-cold.log` |
| 2 | same | base + one induced `CS0219` | **1 Warning(s)** | `measure-01-warning-first.log` |
| 3 | **same command again, source unchanged** | **the same tree as run 2** | **0 Warning(s)** | `measure-02-warning-incremental.log` |
| 4 | same + `--no-incremental` | the same tree again | **1 Warning(s)** | `measure-03-no-incremental.log` |

Run 3 is the finding. It reproduced three times across the packet (`measure-02`,
`bite-02-plain-build-says-zero.log` with TWO live warnings, `bite-04b-second-plain-build.log`),
and it means a lane that builds twice — or whose build was already current from an earlier step in
the same session — reads a clean zero off a compilation that did not happen. **This is why the gate
forces `--no-incremental`, and why a warning gate without that flag would be vacuous.**

## 1. BEFORE / AFTER, IN COUNTS

| | before (`4332b8b9`) | after |
|---|---|---|
| gates that observe build warnings | **0** | **1** (`client/tests/floor/check-warnings.mjs`) |
| occurrences of the string `warning` (any case) in `check-floor.mjs` | **0** | **0** — the floor was not touched |
| projects whose warnings are mechanically observed | **0** | **4 of 4** in `client/CcpClient.sln`, discovered from the sln, missing-project = red |
| `0 warnings` claims in the owner-facing digest, none mechanically checked | **31** | 31 historical + every future one gated |
| `0 warnings` claims across `spine-tasks/` (files / occurrences) | **53 / 63** | same history, gated forward |
| pinned facts about warning handling | **0** | **8** |
| pinned warning-suppression sites under `client/` | **0** | **10** (by file and code) |
| induced real warnings the mandated command reported | 1 of 3 (only the compile that produced it) | — |
| induced real warnings the GATE reported | — | **3 of 3**, plus 1 real compile error |

## 2. WHERE THE GATE LIVES, AND WHY IT DOES NOT COMPROMISE THE STALENESS GUARD

`client/tests/floor/check-warnings.mjs`, run as `node client/tests/floor/check-warnings.mjs`. Beside
`check-floor.mjs` because that is where this port keeps its pre-land mechanical gates.

**`check-floor.mjs` was not modified at all.** `git diff 4332b8b9 -- client/tests/floor/check-floor.mjs`
is empty. It still passes `--no-build`, still calls `assertBuildIsFresh`, and still contains no build
invocation — the very defect `client/docs/port-lessons.md:204` records (run standalone it measures
the LAST BUILD, not the tree; at wave-30 it reported 1022 against a source tree containing 1018).

The warning gate builds because **building is the thing it observes**, into the same solution,
configuration and `bin/obj` the floor then reads. So in the prescribed order

```
node client/tests/floor/check-warnings.mjs
node client/tests/floor/check-floor.mjs
```

the warning gate **is** the "always build immediately before the gate" step `port-workflow.md`
already required, and it therefore *satisfies* the floor's freshness precondition instead of eroding
it. That was verified end to end: after the warning gate ran, the floor's stale-build guard did not
fire on either final run.

**This is pinned, not merely promised.** `WarningGateGuardTests.TheTestFloorStillRunsNoBuild_AndKeepsItsStaleBuildGuard`
extracts every `"dotnet", ["<verb>"` invocation from `check-floor.mjs` and requires the verb set to be
exactly `[test]`, plus the presence of `--no-build`, `assertBuildIsFresh` and the `STALE BUILD`
message. Proven to bite (§3, B5).

**Hazard, named rather than hidden.** The gate rebuilds the shared `bin/obj` of the worktree it runs
in, so it must never run concurrently with a floor run **in the same worktree** — the floor's
`--no-build` runner would be loading assemblies while they are replaced. Across worktrees each lane
has its own output, and `client/tools/gate/with-slot.mjs` already bounds machine-wide build
concurrency. Stated in the gate's own header, in `port-workflow.md` and in `verification-harness.md`.

## 3. HOW IT WAS PROVEN TO BITE — executed, not asserted

Every red below was produced by inducing a REAL diagnostic (never a fake string, never a mocked
stream), and every probe was removed afterwards with the restore proven.

**B1 — a compiler warning and the exact SP-113 analyzer warning, together.**
`Sp114BiteProbe.cs` in `CcpClient.Tests` with an unused local and `Assert.Equal(1, items.Count)`.
Gate exit **1**, both named with file, line, code and project
(`evidence/bite-01-gate-red.txt`):

```
WARNING GATE FAILED (SP-114):
  2 BUILD WARNING(S):
      [CS0219] ...Sp114BiteProbe.cs(11,13): warning CS0219: The variable 'unused' is assigned but its value is never used [...]
      [xUnit2013] ...Sp114BiteProbe.cs(17,9): warning xUnit2013: Do not use Assert.Equal() to check for collection size. ...
```

**B2 — the vacuity check: the mandated command called the same tree clean.** Immediately after B1,
with both warnings still in the file, `dotnet build client/CcpClient.sln -c Debug --nologo` printed
`0 Warning(s) / 0 Error(s)` and exited 0 (`evidence/bite-02-plain-build-says-zero.log`). That is the
gate earning its keep in one pair of runs.

**B3 — SP-113's filter, applied to the gate's own captured log** (`evidence/bite-03-sp113-filter-exhibit.txt`).
The log carries **2** lines mentioning `xUnit2013`; `grep -E "error|warning CS|Build succ"` can see
**0** of them, and what it does show a reader is the `CS0219` line and `Build succeeded.`. On a tree
whose only warning was the analyzer one, that filter shows `Build succeeded.` and nothing else.

**B4 — a project the change never touched.** `Sp114BiteProbe.cs` in `CcpVerify` (under
`client/tools/`, the project most easily forgotten). Plain build #1 with the source change: `1
Warning(s)`. Plain build #2, unchanged tree: **`0 Warning(s)`**. Gate over that same unchanged tree:
exit **1**, `[CS0219] ...CcpVerify\Sp114BiteProbe.cs(9,13)` (`evidence/bite-04a/b/c`). This is the
proof that `--no-incremental` re-analyses projects the edit did not touch.

**B5 — the central trap's own pin.** A `execFileSync("dotnet", ["build", SLN_PATH, "-c", "Debug"])`
was appended to `check-floor.mjs`; `TheTestFloorStillRunsNoBuild_AndKeepsItsStaleBuildGuard` failed
with `check-floor.mjs invokes dotnet with verb(s) [test, build] — the test floor must only ever run
'dotnet test'...` (`evidence/bite-05-floor-builder-pin-reds.txt`). Restored with
`git checkout --`; `git diff HEAD -- client/tests/floor/check-floor.mjs` empty afterwards, so
byte-identical.

**B6 — the census, and the gate's blindness, in one experiment.** `Sp114CensusProbe.cs` added a live
`CS0219` **wrapped in a pragma disable**. `WarningSuppressionCensusTests` failed naming it:
`NEW, UNPINNED suppression(s): client/tests/CcpClient.Tests/Sp114CensusProbe.cs::CS0219 ... observed
11, pinned 10` (`evidence/bite-06-census-reds.txt`). On that same tree the warning gate ran **green**
(`evidence/bite-07-gate-is-blind-to-a-suppression.txt`). **The boundary in §5 is therefore
demonstrated, not claimed:** a suppressed warning is invisible to the gate by construction, and the
second instrument is what sees it.

**B7 — the error path and the missing-project check, on a real build.** `Sp114ErrorProbe.cs`
introduced `error CS0103`. Three independent fail-closed reasons fired at once
(`evidence/bite-08-gate-red-on-error.txt`): `dotnet build exited 1`, `project(s) ... that produced NO
output line in this build: CcpClient.Tests`, and the named `[CS0103]`.

**Restores.** All five probe files were untracked and were deleted; `check-floor.mjs` was restored
with `git checkout --`. `git status --porcelain` afterwards lists only this packet's intended files,
and the gate was re-run green on the delivered tree (`evidence/gate-03-final-clean.txt`).

**The self-test** (`node client/tests/floor/check-warnings.mjs --self-test`, no build) covers the
codes that cannot be induced cheaply — `NU1701`, `MSB3277`, `AVLN3001`, `MSB4078`, a codeless
warning, an indented line — plus ten negatives that must NOT match (`0 Warning(s)`, `1 Warning(s)`,
project output lines, a passing test name containing the word "Warning", an `error` line), dedup in
both directions, and four `evaluate()` fail-closed shapes. It carries SP-113's retired filter as an
executable exhibit and fails if that filter ever matches the xUnit2013 line.

## 4. WHAT THE GATE DOES

- Runs `dotnet build <sln> -c Debug --nologo --no-incremental -tl:off` with
  `DOTNET_CLI_UI_LANGUAGE=en`, captures stdout+stderr **whole**, and writes the complete unfiltered
  log to disk **before** reading anything out of it.
- Makes **two independent readings that must agree**: every canonical MSBuild diagnostic line
  (`warning <CODE>:`, code as a wildcard — the direct correction of `warning CS`), deduplicated by
  exact line; and MSBuild's own `N Warning(s)` / `N Error(s)` summary. A disagreement is a failure,
  never a reconciliation; it can only ever make the gate redder.
- Fails closed on every "I could not tell": non-zero build exit, a missing or duplicated summary
  counter line, a solution project that produced no output line, and an unreadable solution. **It
  never reports a count it did not read.**
- Reports whether NuGet restore no-opped, so the reader knows whether restore-time warnings were
  re-evaluated on that run.

`-tl:off` and `DOTNET_CLI_UI_LANGUAGE=en` were probed and accepted on this SDK
(`evidence/measure-04-tloff.log`, SDK 10.0.303 / MSBuild 18.6.14).

## 5. THE BOUNDARY — what the gate cannot see

Also written into `client/docs/verification-harness.md`.

1. **A suppressed warning, by construction.** `NoWarn`, an inline pragma disable, a code-analysis
   suppression attribute, an `.editorconfig` severity of `none`, a lowered `WarningLevel` — all act
   before MSBuild prints anything. **Demonstrated** in B6: the gate ran green over a live `CS0219`.
   The mitigation is a second, lexical instrument, `WarningSuppressionCensusTests`, pinning the
   census measured at base: **1 `NoWarn` (`CcpClient.Desktop.csproj` → `AVLN3001`, deliberate per
   `CLAUDE.md`), 9 inline `CS0067` pragma sites, 0 suppression attributes, 0 `GlobalSuppressions.cs`,
   0 `.editorconfig` from `client/` up to the repository root, 0 project-level warning policy
   (`TreatWarningsAsErrors`, `WarningLevel`, `AnalysisLevel`, `EnableNETAnalyzers`, `RunAnalyzers`)**.
   It is lexical and it judges nothing about whether a pinned suppression is justified; it only makes
   adding one impossible to do quietly.
2. **An `.editorconfig` ABOVE the repository root.** Roslyn's discovery walks to the drive root; the
   census stops at the repository root, deliberately, because a file outside the repository is not
   something an in-tree red could act on. Named in the test's own source and in the harness doc.
3. **Only `client/CcpClient.sln`, only `Debug|Any CPU`, only `net10.0`.** The Release/RID publish
   path (`client/tools/publish/publish.ps1`) is NOT covered, and neither legacy tree
   (`ConditioningControlPanel/`, `CCP.*`) is in scope.
4. **Only this SDK's analyzer set.** Measured on SDK 10.0.303 / MSBuild 18.6.14. A different SDK,
   or different package versions, can emit more or fewer diagnostics; a green here is not a claim
   about another machine.
5. **Restore-time (`NU*`) warnings on a run where restore no-ops.** `--no-incremental` forces
   compilation, not restore. Both final runs reported the no-op, which is why the note is printed
   rather than assumed.
6. **The two readings share one source.** They are independent parses, not independent builds. A
   defect in MSBuild's own reporting would fool both.
7. **It observes a build.** It discharges no `draw-verified` or `presentation-verified` claim and
   proves nothing about behaviour, rendering, timing, audio, focus, input, animation or window
   behaviour.
8. **A lexical guard binds text, not behaviour.** The six `WarningGateGuardTests` facts bind the
   gate's arguments, its discovery, its regex (lifted out of the shipped source and executed) and
   the documents that name it. They cannot prove the gate's runtime is correct; B1–B7 are what does
   that, and they were run once each, by hand, on this machine.

## 6. WHAT I FOUND IN THE CURRENT TREE

- **The build at `4332b8b9` is genuinely 0 warnings / 0 errors**, read unfiltered on a COLD build
  with no `bin/` present (`evidence/build-01-cold.log`, 15 lines, read whole). The packet's premise
  held; no product change was needed and `client/src/**` was not touched.
- **`check-floor.mjs` contains the string `warning` zero times, at base and now.** It never had any
  warning handling, exactly as the packet said.
- **The suppression census is small and every site is reasoned.** All 9 pragma sites are `CS0067`
  ("event never used") on deliberately inert implementations of an interface event, each with an
  inline reason already written by its author. The single `NoWarn` is `AVLN3001` and `CLAUDE.md`
  records why. Nothing here looked like a warning silenced to make a gate pass.
- **There is no `.editorconfig` anywhere between `client/` and the repository root**, so no analyzer
  severity is being lowered out of sight today.
- **No divergence found in product code.** None to file.

## 7. WHAT THIS WORK DOES NOT PROVE

- **It does not re-validate any historical "0 warnings" claim.** 31 in the digest and 63 across
  `spine-tasks/` were made before any gate existed; this packet gates the future, and says nothing
  about whether those particular trees were clean.
- **It proves nothing about the Release or publish configuration**, about the two legacy trees, or
  about another machine's SDK.
- **It proves nothing about a suppressed warning's justification** — only that a new suppression
  cannot be added silently.
- **No headed, rendering, audio, input, focus, animation or window claim is touched.** A build gate
  discharges none of them, and a headless frame still discharges no headed gate.
- **The bite demonstrations were single runs each**, not a rate measurement. They establish that the
  gate CAN fail on each shape, not a frequency.
- **The floor number reported here is this machine's, measured twice** (`evidence/floor-01.txt`,
  `evidence/floor-02.txt`), both 1938/117 with the same two pinned skips.

## 8. FILES CHANGED

| File | Why |
|---|---|
| `client/tests/floor/check-warnings.mjs` | NEW. The gate: forced non-incremental build, whole-stream capture, two agreeing readings, fail-closed everywhere, `--self-test` corpus carrying the SP-113 line. |
| `client/tests/CcpClient.Tests/WarningGateGuardTests.cs` | NEW. Six facts: the floor still does not build and keeps its stale guard; the gate forces `--no-incremental`; its build args weaken no warning; it covers every solution project; its own shipped regex matches the xUnit2013 line the retired filter could not; the gate is named in the workflow, the harness and the auditor prompt. |
| `client/tests/CcpClient.Tests/WarningSuppressionCensusTests.cs` | NEW. Two facts pinning the 10 suppression sites under `client/` by file and code, and the absence of any `.editorconfig`, suppression attribute, `GlobalSuppressions.cs` or project-level warning policy. |
| `client/docs/port-workflow.md` | The rule: run the warning gate, then the floor; never teach the floor to build; never silence a warning to pass; never quote a count from an unverified filter. |
| `client/docs/verification-harness.md` | The gate in tier 1, and the full boundary as a new evidence class. |
| `client/tools/port-audit-prompt.md` | The blind auditor now runs the gate instead of reading `dotnet build` output by eye — the exact reading that produced four false claims. |
| `spine-tasks/SP-114-warning-gate/floor-delta.json` | +8 unit / +0 headless. |
| `spine-tasks/SP-114-warning-gate/plan.md` | The plan checkpoint, written before the first gate edit. |
| `spine-tasks/SP-114-warning-gate/evidence/*` | Every build log and gate run behind every number above, unfiltered. |

`client/src/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`,
`ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**` and `.claude/**` were not touched.

## 9. THREE THINGS A REVIEWER SHOULD CHECK FIRST

1. **That the floor did not become a builder.** `git diff 4332b8b9 -- client/tests/floor/check-floor.mjs`
   must be empty, and `WarningGateGuardTests.TheTestFloorStillRunsNoBuild_AndKeepsItsStaleBuildGuard`
   must be the thing that would notice if it ever did.
2. **That nothing was silenced.** `git diff 4332b8b9` contains no new `#pragma`, no `NoWarn`, no
   `WarningLevel`, no `allowedSkips` entry, and the census pins the pre-existing 10 unchanged.
3. **That the bite reds are real diagnostics, not fixtures.** Every red in `evidence/bite-*` names a
   real file, line and code produced by an actual `dotnet build`, and each probe file is gone from
   the tree.
