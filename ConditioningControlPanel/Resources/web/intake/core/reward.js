/* ============================================================================
 * reward.js — Agent B — the reward SCHEDULE + depth->channel wrapper.
 *
 * The reward module decouples "reward" from "correctness" as the descent
 * deepens. It owns the *scheduling* (mode per band, variable-ratio slot
 * machine, running-score scaling); contracts.js owns the *curve* (the
 * depth->channel map). This file NEVER re-derives the depth curve and NEVER
 * hardcodes an absolute effect strength — all magnitudes are RAW 0..1 and are
 * clamped to the user's caps at the effect layer (invariant #2), except
 * channelsFor(), which is the sanctioned place that applies clampToCaps.
 *
 * Factory (contracts.js "REWARD (Agent B)"):
 *   createReward({ config }) -> {
 *     planFor(band, depth, prompt) -> RewardPlan,
 *     resolve(plan, answerEvent, scoreDelta) -> RewardEvent,
 *     channelsFor(depth) -> Channels,       // depthToChannels + clampToCaps
 *   }
 *
 * REWARD MODE BY BAND (BUILD_PLAN.md §3 Agent B):
 *   Calibration  -> Honest        reward iff correct (baseChance 1.0)
 *   Establishing -> SpicierPick    the hotter pick pays more, regardless of correctness
 *   Deepening    -> ScaleWithScore magnitude scales with RUNNING score, not this answer
 *   Climax       -> VariableRatio  intermittent slot-machine schedule (deterministic per seed)
 *   Recovery     -> "none"         Honest with baseChance 0 -> never fires (invariant #3: no coercion)
 *
 * DECOUPLING: RewardEvent.decoupled is true whenever mode !== Honest, so stats.js
 * can measure how strongly the user chased a reward that was NOT tied to correctness.
 *
 * DETERMINISM: the VariableRatio schedule is a seeded stream. Pass config.seed
 * (or config.rewardSeed) for reproducible fires (tests/harness). With no seed a
 * per-instance random seed is chosen so production runs differ. hash01 (from
 * contracts.js) drives the stream, so a fixed seed yields an identical fire
 * sequence for a fixed sequence of VariableRatio resolves.
 * ==========================================================================*/

import {
  Band,
  Mechanic,
  RewardMode,
  RewardKind,
  depthToChannels,
  clampToCaps,
  DEFAULT_CAPS,
  clamp01,
  smoothstep,
  hash01,
} from './contracts.js';

/* ----------------------------------------------------------------------------
 * baseChance per mode. Honest/SpicierPick/ScaleWithScore fire on every eligible
 * beat (baseChance 1.0); their *magnitude*, not their *chance*, carries the
 * decoupling. VariableRatio is the only intermittent schedule: an intensity-
 * ramped chance in (0,1) so the payout feels like a slot machine.
 * -------------------------------------------------------------------------- */
/* ----------------------------------------------------------------------------
 * slot-machine tuning (VariableRatio only) + streak juice.
 *   JACKPOT: a fired VR roll whose rJack lands in the top band pays a boosted
 *   intensity (base * 1.5, clamped) and flags RewardEvent.jackpot for the full
 *   ceremony. NEAR-MISS: an unfired roll within NEAR_MISS_WINDOW of the chance
 *   threshold flags RewardEvent.nearMiss (fire stays false, intensity 0) so
 *   effects can tease. STREAK: +3%/consecutive-correct, capped at 8 — juice,
 *   not runaway. All rolls stay on the seeded stream (determinism preserved).
 * -------------------------------------------------------------------------- */
const JACKPOT_ROLL = 0.85;       // rJack >= this on a VR hit -> jackpot
const JACKPOT_BOOST = 1.5;       // jackpot intensity = clamp01(base * this)
const NEAR_MISS_WINDOW = 0.08;   // rFire within this above chance -> near-miss
const STREAK_CAP = 8;            // streak beats counted at most
const STREAK_STEP = 0.03;        // intensity multiplier step per streak beat

