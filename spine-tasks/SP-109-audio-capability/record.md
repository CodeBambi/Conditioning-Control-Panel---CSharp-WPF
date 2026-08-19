# SP-109 — record

Branch `lane/SP-109-audio-capability`, base `2b348901`.
Floor: pin **1527 unit / 95 headless**; observed **1589 unit / 100 headless**; declared delta
**+62 unit / +5 headless** (`floor-delta.json`). 1527 + 62 = 1589 and 95 + 5 = 100, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-109-audio-capability`. The floor run
therefore REPORTS a violation against the pin, and that is the expected shape: the orchestrator sums
the deltas and applies one bump. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened.
Build: 0 errors, 0 warnings. `client/tests/floor/floor.json` was never opened.

---

## 0. THE HEADLINE — the refusal is discharged, and the discharge is four facts and one named gate

An earlier wave refused Mind Wipe on the ground that *"nothing in this project can actually verify
that a sound played, so shipping it would have meant claiming something nobody could check."*

**That is no longer true, and here is exactly how far it stopped being true.**

I did not design and then hope. Before the first product edit I wrote the independent WASAPI probe,
drove the port's EXISTING `SoundFlowAudioBackend` through open → play → stop → teardown, and read the
operating system back at every step (`plan.md` §0, raw output included). Every GUID and vtable slot
in that probe was read out of the Windows SDK headers on this machine (10.0.26100.0), not recalled.

### The provable chain, and where it stops

| # | Fact | API asked | Measured |
|---|---|---|---|
| **F1** | the OS reports N ACTIVE render endpoints | `IMMDeviceEnumerator::EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)` | 1 — `Speakers (Realtek(R) Audio)` |
| **F2** | after the port opens a device, the OS holds a render session **whose owning process id is this process** — one it did not hold a moment earlier | `IAudioSessionManager2::GetSessionEnumerator` → `IAudioSessionControl2::GetProcessId` | absent before (5 sessions, none ours); present after (6, one ours) |
| **F3** | that session's state is `AudioSessionStateActive`, and `Inactive` after teardown | `IAudioSessionControl::GetState` | 1 while up, **0 after dispose** |
| **F4** | **the Windows audio engine metered a NON-ZERO peak on this process's own stream while a clip played, and ZERO on the same stream with the device open and nothing cued** | `IAudioMeterInformation::GetPeakValue` | **0 → 0.405 → 0** |

**F4 is why this is not the shape the port has rejected four times.** The number is not produced by
this process; it is the OS mixer's own measurement of the samples it consumed from us. And it carries
its own negative control, MEASURED rather than assumed: with the device open and started and nothing
cued, the same meter on the same stream reads **0**. A product that opened a device and played
nothing reads 0 and reds.

### WHERE THE CHAIN STOPS — stated plainly, and it stops early

The chain stops **at the boundary of the Windows audio engine**. It does NOT prove:

1. that the endpoint was unmuted, or its volume above zero, or that no exclusive-mode client held it;
2. that a DAC converted anything, or that speakers/headphones were attached and powered;
3. that the endpoint is PHYSICAL at all — a virtual or RDP sink satisfies F1–F4 identically with no
   output anywhere, and this port has already recorded that class on WSLg
   (`client/docs/audio-backend-spike.md:24,88`);
4. that the sound was the RIGHT sound — a peak proves non-silent samples, not which clip, which
   channel, or which moment;
5. **that any human heard anything.** That is `audible-verified`, it is a **named manual gate**, and
   **no automated step on any platform discharges it, Windows included.**

The evidence class is written up in `client/docs/verification-harness.md` §"Audio evidence class
(SP-109)" with all three classes (`render-session-verified`, `render-metered`, `audible-verified`)
and the three things tier 1 cannot cover for audio.

---

## 1. ONE MODULE OR TWO — **two**, and the second is explicitly HALF

**Mind Wipe** landed whole. **Brain Drain** landed as its AUDIO half only.

Two, not one, because the capability is only interesting once a SECOND consumer proves it is not
shaped around one module — and these two genuinely differ where it counts:

| | Mind Wipe | Brain Drain |
|---|---|---|
| window | fixed 10 s (`MindWipeService.cs:127-129`) | 500 ms or 5 s, user-switchable (`BrainDrainService.cs:60-69`) |
| probability | `perHour / 360.0` (`:734`) | `intensity/100 / (60/windowSec)` (`:183`) |
| dial clamp | `Math.Clamp(value, 1, 180)` (`:98-99`) | `Math.Clamp(value, 1, 100)` (`:49`) |
| gain | own `MindWipeVolume` (`:104`) | app-wide **master** volume (`:298-302`) — which the port does not have |
| completeness | whole row | **half a row, permanently** |

### The half-row is made LOUD, in three places, and one of them is on every healthy run

1. **The rack row is titled "Brain Drain (audio half)"** — the user reads the scope before enabling
   anything, and a headless fact pins the exact string beside the dot it justifies.
2. **The panel LEADS with the missing half** (not buried at the bottom): the module's own
   `VisualHalfNotice` constant, rendered verbatim, naming the desktop blur, its 30–60 FPS screen
   capture (`OverlayService.cs:1965-1995`), that upstream's own panel calls it the "VISUAL half"
   (`BrainDrainFeatureControl.xaml.cs:170`), and that it is **absent rather than broken**. A headless
   fact also asserts that no blur or melt control EXISTS on the panel — a greyed one would imply the
   blur is one setting away.
3. **`Ready()` returns `Degraded` on EVERY run, however healthy**, carrying
   `braindrain-visual-half-absent`. This is the part that matters most: every other module in the
   rack reports a clean `Available` when everything works. This one must not, because the absence is
   a property of the **build**, not of the run — and a row that only admitted to being half a row
   when something else broke would be exactly the silently-missing half the packet forbids.

The same string is the panel's text AND the typed reason's detail, so the two cannot drift into two
accounts of one absence.

---

## 2. THE DOT'S FIFTH MEANING — decided: **`Live`**, and the rule that settles it

The dot has meant the **clock** (paced), the **screen** (continuous), **change** (moving) and
**custody** (non-drawing). All four are claims about state this process owns and can see.

> **The fifth is REACH: the module's output can get to the user — and for the first time that is a
> fact about a resource this process does NOT own and cannot see. It has to ASK the operating
> system.**
>
> ```
> Live  =  a firing is on the clock  &&  the OS reports an active render session for this process
> ```

Neither clause is redundant. Without the first, a module whose tick died keeps claiming to fire.
Without the second, a module lights its dot on a machine with no output device — the Pink Filter
failure, in the one medium where a user cannot check by looking. **Upstream draws the same line in
the same place**, one call deeper: `if (App.Audio?.IsOutputSuppressed == true) return;` with the
comment "endpoint down — stay quiet, don't spin" (`MindWipeService.cs:771`,
`BrainDrainService.cs:211`).

### And the sub-rule that answers the half-row, which is the packet's actual question

Two landed rules disagreed and neither was written for this case: SP-105's says a module that cannot
draw is `Armed`; the Subliminals rule says a module whose schedule is really on the clock is `Live`.

> **The dot is scoped to what the row IS, and a row that is a PROPER SUBSET of its upstream must say
> so where the user reads it.**

Brain Drain is **`Live`**. Everything this row claims to be is running and audible; the un-ported blur
was never a mechanism this row has. `Armed` would be the *opposite* lie from the one SP-105
prevented — an under-claim about a module that is really working, which would teach the user that the
dot cannot be trusted in the other direction too.

**This makes the row's TITLE load-bearing rather than cosmetic.** The dot is only honest because the
row names its scope. `BrainDrainsHalfRowIsLIVE_BecauseEverythingThisRowClaimsToBeIsRunning` asserts
the dot and the exact title string in the same fact, so a later "tidy" of the label to plain "Brain
Drain" turns a truthful dot into a false one AND reds.

### The three negatives are three DIFFERENT answers, and that is the finding

| situation | arm result | dot | why |
|---|---|---|---|
| no render session (no endpoint, or Linux) | `Unavailable` / `audio-render-unavailable` | **Armed** | the whole output CHANNEL is gone — SP-105's answer |
| clip folder empty | `Degraded` / `audio-no-clip` | **Live** | a pool is CONTENT, not a channel — the Subliminals answer; drop a clip in and it plays with no re-arm |
| Brain Drain's absent visual half | `Degraded` / `braindrain-visual-half-absent` | **Live** | the row is a subset and says so |

---

## 3. PROVING IT BITES — 24 mutations, TWO ROUNDS, and the first round found ten holes

The first submission mutated two predicates and stopped. The code review named that as the fifth
appearance of this port's own rule — **a sweep bounded by where the last instance was found is not a
sweep** — and it was right: mutating **every** conjunct and predicate in the capability found **ten
survivors**, one of which the reviewer had already predicted.

### Round 1 — 24 mutations, 14 caught, 10 survived

| # | mutation | round 1 |
|---|---|---|
| M-a | `Confirmed` drops `SessionActive` | **SURVIVED** → closed |
| M-b | `Confirmed` drops `SessionOwnedByThisProcess` | **SURVIVED** → closed |
| M-c | `Confirmed` drops `Asked` | **SURVIVED** → closed |
| M-d | `IsRendering` drops the read-back conjunct | caught ×5 |
| M-e | `IsRendering` drops `_deviceUp` | **SURVIVED** → equivalent |
| M-f | `IsRendering` drops `!_disposed` | **SURVIVED** → equivalent |
| M-g | `Open` re-inits the device on every call | caught |
| M-h | `Open` skips the endpoint question | caught |
| M-i | `Open` ignores `TryInit`'s ANSWER (device still opened) | **SURVIVED** → closed |
| M-j | `Open` skips the read-back gate | caught ×3 |
| M-k | `Cue` skips the read-back gate | caught ×2 |
| M-l | `Remember` keeps a stale confirmation | **SURVIVED** → equivalent |
| M-m | `WorkIsRunning` drops the OS clause | caught |
| M-n | `WorkIsRunning` drops the CLOCK clause | **SURVIVED** → closed |
| M-o | `Compose` stops honouring a suppressed endpoint | caught |
| M-p | `Compose` stops honouring an empty pool | **SURVIVED** → equivalent |
| M-q | `Compose` stops rolling | caught |
| M-r | `Ready` stops refusing on an unconfirmed session | caught ×2 |
| M-s | `Ready` stops degrading over an empty pool | caught |
| M-t | `Ready` stops opening the device at arm | **SURVIVED** → closed |
| M-u | Brain Drain stops declaring its missing half | caught |
| M-v | the pool stops re-reading while empty | **incomplete mutation** — see below |
| M-w | the pool recurses into subfolders again | caught |
| M-x | the pool stops filtering by extension | caught |

### Round 2 — the ten survivors, re-run against the closed suite

**Six were real holes and are now closed**, each by a fact that isolates the clause:

| # | closed by |
|---|---|
| M-a | `ASessionTheOsOWNSButReportsINACTIVE_IsUNAVAILABLE_NotAvailable` (+ the Cue-side twin) |
| M-b | `AnACTIVESessionOwnedByANOTHERProcess_DoesNotEarnAvailable` |
| M-c | `AnObservationThatWasNeverASKED_IsNotConfirmed_WhateverItsOtherFieldsSay` |
| M-i | `ADeviceThatREFUSESToOpen_IsUnavailable_AndCarriesTheBackendsOwnError` |
| M-n | `BOTHClausesOfTheFifthDotMeaningAreLoadBearing_PinnedOnThePredicateItself` |
| M-t | `ArmingAnEnabledAudioModuleOPENSTheDevice_AndArmingNoneNeverDoes` |

**M-b is the sharpest of them.** Every injected observation in the file moved ownership and liveness
TOGETHER, so `Confirmed` dropping `SessionOwnedByThisProcess` was invisible — and that clause is
`GetProcessId`, the entire reason the read-back enumerates SESSIONS instead of asking the endpoint.
Without it, **another application playing music would have earned this process an `Available`.**

**M-v was an incomplete mutation, not a hole.** It changed only `Draw()`, and the fact reads
`ActiveCount` first, which re-reads. Re-run against BOTH accessors it is **caught by two facts**.

**Four remain, and all four are EQUIVALENT MUTANTS** — the guard is redundant, not unpinned. Reported
rather than papered over with a fact that would only assert the implementation's shape:

- **M-e / M-f** (`IsRendering`'s `_deviceUp` and `!_disposed`). `Dispose` sets `_deviceUp = false`
  AND `_lastObservation = NotAsked`; `_deviceUp` can only be false when nothing was ever opened, in
  which case the observation is `NotAsked` too. Any one of the three conjuncts is sufficient on every
  reachable path. The suite pins the OUTCOME — not rendering after teardown — rather than which guard
  produced it, which is the right thing to pin.
- **M-l** (`Remember` clearing the remembered answer on a non-`Available` open). Every path that
  reaches it with a refusal either has `_deviceUp == false` already, or has just written an
  unconfirmed observation through `ReadAndRemember`. Writing the fact is what proved this: the first
  attempt asserted a second `Open` noticing a vanished endpoint, and it **failed**, because an
  idempotent `Open` deliberately does not re-ask the endpoint question (re-initialising would stop
  the other module's clip). The fact was rewritten to the reachable case — an endpoint pulled
  mid-session is noticed by the READ-BACK at the next ask — and it says in its own body that it does
  not discriminate M-l.
- **M-p** (`Compose`'s empty-pool check). With an empty pool the mutant reaches the roll and then
  `Draw()` returns null, so `Compose` returns null either way; the only difference is one consumed
  random draw. Kept because it is upstream's ORDER (the empty check precedes the roll,
  `MindWipeService.cs:704-708`) and it avoids a pointless roll.

Every mutation was restored byte-identically; the three key predicates are verified present by grep
after the sweep.

## 4. THE SHARED-CODE CHANGE — one word, and the reason is a finding

**`PacedSessionEffect.WorkIsRunning` was `sealed`.** SP-105 made the dot's third input abstract in
`OwnedSessionEffect` on the explicit finding that "the AUTHORITY behind the third one is the module's,
not the base's" — and then `PacedSessionEffect` immediately took that authority back for every paced
module by sealing its answer. That held for four modules because for all four the clock was the whole
story. **It broke on the sixth and seventh:** an audio module's firing can be perfectly scheduled
while the OS reports no render session.

`sealed override` → `override`, plus a one-directional contract written on the member: a subclass may
only **narrow** it (keep `ScheduleArmed` as a conjunct), never widen it, because widening puts back
the stored "I was told to start" bool the whole design forbids. `ScheduleArmed` is public precisely so
the narrowing is expressible and both conjuncts are pinnable separately — and both are pinned
(`WithNoRenderSessionTheModulesAreARMED_NotLive_EvenWithAFiringOnTheClock` asserts `ScheduleArmed` is
true while the dot is `Armed`).

**Nothing else shared changed.** `ISessionEffect`, `OwnedSessionEffect`, `SessionEngine`,
`EffectSignal`, `ScheduledFire`, `OverlaySurfaceSet` and all five landed modules are byte-identical to
base. `EffectReasonCodes` gains three additive codes.

---

## 5. THE SPINE GAP THIS FOUND, and it is the next module's problem

**There is no per-module "acquire your capability" hook that runs BEFORE the arm's change
notification.** `OwnedSessionEffect.Arm()` is `EngageIfEligible()` → `RaiseChanged()` → `Ready()`, and
`Engage` is sealed by `PacedSessionEffect` while `Arm` is not virtual. So the only hook an audio
module has that runs on an arm is `Ready`, which runs *after* the notification the UI repaints on.

Three placements were considered and two rejected for stated reasons:

- **App start (phase 3)** — rejected: it takes a WASAPI session and a volume-mixer row on every
  launch, including every launch by a user who never enables either module, and both ship OFF. It
  would also open a real device inside every landed test that builds a `SessionParticipant`.
- **`Compose()`** — rejected: a half-second native device open on a clock callback, once per window.
- **`Ready()`, with an explicit second `RaiseChanged()`** — taken. The extra notification is what
  makes the dot correct on the first frame after START. It costs one extra repaint **per arm**, and an
  arm is a gesture, not a firing, so it is affordable where a per-firing notification would not be.

The compromise is commented at the call site, not hidden. **A sixth module with a device will hit this
again**, and the fix is a spine hook rather than another `Ready` override.

---

## 6. What this work does NOT prove

- **Nothing here proves a human heard anything.** `audible-verified` is undischarged and is not
  dischargeable by this suite or by any automated step on any platform.
- **No headed capture was taken.** `presentation-verified` is untouched; these modules have nothing to
  present. The headless facts are visual-tree/style/binding only and prove nothing about pixels.
- **Linux audio is unproven**, refuses in type, and the four-step gate in
  `AudioPresenceFactory.LinuxManualGate` is undischarged. WSLg cannot discharge it: it enumerates a
  single virtual "RDP Sink" and ships no `pactl` at all (`audio-backend-spike.md:24,88`).
- **Content is unchecked.** A non-zero peak proves non-silent samples, not which clip, which channel
  or which moment. Two modules cueing each other's clips would pass every automated check here.
- **A muted endpoint or a muted session would legitimately red `render-metered`.** That is a property
  of the MACHINE and it is the honest failure; the assertion says so in its own failure message and
  must not be answered by weakening it or by an `allowedSkips` entry.
- **The device-open cost was not measured under load.** The presence opens on the UI thread at arm.
  Measured as fast in the harness, not profiled on a cold or contended machine.
- **`SoundArbitration` was not unified with this capability.** The DTRH/companion channel model stays
  its own owner (SP-029 boundary). Two audio owners now exist in the process; miniaudio devices
  coexist (SP-017), but nothing here proves they coexist under contention.
- **Concurrency of the two modules against one presence** is exercised only single-threaded through
  the manual clock. The slot table is lock-guarded and never held across a device call, and that is
  reasoning, not a stress result.

---

## 7. Files changed

**Product — new.** `Audio/AudioReasonCodes.cs`, `Audio/IAudioPresence.cs`
(+ `AudioRenderObservation`, `AudioCue`), `Audio/UnsupportedAudioPresence.cs`,
`Audio/AudioPresenceFactory.cs` (+ `LinuxManualGate`), `Audio/WasapiRenderReadback.cs` (the product's
COM read-back), `Audio/WasapiAudioPresence.cs`; `Effects/AudioCuePool.cs`,
`Effects/AudioCueSchedule.cs` (both pacing laws), `Effects/AudioCueEffect.cs` (the shared rolled body
and the fifth dot meaning), `Effects/MindWipeEffect.cs`, `Effects/BrainDrainEffect.cs`;
`Session/MindWipePresetDocument.cs`, `Session/BrainDrainPresetDocument.cs`;
`Views/Pages/AudioPanelNotices.cs`.

**Product — changed.** `Session/EffectReasonCodes.cs` (three additive codes),
`Session/PacedSessionEffect.cs` (**one word**: `sealed override` → `override`, §4),
`Session/SessionParticipant.cs` (composes the capability once and both modules, two stores, the rack
order, teardown), `Views/Pages/StudioPage.axaml` + `.axaml.cs` (the IMMERSION group, two rows, two
panels, five dials).

**`Audio/AudioSeams.cs`, `Audio/SoundArbitration.cs` and `Audio/SoundFlowAudioBackend.cs` are
byte-identical to base.** The packet's instruction was to read `AudioSeams.cs` and not duplicate it;
the new capability is a layer ABOVE `IAudioBackend` that adds the one thing it does not have.

**Tests — new.** `WasapiRenderProbe.cs` (the independent oracle), `TestWav.cs`, `AudioObservations.cs`,
`AudioCapabilityTests.cs` (**22**), `AudioModuleSpineTests.cs` (**23**), `AudioCuePoolTests.cs`
(**8**).

**Tests — changed.** `StudioSurfaceNoticeTests.cs` (**+9**: a six-row theory over the audio panel's
states plus three facts for the pool line and the capability line),
`StudioRackHeadlessTests.cs` (**+5**, plus the row lists and group list extended from five names to
seven, and the missing-half notice pinned POSITIONALLY rather than only by text),
`ContinuousEffectSpineTests.cs` and `SecondEffectSpineTests.cs` (refusal and rack-order lists
extended — **0 count change**).

**Docs.** `client/docs/wpf-surface-reachability.md` (D101–D109),
`client/docs/verification-harness.md` (the audio evidence class).

---

## 8b. WHAT THE CODE REVIEW CHANGED, beyond the two blockers

Five smaller corrections, all of them real:

1. **`AudioCuePool` recursed into subfolders; upstream does not.** `Directory.GetFiles(folder, "*.*")`
   with no overload is `TopDirectoryOnly` (`MindWipeService.cs:162`, `BrainDrainService.cs:87`). A
   subfolder of clips would have been audible in the port and silent in the shipping app — a
   behaviour difference with no upstream evidence behind it. **Matched to upstream** rather than
   recorded as a divergence, and pinned by `EnumerationIsTOPLEVELONLY_BecauseUpstreamsIs`.
2. **Brain Drain's `Ready` replaced the audio half's own degradation — code AND detail — with the
   visual-half notice**, so a Brain Drain armed over an empty clip folder lost "there is no audio clip
   in `<folder>`" from its arm result entirely. **Both now travel**: the code stays the build-level
   one (true on every run), and the run-level cause is carried verbatim, with its own code, at the
   front of the detail.
3. **The negative control was order-dependent.** It asserted `SessionForThisProcess`, and this
   packet's own measurement shows that stays TRUE after teardown — only the STATE falls to Inactive.
   Any xunit ordering that put a lifecycle fact first would have reddened it for a non-defect. It now
   asserts `Active`, which is false both before the first open and after every teardown.
4. **`DescribeCueState` had four branches, zero facts — and one of the branches was FALSE.** Writing
   the theory found it: the fourth arm (session running, audio confirmed, schedule not on the clock)
   repeated "Nothing plays until the session starts" to a user who had already started one — exactly
   the instruction SP-105 had to split apart. Rewritten, and the theory now asserts that no row with
   a running session may contain that phrase.
5. **"The panel LEADS with the missing half" was a positional claim pinned only by text.** The
   headless fact now asserts the notice's INDEX is below the enable toggle's, so moving it to the
   bottom reds.

Also swept: **53 citation corrections** across 16 files. The reviewer named four drifted line numbers;
a full sweep of every `File.cs:line` in the packet's own files against the shipping source found
fifty-three, including the frequency clamp (`:98-99` → `:100`), the extension filter (`:158-161` →
`:162-165`), the per-clip device open (`:783` → `:791`), `StopCurrentAudio` (`:851-869` → `:857-874`),
the Discord call (`:206` → `:212`) and the Brain Drain rack row (`:514` → `:513`). The behaviour each
one described was correct in every case; only the pointers were stale.

---

## 8c. What the reviewer verified, and what remains unverifiable

The review independently confirmed all eight COM GUIDs and every vtable slot against the SDK headers,
that the equality facts genuinely bit on this machine, that the zero-peak control is asserted
unconditionally, and that **D108 holds against the shipping source**. None of that changes the
ceiling: `audible-verified` is still undischarged and still not dischargeable here.

---

## 9. Divergences from D101 onward, in one line each

Full rows with citations are in `client/docs/wpf-surface-reachability.md` §SP-109.

| # | In one line |
|---|---|
| **D101** | Brain Drain ships as its audio half; the desktop blur is absent and is stated on the row, in the panel and in a `Degraded` arm on every run |
| **D102** | The dot gains an OS clause — `Live` needs a confirmed render session, not just a firing on the clock |
| **D103** | One playback device for the app instead of a fresh `WaveOutEvent` per clip |
| **D104** | Clips read from `<dataDir>/assets/sounds/<module>`, and a missing folder is named rather than created. Enumeration is TOP-LEVEL, matching upstream (corrected at code review) |
| **D105** | A moved volume slider takes effect on the next cue, not on the clip already playing |
| **D106** | Brain Drain gets its own volume dial because the port has no master volume |
| **D107** | Loop mode, the crossfade engine, Clean Slate, the custom clip, `TriggerOnce`, session mode, Discord presence and both level unlocks are not ported |
| **D108** | **A discrepancy in the SOURCE, not a port choice**: Mind Wipe's comment says "30-second window" and its arithmetic is `perHour/360` on a 10-second timer. The arithmetic is the behaviour; following the comment would fire a third as often |
| **D109** | Linux refuses typed with a four-step manual gate — because the backend WORKS there and the OS-side proof does not exist here |
