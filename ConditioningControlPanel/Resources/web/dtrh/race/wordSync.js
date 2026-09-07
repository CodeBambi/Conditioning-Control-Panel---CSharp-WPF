/* ============================================================================
 * race/wordSync.js - THE SYNC WIN: the log, the offset, the overlay.
 *
 *   createPopLog({ storage })        -> { push, rows, clear, size }
 *   readNudge / writeNudge(trackId)  -> the per-track offset in localStorage
 *   shiftRoad(chart, sec, { trackId }) -> the same road, every word-derived second moved
 *   createSyncOverlay(root, opts)    -> the `?wsync=1` panel (built on demand only)
 *
 * WHY. A word bubble is poppable for 0.06 s at 22 m/s, so a transcript that is 80 ms
 * late is a road nobody can drive clean, and nobody can tell late from early by ear.
 * So every pop and every miss writes down how far from the word it landed (`dt`, in
 * seconds of the file, positive = popped after the word) into a ring of LOG_MAX rows
 * in localStorage, `[` and `]` move THIS track's offset by NUDGE_SEC (Shift: NUDGE_BIG_SEC)
 * and `\` copies the numbers a words/index.json row wants. The offset is applied ONCE,
 * in race/cloudChart.js, on the road as it leaves the source (file offset + local nudge),
 * so bubbles, rows, plates and the band all move together and the cache never holds it.
 *
 * Everything but createSyncOverlay is node-clean: race/smoke/wsync-check.mjs runs it.
 * ==========================================================================*/

export const LOG_KEY = 'rt-word-sync';
export const LOG_MAX = 512;
export const OFFSET_PREFIX = 'rt-word-offset:';
export const NUDGE_SEC = 0.05, NUDGE_BIG_SEC = 0.25;
/** The overlay's window: median and p90 over this many pops, newest first. */
export const STATS_N = 20;
export const HIST_BINS = 21, HIST_SEC = 1;
/** How long the panel shows itself after a nudge or an export on a page without ?wsync=1. */
export const SHOW_MS = 2000, NOTE_MS = 1500;
/** The event kinds laid off the words file. Energy kinds (build, peak, release) never move. */
export const WORD_KINDS = Object.freeze(['word', 'trigger', 'count', 'drop', 'chant', 'silence']);

const num = (v, d) => (typeof v === 'number' && isFinite(v) ? v : d);
const r3 = (v) => Math.round(v * 1000) / 1000;
const store = () => { try { return typeof localStorage !== 'undefined' ? localStorage : null; } catch (e) { return null; } };

/** The key a track's log rows and offset are filed under: the cloud id first, the hash second. */
export const trackIdOf = (chart) => (chart && chart.source && (chart.source.cloudId || chart.source.hash)) || '';
/** '+0.15 s' */
export const fmtSec = (v) => (v < 0 ? '-' : '+') + Math.abs(num(v, 0)).toFixed(2) + ' s';

export function readNudge(trackId, storage = store()) {
  if (!trackId || !storage) return 0;
  try { return r3(num(parseFloat(storage.getItem(OFFSET_PREFIX + trackId)), 0)); } catch (e) { return 0; }
}
/** Store the offset (rounded to a millisecond; 0 removes the key). Returns what was stored. */
export function writeNudge(trackId, sec, storage = store()) {
  const v = r3(num(sec, 0));
  if (!trackId || !storage) return v;
  try { if (v) storage.setItem(OFFSET_PREFIX + trackId, String(v)); else storage.removeItem(OFFSET_PREFIX + trackId); } catch (e) { /* full, or private */ }
  return v;
}

/**
 * The road with every word-derived second moved by `sec`, as a plain object for
 * normalizeChart. Ids are KEPT, so a mid-run replace (race/chart.js sched.replace)
 * adopts only what is not yet handed over and never re-fires a bubble already down.
 * `analysis.offsetSec` accumulates; `source.cloudId` is stamped when `trackId` is given.
 */
export function shiftRoad(chart, sec, { trackId = '' } = {}) {
  const s = r3(num(sec, 0)), dur = num(chart.source && chart.source.durationSec, 0);
  const mv = (t) => Math.max(0, Math.min(dur, r3(t + s)));
  const events = (chart.events || []).map((e) => (WORD_KINDS.includes(e.kind) ? { ...e, t: mv(e.t) } : { ...e }));
  const words = (chart.words || []).map((w) => ({ ...w, t: mv(w.t) }));
  const source = { ...chart.source, ...(trackId ? { cloudId: trackId } : {}) };
  const analysis = { ...(chart.analysis || {}), offsetSec: r3(num(chart.analysis && chart.analysis.offsetSec, 0) + s) };
  return { ...chart, events, words, source, analysis };
}

/** The ring of pops and misses, written through to storage on every push, never throwing. */
export function createPopLog({ storage = store(), max = LOG_MAX } = {}) {
  let rows = [];
  try { const got = storage ? JSON.parse(storage.getItem(LOG_KEY) || '[]') : []; if (Array.isArray(got)) rows = got.slice(-max); } catch (e) { rows = []; }
  const save = () => { try { if (storage) storage.setItem(LOG_KEY, JSON.stringify(rows)); } catch (e) { /* full, or private */ } };
  return {
    push(row) { rows.push(row); if (rows.length > max) rows.splice(0, rows.length - max); save(); },
    rows(trackId) { return trackId ? rows.filter((r) => r.trackId === trackId) : rows.slice(); },
    clear() { rows = []; save(); },
    get size() { return rows.length; },
  };
}

