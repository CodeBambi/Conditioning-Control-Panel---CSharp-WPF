/* ============================================================================
 * ui/screens/customize.js - the lobby's Customize expander (game night).
 *
 * Closed by default. A first match asks one new thing (pick a song) and
 * everything else lives in here on defaults. Today it holds one control: the
 * game card duel length. The host's pick wins and rides the duel frames, so a
 * guest's setting only matters in the matches they host.
 *
 * Storage is a plain localStorage key in try/catch: the value is a local
 * preference, never a term, never on the consent sheet.
 * ==========================================================================*/

import { el } from '../router.js';
import { DUEL_LENGTHS_SEC } from '../../core/contracts.js';
import { pickLength } from '../duel/rules.js';
import { DUEL_COPY } from '../duel/copy.js';
import { injectDuelStyle } from '../duel/duelView.js';

const LEN_KEY = 'goon.night.duelLen.v1';

/** The duel length this player picked (seconds, one of DUEL_LENGTHS_SEC). */
export function getDuelLength() {
  try {
    return pickLength(typeof localStorage !== 'undefined' ? localStorage.getItem(LEN_KEY) : null);
  } catch (_e) { return pickLength(null); }
}

export function setDuelLength(sec) {
  const v = pickLength(sec);
  try { if (typeof localStorage !== 'undefined') localStorage.setItem(LEN_KEY, String(v)); } catch (_e) { /* kept in memory only */ }
  return v;
}

/**
 * The expander. Returns a node, or null where there is no DOM.
 * @param {{isHost?:boolean}} [o]
 */
export function customizeSection({ isHost = true } = {}) {
  if (typeof document === 'undefined') return null;
  injectDuelStyle(document);
  const current = getDuelLength();
  const choices = DUEL_LENGTHS_SEC.map((sec) => {
    const b = el('button', {
      type: 'button',
      class: 'gg-btn gg-btn--ghost gg-cust-pick' + (sec === current ? ' is-on' : ''),
      'aria-pressed': sec === current ? 'true' : 'false',
      text: DUEL_COPY.lengthChoice(sec),
    });
    b.addEventListener('click', () => {
      setDuelLength(sec);
      for (const other of choices) {
        const on = other === b;
        other.classList.toggle('is-on', on);
        other.setAttribute('aria-pressed', on ? 'true' : 'false');
      }
    });
    return b;
  });
  return el('details', { class: 'gg-customize' }, [
    el('summary', { class: 'gg-customize-sum', text: DUEL_COPY.customize }),
    el('div', { class: 'gg-cust-row' }, [
      el('span', { class: 'gg-cust-label', text: DUEL_COPY.lengthLabel }),
      el('div', { class: 'gg-cust-picks' }, choices),
    ]),
    isHost ? null : el('p', { class: 'gg-cust-fine', text: DUEL_COPY.hostWins }),
  ]);
}

export default { customizeSection, getDuelLength, setDuelLength };
