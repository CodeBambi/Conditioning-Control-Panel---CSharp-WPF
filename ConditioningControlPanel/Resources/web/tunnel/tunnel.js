/* ============================================================================
 * tunnel.js — the endless tube.
 *
 * A "turtle" walks forward one fixed arc STEP at a time, steering its own local
 * frame (tangent T, up-normal N, right-binormal B) by yaw/pitch/roll deltas the
 * active zone supplies. Steering the FRAME with quaternions (never world-up
 * Euler angles) means corkscrews, vertical loops and full 360s never gimbal —
 * the view simply rolls/pitches with the tube. Each visited point is a
 * "station" storing {p, T, N, B, s=cumulative arc length}.
 *
 * Geometry is built as a POOL of chunks (each a small swept-tube BufferGeometry
 * spanning ~chunkStations stations). Chunk N+1 is seeded from chunk N's shared
 * boundary station, so the frame is continuous in position, tangent AND roll —
 * no seam pop (a per-chunk THREE.TubeGeometry would compute independent Frenet
 * frames and roll-pop every chunk). As the camera advances we generate stations
 * ahead, build one new chunk when the front runs short, and recycle chunks that
 * fall behind. One small rebuild per chunk length — never per frame.
 *
 * The tube uses ONE shared ShaderMaterial (adapted from the marketing engine,
 * branch/hole uniforms stripped). uv.x carries the ABSOLUTE arc length so rings
 * and the helical spiral stay continuous across chunk seams and scroll forever.
 * The zone system writes blended palette/fog/glow into these uniforms each frame.
 * ==========================================================================*/
import * as THREE from 'three';
import { Q } from './quality.js';

const STEP = 2.0;         // world units between stations
const RADIUS = 6.0;       // tube radius
const RINGS_PER_UNIT = 1 / 7;   // perpendicular rings: one every ~7 units
const TURNS_PER_UNIT = 1 / 20;  // helical spiral pitch

const TUBE_VERT = `
  varying vec2 vUv;
  varying float vFogDepth;
  void main() {
    vUv = uv;
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    vFogDepth = -mv.z;
    gl_Position = projectionMatrix * mv;
  }`;

// Dark tinted base + perpendicular rings + a helical spiral, both scrolling and
// glowing intermittently so bloom flares them. uv.x = absolute arc length,
// uv.y = around the tube. Exp2 fog in-shader matches scene.fog.
const TUBE_FRAG = `
  precision highp float;
  varying vec2 vUv;
  varying float vFogDepth;
  uniform float uTime;
  uniform vec3 uBg1, uBg2, uLineColor, uSpiralColor, uFogColor;
  uniform float uFogDensity, uRingsPerUnit, uTurnsPerUnit, uArms, uScroll, uGlowMul;

  float lineMask(float coord, float w) {
    float di = 0.5 - abs(fract(coord) - 0.5);
    return 1.0 - smoothstep(0.0, w, di);
  }
  float pulse(float p, float k) { return pow(0.5 + 0.5 * sin(p), k); }

  void main() {
    float len = vUv.x;
    float around = vUv.y;
    float scroll = uTime * uScroll;

    vec3 base = mix(uBg1, uBg2, 0.5 + 0.5 * sin((around + 0.25) * 6.2831));

    float ringCoord = len * uRingsPerUnit - scroll;
    float spiralCoord = around * uArms + len * uTurnsPerUnit - scroll;
    float ring = lineMask(ringCoord, 0.06);
    float spiral = lineMask(spiralCoord, 0.05);

    float globalFlash = pulse(uTime * 0.7, 6.0);
    float ringGlow   = (0.35 + 1.7 * pulse(ringCoord * 3.0 - uTime * 1.6, 3.0) + 1.1 * globalFlash) * uGlowMul;
    float spiralGlow = (0.45 + 2.0 * pulse(spiralCoord * 0.4 - uTime * 2.1, 3.0) + 0.9 * globalFlash) * uGlowMul;

    vec3 col = base + uLineColor * ring * ringGlow + uSpiralColor * spiral * spiralGlow;

    float f = 1.0 - exp(-uFogDensity * uFogDensity * vFogDepth * vFogDepth);
    col = mix(col, uFogColor, clamp(f, 0.0, 1.0));
    gl_FragColor = vec4(col, 1.0);
  }`;

