/* ============================================================================
 * race/words.js - the transcript for a track, if there is one.
 *
 *   loadWords({ cloudId, hash }) -> Promise<{ version, hash, durationSec, words, hits?, engine? } | null>
 *
 * `hits`, when the file has them, are the trigger seconds tools/racechart/fingerprint.py
 * refined against the audio itself: { setId, t, dur, score, src: 'fp' | 'words', conf },
 * `t` at the correlation peak for src 'fp' and at the aligner's guess for src 'words'.
 * race/cloudChart.js triggerHits() lays the road on them and falls back to its own
 * detector scan for a file without them.
 *
 * `race/words/index.json` is a written-down table, exactly like `race/levels.json`
 * beside it: a row per track that has an aligned transcript, keyed by the file's
 * own cloud id first and by its CHART.md hash second. One file can sit at two urls
 * under two names, so the hash is the door that still answers when the id does not.
 * The row also carries what the file itself was stripped of on the way here: `offsetSec`,
 * the seconds the road is shifted by, and `engine`, the aligner that wrote the transcript.
 *
 * WHAT THIS IS FOR. Without words the road is the energy curve and nothing else,
 * which is why the bubbles the owner saw had nothing to do with the voice. With a
 * transcript in hand `race/cloudChart.js` can run the trigger detector over it and
 * lay the road on the words themselves.
 *
 * IT NEVER THROWS. Every door here is optional: no index, no row, a 404, a body
 * that is not the shape - all of them are null and one log line, and the caller
 * falls back to the wordless road it already had. A missing transcript is a
 * quieter road, never a dead run.
 *
 * NOTHING LEAVES THE MACHINE. These files are read off our own origin and are
 * timestamps and text; no audio is fetched here and nothing is uploaded anywhere.
 *
 * Node-clean: no DOM, no window, so `race/smoke/words-check.mjs` runs it under node.
 * ==========================================================================*/

/** Where the table lives, resolved off this module so a vendored copy still finds it. */
export const INDEX_URL = new URL('./words/index.json', import.meta.url).href;

const norm = (v) => String(v == null ? '' : v).trim().toLowerCase();
/** One in-flight promise per key, so a prefetch and a load never fetch twice. */
const cache = new Map();

/** `{ version, rows }`, or an empty table. Fetched once and kept. */
async function readIndex(get, say, indexUrl) {
  const res = await get(indexUrl, { credentials: 'omit' });
  if (!res || !res.ok) throw new Error('the index answered ' + (res ? res.status : 'nothing'));
  const json = await res.json();
  const rows = (json && Array.isArray(json.rows) ? json.rows : [])
    .filter((r) => r && typeof r.file === 'string' && r.file);
  say('words: ' + rows.length + ' transcripts on the shelf');
  return rows;
}

/** The shape the road reads. Anything else is not a transcript and is not used. */
function readFile(json) {
  if (!json || typeof json !== 'object' || !Array.isArray(json.words)) return null;
  const words = json.words.filter((w) => w && typeof w.t === 'number' && typeof w.w === 'string');
  if (!words.length) return null;
  const out = { version: 1, hash: String(json.hash || ''), durationSec: Number(json.durationSec) || 0, words };
  // THE ALIGNER STAMP, when the export carries one ('whisper-large-v3+script' and the like).
  // race/cloudChart.js isAligned() reads it to tell a doubt about the WORD (a transcript heard
  // cold: keep the CAPTION_CONF cut) from a doubt about its SECOND (a transcript aligned to the
  // script: every word is the word, and every one of them goes on the road). The race copies on
  // race/words/ carry no `engine`, so their index row carries it and loadWords() hands it along.
  if (typeof json.engine === 'string' && json.engine) out.engine = json.engine;
  // the fingerprinted trigger seconds, when tools/racechart/fingerprint.py has been over this file:
  // { setId, t, dur, score, src: 'fp' | 'words', conf }. cloudChart.js prefers them to its own scan.
  // Only a well-formed row gets through; a file without them is exactly the file it was.
  if (Array.isArray(json.hits)) {
    const hits = json.hits.filter((h) => h && typeof h.setId === 'string' && h.setId && typeof h.t === 'number' && isFinite(h.t) && h.t >= 0)
      .map((h) => ({ setId: h.setId, t: h.t, dur: Number(h.dur) > 0 ? Number(h.dur) : 0, score: Number(h.score) || 0,
        src: h.src === 'fp' ? 'fp' : 'words', conf: typeof h.conf === 'number' ? Math.max(0, Math.min(1, h.conf)) : 1 }));
    if (hits.length) out.hits = hits;
  }
  return out;
}

