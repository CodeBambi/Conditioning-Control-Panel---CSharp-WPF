/* ============================================================================
 * board/room.js - the room the board stands in, and how it answers the game.
 *
 * Until now the board sat in a flat navy void: one background colour and a
 * fog of the same colour. env.js gave the crowns a studio to reflect, but it
 * was rendered once into a cube map and nobody could see it. This is the room
 * the eye gets:
 *
 *   the FLOOR    a wide dark glossy plane the plinth stands on, taking the
 *                same studio room in its gloss that the men take, and melting
 *                into the horizon under the existing fog;
 *   the POOL     a soft pool of shade on the floor around the plinth, so the
 *                box is standing on something and not hovering over it;
 *   the DOME     a sphere round everything, plum at the horizon, a pink haze
 *                just above it, black overhead. Drawn by one small shader.
 *
 * And the first of the room's reactions, the TURN TELL (PBP-BEATS beat 6):
 *
 *   the LEAN     the light rig (key, fill and rim together, scene.js groups
 *                them) swings round to stand behind whoever is to move, over
 *                leanMs. From the fixed camera that keeps the light on the
 *                mover's shoulder as the view swings; from the top or a free
 *                camera the shadows sweeping round IS the tell;
 *   the EDGE     a pink line on the bevel of the plinth on the mover's side,
 *                full on the first frame of the turn and easing to a resting
 *                glow, while the other edge waits dim.
 *
 * Then the room answers the game (layer 2 of the backdrop):
 *
 *   the DIP      a capture makes the key light flinch: down to dipTo on the
 *                frame the man comes off, back to full over dipMs. A second
 *                capture restarts it; nothing stacks and nothing is gated;
 *   the WARMTH   the ramp's meter (boot.js chains setMeter into here, 0..1
 *                every 200 ms) turns the room pink past warmFrom: the fog,
 *                the background and the dome's horizon move TOGETHER toward
 *                warmHorizon (they must stay one colour, see the dome), the
 *                haze, the hemisphere and the fill follow, eased over warmMs
 *                so the steps never show. Past breathAt (the HUD's
 *                vigBreathe) the warmth breathes on the vignette's own
 *                3600 ms cycle, so the room and the frame swell as one;
 *   the DRAIN    game over: key, fill and hemisphere sink to overLevel of
 *                their base over overMs (the ramp's sigh-out), the rim to
 *                overRim, and one SpotLight comes up over the winner's king.
 *                A draw only dims to overDraw and lights no spot. A new game
 *                brings everything back at once; a take-back that resurrects
 *                the game brings it back over overBackMs.
 *
 * And the room's one piece of air (layer 3):
 *
 *   the SHAFT    a faint open cone of light down the key light's axis, riding
 *                the rig so it always falls from behind the mover. Alpha
 *                fades along its height (lit up at the light, gone at the
 *                board) and at its silhouette, peaks at shaftAlpha so it reads
 *                as air catching light and never a beam, brightens a little
 *                with the warmth, dims with the drain, and is capped as the
 *                camera looks straight down, where it would be a disc.
 *                board/motes.js's dust already drifts through it.
 *
 * Every move is a tween off update(dt), so the headless harness's stepped
 * clock sees all of it. Reduced motion (window.PBP.settings.reducedMotion or
 * the OS query) takes every state at once and never breathes.
 *
 * Wiring: boot.js builds one after the scene with the game, forwards `turn`,
 * `local`, `capture` and `gameover` off the bus, chains setMeter, and pumps
 * update(dt) before render. env.js dresses the floor with the cube map
 * whichever of the two lands first.
 * ==========================================================================*/

import * as THREE from 'three';
import { squareToWorld } from './scene.js';
import {
  ROOM, createTweens, yawFor, glowTargets, easeOutCubic, clamp01,
  warmthFor, breathFor, overTargets, shaftLevel,
} from './roomMath.js';

export { ROOM } from './roomMath.js';

const T = ROOM;

function reducedMotion() {
  try {
    const s = window.PBP && window.PBP.settings;
    if (s && s.reducedMotion) return true;
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  } catch { return false; }
}

