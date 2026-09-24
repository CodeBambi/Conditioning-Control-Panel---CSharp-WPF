/* ============================================================================
 * ui/startGuide.js - "How to win": the three ways to score, drawn once.
 *
 * Three cards side by side, each with its own small looping scene, plus a big
 * title. The countdown screen mounts it INSIDE its own stage (so the router's
 * leave animation is the OUT for the whole screen the frame play begins) and
 * the options drawer can reopen it as a sheet mid-match.
 *
 * Rules that are load-bearing:
 *   - It never touches the clock. The countdown numeral is the shared-clock
 *     readout and keeps painting under this; the cards are decoration with the
 *     same lifetime as the screen they sit in.
 *   - Every loop is CSS (ui/startGuide.css). Nothing here schedules a frame,
 *     so there is nothing to park under reduced motion: the rails in the CSS
 *     switch the animations off and the base styles ARE the static poses.
 *   - Import-safe under node: no DOM at import time, and mount refuses politely
 *     when there is no document.
 * ==========================================================================*/

import { el } from './router.js';
import { S } from './strings.js';

/** The cards, in reading order. Exported so a test can count them. */
export const GUIDE_CARDS = Object.freeze(['pop', 'throw', 'duel']);

/** When the cards start arriving over the countdown, after the VS slam. */
export const GUIDE_ENTER_MS = 900;

const CROWN_SVG = '<svg viewBox="0 0 24 18" width="22" height="16" aria-hidden="true">'
  + '<path d="M2 15h20l-1.6-9.4-4.9 3.6L12 2 8.5 9.2 3.6 5.6Z" fill="currentColor"/>'
  + '<rect x="2.6" y="15" width="18.8" height="2.4" rx="1" fill="currentColor" opacity=".7"/></svg>';

function hasDom() {
  return typeof document !== 'undefined' && !!document && typeof document.createElement === 'function';
}

function sparks(n) {
  const out = [];
  for (let i = 0; i < n; i++) out.push(el('i', { class: 'gsc-spark', style: '--a:' + Math.round((i * 360) / n) + 'deg' }));
  return out;
}

/* Scene 1: a bubble pops into sparks, a picture tile gets clicked, +3 floats. */
function scenePop() {
  return el('div', { class: 'gsc gsc--pop', 'aria-hidden': 'true' }, [
    el('div', { class: 'gsc-bubble-wrap' }, [el('i', { class: 'gsc-bubble' })].concat(sparks(6))),
    el('div', { class: 'gsc-tile-wrap' }, [
      el('i', { class: 'gsc-tile' }),
      el('i', { class: 'gsc-click' }),
      el('b', { class: 'gsc-plus', text: '+3' }),
    ]),
  ]);
}

/* Scene 2: an item arcs across to the opponent, lands with a ring, + chip. */
function sceneThrow() {
  return el('div', { class: 'gsc gsc--throw', 'aria-hidden': 'true' }, [
    el('div', { class: 'gsc-track' }, [
      el('div', { class: 'gsc-arc' }, [el('i', { class: 'gsc-item' })]),
    ]),
    el('div', { class: 'gsc-ava-wrap' }, [
      el('i', { class: 'gsc-impact' }),
      el('i', { class: 'gsc-ava' }),
      el('b', { class: 'gsc-bonus', text: '+' }),
    ]),
  ]);
}

/* Scene 3: two score bars race, the crown lands on the winner. */
function sceneDuel(you, them) {
  return el('div', { class: 'gsc gsc--duel', 'aria-hidden': 'true' }, [
    el('div', { class: 'gsc-lane gsc-lane--you' }, [
      el('span', { class: 'gsc-lane-name', text: you }),
      el('span', { class: 'gsc-lane-track' }, [el('i', { class: 'gsc-bar' })]),
    ]),
    el('div', { class: 'gsc-lane gsc-lane--them' }, [
      el('span', { class: 'gsc-lane-name', text: them }),
      el('span', { class: 'gsc-lane-track' }, [el('i', { class: 'gsc-bar' })]),
    ]),
    el('b', { class: 'gsc-crown', html: CROWN_SVG }),
  ]);
}

function card(id, index, scene, title, line) {
  return el('article', { class: 'gg-guide-card gg-guide-card--' + id, style: '--i:' + index }, [
    scene,
    el('h3', { class: 'gg-guide-card-title', text: title }),
    el('p', { class: 'gg-guide-card-line', text: line }),
  ]);
}

