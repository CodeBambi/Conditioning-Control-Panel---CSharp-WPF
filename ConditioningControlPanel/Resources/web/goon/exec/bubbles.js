/* ============================================================================
 * exec/bubbles.js — GoonElement.Bubbles (3) + GoonPayloadKind.BubbleSwarm (2).
 *
 * THE REAL DtRH BUBBLE FIELD, ported from dtrh/engine/bubbles.js: the same
 * bubble.png art, the same rise + sway keyframes, the same effect-kind sprites,
 * the same click-to-pop with its sparkle burst — and the same pop-driven effect
 * flick that payloadFx.js gives a popped bubble in the Fall.
 *
 * POPPING PAYS NOW — but it still changes NO receipt semantics. A pop rolls an
 * ITEM DROP for the arsenal (ui/drops.js), and that is the whole incentive loop:
 * pop bubbles -> earn drops -> throw items at the opponent. A BubbleSwarm payload
 * still runs its full duration_ms and then done(true) if it was never
 * interrupted, whether you popped every bubble or none of them.
 *
 * THE DROP SEAM IS AN EVENT, NOT AN IMPORT. This module knows nothing about the
 * HUD, the arsenal or charges: every pop dispatches
 *
 *     document.dispatchEvent(new CustomEvent('gg-bubble-pop', {
 *       detail: { kind, worth, payload, size }
 *     }))
 *
 * and ui/hud.js -> ui/drops.js does the economics. `worth` is the drop weight:
 * 1 for a plain bubble, POP_WORTH_EFFECT for one of the five effect kinds, and
 * ZERO for a bubble an opponent's BubbleSwarm minted (their clutter must never
 * pay us — see fromPayload below).
 *
 * POINTER RULES. #gg-fx and every one of its sub-layers are pointer-events:none;
 * `.gg-bubble` is the ONLY node in this tier that opts back in (fx.css), so the
 * rest of the layer stays click-through. That opt-in is now UNCONDITIONAL: fx.css
 * used to revoke it whenever #gg-stage held a video or a lock card, which was
 * harmless while pops were cosmetic and starves the player outright now that pops
 * are the only source of items.
 *
 * SO THE FIELD GETS OUT OF THE WAY INSTEAD OF GOING DEAF. While the stage holds
 * something, spawnRanges() below keeps NEW bubbles in the left/right gutters
 * beside the stage content — they rise vertically, so a gutter spawn stays a
 * gutter bubble for its whole life and never covers the video controls or the
 * typing card. Bubbles already airborne when a video starts may drift across it;
 * they are left alone, because teleporting a live node reads as a glitch and the
 * click they might steal is one click, not a locked-out minute. If the gutters
 * are too thin to be worth it (MIN_GUTTER_PX), the field goes back to full width:
 * a bubble over the video is an annoyance, a dead economy is a broken match.
 *
 * BUDGET. MAX_LIVE bubbles, MAX_POP_FLASH pop flashes, ONE reused pane per pop
 * effect kind (a fresh pop refreshes its deadline instead of stacking a second
 * pane — payloadFx's `holds` pattern), and every node has a timer that retires it
 * even if its animationend never arrives (throttled/hidden tab).
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

import { pickSpiralUrl } from './spiral.js';

export const MAX_LIVE = 26;   // hard ceiling on bubble nodes, swarm included
const MAX_POP_FLASH = 4;      // pop-driven flash images live at once (payloadFx MAX_FLASH's cousin)
const SWARM_BONUS = 4;        // extra bubbles a BubbleSwarm payload puts on the bed

/* Cadence of the population top-up tick. The field is ALWAYS ON now and ramps
   0.15 -> 1.0 across the match, so the tick is a dial too: sparse and slow at
   the open, a steady drip late. Quantised to 100 ms buckets inside
   bubbleTuning() so a continuous ramp cannot restart the interval every cue. */
export const TOPUP_MS_CALM = 1200;   // intensity 0
export const TOPUP_MS_HOT = 400;     // intensity 1

