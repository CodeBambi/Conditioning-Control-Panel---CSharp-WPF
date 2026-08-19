# SP-113 — plan checkpoint

Branch `lane/SP-113-pointer-surface`, base `431e424a`, worktree
`.claude/worktrees/agent-a09c43f867f64dd4a`. Nothing under `client/src` has been edited at the time
this file is written; this is the pre-product-edit checkpoint the packet's step 1 asks for.

---

## 1. SP-112's census, VERIFIED against the code rather than trusted

| SP-112's claim | my read | verdict |
|---|---|---|
| `Win32InputPresence.WindowProc` handles five messages and **no mouse message** | `Win32InputPresence.cs:818-877`: `WmPaint`, `WmKeydown`, `WmSyskeydown`, `WmChar`, `WmClose`, then `DefWindowProcW`. Exactly five cases. | **holds** |
| `Win32InputInterop` declares no mouse message | `Win32InputInterop.cs:54-58` declares `WmPaint 0x000F`, `WmKeydown 0x0100`, `WmSyskeydown 0x0104`, `WmChar 0x0102`, `WmClose 0x0010`. No `WM_LBUTTON*`, no `WM_MOUSEACTIVATE`, no `WM_NCHITTEST`. | **holds** |
| one `nint _window` | `Win32InputPresence.cs:73` `private nint _window;` — the only window field; `:120/385/429/501/623/953/993` all read that one. | **holds** |
| **one `SetWindowPos` site — no move seam** | one call in the whole folder, `Win32InputPresence.cs:206`, inside `Prompt`, with the failure text at `:212`. Nothing moves a placed card. | **holds** |
| `Confirmed` requires `IsForegroundWindow && SystemKeyboardFocusIsThisWindow` | `IInputPresence.cs:182-184`. Also `WindowExists && WindowVisible && AboveEveryOrdinaryWindow && HitTestRoutesHere && Asked`. | **holds** |

**The census is correct in every particular. A pointer capability is genuinely a second capability.**

### The one citation that is NOT what the packet says it is

The packet writes: *"upstream uses `ShowActivated = false` and `WM_MOUSEACTIVATE → MA_NOACTIVATE`
(`BubbleCountWindow.xaml.cs:1823-1824`)"*.

