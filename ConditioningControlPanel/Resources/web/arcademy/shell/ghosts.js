/* ============================================================================
 * shell/ghosts.js - CAMPUS PRESENCE, "The Student Body" (planning/arcademy/
 * PRESENCE.md, design LOCKED 2026-08-24). P1: the renderer.
 *
 * Ghost cursors of the people who actually attended the Arcademy in the last
 * 24 hours, replayed onto tonight's campus. Forty attendees today is forty
 * students walking the corridor tonight - time-SHIFTED, never claimed as
 * concurrent, and dimmed by how old their evening is.
 *
 * FOUR LAWS, and every one of them is load-bearing:
 *
 *  1. THE PAGE NEVER FETCHES FROM A SERVER. The WebView2 page is offline by
 *     construction (trap 2) and the design says so twice. Data arrives ONE of
 *     two ways: the HOST pushes a `presence` frame over the bridge (campus open,
 *     then ~every 60s), or `?presence=fixture` reads the bundled same-origin
 *     `shell/fixtures/presence.json`. No push and no fixture = no ghosts and no
 *     errors: the feature is SILENTLY ABSENT, which is what lets it merge long
 *     before the server half exists.
 *
 *  2. THE WALK GRAPH IS CAMPUS.JS'S. `stopAnchor` / `walkLegs` / `gateLegs`
 *     were lifted out of `createCampus` for this module rather than copied, so
 *     the nightly route and the student body walk ONE corridor grammar. A second
 *     pathing system is precisely how a student ends up inside a wall.
 *
 *  3. NO COORDINATE EVER CAME FROM A SERVER, AND NONE EVER WILL. The wire is
 *     rooms and timestamps (PRESENCE §1/§8, so the same snapshot can later drive
 *     the Open Grounds 3D graph). Every position on this page is SYNTHESISED
 *     here from the event list.
 *
 *  4. THE ENCOUNTERS ARE THEATRE AND THEY ARE HONEST ABOUT IT (PRESENCE §5).
 *     Nobody picks a partner; nothing is sent anywhere; the scenes are 1-4
 *     character blips out of the lexicon, never prose. Detection is ANALYTIC at
 *     plan time - the scheduler already knows every polyline and its timing, so
 *     there is no per-frame collision loop anywhere in this file.
 *
 * The player's own ghost is NEVER drawn (`self`): you are the camera.
 *
 * NODE-SAFE BY CONSTRUCTION (trap 60's discipline): every document / rAF /
 * fetch / matchMedia touch is guarded, so the DOM double can build the whole
 * layer and the pure halves (normalise / schedule / detect) import clean.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { makeRng, hash01 } from '../core/rng.js';
import {
  ROOMS, CAMPUS_GATE, stopAnchor, doorPoint, walkLegs, gateLegs,
} from './campus.js';

const SVGNS = 'http://www.w3.org/2000/svg';

/* ----------------------------------------------------------------------------
 * DIALS. Everything a play-test would want to move lives here.
 * -------------------------------------------------------------------------- */
/** The wire version this module understands. A newer snapshot is refused. */
export const PRESENCE_V = 1;
/** Simultaneous drawn ghosts. Overflow feeds the busyness chips, never the map. */
export const MAX_GHOSTS = 24;
/** THE TOUCH CEILING (perf/arcademy-mobile-web). Every drawn ghost writes an
 * SVG transform attribute per rAF frame into the campus plan, and 24 of those
 * is a standing tax an iPhone pays whenever the campus is up. On a coarse
 * pointer the map draws at most this many; the rest feed the busyness chips
 * exactly the way overflow always has. Desktop keeps MAX_GHOSTS untouched. */
export const TOUCH_MAX_GHOSTS = 8;
/* Probed ONCE at module init (same probe as trap 42's `.ae-touch` seam):
 * coarse pointer, or a touch digitiser on a host whose media queries lie. */
const IS_COARSE = (() => {
  try {
    if (typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches) return true;
  } catch (e) { /* noop */ }
  try {
    return typeof navigator !== 'undefined' && Number(navigator.maxTouchPoints) > 1;
  } catch (e) { /* noop */ }
  return false;
})();
/** Rooms with at least this many attendees wear a count chip (PRESENCE §6). */
export const BUSY_MIN = 3;
/** The replay window. Anything older than this is not a ghost at all. */
export const WINDOW_MS = 24 * 3600 * 1000;
/** Age tiers, in ms. Order matters: the first one that fits wins. */
export const AGE_TIERS = Object.freeze([
  { tier: 'live', maxMs: 5 * 60 * 1000 },
  { tier: 'fresh', maxMs: 3600 * 1000 },
  { tier: 'dim', maxMs: 6 * 3600 * 1000 },
  { tier: 'faint', maxMs: WINDOW_MS },
]);
/** Walking speed, viewBox units per second, per-ghost seeded inside the band. */
export const SPEED_MIN = 40;
export const SPEED_MAX = 60;
/** A replayed class compresses into this many seconds of dwell. */
export const DWELL_MIN_MS = 30000;
export const DWELL_MAX_MS = 90000;
/** The real class lengths the compression maps FROM. */
const REAL_MIN_S = 60;
const REAL_MAX_S = 600;
/** Off-stage beat between one replayed evening and the next. */
const AWAY_MIN_MS = 6000;
const AWAY_MAX_MS = 18000;
/** How far a dwelling student drifts around the door plaque. */
const IDLE_DX = 10;
const IDLE_DY = 6;

/* Encounters (PRESENCE §5, owner-locked numbers). */
export const ENCOUNTER_R = 28;
export const ENCOUNTER_WINDOW_MS = 2000;
export const ENCOUNTER_CHANCE = 0.25;
export const ENCOUNTER_COOLDOWN_MS = 90000;
export const ENCOUNTER_DOOR_CLEAR = 60;
export const SCENE_MS = 2600;
/** How far ahead one encounter plan reaches. Re-planned, deterministically, per epoch. */
export const PLAN_HORIZON_MS = 240000;
const PLAN_STEP_MS = 250;
const PLAN_LEAD_MS = 3000;      // never inside the first beats of a plan (reveal guard)
/** How much faster a ghost walks to catch a schedule an encounter cost it. */
const CATCHUP_RATE = 0.35;
/** A planned encounter is abandoned if the pair has drifted this far apart. */
const SCENE_MAX_GAP = ENCOUNTER_R * 2.5;

/** The weighted scene pool. Weights are the owner's, verbatim. */
export const SCENE_POOL = Object.freeze([
  Object.freeze({ kind: 'hihi', w: 0.40 }),
  Object.freeze({ kind: 'dots', w: 0.25 }),
  Object.freeze({ kind: 'wave', w: 0.20 }),
  Object.freeze({ kind: 'nod', w: 0.15 }),
]);

/**
 * THE FLOOR - every run of walkable ground on the plan, [x, y, w, h] in viewBox
 * units, transcribed from the `campus-ghall` rects `campus.js` draws. Nothing
 * here decides where a ghost goes (campus.js's walk graph does that); this is
 * the ASSERTION the suite runs the whole leg list against, and the clamp that
 * keeps an idle drift on the carpet. Change a floor in campus.js and this table
 * has to move in the same commit or a walker leaves the building.
 */
export const FLOOR_RECTS = Object.freeze([
  Object.freeze({ id: 'hall', x: 200, y: 430, w: 1040, h: 80 }),      // the Main Hall
  Object.freeze({ id: 'west-spur', x: 62, y: 450, w: 138, h: 40 }),   // the hall, carrying on west
  Object.freeze({ id: 'entrance', x: 460, y: 510, w: 240, h: 220 }),  // the Entrance Hall
  Object.freeze({ id: 'gate-alley', x: 700, y: 510, w: 40, h: 220 }), // the front walk's paved run
  Object.freeze({ id: 'front-path', x: 695, y: 730, w: 50, h: 190 }), // out through the Main Gate
]);

/* ============================================================================
 * PURE: THE WIRE
 * ==========================================================================*/

/** Room keys are underscored on this page; a hyphenated wire key canonicalises. */
export function canonRoom(key) {
  if (key == null) return null;
  const k = String(key).trim().toLowerCase().replace(/-/g, '_');
  return Object.prototype.hasOwnProperty.call(ROOMS, k) ? k : null;
}

