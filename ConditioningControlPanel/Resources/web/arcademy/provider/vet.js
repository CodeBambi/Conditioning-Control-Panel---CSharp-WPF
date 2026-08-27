/**
 * provider/vet.js - THE VET (0827). Proof that a url is ALIVE, before a deck
 * is dealt off it.
 *
 * WHY THIS EXISTS. Scrolller's index keeps serving posts whose CDN file is
 * gone: a live probe (2026-08-27) found 7 of 30 r/aww loops and 8 of 30 stills
 * answering 404, and every one of them reached the page as a perfectly
 * well-formed row - the host validates FORMAT, never liveness, and the page
 * trusts the row. The card was dealt, its face fired `error`, and the player
 * sat on the striped back. The blacklist + substitute layer (index.js
 * markBrokenUrl / sort's substituteFor) only ever learned a url was dead AFTER
 * it had been shown, and BROKEN_TTL_MS forgave the first strike 45s later, so
 * the same dead card came back next pass. The owner's word: "pre-fetch the
 * images and only start the game when we got enough".
 *
 * WHAT IT DOES. vet(rows) probes every remote url ONCE per page and answers
 * with a verdict per url:
 *   - a STILL / GIF rides a detached `new Image()`: `load` with pixels is
 *     alive (and the bytes + decode now sit in the browser's image cache, so
 *     the vet doubles as the warm), `error` is DEAD. The media CDN sends no
 *     CORS headers, so this - not fetch() - is the only status a page can read.
 *   - a VIDEO rides a detached `<video preload="metadata" muted>`:
 *     `loadedmetadata` is alive, `error` with MEDIA_ERR_NETWORK (2) or
 *     MEDIA_ERR_SRC_NOT_SUPPORTED (4) is dead (a 404 lands as 4), any other
 *     code is UNSURE. This is the ONE sanctioned detached <video> in the
 *     school (trap 36 forbids them because a demuxer no ceiling counts is a
 *     third decoder off screen): it runs ONLY while the door is up - nothing
 *     is minted yet - it holds at most VIDEO_LANES (= DECODER_CEILING) at a
 *     time, its src is torn down the instant a verdict lands, and settle()
 *     ABORTS every video probe still in flight the moment the gate opens, so
 *     the class never shares a decoder with the vet.
 *   - a probe that answers nothing inside PROBE_MS is UNSURE: a slow CDN is
 *     not a 404, and the ordinary face-error road (faceDied) is still there
 *     for it. Unsure is never a conviction and never counts as alive.
 *   - a LOCAL / same-origin / data: / blob: url is alive by definition (the
 *     host serving the player's own disk) and is never probed.
 *
 * A DEAD verdict is PROOF, so it goes to the blacklist as a PERMANENT strike
 * (markBroken(url, true)) - the 45s TTL is for guesses, and a 404 is not one.
 *
 * THE GATE. vet() resolves when EVERY row has a verdict, or when `enough(stats)`
 * says the caller can deal (SORT: N alive per tag), or at `maxMs` - whichever
 * lands first. The IMAGE probes still queued keep running in the background
 * after an early resolve (they cost no decoder, and every verdict they land
 * feeds the same blacklist the deal reads); the video probes do not (above).
 * Never rejects: a vet that could throw would be a class that could not start.
 *
 * INERT where it cannot see: a node harness (no `document`, no `Image`) gets
 * every url back as alive at once - the suites see no new asynchrony.
 */

export const VET = Object.freeze({
  IMAGE_LANES: 4,       // detached <img> probes in flight (a cache warm each)
  VIDEO_LANES: 2,       // detached <video> probes in flight = DECODER_CEILING
  PROBE_MS: 8000,       // a probe with no answer by then is UNSURE, not dead
  MAX_MS: 20000,        // the gate's hard ceiling, whatever is still pending
});

