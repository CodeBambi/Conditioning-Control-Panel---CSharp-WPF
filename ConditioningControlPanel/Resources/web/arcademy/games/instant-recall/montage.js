/* ============================================================================
 * games/instant-recall/montage.js - THE WALL, and THE TRUTH TAIL.
 *
 * Two things live here, and they are deliberately in the same file because one
 * is only honest if the other is:
 *
 *   THE WALL      a full-bleed FYP-style mosaic of the player's own media. One
 *                 grid, solved from the stage's aspect; every tile keeps its
 *                 OWN staggered seeded dwell and swaps itself to a fresh url on
 *                 its own beat, so the wall is never still and never in
 *                 lockstep. Built on the LIVE WINDOW discipline Lost & Found
 *                 paid for in blood and the decoder budget The Deep End
 *                 measured.
 *   THE LEDGER    the append-only truth tail every stop's question reads. Only
 *                 the emitter (index.js) appends, and it appends what the
 *                 ENGINE ACTUALLY RETURNED - never what it asked for. THE WALL
 *                 NEVER WRITES A LEDGER ENTRY: a tile swap is not an effect, it
 *                 is the room. That is why shedding a gif seat under a frame
 *                 lock, or a whole reshuffle on a resume, can never change the
 *                 answer to a question.
 *
 * ---------------------------------------------------------------------------
 * THE LIVE WINDOW (the pattern is L&F's; games never import each other, so the
 * physics are restated here and the numbers are re-tuned for this stage).
 *
 * Chromium keeps ONE decoder and ONE animation clock per image RESOURCE. So the
 * expensive unit is a DISTINCT ANIMATED URL, not a tile: N distinct animated
 * urls = N main-thread gif decodes per frame. Two tiles on the SAME url cost
 * one decoder - and cannot be desynchronised, because there is one clock.
 *
 * Therefore: at most `liveCap` distinct animated urls are on the wall at once,
 * drawn NO-REPEAT so nothing is ever in lockstep; every other seat wears a
 * STILL (or parks, for free, on a url the wall already animates); <video> is
 * budgeted a second time in ELEMENTS, because 2+ playing videos lock the whole
 * page to 30Hz; the media element is RECYCLED on a repaint (src swap) rather
 * than re-minted, because minting a media player is an IPC round trip; and a
 * frame GOVERNOR watches the achieved rAF cadence and sheds live seats (video
 * first, then gifs) when the page sits at half-rate.
 *
 * The governor uses Math.random ON PURPOSE: a frame-timing-driven choice can
 * never be deterministic, so it must not consume the class's seeded stream
 * (Law V). It only ever changes what a SLEEPER seat is showing.
 *
 * ---------------------------------------------------------------------------
 * ONE DRIVER, ONE CLOCK. Every dwell and every swap completion rides a SINGLE
 * interval that advances `wallMs`. Freezing the wall simply stops advancing it,
 * which is why the freeze can land MID-SWAP and stay there: the CSS swap is a
 * keyframe animation (never a transition), so `.is-frozen` pauses it exactly
 * where it was, and the completion that would have finished it is measured in
 * wall-time that is no longer moving. One timer, one kill switch, nothing that
 * can outlive destroy().
 *
 * ENGINE TARGETING NOTE: `.g-ir-face` is the only element the engine may ever
 * be handed - never `.g-ir-tile`, whose transform belongs to style.js. Nothing
 * in CORE targets the wall today; the rule stands for the decks.
 * ==========================================================================*/

import { makeTaggedRoll } from '../../core/rng.js';

export const MONTAGE = Object.freeze({
  /** Target CELL count by tier, before the density multiplier and before the
   *  aspect solve rounds it to a whole grid. */
  CELLS: Object.freeze({ 1: 9, 2: 12, 3: 16, 4: 20 }),
  CELLS_COARSE: Object.freeze({ 1: 6, 2: 9, 3: 12, 4: 12 }),
  CELLS_MIN: 6,
  CELLS_MAX: 30,
  /** The cell shape the solve aims at (w/h). 1.28 is what makes a 16:9 stage
   *  land on 4x3, a 4:3 stage on 3x3 and a portrait stage on 3x4. */
  CELL_RATIO: 1.28,
  COLS_MIN: 2,
  COLS_MAX: 8,
  ROWS_MIN: 2,
  ROWS_MAX: 7,

  /** Ceiling on DISTINCT animated urls on the wall at once. THE dial. */
  LIVE_LOOP_CAP: 12,
  LIVE_LOOP_CAP_LITE: 6,
  /** Gifs ignore prefers-reduced-motion, so the only honest answer is none. */
  LIVE_LOOP_CAP_REDUCED: 0,
  LIVE_LOOP_SHARE: 0.55,
  LIVE_LOOP_MIN: 4,
  LIVE_DRAW_TRIES: 4,

  /** <video>: 2+ playing lock the page to 30Hz. Gifs carry this wall. */
  VIDEO_TILE_CAP: 4,
  VIDEO_TILE_CAP_LITE: 2,

  /** THE DWELL: how long a tile holds a face, band 0 (cold) -> band 1 (dense).
   *  Reduced motion gets a longer, plainer beat and never a live loop. */
  DWELL_MS: Object.freeze([7000, 2500]),
  DWELL_MS_REDUCED: Object.freeze([11000, 7000]),
  DWELL_JITTER: 0.5,
  /** The swap itself. Reduced motion / motionLevel <= 1 = a plain crossfade. */
  SWAP_MS: 620,
  SWAP_MS_REDUCED: 900,
  SWAP_STYLES: Object.freeze(['fade', 'rise', 'slide']),
  /** At most this many tiles may be mid-swap at once (the transient decoder
   *  overshoot is bounded by exactly this number). */
  MAX_SWAPS_IN_FLIGHT: 4,
  /** THE RESHUFFLE (a resume): every tile turns over inside this window. */
  RESHUFFLE_MS: 1500,

  /** The driver's beat. */
  TICK_MS: 90,

  /** Progressive dressing: the decoders start in a queue, not a stampede. */
  DRESS_WINDOW_MS: 1500,
  DRESS_JITTER_MS: 800,

  /** THE FRAME GOVERNOR. */
  GOVERNOR: true,
  GOV_LOCK_X: 1.6,
  GOV_BAD_MS: 2200,
  GOV_SETTLE_MS: 1600,
  GOV_SHED_VIDEO_MIN_GIFS: 4,
  GOV_VIDEO_FLOOR: 0,
  GOV_GIF_SHED_STEP: 2,
  GOV_GIF_FLOOR: 4,
  GOV_GROW_MS: 9000,
  GOV_SAMPLES: 48,

  /** The truth tail: entries kept in memory, and nodes kept in the DOM. */
  LEDGER_CAP: 128,
  LEDGER_NODES: 24,
});

