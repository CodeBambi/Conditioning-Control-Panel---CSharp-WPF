/* ============================================================================
 * loomField.js - THE LOOM schema v2: the enhanced spiral field.
 *
 * A strict superset of loomSpiral.js's schema v1 (a v1 sidecar normalizes into
 * v2 with every new feature at its neutral default, so old spirals render as
 * they always did). The field itself - arms, six styles, duty bands, twist,
 * up to six threads with hard/gradient blending, a counter-rotating second
 * layer, glow, pulse zoom, liquid wobble, and the background - is computed
 * per-pixel in ONE GLSL fragment shader. The live preview (game/loomStudio.js)
 * and the GIF encoder (engine/loomWorker.js) both composite through
 * composeFrame(), so the file always matches the pane exactly.
 *
 * Seamless looping: every animated quantity is an INTEGER number of cycles of
 * the master phase u in [0,1) - rotation moves an exact symmetry span
 * (loomSpiral.js's rule generalized to 1-6 colors), hue/pulse/wobble/flash
 * complete whole cycles - so frame 0 == frame N by construction and the
 * editor can never produce a non-looping GIF.
 *
 * Schema v2 (persisted verbatim in the loom_<slug>.json sidecar):
 *   { schema: 2, format: 'square'|'wide'|'tall', speed: 1-5,
 *     bg: {kind:'solid'|'radial', color, outer},
 *     layer:  {arms:1-12, turns:0.5-6, duty:0.2-0.8,
 *              style:'log'|'arch'|'ribbon'|'golden'|'tunnel'|'petal',
 *              direction:1|-1, colors:['#rrggbb' x1-6],
 *              bandMode:'hard'|'gradient', speedMul:1-3},
 *     layer2: layer + {enabled:bool},
 *     glow: 0-1, pulse: {amp:0-0.25, cycles:1-4},
 *     wobble: {amp:0-0.35, freq:1-6, cycles:1-3}, hueCycles: 0-2,
 *     centerpiece: {kind:'none'|'dot'|'star'|'cross'|'x'|'mantra', color, sizeFrac:0.08-0.4,
 *                   text:<=12ch, flashCycles:0-4} }
 * ==========================================================================*/

import { drawSpiral } from './loomSpiral.js';

export const LOOM_SCHEMA_V2 = 2;
export const LOOM_STYLES = ['log', 'arch', 'ribbon', 'golden', 'tunnel', 'petal'];
export const LOOM_FORMATS = ['square', 'wide', 'tall'];   // 1:1 · 16:9 · 9:16 (tiktok)
export const LOOM_SWATCHES = ['#ff69b4', '#e56cc0', '#8a5cff', '#00e5ff', '#39ff9d', '#ffd94d', '#ff6a2f', '#ffffff'];

/** speed 1-5 -> GIF frame delay in centiseconds, paired with FRAME_TABLE below.
 * Delays are finer than the old {10,7,5,3,2} desktop table so we can weave more
 * frames per loop (smoother motion) WITHOUT changing spin speed: for every speed
 * FRAME_TABLE[s] * DELAY_CS[s] equals the old 36 * oldDelay, so each loop lasts
 * exactly as long as before - just with ~2x the frames. Preview and encoder both
 * read these (loopMs2 + the worker), so the pane always matches the file. */
const DELAY_CS = { 1: 5, 2: 4, 3: 3, 4: 2, 5: 2 };
/** speed 1-5 -> frames per loop. speed 5 stays 36 (already at the 2cs floor, so
 * it can't gain frames without slowing down). */
const FRAME_TABLE = { 1: 72, 2: 63, 3: 60, 4: 54, 5: 36 };
const TWO_PI = Math.PI * 2;

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const HEX_RE = /^#[0-9a-f]{6}$/i;
const hex = (v, fb) => (typeof v === 'string' && HEX_RE.test(v) ? v.toLowerCase() : fb);
const num = (v, fb) => (typeof v === 'number' && isFinite(v) ? v : fb);
const snap = (v, step) => Math.round(v / step) * step;

