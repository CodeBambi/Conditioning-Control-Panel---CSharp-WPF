/* ============================================================================
 * games/sort/deck.js - THE DECK. PURE, seeded, and the ONLY place that decides
 * which side of the room a card belongs on.
 *
 * SORT deals 90 / 105 / 120 / 120 cards by grade tier, 55% of them TARGET, the
 * rest NOISE, interleaved off the class seed with a run cap of 4 (3 at tier 4)
 * so the room never turns into "press right eleven times". Loops are preferred
 * 60/40 over stills wherever a source has both, because a still stack reads as
 * a slideshow and the wall wants movement in it.
 *
 * THE LEDGER IS THE TAG. `judge()` compares the swipe to `card.tag`, which the
 * HOST stamped when it served the row - never to pixels, never to a filename,
 * never to a guess. In QUICK SORT (the consent-less / one-folder fallback) the
 * truth is `card.kind` instead: LIVE goes right, STILL goes left. Both paths
 * come through one function so the room can never grade itself two ways.
 *
 * THE POOL IS AN INTERFACE, NOT AN IMPORT. This file is handed something that
 * answers `next(tag, {prefer})` and it never knows whether that is LOT P's real
 * tagged provider, a QUICK SORT adapter over the ordinary `claim()` pool
 * (`wrapQuickPool` below), or a test double. A dry tag re-serves its own rows -
 * that is the provider's promise, not ours - so `next` returning null means a
 * tag with ZERO rows, which is the one case the door is supposed to refuse.
 *
 * RETAKE. `rowsFromCards` / `deckFromRows` are the retake cache's two ends: the
 * class writes the dealt rows into game meta once, and a retake on the same day
 * and seed deals those exact rows again rather than claiming a fresh pool.
 * ==========================================================================*/

/** Every dial the deck turns. Nothing here may be re-typed by a caller. */
export const DECK = Object.freeze({
  /** Share of the deck that is TARGET (the right pile). */
  TARGET_SHARE: 0.55,
  /** Cards dealt, by grade tier.
   *  THE DECK IS A COUNT AND THE CLASS IS A CLOCK, so the two have to move
   *  together. At the 180s budget (the class-length wave; it was 120s and
   *  60/70/80/80) a competent player sees roughly 148 cards at tier 1 and 200
   *  at tier 4, so 90/105/120/120 distinct rows is ~1.6-1.7 passes over the
   *  deck - the same "you start meeting cards again near the end" feel the
   *  120s class had at 60/80. Repeats are not a failure here (nextCard()
   *  reshuffles for ever and the SEEN trickster wants them); the size is only
   *  how long the room stays fresh. */
  SIZE_BY_TIER: Object.freeze({ 1: 90, 2: 105, 3: 120, 4: 120 }),
  /** Longest run allowed on one side, by grade tier. */
  MAX_RUN_BY_TIER: Object.freeze({ 1: 4, 2: 4, 3: 4, 4: 3 }),
  /** Preference roll toward a loop when a source has both kinds. */
  LOOP_SHARE: 0.6,
  /** Distinct rows a side needs to play clean (the door's "thin" line). */
  PER_SOURCE_MIN: 12,
  /** The two piles, in the order the room reads them: right, then left. */
  TAGS: Object.freeze(['target', 'noise']),
});

/** Right is the TARGET pile, left is the NOISE pile. Fixed for the class. */
export const SIDE_FOR_TAG = Object.freeze({ target: 'right', noise: 'left' });
export const TAG_FOR_SIDE = Object.freeze({ right: 'target', left: 'noise' });

function tierOf(tier) { return Math.max(1, Math.min(4, Math.round(Number(tier) || 1))); }
function intOf(v, dflt) { const n = Math.round(Number(v)); return Number.isFinite(n) ? n : dflt; }

/** Cards a class of this tier deals. */
export function sizeForTier(tier) { return DECK.SIZE_BY_TIER[tierOf(tier)]; }
/** Longest same-side run this tier allows. */
export function maxRunForTier(tier) { return DECK.MAX_RUN_BY_TIER[tierOf(tier)]; }
/** How many of `size` cards are TARGET (55%, to the nearest card). */
export function targetCountFor(size) {
  const n = Math.max(0, intOf(size, 0));
  return Math.max(0, Math.min(n, Math.round(n * DECK.TARGET_SHARE)));
}

/** The longest run of one value in a list. */
export function longestRun(list) {
  let best = 0; let run = 0; let last = null;
  for (const v of (list || [])) {
    run = (v === last) ? run + 1 : 1;
    last = v;
    if (run > best) best = run;
  }
  return best;
}

