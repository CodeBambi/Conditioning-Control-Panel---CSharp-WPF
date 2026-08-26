/* ============================================================================
 * shell/corkboard.js - THE NOTICEBOARD (PHANTOM POST, agent M2).
 *
 * A cork panel in the hall with paper pinned to it. A term's worth of notices
 * exists; only a handful are up on any given night, and which ones is a function of the
 * DAY, not of a roll - so the board changes on a slow clock and two players on
 * the same night are looking at the same wall.
 *
 * It is an OVERLAY, not a screen: it never touches the shell's router, it mints
 * its own fixed stage, and it comes off by REMOVAL rather than by the hidden
 * attribute (trap 27). The pattern is shell/annexreveal.js's - module-local
 * stage, one dismiss funnel, every timer owned - with the reading furniture of
 * shell/records.js (kicker / h1 / lede / exit bar).
 *
 * THE TABLE BELOW IS THE WALL'S OWN PAPER: the term's notices as they were
 * written, three kinds of them, two permanently pinned and the rest going up
 * as their moment comes or rotating through the remaining slots.
 *
 * ---------------------------------------------------------------------------
 * STATE-NEEDS  (the driver's Wave 4 - this file persists NOTHING itself)
 * ---------------------------------------------------------------------------
 * Everything below is read from and written to an INJECTED plain object handed
 * in as `state`, with an injected `save(state)` callback. This module never
 * imports core/store.js, never posts a meta-command and never touches
 * localStorage. Hand it `{}` and it degrades to a board that forgets.
 *
 *   state.notices        { <noticeId>: { seenAt: string|null } }
 *                        One row per notice the player has actually had up on
 *                        the wall. `seenAt` is a LOCAL 'yyyy-mm-dd' (trap 8:
 *                        every date the player is shown on this page is local).
 *                        Written when the board is opened, for every notice
 *                        that is pinned up in that visit.
 *
 *   state.lastPinDay     string|null. The UTC day seed of the last set the
 *                        player actually SAW pinned up. It is not what selects
 *                        the set (the seed does that, purely) - it is how the
 *                        prop knows to wear its "something new" dot without
 *                        re-reading every row.
 *
 *   state.openedAt       string|null. LOCAL 'yyyy-mm-dd' of the last visit.
 *                        The prop's calm/new treatment hangs off it.
 *
 *   state.visits         number. Plain counter, incremented once per open.
 *
 * The counters the driver was asked to own (`boardSeen` and its family) are
 * NOT written here: this module reports what it saw through `onOpen` and
 * through the state object above, and the driver decides what a counter means.
 *
 * ---------------------------------------------------------------------------
 * WIRING THE DRIVER STILL OWNS
 * ---------------------------------------------------------------------------
 *   - Esc. House law (shell/exits.js header): nothing outside shell.js handles
 *     Esc by itself, and a modal the player opened one press ago gets ONE rung
 *     at the TOP of escapeStep (trap 48). So this overlay ships `close()` and
 *     binds NOTHING by default. `openCorkboard({ bindEscape: true })` exists
 *     for the standalone/demo case and is off in the shell.
 *   - Where the prop sits on the campus, and what it is appended to.
 *   - The lexicon rows. Every chrome string goes through `t(key, fallback)`,
 *     so an unmirrored key renders its English fallback (trap 15) until the
 *     host's NeutralLexicon grows the `board_*` family.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { makeRng, makeTaggedRoll, shuffled } from '../core/rng.js';
import { exitBar, sign as signExit } from './exits.js';

/* ----------------------------------------------------------------------------
 * THE SHEET
 * A real file (shell/corkboard.css), linked once and lazily, resolved against
 * THIS MODULE rather than the document - shell modules and the document can sit
 * at different roots (the campus logo bug, campus.js:320). Injecting the link
 * from here rather than growing a line in index.html is what keeps this wave to
 * new files only.
 * -------------------------------------------------------------------------- */

export const STYLE_ID = 'arc-corkboard-style';

export const STYLE_HREF = (function resolveSheet() {
  try { return new URL('./corkboard.css', import.meta.url).href; }
  catch (e) { return 'shell/corkboard.css'; }
}());

/** Link the sheet once. Idempotent, guarded, a no-op on the node DOM double. */
export function ensureStyles(doc) {
  const d = doc || (typeof document !== 'undefined' ? document : null);
  if (!d || typeof d.createElement !== 'function') return false;
  try {
    if (d.getElementById && d.getElementById(STYLE_ID)) return true;
    const link = d.createElement('link');
    link.id = STYLE_ID;
    link.rel = 'stylesheet';
    link.href = STYLE_HREF;
    const head = d.head || d.body || d.documentElement;
    if (!head || typeof head.appendChild !== 'function') return false;
    head.appendChild(link);
    return true;
  } catch (e) { return false; }
}

/* ----------------------------------------------------------------------------
 * THE TABLE
 * The term's notices, in the order they hang. `kind` drives the paper treatment
 * and the pin colour, nothing mechanical. `pinned` is "this one is always up" -
 * a permanent fixture of the wall, which is what stops the rotation from ever
 * handing the player a blank board. `look` is one treatment token or two
 * ('pencil', 'pencil askew'). `pair` keeps two sheets travelling together
 * through the rotation. `when` is the gate the shell answers before a notice is
 * eligible at all; a sheet without one is always eligible.
 * -------------------------------------------------------------------------- */

/** The paper treatments. `poster` is the odd one and the comment is the whole
 *  warning: it is the ONLY kind with no row in NOTICES - a poster has no title,
 *  no body, no `seenAt` and nothing to read, because it is a printed sheet and
 *  not a notice somebody wrote. See THE POSTER DROP below. */
export const NOTICE_KINDS = Object.freeze(['notice', 'flyer', 'minutes', 'poster']);

