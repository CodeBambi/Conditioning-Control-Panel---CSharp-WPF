/* ============================================================================
 * shell/enrollment.js - ENROLLMENT, and the ceremony that pays for it.
 * PUNCHCARD.md §4. Two halves of one first night:
 *
 *   THE INTRO   two or three flavour cards, shown ONCE EVER per class, BEFORE
 *               the game's own howto sheet (which is untouched: the shell hands
 *               the class over the moment the last card is dismissed). They say
 *               why the class exists and what it is for - the part a rules sheet
 *               never covers, because a rules sheet explains the buttons.
 *   THE CARD    at the end of that first graded run, the punch card fills the
 *               screen and takes TWO holes back to back - thud, thud. The first
 *               is for finishing; the second is on the house, and it gets a line
 *               of its own. Every night after that is one hole, one thud, and
 *               the tenth carries the unlock beat.
 *
 * ENROLLMENT-SEEN IS DERIVED, NEVER STORED (§2.2): `punchCards[key].enrolledAt`
 * is the only flag there is, the host mints it, and a card restored from the
 * server mirror therefore suppresses a repeat tutorial for free. There is no
 * second boolean to fall out of step.
 *
 * THE CEREMONY ANIMATES TO THE HOST'S NUMBER, NEVER FROM A LOCAL COUNTER. The
 * `punchcard-result` frame carries the POST-mint card, and `justUnlocked` is the
 * unlock-beat signal - so a card that the host healed, merged or clamped shows
 * what the host actually holds, and a page that guessed can only ever be wrong.
 * The enrollment beat is the one place the schedule leads: it runs its two
 * punches on its own clock and reconciles when the host answers, because the
 * mint it is celebrating is the one this ceremony is about to ask for.
 *
 * COPY: every user-facing string is a lexicon row. `ENROLL_LEX` below is the
 * English, exported as DATA the way `impulse-control/lex.js` exports IC_LEX -
 * `ArcademyHostService.NeutralLexicon` mirrors it verbatim (copy the values, do
 * not re-word them) or a mod cannot re-voice a word of it. Every value is well
 * under the 96-character mod-skin cap (trap 26): a line that needs more room
 * becomes a second card, never a longer row.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { exitBar, sign as signExit } from './exits.js';
import { cardFace, thud, THUD_PITCH, HOLES } from './punchcard.js';

/* ----------------------------------------------------------------------------
 * THE FLAVOUR COPY
 * Three cards per class, in the campus voice: what the room is FOR, what it
 * makes you do, and what it is quietly doing to you. Second person, flat
 * institutional register, no exclamation, no encouragement.
 * -------------------------------------------------------------------------- */
