/* ============================================================================
 * race/emiPoses.js - Racing Thoughts: EMI's pose layer over the Blender glb.
 *
 * race/emi.js owns the moods (the antenna, the bead, the sweat). This owns her
 * BODY on the race's events: the arms, the feet and a root lean / tilt / lift /
 * squash, blended on damped springs (House Book Law XI: overshoot, never a
 * linear tween) and always falling back to `cruise` when a pose's hold runs out.
 *
 *   createPoseLayer(model, { }) -> { set(name, opts), update(dt, ctx), dispose, fraught, name }
 *   POSES                        the pure preset table (a node smoke reads it)
 *   PIVOTS                       the four glb pivots a preset is allowed to name
 *
 * `model` is the glb root (EMI_root clone). Every preset value is an OFFSET on
 * the pivot's authored rest rotation, so the model's own stance is never lost.
 * `opts`: { side: -1 | 1, tier: 1..3, hold: seconds }. A sided preset is authored
 * for side +1 (the kart's right, +x) and mirrored for -1: L and R swap and the y
 * and z of every rotation flip, along with the root lean.
 *
 * The one thing this layer reads back out is `fraught` (clamp and a kerbed
 * landing raise it); emi.js takes the max of that and the run brain's own value.
 * ==========================================================================*/

/** The pivots a preset may name. Anything else is a typo and the smoke fails on it. */
export const PIVOTS = ['shoulderL', 'shoulderR', 'footL', 'footR'];

const BREATH_SEC = 3.9;      // the same one breath emi.js runs on (Law III)
const BREATH_LIFT = 0.02;    // model metres, so 0.013 m once the seat's 0.64 is on it
const ANT_MAX = 1.2;         // how far the tuck is allowed to drag the antenna base

/**
 * The presets. `root` is { lean (z), tilt (x), lift (y), squash (y scale) }, the four pivots are
 * [x, y, z] offsets in radians, `w` / `zeta` are the spring, `hold` is seconds before `next`
 * (0 = hold until something else is set), `breath` keeps the idle bob, `fraught` feeds emi.js and
 * `antDown` lets the antenna base fall toward the real floor while the world is upside down.
 */
