# SP-126 — The haptic limb: the modules finally command the sink

## Mission

**D179 has been the port's largest parity gap since wave 57.** Upstream drives haptics from
**eighteen** command sites in modules this port has shipped; every one of them is silent here. SP-119
built the sink. SP-120 censused the sites and priced the missing layer. **Nothing has driven it.**

Your outcome: **the reachable subset of those sites commanding the sink, at the right moments with
the right envelopes — with nothing moving, because no provider is admitted, and the panel saying so.**

## THE DECISION IS TAKEN. BUILD OPTION C+.

SP-120 priced four options plus **C+** and recommended "C to start, C+ if the combination rule is
wanted". **The orchestrator has taken C+, and the reasoning is on the record so you can challenge it
with evidence rather than preference:**

- The owner's standing instruction is *"we don't care how it is done under the hood in avalonia, but
  it should keep the same behaviour as the wpf build"* — which **delegates the mechanism and fixes
  the behaviour.**
- SP-120 re-priced C against B and found the difference is **not theoretical**: `HapticService.cs:786`
  posts the flash ladder at priority 1 and `:880` posts `SubliminalTrigger` at priority 1, and
  `HapticMixer.cs:476-502` **sums within a priority group** before taking the max across groups. Those
  are **reachable, collapsed** sites here.
- The measured consequence at shipped defaults: two overlapping 0.5 transients sum to 1.0 and scale to
  **0.70 at the cap**, where MAX gives 0.5 scaling to **0.35** — **a factor of two on the level a user
  feels** (`Models/HapticSettings.cs:29` `_globalIntensity = 0.7`; `HapticMixer.cs:77` cap 0.70).

**So C alone is a knowing behavioural divergence at reachable sites, and C+ is what the instruction
requires.** C+ is C plus a shared-instant evaluator: peak-of-sum within a priority group, MAX across
groups, and the concurrency cap. **No 10 Hz loop** — the timer's own properties (LAN rate limiting,
periodic re-assert) are **delivery** concerns SP-119's seam already puts on the provider
(`HapticContracts.cs:70-73`).

**If you find evidence that C+ is wrong — that peak-of-sum is unreachable here, or that the evaluator
cannot be built without a timer — that is a finding and you stop.** Do not silently build C.

## THE CENSUS IS YOUR MAP — USE IT, DO NOT RE-DERIVE IT

`client/docs/haptic-limb-census.md` maps **18 upstream sites to 5 port trigger points**: 7 collapsed,
3 present, **8 absent-by-decision each with the decision quoted**, 0 unexplained. It was verified
twice independently and its enumeration is pinned by `HapticSiteCensusTests`.

**Build the reachable subset only.** An `absent-by-decision` site has no trigger here — wiring one
would mean inventing the trigger, which is the opposite of parity. **If you believe a site the census
called absent is actually reachable, say so with the citation and stop** — do not quietly add it.

## THE CENTRAL TRAP: nothing will move, and that must stay obvious

No provider is admitted, so `SetOutputsAsync` refuses every call. **That is correct and must not be
worked around.** The limb's job is to command at the right moments with the right envelopes; the
sink's job is to refuse honestly until the owner admits a provider.

**So your evidence cannot be "the device buzzed".** It must be that the right command, with the right
level and shape, reached the sink at the right moment — proved against a **recording** sink that
**records and never transforms**. SP-108's lesson is the exact hazard: a test double whose `Write`
clamped laundered the very clamp the fact existed to test. **Record raw; assert against upstream's
own arithmetic.**

## THE OTHER TRAPS

### 1. The dot must stay honest
SP-119 gave the rack dot three values, of which **`Live` is unreachable by construction** because it
would have to mean something is being SENT. **A limb does not change that** while no provider is
admitted — verify it and say so. **`Views/**` is CLOSED to you**; if you conclude the dot must change,
that is a finding and a board row.

### 2. Upstream's own STOP is missing on three teardown paths — do not copy the bug
`StopVideoBackgroundVibeAsync` is reached **only** through `Cleanup()`; `Stop()`, `CloseAll()` and
`ForceCleanup()` carry zero haptic references, and `ForceCleanup` is the panic-key path. **Panic-key a
video upstream and the layer stays latched** — and a comment beside the stop claims it "runs on every
teardown path". **Put your stop where it actually runs on every path**, and record the divergence.

### 3. The envelopes are arithmetic, not vibes
The flash ladder is 8 rungs, `intensity_i = max(start * 0.7^i, 0.06)` at 450 ms spacing
(`HapticService.cs:784-787`). The luminance layer is a latched continuous value with
`autoZeroMs = clamp(lifetimeMs, 100, 30_000)`. **Port the arithmetic and pin it**; a doc comment says
the ladder "decays over ~2s" and the code spans **~3503 ms** — the code wins, and SP-120 recorded it.

### 4. Standing rules
No wall-clock waits — `TestWait` only. Equivalence claims inadmissible until every consumer is
enumerated by `grep`. **A claim about what a guard cannot do needs the same evidence as a claim about
what it can.** Both gates alone.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Haptics/**`, `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/tests/CcpClient.Tests/**` (new haptic-limb facts and the existing effect facts they touch), `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-126-haptic-limb/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/{Overlay,Input,Audio,Video,Pointer,Glyph,Entitlement,Scheduling,Lifecycle}/**`, `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/haptic-limb-census.md`, `client/docs/execution-census.md`, `client/tools/**`, `client/tests/CcpClient.Tests/{HapticSiteCensusTests,ExecutionCensusTests,RackPresentationTests,FypCensusTests}.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

**The csproj is CLOSED. If you need a package, STOP — that is the owner's open decision, not yours.**

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-126-haptic-limb/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Effects` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/haptic-limb-census.md`, `client/docs/execution-census.md`, `client/tools/**`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/tests/CcpClient.Tests/RackPresentationTests.cs`, `client/tests/CcpClient.Tests/FypCensusTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-126-haptic-limb/record.md`, `spine-tasks/SP-126-haptic-limb/floor-delta.json` |

**Pin: 2332 unit / 144 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** the C+ evaluator's shape and where it lives; which of the 5 trigger points you
   wire and the envelope each carries; how you prove peak-of-sum without a device; and your stop
   placement given upstream's missing one.
2. Build the vocabulary layer. **No timer, no 10 Hz loop.**
3. Wire the reachable subset. Each site's envelope pinned against upstream's arithmetic.
4. **Prove peak-of-sum bites** — two overlapping same-priority transients must combine to the summed
   peak, not the max, and a fact must red if that becomes MAX.
5. **Prove the stop runs on every teardown path**, including the one upstream misses.
6. Verify the dot's `Live` is still unreachable and say why.
7. Sweep every predicate; divergences from D210.

## Completion Criteria

- The reachable subset commands the sink at the right moments with upstream's envelopes.
- Peak-of-sum proven, and proven to red if it degrades to MAX.
- The stop runs on every teardown path this port has.
- Nothing moves, nothing claims to, and the panel still says so.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Add a package or edit the csproj.
- Wire a site the census called absent-by-decision.
- Build C silently if you conclude C+ is wrong — stop and report.
- Let a recording double transform what it records.
- Change the rack dot.

## Git Commit Convention

Conventional commit, `feat(SP-126): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the C+ build, the per-site envelope evidence, the peak-of-sum proof, the stop
placement and its divergence from upstream, and the sweep.
