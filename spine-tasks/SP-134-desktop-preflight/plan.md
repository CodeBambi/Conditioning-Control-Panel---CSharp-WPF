# SP-134 — plan checkpoint (Review Level 3, before any product/test edit)

Base `1a5136beb`, branch `lane/SP-134-desktop-preflight`, worktree
`.claude/worktrees/agent-aee41feeee7da22f2`. Nothing in `client/**` has been edited at the time
this file is written; the only writes so far are this file and a throwaway probe script in the
session scratchpad.

---

## 0. The desktop is contended RIGHT NOW, and I measured it before designing anything

`ConditioningControlPanel v6.8.1`, **pid 16712**, is live on this machine. Measured with a
read-only `EnumWindows` + `WindowFromPoint` probe (scratchpad, not committed), screen 1646x1029:

```
=== VISIBLE TOPMOST NON-CLOAKED WINDOWS ===
  pid=16712 ConditioningControlPanel class='HwndWrapper[ConditioningControlPanel;;019386f6-...]'
            title='Conditioning Control Panel v6.8.1'  rect=(42,19)-(1605,962)      <-- TOPMOST
  pid=16712 ConditioningControlPanel class='HwndWrapper[ConditioningControlPanel;;b40266b5-...]'
            title='Avatar Tube'                        rect=(-151,180)-(343,826)    <-- TOPMOST
  pid=16712 ConditioningControlPanel class='HwndWrapper[ConditioningControlPanel;;1a239cc7-...]'
            title=''                                   rect=(1470,902)-(1646,928)   <-- TOPMOST
  pid=9380  explorer                  class='Shell_TrayWnd'  rect=(0,981)-(1646,1029)
```

12 samples over 3 s: the product held the **foreground** in all 12, and its main window owned
**every one of the five contended lattice points in all 12 samples**. `Shell_TrayWnd` is the only
other foreign topmost window and it sits below the band (y >= 981).

### And the baseline floor run under exactly that contention was GREEN

```
node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-warnings.mjs
  -> WARNING GATE OK (SP-114): 0 warnings, 0 errors across 4 project(s)

node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
  -> FLOOR OK: CcpClient.Tests 2573/2573, 2 skipped
     [ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps,
      SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked]
     CcpClient.HeadlessTests 152/152, 0 skipped
```

**Before failure set at `1a5136beb`: EMPTY.** (2 skips, both `allowedSkips`-listed OS-property
skips, unchanged.)

**This single measurement kills the obvious detector.** "A foreign topmost window owns a contended
point" is TRUE right now on this machine and the suite is nonetheless green. A pre-flight built on
that predicate would red 230+ tests, on this machine, today, for every lane — the exact
`trap 4` outcome. It is not merely a false-positive risk; it is a measured false positive.

### Why the desktop is contended and green at the same time — and what that tells the detector

Upstream re-asserts `HWND_TOPMOST` only from a **running** chaos session:
`ChaosModeService.KeepChromeTopmost` early-returns unless `_spawning`
(`Services/Chaos/ChaosModeService.cs:786`), and it is called from `RunTick` on a
`_chromeRaiseTick >= 4` throttle (`:938-940`) over a `DispatcherTimer` of
`TimeSpan.FromMilliseconds(250)` (`:503`) — **a ~1 s cadence** — and that is what reaches
`App.Flash?.RaiseAllToFront()` (`:772`, `:801`) and thence `FlashService.RaiseAllToFront`
(`Services/Flash/FlashService.cs:206-243`), whose own doc-comment names the same "~once a second"
period.

So a **parked** topmost window (idle product) sits at a fixed place in the topmost band and our
probes' later `SetWindowPos(HWND_TOPMOST)` goes **above** it — green, correctly. A **re-asserting**
topmost window climbs back over whatever we raised, on a cadence — red, correctly. The harmful
condition is the re-assertion, not the presence.

---

## 1. Detection mechanism: stage the contention, measure the outcome

The pre-flight measures **the capability the whole collection depends on**: *can this process put a
window on top of the contended band and KEEP it there for longer than upstream's re-assert period?*

Inside `RealDesktopLease`'s constructor, **after** the machine-wide lease is taken (so no peer
`CcpClient.Tests` process and no `capture.ps1` is on the desktop — `verification-harness.md:39`):

1. Create **three sentinel windows**, `WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`
   (deliberately **not** `WS_EX_TRANSPARENT`: the sentinel must own its point), on the shape of
   `OverlayWindowProbe.cs:341-388`, at the three x-offsets the suite's own rigs occupy:
   - `cx - 260`  — the overlay negative control's rig centre (`OverlayWindowProbe.cs:286-289`:
     `((W-220)/2) - 260`, +110 = `cx-260`)
   - `cx`        — where the observations place their surfaces
   - `cx + 300`  — the input negative control's rig centre (`InputWindowProbe.cs:387-393`:
     `((W-240)/2) + 300`, +120 = `cx+300`)
