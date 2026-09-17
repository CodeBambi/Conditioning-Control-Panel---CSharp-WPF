import { siliconeRebound, SILICONE_SETTLE_MS, customLeverReturn } from './lever-return.js';
import * as T from 'three';

const KINDS = ['knight', 'queen', 'rook'];
const VARIANTS = ['rose', 'violet', 'mint'];
// Session preview only. No inventory or purchases are represented by this state.
const styles = [-1, -1, -1];
// A pull for fun (Room Service): the cabinet's handle swings on its own axle and the drums roll to a
// dummy stop. No server call, no SP, no outcome - nothing reads where the reels land.
const PULL_MAX = 0.5, DOWN_S = 0.16, UP_S = 0.44, STOP_AT = [0.62, 0.86, 1.12], FACES = 13, ROLL = 1.9;
const ease = t => 1 - Math.pow(1 - Math.min(1, Math.max(0, t)), 3);
export const slotHandleStyle = variant => styles[VARIANTS.indexOf(variant)] ?? -1;

function sculpture(model, kind) {
  model.updateMatrixWorld(true);
  const group = new T.Group();
  model.traverse(node => {
    if (!node.isMesh || !node.name.startsWith(kind + '_original_')) return;
    const copy = node.clone(false);
    copy.geometry = node.geometry.clone();
    // GLB attributes may be normalized integers. Deform decoded floating-point coordinates.
    for (const name of ['position','normal']) {
      const attribute=copy.geometry.getAttribute(name);if(!attribute)continue;
      const values=new Float32Array(attribute.count*3);
      for(let i=0;i<attribute.count;i++){values[i*3]=attribute.getX(i);values[i*3+1]=attribute.getY(i);values[i*3+2]=attribute.getZ(i);}
      copy.geometry.setAttribute(name,new T.BufferAttribute(values,3));
    }
    copy.geometry.applyMatrix4(node.matrixWorld);
    copy.position.set(0,0,0); copy.quaternion.identity(); copy.scale.set(1,1,1);
    group.add(copy);
  });
  if (!group.children.length) throw new Error('Missing original chess sculpture: ' + kind);
  return group;
}

/** Replace the moving wand, with the sculpture base on the cabinet's real axle. */
function socket(rig) {
  const anchor = rig.getObjectByName('handle_socket');
  if (anchor) {
    const mount = new T.Group(); mount.name = 'chess_handle_socket';
    anchor.add(mount);
    // Widen the existing side axle to clear the cabinet with a full-size sculpture.
    const extension=new T.Mesh(new T.CylinderGeometry(.022,.022,.025,12),new T.MeshStandardMaterial({color:0xbd8b50,metalness:.7,roughness:.3}));
    extension.rotation.z=-Math.PI/2;extension.position.set(.0125,-.025,0);anchor.add(extension);
    mount.position.x=.025;
    let h=rig;while(h&&!h.userData.slotStretch)h=h.parent;
    if(h)mount.scale.set(1/(h.userData.slotStretchX||1),1/(h.userData.slotStretch||1),1);
    const originals=['wand_head','wand_grip','wand_switch'].map(n=>rig.getObjectByName(n)).filter(Boolean).map(n=>[n,n.visible]);
    return { mount, originals, height: .51, width: .24, depth: .27, restore(){extension.removeFromParent();extension.geometry.dispose();extension.material.dispose();} };
  }
  const lever = rig.getObjectByName('lever'), assembly = rig.getObjectByName('wand_assembly');
  if (!lever || !assembly) return roomSocket(rig);
  rig.updateMatrixWorld(true);
  const orientation = assembly.getWorldQuaternion(new T.Quaternion());
  const base = lever.getWorldPosition(new T.Vector3());
  const frame = new T.Matrix4().compose(base,orientation,new T.Vector3(1,1,1));
  const inverse = frame.clone().invert(), bounds = new T.Box3(), point = new T.Vector3(), originals = [];
  assembly.traverse(node => {
    if (!node.isMesh) return;
    originals.push([node,node.visible]);
    node.geometry.computeBoundingBox();
    const b = node.geometry.boundingBox;
    for (const x of [b.min.x,b.max.x]) for(const y of [b.min.y,b.max.y]) for(const z of [b.min.z,b.max.z])
      bounds.expandByPoint(point.set(x,y,z).applyMatrix4(node.matrixWorld).applyMatrix4(inverse));
  });
  const mount = new T.Group(); mount.name = 'chess_handle_socket';
  mount.matrix.copy(lever.matrixWorld).invert().multiply(frame);
  mount.matrix.decompose(mount.position,mount.quaternion,mount.scale); lever.add(mount);
  return { mount, originals, height: bounds.max.y };
}