export const NOTICES = Object.freeze([
  Object.freeze({
    id: 'n01',
    kind: 'notice',
    pinned: false,
    title: 'NOTICE',
    body: Object.freeze([
      'The hedge has taken the bench, and I want it understood by all concerned that I gave that bench every warning, on the record and in fair weather, which is more than the hedge ever gave me, and that the bench went without a sound, which I take hard, because we had our differences but it was a good bench.',
    ]),
    when: Object.freeze({ beforeDelivered: 'm03' }),
    look: 'pencil',
  }),
  Object.freeze({
    id: 'n02',
    kind: 'notice',
    pinned: false,
    title: 'FROM THE TOWER LOG',
    body: Object.freeze([
      'Tuesday the second, clear, wind light from the west, bells rang six exactly as they always do, and the front desk clock is keeping the other time again this term, which is its right, and which I record here without heat, the way a man records that the neighbouring field grows a different crop.',
      'Kept, T. Pendle.',
    ]),
    when: Object.freeze({ beforeDelivered: 'm03' }),
  }),
  Object.freeze({
    id: 'n03',
    kind: 'flyer',
    pinned: false,
    title: 'MAIN HALL, EVENING OF THE TWENTY-NINTH: SIGN-UP SHEET',
    body: Object.freeze([
      'Further to my circular of Tuesday last, colleagues wishing to reserve the Main Hall for the evening of the twenty-ninth are invited to enter their names below in ink, in confidence that the first name entered shall enjoy priority, the fairness of which principle I trust no colleague will dispute in writing.',
      'Yours in continuity,',
      'A. Petch, Acting Deputy Head',
      '1. Dr. F. Sharp, Music Room (entered first, whatever is claimed below)',
      '2. P. Tartine, the kitchen (entered before the above, whichever line she wrote it on)',
    ]),
    when: Object.freeze({ beforeDelivered: 'm03' }),
  }),
  Object.freeze({
    id: 'n04',
    kind: 'minutes',
    pinned: false,
    title: 'MINUTES OF THE INQUIRY, FOURTH SITTING (ADJOURNED)',
    body: Object.freeze([
      'Further to the minutes of the third sitting, the fourth sitting of the inquiry convened at four o\'clock, or at four minutes past, present the Convenor, and present also the Convenor in his capacity as assistant, no apologies having been received, which the sitting agreed to interpret generously. The sitting reviewed the evidence gathered to date, being the pins, and had entered upon the question of the light in the Entrance Hall when correspondence of the highest character was received touching the autumn programme, whereupon the Convenor ruled, seconding himself, that the inquiry stand adjourned out of respect, the question of on whose authority it should resume being deferred until an authority can be found to defer it to.',
      'Yours in continuity,',
      'A. Petch, Acting Deputy Head and Convenor of the Inquiry (Adjourned)',
    ]),
    when: Object.freeze({ daysAfterRead: ['m07', 0], beforeDelivered: 'm08' }),
  }),
  Object.freeze({
    id: 'n05',
    kind: 'notice',
    pinned: false,
    title: 'NOTICE',
    body: Object.freeze([
      'The hedge has reached the Front Path, having crossed open lawn in under a fortnight, which I said it would do and have it in pencil that I said so, and I am now asking, formally and without prejudice, for either shears or reinforcements, whichever the school can spare first.',
    ]),
    when: Object.freeze({ daysAfterRead: ['m08', 0], beforeDelivered: 'm13' }),
    look: 'pencil',
  }),
  Object.freeze({
    id: 'n06',
    kind: 'notice',
    pinned: false,
    title: 'NOTICE',
    body: Object.freeze([
      'Me and the hedge have come to an arrangement, and that is all I have to say about that.',
    ]),
    when: Object.freeze({ daysAfterRead: ['m15', 0] }),
    look: 'pencil',
  }),
  Object.freeze({
    id: 'n07',
    kind: 'notice',
    pinned: false,
    title: 'FROM THE TOWER LOG',
    body: Object.freeze([
      'Friday\'s forecast is fair, turning colder at dusk, and the tower gives notice that true midnight will be observed on the night of the contested evening, rung on the usual three bells, so that whichever occasion goes forward in the Hall will begin at two different times depending on whose evening a person keeps, and those keeping the other midnight may keep whichever one they can live with.',
      'Kept, T. Pendle.',
    ]),
    when: Object.freeze({ daysAfterRead: ['m15', 0] }),
  }),
  Object.freeze({
    id: 'n08',
    kind: 'notice',
    pinned: false,
    title: 'NOTICE',
    body: Object.freeze([
      'The bench is back, it is facing the wrong way, I did not move it, the hedge did not move it, and I will vouch for the visitor, so the matter of who moved it stays open, and the bench will stay facing the wrong way until it tells me itself, because I am done moving furniture for parties that will not communicate.',
    ]),
    when: Object.freeze({ issueRead: 'g5' }),
    look: 'pencil',
  }),
  Object.freeze({
    id: 'n09',
    kind: 'notice',
    pinned: false,
    title: 'FROM THE TOWER LOG',
    body: Object.freeze([
      'Saturday, fair, a high moon and no wind to speak of, and I record that both midnights passed without incident, true midnight first and the other one four minutes after, that the Hall stood quiet through the pair of them, and that the Quad this morning had the settled look it gets when a thing everyone dreaded has finished not happening.',
      'Kept, T. Pendle.',
    ]),
    when: Object.freeze({ issueRead: 'g5' }),
  }),
  Object.freeze({
    id: 'n10',
    kind: 'notice',
    pinned: false,
    title: 'NOTICE',
    body: Object.freeze([
      'Whoever keeps propping the number three rake against the north wall should know that the rakes are numbered for a reason, that the reason is a good one, that it has served this Quad since before the flagpole, and that I will not be explaining it, since a system explained is a system argued with.',
    ]),
    look: 'pencil',
  }),
  Object.freeze({
    id: 'n11',
    kind: 'notice',
    pinned: false,
    title: 'NOTICE',
    body: Object.freeze([
      'The bed by the south wall that certain parties attached to the kitchen have taken to calling a larder is and remains a load-bearing feature of the whole Quad, entered in my planting book before the flagpole went up, and the rosemary removed from it on Thursday was removed without prejudice to my position, which is on the record, in pencil, pressed hard.',
    ]),
    look: 'pencil',
    pair: 'herb',
  }),
  Object.freeze({
    id: 'n12',
    kind: 'notice',
    pinned: false,
    title: 'A NOTICE CONCERNING THE HERB BED, ADDRESSED TO NOBODY IN PARTICULAR',
    body: Object.freeze([
      'The kitchen wishes it known that the herb bed by the south wall is a working larder and not, whatever certain notices in this vicinity may imply, an ornamental frontier, and that the rosemary taken up on Thursday was taken up by the person who planted it, fed it, and defended it through two frosts, which is a form of ownership no amount of pencil can amend. The bed will be cut as the menu requires, the menu being a matter of record and of art, five stars this week as it happens, and any party wishing to discuss the matter knows where the kitchen is, though I note that no such party has ever once come to it. Bon apettit.',
      'From the kitchen, with what remains of my patience,',
      'P. Tartine',
    ]),
    pair: 'herb',
  }),
  Object.freeze({
    id: 'n13',
    kind: 'notice',
    pinned: false,
    title: 'NOTICE',
    body: Object.freeze([
      'One of my two pins has been borrowed off this very notice, which means somebody stood here, read my words about respecting the fixtures of this board, and took the pin anyway, and I want that person to know the pin was part of a pair, in perpetuity, and that its fellow now holds this notice up alone, which you will observe it is doing, at an angle.',
    ]),
    look: 'pencil askew',
  }),
  Object.freeze({
    id: 'n14',
    kind: 'notice',
    pinned: false,
    title: 'NOTICE',
    body: Object.freeze([
      'Leaf season opens Monday, and the boy will be helping with the east side as usual, so if the barrow is missing from the shed between the hours of eight and eleven, that is where the barrow is, and there is no cause for another note about it.',
    ]),
    when: Object.freeze({ monthIn: [9, 10, 11] }),
    look: 'pencil',
  }),
  Object.freeze({
    id: 'n15',
    kind: 'flyer',
    pinned: false,
    title: 'MENU SUPPLEMENT, WEEK BEGINNING MONDAY',
    body: Object.freeze([
      'Monday brings the barley broth, which my mother made the year the river froze and which I have never once altered, five stars; Tuesday brings the soup, of which enough has been printed elsewhere by cowards, five stars; Wednesday is the pie\'s day of rest, so Wednesday brings the other pie; Thursday brings the reduction, and what the reduction takes from me every week, only the pan knows; and Friday is governed by the standing arrangement, on which the kitchen will take no questions.',
      'To the one who signs himself The Palate, if he is reading this board, and he is: the kitchen remembers 1994, monsieur, and the kitchen is patient. Bonn appetit.',
      'From the kitchen, with what remains of my patience,',
      'P. Tartine',
    ]),
  }),
  Object.freeze({
    id: 'n16',
    kind: 'notice',
    pinned: true,
    title: 'FROM THE TOWER LOG, A STANDING NOTE',
    body: Object.freeze([
      'September the first, mild, as it usually is when this goes up, and I post the standing note for the new intake, unchanged in the posting: the tower rings three bells, the fourth bell rests, as it has rested, and enquiries regarding the fourth bell may be left at the foot of the stairs, where they will receive my thanks and no answer, the stairs not being for visitors.',
      'Kept, T. Pendle.',
    ]),
  }),
  Object.freeze({
    id: 'n17',
    kind: 'minutes',
    pinned: false,
    title: 'MINUTES OF THE STUDENT COUNCIL, MICHAELMAS SITTING',
    body: Object.freeze([
      'Further to the minutes of the summer sitting, which were taken as read at the summer sitting, they having also been taken as read at the sitting before that, and by the same procedure, the Michaelmas sitting of the Student Council was declared open at four o\'clock by the front desk and at four minutes past by the tower, the Chair, wishing to be even-handed, declaring it open at both. The sitting had been due to convene in the faculty lounge, which could not be found on the day, and removed itself to my office, where, no members being present, the agenda was carried entire and without dissent, a smoothness of business the Chair invited the minutes to record as stability. The meeting closed at a quarter past, or at eleven past, and the Chair remained a further quarter of an hour in case of latecomers, of whom, the minutes are asked to note without comment, there have never been any.',
      'Yours in continuity,',
      'A. Petch, Acting Deputy Head and Chair (Acting) of the Student Council',
    ]),
  }),
  Object.freeze({
    id: 'n18',
    kind: 'notice',
    pinned: true,
    title: 'FIRE DRILL: NOTICE OF DATE (AMENDED)',
    body: Object.freeze([
      'The fire drill will be held on the fourteenth of May, corrected in ink to the second of October, corrected again in a different ink to the ninth, and further amended in pencil, in a hand nobody claims, to read simply the spring. Assembly is on the far lawn, weather and groundskeeping matters permitting, and everyone is kindly asked to leave this notice where it hangs, as it is the only copy, has served since before any current tenure, and is expected, at the present rate of amendment, to serve for some years yet.',
    ]),
  }),
  /* ------------------------------------------------------------------------
   * WET INK (THE SEEP, tell 09) - and it is the one sheet on this wall that is
   * LEXICON-KEYED rather than a literal.
   *
   * Everything else here is the season's own prose and stays a literal for the
   * same reason EMI's lines are (diegetic, change-hostile, mod-neutral). This
   * one is user-facing copy the C# NeutralLexicon has to mirror, so it comes
   * through `titleKey` / `bodyKeys` - clause rows joined with one space, every
   * row under trap 26's 96-character mod-skin cap (core/lexicon.js
   * `seep_wetink_*`). The `body` below is the same text as a fallback, so a host
   * that never grew the rows renders the note verbatim anyway (trap 15).
   *
   * IT IS NOT A FLICKER. It goes up and it stays up: `when` makes it a WINDOWED
   * notice, which `pickNotices` treats as always-up once its gate passes, so
   * rotation can never sit on it. The gate is ONE sealed punch card - the same
   * count shell/seep.js's ladder runs on, which is what puts the note on the
   * wall exactly at T1 without the corkboard knowing the seep exists.
   * --------------------------------------------------------------------- */
  Object.freeze({
    id: 'n19',
    kind: 'notice',
    pinned: false,
    title: 'FROM THE FRONT DESK',
    titleKey: 'seep_wetink_title',
    body: Object.freeze([
      'Couple of things this week: the water fountain by 103 is fixed, you\'re welcome, '
        + 'and whoever keeps winning the gate raffle please come collect your pencils. '
        + 'Also if you see light under the Records door after closing, that\'s just the old '
        + 'wiring acting up again, Marco says he\'ll swap the breaker when the part shows up. '
        + 'Be good.',
      'The front desk.',
    ]),
    bodyKeys: Object.freeze([
      Object.freeze(['seep_wetink_1', 'seep_wetink_2', 'seep_wetink_3', 'seep_wetink_4', 'seep_wetink_5']),
      Object.freeze(['seep_wetink_sig']),
    ]),
    when: Object.freeze({ sealedAtLeast: 1 }),
    look: 'pencil',
  }),
]);

