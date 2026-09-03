/* ============================================================================
 * zones.js — bespoke, game-tuned "zones" for the endless rabbit hole.
 *
 * A zone is a stretch of tunnel with its own SHAPE (steering rates fed to the
 * spine generator), SPEED profile (slow / fast-accel), and LOOK (palette, fog,
 * glow, scroll). The scheduler lays zones end-to-end along the infinite arc
 * length with no immediate repeat, biased deeper/bolder as the host raises the
 * depth/intensity hint. Rendering blends the palette/fog/speed across a short
 * transition band so crossings crossfade instead of snapping; the geometry
 * shape changes crisply at the boundary (a new zone SHOULD feel different).
 *
 * Steering rates are radians PER WORLD UNIT (the generator multiplies by its
 * fixed arc STEP), so tuning is resolution-independent.
 * ==========================================================================*/

function hex(h) {
  return { r: ((h >> 16) & 255) / 255, g: ((h >> 8) & 255) / 255, b: (h & 255) / 255 };
}
const lerp = (a, b, t) => a + (b - a) * t;
const smooth = (x) => { const u = Math.min(1, Math.max(0, x)); return u * u * (3 - 2 * u); };
function lerpCol(a, b, t) { return { r: lerp(a.r, b.r, t), g: lerp(a.g, b.g, t), b: lerp(a.b, b.b, t) }; }

// event names are documentation; the numbers do the work.
const ZONES = [
  {
    name: 'pink-descent', event: 'wind', minLen: 120, maxLen: 200, bold: 0,
    steer: { yaw: 0, pitch: 0.004, roll: 0, wind: 0.010, windFreq: 0.045, wobble: 0.006 },
    speed: { target: 26, accel: 0.7 },
    palette: { bg1: hex(0x1a0f22), bg2: hex(0x0c0814), line: hex(0xff5fb0), spiral: hex(0xb078ff), fog: hex(0x1a0d20) },
    fogDensity: 0.016, glowMul: 1.0, scroll: 0.6,
  },
  {
    name: 'twist-turns', event: 'dogleg', minLen: 100, maxLen: 170, bold: 1,
    steer: { yaw: 0.016, pitch: 0.003, roll: 0.004, wind: 0.018, windFreq: 0.030, wobble: 0.010 },
    speed: { target: 30, accel: 0.9 },
    palette: { bg1: hex(0x241033), bg2: hex(0x0e0820), line: hex(0xff79d0), spiral: hex(0x8a5cff), fog: hex(0x1c0e2c) },
    fogDensity: 0.018, glowMul: 1.1, scroll: 0.8,
  },
  {
    name: 'void-slow', event: 'straight', minLen: 90, maxLen: 150, bold: 0,
    steer: { yaw: 0, pitch: -0.002, roll: 0.0015, wind: 0.004, windFreq: 0.02, wobble: 0.003 },
    speed: { target: 13, accel: 0.5 },
    palette: { bg1: hex(0x05060c), bg2: hex(0x02030a), line: hex(0x3f6bd6), spiral: hex(0x244a9e), fog: hex(0x03040a) },
    fogDensity: 0.010, glowMul: 0.75, scroll: 0.35,
  },
  {
    name: 'corkscrew-rave', event: 'corkscrew', minLen: 130, maxLen: 210, bold: 2,
    steer: { yaw: 0.010, pitch: 0.006, roll: 0.052, wind: 0.014, windFreq: 0.05, wobble: 0.010 },
    speed: { target: 44, accel: 1.5 },
    palette: { bg1: hex(0x2a0a2e), bg2: hex(0x120616), line: hex(0xff3b8b), spiral: hex(0x28e0ff), fog: hex(0x220a26) },
    fogDensity: 0.026, glowMul: 1.7, scroll: 1.25,
  },
  {
    name: 'acid-rush', event: 'fast-accel', minLen: 120, maxLen: 190, bold: 2,
    steer: { yaw: 0.016, pitch: 0.004, roll: 0.008, wind: 0.022, windFreq: 0.04, wobble: 0.012 },
    speed: { target: 50, accel: 1.9 },
    palette: { bg1: hex(0x0a1f0d), bg2: hex(0x04120a), line: hex(0x9bff3c), spiral: hex(0x38f5c0), fog: hex(0x081a0c) },
    fogDensity: 0.020, glowMul: 1.5, scroll: 1.4,
  },
  {
    // Was a full vertical loop (pitch 0.070): a 360 closes back through its own entry and the
    // tube folds into itself. Now a helical dive — strong pitch UNDER the curvature cap plus
    // real yaw drift so successive coils never occupy the same space.
    name: 'loop-under', event: 'helix-dive', minLen: 90, maxLen: 130, bold: 3,
    steer: { yaw: 0.020, pitch: 0.034, roll: 0.006, wind: 0.008, windFreq: 0.03, wobble: 0.006 },
    speed: { target: 30, accel: 1.1 },
    palette: { bg1: hex(0x241a06), bg2: hex(0x120c04), line: hex(0xffcf5f), spiral: hex(0xb06bff), fog: hex(0x1c1406) },
    fogDensity: 0.020, glowMul: 1.25, scroll: 0.7,
  },
];