/* GifBurst: an in-browser CCP-flash reward (owner-directed). It joins the kind
 * roll in pickKind at GIFBURST_CHANCE — a weight comparable to how often the
 * heavy Flash / gif-Drop payloads land (Flash is the deep-beat default, Drop the
 * hottest; ~18% keeps GifBurst a frequent-but-not-dominant reward). The kind
 * string mirrors a recommended contracts.js `RewardKind.GifBurst: 'gifburst'`
 * (see FINAL REPORT contract-gap note); until that enum lands the literal is
 * duplicated here + in effects.js (same pattern as the GARNISH_CUE_EVENT
 * literal). effects.js renders it; audio/stats ignore an unknown kind. */
const GIFBURST_KIND = 'gifburst';
const GIFBURST_CHANCE = 0.18;

function baseChanceFor(mode, band, depth) {
  if (band === Band.Recovery) return 0;             // recovery never rewards (inv #3)
  switch (mode) {
    case RewardMode.Honest:         return 1.0;
    case RewardMode.SpicierPick:    return 1.0;     // always pays; the hotter pick pays MORE
    case RewardMode.ScaleWithScore: return 1.0;     // always pays; magnitude tracks the run
    case RewardMode.VariableRatio:  return clamp01(0.30 + 0.30 * smoothstep(depth)); // 0.30..0.60
    default:                        return 1.0;
  }
}

/** Mode assigned to each band (the descent's decoupling schedule). */
function modeForBand(band) {
  switch (band) {
    case Band.Calibration:  return RewardMode.Honest;
    case Band.Establishing: return RewardMode.SpicierPick;
    case Band.Deepening:    return RewardMode.ScaleWithScore;
    case Band.Climax:       return RewardMode.VariableRatio;
    case Band.Recovery:     return RewardMode.Honest;  // "none" via baseChance 0
    default:                return RewardMode.Honest;
  }
}

/* ----------------------------------------------------------------------------
 * baseIntensity from the depth curve. This is derived from the SINGLE SOURCE OF
 * TRUTH (depthToChannels) — NOT a re-derived curve. It is the "reward-salience"
 * blend: the raw channels through which a payoff is actually felt, weighted so
 * they sum to 1.0. Raw 0..1 (caps applied later at the effect layer, inv #2).
 *   0.45 flashOpacity  (the visual pop of a reward)
 *   0.30 bubbleRate    (the bubble/gift payload pressure)
 *   0.25 binauralDepth (the felt audio deepening)
 * -------------------------------------------------------------------------- */
function rewardIntensity01(depth) {
  const c = depthToChannels(depth); // raw curve, single source of truth
  return clamp01(0.45 * c.flashOpacity + 0.30 * c.bubbleRate + 0.25 * c.binauralDepth);
}

/* ----------------------------------------------------------------------------
 * kind bias by mechanic + heat + depth. BubblePop beats reward through the
 * bubble (the centerpiece: the effect bubble IS the reward). Hotter prompts pay
 * in the heavier payloads (drop/praise); deeper beats favour flash; the honest
 * warm-up favours a gentle chime.
 * -------------------------------------------------------------------------- */
function pickKind(band, depth, prompt) {
  if (band === Band.Recovery) return RewardKind.None;
  const hints = (prompt && Array.isArray(prompt.mechanicHints)) ? prompt.mechanicHints : [];
  const heat = (prompt && typeof prompt.heat === 'number') ? prompt.heat : 0;
  if (hints.includes(Mechanic.BubblePop)) return RewardKind.Bubble;
  // GifBurst joins the roll as a random in-browser flash reward (BubblePop still
  // wins — its bubble IS the reward). Opacity ramps by band inside effects.js.
  if (Math.random() < GIFBURST_CHANCE) return GIFBURST_KIND;
  if (heat >= 4) return RewardKind.Drop;    // hottest -> gif burst / subliminal drop
  if (heat >= 3) return RewardKind.Praise;  // voiced/subtitled affirmation
  if (depth >= 0.6) return RewardKind.Flash;
  if (depth >= 0.3) return RewardKind.Bubble;
  return RewardKind.Chime;                  // calm, honest warm-up
}

