/* ============================================================================
 * mediaLog.js — THE RUN'S PAYLOADS, kept so the archive can show them back.
 *
 * The host hands the page a small sample of the user's own flash assets
 * (BootConfig.media — contracts.js MediaManifest). render/effects.js and
 * render/beats.js draw from that pool at spawn time and then forget: nothing
 * anywhere recorded WHICH images a run actually paid you with.
 *
 * This is that ledger. It is the exact sibling of core/spiralLog.js — same
 * shape, same rules: a plain in-memory module-global, reset by boot.js at the
 * top of every run, read once at run end (stats.record) and never persisted
 * here. Recording is a single call at each pick site and MUST never throw:
 * noteMedia() returns the url it was given, so a call site can wrap its pick
 * inline without changing any control flow.
 *
 *   const url = noteMedia(pickOf(gifs), 'gif');
 *
 * Order is ARRIVAL order (first time each url was shown); repeats only bump a
 * count. Capped at MAX_NOTED distinct urls so a long endless lap cannot grow
 * the record without bound.
 * ==========================================================================*/

/** Distinct urls kept per run. The host samples ~10 gifs + ~10 stills, so this
 *  ceiling is comfortably above a normal run's whole pool. */
export const MAX_NOTED = 24;

/** Urls the page's own host serves (ccp.* virtual hosts, inlined data: URIs, page-relative
 *  paths). Anything else came off a third-party CDN — see noteMedia(). */
const LOCAL_URL_RE = /^(?:https?:\/\/ccp\.[a-z0-9-]+\/|data:|blob:|\.{0,2}\/)/i;

const log = {
  order: [],                    // urls, arrival order (<= MAX_NOTED)
  byUrl: Object.create(null),   // url -> { url, kind, count }
  shown: 0,                     // total payloads shown (may exceed order.length)
};

/** Wipe the ledger. boot.js calls this at the top of every run. */
export function resetMediaLog() {
  log.order.length = 0;
  log.byUrl = Object.create(null);
  log.shown = 0;
}

/**
 * Note that `url` was just shown as a payload.
 * @param {string} url   the media url (page-origin resolvable; ccp.assets hosted)
 * @param {string=} kind 'gif' | 'image' (free-form; only used for display)
 * @returns {string} `url`, unchanged — so call sites can wrap their pick inline.
 */
export function noteMedia(url, kind) {
  try {
    if (typeof url !== 'string' || !url) return url;
    log.shown += 1;
    // REMOTE PAYLOADS COUNT BUT ARE NOT KEPT. The archive re-renders these urls weeks
    // later out of IndexedDB; a third-party CDN url is not ours and will not still be
    // there, so retaining one buys a broken thumbnail in the Records Office and a stored
    // reference to somebody else's server. The run's "shown" tally above still includes it.
    if (!LOCAL_URL_RE.test(url)) return url;
    const hit = log.byUrl[url];
    if (hit) { hit.count += 1; return url; }
    if (log.order.length >= MAX_NOTED) return url;
    log.byUrl[url] = { url, kind: (kind === 'image' ? 'image' : 'gif'), count: 1 };
    log.order.push(url);
  } catch (_e) { /* a ledger must never cost a reward */ }
  return url;
}

/** The noted payloads, arrival order. Fresh array, fresh entries. */
export function recordedMedia() {
  const out = [];
  for (const u of log.order) {
    const e = log.byUrl[u];
    if (e) out.push({ url: e.url, kind: e.kind, count: e.count });
  }
  return out;
}

/** Total payloads shown this run (counts repeats; may exceed recordedMedia().length). */
export function mediaShownCount() { return log.shown; }