// --- the dome ---------------------------------------------------------------
// Colours come in as linear THREE.Colors and go out through the renderer's
// colour space chunk but NOT its tone mapping: three converts the fog colour
// to the output space and applies it after tone mapping, so a tone-mapped
// dome could never meet the fully fogged floor at the horizon without a
// seam. Untouched, the horizon IS the fog colour, on screen and inside
// bloom.js's composer alike (where neither is tone mapped until the output
// pass, which then does both the same).
const domeVertex = /* glsl */`
  varying vec3 vDir;
  void main() {
    vDir = normalize((modelMatrix * vec4(position, 1.0)).xyz);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  }`;
const domeFragment = /* glsl */`
  #include <common>
  uniform vec3 horizon; uniform vec3 haze; uniform vec3 zenith; uniform vec3 under;
  uniform float hazeAt; uniform float zenithAt; uniform float underAt;
  varying vec3 vDir;
  void main() {
    float h = normalize(vDir).y;
    vec3 c;
    if (h < 0.0) {
      c = mix(horizon, under, smoothstep(0.0, underAt, -h));
    } else {
      c = mix(horizon, haze, smoothstep(0.0, hazeAt, h));
      c = mix(c, zenith, smoothstep(hazeAt, zenithAt, h));
    }
    gl_FragColor = vec4(c, 1.0);
    #include <colorspace_fragment>
  }`;

function buildDome() {
  const mat = new THREE.ShaderMaterial({
    uniforms: {
      horizon: { value: new THREE.Color(T.horizon) },
      haze: { value: new THREE.Color(T.haze) },
      zenith: { value: new THREE.Color(T.zenith) },
      under: { value: new THREE.Color(T.under) },
      hazeAt: { value: T.hazeAt }, zenithAt: { value: T.zenithAt }, underAt: { value: T.underAt },
    },
    vertexShader: domeVertex, fragmentShader: domeFragment,
    side: THREE.BackSide, depthWrite: false, fog: false,
  });
  mat.toneMapped = false;
  const dome = new THREE.Mesh(new THREE.SphereGeometry(T.domeRadius, 48, 24), mat);
  dome.name = 'pbp-dome';
  dome.frustumCulled = false;
  dome.renderOrder = -10;
  return dome;
}

// --- the floor and the pool of shade under the plinth ----------------------
function buildFloor() {
  const mat = new THREE.MeshPhysicalMaterial({
    color: T.floorColor, roughness: T.floorRough, metalness: 0,
    clearcoat: T.floorClearcoat, clearcoatRoughness: T.floorClearcoatRough,
    envMapIntensity: T.floorEnv,
  });
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(T.floorSize, T.floorSize).rotateX(-Math.PI / 2), mat);
  floor.position.y = T.floorY;
  floor.receiveShadow = true;
  floor.name = 'pbp-floor';
  return floor;
}

/** A blurred dark rounded square, drawn once, laid on the floor. */
function buildPool() {
  const px = T.poolPx;
  let texture = null;
  try {
    const canvas = document.createElement('canvas');
    canvas.width = px; canvas.height = px;
    const ctx = canvas.getContext('2d');
    const inset = px * T.poolInset;
    const r = px * 0.06;
    ctx.shadowColor = 'rgba(0, 0, 0, 1)';
    ctx.shadowBlur = inset * 0.9;
    ctx.fillStyle = 'rgba(0, 0, 0, 1)';
    // three passes stack the blur into a soft falloff instead of one hard-edged smear
    for (let i = 0; i < 3; i++) {
      ctx.beginPath();
      ctx.roundRect(inset, inset, px - inset * 2, px - inset * 2, r);
      ctx.fill();
    }
    texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
  } catch { return null; }   // no 2D canvas (a stripped-down harness): no pool, no harm
  const mat = new THREE.MeshBasicMaterial({
    map: texture, alphaMap: texture, color: 0x000000, transparent: true, opacity: T.poolAlpha,
    depthWrite: false, toneMapped: false,
  });
  const pool = new THREE.Mesh(new THREE.PlaneGeometry(T.poolSize, T.poolSize).rotateX(-Math.PI / 2), mat);
  pool.position.y = T.floorY + 0.004;
  pool.renderOrder = -5;
  pool.name = 'pbp-pool';
  return pool;
}

