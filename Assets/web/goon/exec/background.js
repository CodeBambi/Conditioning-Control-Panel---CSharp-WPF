/* ============================================================================
 * exec/background.js - the living backdrop on #gg-bg (z0), 2026-09-24.
 *
 * WHAT IT IS. A single 2D canvas under every effect and the HUD: a dark room
 * glow in the flavour's hues, broad soft colour fields, a few luminous aurora
 * ribbons with travelling pearls, drifting motes, occasional bead clusters,
 * a vignette and a fine film grain. It is secondary by design: low contrast,
 * dark in the middle of the screen where the media lands, never a strobe.
 *
 * IT READS THE PLAYER'S HEAT. `window` CustomEvent `gg-heat` {detail:{heat}}
 * (0..1, ~10 Hz, sent by the scoring lane). Behind = calm: slow, few ribbons,
 * greyer, dim. Ahead / on a combo = more ribbons, pearls, motes and clusters,
 * warmer and brighter. Heat GLIDES (exponential approach), it never snaps.
 * Until the event exists the heat idles at DEFAULT_HEAT. Dev hooks:
 *   ?heat=0.8     pins the heat (events ignored)
 *   ?bgflavour=x  pins the palette (trance|pink|frills|shiny|censored|mine)
 *   ?bgdebug      a heat slider + a frame-cost readout, bottom left
 *   window.__ggBg { setHeat, setFlavour, stats }
 *
 * PORTED FROM AFTERIMAGE (C:/Projects/afterimage, private; copied as
 * self-contained code, nothing is loaded from there at runtime):
 *   - the "room": a radial gradient in the scene hues under everything
 *     (stage.mjs render()),
 *   - the pre-rendered glow ATLAS: 24 hues x rows, adjacent-hue blending, so a
 *     gliding palette never allocates a canvas per frame
 *     (luminous-atmosphere.mjs atlasFor/glow),
 *   - the veil / aurora ribbon family with its three-pass stroke (broad faint,
 *     mid, thin bright) and Catmull-Rom paths, open ends outside the viewport,
 *   - travelling beads with edge fades, the dust mote field with near/far
 *     scales, and the timed bead clusters,
 *   - shortest-arc hue blending for palette changes,
 *   - the INTEGRATED motion clock: speed changes the clock's rate, never
 *     multiplies absolute time (the playbook's "the field lurches" trap),
 *   - the bounded raster: the field is painted at <= FIELD_MAX_PX on its long
 *     side and stretched by CSS (a background can be soft; it has no text).
 *
 * COST CONTROL. Full tier: <= 30 fps, 15 fps while html[data-gg-fx="hot"] or
 * the load governor is busy. Lite tier (exec/perfTier.js): a static field,
 * repainted only while the heat or palette is gliding, at 2 fps. Reduced
 * motion: the field stands still (motion clock frozen) and only its colour
 * and brightness follow the heat. The Options intensity slider scales the
 * whole field (applyIntensity); at 0 it is a still dark gradient. A hidden page schedules nothing.
 *
 * Pure parts (palettes, heat -> params, glide, hue mix, query parsing) are
 * exported for test/selftest-background.js and import clean under node.
 * ==========================================================================*/

import { perfLite } from './perfTier.js';
import { governorBusy } from './loadGovernor.js';

const TAU = Math.PI * 2;
const clamp01 = x => (Number.isFinite(x) ? Math.max(0, Math.min(1, x)) : 0);
const fract = x => x - Math.floor(x);
const hash = n => fract(Math.sin(n * 127.1 + 311.7) * 43758.5453);

export const DEFAULT_HEAT = 0.3;
export const FIELD_MAX_PX = 960;
export const MAX_STRANDS = 10;
export const MAX_MOTES = 200;
/** Heat glides up faster than it cools: a lead should be felt, a loss should settle. */
export const HEAT_TAU_UP_S = 0.9;
export const HEAT_TAU_DOWN_S = 2.4;
export const PALETTE_TAU_S = 1.2;

/* ---------------------------------------------------------------------------
 * PALETTES. hues = [room/primary, secondary, accent]; sat = the palette's own
 * saturation ceiling (Censored is nearly grey on purpose); light = the room's
 * centre lightness at full heat. Tints match ui/flavours.js cards.
 * ------------------------------------------------------------------------ */