/** How many sheets the wall holds at once. Pinned rows always make the cut. */
export const BOARD_SLOTS = 4;

/* ----------------------------------------------------------------------------
 * THE POSTER DROP (Counter Stock, `poster_drop_1`)
 *
 * A print off the Prize Counter, pinned up with the paper. Three rules, and the
 * first one is the reason this is fifteen lines rather than a second module:
 *
 *  1. A POSTER TAKES A SLOT, IT DOES NOT ADD ONE. The wall holds four sheets;
 *     a fifth would re-flow a board that two players are looking at from
 *     different rooms. So the notices are picked for `slots - 1` and the poster
 *     stands in the seat that opened - the wall is still four things.
 *  2. THE ART MAY LAG THE CODE, AND THE WALL MUST NOT NOTICE. The image's
 *     `onerror` takes the WHOLE slot down (not the image - a pinned empty
 *     rectangle is worse than a shorter wall), so a build that ships the sku
 *     before the print reads as a board with three sheets on it and nothing
 *     else. Nobody is ever shown a broken picture of a poster.
 *  3. SAME NIGHT, SAME POSTER, EVERY WALL. It is seeded off the day exactly
 *     like the rotation, so the hall's overlay, the Records Office close-up and
 *     the office's own miniature all print the same one - which is what makes
 *     it a poster that is UP rather than a picture that is drawn.
 *
 * The ids are the print run. They are final ahead of the art on purpose: a
 * renamed file is a wall that goes blank on somebody's night.
 * -------------------------------------------------------------------------- */

/** The print run, in catalogue order. ONE list - never inline an id. */
export const POSTERS = Object.freeze([
  'poster_attend',
  'poster_eyes_front',
  'poster_stay_late',
  'poster_dive_deep',
  'poster_good_work',
  'poster_listen_well',
]);

/** Where a print lives. Module-relative (the campus logo bug, campus.js:320). */
export function posterUrl(id) {
  const name = String(id == null ? '' : id);
  try { return new URL('../art/posters/' + name + '.png', import.meta.url).href; }
  catch (e) { return 'art/posters/' + name + '.png'; }
}

/**
 * Tonight's print. PURE: same seed in, same poster out, for ever - and its own
 * tag on the seed, so adding a poster can never re-deal the notices under it.
 * @param {string} daySeed
 * @returns {string} one of POSTERS
 */
export function pickPoster(daySeed) {
  const roll = makeTaggedRoll(String(daySeed == null ? '' : daySeed) + '|board-poster');
  const i = Math.floor(roll('which') * POSTERS.length);
  return POSTERS[Math.max(0, Math.min(POSTERS.length - 1, i))];
}

/* ----------------------------------------------------------------------------
 * THE ROTATION
 * Deterministic by DAY, never by Math.random - the same law core/timetable.js
 * runs the night's four classes under. The seed is a UTC day string, because
 * UTC seeds CONTENT and local dates roll attendance (trap 8), and the set of
 * notices on a wall is content.
 * -------------------------------------------------------------------------- */

/** 'yyyy-mm-dd' in UTC. The default seed when the shell has not handed one in. */
export function utcDaySeed(when) {
  const d = (when instanceof Date) ? when : new Date();
  const y = d.getUTCFullYear();
  const m = String(d.getUTCMonth() + 1).padStart(2, '0');
  const day = String(d.getUTCDate()).padStart(2, '0');
  return y + '-' + m + '-' + day;
}

/** 'yyyy-mm-dd' in LOCAL time. What a date STAMP on this page always is. */
export function localDay(when) {
  const d = (when instanceof Date) ? when : new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return y + '-' + m + '-' + day;
}

/**
 * The night's wall. PURE: same seed in, same sheets out, for ever.
 *
 * Two properties worth keeping when this is edited: the pinned rows are always
 * present (so the wall is never blank), and the answer is returned in CATALOG
 * order rather than in draw order - the membership rotates, the layout does
 * not, so a returning player reads the wall the same way twice.
 *
 * @param {string} daySeed
 * @param {number=} slots
 * @returns {Array} the subset of NOTICES that is up
 */
export function pickNotices(daySeed, slots, list) {
  const seed = String(daySeed == null ? '' : daySeed);
  const table = Array.isArray(list) ? list : NOTICES;
  const want = Math.max(1, Math.min(table.length,
    Math.round(Number(slots) || BOARD_SLOTS)));
  /* Three tiers. PINNED is furniture and always up. A WINDOWED notice (it
   * carries a `when` gate and it is in the list at all, so the gate said yes)
   * is a story beat and its moment is NOW - rotation may not sit on it. Only
   * the evergreens rotate. */
  const alwaysUp = table.filter((n) => n.pinned || n.when);
  const loose = table.filter((n) => !n.pinned && !n.when);
  const room = Math.max(0, want - alwaysUp.length);
  /* Evergreens rotate as GROUPS: a notice with a `pair` key travels with its
   * partners (the herb bed war is two notices or none - half an argument on
   * the wall reads as a bug, not a feud). */
  const groups = [];
  const byPair = Object.create(null);
  for (let i = 0; i < loose.length; i += 1) {
    const n = loose[i];
    const key = n.pair ? String(n.pair) : null;
    if (!key) { groups.push([n]); continue; }
    if (!byPair[key]) { byPair[key] = []; groups.push(byPair[key]); }
    byPair[key].push(n);
  }
  const drawnGroups = shuffled(groups, makeRng(seed + '|board-rota'));
  const keep = Object.create(null);
  let used = 0;
  for (let i = 0; i < drawnGroups.length && used < room; i += 1) {
    const g = drawnGroups[i];
    if (used + g.length > room && used > 0) continue;   // a pair never splits
    for (let j = 0; j < g.length; j += 1) { keep[g[j].id] = true; used += 1; }
  }
  return table.filter((n) => n.pinned || n.when || keep[n.id]);
}

/**
 * How a sheet HANGS. Also seeded, also by day, so the wall is not re-arranged
 * under a player who reopens it, and a screenshot taken twice matches itself.
 * @returns {{rot:number, pinX:number, lift:number, torn:boolean}}
 */
