import * as T from 'three';
const MODELS={monstera:'prop_monstera',ivy:'prop_hanging_ivy',terrarium:'prop_terrarium',gallery:'prop_gallery_landscape',portraits:'prop_portrait_pair',billboard:'prop_deco_billboard'};
/** A camera-facing reward on the room renderer. Decoration clones borrow every source resource. */
export function createRoomReward(stage, loom) {
  const group=new T.Group();group.name='wheel_reward';group.visible=false;stage.scene.add(group);
  const owned=new Set();let started=0,still=false,kind=null,lid=null,eyes=[];
  const material=o=>{const m=new T.MeshBasicMaterial({depthTest:false,depthWrite:false,...o});owned.add(m);return m;};
  function mesh(geometry,mat){owned.add(geometry);const n=new T.Mesh(geometry,mat);n.renderOrder=1000;group.add(n);return n;}
  function clear(){group.clear();for(const x of owned)x.dispose();owned.clear();eyes=[];lid=null;group.visible=false;}
  function reveal(result,reduced=false){
    clear();const reward=result?.reward;if(!reward)return false;kind=reward.kind;still=reduced;started=performance.now();
    if(kind==='nothing'){if(!still)stage.emi?.trigger('look');return true;}
    if(kind==='decoration'&&!reward.fallback){
      const source=stage.scene.getObjectByName(MODELS[reward.decorationId]);if(!source)return false;
      const gift=source.clone(true);gift.position.set(0,0,0);gift.rotation.set(0,0,0);gift.scale.setScalar(1);gift.visible=true;
      const box=new T.Box3().setFromObject(gift),size=box.getSize(new T.Vector3()),center=box.getCenter(new T.Vector3());
      const scale=.20/Math.max(size.x,size.y,size.z);gift.scale.setScalar(scale);gift.position.copy(center).multiplyScalar(-scale);gift.position.y+=.018;
      gift.traverse(n=>{n.renderOrder=1001;if(n.material){n.material=Array.isArray(n.material)?n.material.map(copy):copy(n.material);}});
      function copy(m){const c=m.clone();c.depthTest=false;c.depthWrite=false;owned.add(c);return c;}
      group.add(gift);
      const base=mesh(new T.CylinderGeometry(.15,.16,.025,32),material({color:0xd6ae69}));base.position.y=-.10;
      lid=mesh(new T.SphereGeometry(.155,32,16,0,Math.PI*2,0,Math.PI/2),material({color:0xffeddc,transparent:true,opacity:.22,side:T.DoubleSide}));lid.position.y=-.085;lid.scale.y=1.6;lid.renderOrder=1002;
    } else if(kind==='double') {
      const canvas=loom();if(!canvas)return false;const tex=new T.CanvasTexture(canvas);owned.add(tex);
      for(const x of [-.065,.065]){const eye=mesh(new T.CircleGeometry(.064,40),material({map:tex,transparent:true}));eye.position.set(x,0,0);eyes.push(eye);}
    } else return false;
    group.visible=true;update(started);return true;
  }
  function update(now){if(!group.visible)return;const age=(now-started)/1000;
    group.position.copy(stage.camera.position).add(new T.Vector3(0,-.025,-.7).applyQuaternion(stage.camera.quaternion));group.quaternion.copy(stage.camera.quaternion);
    const height=2*.7*Math.tan(stage.camera.fov*Math.PI/360),width=height*stage.camera.aspect;group.scale.setScalar(Math.min(width/.65,height/.8));
    if(lid)lid.position.y=-.085+.24*(still?1:Math.min(1,age/.62));
    eyes.forEach((eye,i)=>{eye.position.x=(i?1:-1)*(.065+(still?0:.035*Math.max(0,1-age/.62)));});
    if(age>4)group.visible=false;
  }
  return {reveal,update,skip(){group.visible=false;},dispose(){clear();group.removeFromParent();}};
}
