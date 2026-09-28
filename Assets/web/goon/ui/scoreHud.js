// The live score HUD for the points model (owner, 2026-09-24).
//
//   (o) 412 ===pink===|==violet== 388 (o)      a slim pill, top centre, each face beside its number
//            x1.30   COMBO x6 ▮▮▮▯
//
// Juice (owner, 2026-09-24): every award sparks at its source, big ones punch the number and
// run a sheen down the bar, their tick steps echo on their side, and a change of lead throws
// gold sparks off the knot. All through juiceDom (burst counts shrink on lite, calm = none).
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
import { S } from './strings.js';
import { POP_EVENT } from '../exec/bubbles.js';
import { FLASH_POP_EVENT } from '../exec/flashes.js';
import { localMonotonicMs } from '../core/clock.js';
import { isCalm, burst, squash } from './juiceDom.js';
import { avatarNode } from './avatar.js';

/** Particle tints per award (rgb triplets, the juiceDom burst shape; never white). */
const TINT = Object.freeze({ pop: '127, 255, 212', hit: '255, 140, 200', held: '201, 181, 255', duel: '255, 212, 94', them: '170, 140, 255' });

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
.gg-sh{position:fixed;left:50%;top:12px;transform:translateX(-50%);z-index:40;pointer-events:none;
  display:flex;flex-direction:column;align-items:center;gap:2px;font-family:var(--gg-font,system-ui);
  transition:opacity .34s cubic-bezier(.2,1.5,.4,1),transform .34s cubic-bezier(.2,1.5,.4,1)}
.gg-sh.is-off{opacity:0;transform:translateX(-50%) translateY(-14px) scale(.92)}
.gg-sh-row{display:flex;align-items:center;gap:6px;padding:3px 6px;border-radius:16px;
  background:rgba(20,8,32,.42);box-shadow:0 0 0 1px rgba(255,255,255,.07)}
.gg-sh-ava{flex:none;display:flex}
.gg-sh .gg-ava{--gg-ava-size:22px}
.gg-sh-num{min-width:36px;font-size:15px;font-weight:800;color:#fff;letter-spacing:.3px;line-height:1;
  text-shadow:0 1px 0 rgba(0,0,0,.4),0 0 10px rgba(255,105,180,.35);font-variant-numeric:tabular-nums}
