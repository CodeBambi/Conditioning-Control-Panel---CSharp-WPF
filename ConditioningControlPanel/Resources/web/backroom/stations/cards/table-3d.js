// Soft Hand's room view. The station owns all cards, steps, locks, moments and SP.
// Geometry follows authored anchors; this view never opens a renderer or moves the camera.
import * as T from 'three';
import { cardsNodes, slotName, validCardSlot } from '../../room/nodes-cards.js';
import { TIMING, fanCard, lampBreath } from './feel.js';
import { DECK_VALUES } from '../../shared/hypno/media.js';
import { createCardFace } from './card-face.js';

const clamp = (v) => Math.max(0, Math.min(1, v));
const ease = (p) => 1 - (1 - p) ** 3;

export function createTable3D(stage, { kit, onDeal } = {}) {
  const nodes = cardsNodes(stage.fixture), group = new T.Group();
  group.name = 'cards_runtime'; stage.scene.add(group);
  const authored = [], cards = [], fans = [], sparks = [], betChips = [];
  let active = -1, hands = 1, fan = null, disposed = false, now = 0, lastPaint = -Infinity;
  const point = (name) => nodes[name].getWorldPosition(new T.Vector3());
  const shoe = point('deck_shoe_mouth');
  const width = Number(nodes.card_slot_p0_0.userData.card_width), height = Number(nodes.card_slot_p0_0.userData.card_height);
  if (!(width > 0 && height > 0)) { group.removeFromParent(); throw new Error('Card anchors require card_width/card_height'); }
  const size = nodes.card_slot_p0_0.getWorldScale(new T.Vector3());
  const geometry = new T.PlaneGeometry(width * size.x, height * size.z);
  const normal = new T.Vector3(0, 1, 0);
  const basis = nodes.card_slot_p0_0.getWorldQuaternion(new T.Quaternion());
  normal.applyQuaternion(basis);
  const base = basis.clone().multiply(new T.Quaternion().setFromAxisAngle(new T.Vector3(1, 0, 0), -Math.PI / 2));
  stage.fixture.traverse((n) => {
    if (/^(player_card_\d|community_card_\d|player_chip(?:\.\d+)?)$/.test(n.name)) { authored.push([n, n.visible]); n.visible = false; }
  });
  const lamp = new T.PointLight('#ffdbbc', 0, 3 * size.x, 2); lamp.position.copy(point('table_lamp')); group.add(lamp);
  function makeCard(c) {
    const face = createCardFace(), material = new T.MeshBasicMaterial({ map: face.texture, side: T.DoubleSide, transparent: true, toneMapped: false });
    const mesh = new T.Mesh(geometry, material); mesh.quaternion.copy(base); mesh.renderOrder = 2; group.add(mesh);
    return { ...c, mesh, material, face, landed: !!c.settled, faceAt: c.settled && c.code ? c.bornAt - TIMING.flipMs : null };
  }
  function drop(c) { c.mesh.removeFromParent(); c.material.dispose(); c.face.dispose(); }
  function target(c) { return point(slotName(c.owner, c.slot)); }
  function setPose(c, at, flip = 1, alpha = 1) {
    c.mesh.position.copy(at); c.mesh.quaternion.copy(base); c.mesh.scale.set(Math.max(.02, Math.abs(Math.cos(Math.PI * flip))), 1, 1);
    c.material.opacity = alpha;
  }
  const flipAt = (c, time, still) => c.faceAt == null ? 0 : still ? 1 : clamp((time - c.faceAt) / TIMING.flipMs);
  const off = stage.register({ dispose: release });
  function release() {
    if (disposed) return; disposed = true;
    stage.canvas.removeEventListener('pointerdown', pickShoe);
    for (const c of [...cards, ...fans]) drop(c);
    betChips.forEach((m) => { m.geometry.dispose(); m.material.dispose(); });
    for (const s of sparks) { s.mesh.geometry.dispose(); s.mesh.material.dispose(); }
    geometry.dispose(); group.removeFromParent();
    authored.forEach(([n, visible]) => { n.visible = visible; });
  }
  function pickShoe(e) {
    if (e.button !== 0 || !onDeal || !stage.pick(e, [nodes.deck_shoe_base]).length) return;
    e.preventDefault(); onDeal(); // Every path enters the same guarded action.
  }
  stage.canvas.addEventListener('pointerdown', pickShoe);
  function effect(kind, at, count = 1, dir = 1) {
    for (let i = 0; i < count; i++) {
      const material = new T.MeshBasicMaterial({ color: dir > 0 ? '#5fffd0' : '#ff5fa2', transparent: true, opacity: 0, depthWrite: false, side: T.DoubleSide });
      const g = kind === 'chip' ? new T.CylinderGeometry(width * .28, width * .28, width * .06, 12) : new T.RingGeometry(width * .65, width * .7, 32);
      const mesh = new T.Mesh(g, material); group.add(mesh);
      if (kind !== 'chip') mesh.quaternion.copy(base);
      sparks.push({ mesh, kind, at: at + i * 160, dir });
    }
  }
  const api = {
    clear() { cards.splice(0).forEach(drop); hands = 1; active = -1; },
    // Ignore malformed cards without inventing a placement or stopping the station.
    addCard(c, time) { if (!validCardSlot(c.owner, c.slot)) return false; cards.push(makeCard({ ...c, code: c.code || null, bornAt: time })); return true; },
    split() { const c = cards.find((x) => x.owner === 0 && x.slot === 1); if (c) { c.owner = 1; c.slot = 0; } hands = 2; },
    reveal(code, time, settled = false) { const c = cards.find((x) => x.owner === 'd' && x.slot === 1); if (c) { c.code = code; c.faceAt = settled ? time - TIMING.flipMs : time; } },
    setActive(i) { active = i; },
    setBets(list) {
      for (const m of betChips.splice(0)) { m.removeFromParent(); m.geometry.dispose(); m.material.dispose(); }
      list.forEach((n, i) => { for (let j = 0; j < n; j++) {
        const m = new T.Mesh(new T.CylinderGeometry(width * .28, width * .28, width * .055, 12), new T.MeshBasicMaterial({ color: '#ff5fa2' }));
        m.position.copy(point('bet_spot_' + i)).addScaledVector(normal, j * width * .07); m.quaternion.copy(basis); group.add(m); betChips.push(m);
      } });
    },
    startFan(time, still) {
      fans.splice(0).forEach(drop); fan = { at: time, still };
      for (let i = 0; i < 13; i++) fans.push(makeCard({ code: DECK_VALUES[i] + (i % 2 ? 'h' : 's'), bornAt: time }));
    },
    fanDone(time) { return !fan || time - fan.at >= (fan.still ? TIMING.sitStillMs : TIMING.sitMs); },
    vortex(dir, count, time) { if (dir < 0) betChips.forEach((m) => { m.visible = false; }); effect('chip', time, count, dir); },
    tunnel(time) { effect('tunnel', time, 7); },
    glowCard(owner, slot, time) { const c = cards.find((c) => c.owner === owner && c.slot === slot); if (c) c.glow = time; },
    cardRect(owner, slot) {
      const c = cards.find((c) => c.owner === owner && c.slot === slot); if (!c) return null;
      const p = target(c), rect = stage.canvas.getBoundingClientRect(), corners = [];
      for (const x of [-.5, .5]) for (const y of [-.5, .5]) {
        const v = new T.Vector3(x * width * size.x, y * height * size.z, 0).applyQuaternion(base).add(p).project(stage.camera);
        corners.push([(v.x + 1) * rect.width / 2, (1 - v.y) * rect.height / 2]);
      }
      const xs = corners.map((c) => c[0]), ys = corners.map((c) => c[1]);
      return { x: Math.min(...xs), y: Math.min(...ys), w: Math.max(...xs) - Math.min(...xs), h: Math.max(...ys) - Math.min(...ys) };
    },
    settled(time, still) { return cards.every((c) => c.landed && flipAt(c, time, still) >= (c.code ? 1 : 0)); },
    draw(o) {
      if (disposed) return; now = o.now;
      // Freeze any in-flight gesture on a settings transition; never replay it later.
      if (o.still) { for (const c of cards) { c.landed = true; if (c.code) c.faceAt = now - TIMING.flipMs; } if (fan) fan.still = true; }
      const repaint = now - lastPaint >= 1000 / 15;
      if (repaint) lastPaint = now;
      lamp.intensity = (o.still ? .35 : lampBreath(now, false)) * o.k * .7;
      for (const c of cards) {
        const end = target(c), p = c.landed ? 1 : clamp((now - c.bornAt) / TIMING.flyMs), pos = shoe.clone().lerp(end, ease(p));
        if (!o.still) pos.addScaledVector(normal, Math.sin(p * Math.PI) * height * o.k);
        if (p === 1 && !c.landed) { c.landed = true; if (c.code) c.faceAt = now; if (o.full) effect('ripple', now); }
        const flip = flipAt(c, now, o.still); setPose(c, pos, flip);
        c.material.color.set(c.glow && now - c.glow < TIMING.glowMs ? '#ffb2d0' : '#ffffff');
        if (repaint) c.face.paint(c.code, flip >= .5, o, typeof kit === 'function' ? kit() : kit);
      }
      if (fan && api.fanDone(now)) { fan = null; fans.splice(0).forEach(drop); }
      if (fan) fans.forEach((c, i) => {
        const f = fanCard(i, now - fan.at, fan.still), a = point('card_slot_p1_0'), b = point('card_slot_p0_5');
        const end = a.lerp(b, i / 12).addScaledVector(normal, height * .6), pos = shoe.clone().lerp(end, ease(f.q > 0 ? 1 - f.q : f.p));
        c.mesh.visible = f.visible; setPose(c, fan.still ? end : pos, f.flip, f.alpha); c.mesh.scale.multiplyScalar(.65);
        if (repaint) c.face.paint(c.code, f.flip >= .5, o, typeof kit === 'function' ? kit() : kit);
      });
      for (let i = sparks.length - 1; i >= 0; i--) {
        const s = sparks[i], duration = s.kind === 'chip' ? TIMING.vortexMs : TIMING.tunnelMs, p = (now - s.at) / duration;
        if (p > 1) { s.mesh.removeFromParent(); s.mesh.geometry.dispose(); s.mesh.material.dispose(); sparks.splice(i, 1); continue; }
        s.mesh.visible = p >= 0; if (p < 0) continue;
        s.still ||= o.still;
        const at = point('bet_spot_0'), u = s.still ? (s.dir > 0 ? 1 : 0) : s.dir > 0 ? ease(p) : 1 - ease(p);
        if (s.kind === 'chip') {
          const to = point('card_slot_dealer_2'), dist = at.distanceTo(to), radius = dist * (1 - u) * .4, angle = u * Math.PI * 2.5;
          s.mesh.position.copy(at).lerp(to, 1 - u).add(new T.Vector3(Math.cos(angle) * radius, .015, Math.sin(angle) * radius).applyQuaternion(basis));
        } else { s.mesh.position.copy(at).addScaledVector(normal, .008); s.mesh.scale.setScalar(1 + (s.still ? .5 : p) * 8); }
        s.mesh.material.opacity = Math.sin(p * Math.PI) * .4 * o.k;
      }
    },
    debug() { return { kind: 'room3d', cards: cards.map((c) => ({ owner: c.owner, slot: c.slot, code: c.code, landed: c.landed, face: c.faceAt != null })), hands, active, fan: !!fan, fanCards: fans.length, chips: sparks.filter((s) => s.kind === 'chip').length, frames: now, disposed }; },
    dispose() { off(); release(); },
  };
  group.userData.debug = api.debug;
  return api;
}