function hexList(v, fb) {
  const out = [];
  if (Array.isArray(v)) {
    for (const c of v) {
      const h = hex(c, null);
      if (h && !out.includes(h)) out.push(h);
      if (out.length >= 6) break;
    }
  }
  return out.length ? out : fb.slice();
}

function normalizeLayer(raw, fallbackColors) {
  const q = raw && typeof raw === 'object' ? raw : {};
  return {
    arms: clamp(Math.round(num(q.arms, 4)), 1, 12),
    turns: clamp(snap(num(q.turns, 2), 0.25), 0.5, 6),
    duty: clamp(snap(num(q.duty, 0.5), 0.05), 0.2, 0.8),
    style: LOOM_STYLES.includes(q.style) ? q.style : 'log',
    direction: q.direction === -1 ? -1 : 1,
    colors: hexList(q.colors, fallbackColors),
    bandMode: q.bandMode === 'gradient' ? 'gradient' : 'hard',
    speedMul: clamp(Math.round(num(q.speedMul, 1)), 1, 3),
  };
}

export function defaultParams2() {
  return {
    schema: LOOM_SCHEMA_V2,
    format: 'square',
    layer: { arms: 4, turns: 2, duty: 0.5, style: 'log', direction: 1, colors: ['#ff69b4'], bandMode: 'hard', speedMul: 1 },
    layer2: { enabled: false, arms: 4, turns: 2, duty: 0.35, style: 'log', direction: -1, colors: ['#8a5cff'], bandMode: 'hard', speedMul: 1 },
    speed: 3,
    bg: { kind: 'solid', color: '#14060f', outer: '#08040e' },
    glow: 0,
    pulse: { amp: 0, cycles: 1 },
    wobble: { amp: 0, freq: 3, cycles: 1 },
    hueCycles: 0,
    centerpiece: { kind: 'none', color: '#ffffff', sizeFrac: 0.16, text: '', flashCycles: 1 },
  };
}

/**
 * Coerce anything - a v2 sidecar, a v1 sidecar (flat fields + bg string), or
 * junk - into valid v2 params. v1 spirals keep their exact desktop look:
 * every v2-only feature defaults to neutral.
 */
export function normalizeParams2(p) {
  const d = defaultParams2();
  const q = p && typeof p === 'object' ? p : {};

  // Desktop v1: flat layer fields at the top level, bg is a plain hex string.
  const isV1 = q.schema === 1 || (q.layer == null && (q.arms != null || q.colors != null));
  if (isV1) {
    d.layer = normalizeLayer(q, d.layer.colors);
    d.speed = clamp(Math.round(num(q.speed, 3)), 1, 5);
    d.bg.color = hex(q.bg, d.bg.color);
    return d;
  }

  const bg = q.bg && typeof q.bg === 'object' ? q.bg : {};
  const pulse = q.pulse && typeof q.pulse === 'object' ? q.pulse : {};
  const wobble = q.wobble && typeof q.wobble === 'object' ? q.wobble : {};
  const cp = q.centerpiece && typeof q.centerpiece === 'object' ? q.centerpiece : {};
  const l2 = q.layer2 && typeof q.layer2 === 'object' ? q.layer2 : {};

  return {
    schema: LOOM_SCHEMA_V2,
    format: LOOM_FORMATS.includes(q.format) ? q.format : 'square',
    layer: normalizeLayer(q.layer, d.layer.colors),
    layer2: Object.assign(normalizeLayer(l2, d.layer2.colors), { enabled: l2.enabled === true }),
    speed: clamp(Math.round(num(q.speed, 3)), 1, 5),
    bg: {
      kind: bg.kind === 'radial' ? 'radial' : 'solid',
      color: hex(bg.color, d.bg.color),
      outer: hex(bg.outer, d.bg.outer),
    },
    glow: clamp(num(q.glow, 0), 0, 1),
    pulse: { amp: clamp(num(pulse.amp, 0), 0, 0.25), cycles: clamp(Math.round(num(pulse.cycles, 1)), 1, 4) },
    wobble: {
      amp: clamp(num(wobble.amp, 0), 0, 0.35),
      freq: clamp(Math.round(num(wobble.freq, 3)), 1, 6),
      cycles: clamp(Math.round(num(wobble.cycles, 1)), 1, 3),
    },
    hueCycles: clamp(Math.round(num(q.hueCycles, 0)), 0, 2),
    centerpiece: {
      kind: ['dot', 'star', 'cross', 'x', 'mantra'].includes(cp.kind) ? cp.kind : 'none',
      color: hex(cp.color, '#ffffff'),
      sizeFrac: clamp(num(cp.sizeFrac, 0.16), 0.08, 0.4),
      text: typeof cp.text === 'string' ? cp.text.slice(0, 12) : '',
      flashCycles: clamp(Math.round(num(cp.flashCycles, 1)), 0, 4),
    },
  };
}

