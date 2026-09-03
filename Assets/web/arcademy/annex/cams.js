/* ============================================================================
 * annex/cams.js - the Records Annex camera wall: nine fake CCTV feeds of the
 * campus, plus the locked laptop, for the monitor grid in the Annex art.
 *
 * WHAT A FEED IS. Each cam is a small SVG window onto ONE stretch of the real
 * campus plan - the same viewBox geography campus.js draws (ROOMS rects, the
 * Main Hall's 430..510 corridor, the Gate at 720,908), painted by the same
 * stylesheet classes (.campus-gfloor / .campus-ghall / .campus-stop-a ...), and
 * walked by the same pixel student ghosts.js builds. Nothing here invents a
 * second campus: a room that moves in campus.js moves on every monitor the
 * next time this file's crop tables are re-read against it (same commit rule
 * as ghosts.js FLOOR_RECTS).
 *
 * WHY NINE LITTLE SVGS AND NOT NINE <use> CLONES OF THE PLAN (trap 36). The
 * full plan carries marquee chases, blooms, route dashes and label stacks -
 * nine live clones would mean nine copies of every one of those animated
 * surfaces. A feed is instead a few dozen STATIC nodes (floor, cabinet
 * gradient, furniture, door arc) whose only movers are one to three sprite
 * groups riding transform, plus one shared scanline drift that is a 3px
 * translate loop, never a background-position. No blend modes, no filters
 * over anything live, and the lite rung (.an-lite / html.arc-reduced /
 * prefers-reduced-motion) drops every decorative animation in one place.
 *
 * CHROME ONLY. No bridge, no store, no fetch. Everything arrives through
 * createCamWall(opts) and leaves through the returned handle. The clock is
 * DIEGETIC - a fixed after-hours base advanced by elapsed frame time, so the
 * wall never reads the machine's real clock (and two launches look alike).
 * ==========================================================================*/

import { ROOMS } from '../shell/campus.js';
import { buildSprite } from '../shell/ghosts.js';
import { makeRng } from '../core/rng.js';
import { t as lexT } from '../core/lexicon.js';

const SVGNS = 'http://www.w3.org/2000/svg';

/* ----------------------------------------------------------------------------
 * DIALS
 * --------------------------------------------------------------------------*/
/** Feed crop aspect mirrors the CRT glass in the art (216x156). */
export const CAM_ASPECT = 340 / 245;
/** Walking speed band, viewBox units/s - a touch under the map ghosts' 40..60:
 *  surveillance distance reads slower. */
const WALK_MIN = 26, WALK_MAX = 44;
/** Off-frame beat between one appearance and the next, seconds. */
const AWAY_MIN_S = 4, AWAY_MAX_S = 17;
/** Chance a route pauses mid-walk, and for how long. */
const LOITER_P = 0.35, LOITER_MIN_S = 2, LOITER_MAX_S = 7;
/** Tracking-cut cadence per cam, seconds. */
const CUT_MIN_S = 18, CUT_MAX_S = 64;
/** THE LOOKER - rare enough to be doubted. One shared cooldown for the whole
 *  wall, and a small per-crossing roll on top of it. */
const LOOK_P = 0.03, LOOK_COOLDOWN_S = 140, LOOK_HOLD_S = 1.7;
/** Diegetic wall clock base - the Annex is an after-hours room. */
const CLOCK_BASE_S = 22 * 3600 + 41 * 60 + 7;

/* ONE AUDIO DOOR (W3 P1-22, trap 18): shell/audio.js owns the only audio node
 * on the page, so the cam wall REQUESTS its sounds on `document` in the shape
 * shell/ceremonies.js set. A dropped cue is not an error. */
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
 * THE NINE CHANNELS - one crop of the plan each, reading order = the art's.
 * `crop` is [x, y, w, h] in campus viewBox units at the CRT's own aspect.
 * `lane` y 470 is CAMPUS_HALL_Y - the corridor centre every route walks.
 * --------------------------------------------------------------------------*/
