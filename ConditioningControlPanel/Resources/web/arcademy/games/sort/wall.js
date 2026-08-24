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
 * `mediaEl` here mints an <img> for everything - a video url gets a still frame
 * or, failing that, its own seeded card back. Never put a live decode on a wall
 * tile (CLAUDE.md trap 36).
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

/**
 * The wall.
 * @param {Object} o
 *   mount     the node the wall lives in (behind the stack)
 *   tier      1..4
 *   reduced   reduced motion
 *   seed      the class seed (the card-back hue for a tile with no still)
 *   stageOf() -> {w, h}
 *   log
 */
export function createWall(o = {}) {
  const mount = o.mount || null;
  const tier = tierOf(o.tier);
  const reduced = !!o.reduced;
  const say = typeof o.log === 'function' ? o.log : () => {};
  const stageOf = typeof o.stageOf === 'function' ? o.stageOf : () => ({ w: 1280, h: 720 });

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

  function paint(tile, card) {
    if (!tile) return;
    /* A WALL TILE IS NEVER A LIVE DECODE (trap 36). A loop lands as its own
     * url in an <img> - a gif still animates cheaply. A VIDEO url gets no <img>
     * at all (owner 2026-08-24): an mp4 in an <img> paints nothing but still
     * downloads the whole file, so the drawn card back stands for it instead. */
    const mime = card && card.mime;
    if ((mime && /^video\//i.test(String(mime)))
      || /\.(mp4|webm|m4v|mov)(\?|#|$)/i.test(String((card && card.url) || ''))) return;
    const img = el('img', 'g-sort-wall-face');
    if (!img) return;
    img.alt = '';
    setAttr(img, 'draggable', 'false');
    setAttr(img, 'decoding', 'async');
    setAttr(img, 'loading', 'lazy');
    if (typeof img.addEventListener === 'function') {
      img.addEventListener('error', () => { try { if (img.parentNode) img.remove(); } catch (e) { /* ignore */ } });
    }
    img.src = card && card.url ? card.url : '';
    tile.appendChild(img);
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
      paint(tile, card);
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

    diagnostics() {
      return {
        landed, cols, visible, bleeding, flooding, kenBurns, tiles: tiles.length,
        fromRung: fromRungFor(tier), tier,
      };
    },

    destroy() {
      tiles.length = 0;
      try { if (root && root.remove) root.remove(); } catch (e) { say('wall destroy: ' + ((e && e.message) || e)); }
    },
  };
  return api;
}

export default { WALL, createWall, wallVisible, layout, colsForCount, fromRungFor };
