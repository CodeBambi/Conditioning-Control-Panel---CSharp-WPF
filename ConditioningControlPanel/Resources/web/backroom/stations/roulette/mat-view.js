import * as T from 'three';

// A second camera, not a second renderer. The original mat and live chips are
// viewed from just above the ledge, below the cabinet canopy.
export function createMatView(stage, cells) {
  const camera=new T.OrthographicCamera(-1,1,1,-1,.001,3),ray=new T.Raycaster(),ndc=new T.Vector2();
  const bounds=new T.Box3(),p=new T.Vector3(),center=new T.Vector3(),up=new T.Vector3(),rotation=new T.Quaternion();
  const viewport=new T.Vector4(),scissor=new T.Vector4(),clear=new T.Color();
  let rect=null,active=false,lastBox='';
  function layout(){
    const box=stage.canvas.getBoundingClientRect();active=box.width<=800||(box.height<=500&&box.width>=box.height);
    if(!active){rect=null;lastBox='';return false;}
    const key=[box.width,box.height,box.left,box.top].join(',');if(key===lastBox)return true;lastBox=key;
    rect={x:box.left,y:box.top+box.height*2/3,w:box.width,h:box.height/3};
    bounds.makeEmpty();
    for(const cell of cells.values()){
      const {plane}=cell,g=plane.geometry.parameters;
      for(const x of [-g.width/2,g.width/2])for(const z of [-g.height/2,g.height/2])bounds.expandByPoint(p.copy(cell.position).add(new T.Vector3(x,0,z)));
    }
    bounds.getCenter(center);const width=bounds.max.x-bounds.min.x,depth=bounds.max.z-bounds.min.z;
    // Fill the lower view horizontally while keeping the actual mat proportions.
    const scale=Math.max((width+.02)/rect.w,(depth+.02)/rect.h);
    const height=Math.min(rect.h,(depth+.035)/scale);rect.y+=(rect.h-height)/2;rect.h=height;
    camera.left=-rect.w*scale/2;camera.right=-camera.left;camera.top=rect.h*scale/2;camera.bottom=-camera.top;
    stage.fixture.getWorldQuaternion(rotation);stage.fixture.getWorldScale(p);
    camera.left*=p.x;camera.right*=p.x;camera.top*=p.x;camera.bottom*=p.x;
    stage.fixture.localToWorld(center);up.set(0,1,0).applyQuaternion(rotation);
    camera.position.copy(center).addScaledVector(up,.18*p.y);camera.up.set(0,0,-1).applyQuaternion(rotation);camera.lookAt(center);
    camera.updateProjectionMatrix();camera.updateMatrixWorld();return true;
  }
  function draw(renderer){
    if(!layout())return;
    renderer.getViewport(viewport);renderer.getScissor(scissor);const tested=renderer.getScissorTest(),auto=renderer.autoClear,alpha=renderer.getClearAlpha();renderer.getClearColor(clear);
    try{
      const full=stage.canvas.getBoundingClientRect();renderer.setViewport(0,0,full.width,full.height/3);renderer.setScissor(0,0,full.width,full.height/3);renderer.setScissorTest(true);
      renderer.setClearColor(0x160e22,1);renderer.clear(true,true,false);renderer.autoClear=false;
      const bottom=full.height-(rect.y-full.top)-rect.h;renderer.setViewport(0,bottom,rect.w,rect.h);renderer.setScissor(0,bottom,rect.w,rect.h);renderer.render(stage.scene,camera);
    }finally{renderer.setClearColor(clear,alpha);renderer.autoClear=auto;renderer.setViewport(viewport);renderer.setScissor(scissor);renderer.setScissorTest(tested);}
  }
  function pick(event,targets){
    if(!layout())return undefined;
    if(stage.ready===false||event.clientX<rect.x||event.clientX>rect.x+rect.w||event.clientY<rect.y||event.clientY>rect.y+rect.h)return null;
    ndc.set((event.clientX-rect.x)/rect.w*2-1,1-(event.clientY-rect.y)/rect.h*2);ray.setFromCamera(ndc,camera);
    return ray.intersectObjects(targets,false)[0]?.object.userData.spot||null;
  }
  function project(position){if(!layout())return null;const v=stage.fixture.localToWorld(position.clone()).project(camera);return{x:rect.x+(v.x+1)*rect.w/2,y:rect.y+(1-v.y)*rect.h/2};}
  return {layout,draw,pick,project,get active(){return active;},debug:()=>({active,rect})};
}
