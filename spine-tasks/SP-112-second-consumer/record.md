# SP-112 — record

Branch `lane/SP-112-second-consumer`, base `7a35f6d8`.
Floor: pin **1742 unit / 107 headless**; observed **1830 unit / 112 headless with zero failures**;
declared **+88 unit / +5 headless** — see §8, which also records the red gate this packet first
reported as somebody else's and the review correctly attributed to this diff. Build: 0 errors, 0
warnings. `client/tests/floor/floor.json` was never opened.

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
| windows that must NOT take the foreground — upstream's bubbles are `ShowActivated=false` (`Services/BubbleService.cs:2158`; `:2960/2988/3103` are the per-bubble `IsHitTestVisible`, which is the OTHER half of what Bubble Pop needs) with `WM_MOUSEACTIVATE` answering `MA_NOACTIVATE` (`Windows/BubbleCountWindow.xaml.cs:1822-1830`) | `Prompt` ESCALATES for the foreground (D115) and `Confirmed` **requires** `IsForegroundWindow && SystemKeyboardFocusIsThisWindow` |

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
Mandatory Video, which does not hand the decoder's output straight to a surface: its
blurred-background composite draws the picture into something else**
(`Services/Video/VideoService.cs:3436`), listed in `VideoSurfacePresenter`'s own remarks at SP-111 as
unported. A clip nothing can draw on can never grow it.

**And the limit is on the record because the review caught me overclaiming it.** An earlier draft
cited `VideoService.cs:4846` as "the attention-check prompt drawn over a playing clip"; `:4846` is
the strict/retry path picking a fresh clip, and the base wording this port inherited
(`VideoSurfacePresenter.cs:135`) says only "attention checks and the whole strict/retry apparatus".
The citation is corrected here and in the interface's own doc rather than defended. What remains is
narrower and still sufficient: the blur composite is a first-consumer case where the picture reaching
the surface is not verbatim the decoder's — and this seam covers the *draw on the picture* half of it
and **not** the *compose the picture into something larger* half, which is stated in the interface's
own remarks. **The seam also has no CLOSE**: a painter told `Opening` is never told the clip ended,
and `Begin` can refuse after `Opening` has run. SP-112's painter holds nothing but arithmetic, so it
did not need one; the first painter that holds a resource will, and the gap is named in the interface
rather than discovered by it.

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

## 6. PROVING IT BITES — 75 mutations, three rounds, **8 survivors**

Every conjunct and predicate this packet added was mutated one at a time by
`spine-tasks/SP-112-second-consumer/sweep.mjs` — which lives inside this packet's folder and writes
only inside it, because a previous wave's driver wrote three levels above its own root into the
shared checkout. The raw logs are beside this record (`sweep-round1.log`, `sweep-round2.log`,
`sweep-round3.log`) and every count below is taken from them. Each mutation ran the module's own
suites plus `LockCardModuleTests`, `MandatoryVideoModuleTests`, `VideoSurfacePresenterTests`,
`StudioSurfaceNoticeTests` and the three spine suites; the driver restores each file byte-identically
and asserts `git status --porcelain client/src` is empty at the end, which it was on every round.

**The books:** 75 distinct mutations; 39 (round 1) + 25 (round 2) + 2 (round 3) + 1 (round 4) =
**67 caught**; **8 survive**; 67 + 8 = 75. Every one of the 67 has a line in a log beside this
record.

> **The review had to correct these books once, and the correction is the point rather than a
> footnote.** An earlier draft wrote the same total as `39 + 1 + 25 + 2`, where the `+1` was M-al
> run as an ad-hoc smoke test whose log I then DELETED. A run with no surviving evidence is
> indistinguishable from a run that never happened, and counting it is exactly the failure §"Round
> 1" below calls disqualifying — reproduced inside the correction of that failure. M-al is now run
> as round 4, with a durable log.

### Round 1 — 39 caught, 9 survived, **27 NOT PATCHED, and that was the driver's own defect**

