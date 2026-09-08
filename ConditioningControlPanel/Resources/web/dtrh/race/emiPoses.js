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
 *   snapshotRest(model)          bank the authored stance before a mixer moves it
 *
 * `model` is the glb root (EMI_root clone). Every preset value is an OFFSET on
 * the pivot's authored rest rotation, so the model's own stance is never lost.
 * `opts`: { side: -1 | 1, tier: 1..3, hold: seconds, amp: 0..1, arms: 0..1 }. A sided preset is authored
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

/* ---------------------------------------------------------------------------
 * THE RIM GRIP (measured, not guessed - keep these numbers with the table)
 *
 * Everything below is in kart-body metres, the space the cup and the seat share:
 *   the cup's lip      top y 0.785, lip radius 0.500 (rMean 0.503; the handle is the outlier)
 *   the seat           (0, 0.395, 0.22), scale 0.8, so a model metre is 0.8 body metres
 *   a shoulder pivot   (-+0.312, 0.787, 0.196) with the root at rest - level with the lip
 *   the glove centre   [0.025, -0.2563, 0.0078] in shoulder-local (|u| 0.257 model = 0.206 body)
 *
 * That reach is short. Unstretched it meets the lip circle only between about 32 and 77 degrees
 * around from dead ahead, and 77 degrees still parks the hand at z +0.12: the FAR arc of the brim,
 * where the chase camera reads it as a knob on the rim rather than a hand holding it. So a holding
 * pose stretches the arm along its own axis (`reach`, 1.28 here, applied as a y scale on the
 * shoulder with the hand counter-scaled so the glove stays a ball) and parks the glove at 82..87
 * degrees, x -+0.51, z +0.02..0.07 - the WIDEST point of the brim, clear of her own case, side on
 * to the camera. The first pass at this got the hands out of the cup (they used to sit 0.109 m
 * under the lip, invisible) but left them on the far arc; this is the half that made them read.
 *
 * Every holding angle below is solved with that pose's OWN root attitude applied (lean, tilt, lift
 * and the squash, which widens x and z by 0.6 of what it takes off y), because a 0.2 rad drift lean
 * walks a glove 0.1 m off a rim that is only 0.5 m across. That is why the drift's two arms are so
 * far apart, and why an unsided pose with a lean (landingKerb) is not symmetric either.
 * ------------------------------------------------------------------------ */

/** A holding arm's stretch along its own axis, and the pair the table hands to `reach`. */
const GRIP = 1.28;
const GRIP2 = [GRIP, GRIP];
/** The two pivots `reach` may stretch, and the nodes counter-scaled so the glove stays round.
 *  emi.js's gloveHands() repaints hand + thumb before the merge, which is what makes handL / handR
 *  the host of that merged glove mesh; a pack without them simply stretches nothing back. */
const ARMS = ['shoulderL', 'shoulderR'];
const HANDS = { shoulderL: ['handL', 'thumbL'], shoulderR: ['handR', 'thumbR'] };

/**
 * The presets. `root` is { lean (z), tilt (x), lift (y), squash (y scale) }, the four pivots are
 * [x, y, z] offsets in radians, `w` / `zeta` are the spring, `hold` is seconds before `next`
 * (0 = hold until something else is set), `breath` keeps the idle bob, `fraught` feeds emi.js and
 * `antDown` lets the antenna base fall toward the real floor while the world is upside down.
 *
 * The `dy` in each comment is the glove centre's height against the lip: + is over the brim,
 * - is pressing down into it. A hand that leaves the rim on purpose (the drift point, the grab,
 * the throw, the cheer) says so, and drops its `reach` back to 1 while it is off.
 *
 * `reach` is [L, R]: how far each arm stretches along its own axis, 1 being the arm as authored.
 * Only a hand that is HOLDING the brim stretches - see THE RIM GRIP above for why it has to.
 */