const VIDEO_URL_RE = /\.(mp4|webm|m4v|mov)(\?|#|$)/i;

/** One verdict per url per PAGE: 'ok' | 'dead' | 'unsure'. A second press of
 *  PLAY, a retake, another class - none of them re-probe a url this page has
 *  already judged (an unsure one IS re-probed: it was a timeout, not a verdict). */
const verdicts = new Map();

/**
 * @param {Object} o
 * @param {Function} o.markBroken (url, permanent) -> the provider's blacklist verb
 * @param {Function} o.isBroken   (url) -> boolean
 * @param {Function} o.isLocal    (url) -> boolean (inventory.js isLocalUrl)
 * @param {Function} [o.log]
 */
export function createVetter(o = {}) {
  const say = typeof o.log === 'function' ? o.log : () => {};
  const markBroken = typeof o.markBroken === 'function' ? o.markBroken : () => {};
  const isBroken = typeof o.isBroken === 'function' ? o.isBroken : () => false;
  const isLocal = typeof o.isLocal === 'function' ? o.isLocal : () => false;

  const canProbe = (() => {
    try {
      return typeof document !== 'undefined' && typeof document.createElement === 'function'
        && typeof Image === 'function';
    } catch (e) { return false; }
  })();

  /** Alive without asking: the host's own disk, our origin, inline data. */
  function instant(url) {
    const s = String(url || '');
    if (!s) return false;
    try { if (isLocal(s)) return true; } catch (e) { /* fall through */ }
    if (!/^https?:\/\//i.test(s)) return true;
    try {
      if (typeof location !== 'undefined' && location.origin && new URL(s).origin === location.origin) return true;
    } catch (e) { /* an unparsable url is not instant */ }
    return false;
  }

  function isVideo(row) {
    if (row && typeof row.mime === 'string' && /^video\//i.test(row.mime)) return true;
    return VIDEO_URL_RE.test(String((row && row.url) || ''));
  }

  /* ---------------------------------------------------------- one probe -- */
  /** @returns {{ promise: Promise<string>, abort: Function }} */
  function probeImage(url) {
    let img = null;
    let timer = 0;
    let settled = false;
    let resolveFn = null;
    const promise = new Promise((res) => { resolveFn = res; });
    const finish = (v) => {
      if (settled) return;
      settled = true;
      try { clearTimeout(timer); } catch (e) { /* noop */ }
      try { if (img) { img.onload = null; img.onerror = null; } } catch (e) { /* noop */ }
      resolveFn(v);
    };
    try {
      img = new Image();
      img.decoding = 'async';
      try { img.fetchPriority = 'low'; } catch (e) { /* older engines */ }
      img.onload = () => finish(Number(img.naturalWidth) > 0 ? 'ok' : 'dead');
      img.onerror = () => finish('dead');
      timer = setTimeout(() => {
        /* a hung request is let go of, not condemned */
        try { img.src = ''; } catch (e) { /* noop */ }
        finish('unsure');
      }, VET.PROBE_MS);
      img.src = url;
      /* an engine that already had it answers synchronously */
      if (img.complete && Number(img.naturalWidth) > 0) finish('ok');
    } catch (e) { finish('unsure'); }
    return { promise, abort: () => { try { if (img) img.src = ''; } catch (e) { /* noop */ } finish('unsure'); } };
  }

  function probeVideo(url) {
    let v = null;
    let timer = 0;
    let settled = false;
    let resolveFn = null;
    const promise = new Promise((res) => { resolveFn = res; });
    const teardown = () => {
      if (!v) return;
      try { v._aeDying = true; } catch (e) { /* noop */ }
      try { if (v.pause) v.pause(); } catch (e) { /* noop */ }
      try { v.removeAttribute('src'); if (v.load) v.load(); } catch (e) { /* noop */ }
      v = null;
    };
    const finish = (verdict) => {
      if (settled) return;
      settled = true;
      try { clearTimeout(timer); } catch (e) { /* noop */ }
      teardown();
      resolveFn(verdict);
    };
    try {
      v = document.createElement('video');
      if (!v || typeof v.addEventListener !== 'function') { finish('unsure'); return { promise, abort: () => finish('unsure') }; }
      v.muted = true; v.playsInline = true; v.autoplay = false;
      try {
        v.setAttribute('muted', ''); v.setAttribute('playsinline', '');
        v.setAttribute('preload', 'metadata'); v.setAttribute('disablepictureinpicture', '');
      } catch (e) { /* noop */ }
      try { v.disableRemotePlayback = true; } catch (e) { /* not everywhere */ }
      v.addEventListener('loadedmetadata', () => finish('ok'), { once: true });
      v.addEventListener('error', () => {
        const code = v && v.error && v.error.code;
        finish(code === 2 || code === 4 ? 'dead' : 'unsure');
      }, { once: true });
      timer = setTimeout(() => finish('unsure'), VET.PROBE_MS);
      v.src = url;
      try { if (typeof v.load === 'function') v.load(); } catch (e) { /* noop */ }
    } catch (e) { finish('unsure'); }
    return { promise, abort: () => finish('unsure') };
  }

  /* ---------------------------------------------------------- the vet ---- */
  /**
   * vet(rows, opts) -> Promise<stats> (+ .cancel()).
   *   rows        [{url, tag?, mime?, kind?}] (or bare url strings)
   *   opts.enough (stats) -> boolean, asked after every verdict; true opens the gate
   *   opts.onProgress (stats)
   *   opts.maxMs  the gate's ceiling (VET.MAX_MS)
   * stats: { total, done, ok, dead, unsure, byTag: {tag: {ok, dead, unsure, total}},
   *          complete, elapsed, urls: {ok: [], dead: [], unsure: []} }
   */
  function vet(rows, opts) {
    const o = opts || {};
    const enough = typeof o.enough === 'function' ? o.enough : null;
    const onProgress = typeof o.onProgress === 'function' ? o.onProgress : null;
    const maxMs = o.maxMs == null ? VET.MAX_MS : Math.max(0, Number(o.maxMs) || 0);
    const startedAt = Date.now();

    /* distinct urls, first row's tag/mime wins */
    const jobs = [];
    const seen = new Set();
    for (const r of (Array.isArray(rows) ? rows : [])) {
      const url = typeof r === 'string' ? r : (r && r.url);
      if (!url || typeof url !== 'string' || seen.has(url)) continue;
      seen.add(url);
      const row = typeof r === 'string' ? { url } : r;
      jobs.push({ url, tag: String(row.tag || ''), video: isVideo(row), verdict: '' });
    }

    const stats = {
      total: jobs.length, done: 0, ok: 0, dead: 0, unsure: 0,
      byTag: Object.create(null), complete: jobs.length === 0, elapsed: 0,
      urls: { ok: [], dead: [], unsure: [] },
    };
    const tagBox = (tag) => {
      const k = tag || '';
      if (!stats.byTag[k]) stats.byTag[k] = { ok: 0, dead: 0, unsure: 0, total: 0 };
      return stats.byTag[k];
    };
    for (const j of jobs) tagBox(j.tag).total += 1;

    let opened = false;          // the gate has resolved (early or complete)
    let cancelled = false;
    let resolveFn = null;
    const promise = new Promise((res) => { resolveFn = res; });
    let gateTimer = 0;
    const inflight = new Set();  // {job, abort}
    const queue = [];
    let imgFlight = 0;
    let vidFlight = 0;

    function record(job, verdict) {
      if (job.verdict) return;
      job.verdict = verdict;
      stats.done += 1;
      stats[verdict] += 1;
      tagBox(job.tag)[verdict] += 1;
      stats.urls[verdict].push(job.url);
      if (verdict !== 'unsure') verdicts.set(job.url, verdict);
      if (verdict === 'dead') { try { markBroken(job.url, true); } catch (e) { /* noop */ } }
      stats.elapsed = Date.now() - startedAt;
      stats.complete = stats.done >= stats.total;
    }

    function open(reason) {
      if (opened) return;
      opened = true;
      try { clearTimeout(gateTimer); } catch (e) { /* noop */ }
      stats.elapsed = Date.now() - startedAt;
      /* the class is about to mint: no vet may hold a decoder past this line.
       * Queued videos are let go of unjudged (unsure), in-flight ones aborted. */
      for (let i = queue.length - 1; i >= 0; i--) {
        if (queue[i].video) { record(queue[i], 'unsure'); queue.splice(i, 1); }
      }
      for (const f of [...inflight]) if (f.job.video) { try { f.abort(); } catch (e) { /* noop */ } }
      say('vet ' + reason + ': ' + stats.ok + ' ok / ' + stats.dead + ' dead / ' + stats.unsure
        + ' unsure of ' + stats.total + ' in ' + stats.elapsed + 'ms'
        + (stats.complete ? '' : ' (' + (stats.total - stats.done) + ' still probing)'));
      resolveFn(stats);
    }

    function progress() {
      if (onProgress) { try { onProgress(stats); } catch (e) { /* a bad listener never stops the vet */ } }
      if (opened) return;
      if (stats.complete) { open('complete'); return; }
      /* asked after EVERY verdict, never throttled: the last verdict before a
       * hung tail is the one that opens the gate, and a skipped check there
       * would park the door on the ceiling */
      if (enough) {
        let yes = false;
        try { yes = !!enough(stats); } catch (e) { yes = false; }
        if (yes) open('enough');
      }
    }

    function pump() {
      if (cancelled) return;
      let moved = false;
      for (let i = 0; i < queue.length;) {
        const job = queue[i];
        if (job.video) {
          if (opened || vidFlight >= VET.VIDEO_LANES) { i += 1; continue; }
          vidFlight += 1;
        } else {
          if (imgFlight >= VET.IMAGE_LANES) { i += 1; continue; }
          imgFlight += 1;
        }
        queue.splice(i, 1);
        moved = true;
        const probe = job.video ? probeVideo(job.url) : probeImage(job.url);
        const flight = { job, abort: probe.abort };
        inflight.add(flight);
        probe.promise.then((verdict) => {
          inflight.delete(flight);
          if (job.video) vidFlight = Math.max(0, vidFlight - 1); else imgFlight = Math.max(0, imgFlight - 1);
          record(job, verdict || 'unsure');
          progress();
          pump();
        });
      }
      if (!moved && !inflight.size && queue.length && !stats.complete) {
        /* only videos queued and the gate is open: nothing will ever run them */
        for (const job of queue.splice(0, queue.length)) record(job, 'unsure');
        progress();
      }
    }

    /* the instant answers first, then the probes */
    for (const job of jobs) {
      if (instant(job.url)) { record(job, 'ok'); continue; }
      let dead = false;
      try { dead = !!isBroken(job.url); } catch (e) { dead = false; }
      if (dead) { record(job, 'dead'); continue; }
      const known = verdicts.get(job.url);
      if (known === 'ok' || known === 'dead') { record(job, known); continue; }
      if (!canProbe) { record(job, 'ok'); continue; }        // inert: a harness sees no network
      queue.push(job);
    }
    if (maxMs > 0 && !stats.complete) gateTimer = setTimeout(() => open('ceiling'), maxMs);
    /* progress() first: a vet with nothing to probe resolves on this tick */
    Promise.resolve().then(() => { if (!cancelled) { progress(); pump(); } });

    promise.cancel = () => {
      if (cancelled) return;
      cancelled = true;
      for (let i = queue.length - 1; i >= 0; i--) { record(queue[i], 'unsure'); }
      queue.length = 0;
      for (const f of [...inflight]) { try { f.abort(); } catch (e) { /* noop */ } }
      open('cancelled');
    };
    return promise;
  }

  return {
    vet,
    /** What this page already knows about a url: 'ok' | 'dead' | '' */
    verdict(url) { return verdicts.get(String(url || '')) || ''; },
    diagnostics() {
      let ok = 0, dead = 0;
      for (const v of verdicts.values()) { if (v === 'ok') ok += 1; else if (v === 'dead') dead += 1; }
      return { ok, dead, canProbe };
    },
  };
}

export default createVetter;
