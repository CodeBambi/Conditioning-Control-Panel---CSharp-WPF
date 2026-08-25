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
                   listed in its SCENES table. FIVE ROOMS LIVE: daily_trigger
                   (Homeroom 101, vn-04, chalkboard + painted corridor door),
                   deja_vu (Memory Lab 102, vn-05, the card racks),
                   impulse_control (Discipline Hall 103, vn-06, THE red button
                   under glass + the panelled door), lost_and_found (L&F 104,
                   vn-07, the central shelf bay - NOT the whole 850px wall, a
                   highlight that big reads as a bug) and the_deep_end (The
                   Pool 105, vn-08, open lane water + the ladder). campus.js
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
                   store, no bridge, no key and no lexicon row. + scene.css
                   A ZOOM IS A ZOOM (owner ruling 0825): the apron band belongs
                   to the WIDE shot and FADES OUT on showView(non-wide) (~200ms,
                   `.asc-bar-away`; .arc-reduced cuts; visibility rides the same
                   rule so the slab leaves the tab order), coming back on the
                   walk home. `.asc-back`, the step-back pill every close-up
                   draws, is the way out while it is away. So THE APRON-LINE
                   WARNING IS WIDE-ONLY: a close-up's rects and props are free
                   to run below y=640 and are never warned about. A consumer
                   must NOT clamp a close-up host to 640 - the paper is measured
                   from the painting or it is not measured at all
shell/recordsroom.js THE RECORDS OFFICE AS A ROOM (0825), the chassis's first
                   tenant. Four rects on vn-09: the TRAY (the one breathing
                   verb - it opens records.js in a scene panel, and wears the
                   two nudges), the CORKBOARD (a close-up of the cork with
                   corkboard.js's own mountNotices pinned over `corkInner`,
                   FULL measured rect - the band is away in a close-up),
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
                   Narrow caps, the annex's law: the shell keeps the store, the
                   bridge and EMI. AND IT MUST BE TORN DOWN IN clearScreen -
                   the apron is on <body>, so a room left standing leaves a
                   slab across the next screen. + recordsroom.css
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
                   and the last spread remembered in `recordsBookPage`. `BOOK` is
                   a PLACEHOLDER table until the owner's prose lands. The prose
                   does NOT go through the lexicon (trap 26 caps a row at 96
                   chars and a paragraph is not a label) - only the chapter tabs
                   and the two arrows are keyed
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
shell/annexreveal.js THE NIGHT THE WALL MOVED: the once-ever reveal cinematic
                     (cut to black -> a thud from below -> EMI startled -> one
                     second of the records office at night with a wall panel
                     ajar). An OVERLAY, never a screen (module-local stage +
                     one Esc rung, traps 48/50), z 48 so EMI's layer paints
                     over the black for free. Fires on the tenth hole of the
                     LAST card and sets `annexRevealSeen` - the only gate the
                     ajar panel on the records wall reads
shell/peek.js      the shared hold-to-reveal verb (caps the class at A)
shell/keybinds.js  manifest-declared verb slots, one blob, PanicKey conflict check
shell/audio.js     THE consumer of engine 'arcademy-sfx' (WebAudio, procedural)
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
             quantize to 256 + optimized PNG (~40-46K each). The FACE IS TEXT -
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
               call site in shell.js is one unguarded line.
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
- **THE DOOR'S OTHER FOUR VERBS** hang off the same object and are how SORT's setup screen
  draws itself: `catalog()` (the sanitized `init.settings` projection - `remoteCatalog`
  `[{id,label,subs}]`, `subLibrary` `[{name,ok,videoCount,stillOnly}]`, `localFolders`
  `[{path,gifs,stills,videos}]`, `assetPresets` `[{id,name}]`, plus `remoteConsent`,
  `remoteMediaEnabled`, `offlineMode`, `mediaSource`), `probeSub(name)` ->
  `probe-sub {reqId,name}` / `sub-probe {reqId,name,ok,videoCount,stillOnly}`,
  `removeLibrarySub(name)` -> `library-remove {name}`, and `onLibrary(cb)` for the host's
  `library {subLibrary:[...]}` push (which any surface can cause - the Assets tab, the FYP
  popover, another probe). `shell.js` passes the six new init fields into `createAssets`
  beside `settings`; a host that predates them ships nothing and the catalog is simply empty.
  **Neither probe nor library is media**, so they ride `remote.js`'s `sendRaw`/`subscribe`
  rather than the reqId mailbox.
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
    Discord user id NEVER reach the page. Desktop caches a 128px PNG and ships
    it as a `data:` URI inside init; the web build sends the first-party proxy
    url. Anything else is a leak, and `<img>` onerror falls back to the drawn
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
    document-root seam `.ae-lite` uses (the game sets it, the engine only reads it, and
    BOTH come off on destroy or the lobby inherits a phone's ceiling).
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
    2026-08-24).** A game may tell the mascot how the room feels - `ctx.mood.tense()`
    latches until `.calm()`, `.clutch()` is the one big moment, `.stumble()` is a small
    >_<, `.runLost()` the once-per-class K.O. - and every one of them is throttled in
    shell.js (15s shared spacing, 3 stumbles a class, tense latch), so a game cannot
    flood her and must never build its own rate limit on top. Three laws: (1) NO BARK
    POOL may ever sit on `tense` or `clutch` - mid-class speech is barred and the only
    words a stumble can buy are the `miss` pool's own (maxPerClass:1); (2) call sites
    are opt-in and null-safe (`if (ctx.mood) ...` inside try/catch) because rigs stub
    ctx without it; (3) ride the game's EXISTING beat (L&F calls it inside its own
    `clutch()` ease) rather than inventing a parallel one. The `classStart` payload
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
    engine's two document listeners are `{passive:true}` in the bubble phase and
    call neither `preventDefault` nor `stopPropagation` (trap 80's shape), and
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
    DARE'S CLOCK IS THE SHORT ONE.** The dares (a09/a10/a11) have to be offered
    on `classStart`, which is also the moment that closes the "not mid-class"
    gate - so `offer()` runs first and `note()` sets the latch, which is the
    whole reason the module has two entry points instead of one. That places the
    strip correctly (shell.js fires `classStart` right after `clearScreen()`, one
    beat before the class chrome is built and long before a board takes input),
    but it does not keep it there: a strip nobody answers would still be up when
    the board goes live. Hence `DARE_GIVE_UP_MS` (12s, against the ordinary 40s)
    on every `classStart` ask, plus the universal any-press cancel. If a future
    ask wants to ride a moment that fires ON a live board, it does not - move the
    offer to the class-rules sheet (trap 85) instead.

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
