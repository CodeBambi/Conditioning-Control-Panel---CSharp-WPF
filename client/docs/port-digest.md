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
