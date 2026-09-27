# Vosk offline speech model (Takeover "repeat after me")

`Services/Speech/SpeechService.cs` loads a [Vosk](https://alphacephei.com/vosk/models) acoustic
model from this folder at runtime. **The model binaries are NOT committed** (same policy as the
ONNX models in `Resources/Models/`) — they're dropped in by the build/release process.

Until a model is present here, `App.Speech.IsAvailable` is `false` and every recognize call
returns `PhraseResult.NotAvailable`, so the app runs normally and the voice action simply never
fires. No crash, no prompt.

## Which model

Two options (both support the runtime grammar JSON we build in `SpeechService.BuildRecognizer`):

- **`vosk-model-small-en-us-0.15`** (~40 MB) - **start here.** Lightweight, grammar-capable, and
  the one every working install in the wild is running.
- `vosk-model-en-us-0.22-lgraph` (~128 MB) - a more accurate acoustic model, so command/grammar
  recognition is a little more reliable. Still supports the dynamic grammar constructor (it's the
  `-lgraph` / large-graph variant).

Get either from <https://alphacephei.com/vosk/models>.

> **The 128 MB one is not harder for the app; it is harder to unpack.** Every support thread about
> it has been a LAYOUT problem, not a size or a memory one: its zip is bigger, so people reach for
> "Extract All", which defaults to a subfolder named after the zip and lands the model one level
> deeper than the instructions below describe. Until 2026-09-21 the resolver only looked one level
> down and reported that as *no model at all* - the hint read "no speech model installed yet" while
> the user was staring at the model they had just put there. Two levels are searched now, so both
> unpack styles work, but check the layout first if a model does not take.

> ⚠️ Do **not** ship the full `vosk-model-en-us-0.22` (1.8 GB). Its static HCLG graph **ignores the
> grammar JSON** we pass, so closed-command recognition silently degrades to open dictation. Only the
> small and `-lgraph` models honour the grammar.

You can drop the lgraph model in **alongside** the old small folder — `ResolveModelDir()` ranks
`lgraph` ahead of `small`, so the upgrade is picked automatically without deleting the old one first.

> Wake word: "Hey Bambi" is out-of-vocabulary for any Vosk model, so a model upgrade improves
> commands but not wake reliability. The dedicated sherpa-onnx KWS spotter (`../sherpa-kws/`,
> open-source + offline + no key) handles wake; Vosk is the fallback when it isn't installed.

## How to install it

**Where this folder is:** `%LOCALAPPDATA%\Programs\ConditioningControlPanel\Resources\Models\vosk`,
beside the executable. It ships with the app (this README is in it), so it is normally already
there.

> **It is NOT the folder the Assets page opens.** That button opens the user-data folder, where
> your media and content packs live, and there is no `Resources` inside it. Looking for
> `Resources\Models\vosk` under that folder is the single most common way this goes wrong - it was
> the whole of a support thread on 2026-09-17.

Rather than navigating there by hand: when the voice hint says the model is missing (Bambi
Takeover, or the She's Listening status card), **Open the models folder** takes you straight to it,
creating it first in the unlikely case it has gone missing.

If the app is installed somewhere you cannot write to, copying files in will need the elevation
prompt Windows shows for that folder, or an administrator. The default per-user install under
`%LOCALAPPDATA%` does not.

Unpack the zip so this folder contains **any** of:

1. the model files directly here:
   ```
   Resources/Models/vosk/am/  conf/  graph/  ivector/  README
   ```
2. a single nested model folder (the way the official zip unpacks):
   ```
   Resources/Models/vosk/vosk-model-small-en-us-0.15/am/ conf/ ...
   ```
3. the same folder twice, which is what Windows' "Extract All" produces:
   ```
   Resources/Models/vosk/vosk-model-en-us-0.22-lgraph/vosk-model-en-us-0.22-lgraph/am/ conf/ ...
   ```

`SpeechService.ResolveModelDir()` accepts all three (it looks, two levels down, for a dir
containing `am/` + `conf/`). Deeper than that is not searched: move the model up.

The existing `Resources\Models\**\*` content glob in `ConditioningControlPanel.csproj` copies
everything here to the output/publish folder automatically — no csproj change needed.

## Notes

- Native `libvosk` ships inside the `Vosk` NuGet package and extracts via
  `IncludeNativeLibrariesForSelfExtract` for the single-file build.
- Privacy: audio is captured in-memory via NAudio, fed straight to Vosk, and never written to
  disk or transmitted. The mic only opens during an explicit listen window and only after
  `MicConsentGiven` is set.
