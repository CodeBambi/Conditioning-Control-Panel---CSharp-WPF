/* ============================================================================
 * games/lost-and-found/board.js - THE LIVING MOSAIC.
 *
 * A DOM-only grid of looping tiles in toroidally drifting rows. DOM-only is a
 * CONTRACT, not an implementation detail: tiles are img/video elements and the
 * found-ceremony art is another DOM node with the same url, so there is no
 * canvas/WebGL/capture path anywhere in this game and the CORS-tainted remote
 * pool is legal (GROUND-RULES §8 two-pool law -> assetNeeds.canvasSafe:false).
 *
 * ---------------------------------------------------------------------------
 * THE THREE THINGS THIS FILE GETS RIGHT AND NOTHING ELSE DOES
 *
 * 1. LOOK SIGNATURES. Every tile owns a UNIQUE (gradient x hue) signature, so
 *    even on the bundled placeholder floor (six SVGs) a 40-tile board shows 40
 *    visually distinct tiles and the hunt is winnable. Signatures are applied as
 *    CSS filters on an INNER `.g-lf-skin` layer - never on the tile itself,
 *    because the engine's glitch_swap dresses the TILE with its own
 *    filter/transform and the two would clobber each other.
 *
 * 2. THE TARGET IS A LOOK, NOT AN ELEMENT. Relocation swaps whole look objects
 *    between two tiles, which is byte-for-byte the same operation as a decoy
 *    noise swap - that is the twist ("relocation hides inside the noise") and it
 *    is why swap_rate raises paranoia instead of just visual load. Hitboxes never
 *    move under a finger: the element stays put, the content changes.
 *
 * 3. DRIFT IS TWO LAYERS. `.g-lf-row` (outer) is handed to engine row_drift,
 *    which owns its transform and derives amplitude from the CLAMPED bgIntensity
 *    channel. `.g-lf-strip` (inner) carries our own CSS marquee for the toroidal
 *    wrap, whose PERIOD is a pace (like a cadence), not an effect strength. Two
 *    elements, two transforms, no fighting. Under reduced motion the marquee is
 *    off and rows step discretely instead (see step()).
 * ==========================================================================*/

import { GRADIENTS, HUES, PLAYTEST } from './constants.js';
import { el, clamp, shuffle } from './util.js';

/** Marquee period band, seconds. drift 0 -> slow, drift 1 -> fast. */
const DUR_SLOW_SEC = 44;
const DUR_FAST_SEC = 12;

/* ----------------------------------------------------------------------------
 * LOOK PAINTING - shared by the board tiles and by every card in hud.js, so a
 * briefing card, a peek card and the found spotlight are guaranteed to render
 * the target exactly as the board does (same gradient, same hue, same url, same
 * DOM layer - no canvas anywhere).
 * -------------------------------------------------------------------------- */
