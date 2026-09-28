// Signature captures. The game commits immediately; these cancellable acts own only presentation.
import * as THREE from 'three';
import { siliconePoint } from './silicone.js';
import { hopPlan, hopAt } from './hops.js';
import { createQueenDeck, queenBash, QUEEN_ACTS } from './queen.js';
import { createRepertoire, extraPose } from './repertoire.js';
import { impactDelay, impactTime, impactPulse } from './impact.js';

export const ACTS = Object.freeze({
  p: { name: 'lamp-stomp', hit: .72, end: 2.18 },
  n: { name: 'backflip', hit: .90, end: 2.36 },
  b: { name: 'double-whip', hit: 1.20, end: 3.20 },
  k: { name: 'royal-squash', hit: .90, end: 2.95 },
  q: QUEEN_ACTS[0],
  r: { name: 'side-swing', hit: 1.30, end: 3.30 },
});
const clamp = t => Math.max(0, Math.min(1, t));
const smooth = t => { t = clamp(t); return t * t * t * (t * (t * 6 - 15) + 10); };
const phase = (t, a, b) => smooth((t - a) / (b - a));
const arrivals = { p: .50, n: .50 };
const stepStarts = { b: .48, r: .48, q: .70, k: .70, kick: .46, sweep: .50 };
const up = new THREE.Vector3(0, 1, 0);
const yaw = p => p.userData.side === 'b' ? Math.PI : 0;
const skins = p => p.userData.materials || [p.userData.material];
const scratch = new THREE.Vector3(), rotation = new THREE.Quaternion();

// Keep the actual body and jewellery vertices, excluding shadow discs and outline shells.
// Bounds and dissolve positions use the same flexible spine as the vertex shader.
function shape(piece) {
  if (piece.userData.captureShape) return piece.userData.captureShape;
  const points = [];
  for (const m of piece.children) {
    if (!m.isMesh || m === piece.userData.contact || m.userData.pbpHull) continue;
    m.updateMatrix();
    const a = m.geometry.attributes.position;
    for (let i = 0; i < a.count; i++) points.push(new THREE.Vector3().fromBufferAttribute(a, i).applyMatrix4(m.matrix));
  }
  piece.userData.captureShape = points;
  const h = Math.max(...points.map(v => v.y));
  const bands = Array.from({ length: 24 }, () => new THREE.Box3());
  for (const v of points) bands[Math.min(23, Math.max(0, Math.floor(v.y / h * 24)))].expandByPoint(v);
  piece.userData.captureBands = bands.filter(b => !b.isEmpty());
  return points;
}
export function worldVertex(piece, v) {
  const u = piece.userData.jiggleUniforms, act = piece.userData.capturePose;
  if (act) return siliconePoint(v, u?.uHeight.value || 1, act, scratch).multiply(piece.scale).applyQuaternion(piece.quaternion).add(piece.position);
  const h = clamp(v.y / (u?.uHeight.value || 1)), squash = act ? 0 : (u?.uSquash.value || 0);
  const wave = act ? 1 : 1 + Math.sin(h * 5 - (u?.uTime.value || 0) * 9 + (u?.uPhase.value || 0)) * .22;
  const bx = act?.x ?? u?.uBend.value.x ?? 0, bz = act?.z ?? u?.uBend.value.y ?? 0;
  scratch.set(v.x * (1 + .5 * squash) + bx * h * h * wave,
    v.y * (1 - squash * h), v.z * (1 + .5 * squash) + bz * h * h * wave);
  return scratch.multiply(piece.scale).applyQuaternion(piece.quaternion).add(piece.position);
}
export function captureVolumes(piece) {
  shape(piece);
  return piece.userData.captureBands.map(b => {
    const result = new THREE.Box3();
    for (const x of [b.min.x, b.max.x]) for (const y of [b.min.y, (b.min.y + b.max.y) / 2, b.max.y]) for (const z of [b.min.z, b.max.z]) {
      result.expandByPoint(worldVertex(piece, { x, y, z }));
    }
    return result;
  });
}
export function captureBounds(piece, box = new THREE.Box3()) {
  box.makeEmpty();
  for (const v of shape(piece)) box.expandByPoint(worldVertex(piece, v));
  return box;
}

