/* ============================================================================
 * engine/util.js — DOM guards + a timer registry.
 *
 * Every DOM touch in the engine goes through `hasDom()` / lazy element creation
 * so that importing any engine module under node (for the pure-logic tests)
 * never explodes. Timers/rAF loops are registered so suspend(on) and dispose()
 * can drop EVERYTHING immediately — that is a hard requirement (panic key,
 * mandatory video, audio-only session).
 * ==========================================================================*/

export const hasDom = () => (typeof document !== 'undefined' && !!document.createElement);
export const hasWindow = () => (typeof window !== 'undefined');
export const nowMs = () => ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now());

/** Create an element with class + optional style/text. Returns null with no DOM. */
export function el(tag, cls, styles) {
  if (!hasDom()) return null;
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (styles) for (const k in styles) { try { n.style[k] = styles[k]; } catch { /* ignore */ } }
  return n;
}

/** A url the <img> element cannot show: a video container. Remote providers
 *  (Scrolller) serve animated content ONLY as silent webm/mp4 - there is no
 *  .gif on that side at all - so a "loop" url may well be one of these. */
export const VIDEO_URL_RE = /\.(mp4|webm|m4v)(\?|#|$)/i;
export const isVideoUrl = (url) => VIDEO_URL_RE.test(String(url || ''));

/* ---- THE DECODER BUDGET (perf, measured 2026-08-23) -----------------------
 * A Chromium trace of a full-screen board with sixteen live video tiles put the
 * GPU process's main thread at 79% of a core, and roughly a third of that was
 * simultaneous 854x480 decodes. Scrolller's SMALLEST rendition is 854x480, so
 * there is no cheaper file to ask for: the only lever is the decoder COUNT.
 *
 * Decoration therefore shares one budget. `gif_rain` and `gif_burst` ask
 * `budgetedKind('loop')` before they ask the pool for a url, and once the
 * budget is spent they are handed a STILL instead - the shower keeps its node
 * count and its rhythm, it just stops minting decoders. The counter lives here
 * because `mediaEl` is the one door a decoration video comes in through and
 * `createTimers` release/kill is the one door it leaves by.
 *
 * The lite rung reads `.ae-lite` on <html> (a game or the shell sets it; the
 * engine never owns that class), because a machine that cannot paint the frame
 * cannot decode six videos either.
 *
 * THE TOUCH RUNG reads `.ae-touch` on <html>, set the same way and by the same
 * kind of caller, and it is a HARDWARE ceiling rather than a quality level: iOS
 * caps concurrent hardware decode SESSIONS (three or four before VideoToolbox
 * thrashes and every stream stutters), and the mobile graphics diet found even
 * two simultaneous 854x480 decodes fighting the game for the frame - so a
 * phone gets ONE decoration decoder, on the FULL rung too. The two compose the
 * protective way - fewer decoders always wins - so touch+lite is min(2,1) = 1
 * and a lite phone is never handed MORE video than a lite desktop. */
export const VIDEO_BUDGET = 6;
export const VIDEO_BUDGET_LITE = 2;
export const VIDEO_BUDGET_TOUCH = 1;
let liveVideos = 0;
/** How many decoration <video> nodes mediaEl has minted and not yet released. */
export const liveVideoCount = () => liveVideos;
export function videoBudget() {
  let budget = VIDEO_BUDGET;
  try {
    const html = hasDom() ? document.documentElement : null;
    const cl = html && html.classList ? html.classList : null;
    if (cl && cl.contains('ae-lite')) budget = Math.min(budget, VIDEO_BUDGET_LITE);
    if (cl && cl.contains('ae-touch')) budget = Math.min(budget, VIDEO_BUDGET_TOUCH);
  } catch { /* ignore */ }
  return budget;
}
/** The asset kind decoration may actually ask for. 'loop' until the budget is
 *  spent, then 'still' - every other kind passes straight through. */
export function budgetedKind(want) {
  if (want !== 'loop') return want;
  return liveVideos >= videoBudget() ? 'still' : 'loop';
}
/** Test seam only: the harness runs many cases in one process. */
export function resetVideoBudget() { liveVideos = 0; }
function forgetVideo(node) {
  if (!node || node.__aeVideo !== true) return;
  try { node.__aeVideo = false; } catch { /* ignore */ }
  liveVideos = liveVideos > 0 ? liveVideos - 1 : 0;
}

/* ---- THE STILL PLATE (phone fx diet, measured 2026-08-28) -----------------
 * An animated gif that covers the stage is the one node a phone cannot pay
 * for. Blink decodes every gif frame on the renderer main thread at native
 * size and then re-rasters every device pixel the image covers, per frame
 * advance: at DPR 3 a 390x844 stage is ~3 Mpx of raster per gif frame and the
 * 150vmax spiral square is ~14 Mpx. The 4x-throttled phone profile put one
 * bundled spiral gif (sp5, 500x375, 60 frames) at 3.1s of GPU-process time per
 * 8s where the CSS conic or the live loom cost 23-126ms, and a full-bleed gif
 * burst at 700ms GPU + 340ms decode per 8s. The scale trick (a half-size box
 * under transform:scale(2)) does NOT help - Chrome rasters at the ideal scale.
 *
 * The plate is the one technique that removes BOTH costs: the gif's FIRST
 * FRAME drawn once into a small canvas (long side PLATE_BACK_LONG, or the
 * gif's own size when smaller) that the stylesheet stretches over the stage.
 * No decoder runs, no per-frame raster, one texture. Read by the touch rung
 * only (sustained.js and oneshots.js branch on html.ae-touch, armed by
 * core/device.js from the arc-mobile decision); desktop keeps the animated
 * gif byte for byte. A plate that never loads is a transparent canvas over the
 * element's own backing - a dimmer wash, never a hole. */
export const PLATE_BACK_LONG = 640;
/** A url the plate stands in for: a .gif is decoded frame by frame, still or not. */
export const GIF_URL_RE = /\.gif(\?|#|$)/i;
export const isGifUrl = (u) => typeof u === 'string' && GIF_URL_RE.test(u);
/**
 * @param {string} url
 * @param {string} [cls]
 * @param {{backLong?:number, square?:boolean, onReady?:Function}} [o]
 *   square: a rotating plate must never bare a corner, so it is cover-cropped
 *   to a square; otherwise the canvas keeps the image's aspect and the
 *   stylesheet's object-fit does the covering.
 * @returns {HTMLCanvasElement|null} null without a DOM (callers fall back to mediaEl)
 */
export function plateEl(url, cls, o = {}) {
  if (!hasDom() || typeof Image !== 'function' || typeof url !== 'string' || !url) return null;
  let c = null;
  try {
    c = document.createElement('canvas');
    if (!c || typeof c.getContext !== 'function') return null;
  } catch { return null; }
  const cap = Math.max(64, (o.backLong | 0) || PLATE_BACK_LONG);
  const square = o.square === true;
  c.width = cap; c.height = cap;
  if (cls) c.className = cls;
  try { c.__aePlate = true; } catch { /* ignore */ }
  const src = new Image();
  try { src.decoding = 'async'; } catch { /* ignore */ }
  src.onload = () => {
    try {
      const nw = src.naturalWidth || cap, nh = src.naturalHeight || cap;
      const long = Math.min(cap, Math.max(nw, nh));
      let w, h;
      if (square) { w = long; h = long; }
      else if (nw >= nh) { w = long; h = Math.max(1, Math.round(long * nh / nw)); }
      else { h = long; w = Math.max(1, Math.round(long * nw / nh)); }
      c.width = w; c.height = h;
      const g = c.getContext('2d');
      if (!g) return;
      // a detached Image draws its FIRST frame; cover so nothing letterboxes
      const s = Math.max(w / nw, h / nh);
      const dw = nw * s, dh = nh * s;
      g.drawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
      try { c.__aePlateReady = true; } catch { /* ignore */ }
      if (typeof o.onReady === 'function') o.onReady(c);
    } catch { /* a plate that cannot draw stays transparent; never a throw */ }
  };
  src.src = url;
  try { c.__aePlateSrc = src; } catch { /* ignore */ }
  return c;
}

/** The media node for a pool url: <img> for stills/gifs, a muted, looping,
 *  inline-autoplaying <video> for mp4/webm. Same class, same `src`, same
 *  object-fit rules - callers never branch. Returns null with no DOM; never
 *  throws (a DOM double without video semantics just gets a bare element).
 *  A <video> is COUNTED against the shared decoder budget from here until the
 *  timer registry releases it. */
export function mediaEl(url, cls) {
  if (!hasDom()) return null;
  const video = isVideoUrl(url);
  const n = document.createElement(video ? 'video' : 'img');
  if (cls) n.className = cls;
  if (video) {
    liveVideos += 1;
    try { n.__aeVideo = true; } catch { /* a double may be frozen; the count still holds */ }
    try {
      n.muted = true; n.loop = true; n.autoplay = true; n.playsInline = true;
      n.setAttribute('muted', ''); n.setAttribute('loop', ''); n.setAttribute('autoplay', '');
      n.setAttribute('playsinline', ''); n.setAttribute('preload', 'auto');
      n.disablePictureInPicture = true;
    } catch { /* ignore */ }
  } else {
    try { n.decoding = 'async'; } catch { /* ignore */ }
  }
  if (url) n.src = url;
  if (video) {
    // muted autoplay is allowed everywhere, but a play() nudge covers the
    // engines that only honour the attribute on elements parsed from markup
    try { const p = n.play(); if (p && typeof p.catch === 'function') p.catch(() => {}); } catch { /* ignore */ }
  }
  return n;
}

/** Live probes (soft degrade, never withholding). Safe with no DOM. */
export function probeReducedMotion() {
  if (!hasWindow() || !window.matchMedia) return false;
  try { return !!window.matchMedia('(prefers-reduced-motion: reduce)').matches; } catch { return false; }
}
export function probeCoarsePointer() {
  if (!hasWindow() || !window.matchMedia) return false;
  try { return !!window.matchMedia('(pointer: coarse)').matches; } catch { return false; }
}

/**
 * A registry of timeouts / intervals / rAF loops / DOM nodes with one kill switch.
 * Nothing in the engine calls setTimeout directly — everything lands here so a
 * panic suspend really is instant.
 */
export function createTimers() {
  let timeouts = new Set();
  let intervals = new Set();
  let rafs = new Set();
  let nodes = new Set();
  let killed = false;

  function after(ms, fn) {
    if (killed) return 0;
    const id = setTimeout(() => { timeouts.delete(id); if (!killed) { try { fn(); } catch { /* swallow */ } } }, Math.max(0, ms | 0));
    timeouts.add(id);
    return id;
  }
  function every(ms, fn) {
    if (killed) return 0;
    const id = setInterval(() => { if (!killed) { try { fn(); } catch { /* swallow */ } } }, Math.max(16, ms | 0));
    intervals.add(id);
    return id;
  }
  function cancel(id) {
    if (timeouts.has(id)) { clearTimeout(id); timeouts.delete(id); }
    if (intervals.has(id)) { clearInterval(id); intervals.delete(id); }
  }
  /** A rAF loop: fn(dtSec, nowMs) -> false to stop. Falls back to setInterval headless. */
  function loop(fn) {
    if (killed) return { cancel() {} };
    let stop = false;
    let last = nowMs();
    const handle = { cancel() { stop = true; rafs.delete(handle); } };
    rafs.add(handle);
    const step = () => {
      if (stop || killed) { rafs.delete(handle); return; }
      const t = nowMs();
      const dt = Math.min(0.05, (t - last) / 1000);
      last = t;
      let keep = true;
      try { keep = fn(dt, t) !== false; } catch { keep = false; }
      if (!keep) { rafs.delete(handle); return; }
      if (hasWindow() && window.requestAnimationFrame) window.requestAnimationFrame(step);
      else after(33, step);
    };
    if (hasWindow() && window.requestAnimationFrame) window.requestAnimationFrame(step);
    else after(33, step);
    return handle;
  }
  /** Track a node so kill() removes it. */
  function own(node) { if (node) nodes.add(node); return node; }
  function release(node) {
    if (!node) return;
    nodes.delete(node);
    forgetVideo(node);                       // give the decoder back to the budget
    try { if (node.parentNode) node.parentNode.removeChild(node); } catch { /* ignore */ }
  }
  function kill() {
    for (const id of timeouts) clearTimeout(id);
    for (const id of intervals) clearInterval(id);
    for (const h of rafs) { try { h.cancel(); } catch { /* ignore */ } }
    for (const n of nodes) { forgetVideo(n); try { if (n.parentNode) n.parentNode.removeChild(n); } catch { /* ignore */ } }
    timeouts = new Set(); intervals = new Set(); rafs = new Set(); nodes = new Set();
  }
  return {
    after, every, cancel, loop, own, release, kill,
    get size() { return timeouts.size + intervals.size + rafs.size + nodes.size; },
    dispose() { killed = true; kill(); },
  };
}

/** Seeded scatter helpers (never Math.random on a replayable path). */
export const rand = (rng, lo, hi) => lo + (hi - lo) * rng();
export const randInt = (rng, lo, hi) => Math.floor(lo + (hi - lo + 1) * rng());
export const pickFrom = (rng, list) => (list && list.length ? list[Math.min(list.length - 1, Math.floor(rng() * list.length))] : null);

export default { hasDom, el, createTimers };