const SHARE_TIERS = new Set(['anon', 'username', 'discord']);
const EVENT_KINDS = new Set(['campus_enter', 'room_enter', 'class_end', 'campus_leave']);
const GRADES = new Set(['S', 'A', 'B', 'C', 'PASS']);
const MAX_STUDENTS = 400;
const MAX_EVENTS = 32;

function num(v) { const n = Number(v); return Number.isFinite(n) ? n : null; }

/**
 * THE ONE DOOR THE DATA COMES THROUGH.
 *
 * Two timestamp shapes, one normaliser. A snapshot that carries a server clock
 * (`now` in epoch SECONDS) is TIME-SHIFTED: an event lands at
 * `nowMs + (t - now) * 1000`, so the ages the page paints are the SERVER's ages
 * and a skewed client clock cannot invent a live ghost. A fixture carries
 * `now: 0` (or nothing) and RELATIVE offsets (`t <= 0`, seconds ago), which land
 * at `nowMs + t * 1000`. Both answer absolute local milliseconds, never later
 * than `nowMs`.
 *
 * Everything is clamped, dropped or defaulted rather than thrown on: an unknown
 * room key is skipped (the class may be retired - Misdirection has no room), an
 * unusable share tier reads `anon`, a name on an anon row is discarded, an
 * avatar on anything but `discord` is discarded, and a student with no usable
 * event at all is not a student.
 *
 * @returns {null|{v, nowMs, students:Array, counts:Object, hash:string}}
 */
export function normalizeSnapshot(raw, nowMs) {
  if (!raw || typeof raw !== 'object') return null;
  const v = num(raw.v);
  if (v != null && v > PRESENCE_V) return null;        // a newer wire is not ours to guess at
  const base = Number.isFinite(nowMs) ? nowMs : Date.now();
  const serverNow = num(raw.now);
  const shifted = serverNow != null && serverNow > 1e9;

  const absMs = (tv) => {
    const s = num(tv);
    if (s == null) return null;
    const ms = shifted ? base + (s - serverNow) * 1000 : base + s * 1000;
    if (!Number.isFinite(ms)) return null;
    // The future is not a memory. A clock a minute out is clamped, not dropped.
    if (ms > base) return ms > base + 60000 ? null : base;
    if (ms < base - WINDOW_MS) return null;
    return ms;
  };

  const list = Array.isArray(raw.students) ? raw.students.slice(0, MAX_STUDENTS) : [];
  const students = [];
  const seen = new Set();
  for (const s of list) {
    if (!s || typeof s !== 'object') continue;
    const id = String(s.id == null ? '' : s.id).slice(0, 64);
    if (!id || seen.has(id)) continue;
    let share = String(s.share || 'anon');
    if (!SHARE_TIERS.has(share)) share = 'anon';
    const rawEvents = Array.isArray(s.events) ? s.events.slice(0, MAX_EVENTS) : [];
    const events = [];
    for (const e of rawEvents) {
      if (!e || typeof e !== 'object') continue;
      const kind = String(e.e || '');
      if (!EVENT_KINDS.has(kind)) continue;
      const ms = absMs(e.t);
      if (ms == null) continue;
      const room = canonRoom(e.room);
      // A room event whose key this build has no room for is SKIPPED, not fatal.
      if ((kind === 'room_enter' || kind === 'class_end') && !room) continue;
      const grade = (kind === 'class_end' && GRADES.has(String(e.grade))) ? String(e.grade) : null;
      events.push({ e: kind, t: ms, room, grade });
    }
    if (!events.length) continue;
    events.sort((a, b) => a.t - b.t);
    const named = (share === 'username' || share === 'discord');
    const name = named && s.name ? String(s.name).slice(0, 24) : null;
    students.push({
      id,
      // A username tier with no resolved name is an anon row wearing a label it
      // does not have. Demote rather than draw an empty plaque.
      share: (named && !name) ? 'anon' : share,
      name: (named && name) ? name : null,
      avatar: (share === 'discord' && s.avatar) ? String(s.avatar).slice(0, 512) : null,
      events,
      lastMs: events[events.length - 1].t,
    });
    seen.add(id);
  }

  const counts = Object.create(null);
  const rawCounts = (raw.counts && typeof raw.counts === 'object') ? raw.counts : {};
  for (const k of Object.keys(rawCounts)) {
    const room = canonRoom(k);
    const n = num(rawCounts[k]);
    if (room && n != null && n > 0) counts[room] = Math.min(9999, Math.round(n));
  }

  return { v: PRESENCE_V, nowMs: base, students, counts, hash: snapshotHash(students, counts) };
}

/**
 * THE ETAG. A stable fingerprint of WHAT the snapshot said, never of WHEN it was
 * read - the encounter seeds hang off it, so the same day replays the same
 * meetings on a repaint, and a genuinely new snapshot deals new ones.
 */
export function snapshotHash(students, counts) {
  const parts = [];
  const rows = (students || []).slice().sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
  for (const s of rows) {
    // Event times are ABSOLUTE ms and would move the hash on every poll, so the
    // fingerprint takes their ORDER and their rooms - the shape of the evening.
    parts.push(s.id + ':' + s.share + ':' + s.events.map((e) => e.e + (e.room || '') + (e.grade || '')).join(','));
  }
  const cnt = counts || {};
  for (const k of Object.keys(cnt).sort()) parts.push('#' + k + '=' + cnt[k]);
  const body = parts.join('|');
  // Two independent hashes concatenated: one 32-bit value collides too readily
  // to be the only thing standing between two days and the same encounters.
  return hash01(body).toFixed(9).slice(2) + '-' + hash01(body.length + '~' + body).toFixed(9).slice(2);
}

/* ============================================================================
 * PURE: THE FLOOR
 * ==========================================================================*/

/** The floor rect a point stands on, or null. Pure. */
export function floorAt(x, y, slack) {
  const s = slack || 0;
  for (const r of FLOOR_RECTS) {
    if (x >= r.x - s && x <= r.x + r.w + s && y >= r.y - s && y <= r.y + r.h + s) return r;
  }
  return null;
}

/** Pull a drifting point back onto the floor it started on. Pure. */
function clampToFloor(pt, home) {
  const r = floorAt(home[0], home[1]) || FLOOR_RECTS[0];
  const inset = 4;
  const x = Math.max(r.x + inset, Math.min(r.x + r.w - inset, pt[0]));
  const y = Math.max(r.y + inset, Math.min(r.y + r.h - inset, pt[1]));
  return [x, y];
}

/* ============================================================================
 * PURE: THE SCHEDULE
 * ==========================================================================*/

/** Which age tier an event age falls in, or null past the window. Pure. */
export function ageTierFor(ageMs) {
  for (const row of AGE_TIERS) if (ageMs < row.maxMs) return row.tier;
  return null;
}

/** A real class length (seconds) compressed into a replayed dwell (ms). Pure. */
export function scaleDwell(realSec) {
  const s = Number(realSec);
  if (!Number.isFinite(s) || s <= 0) return (DWELL_MIN_MS + DWELL_MAX_MS) / 2;
  const p = Math.max(0, Math.min(1, (s - REAL_MIN_S) / (REAL_MAX_S - REAL_MIN_S)));
  return Math.round(DWELL_MIN_MS + p * (DWELL_MAX_MS - DWELL_MIN_MS));
}

/** easeInOutSine - PRESENCE §4's easing, and the only one in this file. Pure. */
export function easeInOutSine(p) {
  const x = p < 0 ? 0 : p > 1 ? 1 : p;
  return -(Math.cos(Math.PI * x) - 1) / 2;
}

/** The visits an event list describes: [{room, realSec, grade}]. Pure. */
export function visitsOf(events) {
  const out = [];
  let open = null;
  for (const e of events) {
    if (e.e === 'room_enter') {
      if (open) { open.realSec = Math.max(0, (e.t - open.atMs) / 1000); out.push(open); }
      open = { room: e.room, atMs: e.t, realSec: 0, grade: null };
    } else if (e.e === 'class_end') {
      if (open && open.room === e.room) {
        open.realSec = Math.max(0, (e.t - open.atMs) / 1000);
        open.grade = e.grade;
        out.push(open);
        open = null;
      }
    } else if (e.e === 'campus_leave') {
      if (open) { open.realSec = Math.max(0, (e.t - open.atMs) / 1000); out.push(open); open = null; }
    }
  }
  if (open) out.push(open);
  return out;
}