export function delayCsFor2(p) { return DELAY_CS[normalizeParams2(p).speed] || 3; }

/** Frames per loop for these params (shared by the preview timing and the encoder). */
export function framesFor2(p) { return FRAME_TABLE[normalizeParams2(p).speed] || 60; }

/** Pixel dims for a format at a given long side: 1:1, 16:9, or 9:16. */
export function formatDims2(format, longSide) {
  const short = Math.round((longSide * 9) / 16);
  if (format === 'wide') return { w: longSide, h: short };
  if (format === 'tall') return { w: short, h: longSide };
  return { w: longSide, h: longSide };
}

/** One seamless loop in ms (preview phase = (elapsed % loopMs2) / loopMs2).
 * frames * delay must match the worker's exactly, so both derive from the tables. */
export function loopMs2(p) { return framesFor2(p) * delayCsFor2(p) * 10; }

/** Smallest rotation that maps a layer onto itself (v1 loopSpanRad, 1-6 colors). */
function symmetrySpanRad(layer) {
  const n = Math.max(1, Math.min(layer.colors.length, 6));
  return (TWO_PI / layer.arms) * (layer.arms % n === 0 ? n : layer.arms);
}

function layerRotationRad(layer, phase) {
  return layer.direction * phase * symmetrySpanRad(layer) * layer.speedMul;
}

// ---- color helpers -------------------------------------------------------

