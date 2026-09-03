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
 *
 * ---------------------------------------------------------------------------
 * 4. THE LIVE WINDOW - why a dense wall is not 200 animations (0821 perf pass).
 *
 * Chromium keeps ONE decoder and ONE animation clock per image RESOURCE, and
 * every element showing that resource shares both. Two facts fall out, and the
 * whole media layer is built on them:
 *
 *   COST scales with DISTINCT ANIMATED URLS, not with tiles. Each one is a
 *   main-thread gif decode per frame (gif frames are inter-frame dependent, so
 *   there is no skipping ahead), and each one dirties every tile wearing it -
 *   x2-3 again for the toroidal wrap clones, each of which rasters through its
 *   own hue-rotate filter. Past ~30-40 distinct animated urls the decode queue
 *   never drains and Blink drops the whole page to a frame every ~0.75s.
 *
 *   SYNC is the same fact wearing a different hat: two tiles on the same url
 *   CANNOT be desynchronised, because there is only one clock. A cache-busting
 *   suffix (the retired `?ccpd=N` lane trick) splits the resource and does
 *   desync them - by MULTIPLYING the decoder count, which is what put us at
 *   ~192 live decoders on a hard board in the first place.
 *
 * So: the board deals at most `liveCap` DISTINCT animated urls (index.js draws
 * them no-repeat, so no two live tiles ever share a clock -> nothing is ever in
 * lockstep), every other seat wears a STILL, and the ordinary swap churn trades
 * animated looks with still looks so the motion ROAMS across the wall. A seat
 * never changes what it is showing outside the sanctioned swap primitive, so
 * the hunt's "the target is a look" law is untouched.
 *
 * Videos are the cheap case (GPU decode, an independent clock each) and are
 * preferred for live seats - but <video> has a per-page player budget of its
 * own, so `videoCap` counts ELEMENTS (tiles x wrap clones), not tiles.
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
const GIF_RE = /\.gif(\?|#|$)/i;

/** Is this url a <video> tile rather than an <img>? */
export function isVideoUrl(url) { return VIDEO_EXT_RE.test(String(url || '')); }
/** Is this url an animated <img>?
 *
 *  ANIMATED WEBP (ccp-bugs#1086). This used to be a documented blind spot: a webp is classed
 *  'still' by extension, so an animated one paid a decoder and a clock that NO budget on this
 *  page could see - the live window did not count it, the frame governor could not shed it, and
 *  a library of them dealt one main-thread decoder per seat. A dense wall of those is the
 *  reported symptom: the page drops to ~1.3fps, the row has drifted by the time the click lands,
 *  and the right tile registers as a miss.
 *
 *  Animation lives in the webp's VP8X container flag, so a URL genuinely cannot answer it and
 *  the page never gets the bytes. The DESKTOP host does: ArcademyHostService header-probes a
 *  local webp and stamps `#.gif` on its ccp.assets url (AnimatedImageHint - the same fragment-hint
 *  convention provider/index.js hintedPileUrl() uses for blob: rows, and dropped before the fetch
 *  by the URL Standard, so the same bytes load). GIF_RE's `#` alternative already reads it, which
 *  is why every budget below this line needed no change. An unhinted webp stays a still - which
 *  is still the honest answer on the web port, where no host has the file. */
export function isGifUrl(url) { return GIF_RE.test(String(url || '')); }
/** Does this url cost a decoder + a clock? THE budget question. */
export function isAnimatedUrl(url) { return isVideoUrl(url) || isGifUrl(url); }

/** Build the media element for a url. Never throws; broken media self-removes.
 *  `o.low` marks a wrap clone: same pixels, lower fetch priority. */
/* THE SHARED VIDEO DOOR (0830 seam). The engine budgets its own <video>
 * elements but could not see ours; engine/util.js adoptVideo() (perf wave,
 * merge-order independent) registers a game-minted player with that budget.
 * engine/ is an OPTIONAL layer by contract, so this is a dynamic import that
 * may never resolve - and adoptVideo itself is a no-op off the touch arm. */
let engUtil = null;
try {
  import('../../engine/util.js').then((m) => { engUtil = m; }).catch(() => {});
} catch (e) { /* a missing engine costs the seam, never the class */ }

export function mediaElFor(url, o) {
  if (!url) return null;
  const low = !!(o && o.low);
  if (isVideoUrl(url)) {
    const v = el('video', 'g-lf-media');
    if (!v) return null;
    v.muted = true; v.loop = true; v.autoplay = true; v.playsInline = true;
    v.setAttribute('muted', '');
    v.setAttribute('loop', '');
    v.setAttribute('playsinline', '');
    v.setAttribute('preload', 'metadata');
    v.setAttribute('disablepictureinpicture', '');
    try { v.disableRemotePlayback = true; } catch (e) { /* not everywhere */ }
    // a video that will not decode must not hold a media-player slot forever
    if (typeof v.addEventListener === 'function') {
      v.addEventListener('error', () => {
        try { v.removeAttribute('src'); if (v.load) v.load(); } catch (e) { /* ignore */ }
        try { if (v.parentNode) v.remove(); } catch (e) { /* ignore */ }
      });
    }
    v.src = url;
    try { if (engUtil && typeof engUtil.adoptVideo === 'function') engUtil.adoptVideo(v); } catch (e) { /* seam only */ }
    if (typeof v.play === 'function') {
      try { const p = v.play(); if (p && p.catch) p.catch(() => {}); } catch (e) { /* autoplay policy */ }
    }
    return v;
  }
  const img = el('img', 'g-lf-media');
  if (!img) return null;
  img.alt = '';
  img.setAttribute('draggable', 'false');
  // decoding:async keeps a first decode off the layout path; a wrap clone is
  // the same resource as its primary, so it never needs to win a race.
  img.setAttribute('decoding', 'async');
  if (low) img.setAttribute('fetchpriority', 'low');
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
/**
 * RETIRED: the `?ccpd=N` decode-lane trick.
 *
 * It rewrote a local gif's src with a cache-busting suffix so duplicate tiles
 * would get their own Chromium resource - and their own animation clock. It
 * worked, and that was the problem: a hard board draws ~194 decoys from a
 * 60-gif local manifest (ArcademyHostService.LocalAssetSample), so 4 lanes per
 * url meant ~190 DISTINCT animated resources, each decoding gif frames on the
 * main thread. That is the "1 frame every 0.75 sec". Desync bought at 4x the
 * decode cost is not desync, it is a stall.
 *
 * The live window replaces it: index.js draws live seats NO-REPEAT, so no two
 * animated tiles share a url in the first place and there is nothing to split.
 * Remote urls were never rewritten (signed urls / refetch cost) and still are
 * not - that constraint outlived the trick that needed it.
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
  // hue 0 = no rotation, and `filter:none` spares the element its own render
  // surface. One in seven tiles, x2-3 wrap clones - it adds up on a dense wall.
  if (skin.classList) {
    if (!(look.hue | 0)) skin.classList.add('g-lf-h0'); else skin.classList.remove('g-lf-h0');
  }

  const existing = (skin.children && skin.children.length) ? skin.children[0] : null;
  if (existing && existing._lfUrl === look.url) return skin;
  if (!look.url) {
    if (existing) { try { existing.remove(); } catch (e) { /* ignore */ } }
    return skin;
  }
  /* RECYCLE the media element when the kind matches (0821 smoothness pass).
   * The churn swaps looks constantly; tearing down and re-minting an element
   * per repaint is an allocation + listener churn per swap for <img>, and a
   * whole MEDIA PLAYER teardown/create for <video> - player creation is an
   * IPC round-trip and the single most stall-prone thing a swap can do.
   * Setting .src on the existing element is the same visual result. */
  if (existing) {
    const wantVid = isVideoUrl(look.url);
    const isVid = existing.tagName === 'VIDEO';
    if (wantVid === isVid) {
      existing._lfUrl = look.url;
      try {
        existing.src = look.url;
        if (isVid) {
          if (existing.load) existing.load();
          if (existing.play) { const p = existing.play(); if (p && p.catch) p.catch(() => {}); }
        }
        return skin;
      } catch (e) { /* fall through to replace */ }
    }
    try { existing.remove(); } catch (e) { /* ignore */ }
  }
  const media = mediaElFor(look.url, { low: (host._lfRep | 0) > 0 });
  if (media) { media._lfUrl = look.url; skin.appendChild(media); }
  return skin;
}

