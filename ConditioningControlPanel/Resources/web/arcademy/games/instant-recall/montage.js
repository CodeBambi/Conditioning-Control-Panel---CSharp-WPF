/* ============================================================================
 * games/instant-recall/montage.js - THE STREAM, and THE TRUTH TAIL.
 *
 * Two things live here, and they are deliberately in the same file because one
 * is only honest if the other is:
 *
 *   THE MONTAGE   a DOM-only wall of pool media on three rotating stage
 *                 layouts (rows / mosaic / swirl), built on the LIVE WINDOW
 *                 discipline Lost & Found paid for in blood.
 *   THE LEDGER    the append-only truth tail every stop's question reads. Only
 *                 the emitter (index.js) appends, and it appends what the
 *                 ENGINE ACTUALLY RETURNED - never what it asked for. The
 *                 montage's own seat maintenance (the governor, the density
 *                 band, a layout rebuild) NEVER writes a ledger entry, which is
 *                 why shedding a gif seat under a frame lock can never change
 *                 the answer to a question.
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
 * NO WRAP CLONES. L&F's rows are toroidal marquees and every tile exists 2-3
 * times over; ours are engine `row_drift` targets with a bounded sway, so a
 * tile is exactly one element. That makes `maxReps` 1 and the element ceilings
 * collapse onto the url ceilings - strictly cheaper than the board that taught
 * us the lesson.
 *
 * ENGINE TARGETING NOTE: `glitch_swap` writes its own filter/animation onto its
 * targets, and style.js owns `.g-ir-tile`'s transform (the swirl orbit is a
 * transform). So the engine is ALWAYS handed the inner `.g-ir-face`, never the
 * tile - the same rule The Deep End keeps for its tile faces.
 * ==========================================================================*/

import { makeTaggedRoll } from '../../core/rng.js';

