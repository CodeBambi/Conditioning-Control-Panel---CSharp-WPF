/* ============================================================================
 * engine/ceremonies.js — the reward ceremonies.
 *
 *   stamp · streak_meter · jackpot · near_miss
 *
 * SYNTHESIS amendment 10: the success-stamp and the 10-segment streak meter are
 * SHELL PRIMITIVES that games skin, never fork. They live in the engine so the
 * shell (shell/ceremonies.js) and every game share one implementation; the
 * variable-ratio canon that decides WHICH ceremony fires lives in
 * engine/schedule.js (ported from intake/core/reward.js).
 *
 * Magnitudes route through the caps vector like every other effect, and the
 * jackpot shimmer is strobe-class, so it also multiplies by effectIntensity.
 * ==========================================================================*/

import { clamp01 } from '../core/caps.js';
import { streakMeterSpec, jackpotSpec, nearMissSpec } from './curves.js';
import { rand, hasDom } from './util.js';

export function createCeremonies(ctx) {
  let meterEl = null;
  let segs = [];

  /* ---- stamp ------------------------------------------------------------- */
  /** opts: { label, tone:'good'|'bad'|'gild', variant, durMs, sfx } */
  function stamp(opts = {}) {
    if (!hasDom()) return null;
    const variant = ctx.variant('stamp', 0.6, opts.variant);
    const node = document.createElement('div');
    const tone = opts.tone === 'bad' ? ' ae-stamp-bad' : (opts.tone === 'gild' || variant.name === 'gild' ? ' ae-stamp-gild' : '');
    node.className = 'ae-stamp' + tone;
    node.textContent = String(opts.label == null ? '' : opts.label);
    ctx.layers.front.appendChild(node);
    ctx.timers.own(node);
    ctx.fx('stamp', variant.name);
    ctx.sfx(opts.sfx || (opts.tone === 'bad' ? 'stamp_bad' : 'stamp'), 0.55, { duck: 'voice' });
    ctx.timers.after(opts.durMs == null ? 1000 : opts.durMs, () => ctx.timers.release(node));
    return { kind: 'stamp', variant: variant.name, node };
  }

  /* ---- streak_meter (10 segments, hidden under 2) ------------------------- */
  /** opts: { streak, anchor } */
  function streakMeter(opts = {}) {
    if (!hasDom()) return null;
    const spec = streakMeterSpec(opts.streak);
    if (!meterEl) {
      meterEl = document.createElement('div');
      meterEl.className = 'ae-meter';
      for (let i = 0; i < spec.segments; i++) {
        const s = document.createElement('div');
        s.className = 'ae-seg';
        meterEl.appendChild(s);
        segs.push(s);
      }
      (opts.anchor && opts.anchor.appendChild ? opts.anchor : ctx.layers.front).appendChild(meterEl);
      ctx.timers.own(meterEl);
    }
    meterEl.classList.toggle('ae-meter-on', spec.visible);
    for (let i = 0; i < segs.length; i++) {
      segs[i].classList.toggle('ae-seg-lit', i < spec.lit);
      segs[i].style.setProperty('--ae-glow', String(spec.glow));
    }
    if (spec.visible && spec.lit > 0) ctx.sfx('streak', clamp01(0.25 + 0.05 * spec.lit));
    ctx.fx('streak_meter', 'segments');
    return { kind: 'streak_meter', lit: spec.lit, visible: spec.visible, node: meterEl };
  }

  /* ---- jackpot ----------------------------------------------------------- */
  /** opts: { intensity, sfx, particles } */
  function jackpot(opts = {}) {
    if (!hasDom()) return null;
    const i = ctx.strobe(clamp01(opts.intensity == null ? 0.8 : opts.intensity));
    const spec = jackpotSpec(i);
    if (!ctx.reduced() && ctx.motion() > 0) {
      const dim = document.createElement('div');
      dim.className = 'ae-dim';
      ctx.layers.front.appendChild(dim);
      ctx.timers.own(dim);
      ctx.timers.after(spec.dimMs + 120, () => ctx.timers.release(dim));
    }
    const glow = document.createElement('div');
    glow.className = 'ae-jackpot';
    ctx.layers.front.appendChild(glow);
    ctx.timers.own(glow);
    ctx.timers.after(spec.shimmerMs + 200, () => ctx.timers.release(glow));

    const bursts = ctx.reduced() ? 1 : spec.bursts;
    for (let b = 0; b < bursts; b++) {
      ctx.timers.after(b * 220, () => {
        const cx = rand(ctx.rng, 25, 75);
        const cy = rand(ctx.rng, 30, 70);
        const n = ctx.reduced() ? 4 : spec.particlesPerBurst;
        for (let p = 0; p < n; p++) {
          const s = document.createElement('div');
          s.className = 'ae-spark';
          s.style.setProperty('--ae-x', cx.toFixed(1) + '%');
          s.style.setProperty('--ae-y', cy.toFixed(1) + '%');
          s.style.setProperty('--ae-dx', Math.round(rand(ctx.rng, -160, 160)) + 'px');
          s.style.setProperty('--ae-dy', Math.round(rand(ctx.rng, -140, 90)) + 'px');
          s.style.setProperty('--ae-dur', Math.round(rand(ctx.rng, 700, 1200)) + 'ms');
          ctx.layers.front.appendChild(s);
          ctx.timers.own(s);
          ctx.timers.after(1400, () => ctx.timers.release(s));
        }
      });
    }
    ctx.fx('jackpot', 'shimmer');
    ctx.sfx(opts.sfx || 'jackpot', clamp01(0.6 + 0.4 * i), { duck: 'spotlight' });
    // the jackpot FORCES a garnish (intake: jackpot forces drain|spiral)
    if (opts.garnish !== false) ctx.forceGarnish(['drain', 'spiral'], i);
    return { kind: 'jackpot', intensity: i, spec };
  }

  /* ---- near_miss --------------------------------------------------------- */
  /** opts: { intensity } — barely-there by design; the tease, not a payout. */
  function nearMiss(opts = {}) {
    if (!hasDom()) return null;
    const i = clamp01(opts.intensity == null ? 0.4 : opts.intensity);
    const spec = nearMissSpec(ctx.magnitude(i));
    const node = document.createElement('div');
    node.className = 'ae-nearmiss';
    node.style.setProperty('--ae-alpha', String(spec.alpha));
    ctx.layers.front.appendChild(node);
    ctx.timers.own(node);
    ctx.timers.after(spec.durMs + 120, () => ctx.timers.release(node));
    if (!ctx.reduced()) {
      for (let p = 0; p < spec.particles; p++) {
        const s = document.createElement('div');
        s.className = 'ae-spark';
        s.style.setProperty('--ae-x', Math.round(rand(ctx.rng, 35, 65)) + '%');
        s.style.setProperty('--ae-y', Math.round(rand(ctx.rng, 45, 70)) + '%');
        s.style.setProperty('--ae-dx', Math.round(rand(ctx.rng, -50, 50)) + 'px');
        s.style.setProperty('--ae-dy', Math.round(rand(ctx.rng, -60, -10)) + 'px');
        s.style.setProperty('--ae-col', 'var(--ae-lav)');
        ctx.layers.front.appendChild(s);
        ctx.timers.own(s);
        ctx.timers.after(1100, () => ctx.timers.release(s));
      }
    }
    ctx.fx('near_miss', 'tease');
    ctx.sfx('near_miss', clamp01(0.2 + 0.3 * i));
    return { kind: 'near_miss', intensity: i, spec };
  }

  return {
    stamp,
    streak_meter: streakMeter,
    jackpot,
    near_miss: nearMiss,
    reset() { meterEl = null; segs = []; },
  };
}

export default createCeremonies;
