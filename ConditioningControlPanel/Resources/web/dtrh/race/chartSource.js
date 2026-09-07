/* ============================================================================
 * race/chartSource.js - which chart a track gets, and the two names a track has.
 *
 *   cloudIdFrom(url)                   -> the stable name of the file on the CDN
 *   hashBytes(head, byteLength)        -> the CHART.md hash, pure
 *   hashUrl(url, { fetch })            -> the same hash WITHOUT downloading the file
 *   loadIndex(url, { fetch })          -> race/charts/index.json, once
 *   findAuthored(index, { cloudId, hash })
 *   isAuthored(chart)
 *
 * THE ORDER (owner rule: AUTHORED CHARTS ALWAYS WIN). Every track is looked up
 * four times and stops at the first answer:
 *
 *   a. the authored index, by `cloudId` off the url
 *   b. the authored index, by the file hash
 *   c. the generated-chart cache, by the file hash
 *   d. nothing: generate one
 *
 * a and b are the point of this file. Both of them have to be able to answer
 * BEFORE the file is downloaded, or "an authored chart costs no download" is a
 * sentence and not a behaviour. a needs only the url. b needs the hash, and the
 * hash is a length and the first 1 MiB, so `hashUrl` asks for a length and a
 * megabyte instead of an hour of audio.
 *
 * WHAT AN AUTHORED CHART IS. Any chart JSON with top level `hand: true`. It is
 * used exactly as it was written: never merged with a generated one, never
 * regenerated, never written into the cache. `race/charts/README.md` is the
 * format and the link step.
 *
 * THE HASH IS CHART.md's, unchanged: SHA1 of the byte length as 8 bytes little
 * endian plus the first 1 MiB. `hashFile` in chart/editor/audio.js is the
 * canonical body and it is IMPORTED here, not copied, so the browser, the desktop
 * host and tools/racechart/align.py stay on one number.
 *
 * WHEN THE CDN WILL NOT COOPERATE. `Range` is not a CORS simple header, so a
 * ranged GET needs a preflight the CDN may not answer, and `Content-Range` needs
 * to be exposed before it can be read. Every one of those doors is tried and none
 * of them is required: if they are all shut, `hashUrl` answers null and the
 * caller hashes the body it had to download anyway. The cache still hits on the
 * second run. Only the saved download is lost, and it is lost quietly.
 * ==========================================================================*/

import { hashFile } from '../chart/editor/audio.js';

/** CHART.md: the hash reads the first 1 MiB and nothing else. */
export const HASH_HEAD = 1024 * 1024;
/** A path segment this long, made only of id characters, is an id and not a word. */
const ID_MIN = 20;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const IDISH = /^[A-Za-z0-9_-]+$/;

/**
 * The stable name of a file on the CDN, and the key an author writes in the index.
 *
 * A cdn url is a path to one file. The last segment is a file name and a file name
 * can be renamed; an id segment cannot. So: if any segment of the path is a uuid,
 * or is ID_MIN characters or more of `[A-Za-z0-9_-]`, the LAST such segment is the
 * id. Otherwise it is the last segment with its extension taken off. Lower cased
 * either way, because an index a person types by hand must not care about case.
 *
 * Pure: no network, no DOM. A url this cannot parse answers ''.
 */
export function cloudIdFrom(url) {
  let u = null;
  try { u = new URL(String(url)); } catch (e) { return ''; }
  const segs = u.pathname.split('/').map((s) => { try { return decodeURIComponent(s); } catch (e) { return s; } }).filter(Boolean);
  if (!segs.length) return '';
  for (let i = segs.length - 1; i >= 0; i--) {
    const s = segs[i];
    if (UUID.test(s) || (s.length >= ID_MIN && IDISH.test(s))) return s.toLowerCase();
  }
  return segs[segs.length - 1].replace(/\.[a-z0-9]{2,4}$/i, '').toLowerCase();
}

/**
 * The CHART.md hash of a file whose first bytes and total length are in hand.
 * Delegates to chart/editor/audio.js so there is one implementation of the sum:
 * `hashFile` wants `.size` and `.slice()`, and this is those two over a buffer we
 * already hold rather than over a File the page never had.
 *
 * @param {Uint8Array|ArrayBuffer} head        the first HASH_HEAD bytes (or fewer, for a small file)
 * @param {number} byteLength                  the WHOLE file's length, not the head's
 */
export function hashBytes(head, byteLength) {
  const bytes = head instanceof Uint8Array ? head : new Uint8Array(head || 0);
  const cut = bytes.length > HASH_HEAD ? bytes.subarray(0, HASH_HEAD) : bytes;
  const total = Number(byteLength);
  if (!(total > 0)) return Promise.resolve('');
  return hashFile({ size: total, slice: () => ({ arrayBuffer: async () => cut.slice().buffer }) });
}

/** `Content-Range: bytes 0-1048575/54237184` -> 54237184. Null when it is not there or not that. */
function totalFromRange(v) {
  const m = /\/\s*(\d+)\s*$/.exec(String(v || ''));
  const n = m ? Number(m[1]) : NaN;
  return n > 0 ? n : null;
}