export const ENROLL_LEX = Object.freeze({
  /* --- Homeroom: the daily word ---------------------------------------- */
  enroll_daily_trigger_1: 'Homeroom takes attendance first, and the register is one word long.',
  enroll_daily_trigger_2: 'Everyone in the school sits the same word tonight. Six chances, no help.',
  enroll_daily_trigger_3: 'Say it enough mornings and you stop deciding what it means.',

  /* --- Lost & Found: the mosaic hunt ----------------------------------- */
  enroll_lost_and_found_1: 'Things go missing here constantly. Nobody files a report.',
  enroll_lost_and_found_2: 'A wall of moving pictures, and one of them is yours. Find it first.',
  enroll_lost_and_found_3: 'This trains the part of you that keeps looking after looking stops working.',

  /* --- Memory Lab: the pair memory ------------------------------------- */
  enroll_deja_vu_1: 'The Memory Lab studies what happens to a board you have already learned.',
  enroll_deja_vu_2: 'Match the pairs. The pairs move when you blink. Both of those are the work.',
  enroll_deja_vu_3: 'You will feel certain and be wrong. We would rather you stopped noticing.',

  /* --- Discipline Hall: the Drop Tube ---------------------------------- */
  enroll_impulse_control_1: 'Discipline Hall exists because you reach for things. Every time.',
  enroll_impulse_control_2: 'Hands on the desk. Pop when told, hold when told. The room may lie.',
  enroll_impulse_control_3: 'A held hand is worth more here than a fast one. Learn which order was real.',

  /* --- The Pool: 2048 by depth ----------------------------------------- */
  enroll_the_deep_end_1: 'The Pool has a shallow end that nobody uses. This class is not held there.',
  enroll_the_deep_end_2: 'Sink tile into tile. Every merge takes you further from the surface.',
  enroll_the_deep_end_3: 'The deeper the board, the harder it is to read. That is the subject.',

  /* --- The Parlour: the shell game ------------------------------------- */
  enroll_misdirection_1: 'The Parlour teaches what a room does to your attention when it wants it.',
  enroll_misdirection_2: 'Keep your eyes on the one that matters. It will not make that easy.',
  enroll_misdirection_3: 'You will be shown the trick and lose anyway. Then shown it again.',

  /* --- The Sorting Room: two piles you chose ----------------------------- */
  enroll_sort_1: 'The Sorting Room does not tell you what matters. You tell it, at the door.',
  enroll_sort_2: 'Yours goes right. Everything else goes left. The ring closes while you decide.',
  enroll_sort_3: 'Sort your own things quickly enough and you stop asking why they are yours.',

  /* --- Music Room: the Simon ring --------------------------------------- */
  enroll_echo_1: 'The Music Room does not teach music. It teaches you to hold a line that somebody else set.',
  enroll_echo_2: 'It plays a phrase. You play it back. Then it adds one and asks again.',
  enroll_echo_3: 'Nobody passes by remembering harder. They pass by stopping the arguing.',

  /* --- Lecture Hall: the vigil ------------------------------------------ */
  enroll_instant_recall_1: 'The Lecture Hall never announces the test. That is the design of the room.',
  enroll_instant_recall_2: 'Watch the hour, answer for it after. You will not hear the question coming.',
  enroll_instant_recall_3: 'Attention that only arrives when asked is not attention. This corrects that.',

  /* --- Darkroom: the odd one out ---------------------------------------- */
  enroll_anomaly_1: 'The Darkroom is where the school checks that you still notice a difference.',
  enroll_anomaly_2: 'Everything on the grid matches. One thing does not. Find it before it moves.',
  enroll_anomaly_3: 'The differences get smaller every year. You are expected to keep up.',

  /* --- The Studio: the sliding picture ---------------------------------- */
  enroll_composure_1: 'The Studio grades one thing: can you finish a picture while interfered with.',
  enroll_composure_2: 'Slide the tiles back into order while the room blurs what order was.',
  enroll_composure_3: 'Nothing in here is fast. Composure is the subject and it cannot be rushed.',
});

/** How many flavour cards a class ships. Three today, for all ten. */
const CARDS = 3;

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

function focusSoon(node) {
  try { if (node && typeof node.focus === 'function') node.focus(); } catch (e) { /* noop */ }
}

/* An exit bar that sticks to the bottom of a CARD's own scroller rather than the
 * document's - trap 43's whole point, and the same `{card:true}` flavour ctx.exits
 * hands the ten classes. exitBar() ignores options it does not know, so the class
 * is added here rather than smuggled through it. */
function cardBar(children) {
  const bar = exitBar(children);
  bar.className = (bar.className || '') + ' arc-exitbar-card';
  return bar;
}

/** The flavour lines a class ships, in order, with the English as fallback. */
export function enrollLines(gameKey) {
  const out = [];
  for (let i = 1; i <= CARDS; i++) {
    const key = 'enroll_' + String(gameKey || '') + '_' + i;
    const en = ENROLL_LEX[key];
    // A class with no copy of its own contributes nothing rather than a raw key:
    // t() would humanize `enroll_foo_2` into "Enroll Foo 2" on screen, which is
    // worse than a two-card intro (trap 15's lesson, applied one level up).
    if (typeof en !== 'string') continue;
    out.push(t(key, en));
  }
  return out;
}

/* ============================================================================
 * THE INTRO
 * ==========================================================================*/
/**
 * Mount the once-ever flavour cards over a class stage. The class has NOT
 * started: `onDone` is what starts it, so the game's own howto sheet is the very
 * next thing the player sees and nothing about it changed.
 *
 * `hideTutorial` (the host's "Skip class tutorials" switch) condenses the whole
 * intro into ONE card carrying every line - the dwell is what it skips, not the
 * enrollment. Enrollment is progression, not a tutorial (§4).
 *
 * @param {Object} o
 * @param {Object} o.mount            the class root
 * @param {string} o.gameKey
 * @param {string} o.name             the class's display name
 * @param {boolean=} o.hideTutorial
 * @param {boolean=} o.reducedMotion
 * @param {Function} o.onDone         start the class
 * @param {Function=} o.log
 * @returns {?Object} {root, close(), skip()} - null when there was nothing to
 *   show (an unknown class with no copy) or nowhere to show it
 */