export const BG_PALETTES = Object.freeze({
  trance:   Object.freeze({ hues: [266, 292, 232], sat: 62, light: 15 }),
  pink:     Object.freeze({ hues: [322, 300, 342], sat: 70, light: 16 }),
  frills:   Object.freeze({ hues: [334, 352, 312], sat: 52, light: 18 }),
  shiny:    Object.freeze({ hues: [168, 196, 284], sat: 64, light: 13 }),
  censored: Object.freeze({ hues: [208, 222, 196], sat: 20, light: 14 }),
  mine:     Object.freeze({ hues: [292, 318, 250], sat: 50, light: 14 }),
});
/** The house look (the page's own pink-violet) for no flavour or an unknown id. */
export const HOUSE_PALETTE = BG_PALETTES.mine;
export const paletteFor = id => BG_PALETTES[String(id || '').toLowerCase()] || HOUSE_PALETTE;

/** Hue a -> b by k along the SHORTER arc (never through the whole wheel). */
export function mixHue(a, b, k) {
  const d = ((((b - a) % 360) + 540) % 360) - 180;
  return (((a + d * clamp01(k)) % 360) + 360) % 360;
}
export function mixPalette(a, b, k) {
  const t = clamp01(k);
  return {
    hues: [0, 1, 2].map(i => mixHue(a.hues[i], b.hues[i], t)),
    sat: a.sat + (b.sat - a.sat) * t,
    light: a.light + (b.light - a.light) * t,
  };
}

/** Exponential approach: frame-rate independent, never overshoots, never snaps. */
export function glide(cur, target, dt, tau) {
  if (!Number.isFinite(cur)) return target;
  if (!(dt > 0) || !(tau > 0)) return cur;
  return cur + (target - cur) * (1 - Math.exp(-dt / tau));
}

/**
 * Heat (0..1) -> what the field does. Every value is monotone in heat, so a
 * rising lead only ever ADDS life. Fractional counts are kept fractional: the
 * renderer fades the last strand / mote in instead of popping it.
 */
export function heatParams(heat) {
  const h = clamp01(heat);
  const late = k => clamp01((h - k) / (1 - k));
  return {
    heat: h,
    rate: 0.3 + 1.3 * h,              // motion clock speed
    strands: 2.5 + 7.5 * h,           // ribbons shown (of MAX_STRANDS)
    motes: 50 + 150 * h,              // motes shown (of MAX_MOTES)
    fields: 0.12 + 0.16 * h,          // colour field opacity
    line: 0.22 + 0.5 * h,             // ribbon opacity
    beads: late(0.3),                 // pearls on the ribbons fade in past 0.3
    trails: late(0.55),               // mote comet tails past 0.55
    clusters: late(0.7),              // bead clusters past 0.7
    sat: 0.45 + 0.55 * h,             // calm is greyer
    room: 0.55 + 0.45 * h,            // room light
    grain: 0.05 - 0.015 * h,          // grain shows most when things are quiet
  };
}

/* ---------------------------------------------------------------------------
 * INTENSITY (the Options slider, 0..1, default 1 = as built). It scales how much
 * of the heat's life the field shows: 0 is the static dark room gradient with
 * no ribbons, motes, pearls or motion; 1 is exactly heatParams(). The pref
 * lives in ui/prefs.js and reaches this tier as <html data-gg-bgint>, the same
 * way data-gg-shader does (exec/ never imports ui/). Absent = 1.
 * ------------------------------------------------------------------------ */
export const BG_INTENSITY_ATTR = 'data-gg-bgint';
export function readIntensity(value) {
  if (value === null || value === undefined || value === '') return 1;
  const n = Number(value);
  return Number.isFinite(n) ? clamp01(n) : 1;
}
/** Scale a heatParams() bag by intensity k. Pure; k = 1 returns the same values. */
export function applyIntensity(P, k) {
  const i = clamp01(k);
  if (i >= 1) return P;
  return {
    ...P,
    rate: P.rate * i,
    strands: P.strands * i,
    motes: P.motes * i,
    fields: P.fields * i,
    line: P.line * i,
    beads: P.beads * i,
    trails: P.trails * i,
    clusters: P.clusters * i,
    sat: P.sat * (0.6 + 0.4 * i),
    room: P.room * (0.55 + 0.45 * i),
    grain: P.grain * i,
    intensity: i,
  };
}