/**
 * The file's hash without downloading the file.
 *
 *   1. HEAD for `content-length` (a CORS safelisted response header, so it reads
 *      cross origin whenever the HEAD itself is allowed).
 *   2. GET with `Range: bytes=0-<1 MiB - 1>`. A 206 gives the head bytes, and its
 *      `Content-Range` gives the total length when step 1 could not.
 *   3. If the server ignored the range and sent 200, that body IS the file: its
 *      length is the total and its first 1 MiB is the head, so the hash is still
 *      right (a full download happened, which is the cost of a server that will
 *      not do ranges).
 *
 * Answers `{ hash, bytes, total, ranged }` or null when it could not be worked
 * out at all. Never throws: every failure is a null and a log line.
 */
export async function hashUrl(url, { fetch: f = null, log = null, signal = null } = {}) {
  const get = f || (typeof fetch !== 'undefined' ? fetch : null);
  const say = (m) => { try { if (log) log('hash: ' + m); } catch (e) { /* no log */ } };
  if (!get || !url) return null;
  let total = null;
  try {
    const h = await get(url, { method: 'HEAD', mode: 'cors', credentials: 'omit', signal });
    if (h && h.ok) { const n = Number(h.headers.get('content-length')); if (n > 0) total = n; }
  } catch (e) { say('no HEAD (' + ((e && e.message) || e) + ')'); }
  let res = null;
  try {
    res = await get(url, { method: 'GET', mode: 'cors', credentials: 'omit', signal, headers: { Range: `bytes=0-${HASH_HEAD - 1}` } });
  } catch (e) { say('no ranged read (' + ((e && e.message) || e) + ')'); return null; }
  if (!res || !res.ok) { say('ranged read answered ' + (res ? res.status : 'nothing')); return null; }
  const ranged = res.status === 206;
  if (!total) total = totalFromRange(res.headers.get('content-range'));
  const bytes = new Uint8Array(await res.arrayBuffer());
  // A 200 to a ranged GET is the whole file: its own length is the only length there is.
  if (!ranged) total = bytes.length;
  if (!(total > 0)) { say('no length anywhere, cannot name this file'); return null; }
  const hash = await hashBytes(bytes, total);
  return hash ? { hash, bytes, total, ranged } : null;
}

/* ---- the authored index -------------------------------------------------- */

/** A chart JSON a person wrote. The one flag, checked in one place. */
export function isAuthored(chart) {
  return !!chart && typeof chart === 'object' && chart.hand === true;
}

const indexCache = new Map();        // url -> Promise<index>, so the file is fetched once a session

/**
 * `race/charts/index.json`, fetched once. An index that is missing or malformed is
 * an EMPTY index and a log line: an authored chart nobody wrote is not an error,
 * and a broken index must never stop a track from being charted.
 */
export function loadIndex(url, { fetch: f = null, log = null } = {}) {
  const key = String(url);
  if (indexCache.has(key)) return indexCache.get(key);
  const get = f || (typeof fetch !== 'undefined' ? fetch : null);
  const say = (m) => { try { if (log) log('charts index: ' + m); } catch (e) { /* no log */ } };
  const p = (async () => {
    if (!get) return { version: 1, tracks: [] };
    try {
      const res = await get(key, { credentials: 'omit' });
      if (!res || !res.ok) { say('none (' + (res ? res.status : 'no answer') + ')'); return { version: 1, tracks: [] }; }
      const json = await res.json();
      const tracks = Array.isArray(json && json.tracks) ? json.tracks.filter((t) => t && typeof t === 'object' && t.chart) : [];
      if (tracks.length) say(tracks.length + ' authored track' + (tracks.length === 1 ? '' : 's'));
      return { version: Number(json && json.version) || 1, tracks };
    } catch (e) { say('unreadable: ' + ((e && e.message) || e)); return { version: 1, tracks: [] }; }
  })();
  indexCache.set(key, p);
  return p;
}

/** Forget the fetched index. For the smoke, which serves more than one of them. */
export function forgetIndex(url) { if (url === undefined) indexCache.clear(); else indexCache.delete(String(url)); }

/**
 * The index row for a track, `cloudId` first and `hash` second, or null. Case is
 * ignored on both because both are typed by hand into a JSON file.
 */
export function findAuthored(index, { cloudId = '', hash = '' } = {}) {
  const rows = (index && Array.isArray(index.tracks)) ? index.tracks : [];
  const id = String(cloudId || '').toLowerCase(), h = String(hash || '').toLowerCase();
  if (id) { const r = rows.find((t) => String(t.cloudId || '').toLowerCase() === id); if (r) return { row: r, by: 'cloudId' }; }
  if (h) { const r = rows.find((t) => String(t.hash || '').toLowerCase() === h); if (r) return { row: r, by: 'hash' }; }
  return null;
}

export default { cloudIdFrom, hashBytes, hashUrl, loadIndex, findAuthored, isAuthored };