export function pinGeometry(daySeed, noticeId) {
  const roll = makeTaggedRoll(String(daySeed == null ? '' : daySeed) + '|board-pin');
  const id = String(noticeId == null ? '' : noticeId);
  const rot = (roll(id + ':rot') * 2 - 1) * 3.1;      // -3.1deg .. +3.1deg
  const pinX = 30 + roll(id + ':pin') * 40;            // 30% .. 70% across
  const lift = roll(id + ':lift') * 6;                 // 0 .. 6px of hang
  const torn = roll(id + ':torn') > 0.78;              // a corner gone, sometimes
  return { rot: rot, pinX: pinX, lift: lift, torn: torn };
}

/* ----------------------------------------------------------------------------
 * PLUMBING
 * -------------------------------------------------------------------------- */

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

function attr(node, name, value) {
  try { if (node && typeof node.setAttribute === 'function') node.setAttribute(name, value); }
  catch (e) { /* the DOM double may not carry attributes - never fatal */ }
}

function styleVar(node, name, value) {
  try { if (node && node.style && typeof node.style.setProperty === 'function') node.style.setProperty(name, value); }
  catch (e) { /* noop */ }
}

function focusSoon(node) {
  try { if (node && typeof node.focus === 'function') node.focus(); }
  catch (e) { /* noop */ }
}

/**
 * One cue through the one door (trap 18): shell/audio.js holds the only audio
 * node on the page, so this is a REQUEST on `document` and never a sound.
 * Copied shape from shell/records.js sfx(). A dropped cue is not an error.
 */
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

/* ----------------------------------------------------------------------------
 * THE FIT - a sheet is as tall as its copy, and the TYPE comes down to the box
 *
 * The office's cork is a close-up of a painting: the sheets are laid out in the
 * plate's own 1285x692 pixels and the whole plate is scaled to the window, so
 * on a phone in landscape one stage pixel is about half a screen pixel. The
 * first pass at that gave every sheet a `max-height` and a `mask-image` fade,
 * and a long notice dissolved mid-sentence - which is not how printed paper
 * works (owner ruling, 2026-08-25). So nothing is cut any more. What is left
 * is an ordering problem, and this is the cheap half of the answer:
 *
 *   a row of the board is worth `boxH / rows`. A sheet over that budget gives
 *   up TYPE - `--note-fs`, the one dial the three type rules are ratios of -
 *   down to a floor of real SCREEN pixels (10 on a phone, 11 on a desktop,
 *   divided back out by the stage scale). A sheet that is still over at the
 *   floor keeps every word it has and the board scrolls instead.
 *
 * Height goes roughly with the square of the type (a smaller face fits more
 * characters per line AND more lines per inch), so the step is a sqrt and
 * three passes are plenty; a pass that would move the size by less than a
 * fifth of a pixel is the end of the ladder.
 *
 * IT MEASURES IN LAYOUT PIXELS, WHICH IS WHAT MAKES THE MINIATURE HONEST.
 * `offsetHeight` ignores the transform the stage rides, so a wall laid out at
 * 1285px measures 1285px whether it is painted at half size on a phone or a
 * fifth of that in the wide shot's thumbnail. Hand BOTH the same `boxH` and
 * the same `scale` and they deal the same type at the same rows - which is the
 * whole of why the preview is a picture of the board and not a second board.
 * -------------------------------------------------------------------------- */

/** The smallest type this wall will print, in SCREEN pixels. */
export const FIT_FLOOR_PHONE = 10;
export const FIT_FLOOR_DESK = 11;

function isPhone() {
  try {
    const de = (typeof document !== 'undefined') ? document.documentElement : null;
    return !!(de && de.classList && de.classList.contains('arc-mobile'));
  } catch (e) { return false; }
}

function px(v, dflt) {
  const n = parseFloat(v);
  return isFinite(n) ? n : dflt;
}

/**
 * Shrink each sheet's type until its row's share of the board holds it.
 * @param {Object} hostEl   the wall (the grid)
 * @param {Array} sheets    the `.arc-corknote` nodes, in DOM order
 * @param {{boxH:number, scale:(number|Function), floorPx:number=}} fit
 */
export function fitSheets(hostEl, sheets, fit) {
  if (!fit || !hostEl || !sheets || !sheets.length) return false;
  if (typeof window === 'undefined' || typeof window.getComputedStyle !== 'function') return false;
  const boxH = Number(fit.boxH) || Number(hostEl.clientHeight) || 0;
  if (!(boxH > 80)) return false;
  let scale = 1;
  try { scale = (typeof fit.scale === 'function') ? Number(fit.scale()) : Number(fit.scale); }
  catch (e) { scale = 1; }
  if (!(scale > 0)) scale = 1;
  const floor = (Number(fit.floorPx) || (isPhone() ? FIT_FLOOR_PHONE : FIT_FLOOR_DESK)) / scale;

  const cs = window.getComputedStyle(hostEl);
  const cols = String(cs.gridTemplateColumns || '').trim().split(/\s+/).filter(Boolean).length || 1;
  const rows = Math.max(1, Math.ceil(sheets.length / cols));
  const gap = px(cs.rowGap, 0);
  const budget = (boxH - px(cs.paddingTop, 0) - px(cs.paddingBottom, 0) - gap * (rows - 1)) / rows;
  if (!(budget > 60)) return false;

  for (let i = 0; i < sheets.length; i += 1) {
    const sheet = sheets[i];
    if (!sheet || !sheet.style || typeof sheet.style.setProperty !== 'function') continue;
    try { sheet.style.removeProperty('--note-fs'); } catch (e) { /* noop */ }
    const base = px(window.getComputedStyle(sheet).getPropertyValue('--note-fs'), 0);
    if (!(base > 0) || base <= floor + 0.25) continue;
    let fs = base;
    for (let pass = 0; pass < 3; pass += 1) {
      const h = Number(sheet.offsetHeight) || 0;
      if (!(h > budget)) break;
      let next = fs * Math.sqrt(budget / h);
      if (next < floor) next = floor;
      if (next >= fs - 0.2) break;          // nothing left to give
      fs = next;
      sheet.style.setProperty('--note-fs', fs.toFixed(2) + 'px');
    }
  }
  return true;
}

function kindLabel(kind) {
  if (kind === 'flyer') return t('board_kind_flyer', 'Flyer');
  if (kind === 'minutes') return t('board_kind_minutes', 'Minutes');
  if (kind === 'poster') return t('board_kind_poster', 'Poster');
  return t('board_kind_notice', 'Notice');
}

/* ----------------------------------------------------------------------------
 * THE INJECTED HALF
 * `state` + `save` are the whole persistence story (see STATE-NEEDS). They can
 * be handed to initCorkboard once, or per-open; per-open wins.
 * -------------------------------------------------------------------------- */

const deps = {
  state: null,
  save: null,
  daySeed: null,
  mount: null,
  log: null,
  when: null,
  /* OWNERSHIP ARRIVES HERE AND NOWHERE ELSE (Counter Stock). A boolean or a
   * getter saying the player owns `poster_drop_1`. It lives on the module's
   * injected deps rather than on each call for one reason: three different
   * files mount this wall (the hall overlay, the Records Office close-up and
   * the office's miniature) and a flag threaded through three call sites is
   * three chances for one of them to print a different board. This file still
   * imports no store, knows no sku and reads no wallet - the shell answers. */
  posters: null,
};

/**
 * Hand the module its injected persistence and defaults. Every field optional.
 * `when` is the season gate evaluator: `(trigger) => boolean`, the shell's one
 * bridge between this wall and the rest of the story (mail.js's triggerHolds
 * over the shared context).
 * `posters` is the Counter Stock flag (see deps.posters): true, or a getter, when
 * the player owns `poster_drop_1`. Anything else - absent, false, a throw - is a
 * board with no print on it, which is the board this file shipped with.
 * @param {{state?:Object, save?:Function, daySeed?:string, mount?:Object, log?:Function, when?:Function, posters?:(boolean|Function)}} opts
 */
export function initCorkboard(opts) {
  const o = opts || {};
  if (o.state && typeof o.state === 'object') deps.state = o.state;
  if (typeof o.save === 'function') deps.save = o.save;
  if (o.daySeed != null) deps.daySeed = String(o.daySeed);
  if (o.mount) deps.mount = o.mount;
  if (typeof o.log === 'function') deps.log = o.log;
  if (typeof o.when === 'function') deps.when = o.when;
  if (o.posters !== undefined) deps.posters = o.posters;
  return deps.state;
}

