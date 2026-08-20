# SP-116 — record

Branch `lane/SP-116-flake-characterisation`, base `11036bbc`, worktree
`.claude/worktrees/agent-a76eaa63a39ac984b`.
Floor: pin **2062 unit / 121 headless**; observed **2065 unit / 121 headless**; declared delta
**+3 unit / +0 headless** (`floor-delta.json`). 2065 = 2062 + 3 and 121 = 121 + 0, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-116-flake-characterisation`.
Two skips, both pre-existing (`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); **none added, and
`allowedSkips` was never opened**. `client/tests/floor/floor.json` was never opened.
Build: 0 warnings, 0 errors (`check-warnings.mjs`, forced non-incremental).
`client/src/**` was never edited.

---

## 0. THE HEADLINE

> **Two of the three strands are one fault, and it is not contention.** A layered top-most window
> that this process has just shown and painted is sometimes ABSENT from a `CAPTUREBLT` read of the
> screen taken immediately afterwards, because nothing ordered the read behind the compositor.
> Measured: **34 misses in 1200 unfenced reads, 0 in 1500 fenced ones**, and on every single miss
> the window OWNED its own centre point by `WindowFromPoint`.
>
> **The third strand is a different fault with the same shape one level up:** one stop releases a
> module's work THREE times and the third arrives on a thread-pool continuation after `Disarm`
> returned, so a test that puts something back on the surface behind the module's back is racing
> its own module's teardown. SP-106 §6.2 named this and retired it; it is correct.

Both are missing edges. Neither is a timeout, a retry or a contended desktop.

## 1. THE PROTOCOL, STATED BEFORE IT WAS RUN

`plan.md` was written and committed (`a68528a8`) before the first measurement and before the first
product edit. Verbatim from it:

- **One sample = one full `dotnet test` of `client/tests/CcpClient.Tests`, sequential, one at a
  time, `--no-build`, TRX preserved.** Red = one or more failed results in the TRX. On red the
  harness pulls every failed test's fully-qualified NAME and MESSAGE out of the TRX — the artifact
  SP-106 lost and SP-115 did not capture.
- **60 runs per arm.** Distinguishing 1-in-7 (p = 0.143) from 1-in-20 (p = 0.05) one-sided at
  alpha ~ 0.05, power ~ 0.8 needs n = 49.2, so 50 is the floor and 60 is what was run. Decision
  line at n = 60 is 6 reds.
- **Load is not simulated** at the suite level: xunit's own parallelism over ~2060 tests is the load
  that produced all three strands.
