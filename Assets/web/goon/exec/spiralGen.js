/* ============================================================================
 * exec/spiralGen.js — THE SPIRAL FOUNDRY. Every spiral this duel shows is woven
 * here, at run time, out of nothing.
 *
 * WHY THIS EXISTS. The bed used to draw from a BAKED POOL: six DtRH GIF/WebP
 * files under /dtrh/assets/bubbles/effects/spirals/, plus a hand-authored table
 * of six presets for the shader path. Both were fixed content — the same six
 * spirals, forever, in every match, on every machine. The owner asked for the
 * Loom's trick instead: the Loom has a 🎲 "surprise me" that rolls a whole
 * spiral out of a parameter space (dtrh/shared/loomField.js#randomParams2), and
 * that is what this module is, ported into goon's tree and re-tuned for a
 * screen-blended fullscreen BED rather than a framed GIF the player is staring
 * straight at.
 *
 * WHAT WAS DELIBERATELY LEFT BEHIND: the Loom's CENTERPIECE (the dot / star /
 * cross / x it can stamp over the middle at 25%). Excluded by the owner, and
 * excluded here BY CONSTRUCTION rather than by a `if (kind !== 'dot')` — the
 * param schema this module rolls into (spiralField.js#normalizeSpiralParams)
 * has no centerpiece field at all, and the raster renderer below draws no
 * centre mark. There is nowhere for one to come from. Same for `hueCycles`: a
 * hue sweep on a screen blend reads as a colour strobe.
 *
 * PRE-GENERATED, NOT PER-FIRE. A session rolls SESSION_SIZE variants ONCE and
 * then rotates through them (shuffled, never the same one twice in a row). Two
 * reasons, and neither is performance theatre:
 *   · a match gets a SET it can recognise — five spirals that come back is a
 *     texture, a fresh spiral every three seconds is noise;
 *   · the raster bake (below) is the expensive half, and a fixed set of five
 *     can be baked lazily and cached instead of re-encoded per cue.
 *
 * …AND WARMED, NOT BAKED ON A CUE (2026-08-05). "Lazily" above used to mean
 * "on the main thread, in the middle of the cue that needs it", and on an
 * iPhone that is a VISIBLE FREEZE: a 1280px bake is ~1600 antialiased path
 * fills plus a PNG encode of a 6.5MB backing store, and the owner's play-test
 * found a spiral that mounted and then sat perfectly still for a second or two
 * before it started turning. warmSpiralStills() walks the pool during IDLE time
 * instead — it is armed from exec/spiral.js at executor build, which on this
 * page is app build, so the whole title/lobby/countdown stretch is available
 * and every bake is already cached long before a cue can ask for one. The
 * laziness is still there underneath (a host that never idles still bakes on
 * first ask); what changed is that nothing normal ever reaches it.
 *
 * EVERY VARIANT IS BOTH BEDS AT ONCE. `params` drives exec/spiralField.js's
 * WebGL field; `image()` bakes the SAME parameters to a still PNG for the
 * no-WebGL raster path and for the bubble-pop flick. That is new: the shader
 * bed and the fallback bed used to show unrelated spirals, so a context loss
 * mid-match visibly swapped the picture. Now it swaps the renderer only.
 *
 * DETERMINISM. Every roll takes an injectable `rng` (default Math.random) so
 * test/selftest-exec.js can drive the whole space from a seeded generator.
 * App code passes nothing and gets Math.random, which is correct here — a
 * spiral has no wire contract and no peer to agree with.
 * ==========================================================================*/

import { normalizeSpiralParams, revolutionSecFor } from './spiralField.js';

