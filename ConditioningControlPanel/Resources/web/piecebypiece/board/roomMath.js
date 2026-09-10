/* ============================================================================
 * board/roomMath.js - the numbers and the pure functions behind board/room.js.
 *
 * Kept apart from room.js so a node smoke can pin the dome's gradient, the
 * lean of the light rig and the turn tell's timeline without a renderer, the
 * way smoke/whip-smoke.mjs pins the whip. Nothing in here touches three.
 * ==========================================================================*/

/** Every number the room is made of. One place, on purpose. */
export const ROOM = Object.freeze({
  // --- the floor: a dark glossy table top the plinth stands on --------------
  floorSize: 220,          // world units; the fog ends long before the edge
  floorY: -0.585,          // the plinth's underside is -0.58
  floorColor: 0x1C1240,    // dark plum, a shade under the plinth walls
  floorRough: 0.55,        // matte-ish: at a grazing angle a gloss floor went white
  floorClearcoat: 0,       // (the clearcoat's Fresnel did most of that; off)
  floorClearcoatRough: 0.4,
  floorEnv: 0.35,          // how much of env.js's room the sheen picks up
  // --- the pool of shade under the plinth ----------------------------------
  poolSize: 14.5,          // the plinth is 9.5 across; the shade reaches past it
  poolInset: 0.16,         // fraction of the canvas the dark core stops short of
  poolAlpha: 0.62,
  poolPx: 256,
  // --- the dome: plum at the horizon, a pink haze low, black overhead --------
  domeRadius: 60,          // inside the camera's far plane (120) with room to spare
  horizon: 0x3A1C58,       // = the fog colour: the far floor melts into exactly this
  haze: 0x7A3070,
  zenith: 0x06040C,
  under: 0x0A0713,         // below the horizon, where only the side view can look
  hazeAt: 0.18,            // sin(elevation) where the haze peaks
  zenithAt: 0.62,          // and where the black has taken over
  underAt: 0.35,
  fogNear: 12,             // the floor melts into the horizon between these
  fogFar: 34,
  // --- the turn tell: the light rig leans to the side to move ----------------
  leanMs: 480,
  leanDeg: 180,            // white's rig, swung round to stand behind black
  // --- and the edge of the plinth on the mover's side lights up --------------
  glowHit: 1.0,            // full on the first frame of the turn
  glowRest: 0.42,          // and easing to this
  glowOff: 0.06,           // the other side's edge, waiting
  glowOver: 0.12,          // both edges once the game is over
  glowMs: 480,
  glowColor: 0xFF69B4,
  glowLength: 9.3,         // along the plinth's edge (the plinth is 9.4 wide)
  glowWidth: 0.09,         // across the bevel, which is 0.07 on the slope
  glowLift: 0.014,         // off the bevel along its normal (out and up), each axis
});

export const clamp01 = (v) => Math.min(1, Math.max(0, Number.isFinite(v) ? v : 0));
export const easeOutCubic = (t) => 1 - Math.pow(1 - clamp01(t), 3);
export const smoothstep = (a, b, x) => { const t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); };

const hexToRgb = (hex) => [((hex >> 16) & 255) / 255, ((hex >> 8) & 255) / 255, (hex & 255) / 255];
const mix = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

/**
 * The dome's colour at a direction whose sine of elevation is `h` (-1 to 1),
 * as [r, g, b] in 0..1. The same three mixes as the shader in room.js; the
 * shader is the one that draws, this is the one the smoke can read.
 */
export function domeColorAt(h, T = ROOM) {
  const horizon = hexToRgb(T.horizon);
  if (h < 0) return mix(horizon, hexToRgb(T.under), smoothstep(0, T.underAt, -h));
  const c = mix(horizon, hexToRgb(T.haze), smoothstep(0, T.hazeAt, h));
  return mix(c, hexToRgb(T.zenith), smoothstep(T.hazeAt, T.zenithAt, h));
}

/** Where the light rig stands for `side`, in radians about +Y. */
export function yawFor(side, T = ROOM) {
  return side === 'b' ? (T.leanDeg * Math.PI) / 180 : 0;
}

/** What each edge glow eases toward once `side` is to move (null = game over). */
export function glowTargets(side, T = ROOM) {
  if (side !== 'w' && side !== 'b') return { w: T.glowOver, b: T.glowOver };
  return { w: side === 'w' ? T.glowRest : T.glowOff, b: side === 'b' ? T.glowRest : T.glowOff };
}

/**
 * A handful of eased tweens, driven by the frame loop's own dt so a stepped
 * harness clock sees them run. `to` replaces any tween on the same target and
 * key; a new tween starts from wherever the value is now.
 */
export function createTweens() {
  const live = [];
  function to(obj, key, value, ms, ease = easeOutCubic) {
    for (let i = live.length - 1; i >= 0; i--) if (live[i].obj === obj && live[i].key === key) live.splice(i, 1);
    if (!(ms > 0)) { obj[key] = value; return; }
    live.push({ obj, key, from: obj[key], to: value, t: 0, ms, ease });
  }
  function update(dt) {
    const ms = Math.max(0, dt) * 1000;
    for (let i = live.length - 1; i >= 0; i--) {
      const w = live[i];
      w.t += ms;
      const p = w.ease(w.t / w.ms);
      w.obj[w.key] = w.from + (w.to - w.from) * p;
      if (w.t >= w.ms) { w.obj[w.key] = w.to; live.splice(i, 1); }
    }
  }
  /** Jump every tween to its end, for a still frame. */
  function settle() { for (const w of live) w.obj[w.key] = w.to; live.length = 0; }
  return { to, update, settle, count: () => live.length };
}
