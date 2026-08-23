# Greenfield Client Digest

This is a concise, current-state summary for owner review. It records landed outcomes and open
decisions; it is not an execution diary, workflow archive, or substitute for the task board.

## Recording A Land

Add one entry only when an integrated change materially changes the product or its verified
capability. Use this format:

```markdown
## YYYY-MM-DD - Outcome

- Landed: the observable behavior and the verification that supports it.
- Does not prove: the platform, privacy, interaction, or integration evidence still missing.
- Owner decision or blocker: the exact unresolved choice, if any.
```

Keep entries short and remove superseded claims when the task board or current source evidence
changes them. Do not record worker identities, model choices, branch names, task identifiers,
workflow counts, runner output, or local archive paths.

## 2026-08-23 - The queue was not the remaining work, and the gate was measuring itself

- Landed: a **completeness census**. A cluster-by-cluster sweep of the shipping product against
  `client/src` and against the board found **115 user-visible behaviours absent or partial with no row
  covering them**, out of 161 examined - whole subsystems, including the scripted Session Rack, XP and
  levels, Quests, the skill tree, Training Programs, the mod system and the microphone/speech stack.
  Concretely: 5 of 25 WPF tabs and 1 of 42 dialogs have a client surface. Each entry carries a WPF
  citation, the grep that proved absence, and a measured size, in
  `client/docs/port-completeness-census.md`; the board gained ten P2 SCOPE rows, not work items.
- Landed: the cause of that gap is traceable rather than mysterious. `wpf-surface-reachability.md` was
  1,906 lines carrying a divergence register with 326 anchors that enumerated exactly these gaps;
  commit `1bdf998e4` cut it to 95 lines and deleted the register. 28 client source files still cite
  anchors that no longer resolve.
- Landed: **the tray right-click no longer costs the process the topmost band.** `SetForegroundWindow`
  on the tray's HIDDEN owner window cannot succeed, and the refused request left this process unable to
  place ANY later window in the topmost band - the overlay, the glyph surface and the video surface.
  Guarded on `IsWindowVisible`, proven by inversion.
- Landed: **the floor gate stopped measuring its own runner.** It now runs each project as the xunit v3
  assembly with `-trx` rather than through `dotnet test`. Nothing was relaxed: the result-list
  anchoring, staleness checks, bad-outcome refusals, exact-total pin and name-anchored skips all still
  run, and it still never retries.
- Landed: `MotionLevel {Full,Reduced,Off}` drives hosted-page motion from the user's setting rather
  than the OS, at all five hosted surfaces through one helper, with the OS read and logged but never
  obeyed.
- Landed: a guard for the two wait shapes that carry no timing literal - unbounded joins and injected
  budgets - including the bare blocking wait that an independent review caught it missing.
- Landed: upstream **v6.8.3 -> v6.8.4** merged with zero conflicts, read-only boundary verified by
  subtree object identity. Its whole delta is Arcademy, and the load-bearing half is that upstream
  SHUT THE ARCADEMY DOOR: porting that surface visible would ship a feature upstream is deliberately
  withholding.
- Does not prove: no headed evidence was taken for the motion setting, and none for Linux. The port's
  Linux legs remain unrun even though a WSL2 host with WSLg now exists. The 115 census entries are
  measured, not scoped - none of them is a decision yet.
- Owner decision or blocker: the four real-desktop band facts have TWO causes and only one is removed.
  VSTest was one and is gone; DESKTOP CONTENTION is the second and still reds them under several
  concurrent headed suites. That is the "or explicitly machine-gated" half of its row and it is open.

## 2026-08-22 - Upstream v6.8.1 -> v6.8.3 merged, and the read-only boundary given a guard

- Landed: 74 upstream commits merged with zero conflicts; `ConditioningControlPanel/` byte-identical
  to `main` by subtree object identity, port untouched. The four guards the merge moved were
  repaired in the same push, every citation re-derived from bytes rather than from the prior
  session's already-derived table.
- Landed: the read-only rule for `ConditioningControlPanel/` now has enforcement. It had none, had
  drifted 1180 files once, and had just drifted again via a documentation sweep that reddened a
  census indirectly.
- Landed: all 36 upstream `fix()` commits triaged against port bytes, each inherited-bug verdict put
  to an independent seat briefed to refute it. Three survived and are filed with both-side
  citations; the rest closed NOT-PORTED or already-correct.
- Does not prove: no headed or Linux evidence was taken for any of this. (SUPERSEDED 2026-08-23: the
  filed defects were repaired and are DONE rows; the Arcademy surface has since been sized against
  source and upstream has shut its door.)
- Owner decision or blocker: SUPERSEDED 2026-08-23. Arcademy was ruled IN SCOPE by the owner. The
  real-desktop non-determinism had two causes: the VSTest runner (removed) and desktop contention
  (still open). The floor is green on a quiet box.
