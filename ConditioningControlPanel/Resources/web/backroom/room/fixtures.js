/* ============================================================================
 * backroom/room/fixtures.js - the shell and the five fixtures, set out once.
 *
 * Ported from blender-scripting backroom/preview/app.js, with the speed pass:
 *   - each glb file is fetched and decoded ONCE; the three slots are clones
 *   - every bulb of a kind becomes one InstancedMesh (per-instance colour drives
 *     the emissive through a two-line shader patch), not one draw per bulb
 *   - every bulb and sconce aura is one point in ONE additive Points draw
 *   - the floor spins in its shader and the reels scroll their texture, so no
 *     node that carries a quantized mesh is ever moved (see the build script)
 *   - the roulette hub's 960 idle motes move on the GPU, not in a JS loop
 *
 * THE REWARD PASS (CONTRACT 10.22.A, lane BR2-room). The coin shower used to be built behind
 * `if (row.id === 'slot')`: one cabinet in four paid visibly and the other three paid in silence.
 * That gate is gone. EVERY fixture gets a shower, aimed by room/payout-anchor.js at its own tray
 * line, and `celebrate()` runs through room/win-echo.js so the floor's half of a win - the coins, the
 * fixture's own screen, its aura and, for a hero, the Parlour's board - is sized by shared/win/plan.js
 * and by nothing here. The room decides WHERE a win shows; plan.js decides WHETHER and HOW BIG.
 *
 * TRAP: the shower is built at BOOT for every fixture, not lazily on the first win, and it costs one
 * instanced draw at count 0 per fixture for it. That is deliberate: a material three.js has never
 * rendered is a material it has not compiled, and building the shower on the winning frame would put
 * a shader compile on exactly the frame that must not stutter.
 * ==========================================================================*/

import * as T from 'three';
import { visibleInTree, idleReelEligible } from './fixture-visibility.js';
import { createVenueLights } from './venue-lights.js';
import { createPrizeDisplay } from './prize-display.js';
import { createPrizeMarquee } from './prize-marquee.js';
import { createEmiIdle } from './emi-idle.js';
import { createCoinShower } from './coin-shower.js';
import { createFloorStyle } from './floor-style.js';
import { createWheelFace } from './wheel-face.js';
import { createRouletteSurfaces } from './roulette-surfaces.js';
import { SHOWER, payoutAnchor, hostAt } from './payout-anchor.js';
import { createWinEcho } from './win-echo.js';
import { createLoomKit } from '../shared/hypno/loom.js';

const BULB = /^(lights_chase_\d|bulb_\d|canopy_bulb_|rim_bulb_)/;
const CHASE = [0xff168e, 0x852bff, 0x00e6b8, 0xff9d08].map((c) => new T.Color(c));
/** What an echoing fixture's bulbs lean toward while the win settles, and how far (Law IX sizes it). */
const WIN_GOLD = new T.Color(0xffcf6b);
const WIN_LEAN = 0.6;
/** The mark the fixture's own screen wears the win line between, the one the slot has always used. */
const WIN_MARK = '✦';
/** A borrowed cabinet's screen breathes between these two (grey to colour), the breakout row's fixture. */
const GLOW_GREY = new T.Color(0x5a5a62), GLOW_TINT = new T.Color(0xff3fa8);

/**
 * A node borrowed out of another glb (`fixture.node`): cloned, floored and centred on the holder's
 * origin, fitted to at most 1 m wide and 2.05 m tall, the box the Parlour's unlocked arcade cabinet
 * takes (room/prize-display.js). Geometry stays shared with the source.
 */
function borrowed(node) {
  const copy = node.clone(true);
  copy.position.set(0, 0, 0); copy.quaternion.identity(); copy.updateMatrixWorld(true);
  const box = new T.Box3().setFromObject(copy), size = box.getSize(new T.Vector3());
  const s = Math.min(1.0 / Math.max(1e-6, size.x), 2.05 / Math.max(1e-6, size.y));
  copy.scale.setScalar(s);
  copy.position.set(-(box.min.x + box.max.x) * s / 2, -box.min.y * s, -(box.min.z + box.max.z) * s / 2);
  const group = new T.Group();
  group.add(copy);
  return group;
}

export function labelTexture(text) {
  const c = document.createElement('canvas');
  c.width = 1024; c.height = 128;
  const x = c.getContext('2d');
  x.fillStyle = '#2b1638'; x.fillRect(0, 0, 1024, 128);
  x.fillStyle = '#ffd9ee'; x.font = '600 72px "Segoe UI", system-ui, sans-serif';
  x.textAlign = 'center'; x.textBaseline = 'middle';
  x.fillText(String(text), 512, 64, 970);
  const t = new T.CanvasTexture(c);
  t.colorSpace = T.SRGBColorSpace; t.flipY = false;
  return t;
}

