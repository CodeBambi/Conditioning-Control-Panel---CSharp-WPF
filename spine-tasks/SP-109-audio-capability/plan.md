# SP-109 — plan checkpoint

Branch `lane/SP-109-audio-capability`, base `2b348901`, worktree
`.claude/worktrees/agent-ad52f5655399f37cf`. Written **before the first product edit**.

The packet says the plan checkpoint is the whole packet, so this file is not a sketch: it is a
**measurement**, taken on this machine, of exactly what the operating system will and will not tell
me about sound. Nothing below is recalled or assumed.

---

## 0. THE MEASUREMENT, taken first

I did not design and then hope. I wrote the independent WASAPI probe
(`client/tests/CcpClient.Tests/WasapiRenderProbe.cs`), drove the port's existing
`SoundFlowAudioBackend` through open → play → stop → teardown, and read the OS back at every step.
**Every GUID and vtable order in that probe was read out of the Windows SDK headers on this machine**
(`10.0.26100.0`: `um/mmdeviceapi.h:1538,754,565,424`, `um/audiopolicy.h:1155,762,1058,337,545`,
`um/endpointvolume.h:842`, `um/audiosessiontypes.h:149-154`) rather than recalled.

Raw output, one run, 2026-08-19:

```
windows=True pid=24008
activeRenderEndpoints=1
session BEFORE any audio:      EndpointReachable=True SessionsOnEndpoint=5 SessionForThisProcess=False State=-1 Meter=False Peak=0
soundflow devices (1):         Speakers (Realtek(R) Audio)
TryInit -> True err=
session AFTER device init:     EndpointReachable=True SessionsOnEndpoint=6 SessionForThisProcess=True  State=1 Meter=True  Peak=0
player created state=Stopped;  after Play() state=Playing
session DURING playback:       EndpointReachable=True SessionsOnEndpoint=6 SessionForThisProcess=True  State=1 Meter=True  Peak=0.4049835
session AFTER Stop():          ...                                          State=1 Meter=True  Peak=0.4049835   (meter holds)
session AFTER backend dispose: ...                                          State=0 Meter=True  Peak=0
```

## 1. WHAT I CAN PROVE FROM THE OS — the chain, link by link

Each link names the API and what a passing assertion means. All four are facts the Windows audio
engine produces about this process; none of them is "my code returned".

| # | Fact | API asked | Measured here |
|---|---|---|---|
| **F1** | The OS reports N ACTIVE render endpoints | `IMMDeviceEnumerator::EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)` → `IMMDeviceCollection::GetCount` | 1 (`Speakers (Realtek(R) Audio)`) |
| **F2** | After the port opens a device, the OS's session enumerator on the default console endpoint holds a session **whose owning process id is this process** — one that did not exist a moment earlier | `IMMDevice::Activate(IID_IAudioSessionManager2)` → `GetSessionEnumerator` → `IAudioSessionControl2::GetProcessId` | absent before (5 sessions, none ours); present after (6 sessions, one ours) |
| **F3** | That session's state is `AudioSessionStateActive`, and it falls to `AudioSessionStateInactive` when the port tears the device down | `IAudioSessionControl::GetState` | 1 while up, **0 after dispose** |
| **F4** | **The Windows audio engine metered NON-ZERO sample values on the stream this process owns**, and read ZERO on the same stream while the device was open but nothing was cued | `IAudioSessionControl` → QI `IAudioMeterInformation::GetPeakValue` | **0 → 0.405 → back to 0 after teardown** |

**F4 is the link that makes this packet different from the four shapes the port has rejected.** The
number does not come from my code; it comes from the OS mixer's own metering of the samples it
consumed from our stream. And it has a **built-in negative control that I measured rather than
assumed**: with the device open and started but no clip cued, the same meter reads **0**. So the fact
cannot pass vacuously — a product that opened a device and played nothing would read 0 and red.

That the QI from `IAudioSessionControl` to `IAudioMeterInformation` works at all is **measured**
(`Meter=True`), not taken from documentation.

## 2. WHAT I CANNOT PROVE — where the chain STOPS, named exactly

The chain stops at the boundary of the Windows audio ENGINE. Past that point I have no instrument
and will claim nothing:

1. **Not that the endpoint was audible.** F4 measures the session's stream. It says nothing about
   endpoint mute, endpoint volume, an exclusive-mode holder, a disabled jack, a DAC, an amplifier, or
   whether speakers/headphones are plugged in and powered.
2. **Not that the sound was the right sound.** A peak proves non-silent samples, not which clip, not
   the right channel, not the intended moment.
3. **Not that the endpoint is physical at all.** An RDP/virtual sink reports F1–F4 identically with
   no physical output anywhere — this port has already recorded exactly that class on WSLg
   (`client/docs/audio-backend-spike.md:24,88` — `RDP Sink`, and no `pactl` present to check it).
4. **Not that a human heard it.** That is a **named manual gate**, discharged by a person, never by
   this suite, and never implied by a green run.

## 3. THE API I WILL ASK, and where each copy lives

Two independent copies, deliberately, on the `TrayShellProbe` precedent:

- **Product** — `Audio/WasapiRenderReadback.cs`: what the presence asks to EARN `Available`.
- **Tests** — `WasapiRenderProbe.cs` (written, above): what the suite asks to CHECK the product's
  claim. Separate declarations so "the product says it holds a render session" and "the OS says a
  render session belongs to this pid" are two facts from two code paths.

Assertions take the `TrayObservations` shape — `Assert.Equal(machineFact, productClaim)` at
statement depth 0, no predicate that can silence anything. On a box with no endpoint both sides are
false and the fact still bites.

