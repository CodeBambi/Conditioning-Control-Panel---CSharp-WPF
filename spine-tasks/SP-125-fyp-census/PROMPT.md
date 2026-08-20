# SP-125 — For You Feed, censused before anyone tries to build it

## Mission

**Four big v6.7 product surfaces have sat undecomposed on the board since 2026-08-11**, and they are
the largest remaining chunk of "the same behaviour as the WPF build". Nobody has scoped any of them.

**For You Feed is the tractable one** — `Services/Fyp/` is **3 new files** against Goon Game's 25
files plus 184 payload files (`client/docs/task-board.md`, rows for both). It is the one a single
packet can honestly survey.

Your outcome: **a census of For You Feed against the shipping source, ending in a build/refuse verdict
with an inventory — not an estimate.**

## THE MODEL, AND WHY IT IS THE MODEL

**SP-112 refused Bubble Pop with an inventory rather than an estimate** — five message types and none
a mouse message, one window handle, no move seam, a predicate requiring the inverse of what a bubble
needs — and that refusal is what made SP-113 writable at all. **SP-117 and SP-120 did the same.**
Scoping beats guessing, and a refusal with a census is a result while a refusal without one is an
excuse.

**Commit `plan.md` with your method before you map anything.** That ordering is the standard here and
it has caught real defects three waves running.

## THE CENTRAL TRAP: this port has been wrong about a count FOUR TIMES, always the same way

8 → 13 → 14 → 18 on the haptic sites, and **every correction came from widening the UNIVERSE, never
from reading harder.** Each search was a file LIST somebody assembled by hand; thirteen missed the
DEFAULT video engine because one file was not in the list, and fourteen missed a module this port had
already SHIPPED.

**So enumerate by DIRECTORY, recursively, and say what your universe is before you count.** `Services/Fyp/`
is where the row says the code is. **Verify that claim** — the row is from a sync ledger, and this
port has found board rows wrong before. Include the payload tree, and check whether anything outside
`Services/Fyp/` drives it.

## THE OTHER TRAPS

### 1. A payload tree is not a feature, and this one is SHARED
`Resources/web/fyp/` is web content the legacy tree owns. `client/Directory.Build.props` links web
payloads read-only out of the legacy tree by csproj glob and copies them to `payload/` — **the bytes
stay owned by the legacy tree and are NEVER forked into `client/`** (root `CLAUDE.md`). Count the
payload, state how the port would serve it, and **do not propose copying it.**

### 2. Say which of the port's capabilities it needs, by name
The port has seven landed capabilities: overlay, input, audio, video, pointer, glyph, haptics. **For
each thing For You Feed does, name the capability that covers it or state precisely what is
missing** — that is what made SP-112's refusal usable. A gap is a finding, not a blocker.

### 3. "Ghost mode" is in the row's title and nobody has said what it is
Find out from the source. If it is a privacy-relevant behaviour — anything that changes what is
recorded, shown to others, or sent anywhere — **say so explicitly and flag it for the owner rather
than folding it into a size estimate.**

### 4. Cite `File.cs:line` for every claim, and verify each citation against the shipping tree
SP-113 found `AppSettings.cs` citations wrong by ~530 lines *and* in the wrong path. SP-120 found four
citations in its own packet that did not say what the packet claimed. **Open every line you cite.**

### 5. `client/src/**` is CLOSED
This packet writes no product code. If the census proves something needs building, that is the
finding and the next packet is authored from it.

### 6. Standing rules
No wall-clock waits. Equivalence claims inadmissible until every consumer is enumerated by `grep`.
Both gates alone.

## File Scope

| | |
|---|---|
| May change | `client/docs/fyp-census.md` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY), `client/tests/CcpClient.Tests/FypCensusTests.cs` (new), and `spine-tasks/SP-125-fyp-census/**` |
| Must not change | everything else, and specifically `client/src/**`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/verification-harness.md`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/tests/CcpClient.Tests/RackPresentationTests.cs`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

**Do not add a new shipped type under `client/src`** — you may not anyway, and a sibling packet in
this wave is removing the reason that would have been fatal.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-125-fyp-census/floor-delta.json` |
| fileScopeMustChange | `client/docs/fyp-census.md` |
| fileScopeMustNotChange | `client/src/**`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/verification-harness.md`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/tests/CcpClient.Tests/RackPresentationTests.cs`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-125-fyp-census/record.md`, `spine-tasks/SP-125-fyp-census/floor-delta.json` |

**Pin: 2309 unit / 144 headless.** `sum-deltas` before deleting any delta file. **Keep every artifact
inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Commit `plan.md` BEFORE mapping**: your universe stated as directories, your method, and how a
   "needs capability X" verdict is decided so it is not a judgement call.
2. **Verify the board row's own evidence** — 3 files in `Services/Fyp/`, plus the payload tree. Report
   what you actually find; if the row is wrong, that is the headline.
3. Map every behaviour to a landed capability or to a named gap, citing both sides.
4. **Say what "ghost mode" is**, from source, and whether it is privacy-relevant.
5. Count the payload and state how the port would serve it **without forking the bytes**.
6. **Verdict: buildable, buildable-in-part, or refused** — with the inventory that proves it, and a
   size the next packet can be authored against.
7. Pin the enumeration so it cannot drift a fifth time; divergences from D207 onward.

## Completion Criteria

- Every behaviour mapped to a capability or a named gap, with verified citations on both sides.
- The universe stated as directories and the board row's own evidence checked.
- "Ghost mode" explained from source and flagged if privacy-relevant.
- A verdict with an inventory, not an estimate.
- No product code; both gates green; build 0 warnings / 0 errors.

## Do NOT

- Estimate where you can enumerate.
- Propose forking the payload bytes into `client/`.
- Inherit the board row's file count without checking it.
- Write product code.

## Git Commit Convention

Conventional commit, `docs(SP-125): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the method, the verified inventory, the capability mapping, the ghost-mode finding
and the verdict; the census itself in `client/docs/fyp-census.md`; divergences in
`client/docs/wpf-surface-reachability.md`.
