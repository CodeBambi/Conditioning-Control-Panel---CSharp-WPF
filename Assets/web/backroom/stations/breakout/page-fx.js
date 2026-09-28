/* ============================================================================
 * stations/breakout/page-fx.js - the cabinet's five big-beat effects drawn INSIDE
 * the game window on the desktop, the way the web build draws them (the site's
 * backroom-web-ext/web/fx.js is the look this ports).
 *
 *   fx.wash         a coloured fade over the field, the dealt picture in the middle
 *   fx.gif_from     the picture grows out of a box (page CSS px) to fill the field
 *   fx.loom_spiral  the house Loom spiral fades in over the field, holds, fades out
 *   fx.sub_single   one word flashes in the page (pink to purple)
 *   fx.sub_pair     two words, then a brief spiral (words skipped when the page spelt them)
 *
 * Before this the desktop host drew these fullscreen on every monitor. Pictures come
 * from the station's OWN deal (media.urlOf(key)), so the player's source choice
 * (local, Scrolller, mixed, bundled) holds and nothing new is fetched. Throttles,
 * reduced motion and strengths are host-fx.js's; this file does not apply them twice.
 * Every timer is cancellable and cancelAll() removes every node at once.
 * ==========================================================================*/

import { isClip } from '../../room/clip-source.js';

/** The ids this renderer answers. Anything else still goes to the host. */
export const PAGE_FX = Object.freeze(['fx.wash', 'fx.gif_from', 'fx.loom_spiral', 'fx.sub_pair', 'fx.sub_single']);
const OWN = new Set(PAGE_FX);

/** The web's timings (fx.js): a spiral hold is stretched, a word lives 830 ms. */
export const WASH_MS = 900, WORD_MS = 830, WORD_GAP_MS = 500, SPIRAL_STRETCH = 1.8, SPIRAL_IN_MS = 1250, SPIRAL_OUT_MS = 500;
const SPIRAL_EDGE = 512, SPIRAL_FPS = 30;
const SPIRAL_PRESETS = ['screen', 'candy', 'pinwheel', 'mint', 'ribbon', 'star'];

/**
 * routeFx({ hosted, fx, page }) -> a ctx.fx shaped function, or null.
 * On the desktop host the five ids go to the page renderer and the rest to the host
 * unchanged; on the web (the site shim draws in-page already) ctx.fx is left alone.
 */
export function routeFx({ hosted = false, fx = null, page = null } = {}) {
  const host = typeof fx === 'function' ? fx : null;
  if (!hosted || !page) return host;
  return (id, symbols, args) => {
    if (OWN.has(id)) return page.fire(id, symbols, args);
    return host ? host(id, symbols, args) : undefined;
  };
}

/**
 * createPageFx({ parent, media, reduced, doc, setTimer, clearTimer, raf, caf, now, loadLoom })
 *   -> { fire(id, symbols, args), cancelAll(), dispose(), layer, count() }
 * `parent` is the station element; the layer mounts inside it on first use.
 * `media()` returns the station's media deck (urlOf(key), words) or null.
 */
