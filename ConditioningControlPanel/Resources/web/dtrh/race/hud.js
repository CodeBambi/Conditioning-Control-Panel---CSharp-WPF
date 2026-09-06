/* ============================================================================
 * hud.js - The Caucus Race HUD. Implements CONTRACT.md "race/hud.js + race/race.css".
 *
 * DOM only, built inside the `.race-hud` div race.html provides. Score rolls
 * up on a short tween (tabular numerals), the multiplier pill pulses on every
 * rung (CHIME LADDER), the combo bar drains over COMBO_HOLD_SEC, the MARQUEE
 * banner slides in once per gate in the room's colour, the item slot bounces,
 * speed is a thin gauge. Toasts each carry their own motion (ALMOST shivers,
 * JACKPOT is a gold flash and a REVEAL, BANK flies tokens into the score and
 * ticks the counter as they land). flicker() is the Stat Flicker: the face
 * lies for 450 ms, the ledger never does (Law I). The Brake and the End card
 * are the only pointer targets. Copy is DtRH voice: lowercase, short.
 * ==========================================================================*/

import { COMBO_HOLD_SEC, MULT_LADDER, KART_MAX_SPEED } from './consts.js';

const FLICK_MS = 450;
const TOAST_HOLD = { pop: 1100, almost: 1300, jackpot: 1800, bank: 1600, item: 1400, effect: 1400 };
const TOP_RUNG = MULT_LADDER[MULT_LADDER.length - 1][1];

