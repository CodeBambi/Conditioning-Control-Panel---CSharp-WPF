import * as T from 'three';
import { createCustomizationPanel } from './customization-panel.js';
import { createSlotCustomHandles } from './slot-custom-handles.js';

const FILES={displays:['gallery-landscape','portrait-pair','deco-billboard'],plants:['monstera','hanging_ivy','terrarium'],statues:['knight','queen','rook']};
const ASPECT=[16/9,3/4,21/9];
const SPOTS={displays:[2.7,2.85,7.86],plants:[1.95,.03,7.2],statues:[3.05,.03,7.25]};

/** A local layout preview. No ownership or SP mutations occur here. */
export async function createCustomization({scene,loader,base,mount,lex,canvas,camera,isActive,room,onPreview=()=>{}}) {
  const row={key:'customization',id:'customization',name:'Room Service',labelKey:'br_custom_title',approach:[5.5,1.65,6.6],look:[7.05,1.4,6.7]};
  const root=new T.Group();root.name='room_customization';scene.add(root);
  const screens=[],groups={},selected={displays:0,plants:0,statues:[0,1,2]},leaves=[];
  const load=async file=>(await loader.loadAsync(base+'customization/'+file+'.glb')).scene;
  const vending=await load('vending');vending.name='customization_vending';
  vending.position.set(7.05,.02,6.4);vending.rotation.y=-Math.PI/2;vending.scale.setScalar(.92);root.add(vending);
  for(const name of ['header_title','header_subtitle','delivery_label']){const n=vending.getObjectByName(name);if(n)n.visible=false;}
  const titleCanvas=document.createElement('canvas');titleCanvas.width=1024;titleCanvas.height=128;
  const tx=titleCanvas.getContext('2d');tx.fillStyle='#180c25';tx.fillRect(0,0,1024,128);tx.fillStyle='#f4d29c';tx.font='600 82px Georgia';tx.textAlign='center';tx.textBaseline='middle';tx.fillText(lex('br_custom_title','Room Service'),512,64,970);
  const titleMap=new T.CanvasTexture(titleCanvas);titleMap.colorSpace=T.SRGBColorSpace;
  const title=new T.Mesh(new T.PlaneGeometry(1.16,.145),new T.MeshBasicMaterial({map:titleMap,toneMapped:false}));title.position.set(0,2.065,.438);vending.add(title);
  for(const [category,files] of Object.entries(FILES)) {
    groups[category]=await Promise.all(files.map(async(file,index)=>{
      const model=await load(file);model.name='custom_'+file;model.position.fromArray(SPOTS[category]);
      model.rotation.y=Math.PI;
      if(category==='plants'&&index===1)model.position.set(1.4,2.85,7.2);
      root.add(model);
      model.traverse(o=>{
        if(o.isMesh&&o.name.startsWith('screen_surface')){o.userData.screenAspect=ASPECT[index];screens.push(o);}
        if(o.name.startsWith('foliage_sway'))leaves.push({o,base:o.quaternion.clone(),phase:leaves.length*.73});
      });
      // Miniatures in the vending bays show the same collection as the full-size spots.
      const mini=model.clone(true);mini.name='sample_'+file;mini.position.set(0,0,0);mini.rotation.set(0,0,0);
      const box=new T.Box3().setFromObject(mini),size=box.getSize(new T.Vector3());
      const scale=Math.min(.25/size.x,.27/size.y,.20/Math.max(.01,size.z));mini.scale.setScalar(scale);
      mini.position.set(-(box.min.x+size.x/2)*scale,-box.min.y*scale,-(box.min.z+size.z/2)*scale);
      const bay=vending.getObjectByName('bay_'+String(Object.keys(FILES).indexOf(category)*3+index+1).padStart(2,'0'));
      if(bay)bay.add(mini);
      model.visible=index===0;
      return model;
    }));
  }
  // The ivy hangs from a real ceiling attachment; the terrarium gets a display stand.
  const brass=new T.MeshStandardMaterial({color:0xb78650,metalness:.65,roughness:.35});
  const hanger=new T.Mesh(new T.CylinderGeometry(.012,.012,1.15,8),brass);hanger.position.set(1.4,4.02,7.2);root.add(hanger);hanger.visible=false;
  const stand=new T.Mesh(new T.CylinderGeometry(.25,.30,.66,24),new T.MeshStandardMaterial({color:0x40215e,metalness:.25,roughness:.4}));
  stand.position.set(1.95,.36,7.2);root.add(stand);stand.visible=false;
  groups.plants[2].position.y=.69;
  // Each pedestal is independent; the original three assets remain shared templates.
  const statueSpots=[3.05,4.3,5.55].map((x,spot)=>groups.statues.map((source,index)=>{
    const model=spot===0?source:source.clone(true);
    if(spot)root.add(model);
    model.name='statue_spot_'+spot+'_'+FILES.statues[index];
    model.position.set(x,.03,7.25);model.visible=index===spot;
    return model;
  }));
  const handles=await createSlotCustomHandles({holders:room.holders,loader,base});
  const getState=()=>({displays:selected.displays,plants:selected.plants,statues:[...selected.statues],handles:handles.getState(),floor:room.getFloorStyle().design,palette:room.getFloorStyle().palette});
  const select=(category,index,target=0)=>{
    if(!Number.isInteger(index))return false;
    if(category==='handles')return handles.set(target,index);
    if(category==='floor'||category==='palette'){
      const current=room.getFloorStyle();
      return room.setFloorStyle(category==='floor'?index:current.design,category==='palette'?index:current.palette);
    }
    if(category==='statues'){
      if(!statueSpots[target]||index< -1||index>2)return false;
      selected.statues[target]=index;statueSpots[target].forEach((g,i)=>{
        g.visible=i===Math.max(0,index);
        g.traverse(o=>{if(o.isMesh&&o.name.includes('_original_'))o.visible=index!==-1;});
      });return true;
    }
    if(!groups[category]?.[index])return false;
    selected[category]=index;groups[category].forEach((g,i)=>g.visible=i===index);
    hanger.visible=selected.plants===1;stand.visible=selected.plants===2;
    return true;
  };
  const restore=state=>{
    for(const category of ['displays','plants','floor','palette'])select(category,state[category]);
    for(let spot=0;spot<3;spot++){select('statues',state.statues[spot],spot);select('handles',state.handles[spot],spot);}
  };
  const preview=(category,index,target=0)=>{
    if(category==='floor'||category==='palette'){onPreview({position:[0,4.2,5.5],look:[0,0,0]});return;}
    if(category==='handles'){
      const holder=room.holders.get(['slot:rose','slot:violet','slot:mint'][target]);
      const center=holder.getWorldPosition(new T.Vector3());
      onPreview({position:[center.x+2.4,1.55,center.z+.35],look:[center.x,1.5,center.z]});return;
    }
    const object=category==='statues'?statueSpots[target][Math.max(0,index)]:groups[category]?.[index];
    if(!object)return;
    const bounds=new T.Box3().setFromObject(object),center=bounds.getCenter(new T.Vector3()),size=bounds.getSize(new T.Vector3());
    const distance=Math.max(1.5,size.y*1.6,size.x*1.15);
    onPreview({position:[center.x,center.y+.12,center.z-distance],look:center.toArray(),width:size.x,height:size.y});
  };
  const panel=createCustomizationPanel({mount,lex,select,getState,restore,preview,onClose:()=>onPreview(null)});
  const ray=new T.Raycaster(),pointer=new T.Vector2();let down=null,time=0;
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
  return {row,screens,select,getState,restore,preview,open:()=>panel.open(),get opened(){return panel.opened;},
    dismiss(){if(!panel.opened)return false;panel.close();return true;},
    update(dt,still){if(!still)time+=dt;for(const {o,base,phase} of leaves){o.quaternion.copy(base);if(!still)o.rotateZ(Math.sin(time*.8+phase)*.018);}},
    debug:()=>({selected:getState(),opened:panel.opened,models:Object.values(groups).reduce((n,g)=>n+g.length,0)}),
    dispose(){handles.dispose();titleMap.dispose();panel.dispose();canvas.removeEventListener('pointerdown',onDown);canvas.removeEventListener('pointerup',onUp);root.removeFromParent();const gs=new Set(),ms=new Set();root.traverse(o=>{if(o.geometry)gs.add(o.geometry);for(const m of (Array.isArray(o.material)?o.material:[o.material]))if(m)ms.add(m);});gs.forEach(g=>g.dispose());ms.forEach(m=>m.dispose());}
  };
}
