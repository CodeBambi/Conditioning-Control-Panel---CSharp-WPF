# SP-112 — record

Branch `lane/SP-112-second-consumer`, base `7a35f6d8`.
Floor: pin **1742 unit / 107 headless**; observed and declared numbers are in §9 and in
`floor-delta.json`. Build: 0 errors, 0 warnings. `client/tests/floor/floor.json` was never opened.

> **This document's real subject is not the module.** It is the answer to one question asked of two
> capabilities that had exactly one consumer each: **did they fit, or did I have to reach around
> them?** §2 is that answer. The module is the instrument.

---

## 1. WHICH MODULE — Bubble Count, and Bubble Pop was refused with a census rather than with "too big"

The packet named Bubble Pop as the harder stress (per-bubble hit tests on windows that MOVE) and
invited an evidence-backed refusal. **I read `Input/**` before choosing, and the answer is not
"hard": it is "absent".**

| what Bubble Pop needs | what the input capability has |
|---|---|
| a MOUSE click delivered from a window | `Win32InputPresence.WindowProc` (`:818-877`) handles `WM_PAINT`, `WM_KEYDOWN`, `WM_SYSKEYDOWN`, `WM_CHAR`, `WM_CLOSE`. `Win32InputInterop` (`:54-58`) declares those five message constants and **no mouse message at all**; the only `Mouse`-shaped token in the folder is `WndClassExW.hCursor` |
| a hit test per bubble | `InputCaptureObservation.HitTestWinner` is read at ONE point — the single card's centre |
| MANY windows at once | `Win32InputPresence` owns one `nint _window`, one `_content`, one `_onKeystroke` |
| windows that MOVE while visible | `Prompt` places once; there is no move seam |
| windows that must NOT take the foreground — upstream's bubbles are `ShowActivated=false` with `WM_MOUSEACTIVATE` answering `MA_NOACTIVATE` (`Windows/BubbleCountWindow.xaml.cs:1822-1830`, `Services/BubbleService.cs:2960/2988/3103`) | `Prompt` ESCALATES for the foreground (D115) and `Confirmed` **requires** `IsForegroundWindow && SystemKeyboardFocusIsThisWindow` |

Consuming this capability for Bubble Pop would mean adding a pointer route, a multi-window
collection, a non-activating window class, a move seam, and a **second, inverted definition of
`Available`**. That is not a second consumer; it is a second capability inside `Input/**`.

**Bubble Count instead, because it stresses both capabilities where they claim to be general:** it
is the first consumer to hand the video capability a picture this process **composed**; the first to
ask the input capability for something other than a phrase; the first module to use **two**
capabilities in one firing, in the order SP-111's coexistence run never took (video up, video down,
then a card that must take the foreground); and the first whose OUTCOME depends on the capability's
own answer — the number the user is asked for is the number of bubbles drawn into pictures the
operating system confirmed it was holding.

`BubbleCountService.cs:30` is `private string _videosPath = "";` and `:29` is `private bool
_isBusy;`. The packet's citation is exact and is not re-broken.

---

## 2. THE CAPABILITY VERDICT — the packet's actual output

### 2.1 What I called that ALREADY EXISTED, and fitted with nothing added

| capability | what I used | verdict |
|---|---|---|
| video | `IVideoSurface.Begin/End/Showing/Running/CanReachADisplay/LastPlacement` | **fitted** |
| video | `IVideoPresence.Show(VideoFrame)` accepting a picture this process COMPOSED rather than the decoder's own | **fitted, and this was the sharpest test.** `Show` letterboxes, blits and then compares the OS's read-back against *the frame it was handed* — so painting bubbles into that frame is proved by the capability's existing differential with no new evidence and no new API |
| video | `VideoFrame.Pixels` / `.Stride` / `.Width` / `.Height`, and the fact that `MediaFoundationClipSource.CopyOut` allocates a fresh buffer per frame (`:375`) | **fitted** — bubbles are painted in place, no copy |
| video | `VideoClipPool` (a second instance over the same folder) | **fitted** |
| input | `IInputPresence.Prompt/Update/Dismiss/IsPrompting/HoldsTheInput/CanReachAUser/LastPrompt/ObserveStation` | **fitted** |
| input | `InputKeystrokeKind.Character` for digits, `.Backspace`, `.Cancel` for Escape | **fitted** |
| input | **Enter**, which the capability does not name: it arrives as `.Key` with `VirtualKey == 0x0D` | **fitted without growing it** — see 2.3 |
| both | the typed `CapabilityState` vocabulary, carried verbatim into the panel | **fitted** |
| overlay | `OverlayDisplays.Enumerate()` for placement | **fitted** (consumed, never edited) |

