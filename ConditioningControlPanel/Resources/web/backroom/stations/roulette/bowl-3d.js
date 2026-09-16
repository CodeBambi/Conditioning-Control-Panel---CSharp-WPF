import * as T from 'three';
import { createRouletteSurfaces, getRouletteSurfaces } from '../../room/roulette-surfaces.js';
import { createBallPath } from './ball-path.js';
import { createBowl } from './bowl.js';
import { FEEL, SEG, restRel, beamAngle, beamLit, whirlAngle } from './feel.js';
import { HIGHLIGHT_MS } from '../../shared/hypno/callout.js';
import { glyphFor, paintGlyph } from './glyphs.js';

// The authored pocket centers define this coordinate system. No model dimensions
// or pocket order are duplicated here; the server order indexes named pockets.
export function createBowl3D({ stage, wheel, rose }) {
  const root = stage.fixture, core = createBowl({ wheel, rose });
  const required = ['roulette_rotor', 'roulette_ball', 'ball_track', 'center_disc', ...wheel.map(n => `pocket_${n}`)];
  const nodes = Object.fromEntries(required.map(name => [name, root.getObjectByName(name)]));
  const missing = required.filter(name => !nodes[name]);
  if (missing.length) throw new Error('Roulette model missing: ' + missing.join(', '));
  const rotor = nodes.roulette_rotor, ball = nodes.roulette_ball;
  const saved = { rotor: rotor.quaternion.clone(), ball: ball.position.clone(), visible: ball.visible };
  const up = new T.Vector3(0, 1, 0), q = new T.Quaternion(), point = new T.Vector3();
  root.updateWorldMatrix(true, true);
  function center(node, parent = rotor) {
    return parent.worldToLocal(new T.Box3().setFromObject(node).getCenter(new T.Vector3()));
  }
  const centers = wheel.map(n => center(nodes[`pocket_${n}`]));
  const angles = centers.map(p => Math.atan2(-p.z, p.x));
  const restRadius = centers.reduce((sum, p) => sum + Math.hypot(p.x, p.z), 0) / centers.length;
  const ballBox = new T.Box3().setFromObject(ball), ballRadius = ballBox.getSize(new T.Vector3()).y / 2;
  const rotorScale = rotor.getWorldScale(new T.Vector3()).y;
  const lift = ballRadius / rotorScale;
  const track = center(nodes.ball_track);
  // Horizontal radius cannot be divided by world Y scale: the room compresses this
  // table vertically, which otherwise stretches the lighthouse out across the felt.
  let trackRadius=0;
  const trackToRotor=rotor.matrixWorld.clone().invert(),trackTransform=new T.Matrix4(),trackVertex=new T.Vector3();
  nodes.ball_track.traverse(n=>{
    if(!n.isMesh||!n.geometry?.attributes.position)return;
    trackTransform.multiplyMatrices(trackToRotor,n.matrixWorld);
    const a=n.geometry.attributes.position;
    for(let i=0;i<a.count;i++){trackVertex.fromBufferAttribute(a,i).applyMatrix4(trackTransform);trackRadius=Math.max(trackRadius,Math.hypot(trackVertex.x,trackVertex.z));}
  });
  const path=createBallPath(rotor,ball,nodes.ball_track,restRadius,lift);
  const pocketTops=wheel.map(n=>{
    const node=nodes['pocket_'+n],bounds=node.geometry.boundingBox||new T.Box3().setFromBufferAttribute(node.geometry.attributes.position);
    return rotor.worldToLocal(node.localToWorld(bounds.getCenter(new T.Vector3()).setY(bounds.max.y))).y;
  });
  const existingSurfaces=getRouletteSurfaces(root);
  const surfaces=existingSurfaces||createRouletteSurfaces(root),numberColors=surfaces.numberColors;
  const disc = nodes.center_disc, oldDisc = disc.material;
  const ink = document.createElement('canvas'); ink.width = ink.height = 256;
  const inkCtx = ink.getContext('2d'), texture = new T.CanvasTexture(ink);
  texture.colorSpace = T.SRGBColorSpace;
  const material = oldDisc.clone(); material.map = texture; disc.material = material;
  const beam = new T.Mesh(new T.RingGeometry(restRadius*.45,trackRadius,32,1,-FEEL.BEAM_HALF,FEEL.BEAM_HALF*2),
    new T.MeshBasicMaterial({color:0xe8c27a,transparent:true,opacity:.2,depthWrite:false,side:T.DoubleSide}));
  beam.rotation.x=-Math.PI/2;beam.position.y=centers[0].y+lift;rotor.add(beam);
  const sparkGeometry=new T.BufferGeometry();sparkGeometry.setAttribute('position',new T.Float32BufferAttribute(new Float32Array(18),3));
  const sparks=new T.LineSegments(sparkGeometry,new T.LineBasicMaterial({color:0x5fffd0,transparent:true,depthWrite:false}));rotor.add(sparks);
  const trailGeometry=new T.BufferGeometry();trailGeometry.setAttribute('position',new T.Float32BufferAttribute(new Float32Array(72),3));
  const trail=new T.Line(trailGeometry,new T.LineBasicMaterial({color:0x5fffd0,transparent:true,opacity:.2,depthWrite:false}));root.add(trail);
  // THE THROW's hint (flick.js): a curved, half-lit arrow around the rim, on the rotor but counter-turned so it
  // stands still in the room while the wheel moves under it. It breathes on a 1.2 s cycle; still, it just sits there.
  // The seat reads angles y UP (angleAt) where the canvas bowl reads them y down, so the arrow is MIRRORED here:
  // it sweeps BACK from its tail to the head, which is the way the rotor's angle SHRINKS, and that reads
  // CLOCKWISE from the chair. A finger that follows it drags the rotor clockwise too, so arrow and wheel agree.
  const HINT_MS=1200,HINT_A0=-Math.PI*.45,HINT_ARC=Math.PI*1.15;
  const hintR=restRadius*1.45,hintTube=restRadius*.05;
  const hintMaterial=new T.MeshBasicMaterial({color:0x5fffd0,transparent:true,opacity:0,depthWrite:false,side:T.DoubleSide});
  const hintArc=new T.Mesh(new T.TorusGeometry(hintR,hintTube,6,44,HINT_ARC),hintMaterial);hintArc.rotation.z=-HINT_ARC;
  const hintHead=new T.Mesh(new T.CircleGeometry(hintTube*3.6,3),hintMaterial);
  hintHead.position.set(Math.cos(HINT_ARC)*hintR,-Math.sin(HINT_ARC)*hintR,0);hintHead.rotation.z=-HINT_ARC-Math.PI/2;
  const hint=new T.Group();hint.name='roulette_flick_hint';hint.add(hintArc,hintHead);
  // The complete arrow, including its widest breathing head, clears the authored brim.
  // A track centre is below that lip, so a fixed multiple of ball height cuts through it.
  const hintScaleMax=1.035, hintOuter=(hintR+hintTube*3.6)*hintScaleMax;
  const hintInner=Math.max(0,(hintR-hintTube*3.6)/hintScaleMax);
  const toRotor=rotor.matrixWorld.clone().invert(), meshToRotor=new T.Matrix4(), vertex=new T.Vector3();
  let hintSurface=track.y;
  function scanHintSurface(node){
    if(!node.isMesh||!node.geometry?.attributes.position||node===ball)return;
    meshToRotor.multiplyMatrices(toRotor,node.matrixWorld);
    const position=node.geometry.attributes.position,index=node.geometry.index,count=index?index.count:position.count;
    for(let i=0;i<count;i+=3){
      let lo=Infinity,hi=-Infinity,top=-Infinity;
      for(let j=0;j<3;j++){
        vertex.fromBufferAttribute(position,index?index.getX(i+j):i+j).applyMatrix4(meshToRotor);
        const r=Math.hypot(vertex.x,vertex.z);lo=Math.min(lo,r);hi=Math.max(hi,r);top=Math.max(top,vertex.y);
      }
      // Lathe facets are chords; a small radial padding keeps this bound conservative.
      if(hi>=hintInner-.01 && lo<=hintOuter+.01)hintSurface=Math.max(hintSurface,top);
    }
  }
  rotor.traverse(scanHintSurface);nodes.ball_track.traverse(scanHintSurface);
  const hintMargin=Math.max(.008,lift*.3), hintHeight=hintSurface+hintTube*hintScaleMax+hintMargin;
  hint.rotation.x=-Math.PI/2;hint.position.y=hintHeight;hint.renderOrder=4;hint.visible=false;rotor.add(hint);
  // THE POCKET GLYPHS (glyphs.js, GLYPHS.md): one faded decal per numbered pocket on the rotor's inner slope, just
  // inside the ball's footprint, keyed by the pocket number. A ray down from above finds the authored surface under
  // each so the mark lies on the wheel, whatever its profile; a miss falls back to the pocket's own top.
  const GLYPH_REST = 0.26, GLYPH_PULSE_MS = 500, glyphCold = new T.Color('#fff1e4'), glyphHot = new T.Color('#5fffd0');
  const glyphTextures = new Map();
  function glyphTexture(id) {
    if (glyphTextures.has(id)) return glyphTextures.get(id);
    const c = document.createElement('canvas'); c.width = c.height = 128; paintGlyph(c.getContext('2d'), id, 128);
    const tex = new T.CanvasTexture(c); tex.colorSpace = T.SRGBColorSpace; tex.anisotropy = 4;
    glyphTextures.set(id, tex); return tex;
  }
  // Pulled further in off the number tiles: at .8 of a segment sitting at restRadius - 2.4 lifts, the mark's
  // outer edge ran into the numbers and the two read as one smudge on a phone (owner, 2026-09-16). One more
  // lift inward and a hair smaller is enough to put daylight between them without leaving the inner slope.
  const glyphR = Math.max(restRadius * .5, restRadius - lift * 3.9), glyphSize = SEG * glyphR * .76;
  const glyphGeometry = new T.PlaneGeometry(1, 1), caster = new T.Raycaster(), rotorInverse = rotor.matrixWorld.clone().invert();
  const skip = new Set([beam, sparks, trail, hintArc, hintHead, ball]);
  const glyphs = wheel.map((n, i) => {
    const id = glyphFor(n); if (!id) return null;
    const a = angles[i], radial = new T.Vector3(Math.cos(a), 0, -Math.sin(a));
    const at = new T.Vector3(Math.cos(a) * glyphR, pocketTops[i] + lift * 4, -Math.sin(a) * glyphR), normal = up.clone();
    caster.set(rotor.localToWorld(at.clone()), new T.Vector3(0, -1, 0).transformDirection(rotor.matrixWorld));
    const hit = caster.intersectObject(rotor, true).find((h) => h.face && h.object.isMesh && !skip.has(h.object) && !h.object.name.startsWith('pocket_glyph_'));
    if (hit) { rotor.worldToLocal(at.copy(hit.point)); normal.copy(hit.face.normal).transformDirection(hit.object.matrixWorld).transformDirection(rotorInverse); }
    else at.y = pocketTops[i];
    const material = new T.MeshBasicMaterial({ map: glyphTexture(id), color: glyphCold, transparent: true, opacity: GLYPH_REST, depthWrite: false, side: T.DoubleSide });
    const mesh = new T.Mesh(glyphGeometry, material); mesh.name = 'pocket_glyph_' + n; mesh.renderOrder = 3;
    const y = radial.clone().addScaledVector(normal, -radial.dot(normal)).normalize(), x = new T.Vector3().crossVectors(y, normal);
    mesh.quaternion.setFromRotationMatrix(new T.Matrix4().makeBasis(x, y, normal));
    mesh.position.copy(at).addScaledVector(normal, lift * .12); mesh.scale.setScalar(glyphSize); rotor.add(mesh);
    return { n, id, index: i, angle: a, material, mesh };
  });
  let glyphLitIndex = -1, glyphLitAt = -Infinity;
  let disposed = false, lastView = {}, activePlan=null, launchAt=0, currentNow=0;
  const trailPoints=[];
  const lit = new Set();
  function apply() {
    if (disposed) return;
    const s = core.debug();
    rotor.quaternion.copy(saved.rotor).multiply(q.setFromAxisAngle(up, s.rot));
    rotor.updateWorldMatrix(true, true);
    ball.visible = s.phase !== 'idle';
    if (ball.visible && s.index >= 0) {
      const p = centers[s.index];
      // Source order runs clockwise; the canvas plan uses increasing indices.
      const angle = angles[s.index] - (s.rel - restRel(s.index));
      const radial = Math.max(0, Math.min(1, (s.radius - FEEL.R_REST) / (FEEL.R_RIM - FEEL.R_REST)));
      const radius = restRadius + (path.rimRadius - restRadius) * radial;
      point.set(Math.cos(angle) * radius, path.height(radius), -Math.sin(angle) * radius);
      if (s.phase === 'rest') point.copy(p).setY(pocketTops[s.index]+lift+path.margin);
      rotor.localToWorld(point); ball.parent.worldToLocal(point); ball.position.copy(point);
    }
    const beamA = beamAngle(s.beamT), k = lastView.k ?? 1;
    beam.rotation.z=beamA-s.rot;beam.material.opacity=.2*k;
    const sec=(currentNow-launchAt)/1000;
    let sparkCount=0;
    if(activePlan && !lastView.still)for(const hit of activePlan.sparks){
      const age=(sec-hit.at)/FEEL.SPARK_S;if(age<0||age>=1)continue;
      const index=((Math.floor(hit.a/SEG)%wheel.length)+wheel.length)%wheel.length;
      const a=angles[index]+SEG*.5;
      for(const radius of [restRadius*.9,restRadius*1.12])sparkGeometry.attributes.position.setXYZ(sparkCount++,Math.cos(a)*radius,centers[index].y+lift,-Math.sin(a)*radius);
    }
    sparks.visible=sparkCount>0;sparkGeometry.setDrawRange(0,sparkCount);sparkGeometry.attributes.position.needsUpdate=true;sparks.material.opacity=.8*k;
    if(lastView.full&&!lastView.still&&ball.visible){
      trailPoints.unshift(root.worldToLocal(ball.getWorldPosition(new T.Vector3())));trailPoints.length=Math.min(24,trailPoints.length);
      trailPoints.forEach((p,i)=>trailGeometry.attributes.position.setXYZ(i,p.x,p.y,p.z));trailGeometry.setDrawRange(0,trailPoints.length);trailGeometry.attributes.position.needsUpdate=true;
    }else trailPoints.length=0;
    trail.visible=trailPoints.length>1;trail.material.opacity=.2*k;
    if (hint.visible) {
      const pulse = lastView.still ? .5 : (1 - Math.cos((currentNow % HINT_MS) / HINT_MS * Math.PI * 2)) / 2;
      hint.rotation.z = -HINT_A0 - s.rot;   // the room's angle, not the rotor's (mirrored: the seat reads y up)
      hint.scale.setScalar(1 + (lastView.still ? 0 : .035 * pulse));
      hintMaterial.opacity = (.3 + .28 * pulse) * k;
    }
    // THE POCKET GLYPHS: the beam brushes them a little; the landed one goes hot on the settle frame (the thud frame,
    // Law X) with a GLYPH_PULSE_MS scale pulse and stays hot while the ball sits there.
    const seated = s.phase === 'settle' || s.phase === 'rest' ? s.index : -1;
    if (seated !== glyphLitIndex) { glyphLitIndex = seated; glyphLitAt = currentNow; }
    const pq = (currentNow - glyphLitAt) / GLYPH_PULSE_MS, glyphPulse = pq >= 0 && pq < 1 ? Math.sin(pq * Math.PI) : 0;
    for (const g of glyphs) {
      if (!g) continue;
      const hot = g.index === glyphLitIndex ? 1 : 0, strength = Math.max(beamLit(g.angle + s.rot, beamA) * .35, hot);
      g.material.opacity = (GLYPH_REST + (1 - GLYPH_REST) * strength) * k;
      g.material.color.copy(glyphCold).lerp(glyphHot, strength);
      g.mesh.scale.setScalar(glyphSize * (1 + (hot ? .3 * glyphPulse : 0)));
    }
    // THE GLYPH HIT: the landed pocket's number lights on its own over HIGHLIGHT_MS from the winning frame (callout.js)
    const hq = (currentNow - s.hitAt) / HIGHLIGHT_MS, hitPulse = hq >= 0 && hq < 1 ? Math.sin(hq * Math.PI) : 0, hitN = s.index >= 0 ? wheel[s.index] : null;
    for (const n of numberColors) {
      const strength = Math.max(beamLit(n.angle + s.rot, beamA) * k, n.n === hitN ? hitPulse : 0);
      if (strength > 0.1) lit.add(n.n);
      for(let i=n.offset;i<n.offset+n.count;i++) n.attribute.setXYZ(i,1,1-strength*.15,1-strength*.45);
      n.attribute.needsUpdate=true;
    }
  }
  function draw(_, view) {
    lastView = view;
    const s = core.debug();
    inkCtx.clearRect(0, 0, 256, 256);
    inkCtx.fillStyle = '#21152e'; inkCtx.fillRect(0, 0, 256, 256);
    let whirlDrawn = false;
    if (s.whirlA > .01 && view.spiral && view.kit) {
      whirlDrawn = view.kit.draw(inkCtx, 'whirl', 0, 0, 256, 256,
        { angle: whirlAngle(s.rot), now: view.now, alpha: FEEL.WHIRL_ALPHA * s.whirlA * view.k, backing: 'small' });
    }
    texture.needsUpdate = true; lastView.whirlDrawn = whirlDrawn; apply();
  }
  function dispose() {
    if (disposed) return; disposed = true;
    rotor.quaternion.copy(saved.rotor); ball.position.copy(saved.ball); ball.visible = saved.visible;
    surfaces.reset(); if(!existingSurfaces)surfaces.dispose();
    for(const object of [beam,sparks,trail,hintArc,hintHead]){object.removeFromParent();object.geometry.dispose();}
    for(const object of [beam,sparks,trail]) object.material.dispose();
    hint.removeFromParent(); hintMaterial.dispose();
    for (const g of glyphs) if (g) { g.mesh.removeFromParent(); g.material.dispose(); }
    glyphGeometry.dispose(); for (const tex of glyphTextures.values()) tex.dispose(); glyphTextures.clear();
    disc.material = oldDisc; material.dispose(); texture.dispose();
    ink.width = ink.height = 1;
  }
  /** The rotor's centre and its rim, in canvas-local CSS px. */
  function screenWheel() {
    const rect = stage.canvas.getBoundingClientRect();
    if (!rect.width || !rect.height) return null;
    root.updateWorldMatrix(true, true); stage.camera.updateMatrixWorld();
    const px = (v) => { const p = v.clone().project(stage.camera); return { x: (p.x + 1) * rect.width / 2, y: (1 - p.y) * rect.height / 2 }; };
    const c = px(rotor.getWorldPosition(new T.Vector3()));
    const edge = px(rotor.localToWorld(new T.Vector3(trackRadius, centers[0].y, 0)));
    return { rect, c, r: Math.max(24, Math.hypot(edge.x - c.x, edge.y - c.y)) };
  }
  /** THE THROW: canvas-local CSS px, the ray first and the wheel's projected disc as the fallback a finger needs. */
  function wheelHit(x, y) {
    if (disposed || stage.ready === false) return false;
    const w = screenWheel();
    if (!w) return false;
    const event = { clientX: w.rect.left + x, clientY: w.rect.top + y };
    if (stage.pick(event, [rotor, nodes.ball_track]).length) return true;
    return Math.hypot(x - w.c.x, y - w.c.y) <= w.r * 1.06;
  }
  /** The pointer's angle around the wheel, in the convention `rot` grows in (seen from the seat: y up). */
  function angleAt(x, y) {
    const w = screenWheel();
    return w ? Math.atan2(-(y - w.c.y), x - w.c.x) : 0;
  }
  function pocketBox(index) {
    root.updateWorldMatrix(true, true);
    point.copy(centers[index]); rotor.localToWorld(point); point.project(stage.camera);
    const rect = stage.canvas.getBoundingClientRect();
    return { x: (point.x + 1) * rect.width / 2 - 20, y: (1 - point.y) * rect.height / 2 - 20, w: 40, h: 40 };
  }
  return {
    layout: core.layout, kick: core.kick, clear() { core.clear(); apply(); },
    launch(plan, now, opts) { activePlan=plan;launchAt=now;trailPoints.length=0;core.launch(plan, now, opts); apply(); },
    seat(index, opts) { core.seat(index, opts); apply(); },
    update(now, opts) { currentNow=now;const result = core.update(now, opts); apply(); return result; },
    draw, pocketBox, dispose, wheelHit, angleAt,
    turn(d) { core.turn(d); apply(); },
    setHint(on) { hint.visible = !!on; if (!hint.visible) hintMaterial.opacity = 0; },
    glow(index, now) { core.glow(index, now); },
    get geo() { return core.geo; }, get phase() { return core.phase; },
    debug() { return { ...core.debug(), view: '3d', lit: [...lit], whirlDrawn: !!lastView.whirlDrawn, hint: hint.visible,
      glyphs: glyphs.filter(Boolean).map((g) => ({ n: g.n, id: g.id })), glyphLit: glyphLitIndex >= 0 ? wheel[glyphLitIndex] : null,
      clearance: {trackRadius,rimRadius:path.rimRadius,ballRadius:lift,profileEdges:path.edges,margin:path.margin, hint:{surface:hintSurface,bottom:hintHeight-hintTube*hintScaleMax,margin:hintMargin,inner:hintInner,outer:hintOuter}},
      ballWorld: ball.getWorldPosition(new T.Vector3()).toArray(),
      pockets: centers.map(p => rotor.localToWorld(p.clone()).toArray()), disposed }; },
  };
}
