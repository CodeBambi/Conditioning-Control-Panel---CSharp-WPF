import * as T from 'three';
import { createLoomKit } from '../shared/hypno/loom.js';

// Decoration uses fixture coordinates, shared bulb batches, and two small Loom tiles.
export function createVenueLights(holders) {
  const roots=[], batches=[], tiles=[], lamps=[], kit=createLoomKit({still:false});
  const palette=[0xff65bf,0xffcb73,0x67ffe0,0xb18aff].map(c=>new T.Color(c));
  const ball=new T.SphereGeometry(.045,10,6), shard=new T.OctahedronGeometry(1);
  const bulbMat=new T.MeshBasicMaterial({color:0xffffff,toneMapped:false});
  const glowCanvas=document.createElement('canvas');glowCanvas.width=glowCanvas.height=64;
  const gx=glowCanvas.getContext('2d'),gradient=gx.createRadialGradient(32,32,0,32,32,32);
  gradient.addColorStop(0,'#ffffffb0');gradient.addColorStop(.25,'#ffffff50');gradient.addColorStop(1,'#ffffff00');gx.fillStyle=gradient;gx.fillRect(0,0,64,64);
  const glowTex=new T.CanvasTexture(glowCanvas), dummy=new T.Object3D();
  function dress(key,points,paths,particleBox){
    const holder=holders.get(key);if(!holder)return null;
    const root=new T.Group();root.name='venue_lights_'+key;holder.add(root);roots.push(root);
    const bulbs=new T.InstancedMesh(ball,bulbMat,points.length);bulbs.raycast=()=>{};root.add(bulbs);
    const glowGeo=new T.BufferGeometry().setAttribute('position',new T.Float32BufferAttribute(points.flat(),3));
    glowGeo.setAttribute('color',new T.Float32BufferAttribute(points.flatMap((_,i)=>palette[i%4].toArray()),3));
    const glow=new T.Points(glowGeo,new T.PointsMaterial({size:.22,map:glowTex,vertexColors:true,transparent:true,depthWrite:false,blending:T.AdditiveBlending,toneMapped:false}));glow.raycast=()=>{};root.add(glow);
    points.forEach((p,i)=>{dummy.position.fromArray(p);dummy.scale.setScalar(1);dummy.rotation.set(0,0,0);dummy.updateMatrix();bulbs.setMatrixAt(i,dummy.matrix);bulbs.setColorAt(i,palette[i%4]);});bulbs.instanceMatrix.needsUpdate=true;
    batches.push(bulbs);
    for(const [p,color] of paths){const curve=new T.CatmullRomCurve3(p.map(v=>new T.Vector3(...v)),false,'catmullrom',0);const strip=new T.Mesh(new T.TubeGeometry(curve,p.length*3,.012,5,false),new T.MeshBasicMaterial({color,toneMapped:false}));strip.raycast=()=>{};root.add(strip);}
    const light=new T.PointLight(key==='counter'?0xff70bd:0x83ffdf,1.5,4,2);light.position.set(0,1.6,.6);root.add(light);lamps.push(light);
    const count=64,particles=new T.InstancedMesh(shard,new T.MeshBasicMaterial({color:0xffffff,transparent:true,opacity:.62,depthWrite:false,blending:T.AdditiveBlending,toneMapped:false}));particles.raycast=()=>{};root.add(particles);
    for(let i=0;i<count;i++)particles.setColorAt(i,palette[i%4]);
    batches.push({particles,box:particleBox});return root;
  }
  function edge(a,b,n){return Array.from({length:n},(_,i)=>a.map((v,j)=>v+(b[j]-v)*i/(n-1)));}
  const counterPoints=[...edge([-1.72,2.81,1.42],[1.72,2.81,1.42],24),...edge([-1.72,2.35,1.42],[1.72,2.35,1.42],24),...edge([-2.48,.85,1.55],[2.48,.85,1.55],30),...edge([-2.23,1.1,1.5],[-2.23,2.32,1.5],9),...edge([2.23,1.1,1.5],[2.23,2.32,1.5],9)];
  dress('counter',counterPoints,[[[[-2.5,.72,1.54],[2.5,.72,1.54]],0xff70cb],[[[-2.5,2.31,1.43],[2.5,2.31,1.43]],0xa78aff]],{width:5,y:.8,height:2.1,z:1.62});
  const niche=[[-2.93,.2,-2.025],[-2.93,2.77,-2.025],[2.93,2.77,-2.025],[2.93,.2,-2.025]];
  const table=Array.from({length:65},(_,i)=>{const a=i/64*Math.PI*2;return [1.71*Math.cos(a),1.045,-1.055*(.88*Math.sin(a)-.23*Math.exp(-Math.pow((a-Math.PI/2)/.55,2)))];});
  const portal=[[-3.3375,.12,.045],[-3.3375,3.79,.045],[3.3375,3.79,.045],[3.3375,.12,.045]];
  const cards=dress('cards',[...edge(niche[0],niche[1],16),...edge(niche[1],niche[2],32),...edge(niche[2],niche[3],16),...table.filter((_,i)=>i%2===0)],[[niche,0xb085ff],[table,0x72ffdc],[portal,0xff93da]],{width:5.7,y:.4,height:2.4,z:-1.98});
  if(cards){
    for(const [i,x] of [-2.1,2.1].entries()){
      const canvas=document.createElement('canvas');canvas.width=canvas.height=128;
      const texture=new T.CanvasTexture(canvas);texture.colorSpace=T.SRGBColorSpace;
      const disc=new T.Mesh(new T.CircleGeometry(.213,48),new T.MeshBasicMaterial({map:texture,toneMapped:false}));disc.name='soft_hand_loom_'+i;disc.position.set(x,1.86,-2.005);disc.raycast=()=>{};cards.add(disc);
      tiles.push({canvas,texture,preset:i?'whirl':'candy'});
    }
  }
  let time=0,last=-1;
  function update(dt,still){
    if(!still)time+=Math.min(.05,Math.max(0,dt));
    const tick=String(still)+Math.floor(time*12);if(tick===last)return;last=tick;
    for(const tile of tiles){kit.paint(tile.canvas,tile.preset,{now:time*1000});tile.texture.needsUpdate=true;}
    for(const batch of batches){
      if(batch.particles){const {particles,box}=batch;particles.visible=!still;for(let i=0;i<particles.count;i++){const phase=(i*.618+time*(.10+(i%5)*.008))%1;dummy.position.set(Math.sin(i*24.7)*box.width/2+Math.sin(time+i)*.035,box.y+phase*box.height,box.z+Math.cos(i*8)*.10);dummy.rotation.set(time+i,time*.7+i,0);dummy.scale.setScalar(.008+.018*Math.sin(phase*Math.PI));dummy.updateMatrix();particles.setMatrixAt(i,dummy.matrix);}particles.instanceMatrix.needsUpdate=true;}
      else{for(let i=0;i<batch.count;i++){const color=palette[i%4].clone().multiplyScalar(still?.8:.7+.3*(.5+.5*Math.sin(time*2.3-i*.38)));batch.setColorAt(i,color);}batch.instanceColor.needsUpdate=true;}
    }
  }
  update(0,false);
  return {update,dispose(){kit.dispose();const geos=new Set([ball,shard]),mats=new Set([bulbMat]);for(const root of roots){root.traverse(n=>{if(n.geometry)geos.add(n.geometry);if(n.material)mats.add(n.material);});root.removeFromParent();}geos.forEach(g=>g.dispose());mats.forEach(m=>m.dispose());tiles.forEach(t=>t.texture.dispose());glowTex.dispose();}};
}