.gg-sh-num--you{text-align:right;color:#ffd1ea}
.gg-sh-num--them{text-align:left;color:#d9c8ff;text-shadow:0 1px 0 rgba(0,0,0,.4),0 0 10px rgba(160,120,255,.35)}
.gg-sh-bar{position:relative;width:min(150px,22vw);height:6px;border-radius:4px;
  background:#8a6bff;box-shadow:inset 0 1px 2px rgba(0,0,0,.35),0 0 0 1px rgba(255,255,255,.12)}
.gg-sh-fill{position:absolute;left:0;top:0;bottom:0;width:50%;border-radius:4px 0 0 4px;
  background:linear-gradient(90deg,#ff4fa8,#ff8cc8)}
.gg-sh-sheen{position:absolute;inset:0;border-radius:4px;overflow:hidden;pointer-events:none}
.gg-sh-sheen>i{position:absolute;top:0;bottom:0;width:30%;left:-30%;
  background:linear-gradient(90deg,transparent,rgba(255,255,255,.6),transparent)}
.gg-sh-knot{position:absolute;top:-3px;width:4px;height:12px;margin-left:-2px;border-radius:2px;left:50%;
  background:#fff;box-shadow:0 0 8px rgba(255,255,255,.85)}
.gg-sh-sub{display:flex;align-items:center;gap:6px;min-height:16px}
.gg-sh-mult{font-size:10px;font-weight:800;color:#ffd45e;padding:0 6px;border-radius:8px;
  background:rgba(40,20,60,.6);box-shadow:0 0 0 1px rgba(255,212,94,.35)}
.gg-sh-combo{display:flex;align-items:center;gap:5px;font-size:11px;font-weight:800;color:#7fffd4;
  opacity:0;transform:scale(.7);transition:opacity .2s ease-out,transform .25s cubic-bezier(.2,1.5,.4,1)}
.gg-sh-combo.is-on{opacity:1;transform:scale(1)}
.gg-sh-drain{width:40px;height:3px;border-radius:2px;background:rgba(127,255,212,.25);overflow:hidden}
.gg-sh-drain>i{display:block;height:100%;width:100%;background:#7fffd4;transform-origin:left}
.gg-sh-float{position:fixed;z-index:41;pointer-events:none;font-family:var(--gg-font,system-ui);
  font-weight:800;font-size:15px;color:#ffe08a;text-shadow:0 2px 0 rgba(0,0,0,.4);white-space:nowrap;
  transform:translate(-50%,-50%)}
.gg-sh-float small{font-size:10px;margin-right:3px;opacity:.85}
.gg-sh-float--hit{color:#ff8cc8;font-size:18px}
.gg-sh-float--held{color:#c9b5ff}
.gg-sh-float--duel{color:#ffd45e;font-size:22px}
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
export function mountScoreHud({ match, host = null, audio = null } = {}) {
  const d = typeof document !== 'undefined' ? document : null;
  const w = typeof window !== 'undefined' ? window : null;
  if (!d || !match) return { unmount() {} };
  injectCss(d);

  const root = mk(d, 'div', 'gg-sh is-off');
  const row = root.appendChild(mk(d, 'div', 'gg-sh-row'));
  const youAvaBox = row.appendChild(mk(d, 'div', 'gg-sh-ava'));
  const youNum = row.appendChild(mk(d, 'div', 'gg-sh-num gg-sh-num--you', '0'));
  const bar = row.appendChild(mk(d, 'div', 'gg-sh-bar'));
  const fill = bar.appendChild(mk(d, 'div', 'gg-sh-fill'));
  const sheen = bar.appendChild(mk(d, 'div', 'gg-sh-sheen')).appendChild(mk(d, 'i'));
  const knot = bar.appendChild(mk(d, 'div', 'gg-sh-knot'));
  const themNum = row.appendChild(mk(d, 'div', 'gg-sh-num gg-sh-num--them', '0'));
  const themAvaBox = row.appendChild(mk(d, 'div', 'gg-sh-ava'));

  /* The two faces. A picture when the HUD's own minis have one (Discord share), else the
   * initial bubble. Re-read once a second: names and pictures land after the hello. */
  const faces = { you: { name: null, uri: null, node: null }, opp: { name: null, uri: null, node: null } };
  function syncFace(side, box, name) {
    const f = faces[side];
    let uri = null;
    try {
      const img = d.querySelector('.gg-ava--mini[data-side="' + side + '"] img');
      uri = img && typeof img.src === 'string' && img.src.slice(0, 5) === 'data:' ? img.src : null;
    } catch (_e) { uri = null; }
    const n = String(name || '');
    if (f.node && f.name === n && f.uri === uri) return;
    const first = !f.node;
    f.name = n; f.uri = uri;
    const node = avatarNode({ side, name: n, dataUri: uri, size: 'score' });
    if (!node) return;
    try { box.replaceChildren(node); } catch (_e) { box.appendChild(node); }
    f.node = node;
    if (!first && !isCalm()) squash(node, { amount: 0.2, ms: 260 });
  }
  function syncFaces() {
    let youName = '', themName = '';
    try { youName = match.localDisplayName || ''; } catch (_e) { /* torn down */ }
    try { themName = (match.opponent && match.opponent.displayName) || ''; } catch (_e) { /* torn down */ }
    syncFace('you', youAvaBox, youName);
    syncFace('opp', themAvaBox, themName);
  }
  syncFaces();
  const sub = root.appendChild(mk(d, 'div', 'gg-sh-sub'));
  const mult = sub.appendChild(mk(d, 'div', 'gg-sh-mult', 'x1.00'));
  const combo = sub.appendChild(mk(d, 'div', 'gg-sh-combo'));
  const comboTxt = combo.appendChild(mk(d, 'span', null, ''));
  const drain = combo.appendChild(mk(d, 'div', 'gg-sh-drain'));
  const drainFill = drain.appendChild(mk(d, 'i'));
  (d.body || d.documentElement).appendChild(root);

  let shownYou = 0, shownThem = 0, shownShare = 0.5, on = false, lastFrame = 0, lastEmit = 0, lastHeatSent = -1;
  let raf = 0, alive = true;
  let lastFaceSync = 0, seenThem = -1, leaderSeen = 0;
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

  /* THE COMBO BLIP rides the Goon audio's GAME bus (ui/audio.js pluck), so the Game
   * sounds slider, the master and mute own it like every other cue. No audio handed
   * in = no blip: this module never opens its own context to the speakers. */
  function blip(step) {
    try {
      if (audio && typeof audio.pluck === 'function') {
        audio.pluck(ROOT_HZ * Math.pow(2, comboSemis(step) / 12), { gain: COMBO_GAIN, ms: 160 });
      }
    } catch (_e) { /* sound is never load-bearing */ }
  }

  function onAward(a) {
    if (!a || !(a.points > 0) || !on) return;
    const at = a.type === 'pop' ? lastPop
      : a.type === 'hit' ? centreOf(themNum)
        : a.type === 'held' ? centreOf(youNum)
          : centreOf(bar);
    // Beside a number, never over it: start under it and rise past it.
    // Outboard of the pill (past the face), never on the sub row: your side left, theirs right.
    const out = a.type === 'hit' ? 74 : a.type === 'held' ? -74 : 0;
    float(a, a.type === 'pop' ? at : { x: at.x + out, y: at.y + (out ? 4 : 26) });
    if (a.type === 'pop') {
      // A pop sparks where it happened; a long combo sparks harder.
      burst(at.x, at.y, { count: a.combo >= 5 ? 10 : 5, color: TINT.pop, dist: 34 + Math.min(10, a.combo | 0) * 3, life: 420, sizeMin: 3, sizeMax: 6 });
      return;
    }
    const big = a.type === 'hit' || a.type === 'duel';
    const mine = centreOf(youNum);
    burst(mine.x, mine.y, { count: big ? 12 : 6, color: TINT[a.type] || TINT.held, dist: big ? 44 : 28, life: big ? 560 : 420, sizeMin: 3, sizeMax: big ? 7 : 5 });
    punch(youNum, big ? 1.28 : 1.12);
    if (faces.you.node && big) squash(faces.you.node, { amount: 0.18, ms: 240 });
    if (big) sweep();
  }

  /** A number (or face) kicks. The one scale punch every award shares. */
  function punch(node, to) {
    if (isCalm()) return;
    try { node.animate([{ transform: 'scale(1)' }, { transform: `scale(${to})` }, { transform: 'scale(1)' }], { duration: 300, easing: 'cubic-bezier(.2,1.5,.4,1)' }); } catch (_e) { /* chrome */ }
  }

  /** A light sheen runs down the bar. */
  function sweep() {
    if (isCalm()) return;
    try { sheen.animate([{ left: '-30%' }, { left: '100%' }], { duration: 520, easing: 'cubic-bezier(.4,0,.2,1)' }); } catch (_e) { /* chrome */ }
  }

  /** The lead changed hands: gold sparks off the knot, the bar flashes and shivers. */
  function leadFlip(mineNow) {
    const k = centreOf(knot);
    burst(k.x, k.y, { count: 16, color: mineNow ? TINT.duel : TINT.them, dist: 46, life: 640, sizeMin: 3, sizeMax: 7, shape: 'dot' });
    sweep();
    if (!isCalm()) {
      try { bar.animate([{ transform: 'translateX(0)' }, { transform: 'translateX(-3px)' }, { transform: 'translateX(3px)' }, { transform: 'translateX(0)' }], { duration: 250 }); } catch (_e) { /* chrome */ }
    }
    punch(mineNow ? youNum : themNum, 1.3);
  }

  /** The opponent scored (their tick moved): a smaller echo on their side. */
  function theirGain(delta) {
    const t = centreOf(themNum);
    burst(t.x, t.y, { count: delta >= 10 ? 9 : 4, color: TINT.them, dist: delta >= 10 ? 36 : 22, life: 440, sizeMin: 3, sizeMax: 5 });
    punch(themNum, delta >= 10 ? 1.2 : 1.08);
    if (delta >= 10 && faces.opp.node) squash(faces.opp.node, { amount: 0.16, ms: 240 });
  }

  function onPop(ev) {
    const det = (ev && ev.detail) || {};
    lastPop.x = Number(det.x) || 0;
    lastPop.y = Number(det.y) || 0;
    let a = null;
    try { a = match.notePop(localMonotonicMs()); } catch (_e) { a = null; }
    if (!a) return;
    if (ev && ev.type === FLASH_POP_EVENT) blip(a.combo);
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
    if (now - lastFaceSync >= 1000) { lastFaceSync = now; syncFaces(); }
    // Their score only moves on their tick: each step up is an echo on their side.
    if (seenThem >= 0 && them - seenThem >= 1) theirGain(them - seenThem);
    seenThem = them;
    // Lead changes hands (ignore the dead-even opening and tiny early leads).
    const leader = me - them >= 5 ? 1 : them - me >= 5 ? -1 : leaderSeen;
    if (leader !== leaderSeen && leaderSeen !== 0 && leader !== 0) leadFlip(leader > 0);
    leaderSeen = leader;
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
      comboTxt.textContent = S.desk.combo(liveCombo);
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
