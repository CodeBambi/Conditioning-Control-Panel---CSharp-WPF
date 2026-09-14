/* ============================================================================
 * ui/sd/staring.js — the round that should never arrive in v1.
 *
 * A staring contest needs two cameras. The web client advertises NoCam, the
 * ladder's kindFor() therefore falls back to the reaction duel, and the caps
 * intersection would drop the kind anyway. This module exists so that if a peer
 * (or a future cam build) ever DOES schedule one, the player gets an honest
 * swap card instead of a blank screen — and the round still resolves: with no
 * blink and no attention samples the round simply runs its clock out and both
 * sides survive, which the judge settles on attention progress.
 * ==========================================================================*/

export function createStaring(ctx) {
  const { el, add, cls, text } = ctx;
  let node = null;

  function build() {
    if (node) return node;
    node = el('div', 'gg-sd-swap gg-plate');
    if (!node) return null;
    node._head = add(node, el('h3', 'gg-sd-swap-head', 'no camera — reaction duel instead.'));
    node._line = add(node, el('p', 'gg-sd-swap-line', 'this one needs two cameras. nobody loses it.'));
    node._bar = add(node, el('i', 'gg-sd-swap-bar'));
    ctx.mountStage(node);
    return node;
  }

  function start(spec) {
    const n = build();
    if (!n) return;
    n.hidden = false;
    cls(n, 'is-in', true);
    const ms = Math.max(1000, (spec && spec.durationMs) | 0);
    text(n._line, 'this one needs two cameras. nobody loses it.');
    if (n._bar && n._bar.style) {
      n._bar.style.transition = 'none';
      n._bar.style.width = '100%';
      const run = () => {
        if (!n._bar || !n._bar.style) return;
        n._bar.style.transition = 'width ' + ms + 'ms linear';
        n._bar.style.width = '0%';
      };
      if (typeof requestAnimationFrame === 'function') requestAnimationFrame(run); else run();
    }
  }

  function end() {
    if (!node) return;
    cls(node, 'is-in', false);
    node.hidden = true;
  }

  return {
    start,
    end,
    dispose() {
      if (node) { try { node.remove(); } catch (_e) { /* gone */ } node = null; }
    },
  };
}
