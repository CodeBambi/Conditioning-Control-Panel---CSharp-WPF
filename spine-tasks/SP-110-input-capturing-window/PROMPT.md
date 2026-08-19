# SP-110 — A window that takes input, which three modules are waiting on

## Mission

Seven modules run. Every one of them is **passive**: it draws, tints, paces, ramps or plays, and **none of them ever asks the user for anything**. The port has an overlay that is deliberately click-**through** (`WS_EX_TRANSPARENT`, SP-099) and a rack whose every row is fire-and-forget.

SP-108's survey found **three** unported rows blocked on the same missing capability: a window that **captures** input — takes focus or the pointer, receives a click or a keystroke, and answers. That is the biggest single unlock left on the board.

Your outcome: **a typed input-capturing window capability whose `Available` is earned from the OS, plus the first module that actually asks the user something.**

## Read the survey first

`spine-tasks/SP-108-non-drawing-module/record.md` §7 names the three rows. **Read it, verify its claims against `ConditioningControlPanel/`, and pick the module whose input need is simplest to prove.** If the survey is wrong about any of the three, say so — it has been corrected twice already, once against a reviewer.

## THE CENTRAL TRAP: this is the exact inverse of SP-099

SP-099 spent a whole wave proving a surface is click-**through**, and its final review recorded that proving input *passes through* is the property most easily faked. **You must now prove the opposite of that, for a different window, and the same standard applies.**

A test asserting `WS_EX_TRANSPARENT` was *not* set is not evidence a click lands. Get to an **OS-level fact**: the OS reports this window as focused / foreground, a hit test at a point inside it returns **this** HWND, a synthesised input reaches the message loop. **Name precisely where the provable chain stops** — the SP-109 discipline, which produced a four-link chain ending in a measured peak meter and an honest ceiling.

## THE OTHER TRAPS

### 1. Do not break the overlay by acquiring focus
The port ships a click-through always-on-top overlay that **five** landed modules draw through. A window that steals focus or z-order from it, or leaves it input-opaque, breaks live sessions. **Prove the overlay still passes input after your window opens and closes** — `OverlayCapabilityTests`' oracle already exists and you may not weaken it.

### 2. The dot's sixth meaning may be owed
It has meant the **clock** (paced), the **screen** (continuous), **change** (moving), **custody** (non-drawing), **reach** (audio — a resource the process does not own). A module *waiting for a human* is a sixth candidate: is it `Live` while the prompt is up and unanswered? Decide it and say why, or show an existing rule fits.

### 3. Linux refuses honestly
Typed `Unavailable(reason)` with the exact manual gate named, following `ISecretStore`/`ITrayPresence`/`IOverlayPresence`/`IAudioPresence`. A no-op is the banned shape.

### 4. Do not weaken the seven landed modules
Their facts pass unchanged. Extraction is fine; semantic change is a finding.

### 5. Sweep your own predicates
SP-109's 24-mutation sweep of one capability found **ten survivors and six real holes** — including one where another process's state would have earned this one `Available`. **Mutate every conjunct of every predicate you add**, and report the survivors and which are genuinely equivalent.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Input/**` (new), `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), `client/docs/verification-harness.md` (input evidence class ONLY), and `spine-tasks/SP-110-input-capturing-window/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

`Overlay/**` is closed: you must **consume** it to prove trap 1, never edit it.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-110-input-capturing-window/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Input` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-110-input-capturing-window/record.md`, `spine-tasks/SP-110-input-capturing-window/floor-delta.json` |

**Pin: 1589 unit / 100 headless.** Build before the gate. **Run `check-floor.mjs` ALONE.** Run `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** which of the three rows, with citations; **exactly what you can prove from the OS about input arriving and what you cannot**; your dot decision; and how you will prove the overlay is unharmed.
2. Build the typed capability. `Available` earned, never asserted.
3. Ship the module with a rack row and a truthful dot.
4. **Prove the overlay survives** — still click-through, still on top, after your window opens and closes.
5. Linux refuses typed with the gate named.
6. **Prove it bites**, then **sweep every predicate by mutation** and report survivors.
7. Record the input evidence class in `client/docs/verification-harness.md`; divergences from D110.

## Completion Criteria

- `Available` earned from the OS; the provable chain's stopping point named in `record.md`.
- One module asks the user something and handles both an answer and no answer.
- The overlay is proven unharmed.
- Linux refuses typed; no test skipped to hide it.
- Seven landed modules' facts pass unchanged.
- Build 0 warnings / 0 errors.

## Do NOT

- Assert input works because a handler was attached.
- Edit `Overlay/**` or `Audio/**`.
- Ship a Linux no-op.
- Claim any human clicked anything.
- Run the floor gate concurrently with anything.

## Git Commit Convention

Conventional commit, `feat(SP-110): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the input evidence chain and its stopping point, the mutation sweep and its survivors, divergences in `client/docs/wpf-surface-reachability.md`, and the input evidence class in `client/docs/verification-harness.md`.
