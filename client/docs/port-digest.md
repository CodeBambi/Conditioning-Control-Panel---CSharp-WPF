# Port digest — owner's read-first log

Three lines per landed wave, newest last. Written by the phase that lands, so an unattended
run cannot bury named limits and owner questions inside ten records nobody opens.

Format:
- **LANDED:** what reached the tree.
- **DOES NOT PROVE:** the honest residual — the limit a reader would otherwise assume away.
- **OWNER:** the question or decision it raised, or `none`.

---

## Wave 19 — SP-062 (`7518c6a4`, 2026-08-12)

- **LANDED:** the SP-057 profile-isolation pin can no longer pass vacuously — loud `Assert.SkipWhen` at both checkpoints plus a probe-proven co-location fix for the process-env leak (`DisableParallelization` measurably does not serialize on this runner). Floor 892/35.
- **DOES NOT PROVE:** that the suite's signal is trustworthy in general. The tripwire fires only for an externally set override; in-suite leaks are prevented, not detected; a skip does not fail the contract gate, so detection still depends on a human comparing counts. The generalized vacuous-test class (conditional `return` shapes) was never swept — new P1 row filed.
- **OWNER:** ratification of the row. Also worth knowing: the wave-18 land shipped a RED base (an inventory disposition flipped without the field its guard requires) — found and repaired in-lane, lesson recorded.

## Wave 20 — SP-063 (`10c37650`, 2026-08-12)

- **LANDED:** your decree — injected test timeout budgets raised to one shared finite constant (`TestWait.InjectedBudget = 60 s`, ~75×), the two timeout-SUBJECT tests kept short and pinned, three inert budgets deleted, and one guard token added with a captured RED so the shape cannot return silently. 10 greens incl. a cold first-ever build; floor unchanged.
- **DOES NOT PROVE:** that the cold-start class is gone. A bigger number lengthens the fuse; it does not remove the time dependence. The class returns on a machine ~75× slower or if the constant is lowered, and the guard catches the option-assignment shape only (not a constant, a computed `TimeSpan`, or a method-argument bound). The deterministic alternative was set aside by decree, not refuted.
- **OWNER:** ratification. No new question.

## Wave 21 — SP-064 launched (`9d5f92c6`, 2026-08-13) — authoring phase, nothing landed yet

- **LANDED:** nothing product-side. Wave 21 was authored and its batch launched: SP-064 makes harness-only entry points refuse to start when `CCP_DATA_ROOT` is unset, so the SP-057 seam stops depending on someone remembering it. **Counts at this commit are unchanged: 892 unit / 35 headless / 0 skipped, 0W/0E** — this phase touched only packet and doc files.
- **DOES NOT PROVE:** anything about the gate; the worker has not run yet. Two things are already known limits of the design you approved: the row's own decree keeps demo flags exempt, so `--dtrh-demo --dtrh-auto-close 30` remains an unattended evidence-shaped run against your real profile, and the gate protects the data root only — WebView2/LibVLC writes under `%LOCALAPPDATA%`/`%TEMP%` were outside SP-057's claim and stay outside this one.
- **OWNER:** two questions. (1) The demo+auto-close hole above — close it in a follow-up row, or accept it as the price of keeping demo flags human-usable? (2) **Your new blind audit runs `dotnet build` + both test projects in the MAIN checkout whenever a phase moves HEAD — including the authoring phase that just launched a batch, so the audit and the lane build/test concurrently.** It should be sound (worktrees have their own obj/bin), but a flaky or port-contended run now writes `.spine/STOP` with a batch in flight. Consider skipping the audit when `spine status` is executing, or auditing after the land phase only.

