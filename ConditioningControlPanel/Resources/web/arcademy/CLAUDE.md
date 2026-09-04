# The Arcademy (web) — architecture + gotchas

The T2 mini-game hub. One WebView2 page served from `https://ccp.game/arcademy/index.html`
by `ArcademyHostService`; vanilla ES modules, **no build step, no framework, no bundler**.
Design law lives in `planning/arcademy/` — read `BUILD-CONTRACT.md` → `GROUND-RULES.md` →
`SYNTHESIS-NOTES.md` → `DECISIONS.md` (earlier wins) before changing behaviour here.

This file is the *implementation* companion: what the pieces are, where the traps are.

---

## 1. What owns what

| Concern | Owner | Never |
|---|---|---|
| the day's 4 classes | `core/timetable.js` (pure) | a game, a store, a clock |
| S/A/B/C + caps | `core/grades.js` (pure) | a game grading itself |
| grade tier (Year 1–4) | `games/registry.js` + meta store | a game choosing its tier |
| XP numbers | **C#** (`payout-result`) | any page-side XP table |
| "already paid today" | **C#** (`ArcademyMetaStore.XpPaidKey`) | the page deciding a retake is free |
| settings values | **C#** (`set-setting` → `setting` echo) | the page assuming its own clamp |
| screens, ctx, lifecycle | `shell/shell.js` | a game calling `bridge` directly |
| effects | `engine/` (parallel agent) | a game exceeding `effectsConsumed` |
| media | `provider/` (parallel agent) | a game fetching anything itself |
| sound | `shell/audio.js` | the engine, a game, or C# owning an Audio node |

Screen router: **split-flap board → class → report card** (+ settings and the Records
Office, which are screens).
`boot.js` owns the bridge handshake and the Esc ladder's outer rungs; `shell.js` owns the
inner rungs.

**Two ladders, and they are different.** The ESC ladder is the page's (tap walks
settings → pause → leave class, then unfullscreen; hold exits). The PANIC ladder is the
HOST's: `MainWindow` hands the panic key to `ArcademyHostService.HandlePanicPress()` while
the window is up, press 1 posts `suspend {on:true, reason:'panic'}` and press 2 within 2s
calls `CloseActive()`. See trap 29 - without that hand-off two panic taps exited the whole
app from inside a class.

## 2. Files

```
index.html   one document; ids are the shell's only DOM contract (see boot.js dom{})
styles.css   shell chrome ONLY. Tokens ported from planning/arcademy/mockups/.
boot.js      handshake, heartbeat, boot deadline, Esc ladder, host-frame routing
bridge.js    postMessage seam: queue-until-init out, pre-buffer + multi-subscriber in
core/lexicon.js    t(key, fallback) over init.lexicon  (mod display strings ONLY)
core/timetable.js  §7 seeded generator (PURE)
core/grades.js     §8 rubric + A-caps (PURE)
core/vocab.js      HOUSE_WORDS (24, niche-agnostic) + slug() + dayVocabulary()
                   (PURE): the word FLOOR the shell deals when init.words is
                   empty. FALLBACK ONLY, never a merge - see trap 108
core/store.js      meta-command client, local cache + write-through
core/rng.js        makeRng/hash01            <- NOT ours (engine agent)
core/caps.js       clampToCaps + heat curve  <- NOT ours (engine agent)
provider/index.js  createAssets: claim() (every class) + claimTagged() (SORT) +
                   the DOOR seam catalog()/probeSub()/removeLibrarySub()/onLibrary()
provider/remote.js the bridge mailbox: assets-request / local-sample-request by
                   reqId, plus sendRaw/subscribe for probe-sub and library
provider/tagged.js THE TAGGED POOL: two piles, per-tag cursors, a seeded dry
                   re-serve, thin frozen at resolve / empty live (§3)
shell/shell.js     screen router + THE class runner (ctx per §11) + THE SETUP
                   DOOR hook (§5: create -> setup() -> beginPlay)
shell/splitflap.js departure-board reveal. The campus hangs it COLLAPSED behind a
                   plaque (campus.js's .campus-boardtab - clock pulses until the
                   first open of the day, store key boardOpenedDate); expanding
                   rolls the flaps, which is why there is no "flip the board
                   again" button any more
shell/reportcard.js day summary + THE one share pipeline
shell/settings.js  THE settings page (3 tiers) + SETTING_KEYS; `gameKey` scopes it to ONE
                   game group (the pause card's door) - argless = the full sheet
shell/ceremonies.js stamp / 10-segment meter / reward beats (engine-delegated; the
                   CSS floor REQUESTS its own cues on `document` since W0 - see trap 66)
shell/exits.js     THE WAY OUT: the campus pill + its confirm, the sticky exit
                   bar, and the casino arrow SIGN. Every back/leave/done in the
                   school is minted here (games borrow it through ctx.exits)
shell/punchcard.js THE CARD: cardFace() = a full-bleed face IMAGE with exactly
                   three live overlays - the ten stamps, the crest (the REWARD,
                   hidden until complete) and the text strip (count + a rotating
                   punchcard_phrase_1..8 line, or MASTERED + the date). Where the
                   three sit is DATA: loadFaceGeometry() reads art/punchcard/
                   faces.json once, optionally, and anything missing falls back
                   to DEFAULT_GEOM + [data-art="off"]'s drawn floor. Plus thud()
                   + holesLine(). Shared by the ceremony and the Records wall so
                   a card is ONE object
shell/enrollment.js the once-ever intro (ENROLL_LEX: 3 flavour cards per class)
                   AND the stamp ceremony (day one = three punches, an S day =
                   two, an ordinary day = one,
                   the tenth = the unlock beat)
shell/room.js      THE ROOM SCENE (VN antechamber): the painted set between the
                   campus door and a class, REPLACING the door card for rooms
                   listed in its SCENES table. ALL TEN ROOMS LIVE, so the door
                   card is now the exception: daily_trigger (Homeroom 101,
                   vn-04, chalkboard + painted corridor door),
                   deja_vu (Memory Lab 102, vn-05, the card racks),
                   impulse_control (Discipline Hall 103, vn-06, THE red button
                   under glass + the panelled door), lost_and_found (L&F 104,
                   vn-07, the central shelf bay - NOT the whole 850px wall, a
                   highlight that big reads as a bug), the_deep_end (The
                   Pool 105, vn-08, open lane water + the ladder), and the
                   Semester II + III five: sort (The Sorting Room 201, vn-12,
                   the card conveyor - the belt, not the three bins),
                   echo (Music Room 202, vn-13, the four lit drum heads on the
                   stage), instant_recall (Lecture Hall 203, vn-14, the blank
                   projection screen - NOT the lectern), anomaly (Darkroom 301,
                   vn-15, the drying lines framed on the ONE crooked print +
                   the black light-trap curtain as the painted exit) and
                   composure (The Studio 302, vn-16, the sliding-tile canvas on
                   the easel). Three of the ten have a painted exit (101, 103,
                   301); the other seven are doorless and that is legal.
                   TWO PLATES ARE COMPOSITED, never regenerated: vn-16's wall
                   sign was painted "COMPOSUE", so the sign rect was cloned out
                   and logo-composure-keyed.png pasted back in (the homeroom
                   "DAILeR" recipe), and vn-12's far-left window pane carried a
                   stray second neon fragment, cloned out from the next pane
                   along. campus.js
                   OFFERS every enterable door via handlers.roomScene(key,
                   {plate}) before popping the card; shell.js takes keys the
                   table has (walkThen first - door, THEN room), declines the
                   rest with false = the card pops unchanged. Dark rooms /
                   suspended school never offered, so the lockedClick EMI seam
                   stays the card's. A screen like records: SCREEN_DEPTH
                   room:1, one Esc rung (showBoard), torn down in clearScreen.
                   Fixed 1376x768 stage, hotspot rects in stage px (lab.js
                   promoted; transform-origin 50% 50%). Enter = begin (hotspot
                   button holds focus). Rects must keep their business ABOVE
                   y=APRON_STAGE_TOP (640) - the apron owns the floor.
                   THE FREE SWIM RULE: the door card is where a game that
                   declares `manifest.endless` shows its second, subordinate
                   Free Swim button, and the room REPLACES that card - so such
                   a game may join SCENES only WITH a `freeSwim` rect, or the
                   offer is silently lost. Only the_deep_end has one (the pool
                   ladder); shell.js hands down onFreeSwim + freeSwimLabel from
                   endlessFor(), and room.js paints both the rect and the
                   apron's side slab from it (no callback = neither renders,
                   whatever the row says). All four start-surfaces spend ONE
                   `entered` latch via spend(), so a room deals one class.
                   TRAP: the first fit() races the lazy rooms.css link - an
                   unstyled root measures as the 1080px .arc-screen column and
                   the room ships as a postcard; room.js refits on link load +
                   art load + one rAF, and .arm-root is position:fixed like
                   .arc-records for the same arc-report-on reason.
shell/rooms.css    room.js's skin, lazy-linked on first visit (corkboard
                   pattern). Pink breath on the one lit hotspot (.arm-main -
                   keyed on that class, NOT on :not(.arm-exit), because a pool
                   has a third rect); .arc-reduced / .arm-lite hold it still
                   (decoration law). The exit is quiet purple, the freeSwim
                   rect (.arm-swim) quiet cyan and never breathing - neither
                   may out-shout the verb that pays.
                   THE MIDWAY APRON (.arm-bar): the bottom action band, mounted
                   on <body> rather than inside .arm-root - root is its own
                   stacking context at z10, so nothing inside it can ever rise
                   above EMI (#arc-emi z50). As a body-level sibling at z55 the
                   band is the front edge of the stage: EMI wanders BEHIND it
                   and the slabs can never be squatted on (toasts z60 still
                   win). fit() anchors its top to the painting's floor line and
                   publishes --arm-band-h on BOTH root and bar (a body-level
                   sibling cannot inherit a var set on root alone); everything
                   in the band sizes off that one number. destroy() owns its
                   removal like root's.
shell/records.js   THE RECORDS OFFICE screen: the wall of ten cards, the per-card
                   stamp docket, and a link to the report card (never a second
                   share pipeline - trap 13). + THE SPOTLIGHT (owner playtest
                   2026-08-24): picking a wall card ALSO lifts that cardFace to
                   centre screen under a key light and plays one choreographed
                   entrance (badge glint -> per-class name idiom -> star wave ->
                   ken burns on the text strip; styles.css THE SPOTLIGHT block).
                   Presentation only - no data moves, the docket still paints.
                   Esc closes it FIRST via a rung in shell.js escapeStep
                   (recordsRoom.escapeStep() folds it before the chassis's own,
                   trap 48's shape); reduced motion (html.arc-reduced / the
                   media query) = one plain fade, no cues.
                   IT IS NO LONGER A SCREEN: shell.js opens the ROOM below, and
                   this file renders inside its card panel with
                   `render({embedded:true})` - no campus pill, and Back reads
                   "put the cards back" instead of walking out of the building.
                   The flag is OFF by default, so any other caller renders the
                   screen byte for byte
shell/scene.js     THE SCENE CHASSIS: a generalised point-and-click ROOM (the
                   1376x768 stage, the slides + the zoom out of the pressed
                   rect, flag PATCHES, mountInView PROPS, one overlay at a
                   time, the apron's back slab on <body>, the inward-out Esc
                   fold that answers FALSE at home). Dumb by construction - it
                   renders a table it was handed and calls back; it owns no
                   store, no bridge, no key and no lexicon row.
                   THREE AXES (portrait wave 0828): `phoneAxis()` answers
                   '' | 'landscape' | 'portrait' and scene.css carries the
                   SAME `[data-arc-orient]` qualifiers - box and origin must
                   travel together or the scale walks off-axis. Portrait
                   top-anchors the plate and the band becomes the painted
                   FLOOR, so `apronWanted()` steps the carpet off for an open
                   panel (an upright phone's panel IS a close-up) and the
                   panel is sized off `--asc-art-line` rather than
                   `--arm-band-h`; `publishBand()` writes 0 to <html> while it
                   is away. Laws 138 + 139. + scene.css
                   THE ALIVE LAYER (W2 0825): a DECLARATIVE `fx` table, the
                   same grammar as `hotspots` and `patches` and the same stage
                   pixels - `[{kind, view, rect:[x,y,w,h] | circle:{cx,cy,r},
                   when?, seed?, ...opts}]` - mounted as ONE `.asc-fx` per
                   view between the plate and the rects,
                   `pointer-events:none`, riding the scale. Seven kinds, all
                   generic: `neon` (halo breath + a seeded 120ms stutter every
                   9-20s), `lamp` (warm radial, .96-1 over 4s), `motes` (THE
                   one canvas: ~25 edge-masked particles drifting up), `window`
                   (halo pulse + a seeded headlight pass ~40s), `clock` (a
                   cream disc over the painted hands + DOM hands on the
                   player's REAL local time, `now` injectable, re-read every
                   20s with the seconds carried as a fraction), `seam` (a cold
                   edge + a floor pool, `when`-gated), `tilt` (2px mouse
                   parallax, moved on the PAINTING not the slide - the slide's
                   transform is the zoom's - desktop pointers only).
                   FOUR LAWS: `.arc-reduced` gets NO LAYER AT ALL (not a paused
                   one - the nodes are never created); `.arm-lite` keeps the
                   keyframes and loses the CANVAS; everything pauses on a
                   hidden tab / blurred window (`.asc-fx-hold` for the
                   keyframes, every timer and rAF CLEARED) and re-arms on
                   return; and EVERY TIMER IS OWNED - `fxStats().timers` is 0
                   after a camera move and after destroy(), which is the one
                   assertion that can catch an orphan interval in a room the
                   player walked out of. Test seams: `fxStats`, `fxCount`,
                   `fxHands`, `fxHold`. An unknown kind is LOGGED, never
                   thrown - a table is data
                   THE STEP-BACK PILL IS THE ROOT'S, NOT THE SLIDE'S (0825): one
                   `.asc-back` hangs off `.asc-root`, added and removed by view
                   the way the apron's class is written, so it is SCREEN pixels
                   on every host - a pill inside the stage rides the fit scale
                   and lands at half size on a phone (trap 101). It runs
                   `escapeStep()`, not `showView('wide')`, so a panel opened
                   over a close-up folds first.
                   A ZOOM IS A ZOOM (owner ruling 0825): the apron band belongs
                   to the WIDE shot and FADES OUT on showView(non-wide) (~200ms,
                   `.asc-bar-away`; .arc-reduced cuts; visibility rides the same
                   rule so the slab leaves the tab order), coming back on the
                   walk home. `.asc-back`, the step-back pill every close-up
                   draws, is the way out while it is away. So THE APRON-LINE
                   WARNING IS WIDE-ONLY: a close-up's rects and props are free
                   to run below y=640 and are never warned about. A consumer
                   must NOT clamp a close-up host to 640 - the paper is measured
                   from the painting or it is not measured at all.
                   `scale()` is the chassis's one measurement seam: the stage
                   scale as of the last `fit()`. A prop that has to reason in
                   SCREEN pixels asks the STAGE, never its own host - the
                   Records miniature carries a second scale of its own, so its
                   host would answer a different number than the close-up's and
                   the two walls would lay out differently
shell/recordsroom.js THE RECORDS OFFICE AS A ROOM (0825), the chassis's first
                   tenant. Four rects on vn-09: the TRAY (the one breathing
                   verb - it opens records.js in a scene panel, and wears the
                   two nudges), the CORKBOARD (a close-up of the cork with
                   corkboard.js's own mountNotices pinned over `corkInner`,
                   FULL measured rect - the band is away in a close-up -
                   `readable:true`, so every sheet carries an `.arc-cork-open`
                   door and one press lifts corkboard.js's READER: a full-size,
                   scrollable copy of that sheet on <body> at z56, above the
                   apron band and below the toasts, and the FIRST rung of this
                   room's Esc fold. It is the answer to a wall that is painted
                   at stage scale: on a phone the close-up's body copy lands
                   near 7px, so the type comes up and a drawn lens glyph says
                   the paper can be picked up.
                   A PRINTED SHEET SHOWS ALL OF ITSELF (owner ruling 0825):
                   the old phone rule capped a sheet at 292px and faded it out
                   with a `mask-image`, which dissolved a notice mid-sentence.
                   Gone. A sheet is now exactly as tall as its copy;
                   corkboard.js's FIT shrinks one sheet's `--note-fs` toward a
                   floor of 10 real screen px on a phone / 11 on a desktop
                   while that buys a row its share of the board; and the
                   close-up host SCROLLS (both sizes now, scrollbar hidden)
                   rather than hanging a term's worth of paper off the rail),
                   the BOOK (a close-up with deskbook.js over `ledgerPages`)
                   and the STOREROOM (`when:'ajar'`, so no flag = no rect, no
                   patch, nothing at all). THE TWO NUDGES: `.rr-fresh`, a pink
                   tab on the tray while the total stamp count beats
                   `recordsRoomSeenStamps` (banked at every panel open), and
                   `.rr-late`, the first-ever visit holding the other three
                   rects back for 2s so the tray lights alone
                   (`recordsRoomVisits`) - WITH `.rr-solo` on the tray for that
                   same window, a second brighter ring, because `.arm-swim`'s
                   2px/.30 resting rim going away is not something a player can
                   see. Both are OFF under .arc-reduced (and .arm-lite gets no
                   solo ring either - static state only).
                   THE MINIATURE hangs on `CORK_WIDE` [237,165,302,152], the
                   PAINTED cork measured off vn-09 (frame inner edges x 236 and
                   539, y 163 and 362, so the cork face is 237..538 x 165..361)
                   and trimmed to stop 5px above `LAMP_SHADE_TOP` (322). It is
                   NOT `RECTS.corkboard` - that rect is the framed OBJECT with
                   press slack under it, and paper hung on it sat on the frame,
                   ran onto the desk and landed on the banker's lamp (trap 105).
                   The mini is dealt the close-up's own `fit` (same `boxH`, same
                   stage `scale`) plus `wholeRows:true`, so it is a picture of
                   the top of the board and never a sheet sliced along a rail.
                   THE ROOM'S FX TABLE (W2 0825) is `recordsFx()`, exported
                   beside the rects and crop-verified on the plate: the sign
                   [577,169,346,91], the lamp pool [383,433,282,42], the dust
                   cone [420,300,200,180], the window [52,120,64,291], the
                   clock {1009,187,r44} with a `faceR` of 19 (the painted hands
                   reach r16 and the numerals start at r21, so the disc buries
                   one and leaves the other painted), and - `when:'ajar'` only
                   - the seam's edge [1180,132,6,444] plus a floor pool
                   [1107,552,180,148] that runs BELOW the apron line on
                   purpose, because it is light on a floor and not a rect. The
                   corkboard's paper flutter is pure CSS in corkboard.css
                   scoped to `.rr-cork`, animating `rotate` and NOT `transform`
                   (the slot deals each sheet a rotation and `.look-askew` adds
                   its own; a `transform` keyframe would snap every sheet
                   straight on hover). NO AMBIENT BED: `arcademy-sfx` reads no
                   `loop` and has no per-name stop, so the room asks for none
                   and says so in a TODO rather than owning an audio node
                   (trap 18)
                   Narrow caps, the annex's law: the shell keeps the store, the
                   bridge and EMI. AND IT MUST BE TORN DOWN IN clearScreen -
                   the apron is on <body>, so a room left standing leaves a
                   slab across the next screen. + recordsroom.css
                   THE MINIATURE (0825): the wide shot hangs a scaled copy of
                   the SAME night's wall in the cork rect
                   (`mountNotices({preview:true})` inside `.rr-corkmini`), so
                   the painted board has paper on it before you walk up to it.
                   A preview marks nothing read and banks no visit - looking at
                   a board is not reading it - and it takes no pointer, so the
                   board hotspot owns every press over it. The scale is DERIVED
                   (rect width / CORK_INNER width), never typed, so a sheet in
                   the picture is the shape of the sheet on the wall.
shell/deskbook.js  THE BOOK ON THE DESK: a two-page spread mounted over the two
                   painted page rects. `left`/`right` are SIDE HOSTS (.rdb-side,
                   no clip) with the paper (.rdb-page, overflow:hidden) inside:
                   the chapter tabs hang off the right SIDE's outer edge, over
                   the painted page-edge. As a child of the clipped page they
                   were laid out at left:100% and clipped away to nothing -
                   three tabs that had never once rendered. Chapters + tabs,
                   page arrows and ArrowLeft/ArrowRight (guarded on an injected
                   isActive(), never Escape - trap 80's lesson), a CSS leaf that
                   turns on the spine with the spread repainting at its midpoint,
                   and the last spread remembered in `recordsBookPage`. THE PROSE
                   LANDED (W2 0825): `BOOK` is the owner's approved draft
                   transcribed VERBATIM - 3 chapters (`story` / `rules` /
                   `tips`, keeping the three `records_book_ch_*` rows the C#
                   NeutralLexicon already mirrors), 26 pages, 13 spreads, two
                   BLANK cream pages (the inside cover's verso and the back)
                   and the ten house rules as a real `<ol>` whose every item
                   carries its own `value`, so THE REST opens at six. A page is
                   `{head, body, list?, blank?}`; `body` is PARAGRAPHS split on
                   a blank line and painted one `<p>` each. SIX PAGES ARE
                   CONTINUATIONS with no head: the draft budgeted 90-140 words
                   a page and the real ceiling at the shipped 19px on a 436x492
                   rect is nearer 95 with a heading, so four of its pages ran
                   31-167px past the fore-edge (measured headless on every
                   spread) and TURN at a paragraph or rule boundary rather than
                   dropping the type. Every chapter still starts on an EVEN
                   page so a tab lands on a whole spread. The prose does NOT go
                   through the lexicon (trap 26 caps a row at 96 chars and a
                   paragraph is not a label) - only the chapter tabs and the
                   two arrows are keyed
shell/idcard.js    THE STUDENT ID: the laminated card in the corner of the
                   campus, and the spotlight it opens (records.js's shape, z 46,
                   veil + key light + placard + Tab trap + one Esc rung above
                   the Records one). Two halves in one file: PURE PARTS that
                   campus.js imports (the drawn PHOTO PENDING / anon portraits
                   as `data:` URIs, `chipRung`, `paintChip`, `studentNumber`,
                   `runPhotoDay`) so the furniture card and the big card can
                   never disagree, and `createIdSpotlight()` itself. The card
                   shows name / number / term / enrolled / homeroom, a YEAR
                   stamp that lands on punchcard.js's own thud, a six-tile stat
                   sheet on a chime ladder, and a FLIP side (barcode, small
                   print, the Records line, the signature). The chip under the
                   photo IS the `presenceShare` discord rung (owner ruling, see
                   §3) and paints only from the echo. NOT SHAREABLE by ruling -
                   reportcard.js stays the one share pipeline (trap 13)
shell/accountchip.js THE ACCOUNT CHIP: the round photo miniature at the far right
                   of the topbar AND of the campus top-right cluster, and the
                   front-desk menu under it (Signed in as / Open my card /
                   Profile / Front Gate / Sign out). A HOST SLOT: mounted only when
                   `init.account` (or `account` on a `profile` frame) arrives -
                   the C# host never sends it, so the desktop is unchanged. The
                   page holds NO url: it posts `account-action {action}` and
                   the host walks (cclabs-web PROFILE-CONTRACT §8). FRONT GATE
                   (2026-09-03) is the third verb, `dashboard`: a player who
                   came in from the site had no breadcrumb back out. Drawn only
                   when the host LISTS it in `actions`, which the C# host never
                   does (the desktop's Play tab is already the way out). Photo =
                   same-origin path or a drawn monogram; "Open my card" is the
                   shell's own showIdCard. No Esc rung (trap 59's shape)
shell/annexreveal.js THE NIGHT THE WALL MOVED: the once-ever reveal cinematic
                     (cut to black -> a thud from below -> EMI startled -> one
                     second of the records office at night with a wall panel
                     ajar). An OVERLAY, never a screen (module-local stage +
                     one Esc rung, traps 48/50), z 48 so EMI's layer paints
                     over the black for free. Fires on the tenth hole of the
                     LAST card and sets `annexRevealSeen` - the only gate the
                     ajar panel on the records wall reads
shell/capsule.js THE TIME CAPSULE: the trophy case exhibit in the entrance hall
                 - one framed screenshot of the February 2026 dashboard under a
                 brass plaque. An OVERLAY, never a screen (traps 48/50), z 38
                 with the corkboard and the Bugle. Owns no store, bridge or
                 clock: `capsuleDoor()` (pure, exported) is the whole unlock
                 rule - a high-water mark that only rises, so thirty nights
                 once opens the case forever. Sealed it draws a parcel and the
                 count, and ships no art, so it cannot 404
shell/prizecounter.js THE PRIZE COUNTER (economy wave): the two-currency shop, a
                   SCREEN at depth 1 beside records - tickets (`t`, the nightly
                   wage) on a timber shelf, tokens (`k`, one a day at best) in a
                   glass case, so a player can tell what a thing costs from six
                   feet away without reading a word. Narrow caps, the annex's
                   law taken to the letter: it imports NO store, NO bridge, NO
                   lexicon and NO EMI - `catalog()`, `balance()`, `inv()`,
                   `unlocks()`, `payday()` and `t` all arrive as functions, so
                   the whole room is importable in bare Node. THE ECHO LAW IS
                   THE WHOLE DESIGN (trap 1): a Buy press paints a WAITING state
                   and sends `prize-buy`; nothing about the wallet, the
                   inventory or the unlocks moves until `settle()` is handed a
                   `wallet-result` frame by the shell. A 6s watchdog clears the
                   waiting look and says "the counter has gone quiet" - it does
                   NOT grant, refund or retry, because a page that guessed at a
                   purchase is a page that can sell the same token twice. Refusal
                   reasons (`poor`/`owned`/`full`/`locked`) each have their own
                   line; an unknown one degrades to a generic. Node-double
                   convention throughout (findCls by children index, never
                   querySelector) + prizecounter.css
shell/alleysign.js LOCKER ROOM / PRIZE COUNTER neon wall plates (counter/locker
                   wave): the booth's right wall walks to RM 004, the Locker's
                   left wall walks back, on `exits.js` sign hardware. Chevron
                   marches under motion, stands still under reduced/lite but is
                   never dimmed + alleysign.css
shell/reveal.js    THE REVEAL: the purchase ceremony that hangs off `arcademy-bought`
                   (whoosh -> plinth -> per-kind stage -> chime ladder). `kindOf(sku,
                   row)` is the CEREMONY kind (outfit|theme|frame|bell|poster|toy|pa|
                   walk|consumable|other), NOT the host catalogue's coarse kind.
                   Theme stage previews without persisting; PA stage asks pa.js via
                   `arcademy-pa-request` outside the two-a-session cap; `html.arc-
                   reveal-on` while up (EMI shop lines hold on it) + reveal.css
shell/pacaption.js VN caption for every `pa_NN` cue (EMI bubble skin, Front Office
                   nameplate, typewriter paced to the clip). Keyed by cueId so a
                   DROPPED cue (answered synchronously, `arcademy-pa-ended` before
                   the listener) never leaves a caption up; its rect rides
                   `campusDoorRects` so EMI keeps off it + pacaption.css
emi/shop.js        EMI's counter/Locker voice: `SHOP_POOLS` (barks.js shape, literal
                   strings, widget law) + `createShopVoice`. One pool per sku
                   (`shop.bought:<sku>`), wear/frame/theme/toy/bell/poor/browse/
                   sentToLocker/locker.opened. Rides the voice ladder via
                   `fire = voiceMoment`; new sku = new pool, nothing else to wire
shell/counterfx.js THE HOUSE MOVES, as one importable kit (counter/locker wave):
                   THUD, SHIVER, GLOW (warm cut), the ALMOST's gold ghost, the
                   bell's SWING, the reversed BANK, the till's COUNT-UP, the
                   SPARKLE BURST and the CHARGE-HOLD. Presentation only: not one
                   function in it reads or writes a balance, an inventory or a
                   meta key, and every one of them is a no-op on a node double.
                   `fxSheet()` links `counterfx.css` lazily the first time a room
                   asks for a move. TWO RULES IT KEEPS FOR YOU: reduced motion
                   takes the STATE and not the travel (trap 92 kills durations
                   with !important, so a "faster" version of a move is a move
                   that does not happen - the reduced cuts are declared classes),
                   and `cue()` is the one audio door, `pitch` multiplying
                   frequency and never duration. `armBoughtHold`/`boughtHoldMs`
                   are the seam that keeps the buy ceremony ONE gesture: the
                   counter arms the length of its bank and tray beat, and
                   shell.js delays the `arcademy-bought` DISPATCH by exactly that
                   (never the host's answer, never the store)
shell/lever.js     THE EXTRA CREDIT LEVER, in ONE place: the words, the lock
                   lines and the rail painting for the three-way wager
                   (Standard / Extra Credit / Honors). It has TWO hosts - the
                   campus door card (under Begin) and the room scene's apron
                   (beside the knobs) - because the room REPLACES the card for
                   every painted room, so a rail that lived only on the card
                   would be invisible in nine rooms out of ten. Imports nothing
                   at all (`t` and the caps arrive as arguments), and A LOCKED
                   RUNG STAYS ON THE RAIL, DIMMED: what you cannot pull yet is
                   the whole reason to walk to the counter, so hiding it would
                   hide the feature. Rebuilt on every pop rather than mutated -
                   a token spent between doors has to light Honors on the very
                   next door
shell/themes.js    CAMPUS LOOK (COUNTER STOCK): the pure theme TABLE. A theme is a
                   palette and NOTHING ELSE - thirteen CSS custom properties on
                   `:root`, then it walks away; every hue in styles.css is already
                   a var() off those tokens, so moving the thirteen reskins the
                   whole school with no per-theme CSS rule anywhere (that absence
                   is deliberate - a second source of truth would rot first).
                   Imports nothing (no DOM, no store, no lexicon) so it runs in
                   bare Node. `campusTheme` is the PAGE-owned meta key; the clamp
                   (`clampThemeId`) sends junk, unknown, unowned and a throwing
                   wallet all to `standard`. The shell owns the meta key, the
                   ownership question and the order of application - a mod skin
                   and a theme means THE THEME WINS while selected, and the revert
                   re-lays the mod's palette from `src.palette`, never a cache
                   (removeProperty cannot tell a theme write from a mod write).
                   Theme names reuse the prize rows (`prize_theme_drone`,
                   `prize_theme_snowday`) so a prize is called ONE thing in the
                   whole school. + themes.css (picker row, weather-canvas belts)
shell/themefx.js   THE WEATHER LAYER (COUNTER STOCK) - the school's FOURTH render
                   surface, after engine effects on `#arc-fx`, scene.js's motes
                   canvas inside a room, and campus-walker's SVG. ONE fixed
                   full-viewport <canvas> on <body>, pointer-events:none, painted
                   by ONE rAF loop throttled to ~30fps, carrying at most
                   THEME_FX_BUDGET (120 desktop / 48 phone) particles. Exists only
                   while a campus theme WITH an `fx` key is the active look.
                   WHERE IT SITS: the body-level stack is class/report stage 10,
                   campus stage 20 and the end card 20, topbar 30, the fixed exit
                   bar 32, the confirm 36, `#arc-fx` 40, `#arc-ceremony` 45,
                   `#arc-emi` 50, `#arc-toast` 60. The canvas takes z-index 25:
                   above the campus stage (which paints an opaque sky and would
                   otherwise bury it) and below every piece of chrome a player has
                   to read or press. Snow falls in front of the quad and behind
                   the topbar, the exit sign, EMI and every toast; it takes no
                   pointer, so no rect, door or button under it moves.
                   IT DOES NOT BREAK THE walk.js LAWS: those laws (rects and
                   polylines only, transform+opacity only, NO canvas, budget caps)
                   bind the walker's own SVG - a layer that shares a stacking
                   context with the plan and whose every node is hit-tested
                   against the campus's doors. This canvas is outside
                   `.campus-stage` entirely, hangs off <body>, hit-tests nothing,
                   and is alone on its layer - the same arrangement scene.js's
                   motes canvas already has inside a room, held to the same kill
                   switches. FOUR KILL SWITCHES, any one enough: (1) reduced
                   motion - NO layer at all, never a created-and-paused one, per
                   scene.js's law; (2) lite (`init.performanceMode`) - same;
                   (3) the active theme has no `fx` - same; (4) a class is
                   running - torn down for the duration, because games own the
                   screen. Plus the free one every timer owes: a hidden tab parks
                   the loop, returning re-arms it. The annex's law: the module
                   imports nothing - flags, seed and log arrive as caps, so it
                   runs in bare Node against a fake 2d context. NO live
                   Math.random: the rain is a function of `init.utcDateSeed`, so
                   two clients on one night draw the same first frame
shell/pa.js        THE PA ANNOUNCER (COUNTER STOCK, `pa_pack`): two timers and a
                   seeded plan, nothing else - no store, no element, no Audio
                   node, no document listener; every cue leaves by the one audio
                   door (trap 18) with bus 'voice', maxMs 8000 and a duck (NOT
                   the tutorial bus - that path caps a clip at 1.2s and would cut
                   every spoken line). Categories partition pa_01..pa_36:
                   ARRIVAL 01-08, SCHEDULE 09-20, ASIDE 21-30, CLOSING 31-36
                   (closing NOT in rotation - the numbering is the contract with
                   whoever records the lines). `planLines(daySeed, session)` is
                   pure and seeded (`daySeed|pa|<session>`); every gate is
                   re-asked at SPEAK time, so a pack bought while a timer is in
                   the air still speaks tonight. Caps: never under lite, never
                   during a class (the notify-fed flag OR the shell's `inClass` -
                   either yes is a no), two lines a session, ONE under reduced
                   motion. A line caught in flight by classStart is SPENT, not
                   deferred. Fed moments by the shell: campusReveal (after the
                   ghosts), classStart, classEnded (the teardownClass funnel, so
                   an Esc cannot buy a second announcement), campusUnmount
shell/peek.js      the shared hold-to-reveal verb (caps the class at A)
shell/keybinds.js  manifest-declared verb slots, one blob, PanicKey conflict check
shell/audio.js     THE consumer of engine 'arcademy-sfx' (WebAudio, procedural)
                   + SAMPLES door (assets/sfx, host-listed), ALIASES (verdict names
                   and sample floors), HOLD/STOP for room-tone beds (trap 114),
                   unknown-name blip logged once (trap 115).
                   COUNTER STOCK: cue `bell` resolves to sample `bell_brass` when
                   the player owns `brass_bell` AND the file is in `available` -
                   ONE resolution point (cueFor). bell_brass is an ALIAS onto
                   bell, NOT SAMPLE_ONLY, giving three floors: the brass file,
                   the school bell's own sample, the school bell's recipe (a
                   present-but-undecodable file must not ring the 660 Hz blip).
                   Ownership arrives via module-level `setBellCosmetic(getter)`
                   (audio.js is built in boot.js before the shell exists, so the
                   shell cannot hand it a constructor arg) - the `set_bell` bus
                   message writes the SAME slot; use ONE road only (the shell
                   uses the getter). The 36 PA rows (pa_01..pa_36) live FLAT in
                   assets/sfx/ (BuildSfxSamples is TopDirectoryOnly), all in
                   NEVER_BUFFERED (36 spoken lines as PCM = tens of MB for <=2
                   plays a night) and SAMPLE_ONLY (missing = silence, never a
                   blip); lines must sit under 8s (CLIP_REQ_MAX_MS truncates)
games/registry.js  guarded allSettled registry + tier math + class_suspended stub
                   + GAME_SEMESTER / OPEN_SEMESTERS (the release gate: a CLOSED semester's
                   games are ABSENT from the pool, never stubs; isOpenSemester())
                   GAME_META here is the PARACHUTE: it must mirror each module's
                   own family/meaty/flagship/timeBudgetSec, because the timetable
                   reads a suspended class's descriptor too
