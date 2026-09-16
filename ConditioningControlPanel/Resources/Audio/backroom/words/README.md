# Back Room word clips

The Back Room slot's subliminal beat is spoken by the host, not by the browser. `BackRoomVoice.cs` looks
for a voice in this order (CONTRACT.md 10.21):

1. **`clip`** - the player's OWN audio for that phrase (an enabled Awareness trigger's PlayAudio action,
   the active mod's `resources/sounds/flashes_audio`, then `Resources/sub_audio`).
2. **`preset`** - a clip in THIS folder, named by `words.json`.
3. **`tts`** - Windows speech, rendered on the fly.
4. **`none`** - the page falls back to the browser's `speechSynthesis`.

This folder includes nine Circe recordings for the English playtest vocabulary: Drop, Relax, Let go, Sink, Deeper, Empty, Obey, Softer and Blank. Player-provided clips keep priority. Other phrases retain the existing fallback chain.

## Naming

The manifest key is the **normalised phrase**: lower case, every run of non-letter / non-digit collapsed
to a single space, trimmed. `"Let Go!"`, `"let  go"` and `"LET GO"` are all the key `let go`.

The file is that phrase as a **slug** - the same string with spaces as hyphens - plus the extension:

| Phrase | Key | File |
|---|---|---|
| `Drop` | `drop` | `drop.mp3` |
| `Let Go` | `let go` | `let-go.mp3` |
| `Good Girl` | `good girl` | `good-girl.mp3` |

```json
{
  "version": 1,
  "words": {
    "drop": "drop.mp3",
    "let go": "let-go.mp3"
  }
}
```

File names must match `^[a-z0-9][a-z0-9_.-]{0,63}$` and are only ever looked for inside this folder;
anything else in the manifest is dropped without a word. `.mp3`, `.wav` and `.ogg` all play (NAudio
reads them all). Reversed playback is rendered from whatever is here, so the format does not matter.

## Generating

`node scripts/backroom-words-manifest.mjs` prints the phrases the preset lexicon ships with, their keys
and their file names, and `--json` prints a ready-made manifest to paste in. Feed the phrase list to the
ElevenLabs pipeline, save each clip under the file name the script printed, then fill `words.json`.

Keep them SHORT (under a second where the phrase allows) and soft: the beat holds a word for 980 ms and
a clip longer than that pushes the next word of a chain back.

## Circe recording pass (2026-09-16)

ElevenLabs eleven_v3, configured Circe voice. Each exact phrase was rendered as `[whispers] [slow] phrase...`, stability 0.5, similarity 0.85, style 0.15, speed 0.85, speaker boost off. Delivery was requested as a quiet, measured whisper; human audition remains the quality check.

Processing: silence trimmed with breath margins retained, 8.5 kHz low-pass, loudness target -25 LUFS and -8 dBTP ceiling, 55 ms fade-in and 140 ms fade-out, mono 44.1 kHz MP3 at 96 kbps. Complete phrases retained (about 0.6 to 2.6 seconds), without a hard duration cut. The word chain waits for the reported clip duration. The phone preview uses an additional 0.65 gain through its master volume and acknowledges these clips as preset audio instead of invoking browser speech.