/* ----------------------------------------------------------------------------
 * THE GRID SOLVE (pure - the suite walks every aspect without a DOM)
 * -------------------------------------------------------------------------- */
/**
 * @param {Object} o  { w, h, tier, coarse, density }
 * @returns {{cols:number, rows:number, cells:number, want:number, aspect:number}}
 */
export function solveGrid(o = {}) {
  const tier = Math.max(1, Math.min(4, Math.round(Number(o.tier) || 1)));
  const mult = Number.isFinite(o.density) ? o.density : 1;
  const base = (o.coarse ? MONTAGE.CELLS_COARSE : MONTAGE.CELLS)[tier];
  const want = Math.max(MONTAGE.CELLS_MIN, Math.min(MONTAGE.CELLS_MAX, Math.round(base * mult)));
  const w = Number(o.w) > 0 ? Number(o.w) : 16;
  const h = Number(o.h) > 0 ? Number(o.h) : 9;
  const aspect = w / h;
  let cols = Math.round(Math.sqrt((want / MONTAGE.CELL_RATIO) * aspect));
  cols = Math.max(MONTAGE.COLS_MIN, Math.min(MONTAGE.COLS_MAX, cols || MONTAGE.COLS_MIN));
  let rows = Math.ceil(want / cols);
  rows = Math.max(MONTAGE.ROWS_MIN, Math.min(MONTAGE.ROWS_MAX, rows));
  return { cols, rows, cells: cols * rows, want, aspect };
}

/* ----------------------------------------------------------------------------
 * MEDIA
 * -------------------------------------------------------------------------- */
