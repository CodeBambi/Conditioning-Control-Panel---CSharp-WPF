// The hash blocklist client (spec §7.4) — "has a moderator banned this artifact?".
//
// TWO GATES, AT TWO DIFFERENT MOMENTS, FOR TWO DIFFERENT REASONS. Both matter and
// they are deliberately not the same check:
//
//   at xfer_offer   answered ONLY from the local Map. An unknown hash is ACCEPTED
//                   and enqueued. Bytes on disk are cheap and harmless; making the
//                   offer gate wait on a network round trip would put latency inside
//                   the protocol's tightest path AND hand a sender a timing oracle
//                   for "is my file blocklisted".
//   at render time  exec/media.js acquireByTag / drawFor. THIS is the gate that
//                   matters, because it is the one that puts pixels on a screen.
//                   Blocked -> null -> the renderer falls back to the receiver's own
//                   library, exactly as if the transfer had never landed, and the
//                   store drops the file (goon-recv-drop) via the onBlocked hook.
//
// FAILS OPEN, ON PURPOSE. A proxy outage must not silently kill the feature for
// everyone, and the sender is someone the receiver already consented to. A 5xx or a
// dead host marks the batch "not blocked, but ask again in 5 minutes" — a soft
// verdict that answers the hot path immediately and re-queues itself later.
//
// NODE-IMPORT-SAFE: `post` is injected (bridge.postNet in the page), so nothing here
// touches fetch, window or the bridge at module scope.

/** Debounce window before a partial batch goes out. */
export const BLOCKLIST_BATCH_MS = 400;
/** …or flush immediately once this many are pending. */
export const BLOCKLIST_FLUSH_AT = 32;
/** The server's per-call cap (BLOCKED_QUERY_MAX). */
export const BLOCKLIST_MAX_PER_POST = 64;
/** How long a fail-open verdict stands before it is worth asking again. */
export const BLOCKLIST_RETRY_MS = 5 * 60 * 1000;

export const BLOCKLIST_PATH = '/v2/goon/blocked';

const SHA_RE = /^[0-9a-f]{64}$/;

/**
 * @param {object} o
 * @param {(path:string, body:object) => Promise<{status:number, body:string}>} o.post
 *   bridge.postNet. NEVER rejects by contract — status 0 means "no answer".
 * @param {(() => string)|string} [o.unifiedId] the identity the server rate-limits on,
 *   read the way net/signaling.js reads it (boot's session.identity.unifiedId).
 * @param {object} [o.logger] console-shaped
 * @param {number} [o.batchMs] TEST AFFORDANCE — production passes nothing
 * @param {number} [o.flushAt] TEST AFFORDANCE
 * @param {number} [o.retryMs] TEST AFFORDANCE
 */
