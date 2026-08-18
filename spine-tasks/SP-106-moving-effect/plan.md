# SP-106 — plan checkpoint (written before the first product edit)

Branch `lane/SP-106-moving-effect`, base `61be3b55`. Pin **1372 unit / 87 headless**.

---

## 1. WHICH EFFECT — the packet's named module is a poor fourth, and here is the evidence

The packet names **Bouncing Text** and gives one escape: *"If reading the source shows Bouncing
Text is a poor fourth — needing a capability the port lacks ... say so at the plan checkpoint with
evidence and propose a better one from the rack."* Reading the source shows exactly that named
condition. **Three independent blockers, none of them fixable inside this packet's File Scope.**

### 1.1 It needs per-pixel alpha, which this port's overlay does not have and deliberately refuses

WPF's bouncing text is glyphs on a **transparent** window: `AllowsTransparency = true`,
`Background = Brushes.Transparent`, a `Canvas` with one `TextBlock`/`OutlinedText` per logo
(`Services/Subliminal/BouncingTextService.cs:820-822`, `:838-840`). Only the letters are painted;
the desktop shows through everywhere else, and the text's own opacity is the dial
(`:889`, `:900` — `Opacity = opacity / 100.0`, default **100** at
`CCP.Core/Models/AppSettings.cs:3619-3624`).

The port's overlay composites **one uniform `LWA_ALPHA` over an opaque BGRX frame**, by design and
with the design's reason written down:

- `Overlay/OverlayFrame.cs:14-22` — "The fourth byte is padding, **NOT per-pixel alpha** ...
  Per-pixel alpha would mean `UpdateLayeredWindow`, which is mutually exclusive with
  `SetLayeredWindowAttributes` and would therefore delete the alpha read-back that
  `OverlayNotComposited` depends on."
- `Overlay/Win32OverlayPresence.cs:175` — `SetLayeredWindowAttributes(window, 0, request.Alpha, LwaAlpha)`.
  `LWA_COLORKEY` is not used anywhere.

So the port can produce only two things, and neither is upstream's outcome:

| Port option | What the user sees | Verdict |
|---|---|---|
| One full-screen surface, painted per frame with the text at its position | The **whole desktop** washed with the frame's background at the dial's opacity — at the shipped default (100) a **black screen** with text on it | Not the same product |
| A small surface sized to the text, moved each frame | An opaque **plate** sliding around the desktop | Degraded, and blocked by 1.2 anyway |

### 1.2 Moving a surface means re-`Present`ing, which is documented as wrong per frame — and which would swallow the user's clicks 60 times a second

The only way to change a surface's position is `IOverlayPresence.Present`, and the interface says
what that costs: *"`Present` walks the OS's top-level z-order and asks the window manager's hit test
in both polarities; that is right once per placement and **wrong per frame**"*
(`Overlay/IOverlayPresence.cs:80-85`; the same sentence again at `Effects/OverlaySurfaceSet.cs:19-22`,
"Present is not a frame path ... There is no render loop here").

It is worse than a cost. `ConfirmInputRouting` **momentarily clears click-through and requires the
point to route TO the surface** before restoring it (`Win32OverlayPresence.cs:547-576`, and the
`Available` text at `:211` says so out loud). At 60 Hz that is a full-screen window becoming
input-catching sixty times a second over whatever the user is doing.

`Overlay/**` is on this packet's **Must not change** list, so a cheap `Move` cannot be added here.

### 1.3 Its cadence is 60 Hz, and this port's `Paint` cannot be spent at 60 Hz

WPF drives the motion off `CompositionTarget.Rendering` — one callback per rendered frame, vsync
aligned — and says why it is not a `DispatcherTimer`: *"quantized to the ~15.6 ms OS tick, which
beats against the display refresh and produces low frame rate judder"*
(`BouncingTextService.cs:26-30`, `:181-182`). The port's `Paint` is
`Marshal.Copy(full frame)` + `BitBlt` + a **full-frame read-back `BitBlt`** + a sampled compare
(`Win32OverlayPresence.cs:295-320`, `:631-673`). Two full-screen blits and an 8 MB copy per frame,
sixty times a second, is not a frame path either.

**Conclusion: Bouncing Text cannot be drawn honestly by this port today.** That finding is recorded
(D83/D84) and is a real deliverable, but it is a finding about the OVERLAY capability, not about the
spine — and the spine is what this packet exists to test.