/* ---------------------------------------------------------------------------
 * THE BOUNDS. The Loom's randomParams2 rolls for a GIF the player will stare
 * at head-on; this bed screen-blends over live gameplay at up to 0.70 opacity.
 * Three of its dials are therefore pulled in hard, and the rest are the Loom's:
 *   · DUTY (the lit fraction of an arm band) — the Loom rolls 0.30..0.70, this
 *     rolls 0.24..0.36. Coverage is what turns a spiral into a curtain. Raise
 *     the ARM COUNT to make one busier, never the duty.
 *   · PALETTE — goon's five, not the Loom's eight. Its acid green / amber /
 *     orange are lovely on black and muddy over a pink HUD.
 *   · REVOLUTION TIME — pinned to 10..18s (see solveSpin) rather than left to
 *     fall out of the speed roll. That is the band the retired CSS keyframe
 *     `ggSpiralSpin 14s` sat in, and it is slow enough to read as a pull.
 * ------------------------------------------------------------------------ */

/** Goon's swatches. The Loom's LOOM_SWATCHES minus everything warm. */
export const SPIRAL_PALETTE = Object.freeze([
  '#ff69b4',   // hot pink — the house colour
  '#e56cc0',   // dusty rose
  '#ff8fd0',   // pale pink
  '#8a5cff',   // violet
  '#00e5ff',   // cyan (the counter-thread; sparing, it is the loudest of the five)
]);

/** Near-black field colours. The bed is screen-blended, so these read as clear. */
const BG_INNER = Object.freeze(['#0a0410', '#080312', '#070310', '#060210', '#0a0414']);
const BG_OUTER = Object.freeze(['#04010a', '#030108', '#020106']);

const STYLES = Object.freeze(['log', 'arch', 'ribbon', 'golden', 'tunnel', 'petal']);

/**
 * Arm-count band per style, and they are NOT interchangeable. Petals are WIDE
 * (a rose lobe, not a line) so a low count reads as a pinwheel; tunnel bands
 * ride the radius so a low count reads as three fat rings. The rest are arms in
 * the ordinary sense and 3..8 is the whole useful range — past 8 a screen blend
 * turns them into a haze.
 */
const ARM_BAND = Object.freeze({
  log: [3, 8], arch: [3, 8], ribbon: [3, 7], golden: [4, 8], tunnel: [5, 10], petal: [6, 10],
});

/** The spin band every variant is solved into, in seconds per revolution. */
export const REV_SEC_MIN = 10;
export const REV_SEC_MAX = 18;

/** Variants a session pre-generates. The owner asked for 4-5; five it is. */
export const SESSION_SIZE = 5;

/**
 * Bake resolution for the raster fallback, in pixels square.
 *
 * THE MAGNIFICATION MATH, because this number is the whole quality argument.
 * The pane covers the window and fx.css then multiplies it by `scale: 1.6`
 * (rotation overscan — a square at `cover` leaves the viewport corners bare at
 * 45°). On a 1920-wide window that is 3072 effective pixels. At 1280 the bake
 * is magnified 2.4x, against 4.3x-8.5x for the 360..720px GIFs this replaces —
 * and unlike them it is a smooth two-tone wedge fill with no palette and no
 * dither, so what magnification costs is softness, not crawling grain.
 * Going higher is not free: the PNG data URL is held in an inline style.
 */
export const RASTER_PX = 1280;

/**
 * …and what the LITE tier bakes at (exec/perfTier.js — phones).
 *
 * BOTH HALVES OF THE COST ARE QUADRATIC IN THIS NUMBER: the wedge fills cover
 * area, and the PNG encode chews the whole backing store (1280² RGBA is 6.5MB,
 * 640² is 1.6MB). Quartering it is the difference between a bake a phone can
 * absorb in an idle slice and one it cannot.
 *
 * AND IT COSTS THE LITE TIER NOTHING VISIBLE, because the still is the FALLBACK
 * bed there and the fallback's competition is a shader that already draws at
 * LITE_MAX_DPR = 1 — a 428pt-wide iPhone weaves at 428 backing pixels, so a
 * 640px bake is MORE detail than the live path on the same device, under a
 * screen blend at <=0.70 with a 1.1px blur over it.
 */
