---
name: mechanical-port-work
description: "Execution discipline for pre-planned Avalonia port work, designed so a smaller/mechanical-tier model can safely continue the port without smart-model judgment. Use this skill whenever picking up ANY item from docs/model-handoff-queue.md, any chaos slice S5-S9 from docs/chaos-run-engine-port-plan.md, any task-board row, or when the user says 'continue the port', 'next slice', 'work the queue', or 'pick up where the smart model left off'. The skill's core promise: every decision that needs judgment was already made and written down with WPF file:line citations, your job is faithful execution, gates, and knowing when to STOP instead of improvising."
---

# mechanical-port-work

You are executing pre-planned work. The planning model already made the judgment calls
and wrote them down with citations. **Faithful execution beats creativity here.** If you
find yourself inventing a design, you are off the rails, go to "When to STOP".

## The work queue

`ConditioningControlPanel/docs/model-handoff-queue.md` is the single ordered queue.
Take the TOP unblocked item unless the user names one. Each item links to its full spec
(usually `docs/chaos-run-engine-port-plan.md` or the task board). Read the spec BEFORE
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
2. **Read the spec** it links to, then the cited WPF lines (sliced reads).
3. **Implement exactly what the spec says.** Match existing naming/logging patterns in
   the file you're editing. Cite WPF `File.cs:line` at each ported decision point.
4. **Add the tests the spec demands** (in `tests/CCP.Core.Tests/`, xUnit v3; look at a
   neighboring test file for conventions).
5. **Run ALL gates** (below). All must pass. A gate failing twice on the same cause is
   a STOP condition.
6. **Update trackers**: evidence row in the plan doc / task-board row / queue-doc
   status, same commit.
7. **Commit** with a message that states what, the WPF citations, and the gate results.

## Gates (copy-paste; run from repo root E:/Code/Conditioning-Control-Panel)

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

## When to STOP (file a blocker instead of improvising)

Append a row to the "Blocked / Questions" ledger in `docs/model-handoff-queue.md`
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
