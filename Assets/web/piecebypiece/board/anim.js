/* ============================================================================
 * board/anim.js - the weight and the wobble.
 *
 * Everything that makes the men feel like objects rather than sprites: a moved
 * piece arcs across and flexes as it lands, a knight hops over the board with a
 * lean at the top, a castling rook follows his king a beat behind, a captured
 * piece is tipped over, rolls once and sinks into the board, and the piece
 * giving check buzzes like a wand. Hooks into pieces.js so the game code never
 * has to ask for any of it.
 *
 * The landing squash is not a scale any more: it is handed to jiggle.js, which
 * bends and squashes the mesh itself so the man lands like silicone.
 *
 * Capture order is fixed here, on purpose: the victim starts tipping the frame
 * the move is played, the taker lands captureLand seconds later as the victim
 * hits the board, and only then does the victim roll away and sink. The dust
 * and the squelch ride the taker's landing, so the ear reads "he landed on
 * him". A knight takes longer to arrive (he hops), so his victim waits the
 * difference before he starts to fall and the beat still lands the same.
 *
 * Bus events this file emits, once bindBus(bus, project) has been called (the
 * board is silent without it; the dust and the sound listen):
 *   land    {square, piece, side, capture, height, world:{x,y,z}, screen:{x,y}}
 *           the frame a flight ends and the man touches his square; `capture`
 *           is true when he took the man who stood there. A refused drop's
 *           spring-back lands too, with `refused: true`.
 *   sunk    {piece, side, object}  a captured man has finished sinking and is
 *           gone from the board; `object` is the man himself, for anyone who
 *           wants to stand him somewhere else (the parade does).
 *   hit     {square, piece, side, victim, victimSide, world, screen, height}
 *           a capture animation struck its victim: the bishop's tentacle
 *           crossed him. The dust puffs and the crack plays off this one.
 * Every `land` also says its `manner`: 'plain' for the moves above, 'whip'
 * for the bishop's capture (board/whip.js), whose two landings (the stand-off
 * and the square) both come through here; the second carries `capture`.
 *
 * The whip keeps the 440 line (PBP-BEATS, beat 3): the bishop is on the
 * square, `land {capture:true}` fired, by 436 ms from the move; the tentacle
 * ringing out and the victim going over are scenery after that and gate
 * nothing. It is also skippable (Law VI): skip() lands the bishop on the
 * square and sends the victim off at once, and a take-back does the same on
 * its own. Under reduced motion the bishop takes the plain way, like everyone
 * else. The victim's SHIVER (house book) is a forced tremor across the whip
 * line at the crack: no colour, no emissive, a flinch and nothing more.
 *
 * Gate: window.PBP.settings.whip (default FALSE). The owner has parked the
 * capture animations, so the whip is built but off: a bishop takes the plain
 * way, like a rook, unless the setting is true (boot.js reads ?whip=1 into
 * it for dev pages and harnesses). whipOn() is the one place that asks.
 * ==========================================================================*/

import * as THREE from 'three';
import { TUNING as TUNE } from './jiggle.js';
import { WHIP_TUNING as W, whipBend, whipTimes, shiverAt } from './whip.js';

/** Every number that decides how a move plays. One place, on purpose. */
export const TUNING = Object.freeze({
  slideSec: 0.30,        // seconds a piece takes to cross to its square
  captureLand: 0.30,     // the taker lands this long after the victim starts to fall
  knightSec: 0.44,       // a hop takes longer than a slide
  knightHop: 0.9,        // world units, the top of the arc over the board
  knightLean: 0.30,      // radians of forward lean at the top of the hop
  knightLand: 1.6,       // extra squash on the landing, so he comes down harder
  castleLag: 0.15,       // the rook leaves this long after the king
  tipSec: 0.30,          // a taken man tips over in this long
  rollSec: 0.42,         // then rolls once
  rollPush: 0.32,        // world units he rolls away from the man who took him
  sinkSec: 0.7,          // and sinks
  buzzSec: 0.7,
});
const T = TUNING;

const ease = (t) => (t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2);
const easeOut = (t) => 1 - Math.pow(1 - t, 3);
const UP = new THREE.Vector3(0, 1, 0);

