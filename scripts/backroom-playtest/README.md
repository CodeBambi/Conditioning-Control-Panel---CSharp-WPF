# Back Room playtest voice overlay

This is the exact browser voice helper used by the isolated playtest. The desktop
host uses its existing clip resolver and Resources/Audio/backroom/words/words.json.
No ElevenLabs key or generation call is needed at runtime.

Copy __phone-voice.js to the staging root. Copy the nine mp3 files from
ConditioningControlPanel/Resources/Audio/backroom/words into backroom/voice there.
The files have 55 ms fade-in, 140 ms fade-out and a quiet normalized level. The helper
adds gain 0.65 through the host's existing master-volume AudioContext wrapper.

The private preview host uses the following integration. These snippets preserve
only the client voice wiring; server modules and the ledger stay outside this repo.

At host initialization:

```js
let presetVoice;
const voiceReady = import('/__phone-voice.js').then(m => (presetVoice = m.createPresetVoice()));
const armVoice = () => { if (presetVoice) presetVoice.arm(); else voiceReady.then(v => v.arm()); };
window.addEventListener('pointerdown', armVoice, { passive: true });
window.addEventListener('keydown', armVoice);
document.addEventListener('visibilitychange', () => { if (document.hidden) presetVoice?.stop(); });
window.addEventListener('pagehide', () => presetVoice?.stop());
```

When settings disable the subliminal gate:

```js
if (live.gates.subliminal === false) presetVoice?.stop();
```

In the existing bridge message handler (emit is its reply callback):

```js
if (m.type === 'word.speak') {
  const answer = live.gates.subliminal === false
    ? Promise.resolve({ source: 'preset', durationMs: 0 })
    : voiceReady.then(v => v.speak(m));
  answer.then(ack => emit({ type: 'word-ack', token: m.token, ...ack }));
}
if (['word.stop', 'suspend', 'close', 'station-close'].includes(m.type)) presetVoice?.stop();
```

A preset acknowledgement intentionally prevents the browser TTS fallback when a
clip is unavailable. Playback has an 800 ms load deadline, replaces previous speech,
cancels pending requests on stop and reverses a copy of samples for reversed speech.
Run `node --test scripts/backroom-playtest/voice.test.mjs` from the repo root.
The deployed preview is a separate artifact; merging this helper does not deploy it.