/** One additive Points draw for every glow in the room. */
function createAuras(count) {
  const pos = new Float32Array(count * 3), col = new Float32Array(count * 3), size = new Float32Array(count);
  const g = new T.BufferGeometry();
  g.setAttribute('position', new T.BufferAttribute(pos, 3));
  g.setAttribute('color', new T.BufferAttribute(col, 3).setUsage(T.DynamicDrawUsage));
  g.setAttribute('aSize', new T.BufferAttribute(size, 1));
  const m = new T.ShaderMaterial({
    transparent: true, depthWrite: false, blending: T.AdditiveBlending, vertexColors: true,
    uniforms: { uScale: { value: 400 } },
    vertexShader: `attribute float aSize;uniform float uScale;varying vec3 vColor;void main(){vColor=color;vec4 p=modelViewMatrix*vec4(position,1.);gl_Position=projectionMatrix*p;gl_PointSize=aSize*uScale/max(.05,-p.z);}`,
    fragmentShader: `varying vec3 vColor;void main(){float r=length(gl_PointCoord-.5)*2.;if(r>1.)discard;float a=r<.22?mix(.69,.25,r/.22):mix(.25,0.,(r-.22)/.78);gl_FragColor=vec4(vColor,a);}`,
  });
  const points = new T.Points(g, m);
  points.frustumCulled = false;
  points.renderOrder = 2;
  // Glow is not a surface. A Points raycast answers within Raycaster.params.Points.threshold (1 m) of
  // every aura, and this cloud is a scene child, so a tap anywhere near a cabinet's bulbs used to land
  // here, in front of the cabinet and on no station (the phone desk run: only the base of a slot entered).
  points.raycast = () => {};
  let n = 0;
  return {
    points,
    add(worldPos, s) { worldPos.toArray(pos, n * 3); size[n] = s; return n++; },
    set(i, color, opacity) { col[i * 3] = color.r * opacity; col[i * 3 + 1] = color.g * opacity; col[i * 3 + 2] = color.b * opacity; },
    commit() { g.setDrawRange(0, n); g.attributes.color.needsUpdate = true; },
    resize(heightPx, fov) { m.uniforms.uScale.value = heightPx / (2 * Math.tan((fov * Math.PI) / 360)); },
  };
}

/** Emissive follows the per-instance colour. */
function patchInstancedEmissive(material) {
  material.onBeforeCompile = (s) => {
    s.fragmentShader = s.fragmentShader.replace('vec3 totalEmissiveRadiance = emissive;',
      'vec3 totalEmissiveRadiance = emissive;\n#if defined(USE_COLOR) || defined(USE_INSTANCING_COLOR)\ntotalEmissiveRadiance *= vColor;\n#endif');
  };
  material.customProgramCacheKey = () => 'br-bulb';
}

/** The roulette hub at rest: the medallion spiral and its motes, all on the GPU. */
function createHub(spiral) {
  spiral.traverse((o) => { if (o.name.startsWith('inset_spiral_')) o.visible = false; });
  const uniforms = { uTime: { value: 0 } };
  const field = new T.Mesh(new T.PlaneGeometry(0.622, 0.622), new T.ShaderMaterial({
    uniforms, transparent: true, depthWrite: false, blending: T.AdditiveBlending, toneMapped: false,
    vertexShader: 'varying vec2 vUv;void main(){vUv=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}',
    fragmentShader: `varying vec2 vUv;uniform float uTime;void main(){vec2 p=(vUv-.5)*2.;float r=length(p);if(r>.98)discard;float a=atan(p.y,p.x);
float d=abs(sin(1.5*(a-r*13.)));float core=1.-smoothstep(.28,.72,d);float aura=exp(-d*d*5.);float flow=.65+.35*sin(r*42.-uTime*4.);
float edge=1.-smoothstep(.90,.98,r);float center=smoothstep(.015,.06,r);vec3 spectral=.52+.48*cos(vec3(0.,2.,4.)+r*7.-uTime*.6);
vec3 col=mix(vec3(1.,.388,.733),spectral,.8);gl_FragColor=vec4(col*1.1,(core*(.65+flow*.25)+aura*.14)*edge*center);}`,
  }));
  field.rotation.x = -Math.PI / 2; field.position.y = 0.023;
  spiral.add(field);
  const count = 960, seeds = new Float32Array(count * 2);
  for (let i = 0; i < count; i++) { seeds[i * 2] = i; seeds[i * 2 + 1] = 0.06 + (((i * 47) % 101) / 101) * 0.08; }
  const g = new T.BufferGeometry();
  g.setAttribute('position', new T.BufferAttribute(new Float32Array(count * 3), 3));
  g.setAttribute('aSeed', new T.BufferAttribute(seeds, 2));
  const motes = new T.Points(g, new T.ShaderMaterial({
    uniforms: { uTime: uniforms.uTime, uDpr: { value: 1 } }, transparent: true, depthWrite: false, blending: T.AdditiveBlending, toneMapped: false,
    vertexShader: `attribute vec2 aSeed;uniform float uTime,uDpr;varying vec3 vColor;
void main(){float i=aSeed.x;float seed=mod(i*61.,960.)/960.;float travel=fract(seed+uTime*.035);float r=.018+travel*.278;
float a=r/.311*13.+mod(i,3.)*6.2831853/3.;float sc=sin(i*12.7)*.002;
vec3 p=vec3(cos(a)*r+sc,.025+sin(i*3.7+uTime)*.0015,-sin(a)*r+cos(i*8.1)*.002);
vColor=mix(vec3(1.,.388,.733),vec3(.4,1.,.878),(.5+.5*sin(travel*6.+uTime*.3))*.8)*(.6+.6*(.5+.5*sin(i*9.1+uTime*1.4)));
vec4 mv=modelViewMatrix*vec4(p,1.);gl_Position=projectionMatrix*mv;gl_PointSize=clamp(aSeed.y*190./-mv.z,1.5,6.)*uDpr;}`,
    fragmentShader: 'varying vec3 vColor;void main(){float r=length(gl_PointCoord-.5)*2.;if(r>1.)discard;gl_FragColor=vec4(vColor,exp(-r*r*15.)+exp(-r*r*3.)*.24);}',
  }));
  motes.frustumCulled = false;
  motes.raycast = () => {};   // the same: a mote field is glow, the medallion under it is the surface
  spiral.add(motes);
  let phase = 0;
  return { update(dt, still) { if (!still) phase += dt * 0.35; uniforms.uTime.value = phase; }, setDpr(d) { motes.material.uniforms.uDpr.value = d; } };
}

