import {test} from 'node:test';
import assert from 'node:assert/strict';
import {interactionKind,trackFlash,flashesBusy} from '../flash-interaction.js';
test('interaction showcase keeps shatter random and uses gesture speed for slide versus fling',()=>{
  assert.equal(interactionKind(0,0),'shatter');
  assert.equal(interactionKind(.32,2),'shatter');
  assert.equal(interactionKind(.5,0),'slide');
  assert.equal(interactionKind(.99,.49),'slide');
  assert.equal(interactionKind(.5,.5),'fling');
  assert.equal(interactionKind(.99,2),'fling');
});

test('roulette exit stays guarded through flash removal and the double-click window',()=>{
 const node={isConnected:true};trackFlash(node);
 assert.equal(flashesBusy(1000),true);
 node.isConnected=false;
 assert.equal(flashesBusy(1001),true);
 assert.equal(flashesBusy(1499),true);
 assert.equal(flashesBusy(1501),false);
});