export const LITE_RASTER_PX = 640;

/* ---------------------------------------------------------------------------
 * ROLLING ONE
 * ------------------------------------------------------------------------ */

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const snap = (v, step) => Math.round(v / step) * step;
/** A defended rng: a host (or a test) that hands back junk must not poison a roll. */
const rngOf = (rng) => {
  const f = typeof rng === 'function' ? rng : Math.random;
  return () => {
    const v = f();
    return typeof v === 'number' && v >= 0 && v < 1 ? v : 0;
  };
};
const pickFrom = (arr, r) => arr[Math.min(arr.length - 1, (r() * arr.length) | 0)];
const between = (lo, hi, r) => lo + r() * (hi - lo);
const intBetween = (lo, hi, r) => lo + Math.min(hi - lo, (r() * (hi - lo + 1)) | 0);

/** Fisher-Yates on a copy — the Loom's `threads()`, minus its sort-comparator shuffle. */
function shuffled(arr, r) {
  const a = arr.slice();
  for (let i = a.length - 1; i > 0; i--) {
    const j = (r() * (i + 1)) | 0;
    const t = a[i]; a[i] = a[j]; a[j] = t;
  }
  return a;
}

/**
 * Arm counts a layer with `threads` colours may take, in `style`'s band. TWO
 * constraints, and both are arithmetic rather than taste — see the revolution
 * formula in spiralField.js's banner:
 *
 *   revSec = loopMs(speed) * arms / (arms % threads === 0 ? threads : arms)
 *            / speedMul / 1000
 *
 *   1. arms % threads === 0. Otherwise there is NO sub-2PI symmetry: the loop
 *      has to cover a whole turn and the bed spins arms/threads times too fast.
 *      (This is the trap the retired `golden` preset was written around: 3 arms
 *      with 2 threads came out at 3.6s instead of 10.8s.)
 *   2. arms >= MIN_ARMS_PER_THREAD * threads. `loopMs` tops out at 3600ms
 *      (speed 1), so the SLOWEST revolution any combination can reach is
 *      3.6s * arms/threads. At a ratio of 3 that is 10.8s — just inside
 *      REV_SEC_MIN, which is what lets solveSpin() promise the band instead of
 *      hoping for it. A ratio of 1 (say 3 arms, 3 threads) caps out at 3.6s and
 *      would give the bed a spin nobody can look at.
 *
 * May return [] — a style's band simply has no legal count for that many
 * threads (3 threads need 9+ arms, which only tunnel and petal reach). The
 * caller picks the thread count from what the style can actually carry.
 */
const MIN_ARMS_PER_THREAD = 3;
function armChoices(style, threads) {
  const [lo, hi] = ARM_BAND[style] || [3, 8];
  const out = [];
  for (let a = Math.max(lo, MIN_ARMS_PER_THREAD * threads); a <= hi; a++) {
    if (a % threads === 0) out.push(a);
  }
  return out;
}

/** Thread counts `style` can carry at all, cheapest question first. */
function threadChoices(style) {
  return [1, 2, 3].filter((n) => armChoices(style, n).length > 0);
}

/**
 * Pick the (speed, speedMul) pair that puts one revolution closest to
 * `targetSec`. Fifteen combinations, each evaluated with the FIELD'S OWN
 * formula, so the answer is right by construction rather than right by comment.
 *
 * This is what replaces the hand-tuning every retired preset carried ("8 arms
 * at speed 3, not 5 at speed 1, because petals are wide and 8 arms at speed 1
 * would crawl round in 28.8s"). A generator cannot hand-tune, so it solves.
 *
 * IN-BAND CANDIDATES WIN OUTRIGHT, even when an out-of-band one sits nearer the
 * target: the target is a preference, [REV_SEC_MIN, REV_SEC_MAX] is the actual
 * requirement. Without this a 5-armed roll asked for 10.0s would take 9.0s
 * (0.9 off) over 12.6s (2.6 off) and quietly leave the band. armChoices'
 * constraint 2 is what guarantees the in-band set is never empty.
 */
