# SP-115 — plan checkpoint (written before the first product edit)

Branch `lane/SP-115-per-pixel-alpha`, base `f3c751c1`. Review Level 3.

I did not design and then hope. Before writing a line of product code I asked **this** operating
system what it will say about a per-pixel-alpha composite. Three probe rounds, raw output below.
The probe lives at `spine-tasks/SP-115-per-pixel-alpha/probe/` (inside this worktree) and is
deleted before the commit.

---

## 0. THE MEASUREMENT — raw, three rounds

```
os=Microsoft Windows 10.0.26200
screen virt=1646x1029 phys=2880x1800
```

### Round 1 — the territory, and a self-inflicted contamination

```
[Q3]  ULW on NON-layered window: ok=False err=87
[Q3b] toggle WS_EX_LAYERED then ULW: ok=True err=0
[Q3c] GetLayeredWindowAttributes after that: False alpha=0 flags=0x0
[Q1]  ULW on layered-at-creation window: ok=True err=0
[Q2]  GetLayeredWindowAttributes on the ULW window: False alpha=0 flags=0x0 err=0
[Q4]  GetDC(glyph)+BitBlt ok=True err=0 samples TL=0x000000 TR=0x000000 BL=0x000000 BR=0x000000
[Q5 flags=0] PrintWindow ok=True TL=0x000000 TR=0x000000 BL=0x000000 BR=0x000000
[Q5 flags=2] PrintWindow ok=True TL=0x000000 TR=0x000000 BL=0xFF00FF BR=0x404040
[Q7]  SLWA after ULW: ok=True GLWA now=True alpha=128 flags=0x2
[Q7b] ULW again after SLWA: ok=False err=87
[Q10] click-through ON  -> OTHER 0x130938 (HwndWrapper[ConditioningControlPanel;;...])
[Q10b] click-through OFF -> GLYPH
[Q12] z-order: glyph@1 backdrop@6 of 21 visible
```

Round 1 poisoned its own window at `Q7` (see §2, finding 3), so `Q8`/`Q9`/`Q13` measured a dead
window and are discarded. Round 1's backdrop was also **buried at position 6** with the shipping
WPF product in between — the same foreign-topmost residue SP-111 recorded, produced unaided.

### Round 2 — fresh window per experiment, backdrop raised adjacently

```
[R1]  ghost (layered, never ULW'd): visible=True GLWA=False alpha=0 flags=0x0
[R1b] PrintWindow(PW_RENDERFULLCONTENT) on the GHOST: ok=True nonzero=0/14400
[R2]  fresh ULW: ok=True err=0
[R2b] PrintWindow(2) FIRST call after show: ok=True TL(a=0)=0x000000 TR(black,a=255)=0x000000
      BL(magenta,a=255)=0xFF00FF BR(white,a=128)=0x808080 nonzero=20000/40000
[R3]  while HIDDEN: pwOk=True BL=0xFF00FF nonzero=20000/40000
[R3b] after re-show WITHOUT another ULW: BL=0xFF00FF BR=0x808080 nonzero=20000/40000
[R4a] ULW(hdcSrc=NULL, flags=0, psize set): ok=False err=31
[R4b] ULW(all NULL but pptDst): ok=True err=0 rect=560,360 200x200
[R4c] SetWindowPos move: ok=True rect=580,380 content BL=0xFF00FF nonzero=20000/40000
[R4d] ULW move+content in ONE call: ok=True rect=500,300 200x200
[R5]  ULW SourceConstantAlpha=128 + AC_SRC_ALPHA: ok=True BL=0xFF00FF BR=0x808080
[R6]  ULW with a NON-premultiplied buffer (white @ a=64): ok=True p=0xFFFFFF
[R7]  z-order after adjacent raise: glyph@1 backdrop@2 of 21 visible
[R7b] intervening visible windows that INTERSECT the backdrop rect: NONE
[R7c glyph UP  ] margin=0xC8280A TL(a=0)=0xC8280A TR(black)=0x000000 BL(magenta)=0xFF00FF
                 BR(white a=128)=0xE49485
[R7d glyph DOWN] margin=0xC8280A TL(a=0)=0xC8280A TR(black)=0xC8280A BL(magenta)=0xC8280A
                 BR(white a=128)=0xC8280A
[R8]  click-through ON -> OTHER (the WPF product); OFF -> GLYPH; restored -> OTHER
[R9]  foreground is the glyph? False
[R10] a SECOND ULW window in the same process: ok=True BL=0xFF00FF
```

