# The haptic limb, censused — every upstream command site, mapped to a port trigger point

Companion to `client/docs/wpf-surface-reachability.md` D202 @ 7527243e7-D206 (the divergences).
The method, reconciliation, and priced vocabulary menu are stated here. **No product code was
written for this document**; `client/src/**` was outside its scope.

The method is stated before the mapping below. This file is the DATA; the LOGIC that pins it is
`client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, which re-derives the candidate set from the
shipping bytes at test time and refuses to trust any total written here.

---

## 1. THE COUNT

**EIGHTEEN command sites in the `Services/{Flash,Video,Subliminal}` family, and they reach FIVE port
trigger points.**

| | |
|---|---|
| command sites | **18** |
| ...of which have a port trigger point | **10** (7 `collapsed`, 3 `present`) |
| ...of which have none | **8** (`absent-by-decision`, every one with a quoted port decision) |
| ...`absent-unexplained` | **0** |
| distinct port trigger points | **5** |
| candidate lines examined | **80** (18 command + 62 accounted-for non-commands) |
| files searched | **16** (the whole three-directory family, including `Services/Video/Browser/`) |
| command sites OUTSIDE the family, whole shipping tree | **31**, listed in §6 and NOT in the 18 |

The previous counts were **8**, then **13**, then **14**. Every correction came from widening the
search, never from reading the same lines harder — §5 reconciles all four numbers line by line.

**T3 fails on every single one of the ten mappable sites, for the same reason**: this build's
persisted haptic state is one boolean (`Haptics/HapticSettingsDocument.cs:47`, `Enabled`), and every
upstream site sends an intensity, a shape, a duration, a mode or a priority that has nowhere to come
from. That is the vocabulary gap, and pricing it is the record's §6. **This document does not choose
it.**

---

## 2. HOW A LINE BECOMES A SITE, AND HOW A VERDICT IS DECIDED

**Found** by grepping all 16 family files, case-insensitively, for ten needles that cannot mean
anything but haptics: `haptic`, `vibe`, `vibrat`, `buzz`, `lovense`, `buttplug`, `funscript`,
`setlayer`, `hapticlayer`, `toyinput`. That yields 80 lines, and every one of them is in a table
below. The needle set and the file universe live in the TEST, never in this document, so editing this
file cannot shrink the search.

A wider over-match sweep (`\btoy`, `pulse`) was also run: 35 further lines, none of which contains
any haptic reference. They are named in §7 and are deliberately NOT part of the pin, because a
`pulse` needle would red the guard on unrelated animation edits — a tolerance is the size of the
defect it hides, and this one would hide nothing while catching everything.

**A line is a `command` when all three hold.** C1: it is a call evaluated for effect whose target is
the haptic subsystem. C2: it flows outward — the app telling haptics to do something, not reading
from it. C3: its effect is a change in what a device is asked to do, directly or by handing a driving
program a script to play.

**Buckets** (exhaustive; every one of the 80 lines is in exactly one):

| bucket | rule |
|---|---|
| `command` | C1 and C2 and C3. **These are the sites.** |
| `adjacent` | participates in a command's execution without being one: a guard, the `suppressHaptic` opt-out and its plumbing, a call path into a helper that holds a command, the `catch`/log line of a command's own `try`. |
| `inbound` | fails C2: toy input in any form (declaration, subscribe, unsubscribe, teardown) and reads of haptic state. |
| `settings` | a read of haptic configuration. |
| `noise` | the line executes nothing about haptics: prose, or an unrelated identifier the needle happened to match. |

**Counting rule:** one site = **one syntactic call expression** into the haptic subsystem. Two
mutually exclusive branches are two sites (they are two lines somebody must port or refuse); whether
the port merges them is a verdict, never a subtraction. **A helper is not double counted**: where an
app-side helper holds the call, the call expression is the site and the helper's callers are
`adjacent` call paths. That rule keeps `FlashService.cs:1627` and `SubliminalService.cs:588` as the
sites rather than their one and four call paths, and it is applied identically to both.

### 2.1 THE HELPER CLAUSE IN THE COMMITTED PLAN SAID THE OPPOSITE, AND IT IS STRUCK HERE

**The number moved because a rule changed, and this section is where that is visible.** The earlier
rule read:

> *"A helper called from two places is **one site per call site of the helper**, and the helper's own
> body is not a site — otherwise the same command is counted twice."*

That is the reverse of the rule above. **Under the committed clause the count is 21, not 18**:
`FlashService.cs:1627` and `SubliminalService.cs:588` stop being sites and five call paths become
them — `FlashService.cs:1525` and `SubliminalService.cs:246`, `:265`, `:321`, `:396`.

**Why the plan's clause is struck rather than followed.** The same plan defines a command by **C1 —
"a method invocation whose target IS the haptic subsystem"**. All five of those call paths invoke an
app-side helper, not the haptic subsystem, so the helper clause admits as sites exactly the lines C1
excludes. **The plan carried two rules that contradict each other**, and this census resolves the
contradiction in favour of the definitional one, the same way and for the same reason it struck the
`adjacent` bucket's port-side clause (§6). C1 also keeps the enumeration comparable with the three
prior counts, all of which are call-expression counts: D202 cites `:1627` and `:588`, and no prior
enumeration has ever cited `:1525` or `:246`.

**What the plan's clause was tracking is not lost.** It was counting distinct MOMENTS, which is a
real thing a limb author needs: those five lines are five separate occasions on which upstream
reaches one command. Every one of them is in §5 as an `adjacent` row and each says which site it
reaches and that it is a distinct moment. **The count is of commands to write; the moments are in the
table beside them.**

**A port trigger point** is a specific existing statement in `client/src/CcpClient.Desktop/**`, cited
`File.cs:line`, at which the same user-observable moment occurs. Three tests: **T1** moment identity,
cited on both sides; **T2** reachability in a default build; **T3** no policy the port has not
already decided. Verdicts:

| verdict | meaning |
|---|---|
| `present` | T1 and T2 hold and exactly one port statement is the counterpart. |
| `collapsed` | N>1 upstream sites share ONE port statement because the port merged the branches. The notes name what distinction is lost. |
| `absent-by-decision` | no port counterpart, and §4 quotes the port decision that removed it and ties the quote to the moment. |
| `absent-unexplained` | no port counterpart and no nameable decision. **None occurred.** |

---

## 3. THE EIGHTEEN COMMAND SITES

Upstream paths are relative to `ConditioningControlPanel/`; port paths to
`client/src/CcpClient.Desktop/`. The `needle` columns are exact substrings that must still be at the
cited line; the pin checks both sides.

<!-- census:sites -->
| # | upstream | needle | verdict | port trigger | port needle | T3 | notes |
|---|---|---|---|---|---|---|---|
| 1 | `Services/Flash/FlashService.cs:1453` | `App.Haptics?.FlashDecayVibeAsync()` | collapsed | `Effects/FlashSurfacePresenter.cs:307` | `_surfaces.Place(slot, request, frame, draw.Lifetime)` | fail: intensity + ladder | A flash image appears. Upstream's LAYER arm; one of three mutually exclusive spawn arms in `SpawnFlashWindow` (`:1445`, `:1455`, `:1482`) that all call the same thing. |
| 2 | `Services/Flash/FlashService.cs:1480` | `App.Haptics?.FlashDecayVibeAsync()` | collapsed | `Effects/FlashSurfacePresenter.cs:307` | `_surfaces.Place(slot, request, frame, draw.Lifetime)` | fail: intensity + ladder | Same moment, upstream's shared-HOST arm. |
| 3 | `Services/Flash/FlashService.cs:1516` | `App.Haptics?.FlashDecayVibeAsync()` | collapsed | `Effects/FlashSurfacePresenter.cs:307` | `_surfaces.Place(slot, request, frame, draw.Lifetime)` | fail: intensity + ladder | Same moment, upstream's per-WINDOW arm. Nothing is lost by the collapse: all three arms call one method and the arm is a rendering choice, not a haptic one. |
| 4 | `Services/Flash/FlashService.cs:1627` | `haptics.SetLayer(Services.Haptics.Core.HapticLayer.Luminance,` | present | `Effects/FlashSurfacePresenter.cs:307` | `_surfaces.Place(slot, request, frame, draw.Lifetime)` | fail: latch + intensity | A flash image is on screen, for as long as it is. Shares the port statement with 1-3 but is a different command: a continuous layer at the image's average brightness, auto-zeroed after the flash's own lifetime. The port already holds the decoded BGRA bytes at this point (`Effects/FlashFrameSource.cs:111`, `:164`), so no second decode is needed. |
| 5 | `Services/Flash/FlashService.cs:1915` | `App.Haptics?.FlashClickVibeAsync()` | absent-by-decision | — | — | — | A flash is popped. Reached by THREE upstream routes, not one: the mouse click (`:1846`), the gaze pop (`:294`) and the layered-visual click (`:3632`). The port has no route of any kind. |
| 6 | `Services/Video/VideoService.cs:2580` | `App.Haptics?.StartVideoBackgroundVibeAsync()` | collapsed | `Effects/MandatoryVideoEffect.cs:287` | `_surface.Begin(firing.Path, firing.MaxLength, OnClipEnded)` | fail: latch + intensity | A clip starts playing. Upstream's LibVLC engine, the fallback one. |
| 7 | `Services/Video/VideoService.Browser.cs:452` | `App.Haptics?.StartVideoBackgroundVibeAsync()` | collapsed | `Effects/MandatoryVideoEffect.cs:287` | `_surface.Begin(firing.Path, firing.MaxLength, OnClipEnded)` | fail: latch + intensity | Same moment on upstream's DEFAULT engine, which `VideoService.cs:2407` routes to first. The port has one video path and no browser engine, so both engines' starts are one statement here. This is the site both prior counts missed. |
| 8 | `Services/Video/VideoService.cs:2584` | `App.Haptics?.FunScript?.OnVideoStarted(path)` | absent-by-decision | — | — | — | A clip starts, and a `.funscript` sidecar next to it should drive the toy for the clip's length. |
| 9 | `Services/Video/VideoService.Browser.cs:453` | `App.Haptics?.FunScript?.OnVideoStarted(_browserPath ?? "")` | absent-by-decision | — | — | — | Same, on the default engine. Not in any prior count. |
| 10 | `Services/Video/VideoService.cs:4585` | `App.Haptics?.VideoTargetHitAsync()` | absent-by-decision | — | — | — | An attention target is caught during a clip. |
| 11 | `Services/Video/VideoService.cs:4673` | `App.Haptics?.PostEvent(Services.Haptics.Core.HapticEventKind.ToyButtonReward)` | absent-by-decision | — | — | — | An attention check is satisfied by squeezing the toy instead of clicking. Absent twice over: the moment needs attention checks, and the reward needs toy INPUT. |
| 12 | `Services/Video/VideoService.cs:6580` | `App.Haptics?.StopVideoBackgroundVibeAsync()` | present | `Effects/MandatoryVideoEffect.cs:416` | `RefreshSchedule()` | fail: latch | The clip stops holding the screen; `:416` is the first statement of the `OnClipEnded` handler declared at `:414`, cited as a statement because §2 defines a trigger point as one. **The port's second teardown path, `Effects/MandatoryVideoEffect.cs:337` (`OnDisarmed`), does NOT reach `OnClipEnded`** — `Effects/VideoSurfacePresenter.cs:466` clears the callback — so a limb must place the stop on BOTH or it repeats upstream's D203 defect. |
| 13 | `Services/Video/VideoService.cs:6584` | `App.Haptics?.FunScript?.OnVideoStopped()` | absent-by-decision | — | — | — | The clip ends and any script following it must be dropped and its layer zeroed. |
| 14 | `Services/Subliminal/SubliminalService.cs:230` | `App.Haptics?.TriggerSubliminalPatternAsync(text)` | collapsed | `Effects/SubliminalsEffect.cs:209` | `_surface?.Show(firing.Card)` | fail: intensity + anticipation | A subliminal phrase is shown. Upstream's WITH-whisper-audio branch of `FlashSubliminal` (`:203`, guard `:220`). |
| 15 | `Services/Subliminal/SubliminalService.cs:588` | `App.Haptics?.TriggerSubliminalPatternAsync(text)` | collapsed | `Effects/SubliminalsEffect.cs:209` | `_surface?.Show(firing.Card)` | fail: intensity + anticipation | Same moment, upstream's silent branch, inside the helper `TriggerSubliminalWithHapticPattern` (`:577`). The port has no whisper audio, so the two branches are one path here. **What the collapse loses is the ANTICIPATION**: upstream fires the haptic first and delays the visual by `SubliminalAnticipationMs` (250 ms, or 1300 ms on Buttplug — `Services/Haptics/HapticService.cs:88`), and the port shows the card immediately. |
| 16 | `Services/Subliminal/SubliminalService.cs:297` | `App.Haptics?.TriggerSubliminalPatternAsync(text)` | absent-by-decision | — | — | — | The "Bambi Freeze" trigger phrase is shown. |
| 17 | `Services/Subliminal/SubliminalService.cs:387` | `App.Haptics?.TriggerSubliminalPatternAsync(resetText)` | absent-by-decision | — | — | — | The "Bambi Reset" follow-up is shown. |
| 18 | `Services/Subliminal/BouncingTextService.cs:515` | `App.Haptics?.BouncingTextBounceAsync()` | present | `Effects/BouncingTextField.cs:230` | `Bounces++` | fail: intensity | A bouncing word hits a screen edge. **In a module this port shipped**, and in no prior count. The port's statement even sits at the same place in the sequence: upstream fires between the bounce bookkeeping and the 10 % text re-roll (`:515` against `:518`), and `:230` sits between `Bounces++` and the same re-roll at `Effects/BouncingTextField.cs:251`. |
<!-- /census:sites -->

### 3.1 The five port trigger points, gathered

| port statement | serves sites | what a limb would do there |
|---|---|---|
| `Effects/FlashSurfacePresenter.cs:307` | 1, 2, 3, 4 | one decay ladder AND one luminance layer per placed image |
| `Effects/MandatoryVideoEffect.cs:287` | 6, 7 | raise the continuous video layer |
| `Effects/MandatoryVideoEffect.cs:416` (+ `:337`) | 12 | zero it, on every path that takes the clip off the screen |
| `Effects/SubliminalsEffect.cs:209` | 14, 15 | one short pulse per card |
| `Effects/BouncingTextField.cs:230` | 18 | one 60 ms pulse per bounce |

---

## 4. THE EIGHT ABSENCES, EACH WITH THE PORT DECISION QUOTED

Every row quotes the decision's own words from the cited port line and ties the quote to the site's
moment. A decision whose words are about a different moment would not be `absent-by-decision`; it
would be `absent-unexplained`, and there are none.

<!-- census:decisions -->
| site | decision | quote |
|---|---|---|
| 5 | `Effects/FlashSurfacePresenter.cs:303` | `serve pop / hydra / XP mechanics this port does not have` |
| 8 | `Haptics/IHapticSink.cs:232` | `a script player this port has not ported at all` |
| 9 | `Haptics/IHapticSink.cs:232` | `a script player this port has not ported at all` |
| 10 | `Effects/MandatoryVideoEffect.cs:405` | `not ported and not shown as dead controls: attention` |
| 11 | `Effects/MandatoryVideoEffect.cs:405` | `not ported and not shown as dead controls: attention` |
| 11 | `Haptics/IHapticSink.cs:235` | `haptic INPUT` |
| 13 | `Haptics/IHapticSink.cs:232` | `a script player this port has not ported at all` |
| 16 | `Effects/SubliminalsEffect.cs:56` | `follow-up (<c>:276-404</c>)` |
| 17 | `Effects/SubliminalsEffect.cs:56` | `follow-up (<c>:276-404</c>)` |
<!-- /census:decisions -->

**Site 5 — the flash pop.** The quoted sentence is the reason the port's flash surfaces are created
`ClickThrough: true` unconditionally (`Effects/FlashSurfacePresenter.cs:306`): a surface that caught
clicks it does nothing with would swallow the user's input. **The moment this site fires on is a
flash being popped**, and with every flash surface click-through there is no pop. **The quote covers all three
upstream routes, because it names the MECHANIC rather than the input**: `pop / hydra / XP mechanics
this port does not have` is as true of the gaze pop (`FlashService.cs:294`, which calls
`OnFlashClicked(..., fromGaze: true)`) and of the layered-visual click (`:3632`) as of the mouse. The
port has no gaze subsystem anywhere, but that is a whole-tree absence rather than a decision written
about this moment, so it is stated here and not cited as one.

**Sites 8, 9, 13 — the funscript player.** The quoted words are about `App.Haptics.FunScript`
specifically, which is the exact member all three sites call. **The moment is a clip starting or
stopping while a `.funscript` sidecar exists**, and with no script player there is nothing to start or
stop. Note that these three are counted as commands, not as adjacency: C3 admits "handing a driving
program a script to play", and being unported is a reason for a VERDICT, never a reason to leave a row
out of the count.

**Sites 10, 11 — the attention checks.** The quoted sentence names attention checks first in its list
of what the video row does not port. **Both moments are attention-check outcomes** — a target caught
(10) and a check satisfied by a toy button (11) — so the quote covers them exactly. Site 11 needs a
second decision because even with attention checks ported, the reward is triggered by the toy driving
the app, and the port's seam carries no inbound verb at all.

**Sites 16, 17 — Bambi Freeze and Bambi Reset.** The quoted words name the follow-up and cite
`SubliminalService.cs:276-404`, the range that contains both sites (`:297` inside `TriggerBambiFreeze`,
`:387` inside `ScheduleBambiReset`). **The moments are those two phrases being shown**, and neither
phrase is shown by this port.

---

## 5. THE OTHER SIXTY-TWO LINES, AND WHY EACH IS NOT A SITE

<!-- census:accounted -->
| upstream | needle | bucket | why |
|---|---|---|---|
| `Services/Flash/FlashService.cs:287` | `haptic` | noise | prose: a doc comment about `GazePop` |
| `Services/Flash/FlashService.cs:405` | `suppressHaptic` | adjacent | the per-effect opt-out, entering the public entry point |
| `Services/Flash/FlashService.cs:433` | `suppressHaptic` | adjacent | opt-out plumbed to the loader |
| `Services/Flash/FlashService.cs:443` | `suppressHaptic` | adjacent | opt-out on the single-image entry point |
| `Services/Flash/FlashService.cs:453` | `suppressHaptic` | adjacent | opt-out forwarded |
| `Services/Flash/FlashService.cs:475` | `suppressHaptic` | adjacent | opt-out plumbed to the loader |
| `Services/Flash/FlashService.cs:478` | `suppressHaptic` | adjacent | opt-out on the loader signature |
| `Services/Flash/FlashService.cs:500` | `suppressHaptic` | adjacent | opt-out forwarded to ShowImages |
| `Services/Flash/FlashService.cs:581` | `suppressHaptic` | adjacent | opt-out on the loader signature |
| `Services/Flash/FlashService.cs:636` | `suppressHaptic` | adjacent | opt-out forwarded to ShowImages |
| `Services/Flash/FlashService.cs:1020` | `suppressHaptic` | adjacent | opt-out on ShowImages |
| `Services/Flash/FlashService.cs:1117` | `suppressHaptic` | adjacent | opt-out forwarded per window |
| `Services/Flash/FlashService.cs:1124` | `suppressHaptic` | adjacent | opt-out captured for the staggered spawn |
| `Services/Flash/FlashService.cs:1133` | `suppressHaptic` | adjacent | captured opt-out forwarded |
| `Services/Flash/FlashService.cs:1164` | `suppressHaptic` | adjacent | opt-out on SpawnFlashWindow |
| `Services/Flash/FlashService.cs:1452` | `suppressHaptic` | adjacent | the guard on site 1 |
| `Services/Flash/FlashService.cs:1479` | `suppressHaptic` | adjacent | the guard on site 2 |
| `Services/Flash/FlashService.cs:1515` | `suppressHaptic` | adjacent | the guard on site 3 |
| `Services/Flash/FlashService.cs:1525` | `suppressHaptic` | adjacent | the guard on site 4 and its only call path into ApplyLuminanceSync. **A distinct moment, and the site itself under the struck helper clause (§2.1)** |
| `Services/Flash/FlashService.cs:1602` | `haptic` | settings | reads the service handle |
| `Services/Flash/FlashService.cs:1603` | `haptic` | settings | reads LuminanceSyncEnabled, default false |
| `Services/Flash/FlashService.cs:1606` | `haptic` | settings | reads LuminanceSyncIntensity |
| `Services/Video/Browser/BrowserVideoSurface.cs:26` | `setlayer` | noise | `SetLayeredWindowAttributes`, a Win32 call with no haptic meaning |
| `Services/Video/Browser/BrowserVideoWindow.cs:21` | `setlayer` | noise | the same false positive |
| `Services/Video/VideoService.Browser.cs:454` | `funscript` | adjacent | the catch and log of site 9's own try |
| `Services/Video/VideoService.cs:383` | `funscript` | noise | prose about the page's time messages |
| `Services/Video/VideoService.cs:2581` | `funscript` | noise | prose introducing site 8 |
| `Services/Video/VideoService.cs:2585` | `funscript` | adjacent | the catch and log of site 8's own try |
| `Services/Video/VideoService.cs:4561` | `haptic` | inbound | declares the toy-button handler |
| `Services/Video/VideoService.cs:4562` | `toyinput` | inbound | declares the unhook local function |
| `Services/Video/VideoService.cs:4567` | `toyinput` | inbound | unsubscribes from toy input |
| `Services/Video/VideoService.cs:4583` | `toyinput` | inbound | unhooks when a click wins the spawn |
| `Services/Video/VideoService.cs:4649` | `haptic` | settings | null-checks the service before arming |
| `Services/Video/VideoService.cs:4650` | `haptic` | settings | reads AttentionCheckToyButton |
| `Services/Video/VideoService.cs:4651` | `haptic` | settings | reads ToyInputEnabled |
| `Services/Video/VideoService.cs:4655` | `toyinput` | noise | prose about dispatcher marshalling |
| `Services/Video/VideoService.cs:4667` | `toyinput` | inbound | unhooks when the spawn is gone |
| `Services/Video/VideoService.cs:4680` | `toyinput` | inbound | subscribes to toy input |
| `Services/Video/VideoService.cs:4691` | `toyinput` | inbound | hands the unhook to the spawn record |
| `Services/Video/VideoService.cs:6579` | `haptic` | noise | prose introducing site 12 |
| `Services/Video/VideoService.cs:6581` | `funscript` | noise | prose introducing site 13, and the claim D203 refutes |
| `Services/Video/VideoService.cs:6585` | `funscript` | adjacent | the catch and log of site 13's own try |
| `Services/Video/VideoService.cs:6708` | `funscript` | noise | prose in the browser-parity checklist |
| `Services/Subliminal/BouncingTextService.cs:514` | `haptic` | noise | prose introducing site 18 |
| `Services/Subliminal/SubliminalService.cs:227` | `haptic` | noise | prose introducing site 14 |
| `Services/Subliminal/SubliminalService.cs:246` | `haptic` | adjacent | a call path into the helper that holds site 15. **A distinct moment — FlashSubliminal's silent branch — and a site under the struck helper clause (§2.1)** |
| `Services/Subliminal/SubliminalService.cs:258` | `suppressHaptic` | adjacent | the opt-out on FlashSubliminalCustom |
| `Services/Subliminal/SubliminalService.cs:265` | `suppressHaptic` | adjacent | a call path into the helper that holds site 15. **A distinct moment — the FlashSubliminalCustom entry point — and a site under the struck helper clause (§2.1)** |
| `Services/Subliminal/SubliminalService.cs:294` | `haptic` | noise | prose introducing site 16 |
| `Services/Subliminal/SubliminalService.cs:320` | `haptic` | noise | prose on the freeze silent branch |
| `Services/Subliminal/SubliminalService.cs:321` | `haptic` | adjacent | a call path into the helper that holds site 15. **A distinct moment — Bambi Freeze's silent branch — and a site under the struck helper clause (§2.1)** |
| `Services/Subliminal/SubliminalService.cs:384` | `haptic` | noise | prose introducing site 17 |
| `Services/Subliminal/SubliminalService.cs:396` | `haptic` | adjacent | a call path into the helper that holds site 15. **A distinct moment — Bambi Reset's silent branch — and a site under the struck helper clause (§2.1)** |
| `Services/Subliminal/SubliminalService.cs:573` | `haptic` | noise | prose on the helper |
| `Services/Subliminal/SubliminalService.cs:575` | `buttplug` | noise | prose naming the 1.3 s provider latency |
| `Services/Subliminal/SubliminalService.cs:577` | `suppressHaptic` | adjacent | the helper declaration that holds site 15 |
| `Services/Subliminal/SubliminalService.cs:581` | `haptic` | noise | prose on the anticipation delay |
| `Services/Subliminal/SubliminalService.cs:582` | `haptic` | noise | prose on the suppressed case |
| `Services/Subliminal/SubliminalService.cs:584` | `haptic` | settings | reads SubliminalAnticipationMs |
| `Services/Subliminal/SubliminalService.cs:586` | `haptic` | noise | prose introducing site 15 |
| `Services/Subliminal/SubliminalService.cs:587` | `suppressHaptic` | adjacent | the guard on site 15 |
| `Services/Subliminal/SubliminalService.cs:599` | `haptic` | adjacent | the catch and log of site 15's own try |
<!-- /census:accounted -->

---

## 6. RECONCILIATION: 8, then 13, then 14, then 18

| claim | where | verdict against this census |
|---|---|---|
| **8 sites in three modules**, citing `Services/SubliminalService.cs:230` | the haptics plan §5, three source comments, D202's first draft | **wrong twice**: the path has no `Subliminal/` directory segment and the file drives four sites, not one |
| **13 sites**, three files | `client/docs/wpf-surface-reachability.md` D202 @ 7527243e7, `Haptics/IHapticSink.cs:210-215` | **correct for the three files it names, under a rule that excluded FunScript.** Misses three files' worth of sites |
| **14 sites**, adding `VideoService.Browser.cs:452` | this packet's brief | **correct as far as it goes.** Still misses `BouncingTextService.cs:515` and `VideoService.Browser.cs:453` |
| **18 sites** | this census | derived from the bytes by §2 |
| **21 sites** | **this packet's own committed plan**, `plan.md:140-141` | what the census's helper clause would yield if the plan's opposite clause governed instead. It does not, and §2.1 says why and lists the five lines that move |

Line by line, from 13 to 18:

| # | line | why it was not in the thirteen |
|---|---|---|
| +1 | `VideoService.Browser.cs:452` | the file was outside the searched set |
| +2 | `VideoService.Browser.cs:453` | the same file, and it is a FunScript site as well |
| +3 | `Subliminal/BouncingTextService.cs:515` | the file was outside the searched set. **The module is ported** |
| +4 | `VideoService.cs:2584` | counted as ADJACENT under the older rule because the port has no script player |
| +5 | `VideoService.cs:6584` | the same |

**The rule change is stated rather than smuggled.** The older enumeration excluded the three FunScript
calls because the port does not have a script player. That is a port-side fact being used to shrink an
UPSTREAM count, and it contradicts the census's own rule that unportedness decides a verdict and never
membership. Under C3 as committed — "directly, or by handing a driving program a script to play" —
they are commands with an `absent-by-decision` verdict. **A reader who wants the older figure can take
it directly: 15 sites command the haptic service itself, and 3 more command it through the script
player.**

**One citation SHIFT, named so it does not read as a correction.** The luminance site is cited here at
`FlashService.cs:1627`, the `SetLayer` call itself, exactly as D202 cites it. Its guard and only call
path is `:1525`, which is the line carrying the `suppressHaptic` test, and a limb author wanting the
guard as well as the command wants both numbers. Both are in this document: `:1627` as site 4,
`:1525` as its `adjacent` row.

### 6.1 Thirty-one command sites OUTSIDE the family — reported, not counted, not mapped

The same alphabet over the whole shipping tree (excluding `CCP.*`, `Tests/**` and
`Services/Haptics/**` itself) finds **31 further call-shaped haptic sites**. They are out of this
packet's scope and are listed so that the next enumeration does not have to rediscover them. Several
drive modules this port HAS:

`Services/BubbleService.cs:979` (bubble pop — already declared unported at
`Effects/BubblePopSurfacePresenter.cs:81-83`, *"XP, the lucky roll, achievements, haptics and Discord
presence"*); `Services/KeywordTriggerService.cs:1541`, `:1806`;
`Services/Progression/AchievementService.cs:715`, `:894`; `Services/Progression/ProgressionService.cs:229`;
`Services/Progression/QuestService.cs:1215`; `Services/Companion/CompanionService.cs:290`;
`Services/RemoteControlService.cs:822`, `:1142`; `Services/BlinkTrainerService.cs:253`;
`Services/Deeper/EnhancementEngine.cs:490`; `Services/Deeper/IActionDispatcher.cs:589`, `:641`, `:657`;
`Services/Commands/HapticCommand.cs:24`; `AvatarTube/AvatarTubeWindow.Speech.cs:1816`;
`Lab/GazeMinigame/GazeMinigameWindow.xaml.cs:1271`; and the UI-side calls on the haptics panel,
the setup window, the Deeper editor and the panic paths.

**A limb built only from the family therefore covers the effects modules and nothing else.** That is
the correct scope for the next packet, and this line exists so nobody later reads "eighteen" as "all".

---

## 7. WHAT THIS CENSUS DOES NOT COVER

Three residuals, named because the method provably cannot reach them:

1. **The over-match sweep.** `\btoy` and `pulse` match 35 further lines across the family — WPF glow
   pulses (`FlashService.cs:1360`, `:1421`, `:1703`, `:1776`), Autonomy's wallpaper pulse
   (`WallpaperService.cs:51`, `:155`, `:261`, `:530-532`), prose about the toy-button path
   (`AttentionTargets.cs:34`, `:66`, `:480`, `VideoService.cs:2583`, `:4463`, `:4557`, `:4647`,
   `:4669`, `:4677`, `:7553`), and the bouncing-text breathing comment
   (`BouncingTextService.cs:533`). Not one contains a haptic reference. They are not pinned, for the
   reason in §2.
2. **The reverse coupling.** `Services/Haptics/FunScriptService.cs:220-222` subscribes to
   `App.Video.PrimaryPlaybackTimeMsChanged`, so the video module drives haptics from a raise site that
   carries no needle at all and lives in a directory this census excludes. It is a haptic consumer of
   a video event, not a video command of haptics, and no grep over the family can find it.
3. **Payload-driven dispatch.** `Services/Deeper/IActionDispatcher.cs:619-657` executes haptic actions
   whose trigger is DTRH payload JSON rather than any `.cs` line. A census over C# cannot enumerate
   what a data file asks for.

---

## 8. THREE UPSTREAM FACTS A LIMB MUST NOT COPY OR CORRECT

Full statements are D203, D204 and D205 in `client/docs/wpf-surface-reachability.md`. In one line
each:

- **The video layer's STOP is on one teardown path out of several** and the panic key is not one of
  them, so panic-keying a clip leaves the toy humming — while the comment sitting between
  `VideoService.cs:6580` and `:6584` asserts the opposite: *"Runs on every teardown path (natural
  end, skip, panic, attention retry) because CloseAll is the single funnel for all of them"*
  (`:6581-6583`), and both stops are BESIDE `CloseAll(...)` rather than inside it. The port must
  place its stop on every path (site 12's notes). **Do not copy the bug.**
- **The activity readout fires when nothing can vibrate**, because the gate refuses centrally inside
  the mixer and `Announce` runs after `Play` unconditionally. **Do not "fix" it** with a
  connected-check at a call site: that would change user-observable behaviour.
- **The decay ladder's own comment is wrong about its length.** `HapticService.cs:817` says "~2s";
  the arithmetic at `:838-843` spans 3503 ms in the default mode and never less than 3308 ms in any
  mode. **Code wins**; a port copies 3503 ms, not the comment.

---

## 9. THE VOCABULARY LAYER, PRICED — SUMMARY ONLY

Full pricing on seven fixed axes is summarized below.
**The decision is the owner's and this packet does not take it.**

| option | seam | files | reproduces | does NOT reproduce |
|---|---|---|---|---|
| **A. Per-module literals** — each module sends a level it computes itself | unchanged, wrapped by nothing | 0 new, ~4 edits | nothing; the 0.06 floor and 0.70 cap only by duplicating them per module | ladder, latch, peak-of-sum, arbitration, and it puts device-key selection in four places |
| **B. A port-side envelope+layer mixer** — one 10 Hz loop, layers by MAX, transients over them, peak-of-sum within a priority group, concurrency cap, floor and cap | unchanged; the mixer becomes the sink's only caller | 4-6 new, ~4 edits | **all five named behaviours, plus peak-of-sum and the concurrency cap** | — |
| **C. A thin command vocabulary, no loop** — `Pulse(level,ms)` / `Layer(level,autoZeroMs)`, each source scheduled independently, MAX at the send boundary | unchanged; wrapped | 2 new, ~4 edits | decay ladder, auto-zero latch, soft ramp (as a scheduled shape), 0.06 floor, 0.70 cap | **peak-of-sum within a priority group** and the concurrency cap. Priority arbitration across groups is MAX either way |
| **C+. C plus a shared-instant evaluator, still no loop** | unchanged; wrapped | 3 new, ~4 edits | everything C does, **plus peak-of-sum and the cap** | nothing the reachable sites ask for |
| **D. Build nothing yet** | untouched | 0 | nothing | everything — and the panel already says so in words |

**What C's one real loss costs, in numbers rather than adjectives.** Upstream sums transients WITHIN
a priority group and takes the max ACROSS groups (`Services/Haptics/Core/HapticMixer.cs:476-502`,
combined with the continuous floor by `Math.Max` at `:506`). The flash decay ladder posts at
**priority 1** (`HapticService.cs:842`) and the subliminal pulse posts at **priority 1**
(`:880`) — sites 1-3 and 14-15, all reachable here — so a flash overlapping a subliminal sums
upstream and would MAX under C. **Worked at the shipped defaults** (master intensity 0.7,
`Models/HapticSettings.cs:29`; cap 0.70, `HapticMixer.cs:77`): two overlapping 0.5 transients sum to
1.0, scale to 0.70 and hit the cap, while MAX gives 0.5, scales to **0.35** and does not.
**A factor of two on the level a user feels.**
Bounces post at priority 0 (`:821`) and sum with each other the same way. **Flash overlapping flash
does NOT sum even upstream**: `PlayDecayLadder` cancels any running flash ladder first
(`:775-776`).

**Recommended: C**, with C+ named as the upgrade that buys the one reachable loss without a poll; the
full reasoning, including why the 10 Hz loop's own safety properties belong to the provider rather
than to the limb, is in the record. Every option leaves `Haptics/IHapticSink.cs` byte-identical,
which is the haptic seam's own claim being cashed: a limb is a layer above the sink, never a change
to it.