// seconds to cross the window at intensity 0 / intensity 1 (DtRH tuning block)
const RISE_MIN_CALM = 9, RISE_MAX_CALM = 16;
const RISE_MIN_HOT = 4.5, RISE_MAX_HOT = 8;

const BUB_MIN_PX = 80, BUB_MAX_PX = 150;

/** The DtRH spawn mix, minus the kinds another element owns (subliminal) and the
 *  Fall's scoring-only ones (lucky/prism). Weights are DtRH's. */
const KINDS = [
  { id: 'normal', w: 50 },
  { id: 'flash', w: 9 },
  { id: 'spiral', w: 8 },
  { id: 'glitch', w: 7 },
  { id: 'braindrain', w: 6 },
  { id: 'pinkfilter', w: 6 },
];
const KIND_TOTAL = KINDS.reduce((a, k) => a + k.w, 0);

/* --------------------------------------------------------------- drop seam */

/** The event ui/hud.js listens for. One dispatch per pop, no exceptions. */
export const POP_EVENT = 'gg-bubble-pop';

/** Drop weight of an effect-kind pop. ui/drops.js multiplies its base chance by
 *  this, so 1 -> 12% and 2.5 -> 30% at the shipped tuning. */
export const POP_WORTH_EFFECT = 2.5;
/** Plain bubble. */
export const POP_WORTH_NORMAL = 1;
/** A bubble an opponent's BubbleSwarm minted. Clutter, not income. */
export const POP_WORTH_PAYLOAD = 0;

/**
 * What one pop is worth to the economy.
 * @param {string} kind         bubble kind ('normal' | 'flash' | ...)
 * @param {boolean} fromPayload minted by an inbound BubbleSwarm
 */
export function popWorthOf(kind, fromPayload) {
  if (fromPayload) return POP_WORTH_PAYLOAD;
  return kind && kind !== 'normal' ? POP_WORTH_EFFECT : POP_WORTH_NORMAL;
}

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const rand = (a, b) => a + Math.random() * (b - a);
// strength 0..100 mapped onto [min,max] — the JS twins of DtRH's Scale/ScaleD
const scale = (min, max, s) => min + Math.round((max - min) * clamp01(s / 100));
const scaleD = (min, max, s) => min + (max - min) * clamp01(s / 100);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};

/* ------------------------------------------------------- stage avoidance */

/** The spawn margins the field has always used, as a percent of the viewport.
 *  `left` is the bubble's LEFT EDGE (the wrap has no width of its own), so the
 *  high end is short of 100 to keep a fat bubble mostly on screen. */
export const SPAWN_MIN_PCT = 2;
export const SPAWN_MAX_PCT = 92;
/** Combined gutter width below which biasing is not worth it — squeezing the
 *  whole field through a 100px slot looks worse than letting it overlap. */
export const MIN_GUTTER_PX = 160;
/** A slice narrower than this carries no visible variety; drop it. */
const MIN_RANGE_PCT = 1.5;

const finiteOr = (v, fallback) => (typeof v === 'number' && v === v && v !== Infinity && v !== -Infinity ? v : fallback);

/**
 * Where a NEW bubble is allowed to start, given whatever the stage is showing.
 *
 * PURE, and deliberately so: it is the one piece of this that has an opinion
 * worth pinning in a test, and it depends on nothing but numbers.
 *
 * @param {{left:number,right?:number,width?:number}|null} stageRect
 *        the stage CONTENT's viewport rect (not the stage layer, which is
 *        full-bleed), or null/garbage when the stage is empty or unmeasurable
 * @param {number} viewportW  window.innerWidth
 * @param {{minGutterPx?:number, bubblePx?:number}} [opts]
 *        bubblePx is this bubble's own width: it grows RIGHTWARD from `left`, so
 *        only the left gutter has to give it room.
 * @returns {Array<[number, number]>} one or more [loPct, hiPct] spans. Always at
 *        least one — the full-width span is the fallback for every degenerate
 *        input, because a field that cannot spawn is the worse failure.
 */
