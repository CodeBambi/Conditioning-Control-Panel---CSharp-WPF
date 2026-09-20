# Back Room memorabilia provenance

Existing project art and authentic screenshots. No generated or reconstructed app screens.
Converted to JPEG quality 85, maximum edge 1600 pixels, aspect ratio preserved. Original sources unchanged.

## Posters and archive photographs

| File | Original source | Depicted content |
|---|---|---|
| eyes-front.jpg | ConditioningControlPanel/Resources/web/arcademy/art/posters/poster_eyes_front.png | Existing Arcademy poster |
| good-work.jpg | ConditioningControlPanel/Resources/web/arcademy/art/posters/poster_good_work.png | Existing Arcademy poster |
| stay-late.jpg | ConditioningControlPanel/Resources/web/arcademy/art/posters/poster_stay_late.png | Existing Arcademy poster |
| attend.jpg | .../arcademy/art/posters/poster_attend.png | Existing Arcademy poster, JPEG copy at original resolution |
| listen-well.jpg | .../arcademy/art/posters/poster_listen_well.png | Existing Arcademy poster, JPEG copy at original resolution |
| dive-deep.jpg | .../arcademy/art/posters/poster_dive_deep.png | Existing Arcademy poster, currently taken down (see memorabilia.js) |
| ccp-601.jpg | cclabs-site/images/screenshots/getting-started/main-window-tour.png | CCP v6.0.1 dashboard, version visually verified |
| ccp-dashboard.jpg | preview.jpg in the client repository, copied from C:/wt-batch0917/preview.jpg | CCP v6.9.1 dashboard, version visually verified |
| ccp-feature.jpg | cclabs-site/images/screenshots/skill-tree/tree-overview.png | Archived CCP skill tree, v6.0.1-era |

The two website screenshots were read from C:/Projects/cclabs-site. These are archive photos; the v6.9.1 screenshot is
newer than v6.0.1 but is not represented as the current release. The three CCP screenshots no longer hang anywhere:
they were the provisional fill for the eight polaroid slots, which are now the vault wall below. They stay on disk
because they are the only authentic archive dashboards available to the room.

## The vault wall (the eight polaroids)

All eight polaroid slots now advertise cards from the premium page, each wearing that card's OWN shelf art,
straight off Resources/features/. Nothing here was drawn, generated, upscaled or reconstructed: each file is a
JPEG quality 85 copy of the shipped PNG at its original resolution, flattened without loss because every one of
those sources is fully opaque. The slot-to-card mapping and the wording live in room/memorabilia-wall.js.

| File | Original source | Vault card | Source size |
|---|---|---|---|
| feature-fyp.jpg | Resources/features/fyp.png | For You (tier 1) | 1376x768 |
| feature-remote-control.jpg | Resources/features/remote_control.png | Remote Control (tier 1) | 1376x768 |
| feature-takeover.jpg | Resources/features/takeover.png | Takeover (tier 1) | 882x973 portrait |
| feature-graded-intake.jpg | Resources/features/lab_quiz_hero.png | Graded Intake (tier 0, weekly pass) | 1376x768 |
| feature-justdrop.jpg | Resources/features/justdrop.png | Just Drop (tier 2) | 1376x768 |
| feature-haptics.jpg | Resources/features/vibe.png | Haptics (tier 1) | 910x884 |
| feature-blink-trainer.jpg | Resources/features/blink_trainer.png | Blink Trainer (tier 1) | 1376x768 |
| feature-awareness.jpg | Resources/features/awareness.png | Awareness (tier 1) | 1376x768 |

The two tier signs stay PNG, because they are stamped over a photograph and need their alpha:

| File | Original source | Depicted content |
|---|---|---|
| tier-badge-basic.png | Resources/features/tier_badge_t1.png | The shipped BASIC SUBJECT neon sign, 900x440, unaltered |
| tier-badge-prime.png | Resources/features/tier_badge_t2.png | The shipped PRIME SUBJECT neon sign, 900x384, unaltered |

Two shapes, one rule: the polaroid's paper follows the picture, and the picture area is 732 pixels wide, so art is
only ever hung if it is at least that wide. That is why Takeover (882 wide, portrait) and Haptics (910 wide, nearly
square) compose perfectly well as taller polaroids, and why the other two art-poor vault cards are absent:
She's Listening only has 512x512 art and Lockdown only has an icon, and either one would have to be blown up.
Neither was faked to fill a slot. The Back Room itself is deliberately not on the wall, and neither are Arcademy,
DtRH or Focus Gaze, which the room already runs on its own ad frame.

Final picture selection still belongs to the owner: swapping one card is one edit to PHOTO_GROUPS, and the eight
JPEGs above are the only files that would go with it.