export const CAM_DEFS = Object.freeze([
  { id: 'cam1', room: 'daily_trigger',   crop: [200, 240, 340, 245], kind: 'corridor' },
  { id: 'cam2', room: 'deja_vu',         crop: [440, 240, 340, 245], kind: 'corridor' },
  { id: 'cam3', room: 'impulse_control', crop: [680, 240, 340, 245], kind: 'corridor' },
  { id: 'cam4', room: 'lost_and_found',  crop: [200, 447, 340, 245], kind: 'corridor' },
  { id: 'cam5', room: 'the_deep_end',    crop: [285, 610, 340, 245], kind: 'pool' },
  { id: 'cam6', room: 'sort',            crop: [700, 447, 340, 245], kind: 'corridor' },
  { id: 'cam7', room: 'echo',            crop: [920, 240, 340, 245], kind: 'corridor' },
  { id: 'cam8', room: 'instant_recall',  crop: [920, 447, 340, 245], kind: 'corridor' },
  { id: 'cam9', room: null,              crop: [550, 622, 440, 317], kind: 'gate' },
]);

/** Floors the feeds may show - the campus-ghall rects, verbatim. */
const FLOORS = Object.freeze([
  [200, 430, 1040, 80],   // the Main Hall
  [62, 450, 138, 40],     // the west spur
  [460, 510, 240, 220],   // the Entrance Hall
  [700, 510, 40, 220],    // the gate alley
]);

/* Room furniture, transcribed shape-for-shape from campus.js furnitureFor()
 * for the rooms the wall watches. l=line r=filled-rect ro=outline-rect
 * c=circle; a trailing string is a stroke-dasharray. */
const FURN = Object.freeze({
  daily_trigger: [
    ['l', 248, 216, 412, 216], ['r', 300, 232, 60, 16],
    ...[272, 304].flatMap((y) => [252, 304, 356].map((x) => ['r', x, y, 22, 14])),
  ],
  deja_vu: [[492, 248], [580, 248]].flatMap(([x, y]) => [
    ['r', x, y, 70, 36],
    ...[0, 18, 36].flatMap((dx) => [12, 26].map((dy) => ['c', x + 14 + dx, y + dy, 2])),
  ]),
  impulse_control: [
    ...[266, 298].flatMap((cy) => [742, 778, 814, 850].map((cx) => ['c', cx, cy, 5])),
    ['c', 810, 282, 28, '3 5'],
  ],
  lost_and_found: [
    ...[576, 612, 648].map((y) => ['r', 252, y, 156, 9]),
    ['r', 256, 666, 18, 18], ['r', 282, 670, 14, 14], ['r', 300, 524, 64, 11],
  ],
  the_deep_end: [
    ['l', 443, 512, 443, 738], ['l', 457, 512, 457, 738],
    ['ro', 330, 760, 280, 44], ['ro', 336, 766, 268, 32, '3 5'],
    ...[771, 782, 793].map((y) => ['l', 340, y, 600, y, '9 7']),
    ...[560, 578].map((x) => ['l', x, 752, x, 770]),
    ...[756, 763].map((y) => ['l', 560, y, 578, y]),
    ['r', 306, 774, 20, 14], ['l', 326, 774, 326, 788],
  ],
  sort: [
    ['card', 782, -14], ['card', 840, 0], ['card', 898, 14],
    ['l', 762, 668, 918, 668],
  ],
  echo: [
    ...[1013, 1037, 1061, 1085, 1109, 1133].map((x) => ['r', x, 220, 14, 14]),
    ['l', 1013, 240, 1147, 240],
  ],
  instant_recall: [
    ...[1019, 1049, 1079, 1109, 1139].map((x) => ['r', x, 520, 22, 16]),
    ['l', 1019, 540, 1161, 540, '4 6'],
  ],
});

/* ----------------------------------------------------------------------------
 * SVG kit (ghosts.js's shape, module-local like everyone's)
 * --------------------------------------------------------------------------*/
