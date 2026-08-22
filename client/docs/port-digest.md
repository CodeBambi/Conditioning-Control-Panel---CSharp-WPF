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
- Does not prove: none of the three confirmed defects is fixed - they are filed, not repaired. The
  Arcademy surface is unexamined beyond establishing its total absence from the port. No headed or
  Linux evidence was taken for any of this.
- Owner decision or blocker: whether Arcademy is in scope at all. Separately, the real-desktop
  verification pair (`PointerCoexistenceTests`, `AiAwarenessTests`) fails non-deterministically -
  across three runs the failing set moved - which keeps the existing P1 row open and means the floor
  is not reliably green on this machine.
