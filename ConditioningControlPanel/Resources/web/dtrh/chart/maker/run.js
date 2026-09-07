/* ============================================================================
 * chart/maker/run.js - the run, whole, on one line (chart/MAKER.md, PR M7).
 *
 * The three lines under the ruler zoom and scroll, which is right for placing
 * a bubble on a word and wrong for answering "where are we in the file". This
 * line answers that: the whole track from 0 to the end at one scale, a small
 * EMI walking it left to right on the audio's clock, and every bubble and wall
 * the author placed drawn where the run will actually meet it. Click or drag
 * anywhere on it and the audio goes there, EMI with it.
 *
 * "Where the run meets it" is the point: the marks sit at `t + offsetSec`,
 * the one knob this file adds. When every effect lands a beat early or late
 * against the voice, the offset slides all of them at once, in the file the
 * maker writes and in the preview, and this line shows the result.
 *
 * Per frame it writes one transform, EMI's. The marks are a canvas repainted
 * on an edit or a view change, never on the clock. The pure half (where a time
 * lands, the tick step, the offset's wording) runs under chart/smoke/maker-check.mjs.
 * ==========================================================================*/

import { KINDS, clamp, fmtShort } from './model.js';

export const PAD = 26;                  // EMI is 52 px wide and stands centred on the time
export const OFFSET_STEP = 0.05, OFFSET_BIG = 0.5, OFFSET_MAX = 5;
export const TICK_MIN_PX = 110;
const TICKS = [5, 10, 15, 30, 60, 120, 300, 600, 900, 1800];
/* maker.css tokens, by value: a canvas cannot read a custom property without
   asking the page for computed style, and nothing in a draw path reads layout (MAKER.md). */
const INK = { road: '#34325a', sub: '#ff8fcf', flash: '#fff6fb', drain: '#9d7bff', pink: '#ff69b4', wall: '#ff4fa5', view: 'rgba(176,128,255,.18)' };

/* ---- pure ---------------------------------------------------------------- */

/** Where a time lands on a lane `width` wide, EMI's own width kept clear at both ends. */
export function runX(t, durationSec, width, pad = PAD) {
  const inner = Math.max(1, width - 2 * pad);
  return pad + clamp(durationSec > 0 ? t / durationSec : 0, 0, 1) * inner;
}
/** The time under a pixel, the inverse of runX. */
export function runT(x, durationSec, width, pad = PAD) {
  const inner = Math.max(1, width - 2 * pad);
  return clamp((x - pad) / inner, 0, 1) * (durationSec || 0);
}
/** The ruler step that keeps labels at least TICK_MIN_PX apart on this lane. */
export function tickStep(durationSec, width, pad = PAD) {
  const inner = Math.max(1, width - 2 * pad);
  const want = (TICK_MIN_PX / inner) * (durationSec || 0);
  return TICKS.find((s) => s >= want) || TICKS[TICKS.length - 1];
}
/** A rounded offset, held inside +-OFFSET_MAX. */
export const clampOffset = (v) => Number(clamp(Number(v) || 0, -OFFSET_MAX, OFFSET_MAX).toFixed(2));
/** What the panel says about the offset. Positive is late: the effects land after the word. */
export function offsetLine(sec) {
  const v = clampOffset(sec);
  if (!v) return 'on time';
  return Math.abs(v).toFixed(2).replace(/0$/, '') + ' s ' + (v > 0 ? 'late' : 'early');
}

/* ---- the lane ------------------------------------------------------------ */

const $ = (id) => document.getElementById(id);
let S = null, api = null, lane, cv, emi, ticksEl, W = 0, lastX = -1, scrub = false;

export function init(state) {
  S = state;
  lane = $('run'); cv = $('runmarks'); emi = $('runemi'); ticksEl = $('runticks');
  W = lane.clientWidth;
}

function drawTicks(dur) {
  const f = document.createDocumentFragment();
  const step = tickStep(dur, W);
  for (let t = 0; t <= dur; t += step) {
    const k = document.createElement('span');
    k.className = 'tick';
    k.style.left = runX(t, dur, W).toFixed(1) + 'px';
    k.textContent = fmtShort(t);
    f.append(k);
  }
  ticksEl.replaceChildren(f);
}

