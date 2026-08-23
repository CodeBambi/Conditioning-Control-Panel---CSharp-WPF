/* ============================================================================
 * shell/records.js - THE RECORDS OFFICE (PUNCHCARD.md §6).
 *
 * The campus already had a Records door; it opened the day's report card and
 * nothing else. It is a full screen now, and it is the ONE diegetic home for
 * everything the school has written down about you:
 *
 *   THE WALL     ten punch cards pinned up, holes visibly punched, greyed while
 *                a class is unattended, wearing an UNLOCKED seal once the tenth
 *                hole lands. A card you have never played still hangs there,
 *                which is how the mechanic advertises itself.
 *   THE DOCKET   pick a card and the desk answers: enrolled on, every stamp
 *                date in order, unlocked on, holes left.
 *   THE REPORT   the existing report card is still one press away, unchanged -
 *                `shell/reportcard.js` stays the ONE share pipeline (trap 13)
 *                and this screen never renders a share of its own.
 *
 * READ-ONLY BY CONSTRUCTION. `punchCards` is host-owned (core/store.js refuses a
 * page write), so there is nothing on this screen that can move a number. It
 * paints what the store holds and offers two doors out.
 *
 * The exits are the shell's (shell/exits.js): a wall of ten cards plus a docket
 * is taller than a short window, so Back rides the sticky bar at the bottom of
 * the scroller rather than sitting above the fold (trap 43).
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { exitBar, sign as signExit } from './exits.js';
import { cardFace, HOLES, holesLine } from './punchcard.js';

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

function attr(node, name, value) {
  try { if (node && typeof node.setAttribute === 'function') node.setAttribute(name, value); }
  catch (e) { /* noop */ }
}

/**
 * @param {Object} o
 * @param {Function} o.gameName        (gameKey) -> display name
 * @param {Function} o.punchCard       (gameKey) -> store.punchCard(gameKey)
 * @param {Function=} o.log
 * @returns {Object} {root, render(state), destroy()}
 */