export function createChoreography({ group, emit, landed, reduced }) {
  const acts = [], nextQueen = createQueenDeck(), nextAct = createRepertoire(ACTS);
  const box = new THREE.Box3();
  const restore = (piece, scale) => {
    piece.scale.copy(scale); piece.rotation.set(0, yaw(piece), 0);
    delete piece.userData.capturePose; piece.userData.busy = false;
    if (piece.userData.jiggleUniforms) piece.userData.jiggleUniforms.uDissolve.value = 0;
    for (const m of skins(piece)) { m.opacity = 1; m.transparent = false; m.depthWrite = true; }
  };
  function exit(a, silent = false) {
    if (a.gone) return;
    a.gone = true;
    group.remove(a.victim);
    restore(a.victim, a.vScale); a.victim.position.copy(a.target);
    if (!silent) emit('sunk', { piece: a.victim.userData.type, side: a.victim.userData.side, object: a.victim });
  }
  function finish(a, silent = false, skipped = true) {
    restore(a.piece, a.scale); a.piece.position.copy(a.to);
    exit(a, silent);
    if (!silent && !a.landed) landed(a.piece, a.to, false, { capture: true, manner: 'signature', skipped });
    a.landed = true; a.piece.userData.tookOne = false;
  }
  function settle(piece) {
    for (let i = acts.length - 1; i >= 0; i--) if (acts[i].piece === piece || acts[i].victim === piece) {
      finish(acts[i]); acts.splice(i, 1);
    }
  }
  function start(piece, victim, from, to) {
    settle(piece);
    const type = piece.userData.type, spec = type === 'q' ? nextQueen() : nextAct(type);
    const motion = spec.motion || type;
    const d = to.clone().sub(from).setY(0).normalize();
    if (d.lengthSq() < .1) d.set(0, 0, piece.userData.side === 'w' ? -1 : 1);
    const a = { piece, victim, from: from.clone(), to: to.clone(), target: victim.position.clone(), d,
      type, motion, spec, flourish: Math.random() < .28, t: 0, scale: piece.scale.clone(), vScale: victim.scale.clone(), low: reduced(), marks: new Set() };
    piece.rotation.set(0, yaw(piece), 0); victim.rotation.set(0, yaw(victim), 0);
    piece.userData.capturePose = { x: 0, z: 0 }; victim.userData.capturePose = { x: 0, z: 0 };
    piece.userData.busy = victim.userData.busy = true;
    a.height = captureBounds(piece).getSize(new THREE.Vector3()).y;
    a.vHeight = captureBounds(victim).getSize(new THREE.Vector3()).y;
    a.radius = Math.max(box.copy(captureBounds(piece)).getSize(scratch).x, scratch.z) / 2;
    a.vRadius = Math.max(box.copy(captureBounds(victim)).getSize(scratch).x, scratch.z) / 2;
    a.near = a.target.clone().addScaledVector(d, -(a.radius + a.vRadius + .12));
    if (motion === 'k') a.near.copy(a.target).addScaledVector(d, -a.height * .94);
    a.sideways = new THREE.Vector3(-d.z, 0, d.x);
    a.contactHeight = Math.max(.18, Math.min(.55, a.vHeight * .36));
    if (motion === 'sweep') a.contactHeight = Math.min(.24, a.vHeight * .22);
    a.sideStrike = ['b', 'r', 'kick', 'sweep'].includes(motion);
    a.contactDirection = motion === 'b' ? a.sideways.clone().negate() : motion === 'kick' ? d.clone() : a.sideways.clone();
    if (motion === 'p' || motion === 'n') a.near.copy(a.target).addScaledVector(d, -Math.min(.65, from.distanceTo(to) * .5));
    a.impactDelay = a.low ? 0 : impactDelay(motion);
    a.approach = hopPlan(from, a.near);
    a.step = hopPlan(a.near, to);
    a.arrival = arrivals[motion] ?? stepStarts[motion] + a.step.duration - .10;
    a.fly = motion === 'r' ? a.sideways.clone() : d.clone();
    a.obstacles = group.children.filter(p => p !== piece && p !== victim && p.userData.type)
      .map(p => ({ piece: p, box: captureBounds(p).clone() }));
    a.ceiling = Math.max(1.35, ...a.obstacles.map(o => o.box.max.y)) + .12;
    // Compute the board exit from the victim's square, even when moving towards a far edge.
    a.exitDistance = Math.min(...['x', 'z'].filter(k => Math.abs(a.fly[k]) > .01)
      .map(k => ((a.fly[k] > 0 ? 5.6 : -5.6) - a.target[k]) / a.fly[k]));
    acts.push(a); piece.position.copy(from);
    return a;
  }
  function pose(piece, scale, position, tilt, spin, direction, bend = 0, sy = 1, shrink = 1, flex = null) {
    piece.scale.copy(scale).multiplyScalar(shrink); piece.scale.y *= sy;
    piece.scale.x /= Math.sqrt(sy); piece.scale.z /= Math.sqrt(sy);
    piece.position.copy(position);
    const axis = new THREE.Vector3().crossVectors(up, direction);
    piece.quaternion.setFromAxisAngle(up, yaw(piece) + spin);
    rotation.setFromAxisAngle(axis, tilt); piece.quaternion.premultiply(rotation);
    scratch.copy(direction).applyQuaternion(rotation.copy(piece.quaternion).invert()).multiplyScalar(bend);
    const act = { x: scratch.x, z: scratch.z, stretch: 0, drop: 0, lx: 0, lz: 0, twist: 0 };
    if (flex) {
      scratch.copy(flex.tip).applyQuaternion(rotation.copy(piece.quaternion).invert());
      act.x += scratch.x; act.z += scratch.z;
      scratch.copy(flex.lag).applyQuaternion(rotation);
      act.lx = scratch.x; act.lz = scratch.z;
      if (flex.dent) {
        scratch.copy(flex.dent).applyQuaternion(rotation);
        act.dx = scratch.x; act.dy = scratch.y; act.dz = scratch.z; act.dentAt = flex.dentAt;
      }
      act.stretch = flex.stretch; act.drop = flex.drop; act.twist = flex.twist; act.bulge = flex.bulge || 0;
    }
    // The short plug has a firm body; keep its deliberate capture squash in root scale.
    if (piece.userData.type === 'p') {
      for (const key of ['x', 'z', 'lx', 'lz', 'twist']) act[key] *= .45;
      act.stretch *= .55; act.bulge *= .55;
      for (const key of ['dx', 'dy', 'dz']) act[key] = (act[key] || 0) * .55;
    }
    piece.userData.capturePose = act;
    const b = captureBounds(piece, box);
    piece.position.y -= Math.min(0, b.min.y); // rotated bases, crowns and heads stay above the board
  }
  // Conservative visible-mesh envelopes. Lift over occupied neighbours, never shove them aside.
  function clearAbove(piece, obstacles) {
    const parts = captureVolumes(piece);
    for (let pass = 0; pass < 32; pass++) {
      let lift = 0;
      for (const b of parts) for (const o of obstacles) {
        if (b.max.x > o.min.x - .008 && b.min.x < o.max.x + .008 && b.max.z > o.min.z - .008 && b.min.z < o.max.z + .008
          && b.min.y < o.max.y + .008 && b.max.y > o.min.y) lift = Math.max(lift, o.max.y + .008 - b.min.y);
      }
      if (!lift) break;
      piece.position.y += lift;
      for (const b of parts) { b.min.y += lift; b.max.y += lift; }
    }
  }
  function clearSide(piece, blockers, direction) {
    const parts = captureVolumes(piece);
    for (let pass = 0; pass < 24; pass++) {
      let travel = 0;
      for (const b of parts) for (const o of blockers) {
        if (b.max.x <= o.min.x || b.min.x >= o.max.x || b.max.y <= o.min.y || b.min.y >= o.max.y || b.max.z <= o.min.z || b.min.z >= o.max.z) continue;
        const distances = ['x', 'z'].filter(k => Math.abs(direction[k]) > .01).map(k =>
          (direction[k] > 0 ? o.max[k] + .012 - b.min[k] : o.min[k] - .012 - b.max[k]) / direction[k]);
        travel = Math.max(travel, Math.min(...distances));
      }
      if (!travel) break;
      const shift = direction.clone().multiplyScalar(travel);
      piece.position.add(shift); for (const b of parts) b.translate(shift);
    }
  }
  function cue(a, mark, time, name) {
    if (a.motionTime < time || a.marks.has(mark)) return;
    a.marks.add(mark); emit('captureCue', { name, piece: a.type });
  }
  function update(dt) {
    for (let i = acts.length - 1; i >= 0; i--) {
      const a = acts[i]; a.t += dt;
      if (!a.piece.parent) { exit(a); acts.splice(i, 1); continue; }
      if (a.piece.userData.held || (!a.low && reduced())) { finish(a); acts.splice(i, 1); continue; }
      const { piece: p, victim: v, motion: type, spec, d } = a;
      const realTime = a.low ? a.t : Math.max(0, a.t - a.approach.duration);
      const t = a.motionTime = a.low ? realTime : impactTime(realTime, spec.hit, a.impactDelay);
      const approaching = !a.low && a.t < a.approach.duration;
      const hit = a.low ? .18 : spec.hit, age = Math.max(0, t - hit);
      const obstacles = a.obstacles.filter(o => o.piece.parent).flatMap(o => captureVolumes(o.piece));
      const lateral = a.sideways;
      const flex = { tip: new THREE.Vector3(), lag: new THREE.Vector3(), stretch: 0, drop: 0, twist: 0 };
      let at = (a.low ? a.from : a.near).clone(), tilt = 0, spin = 0, bend = 0, sy = 1, facing = d;
      if (a.low) {
        // Dissolve in place before entering the occupied square. No rolling or flying.
        at.lerp(a.to, phase(t, .30, .46));
      } else if (type === 'p' || type === 'n') {
        const heavy = spec.name === 'heavy-squash';
        const launch = heavy ? .32 : type === 'n' ? .30 : .16;
        const leap = clamp((t - launch) / (hit - launch));
        at.lerp(a.target, phase(t, launch + .08, hit - .10));
        at.y = Math.sin(Math.PI * leap) * (heavy ? .65 : type === 'n' ? 1.65 : 1.0) + phase(t, launch, launch + .20) * a.vHeight;
        const crouch = phase(t, .02, launch * .70) * (1 - phase(t, launch * .70, launch + .06));
        sy = 1 - (heavy ? .12 : .28) * crouch;
        flex.tip.addScaledVector(d, -.20 * crouch);
        at.addScaledVector(d, -.12 * crouch);
        if (type === 'n') tilt = -Math.PI * 2 * phase(t, launch + .12, hit - .10);
        if (t >= hit) {
          const flatten = phase(age, 0, .19);
          at.copy(a.target); at.y = a.vHeight * (1 - .88 * flatten) + .018;
          sy = 1;
          at.y *= 1 - phase(age, .19, arrivals[type]);
        }
      } else {
        if (type === 'b') {
          const ready = phase(t, .28, .52), release = 1 - phase(age, .14, .58);
          const sweep = -.82 + 1.64 * phase(t, .54, .82) - .36 * phase(t, .82, .96) - 1.28 * phase(t, 1.04, 1.30) + .72 * phase(age, .10, .28);
          const trail = -.82 + 1.64 * phase(t - .09, .54, .86) - 1.64 * phase(t - .09, 1.04, 1.36);
          const coil = phase(t, .30, .51) * (1 - phase(t, .55, .70));
          at.addScaledVector(d, -.10 * coil);
          flex.tip.copy(d).multiplyScalar((a.radius + a.vRadius + .12) * ready * release).addScaledVector(lateral, sweep * ready * release);
          flex.lag.copy(lateral).multiplyScalar((trail - sweep) * .72 * ready * release);
          flex.stretch = .22 * ready * release - .16 * coil;
          flex.tip.addScaledVector(d, -.28 * coil);
          flex.drop = (Math.min(a.height * .90, a.vHeight * .85) - a.height * 1.22) * ready * release;
          flex.twist = -.35 * sweep * ready * release;
        }
        if (type === 'k') {
          const charge = phase(t, .30, .66) * (1 - phase(t, .73, hit));
          const reach = phase(t, hit - .16, hit), press = phase(age, 0, .11);
          const recover = 1 - phase(age, .26, .65);
          flex.stretch = .22 * charge + .24 * reach * recover;
          flex.bulge = .32 * phase(t, hit - .22, hit) * (1 - phase(age, .15, .48));
          flex.tip.copy(d).multiplyScalar(a.height * (1.02 * reach * recover - .22 * charge));
          flex.lag.copy(d).multiplyScalar(a.height * (.15 * charge - .30 * reach * recover));
          flex.drop = (a.vHeight * (1 - .88 * press) + .025 - a.height * (1 + flex.stretch)) * reach * recover;
          at.addScaledVector(d, -.10 * charge);
          if (spec.name === 'triple-bash') {
            const bash = queenBash(t, a.height, a.vHeight);
            flex.stretch = bash.stretch; flex.bulge = .14 * bash.reach;
            flex.tip.copy(d).multiplyScalar(a.height * (1.02 * bash.reach - .18 * bash.charge));
            flex.lag.copy(d).multiplyScalar(a.height * (.10 * bash.charge - .20 * bash.reach));
            flex.drop = bash.drop;
            at.copy(a.near).addScaledVector(d, -.07 * bash.charge);
          }
        }
        if (type === 'r') {
          const ready = phase(t, .30, .76), release = 1 - phase(age, .07, .32);
          const tension = phase(t, .76, .84) * (1 - phase(t, hit - .12, hit - .08));
          const tremble = .025 * Math.sin((t - .76) * 76) * tension;
          const swing = -1.08 + 2.16 * phase(t, hit - .10, hit + .08) - 1.20 * phase(age, .045, .16) + .12 * phase(age, .16, .26) + tremble;
          const trail = -1.08 + 2.16 * phase(t - .025, hit - .10, hit + .08) - 1.20 * phase(age, .07, .185) + .12 * phase(age, .185, .285);
          const reach = (a.radius + a.vRadius + .12) * phase(t, hit - .12, hit) * release;
          flex.tip.copy(lateral).multiplyScalar(swing * ready * release).addScaledVector(d, reach);
          flex.lag.copy(lateral).multiplyScalar((trail - swing) * .24 * ready * release);
          flex.stretch = .025 * ready * release;
          flex.drop = (a.contactHeight - a.height * 1.025) * ready * release;
          flex.twist = -.25 * swing * ready * release;
          at.addScaledVector(lateral, -.065 * phase(age, 0, .065) * (1 - phase(age, .09, .26)));
        }
        if (type === 'kick' || type === 'sweep') {
          const extra = extraPose(a,t,age,at,flex); tilt = extra.tilt; spin = extra.spin;
        }
        if (age >= stepStarts[type]) {
          const step = hopAt(a.step, age - stepStarts[type]);
          at.lerp(a.to, step.travel); at.y = step.height;
          flex.stretch += .22 * step.ring;
          flex.tip.addScaledVector(d, .16 * step.ring);
          flex.lag.addScaledVector(d, -.24 * step.ring);
        }
        // Impact reverses the flexible body before its recovery begins.
        if (['b', 'r'].includes(type) && age > 0) {
          const kick = (1 - Math.exp(-age * 65)) * Math.exp(-age * (type === 'r' ? 18 : 6));
          const ring = Math.sin(age * 31) * Math.exp(-age * (type === 'r' ? 20 : 7));
          flex.tip.addScaledVector(lateral, (type === 'r' ? -.14 : .55) * kick);
          flex.lag.addScaledVector(lateral, (type === 'r' ? .10 : -.60) * kick);
          flex.stretch -= (type === 'r' ? .025 : .13) * ring;
          flex.twist += (type === 'r' ? .06 : .22) * ring;
        }
      }
      if (!a.gone) {
        let pos = a.target.clone(), tip = 0, shrink = 1, vs = 1;
        let reaction = type === 'r' ? lateral : d;
        const impactFlex = { tip: new THREE.Vector3(), lag: new THREE.Vector3(), stretch: 0, drop: 0, twist: 0 };
        if (!a.low && type === 'b') {
          const firstAge = Math.max(0, t - .70);
          const kick = (1 - Math.exp(-firstAge * 32)) * Math.exp(-firstAge * 11);
          reaction = lateral;
          impactFlex.tip.copy(lateral).multiplyScalar(.72 * kick);
          impactFlex.lag.copy(lateral).multiplyScalar(-.38 * kick);
          impactFlex.stretch = -.12 * kick;
        }
        if (!a.low && spec.name === 'triple-bash' && t < hit) {
          for (const tap of [.48, .83]) {
            const dt = Math.max(0, t - tap);
            const compress = phase(t, tap, tap + .025) * (1 - phase(t, tap + .04, tap + .20));
            vs -= .22 * compress;
            impactFlex.tip.addScaledVector(d, .10 * Math.sin(dt * 30) * Math.exp(-dt * 15));
          }
        }
        if (!a.low && age > 0) {
          if (type === 'p' || type === 'n' || type === 'k') vs = 1 - .88 * phase(age, 0, type === 'k' ? .11 : .19);
          else if (type === 'r') {
            const travel = 1 - Math.pow(1 - clamp(age / .75), 2);
            pos.addScaledVector(a.fly, a.exitDistance * travel);
            pos.y = .90 * Math.sin(Math.PI * travel);
            tip = 7 * phase(age, .10, .90);
          } else {
            // Velocity transfers at contact, followed by the slower fall at full size.
            reaction = a.contactDirection;
            const kick = (1 - Math.exp(-age * 40)) * Math.exp(-age * 9);
            const fall = 1 - Math.exp(-age * 10);
            tip = (type === 'kick' ? 2.65 : 1.50) * fall;
            pos.addScaledVector(reaction, (type === 'kick' ? 1.10 : .65) * (1 - Math.exp(-age * 6)));
            if (type === 'kick') pos.y = .18 * Math.sin(Math.PI * clamp(age / .55));
            impactFlex.tip.copy(reaction).multiplyScalar(.36 * kick);
            impactFlex.lag.copy(reaction).multiplyScalar(-.28 * kick);
            impactFlex.stretch = -.15 * kick;
          }
        }
        if (!a.low) {
          let contactAge = realTime - hit, strength = 1;
          let direction = a.sideStrike ? a.contactDirection : up.clone().negate();
          let centre = a.sideStrike && type !== 'b' ? a.contactHeight / a.vHeight : type === 'b' ? .72 : .88;
          if (t < hit && type === 'b') { contactAge = t - .70; direction = lateral; strength = .65; }
          if (t < hit && spec.name === 'triple-bash') { contactAge = t - (t >= .83 ? .83 : .48); strength = .55; }
          const pulse = impactPulse(contactAge, a.vHeight);
          impactFlex.dent = direction.clone().multiplyScalar(.11 * pulse.dent * strength);
          impactFlex.dentAt = centre + ((centre < .5 ? .85 : .30) - centre) * pulse.travel;
          // A tall neck trails the struck body; short victims mainly compress.
          if (a.sideStrike) {
            impactFlex.lag.addScaledVector(direction, -.16 * pulse.wave * strength);
            impactFlex.tip.addScaledVector(direction, .10 * pulse.wave * strength);
          } else impactFlex.bulge = .10 * pulse.dent * strength;
        }
        pose(v, a.vScale, pos, tip, 0, reaction, 0, vs, shrink, impactFlex);
        clearAbove(v, obstacles);
        const fade = a.low ? phase(age, 0, .12) : type === 'r' ? 0 : phase(age, type === 'k' ? .40 : a.sideStrike ? .32 : .20, type === 'k' ? .70 : a.sideStrike ? .65 : .48);
        if (fade > 0 && !a.dissolving) { a.dissolving = true; emit('dissolve', { object: v, low: a.low }); }
        if (v.userData.jiggleUniforms) v.userData.jiggleUniforms.uDissolve.value = a.low ? 0 : fade;
        for (const m of skins(v)) { m.opacity = 1 - fade; m.transparent = fade > 0; m.depthWrite = fade === 0; }
        if (fade >= 1 || (type === 'r' && age > .95 && !a.low)) exit(a);
      }
      if (!a.low) {
        const arrival = hit + a.arrival;
        const stomp = type === 'p' || type === 'n';
        // Stomp contact is the first landing, not the later move onto the empty square.
        const elapsed = Math.max(0, t - (stomp ? hit : arrival)), fade = 1 - phase(t, spec.end - .22, spec.end);
        const damping = a.type === 'p' || a.type === 'r' ? 10 : 4.5;
        const ring = -Math.sin(elapsed * 23) * Math.exp(-elapsed * damping) * fade;
        const delayed = -Math.sin(elapsed * 23 - .45) * Math.exp(-elapsed * damping) * phase(elapsed, 0, .04) * fade;
        flex.tip.addScaledVector(d, .22 * ring).addScaledVector(lateral, .12 * delayed);
        flex.lag.addScaledVector(d, -.32 * delayed);
        flex.stretch += (a.type === 'r' ? .065 : stomp ? .26 : .08) * ring;
        // Travel and impacts stretch the spine; its planted base does not rock.
        if (type === 'p' || type === 'n') {
          flex.stretch += sy - 1; sy = 1;
          flex.tip.addScaledVector(d, -.20 * Math.sin(Math.PI * phase(t, type === 'n' ? .30 : .16, hit)));
          flex.lag.addScaledVector(d, .18 * Math.sin(Math.PI * phase(t, .25, hit + .12)));
        }
      }
      if (!a.low && a.flourish && t > spec.end - .46) {
        const f = Math.sin(Math.PI * clamp((t - spec.end + .46) / .46));
        flex.stretch += .035 * f * f;
        flex.twist += (a.type === 'q' || a.type === 'k' ? .045 : .018) * Math.sin(f * Math.PI);
      }
      if (!a.low && !['p', 'n'].includes(type)) { flex.stretch += sy - 1; sy = 1; }
      if (approaching) {
        const hop = hopAt(a.approach, a.t);
        at.lerpVectors(a.from, a.near, hop.travel); at.y = hop.height;
        flex.stretch = .24 * hop.ring; flex.tip.copy(d).multiplyScalar(.18 * hop.ring);
        flex.lag.copy(d).multiplyScalar(-.28 * hop.ring); tilt = 0;
      }
      pose(p, a.scale, at, tilt, spin, facing, bend, sy, 1, flex);
      // A genuine contact surface: attacker and victim envelopes touch, never overlap.
      const wantedY = p.position.y;
      if ((type === 'p' || type === 'n') && t >= hit) a.lift = 0;
      p.position.y += (a.lift || 0) * Math.exp(-dt * 18);
      const sideStrike = a.sideStrike && !a.low && !a.gone;
      // Side contacts separate horizontally. A vertical solver turns every slap into a headbutt.
      if (sideStrike && age === 0) clearSide(p, captureVolumes(v), d.clone().negate());
      clearAbove(p, a.gone || sideStrike ? obstacles : [...obstacles, ...captureVolumes(v)]);
      if (sideStrike && age > 0) {
        const away = a.contactDirection;
        for (let pass = 0; pass < 8; pass++) {
          const before = v.position.clone();
          clearSide(v, captureVolumes(p), away);
          clearAbove(v, obstacles);
          if (before.distanceToSquared(v.position) < 1e-10) break;
        }
      }
      const lift = p.position.y - wantedY;
      a.lift = Math.max(lift, (a.lift || 0) * Math.exp(-dt * 18));
      p.position.y = wantedY + a.lift;
      const arrival = a.low ? .46 : hit + a.arrival;
      if (!a.landed && t >= arrival && p.position.y < .035) {
        a.touchdown = t; a.landed = true;
        landed(p, a.to, false, { capture: true, manner: 'signature', skipped: false });
      }
      if (t >= hit && !a.struck) {
        a.struck = true;
        if (!a.low) emit('hit', { piece: a.type, sound: spec.sound, impact: type === 'r' ? 'fling' : a.sideStrike ? 'slap' : 'squash', victim: v.userData.type, height: a.vHeight,
          direction: (a.sideStrike ? a.contactDirection : d).clone(),
          manner: 'signature', world: { x: a.target.x, y: a.sideStrike && type !== 'b' ? a.contactHeight : Math.min(a.vHeight, .65), z: a.target.z } });
      }
      if (!a.low) {
        if (type === 'r' && age > .04 && age < .40 && !a.gone) {
          const mark = 'trail' + Math.floor(age / .045);
          if (!a.marks.has(mark)) { a.marks.add(mark); emit('captureTrail', { world: v.position.clone(), direction: a.fly.clone() }); }
        }
        for (const [name, plan, clock] of [['approach', a.approach, a.t], ['square', a.step, age - stepStarts[type]]]) {
          if (name === 'square' && !stepStarts[type]) continue;
          for (let j = 0; j < plan.count; j++) for (const [event, when] of [['hop', .04], ['hopland', plan.flightEnd]]) {
            if (name === 'square' && event === 'hopland' && j === plan.count - 1) continue;
            const mark = name + j + event;
            if (clock >= j * plan.beat + when && !a.marks.has(mark)) {
              a.marks.add(mark); emit('captureCue', { name: event, piece: a.type });
              if (event === 'hopland') emit('hopLand', { piece: a.type, height: a.height,
                small: plan.small, world: { x: p.position.x, y: 0, z: p.position.z } });
            }
          }
        }
        if (type === 'p' || type === 'n') cue(a, 'square-hop', spec.name === 'heavy-squash' ? .32 : type === 'p' ? .16 : .30, 'hop');
        if (type === 'n' && a.type !== 'q') { cue(a, 'hooves', .02, 'hooves'); cue(a, 'neigh', .30, 'neigh'); }
        if (type === 'n' && a.type === 'q') cue(a, 'flip', .30, 'spin');
        if (type === 'kick') { cue(a, 'hooves', .08, 'hooves'); cue(a, 'snort', .37, 'neigh'); cue(a, 'kick-air', hit - .13, 'sweep'); }
        if (type === 'sweep') { cue(a, 'coil', .25, 'stretch'); cue(a, 'sweep-low', hit - .13, 'sweep'); }
        if (type === 'r') cue(a, 'charge', .32, 'charge');
        if (spec.name === 'triple-bash') {
          cue(a, 'final-charge', .97, 'stretch');
          for (const tap of [.48, .83]) if (t >= tap && !a.marks.has('tap' + tap)) {
            a.marks.add('tap' + tap);
            emit('captureCue', { name: 'hopland', piece: a.type });
            emit('contact', { piece: a.type, height: a.vHeight, direction: d.clone(),
              world: { x: a.target.x, y: a.vHeight, z: a.target.z } });
          }
        }
        if (type === 'b') {
          cue(a, 'sweep-one', .54, 'sweep'); cue(a, 'slap-one', .70, 'whip');
          cue(a, 'sweep-two', hit - .16, 'sweep');
          if (t >= .70 && !a.marks.has('contact-one')) {
            a.marks.add('contact-one');
            emit('contact', { piece: a.type, height: a.vHeight, direction: lateral.clone(),
              world: { x: a.target.x, y: Math.min(a.vHeight * .85, a.height * .90), z: a.target.z } });
          }
        }
      }
      if (t >= (a.low ? .46 : spec.end)) { finish(a, false, false); acts.splice(i, 1); }
    }
  }
  return { start, settle, update,
    skip() { for (const a of acts) finish(a); acts.length = 0; },
    clear() { for (const a of acts) finish(a, true); acts.length = 0; },
    replace(old, next) { for (const a of acts) if (a.piece === old) { a.piece = next; a.scale = next.scale.clone(); next.userData.busy = true; } },
    busy: () => acts.length > 0,
    stats: () => acts.map(a => ({ type: a.type, t: a.t, hit: a.spec.hit + (a.low ? 0 : a.approach.duration), end: a.spec.end + a.impactDelay + (a.low ? 0 : a.approach.duration), approach: a.approach.duration, variation: a.spec.name })),
  };
}