/**
 * Can `same` more of the tag we just played and `other` of the opposite one
 * still be laid out under the run cap, given the block we are standing in is
 * already `run` long? The current block can take `maxRun - run` more, and every
 * remaining card of the other tag can open at most one fresh block of maxRun.
 * The mirror condition keeps the OTHER tag legal too.
 */
export function arrangeable(same, other, run, maxRun) {
  if (same < 0 || other < 0) return false;
  if (same > (maxRun - run) + maxRun * other) return false;
  if (other > maxRun * (same + 1)) return false;
  return true;
}

/**
 * The seeded interleave: a list of tags, `targetCount` of them 'target', with
 * no run longer than `maxRun`. The roll is weighted by what is left, so the two
 * piles run out together instead of the deck ending on a wall of one side.
 * @returns {string[]}
 */
export function tagPlan({ size, targetCount, maxRun, rng } = {}) {
  const total = Math.max(0, intOf(size, 0));
  const cap = Math.max(1, intOf(maxRun, 4));
  const roll = typeof rng === 'function' ? rng : () => 0.5;
  let t = Math.max(0, Math.min(total, intOf(targetCount, targetCountFor(total))));
  let n = total - t;
  const out = [];
  let last = null;
  let run = 0;
  for (let i = 0; i < total; i++) {
    const blockedT = (last === 'target' && run >= cap);
    const blockedN = (last === 'noise' && run >= cap);
    const canT = t > 0 && !blockedT;
    const canN = n > 0 && !blockedN;
    let pick;
    if (canT && !canN) pick = 'target';
    else if (canN && !canT) pick = 'noise';
    else if (!canT && !canN) break;                 // nothing legal is left
    else {
      pick = roll() < (t / (t + n)) ? 'target' : 'noise';
      /* THE FEASIBILITY GUARD. A greedy pick that leaves an unarrangeable
       * remainder is the bug that ends a deck on six noise cards; check the
       * remainder before committing and take the other side if it fails. */
      const nt = pick === 'target' ? t - 1 : t;
      const nn = pick === 'noise' ? n - 1 : n;
      const nrun = (pick === last) ? run + 1 : 1;
      const same = pick === 'target' ? nt : nn;
      const other = pick === 'target' ? nn : nt;
      if (!arrangeable(same, other, nrun, cap)) {
        pick = pick === 'target' ? 'noise' : 'target';
      }
    }
    if (pick === 'target') t -= 1; else n -= 1;
    run = (pick === last) ? run + 1 : 1;
    last = pick;
    out.push(pick);
  }
  return out;
}

/* ============================================================================
 * QUICK SORT - the fallback pool adapter.
 *
 * A player with no remote consent and one flat folder still gets a room: LIVE
 * (a gif or a video) is the right pile, STILL is the left. The ordinary
 * `claim()` pool answers `next('loop'|'still') -> {url, remote}`, so this wraps
 * it in the tagged shape the rest of this file speaks. It is deliberately the
 * ONLY place that knows the two APIs differ.
 * ==========================================================================*/
