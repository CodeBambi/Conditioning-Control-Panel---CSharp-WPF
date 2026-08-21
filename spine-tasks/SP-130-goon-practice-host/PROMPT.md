# SP-130 — Goon Game Practice mode, over the payload the port already ships

## Mission

SP-129 censused Goon Game and returned **BUILDABLE-IN-PART** with the unit named and inventoried:

> **Practice mode over the served payload.** Serve `Resources/web/goon/` from the port's existing
> loopback server in the port's existing WebView host, answer the page's `init` and `manifest`
> frames, and a user plays a complete duel against the scripted opponent: all nine element kinds,
> heat and charges, sudden-death rounds, recap. **No network, no microphone, no camera, no
> entitlement, no Discord.**

`client/docs/goon-game-census.md` §7.1 is your inventory and it was verified across three code
reviews and two final reviews. **Read §7.1, §7.1.1 and §7.3 before you plan.**

**Your outcome: a person opens the door and plays a complete duel against the scripted opponent.**

## THE HEADLINE FINDING YOU ARE BUILDING ON: none of the 25 C# files is ported

`GoonHostService.cs:25-27` says the C# engine is the **reference implementation** and the page is a
**second client**. Practice runs entirely in the page on the loopback pair (`ui/soloDriver.js:1-18`,
`net/loopbackTransport.js:19-23`). **You are building a HOST, not a game.** If you find yourself
porting duel logic, stop — that is the census's central finding being violated.

## What already exists, verified

- The embedded WebView capability (`client/src/CcpClient.Desktop/Features/Dtrh/DtrhCapabilityProbes.cs:22`).
- The loopback serving contract (named at `client/src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs:701-707`).
- The payload glob pattern, shipped four times already (`client/src/CcpClient.Desktop/CcpClient.Desktop.csproj:50-54`).
- **`manifest` is nearly free** (§7.1.1): `Features/Dtrh/DtrhUserMedia.cs` is the port of upstream's
  enumerator, and `Features/Dtrh/DtrhProtocol.cs:271` already builds the frame **field-for-field**.
  The only delta is `received`, which is a frame-shape stub (`received: []`) because this unit has no
  media channel to fill it.

## What to build

- The payload served **by a linked read-only glob, zero bytes forked** — the fifth instance of the
  pattern at `csproj:50-54`.
- Host->page: **`init`** and **`manifest`**. Page->host: **`ready`**, **`log`**,
  **`heartbeat`/`pong`**, **`exit`/`exit-done`**.
- The frame catalogue is `GoonHostService.cs:30-53`. **The `init` shape is written field-for-field
  TWICE** — `GoonHostService.cs:300-350` and `Resources/web/goon/bridge.js:371-440` — so it is
  **transcribable, not reverse-engineered**. Transcribe it; do not invent a field.
- `caps` for this unit: `haptics:false`, `camera:false`, `assetCache:false`, `mediaTransfer:false`,
  `canHost:false`; `solo` defaults on (`bridge.js:391`).
- **Three consent defaults, read off the contract rather than invented** (§7.1): `LiveDurationSec`
  720 (`GoonContracts.cs:97`), `ToyCap` 0.7 (`:297`), `PayloadMinGapMs` 30000 (`:108`).
  **Open each line before you use it.**

## THE TRAPS

### 1. The other four title doors must REFUSE HONESTLY
Practice is **1 of 5** title-menu items; Host, Join, voice notes and media setup all lead into §6,
which is **owner-gated and unanswered**. `client/docs/capability-inventory.md:78`'s standard governs:
**"A stub that says running is a failure."** Each of the four gets a **typed refusal naming what is
missing**, never a dead button, never a silent no-op, never a "coming soon" that implies work rather
than a decision. **This is the packet's sharpest honesty requirement.** The census names this as the
thinnest joint in its own derivation (§7.1, "THE THINNEST JOINT") — read it.

### 2. NEVER fork the payload bytes
Web payloads are linked read-only out of the legacy tree by csproj glob and copied to `payload/`.
**The bytes stay owned by the legacy tree.** Copying `Resources/web/goon/**` into `client/` is a
blocking defect, not a shortcut.

