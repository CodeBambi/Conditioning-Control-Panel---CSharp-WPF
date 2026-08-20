# SP-120 — record

Branch `lane/SP-120-haptic-limb-census`, base `4276249e`, merged up to `b5f789de`.
Floor: pin **2247 unit / 141 headless**; observed **2260 unit / 141 headless**; declared
**+13 unit / +0 headless** (`floor-delta.json`). 2247 + 13 = 2260 and 141 + 0 = 141, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-120-haptic-limb-census`. The floor run
therefore REPORTS a violation against the pin, which is the expected shape: the orchestrator sums the
deltas and applies one bump. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened.
Build: 0 errors, 0 warnings (`check-warnings.mjs`, forced non-incremental, 4 projects).
`client/tests/floor/floor.json` was never opened.

**The base was red and is now clear; both gates were re-run after the merge — see §9.**
`feat/crossplatform` was merged into this lane at `b5f789de`, which unreds
`FloorWrapperGuardTests.PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin`. The numbers
above are from the post-merge run: **zero test failures** (`Failed: 0, Passed: 2258, Skipped: 2,
Total: 2260` for the unit project; `Failed: 0, Passed: 141` for the headless one), and the only thing
`check-floor.mjs` still reports is the expected total drift of 2260 against a pin of 2247 — which is
2247 + 13 and is this packet's declared delta, not a failure. **Both projects run in the one invocation**:
`check-floor.mjs:359-386` catches a drifting project and continues, and the run's own results
directory holds two TRX files — `CcpClient.HeadlessTests/results.trx` reads
`total="141" passed="141" failed="0"` and `CcpClient.Tests/results.trx` reads
`total="2260" passed="2258" failed="0"` with the two pre-existing skips. (An earlier draft of this
record said the script stops at the first drift. It does not, and the TRX pair is the evidence.)

> **The method was committed before the first mapping**, at
> `spine-tasks/SP-120-haptic-limb-census/plan.md`, commit `9237ba1b`, and the coordinator approved it
> before any measurement was taken. SP-116 committed its protocol first, SP-117 made that the standard,
> and SP-118 and SP-119 followed it.

**No product code was written.** `git diff --stat 4276249e..HEAD` over `client/src`, `client/tools`,
`ConditioningControlPanel`, `client/tests/floor`, `client/tests/CcpClient.HeadlessTests`,
`client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/docs/task-board.md`,
`client/docs/port-digest.md`, `client/docs/verification-harness.md`, `docs/constitution.md`, `.spine`
and `.claude` is **empty**. Three files changed in total: the census (new), the pin (new), and four
divergence rows plus one in-place correction in the ledger.

---

## 0. THE HEADLINE — it is not fourteen, it is EIGHTEEN, and the fourth correction has the same cause as the first three

| # | claim | where | what it missed |
|---|---|---|---|
| 8 | "eight sites in three modules" | SP-119 plan §5, three source comments | cited `Services/SubliminalService.cs:230`, **a path that does not exist**; that file drives four sites |
| 13 | thirteen, three files | D202, `Haptics/IHapticSink.cs:210-215` | three files' worth of sites, and three FunScript commands |
| 14 | fourteen, adding `VideoService.Browser.cs:452` | this packet's brief | `VideoService.Browser.cs:453`, `BouncingTextService.cs:516`, and the same three FunScript commands |
| **18** | **this census** | `client/docs/haptic-limb-census.md` | — |
| 21 | **this packet's own committed plan** | `plan.md:140-141` | nothing — it counts the same commands by a different unit. It is what the count would be if the plan's helper clause governed instead of the census's, and §1 below strikes it and says why |

**Not every step came from a wider search, and the two rulings that did not are struck-and-stated
below** (§1). **Three** of the eighteen exist because I struck the `adjacent` bucket's port-side
clause (the FunScript commands), and **two more sit on the lines they do because of a helper clause
that reverses my own committed plan** — under the plan as committed, those two lines are not sites,
five call paths are, and **the number is 21**. That is in the table above rather than in a footnote,
and in the owner-facing census as its own section.

**Every correction to the SEARCH so far, including mine, came from widening it rather than from
reading the same lines harder.** Eight became thirteen by reading one file properly; thirteen became fourteen by
adding one file; fourteen became eighteen by searching three DIRECTORIES instead of a list of files,
which is the only version of the search that cannot miss a file nobody thought to name. The
coordinator's mid-task correction is the proof that the file-list method was still failing while this
packet was running: **`Services/Video/Browser/` holds five more `.cs` files** and my own plan's table
said eleven files when the family is **sixteen**.

**The two additions that matter more than the arithmetic:**

1. **`Services/Subliminal/BouncingTextService.cs:516` drives a module this port SHIPPED** (SP-115,
   wave 55). Every other missing site was in a module the port does not have or on an engine it does
   not run. This one has a live port trigger point — `Effects/BouncingTextField.cs:223` — sitting at
   the same place in the same sequence.
2. **`Services/Video/VideoService.Browser.cs:452` is on the DEFAULT video engine.** `VideoService.cs:2407`
   routes to the browser first and the comment two lines up says *"this engine is the default
   (BrowserVideoEngineEnabled ships true)"* (`:2400-2401`). A limb built from the thirteen would have
   started the video layer only on the engine most installs never use.

**And the shape of the answer is more useful than the count: 18 upstream sites reach only FIVE port
statements.** Ten sites map (7 `collapsed`, 3 `present`), eight do not (`absent-by-decision`, every
one with a port decision quoted from its own line), and `absent-unexplained` is **zero**.

**T3 fails at all ten.** Not for ten different reasons: for one. This build's persisted haptic state
is a single boolean (`Haptics/HapticSettingsDocument.cs:47`), and every site sends an intensity, a
shape, a duration, a mode or a priority that has nowhere to come from. That is the vocabulary
question, and §6 prices it without answering it.

---

## 1. THE METHOD, AND THE ONE RULING THAT MOVES THE NUMBER BY THREE

Full method in `plan.md`; the parts that decided the count:

**The universe is three DIRECTORIES, searched recursively — sixteen files.** Not the three named
files, and not the eleven my own plan listed. The coordinator's ruling ("`including` names an
inclusion, not a boundary") is what the census implements, and the pin implements the same rule: the
test walks `Services/{Flash,Video,Subliminal}` with `SearchOption.AllDirectories`, so a file added to
`Services/Video/Browser/` tomorrow is searched without anybody remembering to add it.

**The alphabet is ten needles that cannot mean anything else**, matched case-insensitively, giving
**80 candidate lines**. Every one is in the census in exactly one bucket. The over-match sweep
(`\btoy`, `pulse`) was run as the plan promised: 35 more lines, **not one containing a haptic
reference** (WPF glow pulses, Autonomy's wallpaper pulse, prose about the toy-button path). They are
named in the census §7 and deliberately not pinned, because a `pulse` needle would red the guard on
unrelated animation edits while catching nothing.

**The ruling the review asked for (item 5): C3 governs, and the `adjacent` bucket's port-side clause
is STRUCK.** My plan defined a command as a call that changes what a device is asked to do
"**directly, or by handing a driving program a script to play**", and then contradicted itself by
letting `adjacent` absorb "a command into a subsystem the port has not ported at all". The second
clause uses a PORT fact to shrink an UPSTREAM count, which is exactly what §1 of the same plan
forbids. So the three FunScript calls (`VideoService.cs:2584`, `:6584`,
`VideoService.Browser.cs:453`) are **commands with an `absent-by-decision` verdict**, and `adjacent`
was redefined port-independently as "participates in a command's execution without being one" — the
guards, the `suppressHaptic` plumbing, the helper call paths, the `catch`/log of a command's own
`try`. **A reader who wants the older figure can take it from the same table: fifteen sites command
the haptic service directly and three command it through the script player.**

**THE SECOND RULING, AND IT REVERSES MY OWN COMMITTED PLAN. This is the disclosure the first draft
of this record owed and did not make.** The census counts a helper's call expression as the site and
its callers as `adjacent` call paths. **`plan.md:140-141`, committed before I mapped anything, says
the exact opposite**:

> *"A helper called from two places is **one site per call site of the helper**, and the helper's own
> body is not a site — otherwise the same command is counted twice."*

**Under the rule I committed, the headline is 21, not 18.** `FlashService.cs:1627` and
`SubliminalService.cs:588` stop being sites, and five call paths become them: `FlashService.cs:1525`
and `SubliminalService.cs:246`, `:265`, `:321`, `:396`. The first draft named 21 as "the alternative
reading" and said the census rule was "applied identically in both places" — both true, and beside
the point. **A reader has to be able to see that the number moved because a rule changed, and when.**
It is now in the owner-facing document as its own section (`client/docs/haptic-limb-census.md` §2.1)
and as a row in the reconciliation table beside 8, 13 and 14.

**Why the plan's clause is struck rather than followed, and it is the same ground as the FunScript
ruling.** The same plan defines a command by **C1: "a method invocation whose target IS the haptic
subsystem"**. All five of those call paths invoke an app-side helper — `ApplyLuminanceSync`,
`TriggerSubliminalWithHapticPattern` — and not the haptic subsystem, so the helper clause admits as
sites exactly the lines C1 excludes. **The plan carried two rules that contradict each other**, and
the census resolves that in favour of the definitional one, exactly as it struck the `adjacent`
bucket's port-side clause for contradicting §1. C1 also keeps the enumeration comparable with the
three prior counts, which are all call-expression counts: D202 cites `:1627` and `:588`, and no prior
enumeration has ever cited `:1525` or `:246`.

**The objection I owe an answer to**, because it is the reasoning I used to keep sites 1-3 apart:
"two lines somebody must port or refuse" is exactly what the plan's clause was tracking. The
difference is what the two rules MEASURE. Sites 1-3 are three call expressions INTO the haptic
subsystem, so C1 admits all three; the five call paths are not. What the plan's clause was really
counting is distinct MOMENTS, and a limb author needs those too — so all five are `adjacent` rows in
the census and **each now names the moment it carries** (FlashSubliminal's silent branch, the
FlashSubliminalCustom entry, Bambi Freeze's and Bambi Reset's silent branches, and the luminance
guard). **The count is of commands to write; the moments are in the table beside them, not lost.**

**Two bucket definitions were widened, and the widening is stated rather than smuggled.** `settings`
covers reads of haptic configuration only; the `suppressHaptic` opt-out moved into `adjacent` because
calling it "noise" would be false. `noise` means "the line executes nothing about haptics" — prose, or
an unrelated identifier — because 19 of the 80 lines are comments about haptics and they are not
sites.

---

## 2. THE MAPPING, IN ONE TABLE

Full rows, citations and quoted decisions: `client/docs/haptic-limb-census.md` §3, §4, §5.

| port statement | upstream sites it serves | what the limb would do |
|---|---|---|
| `Effects/FlashSurfacePresenter.cs:298` | `FlashService.cs:1453`, `:1480`, `:1516` (collapsed), `:1627` (present) | one decay ladder AND one luminance layer per placed image |
| `Effects/MandatoryVideoEffect.cs:279` | `VideoService.cs:2580` + `VideoService.Browser.cs:452` (collapsed) | raise the continuous video layer |
| `Effects/MandatoryVideoEffect.cs:372` (+ `:299`) | `VideoService.cs:6580` (present) | zero it on **every** path that takes the clip off the screen |
| `Effects/SubliminalsEffect.cs:197` | `SubliminalService.cs:230` + `:588` (collapsed) | one short pulse per card |
| `Effects/BouncingTextField.cs:223` | `BouncingTextService.cs:516` (present) | one 60 ms pulse per bounce |
| — | `FlashService.cs:1915`; `VideoService.cs:2584`, `:4585`, `:4673`, `:6584`; `VideoService.Browser.cs:453`; `SubliminalService.cs:297`, `:387` | nothing: eight absences, each with a quoted port decision |

**Three collapses lose something and the census says what** (`present`/`collapsed` is not a synonym
for "free"):

- **Flash arms (1-3):** nothing is lost. Three rendering branches call one method.
- **Video engines (6, 7):** nothing behavioural. The port has one video path because
  `Effects/MandatoryVideoEffect.cs:365` declares the browser engine unported.
- **Subliminal branches (14, 15):** **the anticipation is lost.** Upstream fires the haptic first and
  delays the visual by `SubliminalAnticipationMs` — 250 ms, or **1300 ms** on Buttplug
  (`HapticService.cs:88`) — so the buzz lands before the word. The port shows the card immediately,
  and `Effects/SubliminalsEffect.cs:55` already declares "the haptic anticipation delay" unported. A
  limb at `:197` therefore reproduces the pulse and not the anticipation, and **that is a
  user-observable difference on the one provider with 1.3 s of latency.**

**The absence I expected to find and did not:** `absent-unexplained` is zero. Every one of the eight
absences had a port decision already written, in words, in a file — which is what "declared rather
than stubbed" buys a later packet.

---

## 3. THE THREE PREMISES IN THE BRIEF: two confirmed, one confirmed with a wrong citation

**(a) Three sites have no trigger by prior decision — CONFIRMED, and it is eight, not three.**
`VideoService.cs:4585`/`:4673` (attention checks) and `FlashService.cs:1915` (the flash click) are all
absent as the brief says. The brief's citations verify exactly:
`Effects/MandatoryVideoEffect.cs:363-365` and `Effects/FlashSurfacePresenter.cs:293-297` say what it
says they say. **Two things the brief did not have:**

- The flash click is reached by **three** upstream routes, not one — the mouse (`:1846`), the gaze pop
  (`:294`, `GazePop` → `OnFlashClicked(..., fromGaze: true)`) and the layered-visual click (`:3632`) —
  so the click-through decision alone does not cover it. The gaze route needs its own decision, and
  the port has one: `Effects/BubblePopSurfacePresenter.cs:86` lists "the gaze-pop targets" among what
  is not ported.
- Five more sites are absent by decision: the three FunScript commands and the two Bambi phrases.

**(b) The three flash spawn arms collapse into one presenter path — CONFIRMED; the citation does
not.** The claim is true: `FlashService.cs:1445`/`:1455`/`:1482` are mutually exclusive arms of
`SpawnFlashWindow` and the port places every image through one statement. But
**`Effects/FlashImagesEffect.cs:35` does not say so** — that line is part of a sentence about
`WS_EX_TRANSPARENT` and `HWND_TOPMOST`. The statement that carries the claim is
`Effects/FlashSurfacePresenter.cs:254-298` (`ShowOne`, the single placement path), reached from
`:189` (`Show`). Reported rather than improvised around; the census cites the line that carries the
claim.

**(c) There is a fourteenth site on the DEFAULT engine — CONFIRMED, and the brief's supporting
citation is one line off.** `VideoService.Browser.cs:452` is real, is inside `OnBrowserPlaying`'s
deferred post, and sits immediately after `VideoStarted?.Invoke` exactly as `VideoService.cs:2579-2580`
does. The routing citation `VideoService.cs:2403-2410` is the comment block; the predicate is
**`:2407`** and the "this engine is the default" sentence is at `:2400-2401`.

---

## 4. THE FOUR DIVERGENCES RECORDED (ledger D202 corrected, D203-D206 added)

**D202 corrected in place with attribution**, per the review's item 6: leaving it asserting thirteen
while a new row asserted eighteen is how a count gets corrected a fifth time. The same figure inside
`Haptics/IHapticSink.cs:210-215` is **reported and not edited** — that tree was closed to this packet.

**D203 — upstream's STOP is on one teardown path, and the panic key is not it. CONFIRMED, and worse
than the brief states.** `StopVideoBackgroundVibeAsync` has exactly one caller in the whole shipping
tree: `VideoService.cs:6580`, inside `Cleanup()`. `Stop()` (`:1411`) and `ForceCleanup()` (`:1948`)
both go through `CloseAll()` and hold zero haptic references. **Upstream's own comments state the
premise twice**: `ForceCleanup` says *"Panic / session-lock / suspend / wedge-rescue all land here"*
(`:1950`) and *"Panic key / stuck-timer / session-switch teardown routes through ForceCleanup (not
Cleanup)"* (`:1970-1971`), while the comment between its own two stops asserts the
opposite guarantee. `:6580` is the layer stop, `:6584` is the funscript stop, and `:6581-6583`
between them reads *"Runs on every teardown path (natural end, skip, panic, attention retry) because
CloseAll is the single funnel for all of them"* — while both statements sit in `Cleanup()` **beside**
`CloseAll(...)` rather than inside it. **That is why nobody would go looking**: the code does not
merely omit the stop on three paths, it carries a written guarantee it does not provide.

**What I checked that the brief did not, and it makes the finding sharper:** the designed panic-key
path does **not** call `App.Haptics.PanicStop()` either. The only three callers of `PanicStop` in the
tree are the haptics panel's own panic button (`MainWindow/MainWindow.Haptics.cs:625`), the mixer
internally, and the **panic FALLBACK watchdog** (`MainWindow/MainWindow.xaml.cs:954`), which fires
only when the UI thread failed to drain the panic handler. So the video layer is zeroed on a panic
**only when the dispatcher is already wedged**. With `SetLayer(HapticLayer.Video, level)` taking no
`autoZeroMs` (`HapticService.cs:848`), the layer then holds indefinitely.

**Recommendation, not a design:** the port's stop belongs on every path that takes the clip off the
screen. Today that is two statements, because `Effects/VideoSurfacePresenter.cs:466` clears the ended
callback rather than invoking it, so `OnDisarmed` (`:299`) does not reach `OnClipEnded` (`:372`). The
port's own row 28 already says STOP deserves harder treatment than START.

**D204 — the readout fires when nothing can vibrate. CONFIRMED; recorded as behaviour to PRESERVE.**
No call site checks entitlement or a device; the mixer refuses centrally (`HapticMixer.cs:191-204`
is `IsGateOpen`, `:843` is `Play` returning a completed sequence when it is shut), and `Announce`
(`HapticService.cs:424-427`) runs after `Play` unconditionally at `:390`, `:791` and `:849`. An
unentitled user with no toy still watches the activity readout scroll. **A port that added a
connected-check at a call site would change user-observable behaviour**, so the census records it and
designs nothing.

**One thing this changes about the limb, and it is the most practical finding in the packet: this
port has no activity readout at all.** `Views/Pages/HapticsPanelNotices.cs:42-55` offers two dot
states and says in words *"nothing sends anything to it yet"*. So **a limb in a build with no admitted
provider produces NO user-observable effect whatsoever** — not even a scrolling readout — unless the
next packet also makes the dot's third value reachable, which is a change in `Haptics/HapticParticipant.cs`
and in SP-119's own `Live`-is-unreachable finding. A limb packet that does not include that ships
something a user cannot see by any means.

**D205 — "~2s" versus 3503 ms. The comment is REFUTED; code wins.** `HapticService.cs:761` says the
ladder *"decays over ~2s"*. The loop at `:782-787` is eight rungs at `i * 450` ms, each rendering a
250 ms pattern; the default mode is `Constant` (`Models/HapticSettings.cs:58`) and
`HapticPatterns.Render`'s Constant arm (`Core/HapticPatterns.cs:123-128`) yields
`clamp(250/6,10,60) + 250 + clamp(250/4,30,150)` = 41 + 250 + 62 = **353 ms**, so the span is
3150 + 353 = **3503 ms**. I computed all six modes: the minimum is **3308 ms** (Pulse) and the maximum
is 3503 ms. **No mode gets near 2 s**, so this is not a mode-dependent reading of a defensible comment.
**A port copies 3503 ms.** Pinned arithmetically, with the constants checked against their upstream
lines, by `HapticSiteCensusTests.TheDecayLadderNeverSpansTwoSeconds_InAnyMode_SoTheSourcesOwnCommentIsRefuted`.

**D206 — `TriggerBambiFreeze` and `SubAudioAudible`. CONFIRMED, with corrected citations and a worse
consequence.** The predicates are at `SubliminalService.cs:220` and `:379`
(`audioPath != null && App.Settings.Current.SubAudioAudible`) and at **`:287`** (`audioPath != null`
alone). The brief's `:221` and `:380` are opening braces; its `:289` is a comment and the ducking
check is `:290`. **My own first draft of this row said `:286`, which is blank** — an off-by-one on the
very citation the row exists to correct, caught in review. Every number in this paragraph now comes
from `grep -n`, not from counting a `sed` window. The consequence is not only "one guard differs": `PlayWhisperAudio` (`:521`) has no
check of its own, so **with whispers turned off the freeze phrase still plays its whisper and still
ducks other audio.** Neither the branch nor the phrase is ported
(`Effects/SubliminalsEffect.cs:54-56`), so this is recorded to stop a later reader tidying one guard
to match the others.

**D197 was not re-filed.**

---

## 5. THE THIRTY-ONE SITES OUTSIDE THE FAMILY

The check sweep the plan promised ran over the whole shipping tree (excluding `CCP.*`, `Tests/**` and
`Services/Haptics/**`). **31 further call-shaped haptic sites**, listed in the census §6.1 and NOT
folded into the eighteen. The coordinator's list is confirmed and two more were found:
`AvatarTube/AvatarTubeWindow.Speech.cs:1816` and `Lab/GazeMinigame/GazeMinigameWindow.xaml.cs:1271`.

Several drive modules this port HAS — `BubbleService.cs:979` most obviously, which
`Effects/BubblePopSurfacePresenter.cs:81-83` already declares unported (*"XP, the lucky roll,
achievements, haptics and Discord presence"*). **So "eighteen" means eighteen in the effects family,
and a limb scoped to the family covers the effects modules and nothing else.** That sentence is in the
census so nobody later reads the number as "all".

---

## 6. THE VOCABULARY LAYER, PRICED ON THE SEVEN AXES FIXED BEFORE THE OPTIONS WERE KNOWN

**This is a menu. The decision is the owner's and this packet does not take it.**

**What the sites actually demand**, which is what any option has to answer to:

| demand | sites | shape |
|---|---|---|
| a one-shot pulse with a level and a length | 14, 15, 18 | 150 ms keyed off the phrase's wording; 60 ms per bounce |
| an 8-rung decaying envelope | 1, 2, 3 | `max(start * 0.7^i, 0.06)` at `i * 450` ms, each rung 250 ms |
| a continuous layer that self-clears | 4 | level = image luminance × scale, `autoZeroMs` = the flash's own lifetime |
| a continuous layer that does NOT self-clear, plus an explicit stop | 6, 7, 12 | level = `VideoIntensity * 0.1`, floored at 0.06 |
| a transient that rides OVER a floor by priority | 10 (absent anyway) | priority 3 over the video layer |
| **two transients of the SAME priority overlapping** | **1-3 with 14-15, and 18 with itself** | **peak-of-SUM within the group, then max across groups** (`HapticMixer.cs:476-502`), the floor combined by `Math.Max` at `:506` |
| a per-event enable, intensity and mode | all | upstream's routing matrix, twelve rows |

**And three facts that bound every option.** First, the SP-119 seam is keyed by **device AND
actuator** (`Haptics/IHapticSink.cs:82-90`), so whoever calls it must hold device keys from an
`ObserveAsync`; a module cannot. Second, **`SetOutputsAsync` is level-set with no duration by design**
(D193), so every duration in the table above has to be turned into a schedule of level-sets by
somebody. Third — and the first draft of this section got it wrong — **the combination rule is
exercised by REACHABLE sites.** The flash decay ladder posts at **priority 1**
(`HapticService.cs:786`), the subliminal pulse posts at **priority 1** (`:880`) and the bounce posts
at **priority 0** (`:821`); upstream SUMS within a priority group, so a flash overlapping a
subliminal, or two bounces overlapping, combine by peak-of-sum rather than by MAX. **At the shipped
defaults** — master intensity **0.7** (`Models/HapticSettings.cs:29`, `HapticMixer.cs:215`) and cap
**0.70** (`:77`), applied in `Finish` at `:509-517` — two overlapping 0.5 transients sum to 1.0, scale
to 0.70 and meet the cap, while a MAX-only design gives 0.5, scales to **0.35** and does not.
**A factor of two on the level a user feels.** **What does NOT sum, even upstream,
is flash overlapping flash**: `PlayDecayLadder` cancels any running flash ladder before posting
(`:775-776`), so the reachable summing pairs are flash-with-subliminal and bounce-with-bounce.
`TrackKind` (`:408-411`) only records the live sequence per kind and cancels nothing, so same-kind
overlap sums too.

### Option A — per-module literals

| axis | answer |
|---|---|
| cost in files | 0 new; ~4 edits in `Effects/**` plus the composition root |
| effect on the seam | **neither**: no change, no wrapper. Modules call `IHapticSink` directly |
| forecloses | combination of any kind. Two modules firing at once is last-writer-wins on the same actuator |
| reproduces | ladder **cannot** (a module would need its own timer chain); latch **cannot**; priority **cannot**; 0.06 floor and 0.70 cap only by duplicating the clamp in every module |
| 10 Hz loop | none |
| no-provider build | every call returns `Unavailable`; four modules each need to ignore it |
| blast radius | 4 modules |

**Its real defect is not aesthetic.** Every module would have to `ObserveAsync`, choose device keys and
own its own floor and cap. That is a mixer's job done four times, and it is the shape SP-119's D6 exists
to prevent.

### Option B — a port-side mixer, faithful to upstream's combine rules

| axis | answer |
|---|---|
| cost in files | 4-6 new (`Haptics/HapticMixer.cs`, layers, envelopes, routing) plus a lifetime for the loop; ~4 edits |
| effect on the seam | **wraps it**, byte-identical: the mixer becomes the sink's only caller and the only holder of device keys |
| forecloses | little; it is the maximal option |
| reproduces | **all five named behaviours** — ladder, auto-zero latch, priority arbitration, 0.06 floor, 0.70 cap — **plus the two that turned out to matter more**: peak-of-sum within a priority group and the concurrency cap, **and** the two time-based ones nothing else has, the rate limit and the periodic re-assert of unchanged targets and zeros (`HapticMixer.cs:92-99`) |
| 10 Hz loop | **yes, and it is a new lifetime question.** SP-119 §7 was careful to invent no second lifetime model; a 10 Hz output loop is exactly that, and whether it belongs to SP-118's scheduler is an architecture decision |
| no-provider build | the loop runs against a sink that refuses. It needs a predicate to idle, or it burns a thread producing nothing |
| blast radius | 4 modules, the composition root, a participant, possibly the scheduler |

**Its real cost is unexercised behaviour and a lifetime.** Concurrency eviction, temperament scaling
and the band-split are part of 67 KB of upstream mixer whose observable is a motor nobody here can
watch, and SP-119 refused to shape a seam around code this port has never run against a device; B is
that same risk one layer up. **But peak-of-sum is NOT in that category** — five reachable sites
exercise it (§6's demand table), and the recommendation below is made against that, not around it.

### Option C — a thin command vocabulary over the port's own scheduling, no loop

Two verbs on one type: `Pulse(level, duration)` and `Layer(level, autoZeroMs)`, with the limb owning
device keys, the 0.06 floor and the 0.70 cap in one place, and sending at command boundaries rather
than on a poll.

| axis | answer |
|---|---|
| cost in files | 2 new (`Haptics/HapticLimb.cs`, `Haptics/HapticEnvelope.cs`); ~4 edits |
| effect on the seam | **wraps it**, byte-identical |
| forecloses | **peak-of-sum, and with it the concurrency cap.** Each source is scheduled independently, so two overlapping transients combine by MAX at the send boundary rather than by upstream's group sum — **and §6's demand table shows five reachable sites exercising that rule.** Priority arbitration ACROSS groups is MAX either way, so C loses nothing there. C is a strict subset of B and of C+ and upgrades into either without a rewrite |
| reproduces | ladder **yes** (a fixed 8-rung shape computed once at post time); auto-zero latch **yes** (a scheduled zero); soft ramp **yes** (a rise is a scheduled shape like any other); 0.06 floor **yes**; 0.70 cap **yes**; **peak-of-sum no**; priority arbitration across groups **yes, trivially, because it is MAX** |
| 10 Hz loop | **none.** Sends happen on layer change, pulse start and each rung boundary. **This trades away upstream's rate limit**, which the 10 Hz loop exists to provide for the Lovense LAN API (`HapticMixer.cs:69-70`), so a minimum inter-send interval has to be stated as an explicit property of the limb rather than falling out of a tick |
| no-provider build | nothing is armed, nothing runs, nothing to idle |
| blast radius | 4 modules |

### Option C+ — C plus a shared-instant evaluator, still no loop

The frontier is not binary, and the first draft of this section implied it was. What forecloses
peak-of-sum in C is not the absence of a timer; it is that each source is scheduled independently. Keep
an active-envelope list, evaluate every live source on one shared instant grid at each send boundary,
and the group sum falls out — with no poll.

| axis | answer |
|---|---|
| cost in files | 3 new (C's two plus `Haptics/HapticCombine.cs`); ~4 edits |
| effect on the seam | **wraps it**, byte-identical |
| forecloses | nothing the reachable sites ask for. Still no periodic re-assert (see the recommendation) |
| reproduces | everything C does, **plus peak-of-sum within a group and the concurrency cap** |
| 10 Hz loop | **none.** Sends still happen at envelope boundaries |
| no-provider build | as C: nothing armed, nothing running |
| blast radius | 4 modules |

**This is B's evaluator without B's timer**, and it is the honest middle of the menu: the thing the
10 Hz loop buys that an evaluator does not is time-based, not combination-based.

### Option D — build nothing yet

| axis | answer |
|---|---|
| cost in files | zero |
| effect on the seam | none |
| forecloses | nothing |
| reproduces | nothing |
| 10 Hz loop | none |
| no-provider build | unchanged, and the panel already says so in words |
| blast radius | none |

**D is a real option and it deserves the honest argument**: with no admitted provider, a limb's only
end-to-end observable is a recording double. Nothing moves, and — per §4's finding — this port has no
activity readout, so nothing on screen changes either unless the dot's third value is also made
reachable.

### The recommendation

**The first draft of this recommendation was wrong on its central claim and it is corrected here.**
It said B's whole advantage over C was priority arbitration, demanded only by the absent site 10.
**That is false.** B also buys **peak-of-sum within a priority group**, and five REACHABLE sites
exercise it: the flash ladder and the subliminal pulse both post at priority 1
(`HapticService.cs:786`, `:880`), so a flash overlapping a subliminal sums upstream and would MAX
under C — **0.70 against 0.35** at the shipped defaults, a factor of two — and two bounces at
priority 0 (`:821`) do the same.
Only flash-with-flash is exempt, because the ladder cancels itself (`:775-776`). The owner would have
been choosing against a summary that hid the one thing C actually gives up.

**Re-priced, the recommendation is still C, but for a different reason, and C+ is named beside it.**

1. **B's advantage over C is now correctly stated as TWO behaviours, not one**: peak-of-sum (reachable,
   five sites) and the concurrency cap (reachable when four transients overlap). Priority arbitration
   across groups is MAX in both, so it was never the differentiator.
2. **Neither of those needs a 10 Hz loop.** C+ buys both for one more file and no timer. If the owner
   wants the combination rule, **C+ is the option to take, not B** — that is the real frontier, and
   the first draft hid it by treating the menu as binary.
3. **What the loop alone buys is time-based and belongs to the PROVIDER, not the limb.** The two
   properties that need a tick are the self-imposed rate limit for the Lovense LAN API
   (`HapticMixer.cs:69-70`) and the periodic re-assert of unchanged targets **including zeros**
   (`:92-99`), which is *"the only thing that self-heals a dropped, reordered or IO-failed zero"* —
   directly relevant to D203's harm. **Both are delivery properties**, and SP-119's seam already
   places delivery on the provider's side of the line: the hold is *"provider's choice, documented in
   the provider"* (`Services/Haptics/Core/HapticContracts.cs:70-73`), and the port's own record has
   the Lovense sink owing a keep-alive. A limb that grew a loop to solve them would be taking work
   the sink already owes.
4. **The size of C's loss is bounded and measurable the day a device exists.** It is confined to how
   loud an OVERLAP feels — never to whether something fires, latches or stops — and it is bounded
   above by the same 0.70 cap both designs apply. It is not small, though: the worked case is 0.70
   against 0.35. **This is the number the owner is really choosing about**, and it is the one the
   first draft did not put in front of them.
5. A is not viable: it puts device-key selection and the safety cap in four places. D is defensible
   and its argument is above.

**So: C to start, C+ if the owner wants upstream's combination rule, B only if the loop's own
time-based properties are wanted in the limb rather than in the provider.** C upgrades into either
without a rewrite, which is why starting at C forecloses nothing.

**Whatever is chosen, `Haptics/IHapticSink.cs` stays byte-identical.** Every option wraps the seam
rather than changing it, which is SP-119's own claim being cashed: a limb is the missing layer ABOVE
the sink, and the sink's refusals (no duration, no mode, no priority, keyed by device and actuator)
survive all five options intact.

**Two things the next packet must include or the limb is invisible:** the dot's `Live` value becoming
reachable (§4), and the stop placed on both teardown paths (D203).

---

## 7. THE PIN

`client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, **13 facts**. The document is the DATA and the
test is the LOGIC: **the needle set and the file universe live in the test**, so editing the census can
never shrink the search.

| fact | what it makes impossible |
|---|---|
| `EveryHapticLineInTheFamilyIsAccountedFor_RederivedFromTheShippingBytesAndNotFromTheDocument` | a new haptic call, a moved line or a deleted row. It recomputes the 80 candidates from the bytes and never reads the document's own totals |
| `EveryCitedUpstreamLineStillCarriesItsRecordedNeedle` | a citation rotting into a different statement while keeping its number |
| `EveryCitedPortLineStillCarriesItsRecordedNeedle_SoATriggerPointCannotRotIntoAnotherStatement` | the same on the port side, plus **exactly** five distinct trigger points — `Assert.Equal(5, ...)`, because the document claims five and a `>=` would let a sixth appear unremarked |
| `TheVerdictVocabularyIsClosed_AndEveryMappedSiteCitesBothSides` | a new verdict word, a mapped site with no port citation, an absent site with one |
| `EveryAbsenceQuotesADecisionAndTheQuoteIsReallyAtThatPortLine` | an absence justified by a citation whose words are not there (review item 2) |
| `TheDecayLadderNeverSpansTwoSeconds_InAnyMode_SoTheSourcesOwnCommentIsRefuted` | the D205 refutation being taken on trust: all six modes computed, 3503 and 3308 asserted |
| `TheLadderConstantsAreStillAtTheLinesTheCensusCites` | the arithmetic drifting from the source it claims to reproduce. It tethers the ladder's loop constants **and the Constant arm's envelope formulas** (`Core/HapticPatterns.cs:126-127`), because `RenderedRungMs` reproduces that arm and D205's 3503 ms is that reproduction: without the second pair, a change to `Render` would leave the number wrong and the fact green |
| six fixture facts over temp repositories | the guard passing vacuously: an unaccounted line, a rotted needle, a HALF-present reference tree (a hard failure, not the unreachable branch — review item 3), the unreachable branch still enforcing document shape, an absence with no quote, a quote not at its line |

Root-not-found and a missing census are hard failures, never skips. The branch taken is written to the
test output so a permanently-unreachable guard is visible in the TRX. **`allowedSkips` was not touched
and `floor.json` was never opened.**

**Two line numbers in my own draft were wrong and the pin caught them before the commit**:
`Core/HapticPatterns.cs`'s two constants are at `:36` and `:40`, not `:35`/`:38`. That is the SP-113
class, caught by the mechanism built to catch it.

---

## 8. WHAT THIS WORK DOES NOT PROVE

- **Nothing was executed and nothing was drawn.** This packet wrote no product code; the suite result
  proves the census agrees with the bytes, not that any haptic behaviour works.
- **No interaction, rendering, audio, focus, window behaviour or animation was verified.** No headed
  capture was taken; `presentation-verified` is untouched, and no headless frame was rendered either.
- **The five port trigger points are statements, not wiring.** Whether the limb can be injected into
  each of those four types without moving their construction is a question the limb packet answers.
  This census names lines; it did not compile a call at any of them.
- **The reproduction claims in §6 are analytical.** No option was built, so "reproduces the ladder" is
  an argument from the arithmetic, not a measurement.
- **`OverlayFrame`'s pixels are readable and the average is computable** (`Effects/FlashFrameSource.cs:111`,
  `:164`); whether an 8x8 WIC downscale and a full-array mean agree closely enough to be
  indistinguishable on a motor is **not** claimed and was not measured.
- **The 31 out-of-family sites were enumerated, not mapped.** No verdict was assigned to any of them.
- **The three residuals in census §7 are outside the method's reach by construction**: the over-match
  sweep is unpinned, `Services/Haptics/FunScriptService.cs:220-222` subscribes to a video event from
  a directory this census excludes, and `Services/Deeper/IActionDispatcher.cs:619-657` is driven by
  payload JSON that no C# census can enumerate.
- **Linux is untouched and unproven here**, as is Windows: no platform behaviour was exercised at all.

---

## 9. THE BASE WAS RED, AND THE MECHANISM IS A MIRROR DRIFT RATHER THAN A MISSING PATH

Before the merge, `node client/tests/floor/check-floor.mjs` exited non-zero in this worktree on
**one** named failure:

```
FloorWrapperGuardTests.PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin
  spine-tasks/SP-1NN-.../PROMPT.md: packet does not list `client/tests/floor/floor.json`
  in fileScopeMustNotChange
```

**The base was red from the authoring commit `9af75cdf`, before either lane wrote a line**, and both
of wave 60's packets carried it.

**The cause is not a typo and not a missing path.** Both packets listed `client/tests/floor/**`,
which covers the shared pin semantically. **Two enforcers of the same rule disagree about what
"covers" means**: `validate-wave.mjs` check 4 matches with the glob-aware `patternCovers` (`:458`)
and passes the packet, while `FloorWrapperGuardTests` matches with a literal `.Contains` (`:224`) and
reds the suite — so a packet clears the pre-launch validator and fails the gate it is supposed to
mirror. `validate-wave.mjs`'s own header carries a MIRROR note forbidding exactly that drift. Filed
**P0** by the orchestrator on `client/docs/task-board.md` at `b5f789de`.

**What each side did.** My half was fixed in-lane at `eacff685`:
`spine-tasks/SP-120-haptic-limb-census/PROMPT.md:105` now lists `client/tests/floor/floor.json`
alongside `client/tests/floor/**` — that file is inside this packet's own declared write zone, and
the edit only ADDS a path to a must-not-change list, so it widens nothing. **SP-121's half I did not
touch**, because `spine-tasks/SP-121-zero-execution-census/**` is another task's folder and iron rule
1 forbids it; the orchestrator fixed it on base the same way and took my committed row verbatim, so
the merge carried no conflict on that line.

**Nothing was disabled, quarantined or tolerance-widened to get around it**, no name was added to
`allowedSkips`, and `client/tests/floor/floor.json` was never opened. The pre-merge run that carried
the failure reported the same 2260 unit total as the clean post-merge run, so the delta arithmetic
never depended on the failure being fixed.