function hexToRgb01(h) {
  const n = parseInt(h.slice(1), 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}

/** Rotate hue by rad via the standard luma-preserving matrix (hue-cycle). */
function rotateHue(rgb, rad) {
  if (!rad) return rgb;
  const c = Math.cos(rad), s = Math.sin(rad);
  const [r, g, b] = rgb;
  const cl = (v) => Math.max(0, Math.min(1, v));
  return [
    cl((0.213 + c * 0.787 - s * 0.213) * r + (0.715 - c * 0.715 - s * 0.715) * g + (0.072 - c * 0.072 + s * 0.928) * b),
    cl((0.213 - c * 0.213 + s * 0.143) * r + (0.715 + c * 0.285 + s * 0.140) * g + (0.072 - c * 0.072 - s * 0.283) * b),
    cl((0.213 - c * 0.213 - s * 0.787) * r + (0.715 - c * 0.715 + s * 0.715) * g + (0.072 + c * 0.928 + s * 0.072) * b),
  ];
}

function rgb01ToCss(rgb) {
  const to255 = (v) => Math.round(v * 255);
  return `rgb(${to255(rgb[0])},${to255(rgb[1])},${to255(rgb[2])})`;
}

// ---- the field shader ----------------------------------------------------

const VERT_SRC = `
attribute vec2 a_pos;
void main() { gl_Position = vec4(a_pos, 0.0, 1.0); }
`;

/** Per-layer field evaluation, stamped out twice (GLSL can't take array params). */
function layerGlsl(S) {
  return `
uniform float u_arms${S}, u_turns${S}, u_duty${S}, u_style${S}, u_band${S}, u_count${S}, u_rot${S};
uniform vec3 u_colors${S}[6];

vec3 pick${S}(float idx) {
  vec3 c = u_colors${S}[0];
  for (int i = 1; i < 6; i++) {
    if (abs(float(i) - idx) < 0.5) c = u_colors${S}[i];
  }
  return c;
}

vec3 blend${S}(float g) {
  float count = u_count${S};
  float i0 = floor(g);
  float f = g - i0;
  float i1 = mod(i0 + 1.0, count);
  return mix(pick${S}(i0), pick${S}(i1), f);
}

vec4 layer${S}(float r, float th, float wob) {
  float arms = u_arms${S};
  float count = u_count${S};
  float duty = u_duty${S};
  float style = u_style${S};
  float rc = clamp(r, 0.0, 1.0);

  float t;
  if (style < 0.5)      { t = pow(rc, 0.5714286); }                     // log: r = t^1.75
  else if (style < 1.5) { t = rc; }                                     // arch
  else if (style < 2.5) { t = 0.5 - sin(asin(1.0 - 2.0 * rc) / 3.0); }  // ribbon: inverse smoothstep
  else                  { t = log(1.0 + rc * 6.0) / 1.9459101; }        // golden: constant-pitch growth

  float cov = 0.0;
  vec3 col = vec3(0.0);

  if (style < 3.5) {
    // Angular arm bands (log / arch / ribbon / golden).
    float a = th - u_rot${S} - 6.2831853 * u_turns${S} * t + wob;
    float qf = fract(a / 6.2831853);
    float pos = qf * arms;
    float armIdx = floor(pos);
    float s = pos - armIdx;
    float aaS = min(u_px * arms / (6.2831853 * max(rc, 0.02)), 0.45);
    cov = smoothstep(0.0, aaS, s) * (1.0 - smoothstep(duty, duty + aaS, s));
    if (u_glow > 0.001) {
      float k = mix(14.0, 4.0, u_glow);
      float halo = exp(-max(0.0, s - duty) * k) + exp(-(1.0 - s) * k);
      cov = min(1.0, cov + u_glow * 0.55 * halo * (1.0 - cov));
    }
    if (u_band${S} < 0.5) col = pick${S}(mod(armIdx, count));
    else col = blend${S}(fract(qf * arms / count) * count);
  } else if (style < 4.5) {
    // Tunnel: bands ride the radius; turns above the minimum shear them into a helix.
    float shift = u_rot${S} * arms / 6.2831853;
    // The helix shear is linear in th, and atan()'s branch cut (the -x axis)
    // makes th jump by 2*PI there - so pos jumps by the twist-per-revolution.
    // Snap that to a whole number of COLOUR CYCLES (mirrors symmetrySpanRad) or
    // fract()/the colour index don't line up and the cut shows a hard seam.
    // count == 1 (single thread) allows any integer twist.
    float helix = floor((u_turns${S} - 0.5) / count + 0.5) * count;
    float twist = helix * th / 6.2831853;
    float pos = t * arms - shift + twist + wob * 0.5;
    float s = fract(pos);
    float aaS = min(u_px * arms * 1.5, 0.45);
    cov = smoothstep(0.0, aaS, s) * (1.0 - smoothstep(duty, duty + aaS, s));
    if (u_glow > 0.001) {
      float k = mix(14.0, 4.0, u_glow);
      float halo = exp(-max(0.0, s - duty) * k) + exp(-(1.0 - s) * k);
      cov = min(1.0, cov + u_glow * 0.55 * halo * (1.0 - cov));
    }
    if (u_band${S} < 0.5) col = pick${S}(mod(floor(pos), count));
    else col = blend${S}(fract(pos / count) * count);
  } else {
    // Petal: a rose-curve mask that shears into spiral petals as turns rises.
    float a = th - u_rot${S} - 6.2831853 * u_turns${S} * t * 0.35 + wob;
    float m = 0.5 + 0.5 * cos(a * arms);
    float aaM = min(u_px * arms * 2.0, 0.45);
    float thr = 1.0 - duty;
    cov = smoothstep(thr - aaM, thr + aaM, m);
    cov *= smoothstep(0.03, 0.18, rc) * (1.0 - smoothstep(0.85, 1.0, rc));
    if (u_glow > 0.001) {
      float halo = smoothstep(thr - 0.35 * u_glow - aaM, thr, m);
      cov = min(1.0, cov + u_glow * 0.5 * halo * (1.0 - cov));
    }
    float petal = floor(fract(a / 6.2831853) * arms + 0.5);
    if (u_band${S} < 0.5) col = pick${S}(mod(petal, count));
    else col = blend${S}(fract(a / 6.2831853 * arms / count) * count);
  }

  return vec4(col, cov);
}
`;
}

const FRAG_SRC = `
precision mediump float;

uniform vec2 u_res;
uniform float u_phase;
uniform float u_px;
uniform float u_bgKind;
uniform vec3 u_bgA;
uniform vec3 u_bgB;
uniform float u_glow, u_pulseAmp, u_pulseCycles, u_wobAmp, u_wobFreq, u_wobCycles;
uniform float u_on2;
${layerGlsl('1')}
${layerGlsl('2')}

void main() {
  // top-left origin like the 2D canvas the frame is composited onto
  vec2 xy = vec2(gl_FragCoord.x, u_res.y - gl_FragCoord.y);
  vec2 c = 0.5 * u_res;
  // overdraw the corners like v1 drawSpiral: scale from the HALF-DIAGONAL so
  // any aspect (square / 16:9 / 9:16) keeps its corner inside r<=0.975 - the
  // shader's field is only defined to r=1. For a square this is EXACTLY the
  // old min(c)*1.45 (1.0253048 = 1.45/sqrt(2)), so existing spirals re-render
  // identically.
  float scale = length(c) * 1.0253048;
  vec2 p = (xy - c) / scale;
  float zoom = 1.0 + u_pulseAmp * sin(6.2831853 * u_phase * u_pulseCycles);
  float r = length(p) / zoom;
  float th = atan(p.y, p.x);

  vec3 col = (u_bgKind < 0.5) ? u_bgA : mix(u_bgA, u_bgB, clamp(r * 1.45, 0.0, 1.0));

  float wob = 0.0;
  if (u_wobAmp > 0.001) {
    wob = u_wobAmp * sin(6.2831853 * (u_wobFreq * clamp(r, 0.0, 1.0) + u_phase * u_wobCycles));
  }

  vec4 l1 = layer1(r, th, wob);
  col = mix(col, l1.rgb, l1.a);
  if (u_on2 > 0.5) {
    vec4 l2 = layer2(r, th, wob);
    col = mix(col, l2.rgb, l2.a * 0.85);
  }
  gl_FragColor = vec4(col, 1.0);
}
`;

function compile(gl, type, src) {
  const sh = gl.createShader(type);
  gl.shaderSource(sh, src);
  gl.compileShader(sh);
  if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) {
    const info = gl.getShaderInfoLog(sh);
    gl.deleteShader(sh);
    throw new Error('loom shader: ' + info);
  }
  return sh;
}

