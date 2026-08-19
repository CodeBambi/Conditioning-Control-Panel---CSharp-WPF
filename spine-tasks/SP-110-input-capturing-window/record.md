# SP-110 — record

Branch `lane/SP-110-input-capturing-window`, base `b8187eb4`.
Floor: pin **1589 unit / 100 headless**; observed **1647 unit / 104 headless**; declared delta
**+58 unit / +4 headless** (`floor-delta.json`). 1589 + 58 = 1647 and 100 + 4 = 104, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-110-input-capturing-window`. The floor
run therefore REPORTS a violation against the pin, and that is the expected shape: the orchestrator
sums the deltas and applies one bump. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened.
Build: 0 errors, 0 warnings. `client/tests/floor/floor.json` was never opened.

---

## 0. THE HEADLINE — the trap was real, it was measured, and it nearly ate this packet

SP-099 spent a wave proving a surface is click-**through**, and its final review recorded that
proving input *passes through* is the property most easily faked. This packet had to prove the
opposite for a different window at the same standard.

**Before the first product edit I built a throwaway Win32 probe and asked the operating system nine
questions** (`plan.md` §0 carries the raw output of both runs). The first run found the fake:

```
E0 plain SetForegroundWindow=False   foreground=0x130938   systemFocus=0x130938  ours=0x1F306A6
F3 focus(thread): hwndFocus=0x1F306A6                       focusIsOurs=True      <-- OUR window
F5 SendInput(VK_F13): injected=True pumpIterations=400 keydownF13=0               <-- nothing arrived
```

> **`GetGUIThreadInfo(<i>threadId</i>).hwndFocus` is THREAD-LOCAL.** It names the focus inside our
> own thread's input queue, and it named our window while the foreground — and every injected
> keystroke — belonged to another application. A capability built on that read would have been an OS
> API, correctly called, certifying nothing: the exact inverse-SP-099 fake, in a form that looks more
> rigorous than the honest version.

The system-wide read is `GetGUIThreadInfo(**0**)` — documented as the foreground thread — and in the
same instant it answered the *other* application's window. That is the read the capability uses, and
the difference is not left as a comment: `InputWindowProbe.RunNegativeControl` **reproduces the
divergent state deterministically on every suite run** by parking a focused window on a second thread
while the foreground sits on the first, and
`TheThreadLocalFocusRead_ClaimsAWindowThatReceivesNothing_WhichIsWhyTheSystemReadIsTheOneUsed`
asserts all three halves of it.

The second thing run 1 found: **a plain `SetForegroundWindow` from a process that does not already
own the foreground returns FALSE.** WPF survives that because its card re-grabs forever from
`Deactivated` (`Windows/LockCardWindow.xaml.cs:160-176`). This port must EARN the claim or refuse it,
so it escalates once (`AttachThreadInput` + `SetForegroundWindow`, detached immediately) and then
**asks the OS whether it worked** — measured returning TRUE and handing over both the foreground and
the system keyboard focus. That escalation is a real divergence and is recorded as D115.

---

## 1. WHICH ROW — Lock Card, and all three survey claims were checked

SP-108 §7 named three rows blocked on this capability. I verified each against
`ConditioningControlPanel/` rather than trusting the table (`plan.md` §1 has the per-row evidence).

| Row | Survey's claim | Verdict |
|---|---|---|
| Bubble Pop | 4918 lines, spawn timer + per-bubble `DispatcherTimer` hops, clickable moving windows | **holds** — `wc -l` is 4918 exactly; `IsHitTestVisible = _isClickable` at `:2960/:2988/:3103` with `MouseLeftButtonDown` at `:2966/:3018/:3113` |
| Bubble Count | needs video playback and interactive message windows (`:30-39`) | **holds, and the citation is exact.** `:30` is `private string _videosPath = "";`, `:31-33` the pack-video lists, `:38` `List<Window> _messageWindows`. Class summary at `:18-21`: "plays a video … then asks for the total" |
| Lock Card | `ShowOnAllMonitors(...)` at `LockCardService.cs:299` — an input-capturing modal on every monitor | **holds** — exact call at `:299`; the foreground grab is `LockCardWindow.xaml.cs:594-601` |

**The survey is correct on all three rows, and it needed no correction.** An earlier draft of this
record claimed Bubble Count's video field had drifted to `:29`; that was MY error and it is
withdrawn — `:29` is `private bool _isBusy;` and `:30` is the `_videosPath` the survey cited.
Recorded rather than quietly deleted, because a wrong correction propagated into the next
packet's survey is worse than no correction at all.

**Lock Card chosen**, because "simplest to prove" is a property of the OS question, not of the
feature: it needs ONE STATIC WINDOW holding the keyboard, which is `GetForegroundWindow`,
`GetGUIThreadInfo(0)` and one `WindowFromPoint`. Bubble Pop needs those same facts per bubble on
windows that MOVE — the hit test's answer is a function of a position that changes between asking and
clicking, which makes the fact a race by construction — plus D84's cost class. Bubble Count needs a
video capability before its input half is even reachable. Lock Card is also PACED
(`LockCardService.cs:127-134`), so `PacedSessionEffect<TFiring>` fits and all the novelty lands on
the capability instead of on the spine.

---

## 2. THE PROVABLE CHAIN, AND WHERE IT STOPS

### The chain, five links

| # | Fact | API | Measured |
|---|---|---|---|
| **L1** | the OS holds the card, visible, at the exact rectangle, above every ordinary window in its own z-order | `IsWindow` / `IsWindowVisible` / `GetWindowRect` / `GetTopWindow`+`GetWindow` walk | z=1 of 20, first ordinary at 6 |
| **L2** | **the OS reports the card as THE FOREGROUND WINDOW** | `GetForegroundWindow()` | `== ours` after the escalation; **`!= ours` before it** |
| **L3** | **the OS reports the card as the FOREGROUND THREAD's keyboard focus** — the system-wide read, not the thread-local liar | `GetGUIThreadInfo(0).hwndFocus` | ours (run 2); the other app's (run 1) |
| **L4** | the window manager routes a hit test at an interior point **TO** the card — the exact inverse of SP-099 | `WindowFromPoint` | ours |
| **L5** | **a keystroke synthesised at OS level reaches the card's own window procedure**, arrives as a translated CHARACTER at the caller, and does NOT arrive when another window holds the foreground | `SendInput` + the window's `WndProc` | `VK_F13` → 1 keydown on the first pump; `VK_A` → `WM_CHAR 'a'`; thief holding foreground → **0** |

Plus one content link, the same grade of evidence as SP-100's overlay read-back: **the OS holds ink
in the card's own client area**, counted by reading the window's device context back
(`GetDC` + `GetPixel`), measured 161 non-background pixels of 7600 sampled in the throwaway probe and
re-measured on the product's own card in `TheOperatingSystemHoldsInkForThePromptsOwnClientArea`.

`SendInput` is the TEST instrument's, never the product's. **Nothing shipped injects input.** A
capability that pressed keys to check it could receive them would be typing into whatever window it
had just failed to take the foreground from.

### WHERE THE CHAIN STOPS — stated plainly, and it stops early

1. **No human pressed anything.** Every keystroke in the suite is `SendInput`, which enters the
   system input stream below the driver and above the hardware. **`answered-verified` is a named
   manual gate and no automated step on any platform discharges it, Windows included.**
2. **Injected input is not physical input.** UIPI refuses injection into a higher-integrity window,
   the secure desktop takes it away entirely, and a locked workstation refuses it silently. The
   fixture DETECTS those (`SendInput` returns 0, or the foreground grab fails) and the expectation
   flips with the machine — never a skip.
3. **The foreground is LENT, not owned.** Any process can take it back the instant after the check.
   Every claim here is "what the OS said when it was asked", never "what the OS will keep saying".
4. **Nothing proves the card is READABLE.** The ink read-back proves the OS holds non-background
   pixels in the question band. It does not prove the phrase is legible, unclipped, correctly laid
   out, or on a screen anybody is looking at. `presentation-verified` is untouched for this surface.
5. **Nothing proves the host's own loop translates keystrokes for this window.** The product's pump
   is Avalonia's; all the evidence here is taken on the presence's own bounded `Pump`. Named as an
   unproven coupling rather than assumed.
6. **Linux is unproven** and refuses in type with a five-step gate that is run separately on X11 and
   Wayland, because they answer differently — and the gate says in its own text that **Wayland will
   probably fail by design**.

The evidence class is written up in `client/docs/verification-harness.md` §"Input evidence class
(SP-110)" with all three classes (`focus-verified`, `input-delivered`, `answered-verified`) and the
three things tier 1 cannot cover for input.

---

## 3. THE DOT'S SIXTH MEANING — decided: **`Live`**, and it is owed

> **Is the module `Live` while the prompt is up and unanswered? YES.** Waiting for a human IS the
> work. A module that went `Armed` the moment it did its job would be the under-claim SP-109 refused
> for Brain Drain's half-row.

But the existing paced rule (`WorkIsRunning => ScheduleArmed`) is not enough, and the reason is
measured rather than imagined: a card can be visible, topmost and hit-testable while the operating
system routes the keyboard somewhere else entirely. A dot lit there claims a question nobody is being
asked.

```
Live  =  a firing is on the clock
      &&  the OS says this process can put a window in front of a user   (window station + display)
      &&  ( nothing is being asked  OR  the OS says the card holds foreground + focus )
