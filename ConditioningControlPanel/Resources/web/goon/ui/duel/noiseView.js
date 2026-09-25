/* ============================================================================
 * ui/duel/noiseView.js - the Sort duel's VS reveal and noise pick. DOM only.
 *
 * Driven by ui/duel/duelController.js through ui/duel/duelView.js (reveal, pick,
 * picked, pickTimer, lock), torn down by the view's play / close. One layer
 * inside the duel overlay, above its dim and under Mercy like the rest of it.
 *
 *   REVEAL  two plates slam in face to face (yours from the left, theirs from
 *           the right), the VS badge drops between them with a ring and sparks.
 *   PICK    the plates slide up into a header, seven tiles rise in a wave, a
 *           ring clock counts the pick down. A tap stamps the tile gold and
 *           locks the rest; their pick lands as a violet tag on its tile.
 *   LOCK    the tiles nobody picked fall away, the picked ones pulse once.
 *   OUT     the whole layer fades and lifts as the class comes up.
 *
 * Every entrance has an exit, and html[data-gg-motion="reduced"],
 * prefers-reduced-motion and html[data-gg-perf="lite"] drop the motion (the
 * layer still shows, cross-fades only).
 * ==========================================================================*/

import { DUEL_COPY } from './copy.js';
import { noiseTiles, noiseName } from './noisePick.js';

const STYLE_ID = 'gg-noise-style';
export const NOISE_OUT_MS = 260;