function drawMarks(dur, view) {
  const dpr = window.devicePixelRatio || 1;
  const w = cv.clientWidth, h = cv.clientHeight;
  if (!w || !h) return;
  if (cv.width !== Math.round(w * dpr) || cv.height !== Math.round(h * dpr)) { cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr); }
  const ctx = cv.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  const y = h - 9;                                   // the road EMI walks on
  if (view && dur) {                                 // the window the lines below are showing
    const a = runX(view.t0, dur, w), b = runX(view.t1, dur, w);
    ctx.fillStyle = INK.view;
    ctx.fillRect(a, 0, Math.max(2, b - a), h);
  }
  ctx.fillStyle = INK.road;
  ctx.fillRect(PAD, y, Math.max(0, w - 2 * PAD), 2);
  const off = S.offsetSec || 0;
  for (const b of S.bubs) {
    const x = runX(b.t + off, dur, w);
    if (b.kind === 'wall') {
      ctx.fillStyle = INK.wall;
      ctx.fillRect(x - 2, y - 22, 4, 22);
    } else {
      ctx.fillStyle = INK[KINDS[b.kind].cls] || INK.pink;
      ctx.beginPath(); ctx.arc(x, y - 4, 3.5, 0, Math.PI * 2); ctx.fill();
    }
  }
}

/** The whole lane, once. Called from timeline.render, so a pan or an edit repaints it. */
export function draw(view) {
  if (!S || !lane) return;
  W = lane.clientWidth;
  const dur = S.durationSec || 0;
  lane.classList.toggle('empty', !dur);
  drawTicks(dur);
  drawMarks(dur, view);
  lastX = -1;
}

/** The one thing here that moves per frame. */
export function moveEmi(t) {
  if (!emi || !S) return;
  const x = runX(t, S.durationSec || 0, W);
  if (x === lastX) return;
  lastX = x;
  emi.style.transform = 'translateX(' + (x - PAD).toFixed(1) + 'px)';
}

/* ---- the offset ---------------------------------------------------------- */

export function setOffset(v, say = false) {
  S.offsetSec = clampOffset(v);
  $('offv').textContent = offsetLine(S.offsetSec);
  $('offv').classList.toggle('on', !!S.offsetSec);
  api.render();
  if (say) api.status(S.offsetSec ? 'everything lands ' + offsetLine(S.offsetSec) + '. the file and the preview follow.' : 'back on time.');
}

/** A jump on this line is a jump on the lines below too: they scroll to show it. */
function seekAt(clientX) {
  const r = lane.getBoundingClientRect();
  const t = runT(clientX - r.left, S.durationSec || 0, r.width);
  api.seekTo(t);
  api.reveal(t);
}

export function install(theApi) {
  api = theApi;
  lane.addEventListener('pointerdown', (ev) => {
    if (ev.button !== 0 || !S.durationSec) return;
    scrub = true;
    lane.setPointerCapture(ev.pointerId);
    seekAt(ev.clientX);
  });
  lane.addEventListener('pointermove', (ev) => { if (scrub) seekAt(ev.clientX); });
  const done = () => { scrub = false; };
  lane.addEventListener('pointerup', done);
  lane.addEventListener('pointercancel', done);
  $('offm').addEventListener('click', (ev) => setOffset(S.offsetSec - (ev.shiftKey ? OFFSET_BIG : OFFSET_STEP), true));
  $('offp').addEventListener('click', (ev) => setOffset(S.offsetSec + (ev.shiftKey ? OFFSET_BIG : OFFSET_STEP), true));
  $('offv').addEventListener('click', () => setOffset(0, true));
  document.addEventListener('keydown', (ev) => {
    if (ev.target && /^(INPUT|TEXTAREA)$/.test(ev.target.tagName)) return;
    if (api.modal && api.modal()) return;
    if (ev.ctrlKey || ev.metaKey || ev.altKey) return;
    if (ev.code === 'BracketLeft') { ev.preventDefault(); setOffset(S.offsetSec - (ev.shiftKey ? OFFSET_BIG : OFFSET_STEP), true); }
    else if (ev.code === 'BracketRight') { ev.preventDefault(); setOffset(S.offsetSec + (ev.shiftKey ? OFFSET_BIG : OFFSET_STEP), true); }
  });
  setOffset(S.offsetSec || 0);
}
