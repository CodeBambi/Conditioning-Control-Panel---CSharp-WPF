/* ============================================================================
 * backroom/room/scene.js - the walkable room: one renderer, one camera, the
 * walk, the room view and the render loop.
 *
 * WASD / arrows walk (Shift runs), drag looks, E visits the nearest station,
 * M toggles the room view. Collision and proximity are walk.js (pure).
 *
 * THE VISIT: `hold()` saves the pose and STOPS the loop (no rAF at all); the
 * WebGL context is kept, so `release()` puts you back on the exact spot and
 * facing on the next frame instead of decoding the room again (CONTRACT 7).
 *
 * STILL: the floor, bulbs, screens and hub freeze and the head sway is off
 * whenever the host says reduced motion or Calm, or the player asks for it.
 * ==========================================================================*/

import * as T from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
import { buildRoom } from './fixtures.js';
import { createScreens } from './screens.js';
import { START, WALK_SPEED, RUN_SPEED, step, worldDelta, nearestStation, facing } from './walk.js';

const KEYS = new Set(['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'ShiftLeft', 'ShiftRight']);

/**
 * @param {Object} o
 *   mount, stations, base, faces, ads, label(row,key), media(), still (bool),
 *   onNearest(row|null), onVisit(row), onProgress(0..1), log(msg)
 */
export async function createScene(o) {
  const say = typeof o.log === 'function' ? o.log : () => {};
  const canvas = document.createElement('canvas');
  canvas.className = 'br-canvas';
  canvas.setAttribute('aria-label', 'The Back Room');
  o.mount.appendChild(canvas);
  canvas.addEventListener('webglcontextlost', (e) => { e.preventDefault(); say('room webgl context lost'); });

  const renderer = new T.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
  const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
  renderer.setPixelRatio(dpr);
  renderer.toneMapping = T.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.05;
  const scene = new T.Scene();
  scene.background = new T.Color('#1a1125');
  scene.fog = new T.FogExp2('#201229', 0.023);
  const camera = new T.PerspectiveCamera(66, 1, 0.06, 65);
  camera.rotation.order = 'YXZ';

  // A soft studio environment, baked once and thrown away.
  const pm = new T.PMREMGenerator(renderer), studio = new T.Scene();
  studio.background = new T.Color('#54405f');
  for (const p of [[0, 4, 0], [-4, 1, 1], [4, 1, -1]]) {
    const panel = new T.Mesh(new T.PlaneGeometry(3, 3), new T.MeshBasicMaterial({ color: new T.Color(2, 1.5, 1.9), side: T.DoubleSide }));
    panel.position.set(...p); panel.lookAt(0, 0, 0); studio.add(panel);
  }
  scene.environment = pm.fromScene(studio, 0.06).texture;
  scene.environmentIntensity = 0.26;
  studio.traverse((x) => { if (x.geometry) x.geometry.dispose(); if (x.material) x.material.dispose(); });
  pm.dispose();
  scene.add(new T.HemisphereLight(0xf4c3e8, 0x40314c, 1.3));
  const fill = new T.DirectionalLight(0xffd9ed, 1.8); fill.position.set(0, 5, 5); scene.add(fill);
  const cool = new T.DirectionalLight(0xbbaeff, 0.9); cool.position.set(-4, 4, -6); scene.add(cool);
  const point = (c, p, at, d) => { const l = new T.PointLight(c, p, d, 2); l.position.set(...at); scene.add(l); };
  point(0xff65bf, 8, [-5.9, 2.7, 0], 5.5); point(0x9279ff, 8, [5.9, 2.7, 0], 5.5);
  point(0xff89d1, 10, [0, 3.9, -4.6], 8); point(0x987aff, 9, [0, 4.1, 3], 8);

  const loader = new GLTFLoader();
  loader.setMeshoptDecoder(MeshoptDecoder);
  const t0 = performance.now();
  const room = await buildRoom({ scene, loader, stations: o.stations, base: o.base, faces: o.faces, label: o.label, onProgress: o.onProgress });
  const screens = await createScreens({ meshes: room.screens, ads: o.ads, media: o.media, log: say });
  for (const h of room.hubs) h.setDpr(dpr);
  const buildMs = performance.now() - t0;

  // ---- state ----
  const pos = START.slice();
  let yaw = 0, pitch = 0, sway = 0, walkPhase = 0, ambient = 0;
  let still = !!o.still, overview = false, held = null, halted = false, suspended = false;
  let raf = 0, last = performance.now(), nearest = null, drag = null;
  const saved = { pos: null, yaw: 0, pitch: 0 };
  const keys = new Set();
  const vel = new T.Vector2(), want = new T.Vector2();
  const frames = [];

  function topDown() { camera.position.set(0, Math.max(21, 19 / camera.aspect), 0.01); camera.lookAt(0, 0, 0); }
  function resize() {
    const w = Math.max(1, o.mount.clientWidth || window.innerWidth), h = Math.max(1, o.mount.clientHeight || window.innerHeight);
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    room.auras.resize(h * dpr, camera.fov);
    if (overview) topDown();
  }
  window.addEventListener('resize', resize);

  function resetInput() { keys.clear(); vel.set(0, 0); drag = null; }
  window.addEventListener('keydown', (e) => {
    if (held || halted || suspended || e.ctrlKey || e.altKey || e.metaKey) return;
    if (KEYS.has(e.code)) { e.preventDefault(); keys.add(e.code); }
    if (e.repeat) return;
    if (e.code === 'KeyE' && !overview && nearest) { e.preventDefault(); visit(nearest); }
    if (e.code === 'KeyM') { e.preventDefault(); setOverview(!overview); }
  });
  window.addEventListener('keyup', (e) => keys.delete(e.code));
  window.addEventListener('blur', resetInput);
  canvas.addEventListener('pointerdown', (e) => {
    if (held || overview || e.button !== 0) return;
    drag = { id: e.pointerId, x: e.clientX, y: e.clientY };
    try { canvas.setPointerCapture(e.pointerId); } catch (err) { /* noop */ }
  });
  canvas.addEventListener('pointermove', (e) => {
    if (!drag || drag.id !== e.pointerId || overview || held) return;
    yaw -= (e.clientX - drag.x) * 0.003;
    pitch = T.MathUtils.clamp(pitch - (e.clientY - drag.y) * 0.003, -1.12, 1.2);
    drag.x = e.clientX; drag.y = e.clientY;
  });
  for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) canvas.addEventListener(ev, () => { drag = null; });

  function setNearest(row) {
    if (row === nearest) return;
    nearest = row;
    try { if (o.onNearest) o.onNearest(row); } catch (e) { say('onNearest threw: ' + e); }
  }

  function visit(row) {
    if (!row || held || halted) return;
    try { o.onVisit(row); } catch (e) { say('onVisit threw: ' + ((e && e.message) || e)); }
  }

  function setOverview(on) {
    if (held || halted || !!on === overview) return;
    resetInput();
    overview = !!on;
    if (room.ceiling) room.ceiling.visible = !overview;
    if (overview) { saved.pos = pos.slice(); saved.yaw = yaw; saved.pitch = pitch; topDown(); setNearest(null); }
    else if (saved.pos) { pos.splice(0, 3, ...saved.pos); yaw = saved.yaw; pitch = saved.pitch; }
  }

  /** Stand at a station's approach point, facing it. */
  function go(row) {
    if (!row || held) return;
    if (overview) { overview = false; if (room.ceiling) room.ceiling.visible = true; }
    resetInput();
    pos.splice(0, 3, ...row.approach);
    ({ yaw, pitch } = facing(row.approach, row.look));
  }

  function frame(now) {
    raf = 0;
    if (held || halted || suspended) return;
    raf = requestAnimationFrame(frame);
    const raw = now - last;
    last = now;
    if (document.hidden) return;
    const dt = Math.min(0.035, Math.max(0, raw / 1000));
    frames.push(raw);
    if (frames.length > 240) frames.shift();
    if (!overview) {
      let x = (keys.has('KeyD') || keys.has('ArrowRight') ? 1 : 0) - (keys.has('KeyA') || keys.has('ArrowLeft') ? 1 : 0);
      let z = (keys.has('KeyS') || keys.has('ArrowDown') ? 1 : 0) - (keys.has('KeyW') || keys.has('ArrowUp') ? 1 : 0);
      const l = Math.max(1, Math.hypot(x, z)); x /= l; z /= l;
      const speed = keys.has('ShiftLeft') || keys.has('ShiftRight') ? RUN_SPEED : WALK_SPEED;
      vel.lerp(want.set(x * speed, z * speed), 1 - Math.exp(-dt * 12));
      const [dx, dz] = worldDelta(yaw, vel.x * dt, vel.y * dt);
      step(pos, dx, dz, o.stations);
      sway = T.MathUtils.damp(sway, still ? 0 : Math.min(1, vel.length() / WALK_SPEED), 10, dt);
      walkPhase += vel.length() * dt * 3.1;
      camera.position.set(pos[0], pos[1] + Math.sin(walkPhase * 2) * 0.01 * sway, pos[2]);
      camera.rotation.set(pitch + Math.sin(walkPhase * 2) * 0.002 * sway, yaw, Math.sin(walkPhase) * 0.0035 * sway, 'YXZ');
      setNearest(nearestStation(pos, o.stations));
    }
    if (!still) ambient += dt;
    room.update(dt, ambient, still);
    camera.updateMatrixWorld();
    screens.update(ambient, overview ? null : camera, still);
    renderer.render(scene, camera);
  }

  function run() {
    if (raf || held || halted || suspended) return;
    last = performance.now();
    raf = requestAnimationFrame(frame);
  }
  const stop = () => { if (raf) cancelAnimationFrame(raf); raf = 0; };

  resize();
  run();
  screens.deal(() => ambient).catch(() => {});

  return {
    renderer, camera, scene, buildMs,
    /** A station is taking the screen: keep the pose, stop drawing, keep the context. */
    hold() { if (held) return; held = { pos: pos.slice(), yaw, pitch }; resetInput(); stop(); setNearest(null); },
    /** Back from a station: the exact spot and facing, on the next frame. */
    release() {
      if (!held) return;
      pos.splice(0, 3, ...held.pos); yaw = held.yaw; pitch = held.pitch;
      held = null; resetInput(); run();
    },
    pause(on) { suspended = !!on; if (suspended) { stop(); resetInput(); } else run(); },
    halt() { halted = true; stop(); resetInput(); },
    setStill(on) { still = !!on; },
    /** Repaint one fixture label, e.g. the wheel's screen for MUST HIT (10.16.E). */
    setLabel(rowKey, node, text) { return room.setLabel(rowKey, node, text); },
    setOverview, go, visit,
    pose(p, y = 0, tilt = 0) { pos.splice(0, 3, ...p); yaw = y; pitch = tilt; },
    get nearest() { return nearest; },
    get overview() { return overview; },
    get held() { return !!held; },
    /** Test seam: plain numbers only. */
    debug() {
      const sorted = frames.slice().sort((a, b) => a - b);
      return {
        position: pos.slice(), yaw, pitch, overview, held: !!held, running: !!raf, still,
        nearest: nearest ? nearest.key : null, fixtures: room.fixtures, bulbs: room.bulbs, screens: room.screens.length,
        pictures: screens.pictures, animation: screens.animation, calls: renderer.info.render.calls, triangles: renderer.info.render.triangles,
        floorAngle: room.floor ? room.floor.material.uniforms.angle.value : null, ambient, sway,
        frameMedian: sorted.length ? sorted[Math.floor(sorted.length / 2)] : null, buildMs: Math.round(buildMs),
        ceiling: room.ceiling ? room.ceiling.visible : null,
        labels: Object.fromEntries(Array.from(room.labels, ([k, v]) => [k, v.text])),
      };
    },
  };
}
