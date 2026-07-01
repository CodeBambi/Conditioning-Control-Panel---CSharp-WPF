/* ============================================================================
 * fx.js — rolling additive fog motes that fill the tube volume.
 *
 * One THREE.Points draw call. Particles live in world space; when one falls
 * behind the camera it respawns just ahead, inside the tube cross-section using
 * the current camera frame. So it streams past forever with no per-particle
 * frameAt() cost — only the cheap respawn touches a particle. Tint follows the
 * blended zone fog colour. (Technique borrowed from the marketing engine's
 * fog.js, simplified for the endless case.)
 * ==========================================================================*/
import * as THREE from 'three';
import { Q } from './quality.js';

const VERT = `
  attribute float aSize;
  varying float vA;
  uniform float uScale;
  void main() {
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    gl_Position = projectionMatrix * mv;
    gl_PointSize = aSize * uScale / max(1.0, -mv.z);
    vA = clamp(1.0 - (-mv.z) / 220.0, 0.0, 1.0);   // fade with distance
  }`;

const FRAG = `
  precision mediump float;
  varying float vA;
  uniform vec3 uColor;
  void main() {
    vec2 d = gl_PointCoord - vec2(0.5);
    float r = length(d);
    if (r > 0.5) discard;
    float a = smoothstep(0.5, 0.0, r) * vA * 0.5;
    gl_FragColor = vec4(uColor, a);
  }`;

export class Fog {
  constructor(scene) {
    this.n = Q.fogCount;
    this.radius = 5.6;
    this.ahead = 240;
    this._pos = new Float32Array(this.n * 3);
    const sizes = new Float32Array(this.n);
    for (let i = 0; i < this.n; i++) sizes[i] = 40 + Math.random() * 120;

    this.geom = new THREE.BufferGeometry();
    this.geom.setAttribute('position', new THREE.BufferAttribute(this._pos, 3).setUsage(THREE.DynamicDrawUsage));
    this.geom.setAttribute('aSize', new THREE.BufferAttribute(sizes, 1));
    this.geom.setDrawRange(0, this.n);
    this.mat = new THREE.ShaderMaterial({
      uniforms: { uColor: { value: new THREE.Color(0x2a1630) }, uScale: { value: 1.0 } },
      vertexShader: VERT, fragmentShader: FRAG,
      transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
    });
    this.points = new THREE.Points(this.geom, this.mat);
    this.points.frustumCulled = false;
    scene.add(this.points);

    this._spawned = false;
    this._tmp = new THREE.Vector3();
  }

  _spawnAt(i, frame, dist) {
    // random point inside the tube cross-section at `dist` ahead along the frame
    const ang = Math.random() * Math.PI * 2;
    const rad = Math.sqrt(Math.random()) * this.radius;
    this._tmp.copy(frame.pos)
      .addScaledVector(frame.tan, dist)
      .addScaledVector(frame.nor, Math.cos(ang) * rad)
      .addScaledVector(frame.bin, Math.sin(ang) * rad);
    this._pos[i * 3] = this._tmp.x;
    this._pos[i * 3 + 1] = this._tmp.y;
    this._pos[i * 3 + 2] = this._tmp.z;
  }

  update(frame, render) {
    if (render) this.mat.uniforms.uColor.value.setRGB(render.fog.r * 2.2, render.fog.g * 2.2, render.fog.b * 2.2);
    // Fill the whole corridor once, then only respawn stragglers.
    if (!this._spawned) {
      for (let i = 0; i < this.n; i++) this._spawnAt(i, frame, (i / this.n) * this.ahead);
      this._spawned = true;
      this.geom.attributes.position.needsUpdate = true;
      return;
    }
    let dirty = false;
    for (let i = 0; i < this.n; i++) {
      this._tmp.set(this._pos[i * 3], this._pos[i * 3 + 1], this._pos[i * 3 + 2]).sub(frame.pos);
      if (this._tmp.dot(frame.tan) < -8) {           // fell behind the camera
        this._spawnAt(i, frame, this.ahead * (0.8 + Math.random() * 0.2));
        dirty = true;
      }
    }
    if (dirty) this.geom.attributes.position.needsUpdate = true;
  }

  reset() { this._spawned = false; }
}
