# "She Can Hear You Now" — Voice Port Status (v6.2.x merge → Avalonia)

Tracks porting the merged WPF voice/companion changes (~3,500 lines) to the Avalonia head.

**Runtime status (2026-06-28):** the Vosk model is now in place — `vosk-model-small-en-us-0.15`
was downloaded from the official source (alphacephei.com/vosk/models) and unpacked at
`ConditioningControlPanel/Resources/Models/vosk/vosk-model-small-en-us-0.15/` (gitignored, ~40 MB,
same policy as the ONNX models). The engine init is **runtime-verified**: a throwaway probe that
reproduced `WindowsSpeechService.EnsureModel()` exactly (resolve dir → `new Model(dir)`) succeeded
with exit 0 — native libvosk loads and the model is valid, so `IsAvailable` returns true when a
capture device is present. End-to-end mic recognition still needs a live run on the Windows head.

## ✅ Done (compiles clean)

### Speech engine (seam)
- `CCP.Core/Services/Speech/ISpeechRecognitionService.cs` — interface + `PhraseResult`/`RecognizeOptions`/`SpeechInputDevice` + `SpeechMatching` (Normalize/Similarity).
- `CCP.Avalonia/Services/Speech/NullSpeechService.cs` — no-op default.
- `CCP.Avalonia.Desktop.Windows/WindowsSpeechService.cs` — full Vosk/NAudio port (DI: `ISettingsService`+`ILogger`). Vosk 0.3.38 pkg + model-copy added to the Windows csproj.

### Voice command system (the headline)
- `CCP.Core/Services/Speech/VoiceCommandGrammar.cs` — 37-intent grammar (~509 phrasings, verbatim) + fuzzy `Match` router.
- `CCP.Avalonia/Services/Autonomy/AvaloniaAutonomyService.Voice.cs` — wake-word loop, serialized funnel, **24-command dispatch** → existing Avalonia services, **voiced confirmations** (bark manifest → avatar), **mantra fallback**. Armed via `Start()`/`RefreshVoiceInputModes()`, torn down in `Dispose()`.

### Substrate
- `CCP.Core/Services/Bark/BarkRuleLoader.cs` + `IBarkManifestService.cs` (BarkManifestService: `PickVoiceLine` + `ResolveModAudio`).
- `IAvatarWindowService.GigglePriority` widened with `phraseAudioPath`/`barkVoice` (App.cs + AvaloniaHeadStubs) — exposes the avatar's existing voiced-clip playback.
- `CCP.Core/Services/Mantra/MantraVoiceService.cs` (+ `IAudioDurationProvider` seam; `NAudioDurationProvider` on Windows).

### Standalone fixes (#5)
- PopQuizWindow self-close watchdog: `IsLoaded` not `IsVisible` (survives minimize).
- `WindowsSystemAudioDucker`: ducks all active render endpoints (WPF bug #415).

## ✅ Also done since
- **She's Listening tab (#3)** — `SheListeningTabViewModel` + `SheListeningTabView.axaml` + DataTemplate + DI + tab-list; binds the voice settings, lists mic devices, re-arms via `RefreshVoiceInputModes`. (Consent is an inline checkbox; full modal dialog deferred.)
- **Deeper "Speak" effect (#4)** — `ISpeakPromptHost` seam + `EffectTypes.Speak` case in `RealActionDispatcher` + `AvaloniaSpeakPromptHost` (cue via bubble + recognizer + feedback + reps). Region-hold + dedicated cue overlay deferred.

## ⏳ Remaining (coupled to unported subsystems / regression-risky — best done with the model + runtime verification)

| Item | Status | Why it's not a clean drop-in |
|---|---|---|
| **Push-to-talk** (#2 deferral) | **✅ done** | `KeyNameToVk` map (F1–F24, A–Z, 0–9, specials) + subscribe/unsubscribe `OnPttKeyPressed` in `RefreshVoiceInputModes`; default F8. |
| **keyword + quiz commands** (#2 deferral) | **✅ done** | `quiz_once`→`_popQuiz.ShowPopQuiz()`, `keyword_on`/`keyword_off`→`_keywordTriggers.Start()/Stop()` wired into `BuildDispatch()`. |
| **~5 commands w/o targets** (#2 deferral) | not started | no Avalonia service method / no surface: video pause/resume (not on `IVideoService`), freeze, screen-shake, session pause/resume |
| **Trigger Bubbles** (#5) | **core ✅ done** (gated) | `BubbleState.EffectPayload` + gated `RollTriggerPayload()` in `BubbleEngine.SpawnBubble` + `Fire()` on ambient pop + `AvaloniaEffectPayloadFactory` (variant→payload) wired into `AvaloniaBubbleService`. Fully gated on `BubbleTriggersEnabled` (default off) → no impact on the normal pop game. DEFERRED: the avatar "pops-it" egg (needs avatar-glide substrate) + effect-bubble tint (`BubbleState` has no render colour). Runtime-verify the effect actually fires on pop once enabled. |
| **Bark parity** (#5, +57) | **blocked** | the portable slice (`PickVoiceLine`/`ResolveModAudio`) is DONE. The rest (`webcam_running` predicate, one-shot gate refactor) lives in the **unported 1,678-line reactive `BarkService`** (LOC corrected 2026-07-10 against `Services/.../BarkService.cs`) (stubbed on Avalonia as a chaos→event bridge). Porting that engine is a separate large effort (a non-goal of this merge port). |

## Notes / decisions
- The full reactive **bark engine** (1,678 lines, ~15 service subscriptions) is **not** ported — only the manifest slice the voice layer needs. The Avalonia `AvaloniaBarkService : IBarkService` is a different, minimal chaos→event bridge.
- Voiced confirmations achieve **full parity** (per-mod audio clips), not text-only — because the avatar already supported clip playback and the bark-manifest slice resolves the clips.
- DI: all new services registered in `CCP.Avalonia/ServiceCollectionExtensions.cs`; Windows overrides (`WindowsSpeechService`, `NAudioDurationProvider`) in `CCP.Avalonia.Desktop.Windows/Program.cs`.

## To verify any of this
1. ~~Drop a Vosk small model into `ConditioningControlPanel/Resources/Models/vosk`~~ — **done** (2026-06-28): `vosk-model-small-en-us-0.15` present (downloaded from the official source); engine init runtime-verified (a throwaway probe reproducing `WindowsSpeechService.EnsureModel()` — resolve dir → `new Model(dir)` — succeeded with exit 0). The loader accepts the model root or a single nested folder containing `am/` + `conf/`; the csproj `Resources\Models\**\*` glob copies it to build/publish output automatically.
2. Run the Windows head; enable mic consent + wake word (settings exist: `MicConsentGiven`, `SpeechWakeWordEnabled`, `SpeechWakeWords`) — the She's Listening tab (#3) now exposes these (consent is an inline checkbox).
3. Start Takeover (arms the wake loop), say "Hey Bambi … bubbles" etc.
