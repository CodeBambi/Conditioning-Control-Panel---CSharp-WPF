/* ============================================================================
 * games/impulse-control/tube2d.js - the Drop Tube without WebGL.
 *
 * Same contract as tube3d.js, drawn on a 2D canvas: an opaque spiral ribbon
 * winding into a concave basin, a soft glow travelling the path, ring pulses on
 * reveal/pop. The 3D tier's living-material pass has a proportionate echo here:
 * the whole spiral slowly ROTATES (hypnotic by design), a NEON pass marches
 * along the ribbon toward the basin, good pops burst 2D particles, a denyHit
 * jolts the spin, flashes the ribbon red and briefly reverses the march, and
 * setMood() pushes the pattern journey and quickens the hue cycle with the
 * streak while speeding both motions up with class progress.
 *
 * THE PATTERN IS THE LIGHT, here too (House Rules pass, parity in spirit with
 * tube3d.js): the ribbon body stays dark resin; the decoration riding it is
 * bright saturated palette colour with a white-hot core. The same three
 * FAMILIES exist at this tier's fidelity, every one of them built from dash
 * trains on the ribbon's centre line and its two shoulders (one Path2D each,
 * traced once per frame; Path2D absent = traced per stroke):
 *   VEIN   a coloured dash train (rings / candy = long dashes / zigzag = the
 *          train alternates shoulders / lace + runes = a bead train beside it /
 *          eyes = big beads with a white pupil)
 *   FIELD  dot trains on all three lines (polka = solid, bubbles = a dark dot
 *          punched into each, sparkle = small dots with a twinkle)
 *   GRID   staggered dash trains on the shoulders (fishnet = thin + a hairline
 *          spine, diamond = thick + knots, checker = on == off blocks with the
 *          two shoulders in opposite phase)
 * The journey (4-6 stops, at least one FIELD/GRID, progress walks it, streak
 * milestones push it, a broken streak pulls it back) and the palettes (seeded
 * bold triads/quads, ~28% the classic violet->rose pair made bright) follow the
 * 3D tier's rules. Within a family the parameters lerp; across families the
 * two family passes crossfade by alpha over a seeded 1.8-2.8s. The hue cycle is
 * a per-frame hue offset on the palette (bold palettes spin, the classic pair
 * sways); colours are hsla() strings, so no colour math runs per frame.
 *
 * It carries the 3D tier's composition law too (see tube3d.js' header):
 *   MOUTH  the outer end is sized OFF-CANVAS from the viewport itself, so the
 *          ribbon can never be caught ending in frame at any window shape.
 *   BASIN  the inner end HOLDS at the rim radius for the last stretch, then
 *          PEELS off it and curls INWARD to end as a small open MOUTH facing
 *          the crater's centre - the ribbon delivers, it does not just stop.
 *          It still never spirals into the centre: the opening halts short of
 *          it, the crater is what lives there, and the DOM bubble nests in it.
 *
 * SEEDED, like the 3D tier: the class seed picks this ribbon's hue pair, its
 * spin direction, its dash temperament, the period of a small COMET that
 * forever patrols the ribbon mouth->basin (dimming while a real load travels),
 * and - appended after those draws, so every ribbon keeps its Semester-1 base
 * - its palette, cycle and journey. Same seed, same ribbon.
 *
 * When even a 2D context is unavailable (the DOM double, a hostile webview) it
 * degrades once more to a STATIC conic-gradient div - every method stays
 * callable and silent - and that div at least wears the seed's palette as
 * inline spokes over style.js's dark conic base.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

const PIXEL = 3;           // CSS px per rendered texel - the arcade-cabinet
                           // pass (owner 2026-08-24): the ribbon renders at
                           // 1/PIXEL and the stylesheet's pixelated upscale
                           // makes it chunky; matches the 3D tier's constant
const HOLD = 0.86;         // t where the spiral stops shrinking - matches 3D
const SQUASH = 0.8;        // the ground plane's foreshortening at this camera
const RIM_F = 0.20;        // basin rim radius as a fraction of min(W,H)
const BORE_F = 0.80;       // ribbon width as a fraction of the rim radius -
                           // the same bore/rim ratio the 3D tier is built on
                           // (TUBE_R 1.0 / R_IN 2.5; was 0.64 for the 0.8 bore)
const GAP_F = 1.5;         // minimum coil pitch, in ribbon widths
const OUT_F = 1.35;        // how far past the frame corner the mouth reaches

/* THE INNER MOUTH - the 2D echo of the 3D tier's peel. Same shape, same reason:
   a ribbon that levels into the rim and stops delivers nothing, so the last
   sliver turns inward and ends pointing at the middle. Added to the run, not
   carved out of the hold, so the ribbon still paints over the same arc of lit
   rim it always did. Values are the 3D constants divided by R_IN. */
const MOUTH_T = 0.965;     // t where the peel leaves the held ring
const MOUTH_SWEEP = 0.55;  // radians the peel carries around the rim
const MOUTH_F = 0.68;      // the opening's radius, as a fraction of RIM
const MOUTH_L0 = 0.52;     // Hermite tangent lengths (peel-off / aim-in), in RIM
const MOUTH_L1 = 0.50;
const MOUTH_DIP = 0.10;    // screen-down nudge at the opening, in RIM: the 3D
                           // mouth rides lower than its ring, and `lift` is this
                           // projection's convention for exactly that

