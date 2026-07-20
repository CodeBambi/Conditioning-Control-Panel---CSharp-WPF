/* ============================================================================
 * background.js — Agent F. The ambient tube behind everything (canvas #intake-bg).
 *
 * A SIMPLIFIED DtRH-style tube (a low-poly cylinder seen from the inside, with a
 * scrolling ring + spiral shader) whose tint and scroll speed ride the single
 * `depth` scalar via the contract's depth->channel map (`bgIntensity`). It sits
 * behind the beat stage and NEVER touches the beat loop: it owns its own rAF and
 * is a pure sink for `setDepth`.
 *
 * PHONE-SAFE + GRACEFUL:
 *   - three.js is imported LAZILY (dynamic import inside the async init) so a
 *     missing/failed vendor bundle can never break module load or the page.
 *   - WebGL capability is probed first; on no/weak WebGL (or reduced-motion) we
 *     fall back to a cheap animated 2D-canvas tunnel — never blank, never throw.
 *   - Mobile tier caps DPR harder, thins the geometry, and paces to ~30fps.
 *
 * CONTRACT (contracts.js "BACKGROUND (Agent F)"):
 *   createBackground({ canvas }) -> { setDepth(depth), setEnabled(bool), dispose() }
 *
 * The ONLY top-level import is the pure contract module (no DOM, no three), so
 * importing this file is always safe. Everything heavy happens at runtime.
 * ==========================================================================*/

import { depthToChannels } from '../core/contracts.js';

/* --- tiny local helpers (kept self-contained; no DtRH deps) ---------------- */

// depth (0..1) -> the background channel magnitude (0..1). Single source of
// truth is the contract; we only read `.bgIntensity` so the curve can't drift.
function bgIntensityFor(depth) {
  try {
    const ch = depthToChannels(depth);
    const v = ch && typeof ch.bgIntensity === 'number' ? ch.bgIntensity : 0;
    return v < 0 ? 0 : v > 1 ? 1 : v;
  } catch (_e) {
    // Contract shim / stub could throw; degrade to a linear read rather than die.
    const d = depth < 0 ? 0 : depth > 1 ? 1 : depth;
    return 0.2 + d * 0.8;
  }
}

// Cheap, dependency-free WebGL probe on a throwaway canvas (never uses the real
// one, so a failed probe leaves #intake-bg pristine for the 2D fallback).
function hasWebGL() {
  try {
    const c = document.createElement('canvas');
    return !!(window.WebGLRenderingContext &&
      (c.getContext('webgl2') || c.getContext('webgl') || c.getContext('experimental-webgl')));
  } catch (_e) {
    return false;
  }
}

function prefersReducedMotion() {
  try { return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches); }
  catch (_e) { return false; }
}

// Mobile-ish tier: coarse pointer OR a small viewport. Only used to pick lighter
// knobs; the tube look survives either way.
function detectTier() {
  try {
    const coarse = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
    const small = Math.min(window.innerWidth || 1024, window.innerHeight || 768) < 720;
    return (coarse || small) ? 'mobile' : 'desktop';
  } catch (_e) {
    return 'desktop';
  }
}

const clamp01 = (n) => (n < 0 ? 0 : n > 1 ? 1 : n);

/* --- the shader (simplified from dtrh/engine/tunnel.js) --------------------
 * Rings + a single helical spiral, both scrolling; tint lerps two palettes by
 * uIntensity (calm violet -> hot pink); a distance fade hides the far end so we
 * don't need scene.fog. Integer ring/turn counts keep the wrap seam-free even
 * though this is a straight (not looped) tube — the scroll just recycles v. */
const VERT = `
  varying vec2 vUv;
  varying float vFog;
  void main() {
    vUv = uv;
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    vFog = -mv.z;
    gl_Position = projectionMatrix * mv;
  }`;

