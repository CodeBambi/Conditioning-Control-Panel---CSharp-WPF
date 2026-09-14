/* ============================================================================
 * fx.js - the tunnel's mood engine: the deeper you fall, the more it shows.
 *
 * Three always-on garnishes plus a ZONE system:
 *   - ribbons: neon strands helixing around the tube wall (closed loops with
 *     INTEGER twist counts, so they wrap the treadmill seam-free like the
 *     tunnel shader does)
 *   - sparkles: a points cloud filling the tube interior, mostly invisible
 *     until a glimmer zone (or high intensity) brings it up
 *   - lightning: jagged additive line bolts across the tube ahead of the
 *     camera, plus a uFlash bump that lights the tunnel shader's lines
 *
 * Zones: every ZONE_LEN units of depth rolls a mood - calm / pink fog /
 * storm / glimmer / neon. Special zones get more likely as the director's
 * intensity ramps (temple-run escalation). Params crossfade over the zone
 * edges so moods breathe in and out instead of snapping.
 * ==========================================================================*/

import * as THREE from 'three';
import { FOG_COLOR, FOG_DENSITY } from './tunnel.js';
import { S, onSettings } from './settings.js';

const clamp01 = (v) => Math.min(1, Math.max(0, v));
const hslOf = (intOrColor) => {
  const o = {};
  (intOrColor instanceof THREE.Color ? intOrColor : new THREE.Color(intOrColor)).getHSL(o);
  return o;
};
// Set `target` to base HSL shifted by delta d (hue wraps, sat/lum clamp).
function setHSLDelta(target, base, d) {
  target.setHSL((((base.h + d.dh) % 1) + 1) % 1, clamp01(base.s + d.ds), clamp01(base.l + d.dl));
}

const ZONE_LEN = 180;     // depth units per mood zone
const ZONE_EDGE = 0.14;   // fraction of the zone spent fading in/out
const RIBBONS = [
  { twist: 6, r: 4.95, color: 0xff7ac8 },
  { twist: -9, r: 4.65, color: 0xb388ff },
  { twist: 13, r: 5.1, color: 0xff4fa3 },
];
const SPARKLE_COUNT = 900;

// Zone parameter tables. `calm` doubles as the blend base.
const ZONE_TYPES = ['calm', 'pinkfog', 'storm', 'glimmer', 'neon'];
const ZP = {
  calm: { fog: 0x220d1e, density: FOG_DENSITY, line: 0xff69b4, spiral: 0xe56cc0, ribbon: 0.2, sparkle: 0.08, storm: 0 },
  pinkfog: { fog: 0x61163f, density: 0.038, line: 0xff8fce, spiral: 0xff9ad6, ribbon: 0.28, sparkle: 0.14, storm: 0 },
  storm: { fog: 0x1a0c26, density: 0.024, line: 0xd9a0ff, spiral: 0x9a6cf0, ribbon: 0.34, sparkle: 0.12, storm: 1 },
  glimmer: { fog: 0x2a1030, density: 0.017, line: 0xffb3dd, spiral: 0xf0c8ff, ribbon: 0.38, sparkle: 0.85, storm: 0 },
  neon: { fog: 0x24081f, density: 0.026, line: 0xff2f9e, spiral: 0xb46cff, ribbon: 0.85, sparkle: 0.2, storm: 0 },
};
for (const z of Object.values(ZP)) {
  z.fogC = new THREE.Color(z.fog);
  z.lineC = new THREE.Color(z.line);
  z.spiralC = new THREE.Color(z.spiral);
}
// Each zone's colors relative to `calm`, captured in HSL so a user-picked base
// color can be re-expanded into the full zone set (storms stay darker, neon
// brighter, etc.) in whatever hue the theme chooses. calm's deltas are zero.
{
  const cf = hslOf(ZP.calm.fog), cl = hslOf(ZP.calm.line), cs = hslOf(ZP.calm.spiral);
  const delta = (h, base) => ({ dh: h.h - base.h, ds: h.s - base.s, dl: h.l - base.l });
  for (const z of Object.values(ZP)) {
    z._dFog = delta(hslOf(z.fog), cf);
    z._dLine = delta(hslOf(z.line), cl);
    z._dSpiral = delta(hslOf(z.spiral), cs);
  }
}

const rand = (a, b) => a + Math.random() * (b - a);
const smoothstep = (a, b, x) => {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
};