const CSS = `
.gg-noise { position: absolute; inset: 0; z-index: 58; pointer-events: auto; color: var(--gg-text, #fff);
  font-family: var(--gg-font, system-ui, sans-serif); display: flex; flex-direction: column; align-items: center;
  justify-content: center; gap: 1.1rem; padding: 1rem; box-sizing: border-box; overflow: hidden;
  background: radial-gradient(ellipse at 50% 45%, rgba(var(--gg-violet-rgb, 139, 92, 246), 0.28), rgba(8, 3, 14, 0.9) 70%);
  animation: ggNzIn 260ms ease-out both; }
.gg-noise.is-out { animation: ggNzOut ${NOISE_OUT_MS}ms ease-in both; pointer-events: none; }
.gg-nz-kicker { font-size: 0.8rem; letter-spacing: 0.22em; text-transform: uppercase; color: var(--gg-gold, #ffd36e); text-align: center; max-width: 94vw;
  opacity: 0.9; transition: opacity 240ms ease, transform 300ms ease; }
.gg-nz-vs { position: relative; display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; gap: 1rem;
  width: min(46rem, 94vw); transition: transform 420ms cubic-bezier(.2, 1.2, .4, 1), width 420ms ease; }
.gg-nz-plate { position: relative; padding: 0.9rem 1.1rem; border-radius: var(--gg-radius-card, 16px); min-width: 0;
  background: linear-gradient(160deg, rgba(40, 16, 58, 0.95), rgba(20, 8, 32, 0.95));
  border: 2px solid var(--nz-tint, #ff69b4); box-shadow: 0 0 28px color-mix(in srgb, var(--nz-tint, #ff69b4) 45%, transparent);
  transition: padding 300ms ease, box-shadow 300ms ease; }
.gg-nz-plate.is-you { text-align: right; animation: ggNzSlamL 520ms cubic-bezier(.2, 1.4, .4, 1) both; }
.gg-nz-plate.is-them { text-align: left; animation: ggNzSlamR 520ms cubic-bezier(.2, 1.4, .4, 1) both; }
.gg-nz-side { font-size: 0.72rem; letter-spacing: 0.2em; text-transform: uppercase; opacity: 0.7; }
.gg-nz-name { font-size: clamp(1.3rem, 4vw, 2.1rem); font-weight: 900; color: var(--nz-tint, #ff69b4); line-height: 1.1;
  overflow-wrap: anywhere; text-shadow: 0 0 18px color-mix(in srgb, var(--nz-tint, #ff69b4) 55%, transparent); }
.gg-nz-subs { font-size: 0.78rem; opacity: 0.65; margin-top: 0.3rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  transition: opacity 200ms ease, max-height 300ms ease; max-height: 1.4em; }
.gg-nz-badge { position: relative; width: 4.2rem; height: 4.2rem; display: grid; place-items: center; border-radius: 50%;
  font-weight: 900; font-size: 1.5rem; letter-spacing: 0.04em; color: #1a0a24;
  background: radial-gradient(circle at 35% 30%, #fff3c4, var(--gg-gold, #ffd36e) 55%, #e39b2e);
  box-shadow: 0 0 30px rgba(255, 211, 110, 0.7); animation: ggNzBadge 560ms 240ms cubic-bezier(.2, 1.8, .4, 1) both;
  transition: width 300ms ease, height 300ms ease, font-size 300ms ease; }
.gg-nz-ring { position: absolute; inset: -6px; border-radius: 50%; border: 3px solid var(--gg-gold, #ffd36e); opacity: 0;
  animation: ggNzRing 700ms 380ms ease-out both; pointer-events: none; }
.gg-nz-spark { position: absolute; left: 50%; top: 50%; width: 6px; height: 6px; margin: -3px; border-radius: 50%;
  background: var(--gg-gold, #ffd36e); opacity: 0; pointer-events: none;
  animation: ggNzSpark 620ms 400ms cubic-bezier(.1, .8, .3, 1) both; }
.gg-noise.is-pick .gg-nz-vs { transform: scale(0.72); }
.gg-noise.is-pick .gg-nz-plate { padding: 0.5rem 0.8rem; }
.gg-noise.is-pick .gg-nz-subs { opacity: 0; max-height: 0; margin: 0; }
.gg-noise.is-pick .gg-nz-badge { width: 3rem; height: 3rem; font-size: 1.05rem; }
.gg-nz-pickhead { display: flex; align-items: center; gap: 0.9rem; opacity: 0; transform: translateY(12px);
  transition: opacity 260ms ease, transform 320ms cubic-bezier(.2, 1.3, .4, 1); }
.gg-noise.is-pick .gg-nz-pickhead { opacity: 1; transform: none; }
.gg-nz-title { font-size: 1.35rem; font-weight: 900; color: var(--gg-pink, #ff69b4); }
.gg-nz-line { font-size: 0.85rem; opacity: 0.72; margin: 0.15rem 0 0; }
.gg-nz-clock { position: relative; width: 3rem; height: 3rem; flex: none; }
.gg-nz-clock svg { width: 100%; height: 100%; transform: rotate(-90deg); }
.gg-nz-clock circle { fill: none; stroke-width: 4; }
.gg-nz-clock .bg { stroke: rgba(255, 255, 255, 0.12); }
.gg-nz-clock .fg { stroke: var(--gg-gold, #ffd36e); stroke-linecap: round; transition: stroke-dashoffset 250ms linear, stroke 200ms ease; }
.gg-nz-clock.is-low .fg { stroke: #ff6b8a; }
.gg-nz-clock b { position: absolute; inset: 0; display: grid; place-items: center; font-weight: 900; font-variant-numeric: tabular-nums; }
.gg-nz-clock.is-low b { color: #ff6b8a; animation: ggNzThrob 500ms ease-in-out infinite alternate; }
.gg-nz-grid { display: flex; flex-wrap: wrap; justify-content: center; gap: 0.6rem; width: min(40rem, 94vw); }
.gg-nz-grid > .gg-nz-tile { flex: 0 0 calc((100% - 1.8rem) / 4); }
.gg-nz-tile { position: relative; aspect-ratio: 4 / 3; border-radius: 12px; overflow: hidden; cursor: pointer;
  border: 2px solid color-mix(in srgb, var(--nz-tint, #b99cff) 60%, transparent); padding: 0; color: #fff;
  background: linear-gradient(160deg, color-mix(in srgb, var(--nz-tint, #b99cff) 30%, #1c0b2a), #120718);
  font: inherit; display: grid; place-items: center; opacity: 0; transform: translateY(18px) scale(0.9);
  transition: transform 180ms cubic-bezier(.2, 1.4, .4, 1), opacity 220ms ease, border-color 160ms ease, filter 220ms ease, box-shadow 200ms ease; }
.gg-noise.is-pick .gg-nz-tile { opacity: 1; transform: none; transition-delay: calc(var(--i, 0) * 45ms); }
.gg-noise.is-pick .gg-nz-tile:hover:not(:disabled) { transform: translateY(-3px) scale(1.04); transition-delay: 0ms;
  box-shadow: 0 6px 22px color-mix(in srgb, var(--nz-tint, #b99cff) 50%, transparent); }
.gg-nz-tile:focus-visible { outline: 3px solid var(--gg-gold, #ffd36e); outline-offset: 2px; }
.gg-nz-tile:disabled { cursor: default; }
.gg-nz-tile img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; opacity: 0; transition: opacity 400ms ease; }
.gg-nz-tile img.is-on { opacity: 0.8; }
.gg-nz-tile::after { content: ''; position: absolute; inset: 0; background: linear-gradient(to top, rgba(10, 4, 16, 0.85), transparent 60%); }
.gg-nz-glyph { position: relative; z-index: 1; font-size: 1.8rem; line-height: 1; transition: opacity 300ms ease; }
.gg-nz-tile.has-img .gg-nz-glyph { opacity: 0; }
.gg-nz-tname { position: absolute; left: 0; right: 0; bottom: 0.35rem; z-index: 2; text-align: center; font-weight: 800;
  font-size: 0.85rem; letter-spacing: 0.02em; }
.gg-nz-tag { position: absolute; top: 0.35rem; z-index: 3; padding: 0.1rem 0.45rem; border-radius: 999px; font-size: 0.66rem;
  font-weight: 900; letter-spacing: 0.12em; text-transform: uppercase; animation: ggNzTag 360ms cubic-bezier(.2, 1.8, .4, 1) both; }
.gg-nz-tag.is-you { left: 0.35rem; background: var(--gg-gold, #ffd36e); color: #1a0a24; }
.gg-nz-tag.is-them { right: 0.35rem; background: var(--gg-violet, #8b5cf6); color: #fff; }
.gg-noise.is-picked .gg-nz-tile:not(.is-mine):not(.is-theirs) { filter: saturate(0.4) brightness(0.6); }
.gg-nz-tile.is-mine { border-color: var(--gg-gold, #ffd36e); box-shadow: 0 0 24px rgba(255, 211, 110, 0.55); animation: ggNzPop 380ms cubic-bezier(.2, 1.8, .4, 1); }
.gg-nz-tile.is-theirs { border-color: var(--gg-violet, #8b5cf6); }
.gg-nz-tile.is-mine.is-theirs { border-image: linear-gradient(90deg, var(--gg-gold, #ffd36e), var(--gg-violet, #8b5cf6)) 1; }
.gg-noise.is-lock .gg-nz-tile:not(.is-mine):not(.is-theirs) { opacity: 0; transform: translateY(16px) scale(0.85); transition-delay: 0ms; }
.gg-noise.is-lock .gg-nz-tile.is-mine { animation: ggNzPop 420ms cubic-bezier(.2, 1.8, .4, 1); }
.gg-nz-status { min-height: 1.3em; font-size: 0.9rem; display: flex; gap: 1.2rem; justify-content: center; flex-wrap: wrap; }
.gg-nz-status span { opacity: 0.85; }
.gg-nz-status b { color: var(--gg-gold, #ffd36e); }
.gg-nz-status .them b { color: #c9b8ff; }
.gg-nz-hint { font-size: 0.85rem; color: var(--gg-gold, #ffd36e); margin: 0; }
@media (max-width: 560px) { .gg-nz-grid > .gg-nz-tile { flex-basis: calc((100% - 1.2rem) / 3); } .gg-nz-vs { gap: 0.5rem; } }
@keyframes ggNzIn { from { opacity: 0; } to { opacity: 1; } }
@keyframes ggNzOut { from { opacity: 1; transform: none; } to { opacity: 0; transform: translateY(-14px) scale(1.02); } }
@keyframes ggNzSlamL { 0% { transform: translateX(-60vw) skewX(-12deg); opacity: 0; } 70% { transform: translateX(14px) skewX(4deg); opacity: 1; } 100% { transform: none; } }
@keyframes ggNzSlamR { 0% { transform: translateX(60vw) skewX(12deg); opacity: 0; } 70% { transform: translateX(-14px) skewX(-4deg); opacity: 1; } 100% { transform: none; } }
@keyframes ggNzBadge { 0% { transform: scale(3) rotate(-30deg); opacity: 0; } 60% { transform: scale(0.85) rotate(6deg); opacity: 1; } 100% { transform: none; } }
@keyframes ggNzRing { 0% { transform: scale(0.6); opacity: 0.95; } 100% { transform: scale(2.6); opacity: 0; } }
@keyframes ggNzSpark { 0% { transform: translate(0, 0) scale(1); opacity: 1; } 100% { transform: translate(var(--dx), var(--dy)) scale(0.3); opacity: 0; } }
@keyframes ggNzTag { 0% { transform: scale(2); opacity: 0; } 100% { transform: none; opacity: 1; } }
@keyframes ggNzPop { 0% { transform: scale(1); } 45% { transform: scale(1.1); } 100% { transform: scale(1); } }
@keyframes ggNzThrob { from { transform: scale(1); } to { transform: scale(1.15); } }
html[data-gg-motion="reduced"] .gg-noise *, html[data-gg-perf="lite"] .gg-noise * { animation: none !important; }
html[data-gg-motion="reduced"] .gg-nz-spark, html[data-gg-motion="reduced"] .gg-nz-ring,
html[data-gg-perf="lite"] .gg-nz-spark, html[data-gg-perf="lite"] .gg-nz-ring { display: none; }
html[data-gg-motion="reduced"] .gg-nz-tile, html[data-gg-motion="reduced"] .gg-nz-vs { transform: none !important; transition: opacity 200ms linear; }
html[data-gg-motion="reduced"] .gg-noise.is-out { animation: ggNzIn 200ms linear reverse both !important; }
@media (prefers-reduced-motion: reduce) {
  .gg-noise * { animation: none !important; }
  .gg-nz-spark, .gg-nz-ring { display: none; }
  .gg-nz-tile, .gg-nz-vs { transform: none !important; transition: opacity 200ms linear; }
  .gg-noise.is-out { animation: ggNzIn 200ms linear reverse both; }
}
`;

