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

/** Attach at the head socket, retaining quantized mesh transforms and lever pivots. */
function socket(rig) {
  const head = rig.getObjectByName('wand_head');
  if (!head) throw new Error('Slot has no wand_head socket');
  rig.updateMatrixWorld(true);
  const orientation = head.getWorldQuaternion(new T.Quaternion());
  const frame = new T.Matrix4().makeRotationFromQuaternion(orientation);
  const inverse = frame.clone().invert(), bounds = new T.Box3(), point = new T.Vector3();
  head.geometry.computeBoundingBox();
  const b = head.geometry.boundingBox;
  for (const x of [b.min.x,b.max.x]) for(const y of [b.min.y,b.max.y]) for(const z of [b.min.z,b.max.z])
    bounds.expandByPoint(point.set(x,y,z).applyMatrix4(head.matrixWorld).applyMatrix4(inverse));
  const size = bounds.getSize(new T.Vector3());
  const base = new T.Vector3((bounds.min.x+bounds.max.x)/2,bounds.min.y+size.y*.08,(bounds.min.z+bounds.max.z)/2).applyMatrix4(frame);
  const mount = new T.Group(); mount.name = 'chess_handle_socket';
  mount.matrix.copy(head.parent.matrixWorld).invert().multiply(new T.Matrix4().compose(base,orientation,new T.Vector3(1,1,1)));
  mount.matrix.decompose(mount.position,mount.quaternion,mount.scale); head.parent.add(mount);
  const originals = ['wand_head','wand_switch'].map(name => rig.getObjectByName(name)).filter(Boolean).map(node=>[node,node.visible]);
  return { mount, originals, height: size.y * 1.5 };
}

export async function attachSlotCustomHandle({rig,loader,base,style}) {
  if (!Number.isInteger(style) || style < 0 || style > 2) return {dispose(){}};
  const model = (await loader.loadAsync(base + 'customization/' + KINDS[style] + '.glb')).scene;
  const handle = sculpture(model,KINDS[style]), slot = socket(rig);
  const box = new T.Box3().setFromObject(handle), size = box.getSize(new T.Vector3());
  const factor = slot.height / size.y;
  const wrapper = new T.Group(); wrapper.scale.setScalar(factor);
  handle.position.set(-(box.min.x+box.max.x)/2,-box.min.y,-(box.min.z+box.max.z)/2);
  wrapper.add(handle); slot.mount.add(wrapper);
  const ferrule = new T.Mesh(new T.CylinderGeometry(slot.height*.105,slot.height*.085,slot.height*.19,24),
    new T.MeshStandardMaterial({color:0xc49354,metalness:.65,roughness:.28}));
  ferrule.name='chess_handle_ferrule';ferrule.position.y=-slot.height*.045;slot.mount.add(ferrule);
  slot.originals.forEach(([node])=>node.visible=false);
  return {dispose(){slot.originals.forEach(([node,visible])=>node.visible=visible);slot.mount.removeFromParent();
    const geometries=new Set(),materials=new Set();model.traverse(n=>{if(n.geometry)geometries.add(n.geometry);[].concat(n.material||[]).forEach(m=>materials.add(m));});
    ferrule.geometry.dispose();ferrule.material.dispose();
    geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());}};
}

export async function createSlotCustomHandles({holders,loader,base}) {
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
      try {next=await attachSlotCustomHandle({rig,loader,base,style});}
      catch {if(revision===revisions[index])styles[index]=-1;return false;}
      if(disposed || revision!==revisions[index]){next.dispose();return false;}
      active[index]=next;return true;
    });
    return queues[index];
  }
  await Promise.all(styles.map((style,index)=>set(index,style)));
  return {set,getState:()=>styles.slice(),dispose(){disposed=true;active.forEach(h=>h?.dispose());}};
}
