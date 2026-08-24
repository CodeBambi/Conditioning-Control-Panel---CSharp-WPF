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

export const NOTICE_KINDS = Object.freeze(['notice', 'flyer', 'minutes']);

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
]);

/** How many sheets the wall holds at once. Pinned rows always make the cut. */
export const BOARD_SLOTS = 4;

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

function kindLabel(kind) {
  if (kind === 'flyer') return t('board_kind_flyer', 'Flyer');
  if (kind === 'minutes') return t('board_kind_minutes', 'Minutes');
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
};

/**
 * Hand the module its injected persistence and defaults. Every field optional.
 * `when` is the season gate evaluator: `(trigger) => boolean`, the shell's one
 * bridge between this wall and the rest of the story (mail.js's triggerHolds
 * over the shared context).
 * @param {{state?:Object, save?:Function, daySeed?:string, mount?:Object, log?:Function, when?:Function}} opts
 */
export function initCorkboard(opts) {
  const o = opts || {};
  if (o.state && typeof o.state === 'object') deps.state = o.state;
  if (typeof o.save === 'function') deps.save = o.save;
  if (o.daySeed != null) deps.daySeed = String(o.daySeed);
  if (o.mount) deps.mount = o.mount;
  if (typeof o.log === 'function') deps.log = o.log;
  if (typeof o.when === 'function') deps.when = o.when;
  return deps.state;
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

  const seed = String(o.daySeed != null ? o.daySeed : (deps.daySeed || utcDaySeed()));
  const s = stateOf(o.state);
  const up = pickNotices(seed, o.slots, eligibleNotices());
  const today = localDay();

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
  const wall = el('div', 'arc-cork-wall');
  attr(wall, 'role', 'list');
  frame.appendChild(wall);

  if (!up.length) {
    // A wall with nothing on it is a real state (a table trimmed to nothing),
    // and saying so is cheaper than an empty frame reading as a broken screen.
    wall.appendChild(el('p', 'arc-note arc-cork-empty', t('board_empty',
      'Nothing pinned up tonight.')));
  }

  let firstButton = null;

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

    sheet.appendChild(el('h2', 'arc-cork-notetitle', String(notice.title || '')));
    /* A body is one paragraph or a list of them - the season's longer notices
     * (a menu, a set of minutes) breathe in paragraphs like a letter does. */
    const paras = Array.isArray(notice.body) ? notice.body : [notice.body];
    for (let p = 0; p < paras.length; p += 1) {
      sheet.appendChild(el('p', 'arc-cork-notebody', String(paras[p] || '')));
    }

    // A flyer is a flyer because somebody can take a tab off the bottom of it.
    if (notice.kind === 'flyer') {
      const tabs = el('div', 'arc-cork-tabs');
      attr(tabs, 'aria-hidden', 'true');
      for (let k = 0; k < 7; k += 1) tabs.appendChild(el('i', 'arc-cork-tab' + (k < 2 ? ' gone' : '')));
      sheet.appendChild(tabs);
    }

    slot.appendChild(sheet);
    /* THE PIN GOES THROUGH THE SLOT, NOT THROUGH THE SHEET. A torn sheet is a
     * clip-path, and clip-path clips its children too - a pin parented to the
     * paper came out as a half circle on every torn notice (caught in the
     * Chromium pass, not by the node run). */
    const pin = el('i', 'arc-cork-pin');
    attr(pin, 'aria-hidden', 'true');
    slot.appendChild(pin);
    wall.appendChild(slot);
    if (!firstButton) firstButton = sheet;

    /* READING THE WALL IS OPENING IT. There is no per-sheet open verb - a
     * corkboard is read at a glance, and a click-to-expand on a paragraph of
     * copy would be a door with nothing behind it. So the visit marks every
     * sheet it actually pinned up, once. */
    if (!seenBefore) {
      s.notices[notice.id] = { seenAt: today };
      try { if (typeof o.onRead === 'function') o.onRead(notice.id); }
      catch (e) { if (deps.log) { try { deps.log('corkboard onRead: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
    }
  }

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

  /* THE VISIT, BANKED. One write per open, at the end, never per sheet. */
  s.lastPinDay = seed;
  s.openedAt = today;
  s.visits = Math.max(0, Math.round(Number(s.visits) || 0)) + 1;
  persist(s, o.save);

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