---

## 2. THE BETTER FOURTH — Spiral Overlay

SP-105 §1.5 point 5 already named it: *"Spiral Overlay is the natural fourth ... the same continuous
mechanism with a real payload."* Reading the source says it is also the only rack module that both
**moves every frame** and **fits the one drawing shape this port has proved**.

| Requirement | Spiral Overlay |
|---|---|
| Moves while it is on | The GIF's frames are advanced on a timer for the whole session (`Services/Notifications/OverlayService.cs:1369-1378`, `GifFrameTimer_Tick` `:1636-1650`) |
| Full-screen, ONE uniform opacity | `Image { Stretch = UniformToFill, Opacity = (SpiralOpacity / 100.0) * 0.1 }` filling a full-screen window (`OverlayService.cs:1689-1700`) — the surface is covered edge to edge by the image, so **no per-pixel alpha is needed** |
| Present once, paint per frame | Exactly `IOverlayPresence`'s documented pattern: "A caller shows a surface once and paints it" (`IOverlayPresence.cs:84-85`) |
| Cadence a test can drive | The GIF's own frame delay, default **50 ms**, clamped **[20, 500]** (`OverlayService.cs:1545-1552`) — 20 Hz, which the confirm-everything `Paint` path can actually sustain |
| A rack row | **The row already exists and is the port's last unwired one** (`StudioPage.axaml:74-79`; D5/D6 open for exactly this row) |
| Not a second Pink Filter | Same surface lifetime, **different every 50 ms** — which is the whole axis |

**Rejected alternatives, for the record.** Bubble Pop and Brain Drain need per-pixel alpha and
(respectively) input and screen capture; Mind Wipe is audio only (`Services/LockCard/MindWipeService.cs`
is NAudio and a 10 s scheduler, nothing visual); Mandatory Video needs a media stack.

---

## 3. THE WPF MOTION LAW, with citations

| Law | WPF | Port |
|---|---|---|
| The module is started by the session, gated on its own dial | `MainWindow/MainWindow.StartStop.cs:192-193` calls `App.Overlay.Start()` unconditionally; the service reads the flag (`OverlayService.cs:376-381`) | Same shape the spine already has: arm every module, the module's dial decides |
| It needs a dial AND a payload | `if (settings.SpiralEnabled && !string.IsNullOrEmpty(spiralPath))` (`OverlayService.cs:377`) | Two conditions, two typed outcomes |
| Where the payload comes from | `GetSpiralPath()` (`OverlayService.cs:299-319`): configured `SpiralPath` if the file exists, else the randomiser over the library folder, else the built-in resource | Configured path, else the first supported file in `<assets>/spirals`. Randomiser and built-in resource NOT ported (divergences) |
| Frames | `LoadSpiralGifFrames` (`:1516+`); frame count from the image's first frame dimension (`:1541-1543`) | `ISpiralFrameSource` seam; GDI+ `GdipImageSelectActiveFrame` in the product |
| Frame delay | property `0x5100` × 10 ms; **if `< 20` or `> 500` then 50**; missing property → 50 (`:1545-1552`) | Verbatim, as a pure function with its own facts |
| Advance | `_currentGifFrameIndex = (_currentGifFrameIndex + 1) % frames.Count` — a **loop**, never a stop (`:1641`) | Verbatim |
| The timer only exists when there is motion | `if (_spiralGifFrames.Count > 1 && …)` (`:1369`) — a one-frame spiral gets **no timer at all** | Verbatim, and it is why `Live` cannot simply mean "moving" (§5) |
| Opacity | `(opacity / 100.0) * 0.1` — "Very subtle opacity - 90% reduction" (`OverlayService.cs:1692-1693`). Dial default **10**, clamp **[0, 100]** (`AppSettings.cs:2670-2675`) | Verbatim. Dial default 10 → surface opacity 0.01 |
| Dial default | `SpiralEnabled` defaults **true** (`AppSettings.cs:2644`) — unlike the other three modules | Kept |
| Window shape | full-screen, `Topmost`, `ShowInTaskbar=false`, `ShowActivated=false`, `IsHitTestVisible=false`, `WS_EX_TOOLWINDOW|NOACTIVATE|TRANSPARENT|LAYERED` (`OverlayService.cs:1707-1735`) | `OverlaySurfaceRequest(display, opacity, ClickThrough: true)` — what Pink Filter already asks for |
| Reconcile | `RefreshOverlays()`: not showing + flag on → start; showing → update opacity; flag off → stop (`OverlayService.cs:437-450`) | `OwnedSessionEffect.Refresh()`, unchanged |
| Stop | `Stop()` → `StopSpiral()` (`OverlayService.cs:398-409`) | `ReleaseWork()` — cadence disposed AND surface withdrawn |
| Topmost cadence | the 500 ms reconcile loop's periodic unconditional kick (`OverlayService.cs:666-671`) | `OverlaySurfaceSet`'s existing 5 s cadence, as Pink Filter uses it |

