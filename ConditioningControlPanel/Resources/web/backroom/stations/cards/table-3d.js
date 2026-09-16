// Soft Hand's room view. The station owns all cards, steps, locks, moments and SP.
// Large cards use authored origin/dimensions and the same hand layout as the seating camera.
import { cardTilt, landing, flipLift, deckRecoil, touchdown } from './juice.js';
import * as T from 'three';
import { cardsNodes, validCardSlot } from '../../room/nodes-cards.js';
import { TIMING, fanCard, lampBreath } from './feel.js';
import { DECK_VALUES } from '../../shared/hypno/media.js';
import { cardLayout, cardCounts } from './layout-3d.js';
import { createCardFace } from './card-face.js';
import { HIGHLIGHT_MS, HIGHLIGHT_GAP_MS } from '../../shared/hypno/callout.js';

const clamp = (v) => Math.max(0, Math.min(1, v));
const ease = (p) => 1 - (1 - p) ** 3;

export function createTable3D(stage, { kit, onDeal, onCue = () => {} } = {}) {
  const nodes = cardsNodes(stage.fixture), group = new T.Group();
  group.name = 'cards_runtime'; stage.scene.add(group);
  const authored = [], cards = [], fans = [], sparks = [], betChips = [];
  let active = -1, hands = 1, fan = null, disposed = false, now = 0, lastPaint = -Infinity, frameStep=1;
  const point = (name) => nodes[name].getWorldPosition(new T.Vector3());
  const shoe = point('deck_shoe_mouth');
  let deckAt = -Infinity;
  const width = Number(nodes.card_slot_p0_0.userData.card_width), height = Number(nodes.card_slot_p0_0.userData.card_height);
  if (!(width > 0 && height > 0)) { group.removeFromParent(); throw new Error('Card anchors require card_width/card_height'); }
  const size = nodes.card_slot_p0_0.getWorldScale(new T.Vector3());
  let layout=cardLayout(stage.fixture);
  const geometry = new T.PlaneGeometry(layout.width, layout.height);
  // THE GLYPH HIT (callout.js): a mint rim just under a winning card, additive, pulsing with a 1.06 pop over HIGHLIGHT_MS.
  const rimGeometry = new T.PlaneGeometry(layout.width * 1.12, layout.height * 1.1);
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
    const shadow = new T.Mesh(geometry, new T.MeshBasicMaterial({ color: '#031014', transparent: true, opacity: .22, depthWrite: false, side: T.DoubleSide }));
    shadow.quaternion.copy(base); shadow.renderOrder = 1; group.add(shadow);
    return { ...c, mesh, material, face, shadow, quiet: !!c.settled, touchdown: !!c.settled, landed: !!c.settled, faceAt: c.settled && c.code ? c.bornAt - TIMING.flipMs : null };
  }
  function drop(c) { c.shadow.removeFromParent(); c.shadow.material.dispose(); c.mesh.removeFromParent(); c.material.dispose(); c.face.dispose(); if (c.rim) { c.rim.removeFromParent(); c.rim.material.dispose(); c.rim = null; } }
  function rimFor(c) {
    if (c.rim) return c.rim;
    const m = new T.Mesh(rimGeometry, new T.MeshBasicMaterial({ color: '#5fffd0', transparent: true, opacity: 0, depthWrite: false, blending: T.AdditiveBlending, side: T.DoubleSide }));
    m.quaternion.copy(base); m.renderOrder = 1; m.visible = false; group.add(m); c.rim = m; return m;
  }
  const hitPulse = (c, time) => { if (c.hit == null) return 0; const q = (time - c.hit) / HIGHLIGHT_MS; return q >= 0 && q < 1 ? Math.sin(q * Math.PI) : 0; };
  function target(c) { return layout.point(c.owner,c.slot); }
  function setPose(c, at, flip = 1, alpha = 1) {
    c.mesh.position.copy(at); c.mesh.quaternion.copy(base); c.mesh.scale.set(Math.max(.02, Math.abs(Math.cos(Math.PI * flip))), 1, 1);
    c.material.opacity = alpha;
  }
  const flipAt = (c, time, still) => c.faceAt == null ? 0 : still ? 1 : clamp((time - c.faceAt) / TIMING.flipMs);
  // Animate the authored deck cards, retaining their exact transforms for teardown.
  const stack = [];
  stage.fixture.traverse((m) => { if (/^deck_card_[0-4]$/.test(m.name)) stack.push({ m, position: m.position.clone(), rotation: m.quaternion.clone() }); });
  const off = stage.register({ dispose: release });
  function release() {
    if (disposed) return; disposed = true;
    stage.canvas.removeEventListener('pointerdown', pickShoe);
    for (const c of [...cards, ...fans]) drop(c);
    betChips.forEach((m) => { m.geometry.dispose(); m.material.dispose(); });
    for (const s of sparks) { s.mesh.geometry.dispose(); s.mesh.material.dispose(); }
    stack.forEach(({ m, position, rotation }) => { m.position.copy(position); m.quaternion.copy(rotation); });
    geometry.dispose(); rimGeometry.dispose(); group.removeFromParent();
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
    clear() { deckAt = -Infinity; cards.splice(0).forEach(drop); hands = 1; active = -1; },
    // Ignore malformed cards without inventing a placement or stopping the station.
    addCard(c, time) { if (!validCardSlot(c.owner, c.slot)) return false; if (!c.settled) deckAt = time; cards.push(makeCard({ ...c, code: c.code || null, bornAt: time })); return true; },
    // Never replay a touchdown or fan-square after the app returns from suspension.
    skip(time) {
      for (const c of cards) {
        c.quiet = c.touchdown = c.landed = true; c.landAt = -Infinity;
        if (c.code) c.faceAt = time - TIMING.flipMs;
        c.rest = target(c); setPose(c, c.rest, c.code ? 1 : 0); c.hit = c.glow = null;
        c.shadow.position.copy(c.rest).addScaledVector(normal, -.001);
        if (c.rim) c.rim.visible = false;
      }
      fan = null; fans.splice(0).forEach(drop); deckAt = -Infinity;
      stack.forEach(({ m, position, rotation }) => { m.position.copy(position); m.quaternion.copy(rotation); });
      for (const s of sparks.splice(0)) { s.mesh.removeFromParent(); s.mesh.geometry.dispose(); s.mesh.material.dispose(); }
    },
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
    /** THE GLYPH HIT: the cards in `list` ([{ owner, slot }]) glow in turn from `time`, HIGHLIGHT_GAP_MS apart. */
    hitCards(list, time) {
      for (const c of cards) c.hit = null;
      (Array.isArray(list) ? list : []).forEach((h, i) => { const c = cards.find((x) => x.owner === h.owner && x.slot === h.slot); if (c) c.hit = time + i * HIGHLIGHT_GAP_MS; });
    },
    cardRect(owner, slot) {
      const c = cards.find((c) => c.owner === owner && c.slot === slot); if (!c) return null;
      const p = target(c), rect = stage.canvas.getBoundingClientRect(), corners = [];
      for (const x of [-.5, .5]) for (const y of [-.5, .5]) {
        const v = new T.Vector3(x * layout.width, y * layout.height, 0).applyQuaternion(base).add(p).project(stage.camera);
        corners.push([(v.x + 1) * rect.width / 2, (1 - v.y) * rect.height / 2]);
      }
      const xs = corners.map((c) => c[0]), ys = corners.map((c) => c[1]);
      return { x: Math.min(...xs), y: Math.min(...ys), w: Math.max(...xs) - Math.min(...xs), h: Math.max(...ys) - Math.min(...ys) };
    },
    // THE POT: the authored bet spot (both of them on a split), projected to the same canvas space cardRect
    // uses, so THE BANK's tokens leave the felt where the chips are and not from a guessed screen point.
    potRect() {
      const p = point('bet_spot_0');
      if (hands === 2) p.lerp(point('bet_spot_1'), .5);
      const v = p.project(stage.camera), rect = stage.canvas.getBoundingClientRect(), r = layout.width * .5;
      const q = new T.Vector3(r, 0, 0).applyQuaternion(base).add(point('bet_spot_0')).project(stage.camera);
      const w = Math.max(12, Math.abs(q.x - v.x) * rect.width);
      return { x: (v.x + 1) * rect.width / 2 - w / 2, y: (1 - v.y) * rect.height / 2 - w / 2, w, h: w };
    },
    settled(time, still) { return cards.every((c) => c.landed && flipAt(c, time, still) >= (c.code ? 1 : 0)); },
    draw(o) {
      if (disposed) return; frameStep=1-Math.exp(-Math.max(0,o.now-now)/90);now = o.now;
      layout=cardLayout(stage.fixture,hands,cardCounts(cards));
      // Freeze any in-flight gesture on a settings transition; never replay it later.
      if (o.still) { for (const c of cards) { if (!c.landed) touchdown(c, now, true, onCue); c.landed = true; if (c.code) c.faceAt = now - TIMING.flipMs; } if (fan) fan.still = true; }
      const repaint = now - lastPaint >= 1000 / 15;
      if (repaint) lastPaint = now;
      lamp.intensity = (o.still ? .35 : lampBreath(now, false)) * o.k * .7;
      const recoil = deckRecoil(now - deckAt, o.still) * o.k;
      stack.forEach(({ m, position, rotation }, i) => { m.position.copy(position); m.position.y += recoil * .006 * (i + 1); m.position.z += recoil * .012 * i; m.quaternion.copy(rotation); m.rotateY(recoil * .015 * i); });
      for (const c of cards) {
        const end = target(c), p = c.landed ? 1 : clamp((now - c.bornAt) / TIMING.flyMs), pos = shoe.clone().lerp(end, ease(p));
        if(c.landed){c.rest ||=end.clone();c.rest.lerp(end,o.still?1:frameStep);pos.copy(c.rest);}
        if (!o.still) pos.addScaledVector(normal, Math.sin(p * Math.PI) * height * o.k);
        if (p === 1 && !c.landed) { touchdown(c, now, false, onCue); c.landed = true; if (c.code) c.faceAt = now; if (o.full) effect('ripple', now); }
        const flip = flipAt(c, now, o.still), lift = flipLift(flip, o.still) * height * o.k;
        c.shadow.position.copy(pos).addScaledVector(normal, -.001); c.shadow.material.opacity = .22 - flipLift(flip, o.still) * .4;
        pos.addScaledVector(normal, lift);
        pos.add(new T.Vector3(landing(now - (c.landAt ?? -Infinity), o.still) * width * .1 * o.k, 0, 0).applyQuaternion(basis));
        setPose(c, pos, flip); c.mesh.rotateZ(o.still ? 0 : cardTilt(c.owner, c.slot) * o.k);
        const pulse = hitPulse(c, now);
        if (pulse > 0 || c.rim) {
          const rim = rimFor(c); rim.visible = pulse > 0;
          if (pulse > 0) { c.mesh.scale.multiplyScalar(1 + 0.06 * pulse); rim.position.copy(pos).addScaledVector(normal, -0.002); rim.scale.setScalar(1 + 0.06 * pulse); rim.material.opacity = 0.6 * pulse * o.k; }
        }
        c.material.color.set(c.glow && now - c.glow < TIMING.glowMs ? '#ffb2d0' : '#ffffff');
        if (repaint) c.face.paint(c.code, flip >= .5, o, typeof kit === 'function' ? kit() : kit);
      }
      if (fan && api.fanDone(now)) { fan = null; fans.splice(0).forEach(drop); onCue('deck-square'); }
      if (fan) fans.forEach((c, i) => {
        const f = fanCard(i, now - fan.at, fan.still), a = point('card_slot_p1_0'), b = point('card_slot_p0_5');
        const end = a.lerp(b, i / 12).addScaledVector(normal, height * .6), pos = shoe.clone().lerp(end, ease(f.q > 0 ? 1 - f.q : f.p));
        c.shadow.visible = false; c.mesh.visible = f.visible; setPose(c, fan.still ? end : pos, f.flip, f.alpha); c.mesh.scale.multiplyScalar(.65);
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
    debug() { return { kind: 'room3d', cards: cards.map((c) => ({ owner: c.owner, slot: c.slot, code: c.code, landed: c.landed, face: c.faceAt != null })), hands, active, fan: !!fan, fanCards: fans.length, chips: sparks.filter((s) => s.kind === 'chip').length, frames: now, disposed,
      hits: cards.filter((c) => c.hit != null).map((c) => ({ owner: c.owner, slot: c.slot, t0: Math.round(c.hit) })) }; },
    dispose() { off(); release(); },
  };
  group.userData.debug = api.debug;
  return api;
}
