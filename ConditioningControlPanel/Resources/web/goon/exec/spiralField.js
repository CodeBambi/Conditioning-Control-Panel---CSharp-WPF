/* ============================================================================
 * exec/spiralField.js — THE LOOM's spiral, rendered LIVE instead of stretched.
 *
 * WHY THIS EXISTS (the grain, and where it really came from).
 * The fullscreen spiral bed used to be a bundled raster: sp1..sp6 under
 * /dtrh/assets/bubbles/effects/spirals/, 360x360..720x720, palettes as thin as
 * 8 and 16 colours, blown up to COVER the window and then multiplied again by
 * `.gg-spiral { scale: 1.6 }` — an effective 5-8x magnification. Every dither
 * dot became a 6px block, `mix-blend-mode: screen` lifted that noise straight
 * out of the dark field it hides in, and the result read as crawling GRAIN.
 * `filter: blur(1.1px)` was a band-aid over exactly that.
 *
 * Those files were NEVER Loom output — they pre-date the Loom by five days and
 * two of them are WebP, a format the Loom cannot emit. But the Loom itself is
 * not a pile of GIFs: it is a PER-PIXEL FRAGMENT SHADER with analytic
 * antialiasing (dtrh/shared/loomField.js), and the GIF is only what it bakes
 * for export. So the spiral the owner thought was here really does exist —
 * it just lives in the renderer, not on disk.
 *
 * This module is that renderer, ported into goon's own tree: the schema-v2
 * field shader copied verbatim (the math IS the product) with the export half
 * — gifenc, the Bayer dither, the palette policy, the studio UI, the sidecar
 * store — all left behind. It draws at the window's own pixel ratio, so there
 * is no magnification, no palette, no dither and nothing to blur away.
 *
 * NO CROSS-IMPORT, BY CONSTRUCTION. Nothing here reaches into dtrh/. The Loom's
 * consumer module (dtrh/engine/loomSpirals.js) drags DtRH settings + the Loom's
 * saved-spiral storage in with it; goon is offline-by-construction and must
 * never read the player's Loom library. The presets below are authored HERE, in
 * goon's palette, and are the whole content surface.
 *
 * PRESETS AND THEIR SPIN. Rotation is seamless by construction: a layer moves
 * exactly one symmetry span per loop, so revolution time is
 *   revMs = loopMs * arms / (arms % colors === 0 ? colors : arms) / speedMul
 * Every preset below is tuned into a 10-18s revolution — the band the CSS
 * `ggSpiralSpin 14s` keyframe used to sit in, so the bed feels unchanged.
 *
 * The raster pool is NOT deleted: exec/spiral.js still falls back to it when
 * WebGL is unavailable or the context is lost, and exec/bubbles.js still flicks
 * a bundled still on a popped spiral bubble. This module is the upgrade path,
 * not a replacement for the floor.
 * ==========================================================================*/

/* ---------------------------------------------------------------------------
 * TIMING — the Loom's tables. Kept because they define the seamless loop, not
 * because anything here writes a GIF: phase = (elapsed % loopMs) / loopMs.
 * ------------------------------------------------------------------------ */
const DELAY_CS = { 1: 5, 2: 4, 3: 3, 4: 2, 5: 2 };
const FRAME_TABLE = { 1: 72, 2: 63, 3: 60, 4: 54, 5: 36 };
const TWO_PI = Math.PI * 2;

const STYLES = ['log', 'arch', 'ribbon', 'golden', 'tunnel', 'petal'];

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const HEX_RE = /^#[0-9a-f]{6}$/i;
const hex = (v, fb) => (typeof v === 'string' && HEX_RE.test(v) ? v.toLowerCase() : fb);
const num = (v, fb) => (typeof v === 'number' && isFinite(v) ? v : fb);

/* ---------------------------------------------------------------------------
 * PARAMS — a trimmed schema v2. Same field names and same clamps as the Loom
 * (a preset here would normalize identically over there), minus `format` and
 * `centerpiece`: the bed is always the window's aspect and a mantra/dot in the
 * middle of a screen-blended wash is a smear, not a focal point.
 * ------------------------------------------------------------------------ */