function solveSpin(base, targetSec) {
  let best = null;
  let nearest = null;
  for (let speed = 1; speed <= 5; speed++) {
    for (let speedMul = 1; speedMul <= 3; speedMul++) {
      const q = Object.assign({}, base, {
        speed,
        layer: Object.assign({}, base.layer, { speedMul }),
      });
      const revSec = revolutionSecFor(q);
      const cand = { speed, speedMul, revSec, err: Math.abs(revSec - targetSec) };
      if (!nearest || cand.err < nearest.err) nearest = cand;
      if (revSec < REV_SEC_MIN || revSec > REV_SEC_MAX) continue;
      if (!best || cand.err < best.err) best = cand;
    }
  }
  return best || nearest || { speed: 1, speedMul: 1, revSec: 0, err: Infinity };
}

/**
 * Roll one spiral. Returns NORMALIZED schema-v2 params (spiralField's own
 * shape) plus the solved revolution time, which the raster bed uses as its CSS
 * spin duration so both paths turn at the same rate.
 *
 * @param {() => number} [rng] injectable for tests; app code wants Math.random
 * @returns {{params: object, revSec: number}}
 */
export function rollSpiral(rng) {
  const r = rngOf(rng);

  const style = pickFrom(STYLES, r);
  // 1 thread most of the time: two colours on a screen blend is a candy stripe,
  // three is a rainbow. The Loom rolls 1-3 evenly; a bed cannot afford that.
  // Weighted 6/3/1, then intersected with what this style's arm band can carry
  // (see armChoices) — the weights are a preference, the arithmetic is not.
  const legal = threadChoices(style);
  const bag = [];
  for (const n of legal) for (let i = 0; i < (n === 1 ? 6 : n === 2 ? 3 : 1); i++) bag.push(n);
  const threadCount = bag.length ? pickFrom(bag, r) : 1;
  const palette = shuffled(SPIRAL_PALETTE, r);
  const colors = palette.slice(0, threadCount);
  const choices = armChoices(style, threadCount);
  const arms = choices.length ? pickFrom(choices, r) : MIN_ARMS_PER_THREAD * threadCount;
  const direction = r() < 0.5 ? 1 : -1;

  const base = {
    speed: 1,
    glow: +between(0.18, 0.45, r).toFixed(3),
    layer: {
      arms,
      turns: clamp(snap(between(1.5, 3.25, r), 0.25), 0.5, 6),
      duty: +clamp(snap(between(0.24, 0.36, r), 0.02), 0.2, 0.8).toFixed(3),
      style,
      direction,
      colors,
      // Gradient blending only makes sense with something to blend BETWEEN.
      bandMode: threadCount > 1 && r() < 0.3 ? 'gradient' : 'hard',
      speedMul: 1,
    },
    bg: { kind: r() < 0.5 ? 'radial' : 'solid', color: pickFrom(BG_INNER, r), outer: pickFrom(BG_OUTER, r) },
  };

  // A counter-rotating second weave, sometimes. Always the OPPOSITE direction
  // (the Loom does this too) — two layers turning the same way just look thick.
  if (r() < 0.35) {
    base.layer2 = {
      enabled: true,
      arms: intBetween(2, 6, r),
      turns: clamp(snap(between(0.75, 2, r), 0.25), 0.5, 6),
      duty: +between(0.16, 0.26, r).toFixed(3),
      style: pickFrom(['arch', 'log', 'ribbon'], r),
      direction: -direction,
      colors: [palette[palette.length - 1]],   // the thread layer 1 did NOT take
      speedMul: 1,
    };
  }
  if (r() < 0.35) base.pulse = { amp: +between(0.05, 0.1, r).toFixed(3), cycles: 1 };
  if (r() < 0.3) base.wobble = { amp: +between(0.06, 0.14, r).toFixed(3), freq: intBetween(2, 4, r), cycles: 1 };

  const spin = solveSpin(base, between(REV_SEC_MIN, REV_SEC_MAX, r));
  base.speed = spin.speed;
  base.layer.speedMul = spin.speedMul;

  return { params: normalizeSpiralParams(base), revSec: spin.revSec };
}