const VIDEO_EXT_RE = /\.(mp4|webm|m4v)(\?|#|$)/i;

/** Is this url a <video> tile rather than an <img>? */
export function isVideoUrl(url) { return VIDEO_EXT_RE.test(String(url || '')); }

/** Build the media element for a url. Never throws; broken media self-removes. */
export function mediaElFor(url) {
  if (!url) return null;
  if (isVideoUrl(url)) {
    const v = el('video', 'g-lf-media');
    if (!v) return null;
    v.muted = true; v.loop = true; v.autoplay = true; v.playsInline = true;
    v.setAttribute('muted', '');
    v.setAttribute('loop', '');
    v.setAttribute('playsinline', '');
    v.setAttribute('preload', 'metadata');
    v.src = url;
    if (typeof v.play === 'function') {
      try { const p = v.play(); if (p && p.catch) p.catch(() => {}); } catch (e) { /* autoplay policy */ }
    }
    return v;
  }
  const img = el('img', 'g-lf-media');
  if (!img) return null;
  img.alt = '';
  img.setAttribute('draggable', 'false');
  // never leave a broken tile: drop the media, the gradient look still stands
  img.addEventListener('error', () => {
    try { if (img.parentNode) img.remove(); } catch (e) { /* ignore */ }
  });
  img.src = url;
  return img;
}

/**
 * Paint a look ({grad, hue, url}) into any host element, creating the
 * `.g-lf-skin` layer if it is missing and reusing an unchanged media element
 * (a re-decode on every churn tick would strobe the board).
 */
export function paintLook(host, look) {
  if (!host || !look || !host.appendChild) return null;
  let skin = null;
  const kids = host.children || [];
  for (const k of kids) {
    if (k && k.classList && k.classList.contains && k.classList.contains('g-lf-skin')) { skin = k; break; }
  }
  if (!skin) {
    skin = el('div', 'g-lf-skin');
    if (!skin) return null;
    host.appendChild(skin);
  }
  for (let g = 1; g <= GRADIENTS; g++) skin.classList.remove('g-lf-g' + g);
  skin.classList.add('g-lf-g' + (look.grad || 1));
  if (skin.style) skin.style.setProperty('--g-lf-hue', (look.hue || 0) + 'deg');

  const existing = (skin.children && skin.children.length) ? skin.children[0] : null;
  if (existing && existing._lfUrl === look.url) return skin;
  if (existing) { try { existing.remove(); } catch (e) { /* ignore */ } }
  if (!look.url) return skin;
  const media = mediaElFor(look.url);
  if (media) { media._lfUrl = look.url; skin.appendChild(media); }
  return skin;
}

/** Rows from a density: 12 -> 3, 16/20 -> 3-4, 30 -> 4, 40 -> 5. */
export function rowsFor(density) {
  return clamp(Math.round(Math.sqrt(Math.max(1, density) / 1.75)), 3, 6);
}

/** Tiles per row - deliberately uneven so no two rows wrap in step. */
export function rowSizes(density, rng) {
  const rows = rowsFor(density);
  const base = Math.floor(density / rows);
  const sizes = new Array(rows).fill(base);
  let left = density - base * rows;
  for (let i = 0; left > 0; i = (i + 1) % rows, left--) sizes[i] += 1;
  // one seeded +-1 nudge per pair of rows, conserving the total
  for (let i = 0; i + 1 < rows; i += 2) {
    if (sizes[i] > 3 && rng() < 0.6) { sizes[i] -= 1; sizes[i + 1] += 1; }
  }
  return sizes;
}

/**
 * Unique (gradient x hue) look signatures, seeded. 8 x 7 = 56 combos, which
 * covers the largest board (40) with room for the target to stay unique.
 */
export function signaturePool(rng) {
  const all = [];
  for (let g = 1; g <= GRADIENTS; g++) for (const h of HUES) all.push({ grad: g, hue: h });
  return shuffle(all, rng);
}

/**
 * @param {Object} o
 * @param {HTMLElement} o.mount     the .g-lf-view element
 * @param {number} o.density        tile count
 * @param {Function} o.rng          the class's seeded rng
 * @param {number} o.drift          0..1 drift dial (period only - see header)
 * @param {boolean} o.lite          coarse pointer / low quality tier
 * @param {boolean} o.reduced       reduced motion
 * @param {Function} o.onTileClick  (tile, event) => void
 * @param {Function=} o.log
 */
export function createBoard(o) {
  const opts = o || {};
  const rng = typeof opts.rng === 'function' ? opts.rng : Math.random;
  const density = Math.max(4, opts.density | 0);
  const reduced = !!opts.reduced;
  const lite = !!opts.lite;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const videoCap = lite ? PLAYTEST.VIDEO_TILE_CAP_LITE : PLAYTEST.VIDEO_TILE_CAP;

  const mosaic = el('div', 'g-lf-mosaic');
  const rows = [];        // [{ el, strip, tiles:[tile], reps, dir }]
  const tiles = [];       // logical tiles, index === board index
  const byEl = new Map(); // element copy -> tile
  let videoTiles = 0;
  let destroyed = false;

  const sizes = rowSizes(density, rng);
  const sigs = signaturePool(rng);

  /* ---------------------------------------------------------------- build */
  let idx = 0;
  sizes.forEach((count, r) => {
    const rowEl = el('div', 'g-lf-row');
    const strip = el('div', 'g-lf-strip' + (r % 2 ? ' g-lf-rev' : ''));
    // Enough repeats that the wrap never exposes a gap on a wide view.
    const reps = count <= 5 ? 3 : 2;
    const durSec = (DUR_SLOW_SEC - (DUR_SLOW_SEC - DUR_FAST_SEC) * clamp(opts.drift, 0, 1))
      * (0.85 + 0.3 * rng());
    if (strip) {
      strip.style.setProperty('--g-lf-reps', String(reps));
      strip.style.setProperty('--g-lf-dur', durSec.toFixed(1) + 's');
      if (reduced) strip.classList.add('g-lf-static');
    }
    const rowTiles = [];
    for (let c = 0; c < count; c++) {
      const sig = sigs[idx % sigs.length] || { grad: 1 + (idx % GRADIENTS), hue: HUES[idx % HUES.length] };
      const tile = {
        i: idx, row: r, col: c,
        grad: sig.grad, hue: sig.hue,
        url: null, remote: false, isVideo: false,
        target: false, warm: false,
        els: [],
      };
      tiles.push(tile);
      rowTiles.push(tile);
      idx += 1;
    }
    // rep 0 is the primary set; the rest are wrap clones showing the same look
    for (let rep = 0; rep < reps; rep++) {
      for (const tile of rowTiles) {
        const node = buildTileEl(tile, rep);
        if (node && strip) strip.appendChild(node);
      }
    }
    if (rowEl && strip) rowEl.appendChild(strip);
    if (mosaic && rowEl) mosaic.appendChild(rowEl);
    rows.push({ el: rowEl, strip, tiles: rowTiles, reps, durSec, dir: r % 2 ? 'r' : 'l' });
  });
  if (opts.mount && mosaic && opts.mount.appendChild) {
    // Tall boards must not push the class panel past the window: the row count is
    // only known here, so the tile size is trimmed here too (5 rows ~ 350px,
    // 6 rows ~ 370px, both inside the shell's 420px class root). The CSS keeps the
    // default for small boards - this only ever shrinks.
    const size = sizes.length >= 6 ? [58, 52] : (sizes.length >= 5 ? [66, 60] : null);
    if (size && opts.mount.style) {
      opts.mount.style.setProperty('--g-lf-tw', size[0] + 'px');
      opts.mount.style.setProperty('--g-lf-th', size[1] + 'px');
    }
    opts.mount.appendChild(mosaic);
  }

  function buildTileEl(tile, rep) {
    const node = el('div', 'g-lf-tile');
    if (!node) return null;
    node.setAttribute('data-lf-tile', String(tile.i));
    node.setAttribute('role', 'button');
    node.setAttribute('tabindex', '-1');
    node.style.setProperty('--g-lf-i', String(tile.i));
    const skin = el('div', 'g-lf-skin g-lf-g' + tile.grad);
    if (skin) {
      skin.style.setProperty('--g-lf-hue', tile.hue + 'deg');
      node.appendChild(skin);
    }
    // Per-tile listeners rather than one delegated handler: the DOM double has no
    // closest(), and precise per-element hit targets are exactly what a
    // click-precision board wants (engine bursts over it are pointer-events:none).
    node.addEventListener('click', (e) => {
      if (destroyed) return;
      try { if (opts.onTileClick) opts.onTileClick(tile, e); } catch (err) { say('tile click: ' + ((err && err.message) || err)); }
    });
    tile.els.push(node);
    node._lfRep = rep;
    byEl.set(node, tile);
    return node;
  }

  /* ---------------------------------------------------------------- paint */
  /** Repaint every element copy of a tile from its look fields. */
  function repaint(tile) {
    if (!tile) return;
    for (const node of tile.els) paintLook(node, tile);
  }

  /**
   * Give a tile a url. Video tiles are budgeted (DTRH node discipline): past the
   * cap we keep the gradient look rather than melting a low-end device.
   */
  function setUrl(tile, draw) {
    if (!tile) return;
    const url = draw && draw.url ? draw.url : null;
    const isVid = isVideoUrl(url);
    if (isVid && !tile.isVideo && videoTiles >= videoCap) return;   // budget: skip, keep the look
    if (tile.isVideo && !isVid) videoTiles = Math.max(0, videoTiles - 1);
    if (!tile.isVideo && isVid) videoTiles += 1;
    tile.isVideo = isVid;
    tile.url = url;
    tile.remote = !!(draw && draw.remote);
    repaint(tile);
  }

  /* ---------------------------------------------------------------- looks */
  /**
   * Exchange the whole look between two tiles: THE swap primitive.
   *
   * `target` and `warm` travel WITH the look, because both describe the content,
   * not the seat - that is what makes relocation identical to noise churn. The
   * one invariant: the target is never also a near-twin of itself, or clicking it
   * would fire the warm tease instead of a find.
   */
  function swapLooks(a, b) {
    if (!a || !b || a === b) return false;
    const keep = { grad: a.grad, hue: a.hue, url: a.url, remote: a.remote, isVideo: a.isVideo, warm: a.warm, target: a.target };
    a.grad = b.grad; a.hue = b.hue; a.url = b.url; a.remote = b.remote; a.isVideo = b.isVideo; a.warm = b.warm; a.target = b.target;
    b.grad = keep.grad; b.hue = keep.hue; b.url = keep.url; b.remote = keep.remote; b.isVideo = keep.isVideo; b.warm = keep.warm; b.target = keep.target;
    if (a.target) a.warm = false;
    if (b.target) b.warm = false;
    repaint(a); repaint(b);
    return true;
  }

  /** Signatures currently on the board, so a "warm" tile never becomes a twin of
   *  some OTHER tile by accident (that would be an unwinnable ambiguity). */
  function usedSignatures() {
    const used = new Set();
    for (const tile of tiles) used.add(tile.grad + ':' + tile.hue);
    return used;
  }

  /**
   * Near-twin decoys - the classic-difficulty lever, tiers 3-4 only.
   *
   * The provider is ASKED for same-niche decoys (claim spec nearTwinBias) but does
   * not honour the hint yet, so this is the local fallback that makes the tease
   * real either way:
   *   STRONG twins carry the target's actual media at a different hue (capped, or
   *   half the board would be literal copies);
   *   WEAK twins take the target's gradient at an unused hue - visually adjacent,
   *   never ambiguous.
   * Both are tagged `warm`, which is the only thing index.js reads.
   */
  function assignWarm(o) {
    const opts = o || {};
    const share = clamp(opts.share, 0, 1);
    const wantRng = typeof opts.rng === 'function' ? opts.rng : rng;
    for (const tile of tiles) if (!tile.target) tile.warm = false;
    if (share <= 0) return 0;

    const target = api.targetTile();
    if (!target) return 0;
    const urlCap = Number.isFinite(opts.urlCap) ? opts.urlCap : PLAYTEST.NEAR_TWIN_URL_CAP;
    const want = Math.min(Math.round(share * tiles.length), Math.floor(tiles.length / 2));
    const used = usedSignatures();
    const freeHues = HUES.filter((h) => !used.has(target.grad + ':' + h));
    const candidates = shuffle(tiles.filter((tile) => !tile.target), wantRng);

    let made = 0;
    let strong = 0;
    for (const tile of candidates) {
      if (made >= want) break;
      if (strong < urlCap && target.url) {
        // same media, different hue: the honest local version of a near-twin
        used.delete(tile.grad + ':' + tile.hue);
        tile.url = target.url;
        tile.isVideo = isVideoUrl(target.url);
        tile.warm = true;
        used.add(tile.grad + ':' + tile.hue);
        strong += 1; made += 1;
        repaint(tile);
        continue;
      }
      const hue = freeHues.shift();
      if (hue == null) break;                 // out of collision-free signatures
      used.delete(tile.grad + ':' + tile.hue);
      tile.grad = target.grad;
      tile.hue = hue;
      tile.warm = true;
      used.add(tile.grad + ':' + tile.hue);
      made += 1;
      repaint(tile);
    }
    return made;
  }

  /* ------------------------------------------------------------- lifecycle */
  const api = {
    root: mosaic,
    tiles,
    rows,
    density,
    get videoTiles() { return videoTiles; },

    /** Every row element - engine row_drift's opts.targets. */
    rowEls() { return rows.map((r) => r.el).filter(Boolean); },
    /** Every strip - our own marquee layer (pause/resume, discrete stepping). */
    stripEls() { return rows.map((r) => r.strip).filter(Boolean); },
    /** Every element copy of every tile (glitch_swap targets). */
    tileEls() {
      const out = [];
      for (const t of tiles) for (const n of t.els) out.push(n);
      return out;
    },
    /** The PRIMARY element copy of a tile (ceremonies anchor to it). */
    primaryEl(tile) { return tile && tile.els.length ? tile.els[0] : null; },
    tileFor(node) { return byEl.get(node) || null; },
    targetTile() { return tiles.find((t) => t.target) || null; },

    setUrl, repaint, swapLooks, assignWarm,

    /** Mark/unmark the hunt target (a look field, so it rides swaps). */
    setTarget(tile) {
      for (const t of tiles) t.target = false;
      if (tile) tile.target = true;
    },

    /** Class toggling on every copy of a tile (found rim, pity, warm). */
    mark(tile, cls, on) {
      if (!tile) return;
      for (const n of tile.els) {
        try { if (on) n.classList.add(cls); else n.classList.remove(cls); } catch (e) { /* ignore */ }
      }
    },
    clearMark(cls) { for (const t of tiles) api.mark(t, cls, false); },

    /** Freeze / thaw our marquee (pause, suspend, the found ceremony). */
    freeze(on) {
      for (const r of rows) {
        if (!r.strip || !r.strip.style) continue;
        try { r.strip.style.animationPlayState = on ? 'paused' : 'running'; } catch (e) { /* ignore */ }
      }
    },

    /**
     * Retune the marquee period. `mult` is a SPEED multiplier, so the clutch
     * ease (0.8 = 20% slower) lengthens the period. Always derived from each
     * row's build-time period, never from the last value - compounding this was
     * the bug that made a second ease call stop the board dead.
     */
    setDriftMult(mult) {
      const m = clamp(mult, 0.25, 4);
      for (const r of rows) {
        if (!r.strip || !r.strip.style) continue;
        r.strip.style.setProperty('--g-lf-dur', (r.durSec / m).toFixed(1) + 's');
      }
    },

    /**
     * Reduced-motion drift: one discrete row step. Rotates each row's element
     * order (first copy to the end) in alternating directions - positions really
     * do change, the dossier's "one tile-width slide every ~4s" without any
     * transform or transition (both of which the shell has frozen).
     */
    step() {
      for (const r of rows) {
        if (!r.strip || !r.strip.children || r.strip.children.length < 2) continue;
        try {
          const kids = Array.prototype.slice.call(r.strip.children);
          // appendChild MOVES a node that is already a child, in the real DOM and
          // in the test double alike - so re-appending in a rotated order is the
          // one rotation primitive that needs no insertBefore().
          const rotated = r.dir === 'l'
            ? kids.slice(1).concat([kids[0]])
            : [kids[kids.length - 1]].concat(kids.slice(0, -1));
          for (const k of rotated) r.strip.appendChild(k);
        } catch (e) { /* ignore */ }
      }
    },

    destroy() {
      destroyed = true;
      byEl.clear();
      try { if (mosaic && mosaic.remove) mosaic.remove(); } catch (e) { /* ignore */ }
    },
  };

  return api;
}

export default createBoard;
