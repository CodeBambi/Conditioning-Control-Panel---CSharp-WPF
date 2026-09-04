/* ============================================================================
 * emi/shop.js - THE COUNTER AND THE LOCKER, IN HER WORDS (Locker wave, 0828).
 *
 * The Prize Counter sells and the Locker dresses, and until this file EMI
 * watched both with a blank face. The owner's ask was plain: she comments on
 * EVERY purchase, in her own words per thing, and she has something to say
 * about every skin she is put in. This is that, in two halves:
 *
 *   THE POOLS   `SHOP_POOLS`, data in barks.js's exact shape, one pool per sku
 *               on the shelf, one per outfit, frame, campus look and desk toy,
 *               plus the three small shelf moods (can't afford yet, only
 *               browsing, sent down the hall to the Locker). voice.js loads it
 *               beside barks.js as a fourth optional import and treats every
 *               pool as a bark - same floors, same rations, same no-repeat,
 *               same doubles slot. Nothing here reaches the bubble by a road
 *               of its own.
 *   THE EARS    `createShopVoice`, the listener half. It subscribes to the
 *               wave's document events (`arcademy-bought`, `arcademy-equipped`,
 *               `arcademy-refused`, `arcademy-shelf`, `arcademy-locker-opened`)
 *               and turns each into ONE moment name for the voice: the sku or
 *               the slot rides in the NAME (`shop.bought:emi_cheer`,
 *               `shop.worn:swim`) because voice.js matches trigger names
 *               exactly and needs no new predicate for that.
 *
 * TRIGGER NAMES (all fired through the controller's `voiceMoment`):
 *   shop.bought:<sku>       a confirmed buy. Ceremony pools: a purchase is an
 *                           earned moment and is exempt from the 40s floor.
 *   shop.bought             the fallback for a sku this file has no pool for
 *                           (the catalog is the host's and it grows).
 *   shop.worn:<outfit>      usual | labcoat | cheer | swim | varsity
 *   shop.worn:again         the same outfit put on twice
 *   shop.frame:<id>         gold | navy | plain
 *   shop.theme:<id>         standard | drone | snowday, plus shop.theme
 *   shop.toy:<key>          spinner | globe | lamp | beads | auto, plus shop.toy
 *   shop.bell               the Locker's "Ring it" poke
 *   shop.poor               the host refused with reason `poor`
 *   shop.browse             the shelf has been open a while with no buy
 *   shop.sentToLocker       the Locker opened straight off the counter
 *   locker.opened           the Locker opened from anywhere else
 *
 * THE LINES ARE LITERALS, ON PURPOSE. "No lexicon reaches EMI" is the widget's
 * law (CLAUDE.md trap 104, emi/moments.js): the shell resolves strings and
 * hands them down, and the pools in barks.js are data-only text. These follow
 * barks.js, not the lexicon, for the same reason barks.js does - a mod that
 * re-voices her does it here, in one file, in her register.
 *
 * NEVER TALKS OVER. The listener defers while a protected line is on the
 * bubble (`emi.saying`), keeps only the LATEST request per slot while it
 * waits, and gives up after a few seconds rather than land a line about a
 * thing the player has stopped looking at. The voice's own ask-hold and intro
 * hold stand under that; this file adds one courtesy, it removes none.
 *
 * DATA + ONE FACTORY. This module imports nothing, so a suite can load it bare
 * in node and lint every line; the controller and the moment road arrive as
 * caps from emi/index.js.
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * THE DIALS
 * -------------------------------------------------------------------------- */
export const SHOP_DIALS = Object.freeze({
  /** How long after `arcademy-bought` the line is asked for. The tray beat and
   *  the reveal are already moving by then; her bubble is the glance beside
   *  them, not a second hero. */
  BOUGHT_DELAY_MS: 900,
  /** The PA announcer speaks on a `pa_pack` buy (shell/pa.js). She waits for
   *  it: one voice on the speakers at a time. */
  PA_BUY_DELAY_MS: 3200,
  /** After an equip: the tell cue and the repaint go first. */
  WORN_DELAY_MS: 380,
  /** Sympathy is quick or it is not sympathy. */
  POOR_DELAY_MS: 500,
  /** The shelf has been open this long with nothing bought: only browsing. */
  BROWSE_AFTER_MS: 16000,
  /** Sent down the hall: the Locker's own door beat (380ms) goes first. */
  LOCKER_DELAY_MS: 900,
  /** A Locker opened within this many ms of the shelf being up was reached
   *  from the counter (the arrow, the toast, the footer line). */
  FROM_COUNTER_WINDOW_MS: 4000,
  /** While a protected line is up, retry this often... */
  RETRY_MS: 700,
  /** ...and for at most this long, then drop it. */
  RETRY_FOR_MS: 8000,
  /** The purchase reveal (shell/reveal.js) is a z62 card over her z50 bubble
   *  and it stays up until the player answers it. A purchase line waits for
   *  the card to come down - for this long at the outside. */
  REVEAL_WAIT_MS: 90000,
});

