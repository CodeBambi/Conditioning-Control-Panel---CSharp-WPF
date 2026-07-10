---
name: mechanical-port-work
description: "Execution discipline for pre-planned Avalonia port work, designed so a smaller/mechanical-tier model can safely continue the port without smart-model judgment. Use this skill whenever picking up ANY tier-tagged live row from docs/avalonia-migration-task-board.md (the single mechanical work queue), or when the user says 'continue the port', 'next slice', 'work the queue', or 'pick up where the smart model left off'. The skill's core promise: every decision that needs judgment was already made and written down with WPF file:line citations, your job is faithful execution, gates, and knowing when to STOP instead of improvising."
---

# mechanical-port-work

You are executing pre-planned work. The planning model already made the judgment calls
and wrote them down with citations. **Faithful execution beats creativity here.** If you
find yourself inventing a design, you are off the rails, go to "When to STOP".

## The work queue

`ConditioningControlPanel/docs/avalonia-migration-task-board.md` is the single mechanical work queue (its tier-tagged live rows).
Take the TOP unblocked item unless the user names one. Each item links to its full spec
(usually the claimed task-board row's detail doc; see `docs/docs-index.md` for the map). Read the spec BEFORE
touching code.

## Iron rules (violating any of these is a failed task)

1. **Forbidden zones, never edit:**
   - `ConditioningControlPanel/Services/**`, `MainWindow/**`, `Views/**`, `AvatarTube/**`
     at the WPF-head level (everything OUTSIDE `CCP.Core`/`CCP.Avalonia*`/`tests`) -
     the WPF head is read-only ground truth.
   - `CCP.Avalonia/Compositor/**` internals.
   - `SmokeTestRunner.cs`.
   - `CCP.Core/Services/Chaos/BubbleEngine.cs`, the engine is COMPLETE as of S4b-4.
     No remaining planned slice touches it. If your task seems to need an engine change,
     that is a STOP condition.
2. **Trust WPF source over every doc** (plans, contracts, this skill). When they
   disagree, the WPF code wins; cite `File.cs:line` in a comment and note the
   discrepancy in your report/commit.
3. **Sliced reads for big files.** WPF offenders >100KB (BubbleService 230KB,
   ChaosModeService 172KB, AppSettings 192KB, ...): grep for the member, then read only
   the enclosing range. Never open them whole.
4. **Additive interfaces only**: new members on Core interfaces get default
   implementations (DIMs) with safe no-op bodies so fakes/tests compile unchanged.
5. **No TODO/placeholder/partial code.** If something can't be finished, write
   `// (plan: <doc> <item>) <reason>` at the site AND file it in the queue doc's
   Blocked/Questions ledger.
6. **One work item per commit**, `git commit --no-verify`, evidence row updated in the
   SAME commit. Conventional Commit prefix (`feat(av):`, `fix(av):`, `docs:`).
7. **Never let the Core test count drop.** Current floor: **426** (raise it in the plan
   doc when you add tests; never lower it).

## Execution loop (per item)

1. **Claim**: note the item as in-progress in the queue doc (single-line edit).
2. **Read the spec** it links to, then the cited WPF lines (sliced reads). If the cites
   are not enough to pin the behavior, dispatch the `wpf-archaeologist` agent with the
   feature/symbol and use its contract instead of reading the giant WPF files yourself.
3. **Implement**. Items rated XS/S: edit directly, exactly what the spec says. Items
   rated M/L: dispatch the `port-slice-executor` agent with the full item spec (what to
   change, WPF cites, required tests) and audit its report when it returns. Match
   existing naming/logging patterns. Cite WPF `File.cs:line` at each decision point.
4. **Add the tests the spec demands** (in `tests/CCP.Core.Tests/`, xUnit v3; look at a
   neighboring test file for conventions).
5. **Audit before committing**: for state-mutating, economy, lifecycle, or engine-adjacent
   items (anything the queue rates Medium risk or higher), dispatch the
   `port-parity-auditor` agent on the uncommitted diff and act on its
   SHIP / FIX-FIRST / STOP verdict. FIX-FIRST findings get fixed; STOP means the
   blocker protocol below.
6. **Run ALL gates** (below). All must pass. A gate failing twice on the same cause is
   a STOP condition.
7. **Update trackers**: evidence row in the plan doc / task-board row / queue-doc
   status, same commit.
8. **Commit** with a message that states what, the WPF citations, and the gate results.

## Project agents (in .pi/agents/, dispatch via the subagent tool)

- `wpf-archaeologist`: read-only WPF contract extraction with sliced reads. Input: a
  feature or symbol. Output: cited behavioral contract.
- `port-slice-executor`: implements one work item under the iron rules; runs fast gates;
  never commits. Input: the full item spec. Output: files-changed report.
- `port-parity-auditor`: adversarial audit of the uncommitted diff vs WPF ground truth.
  Output: Verified/Deviation/Broken rows + SHIP / FIX-FIRST / STOP.

## Gates (run from repo root E:/Code/Conditioning-Control-Panel)

THE tool: `bash ConditioningControlPanel/tools/run-gates.sh` (all 4 gates, prints PASS/FAIL per gate, exit 0 = safe
to commit). `bash ConditioningControlPanel/tools/run-gates.sh --fast` skips the smoke test for quick iteration; the
FULL script must pass before every commit. Floors (test count 426, smoke 44 tabs /
Findings 5) live at the top of that script, ONE place; raise them when adding tests,
never lower. The script also detects the known smoke flake and tells you what to do.
Manual equivalents if the script is unavailable:

```bash
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -clp:ErrorsOnly   # 0 errors
dotnet build ConditioningControlPanel.sln -clp:ErrorsOnly                # 0 errors
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj  # ALL pass, count >= 426
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --smoke-test
```

Smoke pass = `Tabs visited: 44`, `Findings: 5`, no `Unhandled exception` in output.
(21 first-chance exceptions are benign OAuth noise.) KNOWN FLAKE: a rare unhandled
`InvalidOperationException` in `SolidColorBrush.SerializeChanges` (cross-thread brush
race, filed on the board), if you hit it, re-run the smoke test ONCE; a clean re-run
passes the gate. Two crashes in a row = STOP condition.

## Codebase conventions (follow these; do not copy the exceptions)

- **Service resolution**: new code uses CONSTRUCTOR DI (registrations in
  `CCP.Avalonia/ServiceCollectionExtensions.cs`). The codebase also contains
  `App.Services?.GetService<T>()` locator calls, `CoreApp` statics (some typed
  `object?`), and static chaos facades (`ChaosMeta`, `RevealService`,
  `AvaloniaChaosApp`), those exist ONLY for WPF-parity chaos code. Never add new
  `CoreApp` object-typed properties. Never use `dynamic`.
- **Naming trap**: `CCP.Avalonia/Services/AvaloniaHeadStubs.cs` and
  `CCP.Avalonia/Chaos/AvaloniaChaosStubs.cs` contain ZERO stubs, they hold the REAL
  chaos service, avatar/bark services, and the chaos model/data layer (queue items
  split them; until then do not treat them as disposable scaffolding).
  `Localization/LocExtension.cs` contains class `StrExtension` (used as `{loc:Str}` in
  87 axaml files), not dead code.
- **Tests**: default `[Fact]`; use `[AvaloniaFact]` ONLY when the code under test needs
  the Avalonia dispatcher/UI thread. New tests prefer public/internal seams (see
  `ChaosScoringTests.cs` pattern); do not rewrite existing reflection-based tests.
- **Version guardrails (verified 2026-07-04)**: this repo pins the Avalonia matrix at
  **12.0.5** (all heads + `Avalonia.Headless.XUnit`) and `Avalonia.Controls.DataGrid`
  **12.0.1** (which requires >= 12.0.5; they pair). `MessageBox.Avalonia` 12.0.0 is
  third-party with its own cadence, leave it. DISTRUST any Avalonia advice not
  explicitly v12: v11 answers are actively wrong (renamed APIs, changed
  windowing/compositor). Do not adopt 12.1/13 guidance. Any version change: bump the
  WHOLE Avalonia matrix together, run all gates, and only with a named changelog fix.
  The rare `SolidColorBrush.SerializeChanges` crash is an app-side off-UI-thread brush
  mutation (no matching upstream issue exists), never "fix" it by upgrading Avalonia.

## When to STOP (file a blocker instead of improvising)

Append a row to the "Blocked / Questions" ledger in `docs/avalonia-migration-task-board.md`
(item, what you found, exact file:line, why you stopped), leave the working tree CLEAN
(revert or stash uncommitted work), and tell the user. STOP conditions:

- The spec contradicts the code you're reading (either WPF or port side).
- The task seems to require editing a forbidden zone.
- A gate fails twice for the same root cause.
- The WPF semantics are still ambiguous AFTER reading the cited lines and grepping for
  the symbol's other uses.
- Your change would reduce the test count or change a persisted-settings/JSON schema.
- You need a new NuGet package or a new external endpoint.

Filing a good blocker is a SUCCESSFUL outcome. Guessing is not.

## Related skills

- `wpf-parity`, how to extract a behavioral contract when a spec cite isn't enough.
- `port-feature`, full workflow when an item is a UI port (AXAML work).
- `avalonia-research`, MANDATORY before touching any Avalonia API you're unsure about
  (v12 is new; training data lies).
- `unified-compositor-engine`, background if an item touches layer callers (never
  Compositor internals).
