/* ============================================================================
 * effects.js — in-page effect layer for "Graded Intake" (Agent E).
 *
 * Self-contained DOM effects driven by ONE depth scalar + RewardEvents. NO
 * coupling to WPF App.Flash/App.Bubbles — everything is a DOM overlay mounted
 * inside the beat stage (`root`). Four visual channels ride the depth curve:
 *   flashes · subliminals · ambient bubbles · reward payloads (flash/bubble/drop/praise).
 *
 * CONTRACT (contracts.js §"EFFECTS + AUDIO (Agent E)"):
 *   createEffects({ root, caps, media, theme }) -> {
 *     setDepth(depth),                 // depthToChannels -> clampToCaps -> cadence/opacity
 *     play(rewardEvent, depth),        // clampIntensity -> no-repeat VARIANT per RewardKind;
 *                                      //   honors jackpot / nearMiss / streak
 *     recover(depth),                  // invariant #3: un-ramp; depth 0 = all off/removed
 *     dispose(),                       // full teardown (recover(0) + timers) for hosts
 *   }
 *   `media` (MediaManifest|null): when present, Drop/jackpot payloads use real
 *   GIFs/images from the manifest; absent -> particle stand-ins (unchanged).
 *   media.subliminals: the user's active subliminal phrases (garnish words);
 *   absent/empty -> theme.praise.
 *   `theme` (resolved via themeOf, optional): praise pool + accent colors.
 *
 * VARIETY (the "0 variety" fix): every RewardKind owns a pool of 3-5 visual
 * variants. Each fire picks one with a NO-REPEAT-last rotation per kind, and
 * the pick is intensity-weighted (subtle variants at low intensity, spectacular
 * at high — see variantWeights/pickVariantIndex, exported + testable).
 *
 * AMBIENT + GARNISH (presentation upgrade): (1) an AMBIENT layer — occasionally
 * a GIF/still from the manifest DRIFTS across the viewport or GHOSTS in and out,
 * ghost-faint, BEHIND the question card (own z-1 layer; depth-gated cadence
 * ~45s shallow -> ~18s deep; max 2 concurrent; off below depth ~0.2, under
 * reduced motion, without media, and through Recovery). (2) fullscreen reward
 * GARNISHES that PAIR with a firing reward — ALWAYS GARNISH, ALWAYS DIFFERENT:
 * pink wash / braindrain (dim + blur + faint luminosity-blend image wash) /
 * subliminal word flashes (media.subliminals else theme.praise) / a LIVE LOOM
 * SPIRAL (lazy dynamic import of ../../dtrh/shared/loomField.js — import
 * failure just retires the spiral garnish). Every FIRED reward above a small
 * intensity/depth floor draws the NEXT garnish from a SHUFFLED BAG of all four
 * (createGarnishBag, exported + testable): the bag reshuffles when empty with
 * no immediate repeat across the boundary, so the player cycles through every
 * look. Jackpots skip the bag and force drain-or-spiral (still consumed from
 * the rotation). One garnish at a time (a new one fast-fades the old), all
 * pointer-events:none, all clamped by caps, none during Recovery (recover()
 * force-clears a live one). Spiral/subliminal garnishes ping audio.js via the
 * 'intake-garnish' window CustomEvent (loose seam — boot wiring unchanged;
 * audio clamps by ITS caps); loom import failure / first spiral frame emit an
 * 'intake-log' window CustomEvent so the C# shim can surface them (no
 * devtools in-app; harmless if nobody listens).
 *
 * INVARIANTS honored here:
 *   #2 — every level flows from depthToChannels(depth) x clampToCaps(...,caps) or
 *        clampIntensity(intensity,caps). We NEVER hardcode an absolute strength;
 *        the constants below are ONLY 0..1 -> real-unit (ms / px / alpha) glue.
 *   #3 — recover() walks the stack down; recover(<=0) tears EVERYTHING out —
 *        spawned nodes, media (gif/image) nodes, AND the streak meter.
 *
 * IMPORTS ARE SIDE-EFFECT FREE: no document/DOM access at module load. All DOM
 * work is guarded inside the factory + its methods, so importing this never
 * throws (a throw-at-import = silent infinite loader spin — see dtrh gotchas).
 * Media loading is lazy (src set at spawn), error-tolerant (onerror removes the
 * node), and capped at MAX_MEDIA_NODES concurrent nodes.
 *
 * The pure mappings (channel vector -> render numbers, variant math, specs) are
 * exported for headless tests; the live path calls them too so the tested math
 * IS the shipped math.
 * ==========================================================================*/

import { depthToChannels, clampToCaps, clampIntensity, RewardKind, lerp, clamp01 } from '../core/contracts.js';
import { spiralPalette, harvestOpen } from '../core/palette.js';
import { recordSpiral } from '../core/spiralLog.js';
import { noteMedia } from '../core/mediaLog.js';

/* ----------------------------------------------------------------------------
 * PURE MAPPING — clamped channel vector -> concrete render numbers.
 * Only 0..1 -> real-unit translation lives here (spawn intervals in ms, peak
 * alphas). The CURVE + CAPS already happened upstream (depthToChannels x caps);
 * this never re-derives either. Exported + unit-tested headless.
 * -------------------------------------------------------------------------- */

/** Interval range endpoints (ms). A channel of 0 => Infinity (silent/off). */
const FLASH_MS  = { slow: 1600, fast: 110 };
const SUB_MS    = { slow: 2600, fast: 360 };
const BUBBLE_MS = { slow: 2200, fast: 260 };

/** Comfort ceiling on the full-screen flash wash so cap=1 is bright, not blinding.
 *  This is a safety clamp on the OUTPUT alpha, not a re-derivation of the curve. */
const FLASH_ALPHA_CEIL = 0.85;

/** Clamp to an arbitrary range (contracts only exports clamp01). Pure. */
const clampRange = (v, lo, hi) => (v < lo ? lo : (v > hi ? hi : v));

const _finiteInterval = (active, rate, slow, fast) =>
  active ? lerp(slow, fast, clamp01(rate)) : Infinity;

/**
 * @param {import('../core/contracts.js').Channels} ch  ALREADY clamped to caps.
 * @returns {{
 *   flashOn:boolean, flashMs:number, flashAlpha:number,
 *   subOn:boolean, subMs:number, subAlpha:number,
 *   bubbleOn:boolean, bubbleMs:number, bubbleAlpha:number
 * }}
 */
export function channelsToVisual(ch) {
  const flashRate    = clamp01(ch && ch.flashRate);
  const flashOpacity = clamp01(ch && ch.flashOpacity);
  const subDensity   = clamp01(ch && ch.subDensity);
  const bubbleRate   = clamp01(ch && ch.bubbleRate);

  const flashOn  = flashRate  > 0.001;
  const subOn    = subDensity > 0.001;
  const bubbleOn = bubbleRate > 0.001;

  return {
    flashOn,
    flashMs:    _finiteInterval(flashOn, flashRate, FLASH_MS.slow, FLASH_MS.fast),
    // flashOpacity is the peak; cap already applied, comfort ceiling on top.
    flashAlpha: flashOpacity * FLASH_ALPHA_CEIL,

    subOn,
    subMs:    _finiteInterval(subOn, subDensity, SUB_MS.slow, SUB_MS.fast),
    // subliminals are faint by design: 0.05 floor + up to +0.35.
    subAlpha: subOn ? (0.05 + subDensity * 0.35) : 0,

    bubbleOn,
    bubbleMs:    _finiteInterval(bubbleOn, bubbleRate, BUBBLE_MS.slow, BUBBLE_MS.fast),
    bubbleAlpha: bubbleOn ? (0.10 + bubbleRate * 0.40) : 0,
  };
}

/** Reward intensity (already clampIntensity'd) -> flash-pulse numbers. Pure. */
export function rewardFlashSpec(intensity) {
  const i = clamp01(intensity);
  return { alpha: (0.25 + i * 0.60) * FLASH_ALPHA_CEIL, durMs: 220 + i * 260 };
}
/** Reward intensity -> particle-burst numbers (drop / bubble reward). Pure. */
export function rewardBurstSpec(intensity) {
  const i = clamp01(intensity);
  return { count: Math.round(4 + i * 12), spreadPx: 60 + i * 160, durMs: 620 + i * 640 };
}

/* ----------------------------------------------------------------------------
 * PURE VARIANT MATH — intensity-weighted, no-repeat-last rotation.
 * Variants inside each pool are ordered subtle -> spectacular; the weight window
 * slides with intensity so low pays quiet and high pays loud. Exported + tested.
 * -------------------------------------------------------------------------- */

/** Weight vector for n variants at a given intensity (triangular window with a
 *  floor so every variant stays reachable). Pure. */
export function variantWeights(n, intensity) {
  const count = Math.max(1, n | 0);
  const c = clamp01(intensity) * (count - 1); // window center slides subtle->spectacular
  const w = [];
  for (let k = 0; k < count; k++) {
    w.push(Math.max(0.12, 1 - (Math.abs(k - c) / Math.max(1, count - 1)) * 0.9));
  }
  return w;
}

/** Roll a variant index: intensity-weighted, never repeating lastIndex. Pure
 *  when `rand` (0..1) is supplied. */
export function pickVariantIndex(n, lastIndex, intensity, rand = Math.random()) {
  const count = Math.max(1, n | 0);
  if (count === 1) return 0;
  const w = variantWeights(count, intensity);
  if (lastIndex >= 0 && lastIndex < count) w[lastIndex] = 0; // NO-REPEAT-last
  let total = 0;
  for (let k = 0; k < count; k++) total += w[k];
  if (total <= 0) return (lastIndex + 1) % count;
  let roll = clamp01(rand) * total;
  for (let k = 0; k < count; k++) { roll -= w[k]; if (roll <= 0) return k; }
  return count - 1;
}

/** Reward intensity -> GIF-burst numbers (Drop with media). Pure. */
export function gifBurstSpec(intensity) {
  const i = clamp01(intensity);
  return {
    count:   2 + Math.round(i * 2),            // 2..4 gif nodes
    holdMs:  600 + Math.round(i * 600),        // 600..1200 hold, per brief
    sizePx:  Math.round(120 + i * 150),        // approximate box edge
    enterMs: 200,
    exitMs:  280,
  };
}

/* ----------------------------------------------------------------------------
 * GIFBURST — in-browser CCP-flash REWARD (owner-directed). The kind string
 * mirrors a recommended contracts.js `RewardKind.GifBurst: 'gifburst'` (see the
 * FINAL REPORT contract-gap note); until that enum lands the literal is
 * duplicated here + in reward.js (same pattern as GARNISH_CUE_EVENT). Exported
 * so reward-roll / opacity tests can reference the shipped values.
 * -------------------------------------------------------------------------- */
export const GIFBURST_KIND = 'gifburst';

/** GIF RAIN — the burst's rarer sibling (the DTRH gif-cascade port). Same
 *  contract-gap story as GIFBURST_KIND above: the literal is duplicated in
 *  core/reward.js, which is where the roll that fires it lives. */
export const GIFRAIN_KIND = 'gifrain';

/** Run-progress -> GifBurst opacity, HARDCODED per owner (no settings). The
 *  owner's ladder is BY BAND (Calibration .15 / Establishing .30 / Deepening
 *  .50 / Climax .75 / Recovery 1.00). effects.js only receives a depth scalar,
 *  so we map via the band depth-floors (mirror of contracts.js BAND_DEPTH_FLOOR:
 *  Establishing 0.18, Deepening 0.42, Climax 0.72). Recovery's 1.00 rung is
 *  unreachable through the reward roll — rewards never fire in Recovery
 *  (reward.js baseChance 0) and depth alone can't distinguish it. Pure. */
export function gifBurstOpacityForDepth(depth) {
  const d = clamp01(depth);
  if (d >= 0.72) return 0.75; // Climax
  if (d >= 0.42) return 0.50; // Deepening
  if (d >= 0.18) return 0.30; // Establishing
  return 0.15;                // Calibration
}

/** Run-progress -> HOW MANY gifs one burst spills, owner-directed: about ONE at
 *  the top of the run climbing to a crowd of ~10 by the bottom. The window
 *  itself widens with depth (lo 1->5, hi 1->10) and the count is rolled INSIDE
 *  that window, so two bursts at the same depth rarely match — it reads as a
 *  spill, never as a counter ticking up. The caller keeps reduced motion at one
 *  and clamps to the layer's node budget. Pure when `rand` (0..1) is supplied. */
export function gifBurstCountForDepth(depth, rand = Math.random()) {
  const d = clamp01(depth);
  const lo = 1 + Math.round(d * 4);                 // 1 .. 5
  const hi = Math.max(lo, 1 + Math.round(d * 9));   // 1 .. 10
  return Math.min(hi, lo + Math.floor(clamp01(rand) * (hi - lo + 1)));
}

/** Streak value -> meter display numbers. Hidden under 2, capped at 10. Pure. */
export function streakMeterSpec(streak) {
  const s = Math.max(0, streak | 0);
  return {
    visible: s >= 2,
    lit:     Math.min(s, 10),
    glow:    clamp01((s - 2) / 8), // glow intensifies toward the cap
  };
}

/** Reward intensity -> jackpot-ceremony numbers. Pure. */
export function jackpotSpec(intensity) {
  const i = clamp01(intensity);
  return {
    dimMs:            250,                      // anticipation beat
    shimmerMs:        2000,                     // gold/accent shimmer overlay
    bursts:           3 + Math.round(i * 2),
    particlesPerBurst: Math.round(10 + i * 14),
    bubbles:          Math.round(8 + i * 10),
    spotlightMs:      Math.round(1500 + i * 500),
  };
}

/** Reward intensity -> near-miss tease numbers (fire=false shimmer). Pure. */
export function nearMissSpec(intensity) {
  const i = clamp01(intensity);
  return {
    alpha:     0.05 + i * 0.10, // barely-there by design
    durMs:     400,
    particles: 3 + Math.round(i * 2),
  };
}

/* ----------------------------------------------------------------------------
 * PURE AMBIENT + GARNISH MATH — cadence/opacity curves and the garnish gamble.
 * Same rules as the variant math above: pure when `rand`/`rng` is supplied,
 * exported for headless tests, and the live path consumes EXACTLY these.
 * -------------------------------------------------------------------------- */

/** Ambient drifters only wake once the descent is properly under way. */
export const AMBIENT_MIN_DEPTH = 0.2;

/** Depth -> ambient-layer schedule: interval shrinks ~45s -> ~18s as depth
 *  deepens (±25% jitter via `rand`), opacity climbs 0.10 -> 0.28 (pre-cap).
 *  Below AMBIENT_MIN_DEPTH the layer is off entirely. Pure. */
export function ambientSpec(depth, rand = Math.random()) {
  const d = clamp01(depth);
  if (d <= AMBIENT_MIN_DEPTH) return { on: false, intervalMs: Infinity, opacity: 0 };
  const t = clamp01((d - AMBIENT_MIN_DEPTH) / (1 - AMBIENT_MIN_DEPTH));
  return {
    on: true,
    intervalMs: Math.round(lerp(45000, 18000, t) * (0.75 + clamp01(rand) * 0.5)),
    opacity: 0.10 + t * 0.18, // ghost-faint by design; visual cap multiplies on top
  };
}

/** The four fullscreen garnishes, ordered gentle -> "ulterior reward". */
export const GARNISH_KINDS = Object.freeze(['pink', 'sublim', 'drain', 'spiral']);

/** Floors for the always-garnish rotation: a fired reward below either one
 *  draws nothing (jackpots are exempt). Deliberately tiny — the play-test
 *  verdict on the old probability gates was "saw one pink wash all run". */
export const GARNISH_MIN_INTENSITY = 0.2;
export const GARNISH_MIN_DEPTH = 0.1;

/**
 * The shuffled-bag rotation (ALWAYS GARNISH, ALWAYS DIFFERENT). draw() pops
 * the next garnish name; an empty bag reshuffles with no immediate repeat
 * across the boundary, so 8 straight draws hit all four kinds twice. draw()
 * takes an `avail` subset (live path drops 'spiral' when the loom import
 * failed) — unavailable names are skipped AND stay consumed. force(names)
 * is the jackpot path: picks among `names` (preferring one still waiting in
 * the bag, avoiding a back-to-back repeat) and consumes it from the rotation.
 * Pure when `rng` is supplied. Returns { draw, force, take }.
 */
