/* ============================================================================
 * ui/duel/noiseView.js - the Sort duel's VS reveal. DOM only.
 *
 * Driven by ui/duel/duelController.js through ui/duel/duelView.js (reveal,
 * picked), torn down by the view's play / close. One layer inside the duel
 * overlay, above its dim and under Mercy like the rest of it.
 *
 *   REVEAL  two plates slam in face to face (yours from the left, theirs from
 *           the right), each naming its niche and its noise board, the VS badge
 *           drops between them with a ring and sparks. Their board may land a
 *           beat later (`picked`): it stamps onto their plate.
 *   OUT     the whole layer fades and lifts as the class comes up.
 *
 * The board itself is picked BEFORE the match (ui/screens/noiseSetup.js), which
 * borrows the tile styles below (injectNoiseStyle, .gg-nz-setup).
 *
 * Every entrance has an exit, and html[data-gg-motion="reduced"],
 * prefers-reduced-motion and html[data-gg-perf="lite"] drop the motion (the
 * layer still shows, cross-fades only).
 * ==========================================================================*/

import { DUEL_COPY } from './copy.js';
import { noiseName } from './noisePick.js';

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
.gg-nz-grid { display: flex; flex-wrap: wrap; justify-content: center; gap: 0.6rem; width: min(40rem, 94vw); }
.gg-nz-grid > .gg-nz-tile { flex: 0 0 calc((100% - 1.8rem) / 4); }
.gg-nz-tile { position: relative; aspect-ratio: 4 / 3; border-radius: 12px; overflow: hidden; cursor: pointer;
  border: 2px solid color-mix(in srgb, var(--nz-tint, #b99cff) 60%, transparent); padding: 0; color: #fff;
  background: linear-gradient(160deg, color-mix(in srgb, var(--nz-tint, #b99cff) 30%, #1c0b2a), #120718);
  font: inherit; display: grid; place-items: center; opacity: 0; transform: translateY(18px) scale(0.9);
  transition: transform 180ms cubic-bezier(.2, 1.4, .4, 1), opacity 220ms ease, border-color 160ms ease, filter 220ms ease, box-shadow 200ms ease; }
.gg-nz-setup .gg-nz-tile { opacity: 1; transform: none; animation: ggNzRise 320ms calc(var(--i, 0) * 45ms) cubic-bezier(.2, 1.3, .4, 1) both; }
.gg-nz-setup .gg-nz-tile:hover:not(:disabled) { transform: translateY(-3px) scale(1.04); transition-delay: 0ms;
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
.gg-nz-setup .gg-nz-tile:not(.is-mine) { filter: saturate(0.55) brightness(0.72); }
.gg-nz-setup .gg-nz-tile:not(.is-mine):hover { filter: none; }
.gg-nz-tile.is-mine { border-color: var(--gg-gold, #ffd36e); box-shadow: 0 0 24px rgba(255, 211, 110, 0.55); animation: ggNzPop 380ms cubic-bezier(.2, 1.8, .4, 1); }
.gg-nz-pnoise { font-size: 0.82rem; margin-top: 0.35rem; opacity: 0.9; }
.gg-nz-pnoise b { color: var(--gg-gold, #ffd36e); }
.gg-nz-plate.is-them .gg-nz-pnoise b { color: #c9b8ff; }
.gg-nz-pnoise.is-new b { display: inline-block; animation: ggNzTag 360ms cubic-bezier(.2, 1.8, .4, 1) both; }
.gg-nz-setup { display: flex; flex-direction: column; align-items: center; gap: 0.9rem; text-align: center; }
.gg-nz-setup .gg-nz-title { font-size: 1.35rem; font-weight: 900; color: var(--gg-pink, #ff69b4); margin: 0; }
.gg-nz-setup .gg-nz-line { font-size: 0.9rem; opacity: 0.75; margin: 0; }
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
@keyframes ggNzRise { from { opacity: 0; transform: translateY(18px) scale(0.9); } to { opacity: 1; transform: none; } }
@keyframes ggNzPop { 0% { transform: scale(1); } 45% { transform: scale(1.1); } 100% { transform: scale(1); } }
html[data-gg-motion="reduced"] .gg-noise *, html[data-gg-perf="lite"] .gg-noise * { animation: none !important; }
html[data-gg-motion="reduced"] .gg-nz-spark, html[data-gg-motion="reduced"] .gg-nz-ring,
html[data-gg-perf="lite"] .gg-nz-spark, html[data-gg-perf="lite"] .gg-nz-ring { display: none; }
html[data-gg-motion="reduced"] .gg-nz-tile, html[data-gg-motion="reduced"] .gg-nz-vs { transform: none !important; transition: opacity 200ms linear; }
html[data-gg-motion="reduced"] .gg-nz-setup *, html[data-gg-perf="lite"] .gg-nz-setup * { animation: none !important; }
html[data-gg-motion="reduced"] .gg-noise.is-out { animation: ggNzIn 200ms linear reverse both !important; }
@media (prefers-reduced-motion: reduce) {
  .gg-noise *, .gg-nz-setup * { animation: none !important; }
  .gg-nz-spark, .gg-nz-ring { display: none; }
  .gg-nz-tile, .gg-nz-vs { transform: none !important; transition: opacity 200ms linear; }
  .gg-noise.is-out { animation: ggNzIn 200ms linear reverse both; }
}
`;

/** The layer's styles, once per document. ui/screens/noiseSetup.js borrows the tiles. */
export function injectNoiseStyle(d) {
  if (!d || !d.head || d.getElementById(STYLE_ID)) return;
  const s = d.createElement('style');
  s.id = STYLE_ID;
  s.textContent = CSS;
  d.head.appendChild(s);
}

const SPARKS = 10;

/**
 * @param {object} o
 * @param {Document} o.d
 * @param {HTMLElement} o.host  the duel overlay root
 */
export function createNoiseView({ d, host }) {
  if (!d || !host) return null;
  injectNoiseStyle(d);
  const mk = (tag, cls, text) => {
    const n = d.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = String(text);
    return n;
  };

  let layer = null;
  const boards = { you: null, them: null };   // side -> its plate's noise line
  let theirs = '';

  function paintBoard(side, set, fresh) {
    const line = boards[side];
    if (!line) return;
    while (line.firstChild) line.removeChild(line.firstChild);
    line.classList.toggle('is-new', !!fresh);
    line.appendChild(d.createTextNode((side === 'you' ? DUEL_COPY.pickedYou : DUEL_COPY.pickedThem) + ' '));
    line.appendChild(mk('b', '', set ? noiseName(set) : '...'));
  }

  function plate(side, label, set) {
    const p = mk('div', 'gg-nz-plate is-' + side);
    p.style.setProperty('--nz-tint', (label && label.tint) || '#ff69b4');
    p.appendChild(mk('div', 'gg-nz-side', side === 'you' ? DUEL_COPY.revealYou : DUEL_COPY.revealThem));
    p.appendChild(mk('div', 'gg-nz-name', (label && label.name) || DUEL_COPY.houseMix));
    const subs = label && Array.isArray(label.subs) && label.subs.length
      ? label.subs.slice(0, 3).map((s) => 'r/' + s).join('  ') + (label.more ? '  ' + label.more : '')
      : '';
    if (subs) p.appendChild(mk('div', 'gg-nz-subs', subs));
    boards[side] = mk('div', 'gg-nz-pnoise');
    p.appendChild(boards[side]);
    paintBoard(side, set, false);
    return p;
  }

  function drop() {
    if (layer) { try { layer.remove(); } catch (_e) { /* gone */ } }
    layer = null; boards.you = null; boards.them = null; theirs = '';
  }

  return {
    reveal({ name, you, them, youNoise = '', themNoise = '', hint } = {}) {
      drop();
      layer = mk('div', 'gg-noise');
      layer.appendChild(mk('div', 'gg-nz-kicker', (name || '') + '  /  ' + DUEL_COPY.revealRule));
      const vs = mk('div', 'gg-nz-vs');
      theirs = themNoise || '';
      vs.appendChild(plate('you', you, youNoise));
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
      vs.appendChild(plate('them', them, theirs));
      layer.appendChild(vs);
      if (hint) layer.appendChild(mk('p', 'gg-nz-hint', DUEL_COPY.firstHint));
      host.appendChild(layer);
    },
    /** Their board landed after the reveal opened: stamp it onto their plate. */
    picked({ who, set } = {}) {
      if (!layer || !set || who !== 'them' || theirs) return;
      theirs = set;
      paintBoard('them', set, true);
    },
    /** The class is coming up (or the duel ended): play the OUT, then remove. */
    clear(animate = true) {
      if (!layer) return;
      if (!animate) { drop(); return; }
      const l = layer;
      layer = null;
      l.classList.add('is-out');
      setTimeout(() => { try { l.remove(); } catch (_e) { /* gone */ } }, NOISE_OUT_MS);
      boards.you = null; boards.them = null; theirs = '';
    },
    get active() { return !!layer; },
  };
}

export default createNoiseView;
