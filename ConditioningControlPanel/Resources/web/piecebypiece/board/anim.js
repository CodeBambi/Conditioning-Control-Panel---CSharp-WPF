/* ============================================================================
 * board/anim.js - the weight and the wobble.
 *
 * Everything that makes the men feel like objects rather than sprites: a moved
 * piece arcs across and flexes as it lands, a captured piece is knocked over,
 * rolls once and sinks into the board, and the piece giving check buzzes like a
 * wand. Hooks into pieces.js so the game code never has to ask for any of it.
 *
 * The landing squash is not a scale any more: it is handed to jiggle.js, which
 * bends and squashes the mesh itself so the man lands like silicone.
 * ==========================================================================*/

import * as THREE from 'three';
import { TUNING as TUNE } from './jiggle.js';

const SLIDE = 0.30;   // seconds a piece takes to cross to its square
const BUZZ = 0.7;
const FALL = 0.55;
const SINK = 0.7;

const ease = (t) => (t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2);

// A man is not always one material: a modelled king wears a metal crown over
// his silicone. The sink fade and the check glow belong to the whole man, so
// they go through every material he owns.
const skins = (piece) => piece.userData.materials
  || (piece.userData.material ? [piece.userData.material] : []);

export function createAnim({ group, jiggle = null }) {
  const slides = [];
  const tumbles = [];
  const buzzes = [];

  /** Landing flex. `travel` is the world x/z the man just crossed, or null. */
  function squash(piece, travel = null) {
    if (jiggle) jiggle.land(piece, travel);
  }

  const sliding = (piece) => slides.some((s) => s.piece === piece);

  /** A piece has been placed on `to`; play it as travel rather than a jump. */
  function slide(piece, from, to = null, hop = null) {
    if (!from) return;
    const dest = to || piece.position.clone();
    if (from.distanceToSquared(dest) < 1e-6) return;
    piece.userData.busy = true;   // hands the piece to us; idle wobble stands off
    slides.push({ piece, from: from.clone(), to: dest, t: 0, hop: hop ?? Math.min(0.42, 0.12 + from.distanceTo(dest) * 0.05) });
    piece.position.copy(from);
  }

  /** Knocked over, one roll, then down through the board and gone. */
  function tumble(piece) {
    const axis = new THREE.Vector3(Math.random() < 0.5 ? 1 : -1, 0, Math.random() * 2 - 1).normalize();
    tumbles.push({ piece, axis, t: 0, start: piece.position.y });
  }

  /** The piece giving check gets the wand treatment. */
  function buzz(piece) {
    if (!piece) return;
    buzzes.push({ piece, t: 0 });
  }

  function update(dt) {
    for (let i = slides.length - 1; i >= 0; i--) {
      const s = slides[i];
      s.t += dt;
      const p = Math.min(1, s.t / SLIDE);
      s.piece.position.lerpVectors(s.from, s.to, ease(p));
      s.piece.position.y = s.to.y + Math.sin(Math.PI * p) * s.hop;
      if (p >= 1) {
        s.piece.position.copy(s.to);
        slides.splice(i, 1);
        if (!sliding(s.piece)) s.piece.userData.busy = false;
        squash(s.piece, [s.to.x - s.from.x, s.to.z - s.from.z]);
      }
    }

    for (let i = tumbles.length - 1; i >= 0; i--) {
      const t = tumbles[i];
      t.t += dt;
      const fall = Math.min(1, t.t / FALL);
      t.piece.setRotationFromAxisAngle(t.axis, ease(fall) * (Math.PI / 2 + Math.PI * 2));
      if (t.t > FALL) {
        const sink = Math.min(1, (t.t - FALL) / SINK);
        t.piece.position.y = t.start - sink * 1.1;
        for (const mat of skins(t.piece)) { mat.transparent = true; mat.opacity = 1 - sink; }
        if (sink >= 1) { group.remove(t.piece); tumbles.splice(i, 1); }
      }
    }

    for (let i = buzzes.length - 1; i >= 0; i--) {
      const b = buzzes[i];
      if (sliding(b.piece)) continue;   // let it land before it rattles
      if (!b.home) b.home = { x: b.piece.position.x, z: b.piece.position.z };
      b.t += dt;
      const p = Math.min(1, b.t / BUZZ);
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
    slide(piece, piece.position.clone(), new THREE.Vector3(home.x, 0, home.z), 0.1);
    buzz(piece);
  }

  return {
    update, squash, slide, tumble, buzz, springBack,
    hooks: {
      onMoved: (piece, from) => slide(piece, from),
      onCaptured: (piece) => tumble(piece),
    },
    busy: () => slides.length + tumbles.length > 0,
  };
}
