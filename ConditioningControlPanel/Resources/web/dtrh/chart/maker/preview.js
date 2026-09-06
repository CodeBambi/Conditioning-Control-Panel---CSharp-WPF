/* ============================================================================
 * chart/maker/preview.js - the race in the bottom row (chart/MAKER.md, PR M6).
 *
 * The fifth line under the words is the run itself. Once there is a road to
 * look at, the row fills with an iframe of race.html on the bridge raceBoot.js
 * already answers (`?bridge=parent`): `chart` behind every edit,
 * `clock` four times a second while the file plays, `seek` the moment the
 * author scrubs. The maker's own <audio> is the only sound source in the room,
 * so the frame runs with its music off and its own effects on.
 *
 * Watch only, on purpose: a transparent sheet sits over the frame so a click
 * lands on the maker and space never stops meaning play. "pop out" opens the
 * same run in a tab for anyone who wants to drive it.
 *
 * The sizing and the url are pure and run under chart/smoke/maker-check.mjs.
 * ==========================================================================*/

import { buildChart } from './model.js';

/* ---- the bridge, pure ---------------------------------------------------- */

/** The contract URL, relative to maker.html. Every switch is deliberate: no
 *  pixel block, no intro, and the run starts on its own. Music is 0 because the
 *  author is listening to their own file, and PREVIEW_URL below is this url at 0,
 *  which the smoke pins. */
export function raceUrl(music = 0) {
  const m = Math.round(Math.max(0, Math.min(1, Number(music) || 0)) * 100) / 100;
  return '../race.html?bridge=parent&autostart=1&pixel=0&intro=0&music=' + m;
}
export const CHART_DEBOUNCE_MS = 300;
export const CLOCK_MS = 250;          // 4 Hz, the host's own cadence
export const READY_TIMEOUT_MS = 8000;

const t3 = (t) => Math.round((Number(t) || 0) * 1000) / 1000;

export function clockMessage(t, playing) { return { type: 'clock', t: t3(t), playing: !!playing }; }
export function seekMessage(t, playing) { return { type: 'seek', t: t3(t), playing: !!playing }; }

/** A message is ours only when it came from our own frame, on our own origin, with a body. */
export function accepts(ev, origin, source) {
  return !!ev && ev.origin === origin && !!source && ev.source === source
    && !!ev.data && typeof ev.data === 'object' && typeof ev.data.type === 'string';
}

/** Trailing debounce. `cancel()` drops a pending call, for when the frame goes away. */
export function debounce(fn, ms) {
  let timer = 0;
  const run = (...args) => { clearTimeout(timer); timer = setTimeout(() => { timer = 0; fn(...args); }, ms); };
  run.cancel = () => { clearTimeout(timer); timer = 0; };
  return run;
}

const $ = (id) => document.getElementById(id);

/** No music: the author is listening to their own file. Effects stay on. */
export const PREVIEW_URL = raceUrl(0);
export const HINT = 'what the run looks like';
/** The frame boots a whole 3D world from cold, which is slower than an edit. */
export const WAIT_MS = READY_TIMEOUT_MS * 3;
export const ASPECT_W = 16, ASPECT_H = 9;
export const PAD = 8;

/** Letterbox: the biggest ASPECT_W:ASPECT_H box that fits, centred, whole pixels. */
export function fitBox(width, height, aw = ASPECT_W, ah = ASPECT_H, pad = PAD) {
  const w = Math.max(0, width - pad), h = Math.max(0, height - pad);
  const s = Math.min(w / aw, h / ah);
  return { w: Math.max(0, Math.floor(aw * s)), h: Math.max(0, Math.floor(ah * s)) };
}

/** True when this browser can draw the race at all. No context, no preview. */
export function canDraw(make) {
  try {
    const c = make ? make() : document.createElement('canvas');
    return !!(c.getContext('webgl2') || c.getContext('webgl'));
  } catch (e) { return false; }
}