const VIDEO_EXT_RE = /\.(mp4|webm|m4v)(\?|#|$)/i;
const GIF_RE = /\.gif(\?|#|$)/i;

export function isVideoUrl(url) { return VIDEO_EXT_RE.test(String(url || '')); }
export function isGifUrl(url) { return GIF_RE.test(String(url || '')); }
/** Does this url cost a decoder and an animation clock? THE budget question. */
export function isAnimatedUrl(url) { return isVideoUrl(url) || isGifUrl(url); }

function el(tag, cls) {
  try {
    const n = document.createElement(tag);
    if (cls && n) n.className = cls;
    return n;
  } catch (e) { return null; }
}

/** Build the media element for a url. Never throws; broken media self-removes. */
export function mediaElFor(url) {
  if (!url) return null;
  if (isVideoUrl(url)) {
    const v = el('video', 'g-ir-media');
    if (!v) return null;
    v.muted = true; v.loop = true; v.autoplay = true; v.playsInline = true;
    try {
      v.setAttribute('muted', '');
      v.setAttribute('loop', '');
      v.setAttribute('playsinline', '');
      v.setAttribute('preload', 'metadata');
      v.setAttribute('disablepictureinpicture', '');
    } catch (e) { /* DOM double */ }
    try { v.disableRemotePlayback = true; } catch (e) { /* not everywhere */ }
    if (typeof v.addEventListener === 'function') {
      v.addEventListener('error', () => {
        try { v.removeAttribute('src'); if (v.load) v.load(); } catch (e) { /* ignore */ }
        try { if (v.parentNode) v.remove(); } catch (e) { /* ignore */ }
      });
    }
    v.src = url;
    if (typeof v.play === 'function') {
      try { const p = v.play(); if (p && p.catch) p.catch(() => {}); } catch (e) { /* autoplay policy */ }
    }
    return v;
  }
  const img = el('img', 'g-ir-media');
  if (!img) return null;
  img.alt = '';
  try {
    img.setAttribute('draggable', 'false');
    img.setAttribute('decoding', 'async');
  } catch (e) { /* DOM double */ }
  if (typeof img.addEventListener === 'function') {
    img.addEventListener('error', () => { try { if (img.parentNode) img.remove(); } catch (e) { /* ignore */ } });
  }
  img.src = url;
  return img;
}

/** Paint a url into a face, RECYCLING the existing element when the kind
 *  matches (a re-mint per repaint is an allocation storm for <img> and a whole
 *  media-player teardown for <video>). */
export function paintFace(face, url) {
  if (!face || !face.appendChild) return null;
  const kids = face.children || [];
  const existing = kids.length ? kids[0] : null;
  if (existing && existing._irUrl === url) return existing;
  if (!url) {
    if (existing) { try { existing.remove(); } catch (e) { /* ignore */ } }
    return null;
  }
  if (existing) {
    const wantVid = isVideoUrl(url);
    const isVid = String(existing.tagName || '').toUpperCase() === 'VIDEO';
    if (wantVid === isVid) {
      existing._irUrl = url;
      try {
        existing.src = url;
        if (isVid) {
          if (existing.load) existing.load();
          if (existing.play) { const p = existing.play(); if (p && p.catch) p.catch(() => {}); }
        }
        return existing;
      } catch (e) { /* fall through to replace */ }
    }
    try { existing.remove(); } catch (e) { /* ignore */ }
  }
  const media = mediaElFor(url);
  if (media) { media._irUrl = url; face.appendChild(media); }
  return media;
}

/* ============================================================================
 * THE LEDGER - the append-only truth tail.
 *
 * Entry shape: { t, channel, payload, variant, mode, seq }
 *   t        ms since the vigil opened (the class clock, not wall time)
 *   channel  A POOL KEY ('flash' | 'subliminal' | 'whisper' | 'spiral' | ...)
 *            or the pseudo-channel 'plant' (a real emission that fired over a
 *            freeze and must never become an answer key). NEVER an engine kind:
 *            two pool entries can ride one primitive and stay distinct answers.
 *   payload  { word } | { sting } | { assetId } | { plant, matched }
 *   variant  the engine variant that actually rendered
 *   mode     the stage's own mode at the moment of emission ('wall')
 *
 * The `.g-ir-ledger` node is aria-hidden and visually hidden with INLINE styles
 * (this file injects no CSS and must never write a bare `display:` rule).
 * ==========================================================================*/
export function createLedger(o = {}) {
  const node = o.node || null;
  const cap = Math.max(8, Number(o.cap) || MONTAGE.LEDGER_CAP);
  const nodeCap = Math.max(1, Number(o.nodeCap) || MONTAGE.LEDGER_NODES);
  const rows = [];
  let seq = 0;

  function render(entry) {
    if (!node || !node.appendChild) return;
    const line = el('span', 'g-ir-led');
    if (!line) return;
    const p = entry.payload || {};
    const what = p.word != null ? p.word
      : p.sting != null ? p.sting
        : p.assetId != null ? p.assetId : '';
    line.textContent = entry.t + ' ' + entry.channel + (what ? ' ' + what : '');
    try { line.setAttribute('data-ch', entry.channel); } catch (e) { /* DOM double */ }
    node.appendChild(line);
    /* re-read `children` every pass: it is a LIVE collection, and a snapshot
     * that never shrinks is an infinite loop wearing a trim's clothes. */
    for (let guard = 0; guard < 64; guard++) {
      const kids = node.children || [];
      if (kids.length <= nodeCap) break;
      const first = kids[0];
      if (!first || !first.remove) break;
      try { first.remove(); } catch (e) { break; }
    }
  }

  const api = {
    /** THE only way in. Returns the frozen entry (or null if it was refused). */
    append(entry) {
      if (!entry || !entry.channel) return null;
      const rec = Object.freeze({
        seq: seq++,
        t: Math.max(0, Math.round(Number(entry.t) || 0)),
        channel: String(entry.channel),
        payload: Object.freeze(Object.assign({}, entry.payload || {})),
        variant: entry.variant == null ? '' : String(entry.variant),
        mode: entry.mode == null ? '' : String(entry.mode),
      });
      rows.push(rec);
      while (rows.length > cap) rows.shift();
      render(rec);
      return rec;
    },
    /** The last N entries, oldest first. */
    tail(n) {
      const k = Math.max(0, Math.round(Number(n) || 0));
      return k ? rows.slice(Math.max(0, rows.length - k)) : rows.slice();
    },
    /** The most recent entry matching a channel name or a predicate. */
    lastOf(match) {
      const test = typeof match === 'function' ? match : (r) => r.channel === match;
      for (let i = rows.length - 1; i >= 0; i--) if (test(rows[i])) return rows[i];
      return null;
    },
    /** The most recent entries matching, newest first, up to `n`. */
    recent(match, n) {
      const test = typeof match === 'function' ? match : (r) => r.channel === match;
      const out = [];
      const k = Math.max(1, Math.round(Number(n) || 1));
      for (let i = rows.length - 1; i >= 0 && out.length < k; i--) if (test(rows[i])) out.push(rows[i]);
      return out;
    },
    all() { return rows.slice(); },
    get size() { return rows.length; },
    clear() {
      rows.length = 0;
      if (node) { try { node.textContent = ''; } catch (e) { /* ignore */ } }
    },
  };
  return api;
}

/** Visually hide a node without a stylesheet and without the toggle attribute
 *  the shell owns: inline, screen-reader-hidden, never painted, never hit. */
export function hideTruthNode(node) {
  if (!node || !node.style) return node;
  const s = node.style;
  try {
    s.setProperty('position', 'absolute');
    s.setProperty('width', '1px');
    s.setProperty('height', '1px');
    s.setProperty('overflow', 'hidden');
    s.setProperty('clip-path', 'inset(50%)');
    s.setProperty('white-space', 'nowrap');
    s.setProperty('opacity', '0');
    s.setProperty('pointer-events', 'none');
  } catch (e) { /* DOM double */ }
  try { node.setAttribute('aria-hidden', 'true'); } catch (e) { /* DOM double */ }
  return node;
}

/* ============================================================================
 * THE WALL
 * ==========================================================================*/

/**
 * @param {Object} o
 *   mount     the `.g-ir-montage` element
 *   seed      the class seed (tile dwells, swap styles, the seeded draw order)
 *   tier      1..4
 *   reduced   reduced motion (no live loops, plain crossfades, longer dwells)
 *   coarse    coarse pointer (fewer, bigger tiles)
 *   lite      low-power / motionLevel <= 1 (tighter budgets, plain crossfades)
 *   density   the density MULTIPLIER from `ir_density` (scales the cell count)
 *   timeScale the class's own test seam (1 in production)
 *   log       ctx.log
 */
export function createMontage(o = {}) {
  const mount = o.mount || null;
  const seed = String(o.seed == null ? 'instant_recall' : o.seed);
  const tier = Math.max(1, Math.min(4, Math.round(Number(o.tier) || 1)));
  const reduced = !!o.reduced;
  const coarse = !!o.coarse;
  const lite = !!o.lite;
  const plainSwap = reduced || lite;
  const say = typeof o.log === 'function' ? o.log : () => {};
  const roll = makeTaggedRoll(seed + '|ir-montage');
  const timeScale = Number(o.timeScale) > 0 ? Number(o.timeScale) : 1;

  const densityMult = Number.isFinite(o.density) ? o.density : 1;

  /* ---- the grid, solved from the stage's own box ----------------------- */
  function measure() {
    let w = 0;
    let h = 0;
    try {
      if (mount && typeof mount.getBoundingClientRect === 'function') {
        const r = mount.getBoundingClientRect();
        if (r) { w = Number(r.width) || 0; h = Number(r.height) || 0; }
      }
    } catch (e) { /* DOM double */ }
    if (!(w > 0 && h > 0)) {
      try {
        if (typeof window !== 'undefined' && window.innerWidth) { w = window.innerWidth; h = window.innerHeight; }
      } catch (e) { /* headless */ }
    }
    return { w: w > 0 ? w : 16, h: h > 0 ? h : 9 };
  }
  const box = measure();
  const grid = solveGrid({ w: box.w, h: box.h, tier, coarse, density: densityMult });
  const count = grid.cells;

  /** Budgets - solved once, from the dealt cell count. */
  const liveCap = reduced ? MONTAGE.LIVE_LOOP_CAP_REDUCED
    : Math.max(0, Math.min(
      lite ? MONTAGE.LIVE_LOOP_CAP_LITE : MONTAGE.LIVE_LOOP_CAP,
      Math.max(MONTAGE.LIVE_LOOP_MIN, Math.round(count * MONTAGE.LIVE_LOOP_SHARE)),
    ));
  const videoCap = Math.max(0, Math.min(
    lite ? MONTAGE.VIDEO_TILE_CAP_LITE : MONTAGE.VIDEO_TILE_CAP,
    liveCap,
  ));

  /* logical tiles - one per cell, built once and re-dressed forever */
  const tiles = [];
  for (let i = 0; i < count; i++) {
    tiles.push({
      i,
      url: null, remote: false, live: false, isVideo: false,
      seq: 0, node: null, faces: [null, null], front: 0, _liveUrl: null,
      swapping: false,
      dueAt: 0,
      jit: roll('dwell'),
      styleRing: [
        pickStyle(roll('style')), pickStyle(roll('style')), pickStyle(roll('style')),
      ],
      styleAt: 0,
      sig: 1 + Math.floor(roll('sig') * 6),
      hue: Math.round(roll('hue') * 300),
    });
  }
  function pickStyle(r) {
    if (plainSwap) return 'fade';
    const list = MONTAGE.SWAP_STYLES;
    const f = r < 0 ? 0 : r > 0.999999 ? 0.999999 : r;
    return list[Math.floor(f * list.length)];
  }

  const liveUse = new Map();
  let videoTiles = 0;
  let destroyed = false;
  let frozen = false;
  let containers = [];
  let pool = null;
  let band = 0;
  let wallMs = 0;
  let driver = 0;
  let swaps = 0;
  let reshuffles = 0;
  const inFlight = new Map();       // tile.i -> { tile, doneAt, oldUrl, oldFace }
  const queue = [];
  const dressTimers = new Set();

  /* ---------------------------------------------------------- live budget */
  function releaseLive(tile) {
    const url = tile._liveUrl;
    if (!url) return;
    tile._liveUrl = null;
    const rec = liveUse.get(url);
    if (!rec) return;
    if (rec.n <= 1) liveUse.delete(url); else rec.n -= 1;
  }
  function acquireLive(tile, url) {
    tile._liveUrl = url;
    const rec = liveUse.get(url);
    if (rec) rec.n += 1; else liveUse.set(url, { n: 1 });
  }
  /** Would giving this tile `url` mint a decoder we cannot afford? Adopting a
   *  url the wall already animates is free - same resource, same clock. */
  function liveBlocked(tile, url) {
    if (liveUse.has(url)) return false;
    const rec = tile._liveUrl ? liveUse.get(tile._liveUrl) : null;
    const freeing = rec && rec.n <= 1 ? 1 : 0;
    return (liveUse.size - freeing) >= liveCap;
  }

  /* ----------------------------------------------------------------- paint */
  function frontFace(tile) { return tile.faces[tile.front] || null; }
  function backFace(tile) { return tile.faces[1 - tile.front] || null; }

  /**
   * Give a tile a url, INSTANTLY (no swap animation). Used for the first dress,
   * for the governor's sleeper seats and for a refused draw. Returns true when
   * the look took; a budget refusal is not a failure - the caller rests the
   * seat on a still instead.
   */
  function setUrl(tile, draw, opts) {
    if (!tile || destroyed) return false;
    const url = draw && draw.url ? draw.url : null;
    const isVid = isVideoUrl(url);
    const anim = isAnimatedUrl(url);
    if (anim && liveBlocked(tile, url)) return false;
    if (isVid && !tile.isVideo && videoTiles >= videoCap) return false;

    releaseLive(tile);
    if (tile.isVideo && !isVid) videoTiles = Math.max(0, videoTiles - 1);
    if (!tile.isVideo && isVid) videoTiles += 1;
    tile.isVideo = isVid;
    tile.live = anim;
    tile.url = url;
    tile.remote = !!(draw && draw.remote);
    if (anim) acquireLive(tile, url);
    tile.seq = (tile.seq | 0) + 1;

    const wait = opts && opts.paintDelayMs > 0 ? (opts.paintDelayMs | 0) : 0;
    if (!wait) { paintFace(frontFace(tile), tile.url); return true; }
    const mySeq = tile.seq;
    const h = setTimeout(() => {
      dressTimers.delete(h);
      if (destroyed || tile.seq !== mySeq) return;   // a swap got here first
      paintFace(frontFace(tile), tile.url);
    }, Math.max(0, Math.round(wait * timeScale)));
    dressTimers.add(h);
    return true;
  }

  /* ------------------------------------------------------------------ draw */
  function poolNext(kind) {
    try { return pool && typeof pool.next === 'function' ? (pool.next(kind) || null) : null; }
    catch (e) { return null; }
  }
  /** An ANIMATED url the wall is not already animating (no-repeat = no lockstep). */
  function drawLive() {
    if (liveCap <= 0) return null;
    for (let i = 0; i < MONTAGE.LIVE_DRAW_TRIES; i++) {
      const got = poolNext('loop');
      if (got && got.url && isAnimatedUrl(got.url) && !liveUse.has(got.url)) return got;
    }
    return null;
  }
  /** Something that costs no decoder: a real still, else park on a live gif. */
  function drawSleeper() {
    const got = poolNext('still');
    if (got && got.url && !isAnimatedUrl(got.url)) return got;
    const parks = [];
    for (const url of liveUse.keys()) if (!isVideoUrl(url)) parks.push(url);
    if (parks.length) return { url: parks[Math.floor(Math.random() * parks.length)], remote: false };
    return null;
  }

  /** How many live seats the current density band wants. */
  function liveWant() {
    if (liveCap <= 0) return 0;
    return Math.max(0, Math.min(liveCap, Math.round(liveCap * (0.42 + 0.58 * band))));
  }
  function liveSeats() { return tiles.filter((t) => t.live).length; }
  /** The next face this tile should wear: live while the band still wants one. */
  function drawFor(tile) {
    if (!pool) return null;
    const want = liveWant();
    const have = liveSeats();
    const wantLive = tile.live ? have <= want : have < want;
    if (wantLive) { const got = drawLive(); if (got) return got; }
    return drawSleeper() || drawLive();
  }

  /* --------------------------------------------------------------- dwells */
  function dwellMs(tile) {
    const bandBase = reduced ? MONTAGE.DWELL_MS_REDUCED : MONTAGE.DWELL_MS;
    const base = bandBase[0] + (bandBase[1] - bandBase[0]) * (band < 0 ? 0 : band > 1 ? 1 : band);
    const j = 1 - MONTAGE.DWELL_JITTER / 2 + MONTAGE.DWELL_JITTER * tile.jit;
    return Math.max(900, Math.round(base * j));
  }
  function swapMs() { return plainSwap ? MONTAGE.SWAP_MS_REDUCED : MONTAGE.SWAP_MS; }

  /* ------------------------------------------------------------- the swap */
  function startSwap(tile) {
    if (destroyed || !pool || tile.swapping) return false;
    const got = drawFor(tile);
    if (!got || !got.url || got.url === tile.url) {
      tile.dueAt = wallMs + dwellMs(tile);
      return false;
    }
    const back = backFace(tile);
    if (!back) { return setUrl(tile, got); }

    const isVid = isVideoUrl(got.url);
    const anim = isAnimatedUrl(got.url);
    if (anim && liveBlocked(tile, got.url)) {
      const still = drawSleeper();
      if (!still) { tile.dueAt = wallMs + dwellMs(tile); return false; }
      return startSwapTo(tile, still);
    }
    if (isVid && !tile.isVideo && videoTiles >= videoCap) {
      const still = drawSleeper();
      if (!still) { tile.dueAt = wallMs + dwellMs(tile); return false; }
      return startSwapTo(tile, still);
    }
    return startSwapTo(tile, got);
  }

  function startSwapTo(tile, draw) {
    const back = backFace(tile);
    if (!back) return setUrl(tile, draw);
    const url = draw.url;
    const style = tile.styleRing[tile.styleAt % tile.styleRing.length];
    tile.styleAt += 1;

    paintFace(back, url);
    const oldUrl = tile.url;
    const oldFace = frontFace(tile);
    /* release the OUTGOING seat first so the budget sees the swap as a move,
     * not a doubling; the outgoing element lives on for swapMs, which is the
     * bounded transient MAX_SWAPS_IN_FLIGHT exists to cap. */
    releaseLive(tile);
    const isVid = isVideoUrl(url);
    if (tile.isVideo && !isVid) videoTiles = Math.max(0, videoTiles - 1);
    if (!tile.isVideo && isVid) videoTiles += 1;
    tile.isVideo = isVid;
    tile.live = isAnimatedUrl(url);
    tile.url = url;
    tile.remote = !!draw.remote;
    if (tile.live) acquireLive(tile, url);
    tile.seq = (tile.seq | 0) + 1;
    tile.swapping = true;
    swaps += 1;

    try {
      if (tile.node) {
        tile.node.setAttribute('data-swap', style);
        tile.node.classList.add('is-swapping');
      }
      if (oldFace && oldFace.classList) { oldFace.classList.remove('is-in'); oldFace.classList.add('is-out'); }
      if (back.classList) { back.classList.remove('is-out'); back.classList.add('is-in'); }
      if (tile.node && tile.node.style) tile.node.style.setProperty('--ir-swap', swapMs() + 'ms');
    } catch (e) { /* DOM double */ }

    inFlight.set(tile.i, { tile, doneAt: wallMs + swapMs(), oldFace, oldUrl });
    return true;
  }

  function finishSwap(rec) {
    const tile = rec.tile;
    inFlight.delete(tile.i);
    tile.swapping = false;
    tile.front = 1 - tile.front;
    try {
      if (tile.node) { tile.node.classList.remove('is-swapping'); tile.node.removeAttribute('data-swap'); }
      const f = frontFace(tile);
      if (f && f.classList) { f.classList.remove('is-in'); f.classList.remove('is-out'); }
      if (rec.oldFace && rec.oldFace.classList) { rec.oldFace.classList.remove('is-in'); rec.oldFace.classList.add('is-out'); }
    } catch (e) { /* DOM double */ }
    /* an ANIMATED outgoing url must lose its element or its decoder outlives
     * the seat that paid for it; a still is kept for recycling. */
    if (isAnimatedUrl(rec.oldUrl)) paintFace(rec.oldFace, null);
    tile.dueAt = wallMs + dwellMs(tile);
  }

  /* ------------------------------------------------------------ the driver */
  function step() {
    if (destroyed || frozen) return;
    wallMs += MONTAGE.TICK_MS;
    for (const rec of Array.from(inFlight.values())) {
      if (wallMs >= rec.doneAt) finishSwap(rec);
    }
    for (const tile of tiles) {
      if (tile.swapping || queue.indexOf(tile) >= 0) continue;
      if (wallMs >= tile.dueAt) queue.push(tile);
    }
    let guard = 0;
    while (queue.length && inFlight.size < MONTAGE.MAX_SWAPS_IN_FLIGHT && guard++ < 32) {
      const tile = queue.shift();
      if (!tile || tile.swapping) continue;
      startSwap(tile);
    }
  }
  function startDriver() {
    if (driver || destroyed) return false;
    try {
      driver = setInterval(step, Math.max(16, Math.round(MONTAGE.TICK_MS * timeScale)));
    } catch (e) { driver = 0; return false; }
    return true;
  }
  function stopDriver() {
    if (!driver) return;
    try { clearInterval(driver); } catch (e) { /* ignore */ }
    driver = 0;
  }

  /* ------------------------------------------------------------- structure */
  function clearContainers() {
    for (const c of containers) { try { c.remove(); } catch (e) { /* ignore */ } }
    containers = [];
    for (const tile of tiles) { tile.node = null; tile.faces = [null, null]; }
  }

  function buildTileEl(tile) {
    const node = el('div', 'g-ir-tile');
    if (!node) return null;
    try {
      node.setAttribute('data-i', String(tile.i));
      node.setAttribute('aria-hidden', 'true');
    } catch (e) { /* DOM double */ }
    /* Seeded look vars for style.js. THEY ARE NOT A FILTER BUDGET (web CLAUDE.md
     * trap 36): a `filter:` on a face that may hold a live <video> decode is a
     * full GPU pass over every decoded frame, per tile, per frame. Tint with
     * plain alpha over the face, never with hue-rotate on the media. */
    if (node.style) {
      node.style.setProperty('--ir-i', String(tile.i));
      node.style.setProperty('--ir-sig', String(tile.sig));
      node.style.setProperty('--ir-hue', tile.hue + 'deg');
    }
    /* TWO FACES per tile, and the back one starts hidden: `.is-out` is the
     * hidden state and the absence of a class is the visible one, so the swap
     * animation ends on exactly the state the classes already describe. */
    const a = el('div', 'g-ir-face');
    const b = el('div', 'g-ir-face g-ir-face-b is-out');
    if (a) node.appendChild(a);
    if (b) node.appendChild(b);
    tile.node = node;
    tile.faces = [a, b];
    tile.front = 0;
    return node;
  }

  /** Build the wall. One grid, edge to edge; the tiles outlive nothing else. */
  function build() {
    if (destroyed || !mount) return null;
    clearContainers();
    try {
      mount.setAttribute('data-grid', grid.cols + 'x' + grid.rows);
      mount.setAttribute('data-layout', 'wall');
    } catch (e) { /* DOM double */ }
    if (mount.style) {
      mount.style.setProperty('--ir-cols', String(grid.cols));
      mount.style.setProperty('--ir-rows', String(grid.rows));
    }
    const wall = el('div', 'g-ir-wall');
    if (!wall) return null;
    if (wall.style) {
      wall.style.setProperty('--ir-cols', String(grid.cols));
      wall.style.setProperty('--ir-rows', String(grid.rows));
    }
    for (let i = 0; i < count; i++) {
      const node = buildTileEl(tiles[i]);
      if (!node) continue;
      if (node.style) {
        node.style.setProperty('--ir-col', String(i % grid.cols));
        node.style.setProperty('--ir-row', String(Math.floor(i / grid.cols)));
      }
      wall.appendChild(node);
    }
    mount.appendChild(wall);
    containers.push(wall);
    for (const tile of tiles) paintFace(frontFace(tile), tile.url);
    if (frozen) applyFreeze();
    return wall;
  }

  /* ----------------------------------------------------------------- dress */
  /** The pool landed: dress the wall, staggered, live seats first, and give
   *  every tile its own first dwell so nothing is ever in lockstep. */
  function dress(p) {
    pool = p || null;
    if (!pool || destroyed) return 0;
    const want = liveWant();
    let live = 0;
    let dressed = 0;
    for (let i = 0; i < tiles.length; i++) {
      const tile = tiles[i];
      const delay = Math.round((MONTAGE.DRESS_WINDOW_MS * i) / Math.max(1, tiles.length)
        + roll('dress') * MONTAGE.DRESS_JITTER_MS);
      let got = null;
      if (live < want) { got = drawLive(); if (got) live += 1; }
      if (!got) got = drawSleeper();
      if (got && setUrl(tile, got, { paintDelayMs: delay })) dressed += 1;
      /* the FIRST dwell is spread over one whole dwell period so the wall
       * turns over continuously instead of blinking as one block (Law III) */
      tile.dueAt = wallMs + Math.round(dwellMs(tile) * (0.25 + 0.75 * ((i + tile.jit) / Math.max(1, tiles.length))));
    }
    say('wall dressed: ' + dressed + '/' + tiles.length + ' tiles on a ' + grid.cols + 'x' + grid.rows
      + ' grid, live cap ' + liveCap + ', video cap ' + videoCap + (reduced ? ' (reduced motion)' : ''));
    return dressed;
  }

  /* ------------------------------------------------------------- reshuffle */
  /** THE RESHUFFLE (a resume). Every tile turns over inside RESHUFFLE_MS,
   *  staggered - the wall's answer to "a new layout on resume". It writes NO
   *  ledger entry: the wall is the room, not an effect. */
  function reshuffle() {
    if (destroyed) return 0;
    reshuffles += 1;
    const n = Math.max(1, tiles.length);
    for (let i = 0; i < tiles.length; i++) {
      const tile = tiles[i];
      if (tile.swapping) continue;
      tile.dueAt = wallMs + Math.round((MONTAGE.RESHUFFLE_MS * i) / n + tile.jit * 180);
    }
    return tiles.length;
  }

  /* ---------------------------------------------------------------- freeze */
  function applyFreeze() {
    if (!mount) return;
    try {
      if (frozen) mount.classList.add('is-frozen'); else mount.classList.remove('is-frozen');
      if (mount.setAttribute) mount.setAttribute('data-frozen', frozen ? '1' : '0');
    } catch (e) { /* DOM double */ }
    for (const tile of tiles) {
      for (const face of tile.faces) {
        const kids = (face && face.children) || [];
        const m = kids.length ? kids[0] : null;
        if (!m || String(m.tagName || '').toUpperCase() !== 'VIDEO') continue;
        try {
          if (frozen) { if (m.pause) m.pause(); }
          else if (m.play) { const p = m.play(); if (p && p.catch) p.catch(() => {}); }
        } catch (e) { /* ignore */ }
      }
    }
  }
  /** THE FREEZE: the wall stops dead, MID-SWAP. The class clock does not (Law I). */
  function freeze(on) {
    const want = !!on;
    if (want === frozen) return frozen;
    frozen = want;
    applyFreeze();
    return frozen;
  }

  /* ------------------------------------------------------------- governor */
  const gov = {
    on: false, raf: 0, base: Infinity, med: 0,
    badSince: 0, lastShed: 0, healthySince: 0,
    shedVideos: 0, shedGifs: 0, regrown: 0,
  };
  const gaps = [];
  let govLast = 0;

  /** Rest a seat on something free. Never a ledger write - only pixels. */
  function restSeat(tile) {
    if (!tile || tile.swapping) return false;
    const got = drawSleeper();
    return setUrl(tile, got || { url: null });
  }
  function shedVideoSeat() {
    const vids = tiles.filter((t) => t.isVideo && !t.swapping);
    if (vids.length <= MONTAGE.GOV_VIDEO_FLOOR) return false;
    const gifLive = tiles.reduce((n, t) => n + ((t.live && !t.isVideo) ? 1 : 0), 0);
    if (gifLive < MONTAGE.GOV_SHED_VIDEO_MIN_GIFS) return false;
    return restSeat(vids[Math.floor(Math.random() * vids.length)]);
  }
  function shedGifSeat() {
    const urlTiles = new Map();
    const gifUrls = new Set();
    for (const t of tiles) {
      if (!t.live || t.isVideo || !t.url) continue;
      gifUrls.add(t.url);
      urlTiles.set(t.url, (urlTiles.get(t.url) | 0) + 1);
    }
    if (gifUrls.size <= MONTAGE.GOV_GIF_FLOOR) return false;
    const lives = tiles.filter((t) => t.live && !t.isVideo && t.url && !t.swapping);
    if (!lives.length) return false;
    /* a decoder only dies with its LAST tile, so shed sole holders first */
    const solo = lives.filter((t) => (urlTiles.get(t.url) | 0) === 1);
    const from = solo.length ? solo : lives;
    return restSeat(from[Math.floor(Math.random() * from.length)]);
  }
  function growGifSeat() {
    const sleepers = tiles.filter((t) => !t.live && !t.swapping);
    if (!sleepers.length) return false;
    const got = drawLive();
    if (!got || !got.url || isVideoUrl(got.url) || !isAnimatedUrl(got.url)) return false;
    return setUrl(sleepers[Math.floor(Math.random() * sleepers.length)], got);
  }

  function govTick(ts) {
    if (!gov.on) return;
    try { gov.raf = requestAnimationFrame(govTick); } catch (e) { gov.on = false; return; }
    if (govLast) {
      const gap = ts - govLast;
      const hidden = (typeof document !== 'undefined' && document.hidden);
      if (gap > 0 && gap < 500 && !frozen && !hidden) {
        gaps.push(gap);
        if (gaps.length > 90) gaps.shift();
      } else {
        gaps.length = 0;
      }
    }
    govLast = ts;
    if (gaps.length < MONTAGE.GOV_SAMPLES) return;
    const s = gaps.slice().sort((x, y) => x - y);
    const med = s[s.length >> 1];
    gov.med = med;
    if (med < gov.base) gov.base = med;
    const locked = med >= gov.base * MONTAGE.GOV_LOCK_X;
    if (!locked) {
      gov.badSince = 0;
      if (!gov.healthySince) gov.healthySince = ts;
      if (gov.shedGifs > 0 && !frozen && ts - gov.healthySince > MONTAGE.GOV_GROW_MS) {
        gov.healthySince = ts;
        if (growGifSeat()) { gov.shedGifs -= 1; gov.regrown += 1; }
      }
      return;
    }
    gov.healthySince = 0;
    if (!gov.badSince) { gov.badSince = ts; return; }
    if (ts - gov.badSince < MONTAGE.GOV_BAD_MS) return;
    if (ts - gov.lastShed < MONTAGE.GOV_SETTLE_MS) return;
    if (frozen || destroyed) return;
    gov.lastShed = ts;
    if (shedVideoSeat()) {
      gov.shedVideos += 1;
      say('governor: shed a video seat (median ' + med.toFixed(1) + 'ms vs base ' + gov.base.toFixed(1) + 'ms)');
      return;
    }
    let g = 0;
    for (let i = 0; i < MONTAGE.GOV_GIF_SHED_STEP; i++) { if (shedGifSeat()) g += 1; }
    if (g) {
      gov.shedGifs += g;
      say('governor: shed ' + g + ' gif seat(s) (median ' + med.toFixed(1) + 'ms vs base ' + gov.base.toFixed(1) + 'ms)');
    }
  }

  const api = {
    /* ---- structure ---- */
    build,
    grid() { return { cols: grid.cols, rows: grid.rows, cells: grid.cells }; },
    /** The engine's ONLY legal targets on this stage (never `.g-ir-tile`). */
    faces() { return tiles.map((t) => frontFace(t)).filter(Boolean); },
    facesOf(list) { return (list || []).map((t) => t && frontFace(t)).filter(Boolean); },
    tiles() { return tiles.slice(); },
    mountEl() { return mount; },

    /* ---- media ---- */
    dress,
    reshuffle,
    /** The density band drives the dwell and how many seats are alive. */
    setBand(b) {
      const v = Number(b);
      band = !Number.isFinite(v) ? 0 : v < 0 ? 0 : v > 1 ? 1 : v;
      /* growth converges through the swaps; only an OVERSHOOT is corrected at
       * once, because an overshoot is a budget breach and not a look. */
      let shed = 0;
      const want = liveWant();
      if (liveSeats() > want) {
        for (const tile of tiles) {
          if (liveSeats() <= want || shed >= MONTAGE.MAX_SWAPS_IN_FLIGHT) break;
          if (tile.live && !tile.swapping && restSeat(tile)) shed += 1;
        }
      }
      return shed;
    },
    band() { return band; },

    /* ---- lifecycle ---- */
    freeze,
    frozen() { return frozen; },
    startGovernor() {
      if (!MONTAGE.GOVERNOR || gov.on || reduced) return false;
      if (typeof requestAnimationFrame !== 'function') return false;   // headless
      gov.on = true;
      govLast = 0;
      try { gov.raf = requestAnimationFrame(govTick); } catch (e) { gov.on = false; return false; }
      return true;
    },
    stopGovernor() {
      gov.on = false;
      if (gov.raf && typeof cancelAnimationFrame === 'function') {
        try { cancelAnimationFrame(gov.raf); } catch (e) { /* ignore */ }
      }
      gov.raf = 0;
    },
    start() { return startDriver(); },
    stop() { stopDriver(); },
    destroy() {
      destroyed = true;
      api.stopGovernor();
      stopDriver();
      for (const h of dressTimers) { try { clearTimeout(h); } catch (e) { /* ignore */ } }
      dressTimers.clear();
      inFlight.clear();
      queue.length = 0;
      clearContainers();
      liveUse.clear();
      videoTiles = 0;
      pool = null;
    },

    /* ---- test seams (never read by the shell) ---- */
    /** Drive the wall without a wall clock. */
    advance(ms) {
      const n = Math.max(0, Math.round(Number(ms) || 0) / MONTAGE.TICK_MS);
      for (let i = 0; i < n; i++) step();
      return wallMs;
    },
    wallClock() { return wallMs; },
    swapsInFlight() { return inFlight.size; },

    /* ---- diagnostics (never read by the shell) ---- */
    diagnostics() {
      return {
        grid: { cols: grid.cols, rows: grid.rows, cells: grid.cells, aspect: +grid.aspect.toFixed(3) },
        frozen, band, count, liveCap, videoCap,
        live: liveSeats(),
        liveUrls: liveUse.size,
        videoTiles,
        want: liveWant(),
        swaps,
        reshuffles,
        inFlight: inFlight.size,
        queued: queue.length,
        driver: !!driver,
        wallMs,
        containers: containers.length,
        dressed: tiles.filter((t) => !!t.url).length,
        governor: {
          on: gov.on, base: Number.isFinite(gov.base) ? gov.base : 0, med: gov.med,
          shedVideos: gov.shedVideos, shedGifs: gov.shedGifs, regrown: gov.regrown,
        },
      };
    },
  };
  return api;
}

export default {
  createMontage, createLedger, MONTAGE, solveGrid, isAnimatedUrl, isVideoUrl,
  paintFace, hideTruthNode,
};
