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
});

/* ----------------------------------------------------------------------------
 * PURE STATE MAPPER - what tonight looks like, per room.
 * -------------------------------------------------------------------------- */
/**
 * @param {Object} o
 * @param {Array}  o.classes   timetable.classes ({gameKey, timeLabel, ...})
 * @param {Object} o.records   gameKey -> {grade} | null  (today's rows)
 * @param {boolean=} o.suspended  global suspend (mandatory video etc.)
 * @returns {{rooms:Object, stops:Array<{gameKey:string,n:number}>, allDone:boolean}}
 *   rooms[gameKey] = { scheduled, period (1-based, 0 = not tonight), done,
 *                      grade, mood:'open'|'retake'|'dark', timeLabel }
 */
export function campusState({ classes, records, suspended } = {}) {
  const list = Array.isArray(classes) ? classes : [];
  const recs = records || {};
  const rooms = Object.create(null);
  for (const key of Object.keys(ROOMS)) {
    rooms[key] = { scheduled: false, period: 0, done: false, grade: '', mood: 'dark', timeLabel: '' };
  }
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
 * @param {Object} o.on           {begin(gameKey), records(), registrar()}
 * @param {Function=} o.log
 * @returns {{root, boardMount, footMount, update, closeCard, destroy}}
 */
export function createCampus({ state, gameName, banner, stats, reducedMotion, on, log } = {}) {
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

  /* route (under rooms so door arcs stay crisp) + stop badges (over, added last) */
  routePath = svg('path', { d: '' }, 'campus-route');
  plan.appendChild(routePath);

  /* ------------------------------ rooms ---------------------------------- */
  function doorFor(spec) {
    const d = spec.door;
    if (spec.side === 'n') {
      return [
        svg('line', { x1: d - 12, y1: 430, x2: d + 12, y2: 430 }, 'campus-gap'),
        svg('path', { d: 'M' + (d + 12) + ',430 A24,24 0 0 1 ' + (d - 12) + ',454 L' + (d - 12) + ',430' }, 'campus-door'),
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
    }
  }

  function buildClassRoom(key) {
    const spec = ROOMS[key];
    const [x, y, w, h] = spec.rect;
    const g = svg('g', null, 'campus-room');
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-gfloor'));
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-lit'));
    furnitureFor(key, g);
    const midY = y + h / 2 + (spec.side === 'n' ? 0 : 0);
    const ping = svg('circle', { cx: spec.door, cy: midY, r: 12 }, 'campus-ping');
    g.appendChild(ping);
    const nameY = spec.side === 'n' ? y + 156 : y + 46;
    g.appendChild(svgText(x + w / 2, nameY, 'campus-rname', t(spec.nameKey, spec.nameEn).toUpperCase()));
    g.appendChild(svgText(x + w / 2, nameY + 18, 'campus-rsub',
      (t('campus_rm', 'RM') + ' ' + spec.rm + ' · ' + name(key)).toUpperCase()));
    doorFor(spec).forEach((n) => g.appendChild(n));
    const neonY = spec.side === 'n' ? 398 : 694;
    const neon = svg('g', null, 'campus-neon');
    const neonRect = svg('rect', { x: spec.door - 47, y: neonY, width: 94, height: 16, rx: 3 });
    const neonText = svgText(spec.door, neonY + 11, null, '');
    neon.appendChild(neonRect);
    neon.appendChild(neonText);
    g.appendChild(neon);
    roomRefs[key] = { g, neon, neonText, ping, spec };
    g.addEventListener('click', () => openClassCard(key));
    attachTip(g, () => classTip(key));
    plan.appendChild(g);
    return g;
  }
  Object.keys(ROOMS).forEach((key, i) => stag(buildClassRoom(key), 250 + i * 110));

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
    t('report_card', 'Report Card') + ' · ' + t('attendance', 'Attendance'),
    () => { if (handlers.records) handlers.records(); },
    () => ({
      name: t('campus_records', 'Records'),
      status: t('report_card', 'Report Card'),
      desc: t('campus_desc_records', 'Report card, attendance ledger, grades. Your whole term, in ink.'),
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

  /* wings - Semesters II / III */
  const east = svg('g', null, 'campus-room locked');
  east.appendChild(svg('rect', { x: 1240, y: 350, width: 160, height: 240 }, 'campus-gfloor striped'));
  [[368, 'game_misdirection', 'Misdirection'], [440, 'game_instant_recall', 'Instant Recall'], [512, 'game_echo', 'Echo']]
    .forEach(([y, key, en]) => {
      east.appendChild(svg('rect', { x: 1258, y, width: 124, height: 56 }, 'campus-ghost'));
      east.appendChild(svgText(1320, y + 31, 'campus-ghostt', t(key, en).toUpperCase()));
    });
  east.appendChild(svg('line', { x1: 1240, y1: 446, x2: 1240, y2: 474 }, 'campus-tape2'));
  east.appendChild(svg('line', { x1: 1233, y1: 452, x2: 1247, y2: 468 }, 'campus-tape'));
  east.appendChild(svg('line', { x1: 1247, y1: 452, x2: 1233, y2: 468 }, 'campus-tape'));
  east.appendChild(svgText(1320, 612, 'campus-rsub pink', t('campus_opens_semester_2', 'Opens Semester II').toUpperCase()));
  attachTip(east, () => ({
    name: t('campus_east_wing', 'East Wing'),
    status: t('campus_sealed', 'Sealed') + ' — ' + t('campus_opens_semester_2', 'Opens Semester II'),
    desc: t('campus_desc_east', 'You can hear hammering behind the tape.'),
  }));
  east.addEventListener('click', () => openFacilityCard({
    name: t('campus_east_wing', 'East Wing'),
    status: t('campus_opens_semester_2', 'Opens Semester II'),
    desc: t('campus_desc_east', 'You can hear hammering behind the tape.'),
    sealed: true,
  }));
  plan.appendChild(stag(east, 940));

  const west = svg('g', null, 'campus-room locked');
  west.appendChild(svg('rect', { x: 40, y: 350, width: 160, height: 240 }, 'campus-gfloor striped'));
  [382, 418, 454, 490, 526, 562].forEach((y) => west.appendChild(svg('line', { x1: 52, y1: y, x2: 188, y2: y }, 'campus-furn')));
  west.appendChild(svg('line', { x1: 196, y1: 440, x2: 204, y2: 480 }, 'campus-tape2'));
  west.appendChild(svgText(120, 612, 'campus-rsub dim', t('campus_semester_3', 'Semester III').toUpperCase()));
  attachTip(west, () => ({
    name: t('campus_west_wing', 'West Wing'),
    status: t('campus_sealed', 'Sealed') + ' — ' + t('campus_semester_3', 'Semester III'),
    desc: t('campus_desc_west', 'The boards are older here.'),
  }));
  west.addEventListener('click', () => openFacilityCard({
    name: t('campus_west_wing', 'West Wing'),
    status: t('campus_semester_3', 'Semester III'),
    desc: t('campus_desc_west', 'The boards are older here.'),
    sealed: true,
  }));
  plan.appendChild(stag(west, 1020));

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
  crest.appendChild(el('p', null,
    (t('campus_night_sessions', 'Night Sessions') + ' · ' + t('semester', 'Semester') + ' I').toUpperCase()));
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
  idMeta.appendChild(el('div', 'id-no', (t('semester', 'Semester') + ' I').toUpperCase()));
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
    if (!r.scheduled) return t('campus_not_tonight', 'Not tonight');
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
  card.appendChild(ccActions);
  scrim.appendChild(card);
  root.appendChild(scrim);

  let cardAction = null;
  scrim.addEventListener('click', (e) => { if (e.target === scrim) closeCard(); });
  ccX.addEventListener('click', () => closeCard());
  ccGo.addEventListener('click', () => {
    const act = cardAction;
    closeCard();
    if (act) { try { act(); } catch (e) { say('card action threw: ' + ((e && e.message) || e)); } }
  });

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
    ccStamp.textContent = r.done ? String(r.grade).toUpperCase() : '';
    ccStamp.classList[r.done ? 'remove' : 'add']('off');
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
    } else {
      ccGo.textContent = t('campus_not_tonight', 'Not tonight').toUpperCase();
      ccGo.disabled = true;
      cardAction = null;
    }
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
    } else {
      ccGo.textContent = t('campus_step_inside', 'Step inside').toUpperCase();
      ccGo.disabled = !d.action;
      cardAction = d.action || null;
    }
    popCard();
  }

  /* ------------------------------ update --------------------------------- */
  function stopAnchor(key) {
    const spec = ROOMS[key];
    return spec.side === 'n' ? [spec.door, 447] : [spec.door, 488];
  }

  function routeFor(stops) {
    if (!stops.length) return '';
    let d = 'M720,908 L720,470';
    for (const s of stops) {
      const [x] = stopAnchor(s.gameKey);
      d += ' L' + x + ',470';
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
      if (r.mood === 'open') {
        ref.neonText.textContent = t('campus_in_session', 'In Session').toUpperCase();
        ref.neon.setAttribute('class', 'campus-neon');
      } else if (r.mood === 'retake') {
        ref.neonText.textContent = t('retake', 'Retake').toUpperCase();
        ref.neon.setAttribute('class', 'campus-neon v');
      } else {
        ref.neonText.textContent = '';
        ref.neon.setAttribute('class', 'campus-neon off');
      }
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
    }
  }

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
    destroy() {
      if (destroyed) return;
      destroyed = true;
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
