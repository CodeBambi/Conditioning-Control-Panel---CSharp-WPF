import * as T from 'three';

/** An independent camera on the real cabinet. Shared asset resources belong to the room. */
export function createVendingView({mount,vending,onSelect}) {
  const scene=new T.Scene();scene.background=new T.Color(0x100b20);
  const model=vending.clone(true);model.position.set(0,0,0);model.rotation.set(0,0,0);model.scale.setScalar(1);scene.add(model);
  scene.add(new T.HemisphereLight(0xe8d8ff,0x332044,2.6));
  const key=new T.DirectionalLight(0xffd3ee,3);key.position.set(2,4,5);scene.add(key);
  const fill=new T.DirectionalLight(0x63dfed,2);fill.position.set(-3,2,2);scene.add(fill);
  const renderer=new T.WebGLRenderer({antialias:true});renderer.setPixelRatio(Math.min(devicePixelRatio,1.5));renderer.outputColorSpace=T.SRGBColorSpace;
  mount.append(renderer.domElement);renderer.domElement.setAttribute('aria-hidden','true');
  const camera=new T.PerspectiveCamera(38,1,.01,30),look=new T.Vector3(0,1.18,0),goal=look.clone();
  const bays=Array.from({length:9},(_,i)=>model.getObjectByName('bay_'+String(i+1).padStart(2,'0')));
  const ray=new T.Raycaster(),pointer=new T.Vector2();let selected=-1,zoom=4,goalZoom=4,disposed=false;
  function focus(index){selected=index;model.updateMatrixWorld(true);if(index<0){goal.set(0,1.18,0);}else{const bay=bays[index];if(!bay)return;bay.getWorldPosition(goal);goal.y+=.13;goal.z=.35;}}
  function pick(event){const r=renderer.domElement.getBoundingClientRect();pointer.set((event.clientX-r.left)/r.width*2-1,-(event.clientY-r.top)/r.height*2+1);ray.setFromCamera(pointer,camera);const hits=ray.intersectObjects(bays.filter(Boolean),true);if(hits.length){let n=hits[0].object;while(n&&!bays.includes(n))n=n.parent;const i=bays.indexOf(n);if(i>=0)onSelect(i);}}
  renderer.domElement.addEventListener('click',pick);
  return {focus,update(dt,still){if(disposed)return;const w=mount.clientWidth,h=mount.clientHeight;if(!w||!h)return;
    if(renderer.domElement.width!==Math.round(w*renderer.getPixelRatio())||renderer.domElement.height!==Math.round(h*renderer.getPixelRatio()))renderer.setSize(w,h,false);
    camera.aspect=w/h;camera.updateProjectionMatrix();goalZoom=selected<0?Math.max(3.9,1.65/(2*Math.tan(T.MathUtils.degToRad(19))*camera.aspect)):Math.max(.8,.40/(2*Math.tan(T.MathUtils.degToRad(19))*camera.aspect));
    const a=still?1:1-Math.exp(-Math.min(dt,.1)*8);look.lerp(goal,a);zoom+=(goalZoom-zoom)*a;camera.position.copy(look).add(new T.Vector3(.04,.06,zoom));camera.lookAt(look);renderer.render(scene,camera);
  },dispose(){disposed=true;renderer.domElement.removeEventListener('click',pick);renderer.dispose();renderer.domElement.remove();}};
}
