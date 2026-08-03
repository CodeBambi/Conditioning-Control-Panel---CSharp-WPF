/* ============================================================================
 * exec/bubbles.js — GoonElement.Bubbles (3) + GoonPayloadKind.BubbleSwarm (2).
 *
 * THE REAL DtRH BUBBLE FIELD, ported from dtrh/engine/bubbles.js: the same
 * bubble.png art, the same rise + sway keyframes, the same effect-kind sprites,
 * the same click-to-pop with its sparkle burst — and the same pop-driven effect
 * flick that payloadFx.js gives a popped bubble in the Fall.
 *
 * POPPING IS COSMETIC IN GG v1. It is a toy for your hands while the duel runs:
 * it scores nothing, it ends nothing, and it changes NO receipt semantics. A
 * BubbleSwarm payload still runs its full duration_ms and then done(true) if it
 * was never interrupted, whether you popped every bubble or none of them.
 *
 * POINTER RULES. #gg-fx and every one of its sub-layers are pointer-events:none;
 * `.gg-bubble` is the ONLY node in this tier that opts back in (fx.css), so the
 * rest of the layer stays click-through. fx.css additionally drops that opt-in
 * whenever #gg-stage is occupied, so a bubble can never eat a click meant for a
 * lock card or a video attention check.
 *
 * BUDGET. MAX_LIVE bubbles, MAX_POP_FLASH pop flashes, ONE reused pane per pop
 * effect kind (a fresh pop refreshes its deadline instead of stacking a second
 * pane — payloadFx's `holds` pattern), and every node has a timer that retires it
 * even if its animationend never arrives (throttled/hidden tab).
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

import { pickSpiralUrl } from './spiral.js';

const MAX_LIVE = 26;        // hard ceiling on bubble nodes, swarm included
const MAX_POP_FLASH = 4;    // pop-driven flash images live at once (payloadFx MAX_FLASH's cousin)
const TOPUP_MS = 600;       // cadence of the population top-up tick (DtRH parity)
const SWARM_BONUS = 4;      // extra bubbles a BubbleSwarm payload puts on the bed

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

/** Cue intensity -> population + rise speed (DtRH: intensity is density, not size). */
export function bubbleTuning(intensity, calm, countMin, countMax) {
  const i = clamp01(intensity);
  const slow = calm ? 1.5 : 1;
  return {
    targetCount: Math.round(lerp(countMin, countMax, i)),
    riseMinS: lerp(RISE_MIN_CALM, RISE_MIN_HOT, i) * slow,
    riseMaxS: lerp(RISE_MAX_CALM, RISE_MAX_HOT, i) * slow,
  };
}

export function createBubbles({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:bubbles] ${m}`); };
  const calm = reducedMotion();
  const mobile = (typeof window !== 'undefined' && window.innerWidth) ? window.innerWidth < 640 : false;
  const COUNT_MIN = mobile ? 4 : 5;
  const COUNT_MAX = mobile ? 9 : 16;

  const live = new Set();          // {wrap, bubble, kind, popped}
  let elementIntensity = null;     // null = the ramp is not asking for a bed
  const payloadRuns = new Set();   // live BubbleSwarm runs (each {intensity})
  let topupTimer = 0;
  let tune = bubbleTuning(0, calm, COUNT_MIN, COUNT_MAX);
  let targetCount = 0;

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

  /** One bubble. `seed` scatters it mid-rise so a (re)fill does not march in. */
  function spawn(seed) {
    prune();
    if (targetCount <= 0) return;
    const host = layer();
    if (!host || typeof document === 'undefined') return;
    if (live.size >= Math.min(MAX_LIVE, targetCount)) return;

    const kind = pickKind();
    const size = Math.round(rand(BUB_MIN_PX, BUB_MAX_PX));
    const rise = rand(tune.riseMinS, tune.riseMaxS);

    const wrap = document.createElement('div');
    wrap.className = 'gg-bubble-wrap';
    wrap.style.setProperty('left', `${rand(2, 92).toFixed(1)}%`);
    wrap.style.setProperty('--gg-rise', `${rise.toFixed(2)}s`);
    if (seed) wrap.style.setProperty('animation-delay', `${(-rand(0, rise)).toFixed(2)}s`);

    const bubble = document.createElement('div');
    bubble.className = `gg-bubble gg-bubble--${kind}`;
    bubble.style.setProperty('width', `${size}px`);
    bubble.style.setProperty('height', `${size}px`);
    bubble.style.setProperty('--gg-sway', `${Math.round(rand(18, 46))}px`);
    bubble.style.setProperty('--gg-sway-dur', `${rand(2.5, 5).toFixed(2)}s`);
    wrap.appendChild(bubble);

    const rec = { wrap, bubble, kind, popped: false, size };
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

  function refresh() {
    const wants = [];
    if (elementIntensity !== null) wants.push(elementIntensity);
    for (const r of payloadRuns) wants.push(r.intensity);
    if (!wants.length) {
      targetCount = 0;
      try { clearInterval(topupTimer); } catch (_e) { /* ignore */ }
      topupTimer = 0;
      // Airborne bubbles finish their rise — yanking 20 nodes mid-flight reads
      // as a glitch, and each one retires on its own timer.
      return;
    }
    const want = Math.max.apply(null, wants);
    tune = bubbleTuning(want, calm, COUNT_MIN, COUNT_MAX);
    targetCount = Math.min(MAX_LIVE, tune.targetCount + (payloadRuns.size ? SWARM_BONUS : 0));
    if (!topupTimer) {
      // A soft tick adds ONE bubble at a time, so a ramp never dumps a wall of
      // them in one frame (DtRH's top-up interval, verbatim).
      topupTimer = setInterval(() => {
        prune();
        if (targetCount > 0 && live.size < targetCount) spawn(false);
      }, TOPUP_MS);
      if (topupTimer && typeof topupTimer.unref === 'function') topupTimer.unref();
      // Seed the first few mid-rise so the field does not fade in one at a time.
      const seedN = Math.min(3, targetCount);
      for (let i = 0; i < seedN; i++) soon(() => { if (targetCount > 0) spawn(true); }, i * 180);
    }
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

  function pop(rec, x, y) {
    rec.popped = true;
    rec.bubble.classList.add('is-pop');
    sparkleBurst(x, y);
    sfx('bubble-pop');
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
     * full duration whatever the player does with it — popping is cosmetic, so
     * the receipt is "ran to completion" (endured) unless something interrupts.
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
