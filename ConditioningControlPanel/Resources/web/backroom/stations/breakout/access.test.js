import {test} from 'node:test';
import assert from 'node:assert/strict';
import {breakoutAccess} from './access.js';
test('hosted game fails closed without a full entitlement projection',()=>{
  for(const value of [null,{}, {storyLimit:8}, {endless:true}, {storyLimit:8,endless:true,demo:true}])
    assert.deepEqual(breakoutAccess(value,{hosted:true}),{storyLimit:3,endless:false,demo:true});
});
test('full host projection grants eight Story levels and Endless',()=>{
  assert.deepEqual(breakoutAccess({storyLimit:8,endless:true,demo:false},{hosted:true}),{storyLimit:8,endless:true,demo:false});
});
test('local demo preview cannot open Endless and default preview remains full',()=>{
  assert.equal(breakoutAccess(null,{demo:true}).endless,false);
  assert.equal(breakoutAccess(null).endless,true);
});