export const POSES = {
  // both gloves on the brim, shoulders soft, the breath running                   dy +0.02
  cruise: { w: 7, zeta: 0.7, hold: 0, breath: 1, reach: GRIP2, root: { lean: 0, tilt: 0, lift: 0, squash: 1 },
    shoulderL: [-1.498, 0, -2.089], shoulderR: [-1.498, 0, 2.089], footL: [0, 0, 0], footR: [0, 0, 0] },

  // lean into the turn; the outside hand (shoulderL at side +1) rides up over the brim  dy +0.06 / 0.00
  drift: { sided: 1, w: 9, zeta: 0.55, hold: 1.1, next: 'cruise', reach: GRIP2, root: { lean: -0.20, tilt: 0.04, lift: 0, squash: 1 },
    shoulderL: [-2.096, 0, -1.834], shoulderR: [-1.206, 0, 2.458], footL: [-0.12, 0, -0.10], footR: [0.10, 0, 0.06] },

  // the mini turbo: squash on the kick, hands push down on the brim              dy -0.03
  boost: { w: 18, zeta: 0.6, hold: 0.11, next: 'boostOut', reach: GRIP2, root: { lean: 0, tilt: 0.14, lift: -0.06, squash: 0.86 },
    shoulderL: [-1.340, 0, -2.313], shoulderR: [-1.340, 0, 2.313], footL: [-0.20, 0, 0], footR: [-0.20, 0, 0] },
  // ...then stretch, elbows straightening, hands still on it                     dy +0.06
  boostOut: { w: 10, zeta: 0.38, hold: 0.55, next: 'cruise', reach: GRIP2, root: { lean: 0, tilt: -0.07, lift: 0.05, squash: 1.10 },
    shoulderL: [-1.724, 0, -1.925], shoulderR: [-1.724, 0, 1.925], footL: [0.16, 0, 0], footR: [0.16, 0, 0] },

  // off the ramp: arms ride high on the brim, one leg kicks, a little nose up    dy +0.09
  air: { w: 8, zeta: 0.5, hold: 3, next: 'cruise', reach: GRIP2, root: { lean: 0, tilt: -0.16, lift: 0.04, squash: 1.03 },
    shoulderL: [-0.955, 0, -1.773], shoulderR: [-0.955, 0, 1.773], footL: [-0.70, 0, -0.15], footR: [0.28, 0, 0.10] },

  // touchdown: squash hard with the overshoot, hands slap the brim               dy -0.02
  landing: { w: 16, zeta: 0.35, hold: 0.28, next: 'cruise', reach: GRIP2, root: { lean: 0, tilt: 0.06, lift: -0.05, squash: 0.82 },
    shoulderL: [-1.132, 0, -2.303], shoulderR: [-1.132, 0, 2.303], footL: [0.22, 0, 0], footR: [0.22, 0, 0] },
  // the same, kerbed: one hand jolts down, the other up, and a beat of fraught    dy -0.06 / +0.04
  landingKerb: { w: 14, zeta: 0.30, hold: 0.5, next: 'cruise', fraught: 0.7, reach: GRIP2, root: { lean: 0.16, tilt: 0.12, lift: -0.07, squash: 0.78 },
    shoulderL: [-0.847, 0, -2.686], shoulderR: [-1.059, 0, 2.170], footL: [0.34, 0, 0.12], footR: [0.16, 0, -0.10] },

  // a treat went by: the far hand keeps the brim, the near one reaches out (side +1 = her +x)
  grab: { sided: 1, w: 12, zeta: 0.5, hold: 0.5, next: 'cruise', reach: [GRIP, 1], root: { lean: -0.10, tilt: -0.05, lift: 0.02, squash: 1 },
    shoulderL: [-1.908, 0, -1.962], shoulderR: [-1.75, 0, 0.55], footL: [0, 0, 0], footR: [-0.10, 0, 0] },

  // an effect is pouring: hands clamp the brim and she gets small                dy -0.01
  clamp: { w: 11, zeta: 0.7, hold: 1.2, next: 'cruise', fraught: 1, reach: GRIP2, root: { lean: 0, tilt: 0.10, lift: -0.06, squash: 0.90 },
    shoulderL: [-1.291, 0, -2.302], shoulderR: [-1.291, 0, 2.302], footL: [0.35, 0, 0.08], footR: [0.35, 0, -0.08] },

  // upside down in the Wheel: knees up, white knuckles on the brim, the antenna hangs   dy -0.01
  tuck: { w: 10, zeta: 0.6, hold: 0, antDown: 1, reach: GRIP2, root: { lean: 0, tilt: 0.18, lift: -0.08, squash: 0.94 },
    shoulderL: [-1.369, 0, -2.320], shoulderR: [-1.369, 0, 2.320], footL: [-0.85, 0, 0.10], footR: [-0.85, 0, -0.10] },

  // the item goes over the side: one hand holds, the other arcs over the brim (the low zeta IS the arc)
  throw: { sided: 1, w: 14, zeta: 0.35, hold: 0.45, next: 'cruise', reach: [GRIP, 1], root: { lean: -0.07, tilt: 0.05, lift: 0.02, squash: 1 },
    shoulderL: [-1.811, 0, -2.058], shoulderR: [-0.181, 0, 2.032], footL: [0, 0, 0], footR: [0.12, 0, 0] },

  // a personal best or a jackpot: both arms up
  cheer: { w: 9, zeta: 0.45, hold: 1.3, next: 'cruise', root: { lean: 0, tilt: -0.08, lift: 0.06, squash: 1.04 },
    shoulderL: [-2.60, 0, -0.35], shoulderR: [-2.60, 0, 0.35], footL: [-0.18, 0, 0], footR: [-0.18, 0, 0] },

  // ---- the countdown, 3 2 1 GO (intro.js and run.js's `again` both drive these off the HUD ticks) ----
  // up on the number: a bob off the brim, the weight rolling to one foot. Sided so the tap alternates
  // beat to beat, `hold` short so it drops straight back into `grip` between the numbers.  dy +0.056
  ready: { sided: 1, w: 12, zeta: 0.45, hold: 0.34, next: 'grip', breath: 1, reach: GRIP2,
    root: { lean: -0.06, tilt: -0.05, lift: 0.05, squash: 1.03 },
    shoulderL: [-1.763, 0, -1.958], shoulderR: [-1.348, 0, 2.085], footL: [-0.26, 0, -0.06], footR: [0.06, 0, 0.04] },
  // ...and down between them, hands re-gripping the brim. The long hold is the safety net: if a count
  // is skipped or aborted she walks herself back to cruise instead of standing there mid-bob.  dy -0.011
  grip: { w: 10, zeta: 0.6, hold: 1.2, next: 'cruise', breath: 1, reach: GRIP2,
    root: { lean: 0, tilt: 0.05, lift: -0.02, squash: 0.97 },
    shoulderL: [-1.571, 0, -2.177], shoulderR: [-1.571, 0, 2.177], footL: [0.10, 0, 0], footR: [0.10, 0, 0] },
  // GO: crouch onto the brim and shove, which is exactly where cameraWhip picks the run up.  dy -0.049
  launch: { w: 16, zeta: 0.35, hold: 0.45, next: 'cruise', reach: GRIP2,
    root: { lean: 0, tilt: 0.16, lift: -0.07, squash: 0.84 },
    shoulderL: [-1.425, 0, -2.362], shoulderR: [-1.425, 0, 2.362], footL: [0.30, 0, 0], footR: [0.30, 0, 0] },
};

