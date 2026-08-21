# SP-134 — record

Base `1a5136beb`. Product commit `b3868c168`. Branch `lane/SP-134-desktop-preflight`.
Every red demonstration below was watched at `b3868c168` and reverted;
`client/tests/CcpClient.Tests/RealDesktopCollection.cs` ends this packet byte-identical to that
commit (`git diff --quiet b3868c168 -- client/tests/CcpClient.Tests/RealDesktopCollection.cs`).

---

## 1. The desktop was contended for the whole packet, and that is this packet's own subject

`ConditioningControlPanel v6.8.1`, **pid 16712**, was running on the authoring machine before the
first command of this packet and was still running at the last. **It was not killed**: it is the
owner's application, and no agent message is consent to close it. Its state is recorded beside
every run in §6.

Measured before anything was designed (read-only `EnumWindows` + `WindowFromPoint`, screen
1646x1029 virtualised / 2880x1800 physical):

| window | pid | topmost | rect (virtualised) |
|---|---|---|---|
| `Conditioning Control Panel v6.8.1` | 16712 | yes | `(42,19)-(1605,962)` — the whole contended band |
| `Avatar Tube` | 16712 | yes | `(-151,180)-(343,826)` |
| (untitled) | 16712 | yes | `(1470,902)-(1646,928)` |
| `Shell_TrayWnd` | 9380 (explorer) | yes | `(0,981)-(1646,1029)` — below the band |

12 samples over 3 s: the product held the **foreground in 12/12** and its main window owned **all
five probed lattice points in 12/12**.

## 2. The measurement that killed the obvious detector

**The base floor run, under exactly that contention, was GREEN.**

```
node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
FLOOR OK: CcpClient.Tests: 2573/2573 total, 2 skipped
          [ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps,
           SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked]
          CcpClient.HeadlessTests: 152/152 total, 0 skipped
```

So *"a foreign topmost window owns a contended point"* was TRUE and the tree was FINE. A detector
built on presence would have reddened 212 tests that day. **That is the packet's trap 4 arriving as
a measurement instead of a risk, and it is why the design changed before a line of it was written.**

### Why both are true — the coordinator's line, and it is the cleanest one

`ChaosModeService.cs:930` returns from `RunTick` unless `_spawning && !_paused && !_manualPaused`,
and `:787` returns from `KeepChromeTopmost` again unless `_spawning`. **An idle or paused product
cannot re-assert at all.** It is PARKED in the topmost band, and a window raised after it goes
above it. A running session re-asserts on a ~1 s cadence — `:937` fires every fourth tick of the
`:503` 250 ms `DispatcherTimer`, reaching `App.Flash?.RaiseAllToFront()` at `:772` and `:802` and
thence `FlashService.cs:206-243` — and climbs back over whatever the suite raised.

**The harm is the re-assertion, not the presence.** This is also the only hypothesis that explains
wave-66's three green and six red with the same application present throughout.

## 3. The detection mechanism

`DesktopPreflight.Observe()`, called from `RealDesktopLease`'s constructor **after** the lease is
taken (so every peer `CcpClient.Tests` process and every `capture.ps1` run is already excluded —
`verification-harness.md:39`) and before the collection's first fact:

1. Create three sentinels, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, 96x96, centred on the three
   x-offsets the suite's own rigs occupy: `centre-260` (`OverlayWindowProbe.cs:286-289`), `centre`,
   `centre+300` (`InputWindowProbe.cs:387-393`).
2. **Lift** each to the top of the topmost band (§4).
3. Sample for **2500 ms**, pumping messages, asking `WindowFromPoint` -> `GetAncestor(GA_ROOT)` at
   each point and `GetForegroundWindow` once per round. **Never re-raise**: re-raising would take
   the point back every poll and mask the thing being detected.
4. Tear the sentinels down in a `finally`, on every path including the refusal.

**Refusal predicate, exactly one:** at any reading, a point was owned by a window this process does
not own. Second, distinct refusal `PREFLIGHT-BLIND` when the rig could not be built, zero rounds
were taken, or no point was ever once owned — a detector that saw nothing must never read as clean.

Everything else is **evidence carried by the refusal, never a refusal itself**: the foreground
owner's process/pid/title/class (wave-66's own identification route, `GetForegroundWindow` plus
`GetWindowThreadProcessId`), the first-loss round and elapsed ms, and readings-lost-out-of-taken.

