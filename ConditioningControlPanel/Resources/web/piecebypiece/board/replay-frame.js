/* ============================================================================
 * board/replay-frame.js - the maths behind the replay cameras. Pure: plain
 * arrays, no THREE, no DOM, so node can check it (smoke/replay-shots-smoke.mjs).
 * board/replay-shots.js feeds it the men and stands the real cameras.
 *
 * A panel camera renders the whole drawing buffer with a view offset that puts
 * its optical axis on the panel's centre, so "centred in the panel" is simply
 * "on the axis", and a size in the panel is a size in NDC divided by the
 * panel's share of the stage. Everything here measures in PANEL units: -1..1
 * across the panel's bounding box on each axis.
 *
 * Vectors are [x, y, z]. A subject is a list of spheres [x, y, z, r].
 * ==========================================================================*/

// ---- the shot bank -------------------------------------------------------------
// size   0 extreme close .. 4 overhead; no two panels of one replay share a size
// fov    vertical, degrees, for the whole buffer
// el/az  where the lens stands, radians. el up from the floor. az 0 is square on
//        from the player's side of the move, + swings round BEHIND the attacker
//        (a chase), - round in front of him (behind the victim)
// share  how much of the panel (its larger axis) the subject fills
// window seconds either side of contact whose poses the frame must hold
// ceil   poses higher than this (a stomp's hop, a flip) count only up to it: the man
//        may leave the top of the frame at the apex rather than shrink the whole shot
// flight true: the victim's flight after contact counts (rook fling)
// motion what moves inside the shot (see motion())
export const SHOT_BANK = Object.freeze({
  xclose: { size: 0, fov: 30, el: [.1, .26], az: [-.4, .4], share: .85, window: [-.12, .12], ceil: 9, subject: 'contact', motion: 'dolly' },
  close:  { size: 1, fov: 34, el: [.2, .34], az: [-.5, .5], share: .72, window: [-.28, .3], ceil: 1.25, subject: 'pair', motion: 'orbit' },
  low:    { size: 1, fov: 40, el: [-.05, .05], az: [-.45, .45], share: .7, window: [-.3, .35], ceil: 1.35, subject: 'pair', motion: 'push' },
  medium: { size: 2, fov: 40, el: [.3, .44], az: [.62, 1.0], share: .68, window: [-.5, .45], ceil: 1.6, subject: 'pair', motion: 'push' },
  wide:   { size: 3, fov: 44, el: [.44, .62], az: [-.8, .8], share: .62, window: [-1.2, .9], ceil: 1.8, subject: 'pair', flight: true, motion: 'drift' },
  top:    { size: 4, fov: 34, el: [1.2, 1.36], az: [-1.2, 1.2], share: .6, window: [-1.0, .9], ceil: 2.6, subject: 'pair', flight: true, motion: 'spin' },
});

// Tunables for placing and moving the lens.
export const FRAME = Object.freeze({
  minDist: 1.25,      // never nearer the subject's centre than this
  maxDist: 20,        // nor further (the room is a 60 unit dome; this stays well inside it)
  floorY: .24,        // the lens never goes lower than this above the board
  flightReach: 1.8,   // a flung victim counts for framing this far from contact, no further
  contactR: .24,      // the extreme close holds this ball round the contact point
  manR: .3,           // a man, as a column this wide
  nearHide: .75,      // a man whose skin comes nearer the lens than this is hidden
  bigHide: .5,        // as is one that would cover this share of the panel
  frontSlack: .2,     // "in front": nearer than the subject's nearest point plus this
  pad: .3,            // the subject's box grows this much (panel units) for the in-front test
});

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const clamp01 = v => clamp(v, 0, 1);
const smooth = (a, b, x) => { const t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); };
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const add = (a, b, k = 1) => [a[0] + b[0] * k, a[1] + b[1] * k, a[2] + b[2] * k];
const len = a => Math.hypot(a[0], a[1], a[2]);
const norm = a => { const l = len(a) || 1; return [a[0] / l, a[1] / l, a[2] / l]; };
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