function seg(kind, from, to, ms, extra) {
  return Object.assign({ kind, from, to, ms: Math.max(1, Math.round(ms)) }, extra || {});
}

/** The idle beats a dwell is made of: hold, drift, hold, drift back. Pure. */
function dwellSegs(anchor, ms, rng, room, grade) {
  const drift = () => clampToFloor([
    anchor[0] + (rng() * 2 - 1) * IDLE_DX,
    anchor[1] + (rng() * 2 - 1) * IDLE_DY,
  ], anchor);
  const a = drift();
  const b = drift();
  // 55% standing, 45% shuffling - the plaque is a place you WAIT, not pace.
  const move = Math.round(ms * 0.45 / 4);
  const hold = Math.round(ms * 0.55 / 3);
  return [
    seg('dwell', anchor, anchor, hold, { room, grade, arrive: true }),
    seg('dwell', anchor, a, move, { room }),
    seg('dwell', a, a, hold, { room }),
    seg('dwell', a, b, move * 2, { room }),
    seg('dwell', b, b, hold, { room }),
    seg('dwell', b, anchor, move, { room }),
  ];
}

/**
 * ONE GHOST'S NIGHT, compressed into a loop.
 *
 * Gate -> walk the corridor to the first room's plaque -> dwell the scaled class
 * -> the next room or the way out -> off stage for a beat -> round again, so the
 * campus stays populated for as long as it is up. Every leg comes out of
 * campus.js's walk graph; nothing here invents a coordinate.
 */
export function scheduleFor(student, nowMs) {
  const rng = makeRng('arcademy|presence|' + student.id);
  const speed = SPEED_MIN + rng() * (SPEED_MAX - SPEED_MIN);
  const visits = visitsOf(student.events);
  const segs = [];
  let cur = [CAMPUS_GATE[0], CAMPUS_GATE[1]];

  const walk = (legs) => {
    for (const p of legs) {
      const d = Math.hypot(p[0] - cur[0], p[1] - cur[1]);
      if (d < 0.01) continue;
      segs.push(seg('walk', [cur[0], cur[1]], [p[0], p[1]], (d / speed) * 1000));
      cur = [p[0], p[1]];
    }
  };

  for (const v of visits) {
    const legs = walkLegs(cur, v.room);
    if (!legs.length) continue;             // an unknown room: skip the leg, keep the night
    walk(legs);
    for (const s of dwellSegs(stopAnchor(v.room), scaleDwell(v.realSec), rng, v.room, v.grade)) {
      segs.push(s);
      cur = [s.to[0], s.to[1]];
    }
  }
  walk(gateLegs(cur));
  segs.push(seg('away', [CAMPUS_GATE[0], CAMPUS_GATE[1]], [CAMPUS_GATE[0], CAMPUS_GATE[1]],
    AWAY_MIN_MS + rng() * (AWAY_MAX_MS - AWAY_MIN_MS)));

  let at = 0;
  for (const s of segs) { s.startMs = at; at += s.ms; }
  const loopMs = Math.max(1000, at);
  const ageMs = Math.max(0, nowMs - student.lastMs);

  return {
    id: student.id,
    share: student.share,
    name: student.name,
    avatar: student.avatar,
    tier: ageTierFor(ageMs),
    ageMs,
    lastMs: student.lastMs,
    speed,
    segs,
    loopMs,
    // WHERE IN THE LOOP THEY ARE AT nowMs. Seeded off the id, so a repaint
    // never teleports a ghost and two clients replay the same evening.
    phaseMs: rng() * loopMs,
    rooms: visits.map((v) => v.room),
  };
}

/**
 * Where one ghost stands at an absolute time. `visible:false` is the off-stage
 * beat between two replayed evenings. Pure.
 */
export function evalAt(sched, tMs, nowMs) {
  const into = (((tMs - nowMs) + sched.phaseMs) % sched.loopMs + sched.loopMs) % sched.loopMs;
  let s = sched.segs[sched.segs.length - 1];
  let idx = sched.segs.length - 1;
  for (let i = 0; i < sched.segs.length; i++) {
    const c = sched.segs[i];
    if (into < c.startMs + c.ms) { s = c; idx = i; break; }
  }
  const p = easeInOutSine((into - s.startMs) / s.ms);
  const x = s.from[0] + (s.to[0] - s.from[0]) * p;
  const y = s.from[1] + (s.to[1] - s.from[1]) * p;
  const dx = s.to[0] - s.from[0];
  return {
    x, y, seg: s, segIndex: idx, into,
    visible: s.kind !== 'away',
    walking: s.kind === 'walk',
    // Facing only ever changes on a real horizontal move; a vertical leg keeps
    // whatever the walker was already showing (a sprite that spins in a doorway
    // reads as a bug).
    facing: Math.abs(dx) < 0.5 ? 0 : (dx > 0 ? 1 : -1),
  };
}

/* ============================================================================
 * PURE: THE ENCOUNTERS (PRESENCE §5)
 * ==========================================================================*/

/** Every door threshold on the plan - an encounter never stages on one. */
const DOOR_POINTS = Object.freeze(Object.keys(ROOMS).map(doorPoint).filter(Boolean));

function nearAnyDoor(x, y) {
  for (const d of DOOR_POINTS) {
    if (Math.hypot(x - d[0], y - d[1]) < ENCOUNTER_DOOR_CLEAR) return true;
  }
  return false;
}

/** Weighted draw from SCENE_POOL with one 0..1 roll. Pure. */
export function sceneFor(roll) {
  const r = roll < 0 ? 0 : roll >= 1 ? 0.999999 : roll;
  let acc = 0;
  for (const row of SCENE_POOL) { acc += row.w; if (r < acc) return row.kind; }
  return SCENE_POOL[SCENE_POOL.length - 1].kind;
}

/**
 * THE CROSSINGS, SOLVED ONCE PER PLAN - never per frame.
 *
 * The scheduler already holds every polyline and its timing, so a plan walks the
 * next `horizonMs` at a coarse step, finds the pairs that come within R inside
 * one 2s window, and then LETS THE SEED DECIDE which of them actually happen.
 * The seed is the snapshot's own fingerprint plus the plan epoch plus the pair,
 * so the same day always deals the same meetings - which is the whole reason the
 * detection is analytic rather than a collision loop.
 *
 * Rules, in the order they are applied and in the order they matter:
 *   - both ghosts on stage, at least one of them actually walking;
 *   - clear of every door by ENCOUNTER_DOOR_CLEAR (never stage on a leaf);
 *   - not inside the plan's own lead-in (the entry reveal owns those beats);
 *   - ~25% of what survives fires;
 *   - a ghost is spent for 90s afterwards, and only ONE scene is ever on screen.
 */
