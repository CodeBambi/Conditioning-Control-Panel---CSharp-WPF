# Bundled caption faces

Four faces the Remix Room carries with it, so a caption looks the same on the
phone that made the gif and the phone that opens it. The three faces that came
before them (Display, Mono, Hand) are Windows system stacks and stay that way:
an old remix has to draw exactly as it did.

Every file is the **latin subset only**, as served by Google Fonts, and every
one is under the **SIL Open Font License 1.1**. The licence sits next to the
file it covers, same name, `.OFL.txt`.

| Chip | Family | File | Bytes | Source | Licence |
|---|---|---|---|---|---|
| Block | Anton | `anton-latin.woff2` | 18,612 | [fonts.google.com/specimen/Anton](https://fonts.google.com/specimen/Anton) | `anton-latin.OFL.txt` |
| Script | Pacifico | `pacifico-latin.woff2` | 32,280 | [fonts.google.com/specimen/Pacifico](https://fonts.google.com/specimen/Pacifico) | `pacifico-latin.OFL.txt` |
| Round | Fredoka | `fredoka-latin.woff2` | 16,468 | [fonts.google.com/specimen/Fredoka](https://fonts.google.com/specimen/Fredoka) | `fredoka-latin.OFL.txt` |
| Pixel | Press Start 2P | `pressstart2p-latin.woff2` | 12,512 | [fonts.google.com/specimen/Press+Start+2P](https://fonts.google.com/specimen/Press+Start+2P) | `pressstart2p-latin.OFL.txt` |

About 80 KB for the set, against a 500 KB budget. No serif faces, per the spec.

## How they get on the canvas

`engine/fonts.js` owns the table and loads the files with the `FontFace` API.
`ensureFonts(keys)` is cached and resolves either way, so a file that never
arrives leaves the fallback in `FONTS` (`engine/effects/util.js`) drawing and
never blocks a frame. The project awaits it before an export, the same way it
waits on the decode, and repaints the preview once a face lands.

`remix.css` declares the same four with `@font-face` so the chips in the
Caption panel preview in their own face. Each face is one weight and is
declared at 700, which is the weight a caption is drawn at: an exact match, so
nothing gets a fake bold smeared over it.

## Replacing or adding one

1. Take the latin woff2 and the `OFL.txt` from the family's Google Fonts page.
2. Drop both in here, named `<key>-latin.woff2` and `<key>-latin.OFL.txt`.
3. Add the row to `BUNDLED_FONTS` in `engine/fonts.js` and the stack to `FONTS`
   in `engine/effects/util.js`.
4. Add the `@font-face` and the `.chip.font-<key>` rule to `remix.css`, and the
   chip to `FONTS` in `ui/panels.js`.
5. `node --test "remix/test/*.test.mjs"` checks the file, the licence and the
   total size.

Only OFL faces, and only ones that ship with the licence text.
