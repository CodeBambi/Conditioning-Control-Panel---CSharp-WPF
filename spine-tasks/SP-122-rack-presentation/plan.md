# SP-122 — plan checkpoint (Review Level 3, step 1)

Branch `lane/SP-122-rack-presentation`, worktree
`C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-a5519337b56438890`,
base `145632a1`. **No repository file has been changed at this point except this plan.**

## 0. Reconnaissance already done (read-only, lease held)

I built the client (`0 Warning(s) 0 Error(s)`) and ran ONE read-only UIA reconnaissance pass
against the live shell on the real desktop, holding `%TEMP%/ccp-real-desktop.lease` (opened
`Create/Write/FileShare.Read`, `pid=` written, released in `finally` — byte-compatible with
`RealDesktopLease.TryTake`, `client/tests/CcpClient.Tests/RealDesktopCollection.cs:110`). The
script lives in the scratchpad, not in the repo. It clicked nothing and read no pixels.

Measured (scale 1.75, window `100,140,1925,1330`):

```
RadioButton peers: 20  (5 doors + 15 rack rows)
name='Flash Images rack row'  aid='RowFlashImages'  rect=501;441;402;63  sel=False toggle=Off
   patterns=[SelectionItemPattern, TogglePattern, ScrollItemPattern]
... all fifteen rows, each with an AutomationId, a screen rect and both patterns ...
Pane aid='RackScroll'  rect=485;175;434;965
TEXT aid='' rect=519;456;336;33  name='Flash Images'      (row label, star column)
TEXT aid='' rect=519;856;350;33  name='Visuals'           (the ONE row with no dot column)
TEXT aid='LayoutProbeText' rect=121;1343;1883;117  (5 door lines -> 23.4 px per line)
```

## 1. THE PACKET'S CENTRAL PREMISE IS WRONG, AND I PLAN TO REFUSE THE PRODUCT CHANGE

PROMPT.md §"THE CENTRAL TRAP" says: *"A Studio surface has no probe, so there is nothing to aim a
capture at"*, and opens `Views/**` for a rack probe. **The running app contradicts that.** Every
rack row is a `RadioButton`, and Avalonia gives every `RadioButton` a real UIA peer carrying:

| What a probe would expose | Already exposed by | Measured value |
|---|---|---|
| row screen rect | `AutomationElement.Current.BoundingRectangle` | `501;441;402;63` |
| row identity | `Current.AutomationId` | `RowFlashImages` |
| which row is open | `SelectionItemPattern.IsSelected` / `TogglePattern.ToggleState` | `False` / `Off` |
| the rack viewport (scroll clip) | `Pane aid='RackScroll'` rect | `485;175;434;965` |
| whether a module is armed | `FlashLiveState` text, once the panel is open | `"Switched off. …"` vs `"Armed. …"` (`StudioPage.axaml.cs:1654-1656`) |

The door probe (`MainWindow.axaml.cs:275-281`) is a survival of SP-007, whose anchor was a
demonstrator **card** — a `Border`/`Grid`, which genuinely has no peer. SP-091 re-anchored the
harness onto rail doors (`RadioButton`s) and kept the probe without revisiting it. Cross-check
from this run: probe says `door studio 174.9x44.0 DIP @ scale 1.75 @ screen 121,223`; UIA says
`121;223;306;77`, and `174.9x1.75 = 306.1`, `44x1.75 = 77`. **The two channels agree exactly, so
the probe is redundant on Windows.** It is NOT redundant on Linux — WSLg has no UIA and
`capture-wslg.sh` reads the logged line — so I will not touch it, and I will record that a
future WSLg rack capture would need a logged rack line.

