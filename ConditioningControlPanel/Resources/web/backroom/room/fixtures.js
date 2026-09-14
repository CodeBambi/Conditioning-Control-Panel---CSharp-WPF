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
 * ==========================================================================*/

import * as T from 'three';
import { createPrizeMarquee } from './prize-marquee.js';
import { createEmiIdle } from './emi-idle.js';
import { createCoinShower } from './coin-shower.js';
import { createFloorStyle } from './floor-style.js';

const BULB = /^(lights_chase_\d|bulb_\d|canopy_bulb_|rim_bulb_)/;
const CHASE = [0xff168e, 0x852bff, 0x00e6b8, 0xff9d08].map((c) => new T.Color(c));

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
  spiral.add(motes);
  let phase = 0;
  return { update(dt, still) { if (!still) phase += dt * 0.35; uniforms.uTime.value = phase; }, setDpr(d) { motes.material.uniforms.uDpr.value = d; } };
}

/**
 * Load and set out the room. `faces` is the EMI face atlas url; `label(row, key)`
 * resolves a registry label key ("@name" = the station's own name).
 */
export async function buildRoom({ scene, loader, stations, base, faces, label, onProgress }) {
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
    if (!o.isMesh) return;
    o.geometry.computeBoundingBox();
    warpBounds.copy(o.geometry.boundingBox).applyMatrix4(o.matrixWorld);
    if(warpBounds.max.x<=6.5 || warpBounds.max.z<=0)return;
    const g=o.geometry.clone(), a=g.attributes.position;
    const positions=new Float32Array(a.count*3), inverse=o.matrixWorld.clone().invert();
    for(let i=0;i<a.count;i++) {
      point.fromBufferAttribute(a,i).applyMatrix4(o.matrixWorld);
      point.x += 1.2*Math.max(0,Math.min(1,(point.x-6.5)/.5))*Math.max(0,point.z)/8;
      point.applyMatrix4(inverse).toArray(positions,i*3);
    }
    g.setAttribute('position',new T.BufferAttribute(positions,3));
    g.computeVertexNormals();g.computeBoundingSphere();o.geometry=g;
  });

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

  const holders = new Map();
  const hubs = [];
  const emis = [];
  const payouts = new Map();
  let marquee = null;
  /** Every fixture label mesh, `rowKey/node` -> { mesh, text }, so one can be repainted later (10.16.E). */
  const labels = new Map();
  const set = await Promise.all(stations.map(async (row) => {
    const f = row.fixture;
    const source = await fetchModel(f.file);
    const model = source.clone(true);   // geometry is shared; the decoded original stays pristine
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
      if (BULB.test(o.name)) bulbs.push({ mesh: o, row, rim: o.name.startsWith('rim'), index: bulbs.length });
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
    if (f.reels) {
      for (let i = 1; i <= 3; i++) {
        const reel = model.getObjectByName('reel_' + i);
        if (!reel || !reel.isMesh) continue;
        const c = document.createElement('canvas');
        c.width = 13 * 128; c.height = 128;
        const x = c.getContext('2d');
        x.fillStyle = '#291635'; x.fillRect(0, 0, c.width, 128);
        for (let k = 0; k < 13; k++) {
          x.save(); x.translate((k + 0.5) * 128, 64); x.rotate(-Math.PI / 2); x.scale(1, -1);
          x.fillStyle = ['#f3a1d1', '#a695e4', '#ebc883'][k % 3]; x.font = '70px system-ui';
          x.textAlign = 'center'; x.textBaseline = 'middle'; x.fillText(['✦', '♡', '◎'][(k + i) % 3], 0, 0); x.restore();
        }
        const t = new T.CanvasTexture(c);
        t.colorSpace = T.SRGBColorSpace; t.flipY = false; t.wrapS = T.RepeatWrapping;
        t.offset.x = (i + 0.5) / 13;   // a resting stop, by texture, never by node
        reel.material = new T.MeshBasicMaterial({ map: t });
      }
    }
    if (f.hub) { const spiral = model.getObjectByName('center_spiral'); if (spiral) hubs.push(createHub(spiral)); }
    if (row.id === 'counter') { marquee = createPrizeMarquee(label(row, '@name')); model.add(marquee); }
    const emi = createEmiIdle({ model, row, atlas });
    if (emi) emis.push(emi);
    if(row.id==='slot') payouts.set(row.key,{coins:createCoinShower(model),rest:labels.get(row.key+'/screen_status')?.text||'',showing:false});
    holders.set(row.key, holder);
    tick();
    return holder;
  }));

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

  // ---- bulbs: one InstancedMesh per (geometry, material) ----
  const groups = new Map();
  for (const b of bulbs) {
    const k = b.mesh.geometry.uuid + '|' + b.mesh.material.uuid;
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
      im.setMatrixAt(i, b.mesh.matrixWorld);
      b.slot = i; b.im = im;
      b.aura = auras.add(b.mesh.getWorldPosition(tmp), b.row.id === 'slot' ? 0.19 : 0.23);
      b.mesh.visible = false;
    });
    im.instanceMatrix.needsUpdate = true;
    im.computeBoundingSphere();
    scene.add(im);
    instanced.push(im);
  }
  const sconceAuras = sconces.map((s) => auras.add(s.getWorldPosition(tmp), 0.85));
  scene.add(auras.points);

  const c = new T.Color(), target = new T.Color();
  const sconceColor = new T.Color(0xff79ce);
  function update(dt, t, still) {
    for (const b of bulbs) {
      const offset = b.row.variant === 'violet' ? .33 : b.row.variant === 'mint' ? .66 : 0;
      const travel = t / (b.row.id === 'wheel' ? 6 : 8) * (b.rim ? -1 : 1) + offset;
      const wave = Math.pow(.5 + .5 * Math.cos((b.phase - travel) * Math.PI * 2), 8);
      const power = .30 + wave * .65, op = .16 + wave * .36;
      const phase = (t / 12 + b.phase * .5 + offset) % 4, i = Math.floor(phase);
      c.copy(CHASE[i]).lerp(CHASE[(i + 1) % 4], T.MathUtils.smoothstep(phase % 1, 0, 1));
      auras.set(b.aura, c, op);
      b.im.setColorAt(b.slot, c.multiplyScalar(power));
    }
    for (const im of instanced) if (im.instanceColor) im.instanceColor.needsUpdate = true;
    if (sconceMaterial) sconceMaterial.emissiveIntensity = 1.7 + 0.2 * Math.sin(t * 0.5);
    const so = 0.5 + 0.07 * Math.sin(t * 0.5);
    for (const i of sconceAuras) auras.set(i, sconceColor, so);
    auras.commit();
    for (const h of hubs) h.update(dt, still);
    for (const emi of emis) emi.update(dt, still);
    for(const [key,p] of payouts){p.coins.update(dt,still);const d=p.coins.debug();
      if(d.active){setLabel(key,'screen_status','✦ '+d.label+' ✦');p.showing=true;
        const m=labels.get(key+'/screen_status')?.mesh.material;if(m)m.emissive.setHSL(still ? .1 :(d.age*.18)%1,.8,.6);}
      else if(p.showing){setLabel(key,'screen_status',p.rest);p.showing=false;}
    }
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

  return { shell, ceiling, floor, setFloorStyle: floorStyle.setFloorStyle, getFloorStyle: floorStyle.getFloorStyle, screens, holders, fixtures: set.length, bulbs: bulbs.length, update, auras, hubs, labels, setLabel, emis, marquee, payouts, celebrate(key,amount,tier,text){const p=payouts.get(key);if(!p)return false;if(amount<=0){p.coins.clear();return false;}return p.coins.start(amount,tier,text);} };
}
