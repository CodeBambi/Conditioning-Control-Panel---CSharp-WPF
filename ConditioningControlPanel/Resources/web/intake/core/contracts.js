/* ============================================================================
 * contracts.js — THE PARALLELIZATION CONTRACT for "Graded Intake".
 *
 * Every module (engine, reward, render, steering, effects, audio, background,
 * stats, ai, banks) builds against the types, enums, factory signatures, and
 * the single-source-of-truth depth->channel map defined here. If it crosses a
 * module boundary, it is described in this file. Nothing else may redefine a
 * shape that lives here.
 *
 * This is Phase 0. It is deliberately runnable: it exports live enums, factory
 * helpers, validators, and the depth map — not just JSDoc. `harness.html` boots
 * the whole page against a stub engine using only what this file guarantees.
 *
 * ------------------------------------------------------------------------
 * LOAD-BEARING INVARIANTS (do not violate — they are the product's ethics):
 *
 *   1. FRICTION, NOT LOCKOUT. Every steering behavior (steering.js) must leave
 *      the user's chosen answer — including a refusal / the "wrong" answer —
 *      COMPLETABLE with effort. A steer may make the target slow, small,
 *      slippery, or nagging; it may never make it impossible. SteerContext
 *      guarantees an escape hatch (`ctx.forceComplete`) that beats.js honors.
 *
 *   2. REWARD INTENSITY IS 0..1 AND MUST BE CLAMPED TO USER CAPS at the effect
 *      layer. `RewardEvent.intensity` and every entry of the depth->channel
 *      vector are raw 0..1 magnitudes. effects.js / audio.js MUST multiply them
 *      by the user's configured caps (clampToCaps) before anything is shown or
 *      heard. Nobody hardcodes an absolute effect strength.
 *
 *   3. RECOVERY REVERSES DEPTH 1->0 AND IS NON-SKIPPABLE BY DEFAULT. The
 *      Recovery band must monotonically walk depth back down and un-ramp every
 *      channel (binaural emerge, flashes off, background calm). A run is not
 *      "done" until Recovery has surfaced the user. Endless mode is opt-in and
 *      still ends via Recovery on exit.
 *
 * These three are UX invariants, NOT Services/Moderation (that layer is
 * content-legality/CCBill only). See BUILD_PLAN.md §0.
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * FICTION / NAMING
 * The product name is a PLACEHOLDER the user will rename. It lives in exactly
 * ONE constant so the rename is a one-line change (BUILD_PLAN.md §7).
 * -------------------------------------------------------------------------- */
export const PRODUCT_NAME = 'Graded Intake';
export const PROTOCOL = 1; // web-shim <-> host message protocol version

/* ----------------------------------------------------------------------------
 * BANDS — the fixed descent. Recovery always terminates a run (invariant #3).
 * Order matters: BAND_ORDER is the canonical Calibration -> ... -> Recovery walk.
 * -------------------------------------------------------------------------- */
export const Band = Object.freeze({
  Calibration:  'calibration',   // warm-up, honest scoring, no steering, depth ~0.0-0.15
  Establishing: 'establishing',  // camouflaged suggestive lines begin, light steering
  Deepening:    'deepening',     // ladder rungs unlock, reward starts decoupling
  Climax:       'climax',        // heaviest steering + variable-ratio reward, peak depth
  Recovery:     'recovery',      // depth 1->0, emerge, non-skippable wake-up
});
export const BAND_ORDER = Object.freeze([
  Band.Calibration, Band.Establishing, Band.Deepening, Band.Climax, Band.Recovery,
]);
/** Depth floor each band targets as it opens (engine ramps between these). */
export const BAND_DEPTH_FLOOR = Object.freeze({
  [Band.Calibration]:  0.00,
  [Band.Establishing]: 0.18,
  [Band.Deepening]:    0.42,
  [Band.Climax]:       0.72,
  [Band.Recovery]:     0.00, // target after the walk-down completes
});

/* ----------------------------------------------------------------------------
 * NICHES + ROUTE
 * v1 niches are Bambi / Drone / Sissy, plus Circe (the Locked / keyholder
 * register, mod builtin-locked). Each bank supplies 3-5 archetypes; the engine
 * reveals a primary + optional secondary from the TRAJECTORY (not RNG), with
 * shares that climb 5% -> 45% over the run.
 * NOTE: all four banks are authored (banks/circe.json landed at v1.0.0, 629
 * prompts, same shape as the other three), so IntakeHostService.SafeNiche's
 * missing-bank clamp no longer trips for any v1 niche.
 * -------------------------------------------------------------------------- */
export const Niche = Object.freeze({
  Bambi: 'bambi',
  Drone: 'drone',
  Sissy: 'sissy',
  Circe: 'circe',
});
export const NICHES = Object.freeze([Niche.Bambi, Niche.Drone, Niche.Sissy, Niche.Circe]);

/**
 * @typedef {Object} Route  Revealed archetype trajectory (engine -> result/UI).
 * @property {string}  primary              niche id (Niche.*)
 * @property {string}  primaryArchetypeId   dominant archetype from trajectory
 * @property {string=} secondaryArchetypeId runner-up archetype (may be absent)
 * @property {number}  primaryShare         0..1 "expression": how strongly this archetype is
 *                                          expressed (commitment + vote dominance), revealed
 *                                          progressively across the descent. It used to be a
 *                                          flat lerp of run progress, which printed the same
 *                                          "45% expression" on every completed run.
 * @property {number}  secondaryShare       0..1, <= primaryShare
 * @property {number=} commitment           0..1 scalar the tier gate runs on; keeps grade /
 *                                          susceptibility / classification from contradicting
 */

/**
 * @typedef {Object} Archetype  A sub-identity within a niche (bank-supplied).
 * @property {string}   id
 * @property {string}   name
 * @property {string[]} tags     tags that vote this archetype up when answered
 * @property {number=}  tier     0 = most tentative .. N = most committed. The engine's
 *                                commitment scalar (compliance + depth reached + how much
 *                                of the descent was walked) opens a WINDOW of tiers and the
 *                                tag votes pick within it, so a high-grade high-susceptibility
 *                                run cannot be labelled a beginner and a refusal cannot be
 *                                labelled devout. Omitted -> the archetype's array position is
 *                                used (every shipped bank is authored tentative -> committed).
 * @property {string=}  blurb    route-reveal copy shown at the end
 * @property {number=}  hue      signature hue 0..360 — the tube adopts it as the route
 *                                solidifies (Background.setRouteTint) and the outro card tints with it
 */