// The room optimizer merged wand materials with cabinet parts. Mask only the
// protruding wand triangles in a private geometry, leaving the axle boss intact.
function roomSocket(rig) {
  const head=rig.getObjectByName('wand_head');
  if(!head) throw new Error('Slot has no wand');
  const parent=head.parent; rig.updateMatrixWorld(true);
  const frame=new T.Matrix4().compose(new T.Vector3(.65,.73,.12),new T.Quaternion().setFromEuler(new T.Euler(Math.PI/10,0,0)),new T.Vector3(1,1,1));
  const inverse=frame.clone().invert(), restored=[];
  const headBounds=new T.Box3(),headPoint=new T.Vector3(),headTransform=inverse.clone().multiply(parent.matrixWorld.clone().invert()).multiply(head.matrixWorld);
  const headPositions=head.geometry.getAttribute('position');
  for(let i=0;i<headPositions.count;i++)headBounds.expandByPoint(headPoint.fromBufferAttribute(headPositions,i).applyMatrix4(headTransform));
  parent.traverse(node=>{
    if(!node.isMesh)return;
    const geometry=node.geometry, position=geometry.getAttribute('position'), index=geometry.index;
    if(!position || !index)return;
    const transform=inverse.clone().multiply(parent.matrixWorld.clone().invert()).multiply(node.matrixWorld);
    const keep=[],point=new T.Vector3(),center=new T.Vector3(); let removed=0;
    for(let i=0;i<index.count;i+=3){
      center.set(0,0,0);
      for(let j=0;j<3;j++)center.add(point.fromBufferAttribute(position,index.getX(i+j)).applyMatrix4(transform));
      center.multiplyScalar(1/3);
      if(Math.abs(center.x)<.068 && center.y>.078 && center.y<.57 && Math.abs(center.z)<.08){removed++;continue;}
      keep.push(index.getX(i),index.getX(i+1),index.getX(i+2));
    }
    if(removed){const copy=geometry.clone();copy.setIndex(keep);node.geometry=copy;restored.push(()=>{node.geometry=geometry;copy.dispose();});}
  });
  const mount=new T.Group();mount.name='chess_handle_socket';frame.decompose(mount.position,mount.quaternion,mount.scale);parent.add(mount);
  return {mount,originals:[],height:headBounds.max.y,restore(){restored.forEach(fn=>fn());}};
}

export async function attachSlotCustomHandle({rig,loader,base,style,source=null}) {
  if (!Number.isInteger(style) || style < 0 || style > 2) return {dispose(){}};
  const model = source || (await loader.loadAsync(base + 'customization/' + KINDS[style] + '.glb')).scene;
  const handle = sculpture(model,KINDS[style]), slot = socket(rig);
  const box = new T.Box3().setFromObject(handle), size = box.getSize(new T.Vector3());
  // Keep the lower half fixed; smoothly increase flex toward the sculpture tip.
  const flexMeshes = handle.children.map(n=>({node:n,rest:n.geometry.attributes.position.array.slice()}));
  const hinge=box.min.y+size.y*.55, centerZ=(box.min.z+box.max.z)/2;
  let lastFlex=0;
  slot.mount.userData.flexTip = angle => {
    if(angle===lastFlex)return;lastFlex=angle;
    for(const {node,rest} of flexMeshes){
      const pos=node.geometry.attributes.position;
      for(let i=0;i<pos.count;i++){
        const x=rest[i*3], y=rest[i*3+1], z=rest[i*3+2];
        const u=Math.max(0,Math.min(1,(y-hinge)/(size.y*.45))), a=angle*u*u*(3-2*u);
        if(u===0 || angle===0){pos.setXYZ(i,x,y,z);continue;}
        const dy=y-hinge,dz=z-centerZ;
        pos.setXYZ(i,x,hinge+dy*Math.cos(a)-dz*Math.sin(a),centerZ+dy*Math.sin(a)+dz*Math.cos(a));
      }
      pos.needsUpdate=true;node.geometry.computeVertexNormals();
    }
  };
  const factor = Math.min(slot.height / size.y, (slot.width || Infinity) / size.x, (slot.depth || Infinity) / size.z);
  const wrapper = new T.Group(); wrapper.scale.setScalar(factor);
  handle.position.set(-(box.min.x+box.max.x)/2,-box.min.y,-(box.min.z+box.max.z)/2);
  wrapper.add(handle); slot.mount.add(wrapper);
  slot.originals.forEach(([node])=>node.visible=false);
  return {node:slot.mount,dispose(){slot.originals.forEach(([node,visible])=>node.visible=visible);slot.mount.removeFromParent();slot.restore?.();flexMeshes.forEach(({node})=>node.geometry.dispose());
    if(source)return; // Shared sculpture resources belong to the room catalogue.
    const geometries=new Set(),materials=new Set();model.traverse(n=>{if(n.geometry)geometries.add(n.geometry);[].concat(n.material||[]).forEach(m=>materials.add(m));});
    geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());}};
}