### 2.2 What was NOT there, and what I added — with the first-consumer justification for each

Two additions, both in `Effects/**` except one reason-code constant in `Video/**`. **Nothing at all
was added to `Input/**`.**

**(a) `IVideoFramePainter` — a per-frame paint seam on `VideoSurfacePresenter` (`Effects/`).**
The capability could play a clip and could not let anything draw ON it. This is the one thing the
module genuinely could not do without. **The first-consumer justification is upstream's own
Mandatory Video, which draws over its own video in two places this port had already declared
unported before SP-112 existed:** the blurred-background composite
(`Services/Video/VideoService.cs:3436`) and the attention-check prompt drawn over a playing clip
(`:4846`), both listed in `VideoSurfacePresenter`'s own remarks at SP-111. A clip you cannot draw on
can never grow either. The seam's limit is stated in its own doc: it paints on the decoded picture
and cannot resize or replace it, so upstream's blur composite (picture drawn INTO a larger blurred
background) is still out of reach.

Its `Opening(VideoClipInfo)` half exists for a reason worth naming: the clip is opened and closed
inside the presenter, and `IVideoSurface` deliberately reports what the OS is HOLDING rather than
what the file says — so a painter whose output depends on the clip's LENGTH had no way to learn it.
The alternative was a second decoder on the same file. Upstream re-derives its bubble target the
moment the real length arrives, for the same reason (`BubbleCountWindow.xaml.cs:719-736`,
`AdoptRealDuration`).

**(b) `VideoReasonCodes.VideoAlreadyPlaying` (`Video/`, one constant) plus the refusal that carries
it in `VideoSurfacePresenter.Begin`.** `VideoReasonCodes`'s own doc says it is additive and "every
code lands with the consumer that reads it". **The first consumer wanted this before the second one
existed:** `MandatoryVideoEffect.Compose`'s first guard is "a clip is already on screen — drop the
firing", it runs on the CLOCK thread while playback starts on the SURFACE thread, and it therefore
cannot close the window between the two. Before this, a second `Begin` in that window silently
overwrote `_clip` and `_onEnded` — **leaking an open decoder and leaving the first module believing
it was still playing for ever.** Upstream refuses the same collision at the same granularity
(`Services/BubbleCountService.cs:169-186`).

**(c) Not an addition but a de-duplication: `PrimaryDisplayPlacement` (`Effects/`).** SP-110 gave
`LockCardEffect` a private `DefaultPlacement`; SP-111 gave `VideoSurfacePresenter` its own copy of
the same twenty lines. SP-112 was about to write the third. **This is SP-101's finding arriving a
second time** and it is answered the same way: the display walk and the centring are shared; the
FRACTIONS and the meaning of "no display at all" stay per-module, because those two answers are
genuinely different (a video surface returns null and plays nothing; a card returns a minimum legal
rectangle so the request boundary stays a refusal rather than an exception).

### 2.3 What I DUPLICATED or FOLDED because the seam is the wrong shape — the honest column

| # | the seam | what it cost |
|---|---|---|
| **F1** | **`InputPromptContent` has FOUR slots** (question, progress, answer, hint) and is a LAYOUT rather than a content model. The counting card has FIVE lines upstream: title, attempts, answer, feedback, Esc hint (`Windows/BubbleCountResultWindow.xaml.cs` XAML plus `:249`, `:280`). | **Folded, not grown.** `BubbleCountAnswer.Progress` puts the feedback and the attempt counter in one slot. The fold is honest because those two change together — a judged guess spends an attempt and produces the hint in the same instant — but the finding stands: **a second consumer with a different number of lines must either fold or grow the record, and the third one will hit it again.** |
| **F2** | **`InputKeystrokeKind` names exactly the three keys the lock card cared about.** Enter arrives as `Key` with a raw `VirtualKey`. | Consumed as-is: `BubbleCountAnswer.SubmitVirtualKey = 0x0D`. **I deliberately did not add a `Submit` kind** — that is the "one member per caller" shape the packet forbids — but the constant is a caller re-deriving a Windows fact the capability already had in its hands. |
| **F3** | **The end callback carries nothing.** `IVideoSurface.Begin(..., Action onEnded)` fires identically for "the clip finished" and "the surface stopped holding the picture", and those two mean *ask the question* and *abandon the game*. | The module reads `_surface.LastPlacement` — SHARED state — to tell them apart. Safe because every `Begin`, every frame and the callback run on the one surface thread, and stated as a residue in the code at the read site. **An `Action<CapabilityState>` would have removed it; the first consumer's `OnClipEnded` cannot tell those two apart either and re-paces identically, so this is a gap in the seam rather than in my use of it.** |
| **F4** | **`IVideoPresence.FramesHeld` is a PRESENCE-lifetime counter, not a per-clip one** (prediction P6, confirmed). | Unusable for "how many of MY frames did the OS hold". The module answers the question a different way instead: because the presenter tears the clip down on the first non-`Available` frame, a clip that reached its end with `LastPlacement == Available` had every frame confirmed — so every bubble drawn was in a held picture, and a clip that did not is ABANDONED and never asked about. |
| **F5** | **Both shared capabilities are SINGLE-TENANT** (prediction P5, confirmed, and the sharpest structural finding). Neither knows WHOSE clip or WHOSE card is up. | Every teardown call in `BubbleCountEffect.OnDisarmed` is GUARDED by the module's own bookkeeping: `if (playing) _surface.End()`, `if (answer is not null) _presence.Dismiss()`. The first consumer of each is unguarded and correct — with one consumer there is nothing else to hit. **SP-109's audio presence solved exactly this problem with per-module keyed slots; the video and input capabilities did not, and the second consumer is where that shows.** See §3 for the one residue I could not close from inside my own File Scope. |