const SPIN_BASE = 0.07;    // rad/s - matches the 3D tier
const SPIN_MAX = 0.17;
const FLOW_BASE = 30;      // dash-march px/s at SCALE 1
const FLOW_MAX = 85;

/* THE PATTERN SPACE at this tier: dash trains. on/off are in ribbon widths;
   shoulder 0 = centre line only, 1 = both shoulders, alternate = the colours
   take turns between shoulders; beads = a dot train beside the main one. */
const ARCHETYPES = {
  rings:   { fam: 'vein', on: 0.30, off: 0.70, w: 0.20, shoulder: 0, alt: 0, bead: 0,    eye: 0 },
  chevron: { fam: 'vein', on: 0.34, off: 0.66, w: 0.18, shoulder: 1, alt: 0, bead: 0,    eye: 0 },
  lace:    { fam: 'vein', on: 0.22, off: 0.78, w: 0.14, shoulder: 0, alt: 0, bead: 0.16, eye: 0 },
  waves:   { fam: 'vein', on: 0.40, off: 0.60, w: 0.16, shoulder: 1, alt: 1, bead: 0,    eye: 0 },
  eyes:    { fam: 'vein', on: 0.16, off: 0.84, w: 0.12, shoulder: 0, alt: 0, bead: 0,    eye: 0.34 },
  runes:   { fam: 'vein', on: 0.18, off: 0.50, w: 0.16, shoulder: 0, alt: 0, bead: 0.10, eye: 0 },
  candy:   { fam: 'vein', on: 0.85, off: 0.85, w: 0.30, shoulder: 0, alt: 0, bead: 0,    eye: 0 },
  zigzag:  { fam: 'vein', on: 0.26, off: 0.52, w: 0.15, shoulder: 1, alt: 1, bead: 0,    eye: 0 },
  polka:   { fam: 'field', dot: 0.26, gap: 0.9, hollow: 0, twinkle: 0 },
  bubbles: { fam: 'field', dot: 0.24, gap: 1.0, hollow: 1, twinkle: 0 },
  sparkle: { fam: 'field', dot: 0.13, gap: 0.6, hollow: 0, twinkle: 1 },
  fishnet: { fam: 'grid', on: 0.35, off: 0.35, w: 0.07, knot: 0,    spine: 1 },
  diamond: { fam: 'grid', on: 0.45, off: 0.45, w: 0.12, knot: 0.14, spine: 0 },
  checker: { fam: 'grid', on: 0.60, off: 0.60, w: 0.30, knot: 0,    spine: 0 },
};
const FAM_KEYS = {
  vein:  ['on', 'off', 'w', 'shoulder', 'alt', 'bead', 'eye'],
  field: ['dot', 'gap', 'hollow', 'twinkle'],
  grid:  ['on', 'off', 'w', 'knot', 'spine'],
};
/* the same bold pool as tube3d.js ([hue, sat, light]); index 0 leads */
const PALETTES = [
  { name: 'neon',   cols: [[312, 1, 0.58], [186, 1, 0.56], [84, 1, 0.58]] },
  { name: 'royal',  cols: [[46, 1, 0.60], [268, 1, 0.66], [0, 0, 0.96]] },
  { name: 'sunset', cols: [[338, 1, 0.62], [168, 0.9, 0.56], [36, 1, 0.60]] },
  { name: 'candy',  cols: [[326, 1, 0.60], [222, 1, 0.62], [0, 0, 0.95]] },
  { name: 'acid',   cols: [[76, 1, 0.55], [276, 1, 0.62], [336, 1, 0.58]] },
  { name: 'ice',    cols: [[188, 1, 0.66], [262, 1, 0.80], [0, 0, 0.97]] },
  { name: 'ember',  cols: [[22, 1, 0.58], [46, 1, 0.60], [312, 1, 0.58]] },
  { name: 'ultra',  cols: [[282, 1, 0.62], [150, 1, 0.55], [52, 1, 0.62], [196, 1, 0.60]] },
];
const ARC_CHANCE = 0.28;

