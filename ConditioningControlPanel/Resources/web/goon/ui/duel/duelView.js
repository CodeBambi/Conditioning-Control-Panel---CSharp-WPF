/* ============================================================================
 * ui/duel/duelView.js - the game night duel overlay. DOM only, no rules.
 *
 * The Deep End GRID ONLY: no arcademy window, no heat, no tricksters, no casino
 * lighting. One overlay on <body> at z56: over the HUD (z40) and the toasts
 * (z50), UNDER Mercy (z60), so Mercy is reachable during a duel. The dim layer
 * is pointer-events:none; only the card takes input.
 *
 * Juice: a rubber-stamp intro, tiles that pop on merge, a spark burst per
 * merge, a stamped result. Everything animated is dropped under the page's
 * reduced motion (html[data-gg-motion="reduced"]) or the OS setting.
 *
 * The stylesheet is injected once from here so the feature stays in its own
 * files.
 * ==========================================================================*/

import { DUEL_COPY } from './copy.js';
import { grid, TIER_MAX } from './board.js';
import { tileValue } from './rules.js';

const STYLE_ID = 'gg-duel-style';
const SWIPE_MIN_PX = 24;
const SPARKS_PER_MERGE = 6;

const CSS = `
.gg-duel { position: fixed; inset: 0; z-index: 56; pointer-events: none; display: flex;
  align-items: center; justify-content: center; background: rgba(8, 3, 14, 0.55); }
.gg-duel[hidden] { display: none; }
.gg-duel-card { pointer-events: auto; position: relative; width: min(22rem, 90vw);
  padding: 1rem 1rem 1.1rem; border-radius: 16px; background: rgba(24, 10, 34, 0.96);
  border: 2px solid var(--gg-pink, #ff69b4); box-shadow: 0 0 36px rgba(255, 105, 180, 0.35);
  color: #fff; font-family: var(--gg-font, system-ui, sans-serif); text-align: center; }
.gg-duel-stamp { display: inline-block; margin: 0.2rem 0 0.4rem; padding: 0.25rem 0.8rem;
  border: 4px solid currentColor; border-radius: 8px; font-weight: 900; letter-spacing: 0.08em;
  text-transform: uppercase; font-size: 1.7rem; color: var(--gg-pink, #ff69b4); transform: rotate(-6deg);
  animation: ggDuelStamp 380ms cubic-bezier(.2, 1.6, .4, 1) both; }
.gg-duel-stamp.is-win { color: #6effc8; }
.gg-duel-stamp.is-lose { color: #9a8fb0; }
.gg-duel-name { font-weight: 800; font-size: 1.1rem; margin: 0.2rem 0; }
.gg-duel-fine { opacity: 0.75; font-size: 0.85rem; margin: 0.25rem 0; }
.gg-duel-hint { margin: 0.5rem 0 0; font-size: 0.9rem; color: #ffd36e; }
.gg-duel-top { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 0.5rem;
  font-variant-numeric: tabular-nums; }
.gg-duel-score { font-size: 1.6rem; font-weight: 900; }
.gg-duel-time { font-size: 1rem; opacity: 0.8; }
.gg-duel-time.is-low { color: #ff6b8a; opacity: 1; }
.gg-duel-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 6px; padding: 6px;
  border-radius: 12px; background: rgba(255, 255, 255, 0.06); touch-action: none; user-select: none; }
.gg-duel-cell { aspect-ratio: 1; border-radius: 8px; background: rgba(255, 255, 255, 0.05);
  display: flex; align-items: center; justify-content: center; font-weight: 900; font-size: 1.35rem; }
.gg-duel-cell.t1 { background: #3b2350; } .gg-duel-cell.t2 { background: #4a2466; }
.gg-duel-cell.t3 { background: #6a2a8a; } .gg-duel-cell.t4 { background: #8a2c9c; }
.gg-duel-cell.t5 { background: #b02f9c; } .gg-duel-cell.t6 { background: #d9338f; }
.gg-duel-cell.t7 { background: #ff4f8f; } .gg-duel-cell.t8 { background: #ff7a6b; color: #2a0b1c; }
.gg-duel-cell.t9 { background: #ffb24f; color: #2a0b1c; } .gg-duel-cell.t10 { background: #ffd36e; color: #2a0b1c; }
.gg-duel-cell.t11 { background: #6effc8; color: #06281d; box-shadow: 0 0 18px #6effc8; }
.gg-duel-cell.is-merge { animation: ggDuelPop 180ms ease-out; }
.gg-duel-spark { position: fixed; width: 7px; height: 7px; border-radius: 50%; pointer-events: none;
  z-index: 57; background: #ffd36e; box-shadow: 0 0 8px #ff69b4; transition: transform 420ms ease-out, opacity 420ms ease-out; }
@keyframes ggDuelStamp { 0% { transform: rotate(-6deg) scale(2.4); opacity: 0; } 100% { transform: rotate(-6deg) scale(1); opacity: 1; } }
@keyframes ggDuelPop { 0% { transform: scale(1); } 50% { transform: scale(1.18); } 100% { transform: scale(1); } }
html[data-gg-motion="reduced"] .gg-duel-stamp, html[data-gg-motion="reduced"] .gg-duel-cell.is-merge { animation: none; }
html[data-gg-motion="reduced"] .gg-duel-spark { display: none; }
@media (prefers-reduced-motion: reduce) {
  .gg-duel-stamp, .gg-duel-cell.is-merge { animation: none; }
  .gg-duel-spark { display: none; }
}
.gg-customize { margin-top: 0.6rem; }
.gg-customize-sum { cursor: pointer; opacity: 0.8; font-size: 0.9rem; }
.gg-cust-row { display: flex; align-items: center; gap: 0.6rem; flex-wrap: wrap; margin-top: 0.4rem; }
.gg-cust-picks { display: flex; gap: 0.3rem; }
.gg-cust-pick.is-on { border-color: var(--gg-pink, #ff69b4); }
.gg-cust-fine { opacity: 0.7; font-size: 0.8rem; margin: 0.3rem 0 0; }
`;

