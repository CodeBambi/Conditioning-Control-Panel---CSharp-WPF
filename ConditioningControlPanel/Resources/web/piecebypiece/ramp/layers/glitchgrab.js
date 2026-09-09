/* ============================================================================
 * layers/glitchgrab.js - a glitching tile stuck to the piece you are dragging.
 *
 * On `grab` a small tile appears over the projected screen position of the
 * dragged piece and shudders there; every `dragmove` moves it; `drop` removes
 * it. So the one thing the player most needs to see - the piece in their hand -
 * is the one thing wearing a glitch.
 *
 * ONE reused element (grab/drop can fire many times a game), moved by transform
 * only, so a per-frame dragmove never touches layout. The tile is deliberately
 * SMALLER than the piece it covers is tall: it should sit on the piece like a
 * sticker, not blot out the destination square. That is why it is sized from
 * the board (one square's width on screen) rather than from a pixel count.
 *
 * `screen` comes from the board's own projection (A's projectSquare feeds it),
 * so this layer never needs to know where anything is in 3D.
 * ==========================================================================*/

export function createGlitchGrab(ctx) {
  const t = (ctx.tuning && ctx.tuning.glitchGrab) || { sizePx: 96, alpha: 0.85 };
  let el = null;
  let disposed = false;
  let holding = false;
  let raf = 0;
  let pending = null;

  /**
   * How wide the sticker should be right now. A piece is about one square
   * tall, so the sticker is measured against a square rather than pinned to a
   * pixel count: the same window can hold a very different board. The pixel
   * value in the tuning is the fallback for a caller with no board to ask.
   */
  function sizePx() {
    const square = typeof ctx.squarePx === 'function' ? ctx.squarePx() : 0;
    if (!(square > 0)) return t.sizePx || 96;
    const want = square * (t.squareShare || 0.82);
    return Math.round(Math.min(t.maxPx || 132, Math.max(t.minPx || 52, want)));
  }

  function ensure() {
    if (el || disposed) return el;
    el = ctx.el('div', 'pbp-grab');
    if (el) ctx.mount(el);
    return el;
  }

  /** Write the transform on the next frame, so a 120Hz drag writes once. */
  function flush() {
    raf = 0;
    if (!el || !pending || disposed) return;
    // hung up and to the left of the pointer rather than centred on it: the
    // sticker is wider than a square, and dead-centre it would cover the very
    // square the player is aiming at. Distraction yes, blindfold no.
    try { el.style.transform = `translate3d(${pending.x}px, ${pending.y}px, 0) translate(-82%, -82%)`; } catch { /* gone */ }
  }

  function moveTo(screen) {
    if (!screen || !Number.isFinite(screen.x) || !Number.isFinite(screen.y)) return;
    pending = { x: Math.round(screen.x), y: Math.round(screen.y) };
    if (raf) return;
    try { raf = requestAnimationFrame(flush); } catch { flush(); }
  }

  function grab(p) {
    if (disposed) return;
    const node = ensure();
    if (!node) return;
    const url = ctx.tile();
    try {
      // re-measured every grab: the camera swings between turns
      node.style.setProperty('--pbp-grab-size', sizePx() + 'px');
      if (url) node.style.backgroundImage = `url("${url}")`;
      node.style.opacity = String(t.alpha == null ? 0.85 : t.alpha);
      node.classList.add('is-on');
    } catch { /* gone */ }
    holding = true;
    moveTo(p && p.screen);
  }

  function move(p) { if (holding && !disposed) moveTo(p && p.screen); }

  function drop() {
    holding = false;
    if (el) { try { el.classList.remove('is-on'); el.style.opacity = '0'; } catch { /* gone */ } }
  }

  function clear() { drop(); }

  function dispose() {
    disposed = true;
    if (raf) { try { cancelAnimationFrame(raf); } catch { /* ignore */ } raf = 0; }
    if (el) { try { el.remove(); } catch { /* gone */ } el = null; }
  }

  return { grab, move, drop, clear, dispose, get holding() { return holding; }, set() {}, fire() {}, show() {} };
}

export default createGlitchGrab;