function normalizeLayer(raw, fallbackColor) {
  const q = raw && typeof raw === 'object' ? raw : {};
  const colors = [];
  if (Array.isArray(q.colors)) {
    for (const c of q.colors) {
      const h = hex(c, null);
      if (h && !colors.includes(h)) colors.push(h);
      if (colors.length >= 6) break;
    }
  }
  if (!colors.length) colors.push(fallbackColor);
  return {
    arms: clamp(Math.round(num(q.arms, 4)), 1, 12),
    turns: clamp(num(q.turns, 2), 0.5, 6),
    duty: clamp(num(q.duty, 0.5), 0.2, 0.8),
    style: STYLES.includes(q.style) ? q.style : 'log',
    direction: q.direction === -1 ? -1 : 1,
    colors,
    bandMode: q.bandMode === 'gradient' ? 'gradient' : 'hard',
    speedMul: clamp(Math.round(num(q.speedMul, 1)), 1, 3),
  };
}

/** Coerce a preset (or junk) into a valid, fully-populated param set. */
export function normalizeSpiralParams(p) {
  const q = p && typeof p === 'object' ? p : {};
  const bg = q.bg && typeof q.bg === 'object' ? q.bg : {};
  const pulse = q.pulse && typeof q.pulse === 'object' ? q.pulse : {};
  const wobble = q.wobble && typeof q.wobble === 'object' ? q.wobble : {};
  const l2 = q.layer2 && typeof q.layer2 === 'object' ? q.layer2 : {};
  return {
    layer: normalizeLayer(q.layer, '#ff69b4'),
    layer2: Object.assign(normalizeLayer(l2, '#8a5cff'), { enabled: l2.enabled === true }),
    speed: clamp(Math.round(num(q.speed, 1)), 1, 5),
    bg: {
      kind: bg.kind === 'radial' ? 'radial' : 'solid',
      color: hex(bg.color, '#050208'),
      outer: hex(bg.outer, '#050208'),
    },
    glow: clamp(num(q.glow, 0), 0, 1),
    pulse: { amp: clamp(num(pulse.amp, 0), 0, 0.25), cycles: clamp(Math.round(num(pulse.cycles, 1)), 1, 4) },
    wobble: {
      amp: clamp(num(wobble.amp, 0), 0, 0.35),
      freq: clamp(Math.round(num(wobble.freq, 3)), 1, 6),
      cycles: clamp(Math.round(num(wobble.cycles, 1)), 1, 3),
    },
    hueCycles: 0,   // deliberately pinned off: a hue sweep on a screen-blended
                    // fullscreen bed reads as a colour strobe, not a spiral.
  };
}

/** One seamless loop in ms. */
export function loopMsFor(q) {
  const s = clamp(Math.round(num(q && q.speed, 1)), 1, 5);
  return (FRAME_TABLE[s] || 72) * (DELAY_CS[s] || 5) * 10;
}

/** Smallest rotation that maps a layer onto itself (1-6 threads). */
function symmetrySpanRad(layer) {
  const n = Math.max(1, Math.min(layer.colors.length, 6));
  return (TWO_PI / layer.arms) * (layer.arms % n === 0 ? n : layer.arms);
}

function layerRotationRad(layer, phase) {
  return layer.direction * phase * symmetrySpanRad(layer) * layer.speedMul;
}

/** Seconds per full revolution — the number every preset below is tuned on. */
export function revolutionSecFor(q) {
  const p = normalizeSpiralParams(q);
  return (loopMsFor(p) / (symmetrySpanRad(p.layer) / TWO_PI) / p.layer.speedMul) / 1000;
}

/* ---------------------------------------------------------------------------
 * THE POOL — six woven spirals in goon's palette, one per retired raster.
 *
 * DUTY IS THE COVERAGE DIAL, AND IT IS TUNED LOW ON PURPOSE. This is a BED: it
 * screen-blends over live gameplay at up to 0.70, so it has to read as a spiral
 * without painting over the match. The rasters it replaces were thin bright
 * arms on a mostly-black field; `duty` (the lit fraction of each arm band) is
 * what reproduces that, and every preset sits at 0.26-0.34 rather than the
 * Loom's 0.5 default. Raise the ARM COUNT, not the duty, to make one busier.
 *
 * Comments carry the tuned revolution time (see the banner's revMs formula).
 * ------------------------------------------------------------------------ */
