/* ============================================================================
 * provider/tagged.js - THE TAGGED POOL (SORT, room 201).
 *
 * `claim()` answers "give me 24 loops"; this answers "give me TWO PILES and tell
 * me which pile every row came from". SORT's whole rule is "right = mine, left =
 * the rest", so the truth of a card has to be a FACT carried on the row - the
 * `tag` the host stamped when it served it - and never a guess about pixels.
 *
 *   const pool = await assets.claimTagged({
 *     sources: [
 *       { tag:'target', kind:'remote', subs:['BambiSleep','sissyhypno'] },
 *       { tag:'noise',  kind:'remote', subs:['pokemon'] },
 *       // local rows instead (never mixed with remote in one claim):
 *       // { tag:'target', kind:'local', folders:['images/bambi'] }
 *       // { tag:'noise',  kind:'local', presetId:'<AssetPreset.Id>' }
 *     ],
 *     want: { loops: 48, stills: 32 },   // totals across tags, a HINT
 *     perSourceMin: 12,                  // "enough to deal without repeating"
 *     seed: 'string',                    // the dry re-serve shuffle
 *     timeoutMs: 6000,                   // resolve anyway, with the thin flags set
 *   });
 *   pool.next('target', { prefer:'loop' })  -> row | null
 *   pool.counts() / pool.thin(tag) / pool.empty(tag) / pool.prewarm(n)
 *   pool.spare(tag) / pool.dealt() / pool.dispose()
 *   pool.refill(tag) -> Promise<added>   (the vet's top-up, see below)
 *
 * FIVE LAWS
 *  1. THE TAG IS THE TRUTH. A row keeps the tag of the SOURCE that served it
 *     (the host stamps it; a host that forgets falls back to the tag of the
 *     request). A url that somehow arrives on both tags is kept by the tag that
 *     saw it FIRST and refused to the other - a card that is both piles at once
 *     is the one lie a sort cannot survive.
 *  2. A TAG HAS ITS OWN CURSOR, and a dry tag NEVER returns null: once its
 *     distinct rows are spent it RE-SERVES its own served list in a SEEDED
 *     shuffle (repeats are fine in a sort; a hole in the deck is not). null
 *     means one thing only: that tag has zero rows.
 *  3. RESOLVE IS BOUNDED THREE WAYS - every tag has `perSourceMin` distinct
 *     rows, or the ask budget is spent, or `timeoutMs` elapses. The door has to
 *     open; a claim that could hang would be a class that never starts.
 *  4. THIN IS FROZEN AT RESOLVE, EMPTY IS LIVE. `thin(tag)` is what the door
 *     warned about ("thin, expect repeats") and it must not change under the
 *     player mid-class; `empty(tag)` is the refusal to start and a late batch
 *     may legitimately lift it.
 *  5. NOTHING HERE FETCHES. Remote goes out as `assets-request` and local as
 *     `local-sample-request`, both over the bridge, both answered by `assets`
 *     keyed by reqId - the same mailbox `claim()` uses (GROUND-RULES §8).
 * ==========================================================================*/

import { isLocalUrl, formatOk, kindOf } from './inventory.js';
import { makeRng, shuffled } from '../core/rng.js';

/** Tuning, exported so a suite asserts the same numbers the pool runs on. */
export const TAGGED = Object.freeze({
  PER_SOURCE_MIN: 12,     // "enough to deal 80 cards without a repeat every hand"
  TIMEOUT_MS: 6000,       // the door opens even if the host says nothing
  MAX_ASKS: 8,            // per TAG, not per source row (the host contract's budget)
  RETRY_MS: 1500,         // backed off by attempt, exactly like claim()
  BATCH_MAX: 24,          // the host's own per-reply cap
  PREWARM_CAP: 12,        // per tag, per prewarm() call (see provider PREWARM_MAX)
  TAG_CAP: 120,           // a page never hoards media (REMOTE_CAP's law, per tag)
  REFILL_MAX: 2,          // refill(tag) rounds a pool grants, each a fresh MAX_ASKS
  REFILL_MS: 6000,        // a refill that hears nothing by then answers 0
});

