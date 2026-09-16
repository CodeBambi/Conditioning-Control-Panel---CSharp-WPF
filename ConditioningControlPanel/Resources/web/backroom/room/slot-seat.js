import * as T from 'three';

// The same authored cabinet remains on the floor; only the room camera travels.
export function slotSeat(fixture, camera, width, height) {
  fixture.updateWorldMatrix(true,true);
  const bounds = new T.Box3(),points=[];
  for (const name of ['reel_window','screen_jackpot','lever','freeze_1','freeze_2','freeze_3','spin_button','emi_topper']) {
    const node=fixture.getObjectByName(name); if(node){const box=new T.Box3().setFromObject(node);bounds.union(box);for(const x of [box.min.x,box.max.x])for(const y of [box.min.y,box.max.y])for(const z of [box.min.z,box.max.z])points.push(new T.Vector3(x,y,z));}
  }
  if(bounds.isEmpty())bounds.setFromObject(fixture);
  const direction=new T.Vector3(0,0,1).transformDirection(fixture.matrixWorld);
  const right=new T.Vector3().crossVectors(new T.Vector3(0,1,0),direction).normalize();
  const up=new T.Vector3().crossVectors(direction,right).normalize();
  const center=bounds.getCenter(new T.Vector3());
  const portrait=height>width, top=10, bottom=portrait?62:48;
  const spanY=Math.max(.5,(height-top-bottom)/height), spanX=.985;
  const tan=Math.tan(camera.fov*Math.PI/360);
  let distance=.4;
  for(const corner of points) {
    const p=corner.clone().sub(center), depth=p.dot(direction);
    distance=Math.max(distance,depth+Math.abs(p.dot(right))/(tan*width/height*spanX),depth+Math.abs(p.dot(up))/(tan*spanY));
  }
  const position=center.clone().addScaledVector(direction,distance);
  const view=new T.PerspectiveCamera();view.rotation.order='YXZ';view.position.copy(position);view.lookAt(center);
  return {pos:position.toArray(),yaw:view.rotation.y,pitch:view.rotation.x,offset:(bottom-top)/(2*height),offsetX:0};
}