function prefersReducedMotion() {
  if (typeof window === 'undefined') return false;
  const s = window.PBP && window.PBP.settings;
  if ((s && s.reducedMotion) || (window.PBP && window.PBP.reducedMotion)) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

/** The whip is parked: off unless the setting says so (bloom.js's bloomOn, inverted default). */
function whipOn() {
  const s = (typeof window !== 'undefined' && window.PBP && window.PBP.settings) || {};
  return s.whip === true;
}

// A man is not always one material: a modelled king wears a metal crown over
// his silicone. The sink fade and the check glow belong to the whole man, so
// they go through every material he owns.
const skins = (piece) => piece.userData.materials
  || (piece.userData.material ? [piece.userData.material] : []);

/** The yaw a man stands at when nothing is happening to him. */
const baseYaw = (piece) => (piece.userData.side === 'b' ? Math.PI : 0);

export function createAnim({ group, jiggle = null }) {
  const slides = [];
  const tumbles = [];
  const buzzes = [];
  const whips = [];      // bishops mid-capture, from the stand-off landing until the tentacle is still
  const shivers = [];    // victims flinching at a crack: {piece, t, ax, az}
  const localDir = new THREE.Vector3();
  const invQ = new THREE.Quaternion();
  const qBase = new THREE.Quaternion();
  const qLean = new THREE.Quaternion();
  const axis = new THREE.Vector3();
  const dir = new THREE.Vector3();

  // --- J: bus hooks ---
  let emit = () => {};
  let project = () => null;
  function bindBus(bus, projectFn = null) {
    emit = bus && typeof bus.emit === 'function' ? (t, p) => bus.emit(t, p) : () => {};
    if (typeof projectFn === 'function') project = projectFn;
    // A move taken back mid-whip: the bishop is about to be slid home, so the
    // whip is finished on the spot rather than left driving a man in flight.
    if (bus && typeof bus.on === 'function') bus.on('takeback', () => skip());
  }
  function landed(piece, at, refused, extra = null) {
    const d = piece.userData;
    emit('land', Object.assign({
      square: d.square || null, piece: d.type, side: d.side,
      capture: !!d.tookOne && !refused, refused: !!refused,
      manner: 'plain',
      height: d.scaleBase || 1,
      world: { x: at.x, y: at.y, z: at.z },
      screen: project(at),
    }, extra || {}));
  }
  // --- end J ---

  /** Landing flex. `travel` is the world x/z the man just crossed, or null. */
  function squash(piece, travel = null) {
    if (jiggle) jiggle.land(piece, travel);
  }

  const sliding = (piece) => slides.some((s) => s.piece === piece);
  const fresh = (list) => list.filter((x) => x.fresh);

  /**
   * A piece has been placed on `to`; play it as travel rather than a jump.
   * `hop` overrides the arc height (springBack asks for a low one); a knight
   * ignores the default and hops. Two men leaving in the same frame are read
   * as a castle when one is a king and the other his rook on the same rank:
   * the rook waits a beat. A taker sets his victim's fall so the landing comes
   * exactly captureLand after it starts.
   */
  function slide(piece, from, to = null, hop = null, opts = {}) {
    if (!from) return;
    const dest = to || piece.position.clone();
    if (from.distanceToSquared(dest) < 1e-6) return;
    piece.userData.busy = true;   // hands the piece to us; idle wobble stands off
    const d = piece.userData;
    const refused = !!d.refusedDrop;
    const knight = d.type === 'n' && !refused && hop == null;
    const s = {
      piece, from: from.clone(), to: dest, t: 0, fresh: true, refused,
      dur: opts.dur ?? (knight ? T.knightSec : T.slideSec),
      hop: knight ? T.knightHop : (hop ?? Math.min(0.42, 0.12 + from.distanceTo(dest) * 0.05)),
      lean: knight ? T.knightLean : 0,
      knight,
      delay: 0,
      whip: null,               // set below: this slide is the bishop's approach
      finish: !!opts.finish,    // this slide is the bishop's last stride
    };
    d.refusedDrop = false;
    // Castling: the king is already in flight this frame, and this is his rook
    // leaving along the same rank. He goes a beat behind.
    if (d.type === 'r') {
      const king = fresh(slides).find((k) => k.piece.userData.type === 'k' && k.piece.userData.side === d.side
        && Math.abs(k.from.z - from.z) < 0.01 && Math.abs(dest.z - from.z) < 0.01 && Math.abs(k.to.x - k.from.x) > 1.5);
      if (king) s.delay = T.castleLag;
    }
    // A capture: the victim was tipped this same frame. He falls away from the
    // taker, and a slow arrival (a knight) holds him upright the difference.
    if (d.tookOne && !s.finish) {
      const victim = fresh(tumbles).find((v) => v.piece !== piece);
      if (victim) {
        dir.set(dest.x - from.x, 0, dest.z - from.z);
        if (dir.lengthSq() > 1e-6) {
          dir.normalize();
          victim.axis.crossVectors(UP, dir).normalize();
          victim.push.copy(dir).multiplyScalar(T.rollPush);
        }
        victim.delay = Math.max(0, s.dur - T.captureLand);
        // The bishop does not land on him. He stops one stand-off short on the
        // diagonal and the whip does the taking; the victim stands and waits
        // for the crack (board/whip.js), then goes over faster and further.
        // Only when the whip is switched on; off, he lands on him like anyone.
        if (d.type === 'b' && whipOn() && !refused && !prefersReducedMotion() && dir.lengthSq() > 1e-6) {
          const near = dest.clone().addScaledVector(dir, -W.standOff);
          s.to = near;
          s.dur = W.approachSec;
          s.hop = W.approachHop;
          s.whip = { dest, near, dir: dir.clone(), victim };
          victim.hold = true;
          victim.delay = 0;
          victim.tipSec = W.tipSec;
          victim.rollSec = W.rollSec;
          victim.push.copy(dir).multiplyScalar(W.rollPush);
        }
      }
    }
    slides.push(s);
    piece.position.copy(from);
  }

  /** The bishop has landed at the stand-off: the tentacle takes over. */
  function beginWhip(s) {
    const { piece } = s;
    const w = s.whip;
    invQ.copy(piece.quaternion).invert();
    localDir.copy(w.dir).applyQuaternion(invQ);
    whips.push({
      piece, victim: w.victim, dest: w.dest, near: w.near, dir: w.dir,
      lx: localDir.x, lz: localDir.z,
      t: 0, cracked: false, stepped: false,
    });
    piece.userData.busy = true;
  }

  /** The crack: the tip crosses the victim. Once per whip. */
  function crack(wp) {
    wp.cracked = true;
    const v = wp.victim;
    const at = v.piece.position;
    emit('hit', {
      square: v.piece.userData.square || null,
      piece: wp.piece.userData.type, side: wp.piece.userData.side,
      victim: v.piece.userData.type, victimSide: v.piece.userData.side,
      height: v.piece.userData.scaleBase || 1,
      world: { x: at.x, y: at.y, z: at.z },
      screen: project(at),
    });
    v.hold = false;
    v.t = 0;
    if (jiggle) {
      jiggle.impulse(v.piece, { bend: [wp.dir.x * W.hitBend, wp.dir.z * W.hitBend], squash: W.hitSquash });
      jiggle.impulse(wp.piece, { squash: W.lungeSquash });
      // SHIVER: across the whip line, in the victim's own frame.
      invQ.copy(v.piece.quaternion).invert();
      localDir.set(-wp.dir.z, 0, wp.dir.x).applyQuaternion(invQ);
      shivers.push({ piece: v.piece, t: 0, ax: localDir.x, az: localDir.z });
    }
  }

  /** The stride onto the square. The landing there is the capture landing. */
  function stride(wp) {
    wp.stepped = true;
    slide(wp.piece, wp.near, wp.dest, W.stepHop, { dur: W.stepSec, finish: true });
  }

  /**
   * Land everything the whip still owes, now: the bishop on the square, the
   * victim off the board. A click mid-whip, a take-back, or the harness.
   */
  function skip() {
    for (let i = whips.length - 1; i >= 0; i--) {
      const wp = whips[i];
      whips.splice(i, 1);
      if (!wp.cracked) crack(wp);
      for (let k = shivers.length - 1; k >= 0; k--) {
        if (shivers[k].piece === wp.victim.piece) shivers.splice(k, 1);
      }
      for (let k = slides.length - 1; k >= 0; k--) {
        if (slides[k].piece === wp.piece) slides.splice(k, 1);
      }
      wp.piece.position.copy(wp.dest);
      wp.piece.rotation.set(0, baseYaw(wp.piece), 0);
      wp.piece.userData.busy = false;
      landed(wp.piece, wp.dest, false, { manner: 'whip', stage: 'square', skipped: true });
      squash(wp.piece, [wp.dir.x, wp.dir.z]);
      const v = wp.victim;
      const vi = tumbles.indexOf(v);
      if (vi > -1) {
        tumbles.splice(vi, 1);
        group.remove(v.piece);
        for (const mat of skins(v.piece)) { mat.transparent = true; mat.opacity = 0; }
        emit('sunk', { piece: v.piece.userData.type, side: v.piece.userData.side, object: v.piece });
      }
    }
    // An approach still in flight (the bishop has not landed at the stand-off).
    for (let i = slides.length - 1; i >= 0; i--) {
      const s = slides[i];
      if (!s.whip) continue;
      slides.splice(i, 1);
      beginWhip(s);
      skip();
      return;
    }
  }

  /** Tipped over, one roll, then down through the board and gone. */
  function tumble(piece) {
    const a = new THREE.Vector3(Math.random() < 0.5 ? 1 : -1, 0, Math.random() * 2 - 1).normalize();
    tumbles.push({
      piece, axis: a, t: 0, fresh: true, delay: 0,
      start: piece.position.clone(), push: new THREE.Vector3(),
    });
  }

  /** The piece giving check gets the wand treatment. */
  function buzz(piece) {
    if (!piece) return;
    buzzes.push({ piece, t: 0 });
  }

  /** Lean into the hop: a tilt about the axis across the travel, then upright. */
  function leanTo(s, p) {
    const amount = s.lean * Math.sin(Math.PI * p);
    dir.set(s.to.x - s.from.x, 0, s.to.z - s.from.z);
    if (dir.lengthSq() < 1e-6) return;
    dir.normalize();
    axis.crossVectors(UP, dir).normalize();
    qBase.setFromAxisAngle(UP, baseYaw(s.piece));
    qLean.setFromAxisAngle(axis, amount);
    s.piece.quaternion.copy(qBase).premultiply(qLean);
  }

  function update(dt) {
    for (const s of slides) s.fresh = false;
    for (const t of tumbles) t.fresh = false;

    for (let i = slides.length - 1; i >= 0; i--) {
      const s = slides[i];
      s.t += dt;
      if (s.t < s.delay) continue;   // the rook waits for his king to go first
      const p = Math.min(1, (s.t - s.delay) / s.dur);
      s.piece.position.lerpVectors(s.from, s.to, ease(p));
      s.piece.position.y = s.to.y + (s.knight ? 4 * p * (1 - p) : Math.sin(Math.PI * p)) * s.hop;
      if (s.lean) leanTo(s, p);
      if (p >= 1) {
        s.piece.position.copy(s.to);
        if (s.lean) s.piece.rotation.set(0, baseYaw(s.piece), 0);
        slides.splice(i, 1);
        if (!sliding(s.piece)) s.piece.userData.busy = false;
        if (s.whip) {
          // The stand-off landing: a thud, dust, an honest squash, but not the
          // capture: tookOne is kept for the landing on the square.
          const d = s.piece.userData;
          landed(s.piece, s.to, false, { capture: false, manner: 'whip', stage: 'standoff' });
          d.tookOne = false;
          squash(s.piece, [s.to.x - s.from.x, s.to.z - s.from.z]);
          d.tookOne = true;
          beginWhip(s);
          continue;
        }
        landed(s.piece, s.to, s.refused, s.finish ? { manner: 'whip', stage: 'square' } : null);   // J: before squash, which spends tookOne
        squash(s.piece, [s.to.x - s.from.x, s.to.z - s.from.z]);
        if (s.knight && jiggle) jiggle.impulse(s.piece, { squash: T.knightLand });
      }
    }

    for (let i = whips.length - 1; i >= 0; i--) {
      const wp = whips[i];
      if (!wp.piece.parent) { whips.splice(i, 1); continue; }
      wp.t += dt;
      const k = whipTimes(W);
      const { bend, stage } = whipBend(wp.t, W);
      if (!wp.cracked && wp.t >= k.crack) crack(wp);
      // The stride starts at the crack and runs under the ring: the 440 line.
      if (!wp.stepped && wp.t >= k.step) stride(wp);
      if (stage === 'done') { whips.splice(i, 1); continue; }
      if (jiggle) jiggle.drive(wp.piece, wp.lx * bend, wp.lz * bend);
    }

    for (let i = shivers.length - 1; i >= 0; i--) {
      const sh = shivers[i];
      sh.t += dt;
      const a = shiverAt(sh.t, W);
      if (!a || !sh.piece.parent) { shivers.splice(i, 1); continue; }
      if (jiggle) jiggle.drive(sh.piece, sh.ax * a, sh.az * a);
    }

    for (let i = tumbles.length - 1; i >= 0; i--) {
      const t = tumbles[i];
      if (t.hold) continue;          // he stands and waits for the crack
      t.t += dt;
      if (t.t < t.delay) continue;   // he sees the knight coming
      const at = t.t - t.delay;
      const tipSec = t.tipSec || T.tipSec;
      const rollSec = t.rollSec || T.rollSec;
      // Tip: slow off the balance point, then over, so the taker lands as he
      // hits the board. Roll: the rest of a turn and a shove away from the
      // man who took him. Sink: through the board and gone.
      const tip = Math.min(1, at / tipSec);
      const roll = at <= tipSec ? 0 : Math.min(1, (at - tipSec) / rollSec);
      const angle = (Math.PI / 2) * tip * tip + Math.PI * 2 * easeOut(roll);
      t.piece.setRotationFromAxisAngle(t.axis, angle);
      t.piece.position.x = t.start.x + t.push.x * easeOut(roll);
      t.piece.position.z = t.start.z + t.push.z * easeOut(roll);
      if (roll >= 1) {
        const sink = Math.min(1, (at - tipSec - rollSec) / T.sinkSec);
        t.piece.position.y = t.start.y - sink * 1.1;
        for (const mat of skins(t.piece)) { mat.transparent = true; mat.opacity = 1 - sink; }
        if (sink >= 1) {
          group.remove(t.piece); tumbles.splice(i, 1);
          emit('sunk', { piece: t.piece.userData.type, side: t.piece.userData.side, object: t.piece });   // J
        }
      }
    }

    for (let i = buzzes.length - 1; i >= 0; i--) {
      const b = buzzes[i];
      if (sliding(b.piece) || whips.some((w) => w.piece === b.piece)) continue;   // let it land (or finish the whip) before it rattles
      if (!b.home) b.home = { x: b.piece.position.x, z: b.piece.position.z };
      b.t += dt;
      const p = Math.min(1, b.t / T.buzzSec);
      const amp = 0.035 * (1 - p);
      b.piece.position.x = b.home.x + Math.sin(b.t * 62) * amp;
      b.piece.position.z = b.home.z + Math.cos(b.t * 47) * amp * 0.6;
      // The body rattles, and the soft top rattles harder and later.
      if (jiggle) {
        const fa = TUNE.buzzAmp * (1 - p);
        jiggle.drive(b.piece, Math.sin(b.t * TUNE.buzzFastX) * fa, Math.cos(b.t * TUNE.buzzFastZ) * fa * 0.6);
      }
      for (const mat of skins(b.piece)) mat.emissiveIntensity = 0.55 * (1 - p);
      if (p >= 1) {
        b.piece.position.set(b.home.x, b.piece.position.y, b.home.z);
        for (const mat of skins(b.piece)) mat.emissiveIntensity = 0;
        buzzes.splice(i, 1);
      }
    }
  }

  /** A refused drop: spring back to where it came from, with a wobble to say no. */
  function springBack(piece) {
    const home = piece.userData.home;
    if (!home) return;
    piece.userData.refusedDrop = true;   // J: the landing says so on the bus
    slide(piece, piece.position.clone(), new THREE.Vector3(home.x, 0, home.z), 0.1);
    buzz(piece);
  }

  return {
    update, squash, slide, tumble, buzz, springBack, bindBus, skip,
    hooks: {
      onMoved: (piece, from) => slide(piece, from),
      onCaptured: (piece) => tumble(piece),
    },
    busy: () => slides.length + tumbles.length + whips.length > 0,
 /** True while a bishop is still owed his square; skip() ends it. The ring after that is scenery. */
    whipping: () => whips.some((w) => !w.stepped) || slides.some((s) => s.whip || s.finish),
    /** For the harness: what is in flight right now. */
    stats: () => ({
      slides: slides.map((s) => ({ type: s.piece.userData.type, t: s.t, delay: s.delay, dur: s.dur, hop: s.hop, knight: s.knight, whip: !!s.whip, finish: s.finish })),
      tumbles: tumbles.map((t) => ({ type: t.piece.userData.type, t: t.t, delay: t.delay, hold: !!t.hold })),
      whips: whips.map((w) => ({ t: w.t, cracked: w.cracked, stage: whipBend(w.t, W).stage })),
    }),
  };
}
