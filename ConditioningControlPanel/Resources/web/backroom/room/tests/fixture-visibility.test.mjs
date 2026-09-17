import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { visibleInTree, idleReelEligible } from '../fixture-visibility.js';

test('ancestor visibility and shared slot ownership are respected',()=>{
  const parent={visible:false,userData:{}},model={visible:true,userData:{},parent};
  assert.equal(visibleInTree(model),false);parent.visible=true;assert.equal(visibleInTree(model),true);
  parent.userData.slotPlaying=true;assert.equal(idleReelEligible(model),false);
  parent.userData.slotPlaying=false;parent.userData.slotHandlePulling=true;assert.equal(idleReelEligible(model),false);
});
test('actual room reel update skips unseen painting and catches up independent cabinet textures',()=>{
  const source=readFileSync(new URL('../fixtures.js',import.meta.url),'utf8');
  const start=source.indexOf('    if (!still) {\n      const repaint = clock - reelPaintAt');
  assert.ok(start>0);
  const update=source.slice(start,source.indexOf('    const changedBulbs',start));
  let paints=0,uploads=0,onCamera=false;
  const image={},map={image,offset:{x:0},set needsUpdate(v){if(v)uploads++;}},model={userData:{}},reel={material:{map},parent:model};
  const context=vm.createContext({still:false,clock:200,reelPaintAt:0,idleReels:[{model,reel,index:1}],dt:.033,
    visibleInTree,idleReelEligible,camera:{},reelFrustum:{intersectsObject:()=>onCamera},
    reelStrips:new Map([[1,image]]),reelVersions:new Map([[1,1]]),
    reelStrip(){paints++;context.reelVersions.set(1,context.reelVersions.get(1)+1);},
  });
  vm.runInContext(update,context);assert.equal(paints,0);assert.equal(uploads,0);assert.ok(map.offset.x>0);
  onCamera=true;context.clock=400;vm.runInContext(update,context);assert.equal(paints,1);assert.equal(uploads,1);
  context.clock=420;vm.runInContext(update,context);assert.equal(paints,1);assert.equal(uploads,1);
  model.userData.slotPlaying=true;context.clock=600;vm.runInContext(update,context);assert.equal(paints,1);
});