export function createRaceHud(root) {
  const reduced = !!(typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const timers = new Set();
  let raf = 0;
  let disposed = false;

  const el = (cls, parent, text) => {
    const d = document.createElement('div');
    d.className = cls;
    if (text != null) d.textContent = text;
    parent.appendChild(d);
    return d;
  };
  const later = (fn, ms) => {
    const t = setTimeout(() => { timers.delete(t); if (!disposed) fn(); }, ms);
    timers.add(t);
    return t;
  };
  // re-trigger a one-shot animation class even when it is already applied
  const hit = (node, cls) => { node.classList.remove(cls); void node.offsetWidth; node.classList.add(cls); };
  const fmt = (n) => Math.round(n).toLocaleString('en-US');

  // ---- chrome (z3, click-through) ----
  const chrome = el('rh-chrome', root);
  const vignette = el('rh-vignette', chrome);
  const gold = el('rh-gold', chrome);

  const scoreWrap = el('rh-score-wrap', chrome);
  el('rh-label', scoreWrap, 'score');
  const scoreRow = el('rh-score-row', scoreWrap);
  const scoreEl = el('rh-score rh-num', scoreRow, '0');
  const multEl = el('rh-mult rh-num', scoreRow, 'x1');
  const rungs = el('rh-rungs', scoreRow);
  const rungEls = MULT_LADDER.slice(1).map(() => el('rh-rung', rungs));
  const comboRow = el('rh-combo-row is-off', scoreWrap);
  const comboBar = el('rh-combo-bar', comboRow);
  const comboFill = el('rh-combo-fill', comboBar);
  comboFill.style.setProperty('--rh-hold', `${COMBO_HOLD_SEC}s`);
  const comboN = el('rh-num', comboRow, 'combo 0');
  const bankEl = el('rh-bank rh-num', scoreWrap, 'banked 0');

  const banner = el('rh-banner', chrome);
  const bannerName = el('rh-banner-name', banner);
  const bannerTag = el('rh-banner-tag', banner);

  const item = el('rh-item', chrome);
  const itemBox = el('rh-item-box', item, '·');
  const itemName = el('rh-item-name', item, 'no item yet');
  const itemKey = document.createElement('span');
  itemKey.className = 'rh-item-key';
  itemKey.textContent = 'E';
  itemName.appendChild(itemKey);

  const speed = el('rh-speed', chrome);
  const speedRow = el('', speed);
  const speedN = document.createElement('span');
  speedN.className = 'rh-speed-n rh-num';
  speedN.textContent = '0';
  const speedU = document.createElement('span');
  speedU.className = 'rh-speed-u';
  speedU.textContent = 'km/h';
  speedRow.append(speedN, speedU);
  const speedFill = el('rh-speed-fill', el('rh-speed-bar', speed));

  const toasts = el('rh-toasts', chrome);

  // ---- score tween: a self-terminating rAF loop that only spins while a delta is open ----
  let scoreTarget = 0, scoreShown = 0, bankTarget = 0, bankShown = 0;
  let flickUntil = 0;
  function tick() {
    raf = 0;
    let busy = false;
    if (Math.abs(scoreTarget - scoreShown) > 0.5) {
      scoreShown += (scoreTarget - scoreShown) * (reduced ? 1 : 0.22);
      if (Math.abs(scoreTarget - scoreShown) < 0.5) scoreShown = scoreTarget; else busy = true;
      if (performance.now() > flickUntil) scoreEl.textContent = fmt(scoreShown);
    }
    if (Math.abs(bankTarget - bankShown) > 0.5) {
      bankShown += (bankTarget - bankShown) * (reduced ? 1 : 0.2);
      if (Math.abs(bankTarget - bankShown) < 0.5) bankShown = bankTarget; else busy = true;
      bankEl.textContent = `banked ${fmt(bankShown)}`;
    }
    if (busy && !disposed) raf = requestAnimationFrame(tick);
  }
  const wake = () => { if (!raf && !disposed) raf = requestAnimationFrame(tick); };

  let lastMult = 1, lastCombo = 0;

  // ---- BANK: tokens fly from low centre into the score; the counter ticks per landing ----
  function bankTokens(text) {
    const n = Math.min(7, 3 + Math.floor(Math.abs(bankTarget - bankShown) / 40));
    const s = scoreEl.getBoundingClientRect();
    const w = window.innerWidth, h = window.innerHeight;
    for (let i = 0; i < n; i++) {
      const tk = el('rh-token', chrome);
      const x0 = w * 0.5 + (Math.random() - 0.5) * 60, y0 = h * 0.66;
      tk.style.left = `${x0}px`;
      tk.style.top = `${y0}px`;
      tk.style.setProperty('--dx', `${s.left + s.width / 2 - x0}px`);
      tk.style.setProperty('--dy', `${s.top + s.height / 2 - y0}px`);
      later(() => tk.classList.add('is-fly'), reduced ? 0 : i * 70);
      later(() => { tk.remove(); hit(bankEl, 'is-pop'); if (i === n - 1) hit(scoreEl, 'is-pop'); }, reduced ? 120 : i * 70 + 640);
    }
    return text;
  }

  // ---- screens (z20, the only pointer targets) ----
  let brakeResolve = null, endResolve = null;
  const brake = el('rh-screen', root);
  const brakeCard = el('rh-card', brake);
  brakeCard.appendChild(Object.assign(document.createElement('h2'), { textContent: 'the brake' }));
  brakeCard.appendChild(Object.assign(document.createElement('p'), { textContent: 'the road waits. nothing is lost.' }));
  const brakeBtns = el('rh-btns', brakeCard);
  const btn = (parent, text, cls, on) => {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = `rh-btn${cls ? ' ' + cls : ''}`;
    b.textContent = text;
    b.addEventListener('click', on);
    parent.appendChild(b);
    return b;
  };
  const settleBrake = (v) => { const r = brakeResolve; brakeResolve = null; brake.classList.remove('is-on'); if (r) r(v); };
  btn(brakeBtns, 'back on the road', '', () => settleBrake('resume'));
  btn(brakeBtns, 'end the run', 'rh-btn--quiet', () => settleBrake('end'));
  el('rh-hints', brakeCard, reduced
    ? 'esc resumes. reduced motion is on: no roll, no jolt, banners fade.'
    : 'esc resumes. your score stays. turn on reduced motion in your system settings for a level camera and no jolts.');

  const end = el('rh-screen', root);
  const endCard = el('rh-card', end);
  const endTitle = endCard.appendChild(Object.assign(document.createElement('h2'), { textContent: 'the tea party' }));
  endCard.appendChild(Object.assign(document.createElement('p'), { textContent: 'everybody has won.' }));
  const pbEl = el('rh-pb', endCard, 'personal best');
  const rows = document.createElement('dl');
  rows.className = 'rh-rows';
  endCard.appendChild(rows);
  const endBtns = el('rh-btns', endCard);
  const settleEnd = (v) => { const r = endResolve; endResolve = null; end.classList.remove('is-on'); if (r) r(v); };
  btn(endBtns, 'again', '', () => settleEnd('again'));
  btn(endBtns, 'surface', 'rh-btn--quiet', () => settleEnd('exit'));
  const fmtDur = (sec) => { const m = Math.floor(sec / 60), s = Math.floor(sec % 60); return `${m}:${s < 10 ? '0' : ''}${s}`; };

  const hud = {
    setScore(n) { scoreTarget = Math.max(0, n || 0); wake(); },
    setCombo(combo, mult) {
      combo = combo | 0; mult = mult || 1;
      comboN.textContent = `combo ${combo}`;
      comboRow.classList.toggle('is-off', combo <= 0);
      if (combo > 0 && (combo !== lastCombo || !comboFill.classList.contains('is-draining'))) hit(comboFill, 'is-draining');
      if (combo <= 0) comboFill.classList.remove('is-draining');
      if (mult !== lastMult) {
        multEl.textContent = `x${mult}`;
        multEl.classList.toggle('is-gold', mult >= 6);
        if (mult > lastMult) { hit(multEl, 'is-pulse'); if (mult >= TOP_RUNG) hit(scoreEl, 'is-pop'); }
      }
      rungEls.forEach((r, i) => r.classList.toggle('is-lit', mult >= MULT_LADDER[i + 1][1]));
      lastMult = mult; lastCombo = combo;
    },
    setSpeed(ms) {
      const v = Math.max(0, ms || 0);
      speedN.textContent = String(Math.round(v * 3.6));
      speedFill.style.transform = `scaleX(${Math.min(1, v / KART_MAX_SPEED).toFixed(3)})`;
    },
    setBank(n) { bankTarget = Math.max(0, n || 0); wake(); },
    banner(name, tagline, colorHex) {
      if (colorHex) chrome.style.setProperty('--rh-room', colorHex);
      bannerName.textContent = (name || '').toLowerCase();
      bannerTag.textContent = (tagline || '').toLowerCase();
      banner.classList.remove('is-on');
      later(() => banner.classList.add('is-on'), 40);
      later(() => banner.classList.remove('is-on'), 2600);
    },
    item(glyph, name) {
      itemBox.textContent = glyph == null ? '·' : String(glyph);
      itemName.firstChild.textContent = (name || (glyph == null ? 'no item yet' : '')).toLowerCase();
      itemName.appendChild(itemKey);
      item.classList.toggle('is-armed', glyph != null);
      item.classList.toggle('is-rolling', glyph === '?');
      hit(item, 'is-bounce');
    },
    toast(text, kind) {
      kind = TOAST_HOLD[kind] ? kind : 'pop';
      let body = String(text == null ? '' : text);
      if (kind === 'bank') body = bankTokens(body);
      if (kind === 'jackpot') hit(gold, 'is-on');
      const t = el(`rh-toast rh-toast--${kind}`, toasts, body);
      t.style.setProperty('--rh-hold', `${TOAST_HOLD[kind]}ms`);
      while (toasts.children.length > 3) toasts.firstChild.remove();
      later(() => t.remove(), TOAST_HOLD[kind] + 60);
    },
    flicker() {
      const lie = Math.floor(scoreShown * (1 + (Math.random() < 0.5 ? -1 : 1) * (0.03 + Math.random() * 0.06)));
      flickUntil = performance.now() + FLICK_MS;
      scoreEl.textContent = fmt(lie);
      hit(scoreEl, 'is-flick');
      later(() => { scoreEl.classList.remove('is-flick'); scoreEl.textContent = fmt(scoreShown); }, FLICK_MS);
    },
    setFraught(v) {
      v = Math.min(1, Math.max(0, v || 0));
      chrome.style.setProperty('--rh-fraught', v.toFixed(3));
      vignette.classList.toggle('is-beat', v > 0.35);
      vignette.style.setProperty('--rh-beat', `${(1.3 - v * 0.6).toFixed(2)}s`);
    },
    /** Shows the Brake. Resolves 'resume' | 'end' when the player picks, or 'resume' on setPaused(false). */
    setPaused(on) {
      if (!on) { settleBrake('resume'); return Promise.resolve('resume'); }
      if (brakeResolve) return Promise.resolve('resume');
      brake.classList.add('is-on');
      return new Promise((res) => { brakeResolve = res; });
    },
    showEnd(summary) {
      const s = summary || {};
      settleBrake('resume');
      endTitle.textContent = s.title || 'the tea party';
      pbEl.style.display = s.personalBest ? '' : 'none';
      rows.textContent = '';
      const line = (k, v) => {
        rows.appendChild(Object.assign(document.createElement('dt'), { textContent: k }));
        rows.appendChild(Object.assign(document.createElement('dd'), { textContent: v, className: 'rh-num' }));
      };
      line('score', fmt(s.score || 0));
      line('banked', fmt(s.banked || 0));
      line('best combo', fmt(s.bestCombo || 0));
      line('popped', fmt(s.popped || 0));
      line('laps', fmt(s.laps || 0));
      line('time', fmtDur(s.durationSec || 0));
      if (endResolve) settleEnd('exit');
      end.classList.add('is-on');
      return new Promise((res) => { endResolve = res; });
    },
    dispose() {
      disposed = true;
      settleBrake('resume');
      settleEnd('exit');
      for (const t of timers) clearTimeout(t);
      timers.clear();
      if (raf) cancelAnimationFrame(raf);
      chrome.remove(); brake.remove(); end.remove();
    },
  };
  return hud;
}

// self-check: node --check is the bar; the DOM is required for anything more.
