/* ============================================================================
 * race/run.js - the run brain of The Caucus Race. Implements CONTRACT.md
 * "race/run.js + raceBoot.js + race.html (PR 5, integration)".
 *
 *   createRace({ root, bridge, media, settings, seed }) -> { start(), setPaused(b), dispose() }
 *
 * Composes renderer + spine + tunnel + fx + rooms + bubbles + kart + score + hud
 * + items + payloadFx + screen shake and runs the frame loop. `root` holds the
 * <canvas>, the `.race-hud` div and the `.sf-hud` layer payloadFx draws into.
 * Nothing here subtracts: the run ends only from the Brake (Esc) or the host.
 *
 * Host traffic owned here: sends heartbeat, run-started, sfx, fire-payload
 * (video only), run-ended, exit, exit-done; listens to pause, payout-result.
 * ==========================================================================*/

import * as THREE from 'three';
import { Q } from '../shared/quality.js';
import { createTunnel, FOG_DENSITY } from '../engine/tunnel.js';
import { createFx } from '../engine/fx.js';
import { createPayloadFx } from '../game/payloadFx.js';
import { createScreenShake } from '../game/screenShake.js';
import { INTENSITY_RAMP_SEC, TREATS_ONLY_SEC, KART_BASE_SPEED, MULT_LADDER, makeRng } from './consts.js';
import { createSpine } from './spine.js';
import { roomById, rollRoomOrder, createRoomDresser } from './rooms.js';
import { createWalls } from './walls.js';
import { KIND_BY_ID } from './bubbleKinds.js';
import { createCocktail, CATEGORIES } from './cocktail.js';
import { createBubbleField } from './bubbles.js';
import { createKart } from './kart.js';
import { createScore } from './score.js';
import { createRaceHud } from './hud.js';
import { createMediaLane } from './mediaLane.js';
import { createItems } from './items.js';
import { createInput } from './input.js';
import { createPixelizer, PIXEL_DEFAULT } from './pixel.js';
import { createSpeedFx } from './speed.js';
import { createRaceAudio } from './audio.js';

const HEARTBEAT_MS = 2000, PAYOUT_WAIT_MS = 2000, NEAR_MISS_M = 1.6, FOV_BASE = 72;
// effect lives are cocktail.js CATEGORIES (scaled by the pop's durationMult); a glitch re-pop wobbles
// the world clock this hard, this long (only when tea_time is not already holding it)
const WOBBLE_SCALE = 0.82, WOBBLE_SEC = 0.3;
const ladderMult = (combo) => { let m = 1; for (const [at, mult] of MULT_LADDER) if (combo >= at) m = mult; return m; };
const SPAWN_T0 = 2.5, RAIN_T0 = 20, EARLY_SLOW = 1.5;   // the opening drips slower (TREATS_ONLY_SEC)
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const hex = (n) => '#' + ((n >>> 0) & 0xffffff).toString(16).padStart(6, '0');