export function createPageFx({
  parent, media = () => null, reduced = false, doc = globalThis.document,
  setTimer = (fn, ms) => setTimeout(fn, ms), clearTimer = (t) => clearTimeout(t),
  raf = (fn) => (typeof requestAnimationFrame === 'function' ? requestAnimationFrame(fn) : setTimer(() => fn(Date.now()), 33)),
  caf = (t) => (typeof cancelAnimationFrame === 'function' ? cancelAnimationFrame(t) : clearTimer(t)),
  now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now()),
  loadLoom = () => import('../../shared/hypno/loom.js'),
} = {}) {
  const timers = new Set(), stops = new Set();
  let layer = null, epoch = 0, spiralTurn = 0, disposed = false, loomKit = null;

  function later(fn, ms) {
    const e = epoch;
    const t = setTimer(() => { timers.delete(t); if (e === epoch && !disposed) fn(); }, Math.max(0, ms));
    timers.add(t);
    return t;
  }
  function mount() {
    if (layer && layer.parentNode) return layer;
    if (!parent || !doc) return null;
    layer = doc.createElement('div');
    layer.className = 'bo-pagefx';
    layer.setAttribute('aria-hidden', 'true');
    parent.appendChild(layer);
    return layer;
  }
  function add(cls, tag = 'div') {
    const root = mount(); if (!root) return null;
    const n = doc.createElement(tag); n.className = cls; root.appendChild(n); return n;
  }
  function remove(n) {
    if (!n) return;
    try { if (typeof n.getAnimations === 'function') n.getAnimations().forEach(a => a.cancel()); } catch (e) { /* noop */ }
    if (n.tagName === 'VIDEO') { try { n.pause(); } catch (e) { /* noop */ } n.removeAttribute?.('src'); }
    if (n.parentNode) n.parentNode.removeChild(n);
  }
  const drop = (n, ms) => { if (n) later(() => remove(n), ms); };
  function anim(n, frames, ms, easing = 'ease') {
    if (!n || typeof n.animate !== 'function') { if (n && frames.length) applyFrame(n, frames[Math.min(1, frames.length - 1)]); return null; }
    try { return n.animate(frames, { duration: Math.max(1, ms), easing, fill: 'forwards' }); } catch (e) { return null; }
  }
  function applyFrame(n, f) { if (!n.style) return; for (const k of Object.keys(f)) if (k !== 'offset') n.style[k] = String(f[k]); }
  function size() {
    const root = mount();
    const r = root && typeof root.getBoundingClientRect === 'function' ? root.getBoundingClientRect() : null;
    if (r && r.width > 0 && r.height > 0) return { left: r.left, top: r.top, w: r.width, h: r.height };
    return { left: 0, top: 0, w: 1280, h: 720 };
  }

  /** A picture node for a dealt key, or null (unknown key: the effect runs without a picture). */
  function picture(cls, key) {
    let url = null;
    try { const m = media(); url = m && typeof m.urlOf === 'function' && key ? m.urlOf(key) : null; } catch (e) { url = null; }
    if (!url) return null;
    const clip = isClip(url);
    const n = add(cls, clip ? 'video' : 'img');
    if (!n) return null;
    if (clip) { n.muted = true; n.loop = true; n.autoplay = true; n.playsInline = true; n.setAttribute?.('muted', ''); }
    else n.decoding = 'async';
    n.src = url;
    if (clip) { try { const p = n.play(); if (p && typeof p.catch === 'function') p.catch(() => {}); } catch (e) { /* noop */ } }
    if (typeof n.addEventListener === 'function') n.addEventListener('error', () => remove(n), { once: true });
    return n;
  }
  function wordText(key, i) {
    let list = [];
    try { const m = media(); list = (m && Array.isArray(m.words)) ? m.words : []; } catch (e) { list = []; }
    const hit = key ? list.find(w => w && w.key === key) : null;
    if (hit && hit.text) return String(hit.text);
    if (!list.length) return null;
    const w = list[(i + Math.floor(Math.random() * list.length)) % list.length];
    return w && w.text ? String(w.text) : null;
  }

  /* ------------------------------------------------------------ the five */
  function wash(color, strength, key) {
    const peak = 0.42 * (Number.isFinite(strength) ? strength : 0.7);
    const n = add('bo-pagefx-wash');
    if (!n) return;
    n.style.background = /^#[0-9a-fA-F]{6}$/.test(color || '') ? color : '#9b6bff';
    anim(n, [{ opacity: 0 }, { opacity: peak, offset: 80 / WASH_MS }, { opacity: 0 }], WASH_MS);
    drop(n, WASH_MS + 20);
    const img = picture('bo-pagefx-flash', key);
    if (img) {
      const a = Math.min(1, 0.85 * (Number.isFinite(strength) ? strength : 0.7));
      anim(img, reduced
        ? [{ opacity: 0 }, { opacity: a, offset: 0.15 }, { opacity: a, offset: 0.7 }, { opacity: 0 }]
        : [{ opacity: 0, transform: 'translate(-50%,-50%) scale(.86)' }, { opacity: a, transform: 'translate(-50%,-50%) scale(1)', offset: 0.15 },
           { opacity: a, transform: 'translate(-50%,-50%) scale(1.04)', offset: 0.7 }, { opacity: 0, transform: 'translate(-50%,-50%) scale(1.1)' }],
      WASH_MS * 1.6, 'ease-out');
      drop(img, WASH_MS * 1.6 + 20);
    }
  }
  function gifFrom(rect, key, ms, scale) {
    const dur = Math.max(400, Number(ms) || 3400), s = scale == null ? 1 : Math.max(0, Math.min(1, scale));
    const dim = s < 1 ? null : add('bo-pagefx-dim');
    if (dim) { anim(dim, [{ opacity: 0 }, { opacity: 0.55, offset: 0.2 }, { opacity: 0.55, offset: 0.75 }, { opacity: 0 }], dur); drop(dim, dur + 40); }
    const img = picture('bo-pagefx-full', key);
    if (!img) return;
    const box = size();
    const r = rect && Number.isFinite(rect.x) && Number.isFinite(rect.w) ? rect : { x: box.left + box.w / 2 - 30, y: box.top + box.h / 2 - 22, w: 60, h: 44 };
    const cx = r.x + r.w / 2 - (box.left + box.w / 2), cy = r.y + r.h / 2 - (box.top + box.h / 2);
    const fromT = `translate(${cx.toFixed(1)}px, ${cy.toFixed(1)}px) scale(${Math.max(0.02, r.w / box.w).toFixed(3)})`, at = 'translate(0px, 0px) scale(1)';
    const grow = Math.min(0.45, 700 / dur), fade = Math.max(grow + 0.05, 1 - 900 / dur);
    anim(img, reduced
      ? [{ opacity: 0 }, { opacity: s, offset: grow }, { opacity: s, offset: fade }, { opacity: 0 }]
      : [{ transform: fromT, opacity: 0.2 }, { transform: at, opacity: s, offset: grow }, { transform: at, opacity: s, offset: fade }, { transform: at, opacity: 0 }],
    dur, 'cubic-bezier(.4,0,.2,1)');
    drop(img, dur + 40);
  }
  function spiral(ms, alpha, preset) {
    const hold = Math.max(0, (Number(ms) || 4200) * SPIRAL_STRETCH), a = Math.max(0, Math.min(1, Number.isFinite(alpha) ? alpha : 0.85));
    const canvas = add('bo-pagefx-full bo-pagefx-spiral', 'canvas');
    if (!canvas) return;
    canvas.style.opacity = '0';
    const name = preset === 'wake' ? 'wake' : SPIRAL_PRESETS[spiralTurn++ % SPIRAL_PRESETS.length];
    let frame = 0, last = -Infinity, closed = false;
    const e = epoch;
    const stop = () => { if (closed) return; closed = true; if (frame) caf(frame); frame = 0; stops.delete(stop); remove(canvas); };
    stops.add(stop);
    Promise.resolve().then(loadLoom).then((mod) => {
      if (closed || e !== epoch || disposed || !canvas.parentNode) return;
      if (!loomKit && mod && typeof mod.createLoomKit === 'function') loomKit = mod.createLoomKit({ still: reduced });
      const kit = loomKit;
      const draw = (t) => {
        if (closed) return;
        const at = Number.isFinite(t) ? t : now();
        if (at - last >= 1000 / SPIRAL_FPS && kit) {
          const box = size(), ratio = box.w / box.h;
          const w = ratio >= 1 ? SPIRAL_EDGE : Math.round(SPIRAL_EDGE * ratio), h = ratio >= 1 ? Math.round(SPIRAL_EDGE / ratio) : SPIRAL_EDGE;
          if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
          try { kit.setStill?.(!!reduced); kit.paint(canvas, name, { now: at }); } catch (err) { /* a lost context paints nothing */ }
          last = at;
        }
        frame = raf(draw);
      };
      draw(now());
      anim(canvas, [{ opacity: 0 }, { opacity: a }], SPIRAL_IN_MS, 'ease-in-out');
      later(() => { anim(canvas, [{ opacity: a }, { opacity: 0 }], SPIRAL_OUT_MS); later(stop, SPIRAL_OUT_MS + 20); }, SPIRAL_IN_MS + hold);
    }).catch(() => stop());
  }
  function words(n, keys) {
    for (let i = 0; i < n; i++) later(() => {
      const text = wordText(keys[i] || keys[0], i);
      if (!text) return;
      const w = add('bo-pagefx-word');
      if (!w) return;
      w.textContent = text;
      anim(w, reduced
        ? [{ opacity: 0 }, { opacity: 1, offset: 80 / WORD_MS }, { opacity: 1, offset: 480 / WORD_MS }, { opacity: 0 }]
        : [{ opacity: 0, transform: 'scale(.92)' }, { opacity: 1, transform: 'scale(1)', offset: 80 / WORD_MS },
           { opacity: 1, transform: 'scale(1.03)', offset: 480 / WORD_MS }, { opacity: 0, transform: 'scale(1.08)' }], WORD_MS);
      drop(w, WORD_MS + 20);
    }, i * WORD_GAP_MS);
  }

  function fire(id, symbols, args) {
    if (disposed || !OWN.has(id)) return false;
    if (doc && doc.hidden) return false;
    const keys = Array.isArray(symbols) ? symbols.filter(k => typeof k === 'string' && k) : [];
    const a = args && typeof args === 'object' ? args : {};
    try {
      switch (id) {
        case 'fx.wash': wash(a.color, a.strength, keys[0]); break;
        case 'fx.gif_from': gifFrom(a.from, keys[0], a.ms, a.scale); break;
        case 'fx.loom_spiral': spiral(a.ms || 4200, Number.isFinite(a.alpha) ? a.alpha : 0.85, a.preset || 'screen'); break;
        case 'fx.sub_single': if (!a.wordsShown) words(1, keys); break;
        case 'fx.sub_pair':
          if (!a.wordsShown) words(2, keys);
          later(() => spiral(1500, 0.55, 'screen'), a.wordsShown ? 0 : 1000);
          break;
      }
    } catch (e) { return false; }
    return true;
  }
  /** Pause, menu, hidden page: every pending beat and every node goes at once. */
  function cancelAll() {
    epoch++;
    for (const t of timers) clearTimer(t);
    timers.clear();
    for (const stop of [...stops]) stop();
    if (layer) while (layer.firstChild) remove(layer.firstChild);
  }
  function dispose() {
    cancelAll(); disposed = true;
    try { loomKit && loomKit.dispose && loomKit.dispose(); } catch (e) { /* noop */ }
    loomKit = null;
    if (layer && layer.parentNode) layer.parentNode.removeChild(layer);
    layer = null;
  }
  return { fire, cancelAll, dispose, get layer() { return layer; }, count: () => (layer ? layer.childNodes.length : 0) };
}