/**
 * WebGL renderer for the spiral field on the given canvas (HTMLCanvasElement
 * or OffscreenCanvas - page preview and worker encoder share this).
 * Returns null when WebGL is unavailable (caller falls back to v1 drawSpiral).
 */
export function createFieldRenderer(canvas) {
  const gl = canvas.getContext('webgl', {
    alpha: false, antialias: false, depth: false, stencil: false,
    preserveDrawingBuffer: true,   // the 2D composite drawImage()s us right after render
  });
  if (!gl) return null;

  const prog = gl.createProgram();
  gl.attachShader(prog, compile(gl, gl.VERTEX_SHADER, VERT_SRC));
  gl.attachShader(prog, compile(gl, gl.FRAGMENT_SHADER, FRAG_SRC));
  gl.linkProgram(prog);
  if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
    throw new Error('loom shader link: ' + gl.getProgramInfoLog(prog));
  }
  gl.useProgram(prog);

  const quad = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, quad);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
  const aPos = gl.getAttribLocation(prog, 'a_pos');
  gl.enableVertexAttribArray(aPos);
  gl.vertexAttribPointer(aPos, 2, gl.FLOAT, false, 0, 0);

  const U = {};
  const loc = (n) => (U[n] !== undefined ? U[n] : (U[n] = gl.getUniformLocation(prog, n)));

  function uploadLayer(S, layer, phase, hueRad) {
    const count = Math.max(1, Math.min(layer.colors.length, 6));
    gl.uniform1f(loc('u_arms' + S), layer.arms);
    gl.uniform1f(loc('u_turns' + S), layer.turns);
    gl.uniform1f(loc('u_duty' + S), layer.duty);
    gl.uniform1f(loc('u_style' + S), LOOM_STYLES.indexOf(layer.style));
    gl.uniform1f(loc('u_band' + S), layer.bandMode === 'gradient' ? 1 : 0);
    gl.uniform1f(loc('u_count' + S), count);
    gl.uniform1f(loc('u_rot' + S), layerRotationRad(layer, phase));
    const flat = new Float32Array(18);
    for (let i = 0; i < 6; i++) {
      const rgb = rotateHue(hexToRgb01(layer.colors[Math.min(i, count - 1)]), hueRad);
      flat[i * 3] = rgb[0]; flat[i * 3 + 1] = rgb[1]; flat[i * 3 + 2] = rgb[2];
    }
    gl.uniform3fv(loc('u_colors' + S + '[0]'), flat);
  }

  /** Draw the field for params q (MUST already be normalizeParams2'd) at phase 0..1. */
  function render(q, phase) {
    const w = canvas.width, h = canvas.height;
    gl.viewport(0, 0, w, h);
    gl.useProgram(prog);
    const hueRad = q.hueCycles > 0 ? TWO_PI * q.hueCycles * phase : 0;
    const bgA = hexToRgb01(q.bg.color);
    const bgB = hexToRgb01(q.bg.kind === 'radial' ? q.bg.outer : q.bg.color);
    gl.uniform2f(loc('u_res'), w, h);
    gl.uniform1f(loc('u_phase'), phase);
    gl.uniform1f(loc('u_px'), 2 / Math.max(1, Math.min(w, h)));
    gl.uniform1f(loc('u_bgKind'), q.bg.kind === 'radial' ? 1 : 0);
    gl.uniform3f(loc('u_bgA'), bgA[0], bgA[1], bgA[2]);
    gl.uniform3f(loc('u_bgB'), bgB[0], bgB[1], bgB[2]);
    gl.uniform1f(loc('u_glow'), q.glow);
    gl.uniform1f(loc('u_pulseAmp'), q.pulse.amp);
    gl.uniform1f(loc('u_pulseCycles'), q.pulse.cycles);
    gl.uniform1f(loc('u_wobAmp'), q.wobble.amp);
    gl.uniform1f(loc('u_wobFreq'), q.wobble.freq);
    gl.uniform1f(loc('u_wobCycles'), q.wobble.cycles);
    gl.uniform1f(loc('u_on2'), q.layer2.enabled ? 1 : 0);
    uploadLayer('1', q.layer, phase, hueRad);
    uploadLayer('2', q.layer2, phase, hueRad);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }

  return { canvas, render, gl };
}

