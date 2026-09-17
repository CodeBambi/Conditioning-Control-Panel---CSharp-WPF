import { siliconeRebound, SILICONE_SETTLE_MS, customLeverReturn, LEVER_RETURN_MS, releasedLeverAngle, customSpinLeverAngle, customSpinReturnMs } from '../../room/lever-return.js';
import { sampleEmiReaction } from '../../room/emi-gestures.js';
/* ============================================================================
 * scene.js - the slot cabinet in three.js, ported from blender-scripting
 * slot/preview/{app,polish}.js. One WebGL context per createScene(), freed by
 * dispose(). Nodes are looked up by NAME only (nodes.js); framing comes from
 * cam_seat -> cam_target and live bounds, because the glb is still moving.
 *
 * The feel (lane F1) is decided in feel.js and played here: the lever's Law VIII
 * answer, THE THUD per reel, THE SHIVER, THE BREATH, THE MARQUEE heat on
 * marquee_glow, the payout_tray thud, and one cabinet celebration per win tier.
 *
 * Not ported from the preview: the jewellery trims (hard-coded coordinates), the
 * debug face strip and the view toggle. The payline frame IS here, but taken off
 * live bounds (playbook A6), never off the preview's coordinates.
 * ==========================================================================*/

import * as THREE from 'three';
import { createApronTicker } from './apron-ticker.js';
import { createCrownDisplay } from './crown-display.js';
import { createRimLighting } from './rim-lighting.js';
import { createRenderBudget } from '../../room/render-budget.js';
import { attachSlotCustomHandle, slotHandleStyle } from '../../room/slot-custom-handles.js';
import { createCoinShower } from '../../room/coin-shower.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
import { REQUIRED, OPTIONAL, FACE_MATERIAL } from './nodes.js';
import { reelCellVisible, reelPaintStamp } from './reel-paint.js';
import { stripTransform } from './strip-transform.js';
import { kit as wobbleKit } from '../../shared/sound/kit.js';
import { settleCells, recoilCells, leverRebound, latchTravel } from './juice.js';
import { drawSymbol, disposeSpirals, kindOf, CELL } from './symbols.js';
import { fitText } from '../../shared/text/wrap.js';
import { PACE, reelStopMs, reelsMs, respinStopMs, respinMs } from './pace.js';
import { applyPalette } from './palette.js';
import { FEEL, ALMOST as FEEL_ALMOST, ATTRACT, FLOW, bezier, breath, shiverPx, chaseMs, paylineGlow, paylinePulses, wiggleCells, glyphGlow, hazeAt } from './feel.js';

export const FACES = { idle0_0: 3, hearts: 7, spirals: 8, melt: 9, jackpot: 10 };
const RISE_MS = 620, CAMERA_MS = 620, SINK_MS = 340, PAINT_MS = 100, { THUD_MS } = FEEL;
const LENS_DEG = 20;           // vertical field; a narrow screen backs the camera off to keep the width
const FIT_MARGIN = 1.04;        // breathing room around the marquee, reels and lever box
// THE CLOSE SEAT (owner, 2026-09-15: "bring the pov closer"). fit() measures the box on its nearest face, and the
// lever and EMI's shelf stand well in front of the reel glass, so a straight fit leaves a band of empty room down
// each side. A desk seat takes this much of that distance off: the reels, and the art on them, read big.
const PLAY_CLOSE = 0.93;
// A phone crops INTO its fit: the owner's reference close-ups cut the shelf and the lever ball at the edges
// rather than leaving room around them (owner, 2026-09-16, one shot per orientation).
const LAND_CLOSE = 0.95, PORT_CLOSE = 1.02;
// 0 aims dead at the reel glass, 1 at the middle of the whole span: half way keeps the glass near the centre
// and still leaves the lever its room on the right (owner, 2026-09-16: "keep a little space on the right").
const GLASS_AIM = 0.16;
// EMI'S SHELF: her perch beside the reel window while the player is seated, answering the lever on the right.
// Cabinet space, the glb's own units: outboard of the side panel, the top surface level with the reels.
/* THE T moved her (owner, 2026-09-16). The arms now reach x +-0.673 and their underside is at y 0.978, so
 * the plank goes OUTBOARD of the arm's edge and DOWN into the pocket the overhang makes: she stands beside
 * the stem, under the left arm, with her head just clear of it. This is the sketch, and it is also why the
 * reels could take the width - she is no longer competing with them for it. */
const SHELF = { x: -0.70, top: 0.78, z: 0.36, w: 0.17, d: 0.16, lip: 0.03 };
/* UPRIGHT the plank comes IN off that line. A tall frame is bought by the width, and the shelf at -0.66 is the
 * widest thing in it: every pixel it reaches outboard is a pixel of reel. Pulled in and forward she still stands
 * beside the glass, on the same plank, and the reels come up by about a fifth (owner, 2026-09-16). -0.52 was a
 * step too far: at that distance she stood ON the cabinet's left panel rather than beside it, so -0.58 is where
 * the plank clears the corner and she reads against the room (owner: "move emi slightly to the left"). */
const SHELF_TALL = { x: -0.66, z: 0.44 };
const PERCH_SCALE = 0.85, PERCH_HOP = 0.11;   // a smaller EMI on a small shelf, and the arc of her jump across
const PULL_MAX = 0.5, PULL_COMMIT = 0.55;
const LEAN = 0.22, BREATH_RAD = 0.03;   // Law VIII lean into a press; THE BREATH's reach at rest
const PALETTES = {
  idle: [0xd90068, 0x006acb, 0xff9c00, 0x6220bd, 0x00a97c], spin: [0xff126e, 0x1567ff, 0xffb000, 0x00ccac],
  pull: [0x7920c4, 0xff197c], freeze: [0x0049a8, 0x00c7f2, 0x74f4ff], win: [0xe37100, 0xffd32b, 0xff4b24],
  big: [0xff4b24, 0xffd32b, 0xff126e, 0xffb000], jackpot: [0xffbd00, 0xffe27a, 0xffa000, 0xfff1b8],
  melt: [0x351369, 0xa32d99, 0x6230a0], free: [0x006ca0, 0x00dda8, 0x8726e5],
  tease: [0x3d1550, 0x6a2178, 0x2a1040, 0x59206b], tease_gold: [0xffbd00, 0x8a5a00, 0xffe27a, 0xa06c00],
};
const PARTY_MS = [0, 700, 900, 1000, 1400];   // per tier: how long the cabinet celebrates (THE BREATH waits it out)
const TEASE_DIM = 0.62;   // A1: the cabinet drops a notch while reel 3 holds (never under Calm or reduced motion)

const clamp = x => Math.min(1, Math.max(0, x)), ease = x => 1 - (1 - clamp(x)) ** 3;
const TAU = Math.PI * 2, ATTRACT_SPEED = [1, 0.86, 1.13];   // A4: the three drums drift at slightly different speeds
const wrapRad = a => { const x = ((a % TAU) + TAU) % TAU; return x > Math.PI ? x - TAU : x; };   // ...so home is never half a turn away
/** 0..1 of a reel's travel at `dt`: spin up, blur at a steady speed, decelerate into the stop. */
function reelTravel(dt, dur, up = 180, down = Math.min(PACE.DECEL_MS, dur * 0.5)) {
  const v = 1 / (dur - up / 2 - (2 * down) / 3);
  if (dt <= up) return v * dt * dt / (2 * up);
  return dt <= dur - down ? v * (dt - up / 2) : v * (dur - down - up / 2) + (v * down / 3) * (1 - (1 - clamp((dt - dur + down) / down)) ** 3);
}
/** The drum's speed at `dt`: the travel curve's own slope over its cruise slope, 1 at full blur and 0
 *  stopped. A1's stretched third reel comes out of this on its own, because the stretch is in the curve. */
function reelSpeed(dt, dur, up = 180, down = Math.min(PACE.DECEL_MS, dur * 0.5)) {
  const step = 16, cruise = step / (dur - up / 2 - (2 * down) / 3);
  if (!(cruise > 0)) return 0;
  return clamp((reelTravel(Math.min(dt + step, dur), dur, up, down) - reelTravel(Math.max(0, Math.min(dt, dur)), dur, up, down)) / cruise);
}
const reveal = x => bezier(FEEL.REVEAL_EASE, x), thud = x => bezier(FEEL.THUD_EASE, x);
const asset = p => new URL(p, import.meta.url).href;

function makeCanvas(w, h) { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; }
function canvasTexture(c) {
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace; t.flipY = false; t.generateMipmaps = false;
  t.minFilter = t.magFilter = THREE.LinearFilter;
  return t;
}

/* THE MARQUEE BOARD: the inset panel is the cabinet's message board. It carries the cabinet name at rest and
 * the play's own status and result lines while a spin runs (station.js drives it through api.marquee). A line
 * too long for the panel wraps onto 2 or 3 lines at the largest font that still fits (shared/text/wrap.js),
 * never a squeezed single line. MARQUEE_HOLD_MS after the last message the name comes back on its own restrike. */
export const MARQUEE_HOLD_MS = 4000;
const MARQUEE_FLICKER_MS = 260;   // the neon restrike on a change: a brief emissive stutter, then the rest level
const MARQUEE_LIT = 0.45;
const displayFont = (px) => `600 ${px}px Segoe UI, Arial, sans-serif`;

function paintDisplay(ctx, w, h, marquee, text, aspect=1) {
  const g = ctx.createLinearGradient(0, 0, w, h);
  g.addColorStop(0, '#180d27'); g.addColorStop(0.5, '#432040'); g.addColorStop(1, '#180d27');
  ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
  ctx.lineWidth = 1.5; ctx.strokeStyle = '#bf85ac'; ctx.strokeRect(9, 9, w - 18, h - 18);
  ctx.strokeStyle = '#744366'; ctx.strokeRect(15, 15, w - 30, h - 30);
  ctx.save();ctx.translate(w/2,h/2);ctx.scale(1,aspect);ctx.translate(-w/2,-h/2);
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  const lh = 1.06, measure = (str, size) => { ctx.font = displayFont(size); return ctx.measureText(str).width; };
  const fit = fitText(text, { measure, width: w * 0.82, height: h * (marquee ? 0.78 : 0.74),
    maxLines: marquee ? 3 : 2, min: marquee ? 17 : 15, max: marquee ? 57 : 44, lineHeight: lh });
  ctx.font = displayFont(fit.size);
  ctx.shadowColor = '#ff75c8'; ctx.shadowBlur = marquee ? 12 : 4; ctx.fillStyle = '#ffe3f2';
  const step = fit.size * lh, top = h / 2 - (fit.lines.length - 1) * step / 2;
  for (let i = 0; i < fit.lines.length; i++) ctx.fillText(fit.lines[i], w / 2, top + i * step, w * 0.84);
  ctx.shadowBlur = 0;ctx.restore();
}

/**
 * @param {{canvas:HTMLCanvasElement, reduced:boolean, stillFx?:()=>boolean, hint?:HTMLElement, palette?:object,
 *          canPull:()=>boolean, onLever:(info?:{sounded?:boolean})=>void, onPull?:()=>void,
 *          onFreeze:(i:number)=>void, onReelStop?:(i:number)=>void,
 *          onReelSpeed?:(i:number, speed:number)=>void}} o
 * @returns {Promise<{missing:string[], dispose:()=>void} | object>}
 */
