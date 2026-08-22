/* ============================================================================
 * games/lost-and-found/constants.js - EVERY tunable this game has, in one file.
 *
 * Nothing in here is an absolute effect strength: the engine owns those (a game
 * spends a clamped channel ceiling, it never raises one - engine/index.js "THE
 * CEILING RULE"). What lives here is PACE and CLASSIC DIFFICULTY: cadences in
 * ms, tile counts, par seconds, rubric weights, and the heat scalar we hand to
 * engine.setHeat() - which is the only lever a game has to turn effects UP.
 *
 * GROUND-RULES ordering is baked into TIERS below: effects rise FIRST (drift,
 * swap cadence, sub_flash, bubble_field, crt), classic difficulty (density,
 * near-twin share) follows.
 * ==========================================================================*/

/** The arc is five finds. SYNTHESIS ruling: find 3 = modifier, find 5 = bell. */
export const FINDS_PER_CLASS = 5;
export const MODIFIER_AFTER_FIND = 3;      // "the board wakes up"
export const FINAL_BELL_FIND = 5;          // guaranteed clutch cinematics

/* ----------------------------------------------------------------------------
 * PLAYTEST BLOCK - flagged for tuning by the dossier / synthesis rulings.
 * "Tier-4 'target joins swap churn' needs a dedicated play-test pass: is the
 *  ~15s mid-hunt relocation cadence + pity tuning fun-hard or rage-hard?"
 * Everything the owner may want to dial after one session lives HERE, together.
 * -------------------------------------------------------------------------- */