export function createGarnishBag(rng = Math.random, kinds = GARNISH_KINDS) {
  const all = kinds.slice();
  let bag = [];
  let last = null;
  const roll = (n) => Math.min(n - 1, Math.floor(clamp01(rng()) * n)); // 0..n-1
  function refill(avail) {
    bag = avail.slice();
    for (let k = bag.length - 1; k > 0; k--) { // Fisher-Yates
      const j = roll(k + 1);
      const t = bag[k]; bag[k] = bag[j]; bag[j] = t;
    }
    // draws pop from the END: never let the fresh bag open on the last draw
    if (bag.length > 1 && bag[bag.length - 1] === last) {
      const t = bag[bag.length - 1]; bag[bag.length - 1] = bag[0]; bag[0] = t;
    }
  }
  function take(name) { // consume one occurrence from the rotation
    const idx = bag.indexOf(name);
    if (idx >= 0) bag.splice(idx, 1);
    last = name;
    return name;
  }
  function draw(avail = all) {
    const ok = all.filter((n) => avail.includes(n));
    if (!ok.length) return null;
    for (let guard = 0; guard < 2; guard++) {
      while (bag.length) {
        const n = bag.pop();
        if (ok.includes(n)) { last = n; return n; }
      }
      refill(ok);
    }
    return null; // unreachable: refill(ok) is non-empty
  }
  function force(names) {
    const ok = all.filter((n) => names.includes(n));
    if (!ok.length) return null;
    if (!bag.length) refill(all); // an empty bag consumes from the NEXT cycle
    const inBag = ok.filter((n) => bag.includes(n));
    let pool = inBag.length ? inBag : ok;
    if (pool.length > 1 && pool.includes(last)) pool = pool.filter((n) => n !== last);
    return take(pool[roll(pool.length)]);
  }
  return { draw, force, take };
}

/* The three helpers below are the RETIRED probability-gate model — kept
 * exported for test compat, but the live path now runs on createGarnishBag. */

/** LEGACY. Chance (0..1) that a FIRED reward of `kind` pairs with a garnish.
 *  Jackpots always garnish; Chime unlocks at depth 0.35 and climbs; everything
 *  else only gets a small chance deep. Pure. */
export function garnishChance(kind, depth, jackpot = false) {
  if (jackpot) return 1;
  const d = clamp01(depth);
  if (kind === RewardKind.None) return 0;
  if (kind === RewardKind.Chime) {
    if (d < 0.35) return 0;
    return 0.35 + 0.40 * ((d - 0.35) / 0.65); // 35% at 0.35 -> 75% at full depth
  }
  if (d < 0.5) return 0;
  return 0.10 + 0.15 * ((d - 0.5) / 0.5);     // 10% -> 25% deep
}

/** LEGACY. Weight per garnish name at a given intensity. The spectacle pair
 *  (drain / spiral) scales with intensity; jackpots skew toward them. Pure. */
export function garnishWeights(intensity, jackpot = false) {
  const i = clamp01(intensity);
  const w = {
    pink:   1.0,
    sublim: 0.9,
    drain:  0.55 + 0.45 * i,
    spiral: 0.30 + 0.55 * i,
  };
  if (jackpot) { w.pink *= 0.4; w.sublim *= 0.5; w.drain *= 2.0; w.spiral *= 2.6; }
  return w;
}

/** LEGACY. The garnish gamble: gate roll (garnishChance) then a weighted,
 *  no-repeat-last pick among `kinds`. Consumes rng() twice. Returns a garnish
 *  name or null. Pure when `rng` is supplied. */
export function pickGarnish(kind, depth, intensity, { jackpot = false, last = null, rng = Math.random, kinds = GARNISH_KINDS } = {}) {
  const avail = GARNISH_KINDS.filter((n) => kinds.includes(n));
  if (!avail.length) return null;
  if (rng() >= garnishChance(kind, depth, jackpot)) return null;
  const w = garnishWeights(intensity, jackpot);
  const pool = avail.map((n) => ({ n, w: (n === last && avail.length > 1) ? 0 : w[n] })); // NO-REPEAT-last
  let total = 0;
  for (const p of pool) total += p.w;
  if (total <= 0) return avail[0];
  let roll = clamp01(rng()) * total;
  for (const p of pool) { roll -= p.w; if (roll <= 0) return p.n; }
  return pool[pool.length - 1].n;
}

/** Garnish intensity -> pink-wash numbers (~2.5-4s tint pulse). Pure. */
export function pinkWashSpec(intensity) {
  const i = clamp01(intensity);
  return { alpha: 0.20 + i * 0.30, durMs: Math.round(2500 + i * 1500) };
}
/** Garnish intensity -> braindrain numbers (~5s dim + blur + faint wash). Pure. */
export function drainWashSpec(intensity) {
  const i = clamp01(intensity);
  return { alpha: 0.35 + i * 0.25, durMs: 5000 };
}
/** Garnish intensity -> subliminal-flash numbers: 2-4 rapid word blinks,
 *  140-200ms on, ~350ms apart, low alpha so it reads subliminal. Pure. */
export function sublimFlashSpec(intensity, rand = Math.random()) {
  const i = clamp01(intensity);
  return {
    flashes: 2 + Math.round(clamp01(rand) * 2),  // 2..4
    onMs:    Math.round(140 + i * 60),           // 140..200
    gapMs:   350,
    alpha:   0.22 + i * 0.18,                    // 0.22..0.40
  };
}
/** Garnish intensity -> live loom-spiral numbers (~4-5s, faded). Pure. */
export function spiralGarnishSpec(intensity) {
  const i = clamp01(intensity);
  return { alpha: 0.30 + i * 0.15, durMs: Math.round(4000 + i * 1000), fadeMs: 700 };
}

/* Cross-module spiral no-repeat guard. beats.js carries an IDENTICAL copy (no
 * shared module — intentional duplication) and both route their spiral params
 * through it, so window.__ixSpiralSig makes an interlude spiral and a garnish
 * spiral never land on the same look back-to-back either. `make` MUST mint a
 * fresh params object per call; we reroll up to 4x if it matches the last sig. */
function freshSpiralParams(make) {   // make: () => params object
  let p = make();
  const sig = (o) => { try { return JSON.stringify(o); } catch { return String(Math.random()); } };
  for (let i = 0; i < 4 && sig(p) === window.__ixSpiralSig; i++) p = make();
  window.__ixSpiralSig = sig(p);
  // Recap: the garnish spiral is woven from the player's harvested colours, so
  // the outro re-renders it (core/spiralLog.js -> boot.js runOutro). Same hook
  // beats.js installs in its own copy. Never throws; returns `p` untouched.
  return recordSpiral(p);
}

/* Faint, niche-agnostic subliminal pool. Niche-specific words come from prompts/
 * AI upstream (Agents A/H) — the effect layer stays persona-neutral on purpose. */
const SUBLIMINAL_WORDS = [
  'focus', 'deeper', 'relax', 'soft', 'yes', 'sink',
  'listen', 'good', 'let go', 'drift', 'easy', 'obey',
];
const PRAISE_WORDS = ['good', 'perfect', 'so good', 'yes', 'well done', 'gooood'];

const DEFAULT_ACCENT  = '#ff69b4';
const DEFAULT_ACCENT2 = '#b06cff';

/** Concurrent <img> nodes (gifs + polaroids) allowed at once — leak guard. */
const MAX_MEDIA_NODES = 6;
/** Concurrent ambient drifters (own layer, own counter — separate leak guard). */
const MAX_AMBIENT = 2;
const STREAK_SEGMENTS = 10;
/** Slow fade-in / gentle fade-out timings (ms) so NOTHING pops. Shared by the
 *  fullscreen garnishes (pink wash / drain / loom spiral) and the reward+jackpot
 *  gif nodes. Preemption (a new garnish replacing a live one) fast-forwards to
 *  GARNISH_PREEMPT_MS; dispose()/recover(0) removes instantly. Fades are clamped
 *  against each layer's own lifetime so a short garnish still gets a real fade. */
const GARNISH_FADE_IN_MS  = 1600;
const GARNISH_FADE_OUT_MS = 900;
const GARNISH_PREEMPT_MS  = 250;
const GIF_FADE_IN_MS  = 1200;
const GIF_FADE_OUT_MS = 700;
/** GifBurst (in-browser flash reward) timings + physics thresholds. All
 *  hardcoded per owner directive (no settings). */
const GIFBURST_LIFE_MS    = 6000;  // hard cap on UNPAUSED on-screen time
const GIFBURST_POP_MS     = 250;   // pop-in overshoot
const GIFBURST_FADE_MS    = 700;   // auto fade-out at the 6s cap
const GIFBURST_DISMISS_MS = 200;   // click-dismiss: quick fade + shrink
const GIFBURST_DRAG_PX    = 6;     // release under this travel = a click, not a drag
const GIFBURST_FLING_MIN  = 0.45;  // px/ms release speed to fling (slower = drop in place)
/** Concurrent burst gifs allowed on screen at once — the burst layer's OWN leak
 *  guard (deliberately separate from MAX_MEDIA_NODES so a deep 10-gif spill can
 *  never starve the ambient/polaroid budget, nor be starved BY it). */
const GIFBURST_MAX_NODES  = 10;
const GIFBURST_STAGGER_MS = 90;    // pop-in delay per burst member (spill, not pop)
/** GIF RAIN — the rarer sibling of the burst (see the GIFRAIN section below).
 *  These are DTRH's numbers verbatim (dtrh/game/payloadFx.js gifCascade, itself
 *  the port of the C# ChaosGifCascadeOverlay): SPAWN_RATE 1.67/s across a ~6s
 *  window (~10 gifs) with a 2.4-3.8s fall. Kept as-is — the intake's viewport is
 *  the same fullscreen page DTRH rains into, so nothing here needed retuning. */
const GIFRAIN_WINDOW_MS   = 6000;
const GIFRAIN_GAP_MS      = 1000 / 1.67;
const GIFRAIN_FALL_MIN_S  = 2.4;
const GIFRAIN_FALL_SPAN_S = 1.4;
/** Concurrent falling gifs — DTRH's MAX_CASCADE, and the rain layer's OWN leak
 *  guard (same reasoning as GIFBURST_MAX_NODES: never share a budget between two
 *  layers that can be live at the same time). */
const GIFRAIN_MAX_NODES   = 14;
/** Window CustomEvent name effects fires when a spiral/sublim garnish shows —
 *  audio.js listens for it (same literal there; contracts.js stays untouched). */
export const GARNISH_CUE_EVENT = 'intake-garnish';

/* ----------------------------------------------------------------------------
 * SCOPED STYLES — injected once, guarded. Class prefix `ixfx-`. Accent colors
 * flow through CSS vars (--ixfx-a / --ixfx-a2) set from the theme at mount.
 * -------------------------------------------------------------------------- */
const STYLE_ID = 'ixfx-styles';
const CSS = `
.ixfx-root{position:fixed;inset:0;z-index:6;pointer-events:none;overflow:hidden;
  contain:strict;--ixfx-a:${DEFAULT_ACCENT};--ixfx-a2:${DEFAULT_ACCENT2};}
.ixfx-flash{position:absolute;inset:0;background:radial-gradient(120% 120% at 50% 45%,
  #fff 0%,#ffd9f2 55%,#ffb3e6 100%);opacity:0;will-change:opacity;}
.ixfx-spiral{position:absolute;inset:-25%;opacity:0;will-change:transform,opacity;
  background:conic-gradient(from 0deg at 50% 50%,transparent 0deg,var(--ixfx-a) 45deg,
  transparent 90deg,var(--ixfx-a2) 200deg,transparent 250deg,#fff 320deg,transparent 360deg);}
.ixfx-chroma{position:absolute;inset:0;opacity:0;mix-blend-mode:screen;
  will-change:opacity,transform;}
.ixfx-sub{position:absolute;color:#fff;font-weight:700;letter-spacing:.06em;
  text-transform:lowercase;white-space:nowrap;opacity:0;will-change:opacity,transform;
  text-shadow:0 0 12px rgba(255,105,180,.5);mix-blend-mode:screen;}
.ixfx-bubble{position:absolute;border-radius:50%;
  background:radial-gradient(circle at 35% 30%,rgba(255,255,255,.9),
  rgba(255,105,180,.35) 55%,rgba(176,108,255,.15) 100%);
  box-shadow:0 0 14px rgba(255,105,180,.35);opacity:0;will-change:transform,opacity;}
.ixfx-particle{position:absolute;width:10px;height:10px;border-radius:50%;
  background:radial-gradient(circle at 40% 35%,#fff,var(--ixfx-a) 60%,var(--ixfx-a2) 100%);
  opacity:0;will-change:transform,opacity;}
.ixfx-drip{position:absolute;width:3px;border-radius:2px;opacity:0;
  background:linear-gradient(180deg,transparent,var(--ixfx-a) 55%,#fff 100%);
  will-change:transform,opacity;}
.ixfx-edge{position:absolute;inset:0;opacity:0;will-change:opacity;
  box-shadow:inset 0 0 120px 32px var(--ixfx-a);}
.ixfx-ring{position:absolute;border:2px solid var(--ixfx-a);border-radius:50%;
  width:22px;height:22px;opacity:0;will-change:transform,opacity;
  box-shadow:0 0 10px var(--ixfx-a);}
.ixfx-glint{position:absolute;width:16px;height:16px;opacity:0;
  background:radial-gradient(circle,#fff 0%,var(--ixfx-a) 45%,transparent 72%);
  will-change:transform,opacity;}
.ixfx-praise{position:absolute;left:50%;top:46%;transform:translate(-50%,-50%);
  color:#fff;font-weight:800;font-size:clamp(38px,8.5vw,88px);letter-spacing:.02em;
  text-transform:lowercase;opacity:0;will-change:opacity,transform;text-align:center;
  text-shadow:0 0 24px rgba(255,105,180,.8),0 0 60px rgba(176,108,255,.5);}
.ixfx-whisper{position:absolute;color:#fff;font-weight:700;letter-spacing:.05em;
  font-size:clamp(20px,3.2vw,30px);text-transform:lowercase;opacity:0;
  will-change:opacity,transform;text-shadow:0 0 14px rgba(255,105,180,.6);}
.ixfx-letter{display:inline-block;opacity:0;will-change:transform,opacity;}
.ixfx-gif{position:absolute;border-radius:12px;object-fit:cover;opacity:0;
  box-shadow:0 6px 30px rgba(0,0,0,.5),0 0 26px rgba(255,105,180,.35);
  will-change:transform,opacity;}
.ixfx-polaroid{position:absolute;background:#fff;padding:6px 6px 18px;border-radius:4px;
  box-shadow:0 8px 24px rgba(0,0,0,.45);opacity:0;will-change:transform,opacity;}
.ixfx-polaroid img{display:block;width:100%;height:100%;object-fit:cover;}
.ixfx-dim{position:absolute;inset:0;background:#000;opacity:0;will-change:opacity;}
.ixfx-shimmer{position:absolute;top:-20%;bottom:-20%;left:-60%;width:220%;opacity:0;
  background:linear-gradient(105deg,transparent 38%,rgba(255,226,160,.45) 46%,
  var(--ixfx-a) 50%,rgba(255,226,160,.45) 54%,transparent 62%);
  will-change:transform,opacity;}
.ixfx-streakm{position:absolute;left:50%;bottom:3.5%;transform:translateX(-50%);
  display:flex;gap:4px;opacity:.9;}
.ixfx-seg{width:16px;height:5px;border-radius:3px;background:rgba(255,255,255,.12);
  transition:background .25s,box-shadow .25s;}
.ixfx-seg.on{background:linear-gradient(90deg,var(--ixfx-a),var(--ixfx-a2));
  box-shadow:0 0 calc(4px + 10px * var(--ixfx-sglow,0)) var(--ixfx-a);}
/* ambient drifters live on <body> too (they must survive the stage wipe to
   finish a 14-26s crossing), so their z-index competes at the ROOT level: the
   layer is PREPENDED to <body> at the stage's own z-index (2), which keeps it
   above the tube canvas (0) and the readability scrim (body::after, 1) but —
   by DOM order — behind the stage, hud (3), aside (4) and everything above. */
.ixfx-amb-root{position:fixed;inset:0;z-index:2;pointer-events:none;overflow:hidden;}
.ixfx-amb{position:absolute;opacity:0;border-radius:12px;object-fit:cover;
  max-width:40vw;max-height:44vh;will-change:transform,opacity;filter:saturate(.85);}
/* garnish layer lives on <body> as well (fullscreen washes outlive the card),
   so its z-index competes at the ROOT level: same 5 as the burst root — above
   stage/hud/aside, below the shell overlay (6), the loader (10) and the
   jumpscare. DOM order keeps it under the burst gifs (see placeGarnish). */
.ixfx-gl{position:fixed;inset:0;z-index:5;pointer-events:none;overflow:hidden;
  --ixfx-a:${DEFAULT_ACCENT};--ixfx-a2:${DEFAULT_ACCENT2};}
.ixfx-gwash{position:absolute;inset:0;opacity:0;mix-blend-mode:screen;will-change:opacity;}
.ixfx-gdrain{position:absolute;inset:0;opacity:0;background-color:#0a0410;
  background-position:center;background-size:cover;background-repeat:no-repeat;
  background-blend-mode:luminosity;backdrop-filter:blur(7px);
  -webkit-backdrop-filter:blur(7px);will-change:opacity;}
.ixfx-gword{position:absolute;left:50%;top:46%;transform:translate(-50%,-50%);
  color:#fff;font-weight:800;font-size:clamp(48px,10vw,128px);letter-spacing:.18em;
  text-transform:lowercase;white-space:nowrap;opacity:0;filter:blur(1.2px);
  text-shadow:0 0 34px rgba(255,105,180,.5);}
.ixfx-gspiral{position:absolute;inset:0;width:100%;height:100%;opacity:0;will-change:opacity;}
/* burst layer lives on <body> (it must survive the stage wipe), so its z-index
   competes at the ROOT level: above the stage (2) / hud (3) / aside (4), below
   the shell overlay (6), the loader (10) and the jumpscare. */
.ixfx-burst-root{position:fixed;inset:0;z-index:5;pointer-events:none;overflow:hidden;}
.ixfx-burst{position:absolute;border-radius:14px;object-fit:cover;opacity:0;
  pointer-events:auto;cursor:grab;touch-action:none;user-select:none;-webkit-user-select:none;
  -webkit-user-drag:none;box-shadow:0 8px 34px rgba(0,0,0,.55),0 0 30px rgba(255,105,180,.4);
  will-change:transform,opacity;}
.ixfx-burst.ixfx-grabbing{cursor:grabbing;}
/* rain layer lives on <body> for the same reason as the burst layer (a 3s fall
   must outlive the stage wipe). Click-through and DOM-ordered UNDER the burst,
   so the foreground gif toy is always the thing your pointer finds. The fall is
   a CSS *animation*, not a WAAPI one, precisely because the pause menu holds CSS
   animations with animation-play-state (ui/pause.js) — a paused run's rain hangs
   in the air instead of finishing behind the menu. */
.ixfx-rain-root{position:fixed;inset:0;z-index:5;pointer-events:none;overflow:hidden;}
.ixfx-rain{position:absolute;top:-28vh;width:22vmin;border-radius:10px;opacity:0;
  object-fit:cover;box-shadow:0 6px 30px rgba(0,0,0,.45);
  filter:drop-shadow(0 0 14px rgba(255,105,180,.55));will-change:transform,opacity;
  animation:ixfx-rainfall var(--ixfx-fall,3s) linear forwards;}
@keyframes ixfx-rainfall{
  0%{transform:translateY(0) scale(.45);opacity:0;}
  12%{opacity:var(--ixfx-rain-a,.9);}
  90%{opacity:var(--ixfx-rain-a,.9);}
  100%{transform:translateY(150vh) scale(1);opacity:0;}}
@media (prefers-reduced-motion: reduce){
  .ixfx-sub,.ixfx-bubble,.ixfx-particle{transition:none;}
}`;

