/* ============================================================================
 * layers/overlay.js - the fullscreen gif overlay.
 *
 * The last sustained layer to unlock (meter .70). One element covering the
 * whole plane with a single looping gif at a low alpha, re-picked every few
 * seconds so it never settles into wallpaper. It is the top of the ramp: by the
 * time this is on, the player is reading the board through a moving picture.
 *
 * Semi-transparent by design and click-through like everything else, so the
 * board underneath is dimmed and distracting rather than gone. With no gifs in
 * the pool it wears a generated pink noise tile instead of vanishing.
 * ==========================================================================*/

const REDRESS_MS = 6500;   // how often the overlay swaps its picture

export function createOverlay(ctx) {
  let el = null;
  let disposed = false;
  let on = false;
  let timer = 0;

  function ensure() {
    if (el || disposed) return el;
    el = ctx.el('div', 'pbp-overlay');
    ctx.mount(el);
    return el;
  }

  function dress() {
    if (!el || disposed) return;
    const url = ctx.tile();
    try { if (url) el.style.backgroundImage = `url("${url}")`; } catch { /* gone */ }
  }

  function arm() {
    if (timer || disposed) return;
    timer = setInterval(() => { if (on && !disposed) dress(); }, REDRESS_MS);
  }

  /** spec is schedule.sustainedFor().overlay: { on, alpha }. */
  function set(spec) {
    if (disposed) return;
    const want = !!(spec && spec.on);
    const alpha = want ? Math.min(1, Math.max(0, spec.alpha || 0)) : 0;
    if (want && !el) { ensure(); dress(); }
    if (want) arm();
    if (!el) return;
    try {
      el.style.opacity = String(alpha);
      el.classList.toggle('is-on', want);
    } catch { /* gone */ }
    on = want;
  }

  function clear() {
    on = false;
    if (el) { try { el.style.opacity = '0'; el.classList.remove('is-on'); } catch { /* gone */ } }
  }

  function dispose() {
    disposed = true;
    if (timer) { clearInterval(timer); timer = 0; }
    if (el) { try { el.remove(); } catch { /* gone */ } el = null; }
  }

  return { set, clear, dispose, get on() { return on; }, fire() {}, show() {}, grab() {}, move() {}, drop() {} };
}

export default createOverlay;
