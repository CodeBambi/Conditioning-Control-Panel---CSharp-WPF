import * as T from 'three';
import { cardLayout } from '../stations/cards/layout-3d.js';

// Fit authored play surfaces inside the space left by the station controls.
export function seatPose(row, fixture, camera, width, height, cardHands=1, counts={d:2,0:2}) {
  fixture.updateWorldMatrix(true, true);
  const bounds = new T.Box3(), box = new T.Box3();
  function include(name) { const node = fixture.getObjectByName(name); if (node) bounds.union(box.setFromObject(node)); }
  if (row.id === 'wheel') { include('wheel_rotor'); include('pointer'); fixture.traverse(n=>{if(/^bulb_/.test(n.name))include(n.name);}); }
  if (row.id === 'cards') {
    const layout=cardLayout(fixture,cardHands,counts);
    for(const point of layout.points)for(const x of [-.5,.5])for(const z of [-.5,.5])
      bounds.expandByPoint(new T.Vector3(x*layout.width,0,z*layout.height).applyQuaternion(layout.basis).add(point));
  }
  // Roulette on a phone frames the mat while bets are open and the whole table once the ball runs
  // (stations/roulette/mat-3d.js sets userData.frame on its group); a desktop always sees the whole table.
  const phone=row.id==='roulette'&&(width<=800||height<=500), landscape=phone&&width>height;
  const matFrame=phone&&(fixture.getObjectByName('roulette_runtime_mat')?.userData.frame||'mat')==='mat';
  const wide=[];   // points that only bound the view vertically: the wheel above a mat-framed phone view
  if (row.id === 'roulette') { include('betting_mat'); include('zero_bet');
    fixture.traverse(n => { if (n.name.startsWith('bet_hit_')) {
      const w=n.userData.hit_width/2, h=n.userData.hit_depth/2;
      for(const x of [-w,w])for(const z of [-h,h])bounds.expandByPoint(n.localToWorld(new T.Vector3(x,0,z)));
    }});
    const track=fixture.getObjectByName('ball_track');
    if(track){box.setFromObject(track);if(matFrame){for(const x of [box.min.x,box.max.x])for(const y of [box.min.y,box.max.y])for(const z of [box.min.z,box.max.z])wide.push(new T.Vector3(x,y,z));}else bounds.union(box);}
  }
  if (bounds.isEmpty()) return null;
  const matCenter=bounds.getCenter(new T.Vector3()), matBounds=bounds.clone();
  const direction=new T.Vector3().fromArray(row.approach).sub(new T.Vector3().fromArray(row.look)); direction.y=0; direction.normalize();
  // A landscape phone is short: the wheel's far side may leave the top, its near rim stays in view.
  const far=landscape?.7:Infinity;
  for(const p of wide){const d=p.clone().sub(matCenter).dot(direction);if(d<-far)p.addScaledVector(direction,-far-d);bounds.expandByPoint(p);}
  const center=bounds.getCenter(new T.Vector3());
  direction.y=row.id==='cards'?1.5:row.id==='roulette'?3:0; direction.normalize();
  const right=new T.Vector3().crossVectors(new T.Vector3(0,1,0),direction).normalize();
  const up=new T.Vector3().crossVectors(direction,right).normalize();
  if(matFrame)center.addScaledVector(right,matCenter.clone().sub(center).dot(right));   // the mat sits centred, the wheel above it
  const rightInset=landscape?200:0;
  const top=phone?(landscape?50:150):height<500?85:row.id==='roulette'?110:120, bottom=phone?(landscape?12:108):row.id==='roulette'?155:row.id==='cards'?170:180;
  const available=Math.max(.3,(height-top-bottom)/height), tan=Math.tan(camera.fov*Math.PI/360);
  const fitX=tan*(width-rightInset)/height*(row.id==='wheel'?.965:phone?.985:.92), fitY=tan*available*(phone?.96:.9);
  let distance=.4;
  const corners=b=>{const list=[];for(const x of [b.min.x,b.max.x])for(const y of [b.min.y,b.max.y])for(const z of [b.min.z,b.max.z])list.push(new T.Vector3(x,y,z).sub(center));return list;};
  for(const p of corners(bounds)){const depth=p.dot(direction);distance=Math.max(distance,depth+Math.abs(p.dot(up))/fitY);if(!matFrame)distance=Math.max(distance,depth+Math.abs(p.dot(right))/fitX);}
  if(matFrame)for(const p of corners(matBounds)){const depth=p.dot(direction);distance=Math.max(distance,depth+Math.abs(p.dot(right))/fitX);}
  const position=center.clone().addScaledVector(direction,distance);
  const view=new T.PerspectiveCamera();view.rotation.order='YXZ';view.position.copy(position);view.lookAt(center);
  return {pos:position.toArray(),yaw:view.rotation.y,pitch:view.rotation.x,offset:(bottom-top)/(2*height),offsetX:rightInset/(2*width),bounds:{min:bounds.min.toArray(),max:bounds.max.toArray()}};
}
export const easeSeat=t=>t*t*(3-2*t);
export const shortAngle=(a,b)=>a+Math.atan2(Math.sin(b-a),Math.cos(b-a));
