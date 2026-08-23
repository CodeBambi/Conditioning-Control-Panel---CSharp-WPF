/* ============================================================================
 * shell/campus.js - the night-campus planimetry hub (Direction A).
 *
 * The home screen: a blueprint floor plan of the school at night. Rooms ARE the
 * games (fixed geography - a game always lives in its room), facilities are
 * diegetic (Registrar = settings, Records = report card), tonight's classes
 * glow pink with numbered route stops, the locked wings are Semesters II/III.
 * Ported from planning/arcademy/mockups/arcademy-campus-hub.html, wired to real
 * timetable/grade/streak state instead of placeholder data.
 *
 * CONTRACT WITH THE SHELL
 *   - This module renders CHROME ONLY. It never starts a class, never touches
 *     the bridge, never looks at the store: the shell hands it a computed state
 *     and three handlers (onBegin/onRecords/onRegistrar) and repaints it with
 *     update(). campusState() below is the ONE state mapper, exported pure so
 *     the suite can assert LED/stop/retake logic without a DOM.
 *   - The shell treats createCampus() as OPTIONAL (try/caught): a campus that
 *     throws costs the player the scenery, never the school. Everything here is
 *     therefore defensive about missing platform pieces (getBoundingClientRect,
 *     createElementNS) so the DOM double can build it too.
 *   - Every visible string routes through the lexicon (t) - room names, wing
 *     labels, statuses, the lot. Mods re-voice the campus without touching it.
 *   - All colour comes from styles.css classes off the shell tokens; nothing
 *     here writes a hex. init.palette therefore reskins the campus for free.
 *
 * XP NUMBERS ARE C#'s. The door card never shows an XP figure - only the two
 * law statements (first pass pays / retakes pay nothing). A page-side XP table
 * is the one thing the build contract forbids twice.
 * ==========================================================================*/

import { t, tierLabel, familyLabel } from '../core/lexicon.js';
import { makeRng } from '../core/rng.js';
import { OPEN_SEMESTERS, isOpenSemester } from '../games/registry.js';
import { fireMoment } from '../emi/moments.js';

const SVGNS = 'http://www.w3.org/2000/svg';

/* ----------------------------------------------------------------------------
 * FIXED GEOGRAPHY - viewBox 0 0 1440 920, corridor y 430..510.
 * A game always lives in its room; a new semester adds rooms, it never moves
 * one. Coordinates are the mockup's, verbatim.
 * -------------------------------------------------------------------------- */
export const ROOMS = Object.freeze({
  daily_trigger: {
    rect: [220, 210, 220, 220], side: 'n', door: 330, rm: '101',
    nameKey: 'campus_room_daily_trigger', nameEn: 'Homeroom',
    descKey: 'campus_desc_daily_trigger',
    descEn: 'One word, six chances. The whole school sits the same word today.',
  },
  deja_vu: {
    rect: [460, 210, 220, 220], side: 'n', door: 570, rm: '102',
    nameKey: 'campus_room_deja_vu', nameEn: 'Memory Lab',
    descKey: 'campus_desc_deja_vu',
    descEn: 'Pairs that move when you blink. The board settles only when you stop looking.',
  },
  impulse_control: {
    rect: [700, 210, 220, 220], side: 'n', door: 810, rm: '103',
    nameKey: 'campus_room_impulse_control', nameEn: 'Discipline Hall',
    descKey: 'campus_desc_impulse_control',
    descEn: 'Hands on the desk. Move only when told - the room will lie to you.',
  },
  lost_and_found: {
    rect: [220, 510, 220, 220], side: 's', door: 330, rm: '104',
    nameKey: 'campus_room_lost_and_found', nameEn: 'Lost & Found',
    descKey: 'campus_desc_lost_and_found',
    descEn: 'Things went missing in a wall of moving pictures. Find them before they move again.',
  },
  /* THE POOL - the natatorium on the south lawn, and the one DETACHED class.
   * The corridor's south wall is full (104 | Entrance Hall | Registrar), so the
   * Deep End's door is the 20px alley between 104 and the hall and a covered walk
   * runs down it to the water: `door` is still a corridor x, so doorFor(),
   * stopAnchor() and routeFor() need no change at all - the numbered stop lands in
   * the Main Hall like every other class. Because the building sits off the
   * corridor, `neonY` / `nameY` pin the sign and the label inside it instead of
   * taking the side-derived defaults (which assume a room bolted to the hall). */
  the_deep_end: {
    rect: [302, 738, 336, 114], side: 's', door: 450, rm: '105',
    neonY: 742, nameY: 822,
    nameKey: 'campus_room_the_deep_end', nameEn: 'The Pool',
    descKey: 'campus_desc_the_deep_end',
    descEn: 'Sink tile into tile. The deeper you go, the harder the board is to read.',
  },
  /* ---- EAST WING - Semester II ---------------------------------------------
   * The wing hangs off the corridor's east end and its 20px alley (x 1240..1260)
   * is the Pool's covered walk stood on its side: three rooms open on their WEST
   * wall onto it. Three fields carry the difference, and nothing else moves:
   *   `door` is the coordinate ALONG the wall - an x for a corridor room
   *          (side n/s), a y for a wing room (side w/e);
   *   `stop` pins the numbered badge in the wing alley, because the badge can no
   *          longer stand in the Main Hall outside the door;
   *   `via`  is the junction the route turns at - the polyline walks in off the
   *          hall, touches the stop and walks back out.
   * `neonX`/`neonY`/`nameY` pin the sign and the label INSIDE the room exactly
   * the way the Pool pins its own (a wing room is small, so the label stack runs
   * sign -> name -> room number instead of hanging off a corridor wall).
   * THE SAFE BAND: the plan is `preserveAspectRatio slice`, so a window TALLER
   * than 16:9 crops the LEFT AND RIGHT edges - 72 viewBox units a side at 16:10.
   * The wings sit at those edges, so every wing room and every wing label is
   * kept inside x 72..1368. Widen one and it clips on somebody's monitor. */
  misdirection: {
    rect: [1260, 358, 112, 66], side: 'w', door: 391, rm: '201', wing: 'east',
    stop: [1250, 391], via: [1250, 470], neonX: 1316, neonY: 364, nameY: 398,
    gameEn: 'Misdirection',
    nameKey: 'campus_room_misdirection', nameEn: 'The Parlour',
    descKey: 'campus_desc_misdirection',
    descEn: 'Keep your eyes on the one that matters. It will not make that easy.',
  },
  echo: {
    rect: [1260, 430, 112, 66], side: 'w', door: 463, rm: '202', wing: 'east',
    stop: [1250, 463], via: [1250, 470], neonX: 1316, neonY: 436, nameY: 470,
    gameEn: 'Echo',
    nameKey: 'campus_room_echo', nameEn: 'Music Room',
    descKey: 'campus_desc_echo',
    descEn: 'It plays a line, you play it back. Then it adds one more, every time.',
  },
  instant_recall: {
    rect: [1260, 502, 112, 66], side: 'w', door: 535, rm: '203', wing: 'east',
    stop: [1250, 535], via: [1250, 470], neonX: 1316, neonY: 508, nameY: 542,
    gameEn: 'Instant Recall',
    nameKey: 'campus_room_instant_recall', nameEn: 'Lecture Hall',
    descKey: 'campus_desc_instant_recall',
    descEn: 'Watch the whole hour, then answer for it. You never hear it coming.',
  },
  /* ---- WEST WING - Semester III --------------------------------------------
   * Same construction, mirrored: the alley is x 180..200 and the two rooms open
   * on their EAST wall. Fewer, larger rooms - the slow end of the school. */
  anomaly: {
    rect: [60, 366, 120, 96], side: 'e', door: 414, rm: '301', wing: 'west',
    stop: [190, 414], via: [190, 470], neonX: 120, neonY: 378, nameY: 424,
    gameEn: 'Anomaly',
    nameKey: 'campus_room_anomaly', nameEn: 'Darkroom',
    descKey: 'campus_desc_anomaly',
    descEn: 'Everything in here matches. One thing does not. Find it before it moves.',
  },
  composure: {
    rect: [60, 478, 120, 96], side: 'e', door: 526, rm: '302', wing: 'west',
    stop: [190, 526], via: [190, 470], neonX: 120, neonY: 490, nameY: 536,
    gameEn: 'Composure',
    nameKey: 'campus_room_composure', nameEn: 'The Studio',
    descKey: 'campus_desc_composure',
    descEn: 'Slide the picture back together while the room does its best to blur it.',
  },
});

/* ----------------------------------------------------------------------------
 * THE WINGS. A wing is a BLOCK, not a room: it owns a footprint, an alley and a
 * mouth onto the Main Hall, and it holds the rooms whose `wing` names it. The
 * tape comes off exactly when the wing's semester is in the registry's
 * OPEN_SEMESTERS - one set, one truth, so the release gate that keeps a class
 * out of the pool is the same one that keeps its wing sealed.
 * -------------------------------------------------------------------------- */
export const WINGS = Object.freeze({
  east: {
    semester: 2, roman: 'II', rect: [1240, 350, 160, 240],
    alleyX: 1250, mouthX: 1240, labelX: 1316, labelY: 612, sealedTone: 'pink', din: 180,
    nameKey: 'campus_east_wing', nameEn: 'East Wing',
    sealedKey: 'campus_opens_semester_2', sealedEn: 'Opens Semester II',
    sealedDescKey: 'campus_desc_east', sealedDescEn: 'You can hear hammering behind the tape.',
    openDescKey: 'campus_desc_east_open',
    openDescEn: 'The tape is down. Wet paint, three new doors, nobody at the desk.',
  },
  west: {
    semester: 3, roman: 'III', rect: [40, 350, 160, 240],
    alleyX: 190, mouthX: 200, labelX: 120, labelY: 612, sealedTone: 'dim', din: 210,
    nameKey: 'campus_west_wing', nameEn: 'West Wing',
    sealedKey: 'campus_semester_3', sealedEn: 'Semester III',
    sealedDescKey: 'campus_desc_west', sealedDescEn: 'The boards are older here.',
    openDescKey: 'campus_desc_west_open',
    openDescEn: 'Older boards, deeper rooms. Nobody in here is in any hurry.',
  },
});

