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
  const splitRoulette=row.id==='roulette'&&(width<=800||(height<=500&&width>=height));
  if (row.id === 'roulette') { include('ball_track'); if(!splitRoulette){include('betting_mat');include('zero_bet');}
    fixture.traverse(n => { if (!splitRoulette&&n.name.startsWith('bet_hit_')) {
      const w=n.userData.hit_width/2, h=n.userData.hit_depth/2;
      for(const x of [-w,w])for(const z of [-h,h])bounds.expandByPoint(n.localToWorld(new T.Vector3(x,0,z)));
    }});
  }
  // The slot: the reel window and the jackpot screen are the pan-in target, fitted on height alone (a phone held
  // upright would otherwise back off to fit their width); the station's own canvas frames the play once it is up.
  const travelOnly = row.id === 'slot';
  if (travelOnly) { include('reel_window'); include('screen_jackpot'); }
  if (bounds.isEmpty()) return null;
  const center=bounds.getCenter(new T.Vector3());
  const direction=new T.Vector3().fromArray(row.approach).sub(new T.Vector3().fromArray(row.look)); direction.y=0; direction.normalize();
  direction.y=row.id==='cards'?1.5:row.id==='roulette'?3:0; direction.normalize();
  const right=new T.Vector3().crossVectors(new T.Vector3(0,1,0),direction).normalize();
  const up=new T.Vector3().crossVectors(direction,right).normalize();
  const sideControls=row.id==='roulette'&&width>height&&height<=500;
  const rightInset=splitRoulette?0:sideControls?240:0;
  let top=height<500?85:row.id==='roulette'?110:120, bottom=splitRoulette?height/3:sideControls?20:row.id==='roulette'?155:row.id==='cards'?170:180;
  if(row.id==='slot'){top=height<500?56:110;bottom=height<500?24:150;}   // no station controls share this view: the band is the chrome's only
  const available=Math.max(.3,(height-top-bottom)/height), tan=Math.tan(camera.fov*Math.PI/360);
  let distance=.4;
  for(const x of [bounds.min.x,bounds.max.x])for(const y of [bounds.min.y,bounds.max.y])for(const z of [bounds.min.z,bounds.max.z]){
    const p=new T.Vector3(x,y,z).sub(center), depth=p.dot(direction);
    distance=Math.max(distance,travelOnly?0:depth+Math.abs(p.dot(right))/(tan*(width-rightInset)/height*(row.id==='wheel'?.965:.92)),depth+Math.abs(p.dot(up))/(tan*available*.9));
  }
  const position=center.clone().addScaledVector(direction,distance);
  const view=new T.PerspectiveCamera();view.rotation.order='YXZ';view.position.copy(position);view.lookAt(center);
  return {pos:position.toArray(),yaw:view.rotation.y,pitch:view.rotation.x,offset:(bottom-top)/(2*height),offsetX:rightInset/(2*width),bounds:{min:bounds.min.toArray(),max:bounds.max.toArray()}};
}
export const easeSeat=t=>t*t*(3-2*t);
export const shortAngle=(a,b)=>a+Math.atan2(Math.sin(b-a),Math.cos(b-a));
