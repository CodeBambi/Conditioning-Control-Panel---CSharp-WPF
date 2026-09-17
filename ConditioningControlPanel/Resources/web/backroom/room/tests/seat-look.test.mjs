import test from 'node:test';
import assert from 'node:assert/strict';
import { createSeatLook } from '../seat-look.js';
test('seated look respects surface ownership, clamps travel, resets when disabled and removes listeners',()=>{
  const listeners=new Map(), captures=new Set();
  const surface={addEventListener:(n,f)=>listeners.set(n,f),removeEventListener:n=>listeners.delete(n)};
  globalThis.window={addEventListener(){},removeEventListener(){}};
  const button={style:{},addEventListener(n,f){this[n]=f;},remove(){this.removed=true;}};
  globalThis.document={...window,createElement:()=>button};
  let view,enabled=true;
  const stage={ready:true,camera:{rotation:{x:0,y:0}},canvas:{...surface,getBoundingClientRect:()=>({width:1000,height:700}),
    setPointerCapture:id=>captures.add(id),hasPointerCapture:id=>captures.has(id),releasePointerCapture:id=>captures.delete(id)},
    register(v){view=v;return ()=>v.dispose();}};
  const close=createSeatLook(stage,{mount:{append(){}},enabled:()=>enabled,surface:e=>e.felt});
  const event={pointerId:1,button:0,clientX:0,clientY:0,preventDefault(){}};
  listeners.get('pointerdown')(event);assert.equal(captures.size,0,'game objects keep their gesture');
  listeners.get('pointerdown')({...event,felt:true});
  listeners.get('pointermove')({...event,clientX:10000,clientY:10000});view.update(1);
  assert.ok(Math.abs(stage.camera.rotation.y)<=.075 && Math.abs(stage.camera.rotation.y)>.07);
  assert.ok(Math.abs(stage.camera.rotation.x)<=.045);assert.equal(button.hidden,false);
  enabled=false;stage.camera.rotation={x:0,y:0};view.update(1);
  assert.deepEqual(stage.camera.rotation,{x:0,y:0});assert.equal(captures.size,0);assert.equal(button.hidden,true);
  close();assert.equal(listeners.size,0);assert.equal(button.removed,true);
});
