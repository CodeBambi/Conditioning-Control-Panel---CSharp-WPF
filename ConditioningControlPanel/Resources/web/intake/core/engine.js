/* ============================================================================
 * engine.js — Agent A — the "Graded Intake" run brain (REAL state machine).
 *
 * Replaces the Phase-0 stub. Owns:
 *   - a band/depth STATE MACHINE over BAND_ORDER with ONE depth scalar (0..1)
 *     carrying VELOCITY; depth ramps between BAND_DEPTH_FLOOR anchors.
 *   - a BEAT SEQUENCER: heat-banded, weight-biased, `requires`-gated prompt
 *     selection from the PromptBank; a mechanic chosen from prompt.mechanicHints;
 *     a rewardPlan from reward.planFor(); a band-weighted steerRoll with NO
 *     back-to-back same primary steer.
 *   - a reactive BELIEF-LADDER: bank.ladders climbed in order, each rung
 *     `requires`-gated, deeper rungs unlocked by affirmed mantras.
 *   - DEPTH-VELOCITY: fast + high-score answers raise velocity, which INTENSIFIES
 *     the band (hotter heat window, harder depth nudge) but NEVER shortens it
 *     below its base; timid / slow / low-score answers lower it (linger, stretch).
 *   - ROUTE REVEAL FROM TRAJECTORY (not RNG): answered tags vote archetypes;
 *     primary = top, secondary = runner-up; shares climb ~5% -> 45% across a run.
 *   - a rewardProfile {chasedReward, chaseMagnitude} from the correlation between
 *     decoupled-reward beats and the user drifting off the correct answer.
 *   - a NON-SKIPPABLE Recovery band that walks depth 1->0 monotonically (inv #3).
 *   - opt-in ENDLESS: loop Deepening/Climax laps until abort(), then Recovery.
 *
 * KEEP THE INTERFACE (contracts.js "ENGINE"):
 *   createEngine({ bank, reward, ai, config, stats }) -> Engine
 *   Engine.next(prevAnswer?: AnswerEvent) -> Promise<{done,beat?}|{done,result?}>
 *   Engine.state  -> DepthState        Engine.route -> Route      Engine.abort()
 *
 * MUST NOT THROW AT IMPORT (a throw = silent infinite loader; DtRH gotcha).
 * ==========================================================================*/

import {
  Band, BAND_ORDER, BAND_DEPTH_FLOOR,
  Mechanic, MECHANICS,
  Steer, STEER_BAND_WEIGHT,
  RewardMode, RewardKind,
  Niche, NICHES,
  emptyResult, PRODUCT_NAME,
  themeOf,
  clamp01, smoothstep, lerp, bandIndex, hash01,
  AiWant,
} from './contracts.js';
import {
  COLOR_TAG, MAX_COLOR_BEATS,
  beginHarvest, harvestWantsMore, noteColorServed, closeHarvest,
} from './palette.js';

/* ----------------------------------------------------------------------------
 * TUNING CONSTANTS (pacing over ~81 graded beats; BUILD_PLAN.md §5 Phase 3).
 * The run used to be ~50 beats weighted 60% into Deepening + Climax, which made
 * the descent land too fast: the camouflage phase was over before it had done
 * its job. Two dilution passes have since stretched it:
 *   pass 1  Calibration 9->16, Establishing 11->19 (+30% into the low-heat bands)
 *   pass 2  every band grows again, MORE at the top than the bottom, so the run
 *           gets longer and airier without flattening the climb:
 *             Calibration  16 -> 21   (+30%)
 *             Establishing 19 -> 25   (+30%)
 *             Deepening    16 -> 19   (+20%)
 *             Climax       14 -> 16   (+15%)
 * The graded run is now ~81 beats. Deepening + Climax hold ~43% of them (was
 * ~46%, originally ~60%), so pressure still concentrates late — it just arrives
 * later and with more ordinary ground under it.
 *
 * The extra beats are not more of the same: PLAIN_SHARE below spends the deep
 * bands' growth on boring trivia rather than additional spice (see that block).
 * The heat-0 pool is 280 entries per bank, so the longer camouflage never runs dry.
 * -------------------------------------------------------------------------- */
const DESCENT_BANDS = Object.freeze([Band.Calibration, Band.Establishing, Band.Deepening, Band.Climax]);
const BASE_BEATS = Object.freeze({
  [Band.Calibration]:  21,
  [Band.Establishing]: 25,
  [Band.Deepening]:    19,
  [Band.Climax]:       16,
});
const MIN_BEATS = 5;               // a band can be shortened to this, never skipped (all 5 bands sequence)
const MAX_BEATS = 34;              // a band can be stretched to this by timid play (kept above the new bases)
const RECOVERY_BEATS = 4;          // fixed, non-skippable walk-down (invariant #3)
const CLIMAX_PEAK = 1.0;           // depth ceiling the Climax band ramps toward
const EXPECTED_DESCENT = 81;       // sum(BASE_BEATS); drives progressive route-share climb
const HARD_BEAT_CAP = 1600;        // safety net so a never-aborted ENDLESS run still surfaces (many laps)

/* ----------------------------------------------------------------------------
 * ROUTE / CLASSIFICATION MODEL  (see voteArchetypes + updateRoute)
 *
 * WHAT WENT WRONG BEFORE (measured, not guessed): the route was a raw count of
 * tag hits, unweighted by heat, and credited whichever way the answer went. The
 * heat-0 pool is 285 prompts per bank and EVERY ONE of them carries `trivia` +
 * `curious` — tags owned exclusively by the most tentative archetype. Calibration
 * (21 beats, heat window [0,1]) plus Establishing (25 beats, [1,2]) is 46 of the
 * 81 graded beats, so the shallow half of the run structurally outvoted the deep
 * half before the deep half had been asked a single question. Headless sweeps
 * over 42 synthetic runs returned "Curious Listener" for 21/21 bambi runs and
 * "Closet Sissy" for 18/21 sissy runs - a full-compliance S-grade run and a
 * never-answered-anything D-grade run produced the SAME label. `deep-bambi`,
 * `gone-bambi`, `sissy-princess` and `full-sissy` were unreachable.
 *
 * THE MODEL NOW HAS THREE PARTS:
 *
 *  1. HEAT-WEIGHTED, SIGNED VOTES. A heat-0 trivia answer is camouflage, not a
 *     preference signal, so it barely votes; a heat-5 surrender commit votes ~17x
 *     harder. And the DIRECTION of the answer matters: picking the least-endorsing
 *     option or timing out votes the tags DOWN, because declining a confession is
 *     evidence against "confessor", not for it.
 *
 *  2. A COMMITMENT SCALAR reconciles the three numbers the outro prints. Grade is
 *     score ratio, susceptibility is peak depth, and the classification used to be
 *     computed from neither - which is exactly how "grade A / 100% susceptible /
 *     Closet Sissy" happened. commitmentScore() blends compliance, depth reached
 *     and how much of the descent was actually walked, and it GATES which tier of
 *     archetype the run can land on. Grade and classification can no longer point
 *     in opposite directions.
 *
 *  3. TIERS. Each bank lists its archetypes tentative -> committed and tags them
 *     with `tier` (0..4; falls back to array position for banks that omit it).
 *     Commitment opens a WINDOW of tiers, and the tag votes choose the flavour
 *     WITHIN that window. The tentative tiers stay fully reachable - a run that
 *     refuses or bails simply never opens the window past tier 0/1 - but a long
 *     compliant descent can no longer be told it is a beginner.
 * -------------------------------------------------------------------------- */

/** Vote weight per prompt heat (0..5). Heat-0 trivia is 285/629 of the bank and
 *  is deliberately near-worthless as a signal; the hot end is where identity is
 *  actually expressed. */
const HEAT_VOTE = Object.freeze([0.15, 0.55, 1.0, 1.6, 2.1, 2.6]);

/** How commitment is assembled (weights sum to 1). Compliance leads because it is
 *  the one signal a refuser cannot fake: peak depth climbs even for someone who
 *  answers nothing (the descent runs on a clock, not on consent), and completion
 *  alone only proves you sat still. */
const COMMIT_W = Object.freeze({ compliance: 0.60, reach: 0.15, progress: 0.25 });

/** commitment -> tier window. `ceil` opens the top of the window, `floor` raises
 *  the bottom so a deeply committed run cannot be handed a tentative label. Both
 *  are expressed as fractions of the bank's own top tier, so a 3-archetype bank
 *  behaves the same as a 5-archetype one. */
const TIER_CEIL = Object.freeze({ at0: 0.18, span: 0.72 });   // opens tier 1 at ~0.27, tops out at ~0.90
const TIER_FLOOR = Object.freeze({ at0: 0.40, span: 0.62 });  // starts lifting at 0.40, pins the top tier near 1.0

/** heat window (0..5) the sequencer prefers per band; camouflage low, hot high. */
const HEAT_WINDOW = Object.freeze({
  [Band.Calibration]:  [0, 1],
  [Band.Establishing]: [1, 2],
  [Band.Deepening]:    [2, 4],
  [Band.Climax]:       [3, 5],
});
/** fast+correct play INTENSIFIES instead of shortening: above this velocity the
 *  heat window's upper edge widens by +1 within the band (never shrinks a band). */
const HOT_VELOCITY = 0.3;

