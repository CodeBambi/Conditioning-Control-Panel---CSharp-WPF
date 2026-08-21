# SP-128 — The graded-run award path, and the defect it must not import

## Mission

SP-127 censused Trainer Card and returned **BUILDABLE-IN-PART** with the buildable unit named
precisely: **the graded-run award path — 34 upstream lines, no assets, no OS interop, no owner
decision, cross-platform by construction.** This packet builds it.

`client/docs/trainer-card-census.md` is your map and it was verified twice. **The consumer side is
5 of 16 members present, all five pure arithmetic** (`Features/Intake/IntakeQuizRun.cs:133-159`);
**eleven are absent — every stateful and every awarding member.** SP-058's *"computes and logs but
raises nothing"* is verified at the port's own wiring point.

Your outcome: **a graded run that awards what upstream awards, at the same thresholds, without
importing upstream's own defect.**

## THE CENTRAL TRAP: upstream's `honor_roll` FIRES EARLY, and the port must not copy it

`honor_roll` counts **DISTINCT** categories. Upstream's consumer adds `e.Category` **raw** into a
**default `HashSet<string>`** — ordinal and case-sensitive — while the two producers of that
deliberately source-agnostic event **disagree about casing**: the graded-run path normalises with
`Trim().ToLowerInvariant()`, and the classic quiz path passes an **unnormalised PascalCase enum
name**. So `"sissy"` and `"Sissy"` fill two of three distinct slots, **the award fires a category
early, and the set is persisted so it never un-fires.**

SP-127 bounded it honestly: **latent, not reproduced** — the classic launcher is
`Visibility="Collapsed"`. **The port matches upstream verbatim on the side it has ported, so the port
does not carry the defect today.**

**Do not port the bug.** Normalise at the point the port owns, pin the distinctness with a fact that
**fails if the comparer becomes ordinal**, and record the divergence: this is a place where the port
is deliberately correct and upstream is not. **That is a divergence, not a silent fix — write it
down.**

## THE THRESHOLDS ARE ARITHMETIC — port them, do not paraphrase

From the census, each to be re-verified against the shipping source before you use it:
- **`top_of_the_class`** at the **90%** bar.
- **`honor_roll`** over **DISTINCT** categories.
- **`held_back`** deliberately **fail-streak-only**.

**Open every line you cite.** SP-127 shipped three wrong citations in a packet whose own table graded
others' citations, and they survived because **its pin watched a path and not a number**. Yours must
pin what it claims.

## THE OTHER TRAPS

### 1. Eleven absent members is the size, and the packet is only the award path
Do not drift into the wardrobe, the achievement ledger, `held_back`'s dead-on-arrival status, the card
page or the banners. **The census named those as residue, row by row.** If the award path genuinely
needs one of them, that is a finding and a board row.

### 2. `client/src/**` is OPEN here and the census document is NOT
`client/docs/trainer-card-census.md` is **CLOSED to you** — it is a verified artifact and its
enumeration is pinned. **If you find it wrong, that is a finding and you say so; you do not edit it.**

### 3. A guard must pin what its name claims
Nine guards this session passed over exactly what they existed to catch, every one a **description
outrunning its mechanism**. Before you write an assertion, ask what edit it must red on — then **make
that edit and watch it red.** A claim about what your guard cannot do needs the same evidence as a
claim about what it can.

### 4. Standing rules
No wall-clock waits — `TestWait` only. Equivalence claims inadmissible until every consumer is
enumerated by `grep`. Both gates alone. Escape pipes in table cells.

### 5. Divergence ids: **D226 onward, and no higher than D239**
The last wave collided because two packets were both told "from D210". Your range is **D226-D239** and
the sibling packet's is **D240 onward**. Stay inside it.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Features/Intake/**`, `client/src/CcpClient.Desktop/Features/Progression/**` (new if needed), `client/src/CcpClient.Desktop/Persistence/**`, `client/tests/CcpClient.Tests/**` (new award facts and the intake facts they touch), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D226-D239), and `spine-tasks/SP-128-graded-run-awards/**` |
| Must not change | everything else, and specifically `client/docs/trainer-card-census.md`, `client/tests/CcpClient.Tests/TrainerCardCensusTests.cs`, `client/src/CcpClient.Desktop/{Haptics,Effects,Overlay,Input,Audio,Video,Pointer,Glyph,Views,Lifecycle,Session}/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/fyp-census.md`, `client/docs/haptic-limb-census.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-128-graded-run-awards/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Intake` |
| fileScopeMustNotChange | `client/docs/trainer-card-census.md`, `client/tests/CcpClient.Tests/TrainerCardCensusTests.cs`, `client/src/CcpClient.Desktop/Haptics/**`, `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/fyp-census.md`, `client/docs/haptic-limb-census.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-128-graded-run-awards/record.md`, `spine-tasks/SP-128-graded-run-awards/floor-delta.json` |

**Pin: 2399 unit / 144 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** the three thresholds re-verified against source with citations you opened;
   where you normalise and why that point is the port's to own; **which edit each new guard must red
   on**; and what the eleven absent members mean for what you are NOT building.
2. Build the award path. Thresholds ported as arithmetic, not paraphrase.
3. **Pin distinctness so it fails if the comparer becomes ordinal** — and demonstrate that failure.
4. Record the deliberate divergence from upstream's early-firing `honor_roll`.
5. Sweep every predicate; discharge or withdraw every equivalence claim.
6. Divergences **D226-D239 only**.

## Completion Criteria

- The graded-run award path awards what upstream awards at upstream's thresholds.
- `honor_roll`'s distinctness is case-insensitive **and pinned to fail if that regresses**.
- The deliberate divergence recorded, not silently fixed.
- Nothing built outside the award path.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Import upstream's early-firing defect.
- Edit the census or its guard.
- Drift into the wardrobe, the ledger or the card page.
- Ship a guard you have not watched red.
- Use a divergence id outside D226-D239.

## Git Commit Convention

Conventional commit, `feat(SP-128): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the three thresholds and their citations, the normalisation decision, the
red-on-regression demonstration, and the sweep; divergences in
`client/docs/wpf-surface-reachability.md`.