### 2.4 Did `Available` mean the same thing for me as for the first consumer?

**Video: YES, identically, and that is the strongest thing in this record.** `Show`'s `Available`
means the OS's own copy of the surface carries *the picture that was handed over* — which is exactly
as true of a composed picture as of a decoded one, because the comparison is against the handed-over
frame rather than against the decoder's output. The capability did not have to know that a second
consumer had drawn on the picture, and that is what makes it a capability rather than a wrapper.

**Input: YES for the card, NO for the ROW.** `Prompt`'s `Available` still means foreground plus
system keyboard focus plus hit test plus ink, unchanged. But for the Lock Card, `Available` is
nearly the whole row's success; for Bubble Count it is a **precondition of the second half of a game
whose first half already happened**. A refused prompt here does not mean "no card was shown" — it
means a clip was played, bubbles were drawn, and the count that was earned can now never be asked
for. That is why this module has a `Refused` resolution AND an `Abandoned` one, and why its panel
quotes **both** capabilities: with two channels, one sentence about "the capability" is false about
one of them.

---

## 3. THE ONE RESIDUE I DID NOT CLOSE, named precisely

The shared `IInputPresence` has no ownership seam, so `Prompt` **replaces** a live card's content and
keystroke callback instead of refusing. Both modules guard with `IsPrompting` in `Compose` (clock
thread) and the prompt happens in `Deliver` (signal thread), so a narrow cross-thread window remains
in which a module can pass its own guard and then prompt over another module's live card, stranding
it for the rest of the session.

**I closed my own direction** (`BubbleCountEffect.Ask` refuses and abandons the game when
`_presence.IsPrompting` is already true) and **did not edit `LockCardEffect.Deliver` to close the
other**, because the symmetric fix belongs in the capability — a typed `input-already-prompting`
refusal inside `Win32InputPresence.Prompt`, which would close it for both consumers at once and
needs its own mutation sweep against the 1010-line presence and its 1137-line fact file. That is the
video capability's `VideoAlreadyPlaying` fix (§2.2b) applied to the other seam, and it is the single
most valuable thing a follow-up packet could do to `Input/**`.

---

## 4. THE DOT — no eighth meaning is owed, and the reason is a finding

```
Live = a firing is on the clock
    && the OS says this process can put a video surface on a display      (channel 1)
    && the OS says this process can put a window in front of a user       (channel 2)
    && ( no clip of MINE is up      OR  the OS's copy of the surface ADVANCED )
    && ( no question of MINE is up  OR  the OS says the card holds foreground+focus )
```

**The seven landed meanings — clock, screen, change, custody, reach, demand, motion — are properties
of the CAPABILITIES, not of the modules.** A module that consumes two capabilities inherits two
meanings: MOTION (SP-111) while its clip plays, DEMAND (SP-110) while its question is up. The third
and fourth clauses are a **phase switch between two existing facts**, not a new fact about the world,
so no eighth meaning is owed. That is the answer to the packet's question, and it is a fact about the
dot's grammar rather than about this row.

**Both "of MINE" qualifiers are load-bearing and neither is a stored "I was told to start" flag.**
Each is this module's intent AND the capability's own live answer (`_playing && _surface.Showing`;
`_answer is not null && _presence.IsPrompting`). The surface and the presence are shared with two
other rows, so a bare `_surface.Showing` would darken this row's dot for **Mandatory Video's** clip
and a bare `_presence.IsPrompting` would darken it for a **Lock Card** — a lie about a module that is
idle and healthy. This is the single-tenancy finding (F5) reaching the dot.