function svg(tag, attrs, cls) {
  const n = document.createElementNS(SVGNS, tag);
  if (cls) n.setAttribute('class', cls);
  if (attrs) for (const k of Object.keys(attrs)) n.setAttribute(k, String(attrs[k]));
  return n;
}
function svgText(x, y, cls, text) {
  const n = svg('text', { x, y }, cls);
  n.textContent = text;
  return n;
}
function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}
function hits(crop, x, y, w, h) {
  return x < crop[0] + crop[2] && x + w > crop[0] && y < crop[1] + crop[3] && y + h > crop[1];
}
const easeInOutSine = (p) => 0.5 - Math.cos(Math.PI * p) / 2;

/* ----------------------------------------------------------------------------
 * SCENE - one cam's static geometry
 * --------------------------------------------------------------------------*/
function buildScene(cam) {
  const crop = cam.crop;
  /* `campus-plan` on purpose: the [data-game] identity rules in styles.css are
   * scoped to it, and they are what paint the cabinet gradients below. */
  const plan = svg('svg', {
    viewBox: crop.join(' '), preserveAspectRatio: 'xMidYMid slice',
    'aria-hidden': 'true',
  }, 'campus-plan cam-plan');

  const defs = svg('defs');
  plan.appendChild(defs);
  plan.appendChild(svg('rect', {
    x: crop[0] - 8, y: crop[1] - 8, width: crop[2] + 16, height: crop[3] + 16,
  }, 'an-sky'));

  FLOORS.forEach(([x, y, w, h]) => {
    if (hits(crop, x, y, w, h)) plan.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-ghall'));
  });

  if (cam.kind === 'gate') {
    const g = svg('g', null, 'campus-grounds');
    [[695, 792], [745, 792]].forEach(([x, y]) => g.appendChild(svg('line', { x1: x, y1: y, x2: x, y2: 940 }, 'campus-path-edge')));
    g.appendChild(svg('line', { x1: 720, y1: 800, x2: 720, y2: 940, 'stroke-dasharray': '2 10' }, 'campus-path-mid'));
    [[662, 842], [778, 842]].forEach(([cx, cy]) => {
      g.appendChild(svg('circle', { cx, cy, r: 15 }, 'campus-lamphalo'));
      g.appendChild(svg('circle', { cx, cy, r: 3 }, 'campus-lamp'));
    });
    // the Gate itself: two posts astride the 908 line the nightly route walks
    [[688, 894], [744, 894]].forEach(([x, y]) => g.appendChild(svg('rect', { x, y, width: 8, height: 22 }, 'campus-furnf')));
    g.appendChild(svg('line', { x1: 688, y1: 894, x2: 752, y2: 894 }, 'campus-furn'));
    plan.appendChild(g);
  }

  Object.keys(ROOMS).forEach((key) => {
    const spec = ROOMS[key];
    const [x, y, w, h] = spec.rect;
    if (!hits(crop, x, y, w, h)) return;
    const focus = key === cam.room;
    const g = svg('g', null, 'campus-room open');
    g.setAttribute('data-game', key);
    g.appendChild(svg('rect', { x, y, width: w, height: h }, 'campus-gfloor'));
    /* ids are document-global and ten feeds share one document, so every
     * gradient id carries its cam. */
    const gid = 'anG-' + cam.id + '-' + key;
    const lg = svg('linearGradient', { id: gid, x1: 0, y1: 0, x2: 0, y2: 1 });
    lg.appendChild(svg('stop', { offset: '0', 'data-game': key }, 'campus-stop-a'));
    lg.appendChild(svg('stop', { offset: '1', 'data-game': key }, 'campus-stop-b'));
    defs.appendChild(lg);
    g.appendChild(svg('rect', { x, y, width: w, height: h, fill: 'url(#' + gid + ')' }, 'campus-cabinet'));
    if (focus && FURN[key]) {
      FURN[key].forEach((f) => {
        const dash = typeof f[f.length - 1] === 'string' ? f[f.length - 1] : null;
        if (f[0] === 'l') {
          const n = svg('line', { x1: f[1], y1: f[2], x2: f[3], y2: f[4] }, 'campus-furn');
          if (dash) n.setAttribute('stroke-dasharray', dash);
          g.appendChild(n);
        } else if (f[0] === 'r' || f[0] === 'ro') {
          const n = svg('rect', { x: f[1], y: f[2], width: f[3], height: f[4] }, f[0] === 'r' ? 'campus-furnf' : 'campus-furn');
          if (dash) n.setAttribute('stroke-dasharray', dash);
          g.appendChild(n);
        } else if (f[0] === 'c') {
          const n = svg('circle', { cx: f[1], cy: f[2], r: f[3] }, 'campus-furn');
          if (dash) n.setAttribute('stroke-dasharray', dash);
          g.appendChild(n);
        } else if (f[0] === 'card') {
          const n = svg('rect', { x: f[1] - 21, y: 595, width: 42, height: 58, rx: 3 }, 'campus-furnf');
          if (f[2]) n.setAttribute('transform', 'rotate(' + f[2] + ' ' + f[1] + ' 624)');
          g.appendChild(n);
        }
      });
    }
    // the drawn plates - the plan's own lexicon text, exactly as the map sets it
    if (focus) {
      const nameY = spec.nameY != null ? spec.nameY : (spec.side === 'n' ? y + 156 : y + 46);
      g.appendChild(svgText(x + w / 2, nameY, 'campus-rname', lexT(spec.nameKey, spec.nameEn).toUpperCase()));
      g.appendChild(svgText(x + w / 2, nameY + 16, 'campus-rsub tiny',
        (lexT('campus_rm', 'RM') + ' ' + spec.rm).toUpperCase()));
    }
    // the door symbol: the gap in the wall plus the swinging leaf
    const d = spec.door;
    if (spec.side === 'n') {
      const wy = spec.wallY != null ? spec.wallY : 430;
      g.appendChild(svg('line', { x1: d - 12, y1: wy, x2: d + 12, y2: wy }, 'campus-gap'));
      g.appendChild(svg('path', { d: 'M' + (d + 12) + ',' + wy + ' A24,24 0 0 1 ' + (d - 12) + ',' + (wy + 24) + ' L' + (d - 12) + ',' + wy }, 'campus-door'));
    } else if (spec.side === 's') {
      const wy = spec.wallY != null ? spec.wallY : 510;
      g.appendChild(svg('line', { x1: d - 12, y1: wy, x2: d + 12, y2: wy }, 'campus-gap'));
      g.appendChild(svg('path', { d: 'M' + (d + 12) + ',' + wy + ' A24,24 0 0 0 ' + (d - 12) + ',' + (wy - 24) + ' L' + (d - 12) + ',' + wy }, 'campus-door'));
    }
    plan.appendChild(g);
  });

  const students = svg('g', null, 'campus-students');
  students.setAttribute('aria-hidden', 'true');
  plan.appendChild(students);
  return { plan, students };
}

