# SP-117 — The last three, censused before one is chosen

## Mission

Twelve modules run. **Three remain: Visuals, Haptics, Scheduler.** Nobody has surveyed what they actually need since SP-108, and that survey was corrected twice — once against a reviewer.

**SP-112's census is the model.** It refused Bubble Pop with an inventory rather than an estimate — five message types and none a mouse message, one window handle, no move seam, a predicate requiring the inverse of what a bubble needs — and that refusal is what made SP-113 writable at all. **Scoping beats guessing.**

Your outcome: **a census of all three against the shipping source, then the one that can ship — or a recorded finding that none can.**

## What is already known, and must be verified not assumed

- **Scheduler is probably not a session module at all.** SP-108 found it presses `START` from *outside* a session (`ConditioningControlPanel/Views/Tabs/StudioTabView.xaml.cs` rack entry; the service drives the engine rather than running under it). If that holds, it does not belong to the effect spine and saying so is the finding.
- **Haptics needs a device stack** (SP-108). Verify what stack, and whether this machine has one — **a missing device is a named limit, never a blocker and never a skip.**
- **Visuals has never been scoped at all.** `StudioTabView.xaml.cs:496` is its rack entry. Start there.

## THE CENSUS COMES FIRST

**Do not write product code before the census is committed.** SP-116 committed its protocol before its first measurement and that ordering is now the standard. Commit `plan.md` with all three inventories, then choose.

For each of the three, report: the shipping service and its real surface; every capability it needs; which of the port's **six** landed capabilities (overlay, input, audio, video, pointer, glyph) covers it; and precisely what is missing. **Cite `File.cs:line` for every claim** — and verify each citation against the shipping tree, because SP-113 found `AppSettings.cs` citations wrong by ~530 lines *and* in the wrong path.

## THE OTHER TRAPS

### 1. A refusal with a census is a result; a refusal without one is an excuse
If none of the three can ship, **that is a legitimate outcome** — but only with the inventory that proves it. SP-112's refusal named message types, window handles and predicates. Match that standard.

### 2. Every equivalence claim is a proof obligation
`client/docs/port-workflow.md` carries the rule after **five** false claims in four waves: inadmissible until every consumer of the mutated symbol is enumerated by `grep` and discharged by name.

### 3. A tolerance is the size of the defect it will hide
Also in `port-workflow.md`, after SP-115. If you need a tolerance, ask what defect is exactly that size.

### 4. Do not disturb six landed surfaces or twelve landed modules
All capability folders are **closed to editing**. Their facts pass unchanged.

### 5. Run both gates, alone
`check-warnings.mjs` then `check-floor.mjs`, never concurrently. A plain `dotnet build` cannot see warnings on a rebuild.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-117-last-three/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/{Overlay,Input,Audio,Video,Pointer,Glyph}/**`, `client/tests/floor/floor.json`, `client/tests/floor/check-floor.mjs`, `client/tests/floor/check-warnings.mjs`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

**If a module needs a capability folder changed, that is a finding and a board row — not a licence.**

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-117-last-three/floor-delta.json` |
| fileScopeMustChange | `client/tests` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Input/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/src/CcpClient.Desktop/Video/**`, `client/src/CcpClient.Desktop/Pointer/**`, `client/src/CcpClient.Desktop/Glyph/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-117-last-three/record.md`, `spine-tasks/SP-117-last-three/floor-delta.json` |

**Pin: 2067 unit / 121 headless.** `sum-deltas` before deleting any delta file. **Keep every artifact inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Census all three and commit `plan.md` before any product edit.** Report which can ship and why.
2. Ship the one that can, with a rack row and a truthful dot — or record why none can.
3. If you ship: prove the six landed surfaces survive anything you put on screen or take focus with.
4. **Prove it bites**, then sweep every predicate; discharge or withdraw every equivalence claim.
5. Divergences from D171.

## Completion Criteria

- All three censused with `File.cs:line` evidence, each citation verified against the shipping tree.
- One module ships, or a recorded finding naming what each needs.
- Twelve landed modules' facts pass unchanged.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Write product code before the census is committed.
- Refuse a module without an inventory.
- Edit a capability folder.
- Claim any human saw, heard or clicked anything.
- Run the gates concurrently.

## Git Commit Convention

Conventional commit, `feat(SP-117): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the three-way census, the choice and its reasoning, and the sweep; divergences in `client/docs/wpf-surface-reachability.md`.
