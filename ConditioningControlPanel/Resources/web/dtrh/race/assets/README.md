# race/assets - EMI's game model

`emi.glb` is the hard-surface EMI from the Blender build (`tools/blender/emi/build_hs.py`),
cut down to a game LOD and exported with named pivots, a five-frame face atlas and five
character-select clips. `emi-faces.png` is the atlas the glass material samples. Nothing in
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
| textures | packed; one image, `emi-faces` 760x137 |

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

`emi-faces.png`, 760x137 RGB, five 152x137 frames left to right in the owner-locked order:

| frame | glyph | offset.x |
|---|---|---|
| 0 | `^_^` | 0.0 |
| 1 | `:3` | 0.2 |
| 2 | `>_<` | 0.4 |
| 3 | `o_o` | 0.6 |
| 4 | `$_$` | 0.8 |

Pink (255,105,180) glyphs at one shared size (74 px Noto Sans Mono, 5 px stroke) over the
dim screen purple (62,46,92), so a frame swap never rescales the face and the background is
the idle screen glow. The glass UVs cover frame 0 only, U in [0.0007, 0.1993], V full:

```js
const glass = root.getObjectByName('EMI_glass');
const map = glass.material.emissiveMap;      // the atlas, sampler NEAREST + CLAMP
map.offset.x = frame / 5;                    // frame 0..4
```

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
| `wave` | 2.0 s | no | shoulderR flies up with overshoot, four waggles, comes down; antenna perks, bead swells |
| `hop` | 1.6 s | no | anticipation squash, launch stretch, apex, landing squash with overshoot; antenna whips, arms flare, toes point |
| `peek` | 2.0 s | no | root tilts toward the camera with a curious roll, holds a beat, antenna leans in after, settles |
| `drum` | 2.4 s | no | both hands tap an invisible rim in front of her in alternation, she rocks toward each hit, antenna nods |

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
Blender 5.2.1 portable at `C:\Tools\blender\`, Python with Pillow + fontTools for the atlas.
Launch the .cmd detached (Blender hangs a foreground shell) and poll the log for `ALLDONE`.
`verify_glb.py` prints the node list, animation lengths, triangle count, bounding box and the
glass UV range, then renders the turntable and one still per clip into `out/verify/`.
