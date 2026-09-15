import * as T from 'three';

/* An independent camera on the real cabinet. Shared asset resources belong to the room.
 *
 * NO CONTEXT OF ITS OWN (CONTRACT 7 and 10.13.7): the close-up is a second scissored pass on the
 * ROOM's renderer, drawn into the panel stage's own rectangle just before the room draws its two
 * thirds. The budget stays where the contract put it: the room, the open station and the hypno
 * kit's Loom, at most three contexts alive and two drawing.
 */
export function createVendingView({mount,vending,decorations=[],labels=[],onSelect,onDismount=()=>{}}) {
  const scene=new T.Scene();scene.background=new T.Color(0x100b20);
  const model=vending.clone(true);model.matrixAutoUpdate=true;model.position.set(0,0,0);model.rotation.set(0,0,0);model.scale.setScalar(1);scene.add(model);
  scene.add(new T.HemisphereLight(0xe8d8ff,0x332044,2.6));
  const key=new T.DirectionalLight(0xffd3ee,3);key.position.set(2,4,5);scene.add(key);
  const fill=new T.DirectionalLight(0x63dfed,2);fill.position.set(-3,2,2);scene.add(fill);
  const camera=new T.PerspectiveCamera(38,1,.01,30),look=new T.Vector3(0,1.18,0),goal=look.clone();
  const offset=new T.Vector3();
  const bays=Array.from({length:9},(_,i)=>model.getObjectByName('bay_'+String(i+1).padStart(2,'0')));
  const ray=new T.Raycaster(),pointer=new T.Vector2();let selected=-1,page=0,zoom=4,goalZoom=4,disposed=false;
  const own=[];
  const outline=new T.Box3Helper(new T.Box3(),0x78f4dc);outline.material.depthTest=false;outline.renderOrder=10;outline.visible=false;scene.add(outline);
  const propMinis=decorations.map((source,i)=>{
    const mini=source.clone(true);mini.name='vending_decoration_'+i;mini.matrixAutoUpdate=true;mini.position.set(0,0,0);mini.rotation.set(0,0,0);mini.scale.setScalar(1);mini.visible=true;
    const box=new T.Box3().setFromObject(mini),size=box.getSize(new T.Vector3()),scale=Math.min(.27/size.x,.27/size.y,.20/Math.max(.01,size.z));
    mini.scale.setScalar(scale);mini.position.set(-(box.min.x+size.x/2)*scale,-box.min.y*scale,-(box.min.z+size.z/2)*scale);mini.visible=false;bays[i]?.add(mini);return mini;
  });
  const buttons=bays.map((bay,i)=>{const b=mount.ownerDocument.createElement('button');b.className='br-vending-pick';b.type='button';b.onclick=()=>onSelect(page*9+i);b.oncontextmenu=e=>{e.preventDefault();onDismount(page*9+i);};mount.append(b);own.push(b);return b;});
  function setPage(next){page=next;selected=-1;outline.visible=false;
    bays.forEach((bay,i)=>{const original=bay?.getObjectByName('vending_item_'+i);if(original)original.visible=page===0;if(propMinis[i])propMinis[i].visible=page===1;buttons[i].hidden=page===1&&i>=propMinis.length;buttons[i].setAttribute('aria-label',labels[page*9+i]||'');buttons[i].setAttribute('aria-pressed','false');});
  }
  model.updateMatrixWorld(true);
  const bayBounds=bays.map(b=>b?new T.Box3().setFromObject(b):new T.Box3());
  const allBounds=new T.Box3();bayBounds.forEach(b=>allBounds.union(b));const span=allBounds.getSize(new T.Vector3());
  setPage(0);
  // Selection stays on the cabinet; the room pane carries the item preview.
  function focus(index){selected=index;outline.visible=index>=0;buttons.forEach((b,i)=>b.setAttribute('aria-pressed',String(page*9+i===index)));}
  function bayAt(event){if(event.target!==mount)return -1;   // the stage's own buttons are not the cabinet
    const r=mount.getBoundingClientRect();if(!r.width||!r.height)return -1;pointer.set((event.clientX-r.left)/r.width*2-1,-(event.clientY-r.top)/r.height*2+1);ray.setFromCamera(pointer,camera);const hits=ray.intersectObjects(bays.filter(Boolean),true);if(!hits.length)return -1;let n=hits[0].object;while(n&&!bays.includes(n))n=n.parent;const i=bays.indexOf(n);return i>=0&&(page===0||i<propMinis.length)?page*9+i:-1;}
  function pick(event){const i=bayAt(event);if(i>=0)onSelect(i);}
  /** Right-click on the cabinet: the piece in that bay comes off the room. No browser menu over the stage. */
  function menu(event){event.preventDefault();const i=bayAt(event);if(i>=0)onDismount(i);}
  mount.addEventListener('click',pick);mount.addEventListener('contextmenu',menu);
  /** The stage rectangle in the room canvas's own CSS pixels, y measured up from its bottom edge. */
  function rect(canvas){
    const c=canvas.getBoundingClientRect(),r=mount.getBoundingClientRect();
    const left=Math.max(r.left,c.left),right=Math.min(r.right,c.right);
    const top=Math.max(r.top,c.top),bottom=Math.min(r.bottom,c.bottom);
    const w=Math.floor(right-left),h=Math.floor(bottom-top);
    if(!(w>4&&h>4))return null;
    return {x:Math.round(left-c.left),y:Math.round(c.bottom-bottom),w,h};
  }
  return {focus,setPage,
    update(dt,still){if(disposed)return;const a=still?1:1-Math.exp(-Math.min(dt,.1)*8);look.lerp(goal,a);zoom+=(goalZoom-zoom)*a;},
    /** One scissored pass on the room's renderer; the room sets its own viewport straight after. */
    draw(renderer){
      if(disposed)return false;
      const box=rect(renderer.domElement);
      if(!box)return false;
      camera.aspect=box.w/box.h;camera.updateProjectionMatrix();
      allBounds.getCenter(goal);goal.z=.35;
      goalZoom=Math.max(span.y/(2*Math.tan(T.MathUtils.degToRad(19)))*1.16,span.x/(2*Math.tan(T.MathUtils.degToRad(19))*camera.aspect)*1.14);
      camera.position.copy(look).add(offset.set(.04,.06,zoom));camera.lookAt(look);camera.updateMatrixWorld();
      buttons.forEach((b,i)=>{if(!bays[i]||b.hidden)return;const bounds=bayBounds[i],points=[];for(const x of [bounds.min.x,bounds.max.x])for(const y of [bounds.min.y,bounds.max.y]){points.push(new T.Vector3(x,y,bounds.max.z).project(camera));}const xs=points.map(p=>(p.x+1)*box.w/2),ys=points.map(p=>(1-p.y)*box.h/2);Object.assign(b.style,{left:Math.min(...xs)+'px',top:Math.min(...ys)+'px',width:(Math.max(...xs)-Math.min(...xs))+'px',height:(Math.max(...ys)-Math.min(...ys))+'px'});});
      if(selected>=0){outline.box.copy(bayBounds[selected%9]);outline.box.expandByScalar(.015);}
      renderer.setViewport(box.x,box.y,box.w,box.h);renderer.setScissor(box.x,box.y,box.w,box.h);renderer.setScissorTest(true);
      renderer.render(scene,camera);
      renderer.setScissorTest(false);
      return true;
    },
    debug:()=>({selected,page,visibleItems:[...bays.map((b,i)=>b?.getObjectByName('vending_item_'+i)),...propMinis].filter(n=>n?.visible).map(n=>n.name),zoom:Math.round(zoom*1000)/1000,picks:buttons.filter(b=>!b.hidden).map((b)=>{const r=b.getBoundingClientRect();return {label:b.getAttribute('aria-label'),x:r.x+r.width/2,y:r.y+r.height/2,w:r.width,h:r.height};})}),
    dispose(){disposed=true;mount.removeEventListener('click',pick);mount.removeEventListener('contextmenu',menu);own.forEach(b=>b.remove());propMinis.forEach(m=>m.removeFromParent());outline.geometry.dispose();outline.material.dispose();}};
}
