/* ============================================================================
 * race/popped.js - HOW MANY THOUGHTS YOU TOOK, AND THE BEST YOU EVER DID.
 *
 *   readBests(store)                       -> { key: record }
 *   bestFor(bests, { hash, cloudId })      -> record | null
 *   saveBest(store, run)                   -> { rec, wrote }
 *   bestLine(rec)                          -> 'popped 345 / 560 thoughts'
 *   poppedLine(popped, total)              -> 'popped 12 / 560'
 *
 * WHAT A THOUGHT IS. One word bubble: one `word` event of the chart (CHART.md),
 * which race/cues.js lays on the road as exactly one bubble wearing exactly one
 * word. That is the whole count and it is deliberately the simple one:
 *
 *   - the TOTAL is read off the chart the moment it loads, so the number on the
 *     screen is knowable before the first metre and never depends on what the
 *     road happened to spawn or on how far the player got;
 *   - a TRIGGER ROW is not in it. A row is five faces of one trigger and it is
 *     one thing to take, not five thoughts to pop, so counting it would make the
 *     total a number nobody could check against the words they heard.
 *
 * That leaves `popped N / M thoughts` meaning exactly "the words of this file I
 * took off the road, of all the words in it". The end card's older `taken N of M`
 * is a different and finer thing (a LINE of bubbles counts once there, and drops
 * and rows count too): both are true, they answer different questions.
 *
 * THE STORE is one localStorage key, `race.popped`, holding a map of records:
 *
 *   { "<hash>": { popped, total, name, cloudId, at } }
 *
 * keyed by the chart's file hash (CHART.md `source.hash`) where there is one and
 * by `cid:<cloudId>` where there is not, so a track that was never hashed still
 * remembers. Every record carries its cloudId too, because the LEVELS panel
 * knows a level by its url and never by its hash, so a row looks a best up by
 * cloudId (race/levels.js). Only a run that reached the END of the chart writes:
 * a quit is not a score.
 *
 * A best is beaten on the count alone (`popped`), never on the fraction: a
 * longer version of a file is not a worse run.
 *
 * Pure but for the store, which is handed in (localStorage by default, and every
 * touch of it is wrapped: a private window throws on read and on write and
 * nothing here may care). No DOM, no clock but Date.now, no fetch.
 * ==========================================================================*/

/** The one key. */
export const POPPED_KEY = 'race.popped';
/** How many tracks the map keeps. Past this the oldest records go, oldest by when they were written. */
export const MAX_TRACKS = 240;

const num = (v) => { const n = Number(v); return isFinite(n) && n > 0 ? Math.floor(n) : 0; };
const str = (v, max) => String(v == null ? '' : v).slice(0, max);
const memStore = () => { try { return typeof localStorage !== 'undefined' ? localStorage : null; } catch (e) { return null; } };

/** The key a track files its record under: the hash if it has one, else its cloud id, else ''. */
export function keyFor({ hash = '', cloudId = '' } = {}) {
  const h = str(hash, 64).trim();
  if (h) return h;
  const c = str(cloudId, 64).trim().toLowerCase();
  return c ? 'cid:' + c : '';
}

/** One stored record, or null for anything that is not one. Everything is re-read, nothing trusted. */
function record(raw) {
  if (!raw || typeof raw !== 'object') return null;
  const popped = num(raw.popped), total = num(raw.total);
  if (!total || popped > total) return null;
  return { popped, total, name: str(raw.name, 60), cloudId: str(raw.cloudId, 64).toLowerCase(), at: num(raw.at) };
}

/** The whole map, or an empty one. A private window, a cleared store and a corrupt value are all `{}`. */
export function readBests(store = undefined) {
  const mem = store !== undefined ? store : memStore();
  let raw = null;
  try { raw = mem ? JSON.parse(mem.getItem(POPPED_KEY) || 'null') : null; } catch (e) { return {}; }
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const out = {};
  for (const k of Object.keys(raw)) { const r = record(raw[k]); if (r) out[k] = r; }
  return out;
}

/**
 * The record for a track, by hash first and by cloud id second. The LEVELS panel only ever knows
 * the second one (it reads a url, not a file), so the scan is the whole point of this function.
 */
export function bestFor(bests, { hash = '', cloudId = '' } = {}) {
  if (!bests || typeof bests !== 'object') return null;
  const k = keyFor({ hash, cloudId });
  if (k && bests[k]) return bests[k];
  const c = str(cloudId, 64).trim().toLowerCase();
  if (!c) return null;
  for (const key of Object.keys(bests)) if (bests[key].cloudId === c) return bests[key];
  return null;
}

/**
 * File a completed run. Writes only when it beat what was there (or when nothing was), and answers
 * `{ rec, wrote }` with the record that stands either way. Call it only for a run that reached the
 * end of the chart: a quit is not a score.
 */
export function saveBest(store = undefined, run = {}) {
  const mem = store !== undefined ? store : memStore();
  const key = keyFor(run);
  const fresh = record({ popped: run.popped, total: run.total, name: run.name, cloudId: run.cloudId, at: run.at || Date.now() });
  if (!key || !fresh) return { rec: null, wrote: false };
  const bests = readBests(mem);
  const had = bests[key] || null;
  if (had && had.popped >= fresh.popped) return { rec: had, wrote: false };
  bests[key] = fresh;
  const keys = Object.keys(bests);
  if (keys.length > MAX_TRACKS) {
    keys.sort((a, b) => (bests[a].at || 0) - (bests[b].at || 0));
    for (const k of keys.slice(0, keys.length - MAX_TRACKS)) delete bests[k];
  }
  try { if (mem) mem.setItem(POPPED_KEY, JSON.stringify(bests)); } catch (e) { /* a private window, and nothing is lost */ }
  return { rec: fresh, wrote: true };
}

/** The live line: `popped 12 / 560`. Lower case, house style, no total means no line. */
export function poppedLine(popped, total) {
  const t = num(total);
  if (!t) return '';
  return `popped ${Math.min(num(popped), t)} / ${t}`;
}

/** The menu's line: `popped 345 / 560 thoughts`. '' for no record at all. */
export function bestLine(rec) {
  if (!rec) return '';
  const line = poppedLine(rec.popped, rec.total);
  return line ? line + ' thoughts' : '';
}

// self-check: node --check is the bar; race/smoke/popped-check.mjs drives the whole thing through the page.