export function spawnRanges(stageRect, viewportW, opts) {
  const full = [[SPAWN_MIN_PCT, SPAWN_MAX_PCT]];
  const o = opts || {};
  const vw = finiteOr(Number(viewportW), 0);
  if (!(vw > 0) || !stageRect || typeof stageRect !== 'object') return full;

  const left = finiteOr(Number(stageRect.left), NaN);
  const rawRight = stageRect.right !== undefined
    ? Number(stageRect.right)
    : Number(stageRect.left) + Number(stageRect.width);
  const right = finiteOr(rawRight, NaN);
  if (left !== left || right !== right || !(right > left)) return full;

  const minGutter = finiteOr(Number(o.minGutterPx), MIN_GUTTER_PX);
  const bubblePx = Math.max(0, finiteOr(Number(o.bubblePx), 0));

  // A rect that hangs off either edge only counts for the part on screen.
  const l = Math.max(0, Math.min(vw, left));
  const r = Math.max(0, Math.min(vw, right));
  if (l + (vw - r) < minGutter) return full;

  const pct = (px) => (px / vw) * 100;
  const ranges = [];
  const leftHi = Math.min(SPAWN_MAX_PCT, pct(l - bubblePx));
  if (leftHi - SPAWN_MIN_PCT >= MIN_RANGE_PCT) ranges.push([SPAWN_MIN_PCT, leftHi]);
  const rightLo = Math.max(SPAWN_MIN_PCT, pct(r));
  if (SPAWN_MAX_PCT - rightLo >= MIN_RANGE_PCT) ranges.push([rightLo, SPAWN_MAX_PCT]);
  return ranges.length ? ranges : full;
}

/**
 * Pick a left-edge percent out of spawnRanges()' output, WIDTH-WEIGHTED so a
 * 600px gutter carries proportionally more of the field than a 200px one (an
 * even split would visibly pile the bubbles up on the narrow side).
 *
 * Client-local placement only — plain Math.random, same as every other spawn
 * dimension in this file. Nothing here reaches a receipt.
 *
 * @param {Array<[number, number]>} ranges
 * @param {() => number} [rnd] injectable for tests
 */
export function pickSpawnPct(ranges, rnd) {
  const r = typeof rnd === 'function' ? rnd : Math.random;
  const list = (Array.isArray(ranges) ? ranges : [])
    .filter((x) => Array.isArray(x) && finiteOr(x[1], NaN) > finiteOr(x[0], NaN));
  if (!list.length) return SPAWN_MIN_PCT + r() * (SPAWN_MAX_PCT - SPAWN_MIN_PCT);
  let total = 0;
  for (const [a, b] of list) total += b - a;
  let t = r() * total;
  for (const [a, b] of list) {
    const w = b - a;
    if (t < w) return a + t;
    t -= w;
  }
  const last = list[list.length - 1];
  return last[0] + (last[1] - last[0]) * 0.5;   // r() === 1 exactly
}

/**
 * Cue intensity -> population + rise speed + spawn cadence (DtRH: intensity is
 * density, not size). MONOTONIC BY CONTRACT, and selftest-exec asserts it:
 * targetCount never falls as intensity rises, topupMs/riseMinS/riseMaxS never
 * rise. That is the whole "starts sparse, ends dense" ramp.
 */
export function bubbleTuning(intensity, calm, countMin, countMax) {
  const i = clamp01(intensity);
  const slow = calm ? 1.5 : 1;
  // 100 ms buckets: a ramp that nudges intensity every second must not restart
  // the top-up interval every second (see ensureTopup).
  const topup = Math.round(lerp(TOPUP_MS_CALM, TOPUP_MS_HOT, i) / 100) * 100;
  return {
    targetCount: Math.round(lerp(countMin, countMax, i)),
    riseMinS: lerp(RISE_MIN_CALM, RISE_MIN_HOT, i) * slow,
    riseMaxS: lerp(RISE_MAX_CALM, RISE_MAX_HOT, i) * slow,
    topupMs: Math.max(120, topup) * (calm ? 1.5 : 1),
  };
}

