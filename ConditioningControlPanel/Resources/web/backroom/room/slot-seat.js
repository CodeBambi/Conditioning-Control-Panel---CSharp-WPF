import * as T from 'three';

// Fit the visible controls themselves, including their different depths, so a
// projecting lever or apron does not buy empty room around the entire cabinet.
export function slotSeat(fixture, camera, width, height) {
  fixture.updateWorldMatrix(true,true);
  const bounds = new T.Box3(),points=[];
  for (const name of ['reel_window','screen_jackpot','lever','freeze_1','freeze_2','freeze_3','spin_button','emi_topper','apron_display']) {
    const node=fixture.getObjectByName(name); if(node){const box=new T.Box3().setFromObject(node);bounds.union(box);for(const x of [box.min.x,box.max.x])for(const y of [box.min.y,box.max.y])for(const z of [box.min.z,box.max.z])points.push(new T.Vector3(x,y,z));}
  }
  if(bounds.isEmpty()){bounds.setFromObject(fixture);for(const x of [bounds.min.x,bounds.max.x])for(const y of [bounds.min.y,bounds.max.y])for(const z of [bounds.min.z,bounds.max.z])points.push(new T.Vector3(x,y,z));}
  const direction=new T.Vector3(0,0,1).transformDirection(fixture.matrixWorld);
  const right=new T.Vector3().crossVectors(new T.Vector3(0,1,0),direction).normalize();
  const up=new T.Vector3().crossVectors(direction,right).normalize();
  const center=bounds.getCenter(new T.Vector3());
  const portrait=height>width, top=6, bottom=portrait?56:40;
  const spanY=Math.max(.5,(height-top-bottom)/height), spanX=.985;
  const tan=Math.tan(camera.fov*Math.PI/360), slopeX=tan*width/height*spanX,slopeY=tan*spanY;
  const projected=points.map(corner=>{const p=corner.clone().sub(center);return {x:p.dot(right),y:p.dot(up),z:p.dot(direction)};});
  let near=Math.max(.05,...projected.map(p=>p.z+.05)),far=near+1;
  const envelope=distance=>{
    let left=-Infinity,right=Infinity,bottom=-Infinity,top=Infinity;
    for(const p of projected){const depth=distance-p.z;left=Math.max(left,p.x-slopeX*depth);right=Math.min(right,p.x+slopeX*depth);bottom=Math.max(bottom,p.y-slopeY*depth);top=Math.min(top,p.y+slopeY*depth);}
    return {left,right,bottom,top,fits:left<=right&&bottom<=top};
  };
  while(!envelope(far).fits)far*=2;
  for(let i=0;i<30;i++){const mid=(near+far)/2;if(envelope(mid).fits)far=mid;else near=mid;}
  const fit=envelope(far);
  center.addScaledVector(right,(fit.left+fit.right)/2).addScaledVector(up,(fit.bottom+fit.top)/2);
  const position=center.clone().addScaledVector(direction,far);
  const view=new T.PerspectiveCamera();view.rotation.order='YXZ';view.position.copy(position);view.lookAt(center);
  return {pos:position.toArray(),yaw:view.rotation.y,pitch:view.rotation.x,offset:(bottom-top)/(2*height),offsetX:0};
}
