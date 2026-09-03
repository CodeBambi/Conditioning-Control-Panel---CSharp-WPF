# Vosk offline speech model (Takeover "repeat after me")

`Services/Speech/SpeechService.cs` loads a [Vosk](https://alphacephei.com/vosk/models) acoustic
model from this folder at runtime. **The model binaries are NOT committed** (same policy as the
ONNX models in `Resources/Models/`) — they're dropped in by the build/release process.

Until a model is present here, `App.Speech.IsAvailable` is `false` and every recognize call
returns `PhraseResult.NotAvailable`, so the app runs normally and the voice action simply never
fires. No crash, no prompt.

## Which model

Two options (both support the runtime grammar JSON we build in `SpeechService.BuildRecognizer`):

- **`vosk-model-en-us-0.22-lgraph`** (~128 MB) — **recommended.** Much more accurate acoustic model,
  so command/grammar recognition is noticeably more reliable than the small model. Still supports
  the dynamic grammar constructor (it's the `-lgraph` / large-graph variant).
- `vosk-model-small-en-us-0.15` (~40 MB) — the original lightweight model; fine but less accurate.

Get either from <https://alphacephei.com/vosk/models>.

> ⚠️ Do **not** ship the full `vosk-model-en-us-0.22` (1.8 GB). Its static HCLG graph **ignores the
> grammar JSON** we pass, so closed-command recognition silently degrades to open dictation. Only the
> small and `-lgraph` models honour the grammar.

You can drop the lgraph model in **alongside** the old small folder — `ResolveModelDir()` ranks
`lgraph` ahead of `small`, so the upgrade is picked automatically without deleting the old one first.

> Wake word: "Hey Bambi" is out-of-vocabulary for any Vosk model, so a model upgrade improves
> commands but not wake reliability. The dedicated sherpa-onnx KWS spotter (`../sherpa-kws/`,
> open-source + offline + no key) handles wake; Vosk is the fallback when it isn't installed.

## How to install it

Unpack the zip so this folder contains **either**:

1. the model files directly here:
   ```
   Resources/Models/vosk/am/  conf/  graph/  ivector/  README
   ```
2. **or** a single nested model folder (the way the official zip unpacks):
   ```
   Resources/Models/vosk/vosk-model-small-en-us-0.15/am/ conf/ ...
   ```

`SpeechService.ResolveModelDir()` accepts both layouts (it looks for a dir containing `am/` + `conf/`).

The existing `Resources\Models\**\*` content glob in `ConditioningControlPanel.csproj` copies
everything here to the output/publish folder automatically — no csproj change needed.

## Notes

- Native `libvosk` ships inside the `Vosk` NuGet package and extracts via
  `IncludeNativeLibrariesForSelfExtract` for the single-file build.
- Privacy: audio is captured in-memory via NAudio, fed straight to Vosk, and never written to
  disk or transmitted. The mic only opens during an explicit listen window and only after
  `MicConsentGiven` is set.
