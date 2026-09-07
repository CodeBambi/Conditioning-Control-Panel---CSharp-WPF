/* ============================================================================
 * race/run.js - the run brain of Racing Thoughts. Implements CONTRACT.md
 * "race/run.js + raceBoot.js + race.html (PR 5, integration)".
 *
 *   createRace({ root, bridge, media, settings, seed }) -> { start(), setPaused(b), dispose() }
 *
 * Composes renderer + spine + tunnel + fx + rooms + bubbles + kart + score + hud
 * + pickups + payloadFx + screen shake and runs the frame loop. `root` holds the
 * <canvas>, the `.race-hud` div, the `.sf-hud` layer payloadFx draws into and, when
 * the online feed is on, race/wallDom.js's `.rh-wall3d` layer over the canvas.
 * Nothing here subtracts: the run ends only from the Brake (Esc) or the host.
 *
 * Host traffic owned here: sends heartbeat, run-started, sfx, fire-payload
 * (video only), run-ended, exit, exit-done, and with a track loaded track-play,
 * track-pause, track-stop; listens to pause, payout-result.
 *
 * CAMERA ASPECT (race/viewport.js): FOV_BASE is a HORIZONTAL field of view wearing
 * a vertical number. 72 is what it measures at 16:9, and every other window shape
 * solves back for the vertical fov that keeps that same width of road, clamped at
 * MAX_VFOV so a tall phone stops short of a fisheye. Desktop 16:9 is untouched by
 * definition. The boost/drift kicks are added on top of that per frame.
 *
 * TRACK CHARTS (CHART.md): setTrack(chart) hands the run a charted hypno file and
 * from then on the file is the clock. race/track.js holds the second, the energy
 * curve and the acts; race/cues.js says what each spoken word is worth; everything
 * below only spends those on the world it already owns. Without a track not one
 * line of this changes: the seeded run is the else branch throughout.
 *
 * PERF (raceBoot `?perf=1`): `race.perf()` is one snapshot of the last frame,
 * summed over the pixelizer's passes: draw calls, triangles, the programs the
 * renderer holds, live geometries / textures, the biggest texture edge in the
 * run scene, resident <audio> elements and the governor's device pixel ratio.
 * The world is NOT built under the menu: `prepare()` (raceBoot calls it once
 * "race" is pressed, before the intro) or the first `start()` builds it, and
 * `reseed` under the menu only resets the run state. The menu never pays for
 * the tunnel, the props' glb or the bubble textures, and the run's first frame
 * does not compile the world's programs: prepare() warms them.
 * ==========================================================================*/

import * as THREE from 'three';
import { Q } from '../shared/quality.js';
import { createTunnel, FOG_DENSITY } from '../engine/tunnel.js';
import { createFx } from '../engine/fx.js';
import { createPayloadFx } from '../game/payloadFx.js';
import { setBundledSpiralPool, prefetchSpirals, LEAN_SPIRALS } from '../engine/loomSpirals.js';
import { createScreenShake } from '../game/screenShake.js';
import { INTENSITY_RAMP_SEC, TREATS_ONLY_SEC, KART_BASE_SPEED, MULT_LADDER, makeRng } from './consts.js';
import { createPace } from './pace.js';
import { createSpine } from './spine.js';
import { roomById, rollRoomOrder, createRoomDresser } from './rooms.js';
import { createWalls } from './walls.js';
import { createCueSync, CUE_AHEAD_SEC } from './sync.js';
import { createWallDomPosters } from './wallDom.js';
import { KIND_BY_ID } from './bubbleKinds.js';
import { createCocktail, CATEGORIES } from './cocktail.js';
import { createBubbleField } from './bubbles.js';
import { createTrackState } from './track.js';
import { cueFor, resultTag } from './cues.js';
import { createKart } from './kart.js';
import { createScore } from './score.js';
import { createRaceHud } from './hud.js';
import { createCaptions } from './captions.js';
import { createMediaLane } from './mediaLane.js';
import { createInput } from './input.js';
import { createPickups, TUNE as PICK } from './pickups.js';
import { createPixelizer, PIXEL_DEFAULT } from './pixel.js';
import { createSpeedFx } from './speed.js';
import { vFovForAspect, bindViewportResize } from './viewport.js';
import { createRaceAudio } from './audio.js';
import { resultTier, resultsCamera, preRollCamera } from './intro.js';

