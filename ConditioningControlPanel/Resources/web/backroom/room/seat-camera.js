import * as T from 'three';
import { cardLayout } from '../stations/cards/layout-3d.js';
import { rouletteSeat } from './roulette-seat.js';

// Fit authored play surfaces inside the space left by the station controls.
export function seatPose(row, fixture, camera, width, height, cardHands=1, counts={d:2,0:2}) {
  if (row.id === 'roulette') return rouletteSeat(row,fixture,camera,width,height);
  fixture.updateWorldMatrix(true, true);
  const bounds = new T.Box3(), box = new T.Box3();
  function include(name) { const node = fixture.getObjectByName(name); if (node) bounds.union(box.setFromObject(node)); }
  if (row.id === 'wheel') { include('wheel_rotor'); include('pointer'); fixture.traverse(n=>{if(/^bulb_/.test(n.name))include(n.name);}); }
  if (row.id === 'cards') {
    const layout=cardLayout(fixture,cardHands,counts);
    for(const point of layout.points)for(const x of [-.5,.5])for(const z of [-.5,.5])
      bounds.expandByPoint(new T.Vector3(x*layout.width,0,z*layout.height).applyQuaternion(layout.basis).add(point));
  }
  // Retain the standalone slot travel fit; the room's shared slot uses slot-seat.js.
  const travelOnly = row.id === 'slot';
  if (travelOnly) { include('reel_window'); include('screen_jackpot'); }
  if (bounds.isEmpty()) return null;
  const center=bounds.getCenter(new T.Vector3());
  const direction=new T.Vector3().fromArray(row.approach).sub(new T.Vector3().fromArray(row.look));
  direction.y=0; direction.normalize(); direction.y=row.id==='cards'?1.5:0; direction.normalize();
  const right=new T.Vector3().crossVectors(new T.Vector3(0,1,0),direction).normalize();
  const up=new T.Vector3().crossVectors(direction,right).normalize();
  let top=height<500?85:120, bottom=row.id==='cards'?170:180;
  if(travelOnly){top=height<500?56:110;bottom=height<500?24:150;}
  const small=width<=800||height<=500, shortWays=small&&width>height;
  if((row.id==='wheel'||row.id==='cards')&&small){
    top=shortWays?50:118;
    bottom=shortWays?56:(row.id==='cards'?132:108);
  }
  const available=Math.max(.3,(height-top-bottom)/height), tan=Math.tan(camera.fov*Math.PI/360);
  const fitX=tan*width/height*(row.id==='wheel'?.965:.92), fitY=tan*available*.9;
  let distance=.4;
  for(const x of [bounds.min.x,bounds.max.x])for(const y of [bounds.min.y,bounds.max.y])for(const z of [bounds.min.z,bounds.max.z]){
    const p=new T.Vector3(x,y,z).sub(center),depth=p.dot(direction);
    distance=Math.max(distance,depth+Math.abs(p.dot(up))/fitY,travelOnly?0:depth+Math.abs(p.dot(right))/fitX);
  }
  const position=center.clone().addScaledVector(direction,distance);
  const view=new T.PerspectiveCamera();view.rotation.order='YXZ';view.position.copy(position);view.lookAt(center);
  return {pos:position.toArray(),yaw:view.rotation.y,pitch:view.rotation.x,offset:(bottom-top)/(2*height),offsetX:0,bounds:{min:bounds.min.toArray(),max:bounds.max.toArray()}};
}
export const easeSeat=t=>t*t*(3-2*t);
export const shortAngle=(a,b)=>a+Math.atan2(Math.sin(b-a),Math.cos(b-a));
