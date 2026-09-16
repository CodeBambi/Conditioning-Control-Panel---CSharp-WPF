/* ============================================================================
 * games/sort/wall.js - THE WALL. The collage behind the stack, built out of the
 * cards you already sorted.
 *
 * Every committed card THUDS into the next slot and stays there. That is the
 * whole trick: the room's decoration is your own work, so by the bell the stage
 * is wearing three minutes of your taste. A WRONG card lands dimmed (40%) and
 * stays dim - the wall is an honest ledger, and the one place in the room a
 * mistake is still visible a minute later.
 *
 * WHEN IT APPEARS is the tier dial (pitch section 10): tier 1 sees the wall only
 * at the end card, tier 2 from rung 3, tier 3 from rung 2, tier 4 from rung 1.
 * A low-tier player gets a clean stage; a high-tier one is already sorting into
 * a growing mosaic by their second link.
 *
 * THE SLOTS ARE A GRID SOLVED FROM THE STAGE, not a fixed table: `layout()`
 * picks the column count that keeps tiles near square at the stage's aspect,
 * and slots wrap. Past capacity the wall RECYCLES from the oldest slot, so a
 * long class keeps landing cards somewhere real instead of growing a scroller.
 *
 * DECODER BUDGET. A video card lands as a POSTER, never as a second decode: the
 * wall is dozens of tiles and the stack already owns the budget (ceiling 2).
 * `paint()` here mints an <img> for everything else - a video url gets no
 * element at all and its seeded card back stands in. Never put a live decode on
 * a wall tile (CLAUDE.md trap 36).
 *
 * ...AND AN ANIMATED GIF IS A LIVE DECODE (ccp-bugs #1197). That was the hole in
 * the budget: a still is cheap, a gif in an <img> is not, and the wall held up
 * to CAP of them at once. See THE DECODE RAIL below - the newest few tiles keep
 * a live <img>, everything older is frozen to a canvas, and at most
 * `DECODE_LANES` urls are ever in flight.
 * ==========================================================================*/

import { hash01 } from '../../core/rng.js';

export const WALL = Object.freeze({
  /** The rung the wall first appears at, by grade tier. 0 = end card only. */
  FROM_RUNG_BY_TIER: Object.freeze({ 1: 0, 2: 3, 3: 2, 4: 1 }),
  /** How dim a wrong card lands, and stays. */
  WRONG_ALPHA: 0.4,
  /** The THUD, and its reduced-motion twin. */
  THUD_MS: 340,
  THUD_MS_REDUCED: 120,
  /** Tiles before the wall starts recycling its oldest slot.
   *  96 -> 120 with the 180s budget (the class-length wave). The cap is a
   *  RECYCLE RATE, not a memory: a competent tier-4 player commits ~200 cards
   *  in 180s, which at 96 would have wiped the wall through twice over and made
   *  the early class un-findable at the bell. 120 - the largest deck
   *  (DECK.SIZE_BY_TIER) - keeps it to ~1.7 passes, the same feel the 120s
   *  class had at 96, and holds the tidy invariant that the wall can show at
   *  most one whole deck. It is 25% more <img> tiles and no more decodes: a
   *  wall tile is always a still (see DECODER BUDGET below). */
  CAP: 120,
  /** Column counts the layout will consider. */
  COLS: Object.freeze([6, 7, 8, 9, 10, 11, 12]),
  /** THE LIVE BUDGET (ccp-bugs #1197). How many tiles keep an ANIMATED <img>.
   *  Always the newest ones - the eye is on the landing, never on the corner.
   *  Every older tile is frozen to a canvas still and costs nothing a frame. */
  LIVE_FACES: 3,
  /** How many tile urls may be in flight at once. A tier-4 player commits a
   *  card a second; with no lane count the wall opens a socket and a decode
   *  for every one of them and the STACK's own media queues behind them,
   *  which is how a card "doesn't load in at all". */
  DECODE_LANES: 4,
  /** A lane is a concurrency claim, not a deadline: a url that has not
   *  answered in this long hands its lane back. The <img> is left alone and
   *  still counts if it lands later. */
  LANE_MS: 4000,
  /** The side of a frozen tile's canvas backing store, px. A wall tile is
   *  never wider than ~180 CSS px, so this is one crisp step above it. */
  STILL_PX: 224,
  /** The full-bleed hold at the bell. */
  BLEED_MS: 3000,
  /** THE KEN-BURNS period band, in seconds. One seeded draw for the collage. */
  KB_S: Object.freeze([14, 22]),
});