function makeDotTexture() {
  const s = 64, c = document.createElement('canvas');
  c.width = c.height = s;
  const x = c.getContext('2d');
  const g = x.createRadialGradient(s / 2, s / 2, 0, s / 2, s / 2, s / 2);
  g.addColorStop(0, 'rgba(255,255,255,1)');
  g.addColorStop(0.25, 'rgba(255,255,255,0.6)');
  g.addColorStop(0.6, 'rgba(255,255,255,0.1)');
  g.addColorStop(1, 'rgba(255,255,255,0)');
  x.fillStyle = g;
  x.beginPath(); x.arc(s / 2, s / 2, s / 2, 0, Math.PI * 2); x.fill();
  return new THREE.CanvasTexture(c);
}

export function createFx({ scene, layout, tunnelMat, particleFog }) {
  const seed = Math.random() * 997;

  // each ribbon's hue offset from the first, so one picked ribbon color expands
  // back into the three-strand spread
  const ribbonBaseHSL = hslOf(RIBBONS[0].color);
  const ribbonDeltas = RIBBONS.map((r) => {
    const h = hslOf(r.color);
    return { dh: h.h - ribbonBaseHSL.h, ds: h.s - ribbonBaseHSL.s, dl: h.l - ribbonBaseHSL.l };
  });
  const boltColor = new THREE.Color(0xecdcff); // themed by applyTheme()

  // ---- ribbons ---------------------------------------------------------------
  const ribbons = [];
  for (const spec of RIBBONS) {
    const phase = Math.random() * Math.PI * 2;
    const N = 240;
    const pts = [];
    for (let k = 0; k < N; k++) {
      const tt = k / N;
      const fr = layout.frameAt(tt);
      const a = tt * Math.PI * 2 * spec.twist + phase; // integer twist: closes exactly
      pts.push(fr.pos.clone()
        .addScaledVector(fr.normal, Math.cos(a) * spec.r)
        .addScaledVector(fr.binormal, Math.sin(a) * spec.r));
    }
    const curve = new THREE.CatmullRomCurve3(pts, true, 'catmullrom', 0.5);
    const geo = new THREE.TubeGeometry(curve, 1000, 0.045, 5, true);
    const mat = new THREE.MeshBasicMaterial({
      color: spec.color, transparent: true, opacity: 0.2,
      blending: THREE.AdditiveBlending, depthWrite: false,
    });
    const mesh = new THREE.Mesh(geo, mat);
    scene.add(mesh);
    ribbons.push({ mesh, geo, mat });
  }

  // ---- sparkles ----------------------------------------------------------------
  const dotTex = makeDotTexture();
  const sparkPos = new Float32Array(SPARKLE_COUNT * 3);
  const _v = new THREE.Vector3();
  for (let i = 0; i < SPARKLE_COUNT; i++) {
    const fr = layout.frameAt(Math.random());
    const a = Math.random() * Math.PI * 2;
    const rr = rand(1.2, 4.4);
    _v.copy(fr.pos)
      .addScaledVector(fr.normal, Math.cos(a) * rr)
      .addScaledVector(fr.binormal, Math.sin(a) * rr);
    sparkPos[i * 3] = _v.x; sparkPos[i * 3 + 1] = _v.y; sparkPos[i * 3 + 2] = _v.z;
  }
  const sparkGeo = new THREE.BufferGeometry();
  sparkGeo.setAttribute('position', new THREE.BufferAttribute(sparkPos, 3));
  const sparkMat = new THREE.PointsMaterial({
    map: dotTex, color: 0xffd9ef, size: 0.32, sizeAttenuation: true,
    transparent: true, opacity: 0.08, blending: THREE.AdditiveBlending, depthWrite: false,
  });
  const sparks = new THREE.Points(sparkGeo, sparkMat);
  scene.add(sparks);

  // ---- lightning -----------------------------------------------------------------
  const bolts = [];
  let flash = 0;          // feeds the tunnel shader's uFlash
  let strikeIn = rand(1.5, 4);

  function strike(camDepth) {
    const d = camDepth + rand(18, 55);
    const fr = layout.frameAtDepth(d);
    const a0 = rand(0, Math.PI * 2);
    const a1 = a0 + rand(1.8, 4.4) * (Math.random() < 0.5 ? -1 : 1);
    const R = layout.RADIUS - 0.35;
    const N = 16;
    const pos = new Float32Array((N + 1) * 3);
    for (let i = 0; i <= N; i++) {
      const u = i / N;
      const a = a0 + (a1 - a0) * u;
      const pinch = Math.sin(u * Math.PI); // endpoints on the wall, middle dives inward
      const rr = R - pinch * rand(0.4, R * 0.7);
      _v.copy(fr.pos)
        .addScaledVector(fr.normal, Math.cos(a) * rr)
        .addScaledVector(fr.binormal, Math.sin(a) * rr)
        .addScaledVector(fr.tangent, rand(-1.6, 1.6) * pinch);
      pos[i * 3] = _v.x; pos[i * 3 + 1] = _v.y; pos[i * 3 + 2] = _v.z;
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    const mat = new THREE.LineBasicMaterial({
      color: boltColor.getHex(), transparent: true, opacity: 1,
      blending: THREE.AdditiveBlending, depthWrite: false,
    });
    const line = new THREE.Line(geo, mat);
    scene.add(line);
    bolts.push({ line, geo, mat, life: 0, dur: rand(0.22, 0.4) });
    flash = Math.min(1.3, flash + 0.9);
  }

  // ---- zones ------------------------------------------------------------------
  // Type per zone index is rolled once (against the intensity at first sight)
  // and cached, so a mood never flips mid-zone.
  const zoneCache = new Map();
  const hash = (i) => {
    const x = Math.sin(i * 127.1 + seed) * 43758.5453;
    return x - Math.floor(x);
  };
  function zoneType(i, intensity) {
    if (zoneCache.has(i)) return zoneCache.get(i);
    let type = 'calm';
    if (i >= 1 && hash(i) < 0.3 + 0.55 * intensity) {
      type = ZONE_TYPES[1 + Math.floor(hash(i + 0.5) * 4)] || 'pinkfog';
      const prev = zoneCache.get(i - 1);
      if (prev && prev === type) type = 'calm'; // no double feature
    }
    zoneCache.set(i, type);
    return type;
  }

  const _fogC = new THREE.Color(), _lineC = new THREE.Color(), _spiralC = new THREE.Color();
  let currentZone = 'calm';

  function update(depth, dt, t, intensity, rush) {
    rush = rush || 0;
    const zi = Math.floor(depth / ZONE_LEN);
    const u = (depth - zi * ZONE_LEN) / ZONE_LEN;
    const w = smoothstep(0, ZONE_EDGE, u) * (1 - smoothstep(1 - ZONE_EDGE, 1, u));
    const type = zoneType(zi, intensity);
    currentZone = type;
    const z = ZP[type], base = ZP.calm;

    // crossfade fog + shader palette between the base mood and the zone's
    _fogC.copy(base.fogC).lerp(z.fogC, w);
    _lineC.copy(base.lineC).lerp(z.lineC, w);
    _spiralC.copy(base.spiralC).lerp(z.spiralC, w);
    const density = base.density + (z.density - base.density) * w;
    if (scene.fog) { scene.fog.color.copy(_fogC); scene.fog.density = density; }
    const uni = tunnelMat.uniforms;
    uni.uFogColor.value.copy(_fogC);
    uni.uFogDensity.value = density;
    uni.uLineColor.value.copy(_lineC);
    uni.uSpiralColor.value.copy(_spiralC);

    // ribbons + sparkles: zone blend, plus a slow breath, the intensity ramp,
    // and the speed rush (fast falls burn brighter and shimmer harder)
    const ribbonOp = ((base.ribbon + (z.ribbon - base.ribbon) * w) + 0.22 * intensity) * (1 + rush * 0.9);
    for (let i = 0; i < ribbons.length; i++) {
      ribbons[i].mat.opacity = ribbonOp * (0.8 + 0.2 * Math.sin(t * (0.7 + rush * 1.6) + i * 2.1));
    }
    const sparkleOp = (base.sparkle + (z.sparkle - base.sparkle) * w) + 0.1 * intensity + 0.18 * rush;
    sparkMat.opacity = sparkleOp * (0.7 + 0.3 * Math.sin(t * (2.3 + rush * 3)));

    // storm: strikes while the zone is properly inside its window; a flat-out
    // fall can crack lightning anywhere
    if (z.storm && w > 0.3 && !document.hidden) {
      strikeIn -= dt;
      if (strikeIn <= 0) {
        strike(depth);
        if (Math.random() < 0.35) strike(depth); // occasional double-crack
        strikeIn = rand(1.2, 4.2);
      }
    } else if (rush > 0.75 && !document.hidden && Math.random() < dt * 0.12) {
      strike(depth);
    }
    for (let i = bolts.length - 1; i >= 0; i--) {
      const b = bolts[i];
      b.life += dt;
      if (b.life >= b.dur) {
        scene.remove(b.line); b.geo.dispose(); b.mat.dispose();
        bolts.splice(i, 1);
      } else {
        b.mat.opacity = (Math.random() < 0.75 ? 1 : 0.15) * (1 - b.life / b.dur);
      }
    }
    flash *= Math.exp(-6 * dt);
    if (uni.uFlash) uni.uFlash.value = flash;
  }

  // ---- theming ----------------------------------------------------------------
  // Re-derive every recolorable element from the six picked colors. Zone colors
  // flow to the tunnel/scene fog uniforms via update() every frame, so we only
  // rewrite the ZP color caches here; ribbons/sparkles/lightning/background and
  // the particle fog cloud are set directly.
  // A transient color scramble (the prism bubble) sets themeOverride for a few
  // seconds; CV() prefers it over the saved setting so applyTheme() recolors the
  // whole tube WITHOUT touching (or persisting) the player's chosen theme.
  let themeOverride = null, overrideTimer = 0;
  const CV = (k) => (themeOverride && themeOverride[k] != null) ? themeOverride[k] : S[k];
  function applyTheme() {
    const bFog = hslOf(CV('colFog')), bLine = hslOf(CV('colLine')), bSpiral = hslOf(CV('colSpiral'));
    for (const z of Object.values(ZP)) {
      setHSLDelta(z.fogC, bFog, z._dFog);
      setHSLDelta(z.lineC, bLine, z._dLine);
      setHSLDelta(z.spiralC, bSpiral, z._dSpiral);
    }
    const bRib = hslOf(CV('colRibbon'));
    for (let i = 0; i < ribbons.length; i++) setHSLDelta(ribbons[i].mat.color, bRib, ribbonDeltas[i]);
    sparkMat.color.set(CV('colSparkle'));
    // lightning: the rings hue, lifted pale so bolts read against the tube
    const bl = hslOf(CV('colLine'));
    boltColor.setHSL(bl.h, clamp01(bl.s * 0.55), clamp01(bl.l + 0.4));
    // tunnel background gradient (uBg2 a darker shade of the picked bg)
    if (tunnelMat.uniforms.uBg1) tunnelMat.uniforms.uBg1.value.set(CV('colBg'));
    if (tunnelMat.uniforms.uBg2) {
      const bg = hslOf(CV('colBg'));
      tunnelMat.uniforms.uBg2.value.setHSL(bg.h, bg.s, clamp01(bg.l * 0.55));
    }
    // particle fog cloud (two-tone: rings + spiral)
    const pfu = particleFog && particleFog.mesh && particleFog.mesh.material
      && particleFog.mesh.material.uniforms;
    if (pfu) {
      if (pfu.uColorA) pfu.uColorA.value.set(CV('colLine'));
      if (pfu.uColorB) pfu.uColorB.value.set(CV('colSpiral'));
    }
  }
  applyTheme();
  const offSettings = onSettings((key) => { if (typeof key === 'string' && key.startsWith('col')) applyTheme(); });

  // Prism bubble: randomize every tube color for ~ms, then snap back to the
  // player's theme. Backgrounds stay dark; the rest get vivid full-hue picks.
  const _rnd = (a, b) => a + Math.random() * (b - a);
  const _hue = (sLo, sHi, lLo, lHi) =>
    new THREE.Color().setHSL(Math.random(), _rnd(sLo, sHi), _rnd(lLo, lHi)).getHex();
  function flashRandomTheme(ms = 10000) {
    if (overrideTimer) clearTimeout(overrideTimer);
    themeOverride = {
      colFog: _hue(0.5, 0.9, 0.12, 0.22),
      colLine: _hue(0.6, 0.95, 0.5, 0.65),
      colSpiral: _hue(0.6, 0.95, 0.48, 0.62),
      colRibbon: _hue(0.6, 0.95, 0.55, 0.7),
      colSparkle: _hue(0.4, 0.9, 0.7, 0.85),
      colBg: _hue(0.4, 0.8, 0.05, 0.1),
    };
    applyTheme();
    overrideTimer = setTimeout(() => { overrideTimer = 0; themeOverride = null; applyTheme(); }, ms);
  }

  function dispose() {
    offSettings();
    if (overrideTimer) { clearTimeout(overrideTimer); overrideTimer = 0; }
    themeOverride = null;
    for (const r of ribbons) { scene.remove(r.mesh); r.geo.dispose(); r.mat.dispose(); }
    scene.remove(sparks); sparkGeo.dispose(); sparkMat.dispose(); dotTex.dispose();
    for (const b of bolts) { scene.remove(b.line); b.geo.dispose(); b.mat.dispose(); }
    bolts.length = 0;
  }

  return { update, dispose, flashRandomTheme, getZone: () => currentZone };
}
