/* ============================================================================
 * board/watch.js - the room watches.
 *
 * Past meter 0.4, while a man is held, the other men turn to look at him:
 * each one's yaw eases toward the held man, capped at twelve degrees off
 * his own facing (white faces the far side, black the near, and that stays
 * their base). When he is put down they ease back. Below 0.4, or with
 * nobody held, the room minds its own business.
 *
 * Hesitation: a man held for four seconds gets a half-strength grab
 * impulse (a little flinch in the hand) and the room whispers, once per
 * hold.
 *
 * Reduced motion (window.PBP.settings.reducedMotion or the media query):
 * nobody turns, nobody flinches, nobody whispers.
 * ==========================================================================*/

import * as THREE from 'three';
import { TUNING as JIGGLE } from './jiggle.js';

/** Every number in the room's attention. */
export const TUNING = Object.freeze({
  meterOn: 0.4,          // the room starts watching past this
  maxTurn: 12 * Math.PI / 180,   // radians off a man's own facing
  ease: 4.0,             // per-second pull toward the wanted yaw
  hesitateSec: 4.0,      // held this long: the flinch and the whisper
  flinch: 0.5,           // of the grab squash
});

function reduced() {
  if (typeof window === 'undefined') return false;
  const s = window.PBP && window.PBP.settings;
  if ((s && s.reducedMotion) || (window.PBP && window.PBP.reducedMotion)) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

const wrap = (a) => Math.atan2(Math.sin(a), Math.cos(a));
const baseYaw = (piece) => (piece.userData.side === 'b' ? Math.PI : 0);

/**
 * `group` is the piece group; `bus` the game bus (grab / drop / land mark a
 * hold); `jiggle` the flex system; `sfx` a getter for the sound module.
 */
export function createWatch({ group, bus = null, jiggle = null, sfx = null }) {
  const T = TUNING;
  let meter = 0;
  let held = null;
  let heldFor = 0;
  let hesitated = false;
  let turned = 0;

  function findHeld() {
    for (const p of group.children) if (p.userData && p.userData.held) return p;
    return null;
  }

  function update(dt) {
    const now = findHeld();
    if (now !== held) { held = now; heldFor = 0; hesitated = false; }
    const watching = !!held && meter >= T.meterOn && !reduced();
    if (held) {
      heldFor += dt;
      if (watching && !hesitated && heldFor >= T.hesitateSec) {
        hesitated = true;
        if (jiggle) jiggle.impulse(held, { squash: JIGGLE.grabSquash * T.flinch });
        const s = sfx ? sfx() : null;
        if (s && s.play) s.play('whisper');
      }
    }
    const k = 1 - Math.exp(-T.ease * (dt || 0.016));
    turned = 0;
    for (const p of group.children) {
      if (!p.userData || p === held || p.userData.busy || p.userData.parade || p.userData.pose) continue;
      const base = baseYaw(p);
      let want = base;
      if (watching) {
        // Facing (-sin y, 0, -cos y) is what rotation.y = y looks along.
        const dx = held.position.x - p.position.x;
        const dz = held.position.z - p.position.z;
        if (dx * dx + dz * dz > 1e-6) {
          const look = Math.atan2(-dx, -dz);
          want = base + THREE.MathUtils.clamp(wrap(look - base), -T.maxTurn, T.maxTurn);
        }
      }
      const delta = wrap(want - p.rotation.y);
      if (Math.abs(delta) < 1e-4) { p.rotation.y = want; continue; }
      p.rotation.y += delta * k;
      if (watching) turned++;
    }
  }

  return {
    update,
    setMeter(m) { meter = Math.max(0, Math.min(1, Number(m) || 0)); },
    /** For the harness. */
    stats() {
      let maxOff = 0;
      for (const p of group.children) {
        if (!p.userData || p === held) continue;
        maxOff = Math.max(maxOff, Math.abs(wrap(p.rotation.y - baseYaw(p))));
      }
      return { meter, held: held ? held.userData.type : null, heldFor: +heldFor.toFixed(2), hesitated, turning: turned, maxOffDeg: +(maxOff * 180 / Math.PI).toFixed(2) };
    },
  };
}