---

## 4. THE PREDICTION — does `OwnedSessionEffect` carry per-frame work without a scheduler creeping back?

**Prediction: YES for the module, and the reason is a precedent SP-105 already set.**

1. `ISessionEffect` fits unchanged. Nothing in it names a clock. (Third time.)
2. `OwnedSessionEffect` fits unchanged. `WorkIsRunning` / `Engage` / `ReleaseWork` are exactly the
   three questions a moving module has to answer, and it answers all three.
3. **The module takes NO `ISessionClock`.** The frame cadence belongs to the **surface presenter**,
   beside the topmost cadence SP-105 already put there for the same reason
   (`PinkFilterSurfacePresenter` takes the clock; `PinkFilterEffect` takes none —
   `SessionParticipant.cs:116-119`). A cadence that keeps a SURFACE correct is the presenter's; an
   interval that decides when a MODULE is due is the module's. Bouncing Text and Pink Filter are the
   two ends of that distinction and the spiral sits on the surface end.
4. `PacedSessionEffect<TFiring>` does **not** fit and must not be bent to. Its `WorkIsRunning` is
   `ScheduleArmed` — a clock claim — which would read `Live` for a spiral whose surface the OS
   refused; and its `ReleaseWork` drops the one-shot while leaving the screen alone, which is right
   for a flash and wrong for a layer that is up for the whole session.
5. **One additive change to shared code, predicted now so it can be checked later:**
   `OverlaySurfaceSet.Place` always calls `Present`. A frame advance must paint WITHOUT presenting,
   so `OverlaySurfaceSet` gains a `Repaint(slot, frame)` — additive, the same grade of change as
   SP-105's nullable lifetime, and it withdraws a slot whose paint failed exactly as `Place` does.
   If it turns out to need MORE than that, that is the finding.

**Where I expect to be wrong:** the dot. See §5 — I do not think a single boolean survives contact.

---

## 5. THE DOT'S THIRD MEANING

- Paced `Live` = a claim about the **CLOCK** (a firing is scheduled). SP-101/SP-105.
- Continuous `Live` = a claim about the **SCREEN** (a surface is confirmed up). SP-105.
- Moving `Live` = a claim about the screen **AND that it will be a DIFFERENT screen a moment from
  now**.

The trap is that **"on screen but frozen" is two states, not one**, and WPF's own code proves it:
a one-frame spiral gets no timer (`OverlayService.cs:1369`) and is *supposed* to sit still. So:

```
Running  =  a surface I placed is up
         && the last frame I painted was really held
         && ( this content has ONE frame  ||  the next advance is scheduled )
```

An animated spiral whose cadence died is up, unchanging, and **not** `Live`. A still spiral is up,
unchanging, and **is** `Live`, because nothing more was ever promised. The module panel says which.

## 6. Scope

New: `Session/SpiralPresetDocument.cs`, `Effects/SpiralOverlayEffect.cs`,
`Effects/SpiralSurfacePresenter.cs`, `Effects/SpiralFrameSource.cs`, `Effects/SpiralLibrary.cs`.
Changed: `Effects/OverlaySurfaceSet.cs` (one additive method), `Session/EffectReasonCodes.cs`,
`Session/SessionParticipant.cs`, `Views/Pages/StudioPage.axaml(.cs)`.
Landed facts that must move because the row they describe stops being unported (NOT weakenings —
the SP-105 precedent at its §4): the three headless facts that assert the Spiral row has no dot and
no toggle, and `ContinuousEffectSpineTests.TheRackOrderIsWpfsOwn_…`, which gains one member.
