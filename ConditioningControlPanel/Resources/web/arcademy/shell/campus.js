/* ============================================================================
 * shell/campus.js - the night-campus planimetry hub (Direction A).
 *
 * The home screen: a blueprint floor plan of the school at night. Rooms ARE the
 * games (fixed geography - a game always lives in its room), facilities are
 * diegetic (Front Office = settings, Records = report card), tonight's classes
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
import { isMobile, onDeviceChange } from '../core/device.js';
/* THE PHANTOM POST chrome: the envelope chip by the bell, the noticeboard and
 * the folded paper by the student ID. Campus mounts the furniture; the shell
 * owns the overlays, the engines and every byte of their state (Wave 4 law:
 * everything arrives through the `post` option bag, and a campus built without
 * one simply has no post - the mockup, the suite and old callers all still
 * stand). */
/* THE STUDENT ID'S PARTS. Pure helpers, shared with the spotlight so the
 * furniture card and the big card can never disagree about what your card says
 * (shell/idcard.js's header). Nothing here mounts anything. */
import { genericPortrait, portraitSrc, portraitLabel, chipRung, paintChip, runPhotoDay,
  studentNumber } from './idcard.js';
import { thud as punchThud } from './punchcard.js';
import { createAccountChip } from './accountchip.js';
import { mountMailChip } from './mailbox.js';
import { mountBoardProp } from './corkboard.js';
import { mountBugleProp } from './bugle.js';
import { paintLever } from './lever.js';

const SVGNS = 'http://www.w3.org/2000/svg';

/* ----------------------------------------------------------------------------
 * FIXED GEOGRAPHY - viewBox 0 0 1440 920, corridor y 430..510.
 * A game always lives in its room; a new semester adds rooms, it never moves
 * one. Coordinates are the mockup's, verbatim.
 *
 * THE ONE EXCEPTION, AND IT IS CLOSED AGAIN (owner ruling 2026-08-23, LIGHTS ON
 * lot 2): "reserve the bigger rooms for the actual games and have the smaller
 * ones for utility". The school was drawn before it had storefronts, so two of
 * its biggest fronts held a filing cabinet and a settings desk while two classes
 * shared a 112x66 broom cupboard with no room for their own art. The Arcademy is
 * still dark (DoorAvailable = false), so that could be fixed once, and was:
 *   - ECHO took the north-east front and INSTANT RECALL the south-east one -
 *     the two rooms Records and the Front Office counter used to hold. Their
 *     lexicon identity travelled with them: the Music Room is still the Music
 *     Room, and both are ordinary corridor rooms now (side/door/stop defaults).
 *   - RECORDS + THE FRONT OFFICE COUNTER (ex-Registrar) moved into the EAST WING,
 *     which stopped being a semester and became the FRONT OFFICE: two compact
 *     rooms, a sign each.
 *   - The WEST WING was rebuilt around a HORIZONTAL alley (the Main Hall simply
 *     carries on west at y 450..490) so its two classes get storefronts deep
 *     enough for a logo band instead of two 96-unit shelves.
 * The plan is frozen again from here: a game never moves rooms.
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
   * The corridor's south wall is full (104 | Entrance Hall | Front Office), so the
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
  /* ---- THE SORTING ROOM - Semester II's newest front (lot 3) ---------------
   * SORT shipped after lot 2 gave Misdirection's old parlour to the front
   * office, so the newest class got the last buildable lot on the map: the
   * Entrance Hall's west span. The hall compacted to x 460..700 and the run
   * x 700..740 became the GATE ALLEY - the Main Gate's double doors already
   * met at x 720 and the nightly route has always entered on that line
   * (M720,908 straight up to the Main Hall), so the alley costs the plan
   * nothing: the walk between the hall and the Sorting Room IS the front walk.
   * An ordinary south-corridor room otherwise: side/door only, badge in the
   * Main Hall, plate on the corridor wall like Lost & Found across the way.
   * THE PLATE SAYS 201 (owner ruling 2026-08-24): sort is Misdirection's
   * substitute, so it wears Misdirection's old number - echo and instant
   * recall slid back to 202/203, the numbers they carried before lot 2. The
   * plates rotated; no room moved. */
  sort: {
    rect: [740, 510, 200, 220], side: 's', door: 840, rm: '201',
    gameEn: 'Sort',
    nameKey: 'campus_room_sort', nameEn: 'The Sorting Room',
    descKey: 'campus_desc_sort',
    descEn: 'Two piles, and you decide what goes in them. Yours to the right.',
  },
  /* ---- THE EAST FRONTS - Semester II's two storefronts ---------------------
   * Ordinary corridor rooms, and deliberately so: `side`/`door` are all they
   * carry, which means doorFor(), stopAnchor() and routeFor() treat them exactly
   * like Homeroom - the numbered badge stands in the Main Hall just inside the
   * door and the route never leaves the hall's centre line to reach them.
   * (Both fronts stop at x 1220, leaving the 20-unit run of hall that opens onto
   * the front office's alley at x 1240.) */
  echo: {
    rect: [940, 210, 280, 220], side: 'n', door: 1080, rm: '202',
    gameEn: 'Echo',
    nameKey: 'campus_room_echo', nameEn: 'Music Room',
    descKey: 'campus_desc_echo',
    descEn: 'It plays a line, you play it back. Then it adds one more, every time.',
  },
  instant_recall: {
    rect: [960, 510, 260, 220], side: 's', door: 1040, rm: '203',
    gameEn: 'Instant Recall',
    nameKey: 'campus_room_instant_recall', nameEn: 'Lecture Hall',
    descKey: 'campus_desc_instant_recall',
    descEn: 'Watch the whole hour, then answer for it. You never hear it coming.',
  },
  /* ---- WEST WING - Semester III --------------------------------------------
   * THE HALL CARRIES ON WEST. The wing's alley is HORIZONTAL (x 54..200, y
   * 450..490): a 40-unit spur of the Main Hall on the hall's own centre line,
   * with one deep room above it and one below. That is what buys the two slow
   * classes a storefront - a room bolted to a vertical alley could never be
   * wider than the 126 units between the safe band and the corridor's west wall,
   * and a class with no logo band is the thing lot 2 exists to fix.
   * Four fields carry the difference from a corridor room, and nothing else:
   *   `door`   is the coordinate ALONG the wall the room opens on - an x here,
   *            because both these walls are horizontal, like the corridor's;
   *   `wallY`  is WHICH horizontal wall (the spur's north edge for the room
   *            above it, its south edge for the room below), because 430/510 are
   *            the Main Hall's own walls and these rooms are nowhere near them;
   *   `stop`   pins the numbered badge in the spur;
   *   `neonX`/`neonY`/`nameY` pin the sign and the label stack INSIDE the room
   *            the way the Pool pins its own.
   * No `via`: the spur IS the hall's centre line, so the route walks straight
   * west to the stop and straight back - one line, no junction, no dogleg.
   * THE SAFE BAND: the plan is `preserveAspectRatio slice`, so a window TALLER
   * than 16:9 crops the LEFT AND RIGHT edges - 72 viewBox units a side at 16:10.
   * The wings sit at those edges, so every wing LABEL is kept inside x 72..1368
   * and a room wall may cross it only by the ~10 units these two do (the logo,
   * the sign and the plate are all centred and stay well inside). */
  anomaly: {
    rect: [62, 320, 130, 130], side: 'n', wallY: 450, door: 100, rm: '301', wing: 'west',
    stop: [100, 470], neonX: 127, neonY: 328, nameY: 424,
    gameEn: 'Anomaly',
    nameKey: 'campus_room_anomaly', nameEn: 'Darkroom',
    descKey: 'campus_desc_anomaly',
    descEn: 'Everything in here matches. One thing does not. Find it before it moves.',
  },
  composure: {
    rect: [62, 490, 130, 130], side: 's', wallY: 490, door: 150, rm: '302', wing: 'west',
    stop: [150, 470], neonX: 127, neonY: 498, nameY: 594,
    gameEn: 'Composure',
    nameKey: 'campus_room_composure', nameEn: 'The Studio',
    descKey: 'campus_desc_composure',
    descEn: 'Slide the picture back together while the room does its best to blur it.',
  },
  /* MISDIRECTION HAS NO ROOM. It was retired from the deal on 2026-08-23
   * (games/registry.js RETIRED_GAMES) and its berth in the old east wing is the
   * front office's now. A room here would be a room nothing can ever open:
   * `isOpenSemester` filters it out of the plan, the timetable never deals it,
   * and its lexicon rows stay in core/lexicon.js for the day a replacement
   * class moves in. The Parlour is not sitting empty; it is gone - but its
   * NUMBER is not: SORT, the substitute, wears the 201 plate by the Main
   * Gate (owner ruling 2026-08-24). */
});

/* ----------------------------------------------------------------------------
 * THE WINGS. A wing is a BLOCK, not a room: it owns a footprint, an alley and a
 * mouth onto the Main Hall, and it holds the rooms whose `wing` names it. The
 * tape comes off exactly when the wing's semester is in the registry's
 * OPEN_SEMESTERS - one set, one truth, so the release gate that keeps a class
 * out of the pool is the same one that keeps its wing sealed.
 * -------------------------------------------------------------------------- */