/* ---------------------------------------------------------------------------
 * THE RASTER BAKE — the no-WebGL floor, drawn instead of downloaded.
 *
 * This is the Loom's OWN 2D fallback renderer (dtrh/shared/loomSpiral.js
 * #drawSpiral, reached through its projectToV1) copied across and taught the
 * two things a v2 param set has that v1 did not: a radial background, and up to
 * three threads instead of two. Arms are filled annular wedge strips — cheap,
 * alias-friendly at any size, and no stroke-width tuning.
 *
 * IT DRAWS ONE FRAME, NOT AN ANIMATION. The pane's spin is the CSS keyframe
 * rotating the whole still, which is exactly what the retired GIFs' own frames
 * were doing and costs the compositor a transform instead of a decode. Nothing
 * in the field is rotationally asymmetric per-frame except the arms themselves,
 * so a rotating still and a re-rendered frame are the same picture.
 * ------------------------------------------------------------------------ */

/** v2's six styles down to the three radius easings the 2D path knows. */
const V1_STYLE = Object.freeze({
  log: 'log', arch: 'arch', ribbon: 'ribbon', golden: 'log', tunnel: 'arch', petal: 'ribbon',
});

function radiusAt(style, t) {
  if (style === 'log') return Math.pow(t, 1.75);      // tight centre, opening rim
  if (style === 'arch') return t;                     // archimedean — even bands
  return t * t * (3 - 2 * t);                         // ribbon — smoothstep swell
}

/**
 * Draw one still frame of `params` into a 2D context, `size` px square.
 *
 * Every ctx call that a minimal/stub context may not implement is guarded: this
 * runs on whatever WebView2 hands us and, in the self-test, on a recording
 * fake. A bake that cannot draw returns false and the caller keeps its old
 * image rather than showing a black square.
 *
 * @param {CanvasRenderingContext2D} ctx
 * @param {number} size square edge in pixels
 * @param {object} params NORMALIZED params (rollSpiral's `.params`)
 * @param {number} [phase] 0..1 inside the loop; a still bakes at 0
 * @returns {boolean}
 */
export function drawSpiralRaster(ctx, size, params, phase = 0) {
  if (!ctx || typeof ctx.fillRect !== 'function' || typeof ctx.arc !== 'function') return false;
  const q = normalizeSpiralParams(params);
  const L = q.layer;
  const c = size / 2;
  const maxR = c * 1.45;                        // overdraw the corners: no dead edges
  const arms = Math.max(1, L.arms);
  const armSpan = (Math.PI * 2) / arms;
  const litSpan = armSpan * L.duty;
  const twist = L.turns * Math.PI * 2;          // how far a band shears over the radius
  const rot = L.direction * phase * armSpan;
  const style = V1_STYLE[L.style] || 'log';
  // Radial resolution — how many annular strips the shear is approximated by.
  // The Loom uses size/6 unbounded, because it is baking a file the player will
  // stare at. This bakes at RASTER_PX and 160 strips x 10 arms is already 1600
  // path fills; past that the extra strips are finer than the 1.1px blur
  // .gg-spiral puts over them, so they cost time and change nothing.
  const rings = clamp(Math.round(size / 6), 48, 160);

  try {
    let bg = q.bg.color;
    if (q.bg.kind === 'radial' && typeof ctx.createRadialGradient === 'function') {
      const g = ctx.createRadialGradient(c, c, 0, c, c, maxR);
      g.addColorStop(0, q.bg.color);
      g.addColorStop(1, q.bg.outer);
      bg = g;
    }
    ctx.fillStyle = bg;
    ctx.fillRect(0, 0, size, size);
  } catch (_e) { return false; }

  try {
    for (let ring = 0; ring < rings; ring++) {
      const t0 = ring / rings, t1 = (ring + 1) / rings;
      const r0 = radiusAt(style, t0) * maxR;
      const r1 = radiusAt(style, t1) * maxR + 0.75;   // slight overlap: no moiré seams
      if (r1 <= 0.5) continue;
      const shear = twist * ((t0 + t1) / 2);
      for (let arm = 0; arm < arms; arm++) {
        const a0 = rot + arm * armSpan + shear;
        ctx.fillStyle = L.colors[arm % L.colors.length];
        ctx.beginPath();
        ctx.arc(c, c, r1, a0, a0 + litSpan, false);
        if (r0 > 0.5) ctx.arc(c, c, r0, a0 + litSpan, a0, true);
        else if (typeof ctx.lineTo === 'function') ctx.lineTo(c, c);
        ctx.closePath();
        ctx.fill();
      }
    }
  } catch (_e) { return false; }
  return true;
}

