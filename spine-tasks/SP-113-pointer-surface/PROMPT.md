# SP-113 — The pointer surface, scoped by a census rather than a guess

## Mission

Ten modules run. SP-112 refused Bubble Pop and said exactly why, with an inventory rather than an excuse (`spine-tasks/SP-112-second-consumer/record.md` §1):

- `Win32InputPresence.WindowProc` handles **five** messages — paint, keydown, syskeydown, char, close — and **no mouse message**; `Win32InputInterop` declares none either.
- The presence owns **one** `nint _window`. `Prompt` places it once. **One `SetWindowPos` site: there is no move seam.**
- `Confirmed` **requires** `IsForegroundWindow && SystemKeyboardFocusIsThisWindow` — **the inverse of what a bubble needs**, where upstream uses `ShowActivated = false` and `WM_MOUSEACTIVATE → MA_NOACTIVATE` (`BubbleCountWindow.xaml.cs:1823-1824`), plus `IsHitTestVisible` (`BubbleService.cs:2960/2988/3103`) and `ShowActivated = false` (`:2158`).

**That is a second capability, not a second consumer.** Your outcome: **a typed pointer capability whose `Available` is earned from the OS, plus Bubble Pop running on it.**

## THE CENTRAL TRAP: a click that lands is the hardest thing this port has tried to prove

SP-099 proved a surface is click-**through** and its review recorded that as the property most easily faked. SP-110 proved the inverse for a keyboard window and found `GetGUIThreadInfo(ourThreadId)` **lies** — thread-local, answering "our window" while input went elsewhere.

**You are proving a third thing again: that a click at a point reaches a specific window that must NOT take focus.** Non-activating is what makes it hard — you cannot lean on foreground or focus, which is exactly what SP-110's chain leaned on.

Get to OS-level facts and **name where the chain stops**. Worth measuring rather than assuming: `WindowFromPoint` at the bubble's own rect; a synthesised click arriving as `WM_LBUTTONDOWN`/`WM_LBUTTONUP` in *that* window's procedure; the foreground **not** changing across the click; `WM_MOUSEACTIVATE` answered `MA_NOACTIVATE`. **A click that arrives while the foreground is unchanged is the fact worth having.**

## THE OTHER TRAPS

### 1. The hit test is a race by construction, and SP-110 said so
Bubble Pop's windows **move**. The hit test's answer is a function of a position that changes between asking and clicking. **Do not hide that** — measure it, bound it, and say what the bound is. If you must freeze motion to make a click provable, say that plainly and record what it costs.

### 2. Four surfaces now share one desktop
The overlay (click-through, five modules draw on it), Lock Card (takes foreground), the video surface, and now yours. **Prove all three landed ones survive** your surface opening, moving and closing. `Overlay/**`, `Input/**`, `Audio/**` and `Video/**` are **closed to editing** — consume them.

### 3. Reuse the dot's meaning if you can
Eight exist: clock, screen, change, custody, reach, demand, motion. A pointer module is closest to **DEMAND** (SP-110). If it does not fit, that is a finding about DEMAND, not a licence for an eighth.

### 4. Sweep every predicate, and check your own equivalents
SP-112's M-s was marked an equivalent mutant on a **proof that was false** — four waves had marked 3-4 "equivalent" each and nobody had checked the arithmetic until then. **Every equivalence claim you make is a proof obligation.** Discharge it or mark the survivor uncovered.

### 5. Linux refuses honestly
Typed `Unavailable(reason)` with the exact manual gate named.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Pointer/**` (new), `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), `client/docs/verification-harness.md` (pointer evidence class ONLY), and `spine-tasks/SP-113-pointer-surface/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Input/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/src/CcpClient.Desktop/Video/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-113-pointer-surface/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Pointer` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Input/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/src/CcpClient.Desktop/Video/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-113-pointer-surface/record.md`, `spine-tasks/SP-113-pointer-surface/floor-delta.json` |

**Pin: 1830 unit / 112 headless.** Build before the gate. **Run `check-floor.mjs` ALONE.** `sum-deltas` before deleting any delta file. **Keep every artifact inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** verify SP-112's census yourself; state exactly what you can prove about a click reaching a non-activating window and what you cannot; your dot decision; how you will handle the moving-target race.
2. Build the typed capability. `Available` earned, never asserted.
3. Ship Bubble Pop with a rack row and a truthful dot.
4. **Prove the overlay, Lock Card and the video surface all survive.**
5. Linux refuses typed with the gate named.
6. **Prove it bites**, then sweep every predicate; **discharge or withdraw every equivalence claim**.
7. Record the pointer evidence class in `client/docs/verification-harness.md`; divergences from D141.

## Completion Criteria

- `Available` earned from the OS; the stopping point named.
- Bubble Pop runs with a rack row and a truthful dot.
- Three landed surfaces proven unharmed.
- Ten landed modules' facts pass unchanged.
- Build 0 warnings / 0 errors.

## Do NOT

- Claim a click landed because a handler was attached.
- Take focus — the whole point is a window that does not.
- Edit another capability's folder.
- Claim any human clicked anything.
- Run the floor gate concurrently with anything.

## Git Commit Convention

Conventional commit, `feat(SP-113): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the pointer evidence chain, its stopping point, the race bound, and the mutation sweep; divergences in `client/docs/wpf-surface-reachability.md`; the pointer evidence class in `client/docs/verification-harness.md`.