/**
 * The transcript for one track, or null.
 *
 * @param {object}   o
 * @param {string}   [o.cloudId]   the file uuid off the cdn url, the first key
 * @param {string}   [o.hash]      its CHART.md hash, the second
 * @param {function} [o.fetch]     the smoke's seam
 * @param {function} [o.log]
 * @param {string}   [o.indexUrl]
 */
export async function loadWords({ cloudId = '', hash = '', fetch: f = null, log = null, indexUrl = INDEX_URL } = {}) {
  const get = f || (typeof fetch !== 'undefined' ? fetch : null);
  const say = (m) => { try { if (log) log(m); } catch (e) { /* no log */ } };
  const id = norm(cloudId), h = norm(hash);
  if (!get || (!id && !h)) return null;
  const key = indexUrl + '|' + id + '|' + h;
  if (cache.has(key)) return cache.get(key);
  const work = (async () => {
    try {
      const row = await loadWordsRow({ cloudId, hash, fetch: get, log, indexUrl });
      if (!row) return null;
      const res = await get(new URL(row.file, indexUrl).href, { credentials: 'omit' });
      if (!res || !res.ok) throw new Error('the transcript answered ' + (res ? res.status : 'nothing'));
      const got = readFile(await res.json());
      say('words: ' + (got ? got.words.length + ' words for ' + (row.title || row.cloudId) : 'nothing readable in ' + row.file));
      // the row's own key, offset and aligner stamp ride along, so the road can be filed, shifted
      // and read at its true confidence by them. The file's own `engine` wins when it has one.
      if (got) {
        got.cloudId = row.cloudId;
        got.offsetSec = row.offsetSec;
        if (!got.engine && row.engine) got.engine = row.engine;
      }
      return got;
    } catch (err) {
      say('words: ' + ((err && err.message) || err));
      return null;
    }
  })();
  cache.set(key, work);
  return work;
}

/**
 * The index ROW for one track, or null: `{ cloudId, hash, title, file, offsetSec, engine }`,
 * with `offsetSec` (seconds added to every word of the file, default 0) read off the row so
 * race/cloudChart.js can shift a CACHED road without fetching its transcript again, and
 * `engine` the aligner stamp for a file whose own copy does not carry one ('' when the table
 * says nothing, which leaves the CAPTION_CONF cut in place). Same keys, same doors, never throws.
 */
export async function loadWordsRow({ cloudId = '', hash = '', fetch: f = null, log = null, indexUrl = INDEX_URL } = {}) {
  const get = f || (typeof fetch !== 'undefined' ? fetch : null);
  const say = (m) => { try { if (log) log(m); } catch (e) { /* no log */ } };
  const id = norm(cloudId), h = norm(hash);
  if (!get || (!id && !h)) return null;
  try {
    if (!cache.has(indexUrl)) cache.set(indexUrl, readIndex(get, say, indexUrl));
    const rows = await cache.get(indexUrl);
    const row = (id && rows.find((r) => norm(r.cloudId) === id)) || (h && rows.find((r) => norm(r.hash) === h)) || null;
    if (!row) return null;
    const off = Number(row.offsetSec);
    return { cloudId: norm(row.cloudId), hash: norm(row.hash), title: String(row.title || ''), file: row.file,
      offsetSec: isFinite(off) ? off : 0, engine: String(row.engine || '') };
  } catch (err) { say('words: ' + ((err && err.message) || err)); return null; }
}

/** The smoke starts over between sections; nothing in the game ever calls this. */
export function forgetWords() { cache.clear(); }

export default loadWords;