export function detectEncounters(schedules, opts) {
  const o = opts || {};
  const nowMs = Number.isFinite(o.nowMs) ? o.nowMs : Date.now();
  const t0 = Number.isFinite(o.startMs) ? o.startMs : nowMs;
  const horizon = Number.isFinite(o.horizonMs) ? o.horizonMs : PLAN_HORIZON_MS;
  const hash = String(o.hash || '');
  const epoch = Number.isFinite(o.epoch) ? o.epoch : 0;
  const live = (schedules || []).filter((s) => s && s.segs && s.segs.length);
  if (live.length < 2) return [];

  const steps = Math.max(1, Math.floor(horizon / PLAN_STEP_MS));
  // One pass of positions, then one pass of pairs over the SAME table: 24 ghosts
  // is 276 pairs, and doing it twice a plan is cheaper than one frame of blur.
  const pos = [];
  for (let k = 0; k <= steps; k++) {
    const tk = t0 + k * PLAN_STEP_MS;
    const row = new Array(live.length);
    for (let i = 0; i < live.length; i++) row[i] = evalAt(live[i], tk, nowMs);
    pos.push(row);
  }

  const cands = [];
  for (let i = 0; i < live.length; i++) {
    for (let j = i + 1; j < live.length; j++) {
      let openAt = -1;
      let best = Infinity;
      let bestK = -1;
      const close = (k) => {
        if (openAt < 0) return;
        const tk = t0 + bestK * PLAN_STEP_MS;
        if (tk >= t0 + PLAN_LEAD_MS) {
          cands.push({ i, j, atMs: tk, k: bestK, dist: best });
        }
        openAt = -1; best = Infinity; bestK = -1;
      };
      for (let k = 0; k <= steps; k++) {
        const a = pos[k][i];
        const b = pos[k][j];
        const ok = a.visible && b.visible && (a.walking || b.walking)
          && !nearAnyDoor(a.x, a.y) && !nearAnyDoor(b.x, b.y)
          && Math.hypot(a.x - b.x, a.y - b.y) <= ENCOUNTER_R;
        if (ok) {
          const d = Math.hypot(a.x - b.x, a.y - b.y);
          if (openAt < 0) openAt = k;
          if (d < best) { best = d; bestK = k; }
          // One CROSSING is one candidate: a pair that stays close for longer
          // than the window has crossed again, not gone on standing there.
          if ((k - openAt) * PLAN_STEP_MS >= ENCOUNTER_WINDOW_MS) close(k);
        } else if (openAt >= 0) close(k);
      }
      close(steps);
    }
  }

  cands.sort((a, b) => (a.atMs - b.atMs) || (a.i - b.i) || (a.j - b.j));

  const out = [];
  const spentUntil = Object.create(null);
  let stageFreeAt = -Infinity;
  for (const c of cands) {
    const a = live[c.i];
    const b = live[c.j];
    const pair = a.id < b.id ? a.id + '~' + b.id : b.id + '~' + a.id;
    const rng = makeRng(hash + '|presence-meet|' + epoch + '|' + pair + '|' + c.k);
    if (rng() >= ENCOUNTER_CHANCE) continue;
    if (c.atMs < stageFreeAt) continue;                       // max one on screen
    if (c.atMs < (spentUntil[a.id] || -Infinity)) continue;   // per-ghost 90s
    if (c.atMs < (spentUntil[b.id] || -Infinity)) continue;
    const scene = sceneFor(rng());
    out.push({
      aId: a.id, bId: b.id, atMs: c.atMs, scene, dist: c.dist,
      at: [(pos[c.k][c.i].x + pos[c.k][c.j].x) / 2, (pos[c.k][c.i].y + pos[c.k][c.j].y) / 2],
    });
    spentUntil[a.id] = c.atMs + ENCOUNTER_COOLDOWN_MS;
    spentUntil[b.id] = c.atMs + ENCOUNTER_COOLDOWN_MS;
    stageFreeAt = c.atMs + SCENE_MS;
  }
  return out;
}

/**
 * The whole plan for a snapshot: who is drawn, where they walk, and who meets.
 * Pure, and the ONE place the 24-cap and the self-exclusion are applied.
 */
export function buildSchedules(o) {
  const opts = o || {};
  const snap = opts.snapshot;
  const nowMs = Number.isFinite(opts.nowMs) ? opts.nowMs : Date.now();
  const cap = Number.isFinite(opts.cap) ? opts.cap : MAX_GHOSTS;
  if (!snap || !Array.isArray(snap.students)) {
    return { schedules: [], overflow: 0, counts: {}, hash: '', nowMs };
  }
  const self = opts.self == null ? null : String(opts.self);
  const eligible = snap.students
    // YOU ARE THE CAMERA (PRESENCE §5): your own ghost is never on the map.
    .filter((s) => s.id !== self)
    .filter((s) => ageTierFor(Math.max(0, nowMs - s.lastMs)) !== null)
    // NEWEST FIRST. The cap is a "who is here now" cap, not a random 24.
    .sort((a, b) => b.lastMs - a.lastMs || (a.id < b.id ? -1 : 1));
  const drawn = eligible.slice(0, Math.max(0, cap));
  return {
    schedules: drawn.map((s) => scheduleFor(s, nowMs)),
    overflow: Math.max(0, eligible.length - drawn.length),
    counts: snap.counts || {},
    hash: snap.hash || '',
    nowMs,
  };
}

/* ============================================================================
 * PURE: THE SPRITE
 *
 * A pixel student, drawn from a hash of the id: stable per person, reversible
 * into nothing. NO IMAGE ASSET, and no hex in this file - every part wears a
 * class and styles.css paints it off the shell's own tokens, exactly the way
 * campus.js's rooms are painted (a mod palette reskins the student body free).
 * ==========================================================================*/

/** Deterministic look for one id. Pure - the suite pins it. */
export function spriteTraitsFor(id) {
  const h = hash01('arcademy|presence|look|' + id);
  const h2 = hash01('arcademy|presence|look2|' + id);
  const bits = Math.floor(h * 4294967296);
  return {
    hair: bits % 4,                              // 0 crop  1 long  2 tuft  3 bunches
    hairHue: Math.floor(h2 * 6),                 // gh-h0..5
    shirtHue: Math.floor(h * 8),                 // gh-s0..7
    skin: Math.floor(h2 * 4),                    // gh-k0..3
    tall: (bits >> 5) % 2 === 1,
    bag: (bits >> 7) % 3 === 0,
  };
}

/* ============================================================================
 * THE LAYER
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * A CLASSMATE PASSING. THE ONE AMBIENT CUE IN THE BUNDLE.
 * shell/audio.js holds the only audio node on the page (trap 18), so this is a
 * REQUEST on `document` and never a sound - the exact defensive shape
 * shell/ceremonies.js sfx() set. A dropped cue is not an error.
 * -------------------------------------------------------------------------- */
/* Every other cue in this wave rides a user action or a once-ever ceremony.
 * This one does not, and it is allowed to exist for exactly one reason: the
 * campus is meant to feel INHABITED, and a student body you can only see is a
 * student body you stop noticing. So it is barely audible, it rides the
 * ENCOUNTER (two ghosts actually meeting, which is already a staged, rare
 * thing) and it is throttled to one every eight seconds no matter how busy the
 * night is. Reduced motion has no walking, so it has nothing to overhear.
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

/** THE FLOOR ON THE PASS-BY CUE. Wall-clock, so `?presence=fixture`'s 6x time
 *  scale cannot turn a quiet campus into a switchboard. */
const NEAR_GAP_MS = 8000;

function svgNode(tag, attrs, cls) {
  const n = (typeof document !== 'undefined' && document.createElementNS)
    ? document.createElementNS(SVGNS, tag)
    : (typeof document !== 'undefined' ? document.createElement(tag) : null);
  if (!n) return null;
  if (cls) n.setAttribute('class', cls);
  if (attrs) for (const k of Object.keys(attrs)) n.setAttribute(k, String(attrs[k]));
  return n;
}

function rect(x, y, w, h, cls) { return svgNode('rect', { x, y, width: w, height: h }, cls); }

/**
 * Build the pixel student. 10 units wide, ~16 tall, feet on the origin.
 *
 * ONE BODY FOR THE WHOLE SCHOOL. Exported twice for two callers:
 *  - `buildSprite` for the Annex camera wall (annex/cams.js) - the fake feeds
 *    draw THIS student, never a copy of it (the "lifted, not copied" rule).
 *  - `buildStudentSprite` alias below for the Walk's player miniature
 *    (ORIENTATION.md §2.1) - the miniature is THE SAME BODY as a ghost, so a
 *    mod that reskins the student body reskins you with it; they can't drift.
 * Still pure: no snapshot, no schedule, no bridge - a caller gets a <g> of
 * rects and owns where it stands.
 */
export function buildSprite(id) {
  const tr = spriteTraitsFor(id);
  const g = svgNode('g', null, 'gh-sprite');
  if (!g) return null;
  const top = tr.tall ? -16 : -14;              // head starts here; feet at y 0
  const skin = 'gh-skin gh-k' + tr.skin;
  const hair = 'gh-hair gh-h' + tr.hairHue;
  const shirt = 'gh-shirt gh-s' + tr.shirtHue;
  const put = (n) => { if (n) g.appendChild(n); };

  // head
  put(rect(-3, top + 2, 6, 5, skin));
  // hair, four silhouettes off the same 6-wide skull
  if (tr.hair === 0) put(rect(-3, top, 6, 2, hair));
  else if (tr.hair === 1) { put(rect(-3, top, 6, 2, hair)); put(rect(-4, top, 1, 7, hair)); put(rect(3, top, 1, 7, hair)); }
  else if (tr.hair === 2) { put(rect(-3, top, 6, 2, hair)); put(rect(-1, top - 2, 2, 2, hair)); }
  else { put(rect(-3, top, 6, 2, hair)); put(rect(-5, top + 1, 2, 3, hair)); put(rect(3, top + 1, 2, 3, hair)); }
  // eyes - two pixels, and that is the whole face (no text is ever baked in)
  put(rect(-2, top + 4, 1, 1, 'gh-eye'));
  put(rect(1, top + 4, 1, 1, 'gh-eye'));
  // body + arms
  put(rect(-3, top + 7, 6, 6, shirt));
  put(rect(-4, top + 8, 1, 4, shirt));
  put(rect(3, top + 8, 1, 4, shirt));
  if (tr.bag) put(rect(3, top + 9, 2, 3, 'gh-bag'));
  // legs
  put(rect(-2, top + 13, 2, tr.tall ? 3 : 1, 'gh-leg'));
  put(rect(1, top + 13, 2, tr.tall ? 3 : 1, 'gh-leg'));
  return g;
}