export function createRace({ root, bridge, media, settings = {}, seed = 1 }) {
  const canvas = root.querySelector('canvas');
  const hudRoot = root.querySelector('.race-hud');
  const sfHud = root.querySelector('.sf-hud');
  const reducedMotion = settings.reducedMotion != null ? !!settings.reducedMotion
    : !!(typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const intensityFloor = clamp(Number(settings.intensityFloor) || 0, 0, 1);
  const send = (m) => { try { bridge.send(m); } catch (e) { /* host gone */ } };
  const sfx = (name, scale = 0.8) => audio.sfx(name, scale);   // race/audio.js: host legs + in-page beats

  // ---- renderer / scene / camera (engine/scene.js pattern, pitch-demo look) ----
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: Q.antialias, alpha: false, powerPreference: 'high-performance' });
  // the big-pixel look: the world at low resolution, bubbles + wall media crisp on top (race/pixel.js);
  // host settings.pixel / ?pixel=N override, 0 = off; draw-call stats go to the host log every ~5 s
  const pixel = createPixelizer({ renderer, canvas, block: settings.pixel == null ? PIXEL_DEFAULT : settings.pixel, log: (m) => { if (bridge.log) bridge.log(m); } });
  if ('outputColorSpace' in renderer) renderer.outputColorSpace = THREE.SRGBColorSpace;
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x12261f);
  scene.fog = new THREE.FogExp2(0x12261f, FOG_DENSITY);
  const camera = new THREE.PerspectiveCamera(FOV_BASE, 1, 0.1, 400);
  scene.add(new THREE.AmbientLight(0x8a70a8, 1.0));
  const cupLight = new THREE.PointLight(0xff69b4, 1.4, 14);
  scene.add(cupLight);
  function resize() {
    const w = root.clientWidth || window.innerWidth, h = root.clientHeight || window.innerHeight;
    pixel.resize(w, h); camera.aspect = w / h; camera.updateProjectionMatrix();
  }
  window.addEventListener('resize', resize); resize();

  // ---- the parts that outlive a run ----
  const hud = createRaceHud(hudRoot);
  const fxProxy = { pulseFlash: (a) => { if (W) W.fx.pulseFlash(a); } };   // fx is rebuilt on "again"
  const payloadFx = createPayloadFx({ hud: sfHud, fx: fxProxy, media });
  const lane = createMediaLane(sfHud);   // re-homes payload cards off the road
  const shake = createScreenShake({ el: root });
  if (reducedMotion) shake.setEnabled(false);
  const input = createInput();
  const audio = createRaceAudio({ bridge, hud, settings, input });
  const speedFx = createSpeedFx({ scene, camera, root, reducedMotion });
  const camOut = { pos: new THREE.Vector3(), look: new THREE.Vector3(), up: new THREE.Vector3(0, 1, 0), roll: 0 };
  const _v = new THREE.Vector3();

  // ---- run state ----
  const S = {
    started: false, running: false, paused: false, hostPaused: false, ended: false, disposed: false,
    elapsed: 0, t: 0, intensity: intensityFloor, timeScale: 1, jackpotBias: 1, parasol: false, magnet: false,
    flip: false, spawnT: SPAWN_T0, rainT: RAIN_T0, tunnelTime: 0, rush: 0, fov: 72, fovBoost: 0, gates: 0, room: null,
    wasAirborne: false, effects: [], moodHeld: null, moodHold: 0, mood: 'calm', bestAtStart: 0, seed, wobble: 0,
  };
  const mix = createCocktail({ now: () => S.elapsed });   // THE MIX: one live effect per category (cocktail.js); S.effects mirrors its live slots
  let W = null;                       // the world: everything that is rebuilt on "again"
  let raf = 0, last = 0, lastBeat = 0, payoutResolve = null;
  const trail = [];                   // last ~1 s of kart {d,x,h}, for the ALMOST on a miss

  function poke(mood, sec) { S.moodHeld = mood; S.moodHold = sec; }
  function setFlip(on) { S.flip = !!on; canvas.style.transform = on ? 'scaleX(-1)' : ''; }

  // ---- world build / teardown ----
  function build(runSeed) {
    const layout = createSpine({ seed: runSeed, roomOrder: rollRoomOrder(runSeed) });
    const rng = makeRng(runSeed ^ 0x5bd1e995);
    // the descent's tube is 1000 m; ours is ~3x longer, so keep the ring/segment density it was tuned for
    const segBase = Q.tubeSegMult;
    Q.tubeSegMult = segBase * clamp(layout.totalDepth / 1000, 1, 2.4);
    const tunnel = createTunnel(layout);
    Q.tubeSegMult = segBase;
    const u = tunnel.material.uniforms;
    u.uRings.value = Math.round(layout.totalDepth / 12);        // a ring every 12 m (the descent's 8 m tangles at the far end)
    u.uSpiralTurns.value = Math.round(layout.totalDepth / 36);  // and a lazier spiral, so the tube ends in a point, not a knot
    scene.add(tunnel.mesh);
    const fx = createFx({ scene, layout, tunnelMat: tunnel.material, particleFog: false });
    const dresser = createRoomDresser({ scene, layout });
    const walls = createWalls({ scene, layout, media, renderer, camera, rng });
    const kart = createKart({ scene, layout, reducedMotion });
    const score = createScore();
    const getRoom = () => {
      const r = roomById(layout.roomAtDepth(kart.state.d));
      if (!r || S.jackpotBias === 1) return r;
      return { ...r, bubbleBias: { ...r.bubbleBias, golden: (r.bubbleBias.golden == null ? 1 : r.bubbleBias.golden) * S.jackpotBias } };
    };
    const field = createBubbleField({ scene, layout, media, getIntensity: () => S.intensity, getRoom, getElapsed: () => S.elapsed, onTexture: pixel.filterTexture });
    const items = createItems({ kart, bubbles: field, score, fx, hud, payload: payloadFx, rng });
    const w = { layout, tunnel, fx, dresser, walls, kart, score, field, items, rng };
    field.onPop((p) => onPop(w, p));
    field.onMiss((m) => onMiss(w, m));
    score.onEvent((e) => onScore(w, e));
    items.onEvent((e) => onItem(w, e));
    pixel.retexture(scene);
    return w;
  }
  function teardown() {
    if (!W) return;
    try { W.field.dispose(); W.kart.dispose(); W.walls.dispose(); W.dresser.dispose(); W.fx.dispose(); } catch (e) { /* half-built world */ }
    scene.remove(W.tunnel.mesh); W.tunnel.dispose();
    W = null;
  }
  function resetRunState(runSeed) {
    Object.assign(S, { running: false, paused: false, ended: false, elapsed: 0, t: 0, intensity: intensityFloor, timeScale: 1,
      jackpotBias: 1, parasol: false, magnet: false, spawnT: SPAWN_T0, rainT: RAIN_T0, rush: 0, fovBoost: 0, gates: 0, room: null,
      wasAirborne: false, effects: [], moodHeld: null, moodHold: 0, mood: 'calm', seed: runSeed });
    setFlip(false); trail.length = 0;
    mix.reset(); S.wobble = 0; clearMixChrome();
    hud.setScore(0); hud.setCombo(0, 1); hud.setBank(0); hud.setSpeed(0); hud.setFraught(0); hud.item(null, 'no item yet');
  }

  // ---- rooms ----
  function enterRoom(w, roomId) {
    const first = S.gates === 0;
    const spec = w.dresser.applyRoom(w.fx, roomId, first ? 0.2 : 1.2);
    S.room = spec; S.gates++;
    hud.banner(spec.name, spec.tagline, hex(spec.colors.banner));
    sfx('depth_change', 0.7);
    if (roomId === 'teagarden' && !first && w.score.state.score > 0) {
      const bestBefore = w.score.state.best;
      w.score.bank();
      sfx(w.score.state.best > bestBefore ? 'pb_fanfare' : 'streak_milestone', 0.9);
      poke('smug', 1.6);
    }
  }

  // ---- pops ----
  function treat(w, p) {
    w.score.pop(p.points, p.id);
    if (p.id === 'lucky') w.items.roll(w.score.state.mult);
    if (p.id === 'golden') {
      w.score.jackpot(S.jackpotBias > 1 ? 'major' : 'minor');
      sfx('golden_pop', 0.9); shake.shake(0.35, 240); poke('jackpot', 1.4);
    }
  }
  function onPop(w, p) {
    w.kart.pulseTarget();
    if (p.kind === 'treat') return treat(w, p);
    if (S.parasol) { S.parasol = false; hud.toast('parasol', 'item'); return treat(w, p); }
    // THE MIX: one live effect per category, each with its own re-pop rule (cocktail.js); 'held' scores as a treat
    const durationMult = 0.5 + 0.5 * S.intensity;
    const r = mix.add(p.id, { durationMult });
    if (r.action === 'held' || r.action === 'ignore') { w.score.pop(p.points, 'treat'); hud.toast('held', 'effect'); return; }
    w.score.pop(p.points, p.id);
    pour(w, p, r, Math.round(clamp(p.strength, 0, 1) * 100), durationMult);
    shake.shake(p.payload === 'video' ? 0.9 : 0.5, 300);
    poke('shock', 0.9);
    if (r.recipe) serve(w, r.recipe);
  }

  // ---- THE MIX: actions -> payloadFx + chrome, recipes -> the ledger ----
  const fire = (p, strength, durationMult) => {
    if (p.payload === 'video') send({ type: 'fire-payload', kind: 'video', strength, durationMult });
    else payloadFx.applyPayload({ payload: { kind: p.payload, overlay: p.overlayKind }, strength }, { durationMult });
  };
  function clearMixChrome() { root.removeAttribute('data-ov'); root.removeAttribute('data-tint'); if (hud.setTint) hud.setTint(0); }
  /** Pour one action. Sustained holds (tint / overlay / corruption) get a durationMult that lands payloadFx's
   *  fade on the mixer's drain, so a tint that was extended stays pink for as long as the rail says it will. */
  function pour(w, p, r, strength, durationMult) {
    const label = (KIND_BY_ID[p.id] || {}).label || p.id;
    const slot = mix.live(r.category);
    const holdMult = slot && CATEGORIES[r.category].scaled ? clamp(slot.sec / CATEGORIES[r.category].sec, 0.1, 10) : durationMult;
    switch (r.category) {
      case 'strobe':
        fire(p, strength, durationMult); w.kart.applySlow(0.92, 2.0);
        hud.toast(r.charges > 1 ? `flash x${r.charges}` : 'flash', 'effect');
        if (hud.strobe) hud.strobe(r.charges);
        break;
      case 'tint':
        fire(p, strength, holdMult);
        root.dataset.tint = String(r.depth); if (hud.setTint) hud.setTint(r.depth);   // race.css deepens the wash and the chrome
        hud.toast(r.action === 'extend' ? (r.depth >= 2 ? 'pinker' : 'pink, longer') : label, 'effect');
        break;
      case 'overlay':
        fire(p, strength, holdMult);
        root.dataset.ov = p.overlayKind;   // race.css crossfades the other hold out (the replace)
        hud.toast(r.action === 'refresh' ? 'deeper' : r.action === 'replace' ? `${label} takes over` : label, 'effect');
        break;
      case 'corruption':
        fire(p, strength, holdMult); hud.flicker();
        if (r.action === 'refresh') {
          poke('shock', 0.6); w.kart.applySlow(0.85, 0.6);
          if (S.timeScale === 1) { S.timeScale = WOBBLE_SCALE; S.wobble = WOBBLE_SEC; }
        }
        hud.toast(r.action === 'refresh' ? 'more static' : label, 'effect');
        break;
      case 'video': fire(p, strength, durationMult); hud.toast(label, 'effect'); break;
      default:   // cards, freeze
        fire(p, strength, durationMult); w.kart.applySlow(0.92, 2.0);
        hud.toast(r.charges > 1 ? `${label} x${r.charges}` : label, 'effect');
    }
  }
  /** A recipe is served: the boost never lowers what lucky_star already put on the ledger, the combo holds. */
  function serve(w, rc) {
    const cur = w.score.state.mult / ladderMult(w.score.state.combo);
    w.score.boostMult(Math.max(rc.mult, cur), cur > rc.mult ? Math.max(rc.sec, 8) : rc.sec);
    w.score.freezeCombo(rc.sec);
    hud.toast(rc.name, 'recipe');
    if (rc.marquee) hud.banner(rc.name, rc.line, '#ff3da5');
    sfx(rc.marquee ? 'pb_fanfare' : 'streak_milestone', 0.8);
    shake.shake(rc.marquee ? 0.7 : 0.35, 260);
    poke(rc.marquee ? 'jackpot' : 'smug', rc.marquee ? 1.6 : 1.2);
  }
  function onMix(w, e) {
    switch (e.type) {
      case 'pulse':   // the strobe: burst extras after a stacking pop, the roll at 4 and 5 charges (no roll under reduced motion)
        if (e.kind === 'roll' && reducedMotion) break;
        w.fx.pulseFlash(e.kind === 'burst' ? 0.55 : 0.28 + 0.05 * e.charges);
        if (hud.strobe) hud.strobe(e.charges);
        if (e.kind === 'burst' && e.charges >= 3) payloadFx.applyPayload({ payload: { kind: 'flash' }, strength: 40 }, { durationMult: 0.6 });
        break;
      case 'decay': if (hud.strobe) hud.strobe(e.charges); break;
      case 'expire':
        if (e.category === 'overlay') root.removeAttribute('data-ov');
        else if (e.category === 'tint') { root.removeAttribute('data-tint'); if (hud.setTint) hud.setTint(0); }
        break;
      case 'recipeEnd': break;   // the rail drops the name; nothing to take back
    }
  }
  function onMiss(w, m) {
    // ALMOST: the treat slid past inside NEAR_MISS_M but outside the hit box; else the streak lets go
    let best = null, bestGap = Infinity;
    for (const s of trail) { const g = Math.abs(w.layout.wrap(s.d - m.d + w.layout.totalDepth / 2) - w.layout.totalDepth / 2); if (g < bestGap) { bestGap = g; best = s; } }
    const near = best && m.x != null && Math.abs(m.x - best.x) < NEAR_MISS_M && Math.abs((m.h || 0) - best.h) < NEAR_MISS_M;
    if (near) w.score.nearMiss(); else w.score.miss();
  }
  function onScore(w, e) {
    switch (e.type) {
      case 'pop': hud.setScore(e.score); hud.setCombo(e.combo, e.mult); hud.toast(`+${e.gain}`, 'pop'); break;
      case 'combo': if (!e.combo) hud.setCombo(0, w.score.state.mult); break;
      case 'mult': hud.setCombo(w.score.state.combo, e.to); if (e.to > e.from) { hud.toast(`x${e.to}`, 'pop'); sfx('streak_milestone', 0.6); poke('smug', 1.0); } break;
      case 'miss': hud.setCombo(0, e.mult); break;
      case 'almost': hud.setScore(e.score); hud.toast(`almost +${e.gain}`, 'almost'); break;
      case 'bank': hud.setBank(e.banked); hud.setScore(0); hud.toast(`bank +${e.amount}`, 'bank'); break;
      case 'jackpot': hud.setScore(e.score); hud.toast(`jackpot +${e.gain}`, 'jackpot'); break;
    }
  }
  function onItem(w, e) {
    switch (e.type) {
      case 'itemArm': sfx('ui_click', 0.4); break;
      case 'itemUse': sfx('tunnel_powerup_collect', 0.7); poke('smug', 0.8); break;
      case 'timeScale': S.timeScale = e.value; sfx('time_slow_in', 0.8); break;
      case 'magnet': S.magnet = true; w.field.setReach(2.2); w.kart.setReach(2.2); break;
      case 'multBoost': w.score.boostMult(e.mult, e.sec); break;
      case 'parasol': S.parasol = true; break;
      case 'flip': setFlip(true); break;
      case 'jump': w.kart.state.vh = Math.max(w.kart.state.vh, e.vh); w.kart.state.h = Math.max(w.kart.state.h, 0.06); w.kart.state.airborne = true; poke('shock', 0.6); break;
      case 'comboFreeze': w.score.freezeCombo(e.sec); break;
      case 'jackpotBias': S.jackpotBias = e.mult; break;
      case 'itemEnd':
        if (e.id === 'tea_time') { S.timeScale = 1; sfx('time_slow_out', 0.8); }
        else if (e.id === 'mirror') setFlip(false);
        else if (e.id === 'rabbit_foot') S.jackpotBias = 1;
        else if (e.id === 'magnet') { S.magnet = false; w.field.setReach(1); w.kart.setReach(1); }
        break;
    }
  }

  // ---- the frame ----
  function step(w, dt) {
    const k = w.kart, ks = k.state, lay = w.layout;
    S.elapsed += dt;
    S.intensity = clamp(Math.max(intensityFloor, S.elapsed / INTENSITY_RAMP_SEC), 0, 1);
    const wdt = dt * S.timeScale;                     // the world clock (tea_time); the kart keeps real time
    S.t += wdt;
    const prevD = ks.d;
    k.update(dt, input.read(), lay);
    w.score.tick(dt); w.items.update(dt);
    trail.push({ d: ks.d, x: ks.x, h: ks.h }); if (trail.length > 70) trail.shift();
    for (const e of mix.tick(dt)) onMix(w, e);
    if (S.wobble > 0) { S.wobble -= dt; if (S.wobble <= 0 && S.timeScale === WOBBLE_SCALE) S.timeScale = 1; }
    const mixed = mix.state(); S.effects = mixed.live;
    if (hud.mixer) hud.mixer(mixed);

    // bubbles: seed the chunks ahead, drip spawns, rain bursts
    for (const c of lay.chunks) { const rel = lay.wrap(c.d0 - ks.d + lay.totalDepth / 2) - lay.totalDepth / 2; if (rel > -20 && rel < 250) w.field.seedChunk(c); }
    const early = S.elapsed < TREATS_ONLY_SEC ? EARLY_SLOW : 1;
    S.spawnT -= wdt;
    if (S.spawnT <= 0) { w.field.spawnAhead(ks.d, 1 + Math.round(S.intensity * 2)); S.spawnT = (3.4 - 1.8 * S.intensity) * early; }
    S.rainT -= wdt;
    if (S.rainT <= 0) { w.field.rain(ks.d, 3 + Math.round(S.intensity * 4)); S.rainT = (S.room && S.room.loud ? 9 : 14) * (1 - 0.5 * S.intensity) * early; }
    w.field.update(wdt, S.t, ks);

    // track features crossed this frame
    for (const f of lay.featuresBetween(prevD, ks.d)) {
      if (f.type === 'boost' && !ks.airborne && Math.abs(f.x - ks.x) <= 1.2) { k.applyBoost(1.6); sfx('tunnel_powerup_collect', 0.8); shake.shake(0.25, 200); poke('streamed', 1.2); }
      else if (f.type === 'itembox' && Math.abs(f.x - ks.x) <= 1.2) { if (w.items.roll(w.score.state.mult)) sfx('ui_click', 0.5); }
      else if (f.type === 'gate') enterRoom(w, f.room);
    }
    if (S.wasAirborne && !ks.airborne) { shake.shake(0.8, 300); poke('smug', 0.7); }
    S.wasAirborne = ks.airborne;

    // tube + fx + rooms
    S.tunnelTime += wdt * (0.5 + ks.speed / 12);
    w.tunnel.material.uniforms.uTime.value = S.tunnelTime;
    const rushT = clamp((ks.speed - KART_BASE_SPEED + 2) / 14, 0, 1);
    S.rush += (rushT - S.rush) * Math.min(1, dt * 3);
    w.fx.update(ks.d, wdt, S.t, S.intensity, S.rush);
    scene.background.lerp(scene.fog.color, Math.min(1, dt * 1.5));
    w.dresser.update(ks.d);
    w.walls.update(ks.d, wdt);

    // camera + the cup light
    k.camera(camOut);
    camera.position.copy(camOut.pos); camera.up.copy(camOut.up); camera.lookAt(camOut.look);
    // FOV kick: boost snaps wide (to ~84) and eases back slowly; drift adds a smaller one; tea_time narrows
    const boostT = ks.boostSec > 0 ? 1 : 0;
    S.fovBoost += (boostT - S.fovBoost) * Math.min(1, dt * (boostT ? 9 : 2.2));
    const fovT = FOV_BASE + (reducedMotion ? 5 : 12) * S.fovBoost + (ks.drift && !reducedMotion ? 3 : 0) - (S.timeScale < 1 ? 4 : 0);
    if (Math.abs(fovT - S.fov) > 0.05) { S.fov += (fovT - S.fov) * Math.min(1, dt * 6); camera.fov = S.fov; camera.updateProjectionMatrix(); }
    cupLight.position.copy(k.group.position).addScaledVector(_v.copy(camOut.up), 1.6);
    speedFx.update(dt, ks, lay);

    // EMI: calm cruise, streamed on boost, fraught under a stack, pokes on top
    if (S.moodHold > 0) { S.moodHold -= dt; if (S.moodHold <= 0) S.moodHeld = null; }
    const base = (S.effects.length && S.intensity > 0.5) ? 'fraught' : ks.boostSec > 0 ? 'streamed' : 'calm';
    const mood = S.moodHeld || base;
    if (mood !== S.mood) { S.mood = mood; k.setMood(mood); }
    const fraught = clamp(S.effects.length / 3, 0, 1);
    k.setFraught(fraught); hud.setFraught(fraught);
    hud.setSpeed(ks.speed);
    audio.update(dt, { world: w, run: S, kart: ks });
  }

  function frame(now) {
    if (S.disposed) return;
    raf = requestAnimationFrame(frame);
    if (now - lastBeat > HEARTBEAT_MS) { lastBeat = now; send({ type: 'heartbeat', t: now }); }
    const dt = last ? clamp((now - last) / 1000, 0, 0.1) : 0;
    last = now;
    if (!W) return;
    if (S.running && !S.paused && !S.hostPaused) {
      try { step(W, dt); } catch (e) { bridge.log && bridge.log('race step: ' + (e && e.stack || e)); }
    }
    pixel.render(scene, camera);
  }

  // ---- brake / end / again / exit ----
  async function brake() {
    if (!W || !S.running || S.ended || S.paused) return;
    S.paused = true; sfx('ui_click', 0.5);
    audio.duck(true, 'brake');
    const pick = await hud.setPaused(true);
    if (S.disposed || !S.paused) return;
    if (pick === 'end') return endRun();
    S.paused = false;
    audio.duck(false, 'brake');
  }
  function waitPayout(ms) {
    return new Promise((res) => { const t = setTimeout(() => { payoutResolve = null; res(null); }, ms); payoutResolve = (m) => { clearTimeout(t); payoutResolve = null; res(m); }; });
  }
  async function endRun() {
    if (!W || S.ended) return;
    const w = W;
    S.ended = true; S.running = false; S.paused = false;
    try { payloadFx.cancelHeavy(); } catch (e) { /* nothing heavy */ }
    const st = w.score.state;
    const summary = { score: st.score, banked: st.banked, bestCombo: st.bestCombo, popped: st.popped, treats: st.treats, effects: st.effects,
      nearMisses: st.nearMisses, laps: w.kart.state.lap, durationSec: Math.round(S.elapsed), seed: S.seed,
      personalBest: st.banked + st.score > S.bestAtStart && st.banked + st.score > 0 };
    send({ type: 'run-ended', ...summary });
    sfx('surface', 0.8);
    audio.duck(true, 'end');
    const payout = await waitPayout(PAYOUT_WAIT_MS);
    if (S.disposed) return;
    const shown = { ...summary };
    if (payout && payout.finalXp != null) shown.title = `the tea party · +${Math.round(payout.finalXp)} xp` + (payout.sparksEarned ? ` · ${payout.sparksEarned} sparks` : '');
    const pick = await hud.showEnd(shown);
    if (S.disposed) return;
    if (pick === 'again') again(); else exit();
  }
  function again() {
    teardown();
    const runSeed = (Date.now() ^ Math.floor(Math.random() * 0x7fffffff)) >>> 0;
    resetRunState(runSeed);
    W = build(runSeed);
    S.started = false;
    start();
  }
  function exit() {
    send({ type: 'exit' });
    dispose();
    send({ type: 'exit-done' });
  }

  // ---- host + input wiring ----
  bridge.on('pause', (m) => setPaused(!!(m && m.on)));
  bridge.on('payout-result', (m) => { if (payoutResolve) payoutResolve(m); });
  function cyclePixel() { pixel.cycle(); pixel.retexture(scene); hud.toast(pixel.label(), 'item'); }
  input.onAction((a) => { if (a === 'item') { if (W && S.running && !S.paused) W.items.use(); } else if (a === 'brake') brake(); else if (a === 'pixel') cyclePixel(); });
  const onVis = () => { last = 0; };
  document.addEventListener('visibilitychange', onVis);

  function start() {
    if (S.started || S.disposed) return;
    if (!W) W = build(S.seed);
    S.started = true; S.running = true; S.ended = false;
    S.bestAtStart = W.score.state.best;
    S.room = W.dresser.applyRoom(W.fx, W.layout.roomAtDepth(0), 0.2);   // the gate 9 m in shows the banner
    W.kart.setMood('calm');
    send({ type: 'run-started', seed: S.seed });
    last = 0;
  }
  /** Host pause (native video playing etc): freezes the frame, no Brake screen. */
  function setPaused(on) { S.hostPaused = !!on; if (!on) last = 0; audio.duck(!!on, 'host'); }
  function dispose() {
    if (S.disposed) return;
    S.disposed = true;
    if (raf) cancelAnimationFrame(raf);
    window.removeEventListener('resize', resize);
    document.removeEventListener('visibilitychange', onVis);
    if (payoutResolve) payoutResolve(null);
    teardown();
    audio.dispose();
    input.dispose(); hud.dispose(); shake.dispose(); payloadFx.dispose(); speedFx.dispose(); lane.dispose();
    scene.clear(); renderer.dispose();
    setFlip(false);
  }

  W = build(seed);
  resetRunState(seed);
  raf = requestAnimationFrame(frame);
  return { start, setPaused, dispose };
}

// self-check: node --check is the bar; everything touches the DOM inside createRace.
