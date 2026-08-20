# SP-122 — record: somebody looked at the rack

Branch `lane/SP-122-rack-presentation`, base `145632a1`, worktree
`C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-a5519337b56438890`.

## 0. Outcome in one line

**Two Studio rack surfaces are `presentation-verified` across four states, by five named checks,
each shown failing twice — once on a real capture of the opposite state, once on a real seeded
regression in `MainWindow.axaml`. No product code changed. No human has compared any of it to the
WPF build, and that gate stays open.**

## 1. THE PROBE WAS REFUSED, AND THAT IS THE PACKET'S CENTRAL FINDING

The packet said (`PROMPT.md:26-36`): *"A Studio surface has no probe, so there is nothing to aim a
capture at"*, and opened `client/src/CcpClient.Desktop/Views/**` for a rack probe. **The running
app contradicts it.** Every rack row is a `RadioButton`, and Avalonia gives every `RadioButton` a
real automation peer. Measured on a live shell at scale 1.75:

```
RadioButton peers: 20  (5 doors + 15 rack rows)
name='Flash Images rack row'  aid='RowFlashImages'  rect=501;441;402;63  sel=False  toggle=Off
   patterns=[SelectionItemPattern, TogglePattern, ScrollItemPattern]
Pane aid='RackScroll'  rect=485;175;434;965
TEXT aid='' rect=519;456;336;33 name='Flash Images'
TEXT aid='' rect=519;856;350;33 name='Visuals'
```

| What a rack probe would have published | Already published by | Measured |
|---|---|---|
| row screen rect | `AutomationElement.Current.BoundingRectangle` | `501;441;402;63` |
| row identity | `Current.AutomationId` (Avalonia uses `x:Name`) | `RowFlashImages` |
| which row is open | `SelectionItemPattern.IsSelected` | `False` -> `True` after a click |
| the scroll clip | `Pane aid='RackScroll'` rect | `485;175;434;965` |
| whether a module is armed | `FlashLiveState` text, panel open | `"Switched off. …"` / `"Armed. …"` (`StudioPage.axaml.cs:1654-1656`) |

The door probe (`MainWindow.axaml.cs:275-281`) survives from SP-007, whose anchor was a
demonstrator **card** — a `Border`/`Grid`, which genuinely has no peer, and which is what
`capture.ps1:5` is talking about. SP-091 re-anchored the harness onto `RadioButton`s and kept the
probe without revisiting it; `capture.ps1:136-143` has enumerated `RadioButton` peers since the
SP-094 audit. Both channels were read in one run and agree exactly: probe
`door studio 174.9x44.0 DIP @ scale 1.75 @ screen 121,223`, UIA `121;223;306;77`, and
174.9 x 1.75 = 306.1, 44 x 1.75 = 77.

**And the probe would have moved what it observes.** `LayoutProbeText` is a live, laid-out
`TextBlock` in the bottom-docked footer: 5 door lines occupy 117 px, so 23.4 px per line. Fifteen
rack lines add **351 px**, taken out of the page host, against a rack viewport measured at
**965 px** — 36.4%. At the measured row pitch of (36 + 2) x 1.75 = 66.5 px that is **5.2 rows
pushed below the scroll fold**. A probe that displaces five of the fifteen rows it exists to
photograph is not an observation seam. The coordinator withdrew the authorisation after
re-deriving these numbers; `client/src/CcpClient.Desktop/**` is untouched by this lane.

**Honest consequence, not hidden:** the probe is redundant only on Windows. WSLg has no UIA and
`capture-wslg.sh` reads the logged line, so a WSLg rack capture would need a logged rack line, and
this packet did not add one. Recorded in `client/docs/verification-harness.md`.

## 2. What was captured, how it was driven, and how the drive was confirmed

`capture.ps1` gained two surfaces. Every state is driven by REAL mouse input and confirmed by a
UIA read BEFORE any pixel is taken.

| Surface | State | Drive | Confirmation |
|---|---|---|---|
| `rack-row` | `unselected` | cold start, nothing clicked | `IsSelected == False` |
| `rack-row` | `selected` | left-click the Flash Images row | `IsSelected == True` |
| `rack-row-dot` | `armed` | left-click to open the panel | `FlashLiveState` = `"Armed. Nothing is scheduled until the session starts."` |
| `rack-row-dot` | `off` | left-click, then **right-click the row** | `FlashLiveState` = `"Switched off. Nothing will happen, session or no session."` |