// ---- centerpiece + composite --------------------------------------------

/** Same font string in the pane preview AND the worker so preview == file.
 *  (No webfont in the bundle - Segoe UI is guaranteed on this Windows-only app.) */
const CP_FONT = (px) => `600 ${px}px "Segoe UI", sans-serif`;

/** Five-pointed star, outer radius r, point-up, filled solid in `color`. */
function drawStar(ctx, cx, cy, r, color) {
  const inner = r * 0.42;
  ctx.fillStyle = color;
  ctx.beginPath();
  for (let i = 0; i < 10; i++) {
    const rad = i % 2 === 0 ? r : inner;
    const a = -Math.PI / 2 + (i * Math.PI) / 5;
    const x = cx + rad * Math.cos(a), y = cy + rad * Math.sin(a);
    if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
  }
  ctx.closePath();
  ctx.fill();
}

/** Two crossed bars: angle 0 draws a '+' cross, angle PI/4 draws an 'X'. */
function drawBars(ctx, cx, cy, r, color, angle) {
  const half = r, t = r * 0.32;   // arm reach / half-thickness
  ctx.save();
  ctx.translate(cx, cy);
  ctx.rotate(angle);
  ctx.fillStyle = color;
  ctx.fillRect(-half, -t, half * 2, t * 2);
  ctx.fillRect(-t, -half, t * 2, half * 2);
  ctx.restore();
}