/** The two tags every caller may read unconditionally, even with no sources. */
const BASE_TAGS = Object.freeze(['target', 'noise']);

/** 'loop' | 'still' from a served row: a video mime is always a loop. */
export function rowKind(entry) {
  const mime = String((entry && entry.mime) || '');
  if (/^video\//i.test(mime)) return 'loop';
  if (/^image\/gif$/i.test(mime)) return 'loop';
  if (entry && (entry.kind === 'loop' || entry.kind === 'still')) return entry.kind;
  return kindOf(entry && entry.url ? entry.url : entry);
}

/** What a source row calls itself when the host does not say (`src` fallback). */
export function sourceLabel(src) {
  if (!src || typeof src !== 'object') return '';
  if (src.kind === 'local') {
    if (src.presetId) return 'preset:' + src.presetId;
    const f = Array.isArray(src.folders) ? src.folders[0] : null;
    return f ? String(f) : 'local';
  }
  const s = Array.isArray(src.subs) ? src.subs[0] : null;
  return s ? 'r/' + String(s) : 'r/?';
}

/** Normalise + validate the caller's source rows. Never throws. */
export function normalizeSources(list) {
  const out = [];
  for (const raw of (Array.isArray(list) ? list : [])) {
    if (!raw || typeof raw !== 'object') continue;
    const tag = typeof raw.tag === 'string' && raw.tag ? raw.tag : '';
    if (!tag) continue;
    const local = raw.kind === 'local';
    const subs = Array.isArray(raw.subs) ? raw.subs.filter((s) => typeof s === 'string' && s).slice(0, 64) : [];
    const folders = Array.isArray(raw.folders) ? raw.folders.filter((s) => typeof s === 'string' && s).slice(0, 64) : [];
    const presetId = typeof raw.presetId === 'string' && raw.presetId ? raw.presetId : '';
    if (local) { if (!folders.length && !presetId) continue; }
    else if (!subs.length) continue;
    out.push(Object.freeze({ tag, kind: local ? 'local' : 'remote', subs, folders, presetId }));
  }
  return out;
}

/**
 * @param {Object} o
 * @param {Object} o.spec        the caller's claimTagged() argument
 * @param {Object} o.channel     provider/remote.js channel (request/receive)
 * @param {Object} o.platform    init.platform (the iOS format gate)
 * @param {Function} o.prewarm   the provider's bounded prewarm(urls)
 * @param {Function} o.log
 * @param {boolean} o.remoteAllowed  remote media gate (local rows ignore it)
 * @param {Function} [o.broken]  (url) => boolean, the provider's shared url
 *                               blacklist; a dead row is skipped at serve time
 * @returns {Promise<Object>} the pool (resolves per law 3, never rejects)
 */
export function createTaggedPool({ spec, channel, platform, prewarm, log, remoteAllowed, broken, verdict } = {}) {
  const s = spec || {};
  const say = typeof log === 'function' ? log : () => {};
  const sources = normalizeSources(s.sources);
  const perSourceMin = Math.max(1, Math.round(Number(s.perSourceMin) || TAGGED.PER_SOURCE_MIN));
  const timeoutMs = Math.max(0, Math.round(Number(s.timeoutMs) || TAGGED.TIMEOUT_MS));
  const seed = s.seed == null ? '' : String(s.seed);
  const want = {
    loop: Math.max(0, (s.want && s.want.loops) | 0),
    still: Math.max(0, (s.want && s.want.stills) | 0),
  };

  /* every tag named by a source, plus target/noise so counts() is total */
  const tagNames = [];
  for (const t of BASE_TAGS) tagNames.push(t);
  for (const src of sources) if (tagNames.indexOf(src.tag) < 0) tagNames.push(src.tag);

  const tags = Object.create(null);
  for (const name of tagNames) {
    tags[name] = {
      name,
      rows: [],                 // distinct, in ARRIVAL order
      order: [],                // the serve order of the current pass
      cursor: 0,
      served: 0,
      cycle: 0,                 // 0 = the first pass; 1+ = a seeded re-serve
      asks: 0,
      refills: 0,               // refill() rounds spent (each widens the ask budget)
      inFlight: 0,
      thin: false,              // frozen at resolve (law 4)
      /* Each tag owns its own mulberry32 stream, so adding a tag never shifts
       * another tag's shuffle (core/rng.js makeTaggedRoll's lesson). */
      rng: makeRng(seed + '|sort-tag|' + name),
    };
  }
  const seenUrls = new Set();       // law 1: one url, one tag, forever

  let resolved = false;
  let disposed = false;
  let resolveFn = null;
  const timers = new Set();
  const updateCbs = new Set();

  function later(fn, ms) {
    const t = setTimeout(() => { timers.delete(t); fn(); }, ms);
    timers.add(t);
    return t;
  }

  function clearTimers() {
    for (const t of timers) { try { clearTimeout(t); } catch { /* noop */ } }
    timers.clear();
  }

  const isDead = (url) => { try { return typeof broken === 'function' && !!broken(url); } catch (e) { return false; } };
  /** The vet's word (provider/vet.js verdict()): 'ok' is a row PROVED alive this page. */
  const isOk = (url) => { try { return typeof verdict === 'function' && verdict(url) === 'ok'; } catch (e) { return false; } };

  /* ---------------------- the ask ---------------------------------------- */
  /** How many distinct rows a tag is still worth asking for.
   *
   *  LET THE PILE GROW PAST THE SPEC (0826, the claim()-side twin). This is the
   *  BACKGROUND top-up gate only - `perSourceMin` is still the settle gate the
   *  door waits on, and raising THAT would just delay the door. What this
   *  bounds is how long the ask-again loop keeps pulling rows in after the
   *  class has already started, and stopping it at the deal spec is why a pile
   *  settled at exactly its ask and every later hand re-served the same faces.
   *  Both spec-shaped terms are doubled; TAG_CAP is still the ceiling. */
  function targetFor(tag) {
    const rows = sources.filter((x) => x.tag === tag.name).length || 1;
    const share = Math.ceil((want.loop + want.still) / Math.max(1, tagNames.length)) * 2;
    return Math.max(perSourceMin, Math.min(TAGGED.TAG_CAP, share || perSourceMin * 2, rows * 80));
  }

  /** The ask budget, widened by every refill() round the vet spent. */
  function askCap(tag) { return TAGGED.MAX_ASKS * (1 + (tag.refills | 0)); }
  /** Rows the blacklist has NOT convicted - the only rows worth counting when
   *  deciding whether to keep asking (a pile of 40 dead urls is an empty pile). */
  function liveRows(tag) {
    let n = 0;
    for (const r of tag.rows) if (!isDead(r.url)) n += 1;
    return n;
  }

  function ask(src, kind, attempt) {
    if (disposed) return;
    const tag = tags[src.tag];
    if (!tag) return;
    if (tag.asks >= askCap(tag)) return;
    if (src.kind === 'remote' && !remoteAllowed) return;
    const count = Math.max(4, Math.min(TAGGED.BATCH_MAX, Math.ceil(want[kind] / Math.max(1, sources.length)) || 8));
    const payload = { count, kind, tag: src.tag };
    if (src.kind === 'local') {
      if (src.folders.length) payload.folders = src.folders.slice();
      if (src.presetId) payload.presetId = src.presetId;
    } else {
      payload.subs = src.subs.slice();
    }
    tag.asks += 1;
    tag.inFlight += 1;
    const id = channel && channel.request(Object.assign({
      type: src.kind === 'local' ? 'local-sample-request' : 'assets-request',
      local: src.kind === 'local',      // a local sample is NOT gated on remote consent
      onBatch: (entries) => {
        if (disposed) return;
        const added = absorb(entries, src);
        tag.inFlight = Math.max(0, tag.inFlight - 1);
        if (added) notify();
        /* THE HOST CONTRACT IS "ASK AGAIN AFTER EVERY REPLY" - a cold buffer
         * answers empty and streams the real batch later, so one ask per source
         * would deal a class off nothing. Bounded by the tag's ask budget. */
        if (liveRows(tag) < targetFor(tag) && tag.asks < askCap(tag)) {
          later(() => ask(src, kind, attempt + 1), TAGGED.RETRY_MS * Math.max(1, attempt));
        }
        settleCheck();
      },
    }, payload));
    if (!id) { tag.asks = Math.max(0, tag.asks - 1); tag.inFlight = Math.max(0, tag.inFlight - 1); }
  }

  /* ---------------------- absorbing a batch ------------------------------ */
  function absorb(entries, src) {
    let added = 0;
    for (const e of (Array.isArray(entries) ? entries : [])) {
      if (!e) continue;
      const url = typeof e === 'string' ? e : e.url;
      if (!url || typeof url !== 'string') continue;
      if (!formatOk(url, platform)) continue;                 // iOS mp4-only
      const rowTag = (e && typeof e.tag === 'string' && tags[e.tag]) ? e.tag : src.tag;
      const tag = tags[rowTag];
      if (!tag) continue;
      if (seenUrls.has(url)) continue;                        // law 1
      if (tag.rows.length >= TAGGED.TAG_CAP) continue;
      seenUrls.add(url);
      const row = Object.freeze({
        url,
        remote: !isLocalUrl(url),
        kind: rowKind(e),
        mime: String((e && e.mime) || ''),
        /* THE CLIP'S OWN STILL (0827): the host sends a remote loop's poster so a
         * card can paint it while the clip buffers, or over the decoder ceiling. */
        poster: (e && typeof e.poster === 'string' && e.poster && !isLocalUrl(url)) ? e.poster : '',
        tag: rowTag,
        src: (e && typeof e.src === 'string' && e.src) ? e.src : sourceLabel(src),
      });
      tag.rows.push(row);
      /* A late batch joins the pass already in progress rather than waiting for
       * the next one - a card that arrived is a card the player can be dealt. */
      tag.order.push(row);
      added += 1;
    }
    if (added) say('tagged +' + added + ' (' + summary() + ')');
    return added;
  }

  function summary() {
    return tagNames.map((n) => n + ' ' + tags[n].rows.length).join(' / ');
  }

  function notify() {
    for (const fn of [...updateCbs]) { try { fn(); } catch { /* a bad listener never kills a pool */ } }
  }

  /* ---------------------- resolve ---------------------------------------- */
  function everyTagReady() {
    const used = tagNames.filter((n) => sources.some((x) => x.tag === n));
    if (!used.length) return true;                            // nothing to wait for
    return used.every((n) => tags[n].rows.length >= perSourceMin);
  }

  function budgetSpent() {
    const used = tagNames.filter((n) => sources.some((x) => x.tag === n));
    if (!used.length) return true;
    return used.every((n) => tags[n].asks >= askCap(tags[n]) && tags[n].inFlight === 0);
  }

  function settleCheck() {
    if (resolved || disposed) return;
    if (everyTagReady() || budgetSpent()) settle(everyTagReady() ? 'ready' : 'budget');
  }

  function settle(reason) {
    if (resolved) return;
    resolved = true;
    for (const n of tagNames) tags[n].thin = tags[n].rows.length < perSourceMin;   // law 4
    say('tagged pool ready [' + reason + '] ' + summary());
    if (resolveFn) { const f = resolveFn; resolveFn = null; f(pool); }
  }

  /* ---------------------- serving ---------------------------------------- */
  /** Start the next pass: pass 0 is arrival order, every later one is a SEEDED
   *  shuffle of the rows this tag has already served (law 2). */
  function reserve(tag) {
    tag.cycle += 1;
    tag.order = shuffled(tag.rows, tag.rng);
    tag.cursor = 0;
  }

  function nextRow(tagName, opts) {
    const tag = tags[tagName];
    if (!tag || !tag.rows.length) return null;
    const prefer = opts && (opts.prefer === 'loop' || opts.prefer === 'still') ? opts.prefer : null;
    /* A BLACKLISTED URL IS SKIPPED and the SEEDED RE-SERVE is the substitute
     * source: the cursor just walks on (starting the next seeded pass when it
     * runs off the end), so the replacement for a dead row is the same row a
     * dry tag would have re-served anyway - deterministic for a given seed and
     * blacklist state, and with a clean blacklist this loop runs exactly once,
     * serving the identical sequence it always served. Bounded at two full
     * passes: a tag whose EVERY url died still answers (law 2 - null means
     * zero rows, nothing else), and the game's own floor takes it from there. */
    const cap = Math.max(1, tag.rows.length * 2);
    let row = null;
    for (let attempt = 0; attempt <= cap; attempt++) {
      if (tag.cursor >= tag.order.length) reserve(tag);
      if (!tag.order.length) return null;
      if (attempt === 0) {
        /* PREFERENCE IS A SWAP, NOT A SKIP: the wanted row is pulled forward
         * into the cursor's slot and the row it displaced keeps its place in
         * the pass, so a preference can never starve a kind out of the deck.
         * Two preferences, in order: A JUDGED-ALIVE ROW FIRST (0827 - the
         * vet's 'ok'; a row the vet never reached is dealt only once the
         * judged ones left in this pass are spent, a dead one never), then
         * the wanted kind. With no verdicts in hand this is the old kind-only
         * swap, and with a clean blacklist and no vet the sequence is the one
         * this pool always served. */
        let best = -1;
        let bestScore = 9;
        for (let i = tag.cursor; i < tag.order.length; i++) {
          const r = tag.order[i];
          if (!r || isDead(r.url)) continue;
          const score = (isOk(r.url) ? 0 : 2) + ((prefer && r.kind !== prefer) ? 1 : 0);
          if (score < bestScore) { best = i; bestScore = score; }
          if (score === 0) break;
        }
        if (best > tag.cursor) {
          const t = tag.order[best]; tag.order[best] = tag.order[tag.cursor]; tag.order[tag.cursor] = t;
        }
      }
      const cand = tag.order[tag.cursor];
      tag.cursor += 1;
      if (cand && isDead(cand.url) && attempt < cap) continue;
      row = cand || null;
      break;
    }
    if (!row) return null;
    tag.served += 1;
    return row;
  }

  /* ---------------------- the pool object -------------------------------- */
  const pool = {
    /** row | null - null ONLY when this tag has zero rows (law 2). */
    next(tag, opts) { return disposed ? null : nextRow(String(tag || ''), opts); },
    /** The poster this pool holds for a url ('' when none): a retake re-deals
     *  a cached row list that carries no poster (deck.js rowsFromCards keeps
     *  the meta blob small), and asks the live pool for it instead. */
    posterOf(url) {
      const u = String(url || '');
      if (!u) return '';
      for (const n of tagNames) {
        for (const r of tags[n].rows) if (r.url === u) return r.poster || '';
      }
      return '';
    },

    counts() {
      const out = Object.create(null);
      for (const n of tagNames) out[n] = { distinct: tags[n].rows.length, served: tags[n].served };
      return out;
    },

    /** Frozen at resolve: what the door warned about cannot move mid-class. */
    thin(tag) { const x = tags[String(tag || '')]; return !!(x && x.thin); },

    /** LIVE: a late batch may legitimately lift an empty tag. */
    empty(tag) { const x = tags[String(tag || '')]; return !x || x.rows.length === 0; },

    /** Decode-ahead, n per tag, hard-capped at TAGGED.PREWARM_CAP per tag. */
    prewarm(n) {
      if (disposed || typeof prewarm !== 'function') return 0;
      const many = Math.max(0, Math.min(TAGGED.PREWARM_CAP, Math.round(Number(n) || 0)));
      if (!many) return 0;
      let total = 0;
      for (const name of tagNames) {
        const tag = tags[name];
        if (!tag.rows.length) continue;
        const urls = [];
        for (let i = 0; i < many; i++) {
          const row = tag.order[(tag.cursor + i) % tag.order.length];
          if (row && urls.indexOf(row.url) < 0) urls.push(row.url);
        }
        try { prewarm(urls); } catch { /* a warm-up never breaks a claim */ }
        total += urls.length;
      }
      return total;
    },

    /** Every distinct row the pool holds for ONE tag, in ARRIVAL order (stable
     *  for a given claim, so a pure-hash pick over it repeats on a retake).
     *  The substitute widener's window onto media the deck froze out: the pool
     *  keeps filling to targetFor() after the deal, and those late rows never
     *  reach a frozen deck any other way. */
    spare(tag) {
      const x = tags[String(tag || '')];
      return x ? x.rows.slice() : [];
    },

    /**
     * THE TOP-UP (0827, the vet's road back to the host). When the vet has
     * convicted enough of a tag's rows that fewer than perSourceMin are alive,
     * the door asks for MORE before it deals: one fresh MAX_ASKS budget for
     * every source of the tag, the same ask() chain the claim ran (so RANDOM
     * sort on the host's side hands back posts it has not served yet), and a
     * promise that answers with how many distinct rows landed - on the first
     * batch that grows the tag (plus a short settle so a second reply in the
     * same breath is counted too), or 0 at REFILL_MS. Bounded by REFILL_MAX
     * rounds per tag for the life of the pool. `thin` stays frozen (law 4).
     */
    refill(tagName) {
      const tag = tags[String(tagName || '')];
      if (!tag || disposed || !resolved) return Promise.resolve(0);
      if (tag.refills >= TAGGED.REFILL_MAX) return Promise.resolve(0);
      const srcs = sources.filter((x) => x.tag === tag.name && (x.kind === 'local' || remoteAllowed));
      if (!srcs.length) return Promise.resolve(0);
      tag.refills += 1;
      const before = tag.rows.length;
      for (const src of srcs) {
        if (want.loop || (!want.loop && !want.still)) ask(src, 'loop', 1);
        if (want.still) ask(src, 'still', 1);
      }
      if (!tag.inFlight) return Promise.resolve(0);          // nothing could be asked
      say('refill ' + tag.name + ' (round ' + tag.refills + ')');
      return new Promise((res) => {
        let done = false;
        let off = () => {};
        const fin = () => {
          if (done) return;
          done = true;
          try { off(); } catch { /* noop */ }
          res(Math.max(0, tag.rows.length - before));
        };
        off = pool.onUpdate(() => { if (tag.rows.length > before) later(fin, 400); });
        later(fin, TAGGED.REFILL_MS);
      });
    },

    /** Every DISTINCT row, for the retake cache (a superset of {url,tag,src}). */
    dealt() {
      const out = [];
      for (const n of tagNames) for (const r of tags[n].rows) {
        out.push({ url: r.url, tag: r.tag, src: r.src, kind: r.kind, mime: r.mime, remote: r.remote });
      }
      return out;
    },

    /** "the pool grew" - a late batch landed. Returns an unsubscribe. */
    onUpdate(fn) {
      if (typeof fn !== 'function' || disposed) return () => {};
      updateCbs.add(fn);
      return () => updateCbs.delete(fn);
    },

    /** Diagnostics, for the door's thin warnings and the suite. */
    stats() {
      return {
        perSourceMin, seed, resolved, disposed,
        sources: sources.map((x) => ({ tag: x.tag, kind: x.kind, label: sourceLabel(x) })),
        tags: tagNames.map((n) => ({
          tag: n, distinct: tags[n].rows.length, served: tags[n].served,
          cycle: tags[n].cycle, asks: tags[n].asks, refills: tags[n].refills, thin: tags[n].thin,
        })),
      };
    },

    dispose() {
      if (disposed) return;
      disposed = true;
      clearTimers();
      updateCbs.clear();
      /* A pool that is disposed before it resolved still hands its caller a
       * pool rather than a pending promise nobody will ever settle. */
      if (!resolved) settle('disposed');
    },
  };

  /* ---------------------- kick off --------------------------------------- */
  const promise = new Promise((res) => { resolveFn = res; });
  if (!sources.length) {
    say('claimTagged with no usable sources - every tag is empty');
    settle('no-sources');
  } else {
    for (const src of sources) {
      if (want.loop || (!want.loop && !want.still)) ask(src, 'loop', 1);
      if (want.still) ask(src, 'still', 1);
    }
    /* Law 3's third bound. A host that never answers must not hold the door. */
    later(() => settle('timeout'), timeoutMs);
    settleCheck();
  }

  return resolved ? Promise.resolve(pool) : promise;
}

export default createTaggedPool;
