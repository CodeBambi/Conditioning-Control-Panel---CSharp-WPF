/* ============================================================================
 * layers/melt.js - the pink melt: the board bleeds colour and softens.
 *
 * Two halves, the DtRH/Arcademy brain-drain look split by what it acts on:
 *
 *   1. a CSS FILTER on #stage (saturate + hue-rotate toward pink + a little
 *      contrast loss) contributed through the shared stage-filter composer, so
 *      it stacks with blur rather than overwriting it;
 *   2. a DOM VEIL in #fx - the .sf-pfx-pink radial wash, screen-blended, with a
 *      slow breathing drift so it reads as a melt and not as a flat gel.
 *
 * ONE element for the whole game (the DtRH holdOn posture): a retune writes
 * opacity, it never appends a second veil.
 * ==========================================================================*/

export function createMelt(ctx) {
  let veil = null;
  let disposed = false;
  let on = false;

  function ensure() {
    if (veil || disposed) return veil;
    veil = ctx.el('div', 'pbp-melt');
    ctx.mount(veil);
    return veil;
  }

  /** spec is schedule.sustainedFor().melt: { on, alpha }. */
  function set(spec) {
    if (disposed) return;
    const want = !!(spec && spec.on);
    const alpha = want ? Math.min(1, Math.max(0, spec.alpha || 0)) : 0;

    // the stage filter: how far the board has bled. Kept gentle - the melt is
    // meant to make the board unpleasant to read, never to hide which square
    // a piece is on (that job belongs to the layers above it).
    ctx.stageFilter.set('melt', want
      ? `saturate(${(1 + alpha * 1.5).toFixed(2)}) hue-rotate(${(-alpha * 26).toFixed(1)}deg) contrast(${(1 - alpha * 0.22).toFixed(2)})`
      : '');

    const el = want ? ensure() : veil;
    if (!el) return;
    try {
      el.style.opacity = String(alpha);
      el.classList.toggle('is-on', want);
      // the drift slows as it deepens: a fast wobble reads as noise, a slow one
      // as the board sinking
      el.style.setProperty('--pbp-melt-ms', Math.round(9000 - alpha * 3200) + 'ms');
    } catch { /* the veil went away under us */ }
    on = want;
  }

  function clear() {
    on = false;
    ctx.stageFilter.set('melt', '');
    if (veil) { try { veil.style.opacity = '0'; veil.classList.remove('is-on'); } catch { /* gone */ } }
  }

  function dispose() {
    disposed = true;
    clear();
    if (veil) { try { veil.remove(); } catch { /* gone */ } veil = null; }
  }

  return { set, clear, dispose, get on() { return on; }, fire() {}, show() {}, grab() {}, move() {}, drop() {} };
}

export default createMelt;