Three things about that table were found by measurement and would have been wrong if assumed.

1. **The drive order is left-click THEN right-click.** `FlashLiveState` lives inside
   `FlashModulePanel`, whose `IsVisible` is gated on the row being checked
   (`StudioPage.axaml.cs:540`), and the right-click sets `Handled` and deliberately does NOT
   select (`:556-565`). Without the left-click first the confirmation read is unreachable.
2. **`armed` is the COLD state and `off` is the one that costs a gesture** — the opposite of the
   plan's first draft. `SessionPresetDocument.FlashEnabled` defaults to `true` (`:64`, ported from
   WPF's `AppSettings.FlashEnabled`), so a fresh start with the settings file deleted is already
   armed. The first `-State off` run failed loudly saying so, which is what a confirmed drive is
   for.
3. **The right-click quick-toggle now has real-desktop evidence.** The rack advertises it in its
   own hint text ("Right-click a row to flip that effect on or off") and no run on a real desktop
   had ever performed it. `capture.ps1` performs it, and the module reports the flip.

Arming puts nothing on the screen — "Armed. Nothing is scheduled until the session starts." — so
no capture is polluted by an effect.

**A FOURTH DEFECT, AND IT WAS MY OWN CLAIM THAT WAS WRONG.** The paragraph above originally said
the captures were order-independent because `capture.ps1` deletes
`%APPDATA%\CcpClient\settings.json`. A confirmation run on the committed harness refused:

```
state drive: left-click on the Flash Images rack row -> IsSelected=True
FAIL: the module did not reach 'armed': FlashLiveState reads 'Switched off. Nothing will happen,
session or no session.' (expected it to start 'Armed.')
```

The rack rows' module dials are NOT in `settings.json`. They are in `session_preset.json` in the
same data directory (`SessionPresetDocument.FileName:32`, `SessionParticipant.cs:96`), which
`capture.ps1` never deleted — an incompleteness that has been there since SP-098 introduced the
file, and which nothing before this packet exercised because nothing before it toggled a module.
The previous run's `off` capture had leaked into this one. Two fixes, both landed:

1. The deterministic-start set is now the two files, not one.
2. **The dot state is DRIVEN, not assumed.** `capture.ps1` reads `FlashLiveState`, right-clicks
   only if it disagrees, and reads again — so a leaked store can never silently produce the wrong
   capture. The first version hard-coded "off needs the gesture, armed does not" and that is
   exactly the assumption the leak broke.

Proven order-independent afterwards, `off` then `armed` back to back on the real desktop:

```
[off]    state drive: right-click quick-toggle on the Flash Images row (it read 'Armed. …')
         state drive confirmed: FlashLiveState = 'Switched off. …'          CAPTURE PASS
[armed]  state drive confirmed: FlashLiveState = 'Armed. …'  (no toggle needed)  CAPTURE PASS
```

The confirmed drive is what caught this. An unconfirmed capture would have photographed a hollow
dot, filed it as `armed`, and the check would have failed later looking like a product regression.

**And a near-miss worth recording, because the harness already warns about it in its own words.**
While mutation-testing the new guard for this fix, I restored `capture.ps1` with
`git checkout -- ` while the fix itself was still UNCOMMITTED — and silently lost it.
`self-test.ps1:17-27` documents that exact failure as the SP-094 near-miss and restores from bytes
held in memory precisely so it cannot happen; I did it to myself with a bare git command outside
that script. Caught immediately (the redone script was re-run and re-proved end to end above), but
it is the second time this repository has paid for the same reflex, and the mutation-proof loop for
any uncommitted file should copy bytes rather than reach for git.

### Geometry, and two traps in it

