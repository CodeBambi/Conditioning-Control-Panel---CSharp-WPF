import * as T from 'three';
import { createCustomizationPanel } from './customization-panel.js';

const FILES={displays:['gallery-landscape','portrait-pair','deco-billboard'],plants:['monstera','hanging_ivy','terrarium'],statues:['knight','queen','rook']};
const ASPECT=[16/9,3/4,21/9];
const SPOTS={displays:[2.7,2.85,7.86],plants:[1.95,.03,7.2],statues:[3.6,.03,7.2]};

/** A local layout preview. No ownership or SP mutations occur here. */
export async function createCustomization({scene,loader,base,mount,lex,canvas,camera,isActive}) {
  const row={key:'customization',id:'customization',name:'Room Service',labelKey:'br_custom_title',approach:[5.5,1.65,6.6],look:[7.05,1.4,6.7]};
  const root=new T.Group();root.name='room_customization';scene.add(root);
  const screens=[],groups={},selected={displays:0,plants:0,statues:0},leaves=[];
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
  const select=(category,index)=>{
    if(!groups[category]||!Number.isInteger(index)||!groups[category][index])return false;
    selected[category]=index;groups[category].forEach((g,i)=>g.visible=i===index);
    hanger.visible=selected.plants===1;stand.visible=selected.plants===2;
    return true;
  };
  const panel=createCustomizationPanel({mount,lex,select,onClose:()=>{}});
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
  return {row,screens,select,open:()=>panel.open(),get opened(){return panel.opened;},
    dismiss(){if(!panel.opened)return false;panel.close();return true;},
    update(dt,still){if(!still)time+=dt;for(const {o,base,phase} of leaves){o.quaternion.copy(base);if(!still)o.rotateZ(Math.sin(time*.8+phase)*.018);}},
    debug:()=>({selected:{...selected},opened:panel.opened,models:Object.values(groups).reduce((n,g)=>n+g.length,0)}),
    dispose(){titleMap.dispose();panel.dispose();canvas.removeEventListener('pointerdown',onDown);canvas.removeEventListener('pointerup',onUp);root.removeFromParent();const gs=new Set(),ms=new Set();root.traverse(o=>{if(o.geometry)gs.add(o.geometry);for(const m of (Array.isArray(o.material)?o.material:[o.material]))if(m)ms.add(m);});gs.forEach(g=>g.dispose());ms.forEach(m=>m.dispose());}
  };
}
