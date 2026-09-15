import * as T from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
const cached=new WeakMap();
export const getRouletteSurfaces=root=>cached.get(root);
// The authored wheel glyphs fill under half of their number band (0.033 of 0.083 radially); grown in
// place they read from a seat and from a phone without touching the model. A glyph never leaves its band.
export const NUMBER_GLYPH_SCALE=1.9;
// Keep named authored nodes as anchors, while drawing compatible surfaces together.
export function createRouletteSurfaces(root){
  if(cached.has(root))return cached.get(root);
  const rotor=root.getObjectByName('roulette_rotor');if(!rotor)return null;
  root.updateWorldMatrix(true,true);
  const inverse=rotor.matrixWorld.clone().invert(),groups=new Map(),hidden=[],batches=[],numberColors=[],glyphCenter=new T.Vector3();
  for(const node of [...rotor.children]){
    const match=/^(pocket_|number_band_|number_)(\d+)$/.exec(node.name);
    if(!match||!node.isMesh||Array.isArray(node.material))continue;
    const isNumber=match[1]==='number_',n=Number(match[2]),key=node.material.uuid+(isNumber?':number':':surface');
    if(!groups.has(key))groups.set(key,{material:node.material,geometries:[],colors:[]});
    const group=groups.get(key),geometry=node.geometry.clone();geometry.applyMatrix4(inverse.clone().multiply(node.matrixWorld));
    if(isNumber){
      geometry.computeBoundingBox();geometry.boundingBox.getCenter(glyphCenter);
      geometry.translate(-glyphCenter.x,0,-glyphCenter.z);geometry.scale(NUMBER_GLYPH_SCALE,1,NUMBER_GLYPH_SCALE);geometry.translate(glyphCenter.x,0,glyphCenter.z);
      const color=new Float32Array(geometry.attributes.position.count*3);color.fill(1);geometry.setAttribute('color',new T.BufferAttribute(color,3));
      const p=rotor.worldToLocal(new T.Box3().setFromObject(node).getCenter(new T.Vector3()));
      group.colors.push({n,count:geometry.attributes.position.count,angle:Math.atan2(-p.z,p.x)});
    }
    group.geometries.push(geometry);hidden.push({node,visible:node.visible});node.visible=false;
  }
  for(const group of groups.values()){
    const geometry=mergeGeometries(group.geometries,false);if(!geometry)throw new Error('Roulette surfaces could not be batched');
    for(const g of group.geometries)g.dispose();
    const material=group.material.clone();material.vertexColors=group.colors.length>0;
    // The glyphs glow a little on their own: cream on rose under the canopy lights is not enough contrast at phone size.
    if(group.colors.length&&material.emissive){material.emissive=new T.Color('#fff1e4');material.emissiveIntensity=.42;}
    const mesh=new T.Mesh(geometry,material);mesh.name='roulette_runtime_surfaces';rotor.add(mesh);batches.push(mesh);
    let offset=0;for(const item of group.colors){numberColors.push({...item,offset,attribute:geometry.attributes.color});offset+=item.count;}
  }
  let disposed=false;
  const result={numberColors,reset(){for(const n of numberColors){for(let i=n.offset;i<n.offset+n.count;i++)n.attribute.setXYZ(i,1,1,1);n.attribute.needsUpdate=true;}},
    dispose(){if(disposed)return;disposed=true;cached.delete(root);for(const entry of hidden)entry.node.visible=entry.visible;for(const mesh of batches){mesh.removeFromParent();mesh.geometry.dispose();mesh.material.dispose();}}};
  cached.set(root,result);return result;
}