/** Does the player own the poster drop? A getter that throws owns nothing. */
export function postersOwned(override) {
  const src = (override === undefined) ? deps.posters : override;
  if (typeof src === 'function') {
    try { return src() === true; } catch (e) { return false; }
  }
  return src === true;
}

/**
 * The notices whose moment this is. A notice without a `when` gate is always
 * eligible; one WITH a gate needs the injected evaluator to say yes, and with
 * no evaluator installed it stays down (fail closed - a notice held is a
 * notice that can still go up tomorrow).
 * @returns {Array}
 */
function eligibleNotices() {
  return NOTICES.filter((n) => {
    if (!n.when) return true;
    if (typeof deps.when !== 'function') return false;
    try { return !!deps.when(n.when); } catch (e) { return false; }
  });
}

/** The injected object, or a throwaway. Test seam, and the empty-state answer. */
function stateOf(override) {
  const s = (override && typeof override === 'object') ? override
    : (deps.state && typeof deps.state === 'object') ? deps.state : {};
  if (!s.notices || typeof s.notices !== 'object') s.notices = {};
  return s;
}

/* ----------------------------------------------------------------------------
 * KEYED COPY, and it is OPT-IN PER ROW.
 *
 * The season's own notices are literals on purpose (diegetic prose, the same
 * sanction EMI's lines hold). A row that DOES have to be mod-skinnable declares
 * `titleKey` and/or `bodyKeys` - the latter one CLAUSE LIST PER PARAGRAPH, so a
 * long paragraph can clear trap 26's 96-character cap and still read as one
 * sentence. A missing row degrades to the literal beside it, which degrades to
 * DEFAULT_LEXICON, which degrades to English (trap 15): three floors, and the
 * paper never renders a raw key.
 * -------------------------------------------------------------------------- */

/** The sheet's headline - keyed if the row asked, otherwise its literal. */
export function noticeTitle(notice) {
  const lit = String((notice && notice.title) || '');
  const key = notice && notice.titleKey;
  return (typeof key === 'string' && key) ? t(key, lit) : lit;
}

/** Paragraph `i` of the sheet - the clause rows joined with ONE space. */
export function noticeParagraph(notice, i, fallback) {
  const lit = String(fallback == null ? '' : fallback);
  const table = notice && notice.bodyKeys;
  const clauses = Array.isArray(table) ? table[i] : null;
  if (!Array.isArray(clauses) || !clauses.length) return lit;
  const parts = [];
  for (let k = 0; k < clauses.length; k += 1) {
    const v = t(String(clauses[k]), '');
    if (v) parts.push(v);
  }
  return parts.length ? parts.join(' ') : lit;
}

