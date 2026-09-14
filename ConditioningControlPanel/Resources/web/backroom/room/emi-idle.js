import * as T from '../../vendor/three/three.module.min.js';
import { EMI_REACTIONS, sampleEmiGesture, sampleEmiReaction } from './emi-gestures.js';
const ROOTS = { counter: 'golden_emi_attendant', wheel: 'emi_topper', cards: 'emi_dealer', roulette: 'emi_dealer' };
const PHASES = { counter: 1.7, wheel: 4.3, cards: 8.1, roulette: 11.6 };

/** Add a neutral hinge above an authored node, retaining every quantized child transform. */
function hinge(node) {
  if (!node?.parent) return null;
  const parent = node.parent, slot = parent.children.indexOf(node), pivot = new T.Group(), offset = new T.Group();
  pivot.position.copy(node.position); offset.position.copy(node.position).multiplyScalar(-1);
  parent.add(pivot); pivot.add(offset); offset.add(node);
  return { pivot, reset() { pivot.rotation.set(0,0,0); }, dispose() {
    parent.add(node); parent.remove(pivot); parent.children.splice(parent.children.indexOf(node),1); parent.children.splice(slot,0,node);
  } };
}
function duster(shoulder) {
  if (!shoulder) return null;
  const group = new T.Group(); group.name = 'emi_feather_duster'; group.position.set(0,-.246,.003);
  const gold = new T.MeshStandardMaterial({color:0xd7af6e,metalness:.55,roughness:.4});
  const pink = new T.MeshStandardMaterial({color:0xe8add8,roughness:.85});
  const handle = new T.Mesh(new T.CylinderGeometry(.012,.017,.72,8),gold); handle.position.y=-.35;group.add(handle);
  const fluff = new T.Mesh(new T.SphereGeometry(.10,10,8),pink);fluff.position.y=-.75;fluff.scale.set(.8,1.65,.8);group.add(fluff);
  shoulder.add(group); group.visible=false; gold.transparent=pink.transparent=true; return { group, show(amount) {group.visible=amount>.01;gold.opacity=pink.opacity=amount;}, dispose() {group.removeFromParent();group.traverse(o=>{if(o.isMesh)o.geometry.dispose();});gold.dispose();pink.dispose();} };
}
export function createEmiIdle({ model, row, atlas }) {
  const root = model.getObjectByName(ROOTS[row.id] || '');
  if (!ROOTS[row.id] || !root?.parent) return null;
  const body = hinge(root), pivot = body.pivot; pivot.name = 'emi_idle_' + row.id;
  const left = hinge(root.getObjectByName('shoulderL')), right = hinge(root.getObjectByName('shoulderR')), antenna = hinge(root.getObjectByName('ant0'));
  const tool = row.id === 'counter' ? duster(root.getObjectByName('shoulderR')) : null;
  const shoulder = root.getObjectByName('shoulderR');
  const restAxis = shoulder ? new T.Vector3(0,-1,0).applyQuaternion(shoulder.quaternion) : null;
  const origin = new T.Vector3(), aim = new T.Vector3(), inverse = new T.Matrix4(), targetRotation = new T.Quaternion();
  const face = root.getObjectByName('EMI_glass'), originalMaterial = face?.material;
  const texture = atlas && face ? atlas.clone() : null;
  if (texture && originalMaterial) {
    texture.needsUpdate = true; face.material = originalMaterial.clone();
    face.material.map = texture; face.material.emissiveMap = texture; face.material.needsUpdate = true;
  }
  let elapsed = 0, disposed = false, faceIndex = 3, action = null, age = 0;
  const phase = PHASES[row.id];
  function expression(index) { faceIndex = index; if (texture) texture.offset.set((index*152+.5)/1672,.5/137); }
  function rest() { for(const h of [body,left,right,antenna])h?.reset(); tool?.show(0); expression(3); }
  function trigger(kind='greet') {
    if(disposed || !EMI_REACTIONS[kind] || (kind==='dust' && row.id!=='counter'))return false;
    // Let the current gesture settle instead of snapping between interrupted poses.
    if(action)return false; action=kind;age=0;return true;
  }
  function update(dt, still=false) {
    if(disposed)return;
    // Still takes the settled pose (Law VI) and never ages a gesture, so drop the one in hand
    // instead of parking it to replay whole when motion comes back.
    if(still){action=null;age=0;rest();return;}
    const step=Math.max(0,Math.min(Number.isFinite(dt)?dt:0,.05));elapsed+=step;
    if(action){age+=step;if(age>=EMI_REACTIONS[action]){action=null;age=0;}}
    const t=elapsed+phase, gesture=action?sampleEmiReaction(action,age):sampleEmiGesture(row.id,t);
    tool?.show(gesture.tool);
    const blend=1-Math.exp(-step*10), ease=(h,x,y,z)=>{if(h) {h.pivot.rotation.x+=(x-h.pivot.rotation.x)*blend;h.pivot.rotation.y+=(y-h.pivot.rotation.y)*blend;h.pivot.rotation.z+=(z-h.pivot.rotation.z)*blend;}};
    ease(body,.014*Math.sin(t*1.25)+gesture.pitch,.055*Math.sin(t*.43)+gesture.yaw,.018*Math.sin(t*.71)+gesture.roll);
    ease(left,gesture.reach,0,-gesture.left-.025*Math.sin(t*.85));
    ease(right,gesture.reach,gesture.sweep,gesture.right+.025*Math.sin(t*.85));
    if (tool && gesture.dust > 0) {
      model.updateWorldMatrix(true,true);
      right.pivot.getWorldPosition(origin);
      // Fixed-height arc: the feather head clears the tray throughout every stroke.
      const surface = model.localToWorld(new T.Vector3(0,.839,0)).y;
      const scale = shoulder.getWorldScale(new T.Vector3()).y, reach = .996 * scale;
      const dy = surface + .10 - origin.y, dx = -.20 + gesture.brushX;
      aim.set(dx,dy,Math.sqrt(Math.max(.01,reach*reach-dx*dx-dy*dy))).normalize();
      inverse.copy(right.pivot.parent.matrixWorld).invert(); aim.transformDirection(inverse);
      targetRotation.setFromUnitVectors(restAxis,aim);
      right.pivot.quaternion.slerp(targetRotation,gesture.dust);
    }
    if(antenna)antenna.pivot.rotation.z=.045*Math.sin(t*1.7)+gesture.roll*.4;
    expression(t%5.9<.14?2:(gesture.face??3));
  }
  rest();
  return { id:row.id, root, pivot, interactionRoot:pivot, update, trigger,
    debug:()=>({id:row.id,phase:elapsed,pose:[pivot.rotation.x,pivot.rotation.y,pivot.rotation.z],blink:faceIndex===2,action:action||'idle',age,articulated:!!(left&&right),arms:[left?.pivot.rotation.z||0,right?.pivot.rotation.z||0]}),
    dispose() {
      if(disposed)return;disposed=true;rest();tool?.dispose();
      if(texture&&originalMaterial){face.material.dispose();face.material=originalMaterial;texture.dispose();}
      for(const h of [left,right,antenna,body])h?.dispose();
    },
  };
}