/** Read the dev hooks from a location.search string. Pure. */
export function readBgQuery(search) {
  const out = { heat: null, flavour: null, debug: false };
  let q;
  try { q = new URLSearchParams(String(search || '')); } catch (_e) { return out; }
  if (q.has('heat')) { const v = parseFloat(q.get('heat')); if (Number.isFinite(v)) out.heat = clamp01(v); }
  if (q.has('bgflavour')) { const f = String(q.get('bgflavour')).toLowerCase(); if (BG_PALETTES[f]) out.flavour = f; }
  out.debug = q.has('bgdebug');
  return out;
}
/** A `gg-heat` event's detail -> a heat, or null for junk. */
export function heatFromDetail(detail) {
  const v = detail && typeof detail === 'object' ? Number(detail.heat) : NaN;
  return Number.isFinite(v) ? clamp01(v) : null;
}

/* ---------------------------------------------------------------------------
 * RENDERER
 * ------------------------------------------------------------------------ */
function seeded(seed) {
  let n = seed >>> 0;
  return () => { n += 0x6D2B79F5; let k = n; k = Math.imul(k ^ k >>> 15, k | 1); k ^= k + Math.imul(k ^ k >>> 7, k | 61); return ((k ^ k >>> 14) >>> 0) / 4294967296; };
}
/** Strands are revealed centre-out so a low heat still reads as one composed band. */
const REVEAL = [4, 5, 3, 6, 2, 7, 1, 8, 0, 9];
const ATLAS_CELL = 48, ATLAS_SATS = [88, 24];
const hs = (h, s, l, a) => `hsla(${h.toFixed(1)},${s.toFixed(1)}%,${l.toFixed(1)}%,${Math.max(0, Math.min(1, a)).toFixed(3)})`;

function makeCanvas(w, h) {
  if (typeof document !== 'undefined') { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; }
  return typeof OffscreenCanvas !== 'undefined' ? new OffscreenCanvas(w, h) : null;
}

/** 24 hues x (2 saturations x 2 kinds: pearl, wash). Built once, never per frame. */
function buildAtlas() {
  const cv = makeCanvas(24 * ATLAS_CELL, 4 * ATLAS_CELL);
  if (!cv) return null;
  const s = cv.getContext('2d');
  const r = ATLAS_CELL / 2;
  for (let i = 0; i < 24; i++) for (let row = 0; row < 4; row++) {
    const hue = i * 15, sat = ATLAS_SATS[row >> 1], wash = row & 1;
    const x = i * ATLAS_CELL + r, y = row * ATLAS_CELL + r;
    const g = s.createRadialGradient(x, y, 0, x, y, r);
    if (!wash) {
      g.addColorStop(0, 'rgba(255,255,255,1)'); g.addColorStop(0.07, hs(hue, sat, 92, 0.95));
      g.addColorStop(0.24, hs(hue, sat, 72, 0.6)); g.addColorStop(0.55, hs(hue, sat, 58, 0.16));
    } else {
      g.addColorStop(0, hs(hue, sat, 60, 0.75)); g.addColorStop(0.4, hs(hue, sat, 56, 0.42)); g.addColorStop(0.75, hs(hue, sat, 52, 0.1));
    }
    g.addColorStop(1, hs(hue, sat, 50, 0));
    s.fillStyle = g; s.fillRect(i * ATLAS_CELL, row * ATLAS_CELL, ATLAS_CELL, ATLAS_CELL);
  }
  return cv;
}
/** One soft sprite: blends 2 adjacent hues x 2 saturations. `wash` = the broad kind. */
function sprite(c, atlas, hue, satK, x, y, rx, ry, alpha, wash) {
  if (!atlas || alpha < 0.002) return;
  const f = fract(hue / 360) * 24, i = Math.floor(f) % 24, k = f - Math.floor(f);
  const cells = [[i, 1 - k], [(i + 1) % 24, k]];
  const sats = [[0, satK], [1, 1 - satK]];
  for (const [ci, wk] of cells) {
    if (wk < 0.01) continue;
    for (const [si, ws] of sats) {
      const a = alpha * wk * ws;
      if (a < 0.002) continue;
      c.globalAlpha = a;
      c.drawImage(atlas, ci * ATLAS_CELL, (si * 2 + (wash ? 1 : 0)) * ATLAS_CELL, ATLAS_CELL, ATLAS_CELL, x - rx, y - ry, rx * 2, ry * 2);
    }
  }
}
function grainTile() {
  const cv = makeCanvas(128, 128);
  if (!cv) return null;
  const s = cv.getContext('2d');
  const img = s.createImageData(128, 128), rnd = seeded(907);
  for (let p = 0; p < img.data.length; p += 4) {
    const v = rnd() * 255;
    img.data[p] = img.data[p + 1] = img.data[p + 2] = v; img.data[p + 3] = 255;
  }
  s.putImageData(img, 0, 0);
  return cv;
}

