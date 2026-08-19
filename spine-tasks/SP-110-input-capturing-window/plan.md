# SP-110 — plan (checkpoint 1, written before the first product edit)

Branch `lane/SP-110-input-capturing-window`, base `b8187eb4`, worktree
`.claude/worktrees/agent-a76cfcd88d7331635`.

---

## 0. MEASURED FIRST, DESIGNED SECOND — and the measurement found the trap

SP-109's discipline was to write the independent probe and read the operating system back at every
step *before* the first product edit. I did the same. A throwaway net10.0 console probe
(scratchpad, outside the repo) built a raw Win32 window with its own P/Invokes and asked the OS
nine questions. **Raw output, run 1:**

```
OS: Microsoft Windows 10.0.26200  monitors=1  primary=1646x1029
F0 station: handle=0xEC query=True flags=0x1 visible=True
F0 desktop: handle=0xF0 nameQuery=True name="Default"
F1 window: exists=True visible=True rect=(613,384,420,260) z=1/21 firstOrdinary=7 aboveOrdinary=True
F2 foreground: SetForegroundWindow returned False; GetForegroundWindow=0x130938 ours=0x1F306A6 isForeground=False
F3 focus: GetGUIThreadInfo hwndFocus=0x1F306A6 hwndActive=0x1F306A6 query=True focusIsOurs=True
F4 hit test: WindowFromPoint(823,514)=0x1F306A6 ours=True winnerClass="CcpInputProbePrompt..."
F5 SendInput(VK_F13): injected=True pumpIterations=400 keydownF13=0 keydownOther=0
F5b SendInput(VK_A): injected=True pumpIterations=400 chars=0 lastChar=' '
F5c negative control: ... promptKeydownBefore=0 promptKeydownAfter=0 delivered=False
F6 ink: DrawTextW height=48 background=0x201020 sampled=7600 nonBackground=161
F7 overlay before/during/after: passesThrough=True aboveOrdinary=True alpha=160 transparent=True
F7 overlay differential: with WS_EX_TRANSPARENT cleared hit=0x1F506A6 isOverlay=True
RESULT: capture chain INCOMPLETE
```

### THE TRAP, found by measuring rather than by reasoning

Look at **F3 against F2 in run 1**. `SetForegroundWindow` **failed** — the foreground belonged to
another process the whole time — and **no injected keystroke arrived** (F5, F5b: 0 and 0). And yet
`GetGUIThreadInfo(ourThreadId).hwndFocus == our hwnd` answered **TRUE**.

> **`GetGUIThreadInfo(thread).hwndFocus` is THREAD-LOCAL. It is the focus inside our own thread's
> input queue, and it is set for a window nobody can type into.** A capability that asserted it
> would have been the exact inverse-SP-099 fake the packet names: an OS API, correctly called,
> answering yes about a window that receives nothing.

The system-wide fact is `GetGUIThreadInfo(**0**)` — documented as "the foreground thread" — and in
run 1 it answered the *other* application's window. That is the API this capability will use, and
the difference between the two is pinned by a fact.

### Run 2 — the escalation ladder measured one rung at a time

```
E0 plain SetForegroundWindow=False foreground=0x130938 systemFocus=0x130938 ours=0x4E208B8
E1 escalation attempts=plain=False | attach=True thenSetForeground=True foreground=0x4E208B8
   foreground=0x4E208B8 systemFocus=0x4E208B8 threadFocus=0x4E208B8
F1 window: exists=True visible=True rect=(613,384,420,260) z=1/20 firstOrdinary=6 aboveOrdinary=True
F2 foreground: GetForegroundWindow=0x4E208B8 ours=0x4E208B8 isForeground=True
F3  focus(thread): hwndFocus=0x4E208B8 focusIsOurs=True
F3b focus(SYSTEM, GetGUIThreadInfo(0)): hwndFocus=0x4E208B8 focusIsOurs=True
F4 hit test: WindowFromPoint(823,514)=0x4E208B8 ours=True
F5  SendInput(VK_F13): injected=True pumpIterations=1 keydownF13=1 keydownOther=0
F5b SendInput(VK_A):   injected=True pumpIterations=1 chars=1 lastChar='a'
F5c negative control: thief=0x12096E foregroundNow=0x12096E focusNow=0x12096E
    promptKeydownBefore=1 promptKeydownAfter=1 delivered=False
F6 ink: DrawTextW height=48 background=0x201020 sampled=7600 nonBackground=161
F7 overlay before/during/after: passesThrough=True aboveOrdinary=True alpha=160 transparent=True
F7 overlay differential: with WS_EX_TRANSPARENT cleared hit=0x4E408B8 isOverlay=True
RESULT: capture chain measured
```

