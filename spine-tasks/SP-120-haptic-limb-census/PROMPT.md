# SP-120 — The haptic limb, censused before a line of it is written

## Mission

SP-119 landed the sink and left `Effects/**` byte-identical, saying in its own contract that giving
the modules a limb is a later packet. **This is not that packet either.** The acceptance that was
written at the SP-119 land — *"closed by giving those thirteen sites a haptic limb"* — was
**undischargeable**, and the orchestrator has already corrected it on the board
(`client/docs/task-board.md`, row D179).

Your outcome: **a committed census mapping every upstream haptic command site to a port trigger
point, and the vibe-vocabulary question priced precisely enough for the owner to settle it.**

## WHY A CENSUS AND NOT THE LIMB — three things are already known and each one kills the naive packet

**1. At least three of the sites have NO trigger in this port**, each absent by a recorded prior
decision rather than an oversight:
- `VideoService.cs:4585` and `:4673` fire from **attention checks**, which
  `client/src/CcpClient.Desktop/Effects/MandatoryVideoEffect.cs:363-365` states are *"not ported and
  not shown as dead controls"*.
- `FlashService.cs:1915` fires from a **click on a flash**, impossible here: the port's flash
  surfaces are unconditionally click-through, and `Effects/FlashSurfacePresenter.cs:293-297` says
  WPF's catching arm *"exists to serve pop / hydra / XP mechanics this port does not have"*.
- The three flash spawn arms (`:1453`, `:1480`, `:1516`) are **mutually exclusive branches of one
  spawn** upstream and **collapse into one presenter path** here (`Effects/FlashImagesEffect.cs:35`).

**2. THERE IS A FOURTEENTH SITE AND IT IS ON THE DEFAULT ENGINE.**
`ConditioningControlPanel/Services/Video/VideoService.Browser.cs:452` calls
`StartVideoBackgroundVibeAsync()` from `OnBrowserPlaying()`. The browser engine is the leg
`VideoService.cs:2403-2410` routes to **first**. The thirteen are correct for the three files that
were named; **a limb built from that list alone silently drops the start for the default video
engine.** Verify this yourself and treat the enumeration as *evidence to re-derive*, not a given —
it has now been corrected twice.

**3. The sites do not speak the seam's language.** They command a **mixer**: `FlashDecayVibeAsync()`
is an **8-rung decay ladder**, `intensity_i = max(start * 0.7^i, 0.06)` at 450 ms spacing
(`HapticService.cs:784-787`); `SetLayer(HapticLayer.Luminance, …, autoZeroMs:)` is a **latched
continuous layer** (`FlashService.cs:1627-1629`); events carry **modes** (Constant/Pulse) and
**priorities** that arbitrate. `Services/Haptics/Core/HapticMixer.cs` — **note the `Core/`; the path
without it does not exist** — runs ONE 10 Hz loop that MAXes continuous layers, takes peak-of-sum
within a priority group and MAX across groups, caps concurrency at 4 with priority eviction, soft-ramps
rises only, then applies master intensity, a 0.06 floor and a 0.70 cap.

**SP-119's seam carries a bare 0..1 level keyed by device+actuator, with no duration, no mode, no
priority and no mixer, and it refused those on purpose** (`spine-tasks/SP-119-haptic-seam/record.md:91`,
`:132-134`). **So a limb is the missing layer between the two — and where that layer lives is an
architecture decision no lane may take.** You will price it. You will not choose it.

## THE CENTRAL TRAP: a census that quietly becomes an implementation

SP-117's census and SP-116's protocol are the model, and both were committed **before** the first
measurement or edit. **Commit `plan.md` with the mapping method before you map.** The failure here is
drifting from "map the sites" into "well, I'll just wire the easy ones" — which takes the vocabulary
decision by implication and hands the owner a fait accompli.

**`client/src/**` IS CLOSED TO YOU.** If the census proves a limb needs a product change, that is the
finding, and it is what the next packet is authored from.

## THE OTHER TRAPS

### 1. Upstream's own STOP is missing on three teardown paths — do not copy the bug
`StopVideoBackgroundVibeAsync` is reached **only** through `Cleanup()`. I verified that `Stop()`,
`CloseAll()` and `ForceCleanup()` contain **zero** haptic references, and `ForceCleanup` is the
panic-key, session-lock, suspend and wedge-escape path. The start at `VideoService.cs:2580` passes
**no `autoZeroMs`**, so the layer latches unbounded (`HapticService.cs:848`). **Panic-key a video
upstream and the video layer stays on.** Record it as a divergence with a recommendation; the port's
own row 28 already says STOP deserves harder treatment than START.

