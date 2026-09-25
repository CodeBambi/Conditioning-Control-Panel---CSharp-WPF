/* ============================================================================
 * ui/duel/duelView.js - the game night duel overlay. DOM only, no rules.
 *
 * Owner, 2026-09-23: "I do not want a mockup, I want the actual game ...
 * everything directly in that window as is on the arcademy". So the board in
 * here is not ours any more: `startGame` mounts the REAL Arcademy class (The
 * Deep End's grid with its own tiles, casino light and engine effects; Sort's
 * card swipe) through ui/duel/arcademyHost.js into a full-window stage, and
 * this file only frames it: the "incoming game" card before, a thin strip with
 * the duel clock during, the stamped result after.
 *
 * One overlay on <body> at z56: over the HUD (z40) and the toasts (z50), UNDER
 * Mercy (z60), so Mercy is reachable during a duel. The overlay is a stacking
 * context, so the class's own layers (its engine fx, its ceremonies) stay
 * inside it. Escape is NOT taken: boot.js owns the Escape ladder (mercy, then
 * hold to exit), and a mercy leaves Live, which closes the duel and tears the
 * class down through the controller.
 *
 * The stylesheet is injected once from here so the feature stays in its own
 * files.
 * ==========================================================================*/

import { DUEL_COPY } from './copy.js';
import { duelGame } from './games.js';
import { tileValue } from './rules.js';
import { mountArcademyGame } from './arcademyHost.js';
import { media as goonMedia } from '../../exec/media.js';
import { createNoiseView } from './noiseView.js';

// The prefix is gg-nduel, NOT gg-duel: the lobby's YOU vs THEM card owns .gg-duel and
// .gg-duel-name, and this sheet's fixed full-screen .gg-duel lifted that card out of the
// lobby and over the terms (owner, 2026-09-23).
const STYLE_ID = 'gg-nduel-style';