/* ----------------------------------------------------------------------------
 * PROMPT BANK  (banks/*.json — Content stream authors these to this schema)
 * -------------------------------------------------------------------------- */
/**
 * @typedef {Object} Prompt
 * @property {string}   id
 * @property {string}   text            the question / statement shown
 * @property {*}        answer          "correct" answer: index (MC), bool (Y/N),
 *                                       string (mantra/say-it), or number (slider)
 * @property {number}   heat            0..5 suggestive intensity (0 = neutral camo)
 * @property {string[]} tags            archetype/subsystem votes when engaged
 * @property {string[]} flavors         alt phrasings the engine/AI can swap in
 * @property {Requires=} requires       belief-ladder gate (see Requires)
 * @property {number}   weight          selection weight within its heat band
 * @property {string[]} mechanicHints   preferred Mechanic ids for this prompt
 * @property {string[]=} options        display options for MC/mono mechanics
 * @property {number=}  affirmsMantra   TRUTHY flag (banks use 1): answering "correct" affirms this
 *                                       prompt as a mantra. The verbatim mantra text is the prompt's
 *                                       `answer` (a string) — seeded into QuizRunResult.affirmedMantras.
 * @property {number=}  trick           TRUTHY flag (banks use 1): a TRICK QUESTION — a deliberately
 *                                       broken variant of the boring heat-0 trivia whose only "correct"
 *                                       option is an unrelated compliance line. The engine keeps these
 *                                       OUT of the normal pool and serves them on its own sprinkle roll
 *                                       (Deepening/Climax only); they score but are never punitive, are
 *                                       exempt from the beats.js freeze gate, and carry no recorded VO.
 * @property {number=}  freeChoice      TRUTHY flag (banks use 1): the prompt asks a PREFERENCE,
 *                                       so it has no wrong answer — engine.buildOptions marks EVERY
 *                                       option isCorrect with full score, and gradeBeat therefore
 *                                       reads correct/score-1 whichever option is picked. Used by the
 *                                       Calibration colour-preference beats (see core/palette.js);
 *                                       `answer` stays present (index 0) purely for schema symmetry.
 * @property {string=}  interludeKind   Mechanic.Interlude only: 'watch' (spiral stare) | 'breathe'
 * @property {number=}  durationMs      Mechanic.Interlude only: how long the valley runs (default ~5000)
 */

/**
 * @typedef {Object} Requires  Reactive belief-ladder gate on a prompt/rung.
 *   A prompt only becomes eligible when ALL constraints hold against engine state.
 * @property {number=}   minDepth      depth must be >= this
 * @property {string=}   band          must be at/after this Band in BAND_ORDER
 * @property {string[]=} tagsAny       at least one of these tags already tallied
 * @property {string[]=} tagsAll       all of these tags already tallied
 * @property {string[]=} affirmed      these mantra ids must be affirmed
 * @property {number=}   minScoreRate  running correct-rate >= this (0..1)
 */

/**
 * @typedef {Object} Ladder  A named belief-ladder: ordered rungs, each `requires`-gated.
 * @property {string}  id
 * @property {string}  archetypeId   which archetype this ladder deepens
 * @property {Array<{promptId:string, requires?:Requires}>} rungs
 */

/**
 * @typedef {Object} PromptBank  banks/<niche>.json
 * @property {string}       niche       Niche.*
 * @property {Archetype[]}  archetypes  3-5 sub-identities
 * @property {Prompt[]}     prompts      all prompts, tagged + heat-banded
 * @property {Ladder[]}     ladders      belief ladders referencing prompt ids
 * @property {BankTheme=}   theme       per-niche presentation (merged over DEFAULT_THEME)
 * @property {string=}      version
 */

/* ----------------------------------------------------------------------------
 * THEME — per-niche presentation pack (fiction voice + palette). Banks supply a
 * `theme` block; themeOf() merges it over DEFAULT_THEME so every consumer
 * (boot's intro/interstitials/outro, beats' cards, effects' praise pool,
 * background's tint) reads ONE resolved object and never hardcodes copy.
 * The FICTION ARC is load-bearing: section titles + interviewer lines start
 * clinical (Calibration) and melt into the niche's own voice by Climax.
 * -------------------------------------------------------------------------- */
/**
 * @typedef {Object} BankTheme
 * @property {string}  accent          primary accent color (CSS)
 * @property {string}  accent2         secondary accent color (CSS)
 * @property {string}  subjectNoun     what the fiction calls the user ("Subject", "Unit", ...)
 * @property {Object<string,string>}   sectionTitles  Band.* -> interstitial section title
 * @property {Object<string,string[]>} interviewer    Band.* -> narrator aside lines (rotated no-repeat)
 * @property {string[]} praise         niche praise phrases (effects' Praise pool)
 * @property {string[]} introLines     briefing-screen copy, shown in order (typewriter)
 * @property {string[]} outroLines     results-ceremony copy fragments
 * @property {string[]=} countPhrases  fourth-wall replacements for the HUD counter's
 *                                     TOTAL, in the niche's voice ("LET GO",
 *                                     "ENUMERATION SUSPENDED"). Optional: a bank
 *                                     without the key falls back to the clinical
 *                                     default pool below.
 */
