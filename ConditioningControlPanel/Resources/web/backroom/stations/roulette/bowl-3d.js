import * as T from 'three';
import { createRouletteSurfaces, getRouletteSurfaces } from '../../room/roulette-surfaces.js';
import { createBowl } from './bowl.js';
import { FEEL, SEG, restRel, beamAngle, beamLit, whirlAngle } from './feel.js';

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
  const trackBox = new T.Box3().setFromObject(nodes.ball_track);
  const trackRadius = Math.max(trackBox.getSize(new T.Vector3()).x, trackBox.getSize(new T.Vector3()).z) / (2 * rotorScale);
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
      const radius = restRadius + (trackRadius - restRadius) * radial;
      point.set(Math.cos(angle) * radius, p.y + lift + (track.y - p.y) * radial, -Math.sin(angle) * radius);
      if (s.phase === 'rest') point.copy(p).addScaledVector(up, lift);
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
    for (const n of numberColors) {
      const strength = beamLit(n.angle + s.rot, beamA) * k;
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
    for(const object of [beam,sparks,trail]){object.removeFromParent();object.geometry.dispose();object.material.dispose();}
    disc.material = oldDisc; material.dispose(); texture.dispose();
    ink.width = ink.height = 1;
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
    draw, pocketBox, dispose,
    get geo() { return core.geo; }, get phase() { return core.phase; },
    debug() { return { ...core.debug(), view: '3d', lit: [...lit], whirlDrawn: !!lastView.whirlDrawn,
      ballWorld: ball.getWorldPosition(new T.Vector3()).toArray(),
      pockets: centers.map(p => rotor.localToWorld(p.clone()).toArray()), disposed }; },
  };
}
