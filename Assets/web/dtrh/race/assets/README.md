# race/assets - EMI's game model

`emi.glb` is the hard-surface EMI from the Blender build (`tools/blender/emi/build_hs.py`),
cut down to a game LOD and exported with named pivots, a five-frame face atlas and five
character-select clips. `emi-faces.png` is the atlas the glass material samples, and it is
the LIVE one: it now carries seven frames and `race/gltf.js` loadPack re-points the glass at
it, so a new face is a rerun of `face2.py --game` and never a rebake of the glb. Nothing in
here is hand-edited: regenerate both with `tools/blender/emi/run_game.cmd`.

## Numbers

| | |
|---|---|
| `emi.glb` | 190,580 bytes (mesh 110 KB, animation 19 KB, atlas 9.5 KB, JSON 64 KB) |
| triangles after modifiers | 4,904 (the inverted outline hull is included) |
| vertices | 3,316 |
| units | metres, 1 unit = 1 m |
| height | sole to case top 1.000 m, 1.25 m to the top of the bead |
| footprint | x -0.51 .. 0.51, z(glTF) -0.59 .. 0.14 (she stands a little behind the origin) |
| origin | sole centre between the feet, y = 0 at the floor |
| facing | +Z in glTF (Blender -Y forward, exported Y up) |
| textures | packed; one image, `emi-faces` 760x137 (the five-frame fallback; the shipped png is 1064x137) |

To match the size of the old primitive rig (a 0.525 m CRT case inside the cup), scale the
root by about 0.64; the new case is 0.82 m tall at scale 1.

## Node contract (names are load-bearing, do not rename)

```
EMI_root                     root pivot: breath, hops, tilts live here
  EMI_case                   the CRT case mesh (materials emi_case + outline)
  EMI_glass                  the screen dome (material emi_glass), UVs on atlas frame 0
  bezel, recess, inlet, vent0..vent5
  button                     gold cross button pivot (btn_h, btn_v)
  ant0 > ant1 > ant2         antenna joints, each with antseg<i> + antjoint<i>
    ballpiv                  bead pivot (ball)
  shoulderL, shoulderR       arm pivots: shoulder_cap, arm, hand, thumb ride them
  footL, footR               foot pivots: leg, shoe, sole, laces, bow, knot, cuff ride them
```

