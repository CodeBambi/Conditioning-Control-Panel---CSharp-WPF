# Back Room word clips

The Back Room slot's subliminal beat is spoken by the host, not by the browser. `BackRoomVoice.cs` looks
for a voice in this order (CONTRACT.md 10.21):

1. **`clip`** - the player's OWN audio for that phrase (an enabled Awareness trigger's PlayAudio action,
   the active mod's `resources/sounds/flashes_audio`, then `Resources/sub_audio`).
2. **`preset`** - a clip in THIS folder, named by `words.json`.
3. **`tts`** - Windows speech, rendered on the fly.
4. **`none`** - the page falls back to the browser's `speechSynthesis`.

This folder ships with `words.json` and nothing else. Drop clips in and name them in the manifest and
every player hears them in place of the synthesised voice; leave it empty and nothing breaks.

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
