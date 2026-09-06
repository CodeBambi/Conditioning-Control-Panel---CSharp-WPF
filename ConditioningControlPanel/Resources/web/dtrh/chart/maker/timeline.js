/* ============================================================================
 * chart/maker/timeline.js - three lines, one time axis (MAKER.md, PR M1).
 *
 * Everything on screen is a function of `view` (t0 and pixels per second) and
 * the state; nothing is a function of the clock. That is the whole performance
 * story: the rows are rebuilt only when the view or the track changes, the
 * waveform is repainted only then too, and per frame the page writes exactly
 * two things, the playhead transform and the time, and the time only when the
 * string it would write is different from the one already there. Only what is
 * on screen is built, so a forty minute file has the same row count as a one
 * minute one.
 * ==========================================================================*/

import { KINDS, EFFECTS, fmt, fmtShort, groupsOf, clamp } from './model.js';

export const GUTTER = 120;              // the gutter column, in px (maker.css --gutter)
export const PPS_LO = 6, PPS_HI = 120;
export const view = { t0: 0, pps: 30 };

const TICKS = [0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600];
const el = (id) => document.getElementById(id);
let S = null, W = 0, raf = 0;
let ruler, seqs, wordsRow, wave, head, timeEl, showEl;
let headOn = true, lastTime = '';

export const xOf = (t) => (t - view.t0) * view.pps;
export const tOf = (x) => view.t0 + x / view.pps;
export const width = () => W;
export const spanSec = () => W / view.pps;

export function init(state) {
  S = state;
  ruler = el('ruler'); seqs = el('seqs'); wordsRow = el('words'); wave = el('wave');
  head = el('playhead'); timeEl = el('time'); showEl = el('showing');
  W = seqs.clientWidth;
  window.addEventListener('resize', () => { W = seqs.clientWidth; render(); });
}

/** At most one rebuild per frame, however many wheel events landed in it. */
export function scheduleRender() {
  if (raf) return;
  raf = requestAnimationFrame(() => { raf = 0; render(); });
}

export function setView(t0) {
  const max = Math.max(0, (S.durationSec || 0) - spanSec() * 0.5);
  view.t0 = clamp(t0, 0, max);
  scheduleRender();
}
export function panBy(dt) { setView(view.t0 + dt); }
export function zoomAt(factor, clientX) {
  const r = seqs.getBoundingClientRect();
  const x = clamp((clientX == null ? r.width / 2 : clientX - r.left), 0, r.width);
  const t = tOf(x);
  const next = clamp(view.pps * factor, PPS_LO, PPS_HI);
  if (next === view.pps) return;
  view.pps = next;
  view.t0 = Math.max(0, t - x / view.pps);
  scheduleRender();
}

/* ---- the rows ------------------------------------------------------------ */

function tickStep() {
  const want = 90 / view.pps;
  return TICKS.find((s) => s >= want) || TICKS[TICKS.length - 1];
}

function drawRuler() {
  const f = document.createDocumentFragment(), step = tickStep();
  for (let t = Math.ceil(view.t0 / step) * step; xOf(t) < W; t += step) {
    if (t < 0) continue;
    const k = document.createElement('span');
    k.className = 'tick';
    k.style.left = xOf(t).toFixed(1) + 'px';
    k.textContent = fmtShort(t);
    f.append(k);
  }
  ruler.replaceChildren(f);
}

function drawBubbles() {
  const f = document.createDocumentFragment();
  const wallW = 30 / view.pps;
  // the road first, so it sits under everything the author can actually grab. These are not
  // pickable and never will be: they belong to generate.js, not to the hand (MAKER.md, M5).
  for (const e of (S.road && S.road.events) || []) {
    const x = xOf(e.t);
    if (x < -10 || x > W + 10) continue;
    const d = document.createElement('i');
    d.className = 'road ' + e.kind;
    d.style.left = x.toFixed(1) + 'px';
    d.title = e.kind + (e.label ? ' ' + e.label : '');
    f.append(d);
  }
  for (const [g, list] of groupsOf(S.bubs)) {
    const last = list[list.length - 1];
    const t0 = list[0].t, t1 = last.t + (last.kind === 'wall' ? wallW : 0);
    if (xOf(t1) < -80 || xOf(t0) > W + 80) continue;
    const b = document.createElement('div');
    b.className = 'band' + (list.every((x) => S.sel.has(x.id)) ? ' sel' : '');
    b.dataset.group = g;
    b.style.left = (xOf(t0) - 22).toFixed(1) + 'px';
    b.style.width = Math.max(80, xOf(t1) - xOf(t0) + 44).toFixed(1) + 'px';
    const lbl = document.createElement('span');
    lbl.className = 'lbl';
    lbl.textContent = (S.setById.get(list[0].trig) || {}).name || 'wall';
    const a = document.createElement('span');
    a.className = 'anchor';
    b.append(lbl, a);
    f.append(b);
  }
  for (const b of S.bubs) {
    const x = xOf(b.t);
    if (x < -60 || x > W + 60) continue;
    const d = document.createElement('div');
    const kind = b.kind === 'wall' ? 'wall' : KINDS[b.kind].cls;
    d.className = 'bub ' + kind + (S.sel.has(b.id) ? ' sel' : '');
    d.dataset.id = b.id;
    d.style.left = x.toFixed(1) + 'px';
    if (b.kind === 'wall') {
      const fx = EFFECTS[b.eff] || EFFECTS.melt;
      d.textContent = fx[0];
      d.dataset.name = fx[1];
      d.title = 'wall: ' + fx[1] + '. click it again to change';
      d.style.transform = 'none';
    } else {
      d.textContent = KINDS[b.kind].glyph;
      d.title = KINDS[b.kind].name;
    }
    f.append(d);
  }
  seqs.replaceChildren(f);
}

