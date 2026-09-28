/* ============================================================================
 * ui/screens/noiseSetup.js - "pick your noise", the pre-match step (2026-09-25).
 *
 * Owner: "the pick your noise should be right after the select the niche, not in
 * the sort game (before we play)". So the Sort duel's NOISE board (the pictures a
 * player bins, core/noiseSets.js) is picked here, once, right after the flavour
 * card, and the duel only shows it (ui/duel/duelController.js).
 *
 * NOT a router screen of its own: ui/screens/mediaSetup.js mounts it in place of
 * the flavour card after a pick (the same hold, the same screen), or on its own
 * with `noiseOnly` for a player whose flavour predates this step.
 *
 * RULES THE CARD KEEPS: no timer. One board is already selected (a roll, or the
 * board chosen before), and its pictures start fetching at once; a tap moves the
 * selection (and fetches that board too); Done remembers it (boot's mediaFlavour
 * api -> prefs `noiseSet`) and moves on after a short beat. The same seven tiles
 * as the old in-duel pick, borrowing its styles (ui/duel/noiseView.js).
 * ==========================================================================*/

import { el } from '../router.js';
import { DUEL_COPY } from '../duel/copy.js';
import { noiseTiles } from '../duel/noisePick.js';
import { injectNoiseStyle } from '../duel/noiseView.js';
import { clampNoiseSet, rollNoiseSet } from '../../core/noiseSets.js';

function reduced() {
  try { return document.documentElement.getAttribute('data-gg-motion') === 'reduced'; } catch (_e) { return false; }
}

/**
 * The board this card opens on: the one chosen before, else a roll. Pure.
 * @returns {{set: string, rolled: boolean}}
 */
export function initialNoise(chosen, rand = Math.random) {
  const pre = clampNoiseSet(chosen);
  return pre ? { set: pre, rolled: false } : { set: rollNoiseSet(rand), rolled: true };
}

/**
 * @param {HTMLElement} container
 * @param {object} o
 * @param {object} o.ledger  the screen's ledger (listen / timer / interval)
 * @param {object} o.api     boot's mediaFlavour: noise(), wantNoise(id), setNoise(id), noisePreview(id)
 * @param {object} [o.audio]
 * @param {Function} o.onDone called once, a beat after Done
 * @param {Function} [o.rand]
 */
export function mountNoiseCard(container, { ledger, api, audio = null, onDone, rand = Math.random } = {}) {
  try { injectNoiseStyle(document); } catch (_e) { /* the card still works unstyled */ }
  const sfx = (n) => { try { audio?.sfx?.(n); } catch (_e) { /* stub bus */ } };
  const start = initialNoise(api && typeof api.noise === 'function' ? api.noise() : '', rand);
  let sel = start.set;
  let rolled = start.rolled;
  let done = false;
  try { api?.wantNoise?.(sel); } catch (_e) { /* pictures are a nicety */ }

  const tiles = new Map();   // id -> {btn, img}
  const grid = el('div', { class: 'gg-nz-grid', role: 'radiogroup', 'aria-label': DUEL_COPY.pickTitle });

  function paintSel() {
    for (const [id, t] of tiles) {
      const on = id === sel;
      t.btn.classList.toggle('is-mine', on);
      t.btn.setAttribute('aria-checked', String(on));
      const old = t.btn.querySelector('.gg-nz-tag');
      if (old) old.remove();
      if (on) t.btn.appendChild(el('span', { class: 'gg-nz-tag is-you', text: rolled ? DUEL_COPY.rolled : DUEL_COPY.revealYou }));
    }
  }

  function paintPreviews() {
    if (!api || typeof api.noisePreview !== 'function') return;
    for (const [id, t] of tiles) {
      if (t.img) continue;
      let url = '';
      try { url = api.noisePreview(id) || ''; } catch (_e) { url = ''; }
      if (!url) continue;
      const img = el('img', { alt: '', decoding: 'async' });
      img.onload = () => { img.classList.add('is-on'); t.btn.classList.add('has-img'); };
      img.onerror = () => { try { img.remove(); } catch (_e) { /* gone */ } t.img = null; };
      img.src = url;
      t.img = img;
      t.btn.insertBefore(img, t.btn.firstChild);
    }
  }

  noiseTiles().forEach((t, i) => {
    const btn = el('button', {
      type: 'button', class: 'gg-nz-tile', role: 'radio', 'data-noise': t.id,
      'aria-label': t.name, style: '--nz-tint:' + t.tint + ';--i:' + i,
    }, [
      el('span', { class: 'gg-nz-glyph', text: t.glyph }),
      el('span', { class: 'gg-nz-tname', text: t.name }),
    ]);
    ledger.listen(btn, 'click', (e) => {
      e?.preventDefault?.();
      if (done || sel === t.id) return;
      sel = t.id;
      rolled = false;
      sfx('ui-move');
      try { api?.wantNoise?.(sel); } catch (_e) { /* pictures are a nicety */ }
      paintSel();
    });
    grid.appendChild(btn);
    tiles.set(t.id, { btn, img: null });
  });

  const go = el('button', { type: 'button', class: 'gg-btn gg-btn--primary gg-nz-go', text: DUEL_COPY.setupGo });
  ledger.listen(go, 'click', (e) => {
    e?.preventDefault?.();
    if (done) return;
    done = true;
    go.disabled = true;
    for (const [, t] of tiles) t.btn.disabled = true;
    sfx('ui-select');
    try { api?.setNoise?.(sel); } catch (_e) { /* the pick is never load-bearing */ }
    ledger.timer(() => { try { onDone?.(); } catch (_e) { /* ignore */ } }, reduced() ? 0 : 350);
  });

  const card = el('div', { class: 'gg-card gg-flv gg-nz-setup' }, [
    el('h2', { class: 'gg-nz-title', text: DUEL_COPY.pickTitle }),
    el('p', { class: 'gg-nz-line', text: DUEL_COPY.pickLine }),
    grid,
    el('div', { class: 'gg-flv-foot' }, [go]),
  ]);
  container.appendChild(card);
  paintSel();
  paintPreviews();
  // Board previews arrive when their pictures do: look again for a while, cheaply.
  if (typeof ledger.interval === 'function') ledger.interval(paintPreviews, 700);
  return card;
}

export default mountNoiseCard;
