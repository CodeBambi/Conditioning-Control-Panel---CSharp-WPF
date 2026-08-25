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
 *   THE SPOTLIGHT (owner playtest 2026-08-24) picking a card ALSO lifts it to
 *                the centre of the screen, close up, under a key light: the
 *                office dims, the card sweeps in, the badge catches the lamp,
 *                the class name plays one beat in its own game's idiom, the
 *                stamped stars do a stadium wave and the text strip drifts
 *                like a rostrum camera. Presentation ONLY - the face is still
 *                shell/punchcard.js's cardFace (one card = one object) and no
 *                number moves. Dismiss: backdrop click, the close button, or
 *                Esc (shell.js's escapeStep asks dismissSpotlight() first,
 *                trap 48's shape). All the motion is CSS (styles.css, THE
 *                SPOTLIGHT block); this file only mounts nodes and staggers
 *                sfx. Reduced motion = one plain fade, no beats, no cues.
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
import { exitBar, sign as signExit, campusPillRow } from './exits.js';
import { cardFace, HOLES, holesLine } from './punchcard.js';

/* ----------------------------------------------------------------------------
 * THE OFFICE OPENS, AND THE CARDS TURN.
 * shell/audio.js holds the only audio node on the page (trap 18), so this is a
 * REQUEST on `document` and never a sound - the exact defensive shape
 * shell/ceremonies.js sfx() set. A dropped cue is not an error.
 * -------------------------------------------------------------------------- */
