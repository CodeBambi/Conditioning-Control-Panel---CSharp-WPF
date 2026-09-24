// The live score HUD for the points model (owner, 2026-09-24).
//
//   [ YOU 412 ]  ====pink====|====violet====  [ 388 THEM ]
//            x1.30            COMBO x6 ▮▮▮▯
//
// Both scores face each other top centre with a tug-of-war bar between them, a floating +N at the
// source of every award (a tiny glyph says what paid: pop, hit landed, held, duel), a combo meter
// that climbs a pentatonic ladder and drains after COMBO_GAP_MS, and the one multiplier chip
// (attention x risk, it applies to OWN points only).
//
// It also owns the `heat` signal: 0..1 off your lead and your live combo, smoothed, published as
// a window CustomEvent `gg-heat` {detail:{heat}} at most every HEAT_EMIT_MS and readable any time
// through getHeat(). The background lane subscribes to it.
//
// Legacy matches (an older peer, no points model) never show any of this: mount returns a no-op.
// Every effect has an IN and an OUT; reduced motion (juiceDom isCalm) turns motion into fades.

import { heatTarget, smoothHeat } from '../core/points.js';
import { POP_EVENT } from '../exec/bubbles.js';
import { FLASH_POP_EVENT } from '../exec/flashes.js';
import { localMonotonicMs } from '../core/clock.js';
import { isCalm } from './juiceDom.js';

export const HEAT_EVENT = 'gg-heat';
export const HEAT_EMIT_MS = 100;
export const SCORE_HUD_CLASS = 'gg-hud--points';
/** One glyph per award type, drawn small beside the +N. */
export const AWARD_GLYPH = Object.freeze({ pop: '●', hit: '➶', held: '◉', duel: '★' });
/** Pentatonic ladder (C major pentatonic, semitones from C5) the combo climbs. */
const LADDER = [0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24];
const ROOT_HZ = 523.25;
const COMBO_GAIN = 0.05;

let heatNow = 0;
/** The current heat, 0..1. Safe to call before any HUD mounted (reads 0). */
export function getHeat() { return heatNow; }

/** My share of the tug bar, 0..1. Even (0.5) until somebody scores. */
export function tugShare(mine, theirs) {
  const a = Math.max(0, Number(mine) || 0), b = Math.max(0, Number(theirs) || 0);
  return a + b <= 0 ? 0.5 : a / (a + b);
}

/** Label for a float: '+3', '+24'. Sub-point pops round up so a pop never reads +0. */
export function floatLabel(points) {
  const p = Number(points) || 0;
  return '+' + (p > 0 && p < 1 ? 1 : Math.round(p));
}

/** The semitone a combo step plays: climbs the ladder, holds on its top rung. */
export function comboSemis(combo) {
  const i = Math.max(0, Math.min(LADDER.length - 1, (combo | 0) - 1));
  return LADDER[i];
}