- **The A/B arm alternates base and lane strictly, in one window** (SP-112's lesson).

Harnesses, committed: `repeat.mjs` (the sample loop), `ab.mjs` (the interleaved arm),
`sweep-control.cs.txt` (the high-rate mechanism rig, see §3.1).

## 2. THE RATE AT BASE

**3 red in 60 (5.0 %)**, 95 % Clopper-Pearson interval **1.0 %–14.0 %**. Per-run verdict list:
`base-arm.log`, one line per launched run; nothing was re-run and no run is missing.

| what | count |
|---|---|
| runs | 60 |
| red | 3 (r24, r42, r47) |
| distinct failing test classes | 1 — `FlashDrawTests` |
| `SpiralOverlayEffectTests` reds | **0** |

**This answers the packet's own question.** 3/60 is at p = 0.05 and is four below the decision line
for p = 0.143. So on this machine, on this day, **the base tree's rate is the ~5 % SP-107 measured
and NOT the 1-in-7 SP-115 read**; fifteen runs could not tell those apart and SP-115 said so.

**And the 1-in-7 SP-115 attributed to `SpiralOverlayEffectTests` did not reproduce at all**: zero in
60. That bounds it at **4.9 % (95 % one-sided)** and not at zero. §4 is what was done about it
anyway, and why the run count is not the reason.

The three reds are two sub-signatures of one event:

- **r24, r47** — the instrument's own CONTROL failed. `DesktopCaptureIsLive` came out **False**
  while the subject's composited read in the same run succeeded, and the failure text says so in as
  many words: `capture live = False, the desktop showed the flash = True`, and
  `capture can see a painted layered window = False, and the composited desktop at the top half of
  the flash reads 0x30C0F0 against the frame's 0x30C0F0` — the two AGREE and the assertion is an
  iff. Three facts fail together because all three take that control as their oracle.
- **r42** — the control succeeded and the SUBJECT's end-to-end read returned zero.

Same physical event on two different windows: a freshly shown and painted layered top-most window
missing from an immediate screen read.

## 3. STRAND 1 AND 3: THE VERDICT SP-107 WAS WAITING FOR, AND THE MECHANISM UNDER IT

SP-107 §4 left four verdicts printable and said plainly that the board decision "should not be taken
until the next occurrence reports which of the four verdicts fired". **Run 42 reported.**

```
a desktop capture can see a painted layered window on this machine = True, and while the flash was
up the composited desktop carried 0 pixels of the image's colour against an expected area of at
least 112614. The screen read RETURNED 5184000 pixels (SP-107), of which 43430 were the same colour
as the first one (0x151515). The display metrics were 1646x1029 virtual / H(1646, 2880)
V(1029, 1800) when the flash was PLACED and H(1646, 2880) V(1029, 1800) when the screen was READ —
they agree = True.
```

- 5,184,000 = 2880 x 1800 exactly: **a complete desktop came back. The allocation hypothesis is
  dead** (SP-107 had already refuted it once; this is the second refutation).
- 43,430 of 5,184,000 uniform, 0.8 %: **not a blank or asleep display.**
- The placement pair and the capture pair are identical: **the geometry desynchronisation SP-107's
  review called "the stronger one" is dead too.**

That leaves SP-107's fourth verdict — "genuinely not composited where a user would see it" — which
is a description, not a mechanism. §3.1 is the mechanism.

### 3.1 The mechanism, measured at 1200 samples instead of 3

`FlashDrawObservations.RunControl`'s window lifecycle was replicated in a temporary rig
(`sweep-control.cs.txt`; it lived in `client/tests/CcpClient.Tests/` for the experiment and was
deleted before the gate) so the one event could be sampled hundreds of times per minute instead of
once per 53-second suite run: register class, create a `WS_EX_LAYERED` top-most popup,
`SetLayeredWindowAttributes(255)`, `SetWindowPos(HWND_TOPMOST | SWP_SHOWWINDOW)`, `BitBlt` a solid
colour into its DC, win the point, `PrintWindow`, then read the screen. Eight busy pool work items
stand in for the suite's own parallelism. Raw log: `sweep-control.log`.

| mode | what changed | misses | of |
|---|---|---|---|
| **A** | exactly as shipped | 8, then 11 | 300, 300 |
| **C** | paint AFTER the top-most raise instead of before | 7 | 300 |
| **D** | `GdiFlush()` before the read | 8 | 300 |
| **B** | `GdiFlush()` + `DwmFlush()` before the read | 0, then 0 | 300, 300 |
| **E** | `DwmFlush()` alone | **0** | 300 |
| **A, post-fix** | as shipped, through the fixed `CaptureDesktop` | 0, then 0 | 300, 300 |

**No fence: 34 in 1200 (2.83 %). With the fence: 0 in 1500** (95 % one-sided upper bound 0.20 %).
P(0 in 1500 | p = 0.0283) is about `e^-42`.

**Every miss looks like this, and the detail is the whole argument:**

```
miss #1 at iteration 75: winner=0x3950652 isOurs=True attempts=1 rendersOwnColour=True
captured=132300 controlPixels=0 majority=0x26171E majorityCount=14323
```

- `isOurs=True`, `attempts=1` — **the window owned its own centre point on the first raise. The
  occlusion hypothesis is REFUTED**, not assumed away. This is the control that would have failed
  had the reading been wrong, and it is the one every previous packet lacked: SP-107 §5 and
  SP-115 §9 both reached for "a foreign topmost window" and neither could test it.
- `rendersOwnColour=True` — the paint had landed; the window's own bits are the control colour.
- `captured=132300` — a full read came back.
- `controlPixels=0`, `majority=0x26171E` — the read returned the desktop BEHIND the window.

So the window was up, on top, owner of the point and painted, and the compositor had not published
it. **`GdiFlush` alone changes nothing, so GDI batching is not the mechanism; `DwmFlush` alone is
the entire effect.**

### 3.2 The fix, and why it is not a wait

`FlashPixelProbe.CaptureDesktop` takes a `DwmFlush` fence before the `BitBlt(SRCCOPY|CAPTUREBLT)`.
It is the single choke point every composited desktop read in the suite passes through — flash,
glyph and video alike (`GlyphSurfaceObservations.cs:720`, `VideoSurfaceObservations.cs:547,649`) —
so one edge covers all of them.

**`DwmFlush` blocks until the compositor's next present has consumed the outstanding surface
updates.** That is an edge on the PRODUCER's own completion, the pixel-world twin of awaiting a
task, and it carries no deadline this suite chose. Nothing is re-read. Nothing is re-asserted. No
window was widened. No assertion moved. Every fact downstream still gets exactly one screen read and
must still be exactly right about it.

**The banned fixes were available and were not taken.** A `TestWait` poll around the composited read
would have made the fact "within N seconds the flash arrived" instead of SP-100's measured
"immediately", which SP-107 correctly called retry-until-green wearing a hat; an `allowedSkips`
entry would have been a quarantine for a property of the MOMENT; moving the fact behind the headed
gate would have cost the port its only OS-level composited evidence. None was needed, because the
fault was a missing edge and edges are free.

**The pin:** `FlashDrawTests.EveryCompositedReadIsOrderedBehindTheCompositor_OrEveryNumberBelowIsACoinFlip`.
Deleting the fence makes `LastCompositorFenceResult` keep its `FenceNotTaken` sentinel, so the fact
reds on EVERY run instead of restoring a 5 % intermittent that took three packets to name. It is one
implication — live capture implies fence held — and it is not vacuous: where composition is off,
`DesktopCaptureIsLive` is already false and every composited expectation in that file is already
`false == false`. A DWM that REFUSES the fence and a fence that was never TAKEN are different values
and the failure text prints both.

### 3.3 Membership was checked and was not the problem

`RealDesktopCollection` carries 14 classes plus the lease's own; `RealDesktopCollectionGuardTests`
binds membership mechanically over a source walk and its helper census is current through SP-115.
The only file outside the collection matching any raw window call is
`SubliminalSurfacePresenterTests.cs`, whose single hit is the word `SetWindowPos` inside a COMMENT.
**Nothing is missing from the collection**, and the base arm's failure texts never once show a
foreign window winning a point. This lead was checked and came back clean; it is recorded because
the packet named it, not because it produced anything.

## 4. STRAND 2: THE DISCRIMINATOR, RE-OPENED AND CORRECT

`SpiralOverlayEffectTests.DisarmReleasesTheWorkUNCONDITIONALLY_EvenWhenItThoughtItWasNotArmed`
touches no OS, so "the desktop was contended" cannot explain it. SP-106 §6.2 named `InlineDispatch`
aliasing lock-free fields against the asserting thread and then retired the hypothesis for want of
evidence. **It is right, and the sharper statement is about a COUNT rather than about fields.**

**One stop releases the work three times** (`OwnedSessionEffect.cs`):

1. `Disarm`'s own call, synchronous, on the caller's thread (`:212`);
2. the generation's cancellation registration (`:352`) — on the disarming thread when the parked
   body has already registered, and inline on the POOL thread when it has not, because registering
   an already-cancelled token invokes the callback immediately;
3. the tail after `await stopped.Task` (`:357`), which is a thread-pool continuation because the
   TCS carries `RunContinuationsAsynchronously` (`:345`), so it lands an unbounded time after
   `Disarm` returned. `Disarm` does not clear `_generation`, so the guard at `:417` lets it through.

`SpiralOverlayEffect.ReleaseWork` posts the withdraw through `EffectSignal.Post`
(`SpiralOverlayEffect.cs:268`). In the product that is `Dispatcher.UIThread.Post` and the surface is
only ever touched on one thread. **In the rig it is `InlineDispatch`, which runs the action on
whatever thread posted it — the pool thread.** The test then re-engages the surface directly
(`SpiralOverlayEffectTests.cs:340` — the ONLY direct `Surface.Engage` in the entire project), so the
tail's `Engaged` guard turns true again and the withdraw lands on the thing the test just put up.
`Showing` and `Withdrawals` are plain non-volatile fields of a rig double read from the test thread.

**The census that says why this is one test and not twenty.** Every continuous module's
`ReleaseWork` guards on its own surface state and `PacedSessionEffect`'s is an
`Interlocked.Exchange`, so a second release is a no-op — unless something re-engages the surface in
between, and only this test does. The two `GenerationProbe` facts in `MovingEffectSpineTests` read
their counter only AFTER the re-arm that invalidates the stale tail's generation, so they are safe;
they were checked and left alone.

**The fix is the edge, and the precedent is in the sibling file.**
`MovingEffectSpineTests.AStaleTeardownArrivingAfterARestart_…` already awaits the old generations'
completions for exactly this reason and says so at `:171-174` and `:196-197`. The spiral fact now
does the same through `TestWait.Until(Task)` — the ONE approved bounded signal wait — and then
asserts the typed `Cancelled` outcome. It is not a wait for an assertion to pass: the operation is
finished or the window fails loudly, and the sequence after it is single-threaded.

**Two new facts make the mechanism deterministic instead of a story.** Both use a `TailProbe` whose
own counter is `Interlocked` and whose cross-thread fields are `Volatile`, because an instrument
must not carry the defect it measures.

- `ONESTOPRELEASESTHEWORKTHREETIMES_AndTheTHIRDIsOnAPoolThreadAfterDisarmReturned` — exactly 3
  releases by the time the owned task completes. Deterministic in both branches of the registration
  race, and it pins the ordering that makes awaiting `Completion` a real edge.
- `ATailThatLandsAfterSomethingWasPutBackUp_TAKESITDOWN_WhichIsWhyThePostDisarmAwaitIsNotOptional`
  — the probe PARKS its third release on a gate until the test opens it, so the interleaving that
  used to be the pool's coin flip happens on every run. **The control that fails if the reading is
  wrong is the wait itself:** if the tail never reached `ReleaseWork` after `Disarm` returned, the
  arrival signal never completes and `TestWait` fails loudly with `CONDITION-NEVER-TRUE` rather than
  passing quietly. The gate is opened in a `finally`, so a failed assertion cannot leave a pool
  thread parked.

**This is NOT a product defect and `client/src/**` stayed shut.** On a real dispatcher the withdraw
is posted to the UI thread, every touch of a surface is single-threaded by construction, and a
second withdraw of an already-withdrawn surface is the no-op the contract requires
(`OwnedSessionEffect.cs:298-302`). The defect is in a TEST that reaches around its own module.

**What this does NOT claim.** The natural rate of this race was **0 in 60** at base, so no rate
reduction is claimed for it and none can be: 0/60 bounds it at 4.9 %, not at zero. What is claimed
is that the race is removed by construction and that the hazard is now pinned deterministically.

## 5. BEFORE AND AFTER, SAME PROTOCOL

Every launched run counted. Nothing re-run, nothing discarded.

| arm | tree | runs | red | rate |
|---|---|---|---|---|
| BASE (`base-arm.log`) | `11036bbc` | 60 | 3 | 5.0 % |
| A/B, base half (`ab-arm.log`) | `11036bbc` | 30 | 2 | 6.7 % |
| A/B, lane half (`ab-arm.log`) | `2c3316da` | 30 | **0** | 0 % |
| **base, all** | | **90** | **5** | **5.6 %** |

The A/B arm is **strictly alternating in one window** — base run, lane run, base run — with the six
touched test files checked out of each commit and rebuilt before each run, which is the only design
that separates "the tree changed" from "the desktop drifted". Both of its base reds are the two
sub-signatures of §2 and the lane half has none.

**Provenance, because it changes what the numbers license.** The 60-run BASE arm and the A/B arm's
base half were taken on `11036bbc` exactly. The A/B arm's lane half was taken on `2c3316da`, which
is the committed lane head; the doc and this record were written afterwards and touch no code. The
arm was interrupted by the harness being killed after pair 28 and the final two pairs were run
immediately afterwards in the same window with the same script — so `ab-arm.log` carries two `A/B
TOTAL` lines and the totals above are counted from the per-run lines, which is why they are stated
that way.

**What 30 lane runs can and cannot say.** 0 red in 30 bounds the lane's rate at **9.5 %** (95 %
one-sided) and NOT at zero. It cannot on its own demonstrate an improvement over 5.6 %: those
counts overlap. **The strong evidence is the mechanism-level measurement — 34 in 1200 against 0 in
1500 through the shipped code path — and this record does not pretend otherwise.**