/** The room keys that live in a wing, in floor order. Pure. */
export function wingRoomKeys(wingId) {
  return Object.keys(ROOMS).filter((k) => ROOMS[k].wing === wingId);
}

/** True when a wing's semester has opened (the tape comes off). Pure. */
export function wingIsOpen(wingId) {
  const w = WINGS[wingId];
  return !!w && OPEN_SEMESTERS.has(w.semester);
}

/** Highest open semester - what the crest and the student ID call this term. */
export function currentSemester() {
  let n = 1;
  try { OPEN_SEMESTERS.forEach((v) => { if (v > n) n = v; }); } catch (e) { /* noop */ }
  return Math.max(1, Math.min(4, n));
}

const ROMAN = Object.freeze(['', 'I', 'II', 'III', 'IV']);

/* ----------------------------------------------------------------------------
 * THE IDLE ATTRACT - tunables (Deck VI: demo, don't explain).
 * -------------------------------------------------------------------------- */
export const ATTRACT_IDLE_MS = 25000;   // silence before the school starts showing off
const ATTRACT_LEG_MS = 900;             // one ghost-cursor leg
const ATTRACT_DWELL_MS = 1000;          // how long a room holds its glow
const ATTRACT_LOOP_GAP_MS = 2600;       // dark beat before the show repeats
const ATTRACT_TICK_MS = 50;             // cursor lerp tick (20fps - a hint, not a game)
const ATTRACT_FLIP_MS = 70;             // one split-flap flip
const ATTRACT_FLIPS = 8;                // flips before a sign has fully settled
const ATTRACT_GLYPHS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
const ATTRACT_GLOW = 'brightness(1.3) saturate(1.12)';
const ATTRACT_CURSOR_ALPHA = '0.55';
/* The cursor sprite, drawn (never an emoji, never an image): 12x18 at the origin. */
const ATTRACT_CURSOR_D = 'M0,0 L0,17 L4.2,13 L6.9,18.4 L9.6,17.1 L7,11.8 L12,11.6 Z';

/* ----------------------------------------------------------------------------
 * PURE STATE MAPPER - what tonight looks like, per room.
 * -------------------------------------------------------------------------- */
/**
 * @param {Object} o
 * @param {Array}  o.classes   timetable.classes ({gameKey, timeLabel, ...})
 * @param {Object} o.records   gameKey -> {grade} | null  (today's rows)
 * @param {boolean=} o.suspended  global suspend (mandatory video etc.)
 * @param {boolean=} o.devPass   the host opened the DEV DOOR (`--arcademy`):
 *   every room's door card offers Begin even off tonight's board, so an
 *   unreleased class can be play-tested without waiting for the seed to deal
 *   it. Graded like any class; never set on a player build.
 * @param {Object=} o.endless  gameKey -> {labelKey, hintKey} for every game that
 *   declares `manifest.endless`. The shell computes it (the campus never reads a
 *   manifest); a room without an entry simply shows no Free Swim button.
 * @param {Object=} o.unlocked  gameKey -> true for every room whose PUNCH CARD is
 *   full (PUNCHCARD.md §2.3). An unlocked room offers Begin every night, board or
 *   no board - the one door of the three a player can actually earn, and the
 *   reason the CTA order is `scheduled -> unlocked -> devPass -> not tonight`.
 *   Like `endless` it is a property of the ROOM, not of tonight, so it rides
 *   every room in the map rather than only the dealt ones.
 * @returns {{rooms:Object, stops:Array<{gameKey:string,n:number}>, allDone:boolean}}
 *   rooms[gameKey] = { scheduled, period (1-based, 0 = not tonight), done,
 *                      grade, mood:'open'|'retake'|'dark', timeLabel, endless,
 *                      unlocked }
 */
export function campusState({ classes, records, suspended, endless, devPass, unlocked } = {}) {
  const list = Array.isArray(classes) ? classes : [];
  const recs = records || {};
  const ends = (endless && typeof endless === 'object') ? endless : {};
  const locks = (unlocked && typeof unlocked === 'object') ? unlocked : {};
  const rooms = Object.create(null);
  for (const key of Object.keys(ROOMS)) {
    rooms[key] = {
      scheduled: false, period: 0, done: false, grade: '', mood: 'dark', timeLabel: '',
      // ENDLESS IS A PROPERTY OF THE ROOM, NOT OF TONIGHT. It rides the state
      // for every room so a class that is not on the board (or is already done)
      // still offers its free swim.
      endless: (ends[key] && typeof ends[key] === 'object') ? ends[key] : null,
      // A FULL CARD IS A PERMANENT DOOR. Same reasoning as `endless` above: it
      // belongs to the room forever, not to tonight's deal.
      unlocked: locks[key] === true,
    };
  }
  // AN UNLOCKED ROOM IS NEVER DARK. It is not "in session" (that is what the
  // board deals) but it is open, and a map that painted it the same black as a
  // room you cannot enter would be lying about the thing you spent ten nights
  // earning. The dealt rooms below still overwrite this with their own mood.
  for (const key of Object.keys(rooms)) if (rooms[key].unlocked) rooms[key].mood = 'open';
  const stops = [];
  let done = 0;
  list.forEach((c, i) => {
    const r = rooms[c.gameKey];
    if (!r) return;                    // a future pool game with no room yet
    const rec = recs[c.gameKey];
    r.scheduled = true;
    r.period = i + 1;
    r.timeLabel = String(c.timeLabel || '');
    r.done = !!(rec && rec.grade);
    r.grade = r.done ? String(rec.grade) : '';
    r.mood = r.done ? 'retake' : 'open';
    if (r.done) done += 1;
    stops.push({ gameKey: c.gameKey, n: i + 1 });
  });
  return {
    rooms,
    stops,
    suspended: !!suspended,
    devPass: !!devPass,
    allDone: list.length > 0 && done === list.length,
  };
}

/** Seconds until the next local midnight - the next bell. Pure. */
export function bellSecondsLeft(now) {
  const d = now instanceof Date ? now : new Date();
  const next = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1, 0, 0, 0, 0);
  return Math.max(0, Math.round((next.getTime() - d.getTime()) / 1000));
}

/** 'HH:MM:SS' for the bell chip. Pure. */
export function bellLabel(totalSec) {
  const s = Math.max(0, Math.round(Number(totalSec) || 0));
  const p = (n) => String(n).padStart(2, '0');
  return p(Math.floor(s / 3600)) + ':' + p(Math.floor((s % 3600) / 60)) + ':' + p(s % 60);
}

/* ----------------------------------------------------------------------------
 * TINY BUILDERS - namespace-aware so the DOM double can host the campus.
 * -------------------------------------------------------------------------- */
function svg(tag, attrs, cls) {
  const n = (document.createElementNS
    ? document.createElementNS(SVGNS, tag)
    : document.createElement(tag));
  if (cls) n.setAttribute('class', cls);
  if (attrs) for (const k of Object.keys(attrs)) n.setAttribute(k, String(attrs[k]));
  return n;
}
function svgText(x, y, cls, text, extra) {
  const n = svg('text', Object.assign({ x, y }, extra || {}), cls);
  n.textContent = text == null ? '' : String(text);
  return n;
}
function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/* ============================================================================
 * THE CAMPUS
 * ==========================================================================*/
/**
 * @param {Object} o
 * @param {Object} o.state        campusState() output
 * @param {Function} o.gameName   gameKey -> resolved display name
 * @param {string=} o.banner      timetable banner (notice-board card copy)
 * @param {Object} o.stats        {streak, perfectDays, tier}
 * @param {boolean=} o.reducedMotion
 * @param {Object} o.on           {begin(gameKey), freeSwim(gameKey), records(),
 *   registrar()} - freeSwim is only ever called for a room whose state carries
 *   an `endless` declaration (the shell reads the manifest, never the campus).
 * @param {string=} o.dateSeed  init.utcDateSeed - the idle attract is seeded off
 *   it so every player gets the SAME show tonight (trap 8: UTC seeds content).
 *   Omitted, it falls back to today's UTC date.
 * @param {number=} o.attractIdleMs  test seam; defaults to ATTRACT_IDLE_MS.
 * @param {Function=} o.log
 * @returns {{root, boardMount, footMount, update, closeCard, destroy}}
 */