export const SPIRAL_PRESETS = Object.freeze([
  // silk — the classic: four soft pink arms opening to the rim. 14.4s/rev.
  {
    speed: 1, glow: 0.3,
    layer: { arms: 4, turns: 2.25, duty: 0.3, style: 'log', direction: 1, colors: ['#ff69b4'] },
    bg: { kind: 'radial', color: '#0a0410', outer: '#04010a' },
  },
  // ribbon — five swelling bands, a slow breath under them. 18s/rev.
  {
    speed: 1, glow: 0.2, pulse: { amp: 0.06, cycles: 1 },
    layer: { arms: 5, turns: 1.75, duty: 0.28, style: 'ribbon', direction: -1, colors: ['#e56cc0'] },
    bg: { kind: 'solid', color: '#080312' },
  },
  // twin — pink/violet candy stripe with a counter-rotating cyan weave. 10.8s/rev.
  {
    speed: 1, glow: 0.28,
    layer: { arms: 6, turns: 2.5, duty: 0.28, style: 'log', direction: 1, colors: ['#ff69b4', '#8a5cff'] },
    layer2: { enabled: true, arms: 3, turns: 1.5, duty: 0.2, style: 'arch', direction: -1, colors: ['#00e5ff'], speedMul: 1 },
    bg: { kind: 'radial', color: '#0a0412', outer: '#030108' },
  },
  // golden — constant-pitch growth, gradient-blended, faintly liquid. 10.8s/rev.
  // 6 arms, not 3: an ODD arm count with 2 threads has no sub-2PI symmetry, so
  // the loop has to cover a whole turn and the bed spins 3x too fast (3.6s).
  // Keep `arms % colors.length === 0` on every multi-thread preset.
  {
    speed: 1, glow: 0.4, wobble: { amp: 0.09, freq: 3, cycles: 1 },
    layer: { arms: 6, turns: 3, duty: 0.3, style: 'golden', direction: 1, colors: ['#ff69b4', '#8a5cff'], bandMode: 'gradient' },
    bg: { kind: 'solid', color: '#070310' },
  },
  // tunnel — rings riding the radius, sheared into a helix. Rushes inward. 14.4s/rev.
  {
    speed: 1, glow: 0.22,
    layer: { arms: 8, turns: 2, duty: 0.26, style: 'tunnel', direction: -1, colors: ['#8a5cff', '#ff69b4'] },
    bg: { kind: 'radial', color: '#0a0414', outer: '#020106' },
  },
  // petal — a rose sheared into blades; the softest of the six. 14.4s/rev.
  // 8 arms at speed 3, not 5 at speed 1: petals are WIDE, so a low arm count
  // reads as a pinwheel rather than a spiral, and 8 arms at speed 1 would crawl
  // round in 28.8s. This pairing lands back on the same 14.4s as silk.
  {
    speed: 3, glow: 0.45, pulse: { amp: 0.08, cycles: 1 },
    layer: { arms: 8, turns: 3, duty: 0.34, style: 'petal', direction: 1, colors: ['#ff8fd0'] },
    bg: { kind: 'solid', color: '#060210' },
  },
].map(normalizeSpiralParams));

/** A random woven spiral. The procedural twin of exec/spiral.js#pickSpiralUrl. */
export function pickSpiralParams() {
  return SPIRAL_PRESETS[(Math.random() * SPIRAL_PRESETS.length) | 0];
}

/* ---------------------------------------------------------------------------
 * THE SHADER — copied from the Loom's field (dtrh/shared/loomField.js) so the
 * bed IS a Loom spiral rather than a lookalike. Only the export half was left
 * behind. Do not "tidy" the branch-cut snap in `tunnel` or the half-diagonal
 * scale: both are load-bearing (see the comments in place).
 * ------------------------------------------------------------------------ */
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
  // top-left origin like the 2D canvas the Loom composites onto
  vec2 xy = vec2(gl_FragCoord.x, u_res.y - gl_FragCoord.y);
  vec2 c = 0.5 * u_res;
  // Scale from the HALF-DIAGONAL so ANY aspect - and the bed is whatever shape
  // the window is - keeps its corners inside r<=0.975. The field is only defined
  // to r=1; this is what replaces the old .gg-spiral scale:1.6 overscan, and
  // unlike that transform it costs no resolution.
  // (NO BACKTICKS IN HERE - this whole shader is a template literal.)
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

function hexToRgb01(h) {
  const n = parseInt(h.slice(1), 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}

function compile(gl, type, src) {
  const sh = gl.createShader(type);
  gl.shaderSource(sh, src);
  gl.compileShader(sh);
  if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) {
    const info = gl.getShaderInfoLog(sh);
    gl.deleteShader(sh);
    throw new Error('spiral shader: ' + info);
  }
  return sh;
}

/** WebGL's own CONTEXT_LOST_WEBGL, spelled out: on a lost context the enum is
 *  still readable off `gl`, but a context we have ALREADY lost the reference to
 *  is exactly the case where reading a property off it is least trustworthy. */
export const CONTEXT_LOST_WEBGL = 0x9242;