export async function createScene(o) {
  const { reduced } = o;
  const shared = o.stage || null, canvas = shared ? shared.canvas : o.canvas;
  let releaseView = null, rig = null, coinShower = null, retainReels = null, apronTicker = null, rimLighting = null, releaseEmiClick = null;
  const extras = [], borrowed = new Map(), roomVisibility=new Map();
  const initialStretch=shared?.fixture.userData.slotStretch||1,initialStretchX=shared?.fixture.userData.slotStretchX||1;
  if(shared)for(const n of shared.scene.children)if(n!==shared.fixture&&!n.isLight){roomVisibility.set(n,n.visible);n.visible=false;}
  if (shared) shared.fixture.children[0].traverse(n => borrowed.set(n, { material:n.material, geometry:n.geometry, position:n.position.clone(), rotation:n.rotation.clone(), scale:n.scale.clone(), visible:n.visible }));
  // Law VI: reduced motion, Calm and Motion off all settle the cosmetic travel (station.js stillFx).
  const stillFx = typeof o.stillFx === 'function' ? o.stillFx : () => reduced;
  const budget = createRenderBudget(navigator, devicePixelRatio);
  let lastDraw = -Infinity;
  const renderer = shared ? shared.renderer : new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: 'default' });
  const scene = shared ? shared.scene : new THREE.Scene();
  const camera = shared ? shared.camera : new THREE.PerspectiveCamera(LENS_DEG, 16 / 9, 0.015, 60);
  if (!shared) {
    renderer.setPixelRatio(budget.dpr(canvas.clientWidth, canvas.clientHeight));
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.NeutralToneMapping; renderer.toneMappingExposure = 1.8;
    scene.add(new THREE.HemisphereLight(0xe3bdf9, 0x40213e, 2));
    for (const [color, intensity, pos] of [[0xffd9ed, 3, [-3, 5, 4]], [0xb69cf4, 2, [3, 2, 2]], [0xff83c9, 3, [1, 4, -3]]]) {
      const l = new THREE.DirectionalLight(color, intensity); l.position.set(...pos); scene.add(l);
    }
  }
  const owned = [];            // textures and materials made here, freed in dispose()
  let raf = 0, disposed = false, settle = null;
  function dispose() {
    if (disposed) return;
    disposed = true;
    cancelAnimationFrame(raf);
    if (settle) settle();         // a rise, sink or spin cut short by dispose still resolves its promise
    canvas.removeEventListener('pointerdown', onDown); canvas.removeEventListener('pointermove', onMove);
    canvas.removeEventListener('pointerup', onUp); canvas.removeEventListener('pointercancel', cancelPull);
    retainReels?.(); releaseEmiClick?.();
    for(const [n,visible] of roomVisibility)n.visible=visible;
    customHandle?.dispose();
    disposeSpirals();            // the reel spirals' Loom tiles, and the page's field context with them
    coinShower?.dispose();
    apronTicker?.dispose();
    rimLighting?.dispose();
    if (shared) {
      const oldMaterials = new Set([...borrowed.values()].flatMap(v => [].concat(v.material || [])));
      const oldGeometry = new Set([...borrowed.values()].map(v => v.geometry));
      const oldTextures = new Set([...oldMaterials].flatMap(m => Object.values(m).filter(v => v?.isTexture)));
      const disposeNode = n => {
        if (n.geometry && !oldGeometry.has(n.geometry)) n.geometry.dispose();
        for (const m of [].concat(n.material || [])) if (!oldMaterials.has(m)) {
          for (const t of Object.values(m)) if (t?.isTexture && !oldTextures.has(t)) t.dispose();
          m.dispose();
        }
      };
      const added = [];
      rig?.traverse(n => { disposeNode(n); if (!borrowed.has(n)) added.push(n); });
      for (const n of added) n.removeFromParent();
      for (const n of extras) { n.traverse(disposeNode); n.removeFromParent(); }
      for (const [n,v] of borrowed) { n.material=v.material; n.geometry=v.geometry; n.position.copy(v.position); n.rotation.copy(v.rotation); n.scale.copy(v.scale); n.visible=v.visible; }
      rig?.updateMatrixWorld(true);
      shared.fixture.userData.slotPlaying = false;
      releaseView?.();
    } else {
      scene.traverse(n => {
        if (n.geometry) n.geometry.dispose();
        for (const m of [].concat(n.material || [])) { for (const k of Object.keys(m)) if (m[k]?.isTexture) m[k].dispose(); m.dispose(); }
      });
      renderer.dispose(); renderer.forceContextLoss();
    }
    owned.forEach(x => x.dispose());
  }

  let customHandle = null, returning=false;
  let gltf, atlas = null;
  try {
    [gltf, atlas] = await Promise.all([
      shared ? Promise.resolve({ scene:shared.fixture.children[0] }) : new GLTFLoader().loadAsync(asset('./assets/slot.glb')),
      new THREE.TextureLoader().loadAsync(asset('./assets/emi-faces-slot.png')).catch(() => null),
    ]);
  } catch (e) { dispose(); throw e; }
  rig = gltf.scene;
  coinShower = createCoinShower(rig);
  let coinTick = performance.now();
  if (!shared) scene.add(rig);
  else shared.fixture.userData.slotPlaying = true;
  const recoloured = applyPalette(rig, o.palette);   // a room variant's cabinet colours, before any other swap
  owned.push(...recoloured);
  const get = name => rig.getObjectByName(name) || null;
  const missing = REQUIRED.filter(n => !get(n));
  if (missing.length) return { missing, dispose };
  const absent = OPTIONAL.filter(n => !get(n));
  if (absent.length) console.warn(`[slot] glb lacks optional nodes, degrading: ${absent.join(', ')}`);

  if (!shared) customHandle = await attachSlotCustomHandle({rig,loader:new GLTFLoader().setMeshoptDecoder(MeshoptDecoder),base:asset('../../room/assets/'),style:slotHandleStyle(o.variant)}).catch(()=>null);
  const cabinet = get('cabinet'), lever = get('lever'), rigRest = rig.position.clone();
  const reels = [1, 2, 3].map(i => get(`reel_${i}`)), restX = reels.map(() => 0), reelAngles = [0,0,0];
  const freezers = [1, 2, 3].map(i => get(`freeze_${i}`));
  freezers.forEach(f => f && f.material && (f.material = f.material.clone(), f.userData.restY = f.position.y));
  const bulbs = [];
  rig.traverse(n => { if (n.isMesh && /^lights_chase_\d+$/.test(n.name)) bulbs.push(n); });
  bulbs.sort((a, b) => a.name.localeCompare(b.name));
  const bulbScales = bulbs.map(b => b.scale.clone().multiplyScalar(1.3));
  // Keep the authored perimeter, with 30% larger lamps in the seated view.
  // Rim lighting measures their world scale, so its existing aura grows with them.
  bulbs.forEach((b, i) => {
    b.material = new THREE.MeshPhysicalMaterial({ color: PALETTES.idle[i % 5], emissive: PALETTES.idle[i % 5],
      emissiveIntensity: .6, roughness: .24, metalness: .08, clearcoat: 1, clearcoatRoughness: .14 });
  });
  rimLighting = createRimLighting(scene, bulbs, camera, renderer);
  const glass = get('reel_window');
  if (glass && glass.material) Object.assign(glass.material, { transparent: true, opacity: 0.035, depthWrite: false });
  // THE MARQUEE's heat lives on marquee_glow (its own clone: neon_pink is shared with the piping).
  const glowNode = get('marquee_glow'), glowMat = glowNode && glowNode.isMesh && glowNode.material && glowNode.material.emissive
    ? (glowNode.material = glowNode.material.clone()) : null;
  const glowRest = glowMat && { color: glowMat.emissive.clone(), intensity: glowMat.emissiveIntensity };
  if (glowMat) owned.push(glowMat);
  const tray = get('payout_tray'), trayRest = tray && tray.scale.clone();

  // EMI face: atlas cell with a half-texel inset (FACES.md), Nearest, no mips.
  let faceMesh = null, faceName = 'idle0_0';
  rig.traverse(n => { if (n.isMesh && n.material && n.material.name === FACE_MATERIAL) faceMesh = n; });
  if (atlas && faceMesh) {
    Object.assign(atlas, { flipY: false, colorSpace: THREE.SRGBColorSpace, minFilter: THREE.NearestFilter,
      magFilter: THREE.NearestFilter, generateMipmaps: false, wrapS: THREE.ClampToEdgeWrapping, wrapT: THREE.ClampToEdgeWrapping });
    atlas.repeat.set(151 / 1672, 136 / 137);
    faceMesh.material = new THREE.MeshBasicMaterial({ map: atlas });
  } else if (!faceMesh) console.warn(`[slot] no ${FACE_MATERIAL} material, EMI face stays as modelled`);
  owned.push(...(atlas ? [atlas] : []));

  const screens = {};
  for (const name of ['screen_jackpot', 'screen_status', 'marquee']) {
    const mesh = get(name);
    if (!mesh || !mesh.isMesh) continue;
    const c = makeCanvas(name === 'screen_jackpot' ? 512 : 1024, 128), t = canvasTexture(c);
    mesh.material = new THREE.MeshStandardMaterial({ map: t, emissiveMap: t, emissive: 0xffffff, emissiveIntensity: 0.45, roughness: 0.6 });
    owned.push(t);
    screens[name] = { c, t, mesh, text: null };
  }

  const crown = screens.screen_jackpot;
  const crownDisplay = crown ? createCrownDisplay(crown.c, () => { crown.t.needsUpdate = true; }) : null;
  const crownSize = new THREE.Vector3(), crownScale = new THREE.Vector3();
  if (crown) { crown.mesh.geometry.computeBoundingBox(); crown.mesh.geometry.boundingBox.getSize(crownSize); }

  const apronDisplay = get('apron_display');
  const apronScale = new THREE.Vector3(), apronSize = new THREE.Vector3();
  if (apronDisplay?.isMesh) {
    apronDisplay.geometry.computeBoundingBox();
    apronDisplay.geometry.boundingBox.getSize(apronSize);
    let texture;
    apronTicker = createApronTicker({ reduced: stillFx(), onDirty: () => { if (texture) texture.needsUpdate = true; } });
    texture = canvasTexture(apronTicker.canvas);
    apronDisplay.material = new THREE.MeshBasicMaterial({ map: texture, toneMapped: false });
    owned.push(texture);
    apronTicker.update(performance.now(), stillFx());
  }

  const spinFace=get('spin_button_face');
  if(shared && spinFace)spinFace.visible=false;
  if(!shared && spinFace?.isMesh){
    const c=makeCanvas(512,160),g=c.getContext('2d');
    g.fillStyle='#f2b5dc';g.fillRect(0,0,512,160);g.fillStyle='#32143e';g.textAlign='center';g.textBaseline='middle';g.font='700 70px Segoe UI, sans-serif';g.fillText(o.spinLabel || 'Spin',256,80,470);
    const t=canvasTexture(c);owned.push(t);spinFace.material=new THREE.MeshBasicMaterial({map:t});
  }

  // THE MARQUEE BOARD: the live message (null = the cabinet name) and the frame it was posted on.
  let marqueeMsg = null, marqueeAt = -Infinity;

  // Reels: one canvas per drum, N cells along U, painted in strip order.
  let strips = [[], [], []], look = { reduced }, lastPaint = -Infinity;
  // A2 THE ALMOST: the off-by-one cell on reel 3 going gold, {r, j, at}. Set by almost(), cleared when it ends.
  let ghost = null;
  // THE GLYPH HIT: when each reel's landed cell started glowing (the landing frame) and its offset in the plan.
  const hitAt = [-Infinity, -Infinity, -Infinity], hitStart = [0, 0, 0];
  let hitDirty = false;
  const hitGlow = (r, t) => glyphGlow(t - hitAt[r], hitStart[r], reduced);
  const hitting = t => hitAt.some(a => t - a >= 0 && t - a < FLOW.HIGHLIGHT_MS);
  const stopsNow = [0, 0, 0];
  /** The gold on that cell: in over the tell, then ONE snap back. Reduced motion takes a single tint (Law VI). */
  function ghostAmt(t) {
    if (!ghost) return 0;
    const age = t - ghost.at, rise = FEEL_ALMOST.TELL_MS - FEEL_ALMOST.SNAP_MS;
    if (!(age >= 0) || age >= FEEL_ALMOST.TELL_MS) return 0;
    if (reduced) return age < FEEL_ALMOST.SNAP_MS ? 1 : 0;
    return age < rise ? ease(age / rise) : 1 - clamp((age - rise) / FEEL_ALMOST.SNAP_MS);
  }
  const reelCanvas = [], reelTex = [];
  // Canvas X follows the strip travel; canvas Y spans the physical reel width.
  // The authored shallow arc exposes 1.4 cells, with UV travel driven by the same spin angle.
  let cell={...CELL,hw:CELL.hw*initialStretchX/initialStretch}, CW=cell.hh*2, CH=cell.hw*2;
  const angle = (k, n) => ((k + 0.5) / n - 0.5) * Math.PI * 2;
  let reelMood = 'idle', reelMoodAt = 0;
  const paintedCells = [[], [], []];
  function paint(t, force = false) {
    for (let r = 0; r < 3; r++) {
      const n = strips[r].length || 1, c = reelCanvas[r], ctx = c.getContext('2d');
      let dirty = false;
      for (let j = 0; j < strips[r].length; j++) {
        const hit = j === stopsNow[r] ? hitGlow(r, t) : 0;
        const gh = ghost && ghost.r === r && ghost.j === j ? ghostAmt(t) : 0;
        const kind = kindOf(strips[r][j]).kind;
        const stamp = reelPaintStamp(kind, t, reduced || stillFx(), hit, gh, reelMood);
        // Keep static pixels; only the payline and nearby slivers need live art at rest.
        if (!force && (!reelCellVisible(j, n, reelAngles[r], !!spin) || paintedCells[r][j] === stamp)) continue;
        dirty = true; paintedCells[r][j] = stamp;
        ctx.clearRect(j * CW, 0, CW, CH);
        ctx.save(); ctx.beginPath(); ctx.rect(j * CW, 0, CW, CH); ctx.clip(); ctx.translate((j + 0.5) * CW, CH / 2); ctx.rotate(-Math.PI / 2); ctx.scale(1, -1);
        // THE GLYPH HIT (shared/hypno/callout.js timings): the landed cell pops 6% inside its own cell and takes a
        // rim, reel order, HIGHLIGHT_GAP_MS apart. Reduced motion takes the lit rim and no pop (Law VI).
        if (hit > 0 && !reduced && !stillFx()) { ctx.beginPath(); ctx.rect(-cell.hw, -cell.hh, CH, CW); ctx.clip(); ctx.scale(1 + 0.06 * hit, 1 + 0.06 * hit); }
        // One bad drawable (a broken or tainted GIF) paints the fallback tile, never the whole reel.
        try { ctx.save(); drawSymbol(ctx, strips[r][j], t, {...look,reduced:reduced||stillFx(),faceMood:reelMood,faceAge:t-reelMoodAt},cell); } catch { ctx.restore(); ctx.save(); drawSymbol(ctx, strips[r][j], t, { reduced: look.reduced, face: look.face },cell); }
        ctx.restore();
        const glaze = ctx.createLinearGradient(-cell.hw, 0, cell.hw, 0);
        glaze.addColorStop(0, '#07040f99'); glaze.addColorStop(0.12, '#ffffff08'); glaze.addColorStop(0.5, '#ffffff00');
        glaze.addColorStop(0.88, '#ffffff08'); glaze.addColorStop(1, '#07040f99');
        ctx.fillStyle = glaze; ctx.fillRect(-cell.hw, -cell.hh, CH, CW);
        ctx.strokeStyle = '#e8bbd526'; ctx.lineWidth = 1; ctx.strokeRect(-cell.hw + 12, -cell.hh + 12, CH - 24, CW - 24);
        if (hit > 0) { ctx.strokeStyle = `rgba(255,214,120,${(0.9 * hit).toFixed(3)})`; ctx.lineWidth = 12; ctx.strokeRect(-cell.hw + 12, -cell.hh + 12, CH - 24, CW - 24); }
        // A2: the cell one step off the payline ghosts gold. The reel window shows about half of each
        // neighbour (drum r 0.43, 13 cells, window 0.39 tall), so the tell reads without moving a stop.
        if (gh > 0) { ctx.fillStyle = `rgba(255,194,58,${(0.62 * gh).toFixed(3)})`; ctx.fillRect(-cell.hw, -cell.hh, CH, CW); }
        ctx.restore();
      }
      if (dirty) reelTex[r].needsUpdate = true;
    }
    lastPaint = t;
  }
  function setStrips(next) {
    strips = [0, 1, 2].map(r => (next && Array.isArray(next[r]) ? next[r] : []));
    for (let r = 0; r < 3; r++) {
      if (reelTex[r]) { reelTex[r].dispose(); reels[r].material.dispose(); }
      paintedCells[r] = [];
      reelCanvas[r] = makeCanvas(Math.max(1, strips[r].length) * CW, CH);
      reelTex[r] = canvasTexture(reelCanvas[r]);
      reelTex[r].wrapS = THREE.RepeatWrapping;
      reelTex[r].repeat.x = stripTransform(0, strips[r].length).repeat;
      reels[r].material = new THREE.MeshBasicMaterial({ map: reelTex[r] });
    }
    owned.push(...reelTex);
    paint(performance.now(), true);
  }
  setStrips([]);
  // Keep the final dealt picture on the very same room mesh after standing up.
  // The copied canvas owns no media element and replaces only this cabinet's map image.
  if(shared) retainReels=()=>{
    for(let i=0;i<3;i++){
      const original=borrowed.get(reels[i])?.material, map=original?.map, source=reelCanvas[i];
      // Nothing dealt means nothing to retain. A refused sit-down (setStrips([]) is still in force)
      // used to hand the ROOM's 1664x128 strip a repeat of 13 and a blank 256x304 image, and since
      // .image cannot resize an uploaded texture the old spiral pixels stayed on the GPU with 13x the
      // UV span: the cabinet you stood up from smeared into horizontal bands for the rest of the session.
      if(!map || !source || !strips[i].length)continue;
      const copy=makeCanvas(source.width,source.height);copy.getContext('2d').drawImage(source,0,0);
      map.image=copy;map.wrapS=THREE.RepeatWrapping;
      const uv=stripTransform(reelAngles[i],strips[i].length);map.repeat.x=uv.repeat;map.offset.x=uv.offset;
      map.needsUpdate=true;
    }
  };
  function setStops(stops) {
    stops.forEach((k, r) => { stopsNow[r] = k; reelAngles[r] = restX[r] + angle(k, strips[r].length || 13); });
  }

  // Framing: from cam_seat/cam_target and live bounds, at the rest pose. The marquee is in the play box
  // and EMI's face so the cabinet's name and her glance read above the reels at every aspect (in-room tidy, lane F1).
  let poses = null;
  /** A phone on its side (station.css, the same query and the same numbers). The chips and Odds take a column
   *  on the left, Spin and the face keep the right corners, and Freeze runs along the BOTTOM (owner,
   *  2026-09-16) rather than stacking down the left, which is why the two side columns are now EQUAL: the
   *  view offset below is (right - left) / 2, so matching them is what puts the cabinet in the middle of the
   *  screen instead of shouldering it into whatever space the left column left over.
   *
   *  THE BANDS ARE MARGINS NOW, NOT COLUMNS (owner, 2026-09-16: "push the pov closer on landscape"). A band is
   *  screen the fit is forbidden to use, so a 142 px column down each side and a 66 px sill was paying for the
   *  chrome twice - once in pixels and again in camera distance. The chips are small and opaque and they read
   *  perfectly well OVER the cabinet, so the only thing still bought is the Freeze sill along the bottom. */
  const sideband = (w, h) => (h <= 500 && w > h ? { left: 16, right: 16, top: 8, bottom: 54 } : null);
  function frame(aspect, w = 16, h = 9) {
    let closeSeat = false;   // the seated pose: the one EMI takes her shelf for (the overview keeps her topper)
    standShelf(aspect < 0.8);   // where the plank is has to be settled BEFORE the box that measures it
    const y = rig.position.y; rig.position.y = 0; rig.updateMatrixWorld(true);
    const seat = get('cam_seat').getWorldPosition(new THREE.Vector3()), target = get('cam_target').getWorldPosition(new THREE.Vector3());
    const dir = seat.sub(target); if (dir.lengthSq() < 1e-8) dir.set(0, 0, 1); dir.normalize();
    const playBox = new THREE.Box3();
    (glass ? [glass] : reels).forEach(n => playBox.expandByObject(n));
    playBox.expandByObject(lever);
    for (const n of [get('marquee'), glowNode]) if (n) playBox.expandByObject(n);
    // EMI's shelf, and EMI standing on it, are the left of the seated frame the way the lever is the right. The
    // perch is used, never wherever she happens to be standing this frame, so the framing never rides her hop.
    // Without a shelf (a glb short of emi_stage) she keeps her topper, and the frame keeps reaching up for it.
    /** Put EMI in a box, in the pose that frame will hold her in. Neither branch reads where she happens to be
     *  standing this instant - the shelf is measured at its perch, the topper at her stage - so a resize that
     *  lands mid-hop still measures the pose she is about to settle into. */
    const withEmi = (box, onShelf) => {
      if (onShelf && shelf && perch) {
        box.expandByObject(shelf);
        box.union(new THREE.Box3().setFromCenterAndSize(emiHost.localToWorld(perch.clone().setY(perch.y + emiSpan.y / 2)), emiSpan));
      } else if (stage && emiSpan) {
        const plinth = new THREE.Box3().setFromObject(stage), mid = plinth.getCenter(new THREE.Vector3());
        const full = emiSpan.clone().divideScalar(PERCH_SCALE);   // on her topper she is her whole size
        box.union(plinth);
        box.union(new THREE.Box3().setFromCenterAndSize(new THREE.Vector3(mid.x, plinth.max.y + full.y / 2, mid.z), full));
      } else if (emi) box.expandByObject(emi);
      else if (faceMesh) box.expandByObject(faceMesh);
      return box;
    };
    withEmi(playBox, true);
    const whole = new THREE.Box3().setFromObject(cabinet);
    // `span` is the fraction of the canvas the box may fill on each axis (1 = all of it), the way room/seat-camera.js
    // leaves the station chrome its bands: a smaller span backs the camera off so the box fits inside what is left.
    const fit = (box, d, aimAt, span = { x: 1, y: 1 }) => {
      const right = new THREE.Vector3().crossVectors(new THREE.Vector3(0, 1, 0), d).normalize(), up = new THREE.Vector3().crossVectors(d, right);
      const c = box.getCenter(new THREE.Vector3());
      if (aimAt) c.addScaledVector(up, (aimAt.clone().sub(c).dot(up)) * 0.15);   // a nod toward the authored aim height, keep the box
      const tan = Math.tan(THREE.MathUtils.degToRad(LENS_DEG) / 2);
      let hw = 0, hh = 0, depth = 0;
      for (const x of [box.min.x, box.max.x]) for (const yy of [box.min.y, box.max.y]) for (const z of [box.min.z, box.max.z]) {
        const p = new THREE.Vector3(x, yy, z).sub(c);
        hw = Math.max(hw, Math.abs(p.dot(right))); hh = Math.max(hh, Math.abs(p.dot(up))); depth = Math.max(depth, p.dot(d));
      }
      const dist = Math.max(hh / (tan * span.y), hw / (tan * aspect * span.x)) * FIT_MARGIN + depth;
      return { pos: c.clone().addScaledVector(d, dist), look: c, dist, right };
    };
    const side = new THREE.Vector3().crossVectors(new THREE.Vector3(0, 1, 0), dir).normalize();
    const quarter = dir.clone().addScaledVector(side, 0.75).add(new THREE.Vector3(0, 0.45, 0)).normalize();
    rig.position.y = y; rig.updateMatrixWorld(true);
    const play = fit(playBox, dir, target);
    if (aspect < 0.8) {
      // UPRIGHT, to the owner's own reference frame (2026-09-16): the topper on top, the marquee under it, the
      // reels big, the lever and a little room to its right - and EMI ON THE SIDE SHELF, not on her topper.
      // She took the topper here until now because the shelf is outboard and a tall frame has no width to sell;
      // the owner would rather buy it, so the shelf is in the box and `closeSeat` sends her out to it.
      // THE T CHANGED WHAT UPRIGHT CAN AFFORD (owner, 2026-09-16). The arms span 1.28 m of glass and the two
      // attendants take the span to 1.62; a 390 px frame fitting all of that put the whole cabinet - topper
      // stage, tray and all - on the screen and left the reels smaller than they were before the rebuild. So
      // upright fits THE GLASS and nothing else, and the shelf and the lever ball are cropped at the edges,
      // which is the crop the owner's own reference close-ups hold. Sideways still buys them both.
      const reelBox = new THREE.Box3();
      (glass ? [glass] : reels).forEach(n => reelBox.expandByObject(n));
      closeSeat = true;
      const close = fit(reelBox, dir);
      play.look.copy(close.look);
      play.dist = close.dist * PORT_CLOSE;
      play.pos.copy(play.look).addScaledVector(dir, play.dist);
    }
    const band = sideband(w, h);
    if (band) {
      // A view offset (resize) aims the band's centre, not the canvas centre, at the reels: the seat-camera mechanism.
      // SIDEWAYS is the desk's problem exactly: a short frame cuts her topper off the top. So a phone on its
      // side takes the shelf too, and she reads level with the reels between the button column and the glass.
      // The box is EMI's shelf, the glass and the lever - her at one edge and the handle at the other, which is
      // the span the owner's reference close-up holds. It is the BANDS that got the camera in, not a smaller box:
      // dropping the lever out of the fit did bring the reels up, but it also swung the box's centre over to
      // EMI's side and put the glass off to the right, because the fit aims at the box, not at the glass.
      const reelBox = new THREE.Box3(), glassBox = new THREE.Box3();
      (glass ? [glass] : reels).forEach(n => glassBox.expandByObject(n));
      reelBox.copy(glassBox).expandByObject(lever);
      withEmi(reelBox, true);
      closeSeat = true;
      const close = fit(reelBox, dir, null, { x: (w - band.left - band.right) / w, y: (h - band.top - band.bottom) / h });
      // THE T made the aim matter here too (owner, 2026-09-16). The arms are high and the two attendants are
      // low, so the box's own centre now sits down in the BODY: fitting to it and aiming at it pushed the reels
      // to the top of the screen and gave the control deck the bottom half. The box still buys the distance;
      // the glass buys the aim, exactly as upright has done since the reference frame.
      play.look.copy(glassBox.getCenter(new THREE.Vector3()).lerp(close.look, GLASS_AIM));
      play.dist = close.dist * LAND_CLOSE;
      play.pos.copy(play.look).addScaledVector(dir, play.dist);
    } else if (aspect >= 0.8) {
      // A desk seat: closer than the box fit, so the cabinet fills the frame (PLAY_CLOSE). A phone keeps its own
      // fit above, where the whole width has to be bought back anyway.
      play.dist *= PLAY_CLOSE;
      play.pos.copy(play.look).addScaledVector(dir, play.dist);
      closeSeat = true;
    }
    return { play, arrive: fit(whole, quarter), drop: (whole.max.y - whole.min.y) * 1.6, band, closeSeat };
  }
  const aim = (pos, lookAt) => { camera.position.copy(pos); camera.lookAt(lookAt); };
  function resize() {
    const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1;
    if (shared) {
      const stretch=shared.fixture.userData.slotStretch||1,nextWidth=CELL.hw*(shared.fixture.userData.slotStretchX||1)/stretch;
      if(Math.abs(nextWidth-cell.hw)>.01){cell={...CELL,hw:nextWidth};CW=cell.hh*2;CH=cell.hw*2;setStrips(strips);}
      const look = get('cam_target').getWorldPosition(new THREE.Vector3());
      const forward = new THREE.Vector3(0,0,1).transformDirection(rig.matrixWorld);
      poses = { play:{pos:camera.position.clone(),look,dist:camera.position.distanceTo(look),right:new THREE.Vector3().crossVectors(new THREE.Vector3(0,1,0),forward)}, band:null, closeSeat:false, drop:0 };
      return;
    }
    renderer.setPixelRatio(budget.dpr(w, h));
    renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix();
    poses = frame(camera.aspect, w, h);
    if (poses.band) camera.setViewOffset(w, h, (poses.band.right - poses.band.left) / 2, (poses.band.bottom - poses.band.top) / 2, w, h);
    else camera.clearViewOffset();
    if (phase === 'play') aim(poses.play.pos, poses.play.look);
  }

  // Timeline state.
  let phase = 'hidden', tl = null, spin = null, pull = null, pullBack = null, hold = null, mood = 'idle', moodAt = 0;
  settle = () => { const t = tl, s = spin; tl = null; spin = null; if (t && t.done) t.done(); if (s && s.resolve) s.resolve(); };
  let meltShakeAt = -Infinity;
  let revealAt = -Infinity, revealGain = 0, lean = null, party = null, shiverAt = -Infinity, trayAt = -Infinity, melted = false;
  let teasing = false;   // A1: reel 3 is alone and holding (the marquee's tease mood, the lights a notch down)
  // A4 attract and A5 the EMI land-wiggle: both live on the loop, so neither holds a timer of its own.
  let attract = null, attractOut = null;
  const wiggleAt = [-Infinity, -Infinity, -Infinity];
  // THE ATTRACT HAZE (feel.FLOW): one spiral plane behind the cabinet, breathing at 12% after 8 s idle.
  let hazeMesh = null, hazeOn = null, hazeOut = null, hazeTick = 0;
  function makeHaze() {
    const c = makeCanvas(512, 512), g = c.getContext('2d');
    g.strokeStyle = '#f49aca'; g.lineWidth = 16; g.lineCap = 'round'; g.beginPath();
    for (let k = 0; k < 400; k++) { const a = k * 0.115, rr = k * 0.62; if (k) g.lineTo(256 + Math.cos(a) * rr, 256 + Math.sin(a) * rr); else g.moveTo(256, 256); }
    g.stroke();
    const box = new THREE.Box3().setFromObject(rig), size = box.getSize(new THREE.Vector3()), centre = box.getCenter(new THREE.Vector3());
    const d = Math.max(size.x, size.y) * 2.4;
    const m = new THREE.Mesh(new THREE.PlaneGeometry(d, d), new THREE.MeshBasicMaterial({ map: canvasTexture(c), transparent: true, opacity: 0, depthWrite: false }));
    const dir = poses ? poses.play.look.clone().sub(poses.play.pos).normalize() : new THREE.Vector3(0, 0, -1);
    m.position.copy(centre).addScaledVector(dir, size.z * 0.5 + 0.25);
    m.lookAt(m.position.clone().sub(dir));
    m.renderOrder = -1; m.visible = false;
    scene.add(m); extras.push(m);
    return m;
  }
  const cellRad = i => TAU / (strips[i].length || 13);
  const restAngle = i => restX[i] + angle(stopsNow[i], strips[i].length || 13);
  const attractRad = (i, age) => (age / 1000) * ATTRACT.DRIFT_CELLS_PER_S * ATTRACT_SPEED[i] * cellRad(i);
  let heat = { from: 0, to: 0, at: -Infinity, gold: false };
  const pulse = [-Infinity, -Infinity, -Infinity], stopAt = [-Infinity, -Infinity, -Infinity];
  const sparks = [];
  const spawn = get('payout_spawn');
  if (spawn) {
    const geo = new THREE.OctahedronGeometry(0.012), mat = new THREE.MeshBasicMaterial({ color: 0xffd7a6 });
    for (let i = 0; i < 7; i++) { const m = new THREE.Mesh(geo, mat); m.visible = false; scene.add(m); extras.push(m); sparks.push(m); }
  }
  let gazeYaw = 0, gazePitch = 0, gazeAt = 0, gazeReel = -1;
  const gazePoint = new THREE.Vector3();
  const emi = get('emi_topper'), emiRest = emi && { p: emi.position.clone(), s: emi.scale.clone(), r: emi.rotation.clone() };
  const emiArms = ['shoulderL','shoulderR'].map(name => {const node=emi?.getObjectByName(name); return node?{node,rest:node.rotation.clone()}:null;});
  let emiClickAt = -Infinity;
  releaseEmiClick = shared?.onEmiClick?.(()=>{if(!stillFx())emiClickAt=performance.now();});
  const tmp = new THREE.Vector3(), nextColor = new THREE.Color(), GOLD = new THREE.Color(0xffc23a);

  /* EMI'S SHELF (owner, 2026-09-15). The close seat cuts her topper off the top of the frame, so while the player
   * is seated she hops down onto a plank on the cabinet's left, level with the reels, and the frame reads even:
   * EMI on the left, the lever on the right. The plank is a clone of her own stage (its geometry, its palette-
   * recoloured material, so a room variant paints it too) with a small lip, parented where she is parented so the
   * perch is simply a position in her own space. Nothing is asked of the glb (section 9.6). */
  const stage = get('emi_stage'), emiHost = emi && (emi.parent || rig);
  let shelf = null, perch = null, emiSpan = null;
  if (!shared && emi && stage && emiHost) {
    const span = new THREE.Box3().setFromObject(stage).getSize(new THREE.Vector3());
    emiSpan = new THREE.Box3().setFromObject(emi).getSize(new THREE.Vector3()).multiplyScalar(PERCH_SCALE);
    perch = new THREE.Vector3(SHELF.x, SHELF.top, SHELF.z);
    shelf = new THREE.Group();
    shelf.name = 'emi_shelf';
    shelf.position.copy(perch);
    const plank = stage.clone(true);
    plank.position.set(0, -span.y / 2, 0);         // the group's origin is the top surface: where her feet land
    plank.rotation.set(0, 0, 0);
    plank.scale.set(SHELF.w / Math.max(span.x, 1e-3), 1, SHELF.d / Math.max(span.z, 1e-3));
    shelf.add(plank);
    if (plank.isMesh && plank.material) {
      const lip = new THREE.Mesh(new THREE.BoxGeometry(SHELF.w, SHELF.lip, SHELF.lip), plank.material);
      lip.position.set(0, SHELF.lip / 2 - span.y / 2, (SHELF.d - SHELF.lip) / 2);
      shelf.add(lip);
    }
    emiHost.add(shelf);
  }
  /** Move the plank between its two stations (wide and tall). The perch is the same vector the hop and the fit
   *  both read, so moving it moves her, her shelf and the frame that measures them together. */
  function standShelf(tall) {
    if (!perch || !shelf) return;
    const x = tall ? SHELF_TALL.x : SHELF.x, z = tall ? SHELF_TALL.z : SHELF.z;
    if (perch.x === x && perch.z === z) return;
    perch.set(x, SHELF.top, z);
    shelf.position.copy(perch);
    shelf.updateMatrixWorld(true);
  }
  /** THE HOP: 0 on her topper, 1 on the shelf. She crosses while the camera settles into the seat and climbs back
   *  as the cabinet sinks. Law VI: reduced motion settles the travel (rise() goes straight to `play`), so k is 0
   *  or 1 there and the arc never runs. */
  function perchAt(t) {
    if (!perch || !poses || !poses.closeSeat) return 0;   // no shelf to take: she keeps her topper
    if (phase === 'play') return 1;
    if (tl && tl.kind === 'rise') return ease(clamp((t - tl.start - RISE_MS) / CAMERA_MS));
    if (tl && tl.kind === 'sink') return 1 - ease(clamp((t - tl.start) / SINK_MS));
    return 0;
  }

  function screen(name, text) {
    const s = screens[name];
    const aspect=1;
    if (!s || (s.text === text&&s.aspect===aspect)) return;
    s.text = text;s.aspect=aspect; paintDisplay(s.c.getContext('2d'), s.c.width, s.c.height, name === 'marquee', text,aspect); s.t.needsUpdate = true;
  }
  function setFace(name) {
    faceName = FACES[name] !== undefined ? name : 'idle0_0';
    if (atlas && faceMesh) atlas.offset.set((FACES[faceName] * 152 + 0.5) / 1672, 0.5 / 137);
    const m = { jackpot: 'jackpot', hearts: 'win', melt: 'melt', spirals: 'free' }[faceName] || 'idle';
    if (m !== mood) { mood = m; moodAt = performance.now(); }
  }
  /** THE GLOW on the marquee heat: in fast, out slow; reduced motion steps to the state. */
  function heatNow(t) {
    if (reduced) return heat.to;
    const q = clamp((t - heat.at) / (heat.to > heat.from ? FEEL.GLOW_IN_MS : FEEL.GLOW_OUT_MS));
    return heat.from + (heat.to - heat.from) * ease(q);
  }
  function heatTo(level, gold) { const t = performance.now(); heat = { from: heatNow(t), to: level, at: t, gold: !!gold }; }

  /* THE PAYLINE FRAME (playbook A6). The preview's landing frames sat at hard-coded coordinates and were not
   * ported; this one comes off live bounds instead, like the lever hint. The row is the reel window's own
   * spread, one drum cell tall (the chord of a 2PI/n cell on a drum of that radius), projected to client px,
   * and o.payline (a DOM frame) is moved onto it. No new material, no geometry, nothing in the glb. */
  let paylineAt = -Infinity, paylineHold = 0, paylinePulseN = 1;
  function paylineRect() {
    const box = new THREE.Box3();
    if (glass) box.expandByObject(glass); else reels.forEach(r => box.expandByObject(r));
    if (box.isEmpty()) return null;
    const drum = new THREE.Box3().setFromObject(reels[1]), n = strips[1].length || 13;
    const half = (drum.isEmpty() ? (box.max.y - box.min.y) : (drum.max.y - drum.min.y)) * Math.sin(Math.PI / n) / 2;
    const mid = (box.min.y + box.max.y) / 2, w = canvas.clientWidth || 1, h = canvas.clientHeight || 1, pts = [];
    for (const x of [box.min.x, box.max.x]) for (const y of [mid - half, mid + half]) for (const z of [box.min.z, box.max.z]) {
      const p = new THREE.Vector3(x, y, z).project(camera);
      pts.push([(p.x + 1) * w / 2, (1 - p.y) * h / 2]);
    }
    const left = Math.min(...pts.map(p => p[0])), right = Math.max(...pts.map(p => p[0]));
    const top = Math.min(...pts.map(p => p[1])), bottom = Math.max(...pts.map(p => p[1]));
    if (!(right > left && bottom > top)) return null;
    return { left, top, width: right - left, height: bottom - top };
  }

  /* B1 THE SPIRAL JAR (playbook Tier B, CONTRACT 10.16.A) USED TO STAND HERE, as a DOM tube on live
   * projected bounds at the cabinet's left edge. It is gone (owner, 2026-09-16: "that bar near emi is
   * horrible, remove it") - on a phone it stood exactly where EMI's shelf is, and two narrow things beside
   * one cabinet is one too many. The jar is not gone: the tape counts it, it spills, it pays, and the count
   * reads in the status line (station.js jarPart), which is all Brake 9 ever asked of it. `screenBox` stays
   * because the payline frame is projected the same way. */
  function screenBox(node) {
    if (!node) return null;
    const box = new THREE.Box3().setFromObject(node);
    if (box.isEmpty()) return null;
    const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1, pts = [];
    for (const x of [box.min.x, box.max.x]) for (const y of [box.min.y, box.max.y]) for (const z of [box.min.z, box.max.z]) {
      const p = new THREE.Vector3(x, y, z).project(camera);
      pts.push([(p.x + 1) * w / 2, (1 - p.y) * h / 2]);
    }
    const left = Math.min(...pts.map(q => q[0])), right = Math.max(...pts.map(q => q[0]));
    const top = Math.min(...pts.map(q => q[1])), bottom = Math.max(...pts.map(q => q[1]));
    return right > left && bottom > top ? { left, top, width: right - left, height: bottom - top } : null;
  }

  function settleSpin() {
    if (!spin) return;
    const s = spin; spin = null;
    setStops(s.stops);
    lever.rotation.x = 0;
    s.resolve();
  }

  function settleMechanical() {
    pulse.fill(-Infinity); shiverAt = -Infinity; pullBack = null;
    if (spin && !spin.motionSuppressed) {
      spin.motionSuppressed = true;
      spin.from = reelAngles.map((a, i) => a - restX[i]);
    }
  }

  function update(t) {
    const reduced = !!o.reduced || stillFx();
    if (reduced) settleMechanical();
    if (apronTicker) {
      apronDisplay.getWorldScale(apronScale);
      apronTicker.setAspect(apronSize.x * apronScale.x / Math.max(.001, apronSize.y * apronScale.y));
      apronTicker.update(t, reduced);
    }
    for(const n of roomVisibility.keys())n.visible=false;
    coinShower.update((t-coinTick)/1000,stillFx()); coinTick=t;
    if (tl) {
      const dt = t - tl.start;
      if (tl.kind === 'rise') {
        rig.position.y = -poses.drop * (1 - reveal(dt / RISE_MS));
        const k = ease((dt - RISE_MS) / CAMERA_MS);
        camera.position.lerpVectors(poses.arrive.pos, poses.play.pos, k);
        camera.lookAt(tmp.lerpVectors(poses.arrive.look, poses.play.look, k));
        if (dt >= RISE_MS + CAMERA_MS) { const done = tl.done; tl = null; phase = 'play'; rig.position.y = 0; aim(poses.play.pos, poses.play.look); done(); }
      } else if (tl.kind === 'sink') {
        rig.position.y = tl.fromY - (poses.drop + tl.fromY) * ease(dt / SINK_MS);
        if (dt >= SINK_MS) { const done = tl.done; tl = null; phase = 'hidden'; done(); }
      }
    }
    // A1 THE ANTICIPATION REEL: from reel 2's thud to reel 3's, with reel 3 alone and holding. The tape
    // already carries the outcome, so this window only delays it; nothing here can change what lands.
    // C1 (10.16.D): a solo re-spin has no reels 1 and 2 to wait for, so the whole travel is the tease window:
    // reel 3 alone, gold, for SPIN_MS and the full hold.
    teasing = !!spin && spin.teaseMs > 0 && spin.teaseDim && !reduced && (spin.solo
      ? t - spin.start < respinStopMs(PACE, spin.teaseMs)
      : t - spin.start >= reelStopMs(1) && t - spin.start < reelStopMs(2, PACE, spin.teaseMs));
    const partying = !!party && t < party.end, pr = partying ? party.r : null;
    const sculptureAge = spin ? t - spin.start - customSpinReturnMs(spin.leverFrom) : pullBack ? t - pullBack.start - LEVER_RETURN_MS : Infinity;
    if (spin) {
      const s = spin, dt = t - s.start, stillSpin = reduced || !!s.motionSuppressed;
      // The pull carries on from wherever the Law VIII lean (or the drag) left the lever.
      const wholeMs = s.solo ? respinMs(PACE, s.teaseMs) : reelsMs(PACE, s.teaseMs);
      lever.rotation.x = stillSpin ? (dt < wholeMs ? LEAN : 0) : get('chess_handle_socket') ? customSpinLeverAngle(dt, s.leverFrom) : Math.max(0.4 * Math.sin(Math.PI * clamp(dt / 420)), s.leverFrom * (1 - ease(dt / 420))) + (get('chess_handle_socket') ? customLeverReturn(dt - 420) : leverRebound(dt - 420));
      let all = true;
      for (let i = 0; i < 3; i++) {
        if (s.held === i || s.keep.includes(i)) continue;
        // C1: the re-spin is reel 3 alone, so it takes no stagger (pace.respinStopMs).
        const n = strips[i].length || 13, home = angle(s.stops[i], n);
        const dur = s.solo ? respinStopMs(PACE, s.teaseMs) : reelStopMs(i, PACE, s.teaseMs);
        const target = home + Math.PI * 2 * (6 + 2 * i);
        // Law VI: reduced motion takes the STATE. The reel rests, then is simply on its stop at its thud frame.
        // A1's hold stretches reel 3's slow-down across the whole hold, so it crawls into its stop instead of
        // blurring longer and stopping as sharply as ever (the stop itself is the tape's; only the curve moves).
        const down = i === 2 && s.teaseMs > 0 ? Math.min(PACE.DECEL_MS + s.teaseMs, dur * 0.5) : Math.min(PACE.DECEL_MS, dur * 0.5);
        let x = stillSpin ? s.from[i] : THREE.MathUtils.lerp(s.from[i], target, reelTravel(Math.max(0, dt - 100), dur - 100, 180, down)) + recoilCells(dt) * cellRad(i);
        // THE ROLL follows the drum off the curve itself, so reduced motion keeps it: sound is where the beat
        // lives when the travel is gone (Law VI), and it is the same slope whether the drum is drawn or not.
        if (o.onReelSpeed && dt < dur) o.onReelSpeed(i, reelSpeed(Math.max(0, dt - 100), dur - 100, 180, down));
        if (dt >= dur) {
          if (!s.stopped[i]) { s.stopped[i] = true; stopAt[i] = t; if (o.onReelStop) o.onReelStop(i); }   // THE THUD: cue on this frame
          const k = clamp((dt - dur) / THUD_MS);
          x = stillSpin ? home : target + settleCells(dt - dur, THUD_MS) * cellRad(i);
          if (k < 1) all = false;
        } else all = false;
        reelAngles[i] = restX[i] + x;
      }
      if (all && dt >= wholeMs) settleSpin();   // a held column never shortens the pace
    } else if (pull) lever.rotation.x = PULL_MAX * pull.amount;
    else if (pullBack) { const dt = t - pullBack.start, q = clamp(dt / 200); lever.rotation.x = reduced ? 0 : get('chess_handle_socket') ? releasedLeverAngle(dt, pullBack.angle) : pullBack.angle * (1 - ease(q)) + (get('chess_handle_socket') ? customLeverReturn(dt - 200) : leverRebound(dt - 200)); if (dt >= (get('chess_handle_socket') ? LEVER_RETURN_MS + SILICONE_SETTLE_MS : 400) || reduced) pullBack = null; }
    else if (lean) lever.rotation.x = reduced ? LEAN : lean.from + (LEAN - lean.from) * ease((t - lean.start) / FEEL.LEAN_MS);   // Law VIII
    else lever.rotation.x = reduced || phase !== 'play' || partying || get('chess_handle_socket') ? 0 : BREATH_RAD * breath(t);   // THE BREATH, the only breather

    // A4 THE DRIFT while attracting: the drums roll slowly and the payline lands nothing (the stops never move).
    // Leaving it eases home over SETTLE_MS, never a thud: entering or leaving attract is not a party (Brake 1).
    if (!spin && (attract || attractOut)) {
      const q = attractOut ? clamp((t - attractOut.start) / ATTRACT.SETTLE_MS) : 0;
      for (let i = 0; i < 3; i++) reelAngles[i] = restAngle(i) + (attract ? attractRad(i, t - attract.start) : attractOut.from[i] * (1 - ease(q)));
      if (attractOut && q >= 1) attractOut = null;
    }
    // A5 THE EMI LAND-WIGGLE: a cell that landed EMI shrugs once after its own reel's thud and comes back to
    // the same stop. It rides after the thud, so no other reel waits on it (Law X). Reduced motion: nothing.
    if (!reduced) for (let i = 0; i < 3; i++) {
      const w = wiggleCells(t - wiggleAt[i]);
      if (w) reelAngles[i] = (spin ? reelAngles[i] : restAngle(i)) + w * cellRad(i);
    }

    // The payline reveal lifts (a win) or dims (nothing) for REVEAL_MS; THE THUD flashes each reel 2.2 -> 1.
    const rq = (t - revealAt) / PACE.REVEAL_MS, glow = rq < 0 || rq > 1 ? 1 : 1 + revealGain * (reduced ? 0.6 : Math.sin(Math.PI * rq));
    reels.forEach((r, i) => {
      const k = (t - stopAt[i]) / THUD_MS, flash = k < 0 || k >= 1 ? 1 : reduced ? 1.3 : 1 + 1.2 * (1 - ease(k));
      // THE GLYPH HIT's rim pulse rides the reel's own brightness: the drum lifts with the cell it landed.
      if (r.material && r.material.color) r.material.color.setScalar(glow * flash + 0.7 * hitGlow(i, t));
      // A2 under reduced motion: no travel and no repaint, the reel takes one gold tint and settles (Law VI).
      if (reduced && ghost && ghost.r === i && r.material && r.material.color) r.material.color.lerp(GOLD, 0.6 * ghostAmt(t));
    });

    freezers.forEach((f, i) => {
      if (!f) return;
      if (f.material && 'emissiveIntensity' in f.material) f.material.emissiveIntensity = hold === i ? 1.8 : 0.1;
      f.position.y = f.userData.restY - (reduced ? 0 : latchTravel(t - pulse[i]));
    });

    // THE MARQUEE: the chase runs at the heat of the last win, the tier's own palette while it celebrates.
    const h = heatNow(t);
    const partyMood = pr && pr.chase ? (pr.gold ? 'jackpot' : pr.tier >= 3 ? 'big' : 'win') : null;
    const m = spin ? (teasing ? (spin.teaseGold ? 'tease_gold' : 'tease') : 'spin')
      : partyMood || (mood !== 'idle' ? mood : hold !== null ? 'freeze' : pull ? 'pull' : 'idle');
    const bulbStretchX = shared?.fixture.userData.slotStretchX || 1;
    const bulbStretchY = shared?.fixture.userData.slotStretch || 1;
    const customHandle=get('chess_handle_socket');
    if(customHandle){
      customHandle.scale.set(1/bulbStretchX,1/bulbStretchY,1);
      if(lever.rotation.x > .12) returning=true;
      if(returning && sculptureAge >= 0){returning=false;if(!reduced)try{wobbleKit.play('silicone');}catch{}}
      customHandle.userData.flexTip?.(stillFx()?0:siliconeRebound(sculptureAge));
    }
    const colors = PALETTES[m], period = m === 'spin' ? 420 : chaseMs(partyMood ? Math.max(h, pr.tier) : h);
    const travel = reduced ? 0 : (t - (partyMood ? party.start : moodAt)) / period;
    bulbs.forEach((b, i) => {
      b.scale.copy(bulbScales[i]); b.scale.x /= bulbStretchX; b.scale.y /= bulbStretchY;
      const p = i * 0.65 + travel, step = Math.floor(p), mix = p - step;
      b.material.color.setHex(colors[step % colors.length]).lerp(nextColor.setHex(colors[(step + 1) % colors.length]), mix * mix * (3 - 2 * mix));
      b.material.emissive.copy(b.material.color); b.material.emissiveIntensity = (0.6 + 0.1 * h) * (teasing ? TEASE_DIM : 1);
    });
    // A4: one chase sweeps the bulbs every ~8 s while attracting. Brightness only, one pulse a bulb, no colour
    // change and nothing near the strobe floor.
    if (attract && !reduced) {
      const pass = ((t - attract.start) % ATTRACT.CHASE_MS) / ATTRACT.CHASE_PASS_MS;
      if (pass <= 1) bulbs.forEach((b, i) => {
        const d = Math.abs(pass * (bulbs.length + 8) - 4 - i);
        if (d < 4) b.material.emissiveIntensity += 0.9 * (1 - d / 4) ** 2;
      });
    }
    rimLighting?.update(t, reduced);
    if (glowMat) {
      glowMat.emissive.copy(glowRest.color).lerp(GOLD, heat.gold ? clamp(h / 4) : 0);
      glowMat.emissiveIntensity = glowRest.intensity * (0.55 + 0.45 * h);
    }

    if (emi) {
      const k = perchAt(t);
      emi.position.copy(emiRest.p); emi.scale.copy(emiRest.s); emi.rotation.copy(emiRest.r);
      if(shared){emi.scale.y*=initialStretch/(shared.fixture.userData.slotStretch||1);emi.scale.x*=initialStretchX/(shared.fixture.userData.slotStretchX||1);}
      if (k > 0) {
        emi.position.lerp(perch, k);
        if (k < 1 && !reduced) emi.position.y += PERCH_HOP * Math.sin(Math.PI * k);   // the little jump across
        emi.scale.multiplyScalar(1 - (1 - PERCH_SCALE) * k);
      }
      // Follow the next reel to stop, then the final moving reel. Settle before the reaction.
      gazeReel = !reduced && spin ? [0,1,2].find(i => spin.held !== i && !spin.keep.includes(i) && !spin.stopped[i]) ?? -1 : -1;
      let aimYaw = 0, aimPitch = 0;
      if (gazeReel >= 0) {
        reels[gazeReel].getWorldPosition(gazePoint); emiHost.worldToLocal(gazePoint);
        const dx = gazePoint.x - emi.position.x, dy = gazePoint.y - emi.position.y;
        aimYaw = THREE.MathUtils.clamp(dx * .45, -.22, .22);
        aimPitch = -THREE.MathUtils.clamp(dy * .15, -.1, .1);
      }
      const gazeEase = reduced || partying ? 1 : 1 - Math.exp(-Math.min(100,t-gazeAt)/120);
      gazeAt = t;
      gazeYaw += (aimYaw-gazeYaw)*gazeEase; gazePitch += (aimPitch-gazePitch)*gazeEase;
      emi.rotation.y += gazeYaw; emi.rotation.x += gazePitch;
      const age = t - moodAt, pa = partying ? t - party.start : Infinity;
      for(const arm of emiArms)if(arm)arm.node.rotation.copy(arm.rest);
      if(stillFx())emiClickAt=-Infinity;
      if (!stillFx() && phase === 'play') {
        let lean2 = .035*Math.sin(t/820), hop = 0, squash = 1, pop = 1, turn = .10*Math.sin(t/1900);
        // A small glance and body lean keep the ledge mascot alive between pulls.
        emi.rotation.x += .022*Math.sin(t/1100);
        if (spin) { lean2 = 0.085 * Math.sin((t - spin.start) / (melted ? 440 : 220)); emi.rotation.x += .07; }   // Brake 5: EMI slows while melted
        else if (pull) lean2 = -0.09 * pull.amount;
        else if (pr && pr.reveal && pa < FEEL.REVEAL_MS) { const q = pa / FEEL.REVEAL_MS; pop = 0.6 + 0.4 * reveal(q); turn = Math.PI * 2 * reveal(q); }   // THE REVEAL
        else if (pr && pr.jolt && pa < 600) { const q = pa / 600; hop = Math.sin(q * Math.PI) * 0.025; squash = 1 - 0.07 * Math.sin(q * Math.PI * 2); lean2 = 0.06 * Math.sin(q * Math.PI * 2); }
        else if (m === 'melt') { squash = 0.91; lean2 = -0.07; }
        else if (m === 'free' && age < 600) lean2 = 0.08 * Math.sin(age / 600 * Math.PI * 2);
        const click = sampleEmiReaction('greet',(t-emiClickAt)/1000);
        const armLift = spin ? .45+.10*Math.sin(t/180) : pr?.jolt && pa<1600 ? 1.4*Math.sin(Math.PI*pa/1600) : .10+.06*Math.sin(t/700);
        if(emiArms[0])emiArms[0].node.rotation.z-=armLift+click.left;
        if(emiArms[1])emiArms[1].node.rotation.z+=armLift+click.right;
        emi.position.y += hop; emi.rotation.z += lean2+click.roll; emi.rotation.y += turn+click.yaw;
        emi.scale.multiplyScalar(pop); emi.scale.y *= squash; emi.scale.x /= Math.sqrt(squash);
      }
    }
    // screen_jackpot per tier: the jackpot counts up (THE REVEAL), big thuds 2.2 -> 1, bigger glows, then the base text.
    const sj = screens.screen_jackpot;
    if (sj) {
      const pa = partying ? t - party.start : Infinity;
      let lit = 0.45, text = sj.base || '';
      if (pr && pr.screen) {
        text = pr.tier === 4 && !reduced && pa < FEEL.REVEAL_MS ? Math.round(party.amount * ease(pa / FEEL.REVEAL_MS)).toLocaleString('en-US') : party.label || text;
        lit = reduced ? 1 : pr.tier >= 3 ? 0.45 * (1 + 1.2 * (1 - ease(pa / THUD_MS))) + 0.35 : 0.45 + 0.5 * (1 - ease(pa / FEEL.GLOW_OUT_MS));
      }
      sj.mesh.material.emissiveIntensity = lit;
      sj.mesh.getWorldScale(crownScale);
      crownDisplay.update(t, { base: sj.base || '', result: pr?.screen ? text : '', still: reduced,
        aspect: Math.abs(crownSize.x*crownScale.x / (crownSize.y*crownScale.y || 1)) });
    }
    // THE MARQUEE BOARD: the live line while it holds, then the cabinet name, with a short neon restrike
    // on every change. Reduced motion takes the steady level and no stutter (Law VI).
    const sm = screens.marquee;
    if (sm) {
      if (marqueeMsg && t - marqueeAt >= MARQUEE_HOLD_MS) { marqueeMsg = null; marqueeAt = t; }
      screen('marquee', marqueeMsg || sm.base || '');
      const age = t - marqueeAt, q = age / MARQUEE_FLICKER_MS;
      const flick = reduced || !(q >= 0) || q >= 1 ? 0 : (1 - q) * (q < 0.22 ? 1 : Math.sin(q * 31) > 0 ? 0.85 : 0.2);
      sm.mesh.material.emissiveIntensity = MARQUEE_LIT * (1 + 1.6 * flick);
    }
    const payout=coinShower.debug();
    screen('screen_status',payout.active?'âœ¦ '+payout.label+' âœ¦':screens.screen_status?.base||'');
    if(screens.screen_status) {const m=screens.screen_status.mesh.material;
      if(payout.active)m.emissive.setHSL(reduced ? .1 :(payout.age*.18)%1,.8,.6);else m.emissive.set(0xffffff);}
    sparks.forEach((s, i) => {
      const age = partying && pr.sparks && !reduced ? t - party.start - i * 55 : -1;
      s.visible = age >= 0 && age < 570;   // THE SPARKLE BURST, under 600 ms
      if (!s.visible) return;
      const q = age / 570;
      spawn.getWorldPosition(s.position);
      s.position.x += (i - 3) * 0.07 * q; s.position.y += Math.sin(q * Math.PI) * 0.32; s.position.z += q * 0.2; s.rotation.set(q * 4, i, q * 7);
    });
    if (tray) {   // payout_tray takes a small THE THUD when the bank starts or the spend lands
      const k = (t - trayAt) / THUD_MS;
      tray.scale.copy(trayRest);
      if (!reduced && k >= 0 && k < 1) tray.scale.y *= 1 + 0.12 * (1 - thud(k));
    }
    // THE SHIVER: the whole cabinet, +-4 px across the screen, no colour change. Reduced motion plays nothing.
    const meltAge = (t - meltShakeAt) / 520;
    const meltPx = meltAge >= 0 && meltAge < 1 ? Math.sin(meltAge*37)*9*(1-meltAge)*(1-meltAge) : 0;
    const px = reduced ? 0 : shiverPx(t - shiverAt) + meltPx;
    if (poses && phase === 'play') {
      const wpp = (2 * poses.play.dist * Math.tan(THREE.MathUtils.degToRad(camera.fov) / 2)) / (canvas.clientHeight || 1);
      const localRight = shared ? poses.play.right.clone().transformDirection(rig.parent.matrixWorld.clone().invert()) : poses.play.right;
      rig.position.x = rigRest.x + localRight.x * px * wpp; rig.position.z = rigRest.z + localRight.z * px * wpp;
    }
    if (o.payline) {   // A6: the winning row is framed for the rollup, then THE GLOW goes out over 480 ms
      const lit = phase === 'play' ? paylineGlow(t - paylineAt, paylineHold, paylinePulseN) : 0;
      const rect = lit > 0 ? paylineRect() : null;
      o.payline.hidden = !rect;
      if (rect) {
        const s = o.payline.style;
        s.left = `${rect.left.toFixed(1)}px`; s.top = `${rect.top.toFixed(1)}px`;
        s.width = `${rect.width.toFixed(1)}px`; s.height = `${rect.height.toFixed(1)}px`;
        s.opacity = lit.toFixed(3);
      }
    }
    if (!stillFx() && (ghost || hitting(t) || t - lastPaint > PAINT_MS)) paint(t);   // A2's ghost and the glyph hit repaint every frame
    else if (stillFx() && hitting(t) && t - lastPaint > PAINT_MS) paint(t);          // still reels take the lit rim, a few paints
    if (hitDirty && !hitting(t)) { hitDirty = false; paint(t); }                   // ...and one paint to put it out
    if (hazeMesh) {
      const a = hazeOn ? hazeAt(t - hazeOn.start) : hazeOut ? hazeOut.from * (1 - ease((t - hazeOut.start) / 300)) : 0;
      if (hazeOut && t - hazeOut.start >= 300) hazeOut = null;
      hazeMesh.visible = a > 0.001; hazeMesh.material.opacity = a;
      if (hazeMesh.visible && !reduced) hazeMesh.rotateZ(-TAU * (Math.min(50, t - hazeTick) / 14000));
      hazeTick = t;
    }
    if (ghost && t - ghost.at >= FEEL_ALMOST.TELL_MS) ghost = null;
    if(shared) {
      const compact = canvas.clientHeight <= 500 && canvas.clientWidth > canvas.clientHeight;
      const windowBox=screenBox(glass);
      if(windowBox && o.topRow){
        const width=Math.min(canvas.clientWidth-12,windowBox.width+20);
        o.topRow.style.left=`${Math.max(6,windowBox.left+windowBox.width/2-width/2)}px`;
        const crown = screenBox(get('screen_jackpot'));
        o.topRow.style.top=`${Math.max(4,windowBox.top-(compact?22:31),(crown?.top||0)+(crown?.height||0)+2)}px`;
        o.topRow.style.width=`${width}px`;
      }
      if(o.freezeLabels)for(let i=0;i<3;i++){
        const b=screenBox(freezers[i]),label=o.freezeLabels[i];
        if(b && label){label.style.left=`${b.left+b.width/2}px`;label.style.top=`${Math.min(canvas.clientHeight-122,b.top+b.height+1)}px`;label.style.width=`${Math.max(64,Math.min(90,b.width+8))}px`;}
      }
      const buttonBox=screenBox(get('spin_button'));
      if(buttonBox && o.spinControl){
        o.spinControl.style.left=`${buttonBox.left+buttonBox.width/2}px`;
        const buttonHeight = compact ? 28 : 44;
        const freezeBottom=Math.max(...(o.freezeLabels||[]).map(n=>(parseFloat(n.style.top)||0)+(compact?22:26)));
        const apron = screenBox(apronDisplay);
        const bottomLimit = Math.min(canvas.clientHeight-50, apron ? apron.top-3 : Infinity);
        o.spinControl.style.top=`${Math.min(bottomLimit-buttonHeight,Math.max(freezeBottom+3,buttonBox.top+buttonBox.height/2-buttonHeight/2))}px`;
        o.spinControl.style.width=`${Math.max(110,Math.min(180,buttonBox.width+12))}px`;
      }
    }
    if (o.hint) {
      o.hint.hidden = phase !== 'play' || !!spin;
      if (!o.hint.hidden) {
        const box = new THREE.Box3().setFromObject(lever), p = new THREE.Vector3((box.min.x + box.max.x) / 2, box.max.y, (box.min.z + box.max.z) / 2).project(camera);
        o.hint.style.left = `${(p.x + 1) * canvas.clientWidth / 2}px`; o.hint.style.top = `${(1 - p.y) * canvas.clientHeight / 2 - 12}px`;
      }
    }
    for (let i=0;i<3;i++) if(reelTex[i]) reelTex[i].offset.x=stripTransform(reelAngles[i],strips[i].length).offset;
    const gap = 1000 / (budget.mobile ? 30 : 60);
    if (!shared && !document.hidden && t - lastDraw >= gap - 1) {
      renderer.render(scene, camera); lastDraw = t;
    }
  }
  // A throw in one frame must not stop the loop (a spin in flight would never land). A tainted canvas
  // (cross-origin media without CORS) stays tainted, so drop the dealt GIFs and rebuild the reel canvases.
  let frameFailed = false;
  const loop = t => {
    if (disposed) return;
    try { update(t); } catch (err) {
      if (look.gif) { console.warn('[slot] reel media failed to upload, using fallback art', err); look = { ...look, gif: null }; setStrips(strips); }
      else if (!frameFailed) console.error('[slot] frame failed', err);
      frameFailed = true;
    }
    raf = requestAnimationFrame(loop);
  };

  // Lever drag and freeze taps (preview gesture: release past 55% of the pull spins once).
  const ray = new THREE.Raycaster(), pointer = new THREE.Vector2();
  function leverRect() {
    const box = new THREE.Box3().setFromObject(lever), r = canvas.getBoundingClientRect(), pts = [];
    for (const x of [box.min.x, box.max.x]) for (const y of [box.min.y, box.max.y]) for (const z of [box.min.z, box.max.z]) {
      const p = new THREE.Vector3(x, y, z).project(camera); pts.push([r.left + (p.x + 1) * r.width / 2, r.top + (1 - p.y) * r.height / 2]);
    }
    return { left: Math.min(...pts.map(p => p[0])), right: Math.max(...pts.map(p => p[0])), top: Math.min(...pts.map(p => p[1])), bottom: Math.max(...pts.map(p => p[1])) };
  }
  function cancelPull() {
    if (!pull) return;
    pullBack = { start: performance.now(), angle: lever.rotation.x };
    if (canvas.hasPointerCapture(pull.id)) canvas.releasePointerCapture(pull.id);
    pull = null; canvas.style.cursor = '';
  }
  function onDown(e) {
    if (phase !== 'play' || spin || pull || !e.isPrimary || e.button !== 0 || !o.canPull()) return;
    const r = canvas.getBoundingClientRect();
    pointer.set((e.clientX - r.left) / r.width * 2 - 1, 1 - (e.clientY - r.top) / r.height * 2);
    ray.setFromCamera(pointer, camera);
    const spinButton=get('spin_button');
    const targets = [lever, spinButton, ...freezers].filter(Boolean);
    let ob = ray.intersectObjects(targets, true)[0]?.object;
    while (ob && !targets.includes(ob)) ob = ob.parent;
    const b = leverRect(), pad = 18;
    if (ob === lever || (!ob && e.clientX >= b.left - pad && e.clientX <= b.right + pad && e.clientY >= b.top - pad && e.clientY <= b.bottom + pad)) {
      e.preventDefault(); pullBack = null; lean = null; pull = { id: e.pointerId, y: e.clientY, amount: 0 };
      canvas.setPointerCapture(e.pointerId); canvas.style.cursor = 'grabbing';
    } else if (ob === spinButton) { e.preventDefault(); o.onLever(); }
    else if (ob) o.onFreeze(freezers.indexOf(ob));
  }
  // THE STROKE SOUNDS ON THE WAY DOWN, not when the hand lets go: the cue fires the frame the drag crosses
  // PULL_COMMIT, which is mid-stroke, so the pull and its sound share a frame however long the hand holds on.
  function onMove(e) {
    if (!pull || pull.id !== e.pointerId) return;
    e.preventDefault();
    pull.amount = clamp((e.clientY - pull.y) / Math.min(120, canvas.clientHeight * 0.18));
    if (!pull.sounded && pull.amount >= PULL_COMMIT) { pull.sounded = true; if (o.onPull) o.onPull(); }
  }
  function onUp(e) { if (!pull || pull.id !== e.pointerId) return; const commit = pull.amount >= PULL_COMMIT, sounded = !!pull.sounded; cancelPull(); if (commit) o.onLever({ sounded }); }
  canvas.addEventListener('pointerdown', onDown); canvas.addEventListener('pointermove', onMove);
  canvas.addEventListener('pointerup', onUp); canvas.addEventListener('pointercancel', cancelPull);

  resize();
  if (!shared) rig.position.y = -poses.drop;
  if (!shared) aim(poses.arrive.pos, poses.arrive.look);
  setFace('idle0_0');
  if (shared) releaseView = shared.register({ update:() => { try { update(performance.now()); } catch(err) { if(look.gif){look={...look,gif:null};setStrips(strips);} else if(!frameFailed)console.error('[slot] shared frame failed',err); frameFailed=true; } } });
  else raf = requestAnimationFrame(loop);

  return {
    missing: [],
    faceImage: atlas && atlas.image,
    recoloured: recoloured.length > 0,
    get phase() { return phase; },
    get spinning() { return !!spin; },
    resize, setStrips, setStops, setFace, dispose, cancelPull,
    screen(name, text) { if (screens[name]) screens[name].base = text; if (!(name === 'marquee' && marqueeMsg)) screen(name, text); },
    /** THE MARQUEE BOARD: post a line to the marquee for MARQUEE_HOLD_MS (null or '' returns the cabinet name
     *  now). The centred callout zoom and the word beat are untouched: the board mirrors them, it never
     *  replaces them. Long lines wrap onto up to three lines inside the panel. */
    marquee(text) {
      const line = text == null ? '' : String(text).trim();
      marqueeMsg = line || null;
      apronTicker?.message(line);
      marqueeAt = performance.now();
    },
    get marqueeLine() { return marqueeMsg; },
    meltShake() { meltShakeAt = performance.now(); },
    setLook(next) { look = { ...next, reduced, face: atlas && atlas.image }; paint(performance.now(), true); },
    /** A freeze lit or cleared: the button dips either way (Law VIII). */
    setHold(col) { if (col !== hold) { const c = col !== null ? col : hold; if (c !== null) pulse[c] = performance.now(); } hold = col; },
    /** Law VIII: the lever leans into a press at once, before the tape or the server answers. */
    answer() { if (spin || pull || phase !== 'play') return; pullBack = null; lean = { start: performance.now(), from: lever.rotation.x }; },
    /** A press that was refused: the lean lets go. */
    letGo() { if (!lean) return; lean = null; pullBack = { start: performance.now(), angle: lever.rotation.x }; },
    setMelted(on) { melted = !!on; },
    /** One cabinet celebration per landed outcome (feel.recipe). Brake 2: a lesser party inside a running one merges. */
    celebrate(r, amount = 0, label = '') {
      coinShower.start(amount,r.tier,label);
      if (r.gold) { reelMood = 'jackpot'; reelMoodAt = performance.now(); }
      const t = performance.now();
      heatTo(Math.max(r.heat, party && t < party.end ? heat.to : 0), r.gold || (party && t < party.end && heat.gold));
      if (r.shiver) shiverAt = t;
      if (party && t < party.end && party.r.tier >= r.tier) return false;
      party = r.tier > 0 ? { r, amount, label, start: t, end: t + PARTY_MS[r.tier] } : null;
      return true;
    },
    /** THE BANK touches the tray: when a win starts paying out, or when a spend lands in it. */
    trayThud() { trayAt = performance.now(); },
    malus() { shiverAt = performance.now(); },
    /** A6: frame the winning row for `ms` (THE BANK's rollup), then let THE GLOW out. It rides the landing
     *  beat the reveal already owns (Law X): tier 1 takes one soft pulse, reduced motion and a melted spin
     *  take a steady frame. `r` is feel.recipe's verdict for the outcome. */
    payline(ms, r) {
      paylineAt = performance.now();
      // Reduced motion has no rollup to follow (THE BANK settles at once), so the frame is steady and holds
      // the reveal's own beat instead of the whole count (Law VI: the STATE, not a longer version of it).
      paylineHold = reduced ? PACE.REVEAL_MS : Math.max(0, Number(ms) || 0);
      paylinePulseN = paylinePulses(r ? r.tier : 0, { reduced, melted: !!(r && r.melted) });
    },
    /** Law VI: a press or a new spin takes the frame straight to its settled end, never a faster pulse. */
    paylineOut() { if (paylineAt > -Infinity) paylineHold = Math.max(0, Math.min(paylineHold, performance.now() - paylineAt)); },
    /** Law VI: Back and suspend skip every ceremony to its settled state. */
    skip() { reelMood = 'idle'; settleMechanical(); meltShakeAt = -Infinity; apronTicker?.message(null); marqueeMsg = null; party = null; shiverAt = trayAt = -Infinity; stopAt.fill(-Infinity); revealAt = -Infinity; lean = null; ghost = null; teasing = false; heat = { ...heat, from: heat.to, at: -Infinity }; paylineAt = -Infinity; if (o.payline) o.payline.hidden = true; hitAt.fill(-Infinity); hitDirty = true; hazeOn = null; hazeOut = null; },
    /** THE GLYPH HIT (feel.highlightPlan): the landed cells on `plan` ([{ reel, at }]) glow from this frame, reel
     *  order, each `at` ms in, all out by HIGHLIGHT_MS. Nothing moves a stop; it lights what the tape landed. */
    highlight(plan) {
      const now = performance.now();
      for (const h of Array.isArray(plan) ? plan : []) if (h && h.reel >= 0 && h.reel < 3) { hitAt[h.reel] = now; hitStart[h.reel] = Math.max(0, Number(h.at) || 0); }
      hitDirty = true;
    },
    /** THE ATTRACT HAZE on or off (the station owns the idle timer). Off eases out over 300 ms; reduced motion
     *  and a cabinet not yet in play take nothing (Law VI). */
    haze(on) {
      if (on) {
        if (phase !== 'play' || reduced) return false;
        hazeMesh = hazeMesh || makeHaze();
        if (!hazeOn) { hazeOn = { start: performance.now() }; hazeOut = null; }
        return true;
      }
      if (hazeOn) hazeOut = { start: performance.now(), from: hazeAt(performance.now() - hazeOn.start) };
      hazeOn = null;
      return true;
    },
    get hazing() { return !!hazeOn; },
    /** A node's centre in client px (tokens fly from and to these), null when the glb lacks it. `top` takes the
     *  middle of its top edge instead: where a speech bubble wants to stand, EMI's shelf or her topper. */
    portrait(i) {
      const box = screenBox(reels[i]);
      if (!box || !strips[i]) return null;
      const image = makeCanvas(Math.round(cell.hw*2), Math.round(cell.hh*2));
      const g = image.getContext('2d'); g.translate(image.width/2,image.height/2);
      drawSymbol(g, strips[i][stopsNow[i]], performance.now(), {...look, reduced:reduced||stillFx(), faceMood:reelMood, faceAge:performance.now()-reelMoodAt}, cell);
      const rect = canvas.getBoundingClientRect();
      return { canvas:image, x:rect.left+box.left+box.width/2, y:rect.top+box.top+box.height/2,
        w:box.width, h:Math.min(box.height,box.width*image.height/image.width) };
    },
    project(name, top = false) {
      const n = get(name);
      if (!n) return null;
      const box = new THREE.Box3().setFromObject(n), at = box.isEmpty() ? n.getWorldPosition(new THREE.Vector3()) : box.getCenter(new THREE.Vector3());
      if (top && !box.isEmpty()) at.y = box.max.y;
      const p = at.project(camera);
      const r = canvas.getBoundingClientRect();
      return { x: r.left + (p.x + 1) * r.width / 2, y: r.top + (1 - p.y) * r.height / 2 };
    },
    /** Light the landed payline for PACE.REVEAL_MS (a win lifts, nothing dims). The caller holds the pace. */
    reveal(win) { revealAt = performance.now(); revealGain = win ? 0.45 : -0.25; },
    /** Rise over the dimmed room, then ease to the seat. Resolves when interactive. */
    rise() {
      if (shared) { phase='play'; return Promise.resolve(); }
      if (reduced) { rig.position.y = 0; phase = 'play'; aim(poses.play.pos, poses.play.look); return Promise.resolve(); }
      phase = 'rise';
      return new Promise(done => { tl = { kind: 'rise', start: performance.now(), done }; });
    },
    /** Slide down from wherever it is (Back at every phase). */
    sink() {
      cancelPull(); settleSpin();
      if (shared) { phase='hidden'; return Promise.resolve(); }
      if (reduced || phase === 'hidden') { tl = null; phase = 'hidden'; return Promise.resolve(); }
      const prev = tl; phase = 'sink';
      return new Promise(done => { tl = { kind: 'sink', start: performance.now(), fromY: rig.position.y, done: () => { done(); if (prev && prev.done) prev.done(); } }; });
    },
    /** Spin to `stops`, the held column stays put. Resolves when the last reel has thudded, reelsMs() from
     *  now with or without reduced motion (the pace is economy, not animation).
     *  `tease` is A1 from feel.anticipation: `{holdMs, gold, dim}`. Reel 3 keeps its blur for holdMs longer
     *  and thuds late; reduced motion and Calm keep the hold, they only drop the light change. */
    spin(stops, held = null, tease = null) {
      coinShower.clear();
      reelMood = 'idle';
      settleSpin();
      ghost = null;
      wiggleAt.fill(-Infinity);
      // C1 (10.16.D): `keep` are reels that do not travel at all (the re-spin holds 1 and 2 as EMI), and
      // `solo` drops the stagger, because there is nothing before reel 3 to stagger behind.
      const keep = Array.isArray(tease && tease.keep) ? tease.keep.filter(i => [0, 1, 2].includes(i)) : [];
      return new Promise(resolve => {
        spin = { start: performance.now(), stops, held, keep, solo: keep.length > 0, leverFrom: lever.rotation.x,
                 resolve, stopped: [false, false, false],
                 teaseMs: Math.max(0, (tease && tease.holdMs) || 0), teaseGold: !!(tease && tease.gold),
                 teaseDim: tease ? tease.dim !== false : true,
                 from: reelAngles.map((a, i) => a - restX[i]) };
        pull = null; pullBack = null; lean = null;
      });
    },
    /** A2 THE ALMOST, from feel.almost: the off-by-one cell the server's own strip put next to the line
     *  ghosts gold and snaps back, once. Nothing is weighted, nudged or re-drawn; this shows what landed. */
    almost(near) {
      if (near) { reelMood = 'near'; reelMoodAt = performance.now(); }
      if (!near || !(near.cell >= 0)) return;
      ghost = { r: near.reel === undefined ? 2 : near.reel, j: near.cell, at: performance.now() };
    },
    /** A4: the attract drift on or off (the station owns the idle timer and EMI's wink). `now` snaps back to
     *  the stops instead of easing home (Law VI: Back and suspend skip to the settled state). */
    attract(on, now = false) {
      if (on) {
        if (attract || attractOut || spin || reduced || phase !== 'play') return false;
        attract = { start: performance.now() };
        return true;
      }
      const was = !!attract, at = performance.now();
      if (attract && !now) attractOut = { start: at, from: [0, 1, 2].map(i => wrapRad(attractRad(i, at - attract.start))) };
      attract = null;
      if (now) { attractOut = null; setStops(stopsNow); }
      return was;
    },
    get attracting() { return !!attract; },
    /** A5: reel `i` wiggles `delay` ms from now (its own thud goes first). The loop owns it: no timer. */
    wiggle(i, delay = 0) { if (!reduced && i >= 0 && i < 3) wiggleAt[i] = performance.now() + Math.max(0, delay); },
    settle: settleSpin,
    /** For dev.html and CDP checks only. */
    debug() {
      const t = performance.now();
      return { meltShakeAgeMs: performance.now()-meltShakeAt, crown: crownDisplay?.debug(), shared:!!shared, responsiveStretch:shared?.fixture.userData.slotStretch||1, fov:camera.fov, hiddenRoomObjects:roomVisibility.size, rigId:rig.uuid, reelIds:reels.map(r=>r.uuid), reelAngles:reelAngles.slice(), reelOffsets:reels.map(r=>r.material?.map?.offset.x), lever: lever.rotation.x, heat: heatNow(t), gold: heat.gold, party: party && t < party.end ? party.r.party : null,
               tier: party && t < party.end ? party.r.tier : 0, face: faceName, gaze: { reel:gazeReel, yaw:gazeYaw, pitch:gazePitch }, shiverPx: reduced ? 0 : shiverPx(t - shiverAt),
               reelBrightness: reels.map(r => r.material && r.material.color ? r.material.color.r : 1), leaning: !!lean,
               tease: teasing, teaseMs: spin ? spin.teaseMs : 0, teaseGold: !!(spin && spin.teaseGold),
               almost: ghost ? { cell: ghost.j, reel: ghost.r, amt: Number(ghostAmt(t).toFixed(3)) } : null,
               apron: apronTicker?.debug() || null, apronBounds: screenBox(apronDisplay), bulbs: bulbs.length,
               tray: !!tray, glow: !!glowMat, emiScale: emi ? emi.scale.x / emiRest.s.x : null,
               payline: { lit: paylineGlow(t - paylineAt, paylineHold, paylinePulseN), hold: paylineHold, pulses: paylinePulseN, rect: paylineRect() },
               window: screenBox(glass), band: poses ? poses.band : null, canvas: { w: canvas.clientWidth, h: canvas.clientHeight },
               solo: !!(spin && spin.solo), keep: spin ? spin.keep : [],
               attract: !!attract, drifting: !!(attract || attractOut), wiggling: wiggleAt.map(a => wiggleCells(t - a) !== 0),
               hits: [0, 1, 2].map(i => Number(hitGlow(i, t).toFixed(3))), haze: { on: !!hazeOn, opacity: hazeMesh ? Number(hazeMesh.material.opacity.toFixed(3)) : 0 },
               // THE CLOSE SEAT: what the seated frame actually holds, so a re-check can measure it without eyes.
               seat: { dist: poses ? Number(poses.play.dist.toFixed(3)) : null, perch: Number(perchAt(t).toFixed(3)),
                       marquee: screenBox(glowNode), lever: screenBox(lever), emi: screenBox(emi), shelf: screenBox(shelf), freeze: screenBox(freezers[0]) } };
    },
  };
}
