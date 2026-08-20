# SP-120 — record

Branch `lane/SP-120-haptic-limb-census`, base `4276249e`.
Floor: pin **2247 unit / 141 headless**; observed **2260 unit / 141 headless**; declared
**+13 unit / +0 headless** (`floor-delta.json`). 2247 + 13 = 2260 and 141 + 0 = 141, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-120-haptic-limb-census`. The floor run
therefore REPORTS a violation against the pin, which is the expected shape: the orchestrator sums the
deltas and applies one bump. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened.
Build: 0 errors, 0 warnings (`check-warnings.mjs`, forced non-incremental, 4 projects).
`client/tests/floor/floor.json` was never opened.

**ONE NAMED FAILURE REMAINS AND IT IS NOT MINE TO FIX — see §9.**
`FloorWrapperGuardTests.PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin` reds on
`spine-tasks/SP-121-zero-execution-census/PROMPT.md:102`, another lane's packet, which iron rule 1
forbids this lane from editing. It reds on the base commit too: neither packet in wave 60 was
authored with the literal shared-pin path in its `fileScopeMustNotChange` row. **My own packet's row
was the other half of that failure and I fixed it** (`PROMPT.md:105`, inside this packet's own write
zone); the remaining half needs one line in SP-121's folder.

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

**Every correction so far, including mine, came from widening the search rather than from reading the
same lines harder.** Eight became thirteen by reading one file properly; thirteen became fourteen by
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

**The helper rule, applied identically in both places (review item 7).** One site = one call
expression; a helper's CALLERS are `adjacent` call paths, never extra sites. That keeps
`FlashService.cs:1627` as the luminance site (its guard and only call path, `:1525`, is an `adjacent`
row) and `SubliminalService.cs:588` as the silent-branch site (its four call paths at `:246`, `:265`,
`:321`, `:396` are `adjacent` rows). **Named explicitly so it does not read as a correction to the
thirteen**: D202's line numbers for those two sites were right; the alternative reading — count the
call sites instead — would have produced 21 and made the reconciliation meaningless.

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
Cleanup)"* (`:1970-1971`), while the stop's own comment at `:6581-6583` claims it *"Runs on every
teardown path ... because CloseAll is the single funnel for all of them"* — and the stop is not in
`CloseAll`, it is beside it.

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
(`audioPath != null && App.Settings.Current.SubAudioAudible`) and at **`:286`** (`audioPath != null`
alone) — the brief's `:221`, `:380` and `:289` are the opening braces and the ducking check
respectively. The consequence is not only "one guard differs": `PlayWhisperAudio` (`:521`) has no
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
| a per-event enable, intensity and mode | all | upstream's routing matrix, twelve rows |

**And two facts that bound every option.** First, the SP-119 seam is keyed by **device AND actuator**
(`Haptics/IHapticSink.cs:82-90`), so whoever calls it must hold device keys from an `ObserveAsync`;
a module cannot. Second, **`SetOutputsAsync` is level-set with no duration by design** (D193), so
every duration in the table above has to be turned into a schedule of level-sets by somebody.

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
| reproduces | **all five**: ladder, auto-zero latch, priority arbitration, 0.06 floor, 0.70 cap |
| 10 Hz loop | **yes, and it is a new lifetime question.** SP-119 §7 was careful to invent no second lifetime model; a 10 Hz output loop is exactly that, and whether it belongs to SP-118's scheduler is an architecture decision |
| no-provider build | the loop runs against a sink that refuses. It needs a predicate to idle, or it burns a thread producing nothing |
| blast radius | 4 modules, the composition root, a participant, possibly the scheduler |

**Its real cost is unexercised behaviour.** Peak-of-sum-within-priority-group, concurrency eviction and
soft-ramp are 67 KB of upstream mixer whose observable is a motor nobody here can watch. SP-119 refused
to shape a seam around code this port has never run against a device; B is that same risk one layer up.

### Option C — a thin command vocabulary over the port's own scheduling, no loop

Two verbs on one type: `Pulse(level, duration)` and `Layer(level, autoZeroMs)`, with the limb owning
device keys, the 0.06 floor and the 0.70 cap in one place, and sending at command boundaries rather
than on a poll.

| axis | answer |
|---|---|
| cost in files | 2 new (`Haptics/HapticLimb.cs`, `Haptics/HapticEnvelope.cs`); ~4 edits |
| effect on the seam | **wraps it**, byte-identical |
| forecloses | **priority arbitration and peak-of-sum.** Two simultaneous pulses combine by MAX or by arrival, not by upstream's group rule. C is a strict subset of B and upgrades into it without a rewrite |
| reproduces | ladder **yes** (a fixed 8-rung shape computed once at post time); auto-zero latch **yes** (a scheduled zero); 0.06 floor **yes**; 0.70 cap **yes**; priority arbitration **no** |
| 10 Hz loop | **none.** Sends happen on layer change, pulse start and each rung boundary. **This trades away upstream's rate limit**, which the 10 Hz loop exists to provide for the Lovense LAN API (`HapticMixer.cs:69-70`), so a minimum inter-send interval has to be stated as an explicit property of the limb rather than falling out of a tick |
| no-provider build | nothing is armed, nothing runs, nothing to idle |
| blast radius | 4 modules |

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

**C, and the reason is the reproduction table crossed with the demand table.** Four of the five named
behaviours are demanded by sites that HAVE a port trigger point; **priority arbitration is demanded
only by site 10, which is `absent-by-decision` and cannot fire here.** So B's whole advantage over C is
a behaviour no reachable site in this port asks for, bought with a 10 Hz loop, a new lifetime and a
body of unexercised combine logic. A is not viable: it puts device-key selection and the safety cap in
four places. D is defensible and its argument is above, but C's cost is two files, and the four
behaviours it does reproduce are the ones the port's own trigger points need on the day a provider is
admitted.

**Whatever is chosen, `Haptics/IHapticSink.cs` stays byte-identical.** Every option wraps the seam
rather than changing it, which is SP-119's own claim being cashed: a limb is the missing layer ABOVE
the sink, and the sink's refusals (no duration, no mode, no priority, keyed by device and actuator)
survive all four options intact.

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
| `EveryCitedPortLineStillCarriesItsRecordedNeedle_SoATriggerPointCannotRotIntoAnotherStatement` | the same on the port side, plus "at least five distinct trigger points" |
| `TheVerdictVocabularyIsClosed_AndEveryMappedSiteCitesBothSides` | a new verdict word, a mapped site with no port citation, an absent site with one |
| `EveryAbsenceQuotesADecisionAndTheQuoteIsReallyAtThatPortLine` | an absence justified by a citation whose words are not there (review item 2) |
| `TheDecayLadderNeverSpansTwoSeconds_InAnyMode_SoTheSourcesOwnCommentIsRefuted` | the D205 refutation being taken on trust: all six modes computed, 3503 and 3308 asserted |
| `TheLadderConstantsAreStillAtTheLinesTheCensusCites` | the arithmetic drifting from the source it claims to reproduce |
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

## 9. THE ONE THING I COULD NOT MAKE GREEN, AND EXACTLY WHY

`node client/tests/floor/check-floor.mjs` exits non-zero on **one** named failure:

```
FloorWrapperGuardTests.PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin
  spine-tasks/SP-121-zero-execution-census/PROMPT.md:102: packet SP-121-zero-execution-census does
  not list `client/tests/floor/floor.json` in fileScopeMustNotChange
```

**It is pre-existing on the base commit and it is not this lane's file.** Wave 60 authored two
packets and neither carried the literal `client/tests/floor/floor.json` in its
`fileScopeMustNotChange` row; both had `client/tests/floor/**`, which covers the pin semantically but
not literally, and the guard matches literally on purpose. So the guard was red in every worktree of
this wave before either lane wrote a line.

- **My half is fixed.** `spine-tasks/SP-120-haptic-limb-census/PROMPT.md:105` now lists
  `client/tests/floor/floor.json` alongside `client/tests/floor/**`. That file is inside this
  packet's own declared write zone ("May change ... `spine-tasks/SP-120-haptic-limb-census/**`"), the
  edit only ADDS a path to a must-not-change list, and it widens nothing.
- **SP-121's half is untouched**, because `spine-tasks/SP-121-zero-execution-census/**` is another
  task's folder and iron rule 1 forbids it. The fix is one literal added to `PROMPT.md:102`, by that
  lane or by the orchestrator.

**Nothing was disabled, quarantined or tolerance-widened to get around it**, no name was added to
`allowedSkips`, and the observed totals above are from the same run that carries this failure.

---