2. Raise each with `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE | SWP_SHOWWINDOW)`.
3. **Sample for an observation span of 2500 ms**, pumping messages between samples. Each sample
   asks `WindowFromPoint` at each of the three points and `GetForegroundWindow`, and records
   owner hwnd -> root (`GetAncestor(GA_ROOT)`) -> pid.
4. Tear the sentinels down.

The span is chosen against two measured bounds, both cited, not guessed:
- **lower**: it must exceed upstream's ~1 s re-assert period (`ChaosModeService.cs:503` + `:938`),
  so at least two beats fall inside it;
- **upper**: it stays under the 5 s at which Windows ghosts a window whose thread has not pumped
  (mitigated anyway by the pump in step 3).

### The refusal predicate — exactly one

**`DESKTOP-CONTENDED`: at any sample, a point in the contended band was owned by a window this
process does not own.** That is the harm itself, not a proxy for it.

Second, distinct refusal, so a broken detector can never read as a clean desktop:
**`PREFLIGHT-BLIND`: the sentinels never owned any point at any sample, or zero samples were
taken on a Windows host.** A detector that observed nothing must never report clean.

Everything else is **evidence carried by the refusal, never a refusal on its own** — in
particular the foreground owner's process/pid/title (this is what wave-66 used:
`GetForegroundWindow` + `GetWindowThreadProcessId`), the sample index and elapsed ms at first
loss, the loss count out of samples taken, and the offending window's class and rect.

### Why this predicate and not "a foreign topmost window exists"

Because I measured the latter firing on a green tree today (§0). This one does not fire on a parked
window and does fire on a re-asserting one — and I will demonstrate both (§5).

## 2. Telling our own capture from a foreign product

Three named classes, decided by pid first and process name second:

| class | rule | what the refusal says |
|---|---|---|
| **ours** | `pid == Environment.ProcessId` | never a contention; this is the sentinel or a probe |
| **our harness/product outside the lease** | process name in `CcpClient.Tests`, `testhost`, `CcpClient.Desktop`, `CcpVerify` | still refuses — it contends identically — but says it is OURS and that the lease contract at `verification-harness.md:39` (capture.ps1 takes `%TEMP%/ccp-real-desktop.lease` before the app launches and until after it exits) was violated, so the reader hunts a harness bug, not a foreign app |
| **foreign** | anything else | refuses, naming process, pid, title, class, rect; and when the process is `ConditioningControlPanel`, adds the standing citation `RealDesktopCollection.cs:44-48` / `FlashService.cs:206-243` so the reader lands on the known cause in one line instead of nine runs |

The first line is why the sentinel is `WS_EX_NOACTIVATE`: it must never take the foreground, so a
foreground change during the span is never something we did.

## 3. What one sample can and cannot establish — stated precisely

- **One sample establishes**: that at that instant our topmost window did or did not own the point.
  It cannot distinguish "we hold the top" from "we hold the top until the next beat", because
  upstream re-asserts on a ~1 s cadence and a single sample lands between beats with probability
  ~1 - (beat/period). A single-sample detector reports a clean desktop and then watches
  `VideoOverlayCoexistenceTests` fail anyway — the packet's central trap, and I am not shipping it.
- **The 2500 ms span establishes**: that no re-asserter with a period <= 2.5 s took the band back
  while we held it. Two full beats of upstream's ~1 s cadence fall inside the span.
- **The span does NOT establish**: (a) anything about a contender whose period is longer than
  2.5 s, or that begins after the fixture is constructed — this is a **pre**-flight, and a product
  launched mid-run is outside it; (b) anything about points outside the three probed x-offsets at
  `cy` — a window covering only, say, `cy - 200` is invisible to it; (c) anything about a
  **foreground-only** thief that never raises a topmost window — SP-110's `AttachThreadInput`
  ladder (`Win32InputPresence.cs:525-579`) beats a passive foreground holder, which is why the
  foreground is reported and not refused on, and it is the one blind spot I expect to be argued
  with;  (d) anything at all on a non-Windows host, where the pre-flight is `NotObserved` and says
  so rather than claiming clean.
- **It is falsifiable, and the record will say so**: if wave-66's condition recurs and the
  pre-flight reports clean while those three tests fail, the detector is wrong.

## 4. It fails; it does not skip