export async function createSlotCustomHandles({holders,loader,base,sources=[],onCue=()=>{}}) {
  const active = [null,null,null], revisions = [0,0,0], queues = [Promise.resolve(),Promise.resolve(),Promise.resolve()]; let disposed=false;
  function set(index,style) {
    if(disposed || !Number.isInteger(index) || index<0 || index>2 || !Number.isInteger(style) || style < -1 || style>2)return Promise.resolve(false);
    const rig=holders.get('slot:'+VARIANTS[index]); if(!rig)return Promise.resolve(false);
    const revision=++revisions[index]; styles[index]=style;
    queues[index]=queues[index].catch(()=>{}).then(async()=>{
      if(disposed || revision!==revisions[index])return false;
      active[index]?.dispose();active[index]=null;
      if(style<0)return true;
      let next;
      try {next=await attachSlotCustomHandle({rig,loader,base,style,source:sources[style]});}
      catch {if(revision===revisions[index])styles[index]=-1;return false;}
      if(disposed || revision!==revisions[index]){next.dispose();return false;}
      active[index]=next;return true;
    });
    return queues[index];
  }
  const pulls=[null,null,null], AXIS=new T.Vector3(1,0,0), swing=new T.Quaternion();
  const rigOf=index=>holders.get('slot:'+VARIANTS[index]);
  const reelsOf=rig=>[1,2,3].map(n=>rig.getObjectByName('reel_'+n)).filter(m=>m?.material?.map);
  const face=()=>(Math.floor(Math.random()*FACES)+.5)/FACES;
  /** Swing the handle on cabinet `index` and roll its drums. `still` (motion off) lands them at once. */
  function pull(index,{still=false}={}){
    if(disposed||!Number.isInteger(index)||index<0||index>2||pulls[index])return false;
    const rig=rigOf(index);if(!rig||rig.userData.slotPlaying)return false;
    const pivot=rig.getObjectByName('lever')||active[index]?.node||null;
    const reels=reelsOf(rig).map(mesh=>({map:mesh.material.map,to:face()}));
    if(!pivot&&!reels.length)return false;
    onCue('pull',index);
    if(still){reels.forEach((r,i)=>{r.map.offset.x=r.to;onCue('stop',index,i);});return true;}
    rig.userData.slotHandlePulling=true;
    pulls[index]={t:0,pivot,rest:pivot?pivot.quaternion.clone():null,reels,stopped:[false,false,false]};
    return true;}
  function rest(p){const flex=p.pivot?.getObjectByName('chess_handle_socket');if(flex){flex.userData.flexTip?.(0);};if(p.pivot&&p.rest)p.pivot.quaternion.copy(p.rest);}
  function update(dt=0,still=false){
    if(disposed)return;
    pulls.forEach((p,index)=>{
      if(!p)return;
      if(rigOf(index)?.userData.slotPlaying){rest(p);rigOf(index).userData.slotHandlePulling=false;pulls[index]=null;return;}
      p.t+=still?9:Math.min(Math.max(dt,0),.1);
      if(!p.returnCue&&p.t>=DOWN_S+UP_S){p.returnCue=true;if(active[index])onCue('silicone',index);}
      if(active[index]){active[index].node.userData.flexTip?.(still?0:siliconeRebound((p.t-DOWN_S-UP_S)*1000));}
      if(p.pivot)p.pivot.quaternion.copy(p.rest).multiply(swing.setFromAxisAngle(AXIS,
        p.t<DOWN_S?PULL_MAX*ease(p.t/DOWN_S):p.t<DOWN_S+UP_S?PULL_MAX*(1-ease((p.t-DOWN_S)/UP_S)):active[index]?customLeverReturn((p.t-DOWN_S-UP_S)*1000):0));
      p.reels.forEach((r,i)=>{
        if(p.t>=STOP_AT[i]){if(!p.stopped[i]){p.stopped[i]=true;r.map.offset.x=r.to;onCue('stop',index,i);}return;}
        r.map.offset.x=(r.map.offset.x+ROLL*Math.min(Math.max(dt,0),.1))%1;});
      if(p.t>=Math.max(DOWN_S+UP_S+(active[index]?SILICONE_SETTLE_MS/1000:0),STOP_AT[2])+.06){rest(p);rigOf(index).userData.slotHandlePulling=false;pulls[index]=null;}});}
  await Promise.all(styles.map((style,index)=>set(index,style)));
  return {set,pull,update,pulling:index=>!!pulls[index],getState:()=>styles.slice(),
    dispose(){disposed=true;pulls.forEach((p,i)=>{if(p){rest(p);pulls[i]=null;}});active.forEach(h=>h?.dispose());}};
}