`AttachThreadInput(ourThread, foregroundThread, TRUE)` then `SetForegroundWindow` **returns TRUE**
and the OS hands over foreground **and** system keyboard focus. With that in place a synthesised
`VK_F13` reaches the window procedure on the **first** pump iteration, and `VK_A` arrives as a
translated `WM_CHAR 'a'`. With the foreground given to a scratch thief window, the same injection
does **not** arrive (count stays 1). **The differential closes in both directions.**

---

## 1. WHICH ROW — Lock Card, and the survey verified row by row

SP-108 §7 names three rows blocked on an input-capturing window. I checked all three against
`ConditioningControlPanel/` rather than trusting the table.

| Row | Survey's claim | Verified | Verdict |
|---|---|---|---|
| **Bubble Pop** | "4918 lines of spawn timer plus per-bubble `DispatcherTimer` hops (`BubbleService.cs:192,392,430,796,1779`) driving clickable moving windows" | `wc -l` = **4918** exactly. `:189-194` is the spawn `DispatcherTimer`; `:391-396`, `:429-434`, `:1778-1783` are per-target `DispatcherTimer` hop chains; `:794-799` re-paces the spawn timer. Clickability is real and per-bubble: `IsHitTestVisible = _isClickable` (`:2960`, `:2988`, `:3103`) with `MouseLeftButtonDown` handlers (`:2966`, `:3018`, `:3113`) | **claim holds.** Needs mouse hits on MOVING windows — D84's cost class on top of the input need |
| **Bubble Count** | "needs video playback and interactive message windows (`BubbleCountService.cs:30-39`)" | The class summary at `:18-21` is "plays a video with bubbles to count, then asks for the total"; `_videosPath` at `:29`, `_regularVideos`/`_packVideos` at `:31-33`, `List<Window> _messageWindows` at `:38` | **claim holds**, with a one-line citation drift: the video field starts at `:29`, not `:30`. Needs a video capability the port has no seam for |
| **Lock Card** | "`LockCardWindow.ShowOnAllMonitors(phrase, repeats, strict, isTest, voice)` (`LockCardService.cs:299`) — an input-capturing modal on every monitor" | Exact call at `:299`. `LockCardWindow.xaml.cs:1496` is the method; the input owner takes the foreground at `:594-601` (`SetWindowPos(HWND_TOPMOST)` → `SetForegroundWindow` → `Activate()` → `FocusInput()`) | **claim holds** |

**Lock Card is chosen** because its input need is the *simplest to prove*, and simplest is a
property of the OS question, not of the feature:

* it needs **one static window** holding **keyboard focus**. That is `GetForegroundWindow`,
  `GetGUIThreadInfo(0).hwndFocus` and one `WindowFromPoint` — three questions with an unambiguous
  answer each, and a synthesised keystroke to close the chain.
* Bubble Pop needs the same facts **per bubble, per frame, on windows that move** — the hit test's
  answer is a function of a position that changes between asking and clicking, so the fact is a race
  by construction. Plus D84.
* Bubble Count needs a video capability first; its input half is a message box on the far side of a
  capability the port does not have at all.

It is also PACED (`LockCardService.cs:127-134`), so `PacedSessionEffect<TFiring>` fits and the
packet's novelty lands entirely on the capability instead of on the spine.

**Survey correction (one, minor):** Bubble Count's video evidence begins at `BubbleCountService.cs:29`,
not `:30`. Nothing else in §7's three rows is wrong.

---

## 2. WHAT I CAN PROVE FROM THE OS, AND WHAT I CANNOT

### The chain I will ship, five links