export class Tunnel {
  constructor(scene, zones) {
    this.scene = scene;
    this.zones = zones;
    this.radial = Q.tubeRadial;

    this.material = new THREE.ShaderMaterial({
      uniforms: {
        uTime: { value: 0 },
        uBg1: { value: new THREE.Color(0x1a0f22) },
        uBg2: { value: new THREE.Color(0x0c0814) },
        uLineColor: { value: new THREE.Color(0xff5fb0) },
        uSpiralColor: { value: new THREE.Color(0xb078ff) },
        uFogColor: { value: new THREE.Color(0x1a0d20) },
        uFogDensity: { value: 0.016 },
        uRingsPerUnit: { value: RINGS_PER_UNIT },
        uTurnsPerUnit: { value: TURNS_PER_UNIT },
        uArms: { value: 4.0 },
        uScroll: { value: 0.6 },
        uGlowMul: { value: 1.0 },
      },
      vertexShader: TUBE_VERT,
      fragmentShader: TUBE_FRAG,
      side: THREE.BackSide,
      fog: false,
    });

    // Station ring buffer (absolute indexing via _first).
    this._stations = [];
    this._first = 0;               // absolute index of _stations[0]
    this._chunks = [];             // [{ a0, a1, mesh, geom }]  (absolute station indices)
    this._nextChunkStart = 0;      // absolute index where the next chunk begins

    // Turtle state — start pointing straight DOWN so a run opens as a fall.
    this._pos = new THREE.Vector3(0, 0, 0);
    this._T = new THREE.Vector3(0, -1, 0);
    this._N = new THREE.Vector3(0, 0, 1);
    this._B = new THREE.Vector3().crossVectors(this._T, this._N).normalize();
    this._s = 0;
    this._noiseSeed = Math.random() * 1000;

    // Seed the first station and pre-generate + build the opening corridor.
    this._pushStation();
    this.generateTo(Q.aheadUnits);
    this._buildAheadChunks(0);
  }

  // ---- station generation -------------------------------------------------

  _absGet(i) { return this._stations[i - this._first]; }
  get _headIndex() { return this._first + this._stations.length - 1; }

  _pushStation() {
    this._stations.push({
      p: this._pos.clone(),
      T: this._T.clone(),
      N: this._N.clone(),
      B: this._B.clone(),
      s: this._s,
    });
  }

  // smooth-ish deterministic wander without per-step Math.random churn
  _noise(kind) {
    const s = this._s * 0.1 + this._noiseSeed + (kind === 'p' ? 41.7 : 0);
    return Math.sin(s * 0.7) * 0.6 + Math.sin(s * 1.9 + 1.3) * 0.3 + Math.sin(s * 3.3 + 2.1) * 0.1;
  }

  _step() {
    const z = this.zones.steeringAt(this._s).steer;
    const dyaw = (z.yaw + z.wind * Math.sin(this._s * z.windFreq) + z.wobble * this._noise('y')) * STEP;
    const dpitch = (z.pitch + z.wobble * 0.6 * this._noise('p')) * STEP;
    const droll = z.roll * STEP;

    const q = new THREE.Quaternion();
    if (dyaw) { q.setFromAxisAngle(this._N, dyaw); this._T.applyQuaternion(q); this._B.applyQuaternion(q); }
    if (dpitch) { q.setFromAxisAngle(this._B, dpitch); this._T.applyQuaternion(q); this._N.applyQuaternion(q); }
    if (droll) { q.setFromAxisAngle(this._T, droll); this._N.applyQuaternion(q); this._B.applyQuaternion(q); }

    // Re-orthonormalise (Gram-Schmidt) to fight drift over long runs.
    this._T.normalize();
    this._N.addScaledVector(this._T, -this._N.dot(this._T)).normalize();
    this._B.crossVectors(this._T, this._N).normalize();

    this._pos.addScaledVector(this._T, STEP);
    this._s += STEP;
    this._pushStation();
  }

  /** Generate stations until the head reaches at least arc length sTarget. */
  generateTo(sTarget) {
    let guard = 100000;
    while (this._headIndex >= this._first && this._absGet(this._headIndex).s < sTarget && guard-- > 0) {
      this._step();
    }
  }

  // ---- chunk build / recycle ---------------------------------------------

