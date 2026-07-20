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
 *   - DEPTH-VELOCITY: fast + high-score answers raise velocity (shorten a band);
 *     timid / slow / low-score answers lower it (linger).
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
  clamp01, smoothstep, lerp, bandIndex, hash01,
  AiWant,
} from './contracts.js';

/* ----------------------------------------------------------------------------
 * TUNING CONSTANTS (pacing over ~25-30 beats; BUILD_PLAN.md §5 Phase 3).
 * -------------------------------------------------------------------------- */
const DESCENT_BANDS = Object.freeze([Band.Calibration, Band.Establishing, Band.Deepening, Band.Climax]);
const BASE_BEATS = Object.freeze({
  [Band.Calibration]:  4,
  [Band.Establishing]: 5,
  [Band.Deepening]:    6,
  [Band.Climax]:       6,
});
const MIN_BEATS = 3;               // a band can be shortened to this, never skipped (all 5 bands sequence)
const MAX_BEATS = 9;               // a band can be stretched to this by timid play
const RECOVERY_BEATS = 4;          // fixed, non-skippable walk-down (invariant #3)
const CLIMAX_PEAK = 1.0;           // depth ceiling the Climax band ramps toward
const EXPECTED_DESCENT = 21;       // ~sum(BASE_BEATS); drives progressive route-share climb
const HARD_BEAT_CAP = 800;         // safety net so a never-aborted endless run still surfaces

/** heat window (0..5) the sequencer prefers per band; camouflage low, hot high. */
const HEAT_WINDOW = Object.freeze({
  [Band.Calibration]:  [0, 1],
  [Band.Establishing]: [1, 2],
  [Band.Deepening]:    [2, 4],
  [Band.Climax]:       [3, 5],
});