- `Windows/BubbleCountWindow.xaml.cs:1823` is `private const int WM_MOUSEACTIVATE = 0x0021;` and
  `:1824` is `private const int MA_NOACTIVATE = 3;`, answered by `NoClickRaiseHook` at `:1831-1839`.
  The citation is **exact** — but it is the **Bubble COUNT** window (SP-112's module), not Bubble
  Pop's.
- **Bubble Pop's own bubble windows never answer `WM_MOUSEACTIVATE` at all.** `BubbleService.cs`
  contains no `WM_MOUSEACTIVATE` and no `MA_NOACTIVATE`. Its non-activation is a STYLE:
  `HideFromAltTab` rebuilds the ex-style as `exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`
  (`Services/BubbleService.cs:4887`, constant at `:4899`), every reposition passes `SWP_NOACTIVATE`
  (`:4785`, `:4807`), and the window itself is `ShowActivated = false` (`:2158`).
- `IsHitTestVisible = _isClickable` at `:2960`, `:2988`, `:3103` and `MouseLeftButtonDown` at
  `:2966`, `:3018`, `:3113` are exact, as SP-110 §1 recorded.

**Resolution: I port Bubble Pop's own mechanism (`WS_EX_NOACTIVATE`) AND answer
`WM_MOUSEACTIVATE → MA_NOACTIVATE`, and I say why both.** `WS_EX_NOACTIVATE` is upstream Bubble
Pop's; the message answer is upstream's own belt-and-braces for the same outcome on a sibling
surface, and — this is the operative reason — **the message answer is the only one of the two that
can be OBSERVED at the moment of a click**, which is precisely the fact the packet asks for. A style
bit is a configuration read-back; a `WM_MOUSEACTIVATE` answered `MA_NOACTIVATE` during a real click
is the window procedure refusing activation with a click in its hand.

**Second discrepancy, minor:** the packet's trap 3 says *"Eight exist: clock, screen, change,
custody, reach, demand, motion"* — that list is **seven** names, and the same paragraph then says
*"not a licence for an eighth"*. SP-112 §4 says "the seven landed meanings" over the identical list.
Seven is right; the word "Eight" in the first sentence is a slip. Recorded, not acted on.

---

## 2. WHAT I CAN PROVE ABOUT A CLICK REACHING A NON-ACTIVATING WINDOW, AND WHAT I CANNOT

SP-099 proved click-THROUGH. SP-110 proved a window TAKES the keyboard, and its chain leaned on
`GetForegroundWindow` and `GetGUIThreadInfo(0)`. **Neither is available to me**: a bubble that took
the foreground would be a bug, so every link SP-110 leaned on is one I must assert the NEGATION of.

### The chain the PRODUCT earns (`Available` rests on exactly this, nothing more)

| # | fact | API |
|---|---|---|
| **P1** | the target's window exists, is visible, and the OS holds the exact rectangle asked for | `IsWindow` / `IsWindowVisible` / `GetWindowRect` |
| **P2** | the OS holds `WS_EX_NOACTIVATE` **and not** `WS_EX_TRANSPARENT` for it — upstream's own two style bits (`BubbleService.cs:4887`, `:4891-4892`) read back out of the OS rather than remembered | `GetWindowLongPtrW(GWL_EXSTYLE)` |
| **P3** | the OS's own z-order walk puts it above every ORDINARY window | `GetTopWindow` + `GetWindow` walk |
| **P4** | **the window manager's hit test at the target's own centre returns THAT target's window** — the inverse of SP-099, per target rather than per surface | `WindowFromPoint` |
| **P5** | **the foreground did NOT change across the operation, and is not this window** | `GetForegroundWindow()` sampled before and after |
| **P6** | the OS holds ink in the target's own client area, differentially: a control margin reads back EXACTLY the fill colour and the disc band differs (SP-110's M-t lesson — an unpainted window's DC differs from the background just as reliably as paint does) | `GetDC` + `GetPixel` |

**The product never synthesises input.** `SendInput` is the harness's, as at SP-110. So the
product's `Available` means *"the OS will route a click here, and this window is configured and
currently positioned so that doing so takes nothing from anybody"* — a routing-and-configuration
claim, never a delivery claim.

### The chain the HARNESS adds, and it is the fact worth having

| # | fact | instrument |
|---|---|---|
| **T1** | a click synthesised at OS level at the target's own centre arrives as `WM_LBUTTONDOWN` **and** `WM_LBUTTONUP` in **that target's own window procedure** | `SendInput(MOUSEEVENTF_ABSOLUTE\|MOVE\|LEFTDOWN\|LEFTUP)` |
| **T2** | `WM_MOUSEACTIVATE` arrived at that procedure during the click and was answered `MA_NOACTIVATE` | counters inside the product's own `WindowProc` |
| **T3** | **`GetForegroundWindow()` is byte-identical before and after the click, and is not the target** | `GetForegroundWindow` |
| **T4** | the module's own press callback fired naming the handle the OS chose — with two targets up, the OS decides which, not our arithmetic | product callback |
| **T5** | negative control: with that target closed, the same synthesised click at the same point delivers **nothing** to it, and the count does not move | same |

**T1+T2+T3 together are "a click that arrived while the foreground was unchanged".**

### WHERE THE CHAIN STOPS — six places, stated before I write the code

1. **No human clicked anything.** Every press in the suite is `SendInput`. `popped-verified` is a
   named manual gate and no automated step on any platform discharges it, Windows included.
2. **Injected input is not physical input.** UIPI refuses injection into a higher-integrity window,
   the secure desktop takes it away, a locked workstation swallows it. The fixture DETECTS those
   (`SendInput` returns 0) and the expectation flips with the machine — never a skip.
3. **`Available` cannot include T2.** `WM_MOUSEACTIVATE` only exists when a click really happens, so
   the product can only claim the STYLE (P2) plus "the foreground is not me and did not move" (P5).
   *"This window will never activate"* is not provable without clicking it, and the product must not
   click it. This is the single sharpest stopping point and it is why P2 and P5 are separate links.
4. **The routing answer is momentary.** Any process can cover the point the instant after P4. Every
   claim is "what the OS said when it was asked", never "what it will keep saying".
5. **Nothing proves Avalonia's own message loop delivers mouse messages for this window.** All
   evidence rides the surface's own bounded `Pump`, exactly SP-110's L5 residue.
6. **No headed capture.** `presentation-verified` is untouched: the ink read-back is an OS query
   about pixels the OS holds for a window, not a photograph, and says nothing about whether a bubble
   is visible, aimable, or on a screen anybody is looking at.

---

## 3. THE MOVING-TARGET RACE — measured, bounded, and mostly DESIGNED OUT

SP-110 named it: *"the hit test's answer is a function of a position that changes between asking and
clicking."*

### 3a. The product has no ask-then-click gap at all, and that is a design consequence

Upstream's shared-host path really does have the race: a global mouse hook reads an immutable
`ChaosClickDiscsSnapshot` rebuilt once per UI tick and decides in USER SPACE which bubble was hit
(`BubbleService.cs` primer §4c/§4e, gotcha 4). One tick of staleness sits between the snapshot and
the click.

**The port gives each target its own non-activating window, so the arbiter at click time is the
window manager, at the instant of the click, over the position the OS itself holds.** Nothing in the
product hit-tests and then acts on the answer. `WindowFromPoint` appears only in the CONFIRMATION
path — earning `Available` — and never in the pop decision. That is upstream's own per-window path
(`MouseLeftButtonDown` on the bubble's own window, `:3113`), whose cap is 3 (`MAX_BUBBLES`, `:25`)
for exactly the reason it is a `SetWindowPos` per move; I take the cap with the mechanism.

### 3b. The residue, and its arithmetic bound from upstream's own constants

What remains is that `Open`/`Move` return `Available` on a P4 answer that can be up to **one
animation step** old.

- step: `STEP_MS = 30` ms (`BubbleService.cs:53`).
- vertical: `_posY -= _speed` with `_speed = 1.0 + rand*1.0` DIP/step (`:2825`), multiplied by up to
  6 by the dashboard speed slider, 0..500 % (`:2831-2836`) → **≤ 12 DIP/step**.
- horizontal: `_posX = _startX + offset` (`:3496`) with `offset` one of four wobbles (`:3458-3464`)
  over `_timeAlive += 0.02` per step (`:3399`). The largest per-step derivative is case 1,
  `30·sin(7.5·t)` → `30 × 7.5 × 0.02` = **4.5 DIP/step**. (Case 3 gives `(30×3 + 15×6)×0.02 = 3.6`;
  case 0 `3.0`; case 2 `2.7`.)
- worst-case step displacement `sqrt(12² + 4.5²)` = **12.81 DIP**; unboosted, `sqrt(2² + 4.5²)` =
  **4.92 DIP**.
- smallest legal radius: `BubbleSizing.ClickableFloorDip = 60` DIP (`Services/BubbleSizing.cs:70`) →
  **30 DIP**. The ordinary band is 150..250 DIP (`:41`, `:48`) → 75..125 DIP.

**Bound: one step's displacement (≤ 12.81 DIP) is strictly less than the smallest radius any legal
bubble can have (30 DIP), by a factor of 2.34.** So a point the OS answered with a target at step N
is still inside that same target's window at step N+1. **I will PIN this as arithmetic over the
module's own constants**, so a later change to speed, boost or the size floor that breaks the
inequality reddens.

### 3c. And I will MEASURE it rather than only computing it

Three facts, all with real windows and real `SendInput`:

1. **hit test → move by one worst-case step → click at the ORIGINAL point** ⇒ the click still
   arrives at that same target. The bound, exercised.
2. **hit test → move by more than a full diameter → click at the original point** ⇒ the click does
   NOT arrive at it. Without this the first fact is also true of a window that never moved, which is
   the SP-099 fake in a new costume.
3. the arithmetic pin of 3b, so the two measured points stay attached to the product's constants.

### 3d. Do I freeze motion, and what does it cost

**Yes, for the DELIVERY facts (T1/T2/T3), and I say so plainly.** Between `SendInput` and the pump
that receives the message the harness does not step the field. What that buys is a deterministic
delivery fact. What it costs is exactly this: **nothing measured proves delivery to a target that
moved DURING the flight of a click.** 3c-1 covers the ask/act gap either side of a move, and does
not cover a move interleaved with the OS's own delivery. That gap is a property of the machine's
input timing, no fixture can pin it without a wall-clock wait (banned), and it is named rather than
papered over.

---

## 4. THE DOT — DEMAND is reused, and the instrument is what changes

Seven meanings exist: clock, screen, change, custody, reach, demand, motion (SP-112 §4).

**DEMAND fits, and the finding is that DEMAND was never "foreground".** SP-110 defined it as *"a
claim on the user's ATTENTION, which the operating system grants and can revoke without this process
doing anything at all"* — foreground+focus was its INSTRUMENT, not its meaning. A bubble the OS will
route a click to is a claim on attention granted by the OS, contested by every other topmost window,
and revocable without this process acting: something covers the point and the bubble becomes
decoration — on screen, un-poppable. That is the same harm class as "nobody is being asked", so it is
the same meaning with a different instrument (the hit test, not the foreground).

```
Live = a firing is on the clock
    && the OS says this process can put a pointer target on a display   (station channel)
    && ( no target of MINE is up  OR  the OS routes a click at its own point TO it )
```

The "of MINE" qualifier is SP-112's F5 lesson. My capability is **keyed from birth** — targets are
handles, and every observation is per handle — so unlike `IVideoSurface`/`IInputPresence` it knows
whose target it holds. That is SP-109's audio answer applied at design time instead of at the second
consumer.

**No eighth meaning is owed.** If it turns out the third clause cannot be stated without a new fact
about the world, that is a finding about DEMAND and it goes in the record as one.

---

## 5. SHAPE OF THE WORK

**New capability `client/src/CcpClient.Desktop/Pointer/`:** `IPointerSurface.cs` (interface +
`PointerBounds`, `PointerTargetRequest`, `PointerTargetObservation`, `PointerStationObservation`,
`PointerPress`), `PointerReasonCodes.cs`, `Win32PointerInterop.cs`, `Win32PointerSurface.cs`,
`UnsupportedPointerSurface.cs`, `PointerSurfaceFactory.cs` (Linux refuses typed with an X11/Wayland
gate naming `XShapeInput`, `_NET_WM_STATE_ABOVE`, and the fact that pointer *delivery* on Wayland is
compositor-mediated).

**Module (`Effects/`, `Session/`, `Views/`):** `BubblePopField.cs` (pure arithmetic — spawn cadence,
FloatUp, wobble, pop animation, caps), `BubblePopEffect.cs` (`OwnedSessionEffect`: continuous, as
upstream's `Start`/`Stop` is), `Session/BubblePopPresetDocument.cs`,
`Views/Pages/PointerPanelNotices.cs`, a rack row + panel in `StudioPage.axaml(.cs)`.

**Tests (`client/tests/CcpClient.Tests/`):** `PointerWindowProbe.cs` (mouse `SendInput`, hit test,
z-order, ex-style, foreground — a NEW instrument; `InputWindowProbe` is not modified),
`PointerSurfaceObservations.cs` (the real-desktop runs), `PointerCapabilityTests.cs` (the chain),
`PointerCoexistenceTests.cs` (overlay + Lock Card + video surface all survive),
`BubblePopModuleTests.cs`. Rack lists in the spine suites and `StudioRackHeadlessTests` grow by one.

**Docs:** `client/docs/wpf-surface-reachability.md` §SP-113 divergences; `verification-harness.md`
the pointer evidence class (`pointer-routed`, `click-delivered`, `popped-verified`).

`Overlay/**`, `Input/**`, `Audio/**`, `Video/**`, `client/tests/floor/floor.json`,
`client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**` are not edited.