### Timing route

`TestTimingGuardTests.cs:20-46` bans every sleeping and elapsed-time construct across
`client/tests/**` except inside `TestWait.cs`, and its `// wallclock-allow:` hatch requires editing
that guard, which is outside this packet's File Scope. So the span is driven by
`TestWait.UntilSync` with the per-poll callback taking one reading (its `PollMs` is 10, giving
~160-250 rounds across the span) and `TestWait.MonotonicNow()` — named at `TestWait.cs:47-51` as
the only permitted clock read out here — supplying the elapsed. The pump is a non-blocking
`PeekMessageW`/`TranslateMessage`/`DispatchMessageW` drain.

**The first draft of this block failed the guard**, at `RealDesktopCollection.cs:525`, because a
COMMENT spelled the banned tokens while explaining that it was avoiding them. The guard is
line-based and reads comments. Fixed by naming none of them; the replacement comment says so.

## 4. The lift, and the fact about Windows it rests on

A plain `SetWindowPos(HWND_TOPMOST)` from a process that does not own the foreground **does not
reach the top of the topmost band.** Measured on the live desktop:

| | rank (visible z-order) | owns the centre point |
|---|---|---|
| plain `HWND_TOPMOST` raise | **6** — under three `ConditioningControlPanel` windows, `Shell_TrayWnd`, `Click to Do` | no, `0 of 250` readings |
| + attach-thread-input lift | **1** | yes, `250 of 250` readings |

`GetWindowBand` returned **1 for both** our window and the product's, so this is the foreground
restriction and not a window band. (Rank 0 is explorer's `ThumbnailDeviceHelperWnd` at band 16,
structurally above everything and never at a contended point.)

The suite's own probes get above this with SP-110's ladder (`InputWindowProbe.cs:295-318`). The
pre-flight uses **the same lift minus `SetForegroundWindow` and `BringWindowToTop`**. That is
load-bearing: with `BringWindowToTop` the lift was measured **taking the foreground**
(`before=0x130938 after=<ours>`), which would mean every floor run stealing the keyboard from
whoever is at the machine for 2.5 s. Without it the foreground stayed on
`ConditioningControlPanel v6.8.1` across the whole span, and the sentinel still reached rank 1.

**This also explains the base green in §2 without any appeal to luck**: with the product parked,
the probes lift above it and hold; nothing re-asserts, so nothing takes it back.

## 5. Our own capture versus a foreign product (trap 3)

Four classes, each with a different sentence, decided by pid first and process name second:

| class | rule | what the refusal says |
|---|---|---|
| leaked rig | `pid == Environment.ProcessId`, not the sentinel | *"THIS PROCESS owns that window… the leak is in this suite and not on the machine"* |
| our harness/product | `CcpClient.Tests`, `CcpClient.Desktop`, `CcpVerify`, `testhost` | cites `verification-harness.md:39` and says to *"hunt a harness bug, not a foreign application"* |
| the shipping product | `ConditioningControlPanel` | carries `RealDesktopCollection.cs:44-48`, `FlashService.cs:206-243`, **and the idle-versus-running distinction** (`ChaosModeService.cs:930`), so a reader is not left wondering why an app they have had open all week only broke the suite today |
| foreign | anything else | says so, and cites that no in-process mechanism can exclude one |

## 6. Before and after: FAILURE SETS, not counts

| # | run | desktop | result | failure set |
|---|---|---|---|---|
| B1 | `check-floor.mjs` at base `1a5136beb` | pid 16712 up, parked | `FLOOR OK` 2573/2573 + 152/152 | **empty** |
| A1 | `check-floor.mjs` at lane (first attempt) | pid 16712 up, parked | red | **`TestTimingGuardTests.NoWallClockWaitsOutsideTheApprovedHelper`** — my own comment, §3 |
| A2 | `check-floor.mjs` at lane, after the comment fix | pid 16712 up, parked | `Passed! Failed: 0, Passed: 2583, Skipped: 2, Total: 2585`; floor reported the expected pin drift | **empty** |
| A3 | final `check-floor.mjs`, `b3868c168` + docs | pid 16712 up, parked | `Passed! Failed: 0, Passed: 2583, Skipped: 2, Total: 2585`; headless TRX `total="152" failed="0"`; the ONLY gate line is `FLOOR VIOLATION — total drift: 2585 (pin total 2573)`, which is the declared delta | **empty** |

