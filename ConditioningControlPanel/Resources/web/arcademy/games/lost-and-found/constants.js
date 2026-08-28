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

/* ----------------------------------------------------------------------------
 * THE ARC, AND IT IS PER-TIER NOW (owner ruling 2026-08-24, the class-length
 * wave). The class used to be FIVE finds against a 120s bell at every tier,
 * which meant a competent tier-1 player was done in ~50s of a two-minute class
 * and the bell was decoration. The budget is 300s now (see index.js's module
 * descriptor) and the COUNT is what makes every tier land in the same place.
 *
 * THE TARGET: ~4:00-4:30 (240-270s) of typical competent play, whatever the
 * tier. The bell at 300s stays a HARD cap and a par player may ride it.
 *
 * THE ARITHMETIC (one tile wall, the target relocates between finds, so more
 * finds cost NO new tiles - density and the look-signature pool are untouched):
 *
 *   hunt seconds per find, typical competent play, medium density:
 *     t1  7.8s   (48 tiles)   t2  9.8s   (62 tiles)
 *     t3 13.8s   (92 tiles)   t4 17.8s  (123 tiles)
 *   ...+ FOUND_CEREMONY_MS (1.8s) of ceremony per find, which is dead clock:
 *     cycle  t1 9.6s   t2 11.6s   t3 15.6s   t4 19.6s
 *   ...and the briefing is NOT on the clock (index.js starts the clock in
 *   briefing's own onDone), so the whole budget is n x cycle:
 *     t1 26 x 9.6  = 249.6s     t2 22 x 11.6 = 255.2s
 *     t3 16 x 15.6 = 249.8s     t4 13 x 19.6 = 254.8s
 *   Every tier lands ~250s, ~50s under the bell - which is the headroom the
 *   MISCLICK_TIME_PENALTY_SEC (3s a miss) eats into, deliberately.
 *
 * (t2/t3 hunt seconds are interpolated from the two MEASURED tiers through the
 * game's own par formula: measured hunt / uncapped parSecFor is 0.551 at t1 and
 * 0.535 at t4, so 0.543 x par is the tier-fair estimate in between.)
 * -------------------------------------------------------------------------- */
export const FINDS_BY_TIER = Object.freeze({ 1: 26, 2: 22, 3: 16, 4: 13 });

/** The modifier ("the board wakes up") lands about a THIRD of the way in - the
 *  same shape as the old find 3 of 5, never a fixed find number. */
export const MODIFIER_FIND_RATIO = 1 / 3;

/** How many finds this tier's class is. Anything unusable answers tier 1. */
export function findsForTier(tier) {
  const t = Math.round(Number(tier));
  return FINDS_BY_TIER[t >= 1 && t <= 4 ? t : 1] || FINDS_BY_TIER[1];
}

/** The find that turns the modifier on, per tier (>= 2, so it is never find 1). */
export function modifierFindForTier(tier) {
  return Math.max(2, Math.round(findsForTier(tier) * MODIFIER_FIND_RATIO));
}

/** The final bell = the tier's last find. Kept as its own verb because the bell
 *  is an ARC beat (announced, guaranteed clutch), not merely "the end". */