export const PLAYTEST = Object.freeze({
  /** Tier 4 only: the target rejoins ambient churn mid-hunt (the twist at 11). */
  TIER4_MIDHUNT_RELOCATE: true,
  TIER4_MIDHUNT_RELOCATE_MS: 15000,
  /** ...but never immediately after a find, or the hunt never starts. */
  MIDHUNT_RELOCATE_GRACE_MS: 4000,

  /** Pity pulse (comeback hook): free, grade-neutral, never early. */
  PITY_STUCK_MS: 12000,
  PITY_REPEAT_MS: 8000,
  PITY_MIN_ELAPSED_MS: 6000,
  PITY_SHIMMER_MS: 300,

  /** Clutch ease ("the board relents"). Guaranteed on the final bell. */
  CLUTCH_SEC_LEFT: 10,
  CLUTCH_BELL_DELAY_MS: 12000,   // untimed / long budget: the bell still relents
  CLUTCH_DRIFT_EASE: 0.8,        // 20% slower drift
  CLUTCH_HEAT_EASE: 0.12,        // and a slightly cooler board

  /** Misclick punishment (the punishment IS a distraction). */
  MISCLICK_TIME_PENALTY_SEC: 3,
  MISCLICK_STREAK_FOR_WASH: 3,
  /** Wash pulse on a misclick streak is armed from this tier up (dossier T3). */
  MISCLICK_WASH_FROM_TIER: 3,

  /* ------------------------------------------------------------------------
   * THE LIVE WINDOW (0821 perf pass - "1 frame every 0.75s" + "groups of gifs
   * are still synched"). Read board.js's header for the physics; the short
   * version is that the expensive unit is a DISTINCT ANIMATED URL, not a tile:
   * Chromium keeps ONE decoder and ONE animation clock per image resource, and
   * every element showing that resource shares it. So:
   *   - N distinct animated urls  = N main-thread gif decoders  = the cost;
   *   - two tiles on the SAME url = one clock = the lockstep blink = the sync.
   * The board therefore deals a BOUNDED set of animated looks (each on its own
   * url, so nothing is ever in lockstep) and dresses every other seat with a
   * STILL. The ordinary swap churn then roams the animated seats across the
   * wall, so motion drifts like a marquee instead of sitting in fixed seats.
   * ---------------------------------------------------------------------- */
  /** Ceiling on DISTINCT animated urls on the wall at once. This is the dial. */
  LIVE_LOOP_CAP: 24,
  LIVE_LOOP_CAP_LITE: 10,
  /** Reduced motion: gifs ignore prefers-reduced-motion, so the only honest
   *  answer is to deal none. 0 = a fully still wall (and the cheapest board). */
  LIVE_LOOP_CAP_REDUCED: 0,
  /** ...but never more than this SHARE of a small board (a 12-tile board with
   *  24 animated tiles is just the old board), and never fewer than this many. */
  LIVE_LOOP_SHARE: 0.6,
  LIVE_LOOP_MIN: 8,
  /** Hard ceiling on animated ELEMENTS (live tiles x wrap clones). Every tile
   *  exists 2-3 times over for the toroidal wrap and each copy rasters
   *  independently, so the element count - not the tile count - is what
   *  starves the raster thread. */
  LIVE_ELEMENT_CEIL: 56,
  /** 0821 SMOOTHNESS PASS, measured on the owner's machine (Edge = the same
   *  engine WebView2 runs): with >= ~2 playing <video> tiles on the wall, viz's
   *  frame-interval heuristic drops the WHOLE PAGE to ~30Hz to match the video
   *  cadence - rAF ticks at a locked 33.4ms while 1 video tile (or none) holds
   *  16.7ms. At 30Hz every gif quantises onto a 33ms grid (the "framerate is
   *  low" feel) and every churn burst lands in half the frame budget (the
   *  "worse when things flip" feel). 24 heavy 448px gif decoders, drift AND
   *  hue filters: a clean 60fps - the live window already made gifs cheap.
   *  So the preference is INVERTED now: gifs carry the wall, video is the
   *  fallback for gif-starved pools, and the governor below sheds video seats
   *  if the half-rate lock engages anyway. (playbackRate nudges, will-change,
   *  border-radius removal: all probed, none break the lock.) */
  PREFER_VIDEO_LOOPS: false,
  LIVE_DRAW_TRIES: 4,

  /** Live-media budgets. Video tiles are the expensive ones - not for decode
   *  (GPU) but for the viz half-rate lock above and the per-page media-player
   *  budget. A gif-starved pool may still fill these seats; a mixed pool
   *  almost never reaches them (gifs win the draw). */
  VIDEO_TILE_CAP: 6,
  VIDEO_TILE_CAP_LITE: 4,
  /** Simultaneous <video> ELEMENTS, wrap clones included. Chromium's per-page
   *  media-player budget is the wall we hit; stay well under it. */
  VIDEO_ELEMENT_CEIL: 32,

  /* ------------------------------------------------------------------------
   * THE FRAME GOVERNOR (0821 smoothness pass). A watchdog on the achieved rAF
   * cadence: it learns the display's true frame interval (the best rolling
   * median it ever sees), and when the page sits at half-rate for a while -
   * the video cadence lock above, or a machine weaker than the tuning box -
   * it sheds live seats through the ordinary swap primitives until the rate
   * recovers: video seats first (they cause the lock), then gif seats (decode
   * saturation on weak machines). Videos never grow back within a class (the
   * lock would just re-engage); gif seats regrow one at a time after a long
   * healthy streak. All of it is presentation-only - it never touches the
   * target, a near-twin, or any graded state.
   * ---------------------------------------------------------------------- */
  GOVERNOR: true,
  /** Locked = rolling median rAF gap >= baseline x this. (60Hz: 16.7 -> 26.7ms
   *  trips on the 33.4ms lock with margin; a true 30Hz display just becomes
   *  the baseline and never trips.) */
  GOV_LOCK_X: 1.6,
  /** How long the lock must hold before the first shed (ms). */
  GOV_BAD_MS: 2200,
  /** Wait between sheds so each one can prove itself (ms). */
  GOV_SETTLE_MS: 1600,
  /** Video seats are only worth shedding when gifs are actually carrying the
   *  wall - a video-dominant pool at 30Hz has no gif judder to expose it. */
  GOV_SHED_VIDEO_MIN_GIFS: 4,
  GOV_VIDEO_FLOOR: 1,
  /** Gif shedding (weak machines): seats per shed, and the floor. */
  GOV_GIF_SHED_STEP: 2,
  GOV_GIF_FLOOR: 10,
  /** Regrow one gif seat after this long healthy, never past the dealt cap. */
  GOV_GROW_MS: 9000,

  /** Roaming: how many (animated <-> still) pairs each churn tick trades, so
   *  the motion drifts across the wall instead of living in fixed seats. It
   *  rides the churn timer, so it pauses with pause/suspend and during the
   *  found ceremony for free. 0 = fixed seats. */
  ROAM_PAIRS_PER_CHURN: 2,
  /** A swap burst applies at most this many pairs per tick; the rest follow on
   *  ~frame-spaced ticks. One synchronous batch used to repaint up to a dozen
   *  tiles (x wrap clones) in a single frame - half the frame budget gone in
   *  one gulp on a locked board. The pair CHOICE is unchanged (seeded); only
   *  the apply is spread, and the target's pair always lands in the first
   *  chunk so a relocation is never late. */
  SWAP_APPLY_CHUNK: 3,

  /** Progressive dressing. The board already staggers ASSEMBLY
   *  (ASSEMBLE_STAGGER_MS); this staggers the MEDIA, so the decoders start in
   *  a queue instead of a stampede, and every animated tile starts its clock on
   *  its own tick. Row-ordered span + a per-tile jitter. */
  DRESS_WINDOW_MS: 1600,
  DRESS_JITTER_MS: 900,

  /** The per-tile sheen sweep is a compositor animation on EVERY tile element
   *  (density x wrap clones). Above this density it is dropped - the wall has
   *  plenty of motion of its own by then. */
  SHEEN_MAX_DENSITY: 64,

  /** Found ceremony length; the hunt resumes after it. */
  FOUND_CEREMONY_MS: 1800,
  FOUND_CEREMONY_MS_REDUCED: 900,

  /** How long the near-miss "warm" shimmer sits on the true target. */
  NEAR_TWIN_SHIMMER_MS: 400,
  /**
   * How many near-twins may carry the target's ACTUAL media (at a different hue).
   * The rest are same-gradient/adjacent-hue lookalikes. Uncapped, half a tier-4
   * board would be literal copies of the target - which reads as a bug, not as
   * difficulty. Prime tuning candidate once the provider honours nearTwinBias.
   */
  NEAR_TWIN_URL_CAP: 4,
});

