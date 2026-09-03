/* ============================================================================
 * games/lost-and-found/hud.js - chrome, cards and the two overlays.
 *
 * Everything the player reads. Three rules it obeys:
 *
 *  1. EVERY string goes through ctx.lexicon (t(key, fallback)) - the mod skin
 *     changes display strings only, and this game never reads modId. Numbers
 *     (4 / 26, 00:47) are numbers, so they need no key - but a sentence with a
 *     number IN it still needs one, with a {n} slot the caller fills.
 *  2. The peek control is BUILT here but OWNED by the shell: we hand the node to
 *     ctx.peek.attach() and never track the A-cap ourselves (peek.js trap 9).
 *     lf_peek_input resolves auto -> tap-toggle on a coarse pointer, else hold.
 *  3. The briefing / peek / spotlight cards render the target through the same
 *     paintLook() the board uses, so they are the same look on the same DOM
 *     layer - never a canvas copy of tainted media.
 * ==========================================================================*/

import { paintLook } from './board.js';
import { el } from './util.js';

/** Most tally slots the HUD chip will ever draw. Past this the strip stops
 *  being one-dot-per-find and becomes a proportional meter of this many
 *  segments - see setProgress(). The exact count is always in the numerals. */
const SLOT_CAP = 10;
/** ...and the same ceiling for the drawn rules-sheet tally, which is a PICTURE
 *  of "several finds, and she moves between them", not a countable promise. */
const HOWTO_SLOTS = 6;

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
 * @param {Function=} o.cue         the GAME's clamped cue helper (name, level,
 *        extra). A closure, never the engine - the chrome vocabulary has to
 *        obey the tier ceiling exactly like every gameplay beat does.
 */
