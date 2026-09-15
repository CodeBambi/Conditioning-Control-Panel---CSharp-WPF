import * as T from 'three';

// A radial support envelope from the authored meshes. Repeated lathe edges collapse
// to the same (radius,height) pair, so checking ball clearance stays cheap per frame.
export function createBallPath(rotor, ball, track, restRadius, ballRadius) {
  rotor.updateWorldMatrix(true,true);
  const toRotor=rotor.matrixWorld.clone().invert(), transform=new T.Matrix4(), p=new T.Vector3();
  const start=rotor.worldToLocal(ball.getWorldPosition(new T.Vector3()));
  const rimRadius=Math.hypot(start.x,start.z), edges=new Map();
  function scan(node) {
    if(!node.isMesh||!node.geometry?.attributes.position)return;
    transform.multiplyMatrices(toRotor,node.matrixWorld);
    const g=node.geometry,pos=g.attributes.position,index=g.index,points=[];
    for(let i=0;i<pos.count;i++){p.fromBufferAttribute(pos,i).applyMatrix4(transform);points.push([Math.hypot(p.x,p.z),p.y]);}
    const count=index?index.count:pos.count;
    for(let i=0;i<count;i+=3)for(let j=0;j<3;j++){
      let a=points[index?index.getX(i+j):i+j],b=points[index?index.getX(i+(j+1)%3):i+(j+1)%3];
      if(a[0]>b[0])[a,b]=[b,a];
      if(b[0]<restRadius-ballRadius||a[0]>rimRadius+ballRadius)continue;
      const key=[...a,...b].map(v=>Math.round(v*1e5)).join(',');edges.set(key,[...a,...b]);
    }
  }
  rotor.traverse(scan);track.traverse(scan);
  const profile=[...edges.values()],margin=ballRadius*.025;
  function height(radius) {
    let top=-Infinity;
    for(const [r0,y0,r1,y1] of profile){
      const lo=Math.max(r0,radius-ballRadius),hi=Math.min(r1,radius+ballRadius);if(lo>hi)continue;
      const m=(y1-y0)/(r1-r0||1),at=Math.max(lo,Math.min(hi,radius+ballRadius*m/Math.sqrt(1+m*m)));
      const y=r1-r0<1e-8?Math.max(y0,y1):y0+(at-r0)*m;
      top=Math.max(top,y+Math.sqrt(Math.max(0,ballRadius*ballRadius-(at-radius)**2)));
    }
    return top+margin;
  }
  return {rimRadius,height,edges:profile.length,margin};
}