export function wrapQuickPool(claimPool) {
  const p = claimPool || null;
  const served = { target: 0, noise: 0 };
  const seen = { target: new Set(), noise: new Set() };
  const kindFor = (tag) => (tag === 'target' ? 'loop' : 'still');
  return {
    quick: true,
    next(tag) {
      const tg = tag === 'noise' ? 'noise' : 'target';
      const kind = kindFor(tg);
      let row = null;
      try { row = p && typeof p.next === 'function' ? p.next(kind) : null; } catch (e) { row = null; }
      if (!row || !row.url) return null;
      served[tg] += 1;
      seen[tg].add(row.url);
      return {
        url: row.url,
        remote: !!row.remote,
        kind,
        mime: '',
        poster: (row && typeof row.poster === 'string') ? row.poster : '',
        tag: tg,
        src: row.remote ? 'remote' : 'local',
      };
    },
    counts() {
      return {
        target: { distinct: seen.target.size, served: served.target },
        noise: { distinct: seen.noise.size, served: served.noise },
      };
    },
    thin(tag) { return seen[tag === 'noise' ? 'noise' : 'target'].size < DECK.PER_SOURCE_MIN; },
    empty(tag) { return served[tag === 'noise' ? 'noise' : 'target'] === 0 && seen[tag === 'noise' ? 'noise' : 'target'].size === 0; },
    prewarm() { /* the claim pool prewarmed itself */ },
    /** The tagged pool's spare(tag): rows held beyond the deal. The ordinary
     *  claim pool keeps no tagged reserve, so the honest answer is none - the
     *  substitute widener just falls back to the deck's own survivors. */
    spare() { return []; },
    /* THE MANIFEST SEAM (0825) rides through to the wrapped claim pool, so the
     * room speaks one media API whichever pool shape stands behind it. Absent
     * verbs answer the inert thing: nothing warmed, nothing broken, ready NOW
     * (a pool with no warm rail is the desktop's local disk - already instant). */
    warmManifest(entries, opts) {
      try { return p && typeof p.warmManifest === 'function' ? p.warmManifest(entries, opts) : 0; }
      catch (e) { return 0; }
    },
    warmCursor(i) {
      try { if (p && typeof p.warmCursor === 'function') p.warmCursor(i); } catch (e) { /* noop */ }
    },
    ready(url, opts) {
      try { if (p && typeof p.ready === 'function') return p.ready(url, opts); } catch (e) { /* fall through */ }
      return Promise.resolve(true);
    },
    markBroken(url) {
      try { if (p && typeof p.markBroken === 'function') p.markBroken(url); } catch (e) { /* noop */ }
    },
    isBroken(url) {
      try { return !!(p && typeof p.isBroken === 'function' && p.isBroken(url)); } catch (e) { return false; }
    },
    /* THE VET SEAM (0827) rides through too. A quick pool is the host's own
     * disk, which the vet answers "alive" for without a probe - so the door's
     * gate over a quick sort opens on the next tick, exactly as it did. */
    vet(rows, opts) {
      try { if (p && typeof p.vet === 'function') return p.vet(rows, opts); } catch (e) { /* fall through */ }
      return Promise.resolve(null);
    },
    /** A quick pool has no tag to top up - the ordinary claim keeps itself fed. */
    refill() { return Promise.resolve(0); },
    dealt() {
      const rows = [];
      for (const tg of DECK.TAGS) for (const url of seen[tg]) rows.push({ url, tag: tg, src: 'local' });
      return rows;
    },
    dispose() { try { if (p && typeof p.release === 'function') p.release(); } catch (e) { /* noop */ } },
  };
}

/* ------------------------------------------------------------------ cards -- */
function cardFrom(row, i, quick) {
  const kind = row.kind === 'still' ? 'still' : 'loop';
  const tag = quick
    ? (kind === 'loop' ? 'target' : 'noise')
    : (row.tag === 'noise' ? 'noise' : 'target');
  return {
    i,
    url: String(row.url || ''),
    kind,
    mime: String(row.mime || ''),
    tag,
    src: String(row.src || ''),
    /** The clip's own still, when the host sent one (a remote loop): the face
     *  while the clip buffers, the face over the decoder ceiling. */
    poster: String(row.poster || ''),
    remote: !!row.remote,
    /** A url this deck has already dealt. Repeats are fine in a sort - and a
     *  deck that knows about them hands LOT D's SEEN card for free. */
    seen: false,
  };
}

/**
 * Deal the class.
 * @param {Object} o
 *   pool   anything answering next(tag,{prefer}) / thin(tag) / empty(tag)
 *   seed   the class seed (string)
 *   tier   1..4
 *   quick  QUICK SORT (truth = kind)
 *   rng    optional seeded stream (the caller usually passes makeRng(seed+'|deck'))
 *   size   optional override (tests + a short budget)
 * @returns {{cards:Array, thin:boolean, thinTags:string[], counts:Object, plan:string[]}}
 */