const CSS = `
.gg-sh{position:fixed;left:50%;top:58px;transform:translateX(-50%);z-index:40;pointer-events:none;
  display:flex;flex-direction:column;align-items:center;gap:4px;font-family:var(--gg-font,system-ui);
  transition:opacity .34s cubic-bezier(.2,1.5,.4,1),transform .34s cubic-bezier(.2,1.5,.4,1)}
.gg-sh.is-off{opacity:0;transform:translateX(-50%) translateY(-14px) scale(.92)}
.gg-sh-row{display:flex;align-items:center;gap:10px}
.gg-sh-num{min-width:64px;font-size:26px;font-weight:800;color:#fff;letter-spacing:.5px;
  text-shadow:0 2px 0 rgba(0,0,0,.35),0 0 14px rgba(255,105,180,.35);font-variant-numeric:tabular-nums}
.gg-sh-num--you{text-align:right;color:#ffd1ea}
.gg-sh-num--them{text-align:left;color:#d9c8ff;text-shadow:0 2px 0 rgba(0,0,0,.35),0 0 14px rgba(160,120,255,.35)}
.gg-sh-tag{font-size:10px;font-weight:700;letter-spacing:1.5px;opacity:.7;color:#fff;text-transform:uppercase}
.gg-sh-bar{position:relative;width:min(260px,34vw);height:12px;border-radius:8px;overflow:hidden;
  background:#8a6bff;box-shadow:inset 0 2px 3px rgba(0,0,0,.35),0 0 0 2px rgba(255,255,255,.12)}
.gg-sh-fill{position:absolute;left:0;top:0;bottom:0;width:50%;background:linear-gradient(90deg,#ff4fa8,#ff8cc8)}
.gg-sh-knot{position:absolute;top:-3px;width:6px;height:18px;margin-left:-3px;border-radius:3px;left:50%;
  background:#fff;box-shadow:0 0 10px rgba(255,255,255,.8)}
.gg-sh-sub{display:flex;align-items:center;gap:8px;min-height:20px}
.gg-sh-mult{font-size:12px;font-weight:800;color:#ffd45e;padding:1px 8px;border-radius:10px;
  background:rgba(40,20,60,.6);box-shadow:0 0 0 1px rgba(255,212,94,.35)}
.gg-sh-combo{display:flex;align-items:center;gap:6px;font-size:13px;font-weight:800;color:#7fffd4;
  opacity:0;transform:scale(.7);transition:opacity .2s ease-out,transform .25s cubic-bezier(.2,1.5,.4,1)}
.gg-sh-combo.is-on{opacity:1;transform:scale(1)}
.gg-sh-drain{width:54px;height:4px;border-radius:2px;background:rgba(127,255,212,.25);overflow:hidden}
.gg-sh-drain>i{display:block;height:100%;width:100%;background:#7fffd4;transform-origin:left}
.gg-sh-float{position:fixed;z-index:41;pointer-events:none;font-family:var(--gg-font,system-ui);
  font-weight:800;font-size:18px;color:#ffe08a;text-shadow:0 2px 0 rgba(0,0,0,.4);white-space:nowrap;
  transform:translate(-50%,-50%)}
.gg-sh-float small{font-size:11px;margin-right:3px;opacity:.85}
.gg-sh-float--hit{color:#ff8cc8;font-size:22px}
.gg-sh-float--held{color:#c9b5ff}
.gg-sh-float--duel{color:#ffd45e;font-size:26px}
.${SCORE_HUD_CLASS} .gg-score{visibility:hidden}
`;

function injectCss(d) {
  if (!d || d.getElementById('gg-sh-css')) return;
  const s = d.createElement('style');
  s.id = 'gg-sh-css';
  s.textContent = CSS;
  (d.head || d.documentElement).appendChild(s);
}

function mk(d, tag, cls, txt) {
  const n = d.createElement(tag);
  if (cls) n.className = cls;
  if (txt !== undefined) n.textContent = txt;
  return n;
}

/**
 * @param {object} o
 * @param {object} o.match   GoonMatchService (pointsModel, onPointsAwarded, notePop, scoring, opponent)
 * @param {Element} [o.host] element to toggle SCORE_HUD_CLASS on (the HUD frame)
 * @returns {{unmount:Function}}
 */
