# SP-111 — Video, the last big capability

## Mission

Eight modules run. The board's remaining rows cluster behind three capabilities, and **video is the largest**: it blocks **Bubble Count**, **Mandatory Video** and **Visuals**.

The port has proven this shape twice. SP-109 earned audio from four OS read-backs ending in a measured peak meter. SP-110 earned input from five, ending in a keystroke that arrives and *doesn't* arrive when another window holds the foreground. **Both named exactly where the provable chain stopped.**

Your outcome: **a typed video capability whose `Available` is earned from the OS, plus the first module that plays one — with the chain's stopping point named.**

## THE CENTRAL TRAP: "frames decoded" is not "frames displayed"

A decoder that returns bytes proves a **file is readable**, not that anything reached a screen. That is this port's oldest failure shape — the tray method returning, the overlay flag set, `Play()` returning, a fake dial re-imposing its own clamp.

Get to an **OS-level fact**. Candidates worth measuring rather than assuming: a presenting swapchain or frame-clock the OS acknowledges, a frame counter the compositor advances, a media session the OS reports as playing, a read-back of the target surface showing it *changed between frames*. **A pixel differential across two frames is worth more than any count of decoded buffers**, and SP-110's ink differential is your precedent — it caught a mutation where deleting the paint entirely still passed, because the window had no background brush.

**Name where the chain stops.** "The OS advanced a frame counter" is a fact. "The user saw the video" is not — that is `presentation-verified` or a new manual class, and it must be declared, not implied.

## Ground truth

`ConditioningControlPanel/Services/` — read the real video service and `Services/BubbleCountService.cs` (its video field is at `:30`, `_videosPath`; **not** `:29`, which is `_isBusy` — a correction already made and re-broken once, so do not re-break it). Cite what you find.

`Z:\CCP Vids` is the owner's real-media directory and **may not exist on this machine**. If it does not, say so and use synthesised media; a missing owner directory is a **named limit**, never a blocker and never a skip.

## THE OTHER TRAPS

### 1. Do not disturb the overlay or the card
Five modules draw through a click-through always-on-top overlay; Lock Card takes the foreground. A video surface that steals z-order, focus or the compositor from either breaks live sessions. **Prove both survive** — consume `Overlay/**` and `Input/**`, never edit them.

### 2. The dot's seventh meaning may be owed
It has meant the **clock**, the **screen**, **change**, **custody**, **reach**, and **demand**. A module playing a file has a candidate none of those fit: is it `Live` while a frame is on screen, while the decoder is fed, or while the OS says a surface is presenting? Decide and justify.

### 3. Linux refuses honestly
Typed `Unavailable(reason)` with the exact manual gate named.

### 4. Sweep every predicate
SP-109's sweep found ten survivors and six real holes; SP-110's found seven survivors across 68 mutations. **Mutate every conjunct you add**, report survivors, and mark equivalents with evidence rather than assertion.

### 5. Do not weaken the eight landed modules
Their facts pass unchanged.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Video/**` (new), `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), `client/docs/verification-harness.md` (video evidence class ONLY), and `spine-tasks/SP-111-video-capability/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Input/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-111-video-capability/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Video` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Input/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-111-video-capability/record.md`, `spine-tasks/SP-111-video-capability/floor-delta.json` |

**Pin: 1648 unit / 104 headless.** Build before the gate. **Run `check-floor.mjs` ALONE.** Run `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** exactly what you can prove from the OS about a frame reaching a surface and what you cannot, with the API named; whether `Z:\CCP Vids` exists; which module you ship; your dot decision.
2. Build the typed capability. `Available` earned, never asserted.
3. Ship one module with a rack row and a truthful dot.
4. **Prove the overlay and the card both survive** a video surface opening and closing.
5. Linux refuses typed with the gate named.
6. **Prove it bites**, then sweep every predicate by mutation and report survivors.
7. Record the video evidence class in `client/docs/verification-harness.md`; divergences from D121.

## Completion Criteria

- `Available` earned from the OS; the stopping point named in `record.md`.
- One module plays video with a rack row and a truthful dot.
- Overlay and Lock Card both proven unharmed.
- Linux refuses typed; no test skipped to hide it.
- Eight landed modules' facts pass unchanged.
- Build 0 warnings / 0 errors.

## Do NOT

- Claim video works because frames decoded.
- Edit `Overlay/**`, `Input/**` or `Audio/**`.
- Ship a Linux no-op.
- Claim any human saw anything.
- Run the floor gate concurrently with anything.

## Git Commit Convention

Conventional commit, `feat(SP-111): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the video evidence chain, its stopping point and the mutation sweep; divergences in `client/docs/wpf-surface-reachability.md`; the video evidence class in `client/docs/verification-harness.md`.