  _buildChunk(a0, a1) {
    const rad = this.radial;
    const n = a1 - a0 + 1;
    const vertsPerRing = rad + 1;
    const positions = new Float32Array(n * vertsPerRing * 3);
    const uvs = new Float32Array(n * vertsPerRing * 2);
    const tmp = new THREE.Vector3();
    let vp = 0, up = 0;
    for (let k = 0; k < n; k++) {
      const st = this._absGet(a0 + k);
      for (let j = 0; j <= rad; j++) {
        const ang = (j / rad) * Math.PI * 2;
        const cos = Math.cos(ang), sin = Math.sin(ang);
        tmp.copy(st.p)
          .addScaledVector(st.N, cos * RADIUS)
          .addScaledVector(st.B, sin * RADIUS);
        positions[vp++] = tmp.x; positions[vp++] = tmp.y; positions[vp++] = tmp.z;
        uvs[up++] = st.s;          // absolute arc length -> continuous rings across chunks
        uvs[up++] = j / rad;
      }
    }
    // Winding is CW as seen from OUTSIDE so the face normals point outward and the
    // BackSide material renders the INTERIOR (the wall we fly through). The mirror
    // winding (a,b,a+1 …) points normals inward → BackSide culls every near wall
    // and only curved-away sections show, which reads as a tube seen from outside.
    const indices = [];
    for (let k = 0; k < n - 1; k++) {
      for (let j = 0; j < rad; j++) {
        const a = k * vertsPerRing + j;
        const b = a + vertsPerRing;
        indices.push(a, a + 1, b, a + 1, b + 1, b);
      }
    }
    const geom = new THREE.BufferGeometry();
    geom.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geom.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));
    geom.setIndex(indices);
    const mesh = new THREE.Mesh(geom, this.material);
    mesh.frustumCulled = false;
    this.scene.add(mesh);
    return { a0, a1, mesh, geom };
  }

  /** Build chunks forward until they cover generated stations near the head. */
  _buildAheadChunks(camS) {
    const span = Q.chunkStations;
    // keep building while there are enough fresh stations ahead of the last chunk
    while (this._headIndex - this._nextChunkStart >= span) {
      const a0 = this._nextChunkStart;
      const a1 = a0 + span;                 // shares boundary station with the next chunk
      this._chunks.push(this._buildChunk(a0, a1));
      this._nextChunkStart = a1;
    }
  }

  _recycleBehind(camS) {
    const cutoff = camS - Q.behindUnits;
    while (this._chunks.length > 1) {
      const c = this._chunks[0];
      const endStation = this._absGet(c.a1);
      if (endStation && endStation.s < cutoff) {
        this.scene.remove(c.mesh);
        c.geom.dispose();
        this._chunks.shift();
      } else break;
    }
    // Drop stations that no live chunk references any more.
    const keepFrom = this._chunks.length ? this._chunks[0].a0 : this._first;
    const dropCount = keepFrom - this._first;
    if (dropCount > 0) {
      this._stations.splice(0, dropCount);
      this._first += dropCount;
    }
  }

  // ---- per-frame update ---------------------------------------------------

  update(camS, dt, render) {
    this.material.uniforms.uTime.value += dt;
    if (render) {
      const u = this.material.uniforms;
      u.uBg1.value.setRGB(render.bg1.r, render.bg1.g, render.bg1.b);
      u.uBg2.value.setRGB(render.bg2.r, render.bg2.g, render.bg2.b);
      u.uLineColor.value.setRGB(render.line.r, render.line.g, render.line.b);
      u.uSpiralColor.value.setRGB(render.spiral.r, render.spiral.g, render.spiral.b);
      u.uFogColor.value.setRGB(render.fog.r, render.fog.g, render.fog.b);
      u.uFogDensity.value = render.fogDensity;
      u.uGlowMul.value = render.glowMul;
      u.uScroll.value = render.scroll;
    }
    // Stream: keep geometry generated & built ahead of the camera, recycle behind.
    this.generateTo(camS + Q.aheadUnits);
    this._buildAheadChunks(camS);
    this._recycleBehind(camS);
  }

  // ---- sampling (camera rides these exact stations) -----------------------

  /** {pos, tan, nor, bin} at arc length s, interpolated between stations. */
  frameAt(s, out) {
    out = out || { pos: new THREE.Vector3(), tan: new THREE.Vector3(), nor: new THREE.Vector3(), bin: new THREE.Vector3() };
    const lo = this._first, hi = this._headIndex;
    // stations are ~STEP apart, so estimate then clamp-scan
    let i = this._first + Math.floor((s - this._absGet(lo).s) / STEP);
    i = Math.max(lo, Math.min(hi - 1, i));
    while (i > lo && this._absGet(i).s > s) i--;
    while (i < hi - 1 && this._absGet(i + 1).s < s) i++;
    const a = this._absGet(i), b = this._absGet(i + 1) || a;
    const seg = Math.max(1e-4, b.s - a.s);
    const t = Math.max(0, Math.min(1, (s - a.s) / seg));
    out.pos.copy(a.p).lerp(b.p, t);
    out.tan.copy(a.T).lerp(b.T, t).normalize();
    out.nor.copy(a.N).lerp(b.N, t).normalize();
    out.bin.copy(a.B).lerp(b.B, t).normalize();
    return out;
  }

  get radius() { return RADIUS; }
}