**The dot cell is derived, not hard-coded.** A rack row's grid is `ColumnDefinitions="*,Auto"`:
the caption fills the star column, the 8-DIP dot is the auto column. So the cell starts at the
caption's right edge and is `round(8 x scale)` wide. The cross-check comes from the **Visuals**
row, the only row whose grid has one child because upstream gives it no dot
(`StudioPage.axaml:172-174`; upstream's rule at `StudioTabView.xaml.cs:494-496`) — its caption
spans the whole grid, so caption + dot on any other row must equal it. Live:
`caption 336 px + dot 14 px == Visuals dotless caption 350 px (8 DIP @ scale 1.75)`.

The first draft compared the two rows' right EDGES and was wrong by exactly 5 px on every selected
row: `rack-row:checked` carries `BorderThickness="3,0,0,0"`, so 3 DIP x 1.75 displaces the checked
row's content and the two rows stop sharing an origin. It failed loudly
(`the dot cell measures 9 px … but 8 DIP at scale 1.75 is 14 px`) rather than capturing the wrong
14 pixels. Widths are invariant under that displacement; edges are not.

**UIA lies about visibility, and it took a guard.** It reports UNCLIPPED bounds and
`IsOffscreen=False` for rows scrolled out of the rack: `RowIntensityRamp rect=501;1505;402;63`
inside a window ending at y=1470 and a viewport ending at y=1140. Aiming a capture there
photographs the wallpaper and the check then reports on somebody's desktop background.
`capture.ps1` refuses unless the target rect lies fully inside BOTH the `RackScroll` viewport and
the window.

## 3. THE CHECKS, AND THE BITE PROOF FOR EACH

Five checks in `client/tools/verify/checks.json`, all `presentation-verified`. Every fraction below
is from a real capture of the running app.

| Check | Region / expected | PASS state | WRONG state | Threshold |
|---|---|---|---|---|
| `rack-row-unselected-ground` | strip right of the caption, `#19141F` tol 6 | **1.000** (unselected) | **0.000** (selected) | 0.98 |
| `rack-row-selected-marker` | left band 3 px, `#E066FF` tol 8 | **0.862** (selected) | **0.000** (unselected) | 0.50 |
| `rack-row-selected-fill` | same strip, `#2A2130` tol 4 | **1.000** (selected) | **0.000** (unselected) | 0.98 |
| `rack-row-dot-armed` | whole 14x14 dot cell, `#6B5B73` tol 8 | **0.714** (armed) | **0.224** (off) | 0.50 |
| `rack-row-dot-off` | dot cell centre 6x6, `#2A2130` tol 4 | **1.000** (off) | **0.000** (armed) | 0.90 |

Each wrong-state run is a real capture of the real app in the other state, and `CcpVerify` rejects
it by name with exit 2:

```
--surface rack-row --state selected   <- windows-rack-row-unselected.png
FAIL rack-row-selected-marker - 0/189 pixels matched (fraction 0,000)
FAIL rack-row-selected-fill - 0/7605 pixels matched (fraction 0,000)
FIRST FAILED CHECK: rack-row-selected-marker      EXIT=2

--surface rack-row --state unselected <- windows-rack-row-selected.png
FAIL rack-row-unselected-ground - 0/7605 pixels matched (fraction 0,000)
FIRST FAILED CHECK: rack-row-unselected-ground    EXIT=2

--surface rack-row-dot --state armed  <- windows-rack-row-dot-off.png
FAIL rack-row-dot-armed - 44/196 pixels matched (fraction 0,224)
FIRST FAILED CHECK: rack-row-dot-armed            EXIT=2

--surface rack-row-dot --state off    <- windows-rack-row-dot-armed.png
FAIL rack-row-dot-off - 0/36 pixels matched (fraction 0,000)
FIRST FAILED CHECK: rack-row-dot-off              EXIT=2
```

### The stronger bite: a REAL regression in product markup

A wrong-state capture proves a check can tell two states apart. It does not prove the check
catches a code change. `self-test.ps1` gained a rack phase, and it needed no new seed: `#FFE066FF`
is not door-specific — it is also `rack-row:checked`'s `BorderBrush` and `Ellipse.dot.live`'s
`Fill` (`MainWindow.axaml:67-70, :101-105, :390-393`). One throwaway edit of the real
`MainWindow.axaml`, restored from bytes held in memory and verified byte-for-byte:

```
--- phase 1: seed the regression (selected-door border brush -> #FF336633) ---
FAIL rail-door-selected-border - 0/918 pixels matched (fraction 0,000)
FIRST FAILED CHECK: rail-door-selected-border
seeded regression caught by the SPECIFIC named check (exit 2)
FAIL rack-row-selected-marker - 0/189 pixels matched (fraction 0,000)
PASS rack-row-selected-fill - 7605/7605 pixels matched (fraction 1,000)
FIRST FAILED CHECK: rack-row-selected-marker
seeded regression ALSO caught at the rack, by its own named check (exit 2)
--- phase 2: restore ---
PASS rail-door-selected-border - 888/918 (0,967)     ALL CHECKS PASSED
PASS rack-row-selected-marker - 163/189 (0,862)
PASS rack-row-selected-fill - 7605/7605 (1,000)      ALL CHECKS PASSED
restored build green at both the rail door and the rack
SELF-TEST PASS
```

The seed moved one brush and **exactly one check noticed**: the fill check stayed green through
the regression. That is the discrimination a named check is for.

### Tolerances: the rule in its natural habitat

The rail-door precedent is 24/32. **At 24 these checks would not bite.** Measured on the real
unselected capture, evaluating the selected fill colour `#2A2130` against it:

```
tol 16:      0/7605 = 0,0000
tol 20:   7605/7605 = 1,0000
```

`RadioButton.rack-row:pointerover` is `#FF241E2A` (`MainWindow.axaml:98-100`), 11/10/11 from the
rack's `#FF19141F` ground; the checked fill is 17/13/17 from it. At 24 the ground check would pass
with the mouse merely RESTING on the row, and the fill check would pass on a closed row. The right
precedent is `dashboard-background`'s 8. The two `#2A2130` checks go tighter still, to 4, because
their nearest neighbour is the hovered row at only 6/3/6 — and it costs nothing: those regions are
flat fills that measure **7605/7605** and **36/36 EXACT**, with no antialiasing in them at all.
`RackPresentationTests.NoRackCheckAcceptsTheColourOfAnotherRackState` now asserts that separation
mechanically, so widening any of them reddens the floor and names both colours.

Two of the five thresholds are not near-1.0 and are stated plainly. `rack-row-selected-marker` at
0.862 loses 20/252 pixels to the row's `CornerRadius="4"`. `rack-row-dot-armed` at 0.714 is a
circle inscribed in a square cell — pi x 7^2 / 196 = 0.785 before antialiasing. Both thresholds sit
at 0.50, roughly midway between the measured pass and the measured wrong state (0.862 vs 0.000;
0.714 vs 0.224).

## 4. THE REAL-DESKTOP LEASE — TAKEN, AND PROVEN TWICE

`capture.ps1` ran OUTSIDE `%TEMP%/ccp-real-desktop.lease` until this packet, while doing exactly
what `RealDesktopCollection`'s fixtures do. It now takes it, byte-compatible with
`RealDesktopLease.TryTake` (`RealDesktopCollection.cs:110-118`): `FileMode.Create` /
`FileAccess.Write` / `FileShare.Read`, with `pid=<n>` written RAW into the stream — no
`StreamWriter`, no BOM, no trailing newline, because `HolderProcessId` requires the file to start
literally `pid=` (`:148`). The holder is read back with `FileShare.ReadWrite` as `:144` does; a
reader granting only `Read` would itself be refused while the writer holds the file. Taken BEFORE
the app launches and released after it exits, and released on every failure path.

**Every one of the captures and self-test runs in this record was taken with the lease held.**

**A) Exclusion, against the unmodified script.** A foreign process took the lease and held it 25 s:

```
foreign holder process id = 59252; lease file says: pid=59252
  [capture] real-desktop lease held by pid=51512 (waited 22.1s)
  [capture] real-desktop lease released
  [capture] CAPTURE PASS
capture.ps1 total wall time 31.3s
```

The capture really waited; it did not capture around a contended desktop. The picture it then took
verified identically to one taken uncontended (`0.862` / `1.000` both times).

**B) Refusal, naming the holder.** The same setup with the acquisition deadline seeded from 300 s
down to 3 s (throwaway edit, restored byte-for-byte and verified — the `self-test.ps1` pattern):

```
foreign holder process id = 55952
  [capture] FAIL: could not take the real-desktop lease within 3s. This process is 59584; the
  lease file names process 55952 as the holder. Refusal: The process cannot access the file
  'C:\Users\Micha\AppData\Local\Temp\ccp-real-desktop.lease' because it is being used by another
  process.. A contended desktop is not a flake and must NOT be captured around: the desktop is a
  singleton and this capture would photograph another run's windows.
capture.ps1 restored byte-for-byte
```

That is the whole point of `FileShare.Read` and the raw bytes: it names process 55952 instead of
asserting that somebody must have it. Only the deadline was seeded; the acquisition, the refusal
text and the holder read are the shipped code.

## 5. The fenced read