function ensureStyles() {
  if (typeof document === 'undefined') return;
  if (document.getElementById(STYLE_ID)) return;
  try {
    const s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent = CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) { /* non-fatal */ }
}

/* ----------------------------------------------------------------------------
 * BACKDROP-LIVE body-class contract (shared with beats.js — intentional
 * duplicate, no shared module). While ANY fullscreen visual garnish is up
 * (spiral / drain / pink wash), body carries `ix-backdrop-live` so in-run cards
 * thin out and the show behind them is visible. Ref-counted via a body dataset
 * counter so overlapping layers from BOTH files never stomp each other: the
 * class is present iff at least one layer is live. This file owns the counting
 * for ITS fullscreen garnishes AND the see-through-card CSS (styles.css); beats
 * owns its own layers' counting. Import-side-effect-free: only touches the DOM
 * when called at runtime, never at module load.
 * -------------------------------------------------------------------------- */
function backdropRef(on) {
  if (typeof document === 'undefined' || !document.body) return;
  const b = document.body;
  const n = Math.max(0, (parseInt(b.dataset.ixBackdrop || '0', 10) || 0) + (on ? 1 : -1));
  b.dataset.ixBackdrop = String(n);
  b.classList.toggle('ix-backdrop-live', n > 0);
}

/* ----------------------------------------------------------------------------
 * FACTORY
 * -------------------------------------------------------------------------- */