export function buildDeck({ pool, seed, tier, quick, rng, size } = {}) {
  const gradeTier = tierOf(tier);
  const total = Math.max(0, intOf(size, sizeForTier(gradeTier)));
  const maxRun = maxRunForTier(gradeTier);
  const roll = typeof rng === 'function' ? rng : makeLocalRng(String(seed == null ? '' : seed) + '|deck');
  const plan = tagPlan({ size: total, targetCount: targetCountFor(total), maxRun, rng: roll });

  const cards = [];
  const urls = new Set();
  const dropped = { target: 0, noise: 0 };
  for (let i = 0; i < plan.length; i++) {
    const tag = plan[i];
    const prefer = roll() < DECK.LOOP_SHARE ? 'loop' : 'still';
    let row = null;
    try { row = pool && typeof pool.next === 'function' ? pool.next(tag, { prefer }) : null; }
    catch (e) { row = null; }
    if (!row || !row.url) { dropped[tag] += 1; continue; }
    const card = cardFrom(row, cards.length, !!quick);
    /* a card's TAG is the plan's, not the row's guess, in quick mode only -
     * everywhere else the row's tag is the ledger and the plan bends to it */
    if (!quick && card.tag !== tag) card.tag = row.tag === 'noise' ? 'noise' : 'target';
    card.seen = urls.has(card.url);
    urls.add(card.url);
    cards.push(card);
  }

  const counts = {
    size: cards.length,
    target: cards.filter((c) => c.tag === 'target').length,
    noise: cards.filter((c) => c.tag === 'noise').length,
    loops: cards.filter((c) => c.kind === 'loop').length,
    stills: cards.filter((c) => c.kind === 'still').length,
    distinct: urls.size,
    repeats: cards.filter((c) => c.seen).length,
    dropped,
    maxRun: longestRun(cards.map((c) => c.tag)),
    planned: total,
  };
  const thinTags = [];
  for (const tag of DECK.TAGS) {
    let isThin = false;
    try { isThin = !!(pool && typeof pool.thin === 'function' && pool.thin(tag)); } catch (e) { isThin = false; }
    if (isThin) thinTags.push(tag);
  }
  return { cards, thin: thinTags.length > 0, thinTags, counts, plan };
}

/** The seeded stream buildDeck falls back on when the caller passes none. */
function makeLocalRng(seedStr) {
  let h = 2166136261 >>> 0;
  const s = String(seedStr);
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  let x = Math.floor(((h >>> 0) / 4294967295) * 0xFFFFFFFF) >>> 0;
  return function () {
    x = (x + 0x6D2B79F5) | 0;
    let t = Math.imul(x ^ (x >>> 15), 1 | x);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/* ------------------------------------------------------------------ judge -- */
/** The tag the LEDGER holds for this card. Quick mode reads the kind instead. */
export function truthTag(card, quick) {
  if (!card) return 'noise';
  if (quick) return card.kind === 'loop' ? 'target' : 'noise';
  return card.tag === 'noise' ? 'noise' : 'target';
}

/**
 * Was that swipe right? `side` is 'left' or 'right' (a key is a swipe, so the
 * keyboard comes through here too).
 * @returns {boolean}
 */
export function judge(card, side, quick) {
  const want = truthTag(card, quick);
  const got = side === 'left' ? 'noise' : side === 'right' ? 'target' : null;
  if (got == null) return false;
  return got === want;
}

/** The side a correct swipe of this card would go. */
export function sideFor(card, quick) { return SIDE_FOR_TAG[truthTag(card, quick)]; }

/* ------------------------------------------------------------- retake cache */
/** The rows a retake re-deals. Keep this shape: the meta blob is capped. */
export function rowsFromCards(cards) {
  return (cards || []).map((c) => ({ url: c.url, tag: c.tag, src: c.src, kind: c.kind }));
}

/** Rebuild a deck from a cached row list. No pool, no rolls, no surprises. */
export function deckFromRows(rows, quick) {
  const list = Array.isArray(rows) ? rows : [];
  const urls = new Set();
  const cards = [];
  for (const r of list) {
    if (!r || !r.url) continue;
    const card = cardFrom(r, cards.length, !!quick);
    card.seen = urls.has(card.url);
    urls.add(card.url);
    cards.push(card);
  }
  return {
    cards,
    thin: false,
    thinTags: [],
    plan: cards.map((c) => c.tag),
    counts: {
      size: cards.length,
      target: cards.filter((c) => c.tag === 'target').length,
      noise: cards.filter((c) => c.tag === 'noise').length,
      loops: cards.filter((c) => c.kind === 'loop').length,
      stills: cards.filter((c) => c.kind === 'still').length,
      distinct: urls.size,
      repeats: cards.filter((c) => c.seen).length,
      dropped: { target: 0, noise: 0 },
      maxRun: longestRun(cards.map((c) => c.tag)),
      planned: cards.length,
      cached: true,
    },
  };
}

/** True when a cached deck may be re-dealt for this class. */
export function cacheUsable(cache, day, seed) {
  if (!cache || typeof cache !== 'object') return false;
  if (!Array.isArray(cache.rows) || cache.rows.length < 2) return false;
  return String(cache.day || '') === String(day) && String(cache.seed || '') === String(seed);
}

export default { DECK, buildDeck, judge, tagPlan, wrapQuickPool, deckFromRows, rowsFromCards };
