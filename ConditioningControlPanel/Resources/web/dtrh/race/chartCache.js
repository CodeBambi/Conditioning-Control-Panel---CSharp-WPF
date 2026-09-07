/* ============================================================================
 * race/chartCache.js - the IndexedDB cache for GENERATED charts. WEB ONLY.
 *
 *   openCache({ name, log }) -> { get, put, count, close }   (never throws)
 *
 * WHY IT EXISTS. Charting a track means downloading it and decoding it: tens of
 * megabytes and several seconds of a phone's life. The second time a player runs
 * the same file none of that has to happen again, because the chart is small
 * (timestamps and labels) and the file it was written against has a name that
 * cannot change under it: the CHART.md hash.
 *
 * WHAT IT NEVER HOLDS. Audio. A record is a chart and four small fields. Nothing
 * in here is uploaded, and nothing in here leaves the browser that wrote it.
 *
 * WHAT IT NEVER HOLDS, PART TWO: an AUTHORED chart. A chart with `hand: true` was
 * written by a person and lives in `race/charts/`, not here. `put` refuses one, so
 * a generated chart can never end up masquerading as an authored one and an
 * authored one can never be evicted by the cap.
 *
 * INVALIDATION. Every record carries `gen`, the generator id of the road that
 * wrote it. Tune the road and that string changes, and every chart the old road
 * wrote reads as a miss and is dropped on sight. There is no migration to write:
 * the source of a generated chart is the file, and the file is still there.
 *
 * THE CAP. MAX_ENTRIES, least recently READ first (`at` is touched on a hit, not
 * just on a write), so the track a player keeps coming back to outlives the one
 * they tried once. The sweep runs after a write, never in front of a read.
 *
 * A BROWSER THAT SAYS NO. Private windows, blocked site data and the odd embedded
 * webview all refuse IndexedDB, sometimes by throwing on `indexedDB.open` and
 * sometimes by never answering it. Every call in here is wrapped and time boxed,
 * and a cache that could not open answers `null` to every get and swallows every
 * put. A player with no cache charts every track: slower, never broken.
 * ==========================================================================*/

/** Bump this when the on-disk shape changes. The old store is deleted, not migrated. */
export const DB_VERSION = 1;
export const DB_NAME = 'race-charts';
export const STORE = 'charts';
/** How many generated charts are kept. A chart is a few tens of kB, so this is small on disk. */
export const MAX_ENTRIES = 50;
/** A browser that refuses IndexedDB sometimes refuses it by never answering. */
export const OPEN_TIMEOUT_MS = 4000;

const now = () => Date.now();

/** An IDBRequest as a promise. A failure is `null`, never a throw: the caller has a fallback. */
function ask(req, timeoutMs = 0) {
  return new Promise((res) => {
    let done = false;
    const end = (v) => { if (!done) { done = true; res(v); } };
    const t = timeoutMs ? setTimeout(() => end(null), timeoutMs) : 0;
    try {
      req.onsuccess = () => { if (t) clearTimeout(t); end(req.result); };
      req.onerror = () => { if (t) clearTimeout(t); end(null); };
      req.onblocked = () => { if (t) clearTimeout(t); end(null); };
    } catch (e) { if (t) clearTimeout(t); end(null); }
  });
}

/**
 * Open the cache. Resolves to a cache object whatever happens: if the store could
 * not be opened the object is still there and every get answers null.
 *
 * @param {object}  [o]
 * @param {string}  [o.name]     db name, so a smoke can use its own
 * @param {function}[o.log]
 * @param {object}  [o.idb]      the IDBFactory, for a test that hands in its own
 */
export async function openCache({ name = DB_NAME, log = null, idb = null } = {}) {
  const say = (m) => { try { if (log) log('chart cache: ' + m); } catch (e) { /* no log */ } };
  const factory = idb || (typeof indexedDB !== 'undefined' ? indexedDB : null);
  let db = null;
  if (factory) {
    try {
      const req = factory.open(name, DB_VERSION);
      req.onupgradeneeded = () => {
        const d = req.result;
        // A version bump is a wipe: a chart is cheap to make again and a half migrated
        // store is a bug that only shows up on somebody else's phone.
        if (d.objectStoreNames.contains(STORE)) d.deleteObjectStore(STORE);
        const st = d.createObjectStore(STORE, { keyPath: 'hash' });
        st.createIndex('at', 'at');
      };
      db = await ask(req, OPEN_TIMEOUT_MS);
    } catch (e) { say('would not open: ' + ((e && e.message) || e)); }
  }
  if (!db) say('not available, every track will be charted fresh');

  /** One transaction, one store. `null` when there is no db, which every caller handles. */
  const store = (mode) => {
    if (!db) return null;
    try { return db.transaction(STORE, mode).objectStore(STORE); } catch (e) { say(mode + ': ' + e); return null; }
  };

  /** Drop the oldest records until the store is back under the cap. Fire and forget. */
  async function sweep() {
    const st = store('readwrite');
    if (!st) return;
    const n = await ask(st.count());
    if (!(n > MAX_ENTRIES)) return;
    let over = n - MAX_ENTRIES;
    await new Promise((res) => {
      let cur;
      try { cur = st.index('at').openCursor(); } catch (e) { return res(); }
      cur.onerror = () => res();
      cur.onsuccess = () => {
        const c = cur.result;
        if (!c || over <= 0) return res();
        try { c.delete(); } catch (e) { /* gone under us */ }
        over--;
        c.continue();
      };
    });
    say(`swept ${n - MAX_ENTRIES} old chart${n - MAX_ENTRIES === 1 ? '' : 's'}`);
  }

  return {
    /**
     * The chart written for `hash` by generator `gen`, or null. A record from an
     * older generator is a miss AND a delete: the road changed, that chart is wrong.
     */
    async get(hash, gen) {
      if (!hash || !db) return null;
      const st = store('readonly');
      if (!st) return null;
      const rec = await ask(st.get(String(hash)));
      if (!rec || !rec.chart) return null;
      if (gen && rec.gen !== gen) {
        const w = store('readwrite');
        if (w) { try { w.delete(String(hash)); } catch (e) { /* gone under us */ } }
        say('stale road for ' + String(hash).slice(0, 8) + ', dropped');
        return null;
      }
      const touch = store('readwrite');                 // read counts as use: LRU, not first in first out
      if (touch) { try { touch.put({ ...rec, at: now() }); } catch (e) { /* gone under us */ } }
      return rec.chart;
    },

    /** Keep a GENERATED chart. An authored one is refused: those live in race/charts/. */
    async put(hash, chart, gen) {
      if (!hash || !chart || !db) return false;
      if (chart.hand === true) { say('refused an authored chart: those are not cached'); return false; }
      const st = store('readwrite');
      if (!st) return false;
      const okay = await ask(st.put({ hash: String(hash), gen: String(gen || ''), at: now(), chart })) !== null;
      if (okay) sweep();
      return okay;
    },

    async count() { const st = store('readonly'); return st ? (await ask(st.count())) || 0 : 0; },
    get available() { return !!db; },
    close() { try { if (db) db.close(); } catch (e) { /* already gone */ } db = null; },
  };
}

export default openCache;