`CopyFromScreen` was called with no happens-before edge against the compositor. `DwmFlush()` now
precedes it — the same fence as `FlashPixelProbe.CaptureDesktop` (`FlashPixelProbe.cs:178, :235`),
where SP-116 measured 34 misses in 1200 unfenced reads and 0 in 1500 fenced. A DWM that refuses is
reported and FAILS the capture rather than being swallowed: an unfenced read is a coin flip, and a
PNG that might be of the wallpaper is not evidence. Every capture in this record printed
`screen read fenced through DwmFlush (HRESULT 0)`.

## 6. A third defect, found by the work (a fourth is in §2)

`capture.ps1` never called `exit 0`. A `.ps1` invoked with `&` that never calls `exit` leaves
`$LASTEXITCODE` holding the PREVIOUS command's code, and `self-test.ps1` guards every capture with
`if ($LASTEXITCODE -ne 0)`. Those guards were reading whatever ran before — vacuously green when
the predecessor was a build, and a **false failure** the moment the predecessor was `CcpVerify`
reporting a seeded regression with exit 2. That is exactly how it surfaced: the rack phase failed
with `rack-row capture failed with seeded regression` on a capture that had in fact passed. Fixed
at the source with `exit 0`, which makes the pre-existing guards mean something for the first time.

## 7. Tests added, and each one shown to bite

`client/tests/CcpClient.Tests/RackPresentationTests.cs` — **19 facts**. Manifest shape (every rack
check is `presentation-verified`; both surfaces covered in both states); the surface set derived
from `capture.ps1`'s own `ValidateSet` rather than restated (the SP-094 rule); the tolerance
separation property; ten synthetic-buffer facts that each check accepts its own state's colour and
rejects the other's; two lexical guards on the capture path, and one that the deterministic-start set covers the preset store the rack really writes to, keyed on the PRODUCT constant `SessionPresetDocument.FileName` so renaming the store reddens it.

`client/tests/CcpClient.HeadlessTests/RackPresentationAnchorHeadlessTests.cs` — **3 facts** tying
the manifest's colours to the product's resolved brushes, so a brush change reddens the floor
naming the manifest entry to update instead of going silent until someone next runs a headed
capture.

Deliberate mutations, each reverted from git afterwards and the tree confirmed clean:

