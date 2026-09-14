import * as T from 'three';

/** Three local display previews, fed by the same personal-media renderer as the room. */
export async function createCustomizationScreens({scene,root,room}) {
  const screens=[],models=[],enabled=[false,false,false],groups=[];
  const geometries=new Set(),materials=new Set(),replaced=new Map();
  const brass=new T.MeshStandardMaterial({color:0xbd8b50,metalness:.7,roughness:.3});
  const casing=new T.MeshStandardMaterial({color:0x251334,metalness:.3,roughness:.4});
  materials.add(brass);materials.add(casing);
  function box(w,h,d,material,x=0,y=0,z=0) {
    const g=new T.BoxGeometry(w,h,d);geometries.add(g);
    const m=new T.Mesh(g,material);m.position.set(x,y,z);return m;
  }
  function display(w,h,name,feed=true) {
    const model=new T.Group();model.name=name;
    model.add(box(w+.12,h+.12,.09,brass),box(w+.07,h+.07,.10,casing,0,0,.014));
    const geometry=new T.PlaneGeometry(w,h);geometries.add(geometry);
    // GLB screens use top-origin UVs; match them for the shared GIF renderer.
    const uv=geometry.attributes.uv;for(let i=0;i<uv.count;i++)uv.setY(i,1-uv.getY(i));
    const material=new T.MeshBasicMaterial({color:0x5c327d,toneMapped:false});materials.add(material);
    const surface=new T.Mesh(geometry,material);surface.name='screen_surface_'+name;
    surface.position.z=.067;surface.userData.screenAspect=w/h;model.add(surface);
    if(feed)screens.push(surface);
    return model;
  }
  // Upper wall bands clear the existing attraction signs, screens and walking paths.
  const sites=[[-6.65,3.79,-6.35], [6.65,3.79,-6.35], [-6.65,3.79,4.3], [7.10,3.79,3.0],
    [-6.65,3.79,-4.3],[-6.65,3.79,0],[-6.65,3.79,6.2],
    [6.65,3.79,-3.9],[6.65,3.79,-.5],[7.535,3.79,5.9]];
  for(let pack=0;pack<2;pack++) {
    const group=new T.Group();group.name='extra_screens_'+(pack===0?4:6);root.add(group);groups.push(group);
    for(const [i,position] of sites.slice(pack===0?0:4,pack===0?4:10).entries()) {
      const model=display(1.28,.72,'pack_'+pack+'_'+i);model.position.fromArray(position);
      model.rotation.y=position[0]<0?Math.PI/2:-Math.PI/2;group.add(model);
    }
    group.visible=false;
    const mini=new T.Group();mini.name='sample_extra_screens_'+(pack===0?4:6);
    const count=pack===0?4:6,columns=pack===0?2:3;
    for(let i=0;i<count;i++) {const m=display(.32,.18,'sample_'+pack+'_'+i,false);m.position.set((i%columns-(columns-1)/2)*.38,Math.floor(i/columns)*.26,0);mini.add(m);}
    models.push(mini);
  }
  const mega=display(7.6,4.275,'ceiling_mega');mega.position.set(0,4.35,0);mega.rotation.x=Math.PI/2;
  // Child of the existing ceiling, so Room view hides it with the roof.
  const ceilingParent=room.ceiling||root;ceilingParent.updateWorldMatrix(true,false);
  root.add(mega);root.updateWorldMatrix(true,true);ceilingParent.attach(mega);mega.visible=false;groups.push(mega);
  const miniature=display(.74,.41625,'sample_ceiling',false);models.push(miniature);
  function ceilingDecor(on) {
    if(on) {
      const candidates=[];
      room.shell.traverse(o=>{if(/^(ceiling_(halo|medallion|star)|canopy_spoke)/.test(o.name))candidates.push(o);});
      const canopy=scene.getObjectByName('casino_decor_canopy');if(canopy)candidates.push(...canopy.children);
      for(const o of candidates){if(!replaced.has(o))replaced.set(o,o.visible);o.visible=false;}
    } else {for(const [o,visible] of replaced)o.visible=visible;replaced.clear();}
  }
  return {screens,models,
    set(index,on) {if(!Number.isInteger(index)||index<0||index>2||typeof on!=='boolean')return false;enabled[index]=on;groups[index].visible=on;if(index===2)ceilingDecor(on);return true;},
    getState:()=>[...enabled],
    preview(index) {return index===2?{position:[0,1.6,4.4],look:[0,4.35,0]}:{position:[0,2.4,packLook(index)],look:[-6.65,3.79,index===0?4.3:6.2]};},
    dispose() {ceilingDecor(false);groups.forEach(g=>g.removeFromParent());models.forEach(g=>g.removeFromParent());for(const m of screens)materials.add(m.material);geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());}
  };
}
function packLook(index){return index===0?4.3:6.2;}