export function createTube2D(opts = {}) {
  const mount = opts.mount;
  const reduced = !!opts.reduced;

  /* the ribbon's seeded identity - mirrors the 3D tier's ranges. Draws in a
     FIXED order: the Semester-1 six first, the House Rules draws appended. */
  const rng = makeRng(String(opts.seed == null ? 'tube' : opts.seed) + '|tube2d');
  const ID = (() => {
    const hue = 285 + rng() * 55;
    const hue2 = Math.max(262, Math.min(345, hue + (rng() < 0.5 ? -1 : 1) * (18 + rng() * 22)));
    const spinSign = rng() < 0.3 ? -1 : 1;
    const dashOn = 0.24 + rng() * 0.2;            // legacy temperament (scales on/off)
    const dashOff = 0.5 + rng() * 0.35;
    const cometPeriod = 7 + rng() * 4;
    /* ---- APPENDED (House Rules pass) ---- */
    let palette, cycleMode;
    if (rng() < ARC_CHANCE) {
      palette = [[hue, 1, 0.66], [hue2, 1, 0.70], [0, 0, 0.96]]; cycleMode = 'sway';
    } else {
      const p = PALETTES[Math.min(PALETTES.length - 1, Math.floor(rng() * PALETTES.length))];
      palette = p.cols.map((c) => c.slice()); cycleMode = 'spin';
    }
    const rot = Math.floor(rng() * palette.length) % palette.length;
    palette = palette.slice(rot).concat(palette.slice(0, rot));
    const cycleBase = 6.2832 / (45 + rng() * 40);
    const cycleSign = rng() < 0.5 ? -1 : 1;
    const nStops = 4 + Math.floor(rng() * 3);
    const pool = Object.keys(ARCHETYPES).sort();
    for (let i = pool.length - 1; i > 0; i--) {
      const j = Math.min(i, Math.floor(rng() * (i + 1)));
      const sw = pool[i]; pool[i] = pool[j]; pool[j] = sw;
    }
    const picked = pool.slice(0, nStops);
    if (!picked.some((k) => ARCHETYPES[k].fam !== 'vein')) {
      const alt = pool.slice(nStops).find((k) => ARCHETYPES[k].fam !== 'vein');
      if (alt) picked[nStops - 1] = alt;
    }
    const tempo = (dashOn + dashOff) / 0.975;     // the legacy temperament, ~0.76..1.1
    const stops = picked.map((k) => {
      const base = ARCHETYPES[k];
      const out = { key: k, fam: base.fam };
      for (const f of FAM_KEYS[base.fam]) out[f] = base[f];
      const j1 = rng(), j2 = rng();
      if (base.fam === 'vein' || base.fam === 'grid') {
        out.on *= tempo * (0.85 + j1 * 0.3);
        out.off *= tempo * (0.85 + j2 * 0.3);
      } else {
        out.dot *= 0.85 + j1 * 0.3;
        out.gap *= tempo * (0.85 + j2 * 0.3);
      }
      return out;
    });
    const morphT = 4.5 + rng() * 2.5;
    const xfadeT = 1.8 + rng() * 1.0;
    return { spinSign, cometPeriod, palette, cycleMode, cycleBase, cycleSign, stops, morphT, xfadeT };
  })();
  const PAL = ID.palette, PAL_N = PAL.length;

  /* ------------------------------------------------ static (last resort) */
  function staticTube() {
    try {
      if (mount && typeof document !== 'undefined' && document.createElement) {
        const div = document.createElement('div');
        div.className = 'g-ic-tube-static';
        /* the palette at least: thin bright spokes over the dark conic base
           style.js paints (inline beats the stylesheet; the mask stays) */
        try {
          const spoke = (c) => 'hsl(' + Math.round(c[0]) + ',' + Math.round(c[1] * 100) + '%,' + Math.round(c[2] * 100) + '%)';
          let conic = 'repeating-conic-gradient(from 0deg';
          const step = 36 / PAL_N;
          for (let k = 0; k < PAL_N; k++) {
            const a0 = k * step;
            conic += ', ' + spoke(PAL[k]) + ' ' + a0.toFixed(1) + 'deg ' + (a0 + 2.2).toFixed(1) + 'deg'
              + ', rgba(30,30,58,.9) ' + (a0 + 2.2).toFixed(1) + 'deg ' + (a0 + step).toFixed(1) + 'deg';
          }
          conic += ')';
          div.style.background = 'radial-gradient(circle at 50% 50%, rgba(8,8,22,.95) 0 12%, transparent 13%), ' + conic;
        } catch (e) { /* the stylesheet's base look is fine */ }
        mount.appendChild(div);
      }
    } catch (e) { /* noop */ }
    const noop = () => {};
    return {
      kind: 'static',
      setTravel: noop, loadPulse: noop, reveal: noop, pop: noop,
      denyHit: noop, denyPass: noop, setMood: noop,
      suspend: noop, resize: noop, destroy: noop,
    };
  }

  let canvas = null, g = null;
  try {
    canvas = document.createElement('canvas');
    canvas.className = 'g-ic-tube-canvas';
    g = canvas.getContext && canvas.getContext('2d');
    if (!g) return staticTube();
    mount.appendChild(canvas);
  } catch (e) { return staticTube(); }
  const HAS_P2D = (typeof Path2D === 'function');

  /* Ghost sprite (best effort). */
  let ghostImg = null;
  try {
    if (typeof Image === 'function') {
      const im = new Image();
      im.onload = () => { ghostImg = im; };
      im.src = new URL('./assets/bubble.png', import.meta.url).href;
    }
  } catch (e) { /* glow-only */ }

  let W = 800, H = 600, CX = 400, CY = 300, SCALE = 1;
  let RIM = 120, BORE = 76, ROUT = 900, TURNS = 3.5;
  function resize() {
    try {
      W = mount.clientWidth || 800; H = mount.clientHeight || 600;
      /* the arcade-cabinet pass, this tier's cut: a 1/PIXEL backing store
         nearest-upscaled by the stylesheet (was a 2x-dpr crisp render) */
      canvas.width = Math.max(1, Math.round(W / PIXEL));
      canvas.height = Math.max(1, Math.round(H / PIXEL));
      canvas.style.width = W + 'px'; canvas.style.height = H + 'px';
      g.setTransform(1 / PIXEL, 0, 0, 1 / PIXEL, 0, 0);
      g.imageSmoothingEnabled = false;    // the ghost sprite crunches too
      /* dead centre, NOT nudged down: the DOM bubble is pinned to 50%/50% and
         the crater has to be the thing it lands in */
      CX = W / 2; CY = H / 2;
      SCALE = Math.min(W, H) * 0.0088;
      /* THE WHOLE COMPOSITION IS DERIVED FROM THE VIEWPORT, not from SCALE.
         The mouth has to clear the frame's widest half-axis - W/2 across, and
         H/(2*SQUASH) up the squashed vertical - on EVERY window shape, or the
         ribbon is caught ending in frame the way the 3D tube used to be. */
      RIM = Math.min(W, H) * RIM_F;
      BORE = RIM * BORE_F;
      ROUT = Math.max(W * 0.5, H / (2 * SQUASH)) * OUT_F;
      /* turns then follow from the span, so coils never crowd into each other
         on a squarer window (where ROUT is relatively closer to RIM) */
      TURNS = Math.max(2.2, Math.min(5.5, (ROUT - RIM) / (BORE * GAP_F)));
    } catch (e) { /* noop */ }
  }

  /* The flattened helix: same parametrisation as 3d, projected with a squash.
     Radius falls LINEARLY to the rim then HOLDS, so the last stretch is a level
     ring around the basin instead of a dive into the centre. `spin` rotates the
     whole spiral; the ghost uses the same at() so the load stays sealed to the
     ribbon under rotation. `dr` offsets the radius - the ribbon's shoulders. */
  let spin = 0;
  function at(t, dr) {
    dr = dr || 0;
    const aEnd = -Math.PI * 0.5 + spin + TURNS * Math.PI * 2;
    if (t > MOUTH_T) {
      /* the peel: one cubic Hermite from the ring's pose (position + its own
         tangent, so no kink) to the opening's pose (just inside the rim, tangent
         pointing dead at the centre). The s->u ease keeps the ribbon's speed
         continuous across the seam and lets the load settle into the mouth.
         The shoulders converge into the opening (dr fades out over the peel). */
      const s = Math.min(1, (t - MOUTH_T) / (1 - MOUTH_T));
      const u = s * (2 - s);
      const u2 = u * u, u3 = u2 * u;
      const h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u;
      const h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
      const aM = aEnd + MOUTH_SWEEP;
      const cE = Math.cos(aEnd), sE = Math.sin(aEnd);
      const cM = Math.cos(aM), sM = Math.sin(aM);
      const R0 = RIM + dr * (1 - u);
      const px = h00 * (cE * R0) + h10 * (-sE * RIM * MOUTH_L0)
               + h01 * (cM * RIM * MOUTH_F) + h11 * (-cM * RIM * MOUTH_L1);
      const py = h00 * (sE * R0) + h10 * (cE * RIM * MOUTH_L0)
               + h01 * (sM * RIM * MOUTH_F) + h11 * (-sM * RIM * MOUTH_L1);
      return { x: CX + px, y: CY + py * SQUASH + RIM * MOUTH_DIP * u };
    }
    const tt = t / MOUTH_T;
    const a = -Math.PI * 0.5 + spin + tt * TURNS * Math.PI * 2;
    const k = Math.min(1, tt / HOLD);
    const r = RIM + (ROUT - RIM) * (1 - k) + dr;
    const lift = Math.pow(1 - k, 1.8) * RIM * 0.55;   // the outer coils ride high
    return { x: CX + Math.cos(a) * r, y: CY + Math.sin(a) * r * SQUASH - lift };
  }

  /* the ribbon's three lines, traced ONCE per frame (Path2D) and stroked as
     many times as the pattern needs. shoulder k = -1 / 0 / +1. */
  const pathCache = [null, null, null];
  function trace(target, k) {
    const dr = k * BORE * 0.27;
    for (let i = 0; i <= 220; i++) {
      const q = at(i / 220, dr);
      if (i === 0) target.moveTo(q.x, q.y); else target.lineTo(q.x, q.y);
    }
  }
  function strokeLine(k) {
    if (HAS_P2D) {
      let p = pathCache[k + 1];
      if (!p) { p = new Path2D(); trace(p, k); pathCache[k + 1] = p; }
      g.stroke(p);
    } else {
      g.beginPath(); trace(g, k); g.stroke();
    }
  }

  let travel = null, rimBoost = 0, mouthBoost = 0, clock = 0;
  const pulses = [];
  let suspended = false, dead = false;

  /* living-material state */
  let spinVel = reduced ? 0 : SPIN_BASE;
  let spinKick = 0;
  let flowSpeed = reduced ? 0 : FLOW_BASE;
  let flowDir = 1, flowRevT = 0, flowOff = 0;
  let redFlash = 0;
  let shakeT = 0;
  let mood = { progress: 0, streak: 0 };
  const parts = [];        // {x,y,vx,vy,t}

  let cometT = rng();      // start mid-patrol, seeded
  let cometLevel = 0;

  /* the journey walk + the hue wheel (same rules as tube3d.js) */
  const STOPS_N = ID.stops.length;
  let jPos = 0, jTarget = 0, advance = 0, milestone = 0, lastStreak = 0;
  let hueAng = 0, swayPh = 0;   // degrees; swayPh = the sway's own phase
  const P_CUR = {};
  const smooth = (u) => (u <= 0 ? 0 : u >= 1 ? 1 : u * u * (3 - 2 * u));
  function retarget() {
    const raw = mood.progress * (STOPS_N - 1) + advance;
    jTarget = Math.max(0, Math.min(STOPS_N - 1, Math.round(raw)));
  }

  /* colours: hsla strings off the palette + the hue offset; gold on a hot
     streak and red on a denyHit are semantic overrides, like the 3D tier */
  function ink(k, alpha) {
    if (redFlash > 0.12) return 'rgba(255,64,96,' + alpha + ')';
    const c = PAL[((k % PAL_N) + PAL_N) % PAL_N];
    const h = (((c[0] + hueAng) % 360) + 360) % 360;
    return 'hsla(' + h.toFixed(0) + ',' + Math.round(c[1] * 100) + '%,' + Math.round(c[2] * 100) + '%,' + alpha + ')';
  }
  /* the white-hot cores turn GOLD on a hot streak - the 2D echo of the 3D
     tier's gold tint; the palette colours themselves stay honest */
  const WHITE_HOT = (a) => (mood.streak >= 5 ? 'rgba(255,214,90,' : 'rgba(255,255,255,') + a + ')';
  function litColor(alpha) { return ink(0, alpha); }

  /* ------------------------------------------------ the pattern painters
     Every painter strokes dash trains over the ribbon lines. period = on+off
     in px; a train of PAL_N colours is PAL_N strokes with the dash phase
     stepped by one period each, so the colours take turns along the line. */
  function dashTrain(k, onPx, offPx, width, alpha, phase, whiteCore) {
    const period = onPx + offPx;
    const full = period * PAL_N;
    for (let c = 0; c < PAL_N; c++) {
      g.setLineDash([onPx, full - onPx]);
      g.lineDashOffset = flowOff + phase + c * period;
      g.strokeStyle = ink(c, alpha); g.lineWidth = width;
      strokeLine(k);
    }
    if (whiteCore > 0) {
      g.setLineDash([onPx, offPx]);
      g.lineDashOffset = flowOff + phase;
      g.strokeStyle = WHITE_HOT(alpha * whiteCore); g.lineWidth = Math.max(1, width * 0.34);
      strokeLine(k);
    }
    g.setLineDash([]);
  }
  function dotTrain(k, radPx, gapPx, alpha, phase, hollow, twinkle, colShift) {
    const period = radPx * 2 + gapPx;
    const full = period * PAL_N;
    for (let c = 0; c < PAL_N; c++) {
      const tw = twinkle > 0 ? 1 - twinkle * 0.5 * (0.5 + 0.5 * Math.sin(clock * 5 + c * 2.1 + k)) : 1;
      g.setLineDash([0.01, full - 0.01]);
      g.lineDashOffset = flowOff + phase + c * period;
      g.strokeStyle = ink(c + colShift, alpha * tw); g.lineWidth = radPx * 2;
      strokeLine(k);
      if (hollow > 0.01) {
        g.strokeStyle = 'rgba(24,24,50,' + (alpha * hollow) + ')'; g.lineWidth = radPx * 1.1;
        strokeLine(k);
      }
      g.strokeStyle = WHITE_HOT(alpha * tw * 0.85 * (1 - hollow * 0.6)); g.lineWidth = Math.max(1, radPx * 0.5);
      strokeLine(k);
    }
    g.setLineDash([]);
  }
  function paintVein(P, a) {
    const on = BORE * P.on, off = BORE * P.off, w = BORE * P.w + 1.5;
    if (P.shoulder < 0.5) {
      dashTrain(0, on, off, w, a, 0, 0.8);
    } else if (P.alt > 0.5) {
      /* the train alternates shoulders: colour c rides +1 when c is even */
      const period = on + off, full = period * PAL_N;
      for (let c = 0; c < PAL_N; c++) {
        g.setLineDash([on, full - on]); g.lineDashOffset = flowOff + c * period;
        g.strokeStyle = ink(c, a); g.lineWidth = w;
        strokeLine((c & 1) ? -1 : 1);
        g.strokeStyle = WHITE_HOT(a * 0.7); g.lineWidth = Math.max(1, w * 0.34);
        strokeLine((c & 1) ? -1 : 1);
      }
      g.setLineDash([]);
    } else {
      dashTrain(1, on, off, w, a, 0, 0.7);
      dashTrain(-1, on, off, w, a, (on + off) * 0.5, 0.7);
    }
    if (P.bead > 0.01) dotTrain(1, BORE * P.bead * 0.5 + 1, on + off - BORE * P.bead, a, (on + off) * 0.5, 0, 0, 2);
    if (P.eye > 0.01) dotTrain(0, BORE * P.eye * 0.5, on + off, a, on * 0.5, 0, 0, 1);
  }
  function paintField(P, a) {
    const rad = BORE * P.dot * 0.5 + 1, gap = BORE * P.gap;
    const period = rad * 2 + gap;
    dotTrain(0, rad, gap, a, 0, P.hollow, P.twinkle, 0);
    dotTrain(1, rad * 0.85, gap, a, period * 0.5, P.hollow, P.twinkle, 1);
    dotTrain(-1, rad * 0.85, gap, a, period * 0.5, P.hollow, P.twinkle, 2);
  }
  function paintGrid(P, a) {
    const on = BORE * P.on, off = BORE * P.off, w = BORE * P.w + 1;
    dashTrain(1, on, off, w, a, 0, P.w > 0.1 ? 0.6 : 0);
    dashTrain(-1, on, off, w, a, on, P.w > 0.1 ? 0.6 : 0);
    if (P.spine > 0.01) {
      g.strokeStyle = ink(2, a * P.spine * 0.7); g.lineWidth = Math.max(1, w * 0.6);
      strokeLine(0);
    }
    if (P.knot > 0.01) dotTrain(0, BORE * P.knot * 0.5 + 1, on + off - BORE * P.knot, a, on * 0.5, 0, 0, 2);
  }
  function paintStop(S, P, a) {
    if (S.fam === 'vein') paintVein(P, a);
    else if (S.fam === 'field') paintField(P, a);
    else paintGrid(P, a);
  }
  function paintPattern(alphaBase) {
    const n = STOPS_N;
    let i = Math.floor(jPos);
    if (i >= n - 1) i = n - 2;
    if (i < 0) i = 0;
    const A = ID.stops[i], B = ID.stops[Math.min(n - 1, i + 1)];
    const ws = n > 1 ? smooth(Math.max(0, Math.min(1, jPos - i))) : 0;
    if (A.fam === B.fam) {
      for (const f of FAM_KEYS[A.fam]) P_CUR[f] = A[f] + (B[f] - A[f]) * ws;
      paintStop(A, P_CUR, alphaBase);
    } else {
      if (ws < 0.995) paintStop(A, A, alphaBase * (1 - ws));
      if (ws > 0.005) paintStop(B, B, alphaBase * ws);
    }
  }

  function draw(dt) {
    clock += dt;
    spinKick *= Math.pow(0.06, dt);
    spin += (spinVel + spinKick) * dt * ID.spinSign;
    if (flowRevT > 0) { flowRevT -= dt; if (flowRevT <= 0) flowDir = 1; }
    flowOff -= flowSpeed * SCALE * 0.12 * flowDir * dt;
    redFlash *= Math.pow(0.02, dt);
    shakeT = Math.max(0, shakeT - dt);
    pathCache[0] = pathCache[1] = pathCache[2] = null;   // the spin moved

    /* the journey glides toward its target stop at the pair's own pace */
    if (!reduced && STOPS_N > 1 && jPos !== jTarget) {
      let i = Math.floor(Math.min(jPos, jTarget));
      if (i >= STOPS_N - 1) i = STOPS_N - 2;
      if (i < 0) i = 0;
      const same = ID.stops[i].fam === ID.stops[Math.min(STOPS_N - 1, i + 1)].fam;
      const step = dt / (same ? ID.morphT : ID.xfadeT);
      const d = jTarget - jPos;
      jPos += Math.abs(d) <= step ? d : Math.sign(d) * step;
    }
    /* the hue wheel: bold palettes spin, the classic pair sways; the streak
       quickens both */
    if (!reduced) {
      const rate = ID.cycleBase * (1 + Math.min(3, mood.streak / 4)) * 57.2958;   // deg/s
      if (ID.cycleMode === 'sway') { swayPh += rate * 2.2 * dt / 57.2958; hueAng = 29 * Math.sin(swayPh * ID.cycleSign); }
      else hueAng = (hueAng + rate * ID.cycleSign * dt) % 360;
    }

    g.clearRect(0, 0, W, H);
    g.save();
    if (shakeT > 0) {
      const s = shakeT * 16;
      g.translate((Math.random() - 0.5) * s, (Math.random() - 0.5) * s);
    }

    /* THE CRATER, drawn FIRST so the chute's held ring lands on its lip. Dark
       at the belly, lit only at the rim - the same "hole, not dome" ramp the 3D
       dish bakes into its texture. */
    const bR = RIM;
    const dish = g.createRadialGradient(CX, CY - bR * 0.10, bR * 0.04, CX, CY, bR);
    dish.addColorStop(0, 'rgba(2,2,7,.99)');
    dish.addColorStop(0.55, 'rgba(9,9,26,.99)');
    dish.addColorStop(0.88, 'rgba(26,24,58,.99)');
    dish.addColorStop(1, 'rgba(64,58,112,.98)');
    g.beginPath(); g.ellipse(CX, CY, bR, bR * SQUASH, 0, 0, Math.PI * 2);
    g.fillStyle = dish; g.fill();

    /* the lit lip, BEFORE the chute: the held ring then paints over the arc of
       rim it covers, which is the occlusion the 3D tier gets for free */
    g.beginPath(); g.ellipse(CX, CY, bR, bR * SQUASH, 0, 0, Math.PI * 2);
    g.strokeStyle = litColor(0.55 + rimBoost * 0.4);
    g.lineWidth = BORE * 0.06 + 2 + rimBoost * 3; g.stroke();

    /* chute body: dark underlay pass then the resin, then a faint lit top
       edge - the centre line stroked three times with a vertical nudge */
    g.lineCap = 'round'; g.lineJoin = 'round';
    const passes = [
      { w: BORE * 1.14, c: 'rgba(16,16,36,.96)', dy: BORE * 0.04 },
      { w: BORE, c: 'rgba(36,36,74,.98)', dy: 0 },
      { w: BORE * 0.34, c: litColor(0.10 + redFlash * 0.4), dy: -BORE * 0.055 },
    ];
    for (const p of passes) {
      g.save(); g.translate(0, p.dy);
      g.strokeStyle = p.c; g.lineWidth = p.w;
      strokeLine(0);
      g.restore();
    }

    /* THE PATTERN: the neon riding the ribbon. mouthBoost is the load entering
       off-canvas, so it brightens the pattern rather than ringing a mouth
       nobody can see any more. */
    paintPattern(0.82 + Math.sin(clock * 0.9) * 0.08 + redFlash * 0.2 + mouthBoost * 0.2);

    /* THE OPENING. Drawn AFTER the ribbon and the pattern, so both stop at it
       rather than running over it: a dark bore with one thin lit lip. The 3D
       tier gets this from a recessed cone; here a filled ellipse is the whole
       trick, and it is enough because the ribbon around it is already opaque. */
    {
      const m = at(1);
      /* SMALL and QUIET. A wide bore eats the ribbon's end and a bright lip
         turns the pair into a floating eye sitting on the basin - the 2D
         version of the blade the 3D tier's old end-cap was. It only has to be
         a dark spot with an edge; the opaque ribbon around it does the rest. */
      const br = BORE * 0.32;
      g.beginPath();
      g.ellipse(m.x, m.y, br, br * SQUASH, 0, 0, Math.PI * 2);
      g.fillStyle = 'rgba(5,5,16,.96)'; g.fill();
      /* two strokes, same division of labour as the 3D mouth: an UNLIT ring of
         pipe-wall that carries the shape, then a hairline of the lit accent on
         top. One accent-coloured stroke on its own reads as a neon eye. */
      g.strokeStyle = 'rgba(59,54,99,.95)';
      g.lineWidth = Math.max(1.5, BORE * 0.06); g.stroke();
      g.strokeStyle = litColor(0.16 + rimBoost * 0.18);
      g.lineWidth = Math.max(1, BORE * 0.02); g.stroke();
    }

    /* the comet: a short bright breath forever patrolling mouth->basin, dimmed
       while a real load travels (the load is the star) - the 2D echo of the 3D
       tier's patrol light */
    if (!reduced) {
      cometT += dt / ID.cometPeriod;
      if (cometT >= 1) cometT -= 1;
      cometLevel += (((travel == null) ? 0.55 : 0.15) - cometLevel) * Math.min(1, dt * 3);
      if (cometLevel > 0.03) {
        const SPAN = 0.045;
        const t0 = Math.min(1 - SPAN, cometT);
        g.beginPath();
        for (let i = 0; i <= 8; i++) {
          const q = at(t0 + (i / 8) * SPAN);
          if (i === 0) g.moveTo(q.x, q.y); else g.lineTo(q.x, q.y);
        }
        g.strokeStyle = litColor(cometLevel * 0.16);
        g.lineWidth = BORE * 0.66; g.stroke();
        g.strokeStyle = litColor(cometLevel * 0.5);
        g.lineWidth = BORE * 0.24; g.stroke();
      }
    }

    /* travelling ghost - sealed to the ribbon: soft glow first, the faintest
       bubble hint under it, both smaller than the ribbon's width */
    if (travel != null) {
      const q = at(Math.max(0, Math.min(1, travel)));
      const shim = reduced ? 0 : Math.sin(clock * 9) * 0.08;
      const halo = BORE * 0.85;
      const gl = g.createRadialGradient(q.x, q.y, 0, q.x, q.y, halo);
      gl.addColorStop(0, 'rgba(255,158,210,' + (0.5 + shim) + ')');
      gl.addColorStop(1, 'rgba(255,158,210,0)');
      g.beginPath(); g.arc(q.x, q.y, halo, 0, Math.PI * 2);
      g.fillStyle = gl; g.fill();
      if (ghostImg) {
        g.globalAlpha = 0.13 + shim * 0.5;
        const s = BORE * 0.72;   // INSIDE the bore, same law as the 3D ghost
        try { g.drawImage(ghostImg, q.x - s / 2, q.y - s / 2, s, s); } catch (e) { /* noop */ }
        g.globalAlpha = 1;
      }
    }

    /* pop particles */
    for (let i = parts.length - 1; i >= 0; i--) {
      const p = parts[i];
      p.t += dt;
      const k = p.t / 0.9;
      if (k >= 1) { parts.splice(i, 1); continue; }
      p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 130 * SCALE * 0.12 * dt;
      g.beginPath(); g.arc(p.x, p.y, (3.4 * SCALE * 0.35 + 1.6) * (1 - k), 0, Math.PI * 2);
      g.fillStyle = p.c.replace('%A%', String(0.85 * (1 - k)));
      g.fill();
    }

    /* pulses */
    for (let i = pulses.length - 1; i >= 0; i--) {
      const p = pulses[i];
      p.t += dt;
      const k = Math.min(1, p.t / p.dur);
      g.beginPath();
      g.ellipse(CX, CY, bR * (0.55 + k * 2.2), bR * SQUASH * (0.55 + k * 2.2), 0, 0, Math.PI * 2);
      g.strokeStyle = p.c.replace('%A%', String(0.8 * (1 - k)));
      g.lineWidth = 3.5 * (1 - k) + 0.5; g.stroke();
      if (k >= 1) pulses.splice(i, 1);
    }

    g.restore();
    rimBoost *= 0.9; mouthBoost *= 0.88;
  }

  function burst(gold) {
    if (reduced) return;
    const c = gold ? 'rgba(240,194,75,%A%)' : 'rgba(255,143,200,%A%)';
    for (let i = 0; i < 22; i++) {
      const a = Math.random() * Math.PI * 2;
      const v = (30 + Math.random() * 90) * SCALE * 0.12;
      parts.push({ x: CX, y: CY, vx: Math.cos(a) * v * 1.6, vy: -Math.abs(Math.sin(a)) * v * 2.2 - v, t: 0, c });
    }
  }

  let rafId = 0, last = 0;
  function loop(ts) {
    if (dead || suspended) return;
    const dt = last ? Math.min(0.05, (ts - last) / 1000) : 0.016;
    last = ts;
    try { draw(dt); } catch (e) { /* never kill the loop */ }
    const raf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame : null;
    if (raf) rafId = raf(loop);
  }
  function kick() {
    const raf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame : null;
    last = 0;
    if (raf) rafId = raf(loop); else { try { draw(0.016); } catch (e) { /* noop */ } }
  }
  resize();
  kick();

  return {
    kind: '2d',
    setTravel(p) { travel = (p == null ? null : Number(p) || 0); },
    loadPulse() { mouthBoost = 1.2; },
    reveal() { rimBoost = 1.5; travel = null; },
    pop(good) {
      pulses.push({ t: 0, dur: reduced ? 0.01 : 0.55, c: good ? 'rgba(255,105,180,%A%)' : 'rgba(240,194,75,%A%)' });
      rimBoost = 1;
      if (good) burst(mood.streak >= 5);
    },
    denyHit() {
      pulses.push({ t: 0, dur: reduced ? 0.01 : 0.5, c: 'rgba(255,64,96,%A%)' });
      rimBoost = 0.8;
      redFlash = 1;
      if (!reduced) { spinKick = 1.7; flowDir = -1; flowRevT = 0.7; shakeT = 0.3; }
    },
    denyPass() { pulses.push({ t: 0, dur: reduced ? 0.01 : 0.8, c: 'rgba(184,166,232,%A%)' }); },
    setMood(m) {
      try {
        mood = { progress: Math.max(0, Math.min(1, Number(m && m.progress) || 0)),
                 streak: Math.max(0, Math.round(Number(m && m.streak) || 0)) };
        if (!reduced) {
          spinVel = SPIN_BASE + (SPIN_MAX - SPIN_BASE) * mood.progress;
          flowSpeed = FLOW_BASE + (FLOW_MAX - FLOW_BASE) * mood.progress;
        }
        /* the streak pushes the journey (milestones 5/10/15/20 advance it 0.6
           of a stop; a chain of 4+ breaking to 0 pulls it 0.6 back) */
        const rung = mood.streak >= 20 ? 4 : mood.streak >= 15 ? 3 : mood.streak >= 10 ? 2 : mood.streak >= 5 ? 1 : 0;
        if (rung > milestone) {
          advance = Math.min(STOPS_N - 1, advance + 0.6 * (rung - milestone));
          milestone = rung;
        }
        if (mood.streak === 0 && lastStreak >= 4) { advance = Math.max(0, advance - 0.6); milestone = 0; }
        lastStreak = mood.streak;
        if (!reduced) retarget();
      } catch (e) { /* cosmetic */ }
    },
    suspend(on) {
      const want = !!on;
      if (want === suspended) return;
      suspended = want;
      if (!want && !dead) kick();
    },
    resize,
    destroy() {
      dead = true;
      try { if (rafId && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(rafId); } catch (e) { /* noop */ }
      try { if (canvas && canvas.parentNode) canvas.parentNode.removeChild(canvas); } catch (e) { /* noop */ }
    },
  };
}