function buildScene() {
  const rnd = seeded(0x60011);
  return {
    strands: Array.from({ length: MAX_STRANDS }, () => ({
      pts: new Float32Array(2 * 49),
      beads: Array.from({ length: 6 }, () => ({ at: rnd(), speed: 0.012 + rnd() * 0.018, size: 0.7 + rnd() * 1.7, pulse: rnd() * TAU })),
    })),
    motes: Array.from({ length: MAX_MOTES }, () => ({ x: rnd(), y: rnd(), z: rnd(), phase: rnd() * TAU, speed: 4 + rnd() * 12, hue: Math.floor(rnd() * 3) })),
  };
}

/** Aurora fold (Afterimage "veil"), in a 900-high design space, scaled by S. */
function foldStrand(s, i, W, H, u, S) {
  const steps = 48, ratio = i / (MAX_STRANDS - 1), p = s.pts;
  for (let j = 0; j <= steps; j++) {
    const q = j / steps;
    const x = -W * 0.18 + q * W * 1.36;
    const angle = q * TAU * 0.65 + u * 0.082;
    // A third, per-strand term breaks the sheet out of a parallel "stave" look.
    const fold = Math.sin(angle + ratio * 0.65) * 165 + Math.sin(q * TAU * 1.25 - u * 0.044 + ratio * 0.8) * 37
      + Math.sin(q * TAU * 0.9 + u * 0.061 + i * 1.7) * 26;
    const spacing = 44 + 12 * Math.sin(q * TAU * 0.58 - u * 0.053);
    const y = H * 0.5 + ((i - (MAX_STRANDS - 1) / 2) * spacing + fold - (q - 0.5) * 145) * S;
    p[j * 2] = x; p[j * 2 + 1] = y;
  }
  return steps;
}
function strokePath(c, p, n) {
  c.beginPath(); c.moveTo(p[0], p[1]);
  for (let j = 0; j < n; j++) {
    const a = Math.max(0, j - 1), b = j + 1, d = Math.min(n, j + 2);
    c.bezierCurveTo(p[j * 2] + (p[b * 2] - p[a * 2]) / 6, p[j * 2 + 1] + (p[b * 2 + 1] - p[a * 2 + 1]) / 6,
      p[b * 2] - (p[d * 2] - p[j * 2]) / 6, p[b * 2 + 1] - (p[d * 2 + 1] - p[j * 2 + 1]) / 6, p[b * 2], p[b * 2 + 1]);
  }
}
function samplePath(p, n, q) {
  const f = clamp01(q) * n, i = Math.min(n - 1, Math.floor(f)), k = f - i;
  return [p[i * 2] + (p[(i + 1) * 2] - p[i * 2]) * k, p[i * 2 + 1] + (p[(i + 1) * 2 + 1] - p[i * 2 + 1]) * k];
}
function motePos(dot, u, W, H, S) {
  const x = dot.x * W + Math.sin(u * 0.045 + dot.phase) * (28 + dot.z * 38) * S;
  const y = fract(dot.y - u * dot.speed / 2100) * (H + 140 * S) - 70 * S;
  return [x, y];
}

/**
 * Paint one frame. Pure of timers: (ctx, state, u = motion clock, params, palette).
 * Exported so a harness can render a still without a loop.
 */