export const MONTAGE = Object.freeze({
  /** Tile count by tier, before the density multiplier. The tile COUNT is a
   *  tuned dial; the tile SIZE breathes with the window (style.js). */
  TILES: Object.freeze({ 1: 18, 2: 26, 3: 34, 4: 44 }),
  TILES_COARSE: Object.freeze({ 1: 12, 2: 16, 3: 22, 4: 28 }),
  TILES_MIN: 8,
  TILES_MAX: 56,

  /** Ceiling on DISTINCT animated urls on the wall at once. THE dial. */
  LIVE_LOOP_CAP: 20,
  LIVE_LOOP_CAP_LITE: 10,
  /** Gifs ignore prefers-reduced-motion, so the only honest answer is none. */
  LIVE_LOOP_CAP_REDUCED: 0,
  LIVE_LOOP_SHARE: 0.55,
  LIVE_LOOP_MIN: 6,
  LIVE_ELEMENT_CEIL: 44,
  LIVE_DRAW_TRIES: 4,

  /** <video>: 2+ playing lock the page to 30Hz. Gifs carry this wall. */
  VIDEO_TILE_CAP: 4,
  VIDEO_TILE_CAP_LITE: 2,
  VIDEO_ELEMENT_CEIL: 24,

  /** A turn burst applies at most this many tiles per tick; the rest follow on
   *  frame-spaced ticks (one synchronous batch is half a frame budget). */
  SWAP_APPLY_CHUNK: 3,

  /** Progressive dressing: the decoders start in a queue, not a stampede, and
   *  every animated tile starts its clock on its own tick. */
  DRESS_WINDOW_MS: 1500,
  DRESS_JITTER_MS: 800,

  /** Rows layout. */
  ROWS_MIN: 3,
  ROWS_MAX: 7,
  /** Mosaic layout: columns solved from the tile count. */
  MOSAIC_COLS_MIN: 4,
  MOSAIC_COLS_MAX: 9,
  /** Swirl layout: how many turns of the spiral the tiles are spread over. */
  SWIRL_TURNS: 2.4,
  SWIRL_RAD_MIN: 0.16,
  SWIRL_RAD_MAX: 0.92,

  /** THE FRAME GOVERNOR. */
  GOVERNOR: true,
  GOV_LOCK_X: 1.6,
  GOV_BAD_MS: 2200,
  GOV_SETTLE_MS: 1600,
  GOV_SHED_VIDEO_MIN_GIFS: 4,
  GOV_VIDEO_FLOOR: 0,
  GOV_GIF_SHED_STEP: 2,
  GOV_GIF_FLOOR: 6,
  GOV_GROW_MS: 9000,
  GOV_SAMPLES: 48,

  /** The truth tail: entries kept in memory, and nodes kept in the DOM. */
  LEDGER_CAP: 128,
  LEDGER_NODES: 24,
});

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
 * Entry shape (the dossier's): { t, channel, payload, variant, mode, seq }
 *   t        ms since the vigil opened (the class clock, not wall time)
 *   channel  'sub_flash' | 'audio_trigger' | 'bubble_field' | 'wash' | ...
 *            plus the pseudo-channel 'layout' (a stage change is a real event
 *            and MODE questions read it), which is NOT in EFFECT_VOCAB and so
 *            can never be a LAST_EFFECT answer.
 *   payload  { word } | { sting } | { color } | { assetId } | { layout }
 *   variant  the engine variant that actually rendered
 *   mode     the stage layout at the moment of emission
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
        : p.layout != null ? p.layout
          : p.color != null ? p.color
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
 * THE MONTAGE
 * ==========================================================================*/

/**
 * @param {Object} o
 *   mount     the `.g-ir-montage` element
 *   seed      the class seed (tile signatures + the seeded draw order)
 *   tier      1..4
 *   reduced   reduced motion (no live loops, no swirl)
 *   coarse    coarse pointer (fewer, bigger tiles)
 *   lite      low-power / motionLevel <= 1 (tighter budgets)
 *   density   the density MULTIPLIER from `ir_density` (scales the tile count)
 *   log       ctx.log
 */
export function createMontage(o = {}) {
  const mount = o.mount || null;
  const seed = String(o.seed == null ? 'instant_recall' : o.seed);
  const tier = Math.max(1, Math.min(4, Math.round(Number(o.tier) || 1)));
  const reduced = !!o.reduced;
  const coarse = !!o.coarse;
  const lite = !!o.lite;
  const say = typeof o.log === 'function' ? o.log : () => {};
  const roll = makeTaggedRoll(seed + '|ir-montage');

  const densityMult = Number.isFinite(o.density) ? o.density : 1;
  const baseCount = (coarse ? MONTAGE.TILES_COARSE : MONTAGE.TILES)[tier];
  const count = Math.max(MONTAGE.TILES_MIN,
    Math.min(MONTAGE.TILES_MAX, Math.round(baseCount * densityMult)));

  /** Budgets - solved once, from the dealt tile count. */
  const liveCap = reduced ? MONTAGE.LIVE_LOOP_CAP_REDUCED
    : Math.max(0, Math.min(
      lite ? MONTAGE.LIVE_LOOP_CAP_LITE : MONTAGE.LIVE_LOOP_CAP,
      Math.max(MONTAGE.LIVE_LOOP_MIN, Math.round(count * MONTAGE.LIVE_LOOP_SHARE)),
      MONTAGE.LIVE_ELEMENT_CEIL,
    ));
  const videoCap = Math.max(0, Math.min(
    lite ? MONTAGE.VIDEO_TILE_CAP_LITE : MONTAGE.VIDEO_TILE_CAP,
    MONTAGE.VIDEO_ELEMENT_CEIL,
    liveCap,
  ));

  /* logical tiles - they OUTLIVE a layout rebuild (the look is the tile's, the
   * element is the layout's), so the live ledger stays correct across a swirl. */
  const tiles = [];
  for (let i = 0; i < count; i++) {
    tiles.push({
      i,
      url: null, remote: false, live: false, isVideo: false,
      seq: 0, node: null, face: null, _liveUrl: null,
      sig: 1 + Math.floor(roll('sig') * 6),
      hue: Math.round(roll('hue') * 300),
    });
  }
  const liveUse = new Map();
  let videoTiles = 0;
  let destroyed = false;
  let frozen = false;
  let layout = '';
  let containers = [];
  let rowEls = [];
  let pool = null;
  let band = 0;
  const dressTimers = new Set();
  const chunkTimers = new Set();

  /* ---------------------------------------------------------------- ledger */
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
  function repaint(tile) {
    if (!tile || !tile.face) return;
    paintFace(tile.face, tile.url);
  }

  /**
   * Give a tile a url. Returns true if the look took. Two budgets can refuse
   * it (the decoder window, the video element ceiling); a refusal is not a
   * failure - the caller rests the seat on a still instead.
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
    if (!wait) { repaint(tile); return true; }
    const mySeq = tile.seq;
    const h = setTimeout(() => {
      dressTimers.delete(h);
      if (destroyed || tile.seq !== mySeq) return;   // a turn got here first
      repaint(tile);
    }, wait);
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

  /** Grow / shed live seats toward the band's appetite, a few at a time. */
  function applyBand() {
    if (destroyed || !pool) return 0;
    const want = liveWant();
    let moved = 0;
    let have = liveSeats();
    if (have < want) {
      const sleepers = tiles.filter((t) => !t.live);
      for (const tile of sleepers) {
        if (have >= want || moved >= MONTAGE.SWAP_APPLY_CHUNK) break;
        const got = drawLive();
        if (!got) break;
        if (setUrl(tile, got)) { have += 1; moved += 1; }
      }
    } else if (have > want) {
      const lives = tiles.filter((t) => t.live);
      for (const tile of lives) {
        if (have <= want || moved >= MONTAGE.SWAP_APPLY_CHUNK) break;
        const got = drawSleeper();
        if (setUrl(tile, got || { url: null })) { have -= 1; moved += 1; }
      }
    }
    return moved;
  }

  /* ------------------------------------------------------------- structure */
  function clearContainers() {
    for (const h of chunkTimers) { try { clearTimeout(h); } catch (e) { /* ignore */ } }
    chunkTimers.clear();
    for (const c of containers) { try { c.remove(); } catch (e) { /* ignore */ } }
    containers = [];
    rowEls = [];
    for (const tile of tiles) { tile.node = null; tile.face = null; }
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
    const face = el('div', 'g-ir-face');
    if (face) node.appendChild(face);
    tile.node = node;
    tile.face = face;
    return node;
  }

  /** Rebuild the stage for a layout, re-dressing every tile from its own look. */
  function setLayout(kind) {
    if (destroyed || !mount) return layout;
    const want = String(kind || 'rows');
    clearContainers();
    layout = want;
    try { mount.setAttribute('data-layout', layout); } catch (e) { /* DOM double */ }

    if (layout === 'rows') {
      const rows = Math.max(MONTAGE.ROWS_MIN,
        Math.min(MONTAGE.ROWS_MAX, Math.round(Math.sqrt(count / 1.4))));
      if (mount.style) mount.style.setProperty('--ir-rows', String(rows));
      const per = Math.ceil(count / rows);
      for (let r = 0; r < rows; r++) {
        const row = el('div', 'g-ir-row' + (r % 2 ? ' g-ir-rev' : ''));
        if (!row) break;
        try { row.setAttribute('data-r', String(r)); } catch (e) { /* DOM double */ }
        if (row.style) row.style.setProperty('--ir-r', String(r));
        for (let k = 0; k < per; k++) {
          const i = r * per + k;
          if (i >= count) break;
          const node = buildTileEl(tiles[i]);
          if (node) row.appendChild(node);
        }
        mount.appendChild(row);
        containers.push(row);
        rowEls.push(row);
      }
    } else if (layout === 'mosaic') {
      const cols = Math.max(MONTAGE.MOSAIC_COLS_MIN,
        Math.min(MONTAGE.MOSAIC_COLS_MAX, Math.round(Math.sqrt(count * 1.5))));
      const grid = el('div', 'g-ir-mosaic');
      if (grid) {
        if (grid.style) grid.style.setProperty('--ir-cols', String(cols));
        for (let i = 0; i < count; i++) {
          const node = buildTileEl(tiles[i]);
          if (!node) continue;
          if (node.style) {
            node.style.setProperty('--ir-col', String(i % cols));
            node.style.setProperty('--ir-row', String(Math.floor(i / cols)));
          }
          grid.appendChild(node);
        }
        mount.appendChild(grid);
        containers.push(grid);
      }
    } else {
      /* SWIRL: tiles ride a spiral. CORE sets --ir-ang / --ir-rad / --ir-orbit;
       * style.js owns the transform that reads them (and the engine's spiral
       * wash sits behind, held by index.js). */
      const swirl = el('div', 'g-ir-swirl');
      if (swirl) {
        for (let i = 0; i < count; i++) {
          const node = buildTileEl(tiles[i]);
          if (!node) continue;
          const f = count > 1 ? i / (count - 1) : 0;
          const ang = f * 360 * MONTAGE.SWIRL_TURNS;
          const rad = MONTAGE.SWIRL_RAD_MIN + (MONTAGE.SWIRL_RAD_MAX - MONTAGE.SWIRL_RAD_MIN) * f;
          if (node.style) {
            node.style.setProperty('--ir-ang', ang.toFixed(2) + 'deg');
            node.style.setProperty('--ir-rad', (rad * 100).toFixed(2) + '%');
            node.style.setProperty('--ir-orbit', f.toFixed(3));
          }
          swirl.appendChild(node);
        }
        mount.appendChild(swirl);
        containers.push(swirl);
      }
    }

    for (const tile of tiles) repaint(tile);
    if (frozen) applyFreeze();
    return layout;
  }

  /* ----------------------------------------------------------------- dress */
  /** The pool landed: dress the wall, staggered, live seats first. */
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
    }
    say('montage dressed: ' + dressed + '/' + tiles.length + ' tiles, live cap ' + liveCap
      + ', video cap ' + videoCap + (reduced ? ' (reduced motion)' : ''));
    return dressed;
  }

  /* ------------------------------------------------------------------ turn */
  /**
   * THE TURN - the montage's one content primitive (the mosaic's turnover, the
   * rows' churn). Seeded choice, chunked apply: the pick is deterministic, the
   * paint is spread over frames so a burst never eats half a frame budget.
   *
   * @returns {Array} the tiles chosen (their FACES are what the engine dresses)
   */
  function turn(n) {
    if (destroyed || !pool || !tiles.length) return [];
    const k = Math.max(1, Math.min(tiles.length, Math.round(Number(n) || 1)));
    const chosen = [];
    const used = new Set();
    for (let i = 0; i < k * 3 && chosen.length < k; i++) {
      const idx = Math.floor(roll('turn') * tiles.length);
      if (used.has(idx)) continue;
      used.add(idx);
      chosen.push(tiles[idx]);
    }
    const apply = (from) => {
      if (destroyed) return;
      const stop = Math.min(chosen.length, from + MONTAGE.SWAP_APPLY_CHUNK);
      for (let i = from; i < stop; i++) {
        const tile = chosen[i];
        const got = tile.live ? (drawLive() || drawSleeper()) : (drawSleeper() || drawLive());
        if (got) setUrl(tile, got);
      }
      if (stop < chosen.length) {
        const h = setTimeout(() => { chunkTimers.delete(h); apply(stop); }, 24);
        chunkTimers.add(h);
      }
    };
    apply(0);
    return chosen;
  }

  /* ---------------------------------------------------------------- freeze */
  function applyFreeze() {
    if (!mount) return;
    try {
      if (frozen) mount.classList.add('is-frozen'); else mount.classList.remove('is-frozen');
      if (mount.setAttribute) mount.setAttribute('data-frozen', frozen ? '1' : '0');
    } catch (e) { /* DOM double */ }
    for (const tile of tiles) {
      const kids = (tile.face && tile.face.children) || [];
      const m = kids.length ? kids[0] : null;
      if (!m || String(m.tagName || '').toUpperCase() !== 'VIDEO') continue;
      try {
        if (frozen) { if (m.pause) m.pause(); }
        else if (m.play) { const p = m.play(); if (p && p.catch) p.catch(() => {}); }
      } catch (e) { /* ignore */ }
    }
  }
  /** THE FREEZE: the stream stops dead. The class clock does not (Law I). */
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
    if (!tile) return false;
    const got = drawSleeper();
    return setUrl(tile, got || { url: null });
  }
  function shedVideoSeat() {
    const vids = tiles.filter((t) => t.isVideo);
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
    const lives = tiles.filter((t) => t.live && !t.isVideo && t.url);
    if (!lives.length) return false;
    /* a decoder only dies with its LAST tile, so shed sole holders first */
    const solo = lives.filter((t) => (urlTiles.get(t.url) | 0) === 1);
    const from = solo.length ? solo : lives;
    return restSeat(from[Math.floor(Math.random() * from.length)]);
  }
  function growGifSeat() {
    const sleepers = tiles.filter((t) => !t.live);
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
    setLayout,
    layout() { return layout; },
    rows() { return rowEls.slice(); },
    /** The engine's ONLY legal targets on this stage (never `.g-ir-tile`). */
    faces() { return tiles.map((t) => t.face).filter(Boolean); },
    facesOf(list) { return (list || []).map((t) => t && t.face).filter(Boolean); },
    tiles() { return tiles.slice(); },
    mountEl() { return mount; },

    /* ---- media ---- */
    dress,
    turn,
    /** The density band drives how many seats are alive. */
    setBand(b) {
      const v = Number(b);
      band = !Number.isFinite(v) ? 0 : v < 0 ? 0 : v > 1 ? 1 : v;
      return applyBand();
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
    destroy() {
      destroyed = true;
      api.stopGovernor();
      for (const h of dressTimers) { try { clearTimeout(h); } catch (e) { /* ignore */ } }
      dressTimers.clear();
      clearContainers();
      liveUse.clear();
      videoTiles = 0;
      pool = null;
    },

    /* ---- diagnostics (never read by the shell) ---- */
    diagnostics() {
      return {
        layout, frozen, band, count, liveCap, videoCap,
        live: liveSeats(),
        liveUrls: liveUse.size,
        videoTiles,
        want: liveWant(),
        rows: rowEls.length,
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

export default { createMontage, createLedger, MONTAGE, isAnimatedUrl, isVideoUrl, paintFace, hideTruthNode };
