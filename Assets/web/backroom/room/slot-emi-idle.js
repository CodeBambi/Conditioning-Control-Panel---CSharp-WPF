import { sampleEmiGesture, sampleEmiReaction, EMI_REACTIONS } from './emi-gestures.js';

/** Walking mascot owns only idle transforms. The seated slot takes exclusive ownership. */
export function createSlotEmiIdle({ fixture, onClick, phase = 0 }) {
  const root = fixture?.getObjectByName('emi_topper');
  if (!root) return null;
  const rest = root.rotation.clone();
  const arms = ['shoulderL', 'shoulderR'].map(name => {
    const node = root.getObjectByName(name);
    return node ? { node, rest: node.rotation.clone() } : null;
  });
  let time = phase, age = 0, action = null, disposed = false;
  function settle() {
    action = null; age = 0; root.rotation.copy(rest);
    for (const arm of arms) if (arm) arm.node.rotation.copy(arm.rest);
  }
  function trigger(kind = 'greet') {
    if (disposed) return false;
    if (fixture.userData.slotPlaying) { onClick?.(kind); return true; }
    action = EMI_REACTIONS[kind] ? kind : 'greet'; age = 0; return true;
  }
  function update(dt, still = false) {
    if (disposed || fixture.userData.slotPlaying) return;
    if (still) { settle(); return; }
    const step = Math.min(.05, Math.max(0, Number.isFinite(dt) ? dt : 0));
    time += step; age += step;
    if (action && age >= EMI_REACTIONS[action]) action = null;
    const pose = action ? sampleEmiReaction(action, age) : sampleEmiGesture('wheel', time);
    const blend = 1 - Math.exp(-step * 10);
    root.rotation.x += (rest.x + pose.pitch + .022*Math.sin(time*1.1) - root.rotation.x)*blend;
    root.rotation.y += (rest.y + pose.yaw + .07*Math.sin(time*.6) - root.rotation.y)*blend;
    root.rotation.z += (rest.z + pose.roll + .03*Math.sin(time*.9) - root.rotation.z)*blend;
    arms.forEach((arm,i) => {
      if (!arm) return;
      const z = arm.rest.z + (i ? 1 : -1)*(pose[i ? 'right' : 'left'] + .04*Math.sin(time));
      arm.node.rotation.z += (z - arm.node.rotation.z)*blend;
      arm.node.rotation.x += (arm.rest.x + pose.reach - arm.node.rotation.x)*blend;
    });
  }
  return { id: 'slot', fixture, interactionRoot: root, trigger, update, settle,
    dispose() { if(disposed)return; if(!fixture.userData.slotPlaying)settle(); disposed=true; } };
}