function sfx(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

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

function css(node, name, value) {
  try {
    if (node && node.style && typeof node.style.setProperty === 'function') {
      node.style.setProperty(name, value);
    }
  } catch (e) { /* noop */ }
}

/** Reduced motion, either signal: the shell's own class or the OS preference. */
function reducedMotion() {
  try {
    const root = (typeof document !== 'undefined') && document.documentElement;
    if (root && root.classList && typeof root.classList.contains === 'function'
      && root.classList.contains('arc-reduced')) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(prefers-reduced-motion: reduce)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* noop */ }
  return false;
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
  /** The live spotlight, or null. {el, timers:[], from} */
  let spot = null;

  /* --------------------------- THE SPOTLIGHT ---------------------------- */

  /**
   * Close the presentation beat. Returns true when one was up (the Esc rung's
   * answer). `immediate` skips the exit fade - a re-render or a destroy has
   * already taken the ground out from under it.
   */
  function dismissSpotlight(immediate) {
    if (!spot) return false;
    const s = spot;
    spot = null;
    for (const id of s.timers) { try { clearTimeout(id); } catch (e) { /* noop */ } }
    const drop = () => { try { if (s.el && s.el.remove) s.el.remove(); } catch (e) { /* noop */ } };
    if (immediate || !(s.el && s.el.classList)) {
      drop();
    } else {
      sfx('paper', 0.18);
      try { s.el.classList.add('is-closing'); } catch (e) { /* noop */ }
      let scheduled = false;
      try { setTimeout(drop, 220); scheduled = true; } catch (e) { /* noop */ }
      if (!scheduled) drop();
    }
    // The focus goes back where it came from (the wall slot that opened it).
    if (!immediate && s.from) { try { s.from.focus(); } catch (e) { /* noop */ } }
    return true;
  }

  /**
   * Lift one card off the wall to centre screen - evidence held to the desk
   * lamp. Reuses cardFace so the card is still ONE object (punchcard.js's law);
   * everything on top is presentation chrome the CSS choreographs:
   *   0ms    veil + key light, the card sweeps in            ('whoosh')
   *   ~500ms the badge catches the lamp (glint, then breathe) ('chime')
   *   ~700ms the name plays its class's own idiom, letter by letter ('slide')
   *   ~1s    the stars do their stadium wave                 ('pop' ladder)
   *   ~1.2s  the text strip starts its ken burns drift
   * Reduced motion mounts the same dialog with one fade and no cues.
   */
  function openSpotlight(key, from) {
    dismissSpotlight(true);
    const c = readCard(key) || {};
    const label = name(key);
    const rm = reducedMotion();
    const enrolled = c.enrolled === true
      || (typeof c.enrolledAt === 'string' && !!c.enrolledAt);

    const box = el('div', 'arc-rs' + (rm ? ' arc-rs-reduced' : ''));
    attr(box, 'role', 'dialog');
    attr(box, 'aria-modal', 'true');
    attr(box, 'aria-label', label);

    const veil = el('div', 'arc-rs-veil');
    attr(veil, 'aria-hidden', 'true');
    veil.addEventListener('click', () => dismissSpotlight());
    box.appendChild(veil);

    const stage = el('div', 'arc-rs-stage');
    box.appendChild(stage);

    const lamp = el('div', 'arc-rs-lamp');
    attr(lamp, 'aria-hidden', 'true');
    stage.appendChild(lamp);

    /* The card itself: the SAME face the wall pins, at full size. */
    const cardBox = el('div', 'arc-rs-card');
    const face = cardFace({ gameKey: key, card: c, name: label });
    cardBox.appendChild(face.root);
    /* The badge fx ride an overlay INSIDE the card box (the face clips its own
     * corners; this clips the same way) - a halo + glint over the baked
     * Arcademy badge, never a second badge (the art owns the pixels). */
    const fx = el('div', 'arc-rs-fx');
    attr(fx, 'aria-hidden', 'true');
    const badge = el('i', 'arc-rs-badge');
    badge.appendChild(el('i', 'arc-rs-glint'));
    fx.appendChild(badge);
    face.root.appendChild(fx);
    stage.appendChild(cardBox);

    /* The placard: the name in live type (the baked logo cannot animate), one
     * letter per span so each class's keyframes can play its own idiom. */
    const placard = el('div', 'arc-rs-placard');
    const nameEl = el('h2', 'arc-rs-name');
    attr(nameEl, 'data-anim', key);
    attr(nameEl, 'aria-label', label);
    const chars = String(label).split('');
    /* Anomaly's beat needs its odd one out: a deterministic pick past the
     * midpoint, never a space (the same card must always misbehave the same). */
    let oddAt = -1;
    if (key === 'anomaly') {
      const inked = [];
      for (let i = 0; i < chars.length; i++) { if (chars[i].trim()) inked.push(i); }
      if (inked.length) oddAt = inked[Math.floor(inked.length * 0.6)];
    }
    for (let i = 0; i < chars.length; i++) {
      const sp = el('span', 'arc-rs-l' + (i === oddAt ? ' is-odd' : ''), chars[i]);
      attr(sp, 'aria-hidden', 'true');
      css(sp, '--li', String(i));
      nameEl.appendChild(sp);
    }
    placard.appendChild(nameEl);
    placard.appendChild(el('p', 'arc-rs-sub', enrolled
      ? holesLine(c.punches)
      : t('records_not_enrolled', 'Not enrolled - attend the class')));
    stage.appendChild(placard);

    const close = el('button', 'arc-rs-close', '✕');
    close.type = 'button';
    attr(close, 'aria-label', t('records_spot_close', 'Close'));
    close.addEventListener('click', () => dismissSpotlight());
    box.appendChild(close);

    /* One focusable thing lives in here, so the trap is one line. Esc is NOT
     * handled here: boot.js owns the key and shell.js's escapeStep asks
     * dismissSpotlight() first (trap 48's shape - never a second key ladder). */
    box.addEventListener('keydown', (ev) => {
      if (!ev || ev.key !== 'Tab') return;
      try { ev.preventDefault(); } catch (e) { /* noop */ }
      try { close.focus(); } catch (e) { /* noop */ }
    });

    root.appendChild(box);
    spot = { el: box, timers: [], from: from || null };
    try { close.focus(); } catch (e) { /* noop */ }

    /* The cue sheet. Levels stay whisper-low; a dropped cue is not an error,
     * and reduced motion plays NONE of them (there is no beat to score). */
    if (!rm) {
      const cue = (ms, fn) => {
        try { spot.timers.push(setTimeout(() => { if (spot && spot.el === box) fn(); }, ms)); }
        catch (e) { /* noop */ }
      };
      sfx('whoosh', 0.22);
      cue(500, () => sfx('chime', 0.16));
      cue(700, () => sfx('slide', 0.18));
      const punched = Math.max(0, Math.min(HOLES, Math.round(Number(c.punches) || 0)));
      for (let i = 0; i < punched; i++) {
        const n = i;
        cue(1000 + n * 90, () => sfx('pop', 0.12, { pitch: 0.85 + n * 0.06 }));
      }
    }
    return box;
  }

  /**
   * @param {Object} state
   * @param {string[]} state.gameKeys   every registered class, registry order
   * @param {Function=} state.onBack
   * @param {Function=} state.onReport
   * @param {string=} state.reportLabel
   * @param {boolean=} state.embedded   this wall is INSIDE something (the room's
   *                   card panel), so the way out is that thing and not the
   *                   campus - see EMBEDDED below. Absent = the screen it was.
   */
  function render(state) {
    const s = state || {};
    const keys = Array.isArray(s.gameKeys) ? s.gameKeys.slice() : [];
    /* EMBEDDED. shell/recordsroom.js opens this wall in a panel inside the
     * painted office, and a panel's exits are the panel's: the crest pill
     * ("back to campus") would walk a player out of the building they are
     * standing in, and Back has to put the cards away rather than leave. Two
     * differences, both here, and OFF by default - a caller that never passes
     * the flag renders the screen byte for byte. */
    const embedded = s.embedded === true;
    // A render empties the root, so any spotlight node is already gone with it.
    dismissSpotlight(true);
    root.textContent = '';
    // The docket coming out. `shell.js showRecords()` renders once per visit, so
    // a render IS an open - there is no repaint path into this screen.
    sfx('paper', 0.25);

    /* ---------------------------- the desk ------------------------------ */
    const desk = el('div', 'arc-records-desk');
    root.appendChild(desk);

    /* THE WAY HOME, FIRST AND STICKY. Back still rides the bar at the bottom of
     * the scroller; the crest rides the top of it, so the office answers "how
     * do I get out of here" without the player having to scroll to find out.
     * It is the SAME verb the bar's Back runs - one door, two handles. */
    if (!embedded) {
      desk.appendChild(campusPillRow({
        onActivate: () => {
          try { if (s.onBack) s.onBack(); }
          catch (e) { say('records pill: ' + ((e && e.message) || e)); }
        },
      }));
    }

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

    /* THE WALL PANEL (ANNEX-OS.md §1). Gated by the shell: the prop only
     * exists once the reveal has fired, and the shell decides that - this
     * screen just draws what it was handed (the punchCard-cap law above).
     * No badge, no toast, no pulse: the cold seam in a warm room IS the
     * signpost, same pre-attentive doctrine as EMI's off-channels. */
    if (typeof s.onAnnex === 'function') {
      const panel = el('button', 'arc-records-annexdoor');
      panel.type = 'button';
      attr(panel, 'aria-label', t('annex_door', 'A wall panel, ajar'));
      panel.appendChild(el('span', 'arc-records-annexseam'));
      panel.addEventListener('click', () => { sfx('door', 0.3); s.onAnnex(); });
      desk.appendChild(panel);
    }

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
      slot.addEventListener('click', () => {
        // One leaf turning: the wall hands the desk a different card.
        sfx('flap', 0.2);
        selected = entry.key;
        paintDocket();
        // And the office presents it: close up, under the lamp.
        openSpotlight(entry.key, slot);
      });
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
          'The first graded finish opens the card and earns three stamps.')));
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

      /* THE STAMP RECAP. `dates` is daily stamps only - day one's three live in
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
        'Day one is three stamps: finishing, on the house, signing on.')));
    }
    paintDocket();

    /* ---------------------------- the exits ----------------------------- */
    const toReport = el('button', 'btn ghost', s.reportLabel || t('report_card', 'Report Card'));
    toReport.type = 'button';
    toReport.addEventListener('click', () => { try { if (s.onReport) s.onReport(); } catch (e) { say('records report: ' + ((e && e.message) || e)); } });
    signExit(toReport, { dir: 'go', quiet: true });

    const back = el('button', 'btn primary', embedded
      ? t('records_close_panel', 'Put the cards back')
      : t('back', 'Back'));
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
    /** True while the presentation beat is up (test seam). */
    get spotlightUp() { return !!spot; },
    /** The Esc rung's handle: closes the spotlight, answers whether one was up. */
    dismissSpotlight: () => dismissSpotlight(false),
    destroy() {
      dismissSpotlight(true);
      try { root.remove(); } catch (e) { /* noop */ }
    },
  };
}

export default createRecords;
