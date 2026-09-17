import test from 'node:test';
import assert from 'node:assert/strict';
import {createScreenAnimationBudget} from '../screen-animation-budget.js';
test('Performance gives four visible pictures six batches per second without starving the others',()=>{
 const counts=Array(8).fill(0),sources=counts.map((_,i)=>({tick(){counts[i]++;return true;}})),tick=createScreenAnimationBudget();
 assert.equal(tick(sources,0,false,true),4);assert.deepEqual(counts,[1,1,1,1,0,0,0,0]);
 assert.equal(tick(sources,100,false,true),0);assert.equal(tick(sources,167,false,true),4);
 assert.deepEqual(counts,Array(8).fill(1));
});
test('Full preserves per-render batching and skips sources not ready',()=>{
 const seen=[],sources=[{tick(){return false;}},...Array.from({length:4},(_,i)=>({tick(){seen.push(i);return true;}}))],tick=createScreenAnimationBudget();
 assert.equal(tick(sources,0,false,false),4);assert.equal(tick(sources,17,false,false),4);assert.equal(seen.length,8);
});