/** PLAIN BEATS — the deep bands' breathing room.
 *
 *  rollSteer() has no probability gate: in Deepening and Climax the band weight
 *  is 0.65/1.0, so EVERY beat carrying options got a steer. The deep half of the
 *  run therefore read as wall-to-wall interference, and interference that never
 *  stops stops registering — there was no un-steered beat left for a steered one
 *  to feel different from. Same reason TRICK_CHANCE is a sprinkle: the gag needs
 *  a straight baseline, and by Deepening the baseline had disappeared.
 *
 *  A plain beat is ordinary low-heat trivia played completely straight: heat
 *  pulled down to the Calibration window, NO steer, NO timed ring. Everything
 *  else about it is a normal beat — same card, same voice seam, same grading —
 *  so it is camouflage, not a rest screen, and the player cannot use it as a
 *  reliable tell.
 *
 *  The share matches each band's growth (Deepening +20%, Climax +15%), which is
 *  the point: the beats we added are the boring ones. Spice is unchanged in
 *  absolute terms; it is simply spread thinner, and thinner in Deepening than in
 *  Climax so the pressure gradient still tilts toward the end of the run.
 *
 *  SCHEDULED, NOT ROLLED — same reasoning as COLOR_MIN_GAP. A per-beat roll both
 *  clumps and no-shows; a band that happened to roll zero plain beats would be
 *  exactly the wall the player is complaining about. The slots are recomputed
 *  from the LIVE planned count every call so a band stretched mid-flight by timid
 *  play re-spaces instead of leaving its plain beats bunched at the front. */
const PLAIN_SHARE = Object.freeze({
  [Band.Deepening]: 0.20,
  [Band.Climax]:    0.15,
});
/** Plain beats never open a band (the first beat sets the band's tone) and never
 *  land in its last two (the bubble guarantee owns those — see buildDescentBeat). */
const PLAIN_FIRST_SLOT = 1;
const PLAIN_TAIL_MARGIN = 3;
/** Heat window a plain beat draws from — deliberately Calibration's, not the
 *  band's. A "plain" heat-4 confession is still a confession. */
const PLAIN_HEAT_WINDOW = Object.freeze([0, 1]);

/** TRICK QUESTIONS (banks/*.json entries carrying `"trick": 1`) — deliberately
 *  broken variants of the boring heat-0 trivia (inverted units, scrambled
 *  categories, a colour where a number belongs), played completely straight:
 *  same card, same typewriter, same timer, same voice seam as a real question.
 *  Three of the four options are plausible-but-meaningless throwaways; the only
 *  "correct" one is an unrelated niche compliance line.
 *
 *  A SPRINKLE, NOT A THEME. The joke only works against an established baseline
 *  of real trivia, so tricks are Deepening/Climax only (never Calibration /
 *  Establishing / Recovery — see TRICK_CHANCE), never back-to-back, never a
 *  band's opening beat, never its last two (which the bubble guarantee owns),
 *  never a PLAIN slot (that beat's job is to be genuinely unremarkable), and
 *  capped per run. At the base pacing that leaves 15 Deepening + 14 Climax beats
 *  rollable, so the rates below land ~1.2 + ~1.7 ~= 2-3 tricks in an ~81-beat run
 *  — the same absolute count as before the run was stretched, which is intended:
 *  the gag does not scale with length any more than it scales with laps. Same HARD within-run no-repeat as any other
 *  prompt (they share usedIds), and the bank holds 52 of them per niche. */
const TRICK_CHANCE = Object.freeze({
  [Band.Deepening]: 0.08,
  [Band.Climax]:    0.12,
});
const TRICK_MAX_PER_RUN = 4;       // endless laps included — the gag does not scale

/** COLOUR HARVEST SPACING (core/palette.js). The colour questions used to be
 *  literally beats 1-5 of the run, which read as a SURVEY BLOCK bolted to the
 *  front of the assessment. They are now spread evenly across the front stretch
 *  of Calibration so ordinary heat-0 trivia falls between them and each one
 *  arrives as an incidental question rather than part of a form.
 *
 *  The window is [COLOR_FIRST_SLOT .. planned - COLOR_TAIL_MARGIN] beat indices
 *  (0-based, within the band), which at the base 21-beat Calibration puts the
 *  five colour beats at indices 1,5,8,12,15 — questions 2,6,9,13,16 of the run.
 *  Both edges are load-bearing:
 *    - the FIRST slot is never index 0: the run opens on plain trivia, so the
 *      colour question reads as one of the survey's own questions.
 *    - the TAIL MARGIN keeps the last colour beat well clear of (a) the band's
 *      bubble guarantee, which forces a BubblePop — and therefore that
 *      mechanic's always-on loom backdrop — into the last two planned beats,
 *      and (b) depth 0.20, where boot's HUD dial starts crossfading its spiral
 *      glyph in. At index 15 of 21 the ramp is ~0.15 (+<=0.06 velocity nudge). */
const COLOR_FIRST_SLOT = 1;
const COLOR_TAIL_MARGIN = 6;
/** Ordinary beats that must fall between two colour questions. The schedule
 *  already spaces them, but a band re-planned mid-flight (timid play stretches
 *  it, recovered play snaps it back) can shift two slots onto adjacent beats —
 *  and two colour questions in a row is exactly the survey-block read this
 *  spacing exists to kill. */
const COLOR_MIN_GAP = 2;

/** interviewer asides: roll chance per beat (never twice in a row; Recovery always speaks). */
const INTERVIEWER_ROLL = 0.4;

/** timed-pressure beats (Deepening + sub-0.9 Climax; MC4/YesNo only; never back-to-back). */
const TIMED_ROLL = 0.30;
const TIMED_MS_MAX = 9000;
const TIMED_MS_MIN = 6000;
const TIMED_MECHANICS = new Set([Mechanic.MC4, Mechanic.YesNo]);

/** interlude valleys: synthetic non-graded pauses in Deepening + Climax, spaced
 *  ~one per INTERLUDE_EVERY graded beats (so long bands don't run dry of valleys). */
const INTERLUDE_MS_MIN = 5000;
const INTERLUDE_MS_MAX = 6500;
const INTERLUDE_EVERY = 8;         // aim: ~one valley per 8 graded beats within a deep band

/** steer catalogs per band (Calibration/Recovery steer nothing — inv of the fiction). */
const STEER_POOL = Object.freeze({
  [Band.Establishing]: [Steer.Magnet, Steer.SizeSkew, Steer.OpacitySkew, Steer.LateBloom, Steer.Defocus, Steer.AssistClick, Steer.DriftResolve],
  [Band.Deepening]:    [Steer.Magnet, Steer.Flee, Steer.Exile, Steer.Crowd, Steer.SizeSkew, Steer.OpacitySkew, Steer.OccludeGif, Steer.Defocus, Steer.ShrinkHit, Steer.DragReveal, Steer.AssistClick, Steer.DriftResolve, Steer.Decay, Steer.MeltAway],
  [Band.Climax]:       [Steer.Flee, Steer.Exile, Steer.Crowd, Steer.OccludeGif, Steer.ShrinkHit, Steer.DragReveal, Steer.HoldRefuse, Steer.NestedNag, Steer.OverflowHit, Steer.Tunnel, Steer.Decay, Steer.Magnet, Steer.MeltAway],
});

const OPTION_MECHANICS = new Set([Mechanic.MC4, Mechanic.YesNo, Mechanic.BubblePop, Mechanic.Mono, Mechanic.Funnel, Mechanic.Destruct]);

const RECOVERY_LINES = Object.freeze([
  'Take a slow breath in, and let it go. Notice the surface you are resting on.',
  'Wiggle your fingers and your toes. You are coming back up, gently.',
  'Let your eyes soften, then open when you are ready. Almost surfaced now.',
  'You are awake, clear, and entirely yourself again.',
]);

/* ----------------------------------------------------------------------------
 * seeded RNG (mulberry32). Deterministic engine rolls per niche/seed so a fixed
 * answer sequence reproduces; production varies because answers vary.
 * -------------------------------------------------------------------------- */