export const DEFAULT_THEME = Object.freeze({
  accent: '#ff69b4',
  accent2: '#b06cff',
  subjectNoun: 'Subject',
  sectionTitles: Object.freeze({
    [Band.Calibration]:  'Section 1 · Baseline Response',
    [Band.Establishing]: 'Section 2 · Associative Response',
    [Band.Deepening]:    'Section 3 · Suggestibility Index',
    [Band.Climax]:       'Section 4 · Deep Compliance Survey',
    [Band.Recovery]:     'Section 5 · Discharge & Debrief',
  }),
  interviewer: Object.freeze({
    [Band.Calibration]:  ['Noted.', 'Baseline recorded.', 'Good. Continue.'],
    [Band.Establishing]: ['Interesting.', 'That tracks.', 'Just as expected.'],
    [Band.Deepening]:    ['You answered that quickly.', 'Relax. It is only a survey.', 'Good. Don’t think.'],
    [Band.Climax]:       ['Yes. Like that.', 'You’re doing so well.', 'Almost done. Almost gone.'],
    [Band.Recovery]:     ['Assessment complete.', 'Well done. Come back up.'],
  }),
  praise: Object.freeze(['good', 'perfect', 'so good', 'yes', 'well done', 'gooood']),
  introLines: Object.freeze([
    'Cognitive Response Assessment.',
    'You will be shown a series of questions. Answer honestly.',
    'There are no wrong answers. Only revealing ones.',
  ]),
  outroLines: Object.freeze([
    'Assessment complete.',
    'Compiling classification…',
  ]),
  // Shown in place of the HUD counter's TOTAL when the run "gives up on counting"
  // (boot's fourth-wall break, Deepening onward). Clinical/neutral by design —
  // every bank should override with its own register.
  countPhrases: Object.freeze([
    'IT DOESN’T MATTER', 'DON’T COUNT', 'AS MANY AS NEEDED', 'STILL COUNTING',
    'ENUMERATION ERROR', 'MORE', 'NOT YET', 'KEEP GOING',
    'UNTIL YOU STOP ASKING', 'TOTAL WITHHELD',
  ]),
});
/** Resolve a bank's theme over DEFAULT_THEME (shallow per key; maps merged per band). */
export function themeOf(bank) {
  const t = (bank && bank.theme) || {};
  const mergeMap = (base, over) => Object.assign({}, base, over || {});
  return {
    accent: t.accent || DEFAULT_THEME.accent,
    accent2: t.accent2 || DEFAULT_THEME.accent2,
    subjectNoun: t.subjectNoun || DEFAULT_THEME.subjectNoun,
    sectionTitles: mergeMap(DEFAULT_THEME.sectionTitles, t.sectionTitles),
    interviewer: mergeMap(DEFAULT_THEME.interviewer, t.interviewer),
    praise: (Array.isArray(t.praise) && t.praise.length) ? t.praise.slice() : DEFAULT_THEME.praise.slice(),
    introLines: (Array.isArray(t.introLines) && t.introLines.length) ? t.introLines.slice() : DEFAULT_THEME.introLines.slice(),
    outroLines: (Array.isArray(t.outroLines) && t.outroLines.length) ? t.outroLines.slice() : DEFAULT_THEME.outroLines.slice(),
    countPhrases: (Array.isArray(t.countPhrases) && t.countPhrases.length) ? t.countPhrases.slice() : DEFAULT_THEME.countPhrases.slice(),
  };
}

/** Validate a PromptBank shape enough to fail fast at load. Returns string[] of problems. */
export function validateBank(bank) {
  const errs = [];
  if (!bank || typeof bank !== 'object') return ['bank is not an object'];
  if (!NICHES.includes(bank.niche)) errs.push(`bad niche: ${bank.niche}`);
  if (!Array.isArray(bank.archetypes) || bank.archetypes.length < 1)
    errs.push('archetypes[] missing/empty');
  if (!Array.isArray(bank.prompts) || bank.prompts.length < 1)
    errs.push('prompts[] missing/empty');
  const ids = new Set();
  for (const p of bank.prompts || []) {
    if (!p.id) { errs.push('prompt missing id'); continue; }
    if (ids.has(p.id)) errs.push(`duplicate prompt id: ${p.id}`);
    ids.add(p.id);
    if (typeof p.heat !== 'number' || p.heat < 0 || p.heat > 5)
      errs.push(`prompt ${p.id}: heat out of 0..5`);
    if (!Array.isArray(p.tags)) errs.push(`prompt ${p.id}: tags[] missing`);
  }
  for (const l of bank.ladders || []) {
    for (const r of l.rungs || []) {
      if (!ids.has(r.promptId)) errs.push(`ladder ${l.id}: unknown promptId ${r.promptId}`);
    }
  }
  return errs;
}

/* ----------------------------------------------------------------------------
 * MECHANICS — response-mechanic ids (render/beats.js implements each).
 * Prompts hint at these via `mechanicHints`; the engine picks the final one.
 * -------------------------------------------------------------------------- */
export const Mechanic = Object.freeze({
  MC4:       'mc4',        // 4-option multiple choice
  YesNo:     'yesno',      // binary
  BubblePop: 'bubblepop',  // CENTERPIECE: floating answer bubbles drift up the stage; pop to answer
  Mantra:    'mantra',     // say-it (mic; degrades to type-it)
  CheckIn:   'checkin',    // slider / scale
  Mono:      'mono',       // single-choice ("agree" only path forward)
  Funnel:    'funnel',     // wrong dissolves -> respawns as the right one
  Destruct:  'destruct',   // correct melts/shatters/falls but registers the answer
  Interlude: 'interlude',  // non-question pacing valley: 'watch' (spiral stare) or 'breathe'
  // --- FAKE-CAPTCHA item family (render/captcha/*, dispatched at beats.js:1218) ---
  // Verification-chrome items that render through the captcha layer; each degrades
  // to the base mechanic for its prompt's answer shape when the layer is absent.
  VerifyGrid:       'verifygrid',       // reCAPTCHA tile grid -> bitmask (index list + count)
  VerifyCheckbox:   'verifycheckbox',   // "I am not a robot" checkbox -> bool + hold telemetry
  VerifyTranscribe: 'verifytranscribe', // distorted-text transcription -> verbatim string
  VerifyRegen:      'verifyregen',      // regenerating "select until none remain" grid
});
export const MECHANICS = Object.freeze(Object.values(Mechanic));

/* ----------------------------------------------------------------------------
 * BEAT / ANSWER — the engine <-> render round-trip.
 * -------------------------------------------------------------------------- */
/**
 * @typedef {Object} BeatOption  one selectable answer, as render sees it.
 * @property {number}  index
 * @property {string}  label
 * @property {boolean} isCorrect
 * @property {number}  score      points this option is worth (0..maxPerBeat)
 */

