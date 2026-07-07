/* ============================================================================
 * fog.js — real moving fog, made of GPU noise-advected particles.
 *
 * One THREE.Points cloud (single draw call) for the whole scene:
 *   - ambient particles fill the tunnel volume (slow, wide drift)
 *   - dense clusters wrap each feature card (tighter, brighter)
 * Each particle's position is displaced every frame by an animated 3D simplex
 * noise flow field in the vertex shader, so the fog curls and rolls instead of
 * sliding. Soft additive pink/magenta sprites. No textures, no FBOs.
 * ==========================================================================*/

import * as THREE from 'three';
import { Q } from './quality.js';

// Ashima webgl-noise simplex 3D (public domain) — drives the flow field.
const SNOISE = `
vec3 mod289(vec3 x){return x - floor(x*(1.0/289.0))*289.0;}
vec4 mod289(vec4 x){return x - floor(x*(1.0/289.0))*289.0;}
vec4 permute(vec4 x){return mod289(((x*34.0)+1.0)*x);}
vec4 taylorInvSqrt(vec4 r){return 1.79284291400159 - 0.85373472095314 * r;}
float snoise(vec3 v){
  const vec2 C = vec2(1.0/6.0, 1.0/3.0);
  const vec4 D = vec4(0.0, 0.5, 1.0, 2.0);
  vec3 i  = floor(v + dot(v, C.yyy));
  vec3 x0 = v - i + dot(i, C.xxx);
  vec3 g = step(x0.yzx, x0.xyz);
  vec3 l = 1.0 - g;
  vec3 i1 = min(g.xyz, l.zxy);
  vec3 i2 = max(g.xyz, l.zxy);
  vec3 x1 = x0 - i1 + C.xxx;
  vec3 x2 = x0 - i2 + C.yyy;
  vec3 x3 = x0 - D.yyy;
  i = mod289(i);
  vec4 p = permute( permute( permute(
             i.z + vec4(0.0, i1.z, i2.z, 1.0))
           + i.y + vec4(0.0, i1.y, i2.y, 1.0))
           + i.x + vec4(0.0, i1.x, i2.x, 1.0));
  float n_ = 0.142857142857;
  vec3  ns = n_ * D.wyz - D.xzx;
  vec4 j = p - 49.0 * floor(p * ns.z * ns.z);
  vec4 x_ = floor(j * ns.z);
  vec4 y_ = floor(j - 7.0 * x_);
  vec4 x = x_ *ns.x + ns.yyyy;
  vec4 y = y_ *ns.x + ns.yyyy;
  vec4 h = 1.0 - abs(x) - abs(y);
  vec4 b0 = vec4( x.xy, y.xy );
  vec4 b1 = vec4( x.zw, y.zw );
  vec4 s0 = floor(b0)*2.0 + 1.0;
  vec4 s1 = floor(b1)*2.0 + 1.0;
  vec4 sh = -step(h, vec4(0.0));
  vec4 a0 = b0.xzyw + s0.xzyw*sh.xxyy ;
  vec4 a1 = b1.xzyw + s1.xzyw*sh.zzww ;
  vec3 p0 = vec3(a0.xy,h.x);
  vec3 p1 = vec3(a0.zw,h.y);
  vec3 p2 = vec3(a1.xy,h.z);
  vec3 p3 = vec3(a1.zw,h.w);
  vec4 norm = taylorInvSqrt(vec4(dot(p0,p0), dot(p1,p1), dot(p2,p2), dot(p3,p3)));
  p0 *= norm.x; p1 *= norm.y; p2 *= norm.z; p3 *= norm.w;
  vec4 m = max(0.6 - vec4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
  m = m * m;
  return 42.0 * dot( m*m, vec4( dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3) ) );
}`;

const VERT = `
${SNOISE}
attribute float aSeed;
attribute float aSize;
attribute float aKind;   // 0 = ambient, 1 = around a card
uniform float uTime;
uniform float uSizeScale;
varying float vA;
varying float vSeed;
void main() {
  vec3 base = position;
  // animated domain-warped flow field — the fog "rolls" (faster now)
  vec3 q = base * 0.075 + vec3(0.0, 0.0, uTime * 0.085) + aSeed * 3.1;
  vec3 flow = vec3(snoise(q), snoise(q + 17.3), snoise(q + 41.7));
  // The detail octave is 3 more snoise() calls per vertex per frame — the mobile
  // tier compiles it out (FOG_OCTAVES define from quality.js).
#if FOG_OCTAVES > 1
  vec3 flow2 = vec3(snoise(q * 2.1 + 5.0), snoise(q * 2.1 + 23.0), snoise(q * 2.1 + 51.0));
#else
  vec3 flow2 = vec3(0.0);
#endif
  float amp = mix(2.5, 0.95, aKind);          // ambient drifts wider than card fog
  vec3 p = base + flow * amp + flow2 * (amp * 0.45);
  float tw = 0.5 + 0.5 * sin(uTime * 1.05 + aSeed * 6.2831);
  vA = mix(0.45, 1.0, tw) * mix(0.7, 1.0, aKind);
  vSeed = aSeed;
  vec4 mv = modelViewMatrix * vec4(p, 1.0);
  gl_PointSize = min(aSize * uSizeScale / -mv.z * (0.7 + 0.6 * tw), 420.0);
  gl_Position = projectionMatrix * mv;
}`;

