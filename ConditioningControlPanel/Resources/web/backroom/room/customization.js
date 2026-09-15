import * as T from 'three';
import { createCustomizationPanel } from './customization-panel.js';
import { createSlotCustomHandles } from './slot-custom-handles.js';
import { createCustomizationScreens } from './customization-screens.js';
import { createCustomizationProps } from './customization-props.js';
import { createSpiralSamples } from './vending-spirals.js';

const PIECES=['knight','queen','rook'];

/** Local room previews. Ownership and SP are unchanged. */
export async function createCustomization({scene,loader,base,mount,lex,canvas,camera,isActive,room,onPreview=()=>{}}) {
  const row={key:'customization',id:'customization',name:'Room Service',labelKey:'br_custom_title',approach:[5.5,1.65,6.6],look:[7.05,1.4,6.7]};
  const root=new T.Group();root.name='room_customization';scene.add(root);
  const selected={statues:[0,1,2]};let floorEnabled=true;
  const load=async file=>(await loader.loadAsync(base+'customization/'+file+'.glb')).scene;
  const vending=await load('vending');vending.name='customization_vending';
  vending.position.set(7.05,.02,6.4);vending.rotation.y=-Math.PI/2;vending.scale.setScalar(1.08);root.add(vending);
  for(const name of ['header_title','header_subtitle','delivery_label']){const n=vending.getObjectByName(name);if(n)n.visible=false;}
  const titleCanvas=document.createElement('canvas');titleCanvas.width=1024;titleCanvas.height=128;
  const tx=titleCanvas.getContext('2d');tx.fillStyle='#180c25';tx.fillRect(0,0,1024,128);tx.fillStyle='#f4d29c';tx.font='600 82px Georgia';tx.textAlign='center';tx.textBaseline='middle';tx.fillText(lex('br_custom_title','Room Service'),512,64,970);
  const titleMap=new T.CanvasTexture(titleCanvas);titleMap.colorSpace=T.SRGBColorSpace;
  const title=new T.Mesh(new T.PlaneGeometry(1.16,.145),new T.MeshBasicMaterial({map:titleMap,toneMapped:false}));title.position.set(0,2.065,.438);vending.add(title);
  const extras=await createCustomizationScreens({scene,root,loader,base,room});
  const props=await createCustomizationProps({root,loader,base});
  const sculptures=await Promise.all(PIECES.map(load));
  const spirals=createSpiralSamples();
  const samples=[...extras.models,...sculptures,...spirals.models];
  samples.forEach((source,index)=>{
    const mini=source.clone(true);mini.name='vending_item_'+index;
    mini.position.set(0,0,0);mini.rotation.set(0,0,0);mini.visible=true;
    mini.updateMatrixWorld(true);
    const box=new T.Box3().setFromObject(mini),size=box.getSize(new T.Vector3());
    const scale=Math.min(.27/size.x,.27/size.y,.20/Math.max(.01,size.z));mini.scale.multiplyScalar(scale);
    mini.position.set(-(box.min.x+size.x/2)*scale,-box.min.y*scale,-(box.min.z+size.z/2)*scale);
    vending.getObjectByName('bay_'+String(index+1).padStart(2,'0'))?.add(mini);
  });
  const statueSpots=[[-4.1,.03,-6.85],[4.1,.03,-6.85],[3.7,.03,7.25]].map((position,spot)=>sculptures.map((source,index)=>{
    const model=source.clone(true);root.add(model);
    model.name='statue_spot_'+spot+'_'+PIECES[index];model.rotation.y=spot===2?Math.PI:0;
    model.position.fromArray(position);model.visible=index===spot;return model;
  }));
  const handles=await createSlotCustomHandles({holders:room.holders,loader,base,sources:sculptures});
  const getState=()=>({screens:extras.getState(),props:props.getState(),statues:[...selected.statues],handles:handles.getState(),floor:floorEnabled?room.getFloorStyle().design:-1,palette:room.getFloorStyle().palette});
  const select=(category,index,target=0)=>{
    if(category==='screens')return extras.set(target,index);
    if(category==='props')return props.set(target,index);
    if(!Number.isInteger(index))return false;
    if(category==='handles')return handles.set(target,index);
    if(category==='floor'||category==='palette'){
      const current=room.getFloorStyle();
      if(category==='floor'&&index===-1){floorEnabled=false;room.floor.visible=false;return true;}
      const ok=room.setFloorStyle(category==='floor'?index:current.design,category==='palette'?index:current.palette);
      if(ok&&category==='floor'){floorEnabled=true;room.floor.visible=true;}return ok;
    }
    if(category!=='statues'||!statueSpots[target]||index< -1||index>2)return false;
    selected.statues[target]=index;statueSpots[target].forEach((g,i)=>{
      g.visible=i===Math.max(0,index);
      g.traverse(o=>{if(o.isMesh&&o.name.includes('_original_'))o.visible=index!==-1;});
    });return true;
  };
  const restore=async state=>{
    state.screens.forEach((on,index)=>select('screens',on,index));
    state.props.forEach((on,index)=>select('props',on,index));
    select('floor',state.floor);select('palette',state.palette);
    await Promise.all(state.statues.map((piece,spot)=>{select('statues',piece,spot);return select('handles',state.handles[spot],spot);}));
  };
  const preview=(category,index,target=0)=>{
    if(category==='room'){onPreview({position:[0,2.9,5.5],look:[0,1.7,-3]});return;}
    if(category==='screens'){onPreview(extras.preview(target));return;}
    if(category==='props'){const view=props.preview(target);if(view)onPreview(view);return;}
    if(category==='floor'||category==='palette'){onPreview({position:[0,4.2,5.5],look:[0,0,0]});return;}
    if(category==='handles'){
      const holder=room.holders.get(['slot:rose','slot:violet','slot:mint'][target]);
      if(!holder)return;   // a cabinet whose glb carried no lever holder: nothing to fly the camera to
      const center=holder.getWorldPosition(new T.Vector3());
      onPreview({position:[center.x+2.4,1.55,center.z+.35],look:[center.x,1.5,center.z],width:1.8,height:1.7});return;
    }
    const object=statueSpots[target]?.[Math.max(0,index)];if(!object)return;
    const bounds=new T.Box3().setFromObject(object),center=bounds.getCenter(new T.Vector3()),size=bounds.getSize(new T.Vector3());
    const distance=Math.max(1.5,size.y*1.6,size.x*1.15);
    onPreview({position:[center.x,center.y+.12,center.z+Math.cos(object.rotation.y)*distance],look:center.toArray(),width:size.x,height:size.y});
  };
  const panel=createCustomizationPanel({mount,lex,vending,select,getState,restore,preview,onClose:()=>onPreview(null)});
  const ray=new T.Raycaster(),pointer=new T.Vector2();let down=null;
  const onDown=e=>{if(e.button===0&&isActive())down={x:e.clientX,y:e.clientY,t:performance.now()};};
  const onUp=e=>{
    if(!down)return;const start=down;down=null;
    if(!isActive()||Math.hypot(e.clientX-start.x,e.clientY-start.y)>7||performance.now()-start.t>650)return;
    const rect=canvas.getBoundingClientRect();pointer.set((e.clientX-rect.left)/rect.width*2-1,1-(e.clientY-rect.top)/rect.height*2);
    ray.setFromCamera(pointer,camera);
    const hit=ray.intersectObject(vending,true)[0];if(!hit||hit.distance>4)return;
    const obstructed=ray.intersectObjects(scene.children,true).some(h=>{
      if(h.distance>=hit.distance-.04)return false;
      for(let o=h.object;o;o=o.parent){if(!o.visible)return false;if(o===vending)return false;}
      return h.object.isMesh&&!h.object.material?.transparent;
    });
    if(!obstructed)panel.open();
  };
  canvas.addEventListener('pointerdown',onDown);canvas.addEventListener('pointerup',onUp);
  return {row,screens:[...extras.screens,...props.screens],select,getState,restore,preview,open:()=>panel.open(),get opened(){return panel.opened;},
    dismiss(){if(!panel.opened)return false;panel.close();return true;},
    update(dt,still){spirals.update(dt,still);panel.update?.(dt,still);},
    /** The close-up: a scissored pass on the room's own renderer, so it opens no second context. */
    draw(renderer){return panel.draw(renderer);},
    /** What the open panel leaves the room to draw into (viewport pixels, y up from the bottom). */
    previewBox(w,h){return panel.previewBox(w,h);},
    debug:()=>({selected:getState(),opened:panel.opened,models:9,props:props.debug(),view:panel.viewDebug()}),
    dispose(){handles.dispose();props.dispose();extras.dispose();titleMap.dispose();panel.dispose();canvas.removeEventListener('pointerdown',onDown);canvas.removeEventListener('pointerup',onUp);root.removeFromParent();const gs=new Set(),ms=new Set();root.traverse(o=>{if(o.geometry)gs.add(o.geometry);for(const m of (Array.isArray(o.material)?o.material:[o.material]))if(m)ms.add(m);});gs.forEach(g=>g.dispose());ms.forEach(m=>m.dispose());}
  };
}