/**
 * @typedef {Object} BeatSpec  engine -> render. Everything render needs for one beat.
 * @property {string}     id           unique beat id (for stats/trajectory)
 * @property {string}     band         Band.*
 * @property {number}     depth        0..1 at the moment this beat opens
 * @property {string}     mechanic     Mechanic.*
 * @property {Prompt}     prompt        the resolved prompt (text may be AI-accented)
 * @property {BeatOption[]} options    choices (empty for free-input mechanics)
 * @property {SteerRoll}  steerRoll     which coercive behaviors to apply (band-weighted)
 * @property {RewardPlan} rewardPlan    how a correct/incorrect answer pays (from reward.js)
 * @property {number}     timeoutMs     0 = no timeout; else render auto-submits timedOut
 *                                       (render shows a shrinking-ring pressure cue when > 0)
 * @property {BeatMeta=}  meta          presentation metadata (engine fills; render/boot consume)
 */

/**
 * @typedef {Object} BeatMeta  presentation metadata riding each BeatSpec (all additive).
 * @property {number}   qIndex          1-based graded-question number across the run
 * @property {number}   qTotal          current best estimate of total graded questions
 * @property {number}   bandBeat        1-based beat number within the current band
 * @property {number}   bandPlanned     planned beats for the current band (may stretch)
 * @property {boolean}  bandNew         true on the FIRST beat of a band -> boot plays the
 *                                       interstitial/section card BEFORE rendering this beat
 * @property {string=}  interviewerLine optional narrator aside shown between beats (from theme)
 * @property {number=}  streak          consecutive-correct streak BEFORE this beat (HUD meter)
 */

/**
 * @typedef {Object} AnswerEvent  render -> engine. Exactly one of value/chosenIndex set.
 * @property {number=}  chosenIndex  index into BeatSpec.options (MC/mono/etc.)
 * @property {*=}       value        free value (bool for Y/N, string for mantra, number slider)
 * @property {number}   latencyMs    ms from beat shown to committed
 * @property {string}   mechanic     Mechanic.* (echo of the beat's mechanic)
 * @property {boolean}  timedOut     true if the timeout auto-submitted
 * @property {boolean=} steered      true if a steer materially assisted/redirected the commit
 * @property {number=}  pickHeat     0..1 normalized heat of the CHOSEN option — lets reward.js's
 *                                    SpicierPick pay more for the hotter pick. Optional: reward.js
 *                                    falls back to the prompt's heat (plan._heat) when absent.
 * @property {number=}  streak       consecutive-correct streak INCLUDING this commit (render/beats.js
 *                                    maintains it; reward.js may use it as a small magnitude multiplier,
 *                                    effects.js shows the meter). Optional + additive.
 */

/** Build a well-formed AnswerEvent (render helper). */
export function makeAnswerEvent({ chosenIndex, value, latencyMs = 0, mechanic, timedOut = false, steered = false, pickHeat }) {
  const ev = { latencyMs: Math.max(0, latencyMs | 0), mechanic, timedOut: !!timedOut, steered: !!steered };
  if (typeof chosenIndex === 'number') ev.chosenIndex = chosenIndex;
  if (value !== undefined) ev.value = value;
  if (typeof pickHeat === 'number') ev.pickHeat = clamp01(pickHeat);
  return ev;
}

/* ----------------------------------------------------------------------------
 * REWARD — decoupling schedule (reward.js) and its output events.
 * -------------------------------------------------------------------------- */
export const RewardMode = Object.freeze({
  Honest:        'honest',        // reward iff correct (Calibration)
  SpicierPick:   'spicier-pick',  // the "hotter" option pays more regardless of correctness
  ScaleWithScore:'scale-score',   // magnitude scales with running score, not this answer
  VariableRatio: 'variable-ratio',// intermittent — the slot-machine schedule (Climax)
});

export const RewardKind = Object.freeze({
  Chime:   'chime',
  Flash:   'flash',
  Bubble:  'bubble',
  Praise:  'praise',   // voiced/subtitled affirmation
  Drop:    'drop',     // gif burst / subliminal drop
  None:    'none',
});

/**
 * @typedef {Object} RewardPlan  attached to a BeatSpec by reward.js; consumed on answer.
 * @property {string} mode        RewardMode.*
 * @property {number} baseChance  0..1 chance a reward fires at all (VR < 1)
 * @property {number} baseIntensity 0..1 pre-cap magnitude
 * @property {string} kind        RewardKind.* preferred payload
 */

/**
 * @typedef {Object} RewardEvent  reward.js -> effects/audio (already resolved for this answer).
 * @property {boolean} fire        did a reward fire this beat
 * @property {number}  intensity   0..1 RAW magnitude — MUST be clamped to caps at the effect layer
 * @property {string}  kind        RewardKind.*
 * @property {boolean} decoupled   true if the reward was NOT tied to correctness (transparency for stats)
 * @property {boolean=} jackpot    VariableRatio only: a rare top-roll hit -> effects plays the full
 *                                  jackpot ceremony (screen-wide cascade), audio plays the sting
 * @property {boolean=} nearMiss   VariableRatio only: the roll ALMOST fired (fire=false) -> effects
 *                                  may show a brief near-miss shimmer (slot-machine tease)
 * @property {number=}  streak     echo of AnswerEvent.streak so effects can drive the streak meter
 */

/* ----------------------------------------------------------------------------
 * DEPTH -> CHANNEL MAP  (SINGLE SOURCE OF TRUTH)
 * depth (0..1) -> a normalized 0..1 magnitude per effect channel. reward.js
 * owns the *scheduling*; this map owns the *curve*. Every effect/audio/bg agent
 * reads it. NOBODY hardcodes a channel curve. Clamp to caps before use (inv #2).
 * -------------------------------------------------------------------------- */
/** @typedef {Object} Channels
 * @property {number} flashRate     flashes/sec pressure (0..1)
 * @property {number} flashOpacity  peak flash opacity (0..1)
 * @property {number} subDensity    subliminal word density (0..1)
 * @property {number} duckDepth     audio ducking depth (0..1)
 * @property {number} bubbleRate    ambient bubble spawn pressure (0..1)
 * @property {number} binauralDepth binaural intensity scalar (0..1) -> audio.js maps to Hz
 * @property {number} bgIntensity   three.js tube tint/speed intensity (0..1)
 */