function tierOf(tier) { return Math.max(1, Math.min(4, Math.round(Number(tier) || 1))); }
function num(v, d) { const n = Number(v); return Number.isFinite(n) ? n : d; }

/** The rung this tier's wall wakes at. 0 means "only at the end card". */
export function fromRungFor(tier) { return WALL.FROM_RUNG_BY_TIER[tierOf(tier)]; }

/** Should the wall be visible right now? */
export function wallVisible(tier, rung, ended) {
  if (ended) return true;
  const from = fromRungFor(tier);
  if (from <= 0) return false;
  return Math.max(0, Math.round(num(rung, 0))) >= from;
}

/**
 * Columns for a stage of this shape. Pure, so the suite can pin it: we want
 * tiles as near square as the column list allows, given a 16:9-ish stage and a
 * wall that is roughly as tall as it is wide behind the cards.
 */
export function layout(stageW, stageH, cols) {
  const w = Math.max(1, num(stageW, 1280));
  const h = Math.max(1, num(stageH, 720));
  if (cols) return Math.max(2, Math.round(num(cols, 8)));
  const aspect = w / h;
  let best = WALL.COLS[0];
  let bestErr = Infinity;
  for (const c of WALL.COLS) {
    const rows = Math.max(1, Math.round(c / aspect));
    const tileAspect = (w / c) / (h / rows);
    const err = Math.abs(Math.log(tileAspect));
    if (err < bestErr) { bestErr = err; best = c; }
  }
  return best;
}

/**
 * Columns for a wall that has to show THIS MANY tiles at once, near square.
 * layout() solves the empty stage; it does not know how many cards landed, so a
 * long class overruns the bottom of the room once the tiles are honestly square
 * (nine columns of 176px squares is five rows, and a 180s class lands far more
 * than forty-five). The bell is the one moment every tile has to be on stage at
 * once, so bleed() re-solves with the count in hand. Never returns FEWER columns
 * than it was handed: the mosaic may tighten to fit, never re-inflate.
 */
export function colsForCount(stageW, stageH, count, floor) {
  const w = Math.max(1, num(stageW, 1280));
  const h = Math.max(1, num(stageH, 720));
  const n = Math.max(1, Math.round(num(count, 1)));
  const min = Math.max(0, Math.round(num(floor, 0)));
  let best = null;
  let bestErr = Infinity;
  for (const c of WALL.COLS) {
    if (c < min) continue;
    const rows = Math.max(1, Math.ceil(n / c));
    const err = Math.abs(Math.log(((w / c) / (h / rows)) || 1));
    if (err < bestErr) { bestErr = err; best = c; }
  }
  return best == null ? Math.max(min, WALL.COLS[WALL.COLS.length - 1]) : best;
}

function el(tag, cls) {
  try {
    if (typeof document === 'undefined' || !document.createElement) return null;
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    return n;
  } catch (e) { return null; }
}

/* The lane timer resolves setTimeout off the GLOBAL at call time, the way
 * index.js's clock does, so a harness with a fake clock drives the wall too
 * and the shipped file grows no test seam. */
function laterFn() {
  const f = globalThis.setTimeout;
  return typeof f === 'function' ? f : setTimeout;
}
function clearLaterFn() {
  const f = globalThis.clearTimeout;
  return typeof f === 'function' ? f : clearTimeout;
}

/**
 * The wall.
 * @param {Object} o
 *   mount     the node the wall lives in (behind the stack)
 *   tier      1..4
 *   reduced   reduced motion
 *   seed      the class seed (the card-back hue for a tile with no still)
 *   stageOf() -> {w, h}
 *   resolve(card) -> {url, mime} | null   the game's substitute resolution, so
 *             a tile paints the face the STACK showed and never a dead url
 *             (optional: without it a tile paints the card's own url)
 *   log
 */