/**
 * The guide as a node. Pure DOM construction; the caller decides where it lives.
 * @param {{you?:string, them?:string, enterMs?:number}} [o]
 */
export function buildGuide({ you = null, them = null, enterMs = GUIDE_ENTER_MS } = {}) {
  if (!hasDom()) return null;
  const g = S.guide;
  const youName = you || S.lobby.you;
  const themName = them || S.lobby.them;
  const scenes = {
    pop: scenePop(),
    throw: sceneThrow(),
    duel: sceneDuel(youName, themName),
  };
  const copy = {
    pop: [g.popTitle, g.popLine],
    throw: [g.throwTitle, g.throwLine],
    duel: [g.duelTitle, g.duelLine],
  };
  const cards = el('div', { class: 'gg-guide-cards' },
    GUIDE_CARDS.map((id, i) => card(id, i, scenes[id], copy[id][0], copy[id][1])));
  const root = el('section', { class: 'gg-guide', role: 'note', 'aria-label': g.title }, [
    el('h2', { class: 'gg-guide-title gg-grad', text: g.title }),
    cards,
  ]);
  try { root.style.setProperty('--gg-guide-at', Math.max(0, enterMs | 0) + 'ms'); } catch (_e) { /* stub DOM */ }
  return root;
}

/**
 * Put the guide inside `host` (the countdown stage). The countdown's own
 * unmount, or the router's leave, removes it with the screen; remove() is for
 * a caller that wants it gone earlier.
 * @returns {{node:HTMLElement, remove:Function}|null}
 */
export function mountStartGuide({ host = null, you = null, them = null, enterMs = GUIDE_ENTER_MS } = {}) {
  if (!hasDom()) return null;
  const parent = host || document.body;
  if (!parent || typeof parent.appendChild !== 'function') return null;
  const node = buildGuide({ you, them, enterMs });
  if (!node) return null;
  try { parent.appendChild(node); } catch (_e) { return null; }
  let gone = false;
  return {
    node,
    remove() {
      if (gone) return;
      gone = true;
      try { node.remove(); } catch (_e) { /* already gone */ }
    },
  };
}

/**
 * The guide as a sheet the options drawer can open mid-match: a scrim, the
 * cards, one button. Any click on the scrim or the button closes it. The
 * caller keeps the handle and closes it with its own chrome so a sheet can
 * never be the thing left eating bubble pops after the drawer is gone.
 * @returns {{node:HTMLElement, close:Function, isOpen:Function}|null}
 */
export function openGuideSheet({ host = null, you = null, them = null, onClose = null } = {}) {
  if (!hasDom()) return null;
  const parent = host || document.body;
  if (!parent || typeof parent.appendChild !== 'function') return null;
  const guide = buildGuide({ you, them, enterMs: 0 });
  if (!guide) return null;
  const done = el('button', { type: 'button', class: 'gg-btn gg-guide-done', text: S.guide.gotIt });
  const panel = el('div', { class: 'gg-guide-sheet-panel', role: 'dialog', 'aria-label': S.guide.title }, [guide, done]);
  const scrim = el('div', { class: 'gg-guide-sheet' }, [panel]);
  let open = true;
  function close() {
    if (!open) return;
    open = false;
    try { scrim.classList.add('is-leaving'); } catch (_e) { /* ignore */ }
    const drop = () => { try { scrim.remove(); } catch (_e) { /* gone */ } };
    try { setTimeout(drop, 220); } catch (_e) { drop(); }
    if (typeof onClose === 'function') { try { onClose(); } catch (_e) { /* ignore */ } }
  }
  try {
    done.addEventListener('click', (e) => { try { e.stopPropagation(); } catch (_e) { /* stub */ } close(); });
    scrim.addEventListener('click', close);
    panel.addEventListener('click', (e) => { try { e.stopPropagation(); } catch (_e) { /* stub */ } });
    parent.appendChild(scrim);
  } catch (_e) { return null; }
  const arm = () => { try { scrim.classList.add('is-in'); } catch (_e) { /* ignore */ } };
  if (typeof requestAnimationFrame === 'function') requestAnimationFrame(arm); else arm();
  return { node: scrim, close, isOpen: () => open };
}

export default { GUIDE_CARDS, GUIDE_ENTER_MS, buildGuide, mountStartGuide, openGuideSheet };