| # | Fact | API | Measured |
|---|---|---|---|
| **L1** | the OS holds this window, visible, at the exact rectangle, above every ordinary window in its own z-order | `IsWindow` / `IsWindowVisible` / `GetWindowRect` / `GetTopWindow`+`GetWindow` walk | z=1 of 20, first ordinary at 6 |
| **L2** | **the OS reports THIS window as the foreground window** | `GetForegroundWindow()` | `0x4E208B8 == ours` |
| **L3** | **the OS reports THIS window as the keyboard focus of the FOREGROUND thread** — the system-wide fact, not the thread-local one that answered yes with nothing arriving | `GetGUIThreadInfo(0).hwndFocus` | ours (run 2); the *other app's* (run 1) |
| **L4** | the window manager's hit test at an interior point returns THIS window — pointer routing, the exact inverse of SP-099 | `WindowFromPoint` | ours |
| **L5** | **a keystroke synthesised at OS level reaches this window's window procedure**, and does NOT when another window holds the foreground | `SendInput` + the window's own `WndProc` | `VK_F13` → 1 keydown; `VK_A` → `WM_CHAR 'a'`; thief holding foreground → 0 |

L5 is the **test instrument's**, never the product's: nothing shipped injects input.

### WHERE IT STOPS — named now, not after the fact

1. **No human pressed anything.** Every keystroke in the suite is `SendInput`, which enters the
   system input stream *below* the driver and *above* the hardware. `input-delivered` is where this
   stops; `a person answered` is a manual gate and no automated step discharges it.
2. **Injected input is not physical input.** UIPI blocks injection into a higher-integrity window,
   the secure desktop takes it away entirely, and a locked workstation refuses it silently. The suite
   detects those (SendInput returns 0, or the foreground grab fails) and the honest expectation flips
   — never a skip.
3. **The foreground is granted, not owned.** `SetForegroundWindow` alone FAILED here (measured, F2
   run 1). Any other process can take it back the instant after we ask, and the capability can only
   report what the OS said at the moment it asked.