/**
 * ONE scratch canvas for every bake in the page. A 1280-square backing store is
 * ~6.5MB; minting a fresh one per variant would hold five of them alive for as
 * long as the session, for no reason — the PNG is the only thing worth keeping.
 *
 * NOTHING IS CACHED UNTIL A REAL 2D CONTEXT COMES BACK. The canvas and its
 * context are taken together and remembered together, because a host that
 * answers getContext('2d') with null (a WebView under memory pressure, a
 * locked-down embedder, a self-test fake that only serves WebGL) would
 * otherwise have its dud canvas remembered as THE scratch canvas for the life
 * of the page — and every bake after the condition cleared would still fail
 * against it. Refusing to cache a failure is what makes the next ask a retry.
 *
 * @returns {{cv: object, ctx: object, size: number}|null}
 */
let scratch = null;
function scratchTarget(size) {
  if (scratch && scratch.size === size) return scratch;
  if (typeof document === 'undefined' || typeof document.createElement !== 'function') return null;
  let cv = null;
  try { cv = document.createElement('canvas'); } catch (_e) { return null; }
  if (!cv || typeof cv.getContext !== 'function') return null;
  try { cv.width = size; cv.height = size; } catch (_e) { return null; }
  let ctx = null;
  try { ctx = cv.getContext('2d'); } catch (_e) { return null; }
  if (!ctx) return null;
  scratch = { cv, ctx, size };
  return scratch;
}

/**
 * Bake `params` to a PNG data URL, or '' on any host that cannot draw one (no
 * document, no canvas, no 2D context, a toDataURL the host refuses). '' is a
 * first-class answer: exec/spiral.js simply leaves the pane's background-image
 * alone, and a pane with no picture is invisible rather than wrong.
 */
export function bakeSpiralImage(params, size = RASTER_PX) {
  const target = scratchTarget(size);
  if (!target) return '';
  const cv = target.cv;
  if (!drawSpiralRaster(target.ctx, size, params, 0)) return '';
  if (typeof cv.toDataURL !== 'function') return '';
  try {
    const url = cv.toDataURL('image/png');
    return typeof url === 'string' && url.indexOf('data:image') === 0 ? url : '';
  } catch (_e) { return ''; }   // a tainted or oversized canvas throws; the bed survives it
}

/* ---------------------------------------------------------------------------
 * THE SESSION POOL
 * ------------------------------------------------------------------------ */