/** Draw the dot/star/cross/x/mantra centerpiece over a composited frame (2D
 *  context). `height` defaults to `size` (square); non-square frames pass both. */
export function drawCenterpiece(ctx, q, phase, size, height = size) {
  const cp = q.centerpiece;
  if (cp.kind === 'none') return;
  const cx = size / 2, cy = height / 2;
  const r = (cp.sizeFrac * Math.min(size, height)) / 2;
  const hueRad = q.hueCycles > 0 ? TWO_PI * q.hueCycles * phase : 0;
  const color = rgb01ToCss(rotateHue(hexToRgb01(cp.color), hueRad));

  if (cp.kind === 'dot') {
    ctx.fillStyle = color;
    ctx.beginPath(); ctx.arc(cx, cy, r, 0, TWO_PI); ctx.fill();
  } else if (cp.kind === 'star') {
    drawStar(ctx, cx, cy, r, color);
  } else if (cp.kind === 'cross') {
    drawBars(ctx, cx, cy, r, color, 0);
  } else if (cp.kind === 'x') {
    drawBars(ctx, cx, cy, r, color, Math.PI / 4);
  } else if (cp.kind === 'mantra' && cp.text) {
    const flash = cp.flashCycles > 0 ? 0.5 * (1 + Math.cos(TWO_PI * phase * cp.flashCycles)) : 1;
    if (flash <= 0.01) return;
    ctx.save();
    ctx.globalAlpha = flash;
    ctx.fillStyle = color;
    ctx.font = CP_FONT(Math.max(8, Math.round(r)));
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(cp.text, cx, cy);
    ctx.restore();
  }
}

/**
 * One complete frame: WebGL field -> 2D composite -> centerpiece. `field` is a
 * createFieldRenderer() whose canvas already matches the target aspect (the
 * shader renders any aspect natively). q MUST be normalized (normalizeParams2).
 * `height` defaults to `size` so square callers stay unchanged.
 */
export function composeFrame(ctx, field, q, phase, size, height = size) {
  field.render(q, phase);
  ctx.drawImage(field.canvas, 0, 0, size, height);
  drawCenterpiece(ctx, q, phase, size, height);
}

/**
 * No-WebGL fallback frame at any aspect. drawSpiral only knows squares, so
 * non-square frames render a max(w,h) square offscreen and crop its center
 * band. Preview and worker BOTH use this, so even the fallback stays
 * preview == file.
 */