export const POSES = {
  // hands on the far rim, shoulders soft, the breath running
  cruise: { w: 7, zeta: 0.7, hold: 0, breath: 1, root: { lean: 0, tilt: 0, lift: 0, squash: 1 },
    shoulderL: [-0.95, 0, -0.10], shoulderR: [-0.95, 0, 0.10], footL: [0, 0, 0], footR: [0, 0, 0] },

  // lean into the turn; the outside hand (shoulderL at side +1) lifts off the rim and points
  drift: { sided: 1, w: 9, zeta: 0.55, hold: 1.1, next: 'cruise', root: { lean: -0.20, tilt: 0.04, lift: 0, squash: 1 },
    shoulderL: [0.45, 0, -0.75], shoulderR: [-1.15, 0, 0.06], footL: [-0.12, 0, -0.10], footR: [0.10, 0, 0.06] },

  // the mini turbo: squash on the kick...
  boost: { w: 18, zeta: 0.6, hold: 0.11, next: 'boostOut', root: { lean: 0, tilt: 0.14, lift: -0.06, squash: 0.86 },
    shoulderL: [0.55, 0, 0.18], shoulderR: [0.55, 0, -0.18], footL: [-0.20, 0, 0], footR: [-0.20, 0, 0] },
  // ...then stretch, arms pinned back down the road
  boostOut: { w: 10, zeta: 0.38, hold: 0.55, next: 'cruise', root: { lean: 0, tilt: -0.07, lift: 0.05, squash: 1.10 },
    shoulderL: [0.95, 0, 0.26], shoulderR: [0.95, 0, -0.26], footL: [0.16, 0, 0], footR: [0.16, 0, 0] },

  // off the ramp: arms out, one leg kicks, a little nose up
  air: { w: 8, zeta: 0.5, hold: 3, next: 'cruise', root: { lean: 0, tilt: -0.16, lift: 0.04, squash: 1.03 },
    shoulderL: [-0.20, 0, -1.05], shoulderR: [-0.20, 0, 1.05], footL: [-0.70, 0, -0.15], footR: [0.28, 0, 0.10] },

  // touchdown: squash hard with the overshoot, hands slap the rim
  landing: { w: 16, zeta: 0.35, hold: 0.28, next: 'cruise', root: { lean: 0, tilt: 0.06, lift: -0.05, squash: 0.82 },
    shoulderL: [-1.35, 0, 0.06], shoulderR: [-1.35, 0, -0.06], footL: [0.22, 0, 0], footR: [0.22, 0, 0] },
  // the same, kerbed: a wobble on the way down and a beat of fraught
  landingKerb: { w: 14, zeta: 0.30, hold: 0.5, next: 'cruise', fraught: 0.7, root: { lean: 0.16, tilt: 0.12, lift: -0.07, squash: 0.78 },
    shoulderL: [-1.45, 0, 0.22], shoulderR: [-1.10, 0, -0.30], footL: [0.34, 0, 0.12], footR: [0.16, 0, -0.10] },

  // a treat went by: the near hand reaches for it (side +1 = the pop is to her +x)
  grab: { sided: 1, w: 12, zeta: 0.5, hold: 0.5, next: 'cruise', root: { lean: -0.10, tilt: -0.05, lift: 0.02, squash: 1 },
    shoulderL: [-0.85, 0, -0.10], shoulderR: [-1.75, 0, 0.55], footL: [0, 0, 0], footR: [-0.10, 0, 0] },

  // an effect is pouring: hands clamp the rim and she gets small
  clamp: { w: 11, zeta: 0.7, hold: 1.2, next: 'cruise', fraught: 1, root: { lean: 0, tilt: 0.10, lift: -0.06, squash: 0.90 },
    shoulderL: [-1.30, 0, 0.14], shoulderR: [-1.30, 0, -0.14], footL: [0.35, 0, 0.08], footR: [0.35, 0, -0.08] },

  // upside down in the Wheel: knees up, white knuckles, the antenna hangs toward the real floor
  tuck: { w: 10, zeta: 0.6, hold: 0, antDown: 1, root: { lean: 0, tilt: 0.18, lift: -0.08, squash: 0.94 },
    shoulderL: [-1.50, 0, 0.20], shoulderR: [-1.50, 0, -0.20], footL: [-0.85, 0, 0.10], footR: [-0.85, 0, -0.10] },

  // the item goes over the side: the free hand throws an arc (the low zeta IS the arc)
  throw: { sided: 1, w: 14, zeta: 0.35, hold: 0.45, next: 'cruise', root: { lean: -0.07, tilt: 0.05, lift: 0.02, squash: 1 },
    shoulderL: [-0.95, 0, -0.10], shoulderR: [1.00, 0, 0.35], footL: [0, 0, 0], footR: [0.12, 0, 0] },

  // a personal best or a jackpot: both arms up
  cheer: { w: 9, zeta: 0.45, hold: 1.3, next: 'cruise', root: { lean: 0, tilt: -0.08, lift: 0.06, squash: 1.04 },
    shoulderL: [-2.60, 0, -0.35], shoulderR: [-2.60, 0, 0.35], footL: [-0.18, 0, 0], footR: [-0.18, 0, 0] },
};

const ZERO3 = [0, 0, 0];
const DEF_ROOT = { lean: 0, tilt: 0, lift: 0, squash: 1 };
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const flip = (k) => (k.endsWith('L') ? k.slice(0, -1) + 'R' : k.slice(0, -1) + 'L');

/** Damped spring toward a target; zeta < 1 overshoots a little, which is the point (Law XI). */
class Spring {
  constructor(x = 0) { this.x = x; this.v = 0; }
  step(target, dt, w, zeta) {
    this.v += (w * w * (target - this.x) - 2 * zeta * w * this.v) * dt;
    this.x += this.v * dt;
    return this.x;
  }
}

/** One preset plus its options, flattened into the numbers update() blends toward. */
export function resolvePose(name, opts = {}) {
  const p = POSES[name] || POSES.cruise;
  const side = p.sided && +opts.side < 0 ? -1 : 1;
  const t = {
    w: p.w, zeta: p.zeta, breath: !!p.breath, fraught: p.fraught || 0, antDown: p.antDown || 0,
    root: { ...DEF_ROOT, ...(p.root || {}) },
  };
  for (const k of PIVOTS) {
    const src = p[side > 0 ? k : flip(k)] || ZERO3;
    t[k] = side > 0 ? [src[0], src[1], src[2]] : [src[0], -src[1], -src[2]];
  }
  if (side < 0) t.root.lean = -t.root.lean;
  if (opts.tier) t.root.lean *= 1 + 0.12 * clamp(+opts.tier || 0, 0, 3);   // a fatter drift leans harder
  return t;
}