export function createCampus({ state, gameName, banner, stats, reducedMotion, on, log,
  dateSeed, attractIdleMs } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const handlers = on || {};
  const name = typeof gameName === 'function' ? gameName : (k) => String(k);
  let st = state || campusState({ classes: [], records: {} });
  let cardOpen = false;
  let destroyed = false;

  const root = el('div', 'campus-stage enter');
  root.appendChild(el('div', 'campus-stars'));
  root.appendChild(el('div', 'campus-fog a'));
  root.appendChild(el('div', 'campus-fog b'));

  /* THE WINDOW IS THE CAMPUS. The stage is position:fixed and bleeds edge to
   * edge; this class lets the stylesheet freeze body scroll while it is up.
   * The shell hides the topbar for the same reason - every piece of chrome is
   * a diegetic object inside the scene. */
  if (document.documentElement && document.documentElement.classList) {
    document.documentElement.classList.add('arc-campus-on');
  }

  /* The entry reveal is one-shot: rooms stagger in, the route fades, stops pop,
   * the board deals. After it has played, silent repaints must not re-run it
   * (the same law as splitflap's animate:false). */
  let enterTimer = 0;
  if (typeof setTimeout === 'function') {
    enterTimer = setTimeout(() => {
      enterTimer = 0;
      try { root.classList.remove('enter'); } catch (e) { /* noop */ }
    }, 4500);
  }

  /** Entry-stagger delay, per element (consumed by .campus-stage.enter rules). */
  function stag(node, ms) {
    try { node.style.setProperty('--din', ms + 'ms'); } catch (e) { /* noop */ }
    return node;
  }

  /* refs patched by update() */
  const roomRefs = Object.create(null);   // gameKey -> {g, neonText, neonRect, ping}
  let stopsLayer = null;
  let routePath = null;
  let idStreak = null;
  let idPerfect = null;
  let idTier = null;
  let bellText = null;

  /* ------------------------------ SVG plan ------------------------------- */
  /* The viewBox is cut to 16:9 (1440x810) around the architecture and SLICES:
   * the plan always fills the frame, whatever the window - dead sky is cropped
   * instead of letterboxed. Content near the top/bottom edges is croppable
   * flavor only (tower cap, quad), never a room or a control. */
  const plan = svg('svg', { viewBox: '0 55 1440 810', preserveAspectRatio: 'xMidYMid slice' }, 'campus-plan');
  plan.setAttribute('aria-label', t('arcademy', 'The Arcademy'));

  /* corridor paving texture (vector, styled from the stylesheet) */
  const defs = svg('defs');
  const pave = svg('pattern', {
    id: 'campusPave', width: 26, height: 26, patternUnits: 'userSpaceOnUse',
  });
  pave.appendChild(svg('line', { x1: 0, y1: 0, x2: 0, y2: 26 }, 'campus-paveline'));
  pave.appendChild(svg('line', { x1: 0, y1: 0, x2: 26, y2: 0 }, 'campus-paveline'));
  defs.appendChild(pave);
  plan.appendChild(defs);

  /* grounds: trees, paths, lamps, fountain */
  const grounds = svg('g', null, 'campus-grounds');
  [[695, 792], [745, 792]].forEach(([x, y]) => grounds.appendChild(svg('line', { x1: x, y1: y, x2: x, y2: 920 }, 'campus-path-edge')));
  grounds.appendChild(svg('line', { x1: 720, y1: 800, x2: 720, y2: 920, 'stroke-dasharray': '2 10' }, 'campus-path-mid'));
  const trees = svg('g', null, 'campus-trees');
  [[86, 756, 26], [112, 774, 19], [70, 778, 16], [146, 846, 22], [1368, 688, 24], [1394, 712, 15],
   [1322, 856, 26], [1352, 878, 17], [262, 864, 20], [56, 628, 17]]
    .forEach(([cx, cy, r]) => trees.appendChild(svg('circle', { cx, cy, r })));
  grounds.appendChild(trees);
  [[662, 842, 17], [778, 842, 17], [1180, 782, 15]].forEach(([cx, cy, r]) => {
    grounds.appendChild(svg('circle', { cx, cy, r }, 'campus-lamphalo'));
    grounds.appendChild(svg('circle', { cx, cy, r: r > 15 ? 3.4 : 3 }, 'campus-lamp'));
  });
  grounds.appendChild(svg('circle', { cx: 1088, cy: 846, r: 26 }, 'campus-fountain'));
  grounds.appendChild(svg('circle', { cx: 1088, cy: 846, r: 9 }, 'campus-fountain-eye'));
  grounds.appendChild(svg('circle', { cx: 1088, cy: 846, r: 16 }, 'campus-ripple'));
  grounds.appendChild(svg('circle', { cx: 1088, cy: 846, r: 16 }, 'campus-ripple r2'));
  grounds.appendChild(svgText(1088, 806, 'campus-groundlbl', t('campus_the_quad', 'The Quad').toUpperCase()));
  grounds.appendChild(svgText(756, 858, 'campus-groundlbl start', t('campus_front_path', 'Front Path').toUpperCase(),
    { 'text-anchor': 'start' }));
  plan.appendChild(stag(grounds, 0));

  /* bell tower + clock */
  const tower = svg('g', null, 'campus-tower');
  tower.appendChild(svg('line', { x1: 1310, y1: 200, x2: 1310, y2: 346, 'stroke-dasharray': '2 7' }, 'campus-tower-tie'));
  tower.appendChild(svg('circle', { cx: 1310, cy: 156, r: 42 }, 'campus-gfloor'));
  tower.appendChild(svg('circle', { cx: 1310, cy: 156, r: 33 }, 'campus-dial'));
  [[1310, 126, 1310, 131], [1310, 181, 1310, 186], [1280, 156, 1285, 156], [1335, 156, 1340, 156]]
    .forEach(([x1, y1, x2, y2]) => tower.appendChild(svg('line', { x1, y1, x2, y2 }, 'campus-tick')));
  tower.appendChild(svg('line', { x1: 1310, y1: 156, x2: 1310, y2: 139 }, 'campus-clockhand hh'));
  tower.appendChild(svg('line', { x1: 1310, y1: 156, x2: 1310, y2: 131 }, 'campus-clockhand mh'));
  tower.appendChild(svg('circle', { cx: 1310, cy: 156, r: 2.6 }, 'campus-clockpin'));
  tower.appendChild(svgText(1310, 236, 'campus-rsub', t('campus_bell_tower', 'Bell Tower').toUpperCase()));
  plan.appendChild(stag(tower, 120));

  /* corridor + entrance hall floors (+ paving texture overlays) */
  const floors = svg('g', null, 'campus-floors');
  floors.appendChild(svg('rect', { x: 200, y: 430, width: 1040, height: 80 }, 'campus-ghall'));
  floors.appendChild(svg('rect', { x: 200, y: 430, width: 1040, height: 80, fill: 'url(#campusPave)' }, 'campus-pave'));
  floors.appendChild(svgText(252, 474, 'campus-rsub start wide', t('campus_main_hall', 'Main Hall').toUpperCase(), { 'text-anchor': 'start' }));
  floors.appendChild(svg('rect', { x: 460, y: 510, width: 480, height: 220 }, 'campus-ghall'));
  floors.appendChild(svg('rect', { x: 460, y: 510, width: 480, height: 220, fill: 'url(#campusPave)' }, 'campus-pave'));
  plan.appendChild(stag(floors, 60));

  /* ------------------------------ wings ---------------------------------- */
  /* Built BEFORE the rooms so the wing floor paints under them. Two states and
   * nothing between: taped shut (the ghost plates tease the class names) or
   * open (the alley is paved, the rooms are real). OPEN_SEMESTERS decides. */
  function buildWing(id) {
    const w = WINGS[id];
    const open = wingIsOpen(id);
    const [x, y, ww, wh] = w.rect;
    const keys = wingRoomKeys(id);
    const g = svg('g', null, open ? 'campus-wing' : 'campus-room locked');
    g.appendChild(svg('rect', { x, y, width: ww, height: wh }, 'campus-gfloor' + (open ? '' : ' striped')));

    if (open) {
      /* the wing's own alley - the spur every room in here opens onto */
      const ax = w.alleyX - 10;
      g.appendChild(svg('rect', { x: ax, y, width: 20, height: wh }, 'campus-ghall'));
      g.appendChild(svg('rect', { x: ax, y, width: 20, height: wh, fill: 'url(#campusPave)' }, 'campus-pave'));
      /* TWO LINES, not one: "EAST WING · SEMESTER II" is 184px of tracked mono
       * and would run off the cropped edge of the plan (see THE SAFE BAND). */
      g.appendChild(svgText(w.labelX, w.labelY, 'campus-rsub', t(w.nameKey, w.nameEn).toUpperCase()));
      g.appendChild(svgText(w.labelX, w.labelY + 15, 'campus-rsub tiny',
        (t('semester', 'Semester') + ' ' + w.roman).toUpperCase()));
      /* An open wing is scenery, not a door: the ROOMS take the clicks. It keeps
       * its hover card so the alley still says where you are. */
      attachTip(g, () => ({
        name: t(w.nameKey, w.nameEn),
        status: t('semester', 'Semester') + ' ' + w.roman,
        desc: t(w.openDescKey, w.openDescEn),
      }));
      plan.appendChild(stag(g, w.din));
      return g;
    }

    keys.forEach((key) => {
      const spec = ROOMS[key];
      const [rx, ry, rw, rh] = spec.rect;
      g.appendChild(svg('rect', { x: rx + 12, y: ry + 8, width: rw - 24, height: rh - 16 }, 'campus-ghost'));
      g.appendChild(svgText(rx + rw / 2, ry + rh / 2 + 4, 'campus-ghostt',
        t('game_' + key, spec.gameEn || spec.nameEn).toUpperCase()));
    });
    const mx = w.mouthX;
    g.appendChild(svg('line', { x1: mx, y1: 446, x2: mx, y2: 474 }, 'campus-tape2'));
    g.appendChild(svg('line', { x1: mx - 7, y1: 452, x2: mx + 7, y2: 468 }, 'campus-tape'));
    g.appendChild(svg('line', { x1: mx + 7, y1: 452, x2: mx - 7, y2: 468 }, 'campus-tape'));
    g.appendChild(svgText(w.labelX, w.labelY, 'campus-rsub ' + w.sealedTone,
      t(w.sealedKey, w.sealedEn).toUpperCase()));
    const sealedCard = () => ({
      name: t(w.nameKey, w.nameEn),
      status: t(w.sealedKey, w.sealedEn),
      desc: t(w.sealedDescKey, w.sealedDescEn),
    });
    attachTip(g, () => {
      const d = sealedCard();
      return { name: d.name, status: t('campus_sealed', 'Sealed') + ' — ' + d.status, desc: d.desc };
    });
    g.addEventListener('click', () => openFacilityCard(Object.assign(sealedCard(), { sealed: true })));
    plan.appendChild(stag(g, w.din));
    return g;
  }
  Object.keys(WINGS).forEach(buildWing);

  /* route (under rooms so door arcs stay crisp) + stop badges (over, added last) */
  routePath = svg('path', { d: '' }, 'campus-route');
  plan.appendChild(routePath);

  /* ------------------------------ rooms ---------------------------------- */
  /* A door is the architect's symbol: a gap in the wall plus the leaf pivoting
   * on one jamb. `spec.door` is the coordinate ALONG that wall - an x on the
   * corridor's north (y 430) or south (y 510) wall, a y on a wing room's own
   * west/east wall. A wing leaf swings INTO the room, because the wing alley is
   * only 20 wide and a 24-unit swing would cross it. */
  function doorFor(spec) {
    const d = spec.door;
    if (spec.side === 'n') {
      return [
        svg('line', { x1: d - 12, y1: 430, x2: d + 12, y2: 430 }, 'campus-gap'),
        svg('path', { d: 'M' + (d + 12) + ',430 A24,24 0 0 1 ' + (d - 12) + ',454 L' + (d - 12) + ',430' }, 'campus-door'),
      ];
    }
    if (spec.side === 'w' || spec.side === 'e') {
      const r = spec.rect || [0, 0, 0, 0];
      const west = spec.side === 'w';
      const wx = west ? r[0] : r[0] + r[2];
      const tip = west ? wx + 24 : wx - 24;
      return [
        svg('line', { x1: wx, y1: d - 12, x2: wx, y2: d + 12 }, 'campus-gap'),
        svg('path', {
          d: 'M' + wx + ',' + (d + 12) + ' A24,24 0 0 ' + (west ? 0 : 1)
            + ' ' + tip + ',' + (d - 12) + ' L' + wx + ',' + (d - 12),
        }, 'campus-door'),
      ];
    }
    return [
      svg('line', { x1: d - 12, y1: 510, x2: d + 12, y2: 510 }, 'campus-gap'),
      svg('path', { d: 'M' + (d + 12) + ',510 A24,24 0 0 0 ' + (d - 12) + ',486 L' + (d - 12) + ',510' }, 'campus-door'),
    ];
  }

  /** Room furniture, per game - the mockup's dressing, condensed. */
  function furnitureFor(key, g) {
    const put = (n) => g.appendChild(n);
    if (key === 'daily_trigger') {
      put(svg('line', { x1: 248, y1: 216, x2: 412, y2: 216 }, 'campus-furn'));
      put(svg('rect', { x: 300, y: 232, width: 60, height: 16 }, 'campus-furnf'));
      [272, 304].forEach((y) => [252, 304, 356].forEach((x) => put(svg('rect', { x, y, width: 22, height: 14 }, 'campus-furnf'))));
    } else if (key === 'deja_vu') {
      [[492, 248], [580, 248]].forEach(([x, y]) => {
        put(svg('rect', { x, y, width: 70, height: 36 }, 'campus-furnf'));
        [0, 18, 36].forEach((dx) => [12, 26].forEach((dy) => put(svg('circle', { cx: x + 14 + dx, cy: y + dy, r: 2 }, 'campus-furn'))));
      });
    } else if (key === 'impulse_control') {
      [266, 298].forEach((cy) => [742, 778, 814, 850].forEach((cx) => put(svg('circle', { cx, cy, r: 5 }, 'campus-furn'))));
      put(svg('circle', { cx: 810, cy: 282, r: 28, 'stroke-dasharray': '3 5' }, 'campus-furn'));
    } else if (key === 'lost_and_found') {
      [576, 612, 648].forEach((y) => put(svg('rect', { x: 252, y, width: 156, height: 9 }, 'campus-furnf')));
      put(svg('rect', { x: 256, y: 666, width: 18, height: 18 }, 'campus-furnf'));
      put(svg('rect', { x: 282, y: 670, width: 14, height: 14 }, 'campus-furnf'));
      put(svg('rect', { x: 300, y: 524, width: 64, height: 11 }, 'campus-furnf'));
    } else if (key === 'the_deep_end') {
      // covered walk down the alley between 104 and the hall (x 440..460)
      put(svg('line', { x1: 443, y1: 512, x2: 443, y2: 738 }, 'campus-furn'));
      put(svg('line', { x1: 457, y1: 512, x2: 457, y2: 738 }, 'campus-furn'));
      // basin + waterline
      put(svg('rect', { x: 330, y: 760, width: 280, height: 44 }, 'campus-furn'));
      put(svg('rect', { x: 336, y: 766, width: 268, height: 32, 'stroke-dasharray': '3 5' }, 'campus-furn'));
      // lane lines
      [771, 782, 793].forEach((y) => put(svg('line', { x1: 340, y1: y, x2: 600, y2: y, 'stroke-dasharray': '9 7' }, 'campus-furn')));
      // ladder over the north coping
      [560, 578].forEach((x) => put(svg('line', { x1: x, y1: 752, x2: x, y2: 770 }, 'campus-furn')));
      [756, 763].forEach((y) => put(svg('line', { x1: 560, y1: y, x2: 578, y2: y }, 'campus-furn')));
      // diving block on the west deck
      put(svg('rect', { x: 306, y: 774, width: 20, height: 14 }, 'campus-furnf'));
      put(svg('line', { x1: 326, y1: 774, x2: 326, y2: 788 }, 'campus-furn'));
    } else if (key === 'misdirection') {
      // three cups on a felt line, in the band between the sign and the name
      put(svg('line', { x1: 1288, y1: 388, x2: 1344, y2: 388 }, 'campus-furn'));
      [1298, 1316, 1334].forEach((cx) => put(svg('circle', { cx, cy: 384, r: 3.2 }, 'campus-furn')));
    } else if (key === 'echo') {
      // six pads in a row
      [1292, 1302, 1312, 1322, 1332, 1342].forEach((x) => put(svg('rect', { x, y: 456, width: 6, height: 6 }, 'campus-furnf')));
    } else if (key === 'instant_recall') {
      // a four-frame strip
      put(svg('rect', { x: 1288, y: 526, width: 56, height: 9 }, 'campus-furn'));
      [1302, 1316, 1330].forEach((x) => put(svg('line', { x1: x, y1: 526, x2: x, y2: 535 }, 'campus-furn')));
    } else if (key === 'anomaly') {
      // a contact sheet: eight identical frames, one of them isn't
      [398, 407].forEach((y) => [104, 114, 124, 134].forEach((x) => put(svg('rect', { x, y, width: 6, height: 6 }, 'campus-furnf'))));
    } else if (key === 'composure') {
      // a sliding frame, three across
      put(svg('rect', { x: 102, y: 510, width: 36, height: 18 }, 'campus-furn'));
      [114, 126].forEach((x) => put(svg('line', { x1: x, y1: 510, x2: x, y2: 528 }, 'campus-furn')));
      put(svg('line', { x1: 102, y1: 519, x2: 138, y2: 519 }, 'campus-furn'));
    }
  }

  function buildClassRoom(key) {
    const spec = ROOMS[key];
    const [x, y, w, h] = spec.rect;
    const g = svg('g', null, 'campus-room');
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-gfloor'));
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-lit'));
    furnitureFor(key, g);
    /* The sign's centre line: the door x for a corridor room, an explicit pin for
     * a wing room whose `door` is a y. Unchanged for every Semester-1 room. */
    const signX = spec.neonX != null ? spec.neonX : spec.door;
    const ping = svg('circle', { cx: signX, cy: y + h / 2, r: 12 }, 'campus-ping');
    g.appendChild(ping);
    // A room bolted to the corridor derives its label row from `side`; a DETACHED
    // building (the Pool) and a WING room pin their own, because y + 46 would land
    // in the lawn or straight through the sign.
    const nameY = spec.nameY != null ? spec.nameY : (spec.side === 'n' ? y + 156 : y + 46);
    const nameNode = svgText(x + w / 2, nameY, 'campus-rname', t(spec.nameKey, spec.nameEn).toUpperCase());
    /* A wing room is half a corridor room wide, so its plate steps down a size.
     * This is the ONE inline style the campus writes and it is a metric, never a
     * colour - the .campus-rname rule (and every token in it) still applies. */
    if (spec.wing) nameNode.setAttribute('style', 'font-size:11px');
    g.appendChild(nameNode);
    /* ...and its number row carries the ROOM NUMBER alone. "RM 203 · INSTANT
     * RECALL" is 150px of text in a 112px room and would run straight off the
     * cropped edge of the plan; the neon sign, the hover card, the door card and
     * the hanging board all still name the class. */
    g.appendChild(svgText(x + w / 2, nameY + (spec.wing ? 16 : 18),
      spec.wing ? 'campus-rsub tiny' : 'campus-rsub',
      (spec.wing
        ? t('campus_rm', 'RM') + ' ' + spec.rm
        : t('campus_rm', 'RM') + ' ' + spec.rm + ' · ' + name(key)).toUpperCase()));
    doorFor(spec).forEach((n) => g.appendChild(n));
    const neonY = spec.neonY != null ? spec.neonY : (spec.side === 'n' ? 398 : 694);
    const neon = svg('g', null, 'campus-neon');
    const neonRect = svg('rect', { x: signX - 47, y: neonY, width: 94, height: 16, rx: 3 });
    const neonText = svgText(signX, neonY + 11, null, '');
    neon.appendChild(neonRect);
    neon.appendChild(neonText);
    g.appendChild(neon);
    roomRefs[key] = { g, neon, neonText, ping, spec };
    g.addEventListener('click', () => openClassCard(key));
    attachTip(g, () => classTip(key));
    plan.appendChild(g);
    return g;
  }
  /* A CLOSED semester has no rooms at all - not dark ones. Its games are absent
   * from the registry pool too (games/registry.js OPEN_SEMESTERS), so the two
   * halves of the release gate can never disagree. */
  Object.keys(ROOMS).filter(isOpenSemester)
    .forEach((key, i) => stag(buildClassRoom(key), 250 + i * 110));

  /* ------------------------------ facilities ----------------------------- */
  function facility(rect, door, side, nameText, subText, onClick, tip) {
    const [x, y, w, h] = rect;
    const g = svg('g', null, 'campus-room facility');
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-gfloor'));
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-lit'));
    const nameY = side === 'n' ? y + 156 : y + 176;
    g.appendChild(svgText(x + w / 2, nameY, 'campus-rname', nameText.toUpperCase()));
    if (subText) g.appendChild(svgText(x + w / 2, nameY + 18, 'campus-rsub', subText.toUpperCase()));
    if (door != null) doorFor({ door, side }).forEach((n) => g.appendChild(n));
    if (onClick) g.addEventListener('click', onClick);
    if (tip) attachTip(g, tip);
    plan.appendChild(g);
    return g;
  }

  /* Records (report card) - north-east */
  const recordsG = facility([940, 210, 280, 220], 1080, 'n',
    t('campus_records', 'Records'),
    // THE OFFICE, not just the report card: the punch-card wall lives here now
    // (PUNCHCARD §6) and the door plate should say so before it is opened.
    t('punchcard', 'Punch Card') + ' · ' + t('report_card', 'Report Card'),
    () => { if (handlers.records) handlers.records(); },
    () => ({
      name: t('campus_records', 'Records'),
      status: t('report_card', 'Report Card'),
      desc: t('campus_desc_records', 'Report card, attendance ledger, grades. Your whole term, in ink.'),
      // (the desc row is unchanged on purpose - it already describes the office)
    }));
  [228, 262, 296, 330].forEach((y) => recordsG.appendChild(svg('rect', { x: 1196, y, width: 14, height: 26 }, 'campus-furnf')));
  recordsG.appendChild(svg('rect', { x: 1044, y: 264, width: 66, height: 24 }, 'campus-furnf'));
  recordsG.appendChild(svg('rect', { x: 1006, y: 308, width: 140, height: 52, 'stroke-dasharray': '3 5' }, 'campus-furn'));

  /* Registrar (settings) - south-east */
  const regG = facility([960, 510, 260, 220], 1040, 's',
    t('campus_registrar', 'Registrar'),
    t('settings', 'Settings'),
    () => { if (handlers.registrar) handlers.registrar(); },
    () => ({
      name: t('campus_registrar', 'Registrar'),
      status: t('settings', 'Settings'),
      desc: t('campus_desc_registrar', 'Every setting is a form. Every consent, a waiver with a stamp.'),
    }));
  regG.appendChild(svg('rect', { x: 992, y: 560, width: 90, height: 14 }, 'campus-furnf'));
  regG.appendChild(svg('rect', { x: 992, y: 560, width: 14, height: 70 }, 'campus-furnf'));
  [[1124, 586], [1140, 600], [1156, 614]].forEach(([cx, cy]) => regG.appendChild(svg('circle', { cx, cy, r: 3 }, 'campus-furn')));
  regG.appendChild(svg('line', { x1: 1116, y1: 578, x2: 1164, y2: 622, 'stroke-dasharray': '2 5' }, 'campus-furn'));
  [640, 674].forEach((y) => regG.appendChild(svg('rect', { x: 1188, y, width: 14, height: 26 }, 'campus-furnf')));
  stag(recordsG, 700);
  stag(regG, 780);

  /* Entrance hall dressing (notice board, trophy case, admissions desk, crest) */
  const hall = svg('g', null, 'campus-halldress');
  hall.appendChild(svg('circle', { cx: 700, cy: 622, r: 46, 'stroke-dasharray': '4 6' }, 'campus-crestring'));
  hall.appendChild(svgText(700, 638, 'campus-crestA', 'A'));
  hall.appendChild(svg('rect', { x: 500, y: 516, width: 130, height: 10 }, 'campus-furnf'));
  [[516, 'p1'], [548, 'p2'], [583, 'p3'], [612, 'p4']].forEach(([cx, k]) => hall.appendChild(svg('circle', { cx, cy: 521, r: 1.6 }, 'campus-pin ' + k)));
  hall.appendChild(svgText(565, 542, 'campus-rsub tiny', t('campus_notice_board', 'Notice Board').toUpperCase()));
  hall.appendChild(svg('rect', { x: 916, y: 548, width: 12, height: 150 }, 'campus-furnf'));
  [[566, 'gold'], [596, 'gold'], [626, 'lav'], [656, 'dim']].forEach(([cy, k]) => hall.appendChild(svg('circle', { cx: 922, cy, r: 2.4 }, 'campus-trophy ' + k)));
  hall.appendChild(svgText(893, 628, 'campus-rsub tiny', t('campus_trophy_case', 'Trophy Case').toUpperCase(), { transform: 'rotate(-90 893 628)' }));
  hall.appendChild(svg('rect', { x: 472, y: 600, width: 46, height: 86 }, 'campus-furnf'));
  hall.appendChild(svg('line', { x1: 480, y1: 616, x2: 510, y2: 616 }, 'campus-furn'));
  hall.appendChild(svg('line', { x1: 480, y1: 632, x2: 510, y2: 632 }, 'campus-furn'));
  hall.appendChild(svg('circle', { cx: 495, cy: 662, r: 3 }, 'campus-lamp'));
  hall.appendChild(svgText(452, 644, 'campus-rsub tiny', t('campus_admissions', 'Admissions').toUpperCase(), { transform: 'rotate(-90 452 644)' }));
  plan.appendChild(stag(hall, 600));

  /* Entrance hall as a facility hit-area (notices card) */
  const hallG = svg('g', null, 'campus-room facility');
  hallG.appendChild(svg('rect', { x: 460, y: 510, width: 480, height: 220, fill: 'transparent', stroke: 'none' }, 'campus-hit'));
  hallG.appendChild(svgText(700, 700, 'campus-rname dim', t('campus_entrance_hall', 'Entrance Hall').toUpperCase()));
  hallG.addEventListener('click', () => openFacilityCard({
    name: t('campus_entrance_hall', 'Entrance Hall'),
    status: t('campus_notice_board', 'Notice Board'),
    desc: banner || t('campus_desc_entrance',
      'The notice board carries announcements. The trophy case waits for your diplomas.'),
  }));
  attachTip(hallG, () => ({
    name: t('campus_entrance_hall', 'Entrance Hall'),
    status: t('campus_notice_board', 'Notice Board') + ' · ' + t('campus_trophy_case', 'Trophy Case'),
    desc: banner || t('campus_desc_entrance',
      'The notice board carries announcements. The trophy case waits for your diplomas.'),
  }));
  plan.appendChild(stag(hallG, 860));

  /* corridor <-> entrance opening + main gate */
  plan.appendChild(svg('line', { x1: 662, y1: 510, x2: 738, y2: 510 }, 'campus-gap'));
  plan.appendChild(svg('line', { x1: 662, y1: 510, x2: 738, y2: 510, 'stroke-dasharray': '3 6' }, 'campus-opening'));
  plan.appendChild(svg('line', { x1: 682, y1: 730, x2: 758, y2: 730 }, 'campus-gap'));
  plan.appendChild(svg('path', { d: 'M682,730 A38,38 0 0 0 720,768' }, 'campus-door'));
  plan.appendChild(svg('path', { d: 'M758,730 A38,38 0 0 1 720,768' }, 'campus-door'));
  plan.appendChild(svgText(720, 788, 'campus-rsub wide', t('campus_main_gate', 'Main Gate').toUpperCase()));

  /* an OPEN wing's mouth: the corridor's end wall is cut away, same treatment as
   * the corridor <-> entrance opening above. A sealed wing keeps its wall (and
   * its tape). */
  Object.keys(WINGS).forEach((id) => {
    if (!wingIsOpen(id)) return;
    const mx = WINGS[id].mouthX;
    plan.appendChild(svg('line', { x1: mx, y1: 434, x2: mx, y2: 506 }, 'campus-gap'));
    plan.appendChild(svg('line', { x1: mx, y1: 434, x2: mx, y2: 506, 'stroke-dasharray': '3 6' }, 'campus-opening'));
  });

  /* stop badges live above everything in the plan */
  stopsLayer = svg('g', null, 'campus-stops');
  plan.appendChild(stopsLayer);

  root.appendChild(plan);

  /* ------------------------------ fireflies ------------------------------ */
  [['18%', '78%', '9s', '0s', ''], ['26%', '84%', '11s', '2s', 'p'], ['64%', '88%', '8s', '1s', ''],
   ['72%', '80%', '12s', '4s', 'p'], ['84%', '74%', '10s', '3s', ''], ['44%', '90%', '9.5s', '5s', 'p'],
   ['90%', '86%', '13s', '6s', ''], ['10%', '64%', '11s', '7s', 'p']]
    .forEach(([x, y, tt, dl, p]) => {
      const f = el('div', 'campus-fly' + (p ? ' p' : ''));
      f.style.setProperty('--x', x); f.style.setProperty('--y', y);
      f.style.setProperty('--t', tt); f.style.setProperty('--dl', dl);
      root.appendChild(f);
    });

  /* ------------------------------ hanging board -------------------------- */
  const boardwrap = el('div', 'campus-boardwrap');
  const chains = el('div', 'campus-chains');
  chains.appendChild(el('div', 'campus-chain'));
  chains.appendChild(el('div', 'campus-chain'));
  boardwrap.appendChild(chains);
  const boardMount = el('div', 'campus-board');
  boardwrap.appendChild(boardMount);
  const footMount = el('div', 'campus-boardfoot');
  boardwrap.appendChild(footMount);
  root.appendChild(boardwrap);

  /* ------------------------------ crest / bell / hint -------------------- */
  const crest = el('div', 'campus-crest');
  const h1 = el('h1', null, t('arcademy', 'The Arcademy'));
  crest.appendChild(h1);
  const termRoman = ROMAN[currentSemester()] || 'I';
  crest.appendChild(el('p', null,
    (t('campus_night_sessions', 'Night Sessions') + ' · ' + t('semester', 'Semester') + ' ' + termRoman).toUpperCase()));
  crest.appendChild(el('div', 'campus-crestrule'));
  root.appendChild(crest);

  const topCluster = el('div', 'campus-topright');
  const bell = el('div', 'campus-bellchip');
  bell.appendChild(el('span', 'ic', '🔔'));
  bell.appendChild(el('span', 'cap', t('campus_next_bell', 'Next Bell').toUpperCase()));
  bellText = el('span', 'tm num', bellLabel(bellSecondsLeft()));
  bell.appendChild(bellText);
  topCluster.appendChild(bell);
  /* Settings stays one click away even with the topbar gone - the gear is the
   * shortcut, the Registrar room is the diegetic front door to the same page. */
  const gear = el('button', 'campus-gearbtn', '⚙');
  gear.type = 'button';
  gear.setAttribute('aria-label', t('settings', 'Settings'));
  gear.setAttribute('title', t('settings', 'Settings'));
  gear.addEventListener('click', () => { if (handlers.registrar) handlers.registrar(); });
  topCluster.appendChild(gear);
  root.appendChild(topCluster);

  root.appendChild(el('div', 'campus-hint',
    t('campus_hint', 'Hover a room - click to step inside.').toUpperCase()));

  /* ------------------------------ student ID ----------------------------- */
  const id = el('div', 'campus-idcard');
  const idTop = el('div', 'id-top');
  idTop.appendChild(el('div', 'id-photo'));
  const idMeta = el('div');
  idMeta.appendChild(el('div', 'id-name', t('student', 'Student')));
  idMeta.appendChild(el('div', 'id-no', (t('semester', 'Semester') + ' ' + termRoman).toUpperCase()));
  idTier = el('span', 'id-tier', '');
  idMeta.appendChild(idTier);
  idTop.appendChild(idMeta);
  id.appendChild(idTop);
  const idStats = el('div', 'id-stats');
  const stat = (cls, label) => {
    const s = el('div', 'id-stat' + (cls ? ' ' + cls : ''));
    const b = el('b', null, '');
    s.appendChild(b);
    s.appendChild(el('span', null, label.toUpperCase()));
    idStats.appendChild(s);
    return b;
  };
  idStreak = stat('', t('attendance', 'Attendance'));
  idPerfect = stat('gp', t('perfect_attendance', 'Perfect Attendance'));
  id.appendChild(idStats);
  root.appendChild(id);

  root.appendChild(el('div', 'campus-vignette'));

  /* ------------------------------ tooltip -------------------------------- */
  const tip = el('div', 'campus-tip');
  const tipName = el('div', 't-name');
  const tipStatus = el('div', 't-status');
  const tipDesc = el('div', 't-desc');
  tip.appendChild(tipName); tip.appendChild(tipStatus); tip.appendChild(tipDesc);
  root.appendChild(tip);

  function attachTip(g, dataFn) {
    g.addEventListener('mouseenter', () => {
      let d;
      try { d = dataFn(); } catch (e) { d = null; }
      if (!d) return;
      tipName.textContent = d.name || '';
      tipStatus.textContent = d.status || '';
      tipDesc.textContent = d.desc || '';
      tip.classList.add('on');
    });
    g.addEventListener('mousemove', (e) => {
      const r = (root.getBoundingClientRect ? root.getBoundingClientRect() : null);
      if (!r || e.clientX == null) return;
      const w = (typeof window !== 'undefined' && window.innerWidth) ? window.innerWidth : 1280;
      tip.style.setProperty('left', Math.min(e.clientX + 18, w - 270) - r.left + 'px');
      tip.style.setProperty('top', (e.clientY + 18 - r.top) + 'px');
    });
    g.addEventListener('mouseleave', () => tip.classList.remove('on'));
  }

  function classTip(key) {
    const spec = ROOMS[key];
    const r = st.rooms[key] || {};
    return {
      name: t(spec.nameKey, spec.nameEn) + ' ' + spec.rm,
      status: statusLine(key),
      desc: t(spec.descKey, spec.descEn),
    };
  }

  function statusLine(key) {
    const r = st.rooms[key] || {};
    if (!r.scheduled) {
      return r.unlocked
        ? t('campus_unlocked', 'Unlocked - open every night')
        : t('campus_not_tonight', 'Not tonight');
    }
    const period = t('period', 'Period') + ' ' + r.period + (r.timeLabel ? ' · ' + r.timeLabel : '');
    if (r.done) return t('retake', 'Retake') + ' — ' + period;
    return t('campus_in_session', 'In Session') + ' — ' + period;
  }

  /* ------------------------------ class card ----------------------------- */
  const scrim = el('div', 'campus-scrim');
  const card = el('div', 'campus-classcard');
  const ccPlate = el('div', 'cc-plate');
  const ccRoom = el('span', null, '');
  const ccX = el('button', null, '✕');
  ccX.type = 'button';
  ccX.setAttribute('aria-label', t('back', 'Back'));
  ccPlate.appendChild(ccRoom); ccPlate.appendChild(ccX);
  card.appendChild(ccPlate);
  const ccBody = el('div', 'cc-body');
  const ccCourse = el('h3', null, '');
  const ccStatus = el('div', 'cc-status', '');
  const ccDesc = el('p', 'cc-desc', '');
  const ccChips = el('div', 'cc-mods');
  const ccMeta = el('div', 'cc-meta');
  const ccStamp = el('div', 'cc-stamp', '');
  const ccXp = el('div', 'cc-xp', '');
  ccMeta.appendChild(ccStamp); ccMeta.appendChild(ccXp);
  ccBody.appendChild(ccCourse); ccBody.appendChild(ccStatus); ccBody.appendChild(ccDesc);
  ccBody.appendChild(ccChips); ccBody.appendChild(ccMeta);
  card.appendChild(ccBody);
  const ccActions = el('div', 'cc-actions');
  const ccGo = el('button', 'btnp', '');
  ccGo.type = 'button';
  ccActions.appendChild(ccGo);
  /* THE SECOND DOOR. A game that declares `manifest.endless` gets a secondary
   * button next to Begin/Retake. It is live even when the class is not on
   * tonight's board and even when it is already graded - a free swim is not the
   * timetable's business - but never while the host has the class suspended. */
  const ccAlt = el('button', 'btnp alt', '');
  ccAlt.type = 'button';
  ccAlt.hidden = true;
  ccActions.appendChild(ccAlt);
  card.appendChild(ccActions);
  const ccAltHint = el('p', 'cc-althint', '');
  ccAltHint.hidden = true;
  card.appendChild(ccAltHint);
  scrim.appendChild(card);
  root.appendChild(scrim);

  let cardAction = null;
  let cardAltAction = null;
  scrim.addEventListener('click', (e) => { if (e.target === scrim) closeCard(); });
  ccX.addEventListener('click', () => closeCard());
  ccGo.addEventListener('click', () => {
    const act = cardAction;
    closeCard();
    if (act) { try { act(); } catch (e) { say('card action threw: ' + ((e && e.message) || e)); } }
  });
  ccAlt.addEventListener('click', () => {
    const act = cardAltAction;
    closeCard();
    if (act) { try { act(); } catch (e) { say('card alt action threw: ' + ((e && e.message) || e)); } }
  });

  /** Paint (or retire) the secondary Free Swim button for a room. */
  function setAltButton(key, room) {
    const e = room && room.endless;
    if (!e || st.suspended) {
      ccAlt.hidden = true;
      ccAlt.disabled = true;
      ccAlt.textContent = '';
      ccAltHint.hidden = true;
      ccAltHint.textContent = '';
      cardAltAction = null;
      return;
    }
    ccAlt.hidden = false;
    ccAlt.disabled = false;
    ccAlt.textContent = t(e.labelKey || 'free_swim', 'Free Swim').toUpperCase();
    // The game's own hint if it declared one, else the shell's neutral line -
    // the button must always say what it costs (nothing) before it is pressed.
    const hint = t(e.hintKey || 'free_swim_hint', '');
    ccAltHint.textContent = hint;
    ccAltHint.hidden = !hint;
    cardAltAction = () => { if (handlers.freeSwim) handlers.freeSwim(key); };
  }

  function popCard() {
    cardOpen = true;
    scrim.classList.add('on');
    tip.classList.remove('on');
    /* restart the door-open animation (mockup's reflow trick) */
    card.classList.remove('swing');
    void card.offsetWidth;
    card.classList.add('swing');
  }

  function closeCard() {
    if (!cardOpen) return false;
    cardOpen = false;
    scrim.classList.remove('on');
    return true;
  }

  function chip(text) { const c = el('span', 'cc-mod', text); ccChips.appendChild(c); return c; }

  function openClassCard(key) {
    const spec = ROOMS[key];
    const r = st.rooms[key] || {};
    ccRoom.textContent = (t(spec.nameKey, spec.nameEn) + ' · ' + t('campus_rm', 'RM') + ' ' + spec.rm).toUpperCase();
    ccCourse.textContent = name(key);
    ccStatus.textContent = statusLine(key).toUpperCase();
    ccDesc.textContent = t(spec.descKey, spec.descEn);
    ccChips.textContent = '';
    if (r.tier) chip(tierLabel(r.tier));
    if (r.family) chip(familyLabel(r.family));
    if (r.timeBudgetSec) chip(r.timeBudgetSec + 's');
    if (r.homeroom) chip(t('homeroom', 'Homeroom'));
    ccStamp.textContent = r.done ? String(r.grade).toUpperCase()
      : (r.unlocked ? t('punchcard_unlocked_chip', 'Unlocked') : '');
    ccStamp.classList[(r.done || r.unlocked) ? 'remove' : 'add']('off');
    ccStamp.classList[(!r.done && r.unlocked) ? 'add' : 'remove']('cc-stamp-unlocked');
    ccXp.textContent = r.done
      ? t('campus_xp_retake', 'Retakes pay no XP - pride only.')
      : (r.scheduled ? t('campus_xp_first', 'First pass of the day pays XP.') : '');
    if (st.suspended) {
      ccGo.textContent = t('class_suspended', 'Class Suspended').toUpperCase();
      ccGo.disabled = true;
      cardAction = null;
    } else if (r.scheduled) {
      ccGo.textContent = (r.done ? t('retake', 'Retake') : t('begin_class', 'Begin')).toUpperCase();
      ccGo.disabled = false;
      cardAction = () => { if (handlers.begin) handlers.begin(key); };
    } else if (r.unlocked) {
      /* THE EARNED DOOR (PUNCHCARD §2.3). Ten holes closed the card and the
       * card IS the key: this room offers a full graded Begin every night from
       * now on, off the board or on it, through the same path the dev pass
       * uses. It ranks ABOVE the dev pass on purpose - when both are true the
       * player should be told which one they earned. */
      ccGo.textContent = t('begin_class', 'Begin').toUpperCase();
      ccGo.disabled = false;
      ccXp.textContent = t('campus_unlocked_hint',
        'Card complete. This room opens every night, board or no board.');
      cardAction = () => { if (handlers.begin) handlers.begin(key); };
    } else if (st.devPass) {
      /* THE DEV DOOR. Off the board but the host opened the building with the
       * dev switch: Begin anyway (the shell runs it as a graded, timed class
       * built from the registry descriptor). Not a player path. */
      ccGo.textContent = t('campus_dev_pass', 'Dev pass · Begin').toUpperCase();
      ccGo.disabled = false;
      ccXp.textContent = t('campus_dev_pass_hint', "Dev pass: off tonight's board, graded anyway.");
      cardAction = () => { if (handlers.begin) handlers.begin(key); };
    } else {
      ccGo.textContent = t('campus_not_tonight', 'Not tonight').toUpperCase();
      ccGo.disabled = true;
      cardAction = null;
      // EMI SEAM: a room that refused. Never `records` or `lab` from here -
      // this branch is only ever a dark CLASSROOM (voice.js geofences those two
      // names anyway, in the engine, where no data file can reopen them).
      try { fireMoment('lockedClick', { what: 'room', gameKey: key }); } catch (e) { /* noop */ }
    }
    setAltButton(key, r);
    popCard();
  }

  function openFacilityCard(d) {
    ccRoom.textContent = String(d.name || '').toUpperCase();
    ccCourse.textContent = d.name || '';
    ccStatus.textContent = String(d.status || '').toUpperCase();
    ccDesc.textContent = d.desc || '';
    ccChips.textContent = '';
    ccStamp.textContent = '';
    ccStamp.classList.add('off');
    ccXp.textContent = '';
    if (d.sealed) {
      ccGo.textContent = t('campus_sealed', 'Sealed').toUpperCase();
      ccGo.disabled = true;
      cardAction = null;
      // EMI SEAM: a sealed wing refused. A facility that OPENS (Records, the
      // Registrar) is not a locked click and never fires one.
      try { fireMoment('lockedClick', { what: 'sealed' }); } catch (e) { /* noop */ }
    } else {
      ccGo.textContent = t('campus_step_inside', 'Step inside').toUpperCase();
      ccGo.disabled = !d.action;
      cardAction = d.action || null;
    }
    setAltButton(null, null);        // facilities are never swum
    popCard();
  }

  /* ------------------------------ update --------------------------------- */
  /** THE one truth for a room's neon sign. update() paints it and the idle
   * attract's split-flap flutter settles back onto it, so a flutter can never
   * strand a stale word on a sign. */
  function neonLabel(key) {
    const r = st.rooms[key] || {};
    // AN UNLOCKED ROOM IS OPEN, NOT IN SESSION. Only the board puts a class in
    // session; a full punch card lights the room every other night, and a sign
    // that said IN SESSION off the board would be the map lying about the deal.
    if (!r.scheduled && r.unlocked) return t('campus_unlocked_sign', 'Open').toUpperCase();
    if (r.mood === 'open') return t('campus_in_session', 'In Session').toUpperCase();
    if (r.mood === 'retake') return t('retake', 'Retake').toUpperCase();
    return '';
  }

  /* Where tonight's numbered badge stands: in the corridor just inside the door
   * for a room bolted to the hall, in the wing alley for a wing room (which
   * pins its own `stop`, because the Main Hall is nowhere near its door). */
  function stopAnchor(key) {
    const spec = ROOMS[key] || {};
    if (spec.stop) return spec.stop;
    return spec.side === 'n' ? [spec.door, 447] : [spec.door, 488];
  }

  /* The legs the route walks for one stop. A corridor room is one point on the
   * hall's centre line - byte-identical to what this drew before. A wing room
   * turns off at its junction, touches its door and comes back out, so the next
   * leg still starts on the centre line instead of cutting through a wall. */
  function routeLegs(key) {
    const spec = ROOMS[key];
    if (!spec) return [];
    if (spec.via) return [spec.via, stopAnchor(key), spec.via];
    return [[spec.door, 470]];
  }

  function routeFor(stops) {
    if (!stops.length) return '';
    let d = 'M720,908 L720,470';
    for (const s of stops) {
      for (const leg of routeLegs(s.gameKey)) d += ' L' + leg[0] + ',' + leg[1];
    }
    return d;
  }

  function update(nextState, nextStats) {
    if (destroyed) return;
    if (nextState) st = nextState;
    const stats2 = nextStats || stats || {};

    for (const key of Object.keys(roomRefs)) {
      const ref = roomRefs[key];
      const r = st.rooms[key] || { mood: 'dark' };
      ref.g.setAttribute('class', 'campus-room'
        + (r.mood === 'open' ? ' open' : r.mood === 'retake' ? ' retake' : ' dark'));
      ref.neonText.textContent = neonLabel(key);
      ref.neon.setAttribute('class', 'campus-neon'
        + (r.mood === 'open' ? '' : r.mood === 'retake' ? ' v' : ' off'));
    }

    /* stop badges + route */
    stopsLayer.textContent = '';
    st.stops.forEach((s, i) => {
      const [x, y] = stopAnchor(s.gameKey);
      const g = svg('g', null, 'campus-stopb' + ((st.rooms[s.gameKey] || {}).done ? ' done' : ''));
      try { g.style.setProperty('--dstop', (1300 + i * 160) + 'ms'); } catch (e) { /* noop */ }
      g.appendChild(svg('circle', { cx: x, cy: y, r: 11 }, 'halo'));
      g.appendChild(svg('circle', { cx: x, cy: y, r: 11 }));
      g.appendChild(svgText(x, y + 4, null, String(s.n)));
      stopsLayer.appendChild(g);
    });
    routePath.setAttribute('d', routeFor(st.stops));
    routePath.setAttribute('class', 'campus-route' + (st.allDone ? ' done' : ''));

    /* ID card */
    idStreak.textContent = '🔥 ' + String((stats2.streak | 0));
    idPerfect.textContent = String((stats2.perfectDays | 0));
    idTier.textContent = tierLabel(stats2.tier || 1).toUpperCase();
  }

  /* enrich class-card state with descriptor detail the shell passes on stops */
  function noteDescriptors(list) {
    for (const c of (Array.isArray(list) ? list : [])) {
      const r = st.rooms[c.gameKey];
      if (!r) continue;
      r.family = c.family;
      r.timeBudgetSec = c.timeBudgetSec;
      r.homeroom = !!c.homeroom;
      r.tier = c.tier;
      // Only ever ADD what the descriptor knows: campusState already set this
      // for every room (scheduled or not), and descriptors cover only tonight's.
      if (c.endless) r.endless = c.endless;
    }
  }

  /* ==========================================================================
   * THE IDLE ATTRACT - Deck VI, "demo, don't explain".
   *
   * After ATTRACT_IDLE_MS of silence on the campus screen the school shows you
   * what to do instead of telling you: tonight's rooms light in ROUTE order, a
   * drawn ghost cursor walks the route between the numbered stops, and one sign
   * re-flaps like the departure board. Four laws it keeps:
   *   - ANY input cancels it and re-arms the timer (Law II - and the cursor is
   *     `pointer-events:none`, so it can never take a click that was meant for a
   *     room);
   *   - reducedMotion degrades to a STATIC glow: tonight's rooms simply light;
   *   - it is SEEDED off the UTC date, so it is the same show for everyone
   *     tonight and a reload replays it (trap 8: UTC seeds content);
   *   - it writes no colour and no stylesheet: the glow is the hover filter the
   *     stylesheet already transitions, and the cursor borrows two token-driven
   *     classes for its fill and its outline.
   * destroy() tears down every timer, every listener and the glow itself.
   * ========================================================================*/
  const idleMs = Math.max(50, Math.round(Number(attractIdleMs) || ATTRACT_IDLE_MS));
  const attractTimers = new Set();
  const glowing = new Set();
  let attractOn = false;
  let idleTimer = 0;
  let cursorG = null;
  let cursorAt = [720, 900];
  let lastInput = 0;

  function utcDaySeed() {
    try { return new Date().toISOString().slice(0, 10); } catch (e) { return '1970-01-01'; }
  }
  const attractSeed = String(dateSeed || utcDaySeed());
  let arng = makeRng('arcademy|campus|attract|' + attractSeed);

  function attractAfter(ms, fn) {
    if (typeof setTimeout !== 'function') return 0;
    const id = setTimeout(() => { attractTimers.delete(id); if (attractOn) fn(); }, ms);
    attractTimers.add(id);
    return id;
  }
  function attractEvery(ms, fn) {
    if (typeof setInterval !== 'function') return 0;
    const id = setInterval(fn, ms);
    attractTimers.add(id);
    return id;
  }
  function attractStop(id) {
    try { clearInterval(id); } catch (e) { /* noop */ }
    attractTimers.delete(id);
  }
  function attractClearAll() {
    attractTimers.forEach((id) => {
      try { clearTimeout(id); } catch (e) { /* noop */ }
      try { clearInterval(id); } catch (e) { /* noop */ }
    });
    attractTimers.clear();
  }

  /* The glow IS the hover look (styles.css already transitions filter on a
   * room), which is the honest thing to demo: this is what hovering does. */
  function glow(key, on) {
    const ref = roomRefs[key];
    if (!ref) return;
    try { ref.g.style.setProperty('filter', on ? ATTRACT_GLOW : ''); } catch (e) { /* noop */ }
    if (on) glowing.add(key); else glowing.delete(key);
  }
  function unglowAll() { Array.from(glowing).forEach((k) => glow(k, false)); }
  function resettleSigns() {
    for (const key of Object.keys(roomRefs)) {
      try { roomRefs[key].neonText.textContent = neonLabel(key); } catch (e) { /* noop */ }
    }
  }

  function ensureCursor() {
    if (cursorG) return cursorG;
    const g = svg('g', null, 'campus-ghostcursor');
    g.appendChild(svg('path', { d: ATTRACT_CURSOR_D }, 'campus-clockpin'));  // fill: --ink
    g.appendChild(svg('path', { d: ATTRACT_CURSOR_D }, 'campus-door'));      // stroke: --campus-line
    g.setAttribute('pointer-events', 'none');
    g.setAttribute('opacity', '0');
    cursorG = g;
    plan.appendChild(g);
    return g;
  }
  function placeCursor(x, y) {
    cursorAt = [x, y];
    if (!cursorG) return;
    const r1 = Math.round(x * 10) / 10;
    const r2 = Math.round(y * 10) / 10;
    try { cursorG.setAttribute('transform', 'translate(' + r1 + ',' + r2 + ')'); } catch (e) { /* noop */ }
  }
  function showCursor(on) {
    if (!cursorG) return;
    try { cursorG.setAttribute('opacity', on ? ATTRACT_CURSOR_ALPHA : '0'); } catch (e) { /* noop */ }
  }
  function glideCursor(to, ms, done) {
    const from = cursorAt.slice();
    const steps = Math.max(1, Math.round(ms / ATTRACT_TICK_MS));
    let i = 0;
    const id = attractEvery(ATTRACT_TICK_MS, () => {
      if (!attractOn) { attractStop(id); return; }
      i += 1;
      const p = Math.min(1, i / steps);
      const e = p * p * (3 - 2 * p);                 // smoothstep: a hand, not a machine
      placeCursor(from[0] + (to[0] - from[0]) * e, from[1] + (to[1] - from[1]) * e);
      if (p >= 1) { attractStop(id); done(); }
    });
    if (!id) { placeCursor(to[0], to[1]); done(); }  // no timers at all (headless): teleport
  }

  /** Split-flap settle: the left of the row lands first, the tail keeps spinning. */
  function flapText(truth, k) {
    const settled = Math.floor(truth.length * (k / ATTRACT_FLIPS));
    let out = '';
    for (let i = 0; i < truth.length; i++) {
      const c = truth.charAt(i);
      out += (i < settled || c === ' ')
        ? c
        : ATTRACT_GLYPHS.charAt(Math.floor(arng() * ATTRACT_GLYPHS.length));
    }
    return out;
  }
  function flutterSign(key, done) {
    const ref = roomRefs[key];
    const truth = ref ? neonLabel(key) : '';
    if (!ref || !truth) { done(); return; }
    let k = 0;
    const id = attractEvery(ATTRACT_FLIP_MS, () => {
      if (!attractOn) { attractStop(id); return; }
      k += 1;
      if (k >= ATTRACT_FLIPS) {
        attractStop(id);
        try { ref.neonText.textContent = neonLabel(key); } catch (e) { /* noop */ }
        done();
        return;
      }
      try { ref.neonText.textContent = flapText(truth, k); } catch (e) { /* noop */ }
    });
    if (!id) done();
  }

  /** Route order when there is a board; otherwise a short tour of the rooms. */
  function attractOrder() {
    const tonight = st.stops.map((s) => s.gameKey).filter((k) => roomRefs[k]);
    return tonight.length ? tonight : Object.keys(roomRefs).slice(0, 3);
  }

  function playLeg(order, i, flutterKey) {
    if (!attractOn) return;
    if (i >= order.length) {
      showCursor(false);
      unglowAll();
      attractAfter(ATTRACT_LOOP_GAP_MS, () => {
        placeCursor(720, 900);
        showCursor(true);
        playLeg(order, 0, flutterKey);
      });
      return;
    }
    const key = order[i];
    glideCursor(stopAnchor(key), ATTRACT_LEG_MS, () => {
      if (!attractOn) return;
      glow(key, true);
      const onward = () => attractAfter(ATTRACT_DWELL_MS, () => {
        glow(key, false);
        playLeg(order, i + 1, flutterKey);
      });
      if (key === flutterKey) flutterSign(key, onward); else onward();
    });
  }

  function startAttract() {
    idleTimer = 0;
    // A card in front of the plan means the player is mid-decision, not idle.
    if (destroyed || attractOn || cardOpen) { armIdle(); return; }
    // EMI SEAM: the player has gone quiet ON THE CAMPUS. The attract's own idle
    // edge is the signal - a mascot does not get a second idle timer.
    try { fireMoment('idlePlayer', { where: 'hub' }); } catch (e) { /* noop */ }
    const order = attractOrder();
    if (!order.length) { armIdle(); return; }
    attractOn = true;
    arng = makeRng('arcademy|campus|attract|' + attractSeed);   // same show, every loop
    if (reducedMotion) { order.forEach((k) => glow(k, true)); return; }
    ensureCursor();
    placeCursor(720, 900);                    // in through the Main Gate, like the route
    showCursor(true);
    playLeg(order, 0, order[Math.floor(arng() * order.length)] || order[0]);
  }

  function armIdle() {
    if (destroyed) return;
    if (idleTimer) { try { clearTimeout(idleTimer); } catch (e) { /* noop */ } }
    idleTimer = (typeof setTimeout === 'function') ? setTimeout(startAttract, idleMs) : 0;
  }

  function cancelAttract(rearm) {
    if (attractOn) {
      attractOn = false;
      attractClearAll();
      unglowAll();
      resettleSigns();
      showCursor(false);
    }
    if (rearm !== false) armIdle();
  }

  /* ANY input cancels. pointermove floods, so a re-arm is throttled - but a
   * RUNNING attract always yields on the very first event. */
  function onInput() {
    const now = (typeof Date !== 'undefined' && Date.now) ? Date.now() : 0;
    if (attractOn) { lastInput = now; cancelAttract(true); return; }
    if (now && now - lastInput < 400) return;
    lastInput = now;
    armIdle();
  }

  const INPUT_EVENTS = ['pointerdown', 'pointerup', 'pointermove', 'wheel', 'touchstart', 'keydown'];
  try { INPUT_EVENTS.forEach((n) => root.addEventListener(n, onInput, true)); } catch (e) { /* noop */ }
  /* keydown never reaches an unfocused <div>, so the document takes that one -
   * guarded, because the headless DOM double's document is a plain object with
   * no event target on it at all. */
  let docBound = false;
  try {
    if (typeof document !== 'undefined' && document && typeof document.addEventListener === 'function') {
      document.addEventListener('keydown', onInput, true);
      docBound = true;
    }
  } catch (e) { /* noop */ }
  armIdle();

  /* ------------------------------ bell tick ------------------------------ */
  let bellTimer = 0;
  function tickBell() { try { bellText.textContent = bellLabel(bellSecondsLeft()); } catch (e) { /* noop */ } }
  if (!reducedMotion && typeof setInterval === 'function') {
    bellTimer = setInterval(tickBell, 1000);
  }
  tickBell();

  update(st, stats);

  return {
    root,
    boardMount,
    footMount,
    update,
    noteDescriptors,
    closeCard,
    openClassCard,
    /** Test seam for the idle attract - never read by the shell. */
    attractDiagnostics() {
      return {
        armed: !!idleTimer, running: attractOn, idleMs, seed: attractSeed,
        glowing: glowing.size, cursor: !!cursorG, timers: attractTimers.size,
      };
    },
    destroy() {
      if (destroyed) return;
      destroyed = true;
      cancelAttract(false);
      if (idleTimer) { try { clearTimeout(idleTimer); } catch (e) { /* noop */ } idleTimer = 0; }
      try { INPUT_EVENTS.forEach((n) => root.removeEventListener(n, onInput, true)); } catch (e) { /* noop */ }
      if (docBound) { try { document.removeEventListener('keydown', onInput, true); } catch (e) { /* noop */ } }
      if (bellTimer) { try { clearInterval(bellTimer); } catch (e) { /* noop */ } bellTimer = 0; }
      if (enterTimer) { try { clearTimeout(enterTimer); } catch (e) { /* noop */ } enterTimer = 0; }
      if (document.documentElement && document.documentElement.classList) {
        try { document.documentElement.classList.remove('arc-campus-on'); } catch (e) { /* noop */ }
      }
      try { root.remove(); } catch (e) { /* noop */ }
    },
  };
}

export default createCampus;