### Round 3 — the three questions the ghost argument turns on

```
[S1]  SLWA-mode window (THE OVERLAY'S SHAPE): ULW on it ok=False err=87; GLWA still=True alpha=153
[S2a] WS_EX_LAYERED cleared: GLWA=False alpha=0 flags=0x0
[S2b] WS_EX_LAYERED restored then ULW: ok=True err=0; GLWA now=False alpha=0 flags=0x0
[S3]  an all-TRANSPARENT frame: ULW ok=True PrintWindow ok=True nonzero=0/25600
[S4a] PrintWindow(2), no prior call: p=0x808080 (premultiplied 128)
[S4b] PrintWindow(2) AFTER a PrintWindow(0): p=0x808080
[S4c] PrintWindow(2) again: p=0x808080
[S5]  fully-opaque read-back exactness over 10 colours: mismatches=0/10
```

---

## 1. WHAT I CAN PROVE, AND WHERE IT STOPS

| # | Fact | Instrument | Measured |
|---|---|---|---|
| **G1** | the OS accepts a premultiplied BGRA surface for a window this process created layered, and refuses one for a window it did not | `UpdateLayeredWindow` | ok / err 87 (`Q1`, `Q3`) |
| **G2** | **the OS's own copy of the surface carries the frame** — every fully-OPAQUE sample reads back its colour EXACTLY, every fully-TRANSPARENT sample reads back 0 | `PrintWindow(PW_RENDERFULLCONTENT)` | `R2b`, `S5` 0/10 mismatches |
| **G2-control** | **a ghost cannot pass G2**: a layered window that never received ULW reads back **0 non-zero pixels of 14400** | same call | `R1b` |
| **G3** | the OS holds the rectangle, the ex-style, the topmost band and the hit test in both polarities, and never takes the foreground | `GetWindowRect`/`GetWindowLongPtr`/`GetTopWindow`/`WindowFromPoint`/`GetForegroundWindow` | `Q1b`, `R8`, `R9` |
| **G4** | **THE COMPOSITED DESKTOP distinguishes a glyph pixel from the background behind it, and a fully transparent pixel from an opaque black one** | `BitBlt(SRCCOPY\|CAPTUREBLT)` over a KNOWN backdrop | `R7c` vs `R7d` |
| **G5** | the composite is genuinely PER-PIXEL: a half-alpha sample reads exactly premultiplied source-over of the frame over the measured backdrop | same | `R7c` BR `0xE49485` = `128 + 200·127/255`, `128 + 40·127/255`, `128 + 10·127/255` = `(228,148,133)` |

**G4 is the packet's central trap, discharged by measurement.** One capture, one window, four points:

| point | glyph UP | glyph DOWN | what it settles |
|---|---|---|---|
| margin (backdrop only) | `0xC8280A` | `0xC8280A` | the capture is live and the backdrop is really on screen |
| alpha 0 | `0xC8280A` | `0xC8280A` | **a fully transparent pixel shows the background BEHIND it** |
| opaque BLACK | `0x000000` | `0xC8280A` | **opaque black is NOT transparent** — same window, same capture, different value |
| opaque magenta | `0xFF00FF` | `0xC8280A` | **a glyph pixel is distinguished from the background** |
| alpha 128 white | `0xE49485` | `0xC8280A` | the blend is per-pixel and arithmetically exact |

### Where the chain stops — stated now, not after the fact

1. **No human watched anything.** `watched-verified` is a manual gate and nothing here discharges it.
2. **`PrintWindow` is blind to the very distinction the packet names.** A fully transparent pixel
   and an opaque black pixel BOTH read `0x000000` (`R2b` TL and TR). The window read-back therefore
   proves the frame reached the OS; it can never prove transparency. Only the composited-desktop
   class (G4) separates them, and that class is machine-conditional.
