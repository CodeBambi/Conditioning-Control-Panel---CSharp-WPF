# SP-138 — Move an overlay surface without re-confirming input routing, and do not keep a claim you stopped earning

## Mission

**Every moving-glyph module in this port is blocked on one thing, and board row 83 (P0) names it.**

Moving a surface means re-`Present`ing it. `Present` confirms its hit test, and confirming means
**clearing click-through**: `Overlay/Win32OverlayPresence.cs:557` calls
`ApplyClickThroughStyle(window, clickThrough: false)`, hit-tests at `:558`, and only restores the
caller's request at `:566` or `:574`. **At 60Hz that is a full-screen window catching the user's
clicks sixty times a second.**

I verified both of row 83's premises against the code before authoring this; they are exact.

Your outcome: **a MOVE path that repositions an already-confirmed surface without re-running
`ConfirmInputRouting`** — so motion becomes possible at all.

## THE TRAP, AND IT IS THE WHOLE PACKET

`ConfirmInputRouting` is not ceremony. It is what **earns** the claim that input routes to the
surface. A move that skips it has stopped earning that claim for the new position.

**So the honest outcome is not "move silently and keep the old verdict."** If the surface moves
without re-confirmation, the capability's own reported state must say so — the claim degrades to
whatever "confirmed at a position we have since left" honestly is, and it re-earns confirmation on
some stated condition (first present, a monitor change, an explicit request, a bounded cadence — you
choose and you justify).

**A move that keeps a full-strength claim it did not re-verify is the fake-available shape the
truthful-capability contract bans, and it would be worse than the 60Hz bug it fixes.**

If you conclude the claim CANNOT be honestly retained across an unconfirmed move, say so and report
what the honest degraded state is. That is a valid outcome for this packet.

## Out of scope, explicitly — row 83 has three terms and this packet is ONE

- **Per-pixel alpha (row 83 term 2).** `Overlay/OverlayFrame.cs:14-22` documents why BGRX is uniform
  `LWA_ALPHA` and why `UpdateLayeredWindow` would **delete** the alpha read-back
  `OverlayReasonCodes.OverlayNotComposited` depends on. Do not touch it. Do not work around it.
- **The clock seam / elapsed time (row 83 term 3).** SP-106's final review names it and no divergence
  covers it. Not yours.
- **Bouncing Text itself.** This packet does not draw a glyph.

Naming a term you did not do is required; silently widening into one is a scope defect.

## The other traps

### 1. Do not weaken an existing OS-level assertion
`PointerCoexistenceTests`, `InputCapabilityTests` and `OverlayCapabilityTests` are what SP-099,
SP-100 and SP-110 earned. **You add a path; you do not adjust a threshold.**

### 2. The real-desktop suite is contended on this machine RIGHT NOW
Three `PointerCoexistenceTests` facts are red at the wave-68 land for a proven environmental reason
(the owner's foreground app), and `DesktopPreflight` reports CLEAN through it — that is board row
342, filed at that land. **Expect them red, do not chase them, and do not "fix" them.** Compare
FAILURE SETS before and after, never counts.

### 3. Standing rules
No wall-clock waits — `TestWait` only. No TODOs. Every new guard watched red **at the committed
head**, with the SHA. `| **Dnnn** |` rows carry exactly five unescaped pipes: **escape `|` inside code
spans as `\|`** — a literal `||` split two rows at the wave-68 land and GFM silently dropped a whole
cell. **Count the delimiters; do not read them.**

### 4. Divergence ids: **D311 onward**

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/CcpClient.Tests/OverlayMoveTests.cs` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D311 onward), and `spine-tasks/SP-138-overlay-move-without-reconfirm/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Overlay/OverlayFrame.cs`, `client/tests/CcpClient.Tests/{PointerCoexistenceTests,InputCapabilityTests,OverlayCapabilityTests,PointerCapabilityTests,VideoOverlayCoexistenceTests,RealDesktopCollection,DesktopPreflightTests}.cs`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-138-overlay-move-without-reconfirm/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/OverlayMoveTests.cs` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/OverlayFrame.cs`, `client/tests/CcpClient.Tests/PointerCoexistenceTests.cs`, `client/tests/CcpClient.Tests/InputCapabilityTests.cs`, `client/tests/CcpClient.Tests/OverlayCapabilityTests.cs`, `client/tests/CcpClient.Tests/PointerCapabilityTests.cs`, `client/tests/CcpClient.Tests/RealDesktopCollection.cs`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-138-overlay-move-without-reconfirm/record.md`, `spine-tasks/SP-138-overlay-move-without-reconfirm/plan.md`, `spine-tasks/SP-138-overlay-move-without-reconfirm/floor-delta.json` |

**Pin: 2616 unit / 152 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit:** what the move path skips; **what the reported capability state
   becomes across an unconfirmed move, and why that is honest**; on what condition confirmation is
   re-earned; and which edit each new guard reds on.
2. Build the move path.
3. **Prove the 60Hz defect is gone** — a move must not clear click-through — and prove the claim
   degrades. A test that only shows the move happening is half the packet.
4. Evidence class: this is a Windows `user32` fact. `client/docs/verification-harness.md` governs.
   **A headless frame never discharges a headed gate**; say exactly what class you reached.
5. Divergences **D311 onward**.

## Completion Criteria

- An already-confirmed surface moves without re-running `ConfirmInputRouting`.
- The reported capability state across an unconfirmed move is honest, and a test pins it.
- No existing OS-level assertion weakened; no test skipped, quarantined or added to `allowedSkips`.
- Row 83's other two terms named as untouched.
- Build 0 warnings / 0 errors. **The three contended `PointerCoexistenceTests` may be red — say so and
  compare failure sets, do not chase them.**

## Do NOT

- Keep a full-strength input claim across a move you did not confirm.
- Touch `OverlayFrame.cs` or attempt per-pixel alpha.
- Build the clock seam.
- Weaken an OS-level assertion or add anything to `allowedSkips`.
- Use a divergence id below D311.

## Git Commit Convention

Conventional commit, `feat(SP-138): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the move path, the claim's honest state across an unconfirmed move and the reasoning
behind it, the re-confirmation condition, the red demonstrations with the head SHA, the before/after
failure sets, and row 83's two untouched terms named.