### 2. Behaviour the port must NOT "fix": the readout fires when nothing can vibrate
No site checks entitlement and no site checks for a device — the mixer refuses centrally
(`HapticMixer.cs:191-204`, `:843`), and `Announce` runs **after** `Play` unconditionally
(`HapticService.cs:390`, `:791`, `:849`). So an unentitled user with no toy still watches the
activity readout scroll. **That is user-observable behaviour**, and a port that adds a connected-check
at a call site changes it. Census it; do not design it away.

### 3. Three upstream discrepancies are already known — confirm or refute, do not inherit
- `HapticService.cs:761` says the ladder *"decays over ~2s"*; the arithmetic at `:782-787` spans
  ~3503 ms. **Code wins, but say which the port would copy.**
- `TriggerBambiFreeze` (`SubliminalService.cs:289`) does **not** check `SubAudioAudible` while its two
  siblings do (`:221`, `:380`).
- D197's gate disagreement is already filed; do not re-file it.

### 4. Settings live where the archaeology says, not where you assume
Haptic settings are `ConditioningControlPanel/Models/HapticSettings.cs`, **not** `AppSettings`. A
citation into the wrong file is the SP-113 class and it has recurred; **verify every line you cite.**

### 5. Standing rules
Equivalence claims inadmissible until every consumer is enumerated by `grep`. A tolerance is the size
of the defect it hides. No wall-clock waits. Both gates alone.

## File Scope

| | |
|---|---|
| May change | `client/docs/haptic-limb-census.md` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY), `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs` (new), and `spine-tasks/SP-120-haptic-limb-census/**` |
| Must not change | everything else, and specifically **`client/src/**` (this packet writes NO product code)**, `client/tests/floor/**`, `client/tests/CcpClient.HeadlessTests/**`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/docs/task-board.md`, `client/docs/port-digest.md`, `client/docs/verification-harness.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-120-haptic-limb-census/floor-delta.json` |
| fileScopeMustChange | `client/docs/haptic-limb-census.md` |
| fileScopeMustNotChange | `client/src/**`, `client/tests/floor/**`, `client/tests/floor/floor.json`, `client/tests/CcpClient.HeadlessTests/**`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/docs/task-board.md`, `client/docs/port-digest.md`, `client/docs/verification-harness.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-120-haptic-limb-census/record.md`, `spine-tasks/SP-120-haptic-limb-census/floor-delta.json` |

**Pin: 2247 unit / 141 headless.** `sum-deltas` before deleting any delta file. **Keep every artifact
inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Commit `plan.md` BEFORE the first mapping**: your method, and how a "port trigger point" is
   decided so the verdicts are not judgement calls.
2. **Re-derive the site enumeration yourself** across the whole `Services/{Flash,Video,Subliminal}`
   family, including `VideoService.Browser.cs`. Report the count you find and reconcile it with
   fourteen. **If it is not fourteen, that is the headline.**
3. **Map every site** to `present` / `absent-by-decision` / `collapsed`, one citation per verdict on
   BOTH sides. For every `absent`, name the port decision that made it absent.
4. **Price the vocabulary layer.** At least three options, each with its cost in files and its effect
   on the SP-119 seam (does it change? does it wrap?), and what each forecloses. **Recommend one and
   stop.** Say which upstream behaviours each option can and cannot reproduce — the decay ladder, the
   auto-zero latch, priority arbitration, the 0.06 floor, the 0.70 cap.
5. **Pin the enumeration** so it cannot drift a third time.
6. Record the divergences, including the missing-stop finding and the readout-fires-anyway behaviour.

## Completion Criteria

- Every command site mapped with a cited verdict on both sides, and the count re-derived independently.
- The vocabulary layer priced with at least three options, one recommended, none taken.
- The enumeration pinned by a fact.
- No product code written; both gates green; build 0 warnings / 0 errors.

## Do NOT

- Write a limb, wire a call site, or touch `client/src/**`.
- Choose where the vocabulary layer lives.
- Accept the thirteen-site list without re-deriving it.
- Copy upstream's missing stop, or "fix" the readout that fires when nothing can vibrate.

## Git Commit Convention

Conventional commit, `docs(SP-120): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the method, the re-derived count, the priced options and the recommendation; the
mapping itself in `client/docs/haptic-limb-census.md`; divergences in
`client/docs/wpf-surface-reachability.md`.
