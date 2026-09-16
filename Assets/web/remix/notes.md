# Remix engine, vendored

Copied from `cclabs-site/remix` (the Remix Room on cclabs.app) on 2026-09-14 for the
Jackpot Remix flash: one flash in a hundred is a composite of the user's own GIFs, built
by the same `autoCompose` roll the room runs. The UI (`ui/`, the css, `index.html`), the
docs and the sample gifs were left behind; only what the export path needs is here.

    engine/        the whole remixer minus the interface (see engine/README.md)
    vendor/        the two third-party files the engine imports
    assets/fonts/  the four caption faces, latin subset, with their licences
    test/          the engine's node tests that do not import the site's ui/ layer
    jackpot.html   the headless page the desktop host drives (not part of the site)
    jackpot.js

Run the tests from this folder: `node --test "test/*.test.mjs"` (224 tests; the 32 in
`caption-fonts`, `doors`, `sound` and `copy` stayed with the site because they read `ui/`).
The csproj keeps `test/` out of the installer (`RemixTestExclude`, both places).

## The one patch

`engine/decode.js` and `engine/decode.worker.js` imported omggif from the site's shared
`assets/vendor/omggif/` two folders up. Here it sits in `vendor/`, so both imports read
`'../vendor/omggif.module.js'`. Nothing else in `engine/` was changed; a future re-vendor
is a copy plus that one sed.

## Licences

| File | What | Licence |
|---|---|---|
| `vendor/gifenc.esm.js` | gifenc 1.0.3 by Matt DesLauriers (mattdesl), the GIF encoder + quantizer | MIT (github.com/mattdesl/gifenc) |
| `vendor/omggif.module.js` | omggif by Dean McNamee, the GIF reader (LZW decode); ESM wrapper by the site | MIT, notice at the top of the file |
| `assets/fonts/anton-latin.woff2` | Anton, latin subset from Google Fonts | SIL OFL 1.1, `anton-latin.OFL.txt` |
| `assets/fonts/fredoka-latin.woff2` | Fredoka, latin subset | SIL OFL 1.1, `fredoka-latin.OFL.txt` |
| `assets/fonts/pacifico-latin.woff2` | Pacifico, latin subset | SIL OFL 1.1, `pacifico-latin.OFL.txt` |
| `assets/fonts/pressstart2p-latin.woff2` | Press Start 2P, latin subset | SIL OFL 1.1, `pressstart2p-latin.OFL.txt` |
| `engine/**` | the engine itself | CC Labs, same as the site |

gifenc's minified bundle carries no licence header of its own; the MIT text is in the
package's repository and applies to the copy here. The fonts only load when a caption
block is on the timeline, which the roll never adds, so a Jackpot build never touches
them; they ride along so the vendored engine stays whole.
