/* ============================================================================
 * layers/spiral.js - the fullscreen spiral veil.
 *
 * The DtRH .sf-pfx-spiral posture, ported: ONE element for the whole game,
 * screen-blended, spinning slowly, over-scanned so a rotation never bares a
 * corner. What is ported is the structure and the envelope, not the artwork -
 * the image comes from the player's own pool, and when the pool has no gif the
 * element falls back to a CSS pinwheel so Piece by Piece never has to reach
 * into another game's asset folder.
 *
 * Unlike melt and blur this one PULSES rather than sitting on: it unlocks at
 * meter .55 and then comes and goes, and what grows with the meter is how long
 * each pass holds. A veil you can wait out is a veil the player learns to wait
 * out, which is the whole trick.
 * ==========================================================================*/

export function createSpiral(ctx) {
  const t = (ctx.tuning && ctx.tuning.spiral) || { gapMs: 9000 };
  let el = null;
  let disposed = false;
  let showing = false;
  let lastEndAt = -Infinity;
  let holdTimer = 0;
  let spec = { on: false, alpha: 0, holdMs: 0 };

  const now = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());

  function ensure() {
    if (el || disposed) return el;
    el = ctx.el('div', 'pbp-spiral');
    ctx.mount(el);
    return el;
  }

  /** Re-pick the imagery for this pass: a pool gif, else the CSS pinwheel. */
  function dress(node) {
    const url = ctx.tile();
    try {
      if (url) { node.style.backgroundImage = `url("${url}")`; node.classList.remove('is-drawn'); }
      else { node.style.backgroundImage = ''; node.classList.add('is-drawn'); }
      // a hotter veil turns faster, which reads as being pulled in harder
      node.style.setProperty('--pbp-spiral-ms', Math.round(20000 - (spec.alpha || 0) * 12000) + 'ms');
    } catch { /* the veil went away under us */ }
  }

  function hide() {
    showing = false;
    lastEndAt = now();
    if (holdTimer) { clearTimeout(holdTimer); holdTimer = 0; }
    if (el) { try { el.style.opacity = '0'; el.classList.remove('is-on'); } catch { /* gone */ } }
  }

  function show() {
    const node = ensure();
    if (!node) return;
    showing = true;
    dress(node);
    try { node.style.opacity = String(spec.alpha || 0); node.classList.add('is-on'); } catch { /* gone */ }
    if (holdTimer) clearTimeout(holdTimer);
    holdTimer = setTimeout(() => { holdTimer = 0; if (!disposed) hide(); }, Math.max(600, spec.holdMs || 1400));
  }

  /** spec is schedule.sustainedFor().spiral: { on, alpha, holdMs }. */
  function set(next) {
    if (disposed) return;
    spec = next || { on: false, alpha: 0, holdMs: 0 };
    if (!spec.on) { if (showing) hide(); return; }
    if (showing) {
      // a retune mid-pass just refreshes the strength, it never restarts
      if (el) { try { el.style.opacity = String(spec.alpha || 0); } catch { /* gone */ } }
      return;
    }
    if (now() - lastEndAt >= (t.gapMs || 9000)) show();
  }

  function clear() { hide(); }

  function dispose() {
    disposed = true;
    if (holdTimer) { clearTimeout(holdTimer); holdTimer = 0; }
    if (el) { try { el.remove(); } catch { /* gone */ } el = null; }
  }

  return { set, clear, dispose, get showing() { return showing; }, fire() {}, show() {}, grab() {}, move() {}, drop() {} };
}

export default createSpiral;
