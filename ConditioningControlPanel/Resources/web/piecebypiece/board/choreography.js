// Signature captures. The game commits immediately; these cancellable acts own only presentation.
import * as THREE from 'three';
import { siliconePoint } from './silicone.js';

export const ACTS = Object.freeze({
  p: { name: 'lamp-stomp', hit: .72, end: 2.18 },
  n: { name: 'backflip', hit: .90, end: 2.36 },
  b: { name: 'double-whip', hit: 1.20, end: 2.85 },
  k: { name: 'royal-squash', hit: .90, end: 2.64 },
  q: { name: 'breakdance', hit: 1.58, end: 3.35 },
  r: { name: 'side-swing', hit: 1.30, end: 2.95 },
});
const clamp = t => Math.max(0, Math.min(1, t));
const smooth = t => { t = clamp(t); return t * t * t * (t * (t * 6 - 15) + 10); };
const phase = (t, a, b) => smooth((t - a) / (b - a));
const arrivals = { p: .50, n: .50, b: .72, k: 1.08, q: .94, r: .72 };
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
  const acts = [];
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
    const type = piece.userData.type, spec = ACTS[type];
    const d = to.clone().sub(from).setY(0).normalize();
    if (d.lengthSq() < .1) d.set(0, 0, piece.userData.side === 'w' ? -1 : 1);
    const a = { piece, victim, from: from.clone(), to: to.clone(), target: victim.position.clone(), d,
      type, spec, t: 0, scale: piece.scale.clone(), vScale: victim.scale.clone(), low: reduced(), marks: new Set() };
    piece.rotation.set(0, yaw(piece), 0); victim.rotation.set(0, yaw(victim), 0);
    piece.userData.capturePose = { x: 0, z: 0 }; victim.userData.capturePose = { x: 0, z: 0 };
    piece.userData.busy = victim.userData.busy = true;
    a.height = captureBounds(piece).getSize(new THREE.Vector3()).y;
    a.vHeight = captureBounds(victim).getSize(new THREE.Vector3()).y;
    a.radius = Math.max(box.copy(captureBounds(piece)).getSize(scratch).x, scratch.z) / 2;
    a.vRadius = Math.max(box.copy(captureBounds(victim)).getSize(scratch).x, scratch.z) / 2;
    a.near = a.target.clone().addScaledVector(d, -(a.radius + a.vRadius + .12));
    if (type === 'k') a.near.copy(a.target).addScaledVector(d, -a.height * .94);
    a.sideways = new THREE.Vector3(-d.z, 0, d.x);
    a.contactHeight = Math.max(.18, Math.min(.55, a.vHeight * .36));
    a.queenReach = a.height * .78;
    a.queenAnchor = a.target.clone().addScaledVector(d, -(a.queenReach + a.radius + a.vRadius - .04));
    a.fly = type === 'r' ? a.sideways.clone() : d.clone();
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
      act.stretch = flex.stretch; act.drop = flex.drop; act.twist = flex.twist; act.bulge = flex.bulge || 0;
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
    if (a.t < time || a.marks.has(mark)) return;
    a.marks.add(mark); emit('captureCue', { name, piece: a.type });
  }
  function update(dt) {
    for (let i = acts.length - 1; i >= 0; i--) {
      const a = acts[i]; a.t += dt;
      if (!a.piece.parent) { exit(a); acts.splice(i, 1); continue; }
      if (a.piece.userData.held || (!a.low && reduced())) { finish(a); acts.splice(i, 1); continue; }
      const { piece: p, victim: v, type, spec, t, d } = a;
      const hit = a.low ? .18 : spec.hit, age = Math.max(0, t - hit);
      const obstacles = a.obstacles.filter(o => o.piece.parent).flatMap(o => captureVolumes(o.piece));
      const lateral = a.sideways;
      const flex = { tip: new THREE.Vector3(), lag: new THREE.Vector3(), stretch: 0, drop: 0, twist: 0 };
      let headPin = 0;
      let at = a.from.clone(), tilt = 0, spin = 0, bend = 0, sy = 1, facing = d;
      if (a.low) {
        // Dissolve in place before entering the occupied square. No rolling or flying.
        at.lerp(a.to, phase(t, .30, .46));
      } else if (type === 'p' || type === 'n') {
        const launch = type === 'n' ? .30 : .16;
        const leap = clamp((t - launch) / (hit - launch));
        at.lerp(a.target, phase(t, launch + .08, hit - .10));
        at.y = Math.sin(Math.PI * leap) * (type === 'n' ? 1.65 : 1.0) + phase(t, launch, launch + .20) * a.vHeight;
        const crouch = phase(t, .02, launch * .70) * (1 - phase(t, launch * .70, launch + .06));
        sy = 1 - .28 * crouch;
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
        at.lerp(a.near, phase(t, 0, .30));
        if (type === 'b') {
          const ready = phase(t, .28, .52), release = 1 - phase(age, .14, .58);
          const sweep = -.82 + 1.64 * phase(t, .54, .86) - 1.64 * phase(t, 1.04, 1.36);
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
        }
        if (type === 'q') {
          // Three readable beats: arch onto the crown, lift/coil the base, then whip.
          const arch = phase(t, .22, .64), liftBase = phase(t, .80, 1.10);
          const recover = 1 - phase(age, .28, .60), invert = arch * recover;
          const coil = phase(t, .84, 1.18), lash = phase(t, 1.32, 1.70);
          const orbit = -Math.PI - .41 * coil + 4.35 * lash - 1.35 * phase(age, .15, .40);
          facing = d.clone().applyAxisAngle(up, orbit);
          tilt = -Math.PI / 2 * liftBase * invert;
          const baseHeight = Math.max(a.radius + .03, a.contactHeight) * liftBase;
          const reach = a.queenReach;
          // Solve both ends of the curved spine. The head stays fixed while the base rises.
          flex.tip.copy(facing).multiplyScalar(-reach * invert).addScaledVector(up, -baseHeight * invert);
          flex.stretch = .55 * (1 - liftBase) * invert;
          flex.drop = (reach * Math.sin(-tilt) - baseHeight * Math.cos(tilt) - a.height * (1 + flex.stretch)) * invert;
          flex.lag.copy(up).multiplyScalar(.52 * liftBase * invert).addScaledVector(lateral, -.32 * Math.sin(orbit) * invert);
          flex.twist = -.35 * Math.sin(orbit) * invert;
          headPin = arch * (1 - phase(age, .28, .55));
        }
        if (type === 'r') {
          const ready = phase(t, .30, .76), release = 1 - phase(age, .16, .62);
          const tension = phase(t, .76, .84) * (1 - phase(t, hit - .12, hit - .08));
          const tremble = .025 * Math.sin((t - .76) * 76) * tension;
          const swing = -1.08 + 2.16 * phase(t, hit - .10, hit + .10) + tremble;
          const trail = -1.08 + 2.16 * phase(t - .07, hit - .10, hit + .10);
          const reach = (a.radius + a.vRadius + .12) * phase(t, hit - .12, hit) * release;
          flex.tip.copy(lateral).multiplyScalar(swing * ready * release).addScaledVector(d, reach);
          flex.lag.copy(lateral).multiplyScalar((trail - swing) * .85 * ready * release);
          flex.stretch = .12 * ready * release;
          flex.drop = (a.contactHeight - a.height * 1.12) * ready * release;
          flex.twist = -.60 * swing * ready * release;
          at.addScaledVector(lateral, -.20 * phase(age, 0, .14) * (1 - phase(age, .23, .66)));
        }
        const hopStart = type === 'k' ? .70 : .55;
        at.lerp(a.to, phase(age, ['k', 'q'].includes(type) ? hopStart : .32, arrivals[type]));
        if (type === 'k' || type === 'q') at.y = .48 * Math.sin(Math.PI * clamp((age - hopStart) / (arrivals[type] - hopStart)));
      }
      if (!a.gone) {
        let pos = a.target.clone(), tip = 0, shrink = 1, vs = 1;
        if (!a.low && age > 0) {
          if (type === 'p' || type === 'n' || type === 'k') vs = 1 - .88 * phase(age, 0, type === 'k' ? .11 : .19);
          else if (type === 'r') {
            const travel = 1 - Math.pow(1 - clamp(age / .75), 2);
            pos.addScaledVector(a.fly, a.exitDistance * travel);
            pos.y = .90 * Math.sin(Math.PI * travel);
            tip = 7 * phase(age, .10, .90);
          } else {
            tip = 1.5 * phase(age, 0, .22);
            shrink = 1 - .65 * phase(age, 0, .22);
            pos.addScaledVector(d, .12 * phase(age, 0, .22));
          }
        }
        if (!a.low && type === 'b' && t < hit) tip = .28 * phase(t, .70, .78) * (1 - phase(t, .88, 1.10));
        pose(v, a.vScale, pos, type === 'b' ? -tip : tip, 0, type === 'b' || type === 'r' ? lateral : d, 0, vs, shrink);
        clearAbove(v, obstacles);
        const fade = a.low ? phase(age, 0, .12) : type === 'r' ? 0 : phase(age, type === 'k' ? .40 : .20, type === 'k' ? .70 : .48);
        if (fade > 0 && !a.dissolving) { a.dissolving = true; emit('dissolve', { object: v, low: a.low }); }
        if (v.userData.jiggleUniforms) v.userData.jiggleUniforms.uDissolve.value = a.low ? 0 : fade;
        for (const m of skins(v)) { m.opacity = 1 - fade; m.transparent = fade > 0; m.depthWrite = fade === 0; }
        if (fade >= 1 || (type === 'r' && age > .95 && !a.low)) exit(a);
      }
      if (!a.low) {
        const arrival = hit + arrivals[type];
        const stomp = type === 'p' || type === 'n';
        // Stomp contact is the first landing, not the later move onto the empty square.
        const elapsed = Math.max(0, t - (stomp ? hit : arrival)), fade = 1 - phase(t, spec.end - .22, spec.end);
        const ring = -Math.sin(elapsed * 23) * Math.exp(-elapsed * 4.5) * fade;
        const delayed = -Math.sin(elapsed * 23 - .45) * Math.exp(-elapsed * 4.5) * phase(elapsed, 0, .04) * fade;
        flex.tip.addScaledVector(d, .22 * ring).addScaledVector(lateral, .12 * delayed);
        flex.lag.addScaledVector(d, -.32 * delayed);
        flex.stretch += (stomp ? .26 : .16) * ring;
        // Travel and impacts stretch the spine; its planted base does not rock.
        if (type === 'p' || type === 'n') {
          flex.stretch += sy - 1; sy = 1;
          flex.tip.addScaledVector(d, -.20 * Math.sin(Math.PI * phase(t, type === 'n' ? .30 : .16, hit)));
          flex.lag.addScaledVector(d, .18 * Math.sin(Math.PI * phase(t, .25, hit + .12)));
        }
      }
      pose(p, a.scale, at, tilt, spin, facing, bend, sy, 1, flex);
      if (headPin) {
        const head = worldVertex(p, { x: 0, y: p.userData.jiggleUniforms.uHeight.value, z: 0 }).clone();
        const anchor = a.queenAnchor.clone(); anchor.y = .003;
        p.position.addScaledVector(anchor.sub(head), headPin);
        p.position.y -= Math.min(0, captureBounds(p).min.y);
      }
      // A genuine contact surface: attacker and victim envelopes touch, never overlap.
      const wantedY = p.position.y;
      if ((type === 'p' || type === 'n') && t >= hit) a.lift = 0;
      p.position.y += (a.lift || 0) * Math.exp(-dt * 18);
      const sideStrike = ['b', 'q', 'r'].includes(type) && !a.low && !a.gone;
      // Side contacts separate horizontally. A vertical solver turns every slap into a headbutt.
      if (sideStrike && age === 0) clearSide(p, captureVolumes(v), d.clone().negate());
      clearAbove(p, a.gone || sideStrike ? obstacles : [...obstacles, ...captureVolumes(v)]);
      if (sideStrike && age > 0) {
        const away = type === 'r' ? a.fly : d;
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
      const arrival = a.low ? .46 : hit + arrivals[type];
      if (!a.landed && t >= arrival && p.position.y < .035) {
        a.touchdown = t; a.landed = true;
        landed(p, a.to, false, { capture: true, manner: 'signature', skipped: false });
      }
      if (t >= hit && !a.struck) {
        a.struck = true;
        if (!a.low) emit('hit', { piece: type, victim: v.userData.type, height: a.vHeight,
          manner: 'signature', world: { x: a.target.x, y: ['q', 'r'].includes(type) ? a.contactHeight : Math.min(a.vHeight, .65), z: a.target.z } });
      }
      if (!a.low) {
        if (type === 'n') { cue(a, 'hooves', .02, 'hooves'); cue(a, 'neigh', .30, 'neigh'); }
        if (type === 'r') cue(a, 'charge', .32, 'charge');
        if (type === 'q') cue(a, 'spin', 1.32, 'spin');
        if (type === 'b') {
          cue(a, 'sweep-one', .54, 'sweep'); cue(a, 'slap-one', .70, 'whip');
          cue(a, 'sweep-two', hit - .16, 'sweep');
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
    stats: () => acts.map(a => ({ type: a.type, t: a.t, hit: a.spec.hit, variation: a.spec.name })),
  };
}
