import * as T from 'three';
import { prizeFrame } from '../shared/prize-state.js';

// Borrow approved geometry and textures. Only our signs, stands and material copies are owned here.
export function createPrizeDisplay({ scene, counter, lex }) {
  const resources = new Set(), stamps = new Map(), props = new Map(), flights = new Map();
  const rack = new T.Group(); rack.name = 'owned_expansions'; rack.position.set(3.2, 0, -5.7);
  const cabinet = new T.Group(); cabinet.name = 'unlocked_arcade'; cabinet.position.set(4.1, 0, -5.7);
  const finish = new T.MeshStandardMaterial({ color: '#35213f', metalness: .35, roughness: .4 }); resources.add(finish);
  const brass = new T.MeshStandardMaterial({color:'#b78a48',metalness:.7,roughness:.32}); resources.add(brass);
  function box(w, h, d, x, y, z, material = finish) {
    const g = new T.BoxGeometry(w, h, d); resources.add(g);
    const m = new T.Mesh(g, material); m.position.set(x, y, z); rack.add(m);
  }
  box(.68, 1.9, .06, 0, .95, -.2);
  for (const y of [.12, .65, 1.18, 1.71]) { box(.74, .055, .48, 0, y, 0); box(.74,.018,.022,0,y+.025,.244,brass); }
  for (const x of [-.36,.36]) box(.025,1.82,.025,x,.94,-.15,brass);
  function sign(text, width, height) {
    const c = document.createElement('canvas'); c.width = 768; c.height = 192;
    const x = c.getContext('2d'); x.fillStyle = '#25132e'; x.fillRect(0, 0, 768, 192);
    x.strokeStyle = '#ffd18e'; x.lineWidth = 9; x.strokeRect(8, 8, 752, 176);
    x.fillStyle = '#ffe3b6'; x.font = '700 86px "Segoe UI", sans-serif'; x.textAlign = 'center'; x.textBaseline = 'middle';
    x.fillText(text, 384, 96, 715);
    const map = new T.CanvasTexture(c); map.colorSpace = T.SRGBColorSpace;
    const g = new T.PlaneGeometry(width, height), m = new T.MeshBasicMaterial({ map, side: T.DoubleSide });
    resources.add(map); resources.add(g); resources.add(m);
    const node = new T.Mesh(g, m); node.raycast = () => {}; return node;
  }
  function fitted(source, width, height) {
    const wrapper = new T.Group(), copy = source.clone(true); wrapper.add(copy);
    copy.position.set(0, 0, 0); copy.quaternion.identity(); copy.updateMatrix();
    wrapper.updateMatrixWorld(true);
    const bounds = new T.Box3().setFromObject(copy), size = bounds.getSize(new T.Vector3());
    const scale = Math.min(width / size.x, height / size.y);
    const fit = new T.Group(); fit.add(wrapper); wrapper.scale.setScalar(scale);
    wrapper.position.set(-(bounds.min.x + bounds.max.x) * scale / 2, -bounds.min.y * scale, -(bounds.min.z + bounds.max.z) * scale / 2);
    return fit;
  }
  const sourceCabinet = counter?.getObjectByName('racing_cabinet');
  if (!sourceCabinet) throw new Error('counter.glb lacks preserved racing_cabinet');
  cabinet.add(fitted(sourceCabinet, 1.0, 2.05)); sourceCabinet.visible = false;
  const plate = sign(lex('br_arcade_play_tab', 'Play in CCP: Play tab'), .82, .205); plate.position.set(0, .44, .55); cabinet.add(plate);
  counter.updateMatrixWorld(true);
  for (const id of ['jackpot_remix', 'rt_demo', 'high_roller', 'flashes_v2', 'bubbles_v2', 'rt_bundle_1', 'rt_bundle_2', 'rt_bundle_3']) {
    const source = counter.getObjectByName('shelf_' + id); if (!source) continue;
    const bounds = new T.Box3().setFromObject(source), center = bounds.getCenter(new T.Vector3());
    const stamp = sign(lex('br_counter_sold', 'SOLD'), Math.min(.65, Math.max(.38, bounds.max.x - bounds.min.x + .12)), .17);
    stamp.position.set(center.x, center.y, bounds.max.z + .045); stamp.rotation.z = -.1; stamp.visible = false;
    scene.add(stamp); stamps.set(id, stamp);
    const parent = source.parent, pivot = new T.Group(); parent.add(pivot); pivot.attach(source);
    props.set(id, { pivot, source });
    if (id.startsWith('rt_bundle_')) {
      const display = fitted(source, .59, .46); display.position.y = .68 + (Number(id.at(-1)) - 1) * .53;
      display.name = 'owned_' + id; display.visible = false; rack.add(display);
    }
  }
  const materials = [];
  cabinet.traverse(n => { if (!n.material) return; const list = [].concat(n.material).map(m => {
    const copy = m.clone(); resources.add(copy); materials.push({ m: copy, opacity: m.opacity, transparent: m.transparent, depthWrite: m.depthWrite }); return copy;
  }); n.material = Array.isArray(n.material) ? list : list[0]; });
  scene.add(cabinet, rack); cabinet.visible = rack.visible = false;
  let demo = false, reveal = null;
  function apply(snapshot, bought = null) {
    if (!snapshot) return;
    const owned = new Set(snapshot.owned);
    for (const [id, stamp] of stamps) {
      stamp.visible = owned.has(id) && id !== 'rt_demo';
      if (id === 'rt_demo') props.get(id).source.visible = !snapshot.demo;
      const display = rack.getObjectByName('owned_' + id); if (display) display.visible = owned.has(id);
    }
    if (bought && owned.has(bought) && props.has(bought)) flights.set(bought, 0);
    if (snapshot.demo && !demo) reveal = bought ? 0 : null;
    demo = snapshot.demo; cabinet.visible = rack.visible = demo;
    if (!demo) reveal = null;
    // A repeated state refresh must not restart the reveal.
    update(0, false);
  }
  function update(dt, still) {
    for (const [id, elapsed] of flights) {
      const next = elapsed + dt, f = prizeFrame(next, still), prop = props.get(id), stamp = stamps.get(id);
      prop.pivot.position.y = f.lift; prop.pivot.rotation.y = f.turn;
      if (stamp) stamp.scale.setScalar(f.done ? 1 : 1 + (1 - f.stamp) * .35);
      if (f.done) flights.delete(id); else flights.set(id, next);
    }
    if (reveal === null) {
      cabinet.position.y = 0;
      for (const r of materials) { r.m.opacity=r.opacity; r.m.transparent=r.transparent; r.m.depthWrite=r.depthWrite; }
    }
    if (reveal !== null) {
      reveal += dt; const f = prizeFrame(reveal, still);
      cabinet.position.y = f.done ? 0 : -.1 * (1 - f.alpha);
      for (const r of materials) { r.m.opacity = r.opacity * f.alpha; r.m.transparent = f.done ? r.transparent : true; r.m.depthWrite = f.done ? r.depthWrite : false; }
      if (f.done) reveal = null;
    }
  }
  return { apply, update, cabinet, rack,
    dispose() { for (const s of stamps.values()) s.removeFromParent(); rack.removeFromParent(); cabinet.removeFromParent();
      for (const r of resources) r.dispose(); resources.clear(); flights.clear(); },
  };
}