/**
 * Pre-generate a session's spirals and hand back a rotator.
 *
 * ROTATION, NOT RE-ROLLING. `next()` walks a shuffled order and reshuffles at
 * the end of each pass, refusing to open the new pass with the variant that
 * closed the old one — with five entries a plain re-shuffle would repeat back
 * to back roughly one pass in five, and a bed that "changes" into the same
 * spiral reads as a bug.
 *
 * `image()` bakes LAZILY and caches. A match that never drops to the raster
 * path (the normal case — the shader bed is on by default) pays nothing.
 *
 * @param {{rng?: () => number, count?: number, size?: number}} [opts]
 */
export function createSpiralSession(opts = {}) {
  const r = rngOf(opts.rng);
  const count = Math.max(1, (opts.count | 0) || SESSION_SIZE);
  const size = Math.max(64, (opts.size | 0) || RASTER_PX);

  const variants = [];
  for (let i = 0; i < count; i++) {
    const rolled = rollSpiral(r);
    let baked = null;
    variants.push({
      params: rolled.params,
      revSec: rolled.revSec,
      /** The PNG data URL for this variant, baked on first ask. '' if unbakeable.
       *  Closes over its own params rather than reading `this`, so a caller that
       *  pulls the method off the variant still gets the right picture. */
      image: () => {
        if (baked === null) baked = bakeSpiralImage(rolled.params, size);
        return baked;
      },
      /** Has this variant's still been baked yet? ('' counts — a host that
       *  cannot bake has ANSWERED, and asking it again would answer the same.)
       *  warmSpiralStills' progress, and the self-test's pin on it. */
      isBaked: () => baked !== null,
    });
  }

  let order = shuffled(variants.map((_v, i) => i), r);
  let at = 0;
  let last = -1;
  let picks = 0;

  function next() {
    if (at >= order.length) {
      order = shuffled(order, r);
      // Never open a pass with the variant that closed the last one.
      if (order.length > 1 && order[0] === last) { const t = order[0]; order[0] = order[1]; order[1] = t; }
      at = 0;
    }
    last = order[at++];
    picks++;
    return variants[last];
  }

  return {
    size,
    count: variants.length,
    all: () => variants.slice(),
    next,
    /** Any variant, no rotation state touched — the bubble-pop flick's pick. */
    any: () => variants[Math.min(variants.length - 1, (r() * variants.length) | 0)],
    /** How many variants have a resolved bake — warmSpiralStills' progress. */
    bakedCount: () => variants.reduce((n2, v) => n2 + (v.isBaked() ? 1 : 0), 0),
    /** How many times next() has handed one out. This is a COUNTER, not
     *  bookkeeping the rotation needs: exec/spiral.js used to roll TWICE for one
     *  fresh pane (ensurePane repicks, then the cue repicked again on top), and
     *  test/selftest-exec.js pins that it now rolls once. A repick is not free
     *  — it is a variant swap, a redraw and, before the warm, a raster bake. */
    picked: () => picks,
  };
}

/* ---------------------------------------------------------------------------
 * THE WARM — every bake, off the hot path.
 *
 * THE BUG THIS EXISTS FOR (iPhone 13 Pro Max, r8-20260805): a spiral payload
 * mounted its pane and then FROZE for a second or two before it started
 * turning. Nothing was loading and nothing was awaiting — the main thread was
 * baking, inside the cue, with the pane's own rAF loop queued behind it.
 * exec/spiral.js scheduled that bake one tick after the cue landed (see the
 * repick() banner there), which moves it out of the cue's call stack and into
 * the cue's first FRAMES, which is worse: the pane is already on screen and
 * visibly not moving.
 *
 * So the pool warms itself instead, ONE VARIANT PER IDLE SLICE. One per slice
 * matters as much as the idleness does: five bakes back to back is one long
 * task however idle the moment was, and a long task during a countdown is the
 * countdown skipping.
 *
 * `requestIdleCallback` WITH A TIMEOUT, and a plain timer under it. The timeout
 * is because a page that never goes idle would otherwise never warm at all, and
 * a still that arrives late is still ahead of the cue that needs it. The
 * fallback is not decoration either: WebKit was the last engine to ship
 * requestIdleCallback, so "the phone in the bug report" is exactly the host most
 * likely to be missing it. (node has neither, and every bake there is a no-op ''
 * anyway.) `schedule` is injectable so the self-test can drive the walk step by
 * step instead of racing timers.
 * ------------------------------------------------------------------------ */