const FRAG = `
  precision mediump float;
  varying vec2 vUv;
  varying float vFog;
  uniform float uScroll;     // accumulated scroll phase (units)
  uniform float uIntensity;  // 0..1 depth-derived tint/glow
  uniform float uTime;
  uniform vec3  uBgCalm, uBgHot, uLineCalm, uLineHot, uSpiralCalm, uSpiralHot, uFog;
  uniform float uRings, uTurns, uArms, uFar;

  // 1.0 on an integer coord, fading within half-width w (screen-space AA-ish).
  float lineMask(float coord, float w) {
    float di = 0.5 - abs(fract(coord) - 0.5);
    float aa = clamp(1.5 * fwidth(coord), w, 0.5);
    return 1.0 - smoothstep(0.0, aa, di);
  }
  float pulse(float p, float k) { return pow(0.5 + 0.5 * sin(p), k); }

  void main() {
    float len = vUv.y;      // along the tube
    float around = vUv.x;   // around the circumference

    vec3 base   = mix(uBgCalm,   uBgHot,   uIntensity);
    vec3 lineC  = mix(uLineCalm, uLineHot, uIntensity);
    vec3 spirC  = mix(uSpiralCalm, uSpiralHot, uIntensity);

    // gentle tint variation around the bore
    vec3 col = base * (0.85 + 0.15 * sin((around + 0.25) * 6.2831));

    float ringCoord   = len * uRings - uScroll;
    float spiralCoord = around * uArms + len * uTurns - uScroll;

    float ring   = lineMask(ringCoord, 0.06);
    float spiral = lineMask(spiralCoord, 0.05);

    // intermittent glow, faster + hotter as depth climbs
    float gr = 0.25 + 0.75 * uIntensity;
    float ringGlow   = 0.4 + 1.4 * pulse(ringCoord * 0.5 - uTime * 1.4 * gr, 3.0);
    float spiralGlow = 0.5 + 1.6 * pulse(spiralCoord * 0.4 - uTime * 1.9 * gr, 3.0);
    float glow = 0.4 + 0.9 * uIntensity;

    col += lineC * ring   * ringGlow   * glow;
    col += spirC * spiral * spiralGlow * glow;

    // distance fade to the fog color so the far end melts into the page bg
    float f = clamp(vFog / uFar, 0.0, 1.0);
    col = mix(col, uFog, f * f);

    gl_FragColor = vec4(col, 1.0);
  }`;

/* --- palette (JS-side THREE.Color built at init) --------------------------- */
const PALETTE = {
  bgCalm:     [0x14, 0x10, 0x22],
  bgHot:      [0x2b, 0x0f, 0x24],
  lineCalm:   [0x6a, 0x4a, 0x9a],
  lineHot:    [0xff, 0x69, 0xb4],
  spiralCalm: [0x8a, 0x6c, 0xc0],
  spiralHot:  [0xb0, 0x6c, 0xff],
  fog:        [0x14, 0x10, 0x1f], // matches styles.css --intake-bg0
};

/* ==========================================================================
 * FACTORY
 * ========================================================================== */