### 3. `manifest` must not become a route by which the inventory leaves the machine
It is **a port-side inventory of the user's own media**. §6.2 is owner-gated on exactly that. This
unit has **no channel to leave by** and must gain none. No network call, no upload, no persistence of
the listing beyond the frame. If you find you need one, that is a **finding and a STOP**, not an
invention.

### 4. No network, no microphone, no camera, no entitlement
If the page demands any of them to reach a playable duel, **stop and report it**. Do not add an
outbound call. Do not open a sensor. Do not read a token. The census says this unit needs none of
them; if that is wrong, the finding is worth more than a workaround.

### 5. Evidence class — and this is where the packet will be judged
**A compile is not a load, and a load is not a duel.** `client/docs/verification-harness.md` governs.
State plainly, per claim, which class you discharged:
- that the payload **ships** (a build-output assertion),
- that the page **loads and the handshake completes** (bridge traffic, console, or a real capture),
- that a **duel is playable** (this needs headed evidence; `pwsh client/tools/verify/capture.ps1` and
  DISPLAY3 exist for it).

**A headed gate is never dischargeable by a headless frame.** If you reach only the first two, say so
in exactly those words and name the third as owed — that is a good outcome honestly reported, and it
is far better than a claim you cannot support. SP-129's own limits paragraph is the model.

### 6. A guard must red on the edit it names
Twelve guards this session were **descriptions outrunning their mechanisms**, and six holes were
drilled into one of them across four review rounds. Before you write an assertion, ask what edit it
must red on, **make that edit, and watch it red at the committed head** — a demonstration against an
intermediate tree is worth nothing.

### 7. Standing rules
No wall-clock waits — `TestWait` only. No TODOs. Escape pipes in table cells. Do not assert an
Avalonia v12 API you have not verified.

### 8. Divergence ids: **D250-D259**
The sibling packet holds **D260 onward**. Stay inside your range.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Features/Goon/**` (new), `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`, `client/tests/CcpClient.Tests/GoonPracticeTests.cs` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D250-D259), and `spine-tasks/SP-130-goon-practice-host/**` |
| Must not change | everything else, and specifically `client/docs/goon-game-census.md`, `client/tests/CcpClient.Tests/GoonGameCensusTests.cs`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/capability-inventory.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-130-goon-practice-host/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Goon` |
| fileScopeMustNotChange | `client/docs/goon-game-census.md`, `client/tests/CcpClient.Tests/GoonGameCensusTests.cs`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/capability-inventory.md`, `client/docs/upstream-citation-inventory.json`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-130-goon-practice-host/record.md`, `spine-tasks/SP-130-goon-practice-host/plan.md`, `spine-tasks/SP-130-goon-practice-host/floor-delta.json` |

**Pin: 2457 unit / 144 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any product edit:** the frame subset you will answer with the citation
   you opened for each; the three consent defaults re-verified against source; how the four refusing
   doors refuse; **and which evidence class you expect to reach for each of the three claims in trap
   5**. Say what you will NOT build.
2. Ship the payload by a linked glob. Prove it lands in the build output.
3. Build the host window and the bridge subset. Transcribe `init`; do not invent a field.
4. Answer `manifest` over the shipped enumerator and frame, with `received` a documented stub.
5. Make the four owner-gated doors refuse honestly, each naming what is missing.
6. **Reach as far as your evidence supports, and report the boundary in the words of trap 5.**
7. Divergences **D250-D259**.

## Completion Criteria

- The payload ships by a linked glob with **zero bytes forked**, proven from the build output.
- The bridge subset is answered, with `init` transcribed field-for-field from the two upstream copies.
- The four owner-gated doors refuse **typed**, each naming what is missing.
- Every claim carries its evidence class, and the headed claim is not discharged by a headless frame.
- No network, no microphone, no camera, no entitlement anywhere in the diff.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Port duel logic from the 25 C# files.
- Fork payload bytes into `client/`.
- Add an outbound call, a sensor, or a token read.
- Ship a door that looks like it works.
- Claim a duel is playable on headless evidence.
- Use a divergence id outside D250-D259.

## Git Commit Convention

Conventional commit, `feat(SP-130): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the frame subset and its citations, the consent defaults, how each door refuses,
the evidence class reached for each claim and the boundary you stopped at; divergences in
`client/docs/wpf-surface-reachability.md`.