| Mutation | Result |
|---|---|
| `rack-row-unselected-ground` tolerance 6 -> 24 (the rail-door precedent) | `check 'rack-row-unselected-ground' expects #19141F with tolerance 24, which also ACCEPTS the checked row fill … only 17 apart` |
| manifest dot colour `#6B5B73` -> `#6B5B74` | `Ellipse.dot.armed Fill resolves to #6B5B73, but checks.json's 'rack-row-dot-armed' … expecting #6B5B74` |
| `'rack-row-dot'` renamed in the `ValidateSet` | `checks.json names surface 'rack-row-dot' but capture.ps1 accepts only [dashboard, rack-row, rack-row-spot, rail-door] … the check is decoration` |
| `DwmFlush()` call removed | `Assert.InRange() Failure: Value not in range` |
| lease `FileShare.Read` -> `FileShare.None` | `Assert.Contains() Failure: Sub-string not found` |
| `session_preset.json` dropped from the deterministic-start set | `Assert.Contains() Failure: Sub-string not found` |
| **the `Take-Lease` CALL LINE deleted, definition left intact** | `capture.ps1 defines Take-Lease but never CALLS it at top level — the script would run unleased` |
| `Release-Lease` removed from the `Fail` path | `capture.ps1 calls Release-Lease 1 time(s); it must release on the failure path as well as the success path` |
| **`rack-row-dot-armed` `minPixelFraction` 0.5 -> 0.2** | `has minPixelFraction 0,2, which is not strictly between the fractions its own real captures produced: 0,224 on the wrong state, 0,714 on its own` (18 of 19 rack facts stayed green — which is review's whole point) |
| `'armed'` removed from `capture.ps1`'s `$statesFor` map | `Assert.Contains() Failure: Item not found in set` |

**The capture-path guards are LEXICAL and prove deletion, not disabling.** A commented-out lease
would keep them green. §4's transcripts are what prove the mechanism, and the test file says so in
its own summary rather than leaving a reader to assume otherwise.

## 8. Floor

Pin **2270 unit / 141 headless**. Declared delta: **+19 unit / +3 headless**
(`spine-tasks/SP-122-rack-presentation/floor-delta.json`). Expected observed totals: **2289 unit /
144 headless**. Observed and both gates' results are in the final report. `floor.json` was never
opened. Both gates were run alone and never beside a capture.

## 9. WHAT THIS DOES NOT PROVE

- **No human has compared the port's rack to the WPF build.** `presentation-verified` means
  composited pixels were read back and matched named colours in named regions. Whether the rack
  LOOKS like upstream's rack is a MANUAL gate and it is OPEN. Nothing here closes it, and the
  packet's own rule is to name it rather than discharge it.
- One row of fifteen (Flash Images), one machine, one scale (1.75), one theme. Colour and position
  only: not typography, not scrolling, not the other fourteen rows, not Linux/WSLg.
- The `:pointerover` livery has never been captured — the mouse is parked in the diagnostic footer
  before every read. What keeps it from being mistaken for the ground is the tolerance sizing, not
  a capture.
- `CopyFromScreen` reads the composited desktop, so unlike SP-115's `PrintWindow` it is not
  structurally blind to transparent-versus-black. It IS blind to a foreign top-most window owning
  the point, and the lease cannot exclude one (`RealDesktopCollection.cs:44-48`). Unchanged residue.
- **`rack-row-dot-off` cannot see a missing dot.** It reads the open row's fill through a hollow
  ring, so an unrendered dot passes it. Only `rack-row-dot-armed` constrains that the dot exists,
  and only by area above its threshold.
- The captures are gitignored artifacts. What survives in the repository is the manifest, the
  capture path and the guards; the pixels themselves are reproducible, not archived.

## 9a. WHAT CODE REVIEW CAUGHT: two guards that passed over the thing they existed to catch

Review returned REVISE with two blockers, both demonstrated empirically rather than argued, and
both of the same class. Both are now fixed and both fixes are mutation-proved above.

**BLOCKER 1 — the lease guard was INERT, over this packet's #1 correctness requirement.** It read
`script.IndexOf("Take-Lease", script.IndexOf("function Take-Lease") + 1)`. Searching from F+1 finds
the substring at F+9 — still inside the DEFINITION header `function Take-Lease {` — and never
reaches the call site. Review resolved the offset (3305, context `"function Take-Lease {\r\n    $d"`),
deleted the call line at `capture.ps1:291`, and re-evaluated all six assertions: **every one stayed
true.** So `capture.ps1` could have run entirely unleased with the guard green. My own disclosure
("a commented-out lease would keep them green") understated it — the guard did not prove the call
existed at all. And §7's mutation row for it only ever exercised
`FileShare.Read -> FileShare.None`, which trips a different `Assert.Contains`, so the deletion case
was never mutation-tested. The guard now matches a line-exact `^Take-Lease\s*$` call statement,
requires it to precede the launch, and additionally requires at least TWO `Release-Lease` call
sites so the failure path cannot quietly stop releasing. Both cases are now in the mutation table.

**BLOCKER 2 — nothing pinned `minPixelFraction`, which is the entire discriminating power of the
narrowest check.** `Ellipse.dot` strokes `#FF6B5B73` and `Ellipse.dot.armed` fills AND strokes the
same colour (`MainWindow.axaml:386-389`), so `rack-row-dot-armed` separates a filled disc from a
1-DIP ring by AREA alone: 0.714 against 0.224 at a 0.5 threshold.
`NoRackCheckAcceptsTheColourOfAnotherRackState` constrains `Tolerance` only, and nothing in the diff
read `MinPixelFraction`. Review dropped it to 0.2 — the check then PASSES on the real `off` capture
(0.224 >= 0.2) while both `Theory` cases stay green, because the synthetic buffers are solid
`#2A2130` and score 0.000 either way. A green floor would have been equally consistent with a check
that no longer bites. `EveryRackThresholdSitsBetweenTheFractionsItsOwnCapturesProduced` now pins
every threshold strictly between the two fractions its own real captures scored, with an extra 0.1
clearance required on the WRONG side only — the asymmetry is deliberate, because a threshold
creeping toward the wrong-state fraction is a check going blind, whereas a threshold just under a
pass fraction of exactly 1.000 is what a flat unantialiased fill legitimately earns.

**The four non-blocking items, all applied.**

1. `self-test.ps1` cited `MainWindow.axaml:67-70` for `RadioButton.door:checked`; the selector is
   at 68 and closes at 71 (67 is the last comment line). Corrected.
2. **`rack-row-dot-off` also passes if the dot is not rendered at all** — it reads the open row's
   fill showing through a transparent centre. Only `rack-row-dot-armed` constrains that the dot
   exists, and only above its threshold, which is why blocker 2 mattered more than it looked. Now
   stated in `checks.json` beside the checks and in §9 below.
3. `capture.ps1`'s `$statesFor` surface/state map had no floor guard —
   `EveryManifestSurfaceIsOneTheCaptureScriptAccepts` covered `-Surface` only. Added
   `EveryManifestSurfaceStatePairIsOneTheCaptureScriptCanDrive`, mutation-proved.
4. The doc comment on `EachRackCheckRejectsTheOtherStatesColour` claimed it reproduced the real
   fractions "0.000, 0.000, 0.000, 0.224, 0.000"; the synthetic dot-armed case yields 0.000, not
   0.224, because a solid buffer is not a hollow ring. Reworded to say exactly what a solid buffer
   can and cannot stand in for, and to point at the fact that does pin the 0.224.

**One more thing I am recording rather than fixing:** the `//` comments make `checks.json` JSONC.
That is safe today — `CheckManifest.Load` sets `JsonCommentHandling.Skip` (`CheckManifest.cs:50`)
and nothing else parses the file, including the headless anchor test, which passes the same option.
But **a strict JSON parser can no longer read the manifest**, and anything added later that reads it
must opt into comments.

Review confirmed the rest independently: no product byte changed, the probe-refusal arithmetic
(351 px against a 965 px viewport, 5.2 rows below the fold), all four harness defects genuinely
present at base and genuinely fixed, the lease byte contract exact against
`RealDesktopCollection.cs:113,144,148`, the tolerances correctly below 10, and 0.714 vs 0.224 as a
real filled disc against an antialiased ring (pi/4 = 0.785 in a 14x14 cell) rather than noise. It
also checked that the seeded-regression discrimination is genuine rather than `selected-fill` being
insensitive: that check reads a Background the seed never touched, and it does fail 1.000 -> 0.000
on the unselected capture.

## 10. Divergences (D207 onward)

**None.** No product behaviour changed and no upstream decision was reinterpreted. The one thing
that could have been a divergence — a rack probe in `Views/**` — was refused, and the refusal is
recorded in §1 and in `client/docs/verification-harness.md` rather than as a divergence, because
nothing about the product diverged: it stayed exactly as it was.

## 11. Corrections applied from the plan review

1. Ground and fill tolerances sized below 10 (6 and 4), with the measurement that proves 24 would
   not bite (§3).
2. Lease bytes written raw and the holder read with `FileShare.ReadWrite` (§4).
3. Drive order stated and implemented as left-click THEN right-click (§2).
4. Settings-file deletion named as what makes the four captures order-independent (§2).
5. Plan citation corrected: the one-child Visuals grid is `StudioPage.axaml:172-174`, not `:157-166`
   (which is the SP-117 comment).
6. Plan prose corrected: the strip right of the caption carries no accent bar, so `#2A2130` alone
   is what bites `rack-row-unselected-ground`.
7. Real refusal transcript pinned beside the lexical guards, holder pid named (§4B, §7).
8. `verification-harness.md:40`'s stale demonstrator-card line replaced with the eight checks that
   actually exist; the note that `self-test.ps1`'s `#FFE066FF` seed is not door-specific is what
   let the rack phase reuse it unchanged (§3).

Also endorsed and applied: no whole-rack-panel check. Review measured the marker at ~0.08% of the
panel, so no threshold separates it from noise and such a check could only ever pass.
