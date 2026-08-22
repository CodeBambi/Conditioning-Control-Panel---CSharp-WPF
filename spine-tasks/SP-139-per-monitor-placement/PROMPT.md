# SP-139 — Place effects on EVERY monitor, the way the shipping app does

## Mission

**A two-monitor user gets different behaviour from this port than from the WPF app, and the parts
needed to fix it are already built.**

WPF **enumerates monitors and places per monitor** — `Services/Flash/FlashService.cs:2204-2245`, cited
in this port's own source at `Effects/OverlaySurfaceSet.cs:248`. This port places on **one**.

I verified the whole chain in code before authoring this, and the gap is narrower than the board row
suggests:

- **The enumeration already works.** `Overlay/OverlayDisplays.cs:35-42` `Enumerate()` returns *"every
  display the OS reports, primary first"* through USER32, and `[]` off Windows.
- **The container already holds many.** `Effects/OverlaySurfaceSet.cs:39` is a `List<Slot> _slots`,
  and `:107` `LiveRects()` returns a list.
- **The gap is POLICY.** `Effects/PrimaryDisplayPlacement.cs:35-50` `PrimaryBounds()` takes that full
  list and deliberately returns ONE — primary, falling back to the first the OS listed.

So this is not a missing platform capability. **It is nine call sites asking for one rectangle when
the OS already offered them all, into a container that already holds many.**

Your outcome: **effects place on every enumerated display on Windows.**

## The consumers, which is why this is worth doing

Nine sites in the tree take the primary and drop the rest — `Effects/VideoSurfacePresenter.cs:515`,
`LockCardEffect.cs:579`, `BubbleCountEffect.cs:849`, `BubblePopSurfacePresenter.cs:157`,
`FlashSurfacePresenter.cs:163`, `SpiralSurfacePresenter.cs:133`, `PinkFilterSurfacePresenter.cs:102`,
`SubliminalSurfacePresenter.cs:96`, `BouncingTextSurfacePresenter.cs:136`. Several destructure
`[var primary, ..]`, which is the shape that silently discards.

**Read every one before you change any of them.** Some may be correct as-is: a modal card on all four
monitors may be wrong where a flash on all four is right. **Upstream's own behaviour per effect is the
authority, not a blanket rule** — and where upstream differs per effect, say so per effect.

## THE TRAP: N surfaces means N ways to be half-right

Today one surface either presents or it does not. With N, **one monitor can fail while another
succeeds**, and the capability's reported state has to survive that honestly.

**A claim of "presented" that is true on one monitor and false on another is the fake-available shape
this port bans.** Decide what the state is when 1 of 3 fails, justify it, and pin it with a test.
Partial success is the interesting case and the packet is mostly about it.

## Scope, stated so you do not overreach

- **Windows only.** `Enumerate()` returns `[]` off Windows by design — *"this build enumerates
  displays through USER32 and does not guess elsewhere."* **Leave that alone.** Linux stays typed and
  honest; do not fabricate a backend.
- **This does NOT close board line 119**, the geometry spike. That row wants X11 and Wayland, negative
  X/Y, monitors above/below, vertical stacks, gaps, mixed scaling and resolution, portrait and flipped
  orientation, hot-plug, rotation, and rearrangement. **You are doing none of that.** Name it.
- **Hot-plug and rotation are explicitly out.** If a display list changes mid-session, say what happens
  today rather than fixing it.

## The other traps

### 1. The real desktop is contended on this machine
Three `PointerCoexistenceTests` facts are red at base for a proven environmental cause, and
`DesktopPreflight` reports CLEAN through it (the wave-68 land filed that as its own row). **Expect
them red, do not chase them, compare FAILURE SETS and not counts.** They were green for one lane's
baseline an hour later, so it is momentary.

### 2. This machine's second monitor
`client/port.txt` names `DISPLAY3` as the headed-evidence monitor. **If you cannot get real
multi-monitor evidence, say exactly what you did instead** — a synthetic display list is a fine unit
fixture and a poor parity claim, and the difference must be stated. `client/docs/verification-harness.md`
governs, and **a headless frame never discharges a headed gate.**

### 3. Standing rules
No wall-clock waits — `TestWait` only. No TODOs. Every new guard watched red **at the committed head**,
with the SHA. `| **Dnnn** |` rows carry exactly five unescaped pipes: **escape `|` inside code spans as
`\|`**, and verify by COUNTING delimiters, not by reading — a literal `||` silently destroyed a whole
cell at the wave-68 land.

### 4. Divergence ids: **D311 onward** (D311 is free; SP-138 filed none)

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/CcpClient.Tests/PerMonitorPlacementTests.cs` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D311 onward), and `spine-tasks/SP-139-per-monitor-placement/**` |
| Must not change | everything else, and specifically `client/tests/CcpClient.Tests/{PointerCoexistenceTests,InputCapabilityTests,OverlayCapabilityTests,PointerCapabilityTests,VideoOverlayCoexistenceTests,RealDesktopCollection,DesktopPreflightTests}.cs`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-139-per-monitor-placement/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/PerMonitorPlacementTests.cs` |
| fileScopeMustNotChange | `client/tests/CcpClient.Tests/PointerCoexistenceTests.cs`, `client/tests/CcpClient.Tests/InputCapabilityTests.cs`, `client/tests/CcpClient.Tests/OverlayCapabilityTests.cs`, `client/tests/CcpClient.Tests/PointerCapabilityTests.cs`, `client/tests/CcpClient.Tests/RealDesktopCollection.cs`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-139-per-monitor-placement/record.md`, `spine-tasks/SP-139-per-monitor-placement/plan.md`, `spine-tasks/SP-139-per-monitor-placement/floor-delta.json` |

**Pin: 2616 unit / 152 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit:** which of the nine consumers place on all displays and which
   correctly do not, **each justified from upstream's behaviour for that effect**; what the reported
   state is when some monitors succeed and some fail; and which edit each new guard reds on.
2. Make placement per-display where upstream places per-display.
3. **Prove partial failure is reported honestly.** A test that only shows N surfaces on N monitors is
   half the packet.
4. State your evidence class exactly, and whether real multi-monitor hardware was involved.
5. Divergences **D311 onward**.

## Completion Criteria

- Effects place on every enumerated display on Windows, where upstream does.
- Partial failure across monitors is honestly reported and pinned by a test.
- Linux still enumerates `[]` and refuses typed; nothing guessed.
- Board line 119's spike matrix named as NOT closed.
- No existing OS-level assertion weakened; nothing added to `allowedSkips`.
- Build 0 warnings / 0 errors. **The three contended tests may be red — say so, compare failure sets.**

## Do NOT

- Claim a monitor presented when it did not.
- Guess a Linux display backend.
- Claim board line 119 closed.
- Change an effect to all-monitors without upstream evidence for THAT effect.
- Weaken an OS-level assertion or touch `allowedSkips`.
- Use a divergence id below D311.

## Git Commit Convention

Conventional commit, `feat(SP-139): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the per-effect upstream evidence, the partial-failure state and its reasoning, the
evidence class actually reached, the red demonstrations with the head SHA, the before/after failure
sets, and what of line 119 remains.
