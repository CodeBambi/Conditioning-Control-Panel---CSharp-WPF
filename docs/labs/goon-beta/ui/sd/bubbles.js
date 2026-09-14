/* ============================================================================
 * ui/sd/bubbles.js — the bubble race.
 *
 * The swarm is IDENTICAL on both machines: every position, size, spawn offset
 * and drift vector comes from spec.bubbles (a pure function of the round seed),
 * never from the local bubble settings. Coordinates are normalized 0..1 against
 * the stage rect so the same swarm is fair on any aspect ratio.
 *
 * We score by INDEX, exactly as the round does — a popped bubble reports its
 * spec index and duplicates are ignored upstream.
 *
 * The peer's count is not transmitted until the verdict, so the twin counter
 * says `them ?` for the whole round rather than inventing a number.
 * ==========================================================================*/

/** Drift speed unit -> pixels per second. spec.driftSpeed runs ~0.4..1.6. */
const DRIFT_PX_PER_SEC = 42;
/** Base bubble diameter before spec.scale. */
const BASE_PX = 74;

export function createBubbles(ctx) {
  const { el, add, cls, text, sfx } = ctx;
  let node = null;
  let offFrame = null;
  let live = [];
  let startedAt = 0;
  let timeoutMs = 30000;
  let count = 0;
  let popped = 0;

  function build() {
    if (node) return node;
    node = el('div', 'gg-sd-bubbles');
    if (!node) return null;
    const head = add(node, el('div', 'gg-sd-bub-head gg-plate'));
    node._you = add(head, el('span', 'gg-sd-bub-you', 'you 0/0'));
    node._them = add(head, el('span', 'gg-sd-bub-them', 'them ?'));
    node._ring = add(head, el('i', 'gg-sd-bub-ring'));
    node._field = add(node, el('div', 'gg-sd-bub-field'));
    ctx.mountStage(node);
    return node;
  }

  function rectOf(el2) {
    try {
      if (el2 && typeof el2.getBoundingClientRect === 'function') {
        const r = el2.getBoundingClientRect();
        if (r && typeof r.width === 'number' && r.width > 0) return r;
      }
    } catch (_e) { /* stub DOM */ }
    const w = (typeof window !== 'undefined' && window && window.innerWidth) || 1280;
    const h = (typeof window !== 'undefined' && window && window.innerHeight) || 720;
    return { left: 0, top: 0, width: w, height: h, right: w, bottom: h };
  }

  function nowMs() {
    try {
      if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') return performance.now();
    } catch (_e) { /* fall through */ }
    return Date.now();
  }

  function start(spec) {
    const n = build();
    if (!n) return;
    stopLoop();
    clearField();

    count = Math.max(0, (spec && spec.count) | 0);
    timeoutMs = Math.max(1000, (spec && spec.timeoutMs) | 0);
    popped = 0;
    startedAt = nowMs();
    n.hidden = false;
    cls(n, 'is-in', true);
    paintCounter();

    const rect = rectOf(n._field);
    const list = (spec && spec.bubbles) || [];
    live = [];
    for (const b of list) {
      const size = Math.max(28, BASE_PX * (b.scale || 1));
      const node2 = el('button', 'gg-bub');
      if (!node2) continue;
      node2.type = 'button';
      add(node2, el('i', 'gg-bub-shine'));
      const rec = {
        index: b.index | 0,
        node: node2,
        size,
        x: (b.normX || 0) * Math.max(1, rect.width - size),
        y: (b.normY || 0) * Math.max(1, rect.height - size),
        vx: Math.cos(((b.driftAngleDeg || 0) * Math.PI) / 180) * (b.driftSpeed || 0) * DRIFT_PX_PER_SEC,
        vy: Math.sin(((b.driftAngleDeg || 0) * Math.PI) / 180) * (b.driftSpeed || 0) * DRIFT_PX_PER_SEC,
        spawnAt: Math.max(0, b.spawnOffsetMs | 0),
        alive: true,
        shown: false,
      };
      if (node2.style) {
        node2.style.width = size + 'px';
        node2.style.height = size + 'px';
        node2.style.left = rec.x + 'px';
        node2.style.top = rec.y + 'px';
      }
      node2.hidden = true;
      const onDown = (e) => { if (e && e.preventDefault) e.preventDefault(); pop(rec); };
      node2.addEventListener('pointerdown', onDown);
      rec.off = () => { try { node2.removeEventListener('pointerdown', onDown); } catch (_e) { /* gone */ } };
      add(n._field, node2);
      live.push(rec);
    }

    let last = nowMs();
    offFrame = ctx.onFrame(() => {
      const t = nowMs();
      const dt = Math.min(0.1, Math.max(0, (t - last) / 1000));
      last = t;
      step(dt, t - startedAt);
    });
  }

  function step(dt, elapsed) {
    const n = node;
    if (!n) return;
    const rect = rectOf(n._field);
    for (const rec of live) {
      if (!rec.alive) continue;
      if (!rec.shown) {
        if (elapsed < rec.spawnAt) continue;
        rec.shown = true;
        rec.node.hidden = false;
        cls(rec.node, 'is-in', true);
      }
      rec.x += rec.vx * dt;
      rec.y += rec.vy * dt;
      const maxX = Math.max(0, rect.width - rec.size);
      const maxY = Math.max(0, rect.height - rec.size);
      if (rec.x < 0) { rec.x = 0; rec.vx = -rec.vx; }
      if (rec.y < 0) { rec.y = 0; rec.vy = -rec.vy; }
      if (rec.x > maxX) { rec.x = maxX; rec.vx = -rec.vx; }
      if (rec.y > maxY) { rec.y = maxY; rec.vy = -rec.vy; }
      if (rec.node.style) {
        rec.node.style.left = rec.x + 'px';
        rec.node.style.top = rec.y + 'px';
      }
    }
    const pct = Math.max(0, Math.min(100, 100 - (elapsed / timeoutMs) * 100));
    if (n._ring && n._ring.style && n._ring.style.setProperty) n._ring.style.setProperty('--gg-ring', pct.toFixed(1) + '%');
  }

  function pop(rec) {
    if (!rec.alive) return;
    rec.alive = false;
    popped++;
    ctx.raise.pop(rec.index);
    sfx('gg-check-ok');
    cls(rec.node, 'is-pop', true);
    const plus = el('span', 'gg-bub-plus', '+1');
    if (plus && rec.node.parentNode && typeof rec.node.parentNode.appendChild === 'function' && plus.style) {
      plus.style.left = (rec.x + rec.size / 2) + 'px';
      plus.style.top = rec.y + 'px';
      rec.node.parentNode.appendChild(plus);
      setTimeout(() => { try { plus.remove(); } catch (_e) { /* gone */ } }, 700);
    }
    setTimeout(() => { try { rec.node.remove(); } catch (_e) { /* gone */ } }, 260);
    paintCounter();
  }

  function paintCounter() {
    if (!node) return;
    text(node._you, 'you ' + popped + '/' + count);
    text(node._them, 'them ?');
  }

  function clearField() {
    for (const rec of live) {
      try { rec.off && rec.off(); } catch (_e) { /* gone */ }
      try { rec.node.remove(); } catch (_e) { /* gone */ }
    }
    live = [];
  }

  function stopLoop() {
    if (typeof offFrame === 'function') { try { offFrame(); } catch (_e) { /* gone */ } }
    offFrame = null;
  }

  function end() {
    stopLoop();
    clearField();
    if (!node) return;
    cls(node, 'is-in', false);
    node.hidden = true;
  }

  return {
    start,
    end,
    dispose() {
      end();
      if (node) { try { node.remove(); } catch (_e) { /* gone */ } node = null; }
    },
  };
}