/**
 * A fixture's own box, in its holder's space, skipping what the row omitted (an omitted alcove is not
 * part of the cabinet and must not drag its tray line sideways). The holders are yaw-only, so folding
 * the world box back through the inverse is exact rather than merely close.
 */
function localBox(holder, model) {
  const box = new T.Box3(), one = new T.Box3();
  (function walk(o) {
    if (o.visible === false) return;
    if (o.isMesh && o.geometry) {
      if (!o.geometry.boundingBox) o.geometry.computeBoundingBox();
      box.union(one.copy(o.geometry.boundingBox).applyMatrix4(o.matrixWorld));
    }
    for (const child of o.children) walk(child);
  })(model);
  if (box.isEmpty()) return null;
  box.applyMatrix4(new T.Matrix4().copy(holder.matrixWorld).invert());
  return { min: { x: box.min.x, y: box.min.y, z: box.min.z }, max: { x: box.max.x, y: box.max.y, z: box.max.z } };
}

/**
 * One fixture's coin shower, hung on a host group that puts the shower's own tray on this fixture's
 * (10.22.A). An authored payout node is believed first; no room glb carries one today, so the fallback
 * measures the cabinet. Null only for a fixture with no visible geometry at all.
 */
function payoutHost(holder, model, row) {
  const f = row.fixture;
  const node = SHOWER.NODES.reduce((found, name) => found || model.getObjectByName(name), null);
  const anchor = node
    ? holder.worldToLocal(node.getWorldPosition(new T.Vector3()))
    : payoutAnchor(localBox(holder, model), holder.worldToLocal(new T.Vector3().fromArray(row.approach)));
  const host = hostAt(anchor, f.scale, f.heightScale);
  if (!host) return null;
  const group = new T.Group();
  group.name = 'payout_' + row.key;
  group.position.set(host.position.x, host.position.y, host.position.z);
  group.scale.set(host.scale.x, host.scale.y, host.scale.z);
  holder.add(group);
  return group;
}

/**
 * Load and set out the room. `faces` is the EMI face atlas url; `label(row, key)`
 * resolves a registry label key ("@name" = the station's own name). `motion()` reads the room's live
 * { still, reduced, lite } for the win echo, which is the only part of this file motion can silence.
 */
