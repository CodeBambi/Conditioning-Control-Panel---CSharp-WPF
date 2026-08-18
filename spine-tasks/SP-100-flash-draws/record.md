# SP-100 — the first composited pixel

Branch `lane/SP-100-flash-draws`, base `845a40b8`, worktree
`.claude/worktrees/agent-aeacaea0d95becc4c`. Review Level 3; the plan checkpoint is `plan.md`,
written and committed before the first product edit.

**What landed:** SP-098's effect hands the paths it drew to SP-099's surface. A flash is now a
picture on the screen, above every other application, and the operating system is asked what the
surface holds afterwards. `wpf-surface-reachability.md` D47 closes on Windows and stays open on
Linux.

---

## 1. The measurements, taken before any of it was written

A throwaway console probe (scratchpad, never in the repo) built SP-099's exact window shape and
asked the OS what reaches the screen. Seven rounds. The load-bearing results:

| measurement | result |
|---|---|
| paint into `GetDC(hwnd)` while the window is **hidden** | discarded — `GetPixel` returns `0xFFFFFFFF` (`CLR_INVALID`). **The order must be show-then-paint.** |
| a layered window **shown and never painted** | the composited desktop reads what was UNDER it. No black frame, so show-then-paint has no visible artifact |
| `BitBlt` from a top-down 32bpp DIB into the window DC | the window's own DC reads the painted colour immediately |
| composited desktop, `BitBlt(SRCCOPY \| CAPTUREBLT)`, **no wait at all** | the painted colour. `CAPTUREBLT` is required: without it a screen read cannot see a layered window |
| the same, held for 2.5 s | stable; the surface kept the hit test throughout (1 raise needed) |
| `PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT)` | **all black on the first call after a show**, correct afterwards — it goes through DWM asynchronously |
| `PrintWindow(hwnd, dc, 0)` | correct every time, at alphas 255/153/26, unblended. **This is the instrument the tests use** |
| who owns the point before anything | `0x130938 class="HwndWrapper[ConditioningControlPanel;;…]" title="Conditioning Control Panel v6.8.1"` — the shipping product, topmost, live |
| `UpdateLayeredWindow` after `SetLayeredWindowAttributes` (round 8, added at revision) | **FALSE, `ERROR_INVALID_PARAMETER` (87)** |
| clearing and re-setting `WS_EX_LAYERED` alone | harmless — the OS still reports `alpha=255 flags=0x2` |
| the toggle **then** `UpdateLayeredWindow` | TRUE, and `GetLayeredWindowAttributes` returns **FALSE for good**: the ghost check is two ordinary lines away from silence |

