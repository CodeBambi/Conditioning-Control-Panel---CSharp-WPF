/* ============================================================================
 * ui/duel/piles.js - the two piles a Sort duel deals: the player's NICHE (right,
 * keep) and their NOISE board (left, bin). Pure over its inputs; the only DOM it
 * ever touches is an `Image` for warming, and only when one exists.
 *
 * Arcademy Sort already plays two tagged piles: its deck.js asks a pool for
 * `next('target'|'noise', {prefer})` and judges every swipe against the tag the
 * pool stamped on the row (the ledger is the tag, never the pixels). Its setup
 * door normally builds that pool through the provider's `claimTagged`. A duel
 * skips the door, so ui/duel/arcademyHost.js hands the class THIS pool through
 * the same `claimTagged` seam, and nothing inside the Sort class changes shape.
 *
 * TARGET STILLS FIRST. Every noise row is a still (the boards are photo boards).
 * A target pile that dealt clips would let a player sort by "does it move"
 * instead of by what it shows, so the target pile serves its stills and reaches
 * for a clip only when it has no still at all.
 *
 * A pile re-serves its own rows in a shuffled cycle when it runs dry, the
 * provider's promise; `next` returns null only for a pile with no rows at all.
 * ==========================================================================*/

export const PILE_PER_SOURCE_MIN = 12;   // Sort's DECK.PER_SOURCE_MIN: the "thin pile" line

/** One Goon media row ({kind:'image'|'video', url, clip?}) as a Sort row, or null. */
export function sortRow(r, tag) {
  if (!r || typeof r.url !== 'string' || !r.url) return null;
  const kind = r.kind === 'video' ? 'loop' : 'still';
  return { url: r.url, remote: false, kind, mime: '', poster: '', tag, src: tag === 'noise' ? 'noise' : 'niche' };
}

/** The rows a pile deals from, deduped by url, target stills first (see the header). */
export function pileRows(rows, tag) {
  const seen = new Set();
  const out = [];
  for (const r of Array.isArray(rows) ? rows : []) {
    const row = sortRow(r, tag);
    if (!row || seen.has(row.url)) continue;
    seen.add(row.url);
    out.push(row);
  }
  if (tag === 'noise') return out.filter((r) => r.kind === 'still');
  const stills = out.filter((r) => r.kind === 'still');
  return stills.length ? stills : out;
}

function shuffle(list, rand) {
  const a = list.slice();
  for (let i = a.length - 1; i > 0; i--) {
    let r = Number(rand());
    if (!Number.isFinite(r) || r < 0 || r >= 1) r = 0;
    const j = Math.floor(r * (i + 1));
    const t = a[i]; a[i] = a[j]; a[j] = t;
  }
  return a;
}

/**
 * The tagged pool Sort's deck draws from.
 * @param {object} o
 * @param {Function|Array} o.target  rows (or () => rows) for the right pile
 * @param {Function|Array} o.noise   rows (or () => rows) for the left pile
 * @param {Function} [o.rand]        () => [0,1)
 * @param {Function} [o.makeImage]   () => an Image-like ({onload,onerror,src}); default: global Image
 */
export function createPilePool({ target, noise, rand = Math.random, makeImage = null } = {}) {
  const read = (src) => { try { return typeof src === 'function' ? src() : src; } catch (_e) { return []; } };
  const piles = { target: pileRows(read(target), 'target'), noise: pileRows(read(noise), 'noise') };
  const order = { target: shuffle(piles.target, rand), noise: shuffle(piles.noise, rand) };
  const cursor = { target: 0, noise: 0 };
  const served = { target: 0, noise: 0 };
  const dealtUrls = { target: new Set(), noise: new Set() };
  const broken = new Set();
  const loaded = new Set();
  const warming = new Map();
  const tagOf = (t) => (t === 'noise' ? 'noise' : 'target');
  const mkImg = () => {
    if (typeof makeImage === 'function') return makeImage();
    return typeof Image === 'function' ? new Image() : null;
  };

  function warm(url) {
    if (!url || loaded.has(url)) return Promise.resolve(true);
    if (broken.has(url)) return Promise.resolve(false);
    if (warming.has(url)) return warming.get(url);
    const img = mkImg();
    if (!img) { loaded.add(url); return Promise.resolve(true); }
    const p = new Promise((resolve) => {
      img.onload = () => { loaded.add(url); warming.delete(url); resolve(true); };
      img.onerror = () => { broken.add(url); warming.delete(url); resolve(false); };
      try { img.src = url; } catch (_e) { warming.delete(url); resolve(false); }
    });
    warming.set(url, p);
    return p;
  }

  return {
    quick: false,
    next(tag) {
      const tg = tagOf(tag);
      const list = order[tg];
      if (!list.length) return null;
      // Skip what broke; a pile where everything broke still answers (Sort's substitute takes over).
      let row = null;
      for (let n = 0; n < list.length * 2; n++) {
        if (cursor[tg] >= order[tg].length) { order[tg] = shuffle(piles[tg], rand); cursor[tg] = 0; }
        row = order[tg][cursor[tg]++];
        if (!broken.has(row.url)) break;
      }
      served[tg]++;
      dealtUrls[tg].add(row.url);
      return Object.assign({}, row);
    },
    counts() {
      return {
        target: { distinct: dealtUrls.target.size, served: served.target, rows: piles.target.length },
        noise: { distinct: dealtUrls.noise.size, served: served.noise, rows: piles.noise.length },
      };
    },
    thin(tag) { return piles[tagOf(tag)].length < PILE_PER_SOURCE_MIN; },
    empty(tag) { return piles[tagOf(tag)].length === 0; },
    prewarm(n) {
      const k = Math.max(0, n | 0);
      for (const tg of ['target', 'noise']) for (const r of order[tg].slice(0, k)) warm(r.url);
    },
    spare(tag) { return piles[tagOf(tag)].filter((r) => !broken.has(r.url)).map((r) => Object.assign({}, r)); },
    warmManifest(entries) {
      let n = 0;
      for (const e of Array.isArray(entries) ? entries.slice(0, 16) : []) {
        if (e && e.url && (e.kind === 'still' || !e.kind)) { warm(e.url); n++; }
      }
      return n;
    },
    warmCursor() { /* the whole pile is a few dozen stills: the manifest warm covers it */ },
    ready(url) { return warm(url); },
    isReady(url) { return loaded.has(url); },
    markBroken(url) { if (url) broken.add(url); },
    isBroken(url) { return broken.has(url); },
    vet() { return Promise.resolve(null); },
    refill() { return Promise.resolve(0); },
    posterOf() { return ''; },
    dealt() {
      const rows = [];
      for (const tg of ['target', 'noise']) for (const url of dealtUrls[tg]) rows.push({ url, tag: tg, src: tg });
      return rows;
    },
    dispose() { warming.clear(); },
  };
}

export default createPilePool;