export function paintField(c, W, H, u, P, pal, res) {
  const S = H / 900, satK = clamp01(P.sat * pal.sat / 88);
  const [h0, h1, h2] = pal.hues, sat = pal.sat * P.sat;
  c.setTransform(1, 0, 0, 1, 0, 0);
  c.globalCompositeOperation = 'source-over'; c.globalAlpha = 1;

  // 1. The room.
  const g = c.createRadialGradient(W / 2, H * 0.46, 10, W / 2, H * 0.46, Math.max(W, H) * 0.8);
  g.addColorStop(0, hs(h0, sat, pal.light * P.room, 1));
  g.addColorStop(0.6, hs(h1, sat * 0.9, 6 + 2 * P.heat, 1));
  g.addColorStop(1, hs(h2, sat * 0.8, 2.5, 1));
  c.fillStyle = g; c.fillRect(0, 0, W, H);

  c.globalCompositeOperation = 'screen';
  // 2. Broad colour fields drift under everything (kept off the dead centre).
  for (let i = 0; i < 5; i++) {
    const x = W * (0.12 + i * 0.19 + 0.06 * Math.sin(u * 0.035 + i * 1.8));
    const y = H * 0.5 + Math.sin(u * 0.043 + i * 2) * 290 * S;
    sprite(c, res.atlas, [h0, h1, h2][i % 3], satK, x, y, W * 0.28, 250 * S, P.fields, true);
  }

  // 3. Ribbons, three-pass stroke; the last one fades in with the heat.
  c.lineCap = 'round'; c.lineJoin = 'round';
  for (let r = 0; r < MAX_STRANDS; r++) {
    const vis = clamp01(P.strands - r);
    if (vis < 0.01) break;
    const i = REVEAL[r], s = res.scene.strands[i];
    const n = foldStrand(s, i, W, H, u, S);
    const hue = mixHue([h0, h1, h2][i % 3], [h0, h1, h2][(i + 1) % 3], 0.3 + 0.2 * Math.sin(u * 0.07 + i));
    const a = P.line * vis;
    strokePath(c, s.pts, n);
    c.globalAlpha = 1;
    c.strokeStyle = hs(hue, sat, 58, 0.06 * a); c.lineWidth = 46 * S; c.stroke();
    c.strokeStyle = hs(hue, sat, 70, 0.2 * a); c.lineWidth = 4.6 * S; c.stroke();
    c.strokeStyle = hs(hue, sat, 84, (r % 3 === 0 ? 0.7 : 0.5) * a); c.lineWidth = Math.max(0.8, 1.3 * S); c.stroke();
    // 4. Pearls travel the ribbon.
    if (P.beads > 0.01) {
      for (const dot of s.beads) {
        const q = fract(dot.at + u * dot.speed);
        const edge = clamp01(Math.min(q, 1 - q) * 9), pulse = 0.76 + 0.24 * Math.sin(u * 0.46 + dot.pulse);
        const [x, y] = samplePath(s.pts, n, q);
        if (x < -30 || x > W + 30 || y < -30 || y > H + 30) continue;
        const rad = dot.size * S * (1 + 0.2 * P.heat) * 5;
        sprite(c, res.atlas, hue, satK, x, y, rad, rad, a * edge * pulse * P.beads, false);
      }
    }
  }

  // 5. Motes: far dust, mid drift, a few soft near bokeh.
  const count = Math.min(MAX_MOTES, P.motes);
  for (let i = 0; i < MAX_MOTES; i++) {
    const vis = clamp01(count - i);
    if (vis < 0.01) break;
    const dot = res.scene.motes[i], hue = [h0, h1, h2][dot.hue] + Math.sin(u * 0.032 + dot.phase) * 14;
    const [x, y] = motePos(dot, u, W, H, S);
    const alpha = vis * (0.3 + dot.z * 0.45) * (0.84 + 0.16 * Math.sin(u * 0.31 + dot.phase)) * (0.55 + 0.45 * P.heat);
    const soft = i % 11 === 0;
    if (!soft && i % 7 === 0 && P.trails > 0.01) {
      const [tx, ty] = motePos(dot, u - 2.1, W, H, S);
      if (Math.abs(ty - y) < H * 0.2) {
        c.globalAlpha = alpha * P.trails * 0.5; c.strokeStyle = hs(hue, sat, 80, 0.4); c.lineWidth = Math.max(0.7, 1.2 * S);
        c.beginPath(); c.moveTo(tx, ty); c.quadraticCurveTo((x + tx) / 2 + Math.sin(dot.phase) * 8 * S, (y + ty) / 2, x, y); c.stroke();
      }
    }
    const rad = soft ? (3 + dot.z * 7) * S * 3.2 : (0.65 + dot.z * dot.z * 2.6) * S * 5;
    sprite(c, res.atlas, hue, satK, x, y, rad, rad, alpha * (soft ? 0.5 : 1), soft);
  }

  // 6. Clusters: a small constellation blooms and drifts off, only when hot.
  if (P.clusters > 0.01) {
    const period = 8, life = 13, cur = Math.floor(u / period);
    for (let slot = cur - 1; slot <= cur; slot++) {
      const age = u - slot * period;
      if (age < 0 || age > life) continue;
      const k = slot * 31 + 7, env = Math.pow(Math.sin(age / life * Math.PI), 2);
      const cx = W * ((slot & 1) ? 0.78 : 0.22) + Math.sin(age * 0.12 + k) * W * 0.05, cy = H * (0.2 + hash(k + 8) * 0.6);
      for (let j = 0; j < 22; j++) {
        const th = hash(k + j * 2.31) * TAU + age * 0.045, rr = (20 + hash(k + j * 3.17) * 100) * (0.6 + age * 0.035) * S;
        const rad = (0.6 + hash(k + j * 4.13) * 2.1) * S * 5;
        sprite(c, res.atlas, [h0, h1, h2][j % 3] + 10, satK, cx + Math.cos(th) * rr, cy + Math.sin(th) * rr * 0.55 - age * 3 * S, rad, rad, env * 0.45 * P.clusters, false);
      }
    }
  }

  // 7. Vignette + a slightly darker centre keep the field secondary to the media.
  c.globalCompositeOperation = 'source-over'; c.globalAlpha = 1;
  const v = c.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.1, W / 2, H / 2, Math.max(W, H) * 0.75);
  v.addColorStop(0, 'rgba(6,2,10,0.28)'); v.addColorStop(0.45, 'rgba(6,2,10,0)'); v.addColorStop(1, 'rgba(3,1,6,0.62)');
  c.fillStyle = v; c.fillRect(0, 0, W, H);

  // 8. Grain.
  if (res.grain && P.grain > 0) {
    if (!res.grainPattern) res.grainPattern = c.createPattern(res.grain, 'repeat');
    const off = res.still ? 0 : Math.floor(u * 97) % 128;
    c.globalCompositeOperation = 'overlay'; c.globalAlpha = P.grain * 2.2;
    c.translate(-off, -((off * 7) % 128));
    c.fillStyle = res.grainPattern; c.fillRect(0, 0, W + 128, H + 128);
    c.setTransform(1, 0, 0, 1, 0, 0);
  }
  c.globalCompositeOperation = 'source-over'; c.globalAlpha = 1;
}

