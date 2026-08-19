# SP-109 — Audio, the capability this port once refused to build

## Mission

SP-108's survey says the port's next gap is a **capability, not a module**: audio. It closes **Mind Wipe outright and the audio half of Brain Drain** — not two whole rows.

But audio is the thing this port deliberately did **not** build first. From the wave that chose Flash Images over Mind Wipe, in the owner digest:

> *"There was an easier first effect — Mind Wipe, which is pure audio and would have looked completely finished. It was rejected on purpose, because nothing in this project can actually verify that a sound played, so shipping it would have meant claiming something nobody could check. A named missing piece beats an unverifiable claim."*

**That refusal is your problem to solve, not to inherit and repeat.** Your outcome: **a typed audio capability whose `Available` is earned from something, plus an honest, written account of exactly what a passing test does and does not prove about sound reaching a human ear.**

## THE CENTRAL TRAP

**A test that asserts "`Play()` returned" is the shape this port has rejected four times.** It is the tray method returning, the overlay flag being set, the fake dial re-imposing its own clamp. If your evidence for audio is that your own code did not throw, you have built the thing SP-098 refused to ship.

Get as close to an **OS-level fact** as this machine allows — the same discipline `ITrayPresence` used with `Shell_NotifyIcon` and `IOverlayPresence` used with eight round-trips. Ask the audio stack what it thinks is happening: device enumeration, a real mixer session, a session state or peak meter read back from the OS, whatever WASAPI/NAudio genuinely exposes. **Then name precisely where the provable chain stops.** "The OS reports an active render session for this process" is a real fact. "A human heard it" is not, and must be a named headed/manual gate.

## Ground truth

`ConditioningControlPanel/Services/` — read the real Mind Wipe service and `Services/LockCard/BrainDrainService.cs` (356 lines, `DispatcherTimer` + NAudio `WaveOutEvent`, engine-started at `MainWindow/MainWindow.StartStop.cs:243`, stopped at `:342`). Cite what you find. The port already has `client/src/CcpClient.Desktop/Audio/AudioSeams.cs` — **read it before designing; do not duplicate it.**

## THE OTHER TRAPS

### 1. Brain Drain is HALF reachable and must not land whole
The same `BrainDrainEnabled` flag drives a desktop-wide blur (`Services/Notifications/OverlayService.cs:383-386`), which the shipping panel calls the **"VISUAL half"** verbatim (`Views/Controls/Studio/BrainDrainFeatureControl.xaml.cs:170`), running a 30-60 FPS screen capture (`OverlayService.cs:1965-1995`) needing a read-back the port lacks. **If you ship a Brain Drain row, its blur must be visibly and honestly absent** — not silently missing.

### 2. The fifth dot meaning is owed, and it is yours if you ship the half-row
The dot has meant four things: the **clock** (paced), the **screen** (continuous), **change** (moving), **custody** (non-drawing). A module whose audio fires while its visual half cannot draw is a fifth case, and **no existing rule settles it** — SP-105's rule says `Armed`, the Subliminals rule says `Live`. Decide it, and say why.

### 3. Linux must refuse honestly, not silently
Follow `ISecretStore`/`ITrayPresence`/`IOverlayPresence`: a typed `Unavailable(reason)` naming the exact manual gate. A Linux no-op is the banned shape.

### 4. Do not weaken the five landed modules
Their facts pass unchanged. Extraction is fine; semantic change is a finding.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Audio/**`, `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), `client/docs/verification-harness.md` (audio evidence class ONLY), and `spine-tasks/SP-109-audio-capability/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-109-audio-capability/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Audio` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-109-audio-capability/record.md`, `spine-tasks/SP-109-audio-capability/floor-delta.json` |

**Pin: 1527 unit / 95 headless.** Build before the gate. **Run `check-floor.mjs` ALONE** — never with a build or another gate (SP-107). Run `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint, and this one is the whole packet:** state **exactly what you can prove from the OS** about audio on this machine and **what you cannot**, with the API you will ask. An honest short chain beats a long unprovable one. Also state whether you ship one module or two, and your dot decision if two.
2. Build the typed capability. `Available` earned, never asserted.
3. Ship Mind Wipe. Ship Brain Drain's audio half **only** if its blur's absence is honest and visible.
4. Linux refuses typed, with the manual gate named.
5. **Prove it bites:** make the backend claim `Available` without opening a device, and confirm a test reds. Restore byte-identically.
6. Record the audio evidence class in `client/docs/verification-harness.md`, including what tier 1 cannot cover.
7. Divergences from D101 onward.

## Completion Criteria

- `Available` is earned from the OS; the provable chain's end is named in `record.md`.
- Mind Wipe runs under the spine with a rack row and a truthful dot.
- Any Brain Drain row states its missing visual half plainly.
- Linux refuses typed with a named gate; no test skipped to hide it.
- Five landed modules' facts pass unchanged.
- Build 0 warnings / 0 errors.

## Do NOT

- Assert audio works because a method returned.
- Ship Brain Drain as a complete row.
- Ship a Linux no-op.
- Claim any human heard anything.
- Run the floor gate concurrently with anything.

## Git Commit Convention

Conventional commit, `feat(SP-109): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the audio evidence chain and its exact stopping point, divergences in `client/docs/wpf-surface-reachability.md`, and the audio evidence class in `client/docs/verification-harness.md`.