4. **Nothing proves the card is READABLE.** The ink read-back (F6: 161 non-background pixels of 7600
   sampled, from the window's own DC) proves the OS holds ink for the window, not that the phrase is
   legible, correctly laid out, unclipped or on a screen anybody is looking at. That is
   `presentation-verified` and undischarged.
5. **Nothing proves the host's own loop translates keystrokes.** The product's pump is Avalonia's;
   the evidence here is taken on this presence's own bounded pump. Named as an unproven coupling.
6. **Linux is unproven** and refuses in type with a manual gate.

---

## 3. THE DOT — decided: **`Live`, and it owes a sixth meaning**

> **Is the module `Live` while the prompt is up and unanswered? YES — but only because the OS says
> the question is in front of the user.**

Waiting for a human *is* the work; a module that went `Armed` the moment it did its job would be an
under-claim of exactly the kind SP-109 refused for the audio half-row. But the existing paced rule
(`WorkIsRunning => ScheduleArmed`) is not enough, and the reason is measured above: a card can be up,
visible, topmost — and the OS can have the keyboard somewhere else entirely (run 1). A dot lit there
claims a question nobody is being asked.

```
Live  =  a firing is on the clock
      &&  the OS says this process can put a window in front of a user   (window station + display)
      &&  ( nothing is being asked  OR  the OS says the prompt window holds foreground + focus )
```

**The sixth meaning is DEMAND: a claim on the user's ATTENTION, which the operating system grants
and can revoke without this process doing anything.** The five before it were the clock (paced), the
screen (continuous), change (moving), custody (non-drawing) and reach (audio). Reach is the closest
— it too asks about a resource the process does not own — but reach is about an *output channel*
this process opens and holds. The foreground is not held: it is *lent*, contested by every other
process on the desktop, and revoked by a click on another window. That is a different fact.

All three clauses are separately pinnable and each will be mutated:
* drop clause 1 → the dot lights with no firing on the clock;
* drop clause 2 → the dot lights on Linux, where nothing can ever be asked (the Pink Filter failure);
* drop clause 3 → the dot lights over a card the OS has taken the keyboard away from;
* replace clause 3 with plain `HoldsTheInput` → the dot goes dark for the 99 % of a session with no
  card up, which is the opposite lie.

---

## 4. THE OVERLAY (trap 1) — how it is proven unharmed

`Overlay/**` is not edited. It is CONSUMED: the coexistence fact builds a real
`Win32OverlayPresence`, presents a click-through surface, and then measures **three times** — before
the prompt window exists, while it holds the foreground, and after it is dismissed and disposed:

* the window manager routes the overlay's own centre point **past** it (`WindowFromPoint != overlay`);
* the OS's z-order walk still puts the overlay above every ordinary window;
* the OS still holds its `LWA_ALPHA`, and `WS_EX_TRANSPARENT` is still set;
* the overlay never becomes the foreground window;
* and the differential that stops all of the above from being vacuous: with `WS_EX_TRANSPARENT`
  cleared, the same point routes **to** the overlay.

Measured in the probe already (F7): unchanged across all three phases, and the differential closes.
The prompt window is placed at a rectangle **disjoint** from the overlay's, so the overlay's hit-test
point is never occluded by the thing under test. `OverlayCapabilityTests` and `OverlayObservations`
are not touched.

---

## 5. SHAPE OF THE WORK

**New — `client/src/CcpClient.Desktop/Input/`**
`IInputPresence.cs` (interface + `InputPromptRequest`/`InputPromptContent`/`InputKeystroke` +
`InputCaptureObservation`/`InputStationObservation`), `InputReasonCodes.cs`, `Win32InputInterop.cs`,
`Win32InputPresence.cs`, `UnsupportedInputPresence.cs`, `InputPresenceFactory.cs` (with
`LinuxManualGate`).

**Effects:** `LockCardEffect.cs`, `LockCardSchedule.cs` (WPF `:127-134`, `:159-168`),
`LockCardPhrasePool.cs` (WPF `PickPhrase` `:324-346`), `LockCardTyping.cs` (WPF `HasTypedEnough`
`:272`, prefix mismatch `:727-733`, match `:740`, `EscClosesCard` `:632`).

**Session:** `LockCardPresetDocument.cs`, three additive `EffectReasonCodes`, `SessionParticipant`
wiring (rack order: GAMES & CARDS sits between EFFECTS and IMMERSION —
`StudioTabView.xaml.cs:498-506`; `StartEngine` starts it at `MainWindow.StartStop.cs:206-209`, after
the overlay pair and before Mind Wipe).

**Views:** the rack's third group header, the Lock Card row + dot, and its panel.

**Tests (unit):** `InputWindowProbe.cs` (independent P/Invokes + its own negative control),
`InputCaptureObservations.cs`, `InputCapabilityTests.cs`, `InputOverlayCoexistenceTests.cs`,
`LockCardEffectTests.cs`; `RealDesktopCollectionGuardTests`' helper census extended so the new probe
files cannot join the suite unleased. **(headless):** rack row, dot and panel facts.

**Divergences to record in `wpf-surface-reachability.md`:** no multi-monitor cover and no fullscreen
(one centred card on the primary display); no voice-solve mode (no mic capability); no phrase editor
(the JSON document is the editor, as the clip folder is for audio); no interaction queue (upstream's
own `DropNoQueue` answer applies verbatim); the phrase is not logged (WPF logs it at `:303`); and
**the escalation itself** — the port asks `AttachThreadInput` for the foreground, which WPF does not,
because WPF compensates with a `Deactivated` re-grab loop (`LockCardWindow.xaml.cs:160-176`) and this
port must EARN the claim or refuse it.

**Safety note that belongs in the plan, not only in the record:** the port's card is **always**
escapable with Esc. Upstream's rule is `EscClosesCard => !strict || !PanicEscapeIsLive`
(`:632`, `:621-622`) — "every failure mode here has to fall open, not shut" — and the port has no
panic-key hook at all, so the rule evaluates to *always closes*. That is upstream's own rule applied
to the port's facts, not a weakening of strict mode, and it is why this packet cannot ship an
inescapable window.

## 6. Floor

Pin **1589 unit / 100 headless**. The delta is declared in
`spine-tasks/SP-110-input-capturing-window/floor-delta.json`; `client/tests/floor/floor.json` is
never opened.