## 4. ONE MODULE OR TWO — **two**, and the second one ships as an explicit HALF

**Mind Wipe** lands whole. **Brain Drain** lands as its audio half only, and the missing half is
made loud in three places rather than left silently absent:

1. Its arm result is **always `CapabilityState.Degraded`** with a new typed code
   `braindrain-visual-half-absent` — on a completely healthy run, not only on failure. Its
   `SurvivingSemantics` says the audio half is running; its `Reason.Detail` names the desktop blur,
   its upstream mechanism and why the port cannot draw it.
2. Its rack row is titled **"Brain Drain (audio half)"** — the user reads the scope on the row.
3. Its panel carries a permanent notice naming the blur, its 30–60 FPS screen-capture pump
   (`Services/Notifications/OverlayService.cs:1965-1995`), the read-back capability the port lacks,
   and the fact that the upstream panel itself calls that the **"VISUAL half"**
   (`Views/Controls/Studio/BrainDrainFeatureControl.xaml.cs:170`).

Why two and not one: the capability is only interesting once a SECOND consumer proves it is not
shaped around one module, and the two modules genuinely differ where it counts — a fixed 10 s window
vs a 500 ms/5 s window the high-refresh dial changes (`MindWipeService.cs:127-129` vs
`BrainDrainService.cs:60-69`), `perHour/360` vs `intensity/100/(60/intervalSec)` (`:734` vs `:183`),
clamps 1..180 vs 1..100 (`:98-99` vs `:49`).

## 5. THE FIFTH DOT MEANING — decided: **`Live`**, and the rule that settles it

Trap 2 is right that no existing rule settles it: SP-105 says `Armed` (half of it cannot draw), the
Subliminals rule says `Live` (the schedule is genuinely on the clock).

**The fifth meaning is REACH, and it has two clauses:**

> **`Live` = the module's work is running AND its output can reach the user — where "can reach the
> user" is, for the first time, a fact about a resource this process does not own and cannot see
> directly. It has to ASK the operating system.**
>
> ```
> Live  =  a firing is on the clock  &&  the OS reports an active render session for this process
> ```

And the sub-rule that answers the half-row:

> **The dot is scoped to what the row IS, and a row that is a proper SUBSET of its upstream must say
> so where the user reads it.** Brain Drain's dot is `Live` because everything this row claims to be
> is running and audible; the un-ported blur was never a mechanism this row has. A dot reading
> `Armed` while clips are firing would be the exact opposite lie from the one SP-105 prevented — it
> would under-claim a module that is really working.

Why this does not contradict SP-105: SP-105's `Armed` is for a module whose **whole** output channel
is down (a refused overlay = nothing at all reaches the user). That case exists here too and is
implemented: **when the audio capability is `Unavailable` — no endpoint, or Linux — both modules read
`Armed`, never `Live`**, because then nothing reaches anyone. Upstream has the same gate in the same
place: `if (App.Audio?.IsOutputSuppressed == true) return;` (`MindWipeService.cs:770`,
`BrainDrainService.cs:211`). And the empty-clip-folder case takes the SUBLIMINALS answer —
`Degraded` arm, `Live` dot — because that is a pool, not a channel.

## 6. LINUX — typed refusal, and the honest reason is NOT "Linux cannot play audio"

SoundFlow/miniaudio ships `libminiaudio.so` and enumerates on Linux today
(`audio-backend-spike.md:13,39`). So the refusal is precisely: **this build cannot EARN `Available`
on Linux, because the read-back that gives `Available` its meaning is WASAPI-only** and no
PipeWire/PulseAudio equivalent is implemented or verified here. Selection by platform never produces
`Available` (runtime-capability-contract §2 rule 2); a Linux no-op is banned. The refusal carries a
gate naming `pactl list sink-inputs` / `pw-dump` as the route, and states that WSLg cannot discharge
it (`RDP Sink`, no `pactl` — `audio-backend-spike.md:24,88`).

## 7. What I will NOT duplicate

`Audio/AudioSeams.cs` already owns `IAudioBackend`, `IAudioPlayer`, the F1 device discipline, the
SP-025 off-sync-context rule and the SP-072/SP-083 orphan-safe bounded player factory. **None of it
is re-implemented.** The new capability is a layer ABOVE `IAudioBackend` that adds exactly one thing
it does not have: the OS read-back that turns "a method returned" into "the OS says so".
`SoundArbitration` is not touched (its DTRH/companion channel model is a different owner — SP-029);
that boundary is recorded, not silently crossed.

## 8. Files I expect to touch

Product: `Audio/{AudioReasonCodes,IAudioPresence,UnsupportedAudioPresence,AudioPresenceFactory,WasapiRenderReadback,WasapiAudioPresence}.cs`;
`Effects/{AudioCuePool,AudioCueSchedule,MindWipeEffect,BrainDrainEffect}.cs`;
`Session/{MindWipePresetDocument,BrainDrainPresetDocument}.cs`, `Session/EffectReasonCodes.cs`
(additive), `Session/SessionParticipant.cs`; `Views/Pages/{StudioPage.axaml,StudioPage.axaml.cs,AudioPanelNotices.cs}`.
Tests: `WasapiRenderProbe.cs`, `TestWav.cs`, `AudioObservations.cs`, `AudioCapabilityTests.cs`,
`AudioModuleSpineTests.cs`, headless `StudioRackHeadlessTests.cs`.
Docs: `client/docs/wpf-surface-reachability.md` (D101+), `client/docs/verification-harness.md`
(audio evidence class).

Out of scope and untouched: `Overlay/**`, `client/tools/**`, `floor.json`, `task-board.md`,
`ConditioningControlPanel/**`.