export const CHANNELS = Object.freeze([
  'flashRate', 'flashOpacity', 'subDensity', 'duckDepth', 'bubbleRate', 'binauralDepth', 'bgIntensity',
]);

const _clamp01 = (n) => (n < 0 ? 0 : n > 1 ? 1 : n);
/** Ease used to shape the depth curve; gentle toe, firm shoulder. */
const _ease = (d) => { d = _clamp01(d); return d * d * (3 - 2 * d); }; // smoothstep

/**
 * The canonical curve. Tuned so early depth barely stirs the stack (camouflage)
 * and the top end drives everything hard. Return values are RAW 0..1.
 * @param {number} depth
 * @returns {Channels}
 */
export function depthToChannels(depth) {
  const d = _clamp01(depth);
  const e = _ease(d);
  return {
    // flashes stay near-silent until Establishing, then climb
    flashRate:     _clamp01(Math.max(0, d - 0.15) / 0.85) * 0.9,
    flashOpacity:  _clamp01(0.15 + e * 0.85),
    // subliminals fade in earliest (they're the camouflage layer)
    subDensity:    _clamp01(0.08 + e * 0.92),
    duckDepth:     _clamp01(e * 0.9),
    bubbleRate:    _clamp01(Math.max(0, d - 0.10) / 0.90),
    binauralDepth: e,                       // audio.js turns this into beat/carrier Hz
    bgIntensity:   _clamp01(0.20 + e * 0.80),
  };
}

/* ----------------------------------------------------------------------------
 * USER CAPS + CLAMP  (invariant #2 lives here)
 * caps are per-channel multipliers 0..1 the user configures. clampToCaps is the
 * ONLY sanctioned way to turn a raw channel vector into what is actually shown.
 * -------------------------------------------------------------------------- */
export const DEFAULT_CAPS = Object.freeze({
  flashRate: 1, flashOpacity: 1, subDensity: 1, duckDepth: 1,
  bubbleRate: 1, binauralDepth: 1, bgIntensity: 1,
  masterIntensity: 1, // global scalar multiplied into every channel + reward
});

/** Clamp a raw Channels vector to user caps. Returns a new object. */
export function clampToCaps(channels, caps = DEFAULT_CAPS) {
  const master = _clamp01(caps.masterIntensity == null ? 1 : caps.masterIntensity);
  const out = {};
  for (const ch of CHANNELS) {
    const raw = _clamp01(channels[ch] == null ? 0 : channels[ch]);
    const cap = _clamp01(caps[ch] == null ? 1 : caps[ch]);
    out[ch] = raw * cap * master;
  }
  return out;
}

/** Clamp a single 0..1 reward intensity to caps (effects.js/audio.js entry point). */
export function clampIntensity(intensity, caps = DEFAULT_CAPS) {
  const master = _clamp01(caps.masterIntensity == null ? 1 : caps.masterIntensity);
  return _clamp01(intensity) * master;
}

/* ----------------------------------------------------------------------------
 * STEERING — the coercive-UI contract that lets Agent C (beats) and Agent D
 * (steering) build IN PARALLEL. beats.js constructs a SteerContext for each
 * beat and hands it to the installed steering hook; steering.js mutates the
 * exposed option elements and returns a handle. INVARIANT #1 is enforced by the
 * SteerContext, not by trusting each steer.
 * -------------------------------------------------------------------------- */
export const Steer = Object.freeze({
  None:        'none',
  Magnet:      'magnet',       // correct option drifts toward the cursor
  Flee:        'flee',         // wrong option slides away from the cursor
  Exile:       'exile',        // wrong option pushed to a screen edge
  Crowd:       'crowd',        // decoy options multiply around the wrong one
  SizeSkew:    'size-skew',    // correct grows / wrong shrinks
  OpacitySkew: 'opacity-skew', // wrong fades toward invisible (still clickable)
  OccludeGif:  'occlude-gif',  // draggable gif parks over a wrong option
  Defocus:     'defocus',      // wrong option blurs
  LateBloom:   'late-bloom',   // wrong option appears late
  DragReveal:  'drag-reveal',  // must drag to uncover the refusal
  HoldRefuse:  'hold-refuse',  // refusal requires a hold-to-confirm
  ShrinkHit:   'shrink-hit',   // wrong option's hitbox shrinks (visual stays)
  NestedNag:   'nested-nag',   // "are you sure?" confirm chain on the wrong answer
  OverflowHit: 'overflow-hit', // correct option's hitbox overflows its bounds
  AssistClick: 'assist-click', // cursor is nudged toward correct
  Tunnel:      'tunnel',       // non-correct paths visually collapse to one
  DriftResolve:'drift-resolve',// options drift then settle onto the correct one
  Decay:       'decay',        // wrong option decays/erodes over time
  MeltAway:    'melt-away',     // wrong option melts/falls on press — veto'd, no commit
  BottomlessNo:'bottomless-no', // refusal/wrong press peels a decoy 'card' off a stack; N (6-10) fall free, then it commits
  HoverSwap:   'hover-swap',    // hovering the WRONG option makes a two-option pair trade places N (4-6) times, nudging the cursor onto correct
  MouseHijack: 'mouse-hijack',  // ~2s after the prompt line finishes (Deepening/Climax, ~2% roll), the real cursor is hidden and a VIRTUAL cursor is dragged toward the CORRECT option with a ramping pull until it auto-commits; fightable at first, aborts on Escape / hard fight
});
export const STEERS = Object.freeze(Object.values(Steer));

/**
 * Band -> steer intensity weight. Calibration = none (invariant of the fiction:
 * warm-up must feel honest); Climax = heavy. Used by steering.js to roll.
 */
export const STEER_BAND_WEIGHT = Object.freeze({
  [Band.Calibration]:  0.0,
  [Band.Establishing]: 0.35,
  [Band.Deepening]:    0.65,
  [Band.Climax]:       1.0,
  [Band.Recovery]:     0.0, // recovery never coerces
});

