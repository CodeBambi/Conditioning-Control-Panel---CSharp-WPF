// Signature captures. The game commits immediately; these cancellable acts own only presentation.
import * as THREE from 'three';

export const ACTS = Object.freeze({
  p: { name: 'lamp-stomp', hit: .72, end: 2.18 },
  n: { name: 'backflip', hit: .90, end: 2.36 },
  b: { name: 'double-slap', hit: 1.02, end: 2.48 },
  k: { name: 'royal-squash', hit: .90, end: 2.64 },
  q: { name: 'breakdance', hit: 1.00, end: 2.46 },
  r: { name: 'home-run', hit: 1.00, end: 2.46 },
});
const clamp = t => Math.max(0, Math.min(1, t));
const smooth = t => { t = clamp(t); return t * t * t * (t * (t * 6 - 15) + 10); };
const phase = (t, a, b) => smooth((t - a) / (b - a));
const up = new THREE.Vector3(0, 1, 0);
const yaw = p => p.userData.side === 'b' ? Math.PI : 0;
const skins = p => p.userData.materials || [p.userData.material];
const scratch = new THREE.Vector3(), rotation = new THREE.Quaternion();

// Keep the actual body and jewellery vertices, excluding shadow discs and outline shells.
// Bounds and dissolve positions use the same quadratic bend as the vertex shader.
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
function worldVertex(piece, v) {
  const u = piece.userData.jiggleUniforms, act = piece.userData.capturePose;
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
    for (const x of [b.min.x, b.max.x]) for (const y of [b.min.y, b.max.y]) for (const z of [b.min.z, b.max.z]) {
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
    a.obstacles = group.children.filter(p => p !== piece && p !== victim && p.userData.type)
      .map(p => ({ piece: p, box: captureBounds(p).clone() }));
    a.ceiling = Math.max(1.35, ...a.obstacles.map(o => o.box.max.y)) + .12;
    // Compute the board exit from the victim's square, even when moving towards a far edge.
    a.exitDistance = Math.min(...['x', 'z'].filter(k => Math.abs(d[k]) > .01)
      .map(k => ((d[k] > 0 ? 5.6 : -5.6) - a.target[k]) / d[k]));
    acts.push(a); piece.position.copy(from);
    return a;
  }
  function pose(piece, scale, position, tilt, spin, direction, bend = 0, sy = 1, shrink = 1) {
    piece.scale.copy(scale).multiplyScalar(shrink); piece.scale.y *= sy;
    piece.scale.x /= Math.sqrt(sy); piece.scale.z /= Math.sqrt(sy);
    piece.position.copy(position);
    const axis = new THREE.Vector3().crossVectors(up, direction);
    piece.quaternion.setFromAxisAngle(up, yaw(piece) + spin);
    rotation.setFromAxisAngle(axis, tilt); piece.quaternion.premultiply(rotation);
    scratch.copy(direction).applyQuaternion(rotation.copy(piece.quaternion).invert()).multiplyScalar(bend);
    piece.userData.capturePose = { x: scratch.x, z: scratch.z };
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
      let at = a.from.clone(), tilt = 0, spin = 0, bend = 0, sy = 1, facing = d;
      if (a.low) {
        // Dissolve in place before entering the occupied square. No rolling or flying.
        at.lerp(a.to, phase(t, .30, .46));
      } else if (type === 'p' || type === 'n') {
        const leap = phase(t, .16, hit);
        at.lerp(a.target, phase(t, .24, hit - .10));
        at.y = Math.sin(Math.PI * leap) * (type === 'n' ? 1.65 : 1.0) + phase(t, .16, .36) * a.vHeight;
        if (t < .16) sy = 1 - .22 * Math.sin(Math.PI * t / .16);
        if (type === 'n') tilt = -Math.PI * 2 * phase(t, .28, hit - .13);
        if (t >= hit) {
          const flatten = phase(age, 0, .19);
          at.copy(a.target); at.y = a.vHeight * (1 - .88 * flatten) + .018;
          sy = 1 - .22 * Math.sin(Math.PI * clamp(age / .23));
          at.lerp(a.to, phase(age, .34, .67)); at.y *= 1 - phase(age, .34, .67);
        }
      } else {
        at.lerp(a.near, phase(t, 0, .30));
        const wind = Math.sin(Math.PI * phase(t, .30, hit));
        if (type === 'b') {
          const first = phase(t, .40, .59) * (1 - phase(t, .63, .80));
          const second = phase(t, .83, hit) * (1 - phase(age, .03, .28));
          bend = -.20 * wind + (a.radius + a.vRadius + .20) * (first + second);
          tilt = -.10 * wind + .42 * (first + second); spin = .16 * first - .12 * second;
          sy = 1 - (1 - Math.max(.58, Math.min(1, a.vHeight / a.height))) * (first + second);
        }
        if (type === 'k') {
          const reach = phase(t, .46, hit), press = phase(age, 0, .32);
          const recover = 1 - phase(age, .40, .83);
          sy = 1 + .20 * phase(t, .25, .52) * (1 - phase(t, .62, hit + .20));
          tilt = -.15 * wind + (1.05 * reach + .58 * press) * recover;
          bend = .22 * reach * recover;
          // The tall body bows all the way down, holding pressure before rising.
          at.addScaledVector(d, .12 * press * recover);
        }
        if (type === 'q') {
          const invert = phase(t, .30, .68) * (1 - phase(age, .14, .58));
          tilt = -2.48 * invert; spin = -Math.PI * 2 * (1 - phase(t, .60, hit));
          facing = d.clone().applyAxisAngle(up, spin);
          // A tilted headstand: keep the head planted while the broad base sweeps an orbit.
          at.addScaledVector(facing, -a.height * Math.sin(tilt) * invert);
          at.y = Math.max(0, -a.height * Math.cos(tilt)) * invert;
        }
        if (type === 'r') { tilt = -.48 * wind + .48 * phase(t, hit - .13, hit) * (1 - phase(age, .04, .27)); at.addScaledVector(d, -.15 * wind + .10 * phase(t, hit - .13, hit) * (1 - phase(age, .04, .27))); sy = 1 - .16 * wind; }
        at.lerp(a.to, phase(age, type === 'k' ? .55 : .32, type === 'k' ? 1.02 : .72));
      }
      if (!a.gone) {
        let pos = a.target.clone(), tip = 0, shrink = 1, vs = 1;
        if (!a.low && age > 0) {
          if (type === 'p' || type === 'n' || type === 'k') vs = 1 - .88 * phase(age, 0, type === 'k' ? .32 : .19);
          else if (type === 'r') {
            const travel = phase(age, .13, .75);
            pos.addScaledVector(d, a.exitDistance * travel);
            pos.y = (a.ceiling + .8) * phase(age, 0, .13) + 1.1 * Math.sin(Math.PI * travel);
            tip = 7 * phase(age, .10, .90);
          } else {
            tip = 1.5 * phase(age, 0, .22);
            shrink = 1 - .65 * phase(age, 0, .22);
            pos.addScaledVector(d, .12 * phase(age, 0, .22));
          }
        }
        if (!a.low && type === 'b' && t < hit) tip = .28 * phase(t, .59, .67) * (1 - phase(t, .72, .94));
        pose(v, a.vScale, pos, tip, 0, d, 0, vs, shrink);
        clearAbove(v, obstacles);
        const fade = a.low ? phase(age, 0, .12) : type === 'r' ? 0 : phase(age, type === 'k' ? .40 : .20, type === 'k' ? .70 : .48);
        if (fade > 0 && !a.dissolving) { a.dissolving = true; emit('dissolve', { object: v, low: a.low }); }
        if (v.userData.jiggleUniforms) v.userData.jiggleUniforms.uDissolve.value = a.low ? 0 : fade;
        for (const m of skins(v)) { m.opacity = 1 - fade; m.transparent = fade > 0; m.depthWrite = fade === 0; }
        if (fade >= 1 || (type === 'r' && age > .95 && !a.low)) exit(a);
      }
      if (!a.low) {
        const arrival = hit + (type === 'k' ? 1.02 : .72);
        const settle = clamp((t - arrival) / (spec.end - arrival));
        const envelope = Math.sin(Math.PI * settle) * Math.pow(1 - settle, 2);
        const ring = Math.sin(settle * Math.PI * 5);
        at.y += .38 * envelope * Math.abs(ring);
        sy += .22 * envelope * Math.cos(settle * Math.PI * 5);
        bend += .45 * envelope * ring;
        tilt += .09 * envelope * ring;
      }
      pose(p, a.scale, at, tilt, spin, facing, bend, sy);
      // A genuine contact surface: attacker and victim envelopes touch, never overlap.
      const wantedY = p.position.y;
      p.position.y += (a.lift || 0) * Math.exp(-dt * 18);
      clearAbove(p, a.gone ? obstacles : [...obstacles, ...captureVolumes(v)]);
      const lift = p.position.y - wantedY;
      a.lift = Math.max(lift, (a.lift || 0) * Math.exp(-dt * 18));
      p.position.y = wantedY + a.lift;
      if (t >= hit && !a.struck) {
        a.struck = true;
        if (!a.low) emit('hit', { piece: type, victim: v.userData.type, height: a.vHeight,
          manner: 'signature', world: { x: a.target.x, y: Math.min(a.vHeight, .65), z: a.target.z } });
      }
      if (!a.low) {
        if (type === 'n') { cue(a, 'hooves', .02, 'hooves'); cue(a, 'neigh', .18, 'neigh'); }
        if (type === 'r') cue(a, 'charge', .32, 'charge');
        if (type === 'q') cue(a, 'spin', .57, 'spin');
        if (type === 'b') {
          cue(a, 'sweep-one', .44, 'sweep'); cue(a, 'slap-one', .59, 'whip');
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
