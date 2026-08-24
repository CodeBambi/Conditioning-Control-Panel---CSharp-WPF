# census #96 (resumed) — continuation plan

Prior lane: branch `worktree-agent-aa53447054359c666`, code commit `737a641d5`, cherry-picked here
as `14a2776e1`. Its `plan.md` (`50214da51`) is the specification and is NOT on this branch.

## Verified from source (every prior-lane citation re-read)

| claim | source | verdict |
|---|---|---|
| flash sound is stop-replace | `FlashService.cs:3516` `PlaySound` opens `StopCurrentSound()` `:3518`, one `_currentSound` `:3539` | OK |
| flash marks the bark system busy | `FlashService.cs:1044` `App.Audio?.MarkWhisperAudio(duration)` | OK |
| the port already suppresses barks on it | `Companion/BarkPipeline.cs:416-419` -> `"whisper-active"` | OK |
| flash draws once per event, before showing | `FlashService.cs:617` `GetNextSound()`, `:636` `ShowImages(...)` | OK |
| flash draw is WITHOUT replacement | `FlashService.cs:3315-3329`, refill `:3325`; `ClearFileCache` `:3489` | OK (`:3490` -> `:3489`) |
| flash volume law | `FlashService.cs:3529-3530` `Math.Max(0.05f, pow(v,1.5))` | OK |
| `FlashAudioEnabled` default true | `Models/AppSettings.cs:925` | OK |
| pops overlap <=10, drop the 11th | `AudioService.Playback.cs:111`, `:212-218` | OK |
| `PlayOneShot` refuses volume <= 0 before the device | `AudioService.Playback.cs:183-187` | OK |
| pop volume law | `BubbleService.cs:2002-2006`; `BubbleCountWindow.xaml.cs:1342-1343` + clamp `:221` | OK |
| `BubblesVolume` default 50 | `Models/AppSettings.cs:2790` | OK |
| pops go off the caller's thread | `BubbleService.cs:2022-2027`; `BubbleCountWindow.xaml.cs:1331` | OK |
| SFX pool of 8, owner-ratified | `Audio/SoundArbitration.cs:90-92` | OK, untouched |
| clips do not ship | 118 `.wav` in `Resources/sounds/flashes_audio`, `Pop{,2,3}.mp3` in `Resources/sounds/bubbles` | OK |
| the pop sound is on `_onClickPop` | it is NOT: `_onClickPop` `:3984` is the E-Stim charge | WRONG |

## Three corrections to the prior lane

1. Citation, `BubblePopSurfacePresenter.OnPress`. The pop SOUND rides `_onPop`, not `_onClickPop`:
   `Pop()` guard `BubbleService.cs:3994` -> `_onPop?.Invoke(this)` `:4064` -> `OnPop` `:945` ->
   `AwardAmbientPop` `:950` -> `PlayPopSound` `:961`. The guard line is `:3994`, not `:3990`
   (`:3990` is pre-existing in `BubblePopField.cs` and is left alone - off by four, noted).
   The user-visible property the prior lane claimed is unchanged: one sound per real pop.
2. `EffectSounds.ForProduct`'s `random` parameter is deleted. It handed ONE `Random` to both pools,
   and the two pools are drawn on different threads (flash on the signal thread, pops on a pool
   thread). `AudioCuePool` locks per instance, so a shared `Random` is unsynchronised across two
   gates. Nothing called it with a non-null random. Deleting it removes the race and the parameter.
3. The flash stays inline for a better reason than the prior lane gave. Its stated reason (upstream
   is inline) is true but weak. The load-bearing one: the whisper channel is stop-replace, so two
   flashes posted to a thread pool could start out of order and the FIRST would cut off the SECOND.
   SFX has no such ordering, which is why the pops may leave the thread and the flash may not.

## Remaining work

- `Effects/BubbleCountGame.cs` - `onPop` once per bubble at the pop transition
  (`BubbleCountWindow.xaml.cs:1793-1797` `StartPopping`, guarded by `_isPopping`); replace the
  "Not ported: the pop SOUND" paragraph.
- `Effects/BubbleCountEffect.cs` - thread `Action? onPop` to the run.
- `Session/SessionParticipant.cs` - one `AudioParticipant? appAudio` argument,
  `EffectSounds.ForProduct`, wired to Flash + the bubble-pop presenter + Bubble Count.
- `Lifecycle/CompositionRoot.cs` - pass the `audio` local (already in scope at `:259`, above `:275`).
- Facts in `client/tests/CcpClient.Tests/` only. No Avalonia runtime is needed by any of it.

## Reported, not widened

`DtrhHostWindow.axaml.cs:288-293` already NAMES this: `PanicReset()` on window close stops EVERY
channel, and "the day a second one lands this call has to become a stop of the channels this window
used". This packet IS that second consumer. Both halves of the fix (`Features/Dtrh/**` and a
per-channel stop on `Audio/**`) are outside File Scope. Reported.