Every multi-line needle failed to match. The working tree is CRLF (git's `autocrlf`) and the needles
are written with LF, so 27 of the most interesting predicates — every `Compose` guard, every
teardown guard, two of the dot's clauses — were reported as *not patched* rather than as caught.
**A sweep that silently skips its own hardest cases is worse than no sweep**, so it is recorded here
rather than quietly re-run: the driver now normalises for matching and writes the mutant back in the
file's own line endings, and round 2 re-ran **26 of the 27** — the twenty-seventh, M-al, is round 4
below.

### Round 2 — the 27 plus the 9 survivors: **25 caught, 10 survived**

Eleven of round 1's survivors-and-skips were real holes and are closed by facts that isolate the
clause: the 30 s fallback in `Target`, the 0.7 spacing factor, `Recompute`'s own fallback, the
lifetime's random span, the pop's fade and growth, the module's own pacing interval, and **three** of
the four `Compose` guards (M-am, M-an, M-ao; the already-showing guard M-al is round 4).

### Round 3 — the two real holes round 2 found: **2 caught, 0 survived**

`M-ap` (a clip the surface refused outright leaves the run unresolved, so the panel tells the user
nothing at all) and `M-aw` (a keystroke for a question the module has already finished with).

### Round 4 — the mutation the books had counted without a log: **1 caught, 0 survived**

`M-al`, `Compose`'s already-showing guard (`sweep-round4.log`). It is caught by the
`"a clip is already showing"` row of
`AGameThatCanRunNOTHINGCountsNothing_AndKeepsTheScheduleRunning`, whose surface double records
`Begun` unconditionally — so a module that stopped refusing a firing while a clip is up shows up as
a clip that was begun.

### The eight survivors, each with the evidence that disposes of it

**Four are EQUIVALENT MUTANTS, and two of them are findings about my own code:**

- **M-m — the lead-in tick's unconditional spawn.** Removing it makes the first tick apply
  upstream's `roll < 0.7 || shown < target/2` rule — and at `shown == 0` with a target floored at
  **three**, `0 < 3/2 == 1` is always true, so **the first tick spawns either way**. The clause
  cannot change any reachable outcome, which is a fact about upstream's own two rules meeting, and
  the comment that claimed it was load-bearing is corrected rather than defended.
- **M-p — a bubble's END of life in `Paint`.** `Draw` returns immediately when the animation's
  opacity is zero, and `Animation` clamps the pop's progress to 1, so a bubble past `GoneAt` draws
  nothing whichever guard is removed. Redundant, not unpinned.
- **M-s — the sub-pixel radius guard.** With the guard widened, a radius under one pixel still
  paints nothing: every pixel centre inside the loop's bounding box is further from the bubble's
  centre than the radius. The guard makes that explicit rather than incidental.
- **M-q — both ends of a bubble's life.** It additionally allows drawing a bubble BEFORE it is born,
  which no caller can reach: a bubble's `BornAt` is the tick that spawned it, the presenter's
  elapsed time is monotonic, and a bubble is therefore never in the list at a time before its own
  birth.

**Four are UNCOVERED, and each names why:**

- **M-c — the sixty-second interval floor.** Unreachable through the dial's own clamp: at ten games
  an hour with the jitter at its minimum the interval is `3600/10*0.8 = 288 s`. **Upstream has the
  same structure** (`Math.Max(1, Math.Min(10, …))` at `:88` and `Math.Max(60, …)` at `:95`), so the
  floor is upstream's own belt-and-braces that its own clamp makes dead. It is ported because it is
  upstream's line and it sits where a later dial change would meet it, and the fact now asserts the
  STRUCTURAL property — *the clamp keeps the interval above the floor* — rather than reaching the
  branch by a route no caller has.
- **M-t — the left clamp on the painted disc.** Also arithmetically unreachable: the placement
  offset is `0.15 * width` and the largest radius is `0.0586 * width`, so the disc's left edge is
  never negative. Kept as a structural guarantee; reaching it would mean asserting against a
  placement no product path produces.
- **M-bd — the dot's CLOCK clause.** The same residue SP-110 named as its own `M-aa` and SP-111 as
  its `M-be`: `OwnedSessionEffect.Dot` returns `Armed` before consulting `WorkIsRunning` when the
  module is disarmed, so isolating this clause needs `ScheduleArmed == false` while armed — which
  only `ReleaseWork` produces, and it is `protected sealed` on `PacedSessionEffect` with this module
  sealed. **Unsealing a product class purely to reach it was rejected**, exactly as both earlier
  packets rejected it.
- **M-bp — `PrimaryDisplayPlacement` picking the PRIMARY display rather than the first.** Machine
  conditional: on a single-display machine the two are the same window. Named rather than faked with
  a second monitor nobody has.

---

## 7. THE OVERLAY AND THE LOCK CARD, proven unharmed rather than assumed

`Overlay/**`, `Audio/**` and `Input/**` were **not edited**. They were CONSUMED:
`BubbleCountObservations.RunPainted` builds a real `Win32OverlayPresence` presenting a real
click-through surface, opens a real clip through the operating system's own media stack, paints a
real bubble into the decoded picture, hands it to a real `Win32VideoPresence`, takes it down, and
only then puts a real card up — measuring the overlay through `OverlayWindowProbe` and the card
through `InputWindowProbe`, both SP-099's and SP-110's own instruments, unmodified. The three
rectangles are disjoint, so no surface's hit-test point is occluded by another.

Six facts in `BubbleCountCapabilityTests`:

1. **The positive control:** the OS opened the clip, a bubble was really spawned, and the painter
   really changed pixels the decoder produced — measured on the FRAME, before the operating system
   was involved at all. Without this leg every reading below is a test of nothing happening.
2. **The operating system holds a picture THIS PROCESS PAINTED.** The capability's own differential
   is unchanged and unweakened, and an INDEPENDENT read through `PrintWindow` — a call the product
   never makes — carries the bubble's own pixels exactly as this port composed them, at points that
   are also asserted NOT to be the decoder's flat colour (without which background compared against
   background would pass).