**And adding the rack probe would change the surface I am here to photograph.** The footer's
`LayoutProbeText` is a live, laid-out `TextBlock`: 5 door lines occupy 117 px, i.e. 23.4 px per
line. Fifteen rack lines add **351 px** to a docked footer, taken straight out of the page host,
against a rack viewport measured at **965 px** — a 36% reduction, and at the measured 67 px row
pitch that is **five rack rows pushed below the scroll fold**. That is the packet's own stop
condition ("if you find yourself altering what the rack DOES to make it capturable, stop and
record it") reached by arithmetic on measured numbers. **Planned decision: no `Views/**` edit at
all.** `client/src/CcpClient.Desktop/**` will be untouched by this lane.

## 2. What I will capture, and why these are rack surfaces

Two surfaces, four captures. Both are the rack's own visual inventions; neither exists in the
shell checks already discharged.

| Surface | State | Drive (all REAL input) | Confirmed before any pixel is read |
|---|---|---|---|
| `rack-row` | `unselected` | fresh launch, nothing clicked | `RowFlashImages` UIA `IsSelected == False` |
| `rack-row` | `selected` | left-click the Flash Images row | `IsSelected == True` |
| `rack-row-dot` | `off` | row selected, module untouched | `FlashLiveState` = `"Switched off. …"` |
| `rack-row-dot` | `armed` | **right-click the row** (the rack's second gesture, `StudioPage.axaml.cs:449-453,559-569`) | `FlashLiveState` = `"Armed. …"` |

`rack-row` captures the row rect from UIA. `rack-row-dot` captures the 8-DIP dot cell, derived
from two UIA rects and cross-checked: `dotLeft = right(FlashImages label)= 855`,
`dotRight = right(Visuals label) = 869` (Visuals is the only row whose Grid has one child —
`StudioPage.axaml:157-166`, upstream `StudioTabView.xaml.cs:494-496`), and the harness asserts
`869-855 == round(8 x scale) = 14` before capturing. A wrong derivation therefore fails loudly
instead of photographing the wrong 14 px.

**Geometry guard (a real hole, found in reconnaissance):** UIA reports `IsOffscreen=False` for
rows whose rects lie **outside** the rack viewport and even outside the window —
`RowIntensityRamp rect=501;1505;...` against a window ending at y=1470 and a viewport ending at
y=1140. Aiming a capture there would photograph the desktop and the check would read whatever
wallpaper is behind. The harness will refuse to capture unless the target rect is fully inside
BOTH the `RackScroll` viewport rect and the window rect.

## 3. The checks, and WHAT EACH ONE WOULD HAVE TO SEE TO FAIL

Five named checks in `client/tools/verify/checks.json`, all `presentation-verified`. Thresholds
below are the intent; the exact `minPixelFraction` will be set from the two measured fractions
(pass state and wrong state) and BOTH numbers will be in `record.md`.

| Check | Reads | Passes on | **FAILS when it sees** |
|---|---|---|---|
| `rack-row-selected-marker` | left band of the row capture, `#E066FF` | the 3-DIP accent bar of `RadioButton.rack-row:checked` (`MainWindow.axaml:101-105`) | the rack ground `#19141F` in that band — i.e. a row that is NOT open. Proof: run it against the real `rack-row/unselected` capture. |
| `rack-row-selected-fill` | interior strip right of the label, `#2A2130` | the checked row's background | `#19141F` (unselected ground; distance 17/13/17, far outside the tolerance). Proof: same wrong capture. |
| `rack-row-unselected-ground` | same strip, `#19141F` | the rack ground under a closed row | `#2A2130` plus a magenta bar — i.e. an OPEN row. Proof: run it against the real `rack-row/selected` capture. |
| `rack-row-dot-armed` | the 14 px dot cell, `#6B5B73` | the FILLED disc of `Ellipse.dot.armed` (`MainWindow.axaml:386-389`) | a hollow ring on `#2A2130` — the dot of a module that is switched off. Proof: run it against the real `rack-row-dot/off` capture. |
| `rack-row-dot-off` | the same cell, `#2A2130` | ground showing through the hollow ring of `Ellipse.dot` | a solid disc filling ~78% of the cell — an armed module. Proof: run it against the real `rack-row-dot/armed` capture. |

Every bite proof is a **real capture of the real app in the deliberately wrong state**, not a
synthetic buffer, and each check is shown failing by name (`FIRST FAILED CHECK: <name>`, exit 2).
Stretch, if the core lands cleanly: a seeded-regression phase in `self-test.ps1` that replaces the
`rack-row:checked` brush, rebuilds, and proves `rack-row-selected-marker` trips — the same
throwaway-edit pattern the rail-door phase already uses, restoring from bytes held in memory.

**A check I will NOT add:** a whole-rack-panel region check. One row's accent bar is a fraction of
a percent of the panel, so such a check passes with the rack in any state. That is SP-113's
"cannot fail" and it stays out.

## 4. The lease, exactly

`capture.ps1` runs outside the test harness and does not take the lease today. I will add, in
`capture.ps1` itself (so `self-test.ps1` and every future capture inherit it):

- open `Join-Path ([IO.Path]::GetTempPath()) 'ccp-real-desktop.lease'` with
  `FileMode.Create, FileAccess.Write, FileShare.Read` and write `pid=$PID` — byte-for-byte the
  contract `RealDesktopLease.TryTake` uses, so a floor run and a capture contend correctly;
- poll to a deadline; on refusal, **fail** naming the holder pid read out of the file (read
  sharing is granted precisely so the holder is nameable), never retry-around it;
- release in `finally`, so a failed capture does not wedge the machine.

The bite-proof driver will NOT hold an outer lease (that would deadlock against `capture.ps1`'s
own acquisition); `CcpVerify` touches no desktop.

**Fenced read.** `CopyFromScreen` today is unfenced. I will call `DwmFlush()` immediately before
it and record its HRESULT, the same edge as `FlashPixelProbe.CaptureDesktop`
(`client/tests/CcpClient.Tests/FlashPixelProbe.cs:178,235`) — SP-116 measured 34 misses in 1200
unfenced reads and 0 in 1500 fenced. A DWM that refuses is reported, not swallowed.

## 5. Tests and floor

`client/tests/CcpClient.Tests/RackPresentationTests.cs` (new, pure logic, no Avalonia):
manifest-shape facts (every rack check is `presentation-verified`; its surface is one
`capture.ps1` accepts, parsed from the `ValidateSet` so the SP-094 "hard-coded list narrows
silently" failure cannot recur); four synthetic-buffer facts that each rack check rejects the
other state's ground colour; and two lexical guards on `capture.ps1` — it must take the lease
with `FileShare.Read`, and it must call `DwmFlush` before `CopyFromScreen`. Each guard is written
so deleting the thing it guards reddens it.

`client/tests/CcpClient.HeadlessTests/` : a small file tying the manifest's expected colours to
the product's actual brushes (`rack-row:checked` BorderBrush/Background, `Border.rack`
Background), so changing a brush reddens a named test that says which manifest entry to update
instead of leaving the manifest silently wrong.

Delta declared in `spine-tasks/SP-122-rack-presentation/floor-delta.json`. Pin 2270 / 141; my
run will observe pin + delta and I will state both numbers. I will not open `floor.json`.
`check-warnings.mjs` and `check-floor.mjs` run alone, never beside a capture.

## 6. What this will NOT prove (stated up front)

- **No human has compared any of this to the WPF build.** `presentation-verified` here means
  composited pixels were read back and matched named colours in named regions. Upstream
  comparison by eye stays a MANUAL gate and I will name it, not discharge it.
- Colour and position at 4 points on one Windows machine at scale 1.75. Not typography, not
  animation, not the rack's scroll behaviour, not any of the other 13 rows, not Linux/WSLg.
- `CopyFromScreen` reads the composited desktop, so unlike SP-115's `PrintWindow` it is not blind
  to transparent-versus-black — but it IS blind to a foreign topmost window owning the point, and
  the lease cannot exclude one (`RealDesktopCollection.cs:44-48`). That residue stands.

## 7. Stop conditions I will honour

If the dot cell cannot be aimed without changing the rack's markup, or if arming the module puts
anything on the screen, I stop and record it rather than editing product code. Same if the
measured pass/fail fractions are not far apart: a threshold squeezed between two close numbers is
a tolerance the size of the defect it hides, and I would report the surface as not verifiable
rather than tune it.
