import * as T from 'three';
import { createFloorStyle } from './floor-style.js';

// Small framed kinetic floor samples, built from the same shader as the room.
export function createSpiralSamples() {
  const styles=[];
  const models=[0,1,2].map(index=>{
    const root=new T.Group();root.name='spiral_sample_'+index;
    const brass=new T.MeshStandardMaterial({color:0xc39556,metalness:.7,roughness:.28});
    const body=new T.Mesh(new T.CylinderGeometry(.14,.145,.035,48),brass);
    body.rotation.x=Math.PI/2;body.position.set(0,.155,0);root.add(body);
    const style=createFloorStyle();style.setFloorStyle(index,0);styles.push(style);
    const face=new T.Mesh(new T.CircleGeometry(7,64),style.material);
    face.scale.setScalar(.0185);face.position.set(0,.155,.02);root.add(face);
    const foot=new T.Mesh(new T.BoxGeometry(.18,.025,.09),brass);foot.position.y=.0125;root.add(foot);
    const stem=new T.Mesh(new T.BoxGeometry(.026,.06,.025),brass);stem.position.y=.05;root.add(stem);
    return root;
  });
  return {models,update(dt,still){styles.forEach(style=>style.update(dt,still));}};
}