export function injectDuelStyle(d) {
  if (!d || !d.head || d.getElementById(STYLE_ID)) return;
  const s = d.createElement('style');
  s.id = STYLE_ID;
  s.textContent = CSS;
  d.head.appendChild(s);
}

function reduced(d) {
  try {
    if (d.documentElement.getAttribute('data-gg-motion') === 'reduced') return true;
    return !!(typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  } catch (_e) { return false; }
}

/** @returns {object} the view handle ui/duel/duelController.js drives, or null without a DOM. */
export function createDuelView() {
  const d = typeof document !== 'undefined' ? document : null;
  if (!d || !d.body) return null;
  injectDuelStyle(d);

  const root = d.createElement('div');
  root.className = 'gg-duel';
  root.hidden = true;
  const card = d.createElement('div');
  card.className = 'gg-duel-card';
  root.appendChild(card);
  d.body.appendChild(root);

  let input = null;
  let cells = [];
  let gridEl = null;
  let scoreEl = null;
  let timeEl = null;
  let playing = false;

  const mk = (tag, cls, text) => {
    const n = d.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = String(text);
    return n;
  };
  const reset = () => { while (card.firstChild) card.removeChild(card.firstChild); cells = []; gridEl = null; };

  const KEYS = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right', w: 'up', s: 'down', a: 'left', d: 'right' };
  /** Typing somewhere, or a z70 modal/drawer is up: the keys are not the board's. */
  function keysBelongElsewhere(e) {
    const t = e && e.target;
    const tag = t && t.tagName ? String(t.tagName).toLowerCase() : '';
    if (tag === 'input' || tag === 'textarea' || tag === 'select' || (t && t.isContentEditable)) return true;
    for (const id of ['gg-modal', 'gg-drawer']) {
      const host = d.getElementById(id);
      if (host && !host.hidden && host.childElementCount > 0) return true;
    }
    return false;
  }

  function onKey(e) {
    if (!playing || !input || !e || e.ctrlKey || e.altKey || e.metaKey) return;
    if (keysBelongElsewhere(e)) return;
    const dir = KEYS[e.key];
    if (!dir) return;
    e.preventDefault();
    e.stopPropagation();
    input(dir);
  }
  window.addEventListener('keydown', onKey, true);

  let sx = 0; let sy = 0; let pid = null;
  function onDown(e) { pid = e.pointerId; sx = e.clientX; sy = e.clientY; }
  function onUp(e) {
    if (pid == null || e.pointerId !== pid) return;
    pid = null;
    const dx = e.clientX - sx; const dy = e.clientY - sy;
    if (Math.max(Math.abs(dx), Math.abs(dy)) < SWIPE_MIN_PX || !input || !playing) return;
    input(Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 'right' : 'left') : (dy > 0 ? 'down' : 'up'));
  }

  function buildBoard() {
    reset();
    const top = mk('div', 'gg-duel-top');
    scoreEl = mk('span', 'gg-duel-score', '0');
    timeEl = mk('span', 'gg-duel-time', '');
    top.appendChild(scoreEl);
    top.appendChild(timeEl);
    card.appendChild(top);
    gridEl = mk('div', 'gg-duel-grid');
    for (let i = 0; i < 16; i++) { const c = mk('div', 'gg-duel-cell'); cells.push(c); gridEl.appendChild(c); }
    gridEl.addEventListener('pointerdown', onDown);
    gridEl.addEventListener('pointerup', onUp);
    gridEl.addEventListener('pointercancel', () => { pid = null; });
    card.appendChild(gridEl);
  }

  function sparks(cell) {
    if (reduced(d) || !cell || typeof cell.getBoundingClientRect !== 'function') return;
    const r = cell.getBoundingClientRect();
    const cx = r.left + r.width / 2; const cy = r.top + r.height / 2;
    for (let i = 0; i < SPARKS_PER_MERGE; i++) {
      const s = mk('i', 'gg-duel-spark');
      s.style.left = cx + 'px';
      s.style.top = cy + 'px';
      d.body.appendChild(s);
      const a = (Math.PI * 2 * i) / SPARKS_PER_MERGE + Math.random() * 0.6;
      const dist = 26 + Math.random() * 26;
      requestAnimationFrame(() => {
        s.style.transform = 'translate(' + Math.cos(a) * dist + 'px,' + Math.sin(a) * dist + 'px) scale(0.3)';
        s.style.opacity = '0';
      });
      setTimeout(() => { try { s.remove(); } catch (_e) { /* gone */ } }, 480);
    }
  }

  return {
    bind(o) { input = o && o.input; },
    intro({ by, hint }) {
      playing = false;
      reset();
      card.appendChild(mk('div', 'gg-duel-stamp', by === 'you' ? DUEL_COPY.incomingYou : DUEL_COPY.incomingThem));
      card.appendChild(mk('div', 'gg-duel-name', DUEL_COPY.gameName));
      card.appendChild(mk('p', 'gg-duel-fine', DUEL_COPY.rule));
      if (hint) card.appendChild(mk('p', 'gg-duel-hint', DUEL_COPY.firstHint));
      root.hidden = false;
    },
    board(board, { secondsLeft, merges } = {}) {
      if (!gridEl) buildBoard();
      playing = true;
      root.hidden = false;
      const g = grid(board);
      const merged = new Set((merges || []).map((m) => m.r * 4 + m.c));
      for (let r = 0; r < 4; r++) {
        for (let c = 0; c < 4; c++) {
          const t = g[r][c];
          const cell = cells[r * 4 + c];
          const tier = t ? Math.min(t.tier, TIER_MAX) : 0;
          cell.className = 'gg-duel-cell' + (tier ? ' t' + tier : '');
          cell.textContent = tier ? String(tileValue(tier)) : '';
          if (merged.has(r * 4 + c)) {
            void cell.offsetWidth;   // restart the pop
            cell.classList.add('is-merge');
            sparks(cell);
          }
        }
      }
      scoreEl.textContent = String(board.score | 0);
      if (secondsLeft != null) this.timer(secondsLeft);
    },
    timer(left) {
      if (!timeEl) return;
      timeEl.textContent = DUEL_COPY.timeLeft(left);
      timeEl.classList.toggle('is-low', left <= 10);
    },
    waiting() {
      playing = false;
      if (timeEl) timeEl.textContent = DUEL_COPY.waiting;
    },
    result({ outcome, bonus, mine, theirs }) {
      playing = false;
      reset();
      const word = outcome === 'win' ? DUEL_COPY.won(bonus) : outcome === 'lose' ? DUEL_COPY.lost : DUEL_COPY.tied;
      card.appendChild(mk('div', 'gg-duel-stamp is-' + outcome, word));
      card.appendChild(mk('p', 'gg-duel-fine', DUEL_COPY.youLine(tileValue(mine.tile), mine.score)));
      card.appendChild(mk('p', 'gg-duel-fine', DUEL_COPY.themLine(tileValue(theirs.tile), theirs.score)));
    },
    close() { playing = false; reset(); root.hidden = true; },
    dispose() {
      playing = false;
      window.removeEventListener('keydown', onKey, true);
      try { root.remove(); } catch (_e) { /* gone */ }
    },
  };
}

export default createDuelView;