/* ----------------------------------------------------------------------------
 * ROUTES - waypoint recipes per cam kind. Every route returns [[x,y],...] plus
 * how it enters and leaves ('edge' walks in from off-frame, 'door' fades at a
 * doorway the way a body crossing a threshold drops off a camera).
 * --------------------------------------------------------------------------*/
function corridorRoutes(cam) {
  const [cx, , cw] = cam.crop;
  const minX = cx - 40, maxX = cx + cw + 40;
  const spec = ROOMS[cam.room];
  const wy = spec.side === 'n' ? (spec.wallY != null ? spec.wallY : 430) : (spec.wallY != null ? spec.wallY : 510);
  const doorY = spec.side === 'n' ? wy + 8 : wy - 8;
  return [
    { w: 0.28, mk: (r) => { const y = 458 + r() * 26; return { pts: [[minX, y], [maxX, y]], in: 'edge', out: 'edge', look: true }; } },
    { w: 0.28, mk: (r) => { const y = 458 + r() * 26; return { pts: [[maxX, y], [minX, y]], in: 'edge', out: 'edge', look: true }; } },
    { w: 0.22, mk: (r) => { const y = 458 + r() * 26; const j = (r() - 0.5) * 10; const from = r() < 0.5 ? minX : maxX; return { pts: [[from, y], [spec.door + j, y], [spec.door + j, doorY]], in: 'edge', out: 'door' }; } },
    { w: 0.22, mk: (r) => { const y = 458 + r() * 26; const j = (r() - 0.5) * 10; const to = r() < 0.5 ? minX : maxX; return { pts: [[spec.door + j, doorY], [spec.door + j, y], [to, y]], in: 'door', out: 'edge' }; } },
  ];
}
function poolRoutes() {
  return [
    { w: 0.4, mk: (r) => { const x = 574 + r() * 12; return { pts: [[x, 566], [x, 724], [x, 566]], in: 'edge', out: 'edge' }; } },
    { w: 0.35, mk: (r) => { const j = (r() - 0.5) * 8; return { pts: [[450 + j, 600], [450 + j, 748], [620, 748], [620, 826], [380, 826], [620, 826], [620, 748], [450 + j, 748], [450 + j, 600]], in: 'edge', out: 'edge' }; } },
    { w: 0.25, mk: (r) => { const x = 574 + r() * 12; return { pts: [[x, 566], [x, 700]], in: 'edge', out: 'door' }; } },
  ];
}
function gateRoutes() {
  return [
    { w: 0.38, mk: (r) => { const x = 712 + r() * 16; return { pts: [[x, 600], [x, 952]], in: 'edge', out: 'edge' }; } },
    { w: 0.38, mk: (r) => { const x = 712 + r() * 16; return { pts: [[x, 952], [x, 600]], in: 'edge', out: 'edge' }; } },
    { w: 0.24, mk: (r) => { const x = 712 + r() * 16; return { pts: [[x, 600], [x, 838], [676, 846], [x, 838], [x, 600]], in: 'edge', out: 'edge' }; } },
  ];
}
function routesFor(cam) {
  if (cam.kind === 'pool') return poolRoutes();
  if (cam.kind === 'gate') return gateRoutes();
  return corridorRoutes(cam);
}
function pickRoute(routes, r) {
  let roll = r(); // one draw against the summed weights
  const total = routes.reduce((a, b) => a + b.w, 0);
  roll *= total;
  for (const rt of routes) { if ((roll -= rt.w) <= 0) return rt.mk(r); }
  return routes[0].mk(r);
}