export const WINGS = Object.freeze({
  /* THE EAST WING IS THE FRONT OFFICE. It holds no classes at all now - Records
   * and the Front Office counter took the two compact rooms, and its caption
   * says so instead of naming a semester it no longer contains. `office` is
   * what tells the plan that: the wing draws the same floor and alley, and the caption is
   * composed from the two rows the office already owns (NO new lexicon keys -
   * lot 2 moves rooms, never the string table). */
  east: {
    semester: 2, roman: 'II', rect: [1240, 360, 160, 220], office: true,
    alley: [1240, 360, 20, 220], mouthX: 1240, mouth: [434, 506],
    labelX: 1314, labelY: 602, sealedTone: 'pink', din: 180,
    nameKey: 'campus_east_wing', nameEn: 'East Wing',
    sealedKey: 'campus_opens_semester_2', sealedEn: 'Opens Semester II',
    sealedDescKey: 'campus_desc_east', sealedDescEn: 'You can hear hammering behind the tape.',
    openDescKey: 'campus_desc_east_open',
    openDescEn: 'The front office. Two counters, one bell, and a queue that is always you.',
  },
  /* THE WEST WING IS A SPUR, not a side street: its alley runs WEST out of the
   * Main Hall on the hall's own centre line (y 450..490) with a deep room above
   * it and a deep room below. Its label hangs ABOVE the block rather than under
   * it, because the block now reaches down to y 620 and the student ID card
   * lives in that corner of the screen. */
  west: {
    semester: 3, roman: 'III', rect: [40, 312, 160, 316],
    alley: [62, 450, 138, 40], mouthX: 200, mouth: [450, 490],
    labelX: 127, labelY: 288, sealedTone: 'dim', din: 210,
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

/** A wing's alley as a plain rect [x,y,w,h] - down for the east, west for the
 * west. Falls back to the old 20-wide column at `alleyX` so a wing that has not
 * declared one still paves something sane. Pure. */
function alleyRect(w) {
  if (w && Array.isArray(w.alley) && w.alley.length === 4) return w.alley;
  const r = (w && w.rect) || [0, 0, 0, 0];
  return [((w && w.alleyX) || r[0]) - 10, r[1], 20, r[3]];
}

/** The mouth's [y1,y2] where the wing opens onto the Main Hall. Pure. */
function mouthSpan(w) {
  const m = (w && Array.isArray(w.mouth) && w.mouth.length === 2) ? w.mouth : [434, 506];
  return m;
}

/** A wing's second caption line. A wing that holds CLASSES is a semester; the
 * front office is not one, so it says what it is instead - composed from the
 * two rows it already owns, because lot 2 moves rooms and never mints a key. */
function wingCaption(w) {
  if (w && w.office) return t('campus_records', 'Records') + ' · ' + t('campus_registrar', 'Front Office');
  return t('semester', 'Semester') + ' ' + ((w && w.roman) || 'I');
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
 * LIGHTS ON - THE ROOM INHERITS THE CARD.
 *
 * The restyle is entirely in styles.css; what lives here is STRUCTURE, and the
 * module's oldest law still holds without an exception: not one line below
 * writes a colour. The per-room gradients are `<linearGradient>` nodes whose
 * `<stop>`s carry a class and a `data-game`, and the stylesheet paints them off
 * `--room-<key>-a/-b/-glow`; the logos are `<image>` nodes; the marquee bulbs,
 * the cabinet trim and the powered-off scanlines are plain rects wearing class
 * names. A mod palette therefore reskins the lit campus for free, exactly the
 * way it already reskinned the blueprint one.
 * -------------------------------------------------------------------------- */

/**
 * Where a class's keyed logo hangs INSIDE its own room, in viewBox units.
 * A BOX, not a size: the `<image>` is `preserveAspectRatio="xMidYMid meet"`, so
 * a wide wordmark and a squarer crest both land centred inside the same box and
 * neither can ever reach the room plate drawn under it - the name and the RM
 * number are the lexicon surface, and a logo that covered them would take the
 * mod's own words off the map.
 *
 * Deliberately NOT a field on ROOMS: that table is frozen geography and this is
 * dressing. A key with no entry (a retired class, a future one whose art has
 * not been drawn) simply gets no `<image>` and keeps the blueprint room.
 */
const LOGO_BOX = Object.freeze({
  /* corridor rooms - 158 of a 220-wide room (~72%), in the band above the plate */
  daily_trigger:   [251, 244, 158, 96],
  deja_vu:         [491, 244, 158, 96],
  impulse_control: [731, 244, 158, 96],
  lost_and_found:  [251, 586, 158, 96],
  /* the two east fronts are WIDER rooms (280 / 260), so their boxes are wider
   * in the same proportion - same band, same 96-unit height, same clearance
   * over the plate. */
  echo:            [980, 244, 200, 96],
  instant_recall:  [992, 586, 196, 96],
  /* the Pool is a WIDE, SHORT building: a letterbox over the water */
  the_deep_end:    [349, 762, 242, 52],
  /* THE WEST WING GETS ITS ART BACK. The first cut refused these two on the
   * evidence - a logo hung in a 112x66 shelf turned the room name into noise
   * and the RM number vanished under the sign's glow. That was a verdict on the
   * ROOM, not on the art, and lot 2 rebuilt the room: 138x130 with the sign at
   * the top, a 54-unit logo band under it and the plate on its own floor. The
   * band is shallower than a corridor room's 96 and the boxes are letterboxes
   * because of it - `xMidYMid meet` fits a wide wordmark into one without ever
   * reaching the words underneath. */
  anomaly:         [72, 352, 110, 54],
  composure:       [72, 522, 110, 54],
});

/**
 * THE ART BASE, resolved ONCE off this module - and it has to be, because a
 * bare `art/campus/...` on an `<image href>` resolves against the DOCUMENT
 * while the preload probe resolves against the MODULE. Those two agree only
 * when the page happens to sit at the web root: mounted anywhere else (the
 * verification rig serves the module under /arc/ and the page at /) the probe
 * passed off the module base while every `<image>` 404'd, and nine rooms drew
 * a broken-image glyph with `data-art` still saying "on". One base, both
 * users, no way for them to disagree again.
 */
const LOGO_DIR = 'art/campus/';
const ART_BASE = (function resolveArtBase() {
  try { return new URL('../' + LOGO_DIR, import.meta.url).href; }
  catch (e) { return LOGO_DIR; }        // no URL/import.meta (a DOM double): relative
}());

/** The keyed logo file for one class. */
function logoUrl(key) { return ART_BASE + 'logo-' + key + '.png'; }

/* THE PEEK PLATE. The one painted picture the plan is allowed to show, and it
 * is not ON the plan: it rides the hover card for the Prize Counter, which is
 * the alley those three service windows stand in. The map stays vector to the
 * last line (nothing raster may be drawn into the SVG - see the header's own
 * ruling on that), and a card that pops beside the cursor is not the map.
 *
 * It lives with the VN plates rather than under art/campus/, because it IS one:
 * the same painting the antechamber's neighbouring windows are cropped from. */
const PEEK_BASE = (function resolvePeekBase() {
  try { return new URL('../art/vn/', import.meta.url).href; }
  catch (e) { return 'art/vn/'; }       // no URL/import.meta (a DOM double)
}());
const PEEK_PRIZES = PEEK_BASE + 'vn-17-prize-alley.png';

/* ----------------------------------------------------------------------------
 * THE IDLE ATTRACT - tunables (Deck VI: demo, don't explain).
 * -------------------------------------------------------------------------- */
export const ATTRACT_IDLE_MS = 25000;   // silence before the school starts showing off
const ATTRACT_LEG_MS = 900;             // one ghost-cursor leg
const ATTRACT_DWELL_MS = 1000;          // how long a room holds its glow
const ATTRACT_LOOP_GAP_MS = 2600;       // dark beat before the show repeats
const ATTRACT_TICK_MS = 50;             // cursor lerp tick (20fps - a hint, not a game)
const ATTRACT_FLIP_MS = 70;             // one split-flap flip
/* THE PHONE HALF-RATE (perf/arcademy-mobile-web). The attract loop is a 20Hz
 * SVG cursor transform plus sign textContent rewritten ~14Hz inside filtered
 * groups - polish that is invisible at phone sizes and expensive on WebKit.
 * On a coarse pointer both tickers run at HALF rate (tick 100ms, flip 140ms);
 * glideCursor derives its step count from the tick, so a glide still takes the
 * same wall time. Probed ONCE at module init (trap 42's own probe pair);
 * desktop evaluates to the untouched constants above. */
const ATTRACT_COARSE = (() => {
  try {
    if (typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches) return true;
  } catch (e) { /* noop */ }
  try {
    return typeof navigator !== 'undefined' && Number(navigator.maxTouchPoints) > 1;
  } catch (e) { /* noop */ }
  return false;
})();
const ATTRACT_TICK_EFF_MS = ATTRACT_COARSE ? ATTRACT_TICK_MS * 2 : ATTRACT_TICK_MS;
const ATTRACT_FLIP_EFF_MS = ATTRACT_COARSE ? ATTRACT_FLIP_MS * 2 : ATTRACT_FLIP_MS;
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
  /* UTC midnight, NOT local: the timetable day key (`core/timetable.js fmtDay`)
   * and the Daily Trigger seed are UTC days. Counting to local midnight let a
   * 23:00 US session and the next morning land on ONE arcademy day. */
  const next = Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate() + 1, 0, 0, 0, 0);
  return Math.max(0, Math.round((next - d.getTime()) / 1000));
}

/** 'HH:MM:SS' for the bell chip. Pure. */
export function bellLabel(totalSec) {
  const s = Math.max(0, Math.round(Number(totalSec) || 0));
  const p = (n) => String(n).padStart(2, '0');
  return p(Math.floor(s / 3600)) + ':' + p(Math.floor((s % 3600) / 60)) + ':' + p(s % 60);
}

/* ============================================================================
 * THE WALK GRAPH - one corridor grammar, and it is THIS one.
 *
 * These four functions used to live inside createCampus() because the nightly
 * route was their only caller. shell/ghosts.js (Campus Presence) walks the same
 * geography with replayed attendees, and a SECOND pathing system is the one
 * thing that could put a student through a wall - so they were lifted out
 * VERBATIM instead of being copied. They are pure (they read the frozen ROOMS
 * table and nothing else), which is also what lets the suite assert leg legality
 * without building a DOM.
 *
 * THE WHOLE MAP, in three runs of floor:
 *   MAIN HALL     x 200..1240, y 430..510   (the corridor every room opens onto)
 *   WEST SPUR     x  62..200,  y 450..490   (the hall carrying on west)
 *   GATE ALLEY    x 700..740,  y 510..730   + the front path down to y 920
 * Every leg any of these functions returns stays inside that union.
 * ==========================================================================*/

/**
 * THE TWO COUNTERS, as walk-graph rooms.
 *
 * Records and the Front Office are drawn by `facility()` below, not by the
 * ROOMS table - they are furniture, never classes, and `campusState` must never
 * see them. But THE WALK (ORIENTATION.md §2) has to reach them: a player who
 * clicks the Records door walks there like they walk to any other room, and one
 * corridor grammar is the whole law (ghosts.js's LAW 2). So the two counters get
 * a table of their own, in the SAME shape ROOMS uses (`rect`/`side`/`door`), and
 * `stopAnchor` / `doorPoint` / `walkLegs` fall through to it.
 *
 * They open WEST onto the east wing's alley (x 1240..1260, mouth y 434..506), so
 * their dwell anchor is the middle of that alley at the door's own y - which the
 * existing three-step grammar reaches with no special case at all: onto the
 * hall's centre line, east along it and through the mouth, then up (or down) the
 * alley to the counter. Nothing here is a second pathing system.
 *
 * `facility()` reads its geometry from this table, so there is exactly one place
 * either counter's rect or door lives.
 */
export const FACILITIES = Object.freeze({
  records: Object.freeze({
    rect: [1260, 380, 108, 84], side: 'w', door: 422, stop: [1250, 422], rm: '001',
  }),
  registrar: Object.freeze({
    rect: [1260, 476, 108, 84], side: 'w', door: 518, stop: [1250, 518], rm: '002',
  }),
  /* THE ANNEX HATCH (ANNEX-OS.md §1). Above the office because the room is
   * below it: the plate marks the stairs, not the floor. Its stop is Records'
   * proven stop - the walk IS the walk to the office door, and the descent
   * starts there. Drawn only when the shell hands the bag (gated build). */
  annex: Object.freeze({
    rect: [1260, 300, 108, 72], side: 'w', door: 336, stop: [1250, 422], rm: '000',
  }),
  /* THE PRIZE COUNTER (economy wave, 2026-08-26). Third counter down the same
   * alley, under the Front Office: it belongs with the other two because it IS
   * the other two - a window with somebody behind it. Same width, same west
   * door, same three-step walk, and its stop simply continues down the alley
   * the way the registrar's continues past Records. Nothing about the pathing
   * is special-cased for it. */
  prizes: Object.freeze({
    rect: [1260, 572, 108, 84], side: 'w', door: 614, stop: [1250, 614], rm: '003',
  }),
});

/** ROOMS first, the two counters second. Pure; undefined for anything else. */
function walkSpec(key) {
  return ROOMS[key] || FACILITIES[key];
}

/** The Main Gate, where every night's route (and every ghost) walks in. */
export const CAMPUS_GATE = Object.freeze([720, 908]);
/** The Main Hall's centre line - the lane the route and the ghosts walk. */
export const CAMPUS_HALL_Y = 470;
/** How long the entry reveal runs. Nothing may animate over it (PRESENCE §4). */
export const CAMPUS_ENTER_MS = 4500;

/**
 * Where tonight's numbered badge stands, and where a ghost dwells: in the
 * corridor just inside the door for a room bolted to the hall, in the wing alley
 * for a wing room (which pins its own `stop`, because the Main Hall is nowhere
 * near its door). Pure.
 */
export function stopAnchor(key) {
  const spec = walkSpec(key) || {};
  if (spec.stop) return spec.stop;
  return spec.side === 'n' ? [spec.door, 447] : [spec.door, 488];
}

/**
 * The point on the room's own wall the door symbol is drawn at - NOT a walkable
 * spot (it is the threshold itself). Presence uses it as a keep-out radius so an
 * encounter never stages on top of a swinging leaf. Pure; null for an unknown key.
 */
export function doorPoint(key) {
  const spec = walkSpec(key);
  if (!spec || spec.door == null) return null;
  if (spec.side === 'n') return [spec.door, spec.wallY != null ? spec.wallY : 430];
  if (spec.side === 'w' || spec.side === 'e') {
    const r = spec.rect || [0, 0, 0, 0];
    return [spec.side === 'w' ? r[0] : r[0] + r[2], spec.door];
  }
  return [spec.door, spec.wallY != null ? spec.wallY : 510];
}

/**
 * The legs the route walks for one stop. A corridor room is one point on the
 * hall's centre line - byte-identical to what this drew before. A wing room
 * turns off at its junction, touches its door and comes back out, so the next
 * leg still starts on the centre line instead of cutting through a wall. Pure.
 */
export function routeLegs(key) {
  const spec = ROOMS[key];
  if (!spec) return [];
  if (spec.via) return [spec.via, stopAnchor(key), spec.via];
  return [[spec.door, CAMPUS_HALL_Y]];
}

/** The nightly route's `d`, gate first. Pure. */
export function routeFor(stops) {
  if (!stops || !stops.length) return '';
  let d = 'M' + CAMPUS_GATE[0] + ',' + CAMPUS_GATE[1]
    + ' L' + CAMPUS_GATE[0] + ',' + CAMPUS_HALL_Y;
  for (const s of stops) {
    for (const leg of routeLegs(s.gameKey)) d += ' L' + leg[0] + ',' + leg[1];
  }
  return d;
}

/**
 * THE GHOST'S LEG LIST: the waypoints a walker crosses getting from `from` to
 * one room's dwell anchor, in the same corridor grammar as routeFor(). Three
 * moves at most and every one of them axis-aligned:
 *   1. step back onto the hall's centre line (a walker leaving a door plaque);
 *   2. walk the centre line to the room's door coordinate;
 *   3. step off it to the dwell anchor.
 * A leg that would be zero-length is dropped, so a walk that is already on the
 * line returns two points, not four. `from` may be any point on the graph -
 * CAMPUS_GATE included, which is why step 1 walks the gate alley vertically.
 * Pure; an unknown room key answers an empty list (the caller skips that leg).
 */
export function walkLegs(from, key) {
  const spec = walkSpec(key);
  if (!spec || !from) return [];
  const anchor = stopAnchor(key);
  // Already standing on the plaque (two classes running back to back in the
  // same room): no legs at all, rather than a pointless round trip to the lane.
  if (Math.abs(from[0] - anchor[0]) < 0.01 && Math.abs(from[1] - anchor[1]) < 0.01) return [];
  const out = [];
  const push = (p) => {
    const last = out.length ? out[out.length - 1] : from;
    if (Math.abs(last[0] - p[0]) < 0.01 && Math.abs(last[1] - p[1]) < 0.01) return;
    out.push([p[0], p[1]]);
  };
  // 1. onto the lane. From the gate that is the vertical run up the gate alley;
  //    from a door plaque it is the two dozen units back into the hall.
  if (Math.abs(from[1] - CAMPUS_HALL_Y) > 0.01) push([from[0], CAMPUS_HALL_Y]);
  // 2. along the lane to the room's own coordinate.
  push([anchor[0], CAMPUS_HALL_Y]);
  // 3. off the lane to the plaque.
  push([anchor[0], anchor[1]]);
  return out;
}

/** The way back out: the lane, then the gate alley, then the front path. Pure. */
export function gateLegs(from) {
  if (!from) return [];
  const out = [];
  const push = (p) => {
    const last = out.length ? out[out.length - 1] : from;
    if (Math.abs(last[0] - p[0]) < 0.01 && Math.abs(last[1] - p[1]) < 0.01) return;
    out.push([p[0], p[1]]);
  };
  if (Math.abs(from[1] - CAMPUS_HALL_Y) > 0.01) push([from[0], CAMPUS_HALL_Y]);
  push([CAMPUS_GATE[0], CAMPUS_HALL_Y]);
  push([CAMPUS_GATE[0], CAMPUS_GATE[1]]);
  return out;
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
/* ----------------------------------------------------------------------------
 * THE CAMPUS HAS TWO SOUNDS: a door, and the pad under the cursor.
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

/** The hover pad is a chip, not a chime: a pointer sweeping the plan must not
 *  play a tune. One cue per 150ms, whatever the mouse is doing. */
const HOVER_GAP_MS = 150;
let lastHoverCue = 0;

/** EMI's hover seam is the DWELL, never the enter (heartbeat wave, 2026-08-25).
 *  A pointer crossing the plan fires mouseenter dozens of times a minute, and
 *  the throttle above only bounds the RATE - the same reason the owner took the
 *  hover cue down 60%. So the moment is "the pointer SETTLED on one room", and
 *  it is cancelled the instant the pointer leaves. The rest of the ration is the
 *  pool's own: odds 0.15, a three-minute cooldown and maxPerSession 2. */
const HOVER_DWELL_MS = 1200;

/** How close to the last bell EMI is allowed to notice, once a night. */
const BELL_NEAR_SEC = 300;

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/** The student ID's crest: an arcade token with a star, DRAWN. The campus has
 *  no logo file and this one is 20x20 of markup (the nine-broken-logos law). */
function idCrestGlyph() {
  const ns = 'http://www.w3.org/2000/svg';
  try {
    const s = document.createElementNS(ns, 'svg');
    s.setAttribute('viewBox', '0 0 20 20');
    s.setAttribute('class', 'id-crestmark');
    s.setAttribute('aria-hidden', 'true');
    const ring = document.createElementNS(ns, 'circle');
    ring.setAttribute('cx', '10'); ring.setAttribute('cy', '10'); ring.setAttribute('r', '9');
    ring.setAttribute('fill', 'none'); ring.setAttribute('stroke', 'currentColor');
    ring.setAttribute('stroke-width', '2');
    const star = document.createElementNS(ns, 'path');
    star.setAttribute('fill', 'currentColor');
    star.setAttribute('d', 'M10 5.5l1.3 2.8 3 .3-2.3 2 .7 3-2.7-1.6-2.7 1.6.7-3-2.3-2 3-.3z');
    s.appendChild(ring); s.appendChild(star);
    return s;
  } catch (e) { return null; }
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
 *   registrar(), boardToggle(expanded), idCard(), idChip()} - `idCard` is
 *   OFFERED by the student ID's own click and Enter (the shell owns the
 *   spotlight, shell/idcard.js, exactly as it owns the Records one) and
 *   `idChip` is the photo consent leaving for the host; both are optional, and
 *   a caller that hands neither gets the furniture card it has always got.
 *   freeSwim is only ever called for a
 *   room whose state carries an `endless` declaration (the shell reads the
 *   manifest, never the campus). boardToggle fires AFTER the hanging board has
 *   been shown/hidden - the shell rolls the flaps (and stamps the store) on
 *   `true`; the campus itself never touches a flap.
 * @param {string=} o.dateSeed  init.utcDateSeed - the idle attract is seeded off
 *   it so every player gets the SAME show tonight (trap 8: UTC seeds content).
 *   Omitted, it falls back to today's UTC date.
 * @param {number=} o.attractIdleMs  test seam; defaults to ATTRACT_IDLE_MS.
 * @param {string=} o.idCardMode  'shown' (default) | 'withheld'. 'withheld'
 *   builds the bottom-left student ID hidden, for Orientation Day's handover
 *   (ORIENTATION.md §3.2). One node either way - see idCardEl().
 * @param {Function=} o.holdAttract  () => boolean. Answered TRUE while a beat
 *   is on stage, and the idle attract then refuses to start and simply re-arms
 *   (ORIENTATION.md §4). Default is a function that always says false, so a
 *   caller that has never heard of a beat behaves exactly as it did.
 * @param {boolean=} o.boardPulse  the timetable has NOT been opened yet today:
 *   the collapsed plaque's clock pulses until the first expand. The shell
 *   decides (it owns the local date + the store); the campus only wears it.
 * @param {Function=} o.seep  THE CHALK GHOST'S DOOR (tell 12). A GETTER that
 *   answers the shell's seep director, or nothing. It is a getter and not a
 *   handle for the same reason every other seam of that director is (trap 73):
 *   the campus is torn down and rebuilt under it. The campus NEVER imports
 *   shell/seep.js - it asks `beat('door_card')` and paints what it is told,
 *   exactly the way the split-flap board takes `misprintFor`. Absent = a school
 *   that is simply quiet, and the card is what it always was.
 * @param {Object=} o.economy   THE TWO CURRENCIES, handed down and never read:
 *   {balance:() => ({t,k}), lever:{positions, get, set, unlocks}}. This file is
 *   under the header law - it imports no store and no bridge - so the wallet
 *   chip, the Prize Counter's window and the Extra Credit lever on the door
 *   card all live entirely on these getters. Absent = none of the three is
 *   mounted, and the campus is byte-for-byte the campus it was.
 * @param {Function=} o.log
 * @returns {{root, boardMount, footMount, update, closeCard, destroy}}
 */
export function createCampus({ state, gameName, banner, stats, reducedMotion, on, log,
  dateSeed, attractIdleMs, boardPulse, idCardMode, holdAttract, post, seep, annex,
  account, economy } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const handlers = on || {};
  /* THE ACCOUNT CHIP (shell/accountchip.js): a host slot in the top-right
   * cluster. `account` = {get, isMobile, onOpenCard, onAction}; `get()` is
   * null on every host that never sent `init.account` (the desktop), and then
   * nothing is mounted. Minted lazily so a late `profile` frame can still
   * bring the chip in through setAccount(). */
  const acctBag = account && typeof account.get === 'function' ? account : null;
  let acctChip = null;
  let topClusterEl = null;
  let gearEl = null;
  function mountAccountChip(a) {
    if (!acctBag || !topClusterEl || acctChip) return;
    try {
      acctChip = createAccountChip({
        t, account: a, isMobile: acctBag.isMobile,
        onOpenCard: acctBag.onOpenCard, onAction: acctBag.onAction, log: say,
      });
      if (acctChip) topClusterEl.insertBefore(acctChip.el, gearEl ? gearEl.nextSibling : null);
    } catch (e) { say('account chip unavailable (' + ((e && e.message) || e) + ')'); acctChip = null; }
  }
  const name = typeof gameName === 'function' ? gameName : (k) => String(k);
  let st = state || campusState({ classes: [], records: {} });
  let cardOpen = false;
  let destroyed = false;
  /* EMI's two campus latches (heartbeat wave). `hoverDwell` is the settle timer
   * behind `campus.roomHover`; `bellNagged` makes `campus.bellNear` a once-per-
   * campus EDGE rather than a thing the 1s bell tick could say sixty times. */
  let hoverDwell = 0;
  let bellNagged = false;
  function clearHoverDwell() {
    if (!hoverDwell) return;
    try { clearTimeout(hoverDwell); } catch (e) { /* noop */ }
    hoverDwell = 0;
  }
  /* PHANTOM POST furniture handles - null when no `post` bag arrived. */
  let mailChip = null;
  let boardProp = null;
  let bugleProp = null;
  /* THE WALLET CHIP's three nodes - null when no `economy` bag arrived. */
  let walletChip = null;
  let walletTicketN = null;
  let walletTokenN = null;

  /** Repaint the wallet chip from the shell's own reader. Guarded end to end:
   *  a chip is furniture, and furniture may never be the thing that throws. */
  function paintWallet() {
    if (!walletChip || !economy || typeof economy.balance !== 'function') return;
    let b = null;
    try { b = economy.balance(); } catch (e) { b = null; }
    const tt = Math.max(0, Math.round(Number(b && b.t) || 0));
    const kk = Math.max(0, Math.round(Number(b && b.k) || 0));
    try { if (walletTicketN) walletTicketN.textContent = String(tt); } catch (e) { /* noop */ }
    try { if (walletTokenN) walletTokenN.textContent = String(kk); } catch (e) { /* noop */ }
  }
  const holdsAttract = typeof holdAttract === 'function' ? holdAttract : () => false;
  /* The seep director, always ASKED and never held (see the `seep` param). */
  const seepDir = typeof seep === 'function' ? seep : () => null;

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
  let revealed = false;
  function finishReveal() {
    if (revealed || destroyed) return;
    revealed = true;
    try { root.classList.remove('enter'); } catch (e) { /* noop */ }
    /* THE ONE HOOK THE STUDENT BODY NEEDS. PRESENCE.md §4: ghosts start AFTER
     * the reveal, never during it - the rooms are still staggering in and a
     * walker crossing that cascade reads as a glitch, not as a classmate. */
    try { if (typeof handlers.revealDone === 'function') handlers.revealDone(); }
    catch (e) { say('revealDone hook threw: ' + ((e && e.message) || e)); }
  }
  /* W3 P0-31: THE CAMPUS WAKING UP. Four and a half seconds of establishing
   * shot used to play in total silence, right after the intro bed had just
   * finished proving the page can make a sound. `campus_wake` is a 4.2s swell
   * on the MUSIC bus that rises under the cascade and is gone before the board
   * deals, once per mount and never twice. Reduced motion has no cascade to
   * score, so it gets no swell either (trap 66: no cues where there is no
   * animation). */
  if (!reducedMotion) sfx('campus_wake', 0.3, { bus: 'music' });
  if (typeof setTimeout === 'function') {
    // Reduced motion has no cascade to wait out (the sheet refuses it), so the
    // hook fires on the next turn rather than four and a half seconds late.
    enterTimer = setTimeout(() => { enterTimer = 0; finishReveal(); },
      reducedMotion ? 0 : CAMPUS_ENTER_MS);
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
  /* THE STUDENT ID's live parts. `idProfile` is the LAST profile the shell
   * handed down (init.profile, then every `profile` frame) - the card paints
   * from it and derives nothing of its own. */
  let idPhotoImg = null;
  let idPhotoEl = null;
  let idChip = null;
  let idNum = null;
  let idProfile = null;
  let idChipState = 'link';
  let idLastTier = 0;
  let bellText = null;

  /* ------------------------------ SVG plan ------------------------------- */
  /* The viewBox is cut to 16:9 (1440x810) around the architecture and SLICES:
   * the plan always fills the frame, whatever the window - dead sky is cropped
   * instead of letterboxed. Content near the top/bottom edges is croppable
   * flavor only (tower cap, quad), never a room or a control.
   *
   * ON A PHONE IT MEETS INSTEAD (the mobile pass, owner bugs A and B). Slice is
   * only ever right while the frame is CLOSE to 16:9, and a phone is not close
   * in either direction: upright it is about 9:19.5 and slice eats two thirds of
   * the width, turned sideways it is about 19.5:9 and slice eats the top and the
   * bottom, which is exactly where The Pool sits - the owner's landscape
   * screenshot had it sheared off at the bottom edge with no way to pan to it.
   * `meet` fits the whole plan and letterboxes, and the bands it leaves are the
   * stage's own dusk-sky gradient, so nothing looks broken. The attribute is
   * re-written on a rotate rather than set once, because a phone that crosses in
   * or out of the rule mid-scene must not be left wearing the other one. */
  const plan = svg('svg', { viewBox: '0 55 1440 810' }, 'campus-plan');
  function fitPlan() {
    try { plan.setAttribute('preserveAspectRatio', isMobile() ? 'xMidYMid meet' : 'xMidYMid slice'); }
    catch (e) { /* the DOM double may not carry attributes - never fatal */ }
  }
  fitPlan();
  const unfit = onDeviceChange(fitPlan);
  plan.setAttribute('aria-label', t('arcademy', 'The Arcademy'));

  /* corridor paving texture (vector, styled from the stylesheet) */
  const defs = svg('defs');
  const pave = svg('pattern', {
    id: 'campusPave', width: 26, height: 26, patternUnits: 'userSpaceOnUse',
  });
  pave.appendChild(svg('line', { x1: 0, y1: 0, x2: 0, y2: 26 }, 'campus-paveline'));
  pave.appendChild(svg('line', { x1: 0, y1: 0, x2: 26, y2: 0 }, 'campus-paveline'));
  defs.appendChild(pave);

  /* THE MIDWAY CARPET - a drawn 48-unit tile of 90s arcade floor: a ground, two
   * confetti triangles, two squiggles, three dots and two dashes. All vector,
   * all class-styled, and NOTHING in it animates: a tiling background that
   * moved would re-raster the whole hall every frame (trap 36's law). The
   * stylesheet keeps the finished layer well under the route and the stops. */
  const carpet = svg('pattern', {
    id: 'campusCarpet', width: 48, height: 48, patternUnits: 'userSpaceOnUse',
  });
  carpet.appendChild(svg('rect', { x: 0, y: 0, width: 48, height: 48 }, 'campus-carpet-ground'));
  carpet.appendChild(svg('path', { d: 'M7,9 L16,9 L11.5,17 Z' }, 'campus-carpet-p'));
  carpet.appendChild(svg('path', { d: 'M33,29 L42,29 L37.5,37 Z' }, 'campus-carpet-p'));
  carpet.appendChild(svg('path', { d: 'M2,33 q5,-6 10,0 t10,0' }, 'campus-carpet-l'));
  carpet.appendChild(svg('path', { d: 'M26,5 q5,6 10,0' }, 'campus-carpet-l'));
  carpet.appendChild(svg('circle', { cx: 41, cy: 13, r: 2.2 }, 'campus-carpet-g'));
  carpet.appendChild(svg('circle', { cx: 21, cy: 24, r: 1.5 }, 'campus-carpet-g'));
  carpet.appendChild(svg('circle', { cx: 6, cy: 44, r: 1.5 }, 'campus-carpet-p'));
  carpet.appendChild(svg('rect', { x: 28, y: 17, width: 6, height: 2 }, 'campus-carpet-g'));
  carpet.appendChild(svg('rect', { x: 13, y: 39, width: 2, height: 6 }, 'campus-carpet-p'));
  defs.appendChild(carpet);

  /* POWERED-OFF GLASS - the scanlines a dark room's dead screen keeps. */
  const scan = svg('pattern', {
    id: 'campusScan', width: 6, height: 6, patternUnits: 'userSpaceOnUse',
  });
  /* y 3, not y 0: a pattern clips to its own tile, so a 1-unit line on the tile
   * edge would draw at half width and the scanlines would read as a grey haze
   * instead of as lines. */
  scan.appendChild(svg('line', { x1: 0, y1: 3, x2: 6, y2: 3 }, 'campus-scanline'));
  defs.appendChild(scan);

  /* ONE VERTICAL GRADIENT PER ROOM - the card, stood up as a cabinet front.
   * The stops carry the class AND the game key, because a <stop> lives in
   * <defs> and can never inherit a custom property from its room group. */
  function roomGradientId(key) { return 'campusRoomG-' + key; }
  Object.keys(ROOMS).forEach((key) => {
    const lg = svg('linearGradient', { id: roomGradientId(key), x1: 0, y1: 0, x2: 0, y2: 1 });
    lg.appendChild(svg('stop', { offset: '0', 'data-game': key }, 'campus-stop-a'));
    lg.appendChild(svg('stop', { offset: '1', 'data-game': key }, 'campus-stop-b'));
    defs.appendChild(lg);
  });

  plan.appendChild(defs);

  /* grounds: trees, paths, lamps, fountain */
  const grounds = svg('g', null, 'campus-grounds');
  [[695, 792], [745, 792]].forEach(([x, y]) => grounds.appendChild(svg('line', { x1: x, y1: y, x2: x, y2: 920 }, 'campus-path-edge')));
  grounds.appendChild(svg('line', { x1: 720, y1: 800, x2: 720, y2: 920, 'stroke-dasharray': '2 10' }, 'campus-path-mid'));
  const trees = svg('g', null, 'campus-trees');
  [[86, 756, 26], [112, 774, 19], [70, 778, 16], [146, 846, 22], [1368, 688, 24], [1394, 712, 15],
   [1322, 856, 26], [1352, 878, 17], [262, 864, 20], [58, 726, 15]]
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
  /* THE TOKEN FOUNTAIN - five coins in the basin ring (every one of them clear
   * of the r9 eye and inside the r26 coping), each with a one-dot highlight so
   * it reads as struck metal rather than as another lamp. */
  [[1078, 852, 2.6], [1094, 856, 2.2], [1082, 834, 2.0], [1100, 844, 2.4], [1074, 840, 1.8]]
    .forEach(([cx, cy, r]) => {
      grounds.appendChild(svg('circle', { cx, cy, r }, 'campus-coin'));
      grounds.appendChild(svg('circle', {
        cx: cx - r * 0.32, cy: cy - r * 0.32, r: r * 0.34,
      }, 'campus-coin-hi'));
    });
  /* ONE glint, on a 17s cycle that is dark for sixteen and a half of them.
   * SPARKLE BURST IS SCARCE BY LAW - a second one would make it wallpaper. */
  grounds.appendChild(svg('path', { d: 'M1100,830 L1100,840 M1095,835 L1105,835' }, 'campus-sparkle'));
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
  /* THE HALL IS CARPETED. Ground -> midway carpet -> paving lines: the carpet
   * lays over the floor fill and the drawn paving still reads on top of it. */
  floors.appendChild(svg('rect', { x: 200, y: 430, width: 1040, height: 80 }, 'campus-ghall'));
  floors.appendChild(svg('rect', { x: 200, y: 430, width: 1040, height: 80, fill: 'url(#campusCarpet)' }, 'campus-carpet'));
  floors.appendChild(svg('rect', { x: 200, y: 430, width: 1040, height: 80, fill: 'url(#campusPave)' }, 'campus-pave'));
  /* THE HALL'S OWN LABEL SITS ABOVE THE ROUTE'S LANE. It used to sit on the
   * corridor's centre line (y 474) and got away with it because no route leg
   * ever ran west of x 330; the west wing's spur is on that line now, so a
   * night that deals a west class drew the marching pink dashes straight
   * through the words. y 452 is the same floor, one row up. */
  floors.appendChild(svgText(252, 452, 'campus-rsub start wide', t('campus_main_hall', 'Main Hall').toUpperCase(), { 'text-anchor': 'start' }));
  floors.appendChild(svg('rect', { x: 460, y: 510, width: 240, height: 220 }, 'campus-ghall'));
  floors.appendChild(svg('rect', { x: 460, y: 510, width: 240, height: 220, fill: 'url(#campusCarpet)' }, 'campus-carpet'));
  floors.appendChild(svg('rect', { x: 460, y: 510, width: 240, height: 220, fill: 'url(#campusPave)' }, 'campus-pave'));
  /* THE GATE ALLEY HAS A FLOOR. x 700..740 is the run between the Entrance
   * Hall's east wall and the Sorting Room's west one, and the nightly route has
   * always walked it (M720,908 straight up to the Main Hall) - but until lot 3
   * built a room on the hall's west span there was a hall under it, and after
   * lot 3 there was nothing: the marching pink dashes climbed a 40-unit strip
   * of open sky between two buildings. Paved, it is what the plan always meant
   * it to be, a front walk. CORRIDOR STONE, NOT CARPET: the midway tile is a
   * 48-unit pattern and a 40-wide slot of it reads as noise, and this is the
   * one run of the school a player is meant to walk THROUGH, never stand in. */
  floors.appendChild(svg('rect', { x: 700, y: 510, width: 40, height: 220 }, 'campus-ghall'));
  floors.appendChild(svg('rect', { x: 700, y: 510, width: 40, height: 220, fill: 'url(#campusPave)' }, 'campus-pave'));
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
      /* THE WING'S OWN ALLEY - the spur every room in here opens onto. It is a
       * RECT, not a column: the east wing's runs down (20 x 220) and the west
       * wing's runs west (146 x 40, on the Main Hall's own centre line), and
       * carrying the whole rect in the wing means nothing below has to know
       * which way a given wing points. */
      const [ax, ay, aw, ah] = alleyRect(w);
      g.appendChild(svg('rect', { x: ax, y: ay, width: aw, height: ah }, 'campus-ghall'));
      g.appendChild(svg('rect', { x: ax, y: ay, width: aw, height: ah, fill: 'url(#campusPave)' }, 'campus-pave'));
      /* TWO LINES, not one: "EAST WING · SEMESTER II" is 184px of tracked mono
       * and would run off the cropped edge of the plan (see THE SAFE BAND). */
      g.appendChild(svgText(w.labelX, w.labelY, 'campus-rsub', t(w.nameKey, w.nameEn).toUpperCase()));
      /* ONE LINE FOR THE OFFICE. A wing that holds CLASSES names its semester
       * under its own name; the front office would only be repeating the two
       * signs standing twenty units above it (RECORDS, FRONT OFFICE), so it says
       * what it is and stops. The pair is still the hover card's status line -
       * wingCaption() is the one source for both. */
      if (!w.office) {
        g.appendChild(svgText(w.labelX, w.labelY + 15, 'campus-rsub tiny', wingCaption(w).toUpperCase()));
      }
      /* An open wing is scenery, not a door: the ROOMS take the clicks. It keeps
       * its hover card so the alley still says where you are. */
      attachTip(g, () => ({
        name: t(w.nameKey, w.nameEn),
        status: wingCaption(w),
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
    /* THE WALL TAPE IS A CHEQUER. One stroked line can only ever be a dashed
     * line, so the tape is TWO thin rows half a dash out of phase - which is
     * the pink checkerboard an arcade tapes a doorway with. The second row is
     * a new node; the first keeps its own coordinates exactly. */
    /* Centred on the MOUTH, so a wing whose spur sits low in the corridor is
     * taped across its own doorway rather than across the wall beside it. */
    const my = (mouthSpan(w)[0] + mouthSpan(w)[1]) / 2;
    g.appendChild(svg('line', { x1: mx, y1: my - 14, x2: mx, y2: my + 14 }, 'campus-tape2'));
    g.appendChild(svg('line', { x1: mx + 2, y1: my - 14, x2: mx + 2, y2: my + 14 }, 'campus-tape2 b'));
    g.appendChild(svg('line', { x1: mx - 7, y1: my - 8, x2: mx + 7, y2: my + 8 }, 'campus-tape'));
    g.appendChild(svg('line', { x1: mx + 7, y1: my - 8, x2: mx - 7, y2: my + 8 }, 'campus-tape'));
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

  /* THE STUDENT BODY'S LAYER - a SIBLING of the route, mounted here and nowhere
   * else (PRESENCE.md §4): above the floor and the carpet, UNDER the rooms and
   * their door arcs, so a ghost walks the corridor and never over a door leaf.
   * The campus owns the node and nothing else about the feature - shell/ghosts.js
   * fills it, and a campus built with no presence data simply carries an empty
   * group. It is `pointer-events:none` in the sheet (trap 59's law: a layer over
   * the plan that took clicks would eat every room). */
  const ghostLayer = svg('g', null, 'campus-students');
  ghostLayer.setAttribute('aria-hidden', 'true');
  plan.appendChild(ghostLayer);

  /* THE WALKER'S LAYER (ORIENTATION.md §2.1) - the player miniature and its
   * gold trace. A SIBLING of the ghost layer and mounted immediately after it,
   * which is the whole z-order story: YOU are drawn in front of the crowd, and
   * both of you stay under the rooms, their door arcs, the stop badges, the
   * tooltip and the card scrim - so a walker can never cover a door you are
   * about to click. `pointer-events:none` in the sheet, for the ghost layer's
   * reason (trap 59): a full-plan layer that took clicks would eat every room.
   * The campus owns the node and nothing else about the feature - shell/walk.js
   * fills it, and a campus built without a walker carries an empty group. */
  const walkLayer = svg('g', null, 'campus-walker');
  walkLayer.setAttribute('aria-hidden', 'true');
  plan.appendChild(walkLayer);

  /* ------------------------------ rooms ---------------------------------- */
  /* A door is the architect's symbol: a gap in the wall plus the leaf pivoting
   * on one jamb. `spec.door` is the coordinate ALONG that wall - an x on the
   * corridor's north (y 430) or south (y 510) wall, a y on a wing room's own
   * west/east wall. A wing leaf swings INTO the room, because the wing alley is
   * only 20 wide and a 24-unit swing would cross it. */
  function doorFor(spec) {
    const d = spec.door;
    /* WHICH WALL. 430 and 510 are the MAIN HALL's own walls, and every corridor
     * room takes them by default; a room that opens onto a horizontal wing spur
     * instead names its wall with `wallY` and gets the identical symbol there -
     * gap plus a leaf swinging out into the hall it opens on. */
    if (spec.side === 'n') {
      const wy = spec.wallY != null ? spec.wallY : 430;
      return [
        svg('line', { x1: d - 12, y1: wy, x2: d + 12, y2: wy }, 'campus-gap'),
        svg('path', { d: 'M' + (d + 12) + ',' + wy + ' A24,24 0 0 1 ' + (d - 12) + ',' + (wy + 24) + ' L' + (d - 12) + ',' + wy }, 'campus-door'),
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
    const wy = spec.wallY != null ? spec.wallY : 510;
    return [
      svg('line', { x1: d - 12, y1: wy, x2: d + 12, y2: wy }, 'campus-gap'),
      svg('path', { d: 'M' + (d + 12) + ',' + wy + ' A24,24 0 0 0 ' + (d - 12) + ',' + (wy - 24) + ' L' + (d - 12) + ',' + wy }, 'campus-door'),
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
    } else if (key === 'sort') {
      /* A THREE-CARD FAN in the logo band (SORT has no keyed art yet, so the
         fan is the room's interim art): the middle card square to the room,
         its neighbours tipped left and right - the two piles and the one you
         are holding. Scaled up from the parlour original to storefront size;
         the rotations stay around each card's own centre. */
      [[782, -14], [840, 0], [898, 14]].forEach(([cx, deg]) => {
        const card = svg('rect', { x: cx - 21, y: 595, width: 42, height: 58, rx: 3 }, 'campus-furnf');
        if (deg) card.setAttribute('transform', 'rotate(' + deg + ' ' + cx + ' 624)');
        put(card);
      });
      // the felt line the fan sits on
      put(svg('line', { x1: 762, y1: 668, x2: 918, y2: 668 }, 'campus-furn'));
    } else if (key === 'echo') {
      // the ring, laid out along the top wall: six pads and the room's own line
      [1013, 1037, 1061, 1085, 1109, 1133].forEach((x) => put(svg('rect', { x, y: 220, width: 14, height: 14 }, 'campus-furnf')));
      put(svg('line', { x1: 1013, y1: 240, x2: 1147, y2: 240 }, 'campus-furn'));
    } else if (key === 'instant_recall') {
      // the WALL: five frames on a rail, under the plate
      [1019, 1049, 1079, 1109, 1139].forEach((x) => put(svg('rect', { x, y: 520, width: 22, height: 16 }, 'campus-furnf')));
      put(svg('line', { x1: 1019, y1: 540, x2: 1161, y2: 540, 'stroke-dasharray': '4 6' }, 'campus-furn'));
    } else if (key === 'anomaly') {
      // a contact sheet: eight identical frames, one of them isn't
      [368, 382].forEach((y) => [101, 116, 131, 146].forEach((x) => put(svg('rect', { x, y, width: 6, height: 6 }, 'campus-furnf'))));
    } else if (key === 'composure') {
      // a sliding frame, three across
      put(svg('rect', { x: 109, y: 540, width: 36, height: 18 }, 'campus-furn'));
      [121, 133].forEach((x) => put(svg('line', { x1: x, y1: 540, x2: x, y2: 558 }, 'campus-furn')));
      put(svg('line', { x1: 109, y1: 549, x2: 145, y2: 549 }, 'campus-furn'));
    }
  }

  function buildClassRoom(key) {
    const spec = ROOMS[key];
    const [x, y, w, h] = spec.rect;
    const g = svg('g', null, 'campus-room');
    /* THE ROOM IS THE CARD. `data-game` is the only new attribute update() has
     * to leave alone, and it does - update() rewrites `class`, never this. It
     * hands the room its three identity tokens in the stylesheet. */
    g.setAttribute('data-game', key);
    /* EVERY GAME ROOM IS A FULL ROOM NOW (lot 2). The old `data-wing` hook that
     * dimmed a cupboard's sign and withheld its logo is gone from here - it was
     * a compromise for a 112x66 room and there are none left. The stylesheet
     * still owns the compact treatment; the FRONT OFFICE wears it (see
     * facility({compact:true}) and `[data-compact]`), which is what that rule
     * always meant. */
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-gfloor'));
    /* the cabinet front: a second floor rect wearing the room's own gradient.
     * `fill` is a REFERENCE, not a hue - the stops are painted from CSS. This
     * is the same seam .campus-pave has used since the first campus. */
    g.appendChild(svg('rect', {
      x, y, width: w, height: h, fill: 'url(#' + roomGradientId(key) + ')',
    }, 'campus-cabinet'));
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-lit'));
    furnitureFor(key, g);
    /* THE LOGO - over the furniture, under every label. Optional: the art probe
     * below flips the stage to data-art="off" and CSS drops all of these. */
    const box = LOGO_BOX[key];
    if (box) {
      const logo = svg('image', {
        x: box[0], y: box[1], width: box[2], height: box[3],
        href: logoUrl(key),
        preserveAspectRatio: 'xMidYMid meet',
      }, 'campus-logo');
      logo.setAttribute('pointer-events', 'none');
      g.appendChild(logo);
    }
    /* the lit cabinet's bezel (open/retake only - CSS holds it at 0 otherwise)
     * and the powered-off cabinet's scanlines (dark only, same deal). */
    g.appendChild(svg('rect', {
      x: x + 5, y: y + 5, width: Math.max(0, w - 10), height: Math.max(0, h - 10), rx: 3,
    }, 'campus-trim'));
    g.appendChild(svg('rect', {
      x, y, width: w, height: h, fill: 'url(#campusScan)',
    }, 'campus-scan'));
    /* The sign's centre line: the door x for a corridor room, an explicit pin
     * for a room whose door is off-centre in its own wall (the west wing's
     * two). Unchanged for every Semester-1 room. */
    const signX = spec.neonX != null ? spec.neonX : spec.door;
    const ping = svg('circle', { cx: signX, cy: y + h / 2, r: 12 }, 'campus-ping');
    g.appendChild(ping);
    // A room bolted to the corridor derives its label row from `side`; a DETACHED
    // building (the Pool) and a WING room pin their own, because y + 46 would land
    // in the lawn or straight through the sign.
    const nameY = spec.nameY != null ? spec.nameY : (spec.side === 'n' ? y + 156 : y + 46);
    g.appendChild(svgText(x + w / 2, nameY, 'campus-rname', t(spec.nameKey, spec.nameEn).toUpperCase()));
    /* THE NUMBER ROW IS SIZED BY THE ROOM, not by which wing it is in. A wide
     * front carries "RM 202 · ECHO"; a deeper, narrower room carries the number
     * alone, because a mod may re-voice a class into something long and 22
     * characters of tracked mono is 145 units - wider than the west wing's own
     * rooms. The neon sign, the hover card, the door card and the hanging board
     * all still name the class either way. */
    const tight = w < 170;
    /* THE SEEP READS THIS NODE (shell/seep.js, tell 02 "The File Name"): the
     * plate is what flashes the room's bare DEV KEY. It is kept on `roomRefs` so
     * the director never has to guess at a selector - and update() rewrites the
     * room's `class`, never this text, so a plate restored after a 90ms flash
     * cannot be stomped by a repaint. */
    const sub = svgText(x + w / 2, nameY + (tight ? 16 : 18),
      tight ? 'campus-rsub tiny' : 'campus-rsub',
      (tight
        ? t('campus_rm', 'RM') + ' ' + spec.rm
        : t('campus_rm', 'RM') + ' ' + spec.rm + ' · ' + name(key)).toUpperCase());
    g.appendChild(sub);
    doorFor(spec).forEach((n) => g.appendChild(n));
    const neonY = spec.neonY != null ? spec.neonY : (spec.side === 'n' ? 398 : 694);
    const neon = svg('g', null, 'campus-neon');
    const neonRect = svg('rect', { x: signX - 47, y: neonY, width: 94, height: 16, rx: 3 });
    const neonText = svgText(signX, neonY + 11, null, '');
    neon.appendChild(neonRect);
    neon.appendChild(neonText);
    /* THE MARQUEE BULBS. Same rect, deliberately WITHOUT the sign's rx: an
     * un-rounded 94x16 has a perimeter of exactly 220, which divides by the
     * stylesheet's 11-unit bulb pitch into twenty dots with no seam at the
     * path start. The chase itself is one CSS animation shared by every open
     * room - there is no per-bulb node and no JS timer anywhere near it. */
    neon.appendChild(svg('rect', {
      x: signX - 47, y: neonY, width: 94, height: 16,
    }, 'campus-marquee'));
    g.appendChild(neon);
    roomRefs[key] = { g, neon, neonText, ping, spec, sub };
    g.addEventListener('click', () => openClassCard(key));
    // EMI SEAM: the pointer SETTLED on this room (HOVER_DWELL_MS), not crossed it.
    attachTip(g, () => classTip(key), () => {
      try { fireMoment('campus.roomHover', { gameKey: key, inClass: false }); } catch (e) { /* noop */ }
    });
    plan.appendChild(g);
    return g;
  }
  /* A CLOSED semester has no rooms at all - not dark ones. Its games are absent
   * from the registry pool too (games/registry.js OPEN_SEMESTERS), so the two
   * halves of the release gate can never disagree. */
  Object.keys(ROOMS).filter(isOpenSemester)
    .forEach((key, i) => stag(buildClassRoom(key), 250 + i * 110));

  /* ------------------------------ facilities ----------------------------- */
  /**
   * A UTILITY ROOM IS A SIGN, NOT A CABINET. A facility never wears the card
   * treatment a class gets (no gradient, no logo, no marquee, no mood) - it is
   * lit lavender, it has a name and it opens.
   *
   * `compact` is the FRONT OFFICE rung, and it is the honest heir of the old
   * `data-wing` hook: a small room whose sign, name and number stack inside 84
   * units. It is a property of the ROOM's size, never of which wing it stands
   * in, and the one thing the stylesheet is told - it holds the sign's bloom in
   * so the plate under it survives. A compact facility also carries a LIT SIGN
   * of its own, which a big facility never needed: a room this small is read at
   * map distance by its sign first.
   *
   * @param {Object} o {rect, door, side, wallY, name, sub, sign, rm, compact,
   *   nameY, neonY, onClick, tip}
   */
  function facility(o) {
    const [x, y, w, h] = o.rect;
    const g = svg('g', null, 'campus-room facility');
    if (o.compact) g.setAttribute('data-compact', '1');
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-gfloor'));
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-lit'));
    const cx = x + w / 2;
    if (o.sign) {
      const neonY = o.neonY != null ? o.neonY : y + 8;
      const neon = svg('g', null, 'campus-neon');
      neon.appendChild(svg('rect', { x: cx - 47, y: neonY, width: 94, height: 16, rx: 3 }));
      neon.appendChild(svgText(cx, neonY + 11, null, o.sign.toUpperCase()));
      /* No marquee rect: the bulb chase is a CABINET's, and an office that
       * chased its bulbs would be advertising a class it does not teach. */
      /* AND ONE THAT IS NOT LIT. A facility whose room is shut keeps its sign
       * standing - a landmark you can no longer use is still the landmark you
       * navigate by - but the power to it is off, which is two inline
       * properties rather than a stylesheet rule: the tube dims and the bloom
       * around it goes, because bloom is the sign being ON. */
      if (o.signOff) {
        try {
          neon.style.setProperty('opacity', '.32');
          neon.style.setProperty('filter', 'none');
        } catch (e) { /* noop */ }
      }
      g.appendChild(neon);
    }
    const nameY = o.nameY != null ? o.nameY : (o.side === 'n' ? y + 156 : y + 176);
    /* THE COMPACT PLATE FITS ITS ROOM. A compact facility is 108 units wide and
     * `.campus-rname` is 13.5px on .14em of tracking, so ~10 uppercase glyphs is
     * all that fits: "FRONT OFFICE" (the ex-Registrar, renamed 2026-08-24) is
     * 114 units of ink and its last letter would land past the plan's right
     * SAFE BAND edge (x 1368) on any window taller than 16:9. One step down on
     * the size, measured by name LENGTH so a mod-skinned name gets the same
     * treatment, keeps the whole plate inside the room. */
    const longName = !!o.compact && String(o.name || '').length > 10;
    g.appendChild(svgText(cx, nameY, 'campus-rname' + (longName ? ' tight' : ''),
      String(o.name || '').toUpperCase()));
    const sub = o.rm
      ? (t('campus_rm', 'RM') + ' ' + o.rm + (o.sub ? ' · ' + o.sub : ''))
      : (o.sub || '');
    if (sub) {
      g.appendChild(svgText(cx, nameY + (o.compact ? 16 : 18),
        o.compact ? 'campus-rsub tiny' : 'campus-rsub', sub.toUpperCase()));
    }
    if (o.door != null) doorFor({ door: o.door, side: o.side, wallY: o.wallY, rect: o.rect }).forEach((n) => g.appendChild(n));
    if (o.onClick) g.addEventListener('click', o.onClick);
    if (o.tip) attachTip(g, o.tip);
    plan.appendChild(g);
    return g;
  }

  /* ---- THE FRONT OFFICE -----------------------------------------------------
   * Two counters at the east end of the hall, in the wing that used to be a
   * semester. They are the SMALL rooms on purpose (owner ruling, lot 2): a
   * utility only ever needed a sign, and the fronts they used to hold are Echo's
   * and Instant Recall's now. Both open WEST onto the office alley, both keep
   * their handler, their mood and their tip exactly as they were - the geography
   * moved, nothing about what they DO did. */

  /* Records (the punch-card wall + the report card) - office, upper counter */
  const recordsG = facility({
    rect: FACILITIES.records.rect, door: FACILITIES.records.door,
    side: FACILITIES.records.side, compact: true,
    neonY: 388, nameY: 426,
    sign: t('report_card', 'Report Card'),
    name: t('campus_records', 'Records'),
    rm: FACILITIES.records.rm,
    onClick: () => { if (handlers.records) handlers.records(); },
    tip: () => ({
      name: t('campus_records', 'Records'),
      // THE OFFICE, not just the report card: the punch-card wall lives here
      // (PUNCHCARD §6) and the card should say so before the door is opened.
      status: t('punchcard', 'Punch Card') + ' · ' + t('report_card', 'Report Card'),
      desc: t('campus_desc_records', 'Report card, attendance ledger, grades. Your whole term, in ink.'),
    }),
  });
  /* THE TROPHY-CASE LIGHT. Records keeps the whole facility contract (same
   * class, same click, same tip) and gains ONE modifier the stylesheet uses to
   * warm its existing bloom rect from lavender to gold. update() never touches
   * a facility's class - roomRefs holds game rooms only - so this is stable. */
  recordsG.setAttribute('class', 'campus-room facility records');
  /* a bank of drawers along the counter's own wall - the whole term, in ink */
  [1278, 1302, 1326].forEach((x) => recordsG.appendChild(svg('rect', { x, y: 448, width: 20, height: 12 }, 'campus-furnf')));

  /* Front Office (ex-Registrar; settings) - office, lower counter */
  const regG = facility({
    rect: FACILITIES.registrar.rect, door: FACILITIES.registrar.door,
    side: FACILITIES.registrar.side, compact: true,
    neonY: 484, nameY: 522,
    sign: t('settings', 'Settings'),
    name: t('campus_registrar', 'Front Office'),
    rm: FACILITIES.registrar.rm,
    /* THE ROOM CLICK IS NOT THE GEAR (ORIENTATION.md §2.3). The topbar gear is
     * the SHORTCUT and must never walk; the Front Office door is the diegetic
     * way in and does. Both still end at the same page, so a caller that only
     * knows `registrar` (every caller before The Walk, and every suite) is
     * unchanged - `registrarRoom` is an OPT-IN override, never a requirement. */
    onClick: () => {
      const fn = handlers.registrarRoom || handlers.registrar;
      if (fn) fn();
    },
    tip: () => ({
      name: t('campus_registrar', 'Front Office'),
      status: t('settings', 'Settings'),
      desc: t('campus_desc_registrar', 'Every setting is a form. Every consent, a waiver with a stamp.'),
    }),
  });
  /* the counter, the bell and the stamp */
  regG.appendChild(svg('rect', { x: 1276, y: 548, width: 76, height: 7 }, 'campus-furnf'));
  regG.appendChild(svg('circle', { cx: 1282, cy: 543, r: 3 }, 'campus-furn'));
  regG.appendChild(svg('rect', { x: 1338, y: 540, width: 10, height: 7 }, 'campus-furnf'));
  stag(recordsG, 700);
  stag(regG, 780);

  /* THE ANNEX HATCH. The post-bag contract, again: campus mounts the node
   * only when the shell hands the bag, and the shell keeps the gate (reveal
   * seen + a first visit made), the store, and every byte of state. A player
   * who has never been downstairs never sees this group exist. */
  let annexG = null;
  if (annex && typeof annex.open === 'function') {
    annexG = facility({
      rect: FACILITIES.annex.rect, door: FACILITIES.annex.door,
      side: FACILITIES.annex.side, compact: true,
      nameY: 340,
      name: t('campus_annex', 'Records Annex'),
      rm: FACILITIES.annex.rm,
      onClick: () => annex.open(),
      tip: () => ({
        name: t('campus_annex', 'Records Annex'),
        status: t('campus_annex_status', 'Stairs down'),
        desc: t('campus_desc_annex', 'Under the office. The lights are off down there. The screens are not.'),
      }),
    });
    annexG.setAttribute('class', 'campus-room facility annex');
    stag(annexG, 820);
  }

  /* THE PRIZE COUNTER. THIRD WINDOW IN THE ALLEY, AND IT IS ALWAYS THERE.
   *
   * It used to mount only when the shell handed an `economy` bag, which made
   * the plan tell a lie about the building: the alley has three service windows
   * painted in it and, on a host with no economy, only two of them were rooms.
   * A landmark that comes and goes is not a landmark, it is a menu item, and
   * the reason this counter got a painted antechamber at all is that it is a
   * PLACE - somewhere in the school you can be standing.
   *
   * So the bag no longer decides whether the room exists. It decides whether
   * the room is OPEN, which is the distinction the east wing has always drawn:
   *   LIT       sign burning, parcels on the shelf, "Open late", and the click
   *             WALKS (handlers.prizes, which is the walk and then the booth).
   *   SHUTTERED sign standing but dark, shutter down over the shelf, "Closed",
   *             and the click raises the sealed card, whose own lockedClick is
   *             the seam every other shut door on this campus already fires.
   *             No walk: you do not walk somebody the length of a school to
   *             stand in front of a window that is closed.
   *
   * `FACILITIES.prizes` is untouched either way. The rect, the door and the
   * walk's stop are geography, and geography does not keep opening hours. */
  const prizesLit = !!economy;
  const prizesG = facility({
    rect: FACILITIES.prizes.rect, door: FACILITIES.prizes.door,
    side: FACILITIES.prizes.side, compact: true,
    neonY: 580, nameY: 618,
    sign: t('wallet_tickets', 'Tickets'),
    signOff: !prizesLit,
    name: t('campus_room_prizes', 'Prize Counter'),
    rm: FACILITIES.prizes.rm,
    onClick: () => {
      if (prizesLit) { if (handlers.prizes) handlers.prizes(); return; }
      /* The sealed card, exactly as the two shut wings raise it: the name, one
       * line, a GO button that says Sealed and does nothing, and the EMI seam
       * fired from inside `openFacilityCard` rather than from here - so a shut
       * window and a taped-off wing are one event and never two. */
      openFacilityCard({
        name: t('campus_room_prizes', 'Prize Counter'),
        status: t('prize_closed', 'Closed'),
        desc: t('campus_desc_prizes_shut',
          'Shutter down over the window, parcels still stacked behind it. Back another night.'),
        sealed: true,
      });
    },
    tip: () => (prizesLit ? {
      name: t('campus_room_prizes', 'Prize Counter'),
      status: t('campus_prizes_status', 'Open late'),
      desc: t('campus_desc_prizes',
        'Tickets on the shelf, tokens in the case. Somebody is always restocking.'),
      /* THE PEEK, and this is the only card on the plan that carries one: the
       * alley these three windows stand in, painted. It rides the card rather
       * than the walk because walk.js has no surface to show a plate on - it is
       * an SVG miniature of this same map with a line growing across it, and a
       * painting inside a miniature of a map is a second camera nobody asked
       * for. A card beside the cursor is the honest place to say what it looks
       * like over there. */
      art: PEEK_PRIZES,
    } : {
      name: t('campus_room_prizes', 'Prize Counter'),
      status: t('prize_closed', 'Closed'),
      desc: t('campus_desc_prizes_shut',
        'Shutter down over the window, parcels still stacked behind it. Back another night.'),
      /* No peek on a shut window. The painting is of a lit alley, and a picture
       * of the place being open is the wrong thing to hand somebody the moment
       * they find out that it is not. */
    }),
  });
  /* THE LIT WINDOW. Records gets the trophy-case gold through this same one
   * modifier; the counter takes its own so the stylesheet can warm it without
   * either of them borrowing the other's rule. `is-shut` is that same hook one
   * step down, and nothing in the sheet needs to know it exists for the room to
   * read as closed - the drawing below does that on its own. */
  prizesG.setAttribute('class', 'campus-room facility prizes' + (prizesLit ? '' : ' is-shut'));
  /* the shelf behind the glass - three parcels in a row on the back wall */
  [1280, 1304, 1328].forEach((x) => prizesG.appendChild(
    svg('rect', { x, y: 644, width: 18, height: 13 }, 'campus-furnf')));
  /* THE SHUTTER, and it goes on AFTER the parcels so the parcels are behind it,
   * which is exactly what the card claims ("parcels still stacked behind it").
   * Vector like every other line on this plan, and inline-coloured rather than
   * classed: it is one room's one state, and it should not cost a stylesheet
   * rule that a later sheet could fight over. */
  if (!prizesLit) {
    prizesG.appendChild(svg('rect', {
      x: 1274, y: 637, width: 80, height: 21, rx: 1.5,
      fill: '#0D0D1A', stroke: '#3B3455', 'stroke-width': 0.8,
    }));
    /* the slats, which are the thing that makes a dark box read as a shutter */
    [641.5, 646, 650.5, 655].forEach((y) => prizesG.appendChild(svg('rect', {
      x: 1276, y, width: 76, height: 0.9, fill: '#3B3455', opacity: 0.55,
    })));
    /* and the pull handle along the bottom rail */
    prizesG.appendChild(svg('rect', {
      x: 1300, y: 657, width: 28, height: 1.6, fill: '#6A5F8C',
    }));
  }
  stag(prizesG, 860);

  /* Entrance hall dressing (notice board, trophy case, admissions desk, crest) */
  const hall = svg('g', null, 'campus-halldress');
  hall.appendChild(svg('circle', { cx: 582, cy: 622, r: 46, 'stroke-dasharray': '4 6' }, 'campus-crestring'));
  hall.appendChild(svgText(582, 638, 'campus-crestA', 'A'));
  /* THE NOTICE BOARD is corkboard under a lamp now: a felt backing behind the
   * existing board, and a gold plate instead of a chalk one. The board rect,
   * its four pins and their coordinates are untouched. */
  hall.appendChild(svg('rect', { x: 496, y: 512, width: 138, height: 18 }, 'campus-felt'));
  hall.appendChild(svg('rect', { x: 500, y: 516, width: 130, height: 10 }, 'campus-furnf'));
  [[516, 'p1'], [548, 'p2'], [583, 'p3'], [612, 'p4']].forEach(([cx, k]) => hall.appendChild(svg('circle', { cx, cy: 521, r: 1.6 }, 'campus-pin ' + k)));
  hall.appendChild(svgText(565, 542, 'campus-rsub tiny gold', t('campus_notice_board', 'Notice Board').toUpperCase()));
  hall.appendChild(svg('rect', { x: 662, y: 548, width: 12, height: 150 }, 'campus-furnf'));
  [[566, 'gold'], [596, 'gold'], [626, 'lav'], [656, 'dim']].forEach(([cy, k]) => hall.appendChild(svg('circle', { cx: 668, cy, r: 2.4 }, 'campus-trophy ' + k)));
  hall.appendChild(svgText(639, 628, 'campus-rsub tiny', t('campus_trophy_case', 'Trophy Case').toUpperCase(), { transform: 'rotate(-90 639 628)' }));
  /* THE ADMISSIONS DESK, SHIFTED EIGHT UNITS EAST - and its rotated sign with
   * it. The label used to hang at x 452, twenty units clear of the desk and
   * twenty units OUTSIDE the hall, which the old 480-wide hall could afford
   * because nothing stood there. The Pool's covered walk does: its two roof
   * lines run x 443..457 all the way down to the water, and the sign was
   * standing in the middle of the walkway. Inside the wall it goes; the desk
   * moves with it so the sign still reads as the counter's own. */
  hall.appendChild(svg('rect', { x: 480, y: 600, width: 46, height: 86 }, 'campus-furnf'));
  hall.appendChild(svg('line', { x1: 488, y1: 616, x2: 518, y2: 616 }, 'campus-furn'));
  hall.appendChild(svg('line', { x1: 488, y1: 632, x2: 518, y2: 632 }, 'campus-furn'));
  hall.appendChild(svg('circle', { cx: 503, cy: 662, r: 3 }, 'campus-lamp'));
  hall.appendChild(svgText(472, 644, 'campus-rsub tiny', t('campus_admissions', 'Admissions').toUpperCase(), { transform: 'rotate(-90 472 644)' }));
  plan.appendChild(stag(hall, 600));

  /* Entrance hall as a facility hit-area (notices card) */
  const hallG = svg('g', null, 'campus-room facility');
  hallG.appendChild(svg('rect', { x: 460, y: 510, width: 240, height: 220, fill: 'transparent', stroke: 'none' }, 'campus-hit'));
  hallG.appendChild(svgText(582, 704, 'campus-rname dim', t('campus_entrance_hall', 'Entrance Hall').toUpperCase()));
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
  /* THE OPENING NOW RUNS TO THE ALLEY'S FAR JAMB. It used to stop at 738 and
   * nobody could tell, because until lot 3 there was 200 more units of Entrance
   * Hall behind it; with the Sorting Room's west wall standing at 740 those two
   * units were a sliver of corridor wall left hanging across the mouth of the
   * gate alley. Flush with the jamb, the hall's own 38-unit doorway and the
   * alley's 40 read as the one wide opening they are. */
  plan.appendChild(svg('line', { x1: 662, y1: 510, x2: 740, y2: 510 }, 'campus-gap'));
  plan.appendChild(svg('line', { x1: 662, y1: 510, x2: 740, y2: 510, 'stroke-dasharray': '3 6' }, 'campus-opening'));
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
    /* The cut is as tall as the ALLEY behind it, not as tall as the corridor:
     * the west wing's spur is 40 units of hall, and a 72-unit hole in the wall
     * in front of it would be a wall with nothing behind two thirds of it. */
    const [my1, my2] = mouthSpan(WINGS[id]);
    plan.appendChild(svg('line', { x1: mx, y1: my1, x2: mx, y2: my2 }, 'campus-gap'));
    plan.appendChild(svg('line', { x1: mx, y1: my1, x2: mx, y2: my2, 'stroke-dasharray': '3 6' }, 'campus-opening'));
  });

  /* stop badges live above everything in the plan */
  stopsLayer = svg('g', null, 'campus-stops');
  plan.appendChild(stopsLayer);

  root.appendChild(plan);

  /* ------------------------------ THE ART PROBE -------------------------- */
  /* Room logos are OPTIONAL, on exactly the terms punchcard.js's face geometry
   * is: the stage starts on `data-art="on"` and ONE preload decides. No file,
   * no decoder, a 404 or a corrupt png and the attribute flips to "off" - CSS
   * then hides every `<image>` and what is left is the drawn blueprint the
   * campus has always been. WHOLE, not holed: nothing else in the treatment
   * depends on the art, so a room with no logo still wears its card gradient,
   * its gold trim, its marquee and its plate.
   * ONE probe, not nine: the nine files ship together or not at all, and nine
   * preloads on a screen that is always up is nine decodes to learn one fact. */
  try { root.setAttribute('data-art', 'on'); } catch (e) { /* noop */ }
  (function probeArt() {
    const artOff = (why) => {
      try { root.setAttribute('data-art', 'off'); } catch (e) { /* noop */ }
      say('campus: no room art (' + why + ') - drawing the blueprint');
    };
    try {
      const first = Object.keys(LOGO_BOX)[0];
      if (!first) return;
      if (typeof Image !== 'function') { artOff('no Image'); return; }
      const probe = new Image();
      probe.onerror = () => { if (!destroyed) artOff('preload failed'); };
      /* Resolved against THIS module the way loadFaceGeometry resolves its
       * json, so the file:// suites fail fast into the fallback instead of
       * hanging on a path that only exists behind the host's origin. */
      probe.src = logoUrl(first);
    } catch (e) {
      artOff((e && e.message) || e);
    }
  }());

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
  /* COLLAPSED BY DEFAULT: what hangs from the chains at first is the small
   * plaque below - clock, "TIMETABLE", chevron. Clicking it drops the board
   * out beneath (and the shell rolls the flaps through on.boardToggle, which
   * is why there is no "flip the board again" button anywhere any more - the
   * plaque IS the flip). The clock pulses until the first open of the day. */
  const boardwrap = el('div', 'campus-boardwrap collapsed');
  const chains = el('div', 'campus-chains');
  chains.appendChild(el('div', 'campus-chain'));
  chains.appendChild(el('div', 'campus-chain'));
  boardwrap.appendChild(chains);
  /* Everything below the chains hangs as ONE object (the sway lives on this
   * wrapper, not per-piece - a plaque and a board rotating around different
   * origins would shear at the seam). */
  const boardSway = el('div', 'campus-boardsway');
  const boardTab = el('button', 'campus-boardtab' + (boardPulse ? ' pulse' : ''));
  boardTab.type = 'button';
  boardTab.appendChild(el('span', 'tclock', '🕘'));
  boardTab.appendChild(el('span', 'tlabel', t('timetable', 'Timetable').toUpperCase()));
  boardTab.appendChild(el('span', 'tchev', '▾'));
  boardTab.setAttribute('aria-expanded', 'false');
  boardTab.addEventListener('click', () => {
    const expand = boardwrap.classList.contains('collapsed');
    boardwrap.classList.toggle('collapsed', !expand);
    boardTab.setAttribute('aria-expanded', expand ? 'true' : 'false');
    if (expand) boardTab.classList.remove('pulse');   // opened = the clock's job is done
    try { if (handlers.boardToggle) handlers.boardToggle(expand); }
    catch (e) { say('boardToggle handler threw: ' + ((e && e.message) || e)); }
  });
  boardSway.appendChild(boardTab);
  const boardMount = el('div', 'campus-board');
  boardSway.appendChild(boardMount);
  const footMount = el('div', 'campus-boardfoot');
  boardSway.appendChild(footMount);
  boardwrap.appendChild(boardSway);
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
   * shortcut, the Front Office room is the diegetic front door to the same page. */
  const gear = el('button', 'campus-gearbtn', '⚙');
  gear.type = 'button';
  gear.setAttribute('aria-label', t('settings', 'Settings'));
  gear.setAttribute('title', t('settings', 'Settings'));
  gear.addEventListener('click', () => { if (handlers.registrar) handlers.registrar(); });
  topCluster.appendChild(gear);
  /* THE ENVELOPE, between the bell and the gear. The chip hides itself while
   * the box has never held a letter, so a fresh save's chrome is unchanged. */
  if (post && typeof post.openMail === 'function') {
    mailChip = mountMailChip(topCluster, {
      onOpen: post.openMail,
      unreadCount: post.mailUnread,
      total: post.mailTotal,
    });
    if (mailChip && mailChip.el) {
      try { topCluster.insertBefore(mailChip.el, gear); } catch (e) { /* order is cosmetic */ }
    }
  }
  /* THE WALLET, between the envelope and the gear. It is a READING, not a
   * control: two numbers and the two glyphs, and pressing it walks to the
   * counter the same way the Records door walks to the office. It repaints on
   * every campus update() because a payout can land while the board is up and
   * a chip that lied about your tickets for a whole screen is worse than no
   * chip at all. */
  if (economy && typeof economy.balance === 'function') {
    walletChip = el('button', 'campus-wallet');
    walletChip.type = 'button';
    walletChip.setAttribute('aria-label', t('campus_room_prizes', 'Prize Counter'));
    walletChip.setAttribute('title', t('campus_room_prizes', 'Prize Counter'));
    const tWrap = el('span');
    const tIco = el('i', 'arc-tick');
    tIco.setAttribute('aria-hidden', 'true');
    tWrap.appendChild(tIco);
    walletTicketN = el('b', null, '0');
    tWrap.appendChild(walletTicketN);
    walletChip.appendChild(tWrap);
    const kWrap = el('span');
    const kIco = el('i', 'arc-tok', '◉');
    kIco.setAttribute('aria-hidden', 'true');
    kWrap.appendChild(kIco);
    walletTokenN = el('b', null, '0');
    kWrap.appendChild(walletTokenN);
    walletChip.appendChild(kWrap);
    /* THE CHIP IS NOT THE DOOR (ORIENTATION.md §2.3, the gear's ruling applied
     * to the second thing in this cluster that opens a room). The topbar gear
     * goes STRAIGHT to settings while the Front Office door walks there; the
     * purse goes straight to the shelf while the alley window walks there. A
     * shortcut that made you walk would not be a shortcut, and a door that
     * teleported you would not be a door.
     * `prizesShelf` is an OPT-IN override exactly like `registrarRoom` is: a
     * caller that only knows `prizes` (every caller before the booth, and every
     * suite) still gets the behaviour it always got. */
    walletChip.addEventListener('click', () => {
      const fn = handlers.prizesShelf || handlers.prizes;
      if (fn) fn();
    });
    try { topCluster.insertBefore(walletChip, gear); } catch (e) { topCluster.appendChild(walletChip); }
    paintWallet();
  }
  /* THE ACCOUNT CHIP, after the gear - the far right, the way the topbar and
   * the main web app both place it. Web host only. */
  topClusterEl = topCluster;
  gearEl = gear;
  try { mountAccountChip(acctBag ? acctBag.get() : null); } catch (e) { /* noop */ }
  root.appendChild(topCluster);

  /* THE HINT HAS TO BE TRUE. There is no hover on a phone, so the desktop line
   * describes a gesture the player does not have; the touch row says the same
   * thing about the gesture they do. */
  root.appendChild(el('div', 'campus-hint',
    (isMobile()
      ? t('campus_hint_touch', 'Tap a room to step inside.')
      : t('campus_hint', 'Hover a room - click to step inside.')).toUpperCase()));

  /* ------------------------------ student ID ----------------------------- */
  const id = el('div', 'campus-idcard');
  /* WITHHELD IS A MODE, NOT A SECOND CARD (ORIENTATION.md §3.2). Orientation Day
   * hands this exact node to the player mid-air, so there is only ever ONE card
   * object: 'withheld' builds it hidden and the beat un-hides the same element.
   * The default is 'shown', so every caller that has never heard of orientation
   * gets the furniture it has always got. Hidden through the `hidden` PROPERTY
   * and the sheet's `[hidden]{display:none!important}` reset - never a bare
   * `display:` on this node (trap 27). */
  if (idCardMode === 'withheld') id.hidden = true;
  /* THE LANYARD CLIP. Pure furniture, drawn, no asset. */
  const idClip = el('i', 'id-clip');
  idClip.setAttribute('aria-hidden', 'true');
  id.appendChild(idClip);
  /* THE CREST BAND. The card says what it is, in its own words. */
  const idBand = el('div', 'id-band');
  const idCrest = el('span', 'id-crest');
  const idCrestMark = idCrestGlyph();
  if (idCrestMark) idCrest.appendChild(idCrestMark);
  idCrest.appendChild(el('span', null, t('arcademy', 'The Arcademy').toUpperCase()));
  idBand.appendChild(idCrest);
  idBand.appendChild(el('span', 'id-kind', t('student_id_title', 'Student ID').toUpperCase()));
  id.appendChild(idBand);
  const idTop = el('div', 'id-top');
  /* THE PHOTO WELL. ONE `<img>` carries either the host's baked avatar or the
   * drawn stand-in; a decode failure swaps the src back to the stand-in and
   * never reaches for the network again (ghosts.js's rule). No Discord url and
   * no Discord id is ever on this page - the host resolves both (PRESENCE §10). */
  const idWell = el('div', 'id-well');
  const idPhoto = el('div', 'id-photo');
  idPhotoImg = el('img', 'id-photo-img');
  idPhotoImg.setAttribute('alt', '');
  idPhotoImg.setAttribute('decoding', 'async');
  idPhotoImg.addEventListener('error', () => {
    try {
      const fb = genericPortrait(idProfile && idProfile.presenceShare);
      if (idPhotoImg.src !== fb) { idPhotoImg.src = fb; idPhoto.classList.remove('is-real'); }
    } catch (e) { /* noop */ }
  });
  idPhotoImg.src = genericPortrait(null);
  idPhoto.appendChild(idPhotoImg);
  const idFlash = el('i', 'id-flash');
  idFlash.setAttribute('aria-hidden', 'true');
  idPhoto.appendChild(idFlash);
  idWell.appendChild(idPhoto);
  idPhotoEl = idPhoto;
  /* THE CHIP: the photo consent, in the card's own words. It paints the rung it
   * is HANDED and never the one its own click asked for (trap 1) - the click
   * leaves through `handlers.idChip` and the shell waits for the echo. */
  idChip = el('button', 'id-chip');
  idChip.type = 'button';
  idChip.addEventListener('click', (ev) => {
    /* The chip is INSIDE the card's own button, so its click must not also
     * open the spotlight - one press, one verb. */
    try { ev.stopPropagation(); } catch (e) { /* noop */ }
    if (id.hidden || (id.dataset && id.dataset.inflight === '1')) return;
    if (handlers.idChip) { try { handlers.idChip(); } catch (e) { say('idChip threw: ' + ((e && e.message) || e)); } }
  });
  idTop.appendChild(idWell);
  const idMeta = el('div', 'id-meta');
  idMeta.appendChild(el('div', 'id-name', t('student', 'Student')));
  idNum = el('div', 'id-num', '');
  idMeta.appendChild(idNum);
  idMeta.appendChild(el('div', 'id-no', (t('semester', 'Semester') + ' ' + termRoman).toUpperCase()));
  idTier = el('span', 'id-tier', '');
  idMeta.appendChild(idTier);
  idTop.appendChild(idMeta);
  id.appendChild(idTop);
  /* THE CHIP SPANS THE CARD, not the 58px well. "Link Discord" at chip type
   * does not fit under the photo and an ellipsis is not a consent sentence -
   * the row still reads as the photo's own switch because it sits directly
   * under it, and it is a 208px hit target instead of a 48px one. */
  id.appendChild(idChip);
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
  const idFoil = el('i', 'id-foil');
  idFoil.setAttribute('aria-hidden', 'true');
  id.appendChild(idFoil);
  paintIdChip('link');
  /* THE CARD IS A DOOR NOW. It opens the ID spotlight (shell/idcard.js) through
   * the shell, which owns the overlay exactly as it owns the Records one. The
   * press is REFUSED while the card is withheld and while Orientation Day has
   * it in the air (`data-inflight`, set and cleared by orientation.js) - a beat
   * that is mid-flight is not a thing you can pick up. */
  id.setAttribute('role', 'button');
  id.tabIndex = 0;
  id.setAttribute('aria-label', t('student_id_title', 'Student ID'));
  const openIdCard = () => {
    if (id.hidden || (id.dataset && id.dataset.inflight === '1')) return;
    if (!handlers.idCard) return;
    try { handlers.idCard(); } catch (e) { say('idCard threw: ' + ((e && e.message) || e)); }
  };
  id.addEventListener('click', openIdCard);
  id.addEventListener('keydown', (ev) => {
    if (!ev || (ev.key !== 'Enter' && ev.key !== ' ')) return;
    try { ev.preventDefault(); } catch (e) { /* noop */ }
    openIdCard();
  });
  root.appendChild(id);

  /** Paint the chip's rung. `pending` keeps the last label - the echo has not
   *  landed, so there is nothing new to say (trap 1). */
  function paintIdChip(state) {
    idChipState = String(state || 'link');
    paintChip(idChip, t, idChipState, true);
  }

  /**
   * THE PROFILE LANDED. Everything the card says about YOU repaints from this
   * one object and from nothing else: the photo, the name, the number and the
   * chip's resting rung. A host that has never heard of `profile` simply never
   * calls this, and the card keeps the stand-in portrait it was built with.
   * @param {Object} p  {name, avatarUrl, discordLinked, presenceShare, selfId}
   */
  function setIdProfile(p) {
    idProfile = p && typeof p === 'object' ? p : null;
    const prof = idProfile || {};
    try { if (idPhotoImg) idPhotoImg.src = portraitSrc(prof); } catch (e) { /* noop */ }
    try {
      if (idPhotoEl && idPhotoEl.classList) {
        idPhotoEl.classList.toggle('is-real',
          !!prof.discordLinked && String(prof.presenceShare) === 'discord' && !!prof.avatarUrl);
      }
    } catch (e) { /* noop */ }
    try { if (idPhotoEl) idPhotoEl.setAttribute('aria-label', portraitLabel(t, prof)); }
    catch (e) { /* noop */ }
    const nameEl = id.querySelector ? id.querySelector('.id-name') : null;
    if (nameEl) nameEl.textContent = prof.name ? String(prof.name) : t('student', 'Student');
    if (idNum) {
      const num = studentNumber(prof.selfId, prof.enrolled, prof.name);
      idNum.textContent = t('id_no', 'Student no.').toUpperCase() + ' ' + num.no;
    }
    paintIdChip(chipRung(prof));
  }

  /** PHOTO DAY on the furniture card: the shutter, the well-only flash and the
   *  photo developing in. Reduced motion is a plain swap (runPhotoDay's rule). */
  function idPhotoDay() {
    return runPhotoDay(idPhotoEl, sfx, !!reducedMotion, (ms, fn) => {
      try { setTimeout(fn, ms); } catch (e) { fn(); }
    });
  }

  /* THE POST ROW: the noticeboard thumbnail and the folded Bugle, leaning
   * against the wall beside the student ID. Furniture only - the overlays,
   * their engines and their state are the shell's, delivered in `post`. */
  if (post && (typeof post.openBoard === 'function' || typeof post.openBugle === 'function')) {
    const postRow = el('div', 'campus-postrow');
    if (typeof post.openBoard === 'function') {
      boardProp = mountBoardProp(postRow, {
        onOpen: post.openBoard, daySeed: post.daySeed, state: post.boardState,
      });
    }
    if (typeof post.openBugle === 'function') {
      bugleProp = mountBugleProp(postRow, {
        onOpen: post.openBugle, state: post.bugleState,
      });
    }
    root.appendChild(postRow);
  }

  root.appendChild(el('div', 'campus-vignette'));

  /* THE SEEP'S LAYER (shell/seep.js). One empty, pointer-events:none div at
   * z 12 inside the stage - above the plan, under every piece of campus chrome,
   * and under the page's fx / ceremony / reveal / EMI layers for free because
   * the stage is its own stacking context. It costs nothing at rest and the
   * campus knows nothing about what the director puts in it. */
  const seepLayer = el('div', 'campus-seep');
  seepLayer.setAttribute('aria-hidden', 'true');
  root.appendChild(seepLayer);

  /* ------------------------------ tooltip -------------------------------- */
  const tip = el('div', 'campus-tip');
  const tipName = el('div', 't-name');
  const tipStatus = el('div', 't-status');
  const tipDesc = el('div', 't-desc');
  /* THE PEEK. Empty for every room on the plan except the one that has a
   * painting of itself (the Prize Counter's alley), and hidden until a card
   * asks for it. Styled INLINE on purpose: styles.css is another agent's desk
   * this session and the campus already sets tip geometry through
   * `tip.style.setProperty`, so this is the sheet the card already uses. */
  const tipArt = el('img', 't-art');
  try {
    tipArt.alt = '';
    tipArt.setAttribute('aria-hidden', 'true');
    tipArt.style.setProperty('display', 'none');
    tipArt.style.setProperty('width', '100%');
    tipArt.style.setProperty('height', 'auto');
    tipArt.style.setProperty('margin-top', '7px');
    tipArt.style.setProperty('border-radius', '5px');
    /* A 16:9 plate scaled to a 226px card is a fractional downscale, so the
     * browser's own resample and NOT `pixelated` - see prizecounter.css's
     * header for the same ruling, and the same reason. */
    tipArt.style.setProperty('image-rendering', 'auto');
  } catch (e) { /* noop */ }
  tip.appendChild(tipName); tip.appendChild(tipStatus); tip.appendChild(tipDesc);
  tip.appendChild(tipArt);
  root.appendChild(tip);

  /** `onDwell` is EMI's, and only the class rooms pass one - see HOVER_DWELL_MS. */
  function attachTip(g, dataFn, onDwell) {
    g.addEventListener('mouseenter', () => {
      let d;
      try { d = dataFn(); } catch (e) { d = null; }
      if (!d) return;
      /* The pointer has landed on something the plan can name. Throttled, and
       * deliberately under everything else on the stage - this is the felt of
       * the table, not an announcement.
       * 0.06, not 0.15: owner verdict 2026-08-24 took every HOVER cue down 60%
       * because they trigger often - a pointer crossing the plan lands this
       * dozens of times a minute, and the throttle only bounds the RATE, not
       * the fatigue. The gap window is unchanged; only the level moved. */
      try {
        const now = Date.now();
        if (now - lastHoverCue >= HOVER_GAP_MS) { lastHoverCue = now; sfx('pad', 0.06); }
      } catch (e) { /* noop */ }
      tipName.textContent = d.name || '';
      tipStatus.textContent = d.status || '';
      tipDesc.textContent = d.desc || '';
      /* ONE card carries the tip for the whole plan, so a picture set on one
       * room has to be UNSET by the next one or the alley follows the pointer
       * around the school. Cleared first, then set - never the other way. */
      try {
        if (d.art) {
          if (tipArt.getAttribute('src') !== d.art) tipArt.setAttribute('src', d.art);
          tipArt.style.setProperty('display', 'block');
        } else {
          tipArt.style.setProperty('display', 'none');
        }
      } catch (e) { /* noop */ }
      tip.classList.add('on');
      if (onDwell && typeof setTimeout === 'function') {
        clearHoverDwell();
        hoverDwell = setTimeout(() => {
          hoverDwell = 0;
          try { onDwell(); } catch (e) { /* noop */ }
        }, HOVER_DWELL_MS);
      }
    });
    g.addEventListener('mousemove', (e) => {
      const r = (root.getBoundingClientRect ? root.getBoundingClientRect() : null);
      if (!r || e.clientX == null) return;
      const w = (typeof window !== 'undefined' && window.innerWidth) ? window.innerWidth : 1280;
      tip.style.setProperty('left', Math.min(e.clientX + 18, w - 270) - r.left + 'px');
      tip.style.setProperty('top', (e.clientY + 18 - r.top) + 'px');
    });
    g.addEventListener('mouseleave', () => { tip.classList.remove('on'); clearHoverDwell(); });
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
  /* THE EXTRA CREDIT LEVER, under the two doors. It sits BELOW Begin on purpose:
   * it is a choice about the run you are about to take, not a way to take it,
   * and a control above the verb would read as a step you have to complete
   * first. Three positions on one rail, and a position you have not unlocked is
   * DIM AND STILL THERE - what you cannot pull yet is the whole reason to walk
   * down to the counter, so hiding it would hide the feature. Mounted only when
   * the shell handed an `economy` bag; the card is otherwise unchanged. */
  const ccLever = el('div', 'arc-lever');
  ccLever.hidden = true;
  const ccLeverRail = el('div', 'arc-lever-rail');
  const ccLeverHint = el('p', 'arc-lever-hint', '');
  if (economy && economy.lever) {
    ccLever.appendChild(el('p', 'arc-lever-title', t('lever_title', 'Extra Credit')));
    ccLever.appendChild(ccLeverRail);
    ccLever.appendChild(ccLeverHint);
    card.appendChild(ccLever);
  }
  scrim.appendChild(card);
  root.appendChild(scrim);

  let cardAction = null;
  let cardAltAction = null;
  /* COLD FEET, and only cold feet (EMI SEAM, heartbeat wave). `closeCard()`
   * itself is the wrong seam: Begin and Free Swim shut the card on their way IN
   * and would read as a back-out. These two listeners are the only paths that
   * dismiss the card and go nowhere. */
  function backOut() {
    if (!closeCard()) return;
    try { fireMoment('campus.doorBackedOut', { inClass: false }); } catch (e) { /* noop */ }
  }
  scrim.addEventListener('click', (e) => { if (e.target === scrim) backOut(); });
  ccX.addEventListener('click', () => backOut());
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

  /**
   * Paint (or retire) the Extra Credit rail. The words, the lock lines and the
   * painting all live in shell/lever.js, because the room scene's apron shows
   * the SAME rail and two copies of a three-way switch is two chances to drift.
   * Rebuilt on every card pop rather than mutated - see paintLever's note.
   */
  function setLever(show) {
    if (show === false || !economy || !economy.lever) { ccLever.hidden = true; return; }
    ccLever.hidden = false;
    paintLever({ rail: ccLeverRail, hint: ccLeverHint }, economy.lever, t, () => setLever(true));
  }

  function popCard() {
    /* THE DOOR. `card.swing` IS the door-open animation (the mockup's reflow
     * trick, one line down), so the thump belongs here rather than at any of the
     * four click handlers that reach it - one verb, one sound. */
    sfx('door', 0.35);
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
    /* W3 P1-18. popCard has thumped since the door card existed and nothing
     * ever answered it: the card simply vanished. Same door, lighter and a
     * touch higher, because shutting one is not the same gesture as opening
     * it. Guarded by `cardOpen` above, so a stray close is silent. */
    sfx('door', 0.18, { pitch: 1.1 });
    scrim.classList.remove('on');
    chalkClear();          // a card that shut mid-ghost still settles its title
    return true;
  }

  function chip(text) { const c = el('span', 'cc-mod', text); ccChips.appendChild(c); return c; }

  /* --------------------------- 12 THE CHALK GHOST ------------------------
   * THE FILE NAME, FOLLOWING YOU TO CLASS. The door card writes its title and,
   * for two frames, the chalk spells the room's OTHER name - the bare dev key,
   * the thing the filing system calls it - before it settles to the real one.
   *
   * WHY THIS IS FREE. It is asked in the deadest moment a class has: between
   * the door click and the first input, with the card not yet on screen and the
   * game not yet built. Nothing is armed, so nothing is at risk; you were only
   * reading. It costs no input, no point, no grade and no timing window, which
   * is the law above every other law in the class-side kit.
   *
   * ASKED BEFORE popCard(), ALWAYS. The shell's `busy` rung reads
   * `seepSeam().cardIsOpen()`, so a card that is already up refuses the beat -
   * which is correct for a repaint and wrong for the draw. One call site, right
   * before the card pops, and the ordering is the whole guard.
   *
   * THE STRING IS THE ROOM KEY, BARE. Owner ruling 0824: bare key on plates, the
   * cam tag only inside Slips, and the cam tag never comes indoors. It is
   * hardcoded and deliberately NOT lexicon-skinnable for the same reason the
   * plates are - this is the code talking, not the school.
   * -------------------------------------------------------------------- */
  let chalkTimer = 0;
  let chalkUndo = null;

  /** Put the title back and free the claim. Idempotent; every exit calls it. */
  function chalkClear() {
    if (chalkTimer) { try { clearTimeout(chalkTimer); } catch (e) { /* noop */ } chalkTimer = 0; }
    const undo = chalkUndo;
    chalkUndo = null;
    if (undo) { try { undo(); } catch (e) { /* noop */ } }
  }

  function chalkGhost(key) {
    if (chalkUndo) return;                 // one ghost at a time, never stacked
    if (!ROOMS[key]) return;               // a facility card has no dev key
    let token = null;
    try {
      const d = seepDir();
      token = (d && typeof d.beat === 'function') ? d.beat('door_card', { gameKey: key }) : null;
    } catch (e) { token = null; }
    if (!token) return;
    let before = '';
    try { before = String(ccCourse.textContent == null ? '' : ccCourse.textContent); }
    catch (e) { try { token.release(); } catch (e2) { /* noop */ } return; }
    let done = false;
    chalkUndo = () => {
      if (done) return;
      done = true;
      try { ccCourse.textContent = before; } catch (e) { /* noop */ }
      try { ccCourse.classList.remove('arc-seep-chalk'); } catch (e) { /* noop */ }
      try { token.release(); } catch (e) { /* noop */ }
    };
    try {
      ccCourse.textContent = key;
      ccCourse.classList.add('arc-seep-chalk');
    } catch (e) { chalkClear(); return; }
    const ms = Math.max(40, Math.round(Number(token.ms) || 90));
    if (typeof setTimeout === 'function') chalkTimer = setTimeout(chalkClear, ms);
    else chalkClear();
  }

  function openClassCard(key) {
    const spec = ROOMS[key];
    const r = st.rooms[key] || {};
    /* THE ROOM SCENE TAKEOVER (shell/room.js). An ENTERABLE door is offered to
     * the shell before the card pops; a shell that has the painted room takes
     * it (walks, then shows the set) and returns true. Everything the card
     * still owns stays the card's: a dark room (the lockedClick EMI seam), a
     * suspended school, and every key the shell declines. The plate line rides
     * along because ROOMS lives here and the scene should not re-derive it. */
    if (!st.suspended && (r.scheduled || r.unlocked || st.devPass) && handlers.roomScene) {
      let took = false;
      try {
        took = !!handlers.roomScene(key, {
          plate: (t(spec.nameKey, spec.nameEn) + ' · ' + t('campus_rm', 'RM') + ' ' + spec.rm).toUpperCase(),
        });
      } catch (e) { took = false; }
      if (took) return;
    }
    ccRoom.textContent = (t(spec.nameKey, spec.nameEn) + ' · ' + t('campus_rm', 'RM') + ' ' + spec.rm).toUpperCase();
    ccCourse.textContent = name(key);
    ccStatus.textContent = statusLine(key).toUpperCase();
    ccDesc.textContent = t(spec.descKey, spec.descEn);
    ccChips.textContent = '';
    if (r.tier) chip(tierLabel(r.tier));
    if (r.family) chip(familyLabel(r.family));
    /* A CLOCKLESS ROOM WEARS NO SECONDS (the class-length wave). Daily Trigger
     * still runs a budget and still rings; the door card just does not count it
     * out, the same suppression the departure board and the proctor strip make.
     * `clockless` rides in on the shell's descriptor list (noteDescriptors). */
    if (r.timeBudgetSec && !r.clockless) chip(r.timeBudgetSec + 's');
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
    /* THE LEVER rides the class card and only the class card: it is a wager on
     * a graded run, so a facility door has nothing to wager. Rebuilt here rather
     * than once at boot so a token spent at the counter lights Honors on the
     * very next door the player opens. */
    setLever(true);
    /* THE CHALK GHOST, and it must be asked BEFORE the card pops - see above. */
    chalkGhost(key);
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
      // Front Office) is not a locked click and never fires one.
      try { fireMoment('lockedClick', { what: 'sealed' }); } catch (e) { /* noop */ }
    } else {
      ccGo.textContent = t('campus_step_inside', 'Step inside').toUpperCase();
      ccGo.disabled = !d.action;
      cardAction = d.action || null;
    }
    setAltButton(null, null);        // facilities are never swum
    setLever(false);                 // nor wagered on
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

  /* stopAnchor / routeLegs / routeFor are MODULE-LEVEL now (see THE WALK GRAPH,
   * above): shell/ghosts.js walks the same corridor grammar and a second copy of
   * it is exactly how a student ends up inside a wall. */

  function update(nextState, nextStats) {
    if (destroyed) return;
    if (nextState) st = nextState;
    const stats2 = nextStats || stats || {};

    /* Post furniture repaints off its own getters - a delivery or a read that
     * happened since the last paint lands here (the silent-repaint path calls
     * update on every meta echo, so the chip is never stale on the board). */
    if (mailChip) { try { mailChip.update(); } catch (e) { /* noop */ } }
    if (boardProp) { try { boardProp.refresh(post && post.daySeed); } catch (e) { /* noop */ } }
    if (bugleProp) { try { bugleProp.refresh(); } catch (e) { /* noop */ } }
    /* ...and so does the wallet, off the same silent-repaint path: a payout
     * lands while the board is up, and the chip has to have moved by the time
     * the player looks at it. */
    paintWallet();

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
    const tierNow = stats2.tier || 1;
    idTier.textContent = tierLabel(tierNow).toUpperCase();
    /* RE-INKED. A card whose YEAR changed is a card the front desk stamped
     * again, so the stamp lands with the punch card's own thud - once, on the
     * paint that CHANGED it, and never on the first one (as far as tonight
     * knows, the card was always this year). Reduced motion keeps the words
     * and drops the beat, like everything else on this card. */
    if (idLastTier && tierNow !== idLastTier && !reducedMotion) {
      try {
        idTier.classList.remove('is-thud');
        void idTier.offsetWidth;
        idTier.classList.add('is-thud');
        idTier.setAttribute('title', t('id_reinked', 'Re-inked'));
      } catch (e) { /* noop */ }
      try { punchThud(1); } catch (e) { /* noop */ }
    }
    idLastTier = tierNow;
    /* The card's edge warms as the streak climbs: a colour, never a bulb ring. */
    try { id.classList.toggle('is-warm', (stats2.streak | 0) >= 7); } catch (e) { /* noop */ }
  }

  /* enrich class-card state with descriptor detail the shell passes on stops */
  function noteDescriptors(list) {
    for (const c of (Array.isArray(list) ? list : [])) {
      const r = st.rooms[c.gameKey];
      if (!r) continue;
      r.family = c.family;
      r.timeBudgetSec = c.timeBudgetSec;
      r.clockless = !!c.clockless;
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
    const steps = Math.max(1, Math.round(ms / ATTRACT_TICK_EFF_MS));
    let i = 0;
    const id = attractEvery(ATTRACT_TICK_EFF_MS, () => {
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
    const id = attractEvery(ATTRACT_FLIP_EFF_MS, () => {
      if (!attractOn) { attractStop(id); return; }
      k += 1;
      if (k >= ATTRACT_FLIPS) {
        attractStop(id);
        try { ref.neonText.textContent = neonLabel(key); } catch (e) { /* noop */ }
        done();
        return;
      }
      try { ref.neonText.textContent = flapText(truth, k); } catch (e) { /* noop */ }
      /* W3 P1-18: the sign flutters, so the sign ticks. The vane cue lives HERE
       * and not in splitflap's cueCascade (trap 87): that board's stagger is
       * hard-coupled to the stylesheet and this one runs on its own dial.
       * Quiet - it is a sign across a dark campus, not the departure board. */
      sfx('flap', 0.12);
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
    // A BEAT ON STAGE means the same thing and more: a stalled tab must never
    // deal an attract cursor over Orientation Day's handover (ORIENTATION §4).
    // The hold is asked HERE, at the one place the attract decides to begin, so
    // there is no second timer and no second flag to keep in sync.
    let held = false;
    try { held = !!holdsAttract(); } catch (e) { held = false; }
    if (destroyed || attractOn || cardOpen || held) { armIdle(); return; }
    // EMI SEAM: the player has gone quiet ON THE CAMPUS. The attract's own idle
    // edge is the signal - a mascot does not get a second idle timer.
    try { fireMoment('idlePlayer', { where: 'hub' }); } catch (e) { /* noop */ }
    const order = attractOrder();
    if (!order.length) { armIdle(); return; }
    attractOn = true;
    /* W3 P1-22: THE ROOM TONE UNDER THE ATTRACT. The player has gone quiet, the
     * cursor is about to tour an empty campus, and the empty campus has never
     * had any air in it. `campus_idle` is a HOLD - the mixer's only sustain - so
     * it is a bed, not a cue, and it must be let go of by the same code that
     * started it: cancelAttract stops it, and destroy() reaches cancelAttract.
     * The bed is SAMPLE-ONLY: with no file shipped this is honest silence, no
     * fallback and nothing to check. Music bus, under everything. */
    sfx('campus_idle', 0.25, { bus: 'music', hold: true });
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
      sfx('campus_idle', 0.25, { bus: 'music', stop: true });   // W3 P1-22: the bed's owner
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
  /* THE SLOW SECOND (shell/seep.js, tell 07) and its ONE law: this is a
   * DISPLAY freeze and nothing else. `bellSecondsLeft()` is never touched, no
   * offset is ever kept, and every consumer downstream of the real clock is
   * unaware any of this happened - the picture of the clock stutters, the clock
   * does not. `bellHeld` is the value the frozen picture is showing; while it is
   * a number the tick repaints nothing, and the catch-up paints the two skipped
   * seconds and then the truth, 110ms apart, the way a feed resyncs. */
  let bellHeld = null;
  let bellCatch = [];
  function paintBell(sec) {
    try { bellText.textContent = bellLabel(sec == null ? bellSecondsLeft() : sec); }
    catch (e) { /* noop */ }
  }
  function tickBell() {
    if (bellHeld != null) return;
    paintBell(null);
    /* EMI SEAM: the ONE tick that crosses five minutes, and only with the night
     * unfinished. A once-per-campus edge, never a countdown she reads aloud. */
    if (bellNagged || st.allDone) return;
    let left = 0;
    try { left = bellSecondsLeft(); } catch (e) { return; }
    if (!(left > 0 && left <= BELL_NEAR_SEC)) return;
    bellNagged = true;
    try { fireMoment('campus.bellNear', { secondsLeft: left, inClass: false }); } catch (e) { /* noop */ }
  }
  function clearBellCatch() {
    for (const id of bellCatch) { try { clearTimeout(id); } catch (e) { /* noop */ } }
    bellCatch = [];
  }
  /**
   * Freeze the COUNTDOWN DISPLAY for `ms`, then triple-tick back to the truth.
   * @param {number} ms
   * @returns {boolean} false when there is nothing to freeze (no chip, no timers,
   *   reduced motion, or a hold already running - a second stutter on top of the
   *   first would read as a broken clock rather than as a dropped frame).
   */
  function holdBell(ms) {
    if (destroyed || reducedMotion || bellHeld != null) return false;
    if (!bellText || typeof setTimeout !== 'function') return false;
    const hold = Math.max(300, Math.min(6000, Math.round(Number(ms) || 3000)));
    let start = 0;
    try { start = bellSecondsLeft(); } catch (e) { return false; }
    bellHeld = start;
    paintBell(start);
    try { bellText.classList.add('arc-seep-held'); } catch (e) { /* noop */ }
    /* W3 P2-10 (THE SEEP, tell 07). The catch-up is the tell: three quick
     * repaints where a clock should have ticked once. One tick per PAINT, and
     * the cue lives here rather than in seep.js because here is where the
     * paints are - a caller counting them out on its own timer would drift the
     * moment these two numbers move. Under the doctrine's floor: a clock you
     * half-heard resync, not a clock announcing itself. The hold itself is
     * silent, which is what makes the three ticks read as catching up. */
    bellCatch.push(setTimeout(() => {
      try { bellText.classList.remove('arc-seep-held'); } catch (e) { /* noop */ }
      try { bellText.classList.add('arc-seep-catch'); } catch (e) { /* noop */ }
      paintBell(start - 1);
      sfx('clock_tick', 0.09);
      bellCatch.push(setTimeout(() => { paintBell(start - 2); sfx('clock_tick', 0.09); }, 110));
      bellCatch.push(setTimeout(() => {
        bellHeld = null;
        try { bellText.classList.remove('arc-seep-catch'); } catch (e) { /* noop */ }
        paintBell(null);
        sfx('clock_tick', 0.09);
      }, 220));
    }, hold));
    return true;
  }
  if (!reducedMotion && typeof setInterval === 'function') {
    bellTimer = setInterval(tickBell, 1000);
  }
  tickBell();

  update(st, stats);

  return {
    root,
    boardMount,
    footMount,
    /** THE STUDENT BODY'S SEAM: the one group shell/ghosts.js draws into. */
    ghostMount: ghostLayer,
    /** THE WALK'S SEAM: the one group shell/walk.js draws into (above ghosts). */
    walkMount: walkLayer,
    /** The ONE student-ID node. Orientation Day animates this, never a copy. */
    idCardEl() { return id; },
    /**
     * THE PROFILE SEAM (shell/idcard.js's contract). The shell hands the card
     * `init.profile` at build and every `profile` frame after it; the card
     * paints and derives nothing. Called with nothing at all, the card keeps
     * the stand-in portrait and the unlinked chip it was built with.
     */
    setProfile: setIdProfile,
    /** THE ACCOUNT CHIP's seam: a later `account` repaints the chip, or mints
     *  it when init shipped without one. Nothing on a host that sent none. */
    setAccount(a) {
      if (!a) return;
      if (acctChip) { try { acctChip.setAccount(a); } catch (e) { /* noop */ } }
      else mountAccountChip(a);
    },
    /** The chip's in-flight looks, which only the shell knows about:
     *  'wait' (a link is in the air) and 'pending' (a set-setting is waiting on
     *  its echo). Every resting rung comes from `setProfile`. */
    setChipState: paintIdChip,
    /** PHOTO DAY on the furniture card (the spotlight has its own). */
    photoDay: idPhotoDay,
    /**
     * The two counters' groups, by walk-graph key. Orientation Day pulses the
     * Front Office's own neon sign on arrival (§3.3 step 3) and a beat may not
     * guess at a selector to find it - `roomRefs` holds GAME rooms only, so
     * this is the facility half of the same idea. Unknown keys answer null.
     * @param {string} key  'records' | 'registrar'
     * @returns {?Element}
     */
    facilityNode(key) {
      if (key === 'records') return recordsG;
      if (key === 'registrar') return regG;
      if (key === 'annex') return annexG;
      return null;
    },
    /**
     * A viewBox point -> page coordinates, for a FLIP that has to start at a
     * place on the plan (ORIENTATION.md §3.4's handover). Fully guarded: the
     * DOM double has no `getScreenCTM` and a detached SVG answers nothing
     * useful, so this returns null rather than a plausible lie, and the caller
     * lands the card with no animation debt.
     * @param {Array<number>} pt  [x, y] in plan viewBox units
     * @returns {?{x:number, y:number}}
     */
    mapPoint(pt) {
      try {
        if (!pt || pt.length < 2) return null;
        if (!plan || typeof plan.getScreenCTM !== 'function') return null;
        const m = plan.getScreenCTM();
        if (!m) return null;
        const x = m.a * pt[0] + m.c * pt[1] + m.e;
        const y = m.b * pt[0] + m.d * pt[1] + m.f;
        if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
        return { x, y };
      } catch (e) { return null; }
    },
    /** True once the entry reveal has finished (ghosts may not start before). */
    revealDone() { return revealed; },
    /**
     * THE SEEP'S SEAM (shell/seep.js). Five reads and one verb, and every one of
     * them is something the director cannot get any other way without guessing
     * at a selector. The campus knows nothing about the haunting: it hands over
     * nodes and a display-only clock freeze, and the director owns the rest.
     * @returns {{stage, plan, layer, roomKeys, roomNode, roomSub, holdBell}}
     */
    seepSeam() {
      return {
        stage: root,
        plan,
        layer: seepLayer,
        /** Every GAME room that actually built tonight (a closed semester has none). */
        roomKeys() { return Object.keys(roomRefs); },
        /** The room's whole `<g>` - what a Slip toggles. */
        roomNode(key) { return (roomRefs[key] && roomRefs[key].g) || null; },
        /** The room's `RM 101 · HOMEROOM` plate - what the File Name flashes. */
        roomSub(key) { return (roomRefs[key] && roomRefs[key].sub) || null; },
        /** Is the door card up? A READ - `closeCard()` would have closed it. */
        cardIsOpen() { return !!cardOpen; },
        /** Freeze the next-bell DISPLAY for ms, then triple-tick. See holdBell. */
        holdBell,
      };
    },
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
      chalkClear();          // the title goes back and the claim is freed (trap 30)
    if (mailChip) { try { mailChip.destroy(); } catch (e) { /* noop */ } mailChip = null; }
      if (acctChip) { try { acctChip.destroy(); } catch (e) { /* noop */ } acctChip = null; }
      if (boardProp) { try { boardProp.destroy(); } catch (e) { /* noop */ } boardProp = null; }
      if (bugleProp) { try { bugleProp.destroy(); } catch (e) { /* noop */ } bugleProp = null; }
      cancelAttract(false);
      clearHoverDwell();     // a settle timer must never outlive the plan it sat on
      if (idleTimer) { try { clearTimeout(idleTimer); } catch (e) { /* noop */ } idleTimer = 0; }
      try { INPUT_EVENTS.forEach((n) => root.removeEventListener(n, onInput, true)); } catch (e) { /* noop */ }
      if (docBound) { try { document.removeEventListener('keydown', onInput, true); } catch (e) { /* noop */ } }
      if (bellTimer) { try { clearInterval(bellTimer); } catch (e) { /* noop */ } bellTimer = 0; }
      /* A Slow Second in flight must not outlive its clock. */
      clearBellCatch();
      bellHeld = null;
      if (enterTimer) { try { clearTimeout(enterTimer); } catch (e) { /* noop */ } enterTimer = 0; }
      try { unfit(); } catch (e) { /* noop */ }
      if (document.documentElement && document.documentElement.classList) {
        try { document.documentElement.classList.remove('arc-campus-on'); } catch (e) { /* noop */ }
      }
      try { root.remove(); } catch (e) { /* noop */ }
    },
  };
}

export default createCampus;
