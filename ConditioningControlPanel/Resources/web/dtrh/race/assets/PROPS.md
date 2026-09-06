# props.glb - The Caucus Race prop pack

Every prop, the kart cup and saucer, the item box and the road furniture, built in Blender in the
same hard-surface look as the EMI mascot: flat Principled colours (no image textures), one-segment
bevels, an inverted-hull outline. One file, named top-level nodes, ready for the loader to bake
material colour into vertex colour and merge each prop to one geometry.

Source of truth for the geometry: `ConditioningControlPanel/tools/blender/props/` (mirror of
`C:\Tools\emi3d\props\`). Nothing in this file is hand-modelled; regenerate, do not edit the glb.

## Contract

- Units: metres. Y-up, +Z forward (the prop front, the face seen from the road, points +Z).
- Origin: base centre on the ground for every node. Wall props stand on the wall plane at
  y = 0 and protrude toward +Z (into the tube); their opening centres are listed below.
- Shoulder props are centred on x = 0. The step block in `roomProps.js` centres its prop area at
  x = 0.3, so the loader translates shoulder props by +0.3 in x.
- Each node is ONE mesh with one material slot per flat colour, plus one child mesh named
  `<node>_outline` whose only material is `outline` (0x1b1a33). The outline is an inverted hull
  (vertices pushed along their normals, faces flipped): draw it back-face only, unlit.
- Materials are shared across nodes by name. `boost_strip` is the emissive teal rib material on
  the boost pad (emissive in the glb too). Coplanar decals (letters, pips, labels) carry no outline.
- No animation data, no UVs, no textures, no lights, no cameras.

## Nodes (triangles include the outline hull; bbox is glTF Y-up, x / y / z extents)

| node | tris | budget | bbox (x, y, z) | notes |
|---|---:|---:|---|---|
| `kart_cup` | 1720 | 3000 | 1.35 x 0.69 x 1.11 | JS lathe profile; rim = pink glaze band in the wall; handle roots start inside the wall with a boss each. Sit it 0.09 above the saucer like the JS rig. Handle on +x. |
| `kart_saucer` | 488 | 600 | 1.90 x 0.12 x 1.90 | pink glaze rim band + mark at +x |
| `item_cube` | 448 | 600 | 1.25 x 1.25 x 1.25 | 1.2 cube, gold "?" raised 0.025 on all six faces |
| `item_shard_00..11` | 24 each | 40 | 0.52 or 0.68 x 0.34/0.40/0.46 x 0.72/0.48 | 2 x 2 x 3 uneven split; each node's `translation` is its centroid in cube space, so the twelve reassemble the cube exactly (checked: -0.6..0.6, 0..1.2, -0.6..0.6). 01, 06, 10 are gold. |
| `boost_pad` | 280 | 600 | 5.75 x 0.11 x 3.00 | dark plate, edge strips, three chevron rib pairs pointing +z, ribs and strips in `boost_strip` |
| `ramp_lip` | 160 | 600 | 7.10 x 0.31 x 0.34 | cream kerb block + pink roll |
| `air_marker` | 88 | 600 | 0.35 x 0.35 x 0.35 | gold sugar lump |
| `gantry` | 1684 | 2000 | 8.42 x 5.73 x 0.90 | pillars at x = +-3.75, beam z 4.15..4.65, blank blush sign plate under the beam (text is the loader's), bunting on the +z face, teapot on top |
| `podium` | 664 | 2000 | 2.20 x 0.35 x 2.20 | saucer podium, pink glaze rim, dish floor at 0.31 |
| `floor_tile` | 352 | 600 | 2.00 x 0.08 x 2.00 | 2 x 2 checker, cream / blush |
| `teagarden_wall` | 576 | 600 | 1.40 x 0.65 x 0.50 | shelf, two cups with tea tops (steam at +-0.42, 0.5), teapot |
| `teagarden_shoulder` | 376 | 600 | 0.50 x 0.67 x 0.69 | topiary teapot in a planter, blush blossoms |
| `toybox_wall` | 466 | 600 | 1.30 x 0.68 x 0.50 | shelf with E M I blocks and a blush block on top |
| `toybox_shoulder` | 290 | 600 | 0.44 x 1.06 x 0.45 | leaning E M I tower (bounce = whole instance) |
| `toybox_extra` | 544 | 600 | 0.50 x 1.26 x 0.52 | jack-in-box, open lid, coil, EMI ball on top (bob pivot at x = 0.3 in step space) |
| `casino_shoulder` | 464 | 600 | 0.60 x 0.39 x 0.74 | three banded chip stacks, bottoms open (they sit on the step) |
| `casino_extra` | 298 | 600 | 0.38 x 0.38 x 0.38 | die with ink pips; tumble pivot = (0, 0.18, 0) |
| `casino_sign` | 544 | 600 | 1.80 x 1.02 x 0.17 | frame only; opening 1.6 x 0.8 centred at y = 0.5, quad at z = 0 |
| `undertow_shoulder` | 352 | 600 | 0.77 x 1.78 x 0.49 | kelp cluster on a rock (sway = whole instance) |
| `undertow_porthole` | 576 | 600 | 1.56 x 1.56 x 0.22 | steel ring with eight bolts; round opening 1.2 across, centred at y = 0.78, quad at z = 0 |
| `mirrors_wall` | 576 | 600 | 1.22 x 1.96 x 0.16 | gold frame, silver face, crest; the silver face is the plain mirror |
| `mirrors_shoulder` | 208 | 600 | 0.72 x 0.72 x 0.52 | five shards in rubble |
| `chapel_shoulder` | 460 | 600 | 0.50 x 0.90 x 0.71 | three candles with drips and flame caps (`flame`, emissive) on a magenta cloth |
| `chapel_frame` | 448 | 600 | 1.49 x 2.60 x 0.10 | bone frame with a gable and gold cross; opening 1.2 x 1.6 centred at y = 0.9, quad at z = 0 |
| `greyward_shoulder` | 504 | 600 | 0.57 x 0.63 x 0.90 | gurney with rails, pillow at -z |
| `greyward_iv` | 250 | 600 | 0.55 x 1.53 x 0.50 | IV stand, bag on +x with a blush label |
| `greyward_fluoro` | 352 | 600 | 1.80 x 0.60 x 0.12 | steel fixture rim; opening 1.6 x 0.4 centred at y = 0.3, quad at z = 0 |
| `coronation_wall` | 474 | 600 | 1.36 x 2.20 x 0.14 | swallow-tail banner on a gold bar, three tassels; bar at y = 2.2 |
| `coronation_shoulder` | 320 | 600 | 0.60 x 2.24 x 0.60 | fluted pillar, gold cap; top at 2.24 (crown sits on it) |
| `coronation_extra` | 496 | 600 | 0.52 x 0.40 x 0.49 | crown: gold band, six points, magenta jewels and cushion, cross |

Total 14,890 triangles over 41 nodes, 29 materials, 596,512 bytes.

## Regenerate

```
C:\Tools\emi3d\props\run_props.cmd            (RENDER=0: build + export + audit, ~20 s)
set RENDER=1 & C:\Tools\emi3d\props\run_props.cmd   (+ pixel renders + contact sheet, ~4 min)
```

Needs Blender 5.2.1 portable at `C:\Tools\blender\blender-5.2.1-windows-x64\blender.exe`
(override with `BLENDER=`) and a system python with Pillow for the sheet. The runner exports
straight to `Resources/web/dtrh/race/assets/props.glb` (override with `GLB=`), audits the file in
a fresh Blender process (`check_props.py`: node list, tris, bbox, shard reassembly, missing names)
and writes `props.log` ending in ALLDONE. Renders land in `%USERPROFILE%\Pictures\Screenshots\emi-3d\props\`.

Files: `propkit.py` (scene, palette, bmesh primitives, node assembly, export, the EMI render
recipe), `build_props.py` (the prop table: one function per node, all parametric), `check_props.py`,
`sheet.py`, `run_props.cmd`. Blender and Pillow hang a foreground shell: run the .cmd detached and
poll the log.
