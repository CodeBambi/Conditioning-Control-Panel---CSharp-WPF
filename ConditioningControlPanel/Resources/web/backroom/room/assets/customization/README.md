# Room Service collection

Local preview models for fixed room decoration spots. No item prices, ownership, purchases or persistence are implemented by this collection.

Authoring sources live in the Blender workspace: customization-vending/build.py, customization-displays/build.py, customization-plants/build.py and customization-chess/build.py. Run with Blender 5.2 --background --factory-startup --python <script>. Each directory includes a final_report.md and fresh-import render evidence. Runtime copies use the filenames in customization.js.

- vending.glb: 25,564 triangles, authored height 2.3 m; room scale .92. Front +Z. Nine bay anchors, screen selector, hatch hinge and dispense anchor.
- gallery-landscape, portrait-pair, deco-billboard: 3,574 / 6,020 / 5,266 triangles. Front +Z, centered origins. screen_surface planes have full UV0 and 16:9 / 3:4 / 21:9 aspects.
- monstera, hanging_ivy, terrarium: 7,980 / 26,192 / 11,404 triangles, heights 1.25 / .60 / .45 m. Base origins, Y up, foliage_sway pivots. Ivy hook is .60 m above its origin.
- knight, queen, rook: 18,458 / 19,922 / 19,804 triangles, heights 1.28 / 1.44 / 1.25 m. Original Piece by Piece sculpture meshes on new brass/violet pedestals; generated copies clean duplicate vertices. Front +Z, base origins.

Gallery screens use the existing room media feed and GIF budget. Plant sway freezes under Still/Calm/reduced motion. Vending miniatures are cosmetic previews, not inventory or ownership records.