`shoulderL` sits at -X, which is her right hand (the viewer's left when she faces you).
The bevelled bezel is already cut; the boolean cutter never ships. No lights, camera or floor.

Every mesh with an outline carries two primitives: its surface material and the inverted
hull with the material named exactly `outline` (single-sided, near-black). Give it a plain
black `MeshBasicMaterial` with `side: THREE.FrontSide` if the standard one looks grey.

## Face atlas

`emi-faces.png`, 1064x137 RGB, seven 152x137 frames left to right in the owner-locked order.
A frame is only ever APPENDED: the index is the contract `race/gltf.js` FACES, `menu.js`
ROSTER `faces` and `intro.js` FACE all quote.

| frame | glyph | offset.x | drawn how |
|---|---|---|---|
| 0 | `^_^` | 0/7 | text |
| 1 | `:3` | 1/7 | text in a cell turned on its side, then rotated 90 degrees clockwise, so the colon reads as two eyes side by side and the 3 as the cat mouth under them |
| 2 | `>_<` | 2/7 | text |
| 3 | `o_o` | 3/7 | text |
| 4 | `$_$` | 4/7 | text |
| 5 | star eyes | 5/7 | text, the star from the Segoe UI Symbol fallback |
| 6 | spiral eyes | 6/7 | stroked, not typed: the `@` counters close up solid under the 5 px stroke |

Pink (255,105,180) glyphs at one shared size (74 px Noto Sans Mono, 5 px stroke) over the
dim screen purple (62,46,92), so a frame swap never rescales the face and the background is
the idle screen glow. That shared size is measured on the FIRST FIVE faces only
(`face2.py` `SIZED_ON`), so appending a face cannot rescale the ones already on the glass.

The glass UVs cover one frame of the FIVE-frame strip baked into the glb, U in
[0.0007, 0.1993], V full. `loadPack` swaps that strip for this png and takes up the slack
with a repeat, so the offset stays one frame index:

```js
const glass = root.getObjectByName('EMI_glass');
const map = glass.material.emissiveMap;      // the atlas, sampler NEAREST + CLAMP
map.repeat.x = 5 / 7;                        // gltf.js BAKED_FRAMES / FACES.length
map.offset.x = frame / 7;                    // frame 0..6, gltf.js setFace does both
```

**The strip is repainted into a padded texture on load.** `gltf.js` `padAtlasToPot` draws this
png into a power of two 2048x256 canvas (top left, or bottom left when flipY is on, so the strip
always lands at the uv origin) and hands the material a CanvasTexture instead. Two reasons, both
of them phones: an `<img>` can have its decoded pixels dropped by mobile Safari between uploads,
and the glass has no base map, so one blank emissive sample paints the whole screen white; and
1064x137 is not a power of two, which puts RepeatWrapping and mipmaps out of bounds on any
WebGL1 context. So repeat and offset are both multiplied by the pad scale (1064/2048, 137/256),
which `gltf.js` `atlasScale(texture)` reads off `userData` and `setFace` applies: the numbers in
the table above are still the frame indices, they just get squeezed. Sampling never leaves the
strip, so the padded texture is ClampToEdge on both axes, and `setFace` never flags
`needsUpdate` (an offset is a uniform, not pixels, and a version bump re-uploads the atlas).

If the png never arrives, the embedded five-frame strip stays, gets padded the same way, and
frames 5 and 6 clamp to 4: `gltf.js` `faceFrames(texture)` reads the width off the texture,
never off `FACES`.

The material ships with `emissiveMap` = atlas, `emissive` = white, `emissiveIntensity` = 1
(the race renders with no tone mapping, so the pink is exact at 1.0; the AgX studio render
runs it at 2.6) and a near-black base colour, so the screen is self-lit whatever the lights
do. The approved scanlines are a Blender node effect and do not survive glTF; add them in
the shader if wanted. Keep `generateMipmaps` off and the filters nearest (pixel.js does this
while a block is on), otherwise frames bleed into each other at distance.

## Clips

Five glTF animations, 30 fps authoring, keys only on `EMI_root` and the pivots, CUBICSPLINE
(Bezier in Blender), never per-frame sampled. Every clip keys all nine animated nodes at rest
on its first and last frame, so a crossfade never inherits a stray arm from the clip before.

| name | length | loop | what moves |
|---|---|---|---|
| `idle` | 4.0 s | yes, seamless | one breath on root scale/position, lazy weight shift, antenna sway a beat behind, bead pulse |
| `wave` | 2.0 s | no | shoulderR swings out and up about the forward axis, a little back, thumb to the camera, the arm stretching 2x and the shoulder shrugging 9 cm up the case side so the hand rises above the case top; four waggles above and beside the top corner; comes down. Antenna perks, bead swells |
| `hop` | 1.6 s | no | anticipation squash, launch stretch, apex, landing squash with overshoot; antenna whips, arms flare, toes point |
| `peek` | 2.0 s | no | root tilts 16 degrees toward the camera with a curious roll and leans in 5 cm, holds a beat, antenna leans in after, settles |
| `drum` | 2.4 s | no | both hands, held out clear of the case sides, tap an invisible rim in front of her in alternation; she rocks toward each hit, antenna nods |

Every clip is checked from the character-select camera, glTF (0, 0.9, 3.2) looking at
(0, 0.55, 0): `verify_glb.py --cam menu` (the default) renders the stills from it. The
shoulder pivot sits at 0.49 m and the arm reaches 0.29 m, so a rotation alone tops out at
0.78 m; the wave gets the hand above the 1.0 m case top with a 2x cartoon stretch along the
arm and a 9 cm shrug of the shoulder (`WAVE_STRETCH`, `WAVE_LIFT` in `export_glb.py`).

No face frame is baked into any clip; the runtime picks one (the character-select menu, the
race moods). The old `MOODS` table in `race/emi.js` drove antenna pivots directly; the same
pivots exist here (`ant0` = stem pitch, `ant1`/`ant2` = the kink) if that path stays.

## Regenerating

```
C:\Tools\emi3d\hs\run_game.cmd          full: atlas, game LOD, clips, glb, re-import check, renders
C:\Tools\emi3d\hs\run_game.cmd quick    stop after the re-import contract check
```

The mirror of those scripts lives in `tools/blender/emi/` (`build_hs.py --game 1 --mode game`,
`face2.py --game DIR`, `export_glb.py`, `verify_glb.py`, `pixup.py`, the `.cmd` runners).
The atlas alone is `python tools/blender/emi/face2.py --game <dir>`, which needs only Pillow
and fontTools; copy its `emi-faces.png` over the one in here. Frames 0 and 2..4 come out
byte identical to the shipped strip, which is the check that nothing rescaled.
Blender 5.2.1 portable at `C:\Tools\blender\`, Python with Pillow + fontTools for the atlas.
Launch the .cmd detached (Blender hangs a foreground shell) and poll the log for `ALLDONE`.
`verify_glb.py` prints the node list, animation lengths, triangle count, bounding box and the
glass UV range, then renders the turntable (studio angle) and one still per clip (menu camera) into
`out/verify/`.