export function mountScoreHud({ match, host = null } = {}) {
  const d = typeof document !== 'undefined' ? document : null;
  const w = typeof window !== 'undefined' ? window : null;
  if (!d || !match) return { unmount() {} };
  injectCss(d);

  const root = mk(d, 'div', 'gg-sh is-off');
  const row = root.appendChild(mk(d, 'div', 'gg-sh-row'));
  const youCol = row.appendChild(mk(d, 'div'));
  youCol.appendChild(mk(d, 'div', 'gg-sh-tag', 'you'));
  const youNum = youCol.appendChild(mk(d, 'div', 'gg-sh-num gg-sh-num--you', '0'));
  const bar = row.appendChild(mk(d, 'div', 'gg-sh-bar'));
  const fill = bar.appendChild(mk(d, 'div', 'gg-sh-fill'));
  const knot = bar.appendChild(mk(d, 'div', 'gg-sh-knot'));
  const themCol = row.appendChild(mk(d, 'div'));
  themCol.appendChild(mk(d, 'div', 'gg-sh-tag', 'them'));
  const themNum = themCol.appendChild(mk(d, 'div', 'gg-sh-num gg-sh-num--them', '0'));
  const sub = root.appendChild(mk(d, 'div', 'gg-sh-sub'));
  const mult = sub.appendChild(mk(d, 'div', 'gg-sh-mult', 'x1.00'));
  const combo = sub.appendChild(mk(d, 'div', 'gg-sh-combo'));
  const comboTxt = combo.appendChild(mk(d, 'span', null, ''));
  const drain = combo.appendChild(mk(d, 'div', 'gg-sh-drain'));
  const drainFill = drain.appendChild(mk(d, 'i'));
  (d.body || d.documentElement).appendChild(root);

  let shownYou = 0, shownThem = 0, shownShare = 0.5, on = false, lastFrame = 0, lastEmit = 0, lastHeatSent = -1;
  let raf = 0, alive = true, ac = null;
  const lastPop = { x: 0, y: 0 };

  function active() {
    try { return !!match.pointsModel; } catch (_e) { return false; }
  }

  function setOn(v) {
    if (v === on) return;
    on = v;
    root.classList.toggle('is-off', !v);
    if (host && host.classList) host.classList.toggle(SCORE_HUD_CLASS, v);
  }

  function centreOf(node) {
    try { const r = node.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; }
    catch (_e) { return { x: 0, y: 0 }; }
  }

  function float(a, at) {
    const calm = isCalm();
    const f = mk(d, 'div', 'gg-sh-float gg-sh-float--' + a.type);
    f.appendChild(mk(d, 'small', null, AWARD_GLYPH[a.type] || ''));
    f.appendChild(d.createTextNode(floatLabel(a.points)));
    f.style.left = Math.round(at.x) + 'px';
    f.style.top = Math.round(at.y) + 'px';
    (d.body || d.documentElement).appendChild(f);
    const kill = () => { try { f.remove(); } catch (_e) { /* gone */ } };
    const big = a.type === 'duel' || a.type === 'hit';
    const frames = calm
      ? [{ opacity: 0 }, { opacity: 1, offset: 0.2 }, { opacity: 1, offset: 0.7 }, { opacity: 0 }]
      : [
        { opacity: 0, transform: 'translate(-50%,-50%) scale(.5)' },
        { opacity: 1, transform: `translate(-50%,-70%) scale(${big ? 1.25 : 1.1})`, offset: 0.2 },
        { opacity: 1, transform: 'translate(-50%,-110%) scale(1)', offset: 0.65 },
        { opacity: 0, transform: 'translate(-50%,-150%) scale(.9)' },
      ];
    try {
      const anim = f.animate(frames, { duration: big ? 1100 : 800, easing: 'ease-out', fill: 'forwards' });
      anim.onfinish = kill;
    } catch (_e) { /* no WAAPI: the timer below still clears it */ }
    setTimeout(kill, 1300);
  }

  function blip(step) {
    try {
      const Ctx = w && (w.AudioContext || w.webkitAudioContext);
      if (!Ctx) return;
      if (!ac) ac = new Ctx();
      if (ac.state === 'suspended') ac.resume();
      const t = ac.currentTime;
      const o = ac.createOscillator(), g = ac.createGain();
      o.type = 'triangle';
      o.frequency.value = ROOT_HZ * Math.pow(2, comboSemis(step) / 12);
      g.gain.setValueAtTime(0, t);
      g.gain.linearRampToValueAtTime(COMBO_GAIN, t + 0.008);
      g.gain.exponentialRampToValueAtTime(0.0001, t + 0.16);
      o.connect(g); g.connect(ac.destination);
      o.start(t); o.stop(t + 0.18);
      o.onended = () => { try { o.disconnect(); g.disconnect(); } catch (_e) { /* gone */ } };
    } catch (_e) { /* sound is never load-bearing */ }
  }

  function onAward(a) {
    if (!a || !(a.points > 0) || !on) return;
    const at = a.type === 'pop' ? lastPop
      : a.type === 'hit' ? centreOf(themNum)
        : a.type === 'held' ? centreOf(youNum)
          : centreOf(bar);
    // Beside a number, never over it: start under it and rise past it.
    float(a, a.type === 'pop' ? at : { x: at.x, y: at.y + 34 });
    if (!isCalm()) {
      const target = a.type === 'hit' || a.type === 'duel' ? youNum : null;
      if (target) {
        try { target.animate([{ transform: 'scale(1)' }, { transform: 'scale(1.18)' }, { transform: 'scale(1)' }], { duration: 300, easing: 'cubic-bezier(.2,1.5,.4,1)' }); } catch (_e) { /* chrome */ }
      }
    }
  }

  function onPop(ev) {
    const det = (ev && ev.detail) || {};
    lastPop.x = Number(det.x) || 0;
    lastPop.y = Number(det.y) || 0;
    let a = null;
    try { a = match.notePop(localMonotonicMs()); } catch (_e) { a = null; }
    if (!a) return;
    if (a.combo >= 2) blip(a.combo);
    // A pop past the limiter still climbs the combo, it just floats nothing.
  }

  const off = typeof match.onPointsAwarded === 'function' ? match.onPointsAwarded(onAward) : null;
  d.addEventListener(POP_EVENT, onPop);
  d.addEventListener(FLASH_POP_EVENT, onPop);

  function frame() {
    if (!alive) return;
    raf = (w && w.requestAnimationFrame) ? w.requestAnimationFrame(frame) : setTimeout(frame, 50);
    const now = localMonotonicMs();
    const dt = lastFrame ? Math.min(0.25, (now - lastFrame) / 1000) : 0;
    lastFrame = now;
    setOn(active());
    if (!on) return;
    let me = 0, them = 0, chip = 1, liveCombo = 0;
    try {
      me = match.scoring.scoreExact;
      them = Number(match.opponent.score) || 0;
      chip = match.scoring.ownMultiplier;
      liveCombo = match.scoring.points.comboAt(now);
    } catch (_e) { /* torn down */ }
    const calm = isCalm();
    const k = calm ? 1 : 1 - Math.exp(-dt * 8);
    shownYou += (me - shownYou) * k;
    shownThem += (them - shownThem) * k;
    shownShare += (tugShare(me, them) - shownShare) * (calm ? 1 : 1 - Math.exp(-dt * 4));
    youNum.textContent = String(Math.round(shownYou));
    themNum.textContent = String(Math.round(shownThem));
    const pct = (shownShare * 100).toFixed(1) + '%';
    fill.style.width = pct;
    knot.style.left = pct;
    mult.textContent = 'x' + (Number(chip) || 1).toFixed(2);
    combo.classList.toggle('is-on', liveCombo >= 2);
    if (liveCombo >= 2) {
      comboTxt.textContent = 'COMBO x' + liveCombo;
      let left = 1;
      try { left = match.scoring.points.comboLeft(now); } catch (_e) { /* ignore */ }
      drainFill.style.transform = `scaleX(${left.toFixed(3)})`;
    }
    heatNow = smoothHeat(heatNow, heatTarget(me - them, liveCombo), dt);
    if (now - lastEmit >= HEAT_EMIT_MS && Math.abs(heatNow - lastHeatSent) > 0.004 && w && typeof w.dispatchEvent === 'function') {
      lastEmit = now;
      lastHeatSent = heatNow;
      try { w.dispatchEvent(new CustomEvent(HEAT_EVENT, { detail: { heat: heatNow } })); } catch (_e) { /* ignore */ }
    }
  }
  frame();

  return {
    unmount() {
      if (!alive) return;
      alive = false;
      try { if (w && w.cancelAnimationFrame) w.cancelAnimationFrame(raf); clearTimeout(raf); } catch (_e) { /* ignore */ }
      try { if (typeof off === 'function') off(); } catch (_e) { /* ignore */ }
      d.removeEventListener(POP_EVENT, onPop);
      d.removeEventListener(FLASH_POP_EVENT, onPop);
      if (host && host.classList) host.classList.remove(SCORE_HUD_CLASS);
      heatNow = 0;
      try { if (w) w.dispatchEvent(new CustomEvent(HEAT_EVENT, { detail: { heat: 0 } })); } catch (_e) { /* ignore */ }
      root.classList.add('is-off');
      setTimeout(() => { try { root.remove(); } catch (_e) { /* gone */ } }, 360);
      try { if (ac) ac.close(); } catch (_e) { /* ignore */ }
    },
  };
}
