// Bounded shared registry. Only player-moved GIFs sweep the live bubble field.
const bubbles = new Map();
export function registerBubble(node, pop) {
  bubbles.set(node, pop);
  return () => bubbles.delete(node);
}

// Segment against the bubble bounds expanded by the GIF half-size.
export function sweptHit(before, after, target) {
  if (!before || !after || !target || after.width <= 0 || target.width <= 0) return false;
  const x = before.left + before.width / 2, y = before.top + before.height / 2;
  const dx = after.left + after.width / 2 - x, dy = after.top + after.height / 2 - y;
  const hw = Math.max(before.width, after.width) / 2, hh = Math.max(before.height, after.height) / 2;
  let near = 0, far = 1;
  for (const [p, delta, low, high] of [
    [x, dx, target.left - hw, target.left + target.width + hw],
    [y, dy, target.top - hh, target.top + target.height + hh],
  ]) {
    if (Math.abs(delta) < 0.001) { if (p < low || p > high) return false; continue; }
    const a = (low - p) / delta, b = (high - p) / delta;
    near = Math.max(near, Math.min(a, b)); far = Math.min(far, Math.max(a, b));
    if (near > far) return false;
  }
  return true;
}

export function sweepBubbles(node, before) {
  if (!node || !node.isConnected || typeof node.getBoundingClientRect !== 'function') return { rect: null, hits: 0 };
  const rect = node.getBoundingClientRect();
  let hits = 0;
  if (typeof document !== 'undefined' && document.documentElement?.hasAttribute?.('data-gg-lock-active')) return { rect, hits };
  // Measure first, then pop: effect mutations cannot invalidate later reads.
  const struck = [];
  for (const [bubble, pop] of bubbles) {
    if (!bubble.isConnected) { bubbles.delete(bubble); continue; }
    if (typeof bubble.getBoundingClientRect !== 'function') continue;
    const b = bubble.getBoundingClientRect();
    if (sweptHit(before || rect, rect, b)) struck.push({ bubble, pop, b });
  }
  for (const { bubble, pop, b } of struck) {
    bubbles.delete(bubble);
    pop(b.left + b.width / 2, b.top + b.height / 2);
    hits++;
  }
  return { rect, hits };
}