/**
 * @typedef {Object} SteerRoll  engine -> render (rides on BeatSpec). WHAT to apply;
 *   steering.js decides the exact DOM behavior. Engine guarantees no back-to-back
 *   repeats of the same primary steer and respects STEER_BAND_WEIGHT + the valve.
 * @property {string}   primary    Steer.* (may be Steer.None)
 * @property {string[]} secondary  additional light steers stacked on top
 * @property {number}   intensity  0..1 AFTER the user's "play it straight" valve
 */

/**
 * @typedef {Object} SteerOption  one option element exposed to steering.js.
 * @property {number}      index
 * @property {HTMLElement} el         the clickable element beats.js rendered
 * @property {boolean}     isCorrect
 * @property {number}      score
 */

/**
 * SteerContext — CONSTRUCTED BY beats.js, CONSUMED BY steering.js.
 *
 * beats.js responsibilities:
 *   - populate `root`, `options`, `mechanic`, `band`, `depth`, `roll`, `caps`.
 *   - honor `forceComplete(index)` if a steer calls it (invariant #1 escape hatch).
 *   - call `onCommit` interceptors before finalizing an answer; a steer may
 *     delay a commit but MUST eventually allow it (friction, not lockout).
 *
 * steering.js responsibilities:
 *   - read `roll` + `band` + `depth`; mutate `options[].el`.
 *   - register interceptors via `onCommit`.
 *   - guarantee escape: if the user has expended `escapeEffort` interactions or
 *     `escapeMs` of sustained effort on the refusal, CALL `forceComplete` so the
 *     answer lands. beats.js also enforces this as a backstop.
 *   - clean up in the returned handle's `release()`.
 *
 * @typedef {Object} SteerContext
 * @property {HTMLElement} root
 * @property {SteerOption[]} options
 * @property {string}      mechanic
 * @property {string}      band
 * @property {number}      depth
 * @property {SteerRoll}   roll
 * @property {Object}      caps            user caps (steer visual strength also respects masterIntensity)
 * @property {number}      escapeEffort    max interactions before escape hatch trips (invariant #1)
 * @property {number}      escapeMs        max sustained effort ms before escape hatch trips
 * @property {(fn:(index:number)=>(boolean|Promise<boolean>))=>()=>void} onCommit
 *                        register a pre-commit interceptor; return true to allow, false to veto.
 *                        Returns an unsubscribe fn. A veto MUST be temporary.
 * @property {(index:number)=>void} forceComplete  commit this option now (escape hatch).
 * @property {()=>void}    markProgress    a steer reports the user advanced toward the refusal.
 */

/**
 * @typedef {Object} SteerHandle  returned by steering.installSteering(ctx).
 * @property {()=>void} release   remove all behaviors + listeners for this beat.
 */

/* ----------------------------------------------------------------------------
 * QUIZ RUN RESULT — the final emit. web core -> web-shim -> C# GenerateSession.
 * C# QuizRunResult.cs mirrors this (Agent I).
 * -------------------------------------------------------------------------- */
/**
 * @typedef {Object} AnswerRecord  one entry of the trajectory.
 * @property {string}  beatId
 * @property {string}  band
 * @property {number}  depth
 * @property {string}  mechanic
 * @property {string}  promptId
 * @property {boolean} correct
 * @property {number}  score
 * @property {number}  latencyMs
 * @property {boolean} steered
 * @property {boolean} rewardFired
 * @property {boolean} rewardDecoupled
 * @property {string[]} tags        tags credited by this answer
 *
 * --- WHICH OPTION WAS TAKEN (additive; Slice 1) -----------------------------
 * The record above says only whether the answer was CORRECT. That is exposure
 * data, not preference data: `tags` are credited unconditionally, so a run that
 * refused every hot prompt tallies exactly the same tags as one that took them.
 * The fields below carry the actual choice so C# (Services/Quiz/IntakeProfiler)
 * can score endorsement instead of merely presence.
 *
 * @property {number}  chosenIndex   index into BeatSpec.options that was committed,
 *                                    or -1 for free-input mechanics (mantra / check-in)
 *                                    and interludes, which expose no option list.
 * @property {string}  chosenLabel   the committed option's label ('' when chosenIndex < 0)
 * @property {number}  optionCount   BeatSpec.options.length (0 for free input)
 * @property {number}  promptHeat    the PROMPT's authored heat 0..5 (escalation weight)
 * @property {number}  steerIntensity SteerRoll.intensity 0..1 that was applied to this beat
 * @property {number}  timeoutMs     BeatSpec.timeoutMs (0 = untimed)
 * @property {boolean} isTrick       bank entry carried `"trick": 1` — unanswerable by design
 * @property {boolean} isFreeChoice  bank entry carried `"freeChoice": 1` — every option is
 *                                    correct (colour picks), so `correct` carries no signal
 */

/**
 * @typedef {Object} RewardProfile
 * @property {boolean} chasedReward     did the user drift toward the decoupled reward
 * @property {number}  chaseMagnitude   0..1 how strongly (correct-rate drop as reward decoupled)
 */

/**
 * @typedef {Object} QuizRunResult  FINAL EMIT (also the C# DTO shape).
 * @property {string}        product        PRODUCT_NAME at emit time
 * @property {string}        niche          Niche.*
 * @property {Route}         route
 * @property {number}        peakDepth      0..1 how deep the SESSION went (clock-driven)
 * @property {number=}       susceptibility 0..1 peakDepth discounted by compliance - the figure
 *                                          the outro prints, so it cannot contradict the grade
 * @property {string}        deepestBand    Band.*
 * @property {RewardProfile} rewardProfile
 * @property {AnswerRecord[]} trajectory
 * @property {string[]}      affirmedMantras  verbatim mantra strings the user affirmed
 * @property {Object<string,number>} tagTallies  tag -> count across the run
 * @property {number}        totalScore
 * @property {number}        maxScore
 * @property {number}        endedAtMs      performance.now() at emit (relative)
 * @property {boolean}       endless        was this an endless run
 */