/** Median and p90 of the newest `n` pops (misses excluded), and a histogram of every pop given. */
export function statsOf(rows, n = STATS_N) {
  const pops = rows.filter((r) => !r.missed && isFinite(num(r.dt, NaN)));
  const last = pops.slice(-n).map((r) => r.dt).sort((a, b) => a - b);
  const q = (p) => (last.length ? last[Math.min(last.length - 1, Math.max(0, Math.ceil(p * last.length) - 1))] : 0);
  const hist = new Array(HIST_BINS).fill(0);
  for (const r of pops) {
    const b = Math.floor(((r.dt + HIST_SEC) / (2 * HIST_SEC)) * HIST_BINS);
    if (b >= 0 && b < HIST_BINS) hist[b]++;
  }
  return { n: last.length, median: r3(q(0.5)), p90: r3(q(0.9)), hist };
}

/** The line a words/index.json row wants (W6 commits it). */
export function exportOf({ cloudId = '', offsetSec = 0, rows = [] } = {}) {
  const s = statsOf(rows, STATS_N);
  return { cloudId, offsetSec: r3(num(offsetSec, 0)), n: s.n, medianDt: s.median, p90Dt: s.p90 };
}

/**
 * The panel. Nothing is built until this is called, so a page without ?wsync=1 and
 * without a nudge pays nothing. `buttons` puts the phone's [ - ] [ + ] and export on it.
 */
export function createSyncOverlay(root, { buttons = false, onNudge = null, onExport = null } = {}) {
  const el = (tag, cls, parent, text) => { const d = document.createElement(tag); d.className = cls; if (text != null) d.textContent = text; parent.appendChild(d); return d; };
  const box = el('div', 'rw-sync rh-plate', root);
  const title = el('div', 'rw-sync-title', box, '');
  const line = el('div', 'rw-sync-row rh-num', box);
  const med = el('b', '', el('span', '', line, 'med '), '+0.00');
  const p90 = el('b', '', el('span', '', line, 'p90 '), '+0.00');
  const n = el('b', '', el('span', '', line, 'n '), '0');
  const canvas = el('canvas', 'rw-sync-hist', box);
  const offRow = el('div', 'rw-sync-row rh-num', box);
  const off = el('b', 'rw-sync-off', el('span', '', offRow, 'offset '), '+0.00 s');
  if (buttons) {
    const btn = (label, fn) => { const b = el('button', 'rw-sync-btn', offRow, label); b.type = 'button'; b.addEventListener('click', (e) => { e.preventDefault(); fn(); }); return b; };
    btn('-', () => { if (onNudge) onNudge(-NUDGE_SEC); });
    btn('+', () => { if (onNudge) onNudge(NUDGE_SEC); });
    btn('export', () => { if (onExport) onExport(); });
  }
  const note = el('div', 'rw-sync-note', box, '');
  let hideT = 0, noteT = 0, lastHist = new Array(HIST_BINS).fill(0);
  const later = (fn, ms) => setTimeout(fn, ms);
  function paint(hist) {
    const dpr = Math.min(2, num(window.devicePixelRatio, 1)), w = canvas.clientWidth || 160, h = canvas.clientHeight || 34;
    if (canvas.width !== Math.round(w * dpr)) { canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr); }
    const g = canvas.getContext('2d');
    if (!g) return;
    g.setTransform(dpr, 0, 0, dpr, 0, 0); g.clearRect(0, 0, w, h);
    const top = Math.max(1, ...hist), bw = w / HIST_BINS;
    g.fillStyle = 'rgba(255, 255, 255, 0.14)'; g.fillRect(Math.floor(w / 2), 0, 1, h);   // the word itself
    g.fillStyle = '#ff69b4';
    hist.forEach((v, i) => { if (v) { const bh = Math.max(1, (v / top) * (h - 2)); g.fillRect(i * bw + 1, h - bh, Math.max(1, bw - 2), bh); } });
  }
  return {
    el: box,
    /** Fresh numbers. `rows` are this track's log rows, `offsetSec` the total applied to it. */
    update({ rows = [], offsetSec = 0, name = '' } = {}) {
      const s = statsOf(rows);
      title.textContent = name; med.textContent = fmtSec(s.median).slice(0, -2); p90.textContent = fmtSec(s.p90).slice(0, -2);
      n.textContent = String(s.n); off.textContent = fmtSec(offsetSec); lastHist = s.hist;
      if (!box.hidden) paint(s.hist);
    },
    /** Show it; for `ms` only when given (the auto-show on a nudge without ?wsync=1). */
    show(ms = 0) {
      if (box.hidden) { box.hidden = false; paint(lastHist); }
      if (hideT) { clearTimeout(hideT); hideT = 0; }
      if (ms > 0) hideT = later(() => { hideT = 0; box.hidden = true; }, ms);
    },
    say(text) { note.textContent = text; if (noteT) clearTimeout(noteT); noteT = later(() => { noteT = 0; note.textContent = ''; }, NOTE_MS); },
    dispose() { if (hideT) clearTimeout(hideT); if (noteT) clearTimeout(noteT); if (box.parentNode) box.parentNode.removeChild(box); },
  };
}

// self-check: node race/smoke/wsync-check.mjs (the log, the shift, the keys, and the panel on a phone).
