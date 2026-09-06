/* ============================================================================
 * chart/maker/save.js - keeping it, and handing it over (MAKER.md, PR M4).
 *
 * Two different things, on purpose. The autosave is the safety net: every edit
 * writes the working state to localStorage under the audio's hash, so closing
 * the tab and coming back to the same mp3 picks up where you left off, and
 * "start over" throws that away. Saving the track is the deliverable: one
 * <stem>.chart.json in the Downloads folder, chart JSON v1, the file race.html
 * reads. Neither of them ever sends the audio anywhere.
 * ==========================================================================*/

import { buildChart, bumpIds, snapshotState } from './model.js';

const $ = (id) => document.getElementById(id);
const KEY = (hash) => 'trackmaker:' + hash;
const WRITE_AFTER_MS = 800;

let api = null, S = null, timer = 0;

/* ---- the autosave -------------------------------------------------------- */

/** localStorage throws when the browser is set to keep nothing. That is fine. */
function write() {
  if (!S.audio || !S.hits.length) return;
  try {
    localStorage.setItem(KEY(S.audio.hash), JSON.stringify(snapshotState(S)));
    $('startover').hidden = false;
  } catch (e) { /* no store, no net: the track is still exportable */ }
}

/** Called after every edit. Writes once the hand stops, not once per pixel. */
export function touch() {
  clearTimeout(timer);
  timer = setTimeout(write, WRITE_AFTER_MS);
}

export function read(hash) {
  try {
    const raw = localStorage.getItem(KEY(hash));
    const o = raw ? JSON.parse(raw) : null;
    return o && o.v === 1 && Array.isArray(o.bubs) && Array.isArray(o.hits) ? o : null;
  } catch (e) { return null; }
}

export function forget(hash) {
  clearTimeout(timer);
  try { localStorage.removeItem(KEY(hash)); } catch (e) { /* nothing to forget */ }
  $('startover').hidden = true;
}

/**
 * Put a saved state back over the freshly placed one. Ids come back with it, so
 * the id counter walks past them before anything new is made.
 */
export function restoreInto(state, saved) {
  state.bubs = saved.bubs;
  state.hits = saved.hits;
  state.cfg = saved.cfg || state.cfg;
  state.road = saved.road || null;
  state.sel.clear();
  bumpIds(saved.bubs);
  return saved;
}

/* ---- the track ----------------------------------------------------------- */

export function chartName(state) {
  const stem = (state.audio && state.audio.stem) || 'track';
  return stem.replace(/[\\/:*?"<>|]+/g, '-') + '.chart.json';
}

/** Hand the browser the file. Nothing leaves the machine: it is a blob url. */
export function exportChart() {
  if (!S.audio) return api.status('pick an mp3 first');
  if (!S.bubs.length && !S.road) return api.status('nothing on the track yet');
  const json = JSON.stringify(buildChart(S), null, 1);
  const url = URL.createObjectURL(new Blob([json], { type: 'application/json' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = chartName(S);
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
  const walls = S.bubs.filter((b) => b.kind === 'wall').length;
  const road = S.road ? ', ' + S.road.events.length + ' on the road' : '';
  api.status('saved ' + chartName(S) + ': ' + (S.bubs.length - walls) + ' bubbles, ' + walls + ' wall' + (walls === 1 ? '' : 's') + road + '. drop it next to the mp3.');
  return json;
}

export function install(theApi) {
  api = theApi;
  S = theApi.state;
  $('export').addEventListener('click', exportChart);
  $('startover').addEventListener('click', (ev) => { ev.preventDefault(); api.startOver(); });
  document.addEventListener('keydown', (ev) => {
    if ((ev.ctrlKey || ev.metaKey) && ev.code === 'KeyS') { ev.preventDefault(); exportChart(); }
  });
}