// --- the edge glow: one line per side, laid on the plinth's top bevel ------
// The bevel is 0.05 out and 0.05 down from the lid's edge at 4.7 / -0.08, so
// its slope is centred at 4.725 / -0.105 and faces up-and-out at 45 degrees.
// The bar floats a hair off that slope along its normal: on the surface
// itself it z-fought the plinth and came through in flickers.
function buildEdge(side) {
  const holder = new THREE.Group();
  holder.rotation.y = side === 'b' ? Math.PI : 0;
  const mat = new THREE.MeshBasicMaterial({
    color: T.glowColor, transparent: true, opacity: T.glowOff,
    depthWrite: false, toneMapped: false, side: THREE.DoubleSide,
  });
  const bar = new THREE.Mesh(new THREE.PlaneGeometry(T.glowLength, T.glowWidth), mat);
  bar.position.set(0, -0.105 + T.glowLift, 4.725 + T.glowLift);
  bar.rotation.x = -Math.PI / 4;
  bar.name = 'pbp-edge-' + side;
  holder.add(bar);
  return { holder, mat };
}

// --- the light shaft --------------------------------------------------------
// An open truncated cone, wide end on the board's centre, narrow end up at
// the key light, drawn additively with no depth write and no tone mapping so
// it only ever adds a little light to whatever is behind it. The vertex
// shader hands down the height (0 at the board, 1 at the light) and the
// view-space normal; the fragment fades along the height, so the shaft is
// lit where it is narrow and gone where it would lie across the men, and at
// the silhouette, where a cone's edge would otherwise draw as a line. Both
// faces draw (DoubleSide) and add: the core of the projected shape gets a
// front and a back, the edges get nothing, which is what soft air does. The
// peak alpha comes down from JS as uLevel (roomMath.shaftLevel), already
// warmed, drained and capped for a top-down camera.
const KEY_AT = new THREE.Vector3(4.5, 9.5, 5.5);   // scene.js's key, in rig space
const UP = new THREE.Vector3(0, 1, 0);
const shaftVertex = /* glsl */`
  uniform float uHalf;
  varying float vH; varying vec3 vN; varying vec3 vV;
  void main() {
    vH = (position.y + uHalf) / (2.0 * uHalf);
    vN = normalize(normalMatrix * normal);
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    vV = normalize(-mv.xyz);
    gl_Position = projectionMatrix * mv;
  }`;
const shaftFragment = /* glsl */`
  uniform vec3 color; uniform float uLevel;
  uniform float uRise; uniform float uFadeTip; uniform float uSoft;
  varying float vH; varying vec3 vN; varying vec3 vV;
  void main() {
    float along = smoothstep(0.0, uRise, vH) * (1.0 - smoothstep(uFadeTip, 1.0, vH));
    float face = smoothstep(0.0, uSoft, abs(dot(normalize(vN), normalize(vV))));
    float a = along * face * uLevel;
    if (a < 0.002) discard;
    gl_FragColor = vec4(color * a, a);
  }`;

function buildShaft() {
  const half = T.shaftLength / 2;
  const mat = new THREE.ShaderMaterial({
    uniforms: {
      color: { value: new THREE.Color(T.shaftColor) }, uLevel: { value: T.shaftAlpha },
      uHalf: { value: half }, uRise: { value: T.shaftRise }, uFadeTip: { value: T.shaftFadeTip }, uSoft: { value: T.shaftSoft },
    },
    vertexShader: shaftVertex, fragmentShader: shaftFragment,
    side: THREE.DoubleSide, transparent: true, depthWrite: false, depthTest: true,
    blending: THREE.AdditiveBlending, premultipliedAlpha: true, fog: false,
  });
  mat.toneMapped = false;
  const geo = new THREE.CylinderGeometry(T.shaftTip, T.shaftRadius, T.shaftLength, 40, 1, true);
  const shaft = new THREE.Mesh(geo, mat);
  // local +Y up the cone; stand it on the board's centre and point it at the key
  const axis = KEY_AT.clone().normalize();
  shaft.quaternion.setFromUnitVectors(UP, axis);
  shaft.position.copy(axis).multiplyScalar(half);
  shaft.renderOrder = 3;            // after the room and the pool, before the motes
  shaft.frustumCulled = false;
  shaft.name = 'pbp-shaft';
  return shaft;
}

