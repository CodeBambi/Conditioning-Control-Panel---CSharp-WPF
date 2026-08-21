# SP-129 — Goon Game, the last unscoped surface

## Mission

**Goon Game is the last v6.7 product surface nobody has scoped.** For You Feed was censused and
refused with an inventory; Trainer Card was censused and returned buildable-in-part, and its unit is
being built in the sibling packet. Her Room is owner-gated on twelve open questions. **This is the
one remaining unknown.**

`client/docs/task-board.md`'s row records the evidence: `Services/GoonGame/` (**25 files**) and
`Resources/web/goon/` (**184 payload files** — a reviewer confirmed that count exactly), described as
real-time 1v1 duels. **It is the largest of the four and the row says "decompose before scheduling".**

Your outcome: **a census ending in a build/refuse verdict with an inventory, sized so a packet can be
authored against it — or a recorded finding that it cannot be, with the inventory that proves it.**

## USE THE TOOL THAT ALREADY EXISTS

**SP-127 committed `spine-tasks/SP-127-trainer-card-census/walk.mjs`** and a reviewer verified its
structural properties by running it: its only positional argument is a **directory**, so a
hand-assembled file list is *not expressible*; its exclusions are a frozen constant with **no
`--exclude` flag**, so no source file can be dropped by name; and it cross-checks each walk against
`git ls-files`, **reporting disagreements rather than reconciling them**.

**Reuse it.** Copy it into your own packet folder if you need to adjust it, and say what you changed
and why. **Do not hand-assemble a file list** — that habit has produced a wrong count five times
(8 → 13 → 14 → 18 on haptics, 3 → 9 on FYP), and SP-127 found a sixth class: a row whose counts were
**arithmetically exact and still meant the wrong thing.**

## THE CENTRAL TRAP: check what a number COUNTS before checking whether it counts correctly

SP-127's finding is the one to carry: *"a wrong number gets corrected; a right number that means
something else gets trusted."* Its row's `Views/Controls/` count was exact — and **3.5% that
feature**, with six of its files belonging to modules already shipped, and the row **missed the
directory the feature actually lived in** because that feature was twelve partial-class members of one
window.

**So for every count you inherit or derive: say what fraction of it is this surface.** A directory is
not a feature.

## THE OTHER TRAPS

### 1. "Real-time 1v1 duels" is networking, and networking is owner territory
If this surface opens a socket, contacts a server, or transmits anything about the user, that goes in
its **own owner-flagged section** and is **never folded into a size** — the shape SP-125 used for the
third-party API it found. Anything touching consent, sensors, networking, persistence or entitlement
gets the same treatment.

### 2. 184 payload files are legacy-owned
Web payloads are linked read-only out of the legacy tree by csproj glob and copied to `payload/`;
**the bytes are never forked into `client/`.** Count them, say how the port would serve them, and do
not propose copying them.

### 3. Map to the port's landed capabilities by name, with platform cells
Seven capabilities: overlay, input, audio, video, pointer, glyph, haptics — **plus shipped in-window
precedent**, which SP-127 established as a legitimate anchor because a UI-over-state surface maps onto
no OS capability and a narrow rule would **manufacture a refusal**. `"Avalonia can do it"` is not an
anchor. Every row carries `Windows: proven|unproven` / `Linux: proven|unproven` with the gate named —
and **`Linux: unproven` is the default**, since there is no WSL distro on this machine.

### 4. Your pin must re-derive from the shipping bytes
Roots are directories **in the test**, counts recompute every run, a missing reference tree **FAILS**
rather than skips. A guard that checks the document against itself is the vacuity
`HapticSiteCensusTests` names in its own words — and SP-127's pin **watched a path and not a number**,
which is how three wrong citations survived it. **Pin what you claim.**

### 5. Open every line you cite
`sed -n` the exact path before writing any `File.cs:line`. Three packets running have shipped wrong
citations, twice inside their own headline findings.

### 6. `client/src/**` is CLOSED — no product code

### 7. Divergence ids: **D240 onward**
The sibling packet holds **D226-D239**. Last wave two packets were both told "from D210" and collided
on seven ids at merge; `validate-wave.mjs` checks paths, not id ranges. Stay above D239.

## File Scope

| | |
|---|---|
| May change | `client/docs/goon-game-census.md` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D240 onward), `client/tests/CcpClient.Tests/GoonGameCensusTests.cs` (new), and `spine-tasks/SP-129-goon-game-census/**` |
| Must not change | everything else, and specifically `client/src/**`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/fyp-census.md`, `client/docs/trainer-card-census.md`, `client/docs/haptic-limb-census.md`, `client/docs/verification-harness.md`, `client/tests/CcpClient.Tests/{ExecutionCensusTests,RackPresentationTests,HapticSiteCensusTests,FypCensusTests,TrainerCardCensusTests}.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-129-goon-game-census/floor-delta.json` |
| fileScopeMustChange | `client/docs/goon-game-census.md` |
| fileScopeMustNotChange | `client/src/**`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/fyp-census.md`, `client/docs/trainer-card-census.md`, `client/docs/haptic-limb-census.md`, `client/docs/verification-harness.md`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/tests/CcpClient.Tests/RackPresentationTests.cs`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `client/tests/CcpClient.Tests/FypCensusTests.cs`, `client/tests/CcpClient.Tests/TrainerCardCensusTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-129-goon-game-census/record.md`, `spine-tasks/SP-129-goon-game-census/plan.md`, `spine-tasks/SP-129-goon-game-census/floor-delta.json` |

**Pin: 2399 unit / 144 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Commit `plan.md` BEFORE mapping**: your universe as directories, your method, and how a
   capability verdict is decided so it is not a judgement call.
2. **Verify the row's evidence — 25 files and 184 payload files — and say what fraction of each is
   actually this surface.** If a count is exact and misleading, that is your headline.
3. Map every behaviour to a capability, a shipped in-window precedent, or a named gap, with platform
   cells and citations you opened on both sides.
4. **Any networking, consent, sensor, persistence or entitlement behaviour goes in its own
   owner-flagged section**, never folded into a size.
5. Count the payload and say how the port would serve it **without forking the bytes**.
6. **Verdict with an inventory**, and a size the next packet can be authored against.
7. Pin the enumeration against the shipping bytes; divergences **D240 onward**.

## Completion Criteria

- Every behaviour mapped with verified citations on both sides and platform cells.
- Both inherited counts checked, with the this-surface fraction stated for each.
- Networking and any owner-gated behaviour flagged in its own section.
- A verdict with an inventory, not an estimate.
- The enumeration re-derives from the shipping bytes and pins what it claims.
- No product code; both gates green; build 0 warnings / 0 errors.

## Do NOT

- Hand-assemble a file list.
- Estimate where you can enumerate.
- Propose forking the payload bytes.
- Fold an owner-gated behaviour into a size.
- Use a divergence id at or below D239.

## Git Commit Convention

Conventional commit, `docs(SP-129): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the method, the verified inventory with this-surface fractions, the capability
mapping with platform cells, the owner-flagged sections and the verdict; the census in
`client/docs/goon-game-census.md`; divergences in `client/docs/wpf-surface-reachability.md`.
