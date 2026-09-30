// A continuous Loom field on the board, below pieces and legal-move markers.
import * as THREE from 'three';
import { createLoomKit } from '../../backroom/shared/hypno/loom.js';
import { makeRng } from '../../arcademy/core/rng.js';
import { TURN_LOOM, readTurn, turnRecipe } from '../game/turn-loom.js';
import { presentation } from '../game/preferences.js';

export function createTurnSpiral({ view, game, bus, menuOpen = () => false }) {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 512;
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.generateMipmaps = false;
  texture.minFilter = THREE.LinearFilter;
  const kit = createLoomKit();
  const erase = { value: 0 };
  const material = new THREE.MeshBasicMaterial({ map: texture, transparent: true, opacity: 0, depthWrite: false });
  material.onBeforeCompile = shader => {
    shader.uniforms.uErase = erase;
    shader.fragmentShader = 'uniform float uErase;\n' + shader.fragmentShader;
    shader.fragmentShader = shader.fragmentShader.replace('#include <map_fragment>', `
      #include <map_fragment>
      vec2 edge = abs(fract(vMapUv * 8.0 + .5) - .5);
      float grid = 1.0 - smoothstep(.008, .022, min(edge.x, edge.y));
      diffuseColor.rgb = mix(diffuseColor.rgb, vec3(.42, .28, .40), grid);
      vec2 cell = floor(vMapUv * 96.0);
      float grain = fract(sin(dot(cell, vec2(12.9898, 78.233))) * 43758.5453);
      diffuseColor.a *= 1.0 - smoothstep(grain * .72, grain * .72 + .28, uErase);
    `);
  };
  material.customProgramCacheKey = () => 'pbp-turn-loom-v1';
  const surface = new THREE.Mesh(new THREE.PlaneGeometry(8, 8), material);
  surface.name = 'pbp-turn-loom';
  surface.rotation.x = -Math.PI / 2;
  surface.position.y = .002;
  surface.renderOrder = -2;
  surface.raycast = () => {};
  surface.visible = false;
  view.boardGroup.add(surface);

  const count = 160, coords = new Float32Array(count * 3), colors = new Float32Array(count * 3);
  const origins = new Float32Array(count * 3);
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(coords, 3).setUsage(THREE.DynamicDrawUsage));
  geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  const dot = document.createElement('canvas'); dot.width = dot.height = 32;
  const ctx = dot.getContext('2d'), gradient = ctx.createRadialGradient(16, 16, 0, 16, 16, 16);
  gradient.addColorStop(0, '#fff'); gradient.addColorStop(.25, '#fff'); gradient.addColorStop(1, 'rgba(255,255,255,0)');
  ctx.fillStyle = gradient; ctx.fillRect(0, 0, 32, 32);
  const dotTexture = new THREE.CanvasTexture(dot);
  const dustMaterial = new THREE.PointsMaterial({ map: dotTexture, size: .06, vertexColors: true, transparent: true, depthWrite: false, opacity: 0 });
  const dust = new THREE.Points(geometry, dustMaterial);
  dust.name = 'pbp-loom-unravel'; dust.visible = false; dust.frustumCulled = false; dust.raycast = () => {};
  view.boardGroup.add(dust);

  let localSeed = String(Math.random()), key = null, recipe = null;
  let alpha = 0, fade = -1, fadeAlpha = 0, phaseMs = 0, paintedMs = -Infinity, wasStill = false;
  let back = 1;   // 0..1: after a menu or a pause the board eases back in, never at full strength at once
  let disposed = false;
  const off = bus.on('newgame', () => { localSeed = String(Math.random()); clear(); });
  function clear() {
    key = null; alpha = 0; fade = -1; erase.value = 0;
    surface.visible = dust.visible = false;
  }
  function startFade(still) {
    if (fade >= 0 || alpha <= .001) return;
    fade = 0; fadeAlpha = alpha;
    dust.visible = !still;
    if (still) return;
    const rng = makeRng('unravel:' + key), color = new THREE.Color();
    for (let i = 0; i < count; i++) {
      const j = i * 3;
      origins[j] = (rng() - .5) * 7.9; origins[j + 1] = rng(); origins[j + 2] = (rng() - .5) * 7.9;
      color.set(recipe.layer.colors[i % recipe.layer.colors.length]);
      color.toArray(colors, j);
    }
    geometry.attributes.color.needsUpdate = true;
  }
  function update(dt) {
    if (disposed) return;
    const pref = presentation(), still = pref.reducedMotion;
    if (pref.experience !== 'distraction' || menuOpen()) { clear(); back = 0; return; }
    back = Math.min(1, back + dt / TURN_LOOM.fadeSec);
    const turn = readTurn(game, localSeed);
    const nextKey = turn?.key || null;
    if (key !== null && nextKey !== key) startFade(still);
    if (fade >= 0) {
      phaseMs += dt * 1000;
      fade = Math.min(TURN_LOOM.fadeSec, fade + dt);
      const t = fade / TURN_LOOM.fadeSec;
      erase.value = still ? 0 : t;
      alpha = fadeAlpha * (1 - t);
      dust.visible = !still && t < 1;
      dustMaterial.opacity = fadeAlpha * Math.sin(Math.PI * t) * .8;
      for (let i = 0; !still && i < count; i++) {
        const j = i * 3, x = origins[j], z = origins[j + 2], spin = t * .16;
        coords[j] = x * Math.cos(spin) - z * Math.sin(spin);
        coords[j + 1] = .015 + Math.sin(t * Math.PI * .7) * (.12 + origins[j + 1] * .35);
        coords[j + 2] = x * Math.sin(spin) + z * Math.cos(spin);
      }
      if (!still) geometry.attributes.position.needsUpdate = true;
      if (t >= 1) clear();
    } else if (turn && turn.alpha > 0) {
      if (nextKey !== key) {
        key = nextKey; recipe = turnRecipe(key); kit.setRecipe('chess-turn', recipe);
        erase.value = 0; paintedMs = -Infinity;
      }
      alpha = turn.alpha * back;
      phaseMs = turn.ageMs;
    } else alpha = 0;

    material.opacity = alpha;
    surface.visible = alpha > .001;
    if (!surface.visible) return;
    // 25 Hz texture refresh, only while visible. Static under reduced motion.
    const paintMs = still ? 0 : Math.floor(phaseMs / 40) * 40;
    if (paintMs !== paintedMs || still !== wasStill) {
      kit.setStill(still);
      kit.paint(canvas, 'chess-turn', { now: paintMs });
      texture.needsUpdate = true; paintedMs = paintMs; wasStill = still;
    }
  }
  return {
    update,
    debug: () => ({ key, alpha, fade, phaseMs, visible: surface.visible, particles: dust.visible, loom: kit.debug() }),
    dispose() {
      if (disposed) return; disposed = true; off();
      surface.removeFromParent(); dust.removeFromParent();
      surface.geometry.dispose(); material.dispose(); texture.dispose();
      geometry.dispose(); dustMaterial.dispose(); dotTexture.dispose(); kit.dispose();
    },
  };
}