**No run in this packet was re-run to obtain a different answer.** A1 was fixed, not retried; every
other row is a single run. **Six floor-scale runs were made and none was contended** in the sense
this packet detects: pid 16712 was parked throughout, which the pre-flight correctly does not
refuse on (§4, §7-R1b). The two runs that WERE contended are R1 and R1b, both staged deliberately.

**Before failure set = after failure set = EMPTY.** The 2 skips are unchanged and are the two
`allowedSkips` OS-property entries; nothing was added to that list. Headless is untouched:
`total="152" executed="152" passed="152" failed="0"` in the run's own TRX.

The only difference between B1 and A2 is the **declared** count drift, 2573 -> 2585.

## 7. Red demonstrations, all at `b3868c168`

### R1 — a real foreign window, re-asserting (the positive)

A separate process holding an 800x200 topmost window over the band and re-raising it every 250 ms
with the same attach lift, a direct mimic of `ChaosModeService.cs:937-940`. Run:
`dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --no-build`.

```
Collection fixture type 'CcpClient.Tests.RealDesktopLease' threw in its constructor
---- DESKTOP-CONTENDED — the SP-134 pre-flight refused before any real-desktop fact ran. A window
this process does not own took a contended point away from a top-most sentinel held there for
2500 ms: 159 of 483 readings lost across 161 rounds over 3 point(s).
  point 2 at (823,514) was owned by process 'powershell' (pid 63356), window 0xC70912,
      title 'SP-134 red demonstration contender', class 'CcpSp134Contender',
      rect (594,457)-(1051,571)
      first lost at round 2 of 161 (31 ms into the span), 159 reading(s)
      *** A foreign application: not the shipping WPF product and not ours. ***
  the foreground when the span opened: 'Conditioning Control Panel v6.8.1'
      (process 'ConditioningControlPanel', pid 16712, thread 14352, window 0x130938, ...)
Failed!  - Failed:   234, Passed:  2349, Skipped:     2, Total:  2585
```

Note that the refusal reports the foreground as the WPF product **while naming the powershell
window as the actual contender** — the two are different facts and the message keeps them apart.
Only point 2 was lost, because the contender process is DPI-aware and the test host is not, so its
800 physical px rendered as 457 virtualised px and covered only the centre offset. The detector
localised which point, which is the behaviour wanted.

### R1b — the same window, PARKED (the negative control, and the more important half)

Same process, same rect, same class, raised topmost **once** and never again:

```
Passed!  - Failed:     0, Passed:  2583, Skipped:     2, Total:  2585
```

**That pair is the whole claim.** Everything is held constant except the cadence, and the detector
follows the cadence and not the presence — which is the measured false positive of §2 held at bay.

### R2-R6 — mutations, each reverted

| # | mutation | result |
|---|---|---|
| R2 | any-sample -> majority-of-readings (`Losses.Count > 0` -> `LostReadings > readings/2`) | **RED**: `AForeignOwnerAtASingleReadingOutOfHundreds_StillRefuses_BecauseTheContenderReAssertsOnACadence` and three others, all `Assert.NotNull() Failure: Value is null` |
| R3 | the `PREFLIGHT-BLIND` branch made unreachable | **RED**: `Failed: 3, Passed: 8` — the two blind facts and the never-owned-a-point vacuity fact |
| R4 | the `pid == Environment.ProcessId` branch removed | **RED**: `AWindowOfOURSThatIsNotTheSentinel_IsNamedAsALeakedRig_NotAsSomethingOnTheMachine`, `Failed: 1, Passed: 10` |
| R5 | the `FlashService.cs:206-243` citation dropped from the refusal | **RED**: `TheShippingWpfProduct_CarriesItsStandingCitationAndTheIdleVersusRunningDistinction`, `Failed: 1, Passed: 10` |
| R6 | observation span forced to 0 ms | **RED**: the in-collection control `ThePreflightReallyObservedTheDesktop_BeforeAnyRealDesktopFactRanAndOverASpanThatCoversTheCadence`, `Assert.Equal() Failure: Expected: True Actual: False` |

R2 is the packet's central trap encoded as a standing fact. R6 is the one that matters most on the
happy path: a sampler that degenerates to zero readings would otherwise report clean over no
evidence.