function injectStyle(d) {
  if (!d || !d.head || d.getElementById(STYLE_ID)) return;
  const s = d.createElement('style');
  s.id = STYLE_ID;
  s.textContent = CSS;
  d.head.appendChild(s);
}

const SPARKS = 10;
const CLOCK_R = 20;
const CLOCK_LEN = 2 * Math.PI * CLOCK_R;

/**
 * @param {object} o
 * @param {Document} o.d
 * @param {HTMLElement} o.host  the duel overlay root
 */
export function createNoiseView({ d, host }) {
  if (!d || !host) return null;
  injectStyle(d);
  const mk = (tag, cls, text) => {
    const n = d.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = String(text);
    return n;
  };

  let layer = null;
  let tiles = new Map();      // id -> {btn, img}
  let status = null;
  let clock = null;
  let total = 8;
  let preview = null;
  let onPick = null;
  let mine = '';
  let theirs = '';

  function plate(side, label) {
    const p = mk('div', 'gg-nz-plate is-' + side);
    p.style.setProperty('--nz-tint', (label && label.tint) || '#ff69b4');
    p.appendChild(mk('div', 'gg-nz-side', side === 'you' ? DUEL_COPY.revealYou : DUEL_COPY.revealThem));
    p.appendChild(mk('div', 'gg-nz-name', (label && label.name) || DUEL_COPY.houseMix));
    const subs = label && Array.isArray(label.subs) && label.subs.length
      ? label.subs.slice(0, 3).map((s) => 'r/' + s).join('  ') + (label.more ? '  ' + label.more : '')
      : '';
    if (subs) p.appendChild(mk('div', 'gg-nz-subs', subs));
    return p;
  }

  function drop() {
    if (layer) { try { layer.remove(); } catch (_e) { /* gone */ } }
    layer = null; tiles = new Map(); status = null; clock = null; preview = null; onPick = null; mine = ''; theirs = '';
  }

  function paintStatus() {
    if (!status) return;
    while (status.firstChild) status.removeChild(status.firstChild);
    const you = mk('span', 'you');
    you.appendChild(d.createTextNode(DUEL_COPY.pickedYou + ' '));
    you.appendChild(mk('b', '', mine ? noiseName(mine) : '...'));
    const them = mk('span', 'them');
    if (theirs) {
      them.appendChild(d.createTextNode(DUEL_COPY.pickedThem + ' '));
      them.appendChild(mk('b', '', noiseName(theirs)));
    } else them.textContent = DUEL_COPY.theyPick;
    status.appendChild(you);
    status.appendChild(them);
  }

  function refreshPreviews() {
    if (typeof preview !== 'function') return;
    for (const [id, t] of tiles) {
      if (t.img) continue;
      let url = '';
      try { url = preview(id) || ''; } catch (_e) { url = ''; }
      if (!url) continue;
      const img = mk('img', '');
      img.alt = '';
      img.decoding = 'async';
      img.onload = () => { img.classList.add('is-on'); t.btn.classList.add('has-img'); };
      img.onerror = () => { try { img.remove(); } catch (_e) { /* gone */ } t.img = null; };
      img.src = url;
      t.img = img;
      t.btn.insertBefore(img, t.btn.firstChild);
    }
  }

  function tag(id, who, rolled) {
    const t = tiles.get(id);
    if (!t) return;
    t.btn.classList.add(who === 'you' ? 'is-mine' : 'is-theirs');
    const label = who === 'you' ? (rolled ? DUEL_COPY.rolled : DUEL_COPY.revealYou) : DUEL_COPY.revealThem;
    t.btn.appendChild(mk('span', 'gg-nz-tag is-' + who, label));
  }

  return {
    reveal({ name, you, them, hint } = {}) {
      drop();
      layer = mk('div', 'gg-noise');
      layer.appendChild(mk('div', 'gg-nz-kicker', (name || '') + '  /  ' + DUEL_COPY.revealRule));
      const vs = mk('div', 'gg-nz-vs');
      vs.appendChild(plate('you', you));
      const badge = mk('div', 'gg-nz-badge', DUEL_COPY.vs);
      badge.appendChild(mk('span', 'gg-nz-ring'));
      for (let i = 0; i < SPARKS; i++) {
        const s = mk('span', 'gg-nz-spark');
        const a = (i / SPARKS) * Math.PI * 2;
        const r = 60 + (i % 3) * 22;
        s.style.setProperty('--dx', Math.round(Math.cos(a) * r) + 'px');
        s.style.setProperty('--dy', Math.round(Math.sin(a) * r) + 'px');
        badge.appendChild(s);
      }
      vs.appendChild(badge);
      vs.appendChild(plate('them', them));
      layer.appendChild(vs);
      if (hint) layer.appendChild(mk('p', 'gg-nz-hint', DUEL_COPY.firstHint));
      host.appendChild(layer);
    },
    pick({ secondsLeft = 8, theirs: t0 = '', preview: pv = null, onPick: op = null } = {}) {
      if (!layer) return;
      total = Math.max(1, secondsLeft | 0);
      preview = pv;
      onPick = op;
      theirs = t0 || '';
      const head = mk('div', 'gg-nz-pickhead');
      clock = mk('div', 'gg-nz-clock');
      clock.innerHTML = '<svg viewBox="0 0 48 48" aria-hidden="true"><circle class="bg" cx="24" cy="24" r="' + CLOCK_R + '"/>'
        + '<circle class="fg" cx="24" cy="24" r="' + CLOCK_R + '" stroke-dasharray="' + CLOCK_LEN.toFixed(2) + '" stroke-dashoffset="0"/></svg>';
      clock.appendChild(mk('b', '', String(total)));
      const words = mk('div', '');
      words.appendChild(mk('div', 'gg-nz-title', DUEL_COPY.pickTitle));
      words.appendChild(mk('p', 'gg-nz-line', DUEL_COPY.pickLine));
      head.appendChild(clock);
      head.appendChild(words);
      const grid = mk('div', 'gg-nz-grid');
      noiseTiles().forEach((t, i) => {
        const btn = mk('button', 'gg-nz-tile');
        btn.type = 'button';
        btn.style.setProperty('--nz-tint', t.tint);
        btn.style.setProperty('--i', String(i));
        btn.setAttribute('aria-label', t.name);
        btn.appendChild(mk('span', 'gg-nz-glyph', t.glyph));
        btn.appendChild(mk('span', 'gg-nz-tname', t.name));
        btn.addEventListener('click', () => { if (typeof onPick === 'function') onPick(t.id); });
        grid.appendChild(btn);
        tiles.set(t.id, { btn, img: null });
      });
      status = mk('div', 'gg-nz-status');
      layer.appendChild(head);
      layer.appendChild(grid);
      layer.appendChild(status);
      refreshPreviews();
      if (theirs) tag(theirs, 'them', false);
      paintStatus();
      // Next frame, so the tiles transition in from their resting offset.
      const lift = () => { if (layer) layer.classList.add('is-pick'); };
      if (typeof requestAnimationFrame === 'function') requestAnimationFrame(() => requestAnimationFrame(lift)); else lift();
    },
    pickTimer(left) {
      if (!clock) return;
      const n = Math.max(0, left | 0);
      const b = clock.querySelector('b');
      if (b) b.textContent = String(n);
      const fg = clock.querySelector('.fg');
      if (fg) fg.setAttribute('stroke-dashoffset', (CLOCK_LEN * (1 - n / total)).toFixed(2));
      clock.classList.toggle('is-low', n <= 3 && !mine);
      refreshPreviews();
    },
    picked({ who, set, rolled } = {}) {
      if (!layer || !set) return;
      if (who === 'you') {
        if (mine) return;
        mine = set;
        for (const [, t] of tiles) t.btn.disabled = true;
        layer.classList.add('is-picked');
        if (clock) clock.classList.remove('is-low');
      } else {
        if (theirs && theirs !== set) return;
        theirs = set;
      }
      tag(set, who, !!rolled);
      paintStatus();
    },
    lock() {
      if (!layer) return;
      for (const [, t] of tiles) t.btn.disabled = true;
      layer.classList.add('is-picked', 'is-lock');
    },
    /** The class is coming up (or the duel ended): play the OUT, then remove. */
    clear(animate = true) {
      if (!layer) return;
      if (!animate) { drop(); return; }
      const l = layer;
      layer = null;
      l.classList.add('is-out');
      setTimeout(() => { try { l.remove(); } catch (_e) { /* gone */ } }, NOISE_OUT_MS);
      tiles = new Map(); status = null; clock = null; preview = null; onPick = null; mine = ''; theirs = '';
    },
    get active() { return !!layer; },
  };
}

export default createNoiseView;