const CSS = `
.gg-nduel { position: fixed; inset: 0; z-index: 56; pointer-events: none; isolation: isolate; }
.gg-nduel[hidden] { display: none; }
.gg-nduel-dim { position: absolute; inset: 0; background: rgba(8, 3, 14, 0.55); opacity: 0;
  transition: opacity 260ms ease-out; }
.gg-nduel.is-card .gg-nduel-dim { opacity: 1; }
.gg-nduel-stage { position: absolute; inset: 0; pointer-events: auto; background: #14142B; overflow: hidden;
  isolation: isolate; opacity: 0; transform: scale(0.96); transition: opacity 320ms ease-out,
  transform 340ms cubic-bezier(.2, 1.5, .4, 1); }
.gg-nduel.is-play .gg-nduel-stage { opacity: 1; transform: none; }
.gg-nduel-stage[hidden] { display: none; }
/* The bottom band stays clear: Mercy (z60, the HUD's #gg-mercy) sits there over the duel, and the
   class centres its board in what is left. The real band is MEASURED off #gg-mercy (fitRoot); this
   is only the floor for a page that has no Mercy button yet. */
.gg-nduel-classroot { position: absolute; left: 0; right: 0; top: 0; bottom: 124px; overflow: hidden; }
.gg-nduel-fx { position: absolute; inset: 0; z-index: 40; pointer-events: none; overflow: hidden; }
.gg-nduel-cer { position: absolute; inset: 0; z-index: 45; pointer-events: none; }
.gg-nduel-strip { position: absolute; left: 12px; top: 10px; z-index: 50;
  display: flex; gap: 0.8rem; align-items: baseline; padding: 0.3rem 0.9rem; border-radius: 999px;
  background: rgba(24, 10, 34, 0.86); border: 1px solid rgba(255, 105, 180, 0.55); color: #fff;
  font-family: var(--gg-font, system-ui, sans-serif); font-variant-numeric: tabular-nums; pointer-events: none; }
.gg-nduel-strip b { color: var(--gg-pink, #ff69b4); letter-spacing: 0.08em; text-transform: uppercase; font-size: 0.8rem; }
.gg-nduel-time { font-size: 1rem; font-weight: 800; }
.gg-nduel-time.is-low { color: #ff6b8a; }
.gg-nduel-card { position: absolute; left: 50%; top: 50%; z-index: 60; pointer-events: auto;
  width: min(22rem, 90vw); padding: 1rem 1rem 1.1rem; border-radius: 16px;
  background: rgba(24, 10, 34, 0.96); border: 2px solid var(--gg-pink, #ff69b4);
  box-shadow: 0 0 36px rgba(255, 105, 180, 0.35); color: #fff; text-align: center;
  font-family: var(--gg-font, system-ui, sans-serif); transform: translate(-50%, -50%);
  animation: ggDuelCardIn 300ms cubic-bezier(.2, 1.5, .4, 1) both; }
.gg-nduel-card[hidden] { display: none; }
.gg-nduel.is-notice .gg-nduel-dim { opacity: 0; }
.gg-nduel.is-notice .gg-nduel-card { top: 12%; width: min(360px, 75vw); transform: translateX(-50%); pointer-events: none; padding: .6rem 1rem; animation: none; }
.gg-nduel.is-notice .gg-nduel-stamp { font-size: 1rem; }
.gg-nduel.is-notice .gg-nduel-pot { font-size: 1.2rem; }
.gg-nduel.is-notice .gg-nduel-fine { margin: .2rem 0; }
.gg-nduel-stamp { display: inline-block; margin: 0.2rem 0 0.4rem; padding: 0.25rem 0.8rem;
  border: 4px solid currentColor; border-radius: 8px; font-weight: 900; letter-spacing: 0.08em;
  text-transform: uppercase; font-size: 1.7rem; color: var(--gg-pink, #ff69b4); transform: rotate(-6deg);
  animation: ggDuelStamp 380ms cubic-bezier(.2, 1.6, .4, 1) both; }
.gg-nduel-stamp.is-win { color: #6effc8; }
.gg-nduel-stamp.is-lose { color: #9a8fb0; }
.gg-nduel-pot { font-weight: 900; font-size: 1.6rem; margin: 0 0 0.3rem; color: #c9b8e6;
  font-variant-numeric: tabular-nums; }
.gg-nduel-pot.is-win { color: #ffd36e; text-shadow: 0 0 14px rgba(255, 211, 110, 0.55); }
.gg-nduel-name { font-weight: 800; font-size: 1.1rem; margin: 0.2rem 0; }
.gg-nduel-fine { opacity: 0.75; font-size: 0.85rem; margin: 0.25rem 0; }
.gg-nduel-hint { margin: 0.5rem 0 0; font-size: 0.9rem; color: #ffd36e; }
@keyframes ggDuelStamp { 0% { transform: rotate(-6deg) scale(2.4); opacity: 0; } 100% { transform: rotate(-6deg) scale(1); opacity: 1; } }
@keyframes ggDuelCardIn { 0% { transform: translate(-50%, -50%) scale(0.7); opacity: 0; } 100% { transform: translate(-50%, -50%) scale(1); opacity: 1; } }
html[data-gg-motion="reduced"] .gg-nduel-stamp, html[data-gg-motion="reduced"] .gg-nduel-card { animation: none; }
html[data-gg-motion="reduced"] .gg-nduel-stage { transition: opacity 200ms linear; transform: none; }
@media (prefers-reduced-motion: reduce) {
  .gg-nduel-stamp, .gg-nduel-card { animation: none; }
  .gg-nduel-stage { transition: opacity 200ms linear; transform: none; }
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

/** One result line: a tiled game names the tile, any other game its points. */
export function resultLine(who, r) {
  const g = duelGame(r && r.game);
  const score = Math.max(0, (r && r.score) | 0);
  if (!g || g.tiled) return (who === 'you' ? DUEL_COPY.youLine : DUEL_COPY.themLine)(tileValue(r && r.tile), score);
  return (who === 'you' ? DUEL_COPY.youPoints : DUEL_COPY.themPoints)(score);
}

/**
 * @param {object} [o]
 * @param {object} [o.media]   exec/media.js pool (default: the page's)
 * @param {Function} [o.mount] the class mounter (default: arcademyHost.mountArcademyGame)
 * @param {Function} [o.onLog]
 * @param {object} [o.volume]  {level(), subscribe(fn)} the Goon mix the class synth follows
 * @returns {object} the view handle ui/duel/duelController.js drives, or null without a DOM.
 */
export function createDuelView({ media = goonMedia, mount = mountArcademyGame, onLog = null, volume = null } = {}) {
  const d = typeof document !== 'undefined' ? document : null;
  if (!d || !d.body) return null;
  injectDuelStyle(d);

  const mk = (tag, cls, text) => {
    const n = d.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = String(text);
    return n;
  };
  const say = (m) => { try { if (typeof onLog === 'function') onLog({ t: 'duel-view', msg: String(m) }); } catch (_e) { /* never */ } };

  const root = mk('div', 'gg-nduel');
  root.hidden = true;
  const dim = mk('div', 'gg-nduel-dim');
  const stage = mk('div', 'gg-nduel-stage');
  stage.hidden = true;
  const card = mk('div', 'gg-nduel-card');
  card.hidden = true;
  root.appendChild(dim);
  root.appendChild(stage);
  root.appendChild(card);
  d.body.appendChild(root);
  // The Sort duel's VS reveal + noise pick (ui/duel/noiseView.js): its own layer in this overlay.
  const noise = createNoiseView({ d, host: root });
  const noiseOut = (animate) => { try { if (noise) noise.clear(animate); } catch (_e) { /* scenery */ } };

  let timeEl = null;
  let fitted = null;      // the class root being kept clear of Mercy
  let live = null;        // the mounted class handle

  /** Keep the class above Mercy: nothing of the real game may sit under the bottom bar. */
  const BOTTOM_FLOOR_PX = 124;
  const BOTTOM_GAP_PX = 12;
  function fitRoot() {
    if (!fitted) return;
    let bottom = BOTTOM_FLOOR_PX;
    try {
      const m = d.getElementById('gg-mercy');
      const vh = (typeof window !== 'undefined' && window.innerHeight) || 0;
      if (m && vh && typeof m.getBoundingClientRect === 'function') {
        const r = m.getBoundingClientRect();
        if (r.height > 0 && r.top > vh / 2) bottom = Math.max(BOTTOM_FLOOR_PX, Math.ceil(vh - r.top + BOTTOM_GAP_PX));
      }
    } catch (_e) { /* the floor stands */ }
    fitted.style.bottom = bottom + 'px';
  }
  const onResize = () => fitRoot();
  let gen = 0;            // bumps on every close: a class that lands late is torn straight down

  const clearCard = () => { while (card.firstChild) card.removeChild(card.firstChild); };
  function showCard(compact = false) {
    root.classList.toggle('is-notice', compact);
    clearCard();
    card.hidden = false;
    root.classList.add('is-card');
    // restart the entrance
    card.style.animation = 'none'; void card.offsetWidth; card.style.animation = '';
  }
  function hideCard() { root.classList.remove('is-notice'); card.hidden = true; root.classList.remove('is-card'); clearCard(); }

  function killStage() {
    const h = live;
    live = null;
    if (h) { try { h.destroy(); } catch (_e) { /* torn down anyway */ } }
    while (stage.firstChild) stage.removeChild(stage.firstChild);
    stage.hidden = true;
    root.classList.remove('is-play');
    timeEl = null;
    if (fitted) { fitted = null; try { window.removeEventListener('resize', onResize); } catch (_e) { /* gone */ } }
  }

  return {
    bind() { /* the class binds its own input */ },
    intro({ by, name, rule, hint }) {
      gen++;
      killStage();
      noiseOut(false);
      showCard();
      card.appendChild(mk('div', 'gg-nduel-stamp', by === 'you' ? DUEL_COPY.incomingYou : DUEL_COPY.incomingThem));
      card.appendChild(mk('div', 'gg-nduel-name', name || DUEL_COPY.gameName));
      if (rule) card.appendChild(mk('p', 'gg-nduel-fine', rule));
      if (hint) card.appendChild(mk('p', 'gg-nduel-hint', DUEL_COPY.firstHint));
      root.hidden = false;
    },
    /** Sort duel: both niches face to face. The card and stage stay down; the layer owns the screen. */
    reveal(o = {}) {
      gen++;
      killStage();
      hideCard();
      root.hidden = false;
      if (noise) noise.reveal(o);
    },
    pick(o) { if (noise) noise.pick(o); },
    pickTimer(left) { if (noise) noise.pickTimer(left); },
    picked(o) { if (noise) noise.picked(o); },
    lock(o) { if (noise) noise.lock(o); },
    /** The class is about to mount: open the stage, with the duel strip over it. */
    play({ game, secondsLeft } = {}) {
      hideCard();
      noiseOut(true);
      root.hidden = false;
      stage.hidden = false;
      const row = duelGame(game);
      const strip = mk('div', 'gg-nduel-strip');
      strip.appendChild(mk('b', '', DUEL_COPY.stripLabel(row ? row.name : DUEL_COPY.gameName)));
      timeEl = mk('span', 'gg-nduel-time', secondsLeft != null ? DUEL_COPY.timeLeft(secondsLeft) : '');
      strip.appendChild(timeEl);
      stage.appendChild(strip);
      const lift = () => root.classList.add('is-play');
      if (typeof requestAnimationFrame === 'function') requestAnimationFrame(lift); else lift();
    },
    /**
     * Mount the real class into the stage. Resolves to {result(), destroy()} or null.
     * Called by the controller right after play().
     */
    async startGame({ game, seed, len, onEnd, noise: piles = null }) {
      const my = ++gen;
      const classRoot = mk('div', 'gg-nduel-classroot');
      const fx = mk('div', 'gg-nduel-fx');
      const cer = mk('div', 'gg-nduel-cer');
      stage.insertBefore(cer, stage.firstChild);
      stage.insertBefore(fx, cer);
      stage.insertBefore(classRoot, fx);
      fitted = classRoot;
      fitRoot();
      try { window.addEventListener('resize', onResize); } catch (_e) { /* headless */ }
      let h = null;
      try {
        h = await mount({
          root: classRoot, fxLayer: fx, ceremonyLayer: cer, game, seed, lenSec: len, media,
          reduced: reduced(d), onEnd, log: say, volume, noise: piles,
        });
      } catch (e) { say('mount threw: ' + ((e && e.message) || e)); h = null; }
      if (!h) {
        if (my === gen) classRoot.appendChild(mk('p', 'gg-nduel-fine', DUEL_COPY.unavailable));
        return null;
      }
      if (my !== gen) { try { h.destroy(); } catch (_e) { /* gone */ } return null; }
      live = h;
      return {
        result: () => h.result(),
        log: () => (typeof h.log === 'function' ? h.log() : []),
        destroy: () => { if (live === h) killStage(); else { try { h.destroy(); } catch (_e) { /* gone */ } } },
      };
    },
    timer(left) {
      if (!timeEl) return;
      timeEl.textContent = DUEL_COPY.timeLeft(left);
      timeEl.classList.toggle('is-low', left <= 10);
    },
    waiting() {
      gen++;
      killStage();
      noiseOut(false);
      showCard(true);
      card.appendChild(mk('p', 'gg-nduel-fine', DUEL_COPY.waiting));
    },
    result({ outcome, bonus, pot = false, mine, theirs }) {
      gen++;
      killStage();
      showCard(true);
      // Points model: the pot line shows what each side took, the winner's in gold and the
      // loser's (and a tie's) half in a softer colour. A legacy match keeps "won +N".
      const word = outcome === 'win' ? (pot ? DUEL_COPY.wonPlain : DUEL_COPY.won(bonus))
        : outcome === 'lose' ? DUEL_COPY.lost : DUEL_COPY.tied;
      card.appendChild(mk('div', 'gg-nduel-stamp is-' + outcome, word));
      if (pot && bonus > 0) card.appendChild(mk('div', 'gg-nduel-pot' + (outcome === 'win' ? ' is-win' : ''), DUEL_COPY.potLine(bonus)));
      card.appendChild(mk('p', 'gg-nduel-fine', resultLine('you', mine)));
      card.appendChild(mk('p', 'gg-nduel-fine', resultLine('them', theirs)));
    },
    close() { gen++; killStage(); noiseOut(false); hideCard(); root.hidden = true; },
    dispose() {
      gen++;
      killStage();
      noiseOut(false);
      try { root.remove(); } catch (_e) { /* gone */ }
    },
  };
}

export default createDuelView;