export function createBlocklist({ post, unifiedId = '', logger = null, batchMs, flushAt, retryMs } = {}) {
  const postFn = typeof post === 'function' ? post : null;
  const idOf = typeof unifiedId === 'function' ? unifiedId : (() => String(unifiedId || ''));
  const BATCH_MS = Number.isFinite(batchMs) ? batchMs : BLOCKLIST_BATCH_MS;
  const FLUSH_AT = Number.isFinite(flushAt) ? flushAt : BLOCKLIST_FLUSH_AT;
  const RETRY_MS = Number.isFinite(retryMs) ? retryMs : BLOCKLIST_RETRY_MS;

  const info = (m) => { try { logger?.info?.('[GG blocked] ' + m); } catch (_e) { /* ignore */ } };
  const warn = (m) => { try { logger?.warn?.('[GG blocked] ' + m); } catch (_e) { /* ignore */ } };

  /** sha -> {blocked:boolean, at:number, soft:boolean} */
  const verdicts = new Map();
  /** shas waiting for a batch, in insertion order. */
  const pending = new Set();
  /** shas already in flight, so a second check() does not double-post them. */
  const inFlight = new Set();
  const blockedSubs = new Set();

  let timer = 0;
  let posts = 0;
  let failures = 0;
  let failLogged = false;
  let disposed = false;

  const now = () => Date.now();

  /** A soft verdict is a real answer for the hot path AND a note to ask again. */
  function stale(v) { return !!(v && v.soft && (now() - v.at) > RETRY_MS); }

  function record(sha, blocked, soft) {
    const was = verdicts.get(sha);
    verdicts.set(sha, { blocked: !!blocked, at: now(), soft: !!soft });
    if (blocked && !(was && was.blocked)) {
      for (const fn of Array.from(blockedSubs)) {
        try { fn(sha); } catch (e) { warn('onBlocked handler threw: ' + ((e && e.message) || e)); }
      }
    }
  }

  function arm() {
    if (disposed || timer || !pending.size) return;
    try {
      timer = setTimeout(() => { timer = 0; void flush(); }, BATCH_MS);
      if (timer && typeof timer.unref === 'function') timer.unref();
    } catch (_e) { void flush(); }        // no timers: go now rather than never
  }

  /** Enqueue whatever we have no usable verdict for. Never returns a promise. */
  function enqueue(shas) {
    let added = 0;
    for (const raw of (Array.isArray(shas) ? shas : [shas])) {
      const sha = typeof raw === 'string' ? raw : '';
      if (!SHA_RE.test(sha)) continue;
      if (inFlight.has(sha) || pending.has(sha)) continue;
      const v = verdicts.get(sha);
      if (v && !stale(v)) continue;       // known (or freshly failed-open) — nothing to ask
      pending.add(sha);
      added++;
    }
    if (!added) return 0;
    if (pending.size >= FLUSH_AT) {
      if (timer) { try { clearTimeout(timer); } catch (_e) { /* ignore */ } timer = 0; }
      void flush();
    } else {
      arm();
    }
    return added;
  }

  /**
   * Post one batch (<= BLOCKLIST_MAX_PER_POST) and drain the rest behind it.
   * @returns {Promise<number>} how many hashes came back blocked.
   */
  async function flush() {
    if (disposed || !pending.size) return 0;
    if (!postFn) {
      // No transport at all (a test harness, a host with no net). Fail OPEN and
      // keep the retry stamp so a later wiring still gets its answer.
      const batch = Array.from(pending);
      pending.clear();
      for (const sha of batch) record(sha, false, true);
      return 0;
    }

    const batch = Array.from(pending).slice(0, BLOCKLIST_MAX_PER_POST);
    for (const sha of batch) { pending.delete(sha); inFlight.add(sha); }

    let res = null;
    posts++;
    try {
      res = await postFn(BLOCKLIST_PATH, { unified_id: idOf(), hashes: batch });
    } catch (e) {
      // postNet never rejects, but a stubbed one might.
      res = null;
      warn('post threw: ' + ((e && e.message) || e));
    }

    const status = res ? (res.status | 0) : 0;
    let parsed = null;
    if (status >= 200 && status < 300) {
      try { parsed = JSON.parse(res.body || '{}'); } catch (_e) { parsed = null; }
    }

    if (!parsed || parsed.ok === false || !Array.isArray(parsed.blocked)) {
      // FAIL OPEN. Logged ONCE — a dead proxy would otherwise write a line per batch
      // for a whole match, and the first one already said everything.
      failures++;
      if (!failLogged) {
        failLogged = true;
        warn(`lookup failed (status ${status}) — failing OPEN; rechecking in ${Math.round(RETRY_MS / 60000)} min`);
      }
      for (const sha of batch) { inFlight.delete(sha); record(sha, false, true); }
      if (pending.size) arm();
      return 0;
    }

    failLogged = false;
    const hits = new Set(parsed.blocked.filter((s) => typeof s === 'string' && SHA_RE.test(s)));
    for (const sha of batch) { inFlight.delete(sha); record(sha, hits.has(sha), false); }
    if (hits.size) info(`${hits.size} of ${batch.length} hashes are blocklisted`);
    if (pending.size) arm();
    return hits.size;
  }

  return {
    /**
     * The offer gate's question: do we ALREADY know? Unknown is not blocked — see
     * the header. A fail-open verdict counts as known (it answers "not blocked")
     * right up until its retry stamp expires.
     */
    knows(sha) {
      const v = verdicts.get(sha);
      return !!v && !stale(v);
    },

    isBlocked(sha) {
      const v = verdicts.get(sha);
      return !!(v && v.blocked);
    },

    /** Enqueue anything unknown. Fire-and-forget; the batch resolves itself. */
    check(shas) { return enqueue(shas); },

    /**
     * Boot prefetch: everything already in the received store, batched, once.
     * Same path as check() — named separately because the CALL SITE is the point
     * (the whole inbox is asked about before any screen mounts).
     */
    prime(shas) {
      const added = enqueue(shas);
      if (added) info(`prefetching ${added} hash(es) from the received inbox`);
      return added;
    },

    /** Send whatever is pending right now. Returns the number of new blocks found. */
    flush,

    /** @returns {() => void} unsubscribe. Fires once per sha the moment it turns blocked. */
    onBlocked(fn) {
      if (typeof fn !== 'function') return () => {};
      blockedSubs.add(fn);
      return () => blockedSubs.delete(fn);
    },

    stats() {
      let blocked = 0, soft = 0;
      for (const v of verdicts.values()) { if (v.blocked) blocked++; if (v.soft) soft++; }
      return {
        known: verdicts.size, blocked, soft,
        pending: pending.size, inFlight: inFlight.size,
        posts, failures,
      };
    },

    dispose() {
      disposed = true;
      if (timer) { try { clearTimeout(timer); } catch (_e) { /* ignore */ } timer = 0; }
      pending.clear();
      inFlight.clear();
      blockedSubs.clear();
    },
  };
}

export default createBlocklist;