function makeRng(seedInt) {
  let s = seedInt >>> 0;
  return function () {
    s = (s + 0x6D2B79F5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const nowMs = () => (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now();
const normStr = (s) => String(s == null ? '' : s).trim().toLowerCase().replace(/\s+/g, ' ').replace(/[.!?,]+$/g, '');

/* ----------------------------------------------------------------------------
 * PLACEHOLDER BANK — used when no bank is supplied so harness.html still runs.
 * Niche-parameterised; carries archetypes (with voting tags), heat-spread
 * prompts across all bands, and a small ladder (exercises requires + affirmed).
 * -------------------------------------------------------------------------- */
function placeholderBank(niche) {
  return {
    niche,
    version: 'placeholder',
    archetypes: [
      { id: 'softened', name: 'The Softened', tags: ['soft', 'praise', 'relax', 'melt'] },
      { id: 'obedient', name: 'The Obedient', tags: ['obey', 'yes', 'submit', 'good'] },
      { id: 'emptied',  name: 'The Emptied',  tags: ['blank', 'empty', 'quiet', 'drop'] },
    ],
    prompts: [
      { id: 'ph_c0', text: 'Warm-up: what is 7 + 5?', answer: 0, heat: 0, tags: ['relax'], flavors: [], weight: 1, mechanicHints: [Mechanic.MC4], options: ['12', '11', '13', '10'] },
      { id: 'ph_c1', text: 'How many days are in a week?', answer: 2, heat: 0, tags: ['relax'], flavors: [], weight: 1, mechanicHints: [Mechanic.MC4], options: ['5', '6', '7', '8'] },
      { id: 'ph_c2', text: 'Take a slow breath. Does that feel a little nicer?', answer: true, heat: 1, tags: ['soft', 'relax'], flavors: [], weight: 1, mechanicHints: [Mechanic.YesNo] },
      { id: 'ph_e0', text: 'You like being told you did well.', answer: true, heat: 2, tags: ['praise', 'good'], flavors: [], weight: 1, mechanicHints: [Mechanic.YesNo] },
      { id: 'ph_e1', text: 'Which feels easier right now?', answer: 0, heat: 2, tags: ['soft', 'melt'], flavors: [], weight: 1, mechanicHints: [Mechanic.MC4], options: ['Letting go', 'Holding on', 'Resisting', 'Thinking hard'] },
      { id: 'ph_e2', text: 'How relaxed are you feeling?', answer: 3, heat: 1, tags: ['relax', 'soft'], flavors: [], weight: 0.8, mechanicHints: [Mechanic.CheckIn] },
      { id: 'ph_d0', text: 'Agreeing is simpler than deciding.', answer: true, heat: 3, tags: ['obey', 'yes'], flavors: [], weight: 1, mechanicHints: [Mechanic.YesNo] },
      { id: 'ph_d1', text: 'The quiet feels good. Sink into it?', answer: 0, heat: 3, tags: ['drop', 'quiet'], flavors: [], weight: 1, mechanicHints: [Mechanic.MC4], options: ['Yes, sink', 'Stay up', 'Fight it', 'Later'] },
      { id: 'ph_x0', text: 'Say it: I am easy to guide.', answer: 'I am easy to guide', heat: 4, tags: ['obey', 'submit'], flavors: [], weight: 1, mechanicHints: [Mechanic.Mantra, Mechanic.Mono], affirmsMantra: 1 },
      { id: 'ph_x1', text: 'Empty is comfortable.', answer: 0, heat: 5, tags: ['blank', 'empty'], flavors: [], weight: 1, mechanicHints: [Mechanic.Destruct], options: ['Empty', 'Full', 'Busy', 'Loud'] },
      // ladder-only rungs (surface via the belief ladder, in order):
      { id: 'ph_lad1', text: 'It feels good to relax.', answer: 'It feels good to relax', heat: 2, tags: ['soft', 'yes'], flavors: [], weight: 1, mechanicHints: [Mechanic.Mantra], affirmsMantra: 1 },
      { id: 'ph_lad2', text: 'Say it: good and obedient.', answer: 'good and obedient', heat: 3, tags: ['obey', 'good'], flavors: [], weight: 1, mechanicHints: [Mechanic.Mantra, Mechanic.Mono], affirmsMantra: 1, requires: { band: Band.Deepening } },
      { id: 'ph_lad3', text: 'Say it: I obey without thinking.', answer: 'I obey without thinking', heat: 4, tags: ['obey', 'submit'], flavors: [], weight: 1, mechanicHints: [Mechanic.Mono], affirmsMantra: 1, requires: { band: Band.Climax, affirmed: ['ph_lad2'] } },
    ],
    ladders: [
      { id: 'ph-obey-ladder', archetypeId: 'obedient', rungs: [
        { promptId: 'ph_lad1' },
        { promptId: 'ph_lad2', requires: { band: Band.Deepening } },
        { promptId: 'ph_lad3', requires: { band: Band.Climax, affirmed: ['ph_lad2'] } },
      ] },
    ],
  };
}

/* ============================================================================
 * FACTORY
 * ==========================================================================*/
/**
 * @param {Object} deps
 * @param {import('./contracts.js').PromptBank=} deps.bank
 * @param {Object=} deps.reward   reward.js instance { planFor, resolve } (optional)
 * @param {Object=} deps.ai       ai.js instance { askAI } (optional; never blocks the loop)
 * @param {import('./contracts.js').BootConfig=} deps.config
 * @param {Object=} deps.stats
 */
export function createEngine({ bank, reward, ai, config, stats } = {}) {
  const cfg = config || {};
  const usableBank = (bank && Array.isArray(bank.prompts) && bank.prompts.length) ? bank : null;
  const niche = NICHES.includes(cfg.niche) ? cfg.niche
    : (usableBank && NICHES.includes(usableBank.niche)) ? usableBank.niche
    : Niche.Bambi;

  const activeBank = usableBank || placeholderBank(niche);
  const theme = themeOf(activeBank);          // resolved fiction voice (interviewer asides)
  const prompts = Array.isArray(activeBank.prompts) ? activeBank.prompts : [];
  const archetypes = (Array.isArray(activeBank.archetypes) && activeBank.archetypes.length)
    ? activeBank.archetypes : placeholderBank(niche).archetypes;
  const ladders = Array.isArray(activeBank.ladders) ? activeBank.ladders : [];

  const endless = !!cfg.endless;
  const steerValve = clamp01(cfg.steerValve == null ? 1 : cfg.steerValve);
  // Cross-run variety: an EXPLICIT cfg.seed reproduces a run (harness/tests);
  // no seed -> genuine per-run entropy so the prompt draw is freshly shuffled
  // every run (the "constantly the same questions" fix — the old null path
  // hashed a constant 'run', giving an identical rng stream on every play).
  const seedSource = (cfg.seed != null)
    ? (niche + '|' + cfg.seed)
    : (niche + '|' + Date.now() + '|' + Math.random());
  const seedInt = Math.floor(hash01(seedSource) * 0xFFFFFFFF);
  const rng = makeRng(seedInt);

  const byId = new Map(prompts.map((p) => [p.id, p]));
  const trickPrompts = prompts.filter((p) => p && p.trick);   // served only via selectTrickPrompt()
  /* COLOUR HARVEST (core/palette.js): the bank's colour-preference prompts,
   * spread across the front of Calibration (COLOR_FIRST_SLOT/COLOR_TAIL_MARGIN)
   * so the player has named their colours long before anything can weave a
   * spiral out of them, without the questions arriving as a block. Arming the harvest with
   * the real pool size means a bank without colour prompts closes it immediately
   * and every spiral simply keeps its themed palette. */
  const colorPrompts = prompts.filter((p) => p && Array.isArray(p.tags) && p.tags.includes(COLOR_TAG) && !p.trick);
  const colorIds = new Set(colorPrompts.map((p) => p.id));
  const colorPlan = Math.min(MAX_COLOR_BEATS, colorPrompts.length);
  beginHarvest(colorPlan);
  const ladderPromptIds = new Set();
  for (const l of ladders) for (const r of (l.rungs || [])) ladderPromptIds.add(r.promptId);

  // ---- run state ----------------------------------------------------------
  const state = { depth: 0, velocity: 0, band: Band.Calibration };
  const route = { primary: niche, primaryArchetypeId: '', secondaryArchetypeId: undefined, primaryShare: 0, secondaryShare: 0 };
  const result = emptyResult(niche);
  result.endless = endless;

  const usedIds = new Set();              // prompts already served this run (HARD within-run no-repeat)
  let lastServedId = '';                  // most-recent non-ladder pick (last-resort reuse never repeats it)
  const servedRungs = new Set();          // ladder rungs already delivered
  const affirmedIds = new Set();          // mantra prompt-ids affirmed (for requires.affirmed)
  const archetypeVotes = new Map();       // archetypeId -> vote weight (route reveal)

  let phase = 'descent';                  // 'descent' | 'recovery' | 'done'
  let descentIdx = 0;                     // index into DESCENT_BANDS
  let beatsInBand = 0;                    // beats already BUILT in the current band
  let lapCount = 0;                       // endless laps completed
  let recoveryIdx = 0;                    // recovery step built so far
  let recoveryStart = 0;                  // depth at recovery entry
  let aborting = false;
  let lastBeat = null;
  let lastPrimarySteer = Steer.None;
  let beatSeq = 0;
  let totalBeatsBuilt = 0;

  let gradedAnswered = 0;                 // graded beats answered
  let correctCount = 0;                   // graded correct answers
  let descentAnswered = 0;                // descent beats answered (route-share progress)
  let accentFlavor = '';                  // latest AI accent (best-effort, non-blocking)

  // ---- presentation/meta state (BeatMeta + scheduling memories) -----------
  let streakRun = 0;                      // consecutive-correct graded answers (meta.streak)
  let qIndex = 0;                         // 1-based graded-question counter (interludes/recovery carry)
  let lastEmittedBand = null;             // bandNew detection (first beat emitted in a band)
  let lastBeatHadLine = false;            // interviewer aside never rolls twice in a row
  let lastBeatTimed = false;              // no two timed-pressure beats back-to-back
  const lineMem = {};                     // band -> last-two interviewer line indices (no-repeat)
  const interludesInBand = {};            // band -> interludes inserted so far this band (reset per endless lap)
  let bubbleServedInBand = false;         // bubble guarantee: >=1 BubblePop per descent band
  let colorBeatsServed = 0;               // colour beats served (drives the slot schedule)
  let lastColorBeat = -99;                // beat index of the last colour beat (COLOR_MIN_GAP)
  let trickServed = 0;                    // trick questions served this run (TRICK_MAX_PER_RUN)
  let lastBeatWasTrick = false;           // never two trick questions back-to-back
  const servedByBand = {};                // band -> actual graded beats served (qTotal estimate)

  const t0 = nowMs();

  // ---- reward shim (fallback when reward.js not supplied) ------------------
  const planFor = (b, d, p) => {
    if (reward && typeof reward.planFor === 'function') {
      try { return reward.planFor(b, d, p); } catch (_e) { /* fall through */ }
    }
    return fallbackPlan(b, d, p);
  };
  const resolveReward = (plan, ev, sd) => {
    if (reward && typeof reward.resolve === 'function') {
      try { return reward.resolve(plan, ev, sd); } catch (_e) { /* fall through */ }
    }
    return fallbackResolve(plan, sd);
  };
  function fallbackPlan(b, d, prompt) {
    let mode;
    switch (b) {
      case Band.Calibration:  mode = RewardMode.Honest; break;
      case Band.Establishing: mode = RewardMode.SpicierPick; break;
      case Band.Deepening:    mode = RewardMode.ScaleWithScore; break;
      case Band.Climax:       mode = RewardMode.VariableRatio; break;
      default:                mode = RewardMode.Honest; break;
    }
    const rec = b === Band.Recovery;
    return {
      mode,
      baseChance: rec ? 0 : (mode === RewardMode.VariableRatio ? clamp01(0.30 + 0.30 * smoothstep(d)) : 1),
      baseIntensity: rec ? 0 : clamp01(0.15 + smoothstep(d) * 0.85),
      kind: rec ? RewardKind.None : (prompt && prompt.heat >= 4 ? RewardKind.Drop : d >= 0.5 ? RewardKind.Flash : RewardKind.Chime),
    };
  }
  function fallbackResolve(plan, sd) {
    const p = plan || { mode: RewardMode.Honest, baseChance: 0, baseIntensity: 0, kind: RewardKind.None };
    const decoupled = p.mode !== RewardMode.Honest;
    let fire;
    if (p.mode === RewardMode.VariableRatio) fire = rng() < (p.baseChance || 0);
    else if (p.mode === RewardMode.Honest) fire = (p.baseChance > 0) && sd > 0;
    else fire = (p.baseChance || 0) > 0;
    return { fire, intensity: fire ? clamp01(p.baseIntensity) : 0, kind: fire ? (p.kind || RewardKind.None) : RewardKind.None, decoupled };
  }

  // ---- feed-forward seed (continuity): a tiny nudge from last run ----------
  (function seedFromPrior() {
    const prior = cfg.priorRun;
    if (prior && prior.route && prior.route.primaryArchetypeId) {
      const id = prior.route.primaryArchetypeId;
      // Faint head-start, easily overtaken — worth roughly two hot answers on the
      // heat-weighted scale (votes used to be raw counts, where 0.5 meant the same).
      if (archetypes.some((a) => a.id === id)) archetypeVotes.set(id, 4);
    }
  })();

  /* --------------------------------------------------------------------------
   * REQUIRES EVALUATOR (belief-ladder + prompt gating) against live state.
   * ------------------------------------------------------------------------ */
  function requiresOk(req) {
    if (!req) return true;
    if (typeof req.minDepth === 'number' && state.depth < req.minDepth) return false;
    if (req.band && bandIndex(state.band) < bandIndex(req.band)) return false;
    if (Array.isArray(req.tagsAny) && req.tagsAny.length) {
      if (!req.tagsAny.some((t) => (result.tagTallies[t] || 0) > 0)) return false;
    }
    if (Array.isArray(req.tagsAll) && req.tagsAll.length) {
      if (!req.tagsAll.every((t) => (result.tagTallies[t] || 0) > 0)) return false;
    }
    if (Array.isArray(req.affirmed) && req.affirmed.length) {
      if (!req.affirmed.every((id) => affirmedIds.has(id))) return false;
    }
    if (typeof req.minScoreRate === 'number') {
      const rate = gradedAnswered > 0 ? correctCount / gradedAnswered : 1;
      if (rate < req.minScoreRate) return false;
    }
    return true;
  }

  /* --------------------------------------------------------------------------
   * PROMPT SELECTION — ladder-first (deep bands), then heat-banded weighted.
   * ------------------------------------------------------------------------ */
  /** Which archetype's ladder to deepen first. Follows the REVEALED route so the
   *  ladders a player climbs match the classification they are being walked toward
   *  (votes alone can now be negative, which made the raw argmax meaningless). */
  function leadingArchetypeId() {
    if (route.primaryArchetypeId) return route.primaryArchetypeId;
    let best = null; let bestV = -Infinity;
    for (const a of archetypes) {
      const v = archetypeVotes.get(a.id) || 0;
      if (v > bestV) { bestV = v; best = a.id; }
    }
    return best || (archetypes[0] && archetypes[0].id) || niche;
  }

  function nextLadderPrompt() {
    const lead = leadingArchetypeId();
    const ordered = [...ladders.filter((l) => l.archetypeId === lead), ...ladders.filter((l) => l.archetypeId !== lead)];
    for (const l of ordered) {
      const rungs = l.rungs || [];
      for (let i = 0; i < rungs.length; i++) {
        const r = rungs[i];
        if (servedRungs.has(r.promptId)) continue;
        // rungs are ordered: all prior rungs must already be served
        let priorOk = true;
        for (let j = 0; j < i; j++) { if (!servedRungs.has(rungs[j].promptId)) { priorOk = false; break; } }
        if (!priorOk) break;
        const p = byId.get(r.promptId);
        if (!p) { servedRungs.add(r.promptId); continue; } // skip a dangling rung id
        if (!requiresOk(r.requires)) break;   // gate not open yet -> this ladder stalls here
        if (!requiresOk(p.requires)) break;
        return p;
      }
    }
    return null;
  }

  /** Band heat window; fast+correct play (hot velocity) widens the top edge +1. */
  function heatWindowFor(band) {
    const win = HEAT_WINDOW[band] || [0, 5];
    if (state.velocity > HOT_VELOCITY) return [win[0], Math.min(5, win[1] + 1)];
    return win;
  }

  // mode: 'strict' = inside the band heat window; 'wide' = softer allowed + rough hot
  // ceiling; 'any' = ignore heat entirely (last no-repeat widening before reuse).
  // winOverride: a plain beat forces PLAIN_HEAT_WINDOW instead of the band's own,
  // which is the whole difference between it and an ordinary deep-band beat.
  function eligible(band, mode, winOverride) {
    const win = winOverride || heatWindowFor(band);
    const out = [];
    for (const p of prompts) {
      if (usedIds.has(p.id)) continue;
      if (ladderPromptIds.has(p.id)) continue;  // ladder-owned prompts arrive only via the ladder
      if (p.trick) continue;                    // trick questions arrive only via selectTrickPrompt()
      if (colorIds.has(p.id)) continue;         // colour questions arrive only via selectColorPrompt()
      const heat = typeof p.heat === 'number' ? p.heat : 0;
      if (mode === 'strict') { if (heat < win[0] - 0.001 || heat > win[1] + 0.001) continue; }
      else if (mode === 'wide') { if (heat > win[1] + 1.5) continue; } // softer prompts ok, rough hot ceiling
      // 'any' -> no heat filter at all
      if (!requiresOk(p.requires)) continue;
      out.push(p);
    }
    return out;
  }

  function weightedPick(pool) {
    let total = 0;
    for (const p of pool) total += Math.max(0.0001, +p.weight || 1);
    let r = rng() * total;
    for (const p of pool) { r -= Math.max(0.0001, +p.weight || 1); if (r <= 0) return p; }
    return pool[pool.length - 1];
  }

  /** Roll for a TRICK QUESTION (see the TRICK_* block). Returns the prompt when
   *  it fires, else null — every guard here exists to keep it a sprinkle. */
  function selectTrickPrompt(band) {
    const chance = TRICK_CHANCE[band] || 0;
    if (chance <= 0) return null;                       // Deepening/Climax only
    if (trickServed >= TRICK_MAX_PER_RUN) return null;
    if (lastBeatWasTrick) return null;                  // never back-to-back
    const planned = plannedBeats(band);
    if (beatsInBand < 1) return null;                   // never a band's opening beat
    if (beatsInBand >= planned - 2) return null;        // leave the tail to the bubble guarantee
    if (rng() >= chance) return null;
    const pool = trickPrompts.filter((p) => !usedIds.has(p.id) && requiresOk(p.requires));
    if (!pool.length) return null;                      // no-repeat: a served trick never returns
    const p = weightedPick(pool);
    usedIds.add(p.id);
    trickServed += 1;
    return p;
  }

  /** Beat index (0-based, within Calibration) the i-th colour question is due
   *  at: evenly spaced across [COLOR_FIRST_SLOT .. planned - COLOR_TAIL_MARGIN].
   *  Recomputed from the LIVE planned count every call, so a band stretched by
   *  timid play simply spreads the remaining colour beats wider — they can never
   *  drift past the tail margin. Pure w.r.t. its arguments. */
  function colorSlot(i, n, planned) {
    const lo = COLOR_FIRST_SLOT;
    const hi = Math.max(lo, planned - COLOR_TAIL_MARGIN);
    if (n <= 1) return Math.round((lo + hi) / 2);
    return lo + Math.round((i * (hi - lo)) / (n - 1));
  }

  /** COLOUR QUESTION (core/palette.js). Innocuous "which colour do you like
   *  best?" beats whose options ARE colours; the picks build the run's spiral.
   *
   *  SPACING — spread across the front of Calibration on the colorSlot()
   *  schedule (see COLOR_FIRST_SLOT / COLOR_TAIL_MARGIN), rolled ahead of trick
   *  rolls / ladders / the weighted draw so a due colour beat always wins its
   *  slot. `>=` (not `===`) against the slot means a slot can never be MISSED:
   *  a schedule that shifts under a re-planned band just serves on the next beat.
   *
   *  ORDERING GUARANTEE — every colour question still lands, and the harvest
   *  still closes, inside Calibration and before any spiral:
   *    - the fullscreen loom garnish (effects.js) needs depth >= 0.10 and
   *      intensity >= 0.20 — reachable late in Calibration now that the last
   *      colour beat sits around index 10 — so the REAL guarantee is that
   *      effects.js drops 'spiral' from the garnish bag (both the normal draw and
   *      the jackpot force) while harvestOpen().
   *    - the BubblePop loom backdrop (beats.js) is unconditional on that
   *      mechanic, so buildDescentBeat REFUSES BubblePop in Calibration while
   *      the harvest is open (see the spiralHold guard); the band's bubble
   *      guarantee only fires in the last two planned beats, four beats after
   *      the last colour slot, by which point the harvest is shut.
   *    - the 'watch' interlude spiral is Deepening/Climax only.
   *    - boot's HUD dial glyph only crossfades in above depth 0.20; the tail
   *      margin keeps the last colour beat under it.
   *
   *  Returns null once 4 distinct colours are in, once the plan is spent, until
   *  the next slot is due, or as soon as the run leaves Calibration (which also
   *  shuts the harvest for good). */
  function selectColorPrompt(band) {
    if (band !== Band.Calibration) { closeHarvest(); return null; }
    if (!harvestWantsMore()) return null;
    const planned = plannedBeats(band);
    if (beatsInBand >= planned - 2) return null;             // never collide with the bubble guarantee
    if (beatsInBand < colorSlot(colorBeatsServed, colorPlan, planned)) return null; // not due yet
    if (colorBeatsServed > 0 && beatsInBand < lastColorBeat + COLOR_MIN_GAP) return null; // never back-to-back
    const pool = colorPrompts.filter((p) => !usedIds.has(p.id) && requiresOk(p.requires));
    if (!pool.length) { closeHarvest(); return null; }
    const p = weightedPick(pool);
    usedIds.add(p.id);
    colorBeatsServed += 1;
    lastColorBeat = beatsInBand;
    noteColorServed();
    return p;
  }

  /** True when the beat about to be built at `beatsInBand` is a scheduled PLAIN
   *  beat. Recomputed from the live planned count every call (see PLAIN_SHARE).
   *
   *  Slots are spread evenly across [PLAIN_FIRST_SLOT .. planned-PLAIN_TAIL_MARGIN].
   *  With the shipped pacing that is 4 of 19 Deepening beats at indices 1,6,11,16
   *  and 2 of 16 Climax beats at indices 1,13 — never adjacent at any planned
   *  length, so a plain beat is always an island in steered water, never a lull. */
  function plainSlots(band, planned) {
    const share = PLAIN_SHARE[band] || 0;
    if (share <= 0) return null;
    const lo = PLAIN_FIRST_SLOT;
    const hi = planned - PLAIN_TAIL_MARGIN;
    if (hi < lo) return null;
    const want = Math.min(hi - lo + 1, Math.round(planned * share));
    if (want <= 0) return null;
    const span = hi - lo;
    const out = new Set();
    for (let i = 0; i < want; i++) {
      out.add(want === 1 ? lo + Math.round(span / 2)
                         : lo + Math.round((span * i) / (want - 1)));
    }
    return out;
  }
  function isPlainSlot(band) {
    const slots = plainSlots(band, plannedBeats(band));
    return !!slots && slots.has(beatsInBand);
  }

  function selectPrompt(band, plain) {
    // PLAIN BEAT: skip the colour harvest (Calibration-only anyway), the trick
    // roll and the belief ladder outright. Every one of those is a beat with a
    // point to make; a plain beat's entire job is to have none.
    if (plain) {
      let pool = eligible(band, 'strict', PLAIN_HEAT_WINDOW);
      if (!pool.length) pool = eligible(band, 'strict', [0, 2]);   // widen one notch
      if (pool.length) {
        const p = weightedPick(pool);
        usedIds.add(p.id); lastServedId = p.id;
        return p;
      }
      // Low-heat pool exhausted (only a tiny bank reaches this): fall through and
      // serve an ordinary beat rather than repeating a question.
    }
    // COLOUR QUESTIONS: the run's opening beats, ahead of everything else.
    const color = selectColorPrompt(band);
    if (color) { lastServedId = color.id; return color; }
    // TRICK QUESTION: rolled ahead of everything else so it reads as an ordinary
    // beat of the sequence rather than an interruption of one.
    const trick = selectTrickPrompt(band);
    if (trick) { lastServedId = trick.id; return trick; }
    // Belief-ladder gets priority in the deep bands (it IS the reactive spine).
    if ((band === Band.Establishing || band === Band.Deepening || band === Band.Climax) && rng() < 0.55) {
      const rung = nextLadderPrompt();
      if (rung) { servedRungs.add(rung.id); usedIds.add(rung.id); return rung; }
    }
    // WITHIN-RUN NO-REPEAT: a served id never returns. If the heat window is dry,
    // WIDEN (softer -> any heat) before ever reusing — the enlarged banks make the
    // reuse path effectively unreachable in production.
    let pool = eligible(band, 'strict');
    if (!pool.length) pool = eligible(band, 'wide');     // widen the heat window
    if (!pool.length) pool = eligible(band, 'any');      // ignore heat entirely, still no-repeat
    if (!pool.length) {
      // Absolute last resort (only a tiny/placeholder bank can reach here): permit
      // reuse, but keep the just-served id out so we never repeat back-to-back.
      const keep = lastServedId;
      usedIds.clear();
      if (keep) usedIds.add(keep);
      pool = eligible(band, 'any');
      if (!pool.length && keep) { usedIds.delete(keep); pool = eligible(band, 'any'); }
    }
    if (!pool.length) pool = prompts.filter((p) => !ladderPromptIds.has(p.id) && p.id !== lastServedId);
    if (!pool.length) pool = prompts.filter((p) => !ladderPromptIds.has(p.id));
    if (!pool.length) pool = prompts.slice();
    if (!pool.length) return syntheticPrompt(band);
    const p = weightedPick(pool);
    usedIds.add(p.id);
    lastServedId = p.id;
    return p;
  }

  function syntheticPrompt(band) {
    return { id: `syn-${band}-${beatSeq}`, text: 'Just stay with the voice a moment.', answer: true, heat: 1, tags: ['soft'], flavors: [], weight: 1, mechanicHints: [Mechanic.YesNo] };
  }

  /* --------------------------------------------------------------------------
   * MECHANIC + OPTIONS
   * ------------------------------------------------------------------------ */
  function pickMechanic(prompt, band) {
    if (band === Band.Recovery) return Mechanic.CheckIn;
    const hints = (prompt && Array.isArray(prompt.mechanicHints)) ? prompt.mechanicHints.filter((h) => MECHANICS.includes(h)) : [];
    if (hints.length) return hints[Math.floor(rng() * hints.length)];
    switch (band) {
      case Band.Calibration:  return Mechanic.MC4;
      case Band.Establishing: return rng() < 0.5 ? Mechanic.YesNo : Mechanic.MC4;
      default:                return Mechanic.MC4;
    }
  }

  function buildOptions(mechanic, prompt) {
    const ans = prompt ? prompt.answer : undefined;
    const provided = Array.isArray(prompt && prompt.options) ? prompt.options : null;
    if (mechanic === Mechanic.Mantra || mechanic === Mechanic.CheckIn) return [];

    if (mechanic === Mechanic.YesNo) {
      const yes = (ans === true || ans === 1 || ans === 'yes' || ans === 'true' || ans === 'Yes');
      return [
        { index: 0, label: 'Yes', isCorrect: yes, score: yes ? 1 : 0 },
        { index: 1, label: 'No', isCorrect: !yes, score: !yes ? 1 : 0 },
      ];
    }
    if (mechanic === Mechanic.Mono) {
      const label = (provided && provided[0]) || (typeof ans === 'string' && ans) || 'Yes';
      return [{ index: 0, label: String(label), isCorrect: true, score: 1 }];
    }
    if (mechanic === Mechanic.BubblePop && !provided) {
      const pop = (typeof ans === 'boolean') ? ans : (typeof ans === 'number') ? (ans === 0) : true;
      return [
        { index: 0, label: 'Pop it', isCorrect: pop, score: pop ? 1 : 0 },
        { index: 1, label: 'Let it float', isCorrect: !pop, score: !pop ? 1 : 0 },
      ];
    }
    // MC4 / Destruct / Funnel / BubblePop-with-options
    let labels = provided ? provided.slice(0, 4).map(String) : ['Yes', 'Maybe', 'Not really', 'No'];
    if (labels.length < 2) labels = labels.concat(['No', 'Maybe']).slice(0, 2);
    // FREE CHOICE (`"freeChoice": 1`): the prompt asks a PREFERENCE, so it has no
    // wrong answer — every option is correct and worth full marks. gradeBeat then
    // reads correct/score-1/beatMax-1 whichever way the player goes, which keeps
    // the streak, the velocity and the correct-rate honest across the colour
    // beats instead of punishing someone for liking green. (The tags still vote
    // the route exactly like any other heat-0 trivia beat.)
    if (prompt && prompt.freeChoice) {
      return labels.map((label, index) => ({ index, label, isCorrect: true, score: 1 }));
    }
    let correctIdx = (typeof ans === 'number' && ans >= 0 && ans < labels.length) ? ans : 0;
    const opts = labels.map((label, index) => ({ index, label, isCorrect: index === correctIdx, score: index === correctIdx ? 1 : 0 }));
    if (!opts.some((o) => o.isCorrect)) { opts[0].isCorrect = true; opts[0].score = 1; }
    return opts;
  }

  /* --------------------------------------------------------------------------
   * STEERING ROLL — band-weighted, valve-scaled, no back-to-back same primary.
   * ------------------------------------------------------------------------ */
  function rollSteer(band, depth, hasOptions) {
    const w = (STEER_BAND_WEIGHT[band] || 0) * steerValve;
    const pool = STEER_POOL[band];
    if (!hasOptions || w <= 0 || !pool || !pool.length) {
      lastPrimarySteer = Steer.None;
      return { primary: Steer.None, secondary: [], intensity: 0 };
    }
    const choices = pool.length > 1 ? pool.filter((s) => s !== lastPrimarySteer) : pool;
    const primary = choices[Math.floor(rng() * choices.length)] || pool[0];
    lastPrimarySteer = primary;
    const intensity = clamp01(w * lerp(0.7, 1, depth));
    const nSecondary = w > 0.8 ? 2 : w > 0.5 ? 1 : 0;
    const secondary = [];
    if (nSecondary > 0) {
      const rest = pool.filter((s) => s !== primary);
      for (let i = 0; i < nSecondary && rest.length; i++) {
        const idx = Math.floor(rng() * rest.length);
        secondary.push(rest.splice(idx, 1)[0]);
      }
    }
    return { primary, secondary, intensity };
  }

  /* --------------------------------------------------------------------------
   * DEPTH RAMP + BAND PACING (velocity shortens/stretches a band).
   * ------------------------------------------------------------------------ */
  function ceilForBand(band) {
    switch (band) {
      case Band.Calibration:  return BAND_DEPTH_FLOOR[Band.Establishing];
      case Band.Establishing: return BAND_DEPTH_FLOOR[Band.Deepening];
      case Band.Deepening:    return BAND_DEPTH_FLOOR[Band.Climax];
      case Band.Climax:       return CLIMAX_PEAK;
      default:                return 1;
    }
  }
  function plannedBeats(band) {
    // Velocity NEVER shortens a band below its base (fast play must not shrink
    // the run — engagement is rewarded with INTENSITY, not less game): positive
    // velocity keeps the base and instead widens the heat window (heatWindowFor)
    // and nudges depth harder (descentDepth). Timid play still stretches.
    const base = BASE_BEATS[band] || 5;
    if (state.velocity >= 0) return Math.max(MIN_BEATS, Math.min(MAX_BEATS, base));
    const stretched = Math.round(base * (1 - 0.35 * state.velocity)); // negative v -> more beats
    return Math.max(MIN_BEATS, Math.min(MAX_BEATS, stretched));
  }
  function descentDepth(band, beatIndex, planned) {
    let floor = BAND_DEPTH_FLOOR[band];
    let ceil = ceilForBand(band);
    if (endless && lapCount > 0) {
      const bonus = Math.min(0.15, lapCount * 0.05); // endless keeps deepening each lap
      floor = clamp01(floor + bonus); ceil = clamp01(ceil + bonus);
    }
    const frac = planned > 1 ? beatIndex / (planned - 1) : 1; // 0..1 across the band
    const base = lerp(floor, ceil, smoothstep(frac));
    // small reactive nudge; fast+correct play pushes slightly harder still
    return clamp01(base + state.velocity * 0.04 + Math.max(0, state.velocity) * 0.02);
  }

  /* --------------------------------------------------------------------------
   * ANSWER RECORDING + VELOCITY + ROUTE
   * ------------------------------------------------------------------------ */
  function gradeBeat(beat, ev) {
    // Interlude valleys are never graded and never wrong (beats.js may commit
    // them as {value:true, mechanic:'interlude'}). score 0 / beatMax 0 keeps
    // maxScore untouched if a trajectory entry is recorded.
    if (beat.mechanic === Mechanic.Interlude || (ev && ev.mechanic === Mechanic.Interlude)) {
      return { correct: true, score: 0, beatMax: 0 };
    }
    const opt = (ev && typeof ev.chosenIndex === 'number' && beat.options && beat.options[ev.chosenIndex]) || null;
    let correct, score, beatMax;
    if (opt) {
      correct = !!opt.isCorrect; score = opt.score;
      beatMax = beat.options.reduce((m, o) => Math.max(m, o.score), 0) || 1;
    } else if (beat.mechanic === Mechanic.Mantra || beat.mechanic === Mechanic.Mono) {
      const want = normStr(beat.prompt && beat.prompt.answer);
      const got = normStr(ev && ev.value);
      correct = want ? got === want : !!got; score = correct ? 1 : 0; beatMax = 1;
    } else { // check-in / free input: engagement, no "wrong" — timeout is the only miss
      correct = !(ev && ev.timedOut); score = correct ? 1 : 0; beatMax = 1;
    }
    return { correct, score, beatMax };
  }

  function updateVelocity(correct, ev) {
    const latency = (ev && ev.latencyMs) || 0;
    const fast = latency > 0 && latency < 1800;
    const slow = latency > 5000 || (ev && ev.timedOut);
    let dv = correct ? 0.18 : -0.22;
    if (fast) dv += 0.12;
    if (slow) dv -= 0.15;
    state.velocity = Math.max(-1, Math.min(1, state.velocity * 0.7 + dv));
  }

  function recordAnswer(beat, ev) {
    if (!beat) return;
    const isInterlude = beat.mechanic === Mechanic.Interlude || (ev && ev.mechanic === Mechanic.Interlude);
    const isTrick = !!(beat.prompt && beat.prompt.trick);
    const { correct, score, beatMax } = gradeBeat(beat, ev);
    const graded = !isInterlude && beat.band !== Band.Recovery;
    const tags = (beat.prompt && Array.isArray(beat.prompt.tags)) ? beat.prompt.tags : [];

    // Interludes never touch scoring/velocity/route/reward tallies — a valley, not a question.
    const rewardEvent = isInterlude ? null : resolveReward(beat.rewardPlan, ev, score);
    const rewardFired = graded && !!(rewardEvent && rewardEvent.fire);
    const rewardDecoupled = !!(rewardEvent && rewardEvent.decoupled);

    // WHICH OPTION WAS TAKEN (additive; see contracts.js AnswerRecord). `correct`
    // alone cannot tell an endorsement from a refusal — and `tags` are credited
    // whichever way the answer went — so the choice itself is recorded here for
    // the C# profiler. Free-input mechanics (mantra / check-in) and interludes
    // expose no option list: chosenIndex -1 / chosenLabel '' / optionCount 0 mark
    // them as un-scoreable rather than pretending index 0 was picked.
    const optList = Array.isArray(beat.options) ? beat.options : [];
    const chosenIndex = (ev && typeof ev.chosenIndex === 'number' && ev.chosenIndex >= 0 && ev.chosenIndex < optList.length)
      ? ev.chosenIndex : -1;
    const chosenOpt = chosenIndex >= 0 ? optList[chosenIndex] : null;

    result.trajectory.push({
      beatId: beat.id, band: beat.band, depth: beat.depth, mechanic: beat.mechanic,
      promptId: beat.prompt && beat.prompt.id, correct, score, latencyMs: (ev && ev.latencyMs) || 0,
      steered: !!(ev && ev.steered), rewardFired, rewardDecoupled, tags: tags.slice(),
      chosenIndex,
      chosenLabel: chosenOpt ? String(chosenOpt.label == null ? '' : chosenOpt.label) : '',
      optionCount: optList.length,
      promptHeat: (beat.prompt && typeof beat.prompt.heat === 'number') ? beat.prompt.heat : 0,
      steerIntensity: (beat.steerRoll && typeof beat.steerRoll.intensity === 'number') ? beat.steerRoll.intensity : 0,
      timeoutMs: (typeof beat.timeoutMs === 'number') ? beat.timeoutMs : 0,
      isTrick,
      isFreeChoice: !!(beat.prompt && beat.prompt.freeChoice),
    });

    if (graded) {
      result.totalScore += score;
      result.maxScore += beatMax;
      gradedAnswered += 1;
      if (correct) correctCount += 1;
      descentAnswered += 1;
      // TRICK QUESTIONS still SCORE — missing one is the whole joke and it should
      // show up in the tally — but an unanswerable question must not be punitive
      // beyond that: it never breaks the streak, never brakes descent velocity
      // (which would stretch the band as if the user had gone timid), and never
      // votes the route, because "trick" is noise, not a preference signal.
      if (!isTrick) {
        streakRun = (correct && !(ev && ev.timedOut)) ? streakRun + 1 : 0; // wrong/timeout resets
        updateVelocity(correct, ev);
        for (const t of tags) result.tagTallies[t] = (result.tagTallies[t] || 0) + 1;
        voteArchetypes(tags, (beat.prompt && beat.prompt.heat) || 0,
          endorsementOf(beat, ev, correct, score, beatMax));
      }
      // affirm a mantra (mantra OR mono path) when the user commits it correctly
      if (correct && beat.prompt && beat.prompt.affirmsMantra && beat.prompt.answer != null) {
        const verbatim = String(beat.prompt.answer);
        if (!result.affirmedMantras.includes(verbatim)) result.affirmedMantras.push(verbatim);
        affirmedIds.add(beat.prompt.id);
      }
    }
    updateRoute();
  }

  /** How strongly (and in which direction) an answer endorsed what it was asked.
   *  +1 = took the most-committing option / said the mantra verbatim.
   *  -1 = took the least-committing option, got it wrong, or let it time out.
   *  Free check-ins expose no option list, so engaging at all is a soft +0.5 —
   *  real, but never as loud as picking the endorsing answer off a real fork. */
  function endorsementOf(beat, ev, correct, score, beatMax) {
    if (ev && ev.timedOut) return -1;
    const optList = Array.isArray(beat.options) ? beat.options : [];
    if (optList.length > 1 && beatMax > 0) {
      let min = Infinity;
      for (const o of optList) min = Math.min(min, (o && typeof o.score === 'number') ? o.score : 0);
      const span = beatMax - min;
      if (span > 0) return Math.max(-1, Math.min(1, ((score - min) / span) * 2 - 1));
    }
    if (beat.mechanic === Mechanic.Mantra || beat.mechanic === Mechanic.Mono) return correct ? 1 : -1;
    if (!optList.length) return correct ? 0.5 : -1;
    return correct ? 1 : -1;
  }

  /** Signed, heat-weighted archetype vote. See the ROUTE / CLASSIFICATION MODEL
   *  block for why both the sign and the heat weight exist. */
  function voteArchetypes(tags, heat, endorsement) {
    if (!tags.length || !endorsement) return;
    const h = Math.max(0, Math.min(HEAT_VOTE.length - 1, Math.round(heat || 0)));
    const w = HEAT_VOTE[h] * endorsement;
    for (const a of archetypes) {
      const atags = Array.isArray(a.tags) ? a.tags : [];
      let hit = 0;
      for (const t of tags) if (atags.includes(t)) hit += 1;
      if (hit > 0) archetypeVotes.set(a.id, (archetypeVotes.get(a.id) || 0) + hit * w);
    }
  }

  /** Tier of an archetype: explicit `tier` if the bank declares one, else its
   *  position in the list (every shipped bank is authored tentative -> committed). */
  function tierOf(a, i) {
    return (a && typeof a.tier === 'number' && isFinite(a.tier)) ? a.tier : i;
  }
  const TOP_TIER = archetypes.reduce((m, a, i) => Math.max(m, tierOf(a, i)), 0);

  /** 0..1 — how much of themselves the run actually gave. The single number that
   *  keeps grade, susceptibility and classification from contradicting each other. */
  function commitmentScore() {
    const compliance = result.maxScore > 0 ? clamp01(result.totalScore / result.maxScore) : 0;
    const reach = clamp01(result.peakDepth || 0);
    const progress = clamp01(gradedAnswered / EXPECTED_DESCENT);
    return clamp01(COMMIT_W.compliance * compliance + COMMIT_W.reach * reach + COMMIT_W.progress * progress);
  }

  function updateRoute() {
    const commitment = commitmentScore();

    // commitment opens a tier WINDOW; the tag votes pick the flavour inside it.
    const ceilFrac = clamp01((commitment - TIER_CEIL.at0) / TIER_CEIL.span);
    const floorFrac = clamp01((commitment - TIER_FLOOR.at0) / TIER_FLOOR.span);
    const maxTier = Math.round(ceilFrac * TOP_TIER);
    const minTier = Math.min(maxTier, Math.floor(floorFrac * TOP_TIER));

    const all = archetypes.map((a, i) => ({ id: a.id, tier: tierOf(a, i), v: archetypeVotes.get(a.id) || 0 }));
    let cands = all.filter((c) => c.tier >= minTier && c.tier <= maxTier);
    if (!cands.length) {
      // pathological bank (tiers with holes) — fall back to the nearest tier.
      const target = (minTier + maxTier) / 2;
      let best = Infinity;
      for (const c of all) best = Math.min(best, Math.abs(c.tier - target));
      cands = all.filter((c) => Math.abs(c.tier - target) === best);
    }
    // highest vote wins; ties resolve to the LOWER tier (never over-claim on a coin flip).
    cands.sort((x, y) => (y.v - x.v) || (x.tier - y.tier));

    // Nothing in the window was endorsed at all -> name the least-claiming one in it.
    const anyPositive = cands.some((c) => c.v > 0);
    const top = anyPositive ? cands[0] : cands.reduce((lo, c) => (c.tier < lo.tier ? c : lo), cands[0]);
    const runner = cands.find((c) => c !== top && c.v > 0);

    // EXPRESSION — a real number now, not the old lerp(0.05, 0.45, progress) that
    // printed "45% expression" on every completed run regardless of what happened.
    // Dominance is how lopsided the vote is against an even split of the window.
    const pos = cands.filter((c) => c.v > 0);
    const totalPos = pos.reduce((s, c) => s + c.v, 0);
    const n = Math.max(1, cands.length);
    const frac = (totalPos > 0 && top.v > 0) ? (top.v / totalPos) : (1 / n);
    const even = 1 / n;
    // A one-archetype window cannot express dominance at all, so it reports neutral
    // rather than a spurious 100% (which used to read as MORE expressed than a run
    // that actually beat four rivals).
    const dominance = (n > 1) ? clamp01((frac - even) / (1 - even)) : 0.5;
    const expressed = clamp01(0.30 + 0.45 * commitment + 0.25 * dominance);

    // The reveal still CLIMBS across the run (the tube tint reads this as strength);
    // it just resolves to an honest figure instead of a fixed 45%.
    const progress = phase === 'descent' ? clamp01(descentAnswered / EXPECTED_DESCENT) : 1;
    const primaryShare = clamp01(lerp(0.05, expressed, progress));

    route.primary = niche;
    route.commitment = commitment;
    route.primaryArchetypeId = (top && top.id) || (archetypes[0] && archetypes[0].id) || niche;
    route.primaryShare = primaryShare;
    if (runner && top && top.v > 0) {
      // squared so a close-second (adjacent tiers share most of their tags) still
      // reads as a secondary rather than tying the headline classification.
      const ratio = clamp01(runner.v / top.v);
      route.secondaryArchetypeId = runner.id;
      route.secondaryShare = clamp01(primaryShare * ratio * ratio);
    } else {
      route.secondaryArchetypeId = undefined;
      route.secondaryShare = 0;
    }
  }

  /* --------------------------------------------------------------------------
   * AI ACCENT — best-effort, NEVER awaited in the loop (guarded, tolerant).
   * ------------------------------------------------------------------------ */
  function kickAccent(band, depth) {
    if (!ai || typeof ai.askAI !== 'function') return;
    try {
      const lastAnswers = result.trajectory.slice(-3).map((r) => ({ promptId: r.promptId, correct: r.correct, tags: r.tags }));
      const req = { niche, want: AiWant.Accent, route, depth, band, lastAnswers, tagTallies: result.tagTallies };
      Promise.resolve(ai.askAI(req)).then((resp) => {
        const b = resp && Array.isArray(resp.beats) && resp.beats[0];
        const line = b && (b.flavor || b.text);
        if (line) accentFlavor = String(line);
      }).catch(() => {});
    } catch (_e) { /* accent is optional; never break the loop */ }
  }

  /* --------------------------------------------------------------------------
   * BEAT META + INTERVIEWER ASIDES (theme voice, seeded rolls, no-repeat memory)
   * ------------------------------------------------------------------------ */
  /** qTotal: best current estimate of GRADED questions across the descent —
   *  completed bands count what was actually served; the current + future bands
   *  count plannedBeats at the current velocity. Stable ~50 under steady play. */
  function estimateQTotal() {
    let total = 0;
    for (let i = 0; i < DESCENT_BANDS.length; i++) {
      const b = DESCENT_BANDS[i];
      if (i < descentIdx) total += (servedByBand[b] != null ? servedByBand[b] : BASE_BEATS[b]);
      else if (i === descentIdx) total += Math.max(plannedBeats(b), beatsInBand);
      else total += plannedBeats(b);
    }
    return total;
  }

  /** Pick a theme interviewer line for a band, avoiding the last two used. */
  function pickInterviewerLine(band) {
    const pool = (theme.interviewer && theme.interviewer[band]) || [];
    if (!pool.length) return undefined;
    const mem = lineMem[band] || (lineMem[band] = []);
    let idxs = pool.map((_, i) => i).filter((i) => !mem.includes(i));
    if (!idxs.length) idxs = pool.map((_, i) => i).filter((i) => i !== mem[mem.length - 1]);
    if (!idxs.length) idxs = pool.map((_, i) => i);
    const idx = idxs[Math.floor(rng() * idxs.length)];
    mem.push(idx); if (mem.length > 2) mem.shift();
    return pool[idx];
  }

  /** Roll an interviewer aside (~every 2-3 beats; never twice in a row). */
  function rollInterviewerLine(band) {
    const speak = !lastBeatHadLine && rng() < INTERVIEWER_ROLL;
    if (!speak) { lastBeatHadLine = false; return undefined; }
    const line = pickInterviewerLine(band);
    lastBeatHadLine = !!line;
    return line;
  }

  /** Assemble BeatMeta for the beat being emitted (also flips bandNew memory). */
  function makeMeta(band, bandBeat, bandPlanned, interviewerLine) {
    const meta = {
      qIndex,
      qTotal: estimateQTotal(),
      bandBeat,
      bandPlanned,
      bandNew: lastEmittedBand !== band,
      streak: streakRun,
    };
    if (interviewerLine) meta.interviewerLine = interviewerLine;
    lastEmittedBand = band;
    return meta;
  }

  /* --------------------------------------------------------------------------
   * BEAT BUILDERS
   * ------------------------------------------------------------------------ */
  function buildDescentBeat(band) {
    const planned = plannedBeats(band);
    const depth = descentDepth(band, beatsInBand, planned);
    state.depth = depth;
    state.band = band;

    const plain = isPlainSlot(band);
    const src = selectPrompt(band, plain);
    lastBeatWasTrick = !!src.trick;
    let mechanic = pickMechanic(src, band);
    // SPIRAL HOLD: a BubblePop beat mounts an unconditional fullscreen loom
    // backdrop (beats.js), and a loom is woven from the harvested colours — so
    // while Calibration is still collecting them, that mechanic is withheld and
    // the beat plays as plain MC4. Calibration's heat window admits heat-1
    // bubble prompts, so this is a live path, not a theoretical one.
    const spiralHold = band === Band.Calibration && harvestWantsMore();
    if (spiralHold && mechanic === Mechanic.BubblePop) mechanic = Mechanic.MC4;
    // BUBBLE GUARANTEE: every descent band serves >=1 BubblePop. If the band hits
    // its last 2 planned beats dry, force it on the next option-compatible prompt.
    if (!bubbleServedInBand && !spiralHold && beatsInBand >= planned - 2 && mechanic !== Mechanic.BubblePop
        && (OPTION_MECHANICS.has(mechanic) || (Array.isArray(src.options) && src.options.length))) {
      mechanic = Mechanic.BubblePop;
    }
    if (mechanic === Mechanic.BubblePop) bubbleServedInBand = true;
    const options = buildOptions(mechanic, src);
    // A plain beat is served UNSTEERED. Note this also clears lastPrimarySteer,
    // so the steer that follows it is free to repeat the one before — which is
    // correct: with an unsteered beat between them they no longer read as a pair.
    const steerRoll = rollSteer(band, depth, !plain && options.length > 0);
    const rewardPlan = planFor(band, depth, src) || fallbackPlan(band, depth, src);

    // TIMED PRESSURE: ~30% of MC4/YesNo beats in Deepening (and sub-0.9 Climax)
    // get a shrinking-ring timeout, shorter as depth rises; never back-to-back.
    let timeoutMs = 0;
    const timedEligible = !plain && TIMED_MECHANICS.has(mechanic) && !lastBeatTimed
      && (band === Band.Deepening || (band === Band.Climax && depth < 0.9));
    if (timedEligible && rng() < TIMED_ROLL) {
      const target = lerp(TIMED_MS_MAX, TIMED_MS_MIN, depth) + (rng() - 0.5) * 1000;
      timeoutMs = Math.round(Math.max(TIMED_MS_MIN, Math.min(TIMED_MS_MAX, target)));
    }
    lastBeatTimed = timeoutMs > 0;

    kickAccent(band, depth); // fire-and-forget; may color a later beat
    const prompt = Object.assign({}, src);
    if (accentFlavor) prompt.flavor = accentFlavor;

    beatSeq += 1; totalBeatsBuilt += 1; beatsInBand += 1; qIndex += 1;
    const meta = makeMeta(band, beatsInBand, planned, rollInterviewerLine(band));
    trackPeak(depth, band);
    return { id: `b${beatSeq}-${band}`, band, depth, mechanic, prompt, options, steerRoll, rewardPlan, timeoutMs, meta };
  }

  function buildInterludeBeat(band) {
    const planned = plannedBeats(band);
    const depth = descentDepth(band, beatsInBand, planned);
    state.depth = depth;
    state.band = band;

    const win = HEAT_WINDOW[band] || [0, 5];
    const kind = band === Band.Climax ? 'breathe' : 'watch';
    const durationMs = Math.round(lerp(INTERLUDE_MS_MIN, INTERLUDE_MS_MAX, rng()));
    const prompt = {
      id: `interlude-${band}`,
      text: kind === 'watch' ? 'just watch.' : 'breathe with it.',
      answer: true, heat: Math.round((win[0] + win[1]) / 2), tags: [], flavors: [], weight: 1,
      mechanicHints: [Mechanic.Interlude],
      interludeKind: kind, durationMs,
    };
    const rewardPlan = planFor(band, depth, prompt) || fallbackPlan(band, depth, prompt);

    // NOT beatsInBand, NOT qIndex — a valley, not a question (band never shrinks).
    beatSeq += 1; totalBeatsBuilt += 1;
    lastBeatTimed = false;
    const meta = makeMeta(band, beatsInBand, planned, rollInterviewerLine(band));
    trackPeak(depth, band);
    return { id: `b${beatSeq}-${band}-interlude`, band, depth, mechanic: Mechanic.Interlude, prompt, options: [], steerRoll: { primary: Steer.None, secondary: [], intensity: 0 }, rewardPlan, timeoutMs: 0, meta };
  }

  function buildRecoveryBeat() {
    const step = recoveryIdx; // 0-based
    const depth = clamp01(recoveryStart * (1 - (step + 1) / RECOVERY_BEATS)); // monotonic 1->0, ends 0
    state.depth = depth;
    state.band = Band.Recovery;
    state.velocity = Math.max(0, state.velocity * 0.5); // settle

    const line = RECOVERY_LINES[Math.min(step, RECOVERY_LINES.length - 1)];
    const prompt = { id: `recovery-${step}`, text: line, answer: 1, heat: 0, tags: [], flavors: [], weight: 1, mechanicHints: [Mechanic.CheckIn] };
    const rewardPlan = planFor(Band.Recovery, depth, prompt) || fallbackPlan(Band.Recovery, depth, prompt);

    // Recovery beats ALWAYS carry an interviewer aside (the debrief voice).
    const meta = makeMeta(Band.Recovery, step + 1, RECOVERY_BEATS, pickInterviewerLine(Band.Recovery));
    lastBeatHadLine = true;

    recoveryIdx += 1; beatSeq += 1; totalBeatsBuilt += 1;
    trackPeak(depth, Band.Recovery);
    return { id: `b${beatSeq}-recovery`, band: Band.Recovery, depth, mechanic: Mechanic.CheckIn, prompt, options: [], steerRoll: { primary: Steer.None, secondary: [], intensity: 0 }, rewardPlan, timeoutMs: 0, meta };
  }

  function trackPeak(depth, band) {
    if (depth > result.peakDepth) result.peakDepth = depth;
    if (band !== Band.Recovery && bandIndex(band) > bandIndex(result.deepestBand)) result.deepestBand = band;
  }

  /* --------------------------------------------------------------------------
   * FINALIZE — a fully-populated QuizRunResult.
   * ------------------------------------------------------------------------ */
  function finalize() {
    phase = 'done';
    updateRoute();                       // final progress -> shares reach ~0.45
    result.route = { primary: route.primary, primaryArchetypeId: route.primaryArchetypeId, secondaryArchetypeId: route.secondaryArchetypeId, primaryShare: route.primaryShare, secondaryShare: route.secondaryShare, commitment: route.commitment };
    result.niche = niche;
    result.product = PRODUCT_NAME;
    result.rewardProfile = computeRewardProfile();
    // SUSCEPTIBILITY vs PEAK DEPTH. peakDepth is how deep the SESSION went, and it
    // climbs on a clock whether or not the subject came along — a run that answered
    // nothing at all still peaked at 0.96, which the outro then printed as "96%
    // susceptibility" beside a D. Susceptibility is that depth discounted by how much
    // the subject actually gave to it, so the three headline numbers agree. peakDepth
    // itself is untouched (the C# session generator maps it to difficulty).
    result.susceptibility = clamp01(clamp01(result.peakDepth || 0)
      * (0.35 + 0.65 * (result.maxScore > 0 ? clamp01(result.totalScore / result.maxScore) : 0)));
    result.endless = endless;
    result.endedAtMs = nowMs() - t0;
    return result;
  }

  function computeRewardProfile() {
    const graded = result.trajectory.filter((r) => r.band !== Band.Recovery);
    const honest = graded.filter((r) => !r.rewardDecoupled);
    const decoup = graded.filter((r) => r.rewardDecoupled);
    if (decoup.length < 3 || honest.length < 1) return { chasedReward: false, chaseMagnitude: 0 };
    const rate = (arr) => arr.reduce((s, r) => s + (r.correct ? 1 : 0), 0) / arr.length;
    const rHonest = rate(honest);
    const rDecoupled = rate(decoup);
    const drop = rHonest - rDecoupled; // >0 => drifted off correct once reward decoupled from correctness
    return { chasedReward: drop > 0.12, chaseMagnitude: clamp01(drop * 1.5) };
  }

  /* --------------------------------------------------------------------------
   * DRIVER — the canonical next() step machine.
   * ------------------------------------------------------------------------ */
  function stepDescent() {
    // advance the band if the current one is full (never below MIN_BEATS -> all bands sequence)
    let band = DESCENT_BANDS[descentIdx];
    if (beatsInBand >= plannedBeats(band)) {
      servedByBand[band] = beatsInBand; // actual graded beats -> qTotal estimate
      descentIdx += 1;
      beatsInBand = 0;
      bubbleServedInBand = false;
      if (descentIdx >= DESCENT_BANDS.length) {
        if (endless && !aborting) {
          lapCount += 1;
          descentIdx = DESCENT_BANDS.indexOf(Band.Deepening); // loop the deep bands, keep deepening
          interludesInBand[Band.Deepening] = 0;               // each lap gets its valleys again
          interludesInBand[Band.Climax] = 0;
        } else {
          return enterRecovery();
        }
      }
      band = DESCENT_BANDS[descentIdx];
    }
    // INTERLUDE VALLEYS: synthetic non-graded pauses in Deepening + Climax, spaced
    // ~one per INTERLUDE_EVERY graded beats and evenly distributed across the band
    // (so a 14-16 beat band never runs a long dry stretch without a valley). Never
    // lands on the band's final beat. Velocity-shifted planned counts stay covered
    // because positions are computed against the live plannedBeats.
    if (band === Band.Deepening || band === Band.Climax) {
      const planned = plannedBeats(band);
      const nInt = Math.max(1, Math.round(planned / INTERLUDE_EVERY));
      const served = interludesInBand[band] || 0;
      if (served < nInt) {
        const pos = Math.round((served + 1) * planned / (nInt + 1)); // evenly spaced, never at the end
        if (beatsInBand >= pos && beatsInBand < planned) {
          interludesInBand[band] = served + 1;
          return { done: false, beat: buildInterludeBeat(band) };
        }
      }
    }
    return { done: false, beat: buildDescentBeat(band) };
  }

  function enterRecovery() {
    phase = 'recovery';
    recoveryIdx = 0;
    recoveryStart = state.depth; // walk down from wherever we are (near peak, or wherever abort caught us)
    return stepRecovery();
  }

  function stepRecovery() {
    if (recoveryIdx >= RECOVERY_BEATS) return { done: true, result: finalize() };
    return { done: false, beat: buildRecoveryBeat() };
  }

  return {
    get state() { return state; },
    get route() { return route; },

    async next(prevAnswer) {
      if (lastBeat && prevAnswer) recordAnswer(lastBeat, prevAnswer);
      lastBeat = null;

      if (phase === 'done') return { done: true, result: finalize() };

      // safety: a never-aborted endless run must still surface eventually.
      if (totalBeatsBuilt >= HARD_BEAT_CAP && phase !== 'recovery') aborting = true;

      // abort mid-descent -> jump to the (non-skippable) Recovery walk-down.
      if (aborting && phase === 'descent') {
        const step = enterRecovery();
        if (!step.done) lastBeat = step.beat;
        return step;
      }

      let step;
      if (phase === 'recovery') step = stepRecovery();
      else step = stepDescent();

      if (!step.done) lastBeat = step.beat;
      return step;
    },

    abort() { aborting = true; },
  };
}

export default createEngine;
