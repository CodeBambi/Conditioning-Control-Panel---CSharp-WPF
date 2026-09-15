import * as T from 'three';
import { layoutOf } from '../stations/wheel/wheel.js';
import { createPrizeSector } from '../stations/wheel/prize-art.js';
import { sliceText } from '../stations/wheel/rewards.js';

/** The idle room and seated game use the same geometry, colours and printed prizes. */
export function createWheelFace(holder) {
  const rotor=holder.getObjectByName('wheel_rotor');if(!rotor)return null;
  const face=new T.Group();face.name='room_wheel_face';rotor.add(face);
  function clear(){
    const nodes=[];face.traverse(n=>{if(n!==face)nodes.push(n);});
    for(const n of nodes){n.geometry?.dispose();if(n.material){n.material.map?.dispose();n.material.dispose();}n.removeFromParent();}
  }
  function paint(slices,labels=s=>sliceText(s,(_k,f)=>f)){
    clear();const layout=layoutOf(slices);
    if(!layout){
      const neutral=new T.Mesh(new T.RingGeometry(.185,.711,96),new T.MeshStandardMaterial({color:0x67406f,metalness:.15,roughness:.3}));
      neutral.position.z=.077;face.add(neutral);return;
    }
    for(const s of layout){const {mesh,label,peg,border}=createPrizeSector(s,labels(s));face.add(mesh,label,peg,border);}
    face.userData.sliceCount=layout.length;
  }
  face.userData.setSlices=paint;paint(null);
  return {dispose(){clear();face.removeFromParent();}};
}
export function setWheelFace(scene,slices,labels){
  scene?.getObjectByName('room_wheel_face')?.userData.setSlices(slices,labels);
}
