/* ============================================================================
 * layers/blur.js - the board goes soft.
 *
 * A CSS blur on #stage, contributed through the shared stage-filter composer
 * so it stacks with the melt instead of overwriting it.
 *
 * THE HARD CAP IS THE POINT. RAMP_TUNING.blurMaxPx is 3px and this layer
 * re-clamps to it even if a caller hands it something larger: at 3px a piece is
 * still findable and a legal move is still physically possible. The ramp is
 * meant to make you play badly, not to make the game unplayable. Anything that
 * wants to actually hide the board (the spiral veil, the gif overlay, the video
 * card) is a separate layer the player can see past by waiting.
 * ==========================================================================*/

export function createBlur(ctx) {
  const cap = Number.isFinite(ctx.tuning && ctx.tuning.blurMaxPx) ? ctx.tuning.blurMaxPx : 3;
  let disposed = false;
  let px = 0;

  /** spec is schedule.sustainedFor().blur: { on, px }. */
  function set(spec) {
    if (disposed) return;
    const want = !!(spec && spec.on);
    // re-clamp here as well as in the schedule: this is the last line before
    // the pixels, and it is the one a future caller will forget
    px = want ? Math.min(cap, Math.max(0, spec.px || 0)) : 0;
    ctx.stageFilter.set('blur', px > 0.02 ? `blur(${px.toFixed(2)}px)` : '');
  }

  function clear() { px = 0; ctx.stageFilter.set('blur', ''); }

  return {
    set, clear,
    dispose() { disposed = true; clear(); },
    get px() { return px; },
    fire() {}, show() {}, grab() {}, move() {}, drop() {},
  };
}

export default createBlur;
