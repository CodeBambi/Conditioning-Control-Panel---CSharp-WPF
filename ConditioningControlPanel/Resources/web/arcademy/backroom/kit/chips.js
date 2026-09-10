/* ============================================================================
 * backroom/kit/chips.js - the money, and how it moves.
 *
 * Three things, and every machine on the floor uses all three:
 *   balanceChip()  the little "chips: 2,000" plate in a header
 *   chipStack()    the drawn stack in front of the player
 *   bank()         THE BANK: chips arcing from one place to another
 *
 * BANK GOES BOTH WAYS on purpose. A stake leaving for the table is the same
 * ceremony as a win coming home, played backwards, because a room where money
 * only ever arrives with a flourish is a room that is lying about what it is.
 *
 * REDUCED MOTION IS INSTANT, not slow (trap 66/92). Every animation in here is
 * driven by requestAnimationFrame rather than CSS, so the global freeze at the
 * bottom of styles.css cannot reach it - which means this file has to check
 * `reduced` itself and simply land on the final number. It does, on every path.
 * ==========================================================================*/

const nf = (typeof Intl !== 'undefined' && Intl.NumberFormat) ? new Intl.NumberFormat('en-US') : null;

/** 2000 -> "2,000". The one place a chip count becomes text. */
export function fmtChips(n) {
  const v = Math.round(Number(n) || 0);
  try { return nf ? nf.format(v) : String(v); } catch { return String(v); }
}

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** rAF that still ticks in node and in a headless probe with no frame clock -
 *  a missing rAF must never mean "the number never lands" (trap 36's lesson). */
function raf(fn) {
  if (typeof requestAnimationFrame === 'function') return requestAnimationFrame(fn);
  return setTimeout(() => fn(Date.now()), 16);
}
function unraf(h) {
  if (typeof cancelAnimationFrame === 'function') { try { cancelAnimationFrame(h); return; } catch { /* noop */ } }
  try { clearTimeout(h); } catch { /* noop */ }
}

const easeOut = (t) => 1 - Math.pow(1 - t, 3);

/**
 * countUp(node, from, to, {ms, reduced}) -> stop()
 *
 * Walks the printed number. Reduced motion writes the destination on the first
 * tick and stops - the count-up is decoration, the NUMBER is the information,
 * and the information must never be withheld for a second and a half.
 */
export function countUp(node, from, to, opts) {
  const o = opts || {};
  const end = Math.round(Number(to) || 0);
  if (!node) return () => {};
  const write = (v) => { node.textContent = fmtChips(v); };
  if (o.reduced || !(Number(o.ms) > 0)) { write(end); return () => {}; }
  const start = Math.round(Number(from) || 0);
  if (start === end) { write(end); return () => {}; }
  const ms = Number(o.ms);
  /* ONE CLOCK, READ THE SAME WAY AT BOTH ENDS. The stamp requestAnimationFrame
   * hands its callback is NOT always on the same origin as performance.now (a
   * headless probe on a virtual-time budget is the case that caught this), and
   * mixing the two made `t` land outside 0..1 - which painted a MINUS BALANCE
   * for a frame. A balance may never read wrong, not even for one frame. */
  const clock = () => ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now());
  const t0 = clock();
  let handle = null;
  let belt = null;
  let stopped = false;
  let frames = 0;
  const land = () => { if (!stopped) { stopped = true; write(end); } };
  const step = () => {
    if (stopped) return;
    frames += 1;
    /* THE FRAME FLOOR, and it is not belt-and-braces, it is the brace itself.
     * A clock that does not move (a headless probe on a virtual-time budget is
     * the one that caught this) leaves a pure clock-driven rAF loop spinning
     * for ever, which ALSO starves the setTimeout below it, because a renderer
     * with a frame permanently pending never goes idle. Counting frames as a
     * second, slower hand means the count always finishes: whichever hand has
     * gone further wins, so a real clock still drives a real animation. */
    const byClock = (clock() - t0) / ms;
    const byFrame = (frames * 16) / ms;
    const t = Math.max(0, Math.min(1, Math.max(byClock, byFrame)));
    write(Math.round(start + (end - start) * easeOut(t)));
    if (t < 1) handle = raf(step); else stopped = true;
  };
  handle = raf(step);
  // The belt: no frame clock, a backgrounded tab, a probe that never paints -
  // the NUMBER still arrives, because the number was the information.
  belt = setTimeout(land, ms + 240);
  return function stop() {
    stopped = true;
    write(end);
    if (handle != null) unraf(handle);
    try { clearTimeout(belt); } catch { /* noop */ }
  };
}

/**
 * balanceChip({ label, value, reduced }) -> { el, set(n, {animate}), value() }
 *
 * The header plate. `set` count-ups by default and thuds the plate so a change
 * is felt even when the eye was elsewhere.
 */
