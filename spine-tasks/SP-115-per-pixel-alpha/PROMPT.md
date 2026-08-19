# SP-115 — Per-pixel alpha, the blocker SP-106 named and refused to fake

## Mission

Eleven modules run. **Bouncing Text has been blocked since wave 46**, and SP-106 refused it with evidence rather than an excuse (`spine-tasks/SP-106-moving-effect/record.md`, D83/D84):

- Its window is **transparency-backed glyphs** (`ConditioningControlPanel/Services/Subliminal/BouncingTextService.cs:827-828`, `AllowsTransparency` / `Background = Transparent`).
- This port's overlay composites at **one uniform `LWA_ALPHA` over an opaque BGRX frame** and refuses per-pixel alpha **by design** (`client/src/CcpClient.Desktop/Overlay/OverlayFrame.cs:14-22`). At the shipped default opacity it would **black the desktop**.

Your outcome: **a typed per-pixel-alpha surface capability whose `Available` is earned from the OS — or a recorded finding that this machine cannot give one.**

## THE RECORDED HAZARD — read it before designing

SP-099 measured this exact territory and its finding is the reason this packet is written carefully:

> Toggling `WS_EX_LAYERED` alone is harmless; `UpdateLayeredWindow` alone fails with **error 87**; but **toggle then ULW succeeds and kills the ghost check** — two ordinary lines whose first half is silent.

`Overlay/**` earned `Available` from **eight OS round-trips** precisely so a ghost window could not pass. **Do not repeat that.** Read `spine-tasks/SP-099-overlay-surface/record.md` before your plan checkpoint and say how your design avoids it.

## THE CENTRAL TRAP: alpha is the property most easily faked

A window can be layered, present, topmost, and still composite **nothing** — or composite a black rectangle over the user's desktop, which is worse than absent. **`Available` must be earned from a read-back that distinguishes:**

- a glyph pixel from the **background behind it**, and
- a fully transparent pixel from an **opaque black** one.

SP-111's differential is your precedent: read the OS's own copy back and prove it **changed**, with a control proving it does not change when nothing was drawn. A count of `SetPixel` calls is not evidence.

## THE OTHER TRAPS

### 1. Five surfaces now share one desktop
Overlay (click-through, five modules draw on it), Lock Card (foreground), video, pointer, and yours. **SP-113's final review recorded that the coexistence evidence does not scale past four disjoint rectangles** — its strength was that none could occlude another's hit-test point, and a fifth breaks that. **Do not extend `PointerCoexistenceTests` by copying a pair.** Either build the occlusion-aware arbitration that review named, or state plainly what you did not prove.

### 2. Do not weaken the overlay to make room
`Overlay/**`, `Input/**`, `Audio/**`, `Video/**`, `Pointer/**` are **closed to editing**. If per-pixel alpha genuinely requires changing the overlay's contract, **that is a finding**, not a licence.

### 3. Every equivalence claim is a proof obligation
`client/docs/port-workflow.md` now carries the rule: **inadmissible until every consumer of the mutated symbol is enumerated by `grep` and discharged by name.** Four false ones were caught across three waves. If enumeration is not done, the survivor is **uncovered**.

### 4. Run the warning gate
`node client/tests/floor/check-warnings.mjs` — a plain `dotnet build` skips `CoreCompile` on a rebuild and prints 0 warnings over a tree that holds one.

### 5. Linux refuses honestly
Typed `Unavailable(reason)` with the manual gate named.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Glyph/**` (new), `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), `client/docs/verification-harness.md` (glyph evidence class ONLY), and `spine-tasks/SP-115-per-pixel-alpha/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/{Overlay,Input,Audio,Video,Pointer}/**`, `client/tests/floor/floor.json`, `client/tests/floor/check-floor.mjs`, `client/tests/floor/check-warnings.mjs`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-115-per-pixel-alpha/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Glyph` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Input/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/src/CcpClient.Desktop/Video/**`, `client/src/CcpClient.Desktop/Pointer/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-115-per-pixel-alpha/record.md`, `spine-tasks/SP-115-per-pixel-alpha/floor-delta.json` |

**Pin: 1938 unit / 117 headless.** **Run `check-warnings.mjs` and `check-floor.mjs` ALONE**, never concurrently in the same worktree. `sum-deltas` before deleting any delta file. **Keep every artifact inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** what you can prove from the OS about a per-pixel-alpha composite and what you cannot; **how you avoid SP-099's toggle-then-ULW ghost**; your coexistence approach given five surfaces; your dot decision.
2. Build the typed capability. `Available` earned, never asserted.
3. Ship Bouncing Text, or record why it still cannot ship.
4. **Prove the four landed surfaces survive.**
5. Linux refuses typed.
6. **Prove it bites**, then sweep every predicate, discharging or withdrawing every equivalence claim.
7. Record the glyph evidence class; divergences from D152.

## Completion Criteria

- `Available` earned from a read-back that distinguishes glyph from background and transparent from black.
- Bouncing Text ships, or a recorded finding that it cannot.
- Four landed surfaces proven unharmed.
- Eleven landed modules' facts pass unchanged.
- Warning gate green; build 0 warnings / 0 errors.

## Do NOT

- Claim alpha because a window is layered.
- Black the desktop, ever — a surface that composites opaque black over the user's screen is worse than none.
- Edit another capability's folder.
- Extend the coexistence file by copying a pair without saying what it does not prove.
- Run either gate concurrently with anything.

## Git Commit Convention

Conventional commit, `feat(SP-115): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the alpha evidence chain, its stopping point, the ghost-avoidance argument, the coexistence position and the sweep; divergences in `client/docs/wpf-surface-reachability.md`; the glyph evidence class in `client/docs/verification-harness.md`.
