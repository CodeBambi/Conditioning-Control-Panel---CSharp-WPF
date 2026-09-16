/* ============================================================================
 * engine/schedule.js — the reward schedule (variable-ratio canon).
 *
 * Ported from the CURRENT worktree state of intake/core/reward.js, including the
 * SEEDED kind roll (`pickKind` used to be the one Math.random hole) and the
 * RewardKind.GifBurst / GifRain members. Numbers are law:
 *
 *   VariableRatio baseChance = clamp01(0.30 + 0.30 * smoothstep(heat))  -> .30..60
 *   JACKPOT_ROLL 0.85 · JACKPOT_BOOST 1.5 · NEAR_MISS_WINDOW 0.08
 *   STREAK_CAP 8 × STREAK_STEP 0.03
 *   GIFBURST_CHANCE 0.18 · GIFRAIN_CHANCE 0.05
 *   reward salience = .45 flashOpacity + .30 bubbleRate + .25 binauralDepth
 *
 * Magnitudes out of here are RAW 0..1; the effect layer clamps them to caps
 * (invariant #2 — nobody hardcodes an absolute strength).
 *
 * Arcademy kind names are the ENGINE's kinds, so a game can hand the returned
 * `kind` straight back to engine.fire()/ceremony():
 *   'chime' | 'flash_burst' | 'bubble_field' | 'gif_burst' | 'gif_rain' | 'none'
 * ==========================================================================*/

import { clamp01, smoothstep, heatToChannels } from '../core/caps.js';
import { hash01, makeRng } from '../core/rng.js';

export const JACKPOT_ROLL = 0.85;
export const JACKPOT_BOOST = 1.5;
export const NEAR_MISS_WINDOW = 0.08;
export const STREAK_CAP = 8;
export const STREAK_STEP = 0.03;
export const GIFBURST_CHANCE = 0.18;
export const GIFRAIN_CHANCE = 0.05;

export const RewardMode = Object.freeze({
  Honest: 'honest',                 // pays iff the player actually succeeded
  ScaleWithScore: 'scale-score',    // magnitude tracks the running class score
  VariableRatio: 'variable-ratio',  // the slot machine (the Arcademy default)
  None: 'none',
});

export const RewardKind = Object.freeze({
  Chime: 'chime',
  Flash: 'flash_burst',
  Bubble: 'bubble_field',
  GifBurst: 'gif_burst',
  GifRain: 'gif_rain',
  None: 'none',
});

/** Reward salience from the ONE heat curve — never a re-derived curve. */
export function rewardIntensity01(heat) {
  const c = heatToChannels(heat);
  return clamp01(0.45 * c.flashOpacity + 0.30 * c.bubbleRate + 0.25 * c.binauralDepth);
}

/** VariableRatio base chance for a heat (0.30 -> 0.60). */
export function baseChanceFor(mode, heat) {
  switch (mode) {
    case RewardMode.None: return 0;
    case RewardMode.VariableRatio: return clamp01(0.30 + 0.30 * smoothstep(heat));
    default: return 1.0;
  }
}

/**
 * createRewardSchedule({ seed, mode })
 *
 * roll({ heat, success, streak, mode, force }) ->
 *   { fire, intensity, kind, jackpot, nearMiss, streak, mode, chance }
 *
 * The kind roll and the fire/jackpot rolls ride SEPARATE tag namespaces on one
 * seeded stream, so adding a roll never shifts an existing sequence for a seed.
 */
