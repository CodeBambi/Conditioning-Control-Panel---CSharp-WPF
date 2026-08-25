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
  /* W3 P1-17: the meter re-paints on every repaint, not only on a change, so
   * the ladder needs a memory of its own or a still board keeps ringing. */
  let lastLit = -1;

  /* ---- stamp ------------------------------------------------------------- */
  /** opts: { label, tone:'good'|'bad'|'gild', variant, durMs, sfx, dom }
   *  `dom:false` (2026-08-26, the double-stamp fix): the shell's ceremony
   *  module renders its own arc-stamp at the caller's target and delegates
   *  here for the BEAT - the fx event and the cue. Until now both sides
   *  appended a node, so every delegated stamp rendered twice (once here in
   *  the fx layer, once at the target). The shell now asks for the beat
   *  alone; a direct caller that says nothing keeps the node, as ever. */
  function stamp(opts = {}) {
    if (!hasDom()) return null;
    const variant = ctx.variant('stamp', 0.6, opts.variant);
    let node = null;
    if (opts.dom !== false) {
      node = document.createElement('div');
      const tone = opts.tone === 'bad' ? ' ae-stamp-bad' : (opts.tone === 'gild' || variant.name === 'gild' ? ' ae-stamp-gild' : '');
      node.className = 'ae-stamp' + tone;
      node.textContent = String(opts.label == null ? '' : opts.label);
      ctx.layers.front.appendChild(node);
      ctx.timers.own(node);
      ctx.timers.after(opts.durMs == null ? 1000 : opts.durMs, () => ctx.timers.release(node));
    }
    ctx.fx('stamp', variant.name);
    /* W3 P0-22: the arm and the thunk used to land on one frame, so the loudest
     * beat in the school arrived from nowhere. The paper is the arm - it plays
     * NOW - and the stamp follows 180ms later, which is the wrist. Stamps are
     * rare, so there is no throttle here and none is wanted. */
    ctx.sfx('paper', 0.2);
    const hit = opts.sfx || (opts.tone === 'bad' ? 'stamp_bad' : 'stamp');
    ctx.timers.after(180, () => ctx.sfx(hit, 0.55, { duck: 'voice' }));
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
    /* W3 P1-17: the ladder climbs in PITCH, not in level - a streak getting
     * louder is a streak shouting, and the meter repaints often enough that a
     * level ramp read as a fault. One semitone per lit segment off the same
     * recipe, flat level, and only on the frame the count actually moved. */
    if (spec.visible && spec.lit > 0 && spec.lit !== lastLit) {
      ctx.sfx('streak', 0.3, { pitch: Math.pow(2, spec.lit / 12) });
    }
    lastLit = spec.visible ? spec.lit : -1;
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
      /* W3 P0-23: the room going dark is the anticipation, so it takes the air
       * AND the spotlight duck off the payout below - the duck belongs to the
       * beat that clears the stage, not to the one that fills it. */
      ctx.sfx('whoosh', 0.25, { duck: 'spotlight' });
    }
    const glow = document.createElement('div');
    glow.className = 'ae-jackpot';
    ctx.layers.front.appendChild(glow);
    ctx.timers.own(glow);
    ctx.timers.after(spec.shimmerMs + 200, () => ctx.timers.release(glow));

    /* W3 P0-23: every burst after the first was silent, so a four-burst jackpot
     * sounded like a one-burst one. A chime per burst, rising, capped at four,
     * and reduced motion collapses to the single burst it already draws. */
    const BURST_PITCH = [1, 1.15, 1.3, 1.45];
    const bursts = Math.min(4, ctx.reduced() ? 1 : spec.bursts);
    for (let b = 0; b < bursts; b++) {
      ctx.timers.after(b * 220, () => {
        ctx.sfx('chime', 0.3, { pitch: BURST_PITCH[b] || 1.45 });
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
    ctx.sfx(opts.sfx || 'jackpot', clamp01(0.6 + 0.4 * i));
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
    /* W3 P2-9: the riser climbed for 520ms and then resolved into nothing. A
     * quiet bump lands on top of it - a tease has to close, and a near miss is
     * a loss, so it closes under the error ceiling. */
    ctx.timers.after(520, () => ctx.sfx('bump', 0.1));
    return { kind: 'near_miss', intensity: i, spec };
  }

  return {
    stamp,
    streak_meter: streakMeter,
    jackpot,
    near_miss: nearMiss,
    reset() { meterEl = null; segs = []; lastLit = -1; },
  };
}

export default createCeremonies;
