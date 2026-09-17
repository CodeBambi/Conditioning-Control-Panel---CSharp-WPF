import * as T from 'three';
import { createCustomizationPanel } from './customization-panel.js';
import { createSlotCustomHandles } from './slot-custom-handles.js';
import { createCustomizationScreens } from './customization-screens.js';
import { createCustomizationProps } from './customization-props.js';
import { createSpiralSamples } from './vending-spirals.js';
import { kit } from '../shared/sound/kit.js';

const PIECES=['knight','queen','rook'];

/** Local room previews. Ownership and SP are unchanged. */
export async function createCustomization({scene,loader,base,mount,lex,canvas,camera,isActive,room,onPreview=()=>{}}) {
  const row={key:'customization',id:'customization',name:'Room Service',labelKey:'br_custom_title',approach:[5.5,1.65,6.6],look:[7.05,1.4,6.7]};
  const root=new T.Group();root.name='room_customization';scene.add(root);
  const selected={statues:[0,1,2]};let floorEnabled=true;
  let owned=new Set();
  const propIds=['monstera','ivy','terrarium','gallery','portraits','billboard'];
  const load=async file=>(await loader.loadAsync(base+'customization/'+file+'.glb')).scene;
  const vending=await load('vending');vending.name='customization_vending';
  vending.position.set(7.05,.02,6.4);vending.rotation.y=-Math.PI/2;vending.scale.setScalar(1.08);root.add(vending);
  for(const name of ['header_title','header_subtitle','delivery_label']){const n=vending.getObjectByName(name);if(n)n.visible=false;}
  const titleCanvas=document.createElement('canvas');titleCanvas.width=1024;titleCanvas.height=128;
  const tx=titleCanvas.getContext('2d');tx.fillStyle='#180c25';tx.fillRect(0,0,1024,128);tx.fillStyle='#f4d29c';tx.font='600 82px "Segoe UI",system-ui,sans-serif';tx.textAlign='center';tx.textBaseline='middle';tx.fillText(lex('br_custom_title','Room Service'),512,64,970);
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
  const statueSpots=[[-4.1,.03,-6.85],[5.9,.03,5.35],[3.7,.03,7.25]].map((position,spot)=>sculptures.map((source,index)=>{
    const model=source.clone(true);root.add(model);
    model.name='statue_spot_'+spot+'_'+PIECES[index];model.rotation.y=spot===2?Math.PI:0;
    model.position.fromArray(position);model.visible=index===spot;return model;
  }));
  // The pull-for-fun cues, on the room's one kit: the lever's tap, the drums' roll, a thud per stop.
  let pulled=0;
  const cue=(name,index,reel=0)=>{
    if(name==='pull')pulled++;
    try{if(!kit.arm())return;if(name==='pull'){kit.play('tap');kit.play('ticks',{reel:0,ms:1100});}else kit.play('thud',{semis:(reel-1)*2});}catch{/* a cue never breaks a pull */}
  };
  const handles=await createSlotCustomHandles({holders:room.holders,loader,base,sources:sculptures,onCue:cue});
  const getState=()=>({screens:extras.getState(),props:props.getState(),statues:[...selected.statues],handles:handles.getState(),floor:floorEnabled?room.getFloorStyle().design:-1,palette:room.getFloorStyle().palette});
  const select=(category,index,target=0)=>{
    if(category==='screens')return extras.set(target,index);
    if(category==='props')return (!index || owned.has(propIds[target])) && props.set(target,index);
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
  // The lever close-up: one cabinet at a time, the camera at +x looking down -x, so screen-right is -z.
  const SLOTS=['slot:rose','slot:violet','slot:mint'];
  const slotView=target=>{const holder=room.holders.get(SLOTS[target]);if(!holder)return null;   // a cabinet whose glb carried no lever holder: nothing to fly the camera to
    const center=holder.getWorldPosition(new T.Vector3());return {position:[center.x+2.4,1.55,center.z+.35],look:[center.x,1.5,center.z],width:1.8,height:1.7};};
  const slotZ=i=>room.holders.get(SLOTS[i]).getWorldPosition(new T.Vector3()).z;
  const slotOrder=[0,1,2].filter(i=>room.holders.get(SLOTS[i])).sort((a,b)=>slotZ(b)-slotZ(a));
  // The pan between cabinets: a short eased travel, driven from update(); a snap when motion is off or reduced.
  let travel=null,shown=null,quiet=false;
  const mix=(a,b,k)=>a.map((v,i)=>v+(b[i]-v)*k);
  const snaps=()=>quiet||matchMedia('(prefers-reduced-motion: reduce)').matches;
  const preview=(category,index,target=0)=>{
    props.endPreview();
    if(category!=='handles'){travel=null;shown=null;}
    if(category==='room'){onPreview({position:[0,2.9,5.5],look:[0,1.7,-3]});return;}
    if(category==='screens'){onPreview(extras.preview(target));return;}
    if(category==='props'){const view=props.preview(target);if(view)onPreview(view);return;}
    if(category==='floor'||category==='palette'){onPreview({position:[0,4.2,5.5],look:[0,0,0]});return;}
    if(category==='handles'){
      const goal=slotView(target);if(!goal)return;
      if(shown&&!snaps()){travel={from:shown,to:goal,elapsed:0,duration:.45};return;}
      travel=null;shown=goal;onPreview(goal);return;
    }
    const object=statueSpots[target]?.[Math.max(0,index)];if(!object)return;
    const bounds=new T.Box3().setFromObject(object),center=bounds.getCenter(new T.Vector3()),size=bounds.getSize(new T.Vector3());
    const distance=Math.max(1.5,size.y*1.6,size.x*1.15);
    onPreview({position:[center.x,center.y+.12,center.z+Math.cos(object.rotation.y)*distance],look:center.toArray(),width:size.x,height:size.y});
  };
  const panel=createCustomizationPanel({mount,lex,vending,decorations:props.models,select,getState,restore,preview,slotOrder,hasOwnership:id=>owned.has(id),onClose:()=>{props.endPreview();travel=null;shown=null;onPreview(null);}});
  const ray=new T.Raycaster(),pointer=new T.Vector2();let down=null;
  // A finger on the room pane while the lever close-up is up: a horizontal swipe pans to the next cabinet
  // (swipe left, the way a carousel reads) or the previous one; the sheet keeps its own pointer events.
  const SWIPE=40;
  const onDown=e=>{if(e.button===0&&(isActive()||panel.slotArrows))down={x:e.clientX,y:e.clientY,t:performance.now()};};
  const onUp=e=>{
    if(!down)return;const start=down;down=null;
    if(panel.opened){const dx=e.clientX-start.x,dy=e.clientY-start.y,ms=performance.now()-start.t;
      if(!panel.slotArrows)return;
      if(Math.abs(dx)>=SWIPE&&Math.abs(dx)>Math.abs(dy)*1.5&&ms<900){panel.stepSlot(dx<0?1:-1);return;}
      // A tap on the close-up pulls that cabinet for fun: the handle swings, the drums roll, nothing is spent.
      if(Math.hypot(dx,dy)<=7&&ms<650)handles.pull(panel.slotTarget,{still:snaps()});
      return;}
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
  return {setOwned(ids){owned=new Set(ids);propIds.forEach((id,index)=>{if(!owned.has(id))props.set(index,false);});panel.refresh();},row,screens:[...extras.screens,...props.screens],select,getState,restore,preview,open:()=>panel.open(),get opened(){return panel.opened;},
    dismiss(){if(!panel.opened)return false;panel.close();return true;},
    update(dt,still){spirals.update(dt,still);panel.update?.(dt,still);handles.update(dt,still);quiet=!!still;
      if(travel){travel.elapsed+=dt;const t=still?1:Math.min(1,travel.elapsed/travel.duration),k=1-Math.pow(1-t,3);
        shown={...travel.to,position:mix(travel.from.position,travel.to.position,k),look:mix(travel.from.look,travel.to.look,k)};onPreview(shown);if(t>=1)travel=null;}},
    /** The close-up: a scissored pass on the room's own renderer, so it opens no second context. */
    draw(renderer){return panel.draw(renderer);},
    /** What the open panel leaves the room to draw into (viewport pixels, y up from the bottom). */
    previewBox(w,h){return panel.previewBox(w,h);},
    debug:()=>({selected:getState(),opened:panel.opened,models:9,props:props.debug(),view:panel.viewDebug(),pulls:{count:pulled,target:panel.slotTarget,active:[0,1,2].map(i=>handles.pulling(i))},arrows:{...panel.arrowsDebug(),order:slotOrder.slice(),travel:!!travel,view:shown,slots:[0,1,2].map(slotView)}}),
    dispose(){handles.dispose();props.dispose();extras.dispose();titleMap.dispose();panel.dispose();canvas.removeEventListener('pointerdown',onDown);canvas.removeEventListener('pointerup',onUp);root.removeFromParent();const gs=new Set(),ms=new Set();root.traverse(o=>{if(o.geometry)gs.add(o.geometry);for(const m of (Array.isArray(o.material)?o.material:[o.material]))if(m)ms.add(m);});gs.forEach(g=>g.dispose());ms.forEach(m=>m.dispose());}
  };
}