export function createEffects({ root, caps, media, theme } = {}) {
  const hasDOM = typeof document !== 'undefined' && !!root;
  const supportsAnim = hasDOM && typeof Element !== 'undefined' &&
    typeof Element.prototype.animate === 'function';

  /* Theme resolution: praise pool + accent colors degrade to the built-ins. */
  const praisePool = (theme && Array.isArray(theme.praise) && theme.praise.length)
    ? theme.praise.slice() : PRAISE_WORDS.slice();
  const accent  = (theme && typeof theme.accent  === 'string' && theme.accent)  || DEFAULT_ACCENT;
  const accent2 = (theme && typeof theme.accent2 === 'string' && theme.accent2) || DEFAULT_ACCENT2;

  /* Media manifest: plain URL string lists. LIVE REFERENCES, not copies — the host can
   * append remote stills to media.images after boot (web-shim's `assets-append`) and this
   * module is never re-handed a manifest, so holding the array itself is the only way the
   * growth reaches us. web-shim normalizes both keys to real arrays, and everything it
   * pushes is a validated non-empty url, so the old defensive filter has nothing left to
   * catch; the || [] below is for a hand-built manifest (harness, standalone). */
  const gifs = (media && Array.isArray(media.gifs)) ? media.gifs : [];
  const images = (media && Array.isArray(media.images)) ? media.images : [];
  /* Ambient drifters + braindrain washes draw from the whole visual manifest. A FUNCTION,
   * not a snapshot, for the same reason — concat() would freeze the pool at boot size. */
  const ambientPool = () => (images.length ? gifs.concat(images) : gifs);
  /* Garnish words: the user's ACTIVE subliminal phrases, else the praise pool. */
  const userSubliminals = (media && Array.isArray(media.subliminals))
    ? media.subliminals.filter((s) => typeof s === 'string' && s.trim().length > 0) : [];
  const garnishWords = userSubliminals.length ? userSubliminals : praisePool;

  /* Heavy variants degrade to gentle fades when the OS asks for reduced motion. */
  let reducedMotion = false;
  try {
    reducedMotion = typeof matchMedia === 'function' &&
      matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch (_e) {}

  let layer = null;         // .ixfx-root container
  let flashEl = null;       // the full-screen wash
  let mounted = false;

  // current visual params (from the last setDepth)
  let vis = channelsToVisual(clampToCaps(depthToChannels(0), capsOf()));
  let depthNow = 0;

  // rAF spawn driver
  let rafId = 0;
  let running = false;
  let lastFlash = 0, lastSub = 0, lastBubble = 0;

  // live spawned nodes (for teardown on recover(0))
  const live = new Set();
  let mediaLiveCount = 0;   // concurrent <img> nodes (cap: MAX_MEDIA_NODES)

  // per-kind no-repeat variant rotation
  const lastVariant = Object.create(null);

  // streak meter (persists across beats; removed by recover(0))
  let streakEl = null;
  let streakSegEls = [];
  let lastStreakShown = 0;

  // ambient layer (behind the card): setTimeout chain + its own node budget
  let ambRoot = null;
  let ambTimer = 0;
  let ambLive = 0;
  const ambNodes = new Set();

  // garnish layer (above the stage, below the interstitial overlay)
  let glRoot = null;
  let garnishNow = null;    // the single live garnish handle (one at a time)
  const garnishBag = createGarnishBag(); // the always-different rotation
  let inRecovery = false;   // recover() arms it; setDepth() (normal drive) clears

  // GifBurst layer (foreground toy — clickable / flingable). NOT a backdrop: it
  // never ref-counts backdropRef, so cards stay solid behind it. Parented to
  // <body>, NOT to the stage — see the GIFBURST section header.
  let burstRoot = null;      // .ixfx-burst-root container (own body-level layer)
  const burstItems = new Set(); // every live gif handle (a burst = N of them)
  let burstLiveCount = 0;    // concurrent burst <img> nodes (cap: GIFBURST_MAX_NODES)
  let lastBurstGif = null;   // no immediate repeat of the last spawned gif

  // GifRain layer (the DTRH cascade port). Like the burst it is body-parented and
  // has its own node budget; unlike the burst it is a SINGLETON downpour — a
  // second trigger extends the live window instead of starting a second rAF loop.
  let rainRoot = null;       // .ixfx-rain-root container
  let rainLiveCount = 0;     // concurrent falling <img> nodes (cap: GIFRAIN_MAX_NODES)
  let rainCancel = null;     // cancel handle for the one live downpour (null = dry)
  let rainEndAt = 0;         // spawn-window deadline (extended by a re-trigger)
  let rainDepth = 0;         // band depth the live downpour draws its opacity from
  let lastRainUrl = null;    // no immediate repeat of the last spawned drop

  // loom spiral machinery: module + ONE reusable field, all lazy. Params are
  // rolled FRESH per mount (freshSpiralParams) so no two spirals repeat.
  let loomMod = null, loomDead = false;
  let loomField = null, loomFieldFailed = false;
  let loomLogged = false;   // first-successful-frame diagnostic fired once

  function capsOf() { return caps || undefined; }
  function pickOf(arr) { return arr[(Math.random() * arr.length) | 0]; }
  function praisePhrase() { return pickOf(praisePool); }

  /** Diagnostic seam: the app has no devtools, but page logs reach C# via the
   *  shim. boot may or may not listen for 'intake-log' — harmless if unheard. */
  function logSeam(msg) {
    try {
      if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
        window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixfx: ' + msg } }));
      }
    } catch (_e) {}
  }

  /** Re-attach a layer the stage wipe orphaned. beats.js clears the shared stage
   *  (`stage.innerHTML = ''`) at the top of EVERY render, which detaches our
   *  layers along with the outgoing card; without this, everything mounted after
   *  the first beat would draw into a node that is no longer in the document.
   *  Cheap + guarded: a no-op while the layer is still attached. */
  function reattach(el) {
    if (el && !el.parentNode) { try { root.appendChild(el); } catch (_e) {} }
  }

  /** <body> host for the layers that must OUTLIVE the stage wipe: the ambient
   *  drifters, the fullscreen garnishes and the gif burst. Anything parented to
   *  the shared stage dies mid-life the moment the player answers (beats.js does
   *  `stage.innerHTML = ''` at the top of every render); on <body> those layers
   *  expire only on their own clocks. Falls back to `root` if the document has
   *  no body yet — cheap insurance, never happens in the hosted page. */
  function bodyHost() {
    return (typeof document !== 'undefined' && document.body) ? document.body : root;
  }

  function mount() {
    if (!hasDOM) return;
    if (mounted) { reattach(layer); return; }
    ensureStyles();
    try {
      layer = document.createElement('div');
      layer.className = 'ixfx-root';
      layer.setAttribute('aria-hidden', 'true');
      try {
        layer.style.setProperty('--ixfx-a', accent);
        layer.style.setProperty('--ixfx-a2', accent2);
      } catch (_e) {}
      flashEl = document.createElement('div');
      flashEl.className = 'ixfx-flash';
      layer.appendChild(flashEl);
      root.appendChild(layer);
      mounted = true;
    } catch (_e) { mounted = false; }
  }

  function track(el, removeAfterMs) {
    live.add(el);
    if (removeAfterMs != null) {
      setTimeout(() => { removeNode(el); }, removeAfterMs);
    }
  }
  function removeNode(el) {
    live.delete(el);
    if (el && el._ixMedia) { el._ixMedia = false; mediaLiveCount = Math.max(0, mediaLiveCount - 1); }
    // Burst gifs ride their OWN budget; freeing it here (not in the per-instance
    // cleanup) keeps recover(0)'s bulk clear exact and the marker makes a
    // double-remove idempotent, same contract as _ixMedia above.
    if (el && el._ixBurst) { el._ixBurst = false; burstLiveCount = Math.max(0, burstLiveCount - 1); }
    // ...and the rain rides a third budget, freed here on every removal path
    // (animationend, the safety-net timer, killRain, recover(0)'s bulk clear).
    if (el && el._ixRain) { el._ixRain = false; rainLiveCount = Math.max(0, rainLiveCount - 1); }
    // Backdrop bookkeeping: every removal path (garnishFade finish/cancel, spiral
    // stop, recover()/dispose bulk-clear) funnels through here, so one guarded
    // decrement per node keeps the shared ref-count exactly balanced. Clearing
    // the marker makes a double-remove idempotent (can't double-decrement).
    if (el && el._ixBackdrop) { el._ixBackdrop = false; backdropRef(false); }
    try { if (el && el.parentNode) el.parentNode.removeChild(el); } catch (_e) {}
  }
  /** Decrement the shared see-through-card ref-count the moment a fullscreen
   *  garnish's life ENDS (the start of its fade-out) rather than when the node
   *  is finally removed, so in-run cards resolidify in sync with the fade.
   *  Idempotent via the SAME _ixBackdrop marker removeNode clears, so the later
   *  removeNode can never double-decrement. */
  function releaseBackdrop(el) {
    if (el && el._ixBackdrop) { el._ixBackdrop = false; backdropRef(false); }
  }

  /* ----- rAF loop: ambient flashes / subliminals / bubbles by cadence ------- */
  function loop(now) {
    if (!running) return;
    if (vis.flashOn && now - lastFlash >= vis.flashMs) { lastFlash = now; ambientFlash(); }
    if (vis.subOn && now - lastSub >= vis.subMs) { lastSub = now; spawnSubliminal(); }
    if (vis.bubbleOn && now - lastBubble >= vis.bubbleMs) { lastBubble = now; spawnBubble(); }
    rafId = requestAnimationFrame(loop);
  }
  function startLoop() {
    if (running || !mounted) return;
    running = true;
    const t = (typeof performance !== 'undefined' ? performance.now() : Date.now());
    lastFlash = lastSub = lastBubble = t;
    rafId = requestAnimationFrame(loop);
  }
  function stopLoop() {
    running = false;
    if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
  }

  /* ----- primitive spawns --------------------------------------------------- */
  function pulseFlash(alpha, durMs) {
    if (!flashEl) return;
    if (supportsAnim) {
      try {
        flashEl.animate(
          [{ opacity: 0 }, { opacity: alpha, offset: 0.35 }, { opacity: 0 }],
          { duration: durMs, easing: 'ease-out' });
        return;
      } catch (_e) { /* fall through */ }
    }
    // Fallback: opacity blip via timeout.
    flashEl.style.opacity = String(alpha);
    setTimeout(() => { if (flashEl) flashEl.style.opacity = '0'; }, Math.max(40, durMs * 0.4));
  }
  function ambientFlash() { pulseFlash(vis.flashAlpha, 180); }

  function spawnSubliminal() {
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-sub';
      el.textContent = SUBLIMINAL_WORDS[(Math.random() * SUBLIMINAL_WORDS.length) | 0];
      el.style.left = (8 + Math.random() * 74) + '%';
      el.style.top = (12 + Math.random() * 66) + '%';
      el.style.fontSize = (24 + Math.random() * 44) + 'px';
      layer.appendChild(el);
      const peak = vis.subAlpha;
      const dur = 900 + Math.random() * 900;
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: 'scale(.9)' },
           { opacity: peak, offset: 0.4 },
           { opacity: 0, transform: 'scale(1.08)' }],
          { duration: dur, easing: 'ease-in-out' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 200);
      } else {
        el.style.opacity = String(peak);
        track(el, dur);
      }
    } catch (_e) {}
  }

  function spawnBubble() {
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-bubble';
      const size = 16 + Math.random() * 42;
      el.style.width = el.style.height = size + 'px';
      el.style.left = (Math.random() * 92) + '%';
      el.style.bottom = '-8%';
      layer.appendChild(el);
      const peak = vis.bubbleAlpha;
      const dur = 3200 + Math.random() * 2600;
      const drift = (Math.random() * 60 - 30);
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: 'translate(0,0) scale(.6)' },
           { opacity: peak, offset: 0.2 },
           { opacity: peak, offset: 0.8 },
           { opacity: 0, transform: `translate(${drift}px,-115vh) scale(1)` }],
          { duration: dur, easing: 'ease-in' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 200);
      } else {
        el.style.opacity = String(peak);
        track(el, dur);
      }
    } catch (_e) {}
  }

  /** One free-flying reward bubble with a custom start/end (variant helper). */
  function flyBubble({ leftPct, startTop, dx, dyVh, durMs, alpha, sizePx, delayMs = 0 }) {
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-bubble';
      const size = sizePx != null ? sizePx : (12 + Math.random() * 30);
      el.style.width = el.style.height = size + 'px';
      el.style.left = leftPct + '%';
      if (startTop != null) el.style.top = startTop; else el.style.bottom = '-6%';
      layer.appendChild(el);
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: 'translate(0,0) scale(.5)' },
           { opacity: alpha, offset: 0.18 },
           { opacity: alpha, offset: 0.75 },
           { opacity: 0, transform: `translate(${dx}px,${dyVh}vh) scale(1.05)` }],
          { duration: durMs, delay: delayMs, easing: 'ease-in', fill: 'backwards' });
        a.onfinish = () => removeNode(el);
        track(el, delayMs + durMs + 200);
      } else {
        el.style.opacity = String(alpha);
        track(el, delayMs + durMs);
      }
    } catch (_e) {}
  }

  function burst(spec, hue, alpha = 1, cxPct = null, cyPct = null) {
    if (!layer) return;
    try {
      const count = reducedMotion ? Math.min(spec.count, 6) : spec.count;
      const cx = cxPct != null ? cxPct : (40 + Math.random() * 20);
      const cy = cyPct != null ? cyPct : (38 + Math.random() * 24);
      for (let i = 0; i < count; i++) {
        const el = document.createElement('div');
        el.className = 'ixfx-particle';
        el.style.left = cx + '%';
        el.style.top = cy + '%';
        if (hue != null) el.style.filter = `hue-rotate(${hue}deg)`;
        layer.appendChild(el);
        const ang = Math.random() * Math.PI * 2;
        const dist = spec.spreadPx * (0.4 + Math.random() * 0.6);
        const dx = Math.cos(ang) * dist, dy = Math.sin(ang) * dist - dist * 0.3;
        if (supportsAnim) {
          const a = el.animate(
            [{ opacity: alpha, transform: 'translate(-50%,-50%) scale(1)' },
             { opacity: 0, transform: `translate(calc(-50% + ${dx}px),calc(-50% + ${dy}px)) scale(.3)` }],
            { duration: spec.durMs, easing: 'cubic-bezier(.2,.8,.3,1)' });
          a.onfinish = () => removeNode(el);
          track(el, spec.durMs + 120);
        } else {
          track(el, spec.durMs);
        }
      }
    } catch (_e) {}
  }

  /** Gold/accent gradient sweep across the stage (jackpot + near-miss tease). */
  function shimmerSweep(alpha, durMs) {
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-shimmer';
      layer.appendChild(el);
      if (supportsAnim && !reducedMotion) {
        const a = el.animate(
          [{ opacity: 0, transform: 'translateX(-28%)' },
           { opacity: alpha, offset: 0.25 },
           { opacity: alpha, offset: 0.75 },
           { opacity: 0, transform: 'translateX(28%)' }],
          { duration: durMs, easing: 'ease-in-out' });
        a.onfinish = () => removeNode(el);
        track(el, durMs + 200);
      } else if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0 }, { opacity: alpha * 0.7, offset: 0.4 }, { opacity: 0 }],
          { duration: durMs, easing: 'ease-in-out' });
        a.onfinish = () => removeNode(el);
        track(el, durMs + 200);
      } else {
        el.style.opacity = String(alpha * 0.6);
        track(el, durMs);
      }
    } catch (_e) {}
  }

  /* ----- media spawns (lazy, error-tolerant, capped) ------------------------ */

  /** Spawn one <img> gif node: scale-in at a random pos, hold, fade+shrink. */
  function spawnGifNode(url, { sizePx, holdMs, enterMs, exitMs, center = false, alpha = 1 }) {
    if (!layer || mediaLiveCount >= MAX_MEDIA_NODES) return;
    // Slow fade-in / gentle fade-out floors so gifs ease in, never snap in.
    enterMs = Math.max(enterMs || 0, GIF_FADE_IN_MS);
    exitMs  = Math.max(exitMs  || 0, GIF_FADE_OUT_MS);
    try {
      const el = document.createElement('img');
      el.className = 'ixfx-gif';
      el.decoding = 'async';
      el.setAttribute('aria-hidden', 'true');
      el._ixMedia = true;
      mediaLiveCount++;
      el.onerror = () => removeNode(el); // bad URL -> silently gone
      el.style.width = sizePx + 'px';
      el.style.maxWidth = '46vw';
      el.style.maxHeight = '46vh';
      const rot = center ? 0 : (Math.random() * 28 - 14);
      if (center) {
        el.style.left = '50%'; el.style.top = '44%';
      } else {
        el.style.left = (12 + Math.random() * 62) + '%';
        el.style.top  = (12 + Math.random() * 56) + '%';
      }
      el.src = url; // lazy: assigned only at spawn time
      layer.appendChild(el);
      const base = center ? 'translate(-50%,-50%)' : '';
      const total = enterMs + holdMs + exitMs;
      if (supportsAnim && !reducedMotion) {
        const a = el.animate(
          [{ opacity: 0, transform: `${base} rotate(${rot}deg) scale(.35)` },
           { opacity: alpha, offset: enterMs / total, transform: `${base} rotate(${rot}deg) scale(1.06)` },
           { opacity: alpha, offset: (enterMs + holdMs) / total, transform: `${base} rotate(${rot}deg) scale(1)` },
           { opacity: 0, transform: `${base} rotate(${rot}deg) scale(.5)` }],
          { duration: total, easing: 'ease-out' });
        a.onfinish = () => removeNode(el);
      } else if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: `${base} rotate(${rot}deg)` },
           { opacity: alpha, offset: 0.3, transform: `${base} rotate(${rot}deg)` },
           { opacity: 0, transform: `${base} rotate(${rot}deg)` }],
          { duration: total, easing: 'ease-in-out' });
        a.onfinish = () => removeNode(el);
      } else {
        el.style.opacity = String(alpha);
        el.style.transform = `${base} rotate(${rot}deg)`;
      }
      track(el, total + 300);
    } catch (_e) {}
  }

  /** Small polaroid-style still-image pop (sparingly, high-intensity variants). */
  function spawnPolaroid(url, intensity) {
    if (!layer || !url || mediaLiveCount >= MAX_MEDIA_NODES) return;
    try {
      const wrap = document.createElement('div');
      wrap.className = 'ixfx-polaroid';
      wrap._ixMedia = true;
      mediaLiveCount++;
      const w = Math.round(90 + clamp01(intensity) * 60);
      wrap.style.width = w + 'px';
      wrap.style.height = Math.round(w * 1.05) + 'px';
      wrap.style.left = (15 + Math.random() * 58) + '%';
      wrap.style.top  = (14 + Math.random() * 52) + '%';
      const img = document.createElement('img');
      img.decoding = 'async';
      img.onerror = () => removeNode(wrap);
      img.src = url;
      wrap.appendChild(img);
      layer.appendChild(wrap);
      const rot = Math.random() * 24 - 12;
      const hold = 700 + clamp01(intensity) * 500;
      const total = 260 + hold + 380;
      if (supportsAnim && !reducedMotion) {
        const a = wrap.animate(
          [{ opacity: 0, transform: `rotate(${rot}deg) scale(.5)` },
           { opacity: 1, offset: 260 / total, transform: `rotate(${rot}deg) scale(1.04)` },
           { opacity: 1, offset: (260 + hold) / total, transform: `rotate(${rot}deg) scale(1)` },
           { opacity: 0, transform: `rotate(${rot}deg) scale(.7)` }],
          { duration: total, easing: 'ease-out' });
        a.onfinish = () => removeNode(wrap);
      } else if (supportsAnim) {
        const a = wrap.animate(
          [{ opacity: 0 }, { opacity: 1, offset: 0.3 }, { opacity: 0 }],
          { duration: total, easing: 'ease-in-out' });
        a.onfinish = () => removeNode(wrap);
      } else {
        wrap.style.opacity = '1';
      }
      track(wrap, total + 300);
    } catch (_e) {}
  }

  /** Sparingly: maybe pop one polaroid inside a high-intensity Bubble/Drop. */
  function maybePolaroid(intensity) {
    if (!images.length || reducedMotion) return;
    if (clamp01(intensity) < 0.65) return;
    if (Math.random() > 0.4) return;
    // noteMedia rides the pick (core/mediaLog.js): the archive's "payloads
    // issued" list is built from exactly the media a run actually showed.
    spawnPolaroid(noteMedia(pickOf(images), 'image'), intensity);
  }

  /* ==========================================================================
   * VARIANT POOLS — 3-5 distinct looks per RewardKind, ordered subtle ->
   * spectacular so the intensity-weighted pick (pickVariantIndex) maps low pays
   * to quiet looks and big pays to spectacle. No-repeat-last per kind.
   * ========================================================================*/

  /* ----- Flash variants ----------------------------------------------------- */
  function flashWash(i) { // (a) radial wash — the original
    const s = rewardFlashSpec(i);
    pulseFlash(s.alpha, s.durMs);
  }
  function flashDouble(i) { // (b) double-pulse strobe
    const s = rewardFlashSpec(i);
    pulseFlash(s.alpha, Math.max(120, s.durMs * 0.55));
    setTimeout(() => { pulseFlash(s.alpha * 0.85, s.durMs * 0.7); }, 150);
  }
  function flashChroma(i) { // (c) chromatic split — brief RGB offset layers
    if (!layer) return;
    const s = rewardFlashSpec(i);
    const dur = s.durMs + 120;
    const off = 4 + i * 8;
    const layers = [
      { tint: 'rgba(255,64,64,.85)',  dx: -off, dy: 0 },
      { tint: 'rgba(64,255,128,.7)',  dx: off,  dy: 0 },
      { tint: 'rgba(96,128,255,.85)', dx: 0,    dy: off },
    ];
    try {
      for (const spec of layers) {
        const el = document.createElement('div');
        el.className = 'ixfx-chroma';
        el.style.background =
          `radial-gradient(120% 120% at 50% 45%,${spec.tint} 0%,transparent 68%)`;
        layer.appendChild(el);
        if (supportsAnim) {
          const a = el.animate(
            [{ opacity: 0, transform: 'translate(0,0)' },
             { opacity: s.alpha, offset: 0.3, transform: `translate(${spec.dx}px,${spec.dy}px)` },
             { opacity: 0, transform: 'translate(0,0)' }],
            { duration: dur, easing: 'ease-out' });
          a.onfinish = () => removeNode(el);
          track(el, dur + 150);
        } else {
          el.style.opacity = String(s.alpha * 0.7);
          track(el, dur);
        }
      }
    } catch (_e) {}
  }
  function flashSpiral(i) { // (d) conic-gradient sweep
    if (!layer) return;
    try {
      const s = rewardFlashSpec(i);
      const dur = 600 + i * 400;
      const el = document.createElement('div');
      el.className = 'ixfx-spiral';
      layer.appendChild(el);
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: 'rotate(0deg) scale(1)' },
           { opacity: s.alpha * 0.8, offset: 0.3 },
           { opacity: 0, transform: 'rotate(210deg) scale(1.18)' }],
          { duration: dur, easing: 'ease-out' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 150);
      } else {
        el.style.opacity = String(s.alpha * 0.5);
        track(el, dur);
      }
    } catch (_e) {}
  }

  /* ----- Bubble variants ---------------------------------------------------- */
  function bubbleBurst(i) { // (a) burst + one ambient — the original
    burst(rewardBurstSpec(i));
    spawnBubble();
  }
  function bubbleFountain(i) { // (b) fountain from the bottom
    const n = reducedMotion ? 5 : Math.round(6 + i * 10);
    const alpha = 0.30 + i * 0.45;
    for (let k = 0; k < n; k++) {
      flyBubble({
        leftPct: 42 + Math.random() * 16,
        startTop: null,
        dx: (Math.random() - 0.5) * 380,
        dyVh: -(55 + Math.random() * 45),
        durMs: 1100 + Math.random() * 800,
        alpha,
        delayMs: k * 60,
      });
    }
    maybePolaroid(i);
  }
  function bubbleRing(i) { // (c) ring expanding from center
    if (!layer) return;
    const n = reducedMotion ? 6 : Math.round(8 + i * 8);
    const radius = 120 + i * 220;
    const alpha = 0.35 + i * 0.45;
    try {
      for (let k = 0; k < n; k++) {
        const el = document.createElement('div');
        el.className = 'ixfx-bubble';
        const size = 12 + Math.random() * 22;
        el.style.width = el.style.height = size + 'px';
        el.style.left = '50%';
        el.style.top = '46%';
        layer.appendChild(el);
        const ang = (k / n) * Math.PI * 2 + Math.random() * 0.3;
        const dx = Math.cos(ang) * radius, dy = Math.sin(ang) * radius;
        const dur = 800 + Math.random() * 300;
        if (supportsAnim) {
          const a = el.animate(
            [{ opacity: 0, transform: 'translate(-50%,-50%) scale(.4)' },
             { opacity: alpha, offset: 0.25 },
             { opacity: 0, transform: `translate(calc(-50% + ${dx}px),calc(-50% + ${dy}px)) scale(1.1)` }],
            { duration: dur, easing: 'ease-out' });
          a.onfinish = () => removeNode(el);
          track(el, dur + 150);
        } else {
          el.style.opacity = String(alpha);
          track(el, dur);
        }
      }
    } catch (_e) {}
    maybePolaroid(i);
  }
  function bubbleRain(i) { // (d) bubble rain from the top
    const n = reducedMotion ? 5 : Math.round(7 + i * 9);
    const alpha = 0.28 + i * 0.42;
    for (let k = 0; k < n; k++) {
      flyBubble({
        leftPct: 4 + Math.random() * 90,
        startTop: '-6%',
        dx: (Math.random() - 0.5) * 90,
        dyVh: 60 + Math.random() * 55,
        durMs: 1400 + Math.random() * 1000,
        alpha,
        delayMs: Math.random() * 320,
      });
    }
  }

  /* ----- Drop variants ------------------------------------------------------ */
  function gifBurst(i) { // media path: 2-4 gifs scale-in / hold / fade-shrink
    if (!gifs.length) { dropShower(i); return; }
    const spec = gifBurstSpec(i);
    for (let k = 0; k < spec.count; k++) {
      spawnGifNode(noteMedia(pickOf(gifs), 'gif'), {
        sizePx: Math.round(spec.sizePx * (0.8 + Math.random() * 0.4)),
        holdMs: spec.holdMs + Math.round(Math.random() * 200),
        enterMs: spec.enterMs,
        exitMs: spec.exitMs,
      });
    }
    maybePolaroid(i);
  }
  function dropShower(i) { // (a) particle shower — the original stand-in
    burst(rewardBurstSpec(i), 40);
    maybePolaroid(i);
  }
  function dropStreaks(i) { // (b) heavy droplet streaks falling through
    if (!layer) return;
    const n = reducedMotion ? 6 : Math.round(10 + i * 16);
    const alpha = 0.35 + i * 0.45;
    try {
      for (let k = 0; k < n; k++) {
        const el = document.createElement('div');
        el.className = 'ixfx-drip';
        el.style.height = (40 + Math.random() * 70) + 'px';
        el.style.left = (Math.random() * 98) + '%';
        el.style.top = '-12%';
        layer.appendChild(el);
        const dur = 520 + Math.random() * 420;
        const delay = Math.random() * 260;
        if (supportsAnim) {
          const a = el.animate(
            [{ opacity: 0, transform: 'translateY(0)' },
             { opacity: alpha, offset: 0.15 },
             { opacity: alpha, offset: 0.8 },
             { opacity: 0, transform: 'translateY(122vh)' }],
            { duration: dur, delay, easing: 'ease-in', fill: 'backwards' });
          a.onfinish = () => removeNode(el);
          track(el, delay + dur + 150);
        } else {
          el.style.opacity = String(alpha);
          track(el, delay + dur);
        }
      }
    } catch (_e) {}
  }
  function dropEdgeGlow(i) { // (c) screen-edge glow surge
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-edge';
      layer.appendChild(el);
      const peak = 0.30 + i * 0.45;
      const dur = 750 + i * 550;
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0 }, { opacity: peak, offset: 0.25 },
           { opacity: peak * 0.5, offset: 0.55 },
           { opacity: peak * 0.85, offset: 0.72 }, { opacity: 0 }],
          { duration: dur, easing: 'ease-in-out' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 150);
      } else {
        el.style.opacity = String(peak * 0.6);
        track(el, dur);
      }
    } catch (_e) {}
  }

  /* ----- Praise variants ---------------------------------------------------- */
  function praiseBig(i, phrase) { // (a) big phrase — original, pool-fed
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-praise';
      el.textContent = phrase != null ? phrase : praisePhrase();
      layer.appendChild(el);
      const peak = 0.35 + clamp01(i) * 0.6;
      const dur = 900 + clamp01(i) * 700;
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: 'translate(-50%,-40%) scale(.8)' },
           { opacity: peak, offset: 0.35, transform: 'translate(-50%,-52%) scale(1.05)' },
           { opacity: 0, transform: 'translate(-50%,-64%) scale(1.15)' }],
          { duration: dur, easing: 'ease-out' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 150);
      } else {
        el.style.opacity = String(peak);
        track(el, dur);
      }
    } catch (_e) {}
  }
  function praiseEcho(i) { // (b) stacked echo — same phrase 3x offset/faded
    if (!layer) return;
    const phrase = praisePhrase();
    const peak = 0.35 + clamp01(i) * 0.6;
    const dur = 1000 + clamp01(i) * 700;
    const echoes = [
      { dx: 0,   dy: 0,  scale: 1.0,  a: peak,        delay: 0 },
      { dx: -14, dy: 12, scale: 0.96, a: peak * 0.55, delay: 90 },
      { dx: 14,  dy: -12, scale: 0.92, a: peak * 0.35, delay: 180 },
    ];
    try {
      for (const e of echoes) {
        const el = document.createElement('div');
        el.className = 'ixfx-praise';
        el.textContent = phrase;
        layer.appendChild(el);
        if (supportsAnim) {
          const a = el.animate(
            [{ opacity: 0, transform: `translate(calc(-50% + ${e.dx}px),calc(-42% + ${e.dy}px)) scale(${e.scale * 0.85})` },
             { opacity: e.a, offset: 0.35, transform: `translate(calc(-50% + ${e.dx}px),calc(-52% + ${e.dy}px)) scale(${e.scale})` },
             { opacity: 0, transform: `translate(calc(-50% + ${e.dx}px),calc(-62% + ${e.dy}px)) scale(${e.scale * 1.1})` }],
            { duration: dur, delay: e.delay, easing: 'ease-out', fill: 'backwards' });
          a.onfinish = () => removeNode(el);
          track(el, e.delay + dur + 150);
        } else {
          el.style.opacity = String(e.a);
          track(el, e.delay + dur);
        }
      }
    } catch (_e) {}
  }
  function praiseCascade(i) { // (c) letter-cascade — letters drop in
    if (!layer) return;
    try {
      const phrase = praisePhrase();
      const el = document.createElement('div');
      el.className = 'ixfx-praise';
      el.style.opacity = '1'; // container static; letters animate individually
      layer.appendChild(el);
      const peak = 0.4 + clamp01(i) * 0.55;
      const stepMs = 45;
      const chars = Array.from(phrase);
      const settleMs = 380;
      const holdMs = 550 + clamp01(i) * 450;
      const fadeMs = 420;
      const total = settleMs + chars.length * stepMs + holdMs + fadeMs;
      chars.forEach((ch, idx) => {
        const span = document.createElement('span');
        span.className = 'ixfx-letter';
        span.textContent = ch === ' ' ? ' ' : ch;
        el.appendChild(span);
        if (supportsAnim && !reducedMotion) {
          span.animate(
            [{ opacity: 0, transform: 'translateY(-42px)' },
             { opacity: peak, transform: 'translateY(4px)', offset: 0.7 },
             { opacity: peak, transform: 'translateY(0)' }],
            { duration: settleMs, delay: idx * stepMs, easing: 'cubic-bezier(.3,1.4,.5,1)', fill: 'both' });
        } else if (supportsAnim) {
          span.animate(
            [{ opacity: 0 }, { opacity: peak }],
            { duration: settleMs, delay: idx * stepMs, easing: 'ease-out', fill: 'both' });
        } else {
          span.style.opacity = String(peak);
        }
      });
      if (supportsAnim) {
        const fade = el.animate(
          [{ opacity: 1 }, { opacity: 1, offset: (total - fadeMs) / total }, { opacity: 0 }],
          { duration: total, easing: 'ease-in' });
        fade.onfinish = () => removeNode(el);
      }
      track(el, total + 200);
    } catch (_e) {}
  }
  function praiseWhisper(i) { // (d) whisper-corner — small, intimate
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-whisper';
      el.textContent = praisePhrase();
      const corner = (Math.random() * 4) | 0;
      const inset = (4 + Math.random() * 5) + '%';
      const vInset = (8 + Math.random() * 7) + '%';
      if (corner & 1) el.style.right = inset; else el.style.left = inset;
      if (corner & 2) el.style.bottom = vInset; else el.style.top = vInset;
      layer.appendChild(el);
      const peak = 0.45 + clamp01(i) * 0.35;
      const dur = 1400 + clamp01(i) * 700;
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: 'translateY(6px)' },
           { opacity: peak, offset: 0.3, transform: 'translateY(0)' },
           { opacity: peak, offset: 0.75 },
           { opacity: 0, transform: 'translateY(-6px)' }],
          { duration: dur, easing: 'ease-in-out' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 150);
      } else {
        el.style.opacity = String(peak);
        track(el, dur);
      }
    } catch (_e) {}
  }

  /* ----- Chime variants ----------------------------------------------------- */
  function chimeSparkle(i) { // (a) small sparkle — the original
    burst({ count: Math.round(3 + i * 5), spreadPx: 40 + i * 70, durMs: 480 });
  }
  function chimeRipple(i) { // (b) tiny ripple ring
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-ring';
      el.style.left = (38 + Math.random() * 24) + '%';
      el.style.top = (36 + Math.random() * 24) + '%';
      layer.appendChild(el);
      const scale = 5 + i * 8;
      const dur = 620;
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0.55, transform: 'translate(-50%,-50%) scale(1)' },
           { opacity: 0, transform: `translate(-50%,-50%) scale(${scale})` }],
          { duration: dur, easing: 'ease-out' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 120);
      } else {
        el.style.opacity = '0.4';
        track(el, dur);
      }
    } catch (_e) {}
  }
  function chimeGlint(i) { // (c) corner glint
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-glint';
      const corner = (Math.random() * 4) | 0;
      const inset = (6 + Math.random() * 8) + '%';
      const vInset = (8 + Math.random() * 10) + '%';
      if (corner & 1) el.style.right = inset; else el.style.left = inset;
      if (corner & 2) el.style.bottom = vInset; else el.style.top = vInset;
      layer.appendChild(el);
      const peak = 0.6 + i * 0.4;
      const dur = 520;
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: 'scale(0) rotate(0deg)' },
           { opacity: peak, offset: 0.4, transform: 'scale(1.4) rotate(45deg)' },
           { opacity: 0, transform: 'scale(.2) rotate(90deg)' }],
          { duration: dur, easing: 'ease-out' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 120);
      } else {
        el.style.opacity = String(peak * 0.6);
        track(el, dur);
      }
    } catch (_e) {}
  }

  /* Pools ordered subtle -> spectacular (the weight window rides intensity). */
  const FLASH_VARIANTS  = [flashWash, flashDouble, flashChroma, flashSpiral];
  const BUBBLE_VARIANTS = [bubbleBurst, bubbleRain, bubbleFountain, bubbleRing];
  const DROP_VARIANTS   = [dropShower, dropEdgeGlow, dropStreaks]; // no-media path
  const PRAISE_VARIANTS = [praiseWhisper, praiseBig, praiseEcho, praiseCascade];
  const CHIME_VARIANTS  = [chimeSparkle, chimeRipple, chimeGlint];

  function runVariant(kindKey, pool, intensity) {
    // reduced motion: always take the gentlest look in the pool
    const idx = reducedMotion
      ? 0
      : pickVariantIndex(pool.length, kindKey in lastVariant ? lastVariant[kindKey] : -1, intensity);
    lastVariant[kindKey] = idx;
    try { pool[idx](intensity); } catch (_e) {}
  }

  /* ==========================================================================
   * JACKPOT CEREMONY + NEAR-MISS TEASE  (RewardEvent.jackpot / .nearMiss)
   * ========================================================================*/

  /** Full ceremony: ~250ms anticipation dim, then a screen-wide cascade —
   *  bursts + bubbles + (media) gif spotlight + praise phrase + gold shimmer. */
  function jackpotCeremony(i) {
    if (!layer) return;
    if (reducedMotion) { // gentle: soft wash + praise fade, no strobe/cascade
      flashWash(i);
      praiseBig(i);
      shimmerSweep(0.15, 1200);
      return;
    }
    const spec = jackpotSpec(i);
    try { // 1) anticipation beat: brief dim
      const dim = document.createElement('div');
      dim.className = 'ixfx-dim';
      layer.appendChild(dim);
      if (supportsAnim) {
        const a = dim.animate(
          [{ opacity: 0 }, { opacity: 0.5, offset: 0.45 }, { opacity: 0 }],
          { duration: spec.dimMs + 140, easing: 'ease-in-out' });
        a.onfinish = () => removeNode(dim);
      }
      track(dim, spec.dimMs + 350);
    } catch (_e) {}
    // 2) the cascade lands right as the dim releases
    setTimeout(() => {
      if (!layer) return; // recover(0) may have torn us down mid-beat
      try {
        shimmerSweep(0.30 + i * 0.40, spec.shimmerMs);
        pulseFlash(rewardFlashSpec(i).alpha, 480);
        for (let b = 0; b < spec.bursts; b++) {
          setTimeout(() => {
            burst({
              count: spec.particlesPerBurst,
              spreadPx: 160 + Math.random() * 220,
              durMs: 900 + Math.random() * 500,
            }, null, 1, 10 + Math.random() * 80, 15 + Math.random() * 60);
          }, b * 140);
        }
        for (let k = 0; k < spec.bubbles; k++) {
          flyBubble({
            leftPct: 4 + Math.random() * 90,
            startTop: null,
            dx: (Math.random() - 0.5) * 200,
            dyVh: -(60 + Math.random() * 50),
            durMs: 1400 + Math.random() * 1000,
            alpha: 0.35 + i * 0.4,
            delayMs: k * 70,
          });
        }
        if (gifs.length) {
          spawnGifNode(noteMedia(pickOf(gifs), 'gif'), {
            sizePx: Math.round(240 + i * 160),
            holdMs: spec.spotlightMs,
            enterMs: 260,
            exitMs: 340,
            center: true,
          });
        }
        // "perfect response"-style phrase comes from the theme's praise pool
        praiseBig(Math.min(1, i * 1.1 + 0.1), praisePhrase());
      } catch (_e) {}
    }, spec.dimMs);
  }

  /** Near-miss (fire=false): a faint shimmer sweep + a barely-there particle sigh. */
  function nearMissTease(i) {
    const spec = nearMissSpec(i);
    shimmerSweep(spec.alpha, spec.durMs);
    burst(
      { count: spec.particles, spreadPx: 50, durMs: spec.durMs + 220 },
      null, 0.25, 44 + Math.random() * 12, 58 + Math.random() * 10);
  }

  /* ==========================================================================
   * STREAK METER — persistent slim segment bar (bottom-center). Lives across
   * beats via module state; hidden below streak 2; shatters on a reset from a
   * streak >= 3; recover(0) removes it entirely (invariant #3).
   * ========================================================================*/

  function ensureStreakMeter() {
    if (streakEl || !layer) return;
    try {
      streakEl = document.createElement('div');
      streakEl.className = 'ixfx-streakm';
      streakEl.setAttribute('aria-hidden', 'true');
      streakSegEls = [];
      for (let k = 0; k < STREAK_SEGMENTS; k++) {
        const seg = document.createElement('div');
        seg.className = 'ixfx-seg';
        streakEl.appendChild(seg);
        streakSegEls.push(seg);
      }
      layer.appendChild(streakEl);
    } catch (_e) { streakEl = null; streakSegEls = []; }
  }
  function removeStreakMeter() {
    const el = streakEl;
    streakEl = null; streakSegEls = [];
    if (!el) return;
    try { if (el.parentNode) el.parentNode.removeChild(el); } catch (_e) {}
  }
  /** The break moment: lit segments fling apart + fade, then the bar goes. */
  function shatterStreakMeter() {
    const el = streakEl;
    const segs = streakSegEls;
    streakEl = null; streakSegEls = [];
    if (!el) return;
    if (!supportsAnim || reducedMotion) { // gentle fade instead of the fling
      try {
        el.style.transition = 'opacity .45s ease-out';
        el.style.opacity = '0';
      } catch (_e) {}
      setTimeout(() => { try { if (el.parentNode) el.parentNode.removeChild(el); } catch (_e) {} }, 500);
      return;
    }
    try {
      for (const seg of segs) {
        const dx = (Math.random() - 0.5) * 150;
        const dy = 30 + Math.random() * 90;
        const rot = (Math.random() - 0.5) * 220;
        seg.animate(
          [{ opacity: 1, transform: 'translate(0,0) rotate(0deg)' },
           { opacity: 0, transform: `translate(${dx}px,${dy}px) rotate(${rot}deg)` }],
          { duration: 460 + Math.random() * 260, easing: 'cubic-bezier(.3,.7,.4,1)', fill: 'forwards' });
      }
      setTimeout(() => { try { if (el.parentNode) el.parentNode.removeChild(el); } catch (_e) {} }, 800);
    } catch (_e) {
      try { if (el.parentNode) el.parentNode.removeChild(el); } catch (_e2) {}
    }
  }

  /** Drive the meter from RewardEvent.streak. Runs even on fire=false beats. */
  function updateStreak(streak) {
    if (!hasDOM) return;
    const s = Math.max(0, streak | 0);
    // invariant #2: a zeroed master cap means NOTHING shows, HUD included.
    if (clampIntensity(1, capsOf()) <= 0.0005) {
      removeStreakMeter();
      lastStreakShown = s;
      return;
    }
    const spec = streakMeterSpec(s);
    if (!spec.visible) {
      if (s === 0 && lastStreakShown >= 3 && streakEl) shatterStreakMeter();
      else removeStreakMeter();
      lastStreakShown = s;
      return;
    }
    mount();
    ensureStreakMeter();
    if (streakEl) {
      try {
        streakEl.style.setProperty('--ixfx-sglow', String(spec.glow));
        for (let k = 0; k < streakSegEls.length; k++) {
          streakSegEls[k].classList.toggle('on', k < spec.lit);
        }
      } catch (_e) {}
    }
    lastStreakShown = s;
  }

  /* ==========================================================================
   * AMBIENT ASSET LAYER — a GIF/still occasionally DRIFTS across the viewport
   * or GHOSTS in and out, ghost-faint, on its own layer BEHIND the question
   * card. Depth-gated cadence (ambientSpec), max 2 concurrent, lazy <img> with
   * onerror cleanup. Fully off: below depth ~0.2 / reduced motion / no media /
   * Recovery. Killed by recover() and dispose().
   *
   * OUTLIVES THE CARD: like the burst layer, this one is parented to <body>, NOT
   * to the stage — a DRIFT is a 14-26s edge-to-edge crossing and the stage wipe
   * (`stage.innerHTML = ''`, beats.js render) used to cut it dead the instant the
   * player answered. On <body> a drifter finishes its travel off-screen and then
   * removes itself on its own animation/safety clock.
   * ========================================================================*/

  function mountAmbient() {
    if (!hasDOM) return;
    const host = bodyHost();
    // PREPENDED, not appended: at the same z-index as .intake-stage (2), DOM
    // order decides, so a body-first layer paints BEHIND the card while still
    // sitting above the tube canvas (z0) and the readability scrim (body::after
    // z1) — exactly where the drifters drew as a stage child.
    if (ambRoot) {
      if (!ambRoot.parentNode) {              // paranoia: host swap / manual wipe
        try { host.insertBefore(ambRoot, host.firstChild); } catch (_e) {}
      }
      return;
    }
    ensureStyles();
    try {
      ambRoot = document.createElement('div');
      ambRoot.className = 'ixfx-amb-root';
      ambRoot.setAttribute('aria-hidden', 'true');
      host.insertBefore(ambRoot, host.firstChild);
    } catch (_e) { ambRoot = null; }
  }
  function removeAmbientNode(el) {
    if (ambNodes.delete(el)) ambLive = Math.max(0, ambLive - 1);
    try { if (el && el.parentNode) el.parentNode.removeChild(el); } catch (_e) {}
  }
  function killAmbient() {
    if (ambTimer) { clearTimeout(ambTimer); ambTimer = 0; }
    for (const el of Array.from(ambNodes)) removeAmbientNode(el);
    ambLive = 0;
    if (ambRoot) { try { if (ambRoot.parentNode) ambRoot.parentNode.removeChild(ambRoot); } catch (_e) {} }
    ambRoot = null;
  }

  /** One drifter: DRIFT (edge-to-edge crossing, 14-26s, gentle rotation/scale)
   *  or GHOST (fade in at a random spot, hold 4-8s, fade out). */
  function spawnAmbient() {
    if (!hasDOM || reducedMotion || !supportsAnim || !ambientPool().length) return;
    if (ambLive >= MAX_AMBIENT || inRecovery) return;
    const spec = ambientSpec(depthNow);
    if (!spec.on) return;
    const alpha = spec.opacity * clampIntensity(1, capsOf()); // visual cap on top
    if (alpha <= 0.005) return;
    mountAmbient();
    if (!ambRoot) return;
    try {
      const el = document.createElement('img');
      el.className = 'ixfx-amb';
      el.decoding = 'async';
      el.loading = 'lazy';
      el.setAttribute('aria-hidden', 'true');
      el.onerror = () => removeAmbientNode(el); // bad URL -> silently gone
      el.style.width = (18 + Math.random() * 16) + 'vmin';
      el.src = pickOf(ambientPool()); // lazy: assigned only at spawn time
      ambRoot.appendChild(el);
      ambNodes.add(el);
      ambLive++;
      if (Math.random() < 0.5) { // DRIFT: enter one edge, cross, exit the other
        const ltr = Math.random() < 0.5;
        el.style.left = ltr ? '-32vw' : '104vw';
        el.style.top = (8 + Math.random() * 60) + '%';
        const dur = 14000 + Math.random() * 12000;
        const dx = (ltr ? 1 : -1) * 150;
        const dy = (Math.random() - 0.5) * 18;
        const rot = (Math.random() - 0.5) * 16;
        const a = el.animate(
          [{ opacity: 0, transform: 'translate(0,0) rotate(0deg) scale(.92)' },
           { opacity: alpha, offset: 0.12 },
           { opacity: alpha, offset: 0.85 },
           { opacity: 0, transform: `translate(${dx}vw,${dy}vh) rotate(${rot}deg) scale(1.08)` }],
          { duration: dur, easing: 'linear' });
        a.onfinish = () => removeAmbientNode(el);
        setTimeout(() => removeAmbientNode(el), dur + 500); // safety net
      } else { // GHOST: fade in, hold, fade out in place
        el.style.left = (8 + Math.random() * 58) + '%';
        el.style.top = (10 + Math.random() * 55) + '%';
        const fade = 1400;
        const hold = 4000 + Math.random() * 4000;
        const dur = fade + hold + fade;
        const a = el.animate(
          [{ opacity: 0, transform: 'scale(.97)' },
           { opacity: alpha, offset: fade / dur },
           { opacity: alpha, offset: (fade + hold) / dur },
           { opacity: 0, transform: 'scale(1.05)' }],
          { duration: dur, easing: 'ease-in-out' });
        a.onfinish = () => removeAmbientNode(el);
        setTimeout(() => removeAmbientNode(el), dur + 500);
      }
    } catch (_e) {}
  }

  /** Self-rescheduling timer chain. Each fire re-checks eligibility, so the
   *  chain dies quietly when depth sinks below the gate and setDepth restarts
   *  it when the descent resumes. */
  function scheduleAmbient() {
    if (!hasDOM || reducedMotion || !ambientPool().length) return;
    if (ambTimer || inRecovery) return;
    const spec = ambientSpec(depthNow, Math.random());
    if (!spec.on) return;
    ambTimer = setTimeout(() => {
      ambTimer = 0;
      spawnAmbient();
      scheduleAmbient();
    }, spec.intervalMs);
  }

  /* ==========================================================================
   * BIG-REWARD GARNISHES — fullscreen pairings a fired reward sometimes earns:
   * pink wash / braindrain / subliminal word flashes / live LOOM SPIRAL. One
   * at a time (a new one fast-fades the old), rolled by pickGarnish, all on
   * the z-5 garnish layer (above the stage/hud/aside, below the shell overlay
   * z6, the loader z10 and the jumpscare).
   *
   * OUTLIVES THE CARD: parented to <body>, NOT to the stage — same reason as the
   * burst layer. A garnish owns a multi-second timed life (garnishFade / the
   * spiral's own rAF+timer) and the stage wipe used to kill the fullscreen wash
   * mid-fade the instant the beat resolved. The layer is click-through
   * (pointer-events:none in CSS), so surviving the swap can never eat a tap
   * meant for the next card. It is inserted BEFORE the burst root when that
   * exists so the backdrop-ish garnishes stay under the foreground gif toy
   * (both are z5; DOM order breaks the tie deterministically).
   * ========================================================================*/

  /** Put glRoot in the body, under the burst layer when that is already up. */
  function placeGarnish(host) {
    if (burstRoot && burstRoot.parentNode === host) host.insertBefore(glRoot, burstRoot);
    else host.appendChild(glRoot);
  }
  function mountGarnish() {
    if (!hasDOM) return;
    const host = bodyHost();
    if (glRoot) {
      if (!glRoot.parentNode) { try { placeGarnish(host); } catch (_e) {} }
      return;
    }
    ensureStyles();
    try {
      glRoot = document.createElement('div');
      glRoot.className = 'ixfx-gl';
      glRoot.setAttribute('aria-hidden', 'true');
      try {
        glRoot.style.setProperty('--ixfx-a', accent);
        glRoot.style.setProperty('--ixfx-a2', accent2);
      } catch (_e) {}
      placeGarnish(host);
    } catch (_e) { glRoot = null; }
  }
  function endGarnish(handle) { if (garnishNow === handle) garnishNow = null; }
  function preemptGarnish() {
    const g = garnishNow;
    garnishNow = null;
    if (g) { try { g.cancel(); } catch (_e) {} }
  }
  function removeGarnishLayer() {
    preemptGarnish();
    if (glRoot) { try { if (glRoot.parentNode) glRoot.parentNode.removeChild(glRoot); } catch (_e) {} }
    glRoot = null;
  }

  /** Shared wash lifecycle: SLOW fade in (~1.6s) / hold / gentle fade out
   *  (~0.9s) over durMs — no hard pop in either direction. Fades are clamped to
   *  the layer's own lifetime (they can't exceed 90% of durMs) so a short wash
   *  still gets a real, proportional fade. backdropRef is released at the START
   *  of the fade-out (via a timer + releaseBackdrop) so cards resolidify in sync
   *  with the fade, not after the node is gone. The returned handle's cancel()
   *  fast-fades (~250ms) and releases backdrop immediately — the preemption path. */
  function garnishFade(el, alpha, durMs) {
    let fin = GARNISH_FADE_IN_MS, fout = GARNISH_FADE_OUT_MS;
    if (fin + fout > durMs * 0.9) {           // keep a sliver of hold on short washes
      const k = (durMs * 0.9) / (fin + fout);
      fin *= k; fout *= k;
    }
    const inOff  = clamp01(fin / durMs);
    const outOff = clamp01(1 - fout / durMs);
    let anim = null, safety = 0, backdropTimer = 0;
    const handle = {
      cancel: () => {
        clearTimeout(safety); clearTimeout(backdropTimer);
        releaseBackdrop(el);                  // life ends now -> cards resolidify
        try { if (anim) anim.cancel(); } catch (_e) {}
        if (supportsAnim) {
          try {
            const a2 = el.animate(
              [{ opacity: alpha * 0.8 }, { opacity: 0 }],
              { duration: GARNISH_PREEMPT_MS, easing: 'ease-out', fill: 'forwards' });
            a2.onfinish = () => removeNode(el);
          } catch (_e) {}
          setTimeout(() => removeNode(el), GARNISH_PREEMPT_MS + 80);
        } else removeNode(el);
      },
    };
    const finish = () => {
      clearTimeout(safety); clearTimeout(backdropTimer);
      releaseBackdrop(el); removeNode(el); endGarnish(handle);
    };
    if (supportsAnim) {
      anim = el.animate(
        [{ opacity: 0 }, { opacity: alpha, offset: inOff },
         { opacity: alpha, offset: outOff }, { opacity: 0 }],
        { duration: durMs, easing: 'ease-in-out' });
      anim.onfinish = finish;
      safety = setTimeout(finish, durMs + 400);
      // release the see-through-card ref-count at the START of the fade-out
      backdropTimer = setTimeout(() => releaseBackdrop(el), Math.max(0, durMs - fout));
    } else {
      el.style.opacity = String(alpha);
      safety = setTimeout(finish, durMs);
    }
    return handle;
  }

  /** PINK WASH — the sf-pfx-pink look: fullscreen accent tint pulse, screen blend. */
  function showPinkWash(i) {
    mountGarnish();
    if (!glRoot) return null;
    try {
      const spec = pinkWashSpec(i);
      const el = document.createElement('div');
      el.className = 'ixfx-gwash';
      el.style.background =
        `radial-gradient(circle at 50% 45%,${accent} 0%,rgba(255,20,147,.85) 100%)`;
      glRoot.appendChild(el);
      track(el);
      el._ixBackdrop = true; backdropRef(true); // fullscreen wash: cards go see-through
      sfxCue('pink-wash', i); // soft rosy-bloom cue at wash mount
      return garnishFade(el, spec.alpha * clampIntensity(1, capsOf()), spec.durMs);
    } catch (_e) { return null; }
  }

  /** BRAINDRAIN — the sf-pfx-drain look: fullscreen dim + backdrop blur + a
   *  faint random image wash kept subtle by the dark luminosity blend ("drained,
   *  not slideshow"). Works imageless too (pure dim + blur). */
  function showDrain(i) {
    mountGarnish();
    if (!glRoot) return null;
    try {
      const spec = drainWashSpec(i);
      const el = document.createElement('div');
      el.className = 'ixfx-gdrain';
      if (ambientPool().length) {
        try { el.style.backgroundImage = `url("${pickOf(ambientPool())}")`; } catch (_e) {}
      }
      glRoot.appendChild(el);
      track(el);
      el._ixBackdrop = true; backdropRef(true); // fullscreen dim+blur: cards go see-through
      sfxCue('drain-wash', i); // hollow vacuum drone at braindrain mount
      return garnishFade(el, spec.alpha * clampIntensity(1, capsOf()), spec.durMs);
    } catch (_e) { return null; }
  }

  /** SUBLIMINAL FLASHES — 2-4 rapid blinks of a big centered faded word from
   *  the user's subliminal phrases (else theme.praise). Hard on/off blinks —
   *  a fade would read as a title card, not a subliminal. */
  function showSublimFlashes(i) {
    mountGarnish();
    if (!glRoot) return null;
    const spec = sublimFlashSpec(i, Math.random());
    const alpha = spec.alpha * clampIntensity(1, capsOf());
    const timers = [];
    let cur = null;
    const handle = {
      cancel: () => {
        for (const t of timers) clearTimeout(t);
        timers.length = 0;
        if (cur) { removeNode(cur); cur = null; }
      },
    };
    for (let k = 0; k < spec.flashes; k++) {
      timers.push(setTimeout(() => {
        if (!glRoot || inRecovery) return;
        try {
          const el = document.createElement('div');
          el.className = 'ixfx-gword';
          el.textContent = pickOf(garnishWords);
          el.style.opacity = String(alpha);
          glRoot.appendChild(el);
          track(el, spec.onMs + 250);
          cur = el;
          timers.push(setTimeout(() => { if (cur === el) cur = null; removeNode(el); }, spec.onMs));
        } catch (_e) {}
      }, k * spec.gapMs));
    }
    timers.push(setTimeout(() => endGarnish(handle),
      spec.flashes * spec.gapMs + spec.onMs + 250));
    return handle;
  }

  /** Lazy loom module (spiral params are rolled FRESH per show — see
   *  showLoomSpiral). Dynamic import keeps this module import-side-effect-free
   *  and survives a missing dtrh folder: failure just retires the spiral garnish
   *  (loomDead drops it from future picks). */
  async function ensureLoom() {
    if (loomMod || loomDead) return loomMod;
    try {
      const m = await import('../../dtrh/shared/loomField.js');
      loomMod = m;
    } catch (e) {
      loomDead = true; loomMod = null;
      logSeam('loom import failed: ' + ((e && e.message) ? e.message : String(e)));
    }
    return loomMod;
  }
  /** ONE offscreen WebGL field, reused across shows (rebuilt on a size change,
   *  loomStudio-style). null -> the pure-2D drawFallbackFrame path. */
  function ensureLoomField(m, w, h) {
    if (loomFieldFailed) return null;
    if (loomField && loomField.canvas.width === w && loomField.canvas.height === h) return loomField;
    try {
      if (loomField) { // viewport changed: retire the old context first
        try {
          const lose = loomField.gl.getExtension('WEBGL_lose_context');
          if (lose) lose.loseContext();
        } catch (_e) {}
        loomField = null;
      }
      const c = document.createElement('canvas');
      c.width = w; c.height = h;
      loomField = m.createFieldRenderer(c);
      if (!loomField) loomFieldFailed = true; // no WebGL on this machine
    } catch (_e) { loomField = null; loomFieldFailed = true; }
    return loomField;
  }

  /** LOOM SPIRAL — the "ulterior reward": a live-rendered spiral, fullscreen,
   *  faded, 4-5s. rAF composites frames ONLY while the canvas is visible. */
  function showLoomSpiral(i) {
    const spec = spiralGarnishSpec(i);
    const alpha = spec.alpha * clampIntensity(1, capsOf());
    let cancelled = false, rafG = 0, el = null, anim = null, safety = 0, backdropTimer = 0;
    const stop = (fast) => {
      if (cancelled) return;
      cancelled = true;
      if (rafG) { try { cancelAnimationFrame(rafG); } catch (_e) {} rafG = 0; }
      if (safety) { clearTimeout(safety); safety = 0; }
      if (backdropTimer) { clearTimeout(backdropTimer); backdropTimer = 0; }
      try { if (anim) anim.cancel(); } catch (_e) {}
      const node = el;
      el = null;
      if (!node) return;
      releaseBackdrop(node); // life ends now (fade-out start) -> cards resolidify
      if (fast && supportsAnim) { // preempted: quick fade of the frozen frame
        try {
          const a2 = node.animate(
            [{ opacity: alpha * 0.8 }, { opacity: 0 }],
            { duration: GARNISH_PREEMPT_MS, easing: 'ease-out', fill: 'forwards' });
          a2.onfinish = () => removeNode(node);
        } catch (_e) {}
        setTimeout(() => removeNode(node), GARNISH_PREEMPT_MS + 80);
      } else removeNode(node);
    };
    const handle = { cancel: () => stop(true) };
    (async () => {
      const m = await ensureLoom();
      if (!m || cancelled || !hasDOM || inRecovery) return;
      mountGarnish();
      if (!glRoot) return;
      try {
        const vw = Math.max(2, (typeof window !== 'undefined' && window.innerWidth) | 0 || 2);
        const vh = Math.max(2, (typeof window !== 'undefined' && window.innerHeight) | 0 || 2);
        const scaleF = Math.min(1, 900 / Math.max(vw, vh)); // render budget; CSS upscales
        const w = Math.max(2, Math.round(vw * scaleF));
        const h = Math.max(2, Math.round(vh * scaleF));
        el = document.createElement('canvas');
        el.className = 'ixfx-gspiral';
        el.width = w; el.height = h;
        const ctx = el.getContext('2d');
        if (!ctx) { el = null; return; }
        glRoot.appendChild(el);
        track(el);
        el._ixBackdrop = true; backdropRef(true); // fullscreen live spiral: cards go see-through
        // FRESH params every mount + cross-module no-repeat: never the same spiral twice.
        // THE PLAYER'S COLOURS: spiralPalette() returns the 2-4 colours harvested
        // by the Calibration colour questions (core/palette.js), padded to four
        // threads; a short/absent harvest falls back to the theme palette below,
        // which is exactly what this used to be.
        const palette = spiralPalette([accent, accent2, '#ffffff']);
        const q = freshSpiralParams(() => m.randomParams2(palette));
        const span = Math.max(200, m.loopMs2(q));
        const field = ensureLoomField(m, w, h);
        const t0 = (typeof performance !== 'undefined' ? performance.now() : Date.now());
        const frame = () => {
          if (cancelled) return;
          const now = (typeof performance !== 'undefined' ? performance.now() : Date.now());
          const phase = ((now - t0) % span) / span;
          try {
            if (field) m.composeFrame(ctx, field, q, phase, w, h);
            else m.drawFallbackFrame(ctx, q, phase, w, h);
            if (!loomLogged) {
              loomLogged = true;
              logSeam('loom spiral first frame rendered (webgl=' + !!field + ')');
            }
          } catch (_e) {}
          rafG = requestAnimationFrame(frame);
        };
        rafG = requestAnimationFrame(frame);
        const finish = () => { stop(false); endGarnish(handle); };
        if (supportsAnim) {
          // slow fade-in (~1.6s) / gentle fade-out (~0.9s), clamped to the life.
          let fin = GARNISH_FADE_IN_MS, fout = GARNISH_FADE_OUT_MS;
          if (fin + fout > spec.durMs * 0.9) {
            const k = (spec.durMs * 0.9) / (fin + fout);
            fin *= k; fout *= k;
          }
          const inOff  = clamp01(fin / spec.durMs);
          const outOff = clamp01(1 - fout / spec.durMs);
          anim = el.animate(
            [{ opacity: 0 }, { opacity: alpha, offset: inOff },
             { opacity: alpha, offset: outOff }, { opacity: 0 }],
            { duration: spec.durMs, easing: 'ease-in-out' });
          anim.onfinish = finish;
          safety = setTimeout(finish, spec.durMs + 500);
          // release the see-through-card ref-count at the START of the fade-out
          const node = el;
          backdropTimer = setTimeout(() => releaseBackdrop(node), Math.max(0, spec.durMs - fout));
        } else {
          el.style.opacity = String(alpha);
          safety = setTimeout(finish, spec.durMs);
        }
      } catch (_e) {}
    })();
    return handle;
  }

  /** Fire the GENERIC sfx seam (audio.js listens on 'intake-sfx' and routes to
   *  audio.sfx). effects.js holds no audio handle, so — exactly like garnishCue
   *  — it reaches audio through a window CustomEvent. Never throws. */
  function sfxCue(id, rawIntensity) {
    try {
      if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
        window.dispatchEvent(new CustomEvent('intake-sfx', {
          detail: { id, intensity: (rawIntensity == null ? 1 : clamp01(rawIntensity)) },
        }));
      }
    } catch (_e) {}
  }

  /** Fire the audio seam for the garnishes that have a sound (spiral/sublim). */
  function garnishCue(name, rawIntensity) {
    if (name !== 'spiral' && name !== 'sublim') return;
    try {
      if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
        window.dispatchEvent(new CustomEvent(GARNISH_CUE_EVENT, {
          detail: { name, intensity: clamp01(rawIntensity) },
        }));
      }
    } catch (_e) {}
  }

  /** Run a garnish for a FIRED reward (a pairing, never a replacement).
   *  ALWAYS GARNISH, ALWAYS DIFFERENT: every fire above the tiny floors draws
   *  the next name from the shuffled bag; jackpots force drain-or-spiral (and
   *  consume it from the rotation). */
  function maybeGarnish(rewardEvent, depth, intensity) {
    if (!hasDOM || reducedMotion || inRecovery) return;
    const jackpot = !!rewardEvent.jackpot;
    if (!jackpot && (intensity < GARNISH_MIN_INTENSITY || depth < GARNISH_MIN_DEPTH)) return;
    // The spiral is RETIRED while the loom import is dead AND while the colour
    // harvest is still running: a spiral must never appear before the player has
    // finished naming the colours it is woven from (core/palette.js). In practice
    // the colour beats open the run at depths far under GARNISH_MIN_DEPTH, so
    // this is belt-and-braces against a future re-tune of the depth floors.
    const spiralOff = loomDead || harvestOpen();
    const kinds = spiralOff ? GARNISH_KINDS.filter((n) => n !== 'spiral') : GARNISH_KINDS;
    const name = jackpot
      ? (garnishBag.force(['drain', 'spiral'].filter((n) => kinds.includes(n))) || garnishBag.draw(kinds))
      : garnishBag.draw(kinds);
    if (!name) return;
    preemptGarnish(); // one at a time: fast-fade whatever is still live
    let h = null;
    try {
      if (name === 'pink') h = showPinkWash(intensity);
      else if (name === 'drain') h = showDrain(intensity);
      else if (name === 'sublim') h = showSublimFlashes(intensity);
      else if (name === 'spiral') h = showLoomSpiral(intensity);
    } catch (_e) {}
    if (h) {
      garnishNow = h;
      garnishCue(name, rewardEvent.intensity); // RAW: audio clamps by ITS caps
    }
  }

  /* ==========================================================================
   * GIFBURST — an in-browser CCP-flash REWARD (owner-directed). A burst spills
   * N fullscreen GIFs at random spots/sizes, their opacity climbing by run band
   * (0.15 / 0.30 / 0.50 / 0.75 / 1.00 via gifBurstOpacityForDepth). CLICK to
   * dismiss one, or GRAB + FLING it away. Each gif gets its OWN ~6s of UNPAUSED
   * life then fades on its own timer.
   *
   * COUNT SCALES WITH THE DESCENT (gifBurstCountForDepth): ~1 at the top of the
   * run, a rolled 5..10 at the bottom. Reduced motion stays at exactly one.
   *
   * OUTLIVES THE CARD: the layer is parented to <body>, NOT to the stage —
   * beats.js wipes the stage (`stage.innerHTML = ''`) at the top of every render,
   * which used to kill a live burst the instant the next card mounted. On <body>
   * the gifs survive the swap and expire only on their own clocks. z-index 5 puts
   * them above the stage/hud/aside, below the shell overlay + the jumpscare.
   *
   * NO HYDRA is now a BUDGET, not a singleton: bursts may overlap (they must, or
   * a 5s gif would block the next reward), but GIFBURST_MAX_NODES caps how many
   * are ever on screen and a new burst is trimmed to the free budget (0 -> skip,
   * never queued). It is a FOREGROUND TOY — it does NOT ref-count backdropRef,
   * so in-run cards stay solid behind it. All nodes/timers funnel through
   * track/removeNode + burstItems so recover(0) tears the whole spill out.
   * ========================================================================*/
  function mountBurstLayer() {
    if (!hasDOM) return;
    ensureStyles();
    // <body>, not `root` — see the section header (shared bodyHost() fallback).
    const host = bodyHost();
    if (burstRoot) {
      if (!burstRoot.parentNode) { try { host.appendChild(burstRoot); } catch (_e) {} }
      return;
    }
    try {
      burstRoot = document.createElement('div');
      burstRoot.className = 'ixfx-burst-root';
      burstRoot.setAttribute('aria-hidden', 'true');
      host.appendChild(burstRoot);
    } catch (_e) { burstRoot = null; }
  }
  function removeBurstLayer() {
    if (burstRoot) { try { if (burstRoot.parentNode) burstRoot.parentNode.removeChild(burstRoot); } catch (_e) {} }
    burstRoot = null;
  }
  /** Tear every live burst gif down instantly (cancel timers, remove nodes). */
  function killBurst() {
    for (const h of Array.from(burstItems)) { try { h.cancel(); } catch (_e) {} }
    burstItems.clear();
    burstLiveCount = 0;
  }

  /** Fire a GifBurst at the given run depth: roll the count off the depth curve,
   *  trim it to the free node budget, and spawn that many independent gifs.
   *  Bails without DOM / gifs / budget. RM: exactly one gif, no pop-in overshoot
   *  and no fling physics (click-dismiss only), same opacity ladder + 6s cap. */
  function showGifBurst(depth) {
    if (!hasDOM || !gifs.length) return;
    const budget = GIFBURST_MAX_NODES - burstLiveCount;
    if (budget <= 0) return;                    // ceiling: skip, never queue
    mountBurstLayer();
    if (!burstRoot) return;
    const d = clamp01(depth);
    const want = reducedMotion ? 1 : gifBurstCountForDepth(d);
    const n = Math.max(1, Math.min(want, budget));
    for (let i = 0; i < n; i++) {
      try { spawnBurstGif(d, n, i); } catch (_e) { /* one bad node never kills the spill */ }
    }
  }

  /** ONE gif of a burst: `n` is the spill size and `i` this gif's slot in it
   *  (drives the ring placement + the pop-in stagger). Fully independent — its
   *  own life clock, its own drag/fling, its own cleanup. */
  function spawnBurstGif(depth, n, i) {
    if (!burstRoot) return;

    // pick a gif, avoiding an immediate repeat of the last one spawned
    let url = pickOf(gifs);
    if (gifs.length > 1) { let g = 0; while (url === lastBurstGif && g++ < 6) url = pickOf(gifs); }
    lastBurstGif = url;
    noteMedia(url, 'gif');   // ledger for the archive (core/mediaLog.js)

    const targetAlpha = clamp01(gifBurstOpacityForDepth(depth)) * clampIntensity(1, capsOf());
    const rot = (Math.random() * 16 - 8);       // slight ±8deg tilt
    // A single gif keeps the original free placement + size. A crowd is thrown
    // around the centre on a jittered ring (varied angle/radius/size) so it reads
    // as a burst instead of a stack, and shrinks as the count grows so ten of
    // them still leave the card underneath readable.
    const shrink = clampRange(1 - 0.055 * (n - 1), 0.5, 1);
    const sizeVmin = (n === 1) ? (30 + Math.random() * 20)          // large-ish, 30..50vmin
                               : (22 + Math.random() * 26) * shrink; // ~11..27vmin at n=10
    let leftPct, topPct;
    if (n === 1) {
      leftPct = 8 + Math.random() * 58;         // biased inward so the box stays on-screen
      topPct  = 8 + Math.random() * 52;
    } else {
      const ang = (i / n) * Math.PI * 2 + (Math.random() - 0.5) * 0.9;
      const rad = 10 + Math.random() * 26;
      // left/top anchor the box's TOP-LEFT, so pull back by ~a third of the box
      // to sit it on the ring rather than hanging off it (units are close enough
      // — the clamp below is what actually keeps everything on screen).
      leftPct = 46 + Math.cos(ang) * rad * 1.15 - sizeVmin * 0.33;
      topPct  = 44 + Math.sin(ang) * rad - sizeVmin * 0.33;
    }
    leftPct = clampRange(leftPct, 2, Math.max(6, 90 - sizeVmin * 0.55));
    topPct  = clampRange(topPct,  2, Math.max(6, 86 - sizeVmin * 0.55));
    // stagger the pop-ins (and the life clocks with them) so the spill lands as a
    // cascade, not a single frame-slam. No stagger on the RM / no-WAAPI path.
    const delayMs = (supportsAnim && !reducedMotion) ? i * GIFBURST_STAGGER_MS : 0;

    let el;
    try {
      el = document.createElement('img');
      el.className = 'ixfx-burst';
      el.decoding = 'async';
      el.setAttribute('aria-hidden', 'true');
      el._ixBurst = true; burstLiveCount++;      // burst layer's own leak-guard counter
      el.style.width = sizeVmin.toFixed(2) + 'vmin';
      el.style.height = 'auto';
      el.style.left = leftPct.toFixed(2) + '%';
      el.style.top = topPct.toFixed(2) + '%';
      el.onerror = () => cleanup();              // bad url -> vanish, free the slot
      el.src = url;
      burstRoot.appendChild(el);
    } catch (_e) {
      // give the slot back only if we actually took one (marker = idempotent)
      if (el && el._ixBurst) { el._ixBurst = false; burstLiveCount = Math.max(0, burstLiveCount - 1); }
      return;
    }

    track(el);                                   // recover(0) tears it out with the rest

    // --- per-instance state ---------------------------------------------------
    let curX = 0, curY = 0;                      // current translate offset (px)
    let baseX = 0, baseY = 0;                    // offset captured at pointerdown
    let dragging = false, dead = false, ended = false, pointerId = null;
    let moved = 0, downX = 0, downY = 0;
    let samples = [];                            // {x,y,t} for release velocity
    let popAnim = null;
    // the stagger is added to the clock so a late member still gets its full ~6s
    let lifeTimer = 0, lifeRemaining = GIFBURST_LIFE_MS + delayMs, lifeStart = 0;
    const nowMs = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());
    const resting = () => `translate(${curX.toFixed(1)}px,${curY.toFixed(1)}px) rotate(${rot.toFixed(2)}deg)`;

    function startLife() {
      lifeStart = nowMs();
      lifeTimer = setTimeout(() => { lifeTimer = 0; if (!ended) autoFade(); }, Math.max(0, lifeRemaining));
    }
    function pauseLife() {
      if (!lifeTimer) return;
      clearTimeout(lifeTimer); lifeTimer = 0;
      lifeRemaining = Math.max(0, lifeRemaining - (nowMs() - lifeStart));
    }
    function resumeLife() { if (!dead && !ended && !lifeTimer) startLife(); }

    function cleanup() {
      if (dead) return; dead = true; ended = true;
      if (lifeTimer) { clearTimeout(lifeTimer); lifeTimer = 0; }
      removeNode(el);                            // frees this gif's node budget
      burstItems.delete(handle);
    }
    function endWith(toTransform, durMs, easing) {
      if (ended) return; ended = true;
      if (lifeTimer) { clearTimeout(lifeTimer); lifeTimer = 0; }
      if (supportsAnim) {
        try {
          if (popAnim) { try { popAnim.cancel(); } catch (_e) {} popAnim = null; }
          const a = el.animate(
            [{ opacity: targetAlpha, transform: resting() },
             { opacity: 0, transform: toTransform }],
            { duration: durMs, easing: easing, fill: 'forwards' });
          a.onfinish = cleanup;
          setTimeout(cleanup, durMs + 120);       // safety
        } catch (_e) { cleanup(); }
      } else cleanup();
    }
    function autoFade() {                          // 6s cap -> gentle fade in place
      endWith(`translate(${curX.toFixed(1)}px,${curY.toFixed(1)}px) rotate(${rot.toFixed(2)}deg) scale(.92)`,
        GIFBURST_FADE_MS, 'ease-in');
    }
    function dismiss() {                           // click -> quick fade + shrink
      endWith(`translate(${curX.toFixed(1)}px,${curY.toFixed(1)}px) rotate(${rot.toFixed(2)}deg) scale(.6)`,
        GIFBURST_DISMISS_MS, 'ease-in');
    }
    function fling(vx, vy) {                       // fast release -> fly off with spin
      const speed = Math.hypot(vx, vy) || 1;
      // faster fling = slightly shorter flight; 350..500ms momentum ease-out.
      const durMs = Math.round(500 - clamp01((speed - GIFBURST_FLING_MIN) / 3) * 150);
      const travel = Math.min(4200, speed * durMs * 1.1);
      const nx = curX + (vx / speed) * travel;
      const ny = curY + (vy / speed) * travel;
      const spin = rot + (vx >= 0 ? 1 : -1) * (18 + Math.random() * 24);
      endWith(`translate(${nx.toFixed(1)}px,${ny.toFixed(1)}px) rotate(${spin.toFixed(2)}deg)`,
        durMs, 'cubic-bezier(.22,.61,.36,1)');
    }

    // --- pointer interactions -------------------------------------------------
    function commitPop() {                        // freeze pop-in so inline drag styles win
      if (popAnim) { try { popAnim.cancel(); } catch (_e) {} popAnim = null; }
      try { el.style.opacity = String(targetAlpha); el.style.transform = resting(); } catch (_e) {}
    }
    function onDown(e) {
      if (dead || ended) return;
      try { e.preventDefault(); } catch (_e) {}
      commitPop();
      if (reducedMotion) { dismiss(); return; }   // RM: no drag/fling, click-dismiss only
      dragging = true; moved = 0;
      baseX = curX; baseY = curY;
      downX = e.clientX; downY = e.clientY;
      samples = [{ x: e.clientX, y: e.clientY, t: nowMs() }];
      try { el.classList.add('ixfx-grabbing'); } catch (_e) {}
      pauseLife();                                // 6s clock pauses while dragging
      pointerId = (e.pointerId != null) ? e.pointerId : null;
      try { if (pointerId != null && el.setPointerCapture) el.setPointerCapture(pointerId); } catch (_e) {}
    }
    function onMove(e) {
      if (!dragging || dead) return;
      const dx = e.clientX - downX, dy = e.clientY - downY;
      moved = Math.max(moved, Math.hypot(dx, dy));
      curX = baseX + dx; curY = baseY + dy;
      try { el.style.transform = resting(); } catch (_e) {}
      const t = nowMs();
      samples.push({ x: e.clientX, y: e.clientY, t });
      if (samples.length > 6) samples.shift();
    }
    function onUp(e) {
      if (!dragging || dead) return;
      dragging = false;
      try { el.classList.remove('ixfx-grabbing'); } catch (_e) {}
      try { if (pointerId != null && el.releasePointerCapture) el.releasePointerCapture(pointerId); } catch (_e) {}
      if (moved < GIFBURST_DRAG_PX) { dismiss(); return; } // negligible move = a click
      const t = nowMs();
      let a = samples[0];
      for (const s of samples) { if (t - s.t <= 110) { a = s; break; } } // ~last 110ms window
      const b = samples[samples.length - 1];
      const dt = Math.max(1, b.t - a.t);
      const vx = (b.x - a.x) / dt, vy = (b.y - a.y) / dt;
      if (Math.hypot(vx, vy) >= GIFBURST_FLING_MIN) fling(vx, vy);
      else resumeLife();                          // slow release: drop in place, clock resumes
    }
    function onCancel() {
      if (!dragging || dead) return;
      dragging = false;
      try { el.classList.remove('ixfx-grabbing'); } catch (_e) {}
      resumeLife();
    }
    try {
      el.addEventListener('pointerdown', onDown);
      el.addEventListener('pointermove', onMove);
      el.addEventListener('pointerup', onUp);
      el.addEventListener('pointercancel', onCancel);
      el.addEventListener('lostpointercapture', onCancel);
      // fallback for engines without Pointer Events: plain click dismisses.
      if (typeof window === 'undefined' || !('PointerEvent' in window)) {
        el.addEventListener('click', () => { if (!dead && !ended) dismiss(); });
      }
    } catch (_e) {}

    // --- entrance -------------------------------------------------------------
    // While a staggered member is still waiting its turn it is invisible but
    // would otherwise still be hit-testable, swallowing clicks meant for the card
    // underneath — so it stays click-through until its pop-in has run.
    if (delayMs > 0) { try { el.style.pointerEvents = 'none'; } catch (_e) {} }
    const enablePointer = () => { try { el.style.pointerEvents = 'auto'; } catch (_e) {} };
    if (supportsAnim && !reducedMotion) {
      try {
        popAnim = el.animate(
          [{ opacity: 0, transform: `translate(0px,0px) rotate(${rot.toFixed(2)}deg) scale(.35)` },
           { opacity: targetAlpha, offset: 0.72, transform: `translate(0px,0px) rotate(${rot.toFixed(2)}deg) scale(1.09)` },
           { opacity: targetAlpha, transform: `translate(0px,0px) rotate(${rot.toFixed(2)}deg) scale(1)` }],
          { duration: GIFBURST_POP_MS, delay: delayMs, easing: 'cubic-bezier(.2,.85,.35,1.25)', fill: 'both' });
        popAnim.onfinish = () => { commitPop(); enablePointer(); };
      } catch (_e) {
        enablePointer();
        try { el.style.opacity = String(targetAlpha); el.style.transform = resting(); } catch (_e2) {}
      }
    } else {
      // RM / no WAAPI: gentle fade-in, no overshoot (delayMs is 0 on this path).
      enablePointer();
      try {
        el.style.transform = resting();
        el.style.transition = 'opacity 200ms ease';
        if (typeof requestAnimationFrame === 'function') {
          requestAnimationFrame(() => { if (!dead) try { el.style.opacity = String(targetAlpha); } catch (_e) {} });
        } else { el.style.opacity = String(targetAlpha); }
      } catch (_e) {}
    }

    const handle = { cancel: () => cleanup() };
    burstItems.add(handle);
    startLife();
  }

  /* ==========================================================================
   * GIFRAIN — the DTRH gif-cascade, ported as a RARE reward. Where the burst
   * throws a spill of grabbable gifs AT you, the rain lets them fall PAST you:
   * drops enter above the viewport, slide the whole screen height while growing
   * from 0.45x to 1x, and leave. It is the port of dtrh/game/payloadFx.js
   * gifCascade (itself the port of the C# ChaosGifCascadeOverlay), numbers
   * unchanged — 1.67 spawns/s across a ~6s window, 2.4-3.8s per fall — with the
   * same three bits of bookkeeping that keep DTRH's version bounded: a spawn
   * loop with an explicit deadline, a live-node cap (MAX_CASCADE -> 14), and a
   * cancel handle so a teardown can stop the loop mid-window.
   *
   * WHAT IS DELIBERATELY DIFFERENT FROM DTRH:
   *   · SINGLETON. DTRH can stack cascades (each pop makes its own loop); a
   *     reward roll here can repeat, so a re-trigger EXTENDS the live window
   *     rather than adding a second loop — same idiom as payloadFx's holdOn
   *     refreshing a deadline instead of stacking overlays.
   *   · Opacity rides the SAME band ladder as the burst (gifBurstOpacityForDepth
   *     x caps), so rain and burst read as one family instead of the rain being
   *     the one effect in the run that ignores the descent.
   *   · It draws from the whole visual manifest (gifs + stills), like the drain
   *     wash does — DTRH's anyImageUrl does the same. Every pick is logged
   *     through noteMedia so the Records Office lists what the rain paid you.
   *
   * TIMING: the spawn loop is a GLOBAL rAF + performance.now and the fall is a
   * CSS animation, so the whole downpour freezes under the pause menu's shim
   * without a single pause-aware line here (ui/pause.js header).
   * ========================================================================*/

  /** Put rainRoot in the body UNDER the garnish + burst layers (all three are
   *  z5; DOM order breaks the tie). Falls back to a plain append. */
  function placeRain(host) {
    const under = (glRoot && glRoot.parentNode === host) ? glRoot
                : (burstRoot && burstRoot.parentNode === host) ? burstRoot : null;
    if (under) host.insertBefore(rainRoot, under); else host.appendChild(rainRoot);
  }
  function mountRainLayer() {
    if (!hasDOM) return;
    ensureStyles();
    const host = bodyHost();
    if (rainRoot) {
      if (!rainRoot.parentNode) { try { placeRain(host); } catch (_e) {} }
      return;
    }
    try {
      rainRoot = document.createElement('div');
      rainRoot.className = 'ixfx-rain-root';
      rainRoot.setAttribute('aria-hidden', 'true');
      placeRain(host);
    } catch (_e) { rainRoot = null; }
  }
  function removeRainLayer() {
    if (rainRoot) { try { if (rainRoot.parentNode) rainRoot.parentNode.removeChild(rainRoot); } catch (_e) {} }
    rainRoot = null;
    lastRainUrl = null;
  }
  /** Stop the downpour AND clear whatever is still falling (recover/dispose). */
  function killRain() {
    if (rainCancel) { try { rainCancel(); } catch (_e) {} rainCancel = null; }
    for (const el of Array.from(live)) if (el && el._ixRain) removeNode(el);
    rainLiveCount = 0;
  }

  /** Start (or extend) the downpour at the given run depth. Bails without DOM
   *  or media; the caller handles the no-media degrade. */
  function showGifRain(depth) {
    if (!hasDOM || !ambientPool().length) return;
    const now = (typeof performance !== 'undefined' ? performance.now() : Date.now());
    rainDepth = clamp01(depth);
    if (rainCancel) { rainEndAt = Math.max(rainEndAt, now + GIFRAIN_WINDOW_MS); return; }
    mountRainLayer();
    if (!rainRoot) return;
    rainEndAt = now + GIFRAIN_WINDOW_MS;
    let nextAt = now, stopped = false, raf = 0;
    const step = (t) => {
      raf = 0;
      if (stopped) return;
      // DTRH's catch-up spawn: a long frame pays out every drop it slept
      // through, and the `t < rainEndAt` guard is what bounds the whole loop.
      while (t >= nextAt && t < rainEndAt) { spawnRainDrop(rainDepth); nextAt += GIFRAIN_GAP_MS; }
      if (t >= rainEndAt) { rainCancel = null; return; } // window done; drops finish falling
      raf = requestAnimationFrame(step);
    };
    rainCancel = () => {
      stopped = true;
      if (raf) { try { cancelAnimationFrame(raf); } catch (_e) {} raf = 0; }
    };
    raf = requestAnimationFrame(step);
  }

  /** ONE falling drop. Own fall duration, own safety-net removal. */
  function spawnRainDrop(depth) {
    if (!rainRoot || rainLiveCount >= GIFRAIN_MAX_NODES) return;
    let url = pickOf(ambientPool());
    if (ambientPool().length > 1) { let g = 0; while (url === lastRainUrl && g++ < 6) url = pickOf(ambientPool()); }
    lastRainUrl = url;
    noteMedia(url, gifs.indexOf(url) >= 0 ? 'gif' : 'image'); // ledger (core/mediaLog.js)

    const alpha = clamp01(gifBurstOpacityForDepth(depth)) * clampIntensity(1, capsOf());
    const fallS = GIFRAIN_FALL_MIN_S + Math.random() * GIFRAIN_FALL_SPAN_S;
    let el;
    try {
      el = document.createElement('img');
      el.className = 'ixfx-rain';
      el.decoding = 'async';
      el.setAttribute('aria-hidden', 'true');
      el._ixRain = true; rainLiveCount++;     // rain layer's own leak-guard counter
      el.style.left = (4 + Math.random() * 80).toFixed(2) + 'vw';
      el.style.setProperty('--ixfx-fall', fallS.toFixed(2) + 's');
      el.style.setProperty('--ixfx-rain-a', String(alpha));
      el.onerror = () => removeNode(el);      // bad url -> vanish, free the slot
      el.src = url;
      rainRoot.appendChild(el);
    } catch (_e) {
      if (el && el._ixRain) { el._ixRain = false; rainLiveCount = Math.max(0, rainLiveCount - 1); }
      return;
    }
    el.addEventListener('animationend', () => removeNode(el), { once: true });
    // Safety net on the GLOBAL clock: if animationend never fires (decode failure,
    // a tab that never composited the layer) the node still goes. Pausing stops
    // the CSS fall and this timer together, so the slack stays honest.
    track(el, Math.round(fallS * 1000) + 900);
  }

  /* ----- public API --------------------------------------------------------- */

  /** Drive the ambient stack from one depth scalar (invariant #2: via caps). */
  function setDepth(depth) {
    inRecovery = false; // the normal drive path re-arms ambient + garnishes
    applyDepth(depth);
  }
  /** Shared depth application (recover() reuses it WITHOUT clearing the flag). */
  function applyDepth(depth) {
    depthNow = clamp01(depth);
    const ch = clampToCaps(depthToChannels(depthNow), capsOf());
    vis = channelsToVisual(ch);
    if (!hasDOM) return;
    if (depthNow <= 0.0005) { // nothing to show
      stopLoop();
      // leave existing spawned nodes to finish; teardown is recover(0)'s job.
      return;
    }
    mount();
    startLoop();
    scheduleAmbient(); // depth-gated inside; no-op until depth > AMBIENT_MIN_DEPTH
  }

  /** Render a resolved reward: no-repeat variant per kind, scaled by clamped
   *  intensity (invariant #2). Handles jackpot / nearMiss / streak. */
  function play(rewardEvent, depth) {
    if (!rewardEvent || !hasDOM) return;
    const intensity = clampIntensity(rewardEvent.intensity, capsOf());
    // streak meter rides EVERY event that carries a streak (including misses,
    // which is when the reset/shatter happens).
    if (typeof rewardEvent.streak === 'number') updateStreak(rewardEvent.streak);
    if (!rewardEvent.fire) {
      // fire=false is no longer an early return: a near-miss gets its tease.
      if (rewardEvent.nearMiss && intensity > 0.0005) { mount(); nearMissTease(intensity); }
      return;
    }
    if (intensity <= 0.0005) return;
    mount();
    if (rewardEvent.jackpot) {
      jackpotCeremony(intensity);
    } else switch (rewardEvent.kind) {
      case RewardKind.Flash:  runVariant(RewardKind.Flash, FLASH_VARIANTS, intensity); break;
      case RewardKind.Bubble: runVariant(RewardKind.Bubble, BUBBLE_VARIANTS, intensity); break;
      case RewardKind.Drop: {
        // media path: real GIF burst; standalone: rotate the particle variants.
        if (gifs.length && !reducedMotion) { try { gifBurst(intensity); } catch (_e) {} }
        else runVariant(RewardKind.Drop, DROP_VARIANTS, intensity);
        break;
      }
      case RewardKind.Praise: runVariant(RewardKind.Praise, PRAISE_VARIANTS, intensity); break;
      case RewardKind.Chime:  runVariant(RewardKind.Chime, CHIME_VARIANTS, intensity); break;
      case GIFBURST_KIND: {
        // In-browser CCP-flash reward. Needs real gifs; without them, degrade to
        // a flash variant so the fired reward still pays SOMETHING.
        const d = clamp01(typeof depth === 'number' ? depth : depthNow);
        if (gifs.length) { try { showGifBurst(d); } catch (_e) {} }
        else runVariant(RewardKind.Flash, FLASH_VARIANTS, intensity);
        break;
      }
      case GIFRAIN_KIND: {
        // The rare one. Reduced motion gets the burst instead of a screenful of
        // falling gifs (the burst's own RM path is a single, still node) — the
        // reward still pays, it just stops moving.
        const d = clamp01(typeof depth === 'number' ? depth : depthNow);
        if (reducedMotion || !ambientPool().length) {
          // showGifBurst needs real gifs; with neither pool the fired reward
          // still pays SOMETHING (same degrade as the GifBurst kind above).
          if (gifs.length) { try { showGifBurst(d); } catch (_e) {} }
          else runVariant(RewardKind.Flash, FLASH_VARIANTS, intensity);
        } else { try { showGifRain(d); } catch (_e) {} }
        break;
      }
      case RewardKind.None:
      default: break;
    }
    // the garnish gamble rides the same fire (jackpots always land one)
    maybeGarnish(rewardEvent, clamp01(typeof depth === 'number' ? depth : depthNow), intensity);
  }

  /** Invariant #3: un-ramp toward 0. Recovery stays CLEAN: any recover() call
   *  kills the ambient layer + force-clears a live garnish and keeps both down
   *  until the next setDepth. depth<=0 tears the whole stack out — spawned
   *  nodes, media nodes, streak meter, ambient + garnish layers, everything. */
  function recover(depth) {
    const d = clamp01(depth);
    inRecovery = true;
    killAmbient();
    preemptGarnish();
    killBurst();              // foreground toy: clear it on any recover()
    killRain();               // ...and the downpour stops with it
    if (d > 0.0005) { applyDepth(d); return; }
    // full surfacing: stop spawning, drop every live node, hide + remove layers.
    depthNow = 0;
    vis = channelsToVisual(clampToCaps(depthToChannels(0), capsOf()));
    stopLoop();
    if (flashEl) flashEl.style.opacity = '0';
    for (const el of Array.from(live)) removeNode(el);
    live.clear();
    removeStreakMeter();
    lastStreakShown = 0;
    mediaLiveCount = 0;
    removeGarnishLayer();
    removeBurstLayer();
    burstItems.clear(); burstLiveCount = 0;   // killBurst() above already cancelled them
    lastBurstGif = null;
    removeRainLayer();
    rainLiveCount = 0; rainCancel = null; rainEndAt = 0;   // killRain() above already stopped it
    if (layer) { try { if (layer.parentNode) layer.parentNode.removeChild(layer); } catch (_e) {} }
    layer = null; flashEl = null; mounted = false;
  }

  /** Full teardown for hosts (boot doesn't call it): recover(0) already clears
   *  every timer, node, and layer this factory owns. */
  function dispose() { recover(0); }

  return { setDepth, play, recover, dispose };
}