/* ----------------------------------------------------------------------------
 * ACTOR - one student on one channel. A tiny state machine over route legs;
 * every random draw comes from its own seeded stream, so a reload replays the
 * same evening shift.
 * --------------------------------------------------------------------------*/
function makeActor(wall, cam, idx, students) {
  const rng = makeRng('annex-cams|' + cam.id + '|' + idx);
  const g = svg('g', { transform: 'translate(-999,-999)' }, 'campus-student an-actor');
  const inner = svg('g', null, 'gh-inner');
  const sprite = buildSprite('annex|' + cam.id + '|' + idx);
  if (sprite) inner.appendChild(sprite);
  g.appendChild(inner);
  students.appendChild(g);

  const a = {
    g, inner, rng, state: 'away', until: rng() * (AWAY_MAX_S - AWAY_MIN_S) + 1 + idx * 2,
    route: null, leg: 0, legT: 0, legDur: 0, x: 0, y: 0, facing: 1,
    looked: false, lookAt: -1,
  };

  a.tick = (now, dt) => {
    if (a.state === 'away') {
      if (now < a.until) return;
      a.route = pickRoute(wall.routes[cam.id], rng);
      a.leg = 0; a.looked = false;
      const p0 = a.route.pts[0];
      a.x = p0[0]; a.y = p0[1];
      startLeg(now);
      place();
      g.classList.remove('an-out');
      g.classList.add('an-walk');
      if (a.route.in === 'door') g.classList.add('an-in');
      requestAnimationFrame(() => g.classList.remove('an-in'));
      a.state = 'walk';
      return;
    }
    if (a.state === 'loiter' || a.state === 'look') {
      if (a.state === 'look' && a.lookAt > 0 && now >= a.lookAt) {
        wall.roll(cam.id); a.lookAt = -1;
      }
      if (now < a.until) return;
      g.classList.remove('an-look');
      g.classList.add('an-walk');
      a.state = 'walk';
      return;
    }
    // walk
    a.legT += dt;
    const p = a.legDur > 0 ? Math.min(1, a.legT / a.legDur) : 1;
    const e = easeInOutSine(p);
    const [x0, y0] = a.route.pts[a.leg];
    const [x1, y1] = a.route.pts[a.leg + 1];
    a.x = x0 + (x1 - x0) * e; a.y = y0 + (y1 - y0) * e;
    if (Math.abs(x1 - x0) > 1) a.facing = x1 > x0 ? 1 : -1;
    place();
    /* THE LOOKER. Mid-crossing, wall-wide cooldown, tiny roll - and only on a
     * leg that faces the lens (a long horizontal cross). */
    if (!a.looked && a.route.look && p > 0.5 && Math.abs(x1 - x0) > 200
        && now - wall.lastLook > LOOK_COOLDOWN_S && rng() < LOOK_P) {
      a.looked = true; wall.lastLook = now;
      /* W3 P1-22: SOMEBODY LOOKED AT THE LENS. The rarest thing on the wall -
       * one shared 140s cooldown gates it, so this cue is rate-limited by the
       * beat itself and needs no throttle of its own. Barely there on purpose:
       * a tape flinching, not an alarm. Doubting you heard it is the point. */
      sfx('glitch', 0.08);
      a.state = 'look'; a.until = now + LOOK_HOLD_S; a.lookAt = now + 0.8;
      g.classList.remove('an-walk'); g.classList.add('an-look');
      return;
    }
    if (p >= 1) {
      a.leg += 1;
      if (a.leg >= a.route.pts.length - 1) {
        // route done - leave the way the route says to
        if (a.route.out === 'door') g.classList.add('an-out');
        a.state = 'away';
        a.until = now + AWAY_MIN_S + rng() * (AWAY_MAX_S - AWAY_MIN_S);
        g.classList.remove('an-walk');
        if (a.route.out !== 'door') a.g.setAttribute('transform', 'translate(-999,-999)');
        return;
      }
      if (rng() < LOITER_P) {
        a.state = 'loiter';
        a.until = now + LOITER_MIN_S + rng() * (LOITER_MAX_S - LOITER_MIN_S);
        g.classList.remove('an-walk');
      }
      startLeg(now);
    }
  };

  function startLeg() {
    const [x0, y0] = a.route.pts[a.leg];
    const [x1, y1] = a.route.pts[a.leg + 1];
    const dist = Math.hypot(x1 - x0, y1 - y0);
    const speed = WALK_MIN + a.rng() * (WALK_MAX - WALK_MIN);
    a.legDur = Math.max(0.2, dist / speed);
    a.legT = 0;
  }
  function place() {
    a.g.setAttribute('transform', 'translate(' + (Math.round(a.x * 10) / 10) + ',' + (Math.round(a.y * 10) / 10) + ')');
    a.inner.setAttribute('transform', a.facing < 0 ? 'scale(-1,1)' : 'scale(1,1)');
  }
  return a;
}