const HEARTBEAT_MS = 2000, PAYOUT_WAIT_MS = 2000, NEAR_MISS_M = 1.15, FOV_BASE = 72;   // 1.6 read as ALMOST spam: the next lane over qualified
// FOV_BASE is the VERTICAL fov at 16:9 only; race/viewport.js re-solves it per aspect (see the header).
// effect lives are cocktail.js CATEGORIES (scaled by the pop's durationMult); a glitch re-pop wobbles
// the world clock this hard, this long
const WOBBLE_SCALE = 0.82, WOBBLE_SEC = 0.3;
const ladderMult = (combo) => { let m = 1; for (const [at, mult] of MULT_LADDER) if (combo >= at) m = mult; return m; };
const SPAWN_T0 = 2.5, RAIN_T0 = 20, EARLY_SLOW = 1.5;   // the opening drips slower (TREATS_ONLY_SEC)
// track cues: an act only re-dresses the world when no gate is this many seconds of road away, and
// the standalone page logs the scheduler this often (track time). CUE_AHEAD_SEC lives in race/sync.js.
const ACT_GATE_SEC = 6, TRACK_STATS_SEC = 10;
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
  // Q.leanLights (mobile): the dresser's sun, EMI's screen / bead points and this cupLight are hidden
  // (three.js does not count an invisible light); the ambient, the dresser's hemisphere (the pink sky the
  // props and the cup are painted for) and EMI's own cupLight are that tier's whole light bill
  scene.add(new THREE.AmbientLight(0x8a70a8, 1.0));
  const cupLight = new THREE.PointLight(0xff69b4, 1.4, 14);
  cupLight.visible = !Q.leanLights;
  scene.add(cupLight);
  // the vertical fov this window shape needs to hold FOV_BASE's 16:9 width of road; the per-frame
  // kick rides on top of it, and a resize slides S.fov by the same delta so the kick survives a flip
  let fovBase = FOV_BASE, fovLive = false;
  function resize() {
    const w = root.clientWidth || window.innerWidth, h = root.clientHeight || window.innerHeight;
    pixel.resize(w, h); camera.aspect = w / h;
    const next = vFovForAspect(FOV_BASE, camera.aspect);
    if (fovLive) { S.fov += next - fovBase; camera.fov = S.fov; } else camera.fov = next;
    fovBase = next;
    camera.updateProjectionMatrix();
  }
  const unbindResize = bindViewportResize(resize); resize();

  // ---- the parts that outlive a run ----
  const hud = createRaceHud(hudRoot);
  // the voice on the glass: the caption line under the chrome and the trigger plate over it.
  // It reads the track clock and nothing else, so a pause, a resume and a seek all land for free.
  const captions = hudRoot ? createCaptions(hudRoot) : null;
  const fxProxy = { pulseFlash: (a) => { if (W) W.fx.pulseFlash(a); } };   // fx is rebuilt on "again"
  const payloadFx = createPayloadFx({ hud: sfHud, fx: fxProxy, media });
  // Q.leanSpirals (mobile): spiral pops draw from the two lightest bundled gifs, fetched while the intro plays
  // (warmFx) rather than 2-5 MB mid-lap; the desktop pool and the Loom's own spirals are untouched
  setBundledSpiralPool(Q.leanSpirals ? LEAN_SPIRALS : null);
  let fxWarm = false;
  function warmFx() { if (fxWarm || !Q.leanSpirals) return; fxWarm = true; prefetchSpirals(LEAN_SPIRALS); }
  const lane = createMediaLane(sfHud);   // re-homes payload cards off the road
  const shake = createScreenShake({ el: root });
  if (reducedMotion) shake.setEnabled(false);
  const input = createInput({ root });   // root: the touch layer, on a phone, is built inside its .race-hud
  const audio = createRaceAudio({ bridge, hud, settings, input });
  const speedFx = createSpeedFx({ scene, camera, root, reducedMotion });
  const camOut = { pos: new THREE.Vector3(), look: new THREE.Vector3(), up: new THREE.Vector3(0, 1, 0), roll: 0 };
  const _v = new THREE.Vector3();

  // ---- run state ----
  const S = {
    started: false, running: false, paused: false, hostPaused: false, ended: false, disposed: false,
    elapsed: 0, t: 0, intensity: intensityFloor, timeScale: 1, jackpotBias: 1, sweep: false, tide: 1,
    spawnT: SPAWN_T0, rainT: RAIN_T0, tunnelTime: 0, rush: 0, fov: fovBase, fovBoost: 0, gates: 0, room: null,
    wasAirborne: false, airH: 0, effects: [], moodHeld: null, moodHold: 0, mood: 'calm', bestAtStart: 0, seed, wobble: 0,
    trackHold: 0, trackHoldFrom: 0, trackFog: 0, trackPaused: false, statsAt: 0, trackGap: 0,   // trackHold: the track second a fog/density hold ends, 0 for none
  };
  fovLive = true;   // S exists: resize() may shift S.fov from here on
  const mix = createCocktail({ now: () => S.elapsed });   // THE MIX: one live effect per category (cocktail.js); S.effects mirrors its live slots
  const TR = createTrackState();      // the loaded track chart: its clock, its energy, its acts (race/track.js)
  const PACE = createPace();          // how fast the road is allowed to feel this second (race/pace.js)
  const hosted = bridge.isHosted !== false;   // the standalone page logs where the host would be told
  const sync = createCueSync({ aheadSec: CUE_AHEAD_SEC, trace: true });   // the visible half of a cue waits for the word (race/sync.js); the trace is 64 small rows the smokes read
  let W = null;                       // the world: everything that is rebuilt on "again"
  let raf = 0, last = 0, lastBeat = 0, payoutResolve = null;
  let camOverride = null;                // fn(camera, dt, w, camOut) -> false when done (intro.js cameras)
  let stage = null;                      // { update(dt), render() } drawn INSTEAD of the world while set (menu, intro)
  // last ~1 s of kart {d,x,h} for the ALMOST on a miss: a fixed ring, no per-frame objects
  const TRAIL_N = 70, trail = [];
  for (let i = 0; i < TRAIL_N; i++) trail.push({ d: 0, x: 0, h: 0, ok: false });
  let trailI = 0;
  const trailClear = () => { for (const s of trail) s.ok = false; trailI = 0; };

  function poke(mood, sec) { S.moodHeld = mood; S.moodHold = sec; }

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
    // the online feed cannot be a texture, so it rides a DOM layer over the canvas instead
    // (race/wallDom.js); the painted fill placards stand down as soon as its pictures are in
    let wallDom = null;
    const walls = createWalls({ scene, layout, media, renderer, camera, rng, hasDomPosters: () => !!wallDom && wallDom.ready() });
    wallDom = createWallDomPosters({ root, layout, camera, media, rng });
    const kart = createKart({ scene, layout, reducedMotion, pixel });
    const score = createScore();
    const getRoom = () => {
      const r = roomById(layout.roomAtDepth(kart.state.d));
      if (!r || S.jackpotBias === 1) return r;
      return { ...r, bubbleBias: { ...r.bubbleBias, golden: (r.bubbleBias.golden == null ? 1 : r.bubbleBias.golden) * S.jackpotBias } };
    };
    const field = createBubbleField({ scene, layout, media, getIntensity: () => S.intensity, getRoom, getElapsed: () => S.elapsed, onTexture: pixel.filterTexture });
    const pickups = createPickups({ rng, spots: layout.chunks.flatMap((c) => c.features || []).filter((f) => f.type === 'pickup'), totalDepth: layout.totalDepth });
    const w = { layout, tunnel, fx, dresser, walls, wallDom, kart, score, field, pickups, rng };
    field.onPop((p) => onPop(w, p));
    field.onMiss((m) => onMiss(w, m));
    kart.onEvent((e) => onKart(w, e));
    score.onEvent((e) => onScore(w, e));
    pickups.onEvent((e) => onPickup(w, e));
    field.setTracked(!!TR.track);
    field.setSparse(TR.lyrics);
    pixel.retexture(scene);
    return w;
  }
  function teardown() {
    if (!W) return;
    try { W.field.dispose(); W.kart.dispose(); W.wallDom.dispose(); W.walls.dispose(); W.dresser.dispose(); W.fx.dispose(); } catch (e) { /* half-built world */ }
    scene.remove(W.tunnel.mesh); W.tunnel.dispose();
    W = null;
  }
  function resetRunState(runSeed) {
    Object.assign(S, { running: false, paused: false, ended: false, elapsed: 0, t: 0, intensity: intensityFloor, timeScale: 1,
      jackpotBias: 1, sweep: false, tide: 1, spawnT: SPAWN_T0, rainT: RAIN_T0, rush: 0, fovBoost: 0, gates: 0, room: null,
      wasAirborne: false, airH: 0, effects: [], moodHeld: null, moodHold: 0, mood: 'calm', seed: runSeed,
      trackHold: 0, trackHoldFrom: 0, trackFog: 0, trackPaused: false, statsAt: 0, trackGap: 0 });
    trailClear();
    mix.reset(); PACE.reset(); S.wobble = 0; clearMixChrome(); sync.reset();
    hud.setScore(0); hud.setCombo(0, 1); hud.setBank(0); hud.setSpeed(0); hud.setFraught(0); hud.passiveClear(); TR.gild(0);
  }

  // ---- rooms ----
  function enterRoom(w, roomId, actName) {
    const first = S.gates === 0;
    const spec = w.dresser.applyRoom(w.fx, roomId, first ? 0.2 : 1.2);
    S.room = spec; S.gates++;
    hud.banner(actName || spec.name, spec.tagline, hex(spec.colors.banner));
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
    if (p.id === 'golden') {
      w.score.jackpot(S.jackpotBias > 1 ? 'major' : 'minor');
      sfx('golden_pop', 0.9); shake.shake(0.35, 240); poke('jackpot', 1.4); w.kart.pose('cheer');
    }
  }
  function onPop(w, p) {
    if (p.eventId) TR.taken(p.eventId);
    w.kart.pulseTarget(); w.kart.pose('grab', { side: (p.x == null ? w.kart.state.x : p.x) >= w.kart.state.x ? 1 : -1 });
    if (S.sweep) sfx('chain_pop', 0.5);          // the pump: every pop on the road sounds like the chain
    if (p.kind === 'treat') return treat(w, p);
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
    if (p.payload === 'video') { trackPause(true); send({ type: 'fire-payload', kind: 'video', strength, durationMult }); }
    else payloadFx.applyPayload({ payload: { kind: p.payload, overlay: p.overlayKind }, strength }, { durationMult });
  };
  function clearMixChrome() { root.removeAttribute('data-ov'); root.removeAttribute('data-tint'); if (hud.setTint) hud.setTint(0); }
  /** Pour one action. Sustained holds (tint / overlay / corruption) get a durationMult that lands payloadFx's
   *  fade on the mixer's drain, so a tint that was extended stays pink for as long as the rail says it will. */
  function pour(w, p, r, strength, durationMult) {
    const label = (KIND_BY_ID[p.id] || {}).label || p.id;
    const slot = mix.live(r.category);
    const holdMult = slot && CATEGORIES[r.category].scaled ? clamp(slot.sec / CATEGORIES[r.category].sec, 0.1, 10) : durationMult;
    w.kart.pose('clamp', { hold: clamp(slot ? slot.sec : 1.2, 0.6, 4) });   // she rides the pour out
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
    for (const s of trail) { if (!s.ok) continue; const g = Math.abs(w.layout.wrap(s.d - m.d + w.layout.totalDepth / 2) - w.layout.totalDepth / 2); if (g < bestGap) { bestGap = g; best = s; } }
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
      case 'bank': hud.setBank(e.banked); hud.setScore(0); hud.toast(`kept +${e.amount}`, 'bank'); break;
      case 'jackpot': hud.setScore(e.score); hud.toast(`jackpot +${e.gain}`, 'jackpot'); break;
    }
  }
  // ---- THE PICKUPS (race/pickups.js): passive, taken by driving through them ----
  function onPickup(w, e) {
    switch (e.type) {
      case 'pickupSpawn': { const p = w.pickups.byId(e.id); if (p) w.dresser.showPickup({ d: e.d, x: e.x, sprite: p.sprite }); break; }
      case 'pickupDrop': w.dresser.hidePickup(false); break;
      // the take beat: the white flash on the spot, the collect sound, EMI's grab, a plain treat's
      // points (the combo stays warm), then the effect itself (a refresh only restarts the bar)
      case 'pickupTake':
        w.dresser.hidePickup(true);
        w.score.pop(PICK.POINTS, 'pickup');
        sfx('tunnel_powerup_collect', 0.8); shake.shake(0.2, 120); poke('smug', 0.9); w.kart.pose('grab');
        if (!e.refresh) applyPickup(w, e.p, true);
        break;
      case 'pickupEnd': applyPickup(w, w.pickups.byId(e.id), false); hud.passive(e.id, null); break;
    }
  }
  /** The one door pickups.js has into the file: the next chart event's second (track.js nextEvent). */
  const nextEventT = (t, kind) => { const e = TR.nextEvent(t, kind); return e ? e.t : null; };
  /** What each pickup DOES, on and off. Every number comes off its PICKUPS row. */
  function applyPickup(w, p, on) {
    if (!p) return;
    switch (p.id) {
      // poppers: the cup grows, the pop box grows with it and the seat slides back (kart.js setScale)
      case 'poppers': w.kart.setScale(on ? p.scale : 0); w.field.setReach(on ? p.reach : 1); w.kart.setReach(on ? p.reach : 1); break;
      // the pump: the whole road is the pop box, and the kart rides a boost for the length of it
      case 'the_pump': S.sweep = on; w.field.setSweep(on); if (on) w.kart.applyBoost(p.sec); break;
      // pocket watch: the cup swings like the pendulum and the combo clock stops for the length of it
      case 'pocket_watch': w.kart.setSway(on ? p.swing : 0, p.period); if (on) w.score.freezeCombo(p.sec); break;
      // the wand: the pop box reaches for treats alone (an effect bubble still has to be driven into)
      case 'the_wand': w.field.setReach(on ? p.reach : 1, on); w.kart.setReach(on ? p.reach : 1); break;
      // rabbit foot: the seeded lanes lean golden; on a worded road the next rows carry one instead (cues.js)
      case 'rabbit_foot': S.jackpotBias = on ? p.bias : 1; TR.gild(on && TR.lyrics ? p.rows : 0); break;
      // golden touch: every pop pays double for sec (score.js boostMult; off resets it)
      case 'golden_touch': w.score.boostMult(on ? p.mult : 1, on ? p.sec : 0); break;
      // riptide: the road ahead slides into the lane and the cruise runs faster through the pace
      case 'riptide': S.tide = on ? p.speed : 1; w.field.setPull(on); break;
    }
  }

  // ---- the track chart: the file is the clock (CHART.md) ----
  /** Page -> host, and only when there is a host and a track: the audio has to follow the run. */
  function trackSend(type, data) { if (hosted && TR.track) send({ type, ...(data || {}) }); }
  /** The Brake, a host pause and a video pop all stop the voice; anything that resumes starts it. */
  function trackPause(on) {
    if (!TR.track || S.trackPaused === !!on) return;
    S.trackPaused = !!on;
    trackSend('track-pause', { on: !!on });
  }
  /** Load a chart (null goes back to the seeded run). Call before start(), or live for an upgrade. */
  // a chart's acts decide the room order at every gate (step: ts.act.room); the music prefetches by that route
  const routeOf = (t) => { const acts = t && t.chart && Array.isArray(t.chart.acts) ? t.chart.acts : null; return acts ? acts.map((a) => a.room).filter((r, i, arr) => r && r !== arr[i - 1]) : null; };
  function setTrack(chart) {
    const t = TR.setTrack(chart);
    audio.setRoute(routeOf(t));
    S.trackHold = 0; S.statsAt = 0; sync.reset();
    if (W) { W.field.setTracked(!!t); W.field.setSparse(TR.lyrics); W.field.setDensity(1); if (!t) applyFog(W, 0); }
    if (captions) captions.setTrack(t ? t.chart : null);
    audio.duck(!!t, 'track');   // the file is the soundtrack: the room OST sits under it until it is cleared
    if (bridge.log) bridge.log(t ? `race track: ${t.name}, ${Math.round(t.durationSec)}s, ${TR.stats().countable} to take` : 'race track: cleared');
    return t;
  }
  /** The chart's fog knob rides the tunnel weather: fx.update writes scene.fog every frame, so the
   *  zone is the only fog dial the run may hold. 0 hands the organic roll back. */
  function applyFog(w, v) {
    S.trackFog = clamp(Number(v) || 0, 0, 1);
    if (w.fx.forceZone) w.fx.forceZone(S.trackFog > 0.4 ? 'pinkfog' : null);
  }
  /** A cue's `mix`: pour an effect through THE MIX exactly as a popped bubble of that kind would. */
  function cueMix(w, kindId) {
    const k = KIND_BY_ID[kindId];
    if (!k || k.kind !== 'effect') return;
    const durationMult = 0.5 + 0.5 * S.intensity;
    const r = mix.add(k.id, { durationMult });
    if (r.action === 'held' || r.action === 'ignore') return;
    pour(w, { id: k.id, payload: k.payload, overlayKind: k.overlayKind }, r, Math.round(clamp(k.strength, 0, 1) * 100), durationMult);
    if (r.recipe) serve(w, r.recipe);
  }
  /** One event off the scheduler. The scheduler hands it over LEAD_SEC early on purpose, so the SPAWNS
   *  can go down the road at the depth the kart will have reached when the voice says the word: they
   *  go in now. Everything the player sees or hears is held by race/sync.js and fired from trackFrame
   *  the frame the track clock reaches event.t; spent here it landed 2.5 s before the word (the
   *  owner's "too soon by 2 sec or so", measured in race/smoke/sync-check.mjs). */
  function applyCue(w, due) {
    const ks = w.kart.state, e = due.event, t = TR.track ? TR.track.t : 0;
    const cue = cueFor(e, { energy: TR.intensity, act: TR.act, room: S.room, intensity: S.intensity, rng: w.rng, triggerKinds: TR.triggerKinds, lyrics: TR.lyrics, gold: () => TR.takeGold() });
    if (!cue) { TR.skip(e.id); return; }   // a guess the feel pass threw out never counts against the player
    // A row goes in as one thing (bubbles.js spawnRow): the density gate is rolled once for the
    // whole line, so the road never gets a row with a hole in it that the kart can steer through.
    // Its depth assumes the speed the kart has now; the sync re-places it every frame until the
    // kart is on it, so a boost or a ramp inside the lookahead still lands the row on the word.
    // A loose treat keeps its first depth: it is a guess at an unsure word, not a pop on a beat.
    const row = cue.spawn.filter((sp) => sp.row), loose = cue.spawn.filter((sp) => !sp.row);
    if (row.length) {
      const at = e.t + (row[0].at || 0), d = sync.depthFor(t, ks.d, ks.speed, at);
      const rowId = w.field.spawnRow({ kindId: row[0].kindId, kindIds: row.map((sp) => sp.kindId), placement: row[0].placement, d, h: row[0].h, xs: row.map((sp) => sp.x), eventId: e.id });
      if (rowId) sync.trackRow(rowId, e, at, d, t);
    }
    for (const sp of loose) {
      w.field.spawnAt({ kindId: sp.kindId, placement: sp.placement, d: sync.depthFor(t, ks.d, ks.speed, e.t + (sp.at || 0)), x: sp.x, h: sp.h, eventId: e.id });
    }
    sync.defer(e, cue, t);
  }
  /** The visible half of a cue, on the word: trackFrame fires it the frame the clock reaches event.t. */
  function spendCue(w, event, cue) {
    const ks = w.kart.state;
    if (cue.jump) { ks.vh = Math.max(ks.vh, cue.jump); ks.h = Math.max(ks.h, 0.06); ks.airborne = true; w.kart.pose('air'); }
    if (cue.mix) cueMix(w, cue.mix);
    if (cue.mood) poke(cue.mood, Math.max(1.2, cue.holdSec));
    if (cue.pose) w.kart.pose(cue.pose);
    if (cue.toast) hud.toast(cue.toast.text, cue.toast.kind || 'effect');
    // a sure trigger no longer whispers on the toast rail: it flies at the camera, themed off the
    // preset its set carries (race/captions.js + race/triggerTheme.js). Everything else still toasts.
    if (cue.word) { if (event.kind === 'trigger' && captions) captions.showPlate(event); else hud.toast(cue.word, 'effect'); }
    if (cue.fog != null) applyFog(w, cue.fog);
    if (cue.boost) w.kart.applyBoost(cue.boost);
    if (cue.density != null) w.field.setDensity(cue.density);
    // the hold is a stretch of the TRACK, not a count of frames: a seek past it must not carry the
    // fog and the empty road along for the seconds it had left (a 20 s silence hold once did)
    if (cue.holdSec > 0) { S.trackHoldFrom = event.t; S.trackHold = event.t + cue.holdSec; }
  }
  /** The act moved with no gate in reach: dress the new room where we stand and marquee its name. */
  function actMoved(w, act, ks) {
    if (!act) return;
    for (const f of w.layout.featuresBetween(ks.d, ks.d + Math.max(20, ks.speed * ACT_GATE_SEC))) if (f.type === 'gate') return;
    S.room = w.dresser.applyRoom(w.fx, act.room, 2.5);
    S.gates++;
    hud.banner(act.name, S.room.tagline, hex(S.room.colors.banner));
    sfx('depth_change', 0.6);
  }
  /** One frame of the track. Returns true when the file is over and the run has been ended. */
  function trackFrame(w, ts) {
    const ks = w.kart.state;
    if (S.trackHold > 0 && (ts.t >= S.trackHold || ts.t < S.trackHoldFrom)) { S.trackHold = 0; applyFog(w, 0); w.field.setDensity(1); }   // over, or a seek back to before it (the scheduler re-hands it)
    if (captions) captions.update(ts.t);   // the phrase is a function of the second, never of the frame
    for (const due of TR.due(ks.d, ks.speed)) applyCue(w, due);
    // the sync: rows nudged onto their word off the speed the kart has now, held cues fired on the second
    const held = sync.update(ts.t, ks.d, ks.speed);
    for (const m of held.move) w.field.moveRow(m.rowId, m.d);
    for (const f of held.fire) spendCue(w, f.event, f.cue);
    if (ts.actChanged) actMoved(w, ts.act, ks);
    if (ts.dt > S.trackGap) S.trackGap = ts.dt;   // the worst frame the scheduler had to reach over
    if (!hosted && ts.t - S.statsAt >= TRACK_STATS_SEC) {   // standalone dev aid: the scheduler on the console
      S.statsAt = ts.t;
      if (bridge.log) bridge.log(`track ${ts.t.toFixed(0)}s gap ${S.trackGap.toFixed(2)}s ${JSON.stringify(TR.stats())}`);
      S.trackGap = 0;
    }
    if (ts.ended) { endRun(); return true; }
    return false;
  }

  // ---- kart events (drift turbo, tricks, the wheel, laps) ----
  const TURBO = ['', 'mini turbo', 'super turbo', 'ultra turbo'];
  function onKart(w, e) {
    switch (e.type) {
      case 'driftTier': sfx('ui_click', 0.3 + 0.15 * e.tier); w.kart.pose('drift', { side: w.kart.state.steer > 0 ? 1 : -1, tier: e.tier }); break;
      case 'driftBoost': hud.toast(TURBO[e.tier] || 'turbo', 'pop'); sfx('tunnel_powerup_collect', 0.5 + 0.15 * e.tier); if (e.tier >= 2) shake.shake(0.12 * e.tier, 160); poke(e.tier >= 3 ? 'smug' : 'streamed', 1.0); w.kart.pose('boost'); break;
      case 'trick': { const g = w.score.trick(e.points, e.name); hud.toast(`${e.name} +${g}`, 'pop'); sfx('chain_pop', 0.8); poke('smug', 0.6); w.kart.pose('air'); break; }
      case 'landing': w.kart.pose(e.clean ? 'landing' : 'landingKerb'); if (e.trick) { hud.toast(e.clean ? (e.streak >= 3 ? 'hat trick, clean' : 'clean') : 'kerbed it', e.clean ? 'pop' : 'almost'); if (e.clean) sfx('surface', 0.5); } break;
      case 'inverted': w.score.setInverted(e.on); w.kart.pose(e.on ? 'tuck' : 'cruise'); if (e.on) { hud.toast('upside down', 'effect'); poke('shock', 0.8); } break;
      case 'lap': { const r = w.score.lap(e.sec); hud.toast(`lap ${r.text}`, 'item'); if (r.pb && r.prevBest > 0) { hud.toast('pb!', 'jackpot'); sfx('pb_fanfare', 0.9); poke('jackpot', 1.5); w.kart.pose('cheer'); } break; }
      case 'jump': w.kart.pose('air', { hold: e.big ? 0.7 : 0.3 }); if (e.big) { hud.toast('big air', 'pop'); sfx('tunnel_powerup_collect', 0.6); poke('streamed', 0.9); } break;
      case 'split': w.score.pace(e.frac, e.sec); break;
    }
  }

  // ---- the frame ----
  function step(w, dt) {
    const k = w.kart, ks = k.state, lay = w.layout;
    S.elapsed += dt;
    // with a track the intensity is the file's energy curve (smoothed, floored), not the clock ramp.
    // step() takes no delta: the voice runs on the wall, never on the run's clamped frame (track.js).
    const ts = TR.step();
    S.intensity = clamp(Math.max(intensityFloor, ts ? ts.intensity : S.elapsed / INTENSITY_RAMP_SEC), 0, 1);
    const wdt = dt * S.timeScale;                     // the world clock (tea_time); the kart keeps real time
    S.t += wdt;
    const prevD = ks.d;
    // THE OPENING IS GENTLE (race/pace.js): the first act cruises at 0.7 of the base with a boost
    // that only lifts to 1.15 of it, then the full curve walks in over 20 s, and the act's kind
    // colours the pace from there. Off a chart the clock is the run's own elapsed seconds.
    S.pace = PACE.at(ts ? ts.t : S.elapsed, ts ? ts.act : null, TR.track ? TR.track.chart : null);
    k.pace(Math.min(S.pace.base * S.tide, S.pace.cap), S.pace.cap);   // riptide runs the cruise faster, under the same ceiling
    k.update(dt, input.read(), lay);
    w.score.tick(dt);
    w.pickups.update(dt, { d: ks.d, x: ks.x, speed: ks.speed, elapsed: S.elapsed, opening: !!(S.pace && S.pace.opening), mult: w.score.state.mult,
      t: TR.track ? TR.track.t : null, nextEventT });
    for (const c of w.pickups.chips()) hud.passive(c.id, c);
    { const tr = trail[trailI]; tr.d = ks.d; tr.x = ks.x; tr.h = ks.h; tr.ok = true; trailI = (trailI + 1) % TRAIL_N; }
    for (const e of mix.tick(dt)) onMix(w, e);
    if (S.wobble > 0) { S.wobble -= dt; if (S.wobble <= 0 && S.timeScale === WOBBLE_SCALE) S.timeScale = 1; }
    const mixed = mix.state(); S.effects = mixed.live;
    // the mixer rail only needs a frame while something is live (or just went dark)
    const mixOn = mixed.live.length > 0 || !!mixed.recipe;
    if (hud.mixer && (mixOn || S.mixWasOn)) hud.mixer(mixed);
    S.mixWasOn = mixOn;

    // bubbles: seed the chunks ahead, drip spawns, rain bursts
    for (const c of lay.chunks) { const rel = lay.wrap(c.d0 - ks.d + lay.totalDepth / 2) - lay.totalDepth / 2; if (rel > -20 && rel < 250) w.field.seedChunk(c); }
    // a track owns the spawns outright: the random drip and the rain bursts stand down, seedChunk
    // still dresses the chunks ahead so the road never looks empty between the spoken words
    if (ts) { if (trackFrame(w, ts)) return; }
    else {
      const early = S.elapsed < TREATS_ONLY_SEC ? EARLY_SLOW : 1;
      S.spawnT -= wdt;
      if (S.spawnT <= 0) { w.field.spawnAhead(ks.d, 1 + Math.round(S.intensity * 2)); S.spawnT = (3.4 - 1.8 * S.intensity) * early; }
      S.rainT -= wdt;
      if (S.rainT <= 0) { w.field.rain(ks.d, 3 + Math.round(S.intensity * 4)); S.rainT = (S.room && S.room.loud ? 9 : 14) * (1 - 0.5 * S.intensity) * early; }
    }
    w.field.update(wdt, S.t, ks);

    // track features crossed this frame
    for (const f of lay.featuresBetween(prevD, ks.d)) {
      if (f.type === 'boost' && !ks.airborne && Math.abs(f.x - ks.x) <= 1.2) { k.applyBoost(1.6); sfx('tunnel_powerup_collect', 0.8); shake.shake(0.25, 200); poke('streamed', 1.2); k.pose('boost'); }
      else if (f.type === 'gate') enterRoom(w, ts && ts.act ? ts.act.room : f.room, ts && ts.act ? ts.act.name : null);
    }
    if (ks.airborne) S.airH = Math.max(S.airH, ks.h);
    if (S.wasAirborne && !ks.airborne) {                        // the jolt is the flight's: a 1.1 m jump is not a ramp
      shake.shake(0.8 * clamp(S.airH / 2.5, 0.3, 1), 300); poke('smug', 0.7); S.airH = 0;
    }
    S.wasAirborne = ks.airborne;
    for (const n of w.score.drainNotes()) { hud.toast(n.text, n.kind); if (n.mood) poke(n.mood, 1.2); if (n.sfx) sfx(n.sfx, 0.9); }   // upside down, full circle, hot lap

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
    // FOV kick: boost snaps wide (to ~84) and eases back slowly; drift adds a smaller one; a slowed clock narrows
    const boostT = ks.boostSec > 0 ? 1 : 0;
    S.fovBoost += (boostT - S.fovBoost) * Math.min(1, dt * (boostT ? 9 : 2.2));
    const fovT = fovBase + (reducedMotion ? 5 : 12) * S.fovBoost + (ks.drift && !reducedMotion ? 3 : 0) - (S.timeScale < 1 ? 4 : 0);
    if (Math.abs(fovT - S.fov) > 0.05) { S.fov += (fovT - S.fov) * Math.min(1, dt * 6); camera.fov = S.fov; camera.updateProjectionMatrix(); }
    cupLight.position.copy(k.group.position).addScaledVector(_v.copy(camOut.up), 1.6);
    speedFx.update(dt, ks, lay);
    w.wallDom.update(ks.d);   // AFTER the camera: a DOM poster is transformed by THIS frame's lens

    // EMI: calm cruise, streamed on boost, fraught under a stack, pokes on top
    if (S.moodHold > 0) { S.moodHold -= dt; if (S.moodHold <= 0) S.moodHeld = null; }
    const base = (S.effects.length && S.intensity > 0.5) ? 'fraught' : ks.boostSec > 0 ? 'streamed' : 'calm';
    const mood = S.moodHeld || base;
    if (mood !== S.mood) { S.mood = mood; k.setMood(mood); }
    const fraught = clamp(S.effects.length / 3, 0, 1);
    k.setFraught(fraught); hud.setFraught(fraught);
    hud.setSpeed(ks.speed, ks.boostSec > 0);
    audio.update(dt, { world: w, run: S, kart: ks });
  }

  function frame(now) {
    if (S.disposed) return;
    raf = requestAnimationFrame(frame);
    if (now - lastBeat > HEARTBEAT_MS) { lastBeat = now; send({ type: 'heartbeat', t: now }); }
    const dt = last ? clamp((now - last) / 1000, 0, 0.1) : 0;
    last = now;
    // the DOM wall is over the canvas and nothing in the scene can hide it, so it goes away
    // whenever the world is not the thing on screen: the menu, the intro, the Brake, the host's pause
    if (W) W.wallDom.setHidden(!!stage || S.paused || S.hostPaused || S.ended || !S.running);
    if (stage) { try { stage.update(dt); stage.render(); } catch (e) { bridge.log && bridge.log('race stage: ' + (e && e.stack || e)); } return; }
    if (!W) return;
    if (S.running && !S.paused && !S.hostPaused) {
      try { step(W, dt); } catch (e) { bridge.log && bridge.log('race step: ' + (e && e.stack || e)); }
    }
    if (camOverride && camOverride(camera, dt, W, camOut) === false) camOverride = null;
    pixel.render(scene, camera);
  }

  // ---- brake / end / again / exit ----
  async function brake() {
    if (!W || !S.running || S.ended || S.paused) return;
    S.paused = true; sfx('ui_click', 0.5);
    audio.duck(true, 'brake'); trackPause(true);
    const pick = await hud.setPaused(true);
    if (S.disposed || !S.paused) return;
    if (pick === 'end') return endRun();
    S.paused = false;
    audio.duck(false, 'brake'); trackPause(false);
  }
  function waitPayout(ms) {
    return new Promise((res) => { const t = setTimeout(() => { payoutResolve = null; res(null); }, ms); payoutResolve = (m) => { clearTimeout(t); payoutResolve = null; res(m); }; });
  }
  async function endRun() {
    if (!W || S.ended) return;
    const w = W;
    S.ended = true; S.running = false; S.paused = false;
    try { payloadFx.cancelHeavy(); } catch (e) { /* nothing heavy */ }
    if (captions) captions.clear();   // nothing of the last phrase is left over the end card
    const st = w.score.state;
    const summary = { score: st.score, banked: st.banked, bestCombo: st.bestCombo, popped: st.popped, treats: st.treats, effects: st.effects,
      nearMisses: st.nearMisses, laps: w.kart.state.lap, durationSec: Math.round(S.elapsed), seed: S.seed,
      personalBest: st.banked + st.score > S.bestAtStart && st.banked + st.score > 0 };
    const track = TR.summary();   // "you took N of M" on a charted run (the results screen is PR c7)
    if (track) Object.assign(summary, { taken: track.taken, countable: track.countable, trackName: track.name });
    send({ type: 'run-ended', ...summary, ...(track ? { track } : {}) });
    trackSend('track-stop');
    sfx('surface', 0.8);
    audio.duck(true, 'end');
    setCameraOverride(resultsCamera({ tier: resultTier(st.banked + st.score, S.bestAtStart, summary.personalBest), reducedMotion }));   // she turns to face the card
    const payout = await waitPayout(PAYOUT_WAIT_MS);
    if (S.disposed) return;
    const shown = { ...summary };
    if (track && track.countable > 0) shown.title = `you took ${track.taken} of ${track.countable} · ${resultTag(track.taken, track.countable)}`;   // a charted run is scored by the words it met
    if (payout && payout.finalXp != null) shown.title = (shown.title || 'the tea party') + ` · +${Math.round(payout.finalXp)} xp` + (payout.sparksEarned ? ` · ${payout.sparksEarned} sparks` : '');
    const pick = await hud.showEnd(shown, { beside: true });
    if (S.disposed) return;
    if (pick === 'again') again(); else exit();
  }
  /** Rebuild the world on a new seed (again, or the menu changing the seed rule). settings.seedLock pins again to one track.
   *  A world that was never built (the menu changing the rule before "race") stays unbuilt: prepare() / start() own that. */
  function reseed(runSeed) { const had = !!W; teardown(); resetRunState(runSeed); if (had) W = build(runSeed); S.started = false; }
  /** Build the world now (after "race" is pressed, before the intro) so neither the menu nor the run's first
   *  frame pays for it; the renderer compiles the world's programs in the same breath. A no-op once built. */
  function prepare() {
    if (S.disposed || W) return;
    warmFx();
    W = build(S.seed);
    try { renderer.compile(scene, camera); } catch (e) { /* a warm-up only: the first frame compiles what this missed */ }
  }
  function again() {
    reseed(settings.seedLock != null ? settings.seedLock >>> 0 : (Date.now() ^ Math.floor(Math.random() * 0x7fffffff)) >>> 0);
    setCameraOverride(preRollCamera());   // again skips the intro: the chase seat, then 3 2 1
    hud.countdown().then(start);
  }
  function exit() {
    trackSend('track-stop');
    send({ type: 'exit' });
    dispose();
    send({ type: 'exit-done' });
  }

  // ---- host + input wiring ----
  bridge.on('pause', (m) => setPaused(!!(m && m.on)));
  bridge.on('payout-result', (m) => { if (payoutResolve) payoutResolve(m); });
  function cyclePixel() { pixel.cycle(); pixel.retexture(scene); hud.toast(pixel.label(), 'item'); }
  input.onAction((a) => { if (a === 'brake') brake(); else if (a === 'pixel') cyclePixel(); });
  const onVis = () => { last = 0; };
  document.addEventListener('visibilitychange', onVis);

  function start() {
    if (S.started || S.disposed) return;
    if (!W) { warmFx(); W = build(S.seed); }   // ?autostart=1 skipped prepare(): the warm-up rides the first seconds
    S.started = true; S.running = true; S.ended = false;
    input.flush();          // a space that closed the last introduction card is not a jump on frame one
    S.bestAtStart = W.score.state.best;
    camOverride = null;
    S.room = W.dresser.applyRoom(W.fx, W.layout.roomAtDepth(0), 0.2);   // the gate 9 m in shows the banner
    W.kart.setMood('calm');
    send({ type: 'run-started', seed: S.seed });
    if (TR.track) { S.trackPaused = false; trackSend('track-play', { name: TR.track.name }); }
    last = 0;
  }
  /** Host pause (native video playing etc): freezes the frame, no Brake screen. */
  function setPaused(on) { S.hostPaused = !!on; if (!on) last = 0; audio.duck(!!on, 'host'); trackPause(!!on); }
  function dispose() {
    if (S.disposed) return;
    S.disposed = true;
    if (raf) cancelAnimationFrame(raf);
    unbindResize();
    document.removeEventListener('visibilitychange', onVis);
    if (payoutResolve) payoutResolve(null);
    teardown();
    audio.dispose();
    input.dispose(); hud.dispose(); if (captions) captions.dispose(); shake.dispose(); payloadFx.dispose(); speedFx.dispose(); lane.dispose();
    pixel.dispose();
    scene.clear(); renderer.dispose();
  }

  /** Screenshot aid (`?pickup=<id>` on the standalone page): stand that pickup up on the nearest spot
   *  ahead, gentle start or not, so a headless shot catches it without anyone steering. A spot too far
   *  to read is pulled into view first (the kart's depth jumps): a dev-only warp, and the reason this
   *  is never reachable from the host. False until the run is up, the road is clear and a spot is ahead. */
  function debugPickup(id) {
    if (!W || !S.running || S.paused || W.pickups.live) return false;
    const ks = W.kart.state, lay = W.layout, half = lay.totalDepth / 2;
    let best = null, bestRel = Infinity;
    for (const f of W.pickups.spots) {
      const rel = lay.wrap(f.d - ks.d + half) - half;
      if (rel > 5 && rel < bestRel) { bestRel = rel; best = f; }
    }
    if (!best || !W.pickups.light(id, best)) return false;
    if (bestRel > 16) { ks.d = lay.wrap(best.d - 16); trailClear(); }
    ks.x = best.x;
    return true;
  }

  resetRunState(seed);          // the world waits for prepare() / start(): frame() draws the stage until then
  raf = requestAnimationFrame(frame);
  function setCameraOverride(fn) { camOverride = typeof fn === 'function' ? fn : null; }
  /** One perf snapshot (the `?perf=1` aid). Never throws: a missing counter reads as -1. */
  function perf() {
    const info = renderer.info || {}, mem = info.memory || {}, st = pixel.stats || {};
    let texMax = 0;
    try {
      scene.traverse((o) => {
        const mats = Array.isArray(o.material) ? o.material : o.material ? [o.material] : [];
        for (const m of mats) for (const k of ['map', 'emissiveMap', 'alphaMap']) {
          const im = m[k] && m[k].image;
          if (im && im.width) texMax = Math.max(texMax, im.width, im.height || 0);
        }
      });
    } catch (e) { texMax = -1; }
    return {
      calls: st.calls == null ? -1 : st.calls, triangles: st.triangles == null ? -1 : st.triangles, passes: st.passes || 0,
      frameMs: st.frameMs || 0, programs: info.programs ? info.programs.length : -1,
      geometries: mem.geometries == null ? -1 : mem.geometries, textures: mem.textures == null ? -1 : mem.textures, texMax,
      audio: audio._tracks ? audio._tracks.size : -1, dpr: renderer.getPixelRatio(), block: pixel.block,
      world: !!W, stage: !!stage, running: S.running, bubbles: W ? W.field.liveCount : 0,
      // the pace envelope and what the kart actually did with it (race/pace.js, race/smoke/pace-check.mjs)
      speed: W ? W.kart.state.speed : 0, boosting: W ? W.kart.state.boostSec > 0 : false, pace: S.pace ? { ...S.pace } : null,
    };
  }
  function setStage(s) { stage = s && typeof s.update === 'function' ? s : null; if (!stage) pixel.retexture(scene); }   // the menu may have changed the block
  return { start, prepare, setPaused, dispose, setCameraOverride, setStage, reseed, renderer, pixel, audio, hud, camera, perf,
    // track charts (CHART.md): setTrack before start(), replaceTrack for the words pass landing live,
    // trackClock for the host's 250 ms tick, trackEnded when the file runs out at the host's end
    setTrack, replaceTrack: (chart) => { TR.replace(chart); audio.setRoute(routeOf(TR.track)); if (W) W.field.setSparse(TR.lyrics); if (captions) captions.setTrack(TR.track ? TR.track.chart : null); }, trackClock: (t, playing) => TR.clock(t, playing),
    trackEnded: () => { TR.end(); if (TR.track && S.running) endRun(); }, trackStats: () => TR.stats(), syncTrace: () => sync.trace(), debugPickup,
    get track() { return TR.track; } };
}

// self-check: node --check is the bar; everything touches the DOM inside createRace.