The refusal is thrown from `RealDesktopLease`'s constructor, which is the **existing** mechanism
for exactly this class of refusal (`RealDesktopCollection.cs:35-42`: "when the lease cannot be
taken the collection FAILS, loudly"). No skip, no `allowedSkips` entry, no retry, no quarantine, no
environment-variable bypass — an opt-out would be a quarantine wearing a different hat.

**The honest cost, stated up front:** a collection-fixture constructor failure is attributed by
xunit to every test in the collection. `RealDesktopCollection` currently holds **~232 `[Fact]`/
`[Theory]` declarations** across 16 classes (Glyph 40+13+9, Pointer 28+6, Video 29+9+5, Input 23+5,
Overlay 16, Tray 16, Flash 15, Rack 11, Bubble 6, Lease 1). So a contended run reports **one cause
and ~232 attributed reds**, not one red. I will measure the exact number during the red
demonstration and report it. The alternative — record the verdict and assert it in a single fact —
gives literally one red but lets ~232 OS-level facts run against a desktop that cannot certify
them, and the packet's completion criterion says "fails once, **at the fixture**". I am taking the
fixture, and reporting the fan-out rather than hiding it. **If the orchestrator prefers the
one-red shape, this is the decision to overturn, and it is a five-line change.**

## 5. Which edit each new guard must red on (all watched at the committed head, SHA recorded)

| # | guard | the edit / condition it must red on |
|---|---|---|
| R1 | the whole path, end to end, on a REAL foreign window | a separate PowerShell process holding a form at `(cx, cy)` that re-asserts `HWND_TOPMOST` on a 250 ms timer — a direct mimic of `ChaosModeService.cs:938` -> `FlashService.cs:212-243`. Run the collection under it: the fixture must refuse naming `powershell`, its pid and its title |
| R1b | the same guard's **negative** control | the same PowerShell form raised topmost ONCE and left parked. The fixture must **not** refuse — this is the measured §0 state, and a detector that reds here is the false positive I rejected |
| R2 | `AForeignOwnerAtASingleSampleOutOfMany_StillRefuses` | change the verdict from any-sample to majority-of-samples: the cadence fact goes green when it must be red |
| R3 | `AZeroSampleObservationOnAWindowsHost_RefusesAsBlind` | make the blind verdict return clean |
| R4 | `OurOwnSentinel_IsNeverAContention` | classify our own pid as foreign: refuses on our own sentinel |
| R5 | `TheShippingProduct_CarriesItsStandingCitation` | drop the `FlashService.cs:206-243` citation from the message |
| R6 | `ThePreflightReallyObservedTheDesktop` (in-collection) | force the span to 0 ms: the run reports clean having sampled nothing |

## 6. Files, tests, floor

**Changing:** `client/tests/CcpClient.Tests/RealDesktopCollection.cs` (the pre-flight and the
sentinel, self-contained interop — the fixture must not depend on the probes it gates),
`client/tests/CcpClient.Tests/DesktopPreflightTests.cs` (new), `client/docs/verification-harness.md`
(the "NOT covered" residue at `:220-226` now has a pre-flight in front of it),
`client/docs/wpf-surface-reachability.md` (D279-D288 only), `spine-tasks/SP-134-desktop-preflight/**`.

**Not touching:** the three named test classes, any OS-level assertion, `floor.json`, `client/src`,
`client/tools`, the board, the census.

`RealDesktopCollection.cs` is already in `RealDesktopCollectionGuardTests.ExemptFileNames`
(`:52-56`), so a sentinel that calls `CreateWindowExW(` there does not trip the membership or
census walks. `DesktopPreflightTests.cs` creates no window and names no listed helper, so it is
invisible to both walks by construction; its in-collection class carries
`[Collection(nameof(RealDesktopCollection))]` and its pure-verdict class deliberately does not —
the same two-classes-in-one-file shape the guard already names as a blind spot for
`RealDesktopLeaseTests.cs` (`:38-41`), and I will say so rather than let it be inferred.

**Floor:** pin 2573 unit / 152 headless. Expected delta **+8 unit / 0 headless** -> an observed
total of **2581 / 152**. Declared in `spine-tasks/SP-134-desktop-preflight/floor-delta.json`.
`floor.json` is not opened.

## 7. Open risks I am carrying into the code step

1. **The fan-out number** (§4). Measured, not assumed, before the final report.
2. **Whether `WindowFromPoint` returns a `WS_EX_LAYERED` window with a low `LWA_ALPHA`.** I believe
   it does (only `WS_EX_TRANSPARENT` and `UpdateLayeredWindow`'s zero-alpha regions are skipped),
   but I will verify empirically and fall back to an opaque sentinel rather than assume an API.
3. **Whether a collection-fixture throw keeps the reported test TOTAL at 2581.** If xunit drops the
   tests instead of failing them, `check-floor.mjs` would red on pin arithmetic and the desktop
   message would be one level down. Measured during R1, reported either way.
4. **The desktop may be contended for the rest of this packet.** pid 16712 is the user's own
   running application and I will not kill it. Every run from here is recorded with the state of
   that process at the time.