/* ----------------------------------------------------------------------------
 * NOISE - one seeded 96px grain tile, shared by every feed as a data: url.
 * Drawn once at create; only the cut beat ever animates it (transform steps).
 * --------------------------------------------------------------------------*/
function noiseUrl() {
  const c = document.createElement('canvas');
  c.width = 96; c.height = 96;
  const ctx = c.getContext('2d');
  if (!ctx) return '';
  const img = ctx.createImageData(96, 96);
  const r = makeRng('annex-cams|noise');
  for (let i = 0; i < img.data.length; i += 4) {
    const v = Math.floor(r() * 255);
    img.data[i] = v; img.data[i + 1] = v; img.data[i + 2] = v;
    img.data[i + 3] = 26 + Math.floor(r() * 36);
  }
  ctx.putImageData(img, 0, 0);
  try { return c.toDataURL('image/png'); } catch (e) { return ''; }
}

/* ----------------------------------------------------------------------------
 * THE WALL
 * --------------------------------------------------------------------------*/
/**
 * @param {Object} [opts]
 * @param {Function} [opts.t]    lexicon resolver, t(key, fallback)
 * @param {boolean} [opts.lite]  drop decorative animation (an .ae-lite sibling)
 * @param {number}  [opts.cast]  students per channel (default 3; gate gets +1)
 * @returns {{root, tiles, laptop, start, stop, destroy}}
 */
