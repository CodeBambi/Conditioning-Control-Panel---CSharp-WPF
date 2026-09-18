/* ============================================================================
 * stations/breakout/host-fx.js - the cabinet's fullscreen host effects, the
 * room's own five primitives (CONTRACT 10.13.B) fired at the game's big beats.
 * The page never depends on them: ctx.fx may be missing (dev harness, phone),
 * busy, or gated, and every call is fire-and-forget.
 *
 *   wall(n, key)      every wall: a soft pink wash with a dealt picture in the middle
 *   mantra(key)       a mantra wall: the slot's sub rule, the page spelt the word so the
 *                     host plays only the spiral that follows (fx.sub_pair, wordsShown)
 *   crack()           the crack, once a session: the Loom spiral over the field, then a wash
 *   shatterWall(box)  the fractured field shatters: the fullscreen picture grows out of it
 *   breakout(key)     the snap back to colour: a full-strength wash with a picture
 *   jackpot(key)      the hidden +5 brick: the picture grows from the centre, no dim
 *
 * Every fullscreen picture keeps gif_from below scale 1 so the host never dims the
 * field behind it: the ball keeps moving under every one of these.
 * ==========================================================================*/

export const PINK = '#ff5fa2', GOLD = '#e8c27a';
export const GAP_S = Object.freeze({ 'fx.wash': 2, 'fx.gif_from': 8, 'fx.loom_spiral': 20, 'fx.sub_pair': 12 });

/**
 * createHostFx({ fx, clock, reduced }) -> the six moments above plus `fire(id, symbols, args)`.
 * `clock()` is seconds (performance.now based by default) for the per-id throttle; `reduced` halves washes
 * and skips the spiral, the rest is the host's own reduced-motion recipe.
 */
export function createHostFx({ fx = null, clock = null, reduced = false } = {}) {
  const last = {};
  const now = () => (typeof clock === 'function' ? clock() : (typeof performance !== 'undefined' ? performance.now() : Date.now()) / 1000);
  const keys = (k) => (typeof k === 'string' && k ? [k] : []);

  function fire(id, symbols = [], args = null, gap = GAP_S[id] || 0) {
    if (typeof fx !== 'function') return false;
    const t = now();
    if (Object.prototype.hasOwnProperty.call(last, id) && t - last[id] < gap) return false;
    last[id] = t;
    try {
      const p = fx(id, symbols, args || undefined);
      if (p && typeof p.catch === 'function') p.catch(() => {});
    } catch (e) { return false; }
    return true;
  }
  const wash = (key, color, strength) => fire('fx.wash', keys(key), { color, strength: reduced ? strength * 0.5 : strength });

  return {
    fire,
    wall(n, key) { return wash(key, PINK, 0.55); },
    mantra(key) { return fire('fx.sub_pair', keys(key), { wordsShown: true }); },
    crack() {
      const a = reduced ? false : fire('fx.loom_spiral', [], { preset: 'screen', ms: 4200, alpha: 0.6 });
      const b = wash(null, PINK, 0.9);
      return a || b;
    },
    shatterWall(box, key) {
      const from = box && Number.isFinite(box.x) && Number.isFinite(box.w) && box.w >= 8 && box.h >= 8
        ? { x: Math.round(box.x), y: Math.round(box.y), w: Math.round(box.w), h: Math.round(box.h) } : undefined;
      return fire('fx.gif_from', keys(key), { from, ms: 1500, scale: 0.8 });
    },
    breakout(key) { return wash(key, PINK, 1); },
    jackpot(key) { return fire('fx.gif_from', keys(key), { ms: 1600, scale: 0.6 }, 30); },
  };
}