| situation | arm result | dot | why |
|---|---|---|---|
| no display / no compositor / no media stack (and Linux) | `Unavailable` / `video-surface-unavailable` | **Armed** | the whole channel is gone — the Pink Filter answer |
| no interactive station (session 0, or Linux) | `Unavailable` / `input-capture-unavailable` | **Armed** | same, on the other channel |
| clip folder empty | `Degraded` / `video-no-clip` | **Live** | a pool is CONTENT, not a channel — the Subliminals answer |
| this module's clip up, picture STOPPED changing | (nothing new) | **Armed** | MOTION, SP-111's meaning |
| this module's question up, keyboard elsewhere | (nothing new) | **Armed** | DEMAND, SP-110's meaning |
| another row's clip or card up | — | **Live** | not this module's work; it is idle and healthy |

---

## 5. WHAT THE MODULE IS, and what is not ported

A clip plays on a bounded panel with bubbles drifting across it, then the keyboard is taken and the
user is asked how many there were, with three tries and a higher/lower hint.

**Upstream's arithmetic is ported verbatim:** the schedule (`3600/gamesPerHour` +/- 20 %, floored at
60 s, dial clamped 1..**10** — `Services/BubbleCountService.cs:88-96`, and it is NOT the video
module's law); the target `max(3, round(baseRate/30 * seconds +/- 20 %))` with baseRate 3/5/8
(`Windows/BubbleCountWindow.xaml.cs:1139-1151`); the spawn interval `duration*1000/target*0.7` with
the 1500 ms lead-in and the `roll < 0.7 || shown < target/2` rule (`:1199-1245`, integer division
upstream's own); placement `roll*0.7+0.15` / `roll*0.5+0.25` (`:1252-1253`); size 120..225 px over a
1920-wide screen expressed as a fraction of the picture (`:1254`); lifetime `1000 + roll*500` ms
(`:1734`); the grow/pop animation at 0.1 and 0.08/0.12 per 30 ms tick (`:49`, `:1745-1775`); the
safety end at clip length plus 5 s (`:1180`); the 30 s fallback length (`:98`); three attempts
(`:24`), digits only (`:98-101`), Enter submits (`:166-170`), "Please enter a number!" spending
nothing (`:190-195`), and the too-low/too-high hints (`:247`).

**The MECHANISM that differs, deliberately (D133):** the bubbles are composed INTO the decoded
pictures rather than spawned as one topmost transparent window each. Same outcome for the user; and
the port gains an evidence property upstream cannot have — a bubble in the picture is a bubble the
video capability's own read-back proves the OS is holding.

**Not ported, declared rather than stubbed:** the strict lock and the whole WRONG!/WATCH AGAIN
retry-mercy machine including its mercy LOCK CARD; XP, achievements and the level-50 unlock; the
interaction queue and its stuck-detection timeouts; the fullscreen cover on every monitor; content
packs and the browser engine; the LibVLC poison cooldown; the Bambi-freeze bracket; the pop SOUND;
and the #633 inactivity watchdog — whose cause cannot occur here because Escape always closes a card
in this build (D112). **Strict mode is therefore ABSENT rather than an inert dial** (D93's rule):
with no retry loop and Escape always live, a strict switch would move nothing.

---

## 6. Files changed

**Product — new:** `Effects/BubbleCountSchedule.cs`, `Effects/BubbleCountGame.cs`,
`Effects/BubbleCountAnswer.cs`, `Effects/BubbleCountEffect.cs`,
`Effects/PrimaryDisplayPlacement.cs`, `Session/BubbleCountPresetDocument.cs`,
`Views/Pages/BubbleCountPanelNotices.cs`.

**Product — changed:** `Effects/VideoSurfacePresenter.cs` (the painter seam, the already-playing
refusal, the shared placement), `Effects/LockCardEffect.cs` (the shared placement),
`Video/VideoReasonCodes.cs` (**one** additive constant), `Session/SessionParticipant.cs` (the tenth
module, its store, the shared surface and presence), `Views/Pages/StudioPage.axaml` plus `.axaml.cs`
(the row FIRST in GAMES & CARDS, the panel, two dials).

**`Input/**` is byte-identical to base. `Overlay/**` and `Audio/**` are byte-identical to base.**

---

## 7. What this work does NOT prove

- **Nothing here proves a human watched or counted anything.** `watched-verified` and
  `answered-verified` are named manual gates and no automated step on any platform discharges them.
- **No headed capture was taken.** `presentation-verified` is untouched.
- A window read-back is not a monitor (SP-111 §0 stopping point 2 applies unchanged).
- Nothing measures cadence, order or timing; every frame advance is driven by hand on the injected
  clock.
- Linux refuses in type on both capabilities, through the landed factories' own gates.