export function createRecords({ gameName, punchCard, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const name = typeof gameName === 'function' ? gameName : (k) => String(k || '');
  const readCard = typeof punchCard === 'function' ? punchCard : () => ({});

  const root = el('div', 'arc-records');
  let selected = null;

  /**
   * @param {Object} state
   * @param {string[]} state.gameKeys   every registered class, registry order
   * @param {Function=} state.onBack
   * @param {Function=} state.onReport
   * @param {string=} state.reportLabel
   */
  function render(state) {
    const s = state || {};
    const keys = Array.isArray(s.gameKeys) ? s.gameKeys.slice() : [];
    root.textContent = '';

    /* ---------------------------- the desk ------------------------------ */
    const desk = el('div', 'arc-records-desk');
    root.appendChild(desk);

    desk.appendChild(el('p', 'arc-kicker', t('records_kicker', 'Records Office')));
    desk.appendChild(el('h1', 'arc-h1', t('campus_records', 'Records')));
    desk.appendChild(el('p', 'arc-lede', t('records_lede',
      'Ten cards, ten stamps each. The wall keeps them whether you come back or not.')));

    /* The tally. Honest arithmetic off the same cards the wall draws - nothing
     * here is a second source of truth. */
    let enrolled = 0;
    let unlocked = 0;
    let stamps = 0;
    const cards = keys.map((k) => {
      const c = readCard(k) || {};
      if (c.enrolled) enrolled += 1;
      if (c.complete) unlocked += 1;
      stamps += Math.max(0, Math.round(Number(c.punches) || 0));
      return { key: k, card: c };
    });

    const tally = el('div', 'arc-classbar arc-records-tally');
    tally.appendChild(el('span', 'chip', t('records_enrolled', 'Enrolled') + ' ' + enrolled + '/' + keys.length));
    tally.appendChild(el('span', 'chip' + (unlocked ? ' flame' : ''),
      t('punchcard_unlocked_chip', 'Unlocked') + ' ' + unlocked + '/' + keys.length));
    tally.appendChild(el('span', 'chip num', t('records_holes_punched', 'Stamps earned') + ' ' + stamps));
    desk.appendChild(tally);

    /* ---------------------------- the wall ------------------------------ */
    if (!keys.length) {
      // Nothing registered at all: a school with no classes. Say so rather than
      // hanging an empty frame and letting it read as a broken screen.
      desk.appendChild(el('p', 'arc-note', t('records_empty_wall',
        'Nothing on the wall yet. Attend a class and the first card gets pinned.')));
    }

    const wall = el('div', 'arc-records-wall');
    attr(wall, 'role', 'list');
    desk.appendChild(wall);

    const docket = el('div', 'arc-records-docket');
    desk.appendChild(docket);

    for (const entry of cards) {
      const slot = el('button', 'arc-records-slot');
      slot.type = 'button';
      attr(slot, 'role', 'listitem');
      const face = cardFace({
        gameKey: entry.key, card: entry.card, name: name(entry.key), small: true,
      });
      slot.appendChild(face.root);
      // The pre-enrollment face is the advert: greyed, pinned, and it says what
      // it costs to start punching it (§6's empty state).
      slot.appendChild(el('span', 'arc-records-slotline', entry.card.enrolled
        ? holesLine(entry.card.punches)
        : t('records_not_enrolled', 'Not enrolled - attend the class')));
      slot.addEventListener('click', () => { selected = entry.key; paintDocket(); });
      wall.appendChild(slot);
    }

    /* -------------------------- the docket ------------------------------ */
    function paintDocket() {
      docket.textContent = '';
      if (!keys.length) return;
      if (!selected || keys.indexOf(selected) < 0) {
        docket.appendChild(el('p', 'arc-note arc-records-hint',
          t('records_flip_hint', 'Pick a card to read its stamps.')));
        return;
      }
      const c = readCard(selected) || {};
      const box = el('div', 'arc-records-docket-card');
      docket.appendChild(box);

      box.appendChild(el('p', 'arc-kicker', name(selected)));

      if (!c.enrolled) {
        box.appendChild(el('p', 'arc-records-empty',
          t('records_not_enrolled', 'Not enrolled - attend the class')));
        box.appendChild(el('p', 'arc-note', t('records_enroll_hint',
          'The first graded finish opens the card and earns two stamps.')));
        return;
      }

      const rows = el('div', 'arc-records-rows');
      const row = (label, value) => {
        const r = el('div', 'arc-records-row');
        r.appendChild(el('span', 'arc-records-rowlabel', label));
        r.appendChild(el('span', 'arc-records-rowvalue', value));
        rows.appendChild(r);
      };
      row(t('records_enrolled_on', 'Enrolled'), String(c.enrolledAt || ''));
      row(t('punchcard', 'Stamp Card'), holesLine(c.punches));
      if (c.complete) row(t('records_unlocked_on', 'Unlocked'), String(c.unlockedAt || ''));
      else {
        row(t('records_holes_left', 'Stamps left'), String(Math.max(0, HOLES - (c.punches | 0))));
      }
      box.appendChild(rows);

      /* THE STAMP RECAP. `dates` is daily stamps only - day one's pair lives in
       * `enrolledAt` + `house` and is shown as the enrollment row above, which
       * is exactly the split the host stores (§2.1). Saying so is cheaper than
       * a player counting two rows that do not add up. */
      box.appendChild(el('p', 'arc-records-sub', t('records_stamps', 'Daily stamps')));
      const dates = Array.isArray(c.dates) ? c.dates : [];
      if (!dates.length) {
        box.appendChild(el('p', 'arc-note', t('records_no_stamps', 'No daily stamps yet.')));
      } else {
        const list = el('div', 'arc-records-stamps');
        for (const d of dates) list.appendChild(el('span', 'arc-records-stamp', d));
        box.appendChild(list);
      }
      box.appendChild(el('p', 'arc-note', t('records_house_note',
        'Day one is two stamps: one for finishing, one on the house.')));
    }
    paintDocket();

    /* ---------------------------- the exits ----------------------------- */
    const toReport = el('button', 'btn ghost', s.reportLabel || t('report_card', 'Report Card'));
    toReport.type = 'button';
    toReport.addEventListener('click', () => { try { if (s.onReport) s.onReport(); } catch (e) { say('records report: ' + ((e && e.message) || e)); } });
    signExit(toReport, { dir: 'go', quiet: true });

    const back = el('button', 'btn primary', t('back', 'Back'));
    back.type = 'button';
    back.addEventListener('click', () => { try { if (s.onBack) s.onBack(); } catch (e) { say('records back: ' + ((e && e.message) || e)); } });
    signExit(back, { dir: 'back' });

    desk.appendChild(exitBar([back, toReport]));
    return root;
  }

  return {
    root,
    render,
    /** Which card the docket is showing (test seam). */
    get selected() { return selected; },
    destroy() { try { root.remove(); } catch (e) { /* noop */ } },
  };
}

export default createRecords;