export function createEnrollmentIntro(o) {
  const s = o || {};
  if (!s.mount || typeof s.mount.appendChild !== 'function') return null;
  const say = typeof s.log === 'function' ? s.log : () => {};
  const lines = enrollLines(s.gameKey);
  if (!lines.length) { say('enrollment: no copy for ' + s.gameKey + ' - straight to the class'); return null; }

  const pages = s.hideTutorial ? [lines] : lines.map((l) => [l]);
  let page = 0;
  let finished = false;

  const root = el('div', 'arc-enroll');
  attr(root, 'role', 'dialog');
  attr(root, 'aria-modal', 'true');
  const card = el('div', 'arc-enroll-card');
  root.appendChild(card);

  card.appendChild(el('p', 'arc-kicker', t('enroll_kicker', 'Enrollment')));
  const head = el('h2', 'arc-h2', String(s.name || s.gameKey || ''));
  card.appendChild(head);

  const body = el('div', 'arc-enroll-body');
  card.appendChild(body);

  /* The dots are the honest progress bar: three cards, and you can see all
   * three before you commit to reading the first. */
  const dots = el('div', 'arc-enroll-dots');
  attr(dots, 'aria-hidden', 'true');
  const dotNodes = [];
  for (let i = 0; i < pages.length; i++) {
    const d = el('i', 'arc-enroll-dot');
    dots.appendChild(d);
    dotNodes.push(d);
  }
  if (pages.length > 1) card.appendChild(dots);

  /* The card that explains the mechanic sits under every page: this is the
   * first time the player has ever heard of a punch card, and the ceremony is
   * about to hand them two holes. */
  const note = el('p', 'arc-enroll-note', String(t('enroll_card_line',
    'Every class carries a stamp card. Ten stamps, one a night.')));
  card.appendChild(note);

  const go = el('button', 'btn primary', '');
  go.type = 'button';
  go.addEventListener('click', () => next());
  signExit(go, { dir: 'go' });
  // The sign wraps the label in its own span, so the label has to move THROUGH
  // the sign's node rather than through the button's textContent.
  const label = go;
  card.appendChild(cardBar([go]));

  /* THE LABEL LIVES INSIDE THE SIGN, AND WRITING THE BUTTON WIPES IT.
   * signExit() replaces the button's text with three children - the bulb lamps,
   * the arrow and an `.arc-sign-label` span - so `btn.textContent = 'Next'`
   * deletes the whole arrow board and leaves a plain slab. (Caught in a browser
   * capture, not by the suites: the DOM double hands back a real Array for
   * `children` and an `Array.isArray` guard therefore passed in node and failed
   * in Chromium, where it is an HTMLCollection. Never feature-test a DOM
   * collection with Array.isArray.) */
  function setLabel(text) {
    const kids = label.children || [];
    for (let i = 0; i < kids.length; i++) {
      const k = kids[i];
      if (k && k.className && String(k.className).indexOf('arc-sign-label') >= 0) {
        k.textContent = text;
        return;
      }
    }
    // Unsigned button (a sign that could not build): the plain path still works.
    label.textContent = text;
  }

  function paint() {
    body.textContent = '';
    for (const line of pages[page]) body.appendChild(el('p', 'arc-enroll-line', line));
    for (let i = 0; i < dotNodes.length; i++) {
      if (dotNodes[i].classList) dotNodes[i].classList[i === page ? 'add' : 'remove']('on');
    }
    const last = page >= pages.length - 1;
    setLabel(last ? t('enroll_begin', 'Begin class') : t('enroll_next', 'Next'));
    if (!s.reducedMotion && card.classList) {
      card.classList.remove('turn');
      // The reflow the reveal needs (trap 4's law, one card instead of a board).
      void card.offsetWidth;
      card.classList.add('turn');
    }
    focusSoon(go);
  }

  /**
   * @param {string} how   for the log
   * @param {boolean} run  call onDone (i.e. START THE CLASS). FALSE for close():
   *   the shell closes this from teardownClass, where `active` is still set for
   *   one more line - starting a game there would leave one running behind the
   *   board that the leave was supposed to reach.
   */
  function finish(how, run) {
    if (finished) return;
    finished = true;
    try { root.remove(); } catch (e) { /* noop */ }
    say('enrollment intro ' + how + ' (' + s.gameKey + ')');
    if (!run) return;
    try { if (s.onDone) s.onDone(); } catch (e) { say('enrollment onDone threw: ' + ((e && e.message) || e)); }
  }

  function next() {
    if (page >= pages.length - 1) { finish('read', true); return; }
    page += 1;
    paint();
  }

  s.mount.appendChild(root);
  paint();

  return {
    root,
    get done() { return finished; },
    /** Esc: get on with it. The way OUT is still the campus pill on the strip. */
    skip() { finish('skipped', true); },
    close() { finish('closed', false); },
  };
}