let _fbSquare = null;
export function drawFallbackFrame(ctx, q, phase, w, h = w) {
  const v1 = projectToV1(q);
  if (w === h) {
    drawSpiral(ctx, w, v1, phase);
  } else {
    const side = Math.max(w, h);
    if (!_fbSquare || _fbSquare.width !== side) {
      _fbSquare = typeof OffscreenCanvas !== 'undefined'
        ? new OffscreenCanvas(side, side)
        : document.createElement('canvas');
      _fbSquare.width = side; _fbSquare.height = side;
    }
    const sctx = _fbSquare.getContext('2d');
    drawSpiral(sctx, side, v1, phase);
    ctx.drawImage(_fbSquare, (side - w) / 2, (side - h) / 2, w, h, 0, 0, w, h);
  }
  drawCenterpiece(ctx, q, phase, w, h);
}

/**
 * Project v2 params down to something loomSpiral.js drawSpiral can render -
 * the no-WebGL fallback (layer 1 only, first two threads, closest v1 style).
 * Preview and worker both use this so even the fallback stays preview == file.
 */
export function projectToV1(q) {
  const styleMap = { log: 'log', arch: 'arch', ribbon: 'ribbon', golden: 'log', tunnel: 'arch', petal: 'ribbon' };
  return {
    schema: 1,
    arms: clamp(q.layer.arms, 2, 8),
    turns: clamp(q.layer.turns, 0.5, 4),
    style: styleMap[q.layer.style] || 'log',
    duty: q.layer.duty,
    speed: q.speed,
    direction: q.layer.direction,
    colors: q.layer.colors.slice(0, 2),
    bg: q.bg.color,
  };
}

// ---- "surprise me" -------------------------------------------------------

const pickFrom = (arr) => arr[Math.floor(Math.random() * arr.length)];

function threads(count, palette) {
  const pool = palette.slice().sort(() => Math.random() - 0.5);
  return pool.slice(0, count);
}

/** A random but tasteful creation (the 🎲 button). */
export function randomParams2(palette = LOOM_SWATCHES) {
  const d = defaultParams2();
  d.layer.arms = 1 + Math.floor(Math.random() * 8);
  d.layer.turns = clamp(snap(0.5 + Math.random() * 3.5, 0.25), 0.5, 6);
  d.layer.duty = clamp(snap(0.3 + Math.random() * 0.4, 0.05), 0.2, 0.8);
  d.layer.style = pickFrom(LOOM_STYLES);
  d.layer.direction = Math.random() < 0.5 ? 1 : -1;
  d.layer.colors = threads(1 + Math.floor(Math.random() * 3), palette);
  d.layer.bandMode = Math.random() < 0.35 ? 'gradient' : 'hard';
  d.layer2.enabled = Math.random() < 0.35;
  d.layer2.arms = 2 + Math.floor(Math.random() * 5);
  d.layer2.turns = clamp(snap(0.5 + Math.random() * 2.5, 0.25), 0.5, 6);
  d.layer2.colors = threads(1, palette);
  d.layer2.direction = -d.layer.direction;
  d.speed = 2 + Math.floor(Math.random() * 3);
  d.glow = Math.random() < 0.4 ? 0.3 + Math.random() * 0.5 : 0;
  if (Math.random() < 0.35) d.pulse = { amp: 0.05 + Math.random() * 0.12, cycles: 1 + Math.floor(Math.random() * 2) };
  if (Math.random() < 0.3) d.wobble = { amp: 0.08 + Math.random() * 0.15, freq: 2 + Math.floor(Math.random() * 3), cycles: 1 };
  if (Math.random() < 0.2) d.hueCycles = 1;
  if (Math.random() < 0.25) {
    d.centerpiece.kind = pickFrom(['dot', 'star', 'cross', 'x']);
    d.centerpiece.color = pickFrom(palette);
  }
  return normalizeParams2(d);
}