3. **G4 is machine-conditional and the condition is MEASURED, never skipped.** A foreign topmost
   window can own the region, and one did in round 1 — the shipping WPF product, at z-order
   position 2..5 above my backdrop. The arbitration in §3 measures it and names the intruder.
4. **A window read-back is not a monitor** (SP-111's limit, inherited unchanged): `PrintWindow`
   answers about the copy the OS holds FOR THE WINDOW.
5. **Partial-alpha read-back exactness is NOT claimed by the product.** Measured stable at `0x808080`
   three times in round 3 — but round 1 read `0x404040` for the same frame after a `GetDC(hwnd)` +
   `BitBlt` on the same window. So the product anchors its exactness on alpha 255 and alpha 0 only,
   and never calls `GetDC` on its own surface. The partial-alpha value is asserted only in the
   desktop-composited class, where the arithmetic is exact and reproduced twice.
6. **Nothing measures cadence, order or timing.** Every frame advance is driven by hand on the
   injected clock.
7. **Linux is unproven** and refuses in type with a named manual gate.

---

## 2. HOW I AVOID SP-099's TOGGLE-THEN-ULW GHOST

SP-099's hazard, re-measured here three times (`Q3`/`Q3b`, `S1`, `S2`):

> `UpdateLayeredWindow` alone fails with **87**; toggling `WS_EX_LAYERED` alone is harmless;
> **toggle then ULW succeeds and the alpha read-back is gone forever.**

Four structural answers, in descending order of how hard they are to undo:

1. **This capability never touches a window it did not create.** `Glyph/**` owns its own window
   class, its own `CreateWindowExW` and its own handle. The overlay's handle is never obtained,
   never passed in, and never reachable: there is no constructor parameter, no factory argument and
   no P/Invoke in this packet that takes a foreign `hwnd`. `Overlay/**` is byte-identical to base.
2. **The two lines are never adjacent anywhere in the new code.** The window is created with
   `WS_EX_LAYERED` **in the `CreateWindowExW` call itself** and the style is never removed. The
   click-through flip writes `WS_EX_TRANSPARENT` only and re-asserts `WS_EX_LAYERED` in the same
   word — the shape `Win32OverlayPresence.ApplyClickThroughStyle` already uses.
3. **`SetLayeredWindowAttributes` is never called by this packet, on any window.** Measured reason
   (`Q7`, `Q7b`): calling it on a ULW window succeeds and then **permanently** refuses every later
   ULW with 87. It is a one-way door and this capability stays on its own side of it.
4. **And the door is barred from the other side too, by the OS.** `S1`: ULW on a window that only
   ever had `SetLayeredWindowAttributes` — the overlay's exact shape — returns **FALSE err 87**,
   and the overlay's `GetLayeredWindowAttributes` still reads 153. So even a hypothetical stray call
   could not silently convert an overlay surface; only SP-099's style toggle can, and (1) and (2)
   make that unreachable.

**The ghost check itself is REPLACED, not reused.** The overlay's ghost check is
`GetLayeredWindowAttributes` returning a non-zero `LWA_ALPHA`; for a ULW window that call returns
FALSE by design (`Q2`), so reusing it would either be a permanent refusal or a check that means
nothing. The replacement is **G2 + G2-control**: the frame is read back out of the OS and every
fully-opaque sample must equal its own colour, with a measured negative control showing a
never-composited layered window returns **zero** non-zero pixels. That is strictly stronger than the
overlay's check, which only proves the OS holds a NUMBER.

**And the frame itself must be provable.** `S3`: an all-transparent ULW frame reads back exactly
like the ghost. So `Paint` REFUSES a frame carrying no fully-opaque non-black pixel, with a typed
code, rather than claiming a composite it cannot distinguish from nothing.

---

## 3. COEXISTENCE — five surfaces, and the honest position

SP-113's review recorded that the four-disjoint-rectangles argument **does not scale to five**. It
scales even worse here, because **this capability's evidence REQUIRES an overlap**: proving a
transparent pixel shows the background behind it means putting the surface OVER a known background.
Disjointness is not merely insufficient; it is incompatible with the fact.

So I am not extending `PointerCoexistenceTests` and I am not copying a pair. I build the thing the
review named: **occlusion-aware arbitration**, in a new `GlyphCoexistenceTests` /
`GlyphSurfaceObservations`.

**The arbitration rule.** For any read-back point, ownership is not assumed from geometry; it is
DECIDED from the OS's own z-order plus rectangles:

- walk `GetTopWindow` → `GetWindow(GW_HWNDNEXT)`;
- the surface under test must appear before the intended background window;
- **every visible window strictly between them is fetched with `GetWindowRect` and tested for
  intersection with the sample rectangle** — if any intersects, the point is CONTESTED and the fact
  says so, naming the intruder's class and rectangle, rather than reading a pixel that belongs to
  somebody else.

Measured working (`R7b` = `NONE` after raising backdrop then glyph back to back) and measured
FAILING in round 1 (`Q12`: backdrop at 6, the shipping WPF product in between, and the sampled
"backdrop" pixels were the WPF app's). Both arms exist because both happened.

**What that gives, and what it does not.** It gives a rule that scales to N surfaces: the four
landed rectangles stay disjoint from each other and from mine, and the ONE deliberate overlap has
its ownership measured rather than assumed. It does **not** prove that five surfaces coexist under
contention, it does not prove any ordering the OS did not already report, and it does not remove the
foreign-topmost residue — it makes it NAMED instead of silent.

---

## 4. THE DOT DECISION

Seven meanings exist: clock (paced), screen (continuous), change (moving), custody (non-drawing),
reach (audio), demand (input), motion (video read-back). Bouncing Text needs an eighth or it reuses
one, and reuse must be argued rather than assumed.

**Decision: the eighth meaning is COMPOSITE — the OS's own copy of the surface still carries ink
that the surface did not simply paint opaque.** It is not "motion" (SP-111's seventh): a bouncing
logo that stopped moving is still a picture on the screen, whereas a video that stopped advancing is
a dead one. It is not "screen" (the second): a layered window can be present and composite nothing,
which is this packet's whole subject.

```
Live  =  the module is engaged
      && the surface is up
      && the OS's own read-back of the surface still holds the frame's opaque ink
```

The third clause is the one no other module has, and it is exactly the clause a ghost fails.

---

## 5. WHAT I WILL BUILD

**Product — new `client/src/CcpClient.Desktop/Glyph/`:** `GlyphBounds`/`GlyphSurfaceRequest`,
`GlyphFrame` (top-down 32bpp **premultiplied** BGRA, premultiplication ENFORCED because `R6` proves
the OS accepts a wrong buffer silently), `GlyphReasonCodes`, `IGlyphSurface`, `Win32GlyphInterop`,
`Win32GlyphSurface`, `UnsupportedGlyphSurface`, `GlyphSurfaceFactory` (+ Linux manual gate).

**Product — Bouncing Text.** D83 closes (per-pixel alpha exists now) and **D84 closes too**: a move
is `UpdateLayeredWindow` with position only (`R4b`), one call, no z-order walk, no style flip, no
hit test — which is precisely the "cheap overlay MOVE" D84 named as its closer.
`Effects/BouncingTextField.cs` (motion, WPF's own bounce arithmetic and 0.1 s stall clamp),
`Effects/GlyphTextSource.cs` (GDI+ into a premultiplied ARGB bitmap),
`Effects/BouncingTextSurfacePresenter.cs` (the cadence, on the surface — SP-106's rule),
`Effects/BouncingTextEffect.cs` (`OwnedSessionEffect`), `Session/BouncingTextPresetDocument.cs`,
rack row + panel in `Views/Pages/StudioPage.axaml`.

**Tests** in `client/tests/CcpClient.Tests/` (pure logic + real desktop, no Avalonia) and a small
number in `CcpClient.HeadlessTests` for the rack row.

**Docs:** divergences in `client/docs/wpf-surface-reachability.md`, the glyph evidence class in
`client/docs/verification-harness.md`.

**Not touched:** `Overlay/**`, `Input/**`, `Audio/**`, `Video/**`, `Pointer/**`,
`client/tests/floor/*`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`.