/** Unit vector from the subject to the lens, for an elevation and azimuth in the move's own frame. */
export function lensDir(el, az, dN, perp) {
  const c = Math.cos(el), h = [perp[0] * Math.cos(az) - dN[0] * Math.sin(az), 0, perp[2] * Math.cos(az) - dN[2] * Math.sin(az)];
  return norm([h[0] * c, Math.sin(el), h[2] * c]);
}

/** The lens's own axes looking from `pos` at `target` with world up +y (as THREE's lookAt). */
export function basis(pos, target) {
  const fwd = norm(sub(target, pos));
  let right = cross(fwd, [0, 1, 0]);
  if (len(right) < 1e-4) right = [1, 0, 0];
  right = norm(right);
  return { fwd, right, up: cross(right, fwd) };
}

/**
 * Project spheres into PANEL units for a lens. cam = { pos, fwd, right, up, fov, aspect }.
 * Returns { rect: [x0, y0, x1, y1] | null, near, far } (near/far = depth along the axis).
 */
export function projectSpheres(spheres, cam, panel) {
  const tv = Math.tan(cam.fov * Math.PI / 360), th = tv * cam.aspect;
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity, near = Infinity, far = -Infinity;
  for (const s of spheres) {
    const d = sub(s, cam.pos), z = dot(d, cam.fwd), r = s[3] || 0;
    near = Math.min(near, z - r); far = Math.max(far, z + r);
    if (z < .05) continue;
    const x = dot(d, cam.right) / (z * th) / panel.w, y = dot(d, cam.up) / (z * tv) / panel.h;
    const rx = r / (z * th) / panel.w, ry = r / (z * tv) / panel.h;
    x0 = Math.min(x0, x - rx); x1 = Math.max(x1, x + rx); y0 = Math.min(y0, y - ry); y1 = Math.max(y1, y + ry);
  }
  return { rect: x1 >= x0 ? [x0, y0, x1, y1] : null, near, far };
}

/**
 * Stand a lens so `spheres` sit centred in the panel and fill `share` of it.
 * dir: unit vector from subject to lens. panel: { w, h } share of the stage.
 * Returns { pos, target, dist }.
 */