/* ============================================================================
 * THE CEREMONY
 * ==========================================================================*/

/** Punch schedule, ms from the card landing. Reduced motion collapses it. */
const BEAT = Object.freeze({ first: 520, gap: 600, unlock: 900, reduced: 140 });

/**
 * THE CARD, full screen, taking its holes.
 *
 * @param {Object} o
 * @param {Object} o.mount             where the overlay goes (dom.screen)
 * @param {string} o.gameKey
 * @param {string} o.name              class display name
 * @param {Object} o.card              store.punchCard(gameKey) BEFORE the mint
 *                                     for enrollment, or the host's post-mint
 *                                     card for a daily stamp
 * @param {'enrollment'|'daily'} o.reason
 * @param {number=} o.from             holes already on the card (the animation's
 *                                     starting point). Defaults to
 *                                     `card.punches - minted`.
 * @param {number=} o.to               holes to land on. Defaults to card.punches.
 * @param {boolean=} o.justUnlocked    play the unlock beat
 * @param {boolean=} o.reducedMotion
 * @param {Function=} o.onDone         dismissed
 * @param {Function=} o.onPunched      called once the last scheduled punch lands
 *                                     (enrollment posts `enrollment-done` here)
 * @param {Function=} o.log
 * @returns {?Object} {root, destroy(), reconcile(card, justUnlocked)}
 */
