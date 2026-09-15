import * as T from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

// All positions, dimensions and chip sizes are authored extras on bet_hit_<spot>. While seated the
// authored cream glyphs (bet_number_assembly) give way to one printed atlas: bold outlined digits and
// the outside labels, sized to their cells, that read from a seat and from a phone. The room camera
// reads userData.frame on this group ('mat' while bets are open, 'table' once the ball runs).
const ATLAS=1024, PX=1100, MIN_TAP=40;
export function createMat3D({ stage, spots, label }) {
  const root=stage.fixture,group=new T.Group();group.name='roulette_runtime_mat';group.userData.frame='mat';root.add(group);
  root.updateWorldMatrix(true,true);
  const cells=new Map(),targets=[],resources=[],slots=[];
  const hiddenMaterial=new T.MeshBasicMaterial({visible:false,side:T.DoubleSide});resources.push(hiddenMaterial);
  const glyphs=root.getObjectByName('bet_number_assembly'),glyphsVisible=glyphs?glyphs.visible:true;
  if(glyphs)glyphs.visible=false;
  for(const spot of spots){
    const anchor=root.getObjectByName('bet_hit_'+spot),d=anchor?.userData;
    if(!anchor||d.spot!==spot||!(d.hit_width>0&&d.hit_depth>0))throw new Error('Roulette bet anchor missing: '+spot);
    const position=root.worldToLocal(anchor.getWorldPosition(new T.Vector3()));
    const scale=anchor.getWorldScale(new T.Vector3()).divide(root.getWorldScale(new T.Vector3()));
    const w=d.hit_width*scale.x,h=d.hit_depth*scale.z;
    const geometry=new T.PlaneGeometry(w,h),plane=new T.Mesh(geometry,hiddenMaterial);
    resources.push(geometry);plane.rotation.x=-Math.PI/2;plane.position.copy(position);plane.userData.spot=spot;group.add(plane);targets.push(plane);
    cells.set(spot,{plane,position,w,h,radius:d.chip_radius*scale.x});
    slots.push({spot,w:Math.round(w*PX),h:Math.round(h*PX)});
  }
  // One atlas for every print, packed in shelves, each label at its cell's own proportions.
  const canvas=document.createElement('canvas');canvas.width=canvas.height=ATLAS;
  const g=canvas.getContext('2d');
  slots.sort((a,b)=>b.h-a.h);
  let x=0,y=0,shelf=0;
  for(const s of slots){
    if(x+s.w>ATLAS){x=0;y+=shelf;shelf=0;}
    if(y+s.h>ATLAS)throw new Error('Roulette mat atlas overflow');
    s.x=x;s.y=y;x+=s.w;shelf=Math.max(shelf,s.h);
  }
  const prints=[];
  for(const s of slots){
    const number=/^s\d+$/.test(s.spot),text=number?s.spot.slice(1):label(s.spot);
    g.save();g.beginPath();g.rect(s.x,s.y,s.w,s.h);g.clip();
    if(!number){g.fillStyle='#22152e';g.fillRect(s.x,s.y,s.w,s.h);}
    const size=number?Math.min(s.h*.8,s.w*.8/(text.length>1?1.15:.62)):Math.min(s.h*.62,s.w*1.5/Math.max(4,text.length));
    g.font='bold '+Math.round(size)+'px Segoe UI, Arial, sans-serif';g.textAlign='center';g.textBaseline='middle';
    if(number){g.lineJoin='round';g.lineWidth=Math.max(2,size*.17);g.strokeStyle='#1a0d1f';g.strokeText(text,s.x+s.w/2,s.y+s.h/2+size*.05,s.w);}
    g.fillStyle=number?'#fff6ee':'#efd0e7';g.fillText(text,s.x+s.w/2,s.y+s.h/2+size*.05,s.w);g.restore();
    const cell=cells.get(s.spot),print=new T.PlaneGeometry(cell.w,cell.h),uv=print.attributes.uv;
    const u0=s.x/ATLAS,u1=(s.x+s.w)/ATLAS,v0=1-(s.y+s.h)/ATLAS,v1=1-s.y/ATLAS;
    for(let i=0;i<uv.count;i++)uv.setXY(i,u0+uv.getX(i)*(u1-u0),v0+uv.getY(i)*(v1-v0));
    print.rotateX(-Math.PI/2);print.translate(cell.position.x,cell.position.y-.005,cell.position.z);prints.push(print);
  }
  const printGeometry=mergeGeometries(prints,false);for(const p of prints)p.dispose();
  const texture=new T.CanvasTexture(canvas);texture.colorSpace=T.SRGBColorSpace;texture.anisotropy=Math.min(8,stage.renderer?.capabilities.getMaxAnisotropy()||1);
  const printMaterial=new T.MeshBasicMaterial({map:texture,transparent:true,alphaTest:.05,depthWrite:false});
  const printMesh=new T.Mesh(printGeometry,printMaterial);printMesh.name='roulette_mat_prints';group.add(printMesh);resources.push(printGeometry,texture,printMaterial);
  const radius=cells.values().next().value.radius;
  const chipGeometry=new T.CylinderGeometry(radius,radius,radius*.28,24);
  const chipMaterial=new T.MeshStandardMaterial({color:0xff71b5,metalness:.25,roughness:.4});const chipTop=new T.MeshStandardMaterial({color:0xe8c27a,metalness:.3,roughness:.5});resources.push(chipGeometry,chipMaterial,chipTop);
  const chipsMesh=new T.InstancedMesh(chipGeometry,[chipMaterial,chipTop,chipMaterial],12);chipsMesh.name='roulette_live_chips';chipsMesh.count=0;chipsMesh.frustumCulled=false;group.add(chipsMesh);
  const dummy=new T.Object3D(),end=root.worldToLocal(root.getObjectByName('roulette_rotor').getWorldPosition(new T.Vector3())),corner=new T.Vector3();
  let disposed=false,anims=[],lastChips={};
  /** The cell's four corners through the room camera, as a viewport rect. */
  function rectOf(spot){
    const c=cells.get(spot);if(!c)return null;
    const r=stage.canvas.getBoundingClientRect();let x0=Infinity,y0=Infinity,x1=-Infinity,y1=-Infinity;
    for(const dx of [-c.w/2,c.w/2])for(const dz of [-c.h/2,c.h/2]){
      corner.set(c.position.x+dx,c.position.y,c.position.z+dz);root.localToWorld(corner).project(stage.camera);
      const px=r.left+(corner.x+1)*r.width/2,py=r.top+(1-corner.y)*r.height/2;
      x0=Math.min(x0,px);y0=Math.min(y0,py);x1=Math.max(x1,px);y1=Math.max(y1,py);
    }
    return {x:x0,y:y0,w:x1-x0,h:y1-y0};
  }
  /** The ray first; a cell narrower than a fingertip also picks from a 40 px pad around it, overlaps to the nearest centre. */
  function pick(event){
    if(disposed||stage.ready===false)return null;
    const hit=stage.pick(event,targets)[0]?.object.userData.spot;if(hit)return hit;
    let best=null,score=1;
    for(const spot of cells.keys()){
      const r=rectOf(spot),px=Math.max(0,(MIN_TAP-r.w)/2),py=Math.max(0,(MIN_TAP-r.h)/2);if(!px&&!py)continue;
      const d=Math.max(Math.abs(event.clientX-(r.x+r.w/2))/(r.w/2+px),Math.abs(event.clientY-(r.y+r.h/2))/(r.h/2+py));
      if(d<=1&&d<score){score=d;best=spot;}
    }
    return best;
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
  function dispose(){if(disposed)return;disposed=true;group.removeFromParent();for(const resource of resources)resource.dispose();canvas.width=canvas.height=1;if(glyphs)glyphs.visible=glyphsVisible;anims=[];}
  return {layout(){},hit(){return null;},pick,rectOf,draw,dispose,animate(list,now){anims=list.map(a=>({...a,at:now}));},clearAnims(){anims=[];},
    /** 'mat' while bets are open, 'table' once the ball runs: seat-camera.js frames a phone from it. */
    setFrame(frame){group.userData.frame=frame==='table'?'table':'mat';},
    get frame(){return group.userData.frame;},
    debug(){return {view:'3d',frame:group.userData.frame,atlas:slots.map(s=>({spot:s.spot,x:s.x,y:s.y,w:s.w,h:s.h})),glyphsHidden:!!glyphs&&!glyphs.visible,rects:Object.fromEntries([...cells.keys()].map(k=>[k,rectOf(k)])),cells:cells.size,chips:{...lastChips},anims:anims.map(a=>a.kind+':'+a.spot),disposed};}};
}