export async function buildRoom({ scene, loader, stations, base, faces, label, onProgress, motion }) {
  const readMotion = typeof motion === 'function' ? motion : () => ({});
  const echo = createWinEcho();
  // Three shared 128px strips animate at 8Hz; nine independent offsets drift in the room.
  // Paused while playing, under Motion Off, and during Room Service handle pulls.
  const REEL_CELL = 128, REEL_CELLS = 13, REEL_PRESETS = ['screen', 'candy', 'pinwheel', 'mint', 'ribbon', 'star', 'whirl', 'wake', 'hub'];
  const roomTiles = new Map();
  const idleReels = []; let reelPaintAt = -Infinity;
  let reelKit = null;
  const reelStrips = new Map(), reelVersions = new Map();
  const reelFrustum = new T.Frustum(), reelView = new T.Matrix4();
  function reelStrip(i, now = 0, animate = false) {
    const cached = reelStrips.get(i);
    if (cached && !animate) return cached;
    const c = cached || document.createElement('canvas');
    if (!cached) { c.width = REEL_CELLS * REEL_CELL; c.height = REEL_CELL; }
    const x = c.getContext('2d');
    x.fillStyle = '#291635'; x.fillRect(0, 0, c.width, REEL_CELL);
    if (reelKit === null) reelKit = createLoomKit({ still: false });
    let woven = false;
    for (let k = 0; k < REEL_CELLS; k++) {
      const preset = REEL_PRESETS[(k + i) % REEL_PRESETS.length];
      let tile=roomTiles.get(preset);
      if(!tile){tile=document.createElement('canvas');tile.width=tile.height=128;roomTiles.set(preset,tile);}
      if(tile.__at!==now && reelKit?.paint(tile,preset,{now}))tile.__at=now;
      if (tile.__at===now) {
        x.save(); x.translate((k + 0.5) * REEL_CELL, REEL_CELL / 2); x.rotate(-Math.PI / 2); x.scale(1, -1);
        x.drawImage(tile, -REEL_CELL / 2, -REEL_CELL / 2, REEL_CELL, REEL_CELL); x.restore();
        woven = true;
      } else {
        // No WebGL, no Loom: the stand-in stays as the fallback, demoted, never deleted.
        x.save(); x.translate((k + 0.5) * REEL_CELL, REEL_CELL / 2); x.rotate(-Math.PI / 2); x.scale(1, -1);
        x.fillStyle = ['#f3a1d1', '#a695e4', '#ebc883'][k % 3]; x.font = '70px system-ui';
        x.textAlign = 'center'; x.textBaseline = 'middle'; x.fillText(['✦', '♡', '◎'][(k + i) % 3], 0, 0); x.restore();
      }
    }
    if (!woven && reelKit) { reelKit.dispose(); reelKit = null; }
    reelStrips.set(i, c);
    reelVersions.set(i, (reelVersions.get(i) || 0) + 1);
    return c;
  }

  let clock = 0;   // the room's own accumulated ms: it STOPS while a station holds the screen
  const files = new Map();
  const fetchModel = (file) => {
    if (!files.has(file)) files.set(file, loader.loadAsync(base + file).then((g) => g.scene));
    return files.get(file);
  };
  let done = 0;
  const total = stations.length + 1;
  const tick = () => { done++; try { onProgress && onProgress(done / total); } catch (e) { /* noop */ } };

  const [shell, atlas] = await Promise.all([
    fetchModel('shell.glb').then((s) => { tick(); return s; }),
    new T.TextureLoader().loadAsync(faces).catch(() => null),
  ]);
  if (atlas) {
    atlas.flipY = false; atlas.colorSpace = T.SRGBColorSpace;
    atlas.repeat.set(151 / 1672, 136 / 137); atlas.offset.set((3 * 152 + 0.5) / 1672, 0.5 / 137);
    atlas.magFilter = T.NearestFilter; atlas.minFilter = T.NearestFilter; atlas.generateMipmaps = false;
  }
  scene.add(shell);
  shell.updateMatrixWorld(true);
  // Widen the southeast corner gently, preserving all game fixture positions.
  const point = new T.Vector3(), warpBounds = new T.Box3();
  shell.traverse(o => {
    if (!o.isMesh || /^media_screen_/.test(o.name)) return;
    o.geometry.computeBoundingBox();
    warpBounds.copy(o.geometry.boundingBox).applyMatrix4(o.matrixWorld);
    if(warpBounds.max.x<=6.5 || warpBounds.max.z<=0)return;
    const previous=o.geometry, g=previous.clone(), a=g.attributes.position;
    const positions=new Float32Array(a.count*3), inverse=o.matrixWorld.clone().invert();
    for(let i=0;i<a.count;i++) {
      point.fromBufferAttribute(a,i).applyMatrix4(o.matrixWorld);
      point.x += 1.2*Math.max(0,Math.min(1,(point.x-6.5)/.5))*Math.max(0,point.z)/8;
      point.applyMatrix4(inverse).toArray(positions,i*3);
    }
    g.setAttribute('position',new T.BufferAttribute(positions,3));
    // The bent copy replaces the loaded one, which nothing else reads: free it with the swap.
    g.computeVertexNormals();g.computeBoundingSphere();o.geometry=g;previous.dispose();
  });

  // The old frames are baked into the shell. Larger runtime casings cover them,
  // preserving the separate screen UVs and keeping every bay below the ceiling.
  for(let i=0;i<4;i++) {
    const mount=shell.getObjectByName('screen_mount_'+i),screen=shell.getObjectByName('media_screen_'+i);
    if(!mount||!screen)continue;
    screen.scale.multiplyScalar(1.24);screen.position.z=.18;
    for(const [w,h,d,z,color,metalness] of [[2.9512,1.8228,.08,.10,0xbd8b50,.7],[2.8148,1.6864,.04,.145,0x251334,.25]]) {
      const frame=new T.Mesh(new T.BoxGeometry(w,h,d),new T.MeshStandardMaterial({color,metalness,roughness:.35}));
      frame.name='screen_enlarged_frame_'+i;frame.position.z=z;mount.add(frame);
    }
  }
  shell.traverse(o => { o.updateMatrix(); o.matrixAutoUpdate=false; });
  const oldFloor = shell.getObjectByName('spiral_inlay');
  if (oldFloor) oldFloor.visible = false;
  shell.traverse(n => { if(n.name.startsWith('floor_brass_inlay')) n.visible=false; });
  const floorStyle = createFloorStyle();
  const floor = new T.Mesh(new T.PlaneGeometry(14,16), floorStyle.material);
  const fp=floor.geometry.attributes.position; for(let i=0;i<fp.count;i++) {const x=fp.getX(i),z=-fp.getY(i);if(x>0)fp.setX(i,x+1.2*Math.max(0,z)/8);} fp.needsUpdate=true;floor.geometry.computeBoundingSphere();
  floor.name='room_spiral_floor'; floor.rotation.x=-Math.PI/2; floor.position.y=.012; scene.add(floor);
  const ceiling = shell.getObjectByName('ceiling');
  const screens = [], sconces = [], bulbs = [];
  shell.traverse((o) => {
    if (!o.isMesh) return;
    if (o.name.startsWith('sconce_globe')) sconces.push(o);
    if (/^media_screen_\d/.test(o.name)) screens.push(o);
  });
  screens.sort((a, b) => a.name.localeCompare(b.name));
  const sconceMaterial = sconces.length ? sconces[0].material.clone() : null;
  for (const s of sconces) s.material = sconceMaterial;

  const holders = new Map(), rouletteSurfaces = [], wheelFaces = [];
  const hubs = [];
  const emis = [];
  const payouts = new Map();
  /** rowKey -> the fixture's own mascot and its own name, so a win can turn a head and sign a board. */
  const emiByKey = new Map(), names = new Map();
  let marquee = null;
  /** Every fixture label mesh, `rowKey/node` -> { mesh, text }, so one can be repainted later (10.16.E). */
  const labels = new Map();
  /** Screen materials that breathe grey to colour (`fixture.glow`). */
  const glows = [];
  const set = await Promise.all(stations.map(async (row) => {
    const f = row.fixture;
    const source = await fetchModel(row.id === 'slot' ? '../../stations/slot/assets/slot.glb' : f.file);
    const model = f.node ? borrowed(source.getObjectByName(f.node) || source) : source.clone(true);
    if (row.id === 'slot') {
      const copies=new Map(), copy=m=>{if(!copies.has(m))copies.set(m,m.clone());return copies.get(m);};
      model.traverse(n=>{if(n.material)n.material=Array.isArray(n.material)?n.material.map(copy):copy(n.material);});
    }   // geometry is shared; the decoded original stays pristine
    const holder = new T.Group();
    holder.name = 'station_' + row.key;
    holder.position.fromArray(f.position);
    holder.rotation.y = f.yaw;
    holder.scale.set(f.scale, f.scale * f.heightScale, f.scale);
    holder.add(model);
    if (f.heightScale !== 1) { const emi = model.getObjectByName('emi_dealer'); if (emi) emi.scale.y /= f.heightScale; }
    if (f.preserveCharacter) { const c = model.getObjectByName(f.preserveCharacter.name); if (c) c.scale.multiplyScalar(f.preserveCharacter.factor); }
    model.traverse((o) => { if (f.omitPrefixes.some((p) => o.name.startsWith(p))) o.visible = false; });
    scene.add(holder);
    holder.updateMatrixWorld(true);

    const recolored = new Map();
    model.traverse((o) => {
      if (!o.isMesh || !o.material) return;
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      for (const m of mats) m.envMapIntensity = 0.32;
      if (f.palette && f.palette[o.material.name]) {
        if (!recolored.has(o.material.name)) {
          const m = o.material.clone();
          m.color.set('#' + f.palette[o.material.name]);
          recolored.set(o.material.name, m);
        }
        o.material = recolored.get(o.material.name);
      }
      if (row.id !== 'slot' && BULB.test(o.name)) bulbs.push({ mesh: o, row, rim: o.name.startsWith('rim'), index: bulbs.length });
    });
    if (f.faces && atlas) {
      const face = model.getObjectByName('EMI_glass');
      if (face && face.material) { face.material = face.material.clone(); face.material.map = atlas; face.material.emissiveMap = atlas; face.material.needsUpdate = true; }
    }
    for (const [node, key] of Object.entries(f.labels)) {
      const o = model.getObjectByName(node);
      if (!o || !o.isMesh) continue;
      const text = String(label(row, key)).toUpperCase();
      const t = labelTexture(text);
      o.material = new T.MeshStandardMaterial({ map: t, emissiveMap: t, emissive: 0xffffff, emissiveIntensity: 0.5, roughness: 0.5 });
      labels.set(row.key + '/' + node, { mesh: o, text });
    }
    if (f.glow) {
      const o = model.getObjectByName(f.glow);
      if (o && o.isMesh && o.material && o.material.emissive) { o.material = o.material.clone(); glows.push(o.material); }
    }
      if (f.reels) {
      for (let i = 1; i <= 3; i++) {
        const reel = model.getObjectByName('reel_' + i);
        if (!reel || !reel.isMesh) continue;
        const t = new T.CanvasTexture(reelStrip(i));
        t.colorSpace = T.SRGBColorSpace; t.flipY = false; t.wrapS = T.RepeatWrapping;
        t.offset.x = (i + 0.5) / 13;   // a resting stop, by texture, never by node
        // Its OWN Texture over the shared canvas: slot-custom-handles.js rolls offset.x per cabinet, so three
        // cabinets sharing one Texture object would all turn when one of them is pulled.
        reel.material = new T.MeshBasicMaterial({ map: t });
        idleReels.push({model, reel, map:t, index:i});
      }
    }
    if (f.hub) { const spiral = model.getObjectByName('center_spiral'); if (spiral) hubs.push(createHub(spiral)); }
    if (row.id === 'counter') { marquee = createPrizeMarquee(label(row, '@name')); model.add(marquee); }
    const emi = createEmiIdle({ model, row, atlas });
    if (emi) { emis.push(emi); emiByKey.set(row.key, emi); }
    names.set(row.key, String(label(row, '@name')));
    // 10.22.A: every fixture pays visibly, not just the slot. `status` is the fixture's OWN screen -
    // the label whose registry key is a status line - and it is the win's text channel (Brake 9).
    const status = Object.keys(f.labels).find(node => /_status$/.test(String(f.labels[node]))) || null;
    const host = payoutHost(holder, model, row);
    if (host) payouts.set(row.key, { coins: createCoinShower(host), host, node: status,
      rest: labels.get(row.key + '/' + status)?.text || '', showing: false });
    if(row.id === 'wheel') wheelFaces.push(createWheelFace(holder));
    if(row.id === 'roulette') rouletteSurfaces.push(createRouletteSurfaces(holder));
    holders.set(row.key, holder);
    tick();
    return holder;
  }));

  const venueLights=createVenueLights(holders);
  const prizes = createPrizeDisplay({scene, counter: holders.get('counter'), lex: (key, fallback) => { const v = label(stations.find(r => r.id === 'counter'), key); return v && v !== key ? v : fallback; }});

  // Number bulbs within each fixture circuit so the bright trail follows its physical order.
  const circuits = new Map();
  for (const b of bulbs) {
    const key = b.row.key + '/' + b.mesh.name.replace(/\d+$/, '');
    if (!circuits.has(key)) circuits.set(key, []);
    circuits.get(key).push(b);
  }
  for (const list of circuits.values()) {
    list.sort((a, b) => Number(a.mesh.name.match(/\d+$/)?.[0]) - Number(b.mesh.name.match(/\d+$/)?.[0]));
    list.forEach((b, i) => { b.phase = i / list.length; });
  }

  // ---- bulbs: one InstancedMesh per (station, lamp shape) ----
  // PERF (2026-09-18): keyed on geometry uuid this made one batch PER BULB, because the GLBs give every
  // lamp its own geometry object (25 one-instance batches on the wheel, 28 on the roulette: 53 draw calls
  // for what is two shapes). Lamps of one station with the same vertex and index counts and the same
  // bounding radius are the same authored lamp copied about, so they share a batch and draw once. The
  // original material never mattered here: every batch gets the emissive material below.
  const groups = new Map();
  for (const b of bulbs) {
    const g = b.mesh.geometry;
    if (!g.boundingSphere) g.computeBoundingSphere();
    const k = b.row.id + '|' + (g.attributes.position ? g.attributes.position.count : 0) + '|' + (g.index ? g.index.count : 0) + '|' + (g.boundingSphere ? g.boundingSphere.radius.toFixed(4) : '');
    if (!groups.has(k)) groups.set(k, []);
    groups.get(k).push(b);
  }
  const auras = createAuras(bulbs.length + sconces.length);
  const tmp = new T.Vector3();
  const instanced = [];
  for (const list of groups.values()) {
    const mat = new T.MeshStandardMaterial({ color: 0xffffff, emissive: 0xffffff, emissiveIntensity: .55, roughness: .5, metalness: 0, toneMapped: false });
    patchInstancedEmissive(mat);
    const im = new T.InstancedMesh(list[0].mesh.geometry, mat, list.length);
    im.name = 'bulbs_' + list[0].row.id;
    list.forEach((b, i) => {
      b.mesh.updateWorldMatrix(true, false);
      const matrix=b.mesh.matrixWorld.clone();
      if(b.row.id==='wheel')matrix.scale(new T.Vector3(1.45,1.45,1.45));
      im.setMatrixAt(i, matrix);
      b.slot = i; b.im = im;
      b.aura = auras.add(b.mesh.getWorldPosition(tmp), b.row.id === 'wheel' ? .32 : b.row.id === 'slot' ? 0.19 : 0.23);
      b.mesh.visible = false;
    });
    im.instanceMatrix.needsUpdate = true;
    im.computeBoundingSphere();
    im.userData.rows = list.map((b) => b.row.key);   // a bulb left its fixture for this batch: scene.js maps a tap on instance i back to its station
    scene.add(im);
    instanced.push(im);
  }
  const sconceAuras = sconces.map((s) => auras.add(s.getWorldPosition(tmp), 0.85));
  scene.add(auras.points);

  const c = new T.Color(), target = new T.Color();
  const sconceColor = new T.Color(0xff79ce);
  let lampRippleAt=-Infinity;
  const lampPositions=sconces.map(s=>s.getWorldPosition(new T.Vector3()));
  let lampOrigin=new T.Vector3();
  const wheelColors=[0xff328f,0x963cff,0x29cfff,0x36ffc2,0xffc329,0xff6742].map(hex=>new T.Color(hex));
  function update(dt, t, still, camera = null) {
    if (camera) reelFrustum.setFromProjectionMatrix(reelView.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse));
    venueLights.update(dt,still || readMotion().off || readMotion().reduced);
    marquee?.userData.update?.(dt,still || readMotion().off || readMotion().reduced);
    prizes.update(dt, still || readMotion().off || readMotion().reduced);
    // The echo's own clock. Clamped like the shower's, so a tab left in the background for a minute
    // does not age a win away unseen, and STOPPED with the loop while a station holds the screen.
    clock += Math.min(.05, Math.max(0, dt)) * 1000;
    const gains = echo.gains(clock, still);
    if (!still) {
      const repaint = clock - reelPaintAt >= 125, visible = [];
      for (const r of idleReels) {
        if (!idleReelEligible(r.model)) continue;
        const map = r.reel.material?.map;
        if (!map) continue;
        // Cheap offsets keep their elapsed motion even when art is off camera.
        map.offset.x = (map.offset.x + Math.min(.05, Math.max(0, dt)) * .013) % 1;
        if (visibleInTree(r.reel) && (!camera || reelFrustum.intersectsObject(r.reel))) visible.push(r);
      }
      if (repaint) {
        const indices = new Set(visible.filter(r => r.reel.material.map.image === reelStrips.get(r.index)).map(r => r.index));
        for (const index of indices) reelStrip(index, clock, true);
        reelPaintAt = clock;
      }
      for (const r of visible) {
        const map = r.reel.material.map, version = reelVersions.get(r.index);
        // Each cabinet owns a texture over a shared canvas. Returning cabinets catch up once.
        if (map.image === reelStrips.get(r.index) && r.paintVersion !== version) {
          map.needsUpdate = true; r.paintVersion = version;
        }
      }
    }
    const changedBulbs = new Set();
    for (const b of bulbs) {
      if (!visibleInTree(holders.get(b.row.key))) continue;
      const offset = b.row.variant === 'violet' ? .33 : b.row.variant === 'mint' ? .66 : 0;
      const travel = t / (b.row.id === 'wheel' ? 6 : 8) * (b.rim ? -1 : 1) + offset;
      const wave = Math.pow(.5 + .5 * Math.cos((b.phase - travel) * Math.PI * 2), 8);
      let power = .30 + wave * .65, op = .16 + wave * .36;
      const phase = (t / 12 + b.phase * .5 + offset) % 4, i = Math.floor(phase);
      c.copy(CHASE[i]).lerp(CHASE[(i + 1) % 4], T.MathUtils.smoothstep(phase % 1, 0, 1));
      if(b.row.id==='wheel') {
        const hue=(b.phase*wheelColors.length+(still?0:t*.22))%wheelColors.length,j=Math.floor(hue);
        c.copy(wheelColors[j]).lerp(wheelColors[(j+1)%wheelColors.length],hue-j);
        power=.58+wave*.3;op=.26+wave*.35;
      }
      // THE WALK-BACK (10.22.A): a fixture that just paid keeps its aura hot and leans gold for the
      // length of the echo, so the win is still settling when the player stands up and turns round.
      const gain = gains.size ? gains.get(b.row.key) || 0 : 0;
      if (gain > 0) { c.lerp(WIN_GOLD, Math.min(.85, gain * WIN_LEAN)); power *= 1 + gain * .5; op = Math.min(1, op * (1 + gain)); }
      auras.set(b.aura, c, op);
      b.im.setColorAt(b.slot, c.multiplyScalar(power));
      changedBulbs.add(b.im);
    }
    for (const im of changedBulbs) if (im.instanceColor) im.instanceColor.needsUpdate = true;
    if (sconceMaterial) sconceMaterial.emissiveIntensity = 1.7 + 0.2 * Math.sin(t * 0.5);
    const so = 0.5 + 0.07 * Math.sin(t * 0.5);
    const quiet=still || readMotion().off || readMotion().reduced;
    // A borrowed cabinet's screen breathes grey to colour once every ten seconds or so; quiet holds it half lit.
    const breath = quiet ? .5 : .5 + .5 * Math.sin(t * .6);
    for (const m of glows) { m.emissive.copy(GLOW_GREY).lerp(GLOW_TINT, breath); m.emissiveIntensity = .35 + breath * .65; }
    let lampPeak=0;
    for (let n=0;n<sconceAuras.length;n++) {
      const age=(clock-lampRippleAt-lampPositions[n].distanceTo(lampOrigin)*75)/1100;
      const pulse=!quiet && age>0 && age<1 ? Math.sin(age*Math.PI)**2 : 0;
      lampPeak=Math.max(lampPeak,pulse);
      c.copy(sconceColor).lerp(WIN_GOLD,pulse*.8);
      auras.set(sconceAuras[n],c,Math.min(1,so+pulse*.45));
    }
    if(sconceMaterial)sconceMaterial.emissiveIntensity+=lampPeak*.9;
    auras.commit();
    for (const surface of wheelFaces) surface?.update(dt, still || readMotion().off || readMotion().reduced);
    for (const h of hubs) h.update(dt, still);
    for (const emi of emis) emi.update(dt, still);
    // The fixture's own screen carries the line for the whole echo, not just for as long as coins are
    // falling: the coins are 4 s of decoration, the line is the news, and the news outlives Calm.
    for(const [key,p] of payouts){p.coins.update(dt,still);
      if(holders.get(key)?.userData.slotPlaying || !p.node)continue;
      const line=echo.line(key,clock);
      if(line){setLabel(key,p.node,WIN_MARK+' '+line+' '+WIN_MARK);p.showing=true;
        const m=labels.get(key+'/'+p.node)?.mesh.material;if(m)m.emissive.setHSL(still ? .1 :(clock*.00018)%1,.8,.6);}
      else if(p.showing){p.showing=false;
        // Whatever the screen said before the win says it again - which is not always the boot label:
        // the wheel's own screen carries MUST HIT while the pot has to fall (10.16.E).
        if(!setLabel(key,p.node,p.rest)){const m=labels.get(key+'/'+p.node)?.mesh.material;if(m)m.emissive.set(0xffffff);}}
    }
    // THE FLOOR'S BOARD: a hero anywhere in the room, or the Parlour's own name again.
    if(marquee)marquee.userData.say?.(echo.marquee(clock));
    floorStyle.update(dt, still);
  }

  /**
   * Repaint one fixture label (CONTRACT 10.16.E: the wheel's screen reads MUST
   * HIT while the pot has to fall). A no-op when nothing changed, so it is safe
   * to call on every bell read. Law VII: the caller passes a lexicon string.
   */
  function setLabel(rowKey, node, text) {
    const row = labels.get(rowKey + '/' + node);
    const want = String(text == null ? '' : text).toUpperCase();
    if (!row || !want || row.text === want) return false;
    const t = labelTexture(want);
    const old = row.mesh.material;
    row.mesh.material = new T.MeshStandardMaterial({ map: t, emissiveMap: t, emissive: 0xffffff, emissiveIntensity: 0.5, roughness: 0.5 });
    row.text = want;
    try { if (old.map) old.map.dispose(); old.dispose(); } catch (e) { /* the new one is already on */ }
    return true;
  }

  /**
   * A station announced a paid result (10.22.B). The room spends what win-echo.js hands back and
   * decides nothing of its own: the coins (Law IX's own rung), the fixture's screen (Brake 9's text),
   * its aura for the walk back, the mascot's glance (Law XIII) and, once a visit, the Parlour's board.
   * `true` when the floor heard it at all - a tier 1, a melted win and a miss all pass silently.
   */
  function celebrate(key, amount, tier, text) {
    const p = payouts.get(key);
    // A zero or negative amount is a station saying THIS FIXTURE IS NOT SHOWING A WIN - a new spin, a
    // settle - and what is falling is cleared. A paid result too small to cross the room is NOT that:
    // it simply passes, and it leaves an earlier win still settling exactly where it was (Brake 2).
    if (!(Number(amount) > 0)) { p?.coins.clear(); echo.clear(key); return false; }
    const fired = echo.celebrate({ key, amount, tier, text, name: names.get(key) }, readMotion(), clock);
    if (!fired) return false;
    if(fired.aura>0 && clock-lampRippleAt>1800){lampRippleAt=clock;holders.get(key)?.getWorldPosition(lampOrigin);}
    // What the screen said before the win is what it says after it (10.16.E's MUST HIT, most of all).
    if (p && p.node && !p.showing) p.rest = labels.get(key + '/' + p.node)?.text || p.rest;
    // Law XIII: the fixture's own mascot looks up. Quiet keeps her at rest, the way every other room
    // gesture does (emi-interaction's canGesture), and `aura` is exactly that question already asked.
    if (fired.emi && fired.aura > 0) emiByKey.get(key)?.trigger(fired.emi);
    if (fired.shower > 0 && p) p.coins.start(amount, fired.shower, fired.line);
    return true;
  }

  return { prizes, disposeSurfaces(){
    venueLights.dispose();
    prizes.dispose();
    for(const surface of [...rouletteSurfaces,...wheelFaces])surface?.dispose();
    // The page's one GL context is refcounted; leaving the room without releasing this kit leaks it.
    if (reelKit) { reelKit.dispose(); reelKit = null; }
    for (const strip of reelStrips.values()) strip.width = strip.height = 1;
    reelStrips.clear();
    for(const tile of roomTiles.values())tile.width=tile.height=1; roomTiles.clear();
  }, shell, ceiling, floor, setFloorStyle: floorStyle.setFloorStyle, getFloorStyle: floorStyle.getFloorStyle, screens, holders, fixtures: set.length, bulbs: bulbs.length, update, auras, hubs, labels, setLabel, emis, marquee, payouts, celebrate, echo };
}
