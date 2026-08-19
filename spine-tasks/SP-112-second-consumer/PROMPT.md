# SP-112 — The second consumer, which is really a test of two capabilities

## Mission

Nine modules run. Three capabilities were built in the last three waves — audio, input, video — and **each has exactly one consumer, the module that introduced it.** A capability with one consumer is a capability shaped around one caller.

SP-101 asked this question about the effect template and the answer was worth more than the module: the pattern held, but three of six parts were per-module copies that had to become shared. **Ask it again, of a capability.**

Your outcome: **a module that consumes an existing capability it did not shape — and an honest report on what that cost.**

## Which module

**Bubble Count** (needs video) or **Bubble Pop** (needs input) — both were blocked and both are now unblocked. **Read `ConditioningControlPanel/Services/BubbleCountService.cs` (its video field is at `:30`, `_videosPath`; NOT `:29`, which is `_isBusy` — corrected once, re-broken once, do not re-break it) and the Bubble Pop service, then choose and defend at the plan checkpoint.**

Choose by which one **stresses the capability hardest**, not by which is easier. SP-110's own record says Bubble Pop needs per-bubble hit tests on windows that **move** — the hit test's answer is a function of a position that changes between asking and clicking, a race by construction. If that is too much for one packet, say so with evidence and take Bubble Count; if it is tractable, it is the more valuable answer.

## THE CENTRAL QUESTION

**Did the capability fit, or did you have to reach around it?**

Report specifically: what you called that already existed; what you needed that was not there; what you had to duplicate because the capability exposes the wrong seam; and whether `Available` meant the same thing for your module as for the first consumer. **If you find yourself adding a method to `Video/**` or `Input/**` for your own convenience, stop and report it** — that is the finding, and it is worth more than shipping the module.

## THE OTHER TRAPS

### 1. You may consume, and you may extend — but say which
`Video/**` and `Input/**` are open to you, unlike the last two packets. **Every line you add there must be justified in `record.md` as something the first consumer would also have wanted**, not as something your module needs. A capability that grows a method per caller is not a capability.

### 2. The dot's eighth meaning may be owed — or may not
Seven meanings exist: clock, screen, change, custody, reach, demand, motion. **A second consumer of an existing capability should probably reuse that capability's meaning.** If it does not, that is a finding about the meaning, not a licence for an eighth.

### 3. Do not disturb the nine landed modules, the overlay, or the card
Their facts pass unchanged. Prove the overlay and Lock Card still survive if your module puts anything on screen.

### 4. Sweep every predicate you add
78 mutations last wave, 68 the two before. **Mutate every conjunct**, report survivors, mark equivalents with evidence.

### 5. Linux refuses honestly
Typed `Unavailable(reason)` with the manual gate named.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/Video/**`, `client/src/CcpClient.Desktop/Input/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-112-second-consumer/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-112-second-consumer/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Effects` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-112-second-consumer/record.md`, `spine-tasks/SP-112-second-consumer/floor-delta.json` |

**Pin: 1742 unit / 107 headless.** Build before the gate. **Run `check-floor.mjs` ALONE.** Run `sum-deltas` before deleting any delta file. **Keep every artifact inside your worktree** — a lane wrote three levels above its own root last wave.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** which module, why it stresses the capability hardest, **and your prediction of what the capability will be missing**.
2. Build it, consuming the existing capability.
3. **Report the capability verdict in `record.md`** — what fit, what did not, what you added and why the first consumer would have wanted it too. **This is the packet's real output.**
4. Rack row with a truthful dot; prove the overlay and card survive if you draw or take focus.
5. **Prove it bites**, then sweep every predicate by mutation.
6. Divergences from D133.

## Completion Criteria

- A second consumer runs under an existing capability.
- `record.md` carries an explicit capability verdict.
- Nine landed modules' facts pass unchanged; overlay and card unharmed.
- Build 0 warnings / 0 errors.

## Do NOT

- Add to `Video/**` or `Input/**` for your own convenience without reporting it as a finding.
- Claim a human saw, heard or clicked anything.
- Run the floor gate concurrently with anything.
- Write outside your worktree.

## Git Commit Convention

Conventional commit, `feat(SP-112): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` including the capability verdict, plus divergences in `client/docs/wpf-surface-reachability.md`.