**The trap inside the measurement, and it nearly produced a false negative.** Rounds 1-4 read
`0x26171E` (the WPF app's pixels) at what looked like the surface's centre and appeared to prove
that nothing was composited. The probe process was **DPI-unaware**, so USER32 virtualised the
window's coordinates (1646x1029) while the screen device context stayed physical (2880x1800): the
surface was on screen the whole time and the probe was reading the wrong point. Consequence, carried
into the suite: no fact may mix window coordinates with screen-DC coordinates without deriving the
ratio from the OS (`DESKTOPHORZRES / HORZRES`), and `FlashDrawTests` asserts the derivation itself.

## 2. The content route, and its defence (SP-099 residual 1)

**GDI `BitBlt` of a B,G,R,X frame into the existing `WS_POPUP` hwnd, composited at the constant
`LWA_ALPHA` SP-099 already sets and already asks the OS to confirm.**

- **Not `UpdateLayeredWindow`.** It is mutually exclusive with `SetLayeredWindowAttributes`: once
  ULW owns the window, `GetLayeredWindowAttributes` no longer answers for it — and that call is the
  **ghost check**, the one measured discriminator between a real surface and the defect the first
  attempt shipped (`CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45`,
  `OverlayReasonCodes.OverlayNotComposited`). It would buy per-pixel alpha **that WPF's flash does
  not use**: upstream's flash window is a black-backed rectangle (`FlashService.cs:1245`) filled
  edge to edge by an `Image` pinned to the window's own size (`:1274-1281`) at one uniform opacity.
- **Not an Avalonia top-level.** It replaces the HWND, and with it every SP-099 confirmation — the
  z-order walk, the both-polarity hit test, the alpha read-back and the foreground check are all
  written against a handle this backend owns, and `Confirm`/`ConfirmInputRouting` are bound to
  `_window`. The refactor was never the cost; the **evidence** was. It is also the first attempt's
  own shape (`CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs:14` subclasses
  `Avalonia.Controls.Window`), which the constitution classes as failure evidence.
- GDI keeps the whole SP-099 fact set intact, adds no package, adds no Avalonia dependency to a
  Windows-only mechanism, and is provable in the pure-logic test project with no Avalonia runtime.

Recorded as divergence **D57**.

## 3. The other four residuals

| # | residual | what SP-100 did |
|---|---|---|
| 2 | `Present` walks every top-level window; wrong as a render loop | `Present` is called **once per surface per flash** and never per frame. Content goes through the new `Paint`, which touches no style, walks no z-order and asks no hit test. `PresentIsCalledOncePerSurfacePerFlash_AndNeverToChangeContent` pins it: one `Present`, one `Paint`, three cadence re-assertions |
| 3 | `IsPresenting` is a latch, not a live fact | Never consulted. The presenter keeps its own slot state and re-presents every show, including a recycled slot. `IsPresentingIsNeverConsulted_ASurfaceThatLiesAboutItIsStillPresented` drives a presence whose latch is hard-wired **true** and requires the placement to happen anyway |
| 4 | every `Present`/`SetClickThrough` briefly makes the surface input-opaque | Untouched and unhidden; the port adds **no second source** of it. `Paint` never touches styles, `Reassert` is one `SetWindowPos`, and `SetClickThrough` is never called by the presenter (polarity is decided once, in the request). Exposure is therefore bounded by the number of surfaces in a flash |
| 5 | `Raise()` is private, so WPF's ~1/s topmost cadence has no entry point | **Solved, not named.** `IOverlayPresence.Reassert()` is public and returns **nothing** — it confirms nothing, and a `CapabilityState` there would be a claim with no round-trip behind it. The presenter drives it on the injected session clock at WPF's cadence (`FlashService.cs:206-243`) for exactly as long as a surface is up. Reason to solve: the contender is not hypothetical — the probe found the shipping WPF product owning the point on this machine, and a six-second flash that loses the band after one second is a flash nobody sees. Divergence **D62** |

## 4. What was built

| file | what |
|---|---|
| `Overlay/OverlayFrame.cs` | NEW. Width, height, top-down B,G,R,X buffer, validated. Content-free: the overlay never learns what a flash is |
| `Overlay/IOverlayPresence.cs` | `Paint(OverlayFrame)` and `Reassert()` added, with the contract text for both |
| `Overlay/Win32OverlayPresence.cs` | `Paint`, the retained DIB + memory DC, the independent read-back DIB, `WM_PAINT` servicing (a managed wndproc replaces `DefWindowProcW`), public `Reassert`, GDI teardown |
| `Overlay/Win32OverlayInterop.cs` | `GetDC`/`ReleaseDC`/`BeginPaint`/`EndPaint`, `CreateDIBSection`, `BitBlt`, `CreateCompatibleDC`, `SelectObject`, `DeleteObject`, `DeleteDC`, plus `BITMAPINFO`/`PAINTSTRUCT` |
| `Overlay/OverlayReasonCodes.cs` | four codes: `overlay-frame-size-mismatch`, `overlay-paint-refused`, `overlay-content-not-held`, `overlay-no-display` |
| `Overlay/UnsupportedOverlayPresence.cs` | `Paint` refuses like everything else; `Reassert` is a silent no-op that claims nothing |
| `Effects/FlashGeometry.cs` | NEW, pure: WPF's `CalculateGeometry` (`:2292-2301`), `SpawnBounds` (`:2374-2383`), `IsOverlapping` (`:2539-2547`) and the ten-attempt re-pick (`:1198-1207`) |
| `Effects/FlashFrameSource.cs` | NEW: the `IFlashFrameSource` seam and a GDI+ (`gdiplus.dll`) decode-at-display-size implementation, over black |
| `Effects/FlashSurfacePresenter.cs` | NEW: the consumer. Stagger, lifetime, withdraw, topmost cadence, cap, slot recycling, and every refusal kept verbatim |
| `Effects/FlashImagesEffect.cs` | one optional seam, one call inside the EXISTING UI projection, one hide on disarm. **No change to pacing, pool, count, dot or stop** |
| `Session/SessionParticipant.cs` | builds the presenter on the same clock and the same dispatch boundary, lazily, so `CompositionRoot` (out of scope) is untouched |

**Nothing under** `Views/**`, `Lifecycle/**`, `Persistence/**`, `client/tools/**`,
`client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`.

## 5. What is proven, and by which instrument

`client/tests/CcpClient.Tests/` — 40 new facts, no wall-clock wait anywhere in them.

**From the operating system, with instruments that share no declaration with the product**
(`FlashPixelProbe`, a second copy of every read-back path):

| fact | instrument |
|---|---|
| the surface's own device context holds the painted frame | the product's `Paint` confirmation: `BitBlt` the window DC into an independent DIB and compare 1024 spread samples plus four corners and the centre |
| the OS's own rendering of that window is the frame, **every pixel of it**, the right way up | `PrintWindow(hwnd, dc, 0)`, compared against the two-tone frame |
| **the composited desktop carries the frame at the surface's rectangle**, both halves, with no wait | `BitBlt(screenDC, SRCCOPY \| CAPTUREBLT)`, mapped through `DESKTOPHORZRES/HORZRES` |
| withdrawing takes it back off the composited desktop | the same read, after `Withdraw` |
| **a real `.png` on disk** — the product's GDI+ decoder, the product's presenter, a real surface — reaches the composited desktop and leaves it when hidden | a full-desktop capture, counting pixels of a colour nothing else on a desktop is (the placement is a random roll, so the count needs no knowledge of where it landed) |
| a paint with nothing presented / after withdraw / after dispose / of the wrong size | four typed refusals, none of them `Available` |
| the Linux, macOS and Unknown backends refuse to **paint** as well as to present | `OverlayPresenceFactory.CreateFor`, driven on this Windows box |
| **the ghost check still answers on the far side of a paint** — the OS holds the requested alpha, a full re-`Present` still earns `Available`, and re-placing does not blank the surface | `GetLayeredWindowAttributes` through the probe, after the paint; the product's own `Present`; `PrintWindow` again |
| the instrument itself can say "no" | the control: a layered topmost window this probe paints with its **own** calls, re-raised the same bounded way, then read from its own desktop capture. Every composited expectation is compared against that measured machine property, so a locked workstation or a session with no compositor reports "not live" instead of turning the flash's fact red for a reason that has nothing to do with the flash |

**With no screen involved:** the surface is drawn BEFORE `Fired` is raised, so no UI subscriber can
hold the visible half up; the presenter's teardown is POSTED through the dispatch boundary rather
than run down the shutdown thread, which is not the window's thread; the stagger is 300 ms (`:1112`), the lifetime is 6 s (`:1073`), the
cadence is 1 s and stops with the last surface (`:206-243`), the cap is 10 (`:50`), surfaces are
recycled across flashes, `Present` is once per surface, `IsPresenting` is never read, a surface that
does not hold its frame is taken straight back off, an undecodable image contributes no surface and
the rest of the flash still appears, stop takes everything off at once and cancels the staggered ones
that had not appeared yet, and WPF's geometry (40 % of monitor, the scale multiply, the 50 px floor,
the 50 px edge padding, the 30 %-of-candidate overlap rule) holds at its boundaries.

