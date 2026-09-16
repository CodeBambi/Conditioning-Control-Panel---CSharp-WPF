/* ============================================================================
 * board/bloom.js - the room glows when the meter is high.
 *
 * Past meter 0.5 the picture goes through a composer: the scene is drawn
 * into a float target (with MSAA, so nothing gets jaggier than before), an
 * UnrealBloomPass lifts the pink out of it, and one output pass does the
 * tone mapping and the sRGB conversion that three.js only does on its own
 * when drawing straight to the screen. Below 0.5 the composer is skipped
 * entirely and the frame costs what it always cost.
 *
 * What glows: the pink squares (their lit faces and their emissive when the
 * poses warm them), the move markers, the held man's line. Not the cream
 * squares, which are the brightest thing on the board by luminance, so the
 * stock luminosity threshold cannot be used: the high pass is keyed on how
 * pink a texel is (red minus green, in linear light) instead. The uniform
 * names are kept, so `threshold` still means what UnrealBloomPass says.
 *
 * Strength eases toward its target, so the meter crossing 0.5 and the
 * ramp's pushToBoard(0) at game over fade the glow in and out.
 *
 * Gate: window.PBP.settings.bloom (default true). board.setMeter drives it.
 * ==========================================================================*/

import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';

/** Every number the glow is made of. */
export const TUNING = Object.freeze({
  meterOn: 0.5,          // below this the composer is off
  strengthAt1: 0.55,     // bloom strength at meter 1.0 (linear from meterOn)
  radius: 0.35,
  threshold: 0.62,       // red minus green, linear, where the glow starts
  smooth: 0.35,          // and how wide the ramp in is
  ease: 3.0,             // per-second pull of strength toward its target
  samples: 4,            // MSAA on the scene target
  offBelow: 0.01,        // strength under this: plain render again
});

const pinkHighPass = /* glsl */`
  uniform sampler2D tDiffuse;
  uniform vec3 defaultColor;
  uniform float defaultOpacity;
  uniform float luminosityThreshold;
  uniform float smoothWidth;
  varying vec2 vUv;
  void main() {
    vec4 texel = texture2D(tDiffuse, vUv);
    float pink = texel.r - texel.g;
    float alpha = smoothstep(luminosityThreshold, luminosityThreshold + smoothWidth, pink);
    gl_FragColor = mix(vec4(defaultColor.rgb, defaultOpacity), texel, alpha);
  }`;

// The renderer's own tone mapping and output encoding only run when it draws
// to the screen, so the composer's last pass does both by hand. toneMapped is
// off on this material, or the renderer would prepend the same chunk and the
// shader would not compile.
const outputShader = {
  uniforms: { tDiffuse: { value: null }, toneMappingExposure: { value: 1 } },
  vertexShader: /* glsl */`
    varying vec2 vUv;
    void main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`,
  fragmentShader: /* glsl */`
    #include <common>
    #include <tonemapping_pars_fragment>
    uniform sampler2D tDiffuse;
    varying vec2 vUv;
    void main() {
      vec4 texel = texture2D(tDiffuse, vUv);
      gl_FragColor = vec4(ACESFilmicToneMapping(texel.rgb), texel.a);
      gl_FragColor = linearToOutputTexel(gl_FragColor);
    }`,
};

function bloomOn() {
  const s = (typeof window !== 'undefined' && window.PBP && window.PBP.settings) || {};
  return s.bloom !== false;
}

/**
 * `view` is board/scene.js. Wraps view.render (after whoever wrapped it
 * before, so their per-frame work still runs inside the composer's draw).
 */
export function createBloom({ view }) {
  const T = TUNING;
  const renderer = view.renderer;
  const size = new THREE.Vector2();
  renderer.getSize(size);
  const pr = renderer.getPixelRatio();
  const target = new THREE.WebGLRenderTarget(size.x * pr, size.y * pr, { type: THREE.HalfFloatType, samples: T.samples });
  const composer = new EffectComposer(renderer, target);
  composer.renderToScreen = true;
  const bloom = new UnrealBloomPass(new THREE.Vector2(size.x, size.y), 0, T.radius, T.threshold);
  bloom.materialHighPassFilter.fragmentShader = pinkHighPass;
  bloom.materialHighPassFilter.needsUpdate = true;
  bloom.highPassUniforms.smoothWidth.value = T.smooth;
  const output = new ShaderPass(outputShader);
  output.material.toneMapped = false;
  composer.addPass(bloom);
  composer.addPass(output);

  let meter = 0;
  let strength = 0;
  let live = false;
  const cost = { on: [], off: [] };
  const seen = new THREE.Vector2(size.x, size.y);

  function targetStrength() {
    if (!bloomOn() || meter < T.meterOn) return 0;
    return T.strengthAt1 * Math.min(1, (meter - T.meterOn) / (1 - T.meterOn));
  }

  const prev = view.render;
  view.render = () => {
    const t0 = performance.now();
    if (!live) {
      prev();
      push(cost.off, performance.now() - t0);
      return;
    }
    renderer.getSize(size);
    if (size.x !== seen.x || size.y !== seen.y) { seen.copy(size); composer.setSize(size.x, size.y); }
    bloom.strength = strength;
    output.uniforms.toneMappingExposure.value = renderer.toneMappingExposure;
    renderer.setRenderTarget(composer.readBuffer);
    renderer.clear();
    prev();                                   // the scene, into the float target
    renderer.setRenderTarget(null);
    composer.render();
    push(cost.on, performance.now() - t0);
  };
  function push(list, ms) { list.push(ms); if (list.length > 90) list.shift(); }
  const avg = (list) => (list.length ? +(list.reduce((a, b) => a + b, 0) / list.length).toFixed(3) : null);

  /** Once a frame, before render. */
  function update(dt) {
    const want = targetStrength();
    const k = 1 - Math.exp(-T.ease * (dt || 0.016));
    strength += (want - strength) * k;
    if (want === 0 && strength < T.offBelow) strength = 0;
    live = strength > 0;
  }

  return {
    update,
    setMeter(m) { meter = Math.max(0, Math.min(1, Number(m) || 0)); },
    /** For the harness: what the glow is doing and what a frame costs. */
    stats() {
      return { meter, strength: +strength.toFixed(3), live, enabled: bloomOn(), msOn: avg(cost.on), msOff: avg(cost.off), size: [seen.x, seen.y] };
    },
    /** Jump the ease, for a still frame. */
    settle() { strength = targetStrength(); live = strength > 0; },
    dispose() {
      view.render = prev;
      composer.dispose();
      target.dispose();
    },
  };
}