/** THE SEAM shell/walk.js imports. Same function, a name a stranger can read. */
export { buildSprite as buildStudentSprite };

/** The discord tier's avatar chip. Falls back to the sprite on any load error. */
function buildChip(id, url, onFail) {
  const g = svgNode('g', null, 'gh-chip');
  if (!g) return null;
  const clipId = 'ghc-' + String(id).replace(/[^a-z0-9]/gi, '');
  const defs = svgNode('defs');
  const cp = svgNode('clipPath', { id: clipId });
  const c = svgNode('circle', { cx: 0, cy: -8, r: 6 });
  if (cp && c) { cp.appendChild(c); if (defs) { defs.appendChild(cp); g.appendChild(defs); } }
  const img = svgNode('image', {
    x: -6, y: -14, width: 12, height: 12,
    'clip-path': 'url(#' + clipId + ')', preserveAspectRatio: 'xMidYMid slice',
  }, 'gh-chip-img');
  if (img) {
    try { img.setAttributeNS('http://www.w3.org/1999/xlink', 'href', url); } catch (e) { /* noop */ }
    try { img.setAttribute('href', url); } catch (e) { /* noop */ }
    // A DEAD AVATAR IS A PIXEL STUDENT, not a broken-image glyph (the lesson
    // campus.js's logo probe paid for). One shot; a second error cannot loop.
    try {
      img.addEventListener('error', function once() {
        try { img.removeEventListener('error', once); } catch (e) { /* noop */ }
        try { onFail(); } catch (e) { /* noop */ }
      });
    } catch (e) { /* noop */ }
    g.appendChild(img);
  }
  const ring = svgNode('circle', { cx: 0, cy: -8, r: 6.6 }, 'gh-chip-ring');
  if (ring) g.appendChild(ring);
  return g;
}

/** The bubble. 1-4 characters of lexicon, and never one more. */
function buildBubble(text, lift, dx) {
  const g = svgNode('g', null, 'gh-bubble');
  if (!g) return null;
  // A SHARED line hangs BETWEEN the pair, not over one of their heads.
  if (dx) { try { g.setAttribute('transform', 'translate(' + (Math.round(dx * 10) / 10) + ',0)'); } catch (e) { /* noop */ } }
  const s = String(text == null ? '' : text).slice(0, 4);
  const w = 10 + s.length * 6;
  // THE SECOND SPEAKER'S LINE RIDES HIGHER. Two ghosts standing a dozen units
  // apart have bubbles wider than the gap between them, so the reply is lifted
  // clear of the greeting instead of being laid over it.
  const up = lift ? 14 : 0;
  const box = rect(-w / 2, -32 - up, w, 13, 'gb-box');
  if (box) box.setAttribute('rx', '3');
  const tail = svgNode('path', {
    d: 'M-2,' + (-19 - up) + ' L2,' + (-19 - up) + ' L0,' + (-15 - up) + ' Z',
  }, 'gb-tail');
  const txt = svgNode('text', { x: 0, y: -22.5 - up }, 'gb-t');
  if (txt) txt.textContent = s;
  if (box) g.appendChild(box);
  if (tail) g.appendChild(tail);
  if (txt) g.appendChild(txt);
  return g;
}

/** A grade-S pop at a door, four strokes and gone. */
function buildSpark(x, y) {
  const g = svgNode('g', { transform: 'translate(' + x + ',' + y + ')' }, 'gh-spark');
  if (!g) return null;
  [[0, -9, 0, -3], [0, 3, 0, 9], [-9, 0, -3, 0], [3, 0, 9, 0]].forEach(([x1, y1, x2, y2]) => {
    const l = svgNode('line', { x1, y1, x2, y2 });
    if (l) g.appendChild(l);
  });
  return g;
}

/**
 * `?presence=fixture` is the play-test path and the ONLY thing that reads a
 * file. `presenceFast` is a dev time-scale knob for capturing an encounter; both
 * are query-gated, so a production launch (no query string at all) sees neither.
 */
export function presenceOptions(search) {
  let q = search;
  if (q == null) {
    try { q = (typeof location !== 'undefined' && location && location.search) || ''; }
    catch (e) { q = ''; }
  }
  const s = String(q || '');
  const mode = /(^|[?&])presence=fixture(&|$)/.test(s) ? 'fixture' : 'bridge';
  const fast = mode === 'fixture' && /(^|[?&])presenceFast=1(&|$)/.test(s);
  return { mode, fast };
}

/**
 * @param {Object} o
 * @param {Element} o.mount        campus.ghostMount - the <g> sibling of the route
 * @param {Object=} o.bridge       the bridge module ({on}) - the host's push seam
 * @param {string=} o.mode         'bridge' (default) | 'fixture'
 * @param {boolean=} o.fast        the query-gated dev time-scale
 * @param {boolean=} o.reducedMotion  static ghosts, no walking, no encounters
 * @param {boolean=} o.lowPerf     same treatment, for a machine that asked for it
 * @param {Function=} o.log
 * @param {Object=} o.clock        test seam {now(), raf(fn), caf(id)}
 * @param {string=} o.fixtureUrl   test seam
 * @returns {{start, stop, setPaused, setSnapshot, tick, diagnostics, destroy}}
 */