/* ----------------------------------------------------------------------------
 * THE POOLS
 *
 * barks.js's shape exactly: `on` (a trigger name), `odds`, `ceremony`,
 * `priority`, `cooldownMs`, `maxPerSession`, `lines:[{t, face, chain?,
 * double?}]`. Faces only from chains.js's FACES sets. No em-dashes, lowercase,
 * one breath, never explains the joke. `double:true` is the suspiciously-human
 * register and the voice rations it to one per session across every pool.
 * -------------------------------------------------------------------------- */

const CEREMONY = { odds: 1, ceremony: true, priority: 20 };
const WEAR = { odds: 1, ceremony: true, priority: 15, cooldownMs: 20000, maxPerSession: 6 };
const PICK = { odds: 1, ceremony: true, priority: 15, cooldownMs: 20000, maxPerSession: 4 };

function bought(sku, lines) {
  return Object.assign({ on: 'shop.bought:' + sku, lines }, CEREMONY);
}

export const SHOP_POOLS = Object.freeze({
  /* ============================ THE SHELF ============================ */

  boughtGoldFrame: bought('id_frame_gold', [
    { t: "gold on the card. very official. i'd ask for an autograph.", face: '*_*', chain: 'shock' },
    { t: "pinstripes! your card outdresses the whole staff room.", face: '(≧◡≦)' },
    { t: "the shiny one. i knew it. i didn't. i hoped.", face: '^_~' },
  ]),
  boughtNavyFrame: bought('id_frame_navy', [
    { t: "navy. classy. you look like you run this place. do you?", face: '(◔_◔)', chain: 'glance' },
    { t: "the quiet frame. good choice. loud is overrated. says me.", face: '^_^' },
    { t: "navy suits you. i'd wear it. i don't have a card. rude.", face: '¬_¬' },
  ]),
  boughtConfetti: bought('confetti_stamp', [
    { t: "confetti on the report card. the office will hate it. good.", face: '\\o/', chain: 'glee' },
    { t: "a stamp that explodes. politely. i tested it. twice.", face: '(◉_◉)' },
    { t: "every grade gets a party now. even the c. especially the c.", face: '^_^' },
  ]),
  boughtLateSlip: bought('late_slip', [
    { t: "a tardy slip. for emergencies. i won't tell the office.", face: '^_~', chain: 'wink' },
    { t: "one free oops. keep it in the bag. don't spend it on a whim.", face: '0_0' },
    { t: "the slip! streak insurance. very grown up of you.", face: '(◠‿◠)' },
  ]),
  boughtHonors: bought('honors_lever', [
    { t: "the honors lever. you asked for harder. on purpose. brave.", face: '(⊙_⊙)', chain: 'shock' },
    { t: "harder classes. by choice. i'm going to need to sit down.", face: '@_@' },
    { t: "a token for more homework. a first. i'm proud. and worried.", face: 'o_o' },
  ]),
  boughtSwimKey: bought('free_swim_key', [
    { t: "free swim! the key was in my desk the whole time. it wasn't.", face: '\\o/', chain: 'glee' },
    { t: "no grades in the pool. just you, the water, and me waving.", face: '(◠‿◠)' },
    { t: "a key to the pool. i'd come too. not allowed. long story.", face: '._.', double: true },
  ]),
  boughtWideBoard: bought('de_5x5', [
    { t: "the wide board. more squares, slower soak. take your time.", face: '(◠‿◠)' },
    { t: "five by five. the deep end got deeper. i checked the maths.", face: '0_0', chain: 'thinking' },
    { t: "a bigger board for the quiet room. nobody rushes you there.", face: '^_^' },
  ]),
  boughtJukebox: bought('jukebox', [
    { t: "the jukebox! three tokens. i hope it takes requests. mine.", face: '*_*', chain: 'shock' },
    { t: "music in the hall. i've been humming for weeks. you noticed?", face: '^_~' },
  ]),
  boughtAwayColors: bought('away_colors', [
    { t: "away colors! same you, sharper stripes. the stripes are key.", face: '(≧◡≦)', chain: 'glee' },
    { t: "new kit for the little walker. try not to grow out of it.", face: '^_^' },
    { t: "stripes. you look faster already. you're not. you look it.", face: '(¬‿¬)' },
  ]),
  boughtSparklers: bought('sparkler_steps', [
    { t: "sparkles where you walk. the janitor sighed. i heard it.", face: '*_*', chain: 'shock' },
    { t: "little sparks every step. you're a parade now. a small one.", face: '\\o/' },
    { t: "trailing sparks! i'd follow those anywhere. i'd try.", face: '(｡♥‿♥｡)' },
  ]),
  boughtBrassBell: bought('brass_bell', [
    { t: "the old bell! warmer ring. it wobbles a bit. that's charm.", face: '^_^', chain: 'glance' },
    { t: "brass. the new bell sulked when it heard. i'll console it.", face: '._.' },
    { t: "it rang alone in a cupboard for years. i answered once.", face: '(◔_◔)', double: true },
  ]),
  boughtDeskToy: bought('emi_desk_toy', [
    { t: "for my desk? for me? i'm not going to fidget with it. much.", face: '(｡♥‿♥｡)', chain: 'love' },
    { t: "a desk toy. i'll pretend it's office equipment. it isn't.", face: '^_~' },
    { t: "you got me a thing. i'm fine. this is fine. i'm spinning it.", face: '*_*' },
  ]),
  boughtPoster: bought('poster_drop_1', [
    { t: "fresh prints! the corkboard was bored of the same pins.", face: '\\o/', chain: 'glee' },
    { t: "new posters. motivational. i can't explain how. they are.", face: '0_0' },
    { t: "poster drop. the tube smells like ink. i like the smell.", face: '(◠‿◠)' },
  ]),
  boughtPa: bought('pa_pack', [
    { t: "the pa has a voice now. mostly the schedule. mostly.", face: '(◔_◔)', chain: 'glance' },
    { t: "a voice on the speakers. it isn't mine. i asked. twice.", face: '¬_¬' },
    { t: "morning announcements! i'll listen. notes optional.", face: '^_^' },
  ]),
  boughtDrone: bought('theme_drone', [
    { t: "the green one. somebody left that cartridge. it wasn't me.", face: '(⌐■_■)', chain: 'cool' },
    { t: "drone protocol. the campus hums now. i hummed first.", face: '^_~' },
    { t: "everything's green. my eyes adjusted. i think they did.", face: '@_@' },
  ]),
  boughtLabcoat: bought('emi_labcoat', [
    { t: "a lab coat! for me! i'm going to grade things. all things.", face: '(◉_◉)', chain: 'shock' },
    { t: "white coat. clipboard. i still won't write on it. tradition.", face: '^_^' },
    { t: "very scientific. i have a pocket protector now. respect it.", face: '(⌐■_■)' },
  ]),
  boughtCheer: bought('emi_cheer', [
    { t: "pom-poms! the chant is mandatory. i wrote the chant.", face: '\\o/', chain: 'glee' },
    { t: "cheer uniform! navy and pink. i already know the routine.", face: '(≧◡≦)' },
    { t: "pleats and all. give me an s. i'll give you the rest.", face: '^_~' },
  ]),
  boughtSwim: bought('emi_swim', [
    { t: "swim team! lane four. goggles up. i've never been wet.", face: '(⊙_⊙)', chain: 'shock' },
    { t: "the swim kit. i'm ready for water. the water isn't ready.", face: '(⌐■_■)' },
    { t: "goggles on. i can't see a thing. it's fantastic.", face: '@_@' },
  ]),
  boughtGhostWalk: bought('ghost_walk', [
    { t: "ghost walk. see-through you. spooky the fun way. i checked.", face: '0_0', chain: 'glance' },
    { t: "you're a little transparent now. i still see you.", face: '(◠‿◠)' },
    { t: "an afterimage! two of you. one's a bit late. that's fine.", face: 'o_o' },
  ]),
  boughtSnowday: bought('theme_snowday', [
    { t: "snow day! classes run anyway. i'm sorry. i'm not sorry.", face: '(≧◡≦)', chain: 'glee' },
    { t: "frost on the windows. i can draw on those. i will.", face: '*_*' },
    { t: "everything soft and blue. i'll be cold for you. bravely.", face: '^_^' },
  ]),
  boughtVarsity: bought('emi_varsity', [
    { t: "the jacket! it fit in lost and found. it fits better now.", face: '(｡♥‿♥｡)', chain: 'love' },
    { t: "varsity. every pose re-dressed. i've been practising.", face: '(⌐■_■)' },
    { t: "lost and found's best item. it's mine now. yours. ours.", face: '^_^' },
  ]),
  boughtTubeMidnight: bought('tube_midnight', [
    { t: "darker glass for the tube back home. moody. i approve.", face: '(¬‿¬)', chain: 'cool' },
    { t: "midnight glass. very late-night. i'm up anyway.", face: '-_-' },
    { t: "the tube goes dark. i'll still be in it. quieter. same me.", face: '^_^' },
  ]),
  /** The catalog is the host's and it grows. A sku with no pool above still
   *  gets a word, so no purchase is ever met with a blank face. */
  boughtAny: Object.assign({ on: 'shop.bought', priority: 5 }, CEREMONY, {
    lines: [
      { t: "yours. i saw the whole thing. very smooth.", face: '^_^', chain: 'glance' },
      { t: "sold! i'd wrap it. no hands. wrapped in spirit.", face: '\\o/' },
      { t: "new thing. good pick. i say that every time. i mean it.", face: '(◠‿◠)' },
    ],
  }),

  /* ============================ THE WARDROBE ========================= */

  wornUsual: Object.assign({ on: 'shop.worn:usual' }, WEAR, {
    lines: [
      { t: "the usual look. classic. i never left it, really.", face: '^_^' },
      { t: "plain me again. it's a strong look. i'm biased. correct.", face: '(¬‿¬)' },
      { t: "hung it up. okay. the usual is fine. the usual is great.", face: '0_0' },
    ],
  }),
  wornLabcoat: Object.assign({ on: 'shop.worn:labcoat' }, WEAR, {
    lines: [
      { t: "coat's on. everything is a sample now. you too. gently.", face: '(⌐■_■)', chain: 'cool' },
      { t: "lab coat, activated. i feel smarter. i'm not. i feel it.", face: '^_^' },
      { t: "clipboard ready. i'm noting things. mostly the weather.", face: '0_0' },
      { t: "white coat. i've got a hypothesis. it's that you're great.", face: '(◠‿◠)' },
    ],
  }),
  wornCheer: Object.assign({ on: 'shop.worn:cheer' }, WEAR, {
    lines: [
      { t: "ready! okay! two four six eight! i forgot the rest!", face: '\\o/', chain: 'glee' },
      { t: "pom-poms up. this is my loud outfit. brace yourself.", face: '(≧◡≦)' },
      { t: "cheer mode. i'll cheer everything. the bell. the floor. you.", face: '^_^' },
      { t: "give me a you! that's it. that's the whole chant.", face: '^_~' },
    ],
  }),
  wornSwim: Object.assign({ on: 'shop.worn:swim' }, WEAR, {
    lines: [
      { t: "goggles down. can't see. very brave. very wet in spirit.", face: '@_@', chain: 'dizzy' },
      { t: "swim kit on. lane four is a strong lane. i decided.", face: '(⌐■_■)' },
      { t: "the goggles squeak. i love the squeak. don't fix it.", face: '^_^' },
      { t: "swim team. i'm mostly dry. that's the goal. you'd think.", face: '0_0' },
    ],
  }),
  wornVarsity: Object.assign({ on: 'shop.worn:varsity' }, WEAR, {
    lines: [
      { t: "jacket on. lost and found's finest. i strut now. a little.", face: '(⌐■_■)', chain: 'smug' },
      { t: "varsity. the sleeves are long. i'm growing into them.", face: '^_^' },
      { t: "the jacket. it's warm. i don't get cold. it's warm anyway.", face: '(◠‿◠)', double: true },
      { t: "letterman look. i lettered in cheering you on. it counts.", face: '^_~' },
    ],
  }),
  wornAgain: Object.assign({ on: 'shop.worn:again' }, WEAR, {
    lines: [
      { t: "already wearing it. i checked. still on. still good.", face: '0_0' },
      { t: "put it on twice. that's dedication. or a stuck button.", face: '¬_¬' },
    ],
  }),

  /* ============================ THE CARD ============================= */

  frameGold: Object.assign({ on: 'shop.frame:gold' }, PICK, {
    lines: [
      { t: "gold frame on. the card glows. i'm squinting. worth it.", face: '*_*' },
      { t: "pinstripes, on the card. that's a headmaster's card now.", face: '(⊙_⊙)' },
      { t: "gold. they'll look twice at the gate. let them.", face: '(¬‿¬)' },
    ],
  }),
  frameNavy: Object.assign({ on: 'shop.frame:navy' }, PICK, {
    lines: [
      { t: "navy frame. quiet. confident. i'm taking notes on that.", face: '0_0' },
      { t: "navy on the card. understated. i am not. i admire it.", face: '^_^' },
      { t: "the blue one. it matches the band. i planned nothing.", face: '^_~' },
    ],
  }),
  framePlain: Object.assign({ on: 'shop.frame:plain' }, PICK, {
    lines: [
      { t: "plain card. minimalist. bold, in a very quiet way.", face: '._.' },
      { t: "no frame. the card can breathe. it said thanks.", face: '^_^' },
      { t: "back to plain. i respect it. i own no frames. i'd know.", face: '(◔_◔)' },
    ],
  }),

  /* ============================ THE CAMPUS =========================== */

  themeDrone: Object.assign({ on: 'shop.theme:drone' }, PICK, {
    lines: [
      { t: "green campus. the hum is back. i'm humming along.", face: '(⌐■_■)', chain: 'cool' },
      { t: "drone protocol on. everything's green. my eyes are fine.", face: '@_@' },
      { t: "the green look. somebody's cartridge. we like it.", face: '^_~' },
    ],
  }),
  themeSnowday: Object.assign({ on: 'shop.theme:snowday' }, PICK, {
    lines: [
      { t: "snow day! the courtyard's white. classes are still on.", face: '(≧◡≦)' },
      { t: "frost on. i'm drawing on the window. it's a face. it's me.", face: '^_^' },
      { t: "everything's blue and soft. i'll bring the cold. you sit.", face: '(◠‿◠)' },
    ],
  }),
  themeStandard: Object.assign({ on: 'shop.theme:standard' }, PICK, {
    lines: [
      { t: "house standard. the classic look. it never left, really.", face: '^_^' },
      { t: "back to the usual campus. it looked lonely. it's fine now.", face: '(◠‿◠)' },
      { t: "standard colors. i know where everything is again. relief.", face: '0_0' },
    ],
  }),
  themeAny: Object.assign({ on: 'shop.theme', priority: 5 }, PICK, {
    lines: [
      { t: "new look for the whole campus. i'd better learn the halls.", face: 'o_o' },
      { t: "the campus changed clothes. i'll adjust. i've adjusted.", face: '^_^' },
    ],
  }),

  /* ============================ THE DESK ============================= */

  toySpinner: Object.assign({ on: 'shop.toy:spinner' }, PICK, {
    lines: [
      { t: "the spinner. pinned. i'll spin it politely. i'll try.", face: '*_*' },
      { t: "spinner stays. it goes round. i go round with it. a bit.", face: '@_@' },
    ],
  }),
  toyGlobe: Object.assign({ on: 'shop.toy:globe' }, PICK, {
    lines: [
      { t: "the globe. pinned. i've spun it to somewhere warm.", face: '(◠‿◠)' },
      { t: "globe on the desk. the whole world, and it wobbles.", face: '0_0' },
    ],
  }),
  toyLamp: Object.assign({ on: 'shop.toy:lamp' }, PICK, {
    lines: [
      { t: "the lamp. pinned. it's slow. i find that relaxing.", face: '-_-' },
      { t: "lamp on the desk. the blobs are thinking. so am i.", face: '(◔_◔)' },
    ],
  }),
  toyBeads: Object.assign({ on: 'shop.toy:beads' }, PICK, {
    lines: [
      { t: "the beads. pinned. click click click. that's the whole toy.", face: '^_^' },
      { t: "beads on the desk. i count them. i lose count. i restart.", face: '0_0' },
    ],
  }),
  toyAuto: Object.assign({ on: 'shop.toy:auto' }, PICK, {
    lines: [
      { t: "desk's choice again. surprise me, desk. it always does.", face: '^_~' },
      { t: "unpinned. the desk rotates. i like a mystery. a small one.", face: '(¬‿¬)' },
    ],
  }),
  toyAny: Object.assign({ on: 'shop.toy', priority: 5 }, PICK, {
    lines: [
      { t: "pinned. that one stays on my desk. i'll fidget politely.", face: '^_^' },
      { t: "you picked my toy. good pick. i'd have picked it. i did.", face: '(◠‿◠)' },
      { t: "that one, pinned. i'll fidget with it. i'll deny it.", face: '¬_¬' },
    ],
  }),

  /* ============================ THE BELL ============================= */

  bellPoke: {
    on: 'shop.bell', odds: 0.85, ceremony: true, priority: 10, cooldownMs: 45000, maxPerSession: 2,
    lines: [
      { t: "that's the old bell. warmer, right? a little wobbly. charm.", face: '^_^' },
      { t: "ring it again. i'm not stopping you. i'm encouraging you.", face: '(≧◡≦)' },
      { t: "the old bell. it rang alone for years. not now.", face: '(◠‿◠)', double: true },
    ],
  },

  /* ============================ THE SHELF MOODS ====================== */

  /** Sympathy, quickly, and no guilt: the shelf keeps it. */
  shopPoor: {
    on: 'shop.poor', odds: 1, ceremony: true, priority: 10, cooldownMs: 30000, maxPerSession: 3,
    lines: [
      { t: "not yet. close, though. a class or two and it's yours.", face: '^_^', chain: 'glance' },
      { t: "short by a bit. the shelf keeps it. it isn't going anywhere.", face: '(◠‿◠)' },
      { t: "not enough on you. yet. yet is my favourite word.", face: '^_~' },
    ],
  },
  /** Only browsing. Idle chatter, so it obeys the campus floor. */
  shopBrowse: {
    on: 'shop.browse', odds: 0.7, ceremony: false, priority: 5, cooldownMs: 90000, maxPerSession: 2,
    lines: [
      { t: "just looking is allowed. i look at the shelf all day.", face: '0_0' },
      { t: "window shopping. the window's the best bit. i'm in it.", face: '^_^' },
      { t: "take your time. the shelf's not going anywhere. i checked.", face: '(◠‿◠)' },
    ],
  },
  /** The counter's arrow, the toast's verb, the footer line: sent down the hall. */
  sentToLocker: {
    on: 'shop.sentToLocker', odds: 1, ceremony: true, priority: 15, cooldownMs: 60000, maxPerSession: 2,
    lines: [
      { t: "off to the locker. rm 004. i'll be there first. i'm quick.", face: '^_~', chain: 'wink' },
      { t: "down the hall. the locker's got your name on it. inside.", face: '^_^' },
    ],
  },
  /** Arriving at the Locker from anywhere else. Rare, and it obeys the floor. */
  lockerOpened: {
    on: 'locker.opened', odds: 0.5, ceremony: false, priority: 5, cooldownMs: 120000, maxPerSession: 2,
    lines: [
      { t: "rm 004. your combination. i don't know it. i looked away.", face: '0_0' },
      { t: "the locker. everything you own, in one locker. tidy.", face: '(◠‿◠)' },
    ],
  },
});

