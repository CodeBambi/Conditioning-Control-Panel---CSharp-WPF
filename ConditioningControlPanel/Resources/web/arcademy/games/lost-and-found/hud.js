/* ============================================================================
 * games/lost-and-found/hud.js - chrome, cards and the two overlays.
 *
 * Everything the player reads. Three rules it obeys:
 *
 *  1. EVERY string goes through ctx.lexicon (t(key, fallback)) - the mod skin
 *     changes display strings only, and this game never reads modId. Numbers
 *     (4 / 5, 00:47) are numbers, so they need no key.
 *  2. The peek control is BUILT here but OWNED by the shell: we hand the node to
 *     ctx.peek.attach() and never track the A-cap ourselves (peek.js trap 9).
 *     lf_peek_input resolves auto -> tap-toggle on a coarse pointer, else hold.
 *  3. The briefing / peek / spotlight cards render the target through the same
 *     paintLook() the board uses, so they are the same look on the same DOM
 *     layer - never a canvas copy of tainted media.
 * ==========================================================================*/

import { paintLook } from './board.js';
import { el } from './util.js';

function fmtClock(sec) {
  const s = Math.max(0, Math.round(Number(sec) || 0));
  const m = Math.floor(s / 60);
  const r = s % 60;
  return (m < 10 ? '0' : '') + m + ':' + (r < 10 ? '0' : '') + r;
}

/**
 * @param {Object} o
 * @param {HTMLElement} o.root      ctx.root (the shell's class root)
 * @param {Function} o.t            ctx.lexicon
 * @param {Object} o.keys           ctx.keys (for the peek key label)
 * @param {boolean} o.coarse        coarse pointer
 * @param {boolean} o.lite          smaller tiles / lower budgets
 * @param {boolean} o.zen           untimed class - no clock
 */