3. **After the clip comes down, the question takes the foreground and the system keyboard focus** —
   the order SP-111's own coexistence run never took.
4. The overlay is click-through, above every ordinary window, never the foreground, and still holds
   its `LWA_ALPHA` of 153 at **four** moments: before, during the clip, during the question, and
   after.
5. The overlay's own differential still bites (with `WS_EX_TRANSPARENT` cleared the same point
   routes TO it) and its own `Present` still earns `Available` after the whole game.
6. The game's geometry constants are pinned against upstream's numbers, so a change to the placement
   arithmetic moves the sample points with it instead of quietly sampling background.

Every expectation **flips with the machine** rather than being skipped: on a machine with no
interactive desktop each leg is asserted false, which is why this file carries no early return and
no entry in the vacuous-shape ledger.

**The nine landed modules' facts are unchanged in SEMANTICS.** Four rack-order/refusal lists and two
headless rack lists grew by one entry each, exactly as they did at SP-105, SP-106, SP-108, SP-109 and
SP-111; `MandatoryVideoModuleTests`' surface double gained the painter parameter and records it. No
landed assertion was relaxed, reworded to be weaker, or deleted.

---

## 8. THE FLOOR — green, and the red one it took first was MINE

Pin **1742 unit / 107 headless**; observed **1830 unit / 112 headless**, **0 failures in either
suite**; declared **+88 unit / +5 headless** (`floor-delta.json`). 1742 + 88 = 1830 and
107 + 5 = 112, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-112-second-consumer`. The floor run
therefore REPORTS a violation against the pin, which is the expected shape: the orchestrator sums the
deltas and applies one bump. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened.
Build: 0 errors, 0 warnings. `client/tests/floor/floor.json` was never opened.

### The failure this packet first reported as somebody else's, and the review inverted

The first floor run reddened on
`InputCapabilityTests.TheThreadLocalFocusRead_ClaimsAWindowThatReceivesNothing_WhichIsWhyTheSystemReadIsTheOneUsed`,
and **this record claimed it was not this packet's. That was wrong, and the three claims are
withdrawn:**

* "a landed fact this packet did not touch" — **true of the code and false of the GATE.**
  `Input/**` and the three input test files are byte-identical to base, and the gate was still red
  *because of this diff*: adding test classes changed xunit's within-class ORDER (the failing case
  moved from position 9 at base to 14 at lane), and the order is the determinant.
* "with the entire `client/` tree reverted it still failed" — **it does not reproduce.** The reviewer
  ran base 4/4 green on the full suite and 10/10 green on the class filter, interleaved with lane
  runs in the same minute; my own reverted-tree run was not the control I took it for.
* "the shipping WPF product v6.8.1 owning the foreground" — **unsupported, and backwards.** A foreign
  process owning the foreground PREVENTS the merge; it was running in all 14 green base runs.

### The real mechanism, measured

`InputWindowProbe.RunNegativeControl()` was **not idempotent across two calls in one process**. Two
consecutive calls, instrumented, returned `threadLocal=True` then `threadLocal=False`.

`SetFocus` ACTIVATES the top-level window it focuses — SP-110's own record says so (§4, M-m/M-p) —
so the parked rig, whose entire purpose is to hold a THREAD-LOCAL focus while something else owns the
foreground, took the foreground itself as soon as the process had the rights to do it. It does not on
the first call, which is why the trap reproduced; it does on every call after the catcher has taken
the foreground once. The catcher then takes the foreground back, the parked thread is DEACTIVATED,
and the system clears its focus — so the clause fails for a reason that has nothing to do with the
trap it measures.

**The fix is one flag**: the parked rig is created `WS_EX_NOACTIVATE` (`ScratchWindow.Create`'s new
`activatable` parameter), which is the same flag and the same reason as this port's own video surface
(D122) — on screen, in the z-order, never taking the foreground from anybody. Two other candidate
fixes were tried and REVERTED because measurement refused them: `SWP_NOACTIVATE` on the show (the
`SetFocus` that follows activates anyway) and a same-process guard on `TakeForeground`'s
`AttachThreadInput` (the merge was not the mechanism). Neither is in the diff.

**And it is pinned so the suite can find it without re-ordering itself:**
`THENEGATIVECONTROLIsIDEMPOTENT_BecauseASecondCallInOneProcessUsedToDestroyItsOwnTrap` calls
`RunNegativeControl` twice and asserts both halves of the trap on both calls. The reviewer's decisive
control — `--filter "~TheThreadLocalFocusRead|~TheDeliveryOracle"`, which fails 3/3 at base — passes
here, and the class passes 3/3.

**The underlying fragility is landed SP-110 code and belongs on the board as SP-110's row**; what
this packet owed was the corrected attribution and a green gate, and both are here. Nothing was added
to `allowedSkips` and the pin was not widened.

## 9. Files changed

**Product — new:** `Effects/BubbleCountSchedule.cs`, `Effects/BubbleCountGame.cs`,
`Effects/BubbleCountAnswer.cs`, `Effects/BubbleCountEffect.cs`,
`Effects/PrimaryDisplayPlacement.cs`, `Session/BubbleCountPresetDocument.cs`,
`Views/Pages/BubbleCountPanelNotices.cs`.

**Product — changed:** `Effects/VideoSurfacePresenter.cs` (the painter seam, the already-playing
refusal, the shared placement), `Effects/LockCardEffect.cs` (the shared placement),
`Video/VideoReasonCodes.cs` (**one** additive constant), `Session/SessionParticipant.cs` (the tenth
module, its store, the shared surface and presence), `Views/Pages/StudioPage.axaml` plus `.axaml.cs`
(the row FIRST in GAMES & CARDS, the panel, two dials, and the rack's new ScrollViewer — D140).

**`Input/**` is byte-identical to base. `Overlay/**` and `Audio/**` are byte-identical to base.**

**Tests — new.** Counts are TEST CASES, each `[Theory]` row counted individually, which is the unit
`check-floor.mjs` counts and the unit `floor-delta.json` declares.

| file | cases | what it is |
|---|---|---|
| `BubbleCountModuleTests.cs` | **72** | the pacing law, the counting arithmetic, the painter, the answer machine, the six arm outcomes, the five-clause dot, every resolution, the guarded teardown and the shared placement |
| `BubbleCountObservations.cs` | — | the one real-desktop run |
| `BubbleCountCapabilityTests.cs` | **6** | the OS holding a painted picture, the video-then-card order, and the overlay at four moments |

**Tests — changed:** `VideoSurfacePresenterTests.cs` (**+4** — the paint seam and the already-playing
refusal, with its own positive control), `StudioSurfaceNoticeTests.cs` (**+5** — the two-channel
row's seven Armed sentences, its six endings, both quoted capabilities, the shared-folder line and
the difficulty line), `MandatoryVideoModuleTests.cs` (the surface double takes and records the
painter — **0 count change**), `RealDesktopCollectionGuardTests.cs` (the helper census gains
`BubbleCountObservations` — a STRENGTHENING, **0 count change**), `AudioModuleSpineTests.cs`,
`ContinuousEffectSpineTests.cs`, `SecondEffectSpineTests.cs` (rack-order and refusal lists grow by
one — **0 count change**), `StudioRackHeadlessTests.cs` (the rack lists grow by one; **+4** new
cases plus the rack-scroller fact = **+5**, the whole declared headless delta; its `Click` helper now
scrolls the target into view, and because that means no other rack fact can notice a clipped row
again, `RackScroll` is pinned EXPLICITLY — the scroller's two visibility modes, that the extent
really exceeds the viewport at ten rows, and that the last row selects and opens its panel — see
D140).

**Tests — changed (SP-110's instrument, for §8's gate fix):** `InputWindowProbe.cs`
(`ScratchWindow.Create` gains `activatable`, and the parked rig takes `WS_EX_NOACTIVATE`),
`InputCapabilityTests.cs` (**+1** — the idempotency pin).

72 + 6 + 4 + 5 + 1 = **88**, which is the declared unit delta — the last one being the idempotency
pin in `InputCapabilityTests.cs` (§8). The headless delta is 4 rack/panel/dial facts plus the
rack-scroller fact = **5**.

**Docs:** `client/docs/wpf-surface-reachability.md` (§SP-112, D133–D140).

---

## 10. What this work does NOT prove

- **Nothing here proves a human watched or counted anything.** `watched-verified` and
  `answered-verified` are named manual gates and no automated step on any platform discharges them,
  Windows included.
- **No headed capture was taken.** `presentation-verified` is untouched. The window read-back is an
  OS query about pixels the OS holds FOR A WINDOW — SP-111 measured that it is not monitor-aware —
  and nothing here says the bubbles are legible, countable at speed, or on a screen anybody is
  looking at.
- **Nothing measures cadence, order or timing.** Every frame advance in every fact is driven by hand
  on the injected clock, so a game whose bubbles appeared twice as fast as intended satisfies every
  check here.
- **The COMPOSITED DESKTOP leg was not taken for this module.** SP-111's video facts read the screen
  through `BitBlt`; this packet's run reads the window's own copy and `PrintWindow` only. The
  stronger leg is machine-conditional and it was not re-run here.
- **Nothing proves the count a user would make matches the count the module asks about.** The module
  asks about bubbles it drew into pictures the OS confirmed holding; whether a human watching could
  have counted them is exactly the manual gate above.
- **The painter's cost was not profiled.** Every bubble is a managed per-pixel blend over its own
  disc, on the surface thread, once per frame. Measured fast enough at 320x240 in the harness; not
  profiled at 1080p on a contended machine, and it stacks with the nearest-neighbour composition
  SP-111 already named as the most likely place the port's video will need work.
- **Concurrency is single-threaded.** The module's keystrokes, its dot, its safety end and its
  resolutions are exercised on one thread. Two threads racing a resolution against a disarm are not
  covered, and the single-tenancy residue in §3 is precisely a two-thread question.
- **Linux is unproven** on both capabilities and refuses in type through the landed factories' own
  gates; nothing in this packet changes or discharges either.
- **No second instance of anything was exercised.** One video surface, one input presence, one
  process — the composition root's rule. What two of either would do to one foreground or one
  rectangle is still untested.