export function finalBellFindForTier(tier) {
  return findsForTier(tier);
}

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
  /** Touch (phone fx diet, measured 2026-08-28): the LITE four became EIGHT
   *  playing <video> elements on the phone profile (x2 wrap reps), against an
   *  iOS hardware-decode ceiling of three or four sessions and the school's
   *  one-decoder rule for a phone (engine VIDEO_BUDGET_TOUCH). One distinct
   *  url = two players with the wrap. Gifs keep the LITE live cap. */
  VIDEO_TILE_CAP_TOUCH: 1,
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

  /** Found ceremony length; the hunt resumes after it. NOTE it is DEAD CLOCK:
   *  the class timer runs through it, so at the per-tier find counts this is
   *  23s (t4) to 47s (t1) of the 300s budget. Both the pace arithmetic at the
   *  top of this file and RUBRIC.PAR_CEREMONY_SEC account for it. Shortening it
   *  is the obvious lever if a 4-minute class feels ceremony-heavy in play-test
   *  - it is an owner call, not one this wave made. */
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
  /** ...and "time to reach" is HUNTING time. Every find spends a found ceremony
   *  the player cannot hunt through, and at 13-26 finds that is 23-47s of the
   *  budget, not the 9s it was at five. The clamp subtracts it before dividing,
   *  or par would be a bar a par player physically cannot clear. Callers may
   *  override with `ceremonySec` (reduced motion runs the 900ms ceremony). */
  PAR_CEREMONY_SEC: 1.8,

  /** timeScore anchors: .6*par -> 1.0, par -> .78, 1.6*par -> .44, 2.4*par -> 0 */
  TIME_ZERO_MULT: 2.4,
  TIME_SPAN_MULT: 1.8,

  /** one misclick per find zeroes the accuracy term. A RATE - it needed no
   *  rescaling when the class grew (missScore already divides by the finds). */
  MISS_TOLERANCE: 1.0,
  /** The peek tax zeroes the term at this SHARE of the class's finds (floored
   *  at 3, which is exactly the old flat "three peeks" on a five-find class).
   *  A flat count would have been a fixed -0.10 on any 4-minute class the
   *  moment the player peeked twice, i.e. not a graded tax at all. The hard
   *  part of peek was never this term - it is the shell's flat A-cap. */
  PEEK_ZERO_RATIO: 0.35,
  PEEK_ZERO_MIN: 3,

  WEIGHTS: Object.freeze({ time: 0.40, miss: 0.25, peek: 0.10, streak: 0.25 }),

  /** S needs a clean streak, sub-par median AND near-perfect accuracy (§14 gate).
   *  Both halves are RATIOS of the class now, with floors that reproduce the old
   *  3-and-1 exactly at five finds. The streak ratio is 0.32 rather than the old
   *  3/5 = 0.60 because an unbroken run does NOT scale linearly: at a competent
   *  85% clean-find rate the chance of ever running 60% of a 26-find class is a
   *  few percent, where 3-of-5 was ~80%. Measured (exact run DP): a 0.32 ratio
   *  gives 8/7/5/4 by tier and pass rates 0.82/0.85/0.93/0.96 against today's
   *  0.798 - i.e. the gate stays a gate instead of quietly becoming impossible. */
  S_GATE_CLEAN_STREAK_RATIO: 0.32,
  S_GATE_CLEAN_STREAK_MIN: 3,
  /** Misclicks ARE linear, so this one is a plain per-find rate: 1-in-5, the old
   *  number restated. 5/4/3/3 by tier; binomial P(pass) at a 15% miss rate is
   *  0.82/0.77/0.79/0.88 against today's 0.835. */
  S_GATE_MISCLICK_RATE: 0.2,
  S_GATE_MISCLICKS_MIN: 1,

  /** The best clean streak that scores a full 1.0 on the streak term, as
   *  BASE + PER_FIND x finds. It is AFFINE, not a ratio, and that is the whole
   *  point: the longest run in n tries grows like log(n), so a flat ratio would
   *  deflate the term at long classes and inflate it at short ones. Fitted to
   *  hold typical competent play (85% clean) at the ~0.76 it scores today:
   *  divisors 15/14/11/10 by tier -> 0.78/0.77/0.81/0.78. */
  STREAK_FULL_BASE: 5,
  STREAK_FULL_PER_FIND: 0.4,
  STREAK_FULL_MIN: 3,

  /** THE BELL WITH PARTIAL FINDS IS A NORMAL OUTCOME NOW, not a fail.
   *  At five finds "the bell caught you" meant you never finished a 5-find
   *  class in two minutes, so the old rule (composite x finds/5, hard-capped at
   *  0.49 = a guaranteed C) was fair. At 13-26 finds a 12/13 bell run is a good
   *  class, and a flat C would be a lie. So completion is a CREDIT LINE: find
   *  this share of the class and the composite is untouched, below it credit
   *  falls away proportionally (and reaches zero at zero finds). S is still out
   *  of reach on a timeout - grade.js's sGate requires `complete`, which caps
   *  the class at A - and the ceiling below is belt-and-braces for that. */
  TIMEOUT_FULL_CREDIT_RATIO: 0.85,
  TIMEOUT_COMPOSITE_CEIL: 0.90,

  /** flavorXp (shell clamps at 15 too - BUILD-CONTRACT §8). DELIBERATELY NOT
   *  rescaled with the find count: the XP economy is app-wide and this class
   *  must not start paying 2-5x what its neighbours do. The consequence is that
   *  the cap SATURATES mid-class - find 8 of 13-26 tops it out and every find
   *  after it is worth no XP - which is intended, not an oversight. */
  XP_PER_FIND: 2,
  XP_PER_JACKPOT: 2,
  XP_CAP: 15,
});

export default { FINDS_BY_TIER, findsForTier, TIERS, PLAYTEST, RUBRIC };