export function balanceChip(opts) {
  const o = opts || {};
  const root = el('span', 'bk-bal');
  const name = el('i', 'bk-bal-mark');
  name.setAttribute('aria-hidden', 'true');
  const num = el('b', 'bk-bal-n', fmtChips(o.value));
  const cap = el('span', 'bk-bal-label', o.label == null ? '' : String(o.label));
  root.appendChild(name);
  root.appendChild(num);
  root.appendChild(cap);
  root.setAttribute('role', 'status');

  let value = Math.round(Number(o.value) || 0);
  let stop = () => {};
  return {
    el: root,
    value: () => value,
    set(n, setOpts) {
      const s = setOpts || {};
      const next = Math.round(Number(n) || 0);
      const prev = value;
      value = next;
      try { stop(); } catch { /* noop */ }
      const reduced = s.reduced != null ? !!s.reduced : !!o.reduced;
      stop = countUp(num, prev, next, { ms: s.animate === false ? 0 : 520, reduced });
      // The thud is a CLASS the sheet animates, so the global reduced freeze
      // already flattens it - no second guard needed here.
      if (next !== prev) {
        root.classList.remove('bk-thud');
        void root.offsetWidth;
        root.classList.add('bk-thud');
        root.classList.toggle('bk-down', next < prev);
      }
      root.setAttribute('aria-label', (o.label || '') + ' ' + fmtChips(next));
    },
  };
}

/** How many drawn chips a count is worth. A stack that grew one disc per chip
 *  would be a thousand nodes; the floor reads the NUMBER and the stack is the
 *  feeling, so it saturates at ten discs and says the rest in the plate. */
function discsFor(n) {
  const v = Math.max(0, Math.round(Number(n) || 0));
  if (!v) return 0;
  return Math.max(1, Math.min(10, Math.round(Math.log10(v + 1) * 3.2)));
}

/**
 * chipStack({ value, reduced }) -> { el, set(n), rect() }
 * The drawn pile. Purely decorative and never authoritative.
 */
export function chipStack(opts) {
  const o = opts || {};
  const root = el('span', 'bk-stack');
  root.setAttribute('aria-hidden', 'true');
  let discs = -1;
  function set(n) {
    const want = discsFor(n);
    if (want === discs) return;
    discs = want;
    root.textContent = '';
    for (let i = 0; i < want; i++) {
      const d = el('i', 'bk-disc' + (i % 3 === 0 ? ' bk-disc-hot' : ''));
      d.style.setProperty('--bk-i', String(i));
      root.appendChild(d);
    }
  }
  set(o.value);
  return {
    el: root,
    set,
    rect: () => (root.getBoundingClientRect ? root.getBoundingClientRect() : null),
  };
}

function centreOf(target) {
  if (!target) return null;
  const r = (typeof target.getBoundingClientRect === 'function')
    ? target.getBoundingClientRect()
    : (target.el && typeof target.el.getBoundingClientRect === 'function' ? target.el.getBoundingClientRect() : null);
  if (!r) return null;
  return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
}

/**
 * bank(from, to, { count, ms, reduced, host }) -> Promise<void>
 *
 * THE BANK. Arcs a handful of discs from one node to another and resolves when
 * the last one lands, so a caller can chain the number onto the arrival.
 *
 * It resolves IMMEDIATELY and draws nothing when motion is reduced, when either
 * end cannot be measured (a detached node, a node double in a test) or when
 * there is no document to hang the flight layer on. The caller's `.then` still
 * runs, so a room can never be left waiting on a beat that was refused.
 */
export function bank(from, to, opts) {
  const o = opts || {};
  const a = centreOf(from);
  const b = centreOf(to);
  if (o.reduced || !a || !b || typeof document === 'undefined' || !document.body) return Promise.resolve();

  const count = Math.max(1, Math.min(9, Math.round(Number(o.count) || 5)));
  const ms = Math.max(180, Math.min(1200, Number(o.ms) || 620));
  const layer = el('div', 'bk-flight');
  layer.setAttribute('aria-hidden', 'true');
  document.body.appendChild(layer);

  return new Promise((resolve) => {
    let landed = 0;
    let handle = null;
    let killed = false;
    const nodes = [];
    for (let i = 0; i < count; i++) {
      const d = el('i', 'bk-fly');
      // A little scatter so five discs do not read as one disc drawn five times.
      d.__lift = 40 + (i % 3) * 26;
      d.__wobble = ((i % 2) ? 1 : -1) * (8 + i * 3);
      d.__delay = i * 42;
      layer.appendChild(d);
      nodes.push(d);
    }
    const t0 = (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now();
    const done = () => {
      if (killed) return;
      killed = true;
      if (handle != null) unraf(handle);
      try { layer.remove ? layer.remove() : layer.parentNode && layer.parentNode.removeChild(layer); } catch { /* noop */ }
      resolve();
    };
    const step = (now) => {
      if (killed) return;
      const el0 = (now || Date.now()) - t0;
      landed = 0;
      for (const d of nodes) {
        const t = Math.max(0, Math.min(1, (el0 - d.__delay) / ms));
        if (t >= 1) landed += 1;
        const e = easeOut(t);
        const x = a.x + (b.x - a.x) * e + d.__wobble * Math.sin(t * Math.PI);
        const y = a.y + (b.y - a.y) * e - d.__lift * Math.sin(t * Math.PI);
        d.style.transform = 'translate3d(' + x.toFixed(1) + 'px,' + y.toFixed(1) + 'px,0) scale(' + (1 - t * 0.25).toFixed(3) + ')';
        d.style.opacity = t >= 1 ? '0' : '1';
      }
      if (landed >= nodes.length) { done(); return; }
      handle = raf(step);
    };
    handle = raf(step);
    // A belt for the brace: if a frame clock never ticks (a backgrounded tab,
    // a headless probe), the flight still ends and the caller still continues.
    setTimeout(done, ms + count * 42 + 400);
  });
}

export default { fmtChips, countUp, balanceChip, chipStack, bank };
