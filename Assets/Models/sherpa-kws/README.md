# "Hey Bambi" wake word — sherpa-onnx keyword spotting (offline, open-source, no key)

`Services/Speech/SherpaWakeService.cs` uses [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)
(next-gen Kaldi, Apache-2.0) as the dedicated **wake-word** spotter for "Hey Bambi". It runs only
the wake stage — everything after the wake (commands, mantras, "repeat after me") stays on the Vosk
model in `../vosk/`.

**Why a separate engine:** "Bambi" isn't an English dictionary word, so Vosk can't put it in a closed
grammar (the out-of-vocabulary token makes the grammar ctor throw) and falls back to a free,
unconstrained recognizer on the small model — which mis-hears the name roughly half the time.
sherpa-onnx KWS is an **open-vocabulary** spotter: the wake phrase is supplied as a subword-token
sequence in `keywords.txt`, so a novel name is just another token string. **Fully offline. No API key,
no account, no network — ever.**

**This is optional.** Until the model files are present, `App.WakeWord.IsAvailable` is `false` and the
wake loop transparently falls back to the Vosk wake path. Nothing crashes; wake just stays at its old
reliability.

## Setup (one-time)

1. **Download the English KWS model** — the streaming-transducer keyword-spotting model:
   `sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01`
   from the sherpa-onnx model releases:
   <https://github.com/k2-fsa/sherpa-onnx/releases/tag/kws-models>
   (Direct: search that page for `sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01.tar.bz2`.)

2. **Unpack the model files into this folder.** `SherpaWakeService.FindModel()` resolves them by name,
   so either layout works (files directly here, or in a single nested folder):
   ```
   Resources/Models/sherpa-kws/
     encoder-*.onnx     (int8 variant preferred if both are present)
     decoder-*.onnx
     joiner-*.onnx
     tokens.txt
     keywords.txt       ← already committed in this folder (the "hey bambi" keyword)
   ```
   The model archive also ships its own `keywords.txt` (example words) — **keep ours**, don't
   overwrite it, or replace its contents with the line below.

That's it — toggle the wake word on under **She's Listening** and say "Hey Bambi". The status line
there shows "✓ Active" once the model is detected.

## The keyword file (`keywords.txt`)

`keywords.txt` (committed here) holds the wake phrase as model tokens, with an optional boost (`:`)
and trigger threshold (`#`). It ships **9 pronunciation variants** of "hey bambi" (token spellings
verified against this model's `bpe.model`) at boost `2.0`, threshold `0.18` — tuned empirically
against 45 TTS clips (3 voices × 3 rates × 5 pronunciations): this set + threshold catches ~84% vs
~67% for a single `hey bambi`/`#0.25` line, at the same low false-wake rate. The variants cover the
common ways the OOV name lands acoustically (bambie / bamby / bambee / bembi / bumbi / bambo / bay
bambi / hi bambi):

```
▁HE Y ▁BA M B I :2.0 #0.18      (hey bambi)
▁HE Y ▁BA M B I E :2.0 #0.18    (hey bambie)
▁HE Y ▁BA M B Y :2.0 #0.18      (hey bamby)
▁HE Y ▁BA M B E E :2.0 #0.18    (hey bambee)
▁HI ▁BA M B I :2.0 #0.18        (hi bambi)
▁BA Y ▁BA M B I :2.0 #0.18      (bay bambi)
▁HE Y ▁BE M B I :2.0 #0.18      (hey bembi)
▁HE Y ▁BU M B I :2.0 #0.18      (hey bumbi)
▁HE Y ▁BA M B O :2.0 #0.18      (hey bambo)
```

- Lower `#threshold` (toward 0) or raise `:boost` if it still misses real wakes; raise `#threshold`
  (toward 1.0) if it false-wakes too much. Tune to taste — no rebuild needed, the engine reads this
  file on (re)init. Note: below ~0.15 recall plateaus (the remaining misses are below the model's
  confidence floor for those acoustics), so dropping it further mostly just adds false wakes.
- To regenerate the token spelling exactly for this model (e.g. to add another phrase), use the
  sherpa-onnx CLI against the model's `bpe.model`:
  ```bash
  pip install sherpa-onnx
  echo "HEY BAMBI" > in.txt
  sherpa-onnx-cli text2token --tokens tokens.txt --tokens-type bpe \
      --bpe-model bpe.model in.txt keywords.txt
  ```
  then append ` :2.0 #0.25` to the generated line.

## Notes

- **Packaging:** the `Resources\Models\**\*` content glob in `ConditioningControlPanel.csproj` copies
  this folder to output/publish automatically — no csproj change. The native sherpa-onnx library ships
  in the `org.k2fsa.sherpa.onnx.runtime.win-x64` NuGet package.
- **Privacy:** identical contract to Vosk — audio is captured in-memory via NAudio, fed straight to
  sherpa-onnx, and never written to disk or transmitted. The wake mic only opens while the wake loop is
  armed (consent given + wake word enabled).