**Undischarged, and named.** That a **human sees a flash**: a composited desktop read from inside the
process cannot see a Magnifier, a mirror driver, an exclusive-fullscreen swap chain, colour
management, or a monitor that is physically off. `presentation-verified` is the orchestrator's headed
capture and **is not claimed here**. Also undischarged: multi-monitor and cross-DPI placement (this
machine reports one display; the port places on the primary only); sustained topmost over minutes of
real contention; the fade WPF has and this does not (D60); WebP (D63); and every part of Linux.

## 6. The no-overlay path

- `OverlayPresenceFactory.Create()` returns `UnsupportedOverlayPresence` off Windows, and `Paint`
  now refuses there like every other operation — a refusal that covered three of four operations
  would be half a refusal, and a fact asserts all four.
- The presenter records the refusal **verbatim**, including the manual gate, in `LastPresent`. It
  paints nothing, retries nothing, throws nothing.
- The effect is unchanged in every observable way. `WithARefusingOverlay_TheFlashStillComesDue_
  StillCounts_AndStillStops` drives the REAL participant with the **Linux** backend on this Windows
  box: two flashes come due, ten draws happen, `SurfacesShown` is 0, `LastPresent` is the typed
  refusal, stop stops, and ten further clock windows produce nothing.
- **SP-098's facts pass unchanged** — `SessionSpineTests` + `FlashEffectTests` + `OverlayCapability
  Tests`: 63 passed, 0 failed, and not one of those files was edited.
- The surface work only ever happens inside the effect's existing UI projection, which SP-098
  already skips when the dispatch boundary is unbound, so a unit or headless session creates no
  window at all and schedules nothing on the clock.

## 7. Prove it bites (packet step 5)

Both mutations were applied to the committed tree, measured, and reverted with `git checkout --`;
`git status` was clean after each and the restored tree rebuilt at 0/0 and re-ran green.

**Mutation A — claim the paint without ever drawing it.** `var blitted = BitBlt(…)` became
`var blitted = true;`.

```
Failed!  - Failed: 2, Passed: 13, Skipped: 0, Total: 15
```

and the backend refused **unprompted**, with the code that exists for it:

```
Unavailable(overlay-content-not-held: the blit into window 0x9F08A6 returned TRUE, and the OS
returns 0xA81CE8 at 0,0 where the frame carries 0x30C0F0. The surface does not hold what was drawn
into it, so 'it was painted' is not claimed — a draw call that returned is not a picture on a screen)
```

**Mutation B — the silent one: a bottom-up DIB.** `biHeight = -height` became `biHeight = height`,
i.e. the flash appears upside down and nothing errors.

```
Failed!  - Failed: 3, Passed: 12, Skipped: 0, Total: 15
```

```
the OS rendered 0 of 43200 pixels as the frame (interactive desktop = True)
…
the composited desktop at the top half of the flash reads 0xF03080 against the frame's 0x30C0F0
```

**Mutation C — the plausible-looking one that did NOT bite, and what it taught.** Clearing and
re-setting `WS_EX_LAYERED` inside `Paint` (MSDN's prescribed step towards `UpdateLayeredWindow`) left
every fact green — because the OS still reports the alpha after a bare toggle. Measured rather than
assumed, and it is why the mutation below is the real one: the hazard does not announce itself in
halves.

**Mutation D — the one D57 exists to prevent: an alpha ramp written the obvious way.** The toggle
plus `UpdateLayeredWindow` in `Paint`, which is what a future packet reaches for (D60 names the ramp
as the next natural job).

```
Failed!  - Failed: 3, Passed: 13, Skipped: 0, Total: 16
```

```
PaintingCostsNothingTheGhostCheckNeeds…  Expected: 255   Actual: -1
```

The alpha read-back is **gone** — `GetLayeredWindowAttributes` returns FALSE — and the port's only
measured discriminator against the first attempt's defect would have gone quiet with every
pre-revision fact still green. The backend also refused the paint on its own
(`overlay-content-not-held`, `the OS returns 0x000000 at 0,0`), because after `UpdateLayeredWindow`
the surface can no longer be read back through its device context.

**Mutations E and F — the two claims that had nothing behind them.** Swapping `Show` and `Fired` in
`Project`, and disposing the presenter straight down the shutdown thread instead of posting it:

```
Failed!  - Failed: 2, Passed: 22, Skipped: 0, Total: 24
Expected: "draw"  (collections differ at index 0)
the surface was disposed straight down the shutdown thread instead of through the UI dispatch
boundary — on a real host that is the thread that cannot destroy the window
```

**And the finding inside mutation B, recorded as D64:** under mutation B the product's OWN confirmation still
said `Available`, because its read-back travels through a DIB with the same header as the frame and
therefore compares like with like. A consistent orientation error is invisible to the product's
self-check and is caught only by the test's independent instruments. That is the argument for the
second instrument, and it is why the test frame is two-tone.

## 8. The headed capture (packet step 6)

**What a capture must show — the sentence this packet is judged against:**

> On a live, unlocked, non-RDP Windows desktop with the client running and at least one image in
> `<dataDir>/assets/images`, within one flash interval of pressing **START** the capture must show
> **the user's own image, drawn opaquely, over the top of every other application's window** —
> including over the shipping WPF app if it is running — as a rectangle whose size is
> `max(50, floor(source * min(0.4*monW/sourceW, 0.4*monH/sourceH)))` per axis (WPF's
> 40 %-of-monitor base at `ImageScale` 100, `FlashService.cs:2292-2301`), placed at least 50 px from
> every screen edge (`SpawnEdgePadding`, `:2320`), with **no window frame, no caption and no taskbar
> button**, and with the mouse still able to click whatever is underneath it. Up to five such
> rectangles appear per flash, 300 ms apart (`:1112`), each staying up 6 s (`:1073`) and then
> vanishing with no fade. Pressing **STOP** while they are up must remove all of them at once.
> **A capture that shows the client's own window, an empty or black rectangle, a rectangle behind
> another window, or a frame/titlebar, falsifies this packet.**

**Trigger A — deterministic, in-suite, ~1 second, no wall-clock wait.** From the repo root:

```
dotnet build client/CcpClient.sln -c Debug --nologo
dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo --no-build --filter "FullyQualifiedName~FlashDrawTests"
```

It puts real surfaces on the real desktop, paints known frames, reads the composited desktop back,
and writes evidence to **`%TEMP%\ccp-sp100-flash-draws\`**:

| file | what it is |
|---|---|
| `desktop-with-a-real-flash.bmp` | the **whole desktop** while a real `.png` flash is up — this is the capture to look at |
| `desktop-where-the-flash-is.bmp` | the composited desktop cropped to the surface's rectangle: must be the two-tone frame, gold above purple |
| `surface-as-the-os-renders-it.bmp` | `PrintWindow` of the surface: the same two-tone frame |

The surfaces are up for a few dozen syscalls, so an EXTERNAL screenshot cannot catch them; the
bitmaps are the artifact. On this machine the whole-desktop bitmap showed the test image sitting over
the shipping WPF product's Studio page.

**Trigger B — the real application, end to end, for a human's own screenshot.**

1. Put one or more `.png`/`.jpg` images in `<dataDir>/assets/images` (`%APPDATA%\CcpClient\assets\images`
   on Windows).
2. In `<dataDir>\session_preset.json` set `"FlashesPerHour": 180` and `"ImagesPerFlash": 3`
   (or right-click the rack row and move the dials in the panel).
3. `dotnet run --project client/src/CcpClient.Desktop/CcpClient.Desktop.csproj -c Debug`
4. **Studio** door -> the **Flash Images** row -> the shell's **START**.
5. The first flash is due in **14-26 s** (`3600/180 = 20 s ±30 %`, floor 3 s) and each image stays up
   **6 s**. Screenshot during that window. Press **STOP** and screenshot again: the screen must be
   clear immediately.

## 9. Gate results

```
dotnet build client/CcpClient.sln -c Debug --nologo   ->  0 Warning(s)  0 Error(s)
node client/tests/floor/check-floor.mjs               ->  CcpClient.Tests total 1231 (pin 1191)
dotnet test client/tests/CcpClient.HeadlessTests/...  ->  81 passed, 0 skipped
```

Pin is **1191 unit / 81 headless**. Observed **1231 unit / 81 headless**. `1231 = 1191 + 40` and
`81 = 81 + 0`, which is exactly `floor-delta.json` (`unit: 40`, `headless: 0`). The floor run
therefore reports a violation against the shared pin, which is the expected and documented outcome
for a lane. **Nothing was widened, skipped or special-cased**; `client/tests/floor/floor.json` was
never opened and no name was added to `allowedSkips` (the two skips in the run are the pre-existing
Linux-probe pins). The document-reading guards were re-run after the
`wpf-surface-reachability.md` edit (`UpstreamPayloadInventoryTests`, `VersionDerivationTests`,
`AiOperationContractTests`, `VacuousShapeGuardTests`, `FloorWrapperGuardTests`,
`TestTimingGuardTests`, `AllowedSkipsBanGuardTests`, `LaunchFaultTextTests`): 79 passed, 0 failed.
No vacuous-shape ledger entry was needed.

## 10. What the review returned, and what it changed

Final review returned **REVISE** with three gaps. All three are closed above; each is named here
with the mutation that now holds it.

1. **The ghost check was unpinned across `Paint`.** D57's whole defence of the GDI route is that it
   does not cost `GetLayeredWindowAttributes`, and nothing red if that broke: SP-099 reads the alpha
   immediately after `Present`, before any content exists. Measurement round 8 showed the hazard is
   two ordinary lines away (`WS_EX_LAYERED` toggle + `UpdateLayeredWindow`), and that the toggle
   alone is harmless — so it does not announce itself in halves. Now pinned by
   `PaintingCostsNothingTheGhostCheckNeeds_…`, which mutation D reds (`Expected 255, Actual -1`).
2. **A sentence in shipped source that this packet's own D64 falsified.** `ContentSampleTarget`'s doc
   claimed corners-and-centre meant a "shifted, mirrored or blank" frame could not pass. Blank could
   not; **mirrored can**, because `EnsureFrameSurfaces` builds the frame section and the read-back
   section from one `BITMAPINFO` and the error cancels. Corrected at the mechanism, where the next
   lane reads it, and it now names the measured proof and points at the instruments that do catch it.
3. **Two claims with nothing behind them.** The draw-before-`Fired` order is now a fact (mutation E
   reds it). And the presenter's teardown is POSTED through the dispatch boundary instead of run
   down the shutdown thread — which is not the window's thread, so the old code took
   `Win32OverlayPresence`'s wrong-thread branch, `DestroyWindow` failed, and the visible-stop
   guarantee rested on process death. Mutation F reds the new fact, and the residual bound (a post
   that lands after the dispatcher has stopped is not delivered; the OS reclaims the windows at
   process exit; **what the user sees is dealt with by disarm, on the UI thread**) is stated at the
   call site rather than left to be discovered.

## 11. Findings and discrepancies

1. **The Studio panel's notice is now false on Windows, and it is outside this packet's File Scope.**
   `client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml:152` still reads *"Showing the images
   over your other windows is not ported yet: that needs an always-on-top click-through surface this
   build does not have. The schedule above is real and runs - it just has nowhere to draw."* On
   Windows the images now appear; on Linux the sentence is still exactly right. **Reported, not
   fixed:** `Views/**` is in this packet's must-not-change list. The follow-up is one string that
   says both halves, and it should read the presenter's own `LastPresent` rather than assert a
   platform.
2. **The product's own content confirmation cannot see a consistent orientation error** (§7,
   divergence D64). Stated rather than papered over; the independent instruments cover it.
3. **`PW_RENDERFULLCONTENT` is not deterministic without a wait** — it returned an all-black bitmap
   on the first call after a show. The suite uses `PrintWindow(…, 0)`, which was correct on every
   call at three different alphas. Recorded because the flag is the obvious choice and the obvious
   choice is wrong here.
4. **WebP is in the pool's extension list and GDI+ cannot decode it** (D63). Such a file contributes
   no surface, exactly like a corrupt file. Named as a decoder swap, not a design change.
5. **No spec-versus-code disagreement was found in the packet's citations.** Every WPF line cited in
   this record and in the divergence rows was read in the tree at this SHA. SP-099's own citation
   correction (`:3615`, `:3667-3668`, `:3867`) still holds.
6. **A cost worth naming:** the end-to-end fact writes a ~20 MB whole-desktop BMP to `%TEMP%` on
   every suite run. It is the packet's headed-capture artifact and it is overwritten each run.