export function createBackground({ canvas }) {
  // --- shared state ---------------------------------------------------------
  let enabled = false;
  let disposed = false;
  let mode = 'pending';        // 'pending' | 'webgl' | '2d' | 'dead'
  let starting = false;        // an async init is in flight

  let rafId = 0;
  let running = false;         // the rAF loop is live
  let lastT = 0;               // last frame timestamp (ms)
  let scroll = 0;              // accumulated scroll phase

  let targetIntensity = bgIntensityFor(0);
  let curIntensity = targetIntensity;

  const tier = detectTier();
  const reduced = prefersReducedMotion();
  const dprCap = tier === 'mobile' ? 1.5 : 2;
  const minFrameMs = tier === 'mobile' ? 30 : 0; // ~30fps pace on phones

  // WebGL bucket (populated by initWebGL)
  let three = null, renderer = null, scene = null, camera = null, mat = null, geo = null, mesh = null;
  // 2D bucket
  let ctx2d = null;

  // --- resize handling ------------------------------------------------------
  function currentDpr() {
    const d = (window.devicePixelRatio || 1);
    return Math.min(dprCap, Math.max(1, d));
  }
  function sizeWebGL() {
    if (!renderer) return;
    const w = window.innerWidth || 1, h = window.innerHeight || 1;
    renderer.setPixelRatio(currentDpr());
    renderer.setSize(w, h, false);
    if (camera) { camera.aspect = w / h; camera.updateProjectionMatrix(); }
  }
  function size2D() {
    const dpr = currentDpr();
    const w = window.innerWidth || 1, h = window.innerHeight || 1;
    canvas.width = Math.max(1, Math.round(w * dpr));
    canvas.height = Math.max(1, Math.round(h * dpr));
  }
  function onResize() {
    if (disposed) return;
    if (mode === 'webgl') sizeWebGL();
    else if (mode === '2d') size2D();
  }
  window.addEventListener('resize', onResize);

  // --- WebGL init (lazy three import) --------------------------------------
  async function initWebGL() {
    // Dynamic import: a missing/broken vendor bundle throws HERE (caught below)
    // and routes to the 2D fallback — module load is never at risk.
    const THREE = await import('three');
    if (disposed) return false;
    three = THREE;

    renderer = new THREE.WebGLRenderer({ canvas, antialias: tier !== 'mobile', alpha: true, powerPreference: 'high-performance' });
    renderer.setClearColor(0x000000, 0); // let the CSS gradient show behind
    sizeWebGL();

    scene = new THREE.Scene();
    camera = new THREE.PerspectiveCamera(78, (window.innerWidth || 1) / (window.innerHeight || 1), 0.1, 220);
    camera.position.set(0, 0, 0);
    camera.lookAt(0, 0, -1);

    const radial = tier === 'mobile' ? 18 : 32; // low-poly; shader does the detail
    const RADIUS = 5.2, LENGTH = 140;
    // Cylinder is along Y by default; rotate so its length runs down -Z.
    geo = new THREE.CylinderGeometry(RADIUS, RADIUS, LENGTH, radial, 1, true);
    geo.rotateX(Math.PI / 2);
    geo.translate(0, 0, -LENGTH / 2 + 6); // near lip just ahead of the camera

    const C = (rgb) => new THREE.Color(rgb[0] / 255, rgb[1] / 255, rgb[2] / 255);
    mat = new THREE.ShaderMaterial({
      uniforms: {
        uScroll: { value: 0 },
        uIntensity: { value: curIntensity },
        uTime: { value: 0 },
        uBgCalm: { value: C(PALETTE.bgCalm) },
        uBgHot: { value: C(PALETTE.bgHot) },
        uLineCalm: { value: C(PALETTE.lineCalm) },
        uLineHot: { value: C(PALETTE.lineHot) },
        uSpiralCalm: { value: C(PALETTE.spiralCalm) },
        uSpiralHot: { value: C(PALETTE.spiralHot) },
        uFog: { value: C(PALETTE.fog) },
        uRings: { value: 22 },   // integer -> seam-free on the scroll recycle
        uTurns: { value: 6 },    // integer
        uArms: { value: 3.0 },
        uFar: { value: LENGTH * 0.85 },
      },
      vertexShader: VERT,
      fragmentShader: FRAG,
      side: THREE.BackSide,
      depthWrite: false,
      extensions: { derivatives: true }, // fwidth() (core on WebGL2; safety net on WebGL1)
    });
    mesh = new THREE.Mesh(geo, mat);
    scene.add(mesh);
    return true;
  }

  // --- 2D fallback init -----------------------------------------------------
  function init2D() {
    try {
      ctx2d = canvas.getContext('2d');
    } catch (_e) {
      ctx2d = null;
    }
    if (!ctx2d) { mode = 'dead'; return false; } // nothing more we can do; stay blank (CSS gradient shows)
    size2D();
    return true;
  }

  function lerpRGB(a, b, t) {
    return [
      Math.round(a[0] + (b[0] - a[0]) * t),
      Math.round(a[1] + (b[1] - a[1]) * t),
      Math.round(a[2] + (b[2] - a[2]) * t),
    ];
  }
  const rgbStr = (c, alpha) => `rgba(${c[0]},${c[1]},${c[2]},${alpha})`;

  function render2D() {
    if (!ctx2d) return;
    const w = canvas.width, h = canvas.height;
    const cx = w / 2, cy = h / 2;
    const inten = curIntensity;
    const bg = lerpRGB(PALETTE.bgCalm, PALETTE.bgHot, inten);
    const line = lerpRGB(PALETTE.lineCalm, PALETTE.lineHot, inten);

    // background wash
    const maxR = Math.hypot(cx, cy);
    const g = ctx2d.createRadialGradient(cx, cy, 0, cx, cy, maxR);
    g.addColorStop(0, rgbStr(lerpRGB(bg, line, 0.15 * inten), 1));
    g.addColorStop(1, rgbStr(PALETTE.fog, 1));
    ctx2d.fillStyle = g;
    ctx2d.fillRect(0, 0, w, h);

    // concentric rings scrolling outward = a cheap tunnel; count/glow ride depth
    const RINGS = 16;
    const glow = 0.12 + 0.5 * inten;
    ctx2d.lineWidth = Math.max(1, maxR * 0.006);
    for (let i = 0; i < RINGS; i++) {
      // phase 0..1 per ring, offset by scroll; radius grows non-linearly outward
      const p = ((i / RINGS) + (scroll % 1) + 1) % 1;
      const r = Math.pow(p, 1.8) * maxR * 1.05;
      const a = glow * (0.25 + 0.75 * p); // brighter toward the rim
      ctx2d.strokeStyle = rgbStr(line, clamp01(a));
      ctx2d.beginPath();
      ctx2d.arc(cx, cy, r, 0, Math.PI * 2);
      ctx2d.stroke();
    }
  }

  // --- the frame loop (owned rAF; never blocks the beat loop) ---------------
  function frame(now) {
    if (!running || disposed) return;
    const dt = lastT ? Math.min(0.05, (now - lastT) / 1000) : 0.016;

    // pace on mobile: skip the heavy work but keep the rAF alive
    if (minFrameMs && lastT && (now - lastT) < minFrameMs) {
      rafId = requestAnimationFrame(frame);
      return;
    }
    lastT = now;

    // ease the displayed intensity toward the target (Recovery ramps down smoothly)
    curIntensity += (targetIntensity - curIntensity) * Math.min(1, dt * 3);

    // scroll speed rides depth; reduced-motion crawls
    const speed = (reduced ? 0.04 : 0.18) + (reduced ? 0.06 : 0.9) * curIntensity;
    scroll += dt * speed;

    if (mode === 'webgl' && renderer && mat) {
      mat.uniforms.uScroll.value = scroll;
      mat.uniforms.uIntensity.value = curIntensity;
      mat.uniforms.uTime.value += dt;
      try { renderer.render(scene, camera); }
      catch (_e) { /* GL hiccup — drop the frame, keep the loop */ }
    } else if (mode === '2d') {
      render2D();
    }

    rafId = requestAnimationFrame(frame);
  }

  function startLoop() {
    if (running || disposed) return;
    running = true;
    lastT = 0;
    rafId = requestAnimationFrame(frame);
  }
  function stopLoop() {
    running = false;
    if (rafId) { cancelAnimationFrame(rafId); rafId = 0; }
  }

  // Resolve the render mode once (lazy), then start the loop if still enabled.
  async function ensureStarted() {
    if (disposed) return;
    if (mode === 'webgl' || mode === '2d') { startLoop(); return; }
    if (mode === 'dead') return;
    if (starting) return; // an init is already in flight
    starting = true;
    try {
      const canWebGL = !reduced && hasWebGL();
      let ok = false;
      if (canWebGL) {
        try {
          ok = await initWebGL();
          if (ok) mode = 'webgl';
        } catch (_e) {
          // three failed to load/compile — tear down any partial GL, drop to 2D
          teardownWebGL();
          ok = false;
        }
      }
      if (!ok && !disposed) {
        if (init2D()) mode = '2d';
      }
    } finally {
      starting = false;
    }
    if (disposed) { teardownWebGL(); return; }
    if (enabled && (mode === 'webgl' || mode === '2d')) {
      canvas.style.display = '';
      startLoop();
    }
  }

  function teardownWebGL() {
    try { if (geo) geo.dispose(); } catch (_e) {}
    try { if (mat) mat.dispose(); } catch (_e) {}
    try {
      if (renderer) {
        renderer.dispose();
        // free the GL context promptly (important on mobile GPUs)
        const gl = renderer.getContext && renderer.getContext();
        const lose = gl && gl.getExtension && gl.getExtension('WEBGL_lose_context');
        if (lose) lose.loseContext();
      }
    } catch (_e) {}
    if (mesh && scene) { try { scene.remove(mesh); } catch (_e) {} }
    three = renderer = scene = camera = mat = geo = mesh = null;
  }

  // --- public surface (contracts.js BACKGROUND) -----------------------------
  return {
    /** depth 0..1 -> tube tint/speed. Cheap: just stores the eased target. */
    setDepth(depth) {
      if (disposed) return;
      targetIntensity = bgIntensityFor(depth);
    },

    /** Off-switch. true (re)starts + shows the canvas; false stops + hides it. */
    setEnabled(on) {
      if (disposed) return;
      enabled = !!on;
      if (enabled) {
        canvas.style.display = '';
        ensureStarted(); // fire-and-forget; safe if called repeatedly
      } else {
        stopLoop();
        try { canvas.style.display = 'none'; } catch (_e) {}
      }
    },

    /** Release GL resources + cancel rAF + drop listeners. Idempotent. */
    dispose() {
      if (disposed) return;
      disposed = true;
      enabled = false;
      stopLoop();
      window.removeEventListener('resize', onResize);
      teardownWebGL();
      ctx2d = null;
      mode = 'dead';
      try { canvas.style.display = 'none'; } catch (_e) {}
    },
  };
}