/* ----------------------------------------------------------------------------
 * THE EARS
 * -------------------------------------------------------------------------- */

/** Which trigger a purchase's `kind`/`sku` falls back to when no sku pool
 *  spoke. Themes and toys have their own generic pools; everything else takes
 *  the counter's. */
function fallbackFor(sku) {
  const s = String(sku || '');
  if (s.indexOf('theme_') === 0) return 'shop.theme';
  return 'shop.bought';
}

/** The outfit name behind an equip id (`emi_swim` and `swim` both answer
 *  `swim`; null / '' / 'standard' answer `usual`). */
function outfitOf(id) {
  if (id == null || id === '' || id === 'standard' || id === 'usual') return 'usual';
  const s = String(id);
  return s.indexOf('emi_') === 0 ? s.slice(4) : s;
}

function frameOf(id) {
  if (id == null || id === '' || id === 'plain' || id === 'none') return 'plain';
  const s = String(id);
  return s.indexOf('id_frame_') === 0 ? s.slice('id_frame_'.length) : s;
}

function themeOf(id) {
  if (id == null || id === '') return 'standard';
  const s = String(id);
  return s.indexOf('theme_') === 0 ? s.slice('theme_'.length) : s;
}

function toyOf(id) {
  if (id == null || id === '' || id === 'auto') return 'auto';
  return String(id);
}