export function createHud(o) {
  const opts = o || {};
  const t = typeof opts.t === 'function' ? opts.t : (k, f) => f || k;
  const coarse = !!opts.coarse;

  const wrap = el('div', 'g-lf');

  /* --------------------------------- HUD ---------------------------------- */
  /* Floating evidence polaroid (styles.js pins and tilts it); the tally slots
     stamp pink one by one as finds are claimed. */
  const hud = el('div', 'g-lf-hud');
  const tchip = el('span', 'g-lf-tchip');
  const tart = el('span', 'g-lf-tt');
  const tlabel = el('small', null, t('lf_find_prompt', 'Find her'));
  const tcount = el('b', null, '0 / 0');
  const slots = el('span', 'g-lf-slots');
  if (tchip) {
    tchip.appendChild(tart);
    tchip.appendChild(tlabel);
    tchip.appendChild(tcount);
    if (slots) tchip.appendChild(slots);
  }
  const clock = el('span', 'chip num', opts.zen ? t('lf_zen_clock', '--:--') : '00:00');
  const streakLabel = el('span', 'chip', t('streak', 'Streak'));
  const streakMount = el('span', 'g-lf-streak');
  if (hud) {
    hud.appendChild(tchip);
    hud.appendChild(clock);
    hud.appendChild(streakLabel);
    hud.appendChild(streakMount);
    hud.appendChild(el('span', 'g-lf-spacer'));
  }

  /* --------------------------------- VIEW --------------------------------- */
  const view = el('div', 'g-lf-view' + (opts.lite ? ' g-lf-lite' : ''));
  const dim = el('div', 'g-lf-dim');
  // The shared stamp is inline-block (styles.css .arc-stamp), so it gets an
  // absolutely-positioned anchor of its own - handed an in-flow host it would
  // shove the mosaic sideways on every find.
  const stampAnchor = el('div', 'g-lf-stamp');
  if (view) { view.appendChild(dim); view.appendChild(stampAnchor); }

  /* --------------------------------- FOOT --------------------------------- */
  const foot = el('div', 'g-lf-foot');
  const keyLabel = (opts.keys && typeof opts.keys.labelFor === 'function')
    ? opts.keys.labelFor('peek') : 'Space';
  // Touch gets the thumb-reach control inside the board (mockup .thumbpeek);
  // a mouse gets the foot button with its key hint.
  const peekBtn = el('button', coarse ? 'g-lf-thumbpeek' : 'arc-peekbtn');
  if (peekBtn) {
    peekBtn.type = 'button';
    peekBtn.textContent = coarse
      ? t('peek', 'Peek')
      : t('peek', 'Peek') + '  ' + keyLabel;
  }
  const missChip = el('span', 'chip');
  const peekChip = el('span', 'chip');
  const hint = el('span', 'g-lf-hint', t('peek_hint', 'Hold to peek. Using it caps this class at A.'));
  if (foot) {
    if (!coarse && peekBtn) foot.appendChild(peekBtn);
    foot.appendChild(missChip);
    foot.appendChild(peekChip);
    foot.appendChild(el('span', 'g-lf-spacer'));
    foot.appendChild(hint);
  }
  if (coarse && view && peekBtn) view.appendChild(peekBtn);

  if (wrap) {
    wrap.appendChild(hud);
    wrap.appendChild(view);
    wrap.appendChild(foot);
  }
  if (opts.root && wrap && opts.root.appendChild) opts.root.appendChild(wrap);

  /** Cards live in the view so they drift-dim with the board, not over the HUD. */
  function card(cls, look, title, note) {
    const node = el('div', 'g-lf-card' + (cls ? ' ' + cls : ''));
    if (!node) return null;
    const art = el('div', 'g-lf-art');
    if (art) { paintLook(art, look || {}); node.appendChild(art); }
    if (title) node.appendChild(el('h4', null, title));
    if (note) node.appendChild(el('p', null, note));
    if (view) view.appendChild(node);
    return node;
  }

  let briefingCard = null;
  let peekCard = null;
  let spotCard = null;
  let tauntNode = null;

  const api = {
    root: wrap,
    view,
    peekButton: peekBtn,
    streakMount,
    stampAnchor,

    /** "4 / 5" - numerals need no lexicon row. The tally slots mirror it. */
    setProgress(found, total) {
      if (tcount) tcount.textContent = found + ' / ' + total;
      if (slots) {
        const want = Math.max(0, total | 0);
        while (slots.children && slots.children.length > want) {
          try { slots.children[slots.children.length - 1].remove(); } catch (e) { break; }
        }
        while (slots.children && slots.children.length < want) {
          const s = el('span', 'g-lf-slot');
          if (!s) break;
          slots.appendChild(s);
        }
        const kids = slots.children || [];
        for (let i = 0; i < kids.length; i++) {
          const k = kids[i];
          if (!k || !k.classList) continue;
          if (i < found) k.classList.add('on'); else k.classList.remove('on');
        }
      }
    },
    setPrompt(text) { if (tlabel) tlabel.textContent = text; },
    setClock(secLeft) {
      if (!clock) return;
      clock.textContent = secLeft == null ? t('lf_zen_clock', '--:--') : fmtClock(secLeft);
    },
    setChips(misclicks, peeks) {
      if (missChip) missChip.textContent = t('lf_misses', 'Misses') + ' ' + misclicks;
      if (peekChip) peekChip.textContent = t('peek', 'Peek') + ' ' + peeks;
    },
    /** Keep the HUD target chip showing the current target look. */
    setTargetArt(look) { if (tart) paintLook(tart, look || {}); },

    /**
     * Remote media can land AFTER a card is on screen. Repaint every live card so
     * the briefing never shows a different target than the board does - getting
     * this wrong means the player memorises art the board does not have.
     */
    refreshCards(look) {
      api.setTargetArt(look);
      for (const node of [briefingCard, peekCard, spotCard]) {
        if (!node || !node.children) continue;
        for (const kid of node.children) {
          if (kid && kid.classList && kid.classList.contains && kid.classList.contains('g-lf-art')) {
            paintLook(kid, look || {});
          }
        }
      }
    },

    /* ------------------------------ overlays ----------------------------- */
    dim(on) {
      if (!dim || !dim.classList) return;
      if (on) dim.classList.add('on'); else dim.classList.remove('on');
    },

    showBriefing(look, note) {
      api.hideBriefing();
      briefingCard = card(null, look, t('lf_find_prompt', 'Find her'), note);
      return briefingCard;
    },
    /** The card glitch-collapses INTO the board - and ends up nowhere. */
    collapseBriefing() {
      if (briefingCard && briefingCard.classList) briefingCard.classList.add('g-lf-collapse');
    },
    hideBriefing() {
      if (briefingCard) { try { briefingCard.remove(); } catch (e) { /* ignore */ } }
      briefingCard = null;
    },

    /** Peek: translucent target card while the board keeps drifting behind it. */
    showPeek(look) {
      if (peekCard) return peekCard;
      peekCard = card('g-lf-peekcard', look, t('peek', 'Peek'), null);
      return peekCard;
    },
    hidePeek() {
      if (peekCard) { try { peekCard.remove(); } catch (e) { /* ignore */ } }
      peekCard = null;
    },

    /** The found ceremony's spotlight card (board dims behind it). */
    showSpot(look, title) {
      api.hideSpot();
      spotCard = card('g-lf-spot', look, title, null);
      return spotCard;
    },
    hideSpot() {
      if (spotCard) { try { spotCard.remove(); } catch (e) { /* ignore */ } }
      spotCard = null;
    },

    /** The skinned taunt / announce line (misclick streak, modifier, bell). */
    taunt(text) {
      api.clearTaunt();
      tauntNode = el('div', 'g-lf-taunt', String(text == null ? '' : text));
      if (view && tauntNode) view.appendChild(tauntNode);
      return tauntNode;
    },
    clearTaunt() {
      if (tauntNode) { try { tauntNode.remove(); } catch (e) { /* ignore */ } }
      tauntNode = null;
    },

    destroy() {
      api.hideBriefing(); api.hidePeek(); api.hideSpot(); api.clearTaunt();
      try { if (wrap && wrap.remove) wrap.remove(); } catch (e) { /* ignore */ }
    },
  };

  return api;
}

export default createHud;