const FRAG = `
uniform vec3 uColorA;
uniform vec3 uColorB;
uniform float uOpacity;
varying float vA;
varying float vSeed;
void main() {
  float r = length(gl_PointCoord - 0.5);
  float a = smoothstep(0.5, 0.0, r);          // soft round falloff
  a *= a;
  vec3 col = mix(uColorA, uColorB, fract(vSeed * 1.7));
  gl_FragColor = vec4(col, a * vA * uOpacity);
}`;

const rand = (a, b) => a + Math.random() * (b - a);

export function createFog({ layout, cardCenters, titleCenters, colorA, colorB }) {
  const pos = [], seed = [], size = [], kind = [];

  // Ambient fog throughout the tunnel volume — way more pink particles flying
  // around. Distributed in the winding spine's local frame so it fills the tube
  // as it bends (falls back to straight -Z if no spine is provided).
  const ambient = Math.round(layout.totalDepth * Q.fogAmbientPerDepth);
  for (let i = 0; i < ambient; i++) {
    const ang = Math.random() * Math.PI * 2;
    const rad = Math.sqrt(Math.random()) * layout.RADIUS * 0.95;
    if (layout.frameAt) {
      const fr = layout.frameAt(Math.random());
      const p = fr.pos
        .addScaledVector(fr.binormal, Math.cos(ang) * rad)
        .addScaledVector(fr.normal, Math.sin(ang) * rad * 0.9);
      pos.push(p.x, p.y, p.z);
    } else {
      pos.push(Math.cos(ang) * rad, Math.sin(ang) * rad * 0.9, -Math.random() * layout.totalDepth);
    }
    seed.push(Math.random());
    size.push(rand(0.26, 0.6));
    kind.push(0);
  }

  // Light fog hugging each card.
  for (const c of cardCenters || []) {
    for (let i = 0; i < Q.fogPerCard; i++) {
      pos.push(c.x + rand(-1.5, 1.5), c.y + rand(-1.5, 1.5), c.z + rand(-0.8, 0.8));
      seed.push(Math.random());
      size.push(rand(0.16, 0.34));
      kind.push(1);
    }
  }

  // Dense pink halo swirling around each sector title card.
  for (const c of titleCenters || []) {
    for (let i = 0; i < Q.fogPerTitle; i++) {
      pos.push(c.x + rand(-3.4, 3.4), c.y + rand(-2.0, 2.0), c.z + rand(-1.6, 1.6));
      seed.push(Math.random());
      size.push(rand(0.2, 0.5));
      kind.push(0);
    }
  }

  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  geo.setAttribute('aSeed', new THREE.Float32BufferAttribute(seed, 1));
  geo.setAttribute('aSize', new THREE.Float32BufferAttribute(size, 1));
  geo.setAttribute('aKind', new THREE.Float32BufferAttribute(kind, 1));

  const mat = new THREE.ShaderMaterial({
    defines: { FOG_OCTAVES: Q.fogOctaves },
    uniforms: {
      uTime: { value: 0 },
      uSizeScale: { value: 110 },
      uOpacity: { value: 0.05 },
      uColorA: { value: new THREE.Color(colorA != null ? colorA : 0xff5fb0) }, // per-mod (hot pink default)
      uColorB: { value: new THREE.Color(colorB != null ? colorB : 0x9b59b6) }, // per-mod (purple default)
    },
    vertexShader: VERT,
    fragmentShader: FRAG,
    transparent: true,
    depthWrite: false,
    blending: THREE.AdditiveBlending,
  });

  const points = new THREE.Points(geo, mat);
  points.frustumCulled = false; // particles drift outside their base bounds

  return {
    mesh: points,
    update(t) { mat.uniforms.uTime.value = t; },
    dispose() { geo.dispose(); mat.dispose(); },
  };
}