export function createRewardSchedule({ seed, mode = RewardMode.VariableRatio } = {}) {
  const s = seed == null ? ('vr-' + Math.floor(Math.random() * 1e9)) : String(seed);
  /* PORT FIX (measured, reported in the build summary): intake draws the fire roll
   * AND the jackpot roll as hash01(seed+'|vr-fire|'+n) / hash01(seed+'|vr-jack|'+n).
   * Those two strings differ in one character, and FNV-1a over near-identical
   * inputs correlates enough that the jackpot share of HITS measured ~10% instead
   * of the ~15% the .85 threshold intends. The threshold, the boost and the fire
   * chance are all unchanged canon; only the jackpot roll now comes off its own
   * mulberry32 sub-stream (one draw per VR roll, so a seed still replays exactly). */
  const jackRng = makeRng(s + '|vr-jack');
  let vrCount = 0;
  let kindCount = 0;
  let runScore = 0;
  let runPeak = 0;
  let runBeats = 0;

  const kindRoll = (tag) => hash01(s + '|kind-' + tag + '|' + kindCount);

  function pickKind(heat, opts = {}) {
    if (opts.kind) return opts.kind;                       // caller pins the payload
    const h = clamp01(heat);
    const allow = opts.allow || null;                      // e.g. a game's manifest
    const ok = (k) => !allow || allow.includes(k);
    let k;
    if (ok(RewardKind.GifBurst) && kindRoll('burst') < GIFBURST_CHANCE) k = RewardKind.GifBurst;
    else if (ok(RewardKind.GifRain) && kindRoll('rain') < GIFRAIN_CHANCE) k = RewardKind.GifRain;
    else if (h >= 0.6 && ok(RewardKind.Flash)) k = RewardKind.Flash;
    else if (h >= 0.3 && ok(RewardKind.Bubble)) k = RewardKind.Bubble;
    else k = RewardKind.Chime;
    kindCount += 1;
    return k;
  }

  function runningScoreRate() {
    if (runBeats <= 0 || runPeak <= 0) return 0;
    return clamp01(runScore / (runPeak * runBeats));
  }

  function roll(opts = {}) {
    const heat = clamp01(opts.heat);
    const m = opts.mode || mode;
    const base = clamp01(opts.baseIntensity == null ? rewardIntensity01(heat) : opts.baseIntensity);
    const chance = clamp01(opts.chance == null ? baseChanceFor(m, heat) : opts.chance);
    const score = Number.isFinite(opts.score) ? opts.score : (opts.success ? 1 : 0);

    runBeats += 1;
    if (score > 0) runScore += score;
    if (score > runPeak) runPeak = score;

    let fire = false;
    let intensity = 0;
    let jackpot = false;
    let nearMiss = false;

    if (opts.force) {
      fire = true;
      intensity = base;
      jackpot = !!opts.jackpot;
    } else if (m === RewardMode.Honest) {
      fire = chance > 0 && !!opts.success;
      intensity = fire ? base : 0;
    } else if (m === RewardMode.ScaleWithScore) {
      fire = chance > 0;
      intensity = fire ? clamp01(base * (0.4 + 0.9 * runningScoreRate())) : 0;
    } else if (m === RewardMode.VariableRatio) {
      const rFire = hash01(s + '|vr-fire|' + vrCount);
      const rJack = jackRng();
      vrCount += 1;
      fire = rFire < chance;
      if (fire) {
        jackpot = rJack >= JACKPOT_ROLL;
        intensity = jackpot ? clamp01(base * JACKPOT_BOOST) : clamp01(base * (0.7 + 0.6 * rJack));
      } else if (chance > 0 && rFire < chance + NEAR_MISS_WINDOW) {
        nearMiss = true;      // the roll ALMOST landed -> tease, no payout
      }
    }

    const streak = Number.isFinite(opts.streak) ? Math.max(0, opts.streak | 0) : undefined;
    if (fire && streak) intensity = clamp01(intensity * (1 + Math.min(streak, STREAK_CAP) * STREAK_STEP));

    const out = {
      fire, intensity, mode: m, chance,
      kind: fire ? pickKind(heat, opts) : RewardKind.None,
      jackpot, nearMiss,
    };
    if (streak !== undefined) out.streak = streak;
    return out;
  }

  function reset() { vrCount = 0; kindCount = 0; runScore = 0; runPeak = 0; runBeats = 0; }

  return { roll, reset, pickKind, get counts() { return { vrCount, kindCount, runBeats }; } };
}

export default createRewardSchedule;
