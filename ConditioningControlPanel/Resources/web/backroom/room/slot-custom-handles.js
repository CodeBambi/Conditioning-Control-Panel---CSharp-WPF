import * as T from 'three';

const KINDS = ['knight', 'queen', 'rook'];
const VARIANTS = ['rose', 'violet', 'mint'];
// Session preview only. No inventory or purchases are represented by this state.
const styles = [-1, -1, -1];
export const slotHandleStyle = variant => styles[VARIANTS.indexOf(variant)] ?? -1;

function sculpture(model, kind) {
  model.updateMatrixWorld(true);
  const group = new T.Group();
  model.traverse(node => {
    if (!node.isMesh || !node.name.startsWith(kind + '_original_')) return;
    const copy = node.clone(false);
    copy.matrix.copy(node.matrixWorld); copy.matrix.decompose(copy.position, copy.quaternion, copy.scale);
    group.add(copy);
  });
  if (!group.children.length) throw new Error('Missing original chess sculpture: ' + kind);
  return group;
}

/** Replace the moving wand, with the sculpture base on the cabinet's real axle. */
function socket(rig) {
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
  const factor = slot.height / size.y;
  const wrapper = new T.Group(); wrapper.scale.setScalar(factor);
  handle.position.set(-(box.min.x+box.max.x)/2,-box.min.y,-(box.min.z+box.max.z)/2);
  wrapper.add(handle); slot.mount.add(wrapper);
  slot.originals.forEach(([node])=>node.visible=false);
  return {dispose(){slot.originals.forEach(([node,visible])=>node.visible=visible);slot.mount.removeFromParent();slot.restore?.();
    if(source)return; // Shared sculpture resources belong to the room catalogue.
    const geometries=new Set(),materials=new Set();model.traverse(n=>{if(n.geometry)geometries.add(n.geometry);[].concat(n.material||[]).forEach(m=>materials.add(m));});
    geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());}};
}

export async function createSlotCustomHandles({holders,loader,base,sources=[]}) {
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
  await Promise.all(styles.map((style,index)=>set(index,style)));
  return {set,getState:()=>styles.slice(),dispose(){disposed=true;active.forEach(h=>h?.dispose());}};
}