/* ----------------------------------------------------------------------------
 * TIER DIALS. Effects first, classic difficulty second.
 * `drift` is the dossier's drift dial (0..1) and drives OUR marquee period; the
 * caps-governed sway amplitude comes from engine row_drift + `heat`.
 * -------------------------------------------------------------------------- */
export const TIERS = Object.freeze({
  1: Object.freeze({
    heat: 0.18, drift: 0.15, swapMs: 9000, swapBurst: 1, burstChance: 0,
    sub: false, bubbles: false, crt: false, nearTwinShare: 0,
    previewSec: 3.2, density: 22,
  }),
  2: Object.freeze({
    heat: 0.40, drift: 0.35, swapMs: 5000, swapBurst: 1, burstChance: 0.15,
    sub: true, bubbles: false, crt: true, nearTwinShare: 0,
    previewSec: 2.8, density: 28,
  }),
  3: Object.freeze({
    heat: 0.62, drift: 0.55, swapMs: 3000, swapBurst: 1, burstChance: 0.35,
    sub: true, bubbles: true, crt: true, nearTwinShare: 0.25,
    previewSec: 2.4, density: 42,
  }),
  4: Object.freeze({
    heat: 0.85, drift: 0.80, swapMs: 2000, swapBurst: 3, burstChance: 0.5,
    sub: true, bubbles: true, crt: true, nearTwinShare: 0.5,
    previewSec: 1.8, density: 56,
  }),
});

/** Mobile / coarse-pointer density ladder (bigger tiles; ~60% of desktop). */
export const MOBILE_DENSITY = Object.freeze({ 1: 12, 2: 16, 3: 24, 4: 32 });

