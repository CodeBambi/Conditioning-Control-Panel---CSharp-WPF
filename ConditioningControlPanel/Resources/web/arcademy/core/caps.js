/* ============================================================================
 * core/caps.js — the intensity governor. Ported from intake/core/contracts.js
 * (DEFAULT_CAPS / clampToCaps / clampIntensity / depthToChannels).
 *
 * THE LAW (GROUND-RULES §6): nobody hardcodes an absolute strength. Every
 * strength shown on screen or heard is
 *
 *     heatToChannels(heat) -> clampToCaps(channels, caps) -> real units
 *
 * and strobe-class output is additionally multiplied by `effectIntensity`
 * (the photosensitivity guard, 0.2..1.5, DTRH's runEffectIntensity).
 *
 * Channel canon = Intake's names VERBATIM, so the audio channel is
 * `binauralDepth` (SYNTHESIS amendment 9 corrects GROUND-RULES §5's
 * `audioDepth`). Seven channels + masterIntensity:
 *
 *   flashRate      flash / burst cadence pressure          (0..1)
 *   flashOpacity   peak flash alpha                        (0..1)
 *   subDensity     subliminal word/image density           (0..1)
 *   duckDepth      audio ducking depth                     (0..1)
 *   bubbleRate     bubble-field spawn pressure             (0..1)
 *   binauralDepth  binaural bed / felt audio depth         (0..1)
 *   bgIntensity    background motion + dressing intensity  (0..1)
 *
 * Caps are the PLAYER'S CEILINGS (Arcademy settings page); masterIntensity is
 * the one global scalar. A game may spend less than a ceiling, never more.
 * ==========================================================================*/

/** @typedef {{flashRate:number, flashOpacity:number, subDensity:number, duckDepth:number,
 *             bubbleRate:number, binauralDepth:number, bgIntensity:number}} Channels */

export const CHANNELS = Object.freeze([
  'flashRate', 'flashOpacity', 'subDensity', 'duckDepth', 'bubbleRate', 'binauralDepth', 'bgIntensity',
]);

export const clamp01 = (n) => {
  const v = Number(n);
  if (!Number.isFinite(v)) return 0;
  return v < 0 ? 0 : v > 1 ? 1 : v;
};
/** Smoothstep ease: gentle toe, firm shoulder (intake's `_ease`). */
export const smoothstep = (d) => { const x = clamp01(d); return x * x * (3 - 2 * x); };
export const lerp = (a, b, t) => a + (b - a) * clamp01(t);
export const clampRange = (v, lo, hi) => {
  const n = Number(v);
  if (!Number.isFinite(n)) return lo;
  return n < lo ? lo : n > hi ? hi : n;
};

/**
 * Accept BOTH parameter conventions the canon carries: Intake's 0..1 magnitudes
 * and DTRH's `strength 0-100` (GROUND-RULES §6 "parameter shapes = DTRH's
 * strength 0-100 ... driven through Intake's channel curve"). Anything above 1 is
 * read as a percentage, so strength:20 and strength:0.2 mean the same thing.
 */
export function pct01(v) {
  const n = Number(v);
  if (!Number.isFinite(n)) return 0;
  if (n > 1) return clamp01(n / 100);
  return clamp01(n);
}

export const DEFAULT_CAPS = Object.freeze({
  flashRate: 1, flashOpacity: 1, subDensity: 1, duckDepth: 1,
  bubbleRate: 1, binauralDepth: 1, bgIntensity: 1,
  masterIntensity: 1, // global scalar multiplied into every channel + every reward
});

/**
 * THE ONE HEAT CURVE (port of intake's depthToChannels, renamed per contract).
 * One scalar in, the whole channel vector out; NOBODY re-derives it. Raw 0..1 —
 * clamp to caps before anything is shown.
 * @param {number} heat 0..1
 * @returns {Channels}
 */
export function heatToChannels(heat) {
  const d = clamp01(heat);
  const e = smoothstep(d);
  return {
    // flashes stay near-silent early, then climb
    flashRate:     clamp01(Math.max(0, d - 0.15) / 0.85) * 0.9,
    flashOpacity:  clamp01(0.15 + e * 0.85),
    // subliminals fade in earliest (they are the camouflage layer)
    subDensity:    clamp01(0.08 + e * 0.92),
    duckDepth:     clamp01(e * 0.9),
    bubbleRate:    clamp01(Math.max(0, d - 0.10) / 0.90),
    binauralDepth: e,
    bgIntensity:   clamp01(0.20 + e * 0.80),
  };
}

/** Clamp a raw channel vector to the player's caps (× masterIntensity). New object. */
export function clampToCaps(channels, caps = DEFAULT_CAPS) {
  const c = caps || DEFAULT_CAPS;
  const master = clamp01(c.masterIntensity == null ? 1 : c.masterIntensity);
  const out = {};
  for (const ch of CHANNELS) {
    const raw = clamp01(channels ? channels[ch] : 0);
    const cap = clamp01(c[ch] == null ? 1 : c[ch]);
    out[ch] = raw * cap * master;
  }
  return out;
}

/** Clamp a single 0..1 magnitude (reward intensity, one-shot strength) to caps. */
export function clampIntensity(intensity, caps = DEFAULT_CAPS) {
  const c = caps || DEFAULT_CAPS;
  const master = clamp01(c.masterIntensity == null ? 1 : c.masterIntensity);
  return clamp01(intensity) * master;
}

/**
 * Build the caps vector the engine actually runs on: the init projection's 7
 * channel ceilings + masterIntensity folded in as the 8th field, so a single
 * clampToCaps call satisfies "clampToCaps × masterIntensity". Unknown keys are
 * dropped; missing keys default to 1 (a ceiling, not an amplifier).
 */
export function capsFrom(caps, masterIntensity) {
  const out = { masterIntensity: clamp01(masterIntensity == null ? 1 : masterIntensity) };
  for (const ch of CHANNELS) out[ch] = clamp01(caps && caps[ch] != null ? caps[ch] : 1);
  return out;
}

/** The photosensitivity guard's legal range (DTRH runEffectIntensity 0.2..1.5). */
export const EFFECT_INTENSITY_MIN = 0.2;
export const EFFECT_INTENSITY_MAX = 1.5;
export const EFFECT_INTENSITY_DEFAULT = 0.85;
export function clampEffectIntensity(v) {
  const n = Number(v);
  if (!Number.isFinite(n)) return EFFECT_INTENSITY_DEFAULT;
  return clampRange(n, EFFECT_INTENSITY_MIN, EFFECT_INTENSITY_MAX);
}

export default { CHANNELS, DEFAULT_CAPS, heatToChannels, clampToCaps, clampIntensity, capsFrom };