export function fitFrame({ spheres, dir, fov, aspect, panel, share, centre = null }) {
  let lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
  for (const s of spheres) for (let k = 0; k < 3; k++) { lo[k] = Math.min(lo[k], s[k] - s[3]); hi[k] = Math.max(hi[k], s[k] + s[3]); }
  // With a centre (the contact), the lens looks straight at it and the frame is sized
  // to the subject's furthest reach from it; without, the subject's box is centred.
  let target = centre ? centre.slice(0, 3) : [(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2];
  const R = len(sub(hi, lo)) / 2;
  const tv = Math.tan(fov * Math.PI / 360);
  let dist = clamp(R / (share * tv * Math.min(panel.h, panel.w * aspect)), FRAME.minDist, FRAME.maxDist);
  for (let it = 0; it < 6; it++) {
    const pos = add(target, dir, dist), b = basis(pos, target);
    const cam = { pos, ...b, fov, aspect };
    const { rect, near } = projectSpheres(spheres, cam, panel);
    if (!rect) { dist *= 1.5; continue; }
    let ext;
    if (centre) ext = Math.max(-rect[0], rect[2], -rect[1], rect[3]);
    else {
      // Slide the target so the box centre lands on the axis.
      const cx = (rect[0] + rect[2]) / 2, cy = (rect[1] + rect[3]) / 2;
      ext = Math.max(rect[2] - rect[0], rect[3] - rect[1]) / 2;
      target = add(add(target, b.right, cx * panel.w * dist * tv * aspect), b.up, cy * panel.h * dist * tv);
    }
    // Then scale the distance to the share wanted.
    let want = dist * ext / share;
    if (near < .35) want = Math.max(want, dist + (.35 - near));
    dist = clamp(want, FRAME.minDist, FRAME.maxDist);
  }
  return { pos: add(target, dir, dist), target, dist };
}

/** Keep a lens above the board and inside the room. Returns a new position. */
export function clampLens(pos) {
  const p = [pos[0], Math.max(FRAME.floorY, pos[1]), pos[2]];
  const r = len(p);
  return r > FRAME.maxDist + 4 ? p.map(v => v * (FRAME.maxDist + 4) / r) : p;
}

/**
 * Should a man be hidden from this panel? man = { base, top, r }.
 * subject = { rect, near } of the action in panel units (from projectSpheres).
 */
export function hides(man, cam, panel, subject) {
  const mid = [(man.base[0] + man.top[0]) / 2, (man.base[1] + man.top[1]) / 2, (man.base[2] + man.top[2]) / 2];
  const col = [[...man.base, man.r], [...mid, man.r], [...man.top, man.r]];
  // Too near the lens: his skin would fill the frame (or the lens would be in him).
  for (const s of col) if (len(sub(s, cam.pos)) - s[3] < FRAME.nearHide) return true;
  const { rect, near } = projectSpheres(col, cam, panel);
  if (!rect) return false;                                           // behind the lens
  const vis = [clamp(rect[0], -1, 1), clamp(rect[1], -1, 1), clamp(rect[2], -1, 1), clamp(rect[3], -1, 1)];
  const area = Math.max(0, vis[2] - vis[0]) * Math.max(0, vis[3] - vis[1]) / 4;
  if (area > FRAME.bigHide) return true;
  if (!subject?.rect) return false;
  const p = FRAME.pad, s = subject.rect;
  const overlap = rect[0] < s[2] + p && rect[2] > s[0] - p && rect[1] < s[3] + p && rect[3] > s[1] - p;
  return overlap && near < subject.near + FRAME.frontSlack;
}

// ---- motion inside a shot ---------------------------------------------------------
/**
 * What moves at `u` seconds from contact (negative before). `seed` is 0..1,
 * fixed per panel (orbit direction, drift phase). Returns multipliers and offsets
 * on the fitted shot: { dist, az, el, fov, drift: [x, y, z] (units of dist) }.
 */
export function motion(kind, u, seed = .5) {
  const sgn = seed < .5 ? -1 : 1, ph = seed * 40;
  const out = { dist: 1, az: 0, el: 0, fov: 1, drift: [0, 0, 0] };
  // A little handheld on everything, a bit more on the wide.
  const hand = kind === 'drift' ? .006 : .0025;
  out.drift = [Math.sin(u * 1.7 + ph) * hand, Math.sin(u * 2.3 + ph * 1.3) * hand * .7, Math.cos(u * 1.9 + ph * .7) * hand];
  if (kind === 'push') {
    out.dist = 1.12 - .2 * smooth(-1.1, .7, u);                    // eases in toward the hit
  } else if (kind === 'orbit') {
    out.az = sgn * .22 * clamp(u / 1.4, -1, 1);                     // a slow turn round the contact
    out.dist = 1.04 - .06 * smooth(-.6, .6, u);
  } else if (kind === 'dolly') {
    const k = 1 + .5 * smooth(-.08, .5, u);                         // vertigo: back off, narrow in
    out.dist = k * (1.06 - .06 * smooth(-.8, 0, u));
    out.fov = 1 / k;
  } else if (kind === 'drift') {
    out.az = sgn * .12 * clamp(u / 1.5, -1, 1);
    out.dist = 1.06 - .08 * smooth(-1.2, .9, u);
  } else if (kind === 'spin') {
    out.az = sgn * .3 * clamp(u / 1.5, -1, 1);                      // the board turns under an overhead
    out.el = -.08 * smooth(-1, .9, u);
    out.dist = 1.1 - .14 * smooth(-1, .6, u);
  }
  return out;
}

/** The fov actually used, for a base fov and a dolly multiplier on tan(fov/2). */
export const fovFor = (fov, mul) => 360 / Math.PI * Math.atan(Math.tan(fov * Math.PI / 360) * mul);

// ---- choosing a replay's shots --------------------------------------------------------
const PANELS = { trio: 3, duo: 2, corner: 1 };
// The strongest angle for the big panel, by what the capture is.
function leadFor(info) {
  const piece = info?.piece, impact = info?.impact;
  if (impact === 'fling' || piece === 'r') return { medium: 3, wide: 2, low: 2 };      // it travels
  if (piece === 'n' || piece === 'q') return { low: 4, close: 2, medium: 2 };          // tall: from low, looking up
  if (piece === 'k' || impact === 'squash' && piece !== 'p') return { low: 3, xclose: 2, close: 2 };
  if (piece === 'b' || impact === 'slap') return { close: 3, low: 2, xclose: 1 };
  return { low: 3, close: 3, medium: 1 };
}
function weighted(table, random) {
  const keys = Object.keys(table).filter(k => table[k] > 0);
  let total = keys.reduce((a, k) => a + table[k], 0), r = random() * total;
  for (const k of keys) { r -= table[k]; if (r < 0) return k; }
  return keys[keys.length - 1];
}

/**
 * The shots for one replay, one per panel, panel 0 the strongest. No two share
 * a size; a trio always has a context shot (wide or overhead); a fling always
 * gets one that shows the flight. Each shot is { name, az, el, seed }.
 */
export function pickShots(layout, info, random = Math.random) {
  const n = PANELS[layout] || 1;
  const lead = { ...leadFor(info) };
  if (n === 1) { delete lead.xclose; delete lead.wide; delete lead.top; if (!Object.keys(lead).length) lead.close = 1; }
  const names = [weighted(lead, random)];
  const tall = info?.piece === 'n' || info?.piece === 'q';
  const travels = info?.impact === 'fling' || info?.piece === 'r';
  while (names.length < n) {
    const used = new Set(names.map(k => SHOT_BANK[k].size));
    const last = SHOT_BANK[names[names.length - 1]].size;
    const pool = {};
    for (const k of Object.keys(SHOT_BANK)) {
      const sz = SHOT_BANK[k].size;
      if (used.has(sz)) continue;
      let w = 1 + Math.abs(sz - last);                               // contrast with the panel before
      if (tall && k === 'top') w *= .2;                              // a backflip reads badly from above
      if (travels && (k === 'wide' || k === 'top')) w *= 2;
      if (layout === 'duo' && k === 'top') w *= .6;
      pool[k] = w;
    }
    names.push(weighted(pool, random));
  }
  if (n === 3 && !names.some(k => SHOT_BANK[k].size >= 3)) names[2] = tall ? 'wide' : random() < .5 ? 'wide' : 'top';
  if (travels && n > 1 && !names.some(k => SHOT_BANK[k].flight)) names[n - 1] = 'wide';
  // The same size can come back from the fix-ups above; settle it by walking the last panel.
  for (let guard = 0; guard < 4 && new Set(names.map(k => SHOT_BANK[k].size)).size < names.length; guard++) {
    const used = new Set(names.slice(0, -1).map(k => SHOT_BANK[k].size));
    names[n - 1] = Object.keys(SHOT_BANK).find(k => !used.has(SHOT_BANK[k].size) && (SHOT_BANK[k].size >= 2));
  }
  // Panel 0 is square on from the player's side (the eye came from there); the
  // others may cross the line for contrast.
  return names.map((name, i) => {
    const b = SHOT_BANK[name];
    const r1 = random(), r2 = random(), seed = random();
    let az = b.az[0] + (b.az[1] - b.az[0]) * r1;
    if (i > 0 && name !== 'medium' && random() < .35) az = Math.PI - az;
    return { name, size: b.size, az, el: b.el[0] + (b.el[1] - b.el[0]) * r2, seed };
  });
}

/** For tests and the harness: a small seeded random. */
export function seeded(seed = 1) {
  let s = seed >>> 0 || 1;
  return () => { s ^= s << 13; s >>>= 0; s ^= s >> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
}