games/<key>/index.js  one folder per game; games NEVER import each other
  daily-trigger/   the daily word (homeroom, flagship, CLOCKLESS - no seconds chip, no time bar;
                   the ~1-min wordle is the ruled shape, trap 81) - bank/board/ladder/words-*
  lost-and-found/  the mosaic hunt (MEATY, flagship, 300s; finds are PER-TIER 26/22/16/13
                   via findsForTier, and every gate that was a count is now a rate - trap 83) - board/grade/hud/util
  deja-vu/         the pair memory (300s, MULTI-BOARD:  - script (the swap plan)
                   clear -> fresh seeded board (classSeed|bN) with one-notch escalation, bell = the
                   only end and the NORMAL end, timeout auto-C removed - trap 82)
  impulse-control/ the Drop Tube (pop/withhold)         - lex/schedule/scoring/render/style/tube3d/tube2d
                   (seeded three.js chute, vendored r185 in ../vendor/; tube2d = no-WebGL ladder)
                   + the House Rules decks casino (THE FLOOR: bulb-ring marquee, chime ladder,
                   gate, near-miss staging, jackpot ladder + royal) / pressure (THE SURGE: the
                   STREAK-driven CCP effects ladder + tube/HUD tremor, never the basin) /
                   trickster (the Tell, the crooked ring, ghost cursor, stat flicker);
                   THE LANDING: tube3d projects the middle of its VISIBLE hole into
                   `--ic-basin-x/-y` on `.g-ic` (render.js onLanding) and the basin /
                   ring / flourish / stamp hang off those; THE DUSK (`.g-ic-dusk`, z2 over
                   the tube, render.js) is pressure.js's rung-driven dimmer + FLARE;
                   render owns the drawn class-rules sheet + the lit HUD + the ticket debrief;
                   every deck injects its OWN <style id="g-ic-<deck>-style">; render's only
                   audio node is the grandfathered denied.mp3 sting - every other cue is engine
                   audio_trigger (pitch = the streak)
  the-deep-end/    2048 with trance-depth tiers (MEATY) - board/schedule/grade/lex/style/casino/trickster/pressure
                   the deepest tile is the heat dial; board/schedule/grade pure, casino+trickster+pressure decks
                   (pressure = the rung-by-rung CCP effects ladder + the Balatro board tremor/HUD juice)
  -- Semesters II + III (2026-08-23; every class below ships ALL the House Rules decks from day one:
     style (the look + the drawn class-rules sheet) / casino (THE FLOOR) / trickster (schedule-dealt
     cards, budget 2/4/6/8 by tier) / pressure (THE SURGE, the CCP-effects ladder on the game's own
     streak) + a lex.js <P>_LEX table; decks are dynamic-imported + null-safe so a broken deck never
     takes the class down) --
  misdirection/    the shell game (tracking, 120s)        - shuffle (PURE seeded plan + verifyRound = the
                   TRACKABILITY INVARIANT: occlusion hides at most ONE link of a swap chain and every
                   occlusion carries a tell) / grade / lex MD_LEX; keybinds pick1..pick5; md_stake_mode
                   ask|bank|ride (greed scored UPWARD only, ride cap 5), md_shell_skin themed|minimal|contrast
  sort/            the two-pile swipe (tracking, 180s)   - room 201, The Sorting Room, built
                   on the Entrance Hall's west span after Misdirection's retirement (lot 2
                   gave the old parlour to the front office). Right = TARGET, left = NOISE, and
                   the piles are the PLAYER'S OWN NICHES, picked at a setup DOOR that runs
                   BEFORE the class clock (`manifest.setup` + `instance.setup()`, §5). Truth is
                   the `tag` the host stamped on the row, never pixels; the deck comes from
                   `ctx.assets.claimTagged` (§3), never `claim()`
  echo/            the Simon ring (memory, 120s)          - sequence (PURE: warm start 3..6 off bestLen, decoy
                   plan from tier 2 telegraphed) / grade / lex EC_LEX; keybinds pad1..pad6; six pads always live,
                   the TIER restricts the alphabet; tones = engine audio_trigger 'pad' x pitch (+1 semitone per
                   link, cap 7); a fail is NOT the class (new sequences until the bell); Encore once, auto
                   THE TURN IS EXPLICIT (owner verdict 2026-08-23): `.g-ec-phase` (a banner, data-p
                   ready|listen|yours|miss|clear|over) + `.g-ec-steps` (one dot per sequence step,
                   data-fill off|on|bad) + the LISTEN LOCK (--ec-sat drop + not-allowed on the ring
                   while the room plays) + a HAND-OFF beat (its own chime, one pulse per pad). Both
                   tells are text/attribute, so motionLevel 0 loses nothing. Pad states grew `wrong`
                   (red + shake) and `reveal` (the answer, held REVEAL_MS under a "this one" halo).
                   THE PADS ARE THE BUBBLES: each is bound (seeded, no duplicates) to ONE trigger
                   from `init.triggers`, wears its PHRASE as the face, and plays that trigger's
                   whisper clip faintly UNDER its note. `ec_pad_words` = words (default) | glyphs |
                   media - the old gif faces are now the opt-in, not the look.
                   ROUND 2 (same owner pass): the face is VEILED - frosted until you hover / press /
                   focus it, and unveiled for the beat a pad is lit, so LISTEN also reads the phrase
                   out to you. The frost is `color:transparent` + a text-shadow of the same letters,
                   NEVER `filter:` (trap 36 - a filter mints a surface per pad over a live lamp).
                   The hand is RE-DEALT every round (`dealRound`): the hues, the glyphs and the
                   hitboxes never move (Law II), only the words, seeded off `seed|ec-words|<cycle>`
                   so a retake deals the same hands; a pool larger than six is walked whole before
                   any word repeats. A trigger's clip plays on only `CLIP_CHANCE` (0.25) of beats -
                   the roll is SEEDED and always consumed (a roll that lands while a clip is still in
                   the air is spent, not layered), so the whisper pattern is a function of the seed,
                   not the wall clock. THE FIT: one size for the whole ring (`--ec-word-px`), the
                   largest that wraps every dealt phrase into three lines AND spells every word
                   whole; measured with a hidden 100px RULER (`.g-ec-ruler`) because a centred word
                   box reports `scrollWidth === clientWidth` even while the word hangs out of it
  instant-recall/  the vigil (recall, 180s, MEATY)        - vigil (PURE seeded script: stops w/ FINAL-STOP
                   GUARANTEE in the last 15s, density sawtooth, plants, templates LAST_WORD/EFFECT/STING/TWO,
                   THE CADENCE (owner 2026-08-23, "about 5 rounds per minute"): a RATE, not a count -
                   scaleStops holds every budget inside 4.4-5.6/min, so 180s deals 14-16 stops at
                   EVERY tier, ONE question each - tier moves the window (6/6/5/4s) and the template pool,
                   never the rate. MIN_GAP_MS is DERIVED, not tasted: window + VERDICT_MS + DEAL_BEAT_MS +
                   FRESH_MS + slop, so even a fully blanked stop leaves >= 4s of live wall, and seedDues
                   pulls the two earliest dues in so that wall always SAYS >= 2 things - a question can
                   never re-ask the entry the last one was about. grade.js's S gate + timeout ceiling are
                   ratios of the class's own length now (floor(n * 0.12)), not absolute counts,
                   + THE EFFECT POOL: ten CCP effects under CCP's own names, 4/6/8/10 by tier, and ONE seeded
                   dealer that enforces MIN_SEPARATION_MS 700 between any two starts) / montage (THE WALL: one
                   full-bleed grid solved from the stage aspect, two faces per tile, seeded dwells, ONE interval
                   driver + the L&F live-window discipline + createLedger = the TRUTH tail, aria-hidden) /
                   grade / lex IR_LEX; ir_density
                   THE MOSAIC REWORK (owner ruling 2026-08-23, "a mosaic like the FYP... the effects should be
                   those they can recognise"): the rows/mosaic/swirl layouts, the MODE template and the
                   engine-kind option names (`Wash`, `Scanlines`, `Drift`) are GONE. A LAST_EFFECT option is a
                   POOL KEY (`ir_fx_flash` "Flash image", `ir_fx_corner_gif` "Corner GIF", `ir_fx_brain_drain`
                   "Brain Drain" ...), never an engine kind - which is what keeps corner_gif vs fullscreen_gif
                   (both `gif_burst`) and spiral/pink/brain_drain (all `wash`) four distinct honest answers.
                   TWO LAWS THE DECKS INHERIT: a tile swap NEVER writes a ledger entry (the wall is the room),
                   and NO deck may fire a POOL primitive - pressure.js dresses with crt / ambient_field /
                   glitch_swap only, and casino.js passes `garnish:false` to `ceremony('jackpot')` because the
                   forced `drain|spiral` garnish would otherwise be a real Spiral the ledger never saw.
                   THE VARIETY REWORK (owner ruling 2026-08-23, "seems to ask me only about the subliminals
                   that played"): TEN question families - LAST_WORD / LAST_EFFECT / WALL_PICK / SPIRAL (t1),
                   LAST_STING or HEARD / WALL_SEEN / WALL_TWICE (t2), LAST_TWO / WALL_GONE (t3). The dealt
                   variety was always real; the RESOLUTION collapsed, for two reasons that are both gone:
                   `DISTRACTOR_EXCLUDE` (LAST_EFFECT's decoys were the tier pool minus the last five
                   emissions, and a tier-1 pool is FOUR keys, so it was almost never instantiable) is replaced
                   by the per-tier `TAIL_DISTRACTORS` allowance - a thing that fired EARLIER is the
                   recency-error decoy `ir_near` was written for, not an ambiguity, because "the LAST one" is
                   unique by the 700ms rule; and `resolveTemplate` is HISTORY-AWARE (it picks the family the
                   class has asked LEAST, never the one the last question resolved to) instead of walking a
                   fixed FALLBACK_ORDER that always landed on LAST_WORD.
                   THE DEAL IS PERMUTATION ROUNDS, not a weighted ban: round r is a seeded permutation of the
                   surviving pool, the stops walk round 0 then 1..., and a round whose first entry equals the
                   last dealt swaps its first two. Coverage is structural (floor/ceil(n/k) each, never twice
                   in a row) and `assertPlan` re-checks it. Families whose MATERIAL does not exist are dropped
                   at PLAN time (`templateDrops`: wordCount<4, clipCount, spiralCount<4, wallOk) - a family
                   the plan deals and then always falls out of is the bug this replaced. LAST_STING and HEARD
                   are ONE-OF-TWO: clips in the mix -> HEARD (the phrase is the content, NO re-listen button);
                   no clips -> LAST_STING. Media families render `.g-ir-opt-media` previews with no
                   `.g-ir-opt-t`, which is how the trickster's Unreliable Label folds on them.
                   THREE TIMING RULES the new families need: THE CUE (`seedDues(..., wantKey)` pulls the next
                   stop's own channel in to `CUE_LEAD_MS`), THE QUIET (`nextEmission(..., stopAtMs)` refuses to
                   start a channel inside `PRE_STOP_QUIET_MS`, so the last entry was fully PERCEIVED), THE
                   QUENCH (trap 44). THE WALL BOOK = one frozen `montage.snapshot()` per stop, cap 16, and
                   every WALL_* answer is DOM truth read there - never the plan, never the plant request.
                   Sub words are HELD, not brightened: `fire('sub_flash', {holdMs: SUB_HOLD_MS[tier]})` plus a
                   game-local plate on `.g-ir-stage .ae-sub-word` (the alpha is still the engine's clamped
                   channel); `CADENCE.subliminal.min` 1000 -> 1400 so two held words cannot overlap.
  anomaly/         the odd-one-out grid (search, 300s)    - rounds (PURE: kinds/deltas at PERCEPTIBLE floors,
                   relocations cap 2/round, drift) / grade / lex AN_LEX; the odd index lives in CLOSURE ONLY -
                   never a DOM attr/class (suite asserts it); decks get a canMelt(i)/meltCandidates() oracle
                   and nothing else; an_kinds all|gentle
  composure/       the sliding picture (puzzle, 300s, MEATY, MULTI-BOARD: a solve BANKS and
                   re-deals seeded scrambles until the bell, trap 82) - board (PURE, seeded SOLVABLE scramble w/ parity)
                   / solver (PURE baseline: optimal 3x3 IDA*, 4x4/5x5 BFS over tracked-tiles+gap - the greedy
                   textbook solver deadlocked 1 board in 5) / grade (par from the solver) / lex CP_LEX;
                   manifest.peek TRUE (the shell's hold-to-reveal = A-cap); cp_mode timed|zen (zen ends
                   {zen:true} = 'pass'), cp_zen_grid; skill-floor rescue after 20s (solver hint + sGate false);
                   locks are MARKERS never freezes (a frozen tile can make a board unsolvable)
emi/         EMI, the mascot: a living pixel CRT that FLOATS over the whole page.
             The body is a POSE SET of PNGs (art/emi/body*.png, all 859x869, all
             sharing the exact screen rect): body.png is the arms-up CELEBRATION
             frame, body-idle.png the arms-down default, plus sad/shock/smug/pet
             and four sway micro-variants. widget.js owns the layer: every chain
             names its `bodyFrame` in chains.js, a raw hold resolves through
             FACE_BODY_FRAME (face family -> frame; junk -> idle, never throws),
             `opts.bodyFrame` overrides both, and a missing file silently falls
             back to body.png. At rest an idle sway loop ping-pongs the variants
             (~200ms steps, long randomised centre hold; OFF under
             prefers-reduced-motion / html.arc-reduced, stopped by every chain,
             say, drag, hide). New frames reproduce the body.png recipe: octree
             quantize to 256 + optimized PNG (~40-46K each).
             COUNTER STOCK prizes ride widget.js: the desk toy (`emi_desk_toy`,
             art/emi/toy.png, onerror hides) and the VARSITY frame set
             (`emi_varsity`, art/emi/varsity/ - the SAME TEN filenames at the
             SAME 859x869 rect, and the swap is ALL-OR-NOTHING off one probe of
             varsity/body-idle.png: a partial folder 404s odd poses to the
             celebration frame, so never ship fewer than all ten). Varsity art
             must NEVER cover the screen - the face canvas draws at the shared
             fixed screen rect over whichever body img is up. Ownership arrives
             as `prizes` row getters through mountEmi -> createWidget (the shell
             resolves them, she never sees a wallet); `emi.setPrizes()` re-reads
             on the wallet echo. The FACE IS TEXT -
             a kaomoji drawn on a 152px canvas and nearest-neighbour upscaled, so
             any font becomes pixel art. Owner-locked design; the spec is
             EMI-DESIGN-LOCK.md, not this file. Two halves, two owners:
  face.js      createFace(canvas, opts) - the renderer. Locked settings: res 152,
               95% fit, +2% lift, stroke 5, auto-orientation, kaomoji +10%.
  chains.js    FACES sets + the CHAINS table (wink blink wake shock sus thinking
               glance nod say sayNod cry rage reveal glitch love glee cool dizzy
               smug ko) + playChain(chain, hooks) + makeSay(line, reactionFace,
               holdMs). VERBATIM from the lock - re-time a chain there, not here.
               `glee` ((≧◡≦)) is the THREE-PETS / STREAK-STAMP beat; `love`
               ((｡♥‿♥｡)) is a different, rarer one. Do not swap them.
  channels.js  THE OFF CHANNELS (W3): `SL_DIALS` + the eight painters + the
               weighted wheel. DATA AND PAINT ONLY - no timer, no rAF, no DOM
               node, no I/O, which is what makes the whole wave testable in
               node against a fake 2d context. Every painter is
               `{id, weight, cooldownMs, plan(ctx)->spec|null, prepare?,
               start(g,spec), frame(g,spec,t), end(g,spec,reason), gag?, caught}`
               and `g` is `{c, w:152, h:137, rm}` - face.js's own geometry.
               CH1 pong / CH2 browsing / CH3 watching / CH4 reruns / CH5 shop
               ride the wheel; CH6 wrong rides other channels' EXITS; CH7 saver
               and CH8 offair are the DETERMINISTIC deep-idle pair. Every line
               in a `caught` table is tagged `DRAFT: /emi-lines pass pending`.
  takeover.js  `createDeck(...)` - `screenTakeover(painter, {ms})`, the blip,
               the one rAF, the wheel timer, the five document listeners, the
               caught arc (snap -> shiver -> offer -> reveal card -> afterglow)
               and `createMediaBroker` (the wave's ONLY I/O). Traps 76-79.
  fx.js        showFx(host, kind) - hearts/sparks/tears/storm/bang as pixel glyphs.
  vox.js       HER VOICE, "Blipese". createVox() -> {speak, tick, stop, destroy}
               + the pure, harness-testable makeScore(text, mood) and the frozen
               VOX_DIALS. It owns NO audio node (trap 18): it turns a line into a
               SCORE of {atMs, pitch, gain} and fires one `arcademy-sfx` per blip
               off its own setTimeout ladder, on the `voice` bus at level 0.4 -
               deliberately under every game one-shot, and never with a `duck`.
               Grain is per-SYLLABLE (vowel groups, 1..4); punctuation is prosody
               (`?` lifts the sentence's last blips, cleanly and with no jitter;
               `!` raises and hurries the whole line; `...` sags then rests); the
               mood is the BODY FRAME FAMILY widget.js already resolved. Seeded
               on the LINE TEXT via core/rng.js, so a line always sounds like
               itself. Two ceilings, MAX_BLIPS 13 and BURST_MAX_MS 1400, and a
               long line is compressed by giving up SYLLABLES - never by speeding
               the gaps up, because the pace is the character. See trap 70.
               Timbre lives in `shell/audio.js` SOUNDS: `emi_blip` / `emi_tick`.
  emi.css      the SKIN: .emi / .emi-body / .emi-screen (the locked glass rect) /
               .emi-glass (THE SECOND CANVAS, the same rect, hidden at rest) /
               .emi-fx / .emi-bubble + the body moves (.breath .nod .shiver
               .bounce .thud .droop). Ships BOTH bundled fonts (fonts/*.woff2,
               OFL, licences beside them): Noto Sans Mono for the CANVAS face and
               Press Start 2P for the speech bubble + the dock glyph. The bubble
               is a fixed 8px/104px pixel grid, never a cqw clamp - see trap 55.
  demo.html    standalone renderer tester (no shell), loads the real modules.
  widget.js    THE FLOATING ELEMENT: mount, drag, pet, hide/dock, persistence.
               Since EMI ASKS it also owns the CHIP STRIP (`mountAsk` /
               `unmountAsk` / `askReady` / `asking`), the four seams an ask's
               YES needs (`parkMirrored`, `setGazeBias`, `creditPet` and
               `apparate`'s `stay`) and the ask LEDGER, which rides the same
               `emi` blob and the same debounced writer (`askState` /
               `askSave`) - see trap 96.
               Owns the pointer verbs and the ONE chain runner (so there is
               exactly one thing to cancel and one place that knows a SAY is
               mid-line). It NEVER imports the renderer - face/chains/fx are
               injected through attach(), which is what keeps a broken face out
               of the shell's boot path (`vox` rides the same seam). DIALS at the
               top are the tunables.
  index.js     mountEmi({layer, store, toast, enabled}) -> the ONE controller
               {emote, say, idle, hide, show, setEnabled, setWidth, stats, flush,
               destroy, el}. `toast` is the SHELL's toast, borrowed for exactly
               one line (the first x-dismiss ever); EMI mints no toast of her own.
               + getEmi(). Dynamic-imports the three renderer modules OPTIONALLY
               (shell.js's loadOptional discipline) and replays at most one
               pending call inside a 2.5s grace window.
  moments.js   THE TABLE: shell moment -> emote, per the lock's state->moment map
               (greet classStart stamp win miss fail runLost streakBroken thinking
               idlePlayer tabAway suspend resume rareDrop firstUnlock reportCard
               glitch) + fireMoment(name, payload). An unknown name, an unmounted
               EMI and a dismissed EMI are all silent no-ops, which is why every
               call site in shell.js is one unguarded line. Plus the ONE generic
               row: any `game:*` name (a class-commentary note from
               `ctx.mood.note()`) answers out of GAME_NOTE_FACES by
               `payload.kind`, so a name this table has never heard of still
               gets a face. Traps 112-113.
  heartbeat.js THE METRONOME (2026-08-25): createHeartbeat({widget, emi, voice,
               asks, trips}) -> {start, stop, tick, state, destroy}. ONE
               setInterval at HB_DIALS.TICK_MS that measures how long since
               anything visible happened (`widget.onActivity`) and, when the
               period is up and every gate holds, draws ONE act off a weighted
               wheel under the sequence law: face / fidget / nudge / screen /
               bark / ask. It owns no data, no rations and no verbs - it decides
               WHEN, and the engines that own each act decide whether. The one
               sanctioned unattended spender in EMI; mounted last from index.js
               and destroyed first. Traps 109-111.
  widget.css   the layer (.arc-emi, fixed, z 50), the grab/grabbing cursors, the
               x affordance, the edge dock, and the bubble's `.bubble-left` /
               `.bubble-low` flips (right margin / top edge). Plus THE CRT
               POWER-OFF (`emi-crt-off` / `emi-crt-on`, 200ms each, and the
               `.crt-blank` frame between them) - the field trip's transition.
  asks.js      THE ASK ENGINE (EMI ASKS, 2026-08-25): the one thing she does
               that is not a reaction - a question, and then she WAITS. An ask
               is a bubble line plus a two-chip strip that persists until a
               chip is clicked, the ask is dismissed, or she gives up (40s; 12s
               for the ones that ride `classStart`). The IGNORED path is
               universal and WORDLESS: chips out, `-_-` 1400, bubble `...`,
               idle. No line, ever. Five gates - session 3, not mid-class, not
               over a live verb (`widget.askReady()`), the cadence (3 sittings,
               or 2 on a 1-in-3 roll; the NAME ask is exempt) and one ask a
               sitting. TWO entry points and the order is the feature:
               `note(name, payload)` runs on EVERY moment (the latches - is a
               class up, did a dare resolve, is the gaze bias spent) and
               `offer(name, payload)` only on the ones the voice AND the trips
               declined. `greetIntercept()` is the third, and the only thing in
               EMI that runs BEFORE the voice: three skipped "bed?"s replace
               one greet with a groggy one. Effects a YES buys are all widget
               seams (`parkMirrored` / `setGazeBias` / `creditPet`) bar two -
               a01's `soft` night, which shell.js reads off `asks.flags`, and
               the DARES, which flag the next class and resolve on its own
               win/fail into `dareWon` on the payout frame. The lines are the
               ASKS table in the file, VERBATIM from
               `planning/arcademy/EMI-ASKS.md`.
  fieldtrips.js THE ONE AUTONOMOUS VERB (W2a, 2026-08-24): the POI registry and
               the scheduler that decides she may use it. `widget.apparate()` is
               HOW a trip happens; this is WHEN, and it is FIVE gates - never
               before session 3, at most one a sitting, one visit per fixture
               for ever (voice.js's own `seen` ledger via `hasSeen`/`markSeen`),
               the right screen with the fixture actually measurable, and truly
               idle. It owns NO timer and NO observer: it is a passive
               `offer(name, payload)` that `index.js` calls when the voice has
               declined a moment, and the only moment it answers to is
               `idlePlayer` - which campus.js already fires on its attract
               loop's own idle edge ("a mascot does not get a second idle
               timer"). At rest the file measures nothing and costs nothing.
               Six POIs, all campus scenery that `campus.update()` never
               rebuilds; the Records door and every other `.facility` is OUT,
               the same geofence voice.js keeps. The LINES are barks.js's
               `FIELD_TRIPS` table, keyed by `lineKey`; a key with no row is a
               POI that never travels.
vn/          FIRST BELL: the once-ever opening, and the ONLY thing in this bundle
             that is allowed to sit between the splash and the campus. Four
             scenes, all first-run: s01 the gates (2 captions), s02 the
             admissions desk (paper #1) ending in THE BOARD HANDOFF, s03 the
             walk to Homeroom, m01 the second slip after the first-ever stamp.
             Spec: `<Screenshots>/arcademy-vn-proposals/FIRST-BELL.md`. See
             trap 76 - the safety laws are the feature.
  index.js     createFirstBell({store, rows, firstNight, canInterrupt, onMoment,
               reducedMotion, base, log}) -> {armed, splashDone, gateClass,
               afterCeremony, seenState, bankAll, destroy}. Mints its OWN fixed
               layer (z 58: over EMI, under the toast) - index.html grows no id.
  scenes.js    PURE data: the step lists, the plate names and BOARD_ZONE
               (x 25-60%, y 18-48% of the 16:9 frame, the panel SET-NOTES
               reserved). A step is caption / paper / hold / fx / swap / board.
  lex.js       VN_LEX + PAPERS. The two papers are stored as CLAUSE rows joined
               with one space, so every row clears the 96-char mod-skin cap
               (trap 26) while the joined paragraph stays verbatim.
  style.js     the skin, injected as <style id="arc-vn-style"> the way a game
               injects its own (styles.css is shell chrome only).
  demo.html    standalone scene tester, no shell and no bridge; `?beat=<id>`
               (gates|desk|board|walk|mail|coldopen|reduced, plus `&hold=1`)
               jumps straight to one beat so it can be shot headlessly.
annex/       THE RECORDS ANNEX: the lab under the Records Office. A SCREEN
             (shell.showAnnex), the records.js way - the shell owns the store,
             the bridge and EMI; nothing below this line imports any of them.
  lab.js       the room: four 1376x768 slides (wide control room / monitor wall
               / clerk's desk / binder shelf), the paper props and the laptop
               that zooms into the OS. Esc folds inward-out (paper -> OS window
               -> laptop -> close-up) and the shell's own rung walks home to
               records. Assets resolve module-relative (the nine-broken-logos
               law); the monitor wall LIFTS createCamWall, never copies it.
  cams.js      the camera wall: nine small SVG crops of the SAME campus plan
               campus.js draws, walked by ghosts.js's pixel students, plus the
               locked laptop feed. Chrome only - no bridge, no store, no fetch;
               the clock is diegetic and .an-lite drops every decorative
               animation in one place (trap 36: no live clones of the plan).
  os.js        the dated windowed desktop on the laptop: login, FILES, REGISTRY,
               SUBJECT SEARCH, TERMINAL. Carries punches 1-3 (punch 4 is the
               dossier on the desk, lab.js's). Renders truths it was handed and
               computes none: LIVE counts or LINK DOWN, never a fiction, and
               counts under REDACT_UNDER draw as black bars.
  docs.js      THE PAPERS: pure data, every document the OS and the props show.
               No imports, no DOM. UNIT EMI skips file 05 on purpose (story lock
               0823) and the closing glyph renders only at four punches.
```

Each game owns its own lexicon rows; **`ArcademyHostService.NeutralLexicon` mirrors every
one of them** (672 rows as of Semesters II+III, 2026-08-23 - the count is a
floor, never a contract: a scratch script diffs every `t('key'` / lexicon table against the C#
table, see §7) or the shell renders raw keys for the settings
page's `label_key` / `hint_key`. Impulse Control exports its table as data
(`impulse-control/lex.js` `IC_LEX`) - copy the values, do not re-word them.

**THE ANNEX.** The gate is two steps: `annexRevealSeen` (set by `shell/annexreveal.js`)
arms the ajar panel on the Records Office wall, and only picking that panel opens the lab.
The lab is a SCREEN, not an overlay - `showAnnex()` mints it with narrow caps only
(`t`, `lite`, `subject`, `annexState`/`saveAnnex`, `liveFile`, `fetchStats`, `onExit`).
The shell brackets EMI around it (`setEnabled(false)`, restored on EVERY path out) and flips
the voice's `labSeen` on first entry. Progress is ONE page-owned meta blob under the key
`annex` = `{visited, os, p1..p4}`, never four keys. The fence words (retention, engagement,
metric, subject, experiment, data) are legal DOWNSTAIRS ONLY - `annex/docs.js` and the
`annex_*` lexicon rows - and nowhere else in the school. The REGISTRY's live counts arrive
through the host verb `annex-stats` (C# fetches the public aggregate; a null body is LINK
DOWN, and the page never invents a number). **The web build has a third key to that door
and it is not a third gate** - see trap 99, `init.devAnnex`.

## 3. Cross-agent seams — change these only with the other side

- **`shell/settings.js` → `SETTING_KEYS`** is the *complete* list of keys the page writes.
  They are **protocol names, not C# property names**: the init projection's own camelCase
  fields, flattened (`masterIntensity`, `caps.flashRate`, `audioLevels.fx`, `audioMute`,
  `hideTutorial`, `effectIntensity`, `keybinds`). `ArcademyHostService.ApplySetting` maps
  them onto AppSettings and re-clamps every one; `effectIntensity` deliberately lands on the
  existing app-wide `ChaosEffectIntensity` rather than minting a duplicate guard.
  - Anything the host does *not* recognise is bagged as a **per-game** knob under the key
    verbatim (no prefix) in `ArcademySettingsJson`. That is why `GLOBAL_RESERVED` in
    `settings.js` is load-bearing: a manifest declaring `flashRate` would otherwise write
    the global ceiling. `isGlobalSettingKey()` is the same fence on the echo path.
  - `keybinds` is sent as an **object**, not a JSON string - the host tests
    `value is JObject`. A non-object is now REFUSED and the reply carries the value that is
    still STORED (it used to store `""`, i.e. one malformed frame wiped every rebind the
    player had made). The blob is also capped at 7000 chars, deliberately below
    `AppSettings.ArcademyKeybindsJson`'s own 8192 wipe cap.
- **Attendance is HOST-owned.** `ArcademyMetaStore` mints `streak`,
  `perfectAttendance`, `lastAttendanceLocalDate` and `todayClasses` from the `class-ended`
  frame (so a stale page cannot forge a streak) and **refuses** a page write to any of them.
  `core/store.js` lists them in `HOST_OWNED_KEYS`, drops such writes locally, and reads the
  numbers back from two places: `payout-result` (which carries `streak` /
  `perfectAttendance` / `classesToday` on the same frame) and the whole-blob snapshot.
  The page still owns `days` (the graded view) and `games` (tier + per-game state).
  - **`campusTheme` is a PAGE-owned meta key** (COUNTER STOCK), beside `leverPick`,
    `recordsRoomVisits`, `recordsRoomSeenStamps` and `recordsBookPage`. No C# change was
    needed - `ArcademyMetaStore.Set` takes any new top-level key, and the host has no
    opinion about what the campus looks like. The page CLAMPS on every read
    (`clampThemeId` against the wallet), so a stale or forged pick degrades to `standard`.
- **COUNTER STOCK ownership travels as GETTERS off `ownsSku`, one per consumer, wired in
  `shell.js` only.** corkboard gets `posters:`, the walker gets a `cosmetics:` bag (read at
  BUILD - the campus is rebuilt every visit; plus `lite:`, which gates the cosmetics ONLY,
  never the walk itself), EMI gets `prizes:` row getters plus `strings.toy` (she never
  imports the lexicon), audio gets `setBellCosmetic(getter)`, pa gets `owned:`. None of
  those modules imports the store or the wallet - the shell hands each an answer. Purchases
  settle ONLY on the `wallet-result` echo; the same echo handler re-runs `applyTheme()` and
  `emi.setPrizes()`, so a prize bought mid-session lights without a reload.
- **The catalog's WAVE projection is host-side** (`ArcademyEconomy`): `CatalogItem.Wave`,
  `CurrentWave` const, `InStock()`. An above-wave row is ABSENT from the wire (never
  "locked"), `Buy` refuses it as `unknown`, and shipping the next wave is ONE const bump -
  the NeutralLexicon already carries all eleven rows regardless, so no second trip through
  nine language files.
- **`punchCards` is HOST-owned too** (PUNCHCARD.md §2). `ArcademyPunchCards` is the pure math,
  `ArcademyMetaStore.StampPunchCard` / `EnrollPunchCard` the mints; the key is refused to the page
  and every date is LOCAL. One card per game key:
  `{punches:0..10, dates:["yyyy-MM-dd"], sDates:["yyyy-MM-dd"], enrolledAt:string|null,
  house:bool, complete:bool, unlockedAt:string|null}`, with `punches` recomputed on every
  touch so a bad blob self-heals.
  - **THE PACE, and THE FORMULA** (owner ruling 2026-08-23 - three levers shortened the time
    to master a card). Enrolling is worth **3** holes, an ordinary stamped day **1**, and a day
    the class graded **S** is worth **2**. `sDates` is the whole of the third lever: the subset
    of `dates` that graded S. The derivation, and it is ONE line that must match on both sides:

    ```
    punches = min(10, (enrolledAt ? 3 : 0) + dates.length + sDates.length)
    ```

    `core/store.js punchCard()` and `ArcademyPunchCards.Normalize` compute exactly that, and
    both intersect `sDates` with `dates` first, so an S entry for a day that never stamped is
    worth nothing - a card heals DOWN, never up. (The third lever, **four classes a day**, is
    `CLASSES_PER_DAY` in `core/timetable.js`, not a card field.)
  - **OLD BLOBS ARE PROMOTED, deliberately.** A card enrolled under the old two-punch rule
    carries no marker of which rule minted it (`enrolledAt` is the only earned field), so the
    re-derivation pays it the current rate: everybody who already enrolled gains one hole on
    the next touch. It cannot overflow - the total caps at ten and a complete card stays
    complete - and a blob with no `sDates` at all reads as an empty list, never a throw.
  - The **daily stamp rides the attendance credit** on `class-ended`, which makes "any graded
    finish stamps, once a local day" true for free: Esc-leave sends `class-left`, and a Free Swim
    never sends `class-ended` at all (`shell.js finishClass` returns first).
  - The page posts **`enrollment-done {gameKey}`** once, after the enrollment ceremony. It mints
    the three first-run punches and **supersedes that day's daily stamp** (which has already
    landed, the ceremony running after `class-ended`) - the day is folded out of BOTH `dates` and
    `sDates`, so day one is exactly 3, never 4 or 5, in either ordering and even when that first
    night graded S. Repeat frames are no-ops.
  - **THE S DOUBLE IS THE HOST'S DECISION, and it is decided ONCE.** The grade is only known at
    `class-ended`, so `ArcademyHostService` reads it there (`grade == "S"`, after the clamp) and
    passes `gradedS` down through `ArcademyMetaStore.StampPunchCard` to `ArcademyPunchCards.Stamp`.
    The same-day guard runs BEFORE the S list is touched, so **a retake mints nothing whatever it
    grades** (trap 23) and cannot upgrade a day that already stamped at one: the FIRST graded
    finish of the day decides. The page has no S-day verb and never writes `sDates`.
  - The host answers both paths with **`punchcard-result {gameKey, reason:'daily'|'enrollment',
    minted, justUnlocked, holes, card}`** - same-frame truth for the ceremony, the way
    `payout-result` carries the streak. **`minted` is a COUNT, not a flag** (2026-08-23): 3 on an
    enrollment, 2 on an S day, 1 on an ordinary day, **0** on a no-op. Zero is still falsy, so
    the shell's "no ceremony for a hole that was not punched" test reads exactly as it did when
    this was a bool - but a reader that treats `minted` as boolean truth loses the double. The
    count is MEASURED off the card (post minus pre), not assumed, so a card at nine holes that
    grades S is told 1. The whole-blob `meta` snapshot is pushed as well.
  - `complete:true` IS the permanent unlock: the shell offers Begin on that room every night
    through the same door path as `devDoor`. Nothing host-side gates which room may start.
  - **AND IT IS THE DISCORD KEY TOO** (the Activity wave, 2026-08-28). A full card opens the
    class as a Discord Activity: `/dailytrigger` and its nine siblings. `games/registry.js`
    **`DISCORD_COMMAND`** is the key -> slash-command table and `discordCommand(key)` its
    reader; `shell/enrollment.js` prints it as the unlock box's third row
    (`punchcard_unlocked_discord`, `{cmd}` substituted) and `shell.js` appends the same
    sentence to the quiet no-op-mint note. See trap 134 - that table is HALF of a contract.
  - **`init.launchGame`** (string gameKey, or absent) is how a hosted shell says which room
    it was opened for. `shell.js` reads it once beside `devDoor` (`launchRequest`), and
    `maybeLaunchRequested()` fires ONCE per boot, immediately after the boot's `showBoard()`
    - the campus has mounted and the cards have been known since `init.meta` reached the
    store. Complete card -> `launchGraded(key)` (which starts tonight's board class if the
    room is on it, else the free-swim twin); anything else -> stay on the campus and toast
    `launch_card_locked`. **The C# host never sends it** and never should: the desktop opens
    on a campus, so `BuildInit` has nothing to say here. The field is `cclabs-web`'s
    (`scripts/arcademy-web-ext/host/index.js` reads the activity boot blob's `game`).
  - **The SHELL half** (2026-08-23): `core/store.js` lists the key in `HOST_OWNED_KEYS` and
    exposes `store.punchCard(gameKey)` / `store.unlockedGames()`, both of which RE-DERIVE
    `punches`, `house`, `complete` and `enrolled` off the two fields that are actually earned -
    the same self-heal `ArcademyPunchCards.Normalize` does in C#, so a denormalized blob can
    never draw a hole the host does not hold. `shell.js` turns that into `campusState.unlocked`
    (a map, beside `devPass`) and into `needsEnrollment()`; `shell/enrollment.js` runs both
    beats; `boot.js` routes `punchcard-result` to `shell.onPunchCard`. The page's ONLY
    outbound frame about a card is `enrollment-done` - there is deliberately no "punch" verb
    it could send.
- **`art/punchcard/faces.json` is the card-art seam, and it is also the ART
  MANIFEST.** `{ "<gameKey>": { slots:[{x,y,w,h} x10 row-major], crest:{x,y,scale,
  rotation}, text:{x,y,w,h} } }`, every number a fraction of that class's own face
  image (slots and text are BOXES - top-left plus size; the crest's x/y is its
  CENTRE, `scale` its width, `rotation` degrees; an optional `aspect` overrides the
  1.6 default). `shell/punchcard.js` loads it ONCE through `loadFaceGeometry()`
  (`shell.js` calls it beside the engine/provider `loadOptional`s) and sanitizes it
  FIELD BY FIELD, so a junk slot list, an unparseable number or a nine-entry grid
  each fall back on their own rather than taking the card down. **A class listed in
  that file is a class whose face image shipped** - that is the ONLY signal for
  `data-art="on"`, which is what drops the drawn grid boxes, the drawn name band
  and the corner ribbon (all three are baked into a real face). So the json ships
  WITH the pngs, never before them, or a card loses its floor and gains nothing.
  No file at all = every class on `DEFAULT_GEOM` and a finished-looking card.
- **`meta` arrives in TWO shapes** — `{key, value}` (the reply to a meta-command) and
  `{rev, state}` (the snapshot the host pushes after crediting attendance). Handle both; a
  handler that requires `key` silently drops the authoritative streak.
- **Board size** is a per-game setting under a derived key, `<gameKey>_board_size`
  (`shell/settings.js` `boardSizeKey()`), also surfaced to games as `ctx.settings.boardSize`.
- **`init.triggers` is `init.words` WITH THE AUDIO** (2026-08-23, Echo's pads). Shape:
  `[{text: string, audio: string|null}]`, projected top-level beside `words` and passed
  through to games as `ctx.triggers`. Four things are load-bearing:
  - **It is the SAME draw as `words`, in the SAME order.** `BuildInit` shuffles the enabled
    `SubliminalPool` ONCE and hands the list to both, so `triggers[i].text === words[i]`.
    Two `BuildWords()` calls would reshuffle and silently desynchronise a page that reads
    one and indexes the other. `words` itself is unchanged - every other class still reads it.
  - **`audio` is a url on one of two new origins**, resolved by the same rules
    `SubliminalService.FindLinkedAudio` / `KeywordTriggerService.FindLinkedAudio` use
    (case/apostrophe filename variants, then a case-insensitive scan; the active mod wins):
    `ccp.modaudio` -> the mod's `resources/sounds/flashes_audio`, else `ccp.subaudio` ->
    `Resources/sub_audio`. **Both are mapped `Allow`, not `Deny`, and that is deliberate:**
    `shell/audio.js` routes the clip's media element through the WebAudio bus graph, and a
    CORS-tainted stream cannot feed a `MediaElementSource` - it would fall back to raw
    element volume and slip the mixer's mute/level laws.
  - **It is gated TWICE on the whisper mute.** With `SubAudioAudible` false the host writes
    `audio: null` on every row AND the page refuses to fire a clip (`ctx.audioAudible`).
    Neither side alone opens the tap; a phrase whose file cannot be read is a text row,
    never a missing row.
  - **A host that predates the field is fine**: a game falls back to `words` and gets text
    faces with no clips. An empty pool is a contract, not a failure, exactly like `words`.
- **`arcademy-sfx` takes an optional `url` (a CLIP)** alongside `name`. `engine/oneshots.js`
  `audio_trigger` passes `url` / `key` / `maxMs` / `fadeMs` straight through the way it
  already passes `pitch`, and `shell/audio.js` plays the url from an `HTMLAudioElement`
  routed through the requested bus, so mute, master, bus level and ducking all still apply.
  `key` is a VOICE SLOT (a re-fire on the same key cuts the one still playing - Echo keys
  per pad so a fast sequence cannot pile six whispers up), `maxMs` truncates with a fade
  (default 1200), and `CLIP_GAIN` gives a clip the same headroom a recipe gets from its own
  `gain`, so a clip at level L is never louder than an oscillator at level L. The `name` is
  still sent and is the FALLBACK: a host that cannot decode the url plays the recipe rather
  than going silent.
- **`shell/audio.js` accepts an optional `pitch`** on the `arcademy-sfx` detail (0.5-2,
  default 1). It multiplies every frequency in the recipe - oscillator sweep, arpeggio step,
  noise band, stamp thunk - and deliberately NOT the duration, so a pitch ratchet climbs
  instead of speeding up. Anything unusable clamps to 1, so an emitter that never sends the
  field sounds exactly as before.
- **EMI's state is ONE page-owned key in the C# meta store: `emi`.** There is no
  `localStorage` anywhere in this bundle (deliberately - the WebView2 profile is not a place
  to keep player state), so the widget writes through the same `core/store.js` seam `days`
  and `games` use: `store.set('emi', blob)` -> `meta-command {op:'set'}` ->
  `ArcademyMetaStore` -> back in `init.meta` next launch. The key is NOT host-owned and needs
  no C# change: `ArcademyMetaStore.Set` accepts any new top-level key under its 64-key /
  32KB-per-value caps, and this blob is a few hundred bytes.
  ```js
  emi: {
    x: 0.83, y: 0.71,        // TOP-LEFT anchor as a FRACTION of the viewport, so a
                             // resize moves her proportionally and then re-clamps
    hidden: false,           // dismissed to the dock (the x affordance)
    hintShown: true,         // ABSENT until the first x-dismiss spends the hint toast
    w: 150,                  // ABSENT unless setWidth() was called (clamped 110-220).
                             // No key = follow the window: 150px at >= 900px wide,
                             // 116px below. Persisting the auto width would freeze
                             // whichever window she happened to be born in.
    stats: {                 // LIFETIME telemetry. No UI reads it yet; a later
      pets: 0,               // Records Office beat will show the player their own
      petStreaks3: 0,        // numbers back. Counters only, nothing identifying.
      drags: 0, flings: 0,
      hides: 0, dockRestores: 0,
      bubblesSeen: 0,        // say lines that actually landed
      firstSeenAt: null,     // 'yyyy-mm-dd' LOCAL (trap 8: dates on this page are local)
      lastSeenAt: null,
      msVisible: 0           // visible-and-not-docked, rounded to whole seconds
    }
  }
  ```
  **Writes are batched on the END of an interaction, never per pointermove** (600ms debounce;
  a hide/show/destroy flushes immediately, and `pagehide` banks the last stretch of
  `msVisible`). A drag that wrote per frame would post sixty meta-commands a second across
  the bridge.
- **`widget.apparate(getRect, {line, face, onDone})` IS THE WHOLE FIELD TRIP, and it
  takes a GETTER** (W2a, 2026-08-24). Power-off where she stands -> reappear beside the
  fixture -> land the line through the ordinary say path -> power off -> home. It returns
  a cancel function, or **null** when she refused, and she refuses over every live verb
  she has: mid-say, mid-chain, mid-press, mid-drag, dismissed, disabled, or already
  travelling. So a caller needs no guard of its own - `emi/fieldtrips.js` deliberately
  tests none of them twice.
  - **The saved spot is never written.** A trip moves `el.style.left/top` and never
    `fx0`/`fy0`, so "come home" is just `place()`. The ONE exception is the touch cancel
    (`{stay:true}`), which commits the spot she was actually standing on - from the trip's
    own bookkeeping, never from `getBoundingClientRect` (trap 74).
  - **`widget.setPoiRects(fn)`** is the other half: a function answering the live rects of
    every registered fixture. The widget calls it ONCE per drag - never per pointermove -
    and uses it for exactly one thing, the carried `*_*`.
  - **The return is a MOMENT, not a callback into the script.** A completed trip fires
    `fieldTripHome {id, lineKey}` through the ordinary `voiceMoment` path, which is what
    lets story.js own beat `b28_first_trip` and its once-ever flag. A CANCELLED trip fires
    nothing - she did not get home, so there is nothing to be pleased about.
  - **`voice.hasSeen(id)` / `voice.markSeen(id)` / `voice.sessions`** are the three members
    the scheduler borrows, and they exist so there is ONE ledger. A POI id is in the same
    namespace as a beat id; the `trip_` prefix is what keeps them apart.
- **The shell's EMI seams are six one-liners and every one of them is `fireMoment(...)`.**
  `shell.js` mounts once (before the first `showBoard()`, so the opening `greet` has a face to
  wear) and fires at: the board being ARRIVED at (not repainted), `startClass`, the graded
  finish in `finishClass`, the punch-card ceremony, `applySuspend` both ways, and `showReport`
  on a COLD open only. Two suppressions are load-bearing and easy to lose: a
  `showBoard({silent:true})` repaint is not an arrival, and `onPayout` re-rendering the report
  is not one either - without the `wasScreen` guards EMI greets you on every meta echo and
  talks over her own win face.
- **THE OFF CHANNELS' MEDIA SEAM IS THE ONE THE CLASSES ALREADY USE** (W3,
  2026-08-24). `shell.js` now hands `mountEmi` two extra things - `assets` (the
  live `createAssets` handle) and `settings` (`init.settings`) - and NOW WATCHING
  is the only thing in EMI that touches either. Four bright lines, and none of
  them is negotiable:
  - **Remote media goes through the HOST, over the bridge**, exactly as a class's
    does: `emi/takeover.js createMediaBroker` calls `assets.claim({loops:2,
    stills:2})` ONCE a sitting and `provider/remote.js` asks the host. The page
    never talks to a server for media, and the host fetches Scrolller straight
    from the player's own machine (`provider/remote.js` header, GROUND-RULES §8).
  - **The gate is the app's, resolved host-side.**
    `ArcademyHostService.RemoteMediaEnabled()` is literally
    `MediaSource != "local" && HasRemoteMediaConsent`, projected as
    `remoteMediaEnabled` / `remoteConsent` / `mediaSource` on `init.settings` and
    read here through `assets.catalog()`. EMI re-derives nothing.
  - **NO NSFW FILTERING OF REMOTE CONTENT, EVER.** There is no filter in
    `takeover.js` and none may be added.
  - **Consent off is the player's OWN library**, `init.settings.localAssets`
    (trap 16's `{gifs, stills}`), titled with the filename. Neither available and
    `watching.plan()` returns null: the channel is ABSENT from the wheel, never a
    stub and never a black glass (trap 79).
  - `canvasSafe` is deliberately **false** on that claim. The two-pool law exists
    so a consumer that READS pixels never meets a tainted origin; the glass is
    drawn to and never sampled, and the reveal card is a plain `<img>`. A
    canvasSafe claim would have made the flagship channel local-only for every
    consenting player.
- **`fireMoment` now also tells EMI the SCREEN changed hands** (W3). One line in
  `emi/moments.js` calls `emi.noteMoment(name)`, which maps `classStart` /
  `suspend` / `tabAway` to "a class owns the screen" and `win` / `miss` / `fail` /
  `resume` / `reportCard` / `greet` / `dayDone` back to "it does not". It is a leg
  of the off-channel idle gate and nothing else in EMI tracked it. Unknown names
  are ignored, and an EMI with no deck is a no-op.
- **Protocol** (`bridge.PROTOCOL = 1`) must match the host's `PROTOCOL` int. A mismatch
  fails the boot on purpose — a page mis-reading the projection would mis-clamp settings.
- **`ctx.assets.claimTagged()` IS THE SECOND POOL SHAPE, AND IT IS ADDITIVE** (SORT,
  2026-08-23). `claim()` answers "24 loops"; this answers "two piles, and every row
  remembers which pile it came from". Nothing about `claim()` moved - every other class
  draws exactly the media it drew before, and the old `assets-request` frame is
  byte-for-byte what it was (no `tag`, no `subs`).
  ```js
  const pool = await ctx.assets.claimTagged({
    sources: [ { tag:'target', kind:'remote', subs:['BambiSleep','sissyhypno'] },
               { tag:'noise',  kind:'remote', subs:['pokemon'] } ],
    //         or { tag, kind:'local', folders:[...] } / { tag, kind:'local', presetId }
    want: { loops: 48, stills: 32 },  // totals across tags, a HINT
    perSourceMin: 12,                 // resolve when EVERY tag has this many distinct rows
    seed: 'string', timeoutMs: 6000,  // ...or the ask budget is spent, or this elapses
  });
  pool.next(tag, { prefer:'loop'|'still' })  // row {url, remote, kind, mime, tag, src} | null
  pool.counts() / pool.thin(tag) / pool.empty(tag)
  pool.prewarm(n) / pool.dealt() / pool.onUpdate(fn) / pool.dispose()
  ```
  Wire: remote rows go out as `assets-request {reqId, count, kind, subs, tag}`, local rows as
  `local-sample-request {reqId, count, kind, folders?|presetId, tag}`, and BOTH are answered by
  the same `assets {reqId, urls:[{url,kind,mime,tag,src}], done}` mailbox, keyed by reqId
  (`src` = `r/<sub>` | `<folder rel path>` | `preset:<id>`). The host contract is unchanged:
  ask again after every reply, MAX_ASKS **per tag** 8, RETRY_MS 1500, batch cap 24.
- **AN ANIMATED `.webp` IS A LOOP, AND ONLY THE DESKTOP HOST CAN SAY SO** (ccp-bugs#1086). A webp
  animates because of a flag in its VP8X container header, never because of its name, so the page
  cannot tell one from a still and used to class every one of them `still`. That put them outside
  every motion budget on the page - Lost & Found's live window (`liveCap`, board.js §4), its frame
  governor's shed, and the same `/\.gif(\?|#|$)/` test in anomaly / deja-vu / instant-recall - and
  a library of them dealt ONE main-thread decoder per seat instead of ~30. The wall fell to about
  a frame a second, the row drifted under the pointer, and a click on the right tile scored a miss.
  `ArcademyHostService` now header-probes a **sampled** local webp (never a whole library walk) and
  stamps `#.gif` on its `ccp.assets` url (`AnimatedImageHint`), the same fragment-hint lane
  `provider/index.js hintedPileUrl()` uses to give an extension-less `blob:` row a kind. The URL
  Standard drops the fragment before the fetch, so the identical bytes load; `inventory.js`
  `hintExtOf()` lets `kindOf()` read it ahead of the extension, and the rows the host builds carry
  `kind:"loop"` to match. `.webp` therefore sits in **both** `LocalLoopExts` and `LocalStillExts` -
  the probe, not the extension, decides which ask it answers. An **unhinted** webp is still a
  still, which stays the only honest answer on the web port, where no host holds the file.
- **THE DOOR'S OTHER FOUR VERBS** hang off the same object and are how SORT's setup screen
  draws itself: `catalog()` (the sanitized `init.settings` projection - `remoteCatalog`
  `[{id,label,subs}]`, `subLibrary` `[{name,ok,videoCount,stillOnly,selected}]`, `localFolders`
  `[{path,gifs,stills,videos}]`, `assetPresets` `[{id,name}]`, plus `remoteConsent`,
  `remoteMediaEnabled`, `offlineMode`, `mediaSource`), `probeSub(name, {scope, pile})` ->
  `probe-sub {reqId,name,scope?,pile?}` / `sub-probe {reqId,name,ok,videoCount,stillOnly}`,
  `removeLibrarySub(name)` -> `library-remove {name}`, and `onLibrary(cb)` for the host's
  `library {subLibrary:[...]}` push (which any surface can cause - the Assets tab, the FYP
  popover, another probe). `shell.js` passes the six new init fields into `createAssets`
  beside `settings`; a host that predates them ships nothing and the catalog is simply empty.
  **Neither probe nor library is media**, so they ride `remote.js`'s `sendRaw`/`subscribe`
  rather than the reqId mailbox.
- **THE SORT SCOPE: A DECOY PILE IS NOT A TASTE** (2026-08-28, tester report - "the noise
  reddit feeds (cat and pokemon) I added for the sorting room game seem to have followed me
  through to all the other games"). The LIBRARY (subs the player has KEPT) and the FEED
  SELECTION (what the app-wide, untagged `claim()` is answered from) are two lists, and the
  split is the whole point: SORT's door adds to the first and must never add to the second.
  `probeSub` therefore names the surface - `{scope:'sort', pile:'noise'|'target'}`, both
  optional, the frame byte-for-byte the old one without them (the Media counter's own add
  box sends neither).
  - **The C# host honours it by construction and acts on it nowhere.**
    `AppSettings.TryAddLibrarySub` never touches `FypOnlineCustomSubs`, and `RemoteChannels()`
    resolves the app-wide pull from the niches plus that selection alone - so the desktop
    never leaked. `OnProbeSub` reads `scope`/`pile` and logs one line; a second rule there
    would be a second place for the same law to drift.
  - **The web host shim is the one that has to act**, because its library row IS its feed
    selection (`selected` defaults true on a probe add, and its app-wide sub set is "the
    picked niches plus every selected library row" - `scripts/arcademy-web-ext/host/provider.js`
    in the site repo). Until it does, `provider/index.js` **fences the row itself**: on an OK
    `sort`/`noise` verdict that actually grew the library, and ONLY where
    `init.settings.mediaControls === true` (the web-only media counter), it posts one
    `set-setting {key:'media.librarySelect', value:{name, selected:false}}` - the counter's
    own key, so this parks the sub exactly as un-ticking its box would and the player can
    tick it back on there. It fires once, at the moment of the add, because the door never
    re-probes a name the library already holds.
  - **"The noise turns itself off after the class" is NOT built** (owner may want it). It
    would hang off `shell/shell.js finishClass` / SORT's own `end`, reaching back into this
    seam with the pile the class dealt. The fence is the smaller, safer claim: the decoy
    never got enrolled in the first place.
- **`engine/index.js` `createEngine(opts)` / `provider/index.js` `createAssets(opts)`** are
  loaded *optionally* (intake's `loadOptional`). Missing or throwing → null object, and the
  class still runs, silent. Never make either a hard import.
- **CAMPUS PRESENCE is TWO seams, and they are deliberately unequal** (P3, 2026-08-24;
  `planning/arcademy/PRESENCE.md` §3, wire contract `proxy/docs/arcademy-presence-api.md`).
  - **Down the wire to the page: `presence {self, snapshot}`.** `Services/Arcademy/ArcademyPresenceService.cs`
    GETs the public snapshot on campus open and every ~60s ±10s while the window is up, and
    `ArcademyHostService.OnPresenceSnapshot` posts it. **The snapshot rides through UNMODIFIED** -
    its `now` is the SERVER's clock, which is the only reason `shell/ghosts.js` can paint the
    server's ages rather than a skewed machine's. `self` is this account's opaque id (from the last
    POST reply) and is ALWAYS included, because omitting it leaves the page's previous value
    standing; `snapshot` is null on the frame that only carries a newly-learned id. The page reads
    exactly those two fields and nothing else. **The pusher is NOT gated on the share setting** -
    watching is not consenting, so a player at `off` still gets a populated campus.
  - **Up the wire from the HOST: four transitions, and only with consent.** `campus_enter` at
    launch, `room_enter` on `class-started`, `class_end` on `class-ended` (the host's CLAMPED
    grade, so a zen `pass` rides as null), `campus_leave` in `DisposeAll`. All four are gated on
    `AppSettings.ArcademyPresenceShare != "off"` plus an identity plus `!OfflineMode`. **The room
    key is kebab-case and is its OWN table** (`daily-trigger`, not `daily_trigger`, not the punch
    card's snake_case): two features, two vocabularies, no shared table to drift.
  - **`presenceShare` is the ONE consent flag in `SETTING_KEYS`** (`off|anon|username|discord`,
    default `off`). It clamps to an ALLOWLIST at both ends - anything unknown lands on `off`, never
    on the nearest rung, because a consent flag degrades to "no consent". Seven `presence_share_*`
    lexicon rows carry the copy and every option names what it shows PUBLICLY; a rung labelled only
    "Anonymous" is not consent copy.
  - **A downgrade owes one POST, and `off` cannot pay it.** The server's consent row only ever
    moves on a POST, so lowering the rung posts one `campus_leave` carrying the NEW rung - which
    retroactively hides the name and picture on rows already on disk. Turning presence fully OFF
    posts nothing at all: `share` is validated against `anon|username|discord` and there is no
    revoke route, so the only "off" the wire could carry would be this client asserting a consent
    the player just withdrew. The account's prior window ages out inside 24h instead. That gap is
    the server's to close, not the host's to fake.

- **THE MEDIA COUNTER IS A WEB-ONLY SEAM, AND IT HANGS OFF ONE STRICT FLAG** (2026-08-24,
  `MEDIA-CONTRACT.md` v1 in the site repo's `scripts/arcademy-web-ext`). The browser host shim
  owns media on the web the way `ArcademyHostService` owns it in the app, and `shell/settings.js`
  renders its **Media** group ONLY where `init.settings.mediaControls === true`. Strictly true is
  the whole point: the C# host never sets the flag, so on the app it is `undefined`, the group is
  never built, and no `media.*` key can leave the page - which matters because `ApplySetting`
  would bag one under its own name in the per-game scalar bag as junk nothing ever reads. With
  the flag up the read-only "Asset source" row steps aside and the live group replaces it; with
  it absent the settings room renders exactly what it rendered before any of this existed
  (verified against the pre-change tree, digest for digest).
  - **Five keys, all on the ordinary `set-setting` / `setting` echo**: `media.remoteConsent`
    (bool), `media.niches` (the FULL selected array, never a delta), `media.librarySelect`
    (`{name, selected}`), and the two ACTIONS `media.pickLocal` (`'folder'|'zip'|'gallery'`) and
    `media.clearLocal`. An action stores nothing and echoes `value: null`; its RESULT arrives as
    a `local-media` push instead.
  - **The echo carries what is STORED, and here the host says no out loud.** A `media.niches`
    list that sanitizes to empty is REFUSED and the host echoes the list it still holds, so the
    last box a player unticks comes straight back up under them. The group paints a `pending`
    marker from the post until the echo lands and then repaints from the echo, and it carries one
    line (`media_niches_snapback`) for the refusal, because an unexplained snap-back reads as a
    dropped tap. `isGlobalSettingKey()` now answers true for any `media.*` key, which is what
    keeps that refusal echo out of the per-game flat bag on the way through `shell.js onSetting`.
  - **THE GESTURE RULE, and it fails silently.** `media.pickLocal` is posted SYNCHRONOUSLY as the
    FIRST statement of the click handler. A file picker opens only while the browser's transient
    user activation is still standing and the shim's transport hands `postMessage` straight into
    its router, so one `await` in front of that line makes the picker never open, with no error
    anywhere to read.
  - **Sub add/remove are deliberately NOT `media.*` keys.** The group borrows `probeSub` and
    `removeLibrarySub` from `shell.js`'s live `createAssets` handle (now passed into
    `createSettingsPage`), so an add rides SORT's `probe-sub` frame and a remove rides
    `library-remove` rather than a second copy of either. The list repaints ONLY from the host's
    `library` push - never from the provider's optimistic local copy, which is the same law trap 1
    states for a setting.
  - **Two host pushes, both on `bridge.on`** (the same loose seam `provider/remote.js subscribe`
    wraps, and multi-subscriber per trap 11, so listening here never steals the provider's own
    `library` frames): `local-media {images, videos, skipped, active}` after every ingest, every
    clear and once after a cancelled picker, and `local-media-progress {frac, phase}` through a
    zip ingest only. Both write through to the page's view of `init.settings` so a second trip
    through the front office does not repaint yesterday's answer.

- **`engine.deadBeat(beat, opts)` is THE SEEP'S CLASS-SIDE DOOR, and the only one.** A class
  names a DEAD MOMENT it is standing in - `'round_gap'` / `'resume'` / `'resurface'` /
  `'stream'`, the names in `shell/seep.js BEATS` - and the engine asks the director, which
  answers **null** the overwhelming majority of the time. The engine holds the director as an
  INJECTION (`createEngine({ seep: { beat, feed } })`, wired in `shell.js startClass`) and never
  imports `seep.js`; `feed` is `seep.feedTag(gameKey)`, so nobody keeps a second copy of the
  Annex camera map. The handle is `{tell, ms, cancel()}`.
  - `opts.draw(ms) -> undo` lets a game own the pixels where they have to live inside its own
    furniture (The Deep End's backdrop, one Instant Recall tile). With no `draw` the engine
    paints the two tells it owns, on `.ae-layer`.
  - **`opts.onClear` runs on the tell's LAST FRAME**, after the pixels are gone and after the
    claim is released. It is the input re-arm hook, and it is how `pauseClass(false)` proves
    that input comes back only once the Resume Slate has cleared. **A null answer does NOT call
    it** - the caller re-arms itself, synchronously, and the common case pays no latency.
  - One claim per engine; `suspend(true)` and `dispose()` both clear a live one BEFORE
    `timers.kill()`, or the claim is stranded and the director's global cooldown eats it.
  - It is **not an effect**: no channel, no ceiling, no `effectsConsumed` allowlist, no
    `arcademy-fx`, no log line. The school does not report itself.
- **THE CLASS CLOCK GOES BACK INSIDE THE SAME THUNK AS THE GAME.** `pauseClass(false)` packs
  `timeBarSet(true)` *and* `instance.resume()` into one `lift` and hands it to
  `resumeAfterSlate`. Split them and a 120ms Resume Slate costs the player 120ms of graded
  time, which is the one thing no tell may ever do. The pause card is dropped first and
  unconditionally - the door the player pressed must open at once.
- **The Unlisted Frame is no-ledger AT WRITE TIME, not at read time.**
  `montage.seepFrame()` hangs one child inside a tile element and touches no `tile.url`, no
  face, no `seen` set and no counter - so `snapshot()`, `unseen()` and the (never-written) wall
  ledger are all blind to it by construction. If you ever route it through `setUrl` /
  `startSwapTo` "for consistency", the blueprint becomes quiz material and a stop can ask about
  a frame the player was never meant to be tested on. `freeze(true)`, `reshuffle()` and
  `destroy()` all take it down.

- **THE STUDENT ID's `profile` IS ADDITIVE, AND THE PHOTO CHIP IS THE
  `presenceShare` DISCORD RUNG.** `init.profile` = `{name, avatarUrl,
  discordLinked, presenceShare}`; the host pushes `{type:'profile', profile,
  result?:'linked'|'cancelled'|'failed'}` after an OAuth completes, fails or is
  cancelled AND after any `presenceShare` change it applies itself. The page
  posts exactly two things: `{type:'link-discord', thenShare:'discord'}` (ONLY
  while `discordLinked` is false) and the ordinary
  `set-setting {key:'presenceShare'}` once it is true. A host that predates the
  frame is a supported host: the card draws "Student", the drawn stand-in
  portrait and the unlinked chip, nothing throws, and the page asks the network
  for nothing (the rig asserts zero off-origin requests).
  - **ONE SWITCH** (owner ruling): the photo toggle IS the public `discord`
    rung, so photo on means the campus ghost wears it too, and turning it off
    drops to `username` (the name stays). There is no second private flag.
  - **THE SNOWFLAKE RULE HOLDS** (PRESENCE.md §10): the Discord CDN url and the
    Discord user id NEVER reach the page. Desktop caches a 128px still and ships
    it as a `data:` URI inside init (JPEG since the photo-pending fix - WPF's
    PNG encoder INFLATED every real avatar past the size cap, so the card had
    never once worn a photo; the page was only ever promised a `data:` URI and
    `<img>` does not care which codec is inside it); the web build sends the
    first-party proxy url. Anything else is a leak, and `<img>` onerror falls back to the drawn
    portrait rather than retrying.
  - **ONE CLICK IS THE CONSENT** (owner ruling): when a link-up started from the
    chip succeeds, the HOST applies `presenceShare:'discord'` itself and pushes
    `profile {result:'linked'}`. The page never asks twice, and the chip's
    label is what promised it.
  - **THE STUDENT NUMBER IS DERIVED ON THE PAGE**, `core`-free: the presence
    `self` opaque id hashed to `XXXX-XXXX`, or a seed off the enrolment date and
    the name with a tiny `temp` mark until the real one lands.

## 4. Traps (each one cost real time)

1. **Only the echo moves a setting.** Every control posts `set-setting` and paints
   `pending` until the host echoes `setting`. Never write the model on `input`/`change`;
   the host's clamp is the truth. (Tested: host clamps 0.4 → 0.25, the slider lands on 0.25.)
2. **No remote fonts, ever.** The webview is offline. The mockup's Graduate/Sora/IBM Plex
   are gone — `--disp/--body/--mono` are system stacks. Adding a `fonts.googleapis.com`
   link silently falls back and wastes a boot on a DNS timeout.
3. **`.brow` textContent has no spaces.** Split-flap rows render one element per character
   and spaces as empty `.fl.gap` nodes, so `textContent` is `DAILYTRIGGER`. Any test or
   scraper matching `'DAILY TRIGGER'` fails for the wrong reason.
4. **The reveal is CSS-only.** `.board.play` + `--r`/`--i` custom properties drive one
   keyframe; re-flip is remove-class → force reflow (`void root.offsetWidth`) → add-class.
   Without the reflow read the browser coalesces it and nothing animates. Repaints that
   are *not* a reveal must pass `animate: false` or the board re-flaps on every meta echo.
5. **A small pool cannot satisfy no-repeat-3.** (Written for the 4-game pool; the five-game
   pool relaxes on every dealt day too.) 3-4 rotating games into 2 slots means the
   generator relaxes on most days. Relaxation order is law (flagship → meaty → family →
   no-repeat) and no-repeat *narrows* (3→2→1→off) instead of dying, so the board still
   refuses yesterday's class. Every constraint is also a seeded weight so the preference
   survives relaxation. `day.relaxed` and `day.noRepeatWindow` report what happened.
6. **A pool needs FOUR meaty games to deal one meaty class EVERY night.** no-repeat outranks
   meaty. With the five-game pool no-repeat-3 was unsatisfiable and relaxed first, so two meaty
   games (`lost_and_found`, `the_deep_end`) filled the slot nightly "for free". The TEN-game pool
   re-opened it: no-repeat-3 binds again (`noRepeatWindow === 3` every day) and two meaty classes
   cannot cover a 3-day window - measured 13/28 nights with two, 21/28 with three, **28/28 with four**
   (`scratchpad/ttcheck/check.mjs`). So `instant_recall` and `composure` are meaty too (ruled
   2026-08-23); the flag is a timetable fact, nothing in a module branches on it. A fifth meaty game
   changes nothing; dropping to three silently loses a quarter of the nights.
7. **The timetable's history is an epoch walk, not a recursion.** `EPOCH = '2026-08-01'`
   and the generator walks forward to the target date, memoised. That makes it a fixed
   point (day D-1 computed as history === day D-1 computed on its own — tested). **Moving
   EPOCH reshuffles every past day.** Don't.
8. **UTC seeds content, LOCAL date rolls attendance** (regression #978). `init.utcDateSeed`
   → timetable + per-class seeds; `init.localDate` → streak + day rows. Crossing them
   makes the streak timezone-dependent and the daily word non-global.
9. **Peek's A-cap is the shell's, not the game's.** `ctx.peek` is a shell primitive; the
   runner reads `peek.used` at `endClass` and hands `assists.peek` to the rubric. A game
   cannot opt out, and a game that implements its own peek has broken the rule.
10. **The per-class engine handle has no `suspend()`/`dispose()`.** Lifecycle is the
    shell's, so a game cannot un-suspend itself while a mandatory video plays. It is also
    allowlisted to `manifest.effectsConsumed`; an undeclared `fire`/`sustain` no-ops and
    logs **once per kind**.
11. **`bridge.on` is multi-subscriber** (type → Set), unlike `dtrh/bridge.js`'s single-slot
    Map. `core/store.js` wants `meta`, `shell/settings.js` wants `setting`, `provider/`
    wants `assets`. If you "simplify" it back to one handler per type, the last importer
    silently steals the others' frames.
12. **Perfect-attendance credit is guarded by `streak.lastPerfectDate`, not by the day
    row's `perfect` flag** — `completeDay()` sets that flag in the same breath, and the
    first version of this raced itself into never crediting a perfect day.
13. **The share header is the literal string `'The Arcademy'`**, never `t('arcademy')`. A
    mod-skinned header would out the player's mod in a Discord paste. v1 renders **only**
    Daily Trigger's emoji grid; other payloads are ignored with one log per session.
14. **The engine emits its CustomEvents on `document`** (and additionally on `opts.bus` if
    you pass one). The shell passes `bus: null` on purpose — passing `window` double-logs
    every `arcademy-log` line.
15. **A dead lexicon degrades to English, never to raw keys** (`core/lexicon.js` falls
    back caller → defaults → de-snaked key). Same lesson as the app's `en.json` Fatal path.
16. **The local media manifest is called `localAssets`.** `ArcademyHostService.BuildSettingsBag`
    hangs `{gifs:[...], stills:[...]}` of absolute `https://ccp.assets/...` urls off
    `init.settings.localAssets`; `provider/index.js` `MANIFEST_KEYS` lists that name FIRST and
    `shell.js` passes `settings: src.settings` straight through. Rename either end and every
    draw silently falls back to the six bundled placeholder tiles - which looks like art, not
    like a bug. `shell.assetStats().placeholderFloor === true` is the tell.
17. **`bridge.send` takes ONE object.** It drops anything without a string `msg.type`, so
    `send('assets-request', payload)` posts nothing at all and the host is never asked for
    media. `provider/remote.js` flattens to `send({type, ...payload})` for exactly that reason;
    the loose `bridge` shapes in its header are for other hosts, not a signature we may pick.
18. **`shell/audio.js` is the only thing that may hold an audio node.** It listens for
    `arcademy-sfx` on `document`, SYNTHESISES every cue (there are no sfx files in the build)
    and multiplies sfx level x group level x `masterVolume` x `!audioMute`. Three consequences:
    no `AudioContext` is created until the first pointer/key gesture (autoplay policy - a cue
    before that is counted and dropped, never queued); levels move ONLY on the host's `setting`
    echo, same law as the settings page; and a new engine sfx name that is not in its `SOUNDS`
    table degrades to `blip` rather than going silent. `boot.js` builds it before the shell and
    exposes `audioConsumer()` for the harness.
19. **The panic key is projected TOP-LEVEL** (`init.panicKey` / `init.panicKeyEnabled`), not in
    `init.settings`, and it is a LAUNCH-TIME SNAPSHOT - `ProjectedSetting` does not echo it, so
    rebinding the app's panic key mid-class does not move the page's conflict check until the
    next launch. The page only ever refuses to bind over it; it never handles the key.
20. **`manifest.boardSizes.values[0]` is the SHELL's default.** Both `shell/settings.js`
    (`gameValue(..., bs.values[0])`) and `shell.js`'s `gameSettingsFallback` fall back to the
    FIRST entry, so the list has to be ordered with the intended default first - Lost & Found
    ships `[40, 30, 24, 20, 16, 12]`, descending, for exactly that reason. The A-cap then
    hangs off `par`, not off the list: `chosen < par[tier]` is "below par board". Two ways to
    get this wrong: put the easiest size first and every untouched install plays capped, or
    write a `par` value that is not in `values` and par can never be met.
21. **A manifest settings enum needs `values`, not `options`.** `shell/settings.js` tests
    `s.kind === 'enum' && Array.isArray(s.values)`; an `options` array falls through every
    branch and the row simply **never renders** - no warning, no fallback control, and the
    setting silently keeps its default forever. (`selectRow()` takes `options` internally,
    which is where the confusion comes from.)
22. **`fire('glitch_swap').onSwap` rides the engine's timer registry.** The midpoint callback
    is scheduled through the engine's own timers, and `suspend()` kills them (`timers.kill()`)
    while `dispose()` disposes them. A class that does its content swap *only* in `onSwap`
    loses the swap if a mandatory video lands mid-transition. Games must keep their own
    backstop - resolve the swap promise themselves on a deadline and treat `onSwap` as the
    nicety it is.
23. **XP pays ONCE per (game, UTC day); a retake is a free replay.**
    `ArcademyMetaStore.TryClaimXpDay` is the ledger (host-owned key `xpPaidDays`), and a
    repeat `class-ended` answers `payout-result {xp: 0, retake: true}` while still grading,
    stamping and sharing normally. Three consequences: the page must not compute XP from the
    grade (it never could - trap: `results[key].xp` comes only from the payout frame); the
    day's `days[date].classes[key]` row keeps the **first** grade (`shell.js` skips
    `recordClass` on a retake, so a bad second run cannot erase an S); and attendance is
    untouched, because `RecordAttendance` is idempotent per (local day, gameKey) and runs
    either way - which is what still credits a new LOCAL day that shares a UTC day.
    Board rows for a graded class stay CLICKABLE and wear a `t('retake')` chip;
    `classSpec.retake` tells the game. **The punch card obeys the same rule and now has a
    second reason to** (2026-08-23): an S day mints two holes, so a retake that grades S would
    be the farm. It cannot be - `ArcademyPunchCards.Stamp` refuses on the date before it looks
    at the grade, so the FIRST graded finish of a local day is the only one that can stamp or
    double. The mint reports `minted: 0` and the shell draws no ceremony at all.
24. **`ctx.absorb(word)` / `ctx.sessionWords` is SESSION-ONLY.** A class may add to the day's
    word pool and every engine built *after* that gets the longer list (Daily Trigger absorbs
    the word you solved). Nothing is persisted, nothing is posted to the host, and
    `SubliminalPool` is never written - DECISIONS #10, the ramp-never-writes precedent. Reload
    and it is gone. Validated: <= 40 chars, no control characters, no duplicates, 64 adds max.
25. **The timetable memo is keyed on the calendar's DATE KEYS, not its contents.**
    `core/timetable.js` `signature()` hashes the pool plus `Object.keys(calendar)`, so two
    *different* override calendars that name the same date share one memo entry and the second
    silently gets the first one's board. Invisible in the app (one calendar per page load);
    it will eat a test suite that boots repeatedly. `clearTimetableCache()` exists for that.
26. **A `NeutralLexicon` value longer than 96 characters can never be mod-skinned.**
    `MergeModTable` drops any mod string over `Length > 96`, so the long Impulse Control
    rows (the `ic_slip_*` lines) always render English. If a mod must re-voice one, split
    it into two rows rather than raising the cap. (`ic_tube_rules` is no longer rendered -
    the class-rules sheet is drawn - but its C# row stays; the host table is append-only.)

27. **`[hidden]` IS A USER-AGENT RULE, SO ANY AUTHOR `display:` BEATS IT.** `.arc-loader,
    .arc-nope { position:fixed; inset:0; display:flex }` meant `dom.loader.hidden = true` and
    `#arc-nope[hidden]` did *nothing*: two opaque full-page overlays sat over the live shell at
    z-index 70, the later one painting the "The Arcademy is closed" card, and every click on the
    board landed on it. The whole page was unusable and the log said nothing was wrong (playtest
    2026-08-19, shots 01/02/12/13). `styles.css` now opens with `[hidden] { display:none
    !important; }`. **Never write a bare `display:` on a node the shell toggles with the hidden
    attribute** (`#arc-loader`, `#arc-nope`, `#arc-topbar` today) without re-reading that rule, and
    never add a competing `[hidden]` rule in a game or engine stylesheet - the `tt` suite
    (`test-hostfixes.mjs`) parses styles.css and fails if either happens. dtrh/styles.css:400 has
    the same reset for the same reason; that is where the lesson came from.
28. **A suspend can arrive BEFORE the shell exists, and it is a LEVEL, not an edge.** The host
    seeds current native state immediately after `init` (`ArcademyHostService.SeedNativeState`: a
    mandatory video already playing, an `AudioOnlySession` flip that happened between the launch
    gate and the first frame), and `start()` is async - two dynamic imports - so that frame lands
    while `shell` is still null. `boot.js` buffers the LAST such frame and replays it once the
    shell is live (`bufferedSuspend()` is the test seam). Dropping it dealt a board over a running
    video. Buffering the last one and not a queue is deliberate: an on/off pair collapses to "off",
    which is the correct answer for a video that ended during boot.
29. **THE PANIC LADDER IS THE HOST'S, AND THE PAGE NEVER HANDLES THE PANIC KEY.**
    `MainWindow.xaml.cs` hands the press to `ArcademyHostService.HandlePanicPress()` while
    `IsActive` - a rung that must sit BEFORE the app-wide `_panicPressCount >= 2` branch, because
    that branch calls `Application.Current.Shutdown()`: with no rung, two Esc taps inside the
    Arcademy exited the whole app. Press 1 → `Suspend(true, "panic")`; press 2 within 2s →
    `CloseActive()`. A panic suspend has **no natural end** (a video's ends with the video, an
    audio-only one with the session), so the class_suspended treatment grows a Resume button that
    posts `{type:'resume-request', reason:'panic'}` and the HOST answers with `suspend {on:false}`.
    The host refuses that while a video or an audio-only session still owns the screen, and neither
    `OnVideoEnded` nor the audio-only watch may lift a panic suspend. Trap 19 still holds
    separately: `init.panicKey` is a launch-time snapshot the page only uses to refuse a rebind.
    COROLLARY (live-verified 3/3): one physical Esc press reaches BOTH ladders - the host suspends,
    then the page's own tap ladder fires on keyup and used to walk the suspended class to the board,
    destroying the Resume card ~60ms after it appeared. `escapeStep()` therefore consumes the press
    and does NOTHING while `active.suspendEl` is up (any suspend reason): the overlay's Resume /
    Leave class buttons are the page-side way out, the host's press-2 is the fast exit.
30. **`class-started` has a closing bracket now: `class-left`.** Leaving a class with Esc ends no
    class, so `class-ended` was never sent and the host's `_classActive` stayed true for the rest
    of the session - which kept the tighter mid-class heartbeat limit (12s vs 20s) armed and made
    every log line claim the page was still in a class. `shell.js teardownClass()` is the ONE
    funnel every leave path already went through, so the message is sent from there; the host
    handler is idempotent, and a finished class simply sends it right after `class-ended`.
31. **The C# meta blob is bounded and its save is atomic - do not "simplify" either.**
    `ArcademyMetaStore` caps one value at 32KB and the top level at 64 keys, trims `days` to the
    newest 40 rows on every write that touches it (the same `SkipLast` shape `TryClaimXpDay` uses),
    writes through a temp file + `File.Replace` (which leaves one `.bak` generation), and on load
    walks main → `.bak` → empty, SALVAGING an over-cap blob (shed `days`, then the oldest `games`
    entries, then keep the host-owned keys) and copying the original to a `.corrupt` sidecar before
    anything destructive. A bare `WriteAllText` truncates the file first, so a crash in that window
    left a half-written save that the next launch parsed, failed, and replaced with a fresh one:
    the streak, every grade and the XP ledger, gone.
32. **App exit must call `ShutdownFlush()`, never `CloseActive()`.** The graceful close posts
    `end-run` and waits on a 1200ms `DispatcherTimer` for `exit-done` - and inside `App.OnExit`
    that timer can never tick (the dispatcher is already shutting down and OnExit ends in
    TerminateProcess), so the meta flush and the WebView2 disposal it guards never ran.
    `ShutdownFlush` is the synchronous path: flush, dispose, no round trip.

33. **A jackpot's forced garnish KILLED every held spiral wash (engine, fixed 2026-08-22).**
    `ceremonies.jackpot` forces `drain|spiral`, which re-triggers the ONE wash element per kind
    with a hold; the hold's deadline used to write `opacity:0` - and took a class's
    `sustainForever` wheel with it (IC's rung-3 wheel vanished 3.8s after any jackpot; the Deep
    End's was exposed the same way). `engine/sustained.js startWash` now keeps `forever` +
    `heldAlpha` per element: a later NON-forever trigger at a HIGHER alpha is a flare that falls
    back to the held alpha; a LOWER one is the decks' whisper-out step-down and ends the hold.
    `stop('wash')` clears both. Do not "simplify" the three branches.
34. **`Vector3.project(camera)` on a camera that has never rendered projects through the
    identity.** `matrixWorldInverse` is only refreshed by a render or an explicit
    `camera.updateMatrixWorld(true)`; tube3d's THE LANDING solve ran before the first frame,
    got garbage, and silently fell back to 50%/50% (the bubble sat on the near coil again). Call
    `updateMatrixWorld(true)` + `updateProjectionMatrix()` before any build-time projection, and
    sanity-bound the result (15-85%) with an explicit fallback.
35. **"The effects play behind the tube" was never z-order - it was alpha.** Three in-app
    verdicts in a row (owner 2026-08-22). The CDP compositor shot AND a PrintWindow grab both
    showed the fixed `#arc-fx` layer on top; what the eye saw was the engine's heat-gated
    bursts (0.15-0.75 alpha, 120-270px) and `mix-blend-mode:screen` washes drowned by a
    neon WebGL chute, and neon lines bleeding THROUGH a translucent gif read as "behind".
    The engine's ceilings are law, so the fix is GAME-LOCAL and under the effects: the dusk
    (rung-driven opacity on an empty div over the tube) plus THE FLARE (snap to 0.84/0.92 under
    every gif/flash burst for its hold, ease back). Before touching z-index for a "behind"
    report, inject a solid test box into `.ae-front` and PrintWindow the app - if the box is
    on top, it is alpha.

36. **A WEB PAGE'S FRAME BUDGET IS SPENT ON THREE THINGS, AND NONE OF THEM IS "TOO MANY
    NODES."** Chromium trace of The Deep End, full screen, 16 live video tiles, RTX 3060 Ti:
    the GPU process's main thread at **79% of a core**, in three roughly equal thirds.
    - **RENDER SURFACES.** ~86 per frame. `isolation:isolate` + a `mix-blend-mode` pseudo on
      every tile face is two surfaces *per tile*, and a blend surface must read back what is
      under it before it can write. A `filter:` on a `<video>` is worse still: a full GPU pass
      over a decoded 854x480 frame, per tile, per frame. **Tint with plain alpha and bake a
      "desaturate" into the wash gradient; never put a filter on a live decode.**
    - **PER-FRAME RE-RASTER.** `@keyframes { to { background-position: … } }` re-rasters the
      WHOLE layer every frame; six full-screen sheets doing it is a third of the budget on
      gradients that never changed shape. **PATTERNS DRIFT BY TRANSFORM, NEVER BY
      BACKGROUND-POSITION** (the law is written into `the-deep-end/style.js`): oversize the
      sheet by exactly one tile period on its trailing edge and `translate` it by exactly that
      period, so the wrap lands on an identical pixel. One background layer per pseudo - two
      layers with different tile sizes cannot share one transform. Corollary: a *travelling*
      highlight needs a clipping box, and a `::before` cannot own a `::before`, so a sweep on
      a pseudo (the old `g-de-sheen`, `g-de-scan`) either grows a real element or becomes an
      `opacity` breathe. Prefer the breathe; a per-tile node to save raster is a bad trade.
    - **VIDEO DECODES.** Scrolller's SMALLEST rendition IS 854x480, so asking for a smaller
      file is not a lever - the **decoder COUNT** is the only one. Faces are frozen per TIER,
      so a cap counted in tiles is meaningless (17 tier-1 tiles = 1 file, 17 decodes). The
      Deep End caps **distinct animated tiers** (`FACE_CAP` 6) and keeps the numerous shallow
      tiers on stills (`SHALLOW_STILL_MAX_TIER` 3) - *the shallows are still, depth is alive*.
      The ENGINE shares one budget of its own: `engine/util.js` `budgetedKind('loop')` counts
      the `<video>` nodes `mediaEl` has minted and hands `gif_rain` / `gif_burst` a **still**
      once `VIDEO_BUDGET` (6; 2 under `.ae-lite`) is spent. Anything that mints a decoration
      video must come through `mediaEl` and leave through `timers.release`/`kill`, or the
      count leaks and the budget closes for the session.
    **AND A LADDER, NOT A SWITCH:** under a 4x CPU throttle even an all-stills board fell to
    40fps, so the whole frame has to get cheaper, not just the videos. `de_perf`
    (`auto|full|lite`) is the pattern: `lite` = `.g-de-lite` on the game's stage **and
    `.ae-lite` on `document.documentElement`** (the one seam a game and the engine share -
    the engine never owns that class, it only reads it), and **both come off on destroy or
    the lobby inherits a lit-down room**. `auto` samples rAF deltas for ~3s after the board is
    dealt, skips the first 500ms of first-frame cost, and demotes **once, downward only** - a
    room that changes its own look twice is worse than a room that is simply lighter. With no
    `requestAnimationFrame` (node, the DOM double) the probe must **stay full**: a missing
    frame clock is not evidence of a slow machine.

37. **A backtick inside a CSS comment inside a template-literal stylesheet kills the WHOLE
    sheet.** `` /* `[data-tier]` */ `` in a `STYLE_TEXT` template ends the literal early; the page
    dies with `ReferenceError: data is not defined` and `node --check` passes it - only a browser
    load catches it. Three agents hit it in one day (IC stage, DE howto, MD decks). Never write a
    backtick in a CSS comment in a `.js` stylesheet.
38. **`applySuspend(false)` must re-assert the pause.** The shell leaves a lifted suspend behind
    its pause card on purpose (the Resume button is the way back), but a game's `suspend(false)`
    typically restarts its own loop - Misdirection and The Deep End both played on behind the
    overlay. `applySuspend` now calls `instance.pause()` again when `active.paused` is set.
39. **The spiral pool is bundled + THE LOOM.** `shell.js pickSpiralUrl(seed, settings)` appends
    `init.settings.loomSpirals` (the host's `https://ccp.spirals/loom_<slug>.gif` list, same folder
    DTRH exposes) at weight 20 each, cap 24, validated + de-duplicated. No Loom = byte-identical
    picks. The host maps `ccp.spirals` for the Arcademy too (`ArcademyHostService` mappings).
40. **`core/rng.js makeTaggedRoll` is per-tag mulberry32 now.** The first version hashed
    `seed|tag|n` per call and the trailing counter barely avalanched through FNV-1a (~0.4%
    near-equal consecutive pairs); every deck had worked around it with its own per-tag mulberry32.
    Same contract (tags independent, replay exact), different stream - any golden value recorded
    off the old stream (none were) would move.
41. **"WebKit won't transition an unregistered custom property" is a MYTH - measure before you
    rename.** The Deep End web teaser "teleported" tiles on an iPhone 13 Pro Max (2026-08-23) and the
    first diagnosis was exactly that myth; registered `--cp-r/--cp-c` / `--md-x` twins were even
    built and then reverted. The measurement (peer session, Playwright WebKit 26.5 + Chromium): all
    four variants - unregistered var + `transition:transform`, `@property` var, transition on the
    vars themselves, both - interpolate IDENTICALLY in both engines; on the live Deep End page WebKit
    fires `transitionrun` on transform when `--r/--c` change (keyboard and touch) and a no-var
    plain-transform control traced byte-identical. What reads as "the transition never fired" on a
    phone is a 170ms transition receiving 1-3 FRAMES under load - cut per-frame cost (the
    `html.ae-touch` rung: no blur over a live face, no blend surface, no backdrop-filter, video
    budget 3 on coarse pointers) instead of renaming variables. What IS true: `@property` is
    page-global, so a var name shared with mixed types (`--r` is a number here and an ANGLE in
    misdirection/casino.js) can never be registered globally - if registration is ever genuinely
    needed, use game-prefixed names.

42. **A PHONE NEEDS THE CUTS ON *FULL*, AND THE SEAM IS `html.ae-touch`.** The owner's
    iPhone 13 Pro Max skipped frames on slide/merge and skipped harder when effects fired -
    on the LITE rung (web teaser, 2026-08-23). de_perf's full/lite ladder is a QUALITY
    ladder and it cannot fix this, because two of the three costs are HARDWARE ceilings, not
    frame budget: iOS caps concurrent hardware video decode SESSIONS (three or four before
    VideoToolbox thrashes and every stream stutters at once), and WebKit charges several ms
    a frame for a backdrop-filter or a full-screen blend surface that a desktop GPU eats for
    free. So The Deep End probes the device once per class - `matchMedia('(pointer: coarse)')`
    or `navigator.maxTouchPoints > 1` - and puts **`.ae-touch` on `<html>`**, the same
    document-root seam `.ae-lite` uses (the game sets it, the engine only reads it).
    - **GLOBALLY ARMED SINCE 2026-08-25 (perf/arcademy-mobile-web):** `core/device.js`
      arms `.ae-touch` page-wide at paint when `isMobile() && touchProbe()` and stamps
      `data-ae-touch-global="1"` on `<html>` - ONCE ON, NEVER OFF (a rotate can flip
      `isMobile()`, the GPU that needed the ceiling still needs it). The Deep End's own
      lifecycle add/remove SKIPS when the marker is present (its destroy used to hand
      the lobby back its desktop-cost washes); its per-class probe survives only as the
      fallback for big tablets/touch laptops where `isMobile()` is false. The arming
      gates on `isMobile()` too - NOT the bare probe - because desktop WebView2 on a
      touch-screen laptop must never change visuals, and `isMobile()` (coarse PRIMARY
      pointer, no fine pointer anywhere) is structurally false there. A phone also gets
      the engine's lite node budgets (`curves.js` *Lite twins, `ctx.lite()` = coarse or
      motionLevel<=1), the shell's GPU DIET block at the bottom of `styles.css`, and
      per-game `html.ae-touch` blocks (impulse-control's blur diet among them).
    - It is **NOT a third rung**: it applies on FULL too, and `de_perf: full` does not opt
      out of it. There is no setting and there must never be one - the device is the setting.
    - It **composes with the rung in the PROTECTIVE direction, and the two dials point
      opposite ways**: `faceCap` takes the **MIN** (`FACE_CAP` 6 / `_LITE` 3 / `_TOUCH` 4 -
      fewer decoders wins) while `shallowStillMaxTier` takes the **MAX** (3 / 5 / 4 - more
      of the numerous shallow tiers frozen wins). A `min` on the still-line would hand a lite
      PHONE more animated tiers than a lite desktop. `engine/util.js videoBudget()` mins the
      same way: 6 desktop, 3 touch, 2 lite, 2 touch+lite.
    - `engine/style.js` `.ae-touch` drops what WebKit charges most for, on every rung: the
      drain wash's backdrop-filter, `mix-blend-mode` on the four FULL-SCREEN washes (the
      spiral is 150vmax - over twice the viewport of read-back), the scanline's
      `background-position` roll (per-frame re-raster of a full-screen sheet), and the two
      filters that can land over a live decode (`ae-burst-double` on a gif_burst <video>,
      `ae-mosh`'s blur every frame of a swap). The Deep End's own `.ae-touch` block in
      `games/the-deep-end/style.js` does the same on the game side: no blur on `.is-gone` /
      resurface tiles (blur over a live face), the lens loses its blend surface, the glitch
      payload loses backdrop-filter, the merge glyph pops by transform/opacity only.
    - Low Power Mode caps rAF to 30fps and the auto probe demotes to lite on that. That is
      CORRECT, not a bug: iOS Safari caps rAF to 60 even on ProMotion, so the PASS-5
      thresholds (median 20ms / 25ms x 40%) mean the same thing on a phone as on a desktop.
    - Caveat, accepted deliberately: a Windows touchscreen laptop reports
      `maxTouchPoints: 10` with a fine pointer and therefore also gets the ceiling. It is
      hardware-protective and cheap; do not "fix" it by dropping the maxTouchPoints probe,
      which is the only signal a webview that answers no media query has.

43. **`fullBleed` IS THE ONE ADDITIVE ENGINE OPTION INSTANT RECALL ADDED (2026-08-23).**
    `engine/oneshots.js` `fire('flash_burst'|'gif_burst', { fullBleed: true })` forces `count:1`,
    adds the class `ae-burst-cover` (`inset:0`, `object-fit:cover`, no transform, no radius) and
    zeroes `--ae-rot`; the handle answers `fullBleed`. It exists because "fullscreen GIF" is one of
    CCP's OWN named effects and the pool needs it to be visibly different from the corner GIF - which
    needs no engine change at all, because `count:1` plus `x`/`y` (viewport percentages, the layer is
    fixed on `#arc-fx`) is already a placement seam. **Opt-in and count-forcing: a caller that never
    passes the field sees byte-identical behaviour**, which is the bar every engine addition has to
    clear. Alpha still comes from the clamped channel - `fullBleed` changes the SHAPE of a burst,
    never its ceiling, so THE CEILING RULE is untouched.

44. **THE ANSWER CAN STILL BE ON SCREEN WHEN THE CARD IS UP - THAT IS WHAT THE QUENCH IS
    FOR (Instant Recall, 2026-08-23).** `#arc-fx` is `position:fixed; z-index:40` over the
    WHOLE page, and a held effect outlives the freeze: a spiral / pink / drain wash holds
    2.4s, a corner GIF 2s, a bubble field 3.4s (and `clearChannels()` deletes the pulse timer
    that would have stopped it). For the old LAST_EFFECT that gave the answer away some of
    the time; for a SPIRAL or a WALL card it would give it away EVERY time - the thing you are
    being asked to remember is still playing over the slip. So `beginStop()` clears the air
    BEFORE the card: cancel every live burst handle (a ring of 6), `stop('bubble_field')` /
    `stop('gif_rain')`, step the three washes down to alpha 0.01 (**never `stop('wash')` -
    trap 33**), one `fire('audio_trigger', {name:'stop_clips'})` to cut a whisper clip
    mid-word, and blank the game's own `.g-ir-flashwell` so a still-fading sub word cannot sit
    on the slip. **None of it writes a ledger entry** - a silence is not an emission, and the
    truth of what happened is already written. The wall itself is hidden a second way, by
    attribute rather than by effect: `.g-ir-stage[data-shroud="1"]` while a WALL card is up,
    `"0"` at the verdict (the wall IS the proof, with the truth tiles ringed), removed at the
    resume. If you ever add a new held primitive to the pool, it must join the quench in the
    same commit or the class starts handing out answers.
45. **A SPIRAL LOOK-ALIKE IS ANOTHER ASSET, NEVER A CSS VARIANT OF THE ONE YOU SAW.** The
    owner asked for "three generated ones, similar to that one". Five of the seven bundled
    spirals are mirror- and/or rotation-symmetric (sp1/sp7 concentric rings, sp5/sp6 heart
    tunnels, sp3 a kaleidoscope), so `scaleX(-1)` and any `rotate()` produce an option
    IDENTICAL to the truth - input honesty broken, and the card has two right answers.
    `hue-rotate` is a no-op on the two monochrome sets, `invert` on rings is a phase-swap of
    the same animation, and a recolour makes "the canonically-coloured tile" the tell after
    two classes. Recolouring the EMITTED wash is worse still: it is a 150vmax `filter:` over a
    live decode, per frame (trap 36). So `spirals.js` draws a SET of four different spirals
    the engine could equally have shown, kin-first (rings<->rings, hearts<->hearts,
    Loom<->Loom), and the decoys are the other three of that set. The preview is the asset AS
    SHIPPED - square, unspun, uncoloured - because arm shape and palette are what the player
    actually recalls, not the screen-blended turning wash.

46. **AN EXIT THAT SCROLLS AWAY IS NOT AN EXIT.** The settings page is ten classes
    long, the report paper is taller than a short window, and eight of the ten
    class-rules sheets declared `max-height` + `overflow:auto` **and**
    `pointer-events:none` in the same rule - and a box the pointer cannot hit is a box
    the wheel cannot scroll, so on a short window GO sat below a fold that nothing on
    the page could reach and the class simply could not be entered. `shell/exits.js`
    is the answer and `.arc-exitbar` (sticky, bottom:0) is the pattern: it binds to
    whatever scrolls - the document for settings, `.arc-reportstage` for the report,
    an overlay's own box for a card - so no screen has to know what its scroller is.
    Two things that bite:
    - **The sign is `.arc-exitsign.arc-exitsign` on purpose.** Callers sign buttons
      that already wear `.btn.primary` / `.btn.ghost`, and a single class LOSES to two.
      The doubled class buys the specificity with no `!important`.
    - **The bulbs do not move; the light does.** `.arc-sign-lamps` carries the bulb
      mask and a dim fill, and its CHILD `.arc-sign-chase` translates by exactly one
      period (52px = four 13px bulbs). Transforming the masked layer would drag the
      bulbs along with the light, and a pseudo-element cannot own a child - which is
      why the chase is a real node. Never animate its `background-position` (trap 36).
47. **THE PILL AND THE PAUSE CARD MUST NOT BOTH BE UP.** `askLeaveClass()` freezes the
    class to ask, and `pauseClass(true)` mints the Paused overlay with its own Resume /
    Leave class buttons - two stacked cards asking two versions of one question, which
    reads as a bug (it looked like one in the first capture). The pause card steps
    behind the hidden attribute while the confirm is up and comes back if the player
    stays; `dismissConfirm()` carries that undo so all four exits from the dialog
    (cancel, Esc, a host suspend, teardown) restore the same state. The `[hidden]`
    reset at the top of `styles.css` is what makes hiding a `display:flex` node work at
    all - trap 27, used on purpose this time.
48. **THE CONFIRM ADDS EXACTLY ONE ESC RUNG, AT THE TOP.** It is a modal the player
    opened one press ago, so Esc closing it first is the only answer that is not a
    surprise. Everything below it in `escapeStep()` is byte-for-byte the ladder it
    always was, the suspend rung still owns the key while a suspend overlay is up
    (trap 29's corollary), and the pill REFUSES to open while `active.suspendEl` is
    live - that overlay carries its own Leave class and a second door on top of it is
    the exact race trap 29 was written about.

49. **NEVER FEATURE-TEST A DOM COLLECTION WITH `Array.isArray`.** The enrollment
    intro re-labels its own CTA between cards, and the label lives INSIDE the sign
    (`signExit` replaces the button's text with three children: `.arc-sign-lamps`,
    `.arc-sign-arrow`, `.arc-sign-label`), so writing `btn.textContent` deletes the
    whole arrow board and leaves a plain slab. The guard that walked the children to
    find the label span read `Array.isArray(btn.children)` - and the headless DOM
    double hands back a real **Array** while a browser hands back an
    **HTMLCollection**, so the suite went green and Chromium rendered a bare button
    (caught in a capture, 2026-08-23). Walk `node.children` by index; it is iterable
    in both worlds and `Array.isArray` is true in neither that matters.
50. **THE CEREMONY MUST OUTLIVE A REPORT REPAINT, AND `clearScreen()` IS NOT WHERE
    IT DIES.** The punch card mounts ON the report card (which itself sits under the
    Deck V one-more card), and `onPayout` re-renders that report on the same screen
    the instant the host pays out - which is the exact moment the card is meant to be
    up. Dropping it in `clearScreen()` therefore deleted it a frame after it appeared.
    It is dropped beside `dismissEndCard()` at every REAL screen change instead
    (`showBoard` / `showSettings` / `showRecords` / `startClass`) and `showReport()`
    re-seats it last, after the end card - byte-for-byte the treatment the end card
    already had, for byte-for-byte the same reason.
51. **A CEREMONY FOR A HOLE THAT WAS NOT PUNCHED IS THE ONE LIE THIS SCREEN CANNOT
    TELL.** `punchcard-result` is posted on BOTH mint paths even when `minted:false`
    (a same-day retake, a full card, a class the host declined to stamp), so the shell
    can tell "nothing happened" apart from "the host never answered" - and it shows
    NOTHING for the first. The daily path also arms a 4s timeout: a host that never
    answers (an older build, a dropped frame) simply means no card beat this run, and
    the report card behind it is untouched. The ceremony always animates TO
    `card.punches` (already the post-mint total) and `punchTo()` refuses to walk a card
    backwards, so a race between its own schedule and the host's answer can only ever
    add holes.

52. **IN A SORT THE TAG IS THE ONLY TRUTH, SO ONE URL MAY BELONG TO ONE PILE ONLY.**
    `provider/tagged.js` de-duplicates by url ACROSS tags and keeps the row for the tag that
    saw it first. Two remote sources that overlap (a player picking "bimbo" as noise against
    "hypno") really do serve the same file twice, and a card that is both piles at once is the
    one lie a sort cannot survive - the player is marked wrong for being right. The door's job
    is to strip the overlap BEFORE the claim; this is the floor under it.
    Two flags with deliberately different lifetimes hang off the same pool: **`thin(tag)` is
    FROZEN at resolve** (it is what the door warned about, and a warning that changed under the
    player mid-class would be worse than no warning) while **`empty(tag)` is LIVE** (a late
    batch may legitimately lift it, and the refusal to start is checked once, at the door).
53. **A DRY TAG RE-SERVES; `next()` RETURNS null FOR EXACTLY ONE REASON.** When a tag's
    distinct rows are spent it re-serves its OWN served list in a seeded shuffle (repeats are
    fine in a sort - they are what makes a "seen this one" trickster free), so the only null a
    caller can ever get is a tag with ZERO rows. A game that treats null as "out of media" and
    stops dealing has misread the contract; a game that treats it as "this pile is empty" is
    right. `prefer:'loop'|'still'` is a SWAP, not a skip - the wanted kind is pulled forward
    into the cursor's slot and the row it displaced keeps its place, so a preference can never
    starve a kind out of the pass.
124. **A SORT DEALS ONLY OFF VETTED URLS, AND THE VET IS THE ONE DETACHED `<video>` IN THE
    SCHOOL.** Scrolller's index serves posts whose CDN file is GONE (0827 probe: 7/30 r/aww
    loops and 8/30 stills answered 404), the host validates format and never liveness, and a
    dealt dead row is the striped card back the owner was "sick of". `provider/vet.js` probes
    every remote row between the claim and the deal (`pool.vet(rows, {enough, maxMs})`): a
    detached `Image` for a still/gif, a `preload=metadata` `<video>` for a clip - the media
    CDN sends NO CORS headers, so an element is the only status a page can read; `fetch()`
    sees an opaque reply and learns nothing. A dead verdict is a PERMANENT blacklist strike
    (`markBrokenUrl(url, true)` - the 45s TTL is for guesses, a 404 is proof); `unsure`
    (timeout, ABORTED/DECODE codes) is never a conviction and never counts as alive. The door
    (`games/sort/index.js` VET_GATE) opens on ENOUGH alive rows per tag, or every row judged,
    or MAX_MS, and a tag short of `DECK.PER_SOURCE_MIN` alive rows asks the host for more
    first (`pool.refill(tag)`, TAGGED.REFILL_MAX rounds, a fresh MAX_ASKS each). Trap 36 still
    holds: the vet runs ONLY while the door is up (nothing minted), holds at most VIDEO_LANES
    (= DECODER_CEILING) probes, tears each src down on its verdict, and `open()` aborts every
    video probe the instant the gate opens - a class never shares a decoder with the vet. Do
    NOT "optimise" the vet into the warm rail's no-cors fetch (no verdict) or into the class
    (a demuxer the ceiling cannot see). The desktop host now serves SORT the card rendition
    (`Entry.SmallUrl`, clip <= 640 / still <= 1280, the web port's caps) instead of the 1920px
    feed one; the page never reads `smallUrl` - the host substitutes.
54. **THE NEW MEDIA FRAMES ARE ADDITIVE OR THEY ARE A REGRESSION IN NINE OTHER CLASSES.**
    `assets-request` WITHOUT `subs` must stay byte-for-byte the ask it always was (the host's
    app-wide pull), and `local-sample-request` is a SEPARATE type answered by the SAME `assets`
    mailbox. Two things bite here: `provider/remote.js request()` grew five optional fields, so
    a default that is not `undefined` would put a field on every other class's frame; and a
    LOCAL sample is **not gated on remote consent or OfflineMode** (a folder on disk is not a
    network call) while a remote row still is - one flag, `local:true`, on the request.
55. **`setup()` FALSE IS THE ONLY WAY OUT OF A CLASS; EVERYTHING ELSE STARTS IT.** The shell
    awaits `instance.setup()` between `create()` and `beginPlay()` and reads ONE value: a
    resolved `false` walks to campus through the ordinary leave path. True, undefined, a throw
    and a rejection all start the class - a door that broke must never be able to strand a
    player on an empty stage. Consequences a game must handle: the class clock is armed in
    `beginPlay` and nowhere else (so the door is free), the enrollment intro runs FIRST, Esc
    while the door is up is the ordinary leave confirm - which FREEZES the class to ask, so
    **`pause()` and `resume()` can be called before `start()` ever was** - and `destroy()` is
    called on the way out, so a door that mounted anything owns tearing it down.
56. **THE SHELL SUITE'S "229/229" IS NOT A PROPERTY OF THE REPO - IT IS A PROPERTY OF THE
    WORKTREE ITS ABSOLUTE PATHS NAME.** `scratchpad/ir-variety/suite-a/shellsuite` hard-codes
    `C:/wt-ccp-...` in five files (test-e2e's C# read, test-hostfixes' REPO, test-rake's
    WEBROOT, test-timebar's CSS, run.sh's SRC). Re-pointed at `bb22ba34b` (origin/main) it
    scores **215/229**: fourteen assertions were already red there, including the `[hidden]`
    token grep in test-rake / test-hostfixes, which the comment added to `shell/shell.js`
    trips. ALWAYS re-run the baseline from a pristine export (`git archive <sha> ... | tar -x`)
    before attributing a red line to your change, and quote the baseline beside your run.
57. **A REGISTRY ROW WITHOUT A MODULE IS A `class_suspended` ROW ON THE BOARD, NOT AN ABSENCE.**
    `GAME_PATHS.sort` points at `./sort/index.js`; `loadGames` uses `Promise.allSettled`, so a
    missing module is caught, logged and stubbed - the school still deals a board, but the
    player gets a dead room in the rotation. The gate that makes a class ABSENT is
    `RETIRED_GAMES` / a closed semester, and it is one line. So: never land a registry row
    ahead of its module unless the same merge carries the module, and if a class has to ship
    dark, retire it rather than leaving the row to stub.
58. **A ROOM CAN CHANGE HANDS, AND THE LEXICON ROWS DO NOT GO WITH IT.** When Misdirection
    was retired SORT first took its parlour whole; the lot-2 geography rework then razed the
    parlour for the front office and sort built new (room 201 since the 2026-08-24 renumber -
    Misdirection's old plate followed its substitute; echo/IR slid back to 202/203 - on the
    Entrance Hall's donated west span). `ROOMS` has a `sort` entry and no `misdirection` one - un-retiring that class
    now means giving it a room. Its `campus_room_misdirection` / `campus_desc_misdirection` / `game_misdirection`
    rows deliberately STAY: the host's `NeutralLexicon` is append-only and a retired class is
    not a deleted one. (The scratch campus suite asserted "misdirection has a room now" as of
    Semesters II/III - that line needs re-baselining onto `sort`, the LAW it protects, "a pool
    game with no room is skipped, never a throw", is the line above it and still passes.)
59. **A MASCOT OVER A PRECISION BOARD IS THE INPUT-TRUST LAW'S HARDEST CASE, AND THE
    ANSWER IS `#arc-fx`'s.** `#arc-emi` is `position:fixed; inset:0` over the WHOLE
    viewport, so it would eat every board click if it took pointer events - the exact
    failure trap 27 cost a whole playtest for. The layer is `pointer-events:none` and
    exactly two nodes turn it back on: `.emi` and `.emi-dock`. Consequences to keep: a
    dismissed EMI leaves nothing behind but a 28px button; `preventDefault` is called in
    ONE place (the `pointerdown` on `.emi`, to kill the browser's native image-drag ghost)
    and never on a document/window listener; and EMI binds NO key listener at all - she
    adds no rung to the Esc ladder (trap 29's corollary owns that key). The browser pass
    asserts it directly: EMI parked over a board row, a click 60px away on the same row
    still opens the class.
60. **THE FACE IS A CANVAS, SO THE WIDGET MUST SURVIVE A PLATFORM WITH NO 2D CONTEXT.**
    The node DOM double has no `getContext`, `matchMedia`, `getBoundingClientRect` or
    `classList.toggle`. `emi/widget.js` guards every one of them and runs FACELESS rather
    than throwing - and `emi/index.js` loads `face.js` / `chains.js` / `fx.js` with a
    DYNAMIC import inside a try/catch (`loadOptional`'s discipline), so the node suites
    never evaluate a canvas module at all. In practice the shell never gets that far: the
    DOM double registers no element ids, `document.getElementById('arc-emi')` answers null
    and `mountEmi` returns null. Verified - the full node run is assertion-for-assertion
    identical with and without EMI. If you ever make the renderer a STATIC import of
    `shell.js`, one browser-only line in the renderer becomes a boot failure for the whole
    school.
61. **THE BUBBLE HANGS UP OFF HER RIGHT EAR AND THE VIEWPORT IS NOT INFINITE.** `emi.css`
    anchors `.emi-bubble` at `left:58%; bottom:96%` with `width:max-content; max-width:104px`:
    off the ear and RISING, so a long line grows into the sky instead of down across the
    glass. Two half-bugs live in that one line of geometry: an earlier `right:-5%` anchor
    laid the whole box straight over her face, and a left-edge anchor WITHOUT
    `width:max-content` shrink-wraps an abs-pos box against the ~42% of containing block
    that remains right of the anchor, wrapping every bark three characters per row.
    The viewport flips: the natural resting place for a dragged mascot is the bottom-right
    corner, where the line would be cut off by the window - `widget.js` adds `.bubble-left`
    when `left + w * 1.55 > viewportWidth` (and there is room on the other side), and
    `.bubble-low` when she is parked within 96px of the TOP edge (a rising bubble has no
    sky there - it drops below the chin, tail up). `widget.css` mirrors box + tail for all
    four corners. Clamping her further from the edges instead was the wrong fix: it would
    have made the corners - the places players actually park her - unreachable.

62. **THE BUBBLE'S FONT IS A PIXEL GRID, SO ITS BOX CANNOT BE A PERCENTAGE.** Press
    Start 2P is an 8x8 cell face: set it at a whole pixel size or the cells land
    between device pixels and the whole point of it is gone. `.emi-bubble` is therefore
    `font-size:8px` with `max-width:104px` in ABSOLUTE px - not the `clamp(...cqw...)`
    it started as, and not a % of `.emi`, because EMI herself is 150px or 116px
    depending on the window and a % box would wrap every line to four rows on the
    small one. The canvas face is NOT this font: `face.js` measures ink boxes and needs
    Noto Sans Mono plus the exotic kaomoji fallbacks, which a 96-glyph latin subset
    does not have. Two faces, two jobs, both local (trap 2).
63. **HER DEFAULT WIDTH FOLLOWS THE WINDOW, WHICH IS WHY `w` IS USUALLY ABSENT FROM THE
    BLOB.** `DIALS.W_DEFAULT` 150 at >= `W_NARROW_VW` 900px of viewport, `W_NARROW` 116
    below, re-derived on every resize. The blob only carries `w` once `setWidth()` has
    made it the player's choice (there is no resize UI yet). Writing the auto width on
    every save - which is the obvious thing to do - would out-vote the viewport rule for
    ever after the first drag, and the mascot would stay laptop-sized on a 4K screen.
64. **THE MEATY CAP AND THE MEATY REQUIREMENT ARE TWO RULES, AND ONLY ONE OF THEM MAY
    YIELD.** `core/timetable.js` used to express "one meaty class per day" as a single
    predicate, which is fine at three classes a day because the relaxation ladder never has to
    reach it. At FOUR it does - the last seat regularly has no candidate left - and relaxing
    `meaty` dropped the CAP along with the requirement, so the shipped nine-class pool started
    dealing boards with TWO 300-second classes on them. They are now `meatyCapOk` (applied to
    the candidate list, OUTSIDE the ladder, never yields) and `meatyOk` (inside the ladder,
    yields under the name `meaty` exactly as before). Measured after the split: a meaty class
    on 120/120 nights, and never two. If you widen the board again, check what else is riding
    inside a relaxable filter.
65. **FOUR SLOTS COST THE NO-REPEAT WINDOW, AND THE POOL COMPOSITION IS WHAT PAYS.** With
    `CLASSES_PER_DAY = 4` the homeroom is fixed and EIGHT rotating classes have to cover THREE
    slots a night, so a strict 3-day no-repeat window would need nine distinct games and has
    eight: window 3 is arithmetically unsatisfiable and the ladder narrows to 1 on every night
    (reported honestly in `relaxed` / `noRepeatWindow`). The promise players feel - "not the
    class I just played" - still holds: measured ZERO back-to-back over 120 nights. What DID
    change is the spread. With the one-meaty cap, every board is 1 meaty + 3 quick, and the
    rotating pool is four meaty and four quick, so each quick class deals 3 nights in 4 (60/360)
    and each meaty 1 night in 4 (30/360), with a duplicate family on 30/120 nights. Demoting ONE
    class back to quick flattens it to 40-50 and 18-20 - an owner call, not a code fix.

66. **THE LOADER IS THE INTRO SPLASH, AND ONLY THE HAPPY PATH GETS THE BEAT.** `#arc-loader`
    plays a ~3s fixed CSS timeline from t0 (no init needed) and boot.js's `dismissLoader()`
    WAITS OUT `INTRO_MIN_MS` before adding `.is-done` (the fade + zoom-through exit), so an
    early boot never cuts the beat. `failBoot()` must keep snapping `hidden = true` directly -
    an error card delayed by a celebration, or a splash replaying its exit over the nope
    screen, are both wrong. The contract between boot.js and the div is ONLY `hidden` +
    `.is-done`; the decoration inside is free to change. The first screen underneath is the
    ambient campus built with `animate:false`, which is what makes the extended cover safe -
    if a future first screen gains a one-shot entry reveal, it must not fire until the
    loader's `hidden` lands (or the splash will eat it).

67. **shell/ceremonies.js streakMeter must NEVER delegate `streak_meter`, and the CSS
    floor is no longer silent (W0, 2026-08-24).** The engine's ceremony reads `{streak}`
    (this module used to send `{filled, total}`, which parsed as streak 0 - the chime
    ladder NEVER played) and it mounts its OWN meter node in the fx layer, so a "fixed"
    delegate would double-render the meter. The shell draws the one meter and requests
    the chime itself (level + a semitone per lit segment, capped +7, hidden under 2).
    Same wave: every floor beat (stamp / gradeObject / payoff jackpot / near-miss) now
    dispatches `arcademy-sfx` directly when the engine cannot take it - punchcard
    thud()'s precedent; a REQUEST on `document` is not an audio node. gradeObject is
    rank-pitched on the punch-card ladder (C .78 / B .92 / A 1 / S 1.18). If you re-add
    a delegate or "simplify" the quiet flag, the end card goes mute or double-cues.
68. **The pause card is the ONE in-class door to settings, and the scoped page's knobs
    land NEXT run.** The topbar gear is hidden while a class is up, so
    `showSettings(active.cls.gameKey)` from the pause card is the only class-stage
    entry; it renders tiers 1+2 plus that game's group only (an unknown key falls back
    to the FULL page - too many knobs is the lesser bug than hidden ones). ctx.settings
    is a startClass snapshot, so the scoped page prints `applies_next_class` instead of
    pretending to live-apply. The campus gear / Front Office stay argless = full sheet.

69. **Deck gates split two ways since W2 (2026-08-24): `armed()` = visuals and keeps
    capsOk; `sounds()` = cues and NEVER tests capsOk.** bgIntensity 0 is the player's
    VISUAL exit (Law VI), not a mute switch - a game-called beat (the bell, a dim-out,
    a rung climb, a stat correction) still sounds with the lights off, while a deck's
    self-dealt visual cards stay dark AND silent (nothing drawn = nothing to hear; that
    stub staying dead is deliberate, not a gap). Every deck takes the game's own clamped
    helper as `opts.cue` (a closure, NEVER the engine), and every game clamps to
    AUDIO_CEIL [.45,.6,.75,.9] (IR deliberately lower). THE CHROME VOCABULARY is
    uniform across all nine classes: start press = `lift` .5, rules-sheet turn =
    `slide` .35 (a one-page sheet's GO is the START press, never both cues), debrief =
    ONE `slide` unless the rows REALLY stagger (then a `blip` ladder on the same
    timers), refused input = `bump` .3 throttled 250ms. Hover sound exists in exactly
    ONE place in the school - the Lost & Found board (`tell` .12, 150ms throttle,
    hunt-phase only) - and that is an owner ruling, not an oversight to fix elsewhere.

70. **EMI'S VOICE HANGS OFF `setBubble()`, AND THAT IS THE WHOLE SAFETY ARGUMENT.**
    `emi/vox.js` babbles for as long as a landed line is up, which means something has
    to guarantee it never outlives the bubble - a dismiss, a drag, a `setEnabled(false)`,
    a replacement line and a `destroy()` must all cut her instantly. There is exactly
    one place in `widget.js` that already knows the difference between typing (`.`/`..`/
    `...`), a landed line and a cleared bubble, and EVERY cancel path in the file already
    funnels through its `null` branch: `setBubble()`. So the voice is three one-liners
    there (`tick` / `speak` / `stop`) and nothing anywhere else. Do NOT "improve" this by
    calling `vox.speak()` from `play()`, from `voice.js` or from a moment - the moment a
    second call site exists, one of the ten cancel paths stops cutting her and EMI keeps
    talking over a screen she is no longer on. Two consequences worth knowing:
    - the **pop branch speaks on a `queueMicrotask`**, because `playChain` hands the
      bubble over BEFORE it hands the frame to `draw` - which is what resolves the pose
      for a `makeSay` line. Same tick, same frame; it just reads `bodyFrame` (the mood)
      after it exists. `chains.js` is owner-locked and stays untouched.
    - **`stop()` is not a fade**, it clears the pending timers; the worst tail is the one
      blip already in flight (<=56ms). That is the correct trade for an instant dismiss.
    A cold boot is SILENT and that is also correct: `shell/audio.js` creates no context
    before the first gesture, so the opening greet's babble is dropped. Never queue it
    for retro-play - a mascot who talks over a beat that already passed is worse than
    one who missed it.

71. **THE `.emi` ROOT'S INLINE TRANSFORM BELONGS TO THE DANGLE.** While EMI is carried,
    widget.js writes `rotate(...)` straight onto the root's style (the carry tilt); the
    body-move keyframes (`bounce`/`thud`/`shiver`...) still win whenever they run because
    CSS animations out-rank inline styles - which is the whole reason the dangle is legal.
    Do NOT position EMI with a transform, and do NOT convert a body move to a transition:
    a transition on `transform` would composite WITH the inline rotate instead of
    replacing it, and the release spring (`clearDangle`) already owns the only transition
    the root ever wears. The face's gaze lean is the same trick one level down: a CSS
    translate on `.emi-screen` (the canvas ELEMENT), never a canvas repaint - a glyph
    repaint per pointermove is the exact cost the drag-face dedupe exists to avoid.

72. **A SCRIPTED SAY BEHIND A `lead` LANDS ON A TIMER, NOT ON THE CALL.** voice.js
    schedules the bubble `chainMs(lead)` after the moment (~1200ms per unknown chain, and
    a `lead: [a,b,c]` array is the SUM), so a synchronous suite that fires a beat and
    reads `emi.say`'s log immediately sees NOTHING and reports a phantom regression. The
    moment call still returns `true` the instant the beat is consumed - assert on that,
    then await the lead before asserting the line. The perception suite
    (`test-perception.mjs`, session scratchpad) does exactly this for p06/b25.

73. **A FIXTURE RECT CAPTURED AT SCHEDULE TIME IS A RECT THAT HAS MOVED, SO
    `apparate` TAKES A GETTER AND NOT A RECT.** A field trip is offered on an idle
    edge and lands two power-offs later, and in between the window can resize, the
    campus can repaint, and the SVG plan re-solves everything on it (`campus.plan`
    is `preserveAspectRatio="xMidYMid slice"`, so viewBox units do not map linearly
    to the viewport and a 40px window change moves every fixture). `apparate`
    therefore resolves the rect INSIDE the dark, one frame before she lands, and the
    registry's `anchor` is a function returning a function for exactly that reason.
    Two things fall out and both are deliberate: a fixture that has GONE by fire time
    sends her straight home instead of parking her at (0,0), and a resize mid-trip
    cancels it outright (the anchor she is standing at was solved against a viewport
    that no longer exists). Passing a bare rect still works and is the bug this trap
    is named for.
74. **THE CRT KEYFRAME FILLS `forwards`, SO TAKING THE CLASS OFF IS NOT OPTIONAL -
    AND A RECT READ MID-SQUISH IS A 1PX LINE.** `emi-crt-off`/`emi-crt-on` animate
    `transform` on the `.emi` root, which is legal precisely because a CSS animation
    out-ranks the dangle's inline `rotate` while it runs (trap 71). But `forwards`
    means a class nobody removes WELDS `scaleY(1)` onto the root and the dangle
    silently stops working for ever after - the same lesson `droop` taught `BODY_MS`.
    Every exit from a trip goes through `crtClear()` for that reason. The twin half:
    `getBoundingClientRect` reports the TRANSFORMED box, so a position read while the
    squish is running is a 1px-tall line at the wrong top. The trip therefore keeps
    its own `{left, top}` and commits THAT, and nothing in the ladder ever measures
    her.
75. **TOUCH ALWAYS WINS, AND "WINS" MEANS SHE STAYS WHERE SHE IS.** A pointerdown on
    EMI at any point of a trip ends it on the spot - `onDown` cancels before it does
    anything else, so the press carries straight on into an ordinary drag with no
    stranded animation class, no protected say still holding the glass, and no
    teleport. The cancel COMMITS the spot she was standing on, which is the half that
    is easy to miss: leaving the trip's pixel position on the element without writing
    the fractions means the next resize (or the next launch) snaps her back to where
    the trip started, several seconds after the player put her somewhere else. The
    other cancels - a dismiss, a disable, a destroy, a resize, the caller's own -
    bring her HOME instead, because none of them is the player choosing a spot.

76. **THE OPENING IS FURNITURE, NOT A GATE, AND THAT IS AN INVARIANT WITH FIVE
    CONSEQUENCES (FIRST BELL, 2026-08-24).** `vn/` is the first thing this bundle has
    ever put between the splash and a live campus, so every one of its four seams is
    built to be a NO-OP by default rather than a step that has to succeed.
    - **Every entry point takes a continuation and runs it exactly once.**
      `settler()` in `vn/index.js` is the ONE funnel - success, a throw, a missing plate,
      a spent flag and a watchdog all land there and all close the layer and call back.
      If you add a fifth seam, hand it to `settler()` too; do not grow a second exit.
    - **The plates are checked BEFORE anything mounts.** A missing png must never cost
      the player a black rectangle with a caption on it, so `ensurePlate()` gates the
      mount and the four plates are warmed at construction while the campus paints. A
      scene that stands down leaves its ledger entry ARMED for the next eligible night.
    - **`base` is DOCUMENT-relative, not module-relative.** An `Image` src and an inline
      `background-image` both resolve against `index.html`, so the default is `./` and
      only `vn/demo.html` passes `../`. A module-relative `../` asks the host for
      `https://ccp.game/art/...` and silently gets nothing - which looks exactly like a
      missing plate.
    - **s03 spends its flag BEFORE it calls back**, because the callback re-enters
      `startClass` and a flag written afterwards would gate the same class twice. Same
      shape as m01: `afterCeremony()` spends `m01` and then decides whether to draw, so a
      ceremony that cleared onto a live class still burns the beat instead of queueing it.
    - **Escape is never touched.** boot.js owns the key at the window and the shipped
      hold-Esc exit has to work on every VN frame, so `vn/index.js` reads only Enter and
      calls `preventDefault` nowhere. The board dealt into the wall is decoration too -
      no `onSelect`, `aria-hidden`, `tabIndex -1` - because the row the player actually
      clicks is the campus's, one layer down.
    Persistence is `vnSeen`, a plain page-owned key beside EMI's `emiVoice` (no C# change
    needed; `ArcademyMetaStore.Set` takes any new top-level key). First-run only is
    enforced at construction: `shell.js isFirstNight()` reads "no card enrolled and no
    graded day", and a false answer BANKS all four flags so an upgrading player never
    meets a frame of it.

77. **THE OFF CHANNELS ARE A SECOND CANVAS OVER THE FACE, AND THAT IS THE WHOLE
    SAFETY ARGUMENT (W3, 2026-08-24).** `face.js` is owner-locked, so a wave that
    turned EMI's glass into a pong board could not touch it - and does not.
    `.emi-glass` is a SECOND canvas carrying the byte-for-byte rect `.emi-screen`
    carries (`left:34.46% top:29.46% w:41.68% h:37.63%`, `152x137` internal, from
    face.js's own `res` 152 / `h = w x 0.903`), laid over it at `z-index:1` and
    `hidden` at rest. Three things fall out of that and every one of them is load
    bearing: **the face keeps painting underneath the whole time** (proved: 4950
    lit face pixels with a channel live), **killing a channel is hiding one node**
    rather than restoring a renderer's state, and **a broken deck costs EMI her
    channels and nothing else** - `takeover.js` is a dynamic import in a catch,
    the `loadOptional` discipline again. If you ever move `.emi-screen`'s rect,
    move `.emi-glass` in the same commit or the channels drift off her bezel; and
    never, ever paint a channel by calling into the face renderer.
78. **A SAY OUTRANKS THE GLASS, AND THE HOOK IS `cancelChain()` - ONE PLACE.**
    Trap 70's lesson, one wave later: `widget.js`'s `cancelChain()` is already the
    funnel EVERY path that takes her face goes through (a chain, a say, a drag, a
    hide, a `setEnabled(false)`, `destroy()`), so the off-channel preempt is ONE
    line there and nothing anywhere else. Add a second cancel site and one of
    those ten paths stops killing the glass, which is a channel painting over a
    bubble she is speaking through. The rest of the cancel law lives in the deck:
    any pointer or key ANYWHERE cancels instantly (on EMI it is the channel's
    caught beat, elsewhere it is a silent blip-off with no line), a class or a
    suspend refuses a takeover outright (`noteMoment`), and `document.hidden`
    kills one mid-flight. **Zero cost at rest is part of the same law**: one
    repeating wheel timer, five passive document listeners, and NO rAF, no fetch
    and no media element while nothing is up (proved in Chromium: zero
    `requestAnimationFrame` calls in 1.2s of rest).
79. **`plan()` REFUSES, IT NEVER STUBS - AND `prepare()` IS THE ONLY I/O IN THE
    WAVE.** A channel that cannot fully play tonight is ABSENT from the wheel: no
    tape on record and RERUNS does not exist, no library and no consent and NOW
    WATCHING does not exist, reduced motion and PONG / code rain / THE WRONG
    CHANNEL do not exist. Nothing in `channels.js` ever paints an apology, a
    placeholder or an error face, because a mascot that shows you a broken channel
    has told you something about the code instead of about herself. `plan()` also
    does NO I/O - it asks the broker one synchronous question (`ready()`) - and the
    fetch is the optional `prepare()`, which the deck races against
    `FETCH_BUDGET_MS` (2000): a slow answer **skips the takeover entirely** rather
    than opening on a black glass. The wheel's weights are meaningless without
    this: `rollChannel` weights only what actually planned.
80. **THE OFF CHANNELS BIND EMI'S FIRST KEY LISTENER, AND IT IS A PASSIVE READ.**
    Trap 59 says EMI binds no key listener at all, and that rule was about the ESC
    LADDER: she may add no rung to it. "Any input cancels" cannot mean "any input
    except the keyboard", so `takeover.js` listens for `keydown` on `document`
    with `{passive:true}`, in the BUBBLE phase, and does exactly two things -
    stamps `lastInput` and kills a live channel. It never calls `preventDefault`
    or `stopPropagation`, never inspects `ev.key`, and is removed on `destroy()`.
    The Esc ladder is byte-for-byte the ladder it was (boot.js's outer rungs,
    shell.js's inner ones, the host's panic rung above both). If you ever need to
    branch on a key here, do not: put it in the ladder that already owns keys.

81. **CLASS LENGTH IS A RULED QUANTITY, AND MEATY IS NOT THE LENGTH FLAG (2026-08-24).**
    The owner ruled a per-class length table (L&F/anomaly/composure/deja-vu/deep-end 300s,
    sort/instant-recall 180s, echo 120s, daily-trigger and impulse-control deliberately short;
    daily-trigger is also `clockless` - no seconds chip, no time bar, the flag rides the
    descriptor through normalizePool/classFrom and the registry parachute). Do NOT "fix" a
    long non-meaty class by flipping `meaty: true`: `meatyOk` deals EXACTLY ONE meaty class
    per day, so the flag is the anchor-slot marker and flipping it for length would cap long
    classes at one per night and starve the quick slots. Both clamp ceilings are 300
    (`QUICK_MAX_SEC` == `MEATY_MAX_SEC`); the per-module `timeBudgetSec` is the real number,
    and the registry row must mirror it byte-for-byte (the parachute law). Consequence the
    meaty cap no longer covers: a night can legally deal two or three 300s classes (~16.5 min
    worst of 28 simulated) - a total-minutes ceiling would be a NEW timetable rule.

82. **IN A MULTI-BOARD CLASS THE BELL IS THE ONLY END, AND THE LOOP NEEDS A LATCH.**
    Composure and deja-vu clear-and-re-deal until the bell: `onSolved()`/`win()` BANK and
    deal the next seeded board (`classSeed|bN` - board 1 must stay byte-identical to the
    single-board era), they never call finish. Exactly-one-endClass survives only because a
    `closing`/`belled` latch is set FIRST by every terminal path and checked by every
    callback that could re-enter (tap, settle, mutate, deal, howto, trickster). When you add
    any async beat (celebration, deal cascade), assume the bell WILL fire inside it and test
    that seam. Class-level things stay class-level: deja-vu's casino lights ONCE (`start()`
    re-rolls its identity - calling it per board re-skins the room), ambience retunes rather
    than restarts, and composure's pressure deck needs its `deal()` hook or the effects
    ladder freezes at RUNG_MAX after the first bank.

83. **EVERYTHING SIZED AS A COUNT WAS CALIBRATED TO THE OLD BUDGET - STRETCH THE CLASS AND
    EVERY COUNT MUST BECOME A RATE.** The 0824 length wave found the same bug in five
    shapes: anomaly's round plan capped at 24 and looped its seeded deal (now sized off a
    fast-player 2.4s round, COUNT_MAX 140); sort's trickster window covered cards 8..60
    whatever the budget (now a span of expectedCards); L&F's S-gate allowed 1 misclick
    against 26 finds (mathematically unreachable - now a rate per find, and streak/peek/par
    gates scale too, with run-length statistics rather than linear ratios where the quantity
    is an unbroken run); sort's deck dealt 1.6 passes at 120s and would have dealt 2.5+
    (deck +50%, wall cap 120); composure's wash count was a flat count (now scaled to
    budget with a 34% burial cap). When a budget moves, grep the game for every literal that
    was tuned against the old seconds and ask "count or rate?" - the count answers are bugs.

84. **`node --check` PARSES style.js AS A SCRIPT, SO A STRAY BACKTICK INSIDE A TEMPLATE-
    LITERAL STYLESHEET SAILS THROUGH.** The composure rebuild hit it: the file still parses
    (the backtick just re-opens a template) and only an ESM `import()` smoke run finds the
    break. Any edit to a `STYLE_TEXT`-style sheet must be smoke-imported, not just checked.

85. **THE CLASS-RULES SHEET IS FREE OF THE CLOCK, AND IT AUTO-SKIPS ONCE SEEN (owner ruling
    2026-08-24, uniform across every open class).** Two halves, and both are per-game code -
    the shell owns neither. (a) THE GATE: the sheet SHOWS the first time a player meets that
    class at that grade tier and AUTO-SKIPS every later class at that tier, off the game's own
    `gameMeta.howtoTiers` set. `ctx.hideTutorial` ("Skip class tutorials") no longer *narrows*
    a show-every-class default - it now means "skip even the first showing", so every gate reads
    `hideTutorial === true || seen.indexOf(tier) >= 0` where it used to read `&&`. The tier is
    recorded on the GO press and NOWHERE else, so a class left before GO still explains itself
    next time; no meta at all = an empty list = the sheet shows, which is the fallback we want.
    (b) THE CLOCK: every class arms its clock inside the GO callback, never above it - echo was
    the offender (`startClock()` sat beside `bindInput()/claimAssets()`, so a 120s class charged
    the player for reading, and a slow reader could hear the bell over the sheet). What may stay
    on the clock is a GAME beat after GO: instant-recall's and echo's BRIEF line, deja-vu's
    per-board deal+preview. What is deliberately OUTSIDE it: sort's setup DOOR (§5) and the
    sheet itself. Impulse Control has no wall clock at all (the plan is the length), so its sheet
    is free either way. THE SKIP PATH MUST EQUAL AN INSTANT DISMISS: whatever the GO callback
    ran, the auto-skip runs - which is why every game routes both through the same `onDone`.

86. **THE SAMPLE DOOR IS HONEST ONLY BECAUSE THE HOST SAYS WHAT EXISTS (`init.sfxSamples`).**
    `shell/audio.js` maps cue names to `./assets/sfx/<name>.mp3` but plays a sample ONLY when
    the host listed its bare name in `init.sfxSamples` (the C# side scans the folder once,
    `ArcademyHostService.BuildSfxSamples`). A media element reports a missing file
    asynchronously, so probing from the page would either lie to `hasSample()` or eat the
    first cue of every sampled name. Consequences: dropping a new mp3 into `assets/sfx/`
    IS the wiring (add the name to `SAMPLES` if it is new); a browser/test shim that wants
    samples audible MUST list the names in its fake init; `intro_bed`/`flap_deal` are
    SAMPLE-ONLY (no synth recipe - absent file = silence, by design); every other sampled
    name falls back to its recipe. `autoplayOk` in init (also new) arms the mixer with no
    gesture - the host passes `--autoplay-policy=no-user-gesture-required`, a plain browser
    without the flag stays gesture-gated.

    **THE ONE-SHOTS ARE PRE-DECODED NOW (2026-08-26, owner report "late and too loud").** A
    sample used to mint a fresh `new Audio()` per fire, so every tap paid a fetch off the
    virtual host plus an mp3 decode before anything sounded - ~0.6s in WebView2 on cues
    (`paper`, `door`) whose whole job is to be simultaneous with the click. So the moment the
    context comes up, `audio.js` fetches and `decodeAudioData`s every AVAILABLE sample except
    `NEVER_BUFFERED` (the five `hold` beds plus `intro_bed`, which the splash strikes at boot
    long before any decode could land) and fires them from an AudioBufferSourceNode into the
    same bus graph - same slots, same governor, same `onEnded` reasons. Two consequences to
    hold on to: **a cue that beats its own decode takes the RECIPE, never the element** (trap
    70 - instant and quiet beats late and loud; a SAMPLE_ONLY name with nothing decoded drops
    exactly as a missing file drops), and a fetch/decode failure **strikes the name off
    `available`** with the same verdict the element's `error` handler passes. Loudness is a
    separate table, `SAMPLE_TRIM` (paper .45, door .55, whoosh .65, everything else 1),
    multiplied into the one-shot gain on both paths - the mp3s are mastered near full scale
    where the recipes they replaced were quiet by construction. `CLIP_GAIN` is deliberately
    untouched: the trigger whispers ride it too and they were never the loud ones.
    `stats().buffered` / `.decoded` say whether any of this is actually running in a host.

87. **SPLITFLAP'S CUE TIMING IS HARD-COUPLED TO THE CSS CASCADE.** `cueCascade()` staggers
    its `flap` row ticks at `ROW_STEP_MS = 400` and lands `commit` at `(rows-1)*400 + 1500`,
    mirroring `styles.css`'s `--r * .4s` stagger and meta fade. Change one, move both. The
    cues live in `replay()` only - `animate:false` builds stay dead silent (the boot builds
    the campus board that way), and BOTH the timetable deal and the VN board handoff sing
    through `replay()`, so a cue added at the build site would double the handoff.

88. **THE MIXER-SLIDER PREVIEW RIDES THE ECHO, NOT THE DRAG.** `shell/settings.js` fires its
    preview cue only when the host echoes back a write that THIS page made (it checks the
    row's `pending` class before `row.apply` clears it) - a host-initiated settings push
    never beeps at the player, and the cue lands after `audio.js`'s own `setting` subscriber
    so it sounds at the NEW level. Known gap (pre-existing, unfixed): an app-side mixer
    change echoes the WHOLE `audioLevels` object and neither audio.js nor settings.js
    consumes that shape - live levels move on relaunch, not mid-session.

89. **A BEAT CHANGE IS A TRANSITION NOW, SO A SEAM IS LONG ENOUGH TO REACH THE DOOR
    INSIDE IT (AV CLUB, owner playtest 2026-08-24).** The owner's order was "slow down the
    intro beats... about half a second of black screen with a sliding fading transition
    when we change a beat", and `vn/index.js applyPlate` is where it landed: out, black,
    in, settle, all of it on the `BEAT_*` dials and nowhere else. Four things follow, and
    three of them are the ways it can bite.
    - **No beat may start before its plate has landed.** `applyPlate(url, motion, done)`
      pays `done` when the arrival has settled, and EVERY caller hands it the next beat.
      Painting a caption without going through that callback puts the lower third over a
      black rectangle, which is the exact thing this wave was written to stop.
    - **The plate cannot slide itself.** `data-motion` fills a camera keyframe and a
      filling animation out-ranks an inline transform (trap 71), so the slide lives on
      `.arc-vn-bgwrap`, the carriage AROUND the plate. Its default state is "parked off
      the entry side, invisible" - that is what lets the swap happen mid-hold with no
      `transition:none` dance. The neon wash rides in the carriage too, or the arch would
      still be glowing through a hold that is supposed to be black.
    - **A SEAM WITH NO SCENE IN IT STILL NEEDS `skipTo`.** A transition runs over a second
      and no `runScene` owns the pill while one plays, so the cold open parks `toBoard`
      (LATCHED - two landings must be free or the wall gets two boards) and the walk parks
      its settle, both immediately after `mountLayer`. Leave `skipTo` null across a seam
      and the hold-to-skip pill is dead for the length of it, silently.
    - **A skip that lands mid-transition JOINS it, it does not restart it.** A repeat
      request for the plate already on its way in queues behind the arrival; a genuinely
      new plate bumps `plateToken` and the beats queued behind the abandoned one are
      DROPPED, never replayed (`dropPlateWaiters` is the skip path saying so out loud).
      Teardown cancels those debts rather than paying them - `unmountLayer` clears the
      queue and bumps the token, and the one continuation that must always run is the
      settler's, which is `closeLayer`'s business. Verified: a hold landing inside the
      black settles the layer and hands the night back, with nothing left in `body`.
    boot.js is UNTOUCHED by all of this. `scheduleIntroCues`'s beats end at 2650ms and the
    splash is up until at least 3270ms, the seam delay starts strictly AFTER `hidden` lands,
    and every cue timer re-checks `splashIsUp()` - so no cue can fire into the new black
    (traps 66 and 76 both still hold: `failBoot` still snaps, `settler` is still the one
    funnel).


90. **`ctx.mood` IS THE TENSION MIRROR, AND IT IS FACE-ONLY BY LAW (EMI COLOR,
    2026-08-24).** **LAW (1) WAS REVERSED BY THE OWNER ON 2026-08-25 - READ TRAP
    112 WITH THIS ONE.** **AMENDED AGAIN 2026-08-30 (stuck-hints) - THE FACE-ONLY
    HEADLINE NOW HAS EXACTLY ONE EXCEPTION; READ THE AMENDMENT AT THE BOTTOM OF
    THIS TRAP BEFORE THE BODY.** A game may tell the mascot how the room feels -
    `ctx.mood.tense()`
    latches until `.calm()`, `.clutch()` is the one big moment, `.stumble()` is a small
    >_<, `.runLost()` the once-per-class K.O. - and every one of them is throttled in
    shell.js (15s shared spacing, 3 stumbles a class, tense latch), so a game cannot
    flood her and must never build its own rate limit on top. Three laws: (1) ~~NO BARK
    POOL may ever sit on `tense` or `clutch` - mid-class speech is barred~~ **a small,
    clown-only pool on either name is legal since the heartbeat wave; mid-class WORDS
    are now rationed by voice.js (a 20s floor, 8 a class, and the `mood.hold` danger
    gate) rather than barred, and the ordinary road for commentary is
    `ctx.mood.note()`** - and the only
    words a stumble can buy are still the `miss` pool's own (maxPerClass:1); (2) call sites
    are opt-in and null-safe (`if (ctx.mood) ...` inside try/catch) because rigs stub
    ctx without it; (3) ride the game's EXISTING beat (L&F calls it inside its own
    `clutch()` ease) rather than inventing a parallel one.

    **THE AMENDMENT (owner, 2026-08-30).** The clause "no bark pool may sit on
    `tense` or `clutch`, and none may be added" - already half-reversed on
    2026-08-25 - is now amended in full, and `ctx.mood` is no longer face-only
    without qualification. The owner's words:

    > "im actually not oki with this law anymore, i think emi might wanna speak
    > during the games and hoestly it alredy does. Lets not overdo it tho, have
    > it speak somewhat during the games and the hints are an exception they
    > trigger if they are having troubles"

    **THE NEW BOUNDARY, PRECISELY.** EMI may speak in-class only through a
    sanctioned, rate-limited channel. Three things follow, and the third is the
    one that is actually new:
    1. ~~mid-class mascot speech is barred~~ **mid-class WORDS are rationed, not
       barred** - by voice.js for commentary (a 20s floor, 8 a class, the
       `mood.hold` danger gate), through `ctx.mood.note()`.
    2. **`ctx.mood`'s existing verbs stay FACE-ONLY.** `tense`, `calm`,
       `clutch`, `stumble`, `runLost` and `note` still only put a MOMENT on the
       wire; whether it also buys a line is voice.js's decision and never the
       game's. A game still cannot make her say a specific sentence with any of
       them.
    3. **ONE VERB IS THE EXCEPTION, AND IT IS THE ONLY ONE TODAY:
       `ctx.mood.askHelp(spec)`.** It carries the class's own finished strings
       and a callback, fires the `stuck` moment, and is the first ask in the
       codebase offered ON a live board (see trap 97). Its ration is shell.js's,
       not the caller's: **two a class**, behind the same 15s mood spacing, and
       the ask engine's `STUCK_GIVE_UP_MS` (8s) takes the strip off the glass by
       itself so trap 59's "no pointer-active node camping over a live board"
       survives intact. It is the whole of "the hints are an exception".

    **"LET'S NOT OVERDO IT" IS A CONSTRAINT, NOT A MOOD.** This amendment does
    NOT open a general in-class bark system, and it is not a licence to widen
    `askHelp` into a message bus. A second kind of in-class question gets its
    own verb, its own moment name and its own ration - and it needs the owner,
    the same way this one did. The one live consumer is Daily Trigger's
    stuck-hint detector (`games/daily-trigger/index.js`), which exists because
    the answer pool is niche by a separate owner ruling that still stands: the
    words do not get easier, the player gets offered a hand.

    The `classStart` payload
    also carries `family` now - moments.js varies the arrival face by room kind and an
    absent family falls back to the locked glance.
91. **A SPOTLIGHT LETTER CARRIES EXACTLY TWO ANIMATIONS - [entrance, idle] - AND THE
    ORDER IS LOAD-BEARING (title idioms, 2026-08-24).** Every `.arc-rs-l` pair in
    styles.css is `entrance (fill both), idle (no fill, delayed, infinite)`: through the
    idle's delay the entrance's `both` holds the finished pose, and after it the idle
    (later in the list) owns the properties it animates. `html.ae-lite .arc-rs-name
    .arc-rs-l { animation-play-state:running, paused; }` is what makes ae-lite
    "entrance only": it pauses the SECOND slot by POSITION, freezing the idle in its
    invisible pre-delay state. Add a third animation to a letter, give an idle a fill,
    or reorder a pair, and ae-lite silently breaks (a paused mid-list idle with fill
    would freeze the name mid-flicker). Variants ride `animation-name` overrides only
    (sort's 2n, composure's 4n set, deja vu's dying tube 7n+3) so the pair shape never
    changes. Idle events also budget legibility - the name is dark/displaced <=20% of
    any cycle - and the wall whisper (`arc-rw-*`) is hover/focus-gated per card so the
    grid never runs ten ambient loops at once.
92. **A REDUCED-MOTION "ONE FADE" CANNOT BE A TRANSITION, AND IT CANNOT LIVE INSIDE THE
    OVERLAY EITHER (the ID spotlight, 2026-08-25).** The global freeze at the bottom of
    `styles.css` is `html.arc-reduced *, ::before, ::after { animation-duration:.001s
    !important; animation-iteration-count:1 !important; animation-delay:0s !important;
    transition:none !important; }` - so the obvious way to write "reduced motion is one
    120ms fade" (a `transition:opacity` plus a class flipped on the next rAF) is dead on
    arrival: the transition is forbidden and the overlay simply pops. It has to be an
    ANIMATION. But the decoration law's own test is "no `animation-name` other than none
    INSIDE the reduced overlay", which an animated veil would fail. Both hold at once only
    because the fade sits on the OVERLAY ROOT (`.arc-id.arc-id-reduced`) while
    `.arc-id-reduced *` is `animation:none !important` - a root is not "inside" itself.
    Watch the specificity too: `html.arc-reduced *` is (0,1,1) and outranks
    `.arc-id-reduced *` at (0,1,0) on `animation-duration`, but they are different
    longhands, so `animation-name:none` still lands and the freeze still owns the duration.
    Read `.arc-rs-reduced` the same way, and do not "simplify" either one into a transition.
93. **THE STUDENT ID IS ONE NODE WITH THREE OWNERS, AND `data-inflight` IS THE FENCE.**
    `campus.idCardEl()` is built once and never replaced - orientation.js animates that exact
    element (ORIENTATION.md §3.2), `emi/fieldtrips.js` measures it by the `.campus-idcard`
    selector, and the card is a `role=button` that opens the spotlight. Three things follow.
    Adding children is fine; REPLACING the node is not, so `setProfile()` repaints in place
    and never re-renders the card. Withheld is still the `hidden` PROPERTY only (trap 27), and
    the click is refused while it is set. And the handover is refused too: orientation.js
    stamps `data-inflight="1"` where the flight starts and `landCard()` clears it - which is
    the ONE place every path out of the beat already funnels through, so there is exactly one
    place it comes off. A press mid-flight would open a spotlight over a card that is still
    travelling and hand focus back to a node the beat is about to restyle.

92. **THE CHIP STRIP IS THE THIRD AND LAST NODE ON THE LAYER THAT TAKES A CLICK
    (EMI ASKS, 2026-08-25).** Trap 59's law is that `#arc-emi` is
    `pointer-events:none` and exactly two nodes turn it back on (`.emi` and
    `.emi-dock`). An ask adds `.emi-chip` and NOTHING else: the `.emi-ask` strip
    that holds the chips stays inert, so the gap between two chips is still a
    click that reaches the board underneath. Four `pointer-events:auto` rules on
    the whole layer now, and `test-asks.mjs` counts them. The rest of trap 59
    survives untouched and is asserted the same way: `widget.js` still makes
    exactly ONE `preventDefault()` call (the `pointerdown` on `.emi`), the ask
    engine's one document listener (keydown; its pointerdown left with the
    any-press cancel, trap 118) is `{passive:true}` in the bubble phase and
    calls neither `preventDefault` nor `stopPropagation` (trap 80's shape), and
    EMI still adds no rung to the Esc ladder - Esc dismisses an ask on its way
    past and the ladder never notices it happened.
93. **AN ASK MAY NEVER LAND ON TOP OF A LINE, AND THAT IS TWO SEPARATE RULES.**
    (a) THE ORDER: `emi/index.js voiceMoment` asks the voice, then the field
    trips, then the asks - so a night with a scripted beat in it is never also a
    night she stops you to ask a question. (b) THE STATE: `widget.askReady()` is
    the one honest answer to "may she ask right now" and it refuses over a say,
    a chain, a press, a drag, a live field trip, a live off-channel, a dismissed
    or disabled EMI, and a strip that is already up. Neither rule can carry the
    other: the order alone would still let an ask land while a trip's power-off
    was running, and the state alone would let a bark and a question fight over
    the same moment. **And the latches are a third thing entirely.** `note()` is
    called on EVERY moment, `offer()` only on the declined ones - a dare that
    resolved only when the voice happened to stay quiet would resolve about half
    the time. If you add a moment to the ask table, add it to `ASK_TRIGGERS` and
    ask yourself which of the two entry points it belongs to.
94. **`{name}` RESOLVES TO THE LINE THAT SHIPPED, NOT TO A FALLBACK NAME.** The
    token is the ONE substitution `voice.js` performs, and the whole design is in
    what it does when there is no name: the token AND the punctuation it brought
    with it are removed, so `'{name}. you came back.'` is `'you came back.'` byte
    for byte on an install that never answered a14. That is what lets a name-drop
    be written into an existing beat (p06's anniversary line is a PREFIX, not a
    fork) instead of doubling the pool. There is no "hey you" and no "student":
    a token with nothing behind it is silence, which is the rule the whole voice
    runs on. Two consequences: the ladder measures the RESOLVED line for its hold
    (a name is longer than its token, and a `tail` scheduled against the raw text
    would land on a live bubble - trap 72's cousin), and the ration is spent in
    `sayIt` only when the line actually landed, at most once a sitting. New
    name-drop lines in `barks.js` also carry `when: ['hasName']`, so an unnamed
    player sees the pools they always had; only a line that was ALREADY there may
    take a bare prefix.
95. **THE BED ASK RIDES `exitAim`, NOT `exitIntent`, AND IT MAY NEVER TOUCH THE
    DOOR.** `boot.js` fires `exitIntent` 450ms into a 1200ms Esc hold - the wrong
    end of the gesture for a question, because the window is already closing
    behind it (and that moment belongs to the flinch anyway). So `shell.js` mints
    a second, earlier and much softer signal: a `{passive:true}` `pointermove` in
    the bubble phase that fires `exitAim` ONCE a sitting when the cursor reaches
    the top-right corner the host window's close button lives in. The fence is
    the feature and it is asserted by source shape: no `preventDefault`, no
    `stopPropagation`, no `beforeunload`, no refusal, no await, nothing that
    could delay a frame - and the listener comes off in the shell's own teardown.
    A skipped "bed?" costs HER sleep (three running buys a groggy greet), never
    you a second click.
96. **THERE IS EXACTLY ONE WRITER OF THE `emi` KEY, AND THE ASK LEDGER RIDES
    IT.** `widget.js save()` does `store.set('emi', blob())` - a full replace, not
    a merge - so an ask engine that wrote `store.merge('emi', {name})` of its own
    would have its name eaten by the next drag. The ledger (`ask{}`, `name`,
    `lastAskSession`, `bedSkips`) is therefore spliced into `blob()` and mutated
    through `widget.askState()` / `widget.askSave()`, and every field is
    re-derived defensively on the way IN (a stored name is re-sanitised, a row
    with no `a` is dropped). Nothing is written at all for a player who has never
    answered an ask, so an untouched install keeps the blob it always had.
    `voice.js` reads the same ledger through `store.get('emi')` for
    `askIs` / `askAnswered` / `sessionsSinceAsk` / `hasName` and writes none of
    it. An IGNORED ask is deliberately NOT an answer to any of those predicates:
    she can never reference a conversation you declined to have.
97. **`classStart` IS EVALUATED WHILE THE MID-CLASS LATCH IS STILL OPEN, AND A
    DARE'S CLOCK IS THE SHORT ONE.** **AMENDED 2026-08-30 (stuck-hints): THE
    LAST SENTENCE OF THIS TRAP NO LONGER HOLDS - SEE THE AMENDMENT BELOW.**
    The dares (a09/a10/a11) have to be offered
    on `classStart`, which is also the moment that closes the "not mid-class"
    gate - so `offer()` runs first and `note()` sets the latch, which is the
    whole reason the module has two entry points instead of one. That places the
    strip correctly (shell.js fires `classStart` right after `clearScreen()`, one
    beat before the class chrome is built and long before a board takes input),
    but it does not keep it there: a strip nobody answers would still be up when
    the board goes live. Hence `DARE_GIVE_UP_MS` (12s, against the ordinary 40s)
    on every `classStart` ask, plus the `classStart` latch in `note()` closing
    any OTHER ask still standing (it had to move there when presses stopped
    cancelling, trap 118). ~~If a future ask wants to ride a moment that fires ON
    a live board, it does not - move the offer to the class-rules sheet
    (trap 85) instead.~~

    **THE AMENDMENT (owner, 2026-08-30).** The struck sentence above was
    absolute and is no longer. The owner's words, quoted in full under trap 90:

    > "im actually not oki with this law anymore, i think emi might wanna speak
    > during the games and hoestly it alredy does. Lets not overdo it tho, have
    > it speak somewhat during the games and the hints are an exception they
    > trigger if they are having troubles"

    So there is now exactly ONE ask that rides a moment fired ON a live board -
    `a16_stuck`, on the `stuck` moment - and everything that made the old
    sentence true is still true, which is why it needed a carve-out rather than
    a deletion:
    - **It still may not camp.** `STUCK_GIVE_UP_MS` (8s, against the dare's 12s
      and the ordinary 0 = never) is the only thing that takes it down when the
      player just keeps playing, because the any-press cancel was deleted on
      2026-08-25 (trap 118) and the three remaining closers are chip 1, chip 2
      and Esc. It is load-bearing, not a nicety.
    - **The board must not be able to eat the answer.** Daily Trigger's
      `onKeyDown` claims A-Z, Enter and Backspace only, so `1`, `2` and `Escape`
      reach the ask and put no letter on the grid. **An ask may only be offered
      over a board that leaves those three keys free** - check the game's key
      handler before adding a second consumer.
    - **It is never offered over a PRECISION board** (trap 59's families:
      tracking, reflex). Homeroom is a keyboard word game; the strip is not over
      anything the player is aiming at.
    - **The reask ladder refuses it.** `rememberReask` bars `stuck` exactly as
      it bars `classStart`: a banked board question would resurface on the
      campus minutes later with the board torn down, and a YES would fire a
      callback into a dead class. `note('win'|'fail'|'runLost')` also closes a
      standing one.
    - **It is not raised by a game directly.** The one road in is
      `ctx.mood.askHelp()` (trap 90's amendment), which rations it to two a
      class inside shell.js. A game may not fire `stuck` itself.

    A future ask that wants this window has to satisfy all five, and it needs the
    owner. The class-rules sheet (trap 85) is still the right home for anything
    that is merely EARLY rather than genuinely mid-board.

98. **THE STRIP LIVES INSIDE `.emi`, SO EVERY CHIP PRESS HAS TO BE HANDED BACK -
    AND ONLY A BROWSER CAN SEE THAT IT WAS NOT.** `.emi`'s `pointerdown` is the
    drag/pet handler, and it does two things a button cannot survive:
    `setPointerCapture` on the root (which retargets the release, so the chip's
    own `click` never fires) and `preventDefault()` (which suppresses the
    compatibility mouse events the click is minted from). A chip that fell
    through to it was therefore dead on arrival AND quietly banked a head-pat.
    `onDown` now returns early on `inAsk(ev)`, exactly as it has always done for
    `inX(ev)` - the x button has needed the same guard since day one, and for
    the same reason. **The node suite passed the whole time.** The DOM double has
    no pointer capture, no compatibility events and no real click dispatch, so it
    cannot express this failure at all; `proof-asks.mjs` (port 8753) caught it on
    the first run. Any future affordance placed inside `.emi` needs the same
    early return, and a browser assertion to prove it.

99. **`init.devAnnex` IS A PEEK, NOT A REVEAL - AND THE DESKTOP CANNOT SEE IT.**
    The owner needs to walk the Records Annex on `app.cclabs.app/arcademy` without
    burning the once-ever reveal, so the WEB host projects one extra init boolean,
    `devAnnex`, beside `devDoor`. `shell.js` reads it once as `annexPeek` and ORs
    it into exactly TWO places - the campus hatch's bag (`annex:`) and the Records
    Office's `ajar` - which is the whole feature: the lab becomes REACHABLE.
    **It must never widen past that.** In particular:
    - `maybeAnnexReveal` and the morning-after catch-up probe that schedules it
      are UNTOUCHED. OR-ing `annexPeek` into the `if (!store.get('annexRevealSeen'))`
      guard would *suppress* the real beat for good, which is the exact opposite
      of a peek.
    - `store.set('annexRevealSeen', ...)` still happens in ONE place and a peek is
      not it. Nothing about a peek is persisted.
    - `seenFlags.annex` (the postman's ctx) deliberately keeps reading the real
      store flag. That flag gates letters, and a delivered letter *is* persisted
      state; a peek that minted mail would be a reveal wearing a hat.
    - `shell/seep.js`'s `postReveal()` keeps reading the real flag too, for the
      same reason - the Seep's escalation is a story beat, not a door.

    **`ArcademyHostService.cs` has never sent this field and must not start.**
    Absent is false, so the desktop build cannot reach the branch at all; the
    C# side of this feature is that there isn't one. The web end is
    `cclabs-web`: `ARCADEMY_ANNEX_PEEK_EMAILS` (server allow-list) + `?annex=1`
    on the lobby -> `arc-web:annexpeek` in localStorage -> `host/init.js`. The
    query string alone is never enough, and the lobby REMOVES the key on any
    visit that is not a peek. None of it is a security boundary - the web meta
    store is localStorage, so devtools could always reach the annex - it keeps
    the lab off the *supported* path, which is all it was ever asked to do.
100. **THE STUDENT ID NEVER `display:none`s ON A SMALL STAGE, IT FOLDS (owner bug 2026-08-25,
    "I cannot see my profile card on the web").** `styles.css`'s "small stages" query
    (`max-width:760px, max-height:600px`) is EVERY phone in BOTH orientations - an iPhone is
    <=430px on its short side, so landscape trips the height clause and portrait the width one -
    and from the first hub commit it hid `.campus-idcard` outright, written when the card was
    decoration. PR #299 then built the post row around "once the card is gone the corner is
    free". The SAME node now folds into a ~185x52 tag (photo, name, number; band / stats / tier
    / chip / clip hidden by class, never by replacing the node - trap 93 still holds) and the
    post row stepped right of it (`+ 212px`) - which parked both props across the front of The
    Pool, so on the owner's second pass the same day the row left the bottom edge altogether
    and took the TOP-LEFT corner instead (`.campus-crest` is already hidden up there). If a
    future small-stage rule needs the bottom-left corner, fold
    harder; never hide. The web host's account chip (`shell/accountchip.js`) also offers "Open my
    card" from the bar, so the full ID is one tap away on a phone either way.
102. **THE ID CARD FACE HAS EXACTLY ONE DRAWN WIDTH, AND THE PORTRAIT GATE IS WHY (owner bug
    2026-08-25, "the card is cut off at the top and the page will not scroll to it").** Three
    lessons, and only the first is the obvious one.
    (a) A CENTRED FLEX CHILD TALLER THAN ITS CONTAINER OVERFLOWS THE **START** EDGE. `.arc-id`
    centres `.arc-id-stage` with `align-items:center`; the stage measured 656px in a 390px
    window, so the top of the card sat at y=-133 with `overflow:visible` all the way up and
    nothing anywhere to scroll. The cure is the STAGE owning `overflow-y:auto` - never
    `.arc-id`, because the veil, the key light and the close button live outside it and must
    not scroll away - plus `overflow-x:hidden`, since `overflow-y:auto` alone computes the
    OTHER axis to auto as well and the 150%-wide `.arc-id-lamp` then grows a horizontal
    scrollbar.
    (b) THE OBVIOUS SECOND HALF - a smaller `width` on `.arc-id-big` - IS WRONG. The face is
    absolute percentages over fixed 22px insets and fixed 9-24px type, so at ~330px the meta
    column overruns the body and the homeroom row falls off the bottom of the card. Nothing had
    ever caught that because **a phone cannot reach this card at any width but 560px**: portrait
    is refused outright by the orientation gate, so landscape is the only live phone view and
    92vw never binds there. Scale it with `zoom` on `.arc-id-cardwrap` instead. `zoom` scales
    the LAYOUT BOX as well as the paint, so the row and the scroller measure the card at its
    drawn size; a `transform:scale()` would leave a 346px hole in the flow AND fight
    `arc-id-cardin`, which animates that same transform. `zoom` takes a number and there is no
    dividing a `dvh` by a `px` to get one, so the steps are a ladder of media queries.
    (c) A CUSTOM PROPERTY IS HOW YOU BEAT `html.arc-mobile` WITHOUT AN `!important`.
    `html.arc-mobile .arc-id-sheet` is (0,2,1); a bare `.arc-id-sheet` in a LATER media query
    is (0,1,0) and loses, source order be damned. Anything that has to retune a value the
    mobile block already sets gets published as a `--var` on a shared ancestor and READ by the
    mobile rule (`width:var(--id-w, min(560px, 92vw))`) - only one rule ever sets the property,
    so there is no contest to lose. Same shape as `--arc-mobile-chrome-h`.

103. **THE SPLASH EXIT IS A CHAIN OF GATES NOW, AND SILENCE HAS TO ANSWER INSTANTLY
    (owner report 2026-08-25, "wait for the jingle to end before opening the Arcademy").**
    `intro_bed.mp3` is 4.000s and the splash used to walk out on it on BOTH host paths - the
    autoplay host at INTRO_MIN_MS + the fade (hidden ~3.27s from t0) and the knock host at
    KNOCK_EXIT_MS + the fade (hidden ~1.02s after the strike). `dismissLoader()` is now
    RE-ENTRANT and each gate is a "come back to me", never a sleep: the knock, then the bed,
    then the INTRO_MIN_MS floor. Three things about it are load-bearing.
    - **A CUE THAT WILL NOT SOUND MUST SAY SO INSIDE THE DISPATCH.** `shell/audio.js` answers
      the new `detail.onEnded` hook with `'dropped'` SYNCHRONOUSLY on every road that never
      reaches an element (mute, master 0, a zero fx bus, no AudioContext, a SAMPLE_ONLY name
      with no file). Move one of those answers onto a timer and every muted player sits
      through the cap staring at a loading screen for a sound they were never going to hear.
      A muted mixer, a zero bus and a host with no `intro_bed.mp3` all dismiss on the OLD
      timing, and that is the test that proves the hook is wired the right way round.
    - **THE CAP IS NOT OPTIONAL AND IT IS BOOT'S OWN.** `onEnded` is a courtesy: a host with
      no consumer on the `arcademy-sfx` bus, an older audio.js, or an element that stalls
      forever answers NOTHING. `BED_HOLD_CAP_MS` (INTRO_BED_MAX_MS + 200) is measured from the
      strike and ends the hold whatever the audio is doing. Never make the splash's exit
      depend on a promise only the mixer can keep.
    - **REDUCED MOTION MAY NEVER ACQUIRE A HOLD** (trap 66 again): it takes no cues, so it
      strikes no bed, so there is nothing to wait for and it still dismisses at ~3.27s.
    What makes the extra ~1.4s of cover safe is that the loader's CSS timeline ALREADY loops
    when the beat is over (`arc-intro-breath` from 3.1s, `arc-intro-rail-chase` from 2.3s,
    both `infinite`). Shorten those to one-shots and the splash freezes on its last frame for
    over a second while the jingle finishes. Measured after: hidden at ~4.6s from t0 on the
    autoplay host, and knock + ~4.4s on the browser (headless, real mp3 decode).

101. **A CONTROL INSIDE A SCALED OR TRANSFORMED ROOM IS NOT MEASURED IN SCREEN PIXELS,
    AND ON A PHONE THAT IS THE DIFFERENCE BETWEEN A CONTROL AND A SMUDGE (owner bugs
    2026-08-25, Records Office in landscape).** A scene room is a fixed 1376x768 plane
    scaled to fit; a phone in landscape fits it at about **0.5**. Four separate bug
    reports came out of one arithmetic mistake, and the rule that closes all four is:
    **paint is authored in stage pixels, chrome is authored in screen pixels, and the
    two may not live in the same box.**
    - **`.asc-back` was a child of the slide.** Every phone rule written for it - a 44px
      minimum thumb target, 12px type, a safe-area inset - was multiplied by the fit and
      landed as a 39x22 pill with 6px type. It hangs off `.asc-root` now (fixed to the
      window, like the apron band always has been). The same arithmetic hit
      `.arm-hot.arm-exit`'s 2px rim (one physical pixel) and its 13px tag (six).
    - **`position:fixed` LOSES TO ANY TRANSFORMED ANCESTOR.** `.arc-rs` (the card
      spotlight) is `fixed; inset:0` and was appended into `.arc-records` - which, inside
      the Records room, sits in `.asc-panel`, and that panel carries a `transform` for its
      slide-up. A transformed ancestor becomes the containing block, so the whole
      presentation was laid out inside an 810x302 scroller parked at the bottom of the
      phone: veil stopped at the panel edge, card centred on the panel, top of the card
      cut off. **A modal over the window is mounted on `<body>`** - records.js's spotlight
      and corkboard.js's notice reader both are - and anything it must clear (the apron
      band) reaches it as a custom property `scene.js` publishes on `<html>`
      (`--arm-band-h`, cleared in destroy()). A `padding-bottom` written against an
      ancestor it does not actually have is not a fix; it is the same bug twice.
    - **A HOVER-ONLY AFFORDANCE IS NOT AN AFFORDANCE ON A PHONE.** `.arm-exit` was dark
      until `:hover`/`:focus-visible`. A thumb has neither, so the Records storeroom door
      (the only way to the annex from the office) was 75x231 of painted wall that said
      nothing. It carries a resting rim + its tag under `html.arc-mobile` only; the
      desktop stays "found, not advertised".
    - **AND THE HUB HEADER CAN COME BACK OVER A ROOM.** `.arc-topbar` is sticky at z30 and
      a room is fixed at z10. `setStage()` hides the bar, but `renderTopbar()` guarded on
      `campus` alone - so any unconditional repaint (`onMeta` from a host `meta` push,
      `onPayout`) hoisted it back over the Records room, the report card and the annex,
      swallowing the top band and the step-back pill with it. The guard is
      `!!campus || !!stageMode` now: **a full-bleed stage owns the window.**

    Sitting behind all of it: **a phone viewport is a merge gate** (section 6). Every one
    of these was invisible at 1600x900 and obvious in the first 844x390 shot.

104. **AN ASK IS A BUBBLE THAT IS WAITING FOR YOU, AND THE SAY LAW ALONE CANNOT
    PROTECT ONE (owner bug 2026-08-25: "the how can I call you prompt got removed
    because I think I triggered another speech").** widget.js's law 3 is "a protected
    chain refuses to be replaced by an UNPROTECTED one" - and every line EMI says is
    protected, so a bark landing thirty seconds into a question walked straight over it.
    The question text was replaced, the chips stayed, and the first click the player made
    to get rid of the orphaned strip dismissed the ask for free. **An ask therefore
    raises a second, higher fence:** `askOwnsGlass()` is true from the question's own
    `emi.say(q, {ask:true})` until `unmountAsk()`, a later SAY is parked in ONE slot and
    released afterwards if it is still worth saying (`ASK_QUEUE_MS`, 8s), and every other
    reaction - a raw face, a chain, an emote, `force` and all - is refused outright.
    Four things fall out of it and each one was its own half-bug:
    - **THE HOLD OPENS WITH THE LINE, NOT WITH THE STRIP.** The chips land
      `STRIP_LEAD_MS` (1560ms) after the question does, and that gap is the window the
      owner's bug actually fell through. `mountAsk` therefore calls `dropStrip()` - the
      half WITHOUT the release - so clearing a previous strip on the way in cannot take
      down the question that is landing.
    - **THE QUESTION HAS NO AUTO-HIDE AT ALL.** `GIVE_UP_MS` is **0**, which is not
      "expire immediately" but "she waits": the hold handed to the say is an hour
      (`DIALS.ASK_HOLD_MS`, a large FINITE number because `chains.js makeSay` does
      `holdMs | 0` and `Infinity | 0` is a 400ms flash), and `releaseAskLine()` is the
      only thing that ever ends it. The player cannot be trapped by that - Esc is a
      free dismiss and an answer is two chips away (presses stopped cancelling on
      2026-08-25, trap 118) - and there is exactly ONE carve-out: the dares still
      leave after `DARE_GIVE_UP_MS`, because trap 97 says a strip may not be sitting over
      a board that is about to take input.
    - **THE 220ms LEAVE ANIMATION CANNOT RIDE `later()`.** Every resolution an ask has is
      followed in the same tick by her reaction to it, and a reaction runs `killTimers()`.
      The drop timer went with it, so `.out` faded the strip to opacity 0 and left a node
      with two `pointer-events:auto` chips over the board for the rest of the sitting -
      trap 59's failure mode, and invisible in every screenshot. It has its own handle.
    - **AN INTERRUPTED ASK IS NOT AN ANSWERED ONE, AND `a14_name` PAID FOR THAT TWICE.**
      A press records nothing (trap 96), so an interrupted question simply vanished; it
      now comes back once a sitting on the next `idlePlayer` or the next firing of its own
      trigger (`S.reask`, dares excluded for the reason above). And the name ask's window
      was `sessions === 3` **exactly**, so one stray click on session three meant EMI
      could never learn your name on that save. It is `>= 3` while unanswered now, held
      off by a once-a-sitting latch the way a15's `bedAsked` is.
    The one display string she renders (the Send button's label, `emi_ask_send`) is
    resolved by **shell.js** and handed to `mountEmi` as `strings.askSend`: no lexicon
    reaches EMI, and this did not change that.

    Two more the phone shot found, both of them trap 61's heuristic coming due:
    - **THE BUBBLE'S EAR TEST IS SIZED OFF HER BODY, AND THE PHONE PAIR DOES NOT FIT
      IT.** `faceBubble`'s reach is `left + w * 1.55`, where `w` is EMI's WIDTH -
      calibrated for the desktop pair (150px EMI, 104px box). A phone runs an 86px EMI
      beside a box allowed `min(72vw, 168px)`, so the correct ear still left "what do i
      call you?" 75px off the right edge at 844x390. The bubble now gets the same
      MEASURED pull-back the strip has had (`--emi-bubble-cx`, on `left`, never on
      `transform` - `.emi-bubble.pop` animates the transform and would out-rank an
      inline one for its whole 280ms).
    - **AND IT HAS TO BE MEASURED WITH `offsetWidth`, NOT WITH A RECT.** That same pop
      is a scale keyframe, and `getBoundingClientRect` reports the TRANSFORMED box
      (trap 74's twin half). A rect read on the frame the line lands measures the box
      at about 60% and produces a clamp a sixth of the size it should be - which is
      exactly what the first attempt shipped, and it looked almost right.

    **The suite: `scratchpad/emihold/test-askhold.mjs`, 117 assertions**, real widget +
    real asks.js + real chains.js over the DOM double and a VIRTUAL CLOCK (an hour-long
    hold cannot be asserted on the wall clock). `before-probe.mjs` beside it is the
    before/after: point its `arc/` at main and watch "rough day?" become "you came back."
    with the chips still up.

105. **`background:` IS A SHORTHAND, AND THAT IS WHY THE NOTICE READER WAS A PANE OF
    GLASS (owner bugs 2026-08-25, three defects on one corkboard).** All three of the
    owner's phone shots were one wave; only the first is a geometry bug and the other two
    share a single root.

    **(a) A HOTSPOT RECT IS NOT A PLACE TO HANG PAPER.** The wide shot's miniature was
    pinned to `RECTS.corkboard` [226,153,323,226] - the rect you PRESS, which covers the
    timber frame and carries a few pixels of slack under it so a thumb is comfortable. The
    painted cork inside that frame is [237,165,302,197]: nine pixels lower at the top and
    seventeen higher at the bottom. So the preview started on the frame, ended on the desk,
    and its bottom-right sheet landed on the banker's lamp - which breaks the cork plane at
    y=322 and reaches back to x=480, INSIDE the board's own right third. Every number here
    was measured off `art/vn/vn-09-records-office.png` with a script, not read off
    `records-room-spec.json`, and the constant that came out of it (`CORK_WIDE`, trimmed to
    152 tall so the paper stops above the shade) carries the derivation in its comment. The
    rule: **a rect you press and a rect you decorate are two different measurements of the
    same object**, and a room that has both must name both.

    **(b) A SHORTHAND RESETS WHAT IT DOES NOT MENTION.** `.arc-corknote` painted its paper
    as `background:linear-gradient(...)`, which sets background-IMAGE and resets
    background-COLOR to `transparent`. Every sheet was therefore one `background-image:none`
    away from being a window - and `.arc-corknote.look-pencil { background-image: none; }`
    is exactly that rule, shipped, for the groundskeeper's notices. On the board they read
    as cork-through-paper. In the READER, which mounts the same `.arc-corknote` stock over
    the dusk on `<body>`, the same sheet was a transparent rectangle with dark ink on it and
    the whole noticeboard legible through it. Nobody had seen it because the look is dealt
    per NOTICE and the reader is opened per SHEET: it took a specific night and a specific
    press. The paper colour is a `background-color` now and the stocks are
    `background-image`s over it, so a rule that turns a texture off can never take the
    opacity with it. **Any element whose colour matters declares that colour as a colour.**

    **(c) A PERCENTAGE THAT WAS SAFE ON A CAPPED SHEET IS NOT SAFE ON AN UNCAPPED ONE.**
    Dropping the `max-height:292px` + `mask-image` fade (owner: "that is not how printed
    paper works, we should see the full doc") let a sheet grow to its copy - and the torn
    corner, `clip-path` at `100% 89%`, bit 11% of the height: 20px on the old sheet and 66px
    on a 600px one, eating sentences. It is `calc(100% - var(--tear))` now, with a matching
    `padding-bottom`, so the bite and the blank paper under the copy are the same fixed
    number. **When you remove a cap, re-read every percentage that was measured against it.**

    Three smaller things the wave settled, each of which cost a run:
    - **THE FIT MUST UNTRIM BEFORE IT MEASURES.** A `display:none` slot measures zero, and a
      zero-height sheet is a sheet the fit believes already fits - so a fit run over an
      already-trimmed wall leaves everything below the cut at full type, and the next trim
      cuts higher for that reason alone. Untrim, fit, trim, in that order, every time. Two
      passes of the wrong order took the desktop miniature from six sheets to three.
    - **A PROP INSIDE A SCALED ROOM CANNOT MEASURE ITS OWN SCALE** (trap 101's cousin). The
      corkboard's type floor is 10 REAL pixels, so the fit has to divide by the stage scale
      - and the miniature carries a second scale of its own, so `rect.width / offsetWidth`
      on ITS host answers a fifth of the truth and prints the thumbnail in bigger type than
      the board it is a picture of. `scene.js` exposes `scale()` (the stage's, as of the
      last `fit()`) and both walls are handed the same one.
    - **TWO SHEETS, ONE SELECTOR, NO WINNER.** `.rr-cork.arc-cork-wall` is written in
      corkboard.css AND in recordsroom.css at the same (0,2,1... 0,2,0) specificity, and the
      two files are lazy-linked by two different modules in whatever order the network
      answers. Whichever landed last won. Every declaration on that selector now has exactly
      one owner, and the room suite greps recordsroom.css (comments stripped - the prose
      explaining what left names the properties) to keep it that way.

106. **THE BED IS STRUCK BEFORE INIT ON THE APP HOST, OR IT IS NOT STRUCK AT ALL (owner,
    third report, 2026-08-25: "wait for the jingle to end before entering, the jingle should
    start sooner").** Trap 105's neighbour, THE SPLASH WAITS FOR THE JINGLE, was correct and
    did nothing on the desktop: strikeBed sits behind the mixer, the mixer behind `init`, and
    the WebView2 host sends init ~3s after the page (log: launched 10:28:49.686, sent init
    10:28:52.922) - five times INTRO_BED_WINDOW_MS - so scheduleIntroCues dealt the stitched
    beats every time and the splash walked out at INTRO_MIN_MS. boot.js now strikes
    `assets/sfx/intro_bed.mp3` as a plain element at module evaluation (`strikeEarlyBed`) on a
    REAL WebView2 only (`window.chrome.webview` without the web shim's `__deliver` seam), sets
    `bedStruck` so the mixer never re-fires it and no stitched beat lands over it, and
    `tuneEarlyBed()` on init applies the mixer's own element math (sqrt(level) * CLIP_GAIN *
    fx * master) or STOPS it under audioMute / zero bus / reduced motion (trap 66). A refused
    play() (a browser) hands the strike back to the knock. tuneEarlyBed runs BEFORE the audio
    consumer import: a host whose mixer fails to load must not keep a muted school's bed
    playing. Test seam `introBedState()`; suite in session fe888044 scratchpad
    `bell/test-earlybed.mjs` (boot.js under a fake DOM, `?instance=n` per world; bridge.js is
    ONE shared instance, so its webview listener belongs to the first world - keep it).
    Same PR: the phone bell chip is two lines (grid, icon spanning both rows) stepped left
    of the bell tower's painted clock by clamp(32px, 5vw, 48px); every rule html.arc-mobile.

107. **EMI'S CADENCE IS THREE DIALS AND ONE LATCH, RETUNED 2026-08-25 (owner: "very
    few comments and animations").** `voice.js` `BARK_FLOOR_MS` 90s -> 40s;
    `CAMPUS_ODDS_MULT` x1.5 scales a pool's odds on every moment that is NOT in
    `MID_CLASS_MOMENTS` (miss/fail/runLost/tense/clutch/thinking) AND not while the
    session latch `S.inClass` is up (classStart/suspend set it, win/fail/runLost/
    reportCard/dayDone/greet/resume clear it - `miss` does NOT, a miss is mid-class).
    `channels.js` `SL_DIALS` 90s/20s/180s/6 -> 30s/10s/60s/14 and every per-channel
    cooldown halved. NEW: **the fidget** - a weighted wordless chain (`FIDGET_CHAINS`,
    glance/wink/thinking/sus/nod/smug/dizzy/wake) that rides `blinkIdle` AFTER the
    glitch passes, `FIDGET_ODDS` per blink, campus-only, and never inside
    `FIDGET_AFTER_SAY_MS` of a line (`S.lastSayAt` is stamped in `sayIt`, the one
    funnel every bark/beat/ask lands through). It owns no timer: it can only fire
    when the blink can, so the blink's own guards (busy/hidden/dragging/disabled)
    are its guards. A pool with `maxPerSession` (idlePlayer = 2) still caps itself
    - the multiplier moves the dice, never the ration or the doubles slot.

108. **THE WORD FLOOR IS A FALLBACK, AND THE VOICE ON IT IS OPT-IN WITH A LATCH**
    (2026-08-25, `core/vocab.js` + `assets/sublim/`). Three rules, and each one is
    a lie waiting to happen if you invert it.
    - **HOUSE WORDS ARE A FALLBACK, NEVER A MERGE.** `dayVocabulary(init.words, rng)`
      returns the host pool untouched when it holds anything and a seeded 12-word slice
      of `HOUSE_WORDS` only when it is empty (nothing enabled, or a creator mod whose
      manifest ships no `SubliminalPool` - `ModService.cs:1091`). Merging house words
      into a configured pool would dilute a list the player deliberately curated, in the
      one layer they cannot audit while it runs. One call site, `shell/shell.js`'s
      `dayWords`; `src.words` is NEVER mutated (init is the host's frame and other
      readers index it), and trap 24 is untouched - `ctx.absorb` pushes into the same
      array either way and `SubliminalPool` is still never written.
    - **THE SETTINGS ROW MUST NAME THE LIST THAT IS FLASHING.** The row used to read
      `init.words.length`; on a house day that says "0 words" while the classes flash
      twelve. The shell passes the RESOLVED `vocab: {count, source}` into
      `createSettingsPage` and the row says "12 words (the school's own)". Any caller
      that omits `vocab` falls back to `init.words` and reads exactly as it did before.
    - **`ctx.triggers` MUST ALWAYS DESCRIBE `ctx.words`.** On a house day the rows come
      from `init.houseTriggers` (`ArcademyHostService.BuildHouseTriggers`, a
      `TopDirectoryOnly` scan of `assets/sublim/*.mp3` cached in a static, empty on a
      missing folder - trap 86's law) FILTERED to the words actually dealt. Never
      synthesise `{text, audio:null}` rows for words the host did not list: echo's
      `triggerPool()` and instant-recall's `clipRows()` both count rows, and audio-less
      ones they already get off the `ctx.words` leg.
    - **`voice` IS OPT-IN, ONE CLIP AT A TIME, AND THE SHELL HOLDS THE GATE.**
      `fire('sub_flash', {voice:true, voiceKey:'<game>-whisper'})` says the word as it
      paints it, through this file's own `audio_trigger` - never `new Audio()`. It needs
      `opts.wordAudio[word]`, which the SHELL builds from the day's trigger rows and
      passes ONLY under `init.audioAudible`, so the flag is inert on a muted day and no
      game has to gate on `ctx.audioAudible` itself. `VOICE_MIN_GAP_MS` (= `SUB_HOLD_MAX_MS`,
      1400) is the latch: the stream ticks as fast as `SUB_MS.fast` 360 and six clips a
      second would empty `CLIP_VOICES` in one breath, so an early tick paints the word and
      stays silent. A call that passes BOTH `sfx` and `voice` (deja-vu's preview flash)
      plays the CLIP and skips the synthesised whisper for that tick - the oscillator is
      the fallback, not a layer over the real voice.
    - The 24 words are spelled the same in `core/vocab.js` and in `HouseWords` in
      `ArcademyHostService.cs`, and the filename is `slug(word)` (lowercase, spaces to
      underscores: "LET GO" -> `let_go.mp3`). Rename a word on one side and you orphan a
      clip on the other, silently.
    - The browser host shim lives in the SITE repo (`scripts/arcademy-web-ext`), so it must
      list `houseTriggers` in its fake init the same way it lists `sfxSamples`, or the web
      build flashes house words with no voice under them.
109. **THE HEARTBEAT IS THE ONE SANCTIONED UNATTENDED SPENDER, AND IT EXISTS SO
    NOTHING ELSE HAS TO BE (owner, 2026-08-25: "It's awfully quiet, tho we got a
    lot of lines. Never be completely idle: something new every 10 seconds ...
    Always do something. It has to feel alive").** This REVERSES the 2026-08-24
    field bug's rule. That rule stands where it was made - `voice.js onGesture`
    still spends no beat and no bark from an idle blink - but "nobody may spend a
    beat unattended" is now "exactly ONE thing may, and it is not the blink".
    `emi/heartbeat.js` carries the four gates a blink structurally cannot:
    - **`document.visibilityState`, and it takes the TIMER down, not the tick.**
      `visibilitychange` -> hidden CLEARS the interval; visible re-arms it AND
      re-stamps the clock, or the whole absence reads as silence and she
      performs on the first frame of a screen you have only just come back to.
    - **THE GEOFENCE, unchanged and absolute.** The Records Office, the annex
      and the lab are `emi.setEnabled(false)` screens, so `emi.enabled` IS the
      gate here. No dial and no data file can open it.
    - **`widget.askReady()` is the whole "is she free" question** - no say, no
      chain, no press, no drag, no trip, no live channel, no strip, not
      dismissed, not disabled. It never pre-empts and it never QUEUES: a refused
      tick is skipped and the next one is 2.5s away.
    - **It decides WHEN, never WHETHER.** Every act goes through the engine that
      already owns it (`emi.emote`, the deck, `voice.onMoment`, `asks.offer`)
      and every ration those keep - the bark floor, the doubles slot, the deck's
      cooldowns and PER_SESSION_CAP, all five ask gates - is untouched.
    ONE `setInterval` at `TICK_MS`, one short gaze-release timer in widget.js,
    and `destroy()` (called BEFORE the widget's, from `emi/index.js`) takes the
    interval, the visibility listener and the activity subscription with it.

110. **THE CLOCK IS MEASURED, NOT SLEPT, AND IT MEASURES `widget.onActivity`.**
    The tick asks one question - how long since anything visible happened - so
    the period stays honest even though the interval is coarse. `onActivity` is
    fired from FOUR choke points, and the exclusion is the trap: `play()` (every
    chain, say and emote), `raw()` (a held face), `apparate()` (a field trip),
    the deck's **`onStat('takeovers')`** - because trap 77's second canvas never
    runs through `play()` AND a wheel-rolled channel that waited on `prepare`
    starts a tick after its caller returned - plus every `emitGesture` **except
    `blinkIdle`**. Counting the idle blink would re-stamp the clock every 5.2s
    and the beat could never come due at all. A player verb counting as activity
    is the feature, not a bug: the heartbeat fills silence and never competes
    with a hand on the mouse.
    The gaze half of a `nudge` is `widget.nudgeGaze(dx, dy, ms)` - the same CSS
    translate on the canvas ELEMENT the cursor lean rides (trap 71), never a
    repaint. It is REFUSED outright under reduced motion (W1's gaze is off
    there and a heartbeat may not smuggle it back in) and over any live verb,
    and its release timer is cleared inside `restGaze()`, which is the one
    funnel every take-the-glass path already runs through. The widget's old
    internal `nudgeGaze()` (kick the rAF) is now `kickGaze()`.

111. **THE SEQUENCE LAW IS THE OWNER'S RHYTHM, AND IT IS WHY THE WHEEL IS NOT
    JUST WEIGHTS.** Never the same KIND twice in a row (a second face counts as
    a repeat even when the glyph differs); a SPOKEN kind (bark, ask) is always
    followed by at least one WORDLESS one; a screen never follows a screen; and
    after a bark the next act comes at `x AFTER_BARK_MULT` so the animation
    ANSWERS the line. STARVATION is the other half - past `SPEAK_STARVE_MS`
    (75s campus) / `CLASS_STARVE_MS` (90s in class) the next act is forced to a
    bark, **but only when the voice's own floor would let one through**, or the
    beat is spent on a refusal. The starvation clock reads `voice.lastSayAt`,
    not just the heartbeat's own acts: a bark spent by a shell MOMENT is words
    too. A refused kind is RE-DRAWN (bounded, three tries a tick) rather than
    queued, which is what stops a cooling deck or a spent ask from eating the
    whole beat. In class the wheel is four kinds - `screen` and `ask` are
    weighted 0, because the deck refuses a channel there anyway and an ask is
    campus-only by owner rule. A heartbeat-sourced deck pulse lifts EXACTLY one
    leg of `eligible()`, `THEATRE_IDLE_MS`; the per-channel cooldowns,
    `GLOBAL_COOLDOWN_MS`, `PER_SESSION_CAP` and every other leg still refuse,
    and the deep-idle screensaver pair is deliberately out of a pulse's reach
    (a screensaver that can be summoned is not a screensaver).
    `HB_DIALS` is the one frozen table. The period floor inside `rollPeriod` is
    deliberately 1000ms and NOT `TICK_MS`, so a suite that lengthens the tick to
    keep the interval out of its way does not silently lengthen the period
    underneath itself - that cost twenty red assertions the first time.

112. **MID-CLASS SPEECH IS LEGAL NOW, AND IT IS PAID FOR WITH A CEILING AND A
    DANGER GATE (owner, 2026-08-25: "it needs to comment ALSO WHILE IN A
    SESSION and react to what's happening").** This reverses trap 90's first law
    - "NO BARK POOL may ever sit on `tense` or `clutch`" - and the wider "no
    speech mid-class" rationale in `voice.js` and `moments.js`. What replaced it,
    all in `voice.js`:
    - `CLASS_BARK_FLOOR_MS` **20000** for a mid-class pool; the 40s
      `BARK_FLOOR_MS` is the CAMPUS floor from here on. Mid-class means `game:*`
      (always - only a game can fire one) and `heartbeat` with `payload.inClass`.
    - `CLASS_BARKS_MAX` **8** per class, reset by `classStart`. Ceremony pools
      are exempt from both, exactly as they always were.
    - **`ctx.mood.hold(true/false)` is the DANGER GATE.** A game holds the window
      where a sentence would actually cost the player the round (Impulse
      Control's go/no-go, Echo's playback, Misdirection's shuffle) and while it
      is held voice.js refuses WORDS on `game:*` and `heartbeat` - asked before
      the pool is even looked up, so no floor, ration or no-repeat is spent by a
      line that was never going to land. **FACES still fall through**, which is
      the tension mirror still working. It arrives as an ordinary `moodHold`
      moment (the games keep ONE seam) but is answered ABOVE the readiness
      check, so it can never be buffered as `pending` and replayed as a reaction
      three seconds into the window it was meant to protect; and it
      AUTO-RELEASES on `classStart` and on every class-ending moment, so a game
      that throws mid-window cannot mute her for the rest of the sitting.
    Predicates `campus` / `inClass` are how ONE trigger name serves both sides
    of the door, and `payload.inClass` OUTRANKS the session latch - the
    heartbeat knows which screen it is standing on, the latch is only as fresh
    as the last moment the shell fired. The campus cadence did not move:
    `BARK_FLOOR_MS` 40s and `CAMPUS_ODDS_MULT` x1.5 are as trap 107 left them.

113. **A `game:*` NOTE ALWAYS GETS A FACE, AND A PAYLOAD TOKEN WITH NOTHING
    BEHIND IT KILLS THE LINE.** `ctx.mood.note(id, extra)` mints the moment
    `game:<id>` - and `<id>` IS the bark pool's key, so renaming one orphans the
    other. There will be dozens of those names and `MOMENTS` deliberately has a
    row for none of them: `moments.js` answers every `game:*` from
    `GAME_NOTE_FACES`, keyed off `extra.kind` (celebrate / commiserate / tease /
    tension / curiosity / ambient - unknown AND absent both read as the ambient
    GLANCE). That is the promise the wave makes to a game author: every note
    gets at least a face, and the voice decides separately whether it also earns
    a line. The throttles are shell.js's and they are a FLOOD GUARD, not a
    ration - 2500ms between any two notes, 6000ms between two of the same id, 40
    a class - because the VOICE does the rationing and a game must never build
    its own on top (trap 90's third law, unchanged). They keep their own
    counters: sharing `lastAt` with `tense`/`clutch` would let one note eat the
    room's whole 15s weather budget. `hold()` is edge-triggered for the same
    reason - a game may call it every frame.
    **THE TOKENS:** `{n} {tile} {word} {left} {streak} {grade}`, resolved off the
    moment's payload the way `{name}` is (trap 94) with ONE difference that is
    the whole design - `{name}` collapses to the un-named variant because it was
    WRITTEN to, but a `{n}` the payload did not carry is a sentence about
    nothing, so the LINE IS SKIPPED. The filter runs in `pickLine`, so the pool
    answers with a plain sibling instead of falling silent, and `sayIt` carries
    the same check as the fence behind the fence. A raw `{n}` may never reach the
    bubble. Zero IS a value; `null`, `''`, `NaN` and a boolean are not. A pool of
    nothing but token lines is simply silent on a payload that carried none, so
    always write plain siblings beside a token line.

114. **A HOLD HAS AN OWNER, AND ONLY A FILE CAN HOLD** (W3 sfx, 2026-08-25,
    `shell/audio.js`). The mixer's one sustain: `detail.hold:true` on a SAMPLED name
    (or a `detail.url`) loops the element in slot `detail.key || name`, fades in over
    `CLIP_FADE_MS`, ignores `maxMs`; `detail.stop:true` fades that slot out, and is
    honoured even when muted so a room can always be left; `stop_clips` and the mute
    echo cut holds too. A recipe CANNOT hold: a hold asked of a name with no file
    behind it is `'dropped'` (a looping oscillator impression of a room is a different
    room), so the five beds (`records_bed` `campus_idle` `vn_bed_ext` `vn_bed_int`
    `cam_bed`) are SAMPLE_ONLY and a bed call site needs no fallback and no
    `hasSample()`. The rule that costs time: WHOEVER STARTS A BED STOPS IT in its own
    teardown / unmount / scene-change path (records room teardown, campus idle, VN
    `applyPlate` int/ext swap, annex cams). The mixer will never guess that a room has
    been left, and a bed that outlives its room plays under the next class.

115. **AN UNKNOWN CUE NAME IS A BLIP, NOT AN ERROR - SO ADD THE NAME IN THE SAME PR
    AS ITS FIRST CALL SITE** (W3). Misdirection fired `hit` `miss` `ride` `bank`
    `reveal` for a whole semester and every one of them degraded to the 660Hz tick;
    nobody noticed because nothing was thrown. The mixer now logs
    `[audio] unknown cue "x" - playing blip` ONCE per name (`unknownNames`), and those
    five are `ALIASES` onto real recipes (sting / thud / pop / commit / tell). The
    merge gate for any sfx wave is mechanical: every name in
    `grep -rhoE "(tick|cue|tone|audio|sfx)\('([a-z_]+)'" games engine shell emi vn annex`
    must be a key of SOUNDS, SAMPLES or ALIASES. Aliases resolve ONE level deep and
    may carry a `pitch` multiplier (`tape_stop` = glitch at .7 when its file is
    absent); the sample lookup always uses the name AS FIRED.

116. **COUNTDOWNS TICK ON THE SECOND BOUNDARY, NEVER ON THE TICKER** (W3). Every
    visible timer drained in silence before this wave, and the wrong fix is one
    `clock_tick` per rAF / per 60ms poll (a Geiger counter). The convention every
    game now follows: fire only when `Math.ceil(remaining/1000)` CHANGES, only in the
    last third (or last 3s, whichever the game's window makes sensible), pitch
    `1 + .06 * n` climbing toward zero, level .10 -> .18, and KILL it in the window's
    disarm so a resolved round never ticks once more over its stamp. Rate limits are
    the caller's job: the mixer will happily play sixty a second.

117. **ENGINE SUSTAINS CUE PER WAVE, NOT PER NODE** (W3, `engine/sustained.js`,
    `engine/loomWash.js`). Bubble beds, bursts, gif rain and the Loom's spiral are
    streams of DOM nodes; a cue per node is the fastest way to turn the fx bus into
    noise and the clip table into a queue. Bursts cap at 3-4 cues per burst, `gif_rain`
    is never per drop, `spiral_hum` strikes on mount and on each re-trigger only, wash
    air fires per wash CHANGE (not per re-entrant `startWash`), and teardown coalesces
    into ONE wash. `sub_flash`'s synth whisper stays suppressed on a voiced tick (108).

118. **AN ASK OUTLIVES EVERY PRESS, AND EXACTLY FOUR THINGS END IT** (owner,
    2026-08-25: "keep the prompt there till we respond - if we click elsewhere
    or on emi we trigger a new bark and we lose it"). The EMI ASKS wave shipped
    an any-press/any-key free dismiss (a document pointerdown listener plus a
    keydown fallthrough, and pet/drag/fling in the gesture handler); in play it
    meant the question was gone before it was ever read, because the FIRST tap
    after a bubble lands is muscle memory. All of that is deleted. A standing
    ask now survives clicks anywhere, petting, dragging, stray keys and the
    heartbeat (its `askReady`/`askOpen` gates already refuse every act), and
    the glass hold keeps barks parked under it. The ONLY closers: a chip (the
    answer), Esc (the deliberate "not now"), `hide`/`gone` (the screen killed
    her), and the `classStart` latch in `asks.js note()` - which is NEW there,
    because "the player pressed Begin" used to close the strip as a side
    effect and trap 59/97 still forbid a chip strip over a live board. Every
    non-chip closer spends no cadence and banks the question (`rememberReask`).
    If you find yourself adding a new dismiss path, it is almost certainly the
    old bug coming back with better manners.

119. **THE BUBBLE-HOLD SCALE IS MODULE-LEVEL, MULTIPLIES THE CURVE, AND NEVER
    AN EXPLICIT HOLD** (`widget.js sayHoldMs`, owner option 2026-08-25: "make
    the bark bubble permanence time an option in the options"). `holdScale`
    (0.6..3, default 1) lives beside `sayHoldMs` because the helper is a pure
    module export with no instance to ask; the widget seeds it from
    `saved.holdScale` on boot, `setBubbleHold` on the handle (and the emi
    controller) is the one writer after that, and the blob persists it ONLY
    when it is not 1. It scales `max(floor, grown)` and NOT the explicit-ms
    argument - `ASK_HOLD_MS` is an hour because a question waits (trap 118),
    and a player who chose "Quick" did not choose quick questions. The options
    page's Mascot group is the UI: the page's one LOCAL row, no protocol key,
    no echo, never `pending` - `shell/settings.js` reaches the controller
    through a getter because the mascot mounts async, and falls back to
    `store.merge('emi', ...)` so the choice lands even when she never did.

120. **A PURCHASE IS AN ECHO, AND THE WATCHDOG NEVER GRANTS** (economy wave,
    `shell/prizecounter.js`). The counter is trap 1 with money on it, which is
    the one place the echo law actually has teeth. The press sends `prize-buy`
    and paints a WAITING state; `settle(frame)` - fed only by the host's
    `wallet-result` - is the ONLY thing in the page that may move the wallet,
    the inventory or the lever unlocks. The 6s watchdog clears the waiting look
    and says the counter has gone quiet; it does **not** grant, refund, retry or
    re-send, because a page that guessed would sell one token twice. The same
    rule reaches one rung up in `shell.js`: `walletEcho` is written ONLY by
    `payout-result` / `wallet-result` and cleared by the next `meta` snapshot, so
    it is an overlay ON the host's truth and never an optimistic paint. If a
    balance ever moves without a frame arriving, the bug is a write that skipped
    `settle()`, not a lost echo.

121. **A GRADE LETTER IS STRING-COMPARED IN FIVE PLACES, AND `S+` BREAKS FOUR OF
    THEM** (economy wave, `core/grades.js`). Adding the S+ letter is one line in
    `gradeClass`; making it WORK is a sweep. `/^[sab]$/i` does not match `'S+'`
    (shell.js's `endMoment` - an honours run would fire EMI's FAIL pool),
    `g === 's'` does not either (ceremonies' `payoff()` - the jackpot silently
    demoted to the stamp floor), `GRADE_PITCH['s+']` was undefined (the chime
    rang at PASS pitch), `.grade.s+` is not a selector CSS honours unescaped
    (every S+ badge painted bare), and `letter === 'S'` on the student ID did not
    count the best day the player had ever had. Hence `gradeKey()`: `'S+'` ->
    `'splus'`, and every class name goes through it. S+ is deliberately NOT in
    `GRADE_ORDER` and `gradeRank()` answers -1 for it, so the cap ladder
    (`capAt`, `capsRaised`) is untouched - it is a letter above S, not a new rung
    in the ceiling.

122. **THE ROOM SCENE REPLACES THE DOOR CARD, SO EVERY CARD CONTROL OWES A
    SECOND HOME** (economy wave, `shell/lever.js`). Nine of ten rooms are
    painted now, which means the door card is the EXCEPTION and anything mounted
    only on the card is invisible to almost everybody. The Extra Credit lever
    found this the hard way: card-only, it would have shipped as a feature the
    Prize Counter sells a rung for and the player can never see. The fix is one
    shared painter with two hosts (card + `room.js` apron), never two copies -
    two implementations of a three-way switch is two chances for the labels, the
    lock lines and the unlock rules to drift apart. When you add a control to
    `popCard()`, ask what the ten painted rooms do with it before you finish.

123. **A LEXICON KEY THE HOST DOES NOT SHIP FAILS SILENTLY, IN ENGLISH** (economy
    wave integration). `t(key, fallback)` is total: a key with no
    `NeutralLexicon` row behind it renders the fallback and the room reads
    perfectly, so a whole screen can ship with nothing a mod or a translation can
    ever re-voice and no play-test will notice. This wave built both halves at
    once and they disagreed about eight spellings - `prize_trade` against
    `prize_buy`, `prize_reason_poor` against `prize_poor`, one `lever_hint`
    against the three per-rung `lever_*_hint` rows, `grade_s_plus` against
    `grade_splus` - plus twelve host rows (`tickets`, `token`, `payday` and the
    rest) that nothing on the page ever asked for. The rule: **the PAGE's call
    sites are authoritative**, because they are the thing that runs;
    `core/lexicon.js` mirrors them as the offline fallback, and the host's table
    is exactly that set plus the per-sku `nameKey`/`blurbKey` rows the catalog
    projects. Those per-sku rows are the ones no grep of the page can find, since
    they arrive as `item.nameKey` off the wire and never appear as a literal
    anywhere. `econtests/test-seam.mjs` reads both sides at once and fails on a
    drift; run it whenever a key moves.

125. **A HOLD FIRED BEFORE THE FIRST TOUCH IS PARKED, NOT DROPPED - AND ON THE WEB THE
    CAMPUS IS BUILT UNDER THE SPLASH** (soundtrack, 2026-08-27). `audio.js` drops every cue
    that arrives before its context exists (`ensureContext()` is null until the first
    pointerdown/keydown; the desktop host promises `autoplayOk` so it never sees this). On a
    browser host the shell builds the campus ~400ms after init, a whole knock BEFORE the
    first pointer, so `ost.enter('campus')` and `campus_idle` both fired into no context and
    were dropped - and a hold has no re-fire, so the web campus was silent until the next
    screen. Reproduced headless (Edge, vendored tree served locally): `ost: ost_campus in`
    logged at 430ms, `[audio] context up` at the first click, no element ever made.
    `audio.js` now keeps a pre-gesture `hold` by slot (`pendingHolds`) and replays it from
    `onGesture` once `ac` exists; `stop` on the slot and `stop_clips` forget it. A one-shot
    fired early is still spent (trap 70). `ost.js` law 6 (never under lite) went with it:
    a track streams from an element, nothing is decoded, and the desktop rung is AUTOMATIC
    (eight flash windows = Balanced), so the gate made the music vanish whenever the app
    was busy, with nothing in the log. `createOst({ lite })` is accepted and ignored.

126. **THE UPRIGHT CAMPUS IS TWO TURNS AND ONE REGISTRY** (upright wave, 2026-08-28,
    `shell/campus.js` + `styles.css` "UPRIGHT CAMPUS"). On a phone held portrait the PLAN
    turns and the player does not: `planUpright()` (mobile AND `data-arc-orient="portrait"`,
    cached, dropped on every `onDeviceChange`) swaps the viewBox `0 55 1440 810` ->
    `0 0 810 1440` and hangs `translate(-55 1440) rotate(-90)` on ONE wrapper,
    `g.campus-orient`, which every plan layer now lives inside. Everything a human READS
    takes the opposite turn back through a registry of `{node, on, off}` rows built from the
    FROZEN tables (never `getBBox` - the DOM double has no layout), stood up or laid down by
    one `applyOrientation()` inside `fitPlan()`. Sprites are the exception: ghosts and the
    walker append `spriteTurn()` at every write site instead, because their transform is
    rewritten per frame. `showBoard()` no longer arms `requireOrientation('landscape')`;
    per-class gates and `orientgate.js` are untouched.
127. **A ROTATE ABOUT THE WRONG PIVOT SLIDES A THING SIDEWAYS, IT DOES NOT JUST TILT IT.**
    `rotate(90 px py)` is a translate as well as a turn for every point that is not the
    pivot, so a plate pivoted 12 units off its own centre lands 12 units OUT OF ITS ROOM,
    and it reads as "the label moved", never as "the pivot is wrong". Every row in the
    registry pivots on its own box centre (`boxCentre(textBox(...))` / `boxCentre(unionBox
    (...))`) and re-places with `to:`, which is then a plain subtraction. The corollary: a
    name and its RM number must turn as ONE wrapper - turned about separate pivots they
    cross each other.
128. **`transform-origin` IN USER UNITS IS MEANINGLESS ONCE A FILE HAS TWO viewBoxes.**
    `.campus-clockhand` pinned its hands with `transform-box:view-box` +
    `transform-origin:1310px 156px`, and the upright viewBox starts at y 0 where the
    landscape one starts at y 55 - so the same declaration names two different points and
    the minute hand orbited a point off the plan entirely. The cure is a reference box
    that cannot drift: `transform-box:fill-box` + `transform-origin:50% 100%` is the
    hand's own pin in either world. (A zero-AREA bbox falls back to view-box, so this
    only works because the hands have height.)
129. **AN `<svg>` CHILD WRAPPER SILENTLY KILLS EVERY `> g` RULE ABOVE IT.** The entry
    reveal is `.campus-stage.enter svg.campus-plan > g { animation:campus-fadein }`, and
    re-homing the layers under `g.campus-orient` left that selector matching exactly one
    element - the wrapper - so the whole school faded in as one block with no stagger and
    nothing in the console. FIX ADDITIVELY, never by editing the old rule (landscape and
    desktop must stay byte-exact): `> g.campus-orient { animation:none; }` plus
    `> g.campus-orient > g { ...the same fade... }`.
130. **`.campus-idcard` IS NOT HIDDEN ON A PHONE, AND THE MAIL CHIP OWNS THE BOTTOM-RIGHT.**
    The `@media (max-width:760px),(max-height:600px)` block FOLDS the ID into a 185x52 tag
    at the bottom LEFT (trap 100); only `.campus-crest` is `display:none`. `shell/mail.css`
    pins `.arc-mailchip` into the bottom-right on the same query, and EMI's first-run spot
    (`vw - 158`) is the middle of that same band. Any wave that plans to "use the empty
    bottom band" is planning against three things that are already standing in it.
131. **THE OPEN PORTRAIT BOARD IS THE WRAP ITSELF, ACTING AS THE SCRIM.**
    `.campus-boardwrap:not(.collapsed)` goes `position:fixed; inset:0` and centres
    `.campus-boardsway` inside itself, so the wrap's rect is the WHOLE VIEWPORT and a test
    that measures `.campus-boardwrap` for the panel gets 390x844 and "proves" an overlap
    with everything. Assert on `.campus-boardsway`. `armBoardScrim()` (shell.js) reads the
    same fact: it only acts when `ev.target` IS the wrap, and it closes by clicking
    `.campus-boardtab` so `aria-expanded`, the pulse and `handlers.boardToggle` stay the
    plaque's business.
132. **THE TURNED PLAQUE IS 74px WIDE AND THE SKY RAIL IS 69px ON A 360px PHONE.** The
    collapsed board is the chains plus the plaque turned -90deg into the plan's own sky
    strip, and that strip is `viewBox` y 55..210 scaled by the `meet` fit - 74.6px at 390
    wide, 68.9px at 360. It overhangs the architecture by 5px on the narrowest phone in
    range. Known, cosmetic, NOT a regression: measure before "fixing" it into a different room.
133. **EMI IS PLACED IN VIEWPORT FRACTIONS, SO A ROTATE CAN PARK HER ON THE HINT.**
    `#arc-emi` is page-level and user-positioned, outside the stage and outside everything
    the upright pass re-homes, so her remembered spot is re-read against the NEW viewport
    and can land on `.campus-hint` (or on the props) after a turn. Landscape's own version
    of this is fixed by hand (`html.arc-mobile[data-arc-orient="landscape"] .campus-hint
    { right:170px }`). The general case is OPEN - it belongs to the widget, not to the
    campus chrome.
134. **`DISCORD_COMMAND` IS HALF OF A TWO-REPO CONTRACT, AND THE OTHER HALF IS A BOT.**
    `games/registry.js DISCORD_COMMAND` maps a game key to the slash command that opens
    that class as a Discord Activity. CCP-Server `bot/arcademy-activity.js`
    (`commandDefinitions()` / `handleCommand`) registers exactly those names against
    exactly those keys, and the Activity link it hands back is
    `custom_id=arcademy-<gameKey>`. Change one side alone and a command silently opens the
    wrong room (or the campus): **both repos move in the same wave, or neither does.**
    Three rules that fall out of it:
    - **The command is derived from the KEY, never from `gameName()` or a lexicon row.** A
      mod re-voices the SENTENCE (`punchcard_unlocked_discord`) and may never rename a
      command, which is why `{cmd}` is a substituted token and not part of the copy.
    - **A key with no row simply has no line.** `misdirection` is retired: no row, no
      command, no launch, and the entitlement route answers `complete:false` for it anyway.
      The ceremony omits the third row rather than printing an empty command.
    - **`init.launchGame` is a request, not a grant.** It is gated by the page's own
      `isUnlocked()` - the same read the campus door uses - so this door can never be wider
      than the one on the quad, whatever a host sends. The hosted shells wall on the
      server's `complete` before the page boots; the page's toast is the belt-and-braces
      half, and it lands the player on a campus rather than on a dead end.
135. **iOS UNLOCKS AUDIO ELEMENTS ONE AT A TIME, IN THE GESTURE, SO THE ELEMENT PATH IS A
    POOL** (owner report, 2026-08-28, iPhone: bells ring, the thirteen soundtracks never
    do). Safari's autoplay rule is PER ELEMENT: an `Audio()` minted after the first tap
    still refuses `play()` with NotAllowedError unless the call itself runs inside a
    gesture handler, and `playClip` swallows that rejection ("a refused clip is not an
    error"), so every NEVER_BUFFERED cue - the `ost_*` tracks, the five beds, the
    thirty-six `pa_*` lines - was silence on a phone while the pre-decoded one-shots,
    riding the already-unlocked AudioContext, were fine. `shell/audio.js` now builds
    POOL_SIZE (CLIP_VOICES + 2 spares for fade-out overlap) elements in `onGesture`, each with a 2ms silent-WAV data url and a
    `play()` fired right there, and `playClip` borrows one instead of minting. Three laws
    come with it: a pooled element's `createMediaElementSource` is made ONCE and must
    NEVER be re-created (a second call on the same element throws InvalidStateError -
    that law is why the old path could not reuse anything), so teardown disconnects the
    node and keeps it; nothing may be awaited between `ensureContext()` and those
    `play()` calls, because a promise hop leaves the gesture task and iOS is counting;
    and the listeners are added per fire, so `killClip` takes them off again or a reused
    element ends up a hundred handlers deep. A clip that finds the pool dry still mints
    and is still refused on a phone - accepted ceiling, six is the voice cap.

134. **AN ANIMATED GIF THAT COVERS THE STAGE IS THE ONE NODE A PHONE CANNOT PAY FOR (measured
    2026-08-28).** Blink decodes every gif frame on the renderer main thread and then re-rasters
    every device pixel the image covers, per frame advance; at DPR 3 the 150vmax spiral square is
    ~14 Mpx and a full-bleed burst ~3 Mpx, PER FRAME. The 4x-throttled phone rig put ONE bundled
    spiral gif painted as a url-string wash at 3.1s of GPU-process time per 8s (the CSS conic or
    the live loom: 23-126ms), and Instant Recall at 33fps with three 500px spiral gifs animating
    behind a SPIRAL question. The half-size-box-under-`transform:scale(2)` trick does NOT help
    (Chrome rasters at the ideal scale). The cure is THE STILL PLATE (`engine/util.js plateEl`):
    the gif's first frame drawn once into a small canvas (long side 640) that the stylesheet
    stretches over the element, with an optional compositor-only spin (`.ae-wash-plate-spin`,
    worn only while the hold is live and never under reduced motion). It is read by the touch
    rung only - `sustained.js paintUrl` for a url-string spiral wash (IR gif-ring decoys, the
    WebGL-lost floor), `oneshots.js` for a full-bleed .gif burst, `instant-recall/spirals.js
    spiralStillThumb` for the SPIRAL option faces - and gates on `html.ae-touch` /
    `ctx.platform.isTouch`, never UA. Desktop keeps the animated gif byte for byte (digest-
    verified). Two rules that fall out of it: a url-string wash on touch must go through
    `paintUrl`, never write `backgroundImage` itself (the plate would be orphaned under it);
    and `plateMount` is idempotent per url because the decks re-trigger a held wash several
    times a second and a re-decode per trigger is the bug it exists to cure. The related
    decoder caps: `MONTAGE.VIDEO_TILE_CAP_TOUCH = 1` (two playing tiles measured p95 33ms, the
    30Hz lock) and `PLAYTEST.VIDEO_TILE_CAP_TOUCH = 1` in Lost and Found (the LITE four were
    EIGHT playing players with the wrap reps).
136. **A SAME-ORIGIN URL IS NOT OUR OWN FILE, AND A BARE PATH IS NOT REMOTE (Discord Activity,
    2026-08-28).** Inside the E.M.I. Activity the frame lives on `<app_id>.discordsays.com` and
    the web shim rewrites every Scrolller row to the portal mapping `/scrolller-media/<sub>/...`
    - our origin on paper, a proxied CDN in fact. Two page laws misread that shape: (1)
    `inventory.js isLocalUrl` calls a RELATIVE url local, so when the shim handed over the bare
    path `absorbRemote()` `continue`d on every row, `absorbLocal()` (blob:/data: only) took none,
    and every ordinary `claim()` - Anomaly, Impulse Control, Composure, Deja Vu, Echo, Instant
    Recall - dealt the six `ae-ph-N.svg` tiles all class (the owner's striped Anomaly board).
    Cure = the shim now hands ABSOLUTE same-origin urls (cclabs-web #112). (2) `warmable()`,
    `instantUrl()` and `vet.js instant()` called anything on OUR ORIGIN "ready now", so inside
    the Activity nothing was ever prefetched or probed: a LAN desktop never noticed, a phone on
    5G met every card cold (striped back while it buffered, dead index posts dealt unvetted).
    Cure = `inventory.js isOwnPageUrl`: same origin AND under the document's own directory
    (`/arcademy/` on the web, `/arcademy/v/<stamp>/` in the Activity) is ours; same origin
    elsewhere travels. `isLocalUrl` is unchanged on purpose - it is the canvas-taint law, and a
    proxied row must stay OUT of `canvasSafe` pools.

137. **AN OUTFIT THAT DRAWS ON THE GLASS NEEDS AN `over-<pose>.png` OR THE FACE EATS IT (swim
    goggles, 0828).** `install_outfits.py` pastes the ORIGINAL screen rect over every outfit frame,
    so anything the generator drew across the tube's glass never reaches `body-*.png`; the widget
    then paints the face canvas on top and the piece is gone twice over. The road: ship
    `art/emi/<outfit>/over-<pose>.png` (same 192 box, transparent except the piece) and
    `emi/widget.js` `overSetFor` paints it in `.emi-over` ABOVE the face canvas, BELOW the bubble,
    mirrored with the body, probed once per outfit and cached (a missing file is a silent none).
    The Locker WEAR tile composites it too. Do NOT reach for z-index on the body sprite: the face
    must stay over the body for every other outfit. Follow-up: the script should emit over-*.png
    itself.
138. **A VISIT IS NOT A FIELD TRIP: DURING A CAMEO, `pointerdown` ON HER IS THE PAT (IC EMI
    CAMEOS, 2026-08-29).** Trap 75's law is that a touch on EMI always wins, which on a trip
    means `cancelTrip({stay:true})` - she stops what she is doing and you get her back. A
    cameo is the opposite shape: the press IS the interaction the class asked for, and
    cancelling would delete the one beat the game is waiting on. So `onDown` tests `cameo`
    FIRST, above the trip branch, calls `visitPat('pointer')` and returns - no press latch,
    no pointer capture, therefore no drag and no `dropAt` that would move her home spot to
    wherever the bubble happened to be. Two consequences to keep: the go key
    (`handle.pat('key')`) must stay indistinguishable from a finger apart from the `src`
    string, and the cameo branch must never grow a second `preventDefault` call - both roads
    share `stopNativeDrag(ev)`, which is what keeps trap 98's one-call grep honest.
139. **A CAMEO REFUSAL IS A SYNCHRONOUS `null`, AND IT IS THE NORMAL ANSWER.** `visit()`
    returns null on the same tick for a docked or disabled EMI, a live say, an ask or a
    channel owning the glass, a visit already in flight, a global suspend, an off-screen
    rect and an 8s floor since the last visit ENDED. It is never a promise, never a throw
    and never a queued visit that lands later: a class that awaits it, or that draws its
    bubble differently while it "waits", has already broken. Deal the ordinary bubble in the
    same statement. The floor is measured from the END so two classes back to back cannot
    stack her, and the ONLY place it is stamped is the shell's `finish()`, so every road out
    (pat, timeout, cancel) pays it.
140. **THE CAMEO COUNTERS LIVE IN THE ONE `emi` BLOB, NOT A NEW STORE KEY.** `visits`,
    `visitPats`, `visitsIgnored` and `filesOpened` are ordinary `blankStats()` fields, so
    they ride the same single writer as `pets` and `zones` (trap 96) and need no branch in
    the reader. Bank them at the moment the player acts, never at the exit: a freeze or a
    class teardown between the pat and the trip home must not un-count a real touch. The
    GESTURES are the opposite - a pat emits `visitPat` plus a kind-keyed
    `visitPatStowaway` / `visitPatDossier`, and the dossier pair is DEFERRED until she is
    home and lit, because the shock chain and the polaroid own the glass and a line said
    over them would be cut by the trip. `visitSnub` fires at the timeout with the running
    `ignored` count, and `voice.js` reads it through `visitsIgnoredAtLeast:N`.
141. **A CAMEO IS A DEBT, NOT A ROUND (IC, `games/impulse-control/cameo.js`, 2026-08-29).**
    A cameo runs INSTEAD of the bubble at the cursor and the cursor does not move: `nextBubble()`
    increments `S.idx`, so the deck latches `S.cameoOwed` and pays it at the TOP of `nextBubble()`
    with `dealCurrent()` of the same index (the suite asserts reveal count == plan length on both
    cards). Two more shapes the suite caught: (a) `teardown()` -> `handle.cancel()` ->
    `onDone('cancel')` schedules the ring's removal on a deck timer that `teardown()` then
    kills, stranding a dead ring in the dish - so every node she leaves behind goes in the
    `parting` register and `sweepParting()` runs unconditionally in teardown; (b) a stowaway's
    load+slide is NOT a round for the other decks: `dealCurrent(onLand)` withholds `decks('load')`
    /`decks('slide')` on her trip and `stowawayLanding` fires the withheld load only on the
    refusal fallthrough, so the trickster never tells on a bubble that will not reveal. The
    trickster and the cameo share ONE cadence ledger (`S.houseLastIdx/Who`, `opts.house`)
    with a self-exemption: a deck never blocks itself through it, so wiring the ledger cannot
    lower the trickster's rate on a class with no cameo. `freeze()` tears the cameo down
    ITSELF before `cam('pause')`, or `onDone('cancel')` would deal on top of `thaw()`'s re-deal.

142. **THE FACE IS THE FLOOR OF HER STACK, NOT ITS CEILING - AND THE OUTFIT ART INSIDE THE
    BEZEL IS GONE, NOT BURIED (owner, 2026-08-30: "the coat behind the screen, it should be
    over it").** Trap 137 ruled that an outfit which draws on the glass needs an
    `over-<pose>.png`. This is how far that actually goes, measured. `install_outfits.py` did
    not paste back the GLASS rect - it pasted back the screen AND THE BEZEL, a block running
    about x271..679, y231..608 of the 859x869 canvas (per pose: `body` 272..679/231..607,
    `idle` and the four sways 271..679/233..608, `pet` and `sad` 274..678/232..605, `shock`
    274..677/233..604, `smug` 273..677/232..605). Inside it ALL FORTY shipped outfit frames -
    varsity, labcoat, cheer, swim, ten poses each - are BYTE-IDENTICAL to `art/emi/body-*.png`,
    and they already were at the install commit (`1ccb2012a`), so there is nothing to recover
    from the sprite and no un-pasted original anywhere in history. Measured on labcoat
    `body-idle`: the coat is a solid 72px white column at x=270 and again at x=679, and
    EXACTLY ZERO white pixels across x271..678 - a shape cut dead flat at both edges of the
    block. What the owner is seeing is a collar that was ERASED, not a collar that lost a
    z-fight, and no amount of layering brings it back.
    THE RULE HAS TWO HALVES. (a) The wardrobe is the topmost thing in her composition bar her
    own hands (`.emi-fx`) and her words: `.emi-over` is z2, the takeover channels (`.emi-glass`)
    are z1, and `.emi-screen` now carries an explicit `z-index: 0` so the FACE CANVAS SITS
    UNDER THE GARMENT BY RULE rather than by `appendChild` order. Never raise the canvas, and
    never cure a buried prop by re-ordering the DOM. (b) The only road back for the other three
    sheets is NEW ART: one `over-<pose>.png` per pose, same 859x869 canvas, transparent
    everywhere except the part of the garment that falls inside that block. The swim sheets are
    the reference - RGBA, 10-12KB, and 100% of their opaque pixels land inside it. Do not trace
    it out of the sprite; there is nothing there to trace.
    AND THE OVERLAY PATH IS NOT A SWIM PATH, though swim is the only sheet that has ever run
    it. Every verdict in `armOver` is keyed by the PROBE URL, so four wardrobes arm, cache and
    refuse independently - but `paintOver` used to assign the new `src` and un-hide the node in
    the same statement, and an `<img>` keeps painting the frame it has already decoded. Harmless
    with one sheet in the bundle; the OLD outfit's prop on her face for a frame or two the day a
    second one ships. A change of SET now stands the layer down until the new frame reports
    itself decoded, while a pose step inside one set never blinks (`preloadOver` already holds
    its ten frames). Proved in a real browser, because a DOM double cannot see a composite: the
    swim goggles render as the GARMENT over a `0_0` face, and the swim bytes served at
    labcoat's `over-` url light a SECOND outfit's layer end to end.

138. **A BAND SIZED FOR A 72px STRIP IS A BAND THAT EATS THE SCREEN WHEN IT BECOMES A FLOOR
    (portrait facilities, 0828).** `.asc-panel` clears `--arm-band-h` because it rises out of
    the carpet, which is honest while the carpet IS a 72px action strip. In portrait it is not:
    fit() hangs the apron off the painting's floor line and runs it to the bottom of the glass,
    so a 390x844 phone fitting the 1376x768 plate by WIDTH puts that line at y=181 and hands the
    band 663px. `100% - var(--arm-band-h) - 16px` is then 165px of panel for a shop whose content
    measures 1790px. The cure is not a shorter band (that leaves a 550px dark void on the wide
    shot): on an upright phone **an open panel IS a close-up**, so `apronWanted()` in `scene.js`
    steps the carpet off for it through the same `.asc-bar-away` the zoom already uses, and the
    panel is sized off `--asc-art-line` - the floor line in SCREEN pixels, published by the same
    fit() - instead of off the band. The catch that is easy to miss: `publishBand()` must write
    **zero** to `<html>`'s `--arm-band-h` while the carpet is away, because `.arc-rs-stage`
    (styles.css) pads its bottom by it and a body-level spotlight would otherwise reserve four
    fifths of the phone for a carpet that is not on screen. Landscape and desktop publish the
    real number in every state, which is what keeps their rects identical.
139. **THE TWO CHASSIS SHARE `rooms.css`, SO HALF OF A PORTRAIT FIX ARRIVES FOR FREE AND THE
    OTHER HALF NEVER DOES.** `.asc-bar` wears `.arm-bar`: PR #382's portrait apron block (the
    grid, `align-content:start`, the 12px top padding, the 48px/11px ghost slab) applied itself
    to the facilities the moment it merged, which is why the back slab was already at the top of
    the band and reading `Back to campus` in full before this branch touched anything. What did
    NOT come across is everything keyed on the chassis's own prefix - the stage anchor
    (`.asc-stage` vs `.arm-stage`) and the overlay - because room.js has no `.asc-panel` and the
    scene had no header card. Read the sibling's diff before writing the same fix twice, and
    check which half is `.arm-`.

143. **A TOAST FIRED WHILE THE BOOT SPLASH IS UP IS A TOAST NOBODY EVER SEES.** `#arc-toast`
    is z 60 and `.arc-loader` is z 70, and the toast's own life is 2.2s while the splash's
    floor is longer than that - so `shout()` called from anywhere inside `createShell`'s
    construction (the `launch_card_locked` refusal had done this since the day it was written)
    plays out and expires behind the splash. The cure in shell.js is `shoutOnScreen()`: say it
    now if `#arc-loader` is not on screen, otherwise park the one line in `pendingShout` and let
    `onSplashDone`'s `flushPendingShout()` speak it when there is a screen to land on. A page
    with no loader node (every headless suite) answers "not up" and behaves exactly as before.
144. **`node --check` DOES NOT CATCH A BLOCK COMMENT YOU CLOSED EARLY, UNLESS THE FILE IS
    CHECKED AS A MODULE.** A doc comment containing a literal `games/*/index.js` ends at that
    `*/`; the rest of the sentence then parses as loose statements, which is legal script and
    `node --check games/registry.js` in a plain directory says nothing. It only became
    `SyntaxError: Unexpected identifier 'shell'` when the same file was checked from a directory
    whose `package.json` carries `"type":"module"`. Run the §6 syntax pass against the rig's
    COPY (which sits under a `module` package.json), never against a bare path, and never write
    `*/` inside a comment - say "of every game module" instead.

## 5. The game module contract (short version)

```js
export default {
  key, family, meaty, flagship, timeBudgetSec, title,
  manifest: { effectsConsumed:[], assetNeeds:{}, boardSizes:null, keybinds:null,
              settings:[], peek:false, setup:false },
  create(ctx) { return { setup?(), start(classSpec), pause(), resume(), suspend(on), destroy() }; },
};
```
**THE SETUP DOOR** (`manifest.setup:true` + an `instance.setup()`, SORT 2026-08-23) is the one
thing a class may put between `create()` and `start()`. The shell awaits it and reads exactly
one value: **`false` means the player backed out** and walks to campus through the ordinary
leave path (teardown, `class-left`, no grade); true, undefined, a throw and a rejection ALL
start the class, because a broken door must never strand a player on an empty stage. The
enrollment intro still runs FIRST (the school speaks before the game does), the class clock is
still armed in `beginPlay` and nowhere else - so a door costs the class no time - and Esc while
it is open is the ordinary leave confirm, one new rung above the pause rung. It is the FUNCTION
that decides, not the flag: a mismatch between them is logged and the function wins.
`ctx = { root, engine, assets, lexicon:t, caps, rng, settings, keys, peek, ceremonies,
exits, store, endClass({metrics:{composite}, hardGates?, zen?, flavorXp?, share?, assists?}), log }`
plus the additive read-only projection: `platform` (init's `{isTouch, hasHaptics, host}`),
`motion` (`{reducedMotion, motionLevel}`), `audioAudible` (resolved `SubAudioAudible` - FALSE
means a cue is mixed but inaudible, so carry a visual tell), `words` (a COPY of the day pool),
`absorb(word)` / `sessionWords` (trap 24), and `keys.panicKey` (the projected panic key name,
a launch-time snapshot - see trap 19).
`ctx.exits` is a shell PRIMITIVE like peek and ceremonies, and it is pure decoration -
neither call wires a handler or can move a screen: `exits.sign(btn, {dir, quiet})` dresses
a button as the lit arrow board (TERMINAL screens only - a rules sheet, a debrief; a sign
on a live board fights the board) and `exits.bar(nodes, {card:true})` returns a sticky
footer row. The CSS is the shell's, so ten classes cannot drift apart (trap 46).
`ctx.emi` is the SECOND mascot seam (IC EMI CAMEOS, 2026-08-29) and it obeys the same law
as `ctx.mood`: a class may BORROW her, it may not drive her, and it never imports `emi/`.
`ctx.emi.visit(spec)` walks her into a bubble the class is already showing and answers a
handle `{pat(src), end(), cancel()}` or **`null`, synchronously** - null is the ordinary
answer (one visit in flight page-wide, an 8s floor since the last one ENDED, an ask or a
takeover or a suspend owning the glass), so deal your ordinary bubble on the same tick and
roll nothing again for that slot. `spec = {kind:'stowaway'|'dossier', rect:()=>rect, face?,
phosphor?, waitMs?, patChain?:'love'|'shock', stayMs?, onArrive?, onPat?, onDone?}`; the
rect getter is called in the dark at fire time (trap 73's law), she lands CENTRED ON the
rect rather than beside it, and `onDone(reason)` fires exactly once for `'pat'`,
`'timeout'` or `'cancel'`. `ctx.emi.fileTag()` answers the seep's subject code once the
page is post-reveal, else null. Both are opt-in and null-safe (`if (ctx.emi) ...`) because
rigs stub ctx without them. The shell cancels a live visit from `clearScreen`,
`teardownClass`, `applySuspend(true)` and `pauseClass(true)`, so a class never has to.

`classSpec = { gradeTier 1..4, seed, timeBudgetSec, retake }` - `retake` is true when today
already has a row for this class (trap 23). The seed is unchanged on a retake, on purpose:
the day's script IS the day's script.

The per-class engine handle carries the pinned surface (`setHeat/fire/sustain/stop/setpiece/
beat/ceremony`) **plus** the engine's additive helpers as pass-throughs: `setPhase`, `armTail`,
`rewardRoll`, `isPlainBeat`, `plainShare`, `cadenceMs`, `channels`, `diagnostics`. Only
`fire`/`sustain` are kind-addressed, so only those two are fenced by `effectsConsumed`; the
rest read clamped state or drive the director the class already drives. A NULL engine answers
`undefined` for all of them, which is why a game still needs its own fallback - presence on
the handle is not a promise of an effect.

A game must not: import another game, touch `bridge.js`, re-expose a global setting (the
settings page skips + logs it), grade itself, or call `endClass` twice (the runner ignores
the second call and logs).

The five `games/*/index.js` are **real games** now (Semester 1 plus The Deep End, the first
Semester III class brought forward) - the placeholder stubs are gone. The shell suite therefore keeps a fixture of its own rather than driving a real game's
UI: see §6.

## 6. Verifying changes (no app UI — the owner is remote)

**A PHONE VIEWPORT IS A MERGE GATE for campus props, rooms and overlays.** Any PR that
touches the campus furniture (`.campus-postrow` and the two `.arc-*prop`s), a room chassis
(`shell/room.js` + `rooms.css`, `shell/scene.js` + `scene.css`) or an overlay that carries a
sticky exit bar (corkboard, mailbox, Bugle) gets a **shot at 844x390 AND at 667x375** from
the CDP screenshot rig before it merges, plus a 1600x900 shot proving the desktop rects did
not move. Three of the five bugs in the 2026-08-25 web/mobile wave were invisible at desktop
size, and one of them - both post-row props stacked on a single pixel, hanging off the bottom
of the window - had been shipping on the DESKTOP too, unseen, because nothing ever measured
the row.

The rig lives at `scratchpad/web-mobile-diag/`: `server.mjs` serves `site/` on
127.0.0.1:8731 with stubbed `/api/arcademy/*`, and `shoot.mjs OUTDIR BASEURL shots.json
seed.js` drives headless Chrome over raw CDP - one entry per shot (`w`, `h`, `query`,
`script`, `probe`, `touch:true` for touch emulation). Four things it will bite you with:

- **`site/` GOES STALE.** It is a COPY of the web tree, not a link, and it is the whole
  trap: re-copy this folder into it (keeping `web/`, `web-shim.js`, `SYNC.json` and the
  `web-shim.js` script tag in `index.html`) before every run, or you screenshot last week's
  bug. The 2026-08-25 copy was 20 files behind and had no Records room in it at all.
- A `pointerup` dispatched on `document` is needed to pass the knock gate, and a seeded
  `arc-web:meta` in localStorage (`seed.js`) to skip Orientation and First Bell.
- `?forcemobile=1` is what makes `isMobile()` true - headless reports a FINE pointer however
  the viewport is sized, so `html.arc-mobile` never lands without it. `?dev=1` on localhost
  enters an unscheduled painted room.
- **MEASURE, DO NOT EYEBALL.** The `probe` expression returns `getBoundingClientRect()` and
  `getComputedStyle` for whatever you name, and diffing a BEFORE run (the touched files
  restored from `HEAD`) against an AFTER run is the only way to prove "desktop is
  pixel-identical". It is also how a diagnosed fix gets refuted: the corkboard's sticky bar
  reaches its flow position at the end of the scroll with or without content padding, so the
  padding that was supposed to clear it bought nothing but dead cork underneath.

Everything here is testable headless. The harness lives in the session scratchpad, not the
repo: it copies this folder next to a `package.json` with `{"type":"module"}` (node treats
bare `.js` as CommonJS, the browser loads them as modules) plus a ~130-line DOM double, then
drives the real modules. Rebuild it when you need it — the recipe is:

- `core/timetable.js` / `core/grades.js` are pure: import and assert directly.
- `shell/shell.js` runs against a DOM double (`createElement`/`classList`/`appendChild`/
  `addEventListener` + a `dispatch()` helper) and a fake bridge `{send, on}` — that is
  enough to click a board row, finish a class and read the report card.
- `bridge.js` needs `window.chrome.webview = {postMessage, addEventListener}` installed
  **before** the import (it captures the transport at module evaluation).
- node 24: `navigator` is getter-only, so `Object.defineProperty` it.
- Fresh `boot.js` instance = `import('./boot.js?instance=2')` (query defeats the ESM cache).

**The shell suite has a fixture class of its own.** Now that `games/*` are real games, none of
them can be driven board -> `endClass` from one synthetic click, and a SHELL case should not
have to fight a game's UI to assert a meta write. The harness drops
`arc/games/test-class/` (the union of what the four retired stubs each proved, with knobs:
`tc_zen`, `tc_fail_gate`, `tc_absorb`) into its COPY of the web root and patches the COPY of
`games/registry.js` with one opt-in hook - `globalThis.__ARC_TEST_GAMES__ = {key: path}`
read at `loadGames()` time. The repo's registry stays a frozen five-entry table: the shell
must never grow a test seam that ships. Cases opt in through an `overrideCalendar`, so every
other case still sees the shipping five-game pool and the seeded boards it asserts against.
Remember `clearTimetableCache()` between boots (trap 25).

Last full run: **276 assertions, 0 failures** (timetable 27, grades 23, shell 48,
bridge+boot 15, **e2e seams 14**, campus 23, **host fixes 20**, time bar + free swim 15,
rake 44, **punch cards 47**), against the live `engine/` + `provider/` modules (the note line
in the shell run says which). The four game suites (`games-dt`, `games-lf`, `games-dv`,
`games-ic`) drive the REAL games and run green alongside it.

**THE PACE re-baseline (2026-08-23, `feat/arcademy-pace`).** Four classes a day plus the
3-punch enrollment and the S double moved every count that names a number: **timetable 32**
(five new cases drive the SHIPPED nine-class pool - four rows every night, the pool walked,
zero back-to-back over 120 nights, one meaty and never two, four distinct periods) and
**punch cards 57** (ten new cases on the derivation: enrol = 3, ordinary day = 1, S day = 2,
a stray `sDates` entry = 0, the enrollment day folded out of both lists, a retake = 0, the cap
at ten, `complete` flipping there, an OLD two-punch blob repriced, and a source-shape check
that the JS and C# formulas are the same three-term sum). Board-row assertions in `shell`,
`bridge+boot` and `e2e` moved 3 -> 4; the ONE that stays 3 is `test-e2e.mjs`'s
`overrideCalendar` day, because a calendar day is VERBATIM (§7) whatever `CLASSES_PER_DAY` is.
The 14 failures that were already red on `feat/arcademy-emi` are unchanged and untouched.

`scratchpad/sort/suite-p/` (2026-08-23, LOT P) is the SORT seam's own: `test-tagged.mjs` (26
cases) drives the real provider against a fake host - per-tag cursors, the seeded dry re-serve,
perSourceMin vs the timeout, thin frozen / empty live, the local path, the prewarm cap, the
probe/library round trip, and `claim()` proven untouched - and `test-setuphook.mjs` (12 cases)
drives the real shell through the setup door (false -> campus, true/throw/reject -> beginPlay,
the clock never armed, Esc = the leave confirm, the enrollment intro first) plus the registry /
campus / lexicon rows. Both are node:test; `run.sh` copies the web root the same way.
`test-sortscope.mjs` (2026-08-28, 10 cases) is the third half, and its subject is THE SORT
SCOPE above: it drives the real provider against a fake host shaped like the WEB shim (a
library row that IS the feed selection) and proves a noise-tagged sub never reaches an
untagged `claim()` - with the unscoped add kept as the RED control, so the harness is known
to be able to see the leak. Plus the frame's optional `scope`/`pile`, the target pile left
alone, the app host never sent a `media.*` key, an already-kept sub never re-parked (the
settings toggle wins), and `selected` carried through `catalog()`. Three cases in the two
older files were ALREADY red before this work (`prewarm is capped at 12` predates the
on-deck rail; the two `test-setuphook.mjs` rows predate SORT's 180s budget and the campus
side field) and are untouched.

`test-punchcard.mjs` (2026-08-23) is the punch-card half: the store's refusal + self-heal,
the 96-char cap on every one of the 30 flavour rows AND the 8 rotating card lines AND their
verbatim presence in the C# table, the face geometry (the uniform fallback, percentage
placement, the manifest fork, a broken entry sanitized field by field), the live strip
(N/10 + a seeded line, turning over to MASTERED + the date) and the crest as the reward, the intro showing once and never again, the ceremony on all three shapes (daily,
enrollment, `justUnlocked`) plus the no-op silence, the door CTA order, and the Records
Office populated and empty. **A boot with no `punchCards` in `init.meta` now opens with an
enrollment intro**, so the older suites seed an already-enrolled school in their `fakeInit`
- a first night is `test-punchcard.mjs`'s subject, not theirs.

`scratchpad/emi-w1/test-channels.mjs` + `proof-channels.mjs` (2026-08-24, W3) are
THE OFF CHANNELS' pair. The node suite (**130 assertions**) drives the real
`emi/channels.js` and `emi/takeover.js` against a fake 2d context, a fake document
and a hand-cranked rAF over a virtual clock - the dials verbatim and frozen, every
`plan()` refusal, the reduced-motion table, the weighted wheel over 4000 rolls,
all eight painters through ten seconds without a throw, the 10s cap, the
screensaver's exemption, the per-session cap, both cooldowns, the caught arc
(snap -> shiver -> offer -> accept -> reveal card -> afterglow, and the decline),
and `destroy()` taking every listener with it. The browser proof (**51
assertions**, port 8752) mounts the REAL widget over the REAL sheets in Chromium
and reads the glass back pixel by pixel: the second canvas landing on
`.emi-screen`'s exact rect, face.js still painting underneath a live channel, a
trusted click and a real keypress each cancelling instantly, the cap and the
saver exemption on the WALL clock, the reveal card's layout and its
`pointer-events:none`, and **zero rAF calls in 1.2s at rest**. Both drive a deck
of their own on compressed dials - the shipped widget grows no test seam.

`scratchpad/askproof/proof-asks.mjs` is the ask strip's browser half (**27 assertions**,
port 8753): the real widget over the real sheets in Chromium, reading the geometry back.
It proves what the DOM double structurally cannot - that `widget.css` parses and all 22
strip rules survive the cascade, that the layer is still `pointer-events:none` with only
the CHIPS live, that the line sits clear above (or below) the strip on all three
orientations, that a strip parked in the right margin stays inside the window, that a
trusted click on a chip is a chip press and NOT a pet or a drag (trap 98 - it caught that
one), that a board row 60px away is still clickable under her (trap 59), and that a14's
field is a real 8-character input that takes focus and submits on Enter.

**Browser pass, not just node.** The suites drive a DOM double, which cannot see a CSS rule
that does not parse, a module that throws on evaluation, or trap 49. The recipe: serve the
web root over plain http, install `window.chrome.webview` through Playwright's
`addInitScript` (bridge.js captures the transport at module scope, so it must exist before
the first import), post a realistic `init`, then screenshot and read `document.styleSheets`
back. It caught trap 49 and two duplicated-copy layout bugs the node run could not.

`scratchpad/asksuite/test-asks.mjs` (2026-08-25, EMI ASKS) is the ask engine's own:
**228 assertions** over the real `emi/asks.js` against a fake widget, the real
`emi/widget.js` strip under the DOM double, and the real `emi/voice.js`. It covers the
five gates (session 3, mid-class, the widget's own refusal, the cadence's two doors, one
ask a sitting), the WORDLESS ignored path and the three ways to reach it (give-up, a
document press, a pet), the store shape and the one-writer law, the name sanitiser and the
`{name}` fallback with its one-a-sitting ration, all four new predicates including the
`{game}` substitution, every dare from the flag to the payout string, a15's fence, and
every question and reaction VERBATIM against a hand-transcribed copy of `EMI-ASKS.md`. It
also greps the seams that are not JavaScript, `test-hostfixes.mjs`-style: shell.js's
additive `dareWon`, the passive exit-aim listener, the C# `DareBonusXp` / `XPSource.Quest`
pair and `BankAccumulator`'s bankable row, impulse-control's additive `newBest`, and the
count of `pointer-events:auto` rules on the EMI layer. It brings its OWN augmented
`document` (one that takes listeners) rather than touching `domshim.mjs`, because every
other suite depends on `audio.js` no-opping there.

`scratchpad/emi-heart/tests/` (2026-08-25, THE HEARTBEAT) is the metronome's three,
**249 assertions**, dropped into a copy of the ask suite's harness (`run.sh` re-copies the
web root into `arc/` - re-run it after every edit or you test last hour's file, which cost
twenty phantom red lines here):
- `test-heartbeat.mjs` (**122**) drives the real `emi/heartbeat.js` with a fake widget, a
  fake emi and `tick()` called by hand over a virtual clock - the dials verbatim and frozen,
  every IDLE_FACE resolved against `chains.js` FACES and every NUDGE_BODY against the real
  `emi.css`, the period and its jitter, the x0.7 after a bark, all four legs of the gate,
  the sequence law over 200 beats (zero repeats, zero doubled speech, zero screen-on-screen),
  both starvation floors, the class wheel's two zero weights, a refused kind being re-drawn,
  `destroy()` taking the interval AND the listener AND the subscription, and an act that
  throws never escaping the tick. Two sections drive the REAL widget: the activity tap
  (raw / play / say / a player verb all stamp it) and `nudgeGaze` under reduced motion.
- `test-classvoice.mjs` (**73**) drives the real `emi/voice.js` with INJECTED pools, which
  is how both new shapes (`on:'heartbeat'` + `when:['campus']`, and `on:'game:<id>'`) are
  proven to resolve before a single line exists: the two floors, the class ceiling and the
  ceremony exemption, the danger gate spending nothing, the payload tokens (substituted,
  skipped, and never printed raw), and the campus cadence proven UNMOVED.
- `test-moodnote.mjs` (**54**) mounts the WHOLE EMI stack under the DOM double with a
  2d-context stub and reads back the face that reached the canvas for every note kind -
  the "every note gets a face" promise, asserted rather than assumed - plus the shell.js
  `ctx.mood.note` / `hold` contract by source shape.
The rest of the suites are unchanged by the wave: 885 passing / 51 failing before it and
the identical 885 / 51 after (those 51 are drift from an older worktree, red on both sides).

**`test-voice.mjs` used to crash before it finished.** `boot.js` binds a document listener
and the shared `domshim.mjs` document is a plain object, so the file threw at its boot
import and reported ZERO assertions while still printing three failures. It now installs
those two methods on its own copy and runs to completion: **115 assertions, 4 failures**,
and the same four fail identically on unmodified `main` (three stale trigger-coverage
fixtures plus the greet-seam one).

`test-hostfixes.mjs` covers the two seams that are not JavaScript. It **parses the real
`styles.css`** and evaluates the `[hidden]` cascade for every element the shell toggles
(trap 27) - the one assertion that would have caught the playtest blocker - and it **greps the
C# host** for the shape of each host-side fix: the panic rung's position relative to the app's
exit branch, the keybinds refusal, the meta store's day trim / atomic save / salvage ladder,
`ShutdownFlush` in `App.OnExit`, the `CurrentReplaced` rebind and the remote-batch generation
guard. A grep is a tripwire, not a unit test - it exists because `ArcademyHostService.cs` and
`ArcademyMetaStore.cs` have no test host of their own, and the precedent is the lexicon-coverage
check in `test-e2e.mjs`. **The atomic-save and salvage paths themselves are covered by source
shape only; the .NET behaviour is unverified by machine and was reasoned through by hand.**

`test-e2e.mjs` is the cross-agent one: a realistic C# init (with `settings.localAssets`) →
board → class → `assets-request` → a host `assets` reply absorbed by reqId → `class-ended` →
`payout-result` → report card, plus the panic-key projection, the lexicon-coverage check
(it greps `ArcademyHostService.cs` for the table) and `shell/audio.js` against a stub
`AudioContext`. Two shims it needs that `domshim.mjs` does not carry: `document`
`addEventListener`/`dispatchEvent` (the shim's document is a plain object, which is why
audio.js no-ops harmlessly in the other suites) and a fake `AudioContext`.

## 7. Known gaps / open questions (v1)

- ~~`arcademy-sfx` has no consumer.~~ **CLOSED** — `shell/audio.js` owns it (trap 18). What
  is still open: every cue is *synthesised*. Real sfx/vo samples (ccp.content is already a
  mapped origin) would replace `playRecipe()` and nothing else.
- ~~`init` carries no panic key.~~ **CLOSED** — projected top-level (trap 19).
- ~~Mods can only skin the rows the host's `NeutralLexicon` declares.~~ **CLOSED** — the C#
  table now mirrors `DEFAULT_LEXICON` key-for-key plus one `game_<key>` row per registered
  game (asserted by the scratch e2e suite). `MergeModTable` still only merges declared keys,
  which is the point: completing the table is the fix, relaxing the filter is not.
  - ~~**Still unskinnable: per-game setting/keybind labels.**~~ **CLOSED** — every row the
    four Semester-1 games can render is in `NeutralLexicon` (147 added: `dt_*` 28, `dv_*` 26,
    `lf_*` 19, `ic_*` 66, plus `absorbed`, `detention_so_close`, `revision_day(_hint)`,
    `mark_hit/near/miss` and the shell's `retake`). The list is derived mechanically - a
    scratch script extracts every `t('key'` call site and `label_key`/`hint_key` in `games/**`
    and diffs it against the C# table; the three keys built by concatenation
    (`ic_err_`, `ic_lie_`, `mark_`) are enumerated in that script, so a NEW suffix in a game
    means adding it there too. Deja Vu's enum key is **`dv_matched_loops`** (`auto` /
    `keep-playing` / `freeze`) - the stub-era `dv_freeze_matched` never shipped and no longer
    exists anywhere.
- **`init.palette` matches** the host's seven keys (`ground/navy/panel/ink/pink/lavender/
  gold`); `shell.js` `PALETTE_TOKENS` also tolerates `accent`/`accent2`/`line` aliases and
  logs anything unknown.
- ~~**One-meaty pools** (see trap 6) fill the meaty slot ~25% of days.~~ **CLOSED** — The Deep
  End is the second meaty class and a 14-day deal now carries one meaty class every day. No code
  changed: the relaxation order is still flagship → meaty → family → no-repeat.
- **Tier promotion** is `tier = 1 + floor(promotions/2)`, cap 4, promotion = S or A, stored
  per game in meta. Simple by construction; nothing in the design pinned a curve.
- **No entry point yet.** `Services/Arcademy/*` and the launch button are the C# agent's;
  nothing in this folder knows how it gets opened.
- ~~`init.protectBrowserVideo` is projected but nothing acts on it.~~ **CLOSED** — the host hooks
  `BrowserMediaService.PlayingChanged` and posts the same `suspend {reason:'video'}` a mandatory
  video gets, gated on the LIVE `ProtectBrowserVideoPlayback` preference rather than the init
  snapshot. The page still needs no new code: it already honours the frame. (The gate properties
  themselves — `ShouldDeferInterruptions` / `ShouldDeferNewVideo` — are polls, and polling a
  class's freeze state would be worse than not having it, which is why the event is the hook.)
- ~~**The punch cards ship on a CSS floor; the art batch is still open**~~ **HALF CLOSED
  2026-08-23.** Landed in `Resources/web/arcademy/art/punchcard/`: nine `face-<gameKey>.png`
  (1208x794), `stamp.png`, nine `crest-<gameKey>.png` (700x700, keyed, transparent) and
  `faces.json` beside them - `--pc-face-src` and `--pc-crest-src` per class plus
  `--pc-stamp-src` now resolve to `url(...)`. **Misdirection has no art on purpose** (the class
  was scrapped): it is absent from `faces.json`, BOTH its tokens stay `none`, and its card must
  keep drawing the whole gradient floor. Still open: `--pc-ribbon-src`, `--pc-desk-src`.
  Four things a reader needs:
  - **`faces.json` carries an `aspect` per class and it is load-bearing.** The faces are
    1208x794 = 1.52141, not the 1.6 `DEFAULT_ASPECT`; without it `background-size:cover` crops
    the face and every measured slot fraction lands somewhere the art did not paint a square.
  - **`data-art="on"` says a FACE shipped, and NOTHING about a crest** - which is why the
    `[data-art="on"] .arc-pc-crest` override was PARKED in a comment while only the faces had
    landed, and the crest floor was re-lit as a drawn gold wax seal (the dark `--pc-well` read
    as a missing image over a painted face; without the stand-in a mastered card revealed an
    EMPTY BOX on its unlock beat, the one moment the card exists to pay out). **Both are now
    UNPARKED**: the nine crests shipped, the original rule is restored, and the stand-in seal is
    gone. It had to go - every crest carries its own thick gold rim and navy depth edge, so the
    drawn rim was a second frame around a framed badge. The rule is now the picture plus one
    `drop-shadow` (which follows the badge silhouette; a `box-shadow` on the square box could
    not). The invariant that replaces the old one: **a face and a crest ship together per
    class**, because a class on the art path with no crest png lands on `background-image:none`
    and is back to revealing an empty box.
  - **The crests are ~700px squares with a transparent margin, and they are BADGES.** Locked
    varsity-pixel DNA (chunky pixel art, gold outline, navy depth, cel shading), one per class
    on its own card's palette, a different shield silhouette each so they collect - and NO TEXT,
    like every other live-drawn layer. They are placed by `faces.json`'s `crest {x,y,scale,
    rotation}` (centre, width, -8.79deg) and drawn `background-size:contain`, so a crest that is
    not square simply letterboxes inside its bay rather than stretching.
  - **No text may ever be baked into the stamp, the crest or the seal** (lexicon law): the count,
    the flavour line and every label are rendered live over the top. The face image is the ONE
    owner-locked exception - it bakes the class logo and the Arcademy logo, which is why the
    drawn name band steps aside under `[data-art="on"]` rather than printing the name twice.
- ~~**The server mirror is a separate PR**~~ **CLOSED** - the mirror is live at both ends
  (PUNCHCARD §5; wire contract `proxy/docs/arcademy-cards-api.md`, client
  `Services/Arcademy/ArcademySyncService.cs`). **Nothing in this folder talks to it or ever
  will**: the page is offline, and the HOST pulls once at launch and pushes after a mint
  (debounced ~6s, `PUT /v2/arcademy/cards`, `X-Auth-Token` + `unified_id`). What the page gets
  for free is a restored card suppressing a repeat enrollment - `enrolledAt` is the only flag
  and it arrives in the blob, in the ordinary `init` projection or in the whole-blob `meta`
  push if the reply is slower than the boot. A card is only ever ADDED to: the merged reply is
  folded in monotonically and every derived number is re-counted from `enrolledAt` + `dates`,
  the same derivation the server runs, so a cold or nonsense mirror cannot talk a card down.
  No identity or no network = the Arcademy behaves exactly as before, on local cards.
- **Nothing consumes `arcademy-fx`.** The engine narrates every primitive on that event and
  only `arcademy-log` is read (by `boot.js`). It is the obvious hook for a future telemetry
  or "what did the engine just do" debug overlay.
