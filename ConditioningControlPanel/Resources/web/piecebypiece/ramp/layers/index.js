/* ============================================================================
 * ramp/layers/index.js - the layer stack the ramp drives.
 *
 * This file is the SEAM. `attachRamp` never talks to a layer directly: it hands
 * this stack a heat number and a list of kinds to fire, and the stack owns the
 * DOM. That keeps the meter/schedule math (which is node-testable) apart from
 * the pixels (which are not).
 *
 * Right now every method is a counted no-op, so the ramp is fully wired and
 * fully testable before a single pixel exists. The real layers (flash, gif
 * rain, melt, blur, spiral, overlay, video card, glitch grab) land on top of
 * this surface without the ramp changing.
 * ==========================================================================*/

/**
 * createLayerStack(ctx) - ctx is { root, front, stage, media, tuning, rng }.
 * Every method must be safe to call with a missing root, stage or media.
 */
export function createLayerStack(ctx = {}) {
  const counts = { flash: 0, gifRain: 0, burst: 0, videoCard: 0, grab: 0, drop: 0 };
  const sustained = { melt: null, blur: null, spiral: null, overlay: null };
  let disposed = false;

  return {
    /** Fire one transient effect of `kind` at this heat. */
    oneshot(kind) {
      if (disposed) return;
      if (counts[kind] == null) counts[kind] = 0;
      counts[kind] += 1;
    },
    /** A capture kick: several one-shots at once, weighted by the burst spec. */
    burst(spec) {
      if (disposed || !spec) return;
      counts.burst += 1;
      for (let i = 0; i < (spec.flashes || 0); i++) this.oneshot('flash');
      for (let i = 0; i < (spec.gifs || 0); i++) this.oneshot('gifRain');
    },
    /** Turn a sustained layer on/off and retune it. `spec` comes from schedule. */
    setSustained(name, spec) {
      if (disposed || !(name in sustained)) return;
      sustained[name] = spec || null;
    },
    /** The video card: rises at the POV, holds, slides out the bottom. */
    videoCard() { if (!disposed) counts.videoCard += 1; },
    /** Drag handling: a glitch tile pinned to the piece under the cursor. */
    grab() { if (!disposed) counts.grab += 1; },
    dragmove() { /* per-frame; the real stack moves the tile here */ },
    drop() { if (!disposed) counts.drop += 1; },
    /** Snap everything off without a fade (suspend / dispose). */
    clear() {
      for (const k of Object.keys(sustained)) sustained[k] = null;
    },
    dispose() { disposed = true; this.clear(); },
    /** Read-only view for the harness and the smoke tests. */
    debug() { return { counts: { ...counts }, sustained: { ...sustained }, hasMedia: !!(ctx.media && ctx.media.size) }; },
  };
}

export default createLayerStack;