/** Player-facing density ladder (`lf_density` setting) - multiplies the tier's
 *  tile count. 0821 owner ruling (round 2): 'easy' = the original feel,
 *  'medium' = the old x2.2 hard (~5 rows x 9 at tier 1), 'hard' = DOUBLE ROWS
 *  AND DOUBLE COLUMNS of medium (~10 x 18) - tiles roughly x4, "almost squared". */
export const DENSITY_LEVELS = Object.freeze({ easy: 1, medium: 2.2, hard: 8.8 });
/** Absolute board ceiling (hard hits it above tier 1); rowsFor caps at 12 rows. */
export const DENSITY_HARD_CAP = 200;
/** Coarse pointers stop being honest input well before the wall does. */
export const DENSITY_COARSE_CAP = 72;

/** Within-class breathing: each find starts the band a little higher (DTRH). */
export const HEAT_BAND = 0.10;
/** The find-3 modifier: one step hotter for the rest of the class. */
export const MODIFIER_HEAT_STEP = 0.08;

/** Beat clock - drives engine.beat() (setpiece gates + the garnish bag). */
export const BEAT_MS = 4000;
/** HUD / timer tick. */
export const TICK_MS = 1000;
/** Board assembly stagger during the briefing (diegetic preloader cover). */
export const ASSEMBLE_STAGGER_MS = 42;
/** How long we wait for the asset claim before painting a gradient-only board. */
export const CLAIM_TIMEOUT_MS = 1200;
/** Over-provision the rotation pool so glitch_swap churn never repeats itself. */
export const POOL_OVERPROVISION = 20;

/** Reduced motion: continuous drift becomes one discrete row step this often. */
export const DISCRETE_STEP_MS = 4000;

/* ----------------------------------------------------------------------------
 * LOOK SIGNATURES. With the bundled placeholder floor (6 tiles) a 40-tile board
 * would show the same art eight times over and the hunt would be unwinnable, so
 * every tile gets a UNIQUE (gradient x hue) signature and the target's signature
 * is never reissued. Same gradient + adjacent hue = a NEAR-TWIN, which is how we
 * tag "warm" decoys without provider support (see index.js nearTwin notes).
 * CSS filters only - no canvas, so CORS-tainted remote media stays legal.
 * -------------------------------------------------------------------------- */
export const GRADIENTS = 8;
export const HUES = Object.freeze([0, 32, 64, 128, 196, 264, 308]);

/* ----------------------------------------------------------------------------
 * GRADING (game-specific inputs to the shared rubric; letters live in grades.js)
 * -------------------------------------------------------------------------- */
export const RUBRIC = Object.freeze({
  /** par seconds per find = A + B*density + C*drift, then clamped by the budget. */
  PAR_BASE_SEC: 3.0,
  PAR_PER_TILE_SEC: 0.22,
  PAR_PER_DRIFT_SEC: 4.0,
  /** A class can never be graded against a par it has no time to reach. */
  PAR_BUDGET_SHARE: 0.9,

  /** timeScore anchors: .6*par -> 1.0, par -> .78, 1.6*par -> .44, 2.4*par -> 0 */
  TIME_ZERO_MULT: 2.4,
  TIME_SPAN_MULT: 1.8,

  /** one misclick per find zeroes the accuracy term. */
  MISS_TOLERANCE: 1.0,
  /** three peeks zero the peek term (each one is a soft tax, never a fail). */
  PEEK_TAX: 0.34,

  WEIGHTS: Object.freeze({ time: 0.40, miss: 0.25, peek: 0.10, streak: 0.25 }),

  /** S needs a clean streak, sub-par median AND near-perfect accuracy (§14 gate). */
  S_GATE_CLEAN_STREAK: 3,
  S_GATE_MAX_MISCLICKS: 1,

  /** Timed round timeout with fewer than 5 finds = C floor with partial credit. */
  TIMEOUT_COMPOSITE_CEIL: 0.49,

  /** flavorXp (shell clamps at 15 too - BUILD-CONTRACT §8). */
  XP_PER_FIND: 2,
  XP_PER_JACKPOT: 2,
  XP_CAP: 15,
});

export default { FINDS_PER_CLASS, TIERS, PLAYTEST, RUBRIC };
