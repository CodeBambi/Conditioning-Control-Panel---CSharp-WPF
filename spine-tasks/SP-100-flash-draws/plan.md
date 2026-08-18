# SP-100 — plan checkpoint (before the first product edit)

Branch `lane/SP-100-flash-draws`, base `845a40b8`, worktree
`.claude/worktrees/agent-aeacaea0d95becc4c`. Review Level 3.

---

## 0. The measurement that settles the packet, taken before any of it was written

A throwaway console probe (scratchpad, never in the repo) built the exact SP-099 window shape and
asked what reaches the screen. Six rounds; the load-bearing results:

```
screen=2880x1800 (process made PER_MONITOR_AWARE_V2); the window that owns the
  centre point before anything: 0x130938 class="HwndWrapper[ConditioningControlPanel;;...]"
  title="Conditioning Control Panel v6.8.1"        <- the shipping product, topmost, live
[hidden paint]     BitBlt into GetDC(hwnd) while the window is HIDDEN -> GetPixel = 0xFFFFFFFF
                   (CLR_INVALID): the paint is DISCARDED. Order must be show-then-paint.
[unpainted show]   shown at alpha 255, never painted -> composited desktop reads the pixel that
                   was UNDER it (0x26171E). A freshly shown surface is not a black flash.
[paint]            BitBlt from a top-down 32bpp DIB -> window DC GetPixel = 0x30C0F0 (the frame)
[composited, t=0]  BitBlt(screenDC, SRCCOPY|CAPTUREBLT) at the surface's rect, NO WAIT AT ALL
                   -> 0x30C0F0. The composited desktop holds the painted pixel immediately.
[t=0..2500ms]      stable, and the surface keeps the hit test the whole time (1 raise needed)
[PrintWindow]      PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT) -> 0x30C0F0. DWM's own rendering
                   of the window carries the frame; independent of the DC that was painted.
```

A full-desktop PNG from that run was eyeballed: the golden rectangle sits over the shipping WPF
app's Studio page. **A GDI blit into SP-099's raw HWND is composited and visible.**

