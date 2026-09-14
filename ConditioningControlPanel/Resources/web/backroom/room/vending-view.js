import * as T from 'three';

/* An independent camera on the real cabinet. Shared asset resources belong to the room.
 *
 * NO CONTEXT OF ITS OWN (CONTRACT 7 and 10.13.7): the close-up is a second scissored pass on the
 * ROOM's renderer, drawn into the panel stage's own rectangle just before the room draws its two
 * thirds. The budget stays where the contract put it: the room, the open station and the hypno
 * kit's Loom, at most three contexts alive and two drawing.
 */
export function createVendingView({mount,vending,onSelect}) {
  const scene=new T.Scene();scene.background=new T.Color(0x100b20);
  const model=vending.clone(true);model.position.set(0,0,0);model.rotation.set(0,0,0);model.scale.setScalar(1);scene.add(model);
  scene.add(new T.HemisphereLight(0xe8d8ff,0x332044,2.6));
  const key=new T.DirectionalLight(0xffd3ee,3);key.position.set(2,4,5);scene.add(key);
  const fill=new T.DirectionalLight(0x63dfed,2);fill.position.set(-3,2,2);scene.add(fill);
  const camera=new T.PerspectiveCamera(38,1,.01,30),look=new T.Vector3(0,1.18,0),goal=look.clone();
  const offset=new T.Vector3();
  const bays=Array.from({length:9},(_,i)=>model.getObjectByName('bay_'+String(i+1).padStart(2,'0')));
  const ray=new T.Raycaster(),pointer=new T.Vector2();let selected=-1,zoom=4,goalZoom=4,disposed=false;
  function focus(index){selected=index;model.updateMatrixWorld(true);if(index<0){goal.set(0,1.18,0);}else{const bay=bays[index];if(!bay)return;bay.getWorldPosition(goal);goal.y+=.13;goal.z=.35;}}
  function pick(event){if(event.target!==mount)return;   // the stage's own buttons are not the cabinet
    const r=mount.getBoundingClientRect();if(!r.width||!r.height)return;pointer.set((event.clientX-r.left)/r.width*2-1,-(event.clientY-r.top)/r.height*2+1);ray.setFromCamera(pointer,camera);const hits=ray.intersectObjects(bays.filter(Boolean),true);if(hits.length){let n=hits[0].object;while(n&&!bays.includes(n))n=n.parent;const i=bays.indexOf(n);if(i>=0)onSelect(i);}}
  mount.addEventListener('click',pick);
  /** The stage rectangle in the room canvas's own CSS pixels, y measured up from its bottom edge. */
  function rect(canvas){
    const c=canvas.getBoundingClientRect(),r=mount.getBoundingClientRect();
    const left=Math.max(r.left,c.left),right=Math.min(r.right,c.right);
    const top=Math.max(r.top,c.top),bottom=Math.min(r.bottom,c.bottom);
    const w=Math.floor(right-left),h=Math.floor(bottom-top);
    if(!(w>4&&h>4))return null;
    return {x:Math.round(left-c.left),y:Math.round(c.bottom-bottom),w,h};
  }
  return {focus,
    update(dt,still){if(disposed)return;const a=still?1:1-Math.exp(-Math.min(dt,.1)*8);look.lerp(goal,a);zoom+=(goalZoom-zoom)*a;},
    /** One scissored pass on the room's renderer; the room sets its own viewport straight after. */
    draw(renderer){
      if(disposed)return false;
      const box=rect(renderer.domElement);
      if(!box)return false;
      camera.aspect=box.w/box.h;camera.updateProjectionMatrix();
      goalZoom=selected<0?Math.max(3.9,1.65/(2*Math.tan(T.MathUtils.degToRad(19))*camera.aspect)):Math.max(.8,.40/(2*Math.tan(T.MathUtils.degToRad(19))*camera.aspect));
      camera.position.copy(look).add(offset.set(.04,.06,zoom));camera.lookAt(look);
      renderer.setViewport(box.x,box.y,box.w,box.h);renderer.setScissor(box.x,box.y,box.w,box.h);renderer.setScissorTest(true);
      renderer.render(scene,camera);
      renderer.setScissorTest(false);
      return true;
    },
    debug:()=>({selected,zoom:Math.round(zoom*1000)/1000}),
    dispose(){disposed=true;mount.removeEventListener('click',pick);}};
}
