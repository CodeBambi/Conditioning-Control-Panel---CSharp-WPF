import * as T from 'three';
import { createMatView } from './mat-view.js';

// All positions, dimensions and chip sizes are authored extras on bet_hit_<spot>.
export function createMat3D({ stage, spots, label }) {
  const root=stage.fixture,group=new T.Group();group.name='roulette_runtime_mat';root.add(group);
  root.updateWorldMatrix(true,true);
  const cells=new Map(),targets=[],resources=[],labels=[];
  const hiddenMaterial=new T.MeshBasicMaterial({visible:false,side:T.DoubleSide});resources.push(hiddenMaterial);
  for(const spot of spots){
    const anchor=root.getObjectByName('bet_hit_'+spot),d=anchor?.userData;
    if(!anchor||d.spot!==spot||!(d.hit_width>0&&d.hit_depth>0))throw new Error('Roulette bet anchor missing: '+spot);
    const position=root.worldToLocal(anchor.getWorldPosition(new T.Vector3()));
    const scale=anchor.getWorldScale(new T.Vector3()).divide(root.getWorldScale(new T.Vector3()));
    const w=d.hit_width*scale.x,h=d.hit_depth*scale.z;
    const geometry=new T.PlaneGeometry(w,h),plane=new T.Mesh(geometry,hiddenMaterial);
    resources.push(geometry);plane.rotation.x=-Math.PI/2;plane.position.copy(position);plane.userData.spot=spot;group.add(plane);targets.push(plane);
    cells.set(spot,{plane,position,radius:d.chip_radius*scale.x});
    if(!/^s\d+$/.test(spot)){
      const canvas=document.createElement('canvas');canvas.width=512;canvas.height=128;
      const g=canvas.getContext('2d');g.fillStyle='#22152e';g.fillRect(0,0,512,128);g.fillStyle='#efd0e7';g.font='bold 48px Segoe UI';g.textAlign='center';g.textBaseline='middle';g.fillText(label(spot),256,64,500);
      const texture=new T.CanvasTexture(canvas);texture.colorSpace=T.SRGBColorSpace;
      const material=new T.MeshBasicMaterial({map:texture,side:T.DoubleSide});
      const print=new T.Mesh(geometry,material);print.rotation.copy(plane.rotation);print.position.copy(position).y-=.005;group.add(print);
      labels.push(canvas);resources.push(texture,material);
    }
  }
  const split=createMatView(stage,cells);split.layout();
  const radius=cells.values().next().value.radius;
  const chipGeometry=new T.CylinderGeometry(radius,radius,radius*.28,24);
  const chipMaterial=new T.MeshStandardMaterial({color:0xff71b5,metalness:.25,roughness:.4});const chipTop=new T.MeshStandardMaterial({color:0xe8c27a,metalness:.3,roughness:.5});resources.push(chipGeometry,chipMaterial,chipTop);
  const chipsMesh=new T.InstancedMesh(chipGeometry,[chipMaterial,chipTop,chipMaterial],12);chipsMesh.name='roulette_live_chips';chipsMesh.count=0;chipsMesh.frustumCulled=false;group.add(chipsMesh);
  const dummy=new T.Object3D(),end=root.worldToLocal(root.getObjectByName('roulette_rotor').getWorldPosition(new T.Vector3()));
  let disposed=false,anims=[],lastChips={};
  function pick(event){if(disposed)return null;const hit=split.pick(event,targets);return hit===undefined?(stage.pick(event,targets)[0]?.object.userData.spot||null):hit;}
  function rectOf(spot){
    const c=cells.get(spot);if(!c)return null;root.updateWorldMatrix(true,true);
    const projected=split.project(c.position);if(projected)return{x:projected.x-10,y:projected.y-10,w:20,h:20};
    const point=root.localToWorld(c.position.clone()).project(stage.camera),r=stage.canvas.getBoundingClientRect();
    return {x:(point.x+1)*r.width/2-10,y:(1-point.y)*r.height/2-10,w:20,h:20};
  }
  function draw(_,view){
    if(disposed)return;lastChips=view.chips;
    if(view.still)anims=[];
    let count=0;
    function put(position){if(count>=12)return;dummy.position.copy(position);dummy.updateMatrix();chipsMesh.setMatrixAt(count++,dummy.matrix);}
    for(const [spot,amount] of Object.entries(view.chips)){
      const cell=cells.get(spot);if(!cell)continue;
      for(let i=0;i<amount;i++)put(cell.position.clone().add(new T.Vector3(0,radius*(.15+i*.32),0)));
    }
    anims=anims.filter(a=>view.now-a.at<1100);
    for(const a of anims){
      const start=cells.get(a.spot)?.position;if(!start)continue;
      const t=Math.min(1,(view.now-a.at)/1100),angle=(1-t)*Math.PI*4;
      const destination=a.kind==='pull'?start:end;
      const origin=a.kind==='pull'?end:start;
      const p=origin.clone().lerp(destination,t),r=radius*5*Math.sin(Math.PI*t)*view.k;
      p.x+=Math.cos(angle)*r;p.z+=Math.sin(angle)*r;p.y+=Math.sin(Math.PI*t)*radius*4;put(p);
    }
    chipsMesh.count=count;chipsMesh.instanceMatrix.needsUpdate=true;
  }
  function dispose(){if(disposed)return;disposed=true;group.removeFromParent();for(const resource of resources)resource.dispose();for(const canvas of labels)canvas.width=canvas.height=1;anims=[];}
  return {drawSplit(renderer){if(!disposed)split.draw(renderer);},get split(){return split.active;},layout(){split.layout();},hit(){return null;},pick,rectOf,draw,dispose,animate(list,now){anims=list.map(a=>({...a,at:now}));},clearAnims(){anims=[];},
    debug(){return {view:'3d',split:split.debug(),rects:Object.fromEntries([...cells.keys()].map(k=>[k,rectOf(k)])),cells:cells.size,chips:{...lastChips},anims:anims.map(a=>a.kind+':'+a.spot),disposed};}};
}