export function install(api) {
  const S = api.state;
  let frame = null, box = null, note = null, guard = null;
  let live = false, on = false, waitTimer = 0, lastClock = 0, lastPlaying = false, offMessage = null;

  const say = (text) => { if (note) { note.textContent = text || ''; note.hidden = !text; } };

  function post(msg) {
    if (!frame || !frame.contentWindow) return;
    try { frame.contentWindow.postMessage(msg, location.origin); }
    catch (e) { say('the preview would not take that: ' + ((e && e.message) || e)); }
  }

  /** The chart the frame runs is exactly the file "save track" writes. */
  function postChart() {
    if (!live || !S.audio || !S.durationSec) return;
    try { post({ type: 'chart', chart: buildChart(S) }); }
    catch (e) { say('that track would not build: ' + ((e && e.message) || e)); }
  }
  const postChartSoon = debounce(postChart, CHART_DEBOUNCE_MS);

  function onMessage(ev) {
    if (!frame || !accepts(ev, location.origin, frame.contentWindow)) return;
    if (ev.data.type !== 'race-ready') return;
    clearTimeout(waitTimer); waitTimer = 0;
    live = true;
    say('');
    postChart();
    post(clockMessage(api.time(), false));
  }

  /** The frame is a picture, not a toy: whatever it does, the keys stay here. */
  function keepFocus() {
    if (frame && document.activeElement === frame) { try { frame.blur(); } catch (e) { /* gone */ } window.focus(); }
  }

  function fit() {
    if (!box || !box.parentElement) return;
    const r = box.parentElement.getBoundingClientRect();
    const f = fitBox(r.width, r.height);
    box.style.width = f.w + 'px';
    box.style.height = f.h + 'px';
  }

  /** The first road turns the row on. Nothing above is built until then. */
  function ensure() {
    if (on) return;
    on = true;
    const host = $('preview');
    $('pvg').hidden = false;
    note = document.createElement('p');
    note.className = 'pvnote';
    if (!canDraw()) { host.replaceChildren(note); say('no preview here. this browser cannot draw the race.'); return; }
    box = document.createElement('div');
    box.className = 'pvbox';
    frame = document.createElement('iframe');
    frame.className = 'pvframe';
    frame.title = 'the race';
    frame.tabIndex = -1;
    frame.setAttribute('allow', 'autoplay');
    frame.setAttribute('referrerpolicy', 'same-origin');
    frame.src = PREVIEW_URL;
    guard = document.createElement('div');
    guard.className = 'pvguard';
    guard.title = 'watch only. pop out to drive it.';
    guard.addEventListener('pointerdown', (ev) => { ev.preventDefault(); keepFocus(); window.focus(); });
    box.append(frame, guard);
    host.replaceChildren(box, note);
    say('the road is drawing');
    window.addEventListener('message', onMessage);
    offMessage = () => window.removeEventListener('message', onMessage);
    waitTimer = setTimeout(() => {
      waitTimer = 0;
      if (live) return;
      host.replaceChildren(note);
      if (offMessage) { offMessage(); offMessage = null; }
      frame = null; box = null; guard = null;
      say('no preview here. the race never answered.');
    }, WAIT_MS);
    fit();
    if (window.ResizeObserver) new ResizeObserver(fit).observe(host);
    else window.addEventListener('resize', fit);
  }

  /* ---- what the maker tells it ----------------------------------------- */

  const onEdit = () => { if (live) postChartSoon(); };
  /** 4 Hz while it plays, and the truth at once when play or pause changes. */
  function clock(t, playing) {
    if (!live) return;
    keepFocus();
    const now = performance.now();
    if (!!playing === lastPlaying && now - lastClock < CLOCK_MS) return;
    lastClock = now; lastPlaying = !!playing;
    post(clockMessage(t, playing));
  }
  function seek(t, playing) {
    if (!live) return;
    lastClock = performance.now(); lastPlaying = !!playing;
    post(seekMessage(t, playing));
  }

  function popOut() {
    if (!S.audio || !S.durationSec) return api.status('generate a track first');
    let url = '';
    try {
      const json = JSON.stringify(buildChart(S));
      url = URL.createObjectURL(new Blob([json], { type: 'application/json' }));
    } catch (e) { return api.status('that track would not build: ' + ((e && e.message) || e)); }
    window.open('../race.html?chart=' + encodeURIComponent(url) + '&autostart=1&intro=0&pixel=0&music=0', '_blank');
    api.status('popped out. that tab runs the track on its own clock.');
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  }

  $('pvpop').addEventListener('click', popOut);
  $('pvg').hidden = true;

  return { ensure, onEdit, clock, seek, popOut, isOn: () => on, isLive: () => live };
}