## 8. The fan-out, measured

xunit v3 attributes a collection-fixture constructor failure to every test in the collection.
Measured at R1: **212 `[Fact]`/`[Theory]` declarations across 14 classes become 234 attributed
FAILED cases** (theory expansion), each carrying the identical named refusal, and **the TRX result
count is preserved at 2585** — so `check-floor.mjs` reds on the desktop rather than on pin
arithmetic.

This is the price of "fails once, AT THE FIXTURE": one CAUSE, 234 attributed reds. The alternative
gives literally one red and lets 212 OS-level facts run against a desktop that cannot certify them
in either direction. `RackPresentationTests` (11) and `VideoLetterboxTests` (9) are NOT members
despite mentioning the collection — the first only in a doc comment at `:286`, the second says so
itself at `:9-11`.

## 9. Floor

Pin **2573 unit / 152 headless**. Declared delta **+12 unit / 0 headless**
(`floor-delta.json`), so the expected observed total is **2585 / 152**.

Observed at A2: `Total: 2585`, `Failed: 0`, `Skipped: 2`; headless TRX `total="152" failed="0"`.
`2573 + 12 = 2585`. **`client/tests/floor/floor.json` was never opened.**

The 12 are 11 pure verdict facts in `DesktopPreflightVerdictTests` (confirmed by the filtered runs:
`Total: 11`) plus 1 in-collection broken-detector control in `DesktopPreflightTests`.

## 10. Guard interactions, stated rather than discovered later

- `RealDesktopCollection.cs` is in `RealDesktopCollectionGuardTests.ExemptFileNames` (`:49-54`,
  applied at `:163` and `:220`), so a sentinel calling `CreateWindowExW` there trips neither the
  membership walk nor the census. `DesktopPreflightTests.cs` creates no window and names no listed
  helper, so it is invisible to both by construction.
- `DesktopPreflightTests.cs` holds two classes and one `[Collection]` attribute between them. That
  is the lexical blind spot `RealDesktopCollectionGuardTests.cs:38-41` already names for
  `RealDesktopLeaseTests.cs`. Neither class here reaches the desktop, so nothing is mis-bound; it
  is written into the file's own summary rather than left to be inferred.
- `client/tests/floor/vacuous-shape-ledger.json` is `fileScopeMustNotChange`, so **no new fact may
  carry a silencing shape**: no bare `return;`, no `OperatingSystem.Is` in a fact body, no
  assertions-all-nested. The in-collection control therefore asserts BOTH branches
  (`Assert.Equal(expected, observation.Observed)` and four more against the same `expected`) rather
  than branching on the platform, and `DesktopPreflight.HostCanBeObserved` exists so the platform
  predicate lives outside any fact body.

## 11. What this does NOT prove

- **Nothing was rendered, interacted with, focused, animated, or heard.** No product code was
  touched; `client/src/**` and `client/tools/**` are unmodified. Neither `draw-verified` nor
  `presentation-verified` is advanced, and no headed gate is discharged.
- **No OS-level assertion was weakened.** `VideoOverlayCoexistenceTests`, `PointerCapabilityTests`,
  `InputCapabilityTests`, `GoonServingTests` and `CitationSelfTestGateTests` are untouched.
- **Windows only, by construction.** The mechanism is `user32` z-order and hit-testing. On any
  other host the pre-flight reports `NotObserved`, which the verdict treats as neither clean nor a
  refusal and which the in-collection control pins as hard as a real observation. No Linux run was
  made and none would exercise this.
- **A refusal is not proof of a clean desktop on the runs that pass.** The residues are D288:
  a contender starting after the fixture, or slower than 2.5 s; anything outside three points at
  the vertical centre of the PRIMARY display, including a second monitor; a foreground-only thief
  that never raises a topmost window; and — in the false-positive direction — a transient one-shot
  occluder refusing a run the suite would have passed, which the first-loss round and readings
  counts are there to let a reader identify, since the any-sample rule that catches the cadence
  cannot itself distinguish them.
- **`capture.ps1` does not get this pre-flight.** It takes the same lease and runs no sentinel, so
  a headed capture remains covered only against peer processes.
- **It is falsifiable**: if wave-66's condition recurs and the pre-flight reports clean while those
  three tests fail, the detector is wrong.