const TRANSITION = 34;   // world units of palette/speed crossfade before a boundary

export class Zones {
  constructor() {
    this._sched = [];         // [{ z, s0, s1 }]
    this._hint = { depth: 1, intensity: 0 };
    this._lastCrossed = -1;   // index of the last boundary we fired a whoosh for
    // Always open on the signature pink descent so the plunge reads on-brand.
    this._sched.push({ z: ZONES[0], s0: 0, s1: 110 });
  }

  setHint(depth, intensity) {
    this._hint.depth = Math.max(1, depth || 1);
    this._hint.intensity = Math.min(1, Math.max(0, intensity || 0));
  }

  /** Make sure zones are scheduled to cover arc length up to sMax. */
  ensure(sMax) {
    while (this._sched.length === 0 || this._sched[this._sched.length - 1].s1 < sMax + 1) {
      const prev = this._sched[this._sched.length - 1];
      const z = this._pick(prev ? prev.z.name : null);
      const len = z.minLen + Math.random() * (z.maxLen - z.minLen);
      const s0 = prev ? prev.s1 : 0;
      this._sched.push({ z, s0, s1: s0 + len });
    }
  }

  _pick(prevName) {
    // Bolder zones become likelier as depth/intensity climb; never repeat the
    // zone we just left.
    const boldPref = Math.min(3, (this._hint.depth - 1) * 0.5 + this._hint.intensity * 2.5);
    let total = 0;
    const weighted = [];
    for (const z of ZONES) {
      if (z.name === prevName) continue;
      // triangular affinity around the preferred boldness, floored so nothing is impossible
      const w = 0.35 + Math.max(0, 1.4 - Math.abs(z.bold - boldPref));
      weighted.push({ z, w });
      total += w;
    }
    let pick = Math.random() * total;
    for (const e of weighted) { pick -= e.w; if (pick <= 0) return e.z; }
    return weighted[weighted.length - 1].z;
  }

  _indexAt(s) {
    this.ensure(s + 1);
    for (let i = 0; i < this._sched.length; i++) {
      if (s < this._sched[i].s1) return i;
    }
    return this._sched.length - 1;
  }

  /** The zone used to GENERATE geometry at arc length s (crisp, not blended). */
  steeringAt(s) { return this._sched[this._indexAt(s)].z; }

  /** Blended render params (palette/fog/glow/scroll) at the camera arc length. */
  render(camS) {
    const i = this._indexAt(camS);
    const cur = this._sched[i];
    let a = cur.z, b = cur.z, t = 0;
    const distToEnd = cur.s1 - camS;
    if (distToEnd < TRANSITION && i + 1 < this._sched.length) {
      b = this._sched[i + 1].z;
      t = smooth(1 - distToEnd / TRANSITION);
    }
    return {
      bg1: lerpCol(a.palette.bg1, b.palette.bg1, t),
      bg2: lerpCol(a.palette.bg2, b.palette.bg2, t),
      line: lerpCol(a.palette.line, b.palette.line, t),
      spiral: lerpCol(a.palette.spiral, b.palette.spiral, t),
      fog: lerpCol(a.palette.fog, b.palette.fog, t),
      fogDensity: lerp(a.fogDensity, b.fogDensity, t),
      glowMul: lerp(a.glowMul, b.glowMul, t),
      scroll: lerp(a.scroll, b.scroll, t),
    };
  }

  /** Blended speed target/accel at the camera arc length. */
  speed(camS) {
    const i = this._indexAt(camS);
    const cur = this._sched[i];
    let a = cur.z, b = cur.z, t = 0;
    const distToEnd = cur.s1 - camS;
    if (distToEnd < TRANSITION && i + 1 < this._sched.length) {
      b = this._sched[i + 1].z;
      t = smooth(1 - distToEnd / TRANSITION);
    }
    return { target: lerp(a.speed.target, b.speed.target, t), accel: lerp(a.speed.accel, b.speed.accel, t) };
  }

  /** True exactly once when the camera crosses into a new zone (for the whoosh). */
  crossed(camS) {
    const i = this._indexAt(camS);
    if (i > this._lastCrossed) { this._lastCrossed = i; return i > 0; }
    return false;
  }

  /** Drop scheduled zones fully behind the camera to bound memory. */
  trim(camS) {
    let drop = 0;
    while (drop < this._sched.length - 1 && this._sched[drop].s1 < camS - 200) drop++;
    if (drop > 0) { this._sched.splice(0, drop); this._lastCrossed -= drop; }
  }

  currentName(camS) { return this._sched[this._indexAt(camS)].z.name; }
}
