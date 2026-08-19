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

  /** Live-media budgets. Video tiles are the expensive ones (DTRH discipline). */
  VIDEO_TILE_CAP: 12,
  VIDEO_TILE_CAP_LITE: 4,

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
    previewSec: 3.2, density: 16,
  }),
  2: Object.freeze({
    heat: 0.40, drift: 0.35, swapMs: 5000, swapBurst: 1, burstChance: 0.15,
    sub: true, bubbles: false, crt: true, nearTwinShare: 0,
    previewSec: 2.8, density: 20,
  }),
  3: Object.freeze({
    heat: 0.62, drift: 0.55, swapMs: 3000, swapBurst: 1, burstChance: 0.35,
    sub: true, bubbles: true, crt: true, nearTwinShare: 0.25,
    previewSec: 2.4, density: 30,
  }),
  4: Object.freeze({
    heat: 0.85, drift: 0.80, swapMs: 2000, swapBurst: 3, burstChance: 0.5,
    sub: true, bubbles: true, crt: true, nearTwinShare: 0.5,
    previewSec: 1.8, density: 40,
  }),
});

/** Mobile / coarse-pointer density ladder (dossier: 12/12/18/24, bigger tiles). */
export const MOBILE_DENSITY = Object.freeze({ 1: 12, 2: 12, 3: 18, 4: 24 });

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
