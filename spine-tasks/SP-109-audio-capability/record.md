# SP-109 — record

Branch `lane/SP-109-audio-capability`, base `2b348901`.
Floor: pin **1527 unit / 95 headless**; observed **1563 unit / 100 headless**; declared delta
**+36 unit / +5 headless** (`floor-delta.json`). 1527 + 36 = 1563 and 95 + 5 = 100, confirmed by
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
comment "endpoint down — stay quiet, don't spin" (`MindWipeService.cs:770`,
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

## 3. PROVING IT BITES — two mutations, and the second one found a real hole in the first's coverage

**M1 — claim `Available` without opening a device.** Replaced the `TryInit` call site in
`WasapiAudioPresence.Open()` with `if (false)`, so the endpoint check passed and no device was ever
opened. **5 facts red**, including the two that matter most:

```
OpenClaimsAvailableEXACTLYWhenTheOsReportsAnActiveRenderSessionForThisProcess  [FAIL]
WhileAClipPlays_WindowsMetersANonZeroPeakOnThisProcessesOwnStream             [FAIL]
CueClaimsAvailableEXACTLYWhenTheOsStillConfirmsTheSession...                  [FAIL]
TheAvailableDetailNamesTheApiThatEarnedIt_AndRefusesTheClaimItCannotMake      [FAIL]
ASecondOpenDoesNotReInitTheDevice_ButDoesAskTheOsAgain                        [FAIL]
```
Restored byte-identically (`grep -c "if (false)"` → 0, both `if (!observation.Confirmed)` sites back).

**M2 — delete the READ-BACK GATE itself**, keeping the real device open (`if (!observation.Confirmed)`
→ `if (false)` in `Open`). **Exactly one fact red**, and it is the right one:
`ADeviceThatOpensWithoutTheOsConfirmingASession_IsUNAVAILABLE_NotAvailable`.

**The hole M2 exposes, and why the design already answers it.** The real-device facts CANNOT catch
M2, because on a machine whose OS confirms the session, "gate present" and "gate deleted" produce the
same answer. The earning step is therefore pinned by a *different* fact — one that injects a
read-back saying the OS holds nothing while the device call succeeds, which is exactly the state a
capability trusting its own return value would report as working. **That is why `WasapiAudioPresence`
takes its read-back as an injected delegate**: not for convenience, but because the branch that only
exists for a disagreement between the API and the OS is unreachable on a machine where they agree.
The same reasoning produced `ACueThatStartsWhileTheOsStopsConfirming_IsDEGRADED_NamingWhichHalfHolds`.

Both mutations restored; the file is byte-identical to the committed version.

---

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
`AudioCapabilityTests.cs` (**15**), `AudioModuleSpineTests.cs` (**21**).

**Tests — changed.** `StudioRackHeadlessTests.cs` (**+5**, plus the row lists and group list extended
from five names to seven), `ContinuousEffectSpineTests.cs` and `SecondEffectSpineTests.cs` (refusal
and rack-order lists extended — **0 count change**).

**Docs.** `client/docs/wpf-surface-reachability.md` (D101–D109),
`client/docs/verification-harness.md` (the audio evidence class).

---

## 8. Divergences from D101 onward, in one line each

Full rows with citations are in `client/docs/wpf-surface-reachability.md` §SP-109.

| # | In one line |
|---|---|
| **D101** | Brain Drain ships as its audio half; the desktop blur is absent and is stated on the row, in the panel and in a `Degraded` arm on every run |
| **D102** | The dot gains an OS clause — `Live` needs a confirmed render session, not just a firing on the clock |
| **D103** | One playback device for the app instead of a fresh `WaveOutEvent` per clip |
| **D104** | Clips read from `<dataDir>/assets/sounds/<module>`, and a missing folder is named rather than created |
| **D105** | A moved volume slider takes effect on the next cue, not on the clip already playing |
| **D106** | Brain Drain gets its own volume dial because the port has no master volume |
| **D107** | Loop mode, the crossfade engine, Clean Slate, the custom clip, `TriggerOnce`, session mode, Discord presence and both level unlocks are not ported |
| **D108** | **A discrepancy in the SOURCE, not a port choice**: Mind Wipe's comment says "30-second window" and its arithmetic is `perHour/360` on a 10-second timer. The arithmetic is the behaviour; following the comment would fire a third as often |
| **D109** | Linux refuses typed with a four-step manual gate — because the backend WORKS there and the OS-side proof does not exist here |