/**
 * The layer. `model` is the glb root; a model without the contract pivots simply drives the ones
 * it has and skips the rest, so a half-finished pack degrades a limb at a time.
 */
export function createPoseLayer(model) {
  const find = (n) => (model && model.getObjectByName ? model.getObjectByName(n) || null : null);
  const piv = {}, rest = {}, sp = {};
  for (const k of PIVOTS) {
    const o = find(k);
    piv[k] = o;
    rest[k] = o ? [o.rotation.x, o.rotation.y, o.rotation.z] : ZERO3;
    sp[k] = [new Spring(), new Spring(), new Spring()];
  }
  const ant0 = find('ant0');
  const rootRest = model ? [model.rotation.x, model.rotation.z, model.position.y] : [0, 0, 0];
  const sLean = new Spring(), sTilt = new Spring(), sLift = new Spring(), sSquash = new Spring(1);
  const sAntX = new Spring(), sAntZ = new Spring();
  let name = 'cruise', target = resolvePose('cruise'), hold = 0;
  const api = { fraught: 0, get name() { return name; } };

  /** Set a pose. Unknown names are ignored (false) rather than blanking her stance. */
  function set(n, opts = {}) {
    if (!POSES[n]) return false;
    name = n;
    target = resolvePose(n, opts);
    hold = opts.hold != null ? Math.max(0, +opts.hold || 0) : (POSES[n].hold || 0);
    api.fraught = target.fraught;
    return true;
  }

  function update(dt, ctx) {
    if (!model) return;
    dt = Math.min(Math.max(+dt || 0, 0), 0.05);
    if (hold > 0) {
      hold -= dt;
      if (hold <= 0) set(POSES[name].next || 'cruise');
    }
    const w = target.w, z = target.zeta;
    for (const k of PIVOTS) {
      const o = piv[k];
      if (!o) continue;
      const tt = target[k], r = rest[k], s = sp[k];
      o.rotation.set(r[0] + s[0].step(tt[0], dt, w, z), r[1] + s[1].step(tt[1], dt, w, z), r[2] + s[2].step(tt[2], dt, w, z));
    }
    const t = (ctx && ctx.t) || 0;
    const breath = target.breath ? BREATH_LIFT * Math.sin(t * (Math.PI * 2) / BREATH_SEC) : 0;
    model.rotation.x = rootRest[0] + sTilt.step(target.root.tilt, dt, w, z);
    model.rotation.z = rootRest[1] + sLean.step(target.root.lean, dt, w, z);
    model.position.y = rootRest[2] + sLift.step(target.root.lift, dt, w, z) + breath;
    const sq = Math.max(0.4, sSquash.step(target.root.squash, dt, w, z));
    const wide = 1 + (1 - sq) * 0.6;
    model.scale.set(wide, sq, wide);
    // the tuck: while the road's up has rolled under the world's, the antenna base falls toward the
    // real floor. World down in kart space is just the frame vectors' y, negated, and how inverted
    // she is scales the whole thing, so upright it is exactly zero.
    let ax = 0, az = 0;
    if (target.antDown && ctx && ctx.up) {
      const inv = Math.max(0, -ctx.up.y);
      const dx = -(ctx.right ? ctx.right.y : 0), dy = -ctx.up.y, dz = -(ctx.tangent ? ctx.tangent.y : 0);
      ax = clamp(Math.atan2(dz, dy) * inv, -ANT_MAX, ANT_MAX);
      az = clamp(-Math.atan2(dx, dy) * inv, -ANT_MAX, ANT_MAX);
    }
    const antX = sAntX.step(ax, dt, w, z), antZ = sAntZ.step(az, dt, w, z);
    if (ant0) { ant0.rotation.x += antX; ant0.rotation.z += antZ; }   // emi.js wrote the mood first
    api.fraught = target.fraught;
  }

  /** Put the stance back the way the pack authored it (the rig frees the model right after). */
  function dispose() {
    for (const k of PIVOTS) { const o = piv[k]; if (o) o.rotation.set(rest[k][0], rest[k][1], rest[k][2]); }
    if (model) { model.rotation.x = rootRest[0]; model.rotation.z = rootRest[1]; model.position.y = rootRest[2]; model.scale.set(1, 1, 1); }
  }

  api.set = set; api.update = update; api.dispose = dispose;
  return api;
}

// self-check: node --check is the bar; race/smoke/poses-smoke.mjs runs the table and the springs.