function persist(s, save) {
  const fn = (typeof save === 'function') ? save : deps.save;
  if (typeof fn !== 'function') return;
  try { fn(s); }
  catch (e) { if (deps.log) { try { deps.log('corkboard save: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
}

/** Is anything on tonight's wall unread? Drives the prop's quiet marker. */
export function hasUnread(daySeed, override) {
  const s = stateOf(override);
  const up = pickNotices(daySeed || deps.daySeed || utcDaySeed(), undefined, eligibleNotices());
  for (let i = 0; i < up.length; i += 1) {
    const row = s.notices[up[i].id];
    if (!row || !row.seenAt) return true;
  }
  return false;
}

/* ----------------------------------------------------------------------------
 * THE PAPER, AND IT IS THE SAME PAPER ON BOTH WALLS
 *
 * There are two places in the school with this cork on them now - the hall prop
 * opens the overlay below, and the Records Office has the board painted into its
 * own set (shell/recordsroom.js's `board` close-up, sheets pinned over the bare
 * cork in the plate). ONE TABLE, ONE STATE, ONE NIGHT'S SET: the owner's call,
 * and this function is how it is kept true. Everything that decides WHAT is up
 * and marks it read lives here; the overlay below is now only the stage, the
 * heading and the way out.
 *
 * It writes exactly what the overlay always wrote, in the same order and at the
 * same moment: the per-notice `seenAt` rows as each sheet is pinned, then the
 * one banked visit (lastPinDay / openedAt / visits) at the end. So opening the
 * office's board and opening the hall's board are the same visit to the same
 * wall, which is why the prop's fresh dot clears from either.
 * -------------------------------------------------------------------------- */

/**
 * Pin tonight's sheets into `hostEl`.
 *
 * @param {Object} hostEl               where the slots go (the caller's wall)
 * @param {Object=} opts
 * @param {string=} opts.daySeed        UTC day string; defaults to today
 * @param {Object=} opts.state          injected persistence (see STATE-NEEDS)
 * @param {Function=} opts.save         save(state) callback
 * @param {number=} opts.slots          how many sheets are up (default 4)
 * @param {Function=} opts.onRead       onRead(noticeId) per sheet marked read
 * @param {boolean=} opts.preview       A MINIATURE, not a visit: the same wall
 *                                      painted for a room to look alive from
 *                                      across it. Marks nothing read, banks no
 *                                      visit, takes no pointer and holds no
 *                                      focus. The Records Office's WIDE shot
 *                                      hangs one of these in the cork rect so
 *                                      the painted board has tonight's paper on
 *                                      it before you walk up to it.
 * @param {boolean=} opts.readable      each sheet gets a real control over it,
 *                                      and pressing one opens the READER (a
 *                                      full-size, scrollable copy over the
 *                                      window). Off by default: the hall's
 *                                      board is a page you already scroll.
 * @param {boolean=} opts.wholeRows    a wall that ends on a WHOLE sheet: any
 *                                      slot that does not fit inside the host
 *                                      is taken down, along with everything
 *                                      after it. For the MINIATURE, whose box
 *                                      is a painted board with a bottom rail -
 *                                      a sheet sliced flat along that rail
 *                                      reads as a rendering fault from across
 *                                      the room, where a shorter wall reads as
 *                                      a wall. Needs `fit` (it runs after it).
 * @param {(boolean|Function)=} opts.posters  does the player own `poster_drop_1`?
 *                                      Per-call override of the injected
 *                                      `deps.posters`; leave it out and every
 *                                      wall in the school answers the same way,
 *                                      which is the point.
 * @param {Object=} opts.fit          THE FIT (see above): {boxH, scale, floorPx}
 *                                      in STAGE pixels. Hand the same pair to
 *                                      a wall and to its miniature and the two
 *                                      lay out identically. Absent = the type
 *                                      the stylesheet asked for, untouched.
 * @param {Function=} opts.log
 * @returns {?Object} {notices, daySeed, first, refit(), closeReader(), destroy()}
 */
export function mountNotices(hostEl, opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  if (!hostEl || typeof hostEl.appendChild !== 'function') return null;

  ensureStyles(doc);

  const seed = String(o.daySeed != null ? o.daySeed : (deps.daySeed || utcDaySeed()));
  const s = stateOf(o.state);
  /* THE PRINT TAKES A SEAT (see THE POSTER DROP). The notices are dealt for one
   * slot fewer, so a wall with a poster on it is still a wall of four things -
   * and a player who does not own the drop deals exactly the four they always
   * did, off the same seed, in the same order.
   *
   * THE ONE NIGHT IT IS FIVE. `slots` is a TARGET, not a grid: pickNotices'
   * always-up tier (pinned furniture + a live story beat) overruns it already,
   * with or without a poster - a night with five windowed notices is a wall of
   * five and always was. On such a night the seat the poster asked for does not
   * exist, so it hangs beside the tier instead of eating a beat whose moment is
   * NOW. The invariant that actually holds, and the one the suite asserts, is
   * that owning the drop NEVER adds a notice: `up` with a poster is always <=
   * `up` without one, off the same seed. THE FIT takes any overflow down. */
  const poster = postersOwned(o.posters) ? pickPoster(seed) : null;
  const askedSlots = Math.max(1, Math.round(Number(o.slots) || BOARD_SLOTS));
  const up = pickNotices(seed, poster ? Math.max(1, askedSlots - 1) : askedSlots, eligibleNotices());
  const today = localDay();
  const say = (msg) => {
    const fn = (typeof o.log === 'function') ? o.log : deps.log;
    if (typeof fn === 'function') { try { fn(msg); } catch (e) { /* noop */ } }
  };

  const mounted = [];
  const sheets = [];
  const timers = [];
  let first = null;
  /* A MINIATURE IS SCENERY. It paints the same wall and writes nothing: no
   * `seenAt` rows, no banked visit, no focus, no pointer (the sheet's own
   * class does that half), and it is hidden from a reader - the real wall is
   * one press away and announcing both would be announcing it twice. */
  const preview = o.preview === true;
  if (preview) attr(hostEl, 'aria-hidden', 'true');

  if (!up.length && !poster) {
    // A wall with nothing on it is a real state (a table trimmed to nothing),
    // and saying so is cheaper than an empty frame reading as a broken screen.
    // A poster on its own is NOT that state: it is a wall with a print on it.
    const empty = el('p', 'arc-note arc-cork-empty', t('board_empty', 'Nothing pinned up tonight.'));
    hostEl.appendChild(empty);
    mounted.push(empty);
  }

  for (let i = 0; i < up.length; i += 1) {
    const notice = up[i];
    const geom = pinGeometry(seed, notice.id);
    const seenBefore = !!(s.notices[notice.id] && s.notices[notice.id].seenAt);

    /* The kind rides the SLOT as well as the sheet: the pin is the slot's
     * child now (see below), so the pin hue has to be reachable from here. */
    const slot = el('div', 'arc-cork-slot kind-' + notice.kind);
    attr(slot, 'role', 'listitem');
    styleVar(slot, '--rot', geom.rot.toFixed(2) + 'deg');
    styleVar(slot, '--lift', geom.lift.toFixed(1) + 'px');
    styleVar(slot, '--pin-x', geom.pinX.toFixed(1) + '%');

    /* `look` is one token or several ('pencil askew') - each gets its class. */
    const looks = String(notice.look || '').split(/\s+/).filter(Boolean)
      .map((w) => ' look-' + w).join('');
    const sheet = el('article', 'arc-corknote kind-' + notice.kind + looks
      + (geom.torn ? ' is-torn' : '')
      + (seenBefore ? ' is-read' : ' is-fresh'));

    const tag = el('span', 'arc-cork-kind', kindLabel(notice.kind).toUpperCase());
    sheet.appendChild(tag);

    sheet.appendChild(el('h2', 'arc-cork-notetitle', noticeTitle(notice)));
    /* A body is one paragraph or a list of them - the season's longer notices
     * (a menu, a set of minutes) breathe in paragraphs like a letter does. */
    const paras = Array.isArray(notice.body) ? notice.body : [notice.body];
    for (let p = 0; p < paras.length; p += 1) {
      sheet.appendChild(el('p', 'arc-cork-notebody', noticeParagraph(notice, p, paras[p])));
    }

    // A flyer is a flyer because somebody can take a tab off the bottom of it.
    if (notice.kind === 'flyer') {
      const tabs = el('div', 'arc-cork-tabs');
      attr(tabs, 'aria-hidden', 'true');
      for (let k = 0; k < 7; k += 1) tabs.appendChild(el('i', 'arc-cork-tab' + (k < 2 ? ' gone' : '')));
      sheet.appendChild(tabs);
    }

    /* THE READER'S DOOR. A transparent control the size of the paper, so the
     * sheet keeps being an <article> (a document, which is what it is) and the
     * press still lands on a real button with a real name. Never minted for a
     * preview - a miniature is scenery. */
    if (o.readable === true && !preview) {
      const open = el('button', 'arc-cork-open');
      open.type = 'button';
      attr(open, 'aria-label', noticeTitle(notice));
      open.setAttribute('title', t('board_note_open', 'Read this one'));
      open.addEventListener('click', function () { openNoticeReader(notice, { log: say }); });
      slot.appendChild(open);
    }

    slot.appendChild(sheet);
    sheets.push(sheet);
    /* THE PIN GOES THROUGH THE SLOT, NOT THROUGH THE SHEET. A torn sheet is a
     * clip-path, and clip-path clips its children too - a pin parented to the
     * paper came out as a half circle on every torn notice (caught in the
     * Chromium pass, not by the node run). */
    const pin = el('i', 'arc-cork-pin');
    attr(pin, 'aria-hidden', 'true');
    slot.appendChild(pin);
    hostEl.appendChild(slot);
    mounted.push(slot);
    if (!first) first = sheet;

    /* READING THE WALL IS OPENING IT. There is no per-sheet open verb - a
     * corkboard is read at a glance, and a click-to-expand on a paragraph of
     * copy would be a door with nothing behind it. So the visit marks every
     * sheet it actually pinned up, once. */
    if (!seenBefore && !preview) {
      s.notices[notice.id] = { seenAt: today };
      try { if (typeof o.onRead === 'function') o.onRead(notice.id); }
      catch (e) { say('corkboard onRead: ' + ((e && e.message) || e)); }
    }
  }

  function pinPoster(id) {
    const geom = pinGeometry(seed, id);
    const slot = el('div', 'arc-cork-slot kind-poster');
    attr(slot, 'role', 'listitem');
    styleVar(slot, '--rot', geom.rot.toFixed(2) + 'deg');
    styleVar(slot, '--lift', geom.lift.toFixed(1) + 'px');
    styleVar(slot, '--pin-x', geom.pinX.toFixed(1) + '%');

    const sheet = el('article', 'arc-corknote kind-poster is-poster');
    const img = el('img', 'cb-poster');
    /* DECORATION, AND IT SAYS SO. An empty alt plus aria-hidden is the whole
     * accessible story of a print: there is no text on this wall to lose. */
    attr(img, 'alt', '');
    attr(img, 'aria-hidden', 'true');
    attr(img, 'decoding', 'async');
    /* THE HANDLER GOES ON BEFORE THE SRC DOES. A cached 404 can fire `error`
     * inside the assignment, and a listener added after it would never hear the
     * one event it exists for. */
    try {
      img.addEventListener('error', function () {
        try { slot.remove(); } catch (e) { /* noop */ }
        const i = mounted.indexOf(slot);
        if (i >= 0) mounted.splice(i, 1);
        say('corkboard: no print behind ' + id + ' - the slot comes down');
      });
    } catch (e) { /* the double has listeners; a host that does not is no worse */ }
    try { img.src = posterUrl(id); } catch (e) { /* noop */ }
    sheet.appendChild(img);
    slot.appendChild(sheet);

    const pin = el('i', 'arc-cork-pin');
    attr(pin, 'aria-hidden', 'true');
    slot.appendChild(pin);
    hostEl.appendChild(slot);
    mounted.push(slot);
    return slot;
  }

  /* THE PRINT, PINNED LAST. It hangs in the seat the rotation gave up above.
   * It marks nothing read and banks no visit: there is nothing on it to read,
   * so a poster can never clear the prop's fresh dot for a notice the player
   * has not seen. */
  if (poster) pinPoster(poster);

  /* THE VISIT, BANKED. One write per mount, at the end, never per sheet - and
   * a PREVIEW is not a visit. Looking at the board from across the room does
   * not read the board: a miniature that banked the night would clear the
   * prop's fresh dot for a wall the player never walked up to. */
  if (!preview) {
    s.lastPinDay = seed;
    s.openedAt = today;
    s.visits = Math.max(0, Math.round(Number(s.visits) || 0)) + 1;
    persist(s, o.save);
  }

  /* THE FIT, AND THE THREE TIMES IT HAS TO RUN. The first measurement races
   * the LAZY stylesheet links (this file's and the room's): an unstyled sheet
   * measures at the browser's own defaults and would be handed type it never
   * needed. scene.js has the same race and answers it the same way - now, next
   * frame, and once more after the sheets have had time to land. Resizing the
   * window changes the stage scale, which changes the floor, which is a fourth
   * reason to run it. */
  const fitOpts = (o.fit && typeof o.fit === 'object') ? o.fit : null;
  let pending = false;

  /* THE WHOLE WALL GOES BACK UP BEFORE ANYTHING IS MEASURED. A hidden slot
   * measures zero, and a zero-height sheet is a sheet THE FIT thinks already
   * fits - so a fit run over a trimmed wall leaves every sheet below the cut
   * at full type, and the next trim then cuts the wall higher for that reason
   * alone. Two runs of that and the miniature is one row. Untrim, fit, trim,
   * in that order, every time. */
  function refit() {
    if (!fitOpts) return false;
    let ok = false;
    try { untrim(); } catch (e) { /* noop */ }
    try { ok = !!fitSheets(hostEl, sheets, fitOpts); }
    catch (e) { say('corkboard fit: ' + ((e && e.message) || e)); }
    try { trimWholeRows(); }
    catch (e) { say('corkboard trim: ' + ((e && e.message) || e)); }
    return ok;
  }

  /** Every slot back in the flow (see refit). */
  function untrim() {
    if (o.wholeRows !== true) return;
    for (let i = 0; i < mounted.length; i += 1) {
      const slot = mounted[i];
      if (!slot || !slot.style || typeof slot.style.removeProperty !== 'function') continue;
      try { slot.style.removeProperty('display'); } catch (e) { /* noop */ }
    }
  }

  /* WHOLE SHEETS ONLY (the miniature's rule - see opts.wholeRows). Measure the
   * whole standing wall first and only then take slots down: a grid re-places
   * what is left the moment one item leaves the flow, so a one-pass "measure,
   * hide, measure the next" walks the wall up under itself. Trailing items are
   * the only ones taken down, so the auto-placement above the cut cannot move. */
  function trimWholeRows() {
    if (!fitOpts || o.wholeRows !== true) return;
    const box = Number(hostEl.clientHeight) || 0;
    if (!(box > 0)) return;
    const seen = [];
    for (let i = 0; i < mounted.length; i += 1) {
      const slot = mounted[i];
      if (slot && slot.style && typeof slot.style.removeProperty === 'function') seen.push(slot);
    }
    let cut = -1;
    for (let i = 0; i < seen.length; i += 1) {
      const top = Number(seen[i].offsetTop) || 0;
      const h = Number(seen[i].offsetHeight) || 0;
      if (top + h > box + 1) { cut = i; break; }
    }
    if (cut < 0) return;
    for (let i = cut; i < seen.length; i += 1) seen[i].style.display = 'none';
  }

  function refitSoon() {
    if (pending) return;
    pending = true;
    const run = () => { pending = false; refit(); };
    if (typeof requestAnimationFrame === 'function') {
      try { requestAnimationFrame(run); return; } catch (e) { /* noop */ }
    }
    timers.push(setTimeout(run, 32));
  }

  let onResize = null;
  if (fitOpts) {
    refit();
    refitSoon();
    if (typeof setTimeout === 'function') timers.push(setTimeout(refit, 420));
    if (typeof window !== 'undefined' && typeof window.addEventListener === 'function') {
      onResize = refitSoon;
      window.addEventListener('resize', onResize);
    }
  }

  return {
    notices: up,
    daySeed: seed,
    /** Tonight's print, or null when the player does not own the drop. */
    poster: poster,
    first: first,
    /** The sheets, for a suite that wants to measure one. */
    sheets: sheets,
    /** Re-run THE FIT (the stage scale moved, or a sheet landed late). */
    refit: refit,
    /** The Esc fold's handle: true when a reader was up and is now down. */
    closeReader() { return closeNoticeReader(); },
    destroy() {
      if (!preview) closeNoticeReader();
      if (onResize && typeof window !== 'undefined' && typeof window.removeEventListener === 'function') {
        try { window.removeEventListener('resize', onResize); } catch (e) { /* noop */ }
        onResize = null;
      }
      for (let i = 0; i < timers.length; i += 1) { try { clearTimeout(timers[i]); } catch (e) { /* noop */ } }
      timers.length = 0;
      for (let i = 0; i < mounted.length; i += 1) {
        try { mounted[i].remove(); } catch (e) { /* noop */ }
      }
      mounted.length = 0;
      sheets.length = 0;
    },
  };
}

/* ----------------------------------------------------------------------------
 * THE READER - one sheet, off the wall, in your hands.
 *
 * The office's cork is a CLOSE-UP of a painting: the sheets are laid out in
 * stage pixels and scaled with the plate, so on a phone in landscape (the fit
 * is about a half) the body copy lands somewhere near seven pixels and the
 * bottom row hangs off the frame and out of the window. The wall is still the
 * wall - it is meant to be read at a glance - but a glance you cannot resolve
 * is a texture. So a sheet can be TAKEN DOWN: one press lifts a full-size,
 * scrollable copy over the window at type nobody has to squint at.
 *
 * The corkboard's own header said there is no per-sheet open verb, because a
 * click-to-expand on a paragraph of copy is a door with nothing behind it.
 * That was written for a wall you read at desk size. The owner's ruling
 * (2026-08-25, iPhone landscape) is that on paper this small the door has the
 * paper behind it, which is the whole of what it needs.
 *
 * IT HANGS OFF <body> AT z56. A room is `position:fixed` at z10 and its apron
 * band is a body-level sibling at z55 - a reader mounted inside the room would
 * be laid out inside the room and painted under the carpet. Above the band,
 * under the toasts (60), and it marks nothing read: the wall already did that
 * when it pinned the sheet up.
 * -------------------------------------------------------------------------- */

/** The one open reader, or null. Two sheets in one hand is a bug. */
let reader = null;

/**
 * Take one notice off the wall.
 * @param {Object} notice  a row of NOTICES (or the same shape)
 * @param {Object=} opts   {mount, log, onClose}
 * @returns {?Object} {root, close()} - null with no DOM
 */
export function openNoticeReader(notice, opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function' || !notice) return null;
  const mount = o.mount || doc.body;
  if (!mount || typeof mount.appendChild !== 'function') return null;

  // ONE SHEET. A second press is the first one replaced, never a second stage.
  closeNoticeReader();
  ensureStyles(doc);

  const root = el('div', 'arc-cork-reader');
  attr(root, 'role', 'dialog');
  attr(root, 'aria-modal', 'true');
  attr(root, 'aria-label', noticeTitle(notice));

  const veil = el('div', 'arc-cork-reader-veil');
  attr(veil, 'aria-hidden', 'true');
  veil.addEventListener('click', function () { closeNoticeReader(); });
  root.appendChild(veil);

  const looks = String(notice.look || '').split(/\s+/).filter(Boolean)
    .map(function (w) { return ' look-' + w; }).join('');
  const sheet = el('article', 'arc-corknote arc-cork-readnote kind-' + notice.kind + looks);
  sheet.appendChild(el('span', 'arc-cork-kind', kindLabel(notice.kind).toUpperCase()));
  sheet.appendChild(el('h2', 'arc-cork-notetitle', noticeTitle(notice)));
  const paras = Array.isArray(notice.body) ? notice.body : [notice.body];
  for (let p = 0; p < paras.length; p += 1) {
    sheet.appendChild(el('p', 'arc-cork-notebody', noticeParagraph(notice, p, paras[p])));
  }
  root.appendChild(sheet);

  const close = el('button', 'arc-cork-readclose', t('board_note_close', 'Put it back on the wall'));
  close.type = 'button';
  close.addEventListener('click', function () { closeNoticeReader(); });
  root.appendChild(close);

  /* One focusable thing in here, so the trap is one line - records.js's
   * spotlight shape. Escape is NOT bound: the shell owns the ladder and the
   * room's escapeStep asks closeReader() first (trap 48's order). */
  root.addEventListener('keydown', function (ev) {
    if (!ev || ev.key !== 'Tab') return;
    try { ev.preventDefault(); } catch (e) { /* noop */ }
    try { close.focus(); } catch (e) { /* noop */ }
  });

  mount.appendChild(root);
  reader = { root: root, onClose: typeof o.onClose === 'function' ? o.onClose : null };
  focusSoon(close);
  sfx('paper', 0.24);
  return { root: root, close: closeNoticeReader };
}

/** Put the sheet back. Returns true when one was in hand (the Esc rung). */
export function closeNoticeReader() {
  if (!reader) return false;
  const r = reader;
  reader = null;
  try { if (r.root && r.root.remove) r.root.remove(); } catch (e) { /* noop */ }
  sfx('paper', 0.18);
  try { if (r.onClose) r.onClose(); } catch (e) { /* noop */ }
  return true;
}

/** Is a sheet in hand right now? (test seam) */
export function readerUp() { return !!reader; }

/* ----------------------------------------------------------------------------
 * THE OVERLAY
 * -------------------------------------------------------------------------- */

let live = null;   // the one open board, or null. Two walls is a bug, not a feature.

/**
 * Open the noticeboard.
 *
 * @param {Object=} opts
 * @param {Object=} opts.mount          where to append (default document.body)
 * @param {string=} opts.daySeed        UTC day string; defaults to today
 * @param {Object=} opts.state          injected persistence (see STATE-NEEDS)
 * @param {Function=} opts.save         save(state) callback
 * @param {number=} opts.slots          how many sheets are up (default 4)
 * @param {boolean=} opts.bindEscape    self-bind Esc (default FALSE - the shell
 *                                      owns the ladder; this is for demos)
 * @param {Function=} opts.onClose      called once, after the stage is gone
 * @param {Function=} opts.onRead       onRead(noticeId) per sheet marked read
 * @returns {?Object} {root, close(), destroy(), notices} - null with no DOM
 */
export function openCorkboard(opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  const mount = o.mount || deps.mount || doc.body;
  if (!mount || typeof mount.appendChild !== 'function') return null;

  // ONE WALL. A second open is the first one raised, not a second stage.
  if (live && !live.closed) { focusSoon(live.firstButton); return live.handle; }

  ensureStyles(doc);

  const root = el('div', 'arc-corkstage');
  attr(root, 'role', 'dialog');
  attr(root, 'aria-modal', 'true');
  attr(root, 'aria-label', t('board_title', 'Noticeboard'));

  const board = el('div', 'arc-corkboard');
  const frame = el('div', 'arc-cork-frame');
  board.appendChild(frame);

  /* ------------------------------ the head ----------------------------- */
  const head = el('div', 'arc-cork-head');
  head.appendChild(el('p', 'arc-kicker', t('board_kicker', 'Pinned up')));
  head.appendChild(el('h1', 'arc-h1 arc-cork-title', t('board_title', 'Noticeboard')));
  head.appendChild(el('p', 'arc-lede arc-cork-lede', t('board_lede',
    'What is up on the wall tonight. Some of it stays. Most of it does not.')));
  frame.appendChild(head);

  /* ------------------------------ the wall ----------------------------- */
  /* THE PAPER IS mountNotices'S, NOT THIS FUNCTION'S. The office wall runs the
   * same call over the cork in its own painted set, so the night's set, the
   * seen rows and the banked visit are one piece of code with two stages. */
  const wall = el('div', 'arc-cork-wall');
  attr(wall, 'role', 'list');
  frame.appendChild(wall);

  const paper = mountNotices(wall, {
    daySeed: o.daySeed,
    state: o.state,
    save: o.save,
    slots: o.slots,
    onRead: o.onRead,
    posters: o.posters,
    log: o.log,
  });
  const seed = paper ? paper.daySeed
    : String(o.daySeed != null ? o.daySeed : (deps.daySeed || utcDaySeed()));
  const up = paper ? paper.notices : [];

  /* THE SLOW CLOCK, SAID OUT LOUD. A board that changes silently reads as a
   * board that forgot what was on it. */
  frame.appendChild(el('p', 'arc-note arc-cork-foot', t('board_rotates',
    'The wall gets sorted through most days. What is pinned flat stays put.')));

  /* ------------------------------ the way out --------------------------- */
  let closed = false;
  let escBound = false;

  function onKey(e) {
    if (!e) return;
    if (e.key !== 'Escape' && e.key !== 'Esc') return;
    try { e.preventDefault(); e.stopPropagation(); } catch (err) { /* noop */ }
    close();
  }

  function close() {
    if (closed) return;
    closed = true;
    if (escBound) {
      try { doc.removeEventListener('keydown', onKey, true); } catch (e) { /* noop */ }
      escBound = false;
    }
    try { root.remove(); } catch (e) { /* noop */ }
    if (live && live.handle === handle) live = null;
    sfx('paper', 0.18, { pitch: 0.92 });
    try { if (typeof o.onClose === 'function') o.onClose(); }
    catch (e) { if (deps.log) { try { deps.log('corkboard onClose: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
  }

  const back = el('button', 'btn primary arc-cork-back', t('back', 'Back'));
  back.type = 'button';
  back.addEventListener('click', close);
  signExit(back, { dir: 'back' });
  frame.appendChild(exitBar([back]));

  /* THE STAGE IS A DOOR TOO. Clicking the dusk outside the board closes it -
   * the board is read-only, so a stray press costs the player nothing. The
   * test is on the target being the stage ITSELF, so a press that lands on a
   * sheet and drags off it never counts. */
  root.addEventListener('click', (e) => {
    if (e && e.target === root) close();
  });

  root.appendChild(board);
  mount.appendChild(root);

  if (o.bindEscape && typeof doc.addEventListener === 'function') {
    doc.addEventListener('keydown', onKey, true);
    escBound = true;
  }

  /* The visit is banked by mountNotices, at the end of the pinning, exactly
   * where it was banked when this function did the pinning itself. */
  sfx('paper', 0.25);
  focusSoon(back);

  const handle = {
    root: root,
    notices: up,
    daySeed: seed,
    close: close,
    destroy: close,
    get closed() { return closed; },
  };
  live = { handle: handle, firstButton: back, get closed() { return closed; } };
  return handle;
}

/** The open board, or null. Test seam and the driver's re-entry guard. */
export function currentCorkboard() {
  return (live && !live.closed) ? live.handle : null;
}

/* ----------------------------------------------------------------------------
 * THE PROP
 * A small board hung on a wall somewhere on the campus. It is a BUTTON, it says
 * what it is, and it wears one quiet marker when tonight's wall holds a sheet
 * this player has never had up. No pulse, no badge count, no bell: the campus
 * is already a busy picture and a noticeboard that shouts is not a noticeboard.
 *
 * POSITIONING IS THE DRIVER'S. The prop paints at `--bp-x` / `--bp-y` (percent
 * of the parent, absolute), which the driver overrides on the returned element
 * or from campus CSS. It defaults to somewhere sane rather than to 0,0 so a
 * mount with no placement is still visible on a screenshot.
 * -------------------------------------------------------------------------- */

/**
 * @param {Object} parentEl
 * @param {Object=} opts
 * @param {Function=} opts.onOpen   called on click (the driver calls openCorkboard)
 * @param {string=} opts.daySeed
 * @param {Object=} opts.state
 * @param {string=} opts.label
 * @returns {?Object} {el, root, refresh(), destroy()}
 */
export function mountBoardProp(parentEl, opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  if (!parentEl || typeof parentEl.appendChild !== 'function') return null;

  ensureStyles(doc);

  const btn = el('button', 'arc-boardprop');
  btn.type = 'button';
  const label = o.label || t('board_prop_label', 'Noticeboard');
  attr(btn, 'aria-label', label);
  attr(btn, 'title', label);

  const cork = el('span', 'arc-boardprop-cork');
  attr(cork, 'aria-hidden', 'true');
  // Three slips of paper, fixed angles: the prop is scenery, and scenery that
  // re-arranges itself every repaint reads as a glitch.
  const slips = [[-4.5, 8, 12], [2.8, 46, 20], [-1.6, 70, 9]];
  for (let i = 0; i < slips.length; i += 1) {
    const slip = el('i', 'arc-boardprop-slip');
    styleVar(slip, '--r', slips[i][0] + 'deg');
    styleVar(slip, '--l', slips[i][1] + '%');
    styleVar(slip, '--tp', slips[i][2] + '%');
    cork.appendChild(slip);
  }
  btn.appendChild(cork);

  const cap = el('span', 'arc-boardprop-label', label.toUpperCase());
  btn.appendChild(cap);

  const dot = el('i', 'arc-boardprop-new');
  attr(dot, 'aria-hidden', 'true');
  btn.appendChild(dot);

  function refresh(daySeed) {
    const seed = daySeed != null ? String(daySeed)
      : (o.daySeed != null ? String(o.daySeed) : (deps.daySeed || utcDaySeed()));
    let fresh = false;
    try { fresh = hasUnread(seed, o.state); } catch (e) { fresh = false; }
    try { btn.classList.toggle('has-new', !!fresh); } catch (e) { /* noop */ }
    return fresh;
  }

  btn.addEventListener('click', () => {
    sfx('flap', 0.2);
    try { if (typeof o.onOpen === 'function') o.onOpen(); }
    catch (e) { if (deps.log) { try { deps.log('board prop onOpen: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
    refresh();
  });

  refresh();
  parentEl.appendChild(btn);

  return {
    el: btn,
    root: btn,
    refresh: refresh,
    destroy() { try { btn.remove(); } catch (e) { /* noop */ } },
  };
}

export default openCorkboard;
