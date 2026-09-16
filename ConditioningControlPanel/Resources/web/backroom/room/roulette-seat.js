import * as T from 'three';

// One close-up of the wheel, dealer and betting board. Fit real mesh corners,
// not the empty corners of a box around the entire booth and canopy.
export function rouletteSeat(row, fixture, camera, width, height) {
  fixture.updateWorldMatrix(true, true);
  const points = [], seen = new Set();
  const names = ['ball_track', 'betting_mat', 'zero_bet', 'emi_dealer', 'dealer_chipassembly'];
  for (const name of names) fixture.getObjectByName(name)?.traverse(node => {
    if (!node.isMesh || seen.has(node)) return;
    seen.add(node);
    const geometry = node.geometry;
    if (!geometry.boundingBox) geometry.computeBoundingBox();
    const b = geometry.boundingBox;
    for (const x of [b.min.x,b.max.x]) for (const y of [b.min.y,b.max.y]) for (const z of [b.min.z,b.max.z])
      points.push(new T.Vector3(x,y,z).applyMatrix4(node.matrixWorld));
  });
  fixture.traverse(node => {
    if (!node.name.startsWith('bet_hit_')) return;
    for (const x of [-.5,.5]) for (const z of [-.5,.5])
      points.push(node.localToWorld(new T.Vector3(x*node.userData.hit_width,0,z*node.userData.hit_depth)));
  });
  if (!points.length) return null;
  const portrait = width < height;
  const short = height <= 500;
  const top = portrait ? 106 : 52, bottom = portrait ? 104 : short ? 64 : 74;
  const bounds = new T.Box3().setFromPoints(points), center = bounds.getCenter(new T.Vector3());
  const direction = new T.Vector3().fromArray(row.approach).sub(new T.Vector3().fromArray(row.look));
  direction.y=0; direction.normalize(); direction.y=portrait?2.6:.7; direction.normalize();
  const right = new T.Vector3().crossVectors(new T.Vector3(0,1,0), direction).normalize();
  const up = new T.Vector3().crossVectors(direction, right).normalize();
  const tan = Math.tan(camera.fov*Math.PI/360), aspect=width/height;
  const projected=points.map(p=>{const v=p.clone().sub(center);return {x:v.dot(right),y:v.dot(up),z:v.dot(direction)};});
  const widthN=2*(width-20)/width, heightN=2*(height-top-bottom)/height;
  const measure=d=>{
    let minX=Infinity,maxX=-Infinity,minY=Infinity,maxY=-Infinity;
    for(const p of projected){const z=d-p.z;if(z<=.05)return {fits:false};const x=p.x/(z*tan*aspect),y=p.y/(z*tan);
      minX=Math.min(minX,x);maxX=Math.max(maxX,x);minY=Math.min(minY,y);maxY=Math.max(maxY,y);}
    return {fits:maxX-minX<=widthN && maxY-minY<=heightN,minX,maxX,minY,maxY};
  };
  let near=.1,far=20;
  for(let i=0;i<48;i++){const d=(near+far)/2;if(measure(d).fits)far=d;else near=d;}
  const fit=measure(far), position=center.clone().addScaledVector(direction,far);
  const view=new T.PerspectiveCamera();view.rotation.order='YXZ';view.position.copy(position);view.lookAt(center);
  return {pos:position.toArray(),yaw:view.rotation.y,pitch:view.rotation.x,
    offset:(bottom-top)/(2*height)-(fit.minY+fit.maxY)/4,
    offsetX:(fit.minX+fit.maxX)/4,bounds:{min:bounds.min.toArray(),max:bounds.max.toArray()}};
}