## 6. WHAT REMAINS UNEXPLAINED

- **Nothing from strand 1/3 remains unexplained**, which is the change. The five verdicts are now
  discriminated by printed numbers and the fifth has a mechanism, a measurement and a pin.
- **Strand 2's OBSERVED RATE remains unexplained.** SP-115 saw it 1 in 7 at base; this lane saw it
  0 in 60 on the same machine five days later. The mechanism is real and now deterministically
  reproducible, but what made it fire seven times more often in SP-115's window is not known and 60
  runs cannot recover it. A machine's pool-thread scheduling under a different concurrent load is
  the obvious candidate and it is a guess.
- **SP-106's three LOST names remain lost** and this packet did not recover them. `repeat.mjs`
  exists so the next one is not lost: it prints every failed test's name and message out of the TRX.
- **The residual rate after the fix is not zero and is not measured to be zero.** 0 in 30 suite runs
  and 0 in 1500 fenced reads is what there is.

## 7. WHAT THIS WORK DOES NOT PROVE

- **It is a compile-and-tier-1 result.** No interaction, rendering, audio, focus, window behaviour
  or animation claim is discharged. No headed capture was taken and no `presentation-verified` gate
  moved; a headless frame never discharges a headed gate and nothing here tried.
- **It proves nothing on Linux.** `DwmFlush` is a Windows call inside a probe every one of whose
  facts is Windows-only by construction, and the fence is not reached at all on a non-Windows host.
  The composited reads that matter refuse by design there.