```

> **The sixth meaning is DEMAND: a claim on the user's ATTENTION, which the operating system grants
> and can revoke without this process doing anything at all.**

The five before it were the **clock** (paced), the **screen** (continuous), **change** (moving),
**custody** (non-drawing) and **reach** (audio). REACH is the closest and it is still a different
fact: an audio device, once opened, is HELD by this process — it appears in the volume mixer and
stays ours. The foreground is **lent**: contested by every other process on the desktop, revoked by a
click on another window, and refused outright to a process that does not already own it. That is the
first time the dot's third input has depended on something another program can take away.

Each clause is pinned by its own fact and each was mutated separately (§4, M-aa/M-ab/M-ac/M-ad):

| situation | arm result | dot | why |
|---|---|---|---|
| no interactive station (session 0, or Linux) | `Unavailable` / `input-capture-unavailable` | **Armed** | the whole CHANNEL is gone — the Pink Filter answer |
| phrase pool empty | `Degraded` / `input-no-phrase` | **Live** | a pool is CONTENT, not a channel — the Subliminals answer; enable one and the next card asks it with no re-arm |
| card up, OS routing the keyboard elsewhere | (nothing new; the card was already refused or has lost focus since) | **Armed** | nobody is being asked anything |
| card up and holding the input | — | **Live** | the question is really in front of them |

**The disjunction matters and is pinned on its own** (`M-ad`): replacing clause 3 with a plain
`HoldsTheInput` conjunct would darken the dot for the 99 % of a session with no card up, which is the
opposite lie.

---

## 4. PROVING IT BITES — the mutation sweep

**66 mutations, FOUR rounds, 57 caught, 7 survivors — and round 1 found nine real holes, round 4 the review's.** Every
conjunct of every predicate this packet added was mutated one at a time, the WHOLE unit suite was run
against each, and each file was restored byte-identically afterwards (verified by `git status` and by
grepping the three key predicates back out). The sweep is not bounded by where the last hole was
found: it covers the two observation records, the presence's six gates, the escalation ladder, the
window procedure, the module's three-clause dot, its three `Compose` refusals, both `Ready`
narrowings, the schedule arithmetic, every typing rule, the phrase rotation and every clamp on the
document.

### Round 1 — 36 mutations, 23 caught, 13 survived

| # | mutation | round 1 |
|---|---|---|
| M-a…M-g | `InputCaptureObservation.Confirmed` drops each of `Asked` / `IsForegroundWindow` / `SystemKeyboardFocusIsThisWindow` / `HitTestRoutesHere` / `WindowVisible` / `AboveEveryOrdinaryWindow` / `WindowExists` | caught ×7 |
| M-h…M-k | `InputStationObservation.Confirmed` drops each of its four | caught ×4 |
| M-l | `HitTestRoutesHere` drops the non-zero window guard | **SURVIVED** → closed |
| M-m | `HoldsTheInput` drops the foreground clause | **SURVIVED** → equivalent |
| M-n | `HoldsTheInput` drops the system-focus clause | caught |
| M-o | `HoldsTheInput` drops the visibility clause | **SURVIVED** → equivalent |
| **M-p** | **`SystemKeyboardFocus` asks the THREAD instead of the foreground** | **SURVIVED** → equivalent, and this is the finding |
| M-q | `Prompt` stops refusing on an unusable window station | **SURVIVED** → closed |
| M-r | `Prompt` never tries to take the foreground | caught ×6 |
| M-s | `Prompt` stops degrading on a blank card | **SURVIVED** → closed |
| **M-t** | **`Prompt` never paints the card at all** | **SURVIVED** → closed, and it exposed a blind ink check |
| M-u | `TakeForeground` never escalates (WPF's plain call only) | caught ×6 |
| M-v | `Escalate` stops attaching the input queue | caught ×6 |
| M-w | `Dismiss` stops checking the hit test | **SURVIVED** → unreachable |
| M-x | `Dismiss` keeps feeding the caller's callback | **SURVIVED** → closed |
| M-y | a refused prompt is left ON SCREEN | **SURVIVED** → uncovered, named |
| M-z | control characters count as typing | **SURVIVED** → closed |
| M-aa | the dot drops the CLOCK clause | **SURVIVED** → uncovered, named |
| M-ab | the dot drops the STATION clause | caught |
| M-ac | the dot drops the DEMAND clause | caught |
| M-ad | the dot requires the input even with no card up | caught |
| M-ae | `Compose` stops honouring an unreachable user | **SURVIVED** → closed |
| M-af | `Compose` stacks a second card on the first | caught |
| M-ag | `Compose` invents a phrase for an empty pool | caught ×2 |
| M-ah | `Ready` stops refusing when nothing can be asked | caught |
| M-ai | `Ready` stops degrading over an empty phrase pool | caught |
| M-aj | a refused card is left up and reported as shown | caught ×3 |

### THE SHARPEST SURVIVOR — M-t, and it means the ink check was BLIND

Deleting the paint call entirely survived. The reason is the kind of thing only a mutation finds:
**the card's window class registers no background brush**, so an UNPAINTED window's device context
holds whatever the OS left in it — which differs from the background colour just as reliably as text
does. A bare "count the pixels that are not the background" therefore reported *inked* for a window
nothing had ever drawn on.

The check is now a **differential**, like every other fact in this port: a control point in a margin
the painter fills and never writes on must read back **exactly** the background colour — which is
what proves the fill happened — and only then does a differing pixel in the question band count as
ink. `InputCaptureObservation.Inked` is `BackgroundHeld && InkedPixels > 0`, and three further
mutations were added to cover the new clause (M-bf/M-bg/M-bh, all caught). The read's stride is now
derived from the band's size toward a target sample count, because the same read runs on every
repaint and a card covering half a 4K display must not cost proportionally more than a small one.

### Round 2 — the 13 survivors plus 3 new ink mutations; 9 more closed

| # | closed by |
|---|---|
| M-l | `EveryClauseOfConfirmed_IsLoadBearing...` (the `NotAsked` observation must not claim a hit test routes to a window it does not have — `0 == 0`) |
| M-q | `DisarmReleasesTheWorkUNCONDITIONALLY...` (a landed fact, reached once the station read gained a consumer) |
| M-s, M-t, M-bf, M-bh | `ACardWithNothingWrittenOnIt_IsDEGRADED_AndTheInkCheckKnowsAPaintedBackgroundFromAnUnpaintedWindow` and `ACardOnNoDisplayAtAll_NeverClaimsAvailable_AndTheINKReadBackIsWhatCatchesIt` |
| M-x | `AfterDismissTheCardStopsFeedingTheCaller_EvenForAMessageAlreadyInItsQueue` |
| M-z | `AControlCharacterIsNotTyping_AndAPrintableOneIs` |
| M-ae | `WithNoDesktopToAskOn_TheModuleIsARMED...`, extended to advance the clock and assert **no card is ever shown** |
| M-bg | `ACardWithNothingWrittenOnIt...`, extended to the REPAINT path, which carries its own copy of the check |

### Round 3 — the 22 not yet run: **22 caught, 0 survived**

The schedule arithmetic (M-ak…M-ap), every typing rule (M-aq…M-aw), the phrase rotation
(M-ax…M-ba) and every clamp on the document (M-bb…M-be).

**M-ak is caught by WEDGING the suite** — it reintroduces the hot loop of §6, and the hang *is* the
assertion. The sweep driver bounds each run so a wedge is reported rather than stalling it, and that
outcome is recorded as what it is rather than dressed up as a failing assertion.

### Round 4 — the code review's own fixes, mutated in turn: 3 caught, 1 uncovered

| # | mutation | verdict |
|---|---|---|
| **M-bi** | **`Deliver`'s refusal branch drops `_presence.Dismiss()`** — the user-harm bug itself | **caught ×2**, by both refusal facts, once the double was made to mirror the product |
| M-al | `OnDisarmed` never resets the schedule counter | **caught** by `ARE_ArmedRunGetsItsFirstCardOffsetBACK...`, which did not exist before the review said this clause had no coverage |
| M-bh | the ink control point is never read (re-run against its new call site) | caught |
| M-bj | a painted band may reach the card's edge, where the control point is read | **SURVIVED** → uncovered, named below |

**M-bj is a structural guarantee replacing an incidental one, and no reachable card size makes its
absence observable.** Without the floor the first band's inset is `width / 12`, which only reaches the
control point's column for a card narrower than ~24 px — and the product's own placement is 55 % of a
display, while every fixture uses hundreds of pixels. Writing a 12-pixel card to reach it would be
asserting against `DrawText`'s behaviour in a one-pixel-tall rectangle, which is not a fact about this
capability. The floor stays because it makes the invariant a property of the painter rather than an
accident of one inset; it is reported here rather than covered by a fact that would prove nothing.

### The six survivors, and not one of them is papered over with a fact that asserts shape

**Three are EQUIVALENT MUTANTS**, and the middle one is this packet's most interesting finding:

- **M-m / M-p — the foreground clause and the system-vs-thread focus read cannot be prised apart for
  a childless top-level window, and that is MEASURED.** The state that would separate them (the
  foreground on one window, the foreground thread's focus on another, both on one thread) **cannot be
  built**: `SetFocus` ACTIVATES the top-level window it focuses, so the two move together —
  measured inside `HoldingTheInputRequiresTheFOREGROUND_NotJustAFocusRead`, which asserts the
  relationship rather than an outcome it wanted. The consequence for the packet's central trap is
  worth stating plainly: **`GetGUIThreadInfo(0)` versus the thread-local read only changes an answer
  when our window's thread is NOT the foreground thread — and in exactly that case the foreground
  clause has already said no.** The trap is real (it is reproduced deterministically by the probe's
  own negative control, where the two reads DO disagree) and the port is immune to it by redundancy,
  not by having asked the right question. Both are kept: they are two different questions, and they
  diverge the moment the card grows a child control.
- **M-o** (`HoldsTheInput`'s visibility clause). Hiding a window takes the foreground away from it —
  the port's own `Dismiss` asserts exactly that — so the visibility conjunct cannot be false while the
  foreground conjunct is true. Redundant, not unpinned.

**Three are UNCOVERED, and each names why:**

- **M-w** (`Dismiss` re-checking the hit test). After `ShowWindow(SW_HIDE)` the OS never routes to the
  window again, so no reachable state makes this clause false. The OUTCOME it guards is pinned
  independently by the probe (`DismissingThePrompt_TakesItDownAndGivesTheKeyboardBack` asserts the hit
  test no longer answers the card), so the product's own claim is redundant with a fact taken through
  a second code path.
- **M-y** (a refused prompt left on screen). **No deterministic post-placement refusal could be
  constructed on a healthy machine** — and the attempt is itself a finding: a card placed at a
  rectangle NO MONITOR COVERS still passes every routing check, because `WindowFromPoint` walks the
  window tree rather than the monitors. That card is caught by the ink link instead, and the
  MODULE-level equivalent (any non-`Available` takes the card down) is covered by two facts.
- **M-aa** (the dot's CLOCK clause). Isolating it needs `ScheduleArmed == false` while the module is
  still armed, enabled and live, which only `ReleaseWork` produces — and it is `protected sealed` on
  `PacedSessionEffect` with `LockCardEffect` sealed, so no probe subclass can reach it. SP-109 solved
  the same problem for the audio pair with a probe subclass of the shared base, and the clause is
  pinned THERE (`BOTHClausesOfTheFifthDotMeaningAreLoadBearing...`) for that module's override.
  **Unsealing a product class purely to reach it was rejected**; the remedy is named instead.


---

## 5. THE OVERLAY, PROVEN UNHARMED RATHER THAN ASSUMED (trap 1)

`Overlay/**` was **not edited**. It was CONSUMED: `InputCaptureObservations.RunCoexistence` builds a
real `Win32OverlayPresence`, presents a real click-through surface, and measures it three times
through `OverlayWindowProbe` — SP-099's own instrument, unmodified — **before** any card exists,
**while** a card holds the foreground, and **after** the card is dismissed and its presence disposed.

Five facts in `InputOverlayCoexistenceTests`:

1. **The card really took the input**, and the overlay really reached the screen. Without this leg
   every fact below would be a test of nothing happening. (It is the same role SP-099's middle leg
   plays for click-through.)
2. The window manager still routes the overlay's own centre **PAST** it at all three moments, and
   `WS_EX_TRANSPARENT` survives the whole lifecycle.
3. The OS's own z-order walk still puts it **above every ordinary window** at all three moments. Not
   "above every window": a card is topmost too and legitimately contests the band, which is the same
   wording and the same reason as SP-099's own z-order fact.
4. The overlay **never becomes the foreground**, and the OS still holds its `LWA_ALPHA` unchanged.
5. **The overlay capability's OWN `Available` is re-earned** after a card has held the foreground —
   which is eight OS round trips including its own click-through differential — and the differential
   is re-run **on the overlay itself**, so "the point went elsewhere" cannot be satisfied by an
   overlay that quietly stopped existing.

The card is placed at a rectangle **disjoint** from the overlay's, so the overlay's hit-test point is
never occluded by the thing under test.

`OverlayCapabilityTests` and `OverlayObservations` are byte-identical to base. Nothing in the overlay
capability was weakened, and no assertion of its was relaxed.

---

## 6. THE BUG THE TESTS FOUND, and it would have shipped

Writing the empty-phrase-pool fact **hung the suite**. The cause is in the module's own scheduling:

> The first-card delay is an OFFSET into the opening interval, `roll * (60/perHour)`
> (`LockCardService.cs:167`), and it can legitimately be **zero**. The first draft chose the offset
> whenever `FireCount == 0` — so a firing that came due and produced NOTHING (empty pool, a card
> already up, a desktop that cannot take the input) re-armed at zero and re-fired immediately,
> forever.

Upstream cannot have the bug because its offset is computed once in `Start` (`:74`) while every
`Timer_Tick` recomputes the ±30 % interval (`:127-134`) whether or not the tick showed anything. The
port now counts **schedules per arm** rather than firings, reset on disarm so a re-armed run gets its
offset back, and `AFiringThatShowedNothing_ReArmsAtTheORDINARYInterval_NotAtTheFirstCardOffset`
pins it. The hang IS the assertion; the counts beside it only confirm the loop did the work.

---

## 7. THE OTHER FINDING — an upstream rule that cannot fire here, said out loud

Upstream's anti-cheat has two halves. The MECHANICAL half (cancel every paste command, disable undo,
swallow Ctrl+C/V/X/A/Z/Y and Shift+Insert/Delete — `LockCardWindow.xaml.cs:196-234`, `:246-264`)
exists because upstream's card is a WPF `TextBox`. **This port's card has no edit control at all**:
it is a raw window whose window procedure receives `WM_CHAR`, so there is no paste route to close and
Ctrl+V arrives as the control character 0x16. The hardening reduces to one rule — *a control
character is not typing* — enforced in the window procedure.

The SEMANTIC half (`HasTypedEnough`, `:272`) is ported and applied — **and in this build it can never
fire**, because the only content path credits exactly one keystroke per appended character, so at the
instant the answer equals the phrase the credit is already at least the phrase's length. That is
recorded as **D119** and pinned by a fact that asserts the STRUCTURAL property rather than faking a
back door to reach the branch. The rule is kept because it is the rule, sitting where a future
keystroke source that appends without crediting would meet it — the same judgement
`ReleaseIfStillOurs`'s residual got at SP-106.

---

## 8. THE SAFETY DECISION — this packet could have shipped an inescapable window

Upstream's strict mode gates Escape: `EscClosesCard => !strict || !PanicEscapeIsLive`
(`LockCardWindow.xaml.cs:632`), where "live" means the panic key's `WH_KEYBOARD_LL` hook is really
installed — upstream checks the HOOK and not the SETTING because Windows silently un-registers a
low-level hook whose callback overran, with nothing reinstalling it (`:610-622`, the #616–#623
cluster).

**The port has no panic-key hook at all, so upstream's own rule returns TRUE and every card here
closes on Escape, strict or not.** That is not strict mode weakened; it is upstream's rule evaluated
against this build's facts, and upstream's own comment is the authority: strict mode "must never
become an inescapable trap … every failure mode here has to fall open, not shut." Taking the other
reading would have put a window on the user's screen that takes their keyboard and cannot be
dismissed. Recorded as **D112**, pinned in both arms by
`EscapeAlwaysClosesACardInThisBuild_BecauseUpstreamsOwnRuleFallsOPENWithoutAPanicHook`, and stated in
the panel's own leading notice where the user reads it.

Two more safety properties fall out of the same reasoning and are pinned:

* **A refusal takes the card straight back down** — including `Degraded`, because a focused blank
  card is a question nobody can read. Upstream's own error path force-closes every card it may have
  half-shown (`LockCardService.cs:308-313`).
* **The port's card is not fullscreen and not on every monitor** (D110). The outcome — a question you
  cannot ignore — is carried by foreground + focus, not by area, and a fullscreen focus-stealing
  cover is the one shape this port must not ship while strict mode has no panic key behind it.

---

## 8b. WHAT THE CODE REVIEW CAUGHT — a user-harm bug this record had already claimed was avoided

**§8 above was true of the intent and false of the code, and the review found it.** Three findings,
all real, all fixed:

**1. A `Degraded` card was left on the user's screen with Escape dead.** `Deliver`'s refusal branch
resolved and returned **without dismissing**. For `Unavailable` that is safe — the presence's
`RefuseAndWithdraw` hides the window itself — but `Degraded` is the case where the OS DID give the
card the foreground and the keyboard and only the ink read-back refused, so `_prompting` is true and
the window stays up. The consequences compound exactly as the review described: `Resolve` clears
`_attempt`, so `OnKeystroke`'s identity guard discards **every key including Escape**; `Compose`'s
already-prompting guard then drops every future card for the rest of the session; and the dot keeps
reading `Live` throughout. A topmost blank window holding the keyboard with no way out is the precise
shape `IInputPresence`'s own contract forbids. **`_presence.Dismiss()` now runs on every non-Available
outcome**, and the mutation that removes it (M-bi) is caught.

**2. The test double diverged from the product exactly where the bug lived.**
`RecordingInputPresence.Prompt` set `IsPrompting` only on `Available`; the real presence sets it from
`observation.Confirmed`, which excludes ink. So the double reported "no card up" in the one state that
traps the user, and no fact built on it could have seen the defect. The double now mirrors the
product (`outcome is not Unavailable`), and both refusal facts assert `Dismissals == 1` and
`IsPrompting == false` — which the three sibling facts already did and these two alone did not.

**3. Evidence for the same defect was measured and not asserted.** `OffScreenWindowLeftVisible` and
`OffScreenPresenceStillPrompting` were populated and asserted nowhere, under a doc-comment that said
"must be false". Both are now asserted — **and the doc was the thing that was wrong**: a `Degraded`
prompt keeps its window ON PURPOSE, because the operating system has given it the input, and taking
it down is the MODULE's job. The corrected doc says so, and the assertion is what keeps the
module-side fact testing something real rather than a window that was never up.

**Four smaller corrections in the same pass:**

* **My "survey correction" was itself wrong and is withdrawn.** `BubbleCountService.cs:30` really is
  `private string _videosPath = "";` — SP-108 §7's citation was exact. `:29` is `_isBusy`. Recorded
  rather than silently deleted, because a wrong correction propagated into the next packet's survey
  is worse than no correction at all.
* `SecondEffectSpineTests`' comment said "seven modules … all seven are named" over a six-name list.
  Corrected to six.
* `OnDisarmed`'s schedule-counter reset had no fact behind it. Now pinned by
  `ARE_ArmedRunGetsItsFirstCardOffsetBACK_BecauseEveryRunIsANewFirstCard`, measured through the
  clock (a re-armed run fires immediately at a zero offset; without the reset it waits the ordinary
  interval).
* The ink control point was read at a fixed `(2, 2)`, which falls inside a text band on a card
  narrow enough that the proportional inset collapses. Every band is now floored at
  `ControlMargin = 3`, so the outermost rows and columns are always background at any size, and the
  mutation that removes the floor (M-bj) is caught.
* `CoexistenceRun.CardTookTheInput` was sourced from the product's own `HoldsTheInput` while every
  other leg was probe-sourced. It now comes from the probe, so the "during" leg cannot be softened by
  the thing under test.
* `CardCount`'s doc claimed a refused card is not counted; the code counts every firing that got as
  far as calling the capability. **The doc was corrected, not the counter** — what the user did with
  a card is `LastResolution`/`SolvedCount`/`DismissedCount`, which is where that question belongs.

---

## 9. What this work does NOT prove

- **Nothing here proves a human pressed anything.** `answered-verified` is undischarged and is not
  dischargeable by this suite or by any automated step on any platform.
- **No headed capture was taken.** `presentation-verified` is untouched. The card's ink read-back is
  an OS query about pixels the OS holds, not a photograph of a screen, and it says nothing about
  legibility, layout, clipping or DPI.
- **Linux input capture is unproven**, refuses in type, and the five-step gate in
  `InputPresenceFactory.LinuxManualGate` is undischarged. WSLg cannot discharge it, and **Wayland may
  never be able to**: unprompted activation is not offered by the protocol.
- **The foreground escalation is a real intervention.** `AttachThreadInput` briefly shares input
  state with another process's thread. It is bounded and detached in a `finally`, and it was measured
  necessary — but it is a divergence from WPF (D115) and it has not been exercised against a hung
  foreground thread.
- **Avalonia's own message loop is not proven to translate keystrokes for this window.** Every
  delivery fact here rides the presence's own `Pump`.
- **Nothing proves the card is the RIGHT card.** The phrase the OS is holding ink for is not compared
  against the phrase the module drew; ink is a count, not a string.
- **Concurrency is single-threaded.** The module's keystroke handling, its dot and its resolutions
  are exercised on one thread. Two threads racing a resolution against a disarm are not covered.
- **`Win32InputPresence` was not exercised against a second instance of itself.** One presence per
  process is the composition root's rule and nothing tests what two would do to one foreground.
- **The seven landed modules' facts are unchanged in SEMANTICS**, but four rack-order/refusal lists
  and two headless lists grew by one entry each, exactly as they did at SP-105, SP-106, SP-108 and
  SP-109. No landed assertion was relaxed, reworded to be weaker, or deleted.

---

## 10. Files changed

**New — `client/src/CcpClient.Desktop/Input/`:** `IInputPresence.cs`, `InputReasonCodes.cs`,
`Win32InputInterop.cs`, `Win32InputPresence.cs`, `UnsupportedInputPresence.cs`,
`InputPresenceFactory.cs`.

**New — effects/session/views:** `Effects/LockCardEffect.cs`, `Effects/LockCardSchedule.cs`,
`Effects/LockCardTyping.cs`, `Effects/LockCardPhrasePool.cs`, `Session/LockCardPresetDocument.cs`,
`Views/Pages/InputPanelNotices.cs`.

**Changed:** `Session/EffectReasonCodes.cs` (two additive codes), `Session/SessionParticipant.cs`
(the eighth module and the shared input presence), `Views/Pages/StudioPage.axaml` + `.axaml.cs` (the
GAMES & CARDS group, the row, the panel).

**New tests:** `InputWindowProbe.cs`, `InputCaptureObservations.cs`, `InputCapabilityTests.cs`,
`InputOverlayCoexistenceTests.cs`, `LockCardModuleTests.cs`.

**Changed tests:** `AudioModuleSpineTests.cs`, `ContinuousEffectSpineTests.cs`,
`SecondEffectSpineTests.cs` (rack-order and refusal lists grow by one),
`RealDesktopCollectionGuardTests.cs` (the helper census gains the three input helpers and the bound
controls gain the two new real-desktop classes — a STRENGTHENING),
`StudioRackHeadlessTests.cs` (the rack lists grow by one; four new facts).

**Docs:** `client/docs/verification-harness.md` (the input evidence class),
`client/docs/wpf-surface-reachability.md` (D110–D120).

**Never touched:** `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Audio/**`,
`client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`,
`ConditioningControlPanel/**`, `docs/constitution.md`.