/** How long an idle callback may be starved before it runs anyway. */
export const WARM_IDLE_TIMEOUT_MS = 3000;
/** Spacing on the no-requestIdleCallback fallback path. */
export const WARM_GAP_MS = 150;

function defaultWarmSchedule(fn) {
  try {
    if (typeof requestIdleCallback === 'function') {
      requestIdleCallback(fn, { timeout: WARM_IDLE_TIMEOUT_MS });
      return;
    }
  } catch (_e) { /* fall through to the timer */ }
  try {
    const t = setTimeout(fn, WARM_GAP_MS);
    // unref'd: a warm walking its five must never be the reason node's loop
    // (the self-tests') stays open.
    if (t && typeof t.unref === 'function') t.unref();
  } catch (_e) { /* a host with no timers simply never warms — every bake stays lazy */ }
}

/**
 * Bake a pool's stills during idle time. Returns a CANCEL — a session that has
 * been replaced must stop warming, or a page that opens two duels pays for the
 * dead pool's pictures.
 *
 * @param {{session?: object, schedule?: (fn: () => void) => void}} [opts]
 * @returns {() => void} cancel
 */
export function warmSpiralStills(opts = {}) {
  const sess = (opts && opts.session) || spiralSession();
  const schedule = (opts && typeof opts.schedule === 'function') ? opts.schedule : defaultWarmSchedule;
  const list = sess.all();
  let stopped = false;
  let i = 0;
  const stepOne = () => {
    if (stopped || i >= list.length) return;
    const v = list[i++];
    // image() caches, including the '' a host that cannot bake answers with, so
    // a second pass over an already-warm pool is free and a warm that overlaps
    // a fallback's own ask cannot double-bake.
    try { v.image(); } catch (_e) { /* an unbakeable variant is still a warmed one */ }
    if (i < list.length) schedule(stepOne);
  };
  schedule(stepOne);
  return () => { stopped = true; };
}

/* --- THE SHARED SESSION. exec/spiral.js (the bed) and exec/bubbles.js (the
   pop flick) must draw from the SAME five, or a popped spiral bubble would
   throw a spiral the match has never shown. Built lazily so a module-import
   sweep costs nothing, and re-rolled by beginSpiralSession() when a new duel
   builds its executor. ----------------------------------------------------- */
let session = null;
let cancelWarm = null;

/** The session pool, built on first ask. */
export function spiralSession() {
  if (!session) session = createSpiralSession();
  return session;
}

/**
 * Roll a fresh set for a new match. Called once, from createSpiral().
 *
 * Any warm still walking the OLD pool is cancelled here: those five stills are
 * about to be unreachable, and paying for a picture nobody can ask for again is
 * exactly the main-thread time this whole path was built to stop spending.
 */
export function beginSpiralSession(opts) {
  if (cancelWarm) { try { cancelWarm(); } catch (_e) { /* ignore */ } cancelWarm = null; }
  session = createSpiralSession(opts || {});
  return session;
}

/** Arm the idle warm for the shared pool. exec/spiral.js calls this at build. */
export function warmSharedSpirals(opts) {
  if (cancelWarm) { try { cancelWarm(); } catch (_e) { /* ignore */ } }
  cancelWarm = warmSpiralStills(Object.assign({ session: spiralSession() }, opts || {}));
  return cancelWarm;
}

/** The next spiral in the session's rotation. */
export function nextSpiral() { return spiralSession().next(); }

/** A baked still from the session, for exec/bubbles.js's popped-spiral flick. */
export function pickSpiralImage() { return spiralSession().any().image(); }

export default createSpiralSession;