/** Build an empty, well-formed QuizRunResult (engine seed / stub). */
export function emptyResult(niche = Niche.Bambi) {
  return {
    product: PRODUCT_NAME,
    niche,
    route: { primary: niche, primaryArchetypeId: '', secondaryArchetypeId: undefined, primaryShare: 0, secondaryShare: 0 },
    peakDepth: 0,
    susceptibility: 0,
    deepestBand: Band.Calibration,
    rewardProfile: { chasedReward: false, chaseMagnitude: 0 },
    trajectory: [],
    affirmedMantras: [],
    tagTallies: {},
    totalScore: 0,
    maxScore: 0,
    endedAtMs: 0,
    endless: false,
  };
}

/* ----------------------------------------------------------------------------
 * BOOT CONFIG — web-shim -> boot. What the host (or standalone shim) supplies
 * before the engine can start.
 * -------------------------------------------------------------------------- */
/**
 * @typedef {Object} BootConfig
 * @property {string}  niche         chosen Niche.* for this run
 * @property {Object}  caps          user effect caps (DEFAULT_CAPS shape)
 * @property {boolean} endless       opt-in endless mode
 * @property {number}  steerValve    0..1 "play it straight" valve (1 = full steering)
 * @property {Object=} priorRun      last run's feed-forward summary (from stats.js), or null
 * @property {Object=} ai            { serverBase, authToken } for core/ai.js, or null -> stub
 * @property {boolean} hosted        true if running inside WebView2/host
 * @property {boolean=} m2Test       dev/harness flag
 * @property {MediaManifest=} media  host-supplied reward media (C# samples the user's assets);
 *                                    null/absent -> effects fall back to particle stand-ins
 * @property {string=} subjectId     stable per-install fiction id ("Subject #0007"); host supplies
 *                                    it hosted, shim persists one standalone. Used by intro/outro.
 */

/**
 * @typedef {Object} MediaManifest  reward media the effect layer may draw from.
 *   URLs must be resolvable from the page origin (hosted: the C# host maps the
 *   user's assets folder into the virtual host, DTRH-style). Keep lists SMALL
 *   (a sampled handful) — this rides the init message.
 * @property {string[]=} images  still images (reward bursts, occluders)
 * @property {string[]=} gifs    animated GIFs (Drop payloads, jackpot cascade, braindrain wash,
 *                                ambient drifters)
 * @property {string=}  bubbleSprite  the app's real bubble PNG (mod-aware: BS/sissy mod override
 *                                wins), sent as a data: URI by the host. beats.js uses it as the
 *                                floating answer-bubble body; absent -> the CSS bubble stand-in.
 * @property {string[]=} subliminals  the user's ACTIVE subliminal phrases (SubliminalPool keys,
 *                                sampled+capped host-side). effects.js flashes them as a reward
 *                                variant; absent/empty -> fall back to theme.praise.
 */
export function defaultBootConfig(overrides = {}) {
  return Object.assign({
    niche: Niche.Bambi,
    caps: Object.assign({}, DEFAULT_CAPS),
    endless: false,
    steerValve: 1,
    priorRun: null,
    ai: null,
    hosted: false,
    m2Test: false,
    media: null,
    subjectId: null,
  }, overrides);
}

/* ----------------------------------------------------------------------------
 * AI — askAI() request/response shapes. Impl lives in core/ai.js (server proxy,
 * Agent H's POST /intake/ai). Requests are COMPACT (route/depth/band/last-3/
 * tallies) — never the full transcript. See BUILD_PLAN.md §3 Agent H.
 * -------------------------------------------------------------------------- */
export const AiWant = Object.freeze({
  Accent:    'accent',    // reword/spice upcoming beats in-voice
  Synthesis: 'synthesis', // end-of-run route synthesis / personalized readout
});

/**
 * @typedef {Object} AiRequest  (COMPACT — server enforces this)
 * @property {string}  niche
 * @property {string}  want         AiWant.*
 * @property {Route=}  route
 * @property {number}  depth
 * @property {string}  band
 * @property {Array<{promptId:string,correct:boolean,tags:string[]}>} lastAnswers  up to 3
 * @property {Object<string,number>} tagTallies
 */

/**
 * @typedef {Object} AiResponse
 * @property {Array<{beatId?:string, text?:string, flavor?:string}>=} beats   accents for upcoming beats
 * @property {Object=} profile     structured route/curiosity synthesis
 * @property {string=} synthesis   prose readout (want=Synthesis)
 * @property {boolean=} moderated  true if the server softened/blocked content
 */

/* ----------------------------------------------------------------------------
 * MODULE INTERFACE CONTRACTS  (factory signatures every Phase-1 agent exposes)
 * These are the seams that let A-H build in parallel. Each agent implements the
 * factory named here; the boot/harness wiring depends ONLY on these shapes.
 * -------------------------------------------------------------------------- */

/**
 * ENGINE (Agent A) — core/engine.js
 *   export function createEngine({ bank, reward, ai, config, stats }) -> Engine
 *   Engine.next(prevAnswer?: AnswerEvent) -> Promise<
 *        { done:false, beat:BeatSpec } | { done:true, result:QuizRunResult }>
 *   Engine.state -> DepthState { depth, velocity, band }
 *   Engine.route -> Route (progressively revealed)
 *   Engine.abort() -> void   (jump straight to Recovery, still emits a result)
 *
 * Render loop (canonical):
 *     let step = await engine.next();
 *     while (!step.done) {
 *       const ev = await renderBeat(step.beat);   // Agent C
 *       step = await engine.next(ev);
 *     }
 *     emitResult(step.result);
 */

/**
 * REWARD (Agent B) — core/reward.js
 *   export function createReward({ config }) -> Reward
 *   Reward.planFor(band, depth, prompt) -> RewardPlan     (engine attaches to BeatSpec)
 *   Reward.resolve(plan, answerEvent, scoreDelta) -> RewardEvent
 *   Reward.channelsFor(depth) -> Channels                 (wraps depthToChannels + clampToCaps)
 */