- **It proves nothing about a machine with a different compositor state.** Every number was taken on
  one Windows 11 host at 175 % scale with DWM composition ON. On a session with composition
  disabled the fence is refused, is reported as refused, and the composited facts are already
  `false == false`.
- **It proves nothing about concurrent gate runs.** Every sample here was sequential and alone;
  SP-107's cross-process lease is untouched and its 3-way concurrency numbers are not re-baselined.
- **It does not prove any earlier land was wrong.** It proves that a five-percent slice of them were
  green for a reason nobody could state, which is a different and smaller claim.
- **It does not make three consecutive greens a proof.** It makes them worth more than they were,
  for the second time in ten packets.

## 8. Files changed

| File | Why |
|---|---|
| `client/tests/CcpClient.Tests/FlashPixelProbe.cs` | The `DwmFlush` fence before every composited read, and the readable fence result. |
| `client/tests/CcpClient.Tests/FlashDrawObservations.cs` | Records whether the fence was held in the run. |
| `client/tests/CcpClient.Tests/FlashEndToEndObservations.cs` | Same, for the end-to-end read: the fifth verdict behind a count of zero. |
| `client/tests/CcpClient.Tests/FlashDrawTests.cs` | NEW fact pinning the fence; the four-verdict message gains the fifth; one stale "no wait anywhere" comment corrected. |
| `client/tests/CcpClient.Tests/SpiralOverlayEffectTests.cs` | The post-disarm ordering edge on the module's own completion. |
| `client/tests/CcpClient.Tests/MovingEffectSpineTests.cs` | TWO new facts and the `TailProbe` that makes the teardown tail deterministic. |
| `client/docs/verification-harness.md` | What the floor now claims about composited reads, the fence, and what the numbers license. |
| `spine-tasks/SP-116-flake-characterisation/plan.md` | The protocol, committed before the first measurement. |
| `spine-tasks/SP-116-flake-characterisation/repeat.mjs` | The sample loop; names failures out of the TRX. |
| `spine-tasks/SP-116-flake-characterisation/ab.mjs` | The interleaved A/B arm. |
| `spine-tasks/SP-116-flake-characterisation/sweep-control.cs.txt` | The high-rate mechanism rig, archived as evidence rather than shipped. |
| `spine-tasks/SP-116-flake-characterisation/*.log`, `floor-01.txt` | Every launched run's verdict, and the floor's own output. |
| `spine-tasks/SP-116-flake-characterisation/floor-delta.json` | +3 unit / +0 headless. |

## 9. Three things a reviewer should check first

1. **That nothing retries or skips.** Grep the diff for `retry`, for a loop around a verdict, for
   `allowedSkips`, for a new `TimeSpan` literal. There is one new wait site and it is
   `TestWait.Until(Task)` on a module's published completion; there is one new blocking call and it
   is `DwmFlush`, which waits on the compositor's present rather than on a clock.
2. **That no assertion was weakened.** The composited facts in `FlashDrawTests` are byte-identical
   except for one enriched failure MESSAGE and one corrected comment; `git diff 11036bbc --
   client/tests/CcpClient.Tests/FlashDrawTests.cs` should show one new `[Fact]`, one message, one
   comment and nothing else.
3. **That the fence pin really bites.** Delete the `TakeCompositorFence()` call from
   `FlashPixelProbe.CaptureDesktop` and
   `EveryCompositedReadIsOrderedBehindTheCompositor_OrEveryNumberBelowIsACoinFlip` must red on the
   next run, naming `FenceNotTaken`.
