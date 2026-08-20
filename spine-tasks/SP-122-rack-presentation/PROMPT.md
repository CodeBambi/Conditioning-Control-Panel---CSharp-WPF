# SP-122 — Somebody looks at the rack, for the first time in fifteen modules

## Mission

**Fifteen of fifteen rack rows are ported and NOBODY HAS EVER SEEN ONE.** Every claim about every
module in this port rests on a headless frame, a window read-back, or a document. The owner's
standing goal is *"the same behaviour as the WPF build"*, and nothing in this port can currently
tell you whether the Studio rack **looks like the WPF rack at all**.

The harness for this already exists and self-tests: `client/tools/verify/capture.ps1` (tier-2
capture), `client/tools/verify/CcpVerify` (tier-3 deterministic check), `client/tools/verify/self-test.ps1`.
It has **four captured PNGs** in `client/tools/verify/artifacts/` and **three discharged
`presentation-verified` checks** in `client/tools/verify/checks.json` — all of the SHELL
(`rail-door` borders, `dashboard` background), none of the rack, none of any module.

Your outcome: **`presentation-verified` discharged for the Studio rack — or a recorded finding
naming exactly what stops it.**

## THE CORRECTION THAT MADE THIS PACKET WRITABLE

An earlier framing said "nobody has ever seen a pixel". **That was wrong** and the record now says
so: three shell checks are genuinely discharged with real captures on disk. **The real gap is
narrower and still serious: no RACK surface has a named check, and no human has confirmed any of
them.** Do not repeat the overclaim; measure what is missing and say only that.

## THE CENTRAL TRAP: this needs a PRODUCT change before it can capture anything

`capture.ps1:16` accepts exactly `ValidateSet('dashboard', 'rail-door')`. Its state drive depends on
the per-door layout probe emitted by `client/src/CcpClient.Desktop/Views/MainWindow.axaml.cs:275-281`
(`ProbeLine(ShellRoute route)`), and that is emitted **only for `ShellRoutes.Declared`**. **A Studio
surface has no probe, so there is nothing to aim a capture at.**

So this packet touches product code, narrowly and on purpose: **a probe for the rack, of the same
shape as the door probe.** `Views/**` is open to you for that and nothing else. **A probe is an
observation seam, never a behaviour change** — if you find yourself altering what the rack DOES to
make it capturable, stop and record it.

## THE OTHER TRAPS

### 1. TAKE THE REAL-DESKTOP LEASE — this is a correctness requirement, not politeness
SP-107 proved that concurrent real-desktop work corrupts evidence machine-wide: one failure read
"expected 0, got 676161", which was another run's flash counted as this one's. The suite's fixtures
serialise through a machine-wide lease at `%TEMP%/ccp-real-desktop.lease`
(`client/docs/verification-harness.md`). **`capture.ps1` runs OUTSIDE the test harness and does not
take it today.** Another lane runs in this wave. **Take the lease for the duration of every capture,
and say in your record that you did** — a capture that raced a real-desktop test is not evidence, and
this closes a latent hole rather than merely avoiding one.

### 2. A screenshot is not a check
SP-115's lesson: `PrintWindow` is structurally blind to transparent-versus-black, and that was
**asserted, not confessed**. A PNG proves a capture happened. **The check must state what it would
have to see to FAIL**, and you must show it failing — capture a deliberately wrong state and prove
the check rejects it. A check that can only pass proves nothing (SP-113).

### 3. Do not claim a human saw it
`felt-verified`'s sibling problem. `presentation-verified` means composited pixels were read back
and checked, **not** that a person looked and judged it right. Upstream comparison by eye is a
MANUAL gate — name it, do not discharge it.

### 4. The flake is bounded, not zero
0.20% fenced / 9.5% suite (`client/docs/task-board.md`). Real-desktop reads must go through the
`DwmFlush` fence at `FlashPixelProbe.CaptureDesktop` or an equivalent — SP-116 measured 34 misses in
1200 unfenced reads and 0 in 1500 fenced. **An unfenced screen read is a defect, not a flake.**

### 5. Standing rules
No wall-clock waits — `TestWait` only. Equivalence claims inadmissible until every consumer is
enumerated by `grep`. A tolerance is the size of the defect it hides. Both gates alone.

## File Scope

| | |
|---|---|
| May change | `client/tools/verify/**`, `client/src/CcpClient.Desktop/Views/**` (the rack PROBE only), `client/docs/verification-harness.md`, `client/tests/CcpClient.Tests/RackPresentationTests.cs` (new), `client/tests/CcpClient.HeadlessTests/**`, and `spine-tasks/SP-122-rack-presentation/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/{Overlay,Input,Audio,Video,Pointer,Glyph,Haptics,Effects,Entitlement,Scheduling,Session,Lifecycle}/**`, `client/tools/coverage/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, both floor scripts, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

**No effect module's behaviour changes. If a rack row cannot be captured without changing what it
does, that is the finding.**

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-122-rack-presentation/floor-delta.json` |
| fileScopeMustChange | `client/tools/verify` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/tools/coverage/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Haptics/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-122-rack-presentation/record.md`, `spine-tasks/SP-122-rack-presentation/floor-delta.json` |

**Pin: 2270 unit / 141 headless.** `sum-deltas` before deleting any delta file. **Keep every artifact
inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** what a rack probe must expose and why that is an observation and not a
   behaviour change; which rack states you will capture; **what each check would have to see to
   FAIL**; and how you take the real-desktop lease.
2. Add the rack probe. Narrow, `Views/**` only, no effect touched.
3. Extend `capture.ps1` and `checks.json`. Capture with the lease held and the read fenced.
4. **Prove each check bites** — capture a wrong state and show rejection.
5. Record the evidence class honestly: what `presentation-verified` now covers, and that no human
   has compared it to WPF.
6. Divergences from D207 onward.

## Completion Criteria

- At least one RACK surface `presentation-verified` with a check proven to fail on a wrong state.
- The real-desktop lease taken by the capture path, and said so.
- No effect module behaviour changed; the probe is an observation seam.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Claim a human saw or approved anything.
- Read the screen unfenced.
- Change what any module does in order to capture it.
- Extend `checks.json` with a check that cannot fail.
- Repeat the "nobody has ever seen a pixel" overclaim.

## Git Commit Convention

Conventional commit, `feat(SP-122): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the probe justification, the capture protocol including the lease, the bite proof
per check, and the honest ceiling; the evidence class in `client/docs/verification-harness.md`.
