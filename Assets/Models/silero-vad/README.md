# Silero VAD — speech/noise gate for the "Hey Bambi" wake mic (offline, open-source, no key)

`Services/Speech/SileroVadGate.cs` uses [Silero VAD](https://github.com/snakers4/silero-vad)
(MIT-licensed, ~2 MB) through the sherpa-onnx runtime the wake engine already ships. It replaces the
energy-based noise gate in front of the wake-word spotter whenever the model file is present here.

**Why:** an RMS energy gate can't tell WHAT is loud. A PC fan or AC unit next to the mic raises the
noise floor until quiet speech no longer clears it — in exactly the noisy rooms the gate exists for,
it starts blocking the user. Silero is a small neural net trained to tell *speech* from *noise*
regardless of level, so the fan stays gated while a soft "hey bambi" opens the stream instantly.
**Fully offline. No API key, no account, no network — ever.**

**This is optional.** If the model file is absent, `SileroVadGate.TryCreate` returns null and the
wake path silently falls back to the adaptive energy gate (`NoiseGate` in `MicFrontEnd.cs`). Nothing
breaks; noisy-room wake reliability just stays at its old level.

## Setup (one-time)

Download `silero_vad.onnx` (v4/v5, the 16 kHz ONNX export) and drop it in this folder:

- From the sherpa-onnx model releases:
  <https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models> — asset `silero_vad.onnx`
- Or from the Silero VAD repo's `src/silero_vad/data/` folder.

```
Resources/Models/silero-vad/
  silero_vad.onnx
```

`SileroVadGate.FindModel()` picks any `*.onnx` here (preferring names containing "silero"), so the
exact filename doesn't matter. The `Resources\Models\**\*` content glob in the csproj copies it to
output/publish automatically — no csproj change.

## Notes

- **Runtime:** no new dependency — the `org.k2fsa.sherpa.onnx` package already referenced for the
  wake-word spotter includes the `VoiceActivityDetector` binding.
- **Tuning:** thresholds live in `SileroVadGate.TryCreate` (detection threshold 0.25 — recall-biased,
  since a false "voiced" only feeds the KWS a little noise while a false "silent" swallows the wake;
  ~0.6 s hangover via `MinSilenceDuration`). The pre-roll buffer that protects the speech onset is in
  the wake path itself (`SherpaWakeService.WaitForWakeAsync`), so it applies to the energy-gate
  fallback too.
- **Privacy:** identical contract to the other speech engines — audio is captured in-memory via
  NAudio, fed straight to the VAD, and never written to disk or transmitted.
