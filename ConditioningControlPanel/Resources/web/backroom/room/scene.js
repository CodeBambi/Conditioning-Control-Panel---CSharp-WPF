/* ============================================================================
 * backroom/room/scene.js - the walkable room: one renderer, one camera, the
 * walk, the room view and the render loop.
 *
 * WASD walks (Shift runs). The mouse looks under pointer lock: a click on the canvas while walking captures it,
 * Esc frees it, the next click takes it again; click-drag stays as the fallback where the lock is refused. The
 * arrow keys turn and tilt at a steady rate. E visits the nearest station, whose fixture glows gold while it is
 * the one E would take. Collision and proximity are walk.js (pure).
 *
 * THE VISIT: `hold()` saves the pose and STOPS the loop (no rAF at all); the
 * WebGL context is kept, so `release()` puts you back on the exact spot and
 * facing on the next frame instead of decoding the room again (CONTRACT 7).
 *
 * STILL: the floor, bulbs, screens and hub freeze and the head sway is off
 * whenever the host says reduced motion or Calm, or the player asks for it.
 * ==========================================================================*/

import * as T from 'three';
import { createRenderBudget } from './render-budget.js';
import { quality } from '../shared/quality.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
import { buildRoom } from './fixtures.js';
import { createScreens } from './screens.js';
import { createSlotEmiIdle } from './slot-emi-idle.js';
import { createEmiInteraction, TAP_SLOP } from './emi-interaction.js';
import { createCasinoDecor } from './casino-decor.js';
import { createMemorabilia } from './memorabilia.js';
import { createMemorabiliaViewer } from './memorabilia-viewer.js';
import { createWelcomePlacards } from './welcome-placards.js';
import { cardCounts } from '../stations/cards/layout-3d.js';
import { slotSeat } from './slot-seat.js';
import { seatPose, easeSeat, shortAngle } from './seat-camera.js';
import { createTouchControl } from './touch-control.js';
import { createCustomization } from './customization.js';
import { isBackKey, isBackwardMove, isStationHit } from './leave-intent.js';
import { stageRect, ndcIn, viewportPageRect } from './stage-rect.js';
import { START, WALK_SPEED, RUN_SPEED, step, worldDelta, nearestStation, facing } from './walk.js';

