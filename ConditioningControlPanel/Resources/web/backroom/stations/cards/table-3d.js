// Soft Hand's room view. The station owns all cards, steps, locks, moments and SP.
// Large cards use authored origin/dimensions and the same hand layout as the seating camera.
import { totalOf } from './hand.js';
import { cardTilt, landing, flipLift, deckRecoil, touchdown } from './juice.js';
import * as T from 'three';
import { cardsNodes, validCardSlot } from '../../room/nodes-cards.js';
import { TIMING, fanCard, lampBreath } from './feel.js';
import { DECK_VALUES } from '../../shared/hypno/media.js';
import { cardLayout, cardCounts } from './layout-3d.js';
import { createCardFace } from './card-face.js';
import { planSlip, wornCode, slipShake, SLIP_MS } from './glitch.js';
import { HIGHLIGHT_MS, HIGHLIGHT_GAP_MS } from '../../shared/hypno/callout.js';

const clamp = (v) => Math.max(0, Math.min(1, v));
const ease = (p) => 1 - (1 - p) ** 3;

export function createTable3D(stage, { kit, onDeal, onCue = () => {} } = {}) {
  const nodes = cardsNodes(stage.fixture), group = new T.Group();
  group.name = 'cards_runtime'; stage.scene.add(group);
  const authored = [], cards = [], departing = [], fans = [], sparks = [], betChips = [];
  const squared = new Set();
  let active = -1, hands = 1, fan = null, disposed = false, now = 0, lastPaint = -Infinity, frameStep=1;
  let seq = 0, slip = null, slipAt = 0;   // THE SLIP (glitch.js): paint-time only, never written to a code
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
    if (/^(player_card_\d|community_card_\d|player_chip.*)$/.test(n.name)) { authored.push([n, n.visible]); n.visible = false; }
  });
  // The asset batches all chip rim inserts into one mesh. Remove only triangles
  // beside the hidden player chips, preserving the dealer rack and original asset.
  const rimRestores = [], playerChips = authored.filter(([n]) => n.name.startsWith('player_chip')).map(([n]) => ({ p:n.getWorldPosition(new T.Vector3()), r:n.getWorldScale(new T.Vector3()).x * 1.3 }));
  stage.fixture.traverse(n => {
    if (n.name !== 'chip_edge_inserts' || !n.geometry || !playerChips.length) return;
    const original = n.geometry, g = original.clone(), pos = g.attributes.position, index = g.index, kept = [];
    for (let i=0;i<(index ? index.count : pos.count);i+=3) {
      const ids = [0,1,2].map(k=>index ? index.getX(i+k) : i+k), c = new T.Vector3();
      ids.forEach(id=>c.add(new T.Vector3().fromBufferAttribute(pos,id))); c.divideScalar(3).applyMatrix4(n.matrixWorld);
      if (!playerChips.some(chip=>c.distanceTo(chip.p)<chip.r)) kept.push(...ids);
    }
    g.setIndex(kept); n.geometry=g; rimRestores.push([n,original,g]);
  });
  const lamp = new T.PointLight('#ffdbbc', 0, 3 * size.x, 2); lamp.position.copy(point('table_lamp')); group.add(lamp);
  function makeCard(c) {
    const face = createCardFace(), material = new T.MeshBasicMaterial({ map: face.texture, side: T.DoubleSide, transparent: true, toneMapped: false });
    const mesh = new T.Mesh(geometry, material); mesh.quaternion.copy(base); mesh.renderOrder = 2; group.add(mesh);
    const shadow = new T.Mesh(geometry, new T.MeshBasicMaterial({ color: '#031014', transparent: true, opacity: .22, depthWrite: false, side: T.DoubleSide }));
    shadow.quaternion.copy(base); shadow.renderOrder = 1; group.add(shadow);
    return { ...c, id: ++seq, mesh, material, face, shadow, quiet: !!c.settled, touchdown: !!c.settled, landed: !!c.settled, faceAt: c.settled && c.code ? c.bornAt - TIMING.flipMs : null };
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
    for (const c of [...cards, ...departing, ...fans]) drop(c);
    betChips.forEach((m) => { m.geometry.dispose(); m.material.dispose(); });
    for (const s of sparks) { s.mesh.geometry.dispose(); s.mesh.material.dispose(); }
    stack.forEach(({ m, position, rotation }) => { m.position.copy(position); m.quaternion.copy(rotation); });
    rimRestores.forEach(([n,original,g])=>{n.geometry=original;g.dispose();});
    geometry.dispose(); rimGeometry.dispose(); chipTexture.dispose(); group.removeFromParent();
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
  const chipFace = document.createElement('canvas'); chipFace.width = chipFace.height = 128;
  const ink = chipFace.getContext('2d'); ink.fillStyle='#e65b9b'; ink.fillRect(0,0,128,128);
  ink.strokeStyle='#ffe6f2'; ink.lineWidth=5; ink.beginPath(); ink.arc(64,64,44,0,Math.PI*2); ink.stroke();
  ink.lineWidth=12; for(let i=0;i<8;i++){ink.beginPath();ink.arc(64,64,59,i*Math.PI/4,i*Math.PI/4+.18);ink.stroke();}
  ink.fillStyle='#fff0f7'; ink.font='bold 46px sans-serif'; ink.textAlign='center'; ink.textBaseline='middle'; ink.fillText('\u2726',64,66);
  const chipTexture=new T.CanvasTexture(chipFace); chipTexture.colorSpace=T.SRGBColorSpace;
  // Reserve space for all six cards, not just the opening pair. Split stakes sit above their own hand.
  const betPoint = (i = 0) => {
    const phone=stage.canvas.getBoundingClientRect().width<=800;
    const center=layout.point(i,0).lerp(layout.point(i,Math.max(1,(cardCounts(cards)[i]||2)-1)),.5);
    return center.add(new T.Vector3(hands===2||phone?0:layout.width*2.65,0,
      hands===2?-layout.height*1.14:phone?layout.height*.82:0).applyQuaternion(basis));
  };
  function removeChip(m) { m.removeFromParent(); m.geometry.dispose(); m.material.dispose(); }
  function makeChip(i,j,n,time,quiet) {
    const m = new T.Mesh(new T.CylinderGeometry(width*.28,width*.28,width*.055,16),
      new T.MeshBasicMaterial({map:chipTexture,toneMapped:false,transparent:true}));
    m.userData.bet={i,j,n,born:quiet?-Infinity:time,landed:quiet};
    m.quaternion.copy(basis); group.add(m); betChips.push(m); return m;
  }
  const api = {
    clear(time = now, animate = false) {
      departing.splice(0).forEach(drop);
      const old = cards.splice(0);
      if (animate && old.length) {
        old.forEach((c, i) => { c.exitAt = time + (i % 6) * 14; c.exitFrom = c.mesh.position.clone(); if(c.rim)c.rim.visible=false; departing.push(c); });
        onCue('card-slide');
      } else old.forEach(drop);
      deckAt = -Infinity; squared.clear(); hands = 1; active = -1; group.userData.bets=null; slip = null; slipAt = 0; },
    /** THE SLIP, the 3D table's half of table.js `armSlip`. Same seed, same answer. */
    armSlip(seed, time) {
      slip = planSlip(seed, cards.filter((c) => c.code && c.faceAt != null).map((c) => ({ id: c.id, code: c.code })));
      slipAt = time;
      return slip;
    },
    slipped() { return slip; },
    // Ignore malformed cards without inventing a placement or stopping the station.
    addCard(c, time) { if (!validCardSlot(c.owner, c.slot)) return false; if (!c.settled) deckAt = time; cards.push(makeCard({ ...c, code: c.code || null, bornAt: time })); return true; },
    // Never replay a touchdown or fan-square after the app returns from suspension.
    skip(time) {
      departing.splice(0).forEach(drop);
      for (const c of cards) {
        c.revealContact=false; c.quiet = c.touchdown = c.landed = true; c.landAt = -Infinity;
        if (c.code) c.faceAt = time - TIMING.flipMs;
        c.rest = target(c); setPose(c, c.rest, c.code ? 1 : 0); c.hit = c.glow = null;
        c.shadow.position.copy(c.rest).addScaledVector(normal, -.001);
        if (c.rim) c.rim.visible = false;
      }
      for(const m of betChips){const b=m.userData.bet;b.born=-Infinity;b.landed=true;if(b.settle)b.settle.quiet=true;}
      fan = null; fans.splice(0).forEach(drop); deckAt = -Infinity;
      stack.forEach(({ m, position, rotation }) => { m.position.copy(position); m.quaternion.copy(rotation); });
      for (const s of sparks.splice(0)) { s.mesh.removeFromParent(); s.mesh.geometry.dispose(); s.mesh.material.dispose(); }
    },
    split() { const c = cards.find((x) => x.owner === 0 && x.slot === 1); if (c) { c.owner = 1; c.slot = 0; } hands = 2; },
    reveal(code, time, settled = false) { const c = cards.find((x) => x.owner === 'd' && x.slot === 1); if (c) { c.code = code; c.faceAt = settled ? time - TIMING.flipMs : time; c.revealContact=!settled; } },
    setActive(i) { if(active>=0 && active!==i)squared.add(active); active = i; },
    setBets(list, time=now, quiet=false) {
      if (JSON.stringify(list) === group.userData.bets) return;
      group.userData.bets = JSON.stringify(list);
      for(const m of betChips) {
        const b=m.userData.bet;
        if(b.settle || b.j >= (list[b.i]||0)) { b.removeAt ??= time; }
        else b.n=list[b.i];
      }
      list.forEach((n,i)=>{for(let j=0;j<n;j++)
        if(!betChips.some(m=>{const b=m.userData.bet;return b.i===i&&b.j===j&&b.removeAt==null&&!b.settle;}))makeChip(i,j,n,time,quiet);
      });
    },
    settleBets(hand, time, quiet) {
      hand.result.hands.forEach((result,i)=>{
        const win=['win','blackjack','charlie'].includes(result.outcome), push=result.outcome==='push';
        for(const m of betChips.filter(m=>m.userData.bet.i===i)) {
          const b=m.userData.bet; b.landed=true;
          if(!push)b.settle={at:time,dir:win?1:-1,quiet};
        }
        // The returned stake stays visible as individual chips. Extra payout chips join from the rack.
        if(win && !quiet) {
          const stake=hand.hands[i].bet, extra=Math.max(0,Math.min(12,Number(result.paid)-stake));
          for(let j=0;j<extra;j++)makeChip(i,stake+j,stake+extra,time,true).userData.bet.settle={at:time,dir:1,quiet:false,reward:true};
        }
      });
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
    hud(forOwner = Math.max(0, active)) {
      const owner = forOwner, list = cards.filter(c => c.owner === owner);
      const shown = list.filter(c => c.code && c.landed && flipAt(c, now, false) >= .5);
      const rectangles = (list.length ? list : [{ owner, slot: 0 }, { owner, slot: 1 }]).map(c => {
        const p = target(c).project(stage.camera), r = stage.canvas.getBoundingClientRect();
        return { x: (p.x + 1) * r.width / 2, y: (1 - p.y) * r.height / 2 };
      });
      const r = stage.canvas.getBoundingClientRect(), anchor = betPoint(owner).project(stage.camera);
      const first = list.length ? api.cardRect(owner, list[0].slot) : null;
      const top = list.length ? Math.min(...list.map(c=>api.cardRect(owner,c.slot).y)) : r.height*.55;
      const bottom = list.length ? Math.max(...list.map(c => { const r = api.cardRect(owner, c.slot); return r.y + r.h; })) : r.height * .75;
      return { total: shown.length ? totalOf(shown.map(c => c.code)).total : null, owner, hands, quiet: shown.every(c=>c.quiet), hidden: shown.length < list.length,
        x: rectangles.reduce((s,p)=>s+p.x,0)/rectangles.length, bottom, top,
        right: list.length ? Math.max(...list.map(c=>{const r=api.cardRect(owner,c.slot);return r.x+r.w;})) : r.width*.58,
          left: first ? Math.min(...list.map(c=>api.cardRect(owner,c.slot).x)) : r.width*.42,
        y: rectangles.reduce((s,p)=>s+p.y,0)/rectangles.length,
        betX: (anchor.x+1)*r.width/2, betY: (1-anchor.y)*r.height/2 };
    },
    // THE POT: the authored bet spot (both of them on a split), projected to the same canvas space cardRect
    // uses, so THE BANK's tokens leave the felt where the chips are and not from a guessed screen point.
    potRect() {
      const p = betPoint(0);
      if (hands === 2) p.lerp(betPoint(1), .5);
      const v = p.project(stage.camera), rect = stage.canvas.getBoundingClientRect(), r = layout.width * .5;
      const q = new T.Vector3(r, 0, 0).applyQuaternion(base).add(betPoint(0)).project(stage.camera);
      const w = Math.max(12, Math.abs(q.x - v.x) * rect.width);
      return { x: (v.x + 1) * rect.width / 2 - w / 2, y: (1 - v.y) * rect.height / 2 - w / 2, w, h: w };
    },
    settled(time, still) { return cards.every((c) => c.landed && flipAt(c, time, still) >= (c.code ? 1 : 0)); },
    draw(o) {
      if (disposed) return; frameStep=1-Math.exp(-Math.max(0,o.now-now)/90);now = o.now;
      layout=cardLayout(stage.fixture,hands,cardCounts(cards));
      for (let i=departing.length-1;i>=0;i--) {
        const c=departing[i], q=o.still?1:clamp((now-c.exitAt)/440);
        if(q===1){drop(c);departing.splice(i,1);continue;}
        const slide=ease(q), dir=c.owner==='d'?1:-1;
        c.mesh.position.copy(c.exitFrom).add(new T.Vector3(dir*layout.width*7*slide,0,-layout.height*.3*slide).applyQuaternion(basis));
        c.mesh.rotateZ(dir*.012*frameStep); c.material.opacity=1-q*q;
        c.shadow.position.copy(c.mesh.position).addScaledVector(normal,-.001);c.shadow.material.opacity=.18*(1-q);
      }
      for(let index=betChips.length-1;index>=0;index--) {
        const m=betChips[index], b=m.userData.bet, {i,j,n}=b;
        if(b.removeAt!=null && (o.still || now-b.removeAt>=220)) { removeChip(m);betChips.splice(index,1);continue; }
        const at=betPoint(i).addScaledVector(normal,Math.floor(j/3)*width*.09)
          .add(new T.Vector3((j%3-(Math.min(n,3)-1)/2)*width*.6,0,0).applyQuaternion(basis));
        b.rest ||= at.clone(); b.rest.lerp(at,o.still?1:frameStep);
        m.position.copy(b.rest); m.quaternion.copy(basis); m.material.opacity=1;
        const p=o.still?1:clamp((now-b.born-j*25)/280);
        if(!b.landed && p===1) { b.landed=true; if(!o.still && j===n-1)onCue('chip-place'); }
        if(!o.still) { m.position.addScaledVector(normal,(1-p)**2*width*1.4);m.rotateZ((1-p)*.18); }
        if(b.removeAt!=null) { const q=clamp((now-b.removeAt)/220);m.position.addScaledVector(normal,q*width);m.material.opacity=1-q; }
        if(b.settle) {
          const st=b.settle, q=o.still||st.quiet?1:clamp((now-st.at-j*35)/850);
          const dealer=point('card_slot_dealer_2'), player=at.clone().add(new T.Vector3(0,0,layout.height*.1).applyQuaternion(basis));
          m.position.copy(st.reward?dealer:at).lerp(st.dir>0?player:dealer,ease(q));
          m.material.opacity=st.dir<0?1-q:1;
        }
      }
      // Freeze any in-flight gesture on a settings transition; never replay it later.
      if (o.still) { for (const c of cards) { if (!c.landed) touchdown(c, now, true, onCue); c.landed = true; if (c.code) c.faceAt = now - TIMING.flipMs; } if (fan) fan.still = true; }
      // A tearing card needs every frame it can get, so the 15 Hz face throttle lifts while a slip runs.
      const tearing = !!slip && now - slipAt >= 0 && now - slipAt < SLIP_MS;
      const repaint = tearing || now - lastPaint >= 1000 / 15;
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
        setPose(c, pos, flip);
        const desired=squared.has(c.owner)?0:cardTilt(c.owner,c.slot)*o.k;
        c.tilt=(c.tilt??desired)+(desired-(c.tilt??desired))*(o.still?1:frameStep);
        c.mesh.rotateZ(o.still?0:c.tilt);
        if(c.revealContact && flip===1) { c.revealContact=false;if(!o.still)onCue('card-land'); }
        const pulse = hitPulse(c, now);
        const activeGlow=o.decide && c.owner===active && c.landed ? .12 : 0;
        if (pulse > 0 || activeGlow || c.rim) {
          const rim = rimFor(c); rim.visible = pulse > 0 || activeGlow>0;
          if(activeGlow && !pulse){rim.position.copy(pos).addScaledVector(normal,-.002);rim.scale.setScalar(1);rim.material.opacity=activeGlow;}
          if (pulse > 0) { c.mesh.scale.multiplyScalar(1 + 0.06 * pulse); rim.position.copy(pos).addScaledVector(normal, -0.002); rim.scale.setScalar(1 + 0.06 * pulse); rim.material.opacity = 0.6 * pulse * o.k; }
        }
        c.material.color.set(c.glow && now - c.glow < TIMING.glowMs ? '#ffb2d0' : '#ffffff');
        if (repaint) c.face.paint(wornCode(c, c.code, slip, slipAt, now), flip >= .5, o,
          typeof kit === 'function' ? kit() : kit, slipShake(c, slip, slipAt, now, o.k, o.still));
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
        const at = betPoint(0), u = s.still ? (s.dir > 0 ? 1 : 0) : s.dir > 0 ? ease(p) : 1 - ease(p);
        if (s.kind === 'chip') {
          const to = point('card_slot_dealer_2'), dist = at.distanceTo(to), radius = dist * (1 - u) * .4, angle = u * Math.PI * 2.5;
          s.mesh.position.copy(at).lerp(to, 1 - u).add(new T.Vector3(Math.cos(angle) * radius, .015, Math.sin(angle) * radius).applyQuaternion(basis));
        } else { s.mesh.position.copy(at).addScaledVector(normal, .008); s.mesh.scale.setScalar(1 + (s.still ? .5 : p) * 8); }
        s.mesh.material.opacity = Math.sin(p * Math.PI) * .4 * o.k;
      }
    },
    debug() { return { kind: 'room3d', departing: departing.length, cards: cards.map((c) => ({ owner: c.owner, slot: c.slot, code: c.code, landed: c.landed, face: c.faceAt != null })), hands, active, fan: !!fan, fanCards: fans.length, betChips: betChips.length, chips: sparks.filter((s) => s.kind === 'chip').length, frames: now, disposed,
      hits: cards.filter((c) => c.hit != null).map((c) => ({ owner: c.owner, slot: c.slot, t0: Math.round(c.hit) })) }; },
    dispose() { off(); release(); },
  };
  group.userData.debug = api.debug;
  return api;
}