/**
 * createShopVoice(caps) -> { destroy, note, pending, DIALS } - the listener.
 *
 * @param {Object} caps
 *  fire   - (name, payload) -> boolean. The controller's `voiceMoment`: the
 *           voice, the ask engine and the trips, in that order, and it answers
 *           true when she spoke. REQUIRED (nothing here can speak without it).
 *  emi    - the controller, for `saying` (the defer) and `emote` (the wordless
 *           fallback when the voice declines a purchase). Optional.
 *  doc    - the document to listen on. Defaults to the global one; a suite
 *           hands in a double with add/removeEventListener.
 *  now    - clock, default Date.now. setTimeout/clearTimeout likewise.
 *  log    - a line sink.
 *  dials  - overrides for SHOP_DIALS (a suite shortens the waits).
 */
export function createShopVoice(caps) {
  const c = caps || {};
  const fire = typeof c.fire === 'function' ? c.fire : null;
  const emi = c.emi || null;
  const doc = c.doc || ((typeof document !== 'undefined') ? document : null);
  const now = typeof c.now === 'function' ? c.now : () => Date.now();
  const setT = typeof c.setTimeout === 'function' ? c.setTimeout : (fn, ms) => setTimeout(fn, ms);
  const clearT = typeof c.clearTimeout === 'function' ? c.clearTimeout : (id) => clearTimeout(id);
  const log = typeof c.log === 'function' ? c.log : () => {};
  const D = Object.assign({}, SHOP_DIALS, c.dials || {});

  let dead = false;
  /** One pending request per slot: `bought`, `worn`, `frame`, `theme`, `toy`,
   *  `bell`, `poor`, `browse`, `locker`. A newer request in the same slot
   *  replaces the older one, which is how four quick outfit presses become
   *  one line about the outfit she is actually in. */
  const slots = Object.create(null);
  /** The last id equipped per slot, for `shop.worn:again`. */
  const lastEquipped = Object.create(null);
  let shelfOpenAt = 0;        // ms, or 0 while the shelf is down
  let shelfClosedAt = 0;      // ms of the last close, for the from-counter window
  let boughtOnShelf = false;  // was anything bought since the shelf opened?
  let browseTimer = null;
  const said = [];            // test seam: every trigger that answered true

  function saying() {
    try { return !!(emi && emi.saying); } catch (e) { return false; }
  }

  /** B's reveal marks the root `arc-reveal-on` for the card's whole life. A
   *  line said under it is a line said to a scrim, so every slot waits. */
  function revealUp() {
    try {
      const h = doc && doc.documentElement;
      return !!(h && h.classList && typeof h.classList.contains === 'function' && h.classList.contains('arc-reveal-on'));
    } catch (e) { return false; }
  }

  /**
   * Ask for one trigger, deferring while a protected line is up. `names` is
   * the ladder: the first that speaks wins, the rest are never asked.
   * `onDecline` runs when the whole ladder was refused (the wordless fallback).
   */
  function request(slot, names, delay, onDecline) {
    if (dead || !fire) return;
    const prev = slots[slot];
    if (prev && prev.timer) { try { clearT(prev.timer); } catch (e) { /* noop */ } }
    const req = { names: names.slice(), born: now(), until: now() + Math.max(0, delay) + D.RETRY_FOR_MS, timer: null, onDecline };
    slots[slot] = req;
    req.timer = setT(() => attempt(slot, req), Math.max(0, delay));
  }

  function attempt(slot, req) {
    if (dead || slots[slot] !== req) return;
    req.timer = null;
    /* Three reasons to wait: her bubble is spoken for, the reveal card is up,
     * or (for a wear/pick line) the purchase line for the same buy has not
     * landed yet - "wear it" off the card must not talk over "you bought it". */
    const card = revealUp();
    const behindBuy = slot !== 'bought' && !!slots.bought;
    if (saying() || card || behindBuy) {
      const limit = card ? Math.max(req.until, req.born + D.REVEAL_WAIT_MS) : req.until;
      if (now() < limit) { req.timer = setT(() => attempt(slot, req), D.RETRY_MS); return; }
      slots[slot] = null;
      return;                                      // she was busy the whole window: let it go
    }
    slots[slot] = null;
    for (const name of req.names) {
      let took = false;
      try { took = !!fire(name, { inClass: false }); }
      catch (e) { log('emi shop: fire threw on ' + name + ' - ' + ((e && e.message) || e)); took = false; }
      if (took) { said.push(name); return; }
      /* BANKED, NOT REFUSED. Before her face is on (index.js's one-slot
       * `pendingMoment`) a moment is kept for the attach and answers false.
       * Walking on to the fallback would overwrite the sku line with the
       * generic one, so the ladder stops here. */
      try { if (emi && emi.pendingMoment === name) return; } catch (e) { /* noop */ }
    }
    if (typeof req.onDecline === 'function') {
      try { req.onDecline(); } catch (e) { /* noop */ }
    }
  }

  /** The wordless fallback for a purchase the voice would not put words to:
   *  she still looks up. A face is cheaper than a line and never rationed. */
  function glance(chain) {
    try { if (emi && typeof emi.emote === 'function') emi.emote(chain || 'glance'); }
    catch (e) { /* noop */ }
  }

  /* ---------------------------------------------------------- the events */

  function onBought(ev) {
    const d = (ev && ev.detail) || {};
    const sku = String(d.sku || '');
    if (!sku) return;
    boughtOnShelf = true;
    stopBrowse();
    let delay = D.BOUGHT_DELAY_MS;
    if (sku === 'pa_pack') delay = D.PA_BUY_DELAY_MS;
    /* THE REVEAL'S SAY-SO. shell/reveal.js may ask her to wait for its own
     * beat: `detail.emiDelayMs` outranks the dial when it asks for MORE. */
    const ask = Number(d.emiDelayMs);
    if (Number.isFinite(ask) && ask > delay) delay = ask;
    request('bought', ['shop.bought:' + sku, fallbackFor(sku)], delay, () => glance('shock'));
  }

  function onEquipped(ev) {
    const d = (ev && ev.detail) || {};
    const slot = String(d.slot || '');
    const id = d.id;
    if (!slot) return;
    if (slot === 'outfit') {
      const name = outfitOf(id);
      const again = lastEquipped.outfit === name;
      lastEquipped.outfit = name;
      request('worn', again ? ['shop.worn:again'] : ['shop.worn:' + name], D.WORN_DELAY_MS);
      return;
    }
    if (slot === 'frame') {
      request('frame', ['shop.frame:' + frameOf(id)], D.WORN_DELAY_MS);
      return;
    }
    if (slot === 'theme') {
      request('theme', ['shop.theme:' + themeOf(id), 'shop.theme'], D.WORN_DELAY_MS);
      return;
    }
    if (slot === 'toy') {
      request('toy', ['shop.toy:' + toyOf(id), 'shop.toy'], D.WORN_DELAY_MS);
      return;
    }
    if (slot === 'bell') {
      request('bell', ['shop.bell'], D.WORN_DELAY_MS);
    }
  }

  function onRefused(ev) {
    const d = (ev && ev.detail) || {};
    if (String(d.reason || '') !== 'poor') return;
    request('poor', ['shop.poor'], D.POOR_DELAY_MS, () => glance('glance'));
  }

  function onShelf(ev) {
    const d = (ev && ev.detail) || {};
    if (d.open) {
      shelfOpenAt = now();
      boughtOnShelf = false;
      startBrowse();
    } else {
      if (shelfOpenAt) shelfClosedAt = now();
      shelfOpenAt = 0;
      stopBrowse();
    }
  }

  function startBrowse() {
    stopBrowse();
    browseTimer = setT(() => {
      browseTimer = null;
      if (dead || !shelfOpenAt || boughtOnShelf) return;
      request('browse', ['shop.browse'], 0);
    }, D.BROWSE_AFTER_MS);
  }

  function stopBrowse() {
    if (browseTimer) { try { clearT(browseTimer); } catch (e) { /* noop */ } browseTimer = null; }
  }

  function onLockerOpened(ev) {
    const d = (ev && ev.detail) || {};
    const t = now();
    const fromCounter = d.from === 'counter' || d.from === 'prizebooth' || d.from === 'prizes'
      || !!shelfOpenAt || (shelfClosedAt && (t - shelfClosedAt) < D.FROM_COUNTER_WINDOW_MS);
    shelfOpenAt = 0;
    stopBrowse();
    request('locker', fromCounter ? ['shop.sentToLocker', 'locker.opened'] : ['locker.opened'], D.LOCKER_DELAY_MS);
  }

  /** `arcademy-emi-say {text, face?, nod?}`: the public "say this now" road,
   *  through the same defer. It bypasses the voice's rations on purpose - the
   *  shell already decided, the way it does for the orientation lines. */
  function onSay(ev) {
    const d = (ev && ev.detail) || {};
    const text = typeof d.text === 'string' ? d.text.trim() : '';
    if (!text || !emi || typeof emi.say !== 'function') return;
    const req = { until: now() + D.RETRY_FOR_MS, timer: null };
    slots.say = req;
    const go = () => {
      if (dead || slots.say !== req) return;
      if (saying() && now() < req.until) { req.timer = setT(go, D.RETRY_MS); return; }
      slots.say = null;
      try { emi.say(text, { face: d.face, nod: !!d.nod }); } catch (e) { /* noop */ }
    };
    req.timer = setT(go, 0);
  }

  const LISTEN = [
    ['arcademy-bought', onBought],
    ['arcademy-equipped', onEquipped],
    ['arcademy-refused', onRefused],
    ['arcademy-shelf', onShelf],
    ['arcademy-locker-opened', onLockerOpened],
    ['arcademy-emi-say', onSay],
  ];

  function bind() {
    if (!doc || typeof doc.addEventListener !== 'function') return false;
    for (const [name, fn] of LISTEN) {
      try { doc.addEventListener(name, fn); } catch (e) { /* noop */ }
    }
    return true;
  }

  function unbind() {
    if (!doc || typeof doc.removeEventListener !== 'function') return;
    for (const [name, fn] of LISTEN) {
      try { doc.removeEventListener(name, fn); } catch (e) { /* noop */ }
    }
  }

  const bound = bind();
  if (!bound) log('emi shop: no document to listen on');

  function destroy() {
    if (dead) return;
    dead = true;
    unbind();
    stopBrowse();
    for (const k of Object.keys(slots)) {
      const r = slots[k];
      if (r && r.timer) { try { clearT(r.timer); } catch (e) { /* noop */ } }
      slots[k] = null;
    }
  }

  return {
    destroy,
    /** Drive one event without a document (test seam / a caller with no DOM). */
    note(name, detail) {
      const fake = { detail: detail || {} };
      for (const [n, fn] of LISTEN) if (n === name) { fn(fake); return true; }
      return false;
    },
    /** Every trigger that answered true, in order (test seam). */
    get said() { return said.slice(); },
    /** Is anything still waiting for the bubble? (test seam) */
    get pending() { return Object.keys(slots).some((k) => !!slots[k]); },
    get bound() { return bound; },
    DIALS: D,
  };
}

export default { SHOP_POOLS, SHOP_DIALS, createShopVoice };