// --- the spot over the winner's king -----------------------------------------
// Built once, dark, and aimed when the game ends. It lives on the scene and
// not the rig: the rig leans to the mover, and the spot has one man to find.
function buildSpot() {
  const spot = new THREE.SpotLight(T.spotColor, 0, 0, T.spotAngle, T.spotPenumbra, 2);
  spot.position.set(0, T.spotHeight, 0);
  spot.castShadow = false;
  spot.name = 'pbp-spot';
  spot.target.name = 'pbp-spot-target';
  return spot;
}

/**
 * createRoom({ view, bus, game, env })
 *   -> { group, update, lean, tell, setMeter, settle, debug, dispose }
 *
 * `view` is board/scene.js (its lightRig is what leans, and its lights are
 * what dip, warm and drain); `game` is the referee, asked where the winner's
 * king stands; `env` is a getter for board/env.js, which may or may not have
 * landed yet.
 */
export function createRoom({ view, bus, game = null, env = () => null }) {
  const scene = view.scene;
  const group = new THREE.Group();
  group.name = 'pbp-room';
  const dome = buildDome();
  const floor = buildFloor();
  const pool = buildPool();
  group.add(dome, floor);
  if (pool) group.add(pool);
  const edges = { w: buildEdge('w'), b: buildEdge('b') };
  group.add(edges.w.holder, edges.b.holder);
  scene.add(group);

  // The fog and the background now belong to the horizon, so the far floor
  // melts into exactly the colour the dome shows behind it.
  const horizon = new THREE.Color(T.horizon);
  scene.background = horizon.clone();
  scene.fog = new THREE.Fog(horizon.clone(), T.fogNear, T.fogFar);

  // env.js hands the floor the cube map if it is already here; if it lands
  // later, boot.js asks it to dress this group too.
  const e = env();
  if (e && e.dressBoard) e.dressBoard(group);

  const tweens = createTweens();
  const rig = view.lightRig || null;
  const lights = view.lights || {};
  const state = { side: 'w', yaw: 0, meter: 0, over: null, spotSquare: null, breathMs: 0, topness: 0 };
  const yaw = { value: 0 };   // tweened, then written onto the rig each frame
  const dip = { value: 1 };   // the key's share of itself: dipTo on a capture, easing home
  const warm = { value: 0 };  // the warmth the room has reached (its target is warmthFor(meter))
  const drain = { key: 1, fill: 1, hemi: 1, rim: 1, spot: 0 };   // shares of base, 1 = live
  let breath = 0;

  // The lights' base, read once from what scene.js built, so this file never
  // needs LIGHT and a retuned rig retunes the room with it.
  const base = {
    key: lights.key ? lights.key.intensity : 0,
    fill: lights.fill ? lights.fill.intensity : 0,
    hemi: lights.hemi ? lights.hemi.intensity : 0,
    rim: lights.rim ? lights.rim.intensity : 0,
    sky: lights.hemi ? lights.hemi.color.clone() : new THREE.Color(),
    ground: lights.hemi ? lights.hemi.groundColor.clone() : new THREE.Color(),
    fillColor: lights.fill ? lights.fill.color.clone() : new THREE.Color(),
    horizon: new THREE.Color(T.horizon),
    haze: new THREE.Color(T.haze),
  };
  const warmTo = {
    horizon: new THREE.Color(T.warmHorizon), haze: new THREE.Color(T.warmHaze),
    sky: new THREE.Color(T.warmSky), ground: new THREE.Color(T.warmGround), fill: new THREE.Color(T.warmFill),
  };

  const spot = buildSpot();
  scene.add(spot, spot.target);
  const shaft = buildShaft();
  if (rig) rig.add(shaft);
  const forward = new THREE.Vector3();
  const kingAt = new THREE.Vector3();

  /** Swing the rig to stand behind `side`. */
  function lean(side, instant = false) {
    state.side = side;
    const want = yawFor(side);
    tweens.to(yaw, 'value', want, instant || reducedMotion() ? 0 : T.leanMs);
  }

  /** The mover's edge hits full and settles; the other waits. null = over. */
  function tell(side, instant = false) {
    const want = glowTargets(side);
    const ms = instant || reducedMotion() ? 0 : T.glowMs;
    for (const s of ['w', 'b']) {
      const m = edges[s].mat;
      if (s === side) m.opacity = T.glowHit;   // frame one, before the ease
      tweens.to(m, 'opacity', want[s], ms, easeOutCubic);
    }
  }

  /** The key flinches: dipTo this frame, home over dipMs. A second one restarts. */
  function flinch() {
    dip.value = T.dipTo;
    tweens.to(dip, 'value', 1, reducedMotion() ? 0 : T.dipMs, easeOutCubic);
  }

  /** The ramp's meter, 0..1, arriving in steps: the warmth eases toward it. */
  function setMeter(m) {
    state.meter = clamp01(m);
    tweens.to(warm, 'value', warmthFor(state.meter), reducedMotion() ? 0 : T.warmMs, easeOutCubic);
  }

  /** Where `side`'s king stands, off the referee, or null. */
  function kingSquare(side) {
    try {
      const pos = game && game.rules && game.rules.position ? game.rules.position() : null;
      if (!pos) return null;
      for (const sq of Object.keys(pos)) {
        const man = pos[sq];
        if (man && man.type === 'k' && man.side === side) return sq;
      }
    } catch { /* a referee that cannot say: no spot */ }
    return null;
  }

  /** Aim the spot at `sq`: high above, a little toward the camera's side. */
  function aimSpot(sq) {
    squareToWorld(sq, 0, kingAt);
    const cam = view.camera ? view.camera.position : null;
    const tx = cam ? cam.x : 0;
    const tz = cam ? cam.z : 1;
    const len = Math.hypot(tx, tz) || 1;
    spot.position.set(kingAt.x + (tx / len) * T.spotToward, T.spotHeight, kingAt.z + (tz / len) * T.spotToward);
    spot.target.position.copy(kingAt);
    state.spotSquare = sq;
  }

  /** Game over: the room drains, and the spot finds the winner's king. */
  function over(result, winner) {
    state.over = { result: result || 'over', winner: winner || null };
    const want = overTargets(state.over.result, state.over.winner);
    const ms = reducedMotion() ? 0 : T.overMs;
    for (const k of ['key', 'fill', 'hemi', 'rim']) tweens.to(drain, k, want[k], ms, easeOutCubic);
    const sq = want.spot ? kingSquare(winner) : null;
    if (sq) aimSpot(sq);
    tweens.to(drain, 'spot', sq ? 1 : 0, ms, easeOutCubic);
  }

  /** Back to the live room: at once for a new game, over overBackMs for a take-back. */
  function restore(instant = false) {
    state.over = null;
    state.spotSquare = null;
    const ms = instant || reducedMotion() ? 0 : T.overBackMs;
    for (const k of ['key', 'fill', 'hemi', 'rim']) tweens.to(drain, k, 1, ms, easeOutCubic);
    tweens.to(drain, 'spot', 0, ms, easeOutCubic);
    if (instant) { tweens.to(dip, 'value', 1, 0); tweens.to(warm, 'value', warmthFor(state.meter), 0); }
  }

  const unsubs = [];
  if (bus && bus.on) {
    unsubs.push(bus.on('turn', (p) => {
      const s = p && p.side === 'b' ? 'b' : 'w';
      lean(s); tell(s);
      if (state.over) restore(false);   // a take-back resurrected the game
    }));
    unsubs.push(bus.on('local', () => { lean('w', true); tell(null, true); restore(true); }));
    unsubs.push(bus.on('capture', () => { flinch(); }));
    unsubs.push(bus.on('gameover', (p) => { tell(null); over(p && p.result, p && p.winner); }));
  }

  function update(dt) {
    tweens.update(dt);
    if (rig) rig.rotation.y = yaw.value;
    state.yaw = yaw.value;

    // the breath runs its own clock only while the meter is over the line and
    // motion is on; below it the clock resets so the next swell starts at rest
    const still = reducedMotion();
    if (state.meter >= T.breathAt && !still) state.breathMs += Math.max(0, dt) * 1000;
    else state.breathMs = 0;
    breath = still ? 0 : breathFor(state.meter, state.breathMs);
    const we = clamp01(warm.value + breath);

    // the lights
    if (lights.key) lights.key.intensity = base.key * dip.value * drain.key;
    if (lights.fill) { lights.fill.intensity = base.fill * drain.fill; lights.fill.color.lerpColors(base.fillColor, warmTo.fill, we); }
    if (lights.hemi) {
      lights.hemi.intensity = base.hemi * drain.hemi;
      lights.hemi.color.lerpColors(base.sky, warmTo.sky, we);
      lights.hemi.groundColor.lerpColors(base.ground, warmTo.ground, we);
    }
    if (lights.rim) lights.rim.intensity = base.rim * drain.rim;
    spot.intensity = T.spotIntensity * drain.spot;

    // the horizon, in all three places at once, and the haze above it
    const u = dome.material.uniforms;
    u.horizon.value.lerpColors(base.horizon, warmTo.horizon, we);
    if (scene.fog) scene.fog.color.copy(u.horizon.value);
    if (scene.background && scene.background.isColor) scene.background.copy(u.horizon.value);
    u.haze.value.lerpColors(base.haze, warmTo.haze, we);

    // the shaft: warmed, drained with the key, capped as the camera goes plumb
    if (view.camera) { view.camera.getWorldDirection(forward); state.topness = clamp01(-forward.y); }
    shaft.material.uniforms.uLevel.value = shaftLevel({ warmth: we, drain: drain.key, topness: state.topness });
  }

  /** Jump every ease to its end, for a still frame. */
  function settle() { tweens.settle(); update(0); }

  function debug() {
    const r3 = (v) => +Number(v).toFixed(3);
    return {
      side: state.side,
      yawDeg: +((yaw.value * 180) / Math.PI).toFixed(1),
      glow: { w: r3(edges.w.mat.opacity), b: r3(edges.b.mat.opacity) },
      tweens: tweens.count(),
      pool: !!pool,
      fog: scene.fog ? [scene.fog.near, scene.fog.far] : null,
      dip: r3(dip.value),
      key: lights.key ? r3(lights.key.intensity) : null,
      meter: r3(state.meter),
      warmth: r3(warm.value),
      breath: r3(breath),
      breathing: state.meter >= T.breathAt && !reducedMotion(),
      horizon: '#' + dome.material.uniforms.horizon.value.getHexString(),
      over: state.over ? { result: state.over.result, winner: state.over.winner } : null,
      drain: { key: r3(drain.key), fill: r3(drain.fill), hemi: r3(drain.hemi), rim: r3(drain.rim) },
      spot: { on: drain.spot > 0.001, square: state.spotSquare, intensity: r3(spot.intensity) },
      shaft: { level: +shaft.material.uniforms.uLevel.value.toFixed(4), topness: r3(state.topness) },
    };
  }

  function dispose() {
    for (const off of unsubs) { try { off(); } catch { /* already gone */ } }
    tweens.settle();
    scene.remove(group, spot, spot.target);
    group.traverse((o) => {
      if (o.geometry) o.geometry.dispose();
      if (o.material) { if (o.material.map) o.material.map.dispose(); o.material.dispose(); }
    });
    if (rig) { rig.remove(shaft); rig.rotation.y = 0; }
    shaft.geometry.dispose();
    shaft.material.dispose();
    spot.dispose();
    // the lights back where scene.js left them
    if (lights.key) lights.key.intensity = base.key;
    if (lights.fill) { lights.fill.intensity = base.fill; lights.fill.color.copy(base.fillColor); }
    if (lights.hemi) { lights.hemi.intensity = base.hemi; lights.hemi.color.copy(base.sky); lights.hemi.groundColor.copy(base.ground); }
    if (lights.rim) lights.rim.intensity = base.rim;
  }

  return { group, dome, floor, pool, edges, shaft, spot, update, lean, tell, setMeter, settle, debug, dispose };
}