export function createWall(o = {}) {
  const mount = o.mount || null;
  const tier = tierOf(o.tier);
  const reduced = !!o.reduced;
  const say = typeof o.log === 'function' ? o.log : () => {};
  const stageOf = typeof o.stageOf === 'function' ? o.stageOf : () => ({ w: 1280, h: 720 });
  /* (card) -> {url, mime} | null, the game's substitute resolution (see paint).
   * Guarded: a resolver that throws must never cost the wall a tile. */
  const resolve = (card) => {
    if (!card) return null;
    if (typeof o.resolve !== 'function') return { url: card.url, mime: card.mime || '' };
    try { return o.resolve(card) || null; } catch (e) { return { url: card.url, mime: card.mime || '' }; }
  };

  const root = el('div', 'g-sort-wall');
  const grid = el('div', 'g-sort-wall-grid');
  if (root && grid) root.appendChild(grid);
  if (mount && root && mount.appendChild) mount.appendChild(root);

  let cols = layout(stageOf().w, stageOf().h);
  if (root && root.style) {
    try { root.style.setProperty('--sort-wall-cols', String(cols)); } catch (e) { /* noop */ }
  }
  /* KEN-BURNS (Law III: no frame of the room is ever still). The whole collage
   * drifts on ONE seeded period - the animation is declared on the faces but it
   * is switched at the ROOT, so a hundred tiles cost one class toggle and not a
   * hundred style writes. Reduced motion never asks for it; the touch and lite
   * rungs drop it in the sheet, because a drifting mosaic is exactly the
   * per-frame re-raster a phone cannot afford (trap 36 / trap 42). */
  let kenBurns = false;
  if (!reduced && root && root.classList) {
    const period = WALL.KB_S[0]
      + (WALL.KB_S[1] - WALL.KB_S[0]) * hash01(String(o.seed || 'sort') + '|sort-wall-kb');
    try { root.style.setProperty('--sort-wall-kb', period.toFixed(1) + 's'); } catch (e) { /* noop */ }
    root.classList.add('is-kb');
    kenBurns = true;
  }
  let visible = false;
  let landed = 0;
  const tiles = [];
  let bleeding = false;
  let flooding = false;

  function setAttr(node, k, v) { try { if (node && node.setAttribute) node.setAttribute(k, String(v)); } catch (e) { /* DOM double */ } }

  /* THE WALL DECLARES ITSELF DARK AT BIRTH. `show()` is idempotent and returns
   * early when nothing changed, so without this the attribute simply would not
   * exist until the first rung change - and "no attribute" is not the same
   * answer as "off" to anything reading the DOM (a deck, a capture, a suite). */
  setAttr(root, 'data-on', '0');
  setAttr(root, 'data-bleed', '0');
  setAttr(root, 'data-flood', '0');

  /* ==================================================== THE DECODE RAIL ====
   * WHY THIS EXISTS (ccp-bugs #1197, "the sort room begins to lag out from too
   * many gifs playing in the background... the timer starts only updating at
   * like 1 frame every few seconds and some of the images don't load in").
   *
   * The wall used to mint an <img> the moment a card landed and then leave it
   * there for the rest of the class. At CAP that is 120 live elements, and in
   * a gif-heavy library every one of them is an ANIMATED gif, which Chromium
   * keeps decoding for ever, on the MAIN thread, whether or not anyone is
   * looking at that corner of the mosaic. Past roughly thirty tiles the
   * decoder owns the frame: the 250ms clock tick lands seconds late (that is
   * the reported timer, and it is not a clock bug - `paintClock` reads the
   * wall clock and is simply never called), and a freshly landed tile never
   * gets a lane, which is the "doesn't load in at all" half. The line that
   * used to sit right here - "a gif still animates cheaply" - was the bug.
   *
   * So the wall now keeps a BUDGET instead of a gallery:
   *
   *   1. AT MOST `WALL.LIVE_FACES` TILES HOLD A LIVE <img>, always the newest.
   *   2. AN OLDER TILE IS FROZEN: its current frame is drawn once into a
   *      <canvas> that takes the <img>'s place, and the <img> is dropped, so
   *      the decoder lets go. The tile looks the same and costs nothing a
   *      frame. `drawImage` into a canvas we never read back is legal on a
   *      CORS-TAINTED image, so a remote feed freezes exactly like the local
   *      library does - the taint only ever bit `toDataURL`/`getImageData`,
   *      and this rail calls neither.
   *   3. AT MOST `WALL.DECODE_LANES` URLS ARE IN FLIGHT, so a fast player
   *      cannot open eighty sockets and starve the STACK's own media.
   *   4. A URL THAT WILL NOT LOAD IS SKIPPED AND COUNTED, never left as a
   *      half-decoded hole. `skipped` rides `diagnostics()` so the class can
   *      say how many the wall dropped.
   *
   * The tier dial, the recycle, the wrong-card dimming and the thud are
   * untouched: this changes what a tile COSTS, never what it shows.
   * ======================================================================= */
  let skipped = 0;      // urls that answered with an error and lost their tile
  let frozenN = 0;      // tiles turned into a canvas still
  let stuck = 0;        // tiles a freeze could not take (kept live, honestly)
  let paintedN = 0;     // tiles whose media actually landed
  const liveFaces = []; // records still holding an <img>, oldest first
  const bySlot = new Map();
  const gens = [];      // per-slot generation, bumped on every recycle
  const queue = [];
  let lanes = 0;
  let dead = false;

  function genOf(slot) { return gens[slot] || 0; }

  /** Let go of a decoder. `removeAttribute` and not `src=''`: an empty src is
   *  a request for the document url in some engines. */
  function killImg(img) {
    if (!img) return;
    try { if (typeof img.removeAttribute === 'function') img.removeAttribute('src'); }
    catch (e) { /* DOM double */ }
    try { if (typeof img.remove === 'function') img.remove(); }
    catch (e) { /* noop */ }
  }

  function freeLane(rec) {
    if (!rec) return;
    if (rec.timer) { try { clearLaterFn()(rec.timer); } catch (e) { /* noop */ } rec.timer = 0; }
    if (rec.lane) { rec.lane = false; lanes = lanes > 0 ? lanes - 1 : 0; pump(); }
  }

  /** Drop whatever face a slot is holding. Bumps the slot's generation, so an
   *  in-flight paint for the card that used to live here lands on the floor
   *  instead of on the card that replaced it. */
  function clearSlot(slot) {
    gens[slot] = genOf(slot) + 1;
    const rec = bySlot.get(slot);
    if (!rec) return;
    bySlot.delete(slot);
    freeLane(rec);
    const at = liveFaces.indexOf(rec);
    if (at >= 0) liveFaces.splice(at, 1);
    killImg(rec.img);
    rec.img = null;
    try { if (rec.canvas && typeof rec.canvas.remove === 'function') rec.canvas.remove(); }
    catch (e) { /* noop */ }
    rec.canvas = null;
  }

  /**
   * THE FREEZE. One `drawImage` and the tile is a still for good.
   * The canvas is SQUARE and centre-cropped by hand, which is what
   * `object-fit:cover` was doing for the <img> in a square tile - done in the
   * draw so the tile does not depend on object-fit applying to a canvas.
   */
  function freeze(rec) {
    if (!rec || !rec.img) return;
    if (rec.gen !== genOf(rec.slot)) { killImg(rec.img); rec.img = null; return; }
    const img = rec.img;
    let canvas = null;
    try {
      const side = Math.max(16, Math.round(WALL.STILL_PX));
      canvas = el('canvas', 'g-sort-wall-face is-still');
      const ctx = canvas && typeof canvas.getContext === 'function' ? canvas.getContext('2d') : null;
      if (!ctx || typeof ctx.drawImage !== 'function') { canvas = null; }
      else {
        canvas.width = side;
        canvas.height = side;
        const nw = Math.round(Number(img.naturalWidth) || 0);
        const nh = Math.round(Number(img.naturalHeight) || 0);
        if (nw > 0 && nh > 0) {
          const s = Math.min(nw, nh);
          ctx.drawImage(img, (nw - s) / 2, (nh - s) / 2, s, s, 0, 0, side, side);
        } else {
          /* no intrinsic size (an svg, a double) - stretch and move on */
          ctx.drawImage(img, 0, 0, side, side);
        }
      }
    } catch (e) { canvas = null; }
    if (!canvas) {
      /* THE ONE HONEST FAILURE. Nothing to freeze onto, so the tile keeps its
       * live <img>: a drifting gif is a smaller sin than a hole in the ledger,
       * and this branch means a decode that broke between load and draw. */
      stuck += 1;
      return;
    }
    setAttr(canvas, 'aria-hidden', 'true');
    try { if (rec.tile && rec.tile.appendChild) rec.tile.appendChild(canvas); } catch (e) { /* noop */ }
    killImg(img);
    rec.img = null;
    rec.canvas = canvas;
    frozenN += 1;
  }

  /** Hold the live budget: the newest LIVE_FACES keep their <img>, the rest freeze. */
  function trim() {
    const cap = Math.max(1, Math.round(WALL.LIVE_FACES));
    while (liveFaces.length > cap) freeze(liveFaces.shift());
  }

  function settle(rec, ok) {
    if (!rec || rec.settled) return;
    rec.settled = true;
    freeLane(rec);
    if (rec.gen !== genOf(rec.slot)) { killImg(rec.img); rec.img = null; return; }
    if (!ok) {
      /* SKIP, DO NOT HOLE. A dead url loses its <img> and the tile falls back
       * to its own drawn back - the same answer the stack gives - and the miss
       * is counted so the class can report it. */
      skipped += 1;
      killImg(rec.img);
      rec.img = null;
      if (bySlot.get(rec.slot) === rec) bySlot.delete(rec.slot);
      return;
    }
    paintedN += 1;
    liveFaces.push(rec);
    trim();
  }

  function start(rec) {
    const img = el('img', 'g-sort-wall-face');
    if (!img) return;
    rec.img = img;
    rec.lane = true;
    lanes += 1;
    img.alt = '';
    setAttr(img, 'draggable', 'false');
    setAttr(img, 'decoding', 'async');
    if (typeof img.addEventListener === 'function') {
      img.addEventListener('load', () => settle(rec, true));
      img.addEventListener('error', () => settle(rec, false));
    }
    try { img.src = rec.url; } catch (e) { settle(rec, false); return; }
    try { if (rec.tile && rec.tile.appendChild) rec.tile.appendChild(img); } catch (e) { /* noop */ }
    rec.timer = laterFn()(() => { rec.timer = 0; freeLane(rec); }, Math.max(0, WALL.LANE_MS));
  }

  /* `pumping` is a re-entrancy guard, not a nicety: a url that throws on
   * assignment settles inside start(), which frees its lane and pumps again,
   * and a whole dead queue would otherwise recurse once per row. */
  let pumping = false;
  function pump() {
    if (pumping) return;
    pumping = true;
    try {
      while (!dead && lanes < Math.max(1, WALL.DECODE_LANES) && queue.length) {
        const rec = queue.shift();
        if (!rec) continue;
        if (rec.gen !== genOf(rec.slot)) continue;   // the slot recycled while it waited
        start(rec);
      }
    } finally { pumping = false; }
  }

  function paint(tile, card, slot) {
    if (!tile || dead) return;
    /* THE SAME FACE THE STACK SHOWED (0826). `resolve` is the game's own
     * substitute resolution (index.js displaySrcOf): a card whose url is on
     * the blacklist was played as a same-tag substitute, and painting its raw
     * url here put a dead url on the wall and made the wall disagree with the
     * class. Null = no healthy media at all, which is the drawn back - the
     * same answer the stack gives. A caller that passes no resolver (a suite,
     * an old double) keeps the raw card. */
    const src = resolve(card) || null;
    if (!src || !src.url) return;
    /* A VIDEO URL GETS NO <img> AT ALL (owner 2026-08-24): an mp4 in an <img>
     * paints nothing but still downloads the whole file, so the drawn card
     * back stands for it instead. */
    const mime = src.mime;
    if ((mime && /^video\//i.test(String(mime)))
      || /\.(mp4|webm|m4v|mov)(\?|#|$)/i.test(String(src.url))) return;
    const rec = {
      slot, gen: genOf(slot), tile, url: String(src.url),
      img: null, canvas: null, lane: false, settled: false, timer: 0,
    };
    bySlot.set(slot, rec);
    queue.push(rec);
    pump();
  }

  const api = {
    get el() { return root; },
    get count() { return landed; },
    get cols() { return cols; },

    /** Re-solve the grid (the stage resized). */
    relayout(force) {
      const s = stageOf();
      const next = layout(s.w, s.h, force);
      if (next === cols) return cols;
      cols = next;
      if (root && root.style) { try { root.style.setProperty('--sort-wall-cols', String(cols)); } catch (e) { /* noop */ } }
      return cols;
    },

    /** THE TIER DIAL. Call it on every rung change; it is idempotent. */
    show(rung, ended) {
      const want = wallVisible(tier, rung, ended);
      if (want === visible) return visible;
      visible = want;
      if (root && root.classList) {
        if (visible) root.classList.add('is-on'); else root.classList.remove('is-on');
      }
      setAttr(root, 'data-on', visible ? '1' : '0');
      return visible;
    },

    /**
     * A card lands. Returns the tile so the caller can aim the shrink at it.
     * @param {Object} card
     * @param {{wrong?:boolean, seed?:number}} opts
     */
    land(card, opts) {
      const wrong = !!(opts && opts.wrong);
      const slot = landed % WALL.CAP;
      landed += 1;
      let tile = tiles[slot] || null;
      if (!tile) {
        tile = el('div', 'g-sort-wall-tile');
        if (!tile) return null;
        tiles[slot] = tile;
        if (grid) grid.appendChild(tile);
      } else {
        /* THE RECYCLE LETS GO FIRST. `textContent = ''` empties the tile in a
         * real DOM but says nothing to the rail, so without this the old
         * record kept a lane and a live <img> the wall could no longer see. */
        clearSlot(slot);
        try { tile.textContent = ''; } catch (e) { /* noop */ }
      }
      setAttr(tile, 'data-slot', String(slot));
      setAttr(tile, 'data-tag', card && card.tag ? card.tag : 'target');
      setAttr(tile, 'data-wrong', wrong ? '1' : '0');
      /* the drawn card back under every tile: a seeded hue so a tile whose
       * media never decodes is still a deliberate-looking square */
      try { tile.style.setProperty('--sort-tile-h', String(Math.round(((card && card.i) || slot) * 37 % 360))); }
      catch (e) { /* noop */ }
      if (tile.classList) {
        tile.classList.remove('thud');
        if (!reduced) tile.classList.add('thud');
        if (wrong) tile.classList.add('is-wrong'); else tile.classList.remove('is-wrong');
      }
      paint(tile, card, slot);
      return tile;
    },

    /**
     * THE FLOOD (rung 8, LOT D's surge). The collage stops being a backdrop
     * and becomes the room: full bleed, full alpha, and the swiped cards fly
     * INTO it. It is a STATE, not the bleed - the bell's bleed is the wall
     * taking the stage when the class is over, and the two can be true at
     * once without either clearing the other.
     * @param {boolean} on
     */
    flood(on) {
      flooding = on !== false;
      if (root && root.classList) {
        if (flooding) { root.classList.add('is-on'); root.classList.add('is-flood'); }
        else root.classList.remove('is-flood');
      }
      /* A FLOOD LIGHTS THE WALL, AND `data-on` HAS TO SAY SO. `show()` is
       * idempotent on its own `visible` flag, so setting that flag here and
       * leaving the attribute alone would make the next show() return early
       * and the DOM would claim a dark wall over a flooded stage. */
      if (flooding) { visible = true; setAttr(root, 'data-on', '1'); }
      setAttr(root, 'data-flood', flooding ? '1' : '0');
      return flooding;
    },

    /** The bell: the wall takes the whole stage and holds. */
    bleed(on) {
      bleeding = on !== false;
      visible = bleeding ? true : visible;
      /* EVERY TILE IS ON STAGE FOR THE HOLD, and still square. */
      if (bleeding && landed > 0) {
        const s = stageOf();
        const next = colsForCount(s.w, s.h, Math.min(landed, WALL.CAP), cols);
        if (next !== cols) {
          cols = next;
          if (root && root.style) { try { root.style.setProperty('--sort-wall-cols', String(cols)); } catch (e) { /* noop */ } }
        }
      }
      if (root && root.classList) {
        if (bleeding) { root.classList.add('is-on'); root.classList.add('is-bleed'); }
        else root.classList.remove('is-bleed');
      }
      setAttr(root, 'data-bleed', bleeding ? '1' : '0');
      return bleeding;
    },

    /** Urls the wall gave up on. The class reads it for its own report line. */
    get skipped() { return skipped; },

    diagnostics() {
      return {
        landed, cols, visible, bleeding, flooding, kenBurns, tiles: tiles.length,
        fromRung: fromRungFor(tier), tier,
        /* the decode rail (ccp-bugs #1197) */
        live: liveFaces.length, frozen: frozenN, painted: paintedN,
        skipped, stuck, queued: queue.length, lanes,
        liveCap: Math.max(1, Math.round(WALL.LIVE_FACES)),
      };
    },

    destroy() {
      dead = true;
      queue.length = 0;
      /* EVERY DECODER GOES BACK, not just the ones on screen. A class that
       * ends mid-flight used to leave its <img> elements attached to a
       * detached tree with their gifs still spinning until GC noticed. */
      try {
        bySlot.forEach((rec) => { freeLane(rec); killImg(rec.img); rec.img = null; });
      } catch (e) { /* noop */ }
      bySlot.clear();
      liveFaces.length = 0;
      lanes = 0;
      if (skipped > 0) say('wall: ' + skipped + ' tile url(s) skipped');
      tiles.length = 0;
      try { if (root && root.remove) root.remove(); } catch (e) { say('wall destroy: ' + ((e && e.message) || e)); }
    },
  };
  return api;
}

export default { WALL, createWall, wallVisible, layout, colsForCount, fromRungFor };