/** steer catalogs per band (Calibration/Recovery steer nothing — inv of the fiction). */
const STEER_POOL = Object.freeze({
  [Band.Establishing]: [Steer.Magnet, Steer.SizeSkew, Steer.OpacitySkew, Steer.LateBloom, Steer.Defocus, Steer.AssistClick, Steer.DriftResolve],
  [Band.Deepening]:    [Steer.Magnet, Steer.Flee, Steer.Exile, Steer.Crowd, Steer.SizeSkew, Steer.OpacitySkew, Steer.OccludeGif, Steer.Defocus, Steer.ShrinkHit, Steer.DragReveal, Steer.AssistClick, Steer.DriftResolve, Steer.Decay],
  [Band.Climax]:       [Steer.Flee, Steer.Exile, Steer.Crowd, Steer.OccludeGif, Steer.ShrinkHit, Steer.DragReveal, Steer.HoldRefuse, Steer.NestedNag, Steer.OverflowHit, Steer.Tunnel, Steer.Decay, Steer.Magnet],
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
  const prompts = Array.isArray(activeBank.prompts) ? activeBank.prompts : [];
  const archetypes = (Array.isArray(activeBank.archetypes) && activeBank.archetypes.length)
    ? activeBank.archetypes : placeholderBank(niche).archetypes;
  const ladders = Array.isArray(activeBank.ladders) ? activeBank.ladders : [];

  const endless = !!cfg.endless;
  const steerValve = clamp01(cfg.steerValve == null ? 1 : cfg.steerValve);
  const seedInt = Math.floor(hash01(niche + '|' + (cfg.seed == null ? 'run' : cfg.seed)) * 0xFFFFFFFF);
  const rng = makeRng(seedInt);

  const byId = new Map(prompts.map((p) => [p.id, p]));
  const ladderPromptIds = new Set();
  for (const l of ladders) for (const r of (l.rungs || [])) ladderPromptIds.add(r.promptId);

  // ---- run state ----------------------------------------------------------
  const state = { depth: 0, velocity: 0, band: Band.Calibration };
  const route = { primary: niche, primaryArchetypeId: '', secondaryArchetypeId: undefined, primaryShare: 0, secondaryShare: 0 };
  const result = emptyResult(niche);
  result.endless = endless;

  const usedIds = new Set();              // prompts already served this run (soft no-repeat)
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
      if (archetypes.some((a) => a.id === id)) archetypeVotes.set(id, 0.5); // faint head-start, easily overtaken
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
  function leadingArchetypeId() {
    let best = null; let bestV = -1;
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

  function eligible(band, strict) {
    const win = HEAT_WINDOW[band] || [0, 5];
    const out = [];
    for (const p of prompts) {
      if (usedIds.has(p.id)) continue;
      if (ladderPromptIds.has(p.id)) continue;  // ladder-owned prompts arrive only via the ladder
      const heat = typeof p.heat === 'number' ? p.heat : 0;
      if (strict) { if (heat < win[0] - 0.001 || heat > win[1] + 0.001) continue; }
      else { if (heat > win[1] + 1.5) continue; } // widened: allow softer prompts, keep a rough hot ceiling
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

  function selectPrompt(band) {
    // Belief-ladder gets priority in the deep bands (it IS the reactive spine).
    if ((band === Band.Establishing || band === Band.Deepening || band === Band.Climax) && rng() < 0.55) {
      const rung = nextLadderPrompt();
      if (rung) { servedRungs.add(rung.id); usedIds.add(rung.id); return rung; }
    }
    let pool = eligible(band, true);
    if (!pool.length) pool = eligible(band, false);      // widen the heat window
    if (!pool.length) { usedIds.clear(); pool = eligible(band, false); } // allow reuse
    if (!pool.length) pool = prompts.filter((p) => !ladderPromptIds.has(p.id));
    if (!pool.length) pool = prompts.slice();
    if (!pool.length) return syntheticPrompt(band);
    const p = weightedPick(pool);
    usedIds.add(p.id);
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
    const base = BASE_BEATS[band] || 5;
    const scaled = Math.round(base * (1 - 0.35 * state.velocity)); // high velocity -> fewer beats
    return Math.max(MIN_BEATS, Math.min(MAX_BEATS, scaled));
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
    return clamp01(base + state.velocity * 0.04); // small reactive nudge
  }

  /* --------------------------------------------------------------------------
   * ANSWER RECORDING + VELOCITY + ROUTE
   * ------------------------------------------------------------------------ */
  function gradeBeat(beat, ev) {
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
    const { correct, score, beatMax } = gradeBeat(beat, ev);
    const graded = beat.band !== Band.Recovery;
    const tags = (beat.prompt && Array.isArray(beat.prompt.tags)) ? beat.prompt.tags : [];

    const rewardEvent = resolveReward(beat.rewardPlan, ev, score);
    const rewardFired = graded && !!(rewardEvent && rewardEvent.fire);
    const rewardDecoupled = !!(rewardEvent && rewardEvent.decoupled);

    result.trajectory.push({
      beatId: beat.id, band: beat.band, depth: beat.depth, mechanic: beat.mechanic,
      promptId: beat.prompt && beat.prompt.id, correct, score, latencyMs: (ev && ev.latencyMs) || 0,
      steered: !!(ev && ev.steered), rewardFired, rewardDecoupled, tags: tags.slice(),
    });

    if (graded) {
      result.totalScore += score;
      result.maxScore += beatMax;
      gradedAnswered += 1;
      if (correct) correctCount += 1;
      descentAnswered += 1;
      updateVelocity(correct, ev);
      for (const t of tags) result.tagTallies[t] = (result.tagTallies[t] || 0) + 1;
      voteArchetypes(tags, correct);
      // affirm a mantra (mantra OR mono path) when the user commits it correctly
      if (correct && beat.prompt && beat.prompt.affirmsMantra && beat.prompt.answer != null) {
        const verbatim = String(beat.prompt.answer);
        if (!result.affirmedMantras.includes(verbatim)) result.affirmedMantras.push(verbatim);
        affirmedIds.add(beat.prompt.id);
      }
    }
    updateRoute();
  }

  function voteArchetypes(tags, correct) {
    if (!tags.length) return;
    const w = correct ? 1.5 : 1; // engaging at all reveals interest; a correct commit reveals more
    for (const a of archetypes) {
      const atags = Array.isArray(a.tags) ? a.tags : [];
      let hit = 0;
      for (const t of tags) if (atags.includes(t)) hit += 1;
      if (hit > 0) archetypeVotes.set(a.id, (archetypeVotes.get(a.id) || 0) + hit * w);
    }
  }

  function updateRoute() {
    const sorted = archetypes
      .map((a) => ({ id: a.id, v: archetypeVotes.get(a.id) || 0 }))
      .sort((x, y) => y.v - x.v);
    const progress = phase === 'descent' ? clamp01(descentAnswered / EXPECTED_DESCENT) : 1;
    const primaryShare = lerp(0.05, 0.45, progress);
    route.primary = niche;
    const top = sorted[0];
    const runner = sorted[1];
    route.primaryArchetypeId = (top && top.v > 0) ? top.id : (archetypes[0] && archetypes[0].id) || niche;
    route.primaryShare = primaryShare;
    if (runner && runner.v > 0 && top && top.v > 0) {
      route.secondaryArchetypeId = runner.id;
      route.secondaryShare = clamp01(primaryShare * (runner.v / top.v));
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
   * BEAT BUILDERS
   * ------------------------------------------------------------------------ */
  function buildDescentBeat(band) {
    const planned = plannedBeats(band);
    const depth = descentDepth(band, beatsInBand, planned);
    state.depth = depth;
    state.band = band;

    const src = selectPrompt(band);
    const mechanic = pickMechanic(src, band);
    const options = buildOptions(mechanic, src);
    const steerRoll = rollSteer(band, depth, options.length > 0);
    const rewardPlan = planFor(band, depth, src) || fallbackPlan(band, depth, src);

    kickAccent(band, depth); // fire-and-forget; may color a later beat
    const prompt = Object.assign({}, src);
    if (accentFlavor) prompt.flavor = accentFlavor;

    beatSeq += 1; totalBeatsBuilt += 1; beatsInBand += 1;
    trackPeak(depth, band);
    return { id: `b${beatSeq}-${band}`, band, depth, mechanic, prompt, options, steerRoll, rewardPlan, timeoutMs: 0 };
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

    recoveryIdx += 1; beatSeq += 1; totalBeatsBuilt += 1;
    trackPeak(depth, Band.Recovery);
    return { id: `b${beatSeq}-recovery`, band: Band.Recovery, depth, mechanic: Mechanic.CheckIn, prompt, options: [], steerRoll: { primary: Steer.None, secondary: [], intensity: 0 }, rewardPlan, timeoutMs: 0 };
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
    result.route = { primary: route.primary, primaryArchetypeId: route.primaryArchetypeId, secondaryArchetypeId: route.secondaryArchetypeId, primaryShare: route.primaryShare, secondaryShare: route.secondaryShare };
    result.niche = niche;
    result.product = PRODUCT_NAME;
    result.rewardProfile = computeRewardProfile();
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
      descentIdx += 1;
      beatsInBand = 0;
      if (descentIdx >= DESCENT_BANDS.length) {
        if (endless && !aborting) {
          lapCount += 1;
          descentIdx = DESCENT_BANDS.indexOf(Band.Deepening); // loop the deep bands, keep deepening
        } else {
          return enterRecovery();
        }
      }
      band = DESCENT_BANDS[descentIdx];
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