export function createPunchCeremony(o) {
  const s = o || {};
  if (!s.mount || typeof s.mount.appendChild !== 'function') return null;
  const say = typeof s.log === 'function' ? s.log : () => {};
  const reduced = !!s.reducedMotion;
  const enrollment = s.reason === 'enrollment';

  const card = s.card || {};
  const to = Math.max(0, Math.min(HOLES,
    Math.round(s.to == null ? (Number(card.punches) || 0) : s.to)));
  const from = Math.max(0, Math.min(to,
    Math.round(s.from == null ? Math.max(0, to - (enrollment ? 2 : 1)) : s.from)));

  const timers = new Set();
  let destroyed = false;
  let punchedDone = false;

  function later(fn, ms) {
    const id = setTimeout(() => {
      timers.delete(id);
      if (destroyed) return;
      try { fn(); } catch (e) { say('punch beat threw: ' + ((e && e.message) || e)); }
    }, Math.max(0, ms));
    timers.add(id);
    return id;
  }

  const root = el('div', 'arc-pcstage');
  attr(root, 'role', 'dialog');
  attr(root, 'aria-modal', 'true');
  const box = el('div', 'arc-pcstage-card');
  root.appendChild(box);

  box.appendChild(el('p', 'arc-kicker',
    enrollment ? t('enroll_kicker', 'Enrollment') : t('punchcard', 'Stamp Card')));
  box.appendChild(el('h2', 'arc-h2', String(s.name || s.gameKey || '')));

  const face = cardFace({
    gameKey: s.gameKey,
    // Draw the card as it will be, but start the HOLES at the pre-mint count -
    // the punches below are what closes the gap, and that is the whole beat.
    card: Object.assign({}, card, { complete: false }),
    name: String(s.name || s.gameKey || ''),
    showPunches: from,
  });
  box.appendChild(face.root);

  /* The dialogue beat. One line at a time, replaced rather than stacked - a
   * ceremony that grows a paragraph is a ceremony nobody reads. */
  const line = el('p', 'arc-pc-line', '');
  attr(line, 'role', 'status');
  box.appendChild(line);

  const unlockBox = el('div', 'arc-pc-unlock');
  unlockBox.hidden = true;
  unlockBox.appendChild(el('h3', 'arc-pc-unlock-title',
    t('punchcard_unlocked_title', 'Assignment complete')));
  unlockBox.appendChild(el('p', 'arc-pc-unlock-line',
    t('punchcard_unlocked_line',
      'This room is now open even when the course is not in session.')));
  box.appendChild(unlockBox);

  const done = el('button', 'btn primary', t('done', 'Done'));
  done.type = 'button';
  done.addEventListener('click', () => api.destroy());
  signExit(done, { dir: 'back' });
  box.appendChild(cardBar([done]));

  function setLine(text) { line.textContent = String(text || ''); }

  /** One hole, one thud, one line. */
  function beat(n, pitch, text) {
    const moved = face.punch(n, { quiet: reduced });
    if (moved) thud(pitch);
    if (text) setLine(text);
  }

  function unlockBeat(unlockedAt) {
    unlockBox.hidden = false;
    // The card's own MASTERED strip carries the date the host closed it on; the
    // panel below carries the sentence. Neither invents the other's copy.
    face.markComplete(unlockedAt || card.unlockedAt);
    thud(THUD_PITCH.unlock);
    // The panel carries the unlock line; the dialogue line deliberately does NOT
    // repeat it. Printing the same sentence twice, one above the other, is what
    // the first capture did and it read as a rendering bug.
    setLine('');
  }

  function schedule() {
    const step = reduced ? BEAT.reduced : BEAT.gap;
    const head = reduced ? BEAT.reduced : BEAT.first;
    if (enrollment) {
      /* DAY ONE IS EXACTLY TWO (owner ruling, §3). The first hole is for
       * finishing; the second is on the house and says so. */
      later(() => beat(from + 1, THUD_PITCH.first,
        t('enroll_tutorial_line', 'One stamp for finishing your first class.')), head);
      later(() => beat(from + 2, THUD_PITCH.house,
        t('enroll_house_line', 'And one on the house. Welcome to the class.')), head + step);
      later(() => {
        punchedDone = true;
        try { if (s.onPunched) s.onPunched(); } catch (e) { say('onPunched threw: ' + ((e && e.message) || e)); }
      }, head + step + 40);
    } else {
      later(() => {
        beat(to, THUD_PITCH.daily, t('punchcard_stamped', 'Stamped for today.'));
        punchedDone = true;
        try { if (s.onPunched) s.onPunched(); } catch (e) { say('onPunched threw: ' + ((e && e.message) || e)); }
      }, head);
      if (!s.justUnlocked && to < HOLES) {
        later(() => setLine(t('punchcard_next_hole', 'Come back tomorrow for the next stamp.')),
          head + step);
      }
    }
    if (s.justUnlocked) later(unlockBeat, head + step + (reduced ? BEAT.reduced : BEAT.unlock));
  }

  const api = {
    root,
    gameKey: s.gameKey,
    reason: s.reason,
    get punched() { return punchedDone; },
    /**
     * The host answered. `card.punches` is the POST-mint total, so this only
     * ever punches FORWARD to it - a ceremony that had already animated past a
     * lower number would otherwise have to un-punch a hole, and a hole that
     * un-punches is a card nobody trusts.
     */
    reconcile(next, justUnlocked) {
      if (destroyed || !next) return;
      const target = Math.max(0, Math.min(HOLES, Math.round(Number(next.punches) || 0)));
      // Wait out the scheduled beats: reconciling mid-animation would snap the
      // holes the player is watching land.
      later(() => {
        if (face.punchTo(target, { quiet: true })) thud(THUD_PITCH.daily);
        if (justUnlocked || next.complete) unlockBeat(next.unlockedAt);
      }, punchedDone ? 0 : (reduced ? BEAT.reduced * 3 : BEAT.first + BEAT.gap + 80));
    },
    destroy() {
      if (destroyed) return;
      destroyed = true;
      for (const id of Array.from(timers)) clearTimeout(id);
      timers.clear();
      try { root.remove(); } catch (e) { /* noop */ }
      try { if (s.onDone) s.onDone(); } catch (e) { /* noop */ }
    },
  };

  s.mount.appendChild(root);
  focusSoon(done);
  schedule();
  return api;
}

export default { createEnrollmentIntro, createPunchCeremony, enrollLines, ENROLL_LEX };
