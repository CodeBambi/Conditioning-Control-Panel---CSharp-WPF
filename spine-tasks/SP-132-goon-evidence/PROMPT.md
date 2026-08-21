# SP-132 — Open the door nobody has opened

## Mission

SP-130 built a Goon practice host and landed it at **evidence class "the payload ships, ONLY"**.
`client/docs/task-board.md`'s row says it plainly: **the PRACTICE button opens a surface nobody has
ever seen.**

**Your outcome: move that surface up the evidence ladder, one rung at a time, and claim exactly the
rungs you reach.**

Three rungs exist and they are NOT equivalent:

1. **The origin SERVES.** An in-process HTTP GET against the running `LoopbackServer` retrieves
   `payload/goon/index.html` and a sample of its assets. **No browser, no headed run, no monitor.**
2. **The page LOADS and the handshake COMPLETES.** The window opens a real WebView2, the page boots,
   and `init` + `manifest` are answered and acknowledged.
3. **A duel is PLAYED.** A human clicks through a complete duel against the scripted opponent.

**Rung 3 is a HUMAN gate and is NOT yours.** Do not attempt it and do not claim it. Rungs 1 and 2 are
yours.

## WHY THIS WAS BLOCKED BEFORE, AND WHY IT IS NOT NOW

SP-130's final review found three bars. **Two were PACKET SCOPE, not the machine:**
`client/tools/verify/**` was closed to that packet, and the demo flag needed a fourth file outside
its grants. **Both are open to you.** The third bar — that no harness can *play a duel* — is real and
applies only to rung 3.

**The harness works and has landed pixel evidence before.** `client/tools/verify/capture.ps1` carries
a machine-wide real-desktop lease (`%TEMP%/ccp-real-desktop.lease`), UIAutomation, and a `DwmFlush`
fence; SP-122 used it to produce the port's first `presentation-verified` evidence. Its `-Surface`
list is a `ValidateSet` parameter with no goon entry — **that is the whole obstruction.**

## What to build

### 1. The in-process GET — do this FIRST, it is cheap and it is a real rung
A test that starts `GoonParticipant`, issues a real HTTP GET at the bound origin, and asserts the
bytes come back. **Assert on content, not on a status code alone.** Include at least one asset that
is NOT `index.html`, and one file the server must REFUSE — `LoopbackServer` denies four files in this
tree by extension, and SP-130 named them (D258).

**This closes a board row and it needs nothing but a socket.**

### 2. `--goon-demo`, wired properly or not at all
SP-130 was granted this flag and **correctly refused to half-wire it**, because it needs
`Program.cs` (parse, thread into `BuildAvaloniaApp`, thread into `new App(...)`) and a flag in
`App.axaml.cs` alone would be *a dial nothing can turn* (D259). **Wire it through all four sites,
mirroring `--intake-demo` exactly**, or leave it unbuilt and say so. **A partially wired flag is a
worse outcome than none.**

### 3. The headed capture
Add a goon surface to `capture.ps1`'s `ValidateSet` and `checks.json`, drive the window, and capture
**the page actually rendered inside it**. Follow SP-122's shape: take the lease, fence with
`DwmFlush`, confirm state through UIAutomation **before** any pixel, and make every check
**demonstrably fail on a capture of the wrong state** — a check that cannot fail on a wrong capture
is the defect this project has found fourteen times.

## THE TRAPS

### 1. Claim the rung you reached and NOT the one above it
`client/docs/verification-harness.md` governs. **A headed gate is never dischargeable by a headless
frame**, and **serving bytes is not loading a page, and loading a page is not playing a duel.** If
the page fails to boot, that is a finding worth more than a workaround — **report it, do not route
around it.**

### 2. The window starts a REAL embedded browser
That is why SP-130 never showed it in a test. Expect first-run WebView2 behaviour, a runtime that may
be absent, and a navigation that may fail. **Every one of those outcomes must be TYPED and reported,
never swallowed.** A capture that silently photographs an error page and passes is the worst possible
result.

### 3. The microphone residual is live on this path
D250: the voice screen is reachable and its recorder asks the browser for the microphone directly, and
this host can neither grant nor deny. **Do not walk into it during a capture**, and if a permission
prompt appears, **that is evidence and you record it** — it is the residual made visible.

### 4. Do not touch what SP-130 got right
`Features/Goon/GoonDoors.cs`'s refusal text was corrected at the wave-65 land after a blind audit
found it overstated a microphone claim. **Read it before you touch anything near it**, and if your
work needs it changed, that is a finding you report rather than an edit you make.

### 5. Standing rules
No wall-clock waits — `TestWait` only. No TODOs. Every new guard watched red **at the committed
head**, with the SHA. Escape pipes in table cells.

### 6. Divergence ids: **D265-D274**

## File Scope

| | |
|---|---|
| May change | `client/tools/verify/capture.ps1`, `client/tools/verify/checks.json`, `client/src/CcpClient.Desktop/Program.cs`, `client/src/CcpClient.Desktop/App.axaml.cs`, `client/src/CcpClient.Desktop/Features/Goon/**`, `client/tests/CcpClient.Tests/GoonServingTests.cs` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D265-D274), and `spine-tasks/SP-132-goon-evidence/**` |
| Must not change | everything else, and specifically `client/tools/citations/**`, `client/tools/coverage/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/goon-game-census.md`, `client/docs/capability-inventory.md`, `client/tests/CcpClient.Tests/GoonPracticeTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-132-goon-evidence/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/GoonServingTests.cs` |
| fileScopeMustNotChange | `client/tools/citations/**`, `client/tools/coverage/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/goon-game-census.md`, `client/docs/capability-inventory.md`, `client/tests/CcpClient.Tests/GoonPracticeTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-132-goon-evidence/record.md`, `spine-tasks/SP-132-goon-evidence/plan.md`, `spine-tasks/SP-132-goon-evidence/floor-delta.json` |

**Pin: 2547 unit / 152 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit:** which rung each piece of work targets; what evidence class it
   produces; **which edit each new check must red on**; and what you will do if the page does not boot.
2. Build rung 1 — the in-process GET, including a refused file.
3. Wire `--goon-demo` through all four sites, or leave it and say why.
4. Add the goon surface to the harness and take a real capture, SP-122's shape.
5. **State the rung you reached in the packet's own words.** If you reach 1 but not 2, say so exactly.
6. Divergences **D265-D274**.

## Completion Criteria

- Rung 1 discharged: the origin serves, proven by content, including a refused file.
- `--goon-demo` wired through all four sites or explicitly unbuilt with the reason.
- Every new check demonstrated to fail on a capture of the wrong state.
- The rung reached is claimed exactly, and the rung above it is named as owed.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Claim a duel was played.
- Discharge a headed claim with a headless frame.
- Half-wire the demo flag.
- Swallow a navigation failure, a missing runtime, or a permission prompt.
- Edit `GoonDoors.cs`'s refusal text without reporting why.
- Use a divergence id outside D265-D274.

## Git Commit Convention

Conventional commit, `feat(SP-132): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the rung reached, the evidence class for each claim, the red demonstrations with the
head SHA, and anything the page did that you did not expect.