function drawTags() {
  const f = document.createDocumentFragment();
  let row = 0, prevRight = -1e9;
  for (const h of S.hits) {
    const x = xOf(h.t);
    if (x < -120 || x > W + 120) continue;
    const set = S.setById.get(h.setId) || { name: h.setId, color: '#aaa5cc' };
    const t = document.createElement('span');
    t.className = 'tag' + (S.sel.has(h.id) ? ' sel' : '');
    t.dataset.id = h.id;
    t.textContent = set.name;
    const w = set.name.length * 9 + 24;
    row = x - w / 2 < prevRight ? 1 - row : 0;
    prevRight = x + w / 2;
    t.style.left = x.toFixed(1) + 'px';
    t.style.top = (row ? 33 : 6) + 'px';
    t.style.background = set.color + '2e';
    t.style.color = set.color;
    f.append(t);
  }
  wordsRow.replaceChildren(f);
}

function drawWave() {
  const dpr = window.devicePixelRatio || 1;
  const w = wave.clientWidth, h = wave.clientHeight;
  if (!w || !h) return;
  if (wave.width !== Math.round(w * dpr) || wave.height !== Math.round(h * dpr)) {
    wave.width = Math.round(w * dpr); wave.height = Math.round(h * dpr);
  }
  const ctx = wave.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  ctx.strokeStyle = '#2b2a48';
  ctx.beginPath(); ctx.moveTo(0, h / 2 + 0.5); ctx.lineTo(w, h / 2 + 0.5); ctx.stroke();
  const peaks = S.peaks;
  if (peaks) {
    const per = S.perSec || 50, bins = peaks.length / 2, half = h / 2 - 4;
    const g = ctx.createLinearGradient(0, 0, 0, h);
    g.addColorStop(0, '#78ffbe'); g.addColorStop(0.5, '#4fd6c8'); g.addColorStop(1, '#78ffbe');
    ctx.fillStyle = g;
    for (let x = 0; x < w; x++) {
      let b0 = Math.floor(tOf(x) * per), b1 = Math.floor(tOf(x + 1) * per);
      if (b1 <= b0) b1 = b0 + 1;
      if (b1 <= 0 || b0 >= bins) continue;
      b0 = Math.max(0, b0); b1 = Math.min(bins, b1);
      let lo = 0, hi = 0;
      for (let b = b0; b < b1; b++) {
        const a = peaks[b * 2], c = peaks[b * 2 + 1];
        if (a < lo) lo = a;
        if (c > hi) hi = c;
      }
      const top = h / 2 - hi * half, bot = h / 2 - lo * half;
      ctx.fillRect(x, top, 1, Math.max(1, bot - top));
    }
  }
  ctx.fillStyle = 'rgba(255,105,180,.25)';
  for (const hit of S.hits) {
    const x = xOf(hit.t);
    if (x < -2 || x > w + 2) continue;
    ctx.fillRect(x - 1, 0, 2, h);
  }
}

function drawShowing() {
  const dur = S.durationSec || 0;
  showEl.textContent = dur
    ? 'showing ' + fmtShort(Math.max(0, view.t0)) + ' to ' + fmtShort(Math.min(dur, view.t0 + spanSec())) + ' of ' + fmtShort(dur)
    : 'nothing loaded';
}

/** The whole picture, once. Called on a view change or a track change, never per frame. */
export function render() {
  if (!S) return;
  W = seqs.clientWidth;
  drawRuler(); drawBubbles(); drawTags(); drawWave(); drawShowing();
}

/* ---- the one thing that moves per frame ---------------------------------- */

export function moveHead(t) {
  const x = xOf(t);
  head.style.transform = 'translateX(' + (GUTTER + x).toFixed(1) + 'px)';
  const on = x >= -4 && x <= W + 4;
  if (on !== headOn) { head.style.visibility = on ? '' : 'hidden'; headOn = on; }
  const s = fmt(t);
  if (s !== lastTime) { timeEl.textContent = s; lastTime = s; }
}

/** True when the playhead has walked far enough right that the view should follow. */
export function shouldFollow(t) { return xOf(t) > W * 0.85 || xOf(t) < 0; }
export function follow(t) { setView(t - (W * 0.15) / view.pps); }