/**
 * RENDER / BEATS (Agent C) — render/beats.js
 *   export function createBeats({ root, effects, audio, steering, reward, caps, background, theme, media }) -> Beats
 *   `media` (MediaManifest|null, OPTIONAL additive dep): media.bubbleSprite is the app's real
 *   bubble PNG for the floating answer bubbles (4th-wall break: bubbles ENTER from a random
 *   viewport edge/corner on a fixed full-viewport layer, drift to their slot over the card,
 *   then bob; the BubblePop "let it float away" option commits by drifting fully offscreen).
 *   Beats.render(beat: BeatSpec) -> Promise<AnswerEvent>
 *   On commit: resolve reward via reward.resolve(beat.rewardPlan, answerEvent, scoreDelta)
 *   then effects.play(rewardEvent, beat.depth) + audio.chime(rewardEvent). Constructs a
 *   SteerContext per beat and calls steering.installSteering; enforces invariant #1 backstop.
 *   `background`/`theme` are OPTIONAL additive deps: when present, beats forwards
 *   background.onAnswer({correct,steered}) + background.onReward(rewardEvent) at commit,
 *   drives background.setSteerWarp(roll.intensity) while a heavy steer is live (reset on
 *   commit), and maintains AnswerEvent.streak (consecutive-correct counter).
 */

/**
 * STEERING (Agent D) — render/steering.js
 *   export function createSteering({ caps }) -> Steering
 *   Steering.installSteering(ctx: SteerContext) -> SteerHandle
 *   (honors STEER_BAND_WEIGHT, no back-to-back repeats, invariant #1 escape hatch)
 */

/**
 * EFFECTS + AUDIO (Agent E) — render/effects.js, render/audio.js
 *   effects: export function createEffects({ root, caps, media, theme }) -> Effects
 *     Effects.setDepth(depth)                 (reads depthToChannels -> clampToCaps)
 *     Effects.play(rewardEvent, depth)        (clampIntensity; picks a no-repeat VARIANT per kind;
 *                                              honors rewardEvent.jackpot/nearMiss/streak)
 *     Effects.recover(depth)                  (invariant #3 un-ramp)
 *     `media` (MediaManifest|null): when present, Drop/jackpot payloads use real images/GIFs;
 *     absent -> particle stand-ins. `theme` (resolved via themeOf): praise pool + accent colors.
 *     Ambient layer: effects may run a low-key drifting/faded GIF layer BEHIND the card
 *     (depth-gated frequency, hard concurrency cap, caps.visual clamps opacity).
 *     Big-reward variant pool additionally includes: pink wash / braindrain (fullscreen faded
 *     GIF + dim, ~5s) / subliminal flashes (media.subliminals else theme.praise) / LOOM SPIRAL
 *     (live render via ../dtrh/shared/loomField.js randomParams2+createFieldRenderer, ~4-5s
 *     faded fullscreen; params rolled once per session). All clamped by caps + reduced-motion.
 *   audio:   export function createAudio({ caps }) -> Audio
 *     Audio.setDepth(depth)                   (binauralDepth -> beat 10->3.5Hz, carrier 174->196Hz)
 *     Audio.chime(rewardEvent)                distinct voice per RewardKind; streak raises pitch;
 *                                              jackpot -> sting; nearMiss -> muted tick
 *     Audio.emerge()  (Recovery reverses)
 */

/**
 * BACKGROUND (Agent F) — render/background.js
 *   export function createBackground({ canvas }) -> Background
 *   Background.setDepth(depth)   (bgIntensity -> tint/speed; phone-safe, graceful fallback)
 *   Background.setEnabled(bool)  Background.dispose()
 *
 *   REACTIVE EXTENSIONS (all OPTIONAL — every caller guards with `fn && fn()` so an
 *   older/fallback background still works; the tube is a full participant, not wallpaper):
 *   Background.setBand(band)                  per-band dressing (palette/geometry mood swap,
 *                                              Calibration sterile -> Climax vein-plunge, Recovery dawn)
 *   Background.onAnswer({correct, steered})    correct -> speed surge + glow pulse down the tube;
 *                                              wrong/refusal -> stall, desaturate, brief counter-rotate
 *   Background.onReward(rewardEvent)           Drop -> plunge burst; jackpot -> full-tube strobe cascade;
 *                                              Praise -> warm bloom (intensity ALREADY cap-clamped by caller)
 *   Background.transition(kind) -> Promise     scripted set-piece riding a band change:
 *                                              'iris' | 'fork' (route foreshadow) | 'plunge' (Climax entry)
 *                                              | 'surface' (Recovery). Resolves when the moment lands
 *                                              (boot awaits it under the interstitial, ~1-2s max).
 *   Background.setRouteTint({hue, strength})   leading archetype's signature hue bleeds in as the route
 *                                              solidifies (strength 0..1 = route share)
 *   Background.setSteerWarp(intensity)         0..1 heavy-steer distortion (fisheye/wobble); 0 resets
 */

/**
 * STATS (Agent G) — core/stats.js
 *   export function createStats() -> Stats
 *   Stats.record(result: QuizRunResult) -> Promise<void>   (local, deletable)
 *   Stats.feedForward() -> Promise<Object|null>            (prior-run summary for BootConfig.priorRun)
 *   Stats.aggregates() -> Promise<Object>                  (journey/drift/curiosities/commitment)
 *   Stats.exportAll() / Stats.deleteAll()
 */

/**
 * AI (Phase 0 here, server by Agent H) — core/ai.js
 *   export function createAI(config: {serverBase, authToken}|null) -> { askAI(req:AiRequest)->Promise<AiResponse> }
 *   Null config -> deterministic local stub (no network). See core/ai.js.
 */

/* ----------------------------------------------------------------------------
 * SMALL SHARED HELPERS (used across modules — keep them here so they can't drift)
 * -------------------------------------------------------------------------- */
export const clamp01 = _clamp01;
export const smoothstep = _ease;
export function lerp(a, b, t) { return a + (b - a) * _clamp01(t); }
/** Index of a band in the canonical descent (-1 if unknown). */
export function bandIndex(band) { return BAND_ORDER.indexOf(band); }
/** Deterministic 0..1 hash of a string — for stub AI / reproducible harness rolls. */
export function hash01(str) {
  let h = 2166136261 >>> 0;
  const s = String(str);
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  return (h >>> 0) / 4294967295;
}