const KEYS = new Set(['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'ShiftLeft', 'ShiftRight']);
/** Mouse look under pointer lock: radians per pixel of mouse movement. */
const LOOK_SENSITIVITY = 0.0022;
/** Arrow-key look: radians per second, left/right turn and up/down tilt. */
const KEY_YAW_RATE = 1.8, KEY_PITCH_RATE = 1.2;
const PITCH_MIN = -1.12, PITCH_MAX = 1.2;
/** A second refused lock in a row means the host has none (or forbids it): stay on click-drag and taps from then on. */
const LOCK_REFUSALS = 4;
const LOCK_COOLDOWN_MS = 1500;
/** The glow on the fixture E would visit (desk tester, 2026-09-18: nothing said what could be played). */
const GLOW = new T.Color('#ffcf6b');

/**
 * @param {Object} o
 *   mount, stations, base, faces, ads, label(row,key), media(), still (bool),
 *   onNearest(row|null), onVisit(row), onProgress(0..1), log(msg),
 *   arcade (bool: the unlocked cabinet is a door), onArcade(game),
 *   onCard(page): a tap on one of the counter's placards (welcome-placards.js) asks for the first-visit card at that page
 */
export async function createScene(o) {
  const say = typeof o.log === 'function' ? o.log : () => {};
  const canvas = document.createElement('canvas');
  canvas.className = 'br-canvas';
  canvas.setAttribute('aria-label', 'The Back Room');
  o.mount.appendChild(canvas);
  // A lost context is the one way this canvas goes still while the HUD around it keeps working. preventDefault
  // asks the browser for a restore; three.js re-initialises on the restored event, and the loop is run() again
  // because the browser hands frames to nobody while a context is gone.
  let contextLost = false;
  canvas.addEventListener('webglcontextlost', (e) => { e.preventDefault(); contextLost = true; say('room webgl context lost'); });
  canvas.addEventListener('webglcontextrestored', () => { contextLost = false; say('room webgl context restored'); run(); });

  const budget = createRenderBudget(navigator, window.devicePixelRatio || 1);
  const renderer = new T.WebGLRenderer({ canvas, antialias: true, powerPreference: 'default' });
  let dpr = budget.dpr(o.mount.clientWidth, o.mount.clientHeight);
  renderer.setPixelRatio(dpr);
  /* TONE MAPPING. ACES Filmic was taking half the chroma off the four hues this room is built out of - measured
     through three's own RRTAndODTFit: lavender #b99cff and mint #5fffd0 came out at 0.48 and 0.51 saturation,
     gold #ffcf6b at 0.64. That is the "not quite vibrant" the owner read (2026-09-16). Neutral holds them at
     0.88-0.92 for the same perceived lightness. The exposure goes UP with the swap and that is not a brightness
     grab: ACES prescales internally by exposure/0.6, so 1.05 was really 1.75. */
  renderer.outputColorSpace = T.SRGBColorSpace;   // the default, said out loud, so a vendor bump cannot wash the room out
  renderer.toneMapping = T.NeutralToneMapping;
  renderer.toneMappingExposure = 1.7;
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
  /* THE WIN ECHO's motion (CONTRACT 10.22). `still` and `reduced` are two different questions and the
   * plan answers them differently: Calm takes the room's decoration and keeps the news, reduced motion
   * takes the state. `lite` is the same board test the render budget already made. Read live, never
   * captured: a player turning Calm on mid-echo is answered on the next frame. */
  const motion = () => {
    const m = o.cameraMotion?.() || {};
    return { still, off: !!m.off, lite: quality.performance,
      reduced: !!m.reduced || matchMedia('(prefers-reduced-motion: reduce)').matches };
  };
  const room = await buildRoom({ scene, loader, stations: o.stations, base: o.base, faces: o.faces, label: o.label, onProgress: o.onProgress, motion, arcade: !!o.arcade });
  const decor = createCasinoDecor({ scene });
  // The eight polaroids advertise vault cards, so their words are lexicon keys like the rest of
  // the room's chrome (LAW VII). The wall cannot reach the room's lookup on its own.
  const memorabilia = createMemorabilia({ scene, lex: o.lex });
  // The first-visit card's two pages, framed on the counter's apron; a tap reopens the card (onCard).
  const placards = createWelcomePlacards({ scene, lex: o.lex });
  const documents = createMemorabiliaViewer({ backLabel: o.lex('br_back', 'Back'),
    onOpen() { held = { pos: pos.slice(), yaw, pitch }; freeLook(); resetInput(); interaction.dismiss(); stop(); setNearest(null); },
    onClose() { held = null; resetInput(); if (!halted) run(); }
  });
  let customization, catalogueView=null;
  /* The viewport the last frame rendered the room into, kept for the picks: null while the room owns the
   * whole canvas, which is every frame but the ones with the Room Service panel up. */
  let pass = null;
  customization = await createCustomization({scene,loader,room,onPreview:view=>{catalogueView=view;},base:o.base,mount:o.mount,lex:o.lex,canvas,camera,isActive:()=>!pendingVisit&&!transition&&!seated&&!held&&!halted&&!suspended&&!customization?.opened});
  const screens = await createScreens({ meshes: [...room.screens,...customization.screens], ads: o.ads, media: o.media, log: say });
  // Subtle cartridge refraction otherwise renders the entire room a second time.
  scene.traverse(node => { for (const m of [].concat(node.material || [])) {
    if (m.transmission > 0) { m.transmission = 0; m.needsUpdate = true; }
  }});
  for (const h of room.hubs) h.setDpr(dpr);
  const buildMs = performance.now() - t0;

  // ---- state ----
  const pos = START.slice();
  let yaw = 0, pitch = 0, sway = 0, walkPhase = 0, ambient = 0;
  let still = !!o.still, held = null, seated = null, halted = false, suspended = false;
  // The mouse under pointer lock: `locked` follows pointerlockchange, `unlockedAt` is when it was last let go (main.js
  // reads it so the Esc that freed the mouse is not also the Esc that leaves the room), `lockRefused` after LOCK_REFUSALS.
  let locked = false, unlockedAt = -Infinity, lockErrors = 0, lockRefused = false;
  let raf = 0, last = performance.now(), lastTick = last, nearest = null, drag = null, tap = null, standTap = null, leaveAsked = false;
  let pendingVisit=false, transition=null, viewOffset=0, viewOffsetX=0, arrival=Promise.resolve(true), cardHands=1, counts={d:2,0:2}, composition='';
  const views = new Set();
  let slotShape=null;
  function setSlotStretch(factor,xFactor=slotShape?.factorX||1){
    if(!slotShape)return;
    slotShape.factor=factor;slotShape.factorX=xFactor;slotShape.holder.scale.y=slotShape.baseY*factor;slotShape.holder.scale.x=slotShape.baseX*xFactor;
    slotShape.holder.userData.slotStretch=factor;slotShape.holder.userData.slotStretchX=xFactor;
    if(slotShape.emi){slotShape.emi.scale.y=slotShape.emiY/factor;slotShape.emi.scale.x=slotShape.emiX/xFactor;}
    const handle=slotShape.holder.getObjectByName('chess_handle_socket');
    if(handle)handle.scale.set(1/xFactor,1/factor,1);
    slotShape.holder.updateMatrixWorld(true);
  }
  const ray = new T.Raycaster(), pointer = new T.Vector2();
  const keys = new Set();
  const vel = new T.Vector2(), want = new T.Vector2();
  const canWalk = () => !pendingVisit && !transition && !seated && !held && !halted && !suspended && !customization.opened && !document.hidden;
  /* LEAVING BY HAND (leave-intent.js). Seated at a station, a tap that lands on the room instead of the
   * station and a step backwards both stand you up the way the Back chip does: o.onLeave runs the room's
   * own Back path, so the station settles first and the camera walks back to where you stood (Law VI). */
  const canLeave = () => o.canLeave?.() !== false && !!seated && !held && !halted && !suspended && !transition && !pendingVisit && !leaveAsked && !customization.opened;
  const touch = createTouchControl({ mount: o.mount, onReset: () => vel.set(0, 0) });
  const frames = [];
  const racingRow = { id: 'race', key: 'race', name: 'Racing Thoughts', labelKey: 'br_station_race', state: 'live', approach: [4.1, 1.65, -5.7], look: [4.1, 1.2, -7.15] };
  const stationRows = [...o.stations, customization.row, racingRow];
  const previewTarget = new T.Vector3();
  const roofFloor=room.ceiling?new T.Box3().setFromObject(room.ceiling).min.y:Infinity;
  const roofMaterials=[];let roofAlpha=1;
  function fadeRoof(){
    if(!room.ceiling)return;
    const alpha=1-T.MathUtils.clamp((camera.position.y-roofFloor+.9)/.7,0,1);
    if(alpha===roofAlpha)return;roofAlpha=alpha;
    if(!roofMaterials.length)room.ceiling.traverse(n=>{if(n.material){const original=n.material;const copies=[].concat(original).map(m=>m.clone());roofMaterials.push({node:n,original,copies});}});
    for(const {node,original,copies} of roofMaterials){
      for(const m of copies){m.transparent=true;m.opacity=alpha;m.depthWrite=false;}
      node.material=alpha===1?original:Array.isArray(original)?copies:copies[0];
    }
    room.ceiling.visible=alpha>.001;
  }

  const emiClicks = new Map();
  const slotEmis = o.stations.filter(row => row.id === 'slot').map((row, index) => createSlotEmiIdle({
    fixture: room.holders.get(row.key), phase: index * 2.7, onClick: kind => emiClicks.get(row.key)?.(kind)
  })).filter(Boolean);
  const interaction = createEmiInteraction({ canvas, camera, scene, emis: [...room.emis, ...slotEmis], mount: o.mount, label: o.lex,
    isActive: () => !pendingVisit && !transition && !held && !halted && !suspended && !customization.opened,
    // Law VI: a still room keeps every NPC at rest, so a click gets the bark without the gesture.
    canGesture: () => !still });

  /* ONE MEASUREMENT, APPLIED ONCE. `sized` is the measurement this function last put into the renderer.
   * One turn of a phone raises a burst of events (resize, orientationchange and visualViewport's own
   * resize all fire for the same rotation) and frame() measures again on every frame, so without the key
   * a seated camera would be re-posed from under a running transition sixty times a second. Keep the key
   * on the measurement rather than on a flag: it is what makes calling this often free and safe. */
  let sized = '';
  function resize() {
    const w = Math.max(1, o.mount.clientWidth || window.innerWidth), h = Math.max(1, o.mount.clientHeight || window.innerHeight);
    const ratio = budget.dpr(w, h), key = w + 'x' + h + '@' + ratio;
    if (key === sized) return;
    sized = key;
    dpr = ratio; renderer.setPixelRatio(dpr);
    renderer.setSize(w, h, false);
    for (const hub of room.hubs) hub.setDpr(dpr);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    room.auras.resize(h * dpr, camera.fov);
    if (seated?.row) {
      const target=gamePose(seated.row);
      if(transition)transition.to={fov:66,stretch:1,stretchX:1,...target};
      else {if(target.fov){camera.fov=target.fov;camera.updateProjectionMatrix();setSlotStretch(target.stretch||1,target.stretchX||1);}pos.splice(0,3,...target.pos);yaw=target.yaw;pitch=target.pitch;viewOffset=target.offset;viewOffsetX=target.offsetX;}
    }
  }
  /* ROTATION ON A PHONE. iOS Safari raises `resize` while the layout is still the PRE-rotation one, so a
   * single read leaves the drawing buffer landscape-sized after the box on screen has gone portrait. The
   * room then paints into a sliver of its own buffer and the rest of the canvas stays dark (the owner's
   * split screenshot, 2026-09-17), and every pointer normalised through the canvas rect lands somewhere
   * else, which is the same rotation read as dead input. The cure is to measure AGAIN once the layout has
   * settled: visualViewport is the viewport that actually moved, orientationchange covers where it is
   * missing, and the deferred read catches the browsers that report the old box at both. frame() measures
   * every frame too, so a drawing room heals itself within a frame; these listeners are what wake a room
   * that has stopped drawing, and resize() applies a measurement once so none of them can double-fire. */
  let settling = 0;
  function settle() {
    resize(); run();
    if (settling) return;
    settling = setTimeout(() => { settling = 0; resize(); run(); }, 300);
  }
  window.addEventListener('resize', settle);
  window.addEventListener('orientationchange', settle);
  window.visualViewport?.addEventListener('resize', settle);

  /** `keepStick`: a seat keeps the touch stick alive and centred by the player's own finger, so a push back still reads. */
  function resetInput(keepStick) { keys.clear(); vel.set(0, 0); drag = tap = null; if (!keepStick) { standTap = null; touch.reset(); } touch.setEnabled(canWalk() || (canLeave() && seated?.row.id !== 'slot')); }
  function leaveSeat() {
    if (!canLeave()) return;
    leaveAsked = true; resetInput();
    try { if (o.onLeave) o.onLeave(seated.row); else unseat(); } catch (e) { say('onLeave threw: ' + ((e && e.message) || e)); }
  }
  window.addEventListener('keydown', (e) => {
    if (e.target?.closest?.('input,textarea,select,[contenteditable]:not([contenteditable="false"])')) { resetInput(); return; }
    if (e.ctrlKey || e.altKey || e.metaKey) return;
    // A seat has no walk: S or the down arrow there is a step back out of the station, never a step in the room.
    if (isBackKey(e.code) && !e.repeat && canLeave()) { e.preventDefault(); leaveSeat(); return; }
    if (pendingVisit || transition || seated || held || halted || suspended || customization.opened) return;
    if (KEYS.has(e.code)) { e.preventDefault(); keys.add(e.code); }
    if (e.repeat) return;
    if (e.code === 'KeyE' && nearest) { e.preventDefault(); visit(nearest); }
  });
  window.addEventListener('keyup', (e) => keys.delete(e.code));
  document.addEventListener('focusin', e => { if(e.target?.closest?.('input,textarea,select,[contenteditable]')) resetInput(); });
  window.addEventListener('blur', () => resetInput());   // an Event is not a keepStick
  // A finger rolls a little between down and up (a mouse hardly moves): under TAP_SLOP css px (emi-interaction.js) it is a tap, over it a look.
  canvas.addEventListener('pointerdown', (e) => {
    if (!canWalk() || e.button !== 0) return;
    // A pointer whose up never arrived (a finger lifted over the browser chrome) must not hold the room forever.
    if (drag && (drag.id === e.pointerId || (canvas.hasPointerCapture && !canvas.hasPointerCapture(drag.id)))) drag = null;
    if (drag) return;
    drag = tap = { id: e.pointerId, x: e.clientX, y: e.clientY, startX:e.clientX, startY:e.clientY, moved:false };
    /* A MOUSE CLICK TAKES THE MOUSE (desk tester, 2026-09-18: click-drag to look "is terrible"). The capturing click is
     * not a tap: it only takes the pointer, so a click to get the mouse back after Esc cannot also walk you into whatever
     * the crosshair happened to rest on. Fingers and pens never lock; a refused lock leaves click-drag and taps as they were. */
    if (e.pointerType === 'mouse' && !locked && !lockRefused && typeof canvas.requestPointerLock === 'function') {
      tap = null;
      try {
        const p = canvas.requestPointerLock();
        if (p && typeof p.catch === 'function') p.catch(() => lockFailed());
      } catch (err) { lockFailed(); }
    }
    try { canvas.setPointerCapture(e.pointerId); } catch (err) { /* noop */ }
  });
  // Chromium refuses a re-lock for ~1 s after an Esc exit; that is a cooldown, not a refusal, so it never counts.
  function lockFailed() { if (performance.now() - unlockedAt < LOCK_COOLDOWN_MS) return; if (++lockErrors >= LOCK_REFUSALS) lockRefused = true; }
  function freeLook() { if (document.pointerLockElement === canvas) { try { document.exitPointerLock(); } catch (err) { /* noop */ } } }
  document.addEventListener('pointerlockchange', () => {
    const now = document.pointerLockElement === canvas;
    if (locked && !now) unlockedAt = performance.now();
    if (now) lockErrors = 0;
    locked = now;
    canvas.classList.toggle('is-locked', locked);
  });
  document.addEventListener('pointerlockerror', () => { lockFailed(); say('pointer lock refused'); });
  /** Where a click lands: the pointer, or the crosshair while the mouse is captured (its position is frozen then). */
  function clickPoint(e) {
    if (!locked) return { x: e.clientX, y: e.clientY };
    const r = canvas.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  }
  function turn(dyaw, dpitch) { yaw -= dyaw; pitch = T.MathUtils.clamp(pitch - dpitch, PITCH_MIN, PITCH_MAX); }
  canvas.addEventListener('pointermove', (e) => {
    if (seated || held || customization.opened) return;
    // Invert camera (o.invertLook, the room's Options switch): the mouse moves the world instead of the camera, both axes.
    const dir = o.invertLook?.() ? -1 : 1;
    // Captured: the movement is the look, no button held. The client position is frozen under a lock, so a tap stays a tap.
    if (locked) { if (canWalk()) turn(e.movementX * LOOK_SENSITIVITY * dir, e.movementY * LOOK_SENSITIVITY * dir); return; }
    if (!drag || drag.id !== e.pointerId) return;
    drag.moved ||= Math.hypot(e.clientX-drag.startX,e.clientY-drag.startY)>TAP_SLOP;
    if(!drag.moved)return;
    turn((e.clientX - drag.x) * 0.003 * dir, (e.clientY - drag.y) * 0.003 * dir);
    drag.x = e.clientX; drag.y = e.clientY;
  });
  canvas.addEventListener('pointerup',e=>{
    // The tap keeps its own record: Safari can drop the capture (lostpointercapture) before the up clears `drag`.
    const start=tap;tap=null;
    if(e.__brStatueTouch)return;
    if(!canWalk()||start?.id!==e.pointerId||start.moved||Math.hypot(e.clientX-start.startX,e.clientY-start.startY)>TAP_SLOP)return;
    // The closest visible surface prevents reading a document through a cabinet or wall.
    const surface = pickAt(e, scene.children).find(hit => {
      for (let node = hit.object; node; node = node.parent) if (!node.visible) return false;
      return hit.object.isMesh && hit.object.material?.depthWrite !== false;
    });
    if (surface?.object.userData.document) { drag = null; documents.open(surface.object.userData.document); return; }
    if (surface?.object.userData.welcomePage != null) { drag = null; try { o.onCard?.(surface.object.userData.welcomePage); } catch (err) { say('onCard threw: ' + ((err && err.message) || err)); } return; }
    // A mascot stands inside its fixture, often behind its glass: a tap that reaches an NPC is the bark (emi-interaction), never a visit.
    const at=clickPoint(e);
    if(interaction.npcAt(at.x,at.y))return;
    // The arcade cabinet before the stations: it stands beside the counter, and the counter's screen box (below, with its
    // finger's margin) would otherwise claim a tap that the exact ray already put on the cabinet.
    const arcade=arcadeAt(e);
    if(arcade){drag=null;try{o.onArcade?.(arcade);}catch(err){say('onArcade threw: '+((err&&err.message)||err));}return;}
    const row=stationAt(e);
    if(row){drag=null;visit(row);}
  });
  canvas.addEventListener('pointercancel',e=>{if(tap?.id===e.pointerId)tap=null;});
  /* THE WAY OUT BY TAP. Seated, the station's own DOM keeps its buttons (room.css: the seat sheet is
   * pointer-events: none but its controls are not), so anything that reaches the canvas is either the
   * station's own meshes - ignored here, the station handles them - or the room, and the room stands you up. */
  const exitEdge = e => { const r=canvas.getBoundingClientRect(); return seated?.row.id !== 'roulette' || e.clientY >= r.bottom - Math.min(60,r.height*.08); };
  canvas.addEventListener('pointerdown', (e) => { if (e.button === 0 && canLeave() && exitEdge(e)) standTap = { id: e.pointerId, x: e.clientX, y: e.clientY }; });
  canvas.addEventListener('pointerup', (e) => {
    const start = standTap; standTap = null;
    if (!start || start.id !== e.pointerId || !canLeave() || !exitEdge(e)) return;
    // The same slop as every other tap in the room: a drag on the wheel rim or across the mat is not a click.
    if (Math.hypot(e.clientX - start.x, e.clientY - start.y) > TAP_SLOP) return;
    if ([...views].some((view) => view.coversRoom)) return;   // no room on screen to be tapped
    const hit = pickAt(e, scene.children).find(h => { for (let n = h.object; n; n = n.parent) if (!n.visible) return false; return true; });
    if (hit && isStationHit(hit.object, room.holders.get(seated.row.key))) return;
    leaveSeat();
  });
  for (const ev of ['pointercancel', 'lostpointercapture']) canvas.addEventListener(ev, (e) => { if (standTap?.id === e.pointerId) standTap = null; });
  for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) canvas.addEventListener(ev, (e) => { if (drag?.id === e.pointerId) drag = null; });

  /* Any tap on a fixture enters it: the exact ray first, then the fixture whose screen box (the visible meshes'
   * world box projected, plus a finger's margin) holds the point, the nearest to the camera when boxes overlap.
   * The vending machine is the Room Service row (customization.js). Boxes are cached: fixtures do not move. */
  const TAP_MARGIN = 24, bounds = new Map(), corner = new T.Vector3();
  function fixtureOf(row) { if (row.key === 'race') return room.prizes.cabinet; return row.key === 'customization' ? scene.getObjectByName('customization_vending') : room.holders.get(row.key); }
  function rowOf(node) { for (let n = node; n; n = n.parent) if (n.name === 'racing_cabinet' || n === room.prizes.cabinet) return racingRow; for (let n = node; n; n = n.parent) { const row = stationRows.find(r => fixtureOf(r) === n); if (row) return row; } return null; }
  function worldBox(node) {
    let box = bounds.get(node);
    if (box) return box;
    box = new T.Box3(); node.updateWorldMatrix(true, false);
    node.traverseVisible(m => {
      if (!m.isMesh || m.isInstancedMesh || !m.geometry) return;
      if (!m.geometry.boundingBox) m.geometry.computeBoundingBox();
      const b = m.geometry.boundingBox; if (!b) return;
      for (let i = 0; i < 8; i++) box.expandByPoint(corner.set(i & 1 ? b.max.x : b.min.x, i & 2 ? b.max.y : b.min.y, i & 4 ? b.max.z : b.min.z).applyMatrix4(m.matrixWorld));
    });
    bounds.set(node, box); return box;
  }
  function stationAt(e) {
    const hit = pickAt(e, scene.children).find(h => { for (let n = h.object; n; n = n.parent) if (!n.visible) return false; return true; });
    if (hit) {
      // A bulb lives in a scene-level InstancedMesh batch (fixtures.js), so its station comes from the instance, not the parents.
      const batched = hit.object.isInstancedMesh ? o.stations.find(r => r.key === hit.object.userData.rows?.[hit.instanceId]) : null;
      const row = batched || rowOf(hit.object);
      if (row) return row;
    }
    const rect = canvas.getBoundingClientRect();
    if (!rect.width || !rect.height) return null;
    const at = clickPoint(e);
    const px = at.x - rect.left, py = at.y - rect.top;
    camera.updateMatrixWorld();
    let best = null, bestDistance = Infinity;
    for (const row of stationRows) {
      const node = fixtureOf(row);
      if (!node || !node.visible) continue;
      const box = worldBox(node);
      if (box.isEmpty()) continue;
      let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity, behind = false;
      for (let i = 0; i < 8 && !behind; i++) {
        corner.set(i & 1 ? box.max.x : box.min.x, i & 2 ? box.max.y : box.min.y, i & 4 ? box.max.z : box.min.z).applyMatrix4(camera.matrixWorldInverse);
        if (corner.z > -camera.near) { behind = true; break; }   // a corner behind the eye has no place on the screen: the exact ray alone serves here
        corner.applyMatrix4(camera.projectionMatrix);
        const sx = (corner.x + 1) * rect.width / 2, sy = (1 - corner.y) * rect.height / 2;
        minX = Math.min(minX, sx); maxX = Math.max(maxX, sx); minY = Math.min(minY, sy); maxY = Math.max(maxY, sy);
      }
      if (behind || px < minX - TAP_MARGIN || px > maxX + TAP_MARGIN || py < minY - TAP_MARGIN || py > maxY + TAP_MARGIN) continue;
      const distance = box.distanceToPoint(camera.position);
      if (distance < bestDistance) { best = row; bestDistance = distance; }
    }
    return best;
  }

  /* THE ARCADE CABINET (prize-display.js). The unlocked cabinet is a small prop, not a fixture: the exact ray only, no
   * screen-box guess, and only while it is on the floor (demo owned) and tagged as a door (a hosted room). */
  function arcadeAt(e) {
    const cabinet = room.prizes?.cabinet;
    if (!cabinet || !cabinet.visible || !cabinet.userData.arcade) return null;
    return pickAt(e, [cabinet]).length ? cabinet.userData.arcade : null;
  }

  function setNearest(row) {
    if (row === nearest) return;
    nearest = row;
    glowTarget(row);
    try { if (o.onNearest) o.onNearest(row); } catch (e) { say('onNearest threw: ' + e); }
  }

  /* THE FIXTURE E WOULD TAKE GLOWS. Its meshes get a gold emissive pulse on CLONED materials, so a material three
   * cabinets share (the slots) lights one cabinet only, and the original goes back the moment the player steps away.
   * Screens, NPCs and the instanced batches are left alone: other code owns their materials. No post-processing. */
  const glows = new Map();   // fixture node -> [{ node, original, copies }]
  const glowSkip = new Set([...room.screens, ...customization.screens]);
  let glow = null;
  function glowSet(node) {
    let list = glows.get(node);
    if (list) return list;
    list = [];
    node.traverse((m) => {
      if (!m.isMesh || m.isInstancedMesh || glowSkip.has(m) || !m.material) return;
      for (let n = m; n && n !== node; n = n.parent) if (/^emi/.test(n.name || '')) return;
      const original = m.material, mats = [].concat(original);
      if (!mats.every((x) => x && x.emissive)) return;
      const copies = mats.map((x) => { const c = x.clone(); c.userData.brGlow = { emissive: x.emissive.clone(), intensity: x.emissiveIntensity }; return c; });
      list.push({ node: m, original, copies: Array.isArray(original) ? copies : copies[0] });
    });
    glows.set(node, list);
    return list;
  }
  function glowTarget(row) {
    if (glow) { for (const g of glow) if (g.node.material === g.copies) g.node.material = g.original; glow = null; }
    const node = row ? fixtureOf(row) : null;
    if (!node) return;
    glow = glowSet(node);
    for (const g of glow) g.node.material = g.copies;
  }
  function glowPulse() {
    if (!glow) return;
    const k = still ? 0.55 : 0.45 + 0.25 * Math.sin(ambient * 5);
    for (const g of glow) for (const m of [].concat(g.copies)) {
      const base = m.userData.brGlow;
      m.emissive.copy(base.emissive).lerp(GLOW, k);
      m.emissiveIntensity = Math.max(base.intensity, 0.7);
    }
  }

  function visit(row) {
    if (!row || transition || seated || held || halted) return;
    if(row.key==='customization'){freeLook();resetInput();customization.open();touch.setEnabled(false);return;}
    try { o.onVisit(row); } catch (e) { say('onVisit threw: ' + ((e && e.message) || e)); }
  }

  /** Walk the camera to a station's approach point, facing it (the Stations list). Resolves true once there, false when
   * something interrupted the trip: main.js visits on true, so the list is a way INTO a game, not a map. */
  function go(row) {
    if (!row || held || seated || pendingVisit || halted) return Promise.resolve(false);
    freeLook(); resetInput(); setNearest(null);
    return moveCamera({pos:row.approach.slice(),...facing(row.approach,row.look),offset:0,offsetX:0});
  }

  function frame(now) {
    raf = 0;
    if (held || halted || suspended) return;
    raf = requestAnimationFrame(frame);
    const raw = now - last;
    touch.setEnabled(canWalk() || (canLeave() && seated?.row.id !== 'slot'));
    const active = !!transition || !!seated || keys.size > 0 || drag || locked || touch.value.x || touch.value.z || customization.opened;
    const gap = 1000 / (active && !budget.mobile && !quality.performance ? 60 : 30);
    if (raw < gap - 1) return;
    last = raw < gap ? now : now - (raw % gap);
    if (document.hidden) return;
    quality.sample(now - lastTick, gap, !seated && !transition);
    if (budget.sample(now - lastTick, gap)) resize();
    const dt = Math.min(0.1, Math.max(0, (now - lastTick) / 1000));
    const frameElapsed = now - lastTick;
    lastTick = now;
    frames.push(frameElapsed);
    if (frames.length > 240) frames.shift();
    if(seated?.row.id==='cards'){
      const view=scene.getObjectByName('cards_runtime')?.userData.debug?.();
      const hands=view?.hands||1, next=cardCounts(view?.cards), key=JSON.stringify([hands,next]);
      if(key!==composition){composition=key;cardHands=hands;counts=next;if(view)moveCamera(gamePose(seated.row),700);}
    }
    if(seated?.row.id==='roulette'){
      // The mat asks for its frame ('mat' while bets are open, 'table' once the ball runs); only a pose that differs moves.
      const want=scene.getObjectByName('roulette_runtime_mat')?.userData.frame;
      if(want&&want!==seated.frame){
        seated.frame=want;const target=gamePose(seated.row);
        if(target.pos.some((v,i)=>Math.abs(v-pos[i])>1e-4)||Math.abs(target.yaw-yaw)>1e-4||Math.abs(target.pitch-pitch)>1e-4||Math.abs(target.offset-viewOffset)>1e-4)moveCamera(target,700);
      }
    }
    // A seat keeps the stick: pushed back it stands you up, the same as S (leave-intent.js).
    if (seated && isBackwardMove(touch.value) && canLeave()) leaveSeat();
    if(customization.opened || seated || transition)resetInput(!!seated && canLeave());
    if(transition){
      const tr=transition; tr.elapsed+=dt*1000;
      const t=o.cameraMotion?.().off?1:Math.min(1,tr.elapsed/tr.duration), k=easeSeat(t);
      pos.splice(0,3,...tr.from.pos.map((v,i)=>v+(tr.to.pos[i]-v)*k));
      yaw=tr.from.yaw+(shortAngle(tr.from.yaw,tr.to.yaw)-tr.from.yaw)*k;
      pitch=tr.from.pitch+(tr.to.pitch-tr.from.pitch)*k; viewOffset=tr.from.offset+(tr.to.offset-tr.from.offset)*k;viewOffsetX=tr.from.offsetX+(tr.to.offsetX-tr.from.offsetX)*k;
      camera.fov=tr.from.fov+(tr.to.fov-tr.from.fov)*k;setSlotStretch(tr.from.stretch+(tr.to.stretch-tr.from.stretch)*k,tr.from.stretchX+(tr.to.stretchX-tr.from.stretchX)*k);camera.updateProjectionMatrix();
      if(t===1){transition=null;tr.resolve(true);resetInput();}
    }
    {
      // The arrow keys look (a steady turn, never a strafe); WASD walks. Q/E stay free: E is the visit.
      if (!seated && !transition) {
        const turnX = (keys.has('ArrowRight') ? 1 : 0) - (keys.has('ArrowLeft') ? 1 : 0);
        const turnY = (keys.has('ArrowDown') ? 1 : 0) - (keys.has('ArrowUp') ? 1 : 0);
        if (turnX || turnY) turn(turnX * KEY_YAW_RATE * dt, turnY * KEY_PITCH_RATE * dt);
      }
      const x = touch.value.x + (keys.has('KeyD') ? 1 : 0) - (keys.has('KeyA') ? 1 : 0);
      let z = touch.value.z + (keys.has('KeyS') ? 1 : 0) - (keys.has('KeyW') ? 1 : 0);
      const speed = keys.has('ShiftLeft') || keys.has('ShiftRight') ? RUN_SPEED : WALK_SPEED;
      vel.lerp(want.set(x, z).clampLength(0, 1).multiplyScalar(speed), 1 - Math.exp(-dt * 12));
      const [dx, dz] = worldDelta(yaw, vel.x * dt, vel.y * dt);
      if(!seated&&!transition)step(pos, dx, dz, o.stations);
      sway = T.MathUtils.damp(sway, still || seated ? 0 : Math.min(1, vel.length() / WALK_SPEED), 10, dt);
      walkPhase += vel.length() * dt * 3.1;
      camera.position.set(pos[0], pos[1] + Math.sin(walkPhase * 2) * 0.004 * sway, pos[2]);
      camera.rotation.set(pitch + Math.sin(walkPhase * 2) * 0.0008 * sway, yaw, Math.sin(walkPhase) * 0.0014 * sway, 'YXZ');
      setNearest(customization.opened || seated || transition ?null:nearestStation(pos,stationRows.filter(row => row.key !== 'race' || room.prizes.cabinet.visible)));
    }
    if (!still) ambient += dt;
    camera.updateMatrixWorld();
    room.update(dt, ambient, still, camera);
    glowPulse();
    for (const view of views) view.update?.(dt, still);
    decor.update(dt, still);
    customization.update(dt, still);
    /* The stage measured on the frame that uses it: the drawing buffer, the camera aspect and the viewport
     * below all come off the same numbers, and a phone that has just been turned reports the new box here
     * a frame or two before any event carries it (settle(), above). resize() applies a measurement once,
     * so this is two integer reads when nothing has moved. */
    resize();
    const fullWidth=Math.max(1,o.mount.clientWidth||window.innerWidth);
    const height=Math.max(1,o.mount.clientHeight||window.innerHeight);
    // The room keeps whatever the panel does not cover: the strip beside it, or the band above it once
    // the panel is a full-width sheet (the phone layout, customization-panel-style.js).
    const box=customization.opened?customization.previewBox(fullWidth,height):{x:0,y:0,w:fullWidth,h:height};
    pass=customization.opened?{box,w:fullWidth,h:height}:null;   // what pickAt() has to normalise against
    if(camera.aspect!==box.w/box.h){camera.aspect=box.w/box.h;camera.updateProjectionMatrix();}
    if(catalogueView&&customization.opened){
      const target=previewTarget.fromArray(catalogueView.look);
      camera.position.fromArray(catalogueView.position);
      if(catalogueView.width){
        const distance=camera.position.distanceTo(target);
        const fit=Math.max(distance,catalogueView.width/(2*Math.tan(camera.fov*Math.PI/360)*camera.aspect)*1.15,catalogueView.height*1.6);
        camera.position.sub(target).normalize().multiplyScalar(fit).add(target);
      }
      camera.lookAt(target);
    }
    if(viewOffset||viewOffsetX)camera.setViewOffset(fullWidth,height,viewOffsetX*fullWidth,viewOffset*height,fullWidth,height);else camera.clearViewOffset();
    camera.updateMatrixWorld();fadeRoof();
    for (const emi of slotEmis) emi.update(dt, still);
    interaction.update(dt, still);
    screens.update(ambient, camera, still);
    // The catalogue close-up shares this context (CONTRACT 7): its own scissored pass, drawn first so
    // renderer.info still reports the room's own frame.
    customization.draw(renderer);
    renderer.setViewport(box.x,box.y,box.w,box.h);
    renderer.setScissor(box.x,box.y,box.w,box.h);
    renderer.setScissorTest(customization.opened);
    const autoReset = renderer.info.autoReset;
    renderer.info.autoReset = false; renderer.info.reset();
    if(![...views].some(view=>view.coversRoom))renderer.render(scene, camera);
    if (seated && views.size) {
      renderer.setScissorTest(true);
      const autoClear = renderer.autoClear;
      renderer.autoClear = false;
      try { for (const view of views) view.draw?.(renderer, camera); }
      finally { renderer.autoClear = autoClear; }
    }
    renderer.info.autoReset = autoReset;
    renderer.setScissorTest(false);
  }

  function gamePose(row) {
    const width=Math.max(1,o.mount.clientWidth),height=Math.max(1,o.mount.clientHeight);
    if (row.id === 'slot') {
      const stretch=height>width?Math.min(3,Math.max(1.4,height/width*1.39)):1,stretchX=height>width?1:Math.min(1.8,Math.max(1,width/height/1.35)),previous=slotShape?.factor||1,previousX=slotShape?.factorX||1;
      setSlotStretch(stretch,stretchX);
      const target=slotSeat(room.holders.get(row.key),{fov:24},width,height);
      setSlotStretch(previous,previousX);
      return {...target,fov:24,stretch,stretchX};
    }
    return seatPose(row,room.holders.get(row.key),camera,width,height,cardHands,counts)||{pos:row.approach.slice(),...facing(row.approach,row.look),offset:0,offsetX:0};
  }
  function moveCamera(to, duration=2100) {
    to={fov:66,stretch:1,stretchX:1,...to};
    if(transition){transition.resolve(false);transition=null;}
    const motion=o.cameraMotion?.()||{off:still};
    motion.reduced ||= matchMedia('(prefers-reduced-motion: reduce)').matches;
    if(motion.off||halted){camera.fov=to.fov;camera.updateProjectionMatrix();setSlotStretch(to.stretch,to.stretchX);pos.splice(0,3,...to.pos);yaw=to.yaw;pitch=to.pitch;viewOffset=to.offset;viewOffsetX=to.offsetX;camera.position.fromArray(pos);camera.rotation.set(pitch,yaw,0,'YXZ');camera.updateMatrixWorld();return Promise.resolve(!halted);}
    const rotation=new T.Euler().setFromQuaternion(camera.quaternion,'YXZ');
    const rendered={fov:camera.fov,stretch:slotShape?.factor||1,stretchX:slotShape?.factorX||1,pos:camera.position.toArray(),yaw:rotation.y,pitch:rotation.x,offset:viewOffset,offsetX:viewOffsetX};
    return new Promise(resolve=>{transition={from:rendered,to,elapsed:0,duration:motion.reduced?duration*1.35:duration,resolve};resetInput();run();});
  }
  function seat(row) {
    if (!row || seated || held || halted || transition) return false;
    if(row.id==='slot'){const holder=room.holders.get(row.key),emi=holder.getObjectByName('emi_topper');slotEmis.find(e=>e.fixture===holder)?.settle();holder.userData.slotPlaying=true;slotShape={holder,emi,baseY:holder.scale.y,baseX:holder.scale.x,emiY:emi?.scale.y||1,emiX:emi?.scale.x||1,factor:1,factorX:1};}
    freeLook();pendingVisit=false;leaveAsked=false;seated={pos:pos.slice(),yaw,pitch,row,fov:camera.fov}; sway=0;cardHands=1;counts={d:2,0:2};composition='';
    if(room.ceiling)room.ceiling.visible=true;
    interaction.dismiss();customization.dismiss();setNearest(null);resetInput();
    arrival=moveCamera(gamePose(row));run();return true;
  }
  function unseat() {
    if (!seated) return;
    for (const view of [...views]) dropView(view);
    const previous=seated;
    if(previous.row.id==='slot')room.holders.get(previous.row.key).userData.slotPlaying=false;
    seated=null;resetInput();
    arrival=moveCamera({...previous,offset:0,offsetX:0});
    arrival.then(()=>{if(!seated){setSlotStretch(1,1);slotShape=null;if(room.ceiling)room.ceiling.visible=true;}});run();
  }
  function dropView(view) {
    if (!views.delete(view)) return;
    try { view.dispose?.(); } catch (e) { say('stage dispose failed: ' + e); }
  }
  /* THE BOX THE ROOM WAS DRAWN IN. A pick normalises against the viewport the last frame actually rendered
   * into, not against the canvas. The two are the same rectangle almost always, but with the Room Service
   * panel up the room keeps only the strip beside the panel or the band above the phone sheet (frame(),
   * previewBox), and a pointer measured against the full canvas rect then comes out up to half a screen
   * from the finger. Anyone reverting this to canvas.getBoundingClientRect() will see nothing break: every
   * caller of pickAt() today is gated on canWalk() or canLeave(), and both are false while the panel is
   * open. It is the first pick that runs with the panel up that pays, and it will look like a broken room
   * rather than a broken rectangle. The three spaces are stage-rect.js.
   */
  function pickBox() {
    const rect = canvas.getBoundingClientRect();
    if (!rect.width || !rect.height) return null;
    /* The room owns the whole canvas: the canvas rect itself, the exact arithmetic the room always used.
     * The panel's own state decides it, not just the remembered pass, because the loop can be stopped
     * (hold(), pause()) after the panel closed and before another frame refreshed the viewport. */
    if (!pass || !customization.opened) return { page: { x: rect.left, y: rect.top, w: rect.width, h: rect.height } };
    return stageRect(rect, viewportPageRect(rect, pass.box, pass.w, pass.h), rect.width, rect.height);
  }
  function pickAt(event, objects) {
    if (transition || halted || suspended || !Array.isArray(objects) || !objects.length) return [];
    const box = pickBox();
    if (!box) return [];
    const at = clickPoint(event);
    const ndc = ndcIn(box, at.x, at.y);
    pointer.set(ndc.x, ndc.y);
    scene.updateMatrixWorld(true); camera.updateMatrixWorld();
    ray.setFromCamera(pointer, camera);
    return ray.intersectObjects(objects, true);
  }
  function stage(row) {
    const fixture = room.holders.get(row.key);
    if (!fixture || !seat(row)) return null;
    let closed = false;
    const owned = new Set();
    return { get ready(){return !closed&&!transition&&!!seated;}, arrived:arrival, renderer, scene, fixture, camera, canvas, lookInverted: () => !!o.invertLook?.(), emi: room.emis.find(e => e.id === row.id), pick: pickAt,
      onEmiClick(callback) { emiClicks.set(row.key, callback); return () => { if (emiClicks.get(row.key) === callback) emiClicks.delete(row.key); }; },
      register(view) {
        if (closed || halted) { view.dispose?.(); return () => {}; }
        views.add(view); owned.add(view);
        return () => { owned.delete(view); dropView(view); };
      },
      dispose() {
        if (closed) return;
        closed = true; emiClicks.delete(row.key);
        for (const view of owned) dropView(view);
        owned.clear(); unseat();
      },
    };
  }

  function run() {
    if (raf || held || halted || suspended || document.hidden) return;
    last = lastTick = performance.now();
    raf = requestAnimationFrame(frame);
  }
  const stop = () => { if (raf) cancelAnimationFrame(raf); raf = 0; };

  const visibility = () => { quality.resetSamples(); resetInput(); if(document.hidden) stop(); else run(); };
  document.addEventListener('visibilitychange', visibility);
  resize();
  run();
  // THE WATCHDOG. frame() drops the loop whenever held, halted or suspended, and every path that clears those
  // is supposed to run() again. A path that forgets leaves a room that draws nothing while its HUD works. Rather
  // than trust every future path, look once a second and restart a loop that nothing is holding.
  const watchdog = setInterval(() => {
    if (raf || held || halted || suspended || document.hidden || contextLost) return;
    say('room loop was stopped with nothing holding it; restarted');
    run();
  }, 1000);
  screens.deal(() => ambient).catch(() => {});

  return {
    setPrizes: (snapshot, bought) => room.prizes.apply(snapshot, bought),
    documents,
    prepareVisit(){pendingVisit=true;freeLook();resetInput();},
    setRewards: snapshot => customization.setOwned(snapshot.owned),
    renderer, camera, scene, buildMs, seat, unseat, pickAt, stage,
    /** A station is taking the screen: keep the pose, stop drawing, keep the context. */
    hold() { if (held) return; held = { pos: pos.slice(), yaw, pitch }; freeLook(); resetInput(); interaction.dismiss(); customization.dismiss(); stop(); setNearest(null); },
    /** Back from a station: the exact spot and facing, on the next frame. */
    release() {
      pendingVisit=false;
      if (!held) {resetInput();run();return;}
      pos.splice(0, 3, ...held.pos); yaw = held.yaw; pitch = held.pitch;
      held = null; resetInput(); run();
    },
    pause(on) { suspended = !!on; if (suspended) { freeLook(); stop(); resetInput(); interaction.dismiss(); customization.dismiss(); } else run(); },
    halt() { halted = true; freeLook(); glowTarget(null); for (const list of glows.values()) for (const g of list) for (const m of [].concat(g.copies)) m.dispose(); glows.clear(); clearInterval(watchdog); documents.dispose(); memorabilia.dispose(); placards.dispose(); if(transition){transition.resolve(false);transition=null;} for (const view of [...views]) dropView(view); stop(); document.removeEventListener('visibilitychange', visibility); screens.dispose(); for(const r of roofMaterials){r.node.material=r.original;for(const m of r.copies)m.dispose();} room.disposeSurfaces(); resetInput(); touch.dispose(); interaction.dispose(); for(const e of [...room.emis,...slotEmis])e.dispose(); customization.dispose(); decor.dispose(); room.echo.clear(); for(const p of room.payouts.values()){p.coins.dispose();p.host?.removeFromParent();} },
    setStill(on) { still = !!on; },
    /** Repaint one fixture label, e.g. the wheel's screen for MUST HIT (10.16.E). */
    setLabel(rowKey, node, text) { return room.setLabel(rowKey, node, text); },
    dismissEmi() { if(customization.dismiss())return true; const open = !!interaction.debug().id; interaction.dismiss(); return open; },
    celebrate: (key,amount,tier,text)=>room.celebrate(key,amount,tier,text),
    customization, go, visit,
    /** Let the mouse go (a panel, card or station is taking the screen). Nothing takes it back but the next click. */
    freeLook,
    /** The Esc that frees the mouse is not the Esc that leaves the room: true while locked or for half a second after. */
    lookJustFreed() { return locked || performance.now() - unlockedAt < 500; },
    navigationPose() { return { position: pos.slice(), yaw, pitch }; },
    pose(p, y = 0, tilt = 0) { pos.splice(0, 3, ...p); yaw = y; pitch = tilt; },
    get transitioning() { return !!transition; },
    get nearest() { return nearest; },
    get lookLocked() { return locked; },
    get seated() { return !!seated; },
    get held() { return !!held; },
    /** Test seam: plain numbers only. */
    debug() {
      const sorted = frames.slice().sort((a, b) => a - b);
      return {
        renderBudget: {...budget.debug(), dpr}, slotStretch:slotShape?.factor||1, fov:camera.fov, position: pos.slice(), yaw, pitch, transitioning:!!transition, viewOffset, viewOffsetX, locked, lockRefused, glowing: glow ? glow.length : 0, held: !!held, seated: !!seated, leaveAsked, running: !!raf, halted, suspended, contextLost, hidden: document.hidden, still,
        nearest: nearest ? nearest.key : null, fixtures: room.fixtures, bulbs: room.bulbs, screens: room.screens.length,
        pictures: screens.pictures, animation: screens.animation, calls: renderer.info.render.calls, triangles: renderer.info.render.triangles,
        touch: touch.debug(), decor: decor.debug(), customization: customization.debug(),
        payouts: Object.fromEntries([...room.payouts].map(([key,p])=>[key,p.coins.debug()])),
        winEcho: room.echo.debug(),
        emiBubble: interaction.debug(),
        emis: room.emis.map((e) => e.debug()),
        marquee: room.marquee?.userData.text,
        bulbColors: scene.children.filter((o) => o.isInstancedMesh && o.instanceColor).slice(0, 2).map((o) => Array.from(o.instanceColor.array.slice(0, 9))),
        floorAngle: room.floor ? room.floor.material.uniforms.angle.value : null, ambient, sway,
        frameP95: sorted.length ? sorted[Math.floor(sorted.length * 0.95)] : null,
        frameMedian: sorted.length ? sorted[Math.floor(sorted.length / 2)] : null, buildMs: Math.round(buildMs),
        ceiling: room.ceiling ? room.ceiling.visible : null,
        labels: Object.fromEntries(Array.from(room.labels, ([k, v]) => [k, v.text])),
      };
    },
  };
}