export function createBubbles({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:bubbles] ${m}`); };
  const calm = reducedMotion();
  const mobile = (typeof window !== 'undefined' && window.innerWidth) ? window.innerWidth < 640 : false;
  // The field runs the WHOLE match now, so intensity 0 has to be genuinely
  // sparse; intensity 1 + SWARM_BONUS lands exactly on MAX_LIVE (26) on desktop.
  const COUNT_MIN = mobile ? 2 : 3;
  const COUNT_MAX = mobile ? 12 : 22;

  const live = new Set();          // {wrap, bubble, kind, popped, fromPayload}
  let elementIntensity = null;     // null = the ramp is not asking for a bed
  const payloadRuns = new Set();   // live BubbleSwarm runs (each {intensity})
  let topupTimer = 0;
  let topupMs = 0;                 // cadence the running interval was built with
  let seeded = false;
  let tune = bubbleTuning(0, calm, COUNT_MIN, COUNT_MAX);
  let targetCount = 0;
  // What the ELEMENT ramp alone asks for. Everything above it while a swarm is
  // running is the swarm's, and a swarm's bubble pays nothing (POP_WORTH_PAYLOAD).
  let fieldTarget = 0;

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('bubbles') : null);
  const sfx = (id) => { if (audio && typeof audio.sfx === 'function') { try { audio.sfx(id); } catch (_e) { /* ignore */ } } };

  /* ------------------------------------------------------------ spawn / field */

  function pickKind() {
    let r = Math.random() * KIND_TOTAL;
    for (const k of KINDS) if ((r -= k.w) < 0) return k.id;
    return 'normal';
  }

  /** Drop bookkeeping for nodes the layer tore out from under us (layers.stopAll). */
  function prune() {
    for (const rec of Array.from(live)) if (!rec.wrap || !rec.wrap.isConnected) live.delete(rec);
  }

  function recycle(rec) {
    if (!live.has(rec)) return;
    live.delete(rec);
    try { rec.wrap.remove(); } catch (_e) { /* ignore */ }
    if (targetCount > 0 && live.size < targetCount) spawn(false);
  }

  /* ------------------------------------------------ stage rect, measured lazily
   * getBoundingClientRect() forces layout, so this is read AT MOST once per
   * STAGE_RECT_TTL_MS and only ever from spawn() — never per frame, and never
   * from the sway/rise path, which is pure CSS and must stay that way. The rect
   * only moves when a video or a lock card mounts, unmounts or the window
   * resizes, all of which are far slower than the TTL. */
  const STAGE_RECT_TTL_MS = 500;
  let rectCache = null;
  let rectAt = -Infinity;

  function stageEl() {
    if (layers && typeof layers.get === 'function') {
      try { const el = layers.get('stage'); if (el) return el; } catch (_e) { /* fall through */ }
    }
    if (typeof document !== 'undefined' && document && typeof document.getElementById === 'function') {
      try { return document.getElementById('gg-stage'); } catch (_e) { return null; }
    }
    return null;
  }

  /** The stage CONTENT's rect (its first child), or null when the stage is empty
   *  or the host cannot measure (the node test stub has no layout box). */
  function measureStage() {
    const stage = stageEl();
    if (!stage) return null;
    const kid = stage.firstElementChild || stage.firstChild || null;
    if (!kid || typeof kid.getBoundingClientRect !== 'function') return null;
    let r = null;
    try { r = kid.getBoundingClientRect(); } catch (_e) { return null; }
    if (!r || !(r.width > 0) || !(r.height > 0)) return null;
    return { left: r.left, right: r.right };
  }

  function stageRect() {
    const now = Date.now();
    if (now - rectAt < STAGE_RECT_TTL_MS) return rectCache;
    rectAt = now;
    rectCache = measureStage();
    return rectCache;
  }

  function viewportWidth() {
    if (typeof window === 'undefined' || !window) return 0;
    const w = Number(window.innerWidth);
    return (w > 0 && w === w) ? w : 0;
  }

  /** Live bubbles that belong to OUR ramp (i.e. the ones a pop can be paid for). */
  function fieldCount() {
    let n = 0;
    for (const rec of live) if (!rec.fromPayload) n++;
    return n;
  }

  /** One bubble. `seed` scatters it mid-rise so a (re)fill does not march in. */
  function spawn(seed) {
    prune();
    if (targetCount <= 0) return;
    const host = layer();
    if (!host || typeof document === 'undefined') return;
    if (live.size >= Math.min(MAX_LIVE, targetCount)) return;

    // Our own bed fills first; anything past it while a swarm is up is THEIRS.
    const fromPayload = payloadRuns.size > 0 && fieldCount() >= fieldTarget;
    const kind = pickKind();
    const size = Math.round(rand(BUB_MIN_PX, BUB_MAX_PX));
    const rise = rand(tune.riseMinS, tune.riseMaxS);

    // Stage-aware placement: gutters while a video/lock card is up, full width
    // otherwise (and whenever the gutters are too thin to be worth the squeeze).
    const spawnPct = pickSpawnPct(spawnRanges(stageRect(), viewportWidth(), { bubblePx: size }));

    const wrap = document.createElement('div');
    wrap.className = 'gg-bubble-wrap';
    wrap.style.setProperty('left', `${spawnPct.toFixed(1)}%`);
    wrap.style.setProperty('--gg-rise', `${rise.toFixed(2)}s`);
    if (seed) wrap.style.setProperty('animation-delay', `${(-rand(0, rise)).toFixed(2)}s`);

    const bubble = document.createElement('div');
    bubble.className = `gg-bubble gg-bubble--${kind}`;
    bubble.style.setProperty('width', `${size}px`);
    bubble.style.setProperty('height', `${size}px`);
    bubble.style.setProperty('--gg-sway', `${Math.round(rand(18, 46))}px`);
    bubble.style.setProperty('--gg-sway-dur', `${rand(2.5, 5).toFixed(2)}s`);
    // The tag a play-test/headless harness can read back: which side minted it.
    try { bubble.setAttribute('data-gg-mint', fromPayload ? 'payload' : 'field'); } catch (_e) { /* stub DOM */ }
    wrap.appendChild(bubble);

    const rec = { wrap, bubble, kind, popped: false, size, fromPayload };
    live.add(rec);

    // e.target guard: the bubble's own pop animation bubbles up through the wrap.
    wrap.addEventListener('animationend', (e) => {
      if (e && e.target && e.target !== wrap) return;
      if (!rec.popped) recycle(rec);
    });
    bubble.addEventListener('pointerdown', (e) => {
      if (e && typeof e.preventDefault === 'function') e.preventDefault();
      if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
      if (rec.popped) return;
      // Left button only: a right-click belongs to the page, not the field.
      if (e && e.button != null && e.button !== 0) return;
      const x = (e && typeof e.clientX === 'number') ? e.clientX : 0;
      const y = (e && typeof e.clientY === 'number') ? e.clientY : 0;
      pop(rec, x, y);
    });

    host.appendChild(wrap);
    // Retire on a timer too: a throttled/hidden tab may never deliver
    // animationend, and a leaked node is a leak for the rest of the match.
    soon(() => recycle(rec), Math.round(rise * 1000) + 500);
  }

  function stopTopup() {
    try { clearInterval(topupTimer); } catch (_e) { /* ignore */ }
    topupTimer = 0;
    topupMs = 0;
  }

  /**
   * A soft tick adds ONE bubble at a time, so a ramp never dumps a wall of them
   * in one frame (DtRH's top-up interval). The CADENCE is part of the ramp now,
   * so the interval is rebuilt when the bucketed value actually moves.
   */
  function ensureTopup() {
    const wantMs = Math.max(120, Math.round(tune.topupMs));
    if (topupTimer && wantMs === topupMs) return;
    if (topupTimer) { try { clearInterval(topupTimer); } catch (_e) { /* ignore */ } topupTimer = 0; }
    topupMs = wantMs;
    topupTimer = setInterval(() => {
      prune();
      if (targetCount > 0 && live.size < targetCount) spawn(false);
    }, wantMs);
    if (topupTimer && typeof topupTimer.unref === 'function') topupTimer.unref();
    if (!seeded) {
      seeded = true;
      // Seed the first few mid-rise so the field does not fade in one at a time.
      const seedN = Math.min(3, targetCount);
      for (let i = 0; i < seedN; i++) soon(() => { if (targetCount > 0) spawn(true); }, i * 180);
    }
  }

  function refresh() {
    const wants = [];
    if (elementIntensity !== null) wants.push(elementIntensity);
    for (const r of payloadRuns) wants.push(r.intensity);
    if (!wants.length) {
      targetCount = 0;
      fieldTarget = 0;
      seeded = false;
      stopTopup();
      // Airborne bubbles finish their rise — yanking 20 nodes mid-flight reads
      // as a glitch, and each one retires on its own timer.
      return;
    }
    const want = Math.max.apply(null, wants);
    tune = bubbleTuning(want, calm, COUNT_MIN, COUNT_MAX);
    targetCount = Math.min(MAX_LIVE, tune.targetCount + (payloadRuns.size ? SWARM_BONUS : 0));
    fieldTarget = elementIntensity === null
      ? 0
      : Math.min(MAX_LIVE, bubbleTuning(elementIntensity, calm, COUNT_MIN, COUNT_MAX).targetCount);
    ensureTopup();
  }

  /* --------------------------------------------------------------- pop + fx */

  /** Sparkle burst, ported from DtRH (which ported it from the WPF BubbleService). */
  function sparkleBurst(x, y) {
    const host = layer();
    if (!host || typeof document === 'undefined') return;
    const n = 9;
    for (let i = 0; i < n; i++) {
      const p = document.createElement('div');
      p.className = 'gg-spark';
      const ang = (Math.PI * 2 * i) / n + rand(-0.35, 0.35);
      const dist = rand(42, 112);
      p.style.setProperty('left', `${x}px`);
      p.style.setProperty('top', `${y}px`);
      p.style.setProperty('--gg-dx', `${Math.round(Math.cos(ang) * dist)}px`);
      p.style.setProperty('--gg-dy', `${Math.round(Math.sin(ang) * dist)}px`);
      p.style.setProperty('--gg-fall', `${Math.round(rand(28, 66))}px`);
      const kill = () => { try { p.remove(); } catch (_e) { /* ignore */ } };
      p.addEventListener('animationend', kill, { once: true });
      host.appendChild(p);
      soon(kill, 900);
    }
  }

  /* Sustained pop panes: ONE reused element per kind, opacity-toggled, exactly
     like payloadFx's `holds`. A fresh pop of the same kind refreshes the fade
     deadline; different kinds stack (that stacking IS the effect chain). */
  const holds = Object.create(null);   // kind -> {el, cls, gen, hideTimer, glitchTimer, handle}

  function releaseHold(h) {
    try { if (h.handle && h.handle.release) h.handle.release(); } catch (_e) { /* ignore */ }
    h.handle = null;
  }

  function ensureHold(kind, cls) {
    let h = holds[kind];
    if (h && h.el && h.el.isConnected) return h;
    const host = layer();
    if (!host || typeof document === 'undefined') return null;
    const el = document.createElement('div');
    el.className = cls;
    host.appendChild(el);
    h = holds[kind] = { el, cls, gen: 0, hideTimer: 0, glitchTimer: 0, handle: (h && h.handle) || null };
    return h;
  }

  /**
   * Snap a pane on at `opacity`, fade it after `durMs`, then TAKE IT OUT. DtRH
   * parks its holds forever because pops there are constant; here they are
   * occasional, and a parked backdrop-filter pane at opacity 0 still costs a
   * compositor pass. `gen` is what keeps a fresh pop from being torn down by the
   * previous pop's removal timer.
   */
  function holdOn(kind, cls, opacity, durMs) {
    const h = ensureHold(kind, cls);
    if (!h) return null;
    if (h.hideTimer) { try { clearTimeout(h.hideTimer); } catch (_e) { /* ignore */ } h.hideTimer = 0; }
    const gen = ++h.gen;
    h.el.style.setProperty('opacity', String(opacity));
    h.hideTimer = soon(() => {
      if (h.gen !== gen) return;
      h.hideTimer = 0;
      if (h.el) h.el.style.setProperty('opacity', '0');
      releaseHold(h);
      soon(() => {
        if (h.gen !== gen) return;               // a newer pop took the pane over
        if (h.glitchTimer) { try { clearTimeout(h.glitchTimer); } catch (_e) { /* ignore */ } h.glitchTimer = 0; }
        try { h.el.remove(); } catch (_e) { /* ignore */ }
        if (holds[kind] === h) delete holds[kind];
      }, 1000);
    }, durMs);
    return h;
  }

  const popFlashes = new Set();
  function pruneFlashes() {
    for (const rec of Array.from(popFlashes)) {
      if (!rec.node || !rec.node.isConnected) {
        popFlashes.delete(rec);
        try { if (rec.handle && rec.handle.release) rec.handle.release(); } catch (_e) { /* ignore */ }
      }
    }
  }

  /** An image handle from the player's own pool (null when the pool has none). */
  function drawImage() {
    if (!media || typeof media.drawKind !== 'function') return null;
    const entry = media.drawKind('image');
    if (!entry) return null;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    return (handle && handle.url) ? handle : null;
  }

  /** The scattered flash a popped flash-bubble throws (payloadFx.flash, bounded). */
  function popFlash(strength) {
    pruneFlashes();
    const host = layer();
    if (!host || typeof document === 'undefined') return;
    const amount = Math.max(1, scale(1, 2, strength));
    const dur = scale(900, 1600, strength);
    for (let i = 0; i < amount; i++) {
      if (popFlashes.size >= MAX_POP_FLASH) break;
      const handle = drawImage();
      if (!handle) break;
      const img = document.createElement('img');
      img.className = 'gg-flash';
      img.decoding = 'async';
      img.alt = '';
      img.style.setProperty('--gg-flash-dur', `${dur}ms`);
      img.style.setProperty('--gg-flash-op', '0.95');
      img.style.setProperty('left', `${(14 + Math.random() * 72).toFixed(1)}vw`);
      img.style.setProperty('top', `${(16 + Math.random() * 64).toFixed(1)}vh`);
      img.style.setProperty('--gg-flash-rot', `${(Math.random() * 16 - 8).toFixed(1)}deg`);
      const rec = { node: img, handle };
      popFlashes.add(rec);
      let killed = false;
      const kill = () => {
        if (killed) return;
        killed = true;
        popFlashes.delete(rec);
        try { img.remove(); } catch (_e) { /* ignore */ }
        try { img.removeAttribute('src'); } catch (_e) { /* ignore */ }
        try { if (handle.release) handle.release(); } catch (_e) { /* ignore */ }
      };
      img.addEventListener('animationend', kill, { once: true });
      img.onerror = kill;
      img.src = handle.url;
      host.appendChild(img);
      soon(kill, dur + 600);
    }
  }

  /** The drain wash: dim + blur-behind with a faint image over it (showBraindrain). */
  function popDrain(strength) {
    const h = holdOn('drain', 'gg-drain', scaleD(0.35, 0.62, strength), scale(1500, 4500, strength));
    if (!h) return h;
    const handle = drawImage();
    if (handle) {
      releaseHold(h);
      h.handle = handle;
      try { h.el.style.setProperty('background-image', `url("${handle.url}")`); } catch (_e) { /* ignore */ }
    }
    return h;
  }

  /** What a popped bubble of each kind throws. Bounded, and never a new layer. */
  function popFx(kind, strength) {
    switch (kind) {
      case 'flash':
        popFlash(strength);
        break;
      case 'spiral': {
        const h = holdOn('spiral', 'gg-spiral', scaleD(0.25, 0.70, strength), scale(1500, 4500, strength));
        if (h) { try { h.el.style.setProperty('background-image', `url('${pickSpiralUrl()}')`); } catch (_e) { /* ignore */ } }
        break;
      }
      case 'pinkfilter':
        holdOn('pink', 'gg-pink', scaleD(0.25, 0.70, strength), scale(1500, 4500, strength));
        break;
      case 'braindrain':
        popDrain(strength);
        break;
      case 'glitch': {
        // RGB-split shudder OVER the drain wash — DtRH's showGlitch, hard-capped
        // so a fat bubble can never strobe forever.
        const h = popDrain(strength);
        if (!h) break;
        h.el.classList.add('is-glitching');
        const ms = Math.min(4000, scale(1200, 3000, strength));
        try { clearTimeout(h.glitchTimer); } catch (_e) { /* ignore */ }
        h.glitchTimer = soon(() => { if (h.el) h.el.classList.remove('is-glitching'); }, ms);
        break;
      }
      default:
        break;   // 'normal' pops for the pop's sake
    }
  }

  /**
   * The economy seam. Fired for EVERY pop including a worth-0 one, so ui/drops.js
   * can count clutter as well as income. A listener that throws is the listener's
   * problem: dispatchEvent never lets it reach the field.
   */
  function announcePop(rec, x, y) {
    if (typeof document === 'undefined' || !document || typeof document.dispatchEvent !== 'function') return;
    if (typeof CustomEvent !== 'function') return;
    const detail = {
      kind: rec.kind,
      worth: popWorthOf(rec.kind, rec.fromPayload),
      payload: !!rec.fromPayload,
      size: rec.size,
      // where it burst, so the HUD can fly its drop sparkle from the right spot
      x: typeof x === 'number' ? x : 0,
      y: typeof y === 'number' ? y : 0,
    };
    try { document.dispatchEvent(new CustomEvent(POP_EVENT, { detail, bubbles: true })); }
    catch (e) { warn(`pop dispatch threw: ${e && e.message}`); }
  }

  function pop(rec, x, y) {
    rec.popped = true;
    rec.bubble.classList.add('is-pop');
    sparkleBurst(x, y);
    sfx('bubble-pop');
    announcePop(rec, x, y);
    // Bubble size IS the strength dial in the Fall; same here.
    const strength = Math.round(clamp01((rec.size - BUB_MIN_PX) / (BUB_MAX_PX - BUB_MIN_PX)) * 100);
    try { popFx(rec.kind, strength); } catch (e) { warn(`popFx ${rec.kind} threw: ${e && e.message}`); }
    rec.bubble.addEventListener('animationend', () => recycle(rec), { once: true });
    soon(() => recycle(rec), 600);
  }

  /* ------------------------------------------------------------------- api */

  return {
    name: 'bubbles',

    start(cue) {
      elementIntensity = clamp01(cue && cue.intensity);
      refresh();
    },

    setIntensity(v) {
      if (elementIntensity === null) return;
      elementIntensity = clamp01(v);
      refresh();
    },

    stop() {
      elementIntensity = null;
      refresh();
    },

    /**
     * BubbleSwarm: a wave. Denser than any bed the ramp asks for, and it runs its
     * full duration whatever the player does with it — popping never settles it,
     * so the receipt is "ran to completion" (endured) unless something interrupts.
     *
     * ITS bubbles pay NOTHING. Everything above the element ramp's own target
     * while a swarm is up is minted for the swarm (spawn(): fromPayload), pops
     * with worth 0, and is skipped by the drop roller — otherwise firing a swarm
     * at someone would hand them free items.
     */
    renderPayload(payload, done) {
      const p = payload || {};
      const runMs = Math.max(1500, (p.duration_ms | 0) || 10000);
      const run = { intensity: Math.max(0.65, clamp01(p.intensity !== undefined ? p.intensity : 0.75)) };

      let finished = false;
      let endTimer = 0;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        try { clearTimeout(endTimer); } catch (_e) { /* ignore */ }
        payloadRuns.delete(run);
        refresh();
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };

      payloadRuns.add(run);
      refresh();
      endTimer = soon(() => settle(true), runMs);
      return () => settle(false);
    },
  };
}

export default createBubbles;