export function createCamWall(opts) {
  const o = opts || {};
  const t = o.t || lexT;
  const root = el('div', 'an-camwall' + (o.lite ? ' an-lite' : ''));
  const grain = noiseUrl();
  const tiles = {};
  const scenes = [];
  const actors = [];
  const rollEls = {};
  const clockEls = [];
  const cuts = [];

  const wall = { routes: {}, lastLook: -1e9, roll: (id) => fireRoll(id) };

  CAM_DEFS.forEach((cam, i) => {
    const tile = el('div', 'cam-tile');
    tile.dataset.cam = cam.id;
    const scene = buildScene(cam);
    tile.appendChild(scene.plan);

    const fx = el('div', 'cam-fx');
    const scan = el('div', 'cam-scan');
    const noise = el('div', 'cam-noise');
    if (grain) noise.style.backgroundImage = 'url(' + grain + ')';
    const roll = el('div', 'cam-roll');
    fx.appendChild(scan); fx.appendChild(noise); fx.appendChild(roll);
    fx.appendChild(el('div', 'cam-grade'));
    tile.appendChild(fx);

    const osd = el('div', 'cam-osd');
    const idLbl = el('span', 'cam-osd-id',
      t('annex_cam', 'CAM') + ' ' + String(i + 1).padStart(2, '0'));
    const rec = el('span', 'cam-osd-rec');
    rec.appendChild(el('i', 'cam-dot'));
    rec.appendChild(el('em', 'cam-rec-word', t('annex_rec', 'REC')));
    const clock = el('em', 'cam-clock', '');
    rec.appendChild(clock);
    clockEls.push(clock);
    const spec = cam.room ? ROOMS[cam.room] : null;
    const roomLbl = el('span', 'cam-osd-room', spec
      ? (t('campus_rm', 'RM') + ' ' + spec.rm + ' · ' + t(spec.nameKey, spec.nameEn)).toUpperCase()
      : t('annex_cam_gate', 'MAIN GATE').toUpperCase());
    osd.appendChild(idLbl); osd.appendChild(rec); osd.appendChild(roomLbl);
    tile.appendChild(osd);

    root.appendChild(tile);
    tiles[cam.id] = tile;
    scenes.push(scene);
    rollEls[cam.id] = roll;
    wall.routes[cam.id] = routesFor(cam);

    const castN = (o.cast || 4) + (cam.kind === 'gate' ? 1 : 0);
    for (let k = 0; k < castN; k++) actors.push(makeActor(wall, cam, k, scene.students));

    const cutRng = makeRng('annex-cams|cuts|' + cam.id);
    cuts.push({ tile, rng: cutRng, next: 4 + cutRng() * CUT_MAX_S });
  });

  /* the laptop - dark glass and a locked prompt; the fake OS is a later phase */
  const laptop = el('div', 'cam-tile cam-laptop');
  laptop.dataset.cam = 'laptop';
  const lap = el('div', 'lap-screen');
  lap.appendChild(el('div', 'lap-title', t('annex_lap_title', 'RECORDS ANNEX').toUpperCase()));
  lap.appendChild(el('div', 'lap-locked', t('annex_lap_locked', 'TERMINAL LOCKED').toUpperCase()));
  const prompt = el('div', 'lap-prompt', t('annex_lap_prompt', 'AWAITING KEY').toUpperCase() + ' ');
  prompt.appendChild(el('i', 'lap-caret'));
  lap.appendChild(prompt);
  laptop.appendChild(lap);
  laptop.appendChild(el('div', 'cam-grade'));
  root.appendChild(laptop);
  tiles.laptop = laptop;

  /* ---- driver: one rAF for the whole wall ---- */
  let raf = 0, last = 0, sim = 0, lastSecond = -1, running = false;
  const reduced = (typeof matchMedia === 'function'
    && matchMedia('(prefers-reduced-motion: reduce)').matches)
    || document.documentElement.classList.contains('arc-reduced');
  const still = reduced || !!o.lite;

  function frame(ts) {
    if (!running) return;
    if (!last) last = ts;
    let dt = (ts - last) / 1000;
    last = ts;
    if (dt > 0.25) dt = 0.25;  // a background tab does not teleport students
    sim += dt;
    for (const a of actors) a.tick(sim, dt);
    const secs = Math.floor(sim);
    if (secs !== lastSecond) {
      lastSecond = secs;
      const cs = (CLOCK_BASE_S + secs) % 86400;
      const hh = String(Math.floor(cs / 3600)).padStart(2, '0');
      const mm = String(Math.floor((cs % 3600) / 60)).padStart(2, '0');
      const ss = String(cs % 60).padStart(2, '0');
      const label = hh + ':' + mm + ':' + ss;
      for (const cEl of clockEls) cEl.textContent = label;
    }
    if (!still) {
      for (const c of cuts) {
        if (sim >= c.next) {
          c.next = sim + CUT_MIN_S + c.rng() * (CUT_MAX_S - CUT_MIN_S);
          c.tile.classList.add('cam-cut');
          setTimeout(() => c.tile.classList.remove('cam-cut'), 280);
        }
      }
    }
    raf = requestAnimationFrame(frame);
  }

  function fireRoll(id) {
    if (still) return;
    const r = rollEls[id];
    if (!r) return;
    r.classList.remove('go');
    void r.offsetWidth;   // restart the one-shot
    r.classList.add('go');
  }

  function onVis() {
    if (document.hidden) { if (raf) cancelAnimationFrame(raf); raf = 0; last = 0; }
    else if (running && !raf) raf = requestAnimationFrame(frame);
  }
  document.addEventListener('visibilitychange', onVis);

  function start() {
    if (running) return;
    running = true; last = 0;
    /* W3 P1-22: nine CRTs and not one sound. `cam_bed` is a HOLD - it loops
     * until somebody lets go of it - and stop() below is its owner, which
     * destroy() reaches too. SAMPLE-ONLY: no mp3, no bed, no fallback, and a
     * silent wall is the honest answer rather than a synthesised hum. */
    sfx('cam_bed', 0.25, { bus: 'music', hold: true });
    if (!still) {
      // power-on, one stagger down the grid - the emi field-trip 200ms beat
      Object.keys(tiles).forEach((id, i) => {
        const tl = tiles[id];
        setTimeout(() => {
          tl.classList.add('cam-boot');
          setTimeout(() => tl.classList.remove('cam-boot'), 600);
        }, i * 90);
      });
    }
    raf = requestAnimationFrame(frame);
  }
  function stop() {
    running = false;
    sfx('cam_bed', 0.25, { bus: 'music', stop: true });   // W3 P1-22: the bed's owner
    if (raf) cancelAnimationFrame(raf);
    raf = 0; last = 0;
  }
  function destroy() {
    stop();
    document.removeEventListener('visibilitychange', onVis);
    try { root.remove(); } catch (e) { /* noop */ }
  }

  return { root, tiles, laptop, start, stop, destroy };
}

export default createCamWall;