/**
 * The field renderer on a canvas. Returns null on ANY host that cannot run it
 * — no canvas element, no getContext, no WebGL, a shader that will not compile
 * — because exec/spiral.js's raster pool is a perfectly good floor and a thrown
 * error inside a renderer would take the whole effect tier down with it.
 *
 * @returns {{render(q:object, phase:number):void, lost():boolean, dispose():void, gl:object}|null}
 */
export function createSpiralField(canvas) {
  if (!canvas || typeof canvas.getContext !== 'function') return null;

  let gl = null;
  try {
    gl = canvas.getContext('webgl', {
      alpha: false, antialias: false, depth: false, stencil: false,
      // No preserveDrawingBuffer: unlike the Loom nothing reads this canvas
      // back, and letting the driver discard is materially cheaper per frame.
      preserveDrawingBuffer: false,
    }) || canvas.getContext('experimental-webgl');
  } catch (_e) { return null; }
  if (!gl) return null;

  let prog = null;
  try {
    prog = gl.createProgram();
    gl.attachShader(prog, compile(gl, gl.VERTEX_SHADER, VERT_SRC));
    gl.attachShader(prog, compile(gl, gl.FRAGMENT_SHADER, FRAG_SRC));
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
      throw new Error('spiral shader link: ' + gl.getProgramInfoLog(prog));
    }
  } catch (_e) { return null; }
  gl.useProgram(prog);

  const quad = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, quad);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
  const aPos = gl.getAttribLocation(prog, 'a_pos');
  gl.enableVertexAttribArray(aPos);
  gl.vertexAttribPointer(aPos, 2, gl.FLOAT, false, 0, 0);

  const U = new Map();
  const loc = (n) => {
    if (!U.has(n)) U.set(n, gl.getUniformLocation(prog, n));
    return U.get(n);
  };
  const flat = new Float32Array(18);

  function uploadLayer(S, layer, phase) {
    const count = Math.max(1, Math.min(layer.colors.length, 6));
    gl.uniform1f(loc('u_arms' + S), layer.arms);
    gl.uniform1f(loc('u_turns' + S), layer.turns);
    gl.uniform1f(loc('u_duty' + S), layer.duty);
    gl.uniform1f(loc('u_style' + S), STYLES.indexOf(layer.style));
    gl.uniform1f(loc('u_band' + S), layer.bandMode === 'gradient' ? 1 : 0);
    gl.uniform1f(loc('u_count' + S), count);
    gl.uniform1f(loc('u_rot' + S), layerRotationRad(layer, phase));
    for (let i = 0; i < 6; i++) {
      const rgb = hexToRgb01(layer.colors[Math.min(i, count - 1)]);
      flat[i * 3] = rgb[0]; flat[i * 3 + 1] = rgb[1]; flat[i * 3 + 2] = rgb[2];
    }
    gl.uniform3fv(loc('u_colors' + S + '[0]'), flat);
  }

  let dead = false;

  /** Draw the field for normalized params q at phase 0..1. */
  function render(q, phase) {
    if (dead) return;
    const w = canvas.width, h = canvas.height;
    if (!(w > 0 && h > 0)) return;
    gl.viewport(0, 0, w, h);
    gl.useProgram(prog);
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
    uploadLayer('1', q.layer, phase);
    uploadLayer('2', q.layer2, phase);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }

  /**
   * Has the driver taken the context away?
   *
   * THIS IS THE CHEAP HALF OF THE FREEZE DEFENCE. `webglcontextlost` is an EVENT,
   * and an event needs a live event loop to be delivered on — the exact thing a
   * compositor/GPU stall makes unreliable. Polling costs one enum read and, once
   * a second from inside the render loop (exec/spiral.js), catches a context that
   * went away without anybody telling us.
   *
   * isContextLost() is the direct question and is preferred; getError() is the
   * fallback, and note that it CLEARS the error queue — which is fine, because
   * nothing else in this module reads errors, but it is why this must never be
   * called per frame.
   */
  function lost() {
    if (dead) return true;
    try {
      if (typeof gl.isContextLost === 'function') return !!gl.isContextLost();
      return gl.getError() === CONTEXT_LOST_WEBGL;
    } catch (_e) {
      return true;   // a context that throws on being asked is not a working one
    }
  }

  /** Hand the GPU its memory back; the pane goes home to the raster pool. */
  function dispose() {
    if (dead) return;
    dead = true;
    try {
      gl.deleteBuffer(quad);
      gl.deleteProgram(prog);
      const lose = gl.getExtension('WEBGL_lose_context');
      if (lose) lose.loseContext();
    } catch (_e) { /* the context is already gone, which is the goal */ }
  }

  return { render, lost, dispose, gl };
}

export default createSpiralField;