/** Rows from a density: 12 -> 3, 30 -> 4, 48 -> 5, ~190 (hard wall) -> 10-11.
 *  The old max of 6 silently squashed dense boards into fewer, longer rows. */
export function rowsFor(density) {
  return clamp(Math.round(Math.sqrt(Math.max(1, density) / 1.75)), 3, 12);
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
 * @param {boolean} o.touch         a phone (ctx.platform.isTouch / coarse probe): one video url
 * @param {boolean} o.reduced       reduced motion
 * @param {Function} o.onTileClick  (tile, event) => void
 * @param {Function=} o.onTileHover (tile, event) => void - THE HOVER TELL. The
 *        board only reports the crossing; the GAME owns the throttle and the
 *        phase gate (index.js onTileHover), so this file never decides when a
 *        sound is allowed.
 * @param {Function=} o.log
 */
export function createBoard(o) {
  const opts = o || {};
  const rng = typeof opts.rng === 'function' ? opts.rng : Math.random;
  const density = Math.max(4, opts.density | 0);
  const reduced = !!opts.reduced;
  const lite = !!opts.lite;
  const touch = !!opts.touch;
  const say = typeof opts.log === 'function' ? opts.log : () => {};

  const mosaic = el('div', 'g-lf-mosaic');
  const rows = [];        // [{ el, strip, tiles:[tile], reps, dir }]
  const tiles = [];       // logical tiles, index === board index
  const byEl = new Map(); // element copy -> tile
  let videoTiles = 0;
  let destroyed = false;
  // THE LIVE LEDGER: canonical animated url -> { n, remote }. Its SIZE is the
  // decoder count (one resource, one clock, however many tiles wear it), which
  // is the number the budget is actually about.
  const liveUse = new Map();
  const desyncTimers = new Set(); // pending staggered media paints

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
        // `live` = this seat's media costs a decoder + owns a clock. It is a
        // LOOK field, so it rides swapLooks with everything else.
        live: false,
        target: false, warm: false,
        seq: 0,          // bumped on every look change; stale paints bail on it
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

  /* ---------------------------------------------------------- press road */
  /* TOUCH TAP RESOLUTION (0830). Two measured mis-tap mechanisms on phones:
   *   1. `click` fires at finger-UP plus synthesis delay, and the strip keeps
   *      drifting under the finger the whole time - a press that landed ON the
   *      target resolved 200-350ms later on the neighbour (rig: 0/5 honest
   *      presses survived a 320ms dwell).
   *   2. Under jank the compositor-driven marquee runs ahead of the last
   *      painted frame, so hit-testing resolves against a position the player
   *      never saw.
   * So on touch the MOSAIC resolves the gesture at pointerdown, and rewinds
   * the hit-test by exactly the animation-timeline advance since the last rAF
   * stamp. The timeline is the honest clock: it holds still while the wall is
   * frozen (ceremony, pause) and while a stall stops the clock with us, so the
   * rewind self-calibrates to zero everywhere except the one case it exists
   * for. The resolved tile goes down the SAME onTileClick road - target, warm,
   * miss and the ledger are untouched, and a press that resolves on a wrong
   * tile is still a miss. Never a forgiveness radius, never a fake hit.
   * Desktop (`touch` false) installs none of this and keeps the click road. */
  let pressRafId = 0;
  let pressStamp = null;   // animation-timeline ms at the last painted frame
  let refStripAnim = null;

  function refAnimTime() {
    try {
      if (refStripAnim && refStripAnim.playState !== 'idle') {
        const ct = Number(refStripAnim.currentTime);
        if (Number.isFinite(ct)) return ct;
      }
      refStripAnim = null;
      for (const r of rows) {
        if (!r.strip || typeof r.strip.getAnimations !== 'function') continue;
        const list = r.strip.getAnimations();
        if (list && list.length) {
          refStripAnim = list[0];
          const ct = Number(refStripAnim.currentTime);
          if (Number.isFinite(ct)) return ct;
        }
      }
    } catch (e) { /* no timeline, no rewind */ }
    return null;
  }
  function pressStampLoop() {
    if (destroyed || !touch) return;
    pressStamp = { at: refAnimTime() };
    pressRafId = requestAnimationFrame(pressStampLoop);
  }
  /** byEl only knows tile roots; a press usually lands on the skin or media. */
  function tileFromNode(n) {
    let node = n;
    for (let hops = 0; node && hops < 6; hops++) {
      const t = byEl.get(node);
      if (t) return t;
      node = node.parentNode || null;
    }
    return null;
  }
  function pressResolve(e) {
    if (destroyed || !e || e.isPrimary === false) return;
    const rawTile = tileFromNode(e.target);
    if (!rawTile) return;                     // gaps stay dead, like the click road
    let resolved = rawTile;
    try {
      const at = refAnimTime();
      const dt = (pressStamp && Number.isFinite(pressStamp.at) && Number.isFinite(at))
        ? at - pressStamp.at : 0;
      if (dt > 24 && dt < 1200 && Number.isFinite(e.clientX)
        && typeof document !== 'undefined' && document.elementFromPoint) {
        const row = rows[rawTile.row];
        const strip = row && row.strip;
        if (strip) {
          const dur = parseFloat(strip.style && strip.style.getPropertyValue
            ? strip.style.getPropertyValue('--g-lf-dur') : '') || row.durSec || 30;
          const reps = (row.reps | 0) || 2;
          const w = strip.scrollWidth || 0;
          if (w > 0 && dur > 0) {
            // driftL translates -x, so content seen at clientX now sits at
            // clientX - v*dt; the reversed strip mirrors the sign.
            const v = (w / reps) / dur;
            const shift = (row.dir === 'r' ? 1 : -1) * v * (dt / 1000);
            const t2 = tileFromNode(document.elementFromPoint(e.clientX + shift, e.clientY));
            if (t2 && t2.row === rawTile.row) resolved = t2;   // never jump rows
          }
        }
      }
    } catch (err) { /* the raw tile still stands */ }
    try { if (opts.onTileClick) opts.onTileClick(resolved, e); } catch (err) { say('tile press: ' + ((err && err.message) || err)); }
  }
  if (touch && mosaic && mosaic.addEventListener) {
    mosaic.addEventListener('pointerdown', pressResolve);
    if (typeof requestAnimationFrame === 'function') pressRafId = requestAnimationFrame(pressStampLoop);
  }

  /* ------------------------------------------------------------- budgets */
  /* Every budget here counts what Chromium actually pays for. `maxReps` is the
     multiplier the toroidal wrap applies to every live element, so it has to be
     known before a single url is dealt - which is why this sits AFTER the build
     rather than at the top of the factory. */
  let maxReps = 1;
  for (const r of rows) maxReps = Math.max(maxReps, (r.reps | 0) || 1);

  /** Ceiling on DISTINCT animated urls (= decoders = clocks). See the header. */
  const liveCap = reduced ? Math.max(0, PLAYTEST.LIVE_LOOP_CAP_REDUCED | 0)
    : Math.max(0, Math.min(
      lite ? PLAYTEST.LIVE_LOOP_CAP_LITE : PLAYTEST.LIVE_LOOP_CAP,
      Math.max(PLAYTEST.LIVE_LOOP_MIN, Math.round(density * PLAYTEST.LIVE_LOOP_SHARE)),
      Math.floor(PLAYTEST.LIVE_ELEMENT_CEIL / maxReps),
    ));
  /** <video> is budgeted a SECOND time, in ELEMENTS: a media player is a much
   *  scarcer page resource than an image decoder, and the wrap clones are real
   *  players too. (The 0821 "40% of density" rule shipped ~156 of them.) */
  const videoCap = Math.max(0, Math.min(
    touch ? PLAYTEST.VIDEO_TILE_CAP_TOUCH : (lite ? PLAYTEST.VIDEO_TILE_CAP_LITE : PLAYTEST.VIDEO_TILE_CAP),
    Math.floor(PLAYTEST.VIDEO_ELEMENT_CEIL / maxReps),
    liveCap,
  ));
  // The per-tile sheen sweep is one compositor animation per ELEMENT; on a
  // dense wall that is 400 of them for a decoration nothing reads.
  if (mosaic && mosaic.classList && density > PLAYTEST.SHEEN_MAX_DENSITY) {
    mosaic.classList.add('g-lf-dense');
  }
  say('board budgets: ' + density + ' tiles x' + maxReps + ' reps, live cap '
    + liveCap + ', video cap ' + videoCap + (reduced ? ' (reduced motion)' : ''));

  if (opts.mount && mosaic && opts.mount.appendChild) {
    // The wall fills the window (immersion wave): the row count is only known
    // here, so it is PUBLISHED here and styles.js solves the tile height from it
    // (rows always fill the frame; density never changes with the window - the
    // tile SIZE breathes, the tile COUNT is a tuned dial).
    if (opts.mount.style) {
      opts.mount.style.setProperty('--g-lf-rows', String(sizes.length));
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
      // On touch the press road below already resolved this gesture at
      // pointerdown; letting the late click land too would double-count it.
      if (touch) return;
      try { if (opts.onTileClick) opts.onTileClick(tile, e); } catch (err) { say('tile click: ' + ((err && err.message) || err)); }
    });
    // THE HOVER TELL rides the same per-element wiring as the click, and for
    // the same two reasons: the DOM double has no closest(), and a wrap clone
    // is a real seat the pointer really crosses. pointerenter (not pointerover)
    // so moving WITHIN a tile is silent - one tick per seat entered.
    node.addEventListener('pointerenter', (e) => {
      if (destroyed) return;
      try { if (opts.onTileHover) opts.onTileHover(tile, e); } catch (err) { /* a tell never breaks a hunt */ }
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

  /* --------------------------------------------------------- live ledger */
  /** Drop this tile's claim on its animated url; the resource dies with the
   *  last tile wearing it (that is when Chromium can stop decoding it). */
  function releaseLive(tile) {
    const url = tile._liveUrl;
    if (!url) return;
    tile._liveUrl = null;
    const rec = liveUse.get(url);
    if (!rec) return;
    if (rec.n <= 1) liveUse.delete(url); else rec.n -= 1;
  }
  function acquireLive(tile, url, remote) {
    tile._liveUrl = url;
    const rec = liveUse.get(url);
    if (rec) rec.n += 1; else liveUse.set(url, { n: 1, remote: !!remote });
  }
  /** Would giving this tile `url` mint a NEW decoder we cannot afford?
   *  Adopting a url the wall already animates is free - same resource, same
   *  clock - which is what lets a still-less library rest its sleepers on the
   *  live set instead of on the bundled placeholder floor. */
  function liveBlocked(tile, url) {
    if (liveUse.has(url)) return false;
    // a tile that is the LAST holder of its own url frees a slot as it moves
    const rec = tile._liveUrl ? liveUse.get(tile._liveUrl) : null;
    const freeing = rec && rec.n <= 1 ? 1 : 0;
    return (liveUse.size - freeing) >= liveCap;
  }

  /**
   * Give a tile a url; returns TRUE if the look took.
   *
   * Two budgets can refuse it (the live-decoder window and the <video> element
   * ceiling) and a refusal is not a failure - the caller draws a still instead
   * and the gradient look stands in the meantime. `o.paintDelayMs` defers only
   * the PAINT: the url lands immediately (so target matching and the near-twin
   * bookkeeping are correct the moment we return) while the element - and with
   * it the decoder and the animation clock - starts on its own tick.
   */
  function setUrl(tile, draw, o) {
    if (!tile) return false;
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
    if (anim) acquireLive(tile, url, tile.remote);
    tile.seq = (tile.seq | 0) + 1;

    // Seeded paint stagger: dressing a whole board on one tick starts every
    // clock together (the lockstep blink) and hands the decoder a stampede.
    const wait = o && (o.paintDelayMs > 0 || o.desyncMs > 0)
      ? ((o.paintDelayMs | 0) || (o.desyncMs | 0)) : 0;
    if (!wait) { repaint(tile); return true; }
    const seq = tile.seq;
    const t = setTimeout(() => {
      desyncTimers.delete(t);
      if (destroyed || tile.seq !== seq) return;   // a swap/melt got here first
      repaint(tile);
    }, wait);
    desyncTimers.add(t);
    return true;
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
    const keep = { grad: a.grad, hue: a.hue, url: a.url, remote: a.remote, isVideo: a.isVideo, live: a.live, warm: a.warm, target: a.target, liveUrl: a._liveUrl };
    a.grad = b.grad; a.hue = b.hue; a.url = b.url; a.remote = b.remote; a.isVideo = b.isVideo; a.live = b.live; a.warm = b.warm; a.target = b.target;
    b.grad = keep.grad; b.hue = keep.hue; b.url = keep.url; b.remote = keep.remote; b.isVideo = keep.isVideo; b.live = keep.live; b.warm = keep.warm; b.target = keep.target;
    // The ledger's TOTALS are untouched by a swap (the same multiset of looks
    // is on the wall), but each seat's own claim moves with its look.
    a._liveUrl = b._liveUrl; b._liveUrl = keep.liveUrl;
    // a deferred paint from before the swap must not land on the new look
    a.seq = (a.seq | 0) + 1; b.seq = (b.seq | 0) + 1;
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
    // Optional stagger for the strong-twin repaints (per-round target rotation
    // on touch): the url still lands NOW - bookkeeping stays correct - only the
    // paint is deferred, through setUrl's own paintDelayMs seam.
    const paintDelay = Math.max(0, opts.paintDelayMs | 0);
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
      if (strong < urlCap && target.url
        // The target's own url is free to copy when it is already on the wall
        // (it always is - this IS the target's url), so a strong twin never
        // mints a decoder. setUrl still arbitrates, so the budget cannot be
        // side-stepped through this door either.
        && setUrl(tile, { url: target.url, remote: target.remote },
          paintDelay ? { paintDelayMs: paintDelay * (strong + 1) } : null)) {
        // same media, different hue: the honest local version of a near-twin
        used.delete(tile.grad + ':' + tile.hue);
        tile.warm = true;
        used.add(tile.grad + ':' + tile.hue);
        strong += 1; made += 1;
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
    get liveTiles() { return tiles.reduce((n, t) => n + (t.live ? 1 : 0), 0); },

    /** The animated urls currently on the wall, each with its remote flag.
     *  Two callers: index.js draws live seats NO-REPEAT against this (so no two
     *  clocks are ever shared, i.e. nothing can blink in lockstep), and a
     *  library with no stills to rest on parks its sleepers ON this set, where
     *  they ride an existing decoder for free. */
    liveUrls() {
      const out = [];
      for (const [url, rec] of liveUse) out.push({ url, remote: !!(rec && rec.remote) });
      return out;
    },
    /** Budget telemetry: what the wall may spend and what it has spent. */
    liveStats() {
      return {
        cap: liveCap, used: liveUse.size, maxReps,
        tiles: tiles.reduce((n, t) => n + (t.live ? 1 : 0), 0),
        videoCap, videoTiles,
        elements: tiles.reduce((n, t) => n + (t.live ? t.els.length : 0), 0),
      };
    },
    /**
     * ROAMING: k (animated seat, still seat) pairs for the churn to trade, so
     * the live window drifts across the wall like a marquee instead of sitting
     * in the seats it was dealt. The target and its near-twins are excluded -
     * their looks move on the RELOCATION schedule and nowhere else, or the
     * board would be quietly relocating the hunt behind the game's back.
     */
    roamPairs(k, r) {
      const want = Math.max(0, k | 0);
      if (!want) return [];
      const rnd = typeof r === 'function' ? r : rng;
      const awake = [];
      const asleep = [];
      for (const t of tiles) {
        if (t.target || t.warm || !t.url) continue;
        (t.live ? awake : asleep).push(t);
      }
      const a = shuffle(awake, rnd);
      const b = shuffle(asleep, rnd);
      const n = Math.min(want, a.length, b.length);
      const out = [];
      for (let i = 0; i < n; i++) out.push([a[i], b[i]]);
      return out;
    },

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

    /** Freeze / thaw our marquee (pause, suspend, the found ceremony).
     *  Video tiles freeze too: a suspend usually means the HOST wants the
     *  decoder (a mandatory video is playing), and a paused <video> holds its
     *  last frame for free - so the wall keeps its pixels and costs nothing.
     *  Gif tiles cannot be paused from script at all; they are budgeted
     *  instead, which is the whole point of the live window. */
    freeze(on) {
      for (const r of rows) {
        if (!r.strip || !r.strip.style) continue;
        try { r.strip.style.animationPlayState = on ? 'paused' : 'running'; } catch (e) { /* ignore */ }
      }
      if (!videoTiles) return;
      for (const t of tiles) {
        if (!t.isVideo) continue;
        for (const n of t.els) {
          const skin = n && n.children && n.children.length ? n.children[0] : null;
          const media = skin && skin.children && skin.children.length ? skin.children[0] : null;
          if (!media || media.tagName !== 'VIDEO') continue;
          try {
            if (on) { if (media.pause) media.pause(); }
            else if (media.play) { const p = media.play(); if (p && p.catch) p.catch(() => {}); }
          } catch (e) { /* ignore */ }
        }
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
      if (pressRafId && typeof cancelAnimationFrame === 'function') {
        try { cancelAnimationFrame(pressRafId); } catch (e) { /* ignore */ }
      }
      pressRafId = 0;
      byEl.clear();
      for (const t of desyncTimers) clearTimeout(t);
      desyncTimers.clear();
      liveUse.clear();
      // A <video> that is merely detached can keep its media player (and its
      // decoder) alive until GC gets around to it; unhook the source first.
      for (const t of tiles) {
        for (const n of t.els) {
          const skin = n && n.children && n.children.length ? n.children[0] : null;
          const media = skin && skin.children && skin.children.length ? skin.children[0] : null;
          if (!media || media.tagName !== 'VIDEO') continue;
          try { media.pause(); media.removeAttribute('src'); if (media.load) media.load(); } catch (e) { /* ignore */ }
        }
      }
      try { if (mosaic && mosaic.remove) mosaic.remove(); } catch (e) { /* ignore */ }
    },
  };

  return api;
}

export default createBoard;
