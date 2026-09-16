# Room Service collection

Local preview models for fixed room decoration spots. No item prices, ownership, purchases or persistence are implemented by this collection.

Authoring sources live in the Blender workspace: customization-vending/build.py, customization-displays/build.py, customization-plants/build.py and customization-chess/build.py. Run with Blender 5.2 --background --factory-startup --python <script>. Each directory includes a final_report.md and fresh-import render evidence. Runtime copies use the filenames in customization.js and customization-props.js.

- vending.glb: 25,564 triangles, authored height 2.3 m; room scale .92. Front +Z. Nine bay anchors, screen selector, hatch hinge and dispense anchor.
- gallery-landscape, portrait-pair, deco-billboard: 3,574 / 6,020 / 5,266 authored triangles. Front +Z, centered origins. screen_surface planes have full UV0 and 16:9 / 3:4 / 21:9 aspects.
- monstera, hanging_ivy, terrarium: 7,980 / 26,192 / 11,404 authored triangles, heights 1.21 / .60 / .43 m. Base origins, Y up. The ivy's hanging eye is at the top of its own box.
- knight, queen, rook: 18,458 / 19,922 / 19,804 triangles, heights 1.28 / 1.44 / 1.25 m. Original Piece by Piece sculpture meshes on new brass/violet pedestals; generated copies clean duplicate vertices. Front +Z, base origins.

Gallery screens use the existing room media feed and GIF budget. Vending miniatures are cosmetic previews, not inventory or ownership records.

## Where each prop stands

Every prop in the collection has one fixed spot in the room. Metres, y up, the room centred on the origin, the frame walk.js uses. Yaw turns the model's authored +Z front into the room. Nothing here persists: a switch lasts as long as the visit.

| Prop | Spot | Position (x, y, z) | Yaw | Switched from |
| --- | --- | --- | ---: | --- |
| vending | Right wall by the entrance, the ROOM SERVICE cabinet itself | 7.05, .02, 6.40 | -pi/2 | Fixed (walk up and press E, or click it) |
| knight | Northwest pedestal, west of the Prize Parlour | -4.10, .03, -6.85 | 0 | Panel: Knight sculpture, Pedestal 1 |
| queen | Northeast pedestal, east of the Prize Parlour | 4.10, .03, -6.85 | 0 | Panel: Queen sculpture, Pedestal 2 |
| rook | Entrance pedestal, right of the door | 3.70, .03, 7.25 | pi | Panel: Rook sculpture, Pedestal 3 |
| monstera | Entrance wall, left of the runner | -2.35, .02, 7.18 | 0 | Panel: Monstera, On/Off |
| hanging_ivy | Northwest corner, hung from the ceiling on a brass rod | -5.90, 2.95, -6.55 | .5 | Panel: Hanging ivy, On/Off |
| terrarium | West flank of the Prize Parlour, on a brass plinth | -3.75, .42, -4.90 | -.6 | Panel: Terrarium, On/Off |
| gallery-landscape | Left wall, above the Candy Rose and Candy Violet booths | -6.86, 2.72, 4.10 | pi/2 | Panel: Gallery frame, On/Off |
| portrait-pair | Left wall, above the Candy Violet and Candy Mint booths | -6.86, 2.72, 5.80 | pi/2 | Panel: Portrait pair, On/Off |
| deco-billboard | Entrance wall, over the door, facing the room | 0, 3.10, 7.72 | pi | Panel: Wide billboard, On/Off |

The cabinet and the three pedestals were already placed by the catalogue itself (customization.js); the six rows below them are customization-props.js.

Notes on the spots:

- The two floor plants are bodies: walk.js carries a blocker for the monstera and for the terrarium's plinth. Every other prop is on a wall, over head height, or inside the cabinet blocker that was already there, so no other walk volume changed.
- No station, cabinet or screen moved. The approved left-wall booth positions (8c848c368) are untouched, and smoke/room-check.mjs asserts that no placed prop reaches into a station's fixture bounds and that every station approach is still clear and reachable from the door.
- The wide billboard is over the entrance door, not on the back wall: the Prize Parlour's own marquee stands across that whole span, so a frame behind it cannot be seen from the room.
- The right wall carries no frame. The cards alcove, the roulette hub, media_screen_1 and the ROOM SERVICE cabinet already fill it, and the wall flares outward past z 0, so a flat frame does not sit flush there.
- The gallery frame and the portrait pair feed from the room's own media feed, like the room's four wall pictures and the extra screen packs.
- Two small pieces of geometry are built in code rather than authored: the brass rod the ivy hangs from and the brass plinth the terrarium stands on.

Each prop is baked to one mesh per material when it is placed, so the ivy's 85 authored mesh nodes cost six draw calls instead of eighty-five. The six decoration props together are 30 draw calls and 60,428 triangles. The authored nodes the bake empties are pruned from the graph with it (32 of them across the six), so nothing dead is walked every frame. Because the batches are baked, the authored foliage_sway pivots are not animated: these are dressing, not performers.