export function createHud(o) {
  const opts = o || {};
  const t = typeof opts.t === 'function' ? opts.t : (k, f) => f || k;
  const coarse = !!opts.coarse;
  const cue = typeof opts.cue === 'function' ? opts.cue : () => {};

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
  let howtoCard = null;

  /**
   * The class-rules sheet (Law IV: the rules are DRAWN, the words only caption).
   * Three vignettes: her polaroid against a live mini-wall (one tile wears her
   * exact look via the same paintLook as the board), the find tally, and
   * the peek keycap. All motion is CSS animation, so the shell's reduced-motion
   * rule freezes it and the sheet stays readable as a still.
   */
  function buildHowto(look, onBegin, finds) {
    const node = el('div', 'g-lf-card g-lf-howto');
    if (!node) return null;
    node.appendChild(el('h4', null, t('lf_howto_title', 'Class rules')));

    const row = (figKids, text) => {
      const r = el('div', 'g-lf-hw-row');
      if (!r) return null;
      const fig = el('span', 'g-lf-hw-fig');
      if (fig) { for (const k of figKids) if (k) fig.appendChild(k); r.appendChild(fig); }
      const p = el('p', null, text);
      if (p) r.appendChild(p);
      return r;
    };

    /* 1 — her polaroid vs the drifting mini-wall; one tile IS her look */
    const pol = el('span', 'g-lf-hw-pol');
    const polArt = el('span', 'g-lf-hw-polart');
    if (polArt) { paintLook(polArt, look || {}); if (pol) pol.appendChild(polArt); }
    if (pol) pol.appendChild(el('small', null, t('lf_find_prompt', 'Find her')));
    const wall = el('span', 'g-lf-hw-wall');
    const grads = ['g-lf-g1', 'g-lf-g4', 'g-lf-g6', 'g-lf-g2', 'g-lf-g5', 'g-lf-g8', 'g-lf-g3', 'g-lf-g7', 'g-lf-g1', 'g-lf-g5', 'g-lf-g2', 'g-lf-g6', 'g-lf-g4', 'g-lf-g8'];
    let gi = 0;
    for (let r = 0; r < 3 && wall; r++) {
      const mrow = el('span', 'g-lf-hw-mrow' + (r === 1 ? ' g-lf-hw-rev' : ''));
      if (!mrow) break;
      for (let c = 0; c < 5; c++) {
        if (r === 1 && c === 2) {
          const mark = el('span', 'g-lf-hw-tile g-lf-hw-mark');
          if (mark) { paintLook(mark, look || {}); mrow.appendChild(mark); }
        } else {
          mrow.appendChild(el('span', 'g-lf-hw-tile ' + grads[gi++ % grads.length]));
        }
      }
      wall.appendChild(mrow);
    }
    node.appendChild(row([pol, el('span', 'g-lf-hw-eq', '→'), wall],
      t('lf_howto_find', 'She hides on a wall that never sits still. Spot the tile that matches her picture.')));

    /* 2 — the tally: she relocates after every find, and the class is however
       many finds this TIER deals (13-26 since the class-length wave). The drawn
       strip stays HOWTO_SLOTS wide whatever the number - it is a picture of the
       verb, and the sentence beside it carries the actual count. */
    const want = Math.max(3, Math.min(HOWTO_SLOTS, Math.round(Number(finds) || HOWTO_SLOTS)));
    const slots = el('span', 'g-lf-hw-slots');
    for (let i = 0; i < want && slots; i++) {
      slots.appendChild(el('span', 'g-lf-hw-slot' + (i < 2 ? ' on' : i === 2 ? ' next' : '')));
    }
    node.appendChild(row([slots],
      t('lf_howto_finds_n', 'Every find, she relocates. Catch her {n} times.')
        .replace('{n}', String(Math.max(1, Math.round(Number(finds) || 0)) || '?'))));

    /* 3 — the peek verb (same key label the foot button wears) */
    node.appendChild(row([el('span', 'g-lf-hw-key', coarse ? t('peek', 'Peek') : keyLabel)],
      t('peek_hint', 'Hold to peek. Using it caps this class at A.')));

    const go = el('button', 'g-lf-hw-go', t('lf_howto_go', 'Start the hunt'));
    if (go) {
      go.type = 'button';
      go.addEventListener('click', () => {
        // THE START PRESS. This one button both dismisses the sheet and starts
        // play, so it is ONE cue and it is the start cue (`lift`), not a page
        // turn - the chrome vocabulary's "once, on the press that actually
        // starts play".
        cue('lift', 0.5);
        if (typeof onBegin === 'function') onBegin();
      });
      node.appendChild(go);
    }
    // refreshCards() repaints these when remote media lands late (expando
    // references, so no querySelector on the test DOM double).
    node._lookEls = [polArt];
    for (const mr of (wall && wall.children) || []) {
      for (const cell of mr.children || []) {
        if (cell && cell.classList && cell.classList.contains && cell.classList.contains('g-lf-hw-mark')) node._lookEls.push(cell);
      }
    }
    return node;
  }

  const api = {
    root: wrap,
    view,
    peekButton: peekBtn,
    streakMount,
    stampAnchor,

    /**
     * "4 / 26" - numerals need no lexicon row, and THEY are the exact truth.
     * The tally slots mirror it, but only while one slot per find still fits on
     * a polaroid: a class is 13-26 finds now (the class-length wave), and 26
     * slots at 11px + 4px of gap is a 476px strip nailed across the HUD chip.
     * Above SLOT_CAP the strip becomes a SEGMENT METER of SLOT_CAP segments
     * lit in proportion, which is why the numerals sit above it - the meter
     * rounds, the numerals never do.
     */
    setProgress(found, total) {
      if (tcount) tcount.textContent = found + ' / ' + total;
      if (slots) {
        const n = Math.max(0, total | 0);
        const meter = n > SLOT_CAP;
        const want = meter ? SLOT_CAP : n;
        // one dot = one find, or one segment = one SLOT_CAP-th of the class
        const lit = meter
          ? (found > 0 ? Math.max(1, Math.round((found / Math.max(1, n)) * SLOT_CAP)) : 0)
          : found;
        if (slots.classList) {
          if (meter) slots.classList.add('g-lf-slots-meter');
          else slots.classList.remove('g-lf-slots-meter');
        }
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
          if (i < lit) k.classList.add('on'); else k.classList.remove('on');
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

    /** Chrome the trickster may flicker (glitch-to-asset). Read-only anchors:
     *  the overlay is positioned OVER these on the wrap, never inside them. */
    chromeEls() { return [tchip, clock, missChip].filter(Boolean); },

    /**
     * Remote media can land AFTER a card is on screen. Repaint every live card so
     * the briefing never shows a different target than the board does - getting
     * this wrong means the player memorises art the board does not have.
     */
    refreshCards(look) {
      api.setTargetArt(look);
      if (howtoCard && Array.isArray(howtoCard._lookEls)) {
        for (const le of howtoCard._lookEls) { if (le) paintLook(le, look || {}); }
      }
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

    /** The class-rules sheet. Dismissed ONLY by its own button (a stray click
     *  on a tutorial must never count as read). Caller owns dim().
     *  `finds` = the finds this tier's class asks for; the sheet says the
     *  number out loud, so it must never be guessed here. */
    showHowto(look, onBegin, finds) {
      api.hideHowto();
      howtoCard = buildHowto(look, onBegin, finds);
      if (view && howtoCard) view.appendChild(howtoCard);
      // THE SHEET ARRIVING. Its three rows paint in ONE frame with no stagger,
      // so the House Book's answer is one `slide`, not a blip ladder.
      if (howtoCard) cue('slide', 0.35);
      return howtoCard;
    },
    hideHowto() {
      if (howtoCard) { try { howtoCard.remove(); } catch (e) { /* ignore */ } }
      howtoCard = null;
    },

    showBriefing(look, note) {
      api.hideBriefing();
      briefingCard = card(null, look, t('lf_find_prompt', 'Find her'), note);
      // The memorize card is the second sheet of the same tutorial breath - the
      // same page-turn cue, a shade under the rules sheet. Its DISMISSAL needs
      // nothing: collapseBriefing() glitch-collapses into a swapBurst, and that
      // burst's `glitch_swap` already carries the engine's own glitch cue.
      if (briefingCard) cue('slide', 0.3);
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
      api.hideHowto(); api.hideBriefing(); api.hidePeek(); api.hideSpot(); api.clearTaunt();
      try { if (wrap && wrap.remove) wrap.remove(); } catch (e) { /* ignore */ }
    },
  };

  return api;
}

export default createHud;