const ZERO3 = [0, 0, 0];
const REACH1 = [1, 1];
const DEF_ROOT = { lean: 0, tilt: 0, lift: 0, squash: 1 };
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
/** A stretch is a scale: never zero, never a limb twice its length. */
const reachOf = (v) => clamp(+v > 0 ? +v : 1, 0.5, 2);
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
  const rc = p.reach || REACH1;                                   // [L, R], mirrored with the rest
  t.reach = side > 0 ? [reachOf(rc[0]), reachOf(rc[1])] : [reachOf(rc[1]), reachOf(rc[0])];
  if (side < 0) t.root.lean = -t.root.lean;
  if (opts.tier) t.root.lean *= 1 + 0.12 * clamp(+opts.tier || 0, 0, 3);   // a fatter drift leans harder
  // `amp` scales the whole-body part of a pose and nothing else, so reduced motion keeps the gesture
  // (the arms still move, she is still doing a thing) and only loses the bounce (Law VI).
  if (opts.amp != null) {
    const a = clamp(+opts.amp || 0, 0, 1);
    t.root.lean *= a; t.root.tilt *= a; t.root.lift *= a;
    t.root.squash = 1 + (t.root.squash - 1) * a;
  }
  // `arms` is the other half of that dial: how far the limbs commit, 1 being the authored pose. The
  // rim grips are solved for the RUN's cup, and the menu stage sits her higher over a bigger one, so
  // the intro asks for a fraction of the swing and keeps her hands inside the bore.
  if (opts.arms != null) {
    const a = clamp(+opts.arms || 0, 0, 1);
    for (const k of PIVOTS) t[k] = [t[k][0] * a, t[k][1] * a, t[k][2] * a];
    t.reach = [1 + (t.reach[0] - 1) * a, 1 + (t.reach[1] - 1) * a];   // a fraction of the swing, a fraction of the stretch
  }
  return t;
}

