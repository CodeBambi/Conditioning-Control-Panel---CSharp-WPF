# Census #115 — the audio dials on the Studio door

Checkpoint file. Removed before the lane reports complete.

## Upstream's shape (verified, not trusted)

- `Views/Controls/AppSettings/AudioSettingsSection.xaml:82-89` master slider 0..100 + `TxtMaster` "N%".
- `:103-110` video slider 0..100 + `TxtVideoVolume` "N%".
- `:181-190` `CmbAudioOutputDevice` + `BtnAudioOutputRefresh` ("↻").
- `:195-201` `BtnTestAudio`.
- Handlers `MainWindow/MainWindow.UiUpdates.cs:1008-1021` (master), `:1023-1030` (video),
  `:1098-1138` (populate; index 0 = System default; id-then-name restore; populate suppresses
  the write-back via `_populatingAudioOutputs` at `:1096`), `:1140-1157` (selection persists +
  invalidates), `:1159-1162` (refresh), `:1065-1091` (test audio, re-entrancy guard `:1063`,
  runs OFF the UI thread because a wedged endpoint blocks for many seconds — #686).
- `Services/AudioService.cs:553-643` `TestAudioPlayback`: device probe, first existing of three
  shipped clips, FIXED 0.5 gain ("bypasses curve", `:625`), `"WARNING: No test sound files found
  to play"` when none (`:604`), then a settings readout.
- Defaults verified in `Models/AppSettings.cs:1127` (32), `:1134` (50), `:1131`/`:1138`
  (`Math.Clamp(value, 0, 100)`), `:1238-1246` (empty = system default).
- Enumeration point: `MainWindow/MainWindow.Settings.cs:140`, inside LoadSettings — i.e. when the
  audio door's own state is loaded, not at process start.

## Port decisions

1. **Row**: a new `Audio` rack row at the end of IMMERSION, beside Haptics (the other app-lifetime
   device row). NO DOT and no quick-toggle — the Visuals precedent (`StudioPage.axaml:167-186`,
   `StudioTabView.xaml.cs:494-496`): there is no enable to flip and no schedule to arm.
2. **Enumeration happens on first reveal of the panel, and on Refresh** — never at page
   construction, because the page and all its panels are built at app start, so enumerating there
   would create a native MiniAudio context at every launch for a user who plays nothing. That is
   the class `AudioParticipant`'s phase-3 rule refuses. `Devices()` opens no device
   (`SoundFlowAudioBackend.EnumerateDevices` creates the engine and reads
   `PlaybackDevices`; only `TryInit` opens one), so a picker can list without seizing an endpoint.
3. **A remembered device that is gone degrades honestly**: the fresh enumeration is the truth, the
   stored NAME is shown as a trailing "(not connected)" entry and selected, and the notice says
   sound falls back to the system default until it returns — which is what the arbitration really
   does (`SoundArbitration.cs:325-328`). Upstream silently selects `devices[0]` instead
   (`UiUpdates.cs:1124`) while keeping the stale name in settings.
4. **The panel SAYS the remembered-failure rule**: `DeviceOutcome` + `DeviceInitAttempts` are
   rendered, including "nothing has been asked yet" for null.
5. **Test audio ships**, because its refusal is upstream's own branch rather than a missing
   feature: `EnsureDevice()` (a real device attempt) then `PlaySfx(clip, 0.5f)` — the fixed
   half-gain that bypasses the master curve, `AudioService.cs:625` — drawing from the user clip
   pools the two audio modules already use. This build ships no clip (zero `.mp3/.wav/.ogg` under
   `client/`; `PopQuizEffect.cs:96-99` records the same absence), so an empty pool is refused BY
   NAME at the call site with the folder in the sentence. The whole button runs off the UI thread
   for upstream's stated reason (#686) and is exposed as an awaitable so no test needs a wait.
6. **NO `session-owned` marker on any of the four.** The rule in
   `StudioPage.axaml.cs:1717`'s remarks is: marked iff the document is one of the eleven
   `ScriptedSessionDials` borrows AND upstream marks it owned. `audio.json` is not borrowed, and
   these are COMFORT dials (volumes are named in the never-lock list). Both halves fail; unmarked.
7. **`VideoVolume` has no reader in this build** (grep: one doc mention in
   `MandatoryVideoPresetDocument.cs:17`, no consumer), and the panel says so in those words.
   `MasterVolume` has exactly one — `DtrhHostWindow.axaml.cs:268` → `BarkPipeline.cs:613`, read at
   window construction, so a change reaches the NEXT window. The panel says that too.

## Scope note (reported, never silent)

`StudioPage` is constructed at `Views/MainWindow.axaml.cs:138` and holds no `ApplicationHost`, so
`AudioParticipant.Of(host)` cannot be reached from inside my File Scope. One statement is added at
that one construction site, in the file's existing throwing-resolution convention. Reported.

## Evidence

- Headless: the row, the panel, the four controls, the persisted values, the session-lock
  exclusion, the picker's system-default entry and its stale-name degrade.
- Unit: the notice sentences (`AudioDialsNotices`), including the refusal texts.
- Headed: new surface `audio-dial`, states `live` and `session`, both photographing the master
  slider's filled track; the inversion is that `studio-dial-locked-track` (#333333, the greyed
  livery) must FAIL on the `session` capture while #0078D4 passes — the session-lock decision
  in pixels.
- NOT proven: nothing here says a human heard anything (`audible-verified` is open), which clip
  played, or that any endpoint outside this machine received samples.
