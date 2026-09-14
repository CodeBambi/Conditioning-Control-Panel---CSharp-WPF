# Approved prize models

The counter asset contains the eight owner-approved prize props, replacing its placeholder shelf objects. Bundle 3 carries 00, 07, 08, 09 and 10 exactly once. Cabinet geometry and EMI are retained from the approved counter.

Rebuild only this asset with:

```powershell
node ConditioningControlPanel/Scripts/build-backroom-room-assets.mjs --only counter.glb
```

Source relative to `--source`: `counter/prize-build/out/counter-prizes.glb`.
Source SHA-256 prefix: `635e2656a6d8`. The source workspace holds the reproducible Blender scripts, individual props and validation renders.

The optimizer preserves these populated groups and their `prize_id` extras:

- `shelf_jackpot_remix`
- `shelf_rt_demo`
- `shelf_high_roller`
- `shelf_flashes_v2`
- `shelf_bubbles_v2`
- `shelf_rt_bundle_1`
- `shelf_rt_bundle_2`
- `shelf_rt_bundle_3`

These are visual asset identifiers. They are not a purchase API or an entitlement contract. The station stays `soon` until the prize system is implemented.

The optimized counter has 118,962 triangles and 82 mesh primitives, down from 420 source primitives. Its GLB is about 1.65 MiB. Transparent materials can require extra render passes in the room; primitive count is not total scene draw calls.

Validation: the room smoke check verifies named runtime nodes, eight unique prize ids and populated prize groups. The full browser check covers room navigation, station lifecycle, reduced motion, bell UI and page errors. Owner WPF desk verification remains separate.


The room renders one decorated marquee in front of the counter. The build removes
`header_title` and `service_title` from the counter, and `house_title`,
`house_sign_frame` and `house_sign_face` from the shell to remove overlapping signs.
Rebuild both `counter.glb` and `shell.glb` after changing this policy. Original
Blender sources stay unchanged.

Room motion uses smooth traveling bulb trails and four independent EMI idle
controllers. NPCs blink, wobble, look around, wave and perform station-specific
gestures. The optimizer preserves authored shoulder and antenna groups; neutral
wrapper hinges animate these groups without altering quantized mesh transforms.
The counter has a cosmetic dusting routine. Prize purchase and handover wiring
remain unimplemented. Motion off, Calm and reduced motion settle NPCs
and freeze the ambient clock. Entering a game pauses the room entirely.