function reducedMotion() {
  try {
    if (document.documentElement.getAttribute('data-gg-motion') === 'reduced') return true;
    return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch (_e) { return false; }
}
function intensity() {
  try { return readIntensity(document.documentElement.getAttribute(BG_INTENSITY_ATTR)); } catch (_e) { return 1; }
}
function fxHot() {
  try { return document.documentElement.getAttribute('data-gg-fx') === 'hot'; } catch (_e) { return false; }
}

/**
 * Mount on #gg-bg. Returns { setFlavour, setHeat, stop, stats }. Idempotent:
 * a second call returns the running instance. Never throws into boot.
 */
let live = null;
export function mountBackground(opts = {}) {
  if (live) return live;
  if (typeof document === 'undefined' || typeof window === 'undefined') return null;
  const canvas = opts.canvas || document.getElementById('gg-bg');
  const c = canvas && canvas.getContext ? canvas.getContext('2d', { alpha: false }) : null;
  if (!c) return null;

  const query = readBgQuery(window.location && window.location.search);
  const res = { atlas: buildAtlas(), grain: grainTile(), grainPattern: null, scene: buildScene(), still: false };
  let target = query.heat != null ? query.heat : DEFAULT_HEAT, heat = target;
  let flavour = query.flavour || '', from = paletteFor(flavour), to = from, palK = 1;
  let u = 0, last = 0, lastPaint = 0, raf = 0, dirty = true;
  const stats = { frames: 0, avgMs: 0, maxMs: 0, w: 0, h: 0, mode: '' };

  function size() {
    const iw = Math.max(1, window.innerWidth || 1), ih = Math.max(1, window.innerHeight || 1);
    const k = Math.min(1, FIELD_MAX_PX / Math.max(iw, ih));
    const w = Math.round(iw * k), h = Math.round(ih * k);
    if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; res.grainPattern = null; dirty = true; }
  }
  function frame(now) {
    raf = 0;
    if (document.hidden) return;
    const dt = last ? Math.min(0.1, (now - last) / 1000) : 0;
    last = now;
    const lite = perfLite(), still = reducedMotion() || intensity() <= 0.001;
    const busy = fxHot() || governorBusy();
    const minGap = lite ? 500 : busy ? 66 : 33;
    heat = glide(heat, target, dt, target > heat ? HEAT_TAU_UP_S : HEAT_TAU_DOWN_S);
    palK = glide(palK, 1, dt, PALETTE_TAU_S);
    const gliding = Math.abs(heat - target) > 0.003 || palK < 0.997;
    if (gliding) dirty = true;
    const P = applyIntensity(heatParams(heat), intensity());
    if (!still && !lite) { u += dt * P.rate; dirty = true; }
    stats.mode = lite ? 'lite' : still ? 'still' : busy ? 'busy' : 'full';
    if (dirty && now - lastPaint >= minGap) {
      res.still = still || lite;
      const t0 = performance.now();
      paintField(c, canvas.width, canvas.height, u, P, mixPalette(from, to, palK), res);
      const ms = performance.now() - t0;
      stats.frames++; stats.avgMs += (ms - stats.avgMs) / Math.min(stats.frames, 60); stats.maxMs = Math.max(stats.maxMs, ms);
      stats.w = canvas.width; stats.h = canvas.height; stats.heat = heat;
      lastPaint = now; dirty = false;
      if (debugOut) debugOut.textContent = `heat ${heat.toFixed(2)} ${stats.mode} ${stats.avgMs.toFixed(2)} ms ${canvas.width}x${canvas.height}`;
    }
    // Lite / still with nothing gliding: sleep until something changes.
    if ((lite || still) && !gliding && !dirty) return;
    raf = requestAnimationFrame(frame);
  }
  function wake() { if (!raf && !document.hidden) { last = 0; raf = requestAnimationFrame(frame); } }

  const api = {
    setHeat(v) { if (query.heat != null) return; const h = clamp01(Number(v)); if (Number.isFinite(Number(v))) { target = h; wake(); } },
    setFlavour(id) {
      const next = query.flavour || String(id || '');
      if (next === flavour) return;
      from = mixPalette(from, to, palK); to = paletteFor(next); flavour = next; palK = 0; dirty = true; wake();
    },
    stop() { if (raf) cancelAnimationFrame(raf); raf = 0; window.removeEventListener('gg-heat', onHeat); live = null; },
    stats,
  };
  const onHeat = e => { const h = heatFromDetail(e && e.detail); if (h != null) api.setHeat(h); };
  window.addEventListener('gg-heat', onHeat);
  window.addEventListener('resize', () => { size(); wake(); });
  document.addEventListener('visibilitychange', () => { if (!document.hidden) { dirty = true; wake(); } });
  try {
    new MutationObserver(() => { dirty = true; wake(); })
      .observe(document.documentElement, { attributes: true, attributeFilter: ['data-gg-perf', 'data-gg-motion', 'data-gg-fx', BG_INTENSITY_ATTR] });
  } catch (_e) { /* no observer: the loop still picks the change up on its next frame */ }

  let debugOut = null;
  if (query.debug) {
    const box = document.createElement('div');
    box.style.cssText = 'position:fixed;left:8px;bottom:8px;z-index:95;font:11px monospace;color:#fff;background:rgba(0,0,0,.55);padding:6px 8px;border-radius:6px';
    const slider = document.createElement('input');
    slider.type = 'range'; slider.min = '0'; slider.max = '1'; slider.step = '0.01'; slider.value = String(target);
    slider.addEventListener('input', () => { query.heat = null; api.setHeat(slider.value); });
    debugOut = document.createElement('div');
    box.append(slider, debugOut); document.body.appendChild(box);
  }

  size();
  window.__ggBg = api;
  live = api;
  wake();
  return api;
}

export default mountBackground;
