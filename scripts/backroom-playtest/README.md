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

## Browser media and options

Copy __phone-fx.js and __phone-options.js to the private playtest staging root.
Load options before room/main.js so its audio and keyboard hooks precede game handlers.
The media adapter provides window.__brMedia and the existing effect globals; the host
continues to own bridge messages, settings, media proxies and the test ledger.
Neither adapter contains the server engine or account-backed state.

Niches are added one at a time with the r/ field. Each pill toggles participation;
its remove button deletes it. All disabled and empty collections persist locally.
Options intercept game shortcuts while open. Desktop WPF settings remain separate.

Validation: browser checks covered typing movement letters, Enter to add, toggle,
remove, reload persistence, an empty collection, and 390px layout. These files are
source backups for the local preview; committing them does not publish that preview.

## Performance and browser lifecycle

The classic effect helper reads `window.__backroomQuality.performance`. The shared
quality module must expose this singleton during boot. Performance keeps the show
moving with four overlapping pictures, one live spiral, 384px overlay sources and
20 fps Loom/melt updates. Melt retains flowing displacement. Reward sparkles and
coins remain owned by the room and station modules.

Effect timers and pending flash imports are cancelled on the existing
`__fxCancelAll()` hook, page hiding and page teardown. The private host should keep
calling this hook when a station closes or settings disable effects.

The staging builder applies `withStaticCaching(config)` from `static-headers.mjs`
to its existing Vercel configuration. Mutable static assets revalidate on reuse;
there are no immutable promises. API and private server cache rules are preserved.
This helper does not deploy anything.

Checks: `node --test scripts/backroom-playtest/fx-performance.test.mjs scripts/backroom-playtest/static-headers.test.mjs`.

## Soundtrack and automatic quality

Copy __phone-music.js beside __phone-options.js and copy backroom/music with the
room assets. Options imports music lazily; no host bridge changes are needed.
The five owner-supplied tracks stream through one audio element, with no full-song
PCM decoding or eager playlist download. Default music level is 15 percent of
master. Zero mutes music. Music and master levels persist separately from quality.
Playback starts with a user gesture, pauses while hidden, and resumes on return.
Each shuffled round includes all five songs; three other songs separate repeats.
The two Midnight Jackpot files are distinct recordings and remain separate tracks.

Quality defaults to Auto and persists as br.quality.v1. Phones and low-memory
hints start in Performance. Desktop Auto reduces presentation costs after sustained
misses; recovery needs longer stability and happens outside a seated game. Motion
and effects intensity remain independent. Full retains existing resolution safety
caps. The room, card painter, wall media and browser effects share this policy.

The music helper reexports the shared room soundtrack. Desktop room Options also
exposes music and quality, with labels in all nine locales. Host suspend/exit pause
or release playback. Physical-device and visual acceptance are owner testing.