export function createGhosts(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const mount = opts.mount || null;
  const still = !!(opts.reducedMotion || opts.lowPerf);
  const timeScale = opts.fast ? 6 : 1;
  const clock = opts.clock || {
    now: () => Date.now(),
    raf: (fn) => (typeof requestAnimationFrame === 'function' ? requestAnimationFrame(fn) : 0),
    caf: (id) => { try { if (typeof cancelAnimationFrame === 'function') cancelAnimationFrame(id); } catch (e) { /* noop */ } },
  };

  let destroyed = false;
  let started = false;
  let paused = false;
  let rafId = 0;
  let unsub = null;
  let docBound = false;

  if (mount) { try { mount.setAttribute('hidden', 'hidden'); } catch (e) { /* noop */ } }

  let plan = null;             // {schedules, counts, hash, nowMs, overflow}
  let selfId = opts.self == null ? null : String(opts.self);
  let encounters = [];
  let planEpoch = -1;
  let planBaseMs = 0;
  const nodes = new Map();     // id -> {g, sprite, chip, name, bubble, lastSeg, lagMs, facing}
  let live = null;             // the encounter currently on stage
  let looker = null;           // THE LOOKER (shell/seep.js tell 05), or null
  let sparkTimers = [];
  let lastNearCue = -1e9;      // wall ms of the last pass-by cue (see NEAR_GAP_MS)
  /* W3 P1-20. Two more ambient floors, each on its own wall clock and each on
   * the same NEAR_GAP_MS budget as the pass-by above: the classmates SPEAKING,
   * and the S flourish at a door. Separate counters on purpose - one shared
   * one would let a spark eat the speech for the next eight seconds. */
  let lastSpeakCue = -1e9;
  let lastSparkCue = -1e9;

  /* ---------------------------------------------------------------- data -- */

  function setSnapshot(raw) {
    if (destroyed) return;
    const nowMs = clock.now();
    const snap = normalizeSnapshot(raw, nowMs);
    if (!snap) { say('presence: snapshot refused (shape or version)'); return; }
    plan = buildSchedules({
      snapshot: snap,
      nowMs,
      self: selfId,
      // The touch ceiling (see TOUCH_MAX_GHOSTS). On a fine pointer this is
      // buildSchedules' own default, byte for byte.
      cap: IS_COARSE ? TOUCH_MAX_GHOSTS : MAX_GHOSTS,
    });
    planEpoch = -1;
    planBaseMs = nowMs;
    render();
    pump();
    say('presence: ' + plan.schedules.length + ' ghost(s)'
      + (plan.overflow ? ' (+' + plan.overflow + ' over the cap)' : '')
      + ', hash ' + plan.hash.slice(0, 8));
  }

  /**
   * THE DEV TIME-SCALE, and it is QUERY-GATED. `?presence=fixture&presenceFast=1`
   * runs the replay clock N times faster so a capture rig can catch an encounter
   * inside one screenshot budget. With no query string it is the identity, which
   * is what makes it harmless in production: `timeScale` is 1 and every
   * expression below is `now`.
   */
  function T(wallMs) {
    return timeScale === 1 ? wallMs : planBaseMs + (wallMs - planBaseMs) * timeScale;
  }

  function ensurePlan(nowMs) {
    if (!plan || still) return;
    const epoch = Math.floor((nowMs - planBaseMs) / PLAN_HORIZON_MS);
    if (epoch === planEpoch) return;
    planEpoch = epoch;
    encounters = detectEncounters(plan.schedules, {
      nowMs: plan.nowMs,
      startMs: planBaseMs + epoch * PLAN_HORIZON_MS,
      horizonMs: PLAN_HORIZON_MS,
      hash: plan.hash,
      epoch,
    });
    say('presence: encounter plan ' + epoch + ' -> ' + encounters.length);
  }

  /* -------------------------------------------------------------- render -- */

  function clearLayer() {
    for (const id of sparkTimers) { try { clearTimeout(id); } catch (e) { /* noop */ } }
    sparkTimers = [];
    nodes.clear();
    live = null;
    looker = null;      // the node it pointed at has just gone
    if (mount) { try { mount.textContent = ''; } catch (e) { /* noop */ } }
  }

  function render() {
    clearLayer();
    if (!mount || !plan) return;
    for (const s of plan.schedules) mountGhost(s);
    mountBusy();
  }

  function mountGhost(s) {
    const g = svgNode('g', { transform: 'translate(0,0)' }, 'campus-student');
    if (!g) return;
    g.setAttribute('data-tier', s.tier || 'faint');
    g.setAttribute('data-share', s.share);
    const inner = svgNode('g', null, 'gh-inner');
    const sprite = buildSprite(s.id);
    if (inner && sprite) inner.appendChild(sprite);
    let chip = null;
    if (s.share === 'discord' && s.avatar) {
      chip = buildChip(s.id, s.avatar, () => {
        try { if (chip && chip.parentNode) chip.parentNode.removeChild(chip); } catch (e) { /* noop */ }
        try { if (sprite) sprite.removeAttribute('hidden'); } catch (e) { /* noop */ }
        say('presence: avatar failed for a discord ghost - pixel student instead');
      });
      if (chip && inner) {
        // The chip IS the body for this tier, so the sprite steps aside - but it
        // stays mounted, because it is also the fallback and re-minting one on an
        // error is a second chance to fail.
        try { if (sprite) sprite.setAttribute('hidden', 'hidden'); } catch (e) { /* noop */ }
        inner.appendChild(chip);
      }
    }
    if (inner) g.appendChild(inner);
    let nameNode = null;
    if (s.name) {
      nameNode = svgNode('text', { x: 0, y: -20 }, 'gh-name');
      if (nameNode) { nameNode.textContent = s.name; g.appendChild(nameNode); }
    }
    mount.appendChild(g);
    nodes.set(s.id, { g, inner, sprite, chip, name: nameNode, bubble: null, lastSeg: -1, lagMs: 0, facing: 1 });
  }

  /**
   * THE BUSYNESS CHIPS (PRESENCE §6). `counts` is the SERVER's number and it
   * includes the people who opted OUT of being drawn - which is the whole point:
   * a room can be busy with students this layer is not allowed to show you.
   */
  function mountBusy() {
    const counts = (plan && plan.counts) || {};
    for (const room of Object.keys(counts)) {
      const n = counts[room];
      if (!(n >= BUSY_MIN) || !ROOMS[room]) continue;
      /* Beside the door and HUGGING THE ROOM'S OWN WALL. Level with the stop
       * anchor it stood shoulder to shoulder with tonight's numbered badge, and
       * two pills of the same size touching read as one control (browser
       * capture, 2026-08-24). It belongs to the ROOM, so it hangs off the room's
       * wall - still in the corridor, never under the room that would cover it.
       * A wing room pins its own stop mid-alley and keeps the level placement. */
      const a = stopAnchor(room);
      const cy = ROOMS[room].stop ? a[1] : a[1] + (ROOMS[room].side === 'n' ? -13 : 13);
      const g = svgNode('g', { transform: 'translate(' + (a[0] + 24) + ',' + cy + ')' }, 'campus-busy');
      if (!g) continue;
      const box = rect(-11, -7, 22, 14, 'cb-box');
      if (box) { box.setAttribute('rx', '5'); g.appendChild(box); }
      const txt = svgNode('text', { x: 0, y: 3.6 }, 'cb-t');
      if (txt) { txt.textContent = String(n); g.appendChild(txt); }
      const title = svgNode('title');
      if (title) { title.textContent = n + ' ' + t('presence_here_tonight', 'here tonight'); g.appendChild(title); }
      mount.appendChild(g);
    }
  }

  function bubbleFor(kind, side) {
    // THREE ASCII DOTS, NOT AN ELLIPSIS. The bubble is set in Press Start 2P, a
    // 96-glyph latin subset (trap 62) - U+2026 draws tofu there, and the whole
    // beat this scene is made of is the shared silence being LEGIBLE.
    if (kind === 'dots') return t('presence_bubble_dots', '...');
    if (kind === 'wave') {
      return side === 0
        ? t('presence_bubble_wave_a', 'o/')
        : t('presence_bubble_wave_b', '\\o');
    }
    if (kind === 'hihi') return t('presence_bubble_hi', 'hihi');
    return '';
  }

  function showBubble(rec, text, lift, dx) {
    if (!rec || !rec.g || !text) return;
    hideBubble(rec);
    const b = buildBubble(text, lift, dx);
    if (b) { rec.g.appendChild(b); rec.bubble = b; }
    /* W3 P1-20: THE STUDENT BODY IS AUDIBLE. Two classmates greeting each
     * other across the plan was the whole payoff of the presence layer and it
     * played silently. One syllable, at a twentieth of a game pop and pitched
     * DOWN - these are classmates, not the mascot, so they sit on the fx bus
     * and never near her Blipese. Only when a bubble actually landed, only
     * where there is walking to hear it over, and never more than once every
     * NEAR_GAP_MS whatever the campus is doing. */
    if (b && !still && !paused) {
      const nowWall = clock.now();
      if (nowWall - lastSpeakCue >= NEAR_GAP_MS) {
        lastSpeakCue = nowWall;
        sfx('emi_blip', 0.04, { pitch: 0.85 });
      }
    }
  }
  function hideBubble(rec) {
    if (rec && rec.bubble) {
      try { if (rec.bubble.parentNode) rec.bubble.parentNode.removeChild(rec.bubble); } catch (e) { /* noop */ }
      rec.bubble = null;
    }
  }

  function popSpark(x, y) {
    if (!mount || still) return;
    const g = buildSpark(x, y);
    if (!g) return;
    mount.appendChild(g);
    /* W3 P1-20: somebody took an S behind that door. The rarest thing the
     * presence layer draws, and the quietest cue in the file - it is a glint
     * across a dark campus, not a payout. Same eight-second floor as the rest
     * of the ambient, on its own clock. */
    if (!paused) {
      const nowWall = clock.now();
      if (nowWall - lastSparkCue >= NEAR_GAP_MS) {
        lastSparkCue = nowWall;
        sfx('chime', 0.06, { pitch: 1.3 });
      }
    }
    if (typeof setTimeout !== 'function') return;
    const id = setTimeout(() => {
      try { if (g.parentNode) g.parentNode.removeChild(g); } catch (e) { /* noop */ }
      sparkTimers = sparkTimers.filter((x2) => x2 !== id);
    }, 1000);
    sparkTimers.push(id);
  }

  /* ----------------------------------------------------------- the scene -- */

  function startScene(enc, wallMs, scaledMs) {
    const ra = nodes.get(enc.aId);
    const rb = nodes.get(enc.bId);
    if (!ra || !rb) return false;
    const sa = plan.schedules.find((s) => s.id === enc.aId);
    const sb = plan.schedules.find((s) => s.id === enc.bId);
    if (!sa || !sb) return false;
    const pa = evalAt(sa, scaledMs - ra.lagMs, plan.nowMs);
    const pb = evalAt(sb, scaledMs - rb.lagMs, plan.nowMs);
    // THE HONESTY GUARD. The plan was solved on the un-lagged schedules; a pair
    // that has drifted apart since (a catch-up still unwinding) does not get a
    // scene staged between two ghosts standing nowhere near each other.
    if (!pa.visible || !pb.visible) return false;
    if (Math.hypot(pa.x - pb.x, pa.y - pb.y) > SCENE_MAX_GAP) return false;
    /* THE STAGING, and it is not cosmetic. Two ghosts who meet HEAD ON are on
     * the same lane by definition - the gate alley is one file wide and the Main
     * Hall's centre line is one lane - so at closest approach they are drawn
     * exactly on top of each other and the scene reads as one glitching student
     * (caught in a browser capture, 2026-08-24). They step a dozen units apart
     * for the beat and step back afterwards, left/right by whoever was already
     * further left so nobody crosses through anybody, and BOTH marks are clamped
     * to the floor they are standing on - a stand-apart can never stage a
     * student inside a wall. */
    const leftIsA = pa.x <= pb.x;
    const oa = clampToFloor([pa.x + (leftIsA ? -11 : 11), pa.y - 4], [pa.x, pa.y]);
    const ob = clampToFloor([pb.x + (leftIsA ? 11 : -11), pb.y + 4], [pb.x, pb.y]);
    live = {
      enc, t0: wallMs, tf: scaledMs, ra, rb, spoke: [false, false],
      posA: oa, posB: ob,
    };
    // Turn to face each other - the sprite flip IS the turn.
    ra.facing = leftIsA ? 1 : -1;
    rb.facing = leftIsA ? -1 : 1;
    if (enc.scene === 'nod') {
      ra.g.setAttribute('data-scene', 'nod');
      rb.g.setAttribute('data-scene', 'nod');
    }
    /* SOMEBODY WENT PAST. Fired only once the scene has actually committed
     * (every refusal above returns before this), and only when there is walking
     * to hear it over.
     * 0.05, not 0.12: this is a PROXIMITY cue, i.e. hover-adjacent ambient -
     * nobody asked for it, it just happens while you stand on the plan. Owner
     * verdict 2026-08-24 took hover cues down 60% for triggering often, and
     * this one rides along with them. NEAR_GAP_MS is untouched. */
    if (!still && !paused) {
      const nowWall = clock.now();
      if (nowWall - lastNearCue >= NEAR_GAP_MS) {
        lastNearCue = nowWall;
        sfx('near', 0.05);
      }
    }
    return true;
  }

  function stepScene(nowMs) {
    if (!live) return;
    const dt = nowMs - live.t0;
    const k = live.enc.scene;
    if (k === 'hihi' || k === 'wave') {
      if (!live.spoke[0] && dt >= 300) { live.spoke[0] = true; showBubble(live.ra, bubbleFor(k, 0)); }
      if (!live.spoke[1] && dt >= 700) { live.spoke[1] = true; showBubble(live.rb, bubbleFor(k, 1), true); }
    } else if (k === 'dots') {
      // ONE bubble BETWEEN them - the shared silence, not two of them, and it
      // hangs off the MIDPOINT of the two marks rather than over either head.
      if (!live.spoke[0] && dt >= 350) {
        live.spoke[0] = true;
        showBubble(live.ra, bubbleFor(k, 0), false, (live.posB[0] - live.posA[0]) / 2);
      }
    }
    if (dt >= SCENE_MS) endScene();
  }

  function endScene() {
    if (!live) return;
    hideBubble(live.ra);
    hideBubble(live.rb);
    try { live.ra.g.removeAttribute('data-scene'); } catch (e) { /* noop */ }
    try { live.rb.g.removeAttribute('data-scene'); } catch (e) { /* noop */ }
    // The stop cost them the beats it lasted; the catch-up below pays it back.
    live.ra.lagMs += SCENE_MS;
    live.rb.lagMs += SCENE_MS;
    live = null;
  }

  /* ------------------------------------------------- THE LOOKER, UPSTAIRS --
   * On the cam wall downstairs, one student sometimes stops and looks straight
   * into the lens (annex/cams.js LOOK_P / LOOK_COOLDOWN_S / LOOK_HOLD_S). This
   * is the same student on the campus side, and it is the SAME SHAPE the
   * encounter scene already uses: one ghost is frozen at a scaled timestamp,
   * paints from there, and pays the stop back afterwards through `lagMs`.
   *
   * TWO THINGS THIS FILE DELIBERATELY DOES NOT OWN: the rarity and the
   * cooldown. `shell/seep.js` is the one authority on when a tell may run - a
   * second roll here would be a second haunting nobody could tune. This is a
   * verb, not a scheduler, and it is a no-op unless somebody calls it.
   * ------------------------------------------------------------------------ */
  /** The Main Gate's own line - the x every route enters the plan on. */
  const LOOK_MID_X = 720;

  function endLook() {
    if (!looker) return;
    const rec = looker.rec;
    const spent = Math.max(0, clock.now() - looker.t0) * timeScale;
    looker = null;
    try { rec.g.removeAttribute('data-seep-look'); } catch (e) { /* noop */ }
    // The stop cost them the beats it lasted; CATCHUP_RATE pays it back.
    rec.lagMs += spent;
  }

  /**
   * Stop one student mid-crossing and face them out of the plan.
   * @param {{ms?:number}=} o2
   * @returns {boolean} false when there is nobody to stop (no plan, nobody on
   *   screen, a scene already staged, reduced motion) - the caller then owns
   *   nothing and releases its claim.
   */
  function lookOut(o2) {
    if (destroyed || !started || paused || still) return false;
    if (looker || live || !plan || !plan.schedules.length) return false;
    const ms = Math.max(200, Math.min(4000, Math.round((o2 && Number(o2.ms)) || 900)));
    const wall = clock.now();
    const scaled = T(wall);
    let best = null;
    let bestD = Infinity;
    for (const s of plan.schedules) {
      const rec = nodes.get(s.id);
      if (!rec || rec.hiddenNow) continue;
      let at = null;
      try { at = evalAt(s, scaled - rec.lagMs, plan.nowMs); } catch (e) { at = null; }
      if (!at || !at.visible || !at.seg || at.seg.kind !== 'walk') continue;
      const d = Math.abs(at.x - LOOK_MID_X);
      if (d < bestD) { bestD = d; best = rec; }
    }
    if (!best) return false;
    looker = { rec: best, t0: wall, tf: scaled, ms };
    try { best.g.setAttribute('data-seep-look', '1'); } catch (e) { /* noop */ }
    return true;
  }

  /* ------------------------------------------------------------ the loop -- */

  let lastFrameMs = 0;

  function tick(nowRaw) {
    if (destroyed || !plan || !mount) return;
    const wall = Number.isFinite(nowRaw) ? nowRaw : clock.now();
    const scaled = T(wall);
    const dt = lastFrameMs ? Math.min(250, wall - lastFrameMs) : 0;
    lastFrameMs = wall;
    ensurePlan(scaled);
    if (looker && wall - looker.t0 >= looker.ms) endLook();

    // The one encounter that may be on stage right now.
    if (!still) {
      if (live) stepScene(wall);
      else {
        for (const enc of encounters) {
          if (enc.fired || scaled < enc.atMs) continue;
          enc.fired = true;                       // a missed cue is spent, never queued
          if (scaled <= enc.atMs + SCENE_MS) { startScene(enc, wall, scaled); break; }
        }
      }
    }

    for (const s of plan.schedules) {
      const rec = nodes.get(s.id);
      if (!rec) continue;
      const frozen = !!live && (live.ra === rec || live.rb === rec);
      /* THE LOOKER is frozen the same way a scene freezes a pair, and it is NOT
       * `frozen`: no stand-apart mark, no eased step, just a stop. */
      const held = !frozen && !!looker && looker.rec === rec;
      if (!frozen && !held && rec.lagMs > 0 && dt > 0) {
        // A SMALL SPEED-UP, never a teleport: the debt drains at 35% of real
        // time, so ~7s of slightly brisk walking puts them back on schedule.
        rec.lagMs = Math.max(0, rec.lagMs - dt * CATCHUP_RATE * timeScale);
      }
      const at = evalAt(s, (frozen ? live.tf : (held ? looker.tf : scaled)) - rec.lagMs, plan.nowMs);
      if (frozen) {
        // Ease OUT to the mark and back again, so the step apart is a step.
        const stand = live.ra === rec ? live.posA : live.posB;
        const inP = easeInOutSine(Math.min(1, (wall - live.t0) / 300));
        const outP = easeInOutSine(Math.max(0, Math.min(1, (wall - live.t0 - (SCENE_MS - 300)) / 300)));
        const k2 = inP * (1 - outP);
        at.x += (stand[0] - at.x) * k2;
        at.y += (stand[1] - at.y) * k2;
      }
      place(rec, s, at, frozen || held);
    }
  }

  function place(rec, sched, at, frozen) {
    const g = rec.g;
    if (!at.visible) {
      if (!rec.hiddenNow) { try { g.setAttribute('hidden', 'hidden'); } catch (e) { /* noop */ } rec.hiddenNow = true; }
      rec.lastSeg = at.segIndex;
      return;
    }
    if (rec.hiddenNow) { try { g.removeAttribute('hidden'); } catch (e) { /* noop */ } rec.hiddenNow = false; }
    const x = Math.round(at.x * 10) / 10;
    const y = Math.round(at.y * 10) / 10;
    try { g.setAttribute('transform', 'translate(' + x + ',' + y + ')'); } catch (e) { /* noop */ }
    if (!frozen && at.facing) rec.facing = at.facing;
    if (rec.inner && rec.facing !== rec.drawnFacing) {
      rec.drawnFacing = rec.facing;
      try { rec.inner.setAttribute('transform', rec.facing < 0 ? 'scale(-1,1)' : 'scale(1,1)'); } catch (e) { /* noop */ }
      // The name plate never mirrors with the body.
      if (rec.name) { try { rec.name.setAttribute('transform', 'scale(1,1)'); } catch (e) { /* noop */ } }
    }
    if (at.segIndex !== rec.lastSeg) {
      rec.lastSeg = at.segIndex;
      const s = at.seg;
      // THE S FLOURISH: one pop, at the door the class was graded behind, the
      // beat the replay actually arrives there (PRESENCE §4).
      if (s.kind === 'dwell' && s.arrive && s.grade === 'S' && s.room) {
        const a = stopAnchor(s.room);
        popSpark(a[0], a[1] - 12);
      }
    }
  }

  function frame() {
    if (destroyed || !started || paused) { rafId = 0; return; }
    tick();
    // NO FRAME CLOCK AT ALL (the DOM double, a headless run) answers 0: the
    // layer draws its one placement and stops. A missing rAF is not a slow
    // machine, it is not a browser (trap 36's corollary).
    rafId = clock.raf(frame);
  }

  function pump() {
    if (destroyed || !started || paused) return;
    if (still) { tick(); return; }      // one placement, no walking (PRESENCE §4)
    if (rafId) return;
    rafId = clock.raf(frame);
    if (!rafId) tick();
  }

  /* ----------------------------------------------------------- lifecycle -- */

  function onVisibility() {
    let hidden = false;
    try { hidden = !!(typeof document !== 'undefined' && document && document.hidden); } catch (e) { hidden = false; }
    setPaused(hidden);
  }

  /**
   * THE LAYER IS HIDDEN UNTIL IT IS STARTED, and that is not a nicety.
   * `setSnapshot` mounts its nodes the moment the data lands - which can be well
   * before the campus reveal has finished - and an un-ticked ghost sits at
   * translate(0,0), i.e. twenty-four students piled in the top-left corner of the
   * plan while the rooms are still staggering in (caught in a browser capture,
   * 2026-08-24: the node suites cannot see it, because a DOM double has no
   * paint). Visible is `started && !paused`, expressed through the [hidden]
   * reset (trap 27) and never a bare `display:`.
   */
  function applyVisibility() {
    if (!mount) return;
    const show = started && !paused;
    try { if (show) mount.removeAttribute('hidden'); else mount.setAttribute('hidden', 'hidden'); }
    catch (e) { /* noop */ }
  }

  function setPaused(on) {
    const next = !!on;
    if (next === paused) return;
    paused = next;
    applyVisibility();
    if (paused) {
      if (rafId) { clock.caf(rafId); rafId = 0; }
      endScene();
      endLook();
      lastFrameMs = 0;
    } else {
      lastFrameMs = 0;
      pump();
    }
  }

  function start() {
    if (destroyed || started) return;
    started = true;
    applyVisibility();
    if (!docBound) {
      try {
        if (typeof document !== 'undefined' && document && typeof document.addEventListener === 'function') {
          document.addEventListener('visibilitychange', onVisibility);
          docBound = true;
        }
      } catch (e) { /* noop */ }
    }
    onVisibility();
    applyVisibility();
    // The first placement is SYNCHRONOUS, so the layer is never revealed for one
    // frame with everybody still standing on the origin.
    pump();
  }

  function stop() {
    started = false;
    applyVisibility();
    if (rafId) { clock.caf(rafId); rafId = 0; }
    endScene();
    endLook();
  }

  function destroy() {
    if (destroyed) return;
    destroyed = true;
    stop();
    if (unsub) { try { unsub(); } catch (e) { /* noop */ } unsub = null; }
    if (docBound) {
      try { document.removeEventListener('visibilitychange', onVisibility); } catch (e) { /* noop */ }
      docBound = false;
    }
    clearLayer();
    plan = null;
    encounters = [];
  }

  /* -------------------------------------------------------------- intake -- */

  const mode = opts.mode === 'fixture' ? 'fixture' : 'bridge';
  if (mode === 'fixture') {
    loadFixture();
  } else if (opts.bridge && typeof opts.bridge.on === 'function') {
    // THE HOST'S PUSH. Multi-subscriber by construction (trap 11), so nothing
    // else's frames are stolen; a host that never pushes leaves the layer empty.
    try {
      unsub = opts.bridge.on('presence', (m) => {
        try {
          if (!m || destroyed) return;
          if (m.self !== undefined) selfId = m.self == null ? null : String(m.self);
          if (m.snapshot) setSnapshot(m.snapshot);
        } catch (e) { say('presence frame threw: ' + ((e && e.message) || e)); }
      });
    } catch (e) { say('presence: no bridge subscription (' + ((e && e.message) || e) + ')'); }
  }

  function loadFixture() {
    let url = opts.fixtureUrl;
    if (!url) {
      try { url = new URL('fixtures/presence.json', import.meta.url).href; }
      catch (e) { url = 'shell/fixtures/presence.json'; }
    }
    if (typeof fetch !== 'function') { say('presence: fixture mode with no fetch - empty layer'); return; }
    try {
      fetch(url)
        .then((r) => (r && r.ok ? r.json() : null))
        .then((j) => { if (j && !destroyed) setSnapshot(j); else if (!j) say('presence: fixture unreadable'); })
        .catch((e) => say('presence: fixture failed (' + ((e && e.message) || e) + ')'));
    } catch (e) { say('presence: fixture threw (' + ((e && e.message) || e) + ')'); }
  }

  return {
    start,
    stop,
    setPaused,
    setSnapshot,
    /** THE SEEP'S SEAM (shell/seep.js tell 05): stop one student mid-crossing
     *  and face them out of the plan. A verb, never a scheduler - the director
     *  owns the rarity and the cooldown. */
    lookOut,
    /** Test seam: drive one frame at an explicit wall time. */
    tick,
    destroy,
    diagnostics() {
      return {
        started, paused, still, mode, timeScale,
        ghosts: plan ? plan.schedules.length : 0,
        overflow: plan ? plan.overflow : 0,
        drawn: nodes.size,
        hash: plan ? plan.hash : '',
        epoch: planEpoch,
        encounters: encounters.length,
        onStage: !!live,
        looking: !!looker,
        self: selfId,
      };
    },
  };
}

export default createGhosts;