/**
 * Remember a model's authored stance while it is still authored. A mixer-driven model (the menu
 * stage runs clips that key shoulderL/R) has moved by the time anything asks for a pose layer, so
 * menu.js calls this the moment the glb lands and createPoseLayer prefers what it stored.
 */
export function snapshotRest(model) {
  if (!model || !model.getObjectByName) return null;
  const rest = {};
  for (const k of PIVOTS) {
    const o = model.getObjectByName(k);
    if (o) rest[k] = [o.rotation.x, o.rotation.y, o.rotation.z];
  }
  rest.root = [model.rotation.x, model.rotation.z, model.position.y];
  model.userData = model.userData || {};
  model.userData.poseRest = rest;
  return rest;
}

/**
 * The layer. `model` is the glb root; a model without the contract pivots simply drives the ones
 * it has and skips the rest, so a half-finished pack degrades a limb at a time.
 */
export function createPoseLayer(model) {
  const find = (n) => (model && model.getObjectByName ? model.getObjectByName(n) || null : null);
  const kept = (model && model.userData && model.userData.poseRest) || null;   // snapshotRest(), if anyone took one
  const piv = {}, rest = {}, sp = {};
  for (const k of PIVOTS) {
    const o = find(k);
    piv[k] = o;
    rest[k] = (kept && kept[k]) || (o ? [o.rotation.x, o.rotation.y, o.rotation.z] : ZERO3);
    sp[k] = [new Spring(), new Spring(), new Spring()];
  }
  // the arm stretch: one spring an arm, and the hand nodes that undo it on the glove. Each hand
  // keeps the scale it was mounted at (emi.js sizes the mitts up), so the counter-scale divides
  // that base instead of replacing it.
  const sReach = { shoulderL: new Spring(1), shoulderR: new Spring(1) };
  const hands = {};
  for (const k of ARMS) hands[k] = HANDS[k].map(find).filter(Boolean).map((o) => ({ o, base: [o.scale.x, o.scale.y, o.scale.z] }));
  const ant0 = find('ant0');
  const rootRest = (kept && kept.root) || (model ? [model.rotation.x, model.rotation.z, model.position.y] : [0, 0, 0]);
  const sLean = new Spring(), sTilt = new Spring(), sLift = new Spring(), sSquash = new Spring(1);
  const sAntX = new Spring(), sAntZ = new Spring();
  let name = 'cruise', target = resolvePose('cruise'), hold = 0, opt = {};
  const api = { fraught: 0, get name() { return name; } };

  /** Set a pose. Unknown names are ignored (false) rather than blanking her stance. */
  function set(n, opts = {}) {
    if (!POSES[n]) return false;
    name = n;
    // the shape of the caller, minus the timing: a pose that chains into `next` inherits it, so a
    // count driven at `arms: 0.3` does not snap to a full swing the moment the beat times out
    opt = { side: opts.side, tier: opts.tier, amp: opts.amp, arms: opts.arms };
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
      if (hold <= 0) set(POSES[name].next || 'cruise', opt);
    }
    const w = target.w, z = target.zeta;
    for (const k of PIVOTS) {
      const o = piv[k];
      if (!o) continue;
      const tt = target[k], r = rest[k], s = sp[k];
      o.rotation.set(r[0] + s[0].step(tt[0], dt, w, z), r[1] + s[1].step(tt[1], dt, w, z), r[2] + s[2].step(tt[2], dt, w, z));
    }
    // the stretch rides the same spring shape: the arm grows along its own axis toward the brim and
    // the hand under it is divided back down, so the glove stays a ball instead of an egg
    for (let i = 0; i < ARMS.length; i++) {
      const o = piv[ARMS[i]];
      if (!o) continue;
      const k = clamp(sReach[ARMS[i]].step(target.reach[i], dt, w, z), 0.5, 2);
      o.scale.set(1, k, 1);
      for (const h of hands[ARMS[i]]) h.o.scale.set(h.base[0], h.base[1] / k, h.base[2]);
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
    for (const k of ARMS) { const o = piv[k]; if (o) o.scale.set(1, 1, 1); for (const h of hands[k]) h.o.scale.set(h.base[0], h.base[1], h.base[2]); }
    if (model) { model.rotation.x = rootRest[0]; model.rotation.z = rootRest[1]; model.position.y = rootRest[2]; model.scale.set(1, 1, 1); }
  }

  api.set = set; api.update = update; api.dispose = dispose;
  return api;
}

// self-check: node --check is the bar; race/smoke/poses-smoke.mjs runs the table and the springs.