/** Normalise a 0..5 prompt heat to 0..1 (used as the SpicierPick fallback). */
function promptHeat01(prompt) {
  const heat = (prompt && typeof prompt.heat === 'number') ? prompt.heat : 0;
  return clamp01(heat / 5);
}

/**
 * The heat of the pick the user actually committed, 0..1.
 * Ideal path: render (beats.js) reports it on the answer as `answerEvent.pickHeat`
 * (the chosen option's heat, normalised 0..1) — this is a forward-compatible
 * OPTIONAL extension of AnswerEvent (see FINAL REPORT contract-gap note; the
 * canonical AnswerEvent shape is unchanged and this field is simply ignored by
 * everyone else). Fallbacks: the prompt's heat stashed on the plan, else 0.5.
 */
function pickHeat01(plan, answerEvent) {
  if (answerEvent && typeof answerEvent.pickHeat === 'number') return clamp01(answerEvent.pickHeat);
  if (plan && typeof plan._heat === 'number') return clamp01(plan._heat);
  return 0.5;
}

export function createReward({ config } = {}) {
  const cfg = config || {};
  const caps = cfg.caps || DEFAULT_CAPS;

  // Seed for the VariableRatio stream. Explicit -> reproducible; absent -> random.
  const seed = (cfg.seed != null) ? String(cfg.seed)
             : (cfg.rewardSeed != null) ? String(cfg.rewardSeed)
             : ('vr-' + Math.floor(Math.random() * 1e9));

  // --- run-scoped state (tracked across resolve calls) ---------------------
  let runScore = 0;   // sum of positive scoreDelta across the whole run
  let runPeak  = 0;   // best single scoreDelta seen (proxy for per-beat max)
  let runBeats = 0;   // number of scored beats
  let vrCount  = 0;   // VariableRatio resolves so far (drives the seeded stream)

  /** Running 0..1 fraction of best-achievable score (ScaleWithScore magnitude). */
  function runningScoreRate() {
    if (runBeats <= 0 || runPeak <= 0) return 0;
    return clamp01(runScore / (runPeak * runBeats));
  }

  /**
   * planFor(band, depth, prompt) -> RewardPlan { mode, baseChance, baseIntensity, kind }
   * Engine attaches this to the BeatSpec; render resolves it on commit.
   * (A private `_heat` is stashed for the SpicierPick fallback — a forward-only
   *  additive field, not a redefinition of RewardPlan; see contract-gap note.)
   */
  function planFor(band, depth, prompt) {
    const d = clamp01(depth);
    const mode = modeForBand(band);
    const isRecovery = band === Band.Recovery;
    const plan = {
      mode,
      baseChance: baseChanceFor(mode, band, d),
      baseIntensity: isRecovery ? 0 : rewardIntensity01(d),
      kind: pickKind(band, d, prompt),
    };
    plan._heat = promptHeat01(prompt); // additive, ignored by non-reward consumers
    return plan;
  }

  /**
   * resolve(plan, answerEvent, scoreDelta) -> RewardEvent
   *   { fire, intensity, kind, decoupled, jackpot?, nearMiss?, streak? }
   * intensity is RAW 0..1 (effect layer clamps to caps, invariant #2).
   * jackpot/nearMiss ride the VariableRatio path only; streak echoes
   * answerEvent.streak (when present) after applying the small multiplier.
   */
  function resolve(plan, answerEvent, scoreDelta) {
    const p = plan || { mode: RewardMode.Honest, baseChance: 0, baseIntensity: 0, kind: RewardKind.None };
    const sd = Number.isFinite(scoreDelta) ? scoreDelta : 0;

    // Track the running score for ScaleWithScore (every beat, any mode).
    runBeats += 1;
    if (sd > 0) runScore += sd;
    if (sd > runPeak) runPeak = sd;

    const mode = p.mode;
    const base = clamp01(p.baseIntensity == null ? 0 : p.baseIntensity);
    const chance = clamp01(p.baseChance == null ? 0 : p.baseChance);
    const decoupled = mode !== RewardMode.Honest;

    let fire = false;
    let intensity = 0;
    let jackpot = false;
    let nearMiss = false;

    switch (mode) {
      case RewardMode.Honest: {
        // Reward IFF the answer was correct (and the band actually rewards).
        fire = chance > 0 && sd > 0;
        intensity = fire ? base : 0;
        break;
      }
      case RewardMode.SpicierPick: {
        // Fires regardless of correctness; pays MORE for the hotter pick.
        // magnitude spans 0.6x..1.4x of base as pick heat goes 0..1.
        const heat = pickHeat01(p, answerEvent);
        fire = chance > 0;
        intensity = fire ? clamp01(base * (0.6 + 0.8 * heat)) : 0;
        break;
      }
      case RewardMode.ScaleWithScore: {
        // Magnitude tracks the RUNNING score, not this answer. Fires regardless
        // of this beat's correctness. 0.4x..1.3x of base as the run-rate 0..1.
        const rate = runningScoreRate();
        fire = chance > 0;
        intensity = fire ? clamp01(base * (0.4 + 0.9 * rate)) : 0;
        break;
      }
      case RewardMode.VariableRatio: {
        // Deterministic slot machine: a seeded stream decides fire + jackpot.
        const rFire = hash01(seed + '|vr-fire|' + vrCount);
        const rJack = hash01(seed + '|vr-jack|' + vrCount);
        vrCount += 1;
        fire = rFire < chance;
        if (fire) {
          // On a hit, magnitude spans 0.7x..1.3x of base; a top roll is a JACKPOT
          // (intensity boosted toward 1, ceremony flagged for effects/audio).
          jackpot = rJack >= JACKPOT_ROLL;
          intensity = jackpot ? clamp01(base * JACKPOT_BOOST) : clamp01(base * (0.7 + 0.6 * rJack));
        } else if (chance > 0 && rFire < chance + NEAR_MISS_WINDOW) {
          // The roll ALMOST fired -> near-miss tease (no payout, no intensity).
          nearMiss = true;
        }
        break;
      }
      default: {
        fire = false;
        intensity = 0;
      }
    }

    // STREAK MULTIPLIER: small magnitude juice from the render-side streak counter.
    const streak = (answerEvent && typeof answerEvent.streak === 'number')
      ? Math.max(0, answerEvent.streak | 0) : undefined;
    if (fire && streak) intensity = clamp01(intensity * (1 + Math.min(streak, STREAK_CAP) * STREAK_STEP));

    const out = {
      fire,
      intensity,
      kind: fire ? (p.kind || RewardKind.None) : RewardKind.None,
      decoupled,
    };
    if (jackpot) out.jackpot = true;
    if (nearMiss) out.nearMiss = true;
    if (streak !== undefined) out.streak = streak; // echo -> effects drive the meter
    return out;
  }

  /**
   * channelsFor(depth) -> Channels  (the sanctioned per-frame channel vector).
   * Wraps the single-source-of-truth curve and applies the user's caps. This is
   * the ONLY place in reward.js that clamps to caps — effects/audio/bg agents
   * may call this to get an already-clamped vector, or call depthToChannels +
   * clampToCaps themselves. Enforces invariant #2 (cap-clamp) inline.
   */
  function channelsFor(depth) {
    return clampToCaps(depthToChannels(depth), caps);
  }

  return { planFor, resolve, channelsFor };
}

export default createReward;