One trap inside the measurement, and it is worth recording because it nearly produced a false
negative: the first four rounds read `0x26171E` (the WPF app's pixels) at what looked like the
surface's centre and appeared to prove nothing was composited. The probe process was
**DPI-unaware**, so USER32 virtualised the window's coordinates (1646x1029 space) while the screen
DC was physical (2880x1800). The surface was on screen the whole time; the probe was reading the
wrong point. Consequence for the tests below: **no in-suite assertion may mix window coordinates
with screen-DC coordinates without deriving the ratio from the OS** (`DESKTOPHORZRES/HORZRES`),
and the derivation itself is asserted.

---

## 1. The content route, and its defence (residual 1)

**Chosen: GDI `BitBlt` from a DIB section into the existing `WS_POPUP` HWND, composited at the
constant `LWA_ALPHA` SP-099 already sets and already asks the OS to confirm.**

Rejected, with reasons:

- **`UpdateLayeredWindow`.** It is mutually exclusive with `SetLayeredWindowAttributes` on the same
  window: once ULW owns the surface, `GetLayeredWindowAttributes` no longer answers for it. That
  call is SP-099's **ghost check** — the one measured discriminator between a real surface and the
  defect the first attempt shipped (`CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45`;
  `OverlayReasonCodes.OverlayNotComposited`). Taking ULW would delete the capability's best fact to
  buy per-pixel alpha **that WPF's flash does not use**: a WPF flash window is a rectangle with a
  black background (`Services/Flash/FlashService.cs:1245`) filled edge to edge by an `Image` pinned
  to the window's own size (`:1274-1281`, geometry at `:2290-2315`) and a uniform window opacity.
  Constant alpha over a rectangle *is* the shipped shape.
- **Attaching an Avalonia top-level.** It replaces the HWND, and with it every SP-099 confirmation —
  `Confirm`/`ConfirmInputRouting` are private and bound to `_window`, so the refactor is not the
  cost, the **evidence** is: z-order walk, both-polarity hit test, alpha read-back and foreground
  check are all written against a handle this backend owns. It is also the first attempt's shape
  (`CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs:14` subclasses `Avalonia.Controls.Window`),
  which the constitution classes as failure evidence, and Avalonia exposes nothing that can answer
  "is it really on top" or "does it really pass clicks through".
- GDI keeps the whole SP-099 fact set intact, adds no package, adds no Avalonia dependency to a
  Windows-only mechanism, and is verifiable from the pure-logic test project with no Avalonia
  runtime — the same reason `Win32TrayPresence` and `Win32OverlayPresence` own their handles.

**Sequencing, as measured:** create/show (`Present`, which is where the OS confirmations live) and
only then `Paint`. Painting a hidden window is discarded, and a freshly shown unpainted surface
composites as what is underneath it, so show-then-paint has no black-frame artifact on this
machine. `Paint` is also serviced from `WM_PAINT` off the retained DIB, so an OS-requested repaint
does not blank the flash (the wndproc stops being `DefWindowProcW` and becomes a managed proc that
handles `WM_PAINT` and forwards everything else).

## 2. How `Present` stays off the frame path (residual 2)

`Present` is called **once per surface per flash** — at most `ImagesPerFlash` (<= 20, capped at
WPF's `MAX_CONCURRENT_FLASH = 10`, `FlashService.cs:50`) times per flash, and a flash comes due at
most once per 3 s (`FlashSchedule.MinimumIntervalSeconds`). Content goes through the new
`Paint(OverlayFrame)`, which does **no** z-order walk, **no** hit test and **no** raise loop: it
blits and then asks the OS for the window's content back. There is no render loop and no animation;
a flash is one frame that lives for its lifetime and then leaves. A fact pins it: with a recording
presence, `PresentCalls == surfaces shown` and never grows with paints or with the topmost cadence.

## 3. `IsPresenting` is not consulted (residual 3)

The presenter keeps its own per-slot state and calls `Present` on every show, including on a
recycled slot. A fact drives a presence whose `IsPresenting` is hard-wired **true** and asserts the
presenter still calls `Present` — i.e. the latch cannot make the port skip a placement.

## 4. The input-opaque flicker (residual 4)

Untouched and unhidden: every `Present` still flips polarity twice inside `ConfirmInputRouting`.
The port does not add a *second* source of it — `Paint` never touches styles, and the topmost
cadence (below) is `SetWindowPos` only. The exposure is therefore bounded by the number of
surfaces in a flash, and it is named in `record.md` and in the divergence row.

## 5. Sustained topmost (residual 5) — solved, not just named

`Raise()` becomes a public `Reassert()`: one `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE|NOSIZE|
NOACTIVATE)` (WPF `ForceTopmost`, `FlashService.cs:3867`) that returns **no** `CapabilityState`,
because it confirms nothing — it is the cadence, not a claim. The presenter drives it on the
**injected session clock at WPF's ~1 s cadence** (`FlashService.cs:206-243`) for exactly as long as
a surface is up, posted through the one UI dispatch boundary with the liveness check inside the
delegate. Zero wall-clock in tests: the manual clock drives it. Reason to solve rather than name:
the contender is not hypothetical — the probe found the shipping WPF app topmost over the point on
this very machine, and a 6 s flash that loses the band after one second is a flash nobody sees.

## 6. What happens with no overlay

- `OverlayPresenceFactory.Create()` already returns `UnsupportedOverlayPresence` off Windows.
  `Paint` gets the same typed refusal as `Present`/`Withdraw`/`SetClickThrough` — a refusal that
  covers three of four operations is half a refusal, and a fact asserts all four.
- The presenter treats every refusal as **recorded, never thrown and never retried into a loop**:
  it decodes nothing further for that flash, presents nothing, and keeps the last
  `CapabilityState` for diagnosis.
- The effect is unchanged in every observable way: the flash still comes due, still draws from the
  pool, still counts, still raises `Fired`, still re-schedules, still stops. SP-098's facts are not
  edited. Two new facts drive the whole session with the **Linux** backend on this Windows box and
  assert the count/stop behaviour is identical to the no-surface case.
- The surface work only ever happens inside the existing UI projection, which SP-098 already skips
  when the dispatch boundary is unbound — so a headless/unit session creates no window at all.

## 7. What a headed capture must show (the sentence that is part of the deliverable)

> On a live, unlocked, non-RDP Windows desktop with the client running and at least one image in
> `<dataDir>/assets/images`, within one flash interval of pressing **START** the capture must show
> **the user's own image, drawn opaquely, over the top of every other application's window** —
> including over the shipping WPF app if it is running — as a rectangle whose width is
> `max(50, round(origW * min(0.4*monW/origW, 0.4*monH/origH)))` (WPF's 40 %-of-monitor base at
> `ImageScale` 100, `FlashService.cs:2292-2300`), placed at least 50 px from every screen edge
> (`SpawnEdgePadding`, `:2320`), with **no window frame, no caption, no taskbar button**, and with
> the mouse cursor still able to click whatever is underneath it. Up to five such rectangles appear
> per flash, 300 ms apart (`:1112`), each staying up 6 s (`duration 5 s + 1 s`, `:1073`) and then
> vanishing with no fade. Pressing **STOP** while they are up must remove all of them at once.
> A capture that shows the client's own window, an empty or black rectangle, a rectangle behind
> another window, or a frame/titlebar, falsifies this packet.

Two triggers are provided, both documented in `record.md`:

- **deterministic, in-suite** — `dotnet test ... --filter "FullyQualifiedName~FlashDraw"`, which
  presents real surfaces, paints a known frame, reads the composited desktop back and writes
  PNG evidence to a stated path. No wall-clock wait anywhere; the surfaces are up for a few dozen
  syscalls, so an *external* screenshot cannot catch them — the PNGs are the artifact.
- **end to end, headed** — set `FlashesPerHour` to 180 and `ImagesPerFlash` to 1-5 in
  `session_preset.json`, drop images in `<dataDir>/assets/images`, run the client, Studio ->
  START. The first flash is due in 14-26 s and stays up 6 s; that is the window for the
  orchestrator's screenshot.

**I do not claim `presentation-verified`.** Everything in-suite proves what the OS holds and
composites, from inside the process; that a human sees it is the orchestrator's capture.

## 8. Files

| file | change |
|---|---|
| `Overlay/OverlayFrame.cs` | NEW. Width/height/top-down BGRA, validated. Knows nothing about images. |
| `Overlay/IOverlayPresence.cs` | `Paint(OverlayFrame)` added; contract text for it. |
| `Overlay/Win32OverlayPresence.cs` | `Paint`, retained DIB, `WM_PAINT` servicing, public `Reassert()`, content read-back via `PrintWindow(PW_RENDERFULLCONTENT)`. |
| `Overlay/UnsupportedOverlayPresence.cs` | `Paint` refuses like the rest. |
| `Overlay/Win32OverlayInterop.cs` | GDI32 entries (`CreateDIBSection`, `BitBlt`, `CreateCompatibleDC`, `SelectObject`, `DeleteObject`, `DeleteDC`, `GetDC`/`ReleaseDC`), `PrintWindow`, `BeginPaint`/`EndPaint`. |
| `Overlay/OverlayReasonCodes.cs` | `overlay-frame-size-mismatch`, `overlay-content-not-held`, `overlay-paint-refused`. |
| `Effects/FlashGeometry.cs` | NEW, pure: WPF's `CalculateGeometry` + `SpawnBounds` + `IsOverlapping`. |
| `Effects/FlashImageDecoder.cs` | NEW: `IFlashFrameSource` seam + GDI+ (`gdiplus.dll`) decode-to-size. Windows-only, no new package. |
| `Effects/FlashSurfacePresenter.cs` | NEW: the consumer. Stagger, lifetime, withdraw, cadence, cap, refusal recording. |
| `Effects/FlashImagesEffect.cs` | one optional seam + one call inside the EXISTING UI projection. No change to pacing, pool, count or stop. |
| `Session/SessionParticipant.cs` | builds the presenter with a lazy `OverlayPresenceFactory.Create` (optional parameter, so `CompositionRoot` — out of scope — is untouched). |
| `client/tests/CcpClient.Tests/FlashDrawTests.cs` + `FlashPaintObservations.cs` + probe additions | the facts. |
| `client/docs/wpf-surface-reachability.md` | divergences D57+. |
| `spine-tasks/SP-100-flash-draws/{plan,record}.md`, `floor-delta.json` | artifacts. |

Nothing under `Views/**`, `Lifecycle/**`, `Persistence/**`, `client/tools/**`,
`client/tests/floor/floor.json`, `client/docs/task-board.md` or `ConditioningControlPanel/**`.

## 9. Facts to add (pure-logic project; headless delta 0)

Draw path: DWM's own rendering of the surface equals the painted frame; the composited desktop
holds the frame at the surface's rect (DPI ratio derived from the OS and asserted); paint with
nothing presented refuses; a wrong-sized frame refuses; the Linux backend refuses to paint too.
Effect path: present-once-per-surface (never per frame); `IsPresenting` is not consulted; lifetime
withdraw on the injected clock; stop withdraws everything; the cap is WPF's 10. Geometry: the 40 %
base, the x scale, the 50 px floor, edge padding, the overlap re-pick. Decoder: a real file decodes
to the requested size; a corrupt one yields nothing and never throws. No-overlay: the effect counts,
re-schedules and stops identically with a refusing surface.

## 10. Known scope-boundary finding (reported, not fixed here)

`client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml:152` tells the user *"Showing the images
over your other windows is not ported yet … it just has nowhere to draw."* After this packet that
sentence is **false on Windows**. `Views/**` is outside this packet's File Scope, so the text is not
edited here; it is reported as a discovery with the exact file, line and the replacement a
follow-up needs.
